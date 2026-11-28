using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a stopped store lost, `STORE.md` §624 and §625: every condition that holds, the
    /// entries that are damage, the summary entries deleted that nothing replaces, the records no
    /// summary entry restates, what a deletion record alone states, the lost objects with what the
    /// seal names for each, and the objects a damaged version's deletion record would make refuse.
    /// </summary>
    [TestFixture]
    public sealed class StoreLossesTests
    {
        /// <summary>The measured reading's reasons, and the conditions the product names them.</summary>
        private static readonly Dictionary<string, StoreStop> Reasons = new Dictionary<string, StoreStop>(StringComparer.Ordinal)
        {
            { "no summary entry of the store is in force", StoreStop.NoSummaryEntry },
            { "a summary entry was deleted that nothing in force replaces", StoreStop.SummaryEntryDeleted },
            { "entries of the store have no version that passes", StoreStop.Damage },
            { "a record no summary entry restates", StoreStop.NotRestated },
        };

        private static IEnumerable<TestCaseData> Stopped()
        {
            foreach (var meaning in ((JObject)RuleVectors.Meanings["meanings"]).Properties()
                         .Where(meaning => meaning.Name.StartsWith("stopped:", StringComparison.Ordinal)))
            {
                yield return new TestCaseData(meaning.Name).SetArgDisplayNames(meaning.Name);
            }
        }

        [TestCaseSource(nameof(Stopped))]
        public void What_a_stopped_store_lost_is_what_the_measured_reading_listed(string name)
        {
            // `tests/Shared/StoreMeaningVectors.json` holds, for each store of the family, what
            // `probe/read.py` listed of what it lost, with its listing on: every condition that
            // holds, every list, and what the seal names for each lost object. The family's stores
            // hold no tree of their own; the measured reading read them beside the nodes their
            // answer about rules names, the tree of the stores of rules.
            var meanings = (JObject)RuleVectors.Meanings["meanings"];
            var store = (JObject)meanings[name];
            var expected = (JObject)store["losses"];
            var tree = (JObject)meanings.Properties().First(meaning => meaning.Name.StartsWith("rules:", StringComparison.Ordinal)).Value["tree"];
            Assert.That(
                ((JObject)store["rules"]["gives"]).Properties().Select(node => node.Name),
                Is.EquivalentTo(new[] { (string)tree["root"] }.Concat(((JObject)tree["groups"]).Properties().Select(group => group.Name))),
                "the nodes the answer names are the tree's");
            using (var reading = RuleVectors.Reading(store, tree))
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Stopped), reading.Reason);
                var losses = StoreLosses.Of(reading);
                Assert.That(losses, Is.Not.Null);
                Assert.That(losses.Reason, Is.EqualTo(reading.Reason));

                Assert.That(losses.Stops, Is.EqualTo(((JArray)expected["reasons"]).Select(reason => Reasons[(string)reason])), "every condition that holds");
                Assert.That(
                    losses.Damaged.Select(damaged => damaged.Hex + " " + damaged.Kind + " " + damaged.Title()).OrderBy(line => line, StringComparer.Ordinal),
                    Is.EqualTo(((JArray)expected["damaged"]).Select(damaged => (string)damaged["id"] + " " + (string)damaged["kind"] + " " + (string)damaged["title"])),
                    "the entries that are damage");
                // Where no summary entry is in force the measured reading lists no summary entry,
                // and the product lists the summary entries a deletion record tells of at the
                // greatest height of any summary entry of the store, the summary entry deleted
                // last, `StoreLosses.Heads`.
                var noneInForce = ((JArray)expected["reasons"]).Any(reason => (string)reason == "no summary entry of the store is in force");
                if (noneInForce)
                {
                    Assert.That(Strings(expected["heads"]), Is.Empty, "the measured reading lists no summary entry with none in force");
                }

                Assert.That(
                    losses.Heads,
                    Is.EqualTo(noneInForce ? DeletedLast(store) : Strings(expected["heads"])),
                    "the summary entries deleted that nothing replaces");
                Assert.That(losses.NotRestated, Is.EqualTo(Strings(expected["notRestated"])), "the records no summary entry restates");
                Assert.That(losses.Revoked, Is.EqualTo(Strings(expected["byDeletion"]["revoked"])), "the revocations a deletion record alone states");
                Assert.That(losses.Retired, Is.EqualTo(Strings(expected["byDeletion"]["retired"])), "the retirements a deletion record alone states");
                Assert.That(
                    losses.Removed.Select(removal => removal.Key + " " + removal.Value),
                    Is.EqualTo(Strings(expected["byDeletion"]["removed"])),
                    "the removals a deletion record alone states");
                Assert.That(
                    losses.Lost.Select(loss => loss.Object + " " + string.Join(" ", loss.Lost)),
                    Is.EqualTo(((JObject)expected["lost"]).Properties().Select(lost => lost.Name + " " + string.Join(" ", Strings(lost.Value)))),
                    "the objects lost, with the deletion records that tell of them");
                Assert.That(losses.RefusesAfter, Is.EqualTo(Strings(expected["refusesAfter"])), "the objects a damaged version's deletion record would make refuse");
                Assert.That(
                    losses.Lost.Select(loss => loss.Object + " after " + string.Join(" ", loss.After) + " lost " + string.Join(" ", loss.Settles)),
                    Is.EqualTo(((JArray)expected["seal"]).Select(seal =>
                        (string)seal["object"] + " after " + string.Join(" ", Strings(seal["after"])) + " lost " + string.Join(" ", Strings(seal["lost"])))),
                    "what the seal names for each lost object");
            }
        }

        [Test]
        public void A_store_that_is_whole_or_holds_no_key_lost_nothing_to_list()
        {
            Assert.That(StoreLosses.Of(new ComposedStore(One).Read()), Is.Null, "whole");

            var none = new ComposedStore(One);
            none.Drop(none.Authority);
            Assert.That(StoreLosses.Of(none.Read()), Is.Null, "no store key");
        }

        [Test]
        public void Every_condition_that_holds_is_listed_and_not_the_first_alone()
        {
            // A summary entry deleted that nothing replaces, beside the one in force; an entry
            // edited so that no version passes; and a revocation no summary entry restates. The
            // reading names the first; the listing names all three.
            var store = new ComposedStore(One);
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 7, new string('a', 64)));
            var edited = store.Revoked('c');
            store.Replace(edited, "{\"client\":\"edited\"}");
            store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('d'), StoreComposer.Content(("client", Hex('d')))));

            var reading = store.Read();
            var losses = StoreLosses.Of(reading);

            Assert.That(reading.Reason, Does.StartWith("a summary entry was deleted"));
            Assert.That(losses.Stops, Is.EqualTo(new[] { StoreStop.SummaryEntryDeleted, StoreStop.Damage, StoreStop.NotRestated }));
            Assert.That(losses.Heads, Has.Count.EqualTo(1));
            Assert.That(losses.Damaged.Single().Hex, Is.EqualTo(edited.Hex));
            Assert.That(losses.Damaged.Single().Kind, Is.EqualTo("revoked"));
            Assert.That(losses.NotRestated, Has.Count.EqualTo(1));
        }

        [Test]
        public void With_no_summary_entry_in_force_the_summary_entries_deleted_last_are_listed()
        {
            // Every write deletes the summary entry before it with a deletion record: three
            // writes, and the last summary entry deleted by hand with one. The listing names the
            // summary entry deleted last, of the greatest height, and only that one.
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            foreach (var height in new[] { 1, 2, 3 })
            {
                store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", height, new string((char)('4' + height), 64)));
            }

            var losses = StoreLosses.Of(store.Read());

            Assert.That(losses.Stops, Is.EqualTo(new[] { StoreStop.NoSummaryEntry }));
            Assert.That(losses.Heads, Is.EqualTo(new[] { StoreComposer.HexOf(StoreComposer.IdentifierOf(One, "head", "store", 3, new string('7', 64))) }));

            // A summary entry above them edited so that no version passes: it is the summary
            // entry the stop is about, listed as damage, and no summary entry told of is listed.
            var edited = store.Add(Head(One, 4, string.Empty));
            store.Replace(edited, "{\"edited\":\"\"}");

            var under = StoreLosses.Of(store.Read());

            Assert.That(under.Damaged.Single().Hex, Is.EqualTo(edited.Hex));
            Assert.That(under.Heads, Is.Empty);
        }

        [Test]
        public void A_record_no_summary_entry_restates_is_asked_of_the_summary_entries_present_and_none_in_force_of_those_in_force()
        {
            // As `probe/read.py` keys them: no summary entry in force is asked of the summary
            // entries in force, and a record no summary entry restates of the summary entries
            // present, in force or not, both what restates and whether it is a condition. The
            // only summary entry is present beside its own deletion record and restates the
            // revocation of c, and the revocation of d is restated by none: both conditions hold,
            // and only d is a record no summary entry restates.
            var store = new ComposedStore(One);
            store.Revoked('c');
            store.Delete(store.Head);
            var restatedByNone = store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('d'), StoreComposer.Content(("client", Hex('d')))));

            var losses = StoreLosses.Of(store.Read());

            Assert.That(losses.Stops, Is.EqualTo(new[] { StoreStop.NoSummaryEntry, StoreStop.NotRestated }));
            Assert.That(losses.NotRestated, Is.EqualTo(new[] { restatedByNone.Hex }));
        }

        [Test]
        public void What_a_deletion_record_alone_states_is_told_apart_from_a_record_and_an_item()
        {
            // `STORE.md` §623 step 2: each is in force by the union either way, and the dialog
            // shows as deleted only what no record present and no item names. A certificate taken
            // away is told of by the admission key a record of kind `client` names.
            var store = new ComposedStore(One);
            store.Revoked('c');
            store.Add(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("certificate", Hex('f')), ("client", Hex('e')))));
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('c'), 0, new string('6', 64)));
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('e'), 0, new string('5', 64)));
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "retired", "authority:" + Hex('a'), 0, new string('5', 64)));
            var removal = StoreComposer.IdentifierOf(One, "removed", "profile:reader", 0, new string('5', 64));
            store.Add(StoreComposer.Record(One, "profile", "profile:reader", StoreComposer.Content(("after", string.Empty), ("name", "reader"))));
            store.Deleted.Add(removal);
            var withdrawn = StoreComposer.IdentifierOf(One, "withdrawn", "certificate:" + Hex('f'), 0, new string('5', 64));
            store.Deleted.Add(withdrawn);
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 9, new string('5', 64)));

            var losses = StoreLosses.Of(store.Read());

            Assert.That(losses.Revoked, Is.EqualTo(new[] { Hex('e') }), "c is revoked by a record and its item as well");
            Assert.That(losses.Retired, Is.EqualTo(new[] { Hex('a') }));
            Assert.That(losses.Removed.Select(pair => pair.Key + " " + pair.Value), Is.EqualTo(new[] { "reader " + StoreComposer.HexOf(removal) }));
            Assert.That(losses.Withdrawn.Select(pair => pair.Key + " " + pair.Value), Is.EqualTo(new[] { StoreComposer.HexOf(withdrawn) + " " + Hex('f') }));
        }

        [Test]
        public void The_seal_of_a_lost_object_names_every_version_of_it_in_force_and_no_identifier_in_both_lists()
        {
            // Two versions of a row made apart, the first naming B in `after` and the second in
            // `lost`, and a deletion record at the row's place that neither names: the seal names
            // both versions and B in `after`, and in `lost` the deletion record alone.
            var row = StoreComposer.RowObject(new string('1', 32), "pf");
            var named = StoreComposer.RuleVersion(One, row, "row:R");
            var first = StoreComposer.RuleVersion(One, row, "row:RU", named.Hex);
            var second = StoreComposer.RuleVersion(One, row, "row:U", Array.Empty<string>(), new[] { named.Hex });
            var store = new ComposedStore(One);
            store.Add(first);
            store.Add(second);
            store.RestateAll(new[] { first, second }.Select(ItemOf));
            var lost = StoreComposer.IdentifierOf(One, "version", "rule:" + new string('1', 32), StoreComposer.ProfileTag(One, "pf"), new string('5', 64));
            store.Deleted.Add(lost);
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 9, new string('5', 64)));

            var losses = StoreLosses.Of(store.Read());

            var loss = losses.Lost.Single();
            Assert.That(loss.Object, Is.EqualTo(row));
            Assert.That(loss.Lost, Is.EqualTo(new[] { StoreComposer.HexOf(lost) }));
            Assert.That(loss.After, Is.EqualTo(new[] { first.Hex, second.Hex, named.Hex }.OrderBy(id => id, StringComparer.Ordinal)));
            Assert.That(loss.Settles, Is.EqualTo(new[] { StoreComposer.HexOf(lost) }));
            Assert.That(loss.After.Intersect(loss.Settles), Is.Empty);
        }

        [Test]
        public void An_entry_that_is_damage_is_listed_by_its_title_as_the_entry_holds_it()
        {
            var store = new ComposedStore(One);
            var edited = store.Revoked('c');
            store.Replace(edited, "{\"client\":\"edited\"}");
            var read = store.Read();
            var versions = new StoreVersions();
            var titled = read.Candidates
                .Select(candidate => candidate.Hex == edited.Hex
                    ? new StoreCandidate(candidate.Identifier, candidate.Versions, "KPG revoked 0007", versions)
                    : candidate)
                .ToList();

            var damaged = StoreLosses.Of(StoreReading.Of(titled, read.Deletions, null, new StoreKept())).Damaged.Single();

            Assert.That(damaged.Title(), Is.EqualTo("KPG revoked 0007"));
        }

        private static IEnumerable<string> Strings(JToken list)
        {
            return ((JArray)list).Select(value => (string)value);
        }

        /// <summary>
        /// The deletion records of a store of the meanings that tell of a summary entry, by the
        /// tag and subject every summary entry's identifier begins with under the stores' key, at
        /// the greatest height of those and of the store's entries of a summary entry, ascending.
        /// </summary>
        private static IEnumerable<string> DeletedLast(JObject store)
        {
            var key = StoreComposer.BytesOf((string)RuleVectors.Meanings["key"]);
            var start = (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2;
            var heads = StoreComposer.HexOf(StoreComposer.IdentifierOf(key, "head", "store", 0, new string('0', 64))).Substring(0, start);
            var told = ((JArray)store["deletions"]).Select(deleted => (string)deleted).Where(deleted => deleted.StartsWith(heads, StringComparison.Ordinal)).ToList();
            var present = ((JObject)store["entries"]).Properties().Select(entry => entry.Name).Where(entry => entry.StartsWith(heads, StringComparison.Ordinal));
            var height = new Func<string, int>(identifier => Convert.ToInt32(identifier.Substring(start, StoreFormat.HeightLength * 2), 16));
            var greatest = told.Concat(present).Select(height).DefaultIfEmpty(0).Max();
            return told.Where(deleted => height(deleted) == greatest).Distinct().OrderBy(deleted => deleted, StringComparer.Ordinal);
        }

        /// <summary>The item `version` that restates this version, as its record holds it.</summary>
        private static string ItemOf(StoreComposer.Composed version)
        {
            var content = JObject.Parse(version.Strings[StoreComposer.Data]);
            return StoreComposer.RuleVersionItem(
                version, (string)content["object"], (string)content["doc"], Listed((string)content["after"]), Listed((string)content["lost"]));
        }

        private static string[] Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? Array.Empty<string>() : list.Split(' ');
        }
    }
}
