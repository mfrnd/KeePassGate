using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// An answer to a renewal that the plugin's memory holds, carried to the KPG tab from the
    /// connection thread that gave it.
    /// </summary>
    /// <remarks>
    /// S028: a standing answer the database keeps is told to the clients page by the writer, which
    /// draws the client's name red from it. An answer for this session is never written, and an
    /// answer owed is written only later, so a renewal refused that way while the settings dialog
    /// showed the page left the name black. Driven against a real window on a real message queue,
    /// with the answer given from another thread, because that is where the renewal handler gives
    /// it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SessionAnswerNoticeTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        private static readonly string Key = new string('a', 64);

        [Test]
        public void An_answer_for_this_session_is_told_on_the_window_s_own_thread()
        {
            using (var form = new Form())
            {
                Handle(form);
                var refusals = new SessionRefusals();
                var told = 0;
                var on = -1;

                using (new SessionAnswerNotice(refusals, form, () =>
                {
                    told++;
                    on = Environment.CurrentManagedThreadId;
                }, null, null))
                {
                    FromAnotherThread(() => refusals.Refuse(Key));
                    var escapes = Escapes.PumpUntil(() => told > 0, Patience);

                    Assert.That(told, Is.EqualTo(1));
                    Assert.That(on, Is.EqualTo(Environment.CurrentManagedThreadId),
                        "told on the window's thread, where every page listening draws");
                    Assert.That(escapes.Unhandled, Is.Null);
                    Assert.That(escapes.Loose, Is.Null);
                }
            }
        }

        [Test]
        public void An_answer_the_database_could_not_take_is_told_as_well()
        {
            using (var form = new Form())
            {
                Handle(form);
                var refusals = new SessionRefusals();
                var told = 0;

                using (new SessionAnswerNotice(refusals, form, () => told++, null, null))
                {
                    FromAnotherThread(() => refusals.Owe("database", Key, RenewalRefusal.Forever(), null));
                    Escapes.PumpUntil(() => told > 0, Patience);

                    Assert.That(told, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void An_answer_already_held_is_not_told_again_and_nothing_is_told_once_let_go()
        {
            using (var form = new Form())
            {
                Handle(form);
                var refusals = new SessionRefusals();
                var told = 0;

                using (new SessionAnswerNotice(refusals, form, () => told++, null, null))
                {
                    refusals.Refuse(Key);
                    refusals.Refuse(Key);
                    Escapes.PumpUntil(() => false, TimeSpan.FromMilliseconds(200));

                    Assert.That(told, Is.EqualTo(1), "the second answer changed nothing on the page");
                }

                refusals.Refuse(new string('b', 64));
                Escapes.PumpUntil(() => false, TimeSpan.FromMilliseconds(200));

                Assert.That(told, Is.EqualTo(1), "a notice let go of went on telling");
            }
        }

        [Test]
        public void A_window_that_has_gone_leaves_the_answer_standing()
        {
            // KeePass's exit leaves a connection thread inside a renewal: the post is refused on
            // that thread, and the answer is held all the same.
            var refusals = new SessionRefusals();
            var log = new RecordingGateLog();

            Form gone;
            using (var form = new Form())
            {
                Handle(form);
                gone = form;
            }

            using (new SessionAnswerNotice(refusals, gone, () => { }, null, log))
            {
                Assert.DoesNotThrow(() => FromAnotherThread(() => refusals.Refuse(Key)));
            }

            Assert.That(refusals.Refuses(Key), Is.True);
            Assert.That(log.Entries, Has.Some.Matches<RecordingGateLog.Entry>(
                entry => entry.Message.Contains("was not told of an answer to a renewal held in memory")));
        }

        [Test]
        public void A_renewal_refused_in_this_session_draws_its_client_red_on_an_open_tab()
        {
            // The whole of it: the renewal handler's answer on a connection thread, the plugin's
            // notice on KeePass's, and the clients page drawing the name as it draws a standing
            // answer the database keeps.
            var database = new FakeGateDatabase();
            var refusals = new SessionRefusals();
            var notice = new AdmissionNotice();

            using (var form = new Form())
            using (var panel = new AuthorityPanel(
                database, new FakePrompt(true), null, null, null, notice, new PendingSessionRefusals(refusals)))
            using (new SessionAnswerNotice(refusals, form, notice.Raise, null, null))
            {
                Handle(form);
                panel.CreateAuthority();

                AdmissionRecord record;
                using (var certificate = Minted.Named("CN=agent-fictitious")
                           .Lasting(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30))
                           .SelfSigned()
                           .Read())
                {
                    record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                    new AdmissionStore(database.Store, null).Write(record);
                }

                panel.Describe();
                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                Assume.That(list.Clients.Count, Is.EqualTo(1));
                Assume.That(list.StandingAt(0), Is.Null, "nothing refuses its renewals yet");

                FromAnotherThread(() => refusals.Refuse(record.AdmissionKey));
                Escapes.PumpUntil(() => list.StandingAt(0) != null, Patience);

                Assert.That(list.StandingAt(0), Is.Not.Null, "the page was not told of the answer");
                Assert.That(list.StandingAt(0).IsThisSession, Is.True);
            }
        }

        /// <summary>
        /// Runs work on a thread of its own, as a connection's, and throws here whatever it threw.
        /// </summary>
        private static void FromAnotherThread(Action work)
        {
            Exception thrown = null;
            var thread = new Thread(() => thrown = Thrown.By(work));
            thread.Start();
            Assert.That(thread.Join(Patience), Is.True, "the other thread did not finish");

            if (thrown != null)
            {
                throw new AssertionException("the other thread threw: " + thrown);
            }
        }

        /// <summary>Forces the window into existence, which is what a post needs.</summary>
        private static void Handle(Control control)
        {
            control.CreateControl();
            Assume.That(control.Handle, Is.Not.EqualTo(IntPtr.Zero));
        }
    }
}
