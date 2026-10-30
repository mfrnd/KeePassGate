using System.Collections.Generic;
using System.Text;

namespace KeePassGate.Transport
{
    /// <summary>
    /// Percent decoding, refusing rather than repairing, in the two places a request needs it.
    /// </summary>
    /// <remarks>
    /// **A path is decoded segment by segment, after it has been split**, which is what RFC 3986
    /// section 2.2 requires and what this used to get wrong. Decoding first makes `%2F` a separator
    /// and turns one segment into two, so an attachment named `a/b` could never be addressed at
    /// all: the request would arrive with an extra segment and match no route. Splitting first
    /// leaves the escape meaning a slash **inside** the segment, which is the whole point of having
    /// escaped it.
    ///
    /// **`+` is a space in a query and a plus in a path.** That rule belongs to
    /// `application/x-www-form-urlencoded`, not to paths, and applying it to both made `a+b` and
    /// `a b` the same request.
    ///
    /// **A control character is refused however it arrived.** A parser that accepts `%0A` is a
    /// parser that puts a line break into a log, and this plugin's audit is a file of one record per
    /// line.
    ///
    /// **A character the grammar says to escape is refused when it arrives bare**, which is
    /// <see cref="StandsForItself"/>: the target is RFC 3986's, and one it does not allow is not a
    /// target to guess the meaning of.
    ///
    /// **An escape is an octet of UTF-8, not a character.** `%C3%A9` is one `e` with an acute
    /// accent and not two characters, so the octets are gathered first and decoded once. A sequence
    /// that is not UTF-8 is refused rather than repaired, like everything else here.
    /// </remarks>
    internal static class PercentEncoding
    {
        /// <summary>A segment of a path, where `+` means itself.</summary>
        public static bool TryDecodeSegment(string text, out string decoded)
        {
            return TryDecode(text, false, out decoded);
        }

        /// <summary>A name or a value from a query, where `+` means a space.</summary>
        public static bool TryDecodeQuery(string text, out string decoded)
        {
            return TryDecode(text, true, out decoded);
        }

        /// <summary>
        /// Whether a decoded segment is one that navigates rather than names.
        /// </summary>
        /// <remarks>
        /// Compared as a whole segment rather than searched for as a substring, which is the other
        /// half of splitting first: `notes..txt` is a perfectly good attachment name, and it used
        /// to match no route at all because its name contains two dots in a row.
        /// </remarks>
        public static bool IsTraversal(string segment)
        {
            return segment == "." || segment == "..";
        }

        /// <summary>Whether a decoded value climbs, however it is spelled.</summary>
        /// <remarks>
        /// Not the same question as <see cref="IsTraversal"/>, which asks whether one segment
        /// *is* a climb. A segment that decodes to `../../` is not `..` and passes that,
        /// because the escape was decoded after the split. That order is deliberate, so that an
        /// attachment called `a/b` can be addressed at all. This asks the other question: whether
        /// what the escape decoded into contains a climb. Backslash counts, because this runs on
        /// Windows and `..%5C..%5C` climbs there exactly as `..%2F..%2F` does.
        ///
        /// Nothing here maps a path to a file. The tree this would climb is the group tree,
        /// which is the one the nearest-wins rules resolve over.
        /// </remarks>
        public static bool Navigates(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var part in value.Split('/', '\\'))
            {
                if (IsTraversal(part))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Refuses a sequence that is not UTF-8 rather than repairing it, which is what every
        /// other refusal here does.
        /// </summary>
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private static bool TryDecode(string text, bool plusIsSpace, out string decoded)
        {
            decoded = null;
            if (text == null)
            {
                return false;
            }

            // Octets first, text second. An escape stands for one byte of UTF-8, not for one
            // character: `%C3%A9` is the two bytes of a single `e` with an acute accent, and
            // turning each byte into a character of its own gives two characters that are not what
            // was sent and match nothing. RFC 3986 section 2.5.
            // https://www.rfc-editor.org/rfc/rfc3986#section-2.5
            var octets = new List<byte>(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (c == '+' && plusIsSpace)
                {
                    octets.Add((byte)' ');
                    continue;
                }

                if (c != '%')
                {
                    // The head is refused unless every byte of it is ASCII, so a character here is
                    // one byte and casting one is exact.
                    if (!StandsForItself(c, plusIsSpace))
                    {
                        return false;
                    }

                    octets.Add((byte)c);
                    continue;
                }

                if (i + 2 >= text.Length)
                {
                    return false;
                }

                var high = HexValue(text[i + 1]);
                var low = HexValue(text[i + 2]);
                if (high < 0 || low < 0)
                {
                    return false;
                }

                var value = (byte)((high << 4) | low);

                // A byte below 0x80 stands for itself in UTF-8, so a control refused here is a
                // control however it was spelled, and no continuation byte is ever caught by it.
                if (value < 0x20 || value == 0x7F)
                {
                    return false;
                }

                octets.Add(value);
                i += 2;
            }

            string text8;
            try
            {
                text8 = Utf8.GetString(octets.ToArray());
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            foreach (var c in text8)
            {
                // The C1 controls, which arrive only as UTF-8 and would otherwise reach a log line
                // that this plugin writes one record per line of.
                if (char.IsControl(c))
                {
                    return false;
                }
            }

            decoded = text8;
            return true;
        }

        /// <summary>
        /// Whether a character may arrive unescaped: what RFC 3986 allows in a path segment, less
        /// the escape itself, and in a query the `/` and `?` it allows besides.
        /// </summary>
        /// <remarks>
        /// What is left out has to be escaped by the grammar: `"`, `#`, `&lt;`, `&gt;`, `\`, `^`,
        /// the grave accent, `{`, `|`, `}`, the brackets, the space and every control. A target
        /// carrying one bare is not a request line, and RFC 9112 section 3 says to answer one with
        /// `400` rather than process it. Taken as itself, a field named `a|b` was served here and
        /// refused by any server that follows the grammar, so a client that forgot to escape
        /// worked against this gate alone. https://www.rfc-editor.org/rfc/rfc3986#section-3.3
        /// </remarks>
        private static bool StandsForItself(char c, bool inQuery)
        {
            return Ascii.IsLetterOrDigit(c)
                   || "-._~!$&'()*+,;=:@".IndexOf(c) >= 0
                   || (inQuery && (c == '/' || c == '?'));
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return (c - 'a') + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return (c - 'A') + 10;
            }

            return -1;
        }
    }
}
