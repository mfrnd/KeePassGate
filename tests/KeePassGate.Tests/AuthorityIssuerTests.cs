using System.Globalization;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Authority;
using KeePassGate.Testing;
using NUnit.Framework;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The shapes `CA.md` §207 specifies, asserted rather than trusted to a comment. A
    /// certificate that is wrong here is wrong for a hundred years.
    /// </summary>
    [TestFixture]
    public sealed class AuthorityIssuerTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private AuthorityIssuer _issuer;
        private CertifiedKey _authorityKey;
        private X509Certificate2 _authority;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer(() => Now);
            _authorityKey = _issuer.CreateAuthority();
            _authority = _authorityKey.Read();
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
            Assert.That(OnP256(_authorityKey), Is.True, "P-256 throughout, so no RSA anywhere.");
        }

        [Test]
        public void Every_name_is_written_as_UTF8String()
        {
            // RFC 5280 section 4.1.2.4 since 2004. An underscore is the case that mattered: Windows
            // wrote PrintableString, which cannot hold one, and switched to BMPString for it.
            var host = _issuer.IssueHost(_authorityKey);
            var client = _issuer.IssueClient(_authorityKey, "agent_fictitious", null);

            Assert.That(CommonNameType(_authorityKey.Certificate.SubjectDN), Is.EqualTo(typeof(DerUtf8String)));
            Assert.That(CommonNameType(host.Certificate.SubjectDN), Is.EqualTo(typeof(DerUtf8String)));
            Assert.That(CommonNameType(client.Certificate.SubjectDN), Is.EqualTo(typeof(DerUtf8String)));
        }

        [Test]
        public void A_leaf_names_its_issuer_in_exactly_the_bytes_its_authority_is_named_in()
        {
            var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", null);

            Assert.That(client.Certificate.IssuerDN.GetEncoded(),
                Is.EqualTo(_authorityKey.Certificate.SubjectDN.GetEncoded()));
        }

        [Test]
        public void An_authority_Windows_wrote_still_issues_certificates_that_chain_to_it()
        {
            // Written by `CertificateRequest` before Bouncy Castle issued anything, so its subject is
            // a PrintableString. A leaf whose issuer was written again as UTF8String would name a
            // different sequence of bytes, which a chain engine comparing bytes does not match.
            var written = CertificateBlob.Load(SeededAuthority.Authority);
            Assert.That(CommonNameType(written.Certificate.SubjectDN), Is.EqualTo(typeof(DerPrintableString)));

            var client = new AuthorityIssuer().IssueClient(written, "agent-fictitious", null);
            using (var leaf = client.Read())
            using (var authority = written.Read())
            {
                Assert.That(client.Certificate.IssuerDN.GetEncoded(), Is.EqualTo(written.Certificate.SubjectDN.GetEncoded()));
                Assert.That(Chains(leaf, authority), Is.True);
                Assert.That(AnchorPin.Chains(leaf, authority, false), Is.True);
            }
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
        public void The_authority_lasts_a_hundred_years_and_starts_slightly_in_the_past()
        {
            Assert.That(_authority.NotBefore.ToUniversalTime(),
                Is.EqualTo((Now - AuthorityIssuer.Backdate).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
            Assert.That(_authority.NotAfter.ToUniversalTime(),
                Is.EqualTo((Now + AuthorityIssuer.AuthorityLifetime).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Every_authority_is_a_different_one()
        {
            using (var other = _issuer.CreateAuthority().Read())
            {
                Assert.That(CertificateDigest.Of(other), Is.Not.EqualTo(CertificateDigest.Of(_authority)));
            }
        }

        [Test]
        public void A_host_is_issued_by_the_authority_and_says_so()
        {
            using (var host = _issuer.IssueHost(_authorityKey).Read())
            {
                Assert.That(host.Subject, Is.EqualTo(AuthorityIssuer.HostSubject));
                Assert.That(host.Issuer, Is.EqualTo(AuthorityIssuer.AuthoritySubject));
            }
        }

        [Test]
        public void A_host_is_not_a_certificate_authority()
        {
            using (var host = _issuer.IssueHost(_authorityKey).Read())
            {
                var constraints = Extension<X509BasicConstraintsExtension>(host, "2.5.29.19");
                Assert.That(constraints.CertificateAuthority, Is.False);
                Assert.That(constraints.Critical, Is.True);
            }
        }

        [Test]
        public void A_host_is_for_serving_and_nothing_else()
        {
            using (var host = _issuer.IssueHost(_authorityKey).Read())
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
            using (var host = _issuer.IssueHost(_authorityKey).Read())
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
            using (var host = _issuer.IssueHost(_authorityKey).Read())
            {
                var authorityKeyIdentifier = host.Extensions["2.5.29.35"];
                Assert.That(authorityKeyIdentifier, Is.Not.Null, "Without this the authority can never be renewed.");

                var subjectKeyIdentifier = Extension<X509SubjectKeyIdentifierExtension>(_authority, "2.5.29.14");
                Assert.That(Hex(authorityKeyIdentifier.RawData),
                    Does.Contain(subjectKeyIdentifier.SubjectKeyIdentifier.ToLowerInvariant()));
            }
        }

        [Test]
        public void A_host_ends_exactly_when_its_authority_does()
        {
            // Issued together, so that the one cannot run out while the other has years left:
            // to the second, which is all either keeps.
            using (var host = _issuer.IssueHost(_authorityKey).Read())
            {
                Assert.That(host.NotAfter.ToUniversalTime(), Is.EqualTo(_authority.NotAfter.ToUniversalTime()));
            }
        }

        [Test]
        public void A_host_issued_later_still_ends_with_its_authority()
        {
            var later = Now + AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(30);
            var issuer = new AuthorityIssuer(() => later);

            using (var host = issuer.IssueHost(_authorityKey).Read())
            {
                Assert.That(host.NotAfter.ToUniversalTime(), Is.EqualTo(_authority.NotAfter.ToUniversalTime()));
            }
        }

        [Test]
        public void A_responder_never_outlives_the_authority_that_vouches_for_it()
        {
            // Unlocking in the authority's last ninety days. Unclamped, the responder would outlive
            // the authority that vouches for it, which `CertificateRequest.Create` refused while it
            // made these, so the unlock threw rather than minting a shorter responder.
            var later = Now + AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(30);
            var issuer = new AuthorityIssuer(() => later);

            using (var responder = issuer.IssueResponder(_authorityKey).Read())
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

            Assert.Throws<InvalidOperationException>(() => issuer.IssueHost(_authorityKey));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueClient(_authorityKey, "agent-fictitious", null));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueResponder(_authorityKey));
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

            Assert.Throws<InvalidOperationException>(() => issuer.IssueHost(_authorityKey));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueClient(_authorityKey, "agent-fictitious", null));
            Assert.Throws<InvalidOperationException>(() => issuer.IssueResponder(_authorityKey));
        }

        [TestCase(" agent-fictitious")]
        [TestCase("agent-fictitious ")]
        [TestCase("   ")]
        public void A_client_name_with_a_space_at_either_end_is_refused(string subjectName)
        {
            // Measured on `X500DistinguishedName`, which wrote these names before Bouncy Castle did:
            // it strips it, so `CN=agent`, `CN= agent` and `CN=agent ` encoded to the same bytes and
            // a name of nothing but spaces to an empty common name. Two clients would carry one
            // identity in a log line and in an admission record.
            Assert.That(AuthorityIssuer.IsUsableSubjectName(subjectName), Is.False);
            Assert.Throws<ArgumentException>(() => _issuer.IssueClient(_authorityKey, subjectName, null));
        }

        [Test]
        public void A_space_inside_a_client_name_is_still_allowed()
        {
            Assert.That(AuthorityIssuer.IsUsableSubjectName("agent fictitious"), Is.True);
        }

        [Test]
        public void An_authority_is_held_only_with_its_own_key()
        {
            // An authority without its key cannot be handed to the issuer at all now, so what is
            // left to refuse is a key that is somebody else's.
            var other = _issuer.CreateAuthority();

            Assert.Throws<ArgumentException>(() => new CertifiedKey(_authorityKey.Certificate, other.PrivateKey));
        }

        [Test]
        public void A_host_chains_to_its_authority_and_to_nothing_else()
        {
            using (var host = _issuer.IssueHost(_authorityKey).Read())
            using (var foreign = new AuthorityIssuer(() => Now).CreateAuthority().Read())
            {
                Assert.That(Chains(host, _authority), Is.True);
                Assert.That(Chains(host, foreign), Is.False, "The anchor is pinned by its bytes, not by trust.");
            }
        }

        [Test]
        public void A_client_is_issued_for_client_authentication_and_nothing_else()
        {
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", null).Read())
            {
                var extended = Extension<X509EnhancedKeyUsageExtension>(client, "2.5.29.37");
                var oids = extended.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ToList();

                Assert.That(oids, Is.EqualTo(new[] { AuthorityIssuer.ClientAuthOid }));
                Assert.That(client.Subject, Is.EqualTo("CN=agent-fictitious"));
            }
        }

        [Test]
        public void A_client_may_sign_a_handshake_and_nothing_else()
        {
            // No `keyAgreement`, unlike the host: a client proves who it is and takes part in no
            // key agreement of its own. Critical for the reason the host's is.
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", null).Read())
            {
                var usage = Extension<X509KeyUsageExtension>(client, "2.5.29.15");

                Assert.That(usage.KeyUsages, Is.EqualTo(X509KeyUsageFlags.DigitalSignature));
                Assert.That(usage.Critical, Is.True);
            }
        }

        [Test]
        public void A_client_nobody_chose_a_lifetime_for_lasts_a_year()
        {
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", null).Read())
            {
                Assert.That(client.NotAfter.ToUniversalTime(),
                    Is.EqualTo((Now + Lifetime.Unasked).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void A_client_lasts_the_lifetime_it_is_given()
        {
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", null, TimeSpan.FromDays(7)).Read())
            {
                Assert.That(client.NotAfter.ToUniversalTime(),
                    Is.EqualTo((Now + TimeSpan.FromDays(7)).UtcDateTime).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(1.5)]
        [TestCase(366.0)]
        public void A_lifetime_outside_whole_days_from_one_to_a_year_is_refused(double days)
        {
            // The wire and the dialog both hold a lifetime to this, and the issuer is the last door
            // a lifetime passes before it is a certificate.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _issuer.IssueClient(_authorityKey, "agent-fictitious", null, TimeSpan.FromDays(days)));
        }

        [Test]
        public void A_client_never_outlives_the_authority_that_vouches_for_it()
        {
            // Pairing in the authority's last year. Unclamped, the client would outlive the authority
            // that vouches for it, which `CertificateRequest.Create` refused while it made these, so
            // the pairing failed rather than being shortened.
            var later = Now + AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(30);
            var issuer = new AuthorityIssuer(() => later);

            using (var client = issuer.IssueClient(_authorityKey, "agent-fictitious", null).Read())
            {
                Assert.That(client.NotAfter.ToUniversalTime(),
                    Is.EqualTo(_authority.NotAfter.ToUniversalTime()).Within(TimeSpan.FromSeconds(1)));
            }
        }

        [Test]
        public void A_clients_profiles_travel_as_claims_naming_no_database()
        {
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", new[] { "ops", "readonly" }).Read())
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
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", Array.Empty<string>()).Read())
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
            // an audit log, neither of which is a place for arbitrary text.
            Assert.Throws<ArgumentException>(() => _issuer.IssueClient(_authorityKey, name, null));
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
            Assert.Throws<ArgumentException>(() => _issuer.IssueClient(_authorityKey, "agent-fictitious", new[] { profile }));
        }

        [Test]
        public void A_client_chains_to_the_authority_that_issued_it_and_to_nothing_else()
        {
            using (var client = _issuer.IssueClient(_authorityKey, "agent-fictitious", null).Read())
            using (var foreign = new AuthorityIssuer(() => Now).CreateAuthority().Read())
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

        private static bool OnP256(CertifiedKey certified)
        {
            var key = certified.PrivateKey as ECPrivateKeyParameters;
            return key != null && X9ObjectIdentifiers.Prime256v1.Equals(key.PublicKeyParamSet);
        }

        /// <summary>The string type the one common name in a name is written as.</summary>
        private static Type CommonNameType(Org.BouncyCastle.Asn1.X509.X509Name name)
        {
            var sequence = (Asn1Sequence)Asn1Object.FromByteArray(name.GetEncoded());
            var attribute = (Asn1Sequence)((Asn1Set)sequence[0])[0];
            return attribute[1].GetType();
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
