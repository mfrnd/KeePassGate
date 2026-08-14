// The half this project keeps: accept the connection, terminate TLS on our own terms, and hand
// the authenticated stream to the vendored parser.
//
// The TLS version is pinned by OFFERING only TLS 1.3 rather than by negotiating freely and
// checking afterwards. By the time SslStream.SslProtocol can be read the handshake is over and the
// client certificate has already been sent, so a check detects a leak where a refusal prevents one.

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Vendor.Http;
using HttpListenerContext = KeePassGate.Vendor.Http.HttpListenerContext;
using HttpListenerRequest = KeePassGate.Vendor.Http.HttpListenerRequest;

namespace KeePassGate.Spike
{
    internal sealed class TlsHost : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _serverCertificate;
        private readonly X509Certificate2 _clientCaCertificate;
        private readonly HttpEndPointListener _endPoint;
        private readonly Action<HttpListenerContext> _onRequest;
        private Thread? _acceptThread;
        private volatile bool _running;

        internal int Port { get; private set; }

        internal TlsHost(X509Certificate2 serverCertificate, X509Certificate2 clientCaCertificate,
                         Action<HttpListenerContext> onRequest)
        {
            _serverCertificate = serverCertificate;
            _clientCaCertificate = clientCaCertificate;
            _onRequest = onRequest;
            _endPoint = new HttpEndPointListener(OnParsedRequest);
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        private void OnParsedRequest(HttpListenerContext context)
        {
            _onRequest(context);
        }

        internal void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "kpg-accept" };
            _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (InvalidOperationException) { return; }

                ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
            }
        }

        private void Handle(TcpClient client)
        {
            SslStream? ssl = null;
            try
            {
                ssl = new SslStream(client.GetStream(), false, ValidateClient);

                // Offer TLS 1.3 and nothing else, and require a client certificate.
                ssl.AuthenticateAsServer(_serverCertificate, true, SslProtocols.Tls13, false);

                if (ssl.SslProtocol != SslProtocols.Tls13)
                {
                    // Unreachable while only 1.3 is offered, and worth asserting anyway.
                    throw new AuthenticationException("negotiated " + ssl.SslProtocol);
                }

                X509Certificate2? clientCert = ssl.RemoteCertificate == null
                    ? null
                    : new X509Certificate2(ssl.RemoteCertificate);

                // Everything above is ours. Everything below is upstream's parser, reading a
                // stream that is already authenticated.
                HttpConnection connection = new HttpConnection(client.Client, _endPoint, ssl, clientCert);
                _endPoint.AddConnection(connection);
                connection.BeginReadRequest();
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [host] connection refused: " + ex.GetType().Name + ": " + ex.Message);
                try { ssl?.Dispose(); } catch { }
                try { client.Close(); } catch { }
            }
        }

        /// <summary>
        /// The client certificate must chain to the pairing authority. A real implementation would
        /// also consult the local registry of admitted fingerprints; this proves the transport.
        /// </summary>
        private bool ValidateClient(object sender, X509Certificate? certificate, X509Chain? chain,
                                    SslPolicyErrors errors)
        {
            if (certificate == null) return false;

            using (X509Chain built = new X509Chain())
            {
                built.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                built.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                built.ChainPolicy.ExtraStore.Add(_clientCaCertificate);
                if (!built.Build(new X509Certificate2(certificate))) return false;

                foreach (X509ChainElement element in built.ChainElements)
                {
                    if (element.Certificate.Thumbprint == _clientCaCertificate.Thumbprint) return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }
    }
}
