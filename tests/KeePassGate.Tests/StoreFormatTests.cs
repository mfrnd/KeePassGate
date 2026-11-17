using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Store;
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
    /// `kpg-store-as-a-whole/store-key/` and a label. Regenerate them from that code and never
    /// from this side, since two halves written here could agree on a format neither measured.
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
        public void Every_kind_the_store_was_measured_with_is_a_kind_it_knows()
        {
            // The kinds of the records the measured code wrote, in the order it lists them. A kind
            // the reading does not know cannot be told by its tag, so a kind left out of this list
            // is a record whose identifier hides nothing.
            Assert.That(
                StoreFormat.Kinds,
                Is.EquivalentTo(new[] { "authority", "retired", "revoked", "client", "profile", "removed", "version", "head" }));
        }

        [Test]
        public void An_authority_gives_the_key_its_content_holds_under_its_own_identifier()
        {
            var version = Version("authority", AuthorityData, "9ef5fefced88074299919ee2f8f73ed7599b18c9611689b83811cb6fa3924fc7");

            Assert.That(StoreFormat.KeyOf(Bytes("b8f37c30b55aab1900009ef5fefced88"), version), Is.EqualTo(One));
        }

        [Test]
        public void A_retirement_that_holds_a_key_gives_it_too()
        {
            var version = Version("retired", RetiredData, "435bc059579230b518906c3dfc7f74de14941f1d0dcd4e4683b19e9aa44279e1");

            Assert.That(StoreFormat.KeyOf(Bytes("cabae6ddabf3fd890000435bc0595792"), version), Is.EqualTo(Two));
        }

        [Test]
        public void A_copy_gives_no_key_and_still_passes_under_the_key()
        {
            // The same strings under another identifier, as Duplicate Entry makes them: no source
            // of a key, STORE.md §621, and still a version whose content the key vouches for.
            var version = Version("authority", AuthorityData, "9ef5fefced88074299919ee2f8f73ed7599b18c9611689b83811cb6fa3924fc7");
            var copy = Bytes("0123456789abcdef0123456789abcdef");

            Assert.That(StoreFormat.KeyOf(copy, version), Is.Null);
            Assert.That(StoreFormat.Passes(One, version), Is.True);
            Assert.That(StoreFormat.Passes(Two, version), Is.False);
        }

        [Test]
        public void A_record_of_another_kind_gives_no_key()
        {
            var version = Version("revoked", RevokedData, "e7c13c328e03ff7d70087201f26a3a31824db427f6880671ebd08ff4a6fd66d9");

            Assert.That(StoreFormat.KeyOf(Bytes("923300d9141b2a5c0000e7c13c328e03"), version), Is.Null);
            Assert.That(StoreFormat.Passes(One, version), Is.True);
        }

        [Test]
        public void A_summary_entry_s_items_are_part_of_its_check()
        {
            var version = Version("head", HeadData, "bf2f4bac6006fa78067a1cebc3db7ab4c5f972403d31fd5076d7a84a1f9b5a03");
            version["KPG.S.0001"] = HeadItem;

            Assert.That(StoreFormat.Passes(One, version), Is.True);

            version["KPG.S.0001"] = HeadItem.Replace("c3", "c4");
            Assert.That(StoreFormat.Passes(One, version), Is.False);
        }

        [Test]
        public void An_authority_whose_key_is_not_its_own_gives_none()
        {
            // The key in the content has to be the key the check passes under: an entry that
            // names a key it was not written with vouches for nothing, STORE.md §621.
            var version = Version("authority", AuthorityData, "9ef5fefced88074299919ee2f8f73ed7599b18c9611689b83811cb6fa3924fc7");
            version["KPG.Mac"] = Hex.Of(Hmac(Two, "authority\n" + AuthorityData));

            Assert.That(StoreFormat.KeyOf(Bytes("b8f37c30b55aab1900009ef5fefced88"), version), Is.Null);
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

            Assert.That(StoreFormat.Passes(One, version), Is.False);
        }

        [Test]
        public void A_version_with_a_line_feed_in_a_part_fails()
        {
            var data = RevokedData.Replace("\"v\":\"2\"}", "\"v\":\"2\"}\n");
            var version = Version("revoked", data, Hex.Of(Hmac(One, "revoked\n" + data)));

            Assert.That(StoreFormat.Passes(One, version), Is.False);
        }

        [Test]
        public void A_version_naming_a_member_twice_fails()
        {
            // STORE.md §607: two readers could take two contents from one text that passes.
            var data = "{\"client\":\"a\",\"client\":\"b\",\"nonce\":\"" + Nonce + "\",\"v\":\"2\"}";
            var version = Version("revoked", data, Hex.Of(Hmac(One, "revoked\n" + data)));

            Assert.That(StoreFormat.Passes(One, version), Is.False);
        }

        [Test]
        public void A_version_whose_content_is_not_an_object_of_strings_fails()
        {
            var data = "{\"client\":[\"a\"],\"nonce\":\"" + Nonce + "\",\"v\":\"2\"}";
            var version = Version("revoked", data, Hex.Of(Hmac(One, "revoked\n" + data)));

            Assert.That(StoreFormat.Passes(One, version), Is.False);
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
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("kpg-store-as-a-whole/store-key/" + label));
            }
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
