using System;
using System.Runtime.Serialization;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Holding the thread that owns KeePass's windows still while another thread reads what it
    /// owns.
    /// </summary>
    /// <remarks>
    /// **Every read of KeePass's objects off its own thread goes through this**, `DESIGN.md`
    /// §119. KeePass changes its tree on that thread with no sign that it is doing so: an
    /// operator's edit, a merge, or this plugin's own writer. A connection thread reading the same
    /// lists meanwhile threw, found a slot a removal had emptied, or missed a key that was there,
    /// and none of that can be detected from the reading side, where a list bumps its version
    /// too late to show. A thread that is waiting for a read to finish changes nothing, so the read
    /// sees a tree standing still.
    ///
    /// **Held for one read step**: a sweep, a lookup, a walk up the rules, an admission record,
    /// or the whole of a request that changes nothing, which the router holds once so that all of
    /// those are read from one database. Nothing that waits belongs inside one, because KeePass's
    /// window waits with it: not the writer, not the audit's pacing or its file, not a dialog,
    /// and not the network. **What is logged inside a hold is queued**, as a line logged on
    /// KeePass's own thread is, and written by the log's own writer:
    /// `ControlUiThread.MayNotWait`.
    /// </remarks>
    public interface IReadHold
    {
        /// <summary>
        /// What <paramref name="read"/> returned, having run on the calling thread while KeePass's
        /// thread waited for it.
        /// </summary>
        /// <remarks>
        /// **Throws `VaultUnsettled` when the read cannot be trusted**, which a handler answers as
        /// `503 busy`: KeePass's thread could not be held within the wait, or the read outlasted
        /// the limit and KeePass's thread was let go before it finished. Whatever the read itself
        /// threw is thrown as it was.
        ///
        /// A read already inside a hold, or running on KeePass's own thread, is simply run: the
        /// tree cannot move under it either way.
        /// </remarks>
        T Hold<T>(Func<T> read);
    }

    /// <summary>
    /// A thread holding KeePass's thread still asked that thread for something, which it cannot do
    /// until the hold ends.
    /// </summary>
    /// <remarks>
    /// A defect here rather than anything a caller or the operator did, so it is thrown as one, to
    /// be counted where the catch-alls count everything: waited out instead, it would hold
    /// KeePass's window for the length of the hold's limit and then fail anyway. Deliberately not
    /// a `GateFailure`, which several filters take as an ordinary failure to read custom data.
    /// </remarks>
    [Serializable]
    public sealed class HoldMisused : Exception
    {
        public HoldMisused(string message)
            : base(message)
        {
        }

        private HoldMisused(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
