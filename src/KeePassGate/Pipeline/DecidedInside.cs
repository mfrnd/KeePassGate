using KeePassGate.Acl;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// A change's request as it is decided where the change is made: the caller and the rules
    /// read again and asked about the node the change found, or null when they refuse it.
    /// </summary>
    /// <remarks>
    /// **The router's answer before dispatch can refuse a change and cannot permit one.** A change
    /// waits for its turn on KeePass's thread after the router has decided it, and the operator
    /// can revoke the client, take a profile off the list, edit a rule or move the node in that
    /// wait. So for every operation whose verb changes the database the router leaves this beside
    /// its answer, and the change calls it from inside its turn, with the node it found there:
    /// the admission record and the list of profiles are read again, the rules are asked again,
    /// and that answer is the answer acted on and the line written down.
    ///
    /// **Null is a refusal, whoever made it**: a record that went, a profile that no longer
    /// grants the verb, a node that now sits under a group that refuses. The caller is answered
    /// as for a node that is not there, and the audit holds which it was.
    ///
    /// What comes back otherwise is the request as it stands inside the turn, for the questions
    /// the handler asks beside the router's: its <see cref="IGateContext.Client"/> is the client
    /// read again, and the <see cref="ProtectedReach"/> it carries asks about that client.
    ///
    /// Throws `VaultUnsettled` when the admission records could not be listed, which the change
    /// answers `busy`.
    /// </remarks>
    /// <param name="node">The node the change found, inside its turn.</param>
    public delegate IGateContext DecidedInside(IAclNode node);
}
