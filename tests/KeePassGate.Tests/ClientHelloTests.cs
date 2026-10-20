using System;
using System.IO;
using System.Linq;
using System.Text;
using KeePassGate.Gate;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The parser that faces unauthenticated bytes. Almost every test here is about refusing
    /// something, because that is what this code is for: it must be total, and it must never be
    /// the reason a password manager falls over.
    /// </summary>
    [TestFixture]
    public sealed class ClientHelloTests
    {
        private const string Name = "abc123.def456.kpg";

        [Test]
        public void The_name_comes_back_out_of_a_well_formed_hello()
        {
            var hello = ClientHelloBuilder.WithName(Name);

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.EqualTo(Name));
        }

        [Test]
        public void It_reads_past_other_extensions_to_find_it()
        {
            var builder = new ClientHelloBuilder();
            builder.AddExtension(0x002b, new byte[] { 0x02, 0x03, 0x04 });   // supported_versions
            builder.AddServerName(Name);
            builder.AddExtension(0x000a, new byte[] { 0x00, 0x02, 0x00, 0x17 });
            var hello = builder.Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.EqualTo(Name));
        }

        [Test]
        public void A_hello_with_no_server_name_has_no_name()
        {
            var hello = new ClientHelloBuilder().AddExtension(0x002b, new byte[] { 0x02, 0x03, 0x04 }).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void A_hello_with_no_extensions_at_all_has_no_name()
        {
            var hello = new ClientHelloBuilder().Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void Anything_that_is_not_a_handshake_record_is_refused()
        {
            var hello = new ClientHelloBuilder { ContentType = 0x17 }.AddServerName(Name).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void Anything_that_is_not_a_client_hello_is_refused()
        {
            var hello = new ClientHelloBuilder { MessageType = 0x02 }.AddServerName(Name).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void A_record_length_that_claims_more_than_is_there_is_refused()
        {
            var builder = new ClientHelloBuilder();
            builder.AddServerName(Name);
            builder.RecordLengthOverride = 4096;

            var hello = builder.Build();
            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void A_message_length_that_claims_more_than_is_there_is_refused()
        {
            var builder = new ClientHelloBuilder();
            builder.AddServerName(Name);
            builder.MessageLengthOverride = 4096;

            var hello = builder.Build();
            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void Every_truncation_of_a_valid_hello_is_refused_and_none_of_them_throws()
        {
            // The cheapest way to be sure a parser is total: give it every prefix of something
            // real, one byte at a time.
            var hello = ClientHelloBuilder.WithName(Name);

            for (var length = 0; length < hello.Length; length++)
            {
                var truncated = hello.Take(length).ToArray();

                string name = null;
                Assert.DoesNotThrow(() => name = ClientHello.ReadServerName(truncated, 0, truncated.Length),
                    "threw on a " + length + " byte prefix");
                Assert.That(name, Is.Null, "read a name out of a " + length + " byte prefix");
            }
        }

        [Test]
        public void Every_single_byte_corruption_is_either_refused_or_read_without_throwing()
        {
            var hello = ClientHelloBuilder.WithName(Name);

            for (var i = 0; i < hello.Length; i++)
            {
                var corrupted = (byte[])hello.Clone();
                corrupted[i] ^= 0xFF;

                Assert.DoesNotThrow(() => ClientHello.ReadServerName(corrupted, 0, corrupted.Length),
                    "threw on a hello with byte " + i + " flipped");
            }
        }

        [Test]
        public void A_name_carrying_bytes_a_host_name_cannot_have_is_refused()
        {
            // Nothing downstream, including a log line, should ever be handed arbitrary bytes.
            var hostile = Encoding.ASCII.GetBytes("evil\r\nname.kpg");
            var hello = new ClientHelloBuilder().AddServerNameBytes(hostile, 0x00).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void A_name_with_a_null_byte_in_it_is_refused()
        {
            var hostile = Encoding.ASCII.GetBytes("abc\0def.kpg");
            var hello = new ClientHelloBuilder().AddServerNameBytes(hostile, 0x00).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void An_empty_name_is_refused()
        {
            var hello = new ClientHelloBuilder().AddServerNameBytes(Array.Empty<byte>(), 0x00).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void A_name_longer_than_a_host_name_can_be_is_refused()
        {
            var tooLong = Encoding.ASCII.GetBytes(new string('a', ClientHello.MaxNameLength + 1));
            var hello = new ClientHelloBuilder().AddServerNameBytes(tooLong, 0x00).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void An_entry_that_is_not_a_host_name_is_not_read_as_one()
        {
            var hello = new ClientHelloBuilder().AddServerNameBytes(Encoding.ASCII.GetBytes(Name), 0x02).Build();

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length), Is.Null);
        }

        [Test]
        public void Nothing_at_all_is_refused()
        {
            Assert.That(ClientHello.ReadServerName(null, 0, 0), Is.Null);
            Assert.That(ClientHello.ReadServerName(Array.Empty<byte>(), 0, 0), Is.Null);
        }

        [Test]
        public void A_range_outside_the_buffer_is_refused_rather_than_read()
        {
            var hello = ClientHelloBuilder.WithName(Name);

            Assert.That(ClientHello.ReadServerName(hello, 0, hello.Length + 1), Is.Null);
            Assert.That(ClientHello.ReadServerName(hello, -1, 4), Is.Null);
            Assert.That(ClientHello.ReadServerName(hello, 4, -1), Is.Null);
        }
    }

    [TestFixture]
    public sealed class ClientHelloReaderTests
    {
        private const string Name = "abc123.def456.kpg";

        [Test]
        public void It_reads_one_record_and_keeps_every_byte_of_it()
        {
            var hello = ClientHelloBuilder.WithName(Name);
            var stream = new MemoryStream(hello.Concat(new byte[] { 0xFF, 0xFE }).ToArray());

            var peek = ClientHelloReader.Read(stream);

            Assert.That(peek, Is.Not.Null);
            Assert.That(peek.ServerName, Is.EqualTo(Name));
            Assert.That(peek.Bytes, Is.EqualTo(hello), "the bytes have to be replayable into the handshake");
            Assert.That(stream.Position, Is.EqualTo(hello.Length), "and nothing beyond the record is taken");
        }

        [Test]
        public void A_record_that_stops_early_is_refused()
        {
            var hello = ClientHelloBuilder.WithName(Name);
            var stream = new MemoryStream(hello.Take(hello.Length - 5).ToArray());

            Assert.That(ClientHelloReader.Read(stream), Is.Null);
        }

        [Test]
        public void A_connection_that_says_nothing_is_refused()
        {
            Assert.That(ClientHelloReader.Read(new MemoryStream(Array.Empty<byte>())), Is.Null);
        }

        [Test]
        public void Something_that_is_not_TLS_is_refused_after_five_bytes()
        {
            var stream = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n"));

            Assert.That(ClientHelloReader.Read(stream), Is.Null);
            Assert.That(stream.Position, Is.EqualTo(ClientHelloReader.HeaderLength),
                "A connection that is not TLS costs five bytes and no more.");
        }

        [Test]
        public void A_record_claiming_to_be_larger_than_TLS_allows_is_refused_before_allocating()
        {
            var header = new byte[] { 0x16, 0x03, 0x01, 0xFF, 0xFF };
            var stream = new MemoryStream(header);

            Assert.That(ClientHelloReader.Read(stream), Is.Null);
        }

        [Test]
        public void A_record_of_no_length_is_refused()
        {
            var stream = new MemoryStream(new byte[] { 0x16, 0x03, 0x01, 0x00, 0x00 });

            Assert.That(ClientHelloReader.Read(stream), Is.Null);
        }

        [Test]
        public void A_valid_record_with_no_name_still_comes_back_with_its_bytes()
        {
            var hello = new ClientHelloBuilder().Build();

            var peek = ClientHelloReader.Read(new MemoryStream(hello));

            Assert.That(peek, Is.Not.Null);
            Assert.That(peek.HasName, Is.False);
            Assert.That(peek.Bytes, Is.EqualTo(hello));
        }

        [Test]
        public void A_stream_that_dribbles_is_read_to_the_end_of_the_record()
        {
            var hello = ClientHelloBuilder.WithName(Name);

            var peek = ClientHelloReader.Read(new DribblingStream(hello));

            Assert.That(peek, Is.Not.Null);
            Assert.That(peek.ServerName, Is.EqualTo(Name));
        }

        /// <summary>Hands back one byte at a time, as a socket under load may.</summary>
        private sealed class DribblingStream : Stream
        {
            private readonly byte[] _bytes;
            private int _position;

            public DribblingStream(byte[] bytes)
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
                if (_position >= _bytes.Length || count == 0)
                {
                    return 0;
                }

                buffer[offset] = _bytes[_position++];
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
