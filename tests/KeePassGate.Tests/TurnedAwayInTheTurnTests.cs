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
    /// A change whose caller the database stopped admitting while the change waited for its turn
    /// is answered as that caller's next request is, and changes nothing.
    /// </summary>
    /// <remarks>
    /// Through the real router, against a database in a dictionary and an authority that admits
    /// the client, with what the operator does landing between the router's answer and the
    /// change's turn. `HeldReadTests` asks the same of a request that changes nothing, and
    /// `DecidedAtTheWriteTests` asks the real vault about the rules.
    ///
    /// Everything is invented: the groups, the entries, the profiles.
    /// </remarks>
    [TestFixture]
    public sealed class TurnedAwayInTheTurnTests
    {
        /// <summary>A change of every kind the rules govern: the method, the path and the body.</summary>
        private static readonly object[] Changes =
        {
            new object[] { "POST", "/groups/{group}/groups", Bodies.Of(("Name", "Invented")) },
            new object[] { "PUT", "/groups/{group}", Bodies.Of(("Name", "Renamed")) },
            new object[] { "DELETE", "/groups/{hollow}", null },
            new object[] { "POST", "/groups/{group}/entries", Bodies.Of(("Title", "invented")) },
            new object[] { "PUT", "/entries/{entry}", Bodies.Of(("Title", "renamed")) },
            new object[] { "DELETE", "/entries/{entry}", null },
            new object[] { "POST", "/entries/{entry}/fields", Bodies.Of(("name", "Invented"), ("value", "x")) },
            new object[] { "PUT", "/entries/{entry}/fields/Title", Bodies.Of(("value", "renamed")) },
            new object[] { "DELETE", "/entries/{entry}/fields/Spare", null },
            new object[] { "POST", "/entries/{entry}/attachments", Bodies.Of(("name", "other.txt"), ("content", Base64("invented"))) },
            new object[] { "PUT", "/entries/{entry}/attachments/notes.txt", Bodies.Of(("content", Base64("changed"))) },
            new object[] { "DELETE", "/entries/{entry}/attachments/notes.txt", null },
        };

        private FakeVault _vault;
        private TestAuthority _authority;
        private X509Certificate2 _certificate;
        private RecordingAudit _audit;
        private SpecRouter _router;
        private string _group;
        private string _hollow;
        private string _entry;

        [SetUp]
        public void SetUp()
        {
            _vault = new FakeVault();
            _group = _vault.Add(_vault.RootAlias, "Fictitious");
            _hollow = _vault.Add(_vault.RootAlias, "Hollow");
            _entry = _vault.AddEntry(_group, "an invented service");
            _vault.Plant(_entry, "Spare", "invented");
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "CRUSDLA")));
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

        [TestCaseSource(nameof(Changes))]
        public void Each_of_them_is_made_while_the_client_stays_admitted(string method, string path, string body)
        {
            // Here so that a change refused below is a change that would have been made.
            var before = Held();

            var context = Asked(method, path, body, null);

            Assert.That(context.Status, Is.EqualTo(200).Or.EqualTo(201), method + " " + path + " " + context.Json);
            Assert.That(Held(), Is.Not.EqualTo(before), method + " " + path);
            Assert.That(_audit.Entries[0].Permitted, Is.True, method + " " + path);
        }

        [TestCaseSource(nameof(Changes))]
        public void A_client_revoked_while_its_change_waited_is_told_it_is_unpaired(string method, string path, string body)
        {
            // Not the `404` of a node that is not there: the node is where it was, and the next
            // request on this certificate is answered `401` whatever it asks for.
            var before = Held();

            var context = Asked(method, path, body, () => Assume.That(_authority.Revoke(_certificate), Is.True));

            Assert.That(context.Status, Is.EqualTo(401), method + " " + path + " " + context.Json);
            Assert.That(context.Json, Is.EqualTo("{\"error\":\"unpaired\"}"), method + " " + path);
            Assert.That(Held(), Is.EqualTo(before), method + " " + path);

            var line = _audit.Entries.Single();
            Assert.That(line.Permitted, Is.False, method + " " + path);
            Assert.That(line.Reason, Does.Contain("no admission record"), method + " " + path);
        }

        [TestCaseSource(nameof(Changes))]
        public void A_profile_taken_off_the_list_while_the_change_waited_refuses_it_as_the_rules_do(
            string method, string path, string body)
        {
            // Still a client, and one the rules now grant nothing: the answer every refused node
            // gets.
            var before = Held();

            var context = Asked(method, path, body, () => _authority.Forget("agent"));

            Assert.That(context.Status, Is.EqualTo(404), method + " " + path + " " + context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(SpecRouter.UnreachableError)), method + " " + path);
            Assert.That(Held(), Is.EqualTo(before), method + " " + path);
            Assert.That(_audit.Entries[0].Permitted, Is.False, method + " " + path);
        }

        [Test]
        public void A_database_that_is_not_there_once_the_turn_comes_is_answered_in_its_own_word(
            [ValueSource(nameof(Changes))] object[] change,
            [Values("locked", "undecided", "busy")] string word)
        {
            // None of the three is a refusal, and a client told `404` would stop looking for a
            // node that is still there. The line kept before dispatch stays the only line: the
            // rules were asked nothing inside the turn.
            var method = (string)change[0];
            var path = (string)change[1];
            var before = Held();

            var context = Asked(method, path, (string)change[2], () =>
            {
                switch (word)
                {
                    case "locked":
                        _authority.AdmissionThrows = new DatabaseShut("the database closed under this request");
                        break;
                    case "undecided":
                        _authority.Unavailable = "undecided";
                        break;
                    default:
                        _authority.AdmissionThrows = new VaultUnsettled("the records could not be listed just now");
                        break;
                }
            });

            _authority.AdmissionThrows = null;
            _authority.Unavailable = null;

            Assert.That(context.Status, Is.EqualTo(503), method + " " + path + " " + context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(word)), method + " " + path);
            Assert.That(Held(), Is.EqualTo(before), method + " " + path);
            Assert.That(_audit.Entries.Single().Permitted, Is.True, "the answer the router gave before the turn");
        }

        private static string Base64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Enough of what the database holds to tell a change that was made.</summary>
        private string Held()
        {
            return string.Join(
                " ",
                _vault.Groups().Count,
                _vault.Entries().Count,
                _vault.GroupTag(_group),
                _vault.EntryTag(_entry));
        }

        /// <summary>
        /// Sends a change as the client its connection's first read found, with what the operator
        /// does landing after the router answered and before the change's turn.
        /// </summary>
        private GateContext Asked(string method, string path, string body, Action meanwhile)
        {
            var client = ClientCredentials.For(_certificate, _authority, NullGateLog.Instance);
            Assume.That(client.IsPaired, Is.True);

            var text = body ?? string.Empty;
            var head = new HttpRequestHead(
                method,
                path.Replace("{group}", _group).Replace("{hollow}", _hollow).Replace("{entry}", _entry),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Bodies.LengthOf(text));

            var context = new GateContext(head, text, client);
            new ServedDatabase(_vault, _authority, NullGateLog.Instance).PutOn(context);

            var landed = meanwhile == null;
            if (meanwhile != null)
            {
                // One lookup is the router's, for the node it may refuse the change at.
                _vault.After(1, () =>
                {
                    landed = true;
                    meanwhile();
                });
            }

            _router.Handle(context);
            _vault.After(0, null);

            Assume.That(landed, Is.True, "nothing landed between the router and the change");
            return context;
        }
    }
}
