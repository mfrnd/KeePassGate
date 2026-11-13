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
    /// <see cref="ClientCredentials.Again"/> has the reason an identity is read again. A change
    /// is handed this by its <see cref="DecidedInside"/>, inside its turn, and a read is decided
    /// and served against this for the whole of its hold. Everything but the caller is the
    /// request's own: the method, the path and the body are the ones of the context this wraps,
    /// so there is still one request and one answer.
    ///
    /// **The items are a copy.** What the router leaves for a handler about the caller, the
    /// <see cref="ProtectedReach"/> above all, is left here about the client read again, and the
    /// request's own items go on holding what was decided before, for whoever still asks there.
    ///
    /// **A read's answer is kept here until its hold has ended**, and handed on by
    /// <see cref="Deliver"/>. A hold can lapse while a read is still running, which is found out
    /// only once the read is over, and what a read made of a database that was let go under it
    /// is not an answer: kept here, it is dropped with this context and the request is answered
    /// `503 busy`. Answered on the request at once, it would have stood, since the first answer
    /// does. A change's answer goes straight to the request, because a turn is KeePass's thread
    /// running the change itself and cannot be let go under it: there is nothing to wait for,
    /// whether the handler answers on the connection's thread once the turn is over or a client
    /// is turned away inside the turn.
    ///
    /// **What a read leaves for after its hold runs here too**, <see cref="After"/>: the part of
    /// an entry's answer made of its values, which is run by <see cref="Deliver"/> on the
    /// connection's own thread, before the answer is handed on. A read whose hold lapsed never
    /// reaches <see cref="Deliver"/>, so what it left is dropped with its answer.
    /// </remarks>
    internal sealed class DecidedContext : IGateContext
    {
        private readonly IGateContext _request;
        private readonly ClientIdentity _client;
        private readonly IDictionary<string, object> _items;
        private readonly bool _keeps;

        private bool _answered;
        private int _status;
        private string _json;
        private WebHeaderCollection _headers;
        private Action _after;

        /// <summary>The request, asked by the client as it was read again.</summary>
        public DecidedContext(IGateContext request, ClientIdentity client)
            : this(request, client, false)
        {
        }

        /// <summary>The same, naming where the answer goes.</summary>
        /// <param name="keeps">
        /// Whether the answer is kept until <see cref="Deliver"/> hands it to the request, which
        /// is what a read served inside a hold needs.
        /// </param>
        public DecidedContext(IGateContext request, ClientIdentity client, bool keeps)
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
            _keeps = keeps;
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
            get { return _keeps ? _answered : _request.Answered; }
        }

        public string GetHeader(string name)
        {
            return _request.GetHeader(name);
        }

        public void Respond(int status, string json)
        {
            Respond(status, json, null);
        }

        public void Respond(int status, string json, WebHeaderCollection headers)
        {
            if (!_keeps)
            {
                Send(status, json, headers);
                return;
            }

            if (_answered)
            {
                // The first answer stands, as it does on the request itself.
                return;
            }

            _answered = true;
            _status = status;
            _json = json;
            _headers = headers;
        }

        /// <summary>
        /// Leaves the rest of a read to be run once its hold has ended, by <see cref="Deliver"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="ServedAfter"/> has what may be left. A context that keeps no answer has
        /// no hold to wait for, so the work is run where it is.
        /// </remarks>
        public void After(Action serve)
        {
            if (serve == null)
            {
                throw new ArgumentNullException(nameof(serve));
            }

            if (!_keeps)
            {
                serve();
                return;
            }

            _after = serve;
        }

        /// <summary>
        /// Runs what the read left for after its hold, and hands the answer kept here to the
        /// request, when there is an answer.
        /// </summary>
        public void Deliver()
        {
            var after = _after;
            _after = null;

            // An answer given inside the hold stands, so nothing left for later runs over it.
            if (after != null && !_answered)
            {
                after();
            }

            if (_answered)
            {
                Send(_status, _json, _headers);
            }
        }

        private void Send(int status, string json, WebHeaderCollection headers)
        {
            if (headers == null)
            {
                _request.Respond(status, json);
                return;
            }

            _request.Respond(status, json, headers);
        }
    }
}
