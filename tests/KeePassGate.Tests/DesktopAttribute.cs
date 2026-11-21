using System;
using System.Globalization;
using System.Text;
using System.Threading;
using KeePassGate.Testing;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

// The visible window station's name in lower case: cspell:ignore winsta
namespace KeePassGate.Tests
{
    /// <summary>
    /// Holds the desktop for each test that shows a window, which takes the activation from
    /// whatever held it, or asks what is on top, which window has the activation, or where the
    /// focus went.
    /// </summary>
    /// <remarks>
    /// **`[NonParallelizable]` keeps such a fixture apart from the others in this process, and
    /// nothing in it reaches another process.** A second test run at the same moment on the same
    /// desktop shows its windows at the same places, and takes the activation from this run's. A
    /// press posted to a button counts no click while another run's identical form lies over the
    /// same point, because WinForms asks the screen which window is under the press before it
    /// raises one. So each test of a fixture carrying this takes a named mutex first and lets it
    /// go after, and the fixtures that care about the screen take turns with every test process
    /// this account runs on the same desktop. On a single test it holds the desktop for that test
    /// alone, for a fixture with one test that shows a window among many that do not.
    ///
    /// **The visible desktop takes the session's mutex, and every other desktop a mutex of its
    /// own.** A test on `WinSta0\Default` takes <see cref="SessionLockName"/>; a test on any
    /// other desktop takes a mutex named for that desktop and its window station, since a
    /// desktop's name is unique only within its station. Windows shown on a desktop of a run's own
    /// lie over nothing another run shows, so such a run takes no turn with a run on any other
    /// desktop, the visible desktop included. A run that cannot read its names takes the
    /// session's mutex too, and so takes turns with the visible desktop, which is where such a
    /// run most likely is and the only desktop whose windows somebody else sees.
    ///
    /// A mutex another process cannot open, or a mutex held for longer than the patience here,
    /// leaves the test unlocked rather than stuck, and a holder that ended without letting go
    /// hands it on as an abandoned wait, which is taken as acquired.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class DesktopAttribute : Attribute, ITestAction
    {
        /// <summary>
        /// The lock of the visible desktop, which is also taken by a run that cannot name the
        /// desktop it is on.
        /// </summary>
        internal const string SessionLockName = @"Local\KeePassGate.Tests.Desktop";

        /// <summary>The window station of the visible desktop.</summary>
        private const string VisibleStation = "WinSta0";

        /// <summary>The visible desktop, in <see cref="VisibleStation"/>.</summary>
        private const string VisibleDesktop = "Default";

        /// <summary>
        /// The longest name .NET Framework's `Mutex` takes, `MAX_PATH`, past which it throws
        /// rather than opening anything.
        /// </summary>
        private const int LongestLockName = 260;

        /// <summary>`UOI_NAME`, which asks `GetUserObjectInformation` for an object's name.</summary>
        private const int NameInformation = 2;

        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

        private Mutex _held;

        public ActionTargets Targets
        {
            get { return ActionTargets.Test; }
        }

        /// <summary>
        /// The lock for the desktop the calling thread is on, in the window station of this
        /// process.
        /// </summary>
        internal static string LockNameOfThisThread()
        {
            var station = NameOf(TestNativeMethods.GetProcessWindowStation());
            var desktop = NameOf(TestNativeMethods.GetThreadDesktop(TestNativeMethods.GetCurrentThreadId()));
            return LockName(station, desktop);
        }

        /// <summary>
        /// The lock for the desktop named <paramref name="desktop"/> in the window station named
        /// <paramref name="station"/>: <see cref="SessionLockName"/> for the visible desktop and
        /// where either name is missing, and a lock of its own for any other desktop.
        /// </summary>
        /// <remarks>
        /// **The visible desktop is recognized without regard to case**, by an ordinal
        /// comparison, because Windows names window stations and desktops without regard to case,
        /// so `winsta0\default` is the visible desktop too. Any other name is escaped as Windows
        /// gives it, which is the spelling the desktop was made with, the same for every thread on
        /// it.
        ///
        /// **Each name is escaped rather than copied**, so that the lock's name is valid whatever
        /// the desktop and the station are called: past its namespace a kernel object's name must
        /// not hold a backslash, and a name read from Windows is not trusted to lack one. An ASCII
        /// letter, digit or hyphen is kept as it is, and every other UTF-16 unit becomes an
        /// underscore and its four hexadecimal digits, the underscore and the dot among them. So
        /// the dot between the two names appears in neither, no name can spell an escape, and two
        /// different pairs never meet in a single lock.
        ///
        /// A lock's name longer than <see cref="LongestLockName"/> falls back as a missing name
        /// does, because the mutex would refuse it.
        /// </remarks>
        internal static string LockName(string station, string desktop)
        {
            if (string.IsNullOrEmpty(station) || string.IsNullOrEmpty(desktop) ||
                (string.Equals(station, VisibleStation, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(desktop, VisibleDesktop, StringComparison.OrdinalIgnoreCase)))
            {
                return SessionLockName;
            }

            var name = new StringBuilder(SessionLockName);
            Escape(name.Append('.'), station);
            Escape(name.Append('.'), desktop);
            return name.Length <= LongestLockName ? name.ToString() : SessionLockName;
        }

        public void BeforeTest(ITest test)
        {
            // codeql[cs/missed-using-statement] disposed only when it is not kept, which no `using` can do
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, LockNameOfThisThread());
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

        /// <summary>
        /// The name of a desktop or a window station, or null where the handle is missing or the
        /// name will not fit in a lock's name.
        /// </summary>
        internal static string NameOf(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var name = new char[LongestLockName];
            int needed;
            if (!TestNativeMethods.GetUserObjectInformation(
                    handle, NameInformation, name, name.Length * sizeof(char), out needed))
            {
                return null;
            }

            var end = Array.IndexOf(name, '\0');
            return end < 0 ? null : new string(name, 0, end);
        }

        /// <summary>The characters a name keeps as they are in a lock's name.</summary>
        private const string Kept = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-";

        private static void Escape(StringBuilder into, string name)
        {
            foreach (var unit in name)
            {
                if (Kept.IndexOf(unit) >= 0)
                {
                    into.Append(unit);
                }
                else
                {
                    into.Append('_').Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
