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
using KeePassGate.Testing;
using NUnit.Framework;
using Org.BouncyCastle.Asn1.X509;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the pin refuses, and what it never does: a certificate from another authority, or
    /// naming ours in other bytes, or under an anchor that is no authority, is refused, and nothing
    /// a certificate names is ever fetched.
    /// </summary>
    /// <remarks>
    /// The two measurements that made the chain engine unfit for this are kept on
    /// `spikes/tls-and-certificates`: it accepted a certificate from a completely foreign authority
    /// unless the anchor was pinned by its bytes, and on its way to refusing one it downloaded
    /// whatever that certificate said signed it.
    /// </remarks>
    [TestFixture]
    public sealed class AnchorPinTests
    {
        /// <summary>
        /// How long the pin may take over a certificate, whichever way it answers. Generous
        /// against a loaded machine and nowhere near the fifteen seconds an unbounded download cost
        /// while a chain engine answered this: 15.0 s for the leaf that names one, 79 ms once the
        /// retrieval was bounded to a millisecond, and nothing fetched at all now.
        /// </summary>
        private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(3);

        private AuthorityIssuer _issuer;
        private CertifiedKey _oursKey;
        private X509Certificate2 _ours;
        private CertifiedKey _foreignKey;
        private X509Certificate2 _foreign;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer();
            _oursKey = _issuer.CreateAuthority();
            _ours = _oursKey.Read();
            _foreignKey = _issuer.CreateAuthority();
            _foreign = _foreignKey.Read();
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
            using (var host = _issuer.IssueHost(_oursKey).Read())
            {
                Assert.That(AnchorPin.Chains(host, _ours), Is.True);
            }
        }

        [Test]
        public void A_foreign_host_does_not()
        {
            // It names its issuer in exactly the bytes ours is named in, so the signature is the
            // whole of what refuses it.
            var host = _issuer.IssueHost(_foreignKey);
            Assume.That(host.Certificate.IssuerDN.GetEncoded(), Is.EqualTo(_oursKey.Certificate.SubjectDN.GetEncoded()));

            using (var read = host.Read())
            {
                Assert.That(AnchorPin.Chains(read, _ours), Is.False);
            }
        }

        [Test]
        public void Two_authorities_share_a_subject_name_and_nothing_else()
        {
            // Which is why the pin compares bytes: names would match here.
            Assert.That(_foreign.Subject, Is.EqualTo(_ours.Subject));
            Assert.That(_foreign.RawData, Is.Not.EqualTo(_ours.RawData));
        }

        [Test]
        public void An_authority_under_another_name_is_refused()
        {
            // `CA.md` §214's measurement: the chain engine found no issuer for this, tolerated a
            // chain of one, and only the count refused it.
            using (var leaf = ClientUnder(SomebodyElse()))
            {
                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void A_leaf_our_key_signed_that_names_us_in_other_bytes_is_refused()
        {
            // Only the holder of our key could make one, and nothing this plugin issues is one:
            // a leaf's issuer is our subject as stored. Refused all the same, because "the very
            // bytes" is the rule the pin keeps, and the rule is what a reader can check.
            var other = new X509Name(
                new[] { X509Name.CN }, new[] { "KeePassGate authority" }, new PrintableNames());
            Assume.That(other.GetEncoded(), Is.Not.EqualTo(_oursKey.Certificate.SubjectDN.GetEncoded()));

            using (var leaf = Minted.Named("CN=stranger")
                       .ForUsage(AuthorityIssuer.ClientAuthOid)
                       .IssuedBy(_oursKey, other)
                       .Read())
            {
                Assert.That(AnchorPin.Chains(leaf, _ours), Is.False);
            }
        }

        [Test]
        public void An_anchor_that_is_no_authority_vouches_for_nothing()
        {
            // A host can sign with its key like anything else; `CA:FALSE` is what says its
            // signature vouches for nobody.
            var host = _issuer.IssueHost(_oursKey);

            using (var anchor = host.Read())
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(host).Read())
            {
                Assert.That(AnchorPin.Chains(leaf, anchor), Is.False);
            }
        }

        [Test]
        public void An_anchor_somebody_else_signed_is_the_subordinate_pathLen_0_forbids()
        {
            var subordinate = Minted.Named("CN=KeePassGate authority").AsAuthority().IssuedBy(_oursKey);

            using (var anchor = subordinate.Read())
            using (var leaf = Minted.Named("CN=stranger").ForUsage(AuthorityIssuer.ClientAuthOid).IssuedBy(subordinate).Read())
            {
                Assert.That(AnchorPin.Chains(leaf, anchor), Is.False);
            }
        }

        [Test]
        public void A_critical_extension_nobody_here_understands_refuses_a_leaf_our_key_signed()
        {
            // RFC 5280 section 4.2, as the chain engine kept it: a reader that does not recognize a
            // critical extension refuses the certificate. The same extension not critical is
            // ignored, as it may be.
            var unknown = new Org.BouncyCastle.Asn1.DerObjectIdentifier("2.25.1");

            using (var critical = Minted.Named("CN=stranger")
                       .ForUsage(AuthorityIssuer.ClientAuthOid)
                       .With(unknown, true, Org.BouncyCastle.Asn1.DerNull.Instance)
                       .IssuedBy(_oursKey)
                       .Read())
            using (var ignorable = Minted.Named("CN=stranger")
                       .ForUsage(AuthorityIssuer.ClientAuthOid)
                       .With(unknown, false, Org.BouncyCastle.Asn1.DerNull.Instance)
                       .IssuedBy(_oursKey)
                       .Read())
            {
                Assert.That(AnchorPin.Chains(critical, _ours), Is.False);
                Assert.That(AnchorPin.Chains(ignorable, _ours), Is.True);
            }
        }

        [Test]
        public void A_key_of_another_kind_is_an_answer_rather_than_an_exception()
        {
            // RSA on either side: a signature the anchor's key cannot check, and an anchor whose key
            // cannot check ours. Bouncy Castle says so by throwing, and the pin answers no.
            using (var rsa = RsaAuthority())
            using (var ours = _issuer.IssueClient(_oursKey, "agent-fictitious", null).Read())
            {
                Assert.That(AnchorPin.Chains(rsa, _ours), Is.False);
                Assert.That(AnchorPin.Chains(ours, rsa), Is.False);
            }
        }

        /// <summary>A self-signed authority under a name that is not ours, as `CA.md` §214 measured.</summary>
        private static CertifiedKey SomebodyElse()
        {
            var now = DateTimeOffset.UtcNow;
            return Minted.Named("CN=Somebody Else Entirely").AsAuthority().Lasting(now.AddMinutes(-5), now.AddYears(1)).SelfSigned();
        }

        /// <summary>A certificate for client authentication, signed by the authority given.</summary>
        private static X509Certificate2 ClientUnder(CertifiedKey authority)
        {
            var now = DateTimeOffset.UtcNow;
            return Minted.Named("CN=stranger")
                .ForUsage(AuthorityIssuer.ClientAuthOid)
                .Lasting(now.AddMinutes(-5), now.AddMonths(1))
                .IssuedBy(authority)
                .Read();
        }

        [Test]
        public void An_authority_does_not_chain_to_itself_as_a_leaf()
        {
            // A self-signed certificate builds a chain of one element, and this hierarchy has two.
            Assert.That(AnchorPin.Chains(_ours, _ours), Is.False);
        }

        [Test]
        public void An_expired_leaf_still_belongs_to_its_authority()
        {
            // A client certificate issued eleven years ago, under an authority made the same day
            // and still current: its one year is long gone, and the authority is still the one that
            // signed it. The dates are `CertificateValidity`'s to judge, and the pin asks nothing of
            // them. A host is no good for this any more, since it ends with its authority, a century
            // on.
            var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(4000);
            var oldAuthorityKey = new AuthorityIssuer(() => longAgo).CreateAuthority();
            using (var oldAuthority = oldAuthorityKey.Read())
            using (var oldClient = new AuthorityIssuer(() => longAgo).IssueClient(oldAuthorityKey, "agent-fictitious", null).Read())
            {
                Assume.That(CertificateValidity.HasExpired(oldClient, DateTimeOffset.UtcNow), Is.True);
                Assert.That(AnchorPin.Chains(oldClient, oldAuthority), Is.True);
            }
        }

        [Test]
        public void Nothing_chains_to_nothing()
        {
            using (var host = _issuer.IssueHost(_oursKey).Read())
            {
                Assert.That(AnchorPin.Chains(null, _ours), Is.False);
                Assert.That(AnchorPin.Chains(host, null), Is.False);
                Assert.That(AnchorPin.Chains(null, null), Is.False);
            }
        }

        /// <summary>
        /// `CA.md` §214: a leaf whose issuer the chain engine did not hold sent it looking for one,
        /// at the address the leaf named, and the certificate arrives from whoever opened the
        /// connection. Nothing is fetched now, from a foreign leaf or from our own.
        /// </summary>
        [Test]
        public void A_leaf_that_names_a_download_is_answered_without_anything_reaching_the_address()
        {
            using (var silent = new SilentHttpAddress())
            using (var foreign = ClientNaming(_foreignKey, silent.CaIssuers))
            using (var ours = ClientNaming(_oursKey, silent.CaIssuers))
            {
                var elapsed = Stopwatch.StartNew();
                var foreignChains = AnchorPin.Chains(foreign, _ours);
                var oursChains = AnchorPin.Chains(ours, _ours);
                elapsed.Stop();

                Assert.That(foreignChains, Is.False, "a leaf from another authority is not ours whatever it says.");
                Assert.That(oursChains, Is.True, "and one of ours is ours whatever it says.");
                Assert.That(elapsed.Elapsed, Is.LessThan(Promptly), elapsed.ElapsedMilliseconds + " ms");

                // A request the engine opened arrived while it gave up, so a quiet moment is given
                // for one to land before nothing is believed to have.
                Thread.Sleep(Quiet);
                Assert.That(silent.Arrivals, Is.Zero, "nothing reaches " + silent.CaIssuers);
            }
        }

        /// <summary>How long a request is given to arrive before none is believed to have been made.</summary>
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// A certificate the pin should accept is accepted at once, because the anchor is handed in
        /// rather than looked up.
        /// </summary>
        [Test]
        public void Our_own_client_still_chains_and_does_so_at_once()
        {
            using (var client = _issuer.IssueClient(_oursKey, "measured", new[] { "reader" }).Read())
            {
                var elapsed = Stopwatch.StartNew();
                var chains = AnchorPin.Chains(client, _ours);
                elapsed.Stop();

                Assert.That(chains, Is.True);
                Assert.That(elapsed.Elapsed, Is.LessThan(Promptly), elapsed.ElapsedMilliseconds + " ms");
            }
        }

        /// <summary>A self-signed authority on an RSA key, which this plugin never makes.</summary>
        private static X509Certificate2 RsaAuthority()
        {
            var keys = new Org.BouncyCastle.Crypto.Generators.RsaKeyPairGenerator();
            keys.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom(), 2048));
            var pair = keys.GenerateKeyPair();

            var name = new X509Name("CN=KeePassGate authority");
            var now = DateTimeOffset.UtcNow;
            var generator = new Org.BouncyCastle.X509.X509V3CertificateGenerator();
            generator.SetSerialNumber(Org.BouncyCastle.Math.BigInteger.One);
            generator.SetIssuerDN(name);
            generator.SetSubjectDN(name);
            generator.SetNotBefore(now.AddMinutes(-5).UtcDateTime);
            generator.SetNotAfter(now.AddYears(1).UtcDateTime);
            generator.SetPublicKey(pair.Public);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(0));

            // cspell:ignore WITHRSA
            var certificate = generator.Generate(
                new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory("SHA256WITHRSA", pair.Private));
            return new X509Certificate2(certificate.GetEncoded());
        }

        /// <summary>Every value in a name as a PrintableString, which is how Windows wrote this plugin's names.</summary>
        private sealed class PrintableNames : X509NameEntryConverter
        {
            public override Org.BouncyCastle.Asn1.Asn1Object GetConvertedValue(
                Org.BouncyCastle.Asn1.DerObjectIdentifier oid, string value)
            {
                return new Org.BouncyCastle.Asn1.DerPrintableString(value);
            }
        }

        /// <summary>
        /// A client certificate under <paramref name="authority"/> carrying an authority
        /// information access extension naming one `caIssuers` location, which `AuthorityIssuer`
        /// never writes and a stranger is free to.
        /// </summary>
        /// <remarks>
        /// RFC 5280 section 4.2.2.1. https://www.rfc-editor.org/rfc/rfc5280#section-4.2.2.1
        /// </remarks>
        private static X509Certificate2 ClientNaming(CertifiedKey authority, string caIssuers)
        {
            // cspell:ignore ADCA
            var location = new GeneralName(GeneralName.UniformResourceIdentifier, caIssuers);
            var now = DateTimeOffset.UtcNow;
            return Minted.Named("CN=stranger")
                .ForUsage(AuthorityIssuer.ClientAuthOid)
                .With(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature))
                .With(
                    X509Extensions.AuthorityInfoAccess,
                    false,
                    new AuthorityInformationAccess(new AccessDescription(AccessDescription.IdADCAIssuers, location)))
                .Lasting(now - AuthorityIssuer.Backdate, now + Lifetime.Unasked)
                .IssuedBy(authority)
                .Read();
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
                    // The same request to end, met by a socket `Stop` had already closed.
                }
                catch (InvalidOperationException)
                {
                    // And again, when `Stop` came between two accepts and the next one found the
                    // listener stopped.
                }
            }
        }
    }
}
