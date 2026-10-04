using System;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A database holding several authorities while the operator has not chosen one, over a real
    /// socket and a real handshake.
    /// </summary>
    /// <remarks>
    /// Nothing is served, and a client is told so rather than dropped: each candidate answers
    /// under its own gate key with its own stored host certificate, and says `503 undecided`
    /// whatever it is asked and whoever asks, a client the candidate admitted included.
    /// </remarks>
    [TestFixture]
    public sealed class UndecidedGateTests
    {
        private TestAuthority _authority;
        private UndecidedTarget _undecided;
        private RecordingGateLog _log;
        private GateListener _listener;
        private CountingHandler _handler;
        private byte[] _gateKey;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _gateKey = _authority.GateKey;
            _undecided = new UndecidedTarget(_authority.DatabaseId, _gateKey, _authority.Host);

            _log = new RecordingGateLog();
            _handler = new CountingHandler();
            _listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(new FakeGateTargets(_undecided), _log),
                _handler,
                _log);

            Assert.That(_listener.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _listener.Dispose();
            _undecided.Dispose();
            _authority.Dispose();
        }

        [TestCase("/gate/status")]
        [TestCase("/pair/hello")]
        [TestCase("/anything")]
        public void Every_path_is_answered_that_nothing_is_decided_yet(string path)
        {
            var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, path);

            Assert.That(reply.Status, Is.EqualTo(503), "on " + path + ": " + reply.Failure);
            Assert.That(reply.Body, Does.Contain(UndecidedTarget.Error));
            Assert.That(reply.Body, Does.Not.Contain(GateConversation.LockedError),
                "nothing is locked, and a client told so would wait for an unlock that is not coming");
            Assert.That(_handler.Calls, Is.Zero, "nothing it asked for is read");
        }

        [Test]
        public void A_client_the_candidate_admitted_is_answered_the_same_way()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _authority.Admit(certificate, "ops");

                var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), certificate, "/gate/status");

                Assert.That(reply.Status, Is.EqualTo(503), reply.Failure);
                Assert.That(_handler.Calls, Is.Zero);
            }
        }

        [Test]
        public void Its_certificate_is_not_even_looked_at()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _authority.Admit(certificate, "ops");

                var identity = ClientCredentials.For(certificate, _undecided, null);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(identity.IsLocked, Is.False, "it is not locked, and must not say so");
                Assert.That(identity.UnavailableError, Is.EqualTo(UndecidedTarget.Error));
                Assert.That(identity.Profiles, Is.Empty);
            }
        }

        [Test]
        public void Nothing_can_be_admitted_while_it_waits()
        {
            Assert.That(_undecided.Authority, Is.Null);
            Assert.That(_undecided.Vault, Is.Null);
            Assert.That(_undecided.Admission(_authority.DatabaseId), Is.Null);
            Assert.That(_undecided.AdmissionBlocked, Is.EqualTo(UndecidedTarget.Reason));
            var write = _undecided.Admit(null, null, null);
            Assert.That(write.Ok, Is.False);
            Assert.That(write.IsBusy, Is.True, "choosing is all it takes, so the invitee should try again");
        }

        [Test]
        public void A_second_client_is_answered_too()
        {
            // Handed out bare, the first connection to give the target back would zero the gate
            // key for everybody after it.
            for (var client = 0; client < 2; client++)
            {
                var reply = GateClient.Get(_listener.Port, SniName.For(_gateKey), null, "/gate/status");

                Assert.That(reply.Status, Is.EqualTo(503), "client " + client + ": " + reply.Failure);
            }
        }

        [Test]
        public void It_is_never_built_without_the_three_things_it_needs()
        {
            Assert.Throws<ArgumentException>(() => new UndecidedTarget(null, _gateKey, _authority.Host));
            Assert.Throws<ArgumentNullException>(() => new UndecidedTarget("db", null, _authority.Host));
            Assert.Throws<ArgumentNullException>(() => new UndecidedTarget("db", _gateKey, null));
        }

        private sealed class CountingHandler : IGateHandler
        {
            public int Calls { get; private set; }

            public void Handle(IGateContext context)
            {
                Calls++;
                context.Respond(200, "{}");
            }
        }
    }
}
