// The socket accept layer, replaced rather than vendored.
//
// Upstream this type owns the listening socket, matches prefixes, and hands finished contexts to
// an HttpListener. None of that is wanted here: this project accepts its own connections so that
// it can terminate TLS itself and pin the version. What remains is the three call sites
// HttpConnection actually uses, which is the whole of its coupling to the layer below it.

using System;
using System.Collections.Generic;
using System.Threading;

namespace KeePassGate.Vendor.Http
{
    internal sealed class HttpEndPointListener
    {
        private readonly List<HttpConnection> _connections = new List<HttpConnection>();
        private readonly Action<HttpListenerContext> _onRequest;

        internal HttpEndPointListener(Action<HttpListenerContext> onRequest)
        {
            _onRequest = onRequest;
        }

        /// <summary>
        /// Called once a request has been parsed. Upstream this checks the context against the
        /// registered prefixes and queues it; here every connection was already authenticated by
        /// mutual TLS before the vendored parser saw a byte, so it is handed straight on.
        /// </summary>
        internal bool BindContext(HttpListenerContext context)
        {
            _onRequest(context);
            return true;
        }

        internal static void UnbindContext(HttpListenerContext context)
        {
        }

        internal void RemoveConnection(HttpConnection connection)
        {
            lock (_connections) { _connections.Remove(connection); }
        }

        internal void AddConnection(HttpConnection connection)
        {
            lock (_connections) { _connections.Add(connection); }
        }
    }
}
