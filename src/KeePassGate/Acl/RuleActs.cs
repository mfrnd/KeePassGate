using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Transport;

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
    }

    /// <summary>
    /// A change of an OK on the KPG tab that was not written, or a version it would have written
    /// anew that was left as it was, and why: what the editor tells the operator of, `STORE.md`
    /// §629.
    /// </summary>
    public sealed class RuleLeft
    {
        /// <summary>A change of this profile's row, or of the node's switch where the profile is null, not written for this reason.</summary>
        /// <param name="pressed">Whether the change is the operator's own press, not a version written anew for another.</param>
        public RuleLeft(string profile, bool pressed, RuleLeftReason reason)
        {
            Profile = profile;
            Pressed = pressed;
            Reason = reason;
        }

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
        /// The node's parent with the rules of this same reading, which every mask recorded is cut
        /// to what it gives; null for the root, above which nothing arrives. Walked where it is
        /// asked, inside the caller's turn, once for each profile a mask is recorded for.
        /// </param>
        public static StoreAct Of(StoreReading reading, string node, IAclNode above, RuleChanges changes, out IReadOnlyList<RuleLeft> left)
        {
            left = Array.Empty<RuleLeft>();
            if (changes == null || changes.IsEmpty)
            {
                return null;
            }

            var inForce = reading == null ? null : reading.InForce;
            if (inForce == null || reading.State != StoreState.Whole)
            {
                left = Every(changes, RuleLeftReason.Unread);
                return null;
            }

            if (!IsNode(node) || Owned(reading, node))
            {
                left = Every(changes, RuleLeftReason.OutOfForm);
                return null;
            }

            var key = (byte[])inForce.Key.Bytes.Clone();
            try
            {
                var composing = new Composing(inForce, key, node, above, StoredRules.Of(reading).At(node), changes);
                var records = composing.Records();
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

        /// <summary>Each change, of every row and of the box, told as not written for this reason.</summary>
        private static IReadOnlyList<RuleLeft> Every(RuleChanges changes, RuleLeftReason reason)
        {
            var every = changes.Rows.Select(change => new RuleLeft(change.Profile, true, reason)).ToList();
            if (changes.Switch != null)
            {
                every.Add(new RuleLeft(null, true, reason));
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

            /// <summary>Whether a version's content and the item that restates it each fit the ceiling of a string.</summary>
            private static bool Fits(StoreRecord record)
            {
                if (record.Strings.Values.Any(value => value.Length > StoreFormat.MaxWritten))
                {
                    return false;
                }

                // The item a summary entry restates the version by, written as a record writes its
                // members: sorted by name in ordinal order, with no white space, `STORE.md` §611.
                var item = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.ItemKind, StoreKinds.Version },
                    { StoreKinds.Id, record.Hex },
                    { StoreKinds.ObjectMember, record.Member(StoreKinds.ObjectMember) },
                    { StoreKinds.After, record.Member(StoreKinds.After) },
                    { StoreKinds.Doc, record.Member(StoreKinds.Doc) },
                };
                using (var json = new JsonWriter())
                {
                    foreach (var member in item)
                    {
                        json.Text(member.Key, member.Value);
                    }

                    return json.Done().Length <= StoreFormat.MaxWritten;
                }
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
                    _left.Add(new RuleLeft(profile, pressed, reason));
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
            private IReadOnlyCollection<string> Names(string objectText, IEnumerable<string> identifiers)
            {
                var place = Place(objectText);
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

                        return !_inForce.Others.Contains(id) && id.StartsWith(place, StringComparison.Ordinal);
                    })
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly();
            }

            /// <summary>The first bytes of the identifier of every version of this object under the key, as hexadecimal, `STORE.md` §609.</summary>
            private string Place(string objectText)
            {
                string place;
                if (_places.TryGetValue(objectText, out place))
                {
                    return place;
                }

                string tagged;
                var subject = StoreFormat.SubjectOf(StoreKinds.Find(StoreKinds.Version), objectText, out tagged);
                var number = tagged == null ? 0 : StoreFormat.ProfileTag(_key, tagged);
                place = Hex.Of(StoreFormat.Tag(_key, StoreKinds.Version)) + Hex.Of(StoreFormat.Subject(_key, subject))
                        + number.ToString("x4", CultureInfo.InvariantCulture);
                _places[objectText] = place;
                return place;
            }
        }

        /// <summary>A version to compose: its object, its text and what it names, and who asked for it.</summary>
        private sealed class Draft
        {
            /// <summary>What the switch's own version is left under.</summary>
            public const string SwitchKey = "switch";

            public Draft(string key, string profile, string doc, IReadOnlyCollection<string> after, bool pressed)
            {
                Key = key;
                Profile = profile;
                Doc = doc;
                After = after;
                Pressed = pressed;
            }

            /// <summary>What the version is left under where it does not fit: a row, a version written anew, or the switch.</summary>
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
