using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The rules a reading of the store holds, `STORE.md` §617: each node's rules worked out from
    /// the standing and the lost versions of the objects at the node.
    /// </summary>
    /// <remarks>
    /// **Worked out once, as the reading's objects are walked**, linear in the standing versions,
    /// and kept with that reading and no longer: a node's rules are looked up by its identifier,
    /// and a node the reading holds nothing for holds `NodeRule.None`.
    ///
    /// **An object out of form is the rule of no node.** Its versions are read by nothing and stop
    /// nothing, as a name out of form is no name on the list, `STORE.md` §616. A version's text
    /// out of form is read where its object is in form, and refuses there.
    ///
    /// **Total.** Nothing a store holds makes this throw: a member a version lacks is a text out
    /// of form, and an `after` out of form has already failed its version in the core.
    /// </remarks>
    internal sealed class StoredRules
    {
        private static readonly StoredRules Empty =
            new StoredRules(new Dictionary<string, List<RuleObject>>(StringComparer.Ordinal));

        private readonly IReadOnlyDictionary<string, NodeRule> _nodes;
        private readonly IReadOnlyDictionary<string, List<RuleObject>> _objects;
        private HashSet<string> _lettingThrough;

        private StoredRules(IReadOnlyDictionary<string, List<RuleObject>> objects)
        {
            _objects = objects;
            _nodes = objects.ToDictionary(pair => pair.Key, pair => new NodeRule(pair.Value), StringComparer.Ordinal);
            Profiles = _nodes.Values.SelectMany(rule => rule.Profiles)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(profile => profile, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// The rules of this reading, none where it holds nothing in force; total, so that nothing
        /// a store holds makes it throw.
        /// </summary>
        public static StoredRules Of(StoreReading reading)
        {
            var inForce = reading == null ? null : reading.InForce;
            if (inForce == null)
            {
                return Empty;
            }

            var objects = new Dictionary<string, List<RuleObject>>(StringComparer.Ordinal);
            foreach (var objectText in inForce.Objects)
            {
                string node;
                string profile;
                if (!RuleText.ReadObject(objectText, out node, out profile))
                {
                    continue;
                }

                var standing = inForce.VersionsOf(objectText).Select(Version);
                List<RuleObject> held;
                if (!objects.TryGetValue(node, out held))
                {
                    held = new List<RuleObject>();
                    objects.Add(node, held);
                }

                held.Add(new RuleObject(profile, standing, inForce.LostOf(objectText)));
            }

            return new StoredRules(objects);
        }

        /// <summary>
        /// The rules of a node, given as the 32 lowercase hexadecimal characters of its identifier:
        /// `NodeRule.None` where the reading holds nothing for it.
        /// </summary>
        public NodeRule At(string node)
        {
            NodeRule rule;
            return node != null && _nodes.TryGetValue(node, out rule) ? rule : NodeRule.None;
        }

        /// <summary>Every profile that has a row at some node of the reading, standing or lost, ascending by ordinal.</summary>
        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>
        /// The nodes at which a standing version of the profile's row grants, an empty rule among
        /// them, each once as 32 lowercase hexadecimal characters: a row whose standing versions
        /// all take the rule off, and a row only lost, are left out.
        /// </summary>
        public IReadOnlyCollection<string> Named(string profile)
        {
            return _nodes
                .Where(node => node.Value.Row(profile).Versions.Any(version => version.Kind == RowVersionKind.Grants))
                .Select(node => node.Key)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Whether a standing version at the node, given as 32 lowercase hexadecimal characters,
        /// lets a profile through: a version of a row that takes the rule off, `off:deny` among
        /// them, or a version of the switch that lets the walk go on. The nodes are worked out
        /// where this is first asked, which only a write above and the tab of a node's dialog do.
        /// </summary>
        public bool LetsThrough(string node)
        {
            return node != null && Lets().Contains(node);
        }

        /// <summary>
        /// Every node, as 32 lowercase hexadecimal characters, at which a standing version lets a
        /// profile through, as <see cref="LetsThrough"/> asks: what the tree a write above is
        /// carried down is walked for, `DatabaseNodes.Tree`.
        /// </summary>
        public IReadOnlyCollection<string> LettingThrough
        {
            get { return Lets(); }
        }

        /// <summary>The nodes that let a profile through, worked out where they are first asked for.</summary>
        private HashSet<string> Lets()
        {
            if (_lettingThrough == null)
            {
                _lettingThrough = new HashSet<string>(
                    _nodes.Where(held => Letting(held.Value)).Select(held => held.Key), StringComparer.Ordinal);
            }

            return _lettingThrough;
        }

        /// <summary>
        /// The identifiers of every standing version at each node below <paramref name="node"/> in
        /// the tree that holds a version letting a profile through: what the KPG tab keeps with
        /// each press for the write above, `STORE.md` §617, so that an OK renews only versions its
        /// tab's reading held.
        /// </summary>
        /// <remarks>
        /// **Every version at such a node**, the versions that grant and those of other profiles
        /// among them, and not only the versions that let through: a version of a profile's row
        /// that stands at the node at the OK and is not among these reached the database after
        /// the tab read it, and leaves every version of that node for that profile as it is, and
        /// such a version of the switch leaves them for every profile.
        /// </remarks>
        public IReadOnlyCollection<string> Below(string node, IRuledTree tree)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            if (node == null || tree == null)
            {
                return kept;
            }

            foreach (var rule in tree.Below(node).Where(LetsThrough).Select(below => _nodes[below]))
            {
                kept.UnionWith(rule.Switch.Versions.Select(version => version.Id));
                kept.UnionWith(rule.Profiles.SelectMany(profile => rule.Row(profile).Versions).Select(version => version.Id));
            }

            return kept;
        }

        /// <summary>
        /// The rules of a node with these versions laid over the versions the reading holds there:
        /// each version given stands, and every version of its own object that it names, standing
        /// or lost, stands no more and is lost no more, `STORE.md` §617. What "after" an act is.
        /// </summary>
        /// <param name="laid">Versions of objects of this node; a version of another node is passed over.</param>
        public NodeRule At(string node, IEnumerable<StoreRecord> laid)
        {
            var given = (laid ?? Enumerable.Empty<StoreRecord>())
                .Select(record => new
                {
                    Record = record,
                    Object = record.Member(StoreKinds.ObjectMember),
                })
                .Where(version =>
                {
                    string at;
                    string profile;
                    return RuleText.ReadObject(version.Object, out at, out profile) && string.Equals(at, node, StringComparison.Ordinal);
                })
                .ToList();
            if (given.Count == 0)
            {
                return At(node);
            }

            List<RuleObject> held;
            var objects = node != null && _objects.TryGetValue(node, out held) ? held : new List<RuleObject>();
            var laidOver = new List<RuleObject>();
            foreach (var objectText in given.Select(version => version.Object).Distinct(StringComparer.Ordinal))
            {
                string at;
                string profile;
                RuleText.ReadObject(objectText, out at, out profile);
                var versions = given.Where(version => version.Object == objectText)
                    .Select(version => RuleVersion.Stored(
                        version.Record.Hex, version.Record.Member(StoreKinds.Doc), version.Record.Member(StoreKinds.After)))
                    .ToList();
                var named = new HashSet<string>(versions.SelectMany(version => version.After), StringComparer.Ordinal);
                var same = objects.Where(item => string.Equals(item.Profile, profile, StringComparison.Ordinal)).ToList();
                laidOver.Add(new RuleObject(
                    profile,
                    same.SelectMany(item => item.Standing).Where(version => !named.Contains(version.Id)).Concat(versions),
                    same.SelectMany(item => item.Lost).Where(id => !named.Contains(id))));
            }

            var touched = new HashSet<string>(laidOver.Select(item => item.Profile ?? string.Empty), StringComparer.Ordinal);
            return new NodeRule(objects.Where(item => !touched.Contains(item.Profile ?? string.Empty)).Concat(laidOver));
        }

        /// <summary>Whether a standing version of the node's rules lets some profile through.</summary>
        private static bool Letting(NodeRule rule)
        {
            return rule.Switch.Versions.Any(version => version.Kind == SwitchVersionKind.Inherits)
                   || rule.Profiles.Any(profile => rule.Row(profile).Versions.Any(version =>
                       version.Kind == RowVersionKind.TakenOff || version.Kind == RowVersionKind.TakenOffRefusing));
        }

        /// <summary>
        /// A standing version as the reading holds it: its identifier, its `doc`, and the text of
        /// its `after`, read only where the acts or the editor ask what it names.
        /// </summary>
        private static RuleVersion Version(StoreNaming naming)
        {
            string doc = null;
            if (naming.Members != null)
            {
                naming.Members.TryGetValue(StoreKinds.Doc, out doc);
            }

            return RuleVersion.Stored(naming.Id, doc, naming.After);
        }
    }
}
