using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// The store as a database holds it at one moment, read whole, `STORE.md` §620 to §624.
    /// </summary>
    /// <remarks>
    /// **A function of what the database holds and of nothing else**: its candidates, the
    /// identifiers its deletion records name, and the checked forms kept from the reading before,
    /// which are a cache of the same text and change no answer. Nothing is read of a time, a place
    /// or which group is the recycle bin.
    ///
    /// **Total.** No content of a file makes this throw: a version that does not read a single way
    /// fails, a version that is not a record is passed over or is damage, and everything else is
    /// a state.
    ///
    /// **It keeps no key.** The keys found are the checked forms' own, held by the forms that
    /// gave them; what it answers with are the tags they give and the entries that pass under
    /// them, which is what the API's guard asks, `STORE.md` §633, and the records it found, each
    /// with the form that passes, whose key is overwritten when the forms kept are let go.
    ///
    /// **Its work is bounded in the keys found**, by `StoreFormat.MaxKeys`: each version is asked
    /// about its own key while keys are found, and past the ceiling about no other, so a file that
    /// brings a key for each of its entries costs a reading no more than a version each.
    ///
    /// **A reading made afresh owns its forms, and whoever asked for it lets it go**, which
    /// overwrites every key they hold, `CA.md` §208: <see cref="Afresh"/>, what the plugin's own
    /// screens and writer read. A gate's reading does not, since the readings it keeps for a
    /// database let go of their forms themselves, and letting go of such a reading does nothing.
    /// </remarks>
    public sealed partial class StoreReading : IDisposable
    {
        private readonly ISet<int> _tags;
        private readonly ISet<string> _passing;
        private readonly Func<string, bool> _retires;
        private StoreKept _owned;

        private StoreReading(
            ISet<int> tags, ISet<string> passing, Judgment judged, IReadOnlyList<StoreCandidate> candidates, IReadOnlyList<byte[]> deletions)
        {
            _tags = tags;
            Candidates = candidates;
            Deletions = deletions;
            _passing = passing;
            State = judged.State;
            Reason = judged.Reason;
            Damage = judged.Damage;
            Passed = judged.Passed;
            ByHistory = judged.ByHistory;
            Judged = judged.Judged;
            InForce = judged.InForce;
            Answering = judged.Answering;
            _retires = judged.Retires;
        }

        /// <summary>
        /// Whether the store was judged, and the counts beside its state mean anything: past
        /// `StoreFormat.MaxKeys` it is not.
        /// </summary>
        public bool Judged { get; private set; }

        /// <summary>Whether the gate may serve the database, and if not why, `STORE.md` §656.</summary>
        public StoreState State { get; private set; }

        /// <summary>
        /// Why the store is in its state, for the log: entries named by the first eight characters
        /// of their identifiers, and nothing of any content. Null for a store that is whole.
        /// </summary>
        public string Reason { get; private set; }

        /// <summary>How many entries are damage under the store key, `STORE.md` §621.</summary>
        public int Damage { get; private set; }

        /// <summary>How many candidates are no record and no damage, passed over and shown.</summary>
        public int Passed { get; private set; }

        /// <summary>How many records count by a version from their history, `STORE.md` §622.</summary>
        public int ByHistory { get; private set; }

        /// <summary>
        /// What is in force under the store key, `STORE.md` §623, for a store that is whole or
        /// stopped: null for a database with no store key, a store under several keys, and a
        /// store of a form the plugin does not read.
        /// </summary>
        internal StoreInForce InForce { get; private set; }

        /// <summary>
        /// The authorities a word is answered under, `STORE.md` §656: each that stands under each
        /// key that stands, in force and retired by nothing under its own key, whatever the
        /// store's state; none for a database with no store key, and none past
        /// `StoreFormat.MaxKeys`, where no key is judged.
        /// </summary>
        internal IReadOnlyList<FoundRecord> Answering { get; private set; }

        /// <summary>
        /// Whether a retirement under a key that stands names this authority, by record, by an
        /// item of a summary entry or by a deletion record of its tag, whatever the store's state:
        /// never where no key is found, and never past `StoreFormat.MaxKeys`, where no key is
        /// judged, `STORE.md` §656.
        /// </summary>
        /// <param name="id">The authority's `id`, hashed against the deletion records of retirements.</param>
        internal bool Retires(string id)
        {
            return id != null && _retires(id);
        }

        /// <summary>
        /// The entries this was read from, with their versions: what a write that has not yet
        /// reached the database is read beside, `PendingRecords`.
        /// </summary>
        internal IReadOnlyList<StoreCandidate> Candidates { get; private set; }

        /// <summary>The identifiers the database's deletion records name, as this was read with them.</summary>
        internal IReadOnlyList<byte[]> Deletions { get; private set; }

        /// <summary>
        /// The identifier of every entry in the tree this was read from, in hexadecimal, records
        /// or not; the candidates' where it was read from candidates alone.
        /// </summary>
        internal ISet<string> Present
        {
            get { return _present ?? (_present = new HashSet<string>(Candidates.Select(candidate => candidate.Hex), StringComparer.Ordinal)); }
        }

        private ISet<string> _present;

        /// <summary>
        /// The room the store has for its next write, `STORE.md` §627, or null where nothing is in
        /// force: a store whole but for its room reads as stopped.
        /// </summary>
        internal StoreRoom Room
        {
            get { return InForce == null ? null : InForce.Room; }
        }

        /// <summary>
        /// Reads the store from these candidates afresh, into forms of its own that
        /// <see cref="Dispose"/> lets go of.
        /// </summary>
        /// <param name="deletions">The identifiers the database's deletion records name.</param>
        /// <param name="present">
        /// The identifier of every entry in the tree, records or not, where the reading has a tree;
        /// null where the candidates are all there is.
        /// </param>
        internal static StoreReading Afresh(
            IReadOnlyList<StoreCandidate> candidates, IReadOnlyList<byte[]> deletions, IEnumerable<byte[]> present = null)
        {
            var owned = new StoreKept();
            StoreReading reading = null;
            try
            {
                reading = Of(candidates, deletions, null, owned);
                reading._owned = owned;
                if (present != null)
                {
                    reading._present = new HashSet<string>(present.Select(Hex.Of), StringComparer.Ordinal);
                }

                return reading;
            }
            finally
            {
                // A reading that never came to own them lets go of the forms here, `Of` having
                // handed every form it made to them whatever it threw.
                if (reading == null)
                {
                    owned.Clear();
                }
            }
        }

        /// <summary>
        /// Overwrites every key the forms this reading owns hold, and drops the forms: nothing for a
        /// reading that owns none.
        /// </summary>
        public void Dispose()
        {
            var owned = _owned;
            _owned = null;
            if (owned != null)
            {
                owned.Clear();
            }
        }

        /// <summary>
        /// Reads the store from these candidates, keeping in <paramref name="next"/> the checked
        /// form of every version it met, taken from <paramref name="kept"/> where the objects are
        /// those kept.
        /// </summary>
        /// <param name="deletions">The identifiers the database's deletion records name.</param>
        internal static StoreReading Of(
            IReadOnlyList<StoreCandidate> candidates, IReadOnlyList<byte[]> deletions, StoreKept kept, StoreKept next)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            var forms = new List<StoreChecked[]>(candidates.Count);
            var keys = new List<FoundKey>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                foreach (var candidate in candidates)
                {
                    var checkedForms = new StoreChecked[candidate.Versions.Count];
                    for (var index = 0; index < checkedForms.Length; index++)
                    {
                        var version = candidate.Versions[index];
                        var form = kept == null ? null : kept.Find(candidate.Hex, version);
                        checkedForms[index] = form ?? StoreChecked.Read(version);
                    }

                    forms.Add(checkedForms);
                }

                // The keys first, `STORE.md` §621: a version of an authority or a retirement that
                // holds a key and passes under that very key, held by the entry whose identifier
                // its content gives under it. Compared by their names, which are digests of their
                // value. Each version is asked about its own key and no other, so this much is a
                // single check a version however many keys the file holds.
                for (var at = 0; at < candidates.Count; at++)
                {
                    for (var index = 0; index < forms[at].Length; index++)
                    {
                        var key = forms[at][index].StoreKey;
                        if (key == null || names.Contains(key.Name))
                        {
                            continue;
                        }

                        var form = Ready(forms[at], index, candidates[at], new[] { key });
                        if (form.Passes(key, candidates[at].Identifier))
                        {
                            names.Add(key.Name);
                            keys.Add(key);
                        }
                    }
                }

                // The tags each key keeps once they are made, so that a key costs a hold a few
                // numbers added to a set and no digest after the reading that first found it.
                var tags = new HashSet<int>();
                foreach (var key in keys)
                {
                    tags.UnionWith(key.Tags);
                }

                // Past the ceiling nothing is asked of a version under a key not its own, which
                // is what grows with the keys times the versions: the store is split, `STORE.md`
                // §649, and every candidate is the store's to the guard, which leaves out at
                // least what it would have left out under every key, `StoreFormat.MaxKeys`.
                var beyond = keys.Count > StoreFormat.MaxKeys;
                var passing = new HashSet<string>(StringComparer.Ordinal);
                for (var at = 0; at < candidates.Count; at++)
                {
                    if (beyond)
                    {
                        passing.Add(candidates[at].Hex);
                        continue;
                    }

                    for (var index = 0; index < forms[at].Length; index++)
                    {
                        var form = Ready(forms[at], index, candidates[at], keys);
                        if (Checks(form, keys, candidates[at].Identifier))
                        {
                            passing.Add(candidates[at].Hex);
                        }
                    }
                }

                var judged = beyond
                    ? Judgment.Beyond(keys.Count, candidates.Count)
                    : Judgment.Of(candidates, forms, keys, deletions ?? Array.Empty<byte[]>());
                return new StoreReading(tags, passing, judged, candidates, deletions ?? Array.Empty<byte[]>());
            }
            finally
            {
                // The keys are the forms' own, kept with them and overwritten when they are let
                // go: nothing of a key is copied here. Every form made is handed to `next`, also
                // where something here threw, so that whoever let go of `next` overwrites the
                // keys of every form it does not keep.
                for (var at = 0; at < forms.Count; at++)
                {
                    foreach (var form in forms[at])
                    {
                        form.Done();
                        if (next != null)
                        {
                            next.Keep(candidates[at].Hex, form);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether an entry under this identifier is the store's to the API: its first three bytes
        /// are the tag of a kind under a key found, or some version of it passes its check under
        /// a key found, `STORE.md` §633.
        /// </summary>
        public bool Holds(byte[] identifier)
        {
            if (identifier == null)
            {
                return false;
            }

            return _tags.Contains(StoreFormat.TagNumber(identifier)) || _passing.Contains(Hex.Of(identifier));
        }

        /// <summary>
        /// The form of a version, read again from its strings where it was kept from an earlier
        /// reading and has not been asked about every one of these keys, so that no answer is
        /// ever given without the text it is about.
        /// </summary>
        private static StoreChecked Ready(StoreChecked[] forms, int index, StoreCandidate candidate, IReadOnlyList<FoundKey> keys)
        {
            var form = forms[index];
            if (keys == null || form.Readable || form.Asked(keys))
            {
                return form;
            }

            form = StoreChecked.Read(candidate.Versions[index]);
            forms[index] = form;
            return form;
        }

        private static bool Checks(StoreChecked form, IEnumerable<FoundKey> keys, byte[] entry)
        {
            return keys.Any(key => form.Checks(key, entry));
        }
    }
}
