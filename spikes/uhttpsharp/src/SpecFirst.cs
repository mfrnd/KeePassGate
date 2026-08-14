// Does uHttpSharp serve as a foundation for spec-first binding, the way Connexion does?
//
// The document below is the routing table. Handlers are bound to operationId, never to paths, and
// a mismatch between the two is a startup error rather than a 404 found later. The request body is
// checked against the schema the document declares before a handler runs. The certificate's kpg://
// claims are put on the context by the middleware in front, so a handler is handed its ceiling
// rather than fetching it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using uhttpsharp;
using uhttpsharp.RequestProviders;

namespace KeePassGate.UHttp
{
    internal static class SpecFirst
    {
        private const string Spec = @"
openapi: 3.0.3
info:
  title: KeePassGate
  version: 0.1.0
paths:
  /v1/entries/lookup:
    post:
      operationId: lookupEntries
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [query]
              properties:
                query:
                  type: string
                  minLength: 1
      responses:
        '200': { description: matches }
  /v1/entries/{entryId}:
    get:
      operationId: getEntry
      responses:
        '200': { description: the entry }
";

        internal static int Run()
        {
            Console.WriteLine("spec-first binding, Connexion style");
            Console.WriteLine();

            SpecRouter router = new SpecRouter(Spec);
            router.Bind("lookupEntries", delegate (Operation op)
            {
                return "{\"operationId\":\"" + op.OperationId + "\",\"template\":\"" + op.Template + "\"}";
            });
            router.Bind("getEntry", delegate (Operation op)
            {
                return "{\"operationId\":\"" + op.OperationId + "\",\"entryId\":\"" +
                       op.PathParameters["entryId"] + "\",\"ceilingEntries\":" + op.Ceiling.Count + "}";
            });

            try
            {
                router.AssertFullyBound();
                Console.WriteLine("  " + router.OperationCount + " operations declared, all bound, none undeclared");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAIL: " + ex.Message);
                return 1;
            }

            X509Certificate2 ca = Ca.CreateAuthority("KeePassGate Pairing Authority");
            X509Certificate2 serverCert = Ca.IssueServer(ca, "localhost");
            X509Certificate2 clientCert = Ca.IssueClient(ca, "agent-fictitious");

            int failures = 0;
            TlsListener listener = new TlsListener(serverCert, ca);
            using (HttpServer http = new HttpServer(new HttpRequestProvider()))
            {
                http.Use(listener);

                // The ceiling, lifted off the certificate and put on the context before routing.
                http.Use(delegate (IHttpContext context, Func<Task> next)
                {
                    X509Certificate2 presented = listener.LastClientCertificate;
                    context.State.Ceiling = ClaimsOf(presented);
                    return next();
                });

                http.Use(router);

                // Anything the document does not declare is refused here. Spec-first routing is
                // default deny by construction: a path that is not in the document has no
                // handler, so there is nothing to reach.
                http.Use(delegate (IHttpContext context, Func<Task> next)
                {
                    context.Response = new HttpResponse(
                        HttpResponseCode.NotFound, "{\"error\":\"not declared by the specification\"}", true);
                    return Task.FromResult<object>(null);
                });

                http.Start();
                Console.WriteLine("  listening on 127.0.0.1:" + listener.Port);
                Console.WriteLine();

                string ok = Client.Request(listener.Port, clientCert, Post("/v1/entries/lookup", "{\"query\":\"demo\"}"));
                Console.WriteLine("  valid body      -> " + FirstLine(ok) + " " + BodyOf(ok));
                failures += Check("routed by operationId", BodyOf(ok).Contains("lookupEntries"));

                string bad = Client.Request(listener.Port, clientCert, Post("/v1/entries/lookup", "{\"nope\":1}"));
                Console.WriteLine("  schema violation-> " + FirstLine(bad) + " " + BodyOf(bad));
                failures += Check("schema rejected before any handler ran", FirstLine(bad).Contains("400"));

                string got = Client.Request(listener.Port, clientCert, Get("/v1/entries/abc123"));
                Console.WriteLine("  path parameter  -> " + FirstLine(got) + " " + BodyOf(got));
                failures += Check("path template captured entryId", BodyOf(got).Contains("abc123"));
                failures += Check("ceiling reached the handler", BodyOf(got).Contains("\"ceilingEntries\":2"));

                string missing = Client.Request(listener.Port, clientCert, Get("/v1/nothing/here"));
                failures += Check("undeclared path not routed", !BodyOf(missing).Contains("operationId"));
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>The kpg:// entries of the certificate's subject alternative name.</summary>
        private static IList<string> ClaimsOf(X509Certificate2 certificate)
        {
            List<string> claims = new List<string>();
            if (certificate == null)
            {
                return claims;
            }
            foreach (X509Extension ext in certificate.Extensions)
            {
                if (ext.Oid == null || ext.Oid.Value != "2.5.29.17")
                {
                    continue;
                }
                foreach (string part in ext.Format(true).Split('\n'))
                {
                    int at = part.IndexOf("kpg://", StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        claims.Add(part.Substring(at).Trim());
                    }
                }
            }
            return claims;
        }

        private static string Post(string path, string body)
        {
            return "POST " + path + " HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\n" +
                   "Content-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body;
        }

        private static string Get(string path)
        {
            return "GET " + path + " HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        }

        private static string FirstLine(string response)
        {
            int nl = response.IndexOf('\r');
            return nl < 0 ? response.Trim() : response.Substring(0, nl);
        }

        private static string BodyOf(string response)
        {
            int at = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return at < 0 ? "" : response.Substring(at + 4).Trim();
        }

        private static int Check(string what, bool ok)
        {
            Console.WriteLine("  [" + (ok ? "pass" : "FAIL") + "] " + what);
            return ok ? 0 : 1;
        }
    }
}
