using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The request parser, which is written here rather than taken as a dependency and therefore
    /// has to earn it. Almost every test is a refusal: guessing is where request smuggling lives.
    /// </summary>
    [TestFixture]
    public sealed class HttpRequestReaderTests
    {
        private GateOptions _options;

        [SetUp]
        public void SetUp()
        {
            _options = new GateOptions();
        }

        [Test]
        public void A_plain_request_is_read()
        {
            var result = Read("GET /gate/status HTTP/1.1\r\nHost: x.kpg\r\n\r\n");

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(result.Head.Method, Is.EqualTo("GET"));
            Assert.That(result.Head.Path, Is.EqualTo("/gate/status"));
            Assert.That(result.Head.ContentLength, Is.Zero);
        }

        [Test]
        public void Headers_are_read_without_regard_to_case()
        {
            var result = Read("GET / HTTP/1.1\r\nX-Thing: value\r\n\r\n");

            Assert.That(result.Head.GetHeader("x-thing"), Is.EqualTo("value"));
            Assert.That(result.Head.GetHeader("X-THING"), Is.EqualTo("value"));
            Assert.That(result.Head.GetHeader("absent"), Is.Null);
        }

        [Test]
        public void A_query_is_split_off_and_decoded()
        {
            var result = Read("GET /thing?first=one&second=two%20three HTTP/1.1\r\n\r\n");

            Assert.That(result.Head.Path, Is.EqualTo("/thing"));
            Assert.That(result.Head.Query["first"], Is.EqualTo("one"));
            Assert.That(result.Head.Query["second"], Is.EqualTo("two three"));
        }

        [Test]
        public void A_body_is_read_to_the_length_that_was_declared()
        {
            var stream = Stream("POST /thing HTTP/1.1\r\nContent-Length: 5\r\n\r\nhello and then some");
            var head = HttpRequestReader.ReadHead(stream, _options);

            string refusal;
            Assert.That(HttpRequestReader.ReadBody(stream, head.Head, out refusal), Is.EqualTo("hello"));
            Assert.That(refusal, Is.Null);
        }

        [Test]
        public void A_body_that_stops_early_is_refused_rather_than_padded()
        {
            var stream = Stream("POST /thing HTTP/1.1\r\nContent-Length: 50\r\n\r\nshort");
            var head = HttpRequestReader.ReadHead(stream, _options);

            string refusal;
            Assert.That(HttpRequestReader.ReadBody(stream, head.Head, out refusal), Is.Null);
            Assert.That(refusal, Is.EqualTo(HttpRequestReader.EndedEarly));
        }

        [Test]
        public void A_body_is_read_as_utf8()
        {
            var body = "{\"name\":\"naïve\"}";
            var bytes = Encoding.UTF8.GetBytes(body);
            var request = "POST /thing HTTP/1.1\r\nContent-Length: " + bytes.Length + "\r\n\r\n";

            var all = new byte[Encoding.ASCII.GetByteCount(request) + bytes.Length];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(request), 0, all, 0, Encoding.ASCII.GetByteCount(request));
            Buffer.BlockCopy(bytes, 0, all, Encoding.ASCII.GetByteCount(request), bytes.Length);
            using (var stream = new MemoryStream(all))
            {
                var head = HttpRequestReader.ReadHead(stream, _options);

                string refusal;
                Assert.That(HttpRequestReader.ReadBody(stream, head.Head, out refusal), Is.EqualTo(body));
                Assert.That(refusal, Is.Null);
            }
        }

        [Test]
        public void A_body_that_is_not_utf8_says_so_rather_than_reporting_a_short_read()
        {
            // Every declared byte arrives, so nothing about the socket or the length is wrong. A
            // refusal naming the length sends whoever reads it looking at the one thing that is in
            // order, and the answer is the same 400 either way, so the sentence is all there is.
            var request = "POST /thing HTTP/1.1\r\nContent-Length: 2\r\n\r\n";
            var all = new byte[Encoding.ASCII.GetByteCount(request) + 2];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(request), 0, all, 0, all.Length - 2);

            // A lead byte announcing a two byte sequence, followed by something that cannot
            // continue one.
            all[all.Length - 2] = 0xC3;
            all[all.Length - 1] = 0x28;

            using (var stream = new MemoryStream(all))
            {
                var head = HttpRequestReader.ReadHead(stream, _options);

                string refusal;
                Assert.That(HttpRequestReader.ReadBody(stream, head.Head, out refusal), Is.Null);
                Assert.That(refusal, Is.EqualTo(HttpRequestReader.NotUtf8));
            }
        }

        [Test]
        public void A_transfer_encoding_is_refused_outright()
        {
            // Not implemented rather than parsed. Two parsers that disagree about framing is the
            // whole of request smuggling.
            var result = Read("POST /thing HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(501));
        }

        [Test]
        public void Two_content_lengths_are_refused()
        {
            var result = Read("POST /thing HTTP/1.1\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(400));
        }

        [TestCase("X-Thing: a\rb")]
        [TestCase("X-Thing: a\nb")]
        [TestCase("X-Thing: a\0b")]
        [TestCase("X-Thing: a\u001Bb")]
        [TestCase("X-Thing: a\u007Fb")]
        [TestCase("Expect: 100-continue\n")]
        public void A_header_value_holding_a_control_character_is_refused(string header)
        {
            // RFC 9110 section 5.5. A bare CR or LF is not a line ending here, so either can sit
            // inside a value, and a value that trims into a match, like the last case, is exactly
            // the guess this reader does not make.
            var result = Read("GET /thing HTTP/1.1\r\n" + header + "\r\n\r\n");

            Assert.That(result.Ok, Is.False, header);
            Assert.That(result.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_tab_inside_a_header_value_is_kept()
        {
            var result = Read("GET /thing HTTP/1.1\r\nX-Thing: a\tb\r\n\r\n");

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(result.Head.GetHeader("X-Thing"), Is.EqualTo("a\tb"));
        }

        [Test]
        public void A_folded_header_is_refused()
        {
            var result = Read("GET / HTTP/1.1\r\nX-Thing: one\r\n  two\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_body_larger_than_the_cap_is_refused_before_it_is_read()
        {
            var result = Read("POST /thing HTTP/1.1\r\nContent-Length: " + (_options.MaxBodyBytes + 1) + "\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(413));
        }

        [Test]
        public void A_head_larger_than_the_cap_is_refused()
        {
            var padding = new string('a', _options.MaxHeadBytes);
            var result = Read("GET / HTTP/1.1\r\nX-Thing: " + padding + "\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(431));
        }

        [Test]
        public void A_bodied_method_without_a_length_is_refused()
        {
            var result = Read("POST /thing HTTP/1.1\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(411));
        }

        [TestCase("GET /thing HTTP/2.0\r\n\r\n", 505)]
        [TestCase("GET http://example.com/thing HTTP/1.1\r\n\r\n", 400)]
        [TestCase("GET  HTTP/1.1\r\n\r\n", 400)]
        [TestCase("GET /thing\r\n\r\n", 400)]
        [TestCase("GET /thing HTTP/1.1 extra\r\n\r\n", 400)]
        [TestCase("GE T /thing HTTP/1.1\r\n\r\n", 400)]
        [TestCase("GET /thing HTTP/1.1\r\nBadHeader\r\n\r\n", 400)]
        [TestCase("GET /thing HTTP/1.1\r\n: value\r\n\r\n", 400)]
        [TestCase("GET /thing HTTP/1.1\r\nContent-Length: many\r\n\r\n", 400)]
        [TestCase("GET /thing HTTP/1.1\r\nContent-Length: -1\r\n\r\n", 400)]
        public void Malformed_requests_are_refused_with_a_status(string request, int status)
        {
            var result = Read(request);

            Assert.That(result.Ok, Is.False, "was read as " + (result.Head == null ? "nothing" : result.Head.Path));
            Assert.That(result.Status, Is.EqualTo(status));
        }

        [Test]
        public void A_path_that_climbs_is_refused()
        {
            // Nothing maps a path to a file, and nothing ever will, but a path that can climb is a
            // liability the moment somebody forgets that.
            Assert.That(Read("GET /../../secrets HTTP/1.1\r\n\r\n").Ok, Is.False);
            Assert.That(Read("GET /a/%2e%2e/b HTTP/1.1\r\n\r\n").Ok, Is.False);
        }

        [Test]
        public void A_percent_escape_that_would_produce_a_control_character_is_refused()
        {
            // A parser that accepts %0A is a parser that puts a line break into a log.
            Assert.That(Read("GET /thing%0A/other HTTP/1.1\r\n\r\n").Ok, Is.False);
            Assert.That(Read("GET /thing%00 HTTP/1.1\r\n\r\n").Ok, Is.False);
        }

        [TestCase("GET /thing%2 HTTP/1.1\r\n\r\n")]
        [TestCase("GET /thing% HTTP/1.1\r\n\r\n")]
        [TestCase("GET /thing%zz HTTP/1.1\r\n\r\n")]
        public void A_malformed_percent_escape_is_refused_rather_than_repaired(string request)
        {
            Assert.That(Read(request).Ok, Is.False);
        }

        [TestCase("GET /entries/x/fields/a|b HTTP/1.1\r\n\r\n")]
        [TestCase("GET /entries/x/fields/a#b HTTP/1.1\r\n\r\n")]
        [TestCase("GET /thing?a={b} HTTP/1.1\r\n\r\n")]
        public void A_target_carrying_a_character_it_should_have_escaped_is_refused(string request)
        {
            // RFC 9112 section 3: a request line the grammar makes invalid is answered with 400
            // rather than processed.
            var result = Read(request);

            Assert.That(result.Ok, Is.False, request);
            Assert.That(result.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_target_longer_than_anything_reasonable_is_refused()
        {
            var result = Read("GET /" + new string('a', 3000) + " HTTP/1.1\r\n\r\n");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Status, Is.EqualTo(414));
        }

        [Test]
        public void A_byte_that_is_not_ascii_is_refused_rather_than_replaced()
        {
            // `Encoding.ASCII` substitutes a question mark for every byte above 0x7F, and a
            // question mark is what the target is split on. Left to it, the bytes below arrive as
            // the path `/gate/status` with a query, and the request answered is not the one sent.
            var request = new List<byte>();
            request.AddRange(Encoding.ASCII.GetBytes("GET /gate/status"));
            request.Add(0x80);
            request.AddRange(Encoding.ASCII.GetBytes("xy HTTP/1.1\r\n\r\n"));

            using (var stream = new MemoryStream(request.ToArray()))
            {
                var result = HttpRequestReader.ReadHead(stream, _options);

                Assert.That(result.Ok, Is.False,
                    "was read as " + (result.Head == null ? "nothing" : result.Head.Path));
                Assert.That(result.Status, Is.EqualTo(400));
            }
        }

        [Test]
        public void A_repeated_header_is_refused_rather_than_resolved()
        {
            // Keeping the last of two is a guess, and an intermediary in front of this would have
            // to make the same guess to agree with it.
            Assert.That(Read("GET /a HTTP/1.1\r\nExpect: 100-continue\r\nExpect: other\r\n\r\n").Ok, Is.False);
            Assert.That(Read("GET /a HTTP/1.1\r\nHost: a\r\nhost: b\r\n\r\n").Ok, Is.False);
        }

        [TestCase("GET /a?limit=10&limit=100000 HTTP/1.1\r\n\r\n")]
        [TestCase("GET /a?group=a&group=a HTTP/1.1\r\n\r\n")]
        [TestCase("GET /a?x=1&x HTTP/1.1\r\n\r\n")]
        [TestCase("GET /a?x+y=1&x%20y=2 HTTP/1.1\r\n\r\n")]
        public void A_repeated_query_name_is_refused_rather_than_resolved(string request)
        {
            // The same guess about a query, and waiting for the first operation that declares one:
            // a limit sent twice would be served as whichever came last. Names are compared once
            // decoded, so two spellings of one name are one name sent twice.
            var result = Read(request);

            Assert.That(result.Ok, Is.False, request);
            Assert.That(result.Status, Is.EqualTo(400));
        }

        [Test]
        public void An_escape_is_read_as_utf8_rather_than_one_character_per_byte()
        {
            var result = Read("GET /a?title=caf%C3%A9 HTTP/1.1\r\n\r\n");

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(result.Head.Query["title"], Is.EqualTo("café"));
        }

        [Test]
        public void An_escape_that_is_not_utf8_is_refused_rather_than_repaired()
        {
            Assert.That(Read("GET /a?title=%C3 HTTP/1.1\r\n\r\n").Ok, Is.False);
            Assert.That(Read("GET /a?title=%FF%FE HTTP/1.1\r\n\r\n").Ok, Is.False);
        }

        [Test]
        public void A_head_that_stops_half_way_is_answered_rather_than_ignored()
        {
            // Somebody is there: they said something first. Silence is what a port with nothing
            // behind it looks like, and it is not this.
            var result = Read("GET /gate/status HTTP/1.1\r\nHo");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Silent, Is.False, "bytes arrived, so there is somebody to answer");
            Assert.That(result.Status, Is.EqualTo(400));
        }

        [Test]
        public void A_body_costs_nothing_until_it_arrives()
        {
            // The length is the caller's number, and a caller that has proved nothing chooses it.
            var head = Read("POST /a HTTP/1.1\r\nContent-Length: 1048576\r\n\r\n");

            Assert.That(head.Ok, Is.True, head.Reason);
            string refusal;
            Assert.That(HttpRequestReader.ReadBody(Stream("short"), head.Head, out refusal), Is.Null);
            Assert.That(refusal, Is.EqualTo(HttpRequestReader.EndedEarly));
        }

        [Test]
        public void A_refusal_says_how_much_body_is_still_on_its_way()
        {
            // What the refusal has to take off the wire before the connection ends. Without it the
            // answer is written into a socket that is then closed over the peer's unread bytes, and
            // the reset that follows discards it.
            var result = Read("POST /thing HTTP/1.1\r\nContent-Length: " + (_options.MaxBodyBytes + 1) + "\r\n\r\n");

            Assert.That(result.Status, Is.EqualTo(413));
            Assert.That(result.Announced, Is.EqualTo(_options.MaxBodyBytes + 1L));
        }

        [Test]
        public void A_refusal_to_a_client_that_asked_first_announces_nothing()
        {
            // It is waiting to be invited, so nothing is on its way and reading would leave both
            // sides waiting for the other.
            var result = Read(
                "POST /thing HTTP/1.1\r\nExpect: 100-continue\r\nContent-Length: " +
                (_options.MaxBodyBytes + 1) + "\r\n\r\n");

            Assert.That(result.Status, Is.EqualTo(413));
            Assert.That(result.Announced, Is.Zero);
        }

        [Test]
        public void A_refusal_decided_before_the_headers_announces_nothing()
        {
            // Nothing was parsed, so there is no length to bound a read by. Said here because it is
            // a limit of the answer rather than an oversight in it.
            var result = Read("BOGUS-VERSION /thing HTTP/9.9\r\nContent-Length: 5\r\n\r\nhello");

            Assert.That(result.Status, Is.EqualTo(505));
            Assert.That(result.Announced, Is.Zero);
        }

        [Test]
        public void What_was_announced_is_dropped_rather_than_kept()
        {
            var stream = Stream("0123456789");

            HttpRequestReader.Discard(stream, 6);

            Assert.That(Encoding.ASCII.GetString(Remaining(stream)), Is.EqualTo("6789"));
        }

        [Test]
        public void Discarding_more_than_arrives_ends_with_the_connection()
        {
            // The count is the peer's own number and bounds nothing on its own. A stream that stops
            // giving is the end of it, exactly as it is for a body that is being read.
            var stream = Stream("short");

            Assert.DoesNotThrow(() => HttpRequestReader.Discard(stream, 1024 * 1024));
            Assert.That(Remaining(stream), Is.Empty);
        }

        [Test]
        public void A_connection_that_says_nothing_is_answered_with_nothing()
        {
            using (var stream = new MemoryStream(Array.Empty<byte>()))
            {
                var result = HttpRequestReader.ReadHead(stream, _options);

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Silent, Is.True, "there is nobody to answer");
            }
        }

        [Test]
        public void A_head_is_read_and_no_more_than_the_head()
        {
            var stream = Stream("GET / HTTP/1.1\r\nContent-Length: 4\r\n\r\nbody");
            HttpRequestReader.ReadHead(stream, _options);

            Assert.That(Encoding.ASCII.GetString(Remaining(stream)), Is.EqualTo("body"));
        }

        private HttpReadResult Read(string request)
        {
            return HttpRequestReader.ReadHead(Stream(request), _options);
        }

        private static MemoryStream Stream(string request)
        {
            return new MemoryStream(Encoding.ASCII.GetBytes(request));
        }

        private static byte[] Remaining(MemoryStream stream)
        {
            var left = new byte[stream.Length - stream.Position];
            stream.Read(left, 0, left.Length);
            return left;
        }
    }
}
