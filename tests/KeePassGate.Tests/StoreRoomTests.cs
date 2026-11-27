using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Whether a store has room for its next write, `STORE.md` §627 and §643: the summary entry
    /// a write that adds nothing would write, held to the plugin's own ceilings.
    /// </summary>
    /// <remarks>
    /// **The figures are of a summary entry's content**, which holds its `after`, its `height` and
    /// its `nonce`: the `after` holds 33 characters for each summary entry it names but the last,
    /// which holds 32, and the rest of the content 67 characters, a nonce of 32 among them, and
    /// the height's digits. A store
    /// whose summary entry of height 1 names the identifiers given is about to write a summary
    /// entry of height 2, naming the same and the summary entry itself. So a summary entry naming
    /// 7,941 holds an `after` of 262,052 characters and a content of 262,120, and a summary entry
    /// naming 7,942 a content of 262,153, past the 262,144 the plugin writes a string of the store
    /// with.
    /// </remarks>
    [TestFixture]
    public sealed class StoreRoomTests
    {
        [Test]
        public void A_store_at_the_ceiling_of_a_string_has_no_room_and_the_same_store_a_summary_entry_short_of_it_has_room()
        {
            var fits = Naming(7941);
            Assert.That(fits.State, Is.EqualTo(StoreState.Whole), fits.Reason);
            Assert.That(fits.Room.Named, Is.EqualTo(7941));
            Assert.That(fits.Room.HasRoom, Is.True);

            var over = Naming(7942);
            Assert.That(over.Room.Named, Is.EqualTo(7942));
            Assert.That(over.Room.HasRoom, Is.False);
            Assert.That(over.State, Is.EqualTo(StoreState.Stopped), "a store with no room is answered as a stopped store");
            Assert.That(over.Reason, Does.Contain("no room").And.Contain("7942").And.Contain("262144"));
            Assert.That(StoreWords.Of(over.State), Is.EqualTo("stopped"));
        }

        [Test]
        public void A_store_at_the_ceiling_of_items_has_no_room_and_the_same_store_an_item_short_of_it_has_room()
        {
            // The item `format` and an item for each revocation.
            var fits = Holding(9998);
            Assert.That(fits.State, Is.EqualTo(StoreState.Whole), fits.Reason);
            Assert.That(fits.Room.Items, Is.EqualTo(9998));
            Assert.That(fits.Room.HasRoom, Is.True);

            var over = Holding(9999);
            Assert.That(over.Room.Items, Is.EqualTo(9999));
            Assert.That(over.Room.HasRoom, Is.False, "no item name is left for a further item");
            Assert.That(over.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(over.Reason, Does.Contain("no room").And.Contain("9999"));
        }

        [Test]
        public void Two_stores_that_each_have_room_synchronized_are_judged_by_what_the_next_write_would_name()
        {
            var first = Head(One, 1, Identifiers(0, 3999));
            var second = Head(One, 1, Identifiers(100000, 3999));

            var alone = new ComposedStore(One);
            alone.Drop(alone.Head);
            alone.Add(first);
            Assert.That(alone.Read().Room.HasRoom, Is.True);

            var other = new ComposedStore(One);
            other.Drop(other.Head);
            other.Add(second);
            Assert.That(other.Read().Room.HasRoom, Is.True);

            // Both summary entries in force after the merge, and the next write names both, and
            // everything each names.
            var merged = new ComposedStore(One);
            merged.Drop(merged.Head);
            merged.Add(first);
            merged.Add(second);
            var reading = merged.Read();
            Assert.That(reading.Room.Named, Is.EqualTo(8000));
            Assert.That(reading.Room.HasRoom, Is.False);
            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void What_the_next_summary_entry_names_counts_each_summary_entry_once_however_many_name_it()
        {
            // A summary entry replaced, beside its deletion record, and named by the summary
            // entry in force: named once.
            var store = new ComposedStore(One);
            store.Restate();
            store.Restate();

            Assert.That(store.Read().Room.Named, Is.EqualTo(3));
        }

        [Test]
        public void Every_value_taken_away_is_an_item_of_the_next_summary_entry_wherever_it_is_carried()
        {
            var store = new ComposedStore(One);
            store.Revoked('c');
            store.Add(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("client", Hex('e')))));
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('e'), 0, new string('5', 64)));

            // The item `format`, the revocation restated, and the revocation a deletion record
            // tells of.
            Assert.That(store.Read().Room.Items, Is.EqualTo(3));
        }

        [Test]
        public void Each_record_that_took_a_certificate_away_counts_an_item_of_its_own()
        {
            // `STORE.md` §627 and §648: two records that took the same certificate away and differ
            // in what they hold are two items, so this store is at the ceiling.
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            var first = store.Add(StoreComposer.Record(
                One, "withdrawn", "certificate:" + Hex('d'), StoreComposer.Content(("certificate", Hex('d')), ("renewed", Hex('1')))));
            var second = store.Add(StoreComposer.Record(
                One, "withdrawn", "certificate:" + Hex('d'), StoreComposer.Content(("certificate", Hex('d')), ("renewed", string.Empty))));
            store.Add(Head(
                One, 1, string.Empty,
                Enumerable.Range(0, StoreFormat.MaxItems - 3)
                    .Select(index => StoreComposer.Item(("client", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "revoked")))
                    .Concat(new[]
                    {
                        StoreComposer.Item(("certificate", Hex('d')), ("id", first.Hex), ("kind", "withdrawn"), ("renewed", Hex('1'))),
                        StoreComposer.Item(("certificate", Hex('d')), ("id", second.Hex), ("kind", "withdrawn"), ("renewed", string.Empty)),
                    })
                    .ToArray()));

            var reading = store.Read();

            Assert.That(reading.InForce, Is.Not.Null, reading.Reason);
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(StoreFormat.MaxItems));
            Assert.That(reading.InForce.Room.HasRoom, Is.False);
        }

        [Test]
        public void A_store_with_no_room_and_any_other_reason_to_stop_is_stopped_for_the_other()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 1, Identifiers(0, 7941)));
            var revoked = store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            store.Replace(revoked, "{\"client\":\"edited\"}");

            var reading = store.Read();

            Assert.That(reading.Room.HasRoom, Is.False);
            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(reading.Reason, Does.Not.Contain("no room"), "damage is named first");
        }

        [Test]
        public void From_half_of_either_ceiling_the_store_is_past_half_its_room()
        {
            // 3,971 identifiers are 131,042 characters, 3,972 are 131,075: half of 262,144 is 131,072.
            Assert.That(Naming(3971).Room.PastHalf, Is.False);
            Assert.That(Naming(3972).Room.PastHalf, Is.True);

            Assert.That(Holding(4999).Room.PastHalf, Is.False);
            Assert.That(Holding(5000).Room.PastHalf, Is.True);
        }

        [Test]
        public void A_store_states_how_many_writes_and_items_still_fit()
        {
            var fresh = new ComposedStore(One).Read().Room;
            Assert.That(fresh.Named, Is.EqualTo(1));
            Assert.That(fresh.Items, Is.EqualTo(1));
            Assert.That(fresh.Writes, Is.EqualTo(7941), "a write names a summary entry more than the write before it");
            Assert.That(fresh.ItemsLeft, Is.EqualTo(9998));

            Assert.That(Naming(7941).Room.Writes, Is.EqualTo(1));
            Assert.That(Naming(7942).Room.Writes, Is.EqualTo(0));
            Assert.That(Holding(9999).Room.ItemsLeft, Is.EqualTo(0));
        }

        [Test]
        public void How_far_a_store_has_grown_is_stated_by_its_counts_and_its_ceilings()
        {
            var room = Naming(3972).Room;

            Assert.That(
                room.Grown,
                Does.Contain("3972").And.Contain("131075").And.Contain("262144").And.Contain("9999")
                    .And.Contain(room.Writes.ToString(CultureInfo.InvariantCulture)));
        }

        [TestCase("version")]
        [TestCase("kept")]
        [TestCase("removed")]
        [TestCase("retired")]
        [TestCase("withdrawn")]
        public void Each_kind_of_item_counts_toward_the_ceiling_of_items_alone(string kind)
        {
            // `STORE.md` §627: the item `format` and an item for each of these, so a store holding
            // 9,997 of them has room and a store holding 9,998 has none.
            var fits = Carrying(kind, StoreFormat.MaxItems - 2);
            Assert.That(fits.Room.Items, Is.EqualTo(StoreFormat.MaxItems - 1), fits.Reason);
            Assert.That(fits.Room.HasRoom, Is.True);

            var over = Carrying(kind, StoreFormat.MaxItems - 1);
            Assert.That(over.Room.Items, Is.EqualTo(StoreFormat.MaxItems));
            Assert.That(over.Room.HasRoom, Is.False);
        }

        [Test]
        public void Removals_of_a_single_name_count_an_item_for_each_identifier()
        {
            // `STORE.md` §611: a removal is restated by its name and its identifier both.
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(
                One, 1, string.Empty,
                StoreComposer.Item(("id", new string('1', 32)), ("kind", "removed"), ("name", "reader")),
                StoreComposer.Item(("id", new string('2', 32)), ("kind", "removed"), ("name", "reader"))));

            Assert.That(store.Read().InForce.Room.Items, Is.EqualTo(3));
        }

        [Test]
        public void A_version_a_version_in_force_replaces_is_not_counted_whichever_carries_it()
        {
            // `STORE.md` §611 restates a standing version, and §623 step 3: a version that a
            // version in force of the same object names in `after` does not stand. Two summary
            // entries in force, made apart, the second carrying a version that replaces the
            // first's.
            var first = new string('1', 32);
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 1, string.Empty, VersionItemOf(first, string.Empty)));
            store.Add(Head(One, 1, string.Empty, VersionItemOf(new string('2', 32), first, "rule:" + first)));

            var reading = store.Read();

            Assert.That(reading.InForce.HeadsInForce, Has.Count.EqualTo(2));
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(2), "the item format and the standing version");
            Assert.That(reading.InForce.Restated().Count(item => item.Kind == "version"), Is.EqualTo(1));
        }

        [Test]
        public void A_standing_answer_to_a_renewal_counts_an_item_and_an_answer_it_replaces_none()
        {
            // `STORE.md` §618 and §643: a standing answer is restated by the summary entry for good,
            // an item for each client ever answered; an answer replaced by the next answer about
            // the client is no standing version and counts nothing, and an answer of an object of
            // nothing is a record, restated and counted as any.
            var client = new string('3', 64);
            var store = new ComposedStore(One);
            var first = StoreComposer.RuleVersion(One, "renew:" + client, "refuse:forever");
            var lift = store.Add(StoreComposer.RuleVersion(One, "renew:" + client, "ask", first.Hex));
            var other = store.Add(StoreComposer.RuleVersion(One, "renew:" + new string('4', 64), "refuse:until:2026-12-14T23:00:00Z"));
            var nothing = store.Add(StoreComposer.RuleVersion(One, "renew:" + new string('C', 64), "refuse:forever"));
            store.RestateAll(new[] { lift, other, nothing }.Select(version =>
            {
                var content = Newtonsoft.Json.Linq.JObject.Parse(version.Strings[StoreComposer.Data]);
                var after = (string)content["after"];
                return StoreComposer.RuleVersionItem(
                    version, (string)content["object"], (string)content["doc"], string.IsNullOrEmpty(after) ? System.Array.Empty<string>() : after.Split(' '));
            }));
            store.Add(first);
            store.Delete(first);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(4), "the item format and the three standing answers");
            Assert.That(
                reading.InForce.Restated().Where(item => item.Kind == "version").Select(item => item.Member("id")),
                Is.EquivalentTo(new[] { lift.Hex, other.Hex, nothing.Hex }));
        }

        [Test]
        public void The_next_height_stops_at_its_ceiling_and_a_fifth_digit_takes_a_character()
        {
            Assert.That(Heading(StoreFormat.MaxNumber).Room.Height, Is.EqualTo(StoreFormat.MaxNumber));
            Assert.That(Heading(StoreFormat.MaxNumber - 1).Room.Height, Is.EqualTo(StoreFormat.MaxNumber));
            Assert.That(
                Heading(9999).Room.Content - Heading(9998).Room.Content,
                Is.EqualTo(1),
                "the next height of 10,000 is a digit longer than 9,999");
            Assert.That(
                Heading(StoreFormat.MaxNumber).Room.Content,
                Is.EqualTo(Heading(StoreFormat.MaxNumber - 1).Room.Content),
                "at the ceiling the next height is the same");
        }

        [Test]
        public void A_deletion_record_of_a_summary_entry_raises_the_next_height_and_is_named_where_no_after_names_it()
        {
            // `STORE.md` §611: the next summary entry is higher than any summary entry present or
            // told of, and names every summary entry a deletion record tells of.
            var store = new ComposedStore(One);
            var told = StoreComposer.IdentifierOf(One, "head", "store", 7, new string('5', 64));
            store.Deleted.Add(told);

            var room = store.Read().InForce.Room;

            Assert.That(room.Height, Is.EqualTo(8));
            Assert.That(store.Read().InForce.After, Does.Contain(KeePassGate.Hex.Of(told)));
            Assert.That(room.Named, Is.EqualTo(2), "the summary entry present and the summary entry told of");
        }

        [Test]
        public void The_seal_s_write_is_held_to_the_room_as_every_write_is()
        {
            // `STORE.md` §625 and §643: a store stopped by a summary entry deleted that nothing
            // replaces, whose next summary entry names as many as a string holds, is sealed; a
            // summary entry more, and it has no room, which the seal does not lift.
            var fits = Stopped(7941);
            Assert.That(fits.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(fits.Room.Named, Is.EqualTo(7941));
            string refused;
            var seal = KeePassGate.Authority.SealActs.Seal(fits, out refused);
            Assert.That(seal, Is.Not.Null, refused);
            var write = StoreWrite.Plan(fits, new[] { seal }, "0.3.0");
            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Add[write.Add.Count - 1].Content.Length, Is.LessThanOrEqualTo(StoreFormat.MaxWritten));

            var over = Stopped(7942);
            Assert.That(over.Room.HasRoom, Is.False);
            Assert.That(KeePassGate.Authority.SealActs.Seal(over, out refused), Is.Null);
            Assert.That(refused, Does.Contain("no room"));
            Assert.That(StoreWrite.Plan(over, new[] { seal }, "0.3.0").NoRoom, Is.True);
            seal.Forget();
        }

        /// <summary>
        /// A store stopped by a summary entry deleted that nothing replaces, whose next summary
        /// entry names this many summary entries.
        /// </summary>
        private static StoreReading Stopped(int named)
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 1, Identifiers(0, named - 2)));
            store.Deleted.Add(StoreComposer.IdentifierOf(One, "head", "store", 2, new string('5', 64)));
            return store.Read();
        }

        /// <summary>A whole store whose summary entry carries this many items of a kind, beside the item format.</summary>
        private static StoreReading Carrying(string kind, int count)
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 1, string.Empty, Enumerable.Range(0, count).Select(index => ItemOf(kind, index)).ToArray()));
            return store.Read();
        }

        private static string ItemOf(string kind, int index)
        {
            var id = index.ToString("x32", CultureInfo.InvariantCulture);
            var value = index.ToString("x64", CultureInfo.InvariantCulture);
            switch (kind)
            {
                case "version":
                    return VersionItemOf(id, string.Empty);
                case "kept":
                    return StoreComposer.Item(("after", string.Empty), ("id", id), ("key", StoreComposer.KeyName(Three)), ("kind", "kept"));
                case "removed":
                    return StoreComposer.Item(("id", id), ("kind", "removed"), ("name", "reader"));
                case "retired":
                    return StoreComposer.Item(("authority", value), ("kind", "retired"));
                default:
                    return StoreComposer.Item(("certificate", value), ("id", id), ("kind", "withdrawn"), ("renewed", string.Empty));
            }
        }

        /// <summary>The item of a version under this identifier, of its own object unless another is given.</summary>
        private static string VersionItemOf(string id, string after, string objectName = null)
        {
            return StoreComposer.Item(
                ("after", after), ("doc", "a rule"), ("id", id), ("kind", "version"), ("object", objectName ?? "rule:" + id));
        }

        /// <summary>A whole store whose summary entry stands at this height.</summary>
        private static StoreReading Heading(int height)
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, height, string.Empty));
            return store.Read();
        }

        /// <summary>A whole store whose next summary entry names this many summary entries.</summary>
        private static StoreReading Naming(int named)
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 1, Identifiers(0, named - 1)));
            return store.Read();
        }

        /// <summary>A whole store whose next summary entry holds this many items.</summary>
        private static StoreReading Holding(int items)
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(
                One, 1, string.Empty,
                Enumerable.Range(0, items - 1)
                    .Select(index => StoreComposer.Item(("client", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "revoked")))
                    .ToArray()));
            return store.Read();
        }

        /// <summary>Identifiers of summary entries the file does not hold, as an `after` names them.</summary>
        private static string Identifiers(int from, int count)
        {
            return string.Join(" ", Enumerable.Range(from, count).Select(index => index.ToString("x32", CultureInfo.InvariantCulture)));
        }
    }
}
