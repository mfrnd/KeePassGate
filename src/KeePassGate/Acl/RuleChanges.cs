using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Acl
{
    /// <summary>What a press on a profile's row of the KPG tab of a node's dialog asks for.</summary>
    public enum RowChangeKind
    {
        /// <summary>A rule chosen on the row: the profile is to be granted the letters wanted.</summary>
        Set = 0,

        /// <summary>The X on a row standing alone: the rule is taken off.</summary>
        TakeOff,

        /// <summary>The X on rows of a bound group: the versions removed go, and each version left is written again.</summary>
        RemoveVersions,

        /// <summary>The X on a limit row: the limit goes, and what arrives is let through.</summary>
        Settle,
    }

    /// <summary>
    /// The press that decides what OK writes for a profile's row, with the node's rules as the
    /// tab showed them at that press.
    /// </summary>
    /// <remarks>
    /// **What the tab showed, and nothing read at OK.** A version the OK writes names what the tab
    /// showed at the press and never a version that reached the database while the dialog stood,
    /// `STORE.md` §629, so the rules shown are held here with the press, and the letters shown
    /// arriving with them, and the versions below the node that the write above may renew.
    /// </remarks>
    public sealed class RowChange
    {
        /// <summary>A press of this kind on this profile's row, with what the tab showed at it.</summary>
        /// <param name="letters">
        /// For <see cref="RowChangeKind.Set"/> the letters wanted; for
        /// <see cref="RowChangeKind.TakeOff"/> and <see cref="RowChangeKind.Settle"/> the letters
        /// the tab showed arriving for the profile at the press; null for
        /// <see cref="RowChangeKind.RemoveVersions"/>.
        /// </param>
        /// <param name="removed">For <see cref="RowChangeKind.RemoveVersions"/> the identifiers of every version removed; none otherwise.</param>
        /// <param name="shown">The node's rules as the tab showed them at the press.</param>
        /// <param name="below">
        /// The identifiers of the standing versions at each node below the node that held a version
        /// letting a profile through, as the tab's reading held them at the press,
        /// `StoredRules.Below`; none where the tab read none.
        /// </param>
        public RowChange(RowChangeKind kind, string profile, AclGrant letters, IEnumerable<string> removed, NodeRule shown, IEnumerable<string> below)
        {
            Kind = kind;
            Profile = profile;
            Letters = letters;
            Removed = (removed ?? Enumerable.Empty<string>())
                .Where(id => id != null)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
            Shown = shown ?? NodeRule.None;
            Below = Kept(below);
        }

        /// <summary>What the press asks for.</summary>
        public RowChangeKind Kind { get; private set; }

        /// <summary>The profile whose row was pressed.</summary>
        public string Profile { get; private set; }

        /// <summary>
        /// The letters wanted for a rule chosen, the letters the tab showed arriving for the
        /// profile at the press for an X on a row standing alone or on a limit row, and null for
        /// versions removed.
        /// </summary>
        public AclGrant Letters { get; private set; }

        /// <summary>The identifiers of the versions removed from a bound group, ascending; none for any other press.</summary>
        public IReadOnlyList<string> Removed { get; private set; }

        /// <summary>
        /// The node's rules as the tab showed them at the press: what a version written names in
        /// `after`, and what each version renewed at the node is taken from.
        /// </summary>
        public NodeRule Shown { get; private set; }

        /// <summary>
        /// The identifiers of the standing versions at the nodes below that the tab's reading held
        /// at the press: the only versions below the write above may renew, `STORE.md` §617.
        /// </summary>
        public IReadOnlyCollection<string> Below { get; private set; }

        /// <summary>These identifiers, each once, none for none given.</summary>
        internal static IReadOnlyCollection<string> Kept(IEnumerable<string> below)
        {
            return new HashSet<string>((below ?? Enumerable.Empty<string>()).Where(id => id != null), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The last press on the check box of the KPG tab of a node's dialog, with the node's rules as
    /// the tab showed them at that press.
    /// </summary>
    public sealed class SwitchChange
    {
        /// <summary>A press leaving the box refusing or inheriting, with what the tab showed at it.</summary>
        /// <param name="arriving">
        /// At a press that clears the box, the letters the tab showed arriving for every profile
        /// that at least a single letter arrived for; none at a press that ticks it.
        /// </param>
        /// <param name="below">
        /// The identifiers of the standing versions at each node below the node that held a version
        /// letting a profile through, as the tab's reading held them at the press; none where the
        /// tab read none.
        /// </param>
        public SwitchChange(bool refuses, NodeRule shown, IEnumerable<KeyValuePair<string, AclGrant>> arriving, IEnumerable<string> below)
        {
            Refuses = refuses;
            Shown = shown ?? NodeRule.None;
            Below = RowChange.Kept(below);

            // A profile given twice holds what both hold, the narrower way, as the text of the
            // switch writes it.
            var letters = new SortedDictionary<string, AclGrant>(StringComparer.Ordinal);
            if (!refuses)
            {
                foreach (var pair in arriving ?? Enumerable.Empty<KeyValuePair<string, AclGrant>>())
                {
                    if (pair.Key == null || pair.Value == null)
                    {
                        continue;
                    }

                    AclGrant held;
                    letters[pair.Key] = letters.TryGetValue(pair.Key, out held) ? held.Cut(pair.Value) : pair.Value;
                }
            }

            Arriving = letters;
        }

        /// <summary>Whether the box is to refuse every profile the node does not name.</summary>
        public bool Refuses { get; private set; }

        /// <summary>
        /// The node's rules as the tab showed them at the press: what the switch's version written
        /// names in `after`, and what each version renewed at the node is taken from.
        /// </summary>
        public NodeRule Shown { get; private set; }

        /// <summary>
        /// The identifiers of the standing versions at the nodes below that the tab's reading held
        /// at the press: the only versions below the write above may renew, `STORE.md` §617.
        /// </summary>
        public IReadOnlyCollection<string> Below { get; private set; }

        /// <summary>
        /// The letters the tab showed arriving at the press for every profile that at least a
        /// single letter arrived for, by profile in ordinal order; none where the press ticks the
        /// box.
        /// </summary>
        public IReadOnlyDictionary<string, AclGrant> Arriving { get; private set; }
    }

    /// <summary>
    /// What the KPG tab of a node's dialog holds for OK, `STORE.md` §629: the press that decides
    /// each row changed, and the last press on the box.
    /// </summary>
    public sealed class RuleChanges
    {
        /// <summary>These rows changed, a single change for each profile, and this change of the box or null.</summary>
        /// <exception cref="ArgumentException">A profile is given a change twice.</exception>
        public RuleChanges(IEnumerable<RowChange> rows, SwitchChange box)
        {
            var given = (rows ?? Enumerable.Empty<RowChange>()).Where(row => row != null).ToList();
            if (given.GroupBy(row => row.Profile, StringComparer.Ordinal).Any(same => same.Count() > 1))
            {
                throw new ArgumentException("A profile's row holds a single change.", nameof(rows));
            }

            Rows = given.OrderBy(row => row.Profile, StringComparer.Ordinal).ToList().AsReadOnly();
            Switch = box;
        }

        /// <summary>No change at all: what a tab holds before a press, and after presses that brought every row back.</summary>
        public static RuleChanges None { get; } = new RuleChanges(null, null);

        /// <summary>The changes of the rows, a single change for each profile, ascending by profile.</summary>
        public IReadOnlyList<RowChange> Rows { get; private set; }

        /// <summary>The change of the box, or null where the box holds none.</summary>
        public SwitchChange Switch { get; private set; }

        /// <summary>Whether nothing is held: no row and not the box.</summary>
        public bool IsEmpty
        {
            get { return Rows.Count == 0 && Switch == null; }
        }
    }
}
