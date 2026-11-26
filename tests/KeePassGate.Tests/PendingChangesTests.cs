using System;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Store;
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
    /// **This reverses a decision.** Every button on that tab wrote at once, and that was wrong:
    /// a dialog with OK and Cancel on it tells that nothing has happened yet. What these settle is that
    /// Cancel means Cancel, that OK writes exactly what was waiting into the plugin's store, and
    /// that neither one disturbs what landed under the dialog.
    /// </remarks>
    [TestFixture]
    public sealed class PendingChangesTests
    {
        [Test]
        public void An_OK_holding_a_name_and_a_lift_writes_both_and_the_log_names_each()
        {
            // The names and the standing answers are both the plugin's store's: a single OK writes
            // them in a single write, and the log names an act about a name by the name and a lift
            // by its client, `STORE.md` §616 and §618, where the audit holds no line for either.
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            var client = new string('a', 64);
            StoreSeeds.Refuse(live.Store, client, RenewalRefusal.Forever());
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);

            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);
            Assert.That(Lift(pending, client), Is.Null);
            Assert.That(pending.ProfilesWaiting, Is.True);

            Assert.That(pending.Apply(), Is.True);

            Assert.That(NamesOf(live), Is.EqualTo(new[] { "ops" }));
            Assert.That(AnswerOf(live, client), Is.Null, "the lift landed");
            Assert.That(log.Entries.Any(entry => entry.Message.Contains(ProfileActs.AddName + " ops")), Is.True);
            Assert.That(log.Entries.Any(entry => entry.Message.Contains(RenewalActs.LiftName + " " + CertificateDigest.Shorten(client))), Is.True);
            Assert.That(pending.ProfilesWaiting, Is.False);
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
                store.Write(AdmissionRecord.For(leaving, DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));
                store.Write(AdmissionRecord.For(other, DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));

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
        public void A_standing_answer_that_lands_after_a_revocation_is_pressed_stays_at_OK()
        {
            // A renewal the operator refuses forever writes its answer live while the dialog is
            // open. Nothing is removed for a client that is revoked, `STORE.md` §618: the answer
            // stays in the store beside the revocation, refusing what the revocation refuses too.
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious", "ops"))
            {
                var store = new Admissions(live);
                store.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));
                var key = CertificateDigest.Of(client);

                var pending = new PendingDatabase(live, null);
                Assume.That(new Admissions(pending).Remove(key), Is.True);

                store.Refuse(key, RenewalRefusal.Forever());

                pending.Apply();

                Assert.That(store.Find(key), Is.Null, "the revocation landed");
                Assert.That(store.Answer(key).IsForever, Is.True, "and the answer stays");
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
                new Admissions(live).Write(AdmissionRecord.For(other, DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id));
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
        public void A_database_is_marked_when_something_lands_and_not_before()
        {
            var inner = Keyed();
            var pending = new PendingDatabase(inner, null);

            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);
            pending.MarkModified();

            Assert.That(inner.MarkedModifiedCount, Is.Zero, "marked for a change that has not landed");
            Assert.That(pending.Waiting, Is.True);

            Assert.That(pending.Apply(), Is.True);

            Assert.That(inner.MarkedModifiedCount, Is.EqualTo(1));
            Assert.That(NamesOf(inner), Is.EqualTo(new[] { "ops" }));
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
            var inner = Keyed();
            var pending = new PendingDatabase(inner, null);

            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);
            pending.MarkModified();

            pending.Discard();

            Assert.That(NamesOf(inner), Is.Empty);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
            Assert.That(pending.Waiting, Is.False);

            Assert.That(pending.Apply(), Is.False, "and there is nothing left to apply");
        }

        [Test]
        public void A_database_that_went_away_writes_nothing_rather_than_failing()
        {
            // A KeePass that locked or closed while the dialog was open. There is nothing to write
            // to and nothing to be done about it.
            var inner = Keyed();
            var pending = new PendingDatabase(inner, null);

            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);
            inner.IsOpen = false;

            Assert.That(pending.Apply(), Is.False);
            inner.IsOpen = true;
            Assert.That(NamesOf(inner), Is.Empty);
            Assert.That(inner.MarkedModifiedCount, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_standing_answer_lifted_on_the_tab_goes_at_OK_and_stays_on_Cancel(bool accepted)
        {
            // M001: Allow renewal waits for the dialog's OK, as a revocation does.
            var live = Keyed();
            var key = new string('a', 64);
            StoreSeeds.Refuse(live.Store, key, RenewalRefusal.Forever());

            var pending = new PendingDatabase(live, null);
            Assert.That(Lift(pending, key), Is.Null);
            Assert.That(AnswerOf(pending, key), Is.Null, "the page reads it as lifted at once");
            Assert.That(AnswerOf(live, key).IsForever, Is.True, "and the gate still refuses");

            if (accepted)
            {
                Assert.That(pending.Apply(), Is.True);
                Assert.That(AnswerOf(live, key), Is.Null);
            }
            else
            {
                pending.Discard();
                Assert.That(AnswerOf(live, key).IsForever, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void An_answer_given_under_the_dialog_that_the_press_was_not_shown_outlasts_the_lift_at_OK(bool passed)
        {
            // The until-date passed while the settings dialog was open, the next renewal raised its
            // dialog, and a copy merged in brought a refusal made apart, forever or until a moment
            // that has passed. The lift names what the page showed at the press and nothing else,
            // so the refusal stands beside it, `STORE.md` §627, and OK warns of it, whatever the
            // clock reads.
            var live = Keyed();
            var key = new string('b', 64);
            StoreSeeds.Refuse(live.Store, key, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(1)));

            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            Assume.That(Lift(pending, key), Is.Null);

            // Made apart, as a copy merged in brings it: it names nothing the press was shown.
            var given = passed ? RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(-1)) : RenewalRefusal.Forever();
            using (var reading = live.Store.Read())
            {
                string refused;
                var apart = RenewalActs.Refuse(reading, key, given, Array.Empty<string>(), out refused);
                Assume.That(live.Store.Write(new[] { apart }).Taken, Is.True, refused);
                apart.Forget();
            }

            Assert.That(AnswerOf(pending, key).IsForever, Is.EqualTo(!passed),
                "the page shows the answer given since while the dialog is still open");

            pending.Apply();

            Assert.That(AnswerOf(live, key), Is.Not.Null, "the answer made apart stands");
            Assert.That(log.Warnings.Select(entry => entry.Message),
                Has.Some.Contains("an answer the press was not shown still stands"));
        }

        [Test]
        public void A_lift_whose_answer_was_replaced_under_the_dialog_writes_nothing_and_the_log_names_the_press()
        {
            // The renewal dialog answered the client again, naming the very version the press was
            // shown: the lift has nothing left to name, comes to nothing, and the refusal stands.
            var live = Keyed();
            var key = new string('c', 64);
            StoreSeeds.Refuse(live.Store, key, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(1)));

            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            Assume.That(Lift(pending, key), Is.Null);

            StoreSeeds.Refuse(live.Store, key, RenewalRefusal.Forever());

            Assert.That(pending.Apply(), Is.False, "nothing was written");
            Assert.That(AnswerOf(live, key).IsForever, Is.True);
            Assert.That(log.Warnings.Select(entry => entry.Message),
                Has.Some.Contains("every answer to the client's renewals that the press was shown was replaced since"));
        }

        [Test]
        public void A_session_answer_lifted_on_the_tab_is_lifted_at_OK_and_kept_on_Cancel()
        {
            // An answer for this session is the plugin's memory and no record in the database, so
            // what holds the lift back until OK is a buffer of its own beside the store's.
            var one = new string('1', 64);
            var two = new string('2', 64);
            var session = new KeePassGate.Pairing.SessionRefusals();
            session.Refuse(one);

            var canceled = new PendingSessionRefusals(session);
            Assert.That(canceled.Lift(one), Is.True);
            Assert.That(canceled.Answer(one), Is.Null, "the page reads it as lifted at once");
            Assert.That(session.Refuses(one), Is.True, "and the gate still refuses");
            canceled.Discard();
            Assert.That(session.Refuses(one), Is.True, "Cancel leaves it standing");

            var accepted = new PendingSessionRefusals(session);
            Assert.That(accepted.Lift(two), Is.False, "nothing stands for that client");
            Assert.That(accepted.Lift(one), Is.True);
            Assert.That(accepted.Apply(), Is.EqualTo(new[] { one }));
            Assert.That(session.Refuses(one), Is.False, "OK lifts it");
        }

        /// <summary>A database whose store holds an authority, and so a key to write under.</summary>
        private static FakeGateDatabase Keyed()
        {
            var live = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            StoreSeeds.Authority(live.Store);
            return live;
        }

        /// <summary>
        /// Allow renewal pressed for a client, as the clients page presses it: a lift naming what
        /// the store read through the buffer shows, held for OK; answers why not, or null.
        /// </summary>
        private static string Lift(PendingDatabase pending, string client)
        {
            StoreAct act;
            using (var reading = pending.Store.Read())
            {
                var standing = StoredRenewals.Of(reading.InForce, client);
                act = RenewalActs.Lift(reading, client, standing.Standing, standing.Lost);
            }

            if (act == null)
            {
                return "nothing to lift";
            }

            var written = pending.Store.Write(new[] { act });
            return written.Taken ? null : written.Reason;
        }

        private static RenewalRefusal AnswerOf(IGateDatabase database, string client)
        {
            using (var reading = database.Store.Read())
            {
                return StoredRenewals.AnswerOf(reading.InForce, client);
            }
        }

        private static string[] NamesOf(FakeGateDatabase live)
        {
            using (var reading = live.Records.Read())
            {
                return ProfileNames.Of(reading).Names.ToArray();
            }
        }
    }
}
