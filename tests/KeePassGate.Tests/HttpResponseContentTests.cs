using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// An answer whose content is written as it is sent, as it goes on the wire: what its head
    /// names, how its content is framed, and what a failure part way leaves behind.
    /// </summary>
    /// <remarks>
    /// The answer to a read of an entry or of a field goes out this way, with its length not
    /// known when its head is written. What a client relies on is pinned here: the head names
    /// chunks and no length, the chunks decode to exactly the text written, and content that
    /// was not finished is never closed by the last chunk. Everything written is invented.
    /// </remarks>
    [TestFixture]
    public sealed class HttpResponseContentTests
    {
        private const string Tag = "\"0123456789abcdef0123456789abcdef\"";

        [Test]
        public void The_head_names_chunks_and_no_length_and_the_content_follows_in_chunks()
        {
            var raw = Written(200, text => text.Write("{\"ok\":true}"), false, Tagged());
            var written = Encoding.UTF8.GetString(raw);

            Assert.That(written, Does.StartWith("HTTP/1.1 200 OK\r\n"));
            Assert.That(written, Does.Contain("Transfer-Encoding: chunked\r\n"));
            Assert.That(written, Does.Not.Contain("Content-Length"));
            Assert.That(written, Does.Contain("Content-Type: application/json; charset=utf-8\r\n"));
            Assert.That(written, Does.Contain("Cache-Control: no-store\r\n"));
            Assert.That(written, Does.Contain("Connection: close\r\n"));
            Assert.That(written, Does.Contain("ETag: " + Tag + "\r\n"), "a field the handler named is carried");
            Assert.That(written, Does.EndWith("\r\n\r\nb\r\n{\"ok\":true}\r\n0\r\n\r\n"),
                "the content as a chunk with nothing ahead of it, and then the last chunk");
            Assert.That(HttpAnswer.Decoded(raw), Does.EndWith("\r\n\r\n{\"ok\":true}"));
        }

        [Test]
        public void Content_that_writes_nothing_is_the_last_chunk_alone()
        {
            var written = Encoding.UTF8.GetString(Written(200, text => { }, false, null));

            Assert.That(written, Does.Contain("Transfer-Encoding: chunked\r\n"));
            Assert.That(written, Does.EndWith("\r\n\r\n0\r\n\r\n"));
        }

        [Test]
        public void A_caller_cannot_name_a_framing_of_its_own()
        {
            // A second field telling where the content ends is a second answer to that
            // question, which is how a request is smuggled past whatever reads this next. The
            // writer decides the framing, so a framing field handed in beside the answer is
            // dropped.
            var headers = Tagged();
            headers.Add("Transfer-Encoding", "identity");
            headers.Add("Content-Length", "9999");

            var written = Encoding.UTF8.GetString(Written(200, text => text.Write("{}"), false, headers));

            Assert.That(written, Does.Not.Contain("identity"));
            Assert.That(written, Does.Not.Contain("9999"));
            Assert.That(written, Does.Not.Contain("Content-Length"));
            Assert.That(
                written.Split(new[] { "Transfer-Encoding" }, StringSplitOptions.None).Length - 1,
                Is.EqualTo(1),
                "the framing is named a single time");
            Assert.That(written, Does.Contain("Transfer-Encoding: chunked\r\n"));
            Assert.That(written, Does.Contain("ETag: " + Tag + "\r\n"));
        }

        [Test]
        public void A_head_is_answered_with_neither_framing_and_its_content_is_never_run()
        {
            // A HEAD asks for no content, and the content of this kind of answer is made of an
            // entry's values, so it is not run at all: asking whether an entry is there reads
            // none of its values. The fields a GET carries stay, less the framing.
            var ran = false;
            var headers = Tagged();
            headers.Add("Transfer-Encoding", "identity");
            headers.Add("Content-Length", "9999");

            var written = Encoding.UTF8.GetString(Written(
                200,
                text =>
                {
                    ran = true;
                    text.Write("{\"ok\":true}");
                },
                true,
                headers));

            Assert.That(ran, Is.False, "the content was run for an answer that carries none");
            Assert.That(written, Does.StartWith("HTTP/1.1 200 OK\r\n"));
            Assert.That(written, Does.Not.Contain("Transfer-Encoding"));
            Assert.That(written, Does.Not.Contain("Content-Length"));
            Assert.That(written, Does.Contain("ETag: " + Tag + "\r\n"));
            Assert.That(written, Does.EndWith("\r\n\r\n"));
            Assert.That(written, Does.Not.Contain("ok"));
            Assert.That(written, Does.Not.EndWith("0\r\n\r\n"), "no chunk follows a head that names none");
        }

        [Test]
        public void Content_that_throws_is_thrown_on_and_what_was_written_has_no_last_chunk()
        {
            // The status is on the wire by the time the content runs, so a failure there cannot
            // be answered. It is thrown to whoever wrote, and what went out is left without the
            // chunk that would close it, so a reader takes none of it for a whole answer.
            using (var stream = new MemoryStream())
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => HttpResponse.Write(
                    stream,
                    200,
                    text =>
                    {
                        text.Write("{\"half\":");
                        throw new InvalidOperationException("an invented failure part way");
                    },
                    false,
                    null));

                Assert.That(thrown.Message, Is.EqualTo("an invented failure part way"));

                var raw = stream.ToArray();
                var written = Encoding.UTF8.GetString(raw);
                Assert.That(written, Does.StartWith("HTTP/1.1 200 OK\r\n"));
                Assert.That(written, Does.Contain("Transfer-Encoding: chunked\r\n"));
                Assert.That(written, Does.Not.EndWith("0\r\n\r\n"));
                Assert.That(written, Does.Not.Contain("half"), "nothing of a failed answer follows the failure");
                Assert.That(
                    () => HttpAnswer.Decoded(raw),
                    Throws.TypeOf<InvalidDataException>(),
                    "a reader that decodes the chunks finds the answer cut short");
            }
        }

        [Test]
        public void Content_that_fails_while_the_peer_is_gone_as_well_is_what_comes_out()
        {
            // The writer of the text still holds what the content wrote when the content fails,
            // and hands it on as it is let go. Sent then, to a peer that is gone, that write
            // fails too, and its failure would come out in place of what failed: an answer
            // lost where there was a defect to count.
            var under = new RecordingStream();

            var thrown = Assert.Throws<InvalidOperationException>(() => HttpResponse.Write(
                under,
                200,
                text =>
                {
                    text.Write("{\"half\":");
                    under.Fails = true;
                    throw new InvalidOperationException("an invented failure part way");
                },
                false,
                null));

            Assert.That(thrown.Message, Is.EqualTo("an invented failure part way"));
            Assert.That(Encoding.ASCII.GetString(under.Bytes), Does.Not.Contain("half"));
        }

        [Test]
        public void Content_that_throws_after_whole_chunks_went_out_still_has_no_last_chunk()
        {
            using (var stream = new MemoryStream())
            {
                Assert.Throws<InvalidOperationException>(() => HttpResponse.Write(
                    stream,
                    200,
                    text =>
                    {
                        text.Write(new string('x', 3 * ChunkedContent.Most));
                        throw new InvalidOperationException("an invented failure part way");
                    },
                    false,
                    null));

                var raw = stream.ToArray();
                Assert.That(raw.Length, Is.GreaterThan(2 * ChunkedContent.Most), "whole chunks went out first");
                Assert.That(Encoding.ASCII.GetString(raw, raw.Length - 5, 5), Is.Not.EqualTo("0\r\n\r\n"));
                Assert.That(() => HttpAnswer.Decoded(raw), Throws.TypeOf<InvalidDataException>());
            }
        }

        [Test]
        public void A_peer_that_went_away_part_way_fails_the_write_and_is_sent_no_last_chunk()
        {
            // The stream underneath fails once the head and a chunk are out, as a client that
            // stopped reading does. What that throws comes out of the write for the conversation
            // to log, and nothing more is sent.
            var under = new RecordingStream();

            Assert.Throws<IOException>(() => HttpResponse.Write(
                under,
                200,
                text =>
                {
                    text.Write(new string('x', ChunkedContent.Most));
                    text.Flush();
                    under.Fails = true;
                    text.Write(new string('q', ChunkedContent.Most));
                    text.Flush();
                },
                false,
                null));

            var raw = under.Bytes;
            Assert.That(raw.Contains((byte)'x'), Is.True, "the chunk written before the peer left went out");
            Assert.That(raw.Contains((byte)'q'), Is.False, "nothing written after the peer left went out");
            Assert.That(Encoding.ASCII.GetString(raw, raw.Length - 5, 5), Is.Not.EqualTo("0\r\n\r\n"));
        }

        [Test]
        public void Text_beyond_ASCII_arrives_as_UTF_8_with_no_mark_ahead_of_it()
        {
            var text = "{\"title\":\"naïve ✓ 𝄞\"}";

            var raw = Written(200, writer => writer.Write(text), false, null);

            var expected = new UTF8Encoding(false).GetBytes(text);
            var size = expected.Length.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
            var content = Encoding.ASCII.GetBytes(size + "\r\n")
                .Concat(expected)
                .Concat(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"))
                .ToArray();

            Assert.That(raw.Skip(raw.Length - content.Length), Is.EqualTo(content),
                "a chunk is sized by its bytes, which are the text as UTF-8 and nothing ahead of it");
            Assert.That(HttpAnswer.Decoded(raw), Does.EndWith("\r\n\r\n" + text));
        }

        [TestCase("é")]
        [TestCase("✓")]
        [TestCase("𝄞")]
        public void A_character_that_falls_across_a_chunk_boundary_is_still_that_character(string character)
        {
            // A chunk is cut where its bytes run out, in the middle of a character as readily
            // as between two. The first byte of the character is the last byte of a chunk here,
            // and the rest of it opens the chunk after.
            var text = new string('a', ChunkedContent.Most - 1) + character + "z";
            var under = new RecordingStream();

            HttpResponse.Write(under, 200, writer => writer.Write(text), false, null);

            var first = under.Writes[1];
            Assert.That(first.Length, Is.EqualTo(16 * 1024), "the write after the head is a full chunk");
            Assert.That(first[first.Length - 3], Is.EqualTo(Encoding.UTF8.GetBytes(character)[0]),
                "the character has to straddle the chunks for this to test anything");

            Assert.That(Encoding.UTF8.GetString(under.Bytes), Does.Contain(((char)0xFFFD).ToString()),
                "decoded with the chunks' own lines still in it, the character is lost");
            Assert.That(HttpAnswer.Decoded(under.Bytes).EndsWith("\r\n\r\n" + text, StringComparison.Ordinal), Is.True,
                "taken out of the chunks first, the character is whole");
        }

        [Test]
        public void Decoding_the_chunks_gives_exactly_the_text_written_however_it_was_written()
        {
            // A document is written a member at a time, in pieces of every size, some of them a
            // single character. Whatever the pieces, what the chunks carry is the text, in
            // writes a record carries.
            var pieces = new[]
            {
                "{\"uuid\":\"0123456789abcdef0123456789abcdef\",\"fields\":{",
                "\"Notes\":\"",
                new string('n', ChunkedContent.Most + 17),
                "naïve ✓ 𝄞",
                new string('m', 4096),
                "\",",
                "\"Title\":\"an invented service\"",
                "}}",
            };

            var under = new RecordingStream();
            HttpResponse.Write(
                under,
                200,
                writer =>
                {
                    foreach (var piece in pieces)
                    {
                        if (piece.Length < 8)
                        {
                            foreach (var character in piece)
                            {
                                writer.Write(character);
                            }
                        }
                        else
                        {
                            writer.Write(piece);
                        }
                    }
                },
                false,
                null);

            var text = string.Concat(pieces);
            var decoded = HttpAnswer.Decoded(under.Bytes);
            var blank = decoded.IndexOf("\r\n\r\n", StringComparison.Ordinal);

            Assert.That(decoded.Substring(blank + 4) == text, Is.True, "the chunks carry other text than was written");
            Assert.That(under.Writes.Select(write => write.Length), Is.All.LessThanOrEqualTo(16 * 1024));
            Assert.That(Encoding.ASCII.GetString(under.Writes[under.Writes.Count - 1]), Is.EqualTo("0\r\n\r\n"));
        }

        [Test]
        public void An_answer_needs_a_stream_and_something_to_write_its_content()
        {
            Action<TextWriter> content = text => { };

            Assert.Throws<ArgumentNullException>(() => HttpResponse.Write(null, 200, content, false, null));

            using (var stream = new MemoryStream())
            {
                Assert.Throws<ArgumentNullException>(
                    () => HttpResponse.Write(stream, 200, (Action<TextWriter>)null, false, null));
            }
        }

        private static WebHeaderCollection Tagged()
        {
            var headers = new WebHeaderCollection();
            headers.Add("ETag", Tag);
            return headers;
        }

        private static byte[] Written(int status, Action<TextWriter> content, bool head, WebHeaderCollection headers)
        {
            using (var stream = new MemoryStream())
            {
                HttpResponse.Write(stream, status, content, head, headers);
                return stream.ToArray();
            }
        }
    }
}
