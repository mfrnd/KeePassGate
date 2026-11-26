using System;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Authority;
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
    /// A renewal refused forever whose write is refused once the operator has answered: the
    /// answer holds for this session, the plugin's message states so once the dialog has closed,
    /// and nothing is left for a later save or for KeePass's exit.
    /// </summary>
    /// <remarks>
    /// **The cause is the database's file replaced underneath the open renewal dialog**, saved
    /// again from outside so that its bytes are not those KeePass read. The questions put
    /// before the dialog have passed by then, and the answer's write, which saves as Approve's
    /// record does, refuses the save KeePass would stop to ask about. It is the cause of the
    /// writes this tree keeps in the custom data that a fixture can make at a moment of its own
    /// choosing, and the same every run.
    ///
    /// Only the real window shows the message's place: up once the renewal dialog has gone, over
    /// KeePass's main window, under the plugin's name, which is why this is in the end to end tier.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveRefusalNotWrittenTests
    {
        private const string HeldLine = "and the answer was not written, so it holds until KeePass exits";

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private string _key;
        private LiveReply _refused;
        private bool _dialogGoneBeforeTheMessage;
        private bool _boxSeen;
        private string _boxText;
        private LiveReply _again;
        private string _titleAfter;
        private bool _askedAtExit;
        private bool _toldAtExit;
        private bool _exited;
        private bool _fileHoldsNoAnswer;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            using (var renewing = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(renewing, "reader");
                _key = CertificateDigest.Of(renewing);

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
                var dialogWindow = new IntPtr(dialog.Current.NativeWindowHandle);

                SaveTheFileAgain(keePass);

                var menu = driver.OpenRefuseMenu(dialog);
                Assert.That(menu, Is.Not.Null, driver.Diagnostics);
                var choice = Uia.Descendant(menu, ControlType.MenuItem, text => text == GateTab.RefuseForeverText);
                Assert.That(choice, Is.Not.Null, "the menu offers no forever: " + Uia.Inventory(menu));
                Mouse.Click(choice);

                Assert.That(asking.Join(DialogTimeout), Is.True, "the client was still waiting");

                var box = WaitForTheMessage(keePass, main);
                if (box != null)
                {
                    _boxSeen = true;
                    _dialogGoneBeforeTheMessage = !TestNativeMethods.IsWindowVisible(dialogWindow);
                    _boxText = Uia.Describe(box, ControlType.Text);
                    Assert.That(driver.PressButton(box, "OK"), Is.True, driver.Diagnostics);
                }

                _again = LiveGateClient.Send(
                    keePass.Port, keePass.NewGateName(), renewing, "POST", RenewalHandler.Path, "{}",
                    LiveGateClient.OnTheOperator);
                _titleAfter = LiveRules.Title(driver);

                Keyboard.Front(driver.MainWindow);
                Keyboard.SendControl('q');
                WatchTheExit(keePass, main);
            }

            _fileHoldsNoAnswer = _session.KeePass.ReadStandingRefusal(_key) == null;
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
        public void The_client_is_refused_as_the_operator_answered()
        {
            Assert.That(_refused, Is.Not.Null);
            Assert.That(_refused.Status, Is.EqualTo(403), _refused.Failure ?? _refused.Body);
        }

        [Test]
        public void The_message_comes_up_once_the_renewal_dialog_has_closed_and_names_the_reason()
        {
            Assert.That(_boxSeen, Is.True, "no message of the plugin's came up");
            Assert.That(_dialogGoneBeforeTheMessage, Is.True, "the message stood beside the renewal dialog");
            Assert.That(_boxText, Does.Contain(RenewalAnswerNotice.NotWrittenLead.Trim()));
            Assert.That(_boxText, Does.Contain("the file on disk is not the one KeePass read"));
            Assert.That(_boxText, Does.Contain(RenewalAnswerNotice.StaysRefused));
        }

        [Test]
        public void The_answer_holds_for_this_session_and_nothing_is_marked()
        {
            Assert.That(_again, Is.Not.Null);
            Assert.That(_again.Status, Is.EqualTo(409), _again.Failure ?? _again.Body);
            Assert.That(_again.Body, Does.Contain("this session"));
            Assert.That(LiveRules.Marked(_titleAfter), Is.False, "KeePass's title is '" + _titleAfter + "'");
            Assert.That(_pluginLog, Does.Match(" WARN .*" + HeldLine));
        }

        [Test]
        public void KeePass_exits_with_no_question_and_no_message_and_the_file_holds_no_answer()
        {
            Assert.That(_askedAtExit, Is.False, "KeePass asked about unsaved changes as it exited");
            Assert.That(_toldAtExit, Is.False, "a message of the plugin's came up as KeePass exited");
            Assert.That(_exited, Is.True, "KeePass was still running");
            Assert.That(_fileHoldsNoAnswer, Is.True, "the file holds an answer nothing saved");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(_pluginLog, Does.Not.Contain(" ERROR "));
            Assert.That(_pluginLog, Does.Not.Contain(" UNEXPECTED "));
        }

        /// <summary>
        /// Saves the database's file again from outside KeePass, so that its bytes are not those
        /// KeePass read while nothing in it changed.
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

        /// <summary>The plugin's message, a window of KeePass's under the plugin's name, or null.</summary>
        private static AutomationElement WaitForTheMessage(ThrowawayKeePass keePass, IntPtr main)
        {
            return Uia.WaitForOrNull(
                () =>
                {
                    var window = NativeWindows.Find(keePass.Process, title => title == GateName.Full);
                    return window == null || window.Handle == main ? null : AutomationElement.FromHandle(window.Handle);
                },
                DialogTimeout);
        }

        /// <summary>
        /// Watches KeePass go, noting a question of KeePass's or a message of the plugin's on the
        /// way, and answers neither.
        /// </summary>
        private void WatchTheExit(ThrowawayKeePass keePass, IntPtr main)
        {
            var process = keePass.Process;
            var deadline = DateTime.UtcNow + DialogTimeout;

            while (!process.HasExited && DateTime.UtcNow < deadline)
            {
                if (NativeWindows.Find(process, title => title == GateName.Full) != null)
                {
                    _toldAtExit = true;
                    break;
                }

                if (NativeWindows.Visible(process).Any(shown => shown.Title == "KeePass" && shown.Handle != main))
                {
                    _askedAtExit = true;
                }

                if (_askedAtExit)
                {
                    break;
                }

                Thread.Sleep(200);
            }

            _exited = process.WaitForExit(20000);
        }
    }
}
