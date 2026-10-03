using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// How fast one client's decisions are written down, which is how fast it can roll the audit.
    /// </summary>
    /// <remarks>
    /// Driven by a clock and a wait of the test's own, so nothing here sleeps: a wait is recorded
    /// and the clock is moved by it, which is what a real one would have done.
    /// </remarks>
    [TestFixture]
    public sealed class DecisionPaceTests
    {
        private TimeSpan _now;
        private List<TimeSpan> _waits;
        private DecisionPace _pace;

        [SetUp]
        public void SetUp()
        {
            _now = TimeSpan.FromMinutes(1);
            _waits = new List<TimeSpan>();
            _pace = new DecisionPace(() => _now, owed =>
            {
                _waits.Add(owed);
                _now += owed;
            });
        }

        [Test]
        public void A_burst_is_written_down_without_waiting()
        {
            Take("a client", DecisionPace.Burst);

            Assert.That(_waits, Is.Empty);
        }

        [Test]
        public void Past_the_burst_a_client_is_held_to_the_rate()
        {
            Take("a client", DecisionPace.Burst + 50);

            Assert.That(_waits, Has.Count.EqualTo(50));
            Assert.That(_now - TimeSpan.FromMinutes(1),
                Is.EqualTo(TimeSpan.FromSeconds(50.0 / DecisionPace.PerSecond)).Within(TimeSpan.FromMilliseconds(1)),
                "fifty decisions past the burst cost what fifty turns at the rate cost");
        }

        [Test]
        public void One_client_spending_its_allowance_leaves_another_alone()
        {
            Take("a flood", DecisionPace.Burst + 50);
            _waits.Clear();

            Take("somebody else", 10);

            Assert.That(_waits, Is.Empty);
        }

        [Test]
        public void An_allowance_comes_back_with_time()
        {
            Take("a client", DecisionPace.Burst);
            _now += TimeSpan.FromSeconds(10);

            Take("a client", 10 * DecisionPace.PerSecond);

            Assert.That(_waits, Is.Empty);
        }

        [Test]
        public void Every_decision_the_access_check_writes_down_is_paced()
        {
            var audit = new RecordingAudit();
            var check = new AccessCheck(audit, null, _pace);
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var client = Client("reader");

            for (var i = 0; i < DecisionPace.Burst + 3; i++)
            {
                check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, i % 2 == 0 ? entry : null);
            }

            Assert.That(_waits, Has.Count.EqualTo(3), "a permitted read and an absent node alike");
            Assert.That(audit.Entries, Has.Count.EqualTo(DecisionPace.Burst + 3), "and none is dropped");
        }

        [Test]
        public void A_decision_with_no_admission_key_is_not_paced()
        {
            Take(null, DecisionPace.Burst + 5);

            Assert.That(_waits, Is.Empty);
        }

        private void Take(string admissionKey, int times)
        {
            for (var i = 0; i < times; i++)
            {
                _pace.Take(admissionKey);
            }
        }

        private static ClientIdentity Client(params string[] profiles)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, profiles);
            }
        }
    }
}
