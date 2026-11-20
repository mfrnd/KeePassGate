using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>What a standing version of a profile's row at a node holds, as its text reads.</summary>
    public enum RowVersionKind
    {
        /// <summary>`row:` and letters: the profile is granted the letters, an empty rule granting none.</summary>
        Grants = 0,

        /// <summary>`off:` and letters: the rule was taken off, recording the letters that arrived then.</summary>
        TakenOff,

        /// <summary>`off:deny`: the rule was taken off while the node's own switch refused, recording no letter.</summary>
        TakenOffRefusing,

        /// <summary>A text out of form, which refuses the profile at the node as a lost version does.</summary>
        OutOfForm,
    }

    /// <summary>What a standing version of a node's switch holds, as its text reads.</summary>
    public enum SwitchVersionKind
    {
        /// <summary>`sw:deny`: the node refuses every profile its rows do not let through.</summary>
        Refuses = 0,

        /// <summary>`sw:inherit` and its items: the node lets through what arrives, recording for each profile the letters that arrived then.</summary>
        Inherits,

        /// <summary>A text out of form, which reads as refusing.</summary>
        OutOfForm,
    }

    /// <summary>
    /// A version of an object of a node's rules as the store holds it: its identifier, its text,
    /// and the identifiers it names.
    /// </summary>
    /// <remarks>
    /// **What a version names is read on first use**, and kept with the version. A version's
    /// `after` grows by an identifier for each edit and each renewal and is never pruned, and the
    /// walk never reads it: only the acts and the editor do. So a version the reading holds keeps
    /// the text of its `after` as the store holds it, and nothing parses it at a hold that only
    /// asks a rule.
    /// </remarks>
    public sealed class RuleVersion
    {
        /// <summary>The text of `after` as the store holds it, for a version read from the store.</summary>
        private string _named;

        private IReadOnlyList<string> _after;

        /// <summary>A version under this identifier holding this text and naming these identifiers in `after`.</summary>
        public RuleVersion(string id, string text, IEnumerable<string> after)
            : this(id, text)
        {
            _after = (after ?? Array.Empty<string>()).Where(name => name != null).ToList().AsReadOnly();
        }

        private RuleVersion(string id, string text)
        {
            Id = id;
            Text = text;
        }

        /// <summary>The identifier of the version, 32 lowercase hexadecimal characters.</summary>
        public string Id { get; private set; }

        /// <summary>The text of its `doc`, as the store holds it, in form or not.</summary>
        public string Text { get; private set; }

        /// <summary>The identifiers its `after` names, as the store holds them.</summary>
        public IReadOnlyList<string> After
        {
            get
            {
                // Two threads asking at once each read the same text into equal lists, so either
                // list kept is the answer.
                var after = Volatile.Read(ref _after);
                if (after == null)
                {
                    after = (StoreFormat.Identifiers(_named) ?? Array.Empty<string>()).ToList().AsReadOnly();
                    Volatile.Write(ref _after, after);
                }

                return after;
            }
        }

        /// <summary>
        /// A version as the store holds it, with the text of its `after` read only where
        /// <see cref="After"/> is asked: an `after` out of form names nothing.
        /// </summary>
        internal static RuleVersion Stored(string id, string text, string after)
        {
            return new RuleVersion(id, text) { _named = after };
        }
    }

    /// <summary>
    /// What the store holds of a single object of a node's rules: a profile's row, or the
    /// node's switch.
    /// </summary>
    public sealed class RuleObject
    {
        /// <summary>
        /// The object of this profile's row, or of the switch where <paramref name="profile"/> is
        /// null, with its standing versions and the identifiers of its lost versions.
        /// </summary>
        public RuleObject(string profile, IEnumerable<RuleVersion> standing, IEnumerable<string> lost)
        {
            Profile = profile;
            Standing = (standing ?? Array.Empty<RuleVersion>()).Where(version => version != null).ToList().AsReadOnly();
            Lost = (lost ?? Array.Empty<string>()).Where(id => id != null).ToList().AsReadOnly();
        }

        /// <summary>The profile whose row this is, or null for the switch.</summary>
        public string Profile { get; private set; }

        /// <summary>The standing versions of the object.</summary>
        public IReadOnlyList<RuleVersion> Standing { get; private set; }

        /// <summary>The identifiers of the lost versions of the object.</summary>
        public IReadOnlyList<string> Lost { get; private set; }
    }

    /// <summary>A standing version of a profile's row, as its text reads.</summary>
    public sealed class RowVersion
    {
        private readonly RuleVersion _version;

        /// <summary>A version of a row, read from what the store holds of it.</summary>
        internal RowVersion(RuleVersion version)
        {
            AclGrant letters;
            _version = version;
            Id = version.Id;
            Kind = RuleText.ReadRow(version.Text, out letters);
            Letters = letters;
        }

        /// <summary>The identifier of the version, 32 lowercase hexadecimal characters.</summary>
        public string Id { get; private set; }

        /// <summary>What the version holds.</summary>
        public RowVersionKind Kind { get; private set; }

        /// <summary>The letters it grants or records; no letter for `off:deny` and for a text out of form.</summary>
        public AclGrant Letters { get; private set; }

        /// <summary>The identifiers its `after` names, read on first use.</summary>
        public IReadOnlyList<string> After
        {
            get { return _version.After; }
        }
    }

    /// <summary>A profile's row at a node: its standing and lost versions, and what the walk reads of them.</summary>
    /// <remarks>
    /// `STORE.md` §617. A row **refuses** for a lost version or a text out of form, whatever else
    /// stands. It **answers** by itself where every standing version grants, with the letters all
    /// of them hold. Otherwise it **lets through** what arrives from above, cut to its limit: the
    /// letters every granting version and every version that took the rule off hold together,
    /// `off:deny` holding none. Several versions only ever narrow each other, whatever order they
    /// stand in.
    /// </remarks>
    public sealed class RowRule
    {
        /// <summary>The row of a profile, read from what the store holds of its object.</summary>
        internal RowRule(string profile, IEnumerable<RuleVersion> standing, IEnumerable<string> lost)
        {
            Profile = profile;
            Versions = Ascending(standing).Select(version => new RowVersion(version)).ToList().AsReadOnly();
            Lost = (lost ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly();

            Refuses = Lost.Count > 0 || Versions.Any(version => version.Kind == RowVersionKind.OutOfForm);
            Answers = !Refuses && Versions.Count > 0 && Versions.All(version => version.Kind == RowVersionKind.Grants);

            // No letter granted where the row refuses, whatever stands beside the lost version or
            // the text out of form.
            var granting = Versions.Where(version => version.Kind == RowVersionKind.Grants).ToList();
            Grants = Refuses || granting.Count == 0 ? null : granting.Aggregate(AclGrant.Every, (held, version) => held.Cut(version.Letters));

            // No letter where the row refuses, so nothing reading a limit can let anything through
            // a row the walk refuses at.
            Limit = Refuses ? AclGrant.Nothing : Versions.Aggregate(AclGrant.Every, (held, version) => held.Cut(version.Letters));
        }

        /// <summary>The row of a profile the node holds nothing for: no version, and every letter let through.</summary>
        private RowRule(string profile)
        {
            Profile = profile;
            Versions = Array.Empty<RowVersion>();
            Lost = Array.Empty<string>();
            Limit = AclGrant.Every;
        }

        /// <summary>The profile whose row this is.</summary>
        public string Profile { get; private set; }

        /// <summary>The standing versions, ascending by identifier.</summary>
        public IReadOnlyList<RowVersion> Versions { get; private set; }

        /// <summary>
        /// The identifiers of the lost versions, ascending: each the identifier a version written
        /// for the row names to replace the lost version.
        /// </summary>
        public IReadOnlyList<string> Lost { get; private set; }

        /// <summary>Whether the row refuses the profile at the node: a version is lost, or a text is out of form.</summary>
        public bool Refuses { get; private set; }

        /// <summary>Whether the row answers by itself: it does not refuse, a version stands, and every standing version grants.</summary>
        public bool Answers { get; private set; }

        /// <summary>The letters every standing version that grants holds, or null where the row refuses or no standing version grants.</summary>
        public AclGrant Grants { get; private set; }

        /// <summary>
        /// The letters the row lets through from above where it neither refuses nor answers: what
        /// every standing version that grants and every standing version that takes the rule off
        /// holds, `off:deny` holding none; every letter for a row with no version.
        /// </summary>
        public AclGrant Limit { get; private set; }

        /// <summary>The row of a profile the node holds nothing for, which cuts nothing.</summary>
        internal static RowRule Unnamed(string profile)
        {
            return new RowRule(profile);
        }

        /// <summary>
        /// The versions given, each once, ascending by identifier, then by text and by what each
        /// names: an order that depends on nothing but the versions themselves.
        /// </summary>
        /// <remarks>
        /// What a version names is read only to tell apart versions alike in identifier and text,
        /// which no reading of a store holds, since an identifier is a single version's: so a hold
        /// never reads it here.
        /// </remarks>
        internal static IEnumerable<RuleVersion> Ascending(IEnumerable<RuleVersion> versions)
        {
            var given = (versions ?? Array.Empty<RuleVersion>()).Where(version => version != null).ToList();
            if (given.Count < 2)
            {
                return given;
            }

            return given
                .GroupBy(version => (Id: version.Id ?? string.Empty, Text: version.Text ?? string.Empty))
                .OrderBy(alike => alike.Key.Id, StringComparer.Ordinal)
                .ThenBy(alike => alike.Key.Text, StringComparer.Ordinal)
                .SelectMany(Once);
        }

        /// <summary>Versions alike in identifier and text, each once by what it names, ascending by it.</summary>
        private static IEnumerable<RuleVersion> Once(IEnumerable<RuleVersion> alike)
        {
            var held = alike.ToList();
            return held.Count == 1
                ? held
                : held.GroupBy(version => string.Join(" ", version.After), StringComparer.Ordinal)
                    .OrderBy(named => named.Key, StringComparer.Ordinal)
                    .Select(named => named.First());
        }
    }

    /// <summary>A standing version of a node's switch, as its text reads.</summary>
    public sealed class SwitchVersion
    {
        private readonly IReadOnlyDictionary<string, AclGrant> _letters;
        private readonly RuleVersion _version;

        /// <summary>A version of a switch, read from what the store holds of it.</summary>
        internal SwitchVersion(RuleVersion version)
        {
            IReadOnlyDictionary<string, AclGrant> letters;
            _version = version;
            Id = version.Id;
            Kind = RuleText.ReadSwitch(version.Text, out letters);
            _letters = letters;
            Profiles = letters.Keys.OrderBy(profile => profile, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        /// <summary>The identifier of the version, 32 lowercase hexadecimal characters.</summary>
        public string Id { get; private set; }

        /// <summary>What the version holds.</summary>
        public SwitchVersionKind Kind { get; private set; }

        /// <summary>The profiles the version records letters for, ascending by ordinal; none unless it inherits.</summary>
        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>The identifiers its `after` names, read on first use.</summary>
        public IReadOnlyList<string> After
        {
            get { return _version.After; }
        }

        /// <summary>The letters the version records for a profile: no letter for a profile it does not name, and none unless it inherits.</summary>
        public AclGrant LettersFor(string profile)
        {
            AclGrant letters;
            return profile != null && _letters.TryGetValue(profile, out letters) ? letters : AclGrant.Nothing;
        }
    }

    /// <summary>A node's switch: its standing and lost versions, and what the walk reads of them.</summary>
    /// <remarks>
    /// `STORE.md` §617. The switch **refuses** where any standing version refuses, a text out of
    /// form among them, and where a version is lost, so versions that differ in whether they
    /// refuse refuse together. Where it lets the walk go on, it lets through for a profile what
    /// every standing version recorded for that profile, and nothing for a profile a version
    /// recorded nothing for. A switch with no version cuts nothing.
    /// </remarks>
    public sealed class SwitchRule
    {
        /// <summary>The switch of a node, read from what the store holds of its object.</summary>
        internal SwitchRule(IEnumerable<RuleVersion> standing, IEnumerable<string> lost)
        {
            Versions = RowRule.Ascending(standing).Select(version => new SwitchVersion(version)).ToList().AsReadOnly();
            Lost = (lost ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly();
            Refuses = Lost.Count > 0 || Versions.Any(version => version.Kind != SwitchVersionKind.Inherits);
            Mixed = Versions.Any(version => version.Kind == SwitchVersionKind.Refuses)
                    && Versions.Any(version => version.Kind == SwitchVersionKind.Inherits);
        }

        /// <summary>The standing versions, ascending by identifier.</summary>
        public IReadOnlyList<SwitchVersion> Versions { get; private set; }

        /// <summary>
        /// The identifiers of the lost versions, ascending: each the identifier a version written
        /// for the switch names to replace the lost version.
        /// </summary>
        public IReadOnlyList<string> Lost { get; private set; }

        /// <summary>
        /// Whether the switch refuses every profile the node's rows do not let through: a standing
        /// version refuses, a version is lost, or a text is out of form.
        /// </summary>
        public bool Refuses { get; private set; }

        /// <summary>Whether a standing version that refuses stands beside a standing version that inherits.</summary>
        public bool Mixed { get; private set; }

        /// <summary>
        /// The letters the switch lets through for a profile where it does not refuse: what every
        /// standing version that inherits records for the profile, no letter where a version does
        /// not name it; every letter where no version stands.
        /// </summary>
        public AclGrant Limit(string profile)
        {
            // No letter where the switch refuses, so nothing reading a limit can let anything
            // through a switch the walk refuses at.
            return Refuses
                ? AclGrant.Nothing
                : Versions.Aggregate(AclGrant.Every, (held, version) => held.Cut(version.LettersFor(profile)));
        }
    }

    /// <summary>
    /// What a node holds for the resolver and the editor, `STORE.md` §617: the row of each
    /// profile and the switch, as the standing and lost versions of their objects read.
    /// </summary>
    /// <remarks>
    /// **Built in a single place**, this constructor, from what the store holds of each object,
    /// so the reading and the tests' own rules cannot come to mean two things by the same versions.
    /// **The same whatever order it is given things in**: versions are kept ascending by their
    /// identifiers, an object given in parts is read whole, and nothing read depends on which
    /// version came first.
    /// </remarks>
    public sealed class NodeRule
    {
        private readonly IReadOnlyDictionary<string, RowRule> _rows;

        /// <summary>
        /// The rules of a node from what the store holds of each of its objects: the single
        /// constructor that the reading and the tests both build a node's rules by.
        /// </summary>
        public NodeRule(IEnumerable<RuleObject> objects)
        {
            var given = (objects ?? Array.Empty<RuleObject>()).Where(item => item != null).ToList();

            var rows = new Dictionary<string, RowRule>(StringComparer.Ordinal);
            foreach (var row in given.Where(item => item.Profile != null).GroupBy(item => item.Profile, StringComparer.Ordinal))
            {
                var standing = row.SelectMany(item => item.Standing).ToList();
                var lost = row.SelectMany(item => item.Lost).ToList();
                if (standing.Count > 0 || lost.Count > 0)
                {
                    rows.Add(row.Key, new RowRule(row.Key, standing, lost));
                }
            }

            var switches = given.Where(item => item.Profile == null).ToList();
            _rows = rows;
            Profiles = rows.Keys.OrderBy(profile => profile, StringComparer.Ordinal).ToList().AsReadOnly();
            Switch = new SwitchRule(switches.SelectMany(item => item.Standing), switches.SelectMany(item => item.Lost));
        }

        /// <summary>The rules of a node the store holds nothing for: no row, and a switch with no version.</summary>
        public static NodeRule None { get; } = new NodeRule(Array.Empty<RuleObject>());

        /// <summary>Every profile that has a row at the node, standing or lost, ascending by ordinal.</summary>
        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>The node's switch: a switch with no version where nothing of it stands or is lost.</summary>
        public SwitchRule Switch { get; private set; }

        /// <summary>The row of a profile: a row with no version, which cuts nothing, for a profile with no row here.</summary>
        public RowRule Row(string profile)
        {
            RowRule row;
            return profile != null && _rows.TryGetValue(profile, out row) ? row : RowRule.Unnamed(profile);
        }

        /// <summary>
        /// The letters the node lets through from above for a profile where its row neither
        /// refuses nor answers and its switch does not refuse: what the row's limit and the
        /// switch's limit for the profile hold together.
        /// </summary>
        public AclGrant Limit(string profile)
        {
            return Row(profile).Limit.Cut(Switch.Limit(profile));
        }
    }
}
