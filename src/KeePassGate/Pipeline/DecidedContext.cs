using System;
using System.Collections.Generic;
using System.Net;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// A request as it stands where it is decided: the same request, asked by the client as the
    /// database holds that client at that moment.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientCredentials.Again"/> has the reason an identity is read again, and
    /// <see cref="DecidedInside"/> where this is handed out. Everything but the caller is the
    /// request's own: the method, the path, the body and the answer all go to the context this
    /// wraps, so there is still one request and one answer.
    ///
    /// **The items are a copy.** What the router leaves for a handler about the caller, the
    /// <see cref="ProtectedReach"/> above all, is left here about the client read again, and the
    /// request's own items go on holding what was decided before, for whoever still asks there.
    /// </remarks>
    internal sealed class DecidedContext : IGateContext
    {
        private readonly IGateContext _request;
        private readonly ClientIdentity _client;
        private readonly IDictionary<string, object> _items;

        public DecidedContext(IGateContext request, ClientIdentity client)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _request = request;
            _client = client;
            _items = new Dictionary<string, object>(request.Items, StringComparer.Ordinal);
        }

        public string Method
        {
            get { return _request.Method; }
        }

        public string Path
        {
            get { return _request.Path; }
        }

        public IReadOnlyDictionary<string, string> Query
        {
            get { return _request.Query; }
        }

        public string Body
        {
            get { return _request.Body; }
        }

        /// <summary>The client as the database holds it where the request is decided.</summary>
        public ClientIdentity Client
        {
            get { return _client; }
        }

        public IDictionary<string, object> Items
        {
            get { return _items; }
        }

        public bool Answered
        {
            get { return _request.Answered; }
        }

        public string GetHeader(string name)
        {
            return _request.GetHeader(name);
        }

        public void Respond(int status, string json)
        {
            _request.Respond(status, json);
        }

        public void Respond(int status, string json, WebHeaderCollection headers)
        {
            _request.Respond(status, json, headers);
        }
    }
}
