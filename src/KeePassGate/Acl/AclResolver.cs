using System;
using System.Collections.Generic;
using System.Linq;

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
        /// <summary>
        /// The rules of this node in the reading its tree was read with: `NodeRule.None` where
        /// that reading holds none for it, and never null.
        /// </summary>
        NodeRule Rule { get; }

        /// <summary>The group above, or null at the root.</summary>
        IAclNode Parent { get; }

        /// <summary>What a log or a refusal calls this, never a title and never a value.</summary>
        string Name { get; }

        /// <summary>
        /// What <paramref name="walk"/> returned, with whatever this node lives in held still while
        /// it read this node and those above it.
        /// </summary>
        /// <remarks>
        /// One walk is one read: a node in KeePass's tree is read on a connection thread while
        /// KeePass's own thread may be moving it, so the resolver asks for the whole climb at once
        /// rather than level by level. A node with nothing to hold runs the walk where it is.
        /// </remarks>
        T Held<T>(Func<T> walk);
    }

    /// <summary>How far into an entry an operation reaches, which a rule answers separately.</summary>
    public enum AclReach
    {
        /// <summary>
        /// The node itself, a group or an entry with its fields. What an operation reaches unless
        /// it declares otherwise.
        /// </summary>
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
            : this(permitted, reason, decidedAt, null)
        {
        }

        internal AclDecision(
            bool permitted, string reason, string decidedAt, IReadOnlyList<KeyValuePair<string, AclGrant>> grants)
        {
            Permitted = permitted;
            Reason = reason;
            DecidedAt = decidedAt;
            Grants = grants;
        }

        public bool Permitted { get; private set; }

        /// <summary>A few words, and never anything a caller sent or a database holds.</summary>
        public string Reason { get; private set; }

        /// <summary>The node whose rule decided, or null when nothing did.</summary>
        public string DecidedAt { get; private set; }

        /// <summary>
        /// For a question about every verb at once, what each profile asked about is granted at the
        /// node, in the order they were asked about. Null for a question about one verb, and for one
        /// that never reached a node.
        /// </summary>
        /// <remarks>
        /// Carried on the decision rather than asked for again, because asked again it could
        /// disagree: a tree that moved in between would let a request the rules permitted be
        /// answered with grants that name nothing, or one they refused be answered at all.
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, AclGrant>> Grants { get; private set; }

        public override string ToString()
        {
            return (Permitted ? "permitted" : "refused") + ": " + Reason;
        }
    }

    /// <summary>
    /// Whether a client holding these profiles may do this to this node.
    /// </summary>
    /// <remarks>
    /// **A single walk, `STORE.md` §617, for every question.** From the node asked about upward,
    /// for a single profile, with a limit that starts as every letter. At each node: a row that
    /// refuses, for a lost version or a text out of form, gives nothing and ends the walk; a row
    /// whose every standing version grants gives the letters all of them hold, cut to the limit,
    /// and ends the walk; otherwise a switch that refuses gives nothing and ends the walk;
    /// otherwise the node lets through, and the limit is cut to what the node's versions recorded.
    /// Above the root nothing arrives. Every question, `Decide`, `DecideAny`, `Arrives` and
    /// `Above`, is answered from that walk and from nothing else, so no entry point can skip a cut.
    ///
    /// **Nearest wins, per profile.** The walk stops at the first row that answers for the
    /// profile. What it grants is the answer, even when something further up granted more: that is
    /// how a grant made at the root is taken away in a group without a deny syntax to get wrong.
    ///
    /// **Only ever cut.** The limit and the granted letters are whole masks, and the walk only
    /// intersects them, so the answer for a profile is never wider than any standing version on its
    /// way allows, a verb never stands beside a switch that no single mask held beside it, and the
    /// order in which versions are met changes nothing.
    ///
    /// **A profile is resolved on its own, and then they are combined.** A client holding two
    /// profiles is permitted when either of them is, which is the only combination that does not
    /// depend on the order they happen to be in. Resolving them together would mean a rule about one
    /// profile silently answering for another. That is one question; a request asking two is put
    /// together above this, `AccessCheck` asking a removal's two sides of one profile at a time
    /// and the strip guard in `VaultHandlers` asking each profile on its own.
    ///
    /// **How far in is asked at the same time as what.** An operation reaching an attachment needs
    /// the verb and the switch from the same rule, so a rule granting `read` without it refuses
    /// where it is found rather than sending the walk up to a rule that would have.
    ///
    /// **Everything that is not an explicit permission is a refusal.** No rule anywhere, a rule
    /// lost or out of form, a node with no chain to a root, a verb that is not grantable: each of
    /// those denies. There is no permissive default here and there is not one anywhere else either.
    ///
    /// **This is half of the answer.** `DESIGN.md` §114 intersects it with the admission
    /// record, and `AccessCheck` is where the two are put together, before any handler runs. On its
    /// own this answers what the database permits a profile, not what a particular client holds.
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

        private const string Lost = "a rule here was lost";
        private const string OutOfForm = "a rule here could not be read";
        private const string MadeApart = "rules here made apart do not all grant it";
        private const string Limited = "a limit here does not let it through";
        private const string Unnamed = "a rule here refuses anything it does not name";
        private const string NoRule = "no rule anywhere grants it";
        private const string TooDeep = "the tree is deeper than this walks";

        /// <summary>How many levels a walk from a node's parent has already walked: the node itself.</summary>
        private const int Edited = 1;

        /// <summary>
        /// What a node under this one inherits: for every profile whose walk from the node given
        /// ends at a row, what that walk gives.
        /// </summary>
        /// <remarks>
        /// The same walk `Decide` makes, stopped one question earlier. It exists for the editor,
        /// which has to show what is already granted beside what is being granted: a table listing
        /// only a group's own rules reports nothing for a group whose parent already opens it wide,
        /// and an operator reading that is being invited to grant more.
        ///
        /// A profile whose walk ends at a row that refuses, a lost version or a text out of form,
        /// is listed as granted nothing, since a row stands for it there. A profile whose walk ends
        /// at a switch that refuses, or above the root, is not listed: nothing above names it on
        /// the way. Each letter listed is what `Arrives` answers, every cut on the way taken.
        ///
        /// For display, so unlike `Decide` it answers about every profile at once. Nothing
        /// authorizes anything on the strength of what comes back.
        /// </remarks>
        /// <param name="node">The parent of the node being edited, which is where its walk goes next.</param>
        public static AclInheritance Above(IAclNode node)
        {
            return node == null ? AclInheritance.None : node.Held(() => Inherited(node));
        }

        private static AclInheritance Inherited(IAclNode node)
        {
            var grants = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
            var named = new HashSet<string>(StringComparer.Ordinal);

            // One level already walked: the node being edited, whose own rules the editor holds.
            // Counted from there, `MaxDepth` gives up at the same group `Decide` does rather than
            // one further up, where a grant it would show is one the resolver never reaches.
            var walked = Edited;
            var tooDeep = false;

            for (var current = node; current != null; current = current.Parent)
            {
                if (++walked > MaxDepth)
                {
                    tooDeep = true;
                    break;
                }

                if (current.Rule != null)
                {
                    named.UnionWith(current.Rule.Profiles);
                }
            }

            foreach (var profile in named)
            {
                var walk = Walked(node, profile, Edited);
                if (walk.Row != null)
                {
                    grants[profile] = walk.Letters;
                }
            }

            return new AclInheritance(grants, tooDeep);
        }

        /// <summary>
        /// The letters a node directly under <paramref name="above"/> receives for a profile: what
        /// the walk from <paramref name="above"/> gives the profile, every verb and switch together
        /// and cut by each limit on the way; no letter where the walk refuses or meets no rule, and
        /// none where <paramref name="above"/> is null.
        /// </summary>
        public static AclGrant Arrives(IAclNode above, string profile)
        {
            return above == null ? AclGrant.Nothing : above.Held(() => Walked(above, profile, Edited).Letters);
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

        /// <summary>
        /// What each of these profiles is granted at this node, and whether any of them is granted
        /// anything there at all.
        /// </summary>
        /// <remarks>
        /// **The same walk every other question takes, asked one profile, one verb and one switch
        /// at a time.** What it reports for a profile is therefore what that profile's own question
        /// about each verb at this node would be answered, and nothing new decides here. It is the
        /// node's question alone: a removal also asks the group the node is leaving, and no
        /// operation strips a group, so a `D` or an `S` reported here can be a grant no operation
        /// can use at this node. That is what the KPG tab shows for the node's rule, and the answer
        /// is what the rules grant here rather than what would succeed.
        ///
        /// **Per profile, because a switch belongs to its rule.** A switch sets how far the verbs of
        /// the same rule reach, so one profile's `read` and another's `protected` do not add up to
        /// reading a protected value, and one grant made of both would claim exactly that. Each
        /// profile is answered on its own, as the router combines them: one is enough by itself,
        /// and none is ever completed by another.
        ///
        /// A switch is reported where it reaches a verb the same profile is granted there and a
        /// switch can qualify, which is every verb but the two removals. Those take an entry's
        /// attachments and protected values without asking, so beside them alone a switch reaches
        /// nothing further.
        ///
        /// **Permitted when some profile is granted some verb**, which is what an operation
        /// declaring `any` needs, and then answered with the first grant found, so the node it
        /// names holds a rule that does grant something. Refused otherwise, with the first
        /// refusal, for the reason a question about one verb reports the first one: an account of
        /// why rather than of the last thing tried.
        /// </remarks>
        public static AclDecision DecideAny(IAclNode node, IEnumerable<string> profiles)
        {
            if (node == null)
            {
                return new AclDecision(false, "there is nothing to decide about", null);
            }

            return node.Held(() => Every(node, profiles));
        }

        private static AclDecision Every(IAclNode node, IEnumerable<string> profiles)
        {
            // Once each, because each is a member of the answer, and named, as everywhere.
            var held = new List<string>();
            foreach (var profile in (profiles ?? Array.Empty<string>())
                         .Where(profile => !string.IsNullOrEmpty(profile) && !held.Contains(profile)))
            {
                held.Add(profile);
            }

            if (held.Count == 0)
            {
                return new AclDecision(false, "the client holds no profiles", null);
            }

            var grants = new List<KeyValuePair<string, AclGrant>>();
            AclDecision granted = null;
            AclDecision refusal = null;

            foreach (var profile in held)
            {
                var verbs = new List<AclVerb>();

                foreach (var verb in AclVerbs.Grantable)
                {
                    if (AclVerbs.IsScope(verb))
                    {
                        continue;
                    }

                    var decision = Decide(node, profile, verb, AclReach.Fields);
                    if (decision.Permitted)
                    {
                        verbs.Add(verb);
                        granted = granted ?? decision;
                    }
                    else
                    {
                        refusal = refusal ?? decision;
                    }
                }

                var holds = new HashSet<AclVerb>(verbs);

                if (Reaches(node, profile, verbs, AclReach.Attachments))
                {
                    holds.Add(AclVerb.Attachments);
                }

                if (Reaches(node, profile, verbs, AclReach.Protected))
                {
                    holds.Add(AclVerb.Protected);
                }

                grants.Add(new KeyValuePair<string, AclGrant>(profile, new AclGrant(holds)));
            }

            var answer = granted ?? refusal;
            return new AclDecision(granted != null, answer.Reason, answer.DecidedAt, grants.AsReadOnly());
        }

        /// <summary>
        /// Whether a switch reaches one of these verbs, all granted to this profile here, that a
        /// switch can qualify.
        /// </summary>
        private static bool Reaches(IAclNode node, string profile, IEnumerable<AclVerb> verbs, AclReach reach)
        {
            foreach (var verb in verbs)
            {
                if (verb != AclVerb.Strip && verb != AclVerb.Delete
                    && Decide(node, profile, verb, reach).Permitted)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The same question, with what answered it.</summary>
        public static AclDecision Decide(IAclNode node, IEnumerable<string> profiles, AclVerb verb, AclReach reach)
        {
            if (node == null)
            {
                return new AclDecision(false, "there is nothing to decide about", null);
            }

            if (!AclVerbs.IsGrantable(verb) || AclVerbs.IsScope(verb))
            {
                // `None` is what an operation that touches no data declares, and no rule grants it.
                // `Any` is every verb at once, which `DecideAny` answers profile by profile and
                // never as one question here. `Attachments` and `Protected` are switches rather
                // than something to do, so they are not a question either. All four are asking the
                // wrong thing, and the answer to that is no.
                return new AclDecision(false, "that is not something a rule grants", null);
            }

            if (profiles == null)
            {
                return new AclDecision(false, "the client holds no profiles", null);
            }

            var held = new List<string>();
            foreach (var profile in profiles.Where(profile => !string.IsNullOrEmpty(profile)))
            {
                held.Add(profile);
            }

            if (held.Count == 0)
            {
                return new AclDecision(false, "the client holds no profiles", null);
            }

            return node.Held(() => First(node, held, verb, reach));
        }

        private static AclDecision First(IAclNode node, IEnumerable<string> held, AclVerb verb, AclReach reach)
        {
            AclDecision refusal = null;

            foreach (var decision in held.Select(profile => Decide(node, profile, verb, reach)))
            {
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

        /// <summary>A single profile's question, answered from its walk.</summary>
        private static AclDecision Decide(IAclNode node, string profile, AclVerb verb, AclReach reach)
        {
            var walk = Walked(node, profile, 0);
            var at = walk.At == null ? null : walk.At.Name;
            if (walk.Row == null || walk.Row.Refuses)
            {
                return new AclDecision(false, walk.Reason, at);
            }

            // The nearest row about this profile is the answer, so a row that grants the verb but
            // not the switch refuses here rather than sending the walk on to find a rule further
            // up that would.
            var row = walk.Row;
            if (!row.Grants.Holds(verb))
            {
                return new AclDecision(false, Apart(row, "a rule here does not grant it", verb, null), at);
            }

            // Neither switch is asked about for a removal. A switch sets how far the verbs a rule
            // grants see or change inside an entry, and removing something shows nobody anything.
            // A rule that may remove an entry already takes every attachment and every protected
            // value with it, so requiring a scope before one of them can be stripped on its own
            // would forbid the smaller act while allowing the larger one.
            AclVerb? scope = null;
            if (verb != AclVerb.Strip && verb != AclVerb.Delete)
            {
                if (reach == AclReach.Attachments)
                {
                    scope = AclVerb.Attachments;
                }
                else if (reach == AclReach.Protected)
                {
                    scope = AclVerb.Protected;
                }
            }

            if (scope != null && !row.Grants.Holds(scope.Value))
            {
                var unreached = scope == AclVerb.Attachments
                    ? "a rule here does not reach attachments"
                    : "a rule here does not reach protected values";
                return new AclDecision(false, Apart(row, unreached, verb, scope), at);
            }

            // Granted where the row stands, and cut on the way to it: the node that took the
            // letters away is what decided, a node whose version recorded what arrived among them.
            if (!walk.Letters.Holds(verb) || (scope != null && !walk.Letters.Holds(scope.Value)))
            {
                return new AclDecision(false, Limited, walk.CutAt(verb, scope) ?? at);
            }

            return new AclDecision(true, "a rule here grants it", at);
        }

        /// <summary>
        /// Why a row that answered does not grant this: a reason of its own where versions made
        /// apart stand and a single one of them would have granted it.
        /// </summary>
        private static string Apart(RowRule row, string reason, AclVerb verb, AclVerb? scope)
        {
            var some = row.Versions.Any(version =>
                version.Letters.Holds(verb) && (scope == null || version.Letters.Holds(scope.Value)));
            return row.Versions.Count > 1 && some ? MadeApart : reason;
        }

        /// <summary>
        /// The walk of `STORE.md` §617 for a single profile, from <paramref name="node"/> upward,
        /// with <paramref name="walked"/> levels already counted against <see cref="MaxDepth"/>.
        /// </summary>
        private static Walk Walked(IAclNode node, string profile, int walked)
        {
            var walk = new Walk();
            var limit = AclGrant.Every;

            for (var current = node; current != null; current = current.Parent)
            {
                if (++walked > MaxDepth)
                {
                    return walk.Ended(AclGrant.Nothing, current, TooDeep, null);
                }

                // Never null; one that is would be a node whose rules nobody could read, and that
                // refuses where it is found rather than being walked past.
                var rule = current.Rule;
                if (rule == null)
                {
                    return walk.Ended(AclGrant.Nothing, current, OutOfForm, null);
                }

                var row = rule.Row(profile);
                if (row.Refuses)
                {
                    return walk.Ended(AclGrant.Nothing, current, row.Lost.Count > 0 ? Lost : OutOfForm, row);
                }

                if (row.Answers)
                {
                    return walk.Ended(row.Grants.Cut(limit), current, null, row);
                }

                var sw = rule.Switch;
                if (sw.Refuses)
                {
                    var reason = sw.Lost.Count > 0
                        ? Lost
                        : sw.Versions.Any(version => version.Kind == SwitchVersionKind.OutOfForm) ? OutOfForm : Unnamed;
                    return walk.Ended(AclGrant.Nothing, current, reason, null);
                }

                // The node lets through: what arrives is cut to what its row's versions and its
                // switch's versions recorded for the profile, and the walk goes on.
                var cut = limit.Cut(rule.Limit(profile));
                if (!cut.SameAs(limit))
                {
                    walk.Cuts.Add(new KeyValuePair<IAclNode, AclGrant>(current, cut));
                }

                limit = cut;
            }

            return walk.Ended(AclGrant.Nothing, null, NoRule, null);
        }

        /// <summary>Where a walk ended, what it gives there, and each node on the way that cut it.</summary>
        private sealed class Walk
        {
            /// <summary>What the walk gives the profile: no letter for a refusal and above the root.</summary>
            public AclGrant Letters { get; private set; }

            /// <summary>The node the walk ended at, or null where it went past the root.</summary>
            public IAclNode At { get; private set; }

            /// <summary>Why it ended there, where it ended for want of a row that answers.</summary>
            public string Reason { get; private set; }

            /// <summary>The row the walk ended at, refusing or answering; null where it ended otherwise.</summary>
            public RowRule Row { get; private set; }

            /// <summary>Each node that cut what arrives, nearest first, with the limit left after it.</summary>
            public List<KeyValuePair<IAclNode, AclGrant>> Cuts { get; } = new List<KeyValuePair<IAclNode, AclGrant>>();

            public Walk Ended(AclGrant letters, IAclNode at, string reason, RowRule row)
            {
                Letters = letters;
                At = at;
                Reason = reason;
                Row = row;
                return this;
            }

            /// <summary>The nearest node whose cut took this verb, or this verb with this switch, away.</summary>
            public string CutAt(AclVerb verb, AclVerb? scope)
            {
                foreach (var cut in Cuts)
                {
                    if (!cut.Value.Holds(verb) || (scope != null && !cut.Value.Holds(scope.Value)))
                    {
                        return cut.Key.Name;
                    }
                }

                return null;
            }
        }
    }
}
