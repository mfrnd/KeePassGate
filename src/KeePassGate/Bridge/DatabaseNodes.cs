using System.Collections.Generic;
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
            if (_database == null || _database.RootGroup == null)
            {
                yield break;
            }

            yield return new GroupNode(_database.RootGroup);

            foreach (var group in _database.RootGroup.GetGroups(true))
            {
                yield return new GroupNode(group);
            }

            foreach (var entry in _database.RootGroup.GetEntries(true))
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
