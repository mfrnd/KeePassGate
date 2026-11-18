using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Transport;

namespace KeePassGate.Store
{
    /// <summary>
    /// What tells a node of the plugin's store by what it is: the group by the prefix of its
    /// identifier, and a record by the keyed tag at the start of its own, `STORE.md` §606 and §609.
    /// </summary>
    /// <remarks>
    /// **The bytes of the store, each written once, here**: the names of its strings, the check,
    /// the identifier and the ceilings of its reader, `STORE.md` §607 to §609, beside the format
    /// number they make up with the kinds of <see cref="StoreKinds"/>. It decides nothing about
    /// what is in force.
    ///
    /// **Every format here is the form the store was measured with**, as `probe/store.py` on
    /// `spikes/storage-survival` wrote it, with what `STORE.md` §646 fixed for the store's core
    /// where the measured code left a format open: six bytes of the check in an identifier, S31,
    /// and two for the number, S12.
    ///
    /// **Whether a node is the store's never rests on its own content passing.** A group is the
    /// plugin's by its identifier alone, and an entry is a record of the store by the first three
    /// bytes of its identifier, under any store key the file holds: so a record the reading would
    /// pass over, damaged, under a key the operator did not keep, or stripped of its strings, is
    /// recognized all the same. What follows is that an ordinary entry whose identifier happens to
    /// begin with a tag is taken for a record as well, which a random identifier meets about once
    /// in 17 million entries for each kind and each store key the file holds, `STORE.md` §621. A
    /// copy of a record is recognized by its content instead, since a copy's identifier is new.
    /// </remarks>
    public static class StoreFormat
    {
        /// <summary>
        /// What the name of every string the store writes begins with, `STORE.md` §607.
        /// </summary>
        public const string FieldPrefix = "KPG.";

        /// <summary>The kind of record a version is.</summary>
        public const string KindField = "KPG.Kind";

        /// <summary>A version's content, one flat JSON object of strings.</summary>
        public const string DataField = "KPG.Data";

        /// <summary>A version's check, `STORE.md` §608.</summary>
        public const string CheckField = "KPG.Mac";

        /// <summary>
        /// What the name of each item of a summary entry begins with, `STORE.md` §611.
        /// </summary>
        public const string ItemPrefix = "KPG.S.";

        /// <summary>
        /// The bytes of an identifier that name the kind, keyed, `STORE.md` §609.
        /// </summary>
        public const int TagLength = 3;

        /// <summary>The bytes that name what a record is about, keyed.</summary>
        public const int SubjectLength = 5;

        /// <summary>
        /// The bytes of a summary entry's height and of a version record's format number, zero
        /// for every other kind.
        /// </summary>
        public const int HeightLength = 2;

        /// <summary>The bytes of the check an identifier holds, as measured, `STORE.md` §609.</summary>
        public const int CheckLength = 6;

        /// <summary>The digits of an item's number, `STORE.md` §608.</summary>
        public const int ItemDigits = 4;

        /// <summary>
        /// The random bytes of a record's nonce, written as twice as many lowercase hexadecimal
        /// characters, `STORE.md` §607.
        /// </summary>
        public const int NonceLength = 16;

        /// <summary>
        /// The format number of the store this plugin reads, `STORE.md` §650: the form of every
        /// record, every item, the check and the identifier, as `StoreKinds` and this class hold
        /// them.
        /// </summary>
        /// <remarks>
        /// **A number of the store's own, and never the release's version**, which moves only
        /// when a release is made: a build between two releases that changed the store's form
        /// carries a number of its own, and that is what tells its store apart from the release's.
        /// It moves in the very change that alters the form, and `StoreKindsTests` holds it beside
        /// a description of that form, so the change cannot be made without the description and
        /// the number being looked at together. Until release 0.3.0 every build states 1 while the
        /// form is still being built, `STORE.md` §658. A patch release never moves it.
        /// </remarks>
        public const int Number = 1;

        /// <summary>A store key, written as 64 hexadecimal characters in the content that holds it.</summary>
        public const int KeyLength = 32;

        /// <summary>
        /// The longest string of the store a reader takes, `STORE.md` §607: the ceiling of the
        /// reader the wire's bodies are read with, which is the reader of a content.
        /// </summary>
        public const int MaxStringLength = JsonBody.MaxStringLength;

        /// <summary>
        /// The longest string of the store the plugin writes, `STORE.md` §643: the longest that was
        /// measured to come through the programs a database is shared with, below what its reader
        /// takes.
        /// </summary>
        /// <remarks>
        /// The distance between the two is what keeps growth from ever reading as damage: records
        /// that each fit this are each read, however many a merge brings together, and only the
        /// next write, which names them all, has to fit again.
        /// </remarks>
        public const int MaxWritten = 262144;

        /// <summary>
        /// The most items a summary entry the plugin writes holds, `STORE.md` §611 and §643: every
        /// item name, `KPG.S.` and four digits, from `KPG.S.0001`.
        /// </summary>
        public const int MaxItems = 9999;

        /// <summary>
        /// The most store keys a reading works under, `STORE.md` §649: past it the store is split,
        /// and no version is checked under a key not its own.
        /// </summary>
        /// <remarks>
        /// **A key is added to a database rarely, and never taken away.** A Recreate carries the
        /// key and a Create after a Delete keeps it, `STORE.md` §632; what adds one is a store
        /// started again, which sets every earlier key aside and leaves it in the file, and copies
        /// that each made a first store before they met. So a database the operator keeps holds a
        /// handful over its whole life, and this is several times that. What it rules out is the
        /// file that brings a key for each of thousands of entries, which anybody can make, since
        /// a key is found by passing under itself: the work the reading does under each key is a
        /// check of every version of every candidate and a look at every pair of keys, and past
        /// this the reading does neither, answers that several keys stand, which serves nothing,
        /// and leaves out of every route every entry that carries a string of the store.
        /// </remarks>
        public const int MaxKeys = 32;

        /// <summary>The highest number bytes 9 and 10 of an identifier can hold, `STORE.md` §609.</summary>
        public const int MaxNumber = 65535;

        /// <summary>The name of the plugin's group, `STORE.md` §606. Nothing reads it.</summary>
        public const string GroupName = "KeePassGate";

        /// <summary>The notes of the plugin's group, `STORE.md` §606. Nothing reads them.</summary>
        public const string GroupNotes = "KeePassGate data.";

        /// <summary>The notes of every record's entry, `STORE.md` §607 (S14). Nothing reads them.</summary>
        public const string RecordNotes =
            "KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing.";

        /// <summary>
        /// The line the notes of an entry that carries the store key begin with, on a line of its
        /// own, `STORE.md` §607 (S14).
        /// </summary>
        public const string KeyNotes = "Do not delete this entry. It holds the key of this database's KeePassGate store.";

        /// <summary>What the title of an entry that carries the store key ends with, `STORE.md` §607 (S14).</summary>
        public const string KeyTitleEnd = " (do not delete)";

        /// <summary>The random bytes of the second half of the plugin's group's identifier, `STORE.md` §606.</summary>
        private const int GroupRandomLength = 8;

        /// <summary>
        /// The first eight bytes of the plugin's group's identifier, the ASCII of `KPG/STOR`, as
        /// measured, `STORE.md` §606.
        /// </summary>
        private static readonly byte[] GroupPrefix = Encoding.ASCII.GetBytes("KPG/STOR");

        /// <summary>
        /// A record's nonce, `NonceLength` random bytes as lowercase hexadecimal, `STORE.md` §607:
        /// from the generator the gate key is drawn from (S2).
        /// </summary>
        internal static string Nonce()
        {
            var bytes = Random(NonceLength);
            try
            {
                return Hex.Of(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// A new store key, from the generator the gate key is drawn from (S2), under which the
        /// tags of all kinds of the format differ, `STORE.md` §632.
        /// </summary>
        internal static byte[] NewKey()
        {
            return NewKey(() => Random(KeyLength));
        }

        /// <summary>
        /// The first key this draw gives under which the tags of all kinds of the format differ,
        /// every key drawn before it overwritten.
        /// </summary>
        /// <remarks>
        /// Where two kinds' tags meet under a key, a deletion record of the first reads as a
        /// deletion record of the second, and a deletion of an admission the plugin makes would
        /// read as a revocation of its client. Two tags of the first format meet about once in
        /// 300,000 draws, so the loop rarely runs twice.
        /// </remarks>
        internal static byte[] NewKey(Func<byte[]> draw)
        {
            while (true)
            {
                var key = draw();
                if (TagsApart(key))
                {
                    return key;
                }

                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>Whether the tags of all kinds of the format differ under this key.</summary>
        private static bool TagsApart(byte[] key)
        {
            var tags = new HashSet<int>();
            return StoreKinds.All.All(kind => tags.Add(TagNumber(Tag(key, kind.Name))));
        }

        /// <summary>
        /// The identifier of a new group of the plugin's own: the prefix, then random bytes drawn
        /// when the group is made, `STORE.md` §606 (S2).
        /// </summary>
        internal static byte[] NewGroupIdentifier()
        {
            var identifier = new byte[GroupPrefix.Length + GroupRandomLength];
            Buffer.BlockCopy(GroupPrefix, 0, identifier, 0, GroupPrefix.Length);
            var random = Random(GroupRandomLength);
            Buffer.BlockCopy(random, 0, identifier, GroupPrefix.Length, GroupRandomLength);
            return identifier;
        }

        private static byte[] Random(int length)
        {
            var bytes = new byte[length];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            return bytes;
        }

        /// <summary>Whether this is the identifier of the plugin's own group.</summary>
        public static bool IsGroupIdentifier(byte[] identifier)
        {
            if (identifier == null || identifier.Length <= GroupPrefix.Length)
            {
                return false;
            }

            for (var index = 0; index < GroupPrefix.Length; index++)
            {
                if (identifier[index] != GroupPrefix[index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a string of this name belongs to the store, byte for byte.</summary>
        public static bool IsField(string name)
        {
            return name != null && name.StartsWith(FieldPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The bytes an identifier of this kind begins with under this key: the first three of
        /// HMAC-SHA256 over `tag`, a line feed and the kind.
        /// </summary>
        public static byte[] Tag(byte[] key, string kind)
        {
            return Keyed(key, "tag\n" + kind, TagLength);
        }

        /// <summary>The first three bytes of an identifier, or of a tag, as a number.</summary>
        public static int TagNumber(byte[] bytes)
        {
            return bytes == null || bytes.Length < TagLength ? -1 : (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
        }

        /// <summary>
        /// The bytes of an identifier that name what a record is about under this key: the first
        /// five of HMAC-SHA256 over `subject`, a line feed and the subject, `STORE.md` §609.
        /// </summary>
        public static byte[] Subject(byte[] key, string subject)
        {
            return Keyed(key, "subject\n" + subject, SubjectLength);
        }

        /// <summary>
        /// The name of a store key, `STORE.md` §649: HMAC-SHA256 under that key over the three
        /// characters `key`, as 64 lowercase hexadecimal characters.
        /// </summary>
        /// <remarks>
        /// That text holds no line feed, and every other text the store puts under a key holds
        /// one, so a name is no tag, no subject hash and no check. Nobody computes it who has not
        /// held the key, so a file from somebody who does not hold a key cannot name it.
        /// </remarks>
        public static string KeyName(byte[] key)
        {
            return Hex.Of(Keyed(key, "key", 32));
        }

        /// <summary>
        /// The identifier a record's content gives, `STORE.md` §609: the tag, the keyed hash of
        /// the subject, a number, and the start of the check.
        /// </summary>
        /// <param name="number">
        /// The height for a summary entry, the format number for a version record, and 0 for every
        /// other kind: big-endian in bytes 9 and 10.
        /// </param>
        internal static byte[] Identifier(byte[] key, string kind, string subject, int number, string check)
        {
            var identifier = new byte[TagLength + SubjectLength + HeightLength + CheckLength];

            Buffer.BlockCopy(Tag(key, kind), 0, identifier, 0, TagLength);
            Buffer.BlockCopy(Subject(key, subject), 0, identifier, TagLength, SubjectLength);
            identifier[TagLength + SubjectLength] = (byte)(number >> 8);
            identifier[TagLength + SubjectLength + 1] = (byte)number;

            for (var index = 0; index < CheckLength; index++)
            {
                identifier[TagLength + SubjectLength + HeightLength + index] =
                    Convert.ToByte(check.Substring(index * 2, 2), 16);
            }

            return identifier;
        }

        /// <summary>
        /// The identifiers a list names, `STORE.md` §607: identifiers of 32 lowercase hexadecimal
        /// characters, separated by single spaces, or nothing at all; null for any other text.
        /// </summary>
        public static IReadOnlyList<string> Identifiers(string text)
        {
            if (text == null)
            {
                return null;
            }

            if (text.Length == 0)
            {
                return Array.Empty<string>();
            }

            var length = (TagLength + SubjectLength + HeightLength + CheckLength) * 2;
            if ((text.Length + 1) % (length + 1) != 0)
            {
                return null;
            }

            var identifiers = new List<string>((text.Length + 1) / (length + 1));
            for (var at = 0; at < text.Length; at += length + 1)
            {
                if (at + length < text.Length && text[at + length] != ' ')
                {
                    return null;
                }

                for (var index = at; index < at + length; index++)
                {
                    var character = text[index];
                    if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    {
                        return null;
                    }
                }

                identifiers.Add(text.Substring(at, length));
            }

            return identifiers;
        }

        private static byte[] Keyed(byte[] key, string text, int length)
        {
            using (var hmac = new HMACSHA256(key))
            {
                var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(text));
                var start = new byte[length];
                Buffer.BlockCopy(digest, 0, start, 0, length);
                return start;
            }
        }
    }
}
