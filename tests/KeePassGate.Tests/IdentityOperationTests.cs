using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// `getIdentity`: who the caller is, as the database it reached admitted it, through the router
    /// this plugin runs and with the identity the gate itself makes of a certificate.
    /// </summary>
    [TestFixture]
    public sealed class IdentityOperationTests
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
        public void A_client_is_told_who_it_is_as_the_database_admitted_it()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious", "reader"))
            {
                _authority.Admit(certificate, "reader", "ops");

                var answer = Answered("GET", Identified(certificate));

                Assert.That((string)answer["subject"], Is.EqualTo("CN=agent-fictitious"));
                Assert.That((string)answer["admissionKey"], Is.EqualTo(CertificateDigest.Of(certificate)));
                Assert.That(answer["profiles"].Values<string>(), Is.EqualTo(new[] { "reader", "ops" }),
                    "the record's profiles, in the record's order, whatever the certificate claims");
                Assert.That(answer["removedProfiles"].Values<string>(), Is.Empty);
            }
        }

        [Test]
        public void The_key_is_the_one_the_operator_revokes_it_by()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var key = (string)Answered("GET", Identified(certificate))["admissionKey"];

                Assert.That(_authority.Admission(key), Is.Not.Null, "the record is stored under it");
                Assert.That(_authority.Revoke(certificate), Is.True);
                Assert.That(_authority.Admission(key), Is.Null, "and revoking it is revoking that key");
            }
        }

        [Test]
        public void The_expiry_is_when_the_certificate_presented_stops_being_admitted()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious", TimeSpan.FromDays(30), "reader"))
            {
                _authority.Admit(certificate, "reader");

                var expires = (string)Answered("GET", Identified(certificate))["expires"];

                Assert.That(expires, Is.EqualTo(
                    Moments.Wire(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero))));
                Assert.That(expires, Does.EndWith("Z"), "RFC 3339 in UTC, as every moment this API answers with");
            }
        }

        [Test]
        public void A_profile_the_database_no_longer_names_is_told_apart_from_the_ones_it_holds()
        {
            // The record keeps every name it was approved for, and the rules are asked about only
            // the ones the database still names. Both halves are this client's own record.
            using (var certificate = _authority.IssueClient("agent-fictitious", "reader"))
            {
                _authority.Admit(certificate, "reader", "ops");
                _authority.Forget("ops");

                var answer = Answered("GET", Identified(certificate));

                Assert.That(answer["profiles"].Values<string>(), Is.EqualTo(new[] { "reader" }));
                Assert.That(answer["removedProfiles"].Values<string>(), Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void It_says_what_the_record_holds_and_nothing_else()
        {
            // What a rule grants at a node is a question about the node. Nothing about a group, an
            // entry or a rule is in here, and the members are exactly the ones the document requires.
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var answer = Answered("GET", Identified(certificate));

                Assert.That(
                    answer.Properties().Select(member => member.Name),
                    Is.EqualTo(new[] { "subject", "admissionKey", "expires", "profiles", "removedProfiles" }));
            }
        }

        [Test]
        public void A_HEAD_runs_what_the_GET_runs()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var context = Asking("HEAD", Identified(certificate));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(200));
            }
        }

        [Test]
        public void A_caller_with_no_admission_is_told_what_every_such_caller_is_told()
        {
            var context = Asking("GET", ClientIdentity.Stranger(_authority.DatabaseId));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
        }

        [Test]
        public void A_revoked_certificate_is_refused_and_written_down_as_on_every_route()
        {
            // It needs an admitted certificate, so one the operator revoked is a stranger here as
            // anywhere, and the refusal is recorded against the key the Clients page revoked.
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                _authority.Revoke(certificate);

                var context = Asking("GET", Identified(certificate));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(401));
                Assert.That(_audit.Entries.Single().OperationId, Is.EqualTo(GateOperations.Identity));
                Assert.That(_audit.Entries.Single().AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)));
            }
        }

        [Test]
        public void An_admitted_client_asking_is_never_written_down()
        {
            // No rule is asked, as for every operation declaring `none`, so there is no decision to
            // record, and a line for it would bury the ones that are.
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                Answered("GET", Identified(certificate));

                Assert.That(_audit.Entries, Is.Empty);
            }
        }

        [Test]
        public void Any_other_method_is_told_the_two_this_path_serves()
        {
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            {
                var context = Asking("POST", Identified(certificate));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(405));
                Assert.That(context.Headers["Allow"], Is.EqualTo("GET, HEAD"));
            }
        }

        [Test]
        public void A_connection_that_arrived_on_an_invitation_is_not_told()
        {
            // A connection on an invitation reaches the pairing routes and nothing else, whatever
            // certificate it carries: what it is told here is what an undeclared path tells it.
            using (var certificate = _authority.IssueAdmittedClient("reader"))
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, NullGateLog.Instance))
            {
                var context = Asking("GET", Identified(certificate));

                new PairingConnection(
                    book.Invite(_authority.DatabaseId),
                    _authority,
                    PairingProof.Binding(_authority.Host.CertificateDer),
                    new byte[SniName.NonceLength],
                    "127.0.0.1:60001").PutOn(context);

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(404));
            }
        }

        /// <summary>What the gate makes of a certificate, which is what this route reports.</summary>
        private ClientIdentity Identified(X509Certificate2 certificate)
        {
            return ClientCredentials.For(certificate, _authority, NullGateLog.Instance);
        }

        private JObject Answered(string method, ClientIdentity client)
        {
            var context = Asking(method, client);

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);

            // Read as written. Json.NET otherwise turns anything shaped like a moment into a
            // `DateTime` and gives it back in the current culture's spelling.
            using (var reader = new JsonTextReader(new StringReader(context.Json)) { DateParseHandling = DateParseHandling.None })
            {
                return JObject.Load(reader);
            }
        }

        private static GateContext Asking(string method, ClientIdentity client)
        {
            var head = new HttpRequestHead(
                method,
                GateOperations.PathOf(GateOperations.Identity),
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);

            return new GateContext(head, string.Empty, client);
        }
    }
}
