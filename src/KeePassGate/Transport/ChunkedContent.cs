using System;
using System.IO;

namespace KeePassGate.Transport
{
    /// <summary>
    /// The content of an answer that is written as it is sent, framed as RFC 9112 section 7.1
    /// frames content whose length is not known when its head goes out: in chunks.
    /// https://www.rfc-editor.org/rfc/rfc9112#section-7.1
    /// </summary>
    /// <remarks>
    /// What is written here is gathered and sent a chunk at a time: the chunk's size in hex, a
    /// line end, that many bytes, and a line end. <see cref="End"/> sends what is still held
    /// and the chunk of no bytes that closes the content. Nothing else here sends that last
    /// chunk, and that is the point of it being a call of its own: content whose writer failed
    /// part way is left without its last chunk, so whoever reads the answer finds it cut short
    /// and takes none of it for the whole. <see cref="Abandon"/> is what such content is let
    /// go with.
    ///
    /// **No extension and no trailer**, which the grammar allows and nothing here has a use
    /// for, so a reader of these answers can refuse both.
    ///
    /// **Every chunk goes out in a single write**, its size and both line ends with it, since
    /// what is underneath turns each write into a TLS record of its own. A chunk with its size
    /// and its line ends is as much as such a record carries.
    ///
    /// **What it gathered is overwritten when it is let go.** An answer holds the values a
    /// caller asked for, so the buffer here held them, and it is the only copy of them on the
    /// way out that this plugin can overwrite: `StoredStrings` has what cannot be.
    ///
    /// Writing only, and the stream underneath is somebody else's: neither closed nor flushed
    /// beyond what was written here.
    /// </remarks>
    internal sealed class ChunkedContent : Stream
    {
        /// <summary>
        /// The most bytes of content a chunk carries: what a TLS record carries, RFC 8446
        /// section 5.1, less the chunk's own size and line ends.
        /// </summary>
        public const int Most = (16 * 1024) - Ahead - 2;

        /// <summary>
        /// Room for a chunk's size ahead of its bytes: the hex digits <see cref="Most"/> takes
        /// at most, and a line end.
        /// </summary>
        private const int Ahead = 4 + 2;

        private static readonly byte[] Last = { (byte)'0', (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

        private readonly Stream _stream;
        private readonly byte[] _chunk = new byte[Ahead + Most + 2];

        private int _held;
        private bool _ended;
        private bool _abandoned;

        public ChunkedContent(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            _stream = stream;
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return !_ended; }
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_abandoned)
            {
                return;
            }

            if (_ended)
            {
                throw new InvalidOperationException("content that was ended takes nothing more");
            }

            while (count > 0)
            {
                var taken = Math.Min(count, Most - _held);
                Buffer.BlockCopy(buffer, offset, _chunk, Ahead + _held, taken);
                _held += taken;
                offset += taken;
                count -= taken;

                if (_held == Most)
                {
                    Send();
                }
            }
        }

        /// <summary>Sends what is held, as a chunk short of <see cref="Most"/>.</summary>
        /// <remarks>
        /// Nothing held sends nothing: a chunk of no bytes is the last chunk, and only
        /// <see cref="End"/> sends that.
        /// </remarks>
        public override void Flush()
        {
            if (_abandoned)
            {
                return;
            }

            Send();
            _stream.Flush();
        }

        /// <summary>
        /// Sends what is still held and the last chunk, after which the content is whole.
        /// </summary>
        public void End()
        {
            if (_ended || _abandoned)
            {
                return;
            }

            Send();
            _ended = true;
            _stream.Write(Last, 0, Last.Length);
        }

        /// <summary>
        /// Drops what is held and whatever is written from here on, for content whose writer
        /// failed part way.
        /// </summary>
        /// <remarks>
        /// Whatever wrote into this still holds text of its own, and hands it on as it is let
        /// go. That would send more of an answer that has already failed, and where the peer
        /// is gone as well, the failure of that sending would be what is thrown, in place of
        /// what failed. Abandoned, this takes that text and sends none of it, and never sends
        /// the last chunk.
        /// </remarks>
        public void Abandon()
        {
            _abandoned = true;
            _held = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Array.Clear(_chunk, 0, _chunk.Length);
                _held = 0;
            }

            base.Dispose(disposing);
        }

        private void Send()
        {
            if (_held == 0)
            {
                return;
            }

            // The size is written backwards from where the bytes begin, so a chunk starts
            // wherever its size does and goes out as the single run of bytes it is.
            var at = Ahead;
            _chunk[--at] = (byte)'\n';
            _chunk[--at] = (byte)'\r';
            for (var left = _held; left > 0; left >>= 4)
            {
                _chunk[--at] = (byte)"0123456789abcdef"[left & 0xF];
            }

            var end = Ahead + _held;
            _chunk[end++] = (byte)'\r';
            _chunk[end++] = (byte)'\n';

            // Emptied before the write, so a write that fails is not sent again by whatever
            // lets go of this afterwards.
            _held = 0;
            _stream.Write(_chunk, at, end - at);
        }
    }
}
