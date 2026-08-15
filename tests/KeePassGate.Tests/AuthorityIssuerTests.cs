using System.Globalization;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The shapes `CA.md` §207 specifies, asserted rather than trusted to a comment. A
    /// certificate that is wrong here is wrong for ten years.
    /// </summary>
    [TestFixture]
    public sealed class AuthorityIssuerTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private AuthorityIssuer _issuer;
        private X509Certificate2 _authority;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer(() => Now);
            _authority = _issuer.CreateAuthority();
        }

        [TearDown]
        public void TearDown()
        {
            if (_authority != null)
            {
                _authority.Reset();
            }
        }

        [Test]
        public void The_authority_signs_itself_and_holds_its_key()
        {
            Assert.That(_authority.Subject, Is.EqualTo(AuthorityIssuer.AuthoritySubject));
            Assert.That(_authority.Issuer, Is.EqualTo(AuthorityIssuer.AuthoritySubject));
            Assert.That(_authority.HasPrivateKey, Is.True);
            Assert.That(_authority.GetECDsaPrivateKey(), Is.Not.Null, "P-256 throughout, so no RSA anywhere.");
        }

        [Test]
        public void The_authority_is_a_certificate_authority_that_cannot_mint_a_subordinate()
        {
            var constraints = Extension<X509BasicConstraintsExtension>(_authority, "2.5.29.19");

            Assert.That(constraints.CertificateAuthority, Is.True);
            Assert.That(constraints.HasPathLengthConstraint, Is.True);
            Assert.That(constraints.PathLengthConstraint, Is.Zero);
            Assert.That(constraints.Critical, Is.True);
        }

        [Test]
        public void The_authority_may_sign_certificates_and_nothing_else()
        {
            var usage = Extension<X509KeyUsageExtension>(_authority, "2.5.29.15");

            Assert.That(usage.KeyUsages, Is.EqualTo(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign));
            Assert.That(usage.Critical, Is.True, "Key usage is only worth anything when it is critical.");
        }

        [Test]
        public void The_authority_carries_no_extended_key_usage()
        {
            // One there would constrain every certificate beneath it, and buy nothing.
            Assert.That(_authority.Extensions["2.5.29.37"], Is.Null);
        }

        [Test]
        public void The_authority_lasts_ten_years_and_starts_slightly_in_the_past()
        {
            Assert.That(_authority.NotBefore.ToUniversalTime(),
                Is.EqualTo((Now - AuthorityIssuer.Backdate).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
            Assert.That(_authority.NotAfter.ToUniversalTime(),
                Is.EqualTo((Now + AuthorityIssuer.AuthorityLifetime).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Every_authority_is_a_different_one()
        {
            using (var other = _issuer.CreateAuthority())
            {
                Assert.That(CertificateDigest.Of(other), Is.Not.EqualTo(CertificateDigest.Of(_authority)));
            }
        }

        [Test]
        public void A_host_is_issued_by_the_authority_and_says_so()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                Assert.That(host.Subject, Is.EqualTo(AuthorityIssuer.HostSubject));
                Assert.That(host.Issuer, Is.EqualTo(AuthorityIssuer.AuthoritySubject));
                Assert.That(host.HasPrivateKey, Is.True);
            }
        }

        [Test]
        public void A_host_is_not_a_certificate_authority()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                var constraints = Extension<X509BasicConstraintsExtension>(host, "2.5.29.19");
                Assert.That(constraints.CertificateAuthority, Is.False);
                Assert.That(constraints.Critical, Is.True);
            }
        }

        [Test]
        public void A_host_is_for_serving_and_nothing_else()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                var extended = Extension<X509EnhancedKeyUsageExtension>(host, "2.5.29.37");
                var oids = extended.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ToList();

                Assert.That(oids, Is.EqualTo(new[] { AuthorityIssuer.ServerAuthOid }));
                Assert.That(oids, Does.Not.Contain(AuthorityIssuer.ClientAuthOid),
                    "The same authority signs clients, so a host must not be able to act as one.");
            }
        }

        [Test]
        public void A_host_points_back_at_the_key_that_signed_it()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                var authorityKeyIdentifier = host.Extensions["2.5.29.35"];
                Assert.That(authorityKeyIdentifier, Is.Not.Null, "Without this the authority can never be renewed.");

                var subjectKeyIdentifier = Extension<X509SubjectKeyIdentifierExtension>(_authority, "2.5.29.14");
                Assert.That(Hex(authorityKeyIdentifier.RawData),
                    Does.Contain(subjectKeyIdentifier.SubjectKeyIdentifier.ToLowerInvariant()));
            }
        }

        [Test]
        public void A_host_lasts_five_years()
        {
            using (var host = _issuer.IssueHost(_authority))
            {
                Assert.That(host.NotAfter.ToUniversalTime(),
                    Is.EqualTo((Now + AuthorityIssuer.HostLifetime).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void A_host_never_outlives_the_authority_that_vouches_for_it()
        {
            var later = Now + AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(30);
            var issuer = new AuthorityIssuer(() => later);

            using (var host = issuer.IssueHost(_authority))
            {
                Assert.That(host.NotAfter.ToUniversalTime(),
                    Is.EqualTo(_authority.NotAfter.ToUniversalTime()).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void An_expired_authority_issues_nothing()
        {
            var afterTheEnd = Now + AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(1);
            var issuer = new AuthorityIssuer(() => afterTheEnd);

            Assert.Throws<InvalidOperationException>(() => issuer.IssueHost(_authority));
        }

        [Test]
        public void An_authority_without_its_key_issues_nothing()
        {
            using (var publicOnly = new X509Certificate2(_authority.RawData))
            {
                Assert.Throws<ArgumentException>(() => _issuer.IssueHost(publicOnly));
            }
        }

        [Test]
        public void A_host_chains_to_its_authority_and_to_nothing_else()
        {
            using (var host = _issuer.IssueHost(_authority))
            using (var foreign = new AuthorityIssuer(() => Now).CreateAuthority())
            {
                Assert.That(Chains(host, _authority), Is.True);
                Assert.That(Chains(host, foreign), Is.False, "The anchor is pinned by its bytes, not by trust.");
            }
        }

        [Test]
        public void Serial_numbers_are_random_and_positive()
        {
            var first = AuthorityIssuer.NewSerialNumber();
            var second = AuthorityIssuer.NewSerialNumber();

            Assert.That(first.Length, Is.EqualTo(16));
            Assert.That(first[0] & 0x80, Is.Zero, "A negative INTEGER is a certificate some parsers refuse.");
            Assert.That(first, Is.Not.EqualTo(second));
        }

        /// <summary>
        /// The pin from `CA.md` §213, in the shape production will use it: build the chain,
        /// insist on exactly two elements, and compare the anchor's bytes.
        /// </summary>
        private static bool Chains(X509Certificate2 leaf, X509Certificate2 anchor)
        {
            using (var chain = new X509Chain())
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.ExtraStore.Add(anchor);

                if (!chain.Build(leaf))
                {
                    return false;
                }

                if (chain.ChainElements.Count != 2)
                {
                    return false;
                }

                var root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                return root.RawData.SequenceEqual(anchor.RawData);
            }
        }

        private static T Extension<T>(X509Certificate2 certificate, string oid) where T : X509Extension
        {
            var extension = certificate.Extensions[oid] as T;
            Assert.That(extension, Is.Not.Null, "The certificate carries no " + oid + " extension.");
            return extension;
        }

        private static string Hex(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
