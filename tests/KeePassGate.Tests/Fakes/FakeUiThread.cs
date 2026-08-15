using System;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The user interface thread, as a test decides it behaves. Its whole reason for existing is
    /// the case that cannot be arranged with a real one: a thread that never gets round to the
    /// work, which is what pairing has to survive rather than wait for.
    /// </summary>
    internal sealed class FakeUiThread : IUiThread
    {
        public FakeUiThread()
        {
            Runs = true;
        }

        /// <summary>False for a user interface thread that is busy elsewhere.</summary>
        public bool Runs { get; set; }

        /// <summary>
        /// True for a thread that reached the work only after the wait was over: it runs the work
        /// to the end and answers false, as `ControlUiThread` does for work that began too late.
        /// </summary>
        public bool RunsLate { get; set; }

        public Exception Throws { get; set; }

        public int Asked { get; private set; }

        public TimeSpan LastTimeout { get; private set; }

        public bool Run(Action work, TimeSpan timeout)
        {
            Asked++;
            LastTimeout = timeout;

            if (Throws != null)
            {
                throw Throws;
            }

            if (!Runs)
            {
                return false;
            }

            work();
            return !RunsLate;
        }
    }
}
