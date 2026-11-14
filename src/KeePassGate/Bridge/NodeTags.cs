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
    /// changes every tag, which costs a `412` and never an overwrite, and so can a version of
    /// the plugin that makes a tag of other bytes: what a tag is made of is no part of the
    /// contract, `API.md` §410.
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
    /// **A new hasher per tag, and a part at a time.** The parts are names, flags, moments and
    /// digests, so a tag hashes little, and no request takes more than two tags, the tag a
    /// change is asked against and the tag it leaves. A hasher kept between requests would be
    /// shared across connection threads, and a buffer of the parts would be a second copy of
    /// every digest to wipe.
    ///
    /// **A string's value enters by its digest, and the digest is taken once for as long as
    /// KeePass keeps the string.** The SHA-256 of the value as a read serves it, kept against
    /// the `ProtectedString` as an attachment's is kept against its `ProtectedBinary`, and on
    /// the same strength: KeePass replaces the object when a value changes and never changes
    /// it in place, which `KeePassContractTests` pins. No write is refused for an entry's
    /// size, so a value can be megabytes, and digested afresh for every tag, fifty protected
    /// strings of a million characters cost every tagged answer about sixty milliseconds and a
    /// copy of each. So a tag costs by how many strings an entry holds and not by how long
    /// they are, once somebody has been handed a tag over them. This changed what an entry's
    /// tag is made of, so every entry's tag changed once with it, which costs a client a `412`
    /// and a read. A group's tag is made as it was.
    ///
    /// **The digest of a value KeePass holds encrypted is kept encrypted**,
    /// <see cref="KeptDigest"/>, by the string's own flag, and a tag is handed a copy that it
    /// overwrites. Taking it reads such a value as a copy of its bytes, the copy overwritten
    /// and no string made of it unless its stored bytes are not UTF-8, so that KeePass goes on
    /// protecting the value, `StoredStrings`; a long value KeePass holds in the open is encoded
    /// a piece at a time, <see cref="Pieces"/>, with no second copy of it made.
    ///
    /// **Whether a digest was already kept shows in how long a tag takes**, for a long value:
    /// the first tag after KeePass puts a value in another object digests it, and the tags
    /// after it do not. KeePass does that when the value changes, and also with the value
    /// unchanged: when the database is locked and unlocked or closed and opened again, and
    /// when a synchronize takes the entry. So a client that sees the value can tell from the
    /// timing whether anybody was handed a tag over it since its object was last replaced,
    /// which is about what other clients, the operator or KeePass did, and never about a
    /// value. The operator's ruling of 2026-11-14 accepts that over a digest kept for each
    /// client: the attachments' digests and KeePass's own count of a value's characters show
    /// the same, and a value out of a caller's sight is never digested for that caller.
    /// </remarks>
    public sealed class NodeTags
    {
        /// <summary>How many bytes of the HMAC a tag carries: 128 bits, written as 32 hex digits.</summary>
        private const int Width = 16;

        /// <summary>What the key is derived under, so that it is this key and no other the same bytes could make.</summary>
        private const string Purpose = "KeePassGate node tags";

        private static readonly ConditionalWeakTable<ProtectedBinary, Lazy<byte[]>> Digests =
            new ConditionalWeakTable<ProtectedBinary, Lazy<byte[]>>();

        private static readonly ConditionalWeakTable<ProtectedString, Lazy<KeptDigest>> ValueDigests =
            new ConditionalWeakTable<ProtectedString, Lazy<KeptDigest>>();

        /// <summary>UTF-8 that refuses what the ordinary encoding replaces.</summary>
        private static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);

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
                    // tag over a value nobody was ever served. By its digest, which the store
                    // keeps against the object the value is held in, `DigestOf`,
                    // so a tag reads a value once for as long as KeePass keeps that object. A
                    // string in sight with no digest is refused and never read in its place:
                    // read here, a protected value became a string for every tag, and nothing
                    // would have shown it.
                    Put(hmac, "value");
                    var digest = field.Digest();
                    if (digest == null)
                    {
                        throw new InvalidOperationException("a string in sight came to a tag with no digest of its value");
                    }

                    try
                    {
                        Put(hmac, digest);
                    }
                    finally
                    {
                        MemUtil.ZeroByteArray(digest);
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

        /// <summary>
        /// The SHA-256 of a string's value as a read serves it, taken once for as long as
        /// KeePass keeps the string, as a copy for the caller to overwrite.
        /// </summary>
        /// <remarks>
        /// What a view's string answers its digest with. Kept against the object, for the
        /// reasons the class gives: KeePass replaces a `ProtectedString` and never changes it,
        /// and a failure is never kept. The digest of a protected value is kept as KeePass
        /// keeps the value, so what is handed out is decrypted for the caller and is the
        /// caller's to overwrite; the digest of a value held in the open is copied, so that the
        /// caller overwriting it leaves the kept digest whole.
        /// </remarks>
        internal static byte[] DigestOf(ProtectedString value)
        {
            return ValueDigests.GetValue(value, DeferredValue).Value.Copy();
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

        private static Lazy<KeptDigest> DeferredValue(ProtectedString value)
        {
            return new Lazy<KeptDigest>(() => new KeptDigest(value), LazyThreadSafetyMode.PublicationOnly);
        }

        /// <summary>
        /// The SHA-256 of a stored string's value as a read serves it, with every copy of a
        /// value KeePass protects overwritten, and no string made of it unless its stored
        /// bytes are not UTF-8.
        /// </summary>
        /// <remarks>
        /// Buffers of its own for each value: the first tag over an entry of long values held
        /// in the open allocates them once for each such value, about 80 KB, and no tag after
        /// it allocates any.
        /// </remarks>
        private static byte[] Digested(ProtectedString value)
        {
            using (var pieces = new Pieces())
            {
                if (!value.IsProtected)
                {
                    // Held in the open: KeePass keeps the string, and hands the same string out.
                    return Digested(value.ReadString(), pieces);
                }

                var stored = value.ReadUtf8();
                try
                {
                    return Digested(stored, pieces);
                }
                finally
                {
                    MemUtil.ZeroByteArray(stored);
                }
            }
        }

        /// <summary>
        /// The SHA-256 of a value's UTF-8 form, with every encoded copy of it wiped: encoded
        /// whole for a value no longer than a piece, and a piece at a time past that.
        /// </summary>
        /// <remarks>
        /// The same bytes either way, so a value's digest does not depend on which side of the
        /// length it falls: the pieces are what one encoder makes of the value from end to end,
        /// a pair of surrogates split between two pieces included.
        /// </remarks>
        internal static byte[] Digested(string value, Pieces pieces)
        {
            using (var sha256 = SHA256.Create())
            {
                if (value.Length <= Pieces.Characters)
                {
                    var whole = Encoding.UTF8.GetBytes(value);
                    try
                    {
                        return sha256.ComputeHash(whole);
                    }
                    finally
                    {
                        MemUtil.ZeroByteArray(whole);
                    }
                }

                pieces.Into(sha256, value);
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var digest = sha256.Hash;

                // The hasher keeps the end of the value in a block of its own until it is
                // started again, which the whole-value path above does for itself.
                sha256.Initialize();
                return digest;
            }
        }

        /// <summary>
        /// The SHA-256 of a value from the bytes the store keeps for it, digested as the value a
        /// read serves is.
        /// </summary>
        /// <remarks>
        /// Bytes that are UTF-8 are the bytes the served value encodes to, so they are digested
        /// as they are and no string is made of them. Bytes that are not are served with each
        /// bad sequence replaced, so they are decoded as a read decodes them and that value is
        /// digested: the same digest either way as the digest of the string, and a string made
        /// only of a value another program stored badly. The caller overwrites the bytes.
        /// </remarks>
        internal static byte[] Digested(byte[] stored, Pieces pieces)
        {
            if (!IsUtf8(stored))
            {
                return Digested(Encoding.UTF8.GetString(stored), pieces);
            }

            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(stored);
            }
        }

        /// <summary>
        /// Whether bytes decode with nothing replaced, which is whether decoding them and
        /// encoding the result gives the same bytes back.
        /// </summary>
        private static bool IsUtf8(byte[] bytes)
        {
            try
            {
                Strict.GetCharCount(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
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

            // The hasher keeps the end of what it was fed in a block of its own, and the last
            // parts of a tag are digests of values. Starting it again clears that block.
            hash.Initialize();
            return Hex.Of(truncated);
        }

        /// <summary>
        /// The digest of a string's value as it is kept for as long as KeePass keeps the string:
        /// as KeePass keeps the value itself.
        /// </summary>
        /// <remarks>
        /// **A protected value's digest is kept inside a `ProtectedBinary`.** A digest of a
        /// short secret is a way to guess the secret for whoever reads memory that left the
        /// process, a crash dump or the page file, and kept in the open it would be weaker than
        /// the value it was taken of, which KeePass keeps encrypted. A value KeePass holds in
        /// the open is in that memory already, and so its digest is kept in the open.
        /// </remarks>
        private sealed class KeptDigest
        {
            private readonly ProtectedBinary _protected;
            private readonly byte[] _open;

            public KeptDigest(ProtectedString value)
            {
                var digest = Digested(value);
                if (!value.IsProtected)
                {
                    _open = digest;
                    return;
                }

                try
                {
                    _protected = new ProtectedBinary(true, digest);
                }
                finally
                {
                    MemUtil.ZeroByteArray(digest);
                }
            }

            /// <summary>A copy of the digest, for the caller to overwrite.</summary>
            public byte[] Copy()
            {
                return _protected != null ? _protected.ReadData() : (byte[])_open.Clone();
            }
        }

        /// <summary>
        /// The buffers a long value held in the open is encoded through for its digest, a piece
        /// at a time, made when a long value asks for them and wiped when its digest is done.
        /// </summary>
        /// <remarks>
        /// Both hold a piece of a value in the clear while it is digested, so both are
        /// overwritten on the way out, as the copy of a value encoded whole is. Each is under
        /// the size the runtime keeps apart for large objects, so digesting a value KeePass
        /// keeps in the open allocates the same little however long the value is. A value
        /// KeePass protects comes here only where its stored bytes are not UTF-8: it is
        /// digested from a copy of those bytes.
        /// </remarks>
        internal sealed class Pieces : IDisposable
        {
            /// <summary>How many characters of a value are encoded at a time.</summary>
            public const int Characters = 16 * 1024;

            private char[] _text;
            private byte[] _encoded;
            private Encoder _encoder;

            /// <summary>Digests the UTF-8 form of a value, and nothing before or after it.</summary>
            public void Into(HashAlgorithm hash, string value)
            {
                if (_text == null)
                {
                    // Room for what a piece can come to, and for a surrogate the piece before
                    // it left with the encoder, so every piece is taken whole.
                    _text = new char[Characters];
                    _encoded = new byte[Encoding.UTF8.GetMaxByteCount(Characters)];
                    _encoder = Encoding.UTF8.GetEncoder();
                }

                _encoder.Reset();

                var at = 0;
                while (at < value.Length)
                {
                    var taken = Math.Min(Characters, value.Length - at);
                    value.CopyTo(at, _text, 0, taken);
                    at += taken;

                    // The buffer has room for whatever a piece comes to, so every character
                    // handed over is used and nothing is left to ask about but the bytes made.
                    int made;
                    _encoder.Convert(
                        _text, 0, taken, _encoded, 0, _encoded.Length, at == value.Length, out _, out made, out _);
                    hash.TransformBlock(_encoded, 0, made, null, 0);
                }
            }

            public void Dispose()
            {
                if (_text != null)
                {
                    Array.Clear(_text, 0, _text.Length);
                    MemUtil.ZeroByteArray(_encoded);
                }
            }
        }
    }
}
