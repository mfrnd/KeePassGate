using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Every group and entry of a real database, as somewhere rules can be read.
    /// </summary>
    /// <remarks>
    /// Against KeePass's own types, because the whole of what this class does is walk them: the
    /// root, the groups under it however deep, and the entries in all of them. A walk that misses a
    /// branch counts a profile as naming fewer rules than it does, and the operator reading that
    /// count is deciding whether removing the profile takes anything away.
    /// </remarks>
    [TestFixture]
    public sealed class DatabaseNodesTests
    {
        [Test]
        public void Every_group_and_entry_is_reached_however_deep_it_is()
        {
            var database = Tree();

            var nodes = new DatabaseNodes(database).All().ToList();

            // The root, two groups under it, one under that, and three entries between them.
            Assert.That(nodes.Count, Is.EqualTo(7));
        }

        [Test]
        public void A_rule_is_read_from_the_node_it_is_written_on()
        {
            var database = Tree();
            var deepest = database.RootGroup.GetGroups(true).Single(group => group.Name == "deeper");
            var rule = Rules.Granting(("ops", "R"));
            deepest.CustomData.Set(AclDocument.CustomDataKey, rule);

            var found = new DatabaseNodes(database).All()
                .Count(node => node.Read() == rule);

            Assert.That(found, Is.EqualTo(1));
        }

        [Test]
        public void The_revisions_an_entry_keeps_are_not_walked()
        {
            // A revision carries the rules of the moment KeePass took it, and none of them is in
            // force: the resolver walks the live tree. Counted, a rule the entry no longer has
            // would read as one that still decides something.
            var database = Tree();
            var entry = database.RootGroup.Entries.Single();
            entry.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("ops", "R")));
            entry.CreateBackup(database);
            entry.CreateBackup(database);
            entry.CustomData.Remove(AclDocument.CustomDataKey);

            var nodes = new DatabaseNodes(database).All().ToList();

            Assert.That(nodes.Count, Is.EqualTo(7), "the same seven, however long a history is");
            Assert.That(nodes.Count(node => node.Read() != null), Is.Zero);
        }

        [Test]
        public void A_database_with_no_tree_yields_nothing_rather_than_failing()
        {
            Assert.That(new DatabaseNodes(null).All(), Is.Empty);
            Assert.That(new DatabaseNodes(new PwDatabase()).All(), Is.Empty);
        }

        /// <summary>A tree of invented content: two branches, one of them nested.</summary>
        private static PwDatabase Tree()
        {
            var database = new PwDatabase();
            database.RootGroup = new PwGroup(true, true, "root", PwIcon.Folder);

            var first = new PwGroup(true, true, "first", PwIcon.Folder);
            var deeper = new PwGroup(true, true, "deeper", PwIcon.Folder);
            var second = new PwGroup(true, true, "second", PwIcon.Folder);

            first.AddGroup(deeper, true);
            database.RootGroup.AddGroup(first, true);
            database.RootGroup.AddGroup(second, true);

            database.RootGroup.AddEntry(Entry("a secret"), true);
            first.AddEntry(Entry("another secret"), true);
            deeper.AddEntry(Entry("a third"), true);

            return database;
        }

        private static PwEntry Entry(string title)
        {
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            return entry;
        }
    }
}
