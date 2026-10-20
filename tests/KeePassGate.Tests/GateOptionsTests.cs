using System;
using KeePassGate.Bridge;
using KeePassGate.Pairing;
using KeePassGate.Transport;
using KeePassGate.Ui;
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
        public void The_pairing_budget_outlasts_everything_a_pairing_waits_for()
        {
            var options = new GateOptions();
            var pairing = new PairingOptions();

            // Every wait a pairing connection can be sitting in, added up: the operator reading the
            // dialog, the prompt waiting a little longer than the dialog does, the queue for the one
            // writer, and the marshaled write and save themselves.
            var waits = pairing.ApprovalTimeout
                        + PairingApprovalPrompt.Grace
                        + AdmissionWriter.LockTimeout
                        + AdmissionWriter.WriteTimeout;

            Assert.That(options.PairingBudget, Is.GreaterThan(waits),
                "the connection budget closes the socket while the operator is still reading, and " +
                "everything after that point still happens: the invitation is spent and the record " +
                "is saved, with nothing left to hand the certificate back on.");
        }

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
