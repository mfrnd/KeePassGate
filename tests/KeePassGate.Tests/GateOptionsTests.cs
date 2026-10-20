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
    /// <remarks>
    /// **The ordinary budget is not one of them**, and a test here used to say it was: it added
    /// `DatabaseWriter.LockTimeout` to `DatabaseWriter.WriteTimeout` and asserted that `Budget`
    /// exceeded the sum, as though that sum bounded a writing request. It does not, because the
    /// second of those bounds the wait for the marshaled call to start rather than the call. The
    /// relation that replaced it is not between two constants at all: the deadline is dropped once
    /// the request is read, and
    /// `GateListenerTests.A_write_that_outlasts_the_budget_is_still_answered` is where that is
    /// measured over a real socket.
    /// </remarks>
    [TestFixture]
    public sealed class GateOptionsTests
    {
        [Test]
        public void The_pairing_budget_outlasts_everything_a_pairing_waits_for()
        {
            var options = new GateOptions();
            var pairing = new PairingOptions();

            // Every wait a pairing connection can be sitting in, added up. The screen is the one
            // that was missing: `PairingApprovalPrompt` puts one pairing in front of the operator
            // at a time, and a second waits for it for as long as it was going to give the operator
            // anyway. Taking the screen just inside that wait leaves the whole dialog still ahead,
            // so the two are consecutive rather than alternatives.
            var forTheScreen = pairing.ApprovalTimeout;
            var forTheDialog = pairing.ApprovalTimeout + PairingApprovalPrompt.Grace;
            var forKeePassToSettle = pairing.ApprovalSettle;
            var forTheWrite = DatabaseWriter.LockTimeout + DatabaseWriter.WriteTimeout;

            var waits = forTheScreen + forTheDialog + forKeePassToSettle + forTheWrite;

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
            // it has to stay short for that. Pairing and renewal are the two exchanges that are not
            // slow but blocked on a person, which is why they are measured separately rather than
            // by raising this for everybody.
            Assert.That(options.Budget, Is.LessThan(options.PairingBudget));
            Assert.That(options.Budget, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(2)));
        }

        [Test]
        public void A_single_read_may_not_outlast_the_whole_connection()
        {
            var options = new GateOptions();

            Assert.That(options.Timeout, Is.LessThan(options.Budget));
        }

        [Test]
        public void A_port_the_operator_chose_is_taken_whatever_the_environment_holds()
        {
            string refused;

            Assert.That(GateOptions.PortToTake(52000, null, out refused), Is.EqualTo(52000));
            Assert.That(refused, Is.Null);

            // The variable is read only when nothing is chosen, so even one that names nothing a
            // gate could take stops nothing here.
            Assert.That(GateOptions.PortToTake(52000, "80", out refused), Is.EqualTo(52000));
            Assert.That(refused, Is.Null);
        }

        [Test]
        public void With_nothing_chosen_the_environment_names_the_port_and_otherwise_the_default_does()
        {
            string refused;

            Assert.That(GateOptions.PortToTake(0, "51234", out refused), Is.EqualTo(51234));
            Assert.That(refused, Is.Null);

            Assert.That(GateOptions.PortToTake(0, null, out refused), Is.EqualTo(GateOptions.DefaultPort));
            Assert.That(refused, Is.Null);

            Assert.That(GateOptions.PortToTake(0, "1024", out refused), Is.EqualTo(1024));
            Assert.That(GateOptions.PortToTake(0, "65535", out refused), Is.EqualTo(65535));
        }

        [Test]
        [TestCase("80")]
        [TestCase("0")]
        [TestCase("1023")]
        [TestCase("65536")]
        [TestCase("99999999999")]
        [TestCase("-51234")]
        [TestCase("+51234")]
        [TestCase(" 51234")]
        [TestCase("51234 ")]
        [TestCase("51,234")]
        [TestCase("5123x")]
        [TestCase("port")]
        [TestCase("")]
        public void A_variable_set_to_anything_but_a_port_stops_the_gate_rather_than_being_passed_over(string named)
        {
            // The operator's ruling on K004: the default is not taken in its place, because a gate
            // that looks healthy on a port nobody named hides the mistake from everybody but the
            // client that cannot connect.
            string refused;
            var port = GateOptions.PortToTake(0, named, out refused);

            Assert.That(port, Is.Zero, "a value that is not a port was turned into one");
            Assert.That(refused, Is.Not.Null.And.Not.Empty);
            Assert.That(refused, Does.Contain(GateOptions.PortVariable), "the reason does not name the variable");
            Assert.That(refused, Does.Contain("1024").And.Contain("65535"), "the reason does not give the range");
            Assert.That(refused, Does.Contain("not listening"), "the reason does not say what it costs");

            if (named.Length == 0)
            {
                Assert.That(refused, Does.Contain("set but empty"));
            }
            else
            {
                Assert.That(refused, Does.Contain("\"" + named + "\""), "the reason does not quote the value it refused");
            }
        }

        [Test]
        public void A_refused_value_is_quoted_on_one_line_and_cut_to_what_a_port_could_be()
        {
            string refused;

            GateOptions.PortToTake(0, "51\n234", out refused);
            Assert.That(refused, Does.Contain("\"51 234\""));
            Assert.That(refused, Does.Not.Contain("\n"));

            GateOptions.PortToTake(0, new string('9', 200), out refused);
            Assert.That(refused, Does.Contain("\"" + new string('9', 32) + "...\""));
        }

        [Test]
        [NonParallelizable]
        public void The_variable_is_read_as_it_was_set()
        {
            var before = Environment.GetEnvironmentVariable(GateOptions.PortVariable);

            try
            {
                Environment.SetEnvironmentVariable(GateOptions.PortVariable, "51234");
                Assert.That(GateOptions.PortFromEnvironment(), Is.EqualTo("51234"));

                Environment.SetEnvironmentVariable(GateOptions.PortVariable, null);
                Assert.That(GateOptions.PortFromEnvironment(), Is.Null);
            }
            finally
            {
                Environment.SetEnvironmentVariable(GateOptions.PortVariable, before);
            }
        }
    }
}
