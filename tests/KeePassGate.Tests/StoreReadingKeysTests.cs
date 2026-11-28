using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Which key stands, `STORE.md` §649: a second key, and the keep records, their items and the
    /// deletion records that set a key aside.
    /// </summary>
    [TestFixture]
    public sealed class StoreReadingKeysTests
    {
        [Test]
        public void Records_under_a_second_key_split_the_store_whether_found_in_an_authority_or_a_retirement()
        {
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(Two, 'b'));
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Split));

            store = new ComposedStore(One);
            store.Add(StoreComposer.Record(
                Two, "retired", "authority:" + Hex('b'),
                StoreComposer.Content(("authority", Hex('b')), ("storeKey", StoreComposer.HexOf(Two)))));
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Split));
        }

        [Test]
        public void A_key_found_beside_a_deletion_record_naming_its_carrier_is_still_found()
        {
            var store = new ComposedStore(One);
            var second = store.Add(StoreRecords.Authority(Two, 'b'));
            store.Delete(second);

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Split));
        }

        [Test]
        public void A_keep_record_its_item_and_its_deletion_record_each_set_a_key_aside()
        {
            var byRecord = new ComposedStore(One);
            byRecord.Add(StoreRecords.Authority(Two, 'b'));
            var kept = byRecord.Kept(One, Two);
            byRecord.Restate(KeptItem(kept, Two));
            Assert.That(byRecord.Read().State, Is.EqualTo(StoreState.Whole), "a keep record");

            var byItem = new ComposedStore(One);
            byItem.Add(StoreRecords.Authority(Two, 'b'));
            byItem.Restate(StoreComposer.Item(("after", string.Empty), ("id", new string('7', 32)), ("key", StoreComposer.KeyName(Two)), ("kind", "kept")));
            Assert.That(byItem.Read().State, Is.EqualTo(StoreState.Whole), "its item in a summary entry");

            var byDeletion = new ComposedStore(One);
            byDeletion.Add(StoreRecords.Authority(Two, 'b'));
            var gone = byDeletion.Kept(One, Two);
            byDeletion.Drop(gone);
            byDeletion.Delete(gone);
            Assert.That(byDeletion.Read().State, Is.EqualTo(StoreState.Whole), "a deletion record that tells of it");
        }

        [Test]
        public void A_keep_record_another_names_in_after_sets_nothing_aside()
        {
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(Two, 'b'));
            var earlier = store.Kept(One, Two);
            store.Restate(KeptItem(earlier, Two));
            store.Add(StoreComposer.Record(
                Two, "kept", "key:" + StoreComposer.KeyName(Three),
                StoreComposer.Content(("after", earlier.Hex), ("key", StoreComposer.KeyName(Three)))));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Split));
        }

        [Test]
        public void Keys_whose_keep_records_name_each_other_all_stand()
        {
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(Two, 'b'));
            store.Kept(One, Two);
            store.Kept(Two, One);

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Split));
        }

        [Test]
        public void A_keep_record_under_a_key_set_aside_sets_nothing_aside_and_a_circle_s_tail_is()
        {
            // One sets Two aside; Two's record naming Three counts for nothing, so Three stands
            // beside One.
            var chain = new ComposedStore(One);
            chain.Add(StoreRecords.Authority(Two, 'b'));
            chain.Add(StoreRecords.Authority(Three, 'c'));
            chain.Kept(One, Two);
            chain.Kept(Two, Three);
            Assert.That(chain.Read().State, Is.EqualTo(StoreState.Split));

            // One and Two in a circle, and One sets Three aside: the circle stands, the tail
            // does not, and the store is still split between the two.
            var tail = new ComposedStore(One);
            tail.Add(StoreRecords.Authority(Two, 'b'));
            tail.Add(StoreRecords.Authority(Three, 'c'));
            tail.Kept(One, Two);
            tail.Kept(Two, One);
            tail.Kept(One, Three);
            Assert.That(tail.Read().State, Is.EqualTo(StoreState.Split));
        }

        private static IEnumerable<TestCaseData> Keyed()
        {
            foreach (var meaning in ((JObject)RuleVectors.Meanings["meanings"]).Properties()
                         .Where(meaning => meaning.Name.StartsWith("keys:", StringComparison.Ordinal)))
            {
                yield return new TestCaseData(meaning.Name).SetArgDisplayNames(meaning.Name);
            }
        }

        [TestCaseSource(nameof(Keyed))]
        public void The_keys_of_a_store_are_what_the_measured_reading_found(string name)
        {
            // `tests/Shared/StoreMeaningVectors.json` holds, for each store of the family, what
            // `probe/read.py` found with its reading of the keys on: its state, the authority
            // served, the keys found, those that stand and those set aside, every keep record read
            // under any key found, and, for a store a start stopped, the stop it lists apart. The
            // keys are named one and two, the first the file's key and the second the key of the
            // records whose names begin with `kept`.
            var store = (JObject)((JObject)RuleVectors.Meanings["meanings"])[name];
            var labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreComposer.KeyName(StoreComposer.BytesOf((string)RuleVectors.Meanings["key"])), "one" },
                { StoreComposer.KeyName(StoreComposer.BytesOf((string)RuleVectors.Records["records"]["format, the version record a start writes under the new key"]["key"])), "two" },
            };
            var keys = (JObject)store["keys"];

            using (var reading = RuleVectors.Reading(store))
            {
                var states = new Dictionary<string, StoreState>(StringComparer.Ordinal)
                {
                    { "ok", StoreState.Whole },
                    { "incomplete", StoreState.Stopped },
                    { "split", StoreState.Split },
                };
                Assert.That(reading.State, Is.EqualTo(states[(string)store["status"]]), reading.Reason);
                Assert.That(StoreAuthorities.Served(reading), Is.EqualTo((string)store["served"]), "the authority served");
                Assert.That(reading.Found.Select(key => labels[key.Name]).OrderBy(label => label, StringComparer.Ordinal), Is.EqualTo(Strings(keys["found"])), "the keys found");
                Assert.That(reading.Standing.Select(key => labels[key]).OrderBy(label => label, StringComparer.Ordinal), Is.EqualTo(Strings(keys["standing"])), "the keys that stand");
                Assert.That(
                    reading.Found.Select(key => key.Name).Except(reading.Standing).Select(key => labels[key]).OrderBy(label => label, StringComparer.Ordinal),
                    Is.EqualTo(Strings(keys["aside"])),
                    "the keys set aside");
                Assert.That(reading.KeptRead, Is.EqualTo(Strings(keys["kept_read"])), "every keep record read");

                var losses = StoreLosses.Of(reading);
                var expected = (JArray)store["losses"]["setAsideBy"];
                Assert.That(
                    losses == null ? Enumerable.Empty<string>() : losses.SetAside.Select(stop => stop.Key + " " + stop.Value),
                    Is.EqualTo(expected.Select(stop => (string)stop["id"] + " " + (string)stop["key"])),
                    "the stops a start wrote, listed apart");
                if (losses != null)
                {
                    Assert.That(losses.NotRestated, Is.EqualTo(Strings(store["losses"]["notRestated"])), "the records no summary entry restates");
                    Assert.That(losses.Stops, Is.EqualTo(new[] { StoreStop.NotRestated }), "the stop's condition");
                }
            }
        }

        [Test]
        public void A_stop_is_a_condition_of_its_own_also_where_no_summary_entry_is_present()
        {
            // The measured listing names the condition of a record no summary entry restates for a
            // stop a start wrote, also beside a store with no summary entry present, where any other
            // such record is the summary entry's loss alone.
            var store = new ComposedStore(One);
            var stop = store.Kept(One, Two);
            store.Drop(store.Head);

            var losses = StoreLosses.Of(store.Read());

            Assert.That(losses.Stops, Is.EqualTo(new[] { StoreStop.NoSummaryEntry, StoreStop.NotRestated }));
            Assert.That(losses.SetAside.Select(pair => pair.Key + " " + pair.Value), Is.EqualTo(new[] { stop.Hex + " " + StoreComposer.KeyName(Two) }));
        }

        [Test]
        public void A_keep_record_naming_a_key_found_is_no_stop_and_one_a_summary_entry_restates_is_none_either()
        {
            // Under a store stopped for another reason: a keep record naming a key that is found is
            // no stop, restated or not, and a keep record naming a key found nowhere that its
            // summary entry restates is none either.
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(Two, 'b'));
            store.Kept(One, Two);
            var restated = store.Kept(One, Three);
            store.RestateAll(new[] { KeptItem(restated, Three) });
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 7, new string('a', 64)));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped), reading.Reason);
            Assert.That(StoreLosses.Of(reading).SetAside, Is.Empty);
        }

        [Test]
        public void A_keep_record_naming_its_own_key_leaves_that_key_standing()
        {
            var store = new ComposedStore(One);
            var self = store.Kept(One, One);
            store.Restate(KeptItem(self, One));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }

        private static IEnumerable<string> Strings(JToken list)
        {
            return ((JArray)list).Select(value => (string)value);
        }
    }
}
