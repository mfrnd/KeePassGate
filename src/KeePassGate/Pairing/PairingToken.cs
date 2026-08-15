using System;
using System.Security.Cryptography;
using System.Text;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// The secret an operator hands to an invitee, and how it is written down.
    /// </summary>
    /// <remarks>
    /// Thirty-two bytes, because the same value is used as a gate key and a gate key is thirty-two
    /// bytes. It is written as base64url without padding, so it survives being pasted into a URL, a
    /// shell, a configuration file or a chat window without anything quoting it. That alphabet is
    /// RFC 4648 section 5, and the padding it omits is section 3.2.
    /// https://www.rfc-editor.org/rfc/rfc4648#section-5
    ///
    /// The plugin only ever encodes. A client decodes, and the decoder is here so that the two
    /// halves are written against one another and tested together rather than agreeing by
    /// coincidence.
    /// </remarks>
    public static class PairingToken
    {
        /// <summary>The same length as a gate key, because a token is used as one.</summary>
        public const int Length = 32;

        /// <summary>Thirty-two bytes in base64url is forty-three characters, with no padding.</summary>
        public const int TextLength = 43;

        private const string Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        public static byte[] New()
        {
            var token = new byte[Length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(token);
            }

            return token;
        }

        public static string Encode(byte[] token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            var text = new StringBuilder((token.Length * 4 + 2) / 3);

            for (var i = 0; i < token.Length; i += 3)
            {
                var remaining = token.Length - i;
                var block = token[i] << 16;
                if (remaining > 1)
                {
                    block |= token[i + 1] << 8;
                }

                if (remaining > 2)
                {
                    block |= token[i + 2];
                }

                text.Append(Alphabet[(block >> 18) & 0x3F]);
                text.Append(Alphabet[(block >> 12) & 0x3F]);
                if (remaining > 1)
                {
                    text.Append(Alphabet[(block >> 6) & 0x3F]);
                }

                if (remaining > 2)
                {
                    text.Append(Alphabet[block & 0x3F]);
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Reads one back. Null for anything that is not exactly a token: the wrong length, a
        /// character outside the alphabet, padding, or a last character whose two spare bits are
        /// set, none of which this ever writes.
        /// </summary>
        public static byte[] Decode(string text)
        {
            if (text == null || text.Length != TextLength)
            {
                return null;
            }

            var token = new byte[Length];
            var written = 0;
            var block = 0;
            var bits = 0;

            foreach (var c in text)
            {
                var value = Alphabet.IndexOf(c);
                if (value < 0)
                {
                    return null;
                }

                block = (block << 6) | value;
                bits += 6;

                if (bits < 8)
                {
                    continue;
                }

                bits -= 8;
                if (written == token.Length)
                {
                    return null;
                }

                token[written++] = (byte)((block >> bits) & 0xFF);
            }

            // Forty-three characters carry 258 bits for a 256 bit token, so two are left over in
            // the last one, and they are written as zero. Ignoring them made four spellings of one
            // token, three of which the client's decoder refuses.
            if ((block & ((1 << bits) - 1)) != 0)
            {
                return null;
            }

            return written == token.Length ? token : null;
        }
    }
}
