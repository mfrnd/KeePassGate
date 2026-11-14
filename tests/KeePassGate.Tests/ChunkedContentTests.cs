using System;
using System.IO;
using System.Linq;
using System.Text;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The framing of an answer that is written as it is sent: what goes to the stream
    /// underneath for what is written here, a chunk at a time, and what marks the content as
    /// whole.
    /// </summary>
    /// <remarks>
    /// The last chunk is what tells a client it holds the whole answer, so what is pinned here
    /// is as much when that chunk is not sent as when it is. Everything written is invented.
    /// </remarks>
    [TestFixture]
    public sealed class ChunkedContentTests
    {
        /// <summary>What a TLS record carries, which a chunk with its size and line ends is held to.</summary>
        private const int Record = 16 * 1024;

        [Test]
        public void A_small_write_goes_out_as_its_size_in_hex_its_bytes_and_a_line_end_after_each()
        {
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                Write(chunks, "{\"invented\":true}");
                Assert.That(under.Writes, Is.Empty, "held until a chunk is full or the content is flushed or ended");

                chunks.End();
            }

            Assert.That(Text(under), Is.EqualTo("11\r\n{\"invented\":true}\r\n0\r\n\r\n"));
            Assert.That(under.Writes.Select(write => Encoding.ASCII.GetString(write)), Is.EqualTo(new[]
            {
                "11\r\n{\"invented\":true}\r\n",
                "0\r\n\r\n",
            }), "the chunk goes down in a single write, its size and both line ends with it");
        }

        [Test]
        public void A_write_larger_than_a_chunk_is_split_and_each_chunk_is_a_single_write_a_record_carries()
        {
            var under = new RecordingStream();
            var bytes = Letters((2 * ChunkedContent.Most) + 100);

            using (var chunks = new ChunkedContent(under))
            {
                chunks.Write(bytes, 0, bytes.Length);
                Assert.That(under.Writes.Count, Is.EqualTo(2), "each full chunk went down as it filled");

                chunks.End();
            }

            Assert.That(under.Writes.Count, Is.EqualTo(4), "the full chunks, what was left, and the last chunk");
            Assert.That(under.Writes.Select(write => write.Length), Is.All.LessThanOrEqualTo(Record));

            var size = ChunkedContent.Most.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\r\n";
            foreach (var full in under.Writes.Take(2))
            {
                Assert.That(full.Length, Is.EqualTo(Record), "a full chunk is exactly a record");
                Assert.That(Encoding.ASCII.GetString(full, 0, size.Length), Is.EqualTo(size));
                Assert.That(Encoding.ASCII.GetString(full, full.Length - 2, 2), Is.EqualTo("\r\n"));
            }

            Assert.That(Encoding.ASCII.GetString(under.Writes[2], 0, 4), Is.EqualTo("64\r\n"));
            Assert.That(Encoding.ASCII.GetString(under.Writes[3]), Is.EqualTo("0\r\n\r\n"));
            Assert.That(HttpAnswer.FromChunks(under.Bytes, 0), Is.EqualTo(bytes), "and the bytes are the bytes written");
        }

        [Test]
        public void Writes_that_fill_a_chunk_between_them_go_out_as_that_chunk()
        {
            // A chunk is cut where its bytes run out and not where a write ended, so many short
            // writes cost no more writes underneath than a long write of the same bytes.
            var under = new RecordingStream();
            var bytes = Letters(ChunkedContent.Most + 1);

            using (var chunks = new ChunkedContent(under))
            {
                for (var at = 0; at < bytes.Length - 2; at += 7)
                {
                    chunks.Write(bytes, at, Math.Min(7, bytes.Length - 2 - at));
                }

                Assert.That(under.Writes, Is.Empty, "a chunk short of full by a byte is still held");

                chunks.Write(bytes, bytes.Length - 2, 2);
                Assert.That(under.Writes.Count, Is.EqualTo(1));
                Assert.That(under.Writes[0].Length, Is.EqualTo(Record));

                chunks.End();
            }

            Assert.That(HttpAnswer.FromChunks(under.Bytes, 0), Is.EqualTo(bytes));
        }

        [Test]
        public void Nothing_held_sends_nothing_on_a_flush()
        {
            // A chunk of no bytes is the last chunk, so a flush with nothing to send must send
            // nothing, however often it is asked.
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                chunks.Flush();
                chunks.Flush();
                Assert.That(under.Writes, Is.Empty);

                Write(chunks, "a");
                chunks.Flush();
                chunks.Flush();
                Assert.That(Text(under), Is.EqualTo("1\r\na\r\n"), "what was held went out, and once");
            }

            Assert.That(under.Flushes, Is.GreaterThan(0), "a flush is handed on to the stream underneath");
        }

        [Test]
        public void Ending_sends_what_is_held_and_the_last_chunk_and_only_the_first_time()
        {
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                Write(chunks, "abc");

                chunks.End();
                chunks.End();
                chunks.Flush();
            }

            Assert.That(Text(under), Is.EqualTo("3\r\nabc\r\n0\r\n\r\n"));
        }

        [Test]
        public void Content_ended_with_nothing_written_is_the_last_chunk_alone()
        {
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                chunks.End();
            }

            Assert.That(Text(under), Is.EqualTo("0\r\n\r\n"));
        }

        [Test]
        public void Letting_go_without_ending_sends_no_last_chunk()
        {
            // Content whose writer failed part way is let go without its end, and what a reader
            // then holds must not read as whole: what went out went out, what was still held is
            // dropped, and the last chunk is never sent.
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                Write(chunks, "sent");
                chunks.Flush();
                Write(chunks, "held");
            }

            Assert.That(Text(under), Is.EqualTo("4\r\nsent\r\n"));
            Assert.That(
                () => HttpAnswer.FromChunks(under.Bytes, 0),
                Throws.TypeOf<InvalidDataException>(),
                "a reader finds it cut short");
        }

        [Test]
        public void Writing_after_the_end_is_refused_and_sends_nothing()
        {
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                Assert.That(chunks.CanWrite, Is.True);
                chunks.End();

                Assert.That(chunks.CanWrite, Is.False);
                Assert.Throws<InvalidOperationException>(() => Write(chunks, "more"));
                chunks.Flush();
            }

            Assert.That(Text(under), Is.EqualTo("0\r\n\r\n"), "nothing follows the last chunk");
        }

        [Test]
        public void A_chunk_whose_write_failed_is_not_sent_again()
        {
            // A peer that went away fails the write of a chunk. Whatever lets go of the content
            // afterwards flushes it, and must find nothing held to try again with.
            var under = new RecordingStream { Fails = true };
            using (var chunks = new ChunkedContent(under))
            {
                Write(chunks, "lost");
                Assert.Throws<IOException>(() => chunks.Flush());

                under.Fails = false;
                chunks.Flush();
            }

            Assert.That(under.Writes, Is.Empty);
        }

        [Test]
        public void It_writes_only_and_needs_a_stream_to_write_to()
        {
            Assert.Throws<ArgumentNullException>(() => new ChunkedContent(null));

            using (var chunks = new ChunkedContent(new RecordingStream()))
            {
                Assert.That(chunks.CanRead, Is.False);
                Assert.That(chunks.CanSeek, Is.False);
                Assert.Throws<NotSupportedException>(() => chunks.ReadByte());
                Assert.Throws<ArgumentNullException>(() => chunks.Write(null, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => chunks.Write(new byte[2], 1, 2));
                Assert.Throws<ArgumentOutOfRangeException>(() => chunks.Write(new byte[2], -1, 1));
            }
        }

        private static void Write(Stream chunks, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            chunks.Write(bytes, 0, bytes.Length);
        }

        private static string Text(RecordingStream under)
        {
            return Encoding.ASCII.GetString(under.Bytes);
        }

        /// <summary>This many bytes that differ from place to place, so bytes out of order show.</summary>
        private static byte[] Letters(int count)
        {
            var bytes = new byte[count];
            for (var index = 0; index < count; index++)
            {
                bytes[index] = (byte)('a' + (index % 23));
            }

            return bytes;
        }

        [Test]
        public void Abandoned_content_drops_what_it_holds_and_whatever_is_written_after()
        {
            // For content whose writer failed part way: nothing of it is sent after the failure,
            // the last chunk least of all, and nothing here touches the stream again.
            var under = new RecordingStream();
            using (var chunks = new ChunkedContent(under))
            {
                var held = Encoding.ASCII.GetBytes("held");
                chunks.Write(held, 0, held.Length);

                chunks.Abandon();
                chunks.Write(held, 0, held.Length);
                chunks.Flush();
                chunks.End();
            }

            Assert.That(under.Bytes, Is.Empty);
            Assert.That(under.Flushes, Is.Zero);
        }
    }
}
