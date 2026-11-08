using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Every group and entry in one database, as somewhere rules can be read.
    /// </summary>
    /// <remarks>
    /// The live tree, not a dialog's copy. This is read from the database settings dialog, which
    /// carries no copy of any node's settings, to count what each profile is named in, and a count
    /// has to reach nodes no dialog is open on at all.
    ///
    /// **Not an entry's history.** Every item in `PwEntry.History` is a whole entry carrying the
    /// rules of the moment KeePass took the backup, and none of them is in force: the resolver
    /// walks the live tree. A count that included them would count as live a rule that decides
    /// nothing until somebody restores that revision, and restoring one is an edit like any other.
    ///
    /// **Not the recycle bin either, for the same reason.** The vault reaches nothing in it,
    /// `DESIGN.md` §138, so a rule on the bin or on anything removed into it decides nothing until
    /// the operator moves the node back out. Counted, the Profiles page offered to put such rules
    /// back in force by adding a name again, and counted them among the rules a removal leaves
    /// inert, which they were already. The bin is the one the vault hides, through the same two
    /// questions it asks.
    /// </remarks>
    public sealed class DatabaseNodes : IRuledNodes
    {
        private readonly PwDatabase _database;

        public DatabaseNodes(PwDatabase database)
        {
            _database = database;
        }

        public IEnumerable<IRuledNode> All()
        {
            var root = _database == null ? null : _database.RootGroup;
            if (root == null)
            {
                yield break;
            }

            var bin = KdbxVault.Bin(_database, root);

            if (!KdbxVault.Discarded(root, bin))
            {
                yield return new GroupNode(root);
            }

            foreach (var group in root.GetGroups(true).Where(group => !KdbxVault.Discarded(group, bin)))
            {
                yield return new GroupNode(group);
            }

            foreach (var entry in root.GetEntries(true).Where(entry => !KdbxVault.Discarded(entry, bin)))
            {
                yield return new EntryNode(entry);
            }
        }

        private sealed class GroupNode : IRuledNode
        {
            private readonly PwGroup _group;

            public GroupNode(PwGroup group)
            {
                _group = group;
            }

            public string Read()
            {
                return _group.CustomData.Get(AclDocument.CustomDataKey);
            }
        }

        private sealed class EntryNode : IRuledNode
        {
            private readonly PwEntry _entry;

            public EntryNode(PwEntry entry)
            {
                _entry = entry;
            }

            public string Read()
            {
                return _entry.CustomData.Get(AclDocument.CustomDataKey);
            }
        }
    }
}
