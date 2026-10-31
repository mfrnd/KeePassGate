using System;

namespace KeePassGate.Gate
{
    /// <summary>
    /// Reads the server name out of a TLS ClientHello.
    /// </summary>
    /// <remarks>
    /// **This is the first code in the plugin to touch unauthenticated bytes**, and the only code
    /// that touches them before TLS. So it is total: every path returns a name or no name, and none
    /// of them throws. Every read is bounds checked through a cursor rather than by arithmetic at
    /// the call site, because that is the mistake this kind of parser makes.
    ///
    /// It exists at all because .NET Framework exposes no server-side SNI callback, and the server
    /// has to choose a host certificate before it has seen anything from the client but this.
    ///
    /// A ClientHello split across several TLS records is not reassembled. It is allowed by the
    /// specification and not sent by anything in practice, and refusing to guess is cheaper than
    /// carrying the state machine that would.
    ///
    /// The wire format is TLS 1.3, RFC 8446: the record layer in section 5.1 and the ClientHello
    /// in section 4.1.2. https://www.rfc-editor.org/rfc/rfc8446#section-4.1.2
    /// The `server_name` extension it digs out is RFC 6066 section 3.
    /// https://www.rfc-editor.org/rfc/rfc6066#section-3
    /// </remarks>
    public static class ClientHello
    {
        /// <summary>
        /// `ContentType.handshake`, the first byte of every record that carries one. RFC 8446
        /// section 5.1: a record that starts with anything else is not a handshake and this gives
        /// up on it after that byte.
        /// </summary>
        public const byte HandshakeContentType = 0x16;

        /// <summary>`HandshakeType.client_hello`. RFC 8446 section 4.</summary>
        public const byte ClientHelloMessageType = 0x01;

        /// <summary>
        /// `ExtensionType.server_name`, which is extension number zero and the only one this reads.
        /// RFC 6066 section 3.
        /// </summary>
        public const int ServerNameExtension = 0x0000;

        /// <summary>
        /// `NameType.host_name`, the only name type ever defined for `server_name`. RFC 6066
        /// section 3, where the list has exactly one entry.
        /// </summary>
        public const byte HostNameType = 0x00;

        /// <summary>
        /// The longest a host name may be here. RFC 1035 section 2.3.4 gives 255 octets for a name
        /// and 63 for a label, so this is the standard's own ceiling rather than a generous one: a
        /// bound to stop a hostile length driving an allocation, not a check on what a name may
        /// contain.
        /// https://www.rfc-editor.org/rfc/rfc1035#section-2.3.4
        /// </summary>
        public const int MaxNameLength = 255;

        /// <summary>
        /// The `server_name` a ClientHello carries, or null when there is none, when the bytes are
        /// not a ClientHello, or when anything at all does not add up.
        /// </summary>
        public static string ReadServerName(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                return null;
            }

            var record = new Cursor(buffer, offset, count);

            byte contentType;
            if (!record.TryByte(out contentType) || contentType != HandshakeContentType)
            {
                return null;
            }

            if (!record.TrySkip(2))
            {
                return null;
            }

            int recordLength;
            if (!record.TryUInt16(out recordLength))
            {
                return null;
            }

            Cursor handshake;
            if (!record.TrySlice(recordLength, out handshake))
            {
                return null;
            }

            byte messageType;
            if (!handshake.TryByte(out messageType) || messageType != ClientHelloMessageType)
            {
                return null;
            }

            int messageLength;
            if (!handshake.TryUInt24(out messageLength))
            {
                return null;
            }

            Cursor hello;
            if (!handshake.TrySlice(messageLength, out hello))
            {
                return null;
            }

            // legacy_version and random
            if (!hello.TrySkip(2 + 32))
            {
                return null;
            }

            if (!SkipVector8(ref hello) ||      // legacy_session_id
                !SkipVector16(ref hello) ||     // cipher_suites
                !SkipVector8(ref hello))        // legacy_compression_methods
            {
                return null;
            }

            int extensionsLength;
            if (!hello.TryUInt16(out extensionsLength))
            {
                // A ClientHello with no extensions is legal and carries no name.
                return null;
            }

            Cursor extensions;
            if (!hello.TrySlice(extensionsLength, out extensions))
            {
                return null;
            }

            while (extensions.Remaining > 0)
            {
                int extensionType;
                int extensionLength;
                if (!extensions.TryUInt16(out extensionType) || !extensions.TryUInt16(out extensionLength))
                {
                    return null;
                }

                Cursor extension;
                if (!extensions.TrySlice(extensionLength, out extension))
                {
                    return null;
                }

                if (extensionType == ServerNameExtension)
                {
                    return ReadHostName(extension);
                }
            }

            return null;
        }

        private static string ReadHostName(Cursor extension)
        {
            int listLength;
            if (!extension.TryUInt16(out listLength))
            {
                return null;
            }

            Cursor list;
            if (!extension.TrySlice(listLength, out list))
            {
                return null;
            }

            while (list.Remaining > 0)
            {
                byte nameType;
                int nameLength;
                if (!list.TryByte(out nameType) || !list.TryUInt16(out nameLength))
                {
                    return null;
                }

                byte[] name;
                if (!list.TryTake(nameLength, out name))
                {
                    return null;
                }

                if (nameType == HostNameType)
                {
                    return AsHostName(name);
                }
            }

            return null;
        }

        /// <summary>
        /// Turns the bytes into a name, or refuses. Nothing but the characters a host name is made
        /// of gets through, so no part of this plugin, including a log line, ever handles somebody
        /// else's arbitrary bytes as text.
        /// </summary>
        private static string AsHostName(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > MaxNameLength)
            {
                return null;
            }

            var name = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                var c = (char)bytes[i];
                var allowed = Ascii.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_';

                if (!allowed)
                {
                    return null;
                }

                name[i] = c;
            }

            return new string(name);
        }

        private static bool SkipVector8(ref Cursor cursor)
        {
            byte length;
            return cursor.TryByte(out length) && cursor.TrySkip(length);
        }

        private static bool SkipVector16(ref Cursor cursor)
        {
            int length;
            return cursor.TryUInt16(out length) && cursor.TrySkip(length);
        }

        /// <summary>
        /// A position in a buffer that refuses to go past its end. Every read in the parser goes
        /// through this, which is what makes the parser total rather than careful.
        /// </summary>
        private struct Cursor
        {
            private readonly byte[] _buffer;
            private int _position;
            private readonly int _end;

            public Cursor(byte[] buffer, int offset, int count)
            {
                _buffer = buffer;
                _position = offset;
                _end = offset + count;
            }

            public int Remaining
            {
                get { return _end - _position; }
            }

            public bool TryByte(out byte value)
            {
                value = 0;
                if (Remaining < 1)
                {
                    return false;
                }

                value = _buffer[_position++];
                return true;
            }

            public bool TryUInt16(out int value)
            {
                value = 0;
                if (Remaining < 2)
                {
                    return false;
                }

                value = (_buffer[_position] << 8) | _buffer[_position + 1];
                _position += 2;
                return true;
            }

            public bool TryUInt24(out int value)
            {
                value = 0;
                if (Remaining < 3)
                {
                    return false;
                }

                value = (_buffer[_position] << 16) | (_buffer[_position + 1] << 8) | _buffer[_position + 2];
                _position += 3;
                return true;
            }

            public bool TrySkip(int count)
            {
                if (count < 0 || Remaining < count)
                {
                    return false;
                }

                _position += count;
                return true;
            }

            public bool TryTake(int count, out byte[] value)
            {
                value = null;
                if (count < 0 || Remaining < count)
                {
                    return false;
                }

                value = new byte[count];
                Buffer.BlockCopy(_buffer, _position, value, 0, count);
                _position += count;
                return true;
            }

            public bool TrySlice(int count, out Cursor slice)
            {
                slice = default(Cursor);
                if (count < 0 || Remaining < count)
                {
                    return false;
                }

                slice = new Cursor(_buffer, _position, count);
                _position += count;
                return true;
            }
        }
    }
}
