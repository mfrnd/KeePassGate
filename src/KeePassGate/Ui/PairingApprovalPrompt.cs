using System;
using System.Threading;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Puts a pairing request on the screen and waits for the answer, from a connection thread.
    /// </summary>
    /// <remarks>
    /// The marshal is synchronous, as every write's is. What is exceptional here is the dialog:
    /// `DESIGN.md` §119 lets no other request put a window in front of the operator. What keeps
    /// the wait from becoming a deadlock: the wait is bounded, the dialog closes itself, and a
    /// pairing that gets no answer fails rather than holds on.
    ///
    /// The grace is why the two timeouts are not the same number. The dialog is told to close after
    /// the approval timeout, and this waits a little longer than that, so an unanswered dialog ends
    /// as a timeout the operator's own screen produced rather than as a connection thread walking
    /// away from a window that is still open.
    /// </remarks>
    public sealed class PairingApprovalPrompt : IPairingApproval
    {
        /// <summary>How much longer than the dialog this waits before giving up on it.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

        private readonly IUiThread _ui;
        private readonly IGateLog _log;

        /// <summary>
        /// One pairing on screen at a time.
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

        public PairingApprovalPrompt(IUiThread ui, IGateLog log)
        {
            if (ui == null)
            {
                throw new ArgumentNullException(nameof(ui));
            }

            _ui = ui;
            _log = log ?? NullGateLog.Instance;
            Dialog = ShowDialog;
        }

        /// <summary>
        /// Puts the request in front of the operator and answers with what they decided. Settable
        /// because a test that put a modal dialog on the screen would wait for somebody to answer
        /// it.
        /// </summary>
        internal Func<PairingRequest, TimeSpan, PairingApproval> Dialog { get; set; }

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // Waited for rather than queued behind indefinitely: a second pairing that cannot get
            // the screen inside the time its own operator was going to be given has nothing left to
            // do with it. Nothing is spent by this answer, so the invitee can try again.
            if (!Monitor.TryEnter(_screen, timeout))
            {
                _log.Warn("another pairing was already in front of the operator, so this one waited and gave up");
                return PairingApproval.Unavailable("another pairing is already being decided");
            }

            try
            {
                PairingApproval answer = null;

                bool shown;
                try
                {
                    shown = _ui.Run(() => answer = Dialog(request, timeout), timeout + Grace);
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("the pairing dialog could not be shown", error);
                    return PairingApproval.Unavailable(error.Message);
                }

                if (!shown && answer == null)
                {
                    _log.Warn("the pairing dialog did not come back, so nothing was approved");
                    return PairingApproval.TimedOut();
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
                    _log.Warn("the pairing dialog was shown late, and its answer is the one given");
                }

                return answer ?? PairingApproval.TimedOut();
            }
            finally
            {
                Monitor.Exit(_screen);
            }
        }

        private PairingApproval ShowDialog(PairingRequest request, TimeSpan timeout)
        {
            using (var dialog = new PairingApprovalForm(request, timeout))
            {
                dialog.ShowDialog();
                var answer = dialog.Answer();
                _log.Info("pairing request for " + request.Name + " was " + answer.Reason);
                return answer;
            }
        }
    }
}
