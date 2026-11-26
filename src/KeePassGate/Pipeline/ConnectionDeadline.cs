using System;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// What a pairing or a renewal tells the transport once a write of its record, or of a
    /// renewal's standing answer, is about to begin, so the connection's deadline is dropped
    /// there, and what a pairing tells it before a
    /// dialog it puts to the operator again, so the deadline is taken up again.
    /// </summary>
    /// <remarks>
    /// **The two exchanges that wait on a person keep a deadline after their request is read**,
    /// `GateOptions.PairingBudget`, because what comes next is a list of waits: the screen, the
    /// dialog, the grace on it and the settling of a busy KeePass. The write is not among them.
    /// It is a whole-database save that no number bounds, and the invitation is spent inside it,
    /// so a deadline still running there could only throw away the certificate of a record that
    /// lands anyway, or the `403` of a refusal that does. Dropped here, the rest is measured as every other write is: no deadline while
    /// the plugin's own work runs, and the ordinary budget again once the answer is being written.
    ///
    /// **A pairing whose write finds its approval stale waits on a person again**: the write saves
    /// nothing, and the request puts the dialog to the operator again. That dialog is a wait like
    /// the first, so the deadline is taken up again before it, to the end of the pairing budget
    /// counted from the request, <see cref="Resume"/>, and dropped again where the next write
    /// begins.
    ///
    /// Only the transport knows the deadline and only the handler knows where the write begins,
    /// so it travels in <see cref="IGateContext.Items"/> for the reason
    /// <see cref="ServedDatabase"/> gives. A context that carries none, which is every test of a
    /// handler on its own, drops and resumes nothing.
    /// </remarks>
    public static class ConnectionDeadline
    {
        public const string Key = "kpg.deadline";

        public const string ResumeKey = "kpg.deadline.resume";

        /// <summary>
        /// Puts on the context what drops the connection's deadline and what takes it up again,
        /// either of them null where the transport gives none.
        /// </summary>
        public static void PutOn(IGateContext context, Action drop, Action resume)
        {
            if (context == null)
            {
                return;
            }

            if (drop != null)
            {
                context.Items[Key] = drop;
            }

            if (resume != null)
            {
                context.Items[ResumeKey] = resume;
            }
        }

        /// <summary>Drops the connection's deadline, where a transport gave a deadline to drop.</summary>
        public static void Drop(IGateContext context)
        {
            Run(context, Key);
        }

        /// <summary>
        /// Takes the connection's deadline up again, to the end of the pairing budget counted from
        /// the request, where a transport gave a deadline to take up.
        /// </summary>
        public static void Resume(IGateContext context)
        {
            Run(context, ResumeKey);
        }

        private static void Run(IGateContext context, string key)
        {
            object found;
            if (context == null || !context.Items.TryGetValue(key, out found))
            {
                return;
            }

            var action = found as Action;
            if (action != null)
            {
                action();
            }
        }
    }
}
