using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using KeePassGate.Transport;

namespace KeePassGate.Pipeline
{
    /// <summary>The transport's own implementation of the seam.</summary>
    public sealed class GateContext : IGateContext
    {
        private readonly HttpRequestHead _head;
        private readonly string _body;
        private readonly ClientIdentity _client;
        private readonly IDictionary<string, object> _items = new Dictionary<string, object>(StringComparer.Ordinal);

        private int _status;
        private string _json;
        private Action<TextWriter> _content;
        private WebHeaderCollection _headers;
        private bool _answered;

        public GateContext(HttpRequestHead head, string body, ClientIdentity client)
        {
            if (head == null)
            {
                throw new ArgumentNullException(nameof(head));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _head = head;
            _body = body ?? string.Empty;
            _client = client;
        }

        public string Method
        {
            get { return _head.Method; }
        }

        public string Path
        {
            get { return _head.Path; }
        }

        public IReadOnlyDictionary<string, string> Query
        {
            get { return _head.Query; }
        }

        public string Body
        {
            get { return _body; }
        }

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
            get { return _answered; }
        }

        /// <summary>What to write. 500 when a handler answered nothing at all.</summary>
        public int Status
        {
            get { return _answered ? _status : 500; }
        }

        /// <summary>
        /// The answer as it was handed over whole, or null for an answer that
        /// <see cref="Content"/> writes.
        /// </summary>
        public string Json
        {
            get { return _answered ? _json : HttpResponse.ErrorBody("no answer"); }
        }

        /// <summary>
        /// What writes the answer as it is sent, or null for an answer handed over whole.
        /// </summary>
        public Action<TextWriter> Content
        {
            get { return _content; }
        }

        public string GetHeader(string name)
        {
            return _head.GetHeader(name);
        }

        public void Respond(int status, string json)
        {
            Respond(status, json, null);
        }

        public void Respond(int status, string json, WebHeaderCollection headers)
        {
            if (_answered)
            {
                // The first answer stands. A second answer is a defect in a handler, or a
                // handler answering the refusal of a change whose caller `SpecRouter` turned away
                // inside the change's turn, and overwriting would turn either into a response
                // that contradicts what was already decided.
                return;
            }

            _answered = true;
            _status = status;
            _json = json ?? string.Empty;
            _headers = headers;
        }

        public void Respond(int status, Action<TextWriter> content, WebHeaderCollection headers)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (_answered)
            {
                // The first answer stands, as above.
                return;
            }

            _answered = true;
            _status = status;
            _content = content;
            _headers = headers;
        }

        /// <summary>What this answer adds to the fields every answer carries, or null.</summary>
        public WebHeaderCollection Headers
        {
            get { return _headers; }
        }
    }
}
