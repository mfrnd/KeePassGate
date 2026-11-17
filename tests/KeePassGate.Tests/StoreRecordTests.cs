using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A record composed by the product, `STORE.md` §607 to §609, held byte for byte to the records
    /// the measured code composed from the same members, and the texts its entry carries (S14).
    /// </summary>
    [TestFixture]
    public sealed class StoreRecordTests
    {
        private static IEnumerable<TestCaseData> Measured()
        {
            foreach (var record in (JObject)Vectors()["records"])
            {
                yield return new TestCaseData(record.Key).SetArgDisplayNames(record.Key);
            }
        }

        [TestCaseSource(nameof(Measured))]
        public void A_record_is_composed_as_the_measured_code_composed_it(string name)
        {
            // `tests/Shared/StoreRecordVectors.json` holds what `probe/store.py` composed from the
            // members written beside each: its content, its check, its identifier and every string
            // of its entry.
            var vector = (JObject)Vectors()["records"][name];
            var key = StoreComposer.BytesOf((string)vector["key"]);
            var members = ((JObject)vector["members"]).Properties().ToDictionary(member => member.Name, member => (string)member.Value);
            var items = vector["items"] == null
                ? null
                : ((JArray)vector["items"]).Select(item => (IReadOnlyDictionary<string, string>)((JObject)item).Properties()
                    .ToDictionary(member => member.Name, member => (string)member.Value)).ToList();

            var record = StoreRecord.Compose(key, (string)vector["kind"], members, items);

            Assert.That(record.Content, Is.EqualTo((string)vector["content"]), "the content");
            Assert.That(record.Check, Is.EqualTo((string)vector["check"]), "the check");
            Assert.That(record.Hex, Is.EqualTo((string)vector["id"]), "the identifier");
            Assert.That(
                record.Strings,
                Is.EquivalentTo(((JObject)vector["strings"]).Properties().ToDictionary(member => member.Name, member => (string)member.Value)),
                "every string of the entry");
        }

        [Test]
        public void A_record_holds_a_nonce_drawn_at_random_where_none_is_given()
        {
            var first = Retirement(One, 'a');
            var second = Retirement(One, 'a');

            Assert.That(first.Member("nonce"), Does.Match("^[0-9a-f]{32}$"));
            Assert.That(first.Hex, Is.Not.EqualTo(second.Hex), "each act of writing has an identity of its own");
        }

        [Test]
        public void An_entry_that_carries_the_store_key_is_marked_in_its_title_and_its_notes_and_no_other_is()
        {
            var authority = StoreRecord.Compose(One, "authority", new Dictionary<string, string>
            {
                { "id", Hex('a') },
                { "storeKey", StoreComposer.HexOf(One) },
            });
            var holding = StoreRecord.Compose(One, "retired", new Dictionary<string, string>
            {
                { "authority", Hex('a') },
                { "storeKey", StoreComposer.HexOf(One) },
            });
            var plain = Retirement(One, 'a');

            foreach (var marked in new[] { authority, holding })
            {
                Assert.That(marked.Protected, Is.True, marked.Kind);
                Assert.That(marked.Title, Is.EqualTo("KPG " + marked.Kind + " " + marked.Hex.Substring(0, 8) + " (do not delete)"));
                Assert.That(marked.Notes, Does.StartWith(
                    "Do not delete this entry. It holds the key of this database's KeePassGate store.\r\n"));
                Assert.That(marked.Notes, Does.EndWith(StoreFormat.RecordNotes));
            }

            Assert.That(plain.Protected, Is.False);
            Assert.That(plain.Title, Is.EqualTo("KPG retired " + plain.Hex.Substring(0, 8)));
            Assert.That(plain.Notes, Is.EqualTo(
                "KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing."));
        }

        [Test]
        public void A_record_that_would_not_read_back_as_its_kind_is_refused()
        {
            Assert.Throws<ArgumentException>(
                () => StoreRecord.Compose(One, "future", new Dictionary<string, string> { { "x", "y" } }),
                "a kind the plugin does not know");
            Assert.Throws<ArgumentException>(
                () => StoreRecord.Compose(One, "retired", new Dictionary<string, string> { { "storeKey", StoreComposer.HexOf(One) } }),
                "without the member its kind reads");
            Assert.Throws<ArgumentException>(
                () => StoreRecord.Compose(One, "retired", new Dictionary<string, string> { { "authority", "a\0b" } }),
                "a character the content's reader refuses");
        }

        [Test]
        public void A_record_composed_reads_as_the_record_it_was_composed_as()
        {
            var record = Retirement(One, 'a');
            var form = StoreVersions.Checked(record.Strings);

            Assert.That(form.Fails, Is.Null);
            Assert.That(form.Kind, Is.EqualTo("retired"));
            Assert.That(form.Member("authority"), Is.EqualTo(Hex('a')));
            Assert.That(StoreVersions.Passes(One, record.Strings), Is.True);
        }

        private static StoreRecord Retirement(byte[] key, char authority)
        {
            return StoreRecord.Compose(key, "retired", new Dictionary<string, string> { { "authority", Hex(authority) } });
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreRecordVectors.json");
                if (File.Exists(path))
                {
                    // Read as text: a member that looks like a date stays the text it is.
                    using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                    {
                        return JObject.Load(reader);
                    }
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreRecordVectors.json is not above the test's directory");
        }
    }
}
