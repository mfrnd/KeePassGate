using System;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The numbers that have to agree with each other, pinned so they cannot drift apart.
    /// </summary>
    [TestFixture]
    public sealed class GateOptionsTests
    {
        [Test]
        public void The_ordinary_budget_is_far_shorter_than_the_pairing_one()
        {
            var options = new GateOptions();

            // The ordinary budget exists to stop a peer holding a thread by feeding it slowly, and
            // it has to stay short for that. Pairing is the one exchange that is not slow but
            // blocked on a person, which is why it is measured separately rather than by raising
            // this for everybody.
            Assert.That(options.Budget, Is.LessThan(options.PairingBudget));
            Assert.That(options.Budget, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public void A_single_read_may_not_outlast_the_whole_connection()
        {
            var options = new GateOptions();

            Assert.That(options.Timeout, Is.LessThan(options.Budget));
        }
    }
}
