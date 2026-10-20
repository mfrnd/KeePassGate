using System;
using System.Collections.Generic;

namespace KeePassGate.Transport
{
    /// <summary>A request line and its headers, once they have been read and believed.</summary>
    public sealed class HttpRequestHead
    {
        private readonly string _method;
        private readonly string _path;
        private readonly IReadOnlyDictionary<string, string> _query;
        private readonly IDictionary<string, string> _headers;
        private readonly long _contentLength;

        public HttpRequestHead(
            string method,
            string path,
            IReadOnlyDictionary<string, string> query,
            IDictionary<string, string> headers,
            long contentLength)
        {
            _method = method;
            _path = path;
            _query = query;
            _headers = headers;
            _contentLength = contentLength;
        }

        /// <summary>Upper case, as sent. A method is case sensitive and ours are all upper case.</summary>
        public string Method
        {
            get { return _method; }
        }

        /// <summary>
        /// The absolute path exactly as it arrived, escapes and all, with the query split off.
        /// </summary>
        /// <remarks>
        /// Not decoded here. Whatever matches a path decodes one segment at a time, which is
        /// what keeps a
        /// `%2F` inside the segment that carries it instead of splitting the path somewhere the
        /// caller did not ask for.
        /// </remarks>
        public string Path
        {
            get { return _path; }
        }

        public IReadOnlyDictionary<string, string> Query
        {
            get { return _query; }
        }

        /// <summary>Case insensitive, as headers are.</summary>
        public string GetHeader(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            string value;
            return _headers.TryGetValue(name, out value) ? value : null;
        }

        public long ContentLength
        {
            get { return _contentLength; }
        }
    }

    /// <summary>
    /// What came of trying to read a request: a head, or the status to answer with instead.
    /// </summary>
    public sealed class HttpReadResult
    {
        private readonly HttpRequestHead _head;
        private readonly int _status;
        private readonly string _reason;
        private readonly string _method;

        private HttpReadResult(HttpRequestHead head, int status, string reason, string method)
        {
            _head = head;
            _status = status;
            _reason = reason;
            _method = method;
        }

        /// <summary>The method this was read for, or null when none was read.</summary>
        /// <remarks>
        /// One string on both kinds of result. A request that was read carries its head's own
        /// method, because <see cref="Read"/> takes it from there, and a refusal carries the
        /// method it was refused for. So a caller deciding what an answer may contain asks this
        /// and nothing else, rather than asking whether there is a head first and reaching
        /// through it.
        /// </remarks>
        public string Method
        {
            get { return _method; }
        }

        /// <summary>
        /// How many bytes of body the peer is sending without waiting to be asked again.
        /// </summary>
        /// <remarks>
        /// What an answer has to take off the wire before the connection is closed. Closing a
        /// socket while the peer's bytes are still unread is answered by the stack with a reset,
        /// and a reset discards what it has already delivered, so the answer is thrown away and
        /// the caller is left unable to tell a refusal from a gate that is not running.
        ///
        /// Zero when the head named no body, and zero when it carried an `Expect`, because such a
        /// client has sent nothing and is waiting: reading would leave both sides waiting for the
        /// other. Zero as well for a refusal decided before the header block was read, where there
        /// is nothing to bound a read by.
        /// </remarks>
        public long Announced { get; private set; }

        public HttpRequestHead Head
        {
            get { return _head; }
        }

        /// <summary>Zero when the request was read. Otherwise what to answer.</summary>
        public int Status
        {
            get { return _status; }
        }

        public string Reason
        {
            get { return _reason; }
        }

        public bool Ok
        {
            get { return _head != null; }
        }

        /// <summary>Nothing arrived at all, so there is nobody to answer.</summary>
        public bool Silent
        {
            get { return _head == null && _status == 0; }
        }

        public static HttpReadResult Read(HttpRequestHead head, long announced)
        {
            if (head == null)
            {
                throw new ArgumentNullException(nameof(head));
            }

            return new HttpReadResult(head, 0, null, head.Method)
            {
                Announced = announced
            };
        }

        /// <summary>A refusal, and the method it was refused for when one was read.</summary>
        /// <remarks>
        /// The method is carried because the answer depends on it: a response to HEAD cannot have
        /// content, and a refusal is a response. It is null for the refusals that happen before a
        /// request line has been read, where there is no method to have identified.
        ///
        /// So is <see cref="Announced"/>, and for the same kind of reason: what the head said it
        /// was about to send outlives the decision not to serve it.
        /// </remarks>
        public static HttpReadResult Refused(int status, string reason, string method = null, long announced = 0)
        {
            return new HttpReadResult(null, status, reason, method) { Announced = announced };
        }

        /// <summary>The connection ended before a request did. Nothing is written back.</summary>
        public static HttpReadResult Nothing()
        {
            return new HttpReadResult(null, 0, null, null);
        }
    }
}
