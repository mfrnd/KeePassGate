using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

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
    /// manager, so a line the file will not take is dropped, the failure is reported to
    /// <see cref="Trace"/> once as it begins, and the file is tried again at the next line.
    ///
    /// **KeePass's own thread hands its lines to a writer of the log's own** rather than waiting
    /// on the file, and so does a thread holding KeePass's thread still. Every line keeps its
    /// place, as the audit's do: see the constructor.
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

        // Under the lock, like the setting aside it is about.
        private bool _rollReported;

        // Under the lock too: whether the last line could not be written, and how many have not
        // been since the file last took one.
        private bool _failing;
        private int _missed;

        private readonly Func<bool> _mayNotWait;

        // The lines a thread that may not wait has logged, with when each was, for the log's own
        // writer to take, and whether a writer is running to take them, under a lock of their own.
        private readonly object _queueLock = new object();
        private readonly Queue<KeyValuePair<DateTime, string>> _queued = new Queue<KeyValuePair<DateTime, string>>();
        private bool _writing;

        /// <summary>
        /// Writes to the file the environment names, or to one beside the audit, which is also
        /// where it writes when the environment names something that is not a path.
        /// </summary>
        /// <param name="mayNotWait">
        /// Whether the thread logging a line may not wait for the file, which is true of KeePass's
        /// own thread and of a thread holding it still, or null when every thread may.
        /// </param>
        public TraceGateLog(Func<bool> mayNotWait)
            : this(PluginFiles.Resolve(LogFileVariable, DefaultFileName, out var refused), refused, null, mayNotWait)
        {
        }

        public TraceGateLog(string filePath)
            : this(filePath, null, null, null)
        {
        }

        internal TraceGateLog(string filePath, Func<DateTime> clock)
            : this(filePath, null, clock, null)
        {
        }

        /// <summary>The log at this path, by this clock, beginning with a name refused in its place.</summary>
        /// <remarks>
        /// **KeePass's own thread never waits on the file**, which is the audit's rule for its
        /// lines and holds for these for the same reason: a log on slow or unreachable storage
        /// held KeePass's window for every line it wrote there, and for every connection's line
        /// ahead of it on the lock. So a line logged on a thread that may not wait goes to the
        /// trace at once and is queued for a thread of the log's own, which runs while anything is
        /// queued and ends once nothing is. A line logged on any other thread first writes every
        /// line queued before it, under the file's lock that the log's writer takes too, so every
        /// line keeps its place.
        ///
        /// **A thread holding KeePass's thread still is such a thread**, since the window waits
        /// for the reader as it waits for its own thread: `ControlUiThread.MayNotWait` is the
        /// question the plugin hands in, and what a read logs inside its hold is queued with the
        /// rest.
        ///
        /// **An ordinary exit waits for it, briefly**: what KeePass logs as it closes, the gate
        /// stopping among it, is queued just before the process ends, and the writer is a background
        /// thread, ended with the process wherever it had got to. So `Terminate` waits for it,
        /// <see cref="Drain"/>, for `PluginFiles.ExitWait` at most, and then lets the process end
        /// anyway, so that storage that does not answer never keeps KeePass's process alive or holds
        /// up a logoff. A line still queued when the process ends is lost, which
        /// `docs/getting-started.md` states.
        ///
        /// **The writer is a root**, in the sense `GateIncidents` means: its thread has nothing
        /// above it, and an exception escaping it ends the process. It is safe the way the accept
        /// thread is, `DESIGN.md` §141: what it writes goes through <see cref="AppendToFile"/>,
        /// whose every call is filtered or guarded like the rest of this log, and the locks it takes
        /// throw nothing. Anything added there has to keep that true.
        /// </remarks>
        /// <param name="refused">
        /// What <see cref="LogFileVariable"/> held when it named no path, which the first line
        /// names beside where the log is kept instead, or null when nothing was refused.
        /// </param>
        /// <param name="mayNotWait">
        /// Whether the thread logging a line may not wait for the file, or null when every thread
        /// may.
        /// </param>
        internal TraceGateLog(string filePath, string refused, Func<DateTime> clock, Func<bool> mayNotWait)
        {
            _filePath = string.IsNullOrEmpty(filePath) ? null : filePath;
            _clock = clock ?? (() => DateTime.UtcNow);
            _mayNotWait = mayNotWait ?? (() => false);

            if (refused != null)
            {
                Warn(LogFileVariable + " names " + refused + ", which is not a path, so the log is kept " +
                     (_filePath == null ? "in no file and reaches debug output alone" : "at " + _filePath));
            }
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
            DateTime at;
            string line;
            try
            {
                at = _clock();
                line = GateLogFormat.Line(at, level, message, error);
            }
            // codeql[cs/catch-of-all-exceptions] the log the catch-alls report to, which may not throw
            catch (Exception)
            {
                // Deliberately everything, and the one rule this file lives by. A logger is called
                // from inside catch handlers, the catch-alls among them, so it may never add
                // an exception of its own, which would escape the very catch reporting through it.
                // Formatting reaches Exception.ToString on an object this plugin did not write.
                return;
            }

            try
            {
                Trace.WriteLine(line);
            }
            // codeql[cs/catch-of-all-exceptions] a trace listener can throw anything, and the log may not
            catch (Exception)
            {
                // A trace listener is somebody else's code, attached by somebody else, and it can
                // throw anything at all. Losing the line is the lesser failure.
            }

            if (_filePath == null)
            {
                return;
            }

            if (_mayNotWait())
            {
                Queue(at, line);
                return;
            }

            lock (_fileLock)
            {
                WriteQueued();
                AppendToFile(at, line);
            }
        }

        /// <summary>
        /// Puts a line in the queue, and starts the log's own writer unless it is already running.
        /// </summary>
        private void Queue(DateTime at, string line)
        {
            lock (_queueLock)
            {
                _queued.Enqueue(new KeyValuePair<DateTime, string>(at, line));
                if (_writing)
                {
                    return;
                }

                _writing = true;
            }

            try
            {
                // A background thread, so that storage that does not answer never keeps KeePass's
                // process alive: an exit waits for it no longer than `PluginFiles.ExitWait`.
                new Thread(WriteInTurn) { IsBackground = true, Name = "KeePassGate log" }.Start();
            }
            // codeql[cs/catch-of-all-exceptions] the log the catch-alls report to, which may not throw
            catch (Exception)
            {
                // A thread that could not be started, which only a process out of memory meets.
                // The line stays queued for the next line logged on another thread, which writes
                // what is queued first, and the next line logged here asks for a writer again.
                lock (_queueLock)
                {
                    _writing = false;
                }
            }
        }

        /// <summary>
        /// Waits until every line queued so far is written, or <paramref name="within"/> has passed,
        /// and answers whether they were.
        /// </summary>
        /// <remarks>
        /// For KeePass exiting, the one moment its thread waits for this file, and only briefly:
        /// `PluginFiles.ExitWait` states for how long and why.
        /// </remarks>
        internal bool Drain(TimeSpan within)
        {
            var waited = Stopwatch.StartNew();
            lock (_queueLock)
            {
                while (_writing || _queued.Count > 0)
                {
                    var left = within - waited.Elapsed;
                    if (left <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    Monitor.Wait(_queueLock, left);
                }

                return true;
            }
        }

        /// <summary>The log's own writer: every queued line, until the queue stays empty.</summary>
        private void WriteInTurn()
        {
            while (true)
            {
                lock (_fileLock)
                {
                    WriteQueued();
                }

                lock (_queueLock)
                {
                    // Asked under the queue's lock, where a line queued after the last one written
                    // either finds this still running or starts another writer.
                    if (_queued.Count == 0)
                    {
                        _writing = false;
                        Monitor.PulseAll(_queueLock);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Writes every line queued so far, in the order queued. Called under the file's lock, so
        /// that taking a line from the queue and writing it are one step to every other writer.
        /// </summary>
        private void WriteQueued()
        {
            while (true)
            {
                KeyValuePair<DateTime, string> next;
                lock (_queueLock)
                {
                    if (_queued.Count == 0)
                    {
                        return;
                    }

                    next = _queued.Dequeue();
                }

                AppendToFile(next.Key, next.Value);
            }
        }

        /// <summary>
        /// Adds a line to the file, after a line naming how many went unwritten when the last of
        /// them could not be written.
        /// </summary>
        /// <remarks>
        /// **Tried again at every line, as the audit is**, because not every failure lasts. A
        /// program reading the file without letting anybody write to it stops every append for
        /// exactly as long as it holds the file, and then the next one works: given up on after one
        /// failure, the file stayed silent for the rest of the session behind a single line to the
        /// trace, while everything the plugin shows sends the operator to it. The failure is
        /// reported to the trace once when it begins, since a line written through this log would
        /// come straight back here, and its end goes into the file, with what it cost.
        /// </remarks>
        private void AppendToFile(DateTime at, string line)
        {
            if (_filePath == null)
            {
                return;
            }

            lock (_fileLock)
            {
                try
                {
                    Prepare();

                    // Share the file so that a reader can watch it while KeePass is running.
                    using (var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        if (_failing)
                        {
                            writer.WriteLine(GateLogFormat.Line(
                                at,
                                GateLogFormat.WarnLevel,
                                "the log file is being written again, and " + Lines(_missed) +
                                " written while it could not be are not in it",
                                null));
                        }

                        writer.WriteLine(line);
                    }

                    _failing = false;
                    _missed = 0;
                }
                catch (Exception error) when (Expected.OfStorage(error))
                {
                    // Every way a path can be unusable: gone, locked, forbidden, or not a path at
                    // all, named once in `Expected` now that the audit needs the same list. Two
                    // spellings of one list can only ever be found to disagree by a failure neither
                    // anticipated.
                    _missed++;
                    if (_failing)
                    {
                        return;
                    }

                    _failing = true;
                    try
                    {
                        Trace.WriteLine(GateLogFormat.Line(
                            _clock(), GateLogFormat.WarnLevel,
                            "log file not written, so lines reach the trace alone until it can be", error));
                    }
                    // codeql[cs/catch-of-all-exceptions] the log the catch-alls report to, which may not throw
                    catch (Exception)
                    {
                        // As above: there is nowhere left to report a logger that cannot report.
                    }
                }
            }
        }

        private static string Lines(int count)
        {
            return count == 1
                ? "1 line"
                : count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " lines";
        }

        /// <summary>
        /// Makes the directory the file goes in, and sets the file aside once it has reached
        /// <see cref="RollAt"/>, keeping one previous file. Called under the lock.
        /// </summary>
        /// <remarks>
        /// A directory that cannot be made throws, and the line is lost as it is for any write that
        /// fails, the next being tried again. Setting the file aside is an attempt of its own, as
        /// the audit's is: it bounds the file and is no condition for writing to it, and a viewer
        /// holding the previous file, which is how anything following a log holds it, would
        /// otherwise end the log for the rest of the session. It is tried again at the next line.
        ///
        /// **And it goes by what the ACLs allow, as the audit's does**, through
        /// `PluginFiles.SetAside`. The log is beside the audit by default, so a directory sealed
        /// against adding files for an append-only audit holds the log too. There a process with
        /// the backup and restore privileges enabled set the log aside and then could not make the
        /// next file, which gave the log up for the session; and wherever the log's own ACL lets
        /// it be removed, any process lost the previous file to the removal before the rename was
        /// refused.
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

                PluginFiles.SetAside(_filePath);
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
                // codeql[cs/catch-of-all-exceptions] the log the catch-alls report to, which may not throw
                catch (Exception)
                {
                    // As above: there is nowhere left to report a logger that cannot report.
                }
            }
        }
    }
}
