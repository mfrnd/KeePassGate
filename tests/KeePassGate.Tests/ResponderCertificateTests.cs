using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The second host certificate: what a locked KeePass answers with.
    /// </summary>
    /// <remarks>
    /// `CA.md` §207 gives its shape, and the marker is the part worth testing hardest. A
    /// client has to be able to tell a responder from a real host certificate, because an impostor
    /// holding this key reads nothing but could answer with a fabricated secret, and an agent
    /// acting on a forged answer is worse than an agent that fails.
    /// </remarks>
    [TestFixture]
    public sealed class ResponderCertificateTests
    {
        private AuthorityIssuer _issuer;
        private X509Certificate2 _authority;
        private X509Certificate2 _responder;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer();
            _authority = _issuer.CreateAuthority();
            _responder = _issuer.IssueResponder(_authority);
        }

        [TearDown]
        public void TearDown()
        {
            _responder.Reset();
            _authority.Reset();
        }

        [Test]
        public void It_is_issued_by_the_database_authority_and_nothing_else()
        {
            Assert.That(AnchorPin.Chains(_responder, _authority, false), Is.True);
        }

        [Test]
        public void It_carries_the_marker_exactly_as_written()
        {
            // The measurement this test exists for: `System.Uri` normalizes a URI with an authority
            // and no path by appending a slash, so a marker written as one thing can be read back
            // as another. What goes in has to come out unchanged or a client cannot match on it.
            Assert.That(CertificateUris.Of(_responder), Is.EqualTo(new[] { AuthorityIssuer.ResponderClaim }));
        }

        [Test]
        public void The_marker_survives_being_stored_and_read_back()
        {
            using (var restored = CertificateBlob.Load(CertificateBlob.Save(_responder)))
            {
                Assert.That(CertificateUris.Of(restored), Is.EqualTo(new[] { AuthorityIssuer.ResponderClaim }));
            }
        }

        [Test]
        public void A_normal_host_certificate_is_not_a_responder()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                Assert.That(CertificateUris.Of(host), Has.No.Member(AuthorityIssuer.ResponderClaim),
                    "otherwise a client could not tell what it is talking to");
            }
        }

        [Test]
        public void A_client_certificate_is_not_a_responder_either()
        {
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", new[] { "ops" }))
            {
                Assert.That(CertificateUris.Of(client), Is.EqualTo(new[] { "kpg://profile/ops" }),
                    "its profile claims and no marker");
            }
        }

        [Test]
        public void It_is_for_server_authentication()
        {
            var usage = _responder.Extensions
                .OfType<X509EnhancedKeyUsageExtension>()
                .Single()
                .EnhancedKeyUsages
                .OfType<System.Security.Cryptography.Oid>()
                .Select(o => o.Value)
                .ToArray();

            Assert.That(usage, Is.EqualTo(new[] { AuthorityIssuer.ServerAuthOid }));
        }

        [Test]
        public void It_signs_a_handshake_and_does_nothing_else()
        {
            // No keyAgreement, unlike the normal host certificate. It has one job.
            var usage = _responder.Extensions.OfType<X509KeyUsageExtension>().Single();

            Assert.That(usage.KeyUsages, Is.EqualTo(X509KeyUsageFlags.DigitalSignature));
            Assert.That(usage.Critical, Is.True);
        }

        [Test]
        public void It_is_not_an_authority()
        {
            var constraints = _responder.Extensions.OfType<X509BasicConstraintsExtension>().Single();

            Assert.That(constraints.CertificateAuthority, Is.False);
            Assert.That(constraints.Critical, Is.True);
        }

        [Test]
        public void It_outlives_a_lock_and_nothing_more()
        {
            var life = _responder.NotAfter - _responder.NotBefore;

            Assert.That(life, Is.LessThan(AuthorityIssuer.HostLifetime), "a responder is not a host certificate");
            Assert.That(life.TotalDays, Is.EqualTo(
                (AuthorityIssuer.ResponderLifetime + AuthorityIssuer.Backdate).TotalDays).Within(1));

            // A week was the first answer and it was too short. A lock is unbounded, a machine
            // left locked over a holiday is ordinary, and a responder that expires mid-lock hands
            // the operator the bare handshake failure it exists to replace.
            Assert.That(AuthorityIssuer.ResponderLifetime, Is.GreaterThan(TimeSpan.FromDays(30)),
                "a lock outlasts a week without anything being wrong");
        }

        [Test]
        public void It_has_its_own_key_rather_than_the_hosts()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                Assert.That(_responder.GetPublicKeyString(), Is.Not.EqualTo(host.GetPublicKeyString()));
            }
        }

        [Test]
        public void Every_one_is_different_because_it_is_re_minted_at_every_unlock()
        {
            using (var second = _issuer.IssueResponder(_authority))
            {
                Assert.That(second.RawData, Is.Not.EqualTo(_responder.RawData));
                Assert.That(second.SerialNumber, Is.Not.EqualTo(_responder.SerialNumber));
            }
        }

        [Test]
        public void An_authority_with_no_key_cannot_mint_one()
        {
            using (var withoutKey = new X509Certificate2(_authority.RawData))
            {
                Assert.Throws<ArgumentException>(() => _issuer.IssueResponder(withoutKey));
            }
        }

        [Test]
        public void There_is_no_responder_without_an_authority()
        {
            Assert.Throws<ArgumentNullException>(() => _issuer.IssueResponder(null));
        }
    }
}
