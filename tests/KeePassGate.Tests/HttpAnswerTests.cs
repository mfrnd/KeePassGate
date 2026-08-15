using System;
using System.IO;
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

            Assert.That(HttpAnswer.ReadToEnd(new MemoryStream(bytes)), Is.EqualTo(body));
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
            Assert.That(HttpAnswer.ReadToEnd(new MemoryStream(Array.Empty<byte>())), Is.Empty);
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
