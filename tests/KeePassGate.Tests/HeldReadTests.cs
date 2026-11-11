using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Diagnostics;
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
    /// A request that changes nothing is decided and served inside one hold: the client's record,
    /// the rules and what is read are all of the database at one moment.
    /// </summary>
    /// <remarks>
    /// Through the real router, against a database in a dictionary and an authority that admits
    /// the client, so that what the operator does between the connection and the request is a
    /// line in a test. `DecidedAtTheReadTests` asks the same of the real vault.
    ///
    /// Everything is invented: the groups, the entries, the profiles.
    /// </remarks>
    [TestFixture]
    public sealed class HeldReadTests
    {
        /// <summary>Every path a `get` is declared on that a rule governs.</summary>
        private static readonly string[] Paths =
        {
            "/groups",
            "/groups/{group}",
            "/groups/{group}/groups",
            "/groups/{group}/entries",
            "/groups/{group}/access",
            "/entries",
            "/entries/{entry}",
            "/entries/{entry}/fields",
            "/entries/{entry}/fields/Title",
            "/entries/{entry}/attachments",
            "/entries/{entry}/attachments/notes.txt",
            "/entries/{entry}/access",
        };

        private FakeVault _vault;
        private TestAuthority _authority;
        private X509Certificate2 _certificate;
        private RecordingAudit _audit;
        private SpecRouter _router;
        private string _group;
        private string _entry;

        [SetUp]
        public void SetUp()
        {
            _vault = new FakeVault();
            _group = _vault.Add(_vault.RootAlias, "Fictitious");
            _entry = _vault.AddEntry(_group, "an invented service");
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "RLA")));
            Assume.That(
                _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultAskings.Free).Ok,
                Is.True);

            _authority = TestAuthority.New();
            _certificate = _authority.IssueAdmittedClient("agent");
            _audit = new RecordingAudit();
            _router = GateOperations.Router("9.9.9", null, new AccessCheck(_audit));
        }

        [TearDown]
        public void TearDown()
        {
            _certificate.Dispose();
            _authority.Dispose();
        }

        [TestCaseSource(nameof(Paths))]
        public void It_is_one_hold_and_asks_the_database_nothing_outside_it(string path)
        {
            foreach (var method in new[] { "GET", "HEAD" })
            {
                var vault = _vault;
                var held = vault.HeldReads;

                var context = Asked(method, path);

                Assert.That(context.Status, Is.EqualTo(200), method + " " + path + " " + context.Json);
                Assert.That(vault.HeldReads - held, Is.EqualTo(1), method + " " + path);
                Assert.That(vault.LooseLookups, Is.Zero, method + " " + path);
            }
        }

        [Test]
        public void A_change_is_what_the_count_of_loose_lookups_does_see()
        {
            // The router asks about a change before its turn, to refuse early, so that asking is
            // a lookup with nothing held. Here so that a count of none above is a count that
            // counts.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "RUL")));

            var context = Asked("PUT", "/entries/{entry}", Bodies.Of(("Title", "an invented name")));

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_vault.LooseLookups, Is.GreaterThan(0));
        }

        [TestCaseSource(nameof(Paths))]
        public void A_hold_that_did_not_last_answers_busy_and_nothing_the_read_made(string path)
        {
            // The read ran to its end against a database that was let go under it. What it made
            // is not an answer, and the first answer on a request stands, so none may have been
            // given by then.
            _vault.Lapses = true;

            var context = Asked("GET", path);

            Assert.That(context.Status, Is.EqualTo(503), path);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")), path);
            Assert.That(_audit.Entries.Single().Permitted, Is.True, "what was decided is written down all the same");
        }

        [Test]
        public void A_hold_that_cannot_be_taken_answers_busy_with_nothing_decided()
        {
            _vault.RefusesHolds = true;

            var context = Asked("GET", "/entries/{entry}");

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_audit.Entries, Is.Empty);
            Assert.That(_vault.LooseLookups, Is.Zero);
        }

        [Test]
        public void A_client_revoked_since_its_connection_is_answered_as_its_next_connection_is()
        {
            // The identity is as old as the connection's first read, and the operator revoked
            // the client after it. The request is decided about the client as the database holds
            // it inside the hold, which is not at all.
            var client = Connected();
            Assume.That(_authority.Revoke(_certificate), Is.True);

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Is.EqualTo("{\"error\":\"unpaired\"}"));

            var line = _audit.Entries.Single();
            Assert.That(line.Permitted, Is.False);
            Assert.That(line.Reason, Does.Contain("no admission record"));
            Assert.That(line.OperationId, Is.EqualTo("readEntry"));
        }

        [Test]
        public void A_profile_taken_off_the_list_since_the_connection_grants_nothing()
        {
            var client = Connected();
            _authority.Forget("agent");

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(404));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(SpecRouter.UnreachableError)));
            Assert.That(context.Json, Does.Not.Contain("an invented service"));
            Assert.That(_audit.Entries.Single().Permitted, Is.False);
        }

        [Test]
        public void A_profile_put_back_on_the_list_since_the_connection_counts()
        {
            // The other direction, which is what shows the answer is about the client read
            // inside the hold and not only refused more often.
            _authority.Forget("agent");
            var client = Connected();
            Assume.That(client.Profiles, Is.Empty);
            _authority.KnownProfiles = new[] { "agent" };

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_audit.Entries.Single().Permitted, Is.True);
        }

        [Test]
        public void A_database_that_shut_since_the_connection_is_locked_and_leaves_no_line()
        {
            var client = Connected();
            _authority.AdmissionThrows = new DatabaseShut("the database closed under this connection");

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("locked")));
            Assert.That(_audit.Entries, Is.Empty, "a lock is not a decision");
        }

        [Test]
        public void Records_that_cannot_be_read_just_now_answer_busy()
        {
            var client = Connected();
            _authority.AdmissionThrows = new VaultUnsettled("the records could not be listed just now");

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_audit.Entries, Is.Empty);
        }

        /// <summary>The client as its connection's first read found it.</summary>
        private ClientIdentity Connected()
        {
            var client = ClientCredentials.For(_certificate, _authority, NullGateLog.Instance);
            Assume.That(client.IsPaired, Is.True);
            return client;
        }

        private GateContext Asked(string method, string path)
        {
            return Asked(method, path, null, Connected());
        }

        private GateContext Asked(string method, string path, string body)
        {
            return Asked(method, path, body, Connected());
        }

        private GateContext Asked(string method, string path, string body, ClientIdentity client)
        {
            var text = body ?? string.Empty;
            var head = new HttpRequestHead(
                method,
                path.Replace("{group}", _group).Replace("{entry}", _entry),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Bodies.LengthOf(text));

            var context = new GateContext(head, text, client);
            new ServedDatabase(_vault, _authority, NullGateLog.Instance).PutOn(context);

            _router.Handle(context);
            return context;
        }
    }
}
