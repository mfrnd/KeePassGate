using System;
using System.Reflection;
using System.Security.Cryptography;
using KeePassLib.Security;

namespace KeePassGate.Bridge
{
    // A method of KeePass's own, named the way KeePass names it.
    // cspell:ignore Xorred

    /// <summary>
    /// Reads the bytes KeePass keeps for an attachment a piece at a time, from where KeePass
    /// keeps them, so that neither a read nor a digest makes a copy of the whole attachment,
    /// and through KeePass's own read where that cannot be done.
    /// </summary>
    /// <remarks>
    /// **KeePass hands an attachment out whole or not at all.** `ProtectedBinary` offers
    /// `ReadData` and `ReadXorredData`, each a copy of everything, and the class is sealed, read
    /// off KeePass 2.61.1. An answer made of such a copy holds the whole attachment for as long
    /// as its peer takes to read it, on every connection at once, which is what held a read
    /// of an attachment to a ceiling. So the bytes are read out of KeePass's own buffer, the
    /// private field `m_pbData`, a piece at a time.
    ///
    /// **An attachment held in the open is its buffer.** KeePass keeps the bytes as they are,
    /// padded to its block size, and never changes them: a `ProtectedBinary` is replaced and
    /// not written to. A piece is that buffer at an offset, with nothing copied. What a file
    /// attached in KeePass's own dialog is held as, `FINDINGS.md`.
    ///
    /// **A protected attachment is decrypted a piece at a time, from a copy of the piece.**
    /// KeePass keeps it encrypted in place with Windows' protection of process memory,
    /// `ProtectedMemory`, and `ReadData` decrypts the whole buffer where it stands, copies it
    /// and encrypts it again, under the object's own lock. That protection chains its blocks,
    /// measured and not documented: a piece of the encrypted buffer decrypts on its own to the
    /// right bytes in every block but its first, and in all of it when the block ahead of it
    /// is decrypted with it and thrown away. So a piece is copied out with the block ahead,
    /// under KeePass's lock so that no buffer standing decrypted for another reader is
    /// copied, the copy is decrypted, handed out and overwritten, and the attachment itself
    /// never stands decrypted for a read made in place.
    ///
    /// **Where that cannot be done, KeePass reads.** The operator's ruling of 2026-11-14,
    /// which reversed a ruling of the same day that nothing be read there: an attachment
    /// KeePass can read is an attachment a client is handed. A KeePass that keeps an
    /// attachment otherwise and a Windows that no longer protects memory the way a piece is
    /// read by, both checked once and logged as the plugin loads, <see cref="WhyNotInPlace"/>;
    /// a protected attachment kept any other way, by KeePass's own cipher where
    /// `ProtectedMemory` is not supported or by the cipher another plugin gave KeePass
    /// through `ProtectedBinary.ExtCrypt`; and a buffer that is not the shape a piece is read
    /// from: each is read through `ReadData`, a single copy of the whole attachment, handed
    /// out in the same pieces and overwritten once the read is over. That copy lives for as
    /// long as the read does, at whatever size the attachment is, as a field's value does,
    /// `StoredStrings`. `KeePassContractTests` pins what the read in place rests on in
    /// KeePass and in Windows.
    ///
    /// **How an attachment is kept is of the moment, and is asked at every piece.** `ReadData`
    /// encrypts the buffer again through whatever cipher stands when it runs, and KeePass's
    /// save reads every attachment, so an attachment made under Windows' protection is kept
    /// through another plugin's cipher once KeePass has read it while that cipher stood, and
    /// under Windows' protection again once KeePass has read it with the cipher gone, the
    /// read made here among those reads. Which it is, is read under KeePass's lock for every
    /// piece, and a read that finds it changed between two pieces goes on from where it
    /// stands through `ReadData`: what an attachment holds never changes, so the pieces
    /// still to come are the same bytes either way.
    ///
    /// **A read and a digest, and nothing else of KeePass's.** KeePass's own reads still
    /// decrypt the buffer where it stands, a save among them. And a write that replaces an
    /// attachment compares the stored attachment with the replacement through KeePass's own
    /// `ProtectedBinary.Equals` when their lengths match, `KdbxVault.UpdateAttachment`, which
    /// reads both whole: bounded by the 512 KiB a write takes, since the lengths have to
    /// match first.
    ///
    /// **What this keeps and what it does not.** KeePass's protection of an attachment is of
    /// memory that leaves the process, and what is kept is that a read made in place leaves
    /// KeePass's buffer encrypted and that every copy the gate itself makes of an
    /// attachment's bytes is overwritten: the decrypted piece and the whole copy here, the
    /// block a hasher keeps, <see cref="Digest"/>, and the base64 made of a piece,
    /// `JsonWriter.Base64`. What was written into an answer stays in the buffers of the text
    /// writer and of the TLS under it, which cannot be overwritten, until the runtime reuses
    /// that memory, as a served string does, `StoredStrings`. Nothing here is a control
    /// against code running as the user, which `CLAUDE.md` concedes.
    /// </remarks>
    internal static class StoredBinaries
    {
        /// <summary>
        /// The most bytes a piece holds: a multiple of the cipher's block, so that a piece ends
        /// where a block does, and of three, so that the base64 of a piece joins the base64 of
        /// the piece after it.
        /// </summary>
        public const int Piece = 48 * 1024;

        /// <summary>The block Windows' protection of process memory works in.</summary>
        private const int Block = 16;

        // Published once it is found and never while finding it failed, so that a failure
        // nothing here names, memory that ran out among them, is tried again by the next read
        // and is not what every later read throws.
        private static readonly Lazy<Layout> Kept =
            new Lazy<Layout>(Layout.Read, System.Threading.LazyThreadSafetyMode.PublicationOnly);

        /// <summary>
        /// Hands out what an attachment holds, in order, a piece at a time.
        /// </summary>
        /// <param name="piece">
        /// Handed an array, an offset into it and a count. The array is KeePass's own buffer
        /// for an attachment held in the open and read in place, and otherwise a copy that is
        /// overwritten once this is done with it: it is read where it is handed over, and
        /// neither kept nor changed.
        /// </param>
        public static void Read(ProtectedBinary binary, Action<byte[], int, int> piece)
        {
            if (binary == null)
            {
                throw new ArgumentNullException(nameof(binary));
            }

            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            var length = checked((int)binary.Length);
            if (length == 0)
            {
                // Nothing to hand out, and nothing KeePass encrypted: it keeps an attachment of
                // no bytes as it is, whatever its flag.
                return;
            }

            var at = InPlace(binary, length, piece);
            if (at < length)
            {
                Whole(binary, at, length, piece);
            }
        }

        /// <summary>
        /// Hands out as much of an attachment as can be read where KeePass keeps it, from its
        /// start, and answers how many bytes that was: all of them, wherever KeePass and
        /// Windows are as they were measured and the attachment stays kept as it was.
        /// </summary>
        private static int InPlace(ProtectedBinary binary, int length, Action<byte[], int, int> piece)
        {
            var layout = Kept.Value;
            if (layout.Missing != null)
            {
                return 0;
            }

            var held = (byte[])layout.Data.GetValue(binary);
            if (held == null || held.Length < length)
            {
                return 0;
            }

            var at = 0;
            if (!binary.IsProtected)
            {
                // Stepped by what was handed out, so that the offset ends at the length and
                // never past it: stepped by a whole piece, it wrapped for an attachment within
                // a piece of the most an array holds.
                while (at < length)
                {
                    var count = Math.Min(Piece, length - at);
                    piece(held, at, count);
                    at += count;
                }

                return at;
            }

            var sync = layout.Sync.GetValue(binary);
            if (sync == null)
            {
                return 0;
            }

            while (at < length)
            {
                // The block ahead of the piece is decrypted with it and thrown away, since the
                // first block of whatever is decrypted is the block the chaining gets wrong.
                var lead = at == 0 ? 0 : Block;
                var count = Math.Min(Piece, length - at);
                var whole = ((count + Block - 1) / Block) * Block;
                if ((long)at + whole > held.Length)
                {
                    // A buffer that ends inside a block is no buffer Windows protected.
                    return at;
                }

                var copy = new byte[lead + whole];
                try
                {
                    // Under KeePass's own lock, and only for the copy: `ReadData` decrypts the
                    // buffer where it stands while it holds this, so a copy taken beside it
                    // could be of a buffer standing decrypted, or of half of each.
                    lock (sync)
                    {
                        if (!Equals(layout.Kind.GetValue(binary), layout.ByWindows))
                        {
                            // Kept through another cipher, from the start or since the piece
                            // ahead of this, which Windows' protection would make noise of
                            // and report nothing.
                            return at;
                        }

                        Buffer.BlockCopy(held, at - lead, copy, 0, copy.Length);
                    }

                    ProtectedMemory.Unprotect(copy, MemoryProtectionScope.SameProcess);
                    piece(copy, lead, count);
                }
                finally
                {
                    Array.Clear(copy, 0, copy.Length);
                }

                at += count;
            }

            return at;
        }

        /// <summary>
        /// Hands out what an attachment holds from an offset on, through KeePass's own read:
        /// a copy of the whole attachment, in the pieces a read in place hands out, overwritten
        /// whatever happens.
        /// </summary>
        private static void Whole(ProtectedBinary binary, int from, int length, Action<byte[], int, int> piece)
        {
            var copy = binary.ReadData();
            try
            {
                if (copy.Length != length)
                {
                    throw new InvalidOperationException("KeePass read an attachment to another length than it holds");
                }

                for (var at = from; at < length;)
                {
                    var count = Math.Min(Piece, length - at);
                    piece(copy, at, count);
                    at += count;
                }
            }
            finally
            {
                Array.Clear(copy, 0, copy.Length);
            }
        }

        /// <summary>
        /// The SHA-256 of what an attachment holds, taken a piece at a time.
        /// </summary>
        public static byte[] Digest(ProtectedBinary binary)
        {
            using (var sha256 = SHA256.Create())
            {
                try
                {
                    Read(binary, (bytes, offset, count) => sha256.TransformBlock(bytes, offset, count, null, 0));
                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return sha256.Hash;
                }
                finally
                {
                    // The hasher keeps the end of what it was fed in a block of its own until it
                    // is started again, and letting go of it clears nothing but the digest: for
                    // an attachment shorter than a block that is the whole attachment. Started
                    // again whatever happened, a read that failed part way among it.
                    sha256.Initialize();
                }
            }
        }

        /// <summary>
        /// Why no attachment is read where KeePass keeps it, or null where everything the read
        /// in place rests on in KeePass and in Windows is as it was measured.
        /// </summary>
        /// <remarks>
        /// Asked as the plugin loads, `KeePassGateExt`, which logs what it is answered, so
        /// that a KeePass or a Windows on which every read of an attachment copies the whole
        /// attachment is in the log, and by the test that pins it.
        /// </remarks>
        public static string WhyNotInPlace
        {
            get { return Kept.Value.Missing; }
        }

        /// <summary>
        /// What this reads of a `ProtectedBinary`, found once, with what is missing where
        /// nothing can be read in place.
        /// </summary>
        private sealed class Layout
        {
            private Layout(string missing)
            {
                Missing = missing;
            }

            private Layout(FieldInfo data, FieldInfo sync, FieldInfo kind, object byWindows)
            {
                Data = data;
                Sync = sync;
                Kind = kind;
                ByWindows = byWindows;
            }

            public string Missing { get; private set; }

            /// <summary>The buffer, `m_pbData`.</summary>
            public FieldInfo Data { get; private set; }

            /// <summary>The lock `ReadData` decrypts the buffer under, `m_objSync`.</summary>
            public FieldInfo Sync { get; private set; }

            /// <summary>How the buffer is kept at this moment, `m_mp`.</summary>
            public FieldInfo Kind { get; private set; }

            /// <summary>The kind that is Windows' protection of process memory.</summary>
            public object ByWindows { get; private set; }

            public static Layout Read()
            {
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(ProtectedBinary);

                var data = type.GetField("m_pbData", Private);
                var sync = type.GetField("m_objSync", Private);
                var kind = type.GetField("m_mp", Private);
                if (data == null || data.FieldType != typeof(byte[])
                    || sync == null
                    || kind == null || !kind.FieldType.IsEnum
                    || !Enum.IsDefined(kind.FieldType, "ProtectedMemory"))
                {
                    return new Layout("this KeePass keeps an attachment otherwise than the gate reads it in place");
                }

                if (!Chains())
                {
                    return new Layout("this Windows does not decrypt a piece of protected memory on its own");
                }

                return new Layout(data, sync, kind, Enum.Parse(kind.FieldType, "ProtectedMemory"));
            }

            /// <summary>
            /// Whether Windows still protects memory the way a protected attachment is read
            /// by, none of which Windows documents: a piece decrypts on its own once the block
            /// ahead of it is decrypted with it; the start of a buffer decrypts on its own,
            /// with no block ahead and whatever the length it was protected at; and the same
            /// bytes are protected to the same bytes, so that pieces copied before and after
            /// KeePass read the attachment itself are pieces of the same encryption.
            /// </summary>
            private static bool Chains()
            {
                var known = new byte[3 * Block];
                for (var i = 0; i < known.Length; i++)
                {
                    known[i] = (byte)((i * 7) + 1);
                }

                try
                {
                    var kept = (byte[])known.Clone();
                    ProtectedMemory.Protect(kept, MemoryProtectionScope.SameProcess);

                    var again = (byte[])known.Clone();
                    ProtectedMemory.Protect(again, MemoryProtectionScope.SameProcess);
                    for (var i = 0; i < kept.Length; i++)
                    {
                        if (kept[i] != again[i])
                        {
                            return false;
                        }
                    }

                    for (var at = Block; at < known.Length; at += Block)
                    {
                        var copy = new byte[2 * Block];
                        Buffer.BlockCopy(kept, at - Block, copy, 0, copy.Length);
                        ProtectedMemory.Unprotect(copy, MemoryProtectionScope.SameProcess);

                        // The first pass decrypts the start of the buffer, shorter than the
                        // buffer and with no block ahead, so all of it has to be right. Later
                        // passes are right from their second block.
                        for (var i = at == Block ? 0 : Block; i < copy.Length; i++)
                        {
                            if (copy[i] != known[at - Block + i])
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }
                catch (CryptographicException)
                {
                    // Windows refused to protect or to unprotect the buffer, which is no way to
                    // read a piece of a protected attachment either.
                    return false;
                }
                catch (NotSupportedException)
                {
                    // Protection of process memory that is not there at all.
                    return false;
                }
            }
        }
    }
}
