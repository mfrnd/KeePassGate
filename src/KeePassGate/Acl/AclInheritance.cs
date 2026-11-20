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
    /// **Each profile with what arrives.** A profile is listed where its walk ends at a row, with
    /// what the walk gives it, every limit on the way taken: a row lost or out of form above lists
    /// the profile granting nothing, since a row stands for it there. An editor listing what a
    /// group further up granted before a node between cut it would be describing permissions
    /// nobody has.
    ///
    /// **A switch that refuses above is not reported, because nothing below it would read
    /// differently.** It ends the walk of every profile its rows do not let through, which is why
    /// nothing above it is listed for those, and that is all it changes here: a profile with no
    /// row is refused either way, and a rule written under the switch is nearer and wins.
    /// </remarks>
    public sealed class AclInheritance
    {
        private readonly Dictionary<string, AclGrant> _grants;

        internal AclInheritance(Dictionary<string, AclGrant> grants, bool tooDeep = false)
        {
            _grants = grants;
            TooDeep = tooDeep;
        }

        /// <summary>
        /// Whether the chain stopped because the tree is deeper than the resolver walks. Then what
        /// is listed is what the nearer rules grant, and every profile they do not name is refused.
        /// </summary>
        /// <remarks>
        /// Told apart from a rule that refuses because the remedy differs: a rule is fixed in the
        /// group that holds it, and a depth bound has no rule anywhere to fix.
        /// </remarks>
        public bool TooDeep { get; private set; }

        /// <summary>Every profile whose walk ends at a row above, in no particular order.</summary>
        public IEnumerable<string> Profiles
        {
            get { return _grants.Keys; }
        }

        /// <summary>
        /// What arrives for a profile from the rule in force above, or null when no walk above
        /// ends at a row for it.
        /// </summary>
        /// <remarks>
        /// An empty grant and null are the two answers that look alike here as everywhere else: the
        /// empty grant is a rule above granting this profile nothing, or a cut taking everything it
        /// granted, and null is silence.
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
                return new AclInheritance(new Dictionary<string, AclGrant>(StringComparer.Ordinal));
            }
        }
    }
}
