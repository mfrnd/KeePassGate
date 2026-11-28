using System;
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
    /// profile's rule stands, and a count has to reach nodes no dialog is open on at all. And read
    /// by the KPG tab of a group's dialog, and by its OK, for the nodes below the group, which a
    /// write above is carried down, <see cref="Tree"/>.
    ///
    /// **Not an entry's history.** Every item in `PwEntry.History` shares the entry's identifier
    /// and is no node of its own: the resolver walks the live tree.
    ///
    /// **The count leaves the recycle bin out.** The vault reaches nothing in it, `DESIGN.md`
    /// §138, so a rule for the bin or for anything removed into it decides nothing until the
    /// operator moves the node back out. Counted, the Profiles page offered to put such rules back
    /// in force by adding a name again, and counted them among the rules a removal leaves inert,
    /// which they were already. The bin is the bin the vault hides, through the same two questions
    /// it asks. **The tree a write above is carried down holds it**, since a node restored from it
    /// takes its rules along, and `IRuledTree` has what that costs.
    ///
    /// **Neither holds the plugin's store**, which the vault hides as it hides the bin, `STORE.md`
    /// §633: a rule naming a group of the store or a record of it reaches no client. Its groups are
    /// told by their identifiers, and its entries as the reading handed over tells them,
    /// `StoreNodes`.
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
            return Walk(reading, true);
        }

        /// <summary>
        /// Every group and entry the tree holds, the recycle bin and what is in it among them, and
        /// no node of the plugin's store: what the seal's dialog names a rule's node by, since a
        /// rule a store lost may be about a node removed into the bin, `STORE.md` §625.
        /// </summary>
        public IEnumerable<IRuledNode> Held(StoreReading reading)
        {
            return Walk(reading, false);
        }

        /// <summary>The nodes of the tree, the recycle bin and what is in it left out where asked.</summary>
        private IEnumerable<IRuledNode> Walk(StoreReading reading, bool leaveBinOut)
        {
            var root = _database == null ? null : _database.RootGroup;
            if (root == null)
            {
                yield break;
            }

            var bin = leaveBinOut ? KdbxVault.Bin(_database, root) : null;
            var store = reading == null ? null : StoreNodes.Of(reading);

            if (!Left(root, bin))
            {
                yield return new Node(root.Uuid, () => root.Name);
            }

            foreach (var group in root.GetGroups(true).Where(group => !Left(group, bin)))
            {
                yield return new Node(group.Uuid, () => group.Name);
            }

            foreach (var entry in root.GetEntries(true).Where(entry => !Left(entry.ParentGroup, bin)
                                                                       && (store == null || !store.Holds(entry))))
            {
                yield return new Node(entry.Uuid, () => entry.Strings.ReadSafe(PwDefs.TitleField));
            }
        }

        /// <summary>
        /// The tree below a group as it stands now, which a write above at the group is carried
        /// down, `STORE.md` §617: the group, every node below it at which a version of these rules
        /// lets a profile through, and every group between, each known by its identifier. The
        /// recycle bin is in it as any group is, and the plugin's store and everything in it is
        /// not. Walked once, where it is first asked, and kept for the turn it is asked in and no
        /// longer.
        /// </summary>
        /// <param name="reading">The reading of the turn, which tells the store's entries.</param>
        /// <param name="rules">The rules of that reading, which tell the nodes that let through.</param>
        /// <param name="top">
        /// The dialog's group: a group no longer under the database's root, or a group of the
        /// store's, has nothing below it.
        /// </param>
        /// <remarks>
        /// **Walked only where a version lets through, and only below the group.** Where no
        /// version of the rules lets a profile through, nothing is walked; otherwise the groups
        /// below the group are walked once, and a node's identifier is written as text only for
        /// a node that lets through and the groups above it up to the group. The group is climbed
        /// from once, to tell that it stands under the root and outside the store; the store's
        /// groups are passed over as the walk meets them, and the store's entries are asked about
        /// only among the nodes that let through.
        /// </remarks>
        internal IRuledTree Tree(StoreReading reading, StoredRules rules, PwGroup top)
        {
            return new Walked(_database, reading, rules, top);
        }

        /// <summary>Whether a group is the recycle bin, a group of the store, or inside either.</summary>
        private static bool Left(PwGroup group, PwGroup bin)
        {
            return KdbxVault.Discarded(group, bin) || StoreNodes.Holds(group);
        }

        /// <summary>
        /// The tree below a group walked once, each node that lets through and each group above
        /// it with the node above it and the nodes directly below it.
        /// </summary>
        private sealed class Walked : IRuledTree
        {
            /// <summary>The length of a node's identifier as a rule names it.</summary>
            private const int NodeLength = 32;

            private readonly PwDatabase _database;
            private readonly StoreReading _reading;
            private readonly StoredRules _rules;
            private readonly PwGroup _top;
            private Dictionary<string, string> _above;
            private Dictionary<string, List<string>> _below;

            public Walked(PwDatabase database, StoreReading reading, StoredRules rules, PwGroup top)
            {
                _database = database;
                _reading = reading;
                _rules = rules;
                _top = top;
            }

            public string Above(string node)
            {
                Walk();
                string above;
                return node != null && _above.TryGetValue(node, out above) ? above : null;
            }

            public IEnumerable<string> Below(string node)
            {
                Walk();
                var found = new List<string>();
                List<string> children;
                if (node == null || !_below.TryGetValue(node, out children))
                {
                    return found;
                }

                var next = new Queue<string>(children);
                while (next.Count > 0)
                {
                    var below = next.Dequeue();
                    found.Add(below);
                    if (_below.TryGetValue(below, out children))
                    {
                        foreach (var child in children)
                        {
                            next.Enqueue(child);
                        }
                    }
                }

                return found;
            }

            /// <summary>
            /// Every node below the group that lets through, and every group between, each under
            /// the node above it; nothing where no node lets through or the group is out of reach.
            /// </summary>
            private void Walk()
            {
                if (_above != null)
                {
                    return;
                }

                _above = new Dictionary<string, string>(StringComparer.Ordinal);
                _below = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var letting = new Dictionary<PwUuid, string>();
                foreach (var node in (_rules == null ? Enumerable.Empty<string>() : _rules.LettingThrough).Where(IsNode))
                {
                    letting[new PwUuid(Bytes(node))] = node;
                }

                if (letting.Count == 0 || !Reached(_top))
                {
                    return;
                }

                var store = _reading == null ? null : StoreNodes.Of(_reading);
                var steps = new Stack<Step>();
                steps.Push(new Step(_top, null));
                while (steps.Count > 0)
                {
                    var step = steps.Pop();
                    if (Letting(letting, step.Group.Uuid) != null)
                    {
                        Place(step);
                    }

                    foreach (var found in step.Group.Entries
                                 .Select(entry => new { Entry = entry, Node = Letting(letting, entry.Uuid) })
                                 .Where(found => found.Node != null && (store == null || !store.Holds(found.Entry))))
                    {
                        Place(step);
                        Link(found.Node, step.Node);
                    }

                    foreach (var group in step.Group.Groups.Where(group => !StoreFormat.IsGroupIdentifier(group.Uuid.UuidBytes)))
                    {
                        steps.Push(new Step(group, step));
                    }
                }
            }

            /// <summary>
            /// Whether the group is the database's root or under it, and neither a group of the
            /// store's nor inside such a group, by a single climb.
            /// </summary>
            private bool Reached(PwGroup top)
            {
                var root = _database == null ? null : _database.RootGroup;
                if (root == null)
                {
                    return false;
                }

                for (var at = top; at != null; at = at.ParentGroup)
                {
                    if (StoreFormat.IsGroupIdentifier(at.Uuid.UuidBytes))
                    {
                        return false;
                    }

                    if (ReferenceEquals(at, root))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>A group walked and every group above it up to the group walked from, each under the node above it, once.</summary>
            private void Place(Step step)
            {
                for (var at = step; at != null && !at.Placed; at = at.Parent)
                {
                    at.Placed = true;
                    if (at.Parent == null)
                    {
                        _above[at.Node] = null;
                    }
                    else
                    {
                        Link(at.Node, at.Parent.Node);
                    }
                }
            }

            /// <summary>A node under the node above it.</summary>
            private void Link(string node, string above)
            {
                _above[node] = above;
                List<string> children;
                if (!_below.TryGetValue(above, out children))
                {
                    children = new List<string>();
                    _below.Add(above, children);
                }

                children.Add(node);
            }

            /// <summary>The node's identifier as a rule names it where a version there lets through, or null.</summary>
            private static string Letting(IReadOnlyDictionary<PwUuid, string> letting, PwUuid uuid)
            {
                string node;
                return letting.TryGetValue(uuid, out node) ? node : null;
            }

            private static bool IsNode(string node)
            {
                return node != null && node.Length == NodeLength && node.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
            }

            private static byte[] Bytes(string hex)
            {
                var bytes = new byte[hex.Length / 2];
                for (var at = 0; at < bytes.Length; at++)
                {
                    bytes[at] = Convert.ToByte(hex.Substring(at * 2, 2), 16);
                }

                return bytes;
            }

            /// <summary>A group of the walk, with the group above it in the walk and its identifier once written.</summary>
            private sealed class Step
            {
                private string _node;

                public Step(PwGroup group, Step parent)
                {
                    Group = group;
                    Parent = parent;
                }

                public PwGroup Group { get; }

                /// <summary>The group above it in the walk, null for the group walked from.</summary>
                public Step Parent { get; }

                /// <summary>Whether the group stands in the tree, under the node above it.</summary>
                public bool Placed { get; set; }

                /// <summary>The group's identifier as a rule names it, written where it is first asked.</summary>
                public string Node
                {
                    get
                    {
                        if (_node == null)
                        {
                            _node = Hex.Of(Group.Uuid.UuidBytes);
                        }

                        return _node;
                    }
                }
            }
        }

        private sealed class Node : IRuledNode
        {
            private readonly Func<string> _title;

            public Node(PwUuid uuid, Func<string> title)
            {
                Id = Hex.Of(uuid.UuidBytes);
                _title = title;
            }

            public string Id { get; private set; }

            public string Title
            {
                get { return _title() ?? string.Empty; }
            }
        }
    }
}
