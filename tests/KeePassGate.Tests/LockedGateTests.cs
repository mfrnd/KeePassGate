using System;
using System.Linq;
using KeePassGate.Authority;
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
    /// A locked database over a real socket: the handshake still completes, and the answer says
    /// only that it is locked.
    /// </summary>
    /// <remarks>
    /// This is the test the responder certificate exists for. Without one, a locked KeePass cannot
    /// complete a handshake at all and a client gets a bare connection failure indistinguishable
    /// from a wrong port, a stopped KeePass, or an attack. `CA.md` §207 and `PAIRING.md`
    /// §308 both turn on that distinction, and only a real handshake can show it.
    /// </remarks>
    [TestFixture]
    public sealed class LockedGateTests
    {
        private TestAuthority _authority;
        private LockedTarget _locked;
        private FakeGateTargets _targets;
        private RecordingGateLog _log;
        private GateListener _listener;
        private byte[] _gateKey;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _gateKey = _authority.GateKey;

            // What the plugin holds after a lock: the gate key, and a responder minted while the
            // database was still open.
            _locked = new LockedTarget(_authority.DatabaseId, _gateKey, Responder());

            _targets = new FakeGateTargets(_locked);
            _log = new RecordingGateLog();
            _listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(_targets, _log),
                GateOperations.Router("9.9.9", new PairingHandler(
                    new InvitationBook(_log), FakePairingApproval.Approving(), _log)),
                _log);

            Assert.That(_listener.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _listener.Dispose();
            _locked.Dispose();
            _authority.Dispose();
        }

        [Test]
        public void A_locked_database_still_answers_the_name_it_answered_before()
        {
            var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, GateOperations.StatusPath);

            Assert.That(reply.Answered, Is.True,
                "a locked KeePass that cannot be reached at all is indistinguishable from an attack");
        }

        [Test]
        public void The_second_client_is_told_it_is_locked_too()
        {
            // Every connection gives its target back when it ends. Handed out bare, the first one
            // to do that zeroed the gate key, and every later client was dropped at the name.
            for (var client = 0; client < 2; client++)
            {
                var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(503), "client " + client + ": " + reply.Failure);
            }
        }

        [TestCase("/gate/status")]
        [TestCase("/pair/hello")]
        [TestCase("/pair/request")]
        [TestCase("/anything")]
        [TestCase("/")]
        public void And_what_it_says_is_that_it_is_locked(string path)
        {
            // Declared, undeclared and pairing alike. Nothing reads the path, which is what makes
            // the list above a demonstration rather than a specification: there is no route here
            // to have got it wrong.
            var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, path);

            Assert.That(reply.Status, Is.EqualTo(503), "on " + path + ": " + reply.Failure);
            Assert.That(reply.Body, Does.Contain(GateConversation.LockedError));
        }

        [Test]
        public void A_request_this_reader_would_refuse_is_still_only_locked()
        {
            // The request line is nonsense and would be a 400 from an unlocked database. A locked
            // one has not read it, so there is nothing to be refused for.
            var reply = GateClient.Send(
                _listener.Port, SniName.For(_gateKey), null, "nonsense\r\n\r\n");

            Assert.That(reply.Status, Is.EqualTo(503), reply.Failure ?? reply.Raw);
        }

        [Test]
        public void A_body_it_has_already_decided_to_refuse_is_never_invited()
        {
            // `Expect: 100-continue`, and no body behind it. An unlocked database answers
            // `100 Continue` and waits; this one has already decided, and saying so costs the
            // client nothing but the head it has already sent.
            var reply = GateClient.Send(
                _listener.Port,
                SniName.For(_gateKey),
                null,
                "POST /pair/hello HTTP/1.1\r\nHost: gate\r\nContent-Length: 9\r\n" +
                "Expect: 100-continue\r\n\r\n");

            Assert.That(reply.Status, Is.EqualTo(503), reply.Failure ?? reply.Raw);
            Assert.That(reply.Raw, Does.Not.Contain("100 Continue"));
        }

        [Test]
        public void A_body_over_the_cap_is_still_told_only_that_it_is_locked()
        {
            // The reader refuses a length over the cap before the lock is ever mentioned, and the
            // body it announced arrives regardless. Closing the socket over those unread bytes is
            // answered with a reset that discards the 503, which leaves the client with no answer
            // at all: the outcome the responder certificate exists to prevent.
            using (var listener = Listening(new GateOptions { Port = 0, MaxBodyBytes = 64 }))
            {
                var body = new string('x', 512 * 1024);
                var reply = GateClient.Send(
                    listener.Port,
                    SniName.For(_gateKey),
                    null,
                    "POST /pair/hello HTTP/1.1\r\nHost: gate\r\nContent-Length: " +
                    body.Length + "\r\n\r\n" + body);

                Assert.That(reply.Status, Is.EqualTo(503), reply.Failure ?? reply.Raw);
                Assert.That(reply.Body, Does.Contain(GateConversation.LockedError));
            }
        }

        [Test]
        public void It_names_the_lock_and_nothing_about_the_database()
        {
            var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, GateOperations.StatusPath);

            Assert.That(reply.Body, Does.Not.Contain(_authority.DatabaseId),
                "a locked gate identifies nothing");
            Assert.That(reply.Body.Length, Is.LessThan(40));
        }

        [Test]
        public void The_certificate_it_answers_with_is_marked_as_a_responder()
        {
            // A client has to be able to tell, because an impostor holding this key reads nothing
            // but could answer with a fabricated secret, and an agent acting on a forged answer is
            // worse than an agent that fails.
            var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, GateOperations.StatusPath);

            Assert.That(reply.ServerCertificate, Is.Not.Null, reply.Failure);
            Assert.That(CertificateUris.Of(reply.ServerCertificate), Is.EqualTo(new[] { AuthorityIssuer.ResponderClaim }));
            Assert.That(reply.ServerCertificate.Subject, Is.EqualTo(AuthorityIssuer.ResponderSubject));
        }

        [Test]
        public void It_is_not_the_normal_host_certificate()
        {
            // The normal host key is let go at lock, `CA.md` §208, which is exact about how much
            // of that is erasure. Two certificates buy nothing if the privileged one is still
            // being served while locked, which is what this asks.
            var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, GateOperations.StatusPath);

            Assert.That(reply.ServerCertificate.RawData, Is.Not.EqualTo(_authority.HostCertificate.RawData));
        }

        [Test]
        public void A_client_that_would_be_admitted_is_told_the_same_thing()
        {
            using (var certificate = _authority.IssueAdmittedClient("ops"))
            {
                var reply = GateClient.Get(
                _listener.Port, SniName.For(_gateKey), certificate, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(503), reply.Failure);
                Assert.That(reply.Body, Does.Contain(GateConversation.LockedError));
            }
        }

        [Test]
        public void Pairing_against_a_locked_database_is_refused_before_anything_is_spent()
        {
            using (var book = new InvitationBook(_log))
            {
                var invitation = book.Invite(_authority.DatabaseId);

                using (var listener = Listening(book))
                {
                    var client = new PairingClient(listener.Port, invitation.Text);

                    var reply = client.Hello();

                    Assert.That(reply.Status, Is.EqualTo(503), reply.Failure ?? reply.Body);
                    Assert.That(book.Outstanding().Count, Is.EqualTo(1),
                        "a lock costs an attempt, never the invitation");
                }
            }
        }

        [Test]
        public void Closing_every_connection_is_something_the_listener_can_do()
        {
            // What both lock transitions call. There is nothing being served here, so this is about
            // the answer rather than the effect: it must be safe to ask at any time.
            Assert.That(_listener.CloseAll("a database was locked"), Is.Zero);
        }

        /// <summary>The same locked database, served with limits of a test's own choosing.</summary>
        private GateListener Listening(GateOptions options)
        {
            var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

            Assert.That(listener.Start(), Is.True);
            return listener;
        }

        private GateListener Listening(IInvitations book)
        {
            var listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(_targets, book, _log),
                GateOperations.Router("9.9.9", new PairingHandler(book, FakePairingApproval.Approving(), _log)),
                _log);

            Assert.That(listener.Start(), Is.True);
            return listener;
        }

        /// <summary>The responder, minted from the authority exactly as the plugin mints it.</summary>
        private HostKeyMaterial Responder()
        {
            using (var responder = new AuthorityIssuer().IssueResponder(_authority.Authority))
            {
                return HostKeyMaterial.From(CertificateBlob.Bytes(CertificateBlob.Save(responder)));
            }
        }
    }
}
