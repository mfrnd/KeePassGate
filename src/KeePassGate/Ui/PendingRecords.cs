using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The KPG tab's buffer for the plugin's store: the acts pressed on the dialog, held until its
    /// OK, `STORE.md` §627.
    /// </summary>
    /// <remarks>
    /// **Each press composes its act's records at the press**, from the store as the page showed
    /// it, and the act is held here. **The pages read the store with the acts beside it**, planned
    /// and applied in memory over the live entries, so a page shows what OK would leave.
    ///
    /// **At OK the acts are written in the order they were pressed**, through the live store and
    /// so through the writer, each held to the store as it stands then: an act whose ground went
    /// meanwhile is left out and named. An authority made on the dialog and retired on it reaches
    /// no database, its retirement with it. **Cancel drops every act**, and a store key made on
    /// the dialog goes with the act that held it.
    ///
    /// **What an act takes out of the custom data is not held here**: the admissions a Recreate or
    /// a Delete takes away are read as gone while it waits, and `PendingDatabase` takes them out of
    /// the live custom data at OK only where the write took the act.
    /// </remarks>
    public sealed class PendingRecords : IGateStore
    {
        private readonly Func<IGateStore> _live;
        private readonly List<StoreAct> _acts = new List<StoreAct>();

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

        /// <summary>Whether an act waiting takes every admission away, as a Recreate and a Delete do.</summary>
        public bool EndsAdmissions
        {
            get { return _acts.Any(act => act.EndsAdmissions); }
        }

        /// <summary>
        /// The store as OK would leave it: the live store with the acts written over it in memory,
        /// or the live store as it is where they leave nothing or no longer apply.
        /// </summary>
        public StoreReading Read()
        {
            if (_acts.Count > 0)
            {
                using (var live = _live().Read())
                {
                    var write = StoreWrite.Plan(live, _acts, GateVersion.Current);
                    if (write.Refused == null && !write.Empty)
                    {
                        var applied = write.Applied(live.Candidates, live.Deletions);
                        return StoreReading.Afresh(applied.Key, applied.Value);
                    }
                }
            }

            // Nothing held, or nothing the acts leave: the store as it is, read for the caller.
            return _live().Read();
        }

        /// <summary>
        /// Holds these acts for the dialog's OK, where they apply to the store as the page shows
        /// it; refused, with nothing held, where they do not, and where nothing is written into
        /// the store in this session.
        /// </summary>
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

            using (var shown = Read())
            {
                var write = StoreWrite.Plan(shown, acts, GateVersion.Current);
                if (write.Refused != null)
                {
                    return StoreWritten.Refused(write.Refused, write.LeftOut);
                }

                _acts.AddRange(acts);
                return StoreWritten.Waiting(write.Taken, write.LeftOut);
            }
        }

        /// <summary>Why the live store takes no write of the operator's in this session, or null.</summary>
        public string Unwritable
        {
            get { return _live().Unwritable; }
        }

        /// <summary>Drops every act, which is what Cancel means, and overwrites the keys they held.</summary>
        public void Discard()
        {
            foreach (var act in _acts)
            {
                act.Forget();
            }

            _acts.Clear();
        }
    }
}
