using System;
using System.Collections.Generic;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;

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
    /// TODO(step 8): that page is what the pairing step puts on the tab; nothing asks this yet.
    /// </remarks>
    public interface IPendingChanges
    {
        /// <summary>Whether anything at all is waiting for the dialog's OK.</summary>
        bool Waiting { get; }

        /// <summary>Whether this particular key is one of the things waiting.</summary>
        bool WaitingOn(string key);
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
    /// buffer, and the dialog's result decides whether the buffer is replayed or dropped.
    ///
    /// **Replayed key by key, never wholesale.** Writing a whole snapshot back would be this
    /// plugin doing to itself what KeePass's group dialog does to it: anything written to the live
    /// database while this dialog was open would be destroyed without either side noticing.
    /// Replaying only the keys the operator touched leaves it alone.
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
        }

        public bool IsOpen
        {
            get { return _database.IsOpen; }
        }

        public IKeyValueStore PluginCustomData
        {
            get { return _store; }
        }

        /// <summary>Whether anything is waiting for the dialog's OK.</summary>
        public bool Waiting
        {
            get
            {
                return _store != null && _store.Waiting;
            }
        }

        /// <summary>Whether one key in particular is waiting.</summary>
        public bool WaitingOn(string key)
        {
            return _store != null && _store.WaitingOn(key);
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

            // On what was written, not on what was asked for. A create and a delete on the same
            // tab cancel out, leaving a mark requested and nothing to save: KeePass then demanded
            // save for a file whose bytes had not changed, which is the thing this class exists to
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
            if (_store != null)
            {
                _store.Discard();
            }

        }
    }

    /// <summary>
    /// A key value store that remembers what it was told and tells the real one later.
    /// </summary>
    /// <remarks>
    /// Reads see what has been written here first and the store underneath second, so a page
    /// showing what it just did is showing the truth about what OK would produce.
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

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string value;
            if (_written.TryGetValue(key, out value))
            {
                return value;
            }

            if (_removed.Contains(key))
            {
                return null;
            }

            var store = Underneath;
            return store == null ? null : store.Get(key);
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }

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

            _written.Remove(key);

            if (!_removed.Contains(key))
            {
                _removed.Add(key);
            }
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

                if (string.Equals(store.Get(key), value, StringComparison.Ordinal))
                {
                    continue;
                }

                store.Set(key, value);
                changed = true;
            }

            foreach (var key in _removed)
            {
                if (store.Get(key) == null)
                {
                    continue;
                }

                store.Remove(key);
                changed = true;
            }

            Discard();
            return changed;
        }

        public void Discard()
        {
            _written.Clear();
            _order.Clear();
            _removed.Clear();
        }
    }

}
