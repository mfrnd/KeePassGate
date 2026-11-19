using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
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
    /// The acts that add a profile name and remove it, `STORE.md` §616 and §627: what each
    /// composes, under which key, and what each is composed to again at a later reading.
    /// </summary>
    [TestFixture]
    public sealed class ProfileActsTests
    {
        [TestCase("profile", "")]
        [TestCase("profile, naming a removal", "removal")]
        [TestCase("removed", null)]
        public void An_act_composes_the_members_of_the_measured_record(string vector, string after)
        {
            // `tests/Shared/StoreRecordVectors.json` holds the records `probe/store.py` composed:
            // an addition with nothing in `after`, an addition naming a removal, and a removal.
            // The record vectors hold the bytes; this holds that the act composes the same
            // members, the nonce aside, which is drawn at random for every record.
            var measured = (JObject)Vectors()["records"][vector];
            var removal = (string)Vectors()["records"]["removed"]["id"];
            var store = new ComposedStore(One);
            if (after == "removal")
            {
                store.Add(StoreComposer.Record(One, "removed", "profile:reader", StoreComposer.Content(("name", "reader"))));
            }

            using (var reading = store.Read())
            {
                var act = after == null
                    ? ProfileActs.Remove(Holding(store, "reader"), "reader")
                    : ProfileActs.Add(reading, "reader");
                var record = act.Records.Single();
                var members = record.Members.Where(member => member.Key != "nonce").ToDictionary(member => member.Key, member => member.Value);
                var expected = ((JObject)measured["members"]).Properties()
                    .Where(member => member.Name != "nonce")
                    .ToDictionary(member => member.Name, member => (string)member.Value);
                if (after == "removal")
                {
                    // The measured removal's identifier is its own; the act names the removal this
                    // store holds, in the same place.
                    expected["after"] = store.Read().InForce.RemovalsOf("reader").Single();
                    Assume.That((string)measured["members"]["after"], Is.EqualTo(removal));
                }

                Assert.That(record.Kind, Is.EqualTo((string)measured["kind"]));
                Assert.That(members, Is.EquivalentTo(expected));
            }
        }

        [Test]
        public void An_addition_names_every_removal_of_its_name_the_reading_holds_in_order()
        {
            var store = new ComposedStore(One);
            var first = store.Add(Removal("reader"));
            var second = store.Add(Removal("reader"));
            store.Add(Removal("writer"));
            store.RestateAll(new[] { RemovalItem(first.Hex, "reader"), RemovalItem(second.Hex, "reader") });

            using (var reading = store.Read())
            {
                var act = ProfileActs.Add(reading, "reader");

                var expected = new[] { first.Hex, second.Hex }.OrderBy(hex => hex, StringComparer.Ordinal);
                Assert.That(act.Records.Single().Member("after"), Is.EqualTo(string.Join(" ", expected)));
                Assert.That(act.Against, Is.EqualTo(reading.InForce.Key.Name), "composed under the key that stands");
            }
        }

        [Test]
        public void No_act_is_composed_where_no_store_key_stands()
        {
            using (var reading = new MemoryStore().Read())
            {
                Assert.That(ProfileActs.Add(reading, "reader"), Is.Null);
                Assert.That(ProfileActs.Remove(reading, "reader"), Is.Null);
            }
        }

        [Test]
        public void An_act_about_a_name_is_told_by_the_name_and_no_other_act_is()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            using (var reading = store.Read())
            {
                Assert.That(ProfileActs.About(ProfileActs.Add(reading, "reader")), Is.EqualTo("reader"));
                Assert.That(ProfileActs.About(AdmissionActs.Revoke(reading, Hex('c'))), Is.Null);
            }

            using (var reading = Holding(store, "reader"))
            {
                Assert.That(ProfileActs.About(ProfileActs.Remove(reading, "reader")), Is.EqualTo("reader"));
            }
        }

        [Test]
        public void Names_written_by_the_acts_are_on_the_list_and_off_it()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);

            StoreSeeds.Name(store, "reader");
            Assert.That(Named(store), Is.EqualTo(new[] { "reader" }));

            StoreSeeds.Unname(store, "reader");
            Assert.That(Named(store), Is.Empty);

            StoreSeeds.Name(store, "reader");
            Assert.That(Named(store), Is.EqualTo(new[] { "reader" }), "added again, naming the removal");
        }

        [Test]
        public void An_addition_composed_again_names_what_the_page_held_and_writes_nothing_once_a_further_removal_stands()
        {
            // Composed at the press, an addition names the removals the page's reading held; a
            // removal made apart since, which the operator never saw, is not overruled.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "reader");
            StoreSeeds.Unname(store, "reader");

            StoreAct pressed;
            using (var reading = store.Read())
            {
                pressed = ProfileActs.Add(reading, "reader");
            }

            using (var unchanged = store.Read())
            {
                var again = pressed.Again(unchanged);
                Assert.That(again, Is.Not.Null);
                Assert.That(again.Records.Single().Member("after"), Is.EqualTo(pressed.Records.Single().Member("after")));
            }

            // A further removal of the name, as a merge brings it.
            StoreSeeds.Name(store, "reader");
            StoreSeeds.Unname(store, "reader");
            using (var further = store.Read())
            {
                Assert.That(pressed.Again(further), Is.Null, "nothing is written for the press");
                Assert.That(ProfileActs.Overtaken(further, pressed), Is.True, "and it is told why");
            }
        }

        [Test]
        public void An_addition_composed_again_writes_nothing_where_the_name_is_on_the_list()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreAct pressed;
            using (var reading = store.Read())
            {
                pressed = ProfileActs.Add(reading, "reader");
            }

            StoreSeeds.Name(store, "reader");
            using (var named = store.Read())
            {
                Assert.That(pressed.Again(named), Is.Null);
                Assert.That(ProfileActs.Overtaken(named, pressed), Is.False, "nothing removed it");
            }
        }

        [Test]
        public void A_removal_composed_again_is_written_only_where_the_name_is_on_the_list()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "reader");
            StoreAct pressed;
            using (var reading = store.Read())
            {
                pressed = ProfileActs.Remove(reading, "reader");
            }

            using (var named = store.Read())
            {
                Assert.That(pressed.Again(named), Is.Not.Null);
            }

            StoreSeeds.Unname(store, "reader");
            using (var gone = store.Read())
            {
                Assert.That(pressed.Again(gone), Is.Null);
            }
        }

        /// <summary>The store with an addition of this name, read.</summary>
        private static StoreReading Holding(ComposedStore store, string name)
        {
            store.Add(StoreComposer.Record(One, "profile", "profile:" + name, StoreComposer.Content(("after", string.Empty), ("name", name))));
            return store.Read();
        }

        /// <summary>The store with this name added, read.</summary>
        private static StoreReading Holding(MemoryStore store, string name)
        {
            StoreSeeds.Name(store, name);
            return store.Read();
        }

        private static IReadOnlyList<string> Named(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return ProfileNames.Of(reading).Names;
            }
        }

        private static StoreComposer.Composed Removal(string name)
        {
            return StoreComposer.Record(One, "removed", "profile:" + name, StoreComposer.Content(("name", name)));
        }

        private static string RemovalItem(string id, string name)
        {
            return StoreComposer.Item(("id", id), ("kind", "removed"), ("name", name));
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreRecordVectors.json");
                if (File.Exists(path))
                {
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
