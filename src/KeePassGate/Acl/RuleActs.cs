using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>Why a change an OK on the KPG tab handed over was not written.</summary>
    public enum RuleLeftReason
    {
        /// <summary>A version would not fit the longest string the plugin writes, `STORE.md` §643.</summary>
        TooLong = 0,

        /// <summary>The change is none the tab hands over: a name the issuer could not issue, no letters, versions the tab did not show, or a node no rule is written at.</summary>
        OutOfForm,

        /// <summary>The store as the write reads it does not let the press be written: a limit settled where the switch refuses, or versions removed that leave none to write again.</summary>
        NotAllowed,

        /// <summary>The store holds no key, or cannot be read as a whole, so no rule is written.</summary>
        Unread,

        /// <summary>
        /// A version below that the write above would have written anew, left because the tab did
        /// not hold, at every press concerning its profile, every version standing at its node for
        /// that profile, `STORE.md` §617.
        /// </summary>
        NotHeld,
    }

    /// <summary>
    /// A change of an OK on the KPG tab that was not written, or a version it would have written
    /// anew that was left as it was, and why: what the editor tells the operator of, `STORE.md`
    /// §629.
    /// </summary>
    public sealed class RuleLeft
    {
        /// <summary>A change of this profile's row at this node, or of the node's switch where the profile is null, not written for this reason.</summary>
        /// <param name="node">The node the change or the version is at, as the OK handed it over.</param>
        /// <param name="pressed">Whether the change is the operator's own press, not a version written anew for another.</param>
        public RuleLeft(string node, string profile, bool pressed, RuleLeftReason reason)
        {
            Node = node;
            Profile = profile;
            Pressed = pressed;
            Reason = reason;
        }

        /// <summary>
        /// The node the change or the version is at, as 32 lowercase hexadecimal characters: the
        /// dialog's node for a press of the tab and for a version written anew there, and a node
        /// below it for a version the write above would have renewed.
        /// </summary>
        public string Node { get; private set; }

        /// <summary>The profile of the row, or null for the switch.</summary>
        public string Profile { get; private set; }

        /// <summary>
        /// Whether the change is the operator's own press, which the message after the dialog's
        /// close tells of beside the log, rather than a version written anew for another press,
        /// which the log alone names.
        /// </summary>
        public bool Pressed { get; private set; }

        /// <summary>Why it was not written.</summary>
        public RuleLeftReason Reason { get; private set; }
    }

    /// <summary>
    /// The act an OK on the KPG tab of a node's dialog writes, `STORE.md` §617 and §629: composed
    /// over the reading the write is planned against, under that reading's store key.
    /// </summary>
    /// <remarks>
    /// **Composed over the write's reading, from what the tab showed.** What the store holds at the
    /// node is read from the reading the write is planned against, so a row is written only where
    /// that reading does not give it already from a single standing version, and only a version
    /// that reading holds standing is written anew. What a version names is what the tab showed at
    /// the press, so a version that reached the database while the dialog stood is named by nothing
    /// and stands beside the new version, `STORE.md` §629.
    ///
    /// **A mask recorded is what the tab showed arriving, cut to what arrives now**: never more
    /// than the operator saw, and never more than the node's parent gives in the reading the write
    /// is composed over. A narrowing above that reached the database while the dialog stood is
    /// recorded, and not recorded away: a mask recorded wider than what arrived would let the
    /// narrowing be undone by a widening made apart, once copies meet. A mask cut to nothing is
    /// `off:` with no letter, which follows what arrives later, and stays apart from `off:deny`.
    ///
    /// **A single version for each version replaced.** The switch as the OK leaves it decides what
    /// a rule taken off records, whichever press came first, and so does the switch the tab showed
    /// at the press: a rule taken off where the tab showed the box ticked, and no press on the tab
    /// cleared it, is `off:deny` whatever reached the store since. A version that two presses write
    /// anew is written once, recording what both presses showed arriving, and a row pressed is
    /// written once whatever the box wrote anew for it; the box writes anew every other rule taken
    /// off that it reaches.
    ///
    /// **Every change not written is told, and takes nothing else with it.** A change out of form,
    /// a press the store as the write reads it no longer allows, a version that does not fit and a
    /// store that cannot be read are each left out and handed back with why, and the rest of the
    /// OK is written; nothing at all to write is an act of null with nothing handed back.
    ///
    /// **Nothing named of another object.** A name the tab hands over that the reading carries as
    /// a version of another object, or that cannot be a name of the object by its first bytes,
    /// `STORE.md` §609, is not named.
    ///
    /// **The ceiling of a string**, `STORE.md` §643: a version whose content or the item restating
    /// it would not fit is left as it was and reported, and the rest is composed again as though
    /// it had not been pressed, so that nothing written depends on a version that was left.
    ///
    /// **The write above**, `STORE.md` §617: once the versions at the node are settled, the same
    /// act renews each version below the node that was following it, in the same reading, so a
    /// change above reaches the nodes below that let it through without the operator visiting
    /// each, and never a node that was being held back. "Before" is the reading without the act,
    /// and "after" is the reading with the act's versions and the renewals made so far laid over
    /// it, so a version that reached the database under the dialog is judged as it stands at the
    /// OK. Only versions the tab's reading held at the press are renewed: for a profile, none at a
    /// node where a version of the profile's row or of the node's switch stands that the tab did
    /// not hold at every press concerning the profile, and such a node is handed back, as no
    /// press, so that the log names why the change above did not reach it.
    ///
    /// **The rules are worked out once for the act**, and the node's parent and the tree are made
    /// from them: the reading's objects are walked a single time whatever the act asks.
    /// </remarks>
    internal static class RuleActs
    {
        /// <summary>The length of a node's identifier as an object names it, and of a version's identifier.</summary>
        private const int NodeLength = 32;

        /// <summary>
        /// The act these changes make at the node, given as the 32 lowercase hexadecimal characters
        /// of its identifier, composed over this reading under its store key, or null where nothing
        /// is written; each change not written, and each version left as it was, in
        /// <paramref name="left"/> with why.
        /// </summary>
        /// <param name="above">
        /// The node's parent holding the rules it is handed, which are this reading's: every mask
        /// recorded is cut to what it gives. Null for the root, above which nothing arrives, and
        /// a parent made null likewise. Walked where it is asked, inside the caller's turn, once
        /// for each profile a mask is recorded for.
        /// </param>
        /// <param name="tree">
        /// The tree as it stands in the caller's turn, which the write above is carried down, made
        /// from the rules it is handed, which are this reading's; asked for only where the act
        /// changes what the node gives a profile. Null for a node with nothing below it.
        /// </param>
        public static StoreAct Of(
            StoreReading reading,
            string node,
            Func<StoredRules, IAclNode> above,
            Func<StoredRules, IRuledTree> tree,
            RuleChanges changes,
            out IReadOnlyList<RuleLeft> left)
        {
            left = Array.Empty<RuleLeft>();
            if (changes == null || changes.IsEmpty)
            {
                return null;
            }

            var inForce = reading == null ? null : reading.InForce;
            if (inForce == null || reading.State != StoreState.Whole)
            {
                left = Every(node, changes, RuleLeftReason.Unread);
                return null;
            }

            if (!IsNode(node) || Owned(reading, node))
            {
                left = Every(node, changes, RuleLeftReason.OutOfForm);
                return null;
            }

            var key = (byte[])inForce.Key.Bytes.Clone();
            try
            {
                var rules = StoredRules.Of(reading);
                var composing = new Composing(inForce, key, node, above == null ? null : above(rules), rules.At(node), changes);
                var records = composing.Records().ToList();
                records.AddRange(composing.Below(rules, tree, records));
                left = composing.Left;
                return records.Count == 0
                    ? null
                    : new StoreAct("the rules at " + node.Substring(0, 8), key, inForce.Key.Name, records, null);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>Each change at the node, of every row and of the box, told as not written for this reason.</summary>
        private static IReadOnlyList<RuleLeft> Every(string node, RuleChanges changes, RuleLeftReason reason)
        {
            var every = changes.Rows.Select(change => new RuleLeft(node, change.Profile, true, reason)).ToList();
            if (changes.Switch != null)
            {
                every.Add(new RuleLeft(node, null, true, reason));
            }

            return every.AsReadOnly();
        }

        /// <summary>
        /// Whether the tab can hand over a change: a usable name, the letters its press needs, and
        /// versions removed that the tab showed, standing or lost, with something of the row left.
        /// </summary>
        private static bool InForm(RowChange change)
        {
            if (!AuthorityIssuer.IsUsableProfileName(change.Profile))
            {
                return false;
            }

            switch (change.Kind)
            {
                case RowChangeKind.Set:
                case RowChangeKind.TakeOff:
                case RowChangeKind.Settle:
                    return change.Letters != null;
                case RowChangeKind.RemoveVersions:
                    // The X on the last of a row's versions takes the rule off, so a removal that
                    // leaves nothing of the row is none the tab hands over.
                    var row = change.Shown.Row(change.Profile);
                    var shown = new HashSet<string>(row.Versions.Select(version => version.Id).Concat(row.Lost), StringComparer.Ordinal);
                    return change.Removed.Count > 0
                           && change.Removed.All(shown.Contains)
                           && shown.Any(id => !change.Removed.Contains(id, StringComparer.Ordinal));
                default:
                    return false;
            }
        }

        /// <summary>Whether this is 32 lowercase hexadecimal characters.</summary>
        private static bool IsNode(string node)
        {
            return node != null && node.Length == NodeLength && node.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        /// <summary>Whether the node is the plugin's own: its group, or an entry carrying a string of the store.</summary>
        private static bool Owned(StoreReading reading, string node)
        {
            return StoreFormat.IsGroupIdentifier(Bytes(node))
                   || reading.Candidates.Any(candidate => string.Equals(candidate.Hex, node, StringComparison.Ordinal));
        }

        private static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var at = 0; at < bytes.Length; at++)
            {
                bytes[at] = Convert.ToByte(hex.Substring(at * 2, 2), 16);
            }

            return bytes;
        }

        /// <summary>The text a standing version of a row holds, composed again from what it reads as.</summary>
        private static string Text(RowVersion version)
        {
            switch (version.Kind)
            {
                case RowVersionKind.Grants:
                    return RuleText.Row(version.Letters);
                case RowVersionKind.TakenOff:
                    return RuleText.TakenOff(version.Letters);
                case RowVersionKind.TakenOffRefusing:
                    return RuleText.TakenOffRefusing;
                default:
                    return null;
            }
        }

        private static bool TakesOff(RowVersion version)
        {
            return version.Kind == RowVersionKind.TakenOff || version.Kind == RowVersionKind.TakenOffRefusing;
        }

        /// <summary>
        /// The act composed for a reading, a node and the changes, each version that does not fit
        /// left out and the rest composed again without it until every version left in fits.
        /// </summary>
        private sealed class Composing
        {
            private readonly StoreInForce _inForce;
            private readonly byte[] _key;
            private readonly string _node;
            private readonly NodeRule _stands;
            private readonly RuleChanges _changes;
            private readonly IReadOnlyList<RowChange> _rows;
            private readonly IAclNode _above;
            private readonly Dictionary<string, AclGrant> _arrives = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
            private readonly List<RuleLeft> _left = new List<RuleLeft>();
            private readonly HashSet<string> _leftKeys = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _places = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _profileTags = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>The tag of `version` under the key, as hexadecimal, once worked out.</summary>
            private string _versionTag;

            public Composing(StoreInForce inForce, byte[] key, string node, IAclNode above, NodeRule stands, RuleChanges changes)
            {
                _above = above;
                _inForce = inForce;
                _key = key;
                _node = node;
                _stands = stands;
                _changes = changes;
                _rows = changes.Rows.Where(InForm).ToList();
                foreach (var change in changes.Rows.Where(change => !InForm(change)))
                {
                    Report(Draft.RowKey(change.Profile), change.Profile, true, RuleLeftReason.OutOfForm);
                }
            }

            /// <summary>Each change not written and each version left as it was, in the order they were found.</summary>
            public IReadOnlyList<RuleLeft> Left
            {
                get { return _left.AsReadOnly(); }
            }

            /// <summary>The records of the act, every record fitting the ceiling.</summary>
            public IReadOnlyList<StoreRecord> Records()
            {
                while (true)
                {
                    var records = new List<StoreRecord>();
                    var unfit = false;
                    foreach (var draft in Drafts())
                    {
                        var record = draft.Compose(_key, _node);
                        if (Fits(record))
                        {
                            records.Add(record);
                            continue;
                        }

                        Report(draft.Key, draft.Profile, draft.Pressed, RuleLeftReason.TooLong);
                        unfit = true;
                    }

                    if (!unfit)
                    {
                        return records;
                    }
                }
            }

            /// <summary>
            /// The renewals of the write above, `STORE.md` §617: each standing version below the
            /// node, the nearer first, that let a profile through and followed what arrived for it
            /// before the act, written anew with what arrives after, where what arrives changed,
            /// the tab kept it, and nothing stands beside it that the tab did not keep.
            /// </summary>
            /// <param name="rules">The rules of the reading the act was composed over: "before".</param>
            /// <param name="tree">The tree below the node, made from those rules where it is asked for.</param>
            /// <param name="act">The versions settled at the node, which "after" lays over the reading.</param>
            /// <remarks>
            /// **"After" grows as the nodes are walked**: a node is renewed against what arrives
            /// with the act and every renewal made above it, so a group renewed passes on what it
            /// lets through, and a group left passes on what it still cuts to. Nodes are taken by
            /// their distance below the node, and by identifier within a distance, and a renewal
            /// changes what arrives at no node of its own distance or nearer, so the order the tree
            /// hands the nodes over in, and the order of the profiles, change nothing.
            ///
            /// **The tree is asked for only where the act changes what the node gives a profile**,
            /// and working that out asks nothing of the tree, the node's parent being the parent
            /// handed over.
            ///
            /// **A renewal that does not fit is left** and told as a renewal, `STORE.md` §643: the
            /// version goes on cutting, which is narrower, and the nodes below it receive what it
            /// still lets through.
            /// </remarks>
            public IReadOnlyList<StoreRecord> Below(StoredRules rules, Func<StoredRules, IRuledTree> tree, IReadOnlyList<StoreRecord> act)
            {
                var renewals = new List<StoreRecord>();
                if (tree == null || act.Count == 0)
                {
                    return renewals;
                }

                var laid = new Dictionary<string, List<StoreRecord>>(StringComparer.Ordinal) { { _node, act.ToList() } };
                var laidRules = new Dictionary<string, NodeRule>(StringComparer.Ordinal);
                Func<string, NodeRule> after = at =>
                {
                    NodeRule rule;
                    List<StoreRecord> held;
                    if (!laid.TryGetValue(at, out held))
                    {
                        return rules.At(at);
                    }

                    if (!laidRules.TryGetValue(at, out rule))
                    {
                        rule = rules.At(at, held);
                        laidRules[at] = rule;
                    }

                    return rule;
                };

                // Only a profile for which what the node gives changed can arrive otherwise below
                // it: what arrives at a node below depends on the node only through what the node
                // gives.
                var atNode = new Walks(null, _node, _above, rules.At);
                var atNodeAfter = new Walks(null, _node, _above, after);
                var concerned = rules.Profiles.Concat(_rows.Select(row => row.Profile))
                    .Distinct(StringComparer.Ordinal)
                    .Where(profile => !atNode.Gives(_node, profile).SameAs(atNodeAfter.Gives(_node, profile)))
                    .OrderBy(profile => profile, StringComparer.Ordinal)
                    .ToList();
                var walked = concerned.Count == 0 ? null : tree(rules);
                if (walked == null)
                {
                    return renewals;
                }

                var before = new Walks(walked, _node, _above, rules.At);
                var afterAct = new Walks(walked, _node, _above, after);
                var kept = concerned.ToDictionary(profile => profile, Kept, StringComparer.Ordinal);
                foreach (var below in Following(rules, walked))
                {
                    var drafts = Renewed(below, rules.At(below), walked.Above(below), concerned, kept, before, afterAct);
                    foreach (var draft in drafts)
                    {
                        var record = draft.Compose(_key, below);
                        if (!Fits(record))
                        {
                            _left.Add(new RuleLeft(below, draft.Profile, false, RuleLeftReason.TooLong));
                            continue;
                        }

                        renewals.Add(record);
                        List<StoreRecord> held;
                        if (!laid.TryGetValue(below, out held))
                        {
                            held = new List<StoreRecord>();
                            laid.Add(below, held);
                        }

                        held.Add(record);
                        laidRules.Remove(below);
                    }
                }

                return renewals;
            }

            /// <summary>
            /// The nodes below the node in the tree at which a standing version lets a profile
            /// through, the nearer first and by identifier within a distance; a node whose way up
            /// does not reach the node within the depth the resolver walks is no node below it.
            /// </summary>
            private IEnumerable<string> Following(StoredRules rules, IRuledTree tree)
            {
                var placed = new List<KeyValuePair<int, string>>();
                foreach (var below in tree.Below(_node).Where(rules.LetsThrough).Distinct(StringComparer.Ordinal))
                {
                    var depth = 0;
                    var at = below;
                    while (at != null && !string.Equals(at, _node, StringComparison.Ordinal) && depth <= AclResolver.MaxDepth)
                    {
                        at = tree.Above(at);
                        depth++;
                    }

                    if (at != null && depth > 0 && depth <= AclResolver.MaxDepth)
                    {
                        placed.Add(new KeyValuePair<int, string>(depth, below));
                    }
                }

                return placed.OrderBy(pair => pair.Key).ThenBy(pair => pair.Value, StringComparer.Ordinal).Select(pair => pair.Value);
            }

            /// <summary>
            /// The identifiers of the versions below that every press concerning the profile kept:
            /// a press of its row and the press on the box, none where no press concerns it.
            /// </summary>
            private ISet<string> Kept(string profile)
            {
                var presses = _rows.Where(row => string.Equals(row.Profile, profile, StringComparison.Ordinal)).Select(row => row.Below)
                    .Concat(_changes.Switch == null ? Enumerable.Empty<IReadOnlyCollection<string>>() : new[] { _changes.Switch.Below })
                    .ToList();
                var kept = new HashSet<string>(presses.FirstOrDefault() ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                foreach (var press in presses.Skip(1))
                {
                    kept.IntersectWith(press);
                }

                return kept;
            }

            /// <summary>
            /// The versions written anew at a node below, a version for each version renewed:
            /// each that the profiles following it record, by profile, `STORE.md` §617.
            /// </summary>
            private List<Draft> Renewed(
                string node,
                NodeRule rule,
                string parent,
                IEnumerable<string> concerned,
                IReadOnlyDictionary<string, ISet<string>> kept,
                Walks before,
                Walks after)
            {
                var drafts = new List<Draft>();

                // A node whose switch refuses lets nothing through, whatever arrives.
                if (rule.Switch.Refuses)
                {
                    return drafts;
                }

                var inheriting = rule.Switch.Versions;
                var follows = new SortedDictionary<string, AclGrant>(StringComparer.Ordinal);
                foreach (var profile in concerned)
                {
                    var row = rule.Row(profile);
                    var lets = row.Versions.Where(TakesOff).ToList();
                    if (row.Refuses || row.Answers || (lets.Count == 0 && inheriting.Count == 0))
                    {
                        continue;
                    }

                    var arrived = before.Gives(parent, profile);
                    var arrives = after.Gives(parent, profile);
                    if (arrived.SameAs(arrives))
                    {
                        continue;
                    }

                    // Every version that lets the profile through followed what arrived: a version
                    // that was cutting, `off:deny` among them, leaves every version of the node for
                    // the profile as it is, the switch's among them.
                    var whole = lets.All(version => version.Kind == RowVersionKind.TakenOff && Followed(version.Letters, arrived))
                                && inheriting.All(version => Followed(version.LettersFor(profile), arrived));
                    if (!whole)
                    {
                        continue;
                    }

                    // And the tab held every version standing there for the profile, of its row and
                    // of the switch, at every press concerning the profile: where it did not, the
                    // node is handed back, as no press, so that the log names why the change above
                    // did not reach it.
                    var held = kept[profile];
                    if (row.Versions.Any(version => !held.Contains(version.Id)) || inheriting.Any(version => !held.Contains(version.Id)))
                    {
                        _left.Add(new RuleLeft(node, profile, false, RuleLeftReason.NotHeld));
                        continue;
                    }

                    follows[profile] = arrives;
                }

                if (follows.Count == 0)
                {
                    return drafts;
                }

                foreach (var version in inheriting.Where(version => follows.Any(pair => !version.LettersFor(pair.Key).SameAs(pair.Value))))
                {
                    var letters = version.Profiles.Concat(follows.Keys).Distinct(StringComparer.Ordinal)
                        .Select(profile =>
                        {
                            AclGrant arrives;
                            return new KeyValuePair<string, AclGrant>(
                                profile, follows.TryGetValue(profile, out arrives) ? arrives : version.LettersFor(profile));
                        });
                    drafts.Add(new Draft(null, RuleText.Inheriting(letters), Names(RuleText.SwitchObject(node), version.Id, version.After)));
                }

                foreach (var pair in follows)
                {
                    var renewed = RuleText.TakenOff(pair.Value);
                    foreach (var version in rule.Row(pair.Key).Versions.Where(version => version.Kind == RowVersionKind.TakenOff && Text(version) != renewed))
                    {
                        drafts.Add(new Draft(pair.Key, renewed, Names(RuleText.RowObject(node, pair.Key), version.Id, version.After)));
                    }
                }

                return drafts;
            }

            /// <summary>Whether these letters hold every letter of those.</summary>
            private static bool Holds(AclGrant letters, AclGrant those)
            {
                return those.Cut(letters).SameAs(those);
            }

            /// <summary>
            /// Whether a version below recording these letters followed what arrived for its
            /// profile before the act: it held every letter that arrived, and where nothing
            /// arrived, it recorded nothing itself.
            /// </summary>
            /// <remarks>
            /// **Where nothing arrived, a version that recorded letters is left**, since nothing
            /// tells whether it was cutting: where a version above is lost, or a refusal made above
            /// in another copy arrives by a merge, a version that followed and a limit made apart
            /// look alike, and renewing every such version would let a repair above lift a limit
            /// that nobody settled at its own node, `STORE.md` §639. A version that recorded
            /// nothing was following whatever arrived, and follows; a version that followed until
            /// an act above emptied what arrived was written anew by that act as recording
            /// nothing. **This is stricter than the measured model**, which takes every version
            /// standing where nothing arrived as following; `STORE.md` §617 records the difference
            /// as owed to the model.
            /// </remarks>
            private static bool Followed(AclGrant recorded, AclGrant arrived)
            {
                return arrived.Count == 0 ? recorded.Count == 0 : Holds(recorded, arrived);
            }

            /// <summary>Whether a version's content and the item that restates it each fit the ceiling of a string.</summary>
            /// <remarks>
            /// **The item's length is had from the version's content**, which the same writer
            /// composed: the content holds `after`, `doc`, `nonce` and `object`, sorted by name in
            /// ordinal order with no white space, and the item a summary entry restates the version
            /// by holds `after`, `doc`, `id`, `kind` and `object` written the same way, `STORE.md`
            /// §611. So the item is the content with the member `nonce` giving way to `id` and
            /// `kind`, each of whose names and values needs no escape, and no text is composed
            /// again to measure it. The tests that crowd a version to the ceiling hold this to the
            /// item composed in full.
            /// </remarks>
            private static bool Fits(StoreRecord record)
            {
                if (record.Strings.Values.Any(value => value.Length > StoreFormat.MaxWritten))
                {
                    return false;
                }

                var item = record.Content.Length
                           - Written(StoreKinds.Nonce, record.Member(StoreKinds.Nonce))
                           + Written(StoreKinds.Id, record.Hex)
                           + Written(StoreKinds.ItemKind, StoreKinds.Version);
                return item <= StoreFormat.MaxWritten;
            }

            /// <summary>
            /// The characters a member whose name and value need no escape takes in a flat object
            /// written with no white space, with the comma that parts it from the member after it.
            /// </summary>
            private static int Written(string name, string value)
            {
                return name.Length + value.Length + "\"\":\"\",".Length;
            }

            private bool IsLeft(string key)
            {
                return _leftKeys.Contains(key);
            }

            /// <summary>Tells of a change or a version not written, once, which leaves it out of every composing after.</summary>
            private void Report(string key, string profile, bool pressed, RuleLeftReason reason)
            {
                if (_leftKeys.Add(key))
                {
                    _left.Add(new RuleLeft(_node, profile, pressed, reason));
                }
            }

            /// <summary>Every version the changes write, with the versions left out at the ceiling taken as not pressed.</summary>
            private IEnumerable<Draft> Drafts()
            {
                var drafts = new List<Draft>();
                var box = _changes.Switch;

                // The switch as the OK leaves it: the value pressed where it is written, or else
                // what the reading gives, `STORE.md` §617.
                var boxWritten = box != null && !Gives(_stands.Switch, box.Refuses) && !IsLeft(Draft.SwitchKey);
                var refuses = boxWritten ? box.Refuses : _stands.Switch.Refuses;
                var turned = boxWritten && !box.Refuses;

                // Whether a press on this tab cleared the box, written or given by the store
                // already.
                var cleared = box != null && !box.Refuses && !IsLeft(Draft.SwitchKey);

                // The letters each press records for a profile in the versions of the switch, and
                // whether it was a press of the profile's own row that asked for them; and each
                // version of a row a press of the row decides, which the box writes anew no more.
                var overrides = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
                var settled = new HashSet<string>(StringComparer.Ordinal);
                var handled = new HashSet<string>(StringComparer.Ordinal);

                foreach (var change in _rows)
                {
                    var profile = change.Profile;
                    var row = _stands.Row(profile);
                    var shown = change.Shown.Row(profile);
                    var rowKey = Draft.RowKey(profile);
                    switch (change.Kind)
                    {
                        case RowChangeKind.Set:
                            if (IsLeft(rowKey))
                            {
                                break;
                            }

                            if (!Single(row, RuleText.Row(change.Letters)))
                            {
                                drafts.Add(new Draft(rowKey, profile, RuleText.Row(change.Letters), Names(RowObject(profile), shown), true));
                            }

                            handled.UnionWith(row.Versions.Select(version => version.Id));
                            break;

                        case RowChangeKind.TakeOff:
                            if (IsLeft(rowKey))
                            {
                                break;
                            }

                            // `off:deny` where the switch refuses as the OK leaves it, and where
                            // the tab showed it refusing at the press with no press here clearing
                            // it: what the tab showed arriving under a refusing switch is nothing.
                            var denied = refuses || (change.Shown.Switch.Refuses && !cleared);
                            var letters = denied ? null : Recorded(change.Letters, box, turned, profile);
                            var text = denied ? RuleText.TakenOffRefusing : RuleText.TakenOff(letters);
                            if (!Single(row, text))
                            {
                                drafts.Add(new Draft(rowKey, profile, text, Names(RowObject(profile), shown), true));
                            }

                            handled.UnionWith(row.Versions.Select(version => version.Id));
                            if (!denied)
                            {
                                overrides[profile] = letters;
                            }

                            break;

                        case RowChangeKind.RemoveVersions:
                            if (IsLeft(rowKey))
                            {
                                break;
                            }

                            var removed = shown.Versions.Where(version => change.Removed.Contains(version.Id, StringComparer.Ordinal)).ToList();
                            var lost = shown.Lost.Where(id => change.Removed.Contains(id, StringComparer.Ordinal)).ToList();
                            var kept = shown.Versions.Where(version => !change.Removed.Contains(version.Id, StringComparer.Ordinal)).ToList();

                            // A version out of form has no text to be written again with, and stays
                            // as it stands; a removal that leaves no other version to write again
                            // cannot be written.
                            var again = kept.Where(version => version.Kind != RowVersionKind.OutOfForm).ToList();
                            if (again.Count == 0)
                            {
                                Report(rowKey, profile, true, RuleLeftReason.NotAllowed);
                                break;
                            }

                            if (Holds(row, kept))
                            {
                                break;
                            }

                            handled.UnionWith(removed.Select(version => version.Id));
                            foreach (var version in again)
                            {
                                var keptText = turned && TakesOff(version)
                                    ? RuleText.TakenOff(BoxArriving(box, profile))
                                    : Text(version);
                                var keptKey = Draft.VersionKey(version.Id);
                                if (!IsLeft(keptKey))
                                {
                                    drafts.Add(new Draft(
                                        keptKey, profile, keptText, Names(RowObject(profile), removed.Concat(new[] { version }), lost), true));
                                    handled.Add(version.Id);
                                }
                            }

                            break;

                        case RowChangeKind.Settle:
                            if (IsLeft(rowKey))
                            {
                                break;
                            }

                            // A limit row shows only where the tab shows the switch letting
                            // through.
                            if (change.Shown.Switch.Refuses && !cleared)
                            {
                                Report(rowKey, profile, true, RuleLeftReason.OutOfForm);
                                break;
                            }

                            if (refuses)
                            {
                                Report(rowKey, profile, true, RuleLeftReason.NotAllowed);
                                break;
                            }

                            var arriving = Recorded(change.Letters, box, turned, profile);
                            foreach (var version in Renewable(row, shown).Where(TakesOff))
                            {
                                handled.Add(version.Id);
                                var renewed = RuleText.TakenOff(arriving);
                                var renewedKey = Draft.VersionKey(version.Id);
                                if (renewed != Text(version) && !IsLeft(renewedKey))
                                {
                                    drafts.Add(new Draft(renewedKey, profile, renewed, Names(RowObject(profile), new[] { version }), true));
                                }
                            }

                            overrides[profile] = arriving;
                            settled.Add(profile);
                            break;
                    }
                }

                // The box turned to let the walk go on writes anew every rule taken off at the node
                // that the tab showed and that records other letters than arrive, but a version a
                // press of its own row decides, `STORE.md` §617.
                if (turned)
                {
                    foreach (var profile in _stands.Profiles)
                    {
                        var renewed = RuleText.TakenOff(BoxArriving(box, profile));
                        foreach (var version in Renewable(_stands.Row(profile), box.Shown.Row(profile)).Where(TakesOff).Where(version => !handled.Contains(version.Id)))
                        {
                            var renewedKey = Draft.VersionKey(version.Id);
                            if (renewed != Text(version) && !IsLeft(renewedKey))
                            {
                                drafts.Add(new Draft(renewedKey, profile, renewed, Names(RowObject(profile), new[] { version }), false));
                            }
                        }
                    }
                }

                drafts.AddRange(Switches(box, boxWritten, refuses, overrides, settled));
                return drafts;
            }

            /// <summary>
            /// The versions of the switch the changes write: the box's own, or each standing
            /// version that inherits, that the tab showed at a press of a profile's row, written
            /// anew with that profile's letters changed and every other profile's kept.
            /// </summary>
            private IEnumerable<Draft> Switches(
                SwitchChange box, bool boxWritten, bool refuses, IReadOnlyDictionary<string, AclGrant> overrides, ISet<string> settled)
            {
                var objectText = RuleText.SwitchObject(_node);
                if (boxWritten)
                {
                    var text = box.Refuses
                        ? RuleText.SwitchRefusing
                        : RuleText.Inheriting(box.Arriving.Keys.Concat(overrides.Keys).Distinct(StringComparer.Ordinal)
                            .Select(profile => new KeyValuePair<string, AclGrant>(profile, Override(overrides, profile, box))));
                    yield return new Draft(Draft.SwitchKey, null, text, Names(objectText, box.Shown.Switch), true);
                    yield break;
                }

                if (refuses)
                {
                    yield break;
                }

                foreach (var version in _stands.Switch.Versions.Where(version => version.Kind == SwitchVersionKind.Inherits))
                {
                    var asking = _rows
                        .Where(change => change.Shown.Switch.Versions.Any(shown => shown.Id == version.Id))
                        .Select(change => new KeyValuePair<string, AclGrant>(change.Profile, Asked(overrides, version, change.Profile)))
                        .Where(pair => pair.Value != null)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    var key = Draft.VersionKey(version.Id);
                    if (asking.Count == 0 || IsLeft(key))
                    {
                        continue;
                    }

                    var letters = version.Profiles.Concat(asking.Keys).Distinct(StringComparer.Ordinal)
                        .Select(profile => new KeyValuePair<string, AclGrant>(profile, Given(asking, profile) ?? version.LettersFor(profile)));
                    yield return new Draft(
                        key,
                        null,
                        RuleText.Inheriting(letters),
                        Names(objectText, version.Id, version.After),
                        asking.Keys.Any(settled.Contains));
                }
            }

            /// <summary>
            /// The letters a press records for a profile in a version of the switch, or null where
            /// no press records letters for the profile or the version holds them already.
            /// </summary>
            private static AclGrant Asked(IReadOnlyDictionary<string, AclGrant> overrides, SwitchVersion version, string profile)
            {
                var letters = Given(overrides, profile);
                return letters != null && !version.LettersFor(profile).SameAs(letters) ? letters : null;
            }

            /// <summary>The letters given for a profile, or null where none are.</summary>
            private static AclGrant Given(IReadOnlyDictionary<string, AclGrant> letters, string profile)
            {
                AclGrant given;
                return letters.TryGetValue(profile, out given) ? given : null;
            }

            /// <summary>The letters a press records for a profile in the switch: its own, or the box's where it gives none.</summary>
            private AclGrant Override(IReadOnlyDictionary<string, AclGrant> overrides, string profile, SwitchChange box)
            {
                return Given(overrides, profile) ?? BoxArriving(box, profile);
            }

            /// <summary>
            /// The letters a press of a profile's row records: what the tab showed arriving at it,
            /// and where the same OK writes the box to let the walk go on, only what the box's
            /// press showed arriving as well; each cut to what arrives now.
            /// </summary>
            private AclGrant Recorded(AclGrant letters, SwitchChange box, bool turned, string profile)
            {
                return (turned ? letters.Cut(BoxArriving(box, profile)) : letters).Cut(Now(profile));
            }

            /// <summary>What the box's press showed arriving for a profile, no letter where it showed none, cut to what arrives now.</summary>
            private AclGrant BoxArriving(SwitchChange box, string profile)
            {
                AclGrant letters;
                return (box.Arriving.TryGetValue(profile, out letters) ? letters : AclGrant.Nothing).Cut(Now(profile));
            }

            /// <summary>
            /// What arrives at the node for a profile in the write's reading: what the node's
            /// parent gives, nothing above the root, walked once for each profile.
            /// </summary>
            private AclGrant Now(string profile)
            {
                AclGrant letters;
                if (!_arrives.TryGetValue(profile, out letters))
                {
                    letters = AclResolver.Arrives(_above, profile);
                    _arrives[profile] = letters;
                }

                return letters;
            }

            /// <summary>The standing versions of a row in the write's reading that the tab showed too.</summary>
            private static IEnumerable<RowVersion> Renewable(RowRule row, RowRule shown)
            {
                return row.Versions.Where(version => shown.Versions.Any(seen => seen.Id == version.Id));
            }

            /// <summary>Whether the reading gives the switch this value from a single standing version, or from none for a switch that lets the walk go on.</summary>
            private static bool Gives(SwitchRule held, bool refuses)
            {
                if (held.Lost.Count > 0)
                {
                    return false;
                }

                return refuses
                    ? held.Versions.Count == 1 && held.Versions[0].Kind == SwitchVersionKind.Refuses
                    : held.Versions.Count == 0 || (held.Versions.Count == 1 && held.Versions[0].Kind == SwitchVersionKind.Inherits);
            }

            /// <summary>Whether the reading gives the row this text from a single standing version.</summary>
            private static bool Single(RowRule row, string text)
            {
                return row.Lost.Count == 0 && row.Versions.Count == 1 && Text(row.Versions[0]) == text;
            }

            /// <summary>Whether the reading holds exactly these versions of the row standing, and nothing lost.</summary>
            private static bool Holds(RowRule row, IReadOnlyCollection<RowVersion> kept)
            {
                if (row.Lost.Count > 0)
                {
                    return false;
                }

                if (kept.Count == 1 && Single(row, Text(kept.First())))
                {
                    return true;
                }

                return row.Versions.Select(version => version.Id).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(kept.Select(version => version.Id).OrderBy(id => id, StringComparer.Ordinal));
            }

            private string RowObject(string profile)
            {
                return RuleText.RowObject(_node, profile);
            }

            /// <summary>What a version of a row names: the versions the tab showed and every name of theirs, the lost versions among them.</summary>
            private IReadOnlyCollection<string> Names(string objectText, RowRule shown)
            {
                return Names(objectText, shown.Versions.SelectMany(version => new[] { version.Id }.Concat(version.After)).Concat(shown.Lost));
            }

            /// <summary>What a version of the switch names: the versions the tab showed and every name of theirs, the lost versions among them.</summary>
            private IReadOnlyCollection<string> Names(string objectText, SwitchRule shown)
            {
                return Names(objectText, shown.Versions.SelectMany(version => new[] { version.Id }.Concat(version.After)).Concat(shown.Lost));
            }

            /// <summary>What a version names: these versions and every name of theirs.</summary>
            private IReadOnlyCollection<string> Names(string objectText, IEnumerable<RowVersion> versions)
            {
                return Names(objectText, versions, Enumerable.Empty<string>());
            }

            /// <summary>What a version names: these versions and every name of theirs, and these lost versions.</summary>
            private IReadOnlyCollection<string> Names(string objectText, IEnumerable<RowVersion> versions, IEnumerable<string> lost)
            {
                return Names(objectText, versions.SelectMany(version => new[] { version.Id }.Concat(version.After)).Concat(lost));
            }

            private IReadOnlyCollection<string> Names(string objectText, string id, IEnumerable<string> after)
            {
                return Names(objectText, new[] { id }.Concat(after));
            }

            /// <summary>
            /// These identifiers, each once and ascending, less every identifier the reading
            /// carries as a version of another object or as a record of another kind, and every
            /// identifier whose first bytes are not those of a version of this object under the
            /// key.
            /// </summary>
            /// <remarks>
            /// The first bytes of this object's versions are worked out only for an identifier the
            /// reading carries nowhere, which is where they decide anything.
            /// </remarks>
            private IReadOnlyCollection<string> Names(string objectText, IEnumerable<string> identifiers)
            {
                return identifiers
                    .Where(id => id != null && id.Length == NodeLength)
                    .Distinct(StringComparer.Ordinal)
                    .Where(id =>
                    {
                        StoreNaming carried;
                        if (_inForce.Versions.TryGetValue(id, out carried))
                        {
                            return string.Equals(carried.Object, objectText, StringComparison.Ordinal);
                        }

                        return !_inForce.Others.Contains(id) && id.StartsWith(Place(objectText), StringComparison.Ordinal);
                    })
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly();
            }

            /// <summary>The first bytes of the identifier of every version of this object under the key, as hexadecimal, `STORE.md` §609.</summary>
            /// <remarks>
            /// **The tag of `version` and a profile's tag are worked out once for the act**, and only
            /// the object's own subject for each object: an OK above many nodes names as many
            /// objects, each under the same tag and the same profile's.
            /// </remarks>
            private string Place(string objectText)
            {
                string place;
                if (_places.TryGetValue(objectText, out place))
                {
                    return place;
                }

                string tagged;
                var subject = StoreFormat.SubjectOf(StoreKinds.Find(StoreKinds.Version), objectText, out tagged);
                var number = 0;
                if (tagged != null && !_profileTags.TryGetValue(tagged, out number))
                {
                    number = StoreFormat.ProfileTag(_key, tagged);
                    _profileTags[tagged] = number;
                }

                if (_versionTag == null)
                {
                    _versionTag = Hex.Of(StoreFormat.Tag(_key, StoreKinds.Version));
                }

                place = _versionTag + Hex.Of(StoreFormat.Subject(_key, subject)) + number.ToString("x4", CultureInfo.InvariantCulture);
                _places[objectText] = place;
                return place;
            }
        }

        /// <summary>
        /// What the nodes of the tree give, each node's rules read from a single place: the
        /// reading's, before the act, or the reading's with the act and the renewals so far laid
        /// over it, after. Each node's answer is kept once asked for, which holds because the
        /// nodes are walked the nearer first, and nothing is laid over a node once a node below it
        /// has asked what it gives.
        /// </summary>
        private sealed class Walks
        {
            private readonly IRuledTree _tree;
            private readonly string _top;
            private readonly IAclNode _above;
            private readonly Func<string, NodeRule> _rules;
            private readonly Dictionary<string, AclGrant> _gives = new Dictionary<string, AclGrant>(StringComparer.Ordinal);

            /// <summary>The walks of this tree, each node's rules read from <paramref name="rules"/>.</summary>
            /// <param name="tree">The tree below the dialog's node, or null where only that node is asked about.</param>
            /// <param name="top">The node of the dialog, above which the walk goes on to <paramref name="above"/>.</param>
            public Walks(IRuledTree tree, string top, IAclNode above, Func<string, NodeRule> rules)
            {
                _tree = tree;
                _top = top;
                _above = above;
                _rules = rules;
            }

            /// <summary>What a node gives a profile: the walk of `STORE.md` §617 from it upward.</summary>
            public AclGrant Gives(string node, string profile)
            {
                var asked = node + "/" + profile;
                AclGrant letters;
                if (!_gives.TryGetValue(asked, out letters))
                {
                    letters = AclResolver.Arrives(new Placed(this, node), profile);
                    _gives[asked] = letters;
                }

                return letters;
            }

            /// <summary>A node of the tree as the resolver walks it, walked where it is asked.</summary>
            private sealed class Placed : IAclNode
            {
                private readonly Walks _walks;
                private readonly string _node;

                public Placed(Walks walks, string node)
                {
                    _walks = walks;
                    _node = node;
                }

                public NodeRule Rule
                {
                    get { return _walks._rules(_node); }
                }

                public IAclNode Parent
                {
                    get
                    {
                        if (string.Equals(_node, _walks._top, StringComparison.Ordinal))
                        {
                            return _walks._above;
                        }

                        var above = _walks._tree == null ? null : _walks._tree.Above(_node);
                        return above == null ? null : new Placed(_walks, above);
                    }
                }

                public string Name
                {
                    get { return _node; }
                }

                public T Held<T>(Func<T> walk)
                {
                    return walk();
                }
            }
        }

        /// <summary>A version to compose: its object, its text and what it names, and who asked for it.</summary>
        private sealed class Draft
        {
            /// <summary>What the switch's own version is left under.</summary>
            public const string SwitchKey = "switch";

            /// <summary>A version at the node, left under this key where it does not fit.</summary>
            public Draft(string key, string profile, string doc, IReadOnlyCollection<string> after, bool pressed)
            {
                Key = key;
                Profile = profile;
                Doc = doc;
                After = after;
                Pressed = pressed;
            }

            /// <summary>
            /// A version below written anew by the write above, which no press asked for: a version
            /// below that does not fit is handed back by its node, and no key leaves it out of a
            /// composing.
            /// </summary>
            public Draft(string profile, string doc, IReadOnlyCollection<string> after)
            {
                Profile = profile;
                Doc = doc;
                After = after;
            }

            /// <summary>
            /// What a version at the node is left under where it does not fit: a row, a version
            /// written anew, or the switch; none for a version below.
            /// </summary>
            public string Key { get; private set; }

            /// <summary>The profile of the row, or null for the switch.</summary>
            public string Profile { get; private set; }

            public string Doc { get; private set; }

            public IReadOnlyCollection<string> After { get; private set; }

            /// <summary>Whether a press asked for it rather than a renewal.</summary>
            public bool Pressed { get; private set; }

            /// <summary>What a row's version written for a press of the row is left under.</summary>
            public static string RowKey(string profile)
            {
                return "row " + profile;
            }

            /// <summary>What a version written anew, or written again, is left under.</summary>
            public static string VersionKey(string id)
            {
                return "version " + id;
            }

            /// <summary>The record of kind `version`, under the key, at the node.</summary>
            public StoreRecord Compose(byte[] key, string node)
            {
                var objectText = Profile == null ? RuleText.SwitchObject(node) : RuleText.RowObject(node, Profile);
                return StoreRecord.Compose(key, StoreKinds.Version, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.ObjectMember, objectText },
                    { StoreKinds.After, string.Join(" ", After) },
                    { StoreKinds.Doc, Doc },
                });
            }
        }
    }
}
