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
    /// A string of an entry as a view holds it, made from what reads its value whole.
    /// </summary>
    /// <remarks>
    /// A view hands a value out a piece at a time, since the store reads a value so. A test
    /// that is not about the pieces has a value, or something that counts how often the value
    /// is asked for, and this hands that out as a single piece, and as none where it is null
    /// or of no characters.
    /// </remarks>
    public static class ViewFields
    {
        /// <summary>A string whose value is what <paramref name="read"/> answers.</summary>
        public static VaultViewField Of(
            string name,
            bool isProtected,
            VaultSight sight,
            System.Func<string> read,
            bool isListed = false,
            System.Func<byte[]> digest = null)
        {
            return new VaultViewField(name, isProtected, sight, read == null ? null : Whole(read), isListed, digest);
        }

        /// <summary>What hands out, as a single piece, the value something answers whole.</summary>
        public static System.Action<System.Action<string>> Whole(System.Func<string> read)
        {
            return piece =>
            {
                var value = read();
                if (!string.IsNullOrEmpty(value))
                {
                    piece(value);
                }
            };
        }
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

        /// <summary>One string's value, however long it is, or null when it is not there.</summary>
        public static string Field(this IVault vault, string entryUuid, string name)
        {
            var view = vault.Whole(entryUuid);
            var field = view == null ? null : view.Field(name);
            return field == null ? null : field.Read();
        }

        /// <summary>
        /// A string's value whole, its pieces joined in the order they came, or null for a
        /// value that is withheld and hands out nothing.
        /// </summary>
        /// <remarks>
        /// The view is asked whether or not it withholds the value, so that a view which kept
        /// what reads a withheld value hands that value out here, and a test that counts its
        /// reads counts that read.
        /// </remarks>
        public static string Read(this VaultViewField field)
        {
            var joined = new System.Text.StringBuilder();
            field.Read(piece => joined.Append(piece));
            return field.IsWithheld && joined.Length == 0 ? null : joined.ToString();
        }

        /// <summary>
        /// Every byte an attachment hands out, joined in the order its pieces came: nothing for
        /// an attachment that is withheld.
        /// </summary>
        /// <remarks>
        /// Each piece is copied where it is handed over, since the array it comes in is the
        /// store's: KeePass's own buffer for an attachment held in the open, and a copy that
        /// is overwritten once the read is over for a protected attachment.
        /// </remarks>
        public static byte[] Bytes(this VaultViewAttachment attachment)
        {
            using (var joined = new System.IO.MemoryStream())
            {
                attachment.Read(joined.Write);
                return joined.ToArray();
            }
        }
    }
}
