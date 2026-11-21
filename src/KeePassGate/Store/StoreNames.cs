using System;
using System.Collections.Generic;

namespace KeePassGate.Store
{
    /// <summary>
    /// The identifiers a list names, `STORE.md` §607, read once from the text that holds them:
    /// each once and ascending, so that whether the list names an identifier is a search of it.
    /// </summary>
    /// <remarks>
    /// **Read with the checked form of the version that holds the list**, `StoreChecked`, and so
    /// kept between two holds with that form, by the very objects its strings are: a list nothing
    /// changed is never read again, and a hold asks it whether it names a deletion record with no
    /// text made of either. What it answers is the text's alone, since the same objects are the
    /// same text, `StoreVersion`.
    /// </remarks>
    internal sealed class StoreNames
    {
        /// <summary>A list that names nothing.</summary>
        public static readonly StoreNames None = new StoreNames(Array.Empty<StoreId>());

        private readonly StoreId[] _names;

        private StoreNames(StoreId[] names)
        {
            _names = names;
        }

        /// <summary>The identifiers named, each once, ascending.</summary>
        public IReadOnlyList<StoreId> All
        {
            get { return _names; }
        }

        public int Count
        {
            get { return _names.Length; }
        }

        /// <summary>
        /// The identifiers a text names, as `StoreFormat.Identifiers` reads it: identifiers of 32
        /// lowercase hexadecimal characters separated by single spaces, or nothing at all; null for
        /// any other text, and for none.
        /// </summary>
        public static StoreNames Read(string text)
        {
            if (text == null)
            {
                return null;
            }

            if (text.Length == 0)
            {
                return None;
            }

            if ((text.Length + 1) % (StoreId.TextLength + 1) != 0)
            {
                return null;
            }

            var names = new StoreId[(text.Length + 1) / (StoreId.TextLength + 1)];
            var at = 0;
            for (var index = 0; index < names.Length; index++, at += StoreId.TextLength + 1)
            {
                if ((at + StoreId.TextLength < text.Length && text[at + StoreId.TextLength] != ' ')
                    || !StoreId.TryParse(text, at, out names[index]))
                {
                    return null;
                }
            }

            Array.Sort(names);
            var distinct = 1;
            for (var index = 1; index < names.Length; index++)
            {
                if (names[index] != names[distinct - 1])
                {
                    names[distinct++] = names[index];
                }
            }

            if (distinct < names.Length)
            {
                Array.Resize(ref names, distinct);
            }

            return new StoreNames(names);
        }

        /// <summary>Whether the list names this identifier.</summary>
        public bool Names(StoreId id)
        {
            return Array.BinarySearch(_names, id) >= 0;
        }
    }
}
