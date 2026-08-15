using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using KeePassLib;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Which groups have a KeePass group dialog open on them right now.
    /// </summary>
    /// <remarks>
    /// The gate against the trap that shaped this whole design. KeePass's group dialog copies a
    /// group's custom data when it opens and writes that copy back over the group on OK, so an
    /// admission record written while one is open is destroyed the moment the operator presses OK,
    /// silently and completely. Pairing refuses while one is open rather than writing into a group
    /// that is about to be overwritten.
    ///
    /// **Specific dialogs are tracked, never a count**, and `PAIRING.md` §307 is explicit about
    /// it. A count answers a different question from the one asked here, which is per node: a group
    /// dialog open on some other group must not refuse a write to this one, and
    /// `GlobalWindowManager.WindowCount` includes every window KeePass registers, nearly none of
    /// which copies anything. It would also leave this guard at the mercy of what the plugin's own
    /// dialogs register: the pairing approval dialog does not add itself to that manager, and it is
    /// the one window certain to be open while a pairing is being written. TODO(step 8): the
    /// approval dialog arrives with operator approval.
    ///
    /// The group behind a dialog is read by reflection, so a KeePass that renames the field leaves
    /// this refusing every write while any group dialog is open rather than allowing one it should
    /// not. That is the fail-closed direction, and the host tests pin the name so the degradation is
    /// never a surprise.
    /// </remarks>
    public sealed class GroupDialogs : IDisposable
    {
        private readonly IWindowEventSource _windows;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;
        private readonly object _lock = new object();
        private readonly Dictionary<Form, PwGroup> _open = new Dictionary<Form, PwGroup>();
        private readonly Dictionary<Form, PwDatabase> _openMaintenance = new Dictionary<Form, PwDatabase>();

        private bool _started;

        public GroupDialogs(IWindowEventSource windows, IGateLog log)
            : this(windows, null, log)
        {
        }

        public GroupDialogs(IWindowEventSource windows, GateIncidents incidents, IGateLog log)
        {
            if (windows == null)
            {
                throw new ArgumentNullException(nameof(windows));
            }

            _windows = windows;
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        public bool Start()
        {
            lock (_lock)
            {
                if (_started)
                {
                    return true;
                }

                try
                {
                    _windows.WindowAdded += OnWindowAdded;
                    _windows.WindowRemoved += OnWindowRemoved;
                    _started = true;
                    return true;
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("the group dialogs could not be watched", error);
                    return false;
                }
            }
        }

        /// <summary>
        /// Why nothing may be written to this group right now, or null when something may be.
        /// </summary>
        public string Blocking(PwGroup group)
        {
            if (group == null)
            {
                return null;
            }

            lock (_lock)
            {
                if (!_started)
                {
                    // Not watching, so an empty table means nothing was noticed rather than that
                    // nothing is open. Whichever it is, this cannot tell, and the whole reason this
                    // class exists is that writing into a group a dialog is holding a stale copy of
                    // destroys the record on OK without a word. Refusing costs a pairing attempt;
                    // guessing costs a certificate nobody can revoke.
                    return "the group dialogs are not being watched";
                }

                foreach (var pair in _open)
                {
                    if (pair.Value == null)
                    {
                        // A group dialog whose group could not be read. It might be this one, so
                        // it is treated as though it were.
                        return "a group dialog is open, and which group it is editing could not be read";
                    }

                    if (ReferenceEquals(pair.Value, group))
                    {
                        return "a group dialog is open on the group this would be written to";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Why no admission record may be written to this database right now, or null when one may.
        /// </summary>
        /// <remarks>
        /// The maintenance dialog is to the database's own custom data what the group dialog is to
        /// a group's: it copies the dictionary when it opens and puts the copy back when it closes.
        /// Records live in that dictionary, so a pairing that lands while one is open is a
        /// certificate issued against a record the dialog then destroys, which is the state this
        /// whole class exists to prevent.
        /// </remarks>
        public string BlockingRecords(PwDatabase database)
        {
            if (database == null)
            {
                return null;
            }

            lock (_lock)
            {
                if (!_started)
                {
                    return "the dialogs are not being watched";
                }

                foreach (var pair in _openMaintenance)
                {
                    if (pair.Value == null)
                    {
                        return "the database maintenance dialog is open, and which database it is working on could not be read";
                    }

                    if (ReferenceEquals(pair.Value, database))
                    {
                        return "the database maintenance dialog is open on this database";
                    }
                }
            }

            return null;
        }

        /// <summary>How many are open, for a test and for a log line.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _open.Count;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_started)
                {
                    try
                    {
                        _windows.WindowAdded -= OnWindowAdded;
                        _windows.WindowRemoved -= OnWindowRemoved;
                    }
                    catch (Exception error) when (Expected.OfUserInterface(error))
                    {
                        _log.Error("the group dialog watch could not be released", error);
                    }

                    _started = false;
                }

                _open.Clear();
                _openMaintenance.Clear();
            }
        }

        private void OnWindowAdded(object sender, GateWindowEventArgs e)
        {
            // On the UI thread, inside KeePass. Nothing here may throw.
            try
            {
                var form = e == null ? null : e.Form;
                if (form == null || (!IsGroupDialog(form) && !IsMaintenanceDialog(form)))
                {
                    return;
                }


                if (IsMaintenanceDialog(form))
                {
                    var maintained = DatabaseBehind(form);

                    lock (_lock)
                    {
                        _openMaintenance[form] = maintained;
                    }

                    return;
                }


                var group = GroupBehind(form);

                lock (_lock)
                {
                    _open[form] = group;
                }
            }
            catch (Exception error)
            {
                // One of the catch-alls, and this is why: it runs on KeePass's own thread,
                // from KeePass's own event, and an exception that escapes here ends the password
                // manager. What it caught is reported as an incident rather than as a log line.
                _incidents.Record("a group dialog opening could not be noted", error);
            }
        }

        private void OnWindowRemoved(object sender, GateWindowEventArgs e)
        {
            try
            {
                var form = e == null ? null : e.Form;
                if (form == null)
                {
                    return;
                }

                lock (_lock)
                {
                    _open.Remove(form);
                    _openMaintenance.Remove(form);
                }
            }
            catch (Exception error)
            {
                // As above: a KeePass event handler may not throw.
                _incidents.Record("a group dialog closing could not be noted", error);
            }
        }

        private static bool IsGroupDialog(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.GroupFormTypeName, StringComparison.Ordinal);
        }


        private static bool IsMaintenanceDialog(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.MaintenanceFormTypeName, StringComparison.Ordinal);
        }

        /// <summary>Null when it cannot be read, which is taken to mean any group at all.</summary>
        private PwGroup GroupBehind(Form form)
        {
            try
            {
                var field = form.GetType().GetField(
                    KeePassContract.GroupFormGroupField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    _log.Warn("a group dialog no longer names its group in " +
                              KeePassContract.GroupFormGroupField + ", so pairing is refused while one is open");
                    return null;
                }

                return field.GetValue(form) as PwGroup;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the group behind a dialog could not be read", error);
                return null;
            }
        }


        /// <summary>Null when it cannot be read, which is taken to mean any database at all.</summary>
        private PwDatabase DatabaseBehind(Form form)
        {
            try
            {
                var field = form.GetType().GetField(
                    KeePassContract.MaintenanceFormDatabaseField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    _log.Warn("the maintenance dialog no longer names its database in " +
                              KeePassContract.MaintenanceFormDatabaseField +
                              ", so pairing is refused while one is open");
                    return null;
                }

                return field.GetValue(form) as PwDatabase;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the database behind the maintenance dialog could not be read", error);
                return null;
            }
        }
    }
}
