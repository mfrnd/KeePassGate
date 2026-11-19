using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What tells a node of the plugin's store by its identifier, held to the code the store's
    /// formats were measured with.
    /// </summary>
    /// <remarks>
    /// **The values are the probe's own**: `probe/store.py` of the family
    /// `spikes/storage/probes/store-as-a-whole` on `spikes/storage-survival`, run over the inputs
    /// written beside them, with each store key derived there as the SHA-256 of
    /// `kpg-store-as-a-whole/store-key/` and a label. The values of the kinds and the subjects
    /// that code never wrote are its `hm`, `tag`, `subject_hash`, `mac` and `ident`, called over
    /// the inputs here by `spikes/storage/probes/store-as-a-whole/vectors.py`, committed on that
    /// branch at `05c8cc64`, which prints every value written here. Regenerate them from that
    /// code and never from this side, since two halves written here could agree on a format
    /// neither measured.
    /// </remarks>
    [TestFixture]
    public sealed class StoreFormatTests
    {
        private const string Nonce = "00112233445566778899aabbccddeeff";

        private static readonly byte[] One = Key("one");
        private static readonly byte[] Two = Key("two");

        private static readonly string AuthorityData =
            "{\"id\":\"" + Repeat("a1") + "\",\"nonce\":\"" + Nonce + "\",\"storeKey\":\""
            + "849b00c169130b902e318a8e483f0a4c4f1ad84821891f7f6d836b863bb9c6a8\",\"v\":\"2\"}";

        private static readonly string RetiredData =
            "{\"authority\":\"" + Repeat("b2") + "\",\"nonce\":\"" + Nonce + "\",\"storeKey\":\""
            + "2f5c61cb7e569ec41c22b06f8d0431df0d67cb4d21034bde1fdeb601f38482ab\",\"v\":\"2\"}";

        private static readonly string RevokedData =
            "{\"client\":\"" + Repeat("c3") + "\",\"nonce\":\"" + Nonce + "\",\"v\":\"2\"}";

        private static readonly string HeadData =
            "{\"after\":\"\",\"height\":\"1\",\"nonce\":\"" + Nonce + "\",\"v\":\"2\"}";

        private static readonly string HeadItem = "{\"client\":\"" + Repeat("c3") + "\",\"kind\":\"revoked\"}";

        private const string FormatItem = "{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.3.0\"}";

        // A tag in hexadecimal that reads as a word to the spelling check.
        // cspell:ignore cabae
        [TestCase("authority", "b8f37c", "38573d")]
        [TestCase("retired", "bcf016", "cabae6")]
        [TestCase("revoked", "923300", "0b3ffb")]
        [TestCase("client", "acf032", "8bc77c")]
        [TestCase("profile", "64017c", "65abf7")]
        [TestCase("removed", "3f3cb0", "06e896")]
        [TestCase("version", "d19b34", "51a20a")]
        [TestCase("head", "e34b4d", "36b469")]
        public void A_kind_s_tag_is_the_one_the_store_was_measured_with(string kind, string underOne, string underTwo)
        {
            Assert.That(Hex.Of(StoreFormat.Tag(One, kind)), Is.EqualTo(underOne));
            Assert.That(Hex.Of(StoreFormat.Tag(Two, kind)), Is.EqualTo(underTwo));
        }

        [Test]
        public void A_new_store_key_is_drawn_again_while_the_tags_of_two_kinds_meet_under_it()
        {
            // `STORE.md` §632. Found by a search run once, over the SHA-256 of `kinds meet ` and a
            // counter: under this key the tags of `retired` and `client` are both `0070fd`.
            var meeting = StoreComposer.BytesOf("3b477ffcedd2a8b8abd61f5591247aeb97230efea6b7e1402d60c8d654ce50e5");
            Assume.That(StoreFormat.Tag(meeting, StoreKinds.Retired), Is.EqualTo(StoreFormat.Tag(meeting, StoreKinds.Client)));

            var draws = new Queue<byte[]>(new[] { (byte[])meeting.Clone(), (byte[])One.Clone() });
            var key = StoreFormat.NewKey(() => draws.Dequeue());

            Assert.That(key, Is.EqualTo(One), "the next draw, under which every kind's tag differs");
            Assert.That(draws, Is.Empty);
            Assert.That(
                StoreKinds.All.Select(kind => Hex.Of(StoreFormat.Tag(One, kind.Name))).Distinct().Count(),
                Is.EqualTo(StoreKinds.All.Count));
        }

        // Kinds the measured code never wrote, `STORE.md` §658, under the measured function.
        [TestCase("withdrawn", "e3e857", "9b775f")]
        [TestCase("kept", "67c05e", "a53262")]
        [TestCase("format", "ac3c9f", "d99f4a")]
        public void A_kind_the_store_was_not_measured_with_has_the_tag_the_measured_function_gives(
            string kind, string underOne, string underTwo)
        {
            Assert.That(StoreKinds.Find(kind), Is.Not.Null, kind + " is a kind of the first format");
            Assert.That(Hex.Of(StoreFormat.Tag(One, kind)), Is.EqualTo(underOne));
            Assert.That(Hex.Of(StoreFormat.Tag(Two, kind)), Is.EqualTo(underTwo));
        }

        [Test]
        public void Every_kind_the_store_was_measured_with_is_a_kind_it_knows()
        {
            // The kinds of the records the measured code wrote, in the order it lists them. A kind
            // the reading does not know cannot be told by its tag, so a kind left out of the
            // table is a record whose identifier hides nothing, and one the reading stops for.
            var known = StoreKinds.All.Select(kind => kind.Name).ToList();

            Assert.That(
                new[] { "authority", "retired", "revoked", "client", "profile", "removed", "version", "head" },
                Is.SubsetOf(known));
            Assert.That(known, Is.Unique);
        }

        [TestCase("authority:a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1", "30b55aab19")]
        [TestCase("client:c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3", "d9141b2a5c")]
        [TestCase("certificate:d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4", "bd9fc6f651")]
        [TestCase("profile:reader", "f2b5a1afa0")]
        [TestCase("store", "9d4d36de78")]
        [TestCase("rule:0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", "5e8d850713")]
        [TestCase("switch:0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", "5bb4ae774b")]
        [TestCase("renew:c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3", "95fddcf328")]
        [TestCase("format:1", "510c21a73d")]
        [TestCase("format:2", "e2439e8ff8")]
        public void A_subject_s_hash_is_the_one_the_measured_function_gives(string subject, string hash)
        {
            Assert.That(Hex.Of(StoreFormat.Subject(One, subject)), Is.EqualTo(hash));
        }

        [TestCase("one", "97ab9bba10be467b10a746654fe243cf965681999a80b3c4056ef2e1c62fd113")]
        [TestCase("two", "f63decdce8d0d81c856a09a2926210e0bb841bb0174d52c7dc1b65a9048c7870")]
        public void A_key_s_name_is_the_measured_function_over_the_text_key(string label, string name)
        {
            Assert.That(StoreFormat.KeyName(Key(label)), Is.EqualTo(name));
        }

        [TestCase(1, "e34b4d9d4d36de780001310b9e458c29", "310b9e458c29c37610e69a5fc65bfdb3c5d969c8a878c0fe3ff0346ac273da2c")]
        [TestCase(258, "e34b4d9d4d36de780102527eaf6e6572", "527eaf6e657286f41ff06548e1d515569dfd73e3398395330a0894243ec77287")]
        [TestCase(65535, "e34b4d9d4d36de78ffff134f28a04c0c", "134f28a04c0c7f456fba99f426390b9e12b8ed81bff4a5504dc361734434538f")]
        public void A_summary_entry_s_height_is_bytes_nine_and_ten_big_endian(int height, string identifier, string check)
        {
            var version = Version(
                "head",
                "{\"after\":\"\",\"height\":\"" + height + "\",\"nonce\":\"" + Nonce + "\"}",
                check);
            version["KPG.S.0001"] = FormatItem;
            version["KPG.S.0002"] = HeadItem;

            Assert.That(StoreVersions.Passes(One, version), Is.True);
            Assert.That(
                Hex.Of(StoreFormat.Identifier(One, "head", "store", height, check)),
                Is.EqualTo(identifier));
        }

        [Test]
        public void A_summary_entry_s_items_are_checked_in_ordinal_order_of_their_names()
        {
            // 0010 before 0002 would be the order of the numbers' values read as text by a
            // culture, and 0100 last either way: the measured code sorts the names by code point.
            var version = Version(
                "head",
                "{\"after\":\"\",\"height\":\"1\",\"nonce\":\"" + Nonce + "\"}",
                "fa79bc057a3361833a50e12dd61cb2fc429025f1444eb2b8a1862d5a62751776");
            foreach (var number in new[] { 1, 2, 10, 11, 100 })
            {
                version["KPG.S." + number.ToString("D4", CultureInfo.InvariantCulture)] =
                    "{\"id\":\"" + number.ToString("x32", CultureInfo.InvariantCulture) + "\",\"kind\":\"removed\",\"name\":\"n" + number + "\"}";
            }

            Assert.That(StoreVersions.Passes(One, version), Is.True);
            Assert.That(
                Hex.Of(StoreFormat.Identifier(One, "head", "store", 1, version["KPG.Mac"])),
                Is.EqualTo("e34b4d9d4d36de780001fa79bc057a33"));
        }

        [TestCase("1", "0.3.0", "ac3c9f510c21a73d0001f3ba811aec97", "f3ba811aec97f92353035e5bd465295735c25b671ff9999f5a567c644bb68e71")]
        [TestCase("2", "0.4.0", "ac3c9fe2439e8ff800027bda409a9719", "7bda409a97196b498dcd2b46ef533f2a40d86d222c610d283596905747e25f9b")]
        public void A_version_record_holds_its_format_number_in_bytes_nine_and_ten(
            string format, string plugin, string identifier, string check)
        {
            var version = Version(
                "format",
                "{\"format\":\"" + format + "\",\"nonce\":\"" + Nonce + "\",\"plugin\":\"" + plugin + "\"}",
                check);

            Assert.That(StoreVersions.Passes(One, version), Is.True);
            Assert.That(
                Hex.Of(StoreFormat.Identifier(One, "format", "format:" + format, int.Parse(format, CultureInfo.InvariantCulture), check)),
                Is.EqualTo(identifier));
        }

        [Test]
        public void A_keep_record_names_a_key_by_its_name()
        {
            var name = "f63decdce8d0d81c856a09a2926210e0bb841bb0174d52c7dc1b65a9048c7870";
            var check = "cb609c4becbe28b77191534bea67f93ba8643423b6e6b303161d41eee3b8c15b";
            var version = Version("kept", "{\"after\":\"\",\"key\":\"" + name + "\",\"nonce\":\"" + Nonce + "\"}", check);

            Assert.That(StoreVersions.Passes(One, version), Is.True);
            Assert.That(
                Hex.Of(StoreFormat.Identifier(One, "kept", "key:" + name, 0, check)),
                Is.EqualTo("67c05e9cb0a5176b0000cb609c4becbe"));
        }

        [Test]
        public void A_certificate_taken_away_is_named_by_its_admission_key()
        {
            var check = "2d3857b66556812b06b99736114919a1095741b8a77760edbdaa113b20206eeb";
            var version = Version(
                "withdrawn", "{\"certificate\":\"" + Repeat("d4") + "\",\"nonce\":\"" + Nonce + "\"}", check);

            Assert.That(StoreVersions.Passes(One, version), Is.True);
            Assert.That(
                Hex.Of(StoreFormat.Identifier(One, "withdrawn", "certificate:" + Repeat("d4"), 0, check)),
                Is.EqualTo("e3e857bd9fc6f65100002d3857b66556"));
        }

        [Test]
        public void The_shared_composer_makes_the_measured_code_s_records()
        {
            // What every test of the reading composes its stores with is held here to the probe's
            // own output, so a record the composer makes is a record the measured code made.
            var composed = StoreComposer.Record(
                One,
                "withdrawn",
                "certificate:" + Repeat("d4"),
                StoreComposer.Content(("certificate", Repeat("d4")), ("nonce", Nonce)));

            Assert.That(composed.Hex, Is.EqualTo("e3e857bd9fc6f65100002d3857b66556"));
            Assert.That(StoreComposer.KeyName(Two), Is.EqualTo(StoreFormat.KeyName(Two)));
        }

        [Test]
        public void An_authority_gives_the_key_its_content_holds_under_its_own_identifier()
        {
            var version = Version("authority", AuthorityData, "9ef5fefced88074299919ee2f8f73ed7599b18c9611689b83811cb6fa3924fc7");

            Assert.That(StoreVersions.KeyOf(Bytes("b8f37c30b55aab1900009ef5fefced88"), version), Is.EqualTo(One));
        }

        [Test]
        public void A_retirement_that_holds_a_key_gives_it_too()
        {
            var version = Version("retired", RetiredData, "435bc059579230b518906c3dfc7f74de14941f1d0dcd4e4683b19e9aa44279e1");

            Assert.That(StoreVersions.KeyOf(Bytes("cabae6ddabf3fd890000435bc0595792"), version), Is.EqualTo(Two));
        }

        [Test]
        public void A_copy_gives_no_key_and_still_passes_under_the_key()
        {
            // The same strings under another identifier, as Duplicate Entry makes them: no source
            // of a key, STORE.md §621, and still a version whose content the key vouches for.
            var version = Version("authority", AuthorityData, "9ef5fefced88074299919ee2f8f73ed7599b18c9611689b83811cb6fa3924fc7");
            var copy = Bytes("0123456789abcdef0123456789abcdef");

            Assert.That(StoreVersions.KeyOf(copy, version), Is.Null);
            Assert.That(StoreVersions.Passes(One, version), Is.True);
            Assert.That(StoreVersions.Passes(Two, version), Is.False);
        }

        [Test]
        public void A_record_of_another_kind_gives_no_key()
        {
            var version = Version("revoked", RevokedData, "e7c13c328e03ff7d70087201f26a3a31824db427f6880671ebd08ff4a6fd66d9");

            Assert.That(StoreVersions.KeyOf(Bytes("923300d9141b2a5c0000e7c13c328e03"), version), Is.Null);
            Assert.That(StoreVersions.Passes(One, version), Is.True);
        }

        [Test]
        public void A_summary_entry_s_items_are_part_of_its_check()
        {
            var version = Version("head", HeadData, "bf2f4bac6006fa78067a1cebc3db7ab4c5f972403d31fd5076d7a84a1f9b5a03");
            version["KPG.S.0001"] = HeadItem;

            Assert.That(StoreVersions.Passes(One, version), Is.True);

            version["KPG.S.0001"] = HeadItem.Replace("c3", "c4");
            Assert.That(StoreVersions.Passes(One, version), Is.False);
        }

        [Test]
        public void An_authority_whose_key_is_not_its_own_gives_none()
        {
            // The key in the content has to be the key the check passes under: an entry that
            // names a key it was not written with vouches for nothing, STORE.md §621.
            var version = Version("authority", AuthorityData, "9ef5fefced88074299919ee2f8f73ed7599b18c9611689b83811cb6fa3924fc7");
            version["KPG.Mac"] = Hex.Of(Hmac(Two, "authority\n" + AuthorityData));

            Assert.That(StoreVersions.KeyOf(Bytes("b8f37c30b55aab1900009ef5fefced88"), version), Is.Null);
        }

        [TestCase("KPG.S.1")]
        [TestCase("KPG.S.00001")]
        [TestCase("KPG.S.000a")]
        [TestCase("KPG.S.")]
        public void A_version_holding_an_item_name_of_another_shape_fails(string name)
        {
            // STORE.md §608: every string beginning `KPG.S.` is named with exactly four digits,
            // or two sets of strings could give one text, and so one check.
            var version = Version("head", HeadData, "bf2f4bac6006fa78067a1cebc3db7ab4c5f972403d31fd5076d7a84a1f9b5a03");
            version["KPG.S.0001"] = HeadItem;
            version[name] = HeadItem;
            version["KPG.Mac"] = Hex.Of(Hmac(One, Text("head", HeadData, version)));

            Assert.That(StoreVersions.Passes(One, version), Is.False);
        }

        [Test]
        public void A_version_with_a_line_feed_in_a_part_fails()
        {
            var data = RevokedData.Replace("\"v\":\"2\"}", "\"v\":\"2\"}\n");
            var version = Version("revoked", data, Hex.Of(Hmac(One, "revoked\n" + data)));

            Assert.That(StoreVersions.Passes(One, version), Is.False);
        }

        [Test]
        public void A_version_naming_a_member_twice_fails()
        {
            // STORE.md §607: two readers could take two contents from one text that passes.
            var data = "{\"client\":\"a\",\"client\":\"b\",\"nonce\":\"" + Nonce + "\",\"v\":\"2\"}";
            var version = Version("revoked", data, Hex.Of(Hmac(One, "revoked\n" + data)));

            Assert.That(StoreVersions.Passes(One, version), Is.False);
        }

        [Test]
        public void A_version_whose_content_is_not_an_object_of_strings_fails()
        {
            var data = "{\"client\":[\"a\"],\"nonce\":\"" + Nonce + "\",\"v\":\"2\"}";
            var version = Version("revoked", data, Hex.Of(Hmac(One, "revoked\n" + data)));

            Assert.That(StoreVersions.Passes(One, version), Is.False);
        }

        [Test]
        public void The_plugin_s_group_is_told_by_the_first_eight_bytes_of_its_identifier()
        {
            var group = Encoding.ASCII.GetBytes("KPG/STOR").Concat(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }).ToArray();
            var other = Encoding.ASCII.GetBytes("KPG/STOP").Concat(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }).ToArray();

            Assert.That(StoreFormat.IsGroupIdentifier(group), Is.True);
            Assert.That(StoreFormat.IsGroupIdentifier(other), Is.False);
            Assert.That(StoreFormat.IsGroupIdentifier(group.Take(8).ToArray()), Is.False);
            Assert.That(StoreFormat.IsGroupIdentifier(null), Is.False);
        }

        [TestCase("KPG.Data", true)]
        [TestCase("KPG.", true)]
        [TestCase("KPG.S.0001", true)]
        [TestCase("KPG", false)]
        [TestCase("kpg.Data", false)]
        [TestCase("Password", false)]
        public void A_field_of_the_store_is_one_whose_name_begins_with_its_prefix(string name, bool isField)
        {
            Assert.That(StoreFormat.IsField(name), Is.EqualTo(isField));
        }

        private static Dictionary<string, string> Version(string kind, string data, string check)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "KPG.Kind", kind },
                { "KPG.Data", data },
                { "KPG.Mac", check },
            };
        }

        private static string Text(string kind, string data, IReadOnlyDictionary<string, string> version)
        {
            var text = new StringBuilder(kind + "\n" + data);
            foreach (var name in version.Keys.Where(name => name.StartsWith("KPG.S.", StringComparison.Ordinal))
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                text.Append('\n').Append(name).Append('\n').Append(version[name]);
            }

            return text.ToString();
        }

        private static byte[] Hmac(byte[] key, string text)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(text));
            }
        }

        private static byte[] Key(string label)
        {
            return StoreComposer.MeasuredKey(label);
        }

        private static string Repeat(string pair)
        {
            return string.Concat(Enumerable.Repeat(pair, 32));
        }

        private static byte[] Bytes(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2)
                .Select(index => Convert.ToByte(hex.Substring(index * 2, 2), 16))
                .ToArray();
        }
    }
}
