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
    /// A request that changes nothing is decided inside one hold and answered with the database
    /// as it stood there: the client's record, the rules and what is read are all of one
    /// moment, and what is made of an entry's values is made once the hold has ended.
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

                Assert.That(context.Status, Is.EqualTo(200), method + " " + path + " " + context.Document());
                Assert.That(vault.HeldReads - held, Is.EqualTo(1), method + " " + path);
                Assert.That(vault.LooseLookups, Is.Zero, method + " " + path);
            }
        }

        [TestCase("/entries/{entry}")]
        [TestCase("/entries/{entry}/fields")]
        [TestCase("/entries/{entry}/fields/Title")]
        [TestCase("/entries/{entry}/attachments")]
        [TestCase("/entries/{entry}/attachments/notes.txt")]
        public void Nothing_is_made_of_an_entry_s_values_while_the_hold_stands(string path)
        {
            // Reading a value, digesting it, reading or digesting an attachment and taking the
            // tag each grow with the entry, and no write is refused for an entry's size. The
            // hold is KeePass's own thread standing still, so the view is taken inside it and
            // asked for all of those once it has ended, a value last of all, as the answer is
            // written.
            var served = path == "/entries/{entry}" || path.EndsWith("/fields/Title", StringComparison.Ordinal);

            foreach (var method in new[] { "GET", "HEAD" })
            {
                var tags = _vault.TagsTaken;
                var reads = _vault.StringReads;

                var context = Asked(method, path);

                Assert.That(context.Status, Is.EqualTo(200), method + " " + path + " " + context.Document());
                Assert.That(context.Headers["ETag"], Is.Not.Null, method + " " + path);
                Assert.That(_vault.TagsTaken, Is.GreaterThan(tags), method + " " + path);
                Assert.That(_vault.ValueWorkHeld, Is.Zero, method + " " + path);
                Assert.That(
                    _vault.StringReads > reads,
                    Is.EqualTo(served && method == "GET"),
                    method + " " + path + ": a value is read where an answer writes it, and nowhere else");
            }
        }

        [TestCase("/entries")]
        [TestCase("/groups/{group}/entries")]
        [TestCase("/groups")]
        [TestCase("/groups/{group}/groups")]
        public void A_listing_is_written_once_the_hold_has_ended(string path)
        {
            // Which groups and entries a caller is shown is the rules' answer, asked inside the
            // hold. Writing the listing out needs nothing of the database but the descriptions
            // taken there, and it grows with the database, megabytes of it for a large database,
            // with every title it shows read for it.
            var context = Asked("GET", path);

            Assert.That(context.Status, Is.EqualTo(200), path + " " + context.Document());
            Assert.That(_vault.ValueWorkHeld, Is.Zero, path);
            if (path.EndsWith("/entries", StringComparison.Ordinal))
            {
                Assert.That(context.Document(), Does.Contain("an invented service"));
            }
        }

        [Test]
        public void Making_a_view_s_strings_inside_a_hold_is_what_the_count_of_held_work_does_see()
        {
            // Here so that a count of none above is a count that counts: asked for its strings
            // with the hold standing, a view makes them there.
            var made = _vault.Held(() => _vault.View(_entry, found => Sights.Whole).Fields.Count);

            Assert.That(made, Is.GreaterThan(0));
            Assert.That(_vault.ValueWorkHeld, Is.GreaterThan(0));
        }

        [TestCase("/entries/{entry}/fields/Absent")]
        [TestCase("/entries/{entry}/attachments/absent.txt")]
        public void A_name_an_entry_does_not_hold_is_found_missing_once_the_hold_has_ended(string path)
        {
            // Looking a name up makes the view's strings or attachments, so it is done after the
            // hold with the rest, and answered as it always was.
            var context = Asked("GET", path);

            Assert.That(context.Status, Is.EqualTo(404), context.Document());
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody(SpecRouter.UnreachableError)));
            Assert.That(_vault.ValueWorkHeld, Is.Zero);
        }

        [Test]
        public void A_tag_taken_inside_a_change_is_what_the_count_of_held_work_does_not_see()
        {
            // A change takes its tags inside its own turn, where the comparison and the change
            // have to be one step, and a turn is not a hold. Here so that the count above is
            // about reads and stays none for the right reason.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "RUL")));
            var tags = _vault.TagsTaken;

            var context = Asked("PUT", "/entries/{entry}", Bodies.Of(("Title", "an invented name")));

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(_vault.TagsTaken, Is.GreaterThan(tags));
            Assert.That(_vault.ValueWorkHeld, Is.Zero);
        }

        [TestCase("/entries/{entry}")]
        [TestCase("/entries/{entry}/fields/Title")]
        public void An_entry_changed_once_the_hold_has_ended_is_answered_as_it_stood_inside(string path)
        {
            // The operator's edit lands on KeePass's thread the moment the hold is let go, which
            // is before the read has asked its view for a value. The view keeps what the entry
            // held when it was taken, so the answer and its tag are of the entry as the rules
            // were asked about it.
            var before = Asked("GET", path);
            Assume.That(before.Status, Is.EqualTo(200), before.Document());

            var landed = false;
            _vault.After(1, () =>
            {
                landed = true;
                Assume.That(
                    _vault.UpdateField(_entry, "Title", "typed once the hold had ended", VaultAskings.Free).Ok,
                    Is.True);
            });

            var context = Asked("GET", path);
            _vault.After(0, null);

            Assert.That(landed, Is.True);
            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(context.Document(), Is.EqualTo(before.Document()));
            Assert.That(context.Headers["ETag"], Is.EqualTo(before.Headers["ETag"]));

            var after = Asked("GET", path);
            Assert.That(after.Document(), Does.Contain("typed once the hold had ended"));
            Assert.That(after.Headers["ETag"], Is.Not.EqualTo(before.Headers["ETag"]));
        }

        [TestCase("/entries/{entry}")]
        [TestCase("/entries/{entry}/fields/Password")]
        public void A_read_that_serves_nothing_writes_no_line_for_a_value_it_withheld(string path)
        {
            // The line for a withheld value is composed inside the hold, where the rules and the
            // node's name can be read, and written only where the answer is served. A read
            // refused by its condition withheld nothing from an answer, and read no value.
            Assume.That(
                _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free).Ok,
                Is.True);

            var served = Asked("GET", path);
            Assert.That(served.Status, Is.EqualTo(200), served.Document());
            Assert.That(_audit.Entries.Count, Is.EqualTo(2), "the read was let through and the value was not");
            Assert.That(_audit.Entries[1].Permitted, Is.False);
            var lines = _audit.Entries.Count;

            var reads = _vault.StringReads;

            var refused = Asked("GET", path, null, Connected(), "\"00000000000000000000000000000000\"");

            Assert.That(refused.Status, Is.EqualTo(412), refused.Document());
            Assert.That(refused.Content, Is.Null, "a refusal is handed over whole, with nothing left to write");
            Assert.That(_audit.Entries.Count, Is.EqualTo(lines + 1), "the decision, and no line beside it");
            Assert.That(_audit.Entries[lines].Permitted, Is.True);
            Assert.That(_vault.StringReads, Is.EqualTo(reads), "no value is read for an answer that is not served");
            Assert.That(_vault.ValueWorkHeld, Is.Zero);
        }

        [Test]
        public void A_change_is_what_the_count_of_loose_lookups_does_see()
        {
            // The router asks about a change before its turn, to refuse early, so that asking is
            // a lookup with nothing held. Here so that a count of none above is a count that
            // counts.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "RUL")));

            var context = Asked("PUT", "/entries/{entry}", Bodies.Of(("Title", "an invented name")));

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
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
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody("busy")), path);
            Assert.That(_audit.Entries.Single().Permitted, Is.True, "what was decided is written down all the same");
        }

        [Test]
        public void What_the_operator_does_under_a_read_waits_for_the_read(
            [ValueSource(nameof(Paths))] string path,
            [Values("remove", "lock")] string what,
            [Range(0, 6)] int lookups)
        {
            // A group removed with what is in it, or a database locked, while a read holds
            // KeePass's thread is removed or locked once the read is over, so the read answers as
            // though neither had been asked for. Tried at every lookup a read makes, and at more
            // than any of them makes.
            var landed = false;
            _vault.After(lookups, () =>
            {
                landed = true;
                Undo(what);
            });

            var context = Asked("GET", path);
            _vault.After(0, null);

            Assert.That(context.Status, Is.EqualTo(200), path + " " + context.Document());
            if (landed)
            {
                Assert.That(what == "lock" ? !_vault.IsOpen : _vault.Group(_group) == null, Is.True,
                    "what was asked for did happen, after the read");
            }
        }

        [Test]
        public void A_removal_or_a_lock_under_a_hold_that_did_not_last_is_answered_busy(
            [ValueSource(nameof(Paths))] string path,
            [Values("remove", "lock")] string what,
            [Range(0, 6)] int lookups)
        {
            // A hold that did not last kept nothing back, so here the group goes or the database
            // locks under the read, between any two of its lookups. Whatever the handler made of
            // a node that vanished half way is not an answer, and it throws nothing on the way:
            // the request is told to come again.
            _vault.Lapses = true;
            _vault.After(lookups, () => Undo(what));

            var context = Asked("GET", path);
            _vault.After(0, null);

            Assert.That(context.Status, Is.EqualTo(503), path + " " + context.Document());
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody("busy")), path);
        }

        [Test]
        public void A_hold_that_cannot_be_taken_answers_busy_with_nothing_decided()
        {
            _vault.RefusesHolds = true;

            var context = Asked("GET", "/entries/{entry}");

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody("busy")));
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
            Assert.That(context.Document(), Is.EqualTo("{\"error\":\"unpaired\"}"));

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
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody(SpecRouter.UnreachableError)));
            Assert.That(context.Document(), Does.Not.Contain("an invented service"));
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

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(_audit.Entries.Single().Permitted, Is.True);
        }

        [Test]
        public void A_database_that_shut_since_the_connection_is_locked_and_leaves_no_line()
        {
            var client = Connected();
            _authority.AdmissionThrows = new DatabaseShut("the database closed under this connection");

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody("locked")));
            Assert.That(_audit.Entries, Is.Empty, "a lock is not a decision");
        }

        [Test]
        public void Records_that_cannot_be_read_just_now_answer_busy()
        {
            var client = Connected();
            _authority.AdmissionThrows = new VaultUnsettled("the records could not be listed just now");

            var context = Asked("GET", "/entries/{entry}", null, client);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Document(), Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_audit.Entries, Is.Empty);
        }

        /// <summary>
        /// Takes the entry and the group the paths are under out of the database, or locks it.
        /// </summary>
        private void Undo(string what)
        {
            if (what == "lock")
            {
                _vault.Lock();
                return;
            }

            Assume.That(_vault.DeleteEntry(_entry, VaultAskings.Free).Ok, Is.True);
            Assume.That(_vault.DeleteGroup(_group, VaultAskings.Free).Ok, Is.True);
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
            return Asked(method, path, body, client, null);
        }

        private GateContext Asked(string method, string path, string body, ClientIdentity client, string ifMatch)
        {
            var text = body ?? string.Empty;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ifMatch != null)
            {
                headers["If-Match"] = ifMatch;
            }

            var head = new HttpRequestHead(
                method,
                path.Replace("{group}", _group).Replace("{entry}", _entry),
                new Dictionary<string, string>(),
                headers,
                Bodies.LengthOf(text));

            var context = new GateContext(head, text, client);
            new ServedDatabase(_vault, _authority, NullGateLog.Instance).PutOn(context);

            _router.Handle(context);

            // An answer written as it is sent is written here, as the transport writes it, so a
            // read of an entry or of a field driven from here reads its values.
            context.Document();
            return context;
        }
    }
}
