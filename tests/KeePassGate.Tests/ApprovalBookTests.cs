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
        }

        [Test]
        public void An_approval_is_used_once()
        {
            var held = Hold("key");

            Assert.That(_book.Take(held), Is.True);
            Assert.That(_book.Take(held), Is.False);
            Assert.That(_book.Holds(held), Is.False);
        }

        [Test]
        public void A_lock_or_a_close_lets_go_of_every_approval_and_says_so_on_each()
        {
            var one = Hold("one");
            ((IApprovalScreen)_book).Locked();

            var two = Hold("two");
            ((IApprovalScreen)_book).Closed();

            Assert.That(_book.Count, Is.Zero);
            Assert.That(one.Cleared && two.Cleared, Is.True);
            Assert.That(_book.Take(one), Is.False);
        }

        [Test]
        public void Holding_the_same_request_again_replaces_what_was_held()
        {
            var first = Hold("key");
            var second = Hold("key");

            Assert.That(_book.Holds(first), Is.False);
            Assert.That(_book.Find("key", "db"), Is.SameAs(second));
        }

        private HeldApproval Hold(string key)
        {
            return _book.Hold(key, "db", new[] { "ops" }, TimeSpan.FromDays(30), new[] { "ops" }, null, _now.AddMinutes(6));
        }
    }
}
