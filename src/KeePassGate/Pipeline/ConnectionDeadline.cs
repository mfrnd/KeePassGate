using System;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// What a pairing or a renewal tells the transport once its waits are over and its record is
    /// about to be written, so the connection's deadline is dropped there.
    /// </summary>
    /// <remarks>
    /// **The two exchanges that wait on a person keep a deadline after their request is read**,
    /// `GateOptions.PairingBudget`, because what comes next is a list of waits: the screen, the
    /// dialog, the grace on it and the settling of a busy KeePass. The write is not among them.
    /// It is a whole-database save that no number bounds, and the invitation is spent inside it,
    /// so a deadline still running there could only throw away the certificate of a record that
    /// lands anyway. Dropped here, the rest is measured as every other write is: no deadline while
    /// the plugin's own work runs, and the ordinary budget again once the answer is being written.
    ///
    /// Only the transport knows the deadline and only the handler knows where the write begins,
    /// so it travels in <see cref="IGateContext.Items"/> for the reason
    /// <see cref="ServedDatabase"/> gives. A context that carries none, which is every test of a
    /// handler on its own, drops nothing.
    /// </remarks>
    public static class ConnectionDeadline
    {
        public const string Key = "kpg.deadline";

        public static void PutOn(IGateContext context, Action drop)
        {
            if (context != null && drop != null)
            {
                context.Items[Key] = drop;
            }
        }

        /// <summary>Drops the connection's deadline, where a transport gave one to drop.</summary>
        public static void Drop(IGateContext context)
        {
            object found;
            if (context == null || !context.Items.TryGetValue(Key, out found))
            {
                return;
            }

            var drop = found as Action;
            if (drop != null)
            {
                drop();
            }
        }
    }
}
