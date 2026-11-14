using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeePassGate.Testing
{
    /// <summary>
    /// Reads a whole answer off a stream the gate is about to close.
    /// </summary>
    /// <remarks>
    /// One copy, because both clients in these tests read an answer the same way and both had the
    /// same defect in it. A helper that gets this wrong is worse than a test with no helper: the
    /// test still runs, still passes on most inputs, and reports the one it corrupts as a
    /// disagreement between the two halves of the wire.
    /// </remarks>
    public static class HttpAnswer
    {
        /// <summary>How much is asked for at once, which is where the decoding used to go wrong.</summary>
        public const int ChunkBytes = 4096;

        /// <summary>
        /// The most hex digits a chunk's size is read from: more than any chunk needs, and few
        /// enough that the size they spell is a count of bytes an array can hold.
        /// </summary>
        private const int MostSizeDigits = 7;

        private static readonly byte[] Blank = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

        /// <summary>
        /// Everything that arrives before the stream ends, decoded once at the end, with content
        /// that arrived in chunks put back together.
        /// </summary>
        /// <remarks>
        /// **The bytes are gathered first and decoded together.** A read boundary falls wherever
        /// the network puts it, and a character this API carries can be four bytes long, so
        /// decoding each read on its own turns a sequence split across two of them into a
        /// replacement character on each side of the split. An answer that arrived intact then
        /// reads as one that did not, and a test comparing what it stored with what came back
        /// fails for a reason nothing on either side of the wire did. The reference client gathers
        /// the whole answer before decoding it for exactly this reason.
        ///
        /// The head is US-ASCII and the body is UTF-8, and one decoding covers both because ASCII
        /// is a subset of it.
        /// </remarks>
        /// <exception cref="InvalidDataException">
        /// The head names chunks and the content is not exactly chunks, <see cref="Decoded"/>.
        /// </exception>
        public static string ReadToEnd(Stream stream)
        {
            return Decoded(Gathered(stream));
        }

        /// <summary>Every byte that arrives before the stream ends or fails.</summary>
        public static byte[] Gathered(Stream stream)
        {
            var buffer = new byte[ChunkBytes];
            using (var bytes = new MemoryStream())
            {
                while (true)
                {
                    int read;
                    try
                    {
                        read = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        break;
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    bytes.Write(buffer, 0, read);
                }

                return bytes.ToArray();
            }
        }

        /// <summary>
        /// An answer's bytes as text: the head as it arrived, and after the blank line the
        /// content, taken out of its chunks where the head names `Transfer-Encoding: chunked`.
        /// </summary>
        /// <remarks>
        /// **Taken out of the chunks before anything is decoded.** The gate writes the answer to
        /// a read of an entry or of a field in chunks, and a chunk ends wherever its bytes ran
        /// out, in the middle of a character as readily as between two. Decoded first, the
        /// character on each side of a chunk's size line would be a replacement character.
        ///
        /// **Strict, and a chunk it does not understand fails whoever asked.** What the gate
        /// writes is a size in hex, a line end, that many bytes, a line end, and a last chunk
        /// of no bytes with nothing after it. Content cut short of that last chunk is an answer
        /// the gate did not finish, and reading it as whole would pass a test on half a document.
        /// </remarks>
        /// <exception cref="InvalidDataException">
        /// The head names chunks and the content is not exactly chunks.
        /// </exception>
        public static string Decoded(byte[] raw)
        {
            if (raw == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            var blank = IndexOf(raw, Blank);
            if (blank < 0)
            {
                return Encoding.UTF8.GetString(raw);
            }

            var head = Encoding.ASCII.GetString(raw, 0, blank);
            if (!head.StartsWith("HTTP/1.", StringComparison.Ordinal) || !NamesChunks(head))
            {
                return Encoding.UTF8.GetString(raw);
            }

            var content = FromChunks(raw, blank + Blank.Length);
            return head + "\r\n\r\n" + Encoding.UTF8.GetString(content);
        }

        /// <summary>
        /// The bytes chunked content carries, from <paramref name="at"/> to the last chunk,
        /// which has to be where the bytes end.
        /// </summary>
        /// <exception cref="InvalidDataException">The content is not exactly chunks.</exception>
        public static byte[] FromChunks(byte[] raw, int at)
        {
            if (raw == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            using (var content = new MemoryStream())
            {
                while (true)
                {
                    if (at >= raw.Length)
                    {
                        throw new InvalidDataException(
                            "the content ends without its last chunk, so the answer was cut short");
                    }

                    var began = at;
                    var size = 0;
                    while (at < raw.Length && Uri.IsHexDigit((char)raw[at]) && at - began < MostSizeDigits)
                    {
                        size = (size * 16) + Uri.FromHex((char)raw[at]);
                        at++;
                    }

                    if (at == began || !LineEndAt(raw, at))
                    {
                        throw new InvalidDataException(string.Format(
                            CultureInfo.InvariantCulture,
                            "the chunk at byte {0} does not begin with a size in hex and a line end",
                            began));
                    }

                    at += 2;

                    if (size == 0)
                    {
                        if (!LineEndAt(raw, at) || at + 2 != raw.Length)
                        {
                            throw new InvalidDataException(
                                "the last chunk is not followed by a line end and then nothing");
                        }

                        return content.ToArray();
                    }

                    if (size > raw.Length - at || !LineEndAt(raw, at + size))
                    {
                        throw new InvalidDataException(string.Format(
                            CultureInfo.InvariantCulture,
                            "the chunk of {0} bytes at byte {1} is not all there with a line end after it",
                            size,
                            at));
                    }

                    content.Write(raw, at, size);
                    at += size + 2;
                }
            }
        }

        /// <summary>Whether a head carries the field that names content sent in chunks.</summary>
        private static bool NamesChunks(string head)
        {
            foreach (var line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                var colon = line.IndexOf(':');
                var name = colon <= 0 ? string.Empty : line.Substring(0, colon);
                if (!string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(line.Substring(colon + 1).Trim(), "chunked", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("the answer names a transfer coding other than chunked: " + line);
                }

                return true;
            }

            return false;
        }

        private static bool LineEndAt(byte[] raw, int at)
        {
            return at >= 0 && at + 1 < raw.Length && raw[at] == (byte)'\r' && raw[at + 1] == (byte)'\n';
        }

        private static int IndexOf(byte[] raw, byte[] sought)
        {
            for (var at = 0; at <= raw.Length - sought.Length; at++)
            {
                var found = true;
                for (var index = 0; index < sought.Length && found; index++)
                {
                    found = raw[at + index] == sought[index];
                }

                if (found)
                {
                    return at;
                }
            }

            return -1;
        }
    }
}
