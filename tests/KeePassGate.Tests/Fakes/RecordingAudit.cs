using System.Collections.Generic;
using KeePassGate.Audit;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>An audit in a list, so a test asserts about decisions rather than about a file.</summary>
    /// <remarks>
    /// One of these rather than one per fixture. Two had already been written byte for byte
    /// alike in two files, which is how two tests come to disagree about what the real thing
    /// does.
    ///
    /// The gate records from its connection threads while a test reads from its own, so a write
    /// takes a lock and a read is a copy taken under it, as in `RecordingGateLog`.
    /// </remarks>
    public sealed class RecordingAudit : IGateAudit
    {
        private readonly object _lock = new object();
        private readonly List<AuditEntry> _entries = new List<AuditEntry>();

        public IReadOnlyList<AuditEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToArray();
                }
            }
        }

        public void Record(AuditEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
            }
        }
    }
}
