using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A client acting on its own admission against a real KeePass: revoking its own certificate.
    /// </summary>
    /// <remarks>
    /// What the loopback tests cannot settle: that the revocation goes through KeePass's own save
    /// on KeePass's own thread, that the very next connection is refused by a KeePass that has
    /// reloaded nothing, and that the file holds the revocation once KeePass has gone.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [NonParallelizable]
    public sealed class LiveCertificateTests
    {
        private LiveSession _session;

        private LiveReply _before;
        private LiveReply _revoked;
        private LiveReply _after;
        private string _revokedKey;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            // Admitted before KeePass starts, because a running KeePass serves the database it
            // holds in memory and would overwrite a record written to the file underneath it.
            using (var leaving = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(leaving, "reader");
                _revokedKey = CertificateDigest.Of(leaving);

                var process = keePass.Start();
                LiveApiTests.WaitUntilServing(keePass, process);

                _before = Send(keePass, leaving, "GET", "/gate/status");
                _revoked = Send(keePass, leaving, "DELETE", "/gate/certificate");
                _after = Send(keePass, leaving, "GET", "/gate/status");

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
        public void A_client_that_revokes_itself_is_refused_on_its_next_connection()
        {
            Assert.That(_before.Status, Is.EqualTo(200), _before.Body);
            Assert.That(_revoked.Answered, Is.True, _revoked.Failure);
            Assert.That(_revoked.Status, Is.EqualTo(200), _revoked.Body);
            Assert.That(_after.Status, Is.EqualTo(401), "refused as a certificate the operator revoked is");
        }

        [Test]
        public void The_revocation_is_in_the_file_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");
            Assert.That(_session.KeePass.ReadAdmissions().Select(record => record.AdmissionKey),
                Has.No.Member(_revokedKey), "the record the client gave up admits nobody in the file");

            // Both halves, because the store lists neither an emptied record nor one with a
            // revocation beside it, and only the revocation survives a merge.
            var keePass = _session.KeePass;
            Assert.That(keePass.ReadCustomData(AuthorityKeys.RevocationKey(_revokedKey)), Is.Not.Null,
                "the revocation is a key of its own in the file");
            Assert.That(keePass.ReadCustomData(AuthorityKeys.ClientRecordKey(_revokedKey)), Is.Empty,
                "and the record beside it is emptied");
        }

        [Test]
        public void The_plugin_said_which_client_revoked_itself_and_reported_only_the_refusal_after_it()
        {
            var revoked = CertificateDigest.Shorten(_revokedKey);
            Assert.That(_pluginLog, Does.Contain("a client revoked its own certificate: " + revoked));

            // The connection after the revocation is refused on purpose, and the gate reports a
            // refusal at a level an operator looks at, naming the certificate. So that line, once
            // and for that certificate, and nothing else: filtering the phrase alone would also
            // pass a session in which the revocation never landed and every request was refused.
            var failures = LiveSession.FailuresIn(_pluginLog);
            var refusals = failures
                .Where(line => line.Contains("refused a certificate with no admission record: " + revoked))
                .ToArray();
            var unexpected = failures.Except(refusals).ToArray();

            Assert.That(refusals, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        private static LiveReply Send(
            ThrowawayKeePass keePass, X509Certificate2 certificate, string method, string path)
        {
            return LiveGateClient.Send(keePass.Port, keePass.NewGateName(), certificate, method, path, null);
        }
    }
}
