using System;
using System.Collections.Generic;

namespace KeePassGate.Store
{
    /// <summary>
    /// How the strings of a version are read: each value's length, asked before anything of it
    /// is read, and then the value.
    /// </summary>
    /// <remarks>
    /// The objects are whatever holds a value where the version came from, which in KeePass is a
    /// `ProtectedString`, and the reading never asks what they are.
    /// </remarks>
    public interface IStoreStrings
    {
        /// <summary>The value's length in characters, without the value being handed out.</summary>
        int Length(object value);

        /// <summary>The value, whole.</summary>
        string Read(object value);
    }

    /// <summary>
    /// A version of an entry as the reading is handed it: the names of its strings of the store,
    /// and the object that holds each value, `STORE.md` §621.
    /// </summary>
    /// <remarks>
    /// **The objects are what a checked form is kept by**, compared by reference, name for name,
    /// `STORE.md` §620: KeePass replaces a string's object when its text changes and never changes
    /// one in place, so the same objects are the same text, and any other object is read again.
    /// </remarks>
    public sealed class StoreVersion
    {
        private readonly string[] _names;
        private readonly object[] _values;

        /// <summary>A version of these strings, each read through <paramref name="strings"/>.</summary>
        /// <param name="names">The names of the version's strings that begin `KPG.`, in any order.</param>
        /// <param name="values">The object holding each of those strings, in the same order.</param>
        public StoreVersion(IReadOnlyList<string> names, IReadOnlyList<object> values, IStoreStrings strings)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            if (values == null || values.Count != names.Count)
            {
                throw new ArgumentException("A value is held for every name, and no more.", nameof(values));
            }

            if (strings == null)
            {
                throw new ArgumentNullException(nameof(strings));
            }

            _names = new string[names.Count];
            _values = new object[values.Count];
            for (var index = 0; index < _names.Length; index++)
            {
                _names[index] = names[index];
                _values[index] = values[index];
            }

            Strings = strings;
        }

        public IReadOnlyList<string> Names
        {
            get { return _names; }
        }

        public IReadOnlyList<object> Values
        {
            get { return _values; }
        }

        public IStoreStrings Strings { get; private set; }

        /// <summary>
        /// Whether the other version holds the very same objects under the very same names, which
        /// is the same text.
        /// </summary>
        public bool Same(StoreVersion other)
        {
            if (other == null || other._names.Length != _names.Length)
            {
                return false;
            }

            for (var index = 0; index < _names.Length; index++)
            {
                if (!string.Equals(_names[index], other._names[index], StringComparison.Ordinal)
                    || !ReferenceEquals(_values[index], other._values[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// An entry of the database that carries a string of the store in some version: its
    /// identifier, and its versions, the current first and then the history newest first,
    /// `STORE.md` §621.
    /// </summary>
    public sealed class StoreCandidate
    {
        private readonly object _title;
        private readonly IStoreStrings _titles;

        /// <summary>An entry under this identifier with these versions, and the title it holds.</summary>
        /// <param name="title">
        /// The object holding the entry's title where it holds one, read only where the title is
        /// asked for, `Title`; null for none.
        /// </param>
        /// <param name="titles">How <paramref name="title"/> is read; null where there is no title.</param>
        public StoreCandidate(byte[] identifier, IReadOnlyList<StoreVersion> versions, object title = null, IStoreStrings titles = null)
        {
            if (identifier == null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            if (versions == null)
            {
                throw new ArgumentNullException(nameof(versions));
            }

            Identifier = (byte[])identifier.Clone();
            Hex = KeePassGate.Hex.Of(identifier);
            Versions = versions;
            _title = title;
            _titles = titles;
        }

        public byte[] Identifier { get; private set; }

        /// <summary>The identifier as lowercase hexadecimal, which is how the reading files it.</summary>
        public string Hex { get; private set; }

        public IReadOnlyList<StoreVersion> Versions { get; private set; }

        /// <summary>
        /// The entry's title as it stands, read when asked, or empty where it holds none: what the KPG tab
        /// names an entry that is damage by, `STORE.md` §625. No reading asks for it.
        /// </summary>
        public string Title()
        {
            return _title == null || _titles == null ? string.Empty : _titles.Read(_title) ?? string.Empty;
        }

        /// <summary>The same entry, with its title, holding these versions.</summary>
        internal StoreCandidate WithVersions(IReadOnlyList<StoreVersion> versions)
        {
            return new StoreCandidate(Identifier, versions, _title, _titles);
        }
    }
}
