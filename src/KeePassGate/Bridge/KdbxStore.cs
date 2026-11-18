using System;
using System.Collections.Generic;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The plugin's store in a database KeePass holds, on KeePass's own thread: read afresh, and
    /// written by a plan applied to its entries, `STORE.md` §627.
    /// </summary>
    /// <remarks>
    /// **Read afresh, inline, and nothing kept.** It is read where a turn of KeePass's thread is
    /// already running, the tab's, the chooser's and the writer's, so it takes no hold of its own:
    /// a hold asked for inside such a turn is the wait `HoldMisused` refuses. Each reading checks every
    /// version again, which costs the first reading's price, `STORE.md` §620; the gate's own
    /// readings, which keep the checked forms, are `StoreReadings`.
    ///
    /// **It saves nothing.** A write of the operator's is in force in the database from the
    /// moment it is applied, the database is marked, and the operator's save writes it, as every
    /// change on the KPG tab is written, `STORE.md` §627.
    ///
    /// **A reading handed out is the caller's to let go**, which overwrites the bytes of every key
    /// its forms found, as the gate's readings are when they are let go. The strings a key passes
    /// through on its way there, the entry's text and the members read out of it, cannot be
    /// overwritten, `CA.md` §208.
    /// </remarks>
    internal sealed class KdbxStore : IGateStore
    {
        private readonly PwDatabase _database;
        private readonly string _plugin;
        private readonly IGateLog _log;

        public KdbxStore(PwDatabase database, string plugin, IGateLog log)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
            _plugin = plugin;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>The store as the database holds it now, for the caller to let go.</summary>
        /// <exception cref="DatabaseShut">The database has no tree: locked or closed.</exception>
        public StoreReading Read()
        {
            return Read(false);
        }

        /// <summary>The same, and with every entry of the tree where a write is planned against it.</summary>
        private StoreReading Read(bool everyEntry)
        {
            var root = _database.RootGroup;
            if (root == null)
            {
                throw new DatabaseShut("the database is not open");
            }

            var walk = StoreSource.Of(_database, root, everyEntry);
            return StoreReading.Afresh(walk.Candidates, walk.Deletions, walk.Present);
        }

        /// <summary>
        /// Plans the write against the store as it stands and applies it, marking the database:
        /// whole, or nothing and why.
        /// </summary>
        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            using (var reading = Read(true))
            {
                var write = StoreWrite.Plan(reading, acts, _plugin);
                if (write.Refused != null)
                {
                    return StoreWritten.Refused(write.Refused, write.LeftOut);
                }

                if (!write.Empty)
                {
                    StoreEntries.Apply(_database, write, reading);
                }

                foreach (var left in write.LeftOut)
                {
                    _log.Warn("an act on the plugin's store was left out, because what it was composed against went: " + left);
                }

                return StoreWritten.Done(write.Taken, write.LeftOut, !write.Empty);
            }
        }

        /// <summary>Null: this writes whenever it is asked, and the questions before it are the writer's.</summary>
        public string Unwritable
        {
            get { return null; }
        }
    }
}
