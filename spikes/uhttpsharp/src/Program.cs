// The same proof as the sibling port, against uHttpSharp instead: our TLS 1.3 and mutual TLS,
// its parser, one request, the same assertions.
//
// The difference worth measuring is how much of uHttpSharp had to change. The answer is nothing:
// it exposes IClient and IHttpListener, so an authenticated stream is fed in by implementing two
// small interfaces rather than by patching a constructor.

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using uhttpsharp;
using uhttpsharp.Clients;
using uhttpsharp.Listeners;
using uhttpsharp.RequestProviders;

namespace KeePassGate.UHttp
{
    /// <summary>An already-authenticated stream, presented as one of its clients.</summary>
    internal sealed class AuthenticatedClient : IClient
    {
        private readonly TcpClient _tcp;

        internal AuthenticatedClient(TcpClient tcp, Stream authenticated, X509Certificate2? clientCertificate)
        {
            _tcp = tcp;
            Stream = authenticated;
            ClientCertificate = clientCertificate;
        }

        public Stream Stream { get; }
        public bool Connected { get { return _tcp.Connected; } }
        public EndPoint RemoteEndPoint { get { return _tcp.Client.RemoteEndPoint; } }
        internal X509Certificate2? ClientCertificate { get; }

        public void Close()
        {
            try { Stream.Dispose(); } catch { }
            try { _tcp.Close(); } catch { }
        }
    }

    /// <summary>
    /// Accepts, terminates TLS 1.3 exclusively, requires a client certificate, and hands the
    /// authenticated stream over. Everything past the handover is uHttpSharp's, unmodified.
    /// </summary>
    internal sealed class TlsListener : IHttpListener
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _server;
        private readonly X509Certificate2 _ca;

        internal int Port { get; private set; }
        internal X509Certificate2? LastClientCertificate { get; private set; }

        internal TlsListener(X509Certificate2 server, X509Certificate2 ca)
        {
            _server = server;
            _ca = ca;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public async Task<IClient> GetClient()
        {
            while (true)
            {
                TcpClient tcp = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                try
                {
                    SslStream ssl = new SslStream(tcp.GetStream(), false, Validate);
                    ssl.AuthenticateAsServer(_server, true, SslProtocols.Tls13, false);
                    Console.WriteLine("  [host] negotiated " + ssl.SslProtocol);

                    X509Certificate2? cert = ssl.RemoteCertificate == null
                        ? null : new X509Certificate2(ssl.RemoteCertificate);
                    LastClientCertificate = cert;
                    return new AuthenticatedClient(tcp, ssl, cert);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [host] refused: " + ex.GetType().Name);
                    try { tcp.Close(); } catch { }
                }
            }
        }

        private bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (certificate == null) return false;
            using (X509Chain built = new X509Chain())
            {
                built.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                built.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                built.ChainPolicy.ExtraStore.Add(_ca);
                if (!built.Build(new X509Certificate2(certificate))) return false;
                foreach (X509ChainElement el in built.ChainElements)
                {
                    if (el.Certificate.Thumbprint == _ca.Thumbprint) return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("KeePassGate transport proof: uHttpSharp");
            Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine();

            X509Certificate2 ca = Ca.CreateAuthority("KeePassGate Pairing Authority");
            X509Certificate2 server = Ca.IssueServer(ca, "localhost");
            X509Certificate2 client = Ca.IssueClient(ca, "agent-fictitious");

            int failures = 0;
            using (ManualResetEventSlim parsed = new ManualResetEventSlim(false))
            {
                string? method = null, path = null, query = null, probe = null;

                TlsListener listener = new TlsListener(server, ca);
                using (HttpServer http = new HttpServer(new HttpRequestProvider()))
                {
                    http.Use(listener);
                    http.Use(delegate (IHttpContext context, Func<Task> next)
                    {
                        method = context.Request.Method.ToString();
                        path = context.Request.Uri.OriginalString;
                        query = string.Join("&", context.Request.QueryString.Select(kv => kv.Key + "=" + kv.Value).ToArray());
                        probe = context.Request.Headers.GetByName("x-kpg-probe");
                        context.Response = new uhttpsharp.HttpResponse(
                            HttpResponseCode.Ok, "{\"ok\":true}", true);
                        parsed.Set();
                        return Task.FromResult<object?>(null);
                    });
                    http.Start();
                    Console.WriteLine("listening on 127.0.0.1:" + listener.Port);

                    string response = Client.Request(listener.Port, client,
                        "POST /v1/entries/lookup?url=example HTTP/1.1\r\n" +
                        "Host: localhost\r\n" +
                        "Content-Type: application/json\r\n" +
                        "Content-Length: 17\r\n" +
                        "X-Kpg-Probe: yes\r\n" +
                        "\r\n" +
                        "{\"query\":\"demo\"}\n");

                    if (!parsed.Wait(TimeSpan.FromSeconds(10)))
                    {
                        Console.WriteLine("FAIL: uHttpSharp never produced a context");
                        return 1;
                    }

                    Console.WriteLine();
                    Console.WriteLine("what the VENDORED parser produced from our stream:");
                    Console.WriteLine("  method        " + method);
                    Console.WriteLine("  path          " + path);
                    Console.WriteLine("  query         " + query);
                    Console.WriteLine("  custom header " + probe);
                    Console.WriteLine("  client cert   " + (listener.LastClientCertificate?.Subject ?? "(none)"));

                    failures += Check("method parsed", method == "Post" || method == "POST");
                    failures += Check("path parsed", path != null && path.StartsWith("/v1/entries/lookup"));
                    failures += Check("query parsed", query == "url=example");
                    failures += Check("header parsed", probe == "yes");
                    failures += Check("client certificate seen",
                        listener.LastClientCertificate?.Subject == "CN=agent-fictitious");
                    failures += Check("response reached the client", response.Contains("{\"ok\":true}"));
                }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static int Check(string what, bool ok)
        {
            Console.WriteLine("  [" + (ok ? "pass" : "FAIL") + "] " + what);
            return ok ? 0 : 1;
        }
    }
}
