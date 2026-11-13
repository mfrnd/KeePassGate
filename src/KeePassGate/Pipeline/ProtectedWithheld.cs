using KeePassGate.Acl;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Writes down that a read withheld a protected value on this node, as the decision that
    /// withheld it.
    /// </summary>
    /// <remarks>
    /// What a read is served is decided by the caller's view of the entry, which the handler
    /// takes from the rules without a line. Handed to the handler by the router beside
    /// <see cref="ProtectedReach"/>, so that a value the view withholds is recorded through
    /// `AccessCheck.Withheld` as the walk that decided it, for the client and the operation the
    /// router decided the request about. Nothing is asked of the rules again, so the line cannot
    /// come to differ from what was served, and a withheld value cannot go unwritten because a
    /// second asking answered otherwise.
    /// </remarks>
    /// <param name="decision">The refusal the view's sight was decided by.</param>
    /// <param name="listed">
    /// Whether every value withheld is a string the listings show, a title or a URL, which are
    /// served on `list` with `protected` as well.
    /// </param>
    public delegate void ProtectedWithheld(IAclNode node, AclVerb verb, AclDecision decision, bool listed);
}
