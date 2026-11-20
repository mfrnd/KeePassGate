using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;
using PairingApprovalForm = KeePassGate.Ui.PairingApprovalForm;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The pairing routes, driven the way a client drives them, with a real authority behind them
    /// and a stand-in for the person at the keyboard.
    /// </summary>
    /// <remarks>
    /// The ordering assertions are the ones worth reading. What is spent and what is not, on each
    /// of the ways a pairing can end, is `PAIRING.md` §306 restated as tests, and getting one
    /// of them wrong is either an invitation that can be used twice or an invitee who loses theirs
    /// because nobody was at the screen.
    /// </remarks>
    [TestFixture]
    public sealed class PairingHandlerTests
    {
        // Where the document declares the two operations, which is how the router chooses them.
        private static readonly string HelloPath = GateOperations.PathOf(GateOperations.PairHello);
        private static readonly string RequestPath = GateOperations.PathOf(GateOperations.PairRequest);

        private TestAuthority _target;
        private InvitationBook _book;
        private Invitation _invitation;
        private RecordingGateLog _log;
        private byte[] _binding;
        private byte[] _nonce;
        private byte[] _token;
        private PairingConnection _connection;
        private AuthorityIssuer _issuer;
        private RecordingAudit _audit;
        private ApprovalBook _held;
        private GateIncidents _incidents;
        private PairingOptions _options;

        /// <summary>The handler's clock where a test sets a clock, or null for the system's.</summary>
        private Func<DateTimeOffset> _clock;

        /// <summary>What stood as each dialog came up, <see cref="Standing"/>, in order.</summary>
        private List<string> _atEachDialog;

        [SetUp]
        public void SetUp()
        {
            _issuer = null;
            _options = null;
            _clock = null;
            _atEachDialog = new List<string>();
            _audit = new RecordingAudit();
            _held = new ApprovalBook();
            _target = TestAuthority.New();
            _log = new RecordingGateLog();
            _incidents = new GateIncidents(_log);
            _book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log);
            _invitation = _book.Invite(_target.DatabaseId);
            _token = _invitation.Token;

            _binding = PairingProof.Binding(_target.Host.CertificateDer);
            _nonce = new byte[SniName.NonceLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(_nonce);
            }

            _connection = new PairingConnection(_invitation, _target, _binding, _nonce, "127.0.0.1:60001");
        }

        [TearDown]
        public void TearDown()
        {
            _book.Dispose();
            _target.Dispose();
        }

        [Test]
        public void A_client_that_proves_it_holds_the_token_is_answered_with_a_proof_of_its_own()
        {
            var context = Hello(ClientProof());

            Assert.That(context.Status, Is.EqualTo(200), context.Json);

            var body = JsonBody.Read(context.Json);
            var challenge = Convert.FromBase64String(body.Text("challenge"));
            var proof = Convert.FromBase64String(body.Text("proof"));

            Assert.That(challenge.Length, Is.EqualTo(PairingChallenge.Length));
            Assert.That(proof, Is.EqualTo(PairingProof.Server(_token, _binding, _nonce, challenge)),
                "the server proof is what tells a client with no anchor that this is the real one");
        }

        [Test]
        public void The_answer_carries_the_version_a_client_compares_with_its_own()
        {
            // API.md §411, so that a client refuses before the operator is asked: a pairing
            // connection reaches the two pairing routes and nothing else, so the status route a
            // paired client asks is not a route a pairing client can.
            var context = Hello(ClientProof());

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            var version = JsonBody.Read(context.Json).Text("version");
            Assert.That(version, Is.EqualTo(GateVersion.Current),
                "the version the status route reports");
        }

        [Test]
        public void A_client_that_cannot_prove_it_is_refused_and_nothing_is_spent()
        {
            var context = Hello(new byte[32]);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Does.Contain("proof"));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1), "a failed proof must not spend anything");
        }

        [Test]
        public void A_failed_proof_leaves_no_exchange_behind_for_a_request_to_use()
        {
            Hello(new byte[32]);

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(409));
        }

        [Test]
        public void A_failed_proof_raises_no_dialog()
        {
            var approval = Approving();

            Hello(new byte[32]);
            Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(approval.Asked, Is.Zero,
                "a replayed name must die at the proof rather than in front of the operator");
        }

        [TestCase("{}")]
        [TestCase("{\"proof\":\"not base64 at all\"}")]
        [TestCase("not json")]
        [TestCase("")]
        [TestCase("{\"proof\":[\"AAAA\"]}")]
        public void A_hello_that_is_not_a_hello_is_refused(string body)
        {
            var context = Send("POST", HelloPath, body, _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_connection_that_did_not_arrive_on_an_invitation_is_told_what_every_route_tells_it()
        {
            var context = Send("POST", HelloPath, "{}", null, Approving());

            // 401, which is what the router answers an unadmitted caller about every path there
            // is, rather than the 404 this used to give. What that buys is one refusal for one
            // caller: whether a route exists is not what it hides, because the routes are
            // declared in a document that ships inside the plugin and `GET /pair/hello` answers
            // `405` for that reason. What it hides is whether this caller was close to using it.
            Assert.That(context.Status, Is.EqualTo(401),
                "a caller that may not pair is refused the way it is refused everywhere");
        }

        [Test]
        public void A_client_that_already_has_a_certificate_finds_no_such_route_either()
        {
            using (var certificate = _target.IssueAdmittedClient())
            {
                var context = Send(
                    "POST",
                    HelloPath,
                    "{}",
                    _connection,
                    Approving(),
                    ClientIdentity.Paired(_target.DatabaseId, certificate, Array.Empty<string>()));

                Assert.That(context.Status, Is.EqualTo(404));
            }
        }

        [Test]
        public void A_request_with_no_hello_before_it_is_refused()
        {
            var context = Send(
                "POST", RequestPath, RequestBody("agent-fictitious", Array.Empty<string>(), null, new byte[32]),
                _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(409));
        }

        [Test]
        public void A_request_that_cannot_prove_itself_is_refused_and_the_exchange_is_over()
        {
            Hello(ClientProof());

            var wrong = Send(
                "POST", RequestPath, RequestBody("agent-fictitious", Array.Empty<string>(), null, new byte[32]),
                _connection, Approving());

            Assert.That(wrong.Status, Is.EqualTo(401));

            var again = Request("agent-fictitious", Array.Empty<string>(), Approving());
            Assert.That(again.Status, Is.EqualTo(409),
                "a challenge is spent by being used, however that use turned out");
        }

        [Test]
        public void A_request_answered_on_a_different_certificate_is_refused()
        {
            Hello(ClientProof());

            // The same invitation, but the connection was served with something else, which is what
            // an on-path attacker holding a stolen token would produce.
            using (var elsewhere = TestAuthority.New())
            {
                var other = new PairingConnection(
                    _invitation, _target, PairingProof.Binding(elsewhere.Host.CertificateDer), _nonce, "peer");

                var context = Send(
                    "POST", RequestPath,
                    RequestBody("agent-fictitious", Array.Empty<string>(), null, new byte[32]), other, Approving());

                Assert.That(context.Status, Is.EqualTo(409));
            }
        }

        [Test]
        public void An_approved_request_comes_back_with_a_certificate_this_authority_issued()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, Approving("ops"));

            Assert.That(context.Status, Is.EqualTo(200), context.Json);

            using (var issued = IdentityIn(context))
            {
                // Its key is asserted by `IdentityIn`, which reads the identity the way the client's
                // own reader would: a certificate stored with a key that is the one it certifies.
                Assert.That(issued.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(AnchorPin.Chains(issued, _target.Authority), Is.True);
            }
        }

        [Test]
        public void What_comes_back_is_everything_a_client_needs_and_nothing_else()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, Approving("ops"));
            var body = JsonBody.Read(context.Json);

            Assert.That(body, Is.Not.Null, context.Json);
            Assert.That(Convert.FromBase64String(body.Text("authority")), Is.EqualTo(_target.Authority.RawData));
            Assert.That(Convert.FromBase64String(body.Text("gateKey")), Is.EqualTo(_target.GateKey));
            Assert.That(body.Text("subject"), Is.EqualTo("CN=agent-fictitious"));
            Assert.That(body.Array("profiles"), Is.EqualTo(new[] { "ops" }));
            Assert.That(body.Text("expires"), Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
        }

        [Test]
        public void The_certificate_it_hands_over_works_on_the_very_next_connection()
        {
            // The whole point of the exercise, and the only assertion that covers the ordering:
            // issued, recorded, and admitted by the same database, without anything else happening.
            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, Approving("ops"));

            using (var issued = IdentityIn(context))
            {
                var identity = ClientCredentials.For(issued, _target, _log);

                Assert.That(identity.IsPaired, Is.True);
                Assert.That(identity.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void The_record_says_what_the_operator_approved_rather_than_what_was_asked_for()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "admin" }, Approving("ops"));

            using (var issued = IdentityIn(context))
            {
                var admitted = _target.Admission(CertificateDigest.Of(issued));

                Assert.That(admitted, Is.Not.Null);
                Assert.That(admitted.Record.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void The_log_says_who_paired_and_not_what_they_may_reach()
        {
            // Which profiles a client holds is in the record already, and a log repeating them is a
            // second copy of the thing worth stealing, in a file nothing rotates.
            Hello(ClientProof());

            Request("agent-fictitious", new[] { "ops-invented" }, Approving("ops-invented"));

            Assert.That(_log.Entries.Any(e => e.Message.Contains("paired CN=agent-fictitious")), Is.True);
            Assert.That(_log.Entries.Any(e => e.Message.Contains("ops-invented")), Is.False);
        }

        [Test]
        public void The_operator_is_shown_what_is_being_asked_for()
        {
            Hello(ClientProof());
            var approval = Approving("ops");

            Request("agent-fictitious", new[] { "ops", "admin" }, approval);

            Assert.That(approval.LastRequest.Name, Is.EqualTo("agent-fictitious"));
            Assert.That(approval.LastRequest.Profiles, Is.EqualTo(new[] { "ops", "admin" }));
            Assert.That(approval.LastRequest.DatabaseId, Is.EqualTo(_target.DatabaseId));
            Assert.That(approval.LastRequest.InvitationId, Is.EqualTo(_invitation.Id));
            Assert.That(approval.LastRequest.Peer, Is.EqualTo("127.0.0.1:60001"));
        }

        [Test]
        public void An_approved_pairing_spends_the_invitation()
        {
            Hello(ClientProof());
            Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(_book.Outstanding(), Is.Empty);
        }

        [Test]
        public void The_same_invitation_cannot_pair_a_second_client()
        {
            Hello(ClientProof());
            Request("agent-fictitious", Array.Empty<string>(), Approving());

            var again = Hello(ClientProof());

            Assert.That(again.Status, Is.EqualTo(409));
        }

        [Test]
        public void A_pairing_that_loses_the_race_to_spend_the_invitation_is_handed_nothing()
        {
            // Two pairings can be in flight on one invitation, and the operator can be reading one
            // dialog while the other is spending it. Only one of them can win, and the winner is
            // whoever burns it: the loser is turned away with nothing issued and no record written.
            //
            // What this pins is the ordering rather than the race. The burn comes before the
            // record, so a rearrangement that wrote first would admit both of them, and every other
            // test here would still pass: they check that the invitation ends up gone, which it
            // does either way.
            Hello(ClientProof());

            var approval = new FakePairingApproval(request =>
            {
                // The other pairing got there while this dialog was up.
                _book.Burn(_invitation.Id);
                return PairingApproval.Approved(Array.Empty<string>());
            });

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(409));
            Assert.That(context.Json, Does.Contain("no invitation"));
            Assert.That(context.Json, Does.Not.Contain("identity"));
            Assert.That(_audit.Entries, Is.Empty, "an approval that admitted nobody is not written down");
            Assert.That(_target.Admitted, Is.Null, "the loser must leave no record of its own behind");
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("lost the race")), Is.True);
        }

        [Test]
        public void A_refusal_spends_the_invitation_and_issues_nothing()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), FakePairingApproval.Refusing());

            Assert.That(context.Status, Is.EqualTo(403));
            Assert.That(context.Json, Does.Contain("refused"));
            Assert.That(_book.Outstanding(), Is.Empty, "a refusal is a decision, and it spends the invitation");
        }

        [Test]
        public void Nobody_answering_spends_nothing()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), FakePairingApproval.Absent());

            Assert.That(context.Status, Is.EqualTo(504));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1),
                "an operator who is away must not cost the invitee its invitation");
        }

        [Test]
        public void A_lock_that_took_the_question_away_spends_nothing_and_says_the_database_is_locked()
        {
            // `PAIRING.md` §308: unanswered, as a timeout is, and told what every connection to a
            // locked database is told.
            Hello(ClientProof());

            var context = Request(
                "agent-fictitious", Array.Empty<string>(), new FakePairingApproval(PairingApproval.Locked()));

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
            Assert.That(_audit.Entries, Is.Empty,
                "the screen that took the question away wrote it down as it did, so the handler writes nothing");
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        public void A_pairing_whose_dialog_its_database_s_close_took_away_is_unanswered_and_writes_nothing()
        {
            // The operator's ruling of 2026-11-08: the dialog goes with its database, as with a
            // lock, so nothing can be approved over a database that is gone. It is answered as a
            // dialog a lock closed is, unanswered, and the log names the close.
            var prompt = new KeePassGate.Ui.PairingApprovalPrompt(
                KeePassGate.Ui.CurrentThread.Instance, null, null, new AdmissionAudit(_audit), _log);
            var writtenAsItClosed = -1;
            prompt.Dialog = (request, timeout) =>
            {
                using (var dialog = new KeePassGate.Ui.PairingApprovalForm(request, timeout))
                {
                    dialog.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    dialog.Location = new System.Drawing.Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;

                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        var ticks = 0;
                        timer.Tick += delegate
                        {
                            if (++ticks == 1)
                            {
                                prompt.Closed(new[] { _target.DatabaseId });
                                writtenAsItClosed = _audit.Entries.Count;
                                return;
                            }

                            // Whatever the close left standing is refused, so that this ends.
                            timer.Stop();
                            dialog.Refuse();
                        };

                        timer.Start();
                        prompt.Show(dialog, request.DatabaseId);
                    }

                    return dialog.Answer();
                }
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), prompt);

            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_target.Admitted, Is.Null, "nothing is written");
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "nor is the invitation spent");
            Assert.That(_held.Count, Is.Zero);
            Assert.That(_audit.Entries.Single().Decision, Is.EqualTo(AuditEntry.Unanswered));
            Assert.That(writtenAsItClosed, Is.EqualTo(1),
                "written down as the close took the dialog away, on KeePass's thread, before the " +
                "dialog's loop could hand anything back: at KeePass's exit that close runs inside the " +
                "same press that unloads the plugin, and nothing waits for this thread after that");
            Assert.That(_log.Warnings.Any(w => w.Message.Contains(PairingApproval.ClosedReason)), Is.True);
        }

        [Test]
        public void A_request_a_lock_kept_off_the_screen_is_not_written_down_as_unanswered()
        {
            // S081. It waited for the screen while KeePass locked and was never shown, so nobody
            // was asked and the audit records nothing, as with no screen to ask. It was taken
            // for a dialog the lock closed, written down as left unanswered, and logged as closed.
            var prompt = new KeePassGate.Ui.PairingApprovalPrompt(new FakeUiThread(), databaseId => false, _log);
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), prompt);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_audit.Entries, Is.Empty, "nobody was asked");
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("closed unanswered")), Is.False,
                "and no dialog was closed");
        }

        [Test]
        public void No_screen_to_ask_spends_nothing_either()
        {
            Hello(ClientProof());

            var context = Request(
                "agent-fictitious", Array.Empty<string>(), new FakePairingApproval(PairingApproval.Unavailable("no screen")));

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
        }

        [Test]
        public void An_answer_the_handler_does_not_name_is_refused_as_busy_and_counted()
        {
            // An approval is the one answer that issues, so it is named. Reached by falling past
            // the other cases, a decision added without a case of its own was issued a certificate.
            Hello(ClientProof());
            var answer = PairingApproval.Approved(new[] { "reader" });
            typeof(PairingApproval).GetField("_decision", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(answer, (PairingDecision)int.MaxValue);

            var context = Request("agent-fictitious", new[] { "reader" }, new FakePairingApproval(answer));

            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_target.Admitted, Is.Null, "nothing is issued");
            Assert.That(_held.Count, Is.Zero, "nor held for a retry");
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1), "and nothing is spent");
            Assert.That(_audit.Entries, Is.Empty, "nothing was decided to write down");
            Assert.That(_incidents.Count, Is.EqualTo(1), "counted where the operator sees it");
        }

        [Test]
        public void An_approval_given_after_the_invitation_ran_out_hands_nothing_over()
        {
            // `PAIRING.md` §306: running out leads to gone, whatever else is happening. The dialog
            // can stay up longer than the invitation has left, and an answer given after that finds
            // nothing to spend, however quiet the book was in between.
            var now = DateTimeOffset.UtcNow;
            _book.Dispose();
            _book = new InvitationBook(TimeSpan.FromMinutes(10), () => now, _log);
            _invitation = _book.Invite(_target.DatabaseId);
            _token = _invitation.Token;
            _connection = new PairingConnection(_invitation, _target, _binding, _nonce, "127.0.0.1:60001");
            Hello(ClientProof());

            var approval = new FakePairingApproval(request =>
            {
                now = now.AddMinutes(11);
                return PairingApproval.Approved(Array.Empty<string>());
            });

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(409));
            Assert.That(context.Json, Does.Contain("no invitation"));
            Assert.That(context.Json, Does.Not.Contain("identity"));
            Assert.That(_target.Admitted, Is.Null, "nothing is recorded for an answer that came too late");
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("ran out while the operator was deciding")), Is.True);
        }

        [Test]
        public void A_name_put_on_the_list_while_the_dialog_showed_it_as_new_is_not_written_as_approved()
        {
            // The dialog showed the name as "(new)", and the operator answered that. A read of the
            // list after the dialog took in the name another approval put on it meanwhile, so the
            // write found the list agreeing with it and wrote the record as though the name had
            // been listed all along. What the write compares now is the list the dialog was built
            // from, which is what the operator answered: nothing is written, and the same request
            // puts the dialog up again over the list as it stands.
            Hello(ClientProof());
            var approval = Answering(request =>
            {
                if (_atEachDialog.Count == 1)
                {
                    _target.KnownProfiles = _target.KnownProfiles.Concat(new[] { "ops" }).ToArray();
                }

                return PairingApproval.Approved(new[] { "ops" });
            });

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Requests[0].IsNew("ops"), Is.True, "the operator was told the name was new");
            Assert.That(_atEachDialog[1], Is.EqualTo(NothingWritten), "nothing is recorded for an approval of what is no longer so");
            Assert.That(approval.Requests[1].IsNew("ops"), Is.False, "the dialog shown again shows the name on the list");
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
        }

        [Test]
        public void A_dialog_that_throws_is_a_refusal_to_serve_rather_than_a_failed_request()
        {
            Hello(ClientProof());
            var approval = Approving();
            approval.Throws = new InvalidOperationException("the screen fell over");

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
            Assert.That(_log.Errors, Is.Not.Empty);
            Assert.That(_audit.Entries, Is.Empty, "nobody was asked, so nothing was decided");
        }

        [Test]
        public void An_approved_pairing_is_written_down_with_its_invitation_and_the_key_it_admitted()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "reader" }, Approving("reader"));

            Assert.That(context.Status, Is.EqualTo(200), context.Json);

            var line = _audit.Entries.Single();
            Assert.That(line.OperationId, Is.EqualTo(GateOperations.PairRequest));
            Assert.That(line.Decision, Is.EqualTo(AuditEntry.Approved));
            Assert.That(line.Invitation, Is.EqualTo(_invitation.Id));
            Assert.That(line.Issued, Is.EqualTo(_target.Admitted.AdmissionKey), "the key the record admits");
            Assert.That(line.AdmissionKey, Is.Null, "the asker held no certificate");
            Assert.That(line.ClientKey, Is.EqualTo(_target.Admitted.ClientKey), "the client the record admits");
            Assert.That(line.ClientKey, Is.EqualTo(line.Issued), "whose first certificate this is");
            Assert.That(line.DatabaseId, Is.EqualTo(_target.DatabaseId));
            Assert.That(line.Line(), Does.Not.Contain("reader"), "never a profile");
        }

        [Test]
        public void A_listener_that_fails_after_the_record_landed_does_not_lose_its_line()
        {
            // The writer writes the line before it tells the Clients page, so a defect there costs
            // the client its certificate and not the operator the record of what landed, and the
            // handler writes no second line of its own.
            _target.WhenLanded = () => { throw new NullReferenceException("a listener's defect"); };
            Hello(ClientProof());

            Assert.Throws<NullReferenceException>(
                () => Request("agent-fictitious", new[] { "reader" }, Approving("reader")));

            var line = _audit.Entries.Single();
            Assert.That(line.Decision, Is.EqualTo(AuditEntry.Approved));
            Assert.That(line.Issued, Is.EqualTo(_target.Admitted.AdmissionKey));
        }

        [Test]
        public void A_refused_pairing_is_written_down_by_its_invitation_and_admits_nobody()
        {
            Hello(ClientProof());

            Request("agent-fictitious", Array.Empty<string>(), FakePairingApproval.Refusing());

            var line = _audit.Entries.Single();
            Assert.That(line.Decision, Is.EqualTo(AuditEntry.Refused));
            Assert.That(line.Reason, Is.EqualTo(AdmissionAudit.RefusedReason));
            Assert.That(line.Invitation, Is.EqualTo(_invitation.Id));
            Assert.That(line.Issued, Is.Null);
            Assert.That(line.ClientKey, Is.Null, "and so names no client");
        }

        [Test]
        public void A_pairing_nobody_answered_is_written_down_as_unanswered()
        {
            Hello(ClientProof());

            Request("agent-fictitious", Array.Empty<string>(), FakePairingApproval.Absent());

            Assert.That(_audit.Entries.Single().Decision, Is.EqualTo(AuditEntry.Unanswered));
            Assert.That(_audit.Entries.Single().Invitation, Is.EqualTo(_invitation.Id));
            Assert.That(_audit.Entries.Single().ClientKey, Is.Null);
        }

        [Test]
        public void A_pairing_nobody_could_be_asked_about_is_not_written_down()
        {
            // Nothing was decided: a screen that was not there is not an answer.
            Hello(ClientProof());

            Request(
                "agent-fictitious", Array.Empty<string>(), new FakePairingApproval(PairingApproval.Unavailable("no screen")));

            Assert.That(_audit.Entries, Is.Empty);
        }

        [Test]
        public void An_approval_that_admitted_nobody_is_not_written_down()
        {
            // `approved` always names a key that was admitted. One whose record did not land issued
            // a certificate nobody was handed, and the plugin's log says why.
            _target.AdmitAnswers = AdmissionWrite.Failed("the save did not happen");
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(500));
            Assert.That(_audit.Entries, Is.Empty);
        }

        [Test]
        public void A_proof_that_fails_is_not_written_down()
        {
            // Nobody was asked anything, and the operator's audit is not a record of strangers.
            Hello(new byte[32]);

            Assert.That(_audit.Entries, Is.Empty);
        }

        [Test]
        public void A_group_dialog_open_on_the_database_refuses_before_anybody_is_disturbed()
        {
            // The trap that shaped the design. A record written now is destroyed when that dialog
            // is dismissed with OK, so the invitation is worth more unspent than the pairing is.
            _target.AdmissionBlocked = "a group dialog is open on the group this would be written to";
            Hello(ClientProof());
            var approval = Approving();

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Does.Contain("busy"));
            Assert.That(approval.Asked, Is.Zero, "the operator is not asked about something that cannot be written");
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
        }

        [Test]
        public void Something_that_blocks_the_write_while_the_dialog_is_up_is_found_before_anything_is_spent()
        {
            // The same answer as before the dialog, for a condition that arose during it: KeePass
            // locking underneath it, or Database Maintenance opened while the operator read. Found
            // only inside the write, it came after the burn and cost the invitation.
            Hello(ClientProof());
            var approval = new FakePairingApproval(request =>
            {
                _target.AdmissionBlocked = "the database is not open";
                return PairingApproval.Approved(Array.Empty<string>());
            });

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Does.Contain("busy"));
            Assert.That(context.Json, Does.Not.Contain("identity"));
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1),
                "a 503 spends nothing, which is what makes it worth trying again");
            Assert.That(_audit.Entries, Is.Empty, "an approval that admitted nobody is not written down");
        }

        [Test]
        public void An_authority_that_has_run_out_is_answered_once_the_issuer_refuses()
        {
            // `CA.md` §219: an authority is issued for a hundred years and nothing refuses ahead of
            // that, so a database that outlives its own raises the dialog like any other and the
            // issuer is where it stops. That arrives as an exception, which is answered rather than
            // allowed to escape, because everything a handler throws is counted against the plugin.
            _issuer = new AuthorityIssuer(
                () => DateTimeOffset.UtcNow + AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(1));
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(500));
            Assert.That(context.Json, Does.Contain("internal error"));
            Assert.That(context.Json, Does.Not.Contain("identity"));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1),
                "nothing was issued against the invitation, so it is not spent");
            Assert.That(_log.Errors.Any(e => e.Message.Contains("could not be issued")), Is.True);
            Assert.That(_log.Surprises, Is.Empty);
            Assert.That(_audit.Entries, Is.Empty, "an approval that admitted nobody is not written down");
            Assert.That(_held.Count, Is.Zero, "and the approval is lost, `PAIRING.md` §311");
        }

        [Test]
        public void An_approval_whose_certificate_could_not_be_issued_is_not_kept_for_a_retry()
        {
            // S015. Nothing was issued, so the approval is lost and the invitation is not. It
            // was kept held, with nothing telling the client so, and the same request from a
            // fresh hello was issued without the dialog once the issuer worked again.
            var issuing = DateTimeOffset.UtcNow + AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(1);
            _issuer = new AuthorityIssuer(() => issuing);
            var approval = Approving();
            Hello(ClientProof());
            Assume.That(Request("agent-fictitious", Array.Empty<string>(), approval).Status, Is.EqualTo(500));

            issuing = DateTimeOffset.UtcNow;
            Hello(ClientProof());
            var retry = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(retry.Status, Is.EqualTo(200), retry.Json);
            Assert.That(approval.Asked, Is.EqualTo(2), "the operator is asked again");
        }

        [Test]
        public void The_dialog_is_told_what_is_left_of_the_budget_once_the_write_is_set_aside()
        {
            // Counted from when the request arrived, which is where the connection's budget
            // starts, so that time spent before the operator is asked comes off the dialog rather
            // than off the write after it.
            var options = new PairingOptions();
            var approval = Approving("ops");
            Hello(ClientProof());

            Request("agent-fictitious", new[] { "ops" }, approval);

            var most = options.ApprovalHeld - options.AfterTheDialog;
            Assert.That(approval.LastTimeout, Is.EqualTo(options.ApprovalTimeout));
            Assert.That(approval.LastLeft, Is.LessThanOrEqualTo(most));
            Assert.That(approval.LastLeft, Is.GreaterThan(most - TimeSpan.FromSeconds(5)));
        }

        [Test]
        public void A_lifetime_asked_for_is_put_to_the_operator()
        {
            var approval = Approving();
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), "P30D", approval);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.LastRequest.Lifetime, Is.EqualTo(TimeSpan.FromDays(30)));
        }

        [Test]
        public void A_request_asking_for_no_lifetime_is_offered_a_year()
        {
            var approval = Approving();
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(approval.LastRequest.Lifetime, Is.Null, "nothing was asked for");
            using (var certificate = IdentityIn(context))
            {
                Assert.That(Lasts(certificate), Is.EqualTo(Lifetime.Unasked).Within(TimeSpan.FromMinutes(1)));
            }
        }

        [Test]
        public void The_certificate_lasts_what_the_operator_picked_rather_than_what_was_asked()
        {
            // The client proposes and the operator picks: thirty days asked for, seven given.
            var approval = new FakePairingApproval(
                request => PairingApproval.Approved(request.Profiles, TimeSpan.FromDays(7)));
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), "P30D", approval);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            using (var certificate = IdentityIn(context))
            {
                Assert.That(Lasts(certificate), Is.EqualTo(TimeSpan.FromDays(7)).Within(TimeSpan.FromMinutes(1)));
            }

            Assert.That(JsonBody.Read(context.Json).Text("expires"), Is.Not.Null.And.Not.Empty,
                "and the bundle says until when");
        }

        [TestCase("P0D")]
        [TestCase("P366D")]
        [TestCase("P030D")]
        [TestCase("30")]
        [TestCase("")]
        public void A_lifetime_that_is_not_one_is_malformed_and_spends_nothing(string written)
        {
            var approval = Approving();
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), written, approval);

            Assert.That(context.Status, Is.EqualTo(400), context.Json);
            Assert.That(context.Json, Does.Contain("malformed"));
            Assert.That(approval.Asked, Is.Zero, "nobody is asked about a request that could never be issued");
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1), "and the invitation is still good");

            // And so is the hello's challenge, which is spent only once the body has been read:
            // a request that follows on it, with no hello in between, is still heard.
            var following = Request("agent-fictitious", Array.Empty<string>(), approval);
            Assert.That(following.Status, Is.EqualTo(200), following.Json);
        }

        [Test]
        public void A_lifetime_changed_on_the_way_does_not_prove_itself()
        {
            // The operator is shown the lifetime, so it is signed for, as the name and the profiles
            // are: a request asking for a year that was signed for a month is somebody else's.
            var approval = Approving();
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), "P30D", "P365D", approval);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Does.Contain("proof"));
            Assert.That(approval.Asked, Is.Zero);
        }

        /// <summary>How long a certificate was issued for, counted from now.</summary>
        private static TimeSpan Lasts(X509Certificate2 certificate)
        {
            return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero) - DateTimeOffset.UtcNow;
        }

        [Test]
        public void The_client_is_never_told_what_is_open_on_the_operators_screen()
        {
            _target.AdmissionBlocked = "a group dialog is open on the group this would be written to";
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Json, Does.Not.Contain("group"));
            Assert.That(context.Json, Does.Not.Contain("dialog"));
        }

        [Test]
        public void A_record_that_does_not_land_hands_nothing_over()
        {
            // The rule the whole ordering exists for: a certificate the database has no record of
            // would be a credential nobody can see and nobody can revoke.
            _target.AdmitAnswers = AdmissionWrite.Failed("the save did not happen");
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(500));
            Assert.That(context.Json, Does.Not.Contain("identity"));
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("did not land")), Is.True);
        }

        [Test]
        public void An_approval_KeePass_is_too_busy_to_write_is_held_and_the_client_told_until_when()
        {
            // The writer found KeePass busy before it took anything, so nothing is spent: the
            // approval is held, and the client is told when it stops being held.
            _target.AdmitAnswers = AdmissionWrite.Busy("another admission record is already being written");
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(503));
            var body = JsonBody.Read(context.Json);
            Assert.That(body.Text("error"), Is.EqualTo("busy"));
            var until = DateTimeOffset.ParseExact(
                body.Text("heldUntil"), Moments.WireFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal);
            Assert.That(until, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(5)).And.LessThan(DateTimeOffset.UtcNow.AddMinutes(7)),
                "held for the pairing budget the request arrived in");
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.EqualTo(1));
            Assert.That(_target.Admitted, Is.Null);
        }

        [Test]
        public void The_same_request_asked_again_is_written_without_the_dialog()
        {
            var approval = Approving("ops");
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            Assume.That(Request("agent-fictitious", new[] { "ops" }, approval).Status, Is.EqualTo(503));

            _target.AdmitAnswers = null;
            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.Asked, Is.EqualTo(1), "the operator answered once");
            Assert.That(_held.Count, Is.Zero, "a landed write uses the approval up");
            Assert.That(_book.Outstanding(), Is.Empty, "and spends the invitation");
            Assert.That(_audit.Entries, Has.Count.EqualTo(1), "the approval is written down when it lands");
            Assert.That(_log.Entries.Any(line => line.Message.Contains("approved earlier and held")), Is.True,
                "and the log says it waited");
        }

        [Test]
        public void A_retry_that_asks_for_anything_else_is_put_to_the_operator_again()
        {
            var approval = Approving();
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            Assume.That(Request("agent-fictitious", Array.Empty<string>(), approval).Status, Is.EqualTo(503));

            _target.AdmitAnswers = null;
            Hello(ClientProof());
            var context = Request("agent-fictitious", Array.Empty<string>(), "P30D", approval);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.Asked, Is.EqualTo(2));
        }

        [Test]
        public void A_lock_lets_go_of_a_held_approval_so_the_retry_asks_again()
        {
            var approval = Approving();
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            Assume.That(Request("agent-fictitious", Array.Empty<string>(), approval).Status, Is.EqualTo(503));

            ((IApprovalScreen)_held).Locked();

            _target.AdmitAnswers = null;
            Hello(ClientProof());
            Assert.That(Request("agent-fictitious", Array.Empty<string>(), approval).Status, Is.EqualTo(200));
            Assert.That(approval.Asked, Is.EqualTo(2));
        }

        [Test]
        public void A_lock_between_the_operators_answer_and_its_hold_lets_the_answer_go()
        {
            // S083. The dialog had answered and KeePass locked before the answer was held, so the
            // book the lock had emptied took it all the same: the client was told `503 busy` until
            // when, and the same request after a quick unlock was written without the dialog.
            var locked = false;
            var approval = new FakePairingApproval(request =>
            {
                if (!locked)
                {
                    locked = true;
                    ((IApprovalScreen)_held).Locked();
                    _target.AdmissionBlocked = "the database is not open";
                }

                return PairingApproval.Approved(Array.Empty<string>());
            });
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_held.Count, Is.Zero, "nothing is held across the lock");
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "and nothing is spent");
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("KeePass locked or closed the database")), Is.True);

            _target.AdmissionBlocked = null;
            Hello(ClientProof());
            var retry = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(retry.Status, Is.EqualTo(200), retry.Json);
            Assert.That(approval.Asked, Is.EqualTo(2), "the operator is asked again after the unlock");
        }

        [Test]
        public void An_approval_given_after_its_database_closed_is_not_written_into_it_once_reopened()
        {
            // D005 and D017. Nothing takes the dialog down when its database closes for real, and
            // an Approve pressed after the close was held in the book the close had emptied, for a
            // retry of the same request on the database opened again under the same authority.
            var closed = false;
            var approval = new FakePairingApproval(request =>
            {
                if (!closed)
                {
                    closed = true;
                    ((IApprovalScreen)_held).Closed(new[] { _target.DatabaseId });
                    _target.AdmissionBlocked = "the database is not open";
                }

                return PairingApproval.Approved(Array.Empty<string>());
            });
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_held.Count, Is.Zero);

            _target.AdmissionBlocked = null;
            Hello(ClientProof());
            var retry = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(retry.Status, Is.EqualTo(200), retry.Json);
            Assert.That(approval.Asked, Is.EqualTo(2), "the reopened database's operator is asked again");
        }

        [Test]
        public void A_held_approval_another_request_wrote_first_is_told_the_invitation_is_gone()
        {
            // D006. Two exchanges on one invitation can both find the same held approval, and
            // the write that lands first takes it and spends the invitation. The other was logged
            // as held until its time ran out and told `503 busy`, inviting a retry on an
            // invitation that was already gone.
            var approval = Approving();
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            Assume.That(Request("agent-fictitious", Array.Empty<string>(), approval).Status, Is.EqualTo(503));

            var held = _held.Find(
                ApprovalBook.Pairing(_invitation.Id, "agent-fictitious", Array.Empty<string>(), null), _target.DatabaseId);
            Assume.That(held, Is.Not.Null);
            _target.AdmitAnswers = null;
            _target.BeforeWrite = () =>
            {
                // The other exchange's write, landing first.
                _held.Take(held);
                _book.Burn(_invitation.Id);
            };

            Hello(ClientProof());
            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(409), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("no invitation")));
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("another request")), Is.True);
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("its time ran out")), Is.False);
            Assert.That(approval.Asked, Is.EqualTo(1));
        }

        [Test]
        public void A_held_approval_another_request_used_up_without_spending_anything_asks_again()
        {
            // The other exchange took the approval and wrote nothing, as a write refused because a
            // profile left the list does: the invitation is still good, so the answer is the
            // `503 busy` that puts the request to the operator again.
            var approval = Approving();
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            Assume.That(Request("agent-fictitious", Array.Empty<string>(), approval).Status, Is.EqualTo(503));

            var held = _held.Find(
                ApprovalBook.Pairing(_invitation.Id, "agent-fictitious", Array.Empty<string>(), null), _target.DatabaseId);
            Assume.That(held, Is.Not.Null);
            _target.AdmitAnswers = null;
            _target.BeforeWrite = () => _held.Take(held);

            Hello(ClientProof());
            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("another request")), Is.True);
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("its time ran out")), Is.False);
        }

        [Test]
        public void A_close_of_another_database_between_the_question_and_the_answer_keeps_the_answer()
        {
            // The operator's ruling R1: a close lets go of its own database's approvals and of no
            // other's, and an answer that arrives after it is no different.
            var approval = new FakePairingApproval(request =>
            {
                ((IApprovalScreen)_held).Closed(new[] { "another database" });
                return PairingApproval.Approved(Array.Empty<string>());
            });
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(JsonBody.Read(context.Json).Text("heldUntil"), Is.Not.Null, context.Json);
            Assert.That(_held.Count, Is.EqualTo(1));
        }

        [Test]
        public void A_profile_taken_off_the_list_under_the_write_voids_the_approval_and_spends_nothing()
        {
            // Compared under the writer's lock: the write would put the name back on the list,
            // with every rule naming it, after the operator took it off. Nothing is written or
            // spent, and the same request puts the dialog to the operator again over the name as
            // it stands, answered once its approval is written.
            _target.KnownProfiles = new[] { "ops" };
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                _target.Forget("ops");
            };
            var approval = Agreeing();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            AskedAgainInTheSameRequest(context, approval);
        }

        [Test]
        public void A_profile_put_on_the_list_under_the_write_voids_the_approval_and_the_dialog_comes_back()
        {
            // Shown as new, and on the list by the write, as another pairing or the Profiles page
            // put it there: not the name the operator was shown. The dialog shown again shows it
            // on the list.
            var approval = Agreeing();
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                _target.KnownProfiles = new[] { "ops" };
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(PairingApprovalForm.Row(approval.Requests[0], "ops"), Is.EqualTo("ops" + PairingApprovalForm.NewSuffix));
            Assert.That(_atEachDialog[1], Is.EqualTo(NothingWritten), "nothing written or spent before the second Approve");
            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Row(approval.Requests[1], "ops"), Is.EqualTo("ops"));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
        }

        [Test]
        public void A_held_approval_of_a_name_off_the_list_is_stale_once_a_removal_the_dialog_did_not_hold_arrives(
            [Values(NeverNamed, RemovedHere)] string before,
            [Values(AsRecordAndItem, AsItemAlone, AsDeletionRecordAlone)] string how)
        {
            // The operator approved `ops` off the list and the write was answered busy, the
            // approval held. A synchronize brings a removal of `ops` made in another copy: the
            // retry writes nothing, as an Add on the Profiles page that a removal overtook writes
            // nothing, `STORE.md` §627, and the retry puts the dialog to the operator again over
            // the name as it stands.
            OffTheList(before);
            var approval = Agreeing();
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            Assert.That(Request("agent-fictitious", new[] { "ops" }, approval).Status, Is.EqualTo(503));
            Assert.That(_held.Count, Is.EqualTo(1), "the approval is held");
            _target.AdmitAnswers = null;
            RemovedElsewhere(how);

            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            AskedAgainInTheSameRequest(context, approval);
        }

        [Test]
        public void An_approval_of_a_name_off_the_list_is_stale_once_a_removal_arrives_while_the_dialog_stands(
            [Values(NeverNamed, RemovedHere)] string before,
            [Values(AsRecordAndItem, AsItemAlone, AsDeletionRecordAlone)] string how)
        {
            // The dialog is built from a reading made before it is shown, and a synchronize lands
            // while the operator reads it. The operator unchecks `deploy` and picks thirty days:
            // the dialog shown again opens with that answer, `deploy` unchecked and thirty days
            // chosen, and `ops` checked only where it was marked removed before as well; a name
            // shown as new before opens unchecked, and the operator checks it again. Its Approve
            // writes what the operator approved.
            OffTheList(before);
            var dialogs = 0;
            var approval = Answering(request =>
            {
                if (++dialogs == 1)
                {
                    RemovedElsewhere(how);
                    return PairingApproval.Approved(new[] { "ops" }, TimeSpan.FromDays(30));
                }

                return PairingApproval.Approved(new[] { "ops" }, request.Picked);
            });
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "deploy" }, approval);

            AskedAgainInTheSameRequest(context, approval);
            Assert.That(approval.Requests[1].Checks("ops"), Is.EqualTo(before == RemovedHere), "checked only where its mark is the same");
            Assert.That(approval.Requests[1].Checks("deploy"), Is.False, "unchecked on the dialog before");
            Assert.That(approval.Requests[1].Picked, Is.EqualTo(TimeSpan.FromDays(30)));
            Assert.That(_target.Admitted.Profiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_removal_only_a_deletion_record_tells_of_before_the_first_dialog_is_put_back_at_the_second_approval(
            [Values(false, true)] bool heldFirst)
        {
            // `ops` named nowhere in the file, and its removal a deletion record alone before the
            // first dialog: the dialog's reading cannot tie that deletion record to the name, so the
            // dialog shows `ops` as new. The write counts with the store key, finds the removal the
            // dialog did not show, and writes nothing, an approval held first as well. The request
            // puts the dialog to the operator again, marking `ops` removed and holding that
            // removal, and its approval puts the name back over it.
            RemovedElsewhere(AsDeletionRecordAlone);
            var approval = Agreeing();
            if (heldFirst)
            {
                _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
                Hello(ClientProof());
                Assert.That(Request("agent-fictitious", new[] { "ops" }, approval).Status, Is.EqualTo(503));
                Assert.That(_held.Count, Is.EqualTo(1), "the approval is held");
                _target.AdmitAnswers = null;
            }

            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(PairingApprovalForm.Row(approval.Requests[0], "ops"), Is.EqualTo("ops" + PairingApprovalForm.NewSuffix));
            AskedAgainInTheSameRequest(context, approval);
        }

        [Test]
        public void A_removal_the_dialog_held_lets_the_approval_of_its_name_through(
            [Values(AsRecordAndItem, AsItemAlone)] string how,
            [Values(false, true)] bool heldFirst)
        {
            // A removal in the database before the dialog was shown, which its reading tells by
            // the removal's record or an item, is a removal the operator saw, since the dialog
            // shows the name off the list: approving it puts the name back.
            RemovedElsewhere(how);
            var approval = Approving("ops");
            if (heldFirst)
            {
                _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
                Hello(ClientProof());
                Assert.That(Request("agent-fictitious", new[] { "ops" }, approval).Status, Is.EqualTo(503));
                _target.AdmitAnswers = null;
            }

            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.Asked, Is.EqualTo(1));
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void An_approval_of_a_name_on_the_list_is_written_whatever_removal_arrives_while_it_stays_on_the_list()
        {
            // A name on the list as the dialog showed it is held to being there at the write, and
            // to nothing else: a removal of it that another copy made and added the name back
            // over, brought in by a synchronize while the dialog stands, leaves it on the list and
            // is not compared, and a removal of another name changes nothing the operator decided.
            _target.KnownProfiles = new[] { "ops" };
            var approval = Approving("ops");
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                var copy = _target.Store.Copy();
                StoreSeeds.Unname(copy, "ops");
                StoreSeeds.Name(copy, "ops");
                _target.Store.Synchronize(copy);
                StoreRecords.RemovedApart(_target.Store, "other");
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Has.Count.EqualTo(1), "a removal the dialog did not hold");
            }

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.Asked, Is.EqualTo(1));
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_name_on_the_list_whose_addition_names_earlier_removals_is_written_at_the_first_approval()
        {
            // `ops` removed and added back before the dialog, so the addition that puts it on the
            // list names that removal: the dialog shows it on the list, its approval is held to
            // that place alone, and the removal is not compared.
            _target.KnownProfiles = new[] { "ops" };
            _target.Forget("ops");
            _target.KnownProfiles = new[] { "ops" };
            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Has.Count.EqualTo(1));
            }

            var approval = Agreeing();
            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(PairingApprovalForm.Row(approval.LastRequest, "ops"), Is.EqualTo("ops"));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.Asked, Is.EqualTo(1));
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void The_dialog_after_an_approval_a_removal_it_did_not_show_made_stale_marks_the_name_removed()
        {
            // The first dialog shows `ops` as new. A removal of it arrives as a deletion record
            // alone, which a reading taken without the store key cannot tie to a name, and the
            // write, which counts with the key, finds it: the approval is stale. The dialog the
            // same request puts to the operator again marks `ops` as removed, with the line about
            // every rule still naming it, as a removed name is shown, and approving that puts the
            // name back.
            var approval = Agreeing();
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            var first = approval.Requests[0];
            Assert.That(PairingApprovalForm.Row(first, "ops"), Is.EqualTo("ops" + PairingApprovalForm.NewSuffix));
            Assert.That(PairingApprovalForm.Regaining(first), Is.Null);
            Assert.That(PairingApprovalForm.Again(first), Is.Null);
            var shown = approval.Requests[1];
            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(PairingApprovalForm.Again(shown), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(shown.IsNew("ops"), Is.False);
            Assert.That(PairingApprovalForm.Asking(shown), Is.EqualTo(PairingApprovalForm.ProfilesAsked));
            Assert.That(PairingApprovalForm.Regaining(shown), Is.EqualTo(AddsBack));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void The_mark_a_stale_approval_leaves_names_every_client_that_regains_the_name_as_a_removed_name_does()
        {
            // A client still holds `ops`, so the first dialog shows it as removed already, naming
            // that client; the dialog shown again shows it the same way.
            using (var holder = _target.IssueClient("agent-holding", "ops"))
            {
                _target.Admit(holder, "ops");
                _target.Forget("ops");
                var approval = Agreeing();
                ARemovalOfOpsUnderTheFirstWrite();
                Hello(ClientProof());

                var context = Request("agent-fictitious", new[] { "ops" }, approval);

                var first = PairingApprovalForm.Regaining(approval.Requests[0]);
                Assert.That(first, Does.Contain("agent-holding"));
                Assert.That(approval.Asked, Is.EqualTo(2));
                Assert.That(PairingApprovalForm.Row(approval.Requests[1], "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(PairingApprovalForm.Regaining(approval.Requests[1]), Is.EqualTo(first));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
            }
        }

        [Test]
        public void Nothing_of_a_request_s_dialogs_reaches_the_next_request()
        {
            // What a write found and what the operator answered belong to the request. A request
            // whose dialog shown again is refused leaves nothing behind: the request on the code
            // handed out next starts from its own reading, its first dialog carries no notice and
            // shows `ops` as that reading shows it, new, and its own write finds the removal.
            var dialogs = 0;
            var approval = Answering(request => ++dialogs == 2
                ? PairingApproval.Refused()
                : PairingApproval.Approved(request.Profiles));
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var refused = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(refused.Status, Is.EqualTo(403), refused.Json);
            AnotherInvitation();
            Hello(ClientProof());

            var context = Request("agent-other", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(4));
            Assert.That(PairingApprovalForm.Again(approval.Requests[2]), Is.Null, "the first dialog of a request carries no notice");
            Assert.That(PairingApprovalForm.Row(approval.Requests[2], "ops"), Is.EqualTo("ops" + PairingApprovalForm.NewSuffix));
            Assert.That(PairingApprovalForm.Again(approval.Requests[3]), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Row(approval.Requests[3], "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_list_change_and_a_removal_found_by_the_same_write_put_the_dialog_up_again_once()
        {
            // Every cause in a single pass: `reader` taken off the list and a removal of `ops` that
            // only a deletion record tells of, both under the same write. The log names both in a
            // single line, the dialog shown again marks both names removed, and its approval is
            // written.
            _target.KnownProfiles = new[] { "reader" };
            var approval = Agreeing();
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                _target.Forget("reader");
                StoreRecords.RemovedApart(_target.Store, "ops");
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "reader" }, approval);

            Assert.That(
                _log.Warnings.Single(entry => entry.Message.Contains(StaleLine)).Message,
                Does.Contain("the profile reader is off the list").And.Contain("a removal of the profile ops"),
                "a single line names both causes");
            var shown = approval.Requests[1];
            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(_atEachDialog[1], Is.EqualTo(NothingWritten));
            Assert.That(PairingApprovalForm.Again(shown), Is.EqualTo(StateChanged));
            Assert.That(
                shown.Profiles.Select(profile => PairingApprovalForm.Row(shown, profile)),
                Is.EqualTo(new[] { "ops" + PairingApprovalForm.RemovedSuffix, "reader" + PairingApprovalForm.RemovedSuffix }));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops", "reader" }));
        }

        [Test]
        public void A_name_taken_off_the_list_by_a_removal_the_next_reading_cannot_tie_puts_the_dialog_up_again_once()
        {
            // `ops` on the list as the dialog showed it. Under the write a synchronize brings in a
            // copy that removed it, deleted its addition, then deleted the removal for good and
            // lost the summary entry restating it: `ops` is off the list, and nothing in the store
            // names it but a deletion record, which the next reading cannot tie to the name. The
            // write finds that removal with the move, so the dialog shown again marks `ops`
            // removed and holds the removal, and its approval is written.
            _target.KnownProfiles = new[] { "ops" };
            var approval = Agreeing();
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                TakenOffApartLeavingADeletionRecord("ops");
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(_atEachDialog[1], Is.EqualTo(NothingWritten));
            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Row(approval.Requests[1], "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(_log.Warnings.Count(entry => entry.Message.Contains(StaleLine)), Is.EqualTo(1));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops", "reader" }));
        }

        [Test]
        public void A_further_removal_while_the_dialog_shown_again_stands_puts_the_dialog_up_a_third_time()
        {
            // A removal of `ops` under the first write, and a further removal of `ops` arriving while
            // the dialog shown again stands, each a deletion record alone, which no reading ties to
            // the name. Each write finds the removal its dialog did not hold, and the third dialog
            // holds both as the two writes found them, so that its approval is written over both.
            // The log names each write's cause in a line of its own. From a fifth dialog on the
            // operator refuses, so that a request whose dialogs hold fewer than both still ends.
            var dialogs = 0;
            var approval = Answering(request =>
            {
                if (++dialogs == 2)
                {
                    RemovedElsewhere(AsDeletionRecordAlone);
                }

                return dialogs < 5 ? PairingApproval.Approved(request.Profiles) : PairingApproval.Refused();
            });
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(3));
            Assert.That(PairingApprovalForm.Again(approval.Requests[0]), Is.Null);
            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Again(approval.Requests[2]), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Row(approval.Requests[2], "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(_atEachDialog[2], Is.EqualTo(NothingWritten), "nothing written or spent before the third Approve");
            Assert.That(_log.Warnings.Count(entry => entry.Message.Contains(StaleLine)), Is.EqualTo(2));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void Refusing_the_dialog_shown_again_is_answered_as_a_refusal_and_spends_the_invitation()
        {
            // Answered as a refusal on a first dialog is: `403 refused`, the invitation spent, the
            // refusal written down by its invitation, and nothing written.
            var dialogs = 0;
            var approval = Answering(request => ++dialogs == 1
                ? PairingApproval.Approved(request.Profiles)
                : PairingApproval.Refused());
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(context.Status, Is.EqualTo(403), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("refused")));
            Assert.That(_book.Outstanding(), Is.Empty, "a refusal is a decision, and it spends the invitation");
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_target.KnownProfiles, Is.Empty);
            Assert.That(_held.Count, Is.Zero);
            Assert.That(_audit.Entries.Single().Decision, Is.EqualTo(AuditEntry.Refused));
            Assert.That(_audit.Entries.Single().Invitation, Is.EqualTo(_invitation.Id));
        }

        [Test]
        public void Nobody_answering_the_dialog_shown_again_spends_nothing()
        {
            // Answered as a first dialog nobody answers is: `504 unanswered`, nothing written or
            // spent, and the request written down as unanswered.
            var dialogs = 0;
            var approval = Answering(request => ++dialogs == 1
                ? PairingApproval.Approved(request.Profiles)
                : PairingApproval.TimedOut());
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(context.Status, Is.EqualTo(504), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unanswered")));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_held.Count, Is.Zero);
            Assert.That(_audit.Entries.Single().Decision, Is.EqualTo(AuditEntry.Unanswered));
        }

        [Test]
        public void The_dialog_shown_again_is_given_what_is_left_of_the_pairing_budget()
        {
            // A further dialog stays inside the budget its request arrived in: it is given what is
            // left of that budget once the waits after the dialog are set aside, as a dialog that
            // comes up late is, and never more than the operator's own time.
            var now = DateTimeOffset.UtcNow;
            _clock = () => now;
            _options = new PairingOptions();
            var thought = TimeSpan.FromSeconds(100);
            var approval = Answering(request =>
            {
                now += thought;
                return PairingApproval.Approved(request.Profiles);
            });
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            var budget = _options.ApprovalHeld - _options.AfterTheDialog;
            Assert.That(approval.Lefts, Is.EqualTo(new[] { budget, budget - thought }));
            Assert.That(approval.LastTimeout, Is.EqualTo(_options.ApprovalTimeout));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
        }

        [Test]
        public void A_dialog_shown_again_with_too_little_of_the_budget_left_is_not_shown()
        {
            // The screen puts up no dialog that cannot be read, `PairingApprovalPrompt.Shortest`.
            // Where the first round used so much of the budget that too little is left for a
            // dialog, the request is answered as a request too late for the screen is, `503 busy`,
            // with nothing written or spent.
            var now = DateTimeOffset.UtcNow;
            _clock = () => now;
            _options = new PairingOptions();
            var shown = 0;
            var prompt = new KeePassGate.Ui.PairingApprovalPrompt(new FakeUiThread(), _log)
            {
                Dialog = (request, allowed) =>
                {
                    shown++;
                    now += _options.ApprovalHeld - _options.AfterTheDialog - KeePassGate.Ui.PairingApprovalPrompt.Shortest
                           + TimeSpan.FromSeconds(1);
                    return PairingApproval.Approved(request.Profiles);
                }
            };
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, prompt);

            Assert.That(shown, Is.EqualTo(1));
            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_log.Warnings.Any(entry => entry.Message.Contains("too little of its time was left")), Is.True);
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_held.Count, Is.Zero);
        }

        [Test]
        public void The_screen_stays_with_the_request_until_the_request_is_answered()
        {
            // A single request is put to the operator at a time, and a request whose write found its
            // approval stale keeps the screen through that write and into its next dialog, so that
            // no other request's dialog comes up between them: a request asking meanwhile waits
            // within its own wait for the screen, and here gives up. Once the request is answered
            // the screen is free.
            var prompt = new KeePassGate.Ui.PairingApprovalPrompt(new FakeUiThread(), _log)
            {
                Dialog = (request, allowed) => PairingApproval.Approved(request.Profiles)
            };
            PairingApproval meanwhile = null;
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                meanwhile = OnAnotherThread(() => prompt.Ask(AnotherRequest(), TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(10)));
                StoreRecords.RemovedApart(_target.Store, "ops");
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, prompt);

            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(meanwhile.Decision, Is.EqualTo(PairingDecision.Unavailable), "no other dialog while the request writes");
            Assert.That(
                OnAnotherThread(() => prompt.Ask(AnotherRequest(), TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(10))).Decision,
                Is.EqualTo(PairingDecision.Approved),
                "the screen is free once the request is answered");
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        public void A_held_approval_found_stale_at_its_retry_opens_the_dialog_as_the_operator_approved_it()
        {
            // The client asks for `reader`, `admin` and `ops`; the operator unchecks `admin`, picks
            // seven days and approves, KeePass is busy and the approval is held. A removal of `ops`
            // that the dialog did not show arrives before the retry: the retry finds the approval
            // stale at its write and puts the real dialog to the operator with its notice, `reader`
            // checked, `admin` unchecked, `ops` unchecked as a row whose mark changed, and seven
            // days chosen, the single case in which a second request carries the notice.
            _target.KnownProfiles = new[] { "reader" };
            var opened = new List<(IReadOnlyList<string> Checked, TimeSpan Lifetime)>();
            var approval = Answering(request =>
            {
                opened.Add(Opened(request));
                return opened.Count == 1
                    ? PairingApproval.Approved(new[] { "reader", "ops" }, TimeSpan.FromDays(7))
                    : PairingApproval.Approved(opened[1].Checked, opened[1].Lifetime);
            });
            _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
            Hello(ClientProof());
            var busy = Request("agent-fictitious", new[] { "reader", "admin", "ops" }, approval);
            Assert.That(JsonBody.Read(busy.Json).Text("heldUntil"), Is.Not.Null, busy.Json);
            _target.AdmitAnswers = null;
            RemovedElsewhere(AsDeletionRecordAlone);

            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "reader", "admin", "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(PairingApprovalForm.Again(approval.Requests[0]), Is.Null);
            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            Assert.That(opened[1].Checked, Is.EqualTo(new[] { "reader" }));
            Assert.That(opened[1].Lifetime, Is.EqualTo(TimeSpan.FromDays(7)));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.Admitted.Profiles, Is.EqualTo(new[] { "reader" }));
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        public void The_dialog_shown_again_opens_with_what_the_operator_approved_before()
        {
            // The client asks for `reader`, `admin` and `ops`; the operator unchecks `admin`, picks
            // seven days and approves, and the write finds a removal of `ops` that the dialog did
            // not show. The dialog shown again opens with `reader` checked, `admin` unchecked as
            // the operator left it, `ops` unchecked as a row whose mark changed, and seven days
            // chosen, so that a single Approve on it grants no more than the operator approved.
            _target.KnownProfiles = new[] { "reader" };
            var opened = new List<(IReadOnlyList<string> Checked, TimeSpan Lifetime)>();
            var approval = Answering(request =>
            {
                opened.Add(Opened(request));
                return opened.Count == 1
                    ? PairingApproval.Approved(new[] { "reader", "ops" }, TimeSpan.FromDays(7))
                    : PairingApproval.Approved(opened[1].Checked, opened[1].Lifetime);
            });
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "reader", "admin", "ops" }, approval);

            Assert.That(opened[0].Checked, Is.EqualTo(new[] { "reader", "admin", "ops" }), "a first dialog checks every row");
            Assert.That(opened[1].Checked, Is.EqualTo(new[] { "reader" }));
            Assert.That(opened[1].Lifetime, Is.EqualTo(TimeSpan.FromDays(7)));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.Admitted.Profiles, Is.EqualTo(new[] { "reader" }));
        }

        [TestCase(ByARemoval)]
        [TestCase(ByAMove)]
        [TestCase(ByBoth)]
        public void The_notice_reads_the_same_whatever_the_write_found_and_the_log_names_each_cause(string how)
        {
            // The dialog shown again states only that the state changed: the rows show the state,
            // and the log names each profile and each cause
            // the write found, all of them in a single line.
            _target.KnownProfiles = new[] { "reader" };
            var approval = Agreeing();
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                if (how != ByAMove)
                {
                    StoreRecords.RemovedApart(_target.Store, "ops");
                }

                if (how != ByARemoval)
                {
                    _target.Forget("reader");
                }
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "reader" }, approval);

            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            var line = _log.Warnings.Select(entry => entry.Message).Single(message => message.Contains(StaleLine));
            Assert.That(line.Contains("a removal of the profile ops"), Is.EqualTo(how != ByAMove), line);
            Assert.That(line.Contains("the profile reader is off the list"), Is.EqualTo(how != ByARemoval), line);
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
        }

        [Test]
        public void A_name_found_removed_and_unchecked_on_the_dialog_shown_again_stays_off_the_list()
        {
            // The operator approved `ops` and `deploy`, and Approve found `ops` removed. On the
            // dialog shown again `ops` is unchecked: the approval of `deploy` alone is written,
            // and `ops` stays where its removal put it.
            var dialogs = 0;
            var approval = Answering(request => PairingApproval.Approved(
                ++dialogs == 1 ? request.Profiles : request.Profiles.Where(profile => profile != "ops").ToList()));
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "deploy" }, approval);

            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "deploy" }));
            Assert.That(_target.Admitted.Profiles, Is.EqualTo(new[] { "deploy" }));
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        public void A_row_whose_mark_changed_opens_unchecked_on_the_dialog_shown_again()
        {
            // `ops` on the list, `deploy` and `admin` new; the operator unchecks `admin` and
            // approves while a synchronize takes `ops` off the list. The dialog shown again marks
            // `ops` removed, which is not what the operator approved it as, so its row opens
            // unchecked and a single Approve on that dialog puts no name back. `deploy`, marked as
            // before, opens as the operator left it, and so does `admin`.
            _target.KnownProfiles = new[] { "ops" };
            var dialogs = 0;
            var approval = Answering(request =>
            {
                if (++dialogs == 1)
                {
                    _target.Forget("ops");
                    return PairingApproval.Approved(new[] { "ops", "deploy" });
                }

                return PairingApproval.Approved(request.Profiles.Where(request.Checks).ToList());
            });
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "deploy", "admin" }, approval);

            var shown = approval.Requests[1];
            Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(shown.Checks("ops"), Is.False, "on the list before, removed now");
            Assert.That(shown.Checks("deploy"), Is.True, "new before and new now");
            Assert.That(shown.Checks("admin"), Is.False, "unchecked before");
            Assert.That(Opened(shown).Checked, Is.EqualTo(new[] { "deploy" }), "the real form opens so");
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.Admitted.Profiles, Is.EqualTo(new[] { "deploy" }));
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "deploy" }));
        }

        [Test]
        public void A_held_approval_carries_the_removals_its_dialog_held_into_the_dialogs_of_its_retry()
        {
            // `ops` named nowhere in the file, its removal a deletion record alone, and `reader` on
            // the list. The first write finds that removal, and the dialog shown again marks `ops`
            // removed and holds it; the operator approves both names and KeePass is too busy to
            // write, so the approval is held. `reader` then leaves the list, and the retry's write
            // finds the held approval stale over `reader` alone. The retry's dialog holds what the
            // held approval's dialog held, so `ops` is marked removed again and the approval of that
            // dialog is written over that removal, with no further dialog.
            _target.KnownProfiles = new[] { "reader" };
            RemovedElsewhere(AsDeletionRecordAlone);
            var dialogs = 0;
            var approval = Answering(request =>
            {
                if (++dialogs == 2)
                {
                    _target.AdmitAnswers = AdmissionWrite.Busy("KeePass is saving");
                }

                return PairingApproval.Approved(request.Profiles);
            });
            Hello(ClientProof());
            var busy = Request("agent-fictitious", new[] { "ops", "reader" }, approval);
            Assert.That(JsonBody.Read(busy.Json).Text("heldUntil"), Is.Not.Null, busy.Json);
            _target.AdmitAnswers = null;
            _target.Forget("reader");

            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops", "reader" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(3));
            Assert.That(PairingApprovalForm.Row(approval.Requests[2], "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops", "reader" }));
        }

        [TestCase(SeveralAuthorities, 503, "undecided", "several authorities stand")]
        [TestCase(AuthorityRetired, 409, "no invitation", "no longer the authority served")]
        [TestCase(ShutUnderTheReading, 503, "locked", "the database closed")]
        public void A_later_dialog_over_a_reading_that_is_not_served_is_answered_as_its_write_would_be(
            string how, int status, string word, string why)
        {
            // The store stops serving this database's authority once the first write found the
            // approval stale, or the database closes under the next reading. A dialog over that
            // reading would show every row as new, so none is raised: the request is answered as
            // a first dialog's reading is answered over the same store, which is what its write
            // would answer, with nothing written or spent. From a third dialog on the operator
            // refuses, so that a request raising dialogs over such a reading still ends.
            var dialogs = 0;
            var approval = Answering(request => ++dialogs < 3 ? PairingApproval.Approved(request.Profiles) : PairingApproval.Refused());
            ARemovalOfOpsUnderTheFirstWrite();
            _target.AfterWrite = () =>
            {
                _target.AfterWrite = null;
                if (how == SeveralAuthorities)
                {
                    StoreSeeds.Beside(_target.Store);
                }
                else if (how == AuthorityRetired)
                {
                    StoreSeeds.Retire(_target.Store, _target.DatabaseId);
                }
                else
                {
                    _target.Shut = true;
                }
            };
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(1), "no dialog shown again");
            Assert.That(context.Status, Is.EqualTo(status), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(word)));
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.Zero);
            Assert.That(_audit.Entries, Is.Empty);
            Assert.That(
                _log.Warnings.Select(entry => entry.Message),
                Has.Some.Contains("pairing on invitation " + _invitation.Id).And.Contains("was not put to the operator again").And.Contains(why));
        }

        [Test]
        public void A_dialog_never_shown_is_answered_busy_and_written_down_as_nothing()
        {
            // KeePass's thread never took the turn the dialog was queued for, so nobody saw it:
            // answered `503 busy` with nothing spent, as a request that never got the screen is,
            // and not written down as unanswered.
            var prompt = new KeePassGate.Ui.PairingApprovalPrompt(new FakeUiThread { Runs = false }, _log);
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, prompt);

            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_audit.Entries, Is.Empty, "nobody saw it");
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
            Assert.That(_log.Warnings.Any(entry => entry.Message.Contains("was never shown")), Is.True);
        }

        [TestCase(ByALock)]
        [TestCase(ByAClose)]
        public void A_lock_or_a_close_between_two_dialogs_is_answered_locked(string how)
        {
            // Between the write that found the approval stale and the next dialog, KeePass locks or
            // closes the database, which the database answers as not open. Answered as a lock is
            // answered elsewhere in a pairing, `503 locked`, with nothing written or spent.
            _target.AfterWrite = () =>
            {
                _target.AfterWrite = null;
                LockedOrClosed(how);
                _target.AdmissionBlocked = "the database is not open";
            };
            ARemovalOfOpsUnderTheFirstWrite();
            var approval = Agreeing();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(1));
            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
        }

        [TestCase(ByALock)]
        [TestCase(ByAClose)]
        public void A_lock_or_a_close_while_a_later_write_waits_is_answered_locked(string how)
        {
            // The dialog shown again is approved, and KeePass locks or closes the database while
            // that write waits for its turn, which then finds nothing to write to: `503 locked`,
            // as for a lock that takes an approval away before its write.
            var writes = 0;
            _target.BeforeWrite = () =>
            {
                if (++writes == 1)
                {
                    StoreRecords.RemovedApart(_target.Store, "ops");
                    return;
                }

                LockedOrClosed(how);
                _target.ClosedUnderTheWrite = true;
            };
            var approval = Agreeing();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
        }

        [TestCase(ByALock)]
        [TestCase(ByAClose)]
        public void A_lock_or_a_close_that_takes_a_later_dialog_away_is_answered_locked(string how)
        {
            // The screen closes the dialog shown again as unanswered, as it closes a first dialog.
            var dialogs = 0;
            var approval = Answering(request => ++dialogs == 1
                ? PairingApproval.Approved(request.Profiles)
                : how == ByALock ? PairingApproval.Locked() : PairingApproval.Closed());
            ARemovalOfOpsUnderTheFirstWrite();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(2));
            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
        }

        [Test]
        public void A_later_dialog_is_not_raised_once_the_invitation_is_gone()
        {
            // The invitation is spent or runs out between the write that found the approval stale
            // and the next dialog: nothing could come of an Approve on that dialog, so the request
            // is answered `409 no invitation` and no dialog is raised.
            _target.AfterWrite = () =>
            {
                _target.AfterWrite = null;
                _book.Burn(_invitation.Id);
            };
            ARemovalOfOpsUnderTheFirstWrite();
            var approval = Agreeing();
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.EqualTo(1));
            Assert.That(context.Status, Is.EqualTo(409), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("no invitation")));
            Assert.That(_target.Admitted, Is.Null);
        }

        /// <summary>What the log line of an approval its write found stale holds.</summary>
        private const string StaleLine = "was approved and not written, since the database no longer holds what the dialog showed";

        private const string ByALock = "a lock";
        private const string ByAClose = "a close";

        /// <summary>KeePass locks, or closes this database, as the approvals are told of it.</summary>
        private void LockedOrClosed(string how)
        {
            if (how == ByALock)
            {
                ((IApprovalScreen)_held).Locked();
            }
            else
            {
                ((IApprovalScreen)_held).Closed(new[] { _target.DatabaseId });
            }
        }

        [TestCase(AsRecordAndItem)]
        [TestCase(AsItemAlone)]
        public void A_first_dialog_marks_a_name_another_copy_removed_as_removed_and_its_approval_is_written(string how)
        {
            // Another copy named and removed `ops`, and a synchronize brought the removal in before
            // the request; no client holds the name. The dialog's reading holds the removal, so the
            // name is marked removed rather than new, and approving it puts it back over a removal
            // the operator was shown.
            RemovedElsewhere(how);

            var approval = Agreeing();
            Hello(ClientProof());
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(PairingApprovalForm.Row(approval.LastRequest, "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(PairingApprovalForm.Regaining(approval.LastRequest), Is.EqualTo(AddsBack));
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(approval.Asked, Is.EqualTo(1));
        }

        [TestCase(AsRecordAndItem)]
        [TestCase(AsItemAlone)]
        public void A_first_dialog_marks_a_name_removed_here_that_nobody_holds_as_removed(string how)
        {
            // The dialog's reading holds a removal of `ops`, by its record or by an item of the
            // summary entry, and no client holds the name: it is marked removed, with the line
            // about every rule still naming it, as a removed name that clients hold is.
            _target.KnownProfiles = new[] { "ops" };
            _target.Forget("ops");
            if (how == AsItemAlone)
            {
                using (var reading = _target.Store.Read())
                {
                    _target.Store.Drop(reading.InForce.Records.Single(record => record.Is(KeePassGate.Store.StoreKinds.Removed)).Hex);
                }
            }

            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(KeePassGate.Store.StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.RemovalsOf("ops"), Has.Count.EqualTo(1));
            }

            var shown = FirstDialog("ops");

            Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(shown.IsNew("ops"), Is.False);
            Assert.That(PairingApprovalForm.Asking(shown), Is.EqualTo(PairingApprovalForm.ProfilesAsked));
            Assert.That(PairingApprovalForm.Regaining(shown), Is.EqualTo(AddsBack));
        }

        [Test]
        public void A_first_dialog_marks_a_name_never_named_as_new()
        {
            var shown = FirstDialog("ops");

            Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops" + PairingApprovalForm.NewSuffix));
            Assert.That(PairingApprovalForm.Asking(shown), Is.EqualTo(PairingApprovalForm.NewProfilesAsked));
            Assert.That(PairingApprovalForm.Regaining(shown), Is.Null);
        }

        [Test]
        public void A_first_dialog_marks_a_name_removed_that_a_client_holds_as_removed_naming_the_client()
        {
            using (var holder = _target.IssueClient("agent-holding", "ops"))
            {
                _target.Admit(holder, "ops");
                _target.Forget("ops");

                var shown = FirstDialog("ops");

                Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(PairingApprovalForm.Regaining(shown),
                    Is.EqualTo("One client still holds ops from before and will regain it: agent-holding. " + AddsBack));
            }
        }

        [Test]
        public void A_first_dialog_marks_each_name_by_what_the_reading_holds_of_it()
        {
            // A name on the list, a name never named and a name removed here that nobody holds,
            // side by side.
            _target.KnownProfiles = new[] { "reader", "ops" };
            _target.Forget("ops");

            var shown = FirstDialog("reader", "deploy", "ops");

            Assert.That(
                shown.Profiles.Select(profile => PairingApprovalForm.Row(shown, profile)),
                Is.EqualTo(new[] { "reader", "deploy" + PairingApprovalForm.NewSuffix, "ops" + PairingApprovalForm.RemovedSuffix }));
            Assert.That(PairingApprovalForm.Asking(shown), Is.EqualTo(PairingApprovalForm.NewProfilesAsked));
            Assert.That(PairingApprovalForm.Regaining(shown), Is.EqualTo(AddsBack));
        }

        [Test]
        public void A_first_dialog_shows_a_name_removed_and_added_back_without_a_mark()
        {
            // On the list, with a removal of it present that its addition names.
            _target.KnownProfiles = new[] { "ops" };
            _target.Forget("ops");
            _target.KnownProfiles = new[] { "ops" };
            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.InForce.RemovalsOf("ops"), Has.Count.EqualTo(1));
            }

            var shown = FirstDialog("ops");

            Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops"));
            Assert.That(PairingApprovalForm.Asking(shown), Is.EqualTo(PairingApprovalForm.ProfilesAsked));
            Assert.That(PairingApprovalForm.Regaining(shown), Is.Null);
        }

        [Test]
        public void A_first_dialog_over_a_removal_only_a_deletion_record_tells_of_marks_the_name_new()
        {
            // `ops` named nowhere in the file, and its removal known by a deletion record alone:
            // the dialog's reading takes no store key to tie that deletion record to the name.
            var removal = StoreRecords.RemovedApart(_target.Store, "ops");
            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty);
                Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Does.Contain(removal));
            }

            var shown = FirstDialog("ops");

            Assert.That(PairingApprovalForm.Row(shown, "ops"), Is.EqualTo("ops" + PairingApprovalForm.NewSuffix));
            Assert.That(PairingApprovalForm.Asking(shown), Is.EqualTo(PairingApprovalForm.NewProfilesAsked));
            Assert.That(PairingApprovalForm.Regaining(shown), Is.Null);
        }

        private const string ByARemoval = "a removal";
        private const string ByAMove = "a move on the list";
        private const string ByBoth = "both";

        /// <summary>
        /// The line under the list for a profile marked removed that no client holds.
        /// </summary>
        private static readonly string AddsBack =
            "Approving a profile marked" + PairingApprovalForm.RemovedSuffix
            + " adds it back to this database, and every rule still naming it applies again.";

        /// <summary>
        /// The request the first dialog is built from for a pairing asking for these.
        /// </summary>
        private PairingRequest FirstDialog(params string[] profiles)
        {
            var looking = new FakePairingApproval(PairingApproval.Refused());
            Hello(ClientProof());
            Request("agent-fictitious", profiles, looking);
            Assert.That(looking.Asked, Is.EqualTo(1));
            return looking.LastRequest;
        }

        [TestCase(SeveralAuthorities, 503, "undecided", "several authorities stand")]
        [TestCase(AuthorityRetired, 409, "no invitation", "no longer the authority served")]
        public void A_store_that_stopped_serving_before_the_dialog_is_answered_as_its_write_would_be(
            string how, int status, string word, string why)
        {
            // Judged served when its connection arrived, and no longer when the dialog's reading
            // is taken: a dialog from that reading would mark `ops`, on the list, as removed for
            // the client holding it. No dialog is raised, nothing is spent, and the client is told
            // what the write would tell it for the same store.
            using (var holder = _target.IssueClient("agent-holding", "ops"))
            {
                _target.Admit(holder, "ops");
            }

            Hello(ClientProof());
            if (how == SeveralAuthorities)
            {
                StoreSeeds.Beside(_target.Store);
            }
            else
            {
                StoreSeeds.Retire(_target.Store, _target.DatabaseId);
            }

            var approval = Approving("ops");
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.Zero, "no dialog");
            Assert.That(context.Status, Is.EqualTo(status), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(word)));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.Zero);
            Assert.That(_target.Admitted, Is.Null, "nothing written");
            Assert.That(
                _log.Warnings.Select(entry => entry.Message),
                Has.Some.Contains("pairing on invitation " + _invitation.Id).And.Contains("was not put to the operator").And.Contains(why));
        }

        [Test]
        public void A_database_closed_before_the_dialog_s_reading_is_answered_locked_with_no_dialog()
        {
            // As a renewal's reading and every connection to a locked database are answered: a
            // database that closed under the connection is a lock nearly always.
            Hello(ClientProof());
            _target.Shut = true;

            var approval = Approving("ops");
            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(approval.Asked, Is.Zero, "no dialog");
            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("locked")));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_target.Admitted, Is.Null, "nothing written");
            Assert.That(
                _log.Warnings.Select(entry => entry.Message),
                Has.Some.Contains("pairing on invitation " + _invitation.Id).And.Contains("the database closed"));
        }

        private const string SeveralAuthorities = "several authorities stand";
        private const string AuthorityRetired = "its authority retired";
        private const string ShutUnderTheReading = "the database shut under the reading";

        /// <summary>
        /// This name, on the list, taken off it apart: a copy of the store removes it, which
        /// deletes its addition with a deletion record, then deletes the removal for good and loses
        /// the summary entry restating it with no deletion record, while this copy names `reader`,
        /// keeping a summary entry of its own; a synchronize brings the copy in. Nothing then names
        /// the name but the deletion record of its removal.
        /// </summary>
        private void TakenOffApartLeavingADeletionRecord(string name)
        {
            var copy = _target.Store.Copy();
            StoreSeeds.Unname(copy, name);
            using (var reading = copy.Read())
            {
                var removal = reading.InForce.Records.Single(record => record.Is(KeePassGate.Store.StoreKinds.Removed)).Hex;
                copy.Delete(Enumerable.Range(0, removal.Length / 2).Select(at => Convert.ToByte(removal.Substring(at * 2, 2), 16)).ToArray());
                copy.Drop(reading.InForce.Heads.Single().Hex);
            }

            StoreSeeds.Name(_target.Store, "reader");
            _target.Store.Synchronize(copy);
            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(KeePassGate.Store.StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.RemovalsOf(name), Is.Empty, "a name the store does not know");
                Assert.That(reading.InForce.RemovalsOnceKnown(name), Has.Count.EqualTo(1));
            }

            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "reader" }));
        }

        /// <summary>An operator who approves exactly what each dialog asks for.</summary>
        private FakePairingApproval Agreeing()
        {
            return Answering(request => PairingApproval.Approved(request.Profiles));
        }

        /// <summary>
        /// An operator who answers each dialog as this decides, noting first what stood as the
        /// dialog came up, <see cref="Standing"/>, in <see cref="_atEachDialog"/>.
        /// </summary>
        private FakePairingApproval Answering(Func<PairingRequest, PairingApproval> answer)
        {
            return new FakePairingApproval(request =>
            {
                _atEachDialog.Add(Standing());
                return answer(request);
            });
        }

        /// <summary>
        /// Whether an admission was written and how many invitations are outstanding, as a line.
        /// </summary>
        private string Standing()
        {
            return (_target.Admitted == null ? "nothing written" : "written") + ", "
                   + _book.Outstanding().Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " outstanding";
        }

        /// <summary>What stands as a dialog comes up before anything is written or spent.</summary>
        private const string NothingWritten = "nothing written, 1 outstanding";

        /// <summary>What the screen answers, asked on a thread of its own.</summary>
        private static PairingApproval OnAnotherThread(Func<PairingApproval> ask)
        {
            PairingApproval answer = null;
            var thread = new System.Threading.Thread(() => answer = ask()) { IsBackground = true };
            thread.Start();
            Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True, "the screen answered");
            return answer;
        }

        /// <summary>A pairing request of another client, on another invitation.</summary>
        private PairingRequest AnotherRequest()
        {
            return new PairingRequest(_target.DatabaseId, "another invitation", "agent-other", new[] { "ops" }, "127.0.0.1:60002");
        }

        /// <summary>
        /// A new code for this database, which replaces the code before it, and a connection that
        /// arrived on it.
        /// </summary>
        private void AnotherInvitation()
        {
            _invitation = _book.Invite(_target.DatabaseId);
            _token = _invitation.Token;
            _connection = new PairingConnection(_invitation, _target, _binding, _nonce, "127.0.0.1:60002");
        }

        /// <summary>
        /// A removal of `ops` arriving as a deletion record alone under the first write, which no
        /// reading ties to the name and the write finds: an approval of `ops` off the list is stale
        /// there.
        /// </summary>
        private void ARemovalOfOpsUnderTheFirstWrite()
        {
            _target.BeforeWrite = () =>
            {
                _target.BeforeWrite = null;
                StoreRecords.RemovedApart(_target.Store, "ops");
            };
        }

        private const string NeverNamed = "never named";
        private const string RemovedHere = "removed here";
        private const string AsRecordAndItem = "as a record and its item";
        private const string AsItemAlone = "as an item alone";
        private const string AsDeletionRecordAlone = "as a deletion record alone";

        /// <summary>`ops` off the list at this desk: never named, or named and removed here.</summary>
        private void OffTheList(string before)
        {
            if (before == RemovedHere)
            {
                _target.KnownProfiles = new[] { "ops" };
                _target.Forget("ops");
            }

            Assert.That(_target.KnownProfiles, Is.Empty);
        }

        /// <summary>
        /// A removal of `ops` made in another copy and brought in by a synchronize: its record and
        /// the item of the summary entry its write made, that item alone where the record was
        /// deleted with no deletion record, or a deletion record alone.
        /// </summary>
        private void RemovedElsewhere(string how)
        {
            HashSet<string> before;
            using (var reading = _target.Store.Read())
            {
                before = new HashSet<string>(reading.InForce.RemovalsOnceKnown("ops"), StringComparer.Ordinal);
            }

            if (how == AsDeletionRecordAlone)
            {
                StoreRecords.RemovedApart(_target.Store, "ops");
            }
            else
            {
                var copy = _target.Store.Copy();
                StoreSeeds.Name(copy, "ops");
                StoreSeeds.Unname(copy, "ops");
                _target.Store.Synchronize(copy);
                if (how == AsItemAlone)
                {
                    using (var reading = _target.Store.Read())
                    {
                        _target.Store.Drop(reading.InForce.Records
                            .Single(record => record.Is(KeePassGate.Store.StoreKinds.Removed) && !before.Contains(record.Hex)).Hex);
                    }
                }
            }

            using (var reading = _target.Store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(KeePassGate.Store.StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.RemovalsOnceKnown("ops").Count(removal => !before.Contains(removal)), Is.EqualTo(1),
                    "a single removal more");
            }

            Assert.That(_target.KnownProfiles, Does.Not.Contain("ops"));
        }

        /// <summary>
        /// The request was answered once its approval was written, after a single write found the
        /// approval stale: two dialogs in the same request, the first with no notice and the second
        /// with the notice and `ops` marked removed, nothing written or spent before the second
        /// Approve, and the approval held no longer.
        /// </summary>
        private void AskedAgainInTheSameRequest(GateContext context, FakePairingApproval approval)
        {
            Assert.That(approval.Asked, Is.EqualTo(2), "the dialog came up again, over the name as it stands");
            Assert.That(_atEachDialog.Last(), Is.EqualTo(NothingWritten), "nothing written or spent before the second Approve");
            Assert.That(PairingApprovalForm.Again(approval.Requests[0]), Is.Null, "a first dialog carries no notice");
            Assert.That(PairingApprovalForm.Again(approval.Requests[1]), Is.EqualTo(StateChanged));
            Assert.That(PairingApprovalForm.Row(approval.Requests[1], "ops"), Is.EqualTo("ops" + PairingApprovalForm.RemovedSuffix));
            Assert.That(
                _log.Warnings.Count(entry => entry.Message.Contains(StaleLine)), Is.EqualTo(1),
                "not stale again over the removals it was shown");
            Assert.That(context.Status, Is.EqualTo(200), context.Json);
            Assert.That(_target.KnownProfiles, Is.EqualTo(new[] { "ops" }));
            Assert.That(_book.Outstanding(), Is.Empty, "spent by the write that landed");
            Assert.That(_held.Count, Is.Zero);
        }

        /// <summary>
        /// The notice of a dialog shown again, the same whatever Approve found, naming no profile.
        /// </summary>
        private const string StateChanged = "Shown again: the state of the profiles changed.";

        /// <summary>
        /// What the real dialog opens with for this request: the profiles checked, in the order of
        /// the rows, and the lifetime chosen.
        /// </summary>
        private static (IReadOnlyList<string> Checked, TimeSpan Lifetime) Opened(PairingRequest request)
        {
            using (var dialog = new PairingApprovalForm(request, TimeSpan.FromMinutes(2)))
            {
                var list = KeePassGate.Ui.ControlTree.FindByName<System.Windows.Forms.CheckedListBox>(
                    dialog, KeePassGate.Ui.GateTab.ApprovalProfilesListName);
                var lifetime = KeePassGate.Ui.ControlTree.FindByName<System.Windows.Forms.ComboBox>(
                    dialog, KeePassGate.Ui.GateTab.ApprovalLifetimeName);
                var checkedRows = list.CheckedItems.Cast<object>().Select(row => PairingApprovalForm.Asked(row.ToString())).ToList();
                return (checkedRows, KeePassGate.Ui.LifetimeChoice.Offered(request.Lifetime)[lifetime.SelectedIndex]);
            }
        }

        [TestCase("split")]
        [TestCase("outdated")]
        [TestCase("stopped")]
        [TestCase("lost")]
        public void A_store_holding_the_gate_back_at_the_write_is_answered_its_word_and_lets_the_approval_go(string word)
        {
            // Asked again in the write's own turn, `STORE.md` §627: answered as a connection is,
            // with the approval let go and the invitation unspent. Each word ends by an act of the
            // operator's, after which the operator is asked again.
            _target.AdmitAnswers = AdmissionWrite.Unavailable(word);
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(word)));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.Zero, "the approval is let go");
            Assert.That(
                _log.Warnings.Select(entry => entry.Message),
                Has.Some.Contains("the plugin's store holds the gate back").And.Contains(word));
        }

        [Test]
        public void Several_authorities_standing_at_the_write_are_answered_undecided_and_let_the_approval_go()
        {
            // Several authorities serve nothing until the operator keeps an authority, and the
            // pairing is answered as a connection to the database is, with the invitation good.
            _target.AdmitAnswers = AdmissionWrite.Unavailable("undecided");
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(503), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("undecided")));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.Zero, "the approval is let go");
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("several authorities stand"));
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.None.Contains("holds the gate back"));
        }

        [Test]
        public void An_authority_recreated_under_the_write_admits_nobody()
        {
            // The write's plan leaves the admission out before anything is asked or spent, and the
            // answer is the answer for the authority the invitation was made under going: no retry
            // under it can be written, so a `503` would invite a retry that cannot work.
            _target.BeforeWrite = () => _target.Retired = true;
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(409), context.Json);
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("no invitation")));
            Assert.That(_target.Admitted, Is.Null);
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.Zero, "the approval is let go");
        }

        [Test]
        public void A_profile_asked_for_twice_reaches_the_operator_once()
        {
            // Two identical rows in the dialog is a profile the operator has to clear twice to
            // refuse once, and clearing one of them leaves the other checked: the profile they
            // meant to deny is granted anyway. The proof is computed over the list exactly as it
            // arrived, so the collapsing happens after the request has proved itself.
            var approval = FakePairingApproval.Approving("ops");
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops", "ops", "audit" }, approval);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(approval.LastRequest.Profiles, Is.EqualTo(new[] { "ops", "audit" }));
        }

        [Test]
        public void A_profile_approved_twice_reaches_the_certificate_once()
        {
            var approval = FakePairingApproval.Approving("ops", "ops");
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(_target.Admitted.Profiles, Is.EqualTo(new[] { "ops" }),
                "a repeated profile would reach the certificate as a repeated claim");
        }

        [Test]
        public void Approving_a_removed_profile_names_the_old_clients_and_gives_it_back_to_them()
        {
            // Pairing writes the approved names onto the list, so a removed name approved again is
            // back for every client whose record still holds it. The operator is shown who.
            using (var old = _target.IssueClient("ci-runner", "ops"))
            {
                _target.Admit(old, "ops");
                _target.Forget("ops");
                Assume.That(ClientCredentials.For(old, _target, _log).Profiles, Is.Empty);

                var approval = FakePairingApproval.Approving("ops");
                Hello(ClientProof());

                var context = Request("agent-fictitious", new[] { "ops" }, approval);

                Assert.That(context.Status, Is.EqualTo(200));
                Assert.That(approval.LastRequest.Returning("ops").Select(record => record.Subject),
                    Is.EqualTo(new[] { "CN=ci-runner" }));
                Assert.That(approval.LastRequest.IsNew("ops"), Is.False);
                Assert.That(ClientCredentials.For(old, _target, _log).Profiles, Is.EqualTo(new[] { "ops" }),
                    "the name is back on the list, so the old client holds it again");
            }
        }

        [Test]
        public void The_operator_is_told_which_database_by_name_and_not_only_by_digest()
        {
            // Approving lets a stranger into one of possibly several open databases, and sixteen
            // hexadecimal characters is not something anybody recognizes under time pressure. The
            // dialog had a row for this and nothing ever filled it in.
            _target.DatabaseName = "Household";
            var approval = FakePairingApproval.Approving();
            Hello(ClientProof());

            Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(approval.LastRequest.DatabaseName, Is.EqualTo("Household"));
            Assert.That(approval.LastRequest.DatabaseId, Is.EqualTo(_target.DatabaseId),
                "the digest stays beside it, because two databases can carry one name");
        }

        [Test]
        public void A_write_that_throws_is_a_refusal_rather_than_a_certificate()
        {
            _target.AdmitThrows = new InvalidOperationException("the database went away");
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            // Thrown before the write spent anything, so nothing is spent and the answer is one
            // a retry can act on; one thrown after the invitation is spent is `500 not recorded`.
            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Does.Not.Contain("identity"));
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("agent fictitious; DROP")]
        [TestCase("CN=agent")]
        [TestCase("agent\nfictitious")]
        public void A_name_that_could_never_be_issued_is_refused(string name)
        {
            Hello(ClientProof());

            var context = Send(
                "POST", RequestPath, RequestBody(name, Array.Empty<string>(), null, new byte[32]),
                _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(400));
        }

        [TestCase("de fault")]
        [TestCase("default")]
        [TestCase("ops/admin")]
        public void A_profile_that_could_never_be_issued_is_refused(string profile)
        {
            Hello(ClientProof());

            var context = Send(
                "POST", RequestPath, RequestBody("agent-fictitious", new[] { profile }, null, new byte[32]),
                _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_name_is_checked_before_the_proof_is_and_neither_spends_anything()
        {
            Hello(ClientProof());

            Send(
                "POST", RequestPath, RequestBody("", Array.Empty<string>(), null, new byte[32]),
                _connection, Approving());

            Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
        }

        [Test]
        public void Pairing_with_no_profiles_at_all_is_a_pairing()
        {
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(200), context.Json);

            using (var issued = IdentityIn(context))
            {
                var identity = ClientCredentials.For(issued, _target, _log);

                Assert.That(identity.IsPaired, Is.True, "it is admitted");
                Assert.That(identity.Profiles, Is.Empty, "and it reaches nothing, which is not the same thing");
            }
        }

        [Test]
        public void The_handler_is_only_ever_given_a_context()
        {
            Assert.Throws<ArgumentNullException>(() => Handler(Approving()).Hello(null));
            Assert.Throws<ArgumentNullException>(() => Handler(Approving()).Request(null));
        }

        private PairingHandler Handler(IPairingApproval approval)
        {
            // The default issuer unless a test named one, which is how a clock that moves between
            // the check and the issuance is reached at all.
            return _issuer == null && _options == null && _clock == null
                ? new PairingHandler(_book, approval, _held, new AdmissionAudit(_audit), _log, _incidents)
                : new PairingHandler(
                    _book,
                    approval,
                    _options ?? new PairingOptions(),
                    _issuer ?? new AuthorityIssuer(),
                    _clock,
                    new AdmissionAudit(_audit),
                    _log,
                    _held,
                    _incidents);
        }

        private static FakePairingApproval Approving(params string[] profiles)
        {
            return FakePairingApproval.Approving(profiles);
        }

        private byte[] ClientProof()
        {
            return PairingProof.Client(_token, _binding, _nonce);
        }

        private GateContext Hello(byte[] proof)
        {
            return Send(
                "POST",
                HelloPath,
                Bodies.Of(("proof", Convert.ToBase64String(proof))),
                _connection,
                Approving());
        }

        /// <summary>A request that proves itself, which needs the challenge the hello came back with.</summary>
        private GateContext Request(string name, string[] profiles, IPairingApproval approval)
        {
            return Request(name, profiles, null, approval);
        }

        /// <summary>The same, asking for a lifetime as the wire writes it, or for none.</summary>
        private GateContext Request(string name, string[] profiles, string lifetime, IPairingApproval approval)
        {
            return Request(name, profiles, lifetime, lifetime, approval);
        }

        /// <summary>
        /// The same, signing for one lifetime and sending another, which is a request altered on
        /// the way.
        /// </summary>
        private GateContext Request(
            string name, string[] profiles, string signed, string sent, IPairingApproval approval)
        {
            var challenge = _lastChallenge;
            var proof = challenge == null
                ? new byte[32]
                : PairingProof.Request(
                    _token, _binding, _nonce, challenge, PairingProof.Asked(name, profiles, signed));

            return Send(
                "POST", RequestPath, RequestBody(name, profiles, sent, proof), _connection, approval);
        }

        /// <summary>A request's body, its lifetime left out when it asks for none.</summary>
        private static string RequestBody(string name, string[] profiles, string lifetime, byte[] proof)
        {
            var members = new List<(string Name, object Value)> { ("name", name), ("profiles", profiles) };
            if (lifetime != null)
            {
                members.Add(("lifetime", lifetime));
            }

            members.Add(("proof", Convert.ToBase64String(proof)));
            return Bodies.Of(members.ToArray());
        }

        private byte[] _lastChallenge;

        private GateContext Send(
            string method, string path, string body, PairingConnection pairing, IPairingApproval approval)
        {
            return Send(method, path, body, pairing, approval, ClientIdentity.Stranger(_target.DatabaseId));
        }

        private GateContext Send(
            string method,
            string path,
            string body,
            PairingConnection pairing,
            IPairingApproval approval,
            ClientIdentity client)
        {
            var head = new HttpRequestHead(
                method,
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Bodies.LengthOf(body));

            var context = new GateContext(head, body, client);
            if (pairing != null)
            {
                pairing.PutOn(context);
            }

            // What the router would have chosen, chosen here by the same rule: the path the
            // document declares the operation at.
            var handler = Handler(approval);
            if (string.Equals(path, RequestPath, StringComparison.Ordinal))
            {
                handler.Request(context);
            }
            else
            {
                handler.Hello(context);
            }

            if (context.Status == 200 && path == HelloPath)
            {
                var parsed = JsonBody.Read(context.Json);
                _lastChallenge = parsed == null ? null : Convert.FromBase64String(parsed.Text("challenge"));
            }

            return context;
        }

        /// <summary>
        /// The identity the bundle hands out, read as the renewal tests read theirs: through the
        /// plugin's own reader, which holds it to a key the certificate certifies, and given to
        /// Windows as a certificate alone, since nothing here needs Windows to hold its key.
        /// </summary>
        private static X509Certificate2 IdentityIn(GateContext context)
        {
            var body = JsonBody.Read(context.Json);
            Assert.That(body, Is.Not.Null, context.Json);

            var identity = CertificateBlob.Load(body.Text("identity"));
            Assert.That(identity, Is.Not.Null, "a certificate with no key of its own pairs nobody");
            return identity.Read();
        }
    }
}
