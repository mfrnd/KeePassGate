using System;
using System.IO;
using System.Net;
using System.Text;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
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
        /// <summary>
        /// What a locked database answers, on every path there is. One word, so that a client
        /// library can match on it and wait rather than parse a sentence.
        /// </summary>
        public const string LockedError = Gate.LockedTarget.Error;

        /// <summary>
        /// Answers a connection to a locked database, having decided before it asked.
        /// </summary>
        /// <remarks>
        /// Here rather than behind the router, and that is the whole point of it. A locked database
        /// has the same answer for every path, every method and every body, so letting the request
        /// be judged can only produce a second answer: a head this reader will not take is a 400,
        /// an expectation it does not meet is a 417, a missing length is a 411, an announced body
        /// over the cap is a 413, and one that stops early is a 400 again. None of those is true.
        /// The database is locked, and that is all there is to say.
        ///
        /// **No body is ever invited.** `Expect: 100-continue` is not answered, because answering
        /// it means telling a client to spend on something already refused.
        ///
        /// **A body already in flight is taken off the wire and dropped**, which is not the same
        /// thing and is not politeness either. Closing a socket with the peer's bytes still unread
        /// is answered by the stack with a reset, and a reset discards what it has already
        /// delivered: the client is left with no answer at all, which is the one outcome the
        /// responder certificate exists to prevent. It happens after the answer rather than before
        /// it, so a client that reads while it writes can stop without finishing. A length over
        /// the cap is taken the same way as any other, because the reader refusing such a head
        /// changes nothing about what the peer is already sending. A client that asked to be told
        /// first has sent nothing, so there is nothing to take and waiting for it would leave both
        /// sides waiting for the other.
        ///
        /// What it names is the lock and nothing else: not which database, not whether anything is
        /// stored, not whether the caller would have been admitted. `PAIRING.md` §308 refuses
        /// pairing here the same way, because issuing needs the authority key and the admission
        /// write and neither exists in this state.
        /// </remarks>
        public static void Locked(Stream stream, GateOptions options, IGateLog log)
        {
            Unavailable(stream, LockedError, options, log);
        }

        /// <summary>
        /// Answers a connection to a database that is served by nobody for now, with the one word
        /// that says why, having decided before it asked.
        /// </summary>
        /// <remarks>
        /// Everything <see cref="Locked"/> says holds for every such word. The other one is
        /// `undecided`, a database holding more than one authority while the operator has not
        /// chosen: the same answer for every path, method and body, and a client that can wait.
        /// </remarks>
        public static void Unavailable(Stream stream, string error, GateOptions options, IGateLog log)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (string.IsNullOrEmpty(error))
            {
                throw new ArgumentException("An unavailable database says why.", nameof(error));
            }

            var logger = log ?? NullGateLog.Instance;
            var read = HttpRequestReader.ReadHead(stream, options ?? new GateOptions());
            if (read.Silent)
            {
                // Nothing arrived, so there is nobody to answer.
                return;
            }

            Answer(stream, 503, HttpResponse.ErrorBody(error), logger, IsHead(read.Method));

            HttpRequestReader.Discard(stream, read.Announced);
        }

        public static void Serve(
            Stream stream, ClientIdentity client, IGateHandler handler, GateOptions options, IGateLog log)
        {
            Serve(stream, client, handler, options, log, null, null);
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
            PairingConnection pairing,
            GateIncidents incidents)
        {
            Serve(stream, client, handler, options, log, pairing, incidents, null);
        }

        /// <summary>
        /// The same, for a connection that reached a database the API can read and change.
        /// </summary>
        /// <remarks>
        /// The database travels in the context's items beside the pairing connection, for the same
        /// reason: the transport knows which database a name resolved to and the handlers need it,
        /// and neither has to learn anything about the other. Its vault is what the API's routes
        /// read and change, and the target itself is what holds its admission records, which is
        /// what a route about a client's own admission writes.
        /// </remarks>
        /// <param name="peer">
        /// The address the connection came from, for the two routes that show it to a person, or
        /// null where nobody knows.
        /// </param>
        /// <param name="arrived">
        /// Told that the whole request is off the wire, and what it asked for, which is the instant
        /// the peer stops having anything to do with how long the rest takes. The listener hangs its
        /// connection deadline on it, and the request is what says whether the rest waits on a
        /// person: see `GateListener.Deadline.Disarm`. Null where nothing is counting, which is
        /// every test of this over a pair of memory streams.
        /// </param>
        public static void Serve(
            Stream stream,
            ClientIdentity client,
            IGateHandler handler,
            GateOptions options,
            IGateLog log,
            PairingConnection pairing,
            GateIncidents incidents,
            Gate.IGateTarget target,
            string peer = null,
            Action<HttpRequestHead> arrived = null)
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
                Answer(
                    stream, read.Status, HttpResponse.ErrorBody(read.Reason), logger,
                    IsHead(read.Method));

                // The body the head announced is on its way regardless of what was decided about
                // the head, and a socket closed over those bytes is reset by the stack, which
                // throws away the refusal just written. `DESIGN.md` §110 promises a 413 for a body
                // over the cap, and a client that streams one without asking first can only be
                // told so if what it is sending is taken.
                HttpRequestReader.Discard(stream, read.Announced);
                return;
            }

            var head = read.Head;
            if (!Expectation(stream, head, logger))
            {
                return;
            }

            string refusal;
            var body = HttpRequestReader.ReadBody(stream, head, out refusal);
            if (body == null)
            {
                // What the reader said, rather than one sentence for both of the things it refuses.
                // A body that is not UTF-8 reported as one that ended early sends whoever reads the
                // answer to the socket and to the length, where nothing is wrong.
                logger.Warn("refused a request: " + refusal);
                Answer(stream, 400, HttpResponse.ErrorBody(refusal), logger, IsHead(head.Method));
                return;
            }

            if (arrived != null)
            {
                // Here rather than after the handler, because here is where the peer stops being
                // able to make this connection take any longer. Everything below is this plugin's
                // own work and the write of the answer, and neither is something to close a socket
                // over: the one bound that matters to the answer is the socket's `SendTimeout`.
                arrived(head);
            }

            var context = new GateContext(head, body, client);
            if (pairing != null)
            {
                pairing.PutOn(context);
            }

            if (target != null)
            {
                new ServedDatabase(target.Vault, target).PutOn(context);
            }

            ConnectionPeer.PutOn(context, peer);

            try
            {
                handler.Handle(context);
            }
            // codeql[cs/catch-of-all-exceptions] the connection thread's, kept here so the client is answered
            catch (Exception error)
            {
                // Part of the connection catch-all, kept here rather than left to it so that the
                // client is answered rather than dropped. A handler is this plugin's own code, so
                // anything it throws is a defect and is counted as one.
                //
                // The method and not the path. A path names what it is about, a field or an
                // attachment by what it is called, and a name does not go into a log that somebody
                // with no right to the database may read. The exception says which handler it was.
                incidents.Record("a handler failed on " + head.Method, error);
                Answer(
                    stream, 500, HttpResponse.ErrorBody("internal error"), logger,
                    IsHead(head.Method));
                return;
            }

            // The one answer a handler decided, so the one that can carry a field of its own.
            Answer(
                stream, context.Status, context.Json, logger, IsHead(head.Method), context.Headers);
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
                // What the peer expected is not repeated. It was the one string a peer chooses that
                // reached this log, at any length the head allows and carrying whatever it carried,
                // and saying which expectation this server meets is all the line needs.
                log.Warn("refused an expectation other than 100-continue");
                Answer(stream, 417, HttpResponse.ErrorBody("expectation"), log, IsHead(head.Method));
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

        /// <summary>Whether this method's answer may carry content at all.</summary>
        /// <remarks>
        /// Asked at every exit rather than at one of them. A HEAD is refused by the reader, by
        /// the router, by the guard and by the body check, and RFC 9112 section 6.3 applies to
        /// all of them: only the answer to a request nobody could read has no method to ask about.
        /// </remarks>
        private static bool IsHead(string method)
        {
            return string.Equals(method, "HEAD", StringComparison.Ordinal);
        }

        /// <summary>The refusals, which carry nothing a handler decided because none of them is one.</summary>
        private static void Answer(Stream stream, int status, string json, IGateLog log, bool head)
        {
            Answer(stream, status, json, log, head, null);
        }

        private static void Answer(
            Stream stream, int status, string json, IGateLog log, bool head, WebHeaderCollection headers)
        {
            try
            {
                HttpResponse.Write(stream, status, json, head, headers);
            }
            catch (IOException error)
            {
                // The client went away mid-answer, and there is nothing to be done about it. There
                // is something to say, though, and this used to say nothing: a change that was
                // made and saved and then not reported is the one case where the audit has a
                // `permitted` line, the database has the change, and the client has neither the
                // answer nor the identifier. Without a line here an operator finding the duplicate
                // a retry made has no way to tell it from a refusal.
                Lost(log, status, error);
            }
            catch (ObjectDisposedException error)
            {
                Lost(log, status, error);
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                log.Error("a response could not be written", error);
            }
        }

        /// <summary>Says that an answer was decided and never reached the client.</summary>
        /// <remarks>
        /// A warning rather than an error, because nothing here failed: a peer is allowed to stop
        /// reading. What it is worth is the status, which is what says whether anything happened
        /// to the database before the socket went.
        /// </remarks>
        private static void Lost(IGateLog log, int status, Exception error)
        {
            log.Warn("a " + status + " was decided and could not be written, so the client has no " +
                     "answer: " + error.Message);
        }
    }
}
