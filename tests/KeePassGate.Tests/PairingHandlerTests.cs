using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

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
        private TestAuthority _target;
        private InvitationBook _book;
        private Invitation _invitation;
        private RecordingGateLog _log;
        private byte[] _binding;
        private byte[] _nonce;
        private byte[] _token;
        private PairingConnection _connection;
        private AuthorityIssuer _issuer;
        private ApprovalBook _held;

        [SetUp]
        public void SetUp()
        {
            _issuer = null;
            _held = new ApprovalBook();
            _target = TestAuthority.New();
            _log = new RecordingGateLog();
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
            var context = Send("POST", PairingHandler.HelloPath, body, _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_connection_that_did_not_arrive_on_an_invitation_is_told_what_every_route_tells_it()
        {
            var context = Send("POST", PairingHandler.HelloPath, "{}", null, Approving());

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
                    PairingHandler.HelloPath,
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
                "POST", PairingHandler.RequestPath, RequestBody("agent-fictitious", Array.Empty<string>(), null, new byte[32]),
                _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(409));
        }

        [Test]
        public void A_request_that_cannot_prove_itself_is_refused_and_the_exchange_is_over()
        {
            Hello(ClientProof());

            var wrong = Send(
                "POST", PairingHandler.RequestPath, RequestBody("agent-fictitious", Array.Empty<string>(), null, new byte[32]),
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
                    "POST", PairingHandler.RequestPath,
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
                Assert.That(issued.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(issued.HasPrivateKey, Is.True, "a certificate with no key pairs nobody");
                Assert.That(AnchorPin.Chains(issued, _target.Authority, false), Is.True);
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
                var record = _target.Admission(CertificateDigest.Of(issued));

                Assert.That(record, Is.Not.Null);
                Assert.That(record.Profiles, Is.EqualTo(new[] { "ops" }));
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
        public void A_dialog_that_throws_is_a_refusal_to_serve_rather_than_a_failed_request()
        {
            Hello(ClientProof());
            var approval = Approving();
            approval.Throws = new InvalidOperationException("the screen fell over");

            var context = Request("agent-fictitious", Array.Empty<string>(), approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
            Assert.That(_log.Errors, Is.Not.Empty);
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
        public void A_profile_taken_off_the_list_under_the_write_voids_the_approval_and_spends_nothing()
        {
            // Compared under the writer's lock: the write would put the name back on the list,
            // with every rule naming it, after the operator took it off.
            _target.KnownProfiles = new[] { "ops" };
            _target.BeforeWrite = () => _target.Forget("ops");
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, Approving("ops"));

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Does.Not.Contain("heldUntil"), "the operator is asked again");
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
            Assert.That(_held.Count, Is.Zero);
            Assert.That(_target.Admitted, Is.Null);
        }

        [Test]
        public void A_list_of_profiles_that_will_not_read_refuses_the_pairing_before_the_operator_is_asked()
        {
            // As a renewal is refused: shown as naming nothing, every name would be "(new)", and
            // approving would write a list of only the approved names over the damaged one.
            _target.ProfilesUnreadable = "the profiles this database names will not read";
            var approval = Approving("ops");
            Hello(ClientProof());

            var context = Request("agent-fictitious", new[] { "ops" }, approval);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(approval.Asked, Is.Zero);
            Assert.That(_book.Outstanding(), Has.Count.EqualTo(1), "nothing is spent");
        }

        [Test]
        public void An_authority_recreated_under_the_write_admits_nobody()
        {
            _target.BeforeWrite = () => _target.Retired = true;
            Hello(ClientProof());

            var context = Request("agent-fictitious", Array.Empty<string>(), Approving());

            Assert.That(context.Status, Is.EqualTo(409));
            Assert.That(_target.Admitted, Is.Null);
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
                "POST", PairingHandler.RequestPath, RequestBody(name, Array.Empty<string>(), null, new byte[32]),
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
                "POST", PairingHandler.RequestPath, RequestBody("agent-fictitious", new[] { profile }, null, new byte[32]),
                _connection, Approving());

            Assert.That(context.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_name_is_checked_before_the_proof_is_and_neither_spends_anything()
        {
            Hello(ClientProof());

            Send(
                "POST", PairingHandler.RequestPath, RequestBody("", Array.Empty<string>(), null, new byte[32]),
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

        [Test]
        public void The_two_operations_are_declared_where_this_answers_them()
        {
            // Which paths belong to pairing is the document's answer rather than this class's, and
            // the startup assertion is what stops the two disagreeing. What is asserted here is the
            // half that assertion cannot see: that these are the paths the exchange is written for.
            Assert.That(GateOperations.PathOf(GateOperations.PairHello), Is.EqualTo(PairingHandler.HelloPath));
            Assert.That(GateOperations.PathOf(GateOperations.PairRequest), Is.EqualTo(PairingHandler.RequestPath));
        }

        private PairingHandler Handler(IPairingApproval approval)
        {
            // The default issuer unless a test named one, which is how a clock that moves between
            // the check and the issuance is reached at all.
            return _issuer == null
                ? new PairingHandler(_book, approval, _held, _log)
                : new PairingHandler(_book, approval, new PairingOptions(), _issuer, null, _log, _held);
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
                PairingHandler.HelloPath,
                "{\"proof\":\"" + Convert.ToBase64String(proof) + "\"}",
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
                "POST", PairingHandler.RequestPath, RequestBody(name, profiles, sent, proof), _connection, approval);
        }

        private static string RequestBody(string name, string[] profiles, string lifetime, byte[] proof)
        {
            var text = new System.Text.StringBuilder();
            text.Append("{\"name\":\"").Append(name).Append("\",\"profiles\":[");

            for (var i = 0; i < profiles.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append('"').Append(profiles[i]).Append('"');
            }

            text.Append(']');
            if (lifetime != null)
            {
                text.Append(",\"lifetime\":\"").Append(lifetime).Append('"');
            }

            text.Append(",\"proof\":\"").Append(Convert.ToBase64String(proof)).Append("\"}");
            return text.ToString();
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
                body == null ? 0 : body.Length);

            var context = new GateContext(head, body, client);
            if (pairing != null)
            {
                pairing.PutOn(context);
            }

            // What the router would have chosen, chosen here by the same rule: the path the
            // document declares the operation at.
            var handler = Handler(approval);
            if (string.Equals(path, PairingHandler.RequestPath, StringComparison.Ordinal))
            {
                handler.Request(context);
            }
            else
            {
                handler.Hello(context);
            }

            if (context.Status == 200 && path == PairingHandler.HelloPath)
            {
                var parsed = JsonBody.Read(context.Json);
                _lastChallenge = parsed == null ? null : Convert.FromBase64String(parsed.Text("challenge"));
            }

            return context;
        }

        private static X509Certificate2 IdentityIn(GateContext context)
        {
            var body = JsonBody.Read(context.Json);
            Assert.That(body, Is.Not.Null, context.Json);

            return new X509Certificate2(
                Convert.FromBase64String(body.Text("identity")),
                (string)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        }
    }
}
