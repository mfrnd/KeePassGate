using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

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
        /// Whether any key of a kind is waiting, for a subject kept under more than one key: the
        /// authorities and their retirements are one per authority.
        /// </summary>
        bool WaitingOn(Func<string, bool> kind);
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
    /// One case survives all of that and is worth naming rather than hiding. An operator who
    /// recreates or deletes the authority, while a pairing lands, leaves that pairing's record
    /// behind, because the record did not exist when the page read the database. It is inert: its
    /// certificate was issued by an authority that is gone, so it chains to nothing and is
    /// refused. That is the direction this design fails in everywhere else too.
    /// </remarks>
    public sealed class PendingDatabase : IGateDatabase, IPendingChanges
    {
        private readonly IGateDatabase _database;
        private readonly PendingStore _store;
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

            // **The key with a second writer whose two versions have to be merged.** A record
            // or a revocation the tab touches can also be written while the dialog is up, by a
            // client revoking itself or by the prune of expired records every landed write rides
            // on, and replaying writes it again as the operator left it: the same revocation, or
            // one the next prune tidies away. `KPG.Profiles` is also written by a pairing, live
            // and saved, because the profile the operator just approved has to be named by the
            // same write as the admission record; replaying a snapshot taken before that threw the
            // name away and left a client holding a profile this tab could no longer list or
            // count, and that granted it nothing, since a client holds only what the list names.
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
        /// being edited can yet be kept. `ProfileNames.Merge` says why the names already on the
        /// list are the ones kept.
        /// </remarks>
        private string MergeProfiles(string baseline, string current, string buffered, bool landing)
        {
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

        /// <summary>The live tree, read through: nothing on the tab writes to it.</summary>
        public IRuledNodes Nodes
        {
            get { return _database.Nodes; }
        }

        /// <summary>Whether anything is waiting for the dialog's OK.</summary>
        public bool Waiting
        {
            get { return _store != null && _store.Waiting; }
        }

        /// <summary>Whether one key in particular is waiting.</summary>
        public bool WaitingOn(string key)
        {
            return _store != null && _store.WaitingOn(key);
        }

        /// <summary>Whether any key of a kind is waiting.</summary>
        public bool WaitingOn(Func<string, bool> kind)
        {
            return _store != null && _store.WaitingOn(kind);
        }

        /// <summary>
        /// Ignored rather than deferred. What marks the database is a change actually reaching it,
        /// which
        /// is <see cref="Apply"/>, because a database marked for a change that was then canceled
        /// is a database asking to be saved for nothing.
        /// </summary>
        public void MarkModified()
        {
            // Deliberately nothing. What reaches the database is decided by what `Apply`
            // actually writes, not by who asked for a mark.
        }

        /// <summary>
        /// Writes everything that was waiting, and marks the database if anything landed.
        /// </summary>
        /// <returns>True when the database now holds something it did not before.</returns>
        public bool Apply()
        {
            if (!_database.IsOpen)
            {
                // The database went away while the dialog was open, which is a locked or closed
                // KeePass. There is nothing to write to and nothing to be done about it.
                _log.Warn("the " + GateName.Product + " tab had changes to apply and the database was gone");
                Discard();
                return false;
            }

            var changed = false;

            if (_store != null)
            {
                changed |= _store.Apply();
            }

            // A standing answer about a certificate this OK revoked may have landed after the
            // press, when the prune that rode on it had already looked and found nothing. Asked
            // again over what was written, so the order of the two does not decide whether an
            // answer about a certificate no record admits stays in the file.
            var live = _database.PluginCustomData;
            if (changed && live != null)
            {
                changed |= new AdmissionStore(live, _log).PruneRefusals().Count > 0;
            }

            // A name the page's view left out that is still not on the list now it is written, which
            // the merge at OK says nothing about when the list was edited again after it went.
            if (_leftOut.Count > 0)
            {
                var written = ProfileNames.Read(_database.PluginCustomData == null
                    ? null
                    : _database.PluginCustomData.Get(ProfileNames.CustomDataKey));
                foreach (var name in new List<string>(_leftOut))
                {
                    if (!written.Holds(name))
                    {
                        LeftOut(name);
                    }
                }

                _leftOut.Clear();
            }

            // On what was written, not on what was asked for. A create and a delete on the same
            // tab cancel out, leaving a mark requested and nothing to save: KeePass then demanded
            // a save for a file whose bytes had not changed, which is what this class exists to
            // stop happening.
            if (changed)
            {
                _database.MarkModified();
            }

            return changed;
        }

        /// <summary>Throws away everything that was waiting, which is what Cancel means.</summary>
        public void Discard()
        {
            _leftOut.Clear();
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
        /// A `Dictionary` has no order, and replaying one gave whatever it happened to hold:
        /// measured, that inverted the sequence the authority is written in, where the key that
        /// makes it usable goes last so a half-written one never reads as complete. The caller
        /// chose an order; this keeps it.
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

        /// <summary>Whether one key in particular has been written here and not there.</summary>
        public bool WaitingOn(string key)
        {
            return !string.IsNullOrEmpty(key) && (_written.ContainsKey(key) || _removed.Contains(key));
        }

        /// <summary>Whether any key of a kind has been written or removed here and not there.</summary>
        public bool WaitingOn(Func<string, bool> kind)
        {
            if (kind == null)
            {
                return false;
            }

            foreach (var key in _written.Keys)
            {
                if (kind(key))
                {
                    return true;
                }
            }

            foreach (var key in _removed)
            {
                if (kind(key))
                {
                    return true;
                }
            }

            return false;
        }

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
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
                foreach (var key in store == null ? Array.Empty<string>() : store.Keys)
                {
                    if (!_removed.Contains(key) && !_written.ContainsKey(key))
                    {
                        keys.Add(key);
                    }
                }

                foreach (var key in _written.Keys)
                {
                    keys.Add(key);
                }

                return keys;
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
            foreach (var key in _removed)
            {
                changed |= Land(store, key, null);
            }

            Discard();
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
        private readonly HashSet<string> _lifted = new HashSet<string>(StringComparer.Ordinal);

        public PendingSessionRefusals(SessionRefusals session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _session = session;
        }

        /// <summary>Whether renewals of this certificate are refused, as OK would leave them.</summary>
        public bool Refuses(string admissionKey)
        {
            return Answer(admissionKey) != null;
        }

        /// <summary>
        /// The answer the plugin's memory holds about renewing this certificate, as OK would leave
        /// it: this session's, or one for longer still owed to the database, or null.
        /// </summary>
        public RenewalRefusal Answer(string admissionKey)
        {
            return _lifted.Contains(admissionKey ?? string.Empty) ? null : _session.Answer(admissionKey);
        }

        /// <summary>
        /// Lifts this session's answer about one certificate when the dialog is accepted, answering
        /// with whether there was one.
        /// </summary>
        public bool Lift(string admissionKey)
        {
            if (!Refuses(admissionKey))
            {
                return false;
            }

            _lifted.Add(admissionKey);
            return true;
        }

        /// <summary>
        /// Lifts every answer that was lifted on the page, which is what OK means, and answers
        /// with the certificates it lifted.
        /// </summary>
        public IReadOnlyList<string> Apply()
        {
            var lifted = new List<string>(_lifted);
            foreach (var key in lifted)
            {
                _session.Lift(key);
            }

            _lifted.Clear();
            return lifted;
        }

        /// <summary>Forgets what was lifted on the page, which is what Cancel means.</summary>
        public void Discard()
        {
            _lifted.Clear();
        }
    }
}
