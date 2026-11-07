using KeePassGate.Vault;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The two reads a test asks of a vault to see what a write left, under the ceiling the routes
    /// read with.
    /// </summary>
    /// <remarks>
    /// `IVault` takes the ceiling with every read of an entry's strings, so that no route can read
    /// one without it. A test checking a value it wrote is not a route, and reads under the same
    /// number rather than past it, so an entry a test grew over the ceiling reads as one the gate
    /// would refuse.
    /// </remarks>
    public static class VaultReads
    {
        /// <summary>An entry and everything it holds, as `readEntry` reads it.</summary>
        public static VaultEntryDetail Read(this IVault vault, string uuid)
        {
            return vault.Read(uuid, VaultEntryDetail.MostCharacters);
        }

        /// <summary>One string's value, or null when it is not there or is over the ceiling.</summary>
        public static string Field(this IVault vault, string entryUuid, string name)
        {
            var held = vault.Field(entryUuid, name, VaultEntryDetail.MostCharacters);
            return held == null ? null : held.Value;
        }
    }
}
