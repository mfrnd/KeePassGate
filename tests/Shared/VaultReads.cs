using KeePassGate.Vault;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The sights a test takes a view with when it is not testing the rules.
    /// </summary>
    /// <remarks>
    /// Here and not in the plugin, where nothing builds a sight but the handlers, from what the
    /// caller's rules open: a sight opening everything is something only a test has a use for.
    /// </remarks>
    public static class Sights
    {
        /// <summary>
        /// Every string and every attachment, which is what a rule holding `read` with both
        /// switches opens.
        /// </summary>
        public static readonly VaultSight Whole = new VaultSight(true, true, VaultAttachmentSight.Read);
    }

    /// <summary>
    /// The reads a test asks of a vault to see what a write left: the whole of an entry, with
    /// nothing withheld.
    /// </summary>
    /// <remarks>
    /// `IVault` hands out an entry only as a view, filtered by what the caller asking may read, so
    /// that no route can read an entry without the rules being asked. A test checking a value it
    /// wrote is not a route and has no rules, so it asks with <see cref="Sights.Whole"/>. What
    /// a view withholds is tested where the rules are: through the router, and on the view itself.
    /// </remarks>
    public static class VaultReads
    {
        /// <summary>An entry and everything it holds, or null when it is not there.</summary>
        public static VaultEntryView Whole(this IVault vault, string uuid)
        {
            return vault.View(uuid, found => Sights.Whole);
        }

        /// <summary>The tag of the whole of an entry, or null when it is not there.</summary>
        public static string EntryTag(this IVault vault, string uuid)
        {
            var view = vault.Whole(uuid);
            return view == null ? null : view.Tag;
        }

        /// <summary>
        /// One string's value, or null when it is not there or comes, with its name, to more than
        /// the routes hand out.
        /// </summary>
        public static string Field(this IVault vault, string entryUuid, string name)
        {
            var view = vault.Whole(entryUuid);
            var field = view == null ? null : view.Field(name);
            return field == null || field.Characters > VaultEntryView.MostCharacters ? null : field.Value;
        }
    }
}
