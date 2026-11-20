using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Every group and entry in one database a rule can be in force at.
    /// </summary>
    /// <remarks>
    /// The live tree, read from the database settings dialog, to count the nodes at which each
    /// profile's rule stands, and a count has to reach nodes no dialog is open on at all.
    ///
    /// **Not an entry's history.** Every item in `PwEntry.History` shares the entry's identifier
    /// and is no node of its own: the resolver walks the live tree.
    ///
    /// **Not the recycle bin either.** The vault reaches nothing in it, `DESIGN.md` §138, so a
    /// rule for the bin or for anything removed into it decides nothing until the operator moves
    /// the node back out. Counted, the Profiles page offered to put such rules back in force by
    /// adding a name again, and counted them among the rules a removal leaves inert, which they
    /// were already. The bin is the bin the vault hides, through the same two questions it asks.
    ///
    /// **Nor the plugin's store**, which the vault hides as it hides the bin, `STORE.md` §633: a
    /// rule naming a group of the store or a record of it reaches no client. Its groups are told by
    /// their identifiers, and its entries as the reading handed over tells them, `StoreNodes`.
    /// </remarks>
    public sealed class DatabaseNodes : IRuledNodes
    {
        private readonly PwDatabase _database;

        public DatabaseNodes(PwDatabase database)
        {
            _database = database;
        }

        public IEnumerable<IRuledNode> All(StoreReading reading)
        {
            var root = _database == null ? null : _database.RootGroup;
            if (root == null)
            {
                yield break;
            }

            var bin = KdbxVault.Bin(_database, root);
            var store = reading == null ? null : StoreNodes.Of(reading);

            if (!Left(root, bin))
            {
                yield return new Node(root.Uuid);
            }

            foreach (var group in root.GetGroups(true).Where(group => !Left(group, bin)))
            {
                yield return new Node(group.Uuid);
            }

            foreach (var entry in root.GetEntries(true).Where(entry => !Left(entry.ParentGroup, bin)
                                                                       && (store == null || !store.Holds(entry))))
            {
                yield return new Node(entry.Uuid);
            }
        }

        /// <summary>Whether a group is the recycle bin, a group of the store, or inside either.</summary>
        private static bool Left(PwGroup group, PwGroup bin)
        {
            return KdbxVault.Discarded(group, bin) || StoreNodes.Holds(group);
        }

        private sealed class Node : IRuledNode
        {
            public Node(PwUuid uuid)
            {
                Id = Hex.Of(uuid.UuidBytes);
            }

            public string Id { get; private set; }
        }
    }
}
