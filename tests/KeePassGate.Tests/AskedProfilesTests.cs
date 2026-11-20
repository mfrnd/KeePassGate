using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Gate;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a pairing's approval dialog is built from, read off a single reading of the plugin's
    /// store: the names on the list, and the names off it a removal is known of, as the reading
    /// tells it or as the write of a stale approval found it; and what a reading that does not
    /// serve the authority asked under holds instead.
    /// </summary>
    [TestFixture]
    public sealed class AskedProfilesTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

        [Test]
        public void Removed_holds_each_name_off_the_list_whose_removal_the_reading_holds()
        {
            // `STORE.md` §616 and §623: a removal is known by its record, by an item of a summary
            // entry, or by a deletion record of a name the file knows otherwise, here by its
            // addition. A name removed and added back is on the list, and a name whose removal
            // only a deletion record tells of is known to the reading as no name at all.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "listed");
            StoreSeeds.Name(store, "gone");
            StoreSeeds.Unname(store, "gone");
            StoreSeeds.Name(store, "item");
            StoreSeeds.Unname(store, "item");
            using (var reading = store.Read())
            {
                store.Drop(reading.InForce.Records.Single(record => record.Is(StoreKinds.Removed)
                                                                      && record.Form.Member(StoreKinds.NameMember) == "item").Hex);
            }

            StoreSeeds.Name(store, "back");
            StoreSeeds.Unname(store, "back");
            StoreSeeds.Name(store, "back");
            StoreSeeds.Name(store, "known");
            StoreRecords.RemovedApart(store, "known");
            StoreRecords.RemovedApart(store, "apart");

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.RemovalsOf("back"), Has.Count.EqualTo(1), "a removal of a name back on the list");
                Assert.That(reading.InForce.RemovalsOf("apart"), Is.Empty, "a name the file does not know");

                var asked = AskedProfiles.Of(
                    reading, new[] { "listed", "gone", "item", "back", "known", "apart", "fresh" }, Now);

                Assert.That(asked.Listed, Is.EqualTo(new[] { "back", "listed" }));
                Assert.That(asked.Removed, Is.EqualTo(new[] { "gone", "item", "known" }));
            }
        }

        [Test]
        public void What_a_write_found_marks_a_name_removed_while_it_is_off_the_list_and_asked_for()
        {
            // `PAIRING.md` §311: the removals the write of a stale approval found in the database
            // are held by the dialog, and mark the name removed, only for a name it asks for off
            // the list. A name on the list is shown there whatever was found of it.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "listed");
            var found = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                { "apart", new[] { "r1" } },
                { "listed", new[] { "r2" } },
                { "unasked", new[] { "r3" } },
            };

            using (var reading = store.Read())
            {
                var asked = AskedProfiles.Of(reading, new[] { "apart", "listed", "fresh" }, Now).With(found);

                Assert.That(asked.Removed, Is.EqualTo(new[] { "apart" }));
                Assert.That(asked.Held("apart", "r1"), Is.True);
                Assert.That(asked.Held("listed", "r2"), Is.False);
                Assert.That(asked.Held("unasked", "r3"), Is.False);
            }
        }

        [Test]
        public void A_reading_that_does_not_serve_holds_its_word_and_nothing_to_show()
        {
            // A dialog built from it would mark names on the list as new, or as removed where a
            // client holds them: so it holds nothing but the word the write answers it with, and
            // no word where the authority asked under went.
            var asked = AskedProfiles.Unserved("split");

            Assert.That(asked.Served, Is.False);
            Assert.That(asked.Word, Is.EqualTo("split"));
            Assert.That(asked.Listed, Is.Empty);
            Assert.That(asked.Returning, Is.Empty);
            Assert.That(asked.Removed, Is.Empty);
            Assert.That(AskedProfiles.Unserved(null).Word, Is.Null);
        }

        [Test]
        public void A_reading_that_serves_holds_the_list_and_the_removals_it_tells_of()
        {
            // Seeded so that each of these can fail: a name on the list, and a name off it whose
            // removal the reading holds by its identifier.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Name(store, "gone");
            StoreSeeds.Unname(store, "gone");

            using (var reading = store.Read())
            {
                var removal = reading.InForce.RemovalsOf("gone").Single();
                var asked = AskedProfiles.Of(reading, new[] { "ops", "gone" }, Now);

                Assert.That(asked.Served, Is.True);
                Assert.That(asked.Word, Is.Null);
                Assert.That(asked.Listed, Is.EqualTo(new[] { "ops" }));
                Assert.That(asked.Held("gone", removal), Is.True);
                Assert.That(asked.Held("ops", removal), Is.False, "a name on the list was not asked about");
            }

            Assert.Throws<ArgumentException>(() => AskedProfiles.Of(null, new[] { "ops" }, Now));
        }
    }
}
