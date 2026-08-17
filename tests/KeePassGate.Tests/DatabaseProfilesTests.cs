using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The profiles page against a database: the list in its custom data, and the rules everywhere.
    /// </summary>
    /// <remarks>
    /// The symmetry is the thing worth pinning. Adding a name and removing one each touch one
    /// value, the list, and neither writes a rule: a client holds only the names the list still
    /// names, so the list is what decides whether the rules for a name are in force.
    /// </remarks>
    [TestFixture]
    public sealed class DatabaseProfilesTests
    {
        [Test]
        public void A_profile_added_is_written_where_the_database_keeps_them()
        {
            var database = new FakeGateDatabase();
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Add("ops"), Is.Null);

            Assert.That(profiles.Names(), Is.EqualTo(new[] { "ops" }));
            Assert.That(
                ProfileNames.Read(database.PluginCustomData.Get(ProfileNames.CustomDataKey)).Holds("ops"),
                Is.True);
            Assert.That(database.MarkedModifiedCount, Is.EqualTo(1), "or KeePass has nothing to save");
        }

        [Test]
        public void Adding_the_same_name_twice_says_so_rather_than_marking_the_database_again()
        {
            var database = new FakeGateDatabase();
            var profiles = new DatabaseProfiles(database, null);

            profiles.Add("ops");

            Assert.That(profiles.Add("ops"), Does.Contain("already named"));
            Assert.That(database.MarkedModifiedCount, Is.EqualTo(1), "the second one wrote nothing");
        }

        [Test]
        public void Removing_a_profile_leaves_its_rules_where_they_are_and_says_so()
        {
            // The list is the only thing removal touches. A client holds only the names the list
            // still names, so the rules are inert without being rewritten, and adding the name
            // back is a full undo rather than a fresh start.
            var group = new TestRuledNode("a group", "{\"ops\":[\"read\"]}");
            var entry = new TestRuledNode("an entry", "{\"ops\":[\"read\"],\"reader\":[\"list\"]}");
            var database = new FakeGateDatabase
            {
                Nodes = new TestRuledNodes(group, entry, new TestRuledNode("another", "{\"reader\":[\"list\"]}"))
            };

            var profiles = new DatabaseProfiles(database, null);
            profiles.Add("ops");

            Assert.That(profiles.Remove("ops"), Is.Null);

            Assert.That(profiles.Names(), Is.Empty);
            Assert.That(profiles.LastRemoval, Does.Contain("grants nothing").And.Contain("2 rules naming it stay"));
            Assert.That(group.Current, Is.EqualTo("{\"ops\":[\"read\"]}"), "untouched");
            Assert.That(entry.Current, Is.EqualTo("{\"ops\":[\"read\"],\"reader\":[\"list\"]}"));
            Assert.That(database.MarkedModifiedCount, Is.EqualTo(2), "the add and the removal, each one write");
        }

        [Test]
        public void A_removal_no_rule_named_says_only_what_the_name_does_now()
        {
            // Because "gone" and "gone, with rules waiting for it" are different pieces of news.
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);
            profiles.Add("ops");

            Assert.That(profiles.Remove("ops"), Is.Null);

            Assert.That(profiles.LastRemoval, Is.EqualTo("The profile ops is gone, so it grants nothing."));
        }

        [Test]
        public void A_profile_that_was_never_named_cannot_be_removed()
        {
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Remove("ops"), Does.Contain("not named here"));
            Assert.That(profiles.LastRemoval, Is.Null);
        }

        [Test]
        public void Adding_a_removed_name_back_says_how_many_rules_come_back_and_restores_them()
        {
            // A full undo: nothing was rewritten, so the rules that were waiting are the rules in
            // force the moment the name is listed again.
            var group = new TestRuledNode("a group", "{\"ops\":[\"read\"]}");
            var entry = new TestRuledNode("an entry", "{\"ops\":[],\"reader\":[\"list\"]}");
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes(group, entry) };
            var profiles = new DatabaseProfiles(database, null);

            profiles.Add("ops");
            Assert.That(profiles.Returning("ops"), Is.Null, "a listed name has nothing to bring back");

            profiles.Remove("ops");
            Assert.That(profiles.Returning("ops"),
                Is.EqualTo("Adding ops again puts the 2 rules naming it back in force."));

            Assert.That(profiles.Add("ops"), Is.Null);
            Assert.That(profiles.Names(), Is.EqualTo(new[] { "ops" }));
            Assert.That(group.Current, Is.EqualTo("{\"ops\":[\"read\"]}"));
            Assert.That(entry.Current, Is.EqualTo("{\"ops\":[],\"reader\":[\"list\"]}"));
        }

        [Test]
        public void A_name_no_rule_names_comes_back_without_a_warning()
        {
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Returning("ops"), Is.Null);
            Assert.That(new DatabaseProfiles(new FakeGateDatabase { IsOpen = false }, null).Returning("ops"), Is.Null);
        }

        [Test]
        public void The_rules_are_counted_rather_than_remembered()
        {
            // They are edited on other dialogs entirely, so a number kept here would be stale the
            // moment somebody pressed OK on one of them.
            var node = new TestRuledNode("a group", null);
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes(node) };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(Counted(profiles.Rules(), "ops"), Is.Zero);

            node.Current = "{\"ops\":[\"read\"]}";

            Assert.That(Counted(profiles.Rules(), "ops"), Is.EqualTo(1));
        }

        [Test]
        public void Rules_nobody_can_read_are_not_counted()
        {
            var node = new TestRuledNode("a group", "{this will not read");
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes(node) };
            var profiles = new DatabaseProfiles(database, null);
            profiles.Add("ops");

            Assert.That(Counted(profiles.Rules(), "ops"), Is.Zero);
            Assert.That(profiles.Remove("ops"), Is.Null);
            Assert.That(profiles.LastRemoval, Is.EqualTo("The profile ops is gone, so it grants nothing."));
        }

        [Test]
        public void Every_profile_is_counted_in_one_walk()
        {
            // The walk is the cost, over every group and entry, and the page asks for a count on
            // every row.
            var tree = new TestRuledNodes(
                new TestRuledNode("a group", "{\"ops\":[\"read\"],\"reader\":[\"list\"]}"),
                new TestRuledNode("an entry", "{\"ops\":[]}"),
                new TestRuledNode("another", null));
            var profiles = new DatabaseProfiles(new FakeGateDatabase { Nodes = tree }, null);

            var counts = profiles.Rules();

            Assert.That(tree.Walks, Is.EqualTo(1));
            Assert.That(Counted(counts, "ops"), Is.EqualTo(2), "a rule granting nothing still names it");
            Assert.That(Counted(counts, "reader"), Is.EqualTo(1));
            Assert.That(counts.ContainsKey("nobody"), Is.False);
        }

        /// <summary>One profile's count, which is none when the count does not name it.</summary>
        private static int Counted(IReadOnlyDictionary<string, int> counts, string name)
        {
            int found;
            return counts.TryGetValue(name, out found) ? found : 0;
        }

        [Test]
        public void A_closed_database_answers_rather_than_throwing()
        {
            var database = new FakeGateDatabase { IsOpen = false };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Add("ops"), Does.Contain("No database"));
            Assert.That(profiles.Remove("ops"), Does.Contain("No database"));
            Assert.That(Counted(profiles.Rules(), "ops"), Is.Zero);
        }
    }
}
