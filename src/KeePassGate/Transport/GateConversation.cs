using System;
using System.IO;
using System.Net;
using System.Text;
using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;

namespace KeePassGate.Transport
{
    /// <summary>
    /// One request and one response over a stream that is already authenticated, after which the
    /// connection is finished with.
    /// </summary>
    /// <remarks>
    /// Separate from the listener so that the whole of it can be exercised over a pair of memory
    /// streams: no sockets, no TLS, no certificates. What the listener adds is the part that cannot
    /// be tested that way, and it is kept as small as possible for exactly that reason.
    /// </remarks>
    public static class GateConversation
    {
        public static void Serve(
            Stream stream, ClientIdentity client, IGateHandler handler, GateOptions options, IGateLog log)
        {
            Serve(stream, client, handler, options, log, null);
        }

        /// <summary>
        /// The same, with somewhere to report a handler that fails in a way nobody predicted.
        /// </summary>
        public static void Serve(
            Stream stream,
            ClientIdentity client,
            IGateHandler handler,
            GateOptions options,
            IGateLog log,
            GateIncidents incidents)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            options = options ?? new GateOptions();
            var logger = log ?? NullGateLog.Instance;
            incidents = incidents ?? new GateIncidents(logger);

            var read = HttpRequestReader.ReadHead(stream, options);
            if (read.Silent)
            {
                // Nothing arrived, so there is nobody to answer.
                return;
            }

            if (!read.Ok)
            {
                logger.Warn("refused a request: " + read.Reason);
                Answer(stream, read.Status, HttpResponse.ErrorBody(read.Reason), logger, null);
                return;
            }

            var head = read.Head;
            if (!Expectation(stream, head, logger))
            {
                return;
            }

            var body = HttpRequestReader.ReadBody(stream, head);
            if (body == null)
            {
                logger.Warn("refused a request whose body stopped early");
                Answer(stream, 400, HttpResponse.ErrorBody("the body ended early"), logger, null);
                return;
            }

            var context = new GateContext(head, body, client);

            try
            {
                handler.Handle(context);
            }
            catch (Exception error)
            {
                // Part of the connection catch-all, kept here rather than left to it so that the
                // client is answered rather than dropped. A handler is this plugin's own code, so
                // anything it throws is a defect and is counted as one.
                incidents.Record("a handler failed on " + head.Method + " " + head.Path, error);
                Answer(stream, 500, HttpResponse.ErrorBody("internal error"), logger, null);
                return;
            }

            Answer(stream, context.Status, context.Json, logger, context.Headers);
        }

        /// <summary>
        /// Answers `Expect`, if the client sent one. False when the conversation is over.
        /// </summary>
        /// <remarks>
        /// RFC 9110 section 10.1.1: a client may ask to be told its head was acceptable before it
        /// spends anything sending the body. https://www.rfc-editor.org/rfc/rfc9110#section-10.1.1
        /// **libcurl sends this automatically for a body over 1 KB**, so a server that never answers
        /// leaves such a client waiting for its own timeout before it proceeds.
        ///
        /// **Where this sits is the point.** By now `ReadHead` has already refused a body that is
        /// too long, a duplicate length, and a transfer encoding, all without reading a byte of the
        /// body. So a client that would have been refused is refused **before** being invited to
        /// send, which is better than the alternative: it sends a megabyte and is then told no.
        ///
        /// An expectation that is not `100-continue` is refused rather than ignored, because
        /// ignoring one means proceeding while the client believes something that is not true.
        /// </remarks>
        private static bool Expectation(Stream stream, HttpRequestHead head, IGateLog log)
        {
            var expected = head.GetHeader("Expect");
            if (string.IsNullOrEmpty(expected))
            {
                return true;
            }

            if (!string.Equals(expected.Trim(), "100-continue", StringComparison.OrdinalIgnoreCase))
            {
                log.Warn("refused an expectation this server does not meet: " + expected);
                Answer(stream, 417, HttpResponse.ErrorBody("expectation"), log, null);
                return false;
            }

            try
            {
                // Written raw rather than through `HttpResponse`, which would append
                // `Connection: close` and end the message. This is an interim response: the real
                // one still follows on the same connection.
                var interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                stream.Write(interim, 0, interim.Length);
                stream.Flush();
                return true;
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                // The client went away between its head and its body.
                log.Info("a client left before sending the body it announced");
                return false;
            }
        }

        private static void Answer(
            Stream stream, int status, string json, IGateLog log, WebHeaderCollection headers)
        {
            try
            {
                HttpResponse.Write(stream, status, json, headers);
            }
            catch (IOException)
            {
                // The client went away mid-answer. Nothing to do and nothing to say about it.
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                log.Error("a response could not be written", error);
            }
        }
    }
}
