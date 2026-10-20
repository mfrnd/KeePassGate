using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What every reader on the path makes of a certificate meant to outlive the database it is
    /// in, at the two ends the authority could have been given: none at all, and a hundred years.
    /// </summary>
    /// <remarks>
    /// The measurement that settles how long an authority and its host certificate live, kept as
    /// tests. The operator asked for no expiry, RFC 5280's `99991231235959Z`, and for a hundred
    /// years if that turned out not to be fully supported. The builder writes the first as the RFC asks, and `X509Certificate2.NotAfter`
    /// cannot read it back in a zone ahead of UTC: it has no `DateTime` for a local moment past
    /// the end of year 9999, so it answers the last one there is and says nothing. A hundred
    /// years is read exactly by everything here, in every zone tried.
    ///
    /// The certificates are built here rather than by `AuthorityIssuer`, because what is pinned is
    /// what the platform does with a value, whatever the issuer happens to give.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class LongLivedCertificateTests
    {
        /// <summary>RFC 5280 section 4.1.2.5: "no well-defined expiration date".</summary>
        private static readonly DateTimeOffset NoWellDefinedExpiration =
            new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);

        /// <summary>
        /// The zones each reading is asked in: the furthest ahead of UTC there is, one hour ahead,
        /// UTC itself, and the furthest behind.
        /// </summary>
        private static readonly int[] Zones = { 14, 1, 0, -12 };

        private DateTimeOffset _century;
        private X509Certificate2 _authority;
        private X509Certificate2 _host;
        private X509Certificate2 _client;

        [SetUp]
        public void SetUp()
        {
            // To the second, which is all a certificate keeps of a moment.
            var now = DateTimeOffset.UtcNow;
            _century = new DateTimeOffset(now.Year + 100, now.Month, Math.Min(now.Day, 28), now.Hour, now.Minute, now.Second, TimeSpan.Zero);

            _authority = Authority(_century);
            _host = Leaf(_authority, AuthorityIssuer.ServerAuthOid, _century);
            _client = Leaf(_authority, AuthorityIssuer.ClientAuthOid, now.AddDays(365));
        }

        [TearDown]
        public void TearDown()
        {
            _client.Reset();
            _host.Reset();
            _authority.Reset();
        }

        [Test]
        public void The_builder_writes_no_well_defined_expiration_as_RFC_5280_asks()
        {
            using (var never = Authority(NoWellDefinedExpiration))
            {
                Assert.That(EndOf(never.RawData), Is.EqualTo("GeneralizedTime 99991231235959Z"));
            }
        }

        [Test]
        public void NotAfter_cannot_hold_no_well_defined_expiration_in_a_zone_ahead_of_UTC()
        {
            // The reason the authority does not use it. Ahead of UTC the local moment would be in
            // the year 10000, and what comes back instead is the last moment a `DateTime` can hold,
            // which converted back to UTC is that many hours early. No exception says so.
            byte[] der;
            using (var never = Authority(NoWellDefinedExpiration))
            {
                der = never.RawData;
            }

            using (LocalTimeZone.At(1))
            using (var read = new X509Certificate2(der))
            {
                Assert.That(read.NotAfter, Is.EqualTo(DateTime.MaxValue));
                Assert.That(Utc(read.NotAfter), Is.LessThan(NoWellDefinedExpiration),
                    "converted back, it is an earlier moment than the certificate says");
            }

            foreach (var hours in new[] { 0, -12 })
            {
                using (LocalTimeZone.At(hours))
                using (var read = new X509Certificate2(der))
                {
                    Assert.That(Utc(read.NotAfter), Is.EqualTo(NoWellDefinedExpiration),
                        "read exactly at " + Zone(hours));
                }
            }
        }

        [Test]
        public void A_hundred_years_is_written_as_generalized_time()
        {
            // RFC 5280 section 4.1.2.5: UTCTime through 2049, and GeneralizedTime from 2050.
            Assert.That(EndOf(_authority.RawData), Is.EqualTo("GeneralizedTime " + Written(_century)));
            Assert.That(EndOf(_host.RawData), Is.EqualTo("GeneralizedTime " + Written(_century)));
        }

        [Test]
        public void NotAfter_reads_a_hundred_years_back_exactly_in_every_zone()
        {
            foreach (var hours in Zones)
            {
                using (LocalTimeZone.At(hours))
                using (var authority = new X509Certificate2(_authority.RawData))
                using (var host = new X509Certificate2(_host.RawData))
                {
                    var zone = "at " + Zone(hours);
                    Assert.That(Utc(authority.NotAfter), Is.EqualTo(_century), "the authority " + zone);
                    Assert.That(Utc(host.NotAfter), Is.EqualTo(_century), "the host " + zone);
                }
            }
        }

        [Test]
        public void The_anchor_pin_chains_a_hundred_year_host_and_a_client_in_every_zone()
        {
            // With the dates ignored, which is how the plugin always asks, and judged, which is
            // the chain engine reading them itself.
            foreach (var hours in Zones)
            {
                using (LocalTimeZone.At(hours))
                using (var authority = new X509Certificate2(_authority.RawData))
                using (var host = new X509Certificate2(_host.RawData))
                using (var client = new X509Certificate2(_client.RawData))
                {
                    var zone = " at " + Zone(hours);
                    Assert.That(AnchorPin.Chains(host, authority, true), Is.True, "the host" + zone);
                    Assert.That(AnchorPin.Chains(host, authority, false), Is.True, "the host, dates judged" + zone);
                    Assert.That(AnchorPin.Chains(client, authority, true), Is.True, "the client" + zone);
                    Assert.That(AnchorPin.Chains(client, authority, false), Is.True, "the client, dates judged" + zone);
                }
            }
        }

        [Test]
        public void Bouncy_Castle_reads_a_hundred_years_out_of_the_stored_form()
        {
            var material = HostKeyMaterial.From(CertificateBlob.Bytes(CertificateBlob.Save(_host)));

            Assert.That(material, Is.Not.Null, "the PKCS#12 the database keeps loads");
            var read = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(material.CertificateDer);
            Assert.That(new DateTimeOffset(read.NotAfter, TimeSpan.Zero), Is.EqualTo(_century));
        }

        [Test]
        public void The_gate_serves_a_hundred_year_host_that_a_client_pinning_its_authority_accepts()
        {
            var material = HostKeyMaterial.From(CertificateBlob.Bytes(CertificateBlob.Save(_host)));
            var accepted = false;

            var served = Handshake(material, delegate(object sender, X509Certificate sent, X509Chain chain, SslPolicyErrors errors)
            {
                using (var certificate = new X509Certificate2(sent))
                {
                    accepted = AnchorPin.Chains(certificate, _authority, false)
                               && CertificateValidity.IsCurrent(certificate, DateTimeOffset.UtcNow)
                               && Utc(certificate.NotAfter) == _century;
                }

                return accepted;
            });

            Assert.That(served, Is.EqualTo("ok"));
            Assert.That(accepted, Is.True, "what was sent chains, is current, and ends when it was issued to");
        }

        /// <summary>How a zone is named in a failure message.</summary>
        private static string Zone(int hours)
        {
            return "UTC" + hours.ToString("+0;-0;+0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A moment off a certificate, in UTC, as everything in the plugin reads one.</summary>
        private static DateTimeOffset Utc(DateTime moment)
        {
            return new DateTimeOffset(moment.ToUniversalTime(), TimeSpan.Zero);
        }

        /// <summary>How RFC 5280 writes a moment as GeneralizedTime.</summary>
        private static string Written(DateTimeOffset moment)
        {
            return moment.UtcDateTime.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "Z";
        }

        /// <summary>
        /// The end of a certificate's validity as it is encoded: the type its tag says and the text
        /// inside it. Read off the DER rather than off a property, because a property is a reading
        /// and the question is what was written.
        /// </summary>
        private static string EndOf(byte[] der)
        {
            var end = X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(der)).EndDate.ToAsn1Object();
            var encoded = end.GetEncoded();
            var kind = encoded[0] == 0x18 ? "GeneralizedTime" : encoded[0] == 0x17 ? "UTCTime" : "tag " + encoded[0];
            return kind + " " + Encoding.ASCII.GetString(encoded, 2, encoded[1]);
        }

        private static X509Certificate2 Authority(DateTimeOffset notAfter)
        {
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName(AuthorityIssuer.AuthoritySubject), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                using (var made = request.CreateSelfSigned(DateTimeOffset.UtcNow - AuthorityIssuer.Backdate, notAfter))
                {
                    return CertificateBlob.Load(CertificateBlob.Save(made));
                }
            }
        }

        private static X509Certificate2 Leaf(X509Certificate2 authority, string usage, DateTimeOffset notAfter)
        {
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName("CN=invented leaf"), key, HashAlgorithmName.SHA256);

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(usage) }, false));
                request.CertificateExtensions.Add(AuthorityIssuer.AuthorityKeyIdentifierOf(authority));

                using (var issued = request.Create(
                    authority, DateTimeOffset.UtcNow - AuthorityIssuer.Backdate, notAfter, AuthorityIssuer.NewSerialNumber()))
                using (var withKey = issued.CopyWithPrivateKey(key))
                {
                    return CertificateBlob.Load(CertificateBlob.Save(withKey));
                }
            }
        }

        /// <summary>The gate's own TLS server, driven by an ordinary .NET client that decides for itself.</summary>
        private static string Handshake(HostKeyMaterial host, RemoteCertificateValidationCallback decide)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = "not reached";

            var server = new Thread(delegate()
            {
                try
                {
                    using (var accepted = listener.AcceptTcpClient())
                    {
                        var protocol = new Org.BouncyCastle.Tls.TlsServerProtocol(accepted.GetStream());
                        protocol.Accept(new GateTlsServer(new BcTlsCrypto(new SecureRandom()), host));
                        result = "ok";
                        protocol.Close();
                    }
                }
                catch (Exception error)
                {
                    result = error.GetType().Name + ": " + error.Message;
                }
            });

            server.IsBackground = true;
            server.Start();

            try
            {
                using (var client = new TcpClient("127.0.0.1", port))
                using (var stream = new SslStream(client.GetStream(), false, decide))
                {
                    stream.AuthenticateAsClient("host.kpg", null, SslProtocols.Tls13, false);
                }
            }
            catch (AuthenticationException)
            {
                // The client refusing what it was sent, which the assertions report in their
                // own words.
            }

            server.Join(10000);
            listener.Stop();
            return result;
        }
    }
}
