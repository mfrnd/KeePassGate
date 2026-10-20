using KeePassGate.Acl;
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
        public void A_kind_of_key_is_waiting_while_any_key_of_it_is()
        {
            // The authority is one key per authority, so whether it is waiting is a question about
            // every key of that kind, written or removed.
            var live = new InMemoryKeyValueStore();
            live.Plant(AuthorityKeys.AuthorityKey("old"), "a document");
            var pending = new PendingStore(live);

            Assert.That(pending.WaitingOn(AuthorityKeys.IsAuthorityState), Is.False);

            pending.Set("KPG.Thing", "a value");
            Assert.That(pending.WaitingOn(AuthorityKeys.IsAuthorityState), Is.False, "another kind entirely");

            pending.Remove(AuthorityKeys.AuthorityKey("old"));
            Assert.That(pending.WaitingOn(AuthorityKeys.IsAuthorityState), Is.True, "a removal is waiting too");

            pending.Discard();
            pending.Set(AuthorityKeys.RetirementKey("old"), AuthorityKeys.RetiredValue);
            Assert.That(pending.WaitingOn(AuthorityKeys.IsAuthorityState), Is.True, "and so is a retirement");
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
        public void A_profile_a_pairing_named_survives_an_edit_buffered_on_the_tab()
        {
            // The key the untouched-key rule cannot save, because this is the one key with two
            // writers. A pairing writes `KPG.Profiles` live and saves it, in the same write as the
            // admission record, so replaying the tab's older list over it left a client holding a
            // profile the tab could no longer list or count, and that granted it nothing.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, "{\"profiles\":[\"ops\"]}");

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);

            var edited = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Add("readonly"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            // A client is approved for a profile nobody had named, while the dialog is still open.
            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            Assert.That(pending.Apply(), Is.True);

            var after = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey)).Names;

            Assert.That(after, Is.EqualTo(new[] { "deploy", "ops", "readonly" }),
                "the name the pairing wrote is still there, and so is the one the operator typed");
        }

        [Test]
        public void A_profile_the_operator_removed_still_goes_when_a_pairing_writes_that_key()
        {
            // The other half of a difference being replayed rather than a snapshot: the merge must
            // not quietly put back what the operator took out, or removing a profile would depend
            // on whether anybody paired while the dialog was open.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, "{\"profiles\":[\"ops\",\"readonly\"]}");

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);

            var edited = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Remove("readonly"), Is.True);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            pending.Apply();

            Assert.That(ProfileNames.Read(live.Get(ProfileNames.CustomDataKey)).Names,
                Is.EqualTo(new[] { "deploy", "ops" }));
        }

        [Test]
        public void A_profile_a_pairing_named_meanwhile_is_on_the_page_while_the_edit_waits()
        {
            // H090: the page read the buffered list as it was typed, so a name a pairing added
            // while the edit waited was missing from the Profiles page while the Clients page, on
            // the same tab, listed the client holding it. The page now reads what OK will write.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, "{\"profiles\":[\"ops\"]}");

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);

            var edited = ProfileNames.Read(pending.PluginCustomData.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Add("readonly"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            Assert.That(ProfileNames.Read(pending.PluginCustomData.Get(ProfileNames.CustomDataKey)).Names,
                Is.EqualTo(new[] { "deploy", "ops", "readonly" }));
        }

        [Test]
        public void A_name_a_pairing_added_and_the_operator_then_took_out_goes_at_OK()
        {
            // The other half of reading the merge: the operator can now see the pairing's name and
            // take it out, and OK has to do what the page showed. Replayed against the first
            // edit's baseline, the name had never been there to take out, and stayed.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, "{\"profiles\":[\"ops\"]}");

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);

            var edited = ProfileNames.Read(pending.PluginCustomData.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Add("readonly"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            var shown = ProfileNames.Read(pending.PluginCustomData.Get(ProfileNames.CustomDataKey));
            Assume.That(shown.Remove("deploy"), Is.True);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, shown.Write());

            Assert.That(pending.Apply(), Is.True);
            Assert.That(ProfileNames.Read(live.Get(ProfileNames.CustomDataKey)).Names,
                Is.EqualTo(new[] { "ops", "readonly" }),
                "what the operator took out went, and what they added before it stayed");
        }

        [Test]
        public void Reading_the_merge_says_nothing_about_a_name_left_out_until_OK_leaves_it_out()
        {
            // A name the full list could not take is warned about when OK leaves it out. Read on
            // the page before that, it can still be kept, by taking another name out.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, Named(ProfileNames.Most - 1).Write());

            var log = new RecordingGateLog();
            var pending = new PendingDatabase(new FakeGateDatabase(live), log);

            var edited = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Add("ops"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            Assume.That(ProfileNames.Read(pending.PluginCustomData.Get(ProfileNames.CustomDataKey)).Holds("ops"),
                Is.False, "the page shows the list OK would write");
            Assert.That(log.Warnings, Is.Empty, "nothing has been left out yet");

            pending.Apply();

            Assert.That(log.Warnings, Has.Some.Property(nameof(RecordingGateLog.Entry.Message)).Contains("ops"));
        }

        [Test]
        public void A_name_the_full_list_cannot_take_is_left_out_and_said_to_be()
        {
            // The page stops the operator at the ceiling, and a pairing that names a profile of its
            // own while the dialog is open can reach it first. The names already there went into
            // the file with the records that needed them, so the name typed here is the one left
            // out, and the log says which rather than nothing saying anything.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, Named(ProfileNames.Most - 1).Write());

            var log = new RecordingGateLog();
            var pending = new PendingDatabase(new FakeGateDatabase(live), log);

            var edited = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Add("ops"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            pending.Apply();

            var after = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assert.That(after.Holds("deploy"), Is.True, "the approved client keeps what it was approved for");
            Assert.That(after.Holds("ops"), Is.False);
            Assert.That(log.Warnings, Has.Some.Property(nameof(RecordingGateLog.Entry.Message)).Contains("ops"),
                "and the name that was not kept is named");
        }

        [Test]
        public void A_name_left_out_is_said_at_OK_even_when_the_list_was_edited_again_after()
        {
            // The page shows what OK will write, which leaves the name out once a pairing has
            // filled the list; an edit after that is made against what was shown and moves the
            // baseline with it, so OK has nothing left to merge and used to say nothing.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, Named(ProfileNames.Most - 1).Write());

            var log = new RecordingGateLog();
            var pending = new PendingDatabase(new FakeGateDatabase(live), log);

            var edited = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Add("ops"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            var shown = ProfileNames.Read(pending.PluginCustomData.Get(ProfileNames.CustomDataKey));
            Assume.That(shown.Holds("ops"), Is.False, "the page's view leaves it out");
            Assume.That(shown.Remove("profile0"), Is.True);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, shown.Write());

            pending.Apply();

            Assert.That(ProfileNames.Read(live.Get(ProfileNames.CustomDataKey)).Holds("ops"), Is.False);
            Assert.That(log.Warnings, Has.Some.Property(nameof(RecordingGateLog.Entry.Message)).Contains("ops"),
                "the name that was not kept is named at OK all the same");
        }

        [Test]
        public void A_name_taken_out_here_makes_room_for_one_put_in_here()
        {
            // At the ceiling the order matters: an addition replayed before the removal would be
            // refused for want of the room that the operator's own removal makes.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, Named(ProfileNames.Most - 1).Write());

            var log = new RecordingGateLog();
            var pending = new PendingDatabase(new FakeGateDatabase(live), log);

            var edited = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(edited.Remove("profile0"), Is.True);
            Assume.That(edited.Add("ops"), Is.Null);
            pending.PluginCustomData.Set(ProfileNames.CustomDataKey, edited.Write());

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            pending.Apply();

            var after = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assert.That(after.Holds("ops"), Is.True);
            Assert.That(after.Holds("deploy"), Is.True);
            Assert.That(after.Holds("profile0"), Is.False);
            Assert.That(log.Warnings, Is.Empty);
        }

        private static ProfileNames Named(int count)
        {
            var names = ProfileNames.Empty();
            for (var i = 0; i < count; i++)
            {
                Assume.That(names.Add("profile" + i), Is.Null);
            }

            return names;
        }

        [Test]
        public void Taking_the_last_profile_out_does_not_take_a_pairings_name_with_it()
        {
            // Emptying the list removes the key rather than storing an empty document, so this is
            // the path where a removal is what waits for OK. It is reconciled like any other edit.
            var live = new InMemoryKeyValueStore();
            live.Set(ProfileNames.CustomDataKey, "{\"profiles\":[\"ops\"]}");

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);
            pending.PluginCustomData.Remove(ProfileNames.CustomDataKey);

            var approved = ProfileNames.Read(live.Get(ProfileNames.CustomDataKey));
            Assume.That(approved.Add("deploy"), Is.Null);
            live.Set(ProfileNames.CustomDataKey, approved.Write());

            pending.Apply();

            Assert.That(ProfileNames.Read(live.Get(ProfileNames.CustomDataKey)).Names,
                Is.EqualTo(new[] { "deploy" }),
                "the operator removed `ops`, and nobody asked for the approved name to go");
        }

        [Test]
        public void A_key_with_no_second_writer_is_replayed_rather_than_merged()
        {
            // Reconciling is per key, because every other key on this tab has one writer and a
            // merge there could only ever invent a conflict.
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Thing", "as it was");

            var pending = new PendingStore(live);
            pending.Set("KPG.Thing", "as the operator left it");

            live.Set("KPG.Thing", "as somebody else left it");

            pending.Apply();

            Assert.That(live.Get("KPG.Thing"), Is.EqualTo("as the operator left it"));
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
            var inner = new FakeGateDatabase { Nodes = new TestRuledNodes() };
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
            var inner = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var pending = new PendingDatabase(inner, null);

            Assert.That(pending.Apply(), Is.False);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
        }

        [Test]
        public void Canceling_leaves_the_database_untouched_and_unmarked()
        {
            var inner = new FakeGateDatabase { Nodes = new TestRuledNodes() };
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
            var inner = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var pending = new PendingDatabase(inner, null);

            pending.PluginCustomData.Set("KPG.Thing", "a value");
            inner.IsOpen = false;

            Assert.That(pending.Apply(), Is.False);
            Assert.That(inner.Store.Get("KPG.Thing"), Is.Null);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
        }
    }
}
