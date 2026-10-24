using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Audit;
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
        private const string Everything =
            "{\"reader\":[\"create\",\"read\",\"update\",\"strip\",\"delete\",\"list\",\"attachments\"]}";

        /// <summary>
        /// A spelling of `Password` a database cannot tell from the real one, and its path form.
        /// </summary>
        /// <remarks>
        /// U+00AD, the soft hyphen. KeePass keeps an entry's strings under the default comparer,
        /// which compares by the current culture, and that collation gives it no weight at all, so
        /// this name addresses the entry's `Password`. It is a format character rather than a
        /// control one, so nothing in the path or the body refuses it on the way in.
        /// </remarks>
        private const string PasswordAlias = "Pass\u00ADword";

        private const string PasswordAliasPath = "Pass%C2%ADword";

        /// <summary>A combining acute accent, so that `Cafe` plus it is `Café` in the other normal form.</summary>
        private const string Combining = "\u0301";

        private FakeVault _vault;
        private SpecRouter _router;
        private string _group;
        private string _entry;

        [SetUp]
        public void SetUp()
        {
            _vault = new FakeVault();
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
            _vault.Granting(_vault.RootAlias, Everything);

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
            _vault.Granting(_vault.RootAlias, Everything);

            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(200));
            Assert.That(Get("/groups/" + _group).Status, Is.EqualTo(200));
        }

        [Test]
        public void A_group_listing_gives_names_and_never_notes()
        {
            // `list` gives names. A group's notes are free text an operator wrote, and a client
            // holding `list` alone is refused them by reading the group, so a listing handing them
            // out would make the smaller grant the more revealing one.
            Assume.That(_vault.UpdateGroup(_group, null, "invented notes for read alone", VaultTagging.None).Ok, Is.True);
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

            foreach (var path in new[] { "/groups", "/groups/" + _vault.RootAlias + "/groups" })
            {
                var answer = Get(path);

                Assert.That(answer.Status, Is.EqualTo(200), path);
                Assert.That(answer.Json, Does.Contain("\"name\":\"Fictitious\""), path);
                Assert.That(answer.Json, Does.Not.Contain("notes"), path);
            }

            Assert.That(Get("/groups/" + _group).Status, Is.EqualTo(404), "reading needs `read`");

            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"read\"]}");

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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");
            _vault.Granting(other, "{\"reader\":[]}");

            var answer = Get("/entries");

            Assert.That(answer.Status, Is.EqualTo(200));
            Assert.That(answer.Json, Does.Contain("an invented service"));
            Assert.That(answer.Json, Does.Not.Contain("not for this client"));
            Assert.That(answer.Json, Does.Contain("\"count\":1"));
        }

        [Test]
        public void The_root_can_be_reached_by_name_rather_than_by_identifier()
        {
            // Which is what lets a client granted `create` and nothing else put an entry somewhere.
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"create\"]}");

            var made = Send("POST", "/groups/root/entries", "{\"Title\":\"an invented service\"}");

            Assert.That(made.Status, Is.EqualTo(201));
            Assert.That(made.Json, Does.Contain("uuid"));
        }

        [Test]
        public void Creating_needs_create_and_nothing_else_does()
        {
            _vault.Granting(_group, "{\"reader\":[\"create\"]}");

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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

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
            _vault.Granting(_group, Everything);
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultTagging.None);
            _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultTagging.None);

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
            _vault.Granting(_vault.RootAlias, Everything);

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
        public void A_tag_moves_with_what_the_node_holds_and_comes_back_with_it()
        {
            _vault.Granting(_vault.RootAlias, Everything);
            var entry = "/entries/" + _entry;
            var group = "/groups/" + _group;

            var before = Get(entry).Tag;
            var groupBefore = Get(group).Tag;

            Assert.That(Send("PUT", entry, "{\"Title\":\"a renamed service\"}").Status, Is.EqualTo(200));
            var changed = Get(entry).Tag;
            Assert.That(changed, Is.Not.EqualTo(before), "a changed field is a changed entry");
            Assert.That(Get(group).Tag, Is.EqualTo(groupBefore), "and says nothing new about its group");

            Assert.That(Send("PUT", entry, "{\"Title\":\"an invented service\"}").Status, Is.EqualTo(200));
            Assert.That(Get(entry).Tag, Is.EqualTo(before), "what was there, put back, is the tag that was there");

            Assert.That(Send("PUT", group, "{\"Notes\":\"invented notes\"}").Status, Is.EqualTo(200));
            Assert.That(Get(group).Tag, Is.Not.EqualTo(groupBefore));
        }

        [Test]
        public void A_write_against_the_tag_it_read_goes_ahead_and_answers_with_the_next_one()
        {
            _vault.Granting(_group, Everything);
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

        [Test]
        public void A_write_against_a_tag_the_entry_has_moved_on_from_is_refused_and_changes_nothing()
        {
            // The lost update this is for: one client read the entry, another changed it, and the
            // first wrote back what it had read.
            _vault.Granting(_group, Everything);
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
                "{\"reader\":[\"create\",\"read\",\"update\",\"strip\",\"delete\",\"list\",\"attachments\"]}");
            _vault.CreateField(_entry, "Account", "an invented account", false, VaultTagging.None);
            _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultTagging.None);
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
                new[] { "POST", entry + "/attachments", "{\"name\":\"other.txt\",\"content\":\"" + Base64("invented") + "\"}" },
                new[] { "GET", entry + "/attachments/notes.txt", null },
                new[] { "HEAD", entry + "/attachments/notes.txt", null },
                new[] { "PUT", entry + "/attachments/notes.txt", "{\"content\":\"" + Base64("changed") + "\"}" },
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
            foreach (var request in requests)
            {
                var refused = Conditioned(request[0], request[1], request[2], "\"00000000000000000000000000000000\"");

                Assert.That(refused.Status, Is.EqualTo(412), request[0] + " " + request[1]);
            }

            Assert.That(_vault.Field(_entry, "Account"), Is.EqualTo("an invented account"));
            Assert.That(_vault.Attachment(_entry, "notes.txt"), Is.Not.Null);
            Assert.That(_vault.Group(empty), Is.Not.Null);

            // And every one served under `*`, in this order, which ends with the entry gone.
            foreach (var request in requests)
            {
                var served = Conditioned(request[0], request[1], request[2], "*");

                Assert.That(served.Status, Is.EqualTo(200).Or.EqualTo(201), request[0] + " " + request[1] + " " + served.Json);
            }

            Assert.That(_vault.Entry(_entry), Is.Null);
        }

        [Test]
        public void A_condition_on_a_node_the_rules_refuse_is_the_same_404()
        {
            // A refused node and an absent one answer alike, so a condition cannot be the way to
            // tell them apart: whatever tag is named, a client that may not reach the node learns
            // nothing about it.
            _vault.Granting(_group, "{\"reader\":[\"list\"]}");

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
            _vault.Granting(_vault.RootAlias, Everything);
            var nothing = "\"00000000000000000000000000000000\"";

            Assert.That(Conditioned("GET", "/entries", null, nothing).Status, Is.EqualTo(200));
            Assert.That(Conditioned("GET", "/groups/" + _group + "/entries", null, nothing).Status, Is.EqualTo(200));
            Assert.That(Conditioned("POST", "/groups/" + _group + "/entries", "{\"Title\":\"x\"}", nothing).Status,
                Is.EqualTo(201));
        }

        [Test]
        public void A_client_that_may_not_read_everything_a_tag_covers_is_handed_none()
        {
            // An entry's tag covers its attachments' bytes, so a rule whose `read` does not reach
            // them may not have it: with one, a client could test guesses at what it may not read
            // by writing them where it may write and watching the tag that came back.
            _vault.Granting(_group, "{\"reader\":[\"read\",\"update\",\"list\"]}");
            _vault.CreateAttachment(_entry, "notes.txt", Encoding.UTF8.GetBytes("invented"), VaultTagging.None);
            var entry = "/entries/" + _entry;

            foreach (var path in new[] { entry, entry + "/fields", entry + "/fields/Title" })
            {
                foreach (var method in new[] { "GET", "HEAD" })
                {
                    var answer = Send(method, path, null);

                    Assert.That(answer.Status, Is.EqualTo(200), method + " " + path);
                    Assert.That(answer.Tag, Is.Null, method + " " + path);
                }
            }

            var served = Send("PUT", entry, "{\"Title\":\"renamed\"}");
            Assert.That(served.Status, Is.EqualTo(200), "a write that asks nothing is served as it always was");
            Assert.That(served.Written, Is.Null, "and answers no tag");

            // A condition it cannot be told the answer to is one it cannot meet, `*` included, and
            // ignoring it would make the write unconditional, which is the one thing it did not ask.
            foreach (var condition in new[] { "*", "\"00000000000000000000000000000000\"" })
            {
                Assert.That(Conditioned("GET", entry, null, condition).Status, Is.EqualTo(412), condition);

                var refused = Conditioned("PUT", entry, "{\"Title\":\"renamed again\"}", condition);
                Assert.That(refused.Status, Is.EqualTo(412), condition);
                Assert.That(_vault.Field(_entry, "Title"), Is.EqualTo("renamed"), condition);
            }

            _vault.Granting(_group, Everything);
            Assert.That(Get(entry).Tag, Is.Not.Null, "and a rule that does reach them is handed it");
        }

        [Test]
        public void An_entry_holding_a_protected_value_is_tagged_only_for_a_rule_that_reads_it()
        {
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            var entry = "/entries/" + _entry;

            _vault.Granting(_group, Everything);
            Assert.That(Get(entry).Status, Is.EqualTo(200));
            Assert.That(Get(entry).Tag, Is.Null, "read reaching attachments and not protected values");
            Assert.That(Conditioned("PUT", entry, "{\"UserName\":\"x\"}", "*").Status, Is.EqualTo(412));

            _vault.Granting(_group,
                "{\"reader\":[\"create\",\"read\",\"update\",\"strip\",\"delete\",\"list\",\"attachments\",\"protected\"]}");
            var tag = Get(entry).Tag;
            Assert.That(tag, Is.Not.Null);

            var written = Conditioned("PUT", entry, "{\"UserName\":\"x\"}", tag);
            Assert.That(written.Status, Is.EqualTo(200), written.Json);
            Assert.That(written.Written, Is.EqualTo(Get(entry).Tag));
        }

        [Test]
        public void A_group_s_tag_is_for_a_rule_that_reads_the_group()
        {
            _vault.Granting(_group, "{\"reader\":[\"update\"]}");
            var group = "/groups/" + _group;

            Assert.That(Conditioned("PUT", group, "{\"Notes\":\"invented\"}", "*").Status, Is.EqualTo(412));

            var served = Send("PUT", group, "{\"Notes\":\"invented\"}");
            Assert.That(served.Status, Is.EqualTo(200), served.Json);
            Assert.That(served.Written, Is.Null);

            _vault.Granting(_group, "{\"reader\":[\"read\",\"update\"]}");
            var written = Conditioned("PUT", group, "{\"Notes\":\"invented again\"}", Get(group).Tag);
            Assert.That(written.Status, Is.EqualTo(200), written.Json);
            Assert.That(written.Written, Is.EqualTo(Get(group).Tag));
        }

        [Test]
        public void A_write_that_changes_nothing_still_asks_the_condition()
        {
            // An update naming nothing, or only what is there, writes nothing and still answers
            // as an update does, `412` included: the client asked to be told if the entry moved on.
            _vault.Granting(_group, Everything);
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
        [TestCase("entry", 2)]
        [TestCase("entry", 3)]
        [TestCase("field", 0)]
        [TestCase("field", 1)]
        [TestCase("field", 2)]
        [TestCase("field", 3)]
        public void A_change_landing_anywhere_before_the_write_is_still_refused(string route, int lookups)
        {
            // The condition is asked inside the write, of the node as the write finds it. Asked of
            // a handler's own read instead, a change landing between that read and the write was
            // carried straight over, and answered 200. Both routes ask the vault three times before
            // they write, the router's lookup and the handler's two, and the write is the fourth,
            // so these are every moment before it.
            _vault.Granting(_group, Everything);
            var entry = "/entries/" + _entry;
            var tag = Get(entry).Tag;
            var landed = false;

            _vault.After(lookups, () =>
            {
                landed = true;
                _vault.UpdateEntry(
                    _entry, new Dictionary<string, string> { { "Title", "changed by somebody else" } }, VaultTagging.None);
            });

            var answer = route == "entry"
                ? Conditioned("PUT", entry, "{\"Title\":\"what the first client meant\"}", tag)
                : Conditioned("PUT", entry + "/fields/Title", "{\"value\":\"what the first client meant\"}", tag);

            Assert.That(landed, Is.True, "every one of these lands inside the request");
            Assert.That(answer.Status, Is.EqualTo(412), answer.Json);
            Assert.That(_vault.Field(_entry, "Title"), Is.EqualTo("changed by somebody else"));
        }

        [Test]
        public void Reading_an_entry_gives_its_fields_and_says_which_are_protected()
        {
            _vault.Granting(_group, Everything);
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
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\"]}");

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
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\",\"strip\"]}");

            var aliased = "/entries/" + _entry + "/fields/" + PasswordAliasPath;

            Assert.That(Send("PUT", aliased, "{\"value\":\"chosen-by-the-caller\"}").Status, Is.EqualTo(404));
            Assert.That(Send("DELETE", aliased, null).Status, Is.EqualTo(404));
            Assert.That(Send("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"" + PasswordAlias + "\",\"value\":\"chosen-by-the-caller\"}").Status,
                Is.EqualTo(404));

            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("not-a-real-password"));
        }

        [Test]
        public void Nor_is_it_reached_through_one_by_a_rule_that_may_have_protected_values()
        {
            // The other half, and the one the guard cannot answer: this rule may write the
            // password, so what refuses the alias is the name not being a name the entry carries.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"protected\"]}");

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
            _vault.CreateField(_entry, "Cafe" + Combining, "an invented value", false, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

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
            _vault.CreateField(_entry, "Recovery", "not-a-real-secret", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

            var answer = Get("/entries/" + _entry + "/fields/Recovery");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Json, Does.Contain("\"value\":null"));
            Assert.That(answer.Json, Does.Contain("\"protected\":true"));
        }

        [Test]
        public void An_attachment_asked_for_under_such_a_spelling_is_not_read_at_all()
        {
            // `readAttachment` served the stored attachment's bytes under the caller's spelling,
            // and got there past the ceiling the listing is asked about first, because the listing
            // compares names the way the guards do and found nothing to measure.
            _vault.Granting(_group, Everything);
            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("an invented attachment"));
            Send("POST", "/entries/" + _entry + "/attachments",
                "{\"name\":\"notes.txt\",\"content\":\"" + content + "\"}");

            _vault.AttachmentReads = 0;
            var asked = Get("/entries/" + _entry + "/attachments/notes%C2%AD.txt");

            Assert.That(asked.Status, Is.EqualTo(404));
            Assert.That(Get("/entries/" + _entry + "/attachments/notes.txt").Json,
                Does.Contain("\"content\":\"" + content + "\""), "and the stored name still answers");
        }

        [Test]
        public void A_rule_that_reaches_protected_values_may_overwrite_one()
        {
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\",\"protected\"]}");

            var answer = Send("PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"also-invented\"}");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Field(_entry, "Password"), Is.EqualTo("also-invented"));
        }

        [Test]
        public void A_protected_field_cannot_be_added_without_it_either()
        {
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"create\",\"protected\"]}");

            foreach (var marker in new[] { "True", "TRUE", "yes", "1", "on", "" })
            {
                var answer = Send("POST", "/entries/" + _entry + "/fields",
                    "{\"name\":\"Recovery\",\"value\":\"invented\",\"protected\":\"" + marker + "\"}");

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
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"create\"]}");

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
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"create\",\"strip\"]}");

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
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"strip\"]}");

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(200));
            Assert.That(_vault.Field(_entry, "PIN"), Is.Null);
        }

        [Test]
        public void A_rule_that_reaches_protected_values_strips_one_and_puts_another_back()
        {
            _vault.CreateField(_entry, "PIN", "not-a-real-secret", true, VaultTagging.None);
            _vault.Granting(
                _group, "{\"reader\":[\"read\",\"list\",\"create\",\"strip\",\"protected\"]}");

            Assert.That(Send("DELETE", "/entries/" + _entry + "/fields/PIN", null).Status, Is.EqualTo(200));

            var made = Send("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"PIN\",\"value\":\"also-invented\",\"protected\":\"true\"}");

            Assert.That(made.Status, Is.EqualTo(201), made.Json);
            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"protected\":[\"PIN\"]"));
        }

        [Test]
        public void The_entry_route_is_not_a_way_around_it()
        {
            // The same value, set through the entry rather than the field. A gate on one route
            // and not the other is not a gate.
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\"]}");

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
            _vault.Granting(_vault.RootAlias, Everything);

            var listed = Send("GET", "/entries", "{\"group\":\"" + _group + "\"}");
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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

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
            _vault.CreateField(_entry, "URL", "https://invented.example", false, VaultTagging.None);
            _vault.Protect(_entry, "URL");
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

            var listed = Get("/entries");

            Assert.That(listed.Json, Does.Contain("\"url\":null"));
            Assert.That(listed.Json, Does.Not.Contain("invented.example"));
            Assert.That(listed.Json, Does.Contain("an invented service"));
        }

        [Test]
        public void A_listing_shows_a_protected_title_to_a_rule_that_reaches_one()
        {
            _vault.Protect(_entry, "Title");
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\",\"protected\"]}");

            var listed = Get("/entries");

            Assert.That(listed.Json, Does.Contain("an invented service"),
                "the switch is what makes it visible, and it did not");
        }

        [Test]
        public void A_protected_value_is_withheld_from_a_rule_that_does_not_reach_protected_values()
        {
            // The grant this exists for. An agent that has to recognize an entry gets its title,
            // its address and the fields nobody protected, and is told nothing it could sign in
            // with. `list` says too little for that and `read` used to say too much.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.CreateField(_entry, "UserName", "agent-fictitious", false, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

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
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"protected\"]}");

            var answer = Get("/entries/" + _entry);

            Assert.That(answer.Json, Does.Contain("\"Password\":\"not-a-real-password\""));
        }

        [Test]
        public void One_protected_field_read_on_its_own_is_withheld_the_same_way()
        {
            // The same question asked through the other route, because a value withheld from the
            // entry and handed over one level down would be no rule at all.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

            var answer = Get("/entries/" + _entry + "/fields/Password");

            Assert.That(answer.Status, Is.EqualTo(200), "the field is there, and its value is not for this caller");
            Assert.That(answer.Json, Does.Contain("\"value\":null"));
            Assert.That(answer.Json, Does.Contain("\"protected\":true"));
            Assert.That(answer.Json, Does.Not.Contain("not-a-real-password"));
        }

        [Test]
        public void A_field_is_added_changed_and_taken_off_again()
        {
            _vault.Granting(_group, Everything);

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
            _vault.Granting(_group, Everything);
            Send("POST", "/entries/" + _entry + "/fields", "{\"name\":\"Invented\",\"value\":\"one\"}");

            var again = Send("POST", "/entries/" + _entry + "/fields", "{\"name\":\"Invented\",\"value\":\"two\"}");

            Assert.That(again.Status, Is.EqualTo(409));
            Assert.That(again.Json, Does.Contain("conflict"));
        }

        [Test]
        public void Changing_a_field_that_is_not_there_says_there_is_nothing_there()
        {
            _vault.Granting(_group, Everything);

            var answer = Send("PUT", "/entries/" + _entry + "/fields/NotThere", "{\"value\":\"x\"}");

            Assert.That(answer.Status, Is.EqualTo(404));
        }

        [Test]
        public void Listing_fields_names_them_without_quoting_them()
        {
            _vault.Granting(_group, Everything);

            var answer = Get("/entries/" + _entry + "/fields");

            Assert.That(answer.Json, Does.Contain("\"name\":\"Title\""));
            Assert.That(answer.Json, Does.Not.Contain("an invented service"),
                "a listing says which fields exist and never what is in them");
        }

        [Test]
        public void An_attachment_goes_in_and_comes_back_out()
        {
            _vault.Granting(_group, Everything);
            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("an invented attachment"));

            Assert.That(
                Send("POST", "/entries/" + _entry + "/attachments",
                    "{\"name\":\"notes.txt\",\"content\":\"" + content + "\"}").Status,
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
            _vault.Granting(_group, Everything);
            var oversize = new byte[VaultHandlers.MaxAttachmentBytes + 1];
            Assume.That(_vault.CreateAttachment(_entry, "big.bin", oversize, VaultTagging.None).Ok, Is.True);

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
            // The vault is asked with the ceiling and promises not to decrypt past it. When it
            // hands over more anyway, the check of what came back is the only thing between that
            // attachment and an answer over the ceiling.
            _vault.Granting(_group, Everything);
            var oversize = new byte[VaultHandlers.MaxAttachmentBytes + 1];
            Assume.That(_vault.CreateAttachment(_entry, "big.bin", oversize, VaultTagging.None).Ok, Is.True);
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
            _vault.Granting(_group, Everything);
            var largest = new byte[VaultHandlers.MaxAttachmentBytes];
            Assume.That(_vault.CreateAttachment(_entry, "big.bin", largest, VaultTagging.None).Ok, Is.True);

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
            _vault.Granting(_group, Everything);
            var largest = Convert.ToBase64String(new byte[VaultHandlers.MaxAttachmentBytes]);

            Assert.That(Send("POST", "/entries/" + _entry + "/attachments",
                "{\"name\":\"big.bin\",\"content\":\"" + largest + "\"}").Status, Is.EqualTo(201));
            Assert.That(Send("PUT", "/entries/" + _entry + "/attachments/big.bin",
                "{\"content\":\"" + largest + "\"}").Status, Is.EqualTo(200));

            Assert.That(Get("/entries/" + _entry + "/attachments/big.bin").Status, Is.EqualTo(200));
        }

        [Test]
        public void A_write_one_byte_over_the_ceiling_is_refused()
        {
            // The one size the document cannot refuse: one byte over the ceiling is a multiple of
            // three and so spells as many base64 characters as the ceiling does. Stored, it would be
            // an attachment every read answers 413, so the write refuses it the way it refuses the
            // next size up, and leaves what was there alone.
            _vault.Granting(_group, Everything);
            var over = Convert.ToBase64String(new byte[VaultHandlers.MaxAttachmentBytes + 1]);
            Assume.That(over.Length, Is.EqualTo(4 * ((VaultHandlers.MaxAttachmentBytes + 2) / 3)),
                "this is about the size the document's length admits");

            Assert.That(Send("POST", "/entries/" + _entry + "/attachments",
                "{\"name\":\"big.bin\",\"content\":\"" + over + "\"}").Status, Is.EqualTo(400));
            Assert.That(_vault.Attachments(_entry), Is.Empty, "nothing is stored");

            Assume.That(_vault.CreateAttachment(_entry, "small.bin", new byte[] { 1 }, VaultTagging.None).Ok, Is.True);

            Assert.That(Send("PUT", "/entries/" + _entry + "/attachments/small.bin",
                "{\"content\":\"" + over + "\"}").Status, Is.EqualTo(400));
            Assert.That(_vault.Attachment(_entry, "small.bin"), Is.EqualTo(new byte[] { 1 }),
                "what was there is left alone");
        }

        [Test]
        public void Stripping_an_attachment_needs_no_switch()
        {
            _vault.Granting(_group, Everything);
            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("an invented attachment"));

            Assert.That(
                Send("POST", "/entries/" + _entry + "/attachments",
                    "{\"name\":\"notes.txt\",\"content\":\"" + content + "\"}").Status,
                Is.EqualTo(201),
                "the attachment has to be there before removing it means anything");

            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"strip\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"create\",\"read\",\"update\",\"delete\",\"list\"]}");

            Assert.That(Get("/entries/" + _entry + "/attachments").Status, Is.EqualTo(404));
            Assert.That(Get("/entries/" + _entry + "/attachments/notes.txt").Status, Is.EqualTo(404));
            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(200), "and the entry itself is not");
        }

        [Test]
        public void An_attachment_name_with_a_separator_in_it_is_addressable()
        {
            // Which needs the path to be split before it is decoded, and is why that was fixed
            // before any of this was written.
            _vault.Granting(_group, Everything);
            var content = Convert.ToBase64String(new byte[] { 1, 2, 3 });

            Send("POST", "/entries/" + _entry + "/attachments",
                "{\"name\":\"a/b\",\"content\":\"" + content + "\"}");

            var read = Get("/entries/" + _entry + "/attachments/a%2Fb");

            Assert.That(read.Status, Is.EqualTo(200));
            Assert.That(read.Json, Does.Contain("\"name\":\"a/b\""));
        }

        [Test]
        public void A_body_that_is_not_what_the_document_declares_is_refused_before_anything_happens()
        {
            _vault.Granting(_group, Everything);

            var answer = Send("POST", "/entries/" + _entry + "/fields", "{\"value\":\"no name\"}");

            Assert.That(answer.Status, Is.EqualTo(400));
            Assert.That(answer.Json, Does.Contain("malformed"));
        }

        [Test]
        public void A_body_sent_under_another_media_type_is_refused_rather_than_read_as_json()
        {
            // The refusal nothing reached. `Typed` decides it from the one media type this API
            // declares, and until this ran the only evidence that it worked at all was reading it.
            _vault.Granting(_group, Everything);

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
            _vault.Granting(_group, Everything);

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
            _vault.Granting(_vault.RootAlias, Everything);

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
            _vault.Granting(_vault.RootAlias, Everything);

            var answer = Send("DELETE", "/groups/root", null);

            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
            Assert.That(answer.Json, Does.Not.Contain("conflict"));
        }

        [Test]
        public void A_group_that_still_holds_something_is_not_removed()
        {
            _vault.Granting(_vault.RootAlias, Everything);

            var answer = Send("DELETE", "/groups/" + _group, null);

            Assert.That(answer.Status, Is.EqualTo(409));
        }

        [Test]
        public void A_dialog_open_on_what_is_being_written_answers_that_it_is_busy()
        {
            _vault.Granting(_group, Everything);
            _vault.Busy = "an entry dialog is open on the entry this would be written to";

            var answer = Send("PUT", "/entries/" + _entry, "{\"Title\":\"x\"}");

            Assert.That(answer.Status, Is.EqualTo(503));
            Assert.That(answer.Json, Does.Contain("busy"), "worth trying again, unlike a refusal");
        }

        [Test]
        public void A_stranger_is_told_the_same_thing_on_every_route_there_is()
        {
            foreach (var path in new[] { "/entries", "/groups", "/entries/" + _entry, "/nothing/here" })
            {
                var context = new GateContext(
                    Head("GET", path), string.Empty, ClientIdentity.Stranger("d1"));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(401), path);
            }
        }

        [Test]
        public void Every_decision_about_a_route_that_touches_the_database_is_recorded()
        {
            var audit = new RecordingAudit();
            var router = GateOperations.Router("9.9.9", null, new AccessCheck(audit));

            _vault.Granting(_group, Everything);

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

            _vault.Granting(_vault.RootAlias, Everything);
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

            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\"]}");

            var context = Context(
                "PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"chosen-by-the-caller\"}");
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404));
            Assert.That(audit.Entries.Count, Is.EqualTo(2),
                "the verb was granted and the scope was not, which is two decisions");
            Assert.That(audit.Entries[0].Permitted, Is.True, "the route was allowed");
            Assert.That(audit.Entries[1].Permitted, Is.False, "and this is the line worth having");
        }

        [Test]
        public void The_field_route_refuses_what_the_entry_route_refuses()
        {
            // The two halves of "protected" disagree whenever an operator turns a protection on,
            // because KeePass does not go back and re-flag the strings already there. While this
            // route asked only the string's own flag, the credential replacement the test above
            // blocks was reachable one path over, on the same entry, with the same rule.
            _vault.CreateField(_entry, "Password", "not-a-real-password", false, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"update\",\"create\"]}");

            Assert.That(Send("PUT", "/entries/" + _entry, "{\"Password\":\"chosen-by-the-caller\"}").Status,
                Is.EqualTo(404), "the entry route has always refused this");
            Assert.That(
                Send("PUT", "/entries/" + _entry + "/fields/Password", "{\"value\":\"chosen-by-the-caller\"}").Status,
                Is.EqualTo(404), "and the field route has to agree");
        }

        [Test]
        public void A_field_the_database_protects_is_stored_protected_however_it_was_asked_for()
        {
            // The check said `guarded || Protects`; the call passed `guarded`. So a caller holding
            // `strip` could take a standard field off and put it back unprotected, and every later
            // client with `read` and no `protected` was handed it in clear.
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\",\"create\",\"strip\",\"protected\"]}");

            Assert.That(
                Send("POST", "/entries/" + _entry + "/fields",
                     "{\"name\":\"Password\",\"value\":\"not-a-real-password\"}").Status,
                Is.EqualTo(201));

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"protected\":[\"Password\"]"),
                "the database says this name is protected, whatever the request said");

            // And saying `false` out loud is the same request: it cannot take away a protection
            // the database gives the name, so it is stored protected too and still answered 201.
            Assume.That(Send("DELETE", "/entries/" + _entry + "/fields/Password", null).Status, Is.EqualTo(200));
            Assert.That(
                Send("POST", "/entries/" + _entry + "/fields",
                     "{\"name\":\"Password\",\"value\":\"not-a-real-password\",\"protected\":\"false\"}").Status,
                Is.EqualTo(201));

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"protected\":[\"Password\"]"),
                "`false` cannot unprotect a name the database protects");
        }

        [Test]
        public void An_entry_that_goes_away_mid_request_withholds_rather_than_tells()
        {
            // The value is read, and the entry is gone by the time the rules are asked about it.
            // The lookup used to sit inside the conjunction that decides withholding, so a missing
            // entry made the answer "not withheld" and the real value went out in a 200.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");
            _vault.Vanishes = _entry;

            var answer = Get("/entries/" + _entry + "/fields/Password");

            Assert.That(answer.Json, Does.Not.Contain("not-a-real-password"),
                "a value must never be sent because the entry it belongs to could not be found");
        }

        [Test]
        public void A_value_the_database_would_drop_is_refused_rather_than_reported_as_stored()
        {
            // KeePass writes a string into a `.kdbx` through `StrUtil.SafeXmlString`, which removes
            // what XML 1.0 cannot represent. So this was accepted, answered `201`, and came back
            // from the file with the character gone: a client that meant to write one thing was
            // told it had, and had written another.
            _vault.Granting(_vault.RootAlias, Everything);

            var answer = Send("POST", "/groups/" + _group + "/entries",
                              "{\"Notes\":\"first\\u0007second\"}");

            Assert.That(answer.Status, Is.EqualTo(400), answer.Json);
        }

        [Test]
        public void Notes_may_still_be_written_the_way_people_write_paragraphs()
        {
            // The refusal is about characters a database cannot hold, not about whitespace. An
            // entry's notes are a field like any other and people put line breaks in them.
            _vault.Granting(_vault.RootAlias, Everything);

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
            _vault.Granting(_group, Everything);
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
            _vault.Granting(_group, Everything);
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

        [Test]
        public void Right_to_left_text_is_still_a_value_like_any_other()
        {
            // The refusal is about names, which KeePass acts on. A value holding right to left
            // text, the marks included, is somebody's note.
            _vault.Granting(_group, Everything);

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
            _vault.Granting(_vault.RootAlias, Everything);
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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"read\",\"list\"]}");

            var refused = Send("PUT", "/groups/" + _group, "{\"Name\":\"Renamed\"}");
            var absent = Send("PUT", "/groups/" + new string('a', 32), "{\"Name\":\"Renamed\"}");

            Assert.That(refused.Status, Is.EqualTo(404), refused.Json);
            Assert.That(absent.Status, Is.EqualTo(404), absent.Json);
            Assert.That(refused.Json, Is.EqualTo(absent.Json));
        }

        [TestCase("/groups/{0}/groups", TestName = "the groups in a group removed after the router decided")]
        [TestCase("/groups/{0}/entries", TestName = "the entries in a group removed after the router decided")]
        [TestCase("/groups/{0}", TestName = "a group removed after the router decided")]
        public void A_group_gone_after_the_router_decided_is_absent_and_never_empty(string template)
        {
            // The router decides about the group, and the operator removes it before the handler
            // reads it. A listing answered `200` and an empty list, which says the group is still
            // there and holds nothing.
            var gone = _vault.Add(_vault.RootAlias, "Removed");
            _vault.Granting(_vault.RootAlias, Everything);
            _vault.After(1, () => _vault.DeleteGroup(gone, VaultTagging.None));

            var answer = Get(string.Format(CultureInfo.InvariantCulture, template, gone));

            Assert.That(_vault.Group(gone), Is.Null, "the group was removed, which is what this is about");
            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
            Assert.That(answer.Json, Does.Contain(SpecRouter.UnreachableError));
        }

        [TestCase("/entries/{0}/fields", TestName = "the fields of an entry removed after the router decided")]
        [TestCase("/entries/{0}/attachments", TestName = "the attachments of an entry removed after the router decided")]
        [TestCase("/entries/{0}", TestName = "an entry removed after the router decided")]
        [TestCase("/entries/{0}/fields/Title", TestName = "a field of an entry removed after the router decided")]
        [TestCase("/entries/{0}/attachments/note.txt", TestName = "an attachment of an entry removed after the router decided")]
        public void An_entry_gone_after_the_router_decided_is_absent_and_never_empty(string template)
        {
            // The same race one level down. The two entry listings asked whether the entry was
            // there and then listed it, so one removed between the two was an entry with no fields.
            _vault.Granting(_vault.RootAlias, Everything);
            Assume.That(_vault.CreateAttachment(_entry, "note.txt", new byte[] { 1, 2, 3 }, VaultTagging.None).Ok, Is.True);
            _vault.After(1, () => _vault.DeleteEntry(_entry, VaultTagging.None));

            var answer = Get(string.Format(CultureInfo.InvariantCulture, template, _entry));

            Assert.That(_vault.Entry(_entry), Is.Null, "the entry was removed, which is what this is about");
            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
            Assert.That(answer.Json, Does.Contain(SpecRouter.UnreachableError));
        }

        [TestCase("/groups", TestName = "every group, in a database locked after the router decided")]
        [TestCase("/groups/{0}/groups", TestName = "a group's groups, in a database locked after the router decided")]
        [TestCase("/groups/{0}", TestName = "a group, in a database locked after the router decided")]
        [TestCase("/entries", TestName = "every entry, in a database locked after the router decided")]
        [TestCase("/groups/{0}/entries", TestName = "a group's entries, in a database locked after the router decided")]
        [TestCase("/entries/{1}", TestName = "an entry, in a database locked after the router decided")]
        [TestCase("/entries/{1}/fields", TestName = "an entry's fields, in a database locked after the router decided")]
        [TestCase("/entries/{1}/fields/Title", TestName = "a field, in a database locked after the router decided")]
        [TestCase("/entries/{1}/attachments", TestName = "an entry's attachments, in a database locked after the router decided")]
        [TestCase("/entries/{1}/attachments/note.txt", TestName = "an attachment, in a database locked after the router decided")]
        public void A_read_meeting_a_database_locked_after_the_router_decided_is_told_it_is_locked(string template)
        {
            // A node in a database that has locked has not gone, and a client told `404` would stop
            // looking for something that is still there. It is told what a locked database
            // answers before routing, in the same word.
            _vault.Granting(_vault.RootAlias, Everything);
            Assume.That(_vault.CreateAttachment(_entry, "note.txt", new byte[] { 1, 2, 3 }, VaultTagging.None).Ok, Is.True);
            _vault.After(1, _vault.Lock);

            var answer = Get(string.Format(CultureInfo.InvariantCulture, template, _group, _entry));

            Assert.That(_vault.IsOpen, Is.False, "the database locked, which is what this is about");
            Assert.That(answer.Status, Is.EqualTo(503), answer.Json);
            Assert.That(answer.Json, Is.EqualTo("{\"error\":\"locked\"}"));
        }

        [TestCase("PUT", "/groups/{0}", "{\"Name\":\"Renamed\"}", TestName = "a group renamed in a database locked after the router decided")]
        [TestCase("PUT", "/entries/{1}", "{\"Title\":\"renamed\"}", TestName = "an entry changed in a database locked after the router decided")]
        [TestCase("POST", "/groups/{0}/entries", "{\"Title\":\"invented\"}", TestName = "an entry made in a database locked after the router decided")]
        public void A_write_meeting_a_database_locked_after_the_router_decided_still_fails_on_this_side(
            string method, string template, string body)
        {
            // The one exception, which `openapi.yaml` §502 keeps: a write's handler hands the
            // change to the writer, which finds nothing open to change and answers that this side
            // failed. It could not be attempted, so a retry duplicates nothing.
            _vault.Granting(_vault.RootAlias, Everything);
            _vault.After(1, _vault.Lock);

            var answer = Send(method, string.Format(CultureInfo.InvariantCulture, template, _group, _entry), body);

            Assert.That(answer.Status, Is.EqualTo(500), answer.Json);
            Assert.That(answer.Json, Does.Contain("internal error"));
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
            _vault.Granting(_vault.RootAlias, Everything);
            _vault.Lock();

            var read = Context("GET", "/entries/" + _entry, null);
            var write = Context("PUT", "/groups/" + _group, "{\"Name\":\"Renamed\"}");
            router.Handle(read);
            router.Handle(write);

            Assert.That(read.Status, Is.EqualTo(503), read.Json);
            Assert.That(read.Json, Is.EqualTo("{\"error\":\"locked\"}"));
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
            _vault.Granting(_vault.RootAlias, Everything);

            var context = Context("GET", "/entries/" + new string('a', 32), null);
            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), context.Json);
            Assert.That(audit.Entries.Count, Is.EqualTo(1));
            Assert.That(audit.Entries[0].Permitted, Is.False);
            Assert.That(audit.Entries[0].Node, Is.Null);
        }

        [Test]
        public void Reading_one_field_leaves_the_other_values_on_the_entry_alone()
        {
            // `IVault.Read` is the entry with every string decrypted, and this route wanted nothing
            // out of it but the node to ask the rules about, which the entry carries on its own.
            // So asking for one field's value took every other value on that entry out of
            // protected memory, including the ones the same client would have been refused.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

            var answer = Get("/entries/" + _entry + "/fields/Title");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Detailed, Is.Zero,
                "one field was asked for, so one field is what the entry should have been asked for");
        }

        [Test]
        public void A_URL_KeePass_would_resolve_is_refused_wherever_it_is_written()
        {
            // The escalation a write verb carried on its own. The gate stores a value as it
            // arrives and KeePass compiles the URL before opening it, so a reference planted here
            // by a client holding `update` on one entry resolves against the whole database and
            // leaves on the operator's own double-click, past every rule and past the audit.
            _vault.Granting(_group, Everything);
            var planted = "https://invented.example/?p={REF:P@I:0123456789ABCDEF}";

            Assert.That(Send("PUT", "/entries/" + _entry, "{\"URL\":\"" + planted + "\"}").Status,
                Is.EqualTo(400));
            Assert.That(Send("POST", "/groups/" + _group + "/entries", "{\"URL\":\"" + planted + "\"}").Status,
                Is.EqualTo(400));
            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/URL", "{\"value\":\"" + planted + "\"}").Status,
                Is.EqualTo(400));
            Assert.That(Send("POST", "/entries/" + _entry + "/fields",
                "{\"name\":\"URL\",\"value\":\"" + planted + "\"}").Status, Is.EqualTo(400));

            Assert.That(_vault.Field(_entry, "URL"), Is.Null, "and none of them reached the database");
        }

        [Test]
        public void A_command_KeePass_would_run_is_refused_the_same_way()
        {
            _vault.Granting(_group, Everything);

            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/URL",
                "{\"value\":\"cmd://invented.example\"}").Status, Is.EqualTo(400));
            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/URL",
                "{\"value\":\"  CMD://invented.example\"}").Status, Is.EqualTo(400),
                "the scheme is not case sensitive and leading space does not hide it");
        }

        [Test]
        public void An_ordinary_URL_and_a_braced_note_are_both_still_written()
        {
            // The rule is the URL field and nothing else: a brace is ordinary in a note, and
            // refusing one everywhere would be this API deciding what a database may hold.
            _vault.CreateField(_entry, "URL", "https://invented.example/", false, VaultTagging.None);
            _vault.Granting(_group, Everything);

            Assert.That(Send("PUT", "/entries/" + _entry + "/fields/URL",
                "{\"value\":\"https://invented.example/a%7Bb\"}").Status, Is.EqualTo(200));
            Assert.That(Send("PUT", "/entries/" + _entry, "{\"Notes\":\"an invented {note}\"}").Status,
                Is.EqualTo(200));

            Assert.That(_vault.Field(_entry, "URL"), Is.EqualTo("https://invented.example/a%7Bb"));
            Assert.That(_vault.Field(_entry, "Notes"), Is.EqualTo("an invented {note}"));
        }

        [Test]
        public void A_client_that_may_not_write_there_learns_nothing_about_the_URL_rule()
        {
            // Asked after the rules, so the refusal a rule gives is the one that comes back.
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

            var answer = Send("PUT", "/entries/" + _entry + "/fields/URL",
                "{\"value\":\"https://invented.example/?p={REF:P@I:0123456789ABCDEF}\"}");

            Assert.That(answer.Status, Is.EqualTo(404));
        }

        [Test]
        public void A_read_says_when_an_entry_expires_and_null_when_it_does_not()
        {
            // An entry the operator expired, by a date or with Expire Now, read exactly as a live
            // one, so an agent went on using a credential KeePass had struck through. Written the
            // way `created` and `modified` are, because the client reads all three one way.
            _vault.Granting(_group, "{\"reader\":[\"read\"]}");

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"expires\":null"));

            _vault.Expire(_entry, Moment);

            Assert.That(Get("/entries/" + _entry).Json, Does.Contain("\"expires\":\"2031-02-03T04:05:06Z\""));
        }

        [Test]
        public void A_listing_does_not_say_when_an_entry_expires()
        {
            // `list` gives names, and a moment is not one, so the listing is exactly what it was.
            _vault.Expire(_entry, Moment);
            _vault.Granting(_vault.RootAlias, Everything);

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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

            var left = Asked("GET", "/entries", "false");

            Assert.That(left.Status, Is.EqualTo(200), left.Json);
            Assert.That(left.Json, Does.Contain("\"count\":0"));
        }

        [Test]
        public void A_head_takes_the_flag_its_get_takes()
        {
            // One request with the content left off, so the flag is declared on both and a head
            // asking it is not answered as a target nobody declared.
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

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
            _vault.Granting(_vault.RootAlias, "{\"reader\":[\"list\"]}");

            var head = new HttpRequestHead(
                "GET",
                "/entries",
                new Dictionary<string, string>(StringComparer.Ordinal) { { "expired", "false" } },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);
            var context = new GateContext(head, string.Empty, Paired());
            new ServedDatabase(_vault).PutOn(context);

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), context.Json);
        }

        [Test]
        public void An_update_moves_an_expiry_and_takes_one_off()
        {
            // What an agent rotating an expired password needs to finish the job: the fresh
            // password stayed marked expired, and KeePass went on refusing to auto-type it.
            _vault.Expire(_entry, new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero));
            _vault.Granting(_group, "{\"reader\":[\"update\",\"protected\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"update\"]}");

            var answer = Send("PUT", "/entries/" + _entry, "{\"UserName\":\"agent-fictitious\"}");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.EqualTo(Moment));
        }

        [Test]
        public void An_expiry_is_not_written_as_a_field()
        {
            // The body's one member that is not a string the entry holds. Read as one, it would
            // have been a custom field called `expires` beside the five.
            _vault.Granting(_group, "{\"reader\":[\"create\",\"update\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"read\",\"list\"]}");

            var refused = Send("PUT", "/entries/" + _entry, "{\"expires\":\"2031-02-03T04:05:06Z\"}");

            Assert.That(refused.Status, Is.EqualTo(404), refused.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.Null);

            _vault.Granting(_group, "{\"reader\":[\"update\",\"list\"]}");

            Assert.That(Get("/entries/" + _entry).Status, Is.EqualTo(404), "update does not read");
        }

        [Test]
        public void Changing_an_expiry_asks_nothing_of_the_protected_switch()
        {
            // When an entry expires is not a value the database protects, so a rule that may
            // update and may not touch the password still moves it.
            _vault.CreateField(_entry, "Password", "not-a-real-password", true, VaultTagging.None);
            _vault.Granting(_group, "{\"reader\":[\"update\"]}");

            var answer = Send("PUT", "/entries/" + _entry, "{\"expires\":\"2031-02-03T04:05:06Z\"}");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_vault.Entry(_entry).Expires, Is.EqualTo(Moment));
        }

        [Test]
        public void A_create_expires_when_it_is_told_to_and_never_otherwise()
        {
            _vault.NewEntryExpiresInDays = 30;
            _vault.Granting(_group, "{\"reader\":[\"create\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"create\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"update\"]}");

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
            _vault.Granting(_group, "{\"reader\":[\"create\",\"update\"]}");
            var entries = _vault.Entries().Count;
            var body = "{\"Title\":\"x\",\"expires\":\"" + expires + "\"}";

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
        /// its own, which is every shape these tests send.
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

            var context = new GateContext(new HttpRequestHead(method, path, parameters, headers, text.Length), text, Paired());
            new ServedDatabase(_vault).PutOn(context);

            _router.Handle(context);

            return new Answer(context.Status, context.Json, context.Headers);
        }

        private GateContext Context(string method, string path, string body)
        {
            var text = body ?? string.Empty;
            var context = new GateContext(Head(method, path, text.Length), text, Paired());

            new ServedDatabase(_vault).PutOn(context);
            return context;
        }

        private static HttpRequestHead Head(string method, string path)
        {
            return Head(method, path, 0);
        }

        private static HttpRequestHead Head(string method, string path, int length)
        {
            return new HttpRequestHead(
                method,
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                length);
        }

        private static ClientIdentity Paired()
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, new[] { "reader" });
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

        private sealed class RecordingAudit : IGateAudit
        {
            private readonly List<AuditEntry> _entries = new List<AuditEntry>();

            public IReadOnlyList<AuditEntry> Entries
            {
                get { return _entries; }
            }

            public void Record(AuditEntry entry)
            {
                _entries.Add(entry);
            }
        }
    }
}
