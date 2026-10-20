using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the KPG tab holds until the dialog it is on is accepted.
    /// </summary>
    /// <remarks>
    /// **This reverses a decision.** Every button on that tab used to write at once, which was safe,
    /// because KeePass's database settings dialog keeps no copy of this plugin's custom data and
    /// therefore never undid any of it. It was still wrong: a dialog with OK and Cancel on it says
    /// nothing has happened yet. What these settle is that Cancel now means Cancel, that OK writes
    /// exactly what was waiting, and that neither one disturbs a key nobody touched.
    /// </remarks>
    [TestFixture]
    public sealed class PendingChangesTests
    {
        [Test]
        public void Nothing_written_reaches_the_store_until_it_is_applied()
        {
            var live = new InMemoryKeyValueStore();
            var pending = new PendingStore(live);

            pending.Set("KPG.Thing", "a value");

            Assert.That(live.Get("KPG.Thing"), Is.Null, "the store was told");
            Assert.That(pending.Get("KPG.Thing"), Is.EqualTo("a value"),
                "and the page cannot see what it just did");
            Assert.That(pending.Waiting, Is.True);

            Assert.That(pending.Apply(), Is.True);

            Assert.That(live.Get("KPG.Thing"), Is.EqualTo("a value"));
            Assert.That(pending.Waiting, Is.False);
        }

        [Test]
        public void What_is_discarded_never_happened()
        {
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Thing", "as it was");

            var pending = new PendingStore(live);
            pending.Set("KPG.Thing", "as it would have been");
            pending.Remove("KPG.Other");
            pending.Discard();

            Assert.That(live.Get("KPG.Thing"), Is.EqualTo("as it was"));
            Assert.That(pending.Get("KPG.Thing"), Is.EqualTo("as it was"));
            Assert.That(pending.Waiting, Is.False);
        }

        [Test]
        public void A_key_removed_here_is_gone_from_what_the_page_reads()
        {
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Client.aaaa", "a record");
            live.Set("KPG.Client.bbbb", "another");

            var pending = new PendingStore(live);
            pending.Remove("KPG.Client.aaaa");

            Assert.That(pending.Get("KPG.Client.aaaa"), Is.Null);
            Assert.That(pending.Keys, Is.EqualTo(new[] { "KPG.Client.bbbb" }));
            Assert.That(live.Get("KPG.Client.aaaa"), Is.Not.Null, "and still there until OK");

            pending.Apply();

            Assert.That(live.Get("KPG.Client.aaaa"), Is.Null);
        }

        [Test]
        public void Writing_a_value_that_is_already_there_changes_nothing()
        {
            // `StringDictionaryEx.Set` stamps a fresh modification time even when the text is
            // identical, which is how looking at a tab comes to leave a database wanting to be
            // saved. The comparison happens at the end, against whatever the store holds then.
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Thing", "a value");

            var pending = new PendingStore(live);
            pending.Set("KPG.Thing", "a value");

            Assert.That(pending.Apply(), Is.False);
        }

        [Test]
        public void Removing_a_key_that_is_not_there_changes_nothing()
        {
            var live = new InMemoryKeyValueStore();
            var pending = new PendingStore(live);

            pending.Remove("KPG.Thing");

            Assert.That(pending.Apply(), Is.False);
        }

        [Test]
        public void A_key_nobody_touched_is_left_exactly_as_it_is()
        {
            // The one that keeps a pairing alive. A record written to the live database while the
            // settings dialog was open belongs to a client that was admitted, and writing a whole
            // snapshot back over it would be this plugin doing to itself what KeePass's group
            // dialog does to it.
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Client.aaaa", "a record");

            var pending = new PendingStore(live);
            pending.Set("KPG.Profiles", "{\"profiles\":[\"ops\"]}");

            // A pairing lands while the operator is still reading the dialog.
            live.Set("KPG.Client.bbbb", "a record written meanwhile");

            pending.Apply();

            Assert.That(live.Get("KPG.Client.bbbb"), Is.EqualTo("a record written meanwhile"));
            Assert.That(live.Get("KPG.Client.aaaa"), Is.EqualTo("a record"));
            Assert.That(live.Get("KPG.Profiles"), Is.Not.Null);
        }

        [Test]
        public void Setting_a_key_after_removing_it_puts_it_back()
        {
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Thing", "as it was");

            var pending = new PendingStore(live);
            pending.Remove("KPG.Thing");
            pending.Set("KPG.Thing", "as it is now");
            pending.Apply();

            Assert.That(live.Get("KPG.Thing"), Is.EqualTo("as it is now"));
        }

        [Test]
        public void A_database_is_marked_when_something_lands_and_not_before()
        {
            var inner = new FakeGateDatabase();
            var pending = new PendingDatabase(inner, null);

            pending.PluginCustomData.Set("KPG.Thing", "a value");
            pending.MarkModified();

            Assert.That(inner.MarkedModifiedCount, Is.Zero, "marked for a change that has not landed");
            Assert.That(pending.Waiting, Is.True);

            Assert.That(pending.Apply(), Is.True);

            Assert.That(inner.MarkedModifiedCount, Is.EqualTo(1));
            Assert.That(inner.Store.Get("KPG.Thing"), Is.EqualTo("a value"));
        }

        [Test]
        public void A_database_is_not_marked_for_a_dialog_somebody_only_looked_at()
        {
            var inner = new FakeGateDatabase();
            var pending = new PendingDatabase(inner, null);

            Assert.That(pending.Apply(), Is.False);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
        }

        [Test]
        public void Canceling_leaves_the_database_untouched_and_unmarked()
        {
            var inner = new FakeGateDatabase();
            var pending = new PendingDatabase(inner, null);

            pending.PluginCustomData.Set("KPG.Thing", "a value");
            pending.MarkModified();

            pending.Discard();

            Assert.That(inner.Store.Get("KPG.Thing"), Is.Null);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
            Assert.That(pending.Waiting, Is.False);

            Assert.That(pending.Apply(), Is.False, "and there is nothing left to apply");
        }

        [Test]
        public void A_database_that_went_away_writes_nothing_rather_than_failing()
        {
            // A KeePass that locked or closed while the dialog was open. There is nothing to write
            // to and nothing to be done about it.
            var inner = new FakeGateDatabase();
            var pending = new PendingDatabase(inner, null);

            pending.PluginCustomData.Set("KPG.Thing", "a value");
            inner.IsOpen = false;

            Assert.That(pending.Apply(), Is.False);
            Assert.That(inner.Store.Get("KPG.Thing"), Is.Null);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
        }
    }
}
