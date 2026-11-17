using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
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

        [Test]
        public void A_keep_record_naming_its_own_key_leaves_that_key_standing()
        {
            var store = new ComposedStore(One);
            var self = store.Kept(One, One);
            store.Restate(KeptItem(self, One));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole));
        }
    }
}
