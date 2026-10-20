using System;
using System.Collections.Generic;
using System.Text;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Builds TLS ClientHello records by hand, so the parser can be shown bytes a real client
    /// would never send.
    /// </summary>
    /// <remarks>
    /// The valid case is also captured from a real handshake elsewhere in the suite. This exists
    /// for everything else: truncations, lengths that lie, and names that are not names.
    /// </remarks>
    internal sealed class ClientHelloBuilder
    {
        private readonly List<byte> _extensions = new List<byte>();

        public byte ContentType { get; set; }

        public byte MessageType { get; set; }

        public int? RecordLengthOverride { get; set; }

        public int? MessageLengthOverride { get; set; }

        public ClientHelloBuilder()
        {
            ContentType = 0x16;
            MessageType = 0x01;
        }

        public static byte[] WithName(string name)
        {
            var builder = new ClientHelloBuilder();
            builder.AddServerName(name);
            return builder.Build();
        }

        public ClientHelloBuilder AddServerName(string name)
        {
            var bytes = Encoding.ASCII.GetBytes(name);
            return AddServerNameBytes(bytes, 0x00);
        }

        public ClientHelloBuilder AddServerNameBytes(byte[] name, byte nameType)
        {
            var entry = new List<byte> { nameType };
            entry.AddRange(UInt16((ushort)name.Length));
            entry.AddRange(name);

            var list = new List<byte>();
            list.AddRange(UInt16((ushort)entry.Count));
            list.AddRange(entry);

            return AddExtension(0x0000, list.ToArray());
        }

        public ClientHelloBuilder AddExtension(int type, byte[] data)
        {
            _extensions.AddRange(UInt16((ushort)type));
            _extensions.AddRange(UInt16((ushort)data.Length));
            _extensions.AddRange(data);
            return this;
        }

        public byte[] Build()
        {
            var hello = new List<byte>();
            hello.AddRange(new byte[] { 0x03, 0x03 });          // legacy_version
            hello.AddRange(new byte[32]);                        // random
            hello.Add(0x00);                                     // legacy_session_id, empty
            hello.AddRange(UInt16(2));                           // cipher_suites
            hello.AddRange(new byte[] { 0x13, 0x01 });
            hello.Add(0x01);                                     // legacy_compression_methods
            hello.Add(0x00);

            if (_extensions.Count > 0)
            {
                hello.AddRange(UInt16((ushort)_extensions.Count));
                hello.AddRange(_extensions);
            }

            var handshake = new List<byte> { MessageType };
            handshake.AddRange(UInt24(MessageLengthOverride ?? hello.Count));
            handshake.AddRange(hello);

            var record = new List<byte> { ContentType, 0x03, 0x01 };
            record.AddRange(UInt16((ushort)(RecordLengthOverride ?? handshake.Count)));
            record.AddRange(handshake);

            return record.ToArray();
        }

        private static byte[] UInt16(ushort value)
        {
            return new[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }

        private static byte[] UInt24(int value)
        {
            return new[] { (byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) };
        }
    }
}
