using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Spec;
using KeePassGate.Store;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A client acting on its own admission against a real KeePass: giving its own certificate up.
    /// </summary>
    /// <remarks>
    /// What the loopback tests cannot settle: that the record goes through KeePass's own save on
    /// KeePass's own thread, that the very next connection is refused by a KeePass that has
    /// reloaded nothing, and that the file holds the record once KeePass has gone.
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
                _exited = process.WaitForExit(30000) && process.ExitCode == 0;
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
        public void The_certificate_given_up_is_taken_away_in_the_file_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");
            Assert.That(_session.KeePass.ReadAdmissions().Select(record => record.AdmissionKey),
                Has.No.Member(_revokedKey), "the certificate the client gave up admits nobody in the file");

            // Both halves, because the store lists neither an admission it deleted nor an
            // admission a record takes away, and only the record survives a merge.
            var keePass = _session.KeePass;
            Assert.That(keePass.FileHolds(StoreKinds.Withdrawn, StoreKinds.Certificate, _revokedKey), Is.True,
                "the record of kind withdrawn is an entry of its own in the file");
            Assert.That(keePass.FileHolds(StoreKinds.Client, StoreKinds.Certificate, _revokedKey), Is.False,
                "and the write deleted the admission it refuses");
        }

        [Test]
        public void The_audit_wrote_the_revocation_down_by_the_key_it_took_away()
        {
            // The router this plugin runs hands the revocation the access check's own record of
            // admissions, which only a running plugin reaches.
            var revocations = File.ReadAllLines(_session.KeePass.AuditPath)
                .Where(line => line.Contains("\"operation\":\"" + GateOperations.RevokeCertificate + "\""))
                .Where(line => line.Contains("\"decision\":\"" + AuditEntry.Revoked + "\""))
                .ToArray();

            Assert.That(revocations, Has.Length.EqualTo(1));
            Assert.That(revocations[0], Does.Contain(
                "\"admissionKey\":\"" + _revokedKey + "\",\"clientKey\":\"" + _revokedKey + "\""),
                "a client that never renewed, so its client key is the certificate's own");
        }

        [Test]
        public void The_log_names_the_client_that_gave_its_certificate_up_and_only_the_refusal_after_it()
        {
            var revoked = CertificateDigest.Shorten(_revokedKey);
            Assert.That(_pluginLog, Does.Contain("a client gave up its own certificate: " + revoked));

            // The connection after the certificate is given up is refused on purpose, and the gate
            // reports a refusal at a level an operator looks at, naming the certificate. So that
            // line, once and for that certificate, and nothing else: filtering the phrase alone
            // would also pass a session in which the write never landed and every request was
            // refused.
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
