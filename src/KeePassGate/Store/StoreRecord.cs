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
    /// A record composed for the store, `STORE.md` §607 to §609: its content, its items, its check,
    /// its identifier, and the texts its entry carries for a person to read.
    /// </summary>
    /// <remarks>
    /// **Composed as the measured code composed a record**, `probe/store.py` on
    /// `spikes/storage-survival`: the members sorted by name in ordinal order and written with no
    /// white space, every record holding a nonce, the items of a summary entry each an object with
    /// its members sorted, numbered in the ordinal order of their text from `KPG.S.0001`. The
    /// product writes no member `v`, `STORE.md` §607 (S1).
    ///
    /// **Read back before it is handed out.** A record is refused unless the reading takes it for
    /// what it was composed as: a record that reads a single way, as its kind, passing under the key at
    /// the identifier its content gives. So no member reaches a database holding a character the
    /// content's reader refuses, which would be damage in every copy the record reached.
    ///
    /// **It holds no key.** The key is what it is composed under, and goes with the call.
    /// </remarks>
    internal sealed class StoreRecord
    {
        private StoreRecord()
        {
        }

        /// <summary>The kind of record, `KPG.Kind`.</summary>
        public string Kind { get; private set; }

        /// <summary>The members of its content, as composed, the nonce among them.</summary>
        public IReadOnlyDictionary<string, string> Members { get; private set; }

        /// <summary>The content, `KPG.Data`.</summary>
        public string Content { get; private set; }

        /// <summary>The check, `KPG.Mac`, as lowercase hexadecimal.</summary>
        public string Check { get; private set; }

        /// <summary>The identifier its content gives under the key, `STORE.md` §609.</summary>
        public byte[] Identifier { get; private set; }

        /// <summary>The identifier as lowercase hexadecimal.</summary>
        public string Hex { get; private set; }

        /// <summary>Every string of the store its entry holds, by name, in ordinal order.</summary>
        public IReadOnlyDictionary<string, string> Strings { get; private set; }

        /// <summary>
        /// Whether its content holds the store key: an authority, or a retirement that holds the
        /// key, whose content is protected and whose entry carries the mark, `STORE.md` §607.
        /// </summary>
        public bool Protected { get; private set; }

        /// <summary>The entry's title, `STORE.md` §607 (S14). Nothing reads it.</summary>
        public string Title { get; private set; }

        /// <summary>The entry's notes, `STORE.md` §607 (S14). Nothing reads them.</summary>
        public string Notes { get; private set; }

        /// <summary>
        /// The value of a member of its content, or null.
        /// </summary>
        public string Member(string name)
        {
            string value;
            return Members.TryGetValue(name, out value) ? value : null;
        }

        /// <summary>
        /// A record of this kind under this key, with these members and, for a summary entry,
        /// these items, each an item's members with its `kind`.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// A kind the plugin does not know, or members that do not read back as that kind.
        /// </exception>
        public static StoreRecord Compose(
            byte[] key,
            string kind,
            IReadOnlyDictionary<string, string> members,
            IEnumerable<IReadOnlyDictionary<string, string>> items = null)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            var row = StoreKinds.Find(kind);
            if (row == null)
            {
                throw new ArgumentException("A record of a kind the plugin does not know is never composed.", nameof(kind));
            }

            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                sorted[member.Key] = member.Value;
            }

            if (!sorted.ContainsKey(StoreKinds.Nonce))
            {
                sorted[StoreKinds.Nonce] = StoreFormat.Nonce();
            }

            var content = Json(sorted);
            var strings = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreFormat.KindField, kind },
                { StoreFormat.DataField, content },
            };

            var texts = (items ?? Enumerable.Empty<IReadOnlyDictionary<string, string>>())
                .Select(item => Json(new SortedDictionary<string, string>(item.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)))
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
            for (var index = 0; index < texts.Count; index++)
            {
                strings[StoreFormat.ItemPrefix + (index + 1).ToString("D" + StoreFormat.ItemDigits, CultureInfo.InvariantCulture)] = texts[index];
            }

            var check = KeePassGate.Hex.Of(Mac(key, strings));
            strings[StoreFormat.CheckField] = check;

            var subject = row.SubjectPrefix + (row.SubjectMember == null ? string.Empty : Value(sorted, row.SubjectMember));
            var number = row.Number == StoreNumber.Height
                ? StoreChecked.NumberOf(Value(sorted, StoreKinds.Height))
                : row.Number == StoreNumber.Format ? StoreChecked.NumberOf(Value(sorted, StoreKinds.FormatMember)) : 0;
            var identifier = StoreFormat.Identifier(key, kind, subject, number, check);

            var record = new StoreRecord
            {
                Kind = kind,
                Members = sorted,
                Content = content,
                Check = check,
                Identifier = identifier,
                Hex = KeePassGate.Hex.Of(identifier),
                Strings = strings,
                Protected = sorted.ContainsKey(StoreKinds.StoreKey),
            };

            record.Title = "KPG " + kind + " " + record.Hex.Substring(0, 8) + (record.Protected ? StoreFormat.KeyTitleEnd : string.Empty);
            record.Notes = record.Protected ? StoreFormat.KeyNotes + "\r\n" + StoreFormat.RecordNotes : StoreFormat.RecordNotes;

            ReadBack(key, record);
            return record;
        }

        /// <summary>The record's strings as a version the reading reads, held as plain text.</summary>
        public StoreVersion Version()
        {
            var names = Strings.Keys.ToList();
            return new StoreVersion(names, names.Select(name => (object)Strings[name]).ToList(), PlainStrings.Instance);
        }

        /// <summary>
        /// Refuses a record the reading would not take for what it was composed as, and lets go of
        /// the form it read, overwriting the key that form found in an authority's or a
        /// retirement's content.
        /// </summary>
        private static void ReadBack(byte[] key, StoreRecord record)
        {
            var form = StoreChecked.Read(record.Version());
            var copy = (byte[])key.Clone();
            try
            {
                var found = new FoundKey(copy);
                if (form.Fails != null || !form.ReadsAsKind || !form.Passes(found, record.Identifier))
                {
                    throw new ArgumentException(
                        "A record of kind " + record.Kind + " was composed that does not read back as a record of its kind: "
                        + (form.Fails ?? "its members are not those its kind reads"));
                }
            }
            finally
            {
                form.Forget();
                Array.Clear(copy, 0, copy.Length);
            }
        }

        private static string Value(IDictionary<string, string> members, string name)
        {
            string value;
            if (!members.TryGetValue(name, out value) || value == null)
            {
                throw new ArgumentException("A record is composed without the member " + name + " its kind reads.");
            }

            return value;
        }

        /// <summary>A flat object of strings, its members in the order given, with no white space.</summary>
        private static string Json(IEnumerable<KeyValuePair<string, string>> members)
        {
            using (var json = new JsonWriter())
            {
                foreach (var member in members)
                {
                    json.Text(member.Key, member.Value ?? string.Empty);
                }

                return json.Done();
            }
        }

        /// <summary>The check of `STORE.md` §608 over a version's strings.</summary>
        private static byte[] Mac(byte[] key, IReadOnlyDictionary<string, string> strings)
        {
            var text = new StringBuilder(strings[StoreFormat.KindField]).Append('\n').Append(strings[StoreFormat.DataField]);
            foreach (var name in strings.Keys.Where(name => name.StartsWith(StoreFormat.ItemPrefix, StringComparison.Ordinal)))
            {
                text.Append('\n').Append(name).Append('\n').Append(strings[name]);
            }

            var bytes = Encoding.UTF8.GetBytes(text.ToString());
            try
            {
                using (var hmac = new HMACSHA256(key))
                {
                    return hmac.ComputeHash(bytes);
                }
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
    }
}
