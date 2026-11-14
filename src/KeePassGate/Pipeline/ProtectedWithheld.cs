using System;
using KeePassGate.Acl;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Composes the line for a protected value a read withheld on this node, as the decision
    /// that withheld it, and hands back what adds the line to the request's own.
    /// </summary>
    /// <remarks>
    /// What a read is served is decided by the caller's view of the entry, which the handler
    /// takes from the rules without a line. Handed to the handler by the router beside
    /// <see cref="ProtectedReach"/>, so that a value the view withholds is recorded through
    /// `AccessCheck.Withheld` as the walk that decided it, for the client and the operation the
    /// router decided the request about. Nothing is asked of the rules again, so the line cannot
    /// come to differ from what was served, and a withheld value cannot go unwritten because a
    /// second asking answered otherwise.
    ///
    /// **Composed inside the hold and added after it.** The line names the node and the
    /// profiles a removal took away, which are read from the database, so it is composed where
    /// the read is decided. Whether it is written is known only once the view's strings have
    /// been made and the tag taken, after the hold: a view that withholds nothing has no
    /// line, and a read refused by its condition serves nothing, and writes no line for a
    /// value it did not withhold from an answer. So what
    /// comes back is run by the part of the read left for later, <see cref="ServedAfter"/>,
    /// and reads nothing.
    /// </remarks>
    /// <param name="decision">The refusal the view's sight was decided by.</param>
    /// <param name="listed">
    /// Whether every value withheld is a string the listings show, a title or a URL, which are
    /// served on `list` with `protected` as well.
    /// </param>
    /// <returns>What adds the composed line to the request's lines, to be run at most once.</returns>
    public delegate Action ProtectedWithheld(IAclNode node, AclVerb verb, AclDecision decision, bool listed);
}
