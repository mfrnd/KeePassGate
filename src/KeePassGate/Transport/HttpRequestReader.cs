using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeePassGate.Transport
{
    /// <summary>
    /// Reads one HTTP request, and only the parts of HTTP this API has any use for.
    /// </summary>
    /// <remarks>
    /// Written here rather than taken as a dependency, which `DESIGN.md` §104 argues at
    /// length: owning the TLS left only a header parser to buy, and the one that fits the seam is
    /// LGPL-2.1 and unmaintained.
    ///
    /// What that buys has to be paid for in refusals, so this is deliberately narrow. One request
    /// per connection, no keep-alive, no upgrade, no chunked bodies, no continuations, a bounded
    /// head and a bounded body. Everything it will not do is a refusal with a status rather than a
    /// best guess, because guessing is where request smuggling lives.
    /// </remarks>
    public static class HttpRequestReader
    {
        // The refusals this reader can answer with, all from RFC 9110 section 15 except 431,
        // which is RFC 6585 section 5. https://www.rfc-editor.org/rfc/rfc9110#section-15
        private const int BadRequest = 400;
        private const int LengthRequired = 411;
        private const int PayloadTooLarge = 413;
        private const int UriTooLong = 414;

        /// <summary>Request header fields too large, RFC 6585 section 5.</summary>
        private const int HeadersTooLarge = 431;

        private const int NotImplemented = 501;
        private const int VersionNotSupported = 505;

        /// <summary>How much of a body is asked for at once, so nothing is sized from the wire.</summary>
        private const int ChunkBytes = 8 * 1024;

        /// <summary>Refuses bytes that are not UTF-8 rather than rewriting them.</summary>
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// The only line ending a head is split on, held once rather than allocated per request.
        /// </summary>
        /// <remarks>
        /// A bare LF is not a line ending here. RFC 9112 section 2.2 allows a recipient to accept
        /// one, and this one does not: two parsers disagreeing about where a line ends is where
        /// request smuggling lives. https://www.rfc-editor.org/rfc/rfc9112#section-2.2
        /// </remarks>
        private static readonly string[] LineBreak = { "\r\n" };

        public static HttpReadResult ReadHead(Stream stream, GateOptions options)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            byte[] head;
            switch (ReadUntilBlankLine(stream, options.MaxHeadBytes, out head))
            {
                case HeadOutcome.Silence:
                    return HttpReadResult.Nothing();

                case HeadOutcome.Broken:
                    // Bytes arrived and then the connection failed, so somebody is there to answer
                    // even though what they sent was never a request. Silence is reserved for a
                    // connection that said nothing at all, because that is the one signal the gate
                    // uses to look like a port with nothing behind it.
                    return HttpReadResult.Refused(BadRequest, "the head ended early");

                case HeadOutcome.TooLong:
                    return HttpReadResult.Refused(
                        HeadersTooLarge, "the head is over " + options.MaxHeadBytes + " bytes");
            }

            // Before anything is decoded. `Encoding.ASCII` substitutes a question mark for every
            // byte above 0x7F, which invents the character the target is split on: the bytes
            // `/gate/status\x80xy` would arrive as the path `/gate/status` with a query, and the
            // request this server answered would not be the request that was sent. A head is
            // US-ASCII by RFC 9112 section 2.1, so a byte that is not is refused rather than
            // replaced. https://www.rfc-editor.org/rfc/rfc9112#section-2.1
            foreach (var b in head)
            {
                if (b > 0x7F)
                {
                    return HttpReadResult.Refused(BadRequest, "the head is not ascii");
                }
            }

            var text = Encoding.ASCII.GetString(head);
            var lines = text.Split(LineBreak, StringSplitOptions.None);

            string method;
            string target;
            var lineResult = ParseRequestLine(lines[0], out method, out target);
            if (lineResult != null)
            {
                return lineResult;
            }

            string path;
            IReadOnlyDictionary<string, string> query;
            if (!TrySplitTarget(target, out path, out query))
            {
                return HttpReadResult.Refused(BadRequest, "the target is not a path this server accepts");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0)
                {
                    break;
                }

                if (line[0] == ' ' || line[0] == '\t')
                {
                    // Obsolete line folding. Refused rather than joined, because two parsers that
                    // disagree about a folded header is exactly how a request gets smuggled.
                    return HttpReadResult.Refused(BadRequest, "folded headers are not accepted");
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    return HttpReadResult.Refused(BadRequest, "a header line has no name");
                }

                var name = line.Substring(0, colon);
                if (!IsToken(name))
                {
                    return HttpReadResult.Refused(BadRequest, "a header name is not a token");
                }

                var value = line.Substring(colon + 1).Trim(' ', '\t');

                if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    return HttpReadResult.Refused(NotImplemented, "transfer encodings are not accepted");
                }

                // Every name, not only `Content-Length`. Keeping the last of a repeated field is a
                // guess, and a guess about which of two values a header has is what an intermediary
                // in front of this would have to make the same way to agree with it.
                if (headers.ContainsKey(name))
                {
                    return HttpReadResult.Refused(BadRequest, "a header is repeated");
                }

                headers[name] = value;
            }

            long length = 0;
            string lengthHeader;
            if (headers.TryGetValue("Content-Length", out lengthHeader))
            {
                if (!long.TryParse(lengthHeader, NumberStyles.None, CultureInfo.InvariantCulture, out length) ||
                    length < 0)
                {
                    return HttpReadResult.Refused(BadRequest, "the content length is not a number");
                }

                if (length > options.MaxBodyBytes)
                {
                    return HttpReadResult.Refused(PayloadTooLarge, "the body is over " + options.MaxBodyBytes + " bytes");
                }
            }
            else if (TakesABody(method))
            {
                return HttpReadResult.Refused(LengthRequired, "a body needs a content length");
            }

            return HttpReadResult.Read(new HttpRequestHead(method, path, query, headers, length));
        }

        /// <summary>
        /// Reads exactly what the head declared. Null when the connection ends first, which is a
        /// refusal rather than an empty body.
        /// </summary>
        public static string ReadBody(Stream stream, HttpRequestHead head)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (head == null)
            {
                throw new ArgumentNullException(nameof(head));
            }

            if (head.ContentLength == 0)
            {
                return string.Empty;
            }

            // Grown as bytes arrive rather than allocated to what was announced. A caller that has
            // proved nothing yet chooses the number in `Content-Length`, and sizing the buffer from
            // it means a megabyte is spent on a request that never sends one.
            var body = new MemoryStream();
            var chunk = new byte[Math.Min(head.ContentLength, ChunkBytes)];
            var read = 0L;

            while (read < head.ContentLength)
            {
                var wanted = (int)Math.Min(head.ContentLength - read, chunk.Length);

                int got;
                try
                {
                    got = stream.Read(chunk, 0, wanted);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    // A client that hung up mid-body, which is the same thing to this server as one
                    // that stopped sending. Caught here for the reason the head reader catches it:
                    // left to travel, it reaches the connection catch-all and is counted against
                    // the plugin rather than against the client that chose it.
                    return null;
                }

                if (got <= 0)
                {
                    return null;
                }

                body.Write(chunk, 0, got);
                read += got;
            }

            try
            {
                // Throwing rather than replacing. The static `Encoding.UTF8` rewrites every byte it
                // cannot read as U+FFFD, which is repair, in the one module whose whole policy is
                // that a framing it does not understand is refused rather than guessed at. A body
                // that is not UTF-8 would otherwise reach a handler as text nobody sent and be
                // stored that way.
                return StrictUtf8.GetString(body.GetBuffer(), 0, (int)body.Length);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        private static HttpReadResult ParseRequestLine(string line, out string method, out string target)
        {
            method = null;
            target = null;

            var parts = line.Split(' ');
            if (parts.Length != 3)
            {
                return HttpReadResult.Refused(BadRequest, "the request line is not three parts");
            }

            if (!IsToken(parts[0]))
            {
                return HttpReadResult.Refused(BadRequest, "the method is not a token");
            }

            if (parts[1].Length == 0 || parts[1][0] != '/')
            {
                // Only origin form. An absolute URI or an authority is somebody treating this as a
                // proxy, which it is not.
                return HttpReadResult.Refused(BadRequest, "the target is not an absolute path");
            }

            if (parts[1].Length > 2048)
            {
                return HttpReadResult.Refused(UriTooLong, "the target is over 2048 characters");
            }

            if (!string.Equals(parts[2], "HTTP/1.1", StringComparison.Ordinal) &&
                !string.Equals(parts[2], "HTTP/1.0", StringComparison.Ordinal))
            {
                return HttpReadResult.Refused(VersionNotSupported, "only HTTP/1.1 and HTTP/1.0 are accepted");
            }

            method = parts[0];
            target = parts[1];
            return null;
        }

        private static bool TrySplitTarget(string target, out string path, out IReadOnlyDictionary<string, string> query)
        {
            path = null;
            query = new Dictionary<string, string>(StringComparer.Ordinal);

            var mark = target.IndexOf('?');
            var rawPath = mark < 0 ? target : target.Substring(0, mark);
            var rawQuery = mark < 0 ? string.Empty : target.Substring(mark + 1);

            if (!IsUsablePath(rawPath))
            {
                return false;
            }

            if (rawQuery.Length > 0)
            {
                var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in rawQuery.Split('&'))
                {
                    if (pair.Length == 0)
                    {
                        continue;
                    }

                    var equals = pair.IndexOf('=');
                    var rawName = equals < 0 ? pair : pair.Substring(0, equals);
                    var rawValue = equals < 0 ? string.Empty : pair.Substring(equals + 1);

                    string name;
                    string value;
                    if (!PercentEncoding.TryDecodeQuery(rawName, out name) ||
                        !PercentEncoding.TryDecodeQuery(rawValue, out value))
                    {
                        return false;
                    }

                    if (name.Length == 0)
                    {
                        return false;
                    }

                    // Refused for the reason a repeated header is. Keeping the last of two is a
                    // guess, and the first operation to declare a query would inherit it: a limit
                    // sent twice served as the larger one, with the caller told nothing.
                    if (pairs.ContainsKey(name))
                    {
                        return false;
                    }

                    pairs[name] = value;
                }

                query = pairs;
            }

            // Kept as it arrived, escapes and all. Decoding belongs to whoever splits it into
            // segments, because a `%2F` decoded too early stops being part of a name and becomes a
            // separator. Whatever matches a path does that, and it is the only thing that
            // needs to.
            path = rawPath;
            return true;
        }

        /// <summary>
        /// Whether a raw path is one this server will carry any further.
        /// </summary>
        /// <remarks>
        /// Every segment has to decode, and no segment may be one that navigates. Checked here so
        /// that a path which cannot mean anything is refused before a body is read, and checked per
        /// segment so that a name containing two dots is not mistaken for one that climbs.
        /// </remarks>
        private static bool IsUsablePath(string rawPath)
        {
            if (rawPath.Length == 0 || rawPath[0] != '/')
            {
                return false;
            }

            foreach (var raw in rawPath.Substring(1).Split('/'))
            {
                string segment;
                if (!PercentEncoding.TryDecodeSegment(raw, out segment))
                {
                    return false;
                }

                if (PercentEncoding.IsTraversal(segment))
                {
                    // Nothing here maps a path to a file, and it never will, but a path that can
                    // climb is a liability the moment somebody forgets that.
                    return false;
                }
            }

            return true;
        }

        private static bool TakesABody(string method)
        {
            return string.Equals(method, "POST", StringComparison.Ordinal)
                   || string.Equals(method, "PUT", StringComparison.Ordinal)
                   || string.Equals(method, "PATCH", StringComparison.Ordinal);
        }

        private static bool IsToken(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (var c in text)
            {
                var allowed = (c >= 'a' && c <= 'z')
                              || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9')
                              || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0;

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>What came of reading a head, which is not the same as what to answer.</summary>
        private enum HeadOutcome
        {
            /// <summary>A whole head, up to and including the blank line.</summary>
            Read,

            /// <summary>Not one byte arrived, so there is nobody to answer.</summary>
            Silence,

            /// <summary>Bytes arrived and then stopped, which is a request that was never one.</summary>
            Broken,

            /// <summary>The cap was reached before the blank line was.</summary>
            TooLong
        }

        /// <summary>
        /// Reads to the blank line that ends a head, never past it and never past the cap.
        /// </summary>
        private static HeadOutcome ReadUntilBlankLine(Stream stream, int cap, out byte[] head)
        {
            var buffer = new List<byte>(512);
            var matched = 0;
            var single = new byte[1];

            while (true)
            {
                int got;
                try
                {
                    got = stream.Read(single, 0, 1);
                }
                catch (IOException)
                {
                    head = null;
                    return Ended(buffer);
                }
                catch (ObjectDisposedException)
                {
                    head = null;
                    return Ended(buffer);
                }

                if (got <= 0)
                {
                    head = null;
                    return Ended(buffer);
                }

                buffer.Add(single[0]);

                // CR LF CR LF, tracked as it arrives rather than by scanning the buffer again.
                var expected = (matched % 2) == 0 ? (byte)0x0D : (byte)0x0A;
                matched = single[0] == expected ? matched + 1 : (single[0] == 0x0D ? 1 : 0);

                if (matched == 4)
                {
                    head = buffer.ToArray();
                    return HeadOutcome.Read;
                }

                if (buffer.Count >= cap)
                {
                    head = null;
                    return HeadOutcome.TooLong;
                }
            }
        }

        /// <summary>
        /// Whether a connection that stopped had said anything first, which is what separates a
        /// port nobody is behind from a request that was cut off.
        /// </summary>
        private static HeadOutcome Ended(List<byte> buffer)
        {
            return buffer.Count == 0 ? HeadOutcome.Silence : HeadOutcome.Broken;
        }
    }
}
