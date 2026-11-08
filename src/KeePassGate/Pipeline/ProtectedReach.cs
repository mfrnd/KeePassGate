using System.Collections.Generic;
using KeePassGate.Acl;

namespace KeePassGate.Pipeline
{
    /// <summary>Whether this caller may have the protected values on this node.</summary>
    /// <remarks>
    /// The one authorization question a handler asks rather than the router, handed to it by the
    /// router so that the answer the handler acts on is the answer written down, through
    /// `AccessCheck.Reaches`, which records a refusal in the same walk that decides it. A protected
    /// value arrives inside the answer to a route that is already allowed, so the only moment the
    /// question exists is while that answer is being written. That is a reason for the handler to
    /// ask it, not a reason for the answer to go unrecorded, nor for the handler to decide it
    /// apart from what is recorded.
    /// </remarks>
    /// <param name="profiles">
    /// The caller's profiles to ask with, for a question a change asks of each profile on its own,
    /// or null for every profile the caller holds.
    /// </param>
    public delegate bool ProtectedReach(IAclNode node, AclVerb verb, IReadOnlyList<string> profiles);
}
