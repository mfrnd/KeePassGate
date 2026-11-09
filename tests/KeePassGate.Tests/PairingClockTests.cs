using System;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What ends in pairing ends by elapsed time, whatever the system clock is set to meanwhile,
    /// and what is shown and sent stays the wall-clock time read when each thing was made.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-11-08. Each of these sets the wall clock back an hour, or
    /// forward an hour, while elapsed time moves a few minutes, which is what an operator
    /// correcting a wrong clock, a machine restored from a snapshot or a time service stepping does.
    /// Measured against the wall clock, a step back kept an invitation, a challenge and a held
    /// approval alive for the hour, and a step forward ended each at once.
    /// </remarks>
    [TestFixture]
    public sealed class PairingClockTests
    {
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        private DateTimeOffset _wall;
        private TimeSpan _elapsed;
        private PairingClock _clock;

        [SetUp]
        public void SetUp()
        {
            _wall = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
            _elapsed = TimeSpan.FromHours(3);
            _clock = new PairingClock(() => _wall, () => _elapsed);
        }

        [Test]
        public void An_invitation_runs_out_on_time_when_the_clock_is_set_back()
        {
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), new RecordingGateLog(), _clock))
            {
                var invitation = book.Invite("aaaabbbbccccdddd");
                Assert.That(invitation.Expires, Is.EqualTo(_wall.AddMinutes(10)), "shown as the clock read it");

                _wall -= Hour;
                _elapsed += TimeSpan.FromMinutes(11);

                Assert.That(book.Outstanding(), Is.Empty);
            }
        }

        [Test]
        public void An_invitation_lasts_its_time_when_the_clock_is_set_forward()
        {
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), new RecordingGateLog(), _clock))
            {
                book.Invite("aaaabbbbccccdddd");

                _wall += Hour;
                _elapsed += TimeSpan.FromMinutes(1);

                Assert.That(book.Outstanding(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void A_challenge_ends_by_elapsed_time_either_way_the_clock_moves()
        {
            using (var invitation = new Invitation(
                "abcdef01", "aaaabbbbccccdddd", PairingToken.New(), _clock, TimeSpan.FromMinutes(10)))
            {
                var issued = invitation.Challenge(new byte[1], new byte[1], TimeSpan.FromMinutes(2));
                Assert.That(issued.Expires, Is.EqualTo(_wall.AddMinutes(2)), "sent as the clock read it");

                _wall += Hour;
                _elapsed += TimeSpan.FromMinutes(1);
                Assert.That(invitation.TakeChallenge(), Is.Not.Null, "a clock set forward does not end it");

                invitation.Challenge(new byte[1], new byte[1], TimeSpan.FromMinutes(2));
                _wall -= Hour + Hour;
                _elapsed += TimeSpan.FromMinutes(3);
                Assert.That(invitation.TakeChallenge(), Is.Null, "and one set back does not keep it");
            }
        }

        [Test]
        public void A_held_approval_ends_with_its_budget_either_way_the_clock_moves()
        {
            var book = new ApprovalBook(_clock);
            var until = _clock.Read().After(TimeSpan.FromMinutes(6));
            var held = book.Hold(
                "key", "db", new[] { "ops" }, TimeSpan.FromDays(30), new[] { "ops" }, null, until, book.Mark());
            Assert.That(held.Until, Is.EqualTo(_wall.AddMinutes(6)), "told to the client as the clock read it");

            _wall += Hour;
            _elapsed += TimeSpan.FromMinutes(1);
            Assert.That(book.Find("key", "db"), Is.SameAs(held), "a clock set forward does not end it");

            _wall -= Hour + Hour;
            _elapsed += TimeSpan.FromMinutes(6);
            Assert.That(book.Find("key", "db"), Is.Null, "and one set back does not keep it writable");
        }

        [Test]
        public void A_clock_given_as_a_wall_clock_alone_moves_its_elapsed_time_with_it()
        {
            // What a test that moves a wall clock relies on, and what makes two parts handed the
            // same wall clock agree about when a thing they share ends.
            var wall = _wall;
            var first = PairingClock.For(() => wall);
            var second = PairingClock.For(() => wall);
            var ends = first.Read().After(TimeSpan.FromMinutes(5));

            wall = wall.AddMinutes(4);
            Assert.That(second.HasReached(ends), Is.False);

            wall = wall.AddMinutes(1);
            Assert.That(second.HasReached(ends), Is.True);
            Assert.That(PairingClock.For(null), Is.SameAs(PairingClock.Default));
        }
    }
}
