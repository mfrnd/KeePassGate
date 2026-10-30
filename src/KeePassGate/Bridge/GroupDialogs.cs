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
    /// Which groups and entries have a KeePass dialog open on them right now.
    /// </summary>
    /// <remarks>
    /// The gate against the trap that shaped this whole design. KeePass's group dialog copies a
    /// group's properties and custom data when it opens and writes that copy back over the group
    /// on OK, so a change made to that group while one is open is destroyed the moment the operator
    /// presses OK, silently and completely. A write through the gate is refused while one is open
    /// on the group it would change, rather than written into a group that is about to be
    /// overwritten.
    ///
    /// **The entry dialog does the same thing to an entry's strings**, which is what brought it in
    /// here when the API arrived. A field written through the gate while its entry is open on
    /// screen would be destroyed by that dialog's OK exactly as a group's name would, so the same
    /// refusal covers both, per node rather than wholesale.
    ///
    /// **Three dialogs are about a group whose place in the tree is still being decided**, and a
    /// node the dialog holds cannot say so. Add Entry and the TAN wizard fill a group on OK that
    /// they hold nothing of, which `BlockingRemoval` answers, and Add Group has put a group into
    /// the tree that its Cancel takes back out, which `BlockingAdditions` answers.
    ///
    /// **The database settings dialog is here for one thing only**, and it is not custom data:
    /// that dialog copies nothing of the kind, which is what makes the KPG tab's own buttons safe
    /// on it. What it does copy is the recycle bin choice, so a removal that makes a database its
    /// first bin while one is open is undone by that dialog's OK. `BlockingRecycleBin` is that
    /// question and nothing else asks it.
    ///
    /// **KeePass's progress window is here for a different reason**: it holds nothing, and says
    /// that KeePass is part way through an operation that pumps messages from inside the live tree.
    /// `Working` is that question, and the writer asks it beside KeePass's own two busy signals,
    /// because XML Replace raises neither of them while it merges into the database.
    ///
    /// **Specific dialogs are tracked, never a count**, and `PAIRING.md` §307 is explicit about
    /// it. A count answers a different question from the one asked here, which is per node: a group
    /// dialog open on some other group must not refuse a write to this one, and
    /// `GlobalWindowManager.WindowCount` includes every window KeePass registers, nearly none of
    /// which copies anything. It would also leave this guard at the mercy of what the plugin's own
    /// dialogs register: the pairing approval dialog does not add itself to that manager, and it is
    /// the one window certain to be open while a pairing is being written.
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
        private readonly HashSet<Form> _creating = new HashSet<Form>();
        private readonly Dictionary<Form, PwGroup> _filling = new Dictionary<Form, PwGroup>();
        private readonly Dictionary<Form, PwEntry> _openEntries = new Dictionary<Form, PwEntry>();
        private readonly Dictionary<Form, PwDatabase> _openBulk = new Dictionary<Form, PwDatabase>();
        private readonly Dictionary<Form, PwDatabase> _openMaintenance = new Dictionary<Form, PwDatabase>();
        private readonly Dictionary<Form, PwDatabase> _openSettings = new Dictionary<Form, PwDatabase>();
        private readonly HashSet<Form> _progress = new HashSet<Form>();

        private readonly IReadOnlyList<string> _watched;

        private bool _started;

        public GroupDialogs(IWindowEventSource windows, IGateLog log)
            : this(windows, null, log)
        {
        }

        public GroupDialogs(IWindowEventSource windows, GateIncidents incidents, IGateLog log)
            : this(windows, incidents, log, Watched)
        {
        }

        /// <summary>The same, made to look for dialogs by other names, for a test.</summary>
        internal GroupDialogs(
            IWindowEventSource windows, GateIncidents incidents, IGateLog log, IReadOnlyList<string> watched)
        {
            if (windows == null)
            {
                throw new ArgumentNullException(nameof(windows));
            }

            _windows = windows;
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
            _watched = watched ?? Watched;
        }

        /// <summary>The dialogs this watches for, by the names KeePass gives their types.</summary>
        internal static readonly IReadOnlyList<string> Watched = new[]
        {
            KeePassContract.GroupFormTypeName,
            KeePassContract.EntryFormTypeName,
            KeePassContract.MaintenanceFormTypeName,
            KeePassContract.DatabaseSettingsFormTypeName,
            KeePassContract.TanWizardFormTypeName,
            KeePassContract.StatusProgressFormTypeName,
        };

        /// <summary>
        /// Starts watching, or answers false and watches nothing, which refuses every question
        /// this class answers.
        /// </summary>
        /// <remarks>
        /// **Every dialog is looked for in the KeePass that is loaded before anything is watched.**
        /// A window is recognized by the name of its type, and a KeePass that renamed one of them
        /// would open that dialog under a name nothing here compares against: it would never be
        /// recorded, every question about it would answer that nothing is open, and the count
        /// would say none while the watch said it was running. That is allowing exactly the write
        /// this class exists to refuse, on the day an upgrade makes it true and with nothing to
        /// say so. A name that is not there is therefore a watch that does not start, which is the
        /// answer this class already gives when it cannot tell. The host tests pin the names
        /// against the KeePass they build with; this is the same question asked of the one the
        /// plugin is running in.
        /// </remarks>
        public bool Start()
        {
            lock (_lock)
            {
                if (_started)
                {
                    return true;
                }

                var missing = Unrecognized(typeof(KeePass.Forms.MainForm).Assembly, _watched, _log);
                if (missing.Count > 0)
                {
                    _log.Error(
                        "this KeePass has no dialog called " + string.Join(", ", missing) +
                        ", so the dialogs are not watched and every write they would take back is refused",
                        null);
                    return false;
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
        /// The names in <paramref name="names"/> that <paramref name="keePass"/> has no type by.
        /// </summary>
        /// <remarks>
        /// A name that cannot be looked up is counted as missing, since the question is whether a
        /// dialog could arrive under it, and a lookup that failed cannot say that one would.
        /// </remarks>
        internal static IReadOnlyList<string> Unrecognized(Assembly keePass, IEnumerable<string> names, IGateLog log)
        {
            var missing = new List<string>();

            foreach (var name in names)
            {
                try
                {
                    if (keePass.GetType(name, false) != null)
                    {
                        continue;
                    }
                }
                catch (Exception error) when (Expected.OfReflection(error) || error is BadImageFormatException
                                              || error is System.IO.FileNotFoundException)
                {
                    (log ?? NullGateLog.Instance).Error("KeePass could not be asked for " + name, error);
                }

                missing.Add(name);
            }

            return missing;
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
        /// Why this group cannot be taken out of the tree right now, or null when it can.
        /// </summary>
        /// <remarks>
        /// **A dialog that adds an entry is about a group it does not hold.** KeePass's Add Entry
        /// names the group as the new entry's parent and puts the entry into it only on OK, so
        /// while the dialog is open the group is empty and no group dialog is open on it. Removed
        /// underneath, the operator's OK puts what they typed into a group in the recycle bin, or,
        /// with no bin, into a group that is no longer in the tree and never reaches the file.
        ///
        /// An entry dialog on an entry that is already there names its group the same way, and
        /// that costs nothing: a group holding an entry is not empty, and nothing removes one that
        /// is not.
        ///
        /// **The TAN wizard is the same case**: it is handed the selected group and adds one entry
        /// per TAN to it on OK, and a group made for TANs is empty until then.
        /// </remarks>
        public string BlockingRemoval(PwGroup group)
        {
            if (group == null)
            {
                return null;
            }

            lock (_lock)
            {
                if (!_started)
                {
                    return "the dialogs are not being watched";
                }

                foreach (var pair in _filling)
                {
                    if (pair.Value == null)
                    {
                        return "a dialog that adds to a group is open, and which group it adds to could not be read";
                    }

                    if (ReferenceEquals(pair.Value, group))
                    {
                        return "a dialog is open that adds to this group when it is accepted";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Why nothing may be put into this group right now, or null when something may.
        /// </summary>
        /// <remarks>
        /// **Asked only of a group that is about to receive something**, and answered only by Add
        /// Group. Edit Group holds no list of what is inside a group, so adding to one is safe and
        /// stays allowed. Add Group is the other case: KeePass puts the new group into the tree
        /// before the dialog opens and takes it back out on Cancel, with no deleted object and no
        /// recycle bin, so an entry created in it in between is answered as created and then
        /// dropped from the file by the next save.
        /// </remarks>
        public string BlockingAdditions(PwGroup group)
        {
            if (group == null)
            {
                return null;
            }

            lock (_lock)
            {
                if (!_started)
                {
                    return "the dialogs are not being watched";
                }

                foreach (var pair in _open)
                {
                    if (!_creating.Contains(pair.Key))
                    {
                        continue;
                    }

                    if (pair.Value == null)
                    {
                        return "an Add Group dialog is open, and which group it is adding could not be read";
                    }

                    if (ReferenceEquals(pair.Value, group))
                    {
                        return "an Add Group dialog is open on this group, and its Cancel takes the group "
                               + "away with whatever was put in it";
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

        /// <summary>
        /// Why KeePass is showing its progress window for an operation of its own, or null when it
        /// is not.
        /// </summary>
        /// <remarks>
        /// **The operation this is for is XML Replace**, under Tools then Database Tools. It
        /// serializes the live database, edits the copy, and merges the copy back with
        /// `PwDatabase.MergeIn(..., Synchronize, window)`, so the progress window is the status
        /// logger and its `ContinueWork`, which is `Application.DoEvents` on every call, is a pump
        /// on every step of the merge's own walk over the live tree. A write delivered there moves
        /// a list `ApplyDeletions` is enumerating, the merge throws, and KeePass shows a warning
        /// over a database the write's own save has already put on the disk half merged. KeePass
        /// neither blocks its own interaction for it nor raises the message count, so this window
        /// is the only thing that says it is running.
        ///
        /// **Any progress window, not only that one's**, because the window does not say what it
        /// is reporting on and every other KeePass operation that opens it already blocks
        /// interaction as well. So the only cost of asking it wholesale is a write refused during a
        /// manual update check, which is a retry, and a progress window KeePass adds to another
        /// operation later is refused on the day it arrives rather than on the day somebody
        /// notices.
        ///
        /// **Not per database**, since the window holds none. And a watch that is not running
        /// refuses, like every question here.
        /// </remarks>
        public string Working()
        {
            lock (_lock)
            {
                if (!_started)
                {
                    return "the dialogs are not being watched";
                }

                return _progress.Count == 0
                    ? null
                    : "KeePass is showing a progress window for an operation of its own, such as XML Replace";
            }
        }

        /// <summary>
        /// Why this database cannot be given its first recycle bin right now, or null when it can.
        /// </summary>
        /// <remarks>
        /// **Asked only where a bin would have to be made.** KeePass's database settings dialog
        /// fills its recycle bin combo from `RecycleBinUuid` when it loads and writes the choice
        /// back on OK, so a database that had no bin when the dialog opened has that dialog holding
        /// "none": a removal that makes the first bin underneath it is undone the moment the
        /// operator presses OK, and the group the removal moved things into stays under the root as
        /// an ordinary group that `Discarded` no longer recognizes. That is the relocation
        /// `DESIGN.md` §138 closes by putting the bin out of the API's reach, reopened by a dialog
        /// nobody had to touch.
        ///
        /// **A bin that was already there is safe**, because the dialog copied it and puts the same
        /// answer back, which is why the vault asks this only on the branch that would create one.
        /// </remarks>
        public string BlockingRecycleBin(PwDatabase database)
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

                foreach (var pair in _openSettings)
                {
                    if (pair.Value == null)
                    {
                        return "the database settings dialog is open, and which database it is editing could not be read";
                    }

                    if (ReferenceEquals(pair.Value, database))
                    {
                        return "the database settings dialog is open on this database, and its OK would "
                               + "take back a recycle bin made now";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Why an entry cannot be written right now, or null when it can.
        /// </summary>
        /// <remarks>
        /// The entry dialog hands its own copy of the entry's strings to its tabs and writes that
        /// copy back over the entry when it is accepted, so a field written underneath one is a
        /// field the operator's OK throws away.
        /// </remarks>
        public string Blocking(PwEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            lock (_lock)
            {
                if (!_started)
                {
                    // The same answer, for the same reason, as the group overload above: an empty
                    // table means nothing was noticed rather than that nothing is open, and this
                    // cannot tell which. Without it the two halves of one guard disagreed, and the
                    // sentence in `KeePassGateExt` promising that a watch which never started
                    // refuses every write was true of groups and false of entries.
                    return "the entry dialogs are not being watched";
                }

                foreach (var pair in _openEntries)
                {
                    if (pair.Value == null)
                    {
                        // An entry dialog whose entry could not be read. It might be this one.
                        return "an entry dialog is open, and which entry it is editing could not be read";
                    }

                    if (ReferenceEquals(pair.Value, entry))
                    {
                        return "an entry dialog is open on the entry this would be written to";
                    }
                }

                foreach (var pair in _openBulk)
                {
                    if (pair.Value == null)
                    {
                        return "an Edit Entries dialog is open, and which database it is editing could not be read";
                    }

                    if (Within(pair.Value, entry))
                    {
                        return "an Edit Entries dialog is open on entries of this database, and this may be one of them";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Whether the entry is in this database, or might be.
        /// </summary>
        /// <remarks>
        /// Answered by the tree rather than by a lookup, because a bulk dialog's selection is all
        /// one database's and the question is only which one. An entry in no group at all cannot
        /// be placed, so it is taken to be in every database, which is the refusing answer.
        /// </remarks>
        private static bool Within(PwDatabase database, PwEntry entry)
        {
            var top = entry.ParentGroup;
            if (top == null)
            {
                return true;
            }

            while (top.ParentGroup != null)
            {
                top = top.ParentGroup;
            }

            return ReferenceEquals(top, database.RootGroup);
        }

        /// <summary>
        /// How many of the dialogs this watches are open, of every kind, for a test and for a log
        /// line.
        /// </summary>
        /// <remarks>
        /// Nothing refuses on it. A count cannot say "I do not know", since a watch that never
        /// started counts zero exactly as an empty desktop does, so every refusal here asks
        /// `Blocking` or `BlockingRecords`, which fold that question into their answer. The
        /// database settings dialog is not in it: it carries no node's custom data, and what it
        /// does copy is asked about by `BlockingRecycleBin` instead.
        /// </remarks>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _open.Count + _openEntries.Count + _openBulk.Count + _openMaintenance.Count;
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

                // Both, because both are answered from. A watch that has been released still
                // reporting the entry dialogs it saw is a watch that refuses a profile deletion
                // for ever, since the count it gives includes them.
                _open.Clear();
                _creating.Clear();
                _filling.Clear();
                _openEntries.Clear();
                _openBulk.Clear();
                _openMaintenance.Clear();
                _openSettings.Clear();
                _progress.Clear();
            }
        }

        private void OnWindowAdded(object sender, GateWindowEventArgs e)
        {
            // On the UI thread, inside KeePass. Nothing here may throw.
            try
            {
                var form = e == null ? null : e.Form;
                if (form == null || (!IsGroupDialog(form) && !IsEntryDialog(form)
                                     && !IsMaintenanceDialog(form) && !IsSettingsDialog(form)
                                     && !IsTanWizard(form) && !IsProgressWindow(form)))
                {
                    return;
                }

                if (IsProgressWindow(form))
                {
                    // Possibly on another thread: KeePass shows it from one of its own while
                    // plugins load. The lock is all either handler needs, since nothing here
                    // touches the form.
                    lock (_lock)
                    {
                        _progress.Add(form);
                    }

                    return;
                }

                if (IsTanWizard(form))
                {
                    var filled = TanStorageBehind(form);

                    lock (_lock)
                    {
                        _filling[form] = filled;
                    }

                    return;
                }

                if (IsSettingsDialog(form))
                {
                    var edited = SettingsDatabaseBehind(form);

                    lock (_lock)
                    {
                        _openSettings[form] = edited;
                    }

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

                // Each kind of window above is a branch of its own that returns, so only a group
                // dialog or an entry dialog gets this far, and nothing asked inside this branch is
                // anything but a group dialog: a check for another kind placed in here never
                // matches, and that kind of window is never recorded.
                if (IsGroupDialog(form))
                {
                    var group = GroupBehind(form);
                    var creating = Creating(form);

                    lock (_lock)
                    {
                        _open[form] = group;

                        if (creating)
                        {
                            _creating.Add(form);
                        }
                    }

                    return;
                }

                var bulk = IsBulk(form);
                if (bulk != false)
                {
                    var selected = SelectionDatabaseBehind(form);

                    lock (_lock)
                    {
                        _openBulk[form] = selected;

                        // Which kind of entry dialog it is could not be told, so it is taken to
                        // be both: Add Entry fills a group on OK, and which one cannot be said.
                        if (bulk == null)
                        {
                            _filling[form] = null;
                        }
                    }

                    return;
                }

                var entry = EntryBehind(form);

                lock (_lock)
                {
                    _openEntries[form] = entry;

                    // Add Entry puts its entry into its parent on OK and not before. An entry that
                    // could not be read might be one of those, so it is filling any group; one
                    // with no parent is filling none.
                    if (entry == null)
                    {
                        _filling[form] = null;
                    }
                    else if (entry.ParentGroup != null)
                    {
                        _filling[form] = entry.ParentGroup;
                    }
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, one of the four catch-alls
            catch (Exception error)
            {
                // One of the four catch-alls, and this is why: it runs on KeePass's own thread,
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
                    _creating.Remove(form);
                    _filling.Remove(form);
                    _openEntries.Remove(form);
                    _openBulk.Remove(form);
                    _openMaintenance.Remove(form);
                    _openSettings.Remove(form);
                    _progress.Remove(form);
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, one of the four catch-alls
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

        private static bool IsEntryDialog(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.EntryFormTypeName, StringComparison.Ordinal);
        }

        private static bool IsMaintenanceDialog(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.MaintenanceFormTypeName, StringComparison.Ordinal);
        }

        private static bool IsTanWizard(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.TanWizardFormTypeName, StringComparison.Ordinal);
        }

        private static bool IsSettingsDialog(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.DatabaseSettingsFormTypeName, StringComparison.Ordinal);
        }

        private static bool IsProgressWindow(Form form)
        {
            return string.Equals(
                form.GetType().FullName, KeePassContract.StatusProgressFormTypeName, StringComparison.Ordinal);
        }

        /// <summary>Null when it cannot be read, which is taken to mean any database at all.</summary>
        /// <remarks>
        /// A public property rather than a field, and the same one the KPG tab reads to find out
        /// which database it is on.
        /// </remarks>
        private PwDatabase SettingsDatabaseBehind(Form form)
        {
            try
            {
                var property = form.GetType().GetProperty(
                    KeePassContract.DatabaseSettingsFormDatabaseProperty,
                    BindingFlags.Public | BindingFlags.Instance);

                if (property == null)
                {
                    _log.Warn("the settings dialog no longer names its database in " +
                              KeePassContract.DatabaseSettingsFormDatabaseProperty +
                              ", so a first recycle bin is refused while one is open");
                    return null;
                }

                return property.GetValue(form, null) as PwDatabase;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the database behind the settings dialog could not be read", error);
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

        /// <summary>
        /// Whether an entry dialog is KeePass's bulk `Edit Entries` rather than one entry's, or
        /// null when that cannot be told.
        /// </summary>
        /// <remarks>
        /// The entry behind such a dialog is a synthetic one it built by merging the selection, so
        /// it is reference-equal to none of the real entries and matching on it answers "nothing is
        /// open" for every one of them. What OK writes back is applied to the whole selection, and
        /// a key the merged copy does not carry is deleted from all of them, so a field written
        /// underneath is destroyed across every selected entry at once.
        ///
        /// A dialog whose merged context cannot be looked for is taken to be one, which refuses
        /// every entry of its database rather than none of them. **That includes a KeePass that has
        /// renamed the field**, which is not the same answer as the field holding nothing: read as
        /// nothing, every Edit Entries dialog would have been taken for one entry's, and the synthetic
        /// entry behind it refuses writes to no entry at all.
        /// </remarks>
        private bool? IsBulk(Form form)
        {
            try
            {
                var merged = form.GetType().GetField(
                    KeePassContract.EntryFormMultipleField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (merged == null)
                {
                    _log.Warn("an entry dialog no longer says whether it is about several entries in " +
                              KeePassContract.EntryFormMultipleField +
                              ", so every entry of its database is refused while one is open");
                    return null;
                }

                return merged.GetValue(form) != null;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("whether an entry dialog is editing several entries could not be read", error);
                return null;
            }
        }

        /// <summary>Null when it cannot be read, which is taken to mean any database at all.</summary>
        /// <remarks>
        /// **The database and not the selection.** The selection cannot be recovered from
        /// `m_pwEntry`, and a bulk dialog's selection is always one database's, so refusing every
        /// entry of that database is exact enough and refuses nothing anywhere else. Refusing every
        /// entry everywhere, which is what this used to do, turned one operator's bulk edit into a
        /// `503 busy` for every other database the gate serves.
        /// </remarks>
        private PwDatabase SelectionDatabaseBehind(Form form)
        {
            try
            {
                var field = form.GetType().GetField(
                    KeePassContract.NodeFormDatabaseField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    _log.Warn("an entry dialog no longer names its database in " +
                              KeePassContract.NodeFormDatabaseField +
                              ", so entry writes are refused everywhere while Edit Entries is open");
                    return null;
                }

                return field.GetValue(form) as PwDatabase;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the database behind an Edit Entries dialog could not be read", error);
                return null;
            }
        }

        /// <summary>Null when it cannot be read, which is taken to mean any entry at all.</summary>
        private PwEntry EntryBehind(Form form)
        {
            try
            {
                var field = form.GetType().GetField(
                    KeePassContract.EntryFormEntryField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    _log.Warn("an entry dialog no longer names its entry in " +
                              KeePassContract.EntryFormEntryField + ", so writes are refused while one is open");
                    return null;
                }

                return field.GetValue(form) as PwEntry;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the entry behind a dialog could not be read", error);
                return null;
            }
        }

        /// <summary>
        /// Whether a group dialog is Add Group, and true when that cannot be read.
        /// </summary>
        /// <remarks>
        /// True refuses additions to the group an Edit Group is open on, which costs a retry. False
        /// would allow them into a group that Cancel may take away, which costs what was added.
        /// </remarks>
        private bool Creating(Form form)
        {
            try
            {
                var field = form.GetType().GetField(
                    KeePassContract.GroupFormCreatingField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    _log.Warn("a group dialog no longer says whether it is adding a group in " +
                              KeePassContract.GroupFormCreatingField +
                              ", so nothing is added to a group while one is open on it");
                    return true;
                }

                var creating = field.GetValue(form);
                return !(creating is bool) || (bool)creating;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("whether a group dialog is adding a group could not be read", error);
                return true;
            }
        }

        /// <summary>Null when it cannot be read, which is taken to mean any group at all.</summary>
        private PwGroup TanStorageBehind(Form form)
        {
            try
            {
                var field = form.GetType().GetField(
                    KeePassContract.TanWizardFormGroupField, BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                {
                    _log.Warn("the TAN wizard no longer names its group in " +
                              KeePassContract.TanWizardFormGroupField +
                              ", so no group is removed while one is open");
                    return null;
                }

                return field.GetValue(form) as PwGroup;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                _log.Error("the group behind the TAN wizard could not be read", error);
                return null;
            }
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
                              KeePassContract.GroupFormGroupField +
                              ", so every write to a group is refused while one is open");
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
    }
}
