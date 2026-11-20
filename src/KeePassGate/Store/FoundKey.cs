using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly Dictionary<string, string> _subjects = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _names = new Dictionary<string, int>(StringComparer.Ordinal);
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

        /// <summary>
        /// The tag of a kind of `StoreKinds` under the key, as the six hexadecimal characters an
        /// identifier under it begins with.
        /// </summary>
        public string TagOf(StoreKind kind)
        {
            for (var index = 0; index < StoreKinds.All.Count; index++)
            {
                if (ReferenceEquals(StoreKinds.All[index], kind))
                {
                    return Tags[index].ToString("x6", CultureInfo.InvariantCulture);
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind), kind == null ? null : kind.Name, "a kind the table does not hold");
        }

        /// <summary>
        /// The hash of a subject under the key, as the ten hexadecimal characters of an identifier
        /// that follow its tag, `STORE.md` §609: made the first time it is asked for and kept
        /// with the key, as the tags are.
        /// </summary>
        /// <remarks>
        /// A reading asks for a subject only where a deletion record under the key carries the
        /// tag of a kind with that subject, so a store whose deletion records name nothing of it
        /// costs no digest here, and a store whose deletion records do costs a digest a subject
        /// once and none at the holds after it. A hash tells nothing of the key, and every
        /// deletion record of the subject shows it already. The subject's text is kept with its
        /// hash, as long as the key is, `STORE.md` §620.
        ///
        /// **Asked only while a reading is made**, by `StoreInForce` on the thread making it: a
        /// gate's reading holds the forms it reuses under `StoreReadings`' lock on its work, one
        /// reading at a time, and a reading beside it, or made afresh, asks only forms of its own.
        /// Nothing asks a reading handed out for a subject, so the cache has no lock.
        /// </remarks>
        public string Subject(string subject)
        {
            string hash;
            if (!_subjects.TryGetValue(subject, out hash))
            {
                hash = Hex.Of(StoreFormat.Subject(Bytes, subject));
                _subjects[subject] = hash;
            }

            return hash;
        }

        /// <summary>
        /// The tag of a profile's name under the key, `STORE.md` §609: made the first time it is
        /// asked for and kept with the key, as a subject's hash is, and asked from where a subject
        /// is.
        /// </summary>
        /// <remarks>
        /// A reading asks for the tag of a name the file knows only where a deletion record of a
        /// rule's version whose content is nowhere lies at a node's rows, so that it can tell of
        /// every name of that tag, `STORE.md` §617, and only for a name no row record shows.
        /// </remarks>
        public int NameTag(string name)
        {
            int tag;
            if (!_names.TryGetValue(name, out tag))
            {
                tag = StoreFormat.ProfileTag(Bytes, name);
                _names[name] = tag;
            }

            return tag;
        }

        /// <summary>
        /// Overwrites the key, and lets go of the subjects and the names hashed under it, which the
        /// key overwritten would not give. Its name stays, which tells nothing of it.
        /// </summary>
        public void Clear()
        {
            Array.Clear(Bytes, 0, Bytes.Length);
            _subjects.Clear();
            _names.Clear();
        }
    }
}
