namespace KeePassGate.Audit
{
    /// <summary>
    /// Where decisions are written down.
    /// </summary>
    /// <remarks>
    /// A seam, because what an audit is for depends on where it goes: a file on the operator's
    /// disk in a running plugin, and a list in memory in a test that wants to assert about what was
    /// recorded rather than about what a file contains.
    ///
    /// **Recording never fails a request.** An implementation swallows what it cannot write, and
    /// reports it through the log instead. That is the deliberate direction: a full disk must not
    /// silently revoke every agent's access, and a plugin that refused to answer because it could
    /// not write a line would be a denial of service anybody could arrange.
    /// </remarks>
    public interface IGateAudit
    {
        void Record(AuditEntry entry);

        /// <summary>Whether what is recorded here is kept anywhere.</summary>
        /// <remarks>
        /// Asked by the pace, `DecisionPace`, which holds a client back only so that its lines
        /// cannot roll a bounded file away: an audit that keeps nothing has nothing to protect,
        /// and a client slowed for it would be slowed for nothing.
        /// </remarks>
        bool Keeps { get; }
    }

    /// <summary>An audit that keeps nothing, for a caller that has not been given one.</summary>
    public sealed class NullGateAudit : IGateAudit
    {
        public static readonly NullGateAudit Instance = new NullGateAudit();

        private NullGateAudit()
        {
        }

        public bool Keeps
        {
            get { return false; }
        }

        public void Record(AuditEntry entry)
        {
        }
    }
}
