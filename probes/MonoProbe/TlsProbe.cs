using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Security;
using BcCertificate = Org.BouncyCastle.Tls.Certificate;
using BcCertificateRequest = Org.BouncyCastle.Tls.CertificateRequest;
using BcHashAlgorithm = Org.BouncyCastle.Tls.HashAlgorithm;

namespace MonoProbe
{
    /// <summary>
    /// The transport's shape on this runtime: a Bouncy Castle TLS 1.3 server over a loopback
    /// `TcpListener`, a P-256 host certificate, a request for a client certificate naming no
    /// authorities, as `GateTlsServer` does, and a Bouncy Castle client that offers one. Nothing in
    /// it is the runtime's own TLS, so a failure here is about sockets, threads or randomness.
    /// </summary>
    internal static class TlsProbe
    {
        private static readonly SignatureAndHashAlgorithm EcdsaSha256 =
            SignatureAndHashAlgorithm.GetInstance(BcHashAlgorithm.sha256, SignatureAlgorithm.ecdsa);

        internal static void Run()
        {
            var later = DateTime.UtcNow.AddYears(100);
            var authority = BouncyCertificates.Authority("CN=Mono probe authority", later);
            var host = BouncyCertificates.Leaf(authority, "CN=Mono probe host", true, later);
            var client = BouncyCertificates.Leaf(authority, "CN=Mono probe client", false, later);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string serverSaw = null;
            Exception serverFailed = null;
            var serving = new Thread(() =>
            {
                try
                {
                    using (var accepted = listener.AcceptTcpClient())
                    {
                        var crypto = new BcTlsCrypto(new SecureRandom());
                        var server = new ProbeServer(crypto, host);
                        var protocol = new TlsServerProtocol(accepted.GetStream());
                        protocol.Accept(server);
                        var reader = new StreamReader(protocol.Stream, Encoding.ASCII);
                        var line = reader.ReadLine();
                        var reply = Encoding.ASCII.GetBytes("echo " + line + "\n");
                        protocol.Stream.Write(reply, 0, reply.Length);
                        serverSaw = "version " + server.Negotiated + ", client certificate "
                            + (server.ClientCertificate == null || server.ClientCertificate.IsEmpty ? "none" : "offered");
                        protocol.Close();
                    }
                }
                catch (Exception error)
                {
                    serverFailed = error;
                }
            }) { IsBackground = true };
            serving.Start();

            Report.Measure("tls.client-handshake", () =>
            {
                using (var connection = new TcpClient())
                {
                    connection.Connect(IPAddress.Loopback, port);
                    var crypto = new BcTlsCrypto(new SecureRandom());
                    var protocol = new TlsClientProtocol(connection.GetStream());
                    protocol.Connect(new ProbeClient(crypto, client));
                    var request = Encoding.ASCII.GetBytes("probe\n");
                    protocol.Stream.Write(request, 0, request.Length);
                    var answer = new StreamReader(protocol.Stream, Encoding.ASCII).ReadLine();
                    protocol.Close();
                    return answer;
                }
            });

            serving.Join(TimeSpan.FromSeconds(10));
            listener.Stop();
            Report.Line("tls.server", serverFailed != null ? Report.Failure(serverFailed) : serverSaw);
        }

        private static TlsCredentialedSigner Signer(TlsContext context, BcTlsCrypto crypto, Issued issued)
        {
            var entry = new CertificateEntry(new BcTlsCertificate(crypto, issued.Der), null);
            var chain = new BcCertificate(TlsUtilities.EmptyBytes, new[] { entry });
            return new BcDefaultTlsCredentialedSigner(
                new TlsCryptoParameters(context), crypto, issued.Key.Private, chain, EcdsaSha256);
        }

        private sealed class ProbeServer : DefaultTlsServer
        {
            private readonly BcTlsCrypto _crypto;
            private readonly Issued _host;

            internal ProbeServer(BcTlsCrypto crypto, Issued host)
                : base(crypto)
            {
                _crypto = crypto;
                _host = host;
            }

            internal BcCertificate ClientCertificate { get; private set; }

            internal string Negotiated => m_context.ServerVersion?.ToString();

            protected override ProtocolVersion[] GetSupportedVersions()
            {
                return ProtocolVersion.TLSv13.Only();
            }

            public override TlsCredentials GetCredentials()
            {
                return Signer(m_context, _crypto, _host);
            }

            public override BcCertificateRequest GetCertificateRequest()
            {
                return new BcCertificateRequest(
                    TlsUtilities.EmptyBytes, new List<SignatureAndHashAlgorithm> { EcdsaSha256 }, null, null);
            }

            public override void NotifyClientCertificate(BcCertificate clientCertificate)
            {
                ClientCertificate = clientCertificate;
            }
        }

        private sealed class ProbeClient : DefaultTlsClient
        {
            private readonly BcTlsCrypto _crypto;
            private readonly Issued _client;

            internal ProbeClient(BcTlsCrypto crypto, Issued client)
                : base(crypto)
            {
                _crypto = crypto;
                _client = client;
            }

            protected override ProtocolVersion[] GetSupportedVersions()
            {
                return ProtocolVersion.TLSv13.Only();
            }

            public override TlsAuthentication GetAuthentication()
            {
                return new Authentication(this);
            }

            private sealed class Authentication : TlsAuthentication
            {
                private readonly ProbeClient _owner;

                internal Authentication(ProbeClient owner)
                {
                    _owner = owner;
                }

                public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
                {
                    // Accepted unseen: whether the server is who it says is the plugin's question,
                    // and this probe asks only whether the handshake completes.
                }

                public TlsCredentials GetClientCredentials(BcCertificateRequest certificateRequest)
                {
                    return Signer(_owner.m_context, _owner._crypto, _owner._client);
                }
            }
        }
    }
}
