using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Func<PwEntry, string> _holding;

        /// <summary>A store over this database, written with no window asked about.</summary>
        public KdbxStore(PwDatabase database, string plugin, IGateLog log)
            : this(database, plugin, log, null)
        {
        }

        /// <summary>The same, asking of every entry a write touches whether a window holds it.</summary>
        /// <param name="holding">
        /// Why an entry cannot be written now, a window holding a copy of it, or null where it can:
        /// the writer's question, `STORE.md` §626; null asks nothing, which is a reading alone.
        /// </param>
        public KdbxStore(PwDatabase database, string plugin, IGateLog log, Func<PwEntry, string> holding)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
            _plugin = plugin;
            _log = log ?? NullGateLog.Instance;
            _holding = holding;
        }

        /// <summary>The store as the database holds it now, for the caller to let go.</summary>
        /// <exception cref="DatabaseShut">The database has no tree: locked or closed.</exception>
        public StoreReading Read()
        {
            return Read(false);
        }

        /// <summary>
        /// The store as the database holds it now, with every entry of the tree, which a write is
        /// planned against, `STORE.md` §627: for the caller to let go at the end of its turn.
        /// </summary>
        /// <exception cref="DatabaseShut">The database has no tree: locked or closed.</exception>
        public StoreReading ForWrite()
        {
            return Read(true);
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
        /// Plans these acts against a reading <see cref="ForWrite"/> made in this turn, and asks
        /// whether a window holds any entry the plan deletes, sets back or protects again,
        /// `STORE.md` §626: refused, held, or staged to be applied.
        /// </summary>
        public StagedWrite Stage(StoreReading reading, IReadOnlyList<StoreAct> acts)
        {
            var write = StoreWrite.Plan(reading, acts, _plugin);
            if (write.Refused != null)
            {
                return new StagedWrite(_database, write, reading, null);
            }

            // Asked once the plan exists, of what it deletes, sets back and protects: an entry
            // dialog puts its copy of an entry back at its OK, `STORE.md` §626, which would take
            // back what this write did to that entry.
            var held = write.Empty || _holding == null
                ? null
                : StoreEntries.Touched(_database, write, reading).Select(_holding).FirstOrDefault(why => why != null);
            return new StagedWrite(_database, write, reading, held);
        }

        /// <summary>
        /// Plans the write against the store as it stands and applies it, marking the database:
        /// whole, or nothing and why. Nothing of it is saved, so nothing is taken back.
        /// </summary>
        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            using (var reading = ForWrite())
            {
                var staged = Stage(reading, acts);
                var refused = staged.Refused ?? staged.Held;
                if (refused != null)
                {
                    return StoreWritten.Refused(refused, staged.Write.LeftOut);
                }

                staged.Apply();

                foreach (var left in staged.Write.LeftOut)
                {
                    _log.Warn("an act on the plugin's store was left out, because what it was composed against went: " + left);
                }

                return StoreWritten.Done(staged.Write.Taken, staged.Write.LeftOut, !staged.Write.Empty);
            }
        }

        /// <summary>Null: this writes whenever it is asked, and the questions before it are the writer's.</summary>
        public string Unwritable
        {
            get { return null; }
        }
    }

    /// <summary>
    /// A write of the plugin's own planned against a database in this turn, `STORE.md` §627:
    /// refused, held by a window, or ready to apply.
    /// </summary>
    internal sealed class StagedWrite
    {
        private readonly PwDatabase _database;
        private readonly StoreReading _reading;

        public StagedWrite(PwDatabase database, StoreWrite write, StoreReading reading, string held)
        {
            _database = database;
            _reading = reading;
            Write = write;
            Held = held;
        }

        /// <summary>The plan.</summary>
        public StoreWrite Write { get; private set; }

        /// <summary>Why the plan refuses the write, or null where it does not.</summary>
        public string Refused
        {
            get { return Write.Refused; }
        }

        /// <summary>
        /// Why a window holds an entry the write touches, or null where none does.
        /// </summary>
        public string Held { get; private set; }

        /// <summary>
        /// Applies the write to the database and answers its undo, which takes back everything it
        /// did but the database's mark; nothing to take back for a write that leaves nothing.
        /// </summary>
        public Action Apply()
        {
            if (Refused != null || Held != null)
            {
                throw new InvalidOperationException("A write refused or held is applied to nothing.");
            }

            return Write.Empty ? () => { } : StoreEntries.Apply(_database, Write, _reading);
        }
    }
}
