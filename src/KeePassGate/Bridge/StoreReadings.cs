using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
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
    /// the moment its change is handed back to the writer, <see cref="InTurn{T}"/>. **The rules
    /// are of the same reading**, <see cref="Rules"/>: worked out for the first question of a hold
    /// about a rule, and let go with the reading.
    ///
    /// **Between two holds only the checked form of a version is kept**, by the entry's
    /// identifier and the very objects of the version, so a version nothing changed is not read
    /// again, and its text is never decrypted again. Nothing kept is an answer: every hold walks
    /// every entry and works everything else out again. The reading of the last hold is held
    /// until the next replaces it, and answers nothing after its hold, since no stamp is given
    /// twice. What is kept goes, with its keys overwritten, when the cache lets go of the target
    /// at a lock, a close or a change of what is served, <see cref="Forget"/>, even where a
    /// connection still holds the target, as a pairing can for its whole budget: that connection
    /// reads afresh from then on and keeps nothing but the rules a hold worked out, for that hold
    /// alone.
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
        private readonly IGateLog _log;
        private PwDatabase _database;
        private bool _keepNothing;
        private StoreKept _kept = new StoreKept();
        private long _stamp;
        private StoreReading _reading;

        // The change a thread of KeePass's is making, and its reading, `InTurn`.
        private int _turnThread;
        private int _turnDepth;
        private StoreReading _turnReading;

        // The rules of a reading kept above, worked out for the first question about a rule and
        // kept while that reading is, and the stamp of the hold they were worked out in, for
        // which they are kept as well where the reading is not: `Rules`. Never answered for any
        // other reading or hold.
        private StoreReading _rulesOf;
        private long _rulesStamp;
        private StoredRules _rules;

        // The state the log last named, and whether it last named the store past half its room,
        // so that a state lasting over many requests is named once. Neither answers anything:
        // every request is judged by the reading of its own hold. Whole to start with, since a
        // word is asked only of a database the gate served or holds back, and a store with no key
        // is a word to the gate.
        private StoreState _logged = StoreState.Whole;
        private bool _loggedHalf;

        public StoreReadings(PwDatabase database, IReadHold hold, IGateLog log)
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
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>
        /// The word a request to this database that belongs to this authority is answered with
        /// under `503`, or null, read now in a hold of KeePass's thread, `STORE.md` §656:
        /// <see cref="WordOf"/>.
        /// </summary>
        /// <remarks>
        /// **A reading that could not be made is no answer**, and never a store that is whole:
        /// <see cref="VaultUnsettled"/> where KeePass's thread could not be held, and
        /// <see cref="DatabaseShut"/> where the database has gone, each thrown to the caller,
        /// which answers them `busy` and `locked`.
        /// </remarks>
        /// <param name="authority">
        /// The `id` of the authority the request belongs to: the authority its connection arrived
        /// under, or the issuer of the admission it writes.
        /// </param>
        public string WordFor(string authority)
        {
            return _hold.Hold(() => WordNow(authority));
        }

        /// <summary>
        /// The word a request that belongs to this authority is answered with under `503` where
        /// the store reads so, or null, `STORE.md` §656: the store's own word,
        /// <see cref="StoreWords.Answered"/>; and `undecided` where the store is whole and several
        /// authorities stand, this authority among them, which serve nothing until the operator
        /// keeps an authority.
        /// </summary>
        /// <remarks>
        /// **`undecided` promises a decision that can still serve the request.** Where several
        /// authorities stand and the request's own authority is not among them, retired or gone,
        /// no decision can, so there is no word: the request is answered as a request whose
        /// authority went is, an admission no longer found or an invitation gone.
        ///
        /// **Asked by a connection made before the second authority stood as well**: its target
        /// was built while a single authority stood, and a renewal or a change waiting for its
        /// turn is answered as a connection made now is. And by a write of a client's in its own
        /// turn, which is answered so before anything is planned, asked or spent.
        /// </remarks>
        /// <param name="authority">The `id` of the authority the request belongs to.</param>
        public static string WordOf(StoreReading reading, string authority)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            var word = StoreWords.Answered(reading.State);
            if (word != null || reading.Answering.Count < 2)
            {
                return word;
            }

            return reading.Answering.Any(record => string.Equals(record.Form.Member(StoreKinds.Id), authority, StringComparison.Ordinal))
                ? HeldBackTarget.Undecided
                : null;
        }

        private string WordNow(string authority)
        {
            var reading = Now();
            var word = StoreWords.Answered(reading.State);
            var answer = WordOf(reading, authority);

            // Whether the reading serves an authority: the store holds nothing back and several
            // authorities do not stand. What is logged is about the database and not about the
            // request asking, since several authorities standing serve nothing whoever asks.
            var serves = word == null && reading.Answering.Count < 2;

            // A store served past half its room: the operator is told from there on, `STORE.md`
            // §643, once for as long as it stays there, as a state is. Several authorities
            // standing serve nothing, so nothing is told as served while they do.
            var half = serves && reading.Room != null && reading.Room.PastHalf;

            StoreState was;
            bool wasHalf;
            lock (_gate)
            {
                was = _logged;
                _logged = reading.State;
                wasHalf = _loggedHalf;
                _loggedHalf = half;
            }

            if (half && !wasHalf)
            {
                _log.Warn("serving a database whose store is past half its room: " + reading.Room.Grown);
            }

            if (was == reading.State)
            {
                return answer;
            }

            // Logged inside the hold, which queues the line, `ControlUiThread.MayNotWait`. What
            // the reading counted beside its state is what tells an operator where to look.
            var counted = !reading.Judged ? string.Empty : string.Format(
                CultureInfo.InvariantCulture,
                " ({0} damaged, {1} passed over, {2} counting by their history)",
                reading.Damage,
                reading.Passed,
                reading.ByHistory);
            if (word != null)
            {
                _log.Warn("not serving a database whose store is " + word + ": " + reading.Reason + counted);
            }
            else if (serves && StoreWords.Answered(was) != null)
            {
                _log.Info("serving a database whose store held it back as " + StoreWords.Answered(was) + " again" + counted);
            }

            return answer;
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
            bool inTurn;
            long stamp;
            return Made(out inTurn, out stamp) ?? Made(database, root, inTurn, stamp);
        }

        /// <summary>
        /// The reading made already for the question the calling thread asks, the turn's or the
        /// hold's, or null where none is: with whether the thread is in a change's turn, and the
        /// stamp of the hold it is inside, 0 in a turn.
        /// </summary>
        private StoreReading Made(out bool inTurn, out long stamp)
        {
            var thread = Environment.CurrentManagedThreadId;
            lock (_gate)
            {
                inTurn = _turnDepth > 0 && _turnThread == thread;
                if (inTurn && _turnReading != null)
                {
                    stamp = 0;
                    return _turnReading;
                }
            }

            // A change's turn is on KeePass's thread, whose stamp is 0 in any case.
            stamp = _hold.Stamp;
            lock (_gate)
            {
                if (!inTurn && stamp != 0 && stamp == _stamp && _reading != null)
                {
                    return _reading;
                }
            }

            return null;
        }

        /// <summary>
        /// A reading made now for the question the calling thread asks, kept for the turn or the
        /// hold it is asked in where anything is kept.
        /// </summary>
        private StoreReading Made(PwDatabase database, PwGroup root, bool inTurn, long stamp)
        {
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

                LetRulesGo(stamp);
            }

            return reading;
        }

        /// <summary>
        /// The rules of the reading <see cref="Now(PwDatabase, PwGroup)"/> answers under this
        /// root, `STORE.md` §617 and §620: what a walk up the rules reads at each node.
        /// </summary>
        /// <remarks>
        /// **Of the hold's own reading, and worked out once for it.** Inside a hold every question
        /// is answered from the reading made for that hold, so the rules a walk reads are of the
        /// moment the client was read again and the node was found. They are worked out at the
        /// first question about a rule and kept while that reading is kept, so a listing that
        /// walks from every entry indexes them once; a hold that asks no rule, as the hold a
        /// connection is judged in, works out none. Nothing of them outlives the reading: a later
        /// hold reads the store afresh and works its rules out again, and a reading that is kept
        /// for no hold, on KeePass's own thread outside a change, works its own out each time.
        ///
        /// **Worked out once inside a hold whatever the cache was told.** Once <see cref="Forget"/>
        /// has let go of what is kept, no reading is kept from a question to the next, and a walk
        /// that asked for its rules at each level, for each profile and each verb, read the whole
        /// store each time with KeePass's thread held. So the rules are kept by the stamp of the
        /// hold that worked them out as well, without the reading and its keys, and answered for
        /// that hold alone: the first question of another hold lets them go.
        /// </remarks>
        public StoredRules Rules(PwDatabase database, PwGroup root)
        {
            bool inTurn;
            long stamp;
            var reading = Made(out inTurn, out stamp);
            lock (_gate)
            {
                if (reading != null && ReferenceEquals(reading, _rulesOf))
                {
                    return _rules;
                }

                if (reading == null && stamp != 0 && stamp == _rulesStamp)
                {
                    return _rules;
                }
            }

            reading = reading ?? Made(database, root, inTurn, stamp);
            var rules = StoredRules.Of(reading);
            lock (_gate)
            {
                var kept = ReferenceEquals(reading, _reading) || ReferenceEquals(reading, _turnReading);
                if (kept || stamp != 0)
                {
                    _rulesOf = kept ? reading : null;
                    _rulesStamp = stamp;
                    _rules = rules;
                }
            }

            return rules;
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
                        LetRulesGo(0);
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
                LetRulesGo(0);
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
                LetRulesGo(0);
            }

            ClearIfLetGo();
        }

        /// <summary>
        /// Drops the rules kept where neither reading kept is the reading they were worked out
        /// from and they are not of the hold of this stamp, 0 for none. Under <c>_gate</c>.
        /// </summary>
        private void LetRulesGo(long stamp)
        {
            var ofAReadingKept = _rulesOf != null
                                 && (ReferenceEquals(_rulesOf, _reading) || ReferenceEquals(_rulesOf, _turnReading));
            var ofThisHold = _rulesStamp != 0 && _rulesStamp == stamp;
            if (!ofAReadingKept && !ofThisHold)
            {
                _rulesOf = null;
                _rulesStamp = 0;
                _rules = null;
            }
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
                    return StoreReading.Of(walk.Candidates, walk.Deletions, null, scratch, walk.Nodes);
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
                var reading = StoreReading.Of(walk.Candidates, walk.Deletions, _kept, next, walk.Nodes);
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
