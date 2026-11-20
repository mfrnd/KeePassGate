using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;

namespace KeePassGate.Ui
{
    /// <summary>A line of the editor: a profile, its rule as a mask, and the states that hold for it.</summary>
    public sealed class GrantRow
    {
        internal GrantRow(
            string profile,
            GrantRowKind kind,
            string version,
            AclGrant letters,
            IReadOnlyList<GrantState> states,
            bool changed,
            AclGrant inherited,
            int group,
            AclGrant granted,
            bool unreadable,
            bool takesRule,
            bool removable)
        {
            Profile = profile;
            Kind = kind;
            Version = version;
            Mask = VerbMask.Of(letters);
            States = states;
            Changed = changed;
            InheritedMask = inherited == null ? null : VerbMask.Of(inherited);
            Group = group;
            Granted = VerbMask.Of(granted);
            Unreadable = unreadable;
            TakesRule = takesRule;
            Removable = removable;
        }

        /// <summary>The profile the row is for.</summary>
        public string Profile { get; private set; }

        /// <summary>
        /// The letters the row shows, as `CRUSDLAP`: what a rule grants, what a version recorded,
        /// what arrives from above, or what a limit lets through, by the row's kind.
        /// </summary>
        public string Mask { get; private set; }

        /// <summary>What the row stands for.</summary>
        public GrantRowKind Kind { get; private set; }

        /// <summary>The identifier of the version the row shows, or null for an inherited row and a limit row.</summary>
        /// <remarks>
        /// Null as well for a rule chosen on the tab, which has no version until OK writes it.
        /// </remarks>
        public string Version { get; private set; }

        /// <summary>
        /// Every state that holds for the row, in the order of <see cref="GrantState"/>, whose
        /// first the State column shows: none for a rule set here in a single version.
        /// </summary>
        public IReadOnlyList<GrantState> States { get; private set; }

        /// <summary>Whether a press on the tab changed the row, which OK writes and Cancel discards.</summary>
        public bool Changed { get; private set; }

        /// <summary>
        /// What arrives at the node for the profile from the groups above, or null where nothing
        /// above ends at a rule for the profile: on a rule of this node's own, what the rule
        /// replaces.
        /// </summary>
        public string InheritedMask { get; private set; }

        /// <summary>
        /// How many rows of the profile are bound together with this row, the row among them: more
        /// than a single row for the versions of a rule in several versions and for a limit under
        /// its inherited row.
        /// </summary>
        public int Group { get; private set; }

        /// <summary>Whether the row is bound together with other rows of its profile.</summary>
        public bool Bound
        {
            get { return Group > 1; }
        }

        /// <summary>What the gate grants the profile at the node once OK is written, as a mask.</summary>
        public string Granted { get; private set; }

        /// <summary>
        /// Whether a lost row shows a version whose text cannot be read, rather than a version
        /// whose content is nowhere; on a limit row, whether the limit is a rule taken off while
        /// the node refused any profile it does not name.
        /// </summary>
        public bool Unreadable { get; private set; }

        /// <summary>
        /// Whether a rule can be chosen on the row: a rule set here, an inherited row, a lost row
        /// standing alone, and every row of a group in which no version that reads would be left.
        /// </summary>
        public bool TakesRule { get; private set; }

        /// <summary>
        /// Whether the row carries an X: every row but an inherited row, and a row of a group only
        /// where OK can write what the X leaves.
        /// </summary>
        public bool Removable { get; private set; }

        /// <summary>The same row, marked as changed by a press on the tab.</summary>
        internal GrantRow AsChanged()
        {
            // cspell:ignore Memberwise
            var changed = (GrantRow)MemberwiseClone();
            changed.Changed = true;
            return changed;
        }

        public override string ToString()
        {
            return Profile + " " + Mask + (States.Count == 0 ? string.Empty : " (" + string.Join(", ", States) + ")")
                   + (Changed ? " (changed)" : string.Empty);
        }
    }

    /// <summary>What a row of the KPG tab's table stands for, `STORE.md` §657.</summary>
    public enum GrantRowKind
    {
        /// <summary>A rule set at this node, in a single version.</summary>
        Own = 0,

        /// <summary>A rule from a group above, as it arrives at this node.</summary>
        Inherited,

        /// <summary>The limit row bound under a profile's inherited row: the letters this node's versions let through.</summary>
        Limit,

        /// <summary>A single standing version of a rule set at this node in several versions, bound with the others.</summary>
        Version,

        /// <summary>A rule set at this node whose version was lost or cannot be read.</summary>
        Lost,
    }

    /// <summary>A word of the State column, in the order whose first a cell shows where several hold.</summary>
    public enum GrantState
    {
        /// <summary>The database no longer names the row's profile.</summary>
        NoProfile = 0,

        /// <summary>A version of the rule was lost, or cannot be read.</summary>
        Lost,

        /// <summary>The rule stands in several versions made in copies of the database.</summary>
        Version,

        /// <summary>In this version the rule was taken off.</summary>
        TakenOff,

        /// <summary>This node lets a grant from above through only as far as the row shows.</summary>
        Limit,

        /// <summary>This node's box refuses the rule from a group above.</summary>
        RefusedHere,

        /// <summary>The rule comes from a group above.</summary>
        Inherited,
    }

    /// <summary>What the check box of the KPG tab shows, `STORE.md` §657.</summary>
    public enum GrantBox
    {
        /// <summary>Cleared: no version of the switch stands, or only versions that inherit.</summary>
        Cleared = 0,

        /// <summary>Ticked: a single standing version refuses, or a press ticked the box.</summary>
        Ticked,

        /// <summary>The mixed mark, counted as ticked: a version that refuses stands beside a version that inherits.</summary>
        Mixed,

        /// <summary>Ticked, with the tooltip of a lost setting: a version was lost or cannot be read.</summary>
        Lost,
    }

    /// <summary>
    /// Every decision the grant editor makes, with no window anywhere in it: what the node's rules
    /// show as, and what each press on the tab asks OK to write.
    /// </summary>
    /// <remarks>
    /// The table is the easy half. What is worth testing is what each row stands for, what each
    /// press is allowed on, and what OK is handed, so all of that lives here, where a test drives it
    /// without a message pump, and `GrantPanel` is the part that draws it.
    ///
    /// **A buffer and no store**, `STORE.md` §629. A press changes the sheet and nothing else, and
    /// the sheet keeps, with each press, the node's rules as the tab showed them then: what a
    /// version written at OK names in `after`, and what each renewal is taken from. It keeps the
    /// standing versions at the nodes below as well, the only versions below an OK may renew.
    /// Nothing reaches the database before the dialog closes with OK, and any other close throws
    /// the sheet away.
    ///
    /// **The table shows what OK makes.** Every row a press changes is drawn as the OK writes it,
    /// marked as changed: a version the box's press renews with what arrives shows the letters OK
    /// records, and a rule removed where nothing arrives keeps a row with no letter until the
    /// dialog closes, so that every change waiting for OK has a row. A press OK would write
    /// nothing for holds no change and leaves the row as the store gives it, which is also how a
    /// press that undoes an earlier press of the same row takes that change away.
    ///
    /// **A press is kept per profile, the last taking the place of the presses before it**, so a
    /// rule chosen after the versions of a rule were removed down to a single row writes a single
    /// version naming every version the tab showed. Removing versions keeps the tab's reading of
    /// the first removal, since the rows it leaves are the versions that reading held.
    ///
    /// **No press is offered that OK cannot write.** The X on a row of a group removes that
    /// version only where a version that reads is left to write again; in a group where none
    /// would be, every row takes a rule and its X takes the rule off, every version with it.
    /// </remarks>
    public sealed class GrantSheet
    {
        private readonly Dictionary<string, Press> _presses = new Dictionary<string, Press>(StringComparer.Ordinal);
        private readonly List<string> _offered = new List<string>();

        private NodeRule _rule;
        private AclInheritance _above;

        /// <summary>The standing versions at the nodes below, as the reading the sheet shows holds them: none until the tab hands them over.</summary>
        private IReadOnlyCollection<string> _below = RowChange.Kept(null);

        /// <summary>The database's list exactly as given, or null when none was.</summary>
        private HashSet<string> _named;

        /// <summary>The last press on the box, or null where the box was not pressed.</summary>
        private BoxPress _box;

        private GrantSheet(NodeRule rule, AclInheritance above, IEnumerable<string> names, string readOnlyReason)
        {
            _rule = rule ?? NodeRule.None;
            _above = above ?? AclInheritance.None;
            ReadOnlyReason = readOnlyReason;
            TakeNames(names);
        }

        /// <summary>
        /// A sheet over a node's rules as a reading holds them, with what arrives from the groups
        /// above and the names the database holds: read only for this reason where it is not null.
        /// </summary>
        /// <param name="rule">The node's rules, or null for a node the store holds nothing for.</param>
        /// <param name="above">
        /// What arrives at the node from the groups above, or null for a node nothing arrives at,
        /// the root group among them.
        /// </param>
        /// <param name="names">
        /// The profiles this database names, for the pick list and for marking a rule whose name
        /// the list no longer holds. Null asks nothing about the list and marks no row.
        /// </param>
        /// <param name="readOnlyReason">
        /// Why a change here could not be written, or null when it can. Set, the rules stay
        /// readable, which is the point of opening the tab, and no press is taken.
        /// </param>
        public static GrantSheet Of(NodeRule rule, AclInheritance above, IEnumerable<string> names, string readOnlyReason)
        {
            return new GrantSheet(rule, above, names, readOnlyReason);
        }

        /// <summary>Why a change could not be written, or null.</summary>
        public string ReadOnlyReason { get; private set; }

        /// <summary>Whether anything here can be changed at all.</summary>
        public bool CanEdit
        {
            get { return string.IsNullOrEmpty(ReadOnlyReason); }
        }

        /// <summary>The profiles worth offering in the pick list, sorted.</summary>
        public IEnumerable<string> Offered
        {
            get { return _offered; }
        }

        /// <summary>
        /// The profiles a new rule here could be for: the ones this database names, less every one
        /// the table already shows a row for.
        /// </summary>
        /// <remarks>
        /// **A profile the table shows is not offered again**, whichever way the row got there:
        /// a rule here, a rule from above, a limit, a version, a rule taken off on the tab and
        /// shown until OK as the inherited rule in its place. What the operator wants for such a
        /// profile is to change the row already there.
        /// </remarks>
        public IEnumerable<string> Available
        {
            get { return _offered.Where(profile => !Shows(profile)).ToList(); }
        }

        /// <summary>The warning worth reading over the table, or null.</summary>
        /// <remarks>
        /// A change that cannot be written comes first. A database that names no profile comes
        /// next: the table then offers no profile to choose a rule for, and nothing else on the tab
        /// tells where a profile is named. **No reason of the read only kind**, since the box and
        /// the X on the row of a name the list no longer holds still take a press OK writes. A node
        /// deeper than the walk reaches comes last, since nothing on the table can show it: every
        /// profile neither this node nor a group in between names is refused there.
        /// </remarks>
        public string Warning
        {
            get
            {
                if (!CanEdit)
                {
                    return ReadOnlyReason;
                }

                if (_offered.Count == 0)
                {
                    return GrantTab.NoProfilesYet;
                }

                return _above.TooDeep ? GrantTab.TooDeep : null;
            }
        }

        /// <summary>What the check box shows.</summary>
        public GrantBox Box
        {
            get
            {
                if (_box != null)
                {
                    return _box.Refuses ? GrantBox.Ticked : GrantBox.Cleared;
                }

                var stored = _rule.Switch;
                if (stored.Lost.Count > 0 || stored.Versions.Any(version => version.Kind == SwitchVersionKind.OutOfForm))
                {
                    return GrantBox.Lost;
                }

                if (stored.Mixed)
                {
                    return GrantBox.Mixed;
                }

                return stored.Refuses ? GrantBox.Ticked : GrantBox.Cleared;
            }
        }

        /// <summary>
        /// Whether a press on the box holds a change, which OK writes: the box pressed to a value
        /// the store does not give from a single standing version, or for a box cleared, from no
        /// version at all.
        /// </summary>
        public bool BoxChanged
        {
            get
            {
                if (_box == null)
                {
                    return false;
                }

                var stored = _rule.Switch;
                var single = stored.Lost.Count == 0 && stored.Versions.Count == 1 ? stored.Versions[0] : null;
                if (_box.Refuses)
                {
                    return single == null || single.Kind != SwitchVersionKind.Refuses;
                }

                var none = stored.Lost.Count == 0 && stored.Versions.Count == 0;
                return !none && (single == null || single.Kind != SwitchVersionKind.Inherits);
            }
        }

        /// <summary>
        /// Whether a press on the tab holds a change, whether or not the tab can still write it:
        /// what an OK on a tab that stopped taking a press tells the operator was not kept.
        /// </summary>
        public bool Pending
        {
            get { return BoxChanged || _presses.Any(pressed => Holds(pressed.Key, pressed.Value)); }
        }

        /// <summary>Whether the box refuses as the tab shows it: ticked, mixed or lost, which OK leaves refusing.</summary>
        private bool BoxRefuses
        {
            get { return Box != GrantBox.Cleared; }
        }

        /// <summary>
        /// Whether a press cleared the box and holds that change: OK then writes the switch anew
        /// with what arrives, and every rule the node took off with it.
        /// </summary>
        private bool BoxSettles
        {
            get { return _box != null && !_box.Refuses && BoxChanged; }
        }

        /// <summary>
        /// What OK writes: the press that decides each row changed and the last press on the box,
        /// each with what the tab showed at it; <see cref="RuleChanges.None"/> where nothing
        /// changed or the tab takes no press.
        /// </summary>
        public RuleChanges Changes
        {
            get
            {
                if (!CanEdit)
                {
                    return RuleChanges.None;
                }

                var rows = new List<RowChange>();
                foreach (var pressed in _presses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!Holds(pressed.Key, pressed.Value))
                    {
                        continue;
                    }

                    var press = pressed.Value;
                    rows.Add(new RowChange(
                        press.Kind,
                        pressed.Key,
                        press.Kind == RowChangeKind.RemoveVersions ? null : press.Letters,
                        press.Removed.OrderBy(id => id, StringComparer.Ordinal),
                        press.Shown,
                        press.Below));
                }

                var box = BoxChanged ? new SwitchChange(_box.Refuses, _box.Shown, _box.Arriving, _box.Below) : null;
                return rows.Count == 0 && box == null ? RuleChanges.None : new RuleChanges(rows, box);
            }
        }

        /// <summary>
        /// Every row to show: what this node holds, and what arrives for the profiles it lets
        /// through, with every press on the tab laid over the reading as OK writes it.
        /// </summary>
        /// <remarks>
        /// Sorted by profile so that the same database reads the same way twice, and inherited rows
        /// sit among the rules of this node rather than under them, because a reader comparing two
        /// profiles wants them next to each other. The rows of a profile that are bound together
        /// stand next to each other in the order of their masks, which is the same in every copy.
        /// </remarks>
        public IList<GrantRow> Rows
        {
            get
            {
                var rows = new List<GrantRow>();
                foreach (var profile in Profiles())
                {
                    rows.AddRange(RowsOf(profile));
                }

                return rows;
            }
        }

        /// <summary>Whether the table shows a row for a profile.</summary>
        public bool Shows(string profile)
        {
            return profile != null && RowsOf(profile).Count > 0;
        }

        /// <summary>
        /// A rule chosen on a row: the profile is to be granted the letters of the mask, in a
        /// single version naming every version the tab shows for the row.
        /// </summary>
        /// <returns>Null when the sheet holds the rule now, or a sentence naming what is wrong.</returns>
        public string Set(string profile, string mask)
        {
            if (!CanEdit)
            {
                return GrantTab.NothingToChange;
            }

            // Keyed exactly, as everything here is: a name the issuer could not issue is no
            // profile, and trimming it would write a rule for the name that does match.
            var name = profile ?? string.Empty;
            if (name.Trim().Length == 0)
            {
                return GrantTab.NameTheProfile;
            }

            if (!AuthorityIssuer.IsUsableProfileName(name))
            {
                return GrantTab.ProfileNameRule;
            }

            var held = VerbMask.Read(mask);
            if (held == null)
            {
                return GrantTab.MaskRule;
            }

            Take(name, new Press(RowChangeKind.Set, AclGrant.Of(held), _rule, _below));
            return null;
        }

        /// <summary>
        /// The X on a row standing alone, and on a row of a group no version of which that reads
        /// would be left: the rule is to be taken off, with the letters arriving for the profile at
        /// the press, and the inherited rule takes the row's place until OK.
        /// </summary>
        /// <returns>False where the profile shows no such row or nothing here can be changed.</returns>
        public bool Remove(string profile)
        {
            if (!CanEdit || profile == null)
            {
                return false;
            }

            var shown = RowsOf(profile);
            var standing = shown.Count == 1 && (shown[0].Kind == GrantRowKind.Own || shown[0].Kind == GrantRowKind.Lost);
            var whole = shown.Count > 1 && shown.All(row => row.Kind == GrantRowKind.Lost && row.TakesRule);
            if (!standing && !whole)
            {
                return false;
            }

            var stored = _rule.Row(profile);
            if (stored.Versions.Count == 0 && stored.Lost.Count == 0)
            {
                // A rule chosen on the tab for a profile the store holds nothing for: taking the
                // rule off brings the row back to the store, and holds no change.
                _presses.Remove(profile);
                return true;
            }

            Take(profile, new Press(RowChangeKind.TakeOff, Arriving(profile), _rule, _below));
            return true;
        }

        /// <summary>
        /// The X on a row of a bound group: that version is to go, and each version left is to be
        /// written again; false where the profile's row holds no such version, no version that
        /// reads would be left to write again, or nothing here can be changed.
        /// </summary>
        /// <remarks>
        /// Once a single row is left, that row is a row like any other, on the same tab and before
        /// OK: a rule can be chosen on it and its X takes the rule off.
        /// </remarks>
        public bool RemoveVersion(string profile, string version)
        {
            if (!CanEdit || profile == null || version == null)
            {
                return false;
            }

            var removed = RowsOf(profile).FirstOrDefault(row =>
                row.Bound
                && row.Removable
                && !row.TakesRule
                && (row.Kind == GrantRowKind.Version || row.Kind == GrantRowKind.Lost)
                && string.Equals(row.Version, version, StringComparison.Ordinal));
            if (removed == null)
            {
                return false;
            }

            Press press;
            if (!_presses.TryGetValue(profile, out press) || press.Kind != RowChangeKind.RemoveVersions)
            {
                press = new Press(RowChangeKind.RemoveVersions, null, _rule, _below);
                _presses[profile] = press;
            }

            press.Removed.Add(version);
            return true;
        }

        /// <summary>
        /// The X on a limit row: the limit is to go, and what arrives at the press is to be let
        /// through; false where the profile shows no limit row or nothing here can be changed.
        /// </summary>
        public bool Settle(string profile)
        {
            if (!CanEdit || profile == null || !RowsOf(profile).Any(row => row.Kind == GrantRowKind.Limit))
            {
                return false;
            }

            Press press;
            var removing = _presses.TryGetValue(profile, out press) && press.Kind == RowChangeKind.RemoveVersions;

            // After versions were removed down to a version that takes the rule off, the press that
            // lets through what arrives is the rule taken off: a single version naming every
            // version the tab showed, the removed ones among them, where settling would write the
            // version left anew and leave the removed versions standing.
            Take(profile, new Press(removing ? RowChangeKind.TakeOff : RowChangeKind.Settle, Arriving(profile), _rule, _below));
            return true;
        }

        /// <summary>
        /// The X on a row, as the row offers it: the limit settled on a limit row, the version
        /// removed on a row of a group, and the rule taken off on any other row; false where the
        /// row offers no X or nothing here can be changed.
        /// </summary>
        public bool Forget(GrantRow row)
        {
            if (row == null || !row.Removable || !CanEdit)
            {
                return false;
            }

            if (row.Kind == GrantRowKind.Limit)
            {
                return Settle(row.Profile);
            }

            return row.Bound && !row.TakesRule ? RemoveVersion(row.Profile, row.Version) : Remove(row.Profile);
        }

        /// <summary>
        /// A press on the check box: a cleared box and a box in the mixed mark are ticked, a ticked
        /// box and a lost box are cleared; false where nothing here can be changed.
        /// </summary>
        /// <remarks>
        /// A press that clears keeps the letters arriving for every profile that at least a single
        /// letter arrives for, which the switch's version records at OK. No press brings the mixed
        /// mark back.
        /// </remarks>
        public bool PressBox()
        {
            if (!CanEdit)
            {
                return false;
            }

            var refuses = Box == GrantBox.Cleared || Box == GrantBox.Mixed;
            var arriving = new List<KeyValuePair<string, AclGrant>>();
            if (!refuses)
            {
                foreach (var profile in _above.Profiles.OrderBy(name => name, StringComparer.Ordinal))
                {
                    var letters = _above.For(profile);
                    if (letters != null && letters.Count > 0)
                    {
                        arriving.Add(new KeyValuePair<string, AclGrant>(profile, letters));
                    }
                }
            }

            _box = new BoxPress(refuses, _rule, arriving, _below);
            return true;
        }

        /// <summary>
        /// Takes the node's rules as a reading made since holds them: a row the operator did not
        /// touch follows that reading, and a row the operator touched keeps its press and what the
        /// tab showed at it.
        /// </summary>
        /// <param name="readOnlyReason">
        /// Why a change can no longer be written, or null. A reason given makes the tab read only
        /// from here on, with every press kept on the table and nothing written at OK; a tab that
        /// took no press never starts to take one.
        /// </param>
        public void Rebase(NodeRule rule, AclInheritance above, IEnumerable<string> names, string readOnlyReason)
        {
            _rule = rule ?? NodeRule.None;
            _above = above ?? AclInheritance.None;
            TakeNames(names);

            if (CanEdit && !string.IsNullOrEmpty(readOnlyReason))
            {
                ReadOnlyReason = readOnlyReason;
            }
        }

        /// <summary>
        /// Takes the standing versions at the nodes below the node, as the reading the sheet shows
        /// holds them, `StoredRules.Below`: what each press from now on keeps for the write above,
        /// `STORE.md` §617, so that an OK renews below only what the tab's reading held at the
        /// press. A press made before keeps what it kept.
        /// </summary>
        public void TakeBelow(IEnumerable<string> versions)
        {
            _below = RowChange.Kept(versions);
        }

        /// <summary>The names to offer and to mark rows by, from the database's list.</summary>
        private void TakeNames(IEnumerable<string> names)
        {
            _offered.Clear();
            _named = names == null ? null : new HashSet<string>(names, StringComparer.Ordinal);

            if (_named == null)
            {
                return;
            }

            _offered.AddRange(_named.Where(AuthorityIssuer.IsUsableProfileName));
            _offered.Sort(StringComparer.Ordinal);
        }

        /// <summary>
        /// A press taking the place of the presses before it on the profile's row, or taking them
        /// all away where OK would write nothing for it: the row is then as the store gives it.
        /// </summary>
        private void Take(string profile, Press press)
        {
            if (Holds(profile, press))
            {
                _presses[profile] = press;
            }
            else
            {
                _presses.Remove(profile);
            }
        }

        /// <summary>
        /// Whether the database names a profile, which is whether a rule for it grants anything:
        /// every profile where the sheet was given no list.
        /// </summary>
        private bool Named(string profile)
        {
            return _named == null || _named.Contains(profile);
        }

        /// <summary>What arrives for a profile from above, no letter where nothing does.</summary>
        private AclGrant Arriving(string profile)
        {
            return _above.For(profile) ?? AclGrant.Nothing;
        }

        /// <summary>What the box's press that clears showed arriving for a profile, no letter where it showed none.</summary>
        private AclGrant BoxArriving(string profile)
        {
            foreach (var pair in _box.Arriving)
            {
                if (string.Equals(pair.Key, profile, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            return AclGrant.Nothing;
        }

        /// <summary>
        /// The letters a rule taken off or a limit settled records once OK is written: what
        /// arrived at the press, and where the same OK clears the box, only what the box's press
        /// showed arriving as well.
        /// </summary>
        private AclGrant Recorded(string profile, Press press)
        {
            return BoxSettles ? press.Letters.Cut(BoxArriving(profile)) : press.Letters;
        }

        /// <summary>
        /// What the switch lets through for a profile once OK is written, or null where the box
        /// refuses then: renewed with what a press recorded for the profile, or with what the
        /// box's press showed arriving where a press cleared the box.
        /// </summary>
        private AclGrant SwitchAfter(string profile, Press press)
        {
            if (BoxRefuses)
            {
                return null;
            }

            var recorded = press != null && (press.Kind == RowChangeKind.TakeOff || press.Kind == RowChangeKind.Settle)
                ? Recorded(profile, press)
                : null;
            if (BoxSettles)
            {
                return recorded ?? BoxArriving(profile);
            }

            if (recorded != null && _rule.Switch.Versions.Any(version => version.Kind == SwitchVersionKind.Inherits))
            {
                return recorded;
            }

            return _rule.Switch.Limit(profile);
        }

        /// <summary>Every profile the reading, what arrives and the presses name, ascending.</summary>
        private IEnumerable<string> Profiles()
        {
            var profiles = new SortedSet<string>(_rule.Profiles, StringComparer.Ordinal);
            profiles.UnionWith(_above.Profiles);
            profiles.UnionWith(_presses.Keys);
            return profiles;
        }

        /// <summary>Whether a press on a profile's row holds a change OK writes.</summary>
        private bool Holds(string profile, Press press)
        {
            var stored = _rule.Row(profile);
            var single = stored.Lost.Count == 0 && stored.Versions.Count == 1 ? stored.Versions[0] : null;
            var none = stored.Lost.Count == 0 && stored.Versions.Count == 0;

            switch (press.Kind)
            {
                case RowChangeKind.Set:
                    return single == null || single.Kind != RowVersionKind.Grants || !single.Letters.SameAs(press.Letters);

                case RowChangeKind.TakeOff:
                    if (none || single == null)
                    {
                        return !none;
                    }

                    return BoxRefuses
                        ? single.Kind != RowVersionKind.TakenOffRefusing
                        : single.Kind != RowVersionKind.TakenOff || !single.Letters.SameAs(Recorded(profile, press));

                case RowChangeKind.RemoveVersions:
                    return press.Removed.Count > 0;

                default:
                    // A limit settled holds a change where the row shows a limit as the store
                    // gives it: while the box refuses, or once a press clears the box, none shows.
                    return Unpressed(profile).Any(row => row.Kind == GrantRowKind.Limit);
            }
        }

        /// <summary>The rows of a single profile, with its press laid over the reading as OK writes it.</summary>
        private IList<GrantRow> RowsOf(string profile)
        {
            Press press;
            if (!_presses.TryGetValue(profile, out press) || !Holds(profile, press))
            {
                return Unpressed(profile);
            }

            var stored = _rule.Row(profile);
            IList<GrantRow> rows;
            switch (press.Kind)
            {
                case RowChangeKind.Set:
                    rows = Shown(profile, new[] { new Seen(null, RowVersionKind.Grants, press.Letters) }, Array.Empty<string>(), true, null);
                    break;

                case RowChangeKind.TakeOff:
                    var refusing = BoxRefuses;
                    var taken = new Seen(null, refusing ? RowVersionKind.TakenOffRefusing : RowVersionKind.TakenOff, refusing ? AclGrant.Nothing : Recorded(profile, press));
                    rows = Shown(profile, new[] { taken }, Array.Empty<string>(), true, SwitchAfter(profile, press));
                    break;

                case RowChangeKind.RemoveVersions:
                    var row = press.Shown.Row(profile);
                    rows = Shown(
                        profile,
                        AsLeft(profile, row.Versions.Where(version => !press.Removed.Contains(version.Id))),
                        row.Lost.Where(id => !press.Removed.Contains(id)).ToList(),
                        true,
                        SwitchAfter(profile, null));
                    break;

                default:
                    var settled = Recorded(profile, press);
                    rows = Shown(
                        profile,
                        stored.Versions.Select(version => Taking(version) ? new Seen(version.Id, RowVersionKind.TakenOff, settled) : new Seen(version)).ToList(),
                        stored.Lost,
                        true,
                        SwitchAfter(profile, press));
                    break;
            }

            // A change waiting for OK keeps a row, also where nothing would show once OK is
            // written: where the box refuses, refused here as every profile the node does not
            // name is refused, whether or not a rule from above arrives for the profile.
            var left = BoxRefuses ? GrantState.RefusedHere : GrantState.Inherited;
            return rows.Count > 0 ? rows : new[] { Inherited(profile, AclGrant.Nothing, true, 1, left) };
        }

        /// <summary>
        /// The rows of a profile no press on the tab decides: as the store gives them, with each
        /// version the box's press renews shown as OK renews it.
        /// </summary>
        private IList<GrantRow> Unpressed(string profile)
        {
            var stored = _rule.Row(profile);
            var left = AsLeft(profile, stored.Versions);
            var renewed = left.Where((seen, at) => seen.Kind != stored.Versions[at].Kind || !seen.Letters.SameAs(new Seen(stored.Versions[at]).Letters)).Any();
            var rows = Shown(profile, left, stored.Lost, renewed, SwitchAfter(profile, null));
            if (renewed || !BoxChanged)
            {
                return rows;
            }

            // A press on the box that OK writes changes what other rows show as well: a rule from
            // above refused or arriving again, a limit coming or going. Each row that shows a state
            // or letters the tab did not show before the press is a row that changed; a row that
            // goes needs no mark.
            var before = WithoutTheBox(profile);
            return rows
                .Select(row => before.Any(shown => SameShown(shown, row)) ? row : row.AsChanged())
                .ToList();
        }

        /// <summary>The rows of a profile no press decides, as the tab shows them with no press on the box.</summary>
        private IList<GrantRow> WithoutTheBox(string profile)
        {
            var pressed = _box;
            _box = null;
            try
            {
                return Unpressed(profile);
            }
            finally
            {
                _box = pressed;
            }
        }

        /// <summary>Whether two rows show the same: the same kind of row, version, states and letters.</summary>
        private static bool SameShown(GrantRow first, GrantRow second)
        {
            return first.Kind == second.Kind
                   && string.Equals(first.Version, second.Version, StringComparison.Ordinal)
                   && string.Equals(first.Mask, second.Mask, StringComparison.Ordinal)
                   && first.States.SequenceEqual(second.States);
        }

        /// <summary>
        /// The standing versions of a row as OK leaves them where no press of the row decides:
        /// where a press clears the box, each version that takes the rule off records what the
        /// box's press showed arriving.
        /// </summary>
        private IReadOnlyList<Seen> AsLeft(string profile, IEnumerable<RowVersion> versions)
        {
            return versions
                .Select(version => BoxSettles && Taking(version)
                    ? new Seen(version.Id, RowVersionKind.TakenOff, BoxArriving(profile))
                    : new Seen(version))
                .ToList();
        }

        private static bool Taking(RowVersion version)
        {
            return version.Kind == RowVersionKind.TakenOff || version.Kind == RowVersionKind.TakenOffRefusing;
        }

        /// <summary>The rows of a profile whose row holds these versions, standing and lost, once OK is written.</summary>
        /// <param name="switchAfter">What the switch lets through for the profile then, or null where the box refuses.</param>
        private IList<GrantRow> Shown(string profile, IReadOnlyList<Seen> standing, IReadOnlyList<string> lost, bool changed, AclGrant switchAfter)
        {
            var count = standing.Count + lost.Count;
            if (count > 1)
            {
                var granted = Granted(profile, standing, lost, switchAfter);
                var readable = standing.Count(version => version.Kind != RowVersionKind.OutOfForm);

                // In a group no version of which reads, every row takes a rule and its X takes the
                // rule off: no version is left to write again, so no version can be removed alone.
                var whole = readable == 0;
                var group = new List<GrantRow>();
                foreach (var version in standing)
                {
                    var outOfForm = version.Kind == RowVersionKind.OutOfForm;
                    var kind = outOfForm ? GrantRowKind.Lost : GrantRowKind.Version;
                    var state = outOfForm
                        ? GrantState.Lost
                        : version.Kind == RowVersionKind.Grants ? GrantState.Version : GrantState.TakenOff;
                    var left = readable - (outOfForm ? 0 : 1);
                    group.Add(Row(
                        profile, kind, version.Id, outOfForm ? AclGrant.Nothing : version.Letters, new[] { state }, changed, count,
                        granted, outOfForm, whole, whole || left > 0));
                }

                foreach (var id in lost)
                {
                    group.Add(Row(profile, GrantRowKind.Lost, id, AclGrant.Nothing, new[] { GrantState.Lost }, changed, count, granted, false, whole, true));
                }

                return group
                    .OrderBy(row => row.Mask, StringComparer.Ordinal)
                    .ThenBy(row => row.Version, StringComparer.Ordinal)
                    .ToList();
            }

            if (lost.Count == 1)
            {
                return new[] { Row(profile, GrantRowKind.Lost, lost[0], AclGrant.Nothing, new[] { GrantState.Lost }, changed, 1, AclGrant.Nothing, false, true, true) };
            }

            if (standing.Count == 1 && standing[0].Kind == RowVersionKind.OutOfForm)
            {
                return new[] { Row(profile, GrantRowKind.Lost, standing[0].Id, AclGrant.Nothing, new[] { GrantState.Lost }, changed, 1, AclGrant.Nothing, true, true, true) };
            }

            if (standing.Count == 1 && standing[0].Kind == RowVersionKind.Grants)
            {
                return new[] { Row(profile, GrantRowKind.Own, standing[0].Id, standing[0].Letters, NoState, changed, 1, standing[0].Letters, false, true, true) };
            }

            // The row lets through what arrives: no row of its own, the inherited rule where any
            // arrives, and under it the limit where the node cuts what arrives.
            var arriving = _above.For(profile);
            if (arriving == null)
            {
                return Array.Empty<GrantRow>();
            }

            var inherited = BoxRefuses ? GrantState.RefusedHere : GrantState.Inherited;
            var limit = switchAfter == null ? null : standing.Aggregate(AclGrant.Every, (held, version) => held.Cut(version.Letters)).Cut(switchAfter);
            if (limit == null || arriving.Cut(limit).SameAs(arriving))
            {
                return new[] { Inherited(profile, arriving, changed, 1, inherited) };
            }

            var refusing = standing.Any(version => version.Kind == RowVersionKind.TakenOffRefusing);
            return new[]
            {
                Inherited(profile, arriving, changed, 2, inherited),
                Row(profile, GrantRowKind.Limit, null, limit, new[] { GrantState.Limit }, changed, 2, arriving.Cut(limit), refusing, false, true),
            };
        }

        /// <summary>
        /// What the gate grants a profile at the node whose row holds these versions once OK is
        /// written: nothing for a lost version or a text out of form, the letters every granting
        /// version holds where all of them grant, and otherwise what arrives cut to what the node
        /// lets through, nothing where the box refuses.
        /// </summary>
        private AclGrant Granted(string profile, IReadOnlyList<Seen> standing, IReadOnlyList<string> lost, AclGrant switchAfter)
        {
            if (lost.Count > 0 || standing.Any(version => version.Kind == RowVersionKind.OutOfForm))
            {
                return AclGrant.Nothing;
            }

            var letters = standing.Aggregate(AclGrant.Every, (held, version) => held.Cut(version.Letters));
            if (standing.Count > 0 && standing.All(version => version.Kind == RowVersionKind.Grants))
            {
                return letters;
            }

            return switchAfter == null ? AclGrant.Nothing : Arriving(profile).Cut(letters.Cut(switchAfter));
        }

        /// <summary>The inherited row of a profile, with these letters arriving, in this state.</summary>
        private GrantRow Inherited(string profile, AclGrant arriving, bool changed, int group, GrantState state)
        {
            return Row(
                profile,
                GrantRowKind.Inherited,
                null,
                arriving,
                new[] { state },
                changed,
                group,
                state == GrantState.RefusedHere ? AclGrant.Nothing : arriving,
                false,
                true,
                false);
        }

        private GrantRow Row(
            string profile,
            GrantRowKind kind,
            string version,
            AclGrant letters,
            IEnumerable<GrantState> states,
            bool changed,
            int group,
            AclGrant granted,
            bool unreadable,
            bool takesRule,
            bool removable)
        {
            var held = new List<GrantState>();
            if (!Named(profile))
            {
                held.Add(GrantState.NoProfile);
            }

            held.AddRange(states);
            return new GrantRow(
                profile, kind, version, letters, held.AsReadOnly(), changed, _above.For(profile), group, granted, unreadable, takesRule, removable);
        }

        private static readonly GrantState[] NoState = Array.Empty<GrantState>();

        /// <summary>A version of a row as OK leaves it: its identifier, what its text reads as, and its letters.</summary>
        private sealed class Seen
        {
            public Seen(RowVersion version)
                : this(version.Id, version.Kind, version.Letters)
            {
            }

            public Seen(string id, RowVersionKind kind, AclGrant letters)
            {
                Id = id;
                Kind = kind;
                Letters = letters ?? AclGrant.Nothing;
            }

            /// <summary>The version's identifier, or null for a version OK writes.</summary>
            public string Id { get; }

            public RowVersionKind Kind { get; }

            public AclGrant Letters { get; }
        }

        /// <summary>A press on a profile's row, with the node's rules as the tab showed them.</summary>
        private sealed class Press
        {
            public Press(RowChangeKind kind, AclGrant letters, NodeRule shown, IReadOnlyCollection<string> below)
            {
                Kind = kind;
                Letters = letters;
                Shown = shown;
                Below = below;
                Removed = new HashSet<string>(StringComparer.Ordinal);
            }

            public RowChangeKind Kind { get; }

            /// <summary>The letters wanted for a rule chosen, the letters arriving at the press for a rule taken off or settled.</summary>
            public AclGrant Letters { get; }

            /// <summary>The identifiers of the versions removed from a bound group.</summary>
            public HashSet<string> Removed { get; }

            public NodeRule Shown { get; }

            /// <summary>The standing versions at the nodes below as the tab's reading held them at the press.</summary>
            public IReadOnlyCollection<string> Below { get; }
        }

        /// <summary>The last press on the box, with the node's rules as the tab showed them.</summary>
        private sealed class BoxPress
        {
            public BoxPress(bool refuses, NodeRule shown, IEnumerable<KeyValuePair<string, AclGrant>> arriving, IReadOnlyCollection<string> below)
            {
                Refuses = refuses;
                Shown = shown;
                Arriving = arriving.ToList();
                Below = below;
            }

            public bool Refuses { get; }

            public NodeRule Shown { get; }

            /// <summary>The standing versions at the nodes below as the tab's reading held them at the press.</summary>
            public IReadOnlyCollection<string> Below { get; }

            public IReadOnlyList<KeyValuePair<string, AclGrant>> Arriving { get; }
        }
    }
}
