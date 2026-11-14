using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// The API, driven through the real router against a database in a dictionary.
    /// </summary>
    /// <remarks>
    /// Every request here goes through the whole pipeline the transport would hand it to: the
    /// document decides which operation it is, identity is checked, the record and the rules are
    /// intersected against the node the operation names, the body is checked, and only then does a
    /// handler run. So what these assert is the API as a client meets it, without a socket or a
    /// password manager anywhere in it.
    ///
    /// Everything is invented: the groups, the entries, the field names, the profiles.
    /// </remarks>
    [TestFixture]
    public sealed class ApiTests
    {
        private static readonly string AllButProtected = Rules.Granting(("agent", "CRUSDLA"));

        /// <summary>The reason the resolver gives when a rule holds the verb and not `P`.</summary>
        private const string NoProtectedReach = "a rule here does not reach protected values";

        /// <summary>
        /// A spelling of `Password` a database cannot tell from the real one, and its path form.
        /// </summary>
        /// <remarks>
        /// U+00AD, the soft hyphen. KeePass keeps an entry's strings under the default comparer,
        /// which compares by the current culture, and that collation gives it no weight at all, so
        /// this name addresses the entry's `Password`. It is a format character rather than a
        /// control character, so nothing in the path refuses it on the way in; a body making a
        /// name holding it is refused, as every name holding a format character is.
        /// </remarks>
        private const string PasswordAlias = "Pass\u00ADword";

        private const string PasswordAliasPath = "Pass%C2%ADword";

        /// <summary>A combining acute accent, so that `Cafe` plus it is `Café` in the other normal form.</summary>
        private const string Combining = "\u0301";

        private FakeVault _vault;

        /// <summary>The profiles the client's record holds: `agent`, unless a test sets others.</summary>
        private string[] _profiles;

        /// <summary>
        /// The profiles the record holds that the database's list no longer names: none, unless a
        /// test sets some.
        /// </summary>
        private string[] _removed;
        private SpecRouter _router;

        /// <summary>The plugin's log as the requests here are served with it.</summary>
        private RecordingGateLog _log;

        private string _group;
        private string _entry;

        [SetUp]
        public void SetUp()
        {
            _vault = new FakeVault();
            _profiles = new[] { "agent" };
            _removed = null;
            _log = new RecordingGateLog();
            _group = _vault.Add(_vault.RootAlias, "Fictitious");
            _entry = _vault.AddEntry(_group, "an invented service");

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
        }

        [Test]
        public void A_client_with_no_rules_reaches_nothing_at_all()
        {
            // The state every database is in until somebody writes a grant. Not an error: a
            // database nothing may reach until somebody says otherwise.
            foreach (var path in new[] { "/groups", "/entries", "/entries/" + _entry })
            {
                var answer = Get(path);

                Assert.That(answer.Status, Is.EqualTo(404), path);
            }
        }

        [Test]
        public void The_times_it_serves_are_RFC_3339()
        {
            // Nothing asserted on `created` or `modified` anywhere, which is how a second spelling
            // of the wire's own moment lived here unnoticed. It matters more than it looks:
            // `PyKeePassGate` reads these with one fixed pattern and answers `None` when they do
            // not match, so a shape this side got wrong would empty every time in the client
            // rather than failing anything.
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var answer = Get("/entries/" + _entry);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Json, Does.Match(
                "\"created\":\"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\""));
            Assert.That(answer.Json, Does.Match(
                "\"modified\":\"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\""));
        }

        [Test]
        public void A_rule_at_the_root_reaches_everything_under_it()
        {
            _vault.Granting(_vault.RootAlias, AllButProtected);

            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(200));
            Assert.That(Get("/groups/" + _group).Status, Is.EqualTo(200));
        }

        [Test]
        public void A_group_listing_gives_names_and_never_notes()
        {
            // `list` gives names. A group's notes are free text an operator wrote, and a client
            // holding `list` alone is refused them by reading the group, so a listing handing them
            // out would make the smaller grant the more revealing one.
            Assume.That(_vault.UpdateGroup(_group, null, "invented notes for read alone", VaultAskings.Free).Ok, Is.True);
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            foreach (var path in new[] { "/groups", "/groups/" + _vault.RootAlias + "/groups" })
            {
                var answer = Get(path);

                Assert.That(answer.Status, Is.EqualTo(200), path);
                Assert.That(answer.Json, Does.Contain("\"name\":\"Fictitious\""), path);
                Assert.That(answer.Json, Does.Not.Contain("notes"), path);
            }

            Assert.That(Get("/groups/" + _group).Status, Is.EqualTo(404), "reading needs `read`");

            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "R")));

            Assert.That(Get("/groups/" + _group).Json,
                Does.Contain("\"notes\":\"invented notes for read alone\""));
        }

        [Test]
        public void A_listing_leaves_out_what_the_rules_do_not_allow()
        {
            // The one place a handler asks the rules, and it asks per item. An entry left out is
            // not a refusal: it is the normal case for a client that may see part of a database.
            var other = _vault.Add(_vault.RootAlias, "Private");
            _vault.AddEntry(other, "not for this client");

            // The sweep is an operation on the root, so it needs `list` there. What one group then
            // takes back is the empty rule, which grants nothing from itself down.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));
            _vault.Granting(other, Rules.Granting(("agent", "")));

            var answer = Get("/entries");

            Assert.That(answer.Status, Is.EqualTo(200));
            Assert.That(answer.Json, Does.Contain("an invented service"));
            Assert.That(answer.Json, Does.Not.Contain("not for this client"));
            Assert.That(answer.Json, Does.Contain("\"count\":1"));
        }

        [Test]
        public void A_listing_shows_an_item_through_any_profile_once_some_profile_may_list_where_it_was_taken()
        {
            // T045, decided by the operator's ruling R10 as amended on 2026-11-03. `lister` may list
            // from the group and not the entry, `viewer` the entry and nothing from the group. A
            // listing is a read, so the entry is shown: `viewer` is entitled to it, and what the two
            // profiles pool is the way to it.
            _profiles = new[] { "lister", "viewer" };
            _vault.Granting(_group, Rules.Granting(("lister", "L"), ("viewer", "")));
            _vault.Granting(_entry, Rules.Granting(("lister", ""), ("viewer", "L")));

            var listed = Get("/groups/" + _group + "/entries");

            Assert.That(listed.Status, Is.EqualTo(200));
            Assert.That(listed.Json, Does.Contain("an invented service"));
        }

        [Test]
        public void A_listing_of_groups_shows_a_group_through_any_profile_once_some_profile_may_list_where_it_was_taken()
        {
            _profiles = new[] { "lister", "viewer" };
            var shown = _vault.Add(_group, "Shown through another profile");
            _vault.Granting(_group, Rules.Granting(("lister", "L"), ("viewer", "")));
            _vault.Granting(shown, Rules.Granting(("lister", ""), ("viewer", "L")));

            var listed = Get("/groups/" + _group + "/groups");

            Assert.That(listed.Status, Is.EqualTo(200));
            Assert.That(listed.Json, Does.Contain("Shown through another profile"));
        }

        [Test]
        public void A_sweep_shows_an_item_through_any_profile_once_some_profile_may_list_the_root()
        {
            _profiles = new[] { "lister", "viewer" };
            _vault.Granting(_vault.RootAlias, Rules.Granting(("lister", "L")));
            _vault.Granting(_group, Rules.Granting(("lister", ""), ("viewer", "L")));

            var swept = Get("/entries");

            Assert.That(swept.Status, Is.EqualTo(200));
            Assert.That(swept.Json, Does.Contain("an invented service"));
        }

        [Test]
        public void A_listing_shows_a_protected_title_through_any_profile_that_reaches_it()
        {
            _profiles = new[] { "lister", "viewer" };
            _vault.Protect(_entry, "Title");
            _vault.Granting(_group, Rules.Granting(("lister", "L"), ("viewer", "")));
            _vault.Granting(_entry, Rules.Granting(("lister", "L"), ("viewer", "LP")));

            var listed = Get("/groups/" + _group + "/entries");

            Assert.That(listed.Status, Is.EqualTo(200));
            Assert.That(listed.Json, Does.Contain("an invented service"));
        }

        [Test]
        public void A_listing_is_taken_only_where_some_profile_may_list_and_shows_only_what_some_profile_may_list()
        {
            // What the profiles pool is the way to an item, never the item: a profile entitled to
            // the entry alone may not take the listing, and an entry no profile may list stays out.
            _profiles = new[] { "viewer" };
            _vault.Granting(_group, Rules.Granting(("lister", "L"), ("viewer", "")));
            _vault.Granting(_entry, Rules.Granting(("lister", ""), ("viewer", "L")));

            Assert.That(Get("/groups/" + _group + "/entries").Status, Is.EqualTo(404));

            _profiles = new[] { "lister", "viewer" };
            _vault.Granting(_entry, Rules.Granting(("lister", ""), ("viewer", "")));

            var listed = Get("/groups/" + _group + "/entries");

            Assert.That(listed.Status, Is.EqualTo(200));
            Assert.That(listed.Json, Does.Contain("\"count\":0"));
        }

        [Test]
        public void The_root_can_be_reached_by_name_rather_than_by_identifier()
        {
            // Which is what lets a client granted `create` and nothing else put an entry somewhere.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "C")));

            var made = Send("POST", "/groups/root/entries", "{\"Title\":\"an invented service\"}");

            Assert.That(made.Status, Is.EqualTo(201));
            Assert.That(made.Json, Does.Contain("uuid"));
        }

        [Test]
        public void Creating_needs_create_and_nothing_else_does()
        {
            _vault.Granting(_group, Rules.Granting(("agent", "C")));

            Assert.That(Send("POST", "/groups/" + _group + "/entries", "{\"Title\":\"x\"}").Status,
                Is.EqualTo(201));

            // The same client, the same node, a different verb.
            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(404));
            Assert.That(Send("DELETE", "/entries/" + _entry, null).Status, Is.EqualTo(404));
        }

        [Test]
        public void Listing_says_a_name_and_reading_says_a_value()
        {
            // What makes `list` worth granting on its own. A client that may find an entry cannot
            // read what is in it.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            var listed = Get("/entries");

            Assert.That(listed.Status, Is.EqualTo(200));
            Assert.That(listed.Json, Does.Contain("an invented service"));

            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(404));
            Assert.That(Get("/entries/" + _entry + "/fields/Password").Status, Is.EqualTo(404));
        }

        [Test]
        public void Every_route_an_entry_answers_for_carries_the_entry_s_one_tag()
        {
            // A field and an attachment are not nodes of their own, so the rules answer for them
            // by the entry and so does the tag: one condensation of everything the entry holds,
            // whichever of its routes was asked, and the same again for the head beside each.
            _vault.Granting(_group, AllButProtected);
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultAskings.Free);
            _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultAskings.Free);

            var tag = Get("/entries/" + _entry).Tag;
            Assert.That(tag, Does.Match("^\"[0-9a-f]{32}\"$"), "strong, quoted, and nothing to read in it");

            foreach (var path in new[]
            {
                "/entries/" + _entry,
                "/entries/" + _entry + "/fields",
                "/entries/" + _entry + "/fields/Account",
                "/entries/" + _entry + "/attachments",
                "/entries/" + _entry + "/attachments/notes.txt",
            })
            {
                foreach (var method in new[] { "GET", "HEAD" })
                {
                    var answer = Send(method, path, null);

                    Assert.That(answer.Status, Is.EqualTo(200), method + " " + path);
                    Assert.That(answer.Tag, Is.EqualTo(tag), method + " " + path);
                }
            }
        }

        [Test]
        public void A_group_carries_a_tag_of_its_own_and_a_listing_carries_none()
        {
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var group = Get("/groups/" + _group);
            Assert.That(group.Tag, Does.Match("^\"[0-9a-f]{32}\"$"));
            Assert.That(Send("HEAD", "/groups/" + _group, null).Tag, Is.EqualTo(group.Tag));
            Assert.That(group.Tag, Is.Not.EqualTo(Get("/entries/" + _entry).Tag));

            // A listing answers about a collection, and no collection here has a tag.
            var listings = new[]
            {
                "/groups", "/groups/" + _group + "/groups", "/entries", "/groups/" + _group + "/entries",
            };

            foreach (var path in listings)
            {
                var listed = Get(path);

                Assert.That(listed.Status, Is.EqualTo(200), path);
                Assert.That(listed.Tag, Is.Null, path);
            }
        }

        [Test]
        public void A_tag_is_taken_only_by_a_route_that_hands_it_out()
        {
            // A tag is a digest of every string and attachment its caller may read of the node,
            // which the real vault decrypts to take, so a tag is taken only by a route that will
            // use it: a listing, an access answer and a caller that is handed no tag cost none.
            // Nothing a client sees would differ if they did, which is why this counts.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "CRUSDLAP")));
            Assert.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);
            var entry = "/entries/" + _entry;

            var taken = _vault.TagsTaken;
            foreach (var path in new[]
            {
                "/groups", "/groups/" + _group + "/groups", "/entries", "/groups/" + _group + "/entries",
                "/groups/" + _group + "/access", entry + "/access",
            })
            {
                Assert.That(Get(path).Status, Is.EqualTo(200), path);
            }

            Assert.That(_vault.TagsTaken, Is.EqualTo(taken), "a route handing out no tag took a tag");

            Assert.That(Get(entry).Tag, Is.Not.Null);
            Assert.That(_vault.TagsTaken, Is.EqualTo(taken + 1), "and a read handing a tag out takes it once");

            // A rule without `read` is handed no tag, on the routes `list` opens and on a change.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "ULA")));
            taken = _vault.TagsTaken;

            foreach (var path in new[] { entry + "/fields", entry + "/attachments" })
            {
                var listed = Get(path);

                Assert.That(listed.Status, Is.EqualTo(200), path);
                Assert.That(listed.Tag, Is.Null, path);
            }

            Assert.That(Send("PUT", entry, "{\"Title\":\"renamed\"}").Status, Is.EqualTo(200));
            Assert.That(_vault.TagsTaken, Is.EqualTo(taken), "a caller that is handed no tag cost a tag");
        }

        [Test]
        public void A_tag_moves_with_what_the_node_holds()
        {
            _vault.Granting(_vault.RootAlias, AllButProtected);
            var entry = "/entries/" + _entry;
            var group = "/groups/" + _group;

            var before = Get(entry).Tag;
            var groupBefore = Get(group).Tag;

            Assert.That(Send("PUT", entry, "{\"Title\":\"a renamed service\"}").Status, Is.EqualTo(200));
            var changed = Get(entry).Tag;
            Assert.That(changed, Is.Not.EqualTo(before), "a changed field is a changed entry");
            Assert.That(Get(group).Tag, Is.EqualTo(groupBefore), "and says nothing new about its group");

            // Not whether the tag that was there comes back with what was there: that is the real
            // vault's to answer, whose tag carries the time every change touches, so it does only
            // within the second, and `NodeTagsTests` holds the content half of it. This fake keeps
            // no times, so asked here it would pin a property the gate does not have.
            Assert.That(Send("PUT", entry, "{\"Title\":\"an invented service\"}").Status, Is.EqualTo(200));
            Assert.That(Get(entry).Tag, Is.Not.EqualTo(changed), "the entry moved again");

            Assert.That(Send("PUT", group, "{\"Notes\":\"invented notes\"}").Status, Is.EqualTo(200));
            Assert.That(Get(group).Tag, Is.Not.EqualTo(groupBefore));
        }

        [TestCase("field", "U", false)]
        [TestCase("field", "UL", false)]
        [TestCase("field", "RU", true)]
        [TestCase("secret", "UP", false)]
        [TestCase("secret", "ULP", false)]
        [TestCase("secret", "RUP", true)]
        [TestCase("entry", "U", false)]
        [TestCase("entry", "RU", true)]
        [TestCase("attachment", "UA", false)]
        [TestCase("attachment", "RUA", true)]
        [TestCase("group", "U", false)]
        [TestCase("group", "RU", true)]
        public void The_sight_an_update_hands_the_vault_reads_what_the_update_names_or_does_not(
            string what, string mask, bool sees)
        {
            // What the real vault skips an update on, when the update would change nothing: a
            // caller that may write a value and not read it is written like any change, so that a
            // guess at the value costs the same right or wrong. The handler hands the vault a
            // sight and nothing else, and whether that sight reads what the update names is read
            // off the caller's view, by the code the real vault reads it with. This vault writes
            // every update and names the strings itself, so what is pinned here is the sight the
            // handler hands over, and `SkippedOnlyForAReaderTests` holds the real vault to
            // consulting the view with the names the update carries.
            Named(what, Rules.Granting(("agent", mask)));

            Assert.That(Updated(what).Status, Is.EqualTo(200));
            Assert.That(_vault.Saw, Is.EqualTo(sees));
        }

        [TestCase("field", "R", "U", true)]
        [TestCase("secret", "R", "UP", false)]
        [TestCase("secret", "RP", "UP", true)]
        [TestCase("attachment", "R", "UA", false)]
        [TestCase("attachment", "RA", "UA", true)]
        public void Reading_what_an_update_names_is_asked_of_every_profile_and_of_one_rule_at_a_time(
            string what, string reader, string writer, bool sees)
        {
            // Profiles are combined with or, as a read combines them, and a switch belongs to its
            // rule: reading the entry through one profile and setting its protected values through
            // another reads no protected value.
            _profiles = new[] { "reader", "writer" };
            Named(what, Rules.Granting(("reader", reader), ("writer", writer)));

            Assert.That(Updated(what).Status, Is.EqualTo(200));
            Assert.That(_vault.Saw, Is.EqualTo(sees));
        }

        [Test]
        public void A_protected_title_a_listing_shows_its_caller_is_served_by_a_read_and_covered_by_its_tag()
        {
            // A caller's view of an entry holds everything any route shows that caller of the
            // entry. `list` with `protected` shows a protected title in a listing, so beside
            // `read` a read serves it too and the tag covers it, while the password, which no
            // listing shows, stays withheld and out of the tag.
            _profiles = new[] { "reader", "lister", "writer" };
            _vault.Protect(_entry, "Title");
            _vault.Plant(_entry, "Password", "not-a-real-password");
            _vault.Granting(_group, Rules.Granting(("reader", "R"), ("lister", "LP"), ("writer", "UP")));
            var entry = "/entries/" + _entry;

            Assert.That(Get("/groups/" + _group + "/entries").Json, Does.Contain("an invented service"));

            var read = Get(entry);
            Assert.That(read.Json, Does.Contain("\"Title\":\"an invented service\""));
            Assert.That(read.Json, Does.Contain("\"Password\":null"));
            Assert.That(Get(entry + "/fields/Title").Json, Does.Contain("\"value\":\"an invented service\""));
            Assert.That(Get(entry + "/fields/Title").Tag, Is.EqualTo(read.Tag));
            Assert.That(Get(entry + "/fields").Tag, Is.EqualTo(read.Tag));

            // An update naming the title as it is: this caller reads it, so the vault is told so.
            Assert.That(Send("PUT", entry + "/fields/Title", Bodies.Of(("value", "an invented service"))).Status,
                Is.EqualTo(200));
            Assert.That(_vault.Saw, Is.True);
            Assert.That(Send("PUT", entry + "/fields/Password", Bodies.Of(("value", "not-a-real-password"))).Status,
                Is.EqualTo(200));
            Assert.That(_vault.Saw, Is.False, "the listing's reach opens no other protected string");

            _vault.Plant(_entry, "Password", "another-invented-password");
            Assert.That(Get(entry).Tag, Is.EqualTo(read.Tag));
            _vault.Plant(_entry, "Title", "renamed by the operator");
            Assert.That(Get(entry).Tag, Is.Not.EqualTo(read.Tag));

            // Without the listing's reach the title is withheld by both routes, as it was.
            _vault.Granting(_group, Rules.Granting(("reader", "R"), ("lister", "L"), ("writer", "UP")));
            Assert.That(Get("/groups/" + _group + "/entries").Json, Does.Not.Contain("renamed by the operator"));
            Assert.That(Get(entry).Json, Does.Contain("\"Title\":null"));
            Assert.That(Send("PUT", entry + "/fields/Title", Bodies.Of(("value", "renamed by the operator"))).Status,
                Is.EqualTo(200));
            Assert.That(_vault.Saw, Is.False);
        }

        private void Named(string what, string rules)
        {
            _vault.Granting(_group, rules);
            _vault.Plant(_entry, "Password", "not-a-real-password");
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);
        }

        private Answer Updated(string what)
        {
            var entry = "/entries/" + _entry;

            switch (what)
            {
                case "field":
                    return Send("PUT", entry + "/fields/Title", Bodies.Of(("value", "an invented service")));
                case "secret":
                    return Send("PUT", entry + "/fields/Password", Bodies.Of(("value", "not-a-real-password")));
                case "entry":
                    return Send("PUT", entry, "{\"Title\":\"an invented service\"}");
                case "attachment":
                    return Send("PUT", entry + "/attachments/notes.txt", Bodies.Of(("content", Base64("x"))));
                default:
                    return Send("PUT", "/groups/" + _group, "{\"Notes\":\"invented notes\"}");
            }
        }

        [Test]
        public void A_write_against_the_tag_it_read_goes_ahead_and_answers_with_the_next_one()
        {
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;
            var tag = Get(entry).Tag;

            var written = Conditioned("PUT", entry, "{\"Title\":\"a renamed service\"}", tag);

            Assert.That(written.Status, Is.EqualTo(200), written.Json);
            Assert.That(written.Written, Is.Not.Null.And.Not.EqualTo(tag));
            Assert.That(written.Written, Is.EqualTo(Get(entry).Tag), "the tag of exactly what was written");
            Assert.That(written.Tag, Is.Null,
                "in the body and not as `ETag`, which on a `PUT` says the body sent was stored as sent");

            // So a second write can name it without reading in between.
            Assert.That(Conditioned("PUT", entry, "{\"Title\":\"renamed again\"}", written.Written).Status,
                Is.EqualTo(200));
        }

        [TestCase("field", TestName = "Adding a field answers with the tag of the entry it leaves")]
        [TestCase("attachment", TestName = "Adding an attachment answers with the tag of the entry it leaves")]
        [TestCase("replace", TestName = "Replacing an attachment answers with the tag of the entry it leaves")]
        public void A_change_under_an_entry_answers_with_the_entry_s_tag(string route)
        {
            // The routing document's `WrittenTag`, for the changes that add or replace something
            // under an entry: the client writes again under the tag it is answered with.
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            Answer written;
            switch (route)
            {
                case "field":
                    written = Send("POST", entry + "/fields", Bodies.Of(("name", "Added"), ("value", "a value")));
                    break;
                case "attachment":
                    written = Send("POST", entry + "/attachments", Bodies.Of(("name", "added.txt"), ("content", Base64("x"))));
                    break;
                default:
                    written = Send("PUT", entry + "/attachments/notes.txt", Bodies.Of(("content", Base64("y"))));
                    break;
            }

            Assert.That(written.Status, Is.EqualTo(route == "replace" ? 200 : VaultHandlers.Created), written.Json);
            Assert.That(written.Written, Is.Not.Null.And.EqualTo(Get(entry).Tag), written.Json);
            Assert.That(Conditioned("PUT", entry, "{\"Title\":\"renamed\"}", written.Written).Status, Is.EqualTo(200),
                "and a second write can name it without reading in between");
        }

        [Test]
        public void A_write_against_a_tag_the_entry_has_moved_on_from_is_refused_and_changes_nothing()
        {
            // The lost update this is for: one client read the entry, another changed it, and the
            // first wrote back what it had read.
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;
            var stale = Get(entry).Tag;

            Assert.That(Send("PUT", entry, "{\"Title\":\"changed by somebody else\"}").Status, Is.EqualTo(200));

            var refused = Conditioned("PUT", entry, "{\"Title\":\"what the first client meant\"}", stale);

            Assert.That(refused.Status, Is.EqualTo(412));
            Assert.That(refused.Json, Is.EqualTo("{\"error\":\"precondition failed\"}"));
            Assert.That(Get(entry).Json, Does.Contain("changed by somebody else"));
        }

        [Test]
        public void Every_operation_on_a_tagged_target_asks_the_condition()
        {
            _vault.Granting(_vault.RootAlias,
                Rules.Granting(("agent", "CRUSDLA")));
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultAskings.Free);
            _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultAskings.Free);
            var empty = _vault.Add(_group, "Empty");

            var entry = "/entries/" + _entry;
            var document = SpecDocument.Embedded();
            var requests = new[]
            {
                new[] { "GET", "/groups/" + _group, null },
                new[] { "HEAD", "/groups/" + _group, null },
                new[] { "PUT", "/groups/" + _group, "{\"Notes\":\"invented\"}" },
                new[] { "DELETE", "/groups/" + empty, null },
                new[] { "GET", entry, null },
                new[] { "HEAD", entry, null },
                new[] { "PUT", entry, "{\"Title\":\"renamed\"}" },
                new[] { "GET", entry + "/fields", null },
                new[] { "HEAD", entry + "/fields", null },
                new[] { "POST", entry + "/fields", "{\"name\":\"Other\",\"value\":\"invented\"}" },
                new[] { "GET", entry + "/fields/Account", null },
                new[] { "HEAD", entry + "/fields/Account", null },
                new[] { "PUT", entry + "/fields/Account", "{\"value\":\"changed\"}" },
                new[] { "DELETE", entry + "/fields/Account", null },
                new[] { "GET", entry + "/attachments", null },
                new[] { "HEAD", entry + "/attachments", null },
                new[] { "POST", entry + "/attachments", Bodies.Of(("name", "other.txt"), ("content", Base64("invented"))) },
                new[] { "GET", entry + "/attachments/notes.txt", null },
                new[] { "HEAD", entry + "/attachments/notes.txt", null },
                new[] { "PUT", entry + "/attachments/notes.txt", Bodies.Of(("content", Base64("changed"))) },
                new[] { "DELETE", entry + "/attachments/notes.txt", null },
                new[] { "DELETE", entry, null },
            };

            // Exactly the operations the document declares `If-Match` on, by the operation each
            // request is routed to, so that the document cannot come to promise a `412` on an
            // operation this sweep never asks, and the handler there never asks either.
            var swept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in requests)
            {
                IReadOnlyDictionary<string, string> captured;
                swept.Add(document.Operations.Single(o =>
                    string.Equals(o.Method, request[0], StringComparison.Ordinal)
                    && o.Path.TryMatch(request[1], out captured)).OperationId);
            }

            Assert.That(swept, Is.EquivalentTo(
                document.Operations.Where(o => o.TakesIfMatch).Select(o => o.OperationId)));

            // Every one refused against a tag nothing holds, and nothing changed by any of them.
            // Asked of each node's tag, which is a digest of everything the node holds, so a
            // refused request that wrote a title, a group's notes, an attachment's bytes, or a
            // field or attachment of its own changes the tag of the node it wrote to.
            var entryBefore = _vault.EntryTag(_entry);
            var groupBefore = _vault.GroupTag(_group);
            var emptyBefore = _vault.GroupTag(empty);

            foreach (var request in requests)
            {
                var refused = Conditioned(request[0], request[1], request[2], "\"00000000000000000000000000000000\"");

                Assert.That(refused.Status, Is.EqualTo(412), request[0] + " " + request[1]);
            }

            Assert.That(_vault.EntryTag(_entry), Is.Not.Null.And.EqualTo(entryBefore), "the entry changed");
            Assert.That(_vault.GroupTag(_group), Is.Not.Null.And.EqualTo(groupBefore), "its group changed");
            Assert.That(_vault.GroupTag(empty), Is.Not.Null.And.EqualTo(emptyBefore), "the empty group changed");

            // And every one served under `*`, in this order, which ends with the entry gone.
            foreach (var request in requests)
            {
                var served = Conditioned(request[0], request[1], request[2], "*");

                Assert.That(served.Status, Is.EqualTo(200).Or.EqualTo(201), request[0] + " " + request[1] + " " + served.Json);
            }

            Assert.That(_vault.Entry(_entry), Is.Null);
        }

        [TestCase("/groups/{group}", "\"00000000000000000000000000000000\"", false, TestName = "A read of a group refused under a stale tag is logged with its reason")]
        [TestCase("/entries/{entry}", "\"00000000000000000000000000000000\"", false, TestName = "A read of an entry refused under a stale tag is logged with its reason")]
        [TestCase("/entries/{entry}/fields/Title", "\"unterminated", false, TestName = "A read refused under a field that does not read is logged with its reason")]
        [TestCase("/entries/{entry}/attachments", "*", true, TestName = "A read refused to a client handed no tag is logged with its reason")]
        public void A_read_refused_412_is_logged_as_a_change_refused_412_is(
            string route, string ifMatch, bool listsOnly)
        {
            // The maintainer's word of 2026-11-08. A change's `412` has always been a warning in
            // the log, with its reason; a read's was written nowhere, so a client refused on its
            // reads over and over showed nowhere at all.
            //
            // A caller handed no tag is a caller without `read`, which reaches the routes `list`
            // opens and no others.
            _vault.Granting(_group, listsOnly ? Rules.Granting(("agent", "LA")) : AllButProtected);
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            var read = Conditioned("GET", route.Replace("{group}", _group).Replace("{entry}", _entry), null, ifMatch);

            // The reason a change's refusal gives, one way of naming a cause for both: a caller
            // handed no tag is refused under `Withheld`, as its changes are, and any other caller
            // under what it asked.
            var reason = (listsOnly ? VaultPrecondition.Withheld() : VaultPrecondition.Read(ifMatch)).Refusal;

            Assert.That(read.Status, Is.EqualTo(412), read.Json);
            Assert.That(_log.Warnings.Select(line => line.Message), Is.EqualTo(new[]
            {
                "a read was refused, its precondition failing: " + reason,
            }));
        }

        [Test]
        public void A_read_whose_condition_holds_logs_nothing()
        {
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;

            Assert.That(Conditioned("GET", entry, null, Get(entry).Tag).Status, Is.EqualTo(200));
            Assert.That(_log.Warnings, Is.Empty);
        }

        [Test]
        public void A_condition_on_a_node_the_rules_refuse_is_the_same_404()
        {
            // A refused node and an absent one answer alike, so a condition cannot be the way to
            // tell them apart: whatever tag is named, a client that may not reach the node learns
            // nothing about it.
            _vault.Granting(_group, Rules.Granting(("agent", "L")));

            foreach (var tag in new[] { "*", "\"00000000000000000000000000000000\"" })
            {
                Assert.That(Conditioned("GET", "/entries/" + _entry, null, tag).Status, Is.EqualTo(404), tag);
                Assert.That(Conditioned("PUT", "/entries/" + _entry, "{\"Title\":\"x\"}", tag).Status,
                    Is.EqualTo(404), tag);
            }
        }

        [Test]
        public void A_route_whose_target_has_no_tag_does_not_read_the_condition()
        {
            // A listing answers about a collection and a create is sent to one, and neither has a
            // tag to match, so the document declares no condition there and none is asked.
            _vault.Granting(_vault.RootAlias, AllButProtected);
            var nothing = "\"00000000000000000000000000000000\"";

            Assert.That(Conditioned("GET", "/entries", null, nothing).Status, Is.EqualTo(200));
            Assert.That(Conditioned("GET", "/groups/" + _group + "/entries", null, nothing).Status, Is.EqualTo(200));
            Assert.That(Conditioned("POST", "/groups/" + _group + "/entries", "{\"Title\":\"x\"}", nothing).Status,
                Is.EqualTo(201));
        }

        [Test]
        public void A_client_that_may_not_read_an_entry_s_attachments_is_handed_the_tag_of_what_it_reads()
        {
            // An entry's tag covers what its caller may read, so a rule whose `read` does not
            // reach the attachments is handed a tag that nothing about them is in: a tag covering
            // them would let it test guesses at what it may not read by writing them where it
            // may write and watching the tag that came back.
            _vault.Granting(_group, Rules.Granting(("agent", "RUL")));
            _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultAskings.Free);
            var entry = "/entries/" + _entry;
            var tag = Get(entry).Tag;

            Assert.That(tag, Does.Match("^\"[0-9a-f]{32}\"$"));
            Assert.That(tag, Is.Not.EqualTo("\"" + _vault.EntryTag(_entry) + "\""), "and not the tag of the whole entry");

            foreach (var path in new[] { entry, entry + "/fields", entry + "/fields/Title" })
            {
                foreach (var method in new[] { "GET", "HEAD" })
                {
                    var answer = Send(method, path, null);

                    Assert.That(answer.Status, Is.EqualTo(200), method + " " + path);
                    Assert.That(answer.Tag, Is.EqualTo(tag), method + " " + path);
                }
            }

            // An attachment replaced, added and taken off by somebody else moves nothing this
            // caller may read, so its tag still holds and its write goes ahead.
            Assert.That(_vault.UpdateAttachment(_entry, "notes.txt", new byte[] { 1, 2 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(_vault.CreateAttachment(_entry, "more.txt", new byte[] { 3 }, VaultAskings.Free).Ok, Is.True);
            Assert.That(Get(entry).Tag, Is.EqualTo(tag));

            var served = Conditioned("PUT", entry, "{\"Title\":\"renamed\"}", tag);
            Assert.That(served.Status, Is.EqualTo(200), served.Json);
            Assert.That(served.Written, Is.Not.Null.And.Not.EqualTo(tag));
            Assert.That(served.Written, Is.EqualTo(Get(entry).Tag), "the tag of this caller's view of what was written");

            // The condition is asked of that tag as of any: `*` holds, and a tag the view has
            // moved on from, or never had, fails and changes nothing.
            Assert.That(Conditioned("GET", entry, null, "*").Status, Is.EqualTo(200));
            foreach (var condition in new[] { tag, "\"00000000000000000000000000000000\"" })
            {
                Assert.That(Conditioned("GET", entry, null, condition).Status, Is.EqualTo(412), condition);

                var refused = Conditioned("PUT", entry, "{\"Title\":\"renamed again\"}", condition);
                Assert.That(refused.Status, Is.EqualTo(412), condition);
                Assert.That(_vault.Field(_entry, "Title"), Is.EqualTo("renamed"), condition);
            }

            // A rule widened so that more of the entry comes into sight is another view, and
            // the tag of the narrower view fails against it once.
            var narrow = Get(entry).Tag;
            _vault.Granting(_group, AllButProtected);
            Assert.That(Get(entry).Tag, Is.Not.Null.And.Not.EqualTo(narrow));
            Assert.That(Conditioned("PUT", entry, "{\"Title\":\"renamed again\"}", narrow).Status, Is.EqualTo(412));
        }

        [Test]
        public void An_entry_holding_a_protected_value_is_tagged_for_a_rule_that_does_not_read_it_by_what_the_rule_reads()
        {
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            var entry = "/entries/" + _entry;

            _vault.Granting(_group, AllButProtected);
            var narrow = Get(entry).Tag;
            Assert.That(narrow, Is.Not.Null, "read reaching attachments and not protected values");
            Assert.That(Get(entry).Json, Does.Not.Contain("not-a-real-password"));

            // The withheld value changed by somebody else: nothing this caller reads moved, so
            // its tag did not, and its write under that tag goes ahead.
            _vault.Plant(_entry, "Password", "another-invented-password");
            Assert.That(Get(entry).Tag, Is.EqualTo(narrow));
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"x\"}", "*").Status, Is.EqualTo(200));
            narrow = Get(entry).Tag;
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"y\"}", narrow).Status, Is.EqualTo(200));
            narrow = Get(entry).Tag;

            _vault.Granting(_group,
                Rules.Granting(("agent", "CRUSDLAP")));
            var tag = Get(entry).Tag;
            Assert.That(tag, Is.Not.Null.And.Not.EqualTo(narrow), "a rule reading the value holds another tag for the entry");
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"z\"}", narrow).Status, Is.EqualTo(412),
                "and the tag of a view that leaves the value out is not its tag");

            _vault.Plant(_entry, "Password", "not-a-real-password");
            Assert.That(Get(entry).Tag, Is.Not.EqualTo(tag), "which moves with the value");
            tag = Get(entry).Tag;

            var written = Conditioned("PUT", entry, "{\"UserName\":\"x\"}", tag);
            Assert.That(written.Status, Is.EqualTo(200), written.Json);
            Assert.That(written.Written, Is.EqualTo(Get(entry).Tag));
        }

        [Test]
        public void An_entry_made_as_KeePass_makes_one_is_tagged_for_a_rule_not_reaching_protected_values()
        {
            // The tag tests above run on an entry holding only its title. KeePass and `createEntry`
            // give every entry all five standard strings, and the database protects the password
            // however empty it is, so every such entry holds a protected value from the start: a
            // rule without `protected` is handed the tag of its own view of the entry, and its
            // conditions are asked of that tag.
            _vault.Granting(_group, AllButProtected);
            var made = Send("POST", "/groups/" + _group + "/entries", Bodies.Of(("Title", "an invented entry")));
            Assert.That(made.Status, Is.EqualTo(VaultHandlers.Created), made.Json);
            var entry = "/entries/" + JsonBody.Read(made.Json).Text("uuid");

            Assert.That(Get(entry).Status, Is.EqualTo(200));
            var narrow = Get(entry).Tag;
            Assert.That(narrow, Is.Not.Null);
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"x\"}", narrow).Status, Is.EqualTo(200));
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"y\"}", "*").Status, Is.EqualTo(200));
            narrow = Get(entry).Tag;

            _vault.Granting(_group, Rules.Granting(("agent", "CRUSDLAP")));
            var tag = Get(entry).Tag;
            Assert.That(tag, Is.Not.Null.And.Not.EqualTo(narrow),
                "an empty password the database protects is a protected value, and in this view");
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"x\"}", tag).Status, Is.EqualTo(200));
        }

        [Test]
        public void A_client_that_may_set_a_protected_value_and_not_read_the_entry_is_handed_no_tag()
        {
            // The guessing attack of `API.md` §410 from its first side: `update` and `protected`
            // with no `read`. A tag here would be a tag of nothing the caller reads, so there is
            // none, on the routes `list` opens and in the answer to a change, and a condition
            // from this caller fails and is never ignored.
            _vault.Plant(_entry, "Password", "not-a-real-password");
            _vault.Granting(_group, Rules.Granting(("agent", "ULP")));
            var entry = "/entries/" + _entry;

            Assert.That(Get(entry).Status, Is.EqualTo(404));
            Assert.That(Get(entry + "/fields").Status, Is.EqualTo(200));
            Assert.That(Get(entry + "/fields").Tag, Is.Null);

            // A right guess and a wrong guess, each sent at the same stored value, are answered
            // alike and with no tag. That both are written like any change is the real vault's
            // to show, `SkippedOnlyForAReaderTests`: this vault writes every update.
            var answers = new List<string>();
            foreach (var guess in new[] { "not-a-real-password", "a-wrong-guess" })
            {
                _vault.Plant(_entry, "Password", "not-a-real-password");
                var written = Send("PUT", entry + "/fields/Password", Bodies.Of(("value", guess)));

                Assert.That(written.Status, Is.EqualTo(200), written.Json);
                Assert.That(written.Written, Is.Null, guess);
                Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo(guess));
                answers.Add(written.Json);
            }

            Assert.That(answers[1], Is.EqualTo(answers[0]), "the answer to a wrong guess and to a right guess");
            _vault.Plant(_entry, "Password", "not-a-real-password");

            foreach (var condition in new[] { "*", "\"00000000000000000000000000000000\"" })
            {
                Assert.That(Conditioned("GET", entry + "/fields", null, condition).Status, Is.EqualTo(412), condition);
                Assert.That(
                    Conditioned("PUT", entry + "/fields/Password", Bodies.Of(("value", "overwritten")), condition).Status,
                    Is.EqualTo(412),
                    condition);
                Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("not-a-real-password"), condition);
            }
        }

        [Test]
        public void Every_change_by_a_client_without_read_answers_no_tag_and_fails_any_condition()
        {
            // Every verb and both switches but `read`: each change this client may make to an
            // entry is refused under any condition, `*` among them, with nothing changed, and
            // made without a condition answers with no tag.
            _vault.Granting(_group, Rules.Granting(("agent", "CUSDLAP")));
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultAskings.Free);
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);
            var entry = "/entries/" + _entry;

            var changes = new[]
            {
                new[] { "PUT", entry, "{\"Title\":\"renamed\"}" },
                new[] { "POST", entry + "/fields", Bodies.Of(("name", "Other"), ("value", "invented")) },
                new[] { "PUT", entry + "/fields/Account", Bodies.Of(("value", "changed")) },
                new[] { "DELETE", entry + "/fields/Account", null },
                new[] { "POST", entry + "/attachments", Bodies.Of(("name", "other.txt"), ("content", Base64("invented"))) },
                new[] { "PUT", entry + "/attachments/notes.txt", Bodies.Of(("content", Base64("changed"))) },
                new[] { "DELETE", entry + "/attachments/notes.txt", null },
                new[] { "DELETE", entry, null },
            };

            var before = _vault.EntryTag(_entry);
            foreach (var condition in new[] { "*", "\"00000000000000000000000000000000\"" })
            {
                foreach (var change in changes)
                {
                    Assert.That(Conditioned(change[0], change[1], change[2], condition).Status, Is.EqualTo(412),
                        change[0] + " " + change[1] + " under " + condition);
                }
            }

            Assert.That(_vault.EntryTag(_entry), Is.Not.Null.And.EqualTo(before), "a refused change changed the entry");

            foreach (var change in changes)
            {
                var made = Send(change[0], change[1], change[2]);

                Assert.That(made.Status, Is.EqualTo(200).Or.EqualTo(201), change[0] + " " + change[1] + " " + made.Json);
                Assert.That(made.Written, Is.Null, change[0] + " " + change[1]);
            }

            Assert.That(_vault.Entry(_entry), Is.Null, "the changes were made, the last of them the removal");
        }

        [Test]
        public void A_client_reading_through_one_rule_and_setting_protected_values_through_another_holds_a_tag_the_password_is_not_in()
        {
            // The same attack from its other side: `read` without `protected` in one rule,
            // `update` with `protected` in another. The caller is handed a tag, and nothing
            // derived from the password is in it, so a right guess and a wrong guess, each sent
            // at the same stored value, are answered alike.
            //
            // **The tag coming back unchanged is this vault's, which keeps no times.** The real
            // vault writes a blind guess like any change, right or wrong, which moves `modified`
            // and with it the tag, for both alike: `TagOfTheCallersViewTests` holds it to that.
            // What is pinned here is the half a vault without times can show: the value written
            // is not in the tag.
            _profiles = new[] { "reader", "writer" };
            _vault.Plant(_entry, "Password", "not-a-real-password");
            _vault.Granting(_group, Rules.Granting(("reader", "RL"), ("writer", "UP")));
            var entry = "/entries/" + _entry;

            var tag = Get(entry).Tag;
            Assert.That(tag, Is.Not.Null);
            Assert.That(Get(entry).Json, Does.Not.Contain("not-a-real-password"));

            var answers = new List<string>();
            foreach (var guess in new[] { "not-a-real-password", "a-wrong-guess" })
            {
                _vault.Plant(_entry, "Password", "not-a-real-password");
                var written = Conditioned("PUT", entry + "/fields/Password", Bodies.Of(("value", guess)), tag);

                Assert.That(written.Status, Is.EqualTo(200), guess + ": " + written.Json);
                Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo(guess));
                Assert.That(written.Written, Is.EqualTo(tag), guess);
                Assert.That(Get(entry).Tag, Is.EqualTo(tag), guess);
                answers.Add(written.Json);
            }

            Assert.That(answers[1], Is.EqualTo(answers[0]), "the answer to a wrong guess and to a right guess");

            _vault.Plant(_entry, "Password", "changed-by-the-operator");
            Assert.That(Get(entry).Tag, Is.EqualTo(tag));

            Assert.That(Send("PUT", entry, "{\"Title\":\"renamed\"}").Status, Is.EqualTo(200));
            Assert.That(Get(entry).Tag, Is.Not.EqualTo(tag), "while a string it reads moves the tag as ever");
        }

        [Test]
        public void A_protected_value_arriving_during_a_request_is_never_covered_by_the_tag_it_hands_out(
            [Values("entry", "field", "fields", "attachments", "attachment", "write")] string route,
            [Range(0, 6)] int lookups)
        {
            // This rule reads everything but protected values, so the tag it is handed covers no
            // protected value. A read decides that inside one hold of KeePass's thread and
            // takes an entry's tag of the view it took there, so a protected value asked for
            // under the read lands once the hold is let go and is in neither the view nor its
            // tag, and a change decides it inside its own turn. Either way no value landing
            // during the request goes out under a tag that covers it. Tried at every moment of
            // each route rather than at a moment counted, so that what is pinned is the tag handed
            // out, not where the question is asked.
            //
            // The entry holds a password from the start, so the tag of this caller's view and
            // the tag of the whole entry differ at every moment, and a tag taken with a wider
            // sight on any route fails here whenever the value lands.
            _vault.Granting(_group, AllButProtected);
            _vault.Plant(_entry, "Password", "not-a-real-password");
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);
            var entry = "/entries/" + _entry;

            var landed = false;
            _vault.After(lookups, () =>
            {
                landed = true;
                _vault.CreateField(_entry, "PIN", "not-a-real-pin", true, VaultAskings.Free);
            });

            Answer answer;
            switch (route)
            {
                case "entry":
                    answer = Get(entry);
                    break;
                case "field":
                    answer = Get(entry + "/fields/Title");
                    break;
                case "fields":
                    answer = Get(entry + "/fields");
                    break;
                case "attachments":
                    answer = Get(entry + "/attachments");
                    break;
                case "attachment":
                    answer = Get(entry + "/attachments/notes.txt");
                    break;
                default:
                    answer = Send("PUT", entry, "{\"Title\":\"what the client meant\"}");
                    break;
            }

            _vault.After(0, null);
            var handed = route == "write" ? answer.Written : answer.Tag;

            // The tag handed out is the tag of this caller's view of the entry, as the entry
            // stood with the value landed or as it stood without, and never anything else: a
            // whole view's tag, with or without the value, is neither.
            var narrow = new VaultSight(true, false, VaultAttachmentSight.Read);
            var views = new List<string> { "\"" + _vault.View(_entry, found => narrow).Tag + "\"" };

            if (landed)
            {
                Assume.That(_vault.DeleteField(_entry, "PIN", VaultAskings.Free).Ok, Is.True);
                views.Add("\"" + _vault.View(_entry, found => narrow).Tag + "\"");
            }

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(views, Does.Contain(handed),
                "handed a tag that is not the tag of this caller's view: " + answer.Json);
            Assert.That(views, Does.Not.Contain("\"" + _vault.EntryTag(_entry) + "\""),
                "the whole entry and this caller's view have to differ for this to pin anything");
        }

        [Test]
        public void A_group_s_tag_is_for_a_rule_that_reads_the_group()
        {
            _vault.Granting(_group, Rules.Granting(("agent", "U")));
            var group = "/groups/" + _group;

            Assert.That(Conditioned("PUT", group, "{\"Notes\":\"invented\"}", "*").Status, Is.EqualTo(412));

            var served = Send("PUT", group, "{\"Notes\":\"invented\"}");
            Assert.That(served.Status, Is.EqualTo(200), served.Json);
            Assert.That(served.Written, Is.Null);

            _vault.Granting(_group, Rules.Granting(("agent", "RU")));
            var written = Conditioned("PUT", group, "{\"Notes\":\"invented again\"}", Get(group).Tag);
            Assert.That(written.Status, Is.EqualTo(200), written.Json);
            Assert.That(written.Written, Is.EqualTo(Get(group).Tag));
        }

        [Test]
        public void A_write_that_changes_nothing_still_asks_the_condition()
        {
            // An update naming nothing, or only what is there, writes nothing and still answers
            // as an update does, `412` included: the client asked to be told if the entry moved on.
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;
            var stale = Get(entry).Tag;
            Send("PUT", entry, "{\"Title\":\"changed by somebody else\"}");

            Assert.That(Conditioned("PUT", entry, "{}", stale).Status, Is.EqualTo(412));
            Assert.That(Conditioned("PUT", entry, "{\"Title\":\"changed by somebody else\"}", stale).Status,
                Is.EqualTo(412), "and one naming only what is there");
            Assert.That(Conditioned("PUT", entry, "{}", Get(entry).Tag).Written, Is.EqualTo(Get(entry).Tag));

            _vault.Busy = "a dialog is open on it";
            Assert.That(Conditioned("PUT", entry, "{}", stale).Status, Is.EqualTo(503),
                "a dialog open on the node comes before the condition, as it does for any update");
        }

        [TestCase("entry", 0)]
        [TestCase("entry", 1)]
        [TestCase("field", 0)]
        [TestCase("field", 1)]
        public void A_change_landing_anywhere_before_the_write_is_still_refused(string route, int lookups)
        {
            // The condition is asked inside the write, of the node as the write finds it. Asked of
            // a handler's own read instead, a change landing between that read and the write was
            // carried straight over, and answered 200. Both routes ask the vault once before they
            // write, which is the router's lookup, and the write is the second, so these are every
            // moment before it.
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;
            var tag = Get(entry).Tag;
            var landed = false;

            _vault.After(lookups, () =>
            {
                landed = true;
                _vault.UpdateEntry(
                    _entry, new Dictionary<string, string> { { "Title", "changed by somebody else" } }, VaultAskings.Free);
            });

            var answer = route == "entry"
                ? Conditioned("PUT", entry, "{\"Title\":\"what the first client meant\"}", tag)
                : Conditioned("PUT", entry + "/fields/Title", "{\"value\":\"what the first client meant\"}", tag);

            Assert.That(landed, Is.True, "every one of these lands inside the request");
            Assert.That(answer.Status, Is.EqualTo(412), answer.Json);
            Assert.That(_vault.Field(_entry, "Title"), Is.EqualTo("changed by somebody else"));
        }

        [Test]
        public void A_write_that_landed_is_never_answered_busy(
            [Values("entry", "field", "new field")] string route,
            [Range(0, 6)] int lookups)
        {
            // A write answered with its tag reads the entry again once the change is saved, to
            // confirm this client may still be handed the tag. That read meeting KeePass's thread
            // unable to be held answered `503 busy` for a change already on the disk, and the
            // client's retry met its own work. Tried at every moment of the request, so that
            // whatever the count, a change that landed is answered as a change that landed.
            _vault.Granting(_group, AllButProtected);
            var entry = "/entries/" + _entry;
            var tag = Get(entry).Tag;
            Assume.That(tag, Is.Not.Null, "this client is handed the tag, which is what this is about");

            var thrown = false;
            _vault.After(lookups, () =>
            {
                thrown = true;
                throw new KeePassGate.Diagnostics.VaultUnsettled("KeePass's thread could not be held");
            });

            Answer answer;
            switch (route)
            {
                case "entry":
                    answer = Conditioned("PUT", entry, "{\"Title\":\"what the client meant\"}", tag);
                    break;
                case "field":
                    answer = Conditioned("PUT", entry + "/fields/Title", "{\"value\":\"what the client meant\"}", tag);
                    break;
                default:
                    answer = Conditioned(
                        "POST", entry + "/fields", Bodies.Of(("name", "Added"), ("value", "what the client meant")), tag);
                    break;
            }

            _vault.After(0, null);
            var landed = route == "new field"
                ? _vault.Field(_entry, "Added") != null
                : _vault.Field(_entry, "Title") == "what the client meant";

            if (landed)
            {
                Assert.That(answer.Status, Is.EqualTo(route == "new field" ? VaultHandlers.Created : 200), answer.Json);
            }
            else
            {
                Assert.That(thrown, Is.True, "nothing but the throw keeps the write from landing");
                Assert.That(answer.Status, Is.EqualTo(503), answer.Json);
            }
        }

        [Test]
        public void A_read_never_serves_content_older_than_its_tag(
            [Values("group", "entry", "field", "attachment", "fields", "attachments")] string route,
            [Range(0, 5)] int lookups)
        {
            // A read takes what it serves inside one hold of KeePass's thread, a group's tag
            // and content there and an entry's both of the view taken there, so a change asked
            // for under the read lands once the hold is over and is in neither, and a write
            // under the read's tag is refused. Read under holds of their own, a newer
            // tag could come beside older content, and a write under that tag would be carried over
            // a change the client never saw. So whenever during the read a change is asked for,
            // content that does not show the change has to come with a tag that refuses a write.
            // Each read is tried at every moment rather than at a moment counted, so that the pair
            // is what is pinned, not the count.
            _vault.Granting(_group, AllButProtected);
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            var entry = "/entries/" + _entry;
            string path;
            Action change;
            string changed;
            string write;
            string body;
            switch (route)
            {
                case "group":
                    path = "/groups/" + _group;
                    change = () => _vault.UpdateGroup(_group, "renamed by somebody else", null, VaultAskings.Free);
                    changed = "renamed by somebody else";
                    write = path;
                    body = "{\"Name\":\"what the first client meant\"}";
                    break;
                case "attachment":
                    path = entry + "/attachments/notes.txt";
                    change = () => _vault.UpdateAttachment(_entry, "notes.txt", new byte[] { 2 }, VaultAskings.Free);
                    changed = Convert.ToBase64String(new byte[] { 2 });
                    write = path;
                    body = "{\"content\":\"" + Convert.ToBase64String(new byte[] { 3 }) + "\"}";
                    break;
                // The two listings serve names, so the change each has to show is a name.
                case "fields":
                    path = entry + "/fields";
                    change = () => _vault.CreateField(_entry, "Added by somebody else", "x", false, VaultAskings.Free);
                    changed = "Added by somebody else";
                    write = entry;
                    body = "{\"Title\":\"what the first client meant\"}";
                    break;
                case "attachments":
                    path = entry + "/attachments";
                    change = () => _vault.CreateAttachment(
                        _entry, "added by somebody else.txt", new byte[] { 2 }, VaultAskings.Free);
                    changed = "added by somebody else.txt";
                    write = entry + "/attachments/notes.txt";
                    body = "{\"content\":\"" + Convert.ToBase64String(new byte[] { 3 }) + "\"}";
                    break;
                default:
                    path = route == "entry" ? entry : entry + "/fields/Title";
                    change = () => _vault.UpdateEntry(
                        _entry, new Dictionary<string, string> { { "Title", "changed by somebody else" } }, VaultAskings.Free);
                    changed = "changed by somebody else";
                    write = entry;
                    body = "{\"Title\":\"what the first client meant\"}";
                    break;
            }

            var landed = false;
            _vault.After(lookups, () =>
            {
                landed = true;
                change();
            });

            var read = Get(path);

            // Disarmed, so that a change this read never reached lands nowhere rather than inside
            // the write below.
            _vault.After(0, null);
            Assume.That(read.Status, Is.EqualTo(200), read.Json);
            Assume.That(read.Tag, Is.Not.Null, "this client is handed the tag, which is what this is about");

            var answer = Conditioned("PUT", write, body, read.Tag);

            if (!landed)
            {
                // The read was over before this moment, so nothing changed under it.
                Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            }
            else if (!read.Json.Contains(changed))
            {
                Assert.That(answer.Status, Is.EqualTo(412),
                    "served without the change, under a tag that let a write go over it: " + answer.Json);
            }

            // Content showing the change may come with either tag, and either answer is right: the
            // client saw what it is writing over.
        }

        [Test]
        public void Reading_an_entry_gives_its_fields_and_says_which_are_protected()
        {
            _vault.Granting(_group, AllButProtected);
            Send("PUT", "/entries/" + _entry, "{\"UserName\":\"agent-fictitious\"}");

            var answer = Get("/entries/" + _entry);

            Assert.That(answer.Json, Does.Contain("\"UserName\":\"agent-fictitious\""));
            Assert.That(answer.Json, Does.Contain("\"protected\":["));
        }

        [Test]
        public void A_protected_value_cannot_be_overwritten_without_the_scope_that_reads_one()
        {
            // The half that is easy to forget. Reading a credential is the attack everybody
            // pictures; replacing one with a credential the caller already knows is the quieter
            // one, and an audit line saying an update happened does not tell them apart.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "CRUL")));

            var answer = Send("PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"chosen-by-the-caller\"}");

            Assert.That(answer.Status, Is.EqualTo(404), "a refusal looks like everything else this rule cannot reach");
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("not-a-real-password"));
        }

        [Test]
        public void A_protected_field_is_not_reached_through_a_name_the_database_cannot_tell_from_it()
        {
            // The guard cases above pass on the exact spelling and could say nothing about this
            // one while the fake keyed its names byte for byte: on the fake the alias was simply
            // absent, and on a real database it is the stored key. A client holding `update`, or
            // `strip` and `create`, and not `protected` was judged on the alias and reached the
            // credential.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "CRUSL")));

            var aliased = "/entries/" + _entry + "/fields/" + PasswordAliasPath;

            Assert.That(Send("PUT", aliased, "{\"value\":\"chosen-by-the-caller\"}").Status, Is.EqualTo(404));
            Assert.That(Send("DELETE", aliased, null).Status, Is.EqualTo(404));
            Assert.That(Send("POST", "/entries/" + _entry + "/fields",
                Bodies.Of(("name", PasswordAlias), ("value", "chosen-by-the-caller"))).Status,
                Is.EqualTo(400), "and a name holding a format character is not made at all");

            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("not-a-real-password"));
        }

        [Test]
        public void Nor_is_it_reached_through_one_by_a_rule_that_may_have_protected_values()
        {
            // The other half, and the one the guard cannot answer: this rule may write the
            // password, so what refuses the alias is the name not being a name the entry carries.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RULP")));

            var change = "{\"value\":\"chosen-by-the-caller\"}";

            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/" + PasswordAliasPath, change).Status,
                Is.EqualTo(404), "a name the entry does not carry is nothing there");
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("not-a-real-password"));

            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/Password", change).Status,
                Is.EqualTo(200), "and the name it does carry still writes");
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("chosen-by-the-caller"));
        }

        [Test]
        public void An_honest_client_asking_under_a_spelling_the_entry_does_not_carry_is_told_so()
        {
            // The other end of the same class, and the one an honest client reaches. `readField`
            // fails closed on a name its ordinal loop never matched, so a field nothing protects
            // was answered `200` with a null value and `"protected":true`: a reason that was not
            // true, about a field the listing had just shown under its stored spelling, and which
            // the same client could still write under the spelling it asked with. The name that
            // is not the entry's own is nothing there, which is what every absent name gets.
            _vault.CreateField(_entry, "Cafe" + Combining, "an invented value", false, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var listed = Get("/entries/" + _entry + "/fields");
            Assert.That(listed.Json, Does.Contain("Cafe" + Combining), "the listing reports the stored spelling");

            var asked = Get("/entries/" + _entry + "/fields/Caf%C3%A9");

            Assert.That(asked.Status, Is.EqualTo(404));
            Assert.That(asked.Json, Does.Not.Contain("protected"), "and says nothing untrue about protection");

            var stored = Get("/entries/" + _entry + "/fields/Cafe%CC%81");

            Assert.That(stored.Status, Is.EqualTo(200), stored.Json);
            Assert.That(stored.Json, Does.Contain("\"value\":\"an invented value\""));
            Assert.That(stored.Json, Does.Contain("\"protected\":false"));
        }

        [Test]
        public void A_protected_value_is_still_withheld_under_the_spelling_that_is_there()
        {
            // The fail-closed default stays: what it is for is an entry that goes between the
            // value being read and the protection being decided, and that is a different thing
            // from a name the entry never carried.
            _vault.CreateField(_entry, "Recovery", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var answer = Get("/entries/" + _entry + "/fields/Recovery");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Json, Does.Contain("\"value\":null"));
            Assert.That(answer.Json, Does.Contain("\"protected\":true"));
        }

        [Test]
        public void An_attachment_asked_for_under_such_a_spelling_hands_nothing_over()
        {
            // `readAttachment` served the stored attachment's bytes under the caller's spelling,
            // and got there past the ceiling the listing is asked about first, because the listing
            // compares names the way the guards do and found nothing to measure.
            _vault.Granting(_group, AllButProtected);
            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("an invented attachment"));
            Send("POST", "/entries/" + _entry + "/attachments",
                Bodies.Of(("name", "notes.txt"), ("content", content)));
            var oversize = new byte[VaultHandlers.MaxAttachmentBytes + 1];
            Assert.That(_vault.CreateAttachment(_entry, "big.bin", oversize, VaultAskings.Free).Ok, Is.True);

            // Nothing is read at all. The name is asked for first, and the entry's tag, which
            // digests every attachment up to the ceiling and which the real vault decrypts them
            // to take, is taken only for an answer that hands it out.
            foreach (var spelling in new[] { "notes%C2%AD.txt", "big%C2%AD.bin" })
            {
                _vault.AttachmentReads = 0;
                var asked = Get("/entries/" + _entry + "/attachments/" + spelling);

                Assert.That(asked.Status, Is.EqualTo(404), spelling);
                Assert.That(asked.Json, Does.Not.Contain("content"), spelling);
                Assert.That(_vault.AttachmentReads, Is.Zero, spelling + " was read");
            }

            Assert.That(Get("/entries/" + _entry + "/attachments/notes.txt").Json,
                Does.Contain("\"content\":\"" + content + "\""), "and the stored name still answers");
        }

        [Test]
        public void A_rule_that_reaches_protected_values_may_overwrite_one()
        {
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "CRULP")));

            var answer = Send("PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"also-invented\"}");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("also-invented"));
        }

        [Test]
        public void A_protected_field_cannot_be_added_without_it_either()
        {
            _vault.Granting(_group, Rules.Granting(("agent", "CRUL")));

            var answer = Send("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"Recovery\",\"value\":\"invented\",\"protected\":\"true\"}");

            Assert.That(answer.Status, Is.EqualTo(404));
            Assert.That(_vault.Field(_entry, "Recovery"), Is.Null);
        }

        [Test]
        public void A_protected_marker_this_gate_cannot_read_refuses_the_body()
        {
            // Fail closed on a marker, like everything else here. Anything that was not the word
            // `true` used to mean "not protected", so a client that meant to store a secret stored
            // it in clear and was told 201 with nothing said. The document declares the two words
            // and this refuses a third spelling of them.
            _vault.Granting(_group, Rules.Granting(("agent", "CRLP")));

            foreach (var marker in new[] { "True", "TRUE", "yes", "1", "on", "" })
            {
                var answer = Send("POST", "/entries/" + _entry + "/fields",
                    Bodies.Of(("name", "Recovery"), ("value", "invented"), ("protected", marker)));

                Assert.That(answer.Status, Is.EqualTo(400), marker);
                Assert.That(answer.Json, Does.Contain("malformed"), marker);
                Assert.That(_vault.Field(_entry, "Recovery"), Is.Null, marker);
            }
        }

        [Test]
        public void A_field_said_to_be_unprotected_in_so_many_words_is_created()
        {
            // The other half of the marker, and the reason refusing what is neither costs an
            // honest caller nothing: a body may say either word, and saying neither leaves the
            // answer to the database.
            _vault.Granting(_group, Rules.Granting(("agent", "CRL")));

            var answer = Send("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"Invented\",\"value\":\"one\",\"protected\":\"false\"}");

            Assert.That(answer.Status, Is.EqualTo(201), answer.Json);
            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"Invented\":\"one\""));
            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"protected\":[]"));
        }

        [Test]
        public void A_protected_field_cannot_be_stripped_to_make_room_for_a_chosen_one()
        {
            // Two calls that add up to the overwrite `PUT /entries/{uuid}/fields/{name}` refuses.
            // The strip destroys the only mark saying the name was a secret, so the create that
            // follows reads as an ordinary one and puts the caller's value where the credential
            // was, in clear for every later rule holding `read` without the switch.
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "CRSL")));

            var stripped = Send("DELETE", "/entries/" + _entry + "/fields/PIN", null);

            Assert.That(stripped.Status, Is.EqualTo(404),
                "a refusal reads like everything else this rule cannot reach");
            Assert.That(_vault.Field(_entry, "PIN"), Is.EqualTo("not-a-real-secret"));
        }

        [Test]
        public void A_rule_with_nowhere_to_put_anything_back_strips_a_protected_field_as_before()
        {
            // `...S....` is what `DESIGN.md` §114 spells out and it is untouched. What the guard
            // asks about is `create`, not `strip`, so a rule that may take a protected string off
            // and may not add one takes it off without the switch.
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RSL")));

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(200));
            Assert.That(_vault.Field(_entry, "PIN"), Is.Null);
        }

        [Test]
        public void A_rule_that_reaches_protected_values_strips_one_and_puts_another_back()
        {
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(
                _group, Rules.Granting(("agent", "CRSLP")));

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(200));

            var made = Send("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"PIN\",\"value\":\"also-invented\",\"protected\":\"true\"}");

            Assert.That(made.Status, Is.EqualTo(201), made.Json);
            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"protected\":[\"PIN\"]"));
        }

        [Test]
        public void A_protected_field_is_kept_from_a_strip_when_any_profile_could_put_back_an_unprotected_value()
        {
            // S123, under the operator's ruling R10, each profile asked on its own. `tidier` may
            // create and strip and not reach protected values, so it could put its own value where
            // the credential was; `maker` reaching protected values changes nothing about that.
            // Asked of all the profiles at once, this strip went through.
            _profiles = new[] { "tidier", "maker" };
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("tidier", "CRSL"), ("maker", "CRLP")));

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(404));
            Assert.That(_vault.Field(_entry, "PIN"), Is.EqualTo("not-a-real-secret"));
        }

        [Test]
        public void A_protected_field_is_kept_from_a_strip_when_another_profile_could_put_back_an_unprotected_value()
        {
            // The other direction: `tidier` may strip and not create, and `maker` could put an
            // unprotected value back.
            _profiles = new[] { "tidier", "maker" };
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("tidier", "RSL"), ("maker", "CRL")));

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(404));
            Assert.That(_vault.Field(_entry, "PIN"), Is.EqualTo("not-a-real-secret"));
        }

        [Test]
        public void A_protected_field_is_stripped_when_every_profile_that_could_put_one_back_reaches_protected_values()
        {
            _profiles = new[] { "tidier", "maker" };
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("tidier", "RSL"), ("maker", "CRLP")));

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(200));
            Assert.That(_vault.Field(_entry, "PIN"), Is.Null);
        }

        [Test]
        public void The_entry_route_is_not_a_way_around_it()
        {
            // The same value, set through the entry rather than the field. A gate on one route
            // and not the other is not a gate.
            _vault.Granting(_group, Rules.Granting(("agent", "CRUL")));

            var answer = Send("PUT", "/entries/" + _entry, "{\"Password\":\"chosen-by-the-caller\"}");

            Assert.That(answer.Status, Is.EqualTo(404));
            Assert.That(_vault.Field(_entry, "Password"), Is.Null, "nothing was written");
        }

        [Test]
        public void A_body_sent_to_a_route_that_takes_none_is_an_undeclared_request()
        {
            // Read and then ignored, which is what a query was before it was refused: the sweep
            // came back whole to a caller that had asked for one group, and the delete was a
            // plain delete to one that had asked for more.
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var listed = Send("GET", "/entries", Bodies.Of(("group", _group)));
            var removed = Send("DELETE", "/entries/" + _entry, "{\"recursive\":\"true\"}");

            Assert.That(listed.Status, Is.EqualTo(404));
            Assert.That(listed.Json, Does.Contain("no such route"));
            Assert.That(listed.Json, Does.Not.Contain(_entry), "nothing of the sweep is handed out");
            Assert.That(removed.Status, Is.EqualTo(404));
            Assert.That(_vault.Entry(_entry), Is.Not.Null, "and nothing is removed");
        }

        [Test]
        public void A_listing_withholds_a_protected_title_without_the_switch()
        {
            // `protected` is "and the protected values too, for whichever verbs the rule grants",
            // and `list` is one of them. Without this a client holding `list` alone would be told a
            // title that `read` without `protected` refuses, which makes the smaller grant the more
            // revealing one.
            _vault.Protect(_entry, "Title");
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            var listed = Get("/entries");

            Assert.That(listed.Status, Is.EqualTo(200));
            Assert.That(listed.Json, Does.Contain("\"title\":null"),
                "a protected title reached a client that was not granted `protected`");
            Assert.That(listed.Json, Does.Not.Contain("an invented service"));
        }

        [Test]
        public void A_listing_withholds_a_protected_address_and_still_names_the_entry()
        {
            // Each value is withheld on its own account, and the question is asked only of an
            // entry with something protected to withhold: the title nobody protected is still
            // shown beside an address somebody did.
            _vault.CreateField(_entry, "URL", "https://invented.example", false, VaultAskings.Free);
            _vault.Protect(_entry, "URL");
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            var listed = Get("/entries");

            Assert.That(listed.Json, Does.Contain("\"url\":null"));
            Assert.That(listed.Json, Does.Not.Contain("invented.example"));
            Assert.That(listed.Json, Does.Contain("an invented service"));
        }

        [Test]
        public void A_listing_shows_a_protected_title_to_a_rule_that_reaches_one()
        {
            _vault.Protect(_entry, "Title");
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "LP")));

            var listed = Get("/entries");

            Assert.That(listed.Json, Does.Contain("an invented service"),
                "the switch is what makes it visible, and it did not");
        }

        [TestCase("sweep", "L", "RP", true, true, TestName = "A sweep shows a protected title a read serves, to read with protected beside list")]
        [TestCase("group", "L", "RP", true, true, TestName = "A group's listing shows a protected title a read serves, to read with protected beside list")]
        [TestCase("sweep", "LP", "R", true, true, TestName = "A read serves a protected title a sweep shows, to list with protected beside read")]
        [TestCase("group", "LP", "R", true, true, TestName = "A read serves a protected title a group's listing shows, to list with protected beside read")]
        [TestCase("sweep", "L", "R", false, true, TestName = "Neither a sweep nor a read shows a protected title to list beside read")]
        [TestCase("group", "L", "R", false, true, TestName = "Neither a group's listing nor a read shows a protected title to list beside read")]
        [TestCase("sweep", "LP", "", true, false, TestName = "A sweep shows a protected title to list with protected, which reads nothing")]
        [TestCase("group", "LP", "", true, false, TestName = "A group's listing shows a protected title to list with protected, which reads nothing")]
        public void A_listing_and_a_read_show_a_caller_the_same_of_a_protected_title_and_address(
            string listing, string lister, string reader, bool shown, bool reads)
        {
            // The one rule for both routes: a protected title or URL is shown to a caller that
            // holds `protected` beside `list` or beside `read` in one rule on the entry, on
            // whichever route its rules let it ask. So what a listing shows of the two strings is
            // what a read of the entry serves, whenever the caller may read the entry at all.
            _profiles = new[] { "lister", "reader" };
            _vault.CreateField(_entry, "URL", "https://invented.example", false, VaultAskings.Free);
            _vault.Protect(_entry, "Title");
            _vault.Protect(_entry, "URL");
            _vault.Plant(_entry, "Password", "not-a-real-password");
            _vault.Granting(_vault.RootAlias, Rules.Granting(("lister", lister), ("reader", reader)));

            var listed = Get(listing == "sweep" ? "/entries" : "/groups/" + _group + "/entries");

            Assert.That(listed.Status, Is.EqualTo(200), listed.Json);
            Assert.That(listed.Json.Contains("\"title\":\"an invented service\""), Is.EqualTo(shown), listed.Json);
            Assert.That(listed.Json.Contains("\"url\":\"https://invented.example\""), Is.EqualTo(shown), listed.Json);
            Assert.That(listed.Json.Contains("\"title\":null"), Is.EqualTo(!shown));
            Assert.That(listed.Json, Does.Not.Contain("not-a-real-password"));

            var read = Get("/entries/" + _entry);
            if (!reads)
            {
                Assert.That(read.Status, Is.EqualTo(404), "a caller that lists and does not read has no read");
                Assert.That(Get("/entries/" + _entry + "/fields").Tag, Is.Null, "and no tag");
                return;
            }

            Assert.That(read.Status, Is.EqualTo(200), read.Json);
            Assert.That(read.Json.Contains("\"Title\":\"an invented service\""), Is.EqualTo(shown), read.Json);
            Assert.That(read.Json.Contains("\"URL\":\"https://invented.example\""), Is.EqualTo(shown), read.Json);
            Assert.That(read.Json.Contains("\"Title\":null"), Is.EqualTo(!shown));

            // The tag covers the title exactly where the two routes show it.
            _vault.Plant(_entry, "Title", "renamed by the operator");
            Assert.That(Get("/entries/" + _entry).Tag != read.Tag, Is.EqualTo(shown));
        }

        [Test]
        public void A_listing_shows_a_protected_title_through_a_profile_that_reads_it_and_lists_nothing()
        {
            // The item is shown through the profile that may list it, and its protected title
            // through whichever profile the client holds carries `protected` beside `list` or
            // `read` on the entry, which can be another profile: profiles pool for a listing.
            _profiles = new[] { "lister", "viewer" };
            _vault.Protect(_entry, "Title");
            _vault.Granting(_group, Rules.Granting(("lister", "L"), ("viewer", "")));
            _vault.Granting(_entry, Rules.Granting(("viewer", "RP")));

            var listed = Get("/groups/" + _group + "/entries");

            Assert.That(listed.Status, Is.EqualTo(200), listed.Json);
            Assert.That(listed.Json, Does.Contain("\"title\":\"an invented service\""));
            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"Title\":\"an invented service\""));

            // And never through a profile the client does not hold.
            _profiles = new[] { "lister" };
            Assert.That(Get("/groups/" + _group + "/entries").Json, Does.Contain("\"title\":null"));
        }

        [Test]
        public void A_protected_value_is_withheld_from_a_rule_that_does_not_reach_protected_values()
        {
            // The grant this exists for. An agent that has to recognize an entry gets its title,
            // its address and the fields nobody protected, and is told nothing it could sign in
            // with. `list` says too little for that and `read` used to say too much.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.CreateField(_entry, "UserName", "agent-fictitious", false, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var answer = Get("/entries/" + _entry);

            Assert.That(answer.Status, Is.EqualTo(200));
            Assert.That(answer.Json, Does.Contain("\"UserName\":\"agent-fictitious\""),
                "what nobody protected is what the rule was for");
            Assert.That(answer.Json, Does.Contain("\"Password\":null"),
                "withheld rather than absent, so the client can tell the two apart");
            Assert.That(answer.Json, Does.Not.Contain("not-a-real-password"));
        }

        [Test]
        public void A_rule_that_reaches_protected_values_is_given_the_value_itself()
        {
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RLP")));

            var answer = Get("/entries/" + _entry);

            Assert.That(answer.Json, Does.Contain("\"Password\":\"not-a-real-password\""));
        }

        [Test]
        public void One_protected_field_read_on_its_own_is_withheld_the_same_way()
        {
            // The same question asked through the other route, because a value withheld from the
            // entry and handed over one level down would be no rule at all.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var answer = Get("/entries/" + _entry + "/fields/Password");

            Assert.That(answer.Status, Is.EqualTo(200), "the field is there, and its value is not for this caller");
            Assert.That(answer.Json, Does.Contain("\"value\":null"));
            Assert.That(answer.Json, Does.Contain("\"protected\":true"));
            Assert.That(answer.Json, Does.Not.Contain("not-a-real-password"));
        }

        [Test]
        public void A_field_is_added_changed_and_taken_off_again()
        {
            _vault.Granting(_group, AllButProtected);

            Assert.That(
                Send("POST", "/entries/" + _entry + "/fields", "{\"name\":\"Invented\",\"value\":\"one\"}").Status,
                Is.EqualTo(201));

            Assert.That(Get("/entries/" + _entry + "/fields/Invented").Json, Does.Contain("\"value\":\"one\""));

            Assert.That(
                Send("PUT", "/entries/" + _entry + "/fields/Invented", "{\"value\":\"two\"}").Status,
                Is.EqualTo(200));

            Assert.That(Get("/entries/" + _entry + "/fields/Invented").Json, Does.Contain("\"value\":\"two\""));

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/Invented", null).Status, Is.EqualTo(200));
            Assert.That(Get("/entries/" + _entry + "/fields/Invented").Status, Is.EqualTo(404));
        }

        [Test]
        public void Adding_a_field_that_is_already_there_is_a_conflict()
        {
            _vault.Granting(_group, AllButProtected);
            Send("POST", "/entries/" + _entry + "/fields", "{\"name\":\"Invented\",\"value\":\"one\"}");

            var again = Send("POST", "/entries/" + _entry + "/fields", "{\"name\":\"Invented\",\"value\":\"two\"}");

            Assert.That(again.Status, Is.EqualTo(409));
            Assert.That(again.Json, Does.Contain("conflict"));
        }

        [Test]
        public void Changing_a_field_that_is_not_there_says_there_is_nothing_there()
        {
            _vault.Granting(_group, AllButProtected);

            var answer = Send("PUT", "/entries/" + _entry + "/fields/NotThere", "{\"value\":\"x\"}");

            Assert.That(answer.Status, Is.EqualTo(404));
        }

        [Test]
        public void Listing_fields_names_them_without_quoting_them()
        {
            _vault.Granting(_group, AllButProtected);

            var answer = Get("/entries/" + _entry + "/fields");

            Assert.That(answer.Json, Does.Contain("\"name\":\"Title\""));
            Assert.That(answer.Json, Does.Not.Contain("an invented service"),
                "a listing says which fields exist and never what is in them");
        }

        [Test]
        public void An_attachment_goes_in_and_comes_back_out()
        {
            _vault.Granting(_group, AllButProtected);
            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("an invented attachment"));

            Assert.That(
                Send("POST", "/entries/" + _entry + "/attachments",
                    Bodies.Of(("name", "notes.txt"), ("content", content))).Status,
                Is.EqualTo(201));

            var read = Get("/entries/" + _entry + "/attachments/notes.txt");

            Assert.That(read.Json, Does.Contain("\"content\":\"" + content + "\""));
            Assert.That(read.Json, Does.Contain("\"size\":22"));
        }

        [Test]
        public void An_attachment_over_the_ceiling_is_refused_without_being_read()
        {
            // Building one of these costs about ten times its size in managed memory, all live at
            // once, and the transport serves thirty-two connections at a time inside KeePass. The
            // refusal has to come before the read, which is why this counts the reads.
            _vault.Granting(_group, AllButProtected);
            var oversize = new byte[VaultHandlers.MaxAttachmentBytes + 1];
            Assume.That(_vault.CreateAttachment(_entry, "big.bin", oversize, VaultAskings.Free).Ok, Is.True);

            _vault.AttachmentReads = 0;
            var read = Get("/entries/" + _entry + "/attachments/big.bin");

            Assert.That(read.Status, Is.EqualTo(413));
            Assert.That(
                read.Json,
                Does.Contain(VaultHandlers.MaxAttachmentBytes.ToString(CultureInfo.InvariantCulture)),
                "the refusal has to say what the ceiling is");
            Assert.That(_vault.AttachmentReads, Is.Zero,
                "an attachment refused for its size must not be decrypted to find that out");
        }

        [Test]
        public void An_attachment_a_vault_hands_over_past_the_ceiling_is_still_refused()
        {
            // A view measures an attachment and hands over no bytes of an attachment past the
            // ceiling. When it measures short and hands over more anyway, the check of what came
            // back is the only thing between that attachment and an answer over the ceiling.
            _vault.Granting(_group, AllButProtected);
            var oversize = new byte[VaultHandlers.MaxAttachmentBytes + 1];
            Assume.That(_vault.CreateAttachment(_entry, "big.bin", oversize, VaultAskings.Free).Ok, Is.True);
            _vault.IgnoresCeiling = true;

            _vault.AttachmentReads = 0;
            var read = Get("/entries/" + _entry + "/attachments/big.bin");

            Assert.That(_vault.AttachmentReads, Is.EqualTo(1),
                "the vault handed it over, so this is the check of what came back");
            Assert.That(read.Status, Is.EqualTo(413));
            Assert.That(read.Json, Does.Not.Contain("content"),
                "nothing of the attachment is handed out");
        }

        [Test]
        public void An_attachment_at_the_ceiling_is_still_handed_out()
        {
            // The boundary, on the side that has to keep working: exactly the ceiling is allowed,
            // and it is the same size a write accepts.
            _vault.Granting(_group, AllButProtected);
            var largest = new byte[VaultHandlers.MaxAttachmentBytes];
            Assume.That(_vault.CreateAttachment(_entry, "big.bin", largest, VaultAskings.Free).Ok, Is.True);

            var read = Get("/entries/" + _entry + "/attachments/big.bin");

            Assert.That(read.Status, Is.EqualTo(200));
            Assert.That(
                read.Json,
                Does.Contain("\"size\":" + VaultHandlers.MaxAttachmentBytes.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void An_attachment_written_at_the_ceiling_can_be_read_back()
        {
            // The round trip through the routes on both sides, which is what the ceiling promises:
            // the tests above plant their attachments in the vault and so never ask the write.
            _vault.Granting(_group, AllButProtected);
            var largest = Convert.ToBase64String(new byte[VaultHandlers.MaxAttachmentBytes]);

            Assert.That(Send("POST", "/entries/" + _entry + "/attachments",
                Bodies.Of(("name", "big.bin"), ("content", largest))).Status, Is.EqualTo(201));
            Assert.That(Send("PUT", "/entries/" + _entry + "/attachments/big.bin",
                Bodies.Of(("content", largest))).Status, Is.EqualTo(200));

            Assert.That(Get("/entries/" + _entry + "/attachments/big.bin").Status, Is.EqualTo(200));
        }

        [Test]
        public void A_write_one_byte_over_the_ceiling_is_refused()
        {
            // The one size the document cannot refuse: one byte over the ceiling is a multiple of
            // three and so spells as many base64 characters as the ceiling does. Stored, it would be
            // an attachment every read answers 413, so the write refuses it the way it refuses the
            // next size up, and leaves what was there alone.
            _vault.Granting(_group, AllButProtected);
            var over = Convert.ToBase64String(new byte[VaultHandlers.MaxAttachmentBytes + 1]);
            Assume.That(over.Length, Is.EqualTo(4 * ((VaultHandlers.MaxAttachmentBytes + 2) / 3)),
                "this is about the size the document's length admits");

            Assert.That(Send("POST", "/entries/" + _entry + "/attachments",
                Bodies.Of(("name", "big.bin"), ("content", over))).Status, Is.EqualTo(400));
            Assert.That(_vault.Whole(_entry).Attachments, Is.Empty, "nothing is stored");

            Assume.That(_vault.CreateAttachment(_entry, "small.bin", new byte[] { 1 }, VaultAskings.Free).Ok, Is.True);

            Assert.That(Send("PUT", "/entries/" + _entry + "/attachments/small.bin",
                Bodies.Of(("content", over))).Status, Is.EqualTo(400));
            Assert.That(_vault.Attachment(_entry, "small.bin"), Is.EqualTo(new byte[] { 1 }),
                "what was there is left alone");
        }

        [Test]
        public void An_entry_far_over_what_a_part_is_held_to_is_served_with_every_value_intact()
        {
            // A read of an entry was refused past half a megabyte of names and values, since its
            // answer was built whole and cost several times that in memory. The answer is written
            // as it is sent, a value at a time, so no read is refused for size: an entry of
            // several times that number is served, each value as it is stored, and each value
            // read a single time, as the answer is written.
            _vault.Granting(_group, AllButProtected);
            var big = new string('x', 4 * VaultHandlers.MaxPartCharacters);
            var other = new string('y', VaultHandlers.MaxPartCharacters + 1);
            _vault.Plant(_entry, "Big", big);
            _vault.Plant(_entry, "Other", other);

            _vault.StringReads = 0;
            var read = Get("/entries/" + _entry);

            Assert.That(read.Status, Is.EqualTo(200));
            Assert.That(read.Tag, Is.Not.Null);

            var fields = Newtonsoft.Json.Linq.JObject.Parse(read.Json)["fields"];
            Assert.That((string)fields["Big"] == big, Is.True, "the longest value came back other than it is stored");
            Assert.That((string)fields["Other"] == other, Is.True, "a value came back other than it is stored");
            Assert.That((string)fields["Title"], Is.EqualTo("an invented service"));
            Assert.That(_vault.StringReads, Is.EqualTo(_vault.Whole(_entry).Fields.Count),
                "each value is read where it is written, and nowhere else");
        }

        [Test]
        public void A_read_of_an_entry_reads_each_value_in_sight_a_single_time_and_no_withheld_value()
        {
            // The tag is taken from digests and reads nothing, so what is read is what the
            // answer writes: each value in the caller's sight, where it is written, and never a
            // value the caller's rule withholds.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            _vault.StringReads = 0;
            var narrow = Get("/entries/" + _entry);

            Assert.That(narrow.Status, Is.EqualTo(200), narrow.Json);
            Assert.That(narrow.Tag, Is.Not.Null);
            Assert.That(
                narrow.Json, Does.Contain("\"Account\":\"an invented account\"").And.Contain("\"Password\":null"));
            Assert.That(_vault.StringReads, Is.EqualTo(2),
                "`Title` and `Account` are in this caller's sight, and `Password` is not");

            _vault.Granting(_group, Rules.Granting(("agent", "RLP")));
            _vault.StringReads = 0;
            var whole = Get("/entries/" + _entry);

            Assert.That(whole.Json, Does.Contain("\"Password\":\"not-a-real-password\""));
            Assert.That(_vault.StringReads, Is.EqualTo(3), "every value, each a single time");
        }

        [Test]
        public void Asking_whether_an_entry_or_a_field_is_there_reads_no_value()
        {
            // A handler answering a read of an entry or of a field hands over what writes the
            // values and reads none of them itself, and the transport does not run that for a
            // HEAD, `HttpResponseTests`. So a HEAD is answered with the status and the tag a GET
            // carries, with no value read anywhere.
            _vault.Granting(_group, AllButProtected);
            _vault.Plant(_entry, "Account", "an invented account");

            foreach (var path in new[] { "/entries/" + _entry, "/entries/" + _entry + "/fields/Account" })
            {
                var got = Get(path);
                Assume.That(got.Status, Is.EqualTo(200), path);
                _vault.StringReads = 0;

                var head = Send("HEAD", path, null);

                Assert.That(head.Status, Is.EqualTo(200), path);
                Assert.That(head.Tag, Is.EqualTo(got.Tag), path);
                Assert.That(head.Json, Is.Empty, path);
                Assert.That(_vault.StringReads, Is.Zero, path + " read a value for an answer with no content");
            }
        }
        [TestCase("L")]
        [TestCase("LA")]
        public void A_caller_that_only_lists_is_served_names_flags_and_sizes_and_no_value_is_read(string mask)
        {
            // The view of a caller without `read` holds no value of any string and no bytes of
            // any attachment, so the routes `list` opens serve what they always served, names,
            // flags and sizes, and no value is read or digested for them. No tag either, since
            // that caller is handed none.
            _vault.Granting(_group, Rules.Granting(("agent", mask)));
            _vault.Plant(_entry, "Password", "not-a-real-password");
            Assume.That(_vault.CreateAttachment(_entry, "notes.txt", new byte[] { 1, 2, 3 }, VaultAskings.Free).Ok, Is.True);
            var entry = "/entries/" + _entry;

            _vault.StringReads = 0;
            _vault.StringDigests = 0;
            _vault.AttachmentReads = 0;

            var fields = Get(entry + "/fields");
            Assert.That(fields.Status, Is.EqualTo(200), fields.Json);
            Assert.That(fields.Json, Is.EqualTo(
                "{\"fields\":[{\"name\":\"Password\",\"protected\":true},{\"name\":\"Title\",\"protected\":false}]}"));
            Assert.That(fields.Tag, Is.Null);

            var attachments = Get(entry + "/attachments");
            if (mask == "LA")
            {
                Assert.That(attachments.Status, Is.EqualTo(200), attachments.Json);
                Assert.That(attachments.Json, Is.EqualTo("{\"attachments\":[{\"name\":\"notes.txt\",\"size\":3}]}"));
                Assert.That(attachments.Tag, Is.Null);
            }
            else
            {
                Assert.That(attachments.Status, Is.EqualTo(404));
            }

            Assert.That(Get(entry).Status, Is.EqualTo(404));
            Assert.That(Get(entry + "/fields/Title").Status, Is.EqualTo(404));
            Assert.That(Get(entry + "/attachments/notes.txt").Status, Is.EqualTo(404));
            Assert.That(_vault.StringReads, Is.Zero, "a value was read for a caller that only lists");
            Assert.That(_vault.StringDigests, Is.Zero, "a value was digested for a caller that only lists");
            Assert.That(_vault.AttachmentReads, Is.Zero, "an attachment was read for a caller that only lists");
        }

        [Test]
        public void A_field_far_over_what_a_part_is_held_to_is_served_whole_and_read_a_single_time()
        {
            // A field's answer is written as it is sent, as an entry's is, so a value of any
            // length is served, and the value asked for is the only value read.
            _vault.Granting(_group, AllButProtected);
            var big = new string('x', 4 * VaultHandlers.MaxPartCharacters);
            _vault.Plant(_entry, "Big", big);
            _vault.StringReads = 0;

            var read = Get("/entries/" + _entry + "/fields/Big");

            Assert.That(read.Status, Is.EqualTo(200));
            Assert.That(read.Tag, Is.Not.Null);

            var answer = Newtonsoft.Json.Linq.JObject.Parse(read.Json);
            Assert.That((string)answer["name"], Is.EqualTo("Big"));
            Assert.That((string)answer["value"] == big, Is.True, "the value came back other than it is stored");
            Assert.That((bool)answer["protected"], Is.False);
            Assert.That(_vault.StringReads, Is.EqualTo(1), "the value asked for, and no other value of the entry");
        }

        [TestCase(
            "\"00000000000000000000000000000000\"",
            false,
            TestName = "An attachment over the ceiling asked for under a stale tag is too large")]
        [TestCase(
            "*",
            true,
            TestName = "An attachment over the ceiling asked for under any tag beside a withheld value is too large")]
        public void An_attachment_over_the_ceiling_is_too_large_whatever_its_condition(
            string ifMatch, bool holdsProtected)
        {
            // RFC 9110 section 13.2.1 asks the condition only of a request that would otherwise
            // succeed, and the size does not depend on the tag. Asked first, a stale tag was told
            // `412`, read again, and only then told the read could not be served. A caller whose
            // view withholds a password is handed a tag like any reader, and `*` holds for it.
            _vault.Granting(_group, AllButProtected);
            if (holdsProtected)
            {
                _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            }

            Assume.That(_vault.CreateAttachment(
                _entry, "big.bin", new byte[VaultHandlers.MaxAttachmentBytes + 1], VaultAskings.Free).Ok, Is.True);

            var read = Conditioned("GET", "/entries/" + _entry + "/attachments/big.bin", null, ifMatch);

            Assert.That(read.Status, Is.EqualTo(413), read.Json);
        }

        [TestCase("", TestName = "An entry asked for under a stale tag is refused with no value read")]
        [TestCase("/fields/Big", TestName = "A field asked for under a stale tag is refused with no value read")]
        public void A_read_whose_condition_fails_reads_no_value_for_an_answer(string under)
        {
            // The condition is asked of the tag, which is taken from digests, and it is asked
            // before the answer is handed over. So a read refused by its condition has written
            // nothing and read nothing, however much the entry holds, and the same read under
            // the tag the entry has is served.
            _vault.Granting(_group, AllButProtected);
            _vault.Plant(_entry, "Big", new string('x', 2 * VaultHandlers.MaxPartCharacters));
            var path = "/entries/" + _entry + under;
            var tag = Get(path).Tag;
            _vault.StringReads = 0;

            var stale = Conditioned("GET", path, null, "\"00000000000000000000000000000000\"");

            Assert.That(stale.Status, Is.EqualTo(412), stale.Json);
            Assert.That(stale.Json, Does.Not.Contain("xxxx"), "nothing of the entry is handed out");
            Assert.That(_vault.StringReads, Is.Zero, "a value was read for an answer that was refused");

            var current = Conditioned("GET", path, null, tag);

            Assert.That(current.Status, Is.EqualTo(200));
            Assert.That(current.Json, Does.Contain("xxxx"));
        }

        [Test]
        public void A_write_however_far_it_grows_an_entry_is_made_and_the_entry_still_reads_back_whole()
        {
            // No write is refused for what an entry's strings come to, since the refusal told a
            // caller that may not read how long the values it was withheld were. No read is
            // refused for it either, so an entry grown past what a part of a listing is held to,
            // a write at a time, is read back whole and a field at a time.
            _vault.Granting(_group, AllButProtected);
            _vault.Plant(_entry, "Filler", new string('x', VaultHandlers.MaxPartCharacters));

            Assert.That(Send("POST", "/entries/" + _entry + "/fields",
                Bodies.Of(("name", "More"), ("value", new string('y', 10)))).Status, Is.EqualTo(201));
            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/More",
                Bodies.Of(("value", new string('y', 11)))).Status, Is.EqualTo(200));
            Assert.That(Send("PUT", "/entries/" + _entry, "{\"Notes\":\"z\"}").Status, Is.EqualTo(200),
                "an update naming a string the entry does not hold adds it");
            Assert.That(Send("POST", "/entries/" + _entry + "/fields",
                Bodies.Of(("name", "Again"), ("value", "w"))).Status, Is.EqualTo(201));

            var whole = Get("/entries/" + _entry);
            Assert.That(whole.Status, Is.EqualTo(200), "an entry is handed out whole whatever its strings come to");

            var fields = Newtonsoft.Json.Linq.JObject.Parse(whole.Json)["fields"];
            Assert.That(((string)fields["Filler"]).Length, Is.EqualTo(VaultHandlers.MaxPartCharacters));
            Assert.That((string)fields["More"], Is.EqualTo(new string('y', 11)));
            Assert.That((string)fields["Notes"], Is.EqualTo("z"));
            Assert.That((string)fields["Again"], Is.EqualTo("w"));

            Assert.That(Get("/entries/" + _entry + "/fields").Status, Is.EqualTo(200), "its names are listed");
            foreach (var name in new[] { "Title", "Filler", "More", "Notes", "Again" })
            {
                Assert.That(Get("/entries/" + _entry + "/fields/" + name).Status, Is.EqualTo(200), name);
            }
        }

        [Test]
        public void A_withheld_value_is_served_as_null_however_long_it_is_and_is_never_read()
        {
            // A protected string is no part of the answer to a caller that may not read
            // protected values, whatever its length: that caller is handed the entry with the
            // value as the `null` every withheld value is, and nothing reads or digests the
            // value. A caller that reads protected values is handed it whole.
            var big = new string('x', 4 * VaultHandlers.MaxPartCharacters);
            _vault.Plant(_entry, "Big", big);
            _vault.Protect(_entry, "Big");

            _vault.Granting(_group, AllButProtected);
            _vault.StringReads = 0;
            _vault.StringDigests = 0;
            var narrow = Get("/entries/" + _entry);

            Assert.That(narrow.Status, Is.EqualTo(200), narrow.Json);
            Assert.That(narrow.Json, Does.Contain("\"Big\":null"));
            Assert.That(_vault.StringReads, Is.EqualTo(1), "the title, and nothing of the withheld value");
            Assert.That(
                _vault.StringDigests, Is.EqualTo(1), "the title, and the withheld value is not digested either");

            _vault.StringReads = 0;
            var field = Get("/entries/" + _entry + "/fields/Big");
            Assert.That(field.Status, Is.EqualTo(200));
            Assert.That(field.Json, Is.EqualTo("{\"name\":\"Big\",\"value\":null,\"protected\":true}"));
            Assert.That(_vault.StringReads, Is.Zero, "a withheld field is answered with nothing read");

            _vault.Granting(_group, Rules.Granting(("agent", "CRUSDLAP")));

            var whole = Get("/entries/" + _entry);
            Assert.That(whole.Status, Is.EqualTo(200));
            Assert.That(
                (string)Newtonsoft.Json.Linq.JObject.Parse(whole.Json)["fields"]["Big"] == big,
                Is.True,
                "a caller reading protected values is handed the value as it is stored");

            var single = Get("/entries/" + _entry + "/fields/Big");
            Assert.That(single.Status, Is.EqualTo(200));
            Assert.That((string)Newtonsoft.Json.Linq.JObject.Parse(single.Json)["value"] == big, Is.True);
        }
        [TestCase("L")]
        [TestCase("RL")]
        public void A_listing_of_fields_past_what_an_answer_hands_out_is_served_in_parts(string mask)
        {
            // Nothing bounds how many strings writes give an entry, and the listing serves every
            // name, so the names alone can pass what an answer hands out. Refused for that, the
            // listing left such an entry with no route naming its strings. So it is served in
            // parts, for a caller that reads as for a caller that only lists: each part within
            // what a part is held to by what it writes, each naming where the part after it
            // starts, and every field in a single part.
            _vault.Granting(_group, Rules.Granting(("agent", mask)));
            var planted = new List<string> { "Title" };
            for (var index = 0; index < 2500; index++)
            {
                var numbered = new string('n', 504) + index.ToString("D8", CultureInfo.InvariantCulture);
                _vault.Plant(_entry, numbered, "v");
                planted.Add(numbered);
            }

            Assume.That(planted.Sum(name => name.Length), Is.GreaterThan(2 * VaultHandlers.MaxPartCharacters));

            var whole = Get("/entries/" + _entry);
            Assert.That(
                whole.Status,
                Is.EqualTo(mask == "L" ? 404 : 200),
                "a read of the entry is held to no size, for a caller that may read the entry at all");

            var listed = new List<string>();
            var parts = 0;
            var from = (string)null;
            while (true)
            {
                var part = from == null
                    ? Get("/entries/" + _entry + "/fields")
                    : Request("GET", "/entries/" + _entry + "/fields", null, null, null, "from", from);
                Assert.That(part.Status, Is.EqualTo(200), part.Json);
                Assert.That(part.Tag == null, Is.EqualTo(mask == "L"), "a tag on every part, for a caller that reads");
                parts++;

                var answer = Newtonsoft.Json.Linq.JObject.Parse(part.Json);
                var names = answer["fields"].Select(field => (string)field["name"]).ToList();
                Assert.That(names, Is.Not.Empty);
                Assert.That(
                    names.Sum(name => name.Length + 32),
                    Is.LessThanOrEqualTo(VaultHandlers.MaxPartCharacters),
                    "a part is held to what it writes");
                listed.AddRange(names);

                var next = answer["next"];
                if (next == null)
                {
                    break;
                }

                Assert.That((int)next, Is.EqualTo(listed.Count), "the position of the first field past the part");
                from = ((int)next).ToString(CultureInfo.InvariantCulture);
            }

            Assert.That(parts, Is.GreaterThan(2));
            Assert.That(listed, Is.EquivalentTo(planted));
            Assert.That(listed, Is.Unique);
        }

        [Test]
        public void A_listing_of_fields_that_fits_is_one_part_and_names_no_part_after_it()
        {
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));
            _vault.Plant(_entry, "PIN", "0000");

            var listed = Get("/entries/" + _entry + "/fields");

            Assert.That(listed.Status, Is.EqualTo(200), listed.Json);
            Assert.That(listed.Json, Does.Not.Contain("\"next\""));
            Assert.That(listed.Json, Does.Contain("\"PIN\""));
        }

        [Test]
        public void A_part_of_a_listing_is_cut_by_what_it_writes_and_not_by_its_names_alone()
        {
            // Very many short names come to little, and the item each is written in is ten times
            // a name of three characters. Counted by its names, such a listing was one answer of
            // several times what a part is held to.
            _vault.Granting(_group, Rules.Granting(("agent", "L")));
            var fields = (VaultHandlers.MaxPartCharacters / 35) + 50;
            for (var index = 0; index < fields; index++)
            {
                _vault.Plant(_entry, index.ToString("x5", CultureInfo.InvariantCulture), string.Empty);
            }

            var first = Get("/entries/" + _entry + "/fields");

            Assert.That(first.Status, Is.EqualTo(200), first.Json);
            var answer = Newtonsoft.Json.Linq.JObject.Parse(first.Json);
            Assert.That(answer["next"], Is.Not.Null, "the names come to under a part, and the items do not");
            Assert.That(first.Json.Length, Is.LessThan(VaultHandlers.MaxPartCharacters + 1024));
        }

        [Test]
        public void A_part_always_holds_a_field_however_long_its_name()
        {
            // A name KeePass or an import left longer than a whole part: the part that reaches it
            // holds it and nothing else, so asking for the part after it moves on.
            _vault.Granting(_group, Rules.Granting(("agent", "L")));
            _vault.Plant(_entry, new string('a', VaultHandlers.MaxPartCharacters + 10), "v");
            _vault.Plant(_entry, "b", "v");

            var first = Newtonsoft.Json.Linq.JObject.Parse(Get("/entries/" + _entry + "/fields").Json);
            var listed = first["fields"].Select(field => (string)field["name"]).ToList();
            var from = first["next"];

            while (from != null)
            {
                var part = Request(
                    "GET", "/entries/" + _entry + "/fields", null, null, null,
                    "from", ((int)from).ToString(CultureInfo.InvariantCulture));
                Assert.That(part.Status, Is.EqualTo(200), part.Json);

                var answer = Newtonsoft.Json.Linq.JObject.Parse(part.Json);
                Assert.That(answer["fields"].Count(), Is.GreaterThan(0));
                listed.AddRange(answer["fields"].Select(field => (string)field["name"]));
                from = answer["next"];
            }

            Assert.That(listed.Count, Is.EqualTo(3));
            Assert.That(listed, Does.Contain("b"));
        }

        [TestCase("1", 200)]
        [TestCase("0", 200)]
        [TestCase("999999999", 200)]
        [TestCase("", 400)]
        [TestCase("-1", 400)]
        [TestCase("+1", 400)]
        [TestCase(" 1", 400)]
        [TestCase("1.0", 400)]
        [TestCase("0x1", 400)]
        [TestCase("1234567890", 400)]
        [TestCase("\u0661", 400)]
        [TestCase("next", 400)]
        public void Where_a_part_starts_is_digits_or_the_request_is_refused(string from, int status)
        {
            // A position this cannot read is refused and never read as the start: a caller that
            // asked for a later part and spelled it badly would be handed the first part again
            // and take it for the part it asked for. A digit of another script is not a digit
            // here. A position past the last field is a part holding nothing.
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));
            _vault.Plant(_entry, "PIN", "0000");

            foreach (var method in new[] { "GET", "HEAD" })
            {
                var part = Request(method, "/entries/" + _entry + "/fields", null, null, null, "from", from);

                Assert.That(part.Status, Is.EqualTo(status), method + " " + part.Json);
                if (status == 400 && method == "GET")
                {
                    Assert.That(part.Json, Is.EqualTo(HttpResponse.ErrorBody(BodyCheck.Refusal)));
                }
            }

            if (from == "1")
            {
                var json = Request("GET", "/entries/" + _entry + "/fields", null, null, null, "from", from).Json;
                Assert.That(json, Does.Contain("\"Title\"").Or.Contain("\"PIN\""));
                Assert.That(
                    Newtonsoft.Json.Linq.JObject.Parse(json)["fields"].Count(), Is.EqualTo(1),
                    "the fields from the position named, and not the field before it");
            }

            if (from == "999999999")
            {
                var json = Request("GET", "/entries/" + _entry + "/fields", null, null, null, "from", from).Json;
                Assert.That(Newtonsoft.Json.Linq.JObject.Parse(json)["fields"].Count(), Is.Zero);
                Assert.That(json, Does.Not.Contain("\"next\""));
            }
        }

        [Test]
        public void A_part_asked_of_an_entry_the_caller_may_not_list_is_the_entry_not_being_there()
        {
            // Whatever the position asked for, a malformed position included: the rules answer
            // before the query is read, so nothing about the query tells a caller the entry is
            // there.
            _vault.Granting(_group, Rules.Granting(("agent", "R")));

            foreach (var from in new[] { "0", "7", "nonsense" })
            {
                var part = Request("GET", "/entries/" + _entry + "/fields", null, null, null, "from", from);

                Assert.That(part.Status, Is.EqualTo(404), from);
            }
        }

        [Test]
        public void Stripping_an_attachment_needs_no_switch()
        {
            _vault.Granting(_group, AllButProtected);
            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("an invented attachment"));

            Assert.That(
                Send("POST", "/entries/" + _entry + "/attachments",
                    Bodies.Of(("name", "notes.txt"), ("content", content))).Status,
                Is.EqualTo(201),
                "the attachment has to be there before removing it means anything");

            _vault.Granting(_group, Rules.Granting(("agent", "RSL")));

            Assert.That(Get("/entries/" + _entry + "/attachments").Status, Is.EqualTo(404),
                "reading which attachments are there still needs the switch");
            Assert.That(
                Send("DELETE", "/entries/" + _entry + "/attachments/notes.txt", null).Status,
                Is.EqualTo(200),
                "and taking one off does not, because removing shows nobody anything");
        }

        [Test]
        public void An_attachment_needs_the_switch_as_well_as_the_verb()
        {
            // The whole point of the seventh letter. Everything else is granted here and the
            // attachment routes are still refused.
            _vault.Granting(_group, Rules.Granting(("agent", "CRUDL")));

            Assert.That(Get("/entries/" + _entry + "/attachments").Status, Is.EqualTo(404));
            Assert.That(Get("/entries/" + _entry + "/attachments/notes.txt").Status, Is.EqualTo(404));
            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(200), "and the entry itself is not");
        }

        [Test]
        public void An_attachment_name_with_a_separator_in_it_is_addressable()
        {
            // Which needs the path to be split before it is decoded, and is why that was fixed
            // before any of this was written.
            _vault.Granting(_group, AllButProtected);
            var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });

            Send("POST", "/entries/" + _entry + "/attachments",
                Bodies.Of(("name", "a/b"), ("content", content)));

            var read = Get("/entries/" + _entry + "/attachments/a%2Fb");

            Assert.That(read.Status, Is.EqualTo(200));
            Assert.That(read.Json, Does.Contain("\"name\":\"a/b\""));
        }

        [Test]
        public void A_body_that_is_not_what_the_document_declares_is_refused_before_anything_happens()
        {
            _vault.Granting(_group, AllButProtected);

            var answer = Send("POST", "/entries/" + _entry + "/fields", "{\"value\":\"no name\"}");

            Assert.That(answer.Status, Is.EqualTo(400));
            Assert.That(answer.Json, Does.Contain("malformed"));
        }

        [Test]
        public void A_body_sent_under_another_media_type_is_refused_rather_than_read_as_json()
        {
            // The refusal nothing reached. `Typed` decides it from the one media type this API
            // declares, and until this ran the only evidence that it worked at all was reading it.
            _vault.Granting(_group, AllButProtected);

            var answer = Typed("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"invented\",\"value\":\"x\"}", "application/x-www-form-urlencoded");

            Assert.That(answer.Status, Is.EqualTo(415));
            Assert.That(answer.Json, Does.Contain("media type"));

            // RFC 9110 section 15.5.16. A status with no phrase behind it went out as
            // `Status 415`, which names nothing a client or a proxy can look up.
            Assert.That(HttpResponse.ReasonFor(415), Is.EqualTo("Unsupported Media Type"));
        }

        [Test]
        public void A_body_declaring_the_media_type_this_api_speaks_is_read_as_usual()
        {
            // The other half, because a check that refused everything would pass the first test.
            // A parameter after the type is the media type with a charset on it, not another one.
            _vault.Granting(_group, AllButProtected);

            var answer = Typed("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"invented\",\"value\":\"x\"}", "application/json; charset=utf-8");

            Assert.That(answer.Status, Is.EqualTo(201), answer.Json);
        }

        [Test]
        public void A_body_the_rules_would_have_refused_is_never_looked_at()
        {
            // The ordering the guard established and the rules inherited. A caller that may reach
            // nothing must not learn whether its body would have been accepted.
            var answer = Send("POST", "/entries/" + _entry + "/fields", "{\"nonsense\":\"x\"}");

            Assert.That(answer.Status, Is.EqualTo(404), "not 400");
        }

        [Test]
        public void A_group_is_made_renamed_and_removed()
        {
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var made = Send("POST", "/groups/root/groups", "{\"Name\":\"Invented\"}");
            Assert.That(made.Status, Is.EqualTo(201));

            var uuid = JsonBody.Read(made.Json).Text("uuid");

            Assert.That(Send("PUT", "/groups/" + uuid, "{\"Name\":\"Renamed\"}").Status, Is.EqualTo(200));
            Assert.That(Get("/groups/" + uuid).Json, Does.Contain("\"name\":\"Renamed\""));
            Assert.That(Send("DELETE", "/groups/" + uuid, null).Status, Is.EqualTo(200));
        }

        [Test]
        public void Removing_the_root_group_is_a_404_and_never_a_conflict()
        {
            // With everything granted on the root itself, so what answers is the shape of the
            // question rather than a rule. A removal is asked of the group a node is leaving and
            // the root is leaving nothing, so `AccessCheck` refuses before a handler could call
            // it a conflict, and the caller cannot tell the root from a group it may not have.
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var answer = Send("DELETE", "/groups/root", null);

            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
            Assert.That(answer.Json, Does.Not.Contain("conflict"));
        }

        [Test]
        public void A_group_that_still_holds_something_is_not_removed()
        {
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var answer = Send("DELETE", "/groups/" + _group, null);

            Assert.That(answer.Status, Is.EqualTo(409));
        }

        [Test]
        public void A_dialog_open_on_what_is_being_written_answers_that_it_is_busy()
        {
            _vault.Granting(_group, AllButProtected);
            _vault.Busy = "an entry dialog is open on the entry this would be written to";

            var answer = Send("PUT", "/entries/" + _entry, "{\"Title\":\"x\"}");

            Assert.That(answer.Status, Is.EqualTo(503));
            Assert.That(answer.Json, Does.Contain("busy"), "worth trying again, unlike a refusal");
        }

        [Test]
        public void A_stranger_is_told_the_same_thing_on_every_route_there_is()
        {
            // Every operation that needs a certificate, at the method it declares, and beside them
            // a path nothing declares and a declared path at a method nothing declares: one status
            // and one body for all of them, so a stranger learns nothing about which exist.
            var requests = SpecDocument.Embedded().Operations
                .Where(o => o.NeedsIdentity)
                .Select(o => new[] { o.Method, o.Path.Text.Replace("{uuid}", _entry).Replace("{name}", "Title") })
                .Concat(new[]
                {
                    new[] { "GET", "/nothing/here" },
                    new[] { "PATCH", "/entries/" + _entry },
                })
                .ToList();

            Assert.That(requests.Count, Is.GreaterThan(2), "the document declares no operation a stranger is refused");

            foreach (var request in requests)
            {
                var context = new GateContext(
                    Head(request[0], request[1]), string.Empty, ClientIdentity.Stranger("d1"));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(401), request[0] + " " + request[1]);
                Assert.That(context.Document(), Is.EqualTo("{\"error\":\"unpaired\"}"), request[0] + " " + request[1]);
            }
        }

        [Test]
        public void Every_decision_about_a_route_that_touches_the_database_is_recorded()
        {
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));

            _vault.Granting(_group, AllButProtected);

            var context = Context("GET", "/entries/" + _entry, null);
            router.Handle(context);

            Assert.That(audit.Entries.Count, Is.EqualTo(1));
            Assert.That(audit.Entries[0].OperationId, Is.EqualTo("readEntry"));
            Assert.That(audit.Entries[0].Permitted, Is.True);
            Assert.That(audit.Entries[0].Node, Is.Not.Null);
        }

        [Test]
        public void A_listing_is_recorded_once_and_not_once_per_item()
        {
            // An audit line per entry a client cannot see would be a line for the normal case, and
            // would bury the decisions worth reading.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));

            _vault.Granting(_vault.RootAlias, AllButProtected);
            _vault.AddEntry(_group, "another invented service");

            var context = Context("GET", "/entries", null);
            router.Handle(context);

            Assert.That(audit.Entries.Count, Is.EqualTo(1));
            Assert.That(audit.Entries[0].OperationId, Is.EqualTo("listEntries"));
        }

        [Test]
        public void A_protected_value_withheld_is_written_down_as_the_refusal_it_is()
        {
            // The test above says an audit line saying an update happened does not tell the two
            // apart. It did not, because nothing wrote the second line: the router recorded that
            // `update` was granted, before dispatch, and the handler's refusal was recorded
            // nowhere. The file therefore said this client had changed a password the gate had in
            // fact refused it, and the attempt the switch exists to stop left no trace at all.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));

            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "CRUL")));

            var context = Context(
                "PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"chosen-by-the-caller\"}");
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404));
            Assert.That(audit.Entries.Count, Is.EqualTo(2),
                "the verb was granted and the scope was not, which is two decisions");
            Assert.That(audit.Entries[0].Permitted, Is.True, "the route was allowed");
            Assert.That(audit.Entries[1].Permitted, Is.False, "and this is the line worth having");
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));
        }

        [Test]
        public void A_protected_value_a_read_withholds_is_written_down_as_the_decision_that_withheld_it()
        {
            // A read is served from the caller's view, and the line for a value the view
            // withholds is the walk the view was decided by, written once and never asked for
            // again. Where the view serves every value asked for, nothing further is written:
            // a protected title shown through the listing's reach is no refusal.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));

            _profiles = new[] { "reader", "lister" };
            _vault.Protect(_entry, "Title");
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("reader", "R"), ("lister", "LP")));
            var entry = "/entries/" + _entry;

            var whole = Context("GET", entry, null);
            router.Handle(whole);

            Assert.That(whole.Status, Is.EqualTo(200), whole.Document());
            Assert.That(
                whole.Document(),
                Does.Contain("\"Title\":\"an invented service\"").And.Contain("\"Password\":null"));
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "the read was granted and a value in it was not");
            Assert.That(audit.Entries[0].Permitted, Is.True);
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].OperationId, Is.EqualTo("readEntry"));
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));

            var secret = Context("GET", entry + "/fields/Password", null);
            router.Handle(secret);

            Assert.That(secret.Status, Is.EqualTo(200), secret.Document());
            Assert.That(audit.Entries.Count, Is.EqualTo(4));
            Assert.That(audit.Entries[3].Permitted, Is.False);
            Assert.That(audit.Entries[3].OperationId, Is.EqualTo("readField"));
            Assert.That(audit.Entries[3].Reason, Is.EqualTo(NoProtectedReach));

            var title = Context("GET", entry + "/fields/Title", null);
            router.Handle(title);

            Assert.That(title.Status, Is.EqualTo(200), title.Document());
            Assert.That(title.Document(), Does.Contain("\"value\":\"an invented service\""));
            Assert.That(audit.Entries.Count, Is.EqualTo(5), "the view served the title, so nothing was withheld to write down");
            Assert.That(audit.Entries[4].Permitted, Is.True);
        }

        [TestCase("", "readEntry", "\"Password\":null")]
        [TestCase("/fields/Password", "readField", "\"value\":null")]
        public void The_line_for_a_withheld_value_is_written_before_any_of_the_answer_is(
            string under, string operation, string withheld)
        {
            // The answer to a read of an entry or of a field is written as it is sent, and
            // nothing can be written down once its content is going out. So the line for a
            // value the view withholds is on record when the handler hands the answer over,
            // with nothing of the answer written, and writing the answer records nothing more.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var context = Context("GET", "/entries/" + _entry + under, null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(context.Content, Is.Not.Null, "the answer is handed over as what writes it");
            Assert.That(context.Json, Is.Null, "and not as a document made already");
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "the read was granted and a value in it was not");
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].OperationId, Is.EqualTo(operation));
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));

            Assert.That(context.Document(), Does.Contain(withheld));
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "writing the answer records nothing further");
        }

        [Test]
        public void Every_write_refused_a_protected_value_writes_the_refusal_down(
            [Values("createEntry", "updateEntry", "createField", "stripField")] string operation)
        {
            // The test above, for the other writes that ask the switch. Each is refused by the
            // handler after the router wrote that the verb was granted, so each needs the second
            // line or the file shows a write the gate turned away as a write that was allowed.
            // `stripField` asks `create` of the same rule, since taking a protected field off is
            // only refused where the caller could put an unprotected value back in its place.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _vault.Granting(_group, Rules.Granting(("agent", "CRUSL")));

            var context = Withholding(operation);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), context.Document());
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "the route was granted and the value was not");
            Assert.That(audit.Entries[0].Permitted, Is.True);
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].OperationId, Is.EqualTo(operation));
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));
        }

        [TestCase("CRSL", "CRLP", TestName = "A strip kept from a profile beside a profile reaching protected values is written down")]
        [TestCase("RSL", "CRL", TestName = "A strip kept from the profile that could put a value back is written down")]
        public void A_strip_the_guard_keeps_from_a_profile_writes_its_refusal_down(string tidier, string maker)
        {
            // The guard asks each profile on its own, under R10, and its refusal was written down
            // by a walk pooling every profile. Where some profile could create a protected value,
            // as `maker` can in the first case, that walk permitted and wrote nothing, so the
            // attempt the guard exists to stop left no line.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _profiles = new[] { "tidier", "maker" };
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("tidier", tidier), ("maker", maker)));

            var context = Context("DELETE", "/entries/" + _entry + "/fields/PIN", null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), context.Document());
            Assert.That(_vault.Field(_entry, "PIN"), Is.EqualTo("not-a-real-secret"));
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "the strip was granted and the guard refused it");
            Assert.That(audit.Entries[0].Permitted, Is.True);
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].OperationId, Is.EqualTo("stripField"));
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));
        }

        [Test]
        public void A_read_that_withholds_protected_values_writes_one_refusal_however_many_it_blanks()
        {
            // The read is answered, with the protected values as `null`, so the router's line is
            // true and is all the router knows. The refusal inside the answer is written by the
            // handler, and written once for the entry: blanking two values is a single decision,
            // and a line per field would grow the file by whatever the entry holds.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.CreateField(_entry, "PIN", "not-a-real-pin", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var context = Context("GET", "/entries/" + _entry, null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(context.Document(), Does.Contain("\"Password\":null"));
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "the read was granted and two values were withheld in it");
            Assert.That(audit.Entries[0].Permitted, Is.True);
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].OperationId, Is.EqualTo("readEntry"));
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));
        }

        [Test]
        public void A_field_read_writes_a_refusal_only_when_it_withholds_the_value()
        {
            // Reading a field nothing protects withholds nothing, so the router's line is the
            // whole account of it; a line for every read a client makes without the switch is the
            // noise `A_listing_is_recorded_once_and_not_once_per_item` keeps out.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var title = Context("GET", "/entries/" + _entry + "/fields/Title", null);
            router.Handle(title);

            Assert.That(title.Status, Is.EqualTo(200), title.Document());
            Assert.That(audit.Entries.Count, Is.EqualTo(1), "nothing was withheld from it");

            var password = Context("GET", "/entries/" + _entry + "/fields/Password", null);
            router.Handle(password);

            Assert.That(password.Status, Is.EqualTo(200), password.Document());
            Assert.That(password.Document(), Does.Contain("\"value\":null"));
            Assert.That(audit.Entries.Count, Is.EqualTo(3), "and this read withheld its value");
            Assert.That(audit.Entries[1].Permitted, Is.True);
            Assert.That(audit.Entries[2].Permitted, Is.False);
            Assert.That(audit.Entries[2].OperationId, Is.EqualTo("readField"));
            Assert.That(audit.Entries[2].Reason, Is.EqualTo(NoProtectedReach));
        }

        [Test]
        public void A_value_a_removed_profile_would_have_reached_is_written_down_as_removed()
        {
            // The record holds `keeper`, whose rule here reaches protected values, and the list no
            // longer names it, so the client reads holding `agent` alone and the value is withheld.
            // Written as the rule refusing, the line sends the operator to a rule that is not the
            // reason; the handler's refusal goes through the router's check so that it is written
            // the way the router's own refusals are.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _removed = new[] { "keeper" };
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL"), ("keeper", "RLP")));

            var context = Context("GET", "/entries/" + _entry, null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(context.Document(), Does.Contain("\"Password\":null"));
            Assert.That(audit.Entries.Count, Is.EqualTo(2));
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(AccessCheck.ProfileRemoved));
            Assert.That(audit.Entries[1].RemovedProfiles, Is.EqualTo(new[] { "keeper" }));
        }

        [TestCase("entry")]
        [TestCase("field")]
        public void A_title_a_removed_profile_would_have_shown_through_the_listing_s_reach_is_written_down_as_removed(
            string route)
        {
            // A protected title is served on `list` with `protected` beside `read` as well as on
            // `read` with `protected`. The record holds `lister`, whose rule carries `list` with
            // `protected`, and the list no longer names it, so the title is withheld for that
            // reason and the line names the profile. Asked of `read` with `protected` alone, as
            // every other protected value is, putting `lister` back would not have permitted
            // it, and the line sent the operator to a rule that is not the reason.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _removed = new[] { "lister" };
            _vault.Protect(_entry, "Title");
            _vault.Granting(_group, Rules.Granting(("agent", "R"), ("lister", "LP")));

            var path = "/entries/" + _entry + (route == "field" ? "/fields/Title" : string.Empty);
            var context = Context("GET", path, null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(context.Document(), Does.Contain(route == "field" ? "\"value\":null" : "\"Title\":null"));
            Assert.That(audit.Entries.Count, Is.EqualTo(2), "still one line for the value withheld");
            Assert.That(audit.Entries[1].Permitted, Is.False);
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(AccessCheck.ProfileRemoved));
            Assert.That(audit.Entries[1].RemovedProfiles, Is.EqualTo(new[] { "lister" }));
        }

        [Test]
        public void A_password_withheld_beside_a_title_is_written_down_as_the_rules_refusing_whatever_the_listing_s_reach()
        {
            // The listing's reach serves a title and a URL and nothing else, so where a password
            // is withheld as well, putting `lister` back would not have served everything that
            // was withheld, and the line stays the refusal of `read` with `protected`.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _removed = new[] { "lister" };
            _vault.Protect(_entry, "Title");
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "R"), ("lister", "LP")));

            var context = Context("GET", "/entries/" + _entry, null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200), context.Document());
            Assert.That(audit.Entries.Count, Is.EqualTo(2));
            Assert.That(audit.Entries[1].Reason, Is.EqualTo(NoProtectedReach));
            Assert.That(audit.Entries[1].RemovedProfiles, Is.Null.Or.Empty);
        }

        [Test]
        public void The_field_route_refuses_what_the_entry_route_refuses()
        {
            // The two halves of "protected" disagree whenever an operator turns a protection on,
            // because KeePass does not go back and re-flag the strings already there. While this
            // route asked only the string's own flag, the credential replacement the test above
            // blocks was reachable one path over, on the same entry, with the same rule.
            _vault.CreateField(_entry, "Password", "not-a-real-password", false, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "CRUL")));

            Assert.That(Send("PUT", "/entries/" + _entry, "{\"Password\":\"chosen-by-the-caller\"}").Status,
                Is.EqualTo(404), "the entry route has always refused this");
            Assert.That(
                Send("PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"chosen-by-the-caller\"}").Status,
                Is.EqualTo(404), "and the field route has to agree");
        }

        [Test]
        public void A_field_the_database_protects_is_stored_protected_however_it_was_asked_for()
        {
            // The check asked `guarded || Protects`; the call passed `guarded`. So a standard field
            // an entry lacks, which an entry another program wrote can, was put on it unprotected,
            // and every later client with `read` and no `protected` was handed it in clear. Both
            // entries here hold only their titles, since taking a standard field off to put it
            // back is refused.
            _vault.Granting(_group, Rules.Granting(("agent", "CRSLP")));
            var other = _vault.AddEntry(_group, "another invented service");

            Assert.That(
                Send("POST", "/entries/" + _entry + "/fields",
                     "{\"name\":\"Password\",\"value\":\"not-a-real-password\"}").Status,
                Is.EqualTo(201));

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"protected\":[\"Password\"]"),
                "the database says this name is protected, whatever the request said");
            Assert.That(_vault.Flagged(_entry, "Password"), Is.True, "and it is stored with the flag set");

            // And saying `false` out loud is the same request: it cannot take away a protection
            // the database gives the name, so it is stored protected too and still answered 201.
            Assert.That(
                Send("POST", "/entries/" + other + "/fields",
                     "{\"name\":\"Password\",\"value\":\"not-a-real-password\",\"protected\":\"false\"}").Status,
                Is.EqualTo(201));

            Assert.That(Get("/entries/" + other).Json, Does.Contain("\"protected\":[\"Password\"]"),
                "`false` cannot unprotect a name the database protects");
            Assert.That(_vault.Flagged(other, "Password"), Is.True, "nor store it without the flag");
        }

        [Test]
        public void A_standard_field_can_be_emptied_and_not_taken_off()
        {
            // The real vault's rule, which `KdbxVaultTests` holds it to, so the handlers here meet
            // it too: a request sequence the gate refuses is not a sequence a test can lean on.
            _vault.Granting(_group, Rules.Granting(("agent", "CRUSL")));
            var made = Send("POST", "/groups/" + _group + "/entries", Bodies.Of(("Title", "an invented entry")));
            Assert.That(made.Status, Is.EqualTo(VaultHandlers.Created), made.Json);
            var entry = "/entries/" + JsonBody.Read(made.Json).Text("uuid");

            var stripped = Send("DELETE", entry + "/fields/URL", null);

            Assert.That(stripped.Status, Is.EqualTo(409), stripped.Json);
            Assert.That(Send("PUT", entry + "/fields/URL", Bodies.Of(("value", string.Empty))).Status, Is.EqualTo(200),
                "emptied, which is what taking a standard field off means");
            Assert.That(Get(entry + "/fields").Json, Does.Contain("\"name\":\"URL\""), "and still there");
        }

        [Test]
        public void An_entry_that_goes_away_mid_request_withholds_rather_than_tells()
        {
            // The value is read, and the entry is gone by the time the rules are asked about it.
            // The lookup used to sit inside the conjunction that decides withholding, so a missing
            // entry made the answer "not withheld" and the real value went out in a 200.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));
            _vault.Vanishes = _entry;

            var answer = Get("/entries/" + _entry + "/fields/Password");

            Assert.That(answer.Json, Does.Not.Contain("not-a-real-password"),
                "a value must never be sent because the entry it belongs to could not be found");
        }

        [TestCase("POST", "/groups/{0}/entries", "{\"Password\":\"not-a-real-password\"}",
                  TestName = "a new entry's password, in a group its handler could not find")]
        [TestCase("PUT", "/entries/{1}", "{\"Password\":\"not-a-real-password\"}",
                  TestName = "an entry's password, on an entry its handler could not find")]
        [TestCase("POST", "/entries/{1}/fields",
                  "{\"name\":\"Secret\",\"value\":\"not-a-real-password\",\"protected\":\"true\"}",
                  TestName = "a new protected field, on an entry its handler could not find")]
        [TestCase("PUT", "/entries/{1}/fields/Password", "{\"value\":\"not-a-real-password\"}",
                  TestName = "a protected field written, on an entry its handler could not find")]
        [TestCase("DELETE", "/entries/{1}/fields/Password", null,
                  TestName = "a protected field stripped, from an entry its handler could not find")]
        public void A_write_whose_node_is_gone_by_its_turn_writes_nothing(string method, string template, string body)
        {
            // The router finds the node and the write, in its own turn, does not: an operator's
            // removal landing between the two. The question about protected values is asked of
            // the node the write finds, so there is no node to ask it of and nothing to write to.
            // Asked by a handler's own lookup instead, a miss there once skipped the question, and
            // a write that found the node again wrote a protected value for a rule without
            // `protected`, and stripped a protected field for a rule that could put an
            // unprotected value back.
            _vault.CreateField(_entry, "Password", "the stored password", true, VaultAskings.Free);
            _vault.Granting(_vault.RootAlias, AllButProtected);
            var before = Written();
            var lost = template.StartsWith("/groups/", StringComparison.Ordinal) ? _group : _entry;
            _vault.After(1, () => _vault.Vanishes = lost);

            var answer = Send(method, string.Format(CultureInfo.InvariantCulture, template, _group, _entry), body);
            _vault.Vanishes = null;

            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
            Assert.That(answer.Json, Does.Contain(SpecRouter.UnreachableError));
            Assert.That(Written(), Is.EqualTo(before), "nothing is written that the rules were not asked about");
        }

        [TestCase("PUT", "/entries/{0}", "{\"UserName\":\"not-a-real-name\"}", "UserName",
                  TestName = "an entry's user name, marked protected after the router decided")]
        [TestCase("PUT", "/entries/{0}/fields/PIN", "{\"value\":\"not-a-real-pin\"}", "PIN",
                  TestName = "a field written, marked protected after the router decided")]
        [TestCase("DELETE", "/entries/{0}/fields/PIN", null, "PIN",
                  TestName = "a field stripped, marked protected after the router decided")]
        public void A_string_marked_protected_before_the_write_s_turn_is_not_written_over(
            string method, string template, string body, string name)
        {
            // Which of an entry's strings carry their own protection is asked inside the write,
            // of the entry as the write finds it. Asked by the handler beforehand, the answer
            // was "nothing is protected" for a string the operator marked a moment later, and the
            // write set or took away a string protected by its own flag for a rule without
            // `protected`. The database here protects `Password` alone, so the flag is the only
            // thing standing in the way.
            _vault.Plant(_entry, name, "the stored value");
            _vault.Granting(_vault.RootAlias, AllButProtected);
            _vault.After(1, () => _vault.Protect(_entry, name));

            var marked = name + "=the stored value, protected";
            var answer = Send(method, string.Format(CultureInfo.InvariantCulture, template, _entry), body);

            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
            Assert.That(answer.Json, Does.Contain(SpecRouter.UnreachableError));
            Assert.That(Written(), Does.Contain(marked), "nothing is written that the rules were not asked about");
        }

        [Test]
        public void A_value_the_database_would_drop_is_refused_rather_than_reported_as_stored()
        {
            // KeePass writes a string into a `.kdbx` through `StrUtil.SafeXmlString`, which removes
            // what XML 1.0 cannot represent. So this was accepted, answered `201`, and came back
            // from the file with the character gone: a client that meant to write one thing was
            // told it had, and had written another.
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var answer = Send("POST", "/groups/" + _group + "/entries",
                              "{\"Notes\":\"first\\u0007second\"}");

            Assert.That(answer.Status, Is.EqualTo(400), answer.Json);
        }

        [Test]
        public void Notes_may_still_be_written_the_way_people_write_paragraphs()
        {
            // The refusal is about characters a database cannot hold, not about whitespace. An
            // entry's notes are a field like any other and people put line breaks in them.
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var answer = Send("POST", "/groups/" + _group + "/entries",
                              "{\"Notes\":\"first\\r\\nsecond\\tthird\"}");

            Assert.That(answer.Status, Is.EqualTo(VaultHandlers.Created), answer.Json);
        }

        [TestCase("api\\rkey", "api\rkey", TestName = "a carriage return, which the file reads back as a line feed")]
        [TestCase("api\\nkey", "api\nkey", TestName = "a line feed")]
        [TestCase("api\\tkey", "api\tkey", TestName = "a tab")]
        [TestCase("api\\u007Fkey", "api\u007Fkey", TestName = "delete")]
        [TestCase("api\\u0085key", "api\u0085key", TestName = "a C1 control")]
        [TestCase("a/../b", "a/../b", TestName = "a climb")]
        public void A_name_no_route_could_address_again_is_not_made(string spelled, string name)
        {
            // A name is not a paragraph. One the routes refuse was made with `201` and could
            // never be read, changed or stripped again, and a carriage return was worse: two
            // names distinct in memory became one when the file was reopened, so a client with
            // only `create` could write over a protected field whose name held a line feed.
            _vault.Granting(_group, AllButProtected);
            var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });

            var field = Send("POST", "/entries/" + _entry + "/fields",
                             "{\"name\":\"" + spelled + "\",\"value\":\"invented\"}");
            var attachment = Send("POST", "/entries/" + _entry + "/attachments",
                                  "{\"name\":\"" + spelled + "\",\"content\":\"" + content + "\"}");

            Assert.That(field.Status, Is.EqualTo(400), field.Json);
            Assert.That(field.Json, Does.Contain("malformed"));
            Assert.That(attachment.Status, Is.EqualTo(400), attachment.Json);
            Assert.That(attachment.Json, Does.Contain("malformed"));
            Assert.That(_vault.Field(_entry, name), Is.Null);
            Assert.That(_vault.Attachment(_entry, name), Is.Null);
        }

        [TestCase("report\\u202Efdp.exe", "report\u202Efdp.exe", TestName = "the right to left override")]
        [TestCase("report\\u2067fdp.exe", "report\u2067fdp.exe", TestName = "a right to left isolate")]
        [TestCase("report\\u200Ffdp.exe", "report\u200Ffdp.exe", TestName = "a right to left mark")]
        [TestCase("report\\u061Cfdp.exe", "report\u061Cfdp.exe", TestName = "the Arabic letter mark")]
        public void A_name_that_would_be_shown_in_another_order_is_not_made(string spelled, string name)
        {
            // KeePass lists an attachment under its name and opens one it cannot show itself with
            // whatever Windows starts for the real suffix, so this one was shown as a document and
            // started as a program on the operator's double click.
            _vault.Granting(_group, AllButProtected);
            var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });

            var field = Send("POST", "/entries/" + _entry + "/fields",
                             "{\"name\":\"" + spelled + "\",\"value\":\"invented\"}");
            var attachment = Send("POST", "/entries/" + _entry + "/attachments",
                                  "{\"name\":\"" + spelled + "\",\"content\":\"" + content + "\"}");

            Assert.That(field.Status, Is.EqualTo(400), field.Json);
            Assert.That(attachment.Status, Is.EqualTo(400), attachment.Json);
            Assert.That(attachment.Json, Does.Contain("malformed"));
            Assert.That(_vault.Field(_entry, name), Is.Null);
            Assert.That(_vault.Attachment(_entry, name), Is.Null);
        }

        [TestCase("Pass\\u200Bword", "Pass\u200Bword", TestName = "a zero-width space")]
        [TestCase("Pass\\u200Cword", "Pass\u200Cword", TestName = "a zero-width non-joiner")]
        [TestCase("Pass\\u200Dword", "Pass\u200Dword", TestName = "a zero-width joiner")]
        [TestCase("Pass\\u2060word", "Pass\u2060word", TestName = "a word joiner")]
        [TestCase("Pass\\u00ADword", "Pass\u00ADword", TestName = "a soft hyphen")]
        [TestCase("Pass\\uFEFFword", "Pass\uFEFFword", TestName = "a byte order mark")]
        [TestCase("Pass\\u180Eword", "Pass\u180Eword", TestName = "the Mongolian vowel separator")]
        [TestCase("Pass\\uDB40\\uDC41word", "Pass\uDB40\uDC41word", TestName = "a tag character beyond the first plane")]
        public void A_name_holding_a_character_drawn_as_nothing_is_not_made(string spelled, string name)
        {
            // Each of these is drawn as nothing at all, so the name is drawn as `Password`: a
            // client holding `create` and not `protected` put a second, unprotected `Password`
            // beside the real `Password`, holding what it chose, for the operator to type into or
            // copy from. The database tells names apart by the culture, which weighs some of these
            // and not others, so the zero-width space was a second name to it and the same name to
            // the eye.
            _vault.Granting(_group, AllButProtected);
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            var fields = _vault.Whole(_entry).Fields.Count;

            var field = Send("POST", "/entries/" + _entry + "/fields",
                             "{\"name\":\"" + spelled + "\",\"value\":\"chosen-by-the-caller\"}");
            var attachment = Send("POST", "/entries/" + _entry + "/attachments",
                                  "{\"name\":\"" + spelled + "\",\"content\":\"" + content + "\"}");

            Assert.That(field.Status, Is.EqualTo(400), field.Json);
            Assert.That(field.Json, Does.Contain("malformed"));
            Assert.That(attachment.Status, Is.EqualTo(400), attachment.Json);
            Assert.That(_vault.Whole(_entry).Fields.Count, Is.EqualTo(fields), "nothing was made");
            Assert.That(_vault.Attachment(_entry, name), Is.Null);
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("not-a-real-password"));
        }

        [TestCase("fields", "\u4E2D", TestName = "A field name of Chinese characters is made only while its routes can name it")]
        [TestCase("attachments", "\u4E2D", TestName = "An attachment name of Chinese characters is made only while its routes can name it")]
        [TestCase("fields", "\u0436", TestName = "A field name of Cyrillic letters is made only while its routes can name it")]
        [TestCase("attachments", "\uD83D\uDE00", TestName = "An attachment name beyond the first plane is made only while its routes can name it")]
        public void A_name_is_made_only_while_the_route_naming_it_fits_the_target_ceiling(string kind, string letter)
        {
            // Percent-encoded, a character beyond ASCII takes six to twelve characters of the
            // path, and the transport refuses a target over its ceiling before any route is
            // chosen. So a name inside the 512 the body allows was made with `201`, and every
            // route naming it was answered `414`.
            _vault.Granting(_group, AllButProtected);
            var under = "/entries/" + _entry + "/" + kind + "/";
            var encoded = Uri.EscapeDataString(letter).Length;
            var fits = (HttpRequestReader.MaxTargetCharacters - under.Length) / encoded;
            var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });

            foreach (var count in new[] { fits, fits + 1 })
            {
                var name = string.Concat(Enumerable.Repeat(letter, count));
                var body = kind == "fields"
                    ? Bodies.Of(("name", name), ("value", "invented"))
                    : Bodies.Of(("name", name), ("content", content));
                var route = under + Uri.EscapeDataString(name);
                Assume.That(route.Length <= HttpRequestReader.MaxTargetCharacters, Is.EqualTo(count == fits), route.Length.ToString(CultureInfo.InvariantCulture));

                var made = Send("POST", "/entries/" + _entry + "/" + kind, body);

                if (count == fits)
                {
                    Assert.That(made.Status, Is.EqualTo(VaultHandlers.Created), made.Json);
                    Assert.That(Get(route).Status, Is.EqualTo(200), "and its own route names it");
                }
                else
                {
                    Assert.That(made.Status, Is.EqualTo(400), made.Json);
                    Assert.That(made.Json, Does.Contain("malformed"));
                }
            }
        }

        [Test]
        public void The_routes_a_name_is_measured_against_are_the_routes_the_document_declares()
        {
            // The ceiling is asked of what comes before the name in the route naming it, so the
            // handler's count has to be the document's.
            var named = SpecDocument.Embedded().Operations
                .Select(operation => operation.Path.Text)
                .Where(path => path.EndsWith("/{name}", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            Assert.That(named, Is.EquivalentTo(new[] { "/entries/{uuid}/fields/{name}", "/entries/{uuid}/attachments/{name}" }));
            Assert.That(VaultHandlers.FieldRouteCharacters, Is.EqualTo("/entries/{uuid}/fields/".Replace("{uuid}", new string('0', 32)).Length));
            Assert.That(VaultHandlers.AttachmentRouteCharacters, Is.EqualTo("/entries/{uuid}/attachments/".Replace("{uuid}", new string('0', 32)).Length));
        }

        [Test]
        public void Right_to_left_text_is_still_a_value_like_any_other()
        {
            // The refusal is about names, which KeePass acts on. A value holding right to left
            // text, the marks included, is somebody's note.
            _vault.Granting(_group, AllButProtected);

            var answer = Send("POST", "/entries/" + _entry + "/fields",
                              "{\"name\":\"invented\",\"value\":\"\\u202Bnote\\u202C\"}");

            Assert.That(answer.Status, Is.EqualTo(VaultHandlers.Created), answer.Json);
        }

        [Test]
        public void A_write_this_gate_could_not_attempt_says_so_rather_than_naming_the_route()
        {
            // The database closing under a request, or KeePass answering the attempt with an
            // error. That is the gate failing, and it used to be reported as `404 no such route`,
            // which is what an absent node and a forbidden one are told: an operator went looking
            // at rules and identifiers over a database that had simply gone.
            _vault.Granting(_vault.RootAlias, AllButProtected);
            _vault.WriteBlocked = "the database is not open";

            var answer = Send("PUT", "/groups/" + _group, "{\"Name\":\"Renamed\"}");

            Assert.That(answer.Status, Is.EqualTo(500), answer.Json);
            Assert.That(answer.Json, Does.Contain("internal error"));
        }

        [Test]
        public void A_node_the_rules_do_not_reach_still_answers_as_though_it_were_not_there()
        {
            // The other half of the one above, asserted beside it: telling a failure apart from a
            // refusal must not have told the two refusals apart from each other.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "RL")));

            var refused = Send("PUT", "/groups/" + _group, "{\"Name\":\"Renamed\"}");
            var absent = Send("PUT", "/groups/" + new string('a', 32), "{\"Name\":\"Renamed\"}");

            Assert.That(refused.Status, Is.EqualTo(404), refused.Json);
            Assert.That(absent.Status, Is.EqualTo(404), absent.Json);
            Assert.That(refused.Json, Is.EqualTo(absent.Json));
        }

        [Test]
        public void An_undeclared_path_answers_an_admitted_client_as_a_refused_node_does()
        {
            // The router answers the two from two places, the authorization refusal and
            // `Undeclared`, and the property is that an admitted client cannot tell them apart.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "RL")));

            var refused = Send("PUT", "/groups/" + _group, "{\"Name\":\"Renamed\"}");
            var undeclared = Get("/groups/" + _group + "/nothing-is-declared-here");

            Assert.That(refused.Status, Is.EqualTo(404), refused.Json);
            Assert.That(undeclared.Status, Is.EqualTo(refused.Status), undeclared.Json);
            Assert.That(undeclared.Json, Is.EqualTo(refused.Json));
        }

        [TestCase("POST", "/groups/{0}/groups", "{\"Name\":\"Invented\"}", TestName = "a group made in a database locked after the router decided")]
        [TestCase("PUT", "/groups/{0}", "{\"Name\":\"Renamed\"}", TestName = "a group renamed in a database locked after the router decided")]
        [TestCase("DELETE", "/groups/{0}", null, TestName = "a group removed in a database locked after the router decided")]
        [TestCase("POST", "/groups/{0}/entries", "{\"Title\":\"invented\"}", TestName = "an entry made in a database locked after the router decided")]
        [TestCase("PUT", "/entries/{1}", "{\"Title\":\"renamed\"}", TestName = "an entry changed in a database locked after the router decided")]
        [TestCase("DELETE", "/entries/{1}", null, TestName = "an entry removed in a database locked after the router decided")]
        [TestCase("POST", "/entries/{1}/fields", "{\"name\":\"Invented\",\"value\":\"x\"}", TestName = "a field made in a database locked after the router decided")]
        [TestCase("PUT", "/entries/{1}/fields/Title", "{\"value\":\"renamed\"}", TestName = "a field changed in a database locked after the router decided")]
        [TestCase("DELETE", "/entries/{1}/fields/Title", null, TestName = "a field stripped in a database locked after the router decided")]
        [TestCase("DELETE", "/entries/{1}/attachments/note.txt", null, TestName = "an attachment stripped in a database locked after the router decided")]
        public void A_write_meeting_a_database_locked_after_the_router_decided_is_told_it_is_locked(
            string method, string template, string body)
        {
            // A change is handed to the writer with the questions about it, and the writer finds
            // nothing open to change. The node has not gone and nothing was attempted, so the
            // client is told what a read meeting the same lock is told, and a retry once the
            // database is open again duplicates nothing. That answer was `500 internal error`
            // while some handlers looked their node up first and met the lock there: one event
            // answered two ways, by which route it landed in.
            _vault.Granting(_vault.RootAlias, AllButProtected);
            Assume.That(_vault.CreateAttachment(_entry, "note.txt", new byte[] { 1, 2, 3 }, VaultAskings.Free).Ok, Is.True);
            _vault.After(1, _vault.Lock);

            var answer = Send(method, string.Format(CultureInfo.InvariantCulture, template, _group, _entry), body);

            Assert.That(_vault.IsOpen, Is.False, "the database locked, which is what this is about");
            Assert.That(answer.Status, Is.EqualTo(503), answer.Json);
            Assert.That(answer.Json, Is.EqualTo("{\"error\":\"locked\"}"));
        }

        [Test]
        public void A_database_locked_before_the_router_looks_is_told_so_and_recorded_nowhere()
        {
            // A lock landing while the request was still being read. There was nothing to decide
            // about, so nothing was decided: the line a missing node gets would say the rules
            // turned away what a lock turned away, and a request meeting the lock before routing
            // writes no line either.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _vault.Granting(_vault.RootAlias, AllButProtected);
            _vault.Lock();

            var read = Context("GET", "/entries/" + _entry, null);
            var write = Context("PUT", "/groups/" + _group, "{\"Name\":\"Renamed\"}");
            router.Handle(read);
            router.Handle(write);

            Assert.That(read.Status, Is.EqualTo(503), read.Document());
            Assert.That(read.Document(), Is.EqualTo("{\"error\":\"locked\"}"));
            Assert.That(write.Status, Is.EqualTo(503), "a write meets it here as a read does, before anything was attempted");
            Assert.That(audit.Entries, Is.Empty);
        }

        [Test]
        public void A_node_absent_while_the_database_is_open_is_still_a_refusal_on_record()
        {
            // The other branch of the same lookup, and the one that stays as it was: a node that is
            // not there while the database is open is decided about and refused, which the audit is
            // where the operator learns it was absent rather than forbidden.
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));
            _vault.Granting(_vault.RootAlias, AllButProtected);

            var context = Context("GET", "/entries/" + new string('a', 32), null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), context.Document());
            Assert.That(audit.Entries.Count, Is.EqualTo(1));
            Assert.That(audit.Entries[0].Permitted, Is.False);
            Assert.That(audit.Entries[0].Node, Is.Null);
        }

        [Test]
        public void Reading_one_field_reads_the_values_in_its_caller_s_sight_and_no_value_out_of_it()
        {
            // The view of an entry reads a value when that value is asked for, which is as the
            // answer is written: the string asked about, and no other. A field read is answered
            // with the entry's tag, which every caller reaching the route is handed, and the
            // tag asks for the digest of every value in that caller's sight: the string asked
            // about and the other open strings. A value the caller's rule withholds is never
            // read or digested, for the answer or for the tag.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultAskings.Free);
            _vault.CreateField(_entry, "PIN", "not-a-real-pin", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));
            _vault.StringReads = 0;
            _vault.StringDigests = 0;

            var answer = Get("/entries/" + _entry + "/fields/Title");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Tag, Is.Not.Null);
            Assert.That(_vault.StringReads, Is.EqualTo(1), "`Title`, which is the value the answer writes");
            Assert.That(_vault.StringDigests, Is.EqualTo(2),
                "`Title` and `Account` are in this caller's sight, and `Password` and `PIN` are not");

            // And a field read refused before the tag reads and digests nothing at all.
            _vault.StringReads = 0;
            _vault.StringDigests = 0;
            Assert.That(Get("/entries/" + _entry + "/fields/Absent").Status, Is.EqualTo(404));
            Assert.That(_vault.StringReads, Is.Zero);
            Assert.That(_vault.StringDigests, Is.Zero);
        }

        [Test]
        public void A_URL_is_stored_as_written_whatever_KeePass_would_make_of_it()
        {
            // Every route that writes a URL stores it as it arrives. What KeePass's Open URL makes
            // of a reference, a variable, a path or a command is the operator's to weigh, and
            // docs/access-control.md is where the operator learns what that is.
            _vault.Granting(_group, AllButProtected);
            var planted = "https://invented.example/?p={REF:P@I:0123456789ABCDEF}&t=%TEMP%";

            Assert.That(Send("POST", "/entries/" + _entry + "/fields",
                Bodies.Of(("name", "URL"), ("value", "cmd://invented.example"))).Status, Is.EqualTo(201));
            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/URL",
                Bodies.Of(("value", "\\\\invented.example\\share\\tool.exe"))).Status, Is.EqualTo(200));
            Assert.That(Send("PUT", "/entries/" + _entry, Bodies.Of(("URL", planted))).Status, Is.EqualTo(200));
            Assert.That(Send("POST", "/groups/" + _group + "/entries", Bodies.Of(("URL", planted))).Status,
                Is.EqualTo(201));

            Assert.That(_vault.Field(_entry, "URL"), Is.EqualTo(planted));
        }

        [Test]
        public void A_read_says_when_an_entry_expires_and_null_when_it_does_not()
        {
            // An entry the operator expired, by a date or with Expire Now, read exactly as a live
            // one, so an agent went on using a credential KeePass had struck through. Written the
            // way `created` and `modified` are, because the client reads all three one way.
            _vault.Granting(_group, Rules.Granting(("agent", "R")));

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"expires\":null"));

            _vault.Expire(_entry, Moment);

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"expires\":\"2031-02-03T04:05:06Z\""));
        }

        [Test]
        public void A_listing_does_not_say_when_an_entry_expires()
        {
            // `list` gives names, and a moment is not one, so the listing is exactly what it was.
            _vault.Expire(_entry, Moment);
            _vault.Granting(_vault.RootAlias, AllButProtected);

            foreach (var path in new[] { "/entries", "/groups/" + _group + "/entries" })
            {
                var answer = Get(path);

                Assert.That(answer.Status, Is.EqualTo(200), path);
                Assert.That(answer.Json, Does.Contain("an invented service"), path);
                Assert.That(answer.Json, Does.Not.Contain("expires"), path);
            }
        }

        [Test]
        public void A_listing_keeps_expired_entries_unless_it_is_asked_to_leave_them_out()
        {
            // Listed by default, because KeePass lists them too, struck through, and a client
            // that has not heard of expiry must go on seeing the database it saw before.
            var past = _vault.AddEntry(_group, "an invented retired service");
            var future = _vault.AddEntry(_group, "an invented service expiring later");
            _vault.Expire(past, new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero));
            _vault.Expire(future, new DateTimeOffset(2999, 2, 3, 4, 5, 6, TimeSpan.Zero));
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            foreach (var path in new[] { "/entries", "/groups/" + _group + "/entries" })
            {
                foreach (var kept in new[] { Get(path), Asked("GET", path, "true") })
                {
                    Assert.That(kept.Status, Is.EqualTo(200), path);
                    Assert.That(kept.Json, Does.Contain("an invented retired service"), path);
                    Assert.That(kept.Json, Does.Contain("\"count\":3"), path);
                }

                var left = Asked("GET", path, "false");

                Assert.That(left.Status, Is.EqualTo(200), path + " " + left.Json);
                Assert.That(left.Json, Does.Not.Contain("an invented retired service"), path);
                Assert.That(left.Json, Does.Contain("an invented service expiring later"), path);
                Assert.That(left.Json, Does.Contain("an invented service\""), path + ", which never expires");
                Assert.That(left.Json, Does.Contain("\"count\":2"), path + ": what was left out is not counted");
            }
        }

        [Test]
        public void An_entry_whose_moment_has_come_has_expired()
        {
            // KeePass's own comparison is the moment against now, inclusive, and the gate's clock
            // is the one asked, since the gate is where the answer is made.
            _vault.Expire(_entry, DateTimeOffset.UtcNow);
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            var left = Asked("GET", "/entries", "false");

            Assert.That(left.Status, Is.EqualTo(200), left.Json);
            Assert.That(left.Json, Does.Contain("\"count\":0"));
        }

        [Test]
        public void A_head_takes_the_flag_its_get_takes()
        {
            // One request with the content left off, so the flag is declared on both and a head
            // asking it is not answered as a target nobody declared.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            Assert.That(Asked("HEAD", "/entries", "false").Status, Is.EqualTo(200));
            Assert.That(Asked("HEAD", "/groups/" + _group + "/entries", "false").Status, Is.EqualTo(200));
        }

        [TestCase("no", TestName = "a word the flag does not know")]
        [TestCase("FALSE", TestName = "false in capitals")]
        [TestCase("0", TestName = "a number")]
        [TestCase("", TestName = "nothing after the name")]
        public void A_flag_this_gate_does_not_read_is_refused_rather_than_read_as_true(string flag)
        {
            // Read as `true`, a caller that meant to leave expired entries out would be handed
            // every one of them and told nothing.
            _vault.Expire(_entry, new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero));
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            var answer = Asked("GET", "/entries", flag);

            Assert.That(answer.Status, Is.EqualTo(400), answer.Json);
            Assert.That(answer.Json, Does.Contain("malformed"));
            Assert.That(answer.Json, Does.Not.Contain("an invented service"));
        }

        [Test]
        public void The_flag_is_its_own_name_and_no_other()
        {
            // The one query parameter this API declares. A spelling of it the document does not
            // declare is a target the document does not declare, answered as an undeclared path.
            _vault.Granting(_vault.RootAlias, Rules.Granting(("agent", "L")));

            var head = new HttpRequestHead(
                "GET",
                "/entries",
                new Dictionary<string, string>(StringComparer.Ordinal) { { "expired", "false" } },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);
            var context = new GateContext(head, string.Empty, Paired());
            new ServedDatabase(_vault).PutOn(context);

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), context.Document());
        }

        [Test]
        public void An_update_moves_an_expiry_and_takes_one_off()
        {
            // What an agent rotating an expired password needs to finish the job: the fresh
            // password stayed marked expired, and KeePass went on refusing to auto-type it.
            _vault.Expire(_entry, new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero));
            _vault.Granting(_group, Rules.Granting(("agent", "UP")));

            var moved = Send("PUT", "/entries/" + _entry, "{\"expires\":\"2031-02-03T04:05:06Z\"}");

            Assert.That(moved.Status, Is.EqualTo(200), moved.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.EqualTo(Moment));

            var cleared = Send("PUT", "/entries/" + _entry, "{\"Password\":\"a-fresh-invented-one\",\"expires\":\"\"}");

            Assert.That(cleared.Status, Is.EqualTo(200), cleared.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.Null);
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("a-fresh-invented-one"));
        }

        [Test]
        public void An_update_naming_no_expiry_leaves_the_one_there()
        {
            _vault.Expire(_entry, Moment);
            _vault.Granting(_group, Rules.Granting(("agent", "U")));

            var answer = Send("PUT", "/entries/" + _entry, "{\"UserName\":\"agent-fictitious\"}");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.EqualTo(Moment));
        }

        [Test]
        public void An_expiry_is_not_written_as_a_field()
        {
            // The body's one member that is not a string the entry holds. Read as one, it would
            // have been a custom field called `expires` beside the five.
            _vault.Granting(_group, Rules.Granting(("agent", "CU")));

            Send("PUT", "/entries/" + _entry, "{\"expires\":\"2031-02-03T04:05:06Z\"}");
            var made = Made(Send("POST", "/groups/" + _group + "/entries",
                                 "{\"Title\":\"x\",\"expires\":\"2031-02-03T04:05:06Z\"}"));

            foreach (var uuid in new[] { _entry, made })
            {
                Assert.That(_vault.Field(uuid, "expires"), Is.Null, uuid);
                Assert.That(_vault.Entry(uuid).Expires, Is.EqualTo(Moment), uuid);
            }
        }

        [Test]
        public void Changing_an_expiry_needs_update_and_reading_one_needs_read()
        {
            // The existing verbs and nothing added to the mask: `read` sees it and `update`
            // changes it, and neither is implied by the other or by `list`.
            _vault.Granting(_group, Rules.Granting(("agent", "RL")));

            var refused = Send("PUT", "/entries/" + _entry, "{\"expires\":\"2031-02-03T04:05:06Z\"}");

            Assert.That(refused.Status, Is.EqualTo(404), refused.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.Null);

            _vault.Granting(_group, Rules.Granting(("agent", "UL")));

            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(404), "update does not read");
        }

        [Test]
        public void Changing_an_expiry_asks_nothing_of_the_protected_switch()
        {
            // When an entry expires is not a value the database protects, so a rule that may
            // update and may not touch the password still moves it.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            _vault.Granting(_group, Rules.Granting(("agent", "U")));

            var answer = Send("PUT", "/entries/" + _entry, "{\"expires\":\"2031-02-03T04:05:06Z\"}");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.EqualTo(Moment));
        }

        [Test]
        public void A_create_expires_when_it_is_told_to_and_never_otherwise()
        {
            _vault.NewEntryExpiresInDays = 30;
            _vault.Granting(_group, Rules.Granting(("agent", "C")));

            var silent = Made(Send("POST", "/groups/" + _group + "/entries", "{\"Title\":\"x\"}"));
            var empty = Made(Send("POST", "/groups/" + _group + "/entries", "{\"Title\":\"x\",\"expires\":\"\"}"));
            var given = Made(Send("POST", "/groups/" + _group + "/entries",
                                  "{\"Title\":\"x\",\"expires\":\"2031-02-03T04:05:06Z\"}"));

            Assert.That(_vault.Entry(silent).Expires, Is.Null,
                "the options give new entries an expiry, and a create that asked for none gets none");
            Assert.That(_vault.Entry(empty).Expires, Is.Null);
            Assert.That(_vault.Entry(given).Expires, Is.EqualTo(Moment));
        }

        [Test]
        public void Auto_on_a_create_is_whatever_KeePass_s_options_give_a_new_entry()
        {
            _vault.Granting(_group, Rules.Granting(("agent", "C")));

            var off = Made(Send("POST", "/groups/" + _group + "/entries", "{\"expires\":\"auto\"}"));

            _vault.NewEntryExpiresInDays = 30;
            var before = DateTimeOffset.UtcNow.AddDays(30);
            var on = Made(Send("POST", "/groups/" + _group + "/entries", "{\"expires\":\"auto\"}"));
            var after = DateTimeOffset.UtcNow.AddDays(30);

            Assert.That(_vault.Entry(off).Expires, Is.Null, "the option is off until somebody switches it on");
            Assert.That(_vault.Entry(on).Expires, Is.InRange(before, after));
        }

        [Test]
        public void Auto_on_an_update_is_refused()
        {
            // An entry that exists already has the expiry somebody decided on, and asking the
            // options to decide it again is not a change this API makes.
            _vault.NewEntryExpiresInDays = 30;
            _vault.Expire(_entry, Moment);
            _vault.Granting(_group, Rules.Granting(("agent", "U")));

            var answer = Send("PUT", "/entries/" + _entry, "{\"UserName\":\"x\",\"expires\":\"auto\"}");

            Assert.That(answer.Status, Is.EqualTo(400), answer.Json);
            Assert.That(answer.Json, Does.Contain("malformed"));
            Assert.That(_vault.Entry(_entry).Expires, Is.EqualTo(Moment));
            Assert.That(_vault.Field(_entry, "UserName"), Is.Null.Or.Empty, "nothing in the body was written");
        }

        [TestCase("2031-02-03", TestName = "a date with no time")]
        [TestCase("2031-02-03T04:05:06", TestName = "a time with no zone")]
        [TestCase("2031-02-03T04:05:06+01:00", TestName = "an offset rather than UTC")]
        [TestCase("2031-02-03T04:05:06.5Z", TestName = "a fraction of a second")]
        [TestCase("2031-02-03t04:05:06z", TestName = "the separators in lower case")]
        [TestCase("2031-02-30T04:05:06Z", TestName = "a day the month does not have")]
        [TestCase("never", TestName = "a word")]
        [TestCase("AUTO", TestName = "auto in capitals")]
        public void An_expiry_this_gate_does_not_read_is_refused_on_both_routes(string expires)
        {
            // One spelling, the one every moment this API answers with is written in, so the
            // `expires` a read answers goes back as it came and nothing is guessed at.
            _vault.Granting(_group, Rules.Granting(("agent", "CU")));
            var entries = _vault.Entries().Count;
            var body = Bodies.Of(("Title", "x"), ("expires", expires));

            var created = Send("POST", "/groups/" + _group + "/entries", body);
            var updated = Send("PUT", "/entries/" + _entry, body);

            Assert.That(created.Status, Is.EqualTo(400), created.Json);
            Assert.That(updated.Status, Is.EqualTo(400), updated.Json);
            Assert.That(_vault.Entries().Count, Is.EqualTo(entries), "nothing was made");
            Assert.That(_vault.Field(_entry, "Title"), Is.EqualTo("an invented service"), "nothing was changed");
        }

        /// <summary>A moment with nothing below the second, which is all a `.kdbx` keeps of one.</summary>
        private static readonly DateTimeOffset Moment = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);

        /// <summary>What a create answered with, which is what addresses it.</summary>
        private static string Made(Answer answer)
        {
            Assert.That(answer.Status, Is.EqualTo(VaultHandlers.Created), answer.Json);

            var found = System.Text.RegularExpressions.Regex.Match(answer.Json, "\"uuid\":\"([0-9a-fA-F]{32})\"");
            Assert.That(found.Success, Is.True, answer.Json);
            return found.Groups[1].Value;
        }

        /// <summary>A request carrying the listing flag, the way the transport hands a parsed query on.</summary>
        private Answer Asked(string method, string path, string includeExpired)
        {
            return Request(method, path, null, null, null, "includeExpired", includeExpired);
        }

        private Answer Get(string path)
        {
            return Send("GET", path, null);
        }

        /// <summary>
        /// What a write could have changed: every field of the entry, with its value and whether it
        /// is protected, and how many entries the group holds.
        /// </summary>
        private string Written()
        {
            var fields = _vault.Whole(_entry).Fields.Select(
                field => field.Name + "=" + _vault.Field(_entry, field.Name) + (field.IsProtected ? ", protected" : string.Empty));
            return string.Join("\n", fields) + "\n" + _vault.Entries(_group).Count;
        }

        private Answer Send(string method, string path, string body)
        {
            return Request(method, path, body, null, null, null, null);
        }

        private static string Base64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>The same request, carrying an `If-Match`.</summary>
        private Answer Conditioned(string method, string path, string body, string ifMatch)
        {
            return Request(method, path, body, "If-Match", ifMatch, null, null);
        }

        /// <summary>The same request, saying what the body is.</summary>
        private Answer Typed(string method, string path, string body, string mediaType)
        {
            return Request(method, path, body, "Content-Type", mediaType, null, null);
        }

        /// <summary>
        /// One request through the whole pipeline, with at most one header and one query name of
        /// its own, which is every shape these tests send, held to the statuses the document
        /// declares for it.
        /// </summary>
        private Answer Request(
            string method, string path, string body, string header, string headerValue, string query, string queryValue)
        {
            var text = body ?? string.Empty;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

            if (header != null)
            {
                headers[header] = headerValue;
            }

            if (query != null)
            {
                parameters[query] = queryValue;
            }

            var context = new GateContext(new HttpRequestHead(method, path, parameters, headers, Bodies.LengthOf(text)), text, Paired());
            new ServedDatabase(_vault, null, _log).PutOn(context);

            _router.Handle(context);

            // An answer written as it is sent is written here, as the transport writes it, so
            // every read of an entry or of a field driven from here reads its values.
            var json = context.Document();
            Declared(method, path, context.Status, json ?? string.Empty);

            return new Answer(context.Status, json, context.Headers);
        }

        /// <summary>
        /// The statuses each operation declares, by its identifier, read off the document once.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<string, HashSet<string>>> Statuses =
            new Lazy<IReadOnlyDictionary<string, HashSet<string>>>(() =>
            {
                Microsoft.OpenApi.Models.OpenApiDocument document;
                using (var stream = typeof(SpecDocument).Assembly.GetManifestResourceStream("KeePassGate.Spec.openapi.yaml"))
                {
                    Microsoft.OpenApi.Readers.OpenApiDiagnostic diagnostic;
                    document = new Microsoft.OpenApi.Readers.OpenApiStreamReader().Read(stream, out diagnostic);
                }

                return document.Paths.Values
                    .SelectMany(path => path.Operations.Values)
                    .ToDictionary(o => o.OperationId, o => new HashSet<string>(o.Responses.Keys), StringComparer.Ordinal);
            });

        /// <summary>The operations the router is built from, read once.</summary>
        private static readonly Lazy<IReadOnlyList<SpecOperation>> Operations =
            new Lazy<IReadOnlyList<SpecOperation>>(() => SpecDocument.Embedded().Operations.ToList());

        /// <summary>
        /// Fails a request answered with a status the document does not declare for it.
        /// </summary>
        /// <remarks>
        /// **Every request here comes through this**, so this is where a handler that learns a new
        /// refusal is held to the document a client is generated from. An operation answering a
        /// status it does not declare is how `updateEntry` came to answer `409`, the writes
        /// `500 not saved` and the field routes `400`, each found by reading rather than by a test.
        ///
        /// Besides what the operation declares, only what the document declares for every path:
        /// the `405` a declared path answers to a method it does not declare, the `404 no such
        /// route` and `401` an undeclared target gets, and the `500 internal error` a read gives
        /// when the vault behind it breaks its own contract, which the top description names as
        /// declared nowhere. What the transport answers before any route is chosen never reaches
        /// a router driven directly, so none of it is allowed here.
        /// </remarks>
        private static void Declared(string method, string path, int status, string json)
        {
            IReadOnlyDictionary<string, string> captured;
            var onPath = Operations.Value.Where(o => o.Path.TryMatch(path, out captured)).ToList();
            var operation = onPath.SingleOrDefault(o => string.Equals(o.Method, method, StringComparison.Ordinal));

            var undeclaredTarget = status == 404 && json.Contains(SpecRouter.UnreachableError);
            var brokenVault = status == 500 && json.Contains("internal error");
            var undeclaredMethod = status == 405 && onPath.Count > 0;

            bool allowed;
            if (operation != null)
            {
                var declared = Statuses.Value[operation.OperationId].Contains(
                    status.ToString(CultureInfo.InvariantCulture));
                allowed = declared || undeclaredTarget || brokenVault;
            }
            else
            {
                allowed = undeclaredTarget || status == 401 || undeclaredMethod;
            }

            Assert.That(allowed, Is.True, string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} was answered {2}, which {3} does not declare: {4}",
                method, path, status, operation == null ? "the document" : operation.OperationId, json));
        }

        /// <summary>
        /// A request to this operation that sets or takes away a protected value, against an entry
        /// made ready for it.
        /// </summary>
        private GateContext Withholding(string operation)
        {
            const string Chosen = "chosen-by-the-caller";
            if (operation == "updateEntry" || operation == "stripField")
            {
                _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultAskings.Free);
            }

            switch (operation)
            {
                case "createEntry":
                    return Context("POST", "/groups/" + _group + "/entries", Bodies.Of(("Title", "x"), ("Password", Chosen)));
                case "updateEntry":
                    return Context("PUT", "/entries/" + _entry, Bodies.Of(("Password", Chosen)));
                case "createField":
                    return Context("POST", "/entries/" + _entry + "/fields", Bodies.Of(("name", "Password"), ("value", Chosen)));
                case "stripField":
                    return Context("DELETE", "/entries/" + _entry + "/fields/Password", null);
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, "no request is written for it");
            }
        }

        private GateContext Context(string method, string path, string body)
        {
            var text = body ?? string.Empty;
            var context = new GateContext(Head(method, path, Bodies.LengthOf(text)), text, Paired());

            new ServedDatabase(_vault).PutOn(context);
            return context;
        }

        private static HttpRequestHead Head(string method, string path)
        {
            return Head(method, path, 0);
        }

        private static HttpRequestHead Head(string method, string path, long length)
        {
            return new HttpRequestHead(
                method,
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                length);
        }

        private ClientIdentity Paired()
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, _profiles, _removed);
            }
        }

        private sealed class Answer
        {
            public Answer(int status, string json, System.Net.WebHeaderCollection headers)
            {
                Status = status;
                Json = json ?? string.Empty;
                Tag = headers == null ? null : headers["ETag"];

                var written = Json.StartsWith("{", StringComparison.Ordinal)
                    ? Newtonsoft.Json.Linq.JObject.Parse(Json)["etag"]
                    : null;
                Written = written == null ? null : (string)written;
            }

            public int Status { get; private set; }

            public string Json { get; private set; }

            /// <summary>The `ETag` the answer carried, quotes and all, or null when it carried none.</summary>
            public string Tag { get; private set; }

            /// <summary>The tag a change answered with in its body, quotes and all, or null.</summary>
            public string Written { get; private set; }
        }
    }
}
