using System;
using System.Diagnostics;
using System.Threading;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;

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
    /// </remarks>
    ///
    /// **A lock takes the question away.** KeePass locks under these dialogs, which hold no lock
    /// off, and a dialog left standing over a locked database offered an Approve that could do
    /// nothing. `PAIRING.md` §308: the lock closes the open dialog as unanswered, a request still
    /// waiting for the screen is never shown, and both are answered `503 locked`.
    public sealed class PairingApprovalPrompt : IPairingApproval, IRenewalApproval, IApprovalScreen
    {
        /// <summary>How much longer than the dialog this waits before giving up on it.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

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

        /// <summary>The dialog on the screen, touched only on KeePass's thread.</summary>
        private ApprovalDialog _showing;

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
        {
            if (ui == null)
            {
                throw new ArgumentNullException(nameof(ui));
            }

            _ui = ui;
            _serving = serving ?? (databaseId => true);
            _writes = writes;
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

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return OnScreen(
                () => Dialog(request, timeout),
                request.DatabaseId,
                timeout,
                "pairing",
                PairingApproval.Unavailable,
                PairingApproval.TimedOut,
                PairingApproval.Locked);
        }

        public RenewalApproval Ask(RenewalRequest request, TimeSpan timeout)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return OnScreen(
                () => RenewalDialog(request, timeout),
                request.DatabaseId,
                timeout,
                "renewal",
                RenewalApproval.Unavailable,
                RenewalApproval.TimedOut,
                RenewalApproval.Locked);
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
        /// </remarks>
        public void Locked()
        {
            var showing = _showing;
            if (showing == null)
            {
                return;
            }

            _log.Warn("KeePass locked while a request was in front of the operator, so it was closed unanswered");
            showing.Withdraw();
        }

        /// <summary>
        /// A database closed for real. Nothing here: a dialog about it finds the database gone
        /// when its answer is acted on, and the approval book lets go of what it held.
        /// </summary>
        public void Closed()
        {
        }

        /// <summary>
        /// Waits for the screen, puts a dialog on it, and waits for the answer.
        /// </summary>
        /// <param name="what">What is being asked, for the log: a pairing or a renewal.</param>
        private TAnswer OnScreen<TAnswer>(
            Func<TAnswer> dialog,
            string databaseId,
            TimeSpan timeout,
            string what,
            Func<string, TAnswer> unavailable,
            Func<TAnswer> timedOut,
            Func<TAnswer> locked)
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
                    // request it would turn away is one the operator has not seen yet; KeePass's
                    // own work, a save of its own, an import, a message box, is answered `503 busy`
                    // inside the call below, as a write is.
                    if (!Quiet(timeout - waiting.Elapsed))
                    {
                        _log.Warn("this plugin's own save was still running when the wait for the screen " +
                                  "ran out, so this " + what + " was never shown");
                        return unavailable("the plugin's own save outlasted the wait for the screen");
                    }

                    var underneath = false;
                    try
                    {
                        // Asked on KeePass's thread, as the lock is: a request that waited here while
                        // KeePass locked is answered as locked without a dialog being put up at all.
                        // And asked there for the second reason a write asks there: this call is a
                        // message, delivered by whatever is pumping, and a save pumps.
                        shown = _ui.Run(
                            () =>
                            {
                                if (!_serving(databaseId))
                                {
                                    _log.Warn("the database a " + what + " was about locked while it waited for the screen");
                                    answer = locked();
                                    return;
                                }

                                if (_writes != null && _writes.Saving)
                                {
                                    // Delivered inside this plugin's own save, which began after
                                    // the wait above: put back, and posted again once it ends.
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

                                answer = dialog();
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
                // operator their whole two minutes after this wait was over, and what they decided
                // is here. Answering a timeout instead turned a refusal into one that spends
                // nothing, so the client could raise the dialog again, and an approval into nothing
                // issued.
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

        /// <summary>
        /// Shows a dialog modally, as the one a lock closes while it stands. Internal so that a
        /// test can show one placed where nobody sees it.
        /// </summary>
        internal void Show(ApprovalDialog dialog)
        {
            _showing = dialog;
            try
            {
                dialog.ShowDialog();
            }
            finally
            {
                _showing = null;
            }
        }

        private PairingApproval ShowDialog(PairingRequest request, TimeSpan timeout)
        {
            using (var dialog = new PairingApprovalForm(request, timeout))
            {
                Show(dialog);
                var answer = dialog.Answer();
                _log.Info("pairing request for " + request.Name + Outcome(answer.Decision, answer.Reason));
                return answer;
            }
        }

        private RenewalApproval ShowRenewalDialog(RenewalRequest request, TimeSpan timeout)
        {
            using (var dialog = new RenewalApprovalForm(request, timeout, _log))
            {
                Show(dialog);
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
