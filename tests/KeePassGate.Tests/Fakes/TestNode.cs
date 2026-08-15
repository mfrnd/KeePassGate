using KeePassGate.Acl;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A node in a tree that is not a database, for asking the resolver what it does.
    /// </summary>
    /// <remarks>
    /// The whole reason `IAclNode` is a seam. The rules are about a tree and a document, and neither
    /// of those needs a password manager to exist, so the interesting cases are built in three lines
    /// instead of being reached through a `.kdbx`. `AclInRealDatabaseTests` is where the same rules are put
    /// in front of the real thing, and it is small because this is where the thinking happens.
    /// TODO(step 12): `AclInRealDatabaseTests` arrives with this step.
    /// </remarks>
    public sealed class TestNode : IAclNode
    {
        private TestNode(string name, TestNode parent)
        {
            Name = name;
            Parent = parent;
        }

        public string Document { get; private set; }

        public IAclNode Parent { get; private set; }

        public string Name { get; private set; }

        /// <summary>The top of a tree, which is what a database's root group is.</summary>
        public static TestNode Root(string name)
        {
            return new TestNode(name, null);
        }

        /// <summary>A group or an entry under this one.</summary>
        public TestNode Child(string name)
        {
            return new TestNode(name, this);
        }

        /// <summary>Writes a document here, exactly as given, including a broken one.</summary>
        public TestNode Holding(string document)
        {
            Document = document;
            return this;
        }

        /// <summary>Writes one rule here, which is what most of these trees need.</summary>
        public TestNode Granting(string profile, params AclVerb[] verbs)
        {
            return Holding("{\"" + profile + "\":[" + Written(verbs) + "]}");
        }

        /// <summary>Writes a rule here that stops anything it does not name.</summary>
        public TestNode Sealing(string profile, params AclVerb[] verbs)
        {
            return Holding("{\"" + AclDocument.DefaultKey + "\":\"deny\",\""
                + profile + "\":[" + Written(verbs) + "]}");
        }

        /// <summary>Points this node's parent at itself, which no database can do and a bug can.</summary>
        public TestNode InACycle()
        {
            Parent = this;
            return this;
        }

        private static string Written(AclVerb[] verbs)
        {
            var text = string.Empty;
            for (var i = 0; i < verbs.Length; i++)
            {
                text += (i > 0 ? ",\"" : "\"") + AclVerbs.Write(verbs[i]) + "\"";
            }

            return text;
        }
    }
}
