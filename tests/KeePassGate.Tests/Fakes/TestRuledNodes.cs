using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A tree of nodes known by their identifiers, and no database anywhere.
    /// </summary>
    /// <remarks>
    /// What the profiles page counts over. A rule is a version in the plugin's store, named by its
    /// node's identifier, and counting them needs no password manager to exist, so the interesting
    /// cases are three lines, with no temporary `.kdbx`. `DatabaseNodesTests` is where the same
    /// walk meets the real thing.
    /// </remarks>
    public sealed class TestRuledNodes : IRuledNodes
    {
        private readonly List<TestRuledNode> _nodes;
        private readonly List<TestRuledNode> _binned = new List<TestRuledNode>();

        public TestRuledNodes(params TestRuledNode[] nodes)
        {
            _nodes = new List<TestRuledNode>(nodes);
        }

        /// <summary>How many times the tree has been walked.</summary>
        public int Walks { get; private set; }

        public IEnumerable<IRuledNode> All(StoreReading reading)
        {
            Walks++;

            foreach (var node in _nodes)
            {
                yield return node;
            }
        }

        /// <summary>The nodes, and every node put in the recycle bin with <see cref="Binned"/>.</summary>
        public IEnumerable<IRuledNode> Held(StoreReading reading)
        {
            return _nodes.Concat(_binned);
        }

        /// <summary>A node in the recycle bin: held by the tree, and no node a rule is in force at.</summary>
        public TestRuledNodes Binned(TestRuledNode node)
        {
            _binned.Add(node);
            return this;
        }

        /// <summary>
        /// A version of a profile's row at a node holding this text, written into the store under
        /// the key that stands by a write of its own, as whoever holds the key can write it, so
        /// that two versions written apart both stand; answers its identifier.
        /// </summary>
        internal static string Rule(MemoryStore store, string node, string profile, string text)
        {
            var key = KeyOf(store);
            var version = Version(key, node, profile, text);
            var written = store.Write(new[] { new StoreAct("a rule", key, StoreFormat.KeyName(key), new[] { version }, null) });
            if (!written.Taken)
            {
                throw new InvalidOperationException("a rule could not be seeded: " + written.Reason);
            }

            return version.Hex;
        }

        /// <summary>
        /// A version of a profile's row at a node that the store knows by its deletion record
        /// alone, as a Synchronize leaves it from a copy that deleted it for good; answers its
        /// identifier.
        /// </summary>
        internal static string Lost(MemoryStore store, string node, string profile)
        {
            var version = Version(KeyOf(store), node, profile, "row:R");
            store.Delete(version.Identifier);
            return version.Hex;
        }

        private static byte[] KeyOf(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return (byte[])reading.InForce.Key.Bytes.Clone();
            }
        }

        private static StoreRecord Version(byte[] key, string node, string profile, string text)
        {
            return StoreRecord.Compose(key, StoreKinds.Version, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.After, string.Empty },
                { StoreKinds.Doc, text },
                { StoreKinds.ObjectMember, StoreComposer.RowObject(node, profile) },
            });
        }
    }

    /// <summary>One group or entry, known by its identifier.</summary>
    public sealed class TestRuledNode : IRuledNode
    {
        /// <summary>A node known by these 32 lowercase hexadecimal characters, as a rule names it.</summary>
        public TestRuledNode(string id, string title = null)
        {
            Id = id;
            Title = title ?? string.Empty;
        }

        public string Id { get; private set; }

        public string Title { get; private set; }
    }
}
