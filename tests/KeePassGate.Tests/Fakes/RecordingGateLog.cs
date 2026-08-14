using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Diagnostics;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>A log that remembers, so a test can assert that a failure was reported.</summary>
    internal sealed class RecordingGateLog : IGateLog
    {
        public sealed class Entry
        {
            public Entry(string level, string message, Exception error)
            {
                Level = level;
                Message = message;
                Error = error;
            }

            public string Level { get; private set; }

            public string Message { get; private set; }

            public Exception Error { get; private set; }

            public override string ToString()
            {
                return Level + " " + Message + (Error == null ? string.Empty : " | " + Error.GetType().Name);
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries
        {
            get { return _entries; }
        }

        public IEnumerable<Entry> Warnings
        {
            get { return _entries.Where(e => e.Level == GateLogFormat.WarnLevel); }
        }

        public IEnumerable<Entry> Errors
        {
            get { return _entries.Where(e => e.Level == GateLogFormat.ErrorLevel); }
        }

        /// <summary>
        /// What one of the catch-alls caught. A test that finds one of these has found
        /// something nobody predicted, which is nearly always a defect rather than a refusal.
        /// </summary>
        public IEnumerable<Entry> Surprises
        {
            get { return _entries.Where(e => e.Level == GateLogFormat.UnexpectedLevel); }
        }

        public void Info(string message)
        {
            _entries.Add(new Entry(GateLogFormat.InfoLevel, message, null));
        }

        public void Warn(string message)
        {
            _entries.Add(new Entry(GateLogFormat.WarnLevel, message, null));
        }

        public void Error(string message, Exception error)
        {
            _entries.Add(new Entry(GateLogFormat.ErrorLevel, message, error));
        }

        public void Unexpected(string message, Exception error)
        {
            _entries.Add(new Entry(GateLogFormat.UnexpectedLevel, message, error));
        }
    }
}
