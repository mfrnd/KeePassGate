using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KeePassGate.Testing
{
    /// <summary>
    /// A record of the plugin's store as a test means it: its identifier and its strings, made
    /// with an HMAC of this file's own.
    /// </summary>
    /// <remarks>
    /// **Composed here rather than by the plugin**, so that the reading is never held to a record
    /// only its own code could have made: two halves written on one side could agree on a form
    /// nobody measured. What holds this composer to the measured code is `StoreFormatTests`, whose
    /// vectors are the probe's own output, made by
    /// `spikes/storage/probes/store-as-a-whole/vectors.py` on `spikes/storage-survival`, committed
    /// there at `05c8cc64`, from `probe/store.py` beside it.
    ///
    /// **Without a member `v`**, which the product writes nowhere, `STORE.md` §607, and every
    /// content's members sorted by name, as the measured code wrote them, with a nonce drawn at
    /// random unless one is given.
    ///
    /// No KeePass type, so the unit tier makes versions of these and the host tier entries.
    /// </remarks>
    public static class StoreComposer
    {
        public const string Kind = "KPG.Kind";
        public const string Data = "KPG.Data";
        public const string Check = "KPG.Mac";

        /// <summary>A store key made from a label, so that two tests naming one label share it.</summary>
        public static byte[] Key(string label)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("a store key for tests/" + label));
            }
        }

        /// <summary>The key the measured code derived from a label, which the vectors are made under.</summary>
        public static byte[] MeasuredKey(string label)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("kpg-store-as-a-whole/store-key/" + label));
            }
        }

        /// <summary>
        /// A content: the members given and a nonce, sorted by name, written without white space.
        /// </summary>
        public static string Content(params (string Name, string Value)[] members)
        {
            var all = members.ToList();
            if (all.All(member => member.Name != "nonce"))
            {
                all.Add(("nonce", Guid.NewGuid().ToString("N")));
            }

            return Object(all);
        }

        /// <summary>An item of a summary entry: its members, sorted by name, with no nonce.</summary>
        public static string Item(params (string Name, string Value)[] members)
        {
            return Object(members);
        }

        /// <summary>
        /// A record as `STORE.md` §607 to §609 have it: the check over the kind, the content and
        /// the items, and the identifier its content gives under this key.
        /// </summary>
        /// <param name="number">The height of a summary entry, the format number of a version record.</param>
        /// <param name="items">A summary entry's items, numbered from `KPG.S.0001` in the order given.</param>
        public static Composed Record(
            byte[] key, string kind, string subject, string content, int number = 0, IEnumerable<string> items = null)
        {
            var strings = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { Kind, kind },
                { Data, content },
            };

            var index = 0;
            foreach (var item in items ?? Enumerable.Empty<string>())
            {
                index++;
                strings["KPG.S." + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)] = item;
            }

            strings[Check] = CheckOf(key, strings);
            return new Composed(IdentifierOf(key, kind, subject, number, strings[Check]), strings);
        }

        /// <summary>
        /// A record of kind `version` of this object, `STORE.md` §609: for an object `rule:` that
        /// holds `/`, the subject is the object before its first `/` and the number the tag of the
        /// profile's name after it, and for every other object the subject is the object and the
        /// number 0, as the measured code's `ident` makes them.
        /// </summary>
        public static Composed Version(byte[] key, string objectName, string content)
        {
            var slash = objectName.StartsWith("rule:", StringComparison.Ordinal) ? objectName.IndexOf('/') : -1;
            return slash < 0
                ? Record(key, "version", objectName, content)
                : Record(key, "version", objectName.Substring(0, slash), content, ProfileTag(key, objectName.Substring(slash + 1)));
        }

        /// <summary>The object of a profile's row at a node, composed here: `rule:`, the node, `/` and the name.</summary>
        public static string RowObject(string node, string profile)
        {
            return "rule:" + node + "/" + profile;
        }

        /// <summary>The object of a node's switch, composed here: `switch:` and the node.</summary>
        public static string SwitchObject(string node)
        {
            return "switch:" + node;
        }

        /// <summary>
        /// A record of kind `version` of this object, its content holding this text in `doc`, these
        /// identifiers in `after`, the object and a nonce.
        /// </summary>
        /// <param name="after">The identifiers named, separated by single spaces in the order given.</param>
        public static Composed RuleVersion(byte[] key, string objectName, string doc, params string[] after)
        {
            return Version(key, objectName, Content(("after", string.Join(" ", after)), ("doc", doc), ("object", objectName)));
        }

        /// <summary>
        /// A record of kind `version` of this object, its content holding this text in `doc`, these
        /// identifiers in `after`, these in `lost`, the object and a nonce, with no `lost` where it
        /// names nothing, as `STORE.md` §617 has a version that settles lost versions.
        /// </summary>
        /// <param name="after">The identifiers named as replaced, separated by single spaces in the order given.</param>
        /// <param name="lost">The lost identifiers named as settled, separated by single spaces in the order given.</param>
        public static Composed RuleVersion(byte[] key, string objectName, string doc, string[] after, string[] lost)
        {
            return Version(key, objectName, Content(VersionMembers(objectName, doc, after, lost).ToArray()));
        }

        /// <summary>
        /// The item of a summary entry that restates a version of this object, holding this text in
        /// `doc` and these identifiers in `after`.
        /// </summary>
        /// <param name="after">The identifiers named, separated by single spaces in the order given.</param>
        public static string RuleVersionItem(Composed version, string objectName, string doc, params string[] after)
        {
            return Item(
                ("after", string.Join(" ", after)), ("doc", doc), ("id", version.Hex), ("kind", "version"), ("object", objectName));
        }

        /// <summary>
        /// The item of a summary entry that restates a version of this object, holding this text in
        /// `doc`, these identifiers in `after` and these in `lost`, with no `lost` where it names
        /// nothing.
        /// </summary>
        public static string RuleVersionItem(Composed version, string objectName, string doc, string[] after, string[] lost)
        {
            return Item(VersionMembers(objectName, doc, after, lost).Concat(new[] { ("id", version.Hex), ("kind", "version") }).ToArray());
        }

        /// <summary>The members of a version's content but its nonce, `lost` among them only where it names something.</summary>
        private static List<(string Name, string Value)> VersionMembers(string objectName, string doc, string[] after, string[] lost)
        {
            var members = new List<(string Name, string Value)> { ("after", string.Join(" ", after)), ("doc", doc), ("object", objectName) };
            if (lost.Length > 0)
            {
                members.Add(("lost", string.Join(" ", lost)));
            }

            return members;
        }

        /// <summary>
        /// The tag of a profile's name under a key, `STORE.md` §609: the first two bytes of the
        /// HMAC over `name`, a line feed and the name, as a number read big-endian.
        /// </summary>
        public static int ProfileTag(byte[] key, string name)
        {
            var tag = Hmac(key, "name\n" + name);
            return (tag[0] << 8) | tag[1];
        }

        /// <summary>The check of `STORE.md` §608 over these strings, as lowercase hexadecimal.</summary>
        public static string CheckOf(byte[] key, IReadOnlyDictionary<string, string> strings)
        {
            var text = new StringBuilder(strings[Kind]).Append('\n').Append(strings[Data]);
            foreach (var name in strings.Keys.Where(name => name.StartsWith("KPG.S.", StringComparison.Ordinal))
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                text.Append('\n').Append(name).Append('\n').Append(strings[name]);
            }

            return HexOf(Hmac(key, text.ToString()));
        }

        /// <summary>The identifier of `STORE.md` §609.</summary>
        public static byte[] IdentifierOf(byte[] key, string kind, string subject, int number, string check)
        {
            return Tag(key, kind)
                .Concat(Hmac(key, "subject\n" + subject).Take(5))
                .Concat(new[] { (byte)(number >> 8), (byte)number })
                .Concat(BytesOf(check.Substring(0, 12)))
                .ToArray();
        }

        public static byte[] Tag(byte[] key, string kind)
        {
            return Hmac(key, "tag\n" + kind).Take(3).ToArray();
        }

        /// <summary>The name of a store key, `STORE.md` §649.</summary>
        public static string KeyName(byte[] key)
        {
            return HexOf(Hmac(key, "key"));
        }

        public static string HexOf(IEnumerable<byte> bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }

        public static byte[] BytesOf(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2)
                .Select(index => Convert.ToByte(hex.Substring(index * 2, 2), 16))
                .ToArray();
        }

        private static string Object(IEnumerable<(string Name, string Value)> members)
        {
            return Bodies.Of(members
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .Select<(string, string), (string Name, object Value)>(member => (member.Item1, member.Item2))
                .ToArray());
        }

        private static byte[] Hmac(byte[] key, string text)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(text));
            }
        }

        /// <summary>
        /// How many of these keys a reading found, told by what it hides: an identifier that
        /// begins with a key's tag of a summary entry is the store's only under a key found.
        /// </summary>
        internal static int Found(this Store.StoreReading reading, params byte[][] keys)
        {
            return keys.Count(key => reading.Holds(Tag(key, "head").Concat(new byte[13]).ToArray()));
        }

        /// <summary>A record composed: its identifier, and its strings by name.</summary>
        public sealed class Composed
        {
            public Composed(byte[] identifier, SortedDictionary<string, string> strings)
            {
                Identifier = identifier;
                Strings = strings;
            }

            public byte[] Identifier { get; private set; }

            public string Hex
            {
                get { return HexOf(Identifier); }
            }

            public SortedDictionary<string, string> Strings { get; private set; }
        }
    }
}
