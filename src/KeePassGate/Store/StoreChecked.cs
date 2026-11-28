using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Transport;

namespace KeePassGate.Store
{
    /// <summary>
    /// An item of a summary entry as the reading takes it: its kind, and the members the core
    /// reads of an item of that kind.
    /// </summary>
    internal sealed class StoreItem
    {
        public StoreItem(string kind, IReadOnlyDictionary<string, string> members)
            : this(kind, members, null, null)
        {
        }

        /// <summary>An item read from a summary entry, with the identifiers its `after` and its `lost` name.</summary>
        public StoreItem(string kind, IReadOnlyDictionary<string, string> members, StoreNames named, StoreNames settles)
        {
            Kind = kind;
            Members = members;
            Named = named;
            Settles = settles;
        }

        public string Kind { get; private set; }

        public IReadOnlyDictionary<string, string> Members { get; private set; }

        /// <summary>
        /// The identifiers its `after` names, read once with the summary entry that holds it; null
        /// for an item of a kind that names none, and for an item a write makes, which nothing
        /// asks.
        /// </summary>
        public StoreNames Named { get; private set; }

        /// <summary>
        /// The identifiers its `lost` names, read once with the summary entry that holds it; null
        /// where it holds no `lost`, and for an item a write makes.
        /// </summary>
        public StoreNames Settles { get; private set; }

        public string Member(string name)
        {
            string value;
            return Members.TryGetValue(name, out value) ? value : null;
        }
    }

    /// <summary>
    /// The checked form of a version, `STORE.md` §620: whether its strings read a single way, what
    /// the core reads of them, and for each key asked whether its check passes under it.
    /// </summary>
    /// <remarks>
    /// **The one thing a gate keeps between two holds**, by the identifier of the entry and the
    /// very objects the version holds. So it keeps nothing of a content beyond what the core
    /// reads of its kind, `STORE.md` §658: an authority's content is read for its `id` and its
    /// store key and nothing else of it is kept, and the store key is held as bytes, which
    /// <see cref="Forget"/> overwrites. The text the check is computed over is held only while a
    /// reading lasts, as bytes, and overwritten at its end. **What was asked is kept by the name
    /// of the key it was asked about**, `StoreFormat.KeyName`, so a form keeps no copy of any key
    /// but the one its own content holds, and an answer is found in a single lookup however many
    /// keys a file holds.
    ///
    /// **Shared between two readings only once it is kept**, and then read and never changed: a
    /// form whose text is gone answers from what was asked, a form that fails answers no without
    /// keeping anything, and a form asked about a key it was not asked about before is read again
    /// as a new form by `StoreReading`. So a reading that overlaps a lapsed one changes nothing
    /// the other reads.
    ///
    /// **Total.** Nothing a file can hold makes this throw: a version that does not read a single
    /// way fails, and a version that reads but not as its kind is no record of it.
    /// </remarks>
    internal sealed class StoreChecked
    {
        private static readonly Dictionary<string, string> NoMembers = new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, Under> _under = new Dictionary<string, Under>(StringComparer.Ordinal);
        private byte[] _text;

        private StoreChecked(StoreVersion version)
        {
            Version = version;
            Members = NoMembers;
            Items = Array.Empty<StoreItem>();
        }

        /// <summary>The version this was read from, which is what it is kept by.</summary>
        public StoreVersion Version { get; private set; }

        /// <summary>
        /// Why the version does not read a single way, which no check passes, or null.
        /// </summary>
        public string Fails { get; private set; }

        public string Kind { get; private set; }

        /// <summary>The row of its kind, or null for a kind the plugin does not know.</summary>
        public StoreKind Row { get; private set; }

        /// <summary>Whether its content holds every member its row names, in its form, `STORE.md` §658.</summary>
        public bool ReadsAsKind { get; private set; }

        /// <summary>
        /// Whether it is of a kind the plugin does not know: a kind no row names, or a version
        /// whose object is of a type the plugin does not know, `STORE.md` §658.
        /// </summary>
        public bool Unknown { get; private set; }

        /// <summary>Whether a summary entry holds an item of a kind or an object the plugin does not know.</summary>
        public bool UnknownItem { get; private set; }

        /// <summary>The members its row names, as the content holds them.</summary>
        public IReadOnlyDictionary<string, string> Members { get; private set; }

        /// <summary>
        /// The identifiers its content's `after` names, read once with the rest of it and kept
        /// with the form, `StoreNames`; null for a kind that names none.
        /// </summary>
        public StoreNames Named { get; private set; }

        /// <summary>
        /// The identifiers its content's `lost` names, read once with the rest of it and kept with
        /// the form; null where it holds no `lost`, as every kind but a version does.
        /// </summary>
        public StoreNames Settles { get; private set; }

        /// <summary>A summary entry's items of the kinds the plugin knows, in order of their names.</summary>
        public IReadOnlyList<StoreItem> Items { get; private set; }

        /// <summary>The store key an authority's or a retirement's content holds, or null.</summary>
        public FoundKey StoreKey { get; private set; }

        /// <summary>
        /// What its identifier's subject is made of, where it reads as its kind, as
        /// `StoreFormat.SubjectOf` makes it from its row.
        /// </summary>
        public string Subject { get; private set; }

        /// <summary>
        /// What bytes 9 and 10 of its identifier hold, where it reads as its kind: the height or
        /// the format number, and 0 for every other record, a version of a profile's rule among
        /// them, whose bytes hold the tag of <see cref="Tagged"/> under each key instead.
        /// </summary>
        public int Number { get; private set; }

        /// <summary>
        /// The text whose tag bytes 9 and 10 of its identifier hold, the profile's name of a
        /// version of that profile's rule, `StoreFormat.SubjectOf`; null for every other record.
        /// </summary>
        public string Tagged { get; private set; }

        public string Check { get; private set; }

        /// <summary>Whether the text is still held, so that a key not asked yet can be.</summary>
        public bool Readable
        {
            get { return _text != null || Fails != null; }
        }

        public string Member(string name)
        {
            string value;
            return Members.TryGetValue(name, out value) ? value : null;
        }

        /// <summary>
        /// The content, read again from the version's own object, or null where the version does
        /// not read as its kind: for what a kind's meaning reads that the checked form does not
        /// keep, as an authority's document, which holds private keys and is kept nowhere as text,
        /// `STORE.md` §614.
        /// </summary>
        /// <remarks>
        /// The very object the form is kept by, so the text read is the text that was checked,
        /// `STORE.md` §620.
        /// </remarks>
        public string Content()
        {
            if (!ReadsAsKind)
            {
                return null;
            }

            for (var index = 0; index < Version.Names.Count; index++)
            {
                if (string.Equals(Version.Names[index], StoreFormat.DataField, StringComparison.Ordinal))
                {
                    return Version.Strings.Read(Version.Values[index]);
                }
            }

            return null;
        }

        /// <summary>
        /// The checked form of a version, read from its strings: the one place the reading reads
        /// a value.
        /// </summary>
        public static StoreChecked Read(StoreVersion version)
        {
            var made = new StoreChecked(version);
            made.Fails = made.ReadStrings();
            return made;
        }

        /// <summary>Whether this was asked about every one of these keys already.</summary>
        public bool Asked(IEnumerable<FoundKey> keys)
        {
            return Fails != null || keys.All(key => _under.ContainsKey(key.Name));
        }

        /// <summary>
        /// Whether its check is the check its content gives under this key, whatever the entry
        /// that holds it: what the API's guard hides a copy by, `STORE.md` §633.
        /// </summary>
        public bool Checks(FoundKey key, byte[] entry)
        {
            return Answer(key, entry).Checks;
        }

        /// <summary>
        /// Whether it passes under this key as the version of this entry, `STORE.md` §621: the
        /// check, and either a kind it reads as under the identifier its content gives, or a kind
        /// the plugin does not know under that kind's tag and the start of the check.
        /// </summary>
        public bool Passes(FoundKey key, byte[] entry)
        {
            return Answer(key, entry).Record;
        }

        /// <summary>
        /// Lets go of the text, overwritten, once a reading is over. What was asked stays.
        /// </summary>
        public void Done()
        {
            if (_text != null)
            {
                Array.Clear(_text, 0, _text.Length);
                _text = null;
            }
        }

        /// <summary>
        /// Overwrites the key this holds, for a form nothing keeps any more. Nothing else of a key
        /// is held: what was asked is kept by the key's name.
        /// </summary>
        public void Forget()
        {
            Done();
            if (StoreKey != null)
            {
                StoreKey.Clear();
            }
        }

        private Under Answer(FoundKey key, byte[] entry)
        {
            if (Fails != null)
            {
                // No check passes, and nothing is kept for it, so a failing form is never changed
                // once it is shared.
                return Under.No;
            }

            Under found;
            if (_under.TryGetValue(key.Name, out found))
            {
                return found;
            }

            if (!Readable)
            {
                // Asked of a form kept from an earlier reading about a key it was not asked about
                // then, which the reading reads again first, `StoreReading`: answering here would
                // be answering without the text.
                throw new InvalidOperationException("a kept form was asked about a key without its text");
            }

            var checks = string.Equals(Hex.Of(Mac(key.Bytes, _text)), Check, StringComparison.Ordinal);
            found = new Under(checks, checks && Placed(key.Bytes, entry));
            _under.Add(key.Name, found);
            return found;
        }

        /// <summary>Whether the entry sits under the identifier this version's content gives.</summary>
        private bool Placed(byte[] key, byte[] entry)
        {
            if (entry == null || entry.Length != StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength + StoreFormat.CheckLength)
            {
                return false;
            }

            if (ReadsAsKind)
            {
                var number = Tagged == null ? Number : StoreFormat.ProfileTag(key, Tagged);
                return Same(StoreFormat.Identifier(key, Kind, Subject, number, Check), entry);
            }

            if (!Unknown)
            {
                return false;
            }

            // A kind only a later plugin knows: what it is about is known only to that plugin,
            // so the tag and the check are what is held, `STORE.md` §621.
            var tag = StoreFormat.Tag(key, Kind);
            for (var index = 0; index < StoreFormat.TagLength; index++)
            {
                if (tag[index] != entry[index])
                {
                    return false;
                }
            }

            var start = StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength;
            for (var index = 0; index < StoreFormat.CheckLength; index++)
            {
                if (Convert.ToByte(Check.Substring(index * 2, 2), 16) != entry[start + index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reads the version's strings, `STORE.md` §607 and §608, and answers why it fails, or
        /// null where it reads a single way.
        /// </summary>
        private string ReadStrings()
        {
            object kind = null;
            object data = null;
            object check = null;
            var items = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var names = Version.Names;

            for (var index = 0; index < names.Count; index++)
            {
                var name = names[index];
                if (string.Equals(name, StoreFormat.KindField, StringComparison.Ordinal))
                {
                    kind = Version.Values[index];
                }
                else if (string.Equals(name, StoreFormat.DataField, StringComparison.Ordinal))
                {
                    data = Version.Values[index];
                }
                else if (string.Equals(name, StoreFormat.CheckField, StringComparison.Ordinal))
                {
                    check = Version.Values[index];
                }
                else if (name != null && name.StartsWith(StoreFormat.ItemPrefix, StringComparison.Ordinal))
                {
                    if (!IsItemName(name))
                    {
                        return "a string named " + StoreFormat.ItemPrefix + " and something other than four digits";
                    }

                    items[name] = Version.Values[index];
                }
            }

            if (kind == null || data == null || check == null)
            {
                return "a string of the store is missing";
            }

            // Asked of its length before anything of it is read, so that a string beyond the
            // ceiling costs the reading nothing: it is never hashed and never parsed.
            if (Long(kind) || Long(data) || Long(check))
            {
                return "a string longer than the reader takes";
            }

            if (items.Values.Any(Long))
            {
                return "a string longer than the reader takes";
            }

            Kind = Version.Strings.Read(kind) ?? string.Empty;
            var content = Version.Strings.Read(data) ?? string.Empty;
            Check = Version.Strings.Read(check) ?? string.Empty;

            if (Kind.IndexOf('\n') >= 0 || content.IndexOf('\n') >= 0)
            {
                return "a line feed in a part of the check";
            }

            var members = Content(content);
            if (members == null)
            {
                return "a content that is not one flat object of strings";
            }

            var text = new StringBuilder(Kind).Append('\n').Append(content);
            var read = new List<KeyValuePair<string, JsonBody>>(items.Count);
            foreach (var item in items)
            {
                var value = Version.Strings.Read(item.Value) ?? string.Empty;
                if (value.IndexOf('\n') >= 0)
                {
                    return "a line feed in a part of the check";
                }

                var parsed = Content(value);
                if (parsed == null)
                {
                    return "an item that is not one flat object of strings";
                }

                read.Add(new KeyValuePair<string, JsonBody>(item.Key, parsed));
                text.Append('\n').Append(item.Key).Append('\n').Append(value);
            }

            _text = Encoding.UTF8.GetBytes(text.ToString());
            Interpret(members, read);
            return null;
        }

        private bool Long(object value)
        {
            return Version.Strings.Length(value) > StoreFormat.MaxStringLength;
        }

        /// <summary>What the core reads of a version that reads a single way, by its kind's row.</summary>
        private void Interpret(JsonBody content, IReadOnlyList<KeyValuePair<string, JsonBody>> items)
        {
            Row = StoreKinds.Find(Kind);
            if (Row == null)
            {
                Unknown = true;
                return;
            }

            StoreNames named;
            StoreNames settles;
            var members = Read(content, Row.Members, out named, out settles);
            if (members == null)
            {
                return;
            }

            if (Row.Name == StoreKinds.Version && !KnownObject(members[StoreKinds.ObjectMember]))
            {
                // A kind only a later plugin knows, by the type of what it is a version of.
                Unknown = true;
                return;
            }

            if (Row.Name == StoreKinds.Head && !ReadItems(items))
            {
                return;
            }

            if (Row.Number == StoreNumber.Height)
            {
                Number = NumberOf(members[StoreKinds.Height]);
            }
            else if (Row.Number == StoreNumber.Format)
            {
                Number = NumberOf(members[StoreKinds.FormatMember]);
            }

            // The key as bytes, which `Forget` overwrites, and not as the text it was read from,
            // which nothing can: a store key is kept as nothing else between two holds.
            string stored;
            if (members.TryGetValue(StoreKinds.StoreKey, out stored))
            {
                StoreKey = new FoundKey(Bytes(stored));
                members.Remove(StoreKinds.StoreKey);
            }

            Members = members;
            Named = named;
            Settles = settles;
            string tagged;
            Subject = StoreFormat.SubjectOf(Row, Row.SubjectMember == null ? null : members[Row.SubjectMember], out tagged);
            Tagged = tagged;
            ReadsAsKind = true;
        }

        /// <summary>
        /// A summary entry's items, each of a kind the plugin knows read in its form, and exactly a
        /// single item `format`, `STORE.md` §658. False where the summary entry does not read as
        /// its kind.
        /// </summary>
        private bool ReadItems(IReadOnlyList<KeyValuePair<string, JsonBody>> items)
        {
            var known = new List<StoreItem>(items.Count);
            var formats = 0;

            foreach (var pair in items)
            {
                var kind = pair.Value.Text(StoreKinds.ItemKind);
                if (kind == null)
                {
                    return false;
                }

                var row = StoreKinds.Find(kind);
                if (row == null || row.Item == null)
                {
                    // An item no row of this plugin's restates by: of a kind only a later plugin
                    // knows, which stops the gate as a record of such a kind does, `STORE.md` §611.
                    UnknownItem = true;
                    continue;
                }

                // An item of a decision that names no subject is read in its kind's form with no
                // subject, `STORE.md` §611; an item that holds its subject is read by that form
                // alone, whatever else it holds.
                StoreNames named;
                StoreNames settles;
                var members = row.Unnamed != null && pair.Value.Text(row.RestatedBy) == null
                    ? Read(pair.Value, row.Unnamed, out named, out settles)
                    : Read(pair.Value, row.Item, out named, out settles);
                if (members == null)
                {
                    return false;
                }

                if (row.Name == StoreKinds.Version && !KnownObject(members[StoreKinds.ObjectMember]))
                {
                    UnknownItem = true;
                    continue;
                }

                if (row.Name == StoreKinds.Format)
                {
                    formats++;
                }

                known.Add(new StoreItem(kind, members, named, settles));
            }

            if (formats != 1)
            {
                return false;
            }

            Items = known;
            return true;
        }

        /// <summary>
        /// The members a row names, each in its form, or null where any is missing or out of form.
        /// A member the row does not name is passed over, `STORE.md` §607.
        /// </summary>
        /// <param name="named">
        /// The identifiers `after` names, read as its form is asked, or null where the row names
        /// no `after`.
        /// </param>
        /// <param name="settles">
        /// The identifiers `lost` names, read as its form is asked, or null where the content holds
        /// no `lost`: the two members of the form of a list a row names.
        /// </param>
        private static Dictionary<string, string> Read(JsonBody content, IReadOnlyList<StoreMember> wanted, out StoreNames named, out StoreNames settles)
        {
            named = null;
            settles = null;
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in wanted)
            {
                var value = content.Text(member.Name);
                if (value == null)
                {
                    if (member.Optional)
                    {
                        continue;
                    }

                    return null;
                }

                if (member.Form == StoreForm.Identifiers)
                {
                    var listed = StoreNames.Read(value);
                    if (listed == null)
                    {
                        return null;
                    }

                    if (member.Name == StoreKinds.Lost)
                    {
                        settles = listed;
                    }
                    else
                    {
                        named = listed;
                    }
                }
                else if (!InForm(value, member.Form))
                {
                    return null;
                }

                members[member.Name] = value;
            }

            return members;
        }

        private static bool KnownObject(string value)
        {
            var end = value.IndexOf(StoreKinds.ObjectTypeEnd);
            if (end < 0)
            {
                return false;
            }

            return StoreKinds.ObjectTypes.Contains(value.Substring(0, end), StringComparer.Ordinal);
        }

        internal static bool InForm(string value, StoreForm form)
        {
            switch (form)
            {
                case StoreForm.Text:
                    return true;
                case StoreForm.Hex:
                case StoreForm.KeyName:
                    return IsHex(value, StoreFormat.KeyLength * 2);
                case StoreForm.Identifiers:
                    return StoreFormat.Identifiers(value) != null;
                case StoreForm.Number:
                    return NumberOf(value) > 0;
                case StoreForm.Version:
                    return IsVersion(value);
                case StoreForm.TagAndSubject:
                    return IsHex(value, (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2);
                case StoreForm.Identifier:
                    return IsHex(value, (StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength + StoreFormat.CheckLength) * 2);
                default:
                    return false;
            }
        }

        /// <summary>
        /// A whole number from 1 to 65,535 in decimal, with no sign and no leading zero, or 0 for
        /// any other text.
        /// </summary>
        internal static int NumberOf(string text)
        {
            int number;
            return Whole(text, 1, out number) ? number : 0;
        }

        private static bool IsVersion(string text)
        {
            var parts = text.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            foreach (var part in parts)
            {
                int ignored;
                if (!Whole(part, 0, out ignored))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Whole(string text, int least, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(text) || text.Length > 5 || (text.Length > 1 && text[0] == '0'))
            {
                return false;
            }

            if (!text.All(Ascii.IsDigit))
            {
                return false;
            }

            number = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            return number >= least && number <= StoreFormat.MaxNumber;
        }

        internal static bool IsHex(string text, int length)
        {
            if (text == null || text.Length != length)
            {
                return false;
            }

            return text.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
        }

        private static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static bool IsItemName(string name)
        {
            if (name.Length != StoreFormat.ItemPrefix.Length + StoreFormat.ItemDigits)
            {
                return false;
            }

            for (var index = StoreFormat.ItemPrefix.Length; index < name.Length; index++)
            {
                if (!Ascii.IsDigit(name[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A content read strictly, `STORE.md` §607, or null: the reader the wire's bodies are
        /// read with, which refuses a member named twice and more than its ceiling of members,
        /// and every value a string.
        /// </summary>
        private static JsonBody Content(string text)
        {
            var content = JsonBody.Read(text);
            if (content == null)
            {
                return null;
            }

            foreach (var name in content.Names)
            {
                if (content.Text(name) == null)
                {
                    return null;
                }
            }

            return content;
        }

        private static byte[] Mac(byte[] key, byte[] text)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(text);
            }
        }

        private static bool Same(byte[] one, byte[] other)
        {
            if (one == null || other == null || one.Length != other.Length)
            {
                return false;
            }

            var differ = 0;
            for (var index = 0; index < one.Length; index++)
            {
                differ |= one[index] ^ other[index];
            }

            return differ == 0;
        }

        /// <summary>What was answered for a key.</summary>
        private sealed class Under
        {
            public static readonly Under No = new Under(false, false);

            public Under(bool checks, bool record)
            {
                Checks = checks;
                Record = record;
            }

            public bool Checks { get; private set; }

            public bool Record { get; private set; }
        }
    }
}
