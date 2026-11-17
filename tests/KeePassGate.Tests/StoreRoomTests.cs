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
