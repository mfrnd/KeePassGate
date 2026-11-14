using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using KeePassGate.Pipeline;
using KeePassGate.Testing;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What becomes of an answer given where a request is decided: kept until the hold has
    /// ended for a read, handed straight to the request for a change, and the same whether
    /// the answer is a document or what writes it.
    /// </summary>
    /// <remarks>
    /// An answer written as it is sent is an entry's values, read as they are written. What
    /// is pinned here is that keeping such an answer keeps what writes it and runs none of
    /// it, so an answer dropped with its context has read nothing. Everything is invented.
    /// </remarks>
    [TestFixture]
    public sealed class DecidedContextTests
    {
        private int _ran;
        private Action<TextWriter> _content;

        [SetUp]
        public void SetUp()
        {
            _ran = 0;
            _content = text =>
            {
                _ran++;
                text.Write("{\"ok\":true}");
            };
        }

        [Test]
        public void Content_is_kept_until_the_answer_is_delivered_and_handed_to_the_request_then()
        {
            var request = Request();
            var decided = new DecidedContext(request, Client(), true);
            var named = new WebHeaderCollection();
            named.Add("ETag", "\"0123456789abcdef0123456789abcdef\"");

            decided.Respond(200, _content, named);

            Assert.That(decided.Answered, Is.True);
            Assert.That(request.Answered, Is.False, "nothing reaches the request while its hold may still lapse");
            Assert.That(_ran, Is.Zero);

            decided.Deliver();

            Assert.That(request.Answered, Is.True);
            Assert.That(request.Status, Is.EqualTo(200));
            Assert.That(request.Content, Is.SameAs(_content));
            Assert.That(request.Json, Is.Null);
            Assert.That(request.Headers, Is.SameAs(named));
            Assert.That(_ran, Is.Zero, "delivering hands on what writes the content, and runs none of it");

            Assert.That(request.Document(), Is.EqualTo("{\"ok\":true}"));
            Assert.That(_ran, Is.EqualTo(1), "it runs where the answer is written");
        }

        [Test]
        public void A_context_dropped_without_delivering_never_ran_its_content()
        {
            // A hold that lapsed drops what the read made of a database let go under it, and
            // the request is answered `busy`. An answer kept as what writes it has made
            // nothing by then, and the answer the request was given stands whatever comes late.
            var request = Request();
            var decided = new DecidedContext(request, Client(), true);

            decided.Respond(200, _content, null);
            request.Respond(503, HttpResponse.ErrorBody("busy"));

            Assert.That(request.Status, Is.EqualTo(503));
            Assert.That(request.Content, Is.Null);
            Assert.That(_ran, Is.Zero);

            decided.Deliver();

            Assert.That(request.Status, Is.EqualTo(503), "the first answer on the request stands");
            Assert.That(request.Content, Is.Null);
            Assert.That(request.Document(), Is.EqualTo(HttpResponse.ErrorBody("busy")));
            Assert.That(_ran, Is.Zero, "content of an answer that was dropped is never run");
        }

        [Test]
        public void The_first_answer_stands_when_both_forms_are_given()
        {
            var written = Request();
            var first = new DecidedContext(written, Client(), true);
            first.Respond(200, _content, null);
            first.Respond(500, "{\"second\":true}");
            first.Deliver();

            Assert.That(written.Status, Is.EqualTo(200));
            Assert.That(written.Content, Is.SameAs(_content));
            Assert.That(written.Json, Is.Null);

            var whole = Request();
            var second = new DecidedContext(whole, Client(), true);
            second.Respond(404, "{\"first\":true}");
            second.Respond(200, _content, null);
            second.Deliver();

            Assert.That(whole.Status, Is.EqualTo(404));
            Assert.That(whole.Json, Is.EqualTo("{\"first\":true}"));
            Assert.That(whole.Content, Is.Null, "content given second is dropped, and never run");
            Assert.That(_ran, Is.Zero);
        }

        [Test]
        public void Content_answered_by_what_a_read_left_for_after_its_hold_is_delivered_the_same_way()
        {
            // A read of an entry answers from the part it leaves for after the hold, which
            // delivering runs first. What that part answers with is handed on in the same step.
            var request = Request();
            var decided = new DecidedContext(request, Client(), true);

            decided.After(() => decided.Respond(200, _content, null));

            Assert.That(decided.Answered, Is.False);
            Assert.That(request.Answered, Is.False);

            decided.Deliver();

            Assert.That(request.Status, Is.EqualTo(200));
            Assert.That(request.Content, Is.SameAs(_content));
            Assert.That(_ran, Is.Zero);
        }

        [Test]
        public void A_context_that_keeps_no_answer_hands_content_straight_to_the_request()
        {
            var request = Request();
            var decided = new DecidedContext(request, Client());
            var named = new WebHeaderCollection();
            named.Add("ETag", "\"0123456789abcdef0123456789abcdef\"");

            decided.Respond(200, _content, named);

            Assert.That(decided.Answered, Is.True);
            Assert.That(request.Status, Is.EqualTo(200));
            Assert.That(request.Content, Is.SameAs(_content));
            Assert.That(request.Headers, Is.SameAs(named));
            Assert.That(_ran, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Answering_with_nothing_to_write_the_content_is_refused_and_answers_nothing(bool keeps)
        {
            var request = Request();
            var decided = new DecidedContext(request, Client(), keeps);

            Assert.Throws<ArgumentNullException>(() => decided.Respond(200, (Action<TextWriter>)null, null));

            decided.Deliver();

            Assert.That(decided.Answered, Is.False);
            Assert.That(request.Answered, Is.False);
        }

        private static ClientIdentity Client()
        {
            return ClientIdentity.Stranger("db");
        }

        private static GateContext Request()
        {
            var head = new HttpRequestHead(
                "GET",
                "/entries/0123456789abcdef0123456789abcdef",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);

            return new GateContext(head, string.Empty, Client());
        }
    }
}
