using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// Writes to <see cref="Trace"/>, and additionally to a file when one is named. The file is
    /// what an automated end to end test reads to find out what the plugin did inside a KeePass it
    /// does not otherwise have a handle on.
    /// </summary>
    /// <remarks>
    /// Nothing here throws. A log that cannot be written is worth less than a running password
    /// manager, so a failed write is dropped after one attempt to report it to <see cref="Trace"/>.
    /// </remarks>
    public sealed class TraceGateLog : IGateLog
    {
        /// <summary>
        /// Names a file to append to. Read from the environment rather than from KeePass's
        /// configuration so that a test can set it without writing to the host's settings.
        /// </summary>
        public const string LogFileVariable = "KEEPASSGATE_LOG_FILE";

        private readonly object _fileLock = new object();
        private readonly string _filePath;
        private readonly Func<DateTime> _clock;
        // Volatile because it is read before the lock is taken and written in a catch after it
        // has been released, from whichever thread logged: the gate reads on a connection
        // thread while the UI thread writes.
        private volatile bool _fileFailed;

        public TraceGateLog()
            : this(ReadLogFileVariable(), null)
        {
        }

        public TraceGateLog(string filePath)
            : this(filePath, null)
        {
        }

        internal TraceGateLog(string filePath, Func<DateTime> clock)
        {
            _filePath = string.IsNullOrEmpty(filePath) ? null : filePath;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public void Info(string message)
        {
            Write(GateLogFormat.InfoLevel, message, null);
        }

        public void Warn(string message)
        {
            Write(GateLogFormat.WarnLevel, message, null);
        }

        public void Error(string message, Exception error)
        {
            Write(GateLogFormat.ErrorLevel, message, error);
        }

        public void Unexpected(string message, Exception error)
        {
            Write(GateLogFormat.UnexpectedLevel, message, error);
        }

        private static string ReadLogFileVariable()
        {
            try
            {
                return Environment.GetEnvironmentVariable(LogFileVariable);
            }
            catch (System.Security.SecurityException)
            {
                // A policy that forbids reading the environment. There is simply no log file.
                return null;
            }
        }

        private void Write(string level, string message, Exception error)
        {
            string line;
            try
            {
                line = GateLogFormat.Line(_clock(), level, message, error);
            }
            catch (Exception)
            {
                // Deliberately everything, and the one rule this file lives by. A logger is called
                // from inside catch handlers, including the four that stand between a failure and
                // the end of the password manager, so it may never add an exception of its own.
                // Formatting reaches Exception.ToString on an object this plugin did not write.
                return;
            }

            try
            {
                Trace.WriteLine(line);
            }
            catch (Exception)
            {
                // A trace listener is somebody else's code, attached by somebody else, and it can
                // throw anything at all. Losing the line is the lesser failure.
            }

            AppendToFile(line);
        }

        private void AppendToFile(string line)
        {
            if (_filePath == null || _fileFailed)
            {
                return;
            }

            try
            {
                lock (_fileLock)
                {
                    // Share the file so that a reader can watch it while KeePass is running.
                    using (var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                    }
                }
            }
            catch (Exception error) when (
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is System.Security.SecurityException ||
                error is ArgumentException ||
                error is NotSupportedException)
            {
                // Every way a path can be unusable: gone, locked, forbidden, or not a path at all.
                // The file is given up on rather than retried, because a log that fails once on a
                // socket thread will fail on every connection after it.
                _fileFailed = true;
                try
                {
                    Trace.WriteLine(GateLogFormat.Line(
                        _clock(), GateLogFormat.WarnLevel, "log file disabled after a failed write", error));
                }
                catch (Exception)
                {
                    // As above: there is nowhere left to report a logger that cannot report.
                }
            }
        }
    }
}
