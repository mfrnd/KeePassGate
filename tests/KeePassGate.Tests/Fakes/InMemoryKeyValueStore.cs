using System;
using System.Collections.Generic;
using KeePassGate.Authority;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Custom data without KeePass. It can also be told to behave like the store really does when
    /// a dialog is holding a copy of it: writes that appear to work and are not there afterwards.
    /// </summary>
    internal sealed class InMemoryKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>When true, every write is accepted and discarded, as a stale snapshot does.</summary>
        public bool SwallowWrites { get; set; }

        /// <summary>
        /// What every write actually stores, when that is not what was written. Null leaves writes
        /// alone.
        /// </summary>
        /// <remarks>
        /// The other half of what a read back catches. Swallowing a write leaves the key absent,
        /// which is the stale snapshot; this leaves the key there and unreadable, which is a write
        /// that landed and cannot be used. The two need different messages, so a fake that can only
        /// produce the first cannot show that.
        /// </remarks>
        public string DamageWritesTo { get; set; }

        public Exception ThrowOnWrite { get; set; }

        public int Count
        {
            get { return _values.Count; }
        }

        public string Get(string key)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : null;
        }

        public void Set(string key, string value)
        {

            if (ThrowOnWrite != null)
            {
                throw ThrowOnWrite;
            }

            if (SwallowWrites)
            {
                return;
            }

            _values[key] = DamageWritesTo ?? value;
        }

        public void Remove(string key)
        {
            // A removal is a write, so a store that swallows writes swallows this too. The
            // caller that has to notice is revocation, which reports what it believes landed.
            if (SwallowWrites)
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
