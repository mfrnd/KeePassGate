using KeePassGate.Acl;

namespace KeePassGate.Pipeline
{
    /// <summary>Whether this caller may have the protected values on this node.</summary>
    /// <remarks>
    /// The one authorization question a handler asks rather than the router, handed to it by the
    /// router so that asking it is recorded the same way as everything else. A protected value
    /// arrives inside the answer to a route that is already allowed, so the only moment the
    /// question exists is while that answer is being written. That is a reason for the handler
    /// to ask it, not a reason for the answer to go unrecorded.
    /// </remarks>
    public delegate bool ProtectedReach(IAclNode node, AclVerb verb);
}
