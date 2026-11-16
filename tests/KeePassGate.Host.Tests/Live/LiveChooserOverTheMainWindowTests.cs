using System;
using System.IO;
using System.Linq;
using System.Threading;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// KeePass's main window under the authority chooser, while KeePass's own periodic trigger
    /// synchronizes the database underneath it.
    /// </summary>
    /// <remarks>
    /// **What only this proves** is the claim `WindowEnabling` rests on, about KeePass rather than
    /// about this plugin: that a trigger fires inside the modal loop of a dialog KeePass has not
    /// registered, and that the synchronize it runs ends by enabling the main window behind it.
    /// The host tier makes KeePass's two assignments itself; this lets KeePass make them.
    ///
    /// **Two copies of one file**, each of which recreated its authority, merged before KeePass
    /// starts, so the database KeePass opens holds both and the question comes up as it opens.
    /// The replica the trigger synchronizes with is a copy of the merged file, so the synchronize
    /// brings no authority the question does not already show, and nothing about it is asked
    /// again.
    ///
    /// **Read from Windows rather than through UI automation**, since the window's own state is
    /// the whole question and its managed property reads true throughout. Polled from before the
    /// synchronize until a while after its save, which is the moment KeePass unblocks the window.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveChooserOverTheMainWindowTests
    {
        private const string OtherCopy = "other-copy.kdbx";

        private const string Replica = "replica.kdbx";

        /// <summary>How often the trigger synchronizes, as often as KeePass lets it.</summary>
        private const int SynchronizeSeconds = 10;

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Long enough for the first synchronize to fall due and save.</summary>
        private static readonly TimeSpan SynchronizeTimeout = TimeSpan.FromSeconds(60);

        /// <summary>How long the window is watched once the synchronize has saved.</summary>
        private static readonly TimeSpan AfterTheSave = TimeSpan.FromSeconds(3);

        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

        private LiveSession _session;
        private bool _chooserAppeared;
        private bool _disabledWhenItCameUp;
        private bool _synchronizedUnderIt;
        private int _seenEnabledUnderIt;
        private bool _enabledOnceItClosed;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            var other = keePass.CopyDatabase(OtherCopy);
            keePass.RecreateAuthority(keePass.DatabasePath);
            keePass.RecreateAuthority(other);
            keePass.MergeIntoDatabase(other);

            var replica = keePass.CopyDatabase(Replica);
            keePass.SynchronizeEvery(SynchronizeSeconds, replica);
            var written = File.GetLastWriteTimeUtc(keePass.DatabasePath);

            var driver = KeePassDriver.Start(keePass);
            var main = new IntPtr(driver.MainWindow.Current.NativeWindowHandle);

            var chooser = driver.WaitForWindowById(GateTab.ChooserFormName, DialogTimeout);
            _chooserAppeared = chooser != null;
            Assert.That(chooser, Is.Not.Null,
                "No chooser appeared as KeePass opened a database holding two authorities. Windows: " +
                NativeWindows.Titles(keePass.Process) + Environment.NewLine + driver.Diagnostics +
                Environment.NewLine + keePass.ReadPluginLog());

            _disabledWhenItCameUp = !User32.IsWindowEnabled(main);

            var deadline = DateTime.UtcNow + SynchronizeTimeout;
            DateTime? saved = null;
            while (DateTime.UtcNow < deadline && (saved == null || DateTime.UtcNow < saved + AfterTheSave))
            {
                if (User32.IsWindowEnabled(main))
                {
                    _seenEnabledUnderIt++;
                }

                if (saved == null && File.GetLastWriteTimeUtc(keePass.DatabasePath) != written)
                {
                    saved = DateTime.UtcNow;
                }

                Thread.Sleep(Poll);
            }

            _synchronizedUnderIt = saved != null;

            Assert.That(driver.PressButton(chooser, GateTab.ChooserCancelButtonText), Is.True, driver.Diagnostics);
            Assert.That(driver.WaitForWindowToClose(GateTab.ChooserFormName, DialogTimeout), Is.True,
                "The chooser was still open after Cancel. " + driver.Diagnostics);

            var back = DateTime.UtcNow + DialogTimeout;
            while (!User32.IsWindowEnabled(main) && DateTime.UtcNow < back)
            {
                Thread.Sleep(Poll);
            }

            _enabledOnceItClosed = User32.IsWindowEnabled(main);

            _exited = driver.ShutDown();
            TestContext.Out.WriteLine(driver.Diagnostics);

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
        }

        [Test]
        public void The_database_KeePass_opened_raised_the_chooser()
        {
            Assert.That(_chooserAppeared, Is.True);
        }

        [Test]
        public void The_chooser_s_modal_loop_disabled_the_main_window()
        {
            Assert.That(_disabledWhenItCameUp, Is.True);
        }

        [Test]
        public void KeePass_s_trigger_synchronized_and_saved_under_the_chooser()
        {
            // Without this, the next test measured a window nothing tried to enable.
            Assert.That(_synchronizedUnderIt, Is.True,
                "the database file did not change within " + SynchronizeTimeout.TotalSeconds + " s");
        }

        [Test]
        public void The_main_window_stayed_disabled_while_the_chooser_stood()
        {
            Assert.That(_seenEnabledUnderIt, Is.Zero,
                "the main window took input behind a question still standing, in that many polls");
        }

        [Test]
        public void And_was_enabled_again_once_the_chooser_closed()
        {
            Assert.That(_enabledOnceItClosed, Is.True);
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly: something was still holding it, or it ended with an error.");
            Assert.That(_pluginLog, Does.Contain("stopped listening"));
        }

        [Test]
        public void Nothing_was_reported_but_the_undecided_database()
        {
            var unexpected = LiveSession.FailuresIn(_pluginLog)
                .Where(line => !line.Contains("until the operator keeps one"))
                .ToArray();

            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }
    }
}
