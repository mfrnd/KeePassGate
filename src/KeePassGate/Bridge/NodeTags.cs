using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// What a group or an entry holds, condensed into the tag a request can name it by.
    /// </summary>
    /// <remarks>
    /// **The tag is the content, not a clock.** KeePass keeps no version counter, and its
    /// modification time misses what a tag must not: it steps every half to one and a half
    /// milliseconds, so two changes inside one step share it; a merge takes the other copy's
    /// strings at an equal time and leaves the time where it was; a move stamps only
    /// `LocationChanged`; and a plugin that sets a value without calling `Touch` moves nothing at
    /// all. Every one of those was measured against KeePass 2.61.1, and `FINDINGS.md` has them. A
    /// digest of what is there misses none of them.
    ///
    /// **Keyed, because a plain digest of a secret is a way to guess it.** Over a plain hash a
    /// client could try values offline until one reproduced the tag. The key is derived from the
    /// database's host certificate as the database stores it, a PKCS#12 carrying the host's private
    /// key, which leaves the database for nobody: the certificate alone is handed to every client
    /// in every handshake, and the private key beside it is what no client holds. So nothing
    /// outside the gate can compute a tag for a guess, and one content gives one tag for as long as
    /// the authority stands: across a lock, a restart and a reload alike. Recreating the authority
    /// changes every tag, which costs a `412` and never an overwrite.
    ///
    /// **The key is overwritten when the database stops being served**, by <see cref="Clear"/>, as
    /// every other array the plugin keeps of the host's material is, `CA.md` §208. A tag asked for
    /// afterwards is refused rather than made under a key of zeros, which anybody could compute.
    ///
    /// **The key keeps tags from being computed, and it does not keep them from being compared.**
    /// A client that can make a write under `If-Match` learns whether the node held what its tag
    /// named, and a write it can make changes the content in a way it chooses, so a tag covering
    /// a part its holder may not read would let that holder test guesses at the part one request
    /// at a time. That is why an entry's tag covers what its caller may read and nothing else,
    /// `API.md` §410: the tag of a caller that may not read a value holds nothing derived from
    /// the value, so a guess written blind moves that tag the same whether the guess was
    /// right, through the entry's `modified` and through nothing else. This
    /// reverses the rule that a tag covered the whole entry and was handed only to a caller
    /// whose one rule read all of it, the operator's ruling of 2026-11-10.
    ///
    /// **What the entry's own routes serve its caller, as they serve it, and nothing a caller
    /// may not read.** A protected title or URL a listing of entries shows a caller is in its
    /// view of the entry, so a read serves it and the tag covers it. A caller holding `read`
    /// with `attachments` and no `list` has every attachment in its tag, since it reads any of
    /// them by name though no route lists them to it, `API.md` §410. An
    /// entry's tag covers its identifier, the group it is in, when it was created and last
    /// changed, every string with its name, whether it is served as protected and, where its
    /// caller may read it, its value, every attachment with its name and its size where its
    /// caller may list attachments and with its bytes where its caller may read them, and when
    /// it expires; a group's covers its identifier, the group it
    /// is in, its name, its notes and its two times. The identifier is there so that two nodes
    /// holding the same never share a tag, which would tell a client they do. A time enters as the
    /// wire writes it, to the second, because a file reopened keeps no more than that: a fraction
    /// in the tag changed every tag across a reload for nothing anybody wrote. A rule is left out,
    /// since no route serves one.
    ///
    /// **An entry's tag is taken of the view the routes answer from**, a
    /// <see cref="VaultEntryView"/>, and never of the entry itself, so the tag and the answer are
    /// one projection and cannot come to cover different things. A value the view withholds
    /// enters as a fixed mark, with nothing derived from what is withheld, and attachments the
    /// view does not hold enter nothing at all. **Nothing about the rule enters**, no scope, no
    /// mark of how far a rule reaches and no client: the content already differs wherever two
    /// views differ, and two callers with the same view hold the same tag, so a reader can hand
    /// its tag to a writer that sees what it sees. **So a rule widened over an entry where
    /// nothing new comes into sight moves no tag**: an entry holding no attachment is tagged
    /// alike for a caller that may read attachments and for a caller that may not, since
    /// neither is shown any, and a `412` is never the answer to a change nobody could see.
    ///
    /// **Strings and attachments enter in an order of the tag's own**, by name, ordinal. KeePass
    /// keeps both in dictionaries sorted by the current culture, in the order their names were
    /// compared when the file loaded, so a tag walked in that order changed for content nobody
    /// changed once the regional format or Windows' collation moved between two sessions, and a
    /// write under the earlier tag was refused `412`.
    ///
    /// **An attachment larger than the gate serves enters by its size alone.** Its bytes cannot be
    /// read through the gate and cannot be written through it either, so no client holds a copy
    /// to lose an update to, and hashing megabytes on a connection thread for a tag that protects
    /// nothing is a cost with no return. One replaced in KeePass by another of the same size
    /// leaves the tag alone, which costs a client nothing it could have read.
    ///
    /// **An attachment's bytes are hashed once.** KeePass never changes a `ProtectedBinary` in
    /// place, so a digest kept against the object stays true for as long as the object lives, and
    /// a changed attachment is a new object nothing has kept a digest for. KeePass relies on that
    /// itself, keeping the object's own hash code in it once asked.
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> keys by the object itself and holds it
    /// weakly, so a digest goes when KeePass lets the attachment go and nothing here keeps one
    /// alive. `KeePassContractTests` pins the immutability, because a KeePass that changed bytes in
    /// place would leave a tag standing over content it no longer describes.
    ///
    /// **A digest is kept and a failure is not.** The digest is a <see cref="Lazy{T}"/> that
    /// publishes the first value made and caches no exception, so a decryption that fails once,
    /// through memory protection, another plugin's `ProtectedBinary.ExtCrypt` or an allocation, is
    /// tried again by the next tag. Kept, it failed every tag of every entry holding that
    /// attachment until KeePass let the object go. The price is that two readers asking at once may
    /// each decrypt it, and one digest of the two stands.
    ///
    /// **A new hasher per tag, and a part at a time**, which was measured against the obvious
    /// economies and kept: reusing the hasher saved a fifth of a microsecond, and buffering the
    /// parts to hash them at once about three, on a tag of about eleven. No request takes more than
    /// two tags, the one a change is asked against and the one it leaves, and a hasher kept between
    /// requests would be one shared across connection threads,
    /// while a buffer of the parts would be one more copy of every value to wipe.
    /// </remarks>
    public sealed class NodeTags
    {
        /// <summary>How many bytes of the HMAC a tag carries: 128 bits, written as 32 hex digits.</summary>
        private const int Width = 16;

        /// <summary>What the key is derived under, so that it is this key and no other the same bytes could make.</summary>
        private const string Purpose = "KeePassGate node tags";

        private static readonly ConditionalWeakTable<ProtectedBinary, Lazy<byte[]>> Digests =
            new ConditionalWeakTable<ProtectedBinary, Lazy<byte[]>>();

        private readonly byte[] _key;
        private bool _cleared;

        /// <summary>For one database, keeping a copy of its key rather than the caller's array.</summary>
        /// <param name="key">The database's tag key, as <see cref="KeyFrom"/> derives it.</param>
        public NodeTags(byte[] key)
        {
            if (key == null || key.Length == 0)
            {
                throw new ArgumentException("a tag key is needed", nameof(key));
            }

            _key = (byte[])key.Clone();
        }

        /// <summary>The tag key for a database, from its host certificate as it is stored.</summary>
        /// <remarks>
        /// **The stored PKCS#12, and not the gate key**, which is the obvious other choice and the
        /// wrong one: every client that paired holds the gate key, since that is what it proves it
        /// was invited with, while the PKCS#12 carries the host's private key and leaves the
        /// database for nobody. Read as stored, so the same file gives the same key every time it
        /// is opened.
        /// </remarks>
        public static byte[] KeyFrom(byte[] hostPkcs12)
        {
            if (hostPkcs12 == null || hostPkcs12.Length == 0)
            {
                throw new ArgumentException("a host certificate is needed", nameof(hostPkcs12));
            }

            using (var hmac = new HMACSHA256(hostPkcs12))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(Purpose));
            }
        }

        /// <summary>Overwrites the key, after which every tag asked for is refused.</summary>
        /// <remarks>
        /// Called when the target holding this is let go by its last holder, so nothing is asking
        /// for a tag by then: what is refused is only a defect that kept the vault past its target.
        /// </remarks>
        public void Clear()
        {
            _cleared = true;
            Array.Clear(_key, 0, _key.Length);
        }

        /// <summary>An entry's tag, as one caller may read the entry.</summary>
        /// <remarks>
        /// **Of the view and of nothing else.** The routes answer from the same view, so a part
        /// that is served is a part that is hashed, and what the view leaves out cannot enter:
        /// this never sees the entry the view was taken of.
        ///
        /// **A withheld value enters as a mark that it is withheld**, the same mark whatever the
        /// value is and however long, so the tag of a view moves with nothing its caller may not
        /// read.
        ///
        /// **Attachments enter as far as the view holds them, and no mark of how far that is.**
        /// Nothing when the view holds none, each name and size when they are listed, and each
        /// name, size and digest when they are read. A view that holds no attachment is tagged
        /// alike whether its caller's rules reach attachments or the entry has none, so a rule
        /// widened over an entry where nothing new comes into sight moves no tag.
        ///
        /// **No tag for a view whose sight does not read**, which the view itself already
        /// answers by never asking, `VaultEntryView.Tag`.
        /// </remarks>
        public string Of(VaultEntryView view)
        {
            if (view == null || !view.Sight.Reads)
            {
                return null;
            }

            using (var hmac = Keyed())
            {
                Put(hmac, "entry");
                Put(hmac, view.Entry.Uuid);
                Put(hmac, view.Entry.Group ?? string.Empty);
                Put(hmac, Moments.Wire(view.Entry.Created));
                Put(hmac, Moments.Wire(view.Entry.Modified));

                foreach (var field in view.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
                {
                    Put(hmac, "string");
                    Put(hmac, field.Name);
                    Put(hmac, field.IsProtected ? "protected" : "plain");

                    if (field.IsWithheld)
                    {
                        Put(hmac, "withheld");
                        continue;
                    }

                    // The value as a read serves it, and not the bytes the store keeps: a file
                    // another program wrote can hold bytes that are not UTF-8, which the read
                    // serves with each bad sequence replaced, and a tag over the stored bytes was a
                    // tag over a value nobody was ever served. The view keeps the string once it
                    // has been read, so asking for it decrypts nothing twice; the encoded copy is
                    // wiped.
                    Put(hmac, "value");
                    var value = Encoding.UTF8.GetBytes(field.Value);
                    try
                    {
                        Put(hmac, value);
                    }
                    finally
                    {
                        MemUtil.ZeroByteArray(value);
                    }
                }

                // What the view holds of the attachments and no mark for how far the sight
                // reaches: a view holding no attachment is tagged alike whether its caller's rule
                // reaches attachments or the entry has none, since neither caller is shown any.
                // A rule widened over an entry where nothing new comes into sight therefore moves
                // no tag, and a `412` is never the answer to a change nobody could see.
                var held = view.Attachments ?? Array.Empty<VaultViewAttachment>();

                foreach (var attachment in held.OrderBy(attachment => attachment.Name, StringComparer.Ordinal))
                {
                    Put(hmac, "attachment");
                    Put(hmac, attachment.Name);
                    Put(hmac, Length((uint)attachment.Size));

                    // Null for an attachment whose bytes are out of sight, and for an attachment
                    // larger than the gate serves: each enters by its name and size alone.
                    var digest = attachment.Digest;
                    if (digest != null)
                    {
                        Put(hmac, "bytes");
                        Put(hmac, digest);
                    }
                }

                Put(hmac, view.Entry.Expires.HasValue ? Moments.Wire(view.Entry.Expires.Value) : "never");

                return Done(hmac);
            }
        }

        /// <summary>
        /// The SHA-256 of an attachment's bytes, taken once for as long as KeePass keeps the
        /// attachment.
        /// </summary>
        /// <remarks>
        /// What a view's attachment answers its digest with. Kept against the object, for the
        /// reasons the class gives: the bytes behind a `ProtectedBinary` never change, and a
        /// failure is never kept.
        /// </remarks>
        internal static byte[] DigestOf(ProtectedBinary binary)
        {
            return Digests.GetValue(binary, Deferred).Value;
        }

        /// <summary>A group's tag.</summary>
        public string Of(PwGroup group)
        {
            if (group == null)
            {
                return null;
            }

            using (var hmac = Keyed())
            {
                Put(hmac, "group");
                Put(hmac, group.Uuid.ToHexString());
                Put(hmac, Parent(group.ParentGroup));
                Put(hmac, Served(group.CreationTime));
                Put(hmac, Served(group.LastModificationTime));
                Put(hmac, group.Name ?? string.Empty);
                Put(hmac, group.Notes ?? string.Empty);

                return Done(hmac);
            }
        }

        private HMACSHA256 Keyed()
        {
            if (_cleared)
            {
                throw new ObjectDisposedException(nameof(NodeTags), "the tag key has been overwritten");
            }

            return new HMACSHA256(_key);
        }

        private static string Parent(PwGroup parent)
        {
            return parent == null ? string.Empty : parent.Uuid.ToHexString();
        }

        /// <summary>A moment as the wire writes it, which is to the second.</summary>
        private static string Served(DateTime time)
        {
            return Moments.Wire(KdbxVault.When(time));
        }

        private static Lazy<byte[]> Deferred(ProtectedBinary binary)
        {
            return new Lazy<byte[]>(() => Digest(binary), LazyThreadSafetyMode.PublicationOnly);
        }

        /// <summary>One attachment's SHA-256, from a copy of its bytes that is wiped afterwards.</summary>
        private static byte[] Digest(ProtectedBinary binary)
        {
            var bytes = binary.ReadData();
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    return sha256.ComputeHash(bytes);
                }
            }
            finally
            {
                MemUtil.ZeroByteArray(bytes);
            }
        }

        private static void Put(HashAlgorithm hash, string text)
        {
            Put(hash, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>One part, behind its length, so that no two different nodes write the same stream.</summary>
        private static void Put(HashAlgorithm hash, byte[] part)
        {
            var length = Length((uint)part.Length);
            hash.TransformBlock(length, 0, length.Length, null, 0);
            hash.TransformBlock(part, 0, part.Length, null, 0);
        }

        /// <summary>A length, big endian, so that the stream is the same whatever machine writes it.</summary>
        private static byte[] Length(uint length)
        {
            return new[]
            {
                (byte)(length >> 24),
                (byte)(length >> 16),
                (byte)(length >> 8),
                (byte)length,
            };
        }

        private static string Done(HashAlgorithm hash)
        {
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var truncated = new byte[Width];
            Array.Copy(hash.Hash, truncated, Width);
            return Hex.Of(truncated);
        }
    }
}
