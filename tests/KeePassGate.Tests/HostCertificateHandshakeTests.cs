using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Transport;
using NUnit.Framework;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

using KeePassGate.Testing;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A host certificate is no use unless something will serve with it, and where that something
    /// looks for the key is the whole reason this plugin terminates TLS itself.
    /// </summary>
    [TestFixture]
    public sealed class HostCertificateHandshakeTests
    {
        private AuthorityIssuer _issuer;
        private CertifiedKey _authorityKey;
        private X509Certificate2 _authority;
        private CertifiedKey _host;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer();
            _authorityKey = _issuer.CreateAuthority();
            _authority = _authorityKey.Read();

            _host = Stored(_issuer.IssueHost(_authorityKey));
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Reset();
        }

        [Test]
        public void A_host_certificate_we_issued_can_complete_a_handshake()
        {
            Assert.That(Handshake(_host), Is.EqualTo("ok"));
        }

        [Test]
        [NonParallelizable]
        public void Serving_writes_no_key_anywhere()
        {
            // Locked against every other test process this account runs, so that a key one of
            // them writes never lands inside this window: `PrivateKeyFiles.Lock` says why.
            using (PrivateKeyFiles.Lock())
            {
                Serving_writes_no_key_anywhere_while_locked();
            }
        }

        private void Serving_writes_no_key_anywhere_while_locked()
        {
            // The point of the whole exercise. SChannel refuses a key that is not in the user's
            // profile, so serving with it meant writing the host key to the disk for as long as a
            // database was open. This asserts that nothing does.
            //
            // The window is the handshake, and only that. What comes before it, minting a key
            // pair and exporting and importing a PKCS#12, is measured by
            // `CertificateBlobTests.No_key_reaches_the_disk_from_minting_one_to_loading_it_back`,
            // which takes its baseline before any of it happens.
            //
            // **Asked more than once, because the directories are not ours.** They belong to the
            // whole logon session, so a key any other process writes inside the window, the other
            // test assembly `dotnet test` runs beside this one among them, reads exactly like a
            // regression. A server that writes a key writes one every time it serves, so one
            // handshake that sees nothing acquits it, and only every handshake seeing something
            // accuses it. A quiet moment afterwards then tells a machine writing keys throughout
            // from the server, and says so rather than guessing. Each attempt serves a host
            // certificate of its own, so none of them is a second handshake with a key the first
            // one already put somewhere.
            var seen = new System.Collections.Generic.List<string>();

            for (var attempt = 0; attempt < KeyAttempts; attempt++)
            {
                var host = Stored(_issuer.IssueHost(_authorityKey));

                var before = PrivateKeyFiles.Names();
                Assert.That(Handshake(host), Is.EqualTo("ok"));

                var appeared = PrivateKeyFiles.Appeared(before);
                if (appeared.Length == 0)
                {
                    return;
                }

                seen.AddRange(appeared);
            }

            var idle = PrivateKeyFiles.Names();
            Thread.Sleep(KeyQuietWindow);
            if (PrivateKeyFiles.Appeared(idle).Length > 0)
            {
                Assert.Inconclusive(
                    "something else on this machine is writing private keys throughout, so this run " +
                    "cannot tell whether serving wrote one");
            }

            Assert.Fail("a private key reached the disk on every attempt: " + string.Join(", ", seen));
        }

        /// <summary>How many handshakes in a row have to write a key before the server is accused.</summary>
        private const int KeyAttempts = 3;

        /// <summary>How long nothing is done while the key directories are watched for anybody else.</summary>
        private static readonly TimeSpan KeyQuietWindow = TimeSpan.FromMilliseconds(250);

        [Test]
        public void The_same_certificate_is_still_refused_by_SChannel()
        {
            // Kept as the reason the managed stack is here at all. If this ever starts passing,
            // the dependency is worth revisiting. Loaded as the plugin loaded every key while
            // Windows held them, with nothing written to the profile.
            using (var ephemeral = new X509Certificate2(
                       _issuer.IssueHost(_authorityKey).ToPkcs12(),
                       (string)null,
                       X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable))
            {
                Assert.That(ephemeral.HasPrivateKey, Is.True, "the key is there");
                Assert.That(ephemeral.GetECDsaPrivateKey(), Is.Not.Null, "and it signs");
                Assert.That(SChannelHandshake(ephemeral), Is.Not.EqualTo("ok"), "and SChannel still will not have it");
            }
        }

        [Test]
        public void Only_TLS_1_3_is_offered()
        {
            // For the version, and asked why. With TLS 1.2 added to the offer the handshake still
            // failed, because the certificate this server sends is shaped for 1.3 and will not
            // encode under 1.2, so a test that asked only whether it failed stayed green: the
            // refusal has to be the one a server gives for a version it does not speak.
            Assert.That(Handshake(_host, SslProtocols.Tls12), Does.Contain("protocol_version"),
                "A client that will not speak TLS 1.3 is refused for its version rather than accommodated.");
        }

        [Test]
        public void The_certificate_request_names_no_authority_and_one_signature_algorithm()
        {
            // What a client is asked for. No authorities, because a client of the gate needs no
            // help choosing a certificate and every authority's subject is the same name, and a
            // single signature algorithm, ECDSA over P-256 with SHA-256, which a client's key is.
            var request = new GateTlsServer(new BcTlsCrypto(new SecureRandom()), _host)
                .GetCertificateRequest();

            Assert.That(request.CertificateAuthorities, Is.Null.Or.Empty);
            Assert.That(request.SupportedSignatureAlgorithms, Has.Count.EqualTo(1));
            Assert.That(request.SupportedSignatureAlgorithms[0].Hash, Is.EqualTo(HashAlgorithm.sha256));
            Assert.That(request.SupportedSignatureAlgorithms[0].Signature, Is.EqualTo(SignatureAlgorithm.ecdsa));
        }

        [Test]
        public void A_host_certificate_that_cannot_be_read_is_refused_rather_than_half_used()
        {
            Assert.That(CertifiedKey.From(null), Is.Null);
            Assert.That(CertifiedKey.From(Array.Empty<byte>()), Is.Null);
            Assert.That(CertifiedKey.From(new byte[] { 1, 2, 3 }), Is.Null);
        }

        [Test]
        public void The_material_carries_the_certificate_that_was_stored()
        {
            var issued = _issuer.IssueHost(_authorityKey);
            var material = Stored(issued);

            Assert.That(material.CertificateDer, Is.EqualTo(issued.CertificateDer));
            Assert.That(material.PrivateKey.IsPrivate, Is.True);
        }

        /// <summary>What the TLS server is handed: the certificate and key, through the form they are stored in.</summary>
        private static CertifiedKey Stored(CertifiedKey issued)
        {
            return CertifiedKey.From(CertificateBlob.Bytes(CertificateBlob.Save(issued)));
        }

        /// <summary>The managed server, driven by an ordinary .NET client.</summary>
        private static string Handshake(CertifiedKey host, SslProtocols clientProtocols = SslProtocols.Tls13)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = "not reached";

            var server = new Thread(delegate()
            {
                var thrown = Thrown.By(() =>
                {
                    using (var accepted = listener.AcceptTcpClient())
                    {
                        var protocol = new TlsServerProtocol(accepted.GetStream());
                        protocol.Accept(new GateTlsServer(new BcTlsCrypto(new SecureRandom()), host));
                        result = "ok";
                        protocol.Close();
                    }
                });

                if (thrown != null)
                {
                    result = thrown.GetType().Name + ": " + thrown.Message;
                }
            });

            server.IsBackground = true;
            server.Start();

            // The client's half fails whenever the server's does, and what is measured is the
            // server's.
            _ = Thrown.By(() =>
            {
                using (var client = new TcpClient("127.0.0.1", port))
                using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("host.kpg", null, clientProtocols, false);
                }
            });

            server.Join(10000);
            listener.Stop();
            return result;
        }

        /// <summary>The platform's server, for the comparison that explains the dependency.</summary>
        private static string SChannelHandshake(X509Certificate2 serverCertificate)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = "not reached";

            var server = new Thread(delegate()
            {
                var thrown = Thrown.By(() =>
                {
                    using (var accepted = listener.AcceptTcpClient())
                    using (var stream = new SslStream(accepted.GetStream(), false))
                    {
                        stream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls13, false);
                        result = "ok";
                    }
                });

                if (thrown != null)
                {
                    result = thrown.GetType().Name + ": " + thrown.Message;
                }
            });

            server.IsBackground = true;
            server.Start();

            // The client's half fails whenever the server's does, and what is measured is the
            // server's.
            _ = Thrown.By(() =>
            {
                using (var client = new TcpClient("127.0.0.1", port))
                using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("host.kpg", null, SslProtocols.Tls13, false);
                }
            });

            server.Join(10000);
            listener.Stop();
            return result;
        }
    }
}
