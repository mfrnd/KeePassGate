using System;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A standing answer to a renewal that no database took, and KeePass then closed by File,
    /// Exit: the box naming it stands alone, with KeePass's main window off the screen.
    /// </summary>
    /// <remarks>
    /// An exit closes the databases without redrawing the main window, which goes on listing the
    /// closed database's groups and entries until KeePass hides it after its plugins unload, and
    /// the box is raised while they unload. Only the real window shows that, which is why this is
    /// in the end to end tier.
    ///
    /// The answer is made owed by saving the throwaway database's file again under the open
    /// renewal dialog, so that its bytes are no longer those KeePass read and the writer refuses
    /// the save KeePass would stop to ask about, and then choosing Refuse forever.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveOwedAnswerAtExitTests
    {
        private const string HeldLine = "so it holds from memory";
        private const string OwedLine = "never reached its database, and goes now that KeePass exits";

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private LiveReply _refused;
        private bool _held;
        private bool _boxSeen;
        private string _boxText;
        private bool _mainWindowVisibleWithTheBox;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            // Invented entries, so that a window left behind the box has something to list.
            keePass.FillWithInventedContent();

            using (var renewing = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(renewing, "reader");

                var driver = KeePassDriver.Start(keePass);
                LivePairingTests.WaitUntilServing(keePass, driver);
                var main = new IntPtr(driver.MainWindow.Current.NativeWindowHandle);

                Exception failed = null;
                var asking = new Thread(() =>
                {
                    failed = Thrown.By(() => _refused = LiveGateClient.Send(
                        keePass.Port, keePass.NewGateName(), renewing, "POST", RenewalHandler.Path, "{}",
                        LiveGateClient.OnTheOperator));
                }) { IsBackground = true };
                asking.Start();

                var dialog = driver.WaitForWindow("asking to renew", DialogTimeout);
                Assert.That(dialog, Is.Not.Null, "the renewal dialog never appeared: " + failed + " " + driver.Diagnostics);

                SaveTheFileAgain(keePass);

                var menu = driver.OpenRefuseMenu(dialog);
                Assert.That(menu, Is.Not.Null, driver.Diagnostics);
                var choice = Uia.Descendant(menu, ControlType.MenuItem, text => text == GateTab.RefuseForeverText);
                Assert.That(choice, Is.Not.Null, "the menu offers no forever: " + Uia.Inventory(menu));
                Mouse.Click(choice);

                Assert.That(asking.Join(TimeSpan.FromSeconds(60)), Is.True, "the client was still waiting");
                _held = Logged(keePass, HeldLine, TimeSpan.FromSeconds(15));

                Keyboard.Front(driver.MainWindow);
                Keyboard.SendControl('q');

                WatchTheExit(keePass, driver, main);
            }

            _pluginLog = _session.KeePass.ReadPluginLog();
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
        public void The_answer_was_refused_forever_and_held_from_memory()
        {
            Assert.That(_refused, Is.Not.Null);
            Assert.That(_refused.Status, Is.EqualTo(403), _refused.Failure ?? _refused.Body);
            Assert.That(_held, Is.True, _pluginLog);
        }

        [Test]
        public void The_box_names_the_answer_lost_with_the_main_window_off_the_screen()
        {
            Assert.That(_boxSeen, Is.True, "File, Exit showed no box about the owed answer");
            Assert.That(_boxText, Does.Contain("was never saved to its database"));
            Assert.That(_mainWindowVisibleWithTheBox, Is.False,
                "the box stood over a main window still listing the closed database");
        }

        [Test]
        public void The_log_holds_what_was_owed_and_KeePass_exited()
        {
            Assert.That(_pluginLog, Does.Match(" WARN .*" + OwedLine));
            Assert.That(_exited, Is.True, "KeePass was still running");
        }

        /// <summary>
        /// Saves the database's file again from outside KeePass, so that its bytes are no longer
        /// those KeePass read while nothing in it changed.
        /// </summary>
        private static void SaveTheFileAgain(ThrowawayKeePass keePass)
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(ThrowawayKeePass.DatabasePassword));

            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(keePass.DatabasePath), key, null);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        private static bool Logged(ThrowawayKeePass keePass, string line, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().IndexOf(line, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }

                Thread.Sleep(200);
            }

            return false;
        }

        /// <summary>
        /// Waits for the box, notes whether the main window is on the screen beside it, answers it,
        /// and waits for KeePass to go.
        /// </summary>
        private void WatchTheExit(ThrowawayKeePass keePass, KeePassDriver driver, IntPtr main)
        {
            var process = keePass.Process;
            var deadline = DateTime.UtcNow + DialogTimeout;

            while (!_boxSeen && DateTime.UtcNow < deadline && !process.HasExited)
            {
                var window = NativeWindows.Find(process, title => title == GateName.Full);
                if (window != null)
                {
                    _boxSeen = true;
                    var box = AutomationElement.FromHandle(window.Handle);
                    _boxText = Uia.Describe(box, ControlType.Text);
                    _mainWindowVisibleWithTheBox = TestNativeMethods.IsWindowVisible(main);
                    Assert.That(driver.PressButton(box, "OK"), Is.True, driver.Diagnostics);
                }
                else
                {
                    Thread.Sleep(200);
                }
            }

            _exited = process.WaitForExit(20000);
        }
    }
}
