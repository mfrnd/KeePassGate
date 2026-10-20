using System;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the resolver does with the references it was handed, on every outcome.
    /// </summary>
    /// <remarks>
    /// **The accounting is the subject here, not the matching.** `IGateTargets.Current` hands out
    /// one reference per candidate, taken for this caller, and the resolver has to give back every
    /// one it is not returning. Getting that wrong is invisible from outside: the connection is
    /// refused exactly as it should be, nothing throws, nothing is logged, and the database's host
    /// key, authority certificate and decrypted contents simply stay in memory for the life of the
    /// process, once per connection attempt.
    /// </remarks>
    [TestFixture]
    public sealed class GateResolverTests
    {
        private TestAuthority _first;
        private TestAuthority _second;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _first = TestAuthority.New();
            _second = TestAuthority.New();
            _log = new RecordingGateLog();
        }

        [TearDown]
        public void TearDown()
        {
            _first.Dispose();
            _second.Dispose();
        }

        [Test]
        public void A_name_that_matches_nothing_gives_every_reference_back()
        {
            var targets = new FakeGateTargets(_first, _second);

            using (new GateResolver(targets, _log).Resolve(SniName.For(new byte[32])))
            {
            }

            Assert.That(_first.Holders, Is.EqualTo(1));
            Assert.That(_second.Holders, Is.EqualTo(1));
        }

        [Test]
        public void The_one_that_is_served_keeps_its_reference_until_the_resolution_is_disposed()
        {
            var targets = new FakeGateTargets(_first, _second);

            var resolution = new GateResolver(targets, _log).Resolve(_first.NewName());

            Assert.That(resolution.IsResolved, Is.True);
            Assert.That(_first.Holders, Is.EqualTo(2), "the connection is holding it");
            Assert.That(_second.Holders, Is.EqualTo(1), "and nothing else is");

            resolution.Dispose();

            Assert.That(_first.Holders, Is.EqualTo(1));
        }

        [Test]
        public void An_ambiguous_name_gives_back_the_one_it_matched_as_well()
        {
            // Two open databases sharing one authority, which is a file and a copy of it open
            // together. Every connection resolves ambiguous and is refused, and the reference the
            // matching candidate arrived with is the one that used to be kept and never handed out:
            // `GateResolution.Ambiguous` carries no target, so nothing could ever release it.
            var shared = _first.NewName();
            var targets = new FakeGateTargets(_first, _first);

            using (var resolution = new GateResolver(targets, _log).Resolve(shared))
            {
                Assert.That(resolution.Outcome, Is.EqualTo(GateOutcome.Ambiguous));
                Assert.That(resolution.Target, Is.Null);
            }

            Assert.That(_first.Holders, Is.EqualTo(1),
                "an ambiguous name is served by nobody, so it must hold nothing");
        }

        [Test]
        public void An_invitation_keeps_one_reference_and_releases_the_rest()
        {
            using (var book = new InvitationBook(_log))
            {
                var invitation = book.Invite(_first.DatabaseId);
                var targets = new FakeGateTargets(_first, _second);

                var resolution = new GateResolver(targets, book, _log).Resolve(invitation.NewName());

                Assert.That(resolution.IsPairing, Is.True);
                Assert.That(_first.Holders, Is.EqualTo(2));
                Assert.That(_second.Holders, Is.EqualTo(1));

                resolution.Dispose();

                Assert.That(_first.Holders, Is.EqualTo(1));
            }
        }

        [Test]
        public void An_invitation_to_a_database_open_twice_is_refused_as_ambiguous()
        {
            // A file and a copy of it open together hold one authority, so both hold the invited
            // database. The gate key's name is refused in that state, and an invitation's must be
            // too: pairing against whichever copy is listed first writes the record into that one
            // alone, where the other copy's next save can take it away.
            using (var book = new InvitationBook(_log))
            {
                var invitation = book.Invite(_first.DatabaseId);
                var targets = new FakeGateTargets(_first, _first);

                using (var resolution = new GateResolver(targets, book, _log).Resolve(invitation.NewName()))
                {
                    Assert.That(resolution.Outcome, Is.EqualTo(GateOutcome.Ambiguous));
                    Assert.That(resolution.IsPairing, Is.False);
                    Assert.That(resolution.Target, Is.Null);
                }

                Assert.That(_first.Holders, Is.EqualTo(1),
                    "a refused invitation is served by nobody, so it must hold nothing");
            }
        }

        [Test]
        public void An_invitation_naming_a_database_that_is_not_open_gives_everything_back()
        {
            using (var book = new InvitationBook(_log))
            {
                var invitation = book.Invite("a database nobody has open");
                var targets = new FakeGateTargets(_first);

                using (var resolution = new GateResolver(targets, book, _log).Resolve(invitation.NewName()))
                {
                    Assert.That(resolution.Outcome, Is.EqualTo(GateOutcome.NoMatch));
                }

                Assert.That(_first.Holders, Is.EqualTo(1));
            }
        }
    }
}
