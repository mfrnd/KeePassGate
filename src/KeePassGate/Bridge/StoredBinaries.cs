using System;
using System.Reflection;
using System.Security.Cryptography;
using KeePassGate.Diagnostics;
using KeePassLib.Security;

namespace KeePassGate.Bridge
{
    // A method of KeePass's own, named the way KeePass names it.
    // cspell:ignore Xorred

    /// <summary>
    /// Reads the bytes KeePass keeps for an attachment a piece at a time, from where KeePass
    /// keeps them, so that no copy of the whole attachment is ever made.
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
    /// never stands decrypted.
    ///
    /// **Where this cannot be done, nothing is read.** A KeePass that keeps an attachment
    /// otherwise, a Windows that no longer decrypts a piece on its own, which is checked the
    /// first time anything is read, and a protected attachment kept any other way, by KeePass's
    /// own cipher where `ProtectedMemory` is not supported or by the cipher another plugin
    /// gave KeePass through `ProtectedBinary.ExtCrypt`, each refuse the read with what is
    /// wrong, <see cref="AttachmentUnreadable"/>, which a client is answered `500 unreadable`
    /// for. There is no second way of reading to fall back to, the operator's ruling of
    /// 2026-11-14. `KeePassContractTests` pins what this reads of KeePass and of Windows.
    ///
    /// **How an attachment is kept is of the moment, and is asked at every read.** `ReadData`
    /// encrypts the buffer again through whatever cipher stands when it runs, and KeePass's
    /// save reads every attachment, so an attachment made under Windows' protection is kept
    /// through another plugin's cipher once KeePass has read it while that cipher stood, and
    /// under Windows' protection again once KeePass has read it with the cipher gone. Which
    /// it is, is read under KeePass's lock for every piece.
    ///
    /// Nothing here is a control against code running as the user, which `CLAUDE.md`
    /// concedes: what it keeps is that the gate does not undo, for longer than a piece is being
    /// sent, the protection KeePass gives an attachment against memory that leaves the process.
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

        private static readonly Lazy<Layout> Kept = new Lazy<Layout>(Layout.Read);

        /// <summary>
        /// Hands out what an attachment holds, in order, a piece at a time.
        /// </summary>
        /// <param name="piece">
        /// Handed an array, an offset into it and a count. The array is KeePass's own buffer
        /// for an attachment held in the open, and a copy that is overwritten once this
        /// returns for a protected attachment: it is read where it is handed over, and neither
        /// kept nor changed.
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

            var layout = Kept.Value;
            if (layout.Refusal != null)
            {
                throw new AttachmentUnreadable(layout.Refusal);
            }

            var held = (byte[])layout.Data.GetValue(binary);
            if (held == null || held.Length < length)
            {
                throw new AttachmentUnreadable("KeePass keeps an attachment in a buffer shorter than the attachment");
            }

            if (!binary.IsProtected)
            {
                for (var at = 0; at < length; at += Piece)
                {
                    piece(held, at, Math.Min(Piece, length - at));
                }

                return;
            }

            var sync = layout.Sync.GetValue(binary);
            if (sync == null)
            {
                throw new AttachmentUnreadable("KeePass keeps a protected attachment with no lock to read it under");
            }

            for (var at = 0; at < length; at += Piece)
            {
                // The block ahead of the piece is decrypted with it and thrown away, since the
                // first block of whatever is decrypted is the block the chaining gets wrong.
                var lead = at == 0 ? 0 : Block;
                var count = Math.Min(Piece, length - at);
                var whole = ((count + Block - 1) / Block) * Block;
                if (at + whole > held.Length)
                {
                    throw new AttachmentUnreadable("KeePass keeps a protected attachment in a buffer that ends inside a block");
                }

                var copy = new byte[lead + whole];
                try
                {
                    // Under KeePass's own lock, and only for the copy: `ReadData` decrypts the
                    // buffer where it stands while it holds this, so a copy taken beside it
                    // could be of a buffer standing decrypted, or of half of each.
                    lock (sync)
                    {
                        var kind = layout.Kind.GetValue(binary);
                        if (!Equals(kind, layout.ByWindows))
                        {
                            throw new AttachmentUnreadable(
                                "KeePass keeps this protected attachment as " + kind + ", which is not read a piece at a time");
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
            }
        }

        /// <summary>
        /// Refuses an attachment that <see cref="Read"/> would refuse, with nothing read.
        /// </summary>
        /// <remarks>
        /// For a write, before the attachment it made is put on an entry: an attachment the
        /// gate cannot read back would fail the tag of the change that stored it, once the
        /// change was made, and every read of its entry after that. KeePass keeps what it is
        /// handed through whatever cipher is set at that moment, so a write made while another
        /// plugin's cipher stands makes such an attachment. And for every tag over an
        /// attachment, `KdbxVault.Seen`, since how an attachment is kept can change after its
        /// digest was kept.
        /// </remarks>
        public static void Check(ProtectedBinary binary)
        {
            if (binary == null)
            {
                throw new ArgumentNullException(nameof(binary));
            }

            if (binary.Length == 0)
            {
                return;
            }

            var layout = Kept.Value;
            if (layout.Refusal != null)
            {
                throw new AttachmentUnreadable(layout.Refusal);
            }

            if (!binary.IsProtected)
            {
                return;
            }

            var sync = layout.Sync.GetValue(binary);
            if (sync == null)
            {
                throw new AttachmentUnreadable("KeePass keeps a protected attachment with no lock to read it under");
            }

            lock (sync)
            {
                var kind = layout.Kind.GetValue(binary);
                if (!Equals(kind, layout.ByWindows))
                {
                    throw new AttachmentUnreadable(
                        "KeePass keeps this protected attachment as " + kind + ", which is not read a piece at a time");
                }
            }
        }

        /// <summary>
        /// The SHA-256 of what an attachment holds, taken a piece at a time.
        /// </summary>
        public static byte[] Digest(ProtectedBinary binary)
        {
            using (var sha256 = SHA256.Create())
            {
                Read(binary, (bytes, offset, count) => sha256.TransformBlock(bytes, offset, count, null, 0));
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return sha256.Hash;
            }
        }

        /// <summary>
        /// Why nothing can be read here, or null where everything this reads of KeePass and of
        /// Windows is as it was measured.
        /// </summary>
        /// <remarks>For the test that pins it, and for whoever wants the reason without a read.</remarks>
        public static string Refusal
        {
            get { return Kept.Value.Refusal; }
        }

        /// <summary>
        /// What this reads of a `ProtectedBinary`, found once, with why it cannot be read where
        /// anything is missing.
        /// </summary>
        private sealed class Layout
        {
            private Layout(string refusal)
            {
                Refusal = refusal;
            }

            private Layout(FieldInfo data, FieldInfo sync, FieldInfo kind, object byWindows)
            {
                Data = data;
                Sync = sync;
                Kind = kind;
                ByWindows = byWindows;
            }

            public string Refusal { get; private set; }

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
                    return new Layout("this KeePass keeps an attachment otherwise than the gate reads it");
                }

                if (!Chains())
                {
                    return new Layout("this Windows does not decrypt a piece of protected memory on its own");
                }

                return new Layout(data, sync, kind, Enum.Parse(kind.FieldType, "ProtectedMemory"));
            }

            /// <summary>
            /// Whether Windows still decrypts a piece of protected memory on its own once the
            /// block ahead of it is decrypted with it, which is what a protected attachment is
            /// read by and nothing Windows documents.
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

                    for (var at = Block; at < known.Length; at += Block)
                    {
                        var copy = new byte[2 * Block];
                        Buffer.BlockCopy(kept, at - Block, copy, 0, copy.Length);
                        ProtectedMemory.Unprotect(copy, MemoryProtectionScope.SameProcess);

                        for (var i = 0; i < Block; i++)
                        {
                            if (copy[Block + i] != known[at + i])
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }
                catch (CryptographicException)
                {
                    // Protection of process memory that is not there at all is no way to read
                    // a protected attachment either.
                    return false;
                }
                catch (PlatformNotSupportedException)
                {
                    return false;
                }
            }
        }
    }
}
