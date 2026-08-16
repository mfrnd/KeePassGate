using System;
using System.IO;

namespace KeePassGate.Gate
{
    /// <summary>What was read off a socket before any TLS work, and what it said.</summary>
    public sealed class ClientHelloPeek
    {
        private readonly byte[] _bytes;
        private readonly string _serverName;

        public ClientHelloPeek(byte[] bytes, string serverName)
        {
            _bytes = bytes ?? Array.Empty<byte>();
            _serverName = serverName;
        }

        /// <summary>Everything consumed, which has to be replayed into the TLS stream.</summary>
        public byte[] Bytes
        {
            get { return _bytes; }
        }

        /// <summary>The name, or null when there was none to read.</summary>
        public string ServerName
        {
            get { return _serverName; }
        }

        public bool HasName
        {
            get { return !string.IsNullOrEmpty(_serverName); }
        }
    }

    /// <summary>
    /// Takes the first TLS record off a connection and reads the name out of it, keeping every byte
    /// so the handshake can still happen.
    /// </summary>
    /// <remarks>
    /// Bounded twice over: a record header is five bytes and a record body is capped at what TLS
    /// itself allows, so a client cannot make this allocate or wait for more than one record's
    /// worth. Anything that does not arrive, or does not parse, ends the connection with no
    /// response at all.
    /// </remarks>
    public static class ClientHelloReader
    {
        public const int HeaderLength = 5;

        /// <summary>The largest TLS record, so the largest this will ever hold.</summary>
        public const int MaxRecordLength = 16384;

        /// <summary>
        /// Reads one record. Returns null when the connection ends first, or when what arrives is
        /// not a TLS handshake record at all.
        /// </summary>
        public static ClientHelloPeek Read(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var header = new byte[HeaderLength];
            if (!ReadFully(stream, header, 0, header.Length))
            {
                return null;
            }

            if (header[0] != ClientHello.HandshakeContentType)
            {
                return null;
            }

            var bodyLength = (header[3] << 8) | header[4];
            if (bodyLength <= 0 || bodyLength > MaxRecordLength)
            {
                return null;
            }

            var record = new byte[HeaderLength + bodyLength];
            Buffer.BlockCopy(header, 0, record, 0, HeaderLength);
            if (!ReadFully(stream, record, HeaderLength, bodyLength))
            {
                return null;
            }

            return new ClientHelloPeek(record, ClientHello.ReadServerName(record, 0, record.Length));
        }

        private static bool ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (read < count)
            {
                int got;
                try
                {
                    got = stream.Read(buffer, offset + read, count - read);
                }
                catch (IOException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }

                if (got <= 0)
                {
                    return false;
                }

                read += got;
            }

            return true;
        }
    }
}
