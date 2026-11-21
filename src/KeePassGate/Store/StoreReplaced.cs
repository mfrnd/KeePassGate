using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// What a write does with the versions it finds replaced, `STORE.md` §617 and §627 step 3:
    /// the records of kind `version` it deletes, the versions its summary entry restates, and the
    /// identifiers no entry carries that it tells of with a deletion record.
    /// </summary>
    /// <remarks>
    /// **A version replaces only versions of its own object.** A name in `after` is of the naming
    /// version's object where a version present or a version of the write carries it with that
    /// object; a name that a version of another object or a record of another kind carries is
    /// passed over. A name nothing carries may be of any object, so it is held named as a name of
    /// the object is, and a deletion record is written for it only where its first ten bytes, the
    /// tag of `version`, the subject hash and the number, are those of the naming version's object
    /// under the key, `STORE.md` §609. Those ten bytes cannot tell apart the rows of two names
    /// whose tags meet under the key, nor a rule's object with no `/` from a row at its node whose
    /// name's tag is 0: a name of either is told of for the other. Every other pair of objects
    /// they tell apart, as far as a hash of five bytes and a tag of two can.
    ///
    /// **A deletion changes no answer.** A version is in force where a record of kind `version` in
    /// force or an item of a summary entry in force carries it, and it names what its record
    /// holds, or what its item holds where no record carries it. A version in force that a version
    /// in force or a version of the write names stays in force after the write only where a name
    /// of its own is named by no version that stays; every version in force that nothing names
    /// stays, and so does every version of the write. A version that does not stay has its record
    /// deleted, with a deletion record, and is restated no more; a version that stays keeps its
    /// record in force, or is restated where no record in force carries it, and goes on naming. A
    /// version present out of force names nothing, and its record is deleted where a version names
    /// it. So no deletion record is left that no version in force names, and no record is left in
    /// force that only a version the summary entry leaves out named.
    ///
    /// **What stays is settled all at once**: each round keeps every version that fails against
    /// what stays so far, so the outcome is the same whatever order the entries are walked in, and
    /// two copies holding the same entries settle alike.
    ///
    /// **A version of the write never names a version of the same write**, `STORE.md` §610: a
    /// version names what its writer had read, and written, the version named would stand beside
    /// its own deletion record. Such a write is refused.
    ///
    /// **Linear in the versions and their names** for each round, and a round follows only a round
    /// that kept a version; each object is placed under the key once. **The versions in force that
    /// a version in force names are the reading's**, `StoreInForce.ReplacedVersions`, worked out
    /// once from the lists read with their forms, so a write that adds no version, which is what
    /// the room counts at every hold, reads no name of any list where no version in force names a
    /// version present, which is a store as the plugin's own writes leave it.
    /// </remarks>
    internal sealed class StoreReplaced
    {
        private static readonly StoreKind Row = StoreKinds.Find(StoreKinds.Version);
        private static readonly IReadOnlyDictionary<string, StoreNaming> NoVersions = new Dictionary<string, StoreNaming>(StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<StoreId, StoreNaming> NoVersionsById = new Dictionary<StoreId, StoreNaming>();
        private static readonly ISet<StoreId> NoIdentifiers = new HashSet<StoreId>();
        private static readonly ISet<string> NoneReplaced = new HashSet<string>(StringComparer.Ordinal);

        private readonly List<StoreNaming> _namers = new List<StoreNaming>();
        private readonly Dictionary<string, List<StoreId>> _names = new Dictionary<string, List<StoreId>>(StringComparer.Ordinal);
        private IReadOnlyDictionary<StoreId, StoreNaming> _versions = NoVersionsById;
        private IReadOnlyDictionary<StoreId, string> _ownObjects = new Dictionary<StoreId, string>();
        private ISet<StoreId> _others = NoIdentifiers;

        private StoreReplaced()
        {
            Deleted = Array.Empty<string>();
            Restated = Array.Empty<string>();
            Told = Array.Empty<string>();
        }

        /// <summary>Whether a version of the write names a version the same write adds.</summary>
        public bool NamesItsOwn { get; private set; }

        /// <summary>The records of kind `version` the write deletes, each with a deletion record.</summary>
        public IReadOnlyCollection<string> Deleted { get; private set; }

        /// <summary>
        /// The versions present the summary entry restates, a version of the write aside: every
        /// version in force that nothing names, and every version that stays and that no record in
        /// force carries.
        /// </summary>
        public IReadOnlyCollection<string> Restated { get; private set; }

        /// <summary>
        /// The identifiers a version that stays names that nothing carries, that the tree holds no
        /// entry under and that no deletion record names, each an identifier a version of the
        /// naming object could have: each told of by a deletion record of its own.
        /// </summary>
        public IReadOnlyCollection<string> Told { get; private set; }

        /// <summary>
        /// What a write adding these records does with the versions present, settled as above; with
        /// no version among them, what a write that adds no version does, which the room counts.
        /// </summary>
        /// <param name="inForce">What is in force in the store, or null for a write that starts a store.</param>
        public static StoreReplaced Settle(StoreInForce inForce, IReadOnlyList<StoreRecord> added)
        {
            var made = new StoreReplaced();
            var versions = inForce == null ? NoVersions : inForce.Versions;
            made._versions = inForce == null ? NoVersionsById : inForce.VersionsById;
            made._others = inForce == null ? NoIdentifiers : inForce.OthersById;
            var own = added.Where(record => record.Kind == StoreKinds.Version)
                .Select(record => new StoreNaming(
                    record.Hex, record.Member(StoreKinds.ObjectMember), record.Member(StoreKinds.After), null, false, false, null))
                .ToList();
            var ownObjects = own.Where(version => version.Identified).ToDictionary(version => version.Identifier, version => version.Object);
            made._ownObjects = ownObjects;

            if (own.Any(version => made.Held(version).Any(ownObjects.ContainsKey)))
            {
                made.NamesItsOwn = true;
                return made;
            }

            // The versions present that a version in force or a version of the write names of its
            // own object: those a version in force names are the reading's.
            var inForceVersions = versions.Values.Where(version => version.InForce).ToList();
            var candidates = new HashSet<string>(
                own.SelectMany(made.Held).Select(made.Present).Where(named => named != null).Select(named => named.Id), StringComparer.Ordinal);
            candidates.UnionWith(inForce == null ? NoneReplaced : inForce.ReplacedVersions);

            made._namers.AddRange(own);
            made._namers.AddRange(inForceVersions.Where(version => !candidates.Contains(version.Id)));

            var kept = new HashSet<string>(StringComparer.Ordinal);
            if (candidates.Count > 0)
            {
                var staying = new HashSet<StoreId>(made._namers.SelectMany(made.Held));
                while (true)
                {
                    var failing = candidates
                        .Where(id => !kept.Contains(id) && versions[id].InForce && !made.Held(versions[id]).All(staying.Contains))
                        .ToList();
                    if (failing.Count == 0)
                    {
                        break;
                    }

                    foreach (var id in failing)
                    {
                        kept.Add(id);
                        made._namers.Add(versions[id]);
                        staying.UnionWith(made.Held(versions[id]));
                    }
                }
            }

            made.Deleted = candidates.Where(id => !kept.Contains(id) && versions[id].Record).ToList();
            made.Restated = inForceVersions
                .Where(version => !candidates.Contains(version.Id) || (kept.Contains(version.Id) && !version.RecordInForce))
                .Select(version => version.Id)
                .ToList();
            return made;
        }

        /// <summary>
        /// What a write adding these records does with the versions of a store under its key, and
        /// which names nothing carries it tells of.
        /// </summary>
        /// <param name="inForce">What is in force in the store, or null for a write that starts a store.</param>
        /// <param name="present">The identifier of every entry of the tree, a record or not.</param>
        /// <param name="held">The identifiers the deletion records of the file name.</param>
        public static StoreReplaced Of(
            byte[] key, StoreInForce inForce, IReadOnlyList<StoreRecord> added, ISet<StoreId> present, ISet<StoreId> held)
        {
            var made = Settle(inForce, added);
            if (made.NamesItsOwn)
            {
                return made;
            }

            var tag = StoreFormat.Tag(key, StoreKinds.Version);
            var places = new Dictionary<string, StoreId>(StringComparer.Ordinal);
            var told = new SortedSet<StoreId>();
            foreach (var namer in made._namers)
            {
                foreach (var identifier in made.Held(namer)
                             .Where(identifier => !made.Carried(identifier) && !present.Contains(identifier) && !held.Contains(identifier)))
                {
                    StoreId place;
                    if (!places.TryGetValue(namer.Object, out place))
                    {
                        string tagged;
                        var subject = StoreFormat.SubjectOf(Row, namer.Object, out tagged);
                        var number = tagged == null ? 0 : StoreFormat.ProfileTag(key, tagged);
                        place = StoreId.PlaceOf(tag, StoreFormat.Subject(key, subject), number);
                        places[namer.Object] = place;
                    }

                    if (identifier.Place == place)
                    {
                        told.Add(identifier);
                    }
                }
            }

            made.Told = told.Select(identifier => identifier.ToString()).ToList();
            return made;
        }

        /// <summary>The version present under this identifier, or null.</summary>
        private StoreNaming Present(StoreId identifier)
        {
            StoreNaming named;
            return _versions.TryGetValue(identifier, out named) ? named : null;
        }

        /// <summary>Whether a version present, a version of the write or a record of another kind carries this identifier.</summary>
        private bool Carried(StoreId identifier)
        {
            return _versions.ContainsKey(identifier) || _ownObjects.ContainsKey(identifier) || _others.Contains(identifier);
        }

        /// <summary>
        /// The names of a version held named: every name a version of its object carries, and every
        /// name nothing carries; worked out once for the write.
        /// </summary>
        private List<StoreId> Held(StoreNaming version)
        {
            List<StoreId> names;
            if (_names.TryGetValue(version.Id, out names))
            {
                return names;
            }

            names = new List<StoreId>(version.Named.Count);
            foreach (var identifier in version.Named.All)
            {
                StoreNaming carried;
                string ownObject;
                var held = _versions.TryGetValue(identifier, out carried)
                    ? string.Equals(carried.Object, version.Object, StringComparison.Ordinal)
                    : _ownObjects.TryGetValue(identifier, out ownObject)
                        ? string.Equals(ownObject, version.Object, StringComparison.Ordinal)
                        : !_others.Contains(identifier);
                if (held)
                {
                    names.Add(identifier);
                }
            }

            _names[version.Id] = names;
            return names;
        }
    }
}
