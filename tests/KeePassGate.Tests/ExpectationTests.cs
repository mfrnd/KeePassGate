using System;
using System.Linq;
using System.Text;
using KeePassGate.Authority;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// `Expect: 100-continue`, which a client sends when it wants to know its head was acceptable
    /// before it spends anything sending the body.
    /// </summary>
    /// <remarks>
    /// It is here because libcurl sends it automatically for a body over 1 KB, and a server that
    /// never answers leaves such a client waiting for its own timeout on every request that carries
    /// one. Pairing hands over a bundle and takes a body, so this is not hypothetical.
    ///
    /// The test that matters most is the one about ordering: the refusals that can be made from the
    /// head alone are made **before** the client is invited to send, so a body that would have been
    /// refused is never sent at all.
    /// </remarks>
    [TestFixture]
    public sealed class ExpectationTests
    {
        private const string Continue = "HTTP/1.1 100 Continue\r\n\r\n";

        private GateOptions _options;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _options = new GateOptions { MaxBodyBytes = 64 };
            _log = new RecordingGateLog();
        }

        [Test]
        public void A_client_that_asks_to_continue_is_told_to_continue_and_then_answered()
        {
            var stream = Serve("POST", "Expect: 100-continue\r\n", "{}");

            Assert.That(stream.Response, Does.StartWith(Continue), "the interim answer comes first");
            Assert.That(stream.Response, Does.Contain("HTTP/1.1 200"), "and the real one follows it");
        }

        [Test]
        public void The_interim_answer_does_not_end_the_message()
        {
            // Written raw rather than through `HttpResponse`, which would append `Connection: close`
            // and tell the client the conversation was over before it had sent anything.
            var stream = Serve("POST", "Expect: 100-continue\r\n", "{}");
            var interim = stream.Response.Substring(0, stream.Response.IndexOf("\r\n\r\n", StringComparison.Ordinal));

            Assert.That(interim, Is.EqualTo("HTTP/1.1 100 Continue"));
            Assert.That(interim, Does.Not.Contain("Connection"));
            Assert.That(interim, Does.Not.Contain("Content-Length"));
        }

        [Test]
        public void The_body_still_arrives_and_still_reaches_the_handler()
        {
            var handler = new SpyHandler(context => context.Respond(200, "{\"seen\":\"" + context.Body + "\"}"));
            var stream = new ConversationStream(
                "POST /gate/status HTTP/1.1\r\nExpect: 100-continue\r\nContent-Length: 4\r\n\r\nbody");

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(stream.Response, Does.Contain("body"));
        }

        [TestCase("expect: 100-continue")]
        [TestCase("Expect: 100-Continue")]
        [TestCase("EXPECT: 100-CONTINUE")]
        [TestCase("Expect:  100-continue \r")]
        public void It_is_recognized_however_it_is_spelled(string header)
        {
            // Field names are case insensitive and so is this token, RFC 9110 section 10.1.1.
            var stream = Serve("POST", header.TrimEnd('\r') + "\r\n", "{}");

            Assert.That(stream.Response, Does.StartWith(Continue), "on: " + header);
        }

        [Test]
        public void A_body_that_is_too_long_is_refused_before_the_client_is_invited_to_send_it()
        {
            // The ordering that makes this worth having. `ReadHead` has already refused the length,
            // so the client learns that from a 413 instead of after uploading a megabyte.
            var stream = new ConversationStream(
                "POST /gate/status HTTP/1.1\r\nExpect: 100-continue\r\nContent-Length: 5000\r\n\r\n");

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(413));
            Assert.That(stream.Response, Does.Not.Contain("100 Continue"),
                "inviting a body this server has already refused is the defect this pins");
        }

        [Test]
        public void A_transfer_encoding_is_still_refused_before_any_continue()
        {
            var stream = new ConversationStream(
                "POST /gate/status HTTP/1.1\r\nExpect: 100-continue\r\nTransfer-Encoding: chunked\r\n\r\n");

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Status, Is.EqualTo(501));
            Assert.That(stream.Response, Does.Not.Contain("100 Continue"));
        }

        [Test]
        public void An_expectation_this_server_does_not_meet_is_refused_rather_than_ignored()
        {
            // Fail closed. Ignoring one means carrying on while the client believes something that
            // is not true.
            var stream = Serve("POST", "Expect: the-moon-on-a-stick\r\n", "{}");

            Assert.That(stream.Status, Is.EqualTo(417));
            Assert.That(stream.Response, Does.Contain("expectation"));
            Assert.That(_log.Warnings, Is.Not.Empty);
        }

        [Test]
        public void What_was_expected_is_not_repeated_into_the_log()
        {
            // The one string a peer chooses that reached the plugin's log, at any length the head
            // allows and carrying whatever it carried. What was refused is named; what it said is
            // not.
            var expected = "the-moon-on-a-stick-" + new string('x', 4000);

            var stream = Serve("POST", "Expect: " + expected + "\r\n", "{}");

            Assert.That(stream.Status, Is.EqualTo(417));
            Assert.That(_log.Entries.Any(e => e.Message.Contains("the-moon")), Is.False);
            Assert.That(_log.Warnings.Any(w => w.Message.Contains("100-continue")), Is.True);
        }

        [Test]
        public void An_unmet_expectation_never_reaches_a_handler()
        {
            var handler = new SpyHandler(context => context.Respond(200, "{}"));
            var stream = new ConversationStream(
                "POST /gate/status HTTP/1.1\r\nExpect: something-else\r\nContent-Length: 2\r\n\r\n{}");

            GateConversation.Serve(stream, Paired(), handler, _options, _log);

            Assert.That(handler.Calls, Is.Zero);
        }

        [Test]
        public void A_request_without_an_expectation_is_answered_exactly_as_before()
        {
            var stream = Serve("POST", string.Empty, "{}");

            Assert.That(stream.Response, Does.Not.Contain("100 Continue"));
            Assert.That(stream.Status, Is.EqualTo(200));
        }

        [Test]
        public void A_GET_carrying_an_expectation_is_still_answered()
        {
            // Odd but legal, and refusing it would be inventing a rule.
            var stream = new ConversationStream(
                "GET /gate/status HTTP/1.1\r\nExpect: 100-continue\r\n\r\n");

            GateConversation.Serve(stream, Paired(), new SpyHandler(c => c.Respond(200, "{}")), _options, _log);

            Assert.That(stream.Response, Does.StartWith(Continue));
            Assert.That(stream.Response, Does.Contain("HTTP/1.1 200"));
        }

        private ConversationStream Serve(string method, string headers, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            var request = method + " /gate/status HTTP/1.1\r\n" + headers +
                          "Content-Length: " + bytes.Length + "\r\n\r\n" + body;

            var stream = new ConversationStream(request);
            GateConversation.Serve(
                stream, Paired(), new SpyHandler(c => c.Respond(200, "{\"ok\":true}")), _options, _log);

            return stream;
        }

        /// <summary>A handler that records whether it was reached at all.</summary>
        private sealed class SpyHandler : IGateHandler
        {
            private readonly Action<IGateContext> _act;

            public SpyHandler(Action<IGateContext> act)
            {
                _act = act;
            }

            public int Calls { get; private set; }

            public void Handle(IGateContext context)
            {
                Calls++;
                _act(context);
            }
        }

        private static ClientIdentity Paired()
        {
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("db", client, Array.Empty<string>());
            }
        }
    }
}
