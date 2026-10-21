using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What happens to a database's admissions, as the audit records it: pairings, renewals and
    /// revocations, in the words and at the pace each is written with.
    /// </summary>
    /// <remarks>
    /// Every key and invitation here is invented. What the handlers write, and when, is asserted
    /// beside each handler; this is about the lines themselves.
    /// </remarks>
    [TestFixture]
    public sealed class AdmissionAuditTests
    {
        private const string Database = "d1e2f3";
        private const string Key = "9f86d081";
        private const string ClientKey = "c0ffee11";
        private const string Issued = "3a9f5c";

        private static readonly DateTimeOffset At = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private RecordingAudit _audit;
        private List<TimeSpan> _waits;
        private AccessCheck _check;

        [SetUp]
        public void SetUp()
        {
            _audit = new RecordingAudit();
            _waits = new List<TimeSpan>();
            _check = new AccessCheck(_audit, () => At, new DecisionPace(() => TimeSpan.Zero, _waits.Add));
        }

        [Test]
        public void A_pairing_is_written_as_the_operator_answered_it_and_names_its_invitation()
        {
            _check.Admissions.Paired(Database, "invitation-one", Issued);
            _check.Admissions.PairingRefused(Database, "invitation-two");
            _check.Admissions.PairingUnanswered(Database, "invitation-three");

            // An approved pairing's client key is the certificate it issued, the client's first;
            // one refused or unanswered admitted nobody, so it names no client.
            Assert.That(Described(), Is.EqualTo(new[]
            {
                "pairRequest | - | " + Issued + " | approved | the operator approved it | invitation-one | " + Issued,
                "pairRequest | - | - | refused | the operator refused it | invitation-two | -",
                "pairRequest | - | - | unanswered | nobody answered | invitation-three | -",
            }));
        }

        [Test]
        public void A_renewal_is_written_against_the_certificate_that_asked_and_the_client_it_belongs_to()
        {
            _check.Admissions.Renewed(Database, Key, ClientKey, Issued);
            _check.Admissions.RenewalUnanswered(Database, Key, ClientKey);

            Assert.That(Described(), Is.EqualTo(new[]
            {
                "renewCertificate | " + Key + " | " + ClientKey + " | approved | the operator approved it | - | " + Issued,
                "renewCertificate | " + Key + " | " + ClientKey + " | unanswered | nobody answered | - | -",
            }));
        }

        [Test]
        public void A_refused_renewal_says_which_of_the_four_answers_the_operator_gave()
        {
            _check.Admissions.RenewalRefused(Database, Key, ClientKey, null);
            _check.Admissions.RenewalRefused(Database, Key, ClientKey, RenewalRefusal.ThisSession());
            _check.Admissions.RenewalRefused(Database, Key, ClientKey, RenewalRefusal.Until(At.AddDays(30)));
            _check.Admissions.RenewalRefused(Database, Key, ClientKey, RenewalRefusal.Forever());

            Assert.That(_audit.Entries.Select(line => line.Decision), Is.All.EqualTo("refused"));
            Assert.That(_audit.Entries.Select(line => line.ClientKey), Is.All.EqualTo(ClientKey));
            Assert.That(_audit.Entries.Select(line => line.Reason), Is.EqualTo(new[]
            {
                "the operator refused it once",
                "the operator refused it in this session",
                "the operator refused it until a later date",
                "the operator refused it forever",
            }));
        }

        [Test]
        public void A_renewal_refused_by_an_earlier_answer_says_nobody_was_asked()
        {
            _check.Admissions.RenewalRefusedWithoutAsking(Database, Key, ClientKey, RenewalRefusal.ThisSession());
            _check.Admissions.RenewalRefusedWithoutAsking(
                Database, Key, ClientKey, RenewalRefusal.Until(At.AddDays(30)));
            _check.Admissions.RenewalRefusedWithoutAsking(Database, Key, ClientKey, RenewalRefusal.Forever());
            Assert.That(() => _check.Admissions.RenewalRefusedWithoutAsking(Database, Key, ClientKey, null),
                Throws.ArgumentNullException, "no standing answer has no reason to write down");
            Assert.That(_audit.Entries.Select(line => line.ClientKey), Is.All.EqualTo(ClientKey));

            Assert.That(_audit.Entries.Select(line => line.Reason), Is.EqualTo(new[]
            {
                "refused in this session, without asking",
                "refused until a later date, without asking",
                "refused forever, without asking",
            }));
        }

        [Test]
        public void A_revocation_names_the_certificate_it_took_away_and_the_client_it_belonged_to()
        {
            _check.Admissions.RevokedItself(Database, Key, ClientKey);
            _check.Admissions.RevokedByOperator(Database, Key, ClientKey);
            _check.Admissions.RevokedByOperator(Database, Issued, null);

            Assert.That(Described(), Is.EqualTo(new[]
            {
                "revokeCertificate | " + Key + " | " + ClientKey + " | revoked | the client revoked its own certificate | - | -",
                "- | " + Key + " | " + ClientKey + " | revoked | the operator revoked it | - | -",
                "- | " + Issued + " | - | revoked | the operator revoked it | - | -",
            }));
        }

        [Test]
        public void No_line_about_an_admission_names_a_node_or_a_rule()
        {
            _check.Admissions.Paired(Database, "invitation-one", Issued);
            _check.Admissions.Renewed(Database, Key, ClientKey, Issued);
            _check.Admissions.RevokedByOperator(Database, Key, ClientKey);

            Assert.That(_audit.Entries.Select(line => line.Node), Is.All.Null);
            Assert.That(_audit.Entries.Select(line => line.DecidedAt), Is.All.Null);
            Assert.That(_audit.Entries.Select(line => line.DatabaseId), Is.All.EqualTo(Database));
            Assert.That(_audit.Entries.Select(line => line.At), Is.All.EqualTo(At), "on the access check's clock");
        }

        [Test]
        public void A_client_s_renewals_and_revocation_are_paced_on_the_allowance_its_decisions_spend()
        {
            // One allowance for everything a client can make the file say. Split by kind, one
            // flooding client could push twice as much out of the file as the pace allows.
            var client = Client("reader");
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            for (var i = 0; i < DecisionPace.Burst; i++)
            {
                _check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            Assume.That(_waits, Is.Empty, "the burst itself is never held back");

            _check.Admissions.RenewalRefusedWithoutAsking(
                Database, client.AdmissionKey, client.ClientKey, RenewalRefusal.Forever());
            _check.Admissions.RevokedItself(Database, client.AdmissionKey, client.ClientKey);
            _check.Admissions.TakeTurn(client.ClientKey);

            Assert.That(_waits, Has.Count.EqualTo(2), "the revocation's turn is taken, as the refusal's is");
        }

        [Test]
        public void A_renewal_does_not_hand_a_client_a_fresh_allowance()
        {
            // Counted by client key, which a renewal copies: the certificate it renews into spends
            // what the one it paired with left, rather than starting again at a whole burst, and
            // the renewal's own turn is taken from that allowance too.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var paired = Client("reader");
            var renewed = Renewed(paired.ClientKey, "reader");
            Assume.That(renewed.AdmissionKey, Is.Not.EqualTo(paired.AdmissionKey), "two certificates");

            for (var i = 0; i < DecisionPace.Burst; i++)
            {
                _check.Decide(paired, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            Assume.That(_waits, Is.Empty, "the burst itself is never held back");

            _check.Admissions.Renewed(Database, paired.AdmissionKey, paired.ClientKey, renewed.AdmissionKey);
            _check.Admissions.TakeTurn(paired.ClientKey);
            Assert.That(_waits, Has.Count.EqualTo(1), "the renewal's turn comes out of the spent allowance");

            _check.Decide(renewed, "readEntry", AclVerb.Read, AclReach.Fields, entry);

            Assert.That(_waits, Has.Count.EqualTo(2), "the renewed certificate waits as the first would have");
        }

        [Test]
        public void A_refused_certificate_with_no_client_key_is_still_paced_by_its_own_key()
        {
            // No record stands behind it, so it has no client key, and a revoked client hammering
            // the gate must not go without a pace for that.
            ClientIdentity refused;
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                refused = ClientIdentity.Refused(Database, certificate, "this certificate has no admission record");
            }

            Assume.That(refused.ClientKey, Is.Null);

            for (var i = 0; i < DecisionPace.Burst + 1; i++)
            {
                _check.Refuse(refused, "readEntry");
            }

            Assert.That(_waits, Has.Count.EqualTo(1));
        }

        [Test]
        public void Another_client_keeps_an_allowance_of_its_own()
        {
            // The other side of counting by client key: a client that never renewed and holds a
            // certificate of its own is not held back by somebody else's flood.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var flooding = Client("reader");
            var other = Client("reader");

            for (var i = 0; i < DecisionPace.Burst + 1; i++)
            {
                _check.Decide(flooding, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            _waits.Clear();
            _check.Decide(other, "readEntry", AclVerb.Read, AclReach.Fields, entry);

            Assert.That(_waits, Is.Empty);
        }

        [Test]
        public void The_lines_the_writer_writes_never_wait_and_their_turn_is_taken_apart_from_them()
        {
            // An approved renewal and a revocation of itself are written by the writer on
            // KeePass's own thread, which must not be held, so the client's turn for each is taken
            // afterwards on its own connection, against the same allowance.
            var client = Client("reader");
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            for (var i = 0; i < DecisionPace.Burst + 1; i++)
            {
                _check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            _waits.Clear();

            _check.Admissions.Renewed(Database, client.AdmissionKey, client.ClientKey, Issued);
            _check.Admissions.RevokedItself(Database, client.AdmissionKey, client.ClientKey);

            Assert.That(_waits, Is.Empty, "neither line waits where it is written");
            Assert.That(_audit.Entries.Count(line => line.AdmissionKey == client.AdmissionKey && line.Node == null),
                Is.EqualTo(2));

            _check.Admissions.TakeTurn(client.ClientKey);
            _check.Admissions.TakeTurn(client.ClientKey);

            Assert.That(_waits, Has.Count.EqualTo(2), "and each turn is waited for apart from it");
        }

        [Test]
        public void A_pairing_and_the_operator_s_revocation_never_wait()
        {
            // A pairing names no client and waits on a person, who is its pace; the operator's
            // revocation is written on KeePass's own thread, which must not be held.
            var client = Client("reader");
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            for (var i = 0; i < DecisionPace.Burst + 1; i++)
            {
                _check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            }

            _waits.Clear();

            _check.Admissions.RevokedByOperator(Database, client.AdmissionKey, client.ClientKey);
            _check.Admissions.Paired(Database, "invitation-one", client.AdmissionKey);
            _check.Admissions.PairingRefused(Database, "invitation-two");

            Assert.That(_waits, Is.Empty);
        }

        [Test]
        public void An_admission_audit_given_nowhere_to_write_writes_nothing_and_says_nothing()
        {
            var nowhere = new AdmissionAudit(null);

            Assert.DoesNotThrow(() =>
            {
                nowhere.Paired(Database, "invitation-one", Issued);
                nowhere.RevokedByOperator(Database, Key, ClientKey);
            });
        }

        /// <summary>
        /// Each line as its operation, certificate, client, decision, reason, invitation and key
        /// issued.
        /// </summary>
        private IEnumerable<string> Described()
        {
            return _audit.Entries.Select(line => string.Join(" | ", new[]
            {
                line.OperationId ?? "-",
                line.AdmissionKey ?? "-",
                line.ClientKey ?? "-",
                line.Decision,
                line.Reason,
                line.Invitation ?? "-",
                line.Issued ?? "-",
            }));
        }

        private static ClientIdentity Client(params string[] profiles)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired(Database, certificate, profiles);
            }
        }

        /// <summary>A certificate of its own, on a record carrying the client key it renewed from.</summary>
        private static ClientIdentity Renewed(string clientKey, params string[] profiles)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired(Database, certificate, profiles, null, clientKey);
            }
        }
    }
}
