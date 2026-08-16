using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace KeePassGate.Transport
{
    /// <summary>
    /// Writes the only kind of response this server sends: a JSON body, a closed connection, and
    /// nothing cached.
    /// </summary>
    /// <remarks>
    /// The status codes and their reason phrases are RFC 9110 section 15.
    /// https://www.rfc-editor.org/rfc/rfc9110#section-15
    /// The message framing is RFC 9112: a status line, headers, a blank line, then exactly
    /// `Content-Length` bytes. https://www.rfc-editor.org/rfc/rfc9112#section-6
    /// </remarks>
    public static class HttpResponse
    {
        private static readonly Dictionary<int, string> Reasons = new Dictionary<int, string>
        {
            { 200, "OK" },
            { 201, "Created" },
            { 400, "Bad Request" },
            { 401, "Unauthorized" },
            { 403, "Forbidden" },
            { 404, "Not Found" },
            { 405, "Method Not Allowed" },
            { 411, "Length Required" },
            { 413, "Content Too Large" },
            { 414, "URI Too Long" },
            // An `Expect` this server does not meet, RFC 9110 section 15.5.18. Refused rather
            // than ignored: ignoring one lets a client proceed believing something untrue.
            { 417, "Expectation Failed" },
            { 431, "Request Header Fields Too Large" },
            { 500, "Internal Server Error" },
            { 501, "Not Implemented" },
            { 503, "Service Unavailable" },
            { 505, "HTTP Version Not Supported" }
        };

        /// <summary>The fields this writer decides, which nothing above it may send again.</summary>
        private static readonly HashSet<string> Ours = new HashSet<string>(
            new[]
            {
                "Content-Type",
                "Content-Length",
                "Cache-Control",
                "X-Content-Type-Options",
                "Connection",
            },
            StringComparer.OrdinalIgnoreCase);

        public static string ReasonFor(int status)
        {
            string reason;
            return Reasons.TryGetValue(status, out reason) ? reason : "Status " + status;
        }

        /// <summary>A JSON object saying only what went wrong, under `error`.</summary>
        public static string ErrorBody(string error)
        {
            return "{\"error\":\"" + Escape(error ?? "refused") + "\"}";
        }

        /// <param name="headers">
        /// Fields this answer carries beyond the ones every answer does, or null. Anything
        /// named below is dropped rather than sent twice: a second `Content-Length` is a
        /// second opinion about where the body ends, which is a request smuggled past
        /// whatever reads this next.
        /// </param>
        public static void Write(Stream stream, int status, string json, WebHeaderCollection headers)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var body = Encoding.UTF8.GetBytes(json ?? string.Empty);

            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(ReasonFor(status)).Append("\r\n");
            head.Append("Content-Type: application/json; charset=utf-8\r\n");
            head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            head.Append("Cache-Control: no-store\r\n");
            head.Append("X-Content-Type-Options: nosniff\r\n");
            // Never keep-alive: one request per connection, so there is no second request to
            // disagree with anybody about.
            head.Append("Connection: close\r\n");

            if (headers != null)
            {
                foreach (var name in headers.AllKeys)
                {
                    if (Ours.Contains(name))
                    {
                        continue;
                    }

                    head.Append(name).Append(": ").Append(headers[name]).Append("\r\n");
                }
            }

            head.Append("\r\n");

            var headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }

            stream.Flush();
        }

        /// <summary>
        /// The smallest correct JSON string escaping. Enough for the words this server puts in a
        /// body, and it refuses nothing, because everything it is given comes from us.
        /// </summary>
        public static string Escape(string text)
        {
            var builder = new StringBuilder(text.Length + 8);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
