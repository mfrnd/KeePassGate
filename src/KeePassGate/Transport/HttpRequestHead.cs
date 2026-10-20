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

        private HttpReadResult(HttpRequestHead head, int status, string reason)
        {
            _head = head;
            _status = status;
            _reason = reason;
        }

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

        public static HttpReadResult Read(HttpRequestHead head)
        {
            if (head == null)
            {
                throw new ArgumentNullException(nameof(head));
            }

            return new HttpReadResult(head, 0, null);
        }

        public static HttpReadResult Refused(int status, string reason)
        {
            return new HttpReadResult(null, status, reason);
        }

        /// <summary>The connection ended before a request did. Nothing is written back.</summary>
        public static HttpReadResult Nothing()
        {
            return new HttpReadResult(null, 0, null);
        }
    }
}
