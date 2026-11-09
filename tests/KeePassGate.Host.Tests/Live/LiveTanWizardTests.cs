using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using KeePassGate.Testing;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// KeePass's TAN wizard open on a database, a write through the gate while it is, and its OK
    /// pressed once the write has been answered.
    /// </summary>
    /// <remarks>
    /// **The wizard adds its entries from inside its own OK**, and the save a write ends in
    /// handles KeePass's messages as it walks the tree, after every entry it writes once fifty
    /// milliseconds have passed. An OK handled during the walk of the group the wizard adds to
    /// moves the list the walk is in, which is why a write is refused while the wizard is open.
    ///
    /// **OK is pressed once the write is answered, and not after a while.** Pressed a fixed time
    /// after the write was sent, on a loaded machine it arrived before the write reached the
    /// writer: the wizard had closed by then, the write went ahead as it should with nothing open,
    /// and the fixture read the plugin as having let a write through. Waiting for the answer is
    /// what makes the write one made while the wizard is open. A plugin that let it through is
    /// still caught, by the answer that is not `503 busy`, though its save then ends before OK is
    /// pressed rather than being broken by it.
    ///
    /// **Pressed through UI automation, which reaches the window as a message.** A press with the
    /// mouse made while KeePass's thread was busy was never handled at all, measured, whereas a
    /// message waits for KeePass's next pump.
    ///
    /// The write is about a group the wizard knows nothing of, because what goes wrong is the save
    /// rather than the write.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveTanWizardTests
    {
        private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

        private static readonly string CreatePath = "/groups/" + ThrowawayKeePass.AutomationUuid + "/entries";

        private LiveSession _session;
        private LiveReply _whileOpen;
        private string _warning;
        private string _timeline;
        private bool _wizardClosed;
        private LiveReply _afterwards;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            keePass.FillWithInventedContent();
            keePass.Grant(Rules.Granting(("agent", "CRUSDLA")));

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, "agent");

                var driver = KeePassDriver.Start(keePass);
                LiveApiTests.WaitUntilServing(keePass, keePass.Process);

                // The root is what a freshly opened KeePass selects, so the wizard adds to it.
                var wizard = driver.OpenTanWizard();
                var ok = driver.PrepareTans(wizard, "1234 5678");

                // Named first, since naming the gate reads the file.
                var name = keePass.NewGateName();
                var sent = DateTime.UtcNow;
                var write = Task.Run(() => LiveGateClient.Send(
                    keePass.Port, name, admitted, "POST", CreatePath, Bodies.Of(("Title", "Invented"))));

                // A save that fails shows KeePass's own warning, and the write is answered only
                // once that is closed.
                var deadline = DateTime.UtcNow + AnswerTimeout;
                while (!write.Wait(250) && DateTime.UtcNow < deadline)
                {
                    var box = NativeWindows.Find(keePass.Process, title => title == "KeePass");
                    if (box != null && _warning == null)
                    {
                        var element = AutomationElement.FromHandle(box.Handle);
                        _warning = Uia.Inventory(element);
                        Keyboard.Front(element);
                        Keyboard.SendEnter();
                    }
                }

                _whileOpen = write.IsCompleted ? write.Result : new LiveReply { Failure = "no answer in time" };
                var answered = DateTime.UtcNow;

                var pressed = DateTime.UtcNow;
                var press = Task.Run(() => ok.Invoke());

                _wizardClosed = press.Wait(TimeSpan.FromSeconds(10)) && Uia.WaitForOrNull(
                    () => NativeWindows.Find(keePass.Process, title => title.Contains("TAN Wizard")) == null ? "closed" : null,
                    TimeSpan.FromSeconds(10)) != null;
                _timeline = "sent " + Moment(sent) + ", answered " + Moment(answered) + ", OK pressed " + Moment(pressed);

                _afterwards = Send(keePass, admitted, "POST", CreatePath, Bodies.Of(("Title", "Afterwards")));

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine(_timeline + ", KeePass's warning: " + (_warning ?? "none"));
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
        public void A_write_while_the_wizard_is_open_is_refused_as_busy()
        {
            Assert.That(_whileOpen.Answered, Is.True, _whileOpen.Failure);
            Assert.That(_whileOpen.Status, Is.EqualTo(503),
                "answered " + _whileOpen.Status + " " + _whileOpen.Body + ", " + _timeline + ", and KeePass showed: " + _warning);
            Assert.That(_whileOpen.Body, Does.Contain("\"busy\""));
        }

        [Test]
        public void KeePass_showed_no_warning_of_its_own()
        {
            Assert.That(_warning, Is.Null);
        }

        [Test]
        public void The_tans_and_the_write_made_once_the_wizard_closed_are_in_the_file()
        {
            Assert.That(_wizardClosed, Is.True, "the TAN wizard did not close");
            Assert.That(_afterwards.Status, Is.EqualTo(201), _afterwards.Body);
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");

            var entries = _session.KeePass.ReadEntries();
            Assert.That(entries.Count(e => e.Strings.ReadSafe(PwDefs.TitleField) == PwDefs.TanTitle), Is.EqualTo(2),
                "the operator's TANs are not in the file");
            Assert.That(entries.Any(e => e.Strings.ReadSafe(PwDefs.TitleField) == "Afterwards"), Is.True);
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at_but_the_busy_write()
        {
            var failures = LiveSession.FailuresIn(_pluginLog);
            var busy = failures.Where(line => line.Contains("a change was refused as busy")).ToArray();
            Assert.That(busy, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(failures.Except(busy), Is.Empty);
        }

        private static string Moment(DateTime at)
        {
            return at.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static LiveReply Send(
            ThrowawayKeePass keePass, X509Certificate2 certificate, string method, string path, string body)
        {
            return LiveGateClient.Send(keePass.Port, keePass.NewGateName(), certificate, method, path, body);
        }
    }
}
