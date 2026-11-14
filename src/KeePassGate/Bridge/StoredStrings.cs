using System.Text;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Reads and makes the strings KeePass keeps for an entry so that a value KeePass protects
    /// in memory is still protected there afterwards.
    /// </summary>
    /// <remarks>
    /// **`ProtectedString.ReadString` ends KeePass's protection of the value it reads.** A
    /// protected value a file was loaded with is held encrypted, as bytes. The first
    /// `ReadString` decodes it, keeps the decoded string inside the object and lets the
    /// encrypted bytes go, read off KeePass 2.61.1, so from then on the value is in KeePass's
    /// memory in the open for as long as the entry is loaded. A tag covers every value in its
    /// caller's sight, so read that way, a listing of an entry's fields left every protected
    /// value the caller could have read in the open, the values it never asked for among them.
    /// `ReadUtf8` hands back a copy and keeps nothing, so that is what a protected value is
    /// read with here, and the copy is overwritten. The digest a tag takes a value by is taken
    /// the same way, `NodeTags.DigestOf`.
    ///
    /// **A value written is handed to KeePass as bytes for the same reason.** A
    /// `ProtectedString` made from a string keeps that string, whatever its flag, and a
    /// `ProtectedString` made from bytes with the flag set encrypts a copy of them.
    ///
    /// **What this keeps and what it does not.** KeePass's protection is of memory that leaves
    /// the process, a crash dump or the page file, and what this keeps is that the gate does
    /// not undo it for a value KeePass has not itself decoded. A value that is served is a
    /// string in this process until the runtime reuses its memory, and so is the answer it is
    /// written into: neither can be overwritten. Nothing here is a control against code
    /// running as the user, which `CLAUDE.md` concedes.
    ///
    /// A string that is not protected is read with `ReadString`, which hands back the string
    /// KeePass already keeps for it and changes nothing.
    /// </remarks>
    internal static class StoredStrings
    {
        /// <summary>
        /// The value of a stored string, or the empty string for a name the entry does not hold,
        /// as KeePass's own `ReadSafe` answers.
        /// </summary>
        public static string Read(ProtectedString held)
        {
            if (held == null)
            {
                return string.Empty;
            }

            if (!held.IsProtected)
            {
                return held.ReadString();
            }

            var bytes = held.ReadUtf8();
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                MemUtil.ZeroByteArray(bytes);
            }
        }

        /// <summary>A string to store, protected in memory from the start when it is protected.</summary>
        public static ProtectedString Of(bool isProtected, string value)
        {
            var text = value ?? string.Empty;
            if (!isProtected)
            {
                return new ProtectedString(false, text);
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            try
            {
                return new ProtectedString(true, bytes);
            }
            finally
            {
                MemUtil.ZeroByteArray(bytes);
            }
        }
    }
}
