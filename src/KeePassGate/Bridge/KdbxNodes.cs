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
    /// The whole of what KeePass has to lend the access control model: which node it is, and what
    /// is above it. Everything else about a `PwGroup` or a `PwEntry` stays on the other side of the
    /// seam, which is why the rules can be tested without a password manager.
    ///
    /// **A node's rules are the plugin's store's**, `STORE.md` §617, looked up by the 32 lowercase
    /// hexadecimal characters of its identifier and compared exactly. So a rule stays with its
    /// node wherever the node is moved, a copy under another identifier holds no rule of its own,
    /// and an earlier version of an entry, which shares the entry's identifier and is never
    /// walked, holds none either.
    ///
    /// **The gate's nodes read the rules of the hold they are walked in.** A node made by
    /// `KdbxVault` carries the vault's hold and the rules of the vault's readings, and
    /// `AclResolver` runs each walk inside <see cref="IAclNode.Held"/>, so every level of a walk
    /// reads the rules of the reading made for that hold, the reading the client and the node were
    /// read from, `StoreReadings.Rules`. Nothing about a rule is kept on a node: a node walked in a
    /// later hold reads the rules of that hold.
    ///
    /// **Read on the connection's own thread, with KeePass's thread held still for the whole
    /// walk.** A walk is one read step however many levels it climbs. It raises nothing a retry
    /// could take again, and it is held all the same: a lookup racing a rebalance misses a key
    /// rather than throwing, so a walk over a tree in motion would get a wrong answer that nothing
    /// sees.
    ///
    /// **The editor's nodes read the rules they are given**, those of the reading the tab read as
    /// it opened, and walk where they are asked, on KeePass's own thread.
    ///
    /// **An entry's parent is its group, and a group's is the group above it.** So a rule written
    /// for an entry is nearest, then its group, then that group's parent, up to the root. That is
    /// the order the resolver walks, and it is the order a person reading the tree would expect.
    /// </remarks>
    public static class KdbxNodes
    {
        /// <summary>The rules of a reading that holds none: what a node given no rules reads.</summary>
        private static readonly StoredRules Unruled = StoredRules.Of(null);

        /// <summary>
        /// The gate's view of an entry, walked with KeePass's thread held still, its rules and
        /// those of every group above it read from <paramref name="rules"/> at each level, inside
        /// the hold the walk is made in.
        /// </summary>
        internal static IAclNode Of(PwEntry entry, IReadHold hold, Func<StoredRules> rules)
        {
            return entry == null ? null : new EntryNode(entry, hold, rules);
        }

        /// <summary>
        /// The gate's view of a group, walked with KeePass's thread held still, its rules and
        /// those of every group above it read from <paramref name="rules"/> at each level, inside
        /// the hold the walk is made in.
        /// </summary>
        internal static IAclNode Of(PwGroup group, IReadHold hold, Func<StoredRules> rules)
        {
            return group == null ? null : new GroupNode(group, hold, rules);
        }

        /// <summary>
        /// The editor's view of a group, walked where it is asked: its rules, and those of every
        /// group above it, taken from the rules of the reading the editor's tab read.
        /// </summary>
        internal static IAclNode Of(PwGroup group, StoredRules rules)
        {
            return group == null ? null : new GroupNode(group, CurrentThread.Instance, Given(rules));
        }

        /// <summary>The rules given, or none for a tab that read none.</summary>
        private static Func<StoredRules> Given(StoredRules rules)
        {
            var held = rules ?? Unruled;
            return () => held;
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

        /// <summary>
        /// The rules held for a node of this identifier: none for a node with no identifier, which
        /// no node of KeePass's is.
        /// </summary>
        private static NodeRule RuleOf(Func<StoredRules> rules, PwUuid uuid)
        {
            return uuid == null ? NodeRule.None : rules().At(Hex.Of(uuid.UuidBytes));
        }

        private sealed class EntryNode : IAclNode
        {
            private readonly PwEntry _entry;
            private readonly IReadHold _hold;
            private readonly Func<StoredRules> _rules;

            public EntryNode(PwEntry entry, IReadHold hold, Func<StoredRules> rules)
            {
                _entry = entry;
                _hold = hold;
                _rules = rules;
            }

            /// <summary>The entry's rules in the reading its tree was read with.</summary>
            public NodeRule Rule
            {
                get { return RuleOf(_rules, _entry.Uuid); }
            }

            public IAclNode Parent
            {
                get
                {
                    var group = _entry.ParentGroup;
                    return group == null ? null : new GroupNode(group, _hold, _rules);
                }
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
            private readonly Func<StoredRules> _rules;

            public GroupNode(PwGroup group, IReadHold hold, Func<StoredRules> rules)
            {
                _group = group;
                _hold = hold;
                _rules = rules;
            }

            /// <summary>The group's rules in the reading its tree was read with.</summary>
            public NodeRule Rule
            {
                get { return RuleOf(_rules, _group.Uuid); }
            }

            public IAclNode Parent
            {
                get
                {
                    var group = _group.ParentGroup;
                    return group == null ? null : new GroupNode(group, _hold, _rules);
                }
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
