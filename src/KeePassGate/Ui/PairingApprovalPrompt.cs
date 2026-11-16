using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Puts a pairing request or a renewal on the screen and waits for the answer, from a
    /// connection thread.
    /// </summary>
    /// <remarks>
    /// The marshal is synchronous, as every write's is. What is exceptional here is the dialog:
    /// `DESIGN.md` §119 lets no other request put a window in front of the operator. What keeps
    /// the wait from becoming a deadlock: the wait is bounded, the dialog closes itself, and a
    /// request that gets no answer fails rather than holds on.
    ///
    /// **One object for both, because they wait their turn for the same screen.** A renewal raised
    /// while a pairing is up would open inside its modal loop and gray it out, exactly as a second
    /// pairing would, so the two share one lock rather than each having one.
    ///
    /// The grace is why the two timeouts are not the same number. The dialog is told to close after
    /// the approval timeout, and this waits a little longer than that, so an unanswered dialog ends
    /// as a timeout the operator's own screen produced rather than as a connection thread walking
    /// away from a window that is still open.
    ///
    /// **A lock takes the question away.** KeePass locks under these dialogs, which hold no lock
    /// off, and a dialog left standing over a locked database offered an Approve that could do
    /// nothing. `PAIRING.md` §308: the lock closes the open dialog as unanswered, a request still
    /// waiting for the screen is never shown, and both are answered `503 locked`. **So does a
    /// close of the database the dialog is about**, which is answered as the lock is.
    ///
    /// **A dialog taken away is written down here, as it goes**, on KeePass's thread, where the
    /// line is queued for the audit's own writer. KeePass's exit closes every database inside the
    /// same press that unloads the plugin, and the unload waits for the lines queued by then: the
    /// answer reaches the connection thread only once that press is over, and a line written there
    /// raced the end of the process and was usually lost.
    /// </remarks>
    public sealed class PairingApprovalPrompt : IPairingApproval, IRenewalApproval, IApprovalScreen
    {
        /// <summary>How much longer than the dialog this waits before giving up on it.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The least a dialog is shown for. A request left less of its pairing budget than this
        /// when its dialog would come up is answered as a request that never got the screen is.
        /// </summary>
        /// <remarks>
        /// Enough to read the request once and press a button, which a dialog that flashed up and
        /// closed again is not. And under what a dialog has left when it comes up as late as the
        /// waits written down here allow, two minutes for the screen and the dialog's own time and
        /// the grace for its turn, so that this never turns away a request those waits alone
        /// delayed: `GateOptionsTests` pins that relation, and `DESIGN.md` §119 has the
        /// arithmetic.
        /// </remarks>
        public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(30);

        private readonly IUiThread _ui;
        private readonly IGateLog _log;

        /// <summary>
        /// One request on screen at a time, a pairing or a renewal.
        /// </summary>
        /// <remarks>
        /// Two connections can be pairing at once, and each marshals a `ShowDialog` onto the same
        /// thread. The second would open inside the first one's modal loop, and a modal dialog
        /// disables every other window on its thread: the first dialog goes gray, the operator
        /// cannot answer the request they are looking at, and it times out in front of them.
        ///
        /// Taken on the connection thread, and the user interface thread never asks for it, so
        /// there is no order in which this and the marshal can deadlock.
        /// </remarks>
        private readonly object _screen = new object();

        /// <summary>Whether a database is served unlocked, by its identifier.</summary>
        private readonly Func<string, bool> _serving;

        /// <summary>The plugin's writes, or null where there are none to wait for.</summary>
        private readonly IWriterState _writes;

        /// <summary>Where a dialog taken away is written down, or null where nothing is.</summary>
        private readonly AdmissionAudit _audit;

        /// <summary>KeePass's main window, held disabled under the dialog, or null.</summary>
        private readonly Control _mainWindow;

        /// <summary>
        /// The dialog on the screen until it closes, or a lock or a close of its database withdraws
        /// it, touched only on KeePass's thread.
        /// </summary>
        private ApprovalDialog _showing;

        /// <summary>The database the dialog on the screen is about, beside it.</summary>
        private string _showingFor;

        /// <summary>
        /// What the audit records of the request being put to the operator if a lock or a close
        /// takes its dialog away, or null; touched only on KeePass's thread, as the dialog is.
        /// </summary>
        private Action _unanswered;

        public PairingApprovalPrompt(IUiThread ui, IGateLog log)
            : this(ui, null, log)
        {
        }

        /// <summary>
        /// The same, and putting up no dialog for a database that has locked in the meantime.
        /// </summary>
        /// <param name="serving">
        /// Whether the database a request is about is still served unlocked, asked on KeePass's
        /// thread just before a dialog would be shown, or null for always.
        /// </param>
        public PairingApprovalPrompt(IUiThread ui, Func<string, bool> serving, IGateLog log)
            : this(ui, serving, null, log)
        {
        }

        /// <summary>
        /// The same, and keeping a dialog out of this plugin's own save and out of whatever KeePass
        /// is part way through.
        /// </summary>
        /// <param name="writes">
        /// The plugin's writes, asked about on KeePass's thread just before a dialog would be
        /// shown, and waited for on the connection's, or null where nothing writes.
        /// </param>
        public PairingApprovalPrompt(IUiThread ui, Func<string, bool> serving, IWriterState writes, IGateLog log)
            : this(ui, serving, writes, null, log)
        {
        }

        /// <summary>The same, and writing down a dialog a lock or a close takes away.</summary>
        /// <param name="audit">
        /// Where the line for a dialog taken away unanswered is recorded, on KeePass's thread as it
        /// is taken away, or null where nothing is written down. The handlers write every other
        /// answer, and leave this line to the screen.
        /// </param>
        public PairingApprovalPrompt(
            IUiThread ui, Func<string, bool> serving, IWriterState writes, AdmissionAudit audit, IGateLog log)
            : this(ui, serving, writes, audit, null, log)
        {
        }

        /// <summary>
        /// The same, and keeping KeePass's main window disabled under the dialog while it stands.
        /// </summary>
        /// <param name="mainWindow">
        /// KeePass's main window, or null where there is none: `WindowEnabling` gives the reason.
        /// </param>
        public PairingApprovalPrompt(
            IUiThread ui,
            Func<string, bool> serving,
            IWriterState writes,
            AdmissionAudit audit,
            Control mainWindow,
            IGateLog log)
        {
            if (ui == null)
            {
                throw new ArgumentNullException(nameof(ui));
            }

            _ui = ui;
            _serving = serving ?? (databaseId => true);
            _writes = writes;
            _audit = audit;
            _mainWindow = mainWindow;
            _log = log ?? NullGateLog.Instance;
            Dialog = ShowDialog;
            RenewalDialog = ShowRenewalDialog;
        }

        /// <summary>
        /// Puts the request in front of the operator and answers with what they decided. Settable
        /// because a test that put a modal dialog on the screen would wait for somebody to answer
        /// it.
        /// </summary>
        internal Func<PairingRequest, TimeSpan, PairingApproval> Dialog { get; set; }

        /// <summary>The same for a renewal, and settable for the same reason.</summary>
        internal Func<RenewalRequest, TimeSpan, RenewalApproval> RenewalDialog { get; set; }

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return OnScreen(
                allowed => Dialog(request, allowed),
                request.DatabaseId,
                timeout,
                left,
                "pairing",
                PairingApproval.Unavailable,
                PairingApproval.TimedOut,
                PairingApproval.LockedWhileWaiting,
                _audit == null
                    ? (Action)null
                    : () => _audit.PairingUnanswered(request.DatabaseId, request.InvitationId));
        }

        public RenewalApproval Ask(RenewalRequest request, TimeSpan timeout, TimeSpan left)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return OnScreen(
                allowed => RenewalDialog(request, allowed),
                request.DatabaseId,
                timeout,
                left,
                "renewal",
                RenewalApproval.Unavailable,
                RenewalApproval.TimedOut,
                RenewalApproval.LockedWhileWaiting,
                _audit == null
                    ? (Action)null
                    : () => _audit.RenewalWithdrawn(request.DatabaseId, request.Client, request.ClientKey));
        }

        /// <summary>
        /// KeePass is locking: the dialog on the screen, if any, closes as unanswered.
        /// </summary>
        /// <remarks>
        /// On KeePass's thread, where every dialog is shown, so nothing here races a dialog being
        /// put up: one shown before this is closed by it, and one about to be shown after it finds
        /// its database no longer served and is never shown. Every way KeePass locks, its own
        /// timers, Ctrl and L, a session lock or switch, a suspend, a minimize, a change of remote
        /// control, ends in the same closing of the database that calls this.
        ///
        /// **Once per lock, however many databases it locks.** KeePass closes each open database
        /// in turn, inside the dialog's own modal loop, and each close calls this while the dialog
        /// is still on the screen, so the dialog is let go of here rather than when its loop ends.
        /// </remarks>
        public void Locked()
        {
            var showing = _showing;
            if (showing == null)
            {
                return;
            }

            _showing = null;
            _showingFor = null;
            _log.Warn("KeePass locked while a request was in front of the operator, so it was closed unanswered");
            Withdraw(showing, databaseClosed: false);
        }

        /// <summary>
        /// A database closed for real: the dialog on the screen, if it is about that database,
        /// closes as unanswered, as it does at a lock.
        /// </summary>
        /// <remarks>
        /// The operator's ruling of 2026-11-08, "close it with the database". **This replaced
        /// leaving the dialog up**, on the argument that its answer would find the database gone
        /// when it was acted on: an Approve then offered nothing it could do, as an Approve over a
        /// locked database once did, and only the approval book letting go of the answer kept it
        /// from being written into the database opened again under the same authority. The book
        /// still does that for an answer given just before a close, which reaches it after.
        ///
        /// A close of another database leaves the dialog alone, R1, and a close that names no
        /// database closes it, since the approval book lets every approval go for such a close. A
        /// request still waiting for the screen needs nothing here: it finds its database no longer
        /// served and is never shown.
        /// </remarks>
        public void Closed(IReadOnlyCollection<string> databaseIds)
        {
            var showing = _showing;
            if (showing == null
                || (databaseIds != null && !databaseIds.Contains(_showingFor, StringComparer.Ordinal)))
            {
                return;
            }

            _showing = null;
            _showingFor = null;
            _log.Warn("a database closed while a request about it was in front of the operator, so it was " +
                      "closed unanswered");
            Withdraw(showing, databaseClosed: true);
        }

        /// <summary>
        /// Closes the dialog as unanswered, and writes it down as unanswered unless the operator
        /// answered it first.
        /// </summary>
        /// <remarks>
        /// Recorded here rather than by the handler once the answer reaches its connection, as the
        /// class remarks set out: at KeePass's exit, nothing waits for that thread. Only once,
        /// since a dialog is withdrawn once and the line goes with it.
        /// </remarks>
        private void Withdraw(ApprovalDialog showing, bool databaseClosed)
        {
            var unanswered = _unanswered;
            _unanswered = null;
            if (showing.Withdraw(databaseClosed) && unanswered != null)
            {
                unanswered();
            }
        }

        /// <summary>
        /// Waits for the screen, puts a dialog on it, and waits for the answer.
        /// </summary>
        /// <param name="dialog">Shows the dialog for as long as it is handed.</param>
        /// <param name="timeout">
        /// How long the operator is given, and how long this waits for the screen.
        /// </param>
        /// <param name="left">
        /// How long from now the dialog may stand at most, so that what follows it still fits the
        /// pairing budget.
        /// </param>
        /// <param name="what">What is being asked, for the log: a pairing or a renewal.</param>
        /// <param name="lockedWhileWaiting">
        /// The answer for a request whose database locked or closed while it waited for the screen,
        /// which is never shown. A dialog the lock or the close takes away answers for itself, as
        /// unanswered.
        /// </param>
        /// <param name="unanswered">
        /// What the audit records of the request if a lock or a close takes its dialog away, or
        /// null for nothing.
        /// </param>
        private TAnswer OnScreen<TAnswer>(
            Func<TimeSpan, TAnswer> dialog,
            string databaseId,
            TimeSpan timeout,
            TimeSpan left,
            string what,
            Func<string, TAnswer> unavailable,
            Func<TAnswer> timedOut,
            Func<TAnswer> lockedWhileWaiting,
            Action unanswered)
            where TAnswer : class
        {
            // Waited for rather than queued behind indefinitely: a request that cannot get the
            // screen inside the time its own operator was going to be given has nothing left to do
            // with it. Nothing is spent by this answer, so the client can ask again. The wait for
            // the plugin's own save below is part of the same wait, not added to it.
            var waiting = Stopwatch.StartNew();
            if (!Monitor.TryEnter(_screen, timeout))
            {
                _log.Warn("another request was already in front of the operator, so this " + what +
                          " waited and gave up");
                return unavailable("another request is already being decided");
            }

            try
            {
                TAnswer answer = null;

                bool shown;
                while (true)
                {
                    // **The plugin's own save is waited out, and KeePass's own work is not.** A
                    // save this plugin makes for another client is a few waits from over, and the
                    // request it would turn away is one the operator has not seen yet. KeePass's
                    // own work is answered `503 busy` as a write is: a message box before the call
                    // below is queued, and anything else inside it.
                    if (!Quiet(timeout - waiting.Elapsed))
                    {
                        _log.Warn("this plugin's own save was still running when the wait for the screen " +
                                  "ran out, so this " + what + " was never shown");
                        return unavailable("the plugin's own save outlasted the wait for the screen");
                    }

                    // Asked before the dialog is queued, as a write asks it: under a dialog of
                    // Windows' own the dialog could not be shown until that one closed.
                    var stalled = _ui.Stalled;
                    if (stalled != null)
                    {
                        _log.Warn("the " + what + " dialog was not shown, because " + stalled);
                        return unavailable(stalled);
                    }

                    var underneath = false;
                    try
                    {
                        // Asked on KeePass's thread, as the lock is: a request that waited here while
                        // KeePass locked, or its database closed, is answered as locked without a
                        // dialog being put up at all.
                        // And asked there for the second reason a write asks there: the turn waits
                        // for KeePass to be idle, and a dialog of WinForms' own shown inside a save
                        // is idle enough to start one.
                        shown = _ui.Run(
                            () =>
                            {
                                if (!_serving(databaseId))
                                {
                                    _log.Warn("the database a " + what + " was about locked or closed while it " +
                                              "waited for the screen");
                                    answer = lockedWhileWaiting();
                                    return;
                                }

                                if (_writes != null && _writes.Saving)
                                {
                                    // Started inside this plugin's own save, which began after the
                                    // wait above, through a dialog shown inside it: put back, and
                                    // queued again once it ends.
                                    underneath = true;
                                    return;
                                }

                                var busy = _writes == null ? null : _writes.Interrupting();
                                if (busy != null)
                                {
                                    _log.Warn("the " + what + " dialog was not shown, because " + busy);
                                    answer = unavailable(busy);
                                    return;
                                }

                                // **What is left of the pairing budget, and never more than the
                                // operator's own time.** The dialog's countdown starts when it is
                                // built, so a dialog that came up behind another, or behind a busy
                                // KeePass, used to give the operator the whole of their time on
                                // purpose, and took the pairing past its budget: the socket closed
                                // under the write, with the invitation spent and the record saved
                                // and no certificate handed back. The operator's ruling of
                                // 2026-11-16 gives it what is left instead, which its countdown
                                // starts from, and shows nothing that could not be read.
                                var remaining = left - waiting.Elapsed;
                                if (remaining < Shortest)
                                {
                                    _log.Warn("the " + what + " dialog was not shown, because only " +
                                              Seconds(remaining) + " s of the time its connection " +
                                              "is given was left when it could come up");
                                    answer = unavailable(
                                        "too little of its time was left to put it to the operator");
                                    return;
                                }

                                if (remaining < timeout)
                                {
                                    _log.Info("the " + what + " dialog came up late, so the operator " +
                                              "was given " + Seconds(remaining) + " s to answer it");
                                }

                                // For as long as this request's dialog can stand, on the thread
                                // that withdraws it.
                                _unanswered = unanswered;
                                try
                                {
                                    answer = dialog(remaining < timeout ? remaining : timeout);
                                }
                                finally
                                {
                                    _unanswered = null;
                                }
                            },
                            timeout + Grace);
                    }
                    catch (Exception error) when (Expected.OfUserInterface(error))
                    {
                        _log.Error("the " + what + " dialog could not be shown", error);
                        return unavailable(error.Message);
                    }

                    if (!underneath)
                    {
                        break;
                    }

                    if (waiting.Elapsed >= timeout)
                    {
                        _log.Warn("this plugin's own save was still running when the wait for the screen " +
                                  "ran out, so this " + what + " was never shown");
                        return unavailable("the plugin's own save outlasted the wait for the screen");
                    }
                }

                if (!shown && answer == null)
                {
                    _log.Warn("the " + what + " dialog did not come back, so nothing was approved");
                    return timedOut();
                }

                // **An answer given after the wait ran out is still the answer.** `IUiThread.Run`
                // says false for work it withdrew before it began and for work that began too late
                // to finish inside the wait, which it then waits out. The dialog's own countdown
                // starts when the dialog is built, so a thread that reached the dialog late gave the
                // operator what was left of the pairing budget after this wait was over, and what
                // they decided is here. That used to be their whole two minutes, deliberately,
                // which is reversed where the dialog is built above. Answering a timeout instead
                // turned a refusal into one that spends nothing, so the client could raise the
                // dialog again, and an approval into nothing issued.
                if (!shown)
                {
                    _log.Warn("the " + what + " dialog was shown late, and its answer is the one given");
                }

                return answer ?? timedOut();
            }
            finally
            {
                Monitor.Exit(_screen);
            }
        }

        /// <summary>
        /// Waits for the plugin's write in flight to end, for no longer than what is left, and
        /// says whether it did.
        /// </summary>
        private bool Quiet(TimeSpan left)
        {
            return _writes == null || _writes.Quiet(left < TimeSpan.Zero ? TimeSpan.Zero : left);
        }

        /// <summary>A span as the log writes it, in whole seconds and never below none.</summary>
        private static string Seconds(TimeSpan span)
        {
            return Math.Max(0, (int)span.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Shows a dialog modally, as the dialog a lock or a close of its database closes while it
        /// stands. Internal so that a test can show a dialog placed where nobody sees it.
        /// </summary>
        /// <param name="databaseId">The database the request on the dialog is about.</param>
        internal void Show(ApprovalDialog dialog, string databaseId)
        {
            _showing = dialog;
            _showingFor = databaseId;
            try
            {
                // Held disabled under the dialog, since a synchronize or a save KeePass's periodic
                // trigger runs inside it ends by enabling the window: `WindowEnabling`.
                WindowEnabling.ShowDialog(dialog, null, _mainWindow);
            }
            finally
            {
                _showing = null;
                _showingFor = null;
            }
        }

        private PairingApproval ShowDialog(PairingRequest request, TimeSpan timeout)
        {
            using (var dialog = new PairingApprovalForm(request, timeout))
            {
                Show(dialog, request.DatabaseId);
                var answer = dialog.Answer();
                _log.Info("pairing request for " + request.Name + Outcome(answer.Decision, answer.Reason));
                return answer;
            }
        }

        private RenewalApproval ShowRenewalDialog(RenewalRequest request, TimeSpan timeout)
        {
            using (var dialog = new RenewalApprovalForm(request, timeout, _log))
            {
                Show(dialog, request.DatabaseId);
                var answer = dialog.Answer();
                _log.Info("renewal request for " + request.Name + Outcome(answer.Decision, answer.Reason));
                return answer;
            }
        }

        /// <summary>
        /// How the log line ends: "was approved" or "was refused once", and "went unanswered"
        /// for a countdown that ran out, whose reason does not read after "was".
        /// </summary>
        internal static string Outcome(PairingDecision decision, string reason)
        {
            return decision == PairingDecision.TimedOut ? " went unanswered" : " was " + reason;
        }
    }
}
