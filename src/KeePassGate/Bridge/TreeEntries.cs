using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The entries of a database's tree by their identifiers, made from the walk of the reading a
    /// write of the plugin's own is planned against, `STORE.md` §627: what asking which entries
    /// the write touches, deleting, setting back and protecting again find their entries in.
    /// </summary>
    /// <remarks>
    /// **The walk of every entry that finds a write's entries is its reading's**, and this is
    /// made from the entries that walk passed, with no text made of any identifier, so a write
    /// looks each identifier up rather than walking the tree for each step.
    ///
    /// **Made for a single write, inside the turn that read the tree**, where nothing else changes
    /// it, and never changed by the write: what the write looks up is what a walk at each of its
    /// steps would find, as long as the write adds no record under an identifier an entry of the
    /// tree holds, deletes, sets back and protects again no record it adds, and sets back and
    /// protects again no record it deletes. A write that does any of those is refused before
    /// anything is changed, <see cref="Unsound"/>.
    /// </remarks>
    internal sealed class TreeEntries
    {
        private static readonly IReadOnlyList<PwEntry> None = Array.Empty<PwEntry>();

        /// <summary>The first entry walked under each identifier.</summary>
        private readonly Dictionary<StoreId, PwEntry> _first;

        /// <summary>Every entry under an identifier that several entries of the tree hold, in the order walked.</summary>
        private readonly Dictionary<StoreId, List<PwEntry>> _several = new Dictionary<StoreId, List<PwEntry>>();

        /// <summary>The entries of a walk, in the order walked.</summary>
        public TreeEntries(IReadOnlyList<PwEntry> walked)
        {
            if (walked == null)
            {
                throw new ArgumentNullException(nameof(walked));
            }

            _first = new Dictionary<StoreId, PwEntry>(walked.Count);
            foreach (var entry in walked)
            {
                StoreId identifier;
                if (!StoreId.TryOf(entry.Uuid.UuidBytes, out identifier))
                {
                    continue;
                }

                PwEntry first;
                if (!_first.TryGetValue(identifier, out first))
                {
                    _first.Add(identifier, entry);
                    continue;
                }

                List<PwEntry> under;
                if (!_several.TryGetValue(identifier, out under))
                {
                    under = new List<PwEntry> { first };
                    _several.Add(identifier, under);
                }

                under.Add(entry);
            }
        }

        /// <summary>
        /// Every entry under the identifier this lowercase hexadecimal names, in the order walked;
        /// none for a text that names no identifier so written.
        /// </summary>
        public IReadOnlyList<PwEntry> Under(string hex)
        {
            StoreId identifier;
            PwEntry first;
            if (!StoreId.TryParse(hex, out identifier) || !_first.TryGetValue(identifier, out first))
            {
                return None;
            }

            List<PwEntry> under;
            return _several.TryGetValue(identifier, out under) ? under : new[] { first };
        }

        /// <summary>
        /// The first entry walked under the identifier this lowercase hexadecimal names, as
        /// KeePass's own search finds an entry, or null.
        /// </summary>
        public PwEntry First(string hex)
        {
            StoreId identifier;
            PwEntry first;
            return StoreId.TryParse(hex, out identifier) && _first.TryGetValue(identifier, out first) ? first : null;
        }

        /// <summary>
        /// Why a write with these records asks of this map what a walk of the tree at its steps
        /// would answer otherwise, or null where it does not: a record added under an identifier
        /// an entry of the tree holds, a record deleted, set back or protected again that the
        /// write adds, or a record set back or protected again that the write deletes. A walk
        /// after the additions would find what they added, and a walk after the deletions would
        /// not find what they deleted; this map finds neither.
        /// </summary>
        /// <param name="added">The identifiers of the records the write adds, as lowercase hexadecimal.</param>
        /// <param name="deleted">The identifiers of the records it deletes.</param>
        /// <param name="setBack">The identifiers of the records it sets back.</param>
        /// <param name="protectedAgain">The identifiers of the records whose content it protects again where that went.</param>
        public string Unsound(IEnumerable<string> added, IEnumerable<string> deleted, IEnumerable<string> setBack, IEnumerable<string> protectedAgain)
        {
            var adding = new HashSet<string>(added, StringComparer.Ordinal);
            if (adding.Any(hex => First(hex) != null))
            {
                return "it adds a record under an identifier an entry of the tree holds";
            }

            var deleting = new HashSet<string>(deleted, StringComparer.Ordinal);
            if (deleting.Overlaps(adding))
            {
                return "it deletes a record it adds";
            }

            if (setBack.Any(hex => adding.Contains(hex) || deleting.Contains(hex)))
            {
                return "it sets back a record it adds or deletes";
            }

            if (protectedAgain.Any(hex => adding.Contains(hex) || deleting.Contains(hex)))
            {
                return "it protects again a record it adds or deletes";
            }

            return null;
        }
    }
}
