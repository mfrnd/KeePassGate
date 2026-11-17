using System.Globalization;
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
    /// **An entry is the store's wherever it sits**, `STORE.md` §621, and that is the store's
    /// reading of the hold the lookup is made in: an entry is the store's when it sits in the
    /// plugin's group, when its identifier begins with the tag of a kind under any key the
    /// reading found, or when some version of it passes its check under any of them, which is
    /// how a copy under another identifier is told. Every key found counts here, whether it
    /// stands or was set aside, `STORE.md` §649, since an entry under a key not kept can hold an
    /// authority's private key as well. <see cref="StoreFormat"/> states what follows from
    /// telling a record by its tag.
    ///
    /// **Hiding asks nothing of what the reading makes of a record**: not whether it counts, not
    /// whether it is damage, not whether the store is stopped.
    /// </remarks>
    internal sealed class StoreNodes
    {
        private readonly StoreReading _reading;

        private StoreNodes(StoreReading reading)
        {
            _reading = reading;
        }

        /// <summary>The store's nodes as this reading of the database found them.</summary>
        public static StoreNodes Of(StoreReading reading)
        {
            return new StoreNodes(reading);
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

            return Holds(entry.ParentGroup) || _reading.Holds(entry.Uuid.UuidBytes);
        }
    }
}
