using System;
using System.Collections.Generic;
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
    /// The act that seals a stopped store again, `STORE.md` §625: a version granting nothing for
    /// each lost object, each type's own text, naming what the store at the write holds, held to
    /// the records the measured model printed; nothing for what the union keeps in force; a
    /// version that does not fit left out; and composed again over a later reading.
    /// </summary>
    [TestFixture]
    public sealed class SealActsTests
    {
        private const string Plugin = "0.3.0";

        [TestCase("stopped: a lost version of a row", "version, the seal's empty rule of a row, naming the standing version and the lost one")]
        [TestCase("stopped: a lost version of the switch", "version, the seal's switch refusing, naming the lost version")]
        [TestCase("stopped: a lost answer", "version, the seal's refusal of renewals forever, naming the standing version and the lost answer")]
        public void The_seal_writes_the_version_the_measured_model_wrote_for_each_type(string store, string vector)
        {
            // `tests/Shared/StoreRecordVectors.json` holds the versions the seal wrote over the
            // stores of `tests/Shared/StoreMeaningVectors.json` of the same names, under the same
            // key: the same members, and composed again with the measured nonce the same bytes.
            using (var reading = Stopped(store))
            {
                string refused;
                var act = SealActs.Seal(reading, out refused);
                Assert.That(act, Is.Not.Null, refused);
                Assert.That(act.Seals, Is.True);
                Assert.That(act.LeftOut, Is.Empty);
                var record = act.Records.Single();
                var measured = (JObject)RuleVectors.Records["records"][vector];
                var members = record.Members.Where(member => member.Key != StoreKinds.Nonce)
                    .ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
                Assert.That(
                    members,
                    Is.EquivalentTo(((JObject)measured["members"]).Properties()
                        .Where(member => member.Name != StoreKinds.Nonce)
                        .ToDictionary(member => member.Name, member => (string)member.Value, StringComparer.Ordinal)),
                    "the members");

                members[StoreKinds.Nonce] = (string)measured["members"][StoreKinds.Nonce];
                var again = StoreRecord.Compose(StoreComposer.BytesOf((string)RuleVectors.Records["key"]), record.Kind, members);
                Assert.That(again.Content, Is.EqualTo((string)measured["content"]), "the content");
                Assert.That(again.Hex, Is.EqualTo((string)measured["id"]), "the identifier");
                Assert.That(again.Check, Is.EqualTo((string)measured["check"]), "the check");

                // The write's summary entry restates the version as the measured summary entry does.
                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
                Assert.That(write.Refused, Is.Null);
                var head = write.Add[write.Add.Count - 1];
                var restated = (JObject)((JArray)RuleVectors.Records["records"]["head of height 1, restating the seal's versions"]["items"])
                    .Single(item => (string)item["object"] == record.Member(StoreKinds.ObjectMember));
                Assert.That(
                    head.Strings.Where(pair => pair.Key.StartsWith("KPG.S.", StringComparison.Ordinal)).Select(pair => JObject.Parse(pair.Value))
                        .Where(item => (string)item["kind"] == "version" && (string)item["id"] == record.Hex)
                        .Select(item => item.Properties().Where(member => member.Name != "id").ToDictionary(member => member.Name, member => (string)member.Value)),
                    Is.EqualTo(new[] { restated.Properties().Where(member => member.Name != "id").ToDictionary(member => member.Name, member => (string)member.Value) }),
                    "restated by an item of the write's summary entry");
                act.Forget();
            }
        }

        [Test]
        public void Over_a_store_a_start_stopped_the_seal_keeps_the_store_as_the_measured_model_did()
        {
            // The store of `tests/Shared/StoreMeaningVectors.json` whose new key was lost after a
            // start: its stop names the new key, and the seal writes the keep record of
            // `tests/Shared/StoreRecordVectors.json`, naming that key and the stop in `after`,
            // which its summary entry restates beside the stop. The store is whole after it, and
            // the earlier authority served.
            const string name = "keys: started again, then the new key lost, the earlier store stopped";
            var store = (JObject)RuleVectors.Meanings["meanings"][name];
            var planned = (JObject)((JArray)store["losses"]["seal"]).Single();
            var measured = (JObject)RuleVectors.Records["records"]["kept, the keep record Seal again writes over a store a start stopped, naming the new key and the stop in after"];
            using (var reading = RuleVectors.Reading(store))
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Stopped), reading.Reason);
                string refused;
                var act = SealActs.Seal(reading, out refused);
                Assert.That(act, Is.Not.Null, refused);
                var record = act.Records.Single();
                Assert.That(record.Kind, Is.EqualTo((string)planned["kind"]));
                Assert.That(record.Member(StoreKinds.Key), Is.EqualTo((string)planned["key"]), "the key the stop names");
                Assert.That(record.Member(StoreKinds.After), Is.EqualTo(string.Join(" ", ((JArray)planned["after"]).Select(id => (string)id))), "the stop in after");

                var members = record.Members.ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
                members[StoreKinds.Nonce] = (string)measured["members"][StoreKinds.Nonce];
                var again = StoreRecord.Compose(StoreComposer.BytesOf((string)measured["key"]), record.Kind, members);
                Assert.That(again.Content, Is.EqualTo((string)measured["content"]), "the content");
                Assert.That(again.Hex, Is.EqualTo((string)measured["id"]), "the identifier");

                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
                Assert.That(write.Refused, Is.Null);
                var kept = write.Add[write.Add.Count - 1].Strings
                    .Where(pair => pair.Key.StartsWith("KPG.S.", StringComparison.Ordinal))
                    .Select(pair => JObject.Parse(pair.Value))
                    .Where(item => (string)item["kind"] == "kept")
                    .Select(item => (string)item["id"] + " " + (string)item["after"])
                    .OrderBy(line => line, StringComparer.Ordinal);
                var stop = (string)planned["after"][0];
                Assert.That(kept, Is.EqualTo(new[] { stop + " ", record.Hex + " " + stop }.OrderBy(line => line, StringComparer.Ordinal)), "the stop and the keep record restated");

                var applied = write.Applied(reading.Candidates, reading.Deletions);
                using (var sealedStore = StoreReading.Afresh(applied.Key, applied.Value))
                {
                    Assert.That(sealedStore.State, Is.EqualTo(StoreState.Whole), sealedStore.Reason);
                    Assert.That(StoreLosses.Of(sealedStore), Is.Null);
                    Assert.That(StoreAuthorities.Served(sealedStore), Is.Not.Null, "the earlier authority served");
                }

                act.Forget();
            }
        }

        [Test]
        public void A_seal_over_a_split_store_is_refused_and_a_stop_beside_another_loss_is_kept_with_it()
        {
            // A split store is no stopped store and is not sealed. A stop beside a lost summary
            // entry: the seal names the summary entry and keeps the store in the same write.
            var split = new ComposedStore(One);
            split.Add(StoreRecords.Authority(Two, 'b'));
            string refused;
            Assert.That(SealActs.Seal(split.Read(), out refused), Is.Null);
            Assert.That(refused, Does.Contain("not stopped"));

            var store = new ComposedStore(One);
            var stop = store.Kept(One, Three);
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 7, new string('a', 64)));
            using (var reading = store.Read())
            {
                var act = SealActs.Seal(reading, out refused);
                Assert.That(act.Records.Select(record => record.Kind + " " + record.Member(StoreKinds.After)), Is.EqualTo(new[] { "kept " + stop.Hex }));
                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
                var applied = write.Applied(reading.Candidates, reading.Deletions);
                using (var sealedStore = StoreReading.Afresh(applied.Key, applied.Value))
                {
                    Assert.That(sealedStore.State, Is.EqualTo(StoreState.Whole), sealedStore.Reason);
                }

                act.Forget();
            }
        }

        private static IEnumerable<TestCaseData> EveryStopped()
        {
            foreach (var meaning in ((JObject)RuleVectors.Meanings["meanings"]).Properties()
                         .Where(meaning => meaning.Name.StartsWith("stopped:", StringComparison.Ordinal)))
            {
                yield return new TestCaseData(meaning.Name).SetArgDisplayNames(meaning.Name);
            }
        }

        [TestCaseSource(nameof(EveryStopped))]
        public void Over_every_stopped_store_the_seal_writes_what_the_measured_listing_planned_and_leaves_the_store_whole(string name)
        {
            // What `probe/read.py` planned the seal to write, `losses.seal` in the vectors: each
            // object, its text, `after` and `lost`, no identifier in both; and the store whole
            // once the write is applied.
            var expected = (JArray)RuleVectors.Meanings["meanings"][name]["losses"]["seal"];
            using (var reading = Stopped(name))
            {
                string refused;
                var act = SealActs.Seal(reading, out refused);
                Assert.That(act, Is.Not.Null, refused);
                Assert.That(
                    act.Records.Select(record => record.Member("object") + " " + record.Member("doc") + " after " + record.Member("after") + " lost " + (record.Member("lost") ?? string.Empty)),
                    Is.EqualTo(expected.Select(seal => (string)seal["object"] + " " + (string)seal["doc"]
                                                     + " after " + string.Join(" ", ((JArray)seal["after"]).Select(id => (string)id))
                                                     + " lost " + string.Join(" ", ((JArray)seal["lost"]).Select(id => (string)id)))),
                    "the versions the seal writes");
                foreach (var record in act.Records)
                {
                    Assert.That(Listed(record.Member("after")).Intersect(Listed(record.Member("lost"))), Is.Empty, "after and lost hold no identifier in common");
                }

                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
                Assert.That(write.Refused, Is.Null);
                var applied = write.Applied(reading.Candidates, reading.Deletions);
                using (var sealedStore = StoreReading.Afresh(applied.Key, applied.Value))
                {
                    Assert.That(sealedStore.State, Is.EqualTo(StoreState.Whole), sealedStore.Reason);
                }

                act.Forget();
            }
        }

        [Test]
        public void Nothing_is_written_for_a_decision_the_union_keeps_in_force()
        {
            // A revocation known by its deletion record alone: the seal writes no record for it,
            // and its summary entry restates the client as revoked.
            using (var reading = Stopped("stopped: a revocation known by its deletion record alone"))
            {
                string refused;
                var act = SealActs.Seal(reading, out refused);
                Assert.That(act.Records, Is.Empty);
                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
                var head = write.Add[write.Add.Count - 1];
                Assert.That(
                    head.Strings.Values.Count(value => value.Contains("\"kind\":\"revoked\"")),
                    Is.EqualTo(1),
                    "the summary entry restates the revocation");
                act.Forget();
            }
        }

        [Test]
        public void A_version_that_does_not_fit_is_left_out_named_and_its_object_goes_on_refusing()
        {
            // A standing refusal whose `after` holds identifiers at the client's place, so many that
            // its item still fits the ceiling of a string and the seal's version, naming it and a
            // lost answer of the same client besides, does not; and a summary entry deleted that
            // nothing replaces.
            var key = StoreComposer.MeasuredKey("K");
            var client = Hex('c');
            var objectText = RenewalText.ObjectOf(client);
            var random = new Random(625);
            Func<string> identifier = () =>
            {
                var check = new byte[32];
                random.NextBytes(check);
                return StoreComposer.HexOf(StoreComposer.IdentifierOf(key, "version", objectText, 0, StoreComposer.HexOf(check)));
            };

            // An item is its content and 14 characters more, and the seal's content is the
            // standing content and 75 more: an identifier and its space, and the member `lost`.
            var named = new List<string> { identifier() };
            var single = StoreComposer.RuleVersion(key, objectText, RenewalText.Forever, named.ToArray()).Strings[StoreComposer.Data].Length;
            while (single + (33 * named.Count) <= StoreFormat.MaxWritten - 89 + 33)
            {
                named.Add(identifier());
            }

            var store = new ComposedStore(key);
            var crowded = store.Add(StoreComposer.RuleVersion(key, objectText, RenewalText.Forever, named.ToArray()));
            var content = crowded.Strings[StoreComposer.Data].Length;
            Assume.That(content + 14, Is.LessThanOrEqualTo(StoreFormat.MaxWritten), "its item fits");
            Assume.That(content + 75 + 14, Is.GreaterThan(StoreFormat.MaxWritten), "the seal's item does not");
            store.RestateAll(new[] { StoreComposer.RuleVersionItem(crowded, objectText, RenewalText.Forever, named.ToArray()) });
            store.Deleted.Add(StoreComposer.IdentifierOf(key, "version", objectText, 0, new string('5', 64)));
            store.Deleted.Add(StoreComposer.IdentifierOf(key, "head", "store", 9, new string('5', 64)));

            using (var reading = store.Read())
            {
                Assume.That(reading.InForce.LostOf(objectText), Has.Count.EqualTo(1));
                string refused;
                var act = SealActs.Seal(reading, out refused);
                Assert.That(act, Is.Not.Null, refused);
                Assert.That(act.Records, Is.Empty, "the version is left out");
                Assert.That(act.LeftOut.Single(), Does.StartWith(objectText + ": "));

                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
                Assert.That(write.Refused, Is.Null, "the rest of the seal is written");
                var applied = write.Applied(reading.Candidates, reading.Deletions);
                using (var sealedStore = StoreReading.Afresh(applied.Key, applied.Value))
                {
                    Assert.That(sealedStore.State, Is.EqualTo(StoreState.Whole), sealedStore.Reason);
                    Assert.That(sealedStore.InForce.LostOf(objectText), Has.Count.EqualTo(1), "the object goes on refusing");
                }

                act.Forget();
            }
        }

        [Test]
        public void The_seal_is_composed_again_over_a_later_reading_and_comes_to_nothing_where_the_store_is_whole()
        {
            // Composed over the store shown, then again over the store a merge left: a version
            // whose text came back gets nothing, `STORE.md` §625, and a store that is whole has
            // nothing to seal.
            var row = StoreComposer.RowObject(new string('1', 32), "pf");
            var standing = StoreComposer.RuleVersion(One, row, "row:R");
            var lost = StoreComposer.RuleVersion(One, row, "row:RU", standing.Hex);
            var store = new ComposedStore(One);
            var first = store.Head;
            store.Add(standing);
            store.RestateAll(new[] { StoreComposer.RuleVersionItem(standing, row, "row:R") });
            var head = store.Head;
            store.Drop(head);
            store.Delete(head);
            store.Deleted.Add(lost.Identifier);

            StoreAct shown;
            using (var reading = store.Read())
            {
                string refused;
                shown = SealActs.Seal(reading, out refused);
                Assert.That(shown.Records.Select(record => record.Member("doc")), Is.EqualTo(new[] { "row:" }), "the lost version gets the empty rule");
            }

            // The version's text comes back with a summary entry restating it.
            store.Add(lost);
            store.Add(head);
            store.RestateAll(new[] { StoreComposer.RuleVersionItem(lost, row, "row:RU", standing.Hex) }, first.Hex);
            using (var later = store.Read())
            {
                Assume.That(later.State, Is.EqualTo(StoreState.Whole), later.Reason);
                Assert.That(shown.Again(later), Is.Null, "nothing is left to seal");
            }

            shown.Forget();
        }

        [Test]
        public void A_store_that_is_not_stopped_or_has_no_room_is_not_sealed()
        {
            string refused;
            Assert.That(SealActs.Seal(new ComposedStore(One).Read(), out refused), Is.Null);
            Assert.That(refused, Does.Contain("not stopped"));

            var none = new ComposedStore(One);
            none.Drop(none.Authority);
            Assert.That(SealActs.Seal(none.Read(), out refused), Is.Null);
        }

        [Test]
        public void A_rule_and_an_answer_set_after_the_seal_name_its_version_and_carry_what_it_settled_on()
        {
            // `measured-seal` 8 item 3: a version set over the seal's names the seal's version in
            // `after` and carries its `lost` on, so that the loss the seal settled stays settled.
            var rows = Sealed("stopped: a lost version of a row");
            var row = "rule:" + new string('b', 32) + "/pf";
            string sealedRow;
            string settled;
            using (var reading = rows.Read())
            {
                var version = reading.InForce.VersionsOf(row).Single();
                sealedRow = version.Id;
                settled = version.Lost;
                Assume.That(version.Members[StoreKinds.Doc], Is.EqualTo("row:"));
            }

            StoreSeeds.Rule(rows, new string('b', 32), ("pf", "R"));
            using (var reading = rows.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var set = reading.InForce.VersionsOf(row).Single();
                Assert.That(Listed(set.After), Does.Contain(sealedRow), "the rule names the seal's version");
                Assert.That(set.Lost, Is.EqualTo(settled), "and carries what the seal settled on");
                Assert.That(reading.InForce.LostOf(row), Is.Empty);
            }

            var answers = Sealed("stopped: a lost answer");
            var client = "1a9bd859c58e662a169cfafcbc5b4d0bdb6eaeb54802d57f609cf055fb627840";
            string sealedAnswer;
            using (var reading = answers.Read())
            {
                var version = reading.InForce.VersionsOf(RenewalText.ObjectOf(client)).Single();
                sealedAnswer = version.Id;
                settled = version.Lost;
                Assume.That(version.Members[StoreKinds.Doc], Is.EqualTo(RenewalText.Forever));
            }

            StoreSeeds.Lift(answers, client);
            using (var reading = answers.Read())
            {
                var lift = reading.InForce.VersionsOf(RenewalText.ObjectOf(client)).Single();
                Assert.That(Listed(lift.After), Does.Contain(sealedAnswer), "the lift names the seal's version");
                Assert.That(lift.Lost, Is.EqualTo(settled), "and carries what the seal settled on");
                Assert.That(StoredRenewals.Of(reading.InForce, client).Answer, Is.Null, "the client's renewals are asked again");
            }
        }

        /// <summary>A store of the `stopped:` family held in memory, beside the tree of the stores of rules, and sealed.</summary>
        private static Fakes.MemoryStore Sealed(string name)
        {
            var meanings = (JObject)RuleVectors.Meanings["meanings"];
            var tree = (JObject)meanings.Properties().First(meaning => meaning.Name.StartsWith("rules:", StringComparison.Ordinal)).Value["tree"];
            var stopped = (JObject)meanings[name];
            var store = new Fakes.MemoryStore();
            foreach (var entry in (JObject)stopped["entries"])
            {
                var current = ((JObject)entry.Value["strings"]).Properties().ToDictionary(property => property.Name, property => (string)property.Value);
                store.Put(StoreComposer.BytesOf(entry.Key), current);
            }

            foreach (var deleted in (JArray)stopped["deletions"])
            {
                store.Delete(StoreComposer.BytesOf((string)deleted));
            }

            store.Tree.Add(StoreComposer.BytesOf((string)tree["root"]));
            store.Tree.AddRange(((JObject)tree["groups"]).Properties().Select(group => StoreComposer.BytesOf(group.Name)));
            using (var reading = store.Read())
            {
                string refused;
                var seal = SealActs.Seal(reading, out refused);
                Assert.That(store.Write(new[] { seal }).Taken, Is.True);
                seal.Forget();
            }

            return store;
        }

        /// <summary>A store of the `stopped:` family, read beside the tree of the stores of rules.</summary>
        private static StoreReading Stopped(string name)
        {
            var meanings = (JObject)RuleVectors.Meanings["meanings"];
            var tree = (JObject)meanings.Properties().First(meaning => meaning.Name.StartsWith("rules:", StringComparison.Ordinal)).Value["tree"];
            return RuleVectors.Reading((JObject)meanings[name], tree);
        }

        private static IEnumerable<string> Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? Enumerable.Empty<string>() : list.Split(' ');
        }
    }
}
