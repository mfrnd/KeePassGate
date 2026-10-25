using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// `revokeCertificate`: a client giving up its own admission, through the router this plugin
    /// runs, against a database whose writes can be told what to answer.
    /// </summary>
    [TestFixture]
    public sealed class RevocationOperationTests
    {
        private TestAuthority _authority;
        private RecordingAudit _audit;
        private SpecRouter _router;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _audit = new RecordingAudit();
            _router = GateOperations.Router("9.9.9", null, new AccessCheck(_audit));
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Dispose();
        }

        [Test]
        public void A_client_that_revokes_itself_is_told_so_and_is_no_longer_admitted()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var context = Revoking(Paired(certificate));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(context.Json, Is.EqualTo("{\"ok\":true}"));
                Assert.That(_authority.Admission(CertificateDigest.Of(certificate)), Is.Null,
                    "the certificate it presented is the one revoked");
            }
        }

        [Test]
        public void Only_the_certificate_presented_is_revoked()
        {
            using (var asking = _authority.IssueAdmittedClient("reader"))
            using (var other = _authority.IssueAdmittedClient("reader"))
            {
                _router.Handle(Revoking(Paired(asking)));

                Assert.That(_authority.Admission(CertificateDigest.Of(other)), Is.Not.Null);
            }
        }

        [TestCase("busy", 503, "busy")]
        [TestCase("unsaved", 500, "not saved")]
        [TestCase("unverified", 500, "not verified")]
        [TestCase("failed", 500, "internal error")]
        [TestCase("missing", 401, "unpaired")]
        public void What_the_write_came_to_is_what_the_client_is_told(string outcome, int status, string error)
        {
            // In the vault writes' words. A record that went before the write could make it is the
            // one case of its own: the operator revoked it in between, so the answer is the one
            // this certificate will get on every connection from now on.
            _authority.RevokeAnswers = Outcome(outcome);

            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var context = Revoking(Paired(certificate));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(status), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(error)));
            }
        }

        [Test]
        public void A_revocation_that_took_is_written_down_by_the_key_it_took_away()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                _router.Handle(Revoking(Paired(certificate)));

                var line = _audit.Entries.Single();
                Assert.That(line.OperationId, Is.EqualTo(GateOperations.RevokeCertificate));
                Assert.That(line.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)));
                Assert.That(line.Decision, Is.EqualTo(AuditEntry.Revoked));
                Assert.That(line.Reason, Is.EqualTo(AdmissionAudit.RevokedItselfReason));
                Assert.That(line.DatabaseId, Is.EqualTo(_authority.DatabaseId));
            }
        }

        [Test]
        public void A_revocation_names_the_client_the_certificate_belonged_to()
        {
            // A client that renewed gives up a certificate that is not the one it paired with, and
            // the line names the client by the key its record carried, which the credential check
            // read as the connection was admitted.
            var pairedWith = new string('c', 64);

            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                _router.Handle(Revoking(
                    ClientIdentity.Paired(_authority.DatabaseId, certificate, new[] { "reader" }, null, pairedWith)));

                var line = _audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)));
                Assert.That(line.ClientKey, Is.EqualTo(pairedWith));
            }
        }

        [Test]
        public void A_revocation_kept_in_memory_and_not_verified_is_written_down_too()
        {
            // The database KeePass holds refuses the certificate from then on, whatever the file
            // turns out to say, so the revocation took.
            _authority.RevokeAnswers = Outcome("unverified");

            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                _router.Handle(Revoking(Paired(certificate)));

                Assert.That(_audit.Entries.Single().Decision, Is.EqualTo(AuditEntry.Revoked));
            }
        }

        [Test]
        public void A_listener_that_fails_after_the_revocation_landed_does_not_lose_its_line()
        {
            // The writer writes the line before it tells the Clients page, so a defect there goes
            // to the connection thread's catch-all with the revocation already written down, and
            // the operation writes no second line of its own.
            _authority.WhenLanded = () => { throw new NullReferenceException("a listener's defect"); };

            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                Assert.Throws<NullReferenceException>(() => _router.Handle(Revoking(Paired(certificate))));

                var line = _audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)));
                Assert.That(line.Reason, Is.EqualTo(AdmissionAudit.RevokedItselfReason));
            }
        }

        [TestCase("busy")]
        [TestCase("unsaved")]
        [TestCase("failed")]
        [TestCase("missing")]
        public void A_revocation_that_did_not_take_is_not_written_down(string outcome)
        {
            // Taken back out, never made, or made by somebody else first: none of these revoked
            // anything here, and the operator's own revocation is written down where it lands.
            _authority.RevokeAnswers = Outcome(outcome);

            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                _router.Handle(Revoking(Paired(certificate)));

                Assert.That(_audit.Entries, Is.Empty);
            }
        }

        [Test]
        public void A_connection_with_no_database_behind_it_is_this_plugin_failing()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var context = new GateContext(Head("DELETE", 0), string.Empty, Paired(certificate));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(500));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("internal error")));
            }
        }

        [Test]
        public void A_caller_with_no_admission_is_refused_before_anything_is_written()
        {
            var context = new GateContext(
                Head("DELETE", 0), string.Empty, ClientIdentity.Stranger(_authority.DatabaseId));
            new ServedDatabase(null, _authority).PutOn(context);
            _authority.RevokeAnswers = VaultWrite.Failed("nothing should have asked");

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
        }

        [Test]
        public void A_body_is_refused_as_a_target_the_document_does_not_declare()
        {
            // It takes no body, so there is no way to name a certificate other than the one
            // presented, and one sent anyway is answered as every undeclared target is.
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                const string body = "{\"certificate\":\"somebody else's\"}";
                var context = new GateContext(Head("DELETE", Bodies.LengthOf(body)), body, Paired(certificate));
                new ServedDatabase(null, _authority).PutOn(context);

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(404), context.Json);
                Assert.That(_authority.Admission(CertificateDigest.Of(certificate)), Is.Not.Null,
                    "nothing was revoked");
            }
        }

        [Test]
        public void Any_other_method_is_told_the_one_this_path_serves()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var context = new GateContext(Head("GET", 0), string.Empty, Paired(certificate));
                new ServedDatabase(null, _authority).PutOn(context);

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(405));
                Assert.That(context.Headers["Allow"], Is.EqualTo("DELETE"));
            }
        }

        private GateContext Revoking(ClientIdentity client)
        {
            var context = new GateContext(Head("DELETE", 0), string.Empty, client);
            new ServedDatabase(null, _authority).PutOn(context);
            return context;
        }

        private ClientIdentity Paired(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
        {
            return ClientIdentity.Paired(_authority.DatabaseId, certificate, new[] { "reader" });
        }

        private static HttpRequestHead Head(string method, long length)
        {
            return new HttpRequestHead(
                method,
                GateOperations.PathOf(GateOperations.RevokeCertificate),
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                length);
        }

        private static VaultWrite Outcome(string outcome)
        {
            switch (outcome)
            {
                case "busy":
                    return VaultWrite.Busy("a dialog is open");
                case "unsaved":
                    return VaultWrite.Unsaved("the save did not reach the disk");
                case "unverified":
                    return VaultWrite.Unverified("the file does not hold what was written");
                case "failed":
                    return VaultWrite.Failed("the database closed");
                default:
                    return VaultWrite.Missing();
            }
        }
    }
}
