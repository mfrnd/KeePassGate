using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>One open KeePass database, as the plugin's screens see it.</summary>
    public sealed class OpenDatabase : IGateDatabase
    {
        private readonly PwDatabase _database;
        private readonly Action _changed;
        private readonly DatabaseWriter _writer;
        private readonly IGateLog _log;

        public OpenDatabase(PwDatabase database)
            : this(database, null)
        {
        }

        /// <summary>
        /// The same, and able to tell the host that the database has changed.
        /// </summary>
        /// <param name="changed">
        /// Run after the database is marked, to let the host say so on screen. Optional, because
        /// everything above this interface is testable without a window to repaint.
        /// </param>
        public OpenDatabase(PwDatabase database, Action changed)
            : this(database, changed, null, null)
        {
        }

        /// <summary>The same, and able to write into the plugin's store through the writer.</summary>
        /// <param name="writer">
        /// What every write into the store goes through, or null for a database the plugin only
        /// reads: a write is then refused.
        /// </param>
        public OpenDatabase(PwDatabase database, Action changed, DatabaseWriter writer, IGateLog log)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
            _changed = changed;
            _writer = writer;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>
        /// The plugin's store in this database, read afresh and written through the writer's
        /// questions, `STORE.md` §627, and refused where this was given no writer.
        /// </summary>
        /// <remarks>
        /// Built on each read rather than remembered, as the custom data store is, so a database
        /// locked and opened again is read as it is now.
        /// </remarks>
        public IGateStore Store
        {
            get
            {
                var holding = _writer == null ? null : new Func<PwEntry, string>(_writer.Holding);
                return new Through(_database, new KdbxStore(_database, GateVersion.Current, _log, holding), _writer);
            }
        }

        public bool IsOpen
        {
            get { return _database.IsOpen && _database.RootGroup != null; }
        }

        /// <summary>
        /// What KeePass calls this database: the name it was given, or the file it came from when
        /// it was given none. A screen naming a database by its fingerprint alone is asking the
        /// operator to recognize sixteen hexadecimal characters.
        /// </summary>
        /// <remarks>
        /// The file's name as KeePass takes it, through `UrlUtil`, as every other place naming a
        /// database does. `System.IO.Path` refuses a character Windows forbids in a path, and a URL
        /// KeePass opens over WebDAV or FTP may hold one: the name threw, and the authority chooser
        /// asking about that database failed as an incident without asking anybody.
        /// </remarks>
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_database.Name))
                {
                    return _database.Name;
                }

                var path = _database.IOConnectionInfo == null ? null : _database.IOConnectionInfo.Path;
                return string.IsNullOrEmpty(path) ? string.Empty : KeePassLib.Utility.UrlUtil.GetFileName(path);
            }
        }

        /// <summary>Every group and entry, for the one count that is not about a single node.</summary>
        public Acl.IRuledNodes Nodes
        {
            get { return new DatabaseNodes(_database); }
        }

        /// <summary>The database's own custom data, as a store over `PwDatabase.CustomData`.</summary>
        /// <remarks>
        /// Built on each read rather than remembered, which costs nothing and keeps this honest
        /// about a database that is locked and opened again.
        /// </remarks>
        public IKeyValueStore PluginCustomData
        {
            get { return new CustomDataStore(_database); }
        }

        /// <summary>
        /// Record that the database has changed, and let the host show it.
        /// </summary>
        /// <remarks>
        /// **Setting the flag is not enough, and the gap is the whole defect.** KeePass paints its
        /// title bar and its toolbar from what it last knew, so a database this plugin marked
        /// looked exactly like one with nothing to save. An operator reading that window
        /// reasonably concludes the change is already on disk, and closes without saving.
        ///
        /// This still does not save. What it does is stop the window from disagreeing with the
        /// database behind it, which is what makes the notice on the tab worth reading.
        /// </remarks>
        public void MarkModified()
        {
            _database.Modified = true;

            if (_changed != null)
            {
                _changed();
            }
        }
    }

    /// <summary>
    /// A database's store, read as it stands and written only through the writer, which asks
    /// first what every write asks, `STORE.md` §627.
    /// </summary>
    internal sealed class Through : IGateStore
    {
        private readonly PwDatabase _database;
        private readonly KdbxStore _store;
        private readonly DatabaseWriter _writer;

        public Through(PwDatabase database, KdbxStore store, DatabaseWriter writer)
        {
            _database = database;
            _store = store;
            _writer = writer;
        }

        public StoreReading Read()
        {
            return _store.Read();
        }

        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            return _writer == null
                ? StoreWritten.Refused(NoWriter)
                : _writer.ByOperator(_database, () => _store.Write(acts));
        }

        /// <summary>Why the writer refuses every write of the operator's in this session, or null.</summary>
        public string Unwritable
        {
            get { return _writer == null ? NoWriter : _writer.Unwritable; }
        }

        private const string NoWriter = "this database is open without a way to write to it";
    }

    /// <summary>
    /// Finds the database behind KeePass's database settings dialog.
    /// </summary>
    /// <remarks>
    /// Through reflection, deliberately. The plugin then degrades to "no database" on a KeePass
    /// that has renamed the property, rather than failing to load at all, and the host tests assert
    /// the name against the KeePass actually installed so the degradation is never a surprise.
    /// </remarks>
    public sealed class DialogDatabases : IGateDatabaseSource
    {
        private readonly IGateLog _log;
        private readonly Action _changed;
        private readonly DatabaseWriter _writer;

        public DialogDatabases(IGateLog log)
            : this(log, null, null)
        {
        }

        /// <summary>
        /// The same, and finding databases able to tell the host that they have changed, and to
        /// write into their stores through the writer.
        /// </summary>
        /// <param name="changed">Handed to every database found, so a change shows on screen.</param>
        /// <param name="writer">What every write into a database's store goes through, or null.</param>
        public DialogDatabases(IGateLog log, Action changed, DatabaseWriter writer)
        {
            _log = log ?? NullGateLog.Instance;
            _changed = changed;
            _writer = writer;
        }

        public IGateDatabase ForDialog(Form dialog)
        {
            if (dialog == null)
            {
                return null;
            }

            try
            {
                var property = dialog.GetType().GetProperty(
                    KeePassContract.DatabaseSettingsFormDatabaseProperty,
                    BindingFlags.Public | BindingFlags.Instance);

                if (property == null)
                {
                    _log.Warn("the settings dialog has no " +
                              KeePassContract.DatabaseSettingsFormDatabaseProperty + " property");
                    return null;
                }

                var database = property.GetValue(dialog, null) as PwDatabase;
                if (database == null)
                {
                    _log.Warn("the settings dialog is not editing a database");
                    return null;
                }

                return new OpenDatabase(database, _changed, _writer, _log);
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the dialog's database could not be reached", error);
                return null;
            }
        }
    }
}
