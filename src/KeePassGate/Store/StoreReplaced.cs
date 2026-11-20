using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// that kept a version; each object is placed under the key once.
    /// </remarks>
    internal sealed class StoreReplaced
    {
        private static readonly StoreKind Row = StoreKinds.Find(StoreKinds.Version);

        private readonly List<StoreNaming> _namers = new List<StoreNaming>();
        private readonly Dictionary<string, List<string>> _names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> _carried = new HashSet<string>(StringComparer.Ordinal);

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
        /// <param name="versions">Every version present, by its identifier.</param>
        /// <param name="others">The identifiers of the records present of another kind.</param>
        public static StoreReplaced Settle(
            IReadOnlyDictionary<string, StoreNaming> versions, ISet<string> others, IReadOnlyList<StoreRecord> added)
        {
            var made = new StoreReplaced();
            var own = added.Where(record => record.Kind == StoreKinds.Version)
                .Select(record => new StoreNaming(
                    record.Hex, record.Member(StoreKinds.ObjectMember), record.Member(StoreKinds.After), false, false, null))
                .ToList();
            var ownObjects = own.ToDictionary(version => version.Id, version => version.Object, StringComparer.Ordinal);
            made._carried.UnionWith(versions.Keys);
            made._carried.UnionWith(ownObjects.Keys);
            made._carried.UnionWith(others);

            foreach (var version in own.Concat(versions.Values))
            {
                made._names[version.Id] = Held(version, versions, ownObjects, others);
            }

            if (own.Any(version => made._names[version.Id].Any(ownObjects.ContainsKey)))
            {
                made.NamesItsOwn = true;
                return made;
            }

            var inForce = versions.Values.Where(version => version.InForce).ToList();
            var candidates = new HashSet<string>(
                own.Concat(inForce).SelectMany(version => made._names[version.Id]).Where(versions.ContainsKey), StringComparer.Ordinal);

            made._namers.AddRange(own);
            made._namers.AddRange(inForce.Where(version => !candidates.Contains(version.Id)));
            var staying = new HashSet<string>(made._namers.SelectMany(version => made._names[version.Id]), StringComparer.Ordinal);

            var kept = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var failing = candidates
                    .Where(id => !kept.Contains(id) && versions[id].InForce && !made._names[id].All(staying.Contains))
                    .ToList();
                if (failing.Count == 0)
                {
                    break;
                }

                foreach (var id in failing)
                {
                    kept.Add(id);
                    made._namers.Add(versions[id]);
                    staying.UnionWith(made._names[id]);
                }
            }

            made.Deleted = candidates.Where(id => !kept.Contains(id) && versions[id].Record).ToList();
            made.Restated = inForce
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
            byte[] key, StoreInForce inForce, IReadOnlyList<StoreRecord> added, ISet<string> present, ISet<string> held)
        {
            var made = Settle(
                inForce == null ? new Dictionary<string, StoreNaming>(StringComparer.Ordinal) : inForce.Versions,
                inForce == null ? new HashSet<string>(StringComparer.Ordinal) : inForce.Others,
                added);
            if (made.NamesItsOwn)
            {
                return made;
            }

            var tag = Hex.Of(StoreFormat.Tag(key, StoreKinds.Version));
            var places = new Dictionary<string, string>(StringComparer.Ordinal);
            var told = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var namer in made._namers)
            {
                foreach (var identifier in made._names[namer.Id]
                             .Where(identifier => !made._carried.Contains(identifier) && !present.Contains(identifier) && !held.Contains(identifier)))
                {
                    string place;
                    if (!places.TryGetValue(namer.Object, out place))
                    {
                        string tagged;
                        var subject = StoreFormat.SubjectOf(Row, namer.Object, out tagged);
                        var number = tagged == null ? 0 : StoreFormat.ProfileTag(key, tagged);
                        place = tag + Hex.Of(StoreFormat.Subject(key, subject)) + number.ToString("x4", CultureInfo.InvariantCulture);
                        places[namer.Object] = place;
                    }

                    if (identifier.StartsWith(place, StringComparison.Ordinal))
                    {
                        told.Add(identifier);
                    }
                }
            }

            made.Told = told.ToList();
            return made;
        }

        /// <summary>
        /// The names of a version held named: every name a version of its object carries, and every
        /// name nothing carries.
        /// </summary>
        private static List<string> Held(
            StoreNaming version,
            IReadOnlyDictionary<string, StoreNaming> versions,
            IReadOnlyDictionary<string, string> ownObjects,
            ISet<string> others)
        {
            var names = new List<string>();
            foreach (var identifier in StoreFormat.Identifiers(version.After) ?? Array.Empty<string>())
            {
                StoreNaming carried;
                string ownObject;
                var held = versions.TryGetValue(identifier, out carried)
                    ? string.Equals(carried.Object, version.Object, StringComparison.Ordinal)
                    : ownObjects.TryGetValue(identifier, out ownObject)
                        ? string.Equals(ownObject, version.Object, StringComparison.Ordinal)
                        : !others.Contains(identifier);
                if (held)
                {
                    names.Add(identifier);
                }
            }

            return names;
        }
    }
}
