using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The whole API for now: one route, one refusal before dispatch, and a terminal 404. The
    /// shape is what matters. TODO(step 11): step 11 replaces the contents and keeps it.
    /// </summary>
    [TestFixture]
    public sealed class GateRouterTests
    {
        private GateRouter _router;

        [SetUp]
        public void SetUp()
        {
            _router = new GateRouter("9.9.9");
        }

        [Test]
        public void A_paired_client_asking_for_the_status_gets_it()
        {
            var context = Request("GET", GateRouter.StatusPath, Paired("db-digest"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(context.Json, Does.Contain("\"ok\":true"));
            Assert.That(context.Json, Does.Contain("\"version\":\"9.9.9\""));
        }

        [Test]
        public void The_status_names_the_database_the_connection_reached()
        {
            var databaseId = new string('a', 64);
            var context = Request("GET", GateRouter.StatusPath, Paired(databaseId));

            _router.Handle(context);

            Assert.That(context.Json, Does.Contain("\"database\":\"" + CertificateDigest.Shorten(databaseId) + "\""));
        }

        [Test]
        public void An_unpaired_client_is_refused_before_anything_is_dispatched()
        {
            var context = Request("GET", GateRouter.StatusPath, ClientIdentity.Stranger("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Does.Contain("unpaired"));
        }

        [Test]
        public void An_unpaired_client_is_refused_on_every_path_including_ones_that_do_not_exist()
        {
            foreach (var path in new[] { "/", "/gate/status", "/pair/request", "/anything" })
            {
                var context = Request("GET", path, ClientIdentity.Stranger("db"));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(401), "on " + path);
            }
        }

        [Test]
        public void A_path_that_does_not_exist_is_a_404()
        {
            var context = Request("GET", "/secrets", Paired("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404));
        }

        [TestCase("POST")]
        [TestCase("PUT")]
        [TestCase("DELETE")]
        public void The_status_route_answers_to_GET_and_nothing_else(string method)
        {
            var context = Request(method, GateRouter.StatusPath, Paired("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(405));

            // RFC 9110 section 15.5.6 requires it, and a client that cannot see what the path
            // does serve has to find out by asking, one method at a time.
            Assert.That(context.Headers, Is.Not.Null, "a 405 has to say what is allowed");
            Assert.That(context.Headers["Allow"], Is.EqualTo("GET"));
        }

        [Test]
        public void Nothing_it_answers_says_anything_about_the_database_contents()
        {
            var context = Request("GET", GateRouter.StatusPath, Paired("db"));

            _router.Handle(context);

            Assert.That(context.Json, Does.Not.Contain("password").IgnoreCase);
            Assert.That(context.Json.Length, Is.LessThan(200));
        }

        [Test]
        public void A_missing_context_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => _router.Handle(null));
        }

        private static ClientIdentity Paired(string databaseId)
        {
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired(databaseId, client, new List<string>());
            }
        }

        private static GateContext Request(string method, string path, ClientIdentity client)
        {
            var head = new HttpRequestHead(
                method,
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);

            return new GateContext(head, string.Empty, client);
        }
    }

    /// <summary>
    /// Turning a presented certificate into an identity, which is the whole of authentication
    /// until the admission record lands. TODO(step 7): that is step 7.
    /// </summary>
    [TestFixture]
    public sealed class ClientCredentialsTests
    {
        private TestAuthority _ours;
        private TestAuthority _theirs;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _ours = TestAuthority.New();
            _theirs = TestAuthority.New();
            _log = new RecordingGateLog();
        }

        [TearDown]
        public void TearDown()
        {
            _ours.Dispose();
            _theirs.Dispose();
        }

        [Test]
        public void A_certificate_from_this_database_is_paired()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious"))
            {
                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.True);
                Assert.That(identity.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(identity.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)));
                Assert.That(identity.DatabaseId, Is.EqualTo(_ours.DatabaseId));
            }
        }

        [Test]
        public void It_carries_no_profiles_yet_whatever_the_certificate_claims()
        {
            // Profiles come from the admission record, never from the certificate, and there are
            // no records yet. TODO(step 7): they land in step 7.
            using (var certificate = _ours.IssueClient("agent-fictitious", "ops", "readonly"))
            {
                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.Profiles, Is.Empty);
            }
        }

        [Test]
        public void A_certificate_from_another_authority_is_a_stranger()
        {
            using (var certificate = _theirs.IssueClient("agent-fictitious"))
            {
                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(_log.Warnings, Is.Not.Empty);
            }
        }

        [Test]
        public void A_host_certificate_presented_as_a_client_is_a_stranger()
        {
            // The same authority signs both, and hostname verification is deliberately abandoned,
            // so the extended key usage is the only thing that separates them.
            var identity = ClientCredentials.For(_ours.HostCertificateAsClient(), _ours, _log);

            Assert.That(identity.IsPaired, Is.False);
        }

        [Test]
        public void No_certificate_at_all_is_a_stranger_rather_than_an_error()
        {
            var identity = ClientCredentials.For(null, _ours, _log);

            Assert.That(identity.IsPaired, Is.False);
            Assert.That(identity.DatabaseId, Is.EqualTo(_ours.DatabaseId));
            Assert.That(identity.Subject, Is.Null);
        }

        [Test]
        public void A_certificate_from_a_foreign_authority_is_refused()
        {
            // A real client certificate, carrying `clientAuth`, issued by an authority that is not
            // this database's. The usage check would pass it, so the only thing that can refuse it
            // is the anchor pin, and this fails if the pin is removed or reordered behind a check
            // this certificate satisfies. Asserted with a certificate the usage check accepts on
            // purpose: shown one with no usage at all, this passes whichever check runs first.
            using (var foreign = TestAuthority.New())
            using (var certificate = foreign.IssueClient("agent-elsewhere"))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(certificate), Is.True,
                    "the usage check has to accept it, or the pin is not what refused it");

                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.False);
            }
        }

        [Test]
        public void A_certificate_with_no_extended_key_usage_is_refused()
        {
            // Unconstrained is not the same as permitted. Asked of the check itself, because a
            // certificate carrying the wrong usage and the right issuer is not something the
            // authority will issue, and routing this through a connection would only ever prove
            // that the anchor pin runs first.
            using (var unconstrained = SelfSigned(null))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(unconstrained), Is.False);
            }
        }

        [Test]
        public void A_certificate_for_serving_rather_than_connecting_is_refused()
        {
            // The other half of the same rule: an extended key usage that is present and says
            // something else is a certificate for a different job.
            using (var serving = SelfSigned("1.3.6.1.5.5.7.3.1"))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(serving), Is.False);
            }
        }

        [Test]
        public void A_certificate_that_says_client_authentication_passes_the_usage_check()
        {
            using (var connecting = SelfSigned(AuthorityIssuer.ClientAuthOid))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(connecting), Is.True);
            }
        }

        /// <summary>
        /// A certificate carrying one extended key usage, or none at all when given null.
        /// </summary>
        /// <remarks>
        /// Built here rather than issued, because the authority always writes `clientAuth` into a
        /// client certificate and there is no way to ask it for one without.
        /// </remarks>
        private static X509Certificate2 SelfSigned(string usageOid)
        {
            using (var key = System.Security.Cryptography.RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=invented",
                    key,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                if (usageOid != null)
                {
                    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                        new System.Security.Cryptography.OidCollection { new System.Security.Cryptography.Oid(usageOid) }, false));
                }

                return request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            }
        }
    }
}
