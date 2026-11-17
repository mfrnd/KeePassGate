using System;
using System.Threading;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Ui;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The store of one served database, read once for each hold, `STORE.md` §620.
    /// </summary>
    /// <remarks>
    /// **Read at every hold, and kept for none.** Inside a hold by a connection the tree cannot
    /// move, so the reading made for that hold answers every question of it, the guard's lookups
    /// among them, and no other hold. On KeePass's own thread every question reads afresh, since
    /// a turn there may change the tree between two of them, <see cref="IReadHold.Stamp"/>, with
    /// a single exception: a change of the API's, between the moment its node is looked up and
    /// the moment its change is handed back to the writer, <see cref="InTurn{T}"/>.
    ///
    /// **Between two holds only the checked form of a version is kept**, by the entry's
    /// identifier and the very objects of the version, so a version nothing changed is not read
    /// again, and its text is never decrypted again. Nothing kept is an answer: every hold walks
    /// every entry and works everything else out again. The reading of the last hold is held
    /// until the next replaces it, and answers nothing after its hold, since no stamp is given
    /// twice. What is kept goes, with its keys overwritten, when the cache lets go of the target
    /// at a lock, a close or a change of what is served, <see cref="Forget"/>, even where a
    /// connection still holds the target, as a pairing can for its whole budget: that connection
    /// reads afresh from then on and keeps nothing.
    ///
    /// **Who reads and who writes, and no thread of KeePass's waits on a reading.** Two locks.
    /// `_gate` guards the fields that say what was read for which hold, and is never held while
    /// anything is read. `_work` is held by the one reading that uses the kept forms and puts the
    /// next in their place, and is only ever tried, never waited for: a reading that finds it
    /// taken, which is one beside a hold that lapsed with its reading still going, reads with
    /// nothing kept and keeps nothing. So two readings never share a form they could change, a
    /// reading in progress keeps no other waiting, and <see cref="Forget"/> and
    /// <see cref="Dispose"/>, which KeePass's thread calls where the cache lets go at a lock and
    /// a connection's thread calls where it held the target last, mark what is kept to go and
    /// return, leaving the reading in progress to let go of it when it ends.
    /// </remarks>
    internal sealed class StoreReadings : IDisposable
    {
        private readonly object _gate = new object();
        private readonly object _work = new object();
        private readonly IReadHold _hold;
        private PwDatabase _database;
        private bool _keepNothing;
        private StoreKept _kept = new StoreKept();
        private long _stamp;
        private StoreReading _reading;

        // The change a thread of KeePass's is making, and its reading, `InTurn`.
        private int _turnThread;
        private int _turnDepth;
        private StoreReading _turnReading;


        public StoreReadings(PwDatabase database, IReadHold hold)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (hold == null)
            {
                throw new ArgumentNullException(nameof(hold));
            }

            _database = database;
            _hold = hold;
        }

        /// <summary>
        /// The reading of the hold the calling thread is inside, of the database as it stands.
        /// </summary>
        /// <remarks>
        /// Throws <see cref="DatabaseShut"/> where the database has gone, locked or closed or let
        /// go with its target, as an admission record read now does.
        /// </remarks>
        public StoreReading Now()
        {
            // Into a local first: the field goes when the target is let go, from another thread.
            var database = _database;
            var root = database == null ? null : database.RootGroup;
            if (root == null)
            {
                throw new DatabaseShut("the database closed under this connection");
            }

            return Now(database, root);
        }

        /// <summary>
        /// The same, under a root the caller has already read, so that a lookup reads the store of
        /// the very tree it looks through.
        /// </summary>
        public StoreReading Now(PwDatabase database, PwGroup root)
        {
            var thread = Environment.CurrentManagedThreadId;
            bool inTurn;
            lock (_gate)
            {
                inTurn = _turnDepth > 0 && _turnThread == thread;
                if (inTurn && _turnReading != null)
                {
                    return _turnReading;
                }
            }

            // A change's turn is on KeePass's thread, whose stamp is 0 in any case.
            var stamp = _hold.Stamp;
            lock (_gate)
            {
                if (!inTurn && stamp != 0 && stamp == _stamp && _reading != null)
                {
                    return _reading;
                }
            }

            var reading = Read(database, root);

            lock (_gate)
            {
                if (inTurn)
                {
                    _turnReading = reading;
                }
                else if (stamp != 0 && !_keepNothing)
                {
                    _stamp = stamp;
                    _reading = reading;
                }
            }

            return reading;
        }

        /// <summary>
        /// Answers every question this thread asks of the store while <paramref name="change"/>
        /// runs from a single reading: a change of the API's, from the lookup of its node to the
        /// change it hands the writer, on KeePass's own thread.
        /// </summary>
        /// <remarks>
        /// **Read once, and safe to read once**: nothing a change of the API's makes can change the
        /// store, since every route refuses an entry of the store, a string of the store and a
        /// group that holds either, `STORE.md` §633, and nothing else runs on KeePass's thread
        /// while the change does. The save the writer makes afterwards is outside it, so whatever
        /// the save runs is read afresh.
        ///
        /// **Nothing pumps inside it, which is what makes "nothing else runs" true**, checked
        /// against what runs between `DatabaseWriter.ChangeOnTheUiThread` calling the change and
        /// the change returning: the vault's lookups and its change of the tree, the dialogs'
        /// watch asked of what is open, and the router's decision inside the turn, which reads
        /// the client again, asks the rules and keeps its audit lines, `SpecRouter.Inside`. None
        /// of them shows a dialog, calls `DoEvents`, or waits on another thread: a hold asked for
        /// on KeePass's own thread runs inline, the log queues a line written there, the audit's
        /// lines are written and paced once the request is answered, and the writer saves only
        /// after the change has returned. A turn of the writer's is never run inside another,
        /// since the writer's lock is taken before the turn is asked for.
        /// </remarks>
        public T InTurn<T>(Func<T> change)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }

            var thread = Environment.CurrentManagedThreadId;
            lock (_gate)
            {
                if (_turnDepth > 0 && _turnThread != thread)
                {
                    // Another thread's change, which the writer never runs beside this one: read
                    // as every other question is.
                    return change();
                }

                _turnThread = thread;
                _turnDepth++;
            }

            try
            {
                return change();
            }
            finally
            {
                lock (_gate)
                {
                    _turnDepth--;
                    if (_turnDepth == 0)
                    {
                        _turnReading = null;
                    }
                }
            }
        }

        /// <summary>
        /// Keeps nothing from here on, and overwrites every key kept: what the cache does when it
        /// lets go of the target at a lock, a close or a change of what is served, whoever still
        /// holds the target. Readings go on, each reading afresh.
        /// </summary>
        /// <remarks>
        /// A connection can hold a target long after the cache has let go of it, a pairing for
        /// the whole of its budget, and what was kept would otherwise outlast the lock that far.
        /// </remarks>
        public void Forget()
        {
            lock (_gate)
            {
                _keepNothing = true;
                _reading = null;
                _stamp = 0;
            }

            ClearIfLetGo();
        }

        /// <summary>
        /// Overwrites every key kept and drops every form, or marks them to go with the reading
        /// that holds them, which then lets go of them: never waited for. Called by whoever lets
        /// go of the target last, which is KeePass's thread where the cache does at a lock or a
        /// close, and a connection's thread where a connection held the target longer.
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                _database = null;
                _keepNothing = true;
                _reading = null;
                _stamp = 0;
            }

            ClearIfLetGo();
        }

        /// <summary>
        /// A reading of the store under this root, with the kept forms where nothing else is
        /// using them, putting the forms it met in their place.
        /// </summary>
        private StoreReading Read(PwDatabase database, PwGroup root)
        {
            var walk = StoreSource.Of(database, root);

            if (!Monitor.TryEnter(_work))
            {
                // Another reading holds the kept forms, which is one beside a hold that lapsed:
                // this reads with nothing kept, and keeps nothing, the keys of its forms
                // overwritten as it ends.
                var scratch = new StoreKept();
                try
                {
                    return StoreReading.Of(walk.Candidates, walk.Deletions, null, scratch);
                }
                finally
                {
                    scratch.Clear();
                }
            }

            var next = new StoreKept();
            var published = false;
            try
            {
                var reading = StoreReading.Of(walk.Candidates, walk.Deletions, _kept, next);
                _kept.Leave(next);
                _kept = next;
                published = true;
                return reading;
            }
            finally
            {
                if (!published)
                {
                    // A reading that did not end: what it made and nobody keeps is overwritten,
                    // and what was kept before stays kept.
                    next.Leave(_kept);
                }

                Monitor.Exit(_work);

                // A target let go while this read: `Dispose` marked it before trying the lock, and
                // this looks after letting the lock go, so one of the two clears what is kept.
                ClearIfLetGo();
            }
        }

        private void ClearIfLetGo()
        {
            bool letGo;
            lock (_gate)
            {
                letGo = _keepNothing;
            }

            if (!letGo || !Monitor.TryEnter(_work))
            {
                return;
            }

            try
            {
                _kept.Clear();
            }
            finally
            {
                Monitor.Exit(_work);
            }
        }
    }
}
