using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Store;
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
        public void The_nodes_the_tree_holds_are_the_bin_and_what_is_in_it_as_well_each_with_its_title()
        {
            // What the seal's dialog names a rule's node by, `STORE.md` §625: a rule a store lost
            // may be about a node removed into the bin, which the database holds.
            var database = Tree();
            var bin = database.RootGroup.GetGroups(true).Single(group => group.Name == "first");
            database.RecycleBinUuid = bin.Uuid;

            var held = new DatabaseNodes(database).Held(null).ToList();

            Assert.That(held.Select(node => node.Id), Is.EquivalentTo(new DatabaseNodes(database).All(null).Select(node => node.Id)
                .Concat(new[] { IdOf(bin.Uuid) })
                .Concat(bin.GetGroups(true).Select(group => IdOf(group.Uuid)))
                .Concat(bin.GetEntries(true).Select(entry => IdOf(entry.Uuid)))));
            Assert.That(held.Single(node => node.Id == IdOf(bin.Uuid)).Title, Is.EqualTo("first"));
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
        public void The_tree_a_write_above_is_carried_down_holds_the_recycle_bin_by_its_own_path_and_the_count_leaves_it_out()
        {
            // `STORE.md` §617 and §639: a write above reaches a node while it lies in the bin, by
            // the bin's place in the tree, where the Profiles page counts no rule in the bin. The
            // plugin's group stays out of both, a version standing for it among the rules.
            using (var temporary = TemporaryDatabase.New())
            {
                var database = temporary.Database;
                var store = new KdbxStore(database, GateVersion.Current, null);
                StoreSeeds.Authority(store);
                var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
                var inside = new PwGroup(true, true, "deleted", PwIcon.Folder);
                database.RootGroup.AddGroup(bin, true);
                bin.AddGroup(inside, true);
                var entry = Entry("deleted too");
                inside.AddEntry(entry, true);
                database.RecycleBinUuid = bin.Uuid;
                var plugin = database.RootGroup.Groups.Single(StoreNodes.Holds);
                StoreSeeds.TakeOff(store, IdOf(entry.Uuid), "pf", "R");
                StoreSeeds.TakeOff(store, IdOf(inside.Uuid), "pf", "R");
                TakenOffAt(store, IdOf(plugin.Uuid));

                using (var reading = store.Read())
                {
                    var tree = new DatabaseNodes(database).Tree(reading, StoredRules.Of(reading), database.RootGroup);
                    var below = tree.Below(IdOf(database.RootGroup.Uuid)).ToList();

                    Assert.That(below, Is.SupersetOf(new[] { IdOf(entry.Uuid), IdOf(inside.Uuid) }), "the nodes in the bin");
                    Assert.That(tree.Above(IdOf(entry.Uuid)), Is.EqualTo(IdOf(inside.Uuid)));
                    Assert.That(tree.Above(IdOf(inside.Uuid)), Is.EqualTo(IdOf(bin.Uuid)), "by the bin's own path");
                    Assert.That(tree.Above(IdOf(bin.Uuid)), Is.EqualTo(IdOf(database.RootGroup.Uuid)));
                    Assert.That(below, Has.No.Member(IdOf(plugin.Uuid)), "and not the plugin's group");

                    var counted = new DatabaseNodes(database).All(reading).Select(node => node.Id).ToList();
                    Assert.That(counted, Has.No.Member(IdOf(entry.Uuid)).And.No.Member(IdOf(inside.Uuid)).And.No.Member(IdOf(bin.Uuid)), "the count leaves the bin out");
                    Assert.That(counted, Has.No.Member(IdOf(plugin.Uuid)));
                }
            }
        }

        /// <summary>
        /// A rule taken off at a node of the plugin's group, composed by hand: the product writes
        /// no rule there, and a store merged in can hold such a rule.
        /// </summary>
        private static void TakenOffAt(KdbxStore store, string node)
        {
            byte[] key;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            try
            {
                var version = StoreRecord.Compose(key, StoreKinds.Version, new System.Collections.Generic.Dictionary<string, string>
                {
                    { StoreKinds.After, string.Empty },
                    { StoreKinds.Doc, "off:R" },
                    { StoreKinds.ObjectMember, StoreComposer.RowObject(node, "pf") },
                });
                var written = store.Write(new[] { new StoreAct("a rule at the plugin's group", key, StoreFormat.KeyName(key), new[] { version }, null) });
                Assume.That(written.Taken, Is.True, written.Reason);
            }
            finally
            {
                System.Array.Clear(key, 0, key.Length);
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
