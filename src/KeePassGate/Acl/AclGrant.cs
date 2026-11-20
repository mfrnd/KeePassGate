using System;
using System.Collections;
using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// Letters of `CRUSDLAP` as a set: what a rule grants a profile, what a version recorded as
    /// arriving, or what a walk lets through, any of which may be empty.
    /// </summary>
    /// <remarks>
    /// A type rather than a bare set, because the empty case is the one worth being unable to
    /// confuse. An empty grant is a rule: this profile may do nothing from here down. The absence
    /// of a grant is silence, and silence is what the node's switch answers for. Handing both back
    /// as collections would make them one `Count == 0` check apart.
    ///
    /// **The walk only ever cuts**, <see cref="Cut"/>: no two masks are united, so no answer holds
    /// a verb beside a switch that no single mask held beside it, `STORE.md` §617.
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

        /// <summary>Every letter of `CRUSDLAP`: the limit a walk starts with.</summary>
        public static AclGrant Every { get; } = new AclGrant(new HashSet<AclVerb>(AclVerbs.Grantable));

        /// <summary>No letter at all.</summary>
        public static AclGrant Nothing { get; } = new AclGrant(new HashSet<AclVerb>());

        /// <summary>A grant of these letters, each once; a verb no rule can hold throws.</summary>
        public static AclGrant Of(IEnumerable<AclVerb> letters)
        {
            if (letters == null)
            {
                throw new ArgumentNullException(nameof(letters));
            }

            var held = new HashSet<AclVerb>();
            foreach (var letter in letters)
            {
                if (!AclVerbs.IsGrantable(letter))
                {
                    throw new ArgumentException(AclVerbs.Write(letter) + " is not something a rule can hold.", nameof(letters));
                }

                held.Add(letter);
            }

            return new AclGrant(held);
        }

        /// <summary>The letters this grant and the limit both hold: this grant cut to the limit.</summary>
        /// <remarks>
        /// **A whole mask cut by a whole mask**, so a verb stays beside a switch only where both
        /// masks hold the two together, and no cut can put a switch beside a verb that no mask held
        /// with it. No limit at all cuts to nothing, which is the narrow direction.
        /// </remarks>
        public AclGrant Cut(AclGrant limit)
        {
            if (limit == null)
            {
                return Nothing;
            }

            var held = new HashSet<AclVerb>(_verbs);
            held.IntersectWith(limit._verbs);
            return new AclGrant(held);
        }

        /// <summary>Whether this grant holds exactly the letters another holds.</summary>
        public bool SameAs(AclGrant other)
        {
            return other != null && _verbs.SetEquals(other._verbs);
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
