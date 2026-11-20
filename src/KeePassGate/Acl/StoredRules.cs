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
            new StoredRules(new Dictionary<string, NodeRule>(StringComparer.Ordinal));

        private readonly IReadOnlyDictionary<string, NodeRule> _nodes;

        private StoredRules(IReadOnlyDictionary<string, NodeRule> nodes)
        {
            _nodes = nodes;
            Profiles = nodes.Values.SelectMany(rule => rule.Profiles)
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

            return new StoredRules(objects.ToDictionary(
                pair => pair.Key, pair => new NodeRule(pair.Value), StringComparer.Ordinal));
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
