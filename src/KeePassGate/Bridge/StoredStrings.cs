using System;
using System.Reflection;
using System.Text;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Reads and makes the strings KeePass keeps for an entry so that a value KeePass protects
    /// in memory is still protected there afterwards, and reads a value a piece at a time from
    /// where KeePass keeps it, as an attachment is read.
    /// </summary>
    /// <remarks>
    /// **`ProtectedString.ReadString` ends KeePass's protection of the value it reads.** A
    /// protected value a file was loaded with is held encrypted, as bytes. The first
    /// `ReadString` decodes it, keeps the decoded string inside the object and lets the
    /// encrypted bytes go, read off KeePass 2.61.1, so from then on the value is in KeePass's
    /// memory in the open for as long as the entry is loaded. A tag covers every value in its
    /// caller's sight, so read that way, a listing of an entry's fields left every protected
    /// value the caller could have read in the open, the values it never asked for among them.
    /// So the gate never asks KeePass for a protected value as a string.
    ///
    /// **KeePass keeps a value as a string or as bytes, and it is read from whichever it is.**
    /// The private fields `m_strPlainText` and `m_pbUtf8`, read off KeePass 2.61.1: a value
    /// that is not protected, a protected value made from a string, and a protected value
    /// KeePass itself has read as a string since, are each kept as that string, in the open;
    /// a protected value made from bytes, which is a value a file was loaded with and a value
    /// written through the gate, is kept as UTF-8 in a `ProtectedBinary`, encrypted as a
    /// protected attachment is. That is the whole of the difference between two values, as it
    /// is between two attachments: in the open, or protected.
    ///
    /// **A value kept as a string is that string.** It is handed out as it is, whole and in a
    /// single piece, with nothing copied: what Json.NET writes of a string it writes a
    /// character at a time, so no copy of the value is made for an answer either.
    ///
    /// **A value kept as bytes is decrypted and decoded a piece at a time.** The bytes are read
    /// as an attachment's are, `StoredBinaries`, 48 KiB at a time from a copy of each piece,
    /// and decoded in steps of <see cref="Step"/> by a decoder that carries a sequence cut by
    /// a step into the step after it, so the pieces joined are the string the bytes decode to
    /// whole, a sequence that is not UTF-8 replaced as a decoding of the whole replaces it.
    /// Neither the value's bytes nor its text ever stand whole for a read, and the buffer a
    /// step is decoded into is overwritten.
    ///
    /// **Where that cannot be done, KeePass reads**, as it does for an attachment. On a
    /// KeePass that keeps a value otherwise, checked once and logged as the plugin loads,
    /// <see cref="WhyNotInPlace"/>, a value that is not protected is read with `ReadString`,
    /// which hands back the string KeePass keeps, and a protected value with `ReadUtf8`, a
    /// copy of the whole value that is decoded and overwritten. And the bytes of a value kept
    /// through another cipher than Windows' are read through KeePass by the reader
    /// underneath, `StoredBinaries`. `KeePassContractTests` pins the two fields and what
    /// KeePass keeps in each.
    ///
    /// **A value written is handed to KeePass as bytes**, since a `ProtectedString` made from
    /// a string keeps that string, whatever its flag, and a `ProtectedString` made from bytes
    /// with the flag set encrypts a copy of them.
    ///
    /// **What this keeps and what it does not.** KeePass's protection is of memory that leaves
    /// the process, a crash dump or the page file, and what this keeps is that the gate does
    /// not undo it for a value KeePass has not itself decoded. A piece of a value that is
    /// served is a string in this process until the runtime reuses its memory, and so is the
    /// answer it is written into: neither can be overwritten. Nothing here is a control
    /// against code running as the user, which `CLAUDE.md` concedes.
    /// </remarks>
    internal static class StoredStrings
    {
        /// <summary>
        /// How many bytes of a value kept as bytes are decoded at a time. The text of a step
        /// and the string made of it are each under the size the runtime keeps apart for
        /// large objects.
        /// </summary>
        public const int Step = 16 * 1024;

        // Published once it is found and never while finding it failed, as the layout of an
        // attachment is, `StoredBinaries`.
        private static readonly Lazy<Layout> Kept =
            new Lazy<Layout>(Layout.Read, System.Threading.LazyThreadSafetyMode.PublicationOnly);

        /// <summary>
        /// Hands out the value of a stored string as text, in order, a piece at a time, and
        /// nothing for a value of no characters or a name the entry does not hold.
        /// </summary>
        /// <param name="piece">
        /// Handed the string KeePass keeps, whole, for a value kept as a string, and a string
        /// of at most <see cref="Step"/> characters at a time for a value kept as bytes.
        /// </param>
        public static void Read(ProtectedString held, Action<string> piece)
        {
            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            if (held == null)
            {
                return;
            }

            string text;
            ProtectedBinary bytes;
            if (Found(held, out text, out bytes))
            {
                if (bytes != null)
                {
                    Decoded(bytes, piece);
                    return;
                }
            }
            else if (!held.IsProtected)
            {
                // The string KeePass keeps for it, which this hands back and changes nothing.
                text = held.ReadString();
            }
            else
            {
                var copy = held.ReadUtf8();
                try
                {
                    text = Encoding.UTF8.GetString(copy);
                }
                finally
                {
                    MemUtil.ZeroByteArray(copy);
                }
            }

            if (text.Length > 0)
            {
                piece(text);
            }
        }

        /// <summary>
        /// The value of a stored string whole, or the empty string for a name the entry does
        /// not hold, as KeePass's own `ReadSafe` answers.
        /// </summary>
        /// <remarks>
        /// For a value that is written into a description of its entry, a title or a URL. The
        /// string KeePass keeps where it keeps the value as a string, and the pieces joined
        /// where it keeps bytes.
        /// </remarks>
        public static string Read(ProtectedString held)
        {
            string first = null;
            StringBuilder joined = null;
            Read(held, piece =>
            {
                if (first == null)
                {
                    first = piece;
                    return;
                }

                if (joined == null)
                {
                    joined = new StringBuilder(first);
                }

                joined.Append(piece);
            });

            return joined != null ? joined.ToString() : first ?? string.Empty;
        }

        /// <summary>
        /// Where KeePass keeps a value at this moment: the string or the bytes, never both, or
        /// false on a KeePass that keeps a value otherwise.
        /// </summary>
        /// <remarks>
        /// The bytes are asked for ahead of the string. KeePass's own `ReadString` moves a
        /// value from bytes to a string on KeePass's thread, and it sets the string before it
        /// lets the bytes go, so a value asked in this order from another thread is found in
        /// the bytes or in the string. The bytes found are still the value once KeePass has
        /// let them go: a `ProtectedBinary` is never changed.
        /// </remarks>
        internal static bool Found(ProtectedString held, out string text, out ProtectedBinary bytes)
        {
            text = null;
            bytes = null;

            var layout = Kept.Value;
            if (layout.Missing != null)
            {
                return false;
            }

            bytes = (ProtectedBinary)layout.Bytes.GetValue(held);
            if (bytes != null)
            {
                return true;
            }

            text = (string)layout.Text.GetValue(held);
            return text != null;
        }

        /// <summary>
        /// Hands out what a value's stored bytes decode to, a piece at a time, each sequence
        /// that is not UTF-8 replaced as a decoding of the whole replaces it.
        /// </summary>
        internal static void Decoded(ProtectedBinary bytes, Action<string> piece)
        {
            var decoder = Encoding.UTF8.GetDecoder();
            var text = new char[Encoding.UTF8.GetMaxCharCount(Step)];
            try
            {
                StoredBinaries.Read(bytes, (held, offset, count) =>
                {
                    for (var at = 0; at < count;)
                    {
                        var taken = Math.Min(Step, count - at);
                        var made = decoder.GetChars(held, offset + at, taken, text, 0, false);
                        if (made > 0)
                        {
                            piece(new string(text, 0, made));
                        }

                        at += taken;
                    }
                });

                // What a sequence cut short at the very end decodes to.
                var last = decoder.GetChars(Array.Empty<byte>(), 0, 0, text, 0, true);
                if (last > 0)
                {
                    piece(new string(text, 0, last));
                }
            }
            finally
            {
                // The decoder keeps the bytes of a sequence a step cut, and the buffer the text
                // of the last step: neither outlives the read.
                decoder.Reset();
                Array.Clear(text, 0, text.Length);
            }
        }

        /// <summary>
        /// Why no value is read where KeePass keeps it, or null where KeePass keeps a value as
        /// it was measured.
        /// </summary>
        /// <remarks>
        /// Asked as the plugin loads, `KeePassGateExt`, which logs what it is answered, so
        /// that a KeePass on which every read of a protected value copies the whole value is
        /// in the log, and by the test that pins it.
        /// </remarks>
        public static string WhyNotInPlace
        {
            get { return Kept.Value.Missing; }
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

        /// <summary>
        /// What this reads of a `ProtectedString`, found once, with what is missing where
        /// nothing can be read in place.
        /// </summary>
        private sealed class Layout
        {
            private Layout(string missing)
            {
                Missing = missing;
            }

            private Layout(FieldInfo bytes, FieldInfo text)
            {
                Bytes = bytes;
                Text = text;
            }

            public string Missing { get; private set; }

            /// <summary>The value as UTF-8 in a `ProtectedBinary`, `m_pbUtf8`.</summary>
            public FieldInfo Bytes { get; private set; }

            /// <summary>The value as a string, `m_strPlainText`.</summary>
            public FieldInfo Text { get; private set; }

            public static Layout Read()
            {
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(ProtectedString);

                var bytes = type.GetField("m_pbUtf8", Private);
                var text = type.GetField("m_strPlainText", Private);
                if (bytes == null || bytes.FieldType != typeof(ProtectedBinary)
                    || text == null || text.FieldType != typeof(string))
                {
                    return new Layout("this KeePass keeps a string otherwise than the gate reads it in place");
                }

                return new Layout(bytes, text);
            }
        }
    }
}
