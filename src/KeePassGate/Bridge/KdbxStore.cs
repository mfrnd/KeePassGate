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
    /// **Read afresh, inline, and no checked form kept.** It is read where a turn of KeePass's
    /// thread is already running, the tab's, the chooser's and the writer's, so it takes no hold of
    /// its own: a hold asked for inside such a turn is the wait `HoldMisused` refuses. Each reading
    /// checks every version again, which costs the first reading's price, `STORE.md` §620; the
    /// gate's own readings, which keep the checked forms, are `StoreReadings`.
    ///
    /// **What it keeps is the last reading made for a write**, with the entries the walk of that
    /// reading passed, and only until a write is staged against it: a write finds its entries
    /// there, `TreeEntries`, and a reading staged against once stages nothing more, nor does a
    /// reading that a later reading for a write replaced.
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

        /// <summary>The reading <see cref="ForWrite"/> made last, which a write is staged against.</summary>
        private StoreReading _forWrite;

        /// <summary>Every entry the walk of that reading passed, in the order walked.</summary>
        private IReadOnlyList<PwEntry> _walked;

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
            var walk = Walked(false);
            return StoreReading.AfreshInTree(walk.Candidates, walk.Deletions, null, walk.Nodes);
        }

        /// <summary>
        /// The store as the database holds it now, with every entry of the tree, which a write is
        /// planned against and finds its entries in, `STORE.md` §627: for the caller to let go at
        /// the end of its turn. The store keeps it, with the entries its walk passed, until a write
        /// is staged against it or a later reading for a write replaces it.
        /// </summary>
        /// <exception cref="DatabaseShut">The database has no tree: locked or closed.</exception>
        public StoreReading ForWrite()
        {
            _forWrite = null;
            _walked = null;
            var walk = Walked(true);
            var reading = StoreReading.AfreshInTree(walk.Candidates, walk.Deletions, walk.Present, walk.Nodes);
            _forWrite = reading;
            _walked = walk.Entries;
            return reading;
        }

        /// <summary>A walk of the database's tree, keeping every entry it passes where a write is planned against it.</summary>
        private StoreSource.Walk Walked(bool everyEntry)
        {
            var root = _database.RootGroup;
            if (root == null)
            {
                throw new DatabaseShut("the database is not open");
            }

            return StoreSource.Of(_database, root, everyEntry);
        }

        /// <summary>
        /// Plans these acts against the reading <see cref="ForWrite"/> made last in this turn, and
        /// asks whether a window holds any entry the plan deletes, sets back or protects again,
        /// `STORE.md` §626: refused, held, or staged to be applied.
        /// </summary>
        /// <remarks>
        /// **The entries are found in that reading's walk**, `TreeEntries`: asking which entries
        /// the write touches, deleting, setting back and protecting again look each identifier up
        /// in the entries that walk passed, and walk the tree for none of them. The store lets the
        /// reading go here, so a reading is staged against once.
        ///
        /// **A plan those entries cannot serve is refused as a defect**: a plan that adds a record
        /// under an identifier an entry of the tree holds, that deletes, sets back or protects
        /// again a record it adds, or that sets back or protects again a record it deletes,
        /// `TreeEntries.Unsound`. `StoreWrite` makes no such plan, so the exception is thrown only
        /// where that stops being true, before anything is changed.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The reading is not the reading <see cref="ForWrite"/> made last, or a write was staged
        /// against it already: its walk is not the write's to find entries in.
        /// </exception>
        /// <exception cref="InvalidOperationException">The plan is a plan those entries cannot serve.</exception>
        public StagedWrite Stage(StoreReading reading, IReadOnlyList<StoreAct> acts)
        {
            if (reading == null || !ReferenceEquals(reading, _forWrite))
            {
                throw new ArgumentException("A write is staged once, against the reading this store made last for a write.", nameof(reading));
            }

            var walked = _walked;
            _forWrite = null;
            _walked = null;

            var write = StoreWrite.Plan(reading, acts, _plugin);
            if (write.Refused != null || write.Empty)
            {
                return new StagedWrite(_database, write, reading, null, null);
            }

            var tree = new TreeEntries(walked);
            var unsound = StoreEntries.Unsound(tree, write, reading);
            if (unsound != null)
            {
                throw new InvalidOperationException("A plan the entries of its reading cannot serve: " + unsound + ".");
            }

            // Asked once the plan exists, of what it deletes, sets back and protects: an entry
            // dialog puts its copy of an entry back at its OK, `STORE.md` §626, which would take
            // back what this write did to that entry.
            var held = _holding == null
                ? null
                : StoreEntries.Touched(tree, write, reading).Select(_holding).FirstOrDefault(why => why != null);
            return new StagedWrite(_database, write, reading, tree, held);
        }

        /// <summary>
        /// Plans the write against the store as it stands and applies it, marking the database:
        /// whole, or nothing and why. Nothing of it is saved, so nothing is taken back.
        /// </summary>
        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            using (var reading = ForWrite())
            {
                return Written(reading, acts);
            }
        }

        /// <summary>
        /// Reads the store afresh with every entry in this turn, composes an act over that reading,
        /// and plans and applies it against the same reading, marking the database: what a write
        /// whose act depends on the versions it replaces makes, an OK on a node's dialog among it.
        /// Answers what came of it, a write of nothing where <paramref name="compose"/> answers
        /// null.
        /// </summary>
        /// <remarks>
        /// **A single reading**, `STORE.md` §629: an act composed over a reading and planned
        /// against another would write anew versions the plan does not hold, and leave out of the
        /// summary entry versions it does. The act is this write's own, so it is let go here.
        /// </remarks>
        public StoreWritten Write(Func<StoreReading, StoreAct> compose)
        {
            if (compose == null)
            {
                throw new ArgumentNullException(nameof(compose));
            }

            using (var reading = ForWrite())
            {
                var act = compose(reading);
                if (act == null)
                {
                    return StoreWritten.Done(Array.Empty<StoreAct>(), Array.Empty<string>(), false);
                }

                try
                {
                    return Written(reading, new[] { act });
                }
                finally
                {
                    act.Forget();
                }
            }
        }

        /// <summary>Plans these acts against this reading and applies them, or answers why not.</summary>
        private StoreWritten Written(StoreReading reading, IReadOnlyList<StoreAct> acts)
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

            return StoreWritten.Done(staged.Write.Taken, staged.Write.LeftOut, !staged.Write.Empty, staged.Write.Sealed);
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
        private readonly TreeEntries _tree;

        /// <summary>A plan staged against this reading, with why a window holds it, or null.</summary>
        /// <param name="tree">The entries of the tree the write finds its entries in; null for a write refused or empty.</param>
        public StagedWrite(PwDatabase database, StoreWrite write, StoreReading reading, TreeEntries tree, string held)
        {
            _database = database;
            _reading = reading;
            _tree = tree;
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

            return Write.Empty ? () => { } : StoreEntries.Apply(_database, Write, _reading, _tree);
        }
    }
}
