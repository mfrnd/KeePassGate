using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The KPG tab's buffer for the plugin's store: the acts pressed on the dialog, held until its
    /// OK, `STORE.md` §627.
    /// </summary>
    /// <remarks>
    /// **Each press composes its act at the press**, from the store as the page showed it, and
    /// the act is held here. **An act on a client, on a certificate or about a profile name holds
    /// what is to be done**: its records are composed again wherever the buffer is read and at OK,
    /// against the store as it stands then with the acts pressed before it planned over it in a
    /// single write, `StoreAct.Again`; an act held while the store holds no key stays as pressed.
    /// A renewal that lands under the dialog is named by a certificate taken away pressed before
    /// it, an addition names the removals the page showed at its press, and each press sees the
    /// presses before it. **The pages read
    /// the store with the acts beside it**, composed so, planned together in a single write and
    /// applied in memory over the live entries, as OK writes them, so a page shows what OK would
    /// write now: a single summary entry more however many acts are held, and the room that
    /// write leaves.
    ///
    /// **What the acts compose to is kept** until the store or the acts held change: each act is
    /// composed once, over the live store with the acts composed before it planned together, and
    /// a press composes its own act alone. A read of the buffer reads the live store to tell
    /// whether it changed, and composes and plans nothing it kept.
    ///
    /// **At OK the acts are written in the order they were pressed**, through the live store and
    /// so through the writer, each held to the store as it stands then: an act whose ground went
    /// meanwhile is left out and named, and a certificate taken away by then is not taken away
    /// again. **Cancel drops every act**, and a store key made on the dialog goes with the act
    /// that held it.
    ///
    /// **The buffer holds what the store would be written with.** A press that retires an
    /// authority an act held made takes that act out with it, and what is left of the two takes
    /// the made act's place: Create then Delete leaves nothing, and Recreate then Delete leaves the
    /// Delete of the authority the database holds, its retirements holding the store key as the
    /// Delete's held it. A later press is then composed against what OK would write. The plan's
    /// own dropping of an authority made and retired in a single write stays as the second line.
    /// **And it holds the operator's last press for each name**, and no earlier press: written as
    /// records, a name added and removed again would leave an item in every later summary entry
    /// for good. **So for Allow renewal, the last press for each client**, naming every version
    /// each press about the client was shown, `RenewalActs.Folded`: a press made again because a
    /// refusal arrived under the dialog would otherwise leave standing the version the first
    /// press lifted.
    ///
    /// **A revocation has no ground**, so OK always writes it; the pages read the client's
    /// admissions as gone while it waits, since the plan deletes what it refuses, `STORE.md` §627.
    /// </remarks>
    public sealed class PendingRecords : IGateStore
    {
        private readonly Func<IGateStore> _live;
        private readonly List<StoreAct> _acts = new List<StoreAct>();

        // What the acts held composed to over the live store as last read, or null.
        private Composition _kept;

        /// <summary>A buffer over the database's own store, holding no act yet.</summary>
        /// <param name="live">
        /// The database's own store, asked for on every access, as a database locked and opened
        /// again comes back with another tree behind the same handle.
        /// </param>
        public PendingRecords(Func<IGateStore> live)
        {
            if (live == null)
            {
                throw new ArgumentNullException(nameof(live));
            }

            _live = live;
        }

        /// <summary>The acts waiting for the dialog's OK, in the order they were pressed.</summary>
        public IReadOnlyList<StoreAct> Acts
        {
            get { return _acts.ToList(); }
        }

        /// <summary>Whether an act is waiting.</summary>
        public bool Waiting
        {
            get { return _acts.Count > 0; }
        }

        /// <summary>
        /// The store as OK would leave it now: the live store with the acts, composed against it,
        /// written over it in memory, or the live store as it is where they leave nothing or no
        /// longer apply.
        /// </summary>
        public StoreReading Read()
        {
            if (_acts.Count == 0)
            {
                return _live().Read();
            }

            using (var live = _live().Read())
            {
                var after = Kept(live).After;
                if (after != null)
                {
                    // A reading of its own, which the caller lets go: what is kept stays.
                    return StoreReading.Afresh(after.Candidates, after.Deletions);
                }
            }

            // Nothing the acts leave: the store as it is, read for the caller.
            return _live().Read();
        }

        /// <summary>
        /// The acts held, composed against the store as it stands now for the write at OK, in
        /// the order pressed, with null in the place of an act with nothing left to write: see
        /// <see cref="Composition"/>. What is composed here is let go by <see cref="Discard"/>,
        /// with the acts held.
        /// </summary>
        public IReadOnlyList<StoreAct> Composed()
        {
            using (var live = _live().Read())
            {
                return Kept(live).Composed.ToList();
            }
        }

        /// <summary>
        /// What the acts held compose to over this reading of the live store: what was kept where
        /// neither the store nor the acts held changed since, and otherwise composed now, over
        /// what was kept where the acts held begin with the acts it was composed for, and kept in
        /// its place.
        /// </summary>
        private Composition Kept(StoreReading live)
        {
            if (_kept != null && _kept.For(live, _acts))
            {
                return _kept;
            }

            var composed = Composition.Of(live, _acts, _kept);
            Keep(composed);
            return composed;
        }

        /// <summary>
        /// Keeps this composition in place of the composition kept, which is let go but for what
        /// the two share; null keeps none.
        /// </summary>
        private void Keep(Composition composition)
        {
            var before = _kept;
            _kept = composition;
            if (before != null && !ReferenceEquals(before, composition))
            {
                before.Release(composition, _acts);
            }
        }

        /// <summary>
        /// Holds these acts for the dialog's OK, where OK would write them: refused, with nothing
        /// held, where nothing is written into the store in this session, where the acts held with
        /// these would be refused, and where any of these would be left out.
        /// </summary>
        /// <remarks>
        /// **The whole buffer is planned, against the store as it stands**, as OK plans it. The
        /// page reads the store as it is where the acts held no longer apply, and an act planned
        /// against that alone was held although OK would refuse every act with it.
        ///
        /// **A press retiring an authority made on this dialog is folded in** before the plan, as
        /// the class remarks have it, and the acts folded are let go once it is held. **So is a
        /// press about a name**: it takes every act held about that name out of the buffer, and is
        /// held itself only where it still has something to write against the store with the acts
        /// left, so a name added and removed on a single dialog, or removed and added again,
        /// writes nothing, `STORE.md` §627. Such a press is taken, holding nothing.
        /// </remarks>
        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            if (acts == null || acts.Count == 0)
            {
                return StoreWritten.Refused("there is nothing to write");
            }

            var unwritable = Unwritable;
            if (unwritable != null)
            {
                return StoreWritten.Refused(unwritable);
            }

            var withheld = WithheldUnderStart(_acts, acts);
            if (withheld != null)
            {
                return StoreWritten.Refused(withheld);
            }

            var next = _acts.ToList();
            var letGo = new List<StoreAct>();
            var standing = new List<StoreAct>();
            Composition composition = null;
            var held = false;
            try
            {
                // In the order pressed, and at once: each fold changes `next` for the press after it.
                standing.AddRange(acts.Select(act => Fold(next, act, letGo)).Where(standIn => standIn != null));

                IReadOnlyList<string> leftOut = Array.Empty<string>();
                if (next.Count > 0)
                {
                    using (var live = _live().Read())
                    {
                        composition = Composition.Of(live, next, _kept);

                        // A press about a name with nothing left to write once the presses
                        // before it about that name are folded out is held as nothing, and the
                        // buffer is composed again without it.
                        var spent = standing
                            .Where(act => ProfileActs.About(act) != null && composition.Composed[next.IndexOf(act)] == null)
                            .ToList();
                        if (spent.Count > 0)
                        {
                            composition.Release(_kept, next.Concat(_acts).ToList());
                            composition = null;
                            foreach (var act in spent)
                            {
                                next.Remove(act);
                                standing.Remove(act);
                                letGo.Add(act);
                            }

                            if (next.Count == 0)
                            {
                                _acts.Clear();
                                held = true;
                                Keep(null);
                                return StoreWritten.Waiting(standing, leftOut);
                            }

                            composition = Composition.Of(live, next, _kept);
                        }

                        var written = composition.Composed;

                        // A press with nothing left to write is refused as an act the plan
                        // leaves out is, before a plan that may hold nothing at all.
                        if (standing.Any(act => written[next.IndexOf(act)] == null))
                        {
                            return StoreWritten.Refused("the act no longer applies to the store as it stands");
                        }

                        // A press that folds to nothing changes nothing of the acts left, and is
                        // taken whatever OK makes of them: an act left out then is named there.
                        var write = StoreWrite.Plan(live, written.Where(act => act != null).ToList(), GateVersion.Current);
                        if (write.Refused != null && standing.Count > 0)
                        {
                            return StoreWritten.Refused(write.Refused, write.LeftOut);
                        }

                        if (standing.Any(act => !write.Taken.Contains(written[next.IndexOf(act)])))
                        {
                            var left = write.LeftOut.Where(line => standing.Any(act => line.StartsWith(act.Name + ": ", StringComparison.Ordinal))).ToList();
                            return StoreWritten.Refused(left.Count == 0 ? "the act no longer applies to the store as it stands" : left[0], left);
                        }

                        leftOut = write.LeftOut;
                    }
                }

                _acts.Clear();
                _acts.AddRange(next);
                held = true;
                Keep(composition);
                return StoreWritten.Waiting(standing, leftOut);
            }
            finally
            {
                if (!held && composition != null && !ReferenceEquals(composition, _kept))
                {
                    composition.Release(_kept, next.Concat(_acts).ToList());
                }

                // An act composed here and not held is let go, and so is every act folded into
                // another where the press was held; the acts pressed are their caller's otherwise.
                foreach (var act in held ? letGo : standing.Where(act => !acts.Contains(act)))
                {
                    act.Forget();
                }
            }
        }

        /// <summary>
        /// Puts an act pressed into the buffer as it would stand, answering with the act that
        /// stands for it there, or null where nothing is left of it.
        /// </summary>
        /// <remarks>
        /// Where the act retires an authority an act held made, the two leave the buffer, and an
        /// act of what is left of both, under the made act's key and ground, takes the made act's
        /// place: the made act's records less that authority, and the pressed act's less its
        /// retirement, every retirement left holding the store key where the retirement dropped
        /// held it. It is named as the act pressed, but where the made act brings a new key, as
        /// a Create on a database with no key and a store started again do: what is left then
        /// brings it too, and is named as the made act. **A store started again stays a start**:
        /// a Recreate pressed after it leaves the start with the authority the Recreate made,
        /// `STORE.md` §632, and a Delete is withheld before it is folded,
        /// <see cref="WithheldUnderStart"/>.
        /// </remarks>
        /// <param name="letGo">Where the acts taken out are put, for the caller to let go once the press is held.</param>
        private static StoreAct Fold(List<StoreAct> buffer, StoreAct act, ICollection<StoreAct> letGo)
        {
            // The operator's last press about a name, and no earlier press about it.
            var profile = ProfileActs.About(act);
            if (profile != null)
            {
                foreach (var earlier in buffer.Where(held => string.Equals(ProfileActs.About(held), profile, StringComparison.Ordinal)).ToList())
                {
                    buffer.Remove(earlier);
                    letGo.Add(earlier);
                }

                buffer.Add(act);
                return act;
            }

            // The operator's last press of Allow renewal for a client, naming what every press
            // about that client was shown.
            var client = RenewalActs.About(act);
            if (client != null)
            {
                var folded = act;
                foreach (var earlier in buffer.Where(held => string.Equals(RenewalActs.About(held), client, StringComparison.Ordinal)).ToList())
                {
                    buffer.Remove(earlier);
                    letGo.Add(earlier);
                    folded = RenewalActs.Folded(earlier, folded);
                }

                if (!ReferenceEquals(folded, act))
                {
                    letGo.Add(act);
                }

                buffer.Add(folded);
                return folded;
            }

            var retired = new HashSet<string>(
                act.Records.Where(record => record.Kind == StoreKinds.Retired).Select(record => record.Member(StoreKinds.AuthorityMember)),
                StringComparer.Ordinal);
            var maker = buffer.FirstOrDefault(held => held.Records.Any(record => Makes(record, retired)));
            if (maker == null)
            {
                buffer.Add(act);
                return act;
            }

            var made = new HashSet<string>(
                maker.Records.Where(record => Makes(record, retired)).Select(record => record.Member(StoreKinds.Id)), StringComparer.Ordinal);
            var unmaking = act.Records.Where(record => Unmakes(record, made)).ToList();
            var left = maker.Records.Where(record => !Makes(record, made))
                .Concat(act.Records.Where(record => !Unmakes(record, made)))
                .ToList();

            var at = buffer.IndexOf(maker);
            buffer.RemoveAt(at);
            letGo.Add(maker);
            letGo.Add(act);
            if (left.Count == 0)
            {
                return null;
            }

            var key = maker.Key();
            try
            {
                if (unmaking.Any(record => record.Protected))
                {
                    left = left.Select(record => record.Kind == StoreKinds.Retired && !record.Protected ? StoreWrite.Holding(key, record) : record).ToList();
                }

                var name = maker.Against == null ? maker.Name : act.Name;
                var folded = new StoreAct(name, key, maker.Against, left, maker.Why, starts: maker.Starts, admitted: maker.Admitted);
                buffer.Insert(at, folded);
                return folded;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// Why a press is withheld while a store started again waits, or null where it is not:
        /// a press that retires the authority the start made and makes none in its place, as
        /// Delete does, `STORE.md` §632.
        /// </summary>
        /// <remarks>
        /// **The start's new key is held by the authority it makes**, and nothing else the start
        /// writes holds it: a start whose authority a Delete took away would set every earlier
        /// store aside under a key no entry holds, and serve nothing in any copy. A start that
        /// holds its key some other way was never measured, so the press is withheld with what
        /// takes the start back, and Recreate, which makes an authority in the place of the
        /// start's, folds into the start.
        /// </remarks>
        private static string WithheldUnderStart(IEnumerable<StoreAct> held, IEnumerable<StoreAct> pressed)
        {
            var starting = new HashSet<string>(
                held.Where(act => act.Starts)
                    .SelectMany(act => act.Records)
                    .Where(record => record.Kind == StoreKinds.Authority)
                    .Select(record => record.Member(StoreKinds.Id)),
                StringComparer.Ordinal);
            var unmaking = pressed.FirstOrDefault(act =>
                act.Records.Any(record => record.Kind == StoreKinds.Retired && starting.Contains(record.Member(StoreKinds.AuthorityMember)))
                && !act.Records.Any(record => record.Kind == StoreKinds.Authority));
            return unmaking == null
                ? null
                : "a store started again waits for this dialog's OK with the authority it makes. Cancel takes the start back";
        }

        /// <summary>Whether a record is an authority among these.</summary>
        private static bool Makes(StoreRecord record, ISet<string> ids)
        {
            return record.Kind == StoreKinds.Authority && ids.Contains(record.Member(StoreKinds.Id));
        }

        /// <summary>Whether a record is a retirement of an authority among these.</summary>
        private static bool Unmakes(StoreRecord record, ISet<string> ids)
        {
            return record.Kind == StoreKinds.Retired && ids.Contains(record.Member(StoreKinds.AuthorityMember));
        }

        /// <summary>Why the live store takes no write of the operator's in this session, or null.</summary>
        public string Unwritable
        {
            get { return _live().Unwritable; }
        }

        /// <summary>
        /// Drops every act, which is what Cancel means, and overwrites the keys they held and the
        /// keys of the acts composed from them.
        /// </summary>
        public void Discard()
        {
            var acts = _acts.ToList();
            _acts.Clear();
            foreach (var act in acts)
            {
                act.Forget();
            }

            Keep(null);
        }

        /// <summary>
        /// Acts composed in the order pressed over a reading of the live store, each act that holds
        /// what is to be done composed again over that reading with the acts composed before it
        /// planned together in a single write, `StoreAct.Again`, and every other act, or an act
        /// over a reading that holds no store key, as it was pressed, with null in the place of an
        /// act with nothing left to write; and the reading they all leave, planned together the
        /// same way, as OK writes them.
        /// </summary>
        private sealed class Composition
        {
            private readonly IReadOnlyList<StoreCandidate> _candidates;
            private readonly IReadOnlyList<byte[]> _deletions;
            private readonly List<StoreAct> _acts;

            private Composition(
                StoreReading live, List<StoreAct> acts, List<StoreAct> composed, StoreReading after)
            {
                _candidates = live.Candidates;
                _deletions = live.Deletions;
                _acts = acts;
                Composed = composed;
                After = after;
            }

            /// <summary>
            /// An act composed for each act held, in its place, or null where nothing is left to
            /// write.
            /// </summary>
            public List<StoreAct> Composed { get; private set; }

            /// <summary>
            /// The reading the acts leave, or null where they leave the store as it is.
            /// </summary>
            public StoreReading After { get; private set; }

            /// <summary>
            /// Whether this was composed for these acts held, over a reading of these very entries
            /// and deletion records.
            /// </summary>
            public bool For(StoreReading live, IReadOnlyList<StoreAct> acts)
            {
                return _acts.Count == acts.Count && Begins(acts) && Over(live);
            }

            /// <summary>
            /// The acts composed over this reading: those this prior composition was composed for
            /// taken from it, where it was composed over the same entries and these acts begin
            /// with its own, and each act after them composed over this reading with the acts
            /// composed before it planned together, as the reading they all leave is.
            /// </summary>
            public static Composition Of(StoreReading live, IReadOnlyList<StoreAct> acts, Composition prior)
            {
                var reused = prior != null && prior._acts.Count <= acts.Count && prior.Begins(acts) && prior.Over(live);
                var composed = reused ? new List<StoreAct>(prior.Composed) : new List<StoreAct>();
                var done = false;
                using (var left = new Left(reused ? prior.After : null))
                {
                    try
                    {
                        // Whether an act was composed since the reading held was planned: that
                        // reading is planned again, with every act composed so far, before an act
                        // is composed over it and once the last act is composed.
                        var since = false;
                        foreach (var act in acts.Skip(composed.Count))
                        {
                            var made = act;
                            if (act.Again != null)
                            {
                                if (since)
                                {
                                    left.Take(Written(live, composed));
                                    since = false;
                                }

                                var over = left.Reading ?? live;
                                made = over.InForce == null ? act : act.Again(over);
                            }

                            composed.Add(made);
                            since |= made != null;
                        }

                        if (since)
                        {
                            left.Take(Written(live, composed));
                        }

                        done = true;
                        return new Composition(live, acts.ToList(), composed, left.Keep());
                    }
                    finally
                    {
                        if (!done)
                        {
                            foreach (var made in composed.Skip(reused ? prior.Composed.Count : 0).Where(made => made != null && !acts.Contains(made)))
                            {
                                made.Forget();
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Lets go of what this holds that the other composition does not, and of no act held:
            /// the acts composed here, and the reading the acts leave.
            /// </summary>
            public void Release(Composition other, IReadOnlyCollection<StoreAct> held)
            {
                foreach (var made in Composed.Where(made => made != null
                             && !held.Contains(made)
                             && (other == null || !other.Composed.Contains(made))))
                {
                    made.Forget();
                }

                if (After != null && (other == null || !ReferenceEquals(After, other.After)))
                {
                    After.Dispose();
                }
            }

            /// <summary>
            /// Whether these acts begin with the acts this was composed for, the very same.
            /// </summary>
            private bool Begins(IReadOnlyList<StoreAct> acts)
            {
                for (var at = 0; at < _acts.Count; at++)
                {
                    if (at >= acts.Count || !ReferenceEquals(_acts[at], acts[at]))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Whether this was composed over a reading of these very entries, version for version,
            /// and these deletion records: the same objects are the same text, `StoreVersion.Same`.
            /// </summary>
            /// <remarks>
            /// **It rests on a reading's lists not changing once they are handed out**: a store
            /// builds the lists of every reading afresh, as `KdbxStore` does, and a write puts new
            /// lists in the place of the old. Lists changed in place would compare as the lists
            /// kept here, and what was composed over them would be handed out for a store it was
            /// not composed over.
            /// </remarks>
            private bool Over(StoreReading live)
            {
                var candidates = live.Candidates;
                var deletions = live.Deletions;
                if (candidates.Count != _candidates.Count || deletions.Count != _deletions.Count)
                {
                    return false;
                }

                for (var at = 0; at < candidates.Count; at++)
                {
                    var now = candidates[at];
                    var then = _candidates[at];
                    if (!string.Equals(now.Hex, then.Hex, StringComparison.Ordinal) || now.Versions.Count != then.Versions.Count)
                    {
                        return false;
                    }

                    for (var version = 0; version < now.Versions.Count; version++)
                    {
                        if (!now.Versions[version].Same(then.Versions[version]))
                        {
                            return false;
                        }
                    }
                }

                for (var at = 0; at < deletions.Count; at++)
                {
                    if (!deletions[at].SequenceEqual(_deletions[at]))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// The live reading with these acts, those not null, planned together in a single
            /// write and written over it in memory, as OK writes them, or null where that write
            /// would be refused or leave nothing.
            /// </summary>
            private static StoreReading Written(StoreReading live, IEnumerable<StoreAct> composed)
            {
                var write = StoreWrite.Plan(live, composed.Where(act => act != null).ToList(), GateVersion.Current);
                if (write.Refused != null || write.Empty)
                {
                    return null;
                }

                var applied = write.Applied(live.Candidates, live.Deletions);
                return StoreReading.Afresh(applied.Key, applied.Value);
            }

            /// <summary>
            /// The reading the acts composed so far leave: the prior composition's to begin with,
            /// which stays that composition's, and then each reading made here, let go where the
            /// next takes its place and at the end unless it is kept.
            /// </summary>
            private sealed class Left : IDisposable
            {
                private bool _made;

                public Left(StoreReading prior)
                {
                    Reading = prior;
                }

                /// <summary>The reading the acts so far leave, or null where they leave the store as it is.</summary>
                public StoreReading Reading { get; private set; }

                /// <summary>
                /// Puts a reading made here in the place of the reading held, or none where the
                /// acts so far leave the store as it is.
                /// </summary>
                public void Take(StoreReading made)
                {
                    Dispose();
                    Reading = made;
                    _made = made != null;
                }

                /// <summary>Hands the reading held to the caller, who lets it go from here on.</summary>
                public StoreReading Keep()
                {
                    _made = false;
                    return Reading;
                }

                /// <summary>Lets the reading held go where it was made here and not handed on.</summary>
                public void Dispose()
                {
                    if (_made)
                    {
                        _made = false;
                        Reading.Dispose();
                    }
                }
            }
        }
    }
}
