using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// KeePass's own attachment editor open on an attachment, and the API asked to remove it.
    /// </summary>
    /// <remarks>
    /// **The editor holds the attachment and nothing about the entry.** KeePass's main window opens
    /// it with the entry kept on its own stack, and when the editor closes changed it writes the
    /// edited bytes back onto that entry, whatever happened to the entry meanwhile. A removal that
    /// landed underneath it was answered as done and then undone by the operator's save, and an
    /// update was overwritten, with nothing to say either had happened.
    ///
    /// **Typed into rather than built and registered**, which is how `VaultWritesUnderDialogsTests`
    /// asks about the dialogs it can stand up: the write-back is KeePass's main window's, after
    /// the editor's modal loop returns, so only a running KeePass shows the write being undone.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveAttachmentEditorTests
    {
        private const string Attachment = "invented-notes.txt";

        private const string Original = "Nothing in this file is real.";

        private static readonly string AttachmentPath =
            "/entries/" + ThrowawayKeePass.MailboxUuid + "/attachments/" + Attachment;

        private LiveSession _session;
        private LiveReply _whileOpen;
        private bool _closed;
        private LiveReply _afterClosing;
        private LiveReply _removedAfterwards;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            keePass.FillWithInventedContent();
            keePass.AttachToTheMailbox(Attachment, Encoding.UTF8.GetBytes(Original));

            // `attachments` because the route is about one, and `strip` because removing one takes
            // a part out of the entry rather than the entry out of its group.
            keePass.Grant(Rules.Granting(("agent", "CRUSDLA")));

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, "agent");

                var driver = KeePassDriver.Start(keePass);
                LiveApiTests.WaitUntilServing(keePass, keePass.Process);

                var editor = driver.OpenAttachmentEditor(Attachment);
                _whileOpen = Send(keePass, admitted, "DELETE", AttachmentPath);

                _closed = driver.ChangeInAttachmentEditor(editor, Attachment, 'x');
                _afterClosing = Send(keePass, admitted, "GET", AttachmentPath);
                _removedAfterwards = Send(keePass, admitted, "DELETE", AttachmentPath);

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

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
        public void A_removal_while_the_editor_is_open_is_refused_as_busy()
        {
            Assert.That(_whileOpen.Answered, Is.True, _whileOpen.Failure);
            Assert.That(_whileOpen.Status, Is.EqualTo(503),
                "answered " + _whileOpen.Status + " " + _whileOpen.Body +
                ", and the editor's close then put the attachment back: " + _afterClosing.Body);
            Assert.That(_whileOpen.Body, Does.Contain("\"busy\""));
        }

        [Test]
        public void What_the_operator_saved_in_the_editor_is_what_the_attachment_holds()
        {
            Assert.That(_closed, Is.True, "the attachment editor did not close");
            Assert.That(_afterClosing.Status, Is.EqualTo(200), _afterClosing.Body);

            var held = Encoding.UTF8.GetString(Convert.FromBase64String((string)JObject.Parse(_afterClosing.Body)["content"]));
            Assert.That(held, Does.Contain(Original).And.Not.EqualTo(Original), "the letter typed into the editor is not in it");
        }

        [Test]
        public void Once_the_editor_is_closed_the_removal_goes_ahead()
        {
            Assert.That(_removedAfterwards.Status, Is.EqualTo(200), _removedAfterwards.Body);
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");

            var mailbox = _session.KeePass.ReadEntries().First(e => e.Uuid.ToHexString() == ThrowawayKeePass.MailboxUuid);
            Assert.That(mailbox.Binaries.Get(Attachment), Is.Null, "the attachment is still in the file");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at_but_the_busy_removal()
        {
            var failures = LiveSession.FailuresIn(_pluginLog);
            var busy = failures.Where(line => line.Contains("a change was refused as busy")).ToArray();
            Assert.That(busy, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(failures.Except(busy), Is.Empty);
        }

        private static LiveReply Send(ThrowawayKeePass keePass, X509Certificate2 certificate, string method, string path)
        {
            return LiveGateClient.Send(keePass.Port, keePass.NewGateName(), certificate, method, path, null);
        }
    }
}
