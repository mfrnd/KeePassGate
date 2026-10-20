using System;
using System.IO;

namespace KeePassGate.Gate
{
    /// <summary>
    /// A stream that hands back some bytes already read before continuing with the real one.
    /// </summary>
    /// <remarks>
    /// This is what makes peeking at the ClientHello possible: the bytes taken off the socket to
    /// choose a certificate are replayed into the TLS server, which then sees the handshake it
    /// expects from its first byte.
    /// </remarks>
    public sealed class PrependingStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly Stream _inner;
        private int _position;

        public PrependingStream(byte[] prefix, Stream inner)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            _prefix = prefix ?? Array.Empty<byte>();
            _inner = inner;
        }

        public override bool CanRead
        {
            // The prefix is readable whatever the socket says, and after it the answer is the
            // socket's. Answering a constant made this claim readable when the inner stream had
            // stopped being.
            get { return _position < _prefix.Length || _inner.CanRead; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return _inner.CanWrite; }
        }

        /// <summary>
        /// Forwarded, like the timeouts below. A wrapper that answered for itself would report a
        /// socket as having no timeouts, and a caller that sets one on the stream rather than on
        /// the socket would get an exception instead of the timeout it asked for.
        /// </summary>
        public override bool CanTimeout
        {
            get { return _inner.CanTimeout; }
        }

        public override int ReadTimeout
        {
            get { return _inner.ReadTimeout; }
            set { _inner.ReadTimeout = value; }
        }

        public override int WriteTimeout
        {
            get { return _inner.WriteTimeout; }
            set { _inner.WriteTimeout = value; }
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
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0)
            {
                return 0;
            }

            var left = _prefix.Length - _position;
            if (left > 0)
            {
                // Never mixes the two in one read: the caller asks again, and simple beats clever
                // in the code that feeds a TLS implementation.
                var take = Math.Min(left, count);
                Buffer.BlockCopy(_prefix, _position, buffer, offset, take);
                _position += take;
                return take;
            }

            return _inner.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override void Flush()
        {
            _inner.Flush();
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
            // The inner stream is not this one's to close. It belongs to the connection, which
            // outlives the replay and closes it itself; there was a switch for the other case and
            // nothing ever passed it.
            base.Dispose(disposing);
        }
    }
}
