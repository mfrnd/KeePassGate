using System;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Holds the desktop for each test of a fixture that shows a window and asks what is on top,
    /// which window has the activation, or where the focus went.
    /// </summary>
    /// <remarks>
    /// **`[NonParallelizable]` keeps such a fixture apart from the others in this process, and
    /// nothing in it reaches another process.** The screen is the logon session's, so a second
    /// test run at the same moment shows its windows on the same desktop, at the same places,
    /// and takes the activation from this one's. A press posted to a button counts no click
    /// while another run's identical form lies over the same point, because WinForms asks the
    /// screen which window is under the press before it raises one. So each
    /// test of a fixture carrying this takes a named mutex first and lets it go after, and the
    /// fixtures that care about the screen take turns with every test process this account runs.
    ///
    /// A mutex another process cannot open, or one held for longer than the patience here,
    /// leaves the test unlocked rather than stuck, and a holder that ended without letting go
    /// hands it on as an abandoned wait, which is taken as acquired.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    internal sealed class DesktopAttribute : Attribute, ITestAction
    {
        private const string LockName = @"Local\KeePassGate.Tests.Desktop";

        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

        private Mutex _held;

        public ActionTargets Targets
        {
            get { return ActionTargets.Test; }
        }

        public void BeforeTest(ITest test)
        {
            // codeql[cs/missed-using-statement] disposed only when it is not kept, which no `using` can do
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, LockName);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            // Let go of on every way out that does not hold it, a throw from the wait among them.
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(Patience);
            }
            catch (AbandonedMutexException)
            {
                // Its holder ended without letting go, and the wait was granted all the same.
                acquired = true;
            }
            finally
            {
                if (!acquired)
                {
                    mutex.Dispose();
                }
            }

            if (!acquired)
            {
                return;
            }

            _held = mutex;
        }

        public void AfterTest(ITest test)
        {
            var mutex = _held;
            if (mutex == null)
            {
                return;
            }

            _held = null;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
