using System.Linq;
using KeePassGate.Bridge;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The nodes a store's reading is handed where a deletion record is left that no object the
    /// file names explains, `STORE.md` §617 and §620: every group and entry of a real tree.
    /// </summary>
    [TestFixture]
    public sealed class StoreSourceNodesTests
    {
        [Test]
        public void The_nodes_are_the_root_and_every_group_and_entry_under_it_the_recycle_bin_among_them_walked_when_asked_for()
        {
            using (var database = TemporaryDatabase.New())
            {
                var root = database.RootGroup;
                var group = Group(root);
                var inner = Group(group);
                var bin = Group(root);
                database.Database.RecycleBinUuid = bin.Uuid;
                var entries = new[] { Entry(root), Entry(inner), Entry(bin) };

                var walk = StoreSource.Of(database.Database, root);
                var late = Entry(group);

                Assert.That(
                    walk.Nodes().Select(Hex.Of),
                    Is.EquivalentTo(new[] { root, group, inner, bin }.Select(node => node.Uuid)
                        .Concat(entries.Concat(new[] { late }).Select(entry => entry.Uuid))
                        .Select(uuid => Hex.Of(uuid.UuidBytes))),
                    "the tree as it stands when the nodes are asked for, an entry added after the walk among them");
                Assert.That(walk.Nodes().Count(), Is.EqualTo(8), "walked again at each asking, each node once");
            }
        }

        private static PwGroup Group(PwGroup parent)
        {
            var group = new PwGroup(true, true);
            parent.AddGroup(group, true);
            return group;
        }

        private static PwEntry Entry(PwGroup parent)
        {
            var entry = new PwEntry(true, true);
            parent.AddEntry(entry, true);
            return entry;
        }
    }
}
