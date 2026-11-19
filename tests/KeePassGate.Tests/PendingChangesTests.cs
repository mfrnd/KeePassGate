using System;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Testing;
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
        public void An_OK_holding_a_name_and_a_lifted_standing_answer_writes_both_and_the_log_names_the_name()
        {
            // The names are the plugin's store's and the standing answers the custom data's: a
            // single OK writes each where it is kept, and the log names an act about a name by the
            // name, `STORE.md` §616, where the audit holds no line for it.
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            live.Values.Set("KPG.RenewalRefused.aaa", "forever");
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);

            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);
            pending.PluginCustomData.Remove("KPG.RenewalRefused.aaa");
            Assert.That(pending.ProfilesWaiting, Is.True);

            Assert.That(pending.Apply(), Is.True);

            using (var reading = live.Records.Read())
            {
                Assert.That(ProfileNames.Of(reading).Names, Is.EqualTo(new[] { "ops" }));
            }

            Assert.That(live.Values.Get("KPG.RenewalRefused.aaa"), Is.Null);
            Assert.That(log.Entries.Any(entry => entry.Message.Contains(ProfileActs.AddName + " ops")), Is.True);
            Assert.That(pending.ProfilesWaiting, Is.False);
        }

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
            live.Set("KPG.Test.aaaa", "a record");
            live.Set("KPG.Test.bbbb", "another");

            var pending = new PendingStore(live);
            pending.Remove("KPG.Test.aaaa");

            Assert.That(pending.Get("KPG.Test.aaaa"), Is.Null);
            Assert.That(pending.Keys, Is.EqualTo(new[] { "KPG.Test.bbbb" }));
            Assert.That(live.Get("KPG.Test.aaaa"), Is.Not.Null, "and still there until OK");

            pending.Apply();

            Assert.That(live.Get("KPG.Test.aaaa"), Is.Null);
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
            live.Set("KPG.Test.aaaa", "a record");

            var pending = new PendingStore(live);
            pending.Set("KPG.Test.cccc", "an edit on the dialog");

            // A pairing lands while the operator is still reading the dialog.
            live.Set("KPG.Test.bbbb", "a record written meanwhile");

            pending.Apply();

            Assert.That(live.Get("KPG.Test.bbbb"), Is.EqualTo("a record written meanwhile"));
            Assert.That(live.Get("KPG.Test.aaaa"), Is.EqualTo("a record"));
            Assert.That(live.Get("KPG.Test.cccc"), Is.EqualTo("an edit on the dialog"));
        }

        [Test]
        public void What_OK_took_away_is_listed_and_a_cancel_lists_nothing()
        {
            // So that what a lift landed can be logged as it lands, rather than at the press.
            var live = new InMemoryKeyValueStore();
            live.Set("KPG.RenewalRefused.aaa", "forever");
            live.Set("KPG.RenewalRefused.bbb", "forever");
            var pending = new PendingDatabase(new FakeGateDatabase(live), null);

            pending.PluginCustomData.Remove("KPG.RenewalRefused.aaa");
            pending.PluginCustomData.Remove("KPG.Nothing.There");
            pending.PluginCustomData.Set("KPG.Written", "x");

            Assert.That(pending.Apply(), Is.True);

            Assert.That(pending.Taken, Is.EqualTo(new[] { "KPG.RenewalRefused.aaa" }), "a removal of nothing took nothing");

            pending.Discard();
            Assert.That(pending.Taken, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A_certificate_a_client_gave_up_while_the_dialog_was_open_stays_given_up_at_OK(bool revokedOnThePageToo)
        {
            // `DELETE /gate/certificate` lands on the live database at a moment nobody controls,
            // which may be while the operator has the settings dialog open and is revoking somebody
            // else on its Clients page. OK writes the acts the page held against the store as it
            // stands then, so the certificate given up stays given up. The second case is the
            // operator revoking the same client in the same minute: neither undoes the other.
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            using (var authority = TestAuthority.New())
            using (var leaving = authority.IssueClient("agent-fictitious", "ops"))
            using (var other = authority.IssueClient("agent-invented", "ops"))
            {
                var store = new Admissions(live);
                store.Write(AdmissionRecord.For(leaving, System.DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));
                store.Write(AdmissionRecord.For(other, System.DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));

                var pending = new PendingDatabase(live, null);
                var page = new Admissions(pending);
                Assume.That(page.Remove(CertificateDigest.Of(other)), Is.True);
                if (revokedOnThePageToo)
                {
                    Assume.That(page.Remove(CertificateDigest.Of(leaving)), Is.True);
                }

                // The client giving its certificate up, written live by the writer while the
                // dialog is open.
                var key = CertificateDigest.Of(leaving);
                StoreSeeds.TakeAway(live.Store, key);

                pending.Apply();

                Assert.That(store.Find(key), Is.Null, "so the certificate stays refused");
                Assert.That(store.Find(CertificateDigest.Of(other)), Is.Null,
                    "and what the operator did on the page landed as well");
            }
        }

        [Test]
        public void A_standing_answer_that_lands_after_the_press_goes_with_the_revocation_at_OK()
        {
            // A renewal the operator refuses forever writes its answer live while the dialog is
            // open. OK asks again over what it wrote, so the file is left with no answer about a
            // certificate no admission names, whichever came first, `STORE.md` §618.
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious", "ops"))
            {
                var store = new Admissions(live);
                store.Write(AdmissionRecord.For(client, System.DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));
                var key = CertificateDigest.Of(client);

                var pending = new PendingDatabase(live, null);
                Assume.That(new Admissions(pending).Remove(key), Is.True);

                store.Answers.RefuseRenewal(key, RenewalRefusal.Forever());
                Assume.That(live.Values.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Not.Null);

                pending.Apply();

                Assert.That(store.Find(key), Is.Null, "the revocation landed");
                Assert.That(live.Values.Get(AuthorityKeys.RenewalRefusalKey(key)), Is.Null, "and the answer went with it");
            }
        }

        [Test]
        public void What_OK_wrote_into_the_store_is_listed_as_landed_and_a_cancel_lists_nothing()
        {
            // The audit writes its lines for each revocation OK wrote, read off this list.
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            using (var authority = TestAuthority.New())
            using (var other = authority.IssueClient("agent-invented", "ops"))
            {
                new Admissions(live).Write(AdmissionRecord.For(other, System.DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));
                var client = CertificateDigest.Of(other);

                var dropped = new PendingDatabase(live, null);
                Assume.That(new Admissions(dropped).Remove(client), Is.True);
                dropped.Discard();
                Assert.That(dropped.Landed, Is.Empty);

                var pending = new PendingDatabase(live, null);
                Assume.That(new Admissions(pending).Remove(client), Is.True);
                Assert.That(pending.ActWaiting(AdmissionActs.RevokeName), Is.True);
                Assert.That(pending.Landed, Is.Empty, "nothing has landed before OK");

                pending.Apply();

                Assert.That(
                    pending.Landed.SelectMany(act => act.Records).Where(record => record.Kind == "revoked").Select(record => record.Member("client")),
                    Is.EqualTo(new[] { client }));
            }
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
            Assert.That(inner.Values.Get("KPG.Thing"), Is.EqualTo("a value"));
        }

        [Test]
        public void A_database_is_not_marked_for_a_dialog_somebody_only_looked_at()
        {
            // The decorator alone. On the real dialog's OK, KeePass's own handler marks the
            // database whatever the tab applied; what this pins is that the tab adds no mark of
            // its own, which is what a Cancel relies on.
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

            Assert.That(inner.Values.Get("KPG.Thing"), Is.Null);
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
            Assert.That(inner.Values.Get("KPG.Thing"), Is.Null);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_standing_answer_lifted_on_the_tab_goes_at_OK_and_stays_on_Cancel(bool accepted)
        {
            // M001: Allow renewal waits for the dialog's OK, as a revocation does.
            var live = new InMemoryKeyValueStore();
            var key = new string('a', 64);
            new RenewalAnswers(live, null).RefuseRenewal(key, RenewalRefusal.Forever());

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);
            Assert.That(new RenewalAnswers(pending.PluginCustomData, null).LiftRenewalRefusal(key), Is.True);
            Assert.That(new RenewalAnswers(pending.PluginCustomData, null).StandingRefusal(key), Is.Null,
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
            var store = new RenewalAnswers(live, null);
            store.RefuseRenewal(key, RenewalRefusal.Until(System.DateTimeOffset.UtcNow.AddDays(1)));

            var pending = new PendingDatabase(new FakeGateDatabase(live), null);
            Assume.That(new RenewalAnswers(pending.PluginCustomData, null).LiftRenewalRefusal(key), Is.True);

            store.RefuseRenewal(key, RenewalRefusal.Forever());

            Assert.That(new RenewalAnswers(pending.PluginCustomData, null).StandingRefusal(key).IsForever, Is.True,
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
            Assert.That(canceled.Holds("one"), Is.False, "the page reads it as lifted at once");
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
            session.Owe("db", "one", RenewalRefusal.Forever(), null);

            var pending = new PendingSessionRefusals(session);
            var now = DateTimeOffset.UtcNow;
            Assert.That(pending.Answer("one", now).IsForever, Is.True);

            Assert.That(pending.Lift("one"), Is.True);
            Assert.That(pending.Answer("one", now), Is.Null, "the page reads it as lifted at once");
            Assert.That(session.Answer("one", now), Is.Not.Null, "and the gate still refuses");

            pending.Apply();
            Assert.That(session.Answer("one", now), Is.Null, "OK lifts it");
            Assert.That(session.Owed(), Is.Empty, "and nothing is owed to the database any more");
        }

        [Test]
        public void An_owed_answer_lifted_on_the_page_is_held_back_from_writes_until_OK_or_Cancel()
        {
            var now = DateTimeOffset.UtcNow;
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Owe("db", "one", RenewalRefusal.Forever(), null);

            var canceled = new PendingSessionRefusals(session);
            Assert.That(canceled.Lift("one"), Is.True);
            Assert.That(session.Owed(), Is.Empty, "no write takes it while the lift waits for OK");
            Assert.That(session.Answer("one", now).IsForever, Is.True, "and the gate still refuses");
            canceled.Discard();
            Assert.That(session.Owed(), Has.Count.EqualTo(1), "Cancel gives it back to the next write");

            var accepted = new PendingSessionRefusals(session);
            Assert.That(accepted.Lift("one"), Is.True);
            session.Owe("db", "one", RenewalRefusal.Until(now.AddDays(3)), null);
            accepted.Apply();

            Assert.That(session.Owed(), Has.Count.EqualTo(1),
                "an answer given on a renewal dialog since the press is newer than the lift, and stands");
            Assert.That(session.Owed()[0].Refusal.IsForever, Is.False);
        }

        [Test]
        public void A_refusal_for_this_session_given_since_the_press_survives_OK()
        {
            // Allow renewal pressed on an owed answer until a date: once the day begins the gate
            // raises the renewal dialog again, and the operator refuses for this session there.
            // That answer is newer than the press, which never saw it, so OK leaves it standing
            // and the page shows it meanwhile.
            var now = DateTimeOffset.UtcNow;
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Owe("db", "one", RenewalRefusal.Until(now.AddDays(-1)), null);

            var pending = new PendingSessionRefusals(session);
            Assert.That(pending.Lift("one"), Is.True);

            session.Refuse("one");
            Assert.That(pending.Answer("one", now).IsThisSession, Is.True, "the page shows the newer answer");

            pending.Apply();

            Assert.That(session.Refuses("one"), Is.True, "OK lifts what was pressed and nothing given since");
            Assert.That(session.Owed(), Is.Empty, "while the owed answer the press lifted goes");
        }

        [Test]
        public void An_owed_answer_given_since_the_press_is_shown_and_lifted_by_a_second_press()
        {
            // The day of the owed answer the press lifted begins, the renewal dialog is raised
            // again, and the operator refuses forever while KeePass is busy.
            var now = DateTimeOffset.UtcNow;
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Owe("db", "one", RenewalRefusal.Until(now.AddDays(-1)), null);

            var pending = new PendingSessionRefusals(session);
            Assert.That(pending.Lift("one"), Is.True);
            Assert.That(pending.Answer("one", now), Is.Null);

            session.Owe("db", "one", RenewalRefusal.Forever(), null);
            Assert.That(pending.Answer("one", now).IsForever, Is.True, "the page shows the newer answer");
            Assert.That(session.Owed(), Has.Count.EqualTo(1), "and nothing holds it back from a write");

            Assert.That(pending.Lift("one"), Is.True, "so there is something to press again");
            Assert.That(session.Owed(), Is.Empty);
            pending.Apply();

            Assert.That(session.Answer("one", now), Is.Null, "both presses land at OK");
        }

        [Test]
        public void An_owed_answer_whose_day_has_begun_leaves_this_sessions_answer_showing()
        {
            // Answered whether or not it still refused, the owed answer stood in front of this
            // session's, and the page let the passed date go and showed the client refused by
            // nothing. It is still there to lift, since memory still holds it.
            var now = DateTimeOffset.UtcNow;
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Owe("db", "one", RenewalRefusal.Until(now.AddDays(-1)), null);
            session.Refuse("one");

            var pending = new PendingSessionRefusals(session);

            Assert.That(pending.Answer("one", now).IsThisSession, Is.True);
            Assert.That(pending.Lift("one"), Is.True);
        }
    }
}
