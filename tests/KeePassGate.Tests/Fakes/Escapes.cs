using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Both ways a failure in posted work can arrive on the user interface thread instead of
    /// somewhere the code that posted it can see it, and a message loop that watches for either.
    /// </summary>
    /// <remarks>
    /// The handler is registered for the length of the pump so that WinForms routes an escaped
    /// callback's exception into an assertion rather than into its own modal error dialog, which
    /// would hang the run instead of failing it. That dialog on KeePass's thread is the defect
    /// both fixtures pin: KeePass registers no handler of its own, so an escape ends up in front
    /// of the operator with a Quit button on it.
    ///
    /// Here rather than in either fixture, because both need the same watch and a second copy of
    /// it would be a second answer to what counts as an escape.
    /// </remarks>
    public sealed class Escapes
    {
        /// <summary>What WinForms caught in the window procedure and handed on.</summary>
        public Exception Unhandled { get; private set; }

        /// <summary>What came straight out of the message pump instead.</summary>
        public Exception Loose { get; private set; }

        /// <summary>
        /// Runs the message loop until <paramref name="finished"/> answers true, or until patience
        /// runs out, watching where a failure goes.
        /// </summary>
        public static Escapes PumpUntil(Func<bool> finished, TimeSpan patience)
        {
            if (finished == null)
            {
                throw new ArgumentNullException(nameof(finished));
            }

            var escapes = new Escapes();
            ThreadExceptionEventHandler watch = (sender, e) => escapes.Unhandled = e.Exception;

            Application.ThreadException += watch;
            try
            {
                var until = DateTime.UtcNow + patience;
                while (!finished() && DateTime.UtcNow < until)
                {
                    var loose = Thrown.By(() => Application.DoEvents());
                    if (loose != null)
                    {
                        // The other route out. WinForms leaves the exception in the window
                        // procedure where a debugger is attached, and then the pump is what raises
                        // it.
                        escapes.Loose = loose;
                    }
                }
            }
            finally
            {
                Application.ThreadException -= watch;
            }

            return escapes;
        }
    }
}
