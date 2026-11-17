using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The nodes of the plugin's store in one database's tree, which the API leaves out as it
    /// leaves out the recycle bin, `STORE.md` §633.
    /// </summary>
    /// <remarks>
    /// **A group is the store's by its identifier, with everything inside it**, wherever it sits,
    /// so this needs nothing but the group and its parents: <see cref="Holds(PwGroup)"/>.
    ///
    /// **An entry is the store's wherever it sits**, `STORE.md` §621, so finding such an entry
    /// takes the whole tree: the store keys are found first, in every entry and every item of its
    /// history, the recycle bin included, and an entry is then the store's when it sits in the
    /// plugin's group, when its identifier begins with the tag of a kind under any of those keys,
    /// or when some version of it passes its check under any of them, which is how a copy under
    /// another identifier is told. Every key found counts here, whether it stands or was set aside,
    /// `STORE.md` §649, since an entry under a key not kept can hold an authority's private key
    /// as well. <see cref="StoreFormat"/> states what follows from telling a record by its tag.
    ///
    /// **Made once for a read**, as the bin is found once, and only where an entry was found or
    /// is to be listed: a lookup of a group, and of an identifier nothing holds, takes no walk.
    /// What the walk costs on KeePass's thread is S20 of `STORE.md` §646.
    /// </remarks>
    internal sealed class StoreNodes
    {
        private readonly ISet<string> _tags;
        private readonly ISet<PwEntry> _passing;

        private StoreNodes(ISet<string> tags, ISet<PwEntry> passing)
        {
            _tags = tags;
            _passing = passing;
        }

        /// <summary>The store's nodes under this root, found by walking every entry once.</summary>
        public static StoreNodes Of(PwGroup root)
        {
            var candidates = new Dictionary<PwEntry, IReadOnlyList<IReadOnlyDictionary<string, string>>>();
            var keys = new List<byte[]>();

            if (root != null)
            {
                foreach (var entry in root.GetEntries(true))
                {
                    var versions = Versions(entry);
                    if (versions.Count == 0)
                    {
                        continue;
                    }

                    candidates[entry] = versions;
                    foreach (var key in versions
                                 .Select(version => StoreFormat.KeyOf(entry.Uuid.UuidBytes, version))
                                 .Where(made => made != null && !keys.Any(found => found.SequenceEqual(made))))
                    {
                        keys.Add(key);
                    }
                }
            }

            var tags = new HashSet<string>(StringComparer.Ordinal);
            var passing = new HashSet<PwEntry>();

            try
            {
                foreach (var key in keys)
                {
                    foreach (var kind in StoreFormat.Kinds)
                    {
                        tags.Add(Hex.Of(StoreFormat.Tag(key, kind)));
                    }
                }

                foreach (var candidate in candidates.Where(read =>
                             read.Value.Any(version => keys.Any(key => StoreFormat.Passes(key, version)))))
                {
                    passing.Add(candidate.Key);
                }
            }
            finally
            {
                // Nothing here needs a key once the tags are made: overwritten, so that a key
                // read for a request is not left for the collector.
                foreach (var key in keys)
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            return new StoreNodes(tags, passing);
        }

        /// <summary>
        /// Whether this group is a group of the plugin's, or sits inside such a group.
        /// </summary>
        public static bool Holds(PwGroup group)
        {
            for (var current = group; current != null; current = current.ParentGroup)
            {
                if (StoreFormat.IsGroupIdentifier(current.Uuid.UuidBytes))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a string of this name belongs to the store, on whichever entry it is.
        /// </summary>
        /// <remarks>
        /// **Byte for byte, and as the entry's own dictionary compares names**, since that
        /// dictionary finds a stored name by a spelling its collation cannot tell from it,
        /// `KdbxVault.Holds`: a name the culture reads as beginning with the prefix is the
        /// store's as well. Asked in KeePass's culture, as every read is.
        /// </remarks>
        public static bool IsField(string name)
        {
            var collation = CultureInfo.CurrentCulture.CompareInfo;
            return StoreFormat.IsField(name) || (name != null && collation.IsPrefix(name, StoreFormat.FieldPrefix));
        }

        /// <summary>Whether this entry belongs to the store.</summary>
        public bool Holds(PwEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            var identifier = entry.Uuid.UuidBytes;
            return Holds(entry.ParentGroup)
                   || _tags.Contains(Hex.Of(identifier.Take(StoreFormat.TagLength).ToArray()))
                   || _passing.Contains(entry);
        }

        /// <summary>
        /// Each version of an entry that holds a string of the store's, as its strings of the
        /// store by name: the entry itself, then the items of its history.
        /// </summary>
        private static IReadOnlyList<IReadOnlyDictionary<string, string>> Versions(PwEntry entry)
        {
            var versions = new List<IReadOnlyDictionary<string, string>>();

            foreach (var version in new[] { entry }.Concat(entry.History))
            {
                Dictionary<string, string> strings = null;
                foreach (var pair in version.Strings)
                {
                    if (!StoreFormat.IsField(pair.Key))
                    {
                        continue;
                    }

                    strings = strings ?? new Dictionary<string, string>(StringComparer.Ordinal);
                    strings[pair.Key] = StoredStrings.Read(pair.Value);
                }

                if (strings != null)
                {
                    versions.Add(strings);
                }
            }

            return versions;
        }
    }
}
