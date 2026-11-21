using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KeePassGate.Testing;
using NUnit.Framework;

// Windows's own names for a desktop's rights, and the visible window station's name in lower
// case: cspell:ignore READOBJECTS CREATEWINDOW winsta
namespace KeePassGate.Tests
{
    /// <summary>
    /// Which lock a test that shows a window takes: the lock of the desktop it is on, so that runs
    /// on the same desktop take turns and runs on different desktops do not.
    /// </summary>
    /// <remarks>
    /// The desktop this fixture makes is never shown and holds no window: a thread of its own is
    /// moved onto it to read its name and moved back, so nothing here reaches the screen, and the
    /// fixture needs no mark to run beside the others.
    /// </remarks>
    [TestFixture]
    public sealed class DesktopAttributeTests
    {
        /// <summary>
        /// The lock of the visible desktop and of a run that cannot name its desktop, written out
        /// rather than read.
        /// </summary>
        private const string Session = @"Local\KeePassGate.Tests.Desktop";

        /// <summary>
        /// `DESKTOP_READOBJECTS` and `DESKTOP_CREATEWINDOW`, the least access that makes a desktop,
        /// moves a thread onto it and reads its name.
        /// </summary>
        /// <remarks>
        /// Nothing here creates a window there, and still Windows 11 refuses to make a desktop for
        /// a handle without `DESKTOP_CREATEWINDOW`, with `ERROR_NOT_ENOUGH_MEMORY`. And a thread
        /// on a desktop whose handle lacks `DESKTOP_READOBJECTS` cannot read that desktop's name,
        /// so its lock is the visible desktop's.
        /// </remarks>
        private const uint ReadAndCreateWindows = 0x0003;

        [Test]
        public void The_visible_desktop_takes_the_lock_of_the_session()
        {
            Assert.That(DesktopAttribute.LockName("WinSta0", "Default"), Is.EqualTo(Session));
            Assert.That(
                DesktopAttribute.LockName("winsta0", "DEFAULT"), Is.EqualTo(Session),
                "Windows names a window station and a desktop without regard to case");
        }

        [Test]
        public void Another_desktop_takes_a_lock_of_its_own_and_the_same_lock_every_time()
        {
            var first = DesktopAttribute.LockName("WinSta0", "kpg-first");

            Assert.That(first, Is.EqualTo(@"Local\KeePassGate.Tests.Desktop.WinSta0.kpg-first"));
            Assert.That(DesktopAttribute.LockName("WinSta0", "kpg-first"), Is.EqualTo(first));
            Assert.That(DesktopAttribute.LockName("WinSta0", "kpg-second"), Is.Not.EqualTo(first));
        }

        [Test]
        public void A_desktop_named_Default_in_another_window_station_is_not_the_visible_desktop()
        {
            Assert.That(
                DesktopAttribute.LockName("Service-0x0-3e7$", "Default"),
                Is.EqualTo(@"Local\KeePassGate.Tests.Desktop.Service-0x0-3e7_0024.Default"),
                "a desktop's name is unique only within its window station");
        }

        [Test]
        public void A_name_no_kernel_object_could_hold_is_escaped_into_a_name_a_mutex_takes()
        {
            var name = DesktopAttribute.LockName("WinSta0", @"a\b.c_d e:ü");

            Assert.That(
                name,
                Is.EqualTo(@"Local\KeePassGate.Tests.Desktop.WinSta0.a_005Cb_002Ec_005Fd_0020e_003A_00FC"));
            Assert.That(() => new Mutex(false, name).Dispose(), Throws.Nothing);
            Assert.That(
                () => new Mutex(false, Session + @".WinSta0.a\b").Dispose(), Throws.InstanceOf<IOException>(),
                "the same name copied rather than escaped, which is what the escaping is for");
        }

        [Test]
        public void No_name_can_spell_an_escape_or_move_the_dot_between_the_two_names()
        {
            Assert.That(
                DesktopAttribute.LockName("WinSta0", "_005C"),
                Is.Not.EqualTo(DesktopAttribute.LockName("WinSta0", @"\")));
            Assert.That(
                DesktopAttribute.LockName("a.b", "c"),
                Is.Not.EqualTo(DesktopAttribute.LockName("a", "b.c")));
        }

        [Test]
        public void A_run_that_cannot_read_its_names_takes_turns_with_the_visible_desktop()
        {
            var visible = DesktopAttribute.LockName("WinSta0", "Default");

            Assert.That(DesktopAttribute.SessionLockName, Is.EqualTo(visible));
            Assert.That(DesktopAttribute.LockName(null, "kpg-first"), Is.EqualTo(visible));
            Assert.That(DesktopAttribute.LockName("kpg-station", null), Is.EqualTo(visible));
            Assert.That(DesktopAttribute.LockName(string.Empty, "kpg-first"), Is.EqualTo(visible));
            Assert.That(DesktopAttribute.LockName("kpg-station", string.Empty), Is.EqualTo(visible));
        }

        [Test]
        public void A_name_too_long_for_a_mutex_takes_turns_with_the_visible_desktop()
        {
            // The prefix and the station come to 40 characters, and a mutex takes 260.
            var longest = DesktopAttribute.LockName("WinSta0", new string('x', 220));

            Assert.That(longest, Has.Length.EqualTo(260));
            Assert.That(() => new Mutex(false, longest).Dispose(), Throws.Nothing);
            Assert.That(
                DesktopAttribute.LockName("WinSta0", new string('x', 221)),
                Is.EqualTo(DesktopAttribute.LockName("WinSta0", "Default")));
        }

        [Test]
        public void The_lock_of_this_thread_is_the_lock_of_the_desktop_it_is_on()
        {
            // Which desktop that is depends on where the run was started, so the names are
            // asserted readable and the lock is asserted to be theirs.
            var station = Station();
            var desktop = DesktopAttribute.NameOf(
                TestNativeMethods.GetThreadDesktop(TestNativeMethods.GetCurrentThreadId()));

            Assert.That(station, Is.Not.Null.And.Not.Empty);
            Assert.That(desktop, Is.Not.Null.And.Not.Empty);
            Assert.That(DesktopAttribute.LockNameOfThisThread(), Is.EqualTo(DesktopAttribute.LockName(station, desktop)));
        }

        [Test]
        public void A_thread_on_another_desktop_reads_that_desktop_into_another_lock()
        {
            var name = DesktopName();
            WithDesktop(name, desktop =>
            {
                var there = OnDesktop(desktop, DesktopAttribute.LockNameOfThisThread);

                Assert.That(there, Is.EqualTo(DesktopAttribute.LockName(Station(), name)));
                Assert.That(there, Is.Not.EqualTo(DesktopAttribute.LockNameOfThisThread()));
            });
        }

        [Test]
        public void A_test_on_another_desktop_takes_the_lock_named_for_that_desktop()
        {
            var name = DesktopName();
            WithDesktop(name, desktop =>
            {
                var expected = DesktopAttribute.LockName(Station(), name);
                var held = OnDesktop(desktop, () =>
                {
                    var attribute = new DesktopAttribute();
                    attribute.BeforeTest(null);
                    try
                    {
                        // The name is new to this test, so the mutex exists only while the
                        // attribute holds it: a mutex it did not acquire is disposed before this
                        // is asked.
                        Mutex opened;
                        var exists = Mutex.TryOpenExisting(expected, out opened);
                        opened?.Dispose();
                        return exists;
                    }
                    finally
                    {
                        attribute.AfterTest(null);
                    }
                });

                Assert.That(held, Is.True);
            });
        }

        /// <summary>
        /// A desktop name nothing else uses, with a space and a dot in it, so that the escaping is
        /// exercised by a name Windows really gave a desktop.
        /// </summary>
        private static string DesktopName()
        {
            return "KeePassGate Tests." + Guid.NewGuid().ToString("N");
        }

        private static string Station()
        {
            return DesktopAttribute.NameOf(TestNativeMethods.GetProcessWindowStation());
        }

        /// <summary>
        /// Makes a desktop in this process's window station, hands it to <paramref name="work"/>,
        /// and closes it whatever the work did.
        /// </summary>
        private static void WithDesktop(string name, Action<IntPtr> work)
        {
            var desktop = TestNativeMethods.CreateDesktop(
                name, IntPtr.Zero, IntPtr.Zero, 0, ReadAndCreateWindows, IntPtr.Zero);
            if (desktop == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            try
            {
                work(desktop);
            }
            finally
            {
                TestNativeMethods.CloseDesktop(desktop);
            }
        }

        /// <summary>
        /// Runs <paramref name="work"/> on a thread of its own moved onto <paramref name="desktop"/>,
        /// and moved back before it ends, so that the desktop is in use by nothing once this returns.
        /// </summary>
        /// <remarks>
        /// A thread of its own because Windows moves only a thread that owns no window and no hook,
        /// which a fresh thread is and a test's thread need not be. What the work throws is thrown
        /// here rather than on that thread, where it would end the process.
        /// </remarks>
        private static T OnDesktop<T>(IntPtr desktop, Func<T> work)
        {
            var task = Task.Factory.StartNew(
                () =>
                {
                    var own = TestNativeMethods.GetThreadDesktop(TestNativeMethods.GetCurrentThreadId());
                    if (!TestNativeMethods.SetThreadDesktop(desktop))
                    {
                        throw new Win32Exception();
                    }

                    try
                    {
                        return work();
                    }
                    finally
                    {
                        TestNativeMethods.SetThreadDesktop(own);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return task.GetAwaiter().GetResult();
        }
    }
}
