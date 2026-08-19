using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Work handed to the user interface's own message queue with nobody waiting for it, guarded
    /// so that it cannot fail there.
    /// </summary>
    /// <remarks>
    /// **`ControlUiThread.Marshaled` is deliberately not reused, and the difference is the
    /// caller.** That type catches everything the work throws and holds it for `Rethrow`, so the
    /// failure lands on the thread that asked for the work and can be named or counted there.
    /// Here there is no such thread. The post is made from the user interface thread itself, in a
    /// handler of one of KeePass's own events, and it returns before the queue is pumped: a stored
    /// exception would have nowhere to be thrown and would simply be lost, and waiting for the
    /// work instead would hold the one thread that has to run it.
    ///
    /// So the failure is reported where it happens rather than carried to somebody. That is what
    /// keeps this from being a fifth place that catches everything: it is the entry point's own
    /// catch-all following the entry point's own work across the post, and what it catches goes to
    /// the same `GateIncidents` every other branch of that catch reports to.
    ///
    /// **What is being kept out is WinForms' own error dialog.** An exception let out of a
    /// callback `BeginInvoke` is running is handed to `Application.OnThreadException` as well as
    /// being stored for `EndInvoke`, and KeePass registers no `Application.ThreadException`
    /// handler and sets no unhandled exception mode, so WinForms goes modal inside the password
    /// manager with a Quit button that ends the process outside KeePass's own shutdown. Nothing
    /// here calls `EndInvoke`, so without this guard the dialog is the only thing that would ever
    /// mention the failure. `FINDINGS.md` records the measurement.
    /// </remarks>
    internal static class Unattended
    {
        /// <summary>
        /// Posts <paramref name="work"/> to run once the thread that owns
        /// <paramref name="control"/> is pumping again. Whatever the work throws is handed to
        /// <paramref name="failed"/>, on that thread.
        /// </summary>
        /// <remarks>
        /// **Only the running of the work is guarded.** A control that has gone, or that never got
        /// a window, refuses the post from here, on the calling thread, where the caller is still
        /// standing and can say what it was unable to say. Catching that here as well would take
        /// the two failures, one of them ordinary and one of them a defect, and report them alike.
        /// </remarks>
        public static void Post(Control control, Action work, Action<Exception> failed)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (failed == null)
            {
                throw new ArgumentNullException(nameof(failed));
            }

            control.BeginInvoke(new Action(delegate
            {
                try
                {
                    work();
                }
                catch (Exception error)
                {
                    try
                    {
                        failed(error);
                    }
                    catch (Exception)
                    {
                        // A reporter that fails while reporting has nowhere left to report to, and
                        // letting it out is exactly the modal dialog this method exists to
                        // prevent. `GateIncidents.Record` swallows a failing log for the same
                        // reason and says so in the same words.
                    }
                }
            }));
        }
    }
}
