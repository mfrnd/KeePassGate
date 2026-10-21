using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Security.Authentication;
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

namespace KeePassGate.Tests
{
    /// <summary>
    /// Pairing over a real socket, with real TLS and a client that holds nothing but a token and a
    /// port number.
    /// </summary>
    /// <remarks>
    /// The one test worth the whole fixture is the one that pairs and then uses what it was given.
    /// Everything else in this suite proves a piece; that one proves the piece fit together, from a
    /// name derived from a token in a ClientHello through to a certificate that opens a route on the
    /// very next connection.
    ///
    /// No KeePass and no desktop. The operator is a stand-in and the database is a fake, because
    /// what is being tested here is the protocol. The end to end tier pairs against the real thing.
    /// </remarks>
    [TestFixture]
    public sealed class PairingGateTests
    {
        private TestAuthority _target;
        private FakeGateTargets _targets;
        private InvitationBook _book;
        private AgreeableOperator _operator;
        private RecordingGateLog _log;
        private GateListener _listener;
        private Invitation _invitation;

        [SetUp]
        public void SetUp()
        {
            _target = TestAuthority.New();
            _targets = new FakeGateTargets(_target);
            _log = new RecordingGateLog();
            _book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log);
            _operator = new AgreeableOperator();

            _listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(_targets, _book, _log),
                GateOperations.Router("9.9.9", new PairingHandler(_book, _operator, null, _log)),
                _log);

            Assert.That(_listener.Start(), Is.True, "the listener did not start");

            _invitation = _book.Invite(_target.DatabaseId);
        }

        [TearDown]
        public void TearDown()
        {
            _listener.Dispose();
            _book.Dispose();
            _target.Dispose();

            foreach (var entry in _log.Entries)
            {
                TestContext.Out.WriteLine(entry.ToString());
            }
        }

        [Test]
        public void A_client_holding_the_token_reaches_the_pairing_routes()
        {
            var client = NewClient();

            var reply = client.Hello();

            Assert.That(reply.Answered, Is.True, reply.Failure);
            Assert.That(reply.Status, Is.EqualTo(200), reply.Body);
        }

        [Test]
        public void The_server_proves_itself_to_a_client_that_has_no_anchor_yet()
        {
            var reply = NewClient().Hello();

            Assert.That(reply.ServerProved, Is.True,
                "without this a client would accept whatever certificate arrived");
        }

        [Test]
        public void A_client_pairs_and_the_certificate_it_is_given_works_at_once()
        {
            var client = NewClient();

            Assume.That(client.Hello().ServerProved, Is.True);
            var paired = client.Request("agent-fictitious", "ops");

            Assert.That(paired.Status, Is.EqualTo(200), paired.Failure ?? paired.Body);

            // The name is built from the gate key it was just handed, and the certificate is the
            // one it was just issued. Nothing else was arranged in between.
            var reply = GateClient.Get(
                _listener.Port, SniName.For(client.GateKey), client.Identity, GateOperations.StatusPath);

            Assert.That(reply.Answered, Is.True, reply.Failure);
            Assert.That(reply.Status, Is.EqualTo(200), reply.Body);
            Assert.That(reply.Body, Does.Contain("\"ok\":true"));
        }

        [Test]
        public void What_it_was_given_names_the_authority_it_will_pin_from_now_on()
        {
            var client = NewClient();
            client.Hello();
            client.Request("agent-fictitious");

            Assert.That(client.Authority.RawData, Is.EqualTo(_target.Authority.RawData));
            Assert.That(AnchorPin.Chains(client.Identity, client.Authority, false), Is.True);
        }

        [Test]
        public void An_invitation_reaches_pairing_and_nothing_else()
        {
            byte[] nonce;
            var name = NewClient().NewName(out nonce);

            var reply = GateClient.Get(_listener.Port, name, null, GateOperations.StatusPath);

            Assert.That(reply.Status, Is.EqualTo(401),
                "an invitation is not an identity, so it opens no route that needs one");
        }

        [Test]
        public void A_paired_connection_does_not_find_the_pairing_routes()
        {
            using (var certificate = _target.IssueAdmittedClient())
            {
                var reply = GateClient.Send(
                    _listener.Port,
                    _target.NewName(),
                    certificate,
                    "POST " + PairingHandler.HelloPath + " HTTP/1.1\r\nHost: x\r\nContent-Length: 2\r\n\r\n{}");

                Assert.That(reply.Status, Is.EqualTo(404));
            }
        }

        [Test]
        public void An_admitted_certificate_on_an_invitation_is_logged_as_the_invitation_it_named()
        {
            // A client can hold both at once, and then it reaches nothing: the router counts it as
            // admitted only while no invitation brought it, and the pairing routes are for clients
            // that are not paired. The operator's log has to say which of the two decided that,
            // and it used to read exactly like an ordinary admitted connection.
            byte[] nonce;
            var name = NewClient().NewName(out nonce);

            using (var certificate = _target.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, name, certificate, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(404),
                    "an admitted certificate on an invitation's name reaches no route at all");
                Assert.That(
                    _log.Entries.Any(e => e.Message.Contains("on invitation " + _invitation.Id)),
                    Is.True, "the connection was logged without the invitation that decided it");
            }
        }

        [Test]
        public void A_spent_invitation_answers_to_nothing_afterwards()
        {
            var client = NewClient();
            client.Hello();
            Assume.That(client.Request("agent-fictitious").Status, Is.EqualTo(200));

            var again = client.Hello();

            Assert.That(again.Answered, Is.False,
                "a spent invitation must be dropped at the gate, exactly like a name that never existed");
        }

        [Test]
        public void An_expired_invitation_is_dropped_without_a_word()
        {
            using (var book = new InvitationBook(TimeSpan.Zero, null, _log))
            {
                var expired = book.Invite(_target.DatabaseId);
                var client = new PairingClient(_listener.Port, expired.Text) { Timeout = 5000 };

                var reply = client.Hello();

                Assert.That(reply.Answered, Is.False);
            }
        }

        [Test]
        public void An_invitation_for_a_database_that_is_not_open_is_dropped()
        {
            // Another database is open, so there is something to serve. It is just not this one,
            // and an invitation only ever pairs with the database it was created against.
            using (var elsewhere = TestAuthority.New())
            {
                var client = NewClient();
                _targets.Clear();
                _targets.Add(elsewhere);

                var reply = client.Hello();

                Assert.That(reply.Answered, Is.False);
                Assert.That(_log.Entries.Any(e => e.Message.Contains("named a database that is not open")), Is.True);
            }
        }

        [Test]
        public void A_name_built_from_the_wrong_token_is_dropped()
        {
            var client = new PairingClient(_listener.Port, PairingToken.Encode(PairingToken.New()))
            {
                Timeout = 5000
            };

            Assert.That(client.Hello().Answered, Is.False);
        }

        [Test]
        public void A_client_that_cannot_prove_the_token_never_reaches_the_operator()
        {
            var client = NewClient();
            client.WrongProof = new byte[32];

            var reply = client.Hello();

            Assert.That(reply.Status, Is.EqualTo(401));
            Assert.That(_operator.Asked, Is.Zero);
        }

        [Test]
        public void An_operator_who_refuses_spends_the_invitation_and_issues_nothing()
        {
            using (var listener = Listening(FakePairingApproval.Refusing()))
            {
                var client = new PairingClient(listener.Port, _invitation.Text);
                client.Hello();

                var reply = client.Request("agent-fictitious");

                Assert.That(reply.Status, Is.EqualTo(403));
                Assert.That(client.Identity, Is.Null);
                Assert.That(_book.Outstanding(), Is.Empty);
            }
        }

        [Test]
        public void An_operator_who_outlasts_the_ordinary_budget_still_pairs()
        {
            // `PAIRING.md` §303: phase 4 waits on a person rather than on the wire, so the
            // connection is measured against `PairingBudget` from the moment its request is off
            // the wire, which is where phase 4 begins and where every other connection's deadline
            // is dropped. Closing it at the ordinary budget cancels nothing, because the
            // invitation is spent and the record written and saved either way. All that is lost is
            // the answer, which leaves a record on file for a certificate nobody ever received and
            // a client with nothing to try again with.
            //
            // Five seconds is a generous ordinary budget, far more than a handshake and a request
            // take on a loaded machine, and the operator outlasts it by two.
            var deliberate = FakePairingApproval.Approving("ops");
            deliberate.Thinks = TimeSpan.FromSeconds(7);

            using (var listener = Listening(deliberate, new GateOptions { Port = 0, Budget = TimeSpan.FromSeconds(5) }))
            {
                var client = new PairingClient(listener.Port, _invitation.Text);
                Assume.That(client.Hello().ServerProved, Is.True);

                var reply = client.Request("agent-fictitious", "ops");

                Assert.That(reply.Status, Is.EqualTo(200), reply.Failure ?? reply.Body);
                Assert.That(client.Identity, Is.Not.Null,
                    "the certificate is what the connection had to outlive the dialog to hand over");
                Assert.That(deliberate.Asked, Is.EqualTo(1));
            }
        }

        [Test]
        public void An_invitation_named_and_no_request_made_is_worth_the_ordinary_budget_and_no_more()
        {
            // The longer budget is for a connection that is about to wait on a person, and a
            // connection that has made no request waits on nobody. Granted on the name instead,
            // one invitation buys every connection slot for six minutes each, held by a peer that
            // feeds the gate a byte at a time: the harm the locked case is guarded against,
            // against a database that was open.
            //
            // The ordinary budget is a generous five seconds, because the handshake is inside it
            // and a loaded machine can take seconds over one, and the pairing budget is a minute.
            var options = new GateOptions
            {
                Port = 0,
                Budget = TimeSpan.FromSeconds(5),
                PairingBudget = TimeSpan.FromSeconds(60),
            };

            using (var listener = Listening(_operator, options))
            {
                byte[] nonce;
                var name = new PairingClient(listener.Port, _invitation.Text).NewName(out nonce);

                // A real handshake under the invitation's name, so the gate has resolved it and is
                // waiting for a request, and then a head a byte at a time, padded to take a minute.
                // Every read is satisfied well inside the read timeout, so nothing but a deadline
                // can end this, and which deadline it is is what the clock reports.
                var spent = Stopwatch.StartNew();

                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", listener.Port);
                    tcp.ReceiveTimeout = 60000;

                    using (var tls = new SslStream(
                        tcp.GetStream(), false, delegate { return true; }, delegate { return null; }))
                    {
                        tls.AuthenticateAsClient(name, null, SslProtocols.Tls13, false);

                        var head = Encoding.ASCII.GetBytes(
                            "POST " + PairingHandler.HelloPath + " HTTP/1.1\r\nHost: x\r\n" +
                            "X-Padding: " + new string('a', 190) + "\r\n\r\n");

                        try
                        {
                            foreach (var b in head)
                            {
                                tls.Write(new[] { b }, 0, 1);
                                tls.Flush();
                                Thread.Sleep(250);
                            }
                        }
                        catch (IOException)
                        {
                            // The write is what fails when the gate gives up on it, which is the
                            // outcome being timed.
                        }
                    }
                }

                spent.Stop();

                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)),
                    "a connection that named an invitation and asked nothing kept its slot for the " +
                    "budget that waits on a person");
            }
        }

        [Test]
        public void A_database_whose_authority_has_run_out_refuses_and_reports_no_defect()
        {
            // What a kdbx a century old looks like from the outside: the authority is read, the
            // database is served, the handshake succeeds and the proofs verify, and the pairing
            // reaches the one thing an expired authority cannot do. Nothing refuses ahead of that,
            // `CA.md` §219, so the operator is asked, and the plugin does not report itself as
            // broken for the age of somebody's database.
            //
            // The century is the issuer's clock rather than the authority's age, because
            // `CertificateRequest` refuses to write a date before 1950 at all.
            var incidents = new GateIncidents(_log);
            var aCenturyOn = new AuthorityIssuer(
                () => DateTimeOffset.UtcNow + AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(1));

            using (var old = TestAuthority.New())
            using (var listener = Listening(new FakeGateTargets(old), incidents, aCenturyOn))
            {
                var invitation = _book.Invite(old.DatabaseId);
                var client = new PairingClient(listener.Port, invitation.Text);

                Assume.That(client.Hello().Status, Is.EqualTo(200));
                var reply = client.Request("agent-fictitious");

                Assert.That(reply.Status, Is.EqualTo(500), reply.ToString());
                Assert.That(reply.Body, Does.Contain("internal error"));
                Assert.That(client.Identity, Is.Null, "and nothing was issued from it");
                Assert.That(incidents.Count, Is.Zero,
                    "an incident is this plugin doing something wrong, which the age of a database is not");
                Assert.That(_operator.Asked, Is.EqualTo(1), "nothing refused the request before the dialog");
            }
        }

        [Test]
        public void The_operator_is_told_where_the_request_came_from()
        {
            var client = NewClient();
            client.Hello();
            client.Request("agent-fictitious");

            Assert.That(_operator.Asked, Is.EqualTo(1));
        }

        [Test]
        public void Two_exchanges_interleaved_leave_neither_of_them_able_to_finish()
        {
            // An invitation carries one exchange at a time, so a second hello takes the first one's
            // challenge away. Two callers racing therefore get nothing rather than one of them
            // getting something the other proved for.
            var first = NewClient();
            var second = NewClient();

            Assume.That(first.Hello().ServerProved, Is.True);
            Assume.That(second.Hello().ServerProved, Is.True);

            var one = first.Request("agent-one");
            var two = second.Request("agent-two");

            Assert.That(one.Status, Is.Not.EqualTo(200), one.ToString());
            Assert.That(two.Status, Is.Not.EqualTo(200), two.ToString());
            Assert.That(_operator.Asked, Is.Zero, "nothing got as far as the operator");
            Assert.That(_book.Outstanding().Count, Is.EqualTo(1),
                "and nothing was spent, so the invitee can simply try again");
        }

        [Test]
        public void And_then_one_of_them_can_start_again_and_pair()
        {
            var first = NewClient();
            var second = NewClient();
            first.Hello();
            second.Hello();
            first.Request("agent-one");
            second.Request("agent-two");

            second.Hello();
            var reply = second.Request("agent-two");

            Assert.That(reply.Status, Is.EqualTo(200), reply.ToString());
        }

        private PairingClient NewClient()
        {
            return new PairingClient(_listener.Port, _invitation.Text);
        }

        private GateListener Listening(IPairingApproval approval)
        {
            return Listening(approval, new GateOptions { Port = 0 });
        }

        /// <summary>The same, with limits of a test's own choosing.</summary>
        private GateListener Listening(IPairingApproval approval, GateOptions options)
        {
            var listener = new GateListener(
                options,
                new GateResolver(_targets, _book, _log),
                GateOperations.Router("9.9.9", new PairingHandler(_book, approval, null, _log)),
                _log);

            Assert.That(listener.Start(), Is.True);
            return listener;
        }

        /// <summary>
        /// The same gate over other databases, issuing on a clock of the test's choosing and
        /// counting what escapes a handler, which is the only place a test can see a refusal being
        /// reported as a defect in this plugin.
        /// </summary>
        private GateListener Listening(FakeGateTargets targets, GateIncidents incidents, AuthorityIssuer issuer)
        {
            var listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(targets, _book, _log),
                GateOperations.Router(
                    "9.9.9", new PairingHandler(_book, _operator, new PairingOptions(), issuer, null, null, _log)),
                incidents,
                _log);

            Assert.That(listener.Start(), Is.True);
            return listener;
        }
    }
}
