using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The whole life of an invitation, which `PAIRING.md` §306 draws as a state machine and
    /// which this fixture walks every edge of.
    /// </summary>
    /// <remarks>
    /// Three properties matter more than the rest, and each one is a defect if it stops holding.
    /// A burn cannot be undone, so a single-use invitation really is single use. A timeout does not
    /// burn, so an absent operator does not cost the invitee its invitation. And an expired
    /// invitation is gone before anything is compared against it, so the gate cannot tell an
    /// attacker that one used to exist.
    /// </remarks>
    [TestFixture]
    public sealed class InvitationBookTests
    {
        private const string Database = "aaaabbbbccccdddd";

        private DateTimeOffset _now;
        private RecordingGateLog _log;
        private InvitationBook _book;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
            _log = new RecordingGateLog();
            _book = new InvitationBook(TimeSpan.FromMinutes(10), () => _now, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _book.Dispose();
        }

        [Test]
        public void An_invitation_carries_a_token_and_the_database_it_is_for()
        {
            var invitation = _book.Invite(Database);

            Assert.That(invitation, Is.Not.Null);
            Assert.That(invitation.DatabaseId, Is.EqualTo(Database));
            Assert.That(invitation.Token.Length, Is.EqualTo(PairingToken.Length));

            // Named, and named independently of the code: the identifier is shown twice, on the
            // tab and on the approval dialog, and anything the code could be recovered from is not
            // a thing to be showing twice. TODO(step 8): the tab's invitation control and the
            // approval dialog arrive with this step.
            Assert.That(invitation.Id, Is.Not.Empty);
            Assert.That(invitation.Text, Does.Not.Contain(invitation.Id));
            Assert.That(invitation.Expires, Is.EqualTo(_now.AddMinutes(10)));
        }

        [Test]
        public void The_token_it_hands_out_is_a_copy_that_a_caller_may_destroy()
        {
            var invitation = _book.Invite(Database);

            var taken = invitation.Token;
            Array.Clear(taken, 0, taken.Length);

            Assert.That(invitation.Token, Is.Not.EqualTo(taken), "zeroing a copy emptied the invitation");
        }

        [Test]
        public void A_name_built_from_the_token_resolves_to_that_invitation()
        {
            var invitation = _book.Invite(Database);

            var matched = _book.Matching(invitation.NewName());

            Assert.That(matched.Count, Is.EqualTo(1));
            Assert.That(matched[0].Id, Is.EqualTo(invitation.Id));
        }

        [Test]
        public void A_name_built_from_something_else_resolves_to_nothing()
        {
            _book.Invite(Database);

            Assert.That(_book.Matching(SniName.For(new byte[32])), Is.Empty);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("not.a.name")]
        [TestCase("kpg")]
        public void Nothing_shaped_wrongly_resolves_to_anything(string name)
        {
            _book.Invite(Database);

            Assert.That(_book.Matching(name), Is.Empty);
        }

        [Test]
        public void An_expired_invitation_is_gone_rather_than_refused()
        {
            var invitation = _book.Invite(Database);
            var name = invitation.NewName();

            _now = _now.AddMinutes(11);

            Assert.That(_book.Matching(name), Is.Empty);
            Assert.That(_book.Outstanding(), Is.Empty, "it is dropped, not kept and refused");
        }

        [Test]
        public void An_invitation_is_still_good_the_moment_before_it_runs_out()
        {
            var invitation = _book.Invite(Database);
            var name = invitation.NewName();

            _now = _now.AddMinutes(10).AddSeconds(-1);

            Assert.That(_book.Matching(name), Is.Not.Empty);
        }

        [Test]
        public void Expiry_zeroes_the_token_rather_than_leaving_it_about()
        {
            var invitation = _book.Invite(Database);

            _now = _now.AddMinutes(11);
            _book.Outstanding();

            Assert.That(invitation.Token, Is.Null);
            Assert.That(invitation.Text, Is.Empty);
        }

        [Test]
        public void Burning_one_spends_it_and_burning_it_again_does_nothing()
        {
            var invitation = _book.Invite(Database);

            Assert.That(_book.Burn(invitation.Id), Is.True);
            Assert.That(_book.Burn(invitation.Id), Is.False, "a spent invitation cannot be spent twice");
            Assert.That(_book.Outstanding(), Is.Empty);
        }

        [Test]
        public void One_that_ran_out_cannot_be_spent_whether_or_not_anything_swept_the_book()
        {
            // The case an approval dialog open across the expiry reaches. Nothing reads the book
            // between the two lines below, so if spending did not sweep, whether the approval
            // landed would depend on whether another connection or a redraw of the tab had.
            // TODO(step 8): the approval dialog arrives with operator approval.
            var invitation = _book.Invite(Database);

            _now = _now.AddMinutes(11);

            Assert.That(_book.Burn(invitation.Id), Is.False, "it ran out, so there is nothing to spend");
            Assert.That(invitation.Token, Is.Null, "and it went the way expiry always goes");
            Assert.That(_log.Entries.Any(e => e.Message.Contains("expired unused")), Is.True);
        }

        [Test]
        public void A_burned_invitation_answers_to_nothing_afterwards()
        {
            var invitation = _book.Invite(Database);
            var name = invitation.NewName();

            _book.Burn(invitation.Id);

            Assert.That(_book.Matching(name), Is.Empty);
        }

        [Test]
        public void Only_one_of_two_threads_racing_to_spend_one_wins()
        {
            // The property the whole single-use claim rests on. A lock in memory is what provides
            // it, which is exactly what custom data could never have provided.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var invitation = _book.Invite(Database);
                var won = 0;
                var start = new ManualResetEventSlim(false);

                var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
                {
                    start.Wait();
                    if (_book.Burn(invitation.Id))
                    {
                        Interlocked.Increment(ref won);
                    }
                })).ToList();

                threads.ForEach(t => t.Start());
                start.Set();
                threads.ForEach(t => t.Join());

                Assert.That(won, Is.EqualTo(1), "more than one caller spent the same invitation");
            }
        }

        [Test]
        public void Burning_something_that_was_never_there_is_not_an_error()
        {
            Assert.That(_book.Burn("nothing"), Is.False);
            Assert.That(_book.Burn(null), Is.False);
        }

        [Test]
        public void Only_the_invitation_for_one_database_is_its_own()
        {
            _book.Invite(Database);
            _book.Invite("some other database");

            Assert.That(_book.For(Database).Count, Is.EqualTo(1));
            Assert.That(_book.For("some other database").Count, Is.EqualTo(1));
            Assert.That(_book.For("a third one"), Is.Empty);
        }

        [Test]
        public void A_new_invitation_replaces_the_one_that_database_had()
        {
            // One code at a time. Two live codes for one database is a state nobody can reason
            // about: which one is somebody about to use, and which one was sent yesterday.
            var first = _book.Invite(Database);
            var second = _book.Invite(Database);

            Assert.That(_book.For(Database).Count, Is.EqualTo(InvitationBook.MaxPerDatabase));
            Assert.That(_book.For(Database)[0].Id, Is.EqualTo(second.Id));
            Assert.That(first.Token, Is.Null, "the replaced code was left in memory");
        }

        [Test]
        public void The_replaced_invitation_stops_working_at_once()
        {
            var first = _book.Invite(Database);
            var id = first.Id;

            _book.Invite(Database);

            Assert.That(_book.Burn(id), Is.False, "a replaced code is gone rather than merely hidden");
        }

        [Test]
        public void Replacing_one_database_s_invitation_leaves_another_s_alone()
        {
            var other = _book.Invite("some other database");

            _book.Invite(Database);
            _book.Invite(Database);

            Assert.That(_book.For("some other database").Count, Is.EqualTo(1));
            Assert.That(_book.For("some other database")[0].Id, Is.EqualTo(other.Id));
        }

        [Test]
        public void Everything_goes_when_KeePass_does()
        {
            var invitation = _book.Invite(Database);

            _book.Clear();

            Assert.That(_book.Outstanding(), Is.Empty);
            Assert.That(invitation.Token, Is.Null, "the token was left in memory");
        }

        [Test]
        public void An_invitation_needs_a_database_to_be_for()
        {
            Assert.Throws<ArgumentException>(() => _book.Invite(null));
            Assert.Throws<ArgumentException>(() => _book.Invite(string.Empty));
        }

        [Test]
        public void A_disposed_book_hands_out_nothing_more()
        {
            _book.Dispose();

            Assert.That(_book.Invite(Database), Is.Null);
        }

        [Test]
        public void Every_invitation_is_named_differently()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 16; i++)
            {
                Assert.That(ids.Add(_book.Invite(Database).Id), Is.True);
            }
        }
    }

    /// <summary>
    /// The challenge an invitation carries between the two halves of the proof exchange, which is
    /// what lets the exchange span two connections on a transport that serves one request each.
    /// </summary>
    [TestFixture]
    public sealed class InvitationChallengeTests
    {
        private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

        private DateTimeOffset _now;
        private Invitation _invitation;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
            _invitation = new Invitation(
                "abcdef01", "database", PairingToken.New(), _now, _now.AddMinutes(10));
        }

        [TearDown]
        public void TearDown()
        {
            _invitation.Dispose();
        }

        [Test]
        public void There_is_no_challenge_until_a_hello_asks_for_one()
        {
            Assert.That(_invitation.TakeChallenge(_now), Is.Null);
        }

        [Test]
        public void A_challenge_carries_the_connection_it_was_issued_on()
        {
            var binding = new byte[] { 1, 2, 3 };
            var nonce = new byte[] { 4, 5, 6 };

            var issued = _invitation.Challenge(binding, nonce, _now, Minute);

            Assert.That(issued.Value.Length, Is.EqualTo(PairingChallenge.Length));
            Assert.That(issued.Binding, Is.EqualTo(binding));
            Assert.That(issued.Nonce, Is.EqualTo(nonce));
            Assert.That(issued.Expires, Is.EqualTo(_now + Minute));
        }

        [Test]
        public void Taking_it_spends_it()
        {
            _invitation.Challenge(new byte[1], new byte[1], _now, Minute);

            Assert.That(_invitation.TakeChallenge(_now), Is.Not.Null);
            Assert.That(_invitation.TakeChallenge(_now), Is.Null,
                "a challenge that can be used twice is a request that can be replayed");
        }

        [Test]
        public void A_second_hello_replaces_the_first()
        {
            var first = _invitation.Challenge(new byte[1], new byte[1], _now, Minute);
            var second = _invitation.Challenge(new byte[1], new byte[1], _now, Minute);

            var taken = _invitation.TakeChallenge(_now);

            Assert.That(taken.Value, Is.EqualTo(second.Value));
            Assert.That(taken.Value, Is.Not.EqualTo(first.Value));
        }

        [Test]
        public void An_expired_challenge_is_not_handed_over()
        {
            _invitation.Challenge(new byte[1], new byte[1], _now, Minute);

            Assert.That(_invitation.TakeChallenge(_now.AddMinutes(2)), Is.Null);
        }

        [Test]
        public void Two_challenges_are_never_the_same()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 100; i++)
            {
                var issued = _invitation.Challenge(new byte[1], new byte[1], _now, Minute);
                Assert.That(seen.Add(Convert.ToBase64String(issued.Value)), Is.True);
            }
        }

        [Test]
        public void A_disposed_invitation_issues_none()
        {
            _invitation.Dispose();

            Assert.That(_invitation.Challenge(new byte[1], new byte[1], _now, Minute), Is.Null);
        }

        [Test]
        public void A_challenge_hands_out_copies_rather_than_what_it_holds()
        {
            var issued = _invitation.Challenge(new byte[] { 9 }, new byte[] { 8 }, _now, Minute);

            var taken = issued.Value;
            Array.Clear(taken, 0, taken.Length);

            Assert.That(issued.Value, Is.Not.EqualTo(taken));
        }
    }
}
