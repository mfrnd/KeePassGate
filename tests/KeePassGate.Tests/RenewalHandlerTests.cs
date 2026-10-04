using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// `renewCertificate`: an admitted client asking for a new certificate, through the router this
    /// plugin runs, with the operator played by a test.
    /// </summary>
    [TestFixture]
    public sealed class RenewalHandlerTests
    {
        private const string Peer = "127.0.0.1:60123";

        private TestAuthority _authority;
        private RecordingGateLog _log;
        private DateTimeOffset _now;
        private SessionRefusals _session;
        private ApprovalBook _held;
        private PairingOptions _options;

        [SetUp]
        public void SetUp()
        {
            _options = new PairingOptions();
            _authority = TestAuthority.New();
            _log = new RecordingGateLog();
            _now = DateTimeOffset.UtcNow;
            _session = new SessionRefusals();
            _held = new ApprovalBook(() => _now);
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Dispose();
        }

        [Test]
        public void An_approved_renewal_answers_with_a_new_identity_and_leaves_the_old_one_admitted()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(200), context.Json);

                var body = JsonBody.Read(context.Json);
                Assert.That(body, Is.Not.Null, context.Json);
                Assert.That(body.Text("subject"), Is.EqualTo("CN=agent-fictitious"), "the same name");

                using (var renewed = Identity(context.Json))
                {
                    Assert.That(CertificateDigest.Of(renewed), Is.Not.EqualTo(CertificateDigest.Of(old)),
                        "a new certificate, with a key of its own");
                    Assert.That(_authority.Admission(CertificateDigest.Of(renewed)), Is.Not.Null,
                        "admitted before the answer was sent");
                    Assert.That(_authority.Admission(CertificateDigest.Of(renewed)).Profiles, Is.EqualTo(new[] { "reader" }));
                }

                Assert.That(_authority.Admission(CertificateDigest.Of(old)), Is.Not.Null,
                    "the old record is left as it is: the client revokes it itself");
            }
        }

        [Test]
        public void A_renewed_certificate_is_the_same_client_however_often_it_renews()
        {
            // The client key is the certificate the client paired with, copied from the record that
            // asked, so a renewal of a renewal is still the client that paired.
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var paired = CertificateDigest.Of(old);
                var first = Renew(operatorAt, old, "{}");
                Assert.That(first.Status, Is.EqualTo(200), first.Json);

                using (var renewed = Identity(first.Json))
                {
                    Assert.That(_authority.Admission(CertificateDigest.Of(renewed)).ClientKey, Is.EqualTo(paired));

                    var second = Renew(operatorAt, renewed, "{}");
                    Assert.That(second.Status, Is.EqualTo(200), second.Json);

                    using (var again = Identity(second.Json))
                    {
                        Assert.That(_authority.Admission(CertificateDigest.Of(again)).ClientKey, Is.EqualTo(paired));
                    }
                }
            }
        }

        [Test]
        public void The_operator_is_shown_what_the_record_holds_rather_than_anything_the_client_said()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader", "ops", "retired"))
            {
                _authority.KnownProfiles = new[] { "reader", "ops" };

                Renew(operatorAt, old, "{\"lifetime\":\"P30D\"}");

                var shown = operatorAt.LastRequest;
                Assert.That(shown.Name, Is.EqualTo("agent-fictitious"));
                Assert.That(shown.Profiles, Is.EqualTo(new[] { "reader", "ops" }));
                Assert.That(shown.Dropped, Is.EqualTo(new[] { "retired" }),
                    "a name the database no longer holds is not offered, and the dialog says so");
                Assert.That(shown.Expires, Is.EqualTo(new DateTimeOffset(old.NotAfter.ToUniversalTime(), TimeSpan.Zero)));
                Assert.That(shown.Lifetime, Is.EqualTo(TimeSpan.FromDays(30)));
                Assert.That(shown.Peer, Is.EqualTo(Peer));
                Assert.That(shown.DatabaseId, Is.EqualTo(_authority.DatabaseId));
            }
        }

        [Test]
        public void The_new_certificate_is_never_wider_than_what_was_offered()
        {
            // Whatever the screen answered: a name the record did not hold, or one the database
            // no longer names, does not reach the new certificate.
            var operatorAt = new FakeRenewalApproval(
                request => RenewalApproval.Approved(new[] { "reader", "admin", "retired", "reader" }));

            using (var old = _authority.IssueAdmittedClient("reader", "retired"))
            {
                _authority.KnownProfiles = new[] { "reader", "admin" };

                var context = Renew(operatorAt, old, "{}");

                using (var renewed = Identity(context.Json))
                {
                    Assert.That(_authority.Admission(CertificateDigest.Of(renewed)).Profiles, Is.EqualTo(new[] { "reader" }));
                }
            }
        }

        [Test]
        public void A_profile_taken_off_the_list_while_the_operator_decided_is_not_carried_over_or_put_back()
        {
            // The write adds every name a record holds that the list does not, so carrying it
            // over would have put the name back on the list, and with it every rule naming it and
            // every client holding it.
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader", "ops"))
            {
                operatorAt.Meanwhile = () => _authority.Forget("ops");

                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                using (var renewed = Identity(context.Json))
                {
                    Assert.That(_authority.Admission(CertificateDigest.Of(renewed)).Profiles, Is.EqualTo(new[] { "reader" }));
                }

                Assert.That(_authority.KnownProfiles, Does.Not.Contain("ops"), "the name stays off the list");
                Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("stopped naming"));
            }
        }

        [Test]
        public void The_lifetime_the_operator_picked_is_the_one_issued()
        {
            var operatorAt = new FakeRenewalApproval(
                request => RenewalApproval.Approved(request.Profiles, TimeSpan.FromDays(7)));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{\"lifetime\":\"P90D\"}");

                using (var renewed = Identity(context.Json))
                {
                    // In UTC, because `NotAfter` and `NotBefore` are local, and a week that takes in
                    // a change of the clocks is an hour longer or shorter on the wall.
                    Assert.That(renewed.NotAfter.ToUniversalTime() - renewed.NotBefore.ToUniversalTime(),
                        Is.EqualTo(TimeSpan.FromDays(7) + AuthorityIssuer.Backdate).Within(TimeSpan.FromSeconds(5)));
                }
            }
        }

        [Test]
        public void A_refusal_once_stores_nothing_and_the_next_renewal_asks_again()
        {
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused());

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(403));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("refused")));
                Assert.That(_authority.StandingRefusal(CertificateDigest.Of(old)), Is.Null);

                Renew(operatorAt, old, "{}");
                Assert.That(operatorAt.Asked, Is.EqualTo(2));
            }
        }

        [Test]
        public void A_refusal_until_a_day_is_answered_without_asking_until_that_day()
        {
            var day = _now.AddDays(3);
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.Until(day)));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assert.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));

                var again = Renew(operatorAt, old, "{}");

                Assert.That(again.Status, Is.EqualTo(409));
                Assert.That(again.Json, Is.EqualTo(
                    "{\"error\":\"too early\",\"notBefore\":\"" + Moments.Wire(RenewalRefusal.Until(day).NotBefore.Value) + "\"}"));
                Assert.That(operatorAt.Asked, Is.EqualTo(1), "nobody was disturbed the second time");

                _now = day;
                Renew(operatorAt, old, "{}");
                Assert.That(operatorAt.Asked, Is.EqualTo(2), "from that moment the operator is asked again");
            }
        }

        [Test]
        public void A_refusal_forever_is_answered_without_asking_from_then_on()
        {
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.Forever()));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));

                _now = _now.AddDays(300);
                var again = Renew(operatorAt, old, "{}");

                Assert.That(again.Status, Is.EqualTo(409));
                Assert.That(again.Json, Is.EqualTo(HttpResponse.ErrorBody("forever")));
                Assert.That(operatorAt.Asked, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_refusal_in_this_session_is_answered_without_asking_and_writes_nothing()
        {
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.ThisSession()));

            using (var old = _authority.IssueAdmittedClient("reader"))
            using (var other = _authority.IssueAdmittedClient("reader"))
            {
                Assert.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));
                Assert.That(_authority.StandingRefusal(CertificateDigest.Of(old)), Is.Null,
                    "held in the plugin's memory, never in the database");

                _now = _now.AddDays(300);
                var again = Renew(operatorAt, old, "{}");

                Assert.That(again.Status, Is.EqualTo(409));
                Assert.That(again.Json, Is.EqualTo(HttpResponse.ErrorBody("this session")));
                Assert.That(operatorAt.Asked, Is.EqualTo(1), "nobody was disturbed the second time");

                Renew(operatorAt, other, "{}");
                Assert.That(operatorAt.Asked, Is.EqualTo(2), "an answer about one certificate and no other");
            }
        }

        [Test]
        public void A_refusal_in_this_session_ends_with_the_session()
        {
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.ThisSession()));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));

                // KeePass started again: a new plugin, and with it a session that has refused nothing.
                _session = new SessionRefusals();
                Renew(operatorAt, old, "{}");

                Assert.That(operatorAt.Asked, Is.EqualTo(2));
            }
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        public void A_session_answer_lifted_on_the_clients_page_lets_the_next_renewal_ask_once_OK_is_pressed()
        {
            // M001: one set of the session's answers for the plugin. The handler refused in it,
            // the clients page shows the same answer and lifts it, and the page's OK is what lets
            // this handler raise the dialog again: a set of the tab's own would show nothing and
            // lift nothing the gate reads.
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.ThisSession()));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));

                var page = new FakeGateDatabase();
                new AdmissionStore(page.PluginCustomData, _log)
                    .Write(AdmissionRecord.For(old, _now, new[] { "reader" }));
                var factory = new KeePassGate.Ui.AuthorityPanelFactory(
                    new FakeDatabaseSource(page), new FakePrompt(true), _log, null, null, null, _session);

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = factory.CreatePanel(form))
                {
                    var list = KeePassGate.Ui.ControlTree.FindByName<KeePassGate.Ui.ClientList>(
                        panel, KeePassGate.Ui.ClientList.ControlName);
                    Assert.That(list.StandingAt(0).IsThisSession, Is.True, "the page shows the gate's answer");

                    Assert.That(list.AllowRenewal(0), Does.Contain("raises the dialog again"));
                    Assert.That(list.StandingAt(0), Is.Null, "the name stops being red at once");

                    Assert.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(409),
                        "the gate goes on refusing until the dialog is accepted");
                    Assert.That(operatorAt.Asked, Is.EqualTo(1));

                    form.DialogResult = System.Windows.Forms.DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));
                Assert.That(operatorAt.Asked, Is.EqualTo(2), "the next renewal raised the dialog again");
            }
        }

        [Test]
        public void A_stored_answer_is_what_the_client_is_told_when_the_session_has_one_too()
        {
            // Only a merge brings the two together, since the dialog is raised while neither
            // refuses: forever says that pairing again is the only way, which the session's answer
            // would leave the client to find out after KeePass next starts.
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.ThisSession()));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));
                Assume.That(_authority.RefuseRenewal(CertificateDigest.Of(old), RenewalRefusal.Forever()).Ok, Is.True);

                var again = Renew(operatorAt, old, "{}");

                Assert.That(again.Json, Is.EqualTo(HttpResponse.ErrorBody("forever")));
            }
        }

        [TestCase("forever", "forever")]
        [TestCase("this session", "this session")]
        public void A_second_renewal_while_the_first_is_answered_is_busy_and_the_next_sees_the_answer(
            string given, string told)
        {
            // The second arrives while the operator is deciding the first, as an agent retrying or
            // the command run again would. It used to read the standing answer before it existed,
            // wait its turn for the screen and raise the same dialog the moment the first closed.
            var standing = given == "forever" ? RenewalRefusal.Forever() : RenewalRefusal.ThisSession();
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(standing));
            var router = Router(Handler(operatorAt));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                GateContext second = null;
                operatorAt.Meanwhile = () =>
                {
                    operatorAt.Meanwhile = null;
                    second = Context(old, "{}");
                    router.Handle(second);
                };

                var first = Context(old, "{}");
                router.Handle(first);

                Assert.That(first.Status, Is.EqualTo(403));
                Assert.That(second.Status, Is.EqualTo(503), "answered at once, with nobody asked");
                Assert.That(second.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
                Assert.That(operatorAt.Asked, Is.EqualTo(1));

                var next = Context(old, "{}");
                router.Handle(next);

                Assert.That(next.Status, Is.EqualTo(409), "the first is answered, and its answer stands");
                Assert.That(next.Json, Is.EqualTo(HttpResponse.ErrorBody(told)));
                Assert.That(operatorAt.Asked, Is.EqualTo(1));
            }
        }

        [Test]
        public void One_arriving_after_the_dialog_closed_and_before_the_answer_landed_is_busy_too()
        {
            // The narrower window: the screen is free again while the first renewal's standing
            // answer is still being written, and a renewal read before that write found nothing
            // to refuse it. The first stays in flight until the answer is kept.
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.Forever()));
            var router = Router(Handler(operatorAt));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                GateContext second = null;
                _authority.WhileRefusing = () =>
                {
                    _authority.WhileRefusing = null;
                    second = Context(old, "{}");
                    router.Handle(second);
                };

                var first = Context(old, "{}");
                router.Handle(first);

                Assert.That(first.Status, Is.EqualTo(403));
                Assert.That(second.Status, Is.EqualTo(503));
                Assert.That(operatorAt.Asked, Is.EqualTo(1));
                Assert.That(_authority.StandingRefusal(CertificateDigest.Of(old)).IsForever, Is.True);
            }
        }

        [Test]
        public void A_renewal_of_another_certificate_is_not_held_up_by_one_in_flight()
        {
            var operatorAt = FakeRenewalApproval.Approving();
            var router = Router(Handler(operatorAt));

            using (var old = _authority.IssueAdmittedClient("reader"))
            using (var other = _authority.IssueAdmittedClient("reader"))
            {
                GateContext second = null;
                operatorAt.Meanwhile = () =>
                {
                    operatorAt.Meanwhile = null;
                    second = Context(other, "{}");
                    router.Handle(second);
                };

                var first = Context(old, "{}");
                router.Handle(first);

                Assert.That(first.Status, Is.EqualTo(200), first.Json);
                Assert.That(second.Status, Is.EqualTo(200), second.Json);
                Assert.That(operatorAt.Asked, Is.EqualTo(2));
            }
        }

        [Test]
        public void A_certificate_can_be_renewed_again_once_its_renewal_has_been_answered()
        {
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused());
            var router = Router(Handler(operatorAt));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                router.Handle(Context(old, "{}"));
                var again = Context(old, "{}");
                router.Handle(again);

                Assert.That(again.Status, Is.EqualTo(403), "a refusal once keeps nothing, in flight or stored");
                Assert.That(operatorAt.Asked, Is.EqualTo(2));
            }
        }

        [Test]
        public void Records_that_could_not_be_listed_are_busy_rather_than_unpaired()
        {
            // A write on KeePass's thread changing the dictionary while it is enumerated is all
            // that takes, and the next connection is admitted again: telling the client it was
            // never admitted sends it off to pair again for nothing.
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                _authority.AdmissionThrows = new KeePassGate.Diagnostics.VaultUnsettled("the admission records could not be listed");

                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
                Assert.That(operatorAt.Asked, Is.Zero);
            }
        }

        [Test]
        public void A_list_of_profiles_that_will_not_read_is_busy_rather_than_a_renewal_that_drops_them_all()
        {
            // Read as naming nothing, it offered no profile, said the database no longer named
            // them, and an approval wrote a record without them that outlasted the list's repair.
            var operatorAt = FakeRenewalApproval.Approving();
            _authority.ProfilesUnreadable = "the profiles this database names will not read";

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var before = _authority.Admitted;
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(operatorAt.Asked, Is.Zero);
                Assert.That(_authority.Admitted, Is.SameAs(before));
            }
        }

        [Test]
        public void A_standing_answer_that_cannot_be_read_is_busy_rather_than_a_dialog()
        {
            // A dialog is the one thing a standing answer exists to stop, so a read that fails
            // raises none: the client is told to ask again, as the store refuses for a value it
            // cannot read.
            var operatorAt = FakeRenewalApproval.Approving();
            _authority.StandingRefusalThrows = new InvalidOperationException("the custom data would not read");

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
                Assert.That(operatorAt.Asked, Is.Zero);
            }
        }

        [Test]
        public void A_standing_answer_that_could_not_be_written_holds_from_memory_at_once()
        {
            // The operator's ruling of 2026-10-14: it takes effect at once, like one for this
            // session, and is owed to the database until the plugin's next write lands with it.
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.Forever()));
            _authority.RefuseRenewalAnswers = VaultWrite.Busy("a dialog is open");

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(403), "the refusal stands");
                Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("holds from memory"));
                Assert.That(_session.Owed().Single().Refusal.IsForever, Is.True);

                var again = Renew(operatorAt, old, "{}");

                Assert.That(again.Status, Is.EqualTo(409));
                Assert.That(again.Json, Is.EqualTo(HttpResponse.ErrorBody("forever")));
                Assert.That(operatorAt.Asked, Is.EqualTo(1), "answered without the dialog");
            }
        }

        [Test]
        public void An_owed_answer_until_a_date_stops_refusing_once_its_day_begins()
        {
            var until = _now.AddDays(2);
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Refused(RenewalRefusal.Until(until)));
            _authority.RefuseRenewalAnswers = VaultWrite.Busy("a dialog is open");

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(403));

                var early = Renew(operatorAt, old, "{}");
                Assert.That(early.Status, Is.EqualTo(409));
                Assert.That(JsonBody.Read(early.Json).Text("notBefore"), Is.EqualTo(Moments.Wire(until)));

                _now = until;
                operatorAt = FakeRenewalApproval.Approving();
                Assert.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(200));
            }
        }

        [TestCase("timed out", 504, "unanswered")]
        [TestCase("unavailable", 503, "busy")]
        public void A_renewal_nobody_decided_changes_nothing(string outcome, int status, string error)
        {
            var operatorAt = new FakeRenewalApproval(
                outcome == "timed out" ? RenewalApproval.TimedOut() : RenewalApproval.Unavailable("no screen"));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(status));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(error)));
            }
        }

        [Test]
        public void A_dialog_that_would_take_the_record_back_refuses_before_anybody_is_asked()
        {
            var operatorAt = FakeRenewalApproval.Approving();
            _authority.AdmissionBlocked = "Database Maintenance is open";

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(operatorAt.Asked, Is.Zero);
            }
        }

        [Test]
        public void One_opened_while_the_operator_was_deciding_is_found_before_anything_is_issued()
        {
            var operatorAt = FakeRenewalApproval.Approving();
            operatorAt.Meanwhile = () => _authority.AdmissionBlocked = "Database Maintenance is open";

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var before = _authority.Admitted;
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(_authority.Admitted, Is.SameAs(before), "nothing was written");
            }
        }

        [Test]
        public void A_busy_that_outlasts_the_retry_window_holds_the_approval_for_the_next_request()
        {
            // The approval is kept across requests, held for the pairing budget, so a client that
            // waits out the busy and asks again is written without the dialog: the operator is
            // asked once, and counted here so that a change either way is seen.
            var operatorAt = FakeRenewalApproval.Approving();
            operatorAt.Meanwhile = () => _authority.AdmissionBlocked = "Database Maintenance is open";

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var busy = Renew(operatorAt, old, "{}");
                Assert.That(busy.Status, Is.EqualTo(503));
                Assert.That(busy.Json, Does.Contain("heldUntil"));
                Assert.That(operatorAt.Asked, Is.EqualTo(1));

                operatorAt.Meanwhile = null;
                _authority.AdmissionBlocked = null;

                var again = Renew(operatorAt, old, "{}");

                Assert.That(again.Status, Is.EqualTo(200), again.Json);
                Assert.That(operatorAt.Asked, Is.EqualTo(1), "asked once for both requests");
            }
        }

        [TestCase("by the client")]
        [TestCase("by the operator")]
        public void A_record_revoked_while_the_operator_was_deciding_is_found_before_anything_is_issued(string by)
        {
            // The certificate's own revocation on another connection, which the dialog's message
            // loop runs, or the operator's landing while the renewal waited its turn: the record
            // the renewal was about is gone, so it is told what every connection will be told.
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                operatorAt.Meanwhile = by == "by the client"
                    ? (Action)(() => Assume.That(_authority.Revoke(CertificateDigest.Of(old)).Ok, Is.True))
                    : () => Assume.That(_authority.Revoke(old), Is.True);

                var before = _authority.Admitted;
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
                Assert.That(_authority.Admitted, Is.SameAs(before), "nothing was written");
                Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("after its record went"));
            }
        }

        [Test]
        public void A_revocation_that_lands_under_the_write_is_not_overtaken()
        {
            // The record is read again after the dialog, but on the connection's thread: a
            // revocation landing between that read and the write, by the client's own
            // `DELETE /gate/certificate` on another connection, was overtaken, and the revoked
            // certificate's holder was handed a new one. The write compares the record again under
            // its own lock, which is the lock the revocation is written under.
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                _authority.BeforeWrite = () => Assume.That(_authority.Revoke(old), Is.True);

                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
                Assert.That(_authority.Admitted, Is.Null, "nothing was written");
            }
        }

        [Test]
        public void An_approval_KeePass_is_too_busy_to_write_is_held_and_written_on_the_retry_without_the_dialog()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                _authority.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
                var first = Renew(operatorAt, old, "{}");

                Assert.That(first.Status, Is.EqualTo(503), first.Json);
                Assert.That(JsonBody.Read(first.Json).Text("heldUntil"), Is.EqualTo(Moments.Wire(_now + GateOptions.DefaultPairingBudget)));

                _authority.AdmitAnswers = null;
                var second = Renew(operatorAt, old, "{}");

                Assert.That(second.Status, Is.EqualTo(200), second.Json);
                Assert.That(operatorAt.Asked, Is.EqualTo(1), "the operator answered once");
                Assert.That(_held.Count, Is.Zero);

                var third = Renew(operatorAt, old, "{}");
                Assert.That(third.Status, Is.EqualTo(200), third.Json);
                Assert.That(operatorAt.Asked, Is.EqualTo(2), "a landed write used the approval up");
            }
        }

        [TestCase("forever")]
        [TestCase("this session")]
        public void A_standing_answer_given_after_a_held_approval_is_what_the_retry_is_told(string given)
        {
            // A merge brings a stored answer in, or the operator answers another renewal of the
            // same certificate in this session. Either outranks the approval held for the retry,
            // which used to be written without asking whether one now stood.
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var key = CertificateDigest.Of(old);
                _authority.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(503));
                _authority.AdmitAnswers = null;

                if (given == "forever")
                {
                    Assume.That(_authority.RefuseRenewal(key, RenewalRefusal.Forever()).Ok, Is.True);
                }
                else
                {
                    _session.Refuse(key);
                }

                var retry = Renew(operatorAt, old, "{}");

                Assert.That(retry.Status, Is.EqualTo(409), retry.Json);
                Assert.That(retry.Json, Is.EqualTo(HttpResponse.ErrorBody(given)));
                Assert.That(_authority.Admitted, Is.Null, "nothing was written");
                Assert.That(_held.Count, Is.Zero, "the approval is let go");
                Assert.That(operatorAt.Asked, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_held_approval_runs_out_with_the_pairing_budget_and_the_dialog_comes_up_again()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                _authority.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
                Assume.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(503));

                _now += GateOptions.DefaultPairingBudget;
                _authority.AdmitAnswers = null;

                Assert.That(Renew(operatorAt, old, "{}").Status, Is.EqualTo(200));
                Assert.That(operatorAt.Asked, Is.EqualTo(2));
            }
        }

        [Test]
        public void A_lock_while_the_approval_settles_is_answered_as_the_lock_and_writes_nothing()
        {
            // KeePass busy as the operator answers, then a lock: the approval is let go, and the
            // wait ends at once rather than writing into the database a quick unlock opens again,
            // after the unlock has closed the connection that would have carried the answer.
            //
            // The settle is longer than the operator's two seconds because what is tested is the
            // lock ending the wait, and a loaded machine starting the lock's thread late would
            // otherwise end it first. The wait ends at the lock, so a longer one costs nothing.
            _options.ApprovalSettle = TimeSpan.FromSeconds(30);
            var operatorAt = FakeRenewalApproval.Approving();
            System.Threading.Tasks.Task locking = null;
            operatorAt.Meanwhile = () =>
            {
                _authority.AdmissionBlocked = "KeePass is saving";
                locking = System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(300);
                    ((IApprovalScreen)_held).Locked();
                    _authority.AdmissionBlocked = null;
                });
            };

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");
                locking.Wait();

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("locked")));
                Assert.That(_authority.Admitted, Is.Null);
            }
        }

        [Test]
        public void A_busy_that_clears_within_the_retry_window_does_not_cost_the_approval()
        {
            // Answered at once, the approval was lost to a moment's save, and a client with a
            // budget for busy answers put the same dialog in front of the operator again.
            //
            // Longer than the operator's two seconds, for the reason the lock test above gives:
            // the wait ends as the busy clears, and a late thread must not end it first.
            _options.ApprovalSettle = TimeSpan.FromSeconds(30);
            var operatorAt = FakeRenewalApproval.Approving();
            operatorAt.Meanwhile = () =>
            {
                _authority.AdmissionBlocked = "KeePass is saving";
                var clears = new System.Threading.Thread(() =>
                {
                    System.Threading.Thread.Sleep(300);
                    _authority.AdmissionBlocked = null;
                });
                clears.IsBackground = true;
                clears.Start();
            };

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(operatorAt.Asked, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_record_that_did_not_land_hands_nothing_over()
        {
            var operatorAt = FakeRenewalApproval.Approving();
            _authority.AdmitAnswers = AdmissionWrite.Failed("the save did not land");

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, "{}");

                Assert.That(context.Status, Is.EqualTo(500));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("not recorded")));
            }
        }

        [Test]
        public void A_lock_that_took_the_question_away_changes_nothing_and_says_the_database_is_locked()
        {
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var before = _authority.Admitted;
                var context = Renew(new FakeRenewalApproval(RenewalApproval.Locked()), old, "{}");

                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
                Assert.That(_authority.Admitted, Is.SameAs(before), "nothing was written");
                Assert.That(_authority.StandingRefusal(CertificateDigest.Of(old)), Is.Null);
            }
        }

        [Test]
        public void A_lifetime_no_dialog_offers_is_a_defect_rather_than_a_failed_issuance()
        {
            // The wire cannot reach it and neither can the dialog, so only a defect can: it leaves
            // the handler for the connection thread's catch-all, which counts it, instead of being
            // answered as a key that would not sign.
            var operatorAt = new FakeRenewalApproval(
                RenewalApproval.Approved(new[] { "reader" }, TimeSpan.FromDays(400)));

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Assert.That(() => Renew(operatorAt, old, "{}"), Throws.InstanceOf<ArgumentOutOfRangeException>());
            }
        }

        [Test]
        public void An_authority_past_its_century_issues_nothing_and_says_so()
        {
            var aCenturyOn = new AuthorityIssuer(
                () => DateTimeOffset.UtcNow + AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(1));
            var handler = new RenewalHandler(
                FakeRenewalApproval.Approving(), new PairingOptions(), aCenturyOn, () => _now, _session, _log);

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Context(old, "{}");
                Router(handler).Handle(context);

                Assert.That(context.Status, Is.EqualTo(500));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("internal error")));
            }
        }

        [Test]
        public void A_certificate_revoked_in_the_meantime_is_told_what_every_connection_will_be()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var client = Paired(old);
                _authority.Revoke(old);

                var context = Context(client, "{}");
                Router(Handler(operatorAt)).Handle(context);

                Assert.That(context.Status, Is.EqualTo(401));
                Assert.That(operatorAt.Asked, Is.Zero);
            }
        }

        [TestCase("")]
        [TestCase("{\"lifetime\":\"P366D\"}")]
        [TestCase("{\"lifetime\":\"30\"}")]
        [TestCase("{\"name\":\"somebody else\"}")]
        [TestCase("[]")]
        public void A_body_that_is_not_what_the_document_declares_asks_nobody(string body)
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Renew(operatorAt, old, body);

                Assert.That(context.Status, Is.EqualTo(400), context.Json);
                Assert.That(operatorAt.Asked, Is.Zero);
            }
        }

        [Test]
        public void The_handler_reads_the_lifetime_again_rather_than_trusting_the_document()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Context(Paired(old), "{\"lifetime\":\"P0D\"}");
                Handler(operatorAt).Renew(context);

                Assert.That(context.Status, Is.EqualTo(400));
                Assert.That(operatorAt.Asked, Is.Zero);
            }
        }

        [Test]
        public void A_build_that_renews_nothing_answers_as_a_route_it_does_not_serve()
        {
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var context = Context(Paired(old), "{}");
                GateOperations.Router("9.9.9").Handle(context);

                Assert.That(context.Status, Is.EqualTo(404));
            }
        }

        [Test]
        public void The_path_this_handler_knows_is_the_one_the_document_declares()
        {
            Assert.That(GateOperations.PathOf(GateOperations.RenewCertificate), Is.EqualTo(RenewalHandler.Path));
        }

        [TestCase("CN=agent-fictitious", "agent-fictitious")]
        [TestCase("CN=Agent One_1.2-3", "Agent One_1.2-3")]
        [TestCase("CN=", null)]
        [TestCase("O=somebody", null)]
        [TestCase("CN=a, O=b", null)]
        [TestCase(null, null)]
        public void The_name_is_read_back_off_the_subject_the_issuer_wrote(string subject, string name)
        {
            Assert.That(RenewalHandler.NameIn(subject), Is.EqualTo(name));
        }

        private GateContext Renew(FakeRenewalApproval operatorAt, X509Certificate2 certificate, string body)
        {
            var context = Context(Paired(certificate), body);
            Router(Handler(operatorAt)).Handle(context);
            return context;
        }

        private GateContext Context(X509Certificate2 certificate, string body)
        {
            return Context(Paired(certificate), body);
        }

        private GateContext Context(ClientIdentity client, string body)
        {
            var head = new HttpRequestHead(
                "POST",
                RenewalHandler.Path,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                body.Length);

            var context = new GateContext(head, body, client);
            new ServedDatabase(null, _authority).PutOn(context);
            ConnectionPeer.PutOn(context, Peer);
            return context;
        }

        private RenewalHandler Handler(FakeRenewalApproval operatorAt)
        {
            return new RenewalHandler(
                operatorAt, _options, new AuthorityIssuer(), () => _now, _session, _log, _held);
        }

        private static SpecRouter Router(RenewalHandler handler)
        {
            return GateOperations.Router("9.9.9", null, handler, new AccessCheck(NullGateAudit.Instance));
        }

        private ClientIdentity Paired(X509Certificate2 certificate)
        {
            return ClientIdentity.Paired(_authority.DatabaseId, certificate, new[] { "reader" });
        }

        private static X509Certificate2 Identity(string bundle)
        {
            var start = bundle.IndexOf("\"identity\":\"", StringComparison.Ordinal) + "\"identity\":\"".Length;
            var end = bundle.IndexOf('"', start);
            return CertificateBlob.Load(bundle.Substring(start, end - start), X509KeyStorageFlags.EphemeralKeySet);
        }
    }
}
