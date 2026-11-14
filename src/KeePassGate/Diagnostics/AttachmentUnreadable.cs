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
    /// on its own, nothing is read and this is thrown.
    ///
    /// **Named so that whoever catches it can tell what was met.** It is nothing a client did
    /// and nothing asking again mends, and it is no defect of the gate's either. Not a
    /// <see cref="GateFailure"/>: those are named in the filters for what a connection and a
    /// store are expected to throw, and this is expected nowhere.
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
