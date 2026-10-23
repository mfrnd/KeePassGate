using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// One request, one response, one closed connection: everything above the socket and below the
    /// route, run without either.
    /// </summary>
    [TestFixture]
    public sealed class GateConversationTests
    {
        private RecordingGateLog _log;
        private GateOptions _options;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
            _options = new GateOptions();
        }

        [Test]
        public void A_request_reaches_the_handler_and_its_answer_is_written()
        {
            var stream = new ConversationStream("GET /gate/status HTTP/1.1\r\n\r\n");
            var handler = new SpyHandler(context => context.Respond(200, "{\"ok\":true}"));

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(handler.Seen, Is.Not.Null);
            Assert.That(handler.Seen.Path, Is.EqualTo("/gate/status"));
            Assert.That(stream.Status, Is.EqualTo(200));
            Assert.That(stream.Body, Is.EqualTo("{\"ok\":true}"));
        }

        [Test]
        public void The_response_says_the_connection_is_over()
        {
            var stream = new ConversationStream("GET / HTTP/1.1\r\n\r\n");

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Response, Does.Contain("Connection: close"));
            Assert.That(stream.Response, Does.Contain("Content-Type: application/json"));
            Assert.That(stream.Response, Does.Contain("Cache-Control: no-store"));
        }

        [Test]
        public void A_head_carries_the_fields_its_handler_named_and_no_body()
        {
            // RFC 9110 section 9.3.2: a head is its get with the content left off, and only the
            // content. The tag is the field a client sends a head for, so a writer that dropped the
            // handler's fields on a head would answer every one of them with nothing to compare.
            var stream = new ConversationStream("HEAD /entries/x HTTP/1.1\r\n\r\n");
            var named = new WebHeaderCollection();
            named.Add("ETag", "\"0123456789abcdef0123456789abcdef\"");

            GateConversation.Serve(
                stream,
                Paired(),
                new SpyHandler(c => c.Respond(200, "{\"uuid\":\"x\"}", named)),
                _options,
                _log);

            Assert.That(stream.Status, Is.EqualTo(200));
            Assert.That(stream.Response, Does.Contain("ETag: \"0123456789abcdef0123456789abcdef\"\r\n"));
            Assert.That(stream.Body, Is.Empty);
        }

        [Test]
        public void A_field_the_answer_named_is_written_after_the_ones_every_answer_carries()
        {
            var stream = new ConversationStream("OPTIONS /gate/status HTTP/1.1\r\n\r\n");
            var named = new WebHeaderCollection();
            named.Add("Allow", "GET");

            GateConversation.Serve(
                stream,
                Paired(),
                new SpyHandler(c => c.Respond(405, "{}", named)),
                _options,
                _log);

            Assert.That(stream.Response, Does.Contain("Allow: GET\r\n"));
            Assert.That(stream.Response, Does.Contain("Connection: close"),
                "and the fields every answer carries are still there");
        }

        [Test]
        public void A_field_the_writer_owns_is_dropped_rather_than_sent_twice()
        {
            // A second `Content-Length` is a second opinion about where the body ends, which is
            // how a request is smuggled past whatever reads this next. `WebHeaderCollection`
            // refuses a value carrying CR or LF and does not refuse this, so the writer does.
            var stream = new ConversationStream("GET /gate/status HTTP/1.1\r\n\r\n");
            var named = new WebHeaderCollection();
            named.Add("Content-Length", "9999");
            named.Add("Connection", "keep-alive");

            GateConversation.Serve(
                stream,
                Paired(),
                new SpyHandler(c => c.Respond(200, "{}", named)),
                _options,
                _log);

            Assert.That(stream.Response, Does.Not.Contain("9999"));
            Assert.That(stream.Response, Does.Not.Contain("keep-alive"));
            Assert.That(stream.Response, Does.Contain("Content-Length: 2\r\n"));
        }

        [Test]
        public void The_body_reaches_the_handler()
        {
            var stream = new ConversationStream("POST /thing HTTP/1.1\r\nContent-Length: 7\r\n\r\n{\"a\":1}");
            var handler = new SpyHandler(context => context.Respond(200, context.Body));

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(stream.Body, Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public void The_identity_reaches_the_handler()
        {
            var stream = new ConversationStream("GET / HTTP/1.1\r\n\r\n");
            var identity = Paired();
            var handler = new SpyHandler(context => context.Respond(200, "{}"));

            GateConversation.Serve(stream, identity, handler, _options, _log);

            Assert.That(handler.Seen.Client, Is.SameAs(identity));
        }

        [Test]
        public void A_refused_request_never_reaches_the_handler()
        {
            var stream = new ConversationStream("POST /thing HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");
            var handler = new SpyHandler(context => context.Respond(200, "{}"));

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(handler.Seen, Is.Null);
            Assert.That(stream.Status, Is.EqualTo(501));
            Assert.That(stream.Body, Does.Contain("error"), "and it still says why");
            Assert.That(_log.Warnings, Is.Not.Empty);
        }

        [Test]
        public void A_body_that_is_not_utf8_is_refused_as_that_rather_than_as_a_short_one()
        {
            // Both refusals are a 400, so the sentence is the whole of what a caller is told. One
            // that names the length is a caller checking a socket and a `Content-Length` that are
            // both in order, and never looking at the bytes.
            var request = "POST /thing HTTP/1.1\r\nContent-Length: 2\r\n\r\n";
            var all = new byte[Encoding.ASCII.GetByteCount(request) + 2];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(request), 0, all, 0, all.Length - 2);
            all[all.Length - 2] = 0xC3;
            all[all.Length - 1] = 0x28;

            var stream = new ConversationStream(all);
            var handler = new SpyHandler(context => context.Respond(200, "{}"));

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(handler.Seen, Is.Null, "and nothing nobody sent reaches a handler");
            Assert.That(stream.Status, Is.EqualTo(400));
            Assert.That(stream.Body, Does.Contain(HttpRequestReader.NotUtf8));
        }

        [Test]
        public void A_head_the_reader_refuses_is_answered_without_content()
        {
            // RFC 9112 section 6.3: a response to HEAD is terminated by the blank line and cannot
            // carry content. A refusal is a response, so the reader has to carry the method it
            // read before it refused. It took one and dropped it, which left every refused HEAD
            // answered as though it were a GET.
            var stream = new ConversationStream(
                "HEAD /gate/status HTTP/1.1\r\nX-Thing: one\r\nX-Thing: two\r\n\r\n");

            GateConversation.Serve(
                stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(400));
            Assert.That(stream.Response, Does.Not.Contain("Content-Length"));
            Assert.That(stream.Body, Is.Empty);
        }

        [Test]
        public void A_head_refused_on_its_request_line_is_answered_without_content_too()
        {
            // The method is settled as soon as it is read rather than once the whole line is
            // believed, so the refusals that judge the target and the version carry it as well.
            var stream = new ConversationStream("HEAD /gate/status HTTP/2.0\r\n\r\n");

            GateConversation.Serve(
                stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(505));
            Assert.That(stream.Response, Does.Not.Contain("Content-Length"));
            Assert.That(stream.Body, Is.Empty);
        }

        [Test]
        public void A_head_refused_before_its_request_line_is_read_is_answered_without_content_too()
        {
            // A head over the cap, and one holding a byte that is not ASCII, are refused before
            // the request line is parsed, and the method is read off what arrived for them: the
            // request line is in the buffer either way.
            var tooLong = new ConversationStream(
                "HEAD /gate/status HTTP/1.1\r\nX-Thing: " + new string('a', _options.MaxHeadBytes) + "\r\n\r\n");
            GateConversation.Serve(
                tooLong, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(tooLong.Status, Is.EqualTo(431));
            Assert.That(tooLong.Response, Does.Not.Contain("Content-Length"));
            Assert.That(tooLong.Body, Is.Empty);

            var bytes = new List<byte>(System.Text.Encoding.ASCII.GetBytes("HEAD /gate/status HTTP/1.1\r\nX-Thing: "));
            bytes.Add(0xE9);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("\r\n\r\n"));
            var notAscii = new ConversationStream(bytes.ToArray());
            GateConversation.Locked(notAscii, _options, _log);

            Assert.That(notAscii.Status, Is.EqualTo(503), "a locked database answers before the head is judged");
            Assert.That(notAscii.Response, Does.Not.Contain("Content-Length"));
            Assert.That(notAscii.Body, Is.Empty);

            var refused = new ConversationStream(bytes.ToArray());
            GateConversation.Serve(
                refused, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(refused.Status, Is.EqualTo(400));
            Assert.That(refused.Response, Does.Not.Contain("Content-Length"));
            Assert.That(refused.Body, Is.Empty);
        }

        [Test]
        public void A_head_a_locked_database_refuses_is_answered_without_content()
        {
            // The lock answers at an exit of its own, and the rule about content reaches that one
            // for the same reason it reaches the others.
            var stream = new ConversationStream(
                "HEAD /gate/status HTTP/1.1\r\nX-Thing: one\r\nX-Thing: two\r\n\r\n");

            GateConversation.Locked(stream, _options, _log);

            Assert.That(stream.Status, Is.EqualTo(503));
            Assert.That(stream.Response, Does.Not.Contain("Content-Length"));
            Assert.That(stream.Body, Is.Empty);
        }

        [Test]
        public void And_so_is_a_head_it_read_without_complaint()
        {
            // The other kind of result reaching the same exit. This head is well formed, so what
            // the lock is answering carries a request rather than a refusal, and the method it
            // names is the one `HttpReadResult.Read` copied off that request. A `Read` that
            // carried anything else puts a body and a length back on this answer while the
            // refusal above goes on passing, which is the only way the two could come apart.
            var stream = new ConversationStream("HEAD /gate/status HTTP/1.1\r\n\r\n");

            GateConversation.Locked(stream, _options, _log);

            Assert.That(stream.Status, Is.EqualTo(503));
            Assert.That(stream.Response, Does.Not.Contain("Content-Length"));
            Assert.That(stream.Body, Is.Empty);
        }

        [Test]
        public void A_connection_that_says_nothing_is_answered_with_nothing()
        {
            var stream = new ConversationStream(string.Empty);

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Answered, Is.False);
        }

        [Test]
        public void A_handler_that_throws_becomes_a_500_and_the_reason_is_logged()
        {
            // An unhandled exception in this process takes the password manager with it.
            var stream = new ConversationStream("GET /thing HTTP/1.1\r\n\r\n");
            var handler = new SpyHandler(context => { throw new InvalidOperationException("handler exploded"); });

            Assert.DoesNotThrow(() => GateConversation.Serve(stream, Paired(), handler, _options, _log));

            Assert.That(stream.Status, Is.EqualTo(500));
            Assert.That(stream.Body, Does.Not.Contain("exploded"), "a client is told nothing about our internals");

            // A handler is this plugin's own code, so anything it throws is a defect rather than a
            // refusal, and it is counted where the operator will see it rather than logged as one
            // more thing that went wrong.
            Assert.That(_log.Surprises, Is.Not.Empty);
        }

        [Test]
        public void A_handler_that_throws_on_a_named_thing_leaves_the_name_out_of_the_log()
        {
            // A path names a field or an attachment by what it is called, and a name is exactly
            // what the log is kept free of.
            var stream = new ConversationStream(
                "GET /entries/00000000000000000000000000000001/fields/Recovery%20codes HTTP/1.1\r\n\r\n");
            var handler = new SpyHandler(context => { throw new InvalidOperationException("handler exploded"); });

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(stream.Status, Is.EqualTo(500));
            Assert.That(_log.Surprises, Is.Not.Empty);

            foreach (var entry in _log.Entries)
            {
                Assert.That(entry.Message, Does.Not.Contain("Recovery"));
            }
        }

        [Test]
        public void An_upgrade_is_never_honored()
        {
            // `DESIGN.md` §104 lists "WebSocket upgrade, never implemented" among the boundary
            // refusals, and it was the one row of that table with nothing pinning it. Never
            // implemented has to mean answered as an ordinary request, not answered with a 101 by
            // some later library that thinks it is being helpful.
            var stream = new ConversationStream(
                "GET /gate/status HTTP/1.1\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n" +
                "Sec-WebSocket-Version: 13\r\nSec-WebSocket-Key: AAAAAAAAAAAAAAAAAAAAAA==\r\n\r\n");

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(200), "an ordinary answer");
            Assert.That(stream.Response, Does.Not.Contain("101"));
            Assert.That(stream.Response, Does.Not.Contain("Sec-WebSocket-Accept"));
            Assert.That(stream.Response, Does.Contain("Connection: close"), "and the connection ends");
        }

        [Test]
        public void A_handler_that_answers_nothing_is_a_500_rather_than_an_empty_200()
        {
            var stream = new ConversationStream("GET /thing HTTP/1.1\r\n\r\n");

            GateConversation.Serve(stream, Paired(), new SpyHandler(context => { }), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(500));
        }

        [Test]
        public void The_body_behind_a_refused_head_is_taken_off_the_wire()
        {
            // The refusal is decided from the head alone, so the body the client announced is still
            // arriving. Over a socket, closing while those bytes are unread is answered with a
            // reset, and the reset discards the refusal already delivered: the caller is left with
            // a connection failure it cannot tell from a gate that is not running.
            _options.MaxBodyBytes = 64;
            var stream = new ConversationStream(
                "POST /thing HTTP/1.1\r\nContent-Length: 200\r\n\r\n" + new string('x', 200));

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(413));
            Assert.That(stream.Unread, Is.Zero, "what is left unread is what the answer is lost to");
        }

        [Test]
        public void A_body_the_client_asked_to_be_told_about_first_is_never_waited_for()
        {
            // It has sent nothing and is waiting to be invited, so reading the length it named
            // would leave both sides waiting for the other.
            _options.MaxBodyBytes = 64;
            var stream = new ConversationStream(
                "POST /thing HTTP/1.1\r\nExpect: 100-continue\r\nContent-Length: 200\r\n\r\n" +
                new string('x', 200));

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(413));
            Assert.That(stream.Unread, Is.EqualTo(200), "nothing was sent, so there is nothing to take");
        }

        [Test]
        public void A_body_that_stops_early_is_refused()
        {
            var stream = new ConversationStream("POST /thing HTTP/1.1\r\nContent-Length: 100\r\n\r\nshort");

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(400));
        }

        private static ClientIdentity Paired()
        {
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired(authority.DatabaseId, client, new List<string>());
            }
        }

        private sealed class SpyHandler : IGateHandler
        {
            private readonly Action<IGateContext> _act;

            public SpyHandler(Action<IGateContext> act)
            {
                _act = act;
            }

            public IGateContext Seen { get; private set; }

            public void Handle(IGateContext context)
            {
                Seen = context;
                _act(context);
            }
        }
    }

    [TestFixture]
    public sealed class GateContextTests
    {
        [Test]
        public void The_first_answer_stands()
        {
            var context = NewContext();

            context.Respond(200, "{\"first\":true}");
            context.Respond(500, "{\"second\":true}");

            Assert.That(context.Answered, Is.True);
            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(context.Json, Is.EqualTo("{\"first\":true}"));
        }

        [Test]
        public void An_unanswered_context_is_a_500_rather_than_a_blank()
        {
            var context = NewContext();

            Assert.That(context.Answered, Is.False);
            Assert.That(context.Status, Is.EqualTo(500));
            Assert.That(context.Json, Does.Contain("error"));
        }

        [Test]
        public void Items_carry_things_between_stages()
        {
            var context = NewContext();

            context.Items["stage"] = 42;

            Assert.That(context.Items["stage"], Is.EqualTo(42));
        }

        [Test]
        public void A_context_without_a_request_or_a_client_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => new GateContext(null, "", ClientIdentity.Stranger("db")));
            Assert.Throws<ArgumentNullException>(() => new GateContext(Head(), "", null));
        }

        private static GateContext NewContext()
        {
            return new GateContext(Head(), string.Empty, ClientIdentity.Stranger("db"));
        }

        private static HttpRequestHead Head()
        {
            return new HttpRequestHead(
                "GET",
                "/thing",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);
        }
    }
}
