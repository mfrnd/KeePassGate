using System;

namespace KeePassGate.Diagnostics
{
    /// <summary>A log that discards everything, so no call site has to test for null.</summary>
    public sealed class NullGateLog : IGateLog
    {
        public static readonly IGateLog Instance = new NullGateLog();

        private NullGateLog()
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception error)
        {
        }

        public void Unexpected(string message, Exception error)
        {
        }
    }
}
