using System;
using KeePassGate.Acl;
using KeePassGate.Ui;
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
    /// **Read on the connection's own thread, with KeePass's thread held still for the whole
    /// walk.** A node made by `KdbxVault` carries the vault's hold, and `AclResolver` runs each walk
    /// inside <see cref="IAclNode.Held"/>, so a walk is one read step however many levels it
    /// climbs. It used to run with nothing held, on the argument that it raises nothing a retry
    /// could take again, and that was true and beside the point: a lookup racing a rebalance
    /// misses a key rather than throwing, so a tree in motion cost a walk a wrong answer that
    /// nothing saw.
    /// A node made on KeePass's own thread, for the editor, has nothing to hold and walks where it
    /// is.
    ///
    /// **A tree standing still answers wrongly too, unless the walk runs in KeePass's culture.**
    /// The descent compares by the default comparer, which collates in the culture of the thread
    /// asking, and a node's dictionary was sorted on KeePass's thread in KeePass's. A key another
    /// program keeps beside `KPG` that orders differently against it in the two cultures can lie
    /// on the way down, and the walk then finds no rule where there is one and goes on to the
    /// parent's: a seal on an entry under a Lithuanian KeePass on an English format let the
    /// group's grant through. `GateListener` serves every connection in KeePass's culture, which
    /// is what makes the descent the one KeePass would make.
    ///
    /// **An entry's parent is its group, and a group's is the group above it.** So a rule written on
    /// an entry is nearest, then its group, then that group's parent, up to the root. That is the
    /// order the resolver walks, and it is the order a person reading the tree would expect.
    /// </remarks>
    public static class KdbxNodes
    {
        /// <summary>The resolver's view of an entry, walked where it is asked.</summary>
        public static IAclNode Of(PwEntry entry)
        {
            return Of(entry, CurrentThread.Instance);
        }

        /// <summary>The resolver's view of a group, walked where it is asked.</summary>
        public static IAclNode Of(PwGroup group)
        {
            return Of(group, CurrentThread.Instance);
        }

        /// <summary>The resolver's view of an entry, walked with KeePass's thread held still.</summary>
        public static IAclNode Of(PwEntry entry, IReadHold hold)
        {
            return entry == null ? null : new EntryNode(entry, hold);
        }

        /// <summary>The resolver's view of a group, walked with KeePass's thread held still.</summary>
        public static IAclNode Of(PwGroup group, IReadHold hold)
        {
            return group == null ? null : new GroupNode(group, hold);
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
            private readonly IReadHold _hold;

            public EntryNode(PwEntry entry, IReadHold hold)
            {
                _entry = entry;
                _hold = hold;
            }

            public string Document
            {
                get { return DocumentOf(_entry); }
            }

            public IAclNode Parent
            {
                get { return Of(_entry.ParentGroup, _hold); }
            }

            public string Name
            {
                get { return NameOf(_entry.Uuid); }
            }

            public T Held<T>(Func<T> walk)
            {
                return _hold.Hold(walk);
            }
        }

        private sealed class GroupNode : IAclNode
        {
            private readonly PwGroup _group;
            private readonly IReadHold _hold;

            public GroupNode(PwGroup group, IReadHold hold)
            {
                _group = group;
                _hold = hold;
            }

            public string Document
            {
                get { return DocumentOf(_group); }
            }

            public IAclNode Parent
            {
                get { return Of(_group.ParentGroup, _hold); }
            }

            public string Name
            {
                get { return NameOf(_group.Uuid); }
            }

            public T Held<T>(Func<T> walk)
            {
                return _hold.Hold(walk);
            }
        }
    }
}
