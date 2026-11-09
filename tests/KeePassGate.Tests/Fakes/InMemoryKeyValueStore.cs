using System;
using System.Collections.Generic;
using KeePassGate.Authority;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Custom data without KeePass, held to `CustomDataStore`'s rules. It can also be told to fail
    /// in ways neither real store does, for the read backs and refusals that are there in case a
    /// real store ever does.
    /// </summary>
    /// <remarks>
    /// **The rules are the real store's**: a null value is stored as an empty string, an empty key
    /// is refused on a write and finds nothing on a read, a write of what is already there changes
    /// nothing, and the keys are kept as KeePass's dictionary keeps them, sorted under the current
    /// culture, so a key that culture cannot tell from another key is the same key here too.
    ///
    /// **The failures are a store's own, never a dialog's.** Neither real store loses a write:
    /// KeePass's dictionary keeps every value it is handed, and the tab's buffer records every
    /// change. A dialog holding a stale copy puts it back only on its own OK, after any read back
    /// has run, so no read back can see that, and `GroupDialogs` refusing the write while such a
    /// dialog is open is what answers it. What these stand for is a store that loses, changes or
    /// refuses a write for reasons of its own.
    /// </remarks>
    internal sealed class InMemoryKeyValueStore : IKeyValueStore
    {
        private readonly SortedDictionary<string, string> _values = new SortedDictionary<string, string>();

        /// <summary>
        /// When true, every write is accepted and discarded, a store keeping nothing.
        /// </summary>
        public bool SwallowWrites { get; set; }

        /// <summary>
        /// What every write actually stores, when that is not what was written. Null leaves writes
        /// alone.
        /// </summary>
        /// <remarks>
        /// The other half of what a read back catches. Swallowing a write leaves the key absent;
        /// this leaves the key there and unreadable, which is a write that landed and cannot be
        /// used. The two need different messages, so a fake that can only produce the first
        /// cannot show that.
        /// </remarks>
        public string DamageWritesTo { get; set; }

        public Exception ThrowOnWrite { get; set; }

        /// <summary>
        /// What a read throws, for the case where the database itself refuses. Nothing above this
        /// can produce that, and a tab that keeps showing what it read last time is exactly what
        /// nobody notices.
        /// </summary>
        public Exception ThrowOnRead { get; set; }

        public int Count
        {
            get { return _values.Count; }
        }

        public string Get(string key)
        {
            if (ThrowOnRead != null)
            {
                throw ThrowOnRead;
            }

            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string value;
            return _values.TryGetValue(key, out value) ? value : null;
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }

            if (ThrowOnWrite != null)
            {
                throw ThrowOnWrite;
            }

            if (SwallowWrites)
            {
                return;
            }

            var wanted = value ?? string.Empty;
            string held;
            if (_values.TryGetValue(key, out held) && held == wanted && DamageWritesTo == null)
            {
                return;
            }

            _values[key] = DamageWritesTo ?? wanted;

            if (WhenSet != null)
            {
                WhenSet(key);
            }
        }

        /// <summary>
        /// Called with the key of each write once it has landed, for a test whose store has to
        /// start refusing part way through a change.
        /// </summary>
        public Action<string> WhenSet { get; set; }

        public void Remove(string key)
        {
            // A removal is a write, so a store that swallows writes swallows this too. The
            // caller that has to notice is revocation, which reports what it believes landed.
            if (SwallowWrites || string.IsNullOrEmpty(key))
            {
                return;
            }

            _values.Remove(key);
        }

        /// <summary>
        /// Makes enumeration alone fail, which is the shape of the race this guards against: a
        /// lookup is a tree descent and survives, an enumeration is invalidated by a concurrent
        /// write. A fake that refuses everything cannot tell the two apart.
        /// </summary>
        public bool ThrowOnList { get; set; }

        public IEnumerable<string> Keys
        {
            get
            {
                if (ThrowOnList)
                {
                    throw new InvalidOperationException("the collection was modified");
                }

                return new List<string>(_values.Keys);
            }
        }

        /// <summary>Puts a value in behind the store's back, for damage that has to be read.</summary>
        public void Plant(string key, string value)
        {
            _values[key] = value;
        }
    }
}
