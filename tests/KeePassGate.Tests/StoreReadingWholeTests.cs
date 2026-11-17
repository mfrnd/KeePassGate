using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Whether a store is whole, `STORE.md` §624: its summary entry in force, and every decision
    /// restated by one.
    /// </summary>
    [TestFixture]
    public sealed class StoreReadingWholeTests
    {
        [Test]
        public void A_store_with_no_summary_entry_in_force_is_stopped()
        {
            var none = new ComposedStore(One);
            none.Drop(none.Head);
            Assert.That(none.Read().State, Is.EqualTo(StoreState.Stopped));

            var deleted = new ComposedStore(One);
            deleted.Delete(deleted.Head);
            Assert.That(deleted.Read().State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_summary_entry_deleted_and_named_by_nothing_in_force_stops_the_store_and_one_replaced_does_not()
        {
            // Restating deletes the summary entry it replaces and names it, as a write does.
            var store = new ComposedStore(One);
            store.Restate();
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole), "the summary entry in force names it");

            store.Delete(Head(One, 7, string.Empty));
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped), "nothing names it");
        }

        [Test]
        public void A_deletion_record_with_the_tag_of_a_summary_entry_and_another_subject_stops_nothing()
        {
            var store = new ComposedStore(One);
            store.Deleted.Add(StoreComposer.Tag(One, "head").Concat(new byte[13]).ToArray());

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }

        [TestCase("revoked")]
        [TestCase("retired")]
        [TestCase("removed")]
        [TestCase("withdrawn")]
        [TestCase("kept")]
        [TestCase("version")]
        public void A_decision_no_summary_entry_restates_stops_the_store_and_restated_it_is_whole(string kind)
        {
            var store = new ComposedStore(One);
            var item = store.Decision(kind);
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped), "not restated");

            store.Restate(item);
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole), "restated");
        }

        [Test]
        public void A_removal_is_restated_only_by_an_item_naming_its_identifier_as_well_as_its_name()
        {
            // `STORE.md` §658: an item `removed` with the same name, whose `id` is the record's
            // identifier. Another removal of the same name restates this one in nothing.
            var store = new ComposedStore(One);
            store.Add(StoreComposer.Record(One, "removed", "profile:reader", StoreComposer.Content(("name", "reader"))));
            store.Restate(StoreComposer.Item(("id", new string('7', 32)), ("kind", "removed"), ("name", "reader")));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_removal_is_restated_by_its_members_compared_each_on_its_own()
        {
            // A line feed is a character a member's value may hold, written escaped in its JSON:
            // a name holding one and an identifier holding one must not join into another
            // removal's name and identifier.
            var store = new ComposedStore(One);
            var removed = store.Add(StoreComposer.Record(One, "removed", "profile:a\nb", StoreComposer.Content(("name", "a\nb"))));
            store.Restate(StoreComposer.Item(("id", "b\n" + removed.Hex), ("kind", "removed"), ("name", "a")));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_version_another_version_names_needs_no_summary_entry()
        {
            var store = new ComposedStore(One);
            var earlier = store.Add(Version(One, "rule:" + new string('1', 32), string.Empty));
            var later = store.Add(Version(One, "rule:" + new string('1', 32), earlier.Hex));
            store.Restate(VersionItem(later, "rule:" + new string('1', 32), earlier.Hex));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }

        [TestCase(0)]
        [TestCase(65536)]
        public void A_summary_entry_whose_height_is_out_of_its_range_fails(int height)
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(StoreComposer.Record(
                One, "head", "store",
                StoreComposer.Content(("after", string.Empty), ("height", height.ToString(CultureInfo.InvariantCulture))),
                height & 0xffff,
                new[] { FormatItem(StoreFormat.Number) }));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_summary_entry_whose_height_is_not_its_identifier_s_fails()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(StoreComposer.Record(
                One, "head", "store",
                StoreComposer.Content(("after", string.Empty), ("height", "2")),
                3,
                new[] { FormatItem(StoreFormat.Number) }));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_summary_entry_of_the_last_height_naming_another_of_that_height_is_in_force()
        {
            var store = new ComposedStore(One);
            var first = Head(One, 65535, string.Empty);
            store.Drop(store.Head);
            store.Add(first);
            store.Delete(first);
            store.Add(Head(One, 65535, first.Hex));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }
    }
}
