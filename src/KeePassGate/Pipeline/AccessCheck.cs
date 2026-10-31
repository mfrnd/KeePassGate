using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Audit;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// The two answers, combined, and written down.
    /// </summary>
    /// <remarks>
    /// <code>
    /// permitted = (record INTERSECT list)(from the kdbx)  INTERSECT  acl(from the kdbx)
    /// </code>
    ///
    /// **The intersection is structural rather than a step.** The profiles handed to the resolver
    /// are the ones the admission record carries that the database's list still names, so the
    /// rules can only ever answer about what the operator approved when the client was admitted,
    /// at pairing or at a renewal, and has not taken away since: a rule granting `analyst` says
    /// nothing about a client whose record does not name `analyst`, or while the list does not, and
    /// a record naming ten profiles opens nothing that no rule mentions. There is no place where the
    /// two are compared and could be compared wrongly.
    ///
    /// All three live in the database, which is what makes any of them enough to take access
    /// away. Deleting the record refuses the certificate on the next connection; taking a name off
    /// the list or narrowing the rule refuses the operation on the next request; none needs a
    /// certificate reissued.
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
            Admissions = new AdmissionAudit(_audit, _clock, _pace);
        }

        /// <summary>
        /// What the same audit records about admissions: pairings, renewals and revocations.
        /// </summary>
        /// <remarks>
        /// Made here rather than beside this, so that both write with one clock and count a client
        /// against one allowance: a client gets one budget for everything it can make the audit
        /// write, and splitting it would double what one flooding client could push out of the file.
        /// </remarks>
        public AdmissionAudit Admissions { get; private set; }

        /// <summary>
        /// What a refusal says when the profiles the operator removed would have permitted it.
        /// </summary>
        public const string ProfileRemoved = "profile removed";

        /// <summary>
        /// Whether this client may do this to this node, with what answered it, recorded either way.
        /// </summary>
        /// <remarks>
        /// **A refusal is asked a second time when the client's record holds a removed name**,
        /// this time with the record's whole list. If that would have permitted it, the line says
        /// <see cref="ProfileRemoved"/> and names the removed profiles that would have done it,
        /// and the node whose rule would have; otherwise it is written as the rules refused it.
        /// Without this the two read alike, and an operator who took a name away and then finds a
        /// client refused goes looking for a rule that is not the reason.
        ///
        /// Only a refusal, and only from a client holding such a name, pays for the second walk,
        /// and nothing about it reaches the caller: the answer returned is the first one.
        /// </remarks>
        public AclDecision Decide(
            ClientIdentity client, string operationId, AclVerb verb, AclReach reach, IAclNode node)
        {
            return Decide(client, operationId, verb, reach, node, null);
        }

        /// <summary>
        /// The same, naming the identifier the request asked for, which the line keeps when no
        /// node was found for it.
        /// </summary>
        public AclDecision Decide(
            ClientIdentity client, string operationId, AclVerb verb, AclReach reach, IAclNode node, string requested)
        {
            var decision = Answer(client, client == null ? null : client.Profiles, verb, reach, node);
            var name = node == null ? null : node.Name;

            AclDecision restored = null;
            var removed = decision.Permitted ? null : Restoring(client, verb, reach, node, out restored);

            if (removed == null || restored == null)
            {
                Write(
                    client, operationId, name, decision.DecidedAt, decision.Permitted, decision.Reason, null, requested);
            }
            else
            {
                Write(client, operationId, name, restored.DecidedAt, false, ProfileRemoved, removed, requested);
            }

            return decision;
        }

        /// <summary>
        /// The removed profiles that would have permitted this, or null when the client holds none
        /// or even all of them would not have.
        /// </summary>
        /// <remarks>
        /// Each name is asked on its own beside what the client still holds, so that a removed
        /// name that grants nothing here is not blamed for a refusal. When none does alone and all
        /// of them together do, which a removal asked on two nodes allows, every one is named.
        /// </remarks>
        private static IReadOnlyList<string> Restoring(
            ClientIdentity client, AclVerb verb, AclReach reach, IAclNode node, out AclDecision restored)
        {
            restored = null;

            if (client == null || !client.IsPaired || client.Removed.Count == 0)
            {
                return null;
            }

            var whole = Answer(client, client.Profiles.Concat(client.Removed).ToList(), verb, reach, node);
            if (!whole.Permitted)
            {
                return null;
            }

            restored = whole;

            if (client.Removed.Count == 1)
            {
                return client.Removed;
            }

            var alone = client.Removed
                .Where(profile => Answer(
                    client, client.Profiles.Concat(new[] { profile }).ToList(), verb, reach, node).Permitted)
                .ToList();

            return alone.Count == 0 ? client.Removed : alone;
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
        /// gave, and no client key, since no record stands behind the certificate to carry one:
        /// `docs/audit.md` says why it is not looked for. A caller that proved nothing, or proved it to another authority, names nobody the
        /// operator issued and is not written down: see <see cref="ClientIdentity.Refusal"/>.
        /// </remarks>
        public void Refuse(ClientIdentity client, string operationId)
        {
            if (client == null || client.Refusal == null)
            {
                return;
            }

            Write(client, operationId, null, null, false, client.Refusal, null, null);
        }

        private void Write(
            ClientIdentity client,
            string operationId,
            string node,
            string decidedAt,
            bool permitted,
            string reason,
            IReadOnlyList<string> removedProfiles,
            string requested)
        {
            // Before the line, so the wait is paid by the request that would have written it. By
            // the client key, so a renewal keeps the allowance spent; a refused certificate has no
            // client and is counted by its own admission key, as `DecisionPace` says.
            _pace.Take(client == null ? null : client.ClientKey ?? client.AdmissionKey);

            _audit.Record(new AuditEntry(
                _clock(),
                client == null ? null : client.DatabaseId,
                client == null ? null : client.AdmissionKey,
                client == null ? null : client.ClientKey,
                operationId,
                node,
                decidedAt,
                permitted,
                reason,
                removedProfiles,
                Identifier(requested)));
        }

        /// <summary>
        /// An identifier as the routes write one, 32 hexadecimal characters or `root`, or null for
        /// anything else, so that nothing a caller could spell freely reaches the line.
        /// </summary>
        internal static string Identifier(string requested)
        {
            if (string.Equals(requested, "root", StringComparison.Ordinal))
            {
                return requested;
            }

            if (requested == null || requested.Length != 32)
            {
                return null;
            }

            foreach (var c in requested)
            {
                if (!Ascii.IsHexDigit(c))
                {
                    return null;
                }
            }

            return requested;
        }

        private static AclDecision Answer(
            ClientIdentity client, IReadOnlyList<string> profiles, AclVerb verb, AclReach reach, IAclNode node)
        {
            if (client == null || !client.IsPaired)
            {
                // The router refuses a stranger long before this, writing a refused certificate
                // down through `Refuse`, so reaching here would be a defect rather than an attack.
                // It still answers no, because a check that trusted its caller to have checked is
                // a check that stops being one.
                return AclDecisions.Refused("this client is not admitted");
            }

            if (verb == AclVerb.Any)
            {
                // Every verb at once, asked of the node alone and profile by profile, with what
                // each profile is granted carried on the answer: `AclResolver.DecideAny`. Not a
                // removal, so the group above is not asked: a `D` in the answer is the rule at the
                // node saying removals are allowed here, as the editor shows it.
                return AclResolver.DecideAny(node, profiles);
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
                return AclResolver.Decide(node, profiles, verb, reach);
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

            var container = AclResolver.Decide(leaving, profiles, verb, reach);
            if (!container.Permitted)
            {
                return container;
            }

            return AclResolver.Decide(node, profiles, verb, reach);
        }
    }
}
