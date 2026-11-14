using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A stream that keeps every write it is handed as a write of its own, so a test can tell
    /// how what was written was cut up on the way down, and can make a chosen write fail.
    /// </summary>
    /// <remarks>
    /// What is underneath the gate's answers turns each write into a TLS record, so how many
    /// writes a chunk takes and how long each of them is are things a test has to see, and a
    /// `MemoryStream` runs them together.
    /// </remarks>
    internal sealed class RecordingStream : Stream
    {
        private readonly List<byte[]> _writes = new List<byte[]>();

        /// <summary>Every write so far, in order, each as the bytes it was handed.</summary>
        public IReadOnlyList<byte[]> Writes
        {
            get { return _writes; }
        }

        /// <summary>Everything written so far, run together.</summary>
        public byte[] Bytes
        {
            get { return _writes.SelectMany(write => write).ToArray(); }
        }

        /// <summary>How many times it was asked to flush.</summary>
        public int Flushes { get; private set; }

        /// <summary>
        /// Set to make every write from now on fail as a peer that went away does, with nothing
        /// of the write kept.
        /// </summary>
        public bool Fails { get; set; }

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

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Fails)
            {
                throw new IOException("an invented peer that went away");
            }

            var write = new byte[count];
            Buffer.BlockCopy(buffer, offset, write, 0, count);
            _writes.Add(write);
        }

        public override void Flush()
        {
            Flushes++;
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
    }
}
