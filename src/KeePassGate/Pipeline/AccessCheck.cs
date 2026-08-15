using System;
using KeePassGate.Acl;
using KeePassGate.Audit;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// The two answers, combined, and written down.
    /// </summary>
    /// <remarks>
    /// <code>
    /// permitted = record(from the kdbx)  INTERSECT  acl(from the kdbx)
    /// </code>
    ///
    /// **The intersection is structural rather than a step.** The profiles handed to the resolver
    /// are the ones the admission record carries, so the rules can only ever answer about what the
    /// operator approved at pairing: a rule granting `analyst` says nothing about a client whose
    /// record does not name `analyst`, and a record naming ten profiles opens nothing that no rule
    /// mentions. There is no place where the two are compared and could be compared wrongly.
    ///
    /// Both halves live in the database, which is what makes either of them enough to take access
    /// away. Deleting the record refuses the certificate on the next connection; narrowing the rule
    /// refuses the operation on the next request; neither needs a certificate reissued.
    ///
    /// **Every question asked here is written down**, permitted or refused, because a decision
    /// nobody recorded is a decision nobody can review. Recording never fails the request: see
    /// <see cref="IGateAudit"/> for why that direction is the safe one. It can make the request
    /// wait, and <see cref="DecisionPace"/> says why a client that writes too fast has to.
    /// </remarks>
    public sealed class AccessCheck
    {
        private readonly IGateAudit _audit;
        private readonly Func<DateTimeOffset> _clock;
        private readonly DecisionPace _pace;

        public AccessCheck(IGateAudit audit)
            : this(audit, null)
        {
        }

        internal AccessCheck(IGateAudit audit, Func<DateTimeOffset> clock)
            : this(audit, clock, null)
        {
        }

        internal AccessCheck(IGateAudit audit, Func<DateTimeOffset> clock, DecisionPace pace)
        {
            _audit = audit ?? NullGateAudit.Instance;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _pace = pace ?? new DecisionPace();
        }

        /// <summary>Whether this client may do this to this node, recorded either way.</summary>
        public bool Permits(ClientIdentity client, string operationId, AclVerb verb, AclReach reach, IAclNode node)
        {
            return Decide(client, operationId, verb, reach, node).Permitted;
        }

        /// <summary>The same question, with what answered it.</summary>
        public AclDecision Decide(
            ClientIdentity client, string operationId, AclVerb verb, AclReach reach, IAclNode node)
        {
            var decision = Answer(client, verb, reach, node);

            Write(client, operationId, node == null ? null : node.Name, decision.DecidedAt,
                decision.Permitted, decision.Reason);

            return decision;
        }

        /// <summary>
        /// Writes down a certificate this database's authority issued and the gate refused, as it
        /// asked for an operation it therefore cannot have.
        /// </summary>
        /// <remarks>
        /// **Not a decision about the rules, and written down beside them anyway.** No rule was
        /// asked, because a certificate with no admission record has no profiles to ask with. But
        /// the operator who revokes a client opens this file to see whether it keeps trying, and a
        /// revoked client hammering the gate otherwise looked exactly like one that stopped. What
        /// is written is the key the Clients page revoked and the reason the credential check
        /// gave. A caller that proved nothing, or proved it to another authority, names nobody the
        /// operator issued and is not written down: see <see cref="ClientIdentity.Refusal"/>.
        /// </remarks>
        public void Refuse(ClientIdentity client, string operationId)
        {
            if (client == null || client.Refusal == null)
            {
                return;
            }

            Write(client, operationId, null, null, false, client.Refusal);
        }

        private void Write(
            ClientIdentity client, string operationId, string node, string decidedAt, bool permitted, string reason)
        {
            // Before the line, so the wait is paid by the request that would have written it.
            _pace.Take(client == null ? null : client.AdmissionKey);

            _audit.Record(new AuditEntry(
                _clock(),
                client == null ? null : client.DatabaseId,
                client == null ? null : client.AdmissionKey,
                operationId,
                node,
                decidedAt,
                permitted,
                reason));
        }

        private static AclDecision Answer(ClientIdentity client, AclVerb verb, AclReach reach, IAclNode node)
        {
            if (client == null || !client.IsPaired)
            {
                // The router refuses a stranger long before this, writing a refused certificate
                // down through `Refuse`, so reaching here would be a defect rather than an attack.
                // It still answers no, because a check that trusted its caller to have checked is
                // a check that stops being one.
                return AclDecisions.Refused("this client is not admitted");
            }

            if (node == null || verb != AclVerb.Delete)
            {
                // A node that is not there is answered by the resolver, for a removal as for
                // everything else, because what it says is that there was nothing to decide about.
                // Left to the two-sided walk below, an absent node had no parent either, and the
                // line written down said that nothing above it allowed removals: an operator
                // reading that goes looking for the rule that refused a node that was never there.
                // A caller is told the same 404 either way on purpose; the audit is the one place
                // the two are meant to be told apart.
                return AclResolver.Decide(node, client.Profiles, verb, reach);
            }

            // A removal is a move, so it is asked on both sides of it: the group the node is
            // leaving, and the node itself. A rule granting `delete` on a container is what makes
            // its contents removable, including nodes the client creates later; a node whose own
            // rule does not grant it is held back from a rule that frees its siblings.
            var leaving = node.Parent;
            if (leaving == null)
            {
                // Nothing is above the root, so nothing allows it to be removed. That is the rule
                // rather than a special case: the root has no container to ask.
                return AclDecisions.Refused("nothing above this node allows removals");
            }

            var container = AclResolver.Decide(leaving, client.Profiles, verb, reach);
            if (!container.Permitted)
            {
                return container;
            }

            return AclResolver.Decide(node, client.Profiles, verb, reach);
        }
    }
}
