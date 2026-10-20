using System;
using System.Globalization;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    public sealed class CertificateValidityTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private TestAuthority _authority;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Dispose();
        }

        [Test]
        public void A_current_certificate_is_current()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                Assert.That(CertificateValidity.IsCurrent(certificate, DateTimeOffset.UtcNow), Is.True);
            }
        }

        [Test]
        public void An_expired_certificate_is_not_and_gets_no_grace()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var afterTheEnd = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                Assert.That(CertificateValidity.IsCurrent(certificate, afterTheEnd), Is.True, "the last moment counts");
                Assert.That(CertificateValidity.IsCurrent(certificate, afterTheEnd.AddSeconds(1)), Is.False);
                Assert.That(CertificateValidity.IsCurrent(certificate, afterTheEnd.AddMinutes(4)), Is.False,
                    "expiry gets no skew, because being generous there costs something");
            }
        }

        [Test]
        public void A_certificate_from_the_near_future_is_allowed_for_a_clock_that_is_behind()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var beforeTheStart = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);

                // The allowance is in the certificate, not in this check: `notBefore` is already
                // five minutes before the moment of issuance, and here it is honored exactly. The
                // documented five minutes is therefore measured from issuance, which is what the
                // second assertion pins.
                Assert.That(CertificateValidity.IsCurrent(certificate, beforeTheStart), Is.True,
                    "the first moment counts");
                Assert.That(CertificateValidity.IsCurrent(certificate, beforeTheStart.AddSeconds(-1)), Is.False);

                var issued = beforeTheStart + AuthorityIssuer.Backdate;
                Assert.That(CertificateValidity.IsCurrent(certificate, issued.AddMinutes(-5)), Is.True,
                    "exactly five minutes of allowance, measured from issuance");
                Assert.That(CertificateValidity.IsCurrent(certificate, issued.AddMinutes(-6)), Is.False,
                    "and no more than five");
            }
        }

        [Test]
        public void The_tab_and_the_gate_draw_the_expiry_boundary_in_the_same_place()
        {
            // The tab holds a date rather than the certificate it came from, so it asks the
            // overload. The two comparisons had drifted by one boundary: at exactly `notAfter` the
            // tab said expired while the gate was still serving.
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                foreach (var moment in new[] { notAfter.AddSeconds(-1), notAfter, notAfter.AddSeconds(1) })
                {
                    Assert.That(
                        CertificateValidity.HasExpired(notAfter, moment),
                        Is.EqualTo(CertificateValidity.HasExpired(certificate, moment)),
                        "the two overloads disagree at " + moment.ToString("O", CultureInfo.InvariantCulture));
                }

                Assert.That(CertificateValidity.HasExpired(notAfter, notAfter), Is.False,
                    "the last moment is still inside the certificate");
            }
        }

        [Test]
        public void What_is_left_is_never_negative()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var afterTheEnd = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                Assert.That(CertificateValidity.RemainingOn(certificate, afterTheEnd.AddDays(1)), Is.EqualTo(TimeSpan.Zero));
                Assert.That(CertificateValidity.RemainingOn(certificate, DateTimeOffset.UtcNow).TotalDays,
                    Is.GreaterThan(300));
            }
        }

        [Test]
        public void Expiry_is_the_same_question_wherever_it_is_asked()
        {
            // The tab and the gate both ask this, and they used to disagree at the boundary: one
            // called a client expired at the instant the other was still serving it.
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var end = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                foreach (var moment in new[] { end.AddSeconds(-1), end, end.AddSeconds(1) })
                {
                    Assert.That(CertificateValidity.HasExpired(certificate, moment),
                        Is.EqualTo(!CertificateValidity.IsCurrent(certificate, moment)), "at " + moment);
                }
            }
        }

        [Test]
        public void Nothing_is_not_current()
        {
            Assert.That(CertificateValidity.IsCurrent(null, Now), Is.False);
            Assert.That(CertificateValidity.HasExpired(null, Now), Is.True);
            Assert.That(CertificateValidity.RemainingOn(null, Now), Is.EqualTo(TimeSpan.Zero));
        }
    }
}
