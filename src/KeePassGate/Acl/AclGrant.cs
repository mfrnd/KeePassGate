using System.Collections;
using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// What one node says one profile may do, which is a set of verbs and may be an empty one.
    /// </summary>
    /// <remarks>
    /// A type rather than a bare set, because the empty case is the one worth being unable to
    /// confuse. An empty grant is a rule: this profile may do nothing from here down. The absence
    /// of a grant is silence, and silence is what the node's default answers for. Handing both back
    /// as collections would make them one `Count == 0` check apart.
    /// </remarks>
    public sealed class AclGrant : IEnumerable<AclVerb>
    {
        private readonly HashSet<AclVerb> _verbs;

        internal AclGrant(HashSet<AclVerb> verbs)
        {
            _verbs = verbs;
        }

        public int Count
        {
            get { return _verbs.Count; }
        }

        /// <summary>True when this rule grants that verb, and only then.</summary>
        public bool Holds(AclVerb verb)
        {
            return _verbs.Contains(verb);
        }

        /// <summary>
        /// Whether what this rule grants reaches the attachments inside an entry as well as its
        /// fields.
        /// </summary>
        /// <remarks>
        /// The same question as `Holds(AclVerb.Attachments)`, named for what it means. A rule
        /// holding nothing else is holding a scope over no verbs, which grants nothing at all.
        /// </remarks>
        public bool CoversAttachments
        {
            get { return _verbs.Contains(AclVerb.Attachments); }
        }

        /// <summary>
        /// Whether what this rule grants reaches the values the database protects as well as the
        /// ones it does not.
        /// </summary>
        /// <remarks>
        /// The same question as `Holds(AclVerb.Protected)`, named for what it means. Without it a
        /// read still answers with the entry and everything unprotected in it, and a protected
        /// value comes back as `null` rather than as itself; an update may change what is not
        /// protected and may not overwrite what is.
        /// </remarks>
        public bool CoversProtected
        {
            get { return _verbs.Contains(AclVerb.Protected); }
        }

        public IEnumerator<AclVerb> GetEnumerator()
        {
            return _verbs.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
