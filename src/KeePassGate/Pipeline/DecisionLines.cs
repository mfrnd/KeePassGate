using System.Collections.Generic;
using KeePassGate.Audit;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// The lines one request's decisions come to, kept until they can be written.
    /// </summary>
    /// <remarks>
    /// **A decision is made where the database stands still, and its line is written where a wait
    /// costs nobody else anything.** A request is decided inside the hold of its read, or
    /// inside the turn its write is made in, and both stop KeePass's own thread: writing a line
    /// there would put the audit file's disk, and the wait <see cref="DecisionPace"/> imposes on a
    /// client that writes too fast, on the thread the operator is typing into. So
    /// <see cref="AccessCheck"/> composes the line where it decides, moment and node name
    /// included, and the router writes what gathered here once the hold or the turn is over.
    ///
    /// **One request has one decision, and the later answer is the line.** A write is asked about
    /// twice: by the router before anything is queued, which can only refuse, and again inside its
    /// turn, which is the answer acted on. The second replaces the first here, so a request is
    /// still one decision line. A write that never reached its turn keeps the first, which is then
    /// the only answer the rules gave it. What a handler asks beside the decision, the protected
    /// values inside an answer already allowed, is kept after it, in the order asked.
    ///
    /// Filled on KeePass's thread and written from the connection's, one after the other and
    /// never at once: `IUiThread.Run` returns only when the work it was given has finished or
    /// been withdrawn. The lock is for the reader that cannot know that. A line composed inside
    /// a read's hold for a value its view withheld is added from the connection's thread, where
    /// the read is served, and before anything is written.
    /// </remarks>
    public sealed class DecisionLines
    {
        private readonly object _lock = new object();
        private readonly List<Line> _asked = new List<Line>();
        private Line _decided;

        /// <summary>The request's decision, in place of one made earlier.</summary>
        internal void Decided(AuditEntry entry, string allowance)
        {
            lock (_lock)
            {
                _decided = new Line(entry, allowance);
            }
        }

        /// <summary>A question asked beside the decision, kept after it.</summary>
        internal void Asked(AuditEntry entry, string allowance)
        {
            lock (_lock)
            {
                _asked.Add(new Line(entry, allowance));
            }
        }

        /// <summary>
        /// Everything another set of lines kept, added after the questions kept here, with
        /// nothing left behind there.
        /// </summary>
        /// <remarks>
        /// For a line composed where a read is decided and added only once the read is known to
        /// be served, <see cref="ProtectedWithheld"/>.
        /// </remarks>
        internal void Asked(DecisionLines composed)
        {
            var taken = composed.Taken();

            lock (_lock)
            {
                _asked.AddRange(taken);
            }
        }

        /// <summary>Everything kept, the decision first, and nothing left behind.</summary>
        internal IReadOnlyList<Line> Taken()
        {
            lock (_lock)
            {
                var taken = new List<Line>();

                if (_decided != null)
                {
                    taken.Add(_decided);
                }

                taken.AddRange(_asked);
                _decided = null;
                _asked.Clear();

                return taken;
            }
        }

        /// <summary>A line and the allowance its writing is counted against.</summary>
        internal sealed class Line
        {
            public Line(AuditEntry entry, string allowance)
            {
                Entry = entry;
                Allowance = allowance;
            }

            public AuditEntry Entry { get; private set; }

            public string Allowance { get; private set; }
        }
    }
}
