// Spec-first binding, the way Connexion does it in Python: the OpenAPI document is the routing
// table, handlers are registered against operationId, and the request body is validated against
// the schema the document declares before any handler sees it.
//
// This exists to answer whether uHttpSharp is a usable foundation for that, so it is deliberately
// small: path templates, one parameter style, body validation, and the ceiling check. It is not a
// framework.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using NJsonSchema;
using uhttpsharp;

namespace KeePassGate.UHttp
{
    /// <summary>What a handler is given: the operation it was matched to, and the request.</summary>
    internal sealed class Operation
    {
        internal string OperationId { get; set; }
        internal string Method { get; set; }
        internal string Template { get; set; }
        internal IDictionary<string, string> PathParameters { get; set; }
        internal string Body { get; set; }
        /// <summary>The kpg:// claims from the client certificate. The ceiling, never a grant.</summary>
        internal IList<string> Ceiling { get; set; }
    }

    internal sealed class SpecRouter : IHttpRequestHandler
    {
        private readonly OpenApiDocument _document;
        private readonly IDictionary<string, Func<Operation, string>> _handlers =
            new Dictionary<string, Func<Operation, string>>(StringComparer.Ordinal);

        internal SpecRouter(string openApiYaml)
        {
            OpenApiDiagnostic diagnostic;
            _document = new OpenApiStringReader().Read(openApiYaml, out diagnostic);
            if (diagnostic.Errors.Count > 0)
            {
                throw new InvalidOperationException("spec did not parse: " + diagnostic.Errors[0].Message);
            }
        }

        internal int OperationCount
        {
            get { return _document.Paths.Sum(p => p.Value.Operations.Count); }
        }

        /// <summary>Bind a handler to an operationId. Unbound operations are a startup error.</summary>
        internal SpecRouter Bind(string operationId, Func<Operation, string> handler)
        {
            _handlers.Add(operationId, handler);
            return this;
        }

        /// <summary>
        /// Every operation the document declares must have a handler, and every handler must
        /// answer to a declared operation. Checked once, at startup, because a spec and an
        /// implementation that disagree is the failure this whole approach exists to prevent.
        /// </summary>
        internal void AssertFullyBound()
        {
            List<string> declared = _document.Paths
                .SelectMany(p => p.Value.Operations.Values)
                .Select(o => o.OperationId)
                .ToList();

            string[] unbound = declared.Where(d => !_handlers.ContainsKey(d)).ToArray();
            if (unbound.Length > 0)
            {
                throw new InvalidOperationException("declared but not implemented: " + string.Join(", ", unbound));
            }

            string[] unknown = _handlers.Keys.Where(k => !declared.Contains(k)).ToArray();
            if (unknown.Length > 0)
            {
                throw new InvalidOperationException("implemented but not declared: " + string.Join(", ", unknown));
            }
        }

        public async Task Handle(IHttpContext context, Func<Task> next)
        {
            string method = context.Request.Method.ToString().ToUpperInvariant();
            string path = context.Request.Uri.OriginalString.Split('?')[0];

            foreach (KeyValuePair<string, OpenApiPathItem> entry in _document.Paths)
            {
                IDictionary<string, string> parameters;
                if (!TryMatch(entry.Key, path, out parameters))
                {
                    continue;
                }

                foreach (KeyValuePair<OperationType, OpenApiOperation> op in entry.Value.Operations)
                {
                    if (!string.Equals(op.Key.ToString(), method, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string body = ReadBody(context);
                    string schemaError = ValidateBody(op.Value, body);
                    if (schemaError != null)
                    {
                        context.Response = Json(HttpResponseCode.BadRequest,
                            "{\"error\":\"request does not match the schema\",\"detail\":\"" + schemaError + "\"}");
                        return;
                    }

                    Func<Operation, string> handler;
                    if (!_handlers.TryGetValue(op.Value.OperationId, out handler))
                    {
                        context.Response = Json(HttpResponseCode.NotImplemented,
                            "{\"error\":\"no handler bound for " + op.Value.OperationId + "\"}");
                        return;
                    }

                    Operation call = new Operation
                    {
                        OperationId = op.Value.OperationId,
                        Method = method,
                        Template = entry.Key,
                        PathParameters = parameters,
                        Body = body,
                        Ceiling = (IList<string>)context.State.Ceiling,
                    };
                    context.Response = Json(HttpResponseCode.Ok, handler(call));
                    return;
                }
            }

            await next().ConfigureAwait(false);
        }

        /// <summary>Matches /v1/entries/{id} against /v1/entries/abc, capturing the parameters.</summary>
        private static bool TryMatch(string template, string path, out IDictionary<string, string> parameters)
        {
            parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] want = template.Trim('/').Split('/');
            string[] got = path.Trim('/').Split('/');
            if (want.Length != got.Length)
            {
                return false;
            }

            for (int i = 0; i < want.Length; i++)
            {
                if (want[i].StartsWith("{", StringComparison.Ordinal) &&
                    want[i].EndsWith("}", StringComparison.Ordinal))
                {
                    parameters[want[i].Trim('{', '}')] = Uri.UnescapeDataString(got[i]);
                    continue;
                }
                if (!string.Equals(want[i], got[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ReadBody(IHttpContext context)
        {
            if (context.Request.Post == null || context.Request.Post.Raw == null)
            {
                return null;
            }
            return System.Text.Encoding.UTF8.GetString(context.Request.Post.Raw);
        }

        /// <summary>Validates the body against the schema the document declares for it.</summary>
        private static string ValidateBody(OpenApiOperation operation, string body)
        {
            if (operation.RequestBody == null || string.IsNullOrEmpty(body))
            {
                return null;
            }

            OpenApiMediaType media;
            if (!operation.RequestBody.Content.TryGetValue("application/json", out media) || media.Schema == null)
            {
                return null;
            }

            string schemaJson = media.Schema.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
            JsonSchema schema = JsonSchema.FromJsonAsync(schemaJson).GetAwaiter().GetResult();
            var errors = schema.Validate(body);
            if (errors.Count == 0)
            {
                return null;
            }
            return errors.First().ToString().Replace("\"", "'");
        }

        private static IHttpResponse Json(HttpResponseCode code, string payload)
        {
            return new HttpResponse(code, payload, false);
        }
    }
}
