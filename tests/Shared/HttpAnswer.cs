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
        /// Everything that arrives before the stream ends, decoded once at the end.
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
        public static string ReadToEnd(Stream stream)
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

                return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            }
        }
    }
}
