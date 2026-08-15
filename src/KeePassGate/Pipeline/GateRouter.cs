using System;
using System.Net;
using KeePassGate.Authority;
using KeePassGate.Transport;
using Newtonsoft.Json;

namespace KeePassGate.Pipeline
{
    /// <summary>Whatever answers a request, once the transport has stopped being involved.</summary>
    public interface IGateHandler
    {
        void Handle(IGateContext context);
    }

    /// <summary>
    /// The whole API, for now: one route that says the transport works, and a terminal refusal for
    /// everything else.
    /// </summary>
    /// <remarks>
    /// TODO(step 11): replaced wholesale in step 11 by the spec router, where the OpenAPI document is the routing
    /// table and handlers bind to `operationId`. What survives from here is the shape: identity is
    /// refused in one place before dispatch, so a route added later is denied by default rather
    /// than by somebody remembering to guard it, and anything undeclared has nowhere to go.
    ///
    /// The status route deliberately reads nothing from the database. There is no authorization
    /// layer yet, so no route may reach a secret, and this one is here to prove a connection rather
    /// than to be useful.
    /// </remarks>
    public sealed class GateRouter : IGateHandler
    {
        public const string StatusPath = "/gate/status";

        private readonly string _version;

        public GateRouter(string version)
        {
            _version = version ?? string.Empty;
        }

        public void Handle(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.Client.IsPaired)
            {
                // One place, before dispatch. The pairing routes are the only ones that will ever
                // take a different answer here, and they do not exist yet.
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            if (string.Equals(context.Path, StatusPath, StringComparison.Ordinal))
            {
                if (!string.Equals(context.Method, "GET", StringComparison.Ordinal))
                {
                    // RFC 9110 section 15.5.6: a 405 says which methods the target does
                    // serve. One route and one method today, so the list is written here;
                    // once the routing table is a document, it is read off that instead.
                    var allowed = new WebHeaderCollection();
                    allowed.Add("Allow", "GET");

                    context.Respond(405, HttpResponse.ErrorBody("method not allowed"), allowed);
                    return;
                }

                context.Respond(200, StatusBody(context));
                return;
            }

            context.Respond(404, HttpResponse.ErrorBody("no such route"));
        }

        private string StatusBody(IGateContext context)
        {
            var database = CertificateDigest.Shorten(context.Client.DatabaseId ?? string.Empty);

            return "{\"ok\":true,\"database\":" + JsonConvert.ToString(database) +
                   ",\"version\":" + JsonConvert.ToString(_version) + "}";
        }
    }
}
