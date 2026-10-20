using System;
using System.Security.Cryptography;

namespace KeePassGate.Gate
{
    /// <summary>
    /// The name a client puts in its ClientHello, which is how a connection says which database it
    /// is for without naming it.
    /// </summary>
    /// <remarks>
    /// <c>hex(nonce) "." hex(HMAC-SHA256(gate key, nonce)[0..16]) ".kpg"</c>, rebuilt for every
    /// connection. Two labels because 64 hex characters exceed the 63-octet limit on one.
    ///
    /// What this buys, from `CA.md` §216: a passive observer sees a different name every time,
    /// and under TLS 1.3 the certificate messages are encrypted, so the only cleartext identifier
    /// on the wire now carries nothing. And a name that does not verify is dropped before any TLS
    /// work, so an attacker who cannot compute a tag never reaches the handshake or a dialog.
    ///
    /// Two honest limits, also from there. The tag is replayable, because the nonce is the client's
    /// and the server keeps no record of which have been used, so this stops blind probing rather
    /// than somebody who captured a connection. And the name travels in the clear, so an observer
    /// learns that some database is being addressed.
    /// </remarks>
    public static class SniName
    {
        /// <summary>
        /// The last label. Not a real top level domain and never resolved: nothing looks this name
        /// up, because the client is told where to connect and the name only says which database
        /// it means once it is there.
        /// </summary>
        public const string Suffix = "kpg";

        /// <summary>
        /// Sixteen bytes, so a label is 32 hex characters and fits the 63 octet limit a DNS label
        /// has. RFC 1035 section 2.3.4. https://www.rfc-editor.org/rfc/rfc1035#section-2.3.4
        /// </summary>
        public const int NonceLength = 16;

        /// <summary>
        /// The first sixteen bytes of the HMAC, for the same reason: all thirty-two would be 64 hex
        /// characters and exceed a label. Truncating an HMAC is what RFC 2104 section 5 describes,
        /// and 128 bits is far more than a value an attacker gets one guess at needs.
        /// https://www.rfc-editor.org/rfc/rfc2104#section-5
        /// </summary>
        public const int TagLength = 16;

        /// <summary>A fresh name for one connection.</summary>
        public static string For(byte[] gateKey)
        {
            var nonce = new byte[NonceLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            return For(gateKey, nonce);
        }

        /// <summary>The name for a nonce that has already been chosen, which is what tests use.</summary>
        public static string For(byte[] gateKey, byte[] nonce)
        {
            if (gateKey == null)
            {
                throw new ArgumentNullException(nameof(gateKey));
            }

            if (nonce == null)
            {
                throw new ArgumentNullException(nameof(nonce));
            }

            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException("A nonce is " + NonceLength + " bytes.", nameof(nonce));
            }

            return KeePassGate.Hex.Of(nonce) + "." + KeePassGate.Hex.Of(Tag(gateKey, nonce)) + "." + Suffix;
        }

        /// <summary>The tag for a nonce under a gate key, truncated as the name requires.</summary>
        public static byte[] Tag(byte[] gateKey, byte[] nonce)
        {
            using (var hmac = new HMACSHA256(gateKey))
            {
                var full = hmac.ComputeHash(nonce);
                var tag = new byte[TagLength];
                Buffer.BlockCopy(full, 0, tag, 0, TagLength);
                return tag;
            }
        }

        /// <summary>
        /// Splits a name into its parts. False for anything that is not shaped like one, which
        /// includes every name that was never ours.
        /// </summary>
        public static bool TryParse(string name, out byte[] nonce, out byte[] tag)
        {
            nonce = null;
            tag = null;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var labels = name.Split('.');
            if (labels.Length != 3)
            {
                return false;
            }

            if (!string.Equals(labels[2], Suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var parsedNonce = FromHex(labels[0], NonceLength);
            var parsedTag = FromHex(labels[1], TagLength);
            if (parsedNonce == null || parsedTag == null)
            {
                return false;
            }

            nonce = parsedNonce;
            tag = parsedTag;
            return true;
        }

        /// <summary>
        /// Whether this name was built from this gate key. Compared in fixed time, because the
        /// caller is holding an attacker's bytes.
        /// </summary>
        public static bool Matches(string name, byte[] gateKey)
        {
            if (gateKey == null)
            {
                return false;
            }

            byte[] nonce;
            byte[] tag;
            if (!TryParse(name, out nonce, out tag))
            {
                return false;
            }

            return FixedTime.Equals(Tag(gateKey, nonce), tag);
        }

        /// <summary>Null for anything that is not exactly the expected number of hex bytes.</summary>
        private static byte[] FromHex(string text, int expectedBytes)
        {
            if (text == null || text.Length != expectedBytes * 2)
            {
                return null;
            }

            var bytes = new byte[expectedBytes];
            for (var i = 0; i < expectedBytes; i++)
            {
                var high = HexValue(text[i * 2]);
                var low = HexValue(text[i * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    return null;
                }

                bytes[i] = (byte)((high << 4) | low);
            }

            return bytes;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            // Lowercase only. A name is generated by us and echoed by a client, so there is no
            // reason to accept a second spelling of the same thing.
            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            return -1;
        }
    }
}
