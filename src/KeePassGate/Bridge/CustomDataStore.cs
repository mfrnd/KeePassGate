using System;
using System.Collections.Generic;
using KeePassLib;
using KeePassLib.Collections;
using KeePassGate.Authority;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// KeePass's custom data on a database, as a plain string store.
    /// </summary>
    /// <remarks>
    /// **The database's own dictionary rather than the root group's, because of what a merge does
    /// to each.** KeePass keeps a modification time per key for `PwDatabase.CustomData` and none at
    /// all for a group's, so a merge combines the first key by key and replaces the second
    /// wholesale, taking every key from whichever replica's group was touched last. Everything
    /// this plugin stores about a database therefore lives here: the authority under one key, so
    /// its three parts cannot be merged apart, the profile list, and one admission record per
    /// client, so two hosts pairing different clients keep both.
    ///
    /// A node's access rule is not here. It belongs to its group or entry, travels with that node
    /// through a merge, a move or a deletion, and is written through the dialog's own copy.
    ///
    /// Nothing here touches the root group, which is what takes this store out of the way of the
    /// group dialog's snapshot. The maintenance dialog holds the equivalent copy of this
    /// dictionary, and `GroupDialogs` is where that is watched for. TODO(step 8): the watch
    /// arrives with the first write it guards, an admission record.
    /// </remarks>
    public sealed class CustomDataStore : IKeyValueStore
    {
        private readonly PwDatabase _database;

        public CustomDataStore(PwDatabase database)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
        }

        private StringDictionaryEx Data
        {
            get { return _database.CustomData; }
        }

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return Data.Get(key);
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }

            var wanted = value ?? string.Empty;
            if (Data.Get(key) == wanted)
            {
                // Writing back what is already there is not a change, and this dictionary stamps
                // every key it is given, so doing it would turn looking at the tab into an unsaved
                // change and would win a merge against a replica that wrote something.
                return;
            }

            Data.Set(key, wanted);
        }

        /// <summary>
        /// Removes the key if it is there. A no-op otherwise.
        /// </summary>
        /// <remarks>
        /// A plain removal, because that is what the seam means everywhere it is used. Revocation
        /// wants something stronger, since a removal is the one change a merge cannot carry, and
        /// `AdmissionStore.Remove` is where that is decided rather than here: a record taken back
        /// out because the save it belonged to never happened has nothing to outlive.
        /// </remarks>
        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            Data.Remove(key);
        }

        public IEnumerable<string> Keys
        {
            get
            {
                var keys = new List<string>();
                foreach (var pair in Data)
                {
                    keys.Add(pair.Key);
                }

                return keys;
            }
        }
    }
}
