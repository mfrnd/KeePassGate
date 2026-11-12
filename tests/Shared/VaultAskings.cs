using System.Collections.Generic;
using KeePassGate.Vault;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The questions a test hands a vault's change when the vault is what is being tested and
    /// the rules are not.
    /// </summary>
    /// <remarks>
    /// `IVault` takes a <see cref="VaultAsking"/> with every change, so that no route can change a
    /// node without the rules being asked inside the change's own turn. A test arranging a
    /// database, or pinning what a change does to a tree, is not a route and has no rules, so it
    /// hands over questions that are answered the same way whatever is found. What a change does
    /// when the rules answer otherwise is tested where the rules are: through the router.
    /// </remarks>
    public static class VaultAskings
    {
        private static bool Yes(VaultFound found)
        {
            return true;
        }

        private static VaultSight Whole(VaultFound found)
        {
            return Sights.Whole;
        }

        /// <summary>
        /// A caller the rules permit, that reads the whole of what it finds, asks no condition
        /// and asks for no tag in its answer.
        /// </summary>
        public static VaultAsking Free
        {
            get { return new VaultAsking(Yes, Whole, null, false); }
        }

        /// <summary>
        /// A caller the rules permit to change what it may not read: no condition, no tag, and
        /// nothing it names is its to see.
        /// </summary>
        public static VaultAsking Blind
        {
            get { return new VaultAsking(Yes, null, null, false); }
        }

        /// <summary>
        /// A caller the rules permit and that reads the whole of what it finds, so its tag is
        /// the tag of the whole node, asking this condition of the node as the change finds it.
        /// </summary>
        /// <param name="answers">Whether the answer carries the tag the change leaves.</param>
        public static VaultAsking Tagged(VaultPrecondition precondition, bool answers)
        {
            return new VaultAsking(Yes, Whole, precondition, answers);
        }

        /// <summary>A group made for a test's arrangement, by a caller the rules permit.</summary>
        public static VaultWrite CreateGroup(this IVault vault, string parentUuid, string name, string notes)
        {
            return vault.CreateGroup(parentUuid, name, notes, Free);
        }

        /// <summary>An entry made for a test's arrangement, by a caller the rules permit.</summary>
        public static VaultWrite CreateEntry(
            this IVault vault, string groupUuid, IReadOnlyDictionary<string, string> fields)
        {
            return vault.CreateEntry(groupUuid, fields, Free);
        }

        /// <summary>The same, with the expiry the entry is to carry.</summary>
        public static VaultWrite CreateEntry(
            this IVault vault, string groupUuid, IReadOnlyDictionary<string, string> fields, VaultExpiry expiry)
        {
            return vault.CreateEntry(groupUuid, fields, Free, expiry);
        }
    }
}
