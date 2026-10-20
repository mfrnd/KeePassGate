using System;
using System.Collections.Generic;
using KeePassGate.Authority;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
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
        private SpecRouter _router;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _router = GateOperations.Router("9.9.9");
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
                var context = new GateContext(Head("DELETE", body.Length), body, Paired(certificate));
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

        private static HttpRequestHead Head(string method, int length)
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
