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

        /// <summary>Whether this particular key is one of the things waiting.</summary>
        bool WaitingOn(string key);

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
    /// **Replaying only the touched keys is not enough for a key with two writers.** The first is
    /// `KPG.Profiles`, and the tab and a pairing write it differently: the tab holds its version
    /// until OK, a pairing writes the live database and saves it, because the profile the operator
    /// just approved has to be named by the same write as the admission record. Replaying the
    /// tab's version put an older list over the newer one, which is the same destruction one key
    /// down. What is replayed for such a key is the operator's difference against what they
    /// started from, so a name somebody else added survives and a name they removed still goes.
    ///
    /// **A standing answer to a renewal is the other kind of key with two writers**, one per
    /// certificate. The clients page's Allow renewal takes one away here, and the renewal dialog
    /// writes one live: once an until-date has passed while this dialog was open, the next
    /// renewal is asked again and may be refused again. That answer is the newer word on the
    /// certificate, so OK keeps it rather than replaying the removal over it.
    ///
    /// **The admissions and the revocations are the plugin's store's**, `STORE.md` §615, written by
    /// the acts the buffer holds, so nothing of them is replayed here: a Recreate, a Delete and a
    /// revocation delete in their own write every admission they refuse, `STORE.md` §627. At OK the
    /// standing answers about a certificate no admission names any more go, against the store as
    /// written.
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

            // **The key with a second writer whose two versions have to be merged.** `KPG.Profiles`
            // is also written by a pairing, live and saved, because the profile the operator just
            // approved has to be named by the same write as the admission; replaying a snapshot
            // taken before that threw the name away and left a client holding a profile this tab
            // could no longer list or count, and that granted it nothing, since a client holds
            // only what the list names.
            //
            // Registered here rather than known by the buffer, because the buffer is a buffer: it
            // holds strings for a dialog and has no business knowing what any of them mean.
            _store.Reconcile(ProfileNames.CustomDataKey, MergeProfiles);

            // And a standing answer to a renewal, lifted here and given again on the renewal
            // dialog while this one was open: the answer given later is the one that stands.
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
        private static string KeepTheLaterAnswer(string baseline, string current, string buffered, bool landing)
        {
            return current;
        }

        /// <summary>
        /// The operator's edit of the profile list, replayed over what a pairing wrote meanwhile,
        /// and, at OK, a warning for each name it added that the full list could not take.
        /// </summary>
        /// <remarks>
        /// In the log, because the merge that counts runs at OK, when the dialog that could have
        /// said so is already closing. The page reads the same merge while the dialog is open, to
        /// show what OK will write, and says nothing then: a name left out of a list that is still
        /// being edited can yet be kept. `ProfileNames.Merge` gives the reason the names already on
        /// the list are the ones kept.
        ///
        /// **A list that changed under the dialog into a list that will not read is left alone**, the
        /// operator's ruling of 2026-11-08, and the log is told at OK. Merged, it read as naming
        /// nothing, so the edit replaced it, every name it held and the sign that it was damaged.
        /// The page then shows the damage, and replacing it takes the operator's choice: starting
        /// a new list there writes against the damaged value the page read, which OK lands only
        /// while that value is still the value stored, so this is not asked.
        /// </remarks>
        private string MergeProfiles(string baseline, string current, string buffered, bool landing)
        {
            if (!ProfileNames.Readable(current))
            {
                if (landing)
                {
                    _log.Warn("the profile list edited on the " + GateName.Product + " tab was not written: "
                              + "the database's list will not read now, and is left as it is");
                }

                return current;
            }

            IReadOnlyList<string> unkept;
            var merged = ProfileNames.Merge(baseline, current, buffered, out unkept);

            foreach (var name in unkept)
            {
                if (landing)
                {
                    LeftOut(name);
                }
                else
                {
                    // Kept for OK, which says it if the name is still left out then. A further
                    // edit of the list is made against this view, and moves the baseline with it,
                    // so OK may have nothing to merge and would otherwise say nothing.
                    _leftOut.Add(name);
                }
            }

            return merged;
        }

        /// <summary>The names the page's view of the list left out while the dialog was open.</summary>
        private readonly HashSet<string> _leftOut = new HashSet<string>(StringComparer.Ordinal);

        private void LeftOut(string name)
        {
            _leftOut.Remove(name);
            _log.Warn("the profile " + name + " added on the " + GateName.Product + " tab was not kept: "
                      + "a pairing named another while the dialog was open, and a database names "
                      + ProfileNames.Most + " profiles at most");
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

        /// <summary>Whether one key in particular is waiting.</summary>
        public bool WaitingOn(string key)
        {
            return _store != null && _store.WaitingOn(key);
        }

        /// <summary>
        /// The keys the last <see cref="Apply"/> took away: see <see cref="PendingStore.Taken"/>.
        /// </summary>
        public IReadOnlyList<string> Taken
        {
            get { return _store == null ? Array.Empty<string>() : _store.Taken; }
        }

        /// <summary>Whether an act of the authority's is waiting on the plugin's store.</summary>
        public bool AuthorityWaiting
        {
            get { return _records.Acts.Any(act => !AdmissionActs.Composes(act.Name)); }
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
        /// half, the audit lines of a revocation, is written there. What it throws reaches the
        /// caller once the custom data half has run, unless that half throws too. Null for
        /// nothing.
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
            if (_records.Waiting)
            {
                // Composed against the store as it stands now, on KeePass's thread with no other
                // write between, and let go, the store key each holds overwritten, whatever the
                // write comes to, a write that throws among it.
                IReadOnlyList<StoreAct> acts;
                StoreWritten written;
                try
                {
                    acts = _records.Composed();
                    written = _database.Store.Write(acts);
                }
                finally
                {
                    _records.Discard();
                }

                if (written.Taken)
                {
                    wrote = written.Wrote;
                    changed = wrote;
                    Landed = wrote ? written.Acts : Array.Empty<StoreAct>();
                    if (wrote)
                    {
                        // A line of its own as soon as the store holds the write, before the
                        // audit lines, so that a defect in what follows leaves a line telling
                        // what landed.
                        _log.Info("the plugin's store holds what the " + GateName.Product + " tab held: "
                                  + string.Join(", ", Landed.Select(Described)));
                    }
                }
                else
                {
                    _log.Warn("what the " + GateName.Product + " tab held for the plugin's store was not written ("
                              + string.Join(", ", acts.Select(act => act.Name)) + "): " + written.Reason
                              + (written.LeftOut.Count == 0 ? string.Empty : "; " + string.Join("; ", written.LeftOut)));
                }
            }

            // What has to outlive a defect in the custom data half first, as soon as the store
            // holds what was written; and that half whatever came of it, so that a defect writing
            // the lines loses nothing of the database's half of the OK, and reaches the catch-all
            // above once it has run. Where the half throws as well, what it threw is the incident
            // recorded. The caller's own steps after this run whatever the lines came to as well.
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

            return changed;
        }

        /// <summary>
        /// An act as the log names it: its name, and the clients and certificates its records are
        /// about, each by the first characters of its key.
        /// </summary>
        private static string Described(StoreAct act)
        {
            var about = act.Records
                .Select(record => record.Member(StoreKinds.Certificate) ?? record.Member(StoreKinds.ClientMember))
                .Where(CertificateDigest.IsDigest)
                .Distinct(StringComparer.Ordinal)
                .Select(CertificateDigest.Shorten)
                .ToList();
            return about.Count == 0 ? act.Name : act.Name + " " + string.Join(" ", about);
        }

        /// <summary>
        /// The custom data half of <see cref="Apply"/>: the keys the tab held, the standing answers
        /// no admission names once the store's write landed, and the names the page left out, with
        /// the database marked where anything changed.
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

            // A name the page's view left out that is still not on the list now it is written, which
            // the merge at OK says nothing about when the list was edited again after it went.
            if (_leftOut.Count > 0)
            {
                var written = ProfileNames.Read(_database.PluginCustomData == null
                    ? null
                    : _database.PluginCustomData.Get(ProfileNames.CustomDataKey));
                foreach (var name in new List<string>(_leftOut).Where(name => !written.Holds(name)))
                {
                    LeftOut(name);
                }

                _leftOut.Clear();
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
            _leftOut.Clear();
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
    /// Only for a key with a second writer, which is why it is registered per key rather than
    /// applied to all of them: for every key the dialog is the only writer of, what the operator
    /// buffered is the answer and comparing it with anything would be inventing a conflict.
    /// </remarks>
    /// <param name="baseline">What the store held when the dialog's edit of this key was made.</param>
    /// <param name="current">What it holds now, which is not the baseline or this is not called.</param>
    /// <param name="buffered">What the dialog is holding, or null when the dialog removed the key.</param>
    /// <param name="landing">
    /// True when the answer is about to be written at OK, false when a page is reading what OK
    /// would write. Anything a merge has to report belongs to the first.
    /// </param>
    /// <returns>What to store, or null to take the key away.</returns>
    public delegate string Reconciler(string baseline, string current, string buffered, bool landing);

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

        private readonly Dictionary<string, Reconciler> _reconcilers =
            new Dictionary<string, Reconciler>(StringComparer.Ordinal);

        /// <summary>The same for every key of a kind, asked after the keys named one by one.</summary>
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

        /// <summary>Whether one key in particular has been written here and not there.</summary>
        public bool WaitingOn(string key)
        {
            return !string.IsNullOrEmpty(key) && (_written.ContainsKey(key) || _removed.Contains(key));
        }

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
        /// the edit, and then the two combined, exactly as OK will combine them. Answering with the
        /// buffered value alone showed a profile list without the name a pairing had just added,
        /// on the same tab as a clients page listing the client that holds it, for as long as the
        /// dialog stayed open.
        /// </remarks>
        private string Viewed(string key, string buffered)
        {
            if (MergeFor(key) == null)
            {
                return buffered;
            }

            var store = Underneath;
            return store == null ? buffered : Reconciled(key, buffered, store.Get(key), false);
        }

        /// <summary>How this key is combined with a second writer's, or null when it has none.</summary>
        private Reconciler MergeFor(string key)
        {
            Reconciler merge;
            if (_reconcilers.TryGetValue(key, out merge))
            {
                return merge;
            }

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
        /// How to combine this key with whatever somebody else writes to it while the dialog is
        /// open. One per key, and only for the keys that have a second writer.
        /// </summary>
        public void Reconcile(string key, Reconciler merge)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }

            if (merge == null)
            {
                throw new ArgumentNullException(nameof(merge));
            }

            _reconcilers[key] = merge;
        }

        /// <summary>
        /// The same for every key of a kind, for a subject kept under one key per thing it is
        /// about. A key registered by name is asked about first.
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
        /// store holds now. Kept at the first edit's, a name a pairing added, shown on the page and
        /// then taken out there, stayed at OK: the merge replays the difference against the
        /// baseline, and against the old one there was nothing to take out. What the operator did
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

            // A removal is an edit like any other, so it is reconciled like one: an operator who
            // took the last name out of a list a pairing has since added to is asking for their
            // name to go, not for the pairing's to go with it.
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
            var wanted = Reconciled(key, buffered, current, true);

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
        private string Reconciled(string key, string buffered, string current, bool landing)
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

            return merge(baseline, current, buffered, landing);
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
