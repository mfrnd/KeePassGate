using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private X509Certificate2 _authority;
        private HostKeyMaterial _host;

        [SetUp]
        public void SetUp()
        {
            _issuer = new AuthorityIssuer();
            _authority = _issuer.CreateAuthority();

            using (var issued = _issuer.IssueHost(_authority))
            {
                _host = HostKeyMaterial.From(CertificateBlob.Bytes(CertificateBlob.Save(issued)));
            }
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
            // The point of the whole exercise. SChannel refuses a key that is not in the user's
            // profile, so serving with it meant writing the host key to the disk for as long as a
            // database was open. This asserts that nothing does.
            var before = KeyFiles();

            Assert.That(Handshake(_host), Is.EqualTo("ok"));

            Assert.That(Appeared(before), Is.Empty, "a private key reached the disk");
        }

        [Test]
        public void The_same_certificate_is_still_refused_by_SChannel()
        {
            // Kept as the reason the managed stack is here at all. If this ever starts passing,
            // the dependency is worth revisiting.
            using (var ephemeral = CertificateBlob.Load(CertificateBlob.Save(WithKey())))
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
        public void A_host_certificate_that_cannot_be_read_is_refused_rather_than_half_used()
        {
            Assert.That(HostKeyMaterial.From(null), Is.Null);
            Assert.That(HostKeyMaterial.From(Array.Empty<byte>()), Is.Null);
            Assert.That(HostKeyMaterial.From(new byte[] { 1, 2, 3 }), Is.Null);
        }

        [Test]
        public void The_material_carries_the_certificate_that_was_stored()
        {
            using (var issued = _issuer.IssueHost(_authority))
            {
                var material = HostKeyMaterial.From(CertificateBlob.Bytes(CertificateBlob.Save(issued)));

                Assert.That(material.CertificateDer, Is.EqualTo(issued.RawData));
                Assert.That(material.PrivateKey.IsPrivate, Is.True);
            }
        }

        private X509Certificate2 WithKey()
        {
            return _issuer.IssueHost(_authority);
        }

        /// <summary>
        /// The names of every private key file this account holds, in both places Windows keeps
        /// them.
        /// </summary>
        /// <remarks>
        /// **Names rather than a count, and both directories.** These are shared by the whole logon
        /// session, so a count changes when anything else on the machine creates or removes a key,
        /// and a test that compared counts failed while nothing was wrong. Comparing names and
        /// asking only whether one appeared leaves the far commoner case, something else tidying up
        /// its own key, unable to fail this. And `Crypto\Keys` is CNG only: a regression that wrote
        /// through a legacy provider would land in `Crypto\RSA` and go unseen, so both are read.
        /// </remarks>
        private static HashSet<string> KeyFiles()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in new[] { @"Microsoft\Crypto\Keys", @"Microsoft\Crypto\RSA" })
            {
                var path = Path.Combine(appData, directory);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    names.Add(file);
                }
            }

            return names;
        }

        /// <summary>What appeared while something was being done, which is the only direction that means anything.</summary>
        private static string[] Appeared(HashSet<string> before)
        {
            var after = KeyFiles();
            after.ExceptWith(before);
            return after.ToArray();
        }

        /// <summary>The managed server, driven by an ordinary .NET client.</summary>
        private static string Handshake(HostKeyMaterial host, SslProtocols clientProtocols = SslProtocols.Tls13)
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
                        var protocol = new TlsServerProtocol(accepted.GetStream());
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
                using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("host.kpg", null, clientProtocols, false);
                }
            }
            catch (Exception)
            {
            }

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
                try
                {
                    using (var accepted = listener.AcceptTcpClient())
                    using (var stream = new SslStream(accepted.GetStream(), false))
                    {
                        stream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls13, false);
                        result = "ok";
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
                using (var stream = new SslStream(client.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("host.kpg", null, SslProtocols.Tls13, false);
                }
            }
            catch (Exception)
            {
            }

            server.Join(10000);
            listener.Stop();
            return result;
        }
    }
}
