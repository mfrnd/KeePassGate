using System;
using System.Reflection;
using System.Windows.Forms;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>One open KeePass database, as the plugin's screens see it.</summary>
    public sealed class OpenDatabase : IGateDatabase
    {
        private readonly PwDatabase _database;
        private readonly Action _changed;

        public OpenDatabase(PwDatabase database)
            : this(database, null)
        {
        }

        /// <param name="changed">
        /// Run after the database is marked, to let the host say so on screen. Optional, because
        /// everything above this interface is testable without a window to repaint.
        /// </param>
        public OpenDatabase(PwDatabase database, Action changed)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
            _changed = changed;
        }

        public bool IsOpen
        {
            get { return _database.IsOpen && _database.RootGroup != null; }
        }

        /// <remarks>
        /// Built on each read rather than remembered. A database that is locked and opened again
        /// comes back with a different root group, and a store held from construction would go on
        /// writing into the one that was replaced.
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

        public DialogDatabases(IGateLog log)
            : this(log, null)
        {
        }

        /// <param name="changed">Handed to every database found, so a change shows on screen.</param>
        public DialogDatabases(IGateLog log, Action changed)
        {
            _log = log ?? NullGateLog.Instance;
            _changed = changed;
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

                return new OpenDatabase(database, _changed);
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the dialog's database could not be reached", error);
                return null;
            }
        }
    }
}
