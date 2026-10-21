using System;
using System.Collections;
using System.Collections.Generic;

namespace KeePassGate.Vault
{
    /// <summary>
    /// An entry's strings by name, enumerated in the order they were added.
    /// </summary>
    /// <remarks>
    /// **The order is load bearing, so it is this type's promise rather than a dictionary's
    /// habit.** `read` writes `fields` in the order it enumerates them, and the reference client
    /// replaces an entry's own placeholders in the order the gate lists them, which is the order
    /// KeePass keeps them in: which of two strings is replaced first decides the answer, and the
    /// vectors KeePass made pin cases that turn on it. `Dictionary` enumerates in insertion order
    /// only as an accident of how it is built today, which .NET leaves undefined.
    /// </remarks>
    public sealed class OrderedFields : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        /// <summary>Adds a string, or replaces its value where it keeps its place.</summary>
        public void Set(string name, string value)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (!_values.ContainsKey(name))
            {
                _order.Add(name);
            }

            _values[name] = value;
        }

        public int Count
        {
            get { return _order.Count; }
        }

        public IEnumerable<string> Keys
        {
            get { return _order; }
        }

        public IEnumerable<string> Values
        {
            get
            {
                foreach (var name in _order)
                {
                    yield return _values[name];
                }
            }
        }

        public string this[string key]
        {
            get { return _values[key]; }
        }

        public bool ContainsKey(string key)
        {
            return _values.ContainsKey(key);
        }

        public bool TryGetValue(string key, out string value)
        {
            return _values.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            foreach (var name in _order)
            {
                yield return new KeyValuePair<string, string>(name, _values[name]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
