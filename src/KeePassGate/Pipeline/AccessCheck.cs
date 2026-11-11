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
        /// The reason the same refusal gives when nobody removed them: the database's list of
        /// profiles would not read, so the names it lost count as removed.
        /// </summary>
        /// <remarks>
        /// The operator's ruling of 2026-11-08. Written as <see cref="ProfileRemoved"/>, it sent
        /// an operator to name again profiles nobody took off, over a list that a copy of the
        /// database from before the damage still holds whole.
        /// </remarks>
        public const string ProfilesUnreadable = "profile list unreadable";

        /// <summary>
        /// The reason a refusal gives when an admitted certificate asked for an ordinary route on a
        /// connection that arrived on an invitation.
        /// </summary>
        public const string ArrivedOnInvitation = "this connection arrived on an invitation";

        /// <summary>
        /// Writes down a certificate this database admits, refused an operation because the
        /// connection it asked on arrived on an invitation, which reaches the pairing routes alone.
        /// </summary>
        /// <remarks>
        /// The other refusal of a certificate this database issued, beside
        /// <see cref="Refuse(ClientIdentity, string)"/>'s, and it went unwritten: a client holding
        /// an invitation as well can ask on its name for what its own name gives it, with the
        /// pairing budget on the thread, and the audit is where the operator sees it. The
        /// certificate is admitted, so its line names both keys.
        /// </remarks>
        public void RefuseOnInvitation(ClientIdentity client, string operationId)
        {
            if (client == null || !client.IsPaired)
            {
                return;
            }

            Write(client, operationId, null, null, false, ArrivedOnInvitation, null, null, null, false);
        }

        /// <summary>
        /// Whether this client may do this to this node, with what answered it, recorded either way.
        /// </summary>
        /// <remarks>
        /// **A refusal is asked a second time when the client's record holds a removed name**,
        /// this time with the record's whole list. If that would have permitted it, the line holds
        /// <see cref="ProfileRemoved"/> and names the removed profiles that would have done it,
        /// and the node whose rule would have, or <see cref="ProfilesUnreadable"/> when the names
        /// were lost to a list that would not read; otherwise it is written as the rules refused
        /// it.
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
            return Decide(client, operationId, verb, reach, node, requested, null);
        }

        /// <summary>
        /// The same, with the line kept as the request's decision rather than written.
        /// </summary>
        /// <remarks>
        /// For a decision made while KeePass's thread is stopped for it: <see cref="DecisionLines"/>
        /// has the reason, and <see cref="Write(DecisionLines)"/> writes what was kept.
        /// </remarks>
        public AclDecision Decide(
            ClientIdentity client,
            string operationId,
            AclVerb verb,
            AclReach reach,
            IAclNode node,
            string requested,
            DecisionLines lines)
        {
            var decision = Answer(client, client == null ? null : client.Profiles, verb, reach, node);
            Written(client, operationId, verb, reach, node, decision, requested, true, lines, true);
            return decision;
        }

        /// <summary>
        /// Whether this client may have what this scope covers at this node, written down only
        /// when it may not.
        /// </summary>
        /// <remarks>
        /// **The question a handler asks of the protected values inside an answer the router has
        /// already allowed**, through the <see cref="ProtectedReach"/> the router hands it. The
        /// walk that answers is the walk written down, so what the handler acts on and what the
        /// line holds cannot disagree. Decided by the handler and asked again here only to be
        /// written, a rule edit landing between the two walks left a withheld value unrecorded,
        /// and the strip guard, which asks profile by profile, was written down by a walk pooling
        /// every profile, which permitted, and so wrote nothing at all.
        ///
        /// **Written only when it refuses.** A protected value handed over is covered by the line
        /// the router wrote before dispatch, and a line for every ordinary read would be the noise
        /// `A_listing_is_recorded_once_and_not_once_per_item` keeps out, burying the refusal an
        /// operator opens the file to find.
        ///
        /// **Some of the client's profiles, for a question a change asks profile by profile**, the
        /// operator's ruling R10, and never a profile the client does not hold: a name it does not
        /// hold is left out of the question. A refusal asked of some profiles is written as the
        /// rules made it, without asking the removed ones, since restoring a removed profile
        /// changes nothing about what the profiles asked may do.
        /// </remarks>
        /// <param name="profiles">The client's profiles to ask with, or null for all of them.</param>
        public bool Reaches(
            ClientIdentity client,
            string operationId,
            AclVerb verb,
            AclReach reach,
            IAclNode node,
            IReadOnlyList<string> profiles)
        {
            return Reaches(client, operationId, verb, reach, node, profiles, null);
        }

        /// <summary>
        /// The same, with a refusal's line kept after the request's decision rather than written.
        /// </summary>
        public bool Reaches(
            ClientIdentity client,
            string operationId,
            AclVerb verb,
            AclReach reach,
            IAclNode node,
            IReadOnlyList<string> profiles,
            DecisionLines lines)
        {
            var held = client == null ? null : client.Profiles;
            var asked = profiles == null || held == null
                ? held
                : held.Where(profile => profiles.Contains(profile, StringComparer.Ordinal)).ToList();

            var decision = Answer(client, asked, verb, reach, node);
            if (decision.Permitted)
            {
                return true;
            }

            Written(client, operationId, verb, reach, node, decision, null, profiles == null, lines, false);
            return false;
        }

        /// <summary>
        /// Writes a decision down: as the rules made it, or, for a refusal the removed profiles
        /// would have permitted, as <see cref="ProfileRemoved"/>.
        /// </summary>
        /// <param name="restoring">
        /// Whether a refusal asks the removed profiles what they would have done.
        /// </param>
        /// <param name="lines">Where the line is kept, or null to write it now.</param>
        /// <param name="decides">Whether a kept line is the request's decision.</param>
        private void Written(
            ClientIdentity client,
            string operationId,
            AclVerb verb,
            AclReach reach,
            IAclNode node,
            AclDecision decision,
            string requested,
            bool restoring,
            DecisionLines lines,
            bool decides)
        {
            var name = node == null ? null : node.Name;

            AclDecision restored = null;
            var removed = decision.Permitted || !restoring ? null : Restoring(client, verb, reach, node, out restored);

            if (removed == null || restored == null)
            {
                Write(
                    client,
                    operationId,
                    name,
                    decision.DecidedAt,
                    decision.Permitted,
                    decision.Reason,
                    null,
                    requested,
                    lines,
                    decides);
            }
            else
            {
                var reason = client.ProfilesUnreadable ? ProfilesUnreadable : ProfileRemoved;
                Write(client, operationId, name, restored.DecidedAt, false, reason, removed, requested, lines, decides);
            }
        }

        /// <summary>
        /// The removed profiles that would have permitted this, or null when the client holds none
        /// or even all of them would not have.
        /// </summary>
        /// <remarks>
        /// Each name is asked on its own beside what the client still holds, so that a removed
        /// name that grants nothing here is not blamed for a refusal. One profile has to allow each
        /// question asked here, a removal's two sides together, so whenever all of them together
        /// would have permitted it, at least one removed profile alone would have.
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

            return client.Removed
                .Where(profile => Answer(
                    client, client.Profiles.Concat(new[] { profile }).ToList(), verb, reach, node).Permitted)
                .ToList();
        }

        /// <summary>
        /// Writes down a certificate this database's authority issued and the gate refused, as it
        /// asked for an operation it therefore cannot have.
        /// </summary>
        /// <remarks>
        /// **Not a decision about the rules, and written down beside them anyway.** No rule was
        /// asked, because a certificate with no admission record has no profiles to ask with. But
        /// the operator who revokes a client opens this file to see whether it keeps trying, and a
        /// revoked client hammering the gate otherwise looked exactly like a client that stopped.
        /// What is written is the key the Clients page revoked and the reason the credential check
        /// gave, and no client key, since no record stands behind the certificate to carry it:
        /// `docs/audit.md` gives the reason it is not looked for. A caller that proved nothing, or
        /// proved it to another authority, names nobody the operator issued and is not written
        /// down: see <see cref="ClientIdentity.Refusal"/>.
        /// </remarks>
        public void Refuse(ClientIdentity client, string operationId)
        {
            Refuse(client, operationId, null);
        }

        /// <summary>
        /// The same, with the line kept as the request's decision rather than written.
        /// </summary>
        /// <remarks>
        /// For a client whose record went between the connection's own check and the request's
        /// decision: that refusal is the answer the request was given, so it is the line.
        /// </remarks>
        public void Refuse(ClientIdentity client, string operationId, DecisionLines lines)
        {
            if (client == null || client.Refusal == null)
            {
                return;
            }

            Write(client, operationId, null, null, false, client.Refusal, null, null, lines, true);
        }

        /// <summary>
        /// Writes the lines a request's decisions were kept as, the decision first.
        /// </summary>
        /// <remarks>
        /// Called once the hold or the turn the decisions were made in is over, on the connection's
        /// own thread, which is the thread that pays the wait <see cref="DecisionPace"/> imposes.
        /// Each line keeps the moment it was decided at.
        /// </remarks>
        public void Write(DecisionLines lines)
        {
            if (lines == null)
            {
                return;
            }

            foreach (var line in lines.Taken())
            {
                Write(line.Entry, line.Allowance);
            }
        }

        private void Write(
            ClientIdentity client,
            string operationId,
            string node,
            string decidedAt,
            bool permitted,
            string reason,
            IReadOnlyList<string> removedProfiles,
            string requested,
            DecisionLines lines,
            bool decides)
        {
            // When it was decided, which is now and not once the wait in the writing is over, or
            // the hold a kept line was decided in: `at` is the moment the rules answered, and a
            // paced client's lines would otherwise be stamped up to seconds late, which is exactly
            // the client somebody reading them is looking at.
            var entry = new AuditEntry(
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
                Identifier(requested));

            if (lines == null)
            {
                Write(entry, Allowance(client));
            }
            else if (decides)
            {
                lines.Decided(entry, Allowance(client));
            }
            else
            {
                lines.Asked(entry, Allowance(client));
            }
        }

        private void Write(AuditEntry entry, string allowance)
        {
            // Before the line, so the wait is paid by the request that would have written it. By
            // the client key, so a renewal keeps the allowance spent; a refused certificate has no
            // client and shares one allowance with every refused certificate of its database, for
            // the reason `DecisionPace` gives. Not at all where the audit keeps nothing, since the
            // pace exists to protect a file and there is none.
            if (_audit.Keeps)
            {
                _pace.Take(allowance);
            }

            _audit.Record(entry);
        }

        /// <summary>
        /// The allowance a line about this caller is counted against, or null for a caller no
        /// line can be counted for.
        /// </summary>
        private static string Allowance(ClientIdentity client)
        {
            if (client == null)
            {
                return null;
            }

            if (client.ClientKey != null)
            {
                return client.ClientKey;
            }

            return client.Refusal != null ? DecisionPace.RefusedAllowance(client.DatabaseId) : null;
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

            // **One profile has to allow both sides**, the operator's ruling R10: a change is
            // allowed by one profile. Asked as two questions each answered by any profile, a client
            // holding one profile that may remove from the group and another that may remove the
            // node removed what neither of them may. So each profile is asked about both sides,
            // the first that allows both answers, and otherwise the first refusal is reported,
            // naming the side that refused as the line always has.
            AclDecision refusal = null;
            var each = (profiles ?? Array.Empty<string>())
                .Where(profile => !string.IsNullOrEmpty(profile))
                .Select(profile => new[] { profile });

            foreach (var alone in each)
            {
                var container = AclResolver.Decide(leaving, alone, verb, reach);
                var decision = container.Permitted ? AclResolver.Decide(node, alone, verb, reach) : container;
                if (decision.Permitted)
                {
                    return decision;
                }

                refusal = refusal ?? decision;
            }

            // No profile to ask at all, which the resolver answers in its own words.
            return refusal ?? AclResolver.Decide(leaving, profiles, verb, reach);
        }
    }
}
