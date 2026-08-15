using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the grant editor decides, with no window anywhere near it.
    /// </summary>
    /// <remarks>
    /// The table is the easy half. What is worth testing is what it refuses, what it shows about
    /// rules it does not own, and exactly when it writes, because the last of those is what decides
    /// whether opening a dialog marks somebody's database as modified.
    /// </remarks>
    [TestFixture]
    public sealed class GrantSheetTests
    {
        [Test]
        public void A_node_with_no_rules_shows_none_and_writes_nothing()
        {
            var sheet = new GrantSheet(null, null, null, null);

            Assert.That(sheet.Rows, Is.Empty);
            Assert.That(sheet.Document, Is.Null, "no rules is the absent key, never an empty document");
            Assert.That(sheet.Changed, Is.False);
            Assert.That(sheet.CanEdit, Is.True);
            Assert.That(sheet.Warning, Is.Null);
        }

        [Test]
        public void A_rule_typed_here_is_written_here()
        {
            var sheet = new GrantSheet(null, null, null, null);

            Assert.That(sheet.Set("reader", ".R...L.."), Is.Null);

            Assert.That(sheet.Changed, Is.True);
            Assert.That(sheet.Document, Is.EqualTo("{\"default\":\"inherit\",\"reader\":[\"read\",\"list\"]}"));
        }

        [Test]
        public void The_scope_goes_in_the_same_rule_as_the_verbs()
        {
            var sheet = new GrantSheet(null, null, null, null);

            Assert.That(sheet.Set("reader", ".R...LA."), Is.Null);

            Assert.That(sheet.Document, Does.Contain("\"attachments\""));
            Assert.That(AclDocument.Read(sheet.Document).Grant("reader").CoversAttachments, Is.True);
        }

        [Test]
        public void An_existing_rule_reads_back_as_the_mask_it_is()
        {
            var sheet = new GrantSheet(
                "{\"default\":\"deny\",\"editor\":[\"read\",\"update\",\"attachments\"]}", null, null, null);

            var row = sheet.Rows.Single();

            Assert.That(row.Profile, Is.EqualTo("editor"));
            Assert.That(row.Mask, Is.EqualTo(".RU...A."), "read and update, over attachments as well");
            Assert.That(row.Local, Is.True);
            Assert.That(sheet.Default, Is.EqualTo(AclDefault.Deny));
        }

        [Test]
        public void Reading_a_node_and_writing_it_back_unchanged_is_not_a_change()
        {
            // The whole reason this is asked at all: `StringDictionaryEx.Set` stamps a fresh
            // modification time even for an identical value, which turns "look at the tab and press
            // OK" into an unsaved database.
            var stored = "{\"default\":\"inherit\",\"reader\":[\"read\"]}";

            Assert.That(new GrantSheet(stored, null, null, null).Changed, Is.False);
        }

        [Test]
        public void A_document_that_differs_only_in_spelling_is_not_rewritten_for_being_read()
        {
            var byHand = "{ \"reader\" : [ \"list\", \"read\" ] , \"default\":\"inherit\" }";

            Assert.That(new GrantSheet(byHand, null, null, null).Changed, Is.False);
        }

        [Test]
        public void Removing_the_last_rule_takes_the_key_away_rather_than_leaving_an_empty_one()
        {
            var sheet = new GrantSheet("{\"default\":\"inherit\",\"reader\":[\"read\"]}", null, null, null);

            Assert.That(sheet.Remove("reader"), Is.True);

            Assert.That(sheet.Document, Is.Null);
            Assert.That(sheet.Changed, Is.True);
        }

        [Test]
        public void A_node_that_refuses_what_it_does_not_name_is_still_saying_something()
        {
            // So it keeps a document even with no rules in it, which is the one exception to the
            // absent key.
            var sheet = new GrantSheet(null, null, null, null);
            sheet.Default = AclDefault.Deny;

            Assert.That(sheet.Document, Is.EqualTo("{\"default\":\"deny\"}"));
        }

        [Test]
        public void Granting_nothing_is_not_the_same_as_removing_a_rule()
        {
            // The distinction the whole model turns on. An empty rule takes a grant away from here
            // down; removing puts back whatever is inherited.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var sheet = new GrantSheet(null, above, null, null);

            Assert.That(sheet.Set("reader", "........"), Is.Null);
            Assert.That(sheet.Document, Is.EqualTo("{\"default\":\"inherit\",\"reader\":[]}"));

            Assert.That(sheet.Remove("reader"), Is.True);
            Assert.That(sheet.Document, Is.Null);
        }

        [Test]
        public void What_a_group_above_grants_is_shown_beside_what_this_one_does()
        {
            // A table listing only local rules reports an empty list for an entry that a group
            // grant already opens wide, and an operator reading that is being invited to grant more.
            var above = TestNode.Root("root")
                .Granting("reader", AclVerb.Read)
                .Child("group").Granting("editor", AclVerb.Read, AclVerb.Update);

            var sheet = new GrantSheet("{\"auditor\":[\"list\"]}", above, null, null);

            var rows = sheet.Rows;

            Assert.That(rows.Select(r => r.Profile), Is.EqualTo(new[] { "auditor", "editor", "reader" }),
                "sorted together, because a reader compares profiles with each other");
            Assert.That(rows.Single(r => r.Profile == "auditor").Local, Is.True);
            Assert.That(rows.Single(r => r.Profile == "reader").Local, Is.False);
            Assert.That(rows.Single(r => r.Profile == "reader").Mask, Is.EqualTo(".R......"));
        }

        [Test]
        public void A_local_rule_says_what_it_is_narrowing()
        {
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read, AclVerb.Update);
            var sheet = new GrantSheet("{\"reader\":[\"read\"]}", above, null, null);

            var row = sheet.Rows.Single();

            Assert.That(row.Local, Is.True);
            Assert.That(row.Mask, Is.EqualTo(".R......"));
            Assert.That(row.InheritedMask, Is.EqualTo(".RU....."), "so narrowing is visibly narrowing");
        }

        [Test]
        public void A_profile_nobody_has_admitted_can_still_be_written_about()
        {
            // A rule naming a profile no client holds grants nobody anything, which is a rule
            // waiting rather than a rule that is wrong.
            var sheet = new GrantSheet(null, null, new[] { "reader" }, null);

            Assert.That(sheet.Offered, Is.EqualTo(new[] { "reader" }));
            Assert.That(sheet.Set("not-admitted-yet", ".R......"), Is.Null);
        }

        [Test]
        public void The_pick_list_is_sorted_and_holds_no_name_a_certificate_could_not_carry()
        {
            var sheet = new GrantSheet(null, null, new[] { "zulu", "alpha", "not a profile", "", "alpha" }, null);

            Assert.That(sheet.Offered, Is.EqualTo(new[] { "alpha", "zulu" }));
        }

        [TestCase("", ".R......", "Name the profile")]
        [TestCase("   ", ".R......", "Name the profile")]
        [TestCase("default", ".R......", "does not name")]
        [TestCase("Default", ".R......", "does not name")]
        [TestCase("has a space", ".R......", "letters, digits")]
        [TestCase("reader", "read", "CRUSDLAP")]
        [TestCase("reader", "R......R", "CRUSDLAP")]
        [TestCase("reader", "", "CRUSDLAP")]
        public void A_rule_that_is_not_one_is_refused_and_says_why(string profile, string mask, string because)
        {
            var sheet = new GrantSheet(null, null, null, null);

            var complaint = sheet.Set(profile, mask);

            Assert.That(complaint, Is.Not.Null);
            Assert.That(complaint, Does.Contain(because));
            Assert.That(sheet.Document, Is.Null, "and nothing was written");
        }

        [Test]
        public void Choosing_a_rule_on_a_name_with_spaces_round_it_does_not_write_a_second_one()
        {
            // A document written by hand can hold a name no certificate can carry, and the row
            // hands that name back exactly. Trimmed, it would become a live rule beside the inert one.
            var stored = "{\" reader \":[\"read\"]}";
            var sheet = new GrantSheet(stored, null, new[] { "reader" }, null);

            Assert.That(sheet.Set(" reader ", ".R...L.."), Does.Contain("letters, digits"));
            Assert.That(sheet.Owns("reader"), Is.False);
            Assert.That(sheet.Changed, Is.False);
        }

        [Test]
        public void A_document_here_that_will_not_read_stops_editing_rather_than_being_overwritten()
        {
            var broken = "{\"reader\":[\"purge\"]}";
            var sheet = new GrantSheet(broken, null, null, null);

            Assert.That(sheet.Unreadable, Is.True);
            Assert.That(sheet.CanEdit, Is.False);
            Assert.That(sheet.Warning, Does.Contain("cannot be read"));
            Assert.That(sheet.Set("reader", ".R......"), Is.Not.Null);

            Assert.That(sheet.Document, Is.EqualTo(broken), "untouched, because nothing here understood it");
            Assert.That(sheet.Changed, Is.False);
        }

        [Test]
        public void An_inherited_rule_is_drawn_refused_under_a_document_here_that_will_not_read()
        {
            // The resolver refuses every profile at a node whose own document will not read, so
            // what a group above grants stops here exactly as it does under a seal.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var broken = "{\"reader\":[\"purge\"]}";
            var sheet = new GrantSheet(broken, above, null, null);

            Assert.That(AclResolver.Permits(
                above.Child("here").Holding(broken), new[] { "reader" }, AclVerb.Read), Is.False);
            Assert.That(sheet.Rows, Has.Count.EqualTo(1));
            Assert.That(sheet.Rows[0].Reaches, Is.False);

            sheet.DiscardUnreadable();

            Assert.That(sheet.Rows[0].Reaches, Is.True, "and arrives again once it is discarded");
        }

        [Test]
        public void Discarding_an_unreadable_document_is_a_decision_of_its_own()
        {
            var sheet = new GrantSheet("{\"reader\":[\"purge\"]}", null, null, null);

            sheet.DiscardUnreadable();

            Assert.That(sheet.Unreadable, Is.False);
            Assert.That(sheet.CanEdit, Is.True);
            Assert.That(sheet.Document, Is.Null);
            Assert.That(sheet.Changed, Is.True);
        }

        [Test]
        public void A_chain_above_that_will_not_read_is_said_out_loud()
        {
            var above = TestNode.Root("root").Holding("{\"reader\":[\"purge\"]}");
            var sheet = new GrantSheet(null, above, null, null);

            Assert.That(sheet.Warning, Does.Contain("above"));
            Assert.That(sheet.CanEdit, Is.True, "this node's own rules are still worth writing");
        }

        [Test]
        public void A_rule_between_this_and_a_chain_that_will_not_read_is_still_shown_in_force()
        {
            // The resolver stops at the nearer rule and never reads the broken one, so the table
            // has to carry it as inherited rather than offering the profile as though it were free.
            var root = TestNode.Root("root").Holding("{\"reader\":[\"purge\"]}");
            var above = root.Child("middle").Granting("reader", AclVerb.Read);
            var sheet = new GrantSheet(null, above, new[] { "reader", "editor" }, null);

            Assert.That(sheet.Rows, Has.Count.EqualTo(1));
            Assert.That(sheet.Rows[0].Profile, Is.EqualTo("reader"));
            Assert.That(sheet.Rows[0].Local, Is.False);
            Assert.That(sheet.Available, Is.EqualTo(new[] { "editor" }));
            Assert.That(sheet.Warning, Does.Contain("above"));
            Assert.That(sheet.Warning, Does.Not.Contain("whatever this tab says"));
        }

        [Test]
        public void An_edit_that_could_not_be_kept_is_refused_before_it_is_made()
        {
            // What the editor does when it cannot reach the dialog's own copy of the custom data.
            // The rules stay readable, which is the point of opening the tab.
            var sheet = new GrantSheet(
                "{\"reader\":[\"read\"]}", null, null, "This KeePass keeps group settings somewhere this cannot reach.");

            Assert.That(sheet.CanEdit, Is.False);
            Assert.That(sheet.Warning, Does.Contain("cannot reach"));
            Assert.That(sheet.Rows, Is.Not.Empty);
            Assert.That(sheet.Set("editor", ".R......"), Is.Not.Null);
            Assert.That(sheet.Remove("reader"), Is.False);
        }

        [Test]
        public void The_warning_worth_reading_is_the_one_that_costs_most_to_miss()
        {
            var sheet = new GrantSheet("{\"reader\":[\"purge\"]}", null, null, "Nothing typed here can be kept.");

            Assert.That(sheet.Warning, Is.EqualTo("Nothing typed here can be kept."));
        }

        [Test]
        public void Sealing_a_node_stops_the_table_showing_what_it_now_refuses()
        {
            // The table is what an operator reads to know who can reach this node. Sealing it
            // makes the resolver refuse every profile the node does not name, inherited ones
            // included. A row still shown in force is therefore a permission nobody has, printed
            // beside the seal's own text saying it has stopped reaching.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var sheet = new GrantSheet(null, above, null, null);

            Assert.That(sheet.Rows[0].Reaches, Is.True, "it arrives while the node is open");

            sheet.Default = AclDefault.Deny;

            Assert.That(sheet.Rows[0].Profile, Is.EqualTo("reader"),
                "still listed, because an operator has to see what the seal is refusing");
            Assert.That(sheet.Rows[0].Reaches, Is.False);
        }

        [Test]
        public void A_local_rule_still_reaches_under_a_seal_because_the_node_names_it()
        {
            // The other half: `deny` refuses what this node does not name, not what it does.
            var sheet = new GrantSheet(null, null, null, null);
            sheet.Default = AclDefault.Deny;
            sheet.Set("reader", VerbMask.Of(new[] { AclVerb.Read }));

            Assert.That(sheet.Rows[0].Reaches, Is.True);
        }

        [Test]
        public void One_rule_past_what_the_reader_takes_is_refused_rather_than_written()
        {
            // The document carries `default` beside the rules, so the reader's ceiling is one
            // fewer rules than it looks. Written past it, the whole document stops reading on
            // the next open. That is not a lost rule but every rule on this node refused,
            // and every node under it with it.
            var sheet = new GrantSheet(null, null, null, null);

            for (var i = 0; i < GrantSheet.Most; i++)
            {
                Assert.That(sheet.Set("profile" + i, "CRUSDLAP"), Is.Null, "rule " + i);
            }

            Assert.That(sheet.Set("one-too-many", "CRUSDLAP"), Does.Contain("one more"));
            Assert.That(sheet.Rows.Count, Is.EqualTo(GrantSheet.Most));

            // What it wrote is still readable, which is the whole point of refusing.
            Assert.That(AclDocument.Read(sheet.Document), Is.Not.Null);
        }

        [Test]
        public void Narrowing_a_rule_that_is_already_here_is_not_one_more_of_them()
        {
            var sheet = new GrantSheet(null, null, null, null);
            for (var i = 0; i < GrantSheet.Most; i++)
            {
                sheet.Set("profile" + i, "CRUSDLAP");
            }

            Assert.That(sheet.Set("profile0", ".R......"), Is.Null,
                "the ceiling counts rules, and this replaces one");
        }

        [Test]
        public void A_node_written_by_hand_past_the_ceiling_is_not_offered_for_editing()
        {
            // 32 profiles and no default fit the reader's members, and the editor always writes
            // the default. Opened as editable, any change to one rule would store a document the
            // reader refuses, so it opens the way every document the reader refuses does.
            var names = Enumerable.Range(0, GrantSheet.Most + 1).Select(i => "\"profile" + i + "\":[\"read\"]");
            var sheet = new GrantSheet("{" + string.Join(",", names) + "}", null, null, null);

            Assert.That(sheet.Unreadable, Is.True);
            Assert.That(sheet.CanEdit, Is.False);
            Assert.That(sheet.Set("profile0", ".R......"), Is.Not.Null);
        }

        [Test]
        public void A_profile_with_a_rule_here_is_not_offered_a_second_one()
        {
            // One row per profile is the whole shape of the table. A pick list offering a name that
            // is already on it is offering to write a rule over one the operator can see.
            var sheet = new GrantSheet(
                "{\"reader\":[\"read\"]}", null, new[] { "reader", "editor" }, null);

            Assert.That(sheet.Available, Is.EqualTo(new[] { "editor" }));
        }

        [Test]
        public void A_profile_a_group_above_grants_is_not_offered_either()
        {
            // It is already a row, in gray, and the way to change it is to choose a rule on that
            // row: the row stops being inherited and becomes this node's own. A second row for the
            // same profile is a thing this format cannot store and a table cannot show.
            var above = TestNode.Root("root").Granting("editor", AclVerb.Read);
            var sheet = new GrantSheet(null, above, new[] { "reader", "editor" }, null);

            Assert.That(sheet.Available, Is.EqualTo(new[] { "reader" }));
        }

        [Test]
        public void A_profile_comes_back_when_its_rule_is_taken_off()
        {
            var sheet = new GrantSheet("{\"reader\":[\"read\"]}", null, new[] { "reader" }, null);

            Assert.That(sheet.Available, Is.Empty);

            sheet.Remove("reader");

            Assert.That(sheet.Available, Is.EqualTo(new[] { "reader" }));
        }
    }
}
