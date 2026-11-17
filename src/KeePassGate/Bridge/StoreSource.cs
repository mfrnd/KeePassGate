using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Security;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// What the store's reading is handed of a database: every entry that carries a string of the
    /// store, with its versions, and the identifiers its deletion records name, `STORE.md` §621.
    /// </summary>
    /// <remarks>
    /// **Every entry of the whole tree, once**, the recycle bin and every group inside it
    /// included, since a record counts wherever it sits. An entry's strings are taken by
    /// enumeration and never by looking a name up, since KeePass's dictionary finds a name by a
    /// spelling its comparer cannot tell from it, and a candidate is an entry with a string whose
    /// name begins `KPG.` by ordinal.
    ///
    /// **An ordinary entry costs it a look at the names of its strings**, and nothing is kept of
    /// an entry that is no candidate. Enumerating KeePass's lists allocates an enumerator either
    /// way, so a query over them costs what a loop does, and the walk is written as queries.
    ///
    /// **Nothing is read of a value here.** A version hands the reading the very objects KeePass
    /// keeps the values in, which is what a checked form is kept by, and the reading asks each its
    /// length before it reads it.
    /// </remarks>
    internal static class StoreSource
    {
        /// <summary>The candidates under this root, and the identifiers this database's deletion records name.</summary>
        public static Walk Of(PwDatabase database, PwGroup root)
        {
            var candidates = new List<StoreCandidate>();

            foreach (var entry in root.GetEntries(true))
            {
                var history = entry.History;
                if (!Carries(entry) && !Carries(history))
                {
                    continue;
                }

                // The current version, then the history from its last item to its first, which is
                // newest first: KeePass adds a version at the end, `KeePassContractTests`.
                var versions = new List<StoreVersion>((int)history.UCount + 1) { Version(entry) };
                for (var index = (int)history.UCount - 1; index >= 0; index--)
                {
                    versions.Add(Version(history.GetAt((uint)index)));
                }

                candidates.Add(new StoreCandidate(entry.Uuid.UuidBytes, versions));
            }

            return new Walk(candidates, database.DeletedObjects.Select(deleted => deleted.Uuid.UuidBytes).ToList());
        }

        private static bool Carries(PwObjectList<PwEntry> history)
        {
            return history.Any(Carries);
        }

        private static bool Carries(PwEntry version)
        {
            return version.Strings.Any(pair => StoreFormat.IsField(pair.Key));
        }

        private static StoreVersion Version(PwEntry version)
        {
            var strings = version.Strings.Where(pair => StoreFormat.IsField(pair.Key)).ToList();
            return new StoreVersion(
                strings.Select(pair => pair.Key).ToList(),
                strings.Select(pair => (object)pair.Value).ToList(),
                KeePassStrings.Instance);
        }

        /// <summary>The candidates of a walk, and the identifiers the deletion records name.</summary>
        internal sealed class Walk
        {
            public Walk(IReadOnlyList<StoreCandidate> candidates, IReadOnlyList<byte[]> deletions)
            {
                Candidates = candidates;
                Deletions = deletions;
            }

            public IReadOnlyList<StoreCandidate> Candidates { get; private set; }

            public IReadOnlyList<byte[]> Deletions { get; private set; }
        }

        /// <summary>A value as KeePass keeps it, its length asked first.</summary>
        private sealed class KeePassStrings : IStoreStrings
        {
            public static readonly KeePassStrings Instance = new KeePassStrings();

            public int Length(object value)
            {
                var held = value as ProtectedString;
                return held == null ? 0 : held.Length;
            }

            /// <summary>
            /// Read so that a value KeePass protects in memory stays protected there, as every
            /// read of a string is, `StoredStrings`.
            /// </summary>
            public string Read(object value)
            {
                return StoredStrings.Read(value as ProtectedString);
            }
        }
    }
}
