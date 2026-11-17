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
    /// What the reading makes of a store as a whole, `STORE.md` §621 to §624 and §649 to §651,
    /// as the measured reading made of it; the history rule, `STORE.md` §622; and the order of
    /// §656, in which a store in two states answers the earlier.
    /// </summary>
    [TestFixture]
    public sealed class StoreReadingStatesTests
    {
        private static IEnumerable<TestCaseData> Measured()
        {
            var vectors = JObject.Parse(File.ReadAllText(VectorsPath()));
            foreach (var reading in (JObject)vectors["readings"])
            {
                yield return new TestCaseData(reading.Key).SetArgDisplayNames(reading.Key);
            }
        }

        [TestCaseSource(nameof(Measured))]
        public void A_store_reads_as_the_measured_reading_read_it(string name)
        {
            // `tests/Shared/StoreReadingVectors.json` holds what `probe/read.py` answered, with its
            // history rule on, for stores `probe/store.py` composed, each answer as the state it is.
            var vectors = JObject.Parse(File.ReadAllText(VectorsPath()));
            var store = (JObject)vectors["readings"][name];
            var versions = new StoreVersions();

            var candidates = new List<StoreCandidate>();
            foreach (var entry in (JObject)store["entries"])
            {
                var current = Strings((JObject)entry.Value["strings"]);
                var history = ((JArray)entry.Value["history"]).Select(item => Strings((JObject)item)).Reverse();
                candidates.Add(versions.Candidate(StoreComposer.BytesOf(entry.Key), new[] { current }.Concat(history).ToArray()));
            }

            var deletions = ((JArray)store["deletions"]).Select(identifier => StoreComposer.BytesOf((string)identifier)).ToList();
            var reading = StoreReading.Of(candidates, deletions, null, new StoreKept());

            var expected = (StoreState)Enum.Parse(typeof(StoreState), (string)store["state"]);

            Assert.That(reading.State, Is.EqualTo(expected), reading.Reason);
            Assert.That(reading.Damage, Is.EqualTo((int)store["damage"]), "damage");
            Assert.That(reading.Passed, Is.EqualTo((int)store["passed"]), "passed over");
            Assert.That(reading.ByHistory, Is.EqualTo((int)store["byHistory"]), "counted by their history");
        }

        [Test]
        public void A_store_with_an_authority_a_summary_entry_and_nothing_else_is_whole()
        {
            Assert.That(new ComposedStore(One).Read().State, Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void A_database_with_no_store_key_has_no_store_whatever_else_it_holds()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Authority);

            Assert.That(store.Read().State, Is.EqualTo(StoreState.None));
        }

        // The history rule, `STORE.md` §622.

        [Test]
        public void A_record_counts_by_the_newest_version_of_its_history_that_passes()
        {
            var store = new ComposedStore(One);
            var revoked = store.Revoked('c');
            store.Edit(revoked, "{\"client\":\"edited\"}", "{\"client\":\"edited again\"}");

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.ByHistory, Is.EqualTo(1));
        }

        [Test]
        public void A_record_with_no_version_that_passes_is_damage_and_stops_the_store()
        {
            var store = new ComposedStore(One);
            var revoked = store.Revoked('c');
            store.Replace(revoked, "{\"client\":\"edited\"}");

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(reading.Damage, Is.EqualTo(1));
        }

        [Test]
        public void An_identifier_that_differs_from_the_content_s_in_any_single_byte_is_no_record()
        {
            for (var at = 0; at < 16; at++)
            {
                var store = new ComposedStore(One);
                var revoked = store.Revoked('c');
                store.Move(revoked, at);

                // Passed over where the tag meets no kind, damage where it does: never a
                // record either way.
                var reading = store.Read();
                Assert.That(reading.Damage + reading.Passed, Is.EqualTo(1), "byte " + at);
            }
        }
        // The order of §656: each pair answers the earlier word.

        [Test]
        public void A_store_in_two_states_answers_the_earlier()
        {
            var splitAndOutdated = new ComposedStore(One, StoreFormat.Number + 1);
            splitAndOutdated.Add(StoreRecords.Authority(Two, 'b'));
            Assert.That(splitAndOutdated.Read().State, Is.EqualTo(StoreState.Split));

            var splitAndStopped = new ComposedStore(One);
            splitAndStopped.Add(StoreRecords.Authority(Two, 'b'));
            splitAndStopped.Drop(splitAndStopped.Head);
            Assert.That(splitAndStopped.Read().State, Is.EqualTo(StoreState.Split));

            var outdatedAndStopped = new ComposedStore(One, StoreFormat.Number + 1);
            outdatedAndStopped.Decision("revoked");
            Assert.That(outdatedAndStopped.Read().State, Is.EqualTo(StoreState.Outdated));

            var noneAndStopped = new ComposedStore(One);
            noneAndStopped.Drop(noneAndStopped.Authority);
            noneAndStopped.Revoked('c');
            Assert.That(noneAndStopped.Read().State, Is.EqualTo(StoreState.None));
        }

        [Test]
        public void A_list_that_names_itself_reads()
        {
            var store = new ComposedStore(One);
            var head = store.Head;
            store.Drop(head);
            var self = StoreComposer.Record(
                One, "head", "store", StoreComposer.Content(("after", new string('e', 32)), ("height", "1")), 1, new[] { FormatItem(1) });
            store.Add(self);

            Assert.DoesNotThrow(() => store.Read());
        }

        private static string VectorsPath()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreReadingVectors.json");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreReadingVectors.json is not above the test's directory");
        }

        private static IReadOnlyDictionary<string, string> Strings(JObject strings)
        {
            return strings.Properties().ToDictionary(property => property.Name, property => (string)property.Value);
        }
    }
}
