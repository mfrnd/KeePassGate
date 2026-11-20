using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A tree of nodes known by their identifiers, each under the node above it, and no database
    /// anywhere: what a write above is carried down in the unit tier.
    /// </summary>
    /// <remarks>
    /// The nodes below a node are handed over in the order given, or reversed, so that a test can
    /// hold a write above to the same result whichever order the tree walks in.
    /// `GrantEditorInRealDialogsTests` is where the same write meets KeePass's own tree.
    /// </remarks>
    public sealed class TestRuledTree : IRuledTree
    {
        private readonly List<KeyValuePair<string, string>> _nodes;
        private readonly bool _reversed;

        private TestRuledTree(IEnumerable<KeyValuePair<string, string>> nodes, bool reversed)
        {
            _nodes = nodes.ToList();
            _reversed = reversed;
        }

        /// <summary>A tree of these nodes, each given with the node above it, null above the root.</summary>
        public static TestRuledTree Of(params (string Node, string Above)[] nodes)
        {
            return new TestRuledTree(nodes.Select(node => new KeyValuePair<string, string>(node.Node, node.Above)), false);
        }

        /// <summary>The same tree, handing the nodes below over in the reverse order.</summary>
        public TestRuledTree Reversed()
        {
            return new TestRuledTree(_nodes, !_reversed);
        }

        /// <summary>The same tree with a node put under another node, as a move in KeePass puts it.</summary>
        public TestRuledTree Moved(string node, string above)
        {
            return new TestRuledTree(_nodes.Select(held => held.Key == node ? new KeyValuePair<string, string>(node, above) : held), _reversed);
        }

        public string Above(string node)
        {
            return _nodes.Where(held => held.Key == node).Select(held => held.Value).FirstOrDefault();
        }

        public IEnumerable<string> Below(string node)
        {
            var found = new List<string>();
            var next = new Queue<string>(new[] { node });
            while (next.Count > 0)
            {
                var at = next.Dequeue();
                foreach (var child in _nodes.Where(held => string.Equals(held.Value, at, StringComparison.Ordinal)).Select(held => held.Key))
                {
                    found.Add(child);
                    next.Enqueue(child);
                }
            }

            if (_reversed)
            {
                found.Reverse();
            }

            return found;
        }
    }
}
