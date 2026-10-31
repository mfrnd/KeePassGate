using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// Writes to <see cref="Trace"/>, and to a file as well: the one the environment names, or one
    /// beside the audit, under this account's own application data.
    /// </summary>
    /// <remarks>
    /// **A file whether or not anybody asked for one**, because what the plugin shows sends the
    /// operator to the log, and a log that reached only a debugger was one that nobody following
    /// that pointer could find. It is also what an automated end to end test reads to find out what
    /// the plugin did inside a KeePass it does not otherwise have a handle on, and the test names a
    /// file of its own so that its lines stay out of the account's.
    ///
    /// **Bounded the way the audit is.** At <see cref="RollAt"/> the file is set aside and a new
    /// one begun, and one previous file is kept. This is written on every machine the plugin is
    /// installed on, and a connection writes a line, so a file with no bound would grow for as long
    /// as the plugin is used.
    ///
    /// Nothing here throws. A log that cannot be written is worth less than a running password
    /// manager, so a failed write is dropped after one attempt to report it to <see cref="Trace"/>.
    /// </remarks>
    public sealed class TraceGateLog : IGateLog
    {
        /// <summary>
        /// Names the file to append to in place of the default. Read from the environment rather
        /// than from KeePass's configuration so that a test can set it without writing to the
        /// host's settings.
        /// </summary>
        public const string LogFileVariable = "KEEPASSGATE_LOG_FILE";

        /// <summary>What the file is called when nothing names one.</summary>
        public const string DefaultFileName = "plugin.log";

        /// <summary>
        /// How large the file grows before the last one is set aside: the audit's bound, which a
        /// text editor still opens.
        /// </summary>
        public const long RollAt = 8 * 1024 * 1024;

        private readonly object _fileLock = new object();
        private readonly string _filePath;
        private readonly Func<DateTime> _clock;
        // Volatile because it is read before the lock is taken and written in a catch after it
        // has been released, from whichever thread logged: the gate reads on a connection
        // thread while the UI thread writes.
        private volatile bool _fileFailed;

        // Under the lock, like the setting aside it is about.
        private bool _rollReported;

        /// <summary>Writes to the file the environment names, or to one beside the audit.</summary>
        public TraceGateLog()
            : this(PluginFiles.Resolve(LogFileVariable, DefaultFileName), null)
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
                // from inside catch handlers, the four catch-alls among them, so it may never add
                // an exception of its own, which would escape the very catch reporting through it.
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
                    Prepare();

                    // Share the file so that a reader can watch it while KeePass is running.
                    using (var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                    }
                }
            }
            catch (Exception error) when (Expected.OfStorage(error))
            {
                // Every way a path can be unusable: gone, locked, forbidden, or not a path at all,
                // named once in `Expected` now that the audit needs the same list. Two spellings of
                // one list can only ever be found to disagree by a failure neither anticipated.
                //
                // The file is given up on rather than retried, because a log that fails once on a
                // socket thread will fail on every connection after it. The audit deliberately does
                // the opposite: it counts and keeps trying, because a lost decision is worth more
                // than a lost log line and a lock that clears should start recording again.
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

        /// <summary>
        /// Makes the directory the file goes in, and sets the file aside once it has reached
        /// <see cref="RollAt"/>, keeping one previous file. Called under the lock.
        /// </summary>
        /// <remarks>
        /// A directory that cannot be made throws, and the file is given up on as it is for any
        /// write that fails. Setting the file aside is an attempt of its own, as the audit's is: it
        /// bounds the file and is no condition for writing to it, and a viewer holding the previous
        /// file, which is how anything following a log holds it, would otherwise end the log for
        /// the rest of the session. It is tried again at the next line.
        /// </remarks>
        private void Prepare()
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var file = new FileInfo(_filePath);
                if (!file.Exists || file.Length < RollAt)
                {
                    return;
                }

                var previous = _filePath + ".1";
                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }

                File.Move(_filePath, previous);
                _rollReported = false;
            }
            catch (Exception error) when (Expected.OfStorage(error))
            {
                if (_rollReported)
                {
                    return;
                }

                // Said once until it works again, and to the trace alone: a line written through
                // this log would come straight back here.
                _rollReported = true;
                try
                {
                    Trace.WriteLine(GateLogFormat.Line(
                        _clock(), GateLogFormat.WarnLevel, "log file not set aside, so it grows past its bound", error));
                }
                catch (Exception)
                {
                    // As above: there is nowhere left to report a logger that cannot report.
                }
            }
        }
    }
}
