using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>A condition of `STORE.md` §624 that a stopped store fails, and the room of §643.</summary>
    internal enum StoreStop
    {
        /// <summary>No summary entry of the store is in force.</summary>
        NoSummaryEntry,

        /// <summary>A summary entry was deleted that nothing in force replaces.</summary>
        SummaryEntryDeleted,

        /// <summary>Entries of the store have no version that passes.</summary>
        Damage,

        /// <summary>A record is present that no summary entry restates.</summary>
        NotRestated,

        /// <summary>The store has no room for its next write, which no seal lifts, `STORE.md` §643.</summary>
        NoRoom,
    }

    /// <summary>
    /// An entry of the store with no version that passes, `STORE.md` §621: its identifier, the
    /// kind its tag names under the store key, and its title as the entry holds it.
    /// </summary>
    internal sealed class StoreDamage
    {
        private readonly StoreCandidate _entry;

        public StoreDamage(StoreCandidate entry, string kind)
        {
            _entry = entry;
            Kind = kind;
        }

        /// <summary>The entry's identifier, as lowercase hexadecimal.</summary>
        public string Hex
        {
            get { return _entry.Hex; }
        }

        /// <summary>The kind whose tag the identifier begins with under the store key.</summary>
        public string Kind { get; private set; }

        /// <summary>The entry's title as it stands, read when asked for.</summary>
        public string Title()
        {
            return _entry.Title();
        }
    }

    /// <summary>
    /// An object a lost version makes refuse, `STORE.md` §617 and §618, with what the seal's version
    /// of it names, §625.
    /// </summary>
    internal sealed class StoreLoss
    {
        public StoreLoss(string objectText, IReadOnlyList<string> lost, IReadOnlyList<string> after, IReadOnlyList<string> settles)
        {
            Object = objectText;
            Lost = lost;
            After = after;
            Settles = settles;
        }

        /// <summary>The object's text.</summary>
        public string Object { get; private set; }

        /// <summary>The identifiers of its lost versions, ascending: what the deletion records tell of.</summary>
        public IReadOnlyList<string> Lost { get; private set; }

        /// <summary>
        /// What the seal's version names in `after`, ascending: every version of the object in
        /// force, and everything those name in `after`.
        /// </summary>
        public IReadOnlyList<string> After { get; private set; }

        /// <summary>
        /// What the seal's version names in `lost`, ascending: <see cref="Lost"/> and everything the
        /// versions of the object in force name in `lost`, none of them in <see cref="After"/>.
        /// </summary>
        public IReadOnlyList<string> Settles { get; private set; }
    }

    /// <summary>
    /// A revocation, a retirement or a removal of a name that deletion records alone tell of, about
    /// a subject the file names nowhere, `STORE.md` §611: its kind, and the deletion records that
    /// tell of it, which the seal's dialog shows. What the summary entry's item with no subject is
    /// to hold of it is the reading's, `StoreInForce.Unnamed`.
    /// </summary>
    internal sealed class StoreUnnamed
    {
        public StoreUnnamed(string kind, IReadOnlyList<string> told)
        {
            Kind = kind;
            Told = told;
        }

        /// <summary>The kind of the decision: `revoked`, `retired` or `removed`.</summary>
        public string Kind { get; private set; }

        /// <summary>The identifier of every deletion record that tells of it, ascending.</summary>
        public IReadOnlyList<string> Told { get; private set; }
    }

    /// <summary>
    /// What a stopped store lost, gathered from a single reading, `STORE.md` §624 and §625: what
    /// the seal's dialog shows and what the seal is composed from.
    /// </summary>
    /// <remarks>
    /// **Every condition that holds, and not the first alone**, which the reading's own reason,
    /// naming the first, does not give: the page tells a stop the seal lifts from a store with no
    /// room, which it does not, whatever condition comes first, and the seal is composed from every
    /// loss the listing holds, of whichever condition. The dialog's first line is the reading's
    /// reason, and its rows are the losses.
    ///
    /// **What a deletion record alone states is told apart**: a revocation, a retirement, a
    /// removal and a certificate taken away in force only by a deletion record, which the union
    /// of `STORE.md` §623 step 2 keeps in force and the seal's summary entry restates, so that the
    /// dialog can show what was deleted with a record and what the seal restates; and apart from
    /// those, a revocation, a retirement and a removal whose subject the file names nowhere, which
    /// the seal's summary entry restates by an item with no subject, `STORE.md` §611.
    ///
    /// **Pure**: it reads the reading and nothing else, and hashes nothing, everything having been
    /// worked out as the reading was made.
    /// </remarks>
    internal sealed class StoreLosses
    {
        private StoreLosses()
        {
        }

        /// <summary>The reading's reason for its state, which names the first condition only.</summary>
        public string Reason { get; private set; }

        /// <summary>Every condition of `STORE.md` §624 the store fails, and its room, in that order.</summary>
        public IReadOnlyList<StoreStop> Stops { get; private set; }

        /// <summary>Every entry that is damage, by identifier.</summary>
        public IReadOnlyList<StoreDamage> Damaged { get; private set; }

        /// <summary>
        /// Every summary entry a deletion record tells of that nothing in force names, ascending,
        /// where a summary entry is in force; where none is, which is a stop of its own, those a
        /// deletion record tells of at the greatest height of any summary entry of the store,
        /// present, damaged or told of: the summary entry deleted last, where none stands above it.
        /// </summary>
        /// <remarks>
        /// **The greatest height alone where no summary entry is in force**, and not every summary
        /// entry told of, though nothing in force names any of them then: every write deletes the
        /// summary entry before it with a deletion record, so a store holds one for each write it
        /// was ever given, each named by the summary entry written after it. The dialog lists the
        /// summary entry deleted that the stop is about, none where a summary entry present or
        /// damaged stands above every summary entry told of, and the seal's summary entry names
        /// every summary entry told of as replaced either way. The measured reading lists none
        /// there.
        /// </remarks>
        public IReadOnlyList<string> Heads { get; private set; }

        /// <summary>Every record present that no summary entry present restates, ascending.</summary>
        public IReadOnlyList<string> NotRestated { get; private set; }

        /// <summary>The client keys revoked by a deletion record alone, ascending.</summary>
        public IReadOnlyList<string> Revoked { get; private set; }

        /// <summary>The authorities retired by a deletion record alone, by `id`, ascending.</summary>
        public IReadOnlyList<string> Retired { get; private set; }

        /// <summary>The removals of names known by a deletion record alone, each its name and identifier, by name.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Removed { get; private set; }

        /// <summary>
        /// The revocations, the retirements and the removals of names a deletion record alone
        /// tells of whose subject the file names nowhere, each with every deletion record that
        /// tells of it, by kind and then by what the seal's item holds of it.
        /// </summary>
        public IReadOnlyList<StoreUnnamed> Unnamed { get; private set; }

        /// <summary>
        /// The certificates taken away by a deletion record alone, each the deletion record's
        /// identifier and the admission key, by identifier.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Withdrawn { get; private set; }

        /// <summary>Every object a lost version makes refuse, by its text.</summary>
        public IReadOnlyList<StoreLoss> Lost { get; private set; }

        /// <summary>
        /// Every object the deletion record of a damaged version would make refuse once the seal
        /// has deleted that version with one, and that no lost version of the reading makes refuse,
        /// ascending.
        /// </summary>
        public IReadOnlyList<string> RefusesAfter { get; private set; }

        /// <summary>What a reading of a stopped store lost, or null for a store in any other state.</summary>
        public static StoreLosses Of(StoreReading reading)
        {
            if (reading == null || reading.State != StoreState.Stopped || reading.InForce == null)
            {
                return null;
            }

            var inForce = reading.InForce;
            var losses = new StoreLosses
            {
                Reason = reading.Reason,
                Damaged = reading.Damaged,
                Heads = inForce.HeadsInForce.Count > 0
                    ? inForce.LostHeads
                    : DeletedLast(
                        inForce.LostHeads,
                        inForce.Heads.Select(head => head.Hex).Concat(reading.Damaged.Where(damaged => damaged.Kind == StoreKinds.Head).Select(damaged => damaged.Hex))),
                NotRestated = inForce.NotRestated().Select(record => record.Hex).OrderBy(hex => hex, StringComparer.Ordinal).ToList(),
                Revoked = Ascending(inForce.ByDeletion(StoreKinds.Revoked)),
                Retired = Ascending(inForce.ByDeletion(StoreKinds.Retired)),
                Removed = inForce.RemovalsByDeletion
                    .OrderBy(removal => removal.Key, StringComparer.Ordinal)
                    .ThenBy(removal => removal.Value, StringComparer.Ordinal)
                    .ToList(),
                Unnamed = inForce.UnnamedByDeletion,
                Withdrawn = inForce.WithdrawnByDeletion.OrderBy(statement => statement.Key, StringComparer.Ordinal).ToList(),
                Lost = inForce.Objects.Where(objectText => inForce.LostOf(objectText).Count > 0).Select(objectText => Loss(inForce, objectText)).ToList(),
                RefusesAfter = inForce.RefusesAfter,
            };

            var stops = new List<StoreStop>();
            if (inForce.HeadsInForce.Count == 0)
            {
                stops.Add(StoreStop.NoSummaryEntry);
            }
            else if (inForce.LostHeads.Count > 0)
            {
                stops.Add(StoreStop.SummaryEntryDeleted);
            }

            if (losses.Damaged.Count > 0)
            {
                stops.Add(StoreStop.Damage);
            }

            // Asked only where a summary entry is present, as the measured reading asked it: with
            // none, a record nothing restates is no loss of its own but the summary entry's.
            if (losses.NotRestated.Count > 0 && inForce.Heads.Count > 0)
            {
                stops.Add(StoreStop.NotRestated);
            }

            if (!inForce.Room.HasRoom)
            {
                stops.Add(StoreStop.NoRoom);
            }

            losses.Stops = stops;
            return losses;
        }

        /// <summary>
        /// A lost object, with what the seal's version of it names: in `after` every version of the
        /// object in force and what those name there, and in `lost` what was lost and what those
        /// name there, but what `after` names, `STORE.md` §625.
        /// </summary>
        private static StoreLoss Loss(StoreInForce inForce, string objectText)
        {
            var inForceOf = inForce.Versions.Values
                .Where(version => version.InForce && string.Equals(version.Object, objectText, StringComparison.Ordinal))
                .ToList();
            var after = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var version in inForceOf)
            {
                after.Add(version.Id);
                after.UnionWith(Listed(version.After));
            }

            var settles = new SortedSet<string>(inForce.LostOf(objectText), StringComparer.Ordinal);
            foreach (var version in inForceOf)
            {
                settles.UnionWith(Listed(version.Lost));
            }

            settles.ExceptWith(after);
            return new StoreLoss(objectText, inForce.LostOf(objectText), after.ToList(), settles.ToList());
        }

        /// <summary>
        /// The summary entries told of at the greatest height of these and of the summary entries
        /// present, ascending: a height is bytes 9 and 10 of an identifier.
        /// </summary>
        private static IReadOnlyList<string> DeletedLast(IReadOnlyList<string> told, IEnumerable<string> present)
        {
            var greatest = told.Concat(present).Select(HeightOf).DefaultIfEmpty(0).Max();
            return Ascending(told.Where(head => HeightOf(head) == greatest));
        }

        private static int HeightOf(string hex)
        {
            StoreId identifier;
            return StoreId.TryParse(hex, out identifier) ? identifier.Number : 0;
        }

        private static IReadOnlyList<string> Ascending(IEnumerable<string> values)
        {
            return values.OrderBy(value => value, StringComparer.Ordinal).ToList();
        }

        /// <summary>The identifiers a list holds, none for a list that is empty or absent.</summary>
        private static IEnumerable<string> Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? Enumerable.Empty<string>() : list.Split(' ').Where(id => id.Length > 0);
        }
    }
}
