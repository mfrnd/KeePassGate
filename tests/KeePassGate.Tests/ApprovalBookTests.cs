using System;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The approvals held for a retry: what one is keyed by, how long it lasts, and what lets go of
    /// it.
    /// </summary>
    [TestFixture]
    public sealed class ApprovalBookTests
    {
        private DateTimeOffset _now;
        private ApprovalBook _book;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
            _book = new ApprovalBook(() => _now);
        }

        [Test]
        public void A_request_differing_in_anything_is_another_request()
        {
            var asked = ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops" }, "P30D");

            Assert.That(ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops" }, "P30D"), Is.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-2", "agent-fictitious", new[] { "ops" }, "P30D"), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-1", "agent-other", new[] { "ops" }, "P30D"), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops", "audit" }, "P30D"), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops" }, null), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Renewal("inv-1", "P30D"), Is.Not.EqualTo(asked));

            // Each part carries its length, so moving a character from one part to the next is not
            // the same request either.
            Assert.That(
                ApprovalBook.Pairing("ab", "c", Array.Empty<string>(), null),
                Is.Not.EqualTo(ApprovalBook.Pairing("a", "bc", Array.Empty<string>(), null)));
        }

        [Test]
        public void An_approval_is_found_for_its_own_database_until_its_time_is_up()
        {
            var held = Hold("key");

            Assert.That(_book.Find("key", "db"), Is.SameAs(held));
            Assert.That(_book.Find("key", "another db"), Is.Null);

            _now = held.Until;
            Assert.That(_book.Find("key", "db"), Is.Null);
            Assert.That(held.Cleared, Is.False, "running out is not being let go");
            Assert.That(held.Taken, Is.False, "nor being taken");
        }

        [Test]
        public void An_approval_is_used_once()
        {
            var held = Hold("key");

            Assert.That(_book.Take(held), Is.True);
            Assert.That(_book.Take(held), Is.False);
            Assert.That(_book.Holds(held), Is.False);
            Assert.That(held.Taken, Is.True, "so a request that finds it gone can tell why");
            Assert.That(held.Cleared, Is.False);
        }

        [Test]
        public void A_lock_lets_go_of_every_approval_and_says_so_on_each()
        {
            var one = Hold("one");
            var two = Hold("two", "another db");

            ((IApprovalScreen)_book).Locked();

            Assert.That(_book.Count, Is.Zero);
            Assert.That(one.Cleared && two.Cleared, Is.True);
            Assert.That(_book.Take(one), Is.False);
        }

        [Test]
        public void A_close_lets_go_of_its_own_database_s_approvals_and_no_others()
        {
            // The operator's ruling R1. It let every approval go, so closing a database told a
            // pairing approved for another database that its database was locked.
            var closing = Hold("one");
            var open = Hold("two", "another db");

            ((IApprovalScreen)_book).Closed(new[] { "db" });

            Assert.That(closing.Cleared, Is.True);
            Assert.That(_book.Holds(closing), Is.False);
            Assert.That(open.Cleared, Is.False);
            Assert.That(_book.Find("two", "another db"), Is.SameAs(open));
        }

        [Test]
        public void A_close_that_names_no_database_lets_go_of_every_approval()
        {
            var one = Hold("one");
            var two = Hold("two", "another db");

            ((IApprovalScreen)_book).Closed(null);

            Assert.That(_book.Count, Is.Zero);
            Assert.That(one.Cleared && two.Cleared, Is.True);
        }

        [Test]
        public void Holding_the_same_request_again_replaces_what_was_held()
        {
            var first = Hold("key");
            var second = Hold("key");

            Assert.That(_book.Holds(first), Is.False);
            Assert.That(_book.Find("key", "db"), Is.SameAs(second));
            Assert.That(first.Taken, Is.True, "the same request approved again took its place");
        }

        [Test]
        public void An_answer_a_lock_overtook_is_let_go_as_it_arrives()
        {
            // S083: the operator answered, and KeePass locked before the answer was held. The
            // book the lock had emptied took it all the same.
            var mark = _book.Mark();
            ((IApprovalScreen)_book).Locked();

            var held = Hold("key", "db", mark);

            Assert.That(held.Cleared, Is.True, "answered as the lock it is");
            Assert.That(_book.Count, Is.Zero);
            Assert.That(_book.Find("key", "db"), Is.Null);
        }

        [Test]
        public void An_answer_given_after_its_database_closed_is_let_go_and_another_database_s_is_held()
        {
            // D005 and D017 for the database that closed, and the operator's ruling R1 for every
            // other: a close takes its own database's approvals and no others.
            var mark = _book.Mark();
            ((IApprovalScreen)_book).Closed(new[] { "db" });

            var closed = Hold("one", "db", mark);
            var open = Hold("two", "another db", mark);

            Assert.That(closed.Cleared, Is.True);
            Assert.That(_book.Find("one", "db"), Is.Null);
            Assert.That(open.Cleared, Is.False);
            Assert.That(_book.Find("two", "another db"), Is.SameAs(open));
        }

        [Test]
        public void A_question_asked_after_a_clear_is_held_as_any_other()
        {
            ((IApprovalScreen)_book).Locked();
            ((IApprovalScreen)_book).Closed(new[] { "db" });
            var mark = _book.Mark();

            var held = Hold("key", "db", mark);

            Assert.That(held.Cleared, Is.False);
            Assert.That(_book.Find("key", "db"), Is.SameAs(held));
        }

        [Test]
        public void An_answer_let_go_as_it_arrives_leaves_what_the_book_already_held()
        {
            // A later question for the same request, asked after the clear, was held under the same
            // key: the answer the clear overtook takes nothing from it.
            var before = _book.Mark();
            ((IApprovalScreen)_book).Locked();
            var later = Hold("key");

            Hold("key", "db", before);

            Assert.That(_book.Find("key", "db"), Is.SameAs(later));
        }

        private HeldApproval Hold(string key, string databaseId = "db")
        {
            return Hold(key, databaseId, _book.Mark());
        }

        private HeldApproval Hold(string key, string databaseId, long mark)
        {
            return _book.Hold(
                key, databaseId, new[] { "ops" }, TimeSpan.FromDays(30), new[] { "ops" }, null,
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), mark);
        }
    }
}
