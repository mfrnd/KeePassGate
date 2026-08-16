using System.Globalization;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
        public void A_host_may_sign_a_handshake_and_agree_a_key_and_nothing_else()
        {
            // The row `CA.md` §207 gives it, and the only one of the four certificates whose key
            // usage nothing asserted. Two things are being kept here. `keyCertSign` must never
            // appear, because the authority is `pathLen 0` and a host that could sign would be a
            // second way to mint identities that the pin would still accept. And the extension has
            // to be critical: a relying party may ignore one that is not, so a non-critical key
            // usage is a list of intentions rather than a limit.
            using (var host = _issuer.IssueHost(_authority))
            {
                var usage = Extension<X509KeyUsageExtension>(host, "2.5.29.15");

                Assert.That(usage.KeyUsages,
                    Is.EqualTo(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement));
                Assert.That(usage.Critical, Is.True, "Key usage is only worth anything when it is critical.");
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
        public void A_responder_never_outlives_the_authority_that_vouches_for_it()
        {
            // Unlocking in the authority's last ninety days. Unclamped, `CertificateRequest.Create`
            // refuses a leaf that ends after its issuer, so the unlock would throw rather than
            // mint a shorter responder.
            var later = Now + AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(30);
            var issuer = new AuthorityIssuer(() => later);

            using (var responder = issuer.IssueResponder(_authority))
            {
                Assert.That(responder.NotAfter.ToUniversalTime(),
                    Is.EqualTo(_authority.NotAfter.ToUniversalTime()).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void An_expired_authority_issues_nothing()
        {
            var afterTheEnd = Now + AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(1);
            var issuer = new AuthorityIssuer(() => afterTheEnd);

            Assert.Throws<InvalidOperationException>(() => issuer.IssueHost(_authority));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueClient(_authority, "agent-fictitious", null));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueResponder(_authority));
        }

        [Test]
        public void An_authority_that_expired_a_minute_ago_issues_nothing_either()
        {
            // The window the backdate opens. Compared against the backdated start rather than
            // against now, this mints a certificate whose validity has already closed, which
            // reading ignores and every connection then refuses.
            var justAfterTheEnd = _authority.NotAfter.ToUniversalTime() + TimeSpan.FromMinutes(1);
            var issuer = new AuthorityIssuer(() => justAfterTheEnd);

            Assert.That(justAfterTheEnd, Is.LessThan(
                _authority.NotAfter.ToUniversalTime() + AuthorityIssuer.Backdate),
                "the test has to sit inside the backdate for it to be testing anything");

            Assert.Throws<InvalidOperationException>(() => issuer.IssueHost(_authority));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueClient(_authority, "agent-fictitious", null));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueResponder(_authority));
        }

        [TestCase(" agent-fictitious")]
        [TestCase("agent-fictitious ")]
        [TestCase("   ")]
        public void A_client_name_with_a_space_at_either_end_is_refused(string subjectName)
        {
            // Measured: `X500DistinguishedName` strips it, so `CN=agent`, `CN= agent` and
            // `CN=agent ` encode to the same bytes and a name of nothing but spaces encodes to an
            // empty common name. Two clients would carry one identity in a log line and in an
            // admission record.
            Assert.That(AuthorityIssuer.IsUsableSubjectName(subjectName), Is.False);
            Assert.Throws<ArgumentException>(() => _issuer.IssueClient(_authority, subjectName, null));
        }

        [Test]
        public void A_space_inside_a_client_name_is_still_allowed()
        {
            Assert.That(AuthorityIssuer.IsUsableSubjectName("agent fictitious"), Is.True);
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
        public void A_client_is_issued_for_client_authentication_and_nothing_else()
        {
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", null))
            {
                var extended = Extension<X509EnhancedKeyUsageExtension>(client, "2.5.29.37");
                var oids = extended.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ToList();

                Assert.That(oids, Is.EqualTo(new[] { AuthorityIssuer.ClientAuthOid }));
                Assert.That(client.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(client.HasPrivateKey, Is.True);
            }
        }

        [Test]
        public void A_client_may_sign_a_handshake_and_nothing_else()
        {
            // No `keyAgreement`, unlike the host: a client proves who it is and takes part in no
            // key agreement of its own. Critical for the reason the host's is.
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", null))
            {
                var usage = Extension<X509KeyUsageExtension>(client, "2.5.29.15");

                Assert.That(usage.KeyUsages, Is.EqualTo(X509KeyUsageFlags.DigitalSignature));
                Assert.That(usage.Critical, Is.True);
            }
        }

        [Test]
        public void A_client_lasts_a_year()
        {
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", null))
            {
                Assert.That(client.NotAfter.ToUniversalTime(),
                    Is.EqualTo((Now + AuthorityIssuer.ClientLifetime).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void A_client_never_outlives_the_authority_that_vouches_for_it()
        {
            // Pairing in the authority's last year. Unclamped, `CertificateRequest.Create` refuses a
            // leaf that ends after its issuer, so the pairing would fail rather than be shortened.
            var later = Now + AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(30);
            var issuer = new AuthorityIssuer(() => later);

            using (var client = issuer.IssueClient(_authority, "agent-fictitious", null))
            {
                Assert.That(client.NotAfter.ToUniversalTime(),
                    Is.EqualTo(_authority.NotAfter.ToUniversalTime()).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void A_clients_profiles_travel_as_claims_naming_no_database()
        {
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", new[] { "ops", "readonly" }))
            {
                var san = client.Extensions["2.5.29.17"];
                Assert.That(san, Is.Not.Null);

                var text = Encoding.ASCII.GetString(san.RawData);
                Assert.That(text, Does.Contain("kpg://profile/ops"));
                Assert.That(text, Does.Contain("kpg://profile/readonly"));
                Assert.That(text, Does.Not.Contain("kpg://any"), "the wildcard is gone and stays gone");
            }
        }

        [Test]
        public void A_client_with_no_profiles_carries_no_claims_at_all()
        {
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", Array.Empty<string>()))
            {
                Assert.That(client.Extensions["2.5.29.17"], Is.Null);
            }
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("agent, O=somebody else")]
        [TestCase("agent\nnewline")]
        [TestCase("agent=equals")]
        public void A_client_name_that_could_be_read_as_something_else_is_refused(string name)
        {
            // The name arrives from outside at pairing and ends up in a distinguished name and in
            // an audit log, neither of which is a place for arbitrary text. TODO(step 14): the
            // audit log arrives with the intersection.
            Assert.Throws<ArgumentException>(() => _issuer.IssueClient(_authority, name, null));
        }

        [TestCase("ops with spaces")]
        [TestCase("ops/slash")]
        [TestCase("")]
        [TestCase("default")]
        [TestCase("DEFAULT")]
        public void A_profile_name_that_could_not_travel_as_a_claim_is_refused(string profile)
        {
            // `default` is the ACL's own word for a rule rather than a grant, so it is never a
            // claim, whatever case it is written in.
            Assert.Throws<ArgumentException>(() => _issuer.IssueClient(_authority, "agent-fictitious", new[] { profile }));
        }

        [Test]
        public void A_client_chains_to_the_authority_that_issued_it_and_to_nothing_else()
        {
            using (var client = _issuer.IssueClient(_authority, "agent-fictitious", null))
            using (var foreign = new AuthorityIssuer(() => Now).CreateAuthority())
            {
                Assert.That(AnchorPin.Chains(client, _authority, false), Is.True);
                Assert.That(AnchorPin.Chains(client, foreign, false), Is.False);
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
