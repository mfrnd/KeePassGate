using System;
using System.IO;
using System.Text;
using KeePassGate.Diagnostics;
using IoPath = System.IO.Path;

namespace KeePassGate.Audit
{
    /// <summary>
    /// The audit as a file of JSON lines, outside the database.
    /// </summary>
    /// <remarks>
    /// **Outside the database on purpose.** A record of what an agent reached, kept inside the
    /// thing it reached, is a record that whoever compromised the database can edit. It is also a
    /// record nobody can read while KeePass is locked, which is exactly when somebody wants to
    /// read it.
    ///
    /// **A failed write is reported, never fatal.** A full disk, a file somebody opened
    /// exclusively, a directory that has gone away: none of those may stop the gate answering,
    /// because a plugin that refused every request when it could not write a log would be a denial
    /// of service anybody could arrange. A failure is logged when it begins and the decisions it
    /// costs are counted, so one broken path does not turn into a line of noise per request.
    ///
    /// **And the end of one is logged too, because not every failure lasts.** A program reading
    /// the file without letting anybody write to it stops every append for exactly as long as it
    /// holds the file, and then the next one works. So the first line written after a failure
    /// says how many decisions went unrecorded in between, and the next failure is reported in its
    /// turn rather than lost behind the first: reporting only ever once said that nothing would be
    /// recorded from then on, which was false as soon as the reader let go, and it left a later
    /// failure that did last, a full disk or a directory taken away, with nothing said at all. The
    /// same holds for setting the file aside.
    ///
    /// **It rolls at a size rather than growing without end.** One previous file is kept, which is
    /// the smallest thing that stops an unbounded file on somebody's disk while leaving the recent
    /// past readable. Anybody who needs more than that is shipping the lines somewhere else, and
    /// this is a line-per-decision format precisely so that they can. A bound by size is one the
    /// caller can spend, so how fast one client's lines arrive is held down before they get here:
    /// see `DecisionPace`.
    ///
    /// **Where it is writing is said as it opens**, and so is having nowhere to write. See the
    /// constructor.
    /// </remarks>
    public sealed class JsonLinesAudit : IGateAudit
    {
        /// <summary>
        /// Names the file to append to. Read from the environment, like the plugin log, so that a
        /// test can put it somewhere of its own without writing to the host's settings.
        /// </summary>
        public const string AuditFileVariable = "KEEPASSGATE_AUDIT_FILE";

        /// <summary>What the file is called when nothing names one.</summary>
        public const string DefaultFileName = "audit.jsonl";

        /// <summary>
        /// How large the file grows before the last one is set aside. Eight megabytes is tens of
        /// thousands of decisions, and small enough that a text editor still opens it.
        /// </summary>
        public const long RollAt = 8 * 1024 * 1024;

        private readonly object _fileLock = new object();
        private readonly string _path;
        private readonly IGateLog _log;

        private bool _reported;
        private bool _rollReported;
        private bool _shapeReported;
        private int _lost;
        private int _unrecorded;

        public JsonLinesAudit(IGateLog log)
            : this(DefaultPath(), log)
        {
        }

        /// <summary>
        /// Opens the audit, and says in the plugin's log where it is writing.
        /// </summary>
        /// <remarks>
        /// **Said once, as the plugin loads, rather than left to be worked out.** Nothing on a
        /// screen names this file, and the operator's page for it can only give the default; a
        /// machine where `KEEPASSGATE_AUDIT_FILE` is set, or where the default resolved to
        /// somewhere else, is a machine where the only way to find the record of what an agent
        /// reached was to guess at it.
        ///
        /// **And having nowhere is said out loud**, at a level that means something went wrong,
        /// because that is the case worth hearing about: `Record` returns at once, `Lost` stays
        /// zero, the gate answers every request as usual, and not one decision is written down.
        /// An audit that is not being written and an audit with nothing in it look the same in
        /// the place somebody would look, which is the file that is not there.
        /// </remarks>
        public JsonLinesAudit(string path, IGateLog log)
        {
            _path = string.IsNullOrEmpty(path) ? null : path;
            _log = log ?? NullGateLog.Instance;

            if (_path == null)
            {
                _log.Error(
                    "no audit file could be named, so no decision this session makes is being " +
                    "recorded. Set " + AuditFileVariable + " to the full path of a file and " +
                    "restart KeePass.", null);
                return;
            }

            _log.Info("decisions are being recorded to " + _path);
        }

        /// <summary>Where this is writing, or null when nowhere is. The log says it too.</summary>
        public string Path
        {
            get { return _path; }
        }

        /// <summary>How many decisions could not be written down. Zero, on a working machine.</summary>
        public int Lost
        {
            get
            {
                lock (_fileLock)
                {
                    return _lost;
                }
            }
        }

        /// <summary>
        /// The file named by the environment, made absolute, or one under this user's own
        /// application data.
        /// </summary>
        /// <remarks>
        /// Not beside the `.kdbx`. A database is often on a share or in somebody's cloud folder,
        /// and a record of which agent read which entry does not belong in either.
        ///
        /// `PluginFiles` decides where, for the plugin's log as well, and says why a name that is
        /// not a full path is made into one.
        /// </remarks>
        public static string DefaultPath()
        {
            return PluginFiles.Resolve(AuditFileVariable, DefaultFileName);
        }

        public void Record(AuditEntry entry)
        {
            if (entry == null || _path == null)
            {
                return;
            }

            lock (_fileLock)
            {
                string line;

                try
                {
                    line = entry.Line();
                }
                catch (Exception error)
                {
                    // Deliberately everything, and for the reason the log gives about its own
                    // formatting: an audit that added an exception of its own would fail the very
                    // request it exists to watch passively. Shaping the line inside the storage
                    // filter below was worse than that. `ArgumentOutOfRangeException` derives
                    // from `ArgumentException`, so a bad moment was caught there and reported as a
                    // file that could not be written, naming a path that was never the trouble.
                    _lost++;

                    if (!_shapeReported)
                    {
                        _shapeReported = true;
                        _log.Error("an audit line could not be written down, and is lost", error);
                    }

                    return;
                }

                try
                {
                    Roll();

                    if (_rollReported)
                    {
                        _rollReported = false;
                        _log.Info("the audit at " + _path + " is within its bound again");
                    }
                }
                catch (Exception error) when (Expected.OfStorage(error))
                {
                    // Its own attempt, because rotation bounds the file and is not a precondition
                    // for writing to it. Sharing the append's `try` meant that once the file passed
                    // `RollAt`, anything holding the previous file stopped every later decision
                    // being recorded, on a file the append itself would have succeeded on. A reader
                    // tailing it counts, and so does a backup. Of the two failures, an audit that
                    // grows past its bound is the one worth having, so it is the one taken.
                    if (!_rollReported)
                    {
                        _rollReported = true;
                        _log.Error("the audit at " + _path + " could not be set aside, so it grows" +
                                   " past its bound until it can be", error);
                    }
                }

                try
                {
                    var directory = IoPath.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception error) when (Expected.OfStorage(error))
                {
                    _lost++;
                    _unrecorded++;

                    if (!_reported)
                    {
                        _reported = true;
                        _log.Error("the audit could not be written to " + _path +
                                   ", so decisions are not being recorded until it can be", error);
                    }

                    return;
                }

                if (_reported)
                {
                    // Written again, which a sharing violation is the moment whoever held the file
                    // lets go of it. Said with what it cost, and the next failure is reported in
                    // its own turn rather than swallowed by this one.
                    _reported = false;
                    _log.Warn("the audit at " + _path + " is being written again, and " +
                              Decisions(_unrecorded) + " made while it could not be are not in it");
                    _unrecorded = 0;
                }
            }
        }

        private void Roll()
        {
            var file = new FileInfo(_path);
            if (!file.Exists || file.Length < RollAt)
            {
                return;
            }

            var previous = _path + ".1";
            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            File.Move(_path, previous);
        }

        private static string Decisions(int count)
        {
            return count == 1
                ? "1 decision"
                : count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " decisions";
        }
    }
}
