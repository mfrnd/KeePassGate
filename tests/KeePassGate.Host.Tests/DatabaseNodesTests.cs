using System.Linq;
using KeePassGate.Bridge;
using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Every group and entry of a real database a rule can be in force at, each known by its
    /// identifier.
    /// </summary>
    /// <remarks>
    /// Against KeePass's own types, because the whole of what this class does is walk them: the
    /// root, the groups under it however deep, and the entries in all of them, less the recycle
    /// bin and the plugin's store. A walk that misses a branch counts a profile as naming fewer
    /// rules than it does, and the operator reading that count is deciding whether removing the
    /// profile takes anything away.
    /// </remarks>
    [TestFixture]
    public sealed class DatabaseNodesTests
    {
        [Test]
        public void Every_group_and_entry_is_reached_however_deep_it_is_by_its_identifier()
        {
            var database = Tree();

            var nodes = new DatabaseNodes(database).All(null).Select(node => node.Id).ToList();

            // The root, two groups under it, one under that, and three entries between them, each
            // by the 32 lowercase hexadecimal characters a rule names it by.
            var expected = new[] { database.RootGroup.Uuid }
                .Concat(database.RootGroup.GetGroups(true).Select(group => group.Uuid))
                .Concat(database.RootGroup.GetEntries(true).Select(entry => entry.Uuid))
                .Select(IdOf);
            Assert.That(nodes, Is.EquivalentTo(expected));
            Assert.That(nodes, Has.Count.EqualTo(7));
            Assert.That(nodes, Has.All.Match("^[0-9a-f]{32}$"));
        }

        [Test]
        public void The_revisions_an_entry_keeps_are_not_walked()
        {
            // A revision shares its entry's identifier and is no node of its own: the resolver
            // walks the live tree.
            var database = Tree();
            var entry = database.RootGroup.Entries.Single();
            entry.CreateBackup(database);
            entry.CreateBackup(database);

            var nodes = new DatabaseNodes(database).All(null).ToList();

            Assert.That(nodes.Count, Is.EqualTo(7), "the same seven, however long a history is");
            Assert.That(nodes.Select(node => node.Id).Distinct().Count(), Is.EqualTo(7));
        }

        [Test]
        public void Nothing_in_the_recycle_bin_is_walked()
        {
            // The vault reaches nothing in the bin, so its rules decide nothing, and counted they
            // were offered back "in force" when the name was added again.
            var database = Tree();
            var bin = database.RootGroup.GetGroups(true).Single(group => group.Name == "first");
            database.RecycleBinUuid = bin.Uuid;

            var nodes = new DatabaseNodes(database).All(null).Select(node => node.Id).ToList();

            // The root, the second group and the one entry under the root: the bin, the group
            // inside it and their two entries are left out.
            Assert.That(nodes.Count, Is.EqualTo(3));
            Assert.That(nodes, Does.Not.Contain(IdOf(bin.Uuid)));
        }

        [Test]
        public void Nothing_of_the_plugin_s_store_is_walked_wherever_its_entries_sit()
        {
            // The vault hides the store as it hides the bin, `STORE.md` §633: its group and the
            // records inside it by the group's identifier, and a record moved out of the group by
            // what the reading tells of it.
            using (var temporary = TemporaryDatabase.New())
            {
                var database = temporary.Database;
                var store = new KdbxStore(database, GateVersion.Current, null);
                StoreSeeds.Authority(store);
                var plugin = database.RootGroup.Groups.Single(StoreNodes.Holds);
                var moved = plugin.Entries.First();
                Assert.That(new DatabaseNodes(database).All(null).Select(node => node.Id), Does.Not.Contain(IdOf(plugin.Uuid)),
                    "the group by its identifier alone");

                plugin.Entries.Remove(moved);
                database.RootGroup.AddEntry(moved, true);

                using (var reading = store.Read())
                {
                    var nodes = new DatabaseNodes(database).All(reading).Select(node => node.Id).ToList();

                    Assert.That(nodes, Does.Not.Contain(IdOf(plugin.Uuid)));
                    Assert.That(nodes, Does.Not.Contain(IdOf(moved.Uuid)), "a record wherever it sits");
                    Assert.That(nodes, Does.Contain(IdOf(database.RootGroup.Uuid)));
                }
            }
        }

        [Test]
        public void A_database_with_no_tree_yields_nothing_rather_than_failing()
        {
            Assert.That(new DatabaseNodes(null).All(null), Is.Empty);
            Assert.That(new DatabaseNodes(new PwDatabase()).All(null), Is.Empty);
        }

        /// <summary>A node's identifier as a rule names it.</summary>
        private static string IdOf(PwUuid uuid)
        {
            return Hex.Of(uuid.UuidBytes);
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
