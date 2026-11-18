using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The plan of a write of the plugin's own, `STORE.md` §627: each step alone, each state's
    /// refusal, the version record, the summary entry, the ceilings, and the room it agrees with.
    /// </summary>
    [TestFixture]
    public sealed class StoreWriteTests
    {
        private const string Plugin = "0.3.0";

        [Test]
        public void A_Create_where_no_key_stands_starts_a_store_with_a_version_record_and_a_summary_entry()
        {
            var key = StoreFormat.NewKey();
            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Create(key, 'a') }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Starts, Is.True);
            Assert.That(write.Add.Select(record => record.Kind), Is.EqualTo(new[] { "format", "authority", "head" }));
            var head = SummaryOf(write);
            Assert.That(head.Member("height"), Is.EqualTo("1"));
            Assert.That(head.Member("after"), Is.Empty);
            Assert.That(Items(head), Is.EqualTo(new[] { "{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.3.0\"}" }));
            Assert.That(write.Delete, Is.Empty);
            Assert.That(write.Deletions, Is.Empty);
        }

        [Test]
        public void A_store_written_reads_whole_with_its_authority_standing()
        {
            var store = new MemoryStore();
            var key = StoreFormat.NewKey();
            Assert.That(store.Write(new[] { Create(key, 'a') }).Taken, Is.True);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.Answering.Select(record => record.Form.Member("id")), Is.EqualTo(new[] { Hex('a') }));
        }

        [Test]
        public void Every_state_but_a_whole_store_and_no_key_refuses_a_write_with_its_word_Create_among_them()
        {
            var split = new ComposedStore(One);
            split.Add(StoreRecords.Authority(Two, 'b'));
            AssertRefused(split.Read(), Create(One, 'c'), "split");

            AssertRefused(new ComposedStore(One, StoreFormat.Number + 1).Read(), Create(One, 'c'), "outdated");

            // A stopped store with no authority standing: Create is refused all the same, `STORE.md`
            // §624.
            var stopped = new ComposedStore(One);
            stopped.Drop(stopped.Authority);
            stopped.Add(StoreComposer.Record(
                One, "retired", "authority:" + Hex('a'),
                StoreComposer.Content(("authority", Hex('a')), ("storeKey", StoreComposer.HexOf(One)))));
            AssertRefused(stopped.Read(), Create(One, 'c'), "stopped");
        }

        [Test]
        public void Where_no_key_stands_only_an_act_that_brings_an_authority_is_taken()
        {
            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Starting(One, Retirement(One, 'a')) }, Plugin);

            Assert.That(write.Refused, Does.Contain("only an authority starts a store"));
        }

        [Test]
        public void A_Create_composed_under_a_store_key_is_left_out_where_no_key_stands_and_starts_nothing()
        {
            // `STORE.md` §614 and §632: Create starts a store under a new key where no key stands,
            // and a Create composed under the key that stood at the press, planned once that key
            // went, would start the store again under it, with every record still in the file.
            var key = StoreFormat.NewKey();
            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Act(key, AuthorityOf(key, 'a')) }, Plugin);

            Assert.That(write.Refused, Does.Contain("composed under a store key that does not stand"));
            Assert.That(write.Starts, Is.False);
        }

        [Test]
        public void An_act_composed_where_no_key_stood_is_left_out_where_a_key_stands()
        {
            var store = Started(out var key, 'a');

            var write = StoreWrite.Plan(store.Read(), new[] { Starting(key, Retirement(key, 'b')) }, Plugin);

            Assert.That(write.Refused, Does.Contain("composed where no store key stood"));
        }

        [Test]
        public void A_retirement_dropped_with_the_authority_it_retires_leaves_its_hold_on_the_key_to_the_retirements_left()
        {
            // A Recreate and then a Delete on one dialog, `STORE.md` §627: the authority the
            // Recreate made goes with the Delete's retirement, which held the key, so the
            // Recreate's retirement holds it in their place and the write is the Delete of what
            // stood.
            var store = Started(out var key, 'a');
            var recreate = new StoreAct(
                "Recreate", key, StoreFormat.KeyName(key), new[] { WithoutKey(key, 'a'), AuthorityOf(key, 'b') }, null);

            var write = StoreWrite.Plan(store.Read(), new[] { recreate, Retire(key, 'b') }, Plugin);

            Assert.That(write.Refused, Is.Null);
            var retirements = write.Add.Where(record => record.Kind == "retired").ToList();
            Assert.That(retirements.Select(record => record.Member("authority")), Is.EqualTo(new[] { Hex('a') }));
            Assert.That(retirements.Single().Protected, Is.True, "the retirement left holds the key");
            Assert.That(write.Add.Any(record => record.Kind == "authority"), Is.False);
        }

        [Test]
        public void An_entry_still_in_the_tree_gets_no_deletion_record_and_no_second_entry_under_its_identifier()
        {
            // `STORE.md` §627: present is every entry of the tree, a record or not. A summary entry
            // that a later summary entry names, back in the tree with every string of the store
            // gone and no deletion record, is neither told of as deleted nor written again.
            var store = Started(out var key, 'a');
            Assert.That(store.Write(new[] { Retire(key, 'z') }).Taken, Is.True);
            var first = store.Deletions.Single();
            var tree = store.Candidates.Select(candidate => candidate.Identifier).Concat(new[] { first }).ToList();

            using (var reading = StoreReading.Afresh(store.Candidates, Array.Empty<byte[]>(), tree))
            {
                Assert.That(StoreWrite.Plan(reading, new[] { Retire(key, 'y') }, Plugin).Deletions, Does.Not.Contain(KeePassGate.Hex.Of(first)));
            }

            using (var reading = StoreReading.Afresh(store.Candidates, Array.Empty<byte[]>()))
            {
                Assert.That(
                    StoreWrite.Plan(reading, new[] { Retire(key, 'y') }, Plugin).Deletions,
                    Does.Contain(KeePassGate.Hex.Of(first)),
                    "an entry the tree does not hold is told of");
            }

            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            using (var reading = StoreReading.Afresh(store.Candidates, store.Deletions, tree.Concat(new[] { name.Identifier })))
            {
                Assert.That(StoreWrite.Plan(reading, new[] { Act(key, name) }, Plugin).Empty, Is.True);
            }
        }

        [Test]
        public void An_act_whose_ground_went_is_left_out_and_named_and_a_single_act_is_refused_for_it()
        {
            var store = Started(out var key, 'a');
            var stale = new StoreAct(
                "a stale act", key, StoreFormat.KeyName(key), new[] { Retirement(key, 'a') }, ground => ground.Standing.Contains(Hex('z')) ? null : "z does not stand");

            var alone = StoreWrite.Plan(store.Read(), new[] { stale }, Plugin);
            Assert.That(alone.Refused, Is.EqualTo("a stale act: z does not stand"));

            var beside = StoreWrite.Plan(store.Read(), new[] { stale, Retire(key, 'a') }, Plugin);
            Assert.That(beside.Refused, Is.Null);
            Assert.That(beside.LeftOut, Is.EqualTo(new[] { "a stale act: z does not stand" }));
            Assert.That(beside.Taken, Has.Count.EqualTo(1));
        }

        [Test]
        public void An_act_composed_under_a_key_that_does_not_stand_is_left_out()
        {
            var store = Started(out var key, 'a');

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(StoreFormat.NewKey(), 'a') }, Plugin);

            Assert.That(write.Refused, Does.Contain("under a store key that does not stand"));
        }

        [Test]
        public void An_act_is_held_to_its_ground_with_the_acts_before_it_taken()
        {
            var store = new MemoryStore();
            var key = StoreFormat.NewKey();
            var create = Create(key, 'a');
            var needsA = new StoreAct(
                "needs a", key, StoreFormat.KeyName(key), new[] { Retirement(key, 'a') }, ground => ground.Standing.Contains(Hex('a')) ? null : "a does not stand");

            var write = StoreWrite.Plan(store.Read(), new[] { create, needsA }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Taken, Has.Count.EqualTo(2));
            Assert.That(write.Add.Select(record => record.Kind), Does.Not.Contain("authority"), "an authority added and retired in one write is never added");
        }

        [Test]
        public void A_retirement_deletes_the_authority_it_names_and_every_summary_entry_present()
        {
            var store = Started(out var key, 'a');
            var before = store.Read();
            var authority = before.InForce.Authorities[0].Hex;
            var head = before.InForce.Heads[0].Hex;

            var write = StoreWrite.Plan(before, new[] { Retire(key, 'a') }, Plugin);

            Assert.That(write.Delete, Is.EquivalentTo(new[] { authority, head }));
            Assert.That(SummaryOf(write).Member("after"), Is.EqualTo(head));
            Assert.That(SummaryOf(write).Member("height"), Is.EqualTo("2"));
            Assert.That(Items(SummaryOf(write)), Does.Contain("{\"authority\":\"" + Hex('a') + "\",\"kind\":\"retired\"}"));

            Assert.That(store.Write(new[] { Retire(key, 'a') }).Taken, Is.True);
            var after = store.Read();
            Assert.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);
            Assert.That(after.Answering, Is.Empty);
            Assert.That(after.Found(key), Is.EqualTo(1), "the retirement holds the key");
        }

        [Test]
        public void A_write_that_would_leave_no_entry_holding_the_store_key_is_refused()
        {
            var store = Started(out var key, 'a');
            var bare = StoreRecord.Compose(key, "retired", new Dictionary<string, string> { { "authority", Hex('a') } });

            var write = StoreWrite.Plan(store.Read(), new[] { Act(key, bare) }, Plugin);

            Assert.That(write.Refused, Is.EqualTo("the write would leave no entry holding the store key"));
            Assert.That(write.Add, Is.Empty);
        }

        [Test]
        public void A_summary_entry_named_and_not_in_the_file_gets_a_deletion_record()
        {
            var store = new ComposedStore(One);
            store.Restate();
            var named = store.Head.Strings[StoreComposer.Data];
            var reading = store.Read();
            var earlier = reading.InForce.After.Single(identifier => identifier != store.Head.Hex);

            var write = StoreWrite.Plan(reading, new[] { Retire(One, 'a') }, Plugin);

            Assert.That(named, Does.Contain(earlier));
            Assert.That(write.Deletions, Is.Empty, "the file holds its deletion record already");

            var dropped = new ComposedStore(One);
            dropped.Drop(dropped.Head);
            dropped.Add(Head(One, 1, new string('7', 32)));
            Assert.That(StoreWrite.Plan(dropped.Read(), new[] { Retire(One, 'a') }, Plugin).Deletions, Is.EqualTo(new[] { new string('7', 32) }));
        }

        [Test]
        public void The_version_record_is_written_at_the_first_write_and_by_a_higher_plugin_and_by_no_other()
        {
            var store = Started(out var key, 'a');
            Assert.That(store.Read().InForce.Plugin, Is.EqualTo("0.3.0"));

            var same = StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "0.3.0");
            Assert.That(same.Add.Select(record => record.Kind), Does.Not.Contain("format"));

            var lower = StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "0.2.9");
            Assert.That(lower.Add.Select(record => record.Kind), Does.Not.Contain("format"));
            Assert.That(Items(SummaryOf(lower)), Does.Contain("{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.3.0\"}"), "the highest stated");

            var higher = StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "0.10.0");
            Assert.That(higher.Add.Count(record => record.Kind == "format"), Is.EqualTo(1));
            Assert.That(Items(SummaryOf(higher)), Does.Contain("{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.10.0\"}"));

            Assert.That(StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "unknown").Refused, Does.Contain("cannot state its version"));
        }

        [Test]
        public void The_height_is_a_step_above_the_highest_and_stays_at_the_last()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 65535, string.Empty));

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(SummaryOf(write).Member("height"), Is.EqualTo("65535"));
        }

        [Test]
        public void Items_of_every_kind_are_carried_whatever_this_build_gives_them_to_mean()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            var version = store.Add(Version(One, "rule:" + new string('1', 32), string.Empty));
            var kept = store.Kept(One, Three);
            var removal = store.Add(StoreComposer.Record(One, "removed", "profile:reader", StoreComposer.Content(("name", "reader")))).Hex;
            store.Add(Head(
                One, 2, string.Empty,
                VersionItem(version, "rule:" + new string('1', 32), string.Empty),
                KeptItem(kept, Three),
                StoreComposer.Item(("id", removal), ("kind", "removed"), ("name", "reader")),
                StoreComposer.Item(("client", Hex('c')), ("kind", "revoked")),
                Statement(new string('6', 32), Hex('d'), Hex('1'), Hex('2'))));
            Assume.That(store.Read().State, Is.EqualTo(StoreState.Whole), store.Read().Reason);

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin);

            var items = Items(SummaryOf(write));
            Assert.That(items, Does.Contain(VersionItem(version, "rule:" + new string('1', 32), string.Empty)));
            Assert.That(items, Does.Contain(KeptItem(kept, Three)));
            Assert.That(items, Does.Contain(StoreComposer.Item(("id", removal), ("kind", "removed"), ("name", "reader"))));
            Assert.That(items, Does.Contain(StoreComposer.Item(("client", Hex('c')), ("kind", "revoked"))));
            Assert.That(items, Does.Contain(Statement(new string('6', 32), Hex('d'), Hex('1'), Hex('2'))), "as it is, every member with it");
        }

        [Test]
        public void A_deletion_record_nothing_answers_for_is_restated_with_no_renewal_and_a_record_an_item_restates_is_not_again()
        {
            // `STORE.md` §648: an item of the deletion record's identifier for a record whose
            // content is nowhere, and nothing more for a deletion record of a record restated.
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            var record = store.Add(StoreComposer.Record(
                One, "withdrawn", "certificate:" + Hex('d'), StoreComposer.Content(("certificate", Hex('d')), ("renewed", Hex('e')))));
            store.Add(Head(One, 2, string.Empty, Statement(record.Hex, Hex('d'), Hex('e'), null)));
            store.Drop(record);
            store.Delete(record);
            var lost = StoreComposer.IdentifierOf(One, "withdrawn", "certificate:" + Hex('e'), 0, new string('5', 64));
            store.Deleted.Add(lost);
            Assume.That(store.Read().State, Is.EqualTo(StoreState.Whole), store.Read().Reason);

            var items = Items(SummaryOf(StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin)))
                .Where(item => item.Contains("\"withdrawn\""))
                .ToList();

            Assert.That(items, Is.EquivalentTo(new[]
            {
                Statement(record.Hex, Hex('d'), Hex('e'), null),
                Statement(KeePassGate.Hex.Of(lost), Hex('e'), string.Empty, null),
            }));
        }

        private static string Statement(string id, string certificate, string renewed, string from)
        {
            var members = new List<(string Name, string Value)> { ("certificate", certificate), ("id", id), ("kind", "withdrawn"), ("renewed", renewed) };
            if (from != null)
            {
                members.Add(("from", from));
            }

            return StoreComposer.Item(members.ToArray());
        }

        [Test]
        public void A_record_counting_by_its_history_is_set_back_to_the_version_that_passes()
        {
            var store = new ComposedStore(One);
            var revoked = store.Revoked('c');
            store.Edit(revoked, "{\"client\":\"edited\"}", "{\"client\":\"edited again\"}");

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin);

            Assert.That(write.SetBack, Is.EqualTo(new[] { new KeyValuePair<string, int>(revoked.Hex, 2) }));
        }

        [Test]
        public void A_write_whose_strings_pass_the_plugin_s_ceiling_is_refused_with_nothing_planned()
        {
            var store = Started(out var key, 'a');
            var long_ = StoreRecord.Compose(key, "retired", new Dictionary<string, string>
            {
                { "authority", Hex('a') },
                { "note", new string('x', StoreFormat.MaxWritten) },
                { "storeKey", StoreComposer.HexOf(key) },
            });
            var fits = StoreRecord.Compose(key, "retired", new Dictionary<string, string>
            {
                { "authority", Hex('a') },
                { "note", new string('x', StoreFormat.MaxWritten - 300) },
                { "storeKey", StoreComposer.HexOf(key) },
            });

            Assert.That(StoreWrite.Plan(store.Read(), new[] { Act(key, fits) }, Plugin).Refused, Is.Null);
            var beyond = StoreWrite.Plan(store.Read(), new[] { Act(key, long_) }, Plugin);
            Assert.That(beyond.Refused, Does.Contain("262144"));
            Assert.That(beyond.Add, Is.Empty);
        }

        [Test]
        public void A_write_the_room_allows_is_never_refused_for_a_ceiling_and_the_first_the_room_refuses_is_refused()
        {
            // A store an item short of its room takes a write that adds an item; with that item
            // in place it has no room, and the plan refuses it as stopped.
            var full = Holding(StoreFormat.MaxItems - 1);
            Assume.That(full.Read().Room.HasRoom, Is.True);
            var written = full.Write(new[] { Retire(One, 'a') });
            Assert.That(written.Taken, Is.True, written.Reason);

            var after = full.Read();
            Assert.That(after.Room.HasRoom, Is.False);
            var refused = StoreWrite.Plan(after, new[] { Retire(One, 'b') }, Plugin);
            Assert.That(refused.Word, Is.EqualTo("stopped"));

            // Named summary entries the same way: the last that fits, and none after it.
            var named = Naming(7940);
            Assume.That(named.Read().Room.HasRoom, Is.True);
            Assert.That(named.Write(new[] { Retire(One, 'a') }).Taken, Is.True);
            Assert.That(named.Read().Room.HasRoom, Is.True);
            Assert.That(named.Write(new[] { Retire(One, 'b') }).Taken, Is.True);
            Assert.That(named.Read().Room.HasRoom, Is.False);
            Assert.That(StoreWrite.Plan(named.Read(), new[] { Retire(One, 'c') }, Plugin).Word, Is.EqualTo("stopped"));
        }

        [Test]
        public void Applied_in_memory_the_plan_leaves_what_the_reading_of_the_file_would()
        {
            var store = Started(out var key, 'a');
            Assert.That(store.Write(new[] { Retire(key, 'a'), Made(key, 'b') }).Taken, Is.True);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.Answering.Select(record => record.Form.Member("id")), Is.EqualTo(new[] { Hex('b') }));
            Assert.That(reading.InForce.Taken("retired"), Is.EqualTo(new[] { Hex('a') }));
            Assert.That(reading.InForce.Records.Count(record => record.Is("authority")), Is.EqualTo(1), "the retired authority's entry is deleted");
        }

        private static void AssertRefused(StoreReading reading, StoreAct act, string word)
        {
            var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
            Assert.That(write.Word, Is.EqualTo(word));
            Assert.That(write.Refused, Does.StartWith("the store is " + word));
            Assert.That(write.Add, Is.Empty);
        }

        private static MemoryStore Started(out byte[] key, char authority)
        {
            key = StoreFormat.NewKey();
            var store = new MemoryStore();
            Assert.That(store.Write(new[] { Create(key, authority) }).Taken, Is.True);
            return store;
        }

        private static MemoryStore Holding(int items)
        {
            var store = new MemoryStore();
            store.Put(StoreRecords.Authority(One, 'a'));
            store.Put(Head(
                One, 1, string.Empty,
                Enumerable.Range(0, items - 1)
                    .Select(index => StoreComposer.Item(("client", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "revoked")))
                    .ToArray()));
            return store;
        }

        private static MemoryStore Naming(int named)
        {
            var store = new MemoryStore();
            store.Put(StoreRecords.Authority(One, 'a'));
            store.Put(Head(
                One, 1, string.Join(" ", Enumerable.Range(0, named - 1).Select(index => index.ToString("x32", CultureInfo.InvariantCulture)))));
            return store;
        }

        /// <summary>A Create where no store key stands, bringing this key.</summary>
        private static StoreAct Create(byte[] key, char id)
        {
            return Starting(key, AuthorityOf(key, id));
        }

        /// <summary>An authority added under the store key that stands, as a Recreate adds it.</summary>
        private static StoreAct Made(byte[] key, char id)
        {
            return Act(key, AuthorityOf(key, id));
        }

        private static StoreRecord AuthorityOf(byte[] key, char id)
        {
            return StoreRecord.Compose(key, "authority", new Dictionary<string, string>
            {
                { "id", Hex(id) },
                { "storeKey", StoreComposer.HexOf(key) },
            });
        }

        private static StoreAct Retire(byte[] key, char id)
        {
            return Act(key, Retirement(key, id));
        }

        /// <summary>A retirement holding no key, as a Recreate's does, `STORE.md` §614.</summary>
        private static StoreRecord WithoutKey(byte[] key, char id)
        {
            return StoreRecord.Compose(key, "retired", new Dictionary<string, string> { { "authority", Hex(id) } });
        }

        /// <summary>A retirement holding the key, as Delete's does, `STORE.md` §614.</summary>
        private static StoreRecord Retirement(byte[] key, char id)
        {
            return StoreRecord.Compose(key, "retired", new Dictionary<string, string>
            {
                { "authority", Hex(id) },
                { "storeKey", StoreComposer.HexOf(key) },
            });
        }

        /// <summary>An act composed against the store key that stands, its records under that key.</summary>
        private static StoreAct Act(byte[] key, StoreRecord record)
        {
            return new StoreAct("an act of the test's", key, StoreFormat.KeyName(key), new[] { record }, null);
        }

        /// <summary>An act composed where no store key stood, bringing this key.</summary>
        private static StoreAct Starting(byte[] key, StoreRecord record)
        {
            return new StoreAct("an act of the test's", key, null, new[] { record }, null);
        }

        private static StoreRecord SummaryOf(StoreWrite write)
        {
            return write.Add[write.Add.Count - 1];
        }

        private static IReadOnlyList<string> Items(StoreRecord head)
        {
            return head.Strings.Where(pair => pair.Key.StartsWith("KPG.S.", StringComparison.Ordinal)).Select(pair => pair.Value).ToList();
        }
    }
}
