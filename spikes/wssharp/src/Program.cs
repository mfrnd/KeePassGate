// The same proof again, against websocket-sharp's HTTP server.
//
// The integration differs from the other two: nothing is injected. websocket-sharp exposes
// ServerSslConfiguration, and EnabledSslProtocols is passed straight into AuthenticateAsServer,
// so pinning TLS 1.3 and requiring a client certificate is configuration rather than a patch.
//
// Set KPG_PLAIN=1 to run the same request without TLS, which attributes a dispatch failure to the
// transport or to the request rather than guessing.

using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace KeePassGate.WsSharp
{
    internal static class Program
    {
        private const string Raw =
            "POST /v1/entries/lookup?url=example HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Content-Type: application/json\r\n" +
            "Content-Length: 17\r\n" +
            "X-Kpg-Probe: yes\r\n" +
            "Connection: close\r\n" +
            "\r\n" +
            "{\"query\":\"demo\"}\n";

        private static int FreePort()
        {
            TcpListener finder = new TcpListener(IPAddress.Loopback, 0);
            finder.Start();
            int port = ((IPEndPoint)finder.LocalEndpoint).Port;
            finder.Stop();
            return port;
        }

        private static int Main()
        {
            Console.WriteLine("KeePassGate transport proof: websocket-sharp");
            Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine();

            return Environment.GetEnvironmentVariable("KPG_PLAIN") == "1" ? Plain() : Secure();
        }

        /// <summary>No TLS. Isolates whether a dispatch failure is about the transport.</summary>
        private static int Plain()
        {
            int port = FreePort();
            bool dispatched = false;
            HttpServer http = new HttpServer(IPAddress.Loopback, port, false);
            http.OnPost += delegate (object sender, HttpRequestEventArgs e)
            {
                dispatched = true;
                Console.WriteLine("  dispatched " + e.Request.HttpMethod + " " + e.Request.Url.AbsolutePath);
                Respond(e);
            };
            http.Start();
            Console.WriteLine("plain HTTP on 127.0.0.1:" + port);

            // A real client first, to tell a bad hand-written request from a bad server setup.
            try
            {
                System.Net.HttpWebRequest w = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                    "http://127.0.0.1:" + port + "/v1/entries/lookup?url=example");
                w.Method = "POST";
                w.ContentType = "application/json";
                w.Timeout = 5000;
                byte[] payload = Encoding.UTF8.GetBytes("{\"query\":\"demo\"}");
                w.ContentLength = payload.Length;
                using (System.IO.Stream rs = w.GetRequestStream()) rs.Write(payload, 0, payload.Length);
                using (System.Net.WebResponse resp = w.GetResponse()) { }
                Console.WriteLine("  HttpWebRequest: completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  HttpWebRequest failed: " + ex.Message);
            }
            Thread.Sleep(400);
            http.Stop();
            Console.WriteLine(dispatched ? "  plain HTTP: DISPATCHED" : "  plain HTTP: NOT dispatched");
            return dispatched ? 0 : 1;
        }

        private static void Respond(HttpRequestEventArgs e)
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"ok\":true}");
            e.Response.StatusCode = 200;
            e.Response.ContentType = "application/json";
            e.Response.ContentLength64 = body.Length;
            e.Response.OutputStream.Write(body, 0, body.Length);
            e.Response.Close();
        }

        private static int Secure()
        {
            X509Certificate2 ca = Ca.CreateAuthority("KeePassGate Pairing Authority");
            X509Certificate2 server = Ca.IssueServer(ca, "localhost");
            X509Certificate2 client = Ca.IssueClient(ca, "agent-fictitious");

            int failures = 0;
            string method = null, path = null, query = null, probe = null, certSubject = null;
            string seenDuringHandshake = null;

            using (ManualResetEventSlim parsed = new ManualResetEventSlim(false))
            {
                int port = FreePort();
                HttpServer http = new HttpServer(IPAddress.Loopback, port, true);
                http.SslConfiguration.ServerCertificate = server;
                http.SslConfiguration.ClientCertificateRequired = true;
                http.SslConfiguration.CheckCertificateRevocation = false;
                // The pin: this value reaches AuthenticateAsServer unchanged.
                http.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls13;
                http.SslConfiguration.ClientCertificateValidationCallback =
                    delegate (object s, X509Certificate cert, X509Chain ch, System.Net.Security.SslPolicyErrors err)
                    {
                        if (cert == null) return false;
                        using (X509Chain built = new X509Chain())
                        {
                            built.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            built.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                            built.ChainPolicy.ExtraStore.Add(ca);
                            X509Certificate2 c2 = new X509Certificate2(cert);
                            seenDuringHandshake = c2.Subject;
                            if (!built.Build(c2)) return false;
                            foreach (X509ChainElement el in built.ChainElements)
                            {
                                if (el.Certificate.Thumbprint == ca.Thumbprint) return true;
                            }
                        }
                        return false;
                    };

                http.OnPost += delegate (object sender, HttpRequestEventArgs e)
                {
                    method = e.Request.HttpMethod;
                    path = e.Request.Url.AbsolutePath;
                    query = e.Request.Url.Query;
                    probe = e.Request.Headers["X-Kpg-Probe"];
                    // Now genuinely from the request, via the patched GetClientCertificate.
                    X509Certificate2 cc = e.Request.GetClientCertificate();
                    certSubject = cc == null ? null : cc.Subject;
                    Respond(e);
                    parsed.Set();
                };

                http.Start();
                Console.WriteLine("listening on 127.0.0.1:" + port);

                // A real HTTPS client with the client certificate. websocket-sharp turned out to
                // be stricter than the other two parsers about a hand-written request, so the
                // measurement uses a conforming one.
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)12288;
                System.Net.ServicePointManager.ServerCertificateValidationCallback =
                    delegate { return true; };
                string response = "";
                try
                {
                    System.Net.HttpWebRequest w = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                        "https://127.0.0.1:" + port + "/v1/entries/lookup?url=example");
                    w.Method = "POST";
                    w.ContentType = "application/json";
                    w.Headers.Add("X-Kpg-Probe", "yes");
                    w.ClientCertificates.Add(client);
                    w.Timeout = 8000;
                    byte[] payload = Encoding.UTF8.GetBytes("{\"query\":\"demo\"}");
                    w.ContentLength = payload.Length;
                    using (System.IO.Stream rs = w.GetRequestStream()) rs.Write(payload, 0, payload.Length);
                    using (System.Net.WebResponse resp = w.GetResponse())
                    using (System.IO.StreamReader rd = new System.IO.StreamReader(resp.GetResponseStream()))
                        response = rd.ReadToEnd();
                }
                catch (Exception ex) { Console.WriteLine("  [client] " + ex.Message); }

                if (!parsed.Wait(TimeSpan.FromSeconds(10)))
                {
                    Console.WriteLine("FAIL: websocket-sharp never produced a request");
                    Console.WriteLine("      (run with KPG_PLAIN=1 to see whether TLS is the cause)");
                    http.Stop();
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("what the VENDORED parser produced:");
                Console.WriteLine("  method        " + method);
                Console.WriteLine("  path          " + path);
                Console.WriteLine("  query         " + query);
                Console.WriteLine("  custom header " + probe);
                Console.WriteLine("  client cert   " + (certSubject ?? "(none)"));

                failures += Check("method parsed", method == "POST");
                failures += Check("path parsed", path == "/v1/entries/lookup");
                failures += Check("query parsed", query == "?url=example");
                failures += Check("header parsed", probe == "yes");
                failures += Check("client certificate seen", certSubject == "CN=agent-fictitious");
                failures += Check("response reached the client", response.Contains("{\"ok\":true}"));
                http.Stop();
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
