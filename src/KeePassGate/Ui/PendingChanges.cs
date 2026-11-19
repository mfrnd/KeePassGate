using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Store;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A database that is holding changes until the dialog they were made on is accepted.
    /// </summary>
    /// <remarks>
    /// Asked by the one screen that has to care. Everything else on the tab reads and writes as
    /// though the database were the database, which is the point of the decorator; the clients page
    /// is different because what it invites somebody into is answered by the gate, out of the live
    /// file, and the gate cannot see anything that is still waiting here.
    /// </remarks>
    public interface IPendingChanges
    {
        /// <summary>Whether anything at all is waiting for the dialog's OK.</summary>
        bool Waiting { get; }

        /// <summary>Whether an act about a profile name is waiting: a name added or removed.</summary>
        bool ProfilesWaiting { get; }

        /// <summary>
        /// Whether an act of the authority's is waiting on the plugin's store: an authority made,
        /// replaced or deleted. A client revoked is not such an act, since it leaves the authority
        /// a pairing is answered by as it is.
        /// </summary>
        bool AuthorityWaiting { get; }
    }

    /// <summary>
    /// A database whose changes are held until the dialog they were made on is accepted.
    /// </summary>
    /// <remarks>
    /// **This reverses a decision, and the reason is what an operator expects a dialog to mean.**
    /// Every button on the KPG tab used to write at once and mark the database as having
    /// something to save. That was safe, because KeePass's database settings dialog keeps no copy
    /// of this plugin's custom data and therefore never undid any of it, and it was wrong anyway: a
    /// dialog with OK and Cancel on it says that nothing has happened yet, and a tab inside one that
    /// has already happened is a tab lying about the buttons around it. Cancel now means Cancel.
    ///
    /// **A decorator rather than a change to the pages**, so that nothing above this knows. Each
    /// page reads and writes exactly as it did, through the same seams; what it is holding is a
    /// buffer, and the dialog's result decides whether it is applied or dropped. Nothing on the tab
    /// writes to a group or an entry, so the rules on nodes are read straight through.
    ///
    /// **Replayed key by key, never wholesale.** Writing a whole snapshot back would be this
    /// plugin doing to itself what KeePass's group dialog does to it: a pairing that landed while
    /// the dialog was open writes an admission record to the live database and saves it, and a
    /// wholesale write would destroy that record without either side noticing. Replaying only the
    /// keys the operator touched leaves it alone.
    ///
    /// **Replaying only the touched keys is not enough for a key with two writers**, and a
    /// standing answer to a renewal is such a key, a key for each certificate. The clients page's
    /// Allow renewal takes an answer away here, and the renewal dialog writes an answer live: once
    /// an until-date has passed while this dialog was open, the next renewal is asked again and may
    /// be refused again. That answer is the newer word on the certificate, so OK keeps it rather
    /// than replaying the removal over it.
    ///
    /// **The admissions, the revocations and the profile names are the plugin's store's**,
    /// `STORE.md` §615 and §616, written by the acts the buffer holds, so nothing of them is
    /// replayed here: each act is composed again at OK against the store as it stands, a name a
    /// pairing added under the dialog among it, and a Recreate, a Delete and a revocation delete
    /// in their own write every admission they refuse, `STORE.md` §627. At OK the standing answers
    /// about a certificate no admission names any more go, against the store as written.
    /// </remarks>
    public sealed class PendingDatabase : IGateDatabase, IPendingChanges
    {
        private readonly IGateDatabase _database;
        private readonly PendingStore _store;
        private readonly PendingRecords _records;
        private readonly IGateLog _log;

        public PendingDatabase(IGateDatabase database, IGateLog log)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
            _log = log ?? NullGateLog.Instance;

            // The store is asked for on every access rather than taken once here. A database that
            // is locked and opened again while this dialog is up comes back with a different root
            // group behind the same handle, and a store captured in a constructor would go on
            // reading and writing the group that was replaced. `OpenDatabaseTargets` rebuilds its
            // own per read for the same reason.
            _store = new PendingStore(() => database.PluginCustomData);

            // And the plugin's store, whose acts are held the same way, asked for on every access
            // for the same reason.
            _records = new PendingRecords(() => database.Store);

            // **The keys with a second writer whose two versions have to be merged**: a standing
            // answer to a renewal, lifted here and given again on the renewal dialog while this
            // dialog was open, where the answer given later is the answer that stands.
            // Registered here rather than known by the buffer, because the buffer is a buffer: it
            // holds strings for a dialog and has no business knowing what any of them mean.
            _store.Reconcile(AuthorityKeys.IsRenewalRefusal, KeepTheLaterAnswer);
        }

        /// <summary>
        /// A standing answer somebody else wrote or took away while the dialog was open, kept over
        /// the tab's own edit of it.
        /// </summary>
        /// <remarks>
        /// Asked only when the key changed under the dialog. The tab only ever takes such a key
        /// away, so what changed it is a renewal answered again, a merge, or the prune that rides
        /// on a landed write, and each of those is a later word on the certificate than the press
        /// on the page.
        /// </remarks>
        private static string KeepTheLaterAnswer(string baseline, string current, string buffered)
        {
            return current;
        }

        public bool IsOpen
        {
            get { return _database.IsOpen; }
        }

        public string Name
        {
            get { return _database.Name; }
        }

        public IKeyValueStore PluginCustomData
        {
            get { return _store; }
        }

        /// <summary>The plugin's store with the acts pressed on the dialog beside it.</summary>
        public IGateStore Store
        {
            get { return _records; }
        }

        /// <summary>The live tree, read through: nothing on the tab writes to it.</summary>
        public IRuledNodes Nodes
        {
            get { return _database.Nodes; }
        }

        /// <summary>Whether anything is waiting for the dialog's OK.</summary>
        public bool Waiting
        {
            get { return (_store != null && _store.Waiting) || _records.Waiting; }
        }

        /// <summary>Whether an act about a profile name is waiting on the plugin's store.</summary>
        public bool ProfilesWaiting
        {
            get { return _records.Acts.Any(act => ProfileActs.About(act) != null); }
        }

        /// <summary>
        /// The keys the last <see cref="Apply"/> took away: see <see cref="PendingStore.Taken"/>.
        /// </summary>
        public IReadOnlyList<string> Taken
        {
            get { return _store == null ? Array.Empty<string>() : _store.Taken; }
        }

        /// <summary>
        /// Whether an act of the authority's is waiting on the plugin's store: an act holding an
        /// authority or a retirement among its records.
        /// </summary>
        public bool AuthorityWaiting
        {
            get
            {
                return _records.Acts.Any(act => act.Records.Any(record =>
                    record.Kind == StoreKinds.Authority || record.Kind == StoreKinds.Retired));
            }
        }

        /// <summary>Whether an act of this name is waiting on the plugin's store.</summary>
        public bool ActWaiting(string name)
        {
            return _records.Acts.Any(act => string.Equals(act.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// The acts the last <see cref="Apply"/> wrote into the plugin's store, in the order they
        /// were pressed; none where it wrote nothing: what the audit of the dialog's OK is
        /// written from.
        /// </summary>
        public IReadOnlyList<StoreAct> Landed { get; private set; } = Array.Empty<StoreAct>();

        /// <summary>
        /// Ignored rather than deferred. What marks the database is a change actually reaching it,
        /// which is <see cref="Apply"/>, because a database marked for a change that was then
        /// canceled is a database asking to be saved for nothing.
        /// </summary>
        /// <remarks>
        /// **What this buys is a Cancel that leaves nothing to save.** On OK, KeePass's own
        /// handler for the settings dialog marks the database changed whatever the tab did, after
        /// the tab has applied what it held, so an OK that changed nothing still asks for a save.
        /// </remarks>
        public void MarkModified()
        {
            // Deliberately nothing. What reaches the database is decided by what `Apply`
            // actually writes, not by who asked for a mark.
        }

        /// <summary>
        /// Writes everything that was waiting, and marks the database if anything landed.
        /// </summary>
        /// <param name="landed">
        /// Run once the plugin's store holds what this wrote into it, before the custom data half,
        /// and only where the store's write wrote anything: what has to outlive a defect in that
        /// half, the audit lines of what the clients page ended, is written there. What it throws
        /// reaches the caller once the custom data half has run and the buffer is let go, unless
        /// either throws too. Null for nothing.
        /// </param>
        /// <returns>True when the database now holds something it did not before.</returns>
        public bool Apply(Action landed = null)
        {
            Landed = Array.Empty<StoreAct>();
            if (!_database.IsOpen)
            {
                // The database went away while the dialog was open, which is a locked or closed
                // KeePass. There is nothing to write to and nothing to be done about it.
                _log.Warn("the " + GateName.Product + " tab had changes to apply and the database was gone");
                Discard();
                return false;
            }

            // The plugin's store first, through the writer, `STORE.md` §627: each act held to the
            // store as it stands now. An act left out is named by the store's writer, and a write
            // refused is named here with the acts it held, a write the writer's guard refuses
            // among them, and adds nothing. A save of the plugin's own is never what the guard
            // meets here: an OK pressed while such a save pumps under the dialog is handled inside
            // that pump, and the dialog's close, which runs this, is raised by the dialog's own
            // loop once the save has returned.
            var changed = false;
            var wrote = false;
            try
            {
                if (_records.Waiting)
                {
                    // Composed against the store as it stands now, on KeePass's thread with no
                    // other write between. An act with nothing left to write writes nothing, and
                    // the log names it: a certificate taken away by then, by its client or in a
                    // copy merged in, or with no record left in force under a client the store
                    // does not take away, as after a revocation held before it or a record deleted
                    // by hand; a name already where the press would put it; and an Add that a
                    // removal of its name the press did not see overtook.
                    var held = _records.Acts;
                    var composed = _records.Composed();
                    if (composed.Contains(null))
                    {
                        using (var live = _database.Store.Read())
                        {
                            for (var at = 0; at < composed.Count; at++)
                            {
                                if (composed[at] != null)
                                {
                                    continue;
                                }

                                if (ProfileActs.Overtaken(live, held[at]))
                                {
                                    _log.Warn(Described(held[at]) + ": not written at OK, since a removal of the name "
                                              + "that the press did not see stands in the database");
                                }
                                else
                                {
                                    _log.Info(Described(held[at]) + ": nothing was left to write at OK");
                                }
                            }
                        }
                    }

                    var acts = composed.Where(act => act != null).ToList();
                    var written = acts.Count == 0 ? null : _database.Store.Write(acts);

                    // The line telling what the write came to as soon as that is known, before the
                    // audit lines, the custom data half and letting the buffer go, so that a defect
                    // in any of them leaves a line telling what landed.
                    if (written != null && written.Taken)
                    {
                        wrote = written.Wrote;
                        changed = wrote;
                        Landed = wrote ? written.Acts : Array.Empty<StoreAct>();
                        if (wrote)
                        {
                            _log.Info("the plugin's store holds what the " + GateName.Product + " tab held: "
                                      + string.Join(", ", Landed.Select(Described)));
                        }
                    }
                    else if (written != null)
                    {
                        _log.Warn("what the " + GateName.Product + " tab held for the plugin's store was not written ("
                                  + string.Join(", ", acts.Select(act => act.Name)) + "): " + written.Reason
                                  + (written.LeftOut.Count == 0 ? string.Empty : "; " + string.Join("; ", written.LeftOut)));
                    }
                }

                // What has to outlive a defect in the custom data half first, as soon as the store
                // holds what was written; and that half whatever came of it, so that a defect
                // writing the lines loses nothing of the database's half of the OK, and reaches the
                // catch-all above once it has run. Where the half throws as well, what it threw is
                // the incident recorded. The caller's own steps after this run whatever the lines
                // came to as well.
                try
                {
                    if (wrote && landed != null)
                    {
                        landed();
                    }
                }
                finally
                {
                    changed = ApplyCustomData(changed, wrote);
                }
            }
            finally
            {
                // The buffer last, whatever the write, the lines and the custom data half came to,
                // a write that throws among it: the acts held and composed, the store key each
                // holds overwritten, and the reading they leave.
                _records.Discard();
            }

            return changed;
        }

        /// <summary>
        /// An act as the log names it: its name, and the profile name, or the clients and
        /// certificates its records are about, each by the first characters of its key.
        /// </summary>
        private static string Described(StoreAct act)
        {
            var name = ProfileActs.About(act);
            if (name != null)
            {
                return act.Name + " " + name;
            }

            var about = act.Records
                .Select(record => record.Member(StoreKinds.Certificate) ?? record.Member(StoreKinds.ClientMember))
                .Where(CertificateDigest.IsDigest)
                .Distinct(StringComparer.Ordinal)
                .Select(CertificateDigest.Shorten)
                .ToList();
            return about.Count == 0 ? act.Name : act.Name + " " + string.Join(" ", about);
        }

        /// <summary>
        /// The custom data half of <see cref="Apply"/>: the keys the tab held and the standing
        /// answers no admission names once the store's write landed, with the database marked
        /// where anything changed.
        /// </summary>
        /// <param name="changed">Whether the store's write changed the database.</param>
        /// <param name="wrote">Whether the store's write wrote anything.</param>
        /// <returns>True when the database now holds something it did not before.</returns>
        private bool ApplyCustomData(bool changed, bool wrote)
        {
            if (_store != null)
            {
                changed |= _store.Apply();
            }

            // A standing answer about a certificate no admission in force names any more goes
            // with this OK, against the store as written: a Recreate, a Delete and a revocation
            // delete the admissions they refuse, and an answer the renewal dialog gave while this
            // dialog was open is asked about with the rest, `STORE.md` §618.
            var live = _database.PluginCustomData;
            if (wrote && live != null)
            {
                changed |= PruneAnswers(live);
            }

            // On what was written, not on what was asked for. A create and a delete on the same
            // tab cancel out, and nothing is marked for them here. KeePass marks the database on
            // the settings dialog's OK in any case, `MarkModified` gives the reason that is not
            // undone here, so what `changed` decides beyond this is the factory's log line about
            // what landed and the prune of standing answers above.
            if (changed)
            {
                _database.MarkModified();
            }

            return changed;
        }

        /// <summary>
        /// Takes away every standing answer to a renewal about a certificate no admission in force
        /// names, against the store as the database holds it now, answering whether any went.
        /// </summary>
        /// <remarks>
        /// A store that cannot be read takes nothing away, which admits nobody, since no standing
        /// answer does, and is named in the log.
        /// </remarks>
        private bool PruneAnswers(IKeyValueStore live)
        {
            try
            {
                using (var reading = _database.Store.Read())
                {
                    if (reading.InForce == null)
                    {
                        return false;
                    }

                    return new RenewalAnswers(live, _log).Prune(StoreAdmissions.Named(reading, null)).Count > 0;
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the standing answers about certificates no admission names could not be taken away: " + error.Message);
                return false;
            }
        }

        /// <summary>Throws away everything that was waiting, which is what Cancel means.</summary>
        public void Discard()
        {
            _records.Discard();
            if (_store != null)
            {
                _store.Discard();
            }
        }
    }

    /// <summary>
    /// Combines a buffered value with one somebody else wrote while the dialog was open.
    /// </summary>
    /// <remarks>
    /// Only for a key with a second writer, which is why it is registered for a kind of key
    /// rather than applied to all of them: for every key the dialog is the only writer of, what
    /// the operator buffered is the answer and comparing it with anything would be inventing a
    /// conflict.
    /// </remarks>
    /// <param name="baseline">What the store held when the dialog's edit of this key was made.</param>
    /// <param name="current">What it holds now, which is not the baseline or this is not called.</param>
    /// <param name="buffered">What the dialog is holding, or null when the dialog removed the key.</param>
    /// <returns>What to store, or null to take the key away.</returns>
    public delegate string Reconciler(string baseline, string current, string buffered);

    /// <summary>
    /// A key value store that remembers what it was told and tells the real one later.
    /// </summary>
    /// <remarks>
    /// Reads see what has been written here first and the store underneath second, so a page
    /// showing what it just did is showing the truth about what OK would produce. A key with a
    /// second writer is read combined with what that writer put there meanwhile, as OK combines
    /// them, for the same reason.
    ///
    /// **A value that is already there is not a change.** `StringDictionaryEx.Set` stamps a fresh
    /// modification time even when the text is identical, which is how looking at a tab comes to
    /// leave a database wanting to be saved. The comparison happens at the end, against whatever
    /// the store holds then, because that is the only moment it can be right.
    /// </remarks>
    public sealed class PendingStore : IKeyValueStore
    {
        private readonly Func<IKeyValueStore> _store;
        private readonly Dictionary<string, string> _written = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The order the writes were asked for, which is the order they are replayed in.
        /// </summary>
        /// <remarks>
        /// A `Dictionary` has no order, and replaying one gave whatever it happened to hold. The
        /// caller chose an order, and this keeps it, so OK lands a change in the order the code that
        /// made it writes a live database in, which is the order that code's own reasoning about
        /// each step holds for: a recreate, for one, writes its replacement before it retires the
        /// authority it replaces.
        /// </remarks>
        private readonly List<string> _order = new List<string>();
        private readonly List<string> _removed = new List<string>();

        /// <summary>
        /// What each key held when it was first written here, which is what an edit was made
        /// against.
        /// </summary>
        /// <remarks>
        /// Without it there is no way to tell an operator's change from somebody else's: a
        /// buffered value and a live value that differ say only that they differ, and replaying
        /// the buffered one over the live one is how a write from elsewhere gets thrown away.
        /// Taken at the first write rather than when the buffer was made, because a key nobody has
        /// touched has no edit to be against.
        /// </remarks>
        private readonly Dictionary<string, string> _before = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>How each kind of key with a second writer is combined with that writer's.</summary>
        private readonly List<KeyValuePair<Func<string, bool>, Reconciler>> _kinds =
            new List<KeyValuePair<Func<string, bool>, Reconciler>>();

        /// <summary>The keys a page reads as gone, whatever the store or this buffer holds for them.</summary>
        private Func<string, bool> _hidden = key => false;

        /// <summary>
        /// Over a store that cannot be replaced underneath, which is what a test holds.
        /// </summary>
        public PendingStore(IKeyValueStore store)
            : this(Always(store))
        {
        }

        public PendingStore(Func<IKeyValueStore> store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _store = store;
        }

        private static Func<IKeyValueStore> Always(IKeyValueStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            return () => store;
        }

        /// <summary>
        /// The store underneath, as it is right now, or null when the database has none.
        /// </summary>
        private IKeyValueStore Underneath
        {
            get { return _store(); }
        }

        /// <summary>Whether anything has been said to this that the store has not been told.</summary>
        public bool Waiting
        {
            get { return _written.Count > 0 || _removed.Count > 0; }
        }

        /// <summary>
        /// The keys the last <see cref="Apply"/> took away, which the removals it asked for and
        /// found there are. Nothing is after a Cancel.
        /// </summary>
        /// <remarks>
        /// For whoever has to log what landed rather than only that something did: a standing
        /// answer to a renewal the clients page lifted is taken away as the dialog's OK lands it,
        /// and the log names it then rather than at the press.
        /// </remarks>
        public IReadOnlyList<string> Taken { get; private set; } = Array.Empty<string>();

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key) || _hidden(key))
            {
                return null;
            }

            string value;
            if (_written.TryGetValue(key, out value))
            {
                return Viewed(key, value);
            }

            if (_removed.Contains(key))
            {
                return Viewed(key, null);
            }

            var store = Underneath;
            return store == null ? null : store.Get(key);
        }

        /// <summary>
        /// What a page reading a buffered key is answered with: what OK would write for it.
        /// </summary>
        /// <remarks>
        /// The buffered value, unless the key has a second writer and that writer has been since
        /// the edit, and then the two combined, exactly as OK will combine them, so that a page
        /// shows what OK would write rather than what the dialog held alone.
        /// </remarks>
        private string Viewed(string key, string buffered)
        {
            if (MergeFor(key) == null)
            {
                return buffered;
            }

            var store = Underneath;
            return store == null ? buffered : Reconciled(key, buffered, store.Get(key));
        }

        /// <summary>How this key is combined with a second writer's, or null when it has none.</summary>
        private Reconciler MergeFor(string key)
        {
            foreach (var kind in _kinds)
            {
                if (kind.Key(key))
                {
                    return kind.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// How to combine every key of a kind with whatever somebody else writes to it while the
        /// dialog is open, for a subject kept under a key per thing it is about. Only for the keys
        /// that have a second writer.
        /// </summary>
        public void Reconcile(Func<string, bool> kind, Reconciler merge)
        {
            if (kind == null)
            {
                throw new ArgumentNullException(nameof(kind));
            }

            if (merge == null)
            {
                throw new ArgumentNullException(nameof(merge));
            }

            _kinds.Add(new KeyValuePair<Func<string, bool>, Reconciler>(kind, merge));
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }

            Remember(key);
            _removed.Remove(key);
            if (!_written.ContainsKey(key))
            {
                _order.Add(key);
            }

            _written[key] = value ?? string.Empty;
        }

        /// <summary>
        /// Takes back the writes and the removals the dialog holds for every key
        /// <paramref name="which"/> picks, as though they had never been made. Nothing is asked
        /// of the store by it.
        /// </summary>
        public void Withdraw(Func<string, bool> which)
        {
            if (which == null)
            {
                throw new ArgumentNullException(nameof(which));
            }

            foreach (var key in _written.Keys.Where(which).ToList())
            {
                _written.Remove(key);
                _order.Remove(key);
                _before.Remove(key);
            }

            foreach (var key in _removed.Where(which).ToList())
            {
                _removed.Remove(key);
                _before.Remove(key);
            }
        }

        /// <summary>
        /// Has a page read every key <paramref name="hidden"/> picks as gone, as it answers then,
        /// with nothing written or removed for it: what OK does with those keys is the caller's.
        /// </summary>
        /// <remarks>
        /// For what an act on the plugin's store takes out of the custom data, which lands only
        /// where the write at OK takes the act: buffered as removals, it landed whatever became of
        /// the act, and it took with it whatever else the dialog had written under those keys.
        /// </remarks>
        public void Hide(Func<string, bool> hidden)
        {
            if (hidden == null)
            {
                throw new ArgumentNullException(nameof(hidden));
            }

            _hidden = hidden;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            Remember(key);
            _written.Remove(key);

            if (!_removed.Contains(key))
            {
                _removed.Add(key);
            }
        }

        /// <summary>
        /// What this key held before the dialog touched it, taken once, and taken again for a key
        /// with a second writer, since an edit of that key is an edit of what the page read.
        /// </summary>
        /// <remarks>
        /// **The page reads such a key combined with what the other writer put there**, so the
        /// operator's next edit is made against that, and the baseline moves with it to what the
        /// store holds now: a merge replays the difference against the baseline, and against an
        /// older baseline it would replay what the page never showed. What the operator did
        /// before is not lost by moving it, because the value written now was read with it applied.
        /// </remarks>
        private void Remember(string key)
        {
            if (_before.ContainsKey(key) && MergeFor(key) == null)
            {
                return;
            }

            var store = Underneath;
            _before[key] = store == null ? null : store.Get(key);
        }

        public IEnumerable<string> Keys
        {
            get
            {
                var keys = new List<string>();

                var store = Underneath;
                foreach (var key in (store == null ? Array.Empty<string>() : store.Keys)
                             .Where(key => !_removed.Contains(key) && !_written.ContainsKey(key)))
                {
                    keys.Add(key);
                }

                foreach (var key in _written.Keys)
                {
                    keys.Add(key);
                }

                return keys.Where(key => !_hidden(key)).ToList();
            }
        }

        /// <summary>Tells the store, one key at a time, and forgets what it told it.</summary>
        /// <returns>True when the store now holds something it did not before.</returns>
        public bool Apply()
        {
            var changed = false;

            var store = Underneath;
            if (store == null)
            {
                // Nowhere to put any of it, which is a database that went away while the dialog
                // was open. Dropped rather than kept, because there is nothing to keep it for.
                Discard();
                return false;
            }

            foreach (var key in _order)
            {
                string value;
                if (!_written.TryGetValue(key, out value))
                {
                    continue;
                }

                changed |= Land(store, key, value);
            }

            // A removal is an edit like any other, so it is reconciled like an edit: a standing
            // answer lifted here and given again by the renewal dialog since is a later word, and
            // stays.
            var taken = new List<string>();
            foreach (var key in _removed)
            {
                var landed = Land(store, key, null);
                changed |= landed;
                if (landed)
                {
                    taken.Add(key);
                }
            }

            Discard();
            Taken = taken.AsReadOnly();
            return changed;
        }

        /// <summary>
        /// Puts one buffered key into the store, answering with whether the store now holds
        /// something it did not.
        /// </summary>
        /// <param name="buffered">What the dialog holds for it, or null when it removed the key.</param>
        private bool Land(IKeyValueStore store, string key, string buffered)
        {
            var current = store.Get(key);
            var wanted = Reconciled(key, buffered, current);

            if (wanted == null)
            {
                if (current == null)
                {
                    return false;
                }

                store.Remove(key);
                return true;
            }

            // A value that is already there is not a change. `StringDictionaryEx.Set` stamps a
            // fresh modification time even when the text is identical, which is how looking at a
            // tab comes to leave a database wanting to be saved.
            if (string.Equals(current, wanted, StringComparison.Ordinal))
            {
                return false;
            }

            store.Set(key, wanted);
            return true;
        }

        /// <summary>
        /// What to write for one key, given what the dialog holds and what is there now.
        /// </summary>
        /// <remarks>
        /// The buffered value, unless this key has a second writer and that writer has been. Both
        /// halves matter: without a reconciler there is nothing to merge with, and without a
        /// change under the dialog there is nothing to merge, and calling one anyway would put a
        /// merged document where an identical one already sits.
        /// </remarks>
        private string Reconciled(string key, string buffered, string current)
        {
            var merge = MergeFor(key);
            if (merge == null)
            {
                return buffered;
            }

            string baseline;
            _before.TryGetValue(key, out baseline);

            if (string.Equals(baseline, current, StringComparison.Ordinal))
            {
                return buffered;
            }

            return merge(baseline, current, buffered);
        }

        public void Discard()
        {
            _written.Clear();
            _order.Clear();
            _removed.Clear();
            _before.Clear();
            Taken = Array.Empty<string>();
        }
    }

    /// <summary>
    /// The renewals refused in this session as one settings dialog sees them, with the answers its
    /// clients page lifted held back until the dialog is accepted.
    /// </summary>
    /// <remarks>
    /// **Beside the buffer rather than in it**, because an answer for this session is no key in
    /// any database: it is the plugin's memory, `PAIRING.md` §312. What it shares with the buffer
    /// is the rule the whole tab follows, that nothing happens until OK. So the page reads through
    /// this, and an answer lifted here stops showing at once, and the plugin's own set is told
    /// only by <see cref="Apply"/>. Cancel is <see cref="Discard"/>, and the gate goes on refusing.
    ///
    /// One per dialog, for the reason the buffer is: two settings dialogs open at once each hold
    /// their own pending changes, and each one's OK applies what was pressed on it.
    /// </remarks>
    public sealed class PendingSessionRefusals
    {
        private readonly SessionRefusals _session;

        /// <summary>
        /// Every certificate lifted on the page, with what the lift takes back at OK.
        /// </summary>
        private readonly Dictionary<string, Lifted> _lifted = new Dictionary<string, Lifted>(StringComparer.Ordinal);

        public PendingSessionRefusals(SessionRefusals session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _session = session;
        }

        /// <summary>
        /// Whether the plugin's memory holds an answer about renewing this certificate, as OK would
        /// leave it.
        /// </summary>
        public bool Holds(string admissionKey)
        {
            var lifted = Find(admissionKey);
            return _session.Holds(admissionKey, lifted.Owed, lifted.Session);
        }

        /// <summary>
        /// The answer the plugin's memory holds about renewing this certificate that refuses at this
        /// moment, as OK would leave it: this session's answer, or an answer for longer still owed
        /// to the database, or null.
        /// </summary>
        /// <remarks>
        /// **An answer given since the press shows**, because OK leaves it standing: once the day
        /// of an owed answer the press lifted begins, the renewal dialog is raised again while the
        /// lift waits, and what the operator answers there is newer than the press.
        /// </remarks>
        public RenewalRefusal Answer(string admissionKey, DateTimeOffset now)
        {
            var lifted = Find(admissionKey);
            return _session.Answer(admissionKey, now, lifted.Owed, lifted.Session);
        }

        /// <summary>
        /// Lifts the plugin's memory of an answer about one certificate when the dialog is
        /// accepted, answering with whether there was an answer.
        /// </summary>
        /// <remarks>
        /// **What stands at the press, and nothing given after it.** An owed answer is held back
        /// from every write at once, through <see cref="SessionRefusals.Withhold"/>, because
        /// nothing in the database's buffer stands for it, and given back by
        /// <see cref="Discard"/>. A second press on the same certificate adds what was given since
        /// the first.
        /// </remarks>
        public bool Lift(string admissionKey)
        {
            if (!Holds(admissionKey))
            {
                return false;
            }

            var before = Find(admissionKey);
            var owed = _session.Withhold(admissionKey);
            if (before.Owed != null && !ReferenceEquals(before.Owed, owed))
            {
                _session.Release(before.Owed);
            }

            _lifted[admissionKey] = new Lifted(owed, before.Session || _session.Refuses(admissionKey));
            return true;
        }

        /// <summary>
        /// Lifts every answer that was lifted on the page, which is what OK means, and answers
        /// with the certificates it lifted.
        /// </summary>
        public IReadOnlyList<string> Apply()
        {
            var lifted = new List<string>(_lifted.Keys);
            foreach (var pair in _lifted)
            {
                _session.Lift(pair.Key, pair.Value.Owed, pair.Value.Session);
            }

            _lifted.Clear();
            return lifted;
        }

        /// <summary>
        /// Forgets what was lifted on the page, which is what Cancel means, and gives every owed
        /// answer it held back to the next write.
        /// </summary>
        public void Discard()
        {
            foreach (var lifted in _lifted.Values)
            {
                _session.Release(lifted.Owed);
            }

            _lifted.Clear();
        }

        /// <summary>What was lifted on the page about this certificate, or nothing.</summary>
        private Lifted Find(string admissionKey)
        {
            Lifted lifted;
            return _lifted.TryGetValue(admissionKey ?? string.Empty, out lifted) ? lifted : Lifted.Nothing;
        }

        /// <summary>What one lift takes back at OK.</summary>
        private sealed class Lifted
        {
            public static readonly Lifted Nothing = new Lifted(null, false);

            public Lifted(OwedAnswer owed, bool session)
            {
                Owed = owed;
                Session = session;
            }

            /// <summary>The owed answer held back from every write, or null for none.</summary>
            public OwedAnswer Owed { get; private set; }

            /// <summary>Whether this session's answer stood at the press.</summary>
            public bool Session { get; private set; }
        }
    }
}
