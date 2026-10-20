using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

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
            { 409, "Conflict" },
            { 411, "Length Required" },
            { 413, "Content Too Large" },
            { 414, "URI Too Long" },
            // A body sent under a media type this API does not speak, RFC 9110 section 15.5.16.
            // The router answers it, so it is named here: a status with no phrase went out as
            // `Status 415`, which is not a reason phrase any registry knows.
            { 415, "Unsupported Media Type" },
            // An `Expect` this server does not meet, RFC 9110 section 15.5.18. Refused rather
            // than ignored: ignoring one lets a client proceed believing something untrue.
            { 417, "Expectation Failed" },
            { 431, "Request Header Fields Too Large" },
            { 500, "Internal Server Error" },
            { 501, "Not Implemented" },
            { 503, "Service Unavailable" },
            // Pairing waits on the operator, who is genuinely the upstream this server did not
            // hear back from in time.
            { 504, "Gateway Timeout" },
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
                "Retry-After",
            },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How long a client is told to wait before asking again, on every `503` this gate sends.
        /// </summary>
        /// <remarks>
        /// Seconds, which is one of the two forms RFC 9110 section 10.2.3 allows and the only one
        /// worth sending here: the other is a date, and a date carries a clock the two sides have
        /// to agree about.
        ///
        /// **One number for every reason**, and that is the decision rather than an economy.
        /// A locked database, a dialog open on what a write would change, a save that would stop
        /// and ask, and KeePass part way through an import all answer `503`, and how long each of
        /// them lasts is a person's business rather than this gate's. Which refusal it was is in
        /// the body, where a client that wants to treat them differently reads it; putting two
        /// different delays on the wire would invite reading a meaning into the difference that
        /// nothing here measured.
        ///
        /// Two seconds because the shortest of these is a save, which is hundreds of milliseconds,
        /// and the longest is somebody closing a dialog, which no number would cover. It is long
        /// enough that a retry is not a second attempt at the same instant and short enough that a
        /// client honoring it is not asleep through the window it was waiting for.
        /// </remarks>
        public const int RetryAfterSeconds = 2;

        public static string ReasonFor(int status)
        {
            string reason;
            return Reasons.TryGetValue(status, out reason) ? reason : "Status " + status;
        }

        /// <summary>A JSON object saying only what went wrong, under `error`.</summary>
        /// <remarks>
        /// **Mostly one word an agent can match on, and not always.** A refusal with more to say
        /// than a word says it in a sentence, as the transport does about a head over its cap, and a
        /// client tells those apart by their status rather than by matching the words.
        ///
        /// The value is quoted and escaped by `Json.NET`, which arrived with the body reader and
        /// is the only thing here that decides what a JSON string looks like. Escaping written
        /// out a second time is a second answer to that question, and the two would only ever
        /// be found to disagree by something a caller sent.
        /// </remarks>
        public static string ErrorBody(string error)
        {
            return "{\"error\":" + JsonConvert.ToString(error ?? "refused") + "}";
        }

        /// <summary>
        /// One answer: the status line, the fields, and the content unless there is to be none.
        /// </summary>
        /// <remarks>
        /// **A HEAD is answered here because this is where the length is decided.** RFC 9112
        /// section 6.3: a response to HEAD is terminated by the blank line and cannot carry
        /// content, whatever its header fields say. Blanking the body one layer above this is what
        /// let the two disagree. The answer was emptied, the writer measured the empty string,
        /// and `Content-Length: 0` went out where a GET would have said forty-five. RFC 9110
        /// section 8.6 forbids exactly that: a length that is not what a GET would have sent.
        ///
        /// The field is omitted rather than corrected, which section 9.3.2 permits for a value
        /// "determined only while generating the content". That is this one.
        ///
        /// The content is generated all the same. A HEAD runs the GET's own handler, because one
        /// handler is what stops the two coming to disagree about whether something is there, and
        /// what it wrote is dropped here. So what a HEAD saves is the bytes on the wire rather than
        /// the work behind them, and a route whose answer is expensive to build is expensive to
        /// ask about either way.
        /// </remarks>
        /// <param name="headers">
        /// Fields this answer carries beyond the ones every answer does, or null. Anything the
        /// writer sends itself is dropped rather than sent twice: a second `Content-Length` is a
        /// second opinion about where the body ends, which is a request smuggled past whatever
        /// reads this next.
        /// </param>
        public static void Write(
            Stream stream, int status, string json, bool head, WebHeaderCollection headers)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var body = head ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json ?? string.Empty);

            var lines = new StringBuilder();
            lines.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(ReasonFor(status)).Append("\r\n");
            lines.Append("Content-Type: application/json; charset=utf-8\r\n");
            if (!head)
            {
                lines.Append("Content-Length: ")
                    .Append(body.Length.ToString(CultureInfo.InvariantCulture))
                    .Append("\r\n");
            }

            lines.Append("Cache-Control: no-store\r\n");
            lines.Append("X-Content-Type-Options: nosniff\r\n");
            // Never keep-alive: one request per connection, so there is no second request to
            // disagree with anybody about.
            lines.Append("Connection: close\r\n");
            if (status == 503)
            {
                // Here rather than at the four places that answer `503`, so that a refusal added
                // later cannot be the one that forgets it. What each of them means is the body's
                // job; <see cref="RetryAfterSeconds"/> says why they all wait the same.
                lines.Append("Retry-After: ")
                    .Append(RetryAfterSeconds.ToString(CultureInfo.InvariantCulture))
                    .Append("\r\n");
            }

            if (headers != null)
            {
                foreach (var name in headers.AllKeys)
                {
                    if (Ours.Contains(name))
                    {
                        continue;
                    }

                    lines.Append(name).Append(": ").Append(headers[name]).Append("\r\n");
                }
            }

            lines.Append("\r\n");

            var headBytes = Encoding.ASCII.GetBytes(lines.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }

            stream.Flush();
        }

    }
}
