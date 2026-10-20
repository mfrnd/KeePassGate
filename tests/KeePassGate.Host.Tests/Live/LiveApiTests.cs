using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Audit;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for the API: a real client creating a real entry in a real KeePass, and
    /// finding it in the file after the process has gone.
    /// </summary>
    /// <remarks>
    /// Nothing here is a stand-in. KeePass is a separate process, the client is an ordinary
    /// `SslStream` in this one, and the certificate it presents was issued by the authority inside
    /// the database file, which this test can do because it holds the file and its password.
    ///
    /// What it proves that the loopback tests cannot: that a write through the gate reaches
    /// KeePass's own save, on KeePass's own thread, and is on disk when the answer comes back. And
    /// that the rules are what let it: the same client, one grant away, is told there is nothing
    /// there.
    ///
    /// The grant and the admission record are both written before KeePass starts, because a
    /// running KeePass serves the database it holds in memory and would overwrite either.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [NonParallelizable]
    public sealed class LiveApiTests
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

        private const string Title = "an invented service";
        private const string Custom = "Invented";

        private LiveSession _session;

        private LiveReply _created;
        private LiveReply _read;
        private LiveReply _listed;
        private LiveReply _addedField;
        private LiveReply _refused;
        private bool _exited;
        private string _pluginLog;
        private string _uuid;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            // One profile, granted everything on the root group. The refusal at the end uses a
            // second client whose record names a profile no rule mentions, which is the
            // intersection failing in the other direction.
            // `protected` among them, because this client writes a password and reads it back. A
            // rule without it would be refused at the write and answered with `null` at the read,
            // which is the other half of this and is proved against the fake rather than here.
            keePass.Grant("{\"reader\":[\"create\",\"read\",\"update\",\"delete\",\"list\",\"protected\"]}");

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            using (var stranger = keePass.IssueClientCertificate("agent-no-rules"))
            {
                keePass.Admit(admitted, "reader");
                keePass.Admit(stranger, "nobody");

                var process = keePass.Start();
                WaitUntilServing(keePass, process);

                _created = Send(keePass, admitted, "POST", "/groups/root/entries",
                    "{\"Title\":\"" + Title + "\",\"Password\":\"not-a-real-password\"}");

                _uuid = Uuid(_created);

                _addedField = Send(keePass, admitted, "POST", "/entries/" + _uuid + "/fields",
                    "{\"name\":\"" + Custom + "\",\"value\":\"a value\"}");

                _read = Send(keePass, admitted, "GET", "/entries/" + _uuid, null);
                _listed = Send(keePass, admitted, "GET", "/entries", null);

                // The same request, from a client whose record names a profile no rule mentions.
                _refused = Send(keePass, stranger, "GET", "/entries/" + _uuid, null);

                process.CloseMainWindow();
                _exited = process.WaitForExit(30000);
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
        public void An_entry_created_through_the_gate_is_in_the_file_afterwards()
        {
            // The whole claim. A client told that something was written can believe it, because the
            // write went through KeePass's own save and this reads the file with KeePass gone.
            Assert.That(_created.Answered, Is.True, _created.Failure);
            Assert.That(_created.Status, Is.EqualTo(201), _created.Body);
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");

            var entries = _session.KeePass.ReadEntries();
            var made = entries.FirstOrDefault(e => e.Uuid.ToHexString() == _uuid);

            Assert.That(made, Is.Not.Null, "the entry is not in the file");
            Assert.That(made.Strings.ReadSafe("Title"), Is.EqualTo(Title));
            Assert.That(made.Strings.ReadSafe("Password"), Is.EqualTo("not-a-real-password"));
        }

        [Test]
        public void A_field_added_afterwards_is_in_the_file_too()
        {
            Assert.That(_addedField.Status, Is.EqualTo(201), _addedField.Body);

            var made = _session.KeePass.ReadEntries().First(e => e.Uuid.ToHexString() == _uuid);

            Assert.That(made.Strings.ReadSafe(Custom), Is.EqualTo("a value"));
        }

        [Test]
        public void Reading_it_back_over_the_gate_gives_what_was_written()
        {
            Assert.That(_read.Status, Is.EqualTo(200), _read.Body);

            // Not read back through `JsonBody`, which is the request reader and refuses nesting on
            // purpose. An entry's answer carries a `fields` object, which is exactly the shape
            // `JsonWriter` exists for and the shape the other direction is not allowed to send.
            Assert.That(_read.Body, Does.Contain("\"fields\":{"));
            Assert.That(_read.Body, Does.Contain(Title));
            Assert.That(_read.Body, Does.Contain("not-a-real-password"));
            Assert.That(_read.Body, Does.Contain("\"protected\":[\"Password\"]"));
        }

        [Test]
        public void The_entry_shows_up_in_a_listing()
        {
            Assert.That(_listed.Status, Is.EqualTo(200), _listed.Body);
            Assert.That(_listed.Body, Does.Contain(_uuid));
            Assert.That(_listed.Body, Does.Not.Contain("not-a-real-password"),
                "a listing says an entry exists and never what is in it");
        }

        [Test]
        public void A_client_whose_profiles_no_rule_mentions_is_told_there_is_nothing_there()
        {
            // The intersection, from the other side: a perfectly good certificate with a perfectly
            // good admission record, and no rule naming what it holds.
            Assert.That(_refused.Answered, Is.True, _refused.Failure);
            Assert.That(_refused.Status, Is.EqualTo(404));
            Assert.That(_refused.Body, Does.Not.Contain(Title));
        }

        [Test]
        public void Every_decision_this_session_made_was_written_down()
        {
            // The production wiring, and the only test that reaches it. `StartListening` is the one
            // place that hands the router an audit which keeps anything; the convenience overload
            // every fixture uses supplies an `AccessCheck` of its own over an audit that keeps
            // nothing. So taking that argument away leaves the router authorizing identically and
            // changes nothing about who may reach what. The whole suite went on passing while the
            // plugin recorded not one decision. What is lost is silent, which is the single
            // thing an audit exists to make impossible.
            //
            // The session is over by the time any of these run, and a line is appended and closed
            // per decision, so the file is complete rather than merely started.
            var path = _session.KeePass.AuditPath;

            Assert.That(File.Exists(path), Is.True, "the plugin recorded no decision at all");

            var lines = File.ReadAllLines(path);

            // Both directions, because an audit that only kept refusals would say nothing about
            // what an agent actually reached, and one that only kept grants would be useless for
            // the question anybody opens it to ask.
            Assert.That(lines.Any(l => l.Contains("\"decision\":\"" + AuditEntry.Granted + "\"")),
                Is.True, "the permitted requests of this session are not in it");
            Assert.That(lines.Any(l => l.Contains("\"decision\":\"" + AuditEntry.Refused + "\"")),
                Is.True, "the refusal of this session is not in it");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private static string Uuid(LiveReply reply)
        {
            var body = JsonBody.Read(reply.Body);
            return body == null ? null : body.Text("uuid");
        }

        private static LiveReply Send(
            ThrowawayKeePass keePass, X509Certificate2 certificate, string method, string path, string body)
        {
            return LiveGateClient.Send(
                keePass.Port, keePass.NewGateName(), certificate, method, path, body);
        }

        /// <summary>
        /// Ready is the database being served, not the socket being open.
        /// </summary>
        /// <remarks>
        /// The listener starts inside the plugin's `Initialize`, and KeePass opens the database it
        /// was given on its command line only once its plugins are initialized. In between, the
        /// resolver has no target for the rotating name, so the connection is dropped before any
        /// TLS answer and the client reports the remote party closing the stream. That is the gate
        /// being fail-closed and is correct; a fixture that sends on `listening on` is racing the
        /// startup, and on a cold machine it loses. `serving a database with authority` is written
        /// once the target is built and cached, which is the first moment a name resolves.
        ///
        /// Both lines, in that order, because the serving line on its own would not tell a gate
        /// that never started from one that started and has nothing to serve yet.
        /// </remarks>
        private static void WaitUntilServing(ThrowawayKeePass keePass, System.Diagnostics.Process process)
        {
            WaitForLog(keePass, process, "listening on 127.0.0.1:" + keePass.Port,
                "the plugin never reported a listening gate");

            WaitForLog(keePass, process, "serving a database with authority",
                "the plugin never reported serving the database KeePass was started with");
        }

        private static void WaitForLog(
            ThrowawayKeePass keePass, System.Diagnostics.Process process, string marker, string failure)
        {
            var deadline = DateTime.UtcNow + StartTimeout;

            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    Assert.Fail("KeePass exited first: " + failure);
                }

                if (keePass.ReadPluginLog().IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return;
                }

                Thread.Sleep(500);
            }

            Assert.Fail(failure + ":" + Environment.NewLine + keePass.ReadPluginLog());
        }
    }
}
