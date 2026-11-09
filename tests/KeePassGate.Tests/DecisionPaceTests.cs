using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Authority;
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
        public void A_paced_line_is_stamped_when_it_was_decided_rather_than_when_its_wait_ended()
        {
            // `at` is when the rules answered. Read after the wait, a paced client's lines were
            // stamped up to seconds late, and a paced client is the client they are read about.
            var audit = new RecordingAudit();
            var started = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
            var check = new AccessCheck(audit, () => started + _now, _pace);
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var client = Client("reader");

            for (var i = 0; i < DecisionPace.Burst; i++)
            {
                check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            var decided = started + _now;
            check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            Assume.That(_waits, Has.Count.EqualTo(1), "the decision past the burst waited");

            var refused = started + _now;
            check.Admissions.RenewalRefusedWithoutAsking(
                "d1e2f3", client.AdmissionKey, client.ClientKey, RenewalRefusal.Forever());
            Assume.That(_waits, Has.Count.EqualTo(2), "and so did the refused renewal");

            var lines = audit.Entries;
            Assert.That(lines[lines.Count - 2].At, Is.EqualTo(decided));
            Assert.That(lines[lines.Count - 1].At, Is.EqualTo(refused));
        }

        [Test]
        public void Nothing_is_paced_for_an_audit_that_keeps_nothing()
        {
            // The pace keeps a client from rolling a bounded file away, and a machine where no
            // audit file could be named has none: a client held to it there is slowed for nothing.
            var check = new AccessCheck(new JsonLinesAudit(null, null), null, _pace);
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var client = Client("reader");

            for (var i = 0; i < DecisionPace.Burst + 3; i++)
            {
                check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            check.Admissions.RenewalRefusedWithoutAsking(
                "d1e2f3", client.AdmissionKey, client.ClientKey, RenewalRefusal.Forever());
            check.Admissions.TakeTurn(client.ClientKey);

            Assert.That(_waits, Is.Empty);
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
