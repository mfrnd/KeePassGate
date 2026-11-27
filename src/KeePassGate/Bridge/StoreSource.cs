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
    /// store, with its versions, and the identifiers its deletion records name, `STORE.md` §621;
    /// and the identifier of every group and entry, where the reading asks for them, §620.
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
        /// <param name="everyEntry">
        /// Whether the walk keeps every entry it passes as well, which a write plans against and
        /// finds its entries in, `STORE.md` §627, and the gate's readings never ask for.
        /// </param>
        public static Walk Of(PwDatabase database, PwGroup root, bool everyEntry = false)
        {
            var candidates = new List<StoreCandidate>();
            var passed = everyEntry ? new List<PwEntry>() : null;

            foreach (var entry in root.GetEntries(true))
            {
                if (passed != null)
                {
                    passed.Add(entry);
                }

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

            return new Walk(
                candidates, passed, database.DeletedObjects.Select(deleted => deleted.Uuid.UuidBytes).ToList(), () => Nodes(root));
        }

        /// <summary>The identifier of the root and of every group and entry under it, walked as it is enumerated.</summary>
        private static IEnumerable<byte[]> Nodes(PwGroup root)
        {
            yield return root.Uuid.UuidBytes;
            foreach (var group in root.GetGroups(true))
            {
                yield return group.Uuid.UuidBytes;
            }

            foreach (var entry in root.GetEntries(true))
            {
                yield return entry.Uuid.UuidBytes;
            }
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

        /// <summary>
        /// The candidates of a walk, every entry it passed, the identifiers the deletion records
        /// name, and the tree's nodes, walked when asked for.
        /// </summary>
        internal sealed class Walk
        {
            public Walk(
                IReadOnlyList<StoreCandidate> candidates,
                IReadOnlyList<PwEntry> entries,
                IReadOnlyList<byte[]> deletions,
                Func<IEnumerable<byte[]>> nodes)
            {
                Candidates = candidates;
                Entries = entries;
                Deletions = deletions;
                Nodes = nodes;
            }

            public IReadOnlyList<StoreCandidate> Candidates { get; private set; }

            /// <summary>
            /// Every entry under the root, a record or not, in the order walked, or null where the
            /// walk was not asked for them.
            /// </summary>
            public IReadOnlyList<PwEntry> Entries { get; private set; }

            /// <summary>The identifier of every entry under the root, or null where the walk was not asked for them.</summary>
            public IEnumerable<byte[]> Present
            {
                get { return Entries == null ? null : Entries.Select(entry => entry.Uuid.UuidBytes); }
            }

            public IReadOnlyList<byte[]> Deletions { get; private set; }

            /// <summary>
            /// The identifier of every group and entry under the walk's root, the root among them,
            /// walked only when called: what a reading asks for where a deletion record is left
            /// that no object the file names explains.
            /// </summary>
            /// <remarks>
            /// **Called only while the tree cannot move**, as the walk of the candidates is: by the
            /// reading made in the same hold or the same turn of KeePass's thread, and never after.
            /// The recycle bin and the plugin's own group are among them, since a rule's object
            /// names a node wherever it sits, `STORE.md` §617.
            /// </remarks>
            public Func<IEnumerable<byte[]>> Nodes { get; private set; }
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
