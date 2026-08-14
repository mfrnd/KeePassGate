// Is there another hook in websocket-sharp that supports spec-first binding?
//
// Yes. `HttpServer` is a facade with OnGet and OnPost events, and it is not the only way in:
// WebSocketSharp.Net.HttpListener exposes Prefixes, SslConfiguration and GetContext, so the accept
// loop and the pipeline can be ours. This file builds the same spec-first router that the
// uHttpSharp proof builds, on that hook, and runs the same checks.
//
// What it costs is the pipeline and the per-request state that uHttpSharp supplies: the small
// Stage list and GateContext below, which is the honest measure of the difference.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using NJsonSchema;
using WebSocketSharp.Net;

namespace KeePassGate.WsSharp
{
    /// <summary>Per-request state, which this library does not provide and uHttpSharp does.</summary>
    internal sealed class GateContext
    {
        internal HttpListenerContext Http { get; set; }
        internal IList<string> Ceiling { get; set; }
        internal string Body { get; set; }

        internal void Respond(int status, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            Http.Response.StatusCode = status;
            Http.Response.ContentType = "application/json";
            Http.Response.ContentLength64 = payload.Length;
            Http.Response.OutputStream.Write(payload, 0, payload.Length);
            Http.Response.Close();
        }
    }

    /// <summary>Returns true when it has answered, which ends the chain.</summary>
    internal delegate bool Stage(GateContext context);

    internal sealed class SpecOperation
    {
        internal string OperationId { get; set; }
        internal string Template { get; set; }
        internal IDictionary<string, string> PathParameters { get; set; }
        internal IList<string> Ceiling { get; set; }
    }

    internal sealed class SpecRouter
    {
        private readonly OpenApiDocument _document;
        private readonly IDictionary<string, Func<SpecOperation, string>> _handlers =
            new Dictionary<string, Func<SpecOperation, string>>(StringComparer.Ordinal);

        internal SpecRouter(string yaml)
        {
            OpenApiDiagnostic diagnostic;
            _document = new OpenApiStringReader().Read(yaml, out diagnostic);
            if (diagnostic.Errors.Count > 0)
                throw new InvalidOperationException("spec did not parse: " + diagnostic.Errors[0].Message);
        }

        internal int OperationCount { get { return _document.Paths.Sum(p => p.Value.Operations.Count); } }

        internal SpecRouter Bind(string operationId, Func<SpecOperation, string> handler)
        {
            _handlers.Add(operationId, handler);
            return this;
        }

        internal void AssertFullyBound()
        {
            List<string> declared = _document.Paths
                .SelectMany(p => p.Value.Operations.Values).Select(o => o.OperationId).ToList();
            string[] unbound = declared.Where(d => !_handlers.ContainsKey(d)).ToArray();
            if (unbound.Length > 0)
                throw new InvalidOperationException("declared but not implemented: " + string.Join(", ", unbound));
            string[] unknown = _handlers.Keys.Where(k => !declared.Contains(k)).ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException("implemented but not declared: " + string.Join(", ", unknown));
        }

        internal bool Handle(GateContext context)
        {
            string method = context.Http.Request.HttpMethod.ToUpperInvariant();
            string path = context.Http.Request.Url.AbsolutePath;

            foreach (KeyValuePair<string, OpenApiPathItem> entry in _document.Paths)
            {
                IDictionary<string, string> parameters;
                if (!TryMatch(entry.Key, path, out parameters)) continue;

                foreach (KeyValuePair<OperationType, OpenApiOperation> op in entry.Value.Operations)
                {
                    if (!string.Equals(op.Key.ToString(), method, StringComparison.OrdinalIgnoreCase)) continue;

                    string schemaError = ValidateBody(op.Value, context.Body);
                    if (schemaError != null)
                    {
                        context.Respond(400, "{\"error\":\"request does not match the schema\",\"detail\":\"" +
                                             schemaError + "\"}");
                        return true;
                    }

                    Func<SpecOperation, string> handler;
                    if (!_handlers.TryGetValue(op.Value.OperationId, out handler))
                    {
                        context.Respond(501, "{\"error\":\"no handler bound\"}");
                        return true;
                    }

                    context.Respond(200, handler(new SpecOperation
                    {
                        OperationId = op.Value.OperationId,
                        Template = entry.Key,
                        PathParameters = parameters,
                        Ceiling = context.Ceiling,
                    }));
                    return true;
                }
            }
            return false;
        }

        private static bool TryMatch(string template, string path, out IDictionary<string, string> parameters)
        {
            parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] want = template.Trim('/').Split('/');
            string[] got = path.Trim('/').Split('/');
            if (want.Length != got.Length) return false;
            for (int i = 0; i < want.Length; i++)
            {
                if (want[i].StartsWith("{", StringComparison.Ordinal) && want[i].EndsWith("}", StringComparison.Ordinal))
                {
                    parameters[want[i].Trim('{', '}')] = Uri.UnescapeDataString(got[i]);
                    continue;
                }
                if (!string.Equals(want[i], got[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static string ValidateBody(OpenApiOperation operation, string body)
        {
            if (operation.RequestBody == null || string.IsNullOrEmpty(body)) return null;
            OpenApiMediaType media;
            if (!operation.RequestBody.Content.TryGetValue("application/json", out media) || media.Schema == null)
                return null;
            string schemaJson = media.Schema.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
            JsonSchema schema = JsonSchema.FromJsonAsync(schemaJson).GetAwaiter().GetResult();
            var errors = schema.Validate(body);
            return errors.Count == 0 ? null : errors.First().ToString().Replace("\"", "'");
        }
    }

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
                query: { type: string, minLength: 1 }
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
            Console.WriteLine("spec-first binding on websocket-sharp's own HttpListener");
            Console.WriteLine();

            SpecRouter router = new SpecRouter(Spec);
            router.Bind("lookupEntries", op => "{\"operationId\":\"" + op.OperationId + "\",\"template\":\"" + op.Template + "\"}");
            router.Bind("getEntry", op => "{\"operationId\":\"" + op.OperationId + "\",\"entryId\":\"" +
                                          op.PathParameters["entryId"] + "\",\"ceilingEntries\":" + op.Ceiling.Count + "}");
            router.AssertFullyBound();
            Console.WriteLine("  " + router.OperationCount + " operations declared, all bound, none undeclared");

            X509Certificate2 ca = Ca.CreateAuthority("KeePassGate Pairing Authority");
            X509Certificate2 serverCert = Ca.IssueServer(ca, "localhost");
            X509Certificate2 clientCert = Ca.IssueClient(ca, "agent-fictitious");

            int port = FreePort();
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("https://127.0.0.1:" + port + "/");
            listener.SslConfiguration.ServerCertificate = serverCert;
            listener.SslConfiguration.ClientCertificateRequired = true;
            listener.SslConfiguration.CheckCertificateRevocation = false;
            listener.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls13;
            listener.SslConfiguration.ClientCertificateValidationCallback =
                (s, cert, chain, errors) => ChainsTo(cert, ca);
            listener.Start();

            // The pipeline this library does not provide, in full. It is a list and a loop.
            List<Stage> pipeline = new List<Stage>
            {
                context =>
                {
                    X509Certificate2 presented = context.Http.Request.GetClientCertificate();
                    context.Ceiling = ClaimsOf(presented);
                    return false;
                },
                context => router.Handle(context),
                context =>
                {
                    context.Respond(404, "{\"error\":\"not declared by the specification\"}");
                    return true;
                },
            };

            Thread accept = new Thread(() =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext http;
                    try { http = listener.GetContext(); }
                    catch { return; }

                    GateContext context = new GateContext { Http = http, Body = ReadBody(http) };
                    foreach (Stage stage in pipeline)
                    {
                        if (stage(context)) break;
                    }
                }
            }) { IsBackground = true };
            accept.Start();
            Console.WriteLine("  listening on 127.0.0.1:" + port);
            Console.WriteLine();

            int failures = 0;
            string ok = Request(port, clientCert, "POST", "/v1/entries/lookup?x=1", "{\"query\":\"demo\"}");
            Console.WriteLine("  valid body      -> " + ok);
            failures += Check("routed by operationId", ok.Contains("lookupEntries"));

            string bad = Request(port, clientCert, "POST", "/v1/entries/lookup", "{\"nope\":1}");
            Console.WriteLine("  schema violation-> " + bad);
            failures += Check("schema rejected before any handler ran", bad.Contains("400"));

            string got = Request(port, clientCert, "GET", "/v1/entries/abc123", null);
            Console.WriteLine("  path parameter  -> " + got);
            failures += Check("path template captured entryId", got.Contains("abc123"));
            failures += Check("ceiling reached the handler", got.Contains("\"ceilingEntries\":2"));

            string missing = Request(port, clientCert, "GET", "/v1/nothing/here", null);
            failures += Check("undeclared path not routed", missing.Contains("404"));

            listener.Stop();
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static string ReadBody(HttpListenerContext http)
        {
            if (!http.Request.HasEntityBody) return null;
            using (StreamReader reader = new StreamReader(http.Request.InputStream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static bool ChainsTo(X509Certificate cert, X509Certificate2 ca)
        {
            if (cert == null) return false;
            using (X509Chain built = new X509Chain())
            {
                built.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                built.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                built.ChainPolicy.ExtraStore.Add(ca);
                if (!built.Build(new X509Certificate2(cert))) return false;
                foreach (X509ChainElement el in built.ChainElements)
                    if (el.Certificate.Thumbprint == ca.Thumbprint) return true;
            }
            return false;
        }

        private static IList<string> ClaimsOf(X509Certificate2 certificate)
        {
            List<string> claims = new List<string>();
            if (certificate == null) return claims;
            foreach (X509Extension ext in certificate.Extensions)
            {
                if (ext.Oid == null || ext.Oid.Value != "2.5.29.17") continue;
                foreach (string part in ext.Format(true).Split('\n'))
                {
                    int at = part.IndexOf("kpg://", StringComparison.Ordinal);
                    if (at >= 0) claims.Add(part.Substring(at).Trim());
                }
            }
            return claims;
        }

        private static int FreePort()
        {
            System.Net.Sockets.TcpListener finder =
                new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            finder.Start();
            int port = ((System.Net.IPEndPoint)finder.LocalEndpoint).Port;
            finder.Stop();
            return port;
        }

        private static string Request(int port, X509Certificate2 cert, string method, string path, string body)
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)12288;
            System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            try
            {
                System.Net.HttpWebRequest w = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                    "https://127.0.0.1:" + port + path);
                w.Method = method;
                w.ClientCertificates.Add(cert);
                w.Timeout = 8000;
                // The server closes each response, so pooled reuse of a dead connection has to be
                // turned off on the client. A harness detail, not a property of the library.
                w.KeepAlive = false;
                w.ServicePoint.ConnectionLimit = 1;
                if (body != null)
                {
                    w.ContentType = "application/json";
                    byte[] payload = Encoding.UTF8.GetBytes(body);
                    w.ContentLength = payload.Length;
                    using (Stream rs = w.GetRequestStream()) rs.Write(payload, 0, payload.Length);
                }
                using (System.Net.WebResponse resp = w.GetResponse())
                using (StreamReader rd = new StreamReader(resp.GetResponseStream()))
                    return rd.ReadToEnd();
            }
            catch (System.Net.WebException ex)
            {
                System.Net.HttpWebResponse r = ex.Response as System.Net.HttpWebResponse;
                if (r == null) return "transport error: " + ex.Message;
                using (StreamReader rd = new StreamReader(r.GetResponseStream()))
                    return ((int)r.StatusCode) + " " + rd.ReadToEnd();
            }
        }

        private static int Check(string what, bool ok)
        {
            Console.WriteLine("  [" + (ok ? "pass" : "FAIL") + "] " + what);
            return ok ? 0 : 1;
        }
    }
}
