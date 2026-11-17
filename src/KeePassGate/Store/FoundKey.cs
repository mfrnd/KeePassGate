using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// A store key a version gives, `STORE.md` §621, with its name, `STORE.md` §649.
    /// </summary>
    /// <remarks>
    /// **The name is what keys are compared and filed by**: it is a digest of the key, made once
    /// when the key is read, so two carriers of one key give one name, and a checked form keeps
    /// its answers under the name and holds no copy of the key it was asked about. The bytes are
    /// held by the form whose content gave them, and <see cref="Clear"/> overwrites them when
    /// that form is let go.
    /// </remarks>
    internal sealed class FoundKey
    {
        private int[] _tags;

        public FoundKey(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            Bytes = bytes;
            Name = StoreFormat.KeyName(bytes);
        }

        public byte[] Bytes { get; private set; }

        /// <summary>`StoreFormat.KeyName` of the key, as 64 lowercase hexadecimal characters.</summary>
        public string Name { get; private set; }

        /// <summary>
        /// The tag of every kind of `StoreKinds` under the key, each its three bytes as a number,
        /// made the first time they are asked for and kept with the key.
        /// </summary>
        /// <remarks>
        /// Kept, so that a reading pays for them once a key and not once a hold: a file that brings
        /// a key for each of its entries would otherwise cost every hold a digest for each kind
        /// under each key. They tell nothing of the key, being what every identifier under it
        /// already shows.
        /// </remarks>
        public IReadOnlyList<int> Tags
        {
            get
            {
                if (_tags == null)
                {
                    _tags = StoreKinds.All.Select(kind => StoreFormat.TagNumber(StoreFormat.Tag(Bytes, kind.Name))).ToArray();
                }

                return _tags;
            }
        }

        /// <summary>Overwrites the key. Its name stays, which tells nothing of it.</summary>
        public void Clear()
        {
            Array.Clear(Bytes, 0, Bytes.Length);
        }
    }
}
