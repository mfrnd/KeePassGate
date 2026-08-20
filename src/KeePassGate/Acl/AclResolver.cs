using System;
using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// One node in the tree a rule can be written on, as the resolver sees it.
    /// </summary>
    /// <remarks>
    /// A seam rather than KeePass's own types, for the reason every seam here exists: the rules can
    /// then be reasoned about, and tested, without a password manager in the room. What it needs
    /// from a group or an entry is what it holds and what is above it, and nothing else.
    /// </remarks>
    public interface IAclNode
    {
        /// <summary>The document written here, or null when nothing is.</summary>
        string Document { get; }

        /// <summary>The group above, or null at the root.</summary>
        IAclNode Parent { get; }

        /// <summary>What a log or a refusal calls this, never a title and never a value.</summary>
        string Name { get; }
    }

    /// <summary>How far into an entry an operation reaches, which a rule answers separately.</summary>
    public enum AclReach
    {
        /// <summary>The entry itself. What an operation reaches unless it says otherwise.</summary>
        Fields = 0,

        /// <summary>
        /// The attachments inside it, which needs the switch as well as the verb.
        /// </summary>
        Attachments,

        /// <summary>
        /// The values the database protects, which needs its own switch as well as the verb.
        /// </summary>
        Protected,
    }

    /// <summary>
    /// Refusals that nothing in the tree decided, for a caller that has to answer before the walk
    /// begins.
    /// </summary>
    /// <remarks>
    /// A refusal is a refusal wherever it came from, and the audit records all of them the same
    /// way. This exists so that the one place which answers before asking the rules can hand back
    /// the same kind of answer rather than a bare false.
    /// </remarks>
    public static class AclDecisions
    {
        public static AclDecision Refused(string reason)
        {
            return new AclDecision(false, reason, null);
        }
    }

    /// <summary>Why a decision came out the way it did, for the audit log.</summary>
    public sealed class AclDecision
    {
        internal AclDecision(bool permitted, string reason, string decidedAt)
        {
            Permitted = permitted;
            Reason = reason;
            DecidedAt = decidedAt;
        }

        public bool Permitted { get; private set; }

        /// <summary>A few words, and never anything a caller sent or a database holds.</summary>
        public string Reason { get; private set; }

        /// <summary>The node whose rule decided, or null when nothing did.</summary>
        public string DecidedAt { get; private set; }

        public override string ToString()
        {
            return (Permitted ? "permitted" : "refused") + ": " + Reason;
        }
    }

    /// <summary>
    /// Whether a client holding these profiles may do this to this node.
    /// </summary>
    /// <remarks>
    /// **Nearest wins, per profile.** Resolution walks from the node up to the root and stops at the
    /// first document that says anything about that profile. What it says is the answer, even when
    /// something further up said more: that is how a grant made at the root is taken away in one
    /// group without a deny syntax to get wrong.
    ///
    /// **A profile is resolved on its own, and then they are combined.** A client holding two
    /// profiles is permitted when either of them is, which is the only combination that does not
    /// depend on the order they happen to be in. Resolving them together would mean a rule about one
    /// profile silently answering for another.
    ///
    /// **How far in is asked at the same time as what.** An operation reaching an attachment needs
    /// the verb and the switch from the same rule, so a rule granting `read` without it refuses
    /// where it is found rather than sending the walk up to a rule that would have.
    ///
    /// **Everything that is not an explicit permission is a refusal.** No rule anywhere, a document
    /// that will not read, a node with no chain to a root, a verb that is not grantable: each of
    /// those denies. There is no permissive default here and there is not one anywhere else either.
    ///
    /// **This is half of the answer.** `DESIGN.md` §114 intersects it with the admission
    /// record, and `AccessCheck` is where the two are put together, before any handler runs. On its
    /// own this says what the database permits a profile, not what a particular client holds.
    ///
    /// Static because it holds nothing and decides nothing on its own account: the tree is the
    /// argument and the answer is a function of it. `JsonLinesAudit` records what came back
    /// rather than being wired in here, which is why <see cref="AclDecision"/> carries the reason
    /// instead of this carrying a logger.
    /// </remarks>
    public static class AclResolver
    {
        /// <summary>
        /// How far up the tree to walk before giving up.
        /// </summary>
        /// <remarks>
        /// A bound rather than a target. KeePass's own tree is nowhere near this deep, and a node
        /// whose parent chain is a cycle would otherwise be an unbounded walk on a request thread.
        /// </remarks>
        public const int MaxDepth = 64;

        /// <summary>
        /// What a node under this one inherits: the rule in force for every profile named at or
        /// above the node given.
        /// </summary>
        /// <remarks>
        /// The same walk `Decide` makes, stopped one question earlier. It exists for the editor,
        /// which has to show what is already granted beside what is being granted: a table listing
        /// only a group's own rules reports nothing for a group whose parent already opens it wide,
        /// and an operator reading that is being invited to grant more.
        ///
        /// For display, so unlike `Decide` it answers about every profile at once and says when it
        /// could not read something rather than refusing. Nothing authorizes anything on the
        /// strength of what comes back.
        /// </remarks>
        /// <param name="node">The parent of the node being edited, which is where its walk goes next.</param>
        public static AclInheritance Above(IAclNode node)
        {
            var grants = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
            var readable = true;

            // One level already walked: the node being edited, whose own rules the editor holds.
            // Counted from there, `MaxDepth` gives up at the same group `Decide` does rather than
            // one further up, where a grant it would show is one the resolver never reaches.
            var walked = 1;

            for (var current = node; current != null; current = current.Parent)
            {
                if (++walked > MaxDepth)
                {
                    // Where `Decide` gives up, and what it answers there: no for every profile
                    // still unanswered. What was collected below this point is still in force.
                    readable = false;
                    break;
                }

                var text = current.Document;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var document = AclDocument.Read(text);
                if (document == null)
                {
                    // Refused where it is found, exactly as `Decide` refuses it, which is only for
                    // a profile no nearer rule has already answered. So what was collected on the
                    // way up stays: those rules are nearer, `Decide` stops at them and never reads
                    // this one. Everything else is refused here, which is a seal as far as the
                    // editor can tell, and nothing further up is inherited.
                    readable = false;
                    break;
                }

                foreach (var profile in document.Profiles)
                {
                    if (!grants.ContainsKey(profile))
                    {
                        // Nearest wins, so the first rule found for a profile is its answer and
                        // anything further up about that profile is already overruled.
                        grants[profile] = document.Grant(profile);
                    }
                }

                if (document.Default == AclDefault.Deny)
                {
                    // A seal: nothing above it reaches anything under it, so nothing above it is
                    // listed as inherited.
                    break;
                }
            }

            return new AclInheritance(grants, readable);
        }

        /// <summary>Whether these profiles may do this to this entry's fields.</summary>
        public static bool Permits(IAclNode node, IEnumerable<string> profiles, AclVerb verb)
        {
            return Decide(node, profiles, verb, AclReach.Fields).Permitted;
        }

        /// <summary>Whether these profiles may do this, as far in as that.</summary>
        public static bool Permits(IAclNode node, IEnumerable<string> profiles, AclVerb verb, AclReach reach)
        {
            return Decide(node, profiles, verb, reach).Permitted;
        }

        /// <summary>The same question about an entry's fields, with what answered it.</summary>
        public static AclDecision Decide(IAclNode node, IEnumerable<string> profiles, AclVerb verb)
        {
            return Decide(node, profiles, verb, AclReach.Fields);
        }

        /// <summary>The same question, with what answered it.</summary>
        public static AclDecision Decide(
            IAclNode node, IEnumerable<string> profiles, AclVerb verb, AclReach reach)
        {
            if (node == null)
            {
                return new AclDecision(false, "there is nothing to decide about", null);
            }

            if (!AclVerbs.IsGrantable(verb) || AclVerbs.IsScope(verb))
            {
                // `None` is what an operation that touches no data declares, and no rule grants it.
                // `Attachments` is a scope rather than something to do, so it is not a question
                // either. Both are asking the wrong thing, and the answer to that is no.
                return new AclDecision(false, "that is not something a rule grants", null);
            }

            if (profiles == null)
            {
                return new AclDecision(false, "the client holds no profiles", null);
            }

            var held = new List<string>();
            foreach (var profile in profiles)
            {
                if (!string.IsNullOrEmpty(profile))
                {
                    held.Add(profile);
                }
            }

            if (held.Count == 0)
            {
                return new AclDecision(false, "the client holds no profiles", null);
            }

            AclDecision refusal = null;

            foreach (var profile in held)
            {
                var decision = Decide(node, profile, verb, reach);
                if (decision.Permitted)
                {
                    return decision;
                }

                // The first refusal is the one reported, so that a client with several profiles
                // gets an account of why rather than of the last one tried.
                refusal = refusal ?? decision;
            }

            return refusal;
        }

        private static AclDecision Decide(IAclNode node, string profile, AclVerb verb, AclReach reach)
        {
            var walked = 0;

            for (var current = node; current != null; current = current.Parent)
            {
                if (++walked > MaxDepth)
                {
                    return new AclDecision(false, "the tree is deeper than this walks", current.Name);
                }

                var text = current.Document;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var document = AclDocument.Read(text);
                if (document == null)
                {
                    // Refused rather than skipped. A document that will not read is a rule somebody
                    // wrote and this cannot honor, and carrying on up the tree would answer with a
                    // permission that the node in front of it may have been taking away.
                    return new AclDecision(false, "a rule here could not be read", current.Name);
                }

                var grant = document.Grant(profile);
                if (grant != null)
                {
                    // The nearest rule about this profile is the answer, so a rule that grants the
                    // verb but not the scope refuses here rather than sending the walk on to find
                    // a rule further up that would.
                    if (!grant.Holds(verb))
                    {
                        return new AclDecision(false, "a rule here does not grant it", current.Name);
                    }

                    // Neither switch is asked about for a removal. A switch says how far the verbs
                    // a rule grants see or change inside an entry, and removing something shows
                    // nobody anything. A rule that may remove an entry already takes every
                    // attachment and every protected value with it, so requiring a scope before
                    // one of them can be stripped on its own would forbid the smaller act while
                    // allowing the larger one.
                    if (verb != AclVerb.Strip && verb != AclVerb.Delete)
                    {
                        if (reach == AclReach.Attachments && !grant.CoversAttachments)
                        {
                            return new AclDecision(
                                false, "a rule here does not reach attachments", current.Name);
                        }

                        if (reach == AclReach.Protected && !grant.CoversProtected)
                        {
                            return new AclDecision(
                                false, "a rule here does not reach protected values", current.Name);
                        }
                    }

                    return new AclDecision(true, "a rule here grants it", current.Name);
                }

                if (document.Default == AclDefault.Deny)
                {
                    return new AclDecision(false, "a rule here refuses anything it does not name", current.Name);
                }
            }

            return new AclDecision(false, "no rule anywhere grants it", null);
        }
    }
}
