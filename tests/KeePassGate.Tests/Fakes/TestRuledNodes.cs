using System.Collections.Generic;
using KeePassGate.Acl;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A tree of nodes with documents on them, and no database anywhere.
    /// </summary>
    /// <remarks>
    /// What the profiles page counts over. A rule is a document on a node, and counting them needs
    /// no password manager to exist, so the interesting cases are three lines instead of a
    /// temporary `.kdbx`. `DatabaseNodesTests` is where the same walk meets the real thing.
    /// </remarks>
    public sealed class TestRuledNodes : IRuledNodes
    {
        private readonly List<TestRuledNode> _nodes;

        public TestRuledNodes(params TestRuledNode[] nodes)
        {
            _nodes = new List<TestRuledNode>(nodes);
        }

        /// <summary>How many times the tree has been walked.</summary>
        public int Walks { get; private set; }

        public IEnumerable<IRuledNode> All()
        {
            Walks++;

            foreach (var node in _nodes)
            {
                yield return node;
            }
        }
    }

    /// <summary>One node's rules, in a field.</summary>
    public sealed class TestRuledNode : IRuledNode
    {
        public TestRuledNode(string name, string document)
        {
            Name = name;
            Current = document;
        }

        public string Name { get; private set; }

        /// <summary>What the node holds. Settable, as an edit on another dialog would set it.</summary>
        public string Current { get; set; }

        public string Read()
        {
            return Current;
        }
    }
}
