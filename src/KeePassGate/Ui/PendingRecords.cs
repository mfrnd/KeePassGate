using System;
using System.Collections.Generic;
using System.Linq;
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
    /// the act is held here. **A revocation holds what is to be done**: its records are composed
    /// again wherever the buffer is read and at OK, against the store as it stands then with the
    /// acts pressed before it written over it, `AdmissionActs.Again`, so OK writes it under the
    /// store key it finds. **The pages read the store with the acts beside it**, composed so,
    /// planned and applied in memory over the live entries, so a page shows what OK would write
    /// now.
    ///
    /// **At OK the acts are written in the order they were pressed**, through the live store and
    /// so through the writer, each held to the store as it stands then: an act whose ground went
    /// meanwhile is left out and named. **Cancel drops every act**, and a store key made on the
    /// dialog goes with the act that held it.
    ///
    /// **The buffer holds what the store would be written with.** A press that retires an
    /// authority an act held made takes that act out with it, and what is left of the two takes
    /// the made act's place: Create then Delete leaves nothing, and Recreate then Delete leaves the
    /// Delete of the authority the database holds, its retirements holding the store key as the
    /// Delete's held it. A later press is then composed against what OK would write. The plan's
    /// own dropping of an authority made and retired in a single write stays as the second line.
    ///
    /// **A revocation has no ground**, so OK always writes it; the pages read the client's
    /// admissions as gone while it waits, since the plan deletes what it refuses, `STORE.md` §627.
    /// </remarks>
    public sealed class PendingRecords : IGateStore
    {
        private readonly Func<IGateStore> _live;
        private readonly List<StoreAct> _acts = new List<StoreAct>();

        // The acts composed for OK's write, which go with the acts held.
        private readonly List<StoreAct> _composed = new List<StoreAct>();

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
            if (_acts.Count > 0)
            {
                using (var live = _live().Read())
                {
                    var composed = Compose(live, _acts);
                    try
                    {
                        var over = Over(live, composed);
                        if (over != null)
                        {
                            return over;
                        }
                    }
                    finally
                    {
                        LetGo(composed, _acts);
                    }
                }
            }

            // Nothing held, or nothing the acts leave: the store as it is, read for the caller.
            return _live().Read();
        }

        /// <summary>
        /// The acts held, composed against the store as it stands now for the write at OK, in
        /// the order pressed: see <see cref="Compose"/>. What is composed here is let go by
        /// <see cref="Discard"/>, with the acts held.
        /// </summary>
        public IReadOnlyList<StoreAct> Composed()
        {
            using (var live = _live().Read())
            {
                var composed = Compose(live, _acts);
                _composed.AddRange(composed.Where(act => !_acts.Contains(act)));
                return composed;
            }
        }

        /// <summary>
        /// The acts in the order pressed, each act on a client composed again against this
        /// reading with the acts before it written over it, `AdmissionActs.Again`, and every
        /// other act as it was pressed. The acts composed here are the caller's to let go,
        /// <see cref="LetGo"/>.
        /// </summary>
        private static List<StoreAct> Compose(StoreReading live, IReadOnlyList<StoreAct> acts)
        {
            var composed = new List<StoreAct>();
            var done = false;
            try
            {
                foreach (var act in acts)
                {
                    if (!AdmissionActs.Composes(act.Name))
                    {
                        composed.Add(act);
                        continue;
                    }

                    var over = composed.Count == 0 ? null : Over(live, composed.ToList());
                    try
                    {
                        composed.Add(AdmissionActs.Again(over ?? live, act));
                    }
                    finally
                    {
                        if (over != null)
                        {
                            over.Dispose();
                        }
                    }
                }

                done = true;
                return composed;
            }
            finally
            {
                if (!done)
                {
                    LetGo(composed, acts);
                }
            }
        }

        /// <summary>
        /// The reading with these acts written over it in memory, or null where they would be
        /// refused or leave nothing.
        /// </summary>
        private static StoreReading Over(StoreReading live, IReadOnlyList<StoreAct> acts)
        {
            var write = StoreWrite.Plan(live, acts, GateVersion.Current);
            if (write.Refused != null || write.Empty)
            {
                return null;
            }

            var applied = write.Applied(live.Candidates, live.Deletions);
            return StoreReading.Afresh(applied.Key, applied.Value);
        }

        /// <summary>
        /// Overwrites the keys of the acts composed that are not among the acts held.
        /// </summary>
        private static void LetGo(IEnumerable<StoreAct> composed, IReadOnlyList<StoreAct> held)
        {
            foreach (var act in composed.Where(act => !held.Contains(act)))
            {
                act.Forget();
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
        /// the class remarks have it, and the acts folded are let go once it is held.
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

            var next = _acts.ToList();
            var letGo = new List<StoreAct>();
            var standing = new List<StoreAct>();
            List<StoreAct> composed = null;
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
                        composed = Compose(live, next);
                        var write = StoreWrite.Plan(live, composed, GateVersion.Current);
                        if (write.Refused != null)
                        {
                            return StoreWritten.Refused(write.Refused, write.LeftOut);
                        }

                        var written = composed;
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
                return StoreWritten.Waiting(standing, leftOut);
            }
            finally
            {
                if (composed != null)
                {
                    LetGo(composed, next);
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
        /// held it. It is named as the act pressed, but where the made act starts the store, as
        /// only a Create on a database with no key does: what is left then starts it too, and is
        /// a Create.
        /// </remarks>
        /// <param name="letGo">Where the acts taken out are put, for the caller to let go once the press is held.</param>
        private static StoreAct Fold(List<StoreAct> buffer, StoreAct act, ICollection<StoreAct> letGo)
        {
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
                var folded = new StoreAct(name, key, maker.Against, left, maker.Why);
                buffer.Insert(at, folded);
                return folded;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
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
        /// keys of the acts composed from them for OK.
        /// </summary>
        public void Discard()
        {
            foreach (var act in _acts.Concat(_composed))
            {
                act.Forget();
            }

            _acts.Clear();
            _composed.Clear();
        }
    }
}
