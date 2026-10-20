using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The measurements that would have shipped as defects, kept as tests: chain validation
    /// accepts a certificate from a completely foreign authority unless the anchor is pinned by
    /// its bytes, and on its way to refusing one it downloads whatever that certificate says
    /// signed it.
    /// </summary>
    [TestFixture]
    public sealed class AnchorPinTests
    {
        /// <summary>
        /// How long the pin may take over a certificate, whichever way it answers. Generous
        /// against a loaded machine and nowhere near the fifteen seconds an unbounded download
        /// costs: measured at 79 ms for the leaf that names one and 55 ms for a leaf that does
        /// not, against 15.0 s for the same leaf before `AnchorPin` bounded the retrieval.
        /// </summary>
        private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(3);

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

        /// <summary>
        /// `CA.md` §214: a leaf whose issuer the pin does not hold sends the chain engine looking
        /// for one, and the leaf says where to look. The certificate arrives from whoever opened
        /// the connection, so the address is theirs, and the refusal has to come back before the
        /// engine has finished asking.
        /// </summary>
        [Test]
        public void A_foreign_leaf_that_names_a_download_is_refused_without_waiting_for_it()
        {
            using (var silent = new SilentHttpAddress())
            using (var leaf = ClientNaming(_foreign, silent.CaIssuers))
            {
                var elapsed = Stopwatch.StartNew();
                bool evaluated;
                var chains = AnchorPin.Chains(leaf, _ours, true, out evaluated);
                elapsed.Stop();

                Assert.That(chains, Is.False, "a leaf from another authority is not ours whatever it says.");
                Assert.That(evaluated, Is.True, "a partial chain is an answer, not an engine that failed.");
                Assert.That(
                    elapsed.Elapsed,
                    Is.LessThan(Promptly),
                    "the pin waited on " + silent.CaIssuers + ", which answers nothing: " +
                    elapsed.ElapsedMilliseconds + " ms, with " + silent.Arrivals + " request(s) arriving.");
            }
        }

        /// <summary>
        /// The other half of the bound: a certificate the pin should accept is still accepted, and
        /// nothing about it is ever downloaded, because the anchor is handed in rather than looked
        /// up.
        /// </summary>
        [Test]
        public void Our_own_client_still_chains_and_does_so_at_once()
        {
            using (var client = _issuer.IssueClient(_ours, "measured", new[] { "reader" }))
            {
                var elapsed = Stopwatch.StartNew();
                var chains = AnchorPin.Chains(client, _ours, true);
                elapsed.Stop();

                Assert.That(chains, Is.True);
                Assert.That(elapsed.Elapsed, Is.LessThan(Promptly), elapsed.ElapsedMilliseconds + " ms");
            }
        }

        /// <summary>
        /// A client certificate under <paramref name="authority"/> carrying an authority
        /// information access extension, which `AuthorityIssuer` never writes and a stranger is
        /// free to.
        /// </summary>
        private static X509Certificate2 ClientNaming(X509Certificate2 authority, string caIssuers)
        {
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName("CN=stranger"), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(AuthorityIssuer.ClientAuthOid) }, false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                request.CertificateExtensions.Add(AuthorityIssuer.AuthorityKeyIdentifierOf(authority));
                request.CertificateExtensions.Add(CaIssuersPointingAt(caIssuers));

                var now = DateTimeOffset.UtcNow;
                return request.Create(
                    authority,
                    now - AuthorityIssuer.Backdate,
                    now + AuthorityIssuer.ClientLifetime,
                    AuthorityIssuer.NewSerialNumber());
            }
        }

        /// <summary>
        /// `id-pe-authorityInfoAccess` naming one `caIssuers` location, encoded by hand because
        /// .NET Framework has no class that writes one.
        /// </summary>
        /// <remarks>
        /// RFC 5280 section 4.2.2.1: `SEQUENCE OF AccessDescription`, each of which is
        /// `SEQUENCE { accessMethod OBJECT IDENTIFIER, accessLocation GeneralName }`. The method is
        /// `id-ad-caIssuers`, `1.3.6.1.5.5.7.48.2`, and the location is a URI, which is context tag
        /// 6 in `GeneralName`. ITU-T X.690 section 8.1 is the length rule, and every length here
        /// is the short form because the caller passes a loopback address.
        /// https://www.rfc-editor.org/rfc/rfc5280#section-4.2.2.1
        /// </remarks>
        private static X509Extension CaIssuersPointingAt(string url)
        {
            var uri = Encoding.ASCII.GetBytes(url);
            if (uri.Length > 100)
            {
                throw new ArgumentException("The lengths below are the DER short form.", nameof(url));
            }

            var method = new byte[] { 0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x30, 0x02 };
            var location = new byte[uri.Length + 2];
            location[0] = 0x86;                          // [6] uniformResourceIdentifier
            location[1] = (byte)uri.Length;
            Buffer.BlockCopy(uri, 0, location, 2, uri.Length);

            var description = new byte[method.Length + location.Length];
            Buffer.BlockCopy(method, 0, description, 0, method.Length);
            Buffer.BlockCopy(location, 0, description, method.Length, location.Length);

            return new X509Extension("1.3.6.1.5.5.7.1.1", Sequence(Sequence(description)), false);
        }

        private static byte[] Sequence(byte[] content)
        {
            var wrapped = new byte[content.Length + 2];
            wrapped[0] = 0x30;                           // SEQUENCE
            wrapped[1] = (byte)content.Length;
            Buffer.BlockCopy(content, 0, wrapped, 2, content.Length);
            return wrapped;
        }

        /// <summary>
        /// A loopback address that accepts a connection and then answers nothing, which is the
        /// shape of a download a stranger names: reachable enough to hold whoever asked, and never
        /// finished. Loopback and a port the operating system picks, so the test needs no network
        /// and no name resolution, and a fresh path each time, because Windows caches what it has
        /// already failed to fetch.
        /// </summary>
        private sealed class SilentHttpAddress : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _accepting;
            private readonly List<TcpClient> _held = new List<TcpClient>();
            private int _arrivals;

            public SilentHttpAddress()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();

                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                CaIssuers = "http://127.0.0.1:" + port + "/" + Guid.NewGuid().ToString("N") + ".cer";

                _accepting = new Thread(Accept) { IsBackground = true, Name = "SilentHttpAddress" };
                _accepting.Start();
            }

            /// <summary>The URL a certificate can point at.</summary>
            public string CaIssuers { get; private set; }

            /// <summary>How many connections arrived, which the failure message reports.</summary>
            public int Arrivals
            {
                get { return Interlocked.CompareExchange(ref _arrivals, 0, 0); }
            }

            public void Dispose()
            {
                _listener.Stop();

                lock (_held)
                {
                    foreach (var client in _held)
                    {
                        client.Close();
                    }

                    _held.Clear();
                }

                _accepting.Join(TimeSpan.FromSeconds(5));
            }

            private void Accept()
            {
                try
                {
                    while (true)
                    {
                        var client = _listener.AcceptTcpClient();
                        Interlocked.Increment(ref _arrivals);

                        // Held rather than answered, and held rather than closed: a refusal is an
                        // answer of a kind, and the point is an address that gives none.
                        lock (_held)
                        {
                            _held.Add(client);
                        }
                    }
                }
                catch (SocketException)
                {
                    // `Stop` on the listener, which is how this thread is asked to end.
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }
}
