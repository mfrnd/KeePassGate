using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassLib;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Asks the operator which authority to keep, the first time the plugin sees a database holding
    /// more than one.
    /// </summary>
    /// <remarks>
    /// **When the plugin first sees it**: a database opened or unlocked, and a save, which is how
    /// KeePass ends a synchronize. Each of those is an event on KeePass's own thread, and the
    /// question is posted from there rather than asked there, so that it arrives once KeePass has
    /// finished what it was doing: a modal box raised inside an open or a synchronize would sit in
    /// the middle of an operation that has not finished writing its own state. A connection never
    /// raises it. One that meets the state answers `503 undecided` and nothing more.
    ///
    /// **Once per database at a time, and once per state.** `OpenDatabaseTargets.Unasked` marks a
    /// state as asked when it hands the database out, so a Cancel is not answered by the same
    /// question a moment later; and a database already being asked about is not asked twice at
    /// once. Cancel serves nothing, and the tab's authority page is where the question can be
    /// asked again.
    ///
    /// **A choice goes through `DatabaseWriter`**, whose guard refuses it while Database
    /// Maintenance is open, and marks the database modified without saving it.
    /// </remarks>
    public sealed class AuthorityChooser : IDisposable
    {
        /// <summary>How long to wait before asking again while KeePass is busy with something.</summary>
        public static readonly TimeSpan Retry = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How long a choice the writer refused is tried again before the operator is told to
        /// choose again.
        /// </summary>
        /// <remarks>
        /// The operator's ruling of 2026-10-14. The usual refusal is another client's write holding
        /// the writer, or KeePass part way through a save, both of which end in a moment, and
        /// telling the operator at once that their answer was not kept asked them to come back
        /// and give it again for a busy spell they could not see. A few seconds covers those;
        /// Database Maintenance left open does not end by itself, and is told after them.
        /// </remarks>
        public static readonly TimeSpan KeepTrying = TimeSpan.FromSeconds(3);

        /// <summary>How long between two tries at keeping a choice.</summary>
        public static readonly TimeSpan KeepEvery = TimeSpan.FromMilliseconds(250);

        private readonly IDatabaseEvents _events;
        private readonly OpenDatabaseTargets _targets;
        private readonly DatabaseWriter _writer;
        private readonly Control _owner;
        private readonly Func<string> _busy;
        private readonly Func<string> _auditPath;
        private readonly IOperatorPrompt _prompt;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;
        private readonly HashSet<PwDatabase> _asking = new HashSet<PwDatabase>();

        private Timer _later;
        private bool _started;

        public AuthorityChooser(
            IDatabaseEvents events,
            OpenDatabaseTargets targets,
            DatabaseWriter writer,
            Control owner,
            Func<string> busy,
            Func<string> auditPath,
            IOperatorPrompt prompt,
            GateIncidents incidents,
            IGateLog log)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            _events = events;
            _targets = targets;
            _writer = writer;
            _owner = owner;
            _busy = busy;
            _auditPath = auditPath;
            _prompt = prompt ?? new MessageBoxPrompt();
            _log = log ?? NullGateLog.Instance;
            AskWhichToKeep = ShowChooser;
            After = Shortly;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        public bool Start()
        {
            if (_started)
            {
                return true;
            }

            try
            {
                _events.Opened += OnChanged;
                _events.Saved += OnChanged;
                _started = true;
                return true;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the authority chooser could not watch for databases changing", error);
                return false;
            }
        }

        public void Dispose()
        {
            if (_started)
            {
                _started = false;
                _events.Opened -= OnChanged;
                _events.Saved -= OnChanged;
            }

            if (_later != null)
            {
                _later.Dispose();
                _later = null;
            }
        }

        /// <summary>
        /// Asks about every open database that holds several authorities and has not been asked
        /// about yet. On the user interface thread, and posted there rather than called from an
        /// event, which is what the remark above is about.
        /// </summary>
        public void AskAll()
        {
            var busy = Busy();
            if (busy != null)
            {
                // Asked again shortly rather than now, because a choice made now would be refused
                // for exactly this reason and the operator would be asked for nothing.
                _log.Info("the authority chooser waits, because " + busy);
                Later();
                return;
            }

            foreach (var database in _targets.Unasked())
            {
                Ask(database);
            }
        }

        /// <summary>
        /// Asks about one database, and writes the answer through the writer.
        /// </summary>
        /// <returns>What happened, or null when nothing was chosen or nothing was asked.</returns>
        public AuthorityChange Ask(PwDatabase database)
        {
            if (database == null || !database.IsOpen || !_asking.Add(database))
            {
                return null;
            }

            var trying = false;
            try
            {
                var chosen = Choose(database);
                if (chosen == null)
                {
                    _log.Info("the operator kept no authority, so the database is still not served");
                    return null;
                }

                var kept = _writer.Keep(database, chosen);
                if (!kept.Succeeded)
                {
                    // Tried again for a moment rather than told at once: the writer refuses while
                    // another write holds it or KeePass is saving, and neither is something the
                    // operator should have to choose again for. The database stays marked as being
                    // asked about, so nothing asks it again in the meantime.
                    _log.Info("the authority the operator chose could not be kept yet, so it is tried again " +
                              "for a moment: " + kept.Message);
                    trying = true;
                    TryAgain(database, chosen, kept, System.Diagnostics.Stopwatch.StartNew());
                }

                return kept;
            }
            finally
            {
                if (!trying)
                {
                    _asking.Remove(database);
                }
            }
        }

        /// <summary>
        /// Runs some work on KeePass's thread once a delay has passed. Settable so that a test can
        /// run it where it stands rather than wait for a timer nobody pumps.
        /// </summary>
        internal Action<TimeSpan, Action> After { get; set; }

        /// <summary>
        /// Tries once more to keep the authority the operator chose, until it is kept or
        /// <see cref="KeepTrying"/> has passed, and then tells the operator.
        /// </summary>
        private void TryAgain(
            PwDatabase database, string chosen, AuthorityChange last, System.Diagnostics.Stopwatch trying)
        {
            if (trying.Elapsed >= KeepTrying || !database.IsOpen)
            {
                NotKept(database, last);
                return;
            }

            After(KeepEvery, () =>
            {
                var kept = database.IsOpen ? _writer.Keep(database, chosen) : last;
                if (kept.Succeeded)
                {
                    _log.Info("the authority the operator chose was kept once the writer was free");
                    _asking.Remove(database);
                    return;
                }

                TryAgain(database, chosen, kept, trying);
            });
        }

        /// <summary>
        /// Tells the operator their choice was not kept, and lets the database be asked about again.
        /// </summary>
        private void NotKept(PwDatabase database, AuthorityChange kept)
        {
            _asking.Remove(database);
            _log.Warn("the authority the operator chose could not be kept: " + kept.Message);
            _prompt.Tell(
                GateName.Full,
                "The authority was not kept: " + kept.Message + Environment.NewLine +
                Environment.NewLine + "Nothing is served for this database until one is. " +
                "Choose again from the " + GateName.OnATab + " tab of the database settings.");
        }

        /// <summary>A one-shot timer on KeePass's thread, whose tick goes through `Unattended`.</summary>
        private void Shortly(TimeSpan delay, Action work)
        {
            var timer = new Timer { Interval = Math.Max(1, (int)delay.TotalMilliseconds) };
            timer.Tick += delegate
            {
                timer.Dispose();
                try
                {
                    Unattended.Post(_owner, work, error => _incidents.Record("an authority could not be kept", error));
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("an authority the operator chose could not be tried again", error);
                }
            };
            timer.Start();
        }

        /// <summary>The digest the operator kept, or null.</summary>
        private string Choose(PwDatabase database)
        {
            var store = new CustomDataStore(database);

            AuthorityChoice choice;
            using (var reading = new AuthorityStore(store, _log).Read())
            {
                if (reading.Presence != AuthorityPresence.Undecided)
                {
                    // Decided since it was noticed, by the tab or by another save.
                    return null;
                }

                choice = AuthorityChoice.From(
                    reading, new AdmissionStore(store, _log).All(), LastSeen, _log);
            }

            return AskWhichToKeep(choice, new OpenDatabase(database).Name);
        }

        /// <summary>
        /// Shows the dialog and answers with the digest kept, or null. Settable so that a test can
        /// answer for the operator rather than put up a modal window nobody is there to close.
        /// </summary>
        internal Func<AuthorityChoice, string, string> AskWhichToKeep { get; set; }

        private string ShowChooser(AuthorityChoice choice, string databaseName)
        {
            using (var form = new AuthorityChooserForm(choice, databaseName))
            {
                form.ShowDialog(_owner);
                return form.Chosen;
            }
        }

        private IDictionary<string, DateTimeOffset> LastSeen(ICollection<string> clients)
        {
            return JsonLinesAudit.LastSeen(_auditPath == null ? null : _auditPath(), clients);
        }

        private string Busy()
        {
            if (_busy == null)
            {
                return null;
            }

            try
            {
                return _busy();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                return "KeePass could not be asked whether it is in the middle of something: " + error.Message;
            }
        }

        /// <summary>Asks again once, a moment from now.</summary>
        private void Later()
        {
            if (_later != null)
            {
                return;
            }

            _later = new Timer { Interval = (int)Retry.TotalMilliseconds };
            _later.Tick += delegate
            {
                var timer = _later;
                _later = null;
                timer.Dispose();

                // Posted rather than run, so that the one catch that has to cover everything is
                // the one `Unattended` already carries, rather than a second copy in a tick.
                OnChanged(this, EventArgs.Empty);
            };
            _later.Start();
        }

        private void OnChanged(object sender, EventArgs e)
        {
            try
            {
                Unattended.Post(_owner, AskAll, error => _incidents.Record("the authority chooser could not ask", error));
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the authority chooser could not be posted", error);
            }
        }
    }
}
