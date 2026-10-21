using System;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A renewal through the whole transport: a real listener, a real TLS client presenting the
    /// certificate being renewed, and the operator played by a test.
    /// </summary>
    [TestFixture]
    public sealed class RenewalGateTests
    {
        private TestAuthority _authority;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _log = new RecordingGateLog();
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Dispose();

            foreach (var entry in _log.Entries)
            {
                TestContext.Out.WriteLine(entry.ToString());
            }
        }

        [Test]
        public void A_renewed_client_is_admitted_with_its_new_certificate_and_still_with_its_old_one()
        {
            using (var listener = Listening(FakeRenewalApproval.Approving(), new GateOptions { Port = 0 }))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var reply = Renew(listener, old);

                Assert.That(reply.Status, Is.EqualTo(200), reply.ToString());
                Assert.That(_authority.Admission(CertificateDigest.Of(old)), Is.Not.Null,
                    "the old record is left for the client to revoke");
                Assert.That(_log.Entries, Has.Some.Matches<RecordingGateLog.Entry>(
                    entry => entry.Message.StartsWith("renewed ", StringComparison.Ordinal)));
            }
        }

        [Test]
        public void The_operator_is_told_where_the_request_came_from()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var listener = Listening(operatorAt, new GateOptions { Port = 0 }))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                Renew(listener, old);

                Assert.That(operatorAt.LastRequest.Peer, Does.StartWith("127.0.0.1:"));
            }
        }

        [Test]
        public void An_operator_who_outlasts_the_ordinary_budget_still_renews()
        {
            // The budget is generous because the handshake is inside it and a loaded machine can
            // take seconds over one, and the operator outlasts it by two seconds while still
            // answering well inside the ten the client waits for a read.
            var deliberate = FakeRenewalApproval.Approving();
            deliberate.Thinks = TimeSpan.FromSeconds(7);

            using (var listener = Listening(deliberate, new GateOptions { Port = 0, Budget = TimeSpan.FromSeconds(5) }))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var reply = Renew(listener, old);

                Assert.That(reply.Status, Is.EqualTo(200), reply.ToString());
            }
        }

        [Test]
        public void A_renewal_is_measured_against_the_pairing_budget()
        {
            // It waits on a person, so what is left after its request is a list of waits somebody
            // wrote down, and the deadline is set to their sum as a pairing's is, rather than
            // dropped as an ordinary request's is. An operator slower than that sum loses the
            // client its answer, which is what the sum is for.
            //
            // The pairing budget is shorter than the ordinary one here, which nobody would
            // configure, so that each is generous in its own direction: the ordinary one leaves a
            // loaded machine seconds for the handshake inside it, and the pairing one runs out
            // five seconds before the operator answers, which still comes well inside the ten
            // the client waits for a read and so would be seen if it got through.
            var deliberate = FakeRenewalApproval.Approving();
            deliberate.Thinks = TimeSpan.FromSeconds(7);
            var options = new GateOptions
            {
                Port = 0,
                Budget = TimeSpan.FromSeconds(5),
                PairingBudget = TimeSpan.FromSeconds(2)
            };

            using (var listener = Listening(deliberate, options))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var reply = Renew(listener, old);

                Assert.That(deliberate.Asked, Is.EqualTo(1), "the request reached the operator");
                Assert.That(reply.Answered, Is.False, reply.ToString());
            }
        }

        [Test]
        public void A_lock_under_the_dialog_leaves_its_connection_to_say_the_database_is_locked()
        {
            // `PAIRING.md` §308. The lock closes every other connection and takes the question
            // away; this one used to be closed with the rest, and the client was told nothing.
            var operatorAt = new FakeRenewalApproval(RenewalApproval.Locked());

            using (var listener = Listening(operatorAt, new GateOptions { Port = 0 }))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                operatorAt.Meanwhile = () => listener.CloseAllButAwaiting("a database was locked");

                var reply = Renew(listener, old);

                Assert.That(reply.Status, Is.EqualTo(503), reply.ToString());
                Assert.That(reply.Body, Does.Contain("\"" + LockedTarget.Error + "\""));
            }
        }

        [Test]
        public void An_unlock_under_the_dialog_still_closes_its_connection()
        {
            var operatorAt = FakeRenewalApproval.Approving();

            using (var listener = Listening(operatorAt, new GateOptions { Port = 0 }))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                operatorAt.Meanwhile = () => listener.CloseAll("a database was unlocked");

                var reply = Renew(listener, old);

                Assert.That(reply.Answered, Is.False, reply.ToString());
            }
        }

        [Test]
        public void A_renewal_spelled_with_an_escape_is_measured_against_the_pairing_budget_too()
        {
            // The router decodes each segment before it compares, so this is served as a renewal;
            // the listener compared the path as it arrived and disarmed the deadline instead. The
            // numbers are the ones the test above chose, for the reasons it gives.
            var deliberate = FakeRenewalApproval.Approving();
            deliberate.Thinks = TimeSpan.FromSeconds(7);
            var options = new GateOptions
            {
                Port = 0,
                Budget = TimeSpan.FromSeconds(5),
                PairingBudget = TimeSpan.FromSeconds(2)
            };

            using (var listener = Listening(deliberate, options))
            using (var old = _authority.IssueAdmittedClient("reader"))
            {
                var name = _authority.NewName();
                var reply = GateClient.Send(
                    listener.Port,
                    name,
                    old,
                    "POST /%67ate/renew HTTP/1.1\r\nHost: " + name +
                    "\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}");

                Assert.That(deliberate.Asked, Is.EqualTo(1), "it is a renewal to the router");
                Assert.That(reply.Answered, Is.False, reply.ToString());
            }
        }

        private GateListener Listening(FakeRenewalApproval operatorAt, GateOptions options)
        {
            var renewal = new RenewalHandler(operatorAt, new PairingOptions(), new AuthorityIssuer(), null, null, null, _log);
            var listener = new GateListener(
                options,
                new GateResolver(new FakeGateTargets(_authority), _log),
                GateOperations.Router("9.9.9", null, renewal, new AccessCheck(NullGateAudit.Instance)),
                _log);

            Assert.That(listener.Start(), Is.True, "the listener did not start");
            return listener;
        }

        private GateReply Renew(GateListener listener, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
        {
            var name = _authority.NewName();
            return GateClient.Send(
                listener.Port,
                name,
                certificate,
                "POST " + RenewalHandler.Path + " HTTP/1.1\r\nHost: " + name +
                "\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}");
        }
    }
}
