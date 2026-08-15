using System;
using System.Net;
using KeePassGate.Authority;
using KeePassGate.Pairing;
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
    /// The whole API, for now: one route that says the transport works, the pairing routes, which
    /// go to a handler of their own, and a terminal refusal for everything else.
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
    ///
    /// Pairing is the one exception to the refusal, and it is taken in this one place rather than
    /// inside the handlers: `/pair/*` is dispatched before identity is considered, and everything
    /// else needs a certificate and a record. A route added later is still denied by default.
    /// </remarks>
    public sealed class GateRouter : IGateHandler
    {
        public const string StatusPath = "/gate/status";

        private readonly string _version;
        private readonly IGateHandler _pairing;

        public GateRouter(string version)
            : this(version, null)
        {
        }

        public GateRouter(string version, IGateHandler pairing)
        {
            _version = version ?? string.Empty;
            _pairing = pairing;
        }

        public void Handle(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (_pairing != null && PairingHandler.Owns(context.Path))
            {
                // The handler decides whether this connection may be here at all, because only it
                // knows what a pairing connection is, and it answers a connection that may not
                // exactly as the lines below would have. A plugin assembled without pairing does
                // not special-case these paths at all, so they fall through to the same answer as
                // any other name nobody declared: asking for one tells nobody that it exists.
                _pairing.Handle(context);
                return;
            }

            if (!context.Client.IsPaired)
            {
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            if (string.Equals(context.Path, StatusPath, StringComparison.Ordinal))
            {
                if (!string.Equals(context.Method, "GET", StringComparison.Ordinal))
                {
                    // RFC 9110 section 15.5.6: a 405 says which methods the target does
                    // serve. One method on this route today, so the list is written here;
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
