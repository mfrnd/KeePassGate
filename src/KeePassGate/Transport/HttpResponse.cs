using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// `Content-Length` bytes, or, for content written as it is sent, the same content in
    /// chunks, <see cref="ChunkedContent"/>. https://www.rfc-editor.org/rfc/rfc9112#section-6
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
            // An `If-Match` the node no longer meets, RFC 9110 section 15.5.13.
            { 412, "Precondition Failed" },
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
                "Transfer-Encoding",
                "Cache-Control",
                "X-Content-Type-Options",
                "Connection",
                "Retry-After",
                UnavailableField,
            },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The field every `503` names its refusal in, as its body does: `busy`, `locked` or
        /// `undecided`.
        /// </summary>
        /// <remarks>
        /// **For the answer to a HEAD**, which carries no body, so a client asking whether
        /// something is there could not tell `busy` from `locked`, and waited out neither. RFC 9110 section 9.3.2 asks a HEAD to carry the fields a GET would,
        /// so a GET carries it too, and a client reads it wherever the body names nothing.
        ///
        /// **Read back out of the body rather than handed in beside it**, so the two cannot name
        /// different refusals, and written here rather than at every place that answers `503`, as
        /// `Retry-After` is, so that no refusal added later can forget it. A body that names no
        /// word of ASCII letters sends none.
        /// </remarks>
        public const string UnavailableField = "KPG-Unavailable";

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
        /// Two seconds, which no reason here is measured against. A save of a small database with
        /// a light key derivation is tens of milliseconds, while the project has measured one of
        /// 201 MiB of attachments at 80.6 seconds and a heavy key derivation adding about eight to
        /// every save, and the longest of these is somebody closing a dialog, which no number would
        /// cover. So two seconds is a pace rather than a prediction: long enough that a retry is
        /// not a second attempt at the same instant, short enough that a client honoring it is not
        /// asleep through the window it was waiting for.
        ///
        /// **It is also how long an approval waits for a busy KeePass on its own connection**,
        /// `PairingOptions.ApprovalSettle`, which the operator kept at two seconds on 2026-10-14.
        /// That wait does not keep the operator's decision through a save of eight or eighty
        /// seconds, and is not meant to: the approval is held in the plugin's memory and the client
        /// told until when, `ApprovalBook`, and that is what keeps it.
        /// </remarks>
        public const int RetryAfterSeconds = 2;

        /// <summary>
        /// How many characters of content written as it is sent are gathered before they are
        /// encoded.
        /// </summary>
        private const int TextCharacters = 4096;

        /// <summary>UTF-8 with no mark ahead of it, which is what a JSON answer is written in.</summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

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
            var framing = head
                ? null
                : "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture);

            var headBytes = Head(status, framing, status == 503 ? UnavailableWord(json) : null, headers);
            stream.Write(headBytes, 0, headBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }

            stream.Flush();
        }

        /// <summary>
        /// One answer whose content is written as it is sent: the status line, the fields, and
        /// the content in chunks unless there is to be none.
        /// </summary>
        /// <remarks>
        /// **For an answer that grows with what a database holds and is made of values.** Handed
        /// over whole, an answer is alive several times over while it is written: its values,
        /// the document they were written into, the text taken of that document and the bytes
        /// the text encodes to. Written as it is sent, what is alive is the value being written
        /// and what <see cref="ChunkedContent"/> gathers of it. `FINDINGS.md` has both measured.
        ///
        /// **Its length is not known when its head goes out**, so it carries none and goes out
        /// in chunks, which is how RFC 9112 section 7.1 frames such content.
        ///
        /// **A failure part way cannot be answered.** The status is on the wire by then, so
        /// whatever <paramref name="content"/> throws is thrown on from here with the content
        /// left without its last chunk, and the connection is closed over it: a client reads
        /// an answer cut short, and takes none of it.
        ///
        /// **A HEAD asks for no content, and none is made**: <paramref name="content"/> is not
        /// run, and the answer names no framing, as a HEAD's answer names no length above.
        /// </remarks>
        /// <param name="content">
        /// What writes the content, as text this writer encodes as UTF-8. Run once, here.
        /// </param>
        /// <param name="headers">As the answer above takes them.</param>
        public static void Write(
            Stream stream, int status, Action<TextWriter> content, bool head, WebHeaderCollection headers)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var headBytes = Head(status, head ? null : "Transfer-Encoding: chunked", null, headers);
            stream.Write(headBytes, 0, headBytes.Length);
            if (!head)
            {
                using (var chunks = new ChunkedContent(stream))
                using (var text = new StreamWriter(chunks, Utf8, TextCharacters, true))
                {
                    var whole = false;
                    try
                    {
                        content(text);
                        text.Flush();
                        chunks.End();
                        whole = true;
                    }
                    finally
                    {
                        if (!whole)
                        {
                            // What failed is thrown on as it is. The writer above is let go
                            // after this and hands on the text it still holds: dropped, so
                            // that nothing of a failed answer follows the failure, and a peer
                            // that is gone as well does not put its own failure in its place.
                            chunks.Abandon();
                        }
                    }
                }
            }

            stream.Flush();
        }

        /// <summary>The status line and the fields of an answer, with the blank line after them.</summary>
        /// <param name="framing">
        /// The field that tells where the content ends, or null for an answer that carries no
        /// content.
        /// </param>
        /// <param name="unavailable">
        /// The word a `503` names its refusal with, or null where it names none.
        /// </param>
        private static byte[] Head(int status, string framing, string unavailable, WebHeaderCollection headers)
        {
            var lines = new StringBuilder();
            lines.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(ReasonFor(status)).Append("\r\n");
            lines.Append("Content-Type: application/json; charset=utf-8\r\n");
            if (framing != null)
            {
                lines.Append(framing).Append("\r\n");
            }

            lines.Append("Cache-Control: no-store\r\n");
            lines.Append("X-Content-Type-Options: nosniff\r\n");
            // Never keep-alive: one request per connection, so there is no second request to
            // disagree with anybody about.
            lines.Append("Connection: close\r\n");
            if (status == 503)
            {
                // Here rather than at each place that answers `503`, so that a refusal added later
                // cannot be the one that forgets it. What each of them means is the body's
                // job; <see cref="RetryAfterSeconds"/> says why they all wait the same.
                lines.Append("Retry-After: ")
                    .Append(RetryAfterSeconds.ToString(CultureInfo.InvariantCulture))
                    .Append("\r\n");

                if (unavailable != null)
                {
                    lines.Append(UnavailableField).Append(": ").Append(unavailable).Append("\r\n");
                }
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

            return Encoding.ASCII.GetBytes(lines.ToString());
        }

        /// <summary>
        /// The word a `503`'s body names under `error`, or null when it names none of ASCII
        /// letters, which is all <see cref="UnavailableField"/> carries.
        /// </summary>
        private static string UnavailableWord(string json)
        {
            var word = JsonBody.Read(json)?.Text("error");
            return !string.IsNullOrEmpty(word) && word.All(Ascii.IsLetter) ? word : null;
        }

    }
}
