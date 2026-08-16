using System;
using System.Collections.Generic;
using System.Net;
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
            Assert.That(_log.Warnings, Is.Not.Empty);
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
