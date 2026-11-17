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
    /// **The smallest part of the store's reading, and the part the API's guard rests on**,
    /// `STORE.md` §633. It finds the store keys, checks a version and composes an identifier as
    /// §608, §609 and §621 state them, and decides nothing about what is in force.
    ///
    /// **Every format here is the form the store was measured with**, as `probe/store.py` on
    /// `spikes/storage-survival` wrote it, and several are not settled: the prefix's bytes, the
    /// kinds, and how many bytes of the check an identifier holds, `STORE.md` §646. Each is
    /// written once, here, so that settling it is a change to this class and nothing else.
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

        /// <summary>The bytes of a summary entry's height, zero for every other kind.</summary>
        public const int HeightLength = 2;

        /// <summary>
        /// The bytes of the check an identifier holds, as measured, which S31 leaves open.
        /// </summary>
        public const int CheckLength = 6;

        /// <summary>The digits of an item's number, `STORE.md` §608.</summary>
        private const int ItemDigits = 4;

        /// <summary>
        /// A store key, written as 64 hexadecimal characters in the content that holds it.
        /// </summary>
        private const int KeyLength = 32;

        /// <summary>
        /// The member of an authority's or a retirement's content that holds the store key.
        /// </summary>
        private const string KeyMember = "storeKey";

        private const string AuthorityKind = "authority";

        private const string RetiredKind = "retired";

        /// <summary>
        /// The first eight bytes of the plugin's group's identifier, the ASCII of `KPG/STOR`, as
        /// measured, `STORE.md` §606.
        /// </summary>
        private static readonly byte[] GroupPrefix = Encoding.ASCII.GetBytes("KPG/STOR");

        /// <summary>
        /// The kinds of record the store was measured with, `STORE.md` §607.
        /// </summary>
        /// <remarks>
        /// The kinds a later part of the store adds have no name yet, S23, and each is added here
        /// as it is named, since a kind missing from this list is a record its tag does not tell.
        /// </remarks>
        public static readonly IReadOnlyList<string> Kinds = Array.AsReadOnly(new[]
        {
            AuthorityKind, RetiredKind, "revoked", "client", "profile", "removed", "version", "head",
        });

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

        /// <summary>
        /// Whether a version's check is the check its content gives under this key, whatever the
        /// identifier of the entry holding it.
        /// </summary>
        /// <param name="version">
        /// The version's strings whose names begin with the prefix, by name.
        /// </param>
        public static bool Passes(byte[] key, IReadOnlyDictionary<string, string> version)
        {
            var check = Field(version, CheckField);
            var computed = Check(key, version);
            return computed != null && string.Equals(computed, check, StringComparison.Ordinal);
        }

        /// <summary>
        /// The store key a version gives, or null for a version that gives none.
        /// </summary>
        /// <remarks>
        /// `STORE.md` §621: a version of an authority or of a retirement whose content holds a key,
        /// that passes under that very key, held by the entry whose identifier its content gives
        /// under that key. So a copy gives none, and neither does a version whose check another
        /// key made.
        /// </remarks>
        /// <param name="identifier">The identifier of the entry holding the version.</param>
        public static byte[] KeyOf(byte[] identifier, IReadOnlyDictionary<string, string> version)
        {
            var kind = Field(version, KindField);
            if (!string.Equals(kind, AuthorityKind, StringComparison.Ordinal)
                && !string.Equals(kind, RetiredKind, StringComparison.Ordinal))
            {
                return null;
            }

            var content = Content(Field(version, DataField));
            var key = content == null ? null : KeyFrom(content.Text(KeyMember));
            var subject = content == null
                ? null
                : content.Text(string.Equals(kind, AuthorityKind, StringComparison.Ordinal) ? "id" : "authority");

            if (key == null || subject == null || !Passes(key, version))
            {
                return null;
            }

            var expected = Identifier(key, kind, "authority:" + subject, 0, Field(version, CheckField));
            return identifier != null && expected.SequenceEqual(identifier) ? key : null;
        }

        /// <summary>
        /// The identifier a record's content gives, `STORE.md` §609: the tag, the keyed hash of
        /// the subject, the height, and the start of the check.
        /// </summary>
        internal static byte[] Identifier(byte[] key, string kind, string subject, int height, string check)
        {
            var identifier = new byte[TagLength + SubjectLength + HeightLength + CheckLength];

            Buffer.BlockCopy(Tag(key, kind), 0, identifier, 0, TagLength);
            Buffer.BlockCopy(Keyed(key, "subject\n" + subject, SubjectLength), 0, identifier, TagLength, SubjectLength);
            identifier[TagLength + SubjectLength] = (byte)(height >> 8);
            identifier[TagLength + SubjectLength + 1] = (byte)height;

            for (var index = 0; index < CheckLength; index++)
            {
                identifier[TagLength + SubjectLength + HeightLength + index] =
                    Convert.ToByte(check.Substring(index * 2, 2), 16);
            }

            return identifier;
        }

        /// <summary>
        /// The check a version's content gives under this key, as lowercase hexadecimal, or null
        /// for a version that does not read a single way.
        /// </summary>
        /// <remarks>
        /// `STORE.md` §608: the kind, a line feed and the content, then a line feed, the name, a
        /// line feed and the value of each item, in ordinal order of the names. A part holding a
        /// line feed, a string beginning `KPG.S.` that is not named with exactly four digits, and
        /// a content or an item that is not one flat object of strings each make a version that
        /// fails, since two sets of strings could otherwise give one text, and two readers two
        /// contents.
        /// </remarks>
        private static string Check(byte[] key, IReadOnlyDictionary<string, string> version)
        {
            var kind = Field(version, KindField);
            var data = Field(version, DataField);
            var check = Field(version, CheckField);

            if (key == null || kind == null || check == null || kind.IndexOf('\n') >= 0 || !Readable(data))
            {
                return null;
            }

            var text = new StringBuilder(kind).Append('\n').Append(data);
            var items = version.Keys
                .Where(name => name.StartsWith(ItemPrefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal);

            foreach (var name in items)
            {
                var value = version[name];
                if (!IsItemName(name) || !Readable(value))
                {
                    return null;
                }

                text.Append('\n').Append(name).Append('\n').Append(value);
            }

            return Hex.Of(Keyed(key, text.ToString(), 32));
        }

        private static bool IsItemName(string name)
        {
            if (name.Length != ItemPrefix.Length + ItemDigits)
            {
                return false;
            }

            for (var index = ItemPrefix.Length; index < name.Length; index++)
            {
                if (!Ascii.IsDigit(name[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a part is JSON written with no line feed, one flat object of strings.
        /// </summary>
        private static bool Readable(string part)
        {
            return part != null && part.IndexOf('\n') < 0 && Content(part) != null;
        }

        /// <summary>
        /// A content read strictly, `STORE.md` §607, or null: the reader the wire's bodies are
        /// read with, which refuses a member named twice, and every value a string.
        /// </summary>
        private static JsonBody Content(string text)
        {
            var content = text == null ? null : JsonBody.Read(text);
            return content == null || content.Names.Any(name => content.Text(name) == null) ? null : content;
        }

        private static byte[] KeyFrom(string hex)
        {
            if (hex == null || hex.Length != KeyLength * 2 || !hex.All(Ascii.IsHexDigit))
            {
                return null;
            }

            var key = new byte[KeyLength];
            for (var index = 0; index < KeyLength; index++)
            {
                key[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return key;
        }

        private static string Field(IReadOnlyDictionary<string, string> version, string name)
        {
            string value;
            return version != null && version.TryGetValue(name, out value) ? value : null;
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
