using System;
using System.Runtime.Serialization;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// An attachment the gate cannot read where KeePass keeps it, with why.
    /// </summary>
    /// <remarks>
    /// **The gate reads an attachment a piece at a time out of KeePass's own buffer and has no
    /// second way of reading**, `Bridge.StoredBinaries`. Where that cannot be done, a protected
    /// attachment KeePass keeps, at that moment, through the cipher another plugin gave it, a
    /// KeePass that keeps an attachment otherwise, a Windows that no longer decrypts a piece
    /// on its own, nothing is read and this is thrown, from a read of the attachment, from
    /// every tag that covers it and from a write that would store such an attachment.
    ///
    /// **Named so that a client is told what it met.** It is nothing a client did and nothing
    /// asking again mends, and it is no defect of the gate's either, so the transport answers
    /// it `500 unreadable` where anything else a handler throws is `500 internal error`,
    /// `GateConversation.Serve`, and counts it the same way, since whoever runs the gate has
    /// to know. Not a <see cref="GateFailure"/>: those are named in the filters for what a
    /// connection and a store are expected to throw, and this is expected nowhere.
    /// </remarks>
    [Serializable]
    public sealed class AttachmentUnreadable : Exception
    {
        public AttachmentUnreadable(string message)
            : base(message)
        {
        }

        private AttachmentUnreadable(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
