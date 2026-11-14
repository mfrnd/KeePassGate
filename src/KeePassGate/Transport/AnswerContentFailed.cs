using System;
using System.Runtime.Serialization;

namespace KeePassGate.Transport
{
    /// <summary>
    /// What the content of an answer threw while the answer was written as it was sent, thrown
    /// again under a name of its own.
    /// </summary>
    /// <remarks>
    /// **So that a failure of the plugin's own reading is never taken for a peer that left.**
    /// An answer written as it is sent runs the plugin's code, the reading of a value among
    /// it, inside the write of the answer, and everything around that write is filtered for
    /// what a connection throws: an `IOException`, an `ObjectDisposedException`, a failure of
    /// the TLS underneath. A value that cannot be decrypted throws a `CryptographicException`,
    /// which the connection's thread takes for host material released at a close, and anything
    /// the content throws of a kind a socket throws would be logged as an answer lost. Each is
    /// a defect to count, and none is the peer's doing.
    ///
    /// `HttpResponse.Write` throws this for whatever the content threw while the stream
    /// underneath had not failed, with what was thrown as its cause. Nothing names it in a
    /// filter, so it goes on to the connection's thread, which counts it: `DESIGN.md` §141
    /// lists the place. A failure of the stream itself is thrown as it is.
    /// </remarks>
    [Serializable]
    public sealed class AnswerContentFailed : Exception
    {
        public AnswerContentFailed(Exception cause)
            : base("the content of an answer failed while the answer was written", cause)
        {
        }

        private AnswerContentFailed(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
