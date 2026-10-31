using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using KeePassGate.Testing;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A client whose TLS is Bouncy Castle's, so that what it presents, how it proves it and which
    /// versions it offers are the test's to choose.
    /// </summary>
    /// <remarks>
    /// **What `SslStream` cannot be asked to do.** It always signs the handshake with the key of the
    /// certificate it presents and offers what the operating system allows, so a client built on
    /// it can never find out whether the gate checks that the key is there, or which versions the
    /// gate refuses and why. This one signs with whatever key it is handed and offers exactly the
    /// versions it is given.
    /// </remarks>
    internal static class BouncyCastleGateClient
    {
        /// <summary>
        /// One request, presenting a certificate and signing the handshake with the key given,
        /// which need not be the certificate's.
        /// </summary>
        public static GateReply Get(
            int port,
            string serverName,
            byte[] certificateDer,
            AsymmetricKeyParameter signingKey,
            string path,
            params ProtocolVersion[] versions)
        {
            var reply = new GateReply();

            var thrown = Thrown.By(() =>
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", port);
                    tcp.ReceiveTimeout = 10000;
                    tcp.SendTimeout = 10000;
                    reply.Connected = true;

                    var protocol = new TlsClientProtocol(tcp.GetStream());
                    try
                    {
                        protocol.Connect(new Client(
                            new BcTlsCrypto(new SecureRandom()), serverName, certificateDer, signingKey, versions));

                        var request = Encoding.ASCII.GetBytes(
                            "GET " + path + " HTTP/1.1\r\nHost: " + serverName + "\r\n\r\n");
                        protocol.Stream.Write(request, 0, request.Length);
                        protocol.Stream.Flush();

                        reply.Raw = ReadToEnd(protocol.Stream);
                    }
                    finally
                    {
                        protocol.Close();
                    }
                }
            });

            if (thrown != null)
            {
                reply.Failure = thrown.GetType().Name + ": " + thrown.Message;
                return reply;
            }

            if (string.IsNullOrEmpty(reply.Raw) || !reply.Raw.StartsWith("HTTP/1.1 ", StringComparison.Ordinal))
            {
                reply.Failure = "the connection ended without an answer";
                return reply;
            }

            reply.Answered = true;
            int status;
            reply.Status = int.TryParse(reply.Raw.Substring(9, 3), out status) ? status : 0;
            var blank = reply.Raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            reply.Body = blank < 0 ? string.Empty : reply.Raw.Substring(blank + 4);
            return reply;
        }

        /// <summary>A P-256 key nobody issued a certificate for.</summary>
        public static AsymmetricKeyParameter AnotherKey()
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, new SecureRandom()));
            return generator.GenerateKeyPair().Private;
        }

        /// <summary>
        /// Everything until the gate closes, with an alert it sends thrown rather than read as the
        /// end.
        /// </summary>
        /// <remarks>
        /// Under TLS 1.3 the client's handshake is over before the gate has checked the client's
        /// certificate, so a gate refusing it says so with an alert that arrives on the first read,
        /// **unless the reset behind it arrives first**: the gate closes with this client's request
        /// unread, and Windows then resets the connection and throws away whatever the client had
        /// not read yet. So what the alert said is the gate's log to answer, not this.
        /// </remarks>
        private static string ReadToEnd(Stream stream)
        {
            var buffer = new byte[4096];
            using (var body = new MemoryStream())
            {
                while (true)
                {
                    int read;
                    try
                    {
                        read = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException error) when (!(error is TlsFatalAlertReceived))
                    {
                        break;
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    body.Write(buffer, 0, read);
                }

                return Encoding.UTF8.GetString(body.ToArray());
            }
        }

        private sealed class Client : DefaultTlsClient
        {
            private readonly BcTlsCrypto _crypto;
            private readonly string _serverName;
            private readonly byte[] _certificateDer;
            private readonly AsymmetricKeyParameter _signingKey;
            private readonly ProtocolVersion[] _versions;

            public Client(
                BcTlsCrypto crypto,
                string serverName,
                byte[] certificateDer,
                AsymmetricKeyParameter signingKey,
                ProtocolVersion[] versions)
                : base(crypto)
            {
                _crypto = crypto;
                _serverName = serverName;
                _certificateDer = certificateDer;
                _signingKey = signingKey;
                _versions = versions == null || versions.Length == 0 ? ProtocolVersion.TLSv13.Only() : versions;
            }

            protected override ProtocolVersion[] GetSupportedVersions()
            {
                return _versions;
            }

            protected override IList<ServerName> GetSniServerNames()
            {
                return new List<ServerName> { new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(_serverName)) };
            }

            public override TlsAuthentication GetAuthentication()
            {
                return new Authentication(this);
            }

            private sealed class Authentication : TlsAuthentication
            {
                private readonly Client _client;

                public Authentication(Client client)
                {
                    _client = client;
                }

                /// <summary>Anything, because what the gate is asked about is the client's side.</summary>
                public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
                {
                }

                public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
                {
                    if (_client._certificateDer == null)
                    {
                        return null;
                    }

                    var entry = new CertificateEntry(new BcTlsCertificate(_client._crypto, _client._certificateDer), null);
                    var certificate = new Certificate(
                        certificateRequest.GetCertificateRequestContext() ?? TlsUtilities.EmptyBytes, new[] { entry });

                    return new BcDefaultTlsCredentialedSigner(
                        new TlsCryptoParameters(_client.m_context),
                        _client._crypto,
                        _client._signingKey,
                        certificate,
                        SignatureAndHashAlgorithm.GetInstance(HashAlgorithm.sha256, SignatureAlgorithm.ecdsa));
                }
            }
        }
    }
}
