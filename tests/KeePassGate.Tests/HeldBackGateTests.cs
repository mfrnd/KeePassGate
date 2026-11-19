using System;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A database the gate holds back, over a real socket and a real handshake: a database
    /// holding several authorities while the operator has chosen none of them, and a database
    /// answered from what the gate last served it with.
    /// </summary>
    /// <remarks>
    /// Nothing is served, and a client is told so rather than dropped: each candidate answers
    /// under its own gate key with its own stored host certificate, and says `503 undecided`
    /// whatever it is asked and whoever asks, a client the candidate admitted included.
    /// </remarks>
    [TestFixture]
    public sealed class HeldBackGateTests
    {
        private TestAuthority _authority;
        private HeldBackTarget _undecided;
        private RecordingGateLog _log;
        private GateListener _listener;
        private CountingHandler _handler;
        private byte[] _gateKey;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _gateKey = _authority.GateKey;
            _undecided = new HeldBackTarget(_authority.DatabaseId, _gateKey, _authority.Host, () => HeldBackTarget.Undecided);

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
            Assert.That(reply.Body, Does.Contain(HeldBackTarget.Undecided));
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
                Assert.That(identity.UnavailableError, Is.EqualTo(HeldBackTarget.Undecided));
                Assert.That(identity.Profiles, Is.Empty);
            }
        }

        [Test]
        public void Nothing_can_be_admitted_while_it_waits()
        {
            Assert.That(_undecided.Authority, Is.Null);
            Assert.That(_undecided.AuthorityKey, Is.Null, "nothing is issued from a candidate either");
            Assert.That(_undecided.Vault, Is.Null);
            Assert.That(_undecided.Admission(_authority.DatabaseId), Is.Null);
            Assert.That(_undecided.AdmissionBlocked, Is.EqualTo("the gate holds the database back"));
            var write = _undecided.Admit(null, null, null);
            Assert.That(write.Ok, Is.False);
            Assert.That(write.IsBusy, Is.True, "what is held back is read anew, so the invitee should try again");
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
        public void It_is_never_built_without_what_it_needs()
        {
            Assert.Throws<ArgumentException>(() => new HeldBackTarget(null, _gateKey, _authority.Host, () => null));
            Assert.Throws<ArgumentNullException>(() => new HeldBackTarget("db", null, _authority.Host, () => null));
            Assert.Throws<ArgumentNullException>(() => new HeldBackTarget("db", _gateKey, null, () => null));
            Assert.Throws<ArgumentNullException>(() => new HeldBackTarget("db", _gateKey, _authority.Host, null));
        }

        [Test]
        public void A_candidate_whose_own_authority_no_longer_stands_among_several_is_answered_busy()
        {
            // Its word is null once its authority went or the operator kept an authority: there is
            // no decision left to wait for, so it is answered as what the gate last served with is,
            // and a connection made now is answered by what the store holds then.
            using (var candidate = new HeldBackTarget(_authority.DatabaseId, _authority.GateKey, _authority.Host, () => null))
            {
                Assert.That(ClientCredentials.For(null, candidate, null).UnavailableError, Is.EqualTo(HeldBackTarget.Busy));
                Assert.That(candidate.AdmissionBlocked, Is.EqualTo("the gate holds the database back"), "a write it turns away names why");
            }
        }

        [Test]
        public void What_the_gate_last_served_with_answers_the_store_s_word_over_a_real_handshake_and_busy_where_none_stands()
        {
            // `STORE.md` §656: where the reading finds no authority to answer with, the gate
            // answers from the host certificate and the gate key it last served with, and `lost`
            // is how a store with no key is answered.
            string word = StoreWords.Lost;
            using (var kept = new HeldBackTarget(_authority.DatabaseId, _gateKey, _authority.Host, () => word))
            using (var listener = new GateListener(
                       new GateOptions { Port = 0 }, new GateResolver(new FakeGateTargets(kept), _log), _handler, _log))
            {
                Assert.That(listener.Start(), Is.True);
                Assert.That(kept.Authority, Is.Null, "no client certificate is judged against it");
                Assert.That(kept.Vault, Is.Null, "and nothing of the database is reached");

                var reply = GateClient.Get(listener.Port, SniName.For(_gateKey), null, "/gate/status");
                Assert.That(reply.Status, Is.EqualTo(503), reply.Failure);
                Assert.That(reply.Body, Does.Contain("\"lost\""));

                word = null;
                Assert.That(GateClient.Get(listener.Port, SniName.For(_gateKey), null, "/gate/status").Body, Does.Contain("\"busy\""));
            }

            Assert.That(_handler.Calls, Is.Zero, "no route was reached");
        }

        [TestCase("split")]
        [TestCase("outdated")]
        [TestCase("stopped")]
        public void A_store_that_holds_the_gate_back_is_answered_before_which_authority_is_served(string word)
        {
            // `STORE.md` §656: which authority is served is asked only of a store that is whole.
            using (var target = new HeldBackTarget(_authority.DatabaseId, _authority.GateKey, _authority.Host, () => word))
            {
                Assert.That(ClientCredentials.For(null, target, null).UnavailableError, Is.EqualTo(word));
            }
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
