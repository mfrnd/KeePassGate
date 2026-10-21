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

        [TestCase(false)]
        [TestCase(true)]
        public void A_revocation_a_client_made_of_itself_survives_the_dialogs_OK(bool revokedOnThePageToo)
        {
            // `DELETE /gate/certificate` lands on the live database at a moment nobody controls,
            // which may be while the operator has the settings dialog open and is revoking somebody
            // else on its Clients page. OK replays only the keys the operator changed, so the
            // client's own revocation and its emptied record stay as the writer left them. The
            // second case is the operator pressing X on the same row in the same minute: the two
            // write the same values, and neither undoes the other.
            using (var authority = TestAuthority.New())
            using (var leaving = authority.IssueClient("agent-fictitious", "ops"))
            using (var other = authority.IssueClient("agent-invented", "ops"))
            {
                var live = new InMemoryKeyValueStore();
                var store = new AdmissionStore(live, null);
                store.Write(AdmissionRecord.For(leaving, System.DateTimeOffset.UtcNow, new[] { "ops" }));
                store.Write(AdmissionRecord.For(other, System.DateTimeOffset.UtcNow, new[] { "ops" }));

                var pending = new PendingDatabase(new FakeGateDatabase(live), null);
                var page = new AdmissionStore(pending.PluginCustomData, null);
                Assume.That(page.Remove(CertificateDigest.Of(other)), Is.True);
                if (revokedOnThePageToo)
                {
                    Assume.That(page.Remove(CertificateDigest.Of(leaving)), Is.True);
                }

                // The client's own revocation, written live by the writer while the dialog is open.
                var key = CertificateDigest.Of(leaving);
                Assume.That(store.Remove(key), Is.True);

                pending.Apply();

                Assert.That(live.Get(AuthorityKeys.ClientRecordKey(key)), Is.Empty, "the record stays emptied");
                Assert.That(live.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null.And.Not.Empty,
                    "and the revocation beside it stays");
                Assert.That(store.Find(key), Is.Null, "so the client stays refused");
                Assert.That(store.Find(CertificateDigest.Of(other)), Is.Null,
                    "and what the operator did on the page landed as well");
            }
        }

        [Test]
        public void A_standing_answer_that_lands_after_the_press_goes_with_the_revocation_at_OK()
        {
            // The press prunes the standing answers it can see, and a renewal the operator refuses
            // forever after it writes one live. OK asks again over what it wrote, so the file is
            // left with no answer about a certificate no record admits, whichever came first.
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious", "ops"))
            {
                var live = new InMemoryKeyValueStore();
                var store = new AdmissionStore(live, null);
                store.Write(AdmissionRecord.For(client, System.DateTimeOffset.UtcNow, new[] { "ops" }));
                var key = CertificateDigest.Of(client);

                var pending = new PendingDatabase(new FakeGateDatabase(live), null);
                Assume.That(new AdmissionStore(pending.PluginCustomData, null).Remove(key), Is.True);

                store.RefuseRenewal(key, RenewalRefusal.Forever());
                Assume.That(live.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Not.Null);

                pending.Apply();

                Assert.That(live.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null, "the revocation landed");
                Assert.That(live.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Null, "and the answer went with it");
            }
        }

        [Test]
        public void What_OK_stored_is_listed_and_a_revocation_already_there_is_not()
        {
            // The audit writes one line for each revocation OK puts in, read off this list. The
            // client that revoked itself while the dialog was open already has its line, and the
            // operator pressing X on its row too must not give it a second.
            using (var authority = TestAuthority.New())
            using (var leaving = authority.IssueClient("agent-fictitious", "ops"))
            using (var other = authority.IssueClient("agent-invented", "ops"))
            {
                var live = new InMemoryKeyValueStore();
                var store = new AdmissionStore(live, null);
                store.Write(AdmissionRecord.For(leaving, System.DateTimeOffset.UtcNow, new[] { "ops" }));
                store.Write(AdmissionRecord.For(other, System.DateTimeOffset.UtcNow, new[] { "ops" }));

                var pending = new PendingDatabase(new FakeGateDatabase(live), null);
                var page = new AdmissionStore(pending.PluginCustomData, null);
                Assume.That(page.Remove(CertificateDigest.Of(other)), Is.True);
                Assume.That(page.Remove(CertificateDigest.Of(leaving)), Is.True);
                Assume.That(store.Remove(CertificateDigest.Of(leaving)), Is.True);

                Assert.That(pending.Stored, Is.Empty, "nothing is stored before OK");

                pending.Apply();

                Assert.That(pending.Stored, Has.Member(AuthorityKeys.RevocationKey(CertificateDigest.Of(other))));
                Assert.That(pending.Stored, Has.No.Member(AuthorityKeys.RevocationKey(CertificateDigest.Of(leaving))),
                    "it was there already, written by the client's own revocation");
            }
        }

        [Test]
        public void A_removal_is_not_listed_as_stored_and_a_cancel_lists_nothing()
        {
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.Old", "going");
            var pending = new PendingStore(live);

            pending.Set("KPG.Thing", "a value");
            pending.Remove("KPG.Old");
            pending.Apply();

            Assert.That(pending.Stored, Is.EqualTo(new[] { "KPG.Thing" }));

            pending.Set("KPG.Other", "never kept");
            pending.Discard();

            Assert.That(pending.Stored, Is.Empty);
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

        [TestCase(true)]
        [TestCase(false)]
        public void A_standing_answer_lifted_on_the_tab_goes_at_OK_and_stays_on_Cancel(bool accepted)
        {
            // M001: Allow renewal waits for the dialog's OK, as a revocation does.
            var live = new InMemoryKeyValueStore();
            var key = new string('a', 64);
            new AdmissionStore(live, null).RefuseRenewal(key, RenewalRefusal.Forever());

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);
            Assert.That(new AdmissionStore(pending.PluginCustomData, null).LiftRenewalRefusal(key), Is.True);
            Assert.That(new AdmissionStore(pending.PluginCustomData, null).StandingRefusal(key), Is.Null,
                "the page reads it as gone at once");
            Assert.That(live.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Not.Null, "and the gate still refuses");

            if (accepted)
            {
                Assert.That(pending.Apply(), Is.True);
                Assert.That(live.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Null);
            }
            else
            {
                pending.Discard();
                Assert.That(live.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.EqualTo(RenewalRefusal.ForeverValue));
            }
        }

        [Test]
        public void An_answer_given_again_while_the_dialog_is_open_outlasts_the_lift_at_OK()
        {
            // The until-date passed while the settings dialog was open, the next renewal raised its
            // dialog, and the operator refused it forever there. That is the later word on the
            // certificate, and replaying the tab's removal over it would throw it away.
            var live = new InMemoryKeyValueStore();
            var key = new string('b', 64);
            var store = new AdmissionStore(live, null);
            store.RefuseRenewal(key, RenewalRefusal.Until(System.DateTimeOffset.UtcNow.AddDays(1)));

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);
            Assume.That(new AdmissionStore(pending.PluginCustomData, null).LiftRenewalRefusal(key), Is.True);

            store.RefuseRenewal(key, RenewalRefusal.Forever());

            Assert.That(new AdmissionStore(pending.PluginCustomData, null).StandingRefusal(key).IsForever, Is.True,
                "the page shows the newer answer while the dialog is still open");

            pending.Apply();

            Assert.That(live.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.EqualTo(RenewalRefusal.ForeverValue));
        }

        [Test]
        public void A_session_answer_lifted_on_the_tab_is_lifted_at_OK_and_kept_on_Cancel()
        {
            // An answer for this session is the plugin's memory and no key in the database, so
            // what holds the lift back until OK is a buffer of its own beside the database's.
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Refuse("one");

            var canceled = new PendingSessionRefusals(session);
            Assert.That(canceled.Lift("one"), Is.True);
            Assert.That(canceled.Refuses("one"), Is.False, "the page reads it as lifted at once");
            Assert.That(session.Refuses("one"), Is.True, "and the gate still refuses");
            canceled.Discard();
            Assert.That(session.Refuses("one"), Is.True, "Cancel leaves it standing");

            var accepted = new PendingSessionRefusals(session);
            Assert.That(accepted.Lift("two"), Is.False, "nothing stands for that certificate");
            Assert.That(accepted.Lift("one"), Is.True);
            Assert.That(accepted.Apply(), Is.EqualTo(new[] { "one" }));
            Assert.That(session.Refuses("one"), Is.False, "OK lifts it");
        }

        [Test]
        public void An_answer_owed_to_the_database_is_shown_and_lifted_as_the_session_one_is()
        {
            // Refused forever while KeePass was busy: the answer holds from memory until the
            // plugin's next write takes it into the database, and the page shows and lifts it as
            // it does one for this session, since the gate answers from the same memory.
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Owe("db", "one", RenewalRefusal.Forever());

            var pending = new PendingSessionRefusals(session);
            Assert.That(pending.Answer("one").IsForever, Is.True);

            Assert.That(pending.Lift("one"), Is.True);
            Assert.That(pending.Answer("one"), Is.Null, "the page reads it as lifted at once");
            Assert.That(session.Answer("one"), Is.Not.Null, "and the gate still refuses");

            pending.Apply();
            Assert.That(session.Answer("one"), Is.Null, "OK lifts it");
            Assert.That(session.Owed(), Is.Empty, "and nothing is owed to the database any more");
        }
    }
}
