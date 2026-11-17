using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The format, `STORE.md` §650 and §651: the number a store is raised to, and the kinds the
    /// plugin does not know.
    /// </summary>
    [TestFixture]
    public sealed class StoreReadingFormatTests
    {
        [Test]
        public void A_version_record_of_the_plugin_s_own_number_restated_by_a_summary_entry_is_whole()
        {
            var store = new ComposedStore(One);
            store.Add(Format(One, StoreFormat.Number));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void A_store_raised_above_the_plugin_is_outdated_by_each_carrier_of_the_number()
        {
            var above = StoreFormat.Number + 1;

            var record = new ComposedStore(One);
            record.Add(Format(One, above));
            Assert.That(record.Read().State, Is.EqualTo(StoreState.Outdated), "a version record");

            var item = new ComposedStore(One, above);
            Assert.That(item.Read().State, Is.EqualTo(StoreState.Outdated), "the item of a summary entry");

            var deletion = new ComposedStore(One);
            var gone = Format(One, above);
            deletion.Delete(gone);
            Assert.That(deletion.Read().State, Is.EqualTo(StoreState.Outdated), "a deletion record that tells of one");
        }

        [Test]
        public void A_deletion_record_with_the_tag_of_a_version_record_and_another_subject_tells_nothing()
        {
            var store = new ComposedStore(One);
            var identifier = StoreComposer.Tag(One, "format")
                .Concat(StoreComposer.BytesOf("0102030405"))
                .Concat(new byte[] { 0, 9 })
                .Concat(new byte[6])
                .ToArray();
            store.Deleted.Add(identifier);

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void A_record_an_item_or_an_object_of_a_kind_the_plugin_does_not_know_is_outdated()
        {
            var record = new ComposedStore(One);
            record.Add(StoreComposer.Record(One, "future", "x", StoreComposer.Content(("about", "x"))));
            Assert.That(record.Read().State, Is.EqualTo(StoreState.Outdated), "a record");

            var item = new ComposedStore(One);
            item.Restate(StoreComposer.Item(("about", "x"), ("kind", "future")));
            Assert.That(item.Read().State, Is.EqualTo(StoreState.Outdated), "an item");

            var objectType = new ComposedStore(One);
            objectType.Add(StoreComposer.Record(
                One, "version", "bin:" + new string('0', 32),
                StoreComposer.Content(("after", string.Empty), ("doc", "x"), ("object", "bin:" + new string('0', 32)))));
            Assert.That(objectType.Read().State, Is.EqualTo(StoreState.Outdated), "a version of an object of another type");
        }
    }
}
