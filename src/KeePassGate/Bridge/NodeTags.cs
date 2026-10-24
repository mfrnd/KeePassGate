using System;
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
    /// database's host certificate, which is stored in the database and handed to no client, so
    /// nothing outside the gate can compute a tag for a guess, and one content gives one tag for as
    /// long as the authority stands: across a lock, a restart and a reload alike. Recreating the
    /// authority changes every tag, which costs a `412` and never an overwrite.
    ///
    /// **The key keeps tags from being computed, and it does not keep them from being compared.**
    /// A client that can make a write under `If-Match` learns whether the node held what its tag
    /// named, and a write it can make changes the content in a way it chooses, so a tag handed to a
    /// client that may not read every part it covers would let that client test guesses at the
    /// rest one request at a time. That is why the routes hand a tag, and honor `If-Match`, only to
    /// a caller whose rule may read everything the tag covers, `API.md` §410, and why nothing here
    /// tries to make a tag safe for anybody else.
    ///
    /// **Exactly what the routes serve, as they serve it, and nothing else.** An entry's tag covers
    /// its identifier, the group it is in, when it was created and last changed, every string with
    /// its name, whether it is served as protected and its value, every attachment with its name,
    /// its size and its bytes, and when it expires; a group's covers its identifier, the group it
    /// is in, its name, its notes and its two times. The identifier is there so that two nodes
    /// holding the same never share a tag, which would tell a client they do. A time enters as the
    /// wire writes it, to the second, because a file reopened keeps no more than that: a fraction
    /// in the tag changed every tag across a reload for nothing anybody wrote. A rule is left out,
    /// since no route serves one.
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
    /// alive, and the digest is a <see cref="Lazy{T}"/> so that two readers asking at once decrypt
    /// it once between them. `KeePassContractTests` pins the immutability, because a KeePass that
    /// changed bytes in place would leave a tag standing over content it no longer describes.
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

        /// <summary>An entry's tag.</summary>
        /// <param name="guarded">
        /// Whether a string is served as protected, which is the flag or the database's own setting
        /// for the name, so that the tag follows what a client is told rather than one half of it.
        /// </param>
        public string Of(PwEntry entry, Func<string, ProtectedString, bool> guarded)
        {
            if (entry == null)
            {
                return null;
            }

            using (var hmac = new HMACSHA256(_key))
            {
                Put(hmac, "entry");
                Put(hmac, entry.Uuid.ToHexString());
                Put(hmac, Parent(entry.ParentGroup));
                Put(hmac, Served(entry.CreationTime));
                Put(hmac, Served(entry.LastModificationTime));

                foreach (var pair in entry.Strings)
                {
                    Put(hmac, "string");
                    Put(hmac, pair.Key);
                    Put(hmac, guarded(pair.Key, pair.Value) ? "protected" : "plain");

                    // The value as a read serves it, and not the bytes the store keeps: a file
                    // another program wrote can hold bytes that are not UTF-8, which the read
                    // serves with each bad sequence replaced, and a tag over the stored bytes was a
                    // tag over a value nobody was ever served. The string is the one KeePass keeps
                    // once it has been read, so asking for it decrypts nothing twice; the encoded
                    // copy is wiped.
                    var value = Encoding.UTF8.GetBytes(pair.Value.ReadString());
                    try
                    {
                        Put(hmac, value);
                    }
                    finally
                    {
                        MemUtil.ZeroByteArray(value);
                    }
                }

                foreach (var pair in entry.Binaries)
                {
                    Put(hmac, "attachment");
                    Put(hmac, pair.Key);
                    Put(hmac, Length(pair.Value.Length));

                    if (pair.Value.Length <= VaultAttachment.MostServed)
                    {
                        Put(hmac, Digests.GetValue(pair.Value, Deferred).Value);
                    }
                }

                Put(hmac, entry.Expires ? Served(entry.ExpiryTime) : "never");

                return Done(hmac);
            }
        }

        /// <summary>A group's tag.</summary>
        public string Of(PwGroup group)
        {
            if (group == null)
            {
                return null;
            }

            using (var hmac = new HMACSHA256(_key))
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
            return new Lazy<byte[]>(() => Digest(binary), LazyThreadSafetyMode.ExecutionAndPublication);
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
