using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The profile names a store keeps, `STORE.md` §616: which names are on the list, each rule
    /// alone, in every order of the entries, and as the measured reading answered them.
    /// </summary>
    [TestFixture]
    public sealed class ProfileNamesTests
    {
        private static IEnumerable<TestCaseData> Measured()
        {
            foreach (var meaning in ((JObject)Vectors()["meanings"]).Properties())
            {
                yield return new TestCaseData(meaning.Name).SetArgDisplayNames(meaning.Name);
            }
        }

        [TestCaseSource(nameof(Measured))]
        public void The_names_on_the_list_are_what_the_measured_reading_answered(string name)
        {
            // `tests/Shared/StoreMeaningVectors.json` holds what `probe/read.py` answered as
            // `names` for stores composed by `probe/store.py`.
            var store = (JObject)Vectors()["meanings"][name];
            var versions = new StoreVersions();
            var candidates = new List<StoreCandidate>();
            foreach (var entry in (JObject)store["entries"])
            {
                var current = Strings((JObject)entry.Value["strings"]);
                var history = ((JArray)entry.Value["history"]).Select(item => Strings((JObject)item)).Reverse();
                candidates.Add(versions.Candidate(StoreComposer.BytesOf(entry.Key), new[] { current }.Concat(history).ToArray()));
            }

            var deletions = ((JArray)store["deletions"]).Select(identifier => StoreComposer.BytesOf((string)identifier)).ToList();
            using (var reading = StoreReading.Of(candidates, deletions, null, new StoreKept()))
            {
                Assert.That(ProfileNames.Of(reading).Names, Is.EqualTo(((JArray)store["names"]).Select(named => (string)named)));
            }
        }

        [Test]
        public void A_name_added_is_on_the_list()
        {
            var store = new ComposedStore(One);
            store.Add(Addition("reader", string.Empty));

            Assert.That(Names(store), Is.EqualTo(new[] { "reader" }));
        }

        [Test]
        public void A_name_removed_is_off_the_list_by_record_by_item_and_by_deletion_record()
        {
            var byRecord = new ComposedStore(One);
            byRecord.Add(Addition("reader", string.Empty));
            var removal = byRecord.Add(Removal("reader"));
            byRecord.Restate(RemovalItem(removal.Hex, "reader"));
            Assert.That(Names(byRecord), Is.Empty, "its record");

            var byItem = new ComposedStore(One);
            byItem.Add(Addition("reader", string.Empty));
            byItem.Restate(RemovalItem(new string('7', 32), "reader"));
            Assert.That(Names(byItem), Is.Empty, "an item");

            var byDeletion = new ComposedStore(One);
            byDeletion.Add(Addition("reader", string.Empty));
            byDeletion.Deleted.Add(StoreComposer.IdentifierOf(One, "removed", "profile:reader", 0, new string('5', 64)));
            Assert.That(Names(byDeletion), Is.Empty, "a deletion record");
        }

        [Test]
        public void A_name_added_again_is_on_the_list_only_where_the_addition_names_every_removal()
        {
            var store = new ComposedStore(One);
            var first = store.Add(Removal("reader"));
            var second = store.Add(Removal("reader"));
            store.RestateAll(new[] { RemovalItem(first.Hex, "reader"), RemovalItem(second.Hex, "reader") });
            store.Add(Addition("reader", first.Hex));
            Assume.That(Names(store), Is.Empty, "an addition that names a single removal of two");

            store.Add(Addition("reader", Listed(first.Hex, second.Hex)));

            Assert.That(Names(store), Is.EqualTo(new[] { "reader" }), "and an addition that names both");
        }

        [Test]
        public void A_removal_known_only_by_an_item_whose_id_is_no_identifier_keeps_its_name_off_the_list()
        {
            // No addition can name an `id` that is no identifier, `after` being a list of them.
            var store = new ComposedStore(One);
            store.Add(Addition("reader", string.Empty));
            store.Restate(RemovalItem("not an identifier", "reader"));

            Assert.That(Names(store), Is.Empty);
        }

        [Test]
        public void An_addition_beside_a_deletion_record_naming_it_puts_nothing_on_the_list()
        {
            var store = new ComposedStore(One);
            store.Delete(store.Add(Addition("reader", string.Empty)));

            Assert.That(Names(store), Is.Empty);
        }

        [Test]
        public void Two_additions_of_a_name_keep_it_on_the_list_where_a_single_addition_names_every_removal()
        {
            var store = new ComposedStore(One);
            var removal = store.Add(Removal("reader"));
            store.Restate(RemovalItem(removal.Hex, "reader"));
            store.Add(Addition("reader", string.Empty));
            store.Add(Addition("reader", removal.Hex));

            Assert.That(Names(store), Is.EqualTo(new[] { "reader" }));
        }

        [Test]
        public void A_name_no_claim_could_carry_is_on_no_list_and_stops_nothing()
        {
            var store = new ComposedStore(One);
            store.Add(Addition("not a name", string.Empty));
            store.Add(Addition("default", string.Empty));
            store.Add(Addition("reader", string.Empty));

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(ProfileNames.Of(reading).Names, Is.EqualTo(new[] { "reader" }));
            }
        }

        [Test]
        public void Two_names_that_differ_in_case_are_two_names()
        {
            var store = new ComposedStore(One);
            store.Add(Addition("reader", string.Empty));
            store.Add(Addition("Reader", string.Empty));
            var removal = store.Add(Removal("reader"));
            store.Restate(RemovalItem(removal.Hex, "reader"));

            Assert.That(Names(store), Is.EqualTo(new[] { "Reader" }), "the removal of a name takes nothing from the other");
        }

        [Test]
        public void A_store_with_no_key_names_nothing()
        {
            using (var reading = new MemoryStore().Read())
            {
                Assert.That(ProfileNames.Of(reading).Names, Is.Empty);
                Assert.That(ProfileNames.Of(reading).Holds("reader"), Is.False);
            }

            Assert.That(ProfileNames.Of(null).Names, Is.Empty);
        }

        [Test]
        public void A_list_of_more_names_than_a_pairing_can_ask_for_is_read_whole()
        {
            // No ceiling on the list, `STORE.md` §616: what a merge of copies that each added names
            // brings is read, every name of it.
            var store = new ComposedStore(One);
            var names = Enumerable.Range(0, 100)
                .Select(index => "profile-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            foreach (var name in names)
            {
                store.Add(Addition(name, string.Empty));
            }

            Assert.That(Names(store), Is.EqualTo(names));
        }

        [Test]
        public void Every_order_of_the_entries_names_the_same()
        {
            // The entries of a database are walked in the order KeePass holds them, which a merge
            // or a move changes; no step reads an order, `STORE.md` §623.
            var probe = new ComposedStore(One);
            var reader = probe.Add(Removal("reader"));
            var auditor = probe.Add(Removal("auditor"));
            var records = new List<StoreComposer.Composed>
            {
                reader,
                auditor,
                Addition("reader", string.Empty),
                Addition("writer", string.Empty),
                Addition("auditor", string.Empty),
                Addition("auditor", auditor.Hex),
            };
            var items = new[] { RemovalItem(reader.Hex, "reader"), RemovalItem(auditor.Hex, "auditor") };

            foreach (var order in Orders(records.Count))
            {
                var store = new ComposedStore(One);
                foreach (var at in order)
                {
                    store.Add(records[at]);
                }

                store.RestateAll(items);
                Assert.That(Names(store), Is.EqualTo(new[] { "auditor", "writer" }), string.Join(" ", order));
            }
        }

        [Test]
        public void A_name_is_refused_by_the_issuer_s_rule_and_nothing_else()
        {
            Assert.That(ProfileNames.Refusal("reader"), Is.Null);
            Assert.That(ProfileNames.Refusal("ops_2-eu"), Is.Null);
            Assert.That(ProfileNames.Refusal("not a name"), Is.Not.Null);
            Assert.That(ProfileNames.Refusal(new string('a', 65)), Is.Not.Null);
            Assert.That(ProfileNames.Refusal("Default"), Is.Not.Null, "the word a rule reserves, in any case");
            Assert.That(ProfileNames.Refusal(null), Is.Not.Null);
        }

        /// <summary>The names on the list of the store as it stands.</summary>
        private static IReadOnlyList<string> Names(ComposedStore store)
        {
            using (var reading = store.Read())
            {
                Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                return ProfileNames.Of(reading).Names;
            }
        }

        private static StoreComposer.Composed Addition(string name, string after)
        {
            return StoreComposer.Record(One, "profile", "profile:" + name, StoreComposer.Content(("after", after), ("name", name)));
        }

        private static StoreComposer.Composed Removal(string name)
        {
            return StoreComposer.Record(One, "removed", "profile:" + name, StoreComposer.Content(("name", name)));
        }

        private static string RemovalItem(string id, string name)
        {
            return StoreComposer.Item(("id", id), ("kind", "removed"), ("name", name));
        }

        private static string Listed(params string[] identifiers)
        {
            return string.Join(" ", identifiers.OrderBy(identifier => identifier, StringComparer.Ordinal));
        }

        /// <summary>Every rotation of this many entries and each rotation reversed: each entry before and after each.</summary>
        private static IEnumerable<int[]> Orders(int count)
        {
            for (var start = 0; start < count; start++)
            {
                var rotated = Enumerable.Range(0, count).Select(index => (index + start) % count).ToArray();
                yield return rotated;
                yield return rotated.Reverse().ToArray();
            }
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreMeaningVectors.json");
                if (File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreMeaningVectors.json is not above the test's directory");
        }

        private static IReadOnlyDictionary<string, string> Strings(JObject strings)
        {
            return strings.Properties().ToDictionary(property => property.Name, property => (string)property.Value);
        }
    }
}
