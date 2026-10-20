using System;
using System.IO;
using System.Text;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A stream that hands out a request and collects the response, so a whole conversation can be
    /// run without a socket in it.
    /// </summary>
    internal sealed class ConversationStream : Stream
    {
        private readonly MemoryStream _incoming;
        private readonly MemoryStream _outgoing = new MemoryStream();

        public ConversationStream(string request)
            : this(Encoding.ASCII.GetBytes(request))
        {
        }

        public ConversationStream(byte[] request)
        {
            _incoming = new MemoryStream(request);
        }

        /// <summary>Everything written back, as text.</summary>
        public string Response
        {
            get { return Encoding.UTF8.GetString(_outgoing.ToArray()); }
        }

        /// <summary>The status line's code, or zero when nothing was written.</summary>
        public int Status
        {
            get
            {
                var response = Response;
                if (!response.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) || response.Length < 12)
                {
                    return 0;
                }

                int status;
                return int.TryParse(response.Substring(9, 3), out status) ? status : 0;
            }
        }

        public string Body
        {
            get
            {
                var response = Response;
                var blank = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                return blank < 0 ? string.Empty : response.Substring(blank + 4);
            }
        }

        public bool Answered
        {
            get { return _outgoing.Length > 0; }
        }

        /// <summary>
        /// How much of the request was never read, which over a socket is what provokes a reset.
        /// </summary>
        public long Unread
        {
            get { return _incoming.Length - _incoming.Position; }
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
            get { return true; }
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
            return _incoming.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _outgoing.Write(buffer, offset, count);
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
    }
}
