using System;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The measurement that would have shipped as a defect, kept as a test: chain validation
    /// accepts a certificate from a completely foreign authority unless the anchor is pinned by
    /// its bytes.
    /// </summary>
    [TestFixture]
    public sealed class AnchorPinTests
    {
        private AuthorityIssuer _issuer;
        private X509Certificate2 _ours;
        private X509Certificate2 _foreign;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer();
            _ours = _issuer.CreateAuthority();
            _foreign = _issuer.CreateAuthority();
        }

        [TearDown]
        public void TearDown()
        {
            _ours.Reset();
            _foreign.Reset();
        }

        [Test]
        public void Our_own_host_chains_to_us()
        {
            using (var host = _issuer.IssueHost(_ours))
            {
                Assert.That(AnchorPin.Chains(host, _ours, false), Is.True);
            }
        }

        [Test]
        public void A_foreign_host_does_not()
        {
            using (var host = _issuer.IssueHost(_foreign))
            {
                Assert.That(AnchorPin.Chains(host, _ours, false), Is.False);
            }
        }

        [Test]
        public void Two_authorities_share_a_subject_name_and_nothing_else()
        {
            // Which is why the pin compares bytes: names would match here.
            Assert.That(_foreign.Subject, Is.EqualTo(_ours.Subject));
            Assert.That(_foreign.RawData, Is.Not.EqualTo(_ours.RawData));
        }

        /// <summary>
        /// `CA.md` §214's measurement, kept: a leaf from an authority under another name builds
        /// under the policy the pin sets, and only the pin's own lines refuse it.
        /// </summary>
        /// <remarks>
        /// Every other foreign authority here is minted by `AuthorityIssuer` and carries our name,
        /// so the engine picks our anchor as its issuer, fails the signature and refuses before
        /// either line of the pin is reached. Under another name it finds no issuer at all, and
        /// `AllowUnknownCertificateAuthority` tolerates a chain of one: the count is what refuses.
        /// </remarks>
        [Test]
        public void An_authority_under_another_name_is_refused_by_the_shape_of_its_chain()
        {
            using (var authority = SomebodyElse())
            using (var leaf = ClientUnder(authority))
            {
                using (var chain = BareChain(_ours))
                {
                    Assert.That(chain.Build(leaf), Is.True, "the engine accepts it on its own");
                    Assert.That(chain.ChainElements.Count, Is.EqualTo(1));
                }

                Assert.That(AnchorPin.Chains(leaf, _ours, true), Is.False);
            }
        }

        /// <summary>
        /// The one shape where the bytes of the anchor are what refuses: a chain of two whose root
        /// the engine found somewhere other than the anchor it was handed.
        /// </summary>
        /// <remarks>
        /// The machine's own stores are where that happens, so the certificate is one of theirs:
        /// an intermediate whose root this machine already holds. No authority this suite mints can
        /// be that, and nothing here adds a root to a store to make one. A machine without such a
        /// certificate cannot answer, and says so.
        /// </remarks>
        [Test]
        public void A_chain_the_machine_completes_to_another_root_is_refused_by_the_anchor_bytes()
        {
            var completed = CompletedElsewhere();
            if (completed == null)
            {
                Assert.Inconclusive(
                    "this machine's stores hold no certificate the engine completes to a root of its own");
            }

            using (completed)
            {
                Assert.That(AnchorPin.Chains(completed, _ours, true), Is.False,
                    completed.Subject + " chains, under the pin's policy, to a root that is not the anchor");
            }
        }

        /// <summary>The chain the pin builds, without the two lines after the build that pin it.</summary>
        private static X509Chain BareChain(X509Certificate2 anchor)
        {
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags =
                X509VerificationFlags.AllowUnknownCertificateAuthority | X509VerificationFlags.IgnoreNotTimeValid;
            chain.ChainPolicy.ExtraStore.Add(anchor);
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1);
            return chain;
        }

        /// <summary>
        /// A certificate from this account's intermediate store that the engine completes, under the
        /// pin's policy, to a chain of two, or null.
        /// </summary>
        private X509Certificate2 CompletedElsewhere()
        {
            using (var store = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                foreach (var certificate in store.Certificates)
                {
                    if (certificate.Subject != certificate.Issuer)
                    {
                        using (var chain = BareChain(_ours))
                        {
                            if (chain.Build(certificate) && chain.ChainElements.Count == 2)
                            {
                                return certificate;
                            }
                        }
                    }

                    certificate.Reset();
                }
            }

            return null;
        }

        /// <summary>A self-signed authority under a name that is not ours, as `CA.md` §214 measured.</summary>
        private static X509Certificate2 SomebodyElse()
        {
            using (var key = System.Security.Cryptography.ECDsa.Create(
                       System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    "CN=Somebody Else Entirely", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));

                var now = DateTimeOffset.UtcNow;
                return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
            }
        }

        /// <summary>A certificate for client authentication, signed by the authority given.</summary>
        private static X509Certificate2 ClientUnder(X509Certificate2 authority)
        {
            using (var key = System.Security.Cryptography.ECDsa.Create(
                       System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    "CN=stranger", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new System.Security.Cryptography.OidCollection
                    {
                        new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.2")
                    },
                    false));

                var now = DateTimeOffset.UtcNow;
                return request.Create(authority, now.AddMinutes(-5), now.AddMonths(1), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            }
        }

        [Test]
        public void An_authority_does_not_chain_to_itself_as_a_leaf()
        {
            // A self-signed certificate builds a chain of one element, and this hierarchy has two.
            Assert.That(AnchorPin.Chains(_ours, _ours, false), Is.False);
        }

        [Test]
        public void An_expired_host_is_refused_unless_expiry_is_being_ignored()
        {
            var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(4000);
            using (var oldAuthority = new AuthorityIssuer(() => longAgo).CreateAuthority())
            using (var oldHost = new AuthorityIssuer(() => longAgo).IssueHost(oldAuthority))
            {
                Assert.That(AnchorPin.Chains(oldHost, oldAuthority, false), Is.False);
                Assert.That(AnchorPin.Chains(oldHost, oldAuthority, true), Is.True);
            }
        }

        [Test]
        public void Nothing_chains_to_nothing()
        {
            using (var host = _issuer.IssueHost(_ours))
            {
                Assert.That(AnchorPin.Chains(null, _ours, false), Is.False);
                Assert.That(AnchorPin.Chains(host, null, false), Is.False);
                Assert.That(AnchorPin.Chains(null, null, false), Is.False);
            }
        }
    }
}
