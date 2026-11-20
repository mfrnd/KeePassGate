using System;
using KeePassGate.Acl;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A node in a tree that is not a database, for asking the resolver what it does.
    /// </summary>
    /// <remarks>
    /// The whole reason `IAclNode` is a seam. The rules are about a tree and what each node holds,
    /// and neither needs a password manager to exist, so the interesting cases are built in three
    /// lines instead of being reached through a `.kdbx`. `AclInRealDatabaseTests` is where the same
    /// rules are put in front of the real thing, and it is small because this is where the thinking
    /// happens.
    /// </remarks>
    public sealed class TestNode : IAclNode
    {
        private readonly Action _walked;

        private TestNode(string name, TestNode parent, Action walked)
        {
            Name = name;
            Parent = parent;
            _walked = walked;
        }

        /// <summary>The rules put here: `NodeRule.None` until some are.</summary>
        public NodeRule Rule { get; private set; } = NodeRule.None;

        public IAclNode Parent { get; private set; }

        public string Name { get; private set; }

        /// <summary>How many walks have been run from this node.</summary>
        public int Walks { get; private set; }

        /// <summary>Walks where it is, since a tree that is not a database has no thread to hold.</summary>
        public T Held<T>(Func<T> walk)
        {
            Walks++;
            _walked?.Invoke();
            return walk();
        }

        /// <summary>The top of a tree, which is what a database's root group is.</summary>
        public static TestNode Root(string name)
        {
            return new TestNode(name, null, null);
        }

        /// <summary>
        /// The top of a tree that tells <paramref name="walked"/> of every walk made from any of
        /// its nodes, for a fake database that counts where it is walked.
        /// </summary>
        public static TestNode Root(string name, Action walked)
        {
            return new TestNode(name, null, walked);
        }

        /// <summary>A group or an entry under this one.</summary>
        public TestNode Child(string name)
        {
            return new TestNode(name, this, _walked);
        }

        /// <summary>Puts these rules here, as the store holds them, in place of whatever was here.</summary>
        public TestNode Holding(NodeRule rule)
        {
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            return this;
        }

        /// <summary>Puts a rule here for a single profile, which is what most of these trees need.</summary>
        public TestNode Granting(string profile, params AclVerb[] verbs)
        {
            return Holding(Rules.Node(false, (profile, VerbMask.Of(verbs))));
        }

        /// <summary>Puts a rule here for a single profile, with the switch refusing anything it does not name.</summary>
        public TestNode Sealing(string profile, params AclVerb[] verbs)
        {
            return Holding(Rules.Node(true, (profile, VerbMask.Of(verbs))));
        }

        /// <summary>Points this node's parent at itself, which no database can do and a bug can.</summary>
        public TestNode InACycle()
        {
            Parent = this;
            return this;
        }
    }
}
