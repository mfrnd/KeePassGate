using System;
using System.IO;
using System.Linq;
using System.Text;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The reader both test clients take an answer with, which is test code and is therefore the
    /// one place a defect makes a passing test mean nothing.
    /// </summary>
    [TestFixture]
    public sealed class HttpAnswerTests
    {
        [Test]
        public void A_character_split_across_two_reads_is_still_one_character()
        {
            // The defect this pins: decoding each read on its own turns a sequence split across a
            // boundary into a replacement character on each side, so an answer that arrived intact
            // reads as one that did not, and the test that compares it with what was stored fails
            // naming the database rather than its own reader.
            var body = new string('a', HttpAnswer.ChunkBytes - 1) + "é" + "z";
            var bytes = Encoding.UTF8.GetBytes(body);

            Assert.That(bytes.Length, Is.EqualTo(HttpAnswer.ChunkBytes + 2),
                "the two bytes of the character have to straddle the boundary for this to test anything");

            using (var stream = new MemoryStream(bytes))
            {
                Assert.That(HttpAnswer.ReadToEnd(stream), Is.EqualTo(body));
            }
        }

        [Test]
        public void Everything_is_read_however_little_arrives_at_a_time()
        {
            // A socket gives back what it has, not what was asked for. The head and the body of an
            // answer reach this in whatever pieces the network chose.
            var body = "{\"title\":\"naïve ✓\"}";

            Assert.That(
                HttpAnswer.ReadToEnd(new DripStream(Encoding.UTF8.GetBytes(body))),
                Is.EqualTo(body));
        }

        [Test]
        public void A_stream_that_says_nothing_reads_as_nothing()
        {
            using (var stream = new MemoryStream(Array.Empty<byte>()))
            {
                Assert.That(HttpAnswer.ReadToEnd(stream), Is.Empty);
            }
        }

        [Test]
        public void An_answer_that_arrived_in_chunks_reads_as_its_head_and_its_content_put_together()
        {
            // The gate writes the answer to a read of an entry or of a field in chunks. A test
            // comparing a body with what it stored has to be handed the body, and none of the
            // lines the chunks were framed with.
            var raw = Chunked("7\r\n{\"a\":\"b\r\n3\r\n\"}\n\r\n0\r\n\r\n");

            using (var stream = new MemoryStream(raw))
            {
                Assert.That(HttpAnswer.ReadToEnd(stream), Is.EqualTo(Head + "\r\n\r\n{\"a\":\"b\"}\n"));
            }
        }

        [Test]
        public void A_character_split_across_chunks_is_still_a_single_character()
        {
            // A chunk ends where its bytes ran out. Decoded before the chunks are put together,
            // each byte of this character would be a replacement character.
            var bytes = Encoding.UTF8.GetBytes("é");
            Assume.That(bytes.Length, Is.EqualTo(2));

            var raw = Encoding.ASCII.GetBytes(Head + "\r\n\r\n2\r\n-")
                .Concat(new[] { bytes[0] })
                .Concat(Encoding.ASCII.GetBytes("\r\n2\r\n"))
                .Concat(new[] { bytes[1] })
                .Concat(Encoding.ASCII.GetBytes("-\r\n0\r\n\r\n"))
                .ToArray();

            Assert.That(HttpAnswer.Decoded(raw), Is.EqualTo(Head + "\r\n\r\n-é-"));
        }

        [Test]
        public void An_answer_that_names_its_length_is_read_as_it_arrived()
        {
            // What looks like a chunk in a body that was not sent in chunks is the body.
            var raw = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 12\r\n\r\n3\r\nabc\r\n0\r\n\r\n");

            Assert.That(HttpAnswer.Decoded(raw), Is.EqualTo(Encoding.ASCII.GetString(raw)));
        }

        [TestCase("", TestName = "Content with no chunk at all is refused")]
        [TestCase("3\r\nabc\r\n", TestName = "Content cut short of its last chunk is refused")]
        [TestCase("3\r\nab", TestName = "A chunk cut short of its bytes is refused")]
        [TestCase("3\r\nabcd\r\n0\r\n\r\n", TestName = "A chunk longer than its size is refused")]
        [TestCase("3\r\nabc0\r\n\r\n", TestName = "A chunk with no line end after its bytes is refused")]
        [TestCase("3\nabc\n0\n\n", TestName = "Chunks ended by a bare line feed are refused")]
        [TestCase("three\r\nabc\r\n0\r\n\r\n", TestName = "A size that is not hex is refused")]
        [TestCase("\r\nabc\r\n0\r\n\r\n", TestName = "A chunk with no size is refused")]
        [TestCase("3;name=value\r\nabc\r\n0\r\n\r\n", TestName = "A chunk carrying an extension is refused")]
        [TestCase("3\r\nabc\r\n0\r\nTrailer: x\r\n\r\n", TestName = "A trailer after the last chunk is refused")]
        [TestCase("3\r\nabc\r\n0\r\n\r\nmore", TestName = "Anything after the last chunk is refused")]
        [TestCase("3\r\nabc\r\n0\r\n", TestName = "A last chunk with no blank line after it is refused")]
        public void Chunked_content_that_is_not_exactly_chunks_fails_whoever_reads_it(string content)
        {
            // Strict on purpose: content cut short is an answer the gate did not finish, and a
            // reader that took what there was would pass a test on half a document.
            Assert.That(
                () => HttpAnswer.Decoded(Chunked(content)),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void A_transfer_coding_other_than_chunked_fails_whoever_reads_it()
        {
            var raw = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: gzip, chunked\r\n\r\n0\r\n\r\n");

            Assert.That(() => HttpAnswer.Decoded(raw), Throws.TypeOf<InvalidDataException>());
        }

        /// <summary>The head of an answer whose content goes out in chunks.</summary>
        private const string Head =
            "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nTransfer-Encoding: chunked";

        /// <summary>That head, the blank line after it, and this content as it is.</summary>
        private static byte[] Chunked(string content)
        {
            return Encoding.ASCII.GetBytes(Head + "\r\n\r\n" + content);
        }

        /// <summary>A stream that hands over one byte per read, as a slow socket does.</summary>
        private sealed class DripStream : Stream
        {
            private readonly byte[] _bytes;
            private int _at;

            public DripStream(byte[] bytes)
            {
                _bytes = bytes;
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_at >= _bytes.Length || count <= 0)
                {
                    return 0;
                }

                buffer[offset] = _bytes[_at++];
                return 1;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
