using System;
using KeePassGate.Acl;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// A group or an entry in a real database, as the resolver sees it.
    /// </summary>
    /// <remarks>
    /// The whole of what KeePass has to lend the access control model: what a node holds under this
    /// plugin's key, and what is above it. Everything else about a `PwGroup` or a `PwEntry` stays on
    /// the other side of the seam, which is why the rules can be tested without a password manager.
    ///
    /// **Read on the connection's own thread, without marshaling**, like everything else that only
    /// reads. `CustomData` is a string dictionary and nothing here mutates one.
    ///
    /// **And read as the resolver walks, with no lock and no copy, which costs nothing.** Both
    /// properties below are evaluated by `AclResolver` rather than when the node is made, so they
    /// are read while another thread may be changing the tree and the rules. What a tree in motion
    /// breaks is an enumerator or a copy, and a walk is neither. Per level it reads `ParentGroup`
    /// and `CustomData`, which compile to plain field reads, and calls `StringDictionaryEx.Get`,
    /// which on KeePass 2.61.1 is one `TryGetValue` against a `SortedDictionary`: a descent with no
    /// version check and no array indexing. `AclInRealDatabaseTests` measures it against a tree and
    /// a set of rules that are being rewritten by another thread throughout.
    ///
    /// **What a tree in motion costs a walk is a stale answer, and no retry addresses that.** A
    /// lookup racing a rebalance misses a key rather than throwing, and a retry only ever runs
    /// again something that threw. `DESIGN.md` §124 accepts that race and says what the
    /// alternative costs. KeePass's own dialogs are not where it comes from: `AssignProperties`
    /// replaces the whole dictionary with a deep clone, on a group and on an entry alike, so a
    /// reader holds one complete dictionary or the other.
    ///
    /// **An entry's parent is its group, and a group's is the group above it.** So a rule written on
    /// an entry is nearest, then its group, then that group's parent, up to the root. That is the
    /// order the resolver walks, and it is the order a person reading the tree would expect.
    /// </remarks>
    public static class KdbxNodes
    {
        /// <summary>The resolver's view of an entry.</summary>
        public static IAclNode Of(PwEntry entry)
        {
            return entry == null ? null : new EntryNode(entry);
        }

        /// <summary>The resolver's view of a group.</summary>
        public static IAclNode Of(PwGroup group)
        {
            return group == null ? null : new GroupNode(group);
        }

        private static string DocumentOf(PwEntry entry)
        {
            return entry.CustomData == null ? null : entry.CustomData.Get(AclDocument.CustomDataKey);
        }

        private static string DocumentOf(PwGroup group)
        {
            return group.CustomData == null ? null : group.CustomData.Get(AclDocument.CustomDataKey);
        }

        /// <summary>
        /// What a refusal calls a node.
        /// </summary>
        /// <remarks>
        /// The UUID, never the name. A group is called "Banking" and an entry is called something
        /// worse, and both end up in a log that is meant to be readable by somebody who is not
        /// entitled to know what is in the database.
        /// </remarks>
        private static string NameOf(PwUuid uuid)
        {
            return uuid == null ? string.Empty : uuid.ToHexString();
        }

        private sealed class EntryNode : IAclNode
        {
            private readonly PwEntry _entry;

            public EntryNode(PwEntry entry)
            {
                _entry = entry;
            }

            public string Document
            {
                get { return DocumentOf(_entry); }
            }

            public IAclNode Parent
            {
                get { return Of(_entry.ParentGroup); }
            }

            public string Name
            {
                get { return NameOf(_entry.Uuid); }
            }
        }

        private sealed class GroupNode : IAclNode
        {
            private readonly PwGroup _group;

            public GroupNode(PwGroup group)
            {
                _group = group;
            }

            public string Document
            {
                get { return DocumentOf(_group); }
            }

            public IAclNode Parent
            {
                get { return Of(_group.ParentGroup); }
            }

            public string Name
            {
                get { return NameOf(_group.Uuid); }
            }
        }
    }
}
