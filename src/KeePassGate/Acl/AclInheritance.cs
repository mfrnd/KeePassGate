using System;
using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// What a node already has in force before it says anything of its own.
    /// </summary>
    /// <remarks>
    /// For showing rather than for deciding. `AclResolver.Decide` is what authorizes a request, and
    /// it answers one question at a time about one client; this answers every profile at once so an
    /// editor can put what is inherited beside what is being written.
    ///
    /// **It says when it does not know.** A document above that will not read is refused where it
    /// is found, which is for every profile no nearer rule has already answered: the rules between
    /// it and this node are still in force and are listed, and everything they do not name is
    /// refused. An editor that carried on listing what the groups further up say would be
    /// describing permissions nobody has. `Readable` false means nothing beyond the broken
    /// document is known.
    ///
    /// **A seal above is not reported, because nothing below it would read differently.** A
    /// `default` of `deny` stops the walk, which is why nothing above it is listed, and that is
    /// all it changes here: a profile with no row is refused either way, by the seal or by no rule
    /// anywhere granting it, and a rule written under the seal is nearer and wins.
    /// </remarks>
    public sealed class AclInheritance
    {
        private readonly Dictionary<string, AclGrant> _grants;

        internal AclInheritance(Dictionary<string, AclGrant> grants, bool readable)
        {
            _grants = grants;
            Readable = readable;
        }

        /// <summary>
        /// Whether the chain could be read all the way. False when a document above will not read
        /// or the tree is deeper than the resolver walks. Then what is listed is what the nearer
        /// rules grant, and every profile they do not name is refused.
        /// </summary>
        public bool Readable { get; private set; }

        /// <summary>Every profile a rule above says something about, in no particular order.</summary>
        public IEnumerable<string> Profiles
        {
            get { return _grants.Keys; }
        }

        /// <summary>
        /// The rule in force for a profile, or null when nothing above names it.
        /// </summary>
        /// <remarks>
        /// An empty rule and null are the two answers that look alike here as everywhere else: the
        /// empty one is a rule above granting this profile nothing, and null is silence.
        /// </remarks>
        public AclGrant For(string profile)
        {
            AclGrant grant;
            return profile != null && _grants.TryGetValue(profile, out grant) ? grant : null;
        }

        /// <summary>Nothing above, which is what the root group inherits.</summary>
        public static AclInheritance None
        {
            get
            {
                return new AclInheritance(new Dictionary<string, AclGrant>(StringComparer.Ordinal), true);
            }
        }
    }
}
