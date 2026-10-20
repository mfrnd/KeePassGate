using System;
using System.Reflection;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Collections;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// A KeePass group or entry dialog, as the grant editor needs it: which node it is about, what
    /// that node hangs under, and where a rule typed on it has to be written.
    /// </summary>
    /// <remarks>
    /// **The write target is the dialog's own copy**, `m_sdCustomData`, and that is the whole
    /// reason this class exists. Both dialogs copy a node's custom data when they open and write
    /// that copy back over the node when they are accepted, so a grant written to the live group or
    /// entry survives Cancel and is destroyed by OK, silently and completely: the rule is on screen
    /// as the dialog closes and gone from the database afterwards. Writing into the copy makes the
    /// dialog's own buttons mean what they say, and costs one private field read by reflection.
    ///
    /// A KeePass that renames the field leaves <see cref="ReadOnlyReason"/> set and
    /// <see cref="Store"/> answering null, so the editor reports why it can do nothing rather than
    /// offering rules it could not keep. That is the safe direction and, unlike writing somewhere
    /// that is about to be discarded, a visible one. The same holds for the two fields that say
    /// whether a copy would be kept at all, the merged selection and the mode.
    ///
    /// Everything here is read on the UI thread while a dialog is being built, so nothing throws
    /// and every failure is a reason in words.
    /// </remarks>
    public sealed class NodeDialog
    {
        private readonly Form _dialog;
        private readonly FieldInfo _workingField;
        private readonly bool _merged;
        private readonly IGateLog _log;

        private NodeDialog(
            Form dialog,
            PwGroup group,
            PwEntry entry,
            PwDatabase database,
            FieldInfo workingField,
            bool merged,
            string readOnly,
            IGateLog log)
        {
            _dialog = dialog;
            _workingField = workingField;
            _merged = merged;
            _log = log;

            Group = group;
            Entry = entry;
            Database = database;
            ReadOnlyReason = readOnly;
        }

        /// <summary>The group this dialog is about, or null when it is about an entry.</summary>
        public PwGroup Group { get; private set; }

        /// <summary>The entry this dialog is about, or null when it is about a group.</summary>
        public PwEntry Entry { get; private set; }

        /// <summary>The database behind it, for the profiles worth offering. Null when unreadable.</summary>
        public PwDatabase Database { get; private set; }

        /// <summary>
        /// The dialog's own copy of the node's custom data, right now.
        /// </summary>
        /// <remarks>
        /// **Read every time rather than once**, because the copy does not exist yet when the tab
        /// is built: KeePass makes it while the dialog loads, and the plugin is told about the
        /// window during that same load. Capturing it at construction would capture null and leave
        /// an editor that silently wrote nowhere. Nobody can type into a dialog before it has
        /// finished loading, so by the time a rule is written this is here.
        /// </remarks>
        public StringDictionaryEx Working
        {
            get
            {
                if (_workingField == null || _dialog == null)
                {
                    return null;
                }

                try
                {
                    return _workingField.GetValue(_dialog) as StringDictionaryEx;
                }
                catch (Exception error) when (Expected.OfReflection(error))
                {
                    _log.Error("a dialog's working copy could not be read", error);
                    return null;
                }
            }
        }

        /// <summary>The node's own custom data, which is what the database currently holds.</summary>
        private StringDictionaryEx Live
        {
            get
            {
                if (Entry != null)
                {
                    return Entry.CustomData;
                }

                return Group == null ? null : Group.CustomData;
            }
        }

        /// <summary>Why an edit could not be kept, or null when it can.</summary>
        public string ReadOnlyReason { get; private set; }

        /// <summary>Whether this dialog is about a node at all.</summary>
        public bool HasNode
        {
            get { return Group != null || Entry != null; }
        }

        /// <summary>What the editor's first line says, which differs by dialog.</summary>
        public string Scope
        {
            get
            {
                if (Entry != null)
                {
                    return GrantTab.EntryScope;
                }

                return Group != null ? GrantTab.GroupScope : GrantTab.NoNodeScope;
            }
        }

        /// <summary>
        /// The node above this one, for what it inherits.
        /// </summary>
        /// <remarks>
        /// An entry's parent is its group and a group's is the group over it, so the root group has
        /// nothing above it and inherits nothing, which is right: it is the widest rule a database
        /// has.
        /// </remarks>
        public IAclNode Above
        {
            get
            {
                if (Entry != null)
                {
                    return KdbxNodes.Of(Entry.ParentGroup);
                }

                return Group == null ? null : KdbxNodes.Of(Group.ParentGroup);
            }
        }

        /// <summary>
        /// Reads a dialog, or answers null when it is not one of the two.
        /// </summary>
        public static NodeDialog Of(Form dialog, IGateLog log)
        {
            var writer = log ?? NullGateLog.Instance;

            if (dialog == null)
            {
                return null;
            }

            var typeName = TypeNameOf(dialog, writer);
            var isGroup = string.Equals(typeName, KeePassContract.GroupFormTypeName, StringComparison.Ordinal);
            var isEntry = string.Equals(typeName, KeePassContract.EntryFormTypeName, StringComparison.Ordinal);

            if (!isGroup && !isEntry)
            {
                return null;
            }

            var group = isGroup ? Field<PwGroup>(dialog, KeePassContract.GroupFormGroupField, writer) : null;
            var entry = isEntry ? Field<PwEntry>(dialog, KeePassContract.EntryFormEntryField, writer) : null;
            // Found before it is read, like the fields below: a dialog that does not say which
            // database it belongs to has no profiles to offer, and a rule is started by choosing
            // one, so that is a reason to be read only rather than an empty list.
            var databaseField = FieldOf(dialog, KeePassContract.NodeFormDatabaseField, writer);
            var database = databaseField == null ? null : ValueOf(databaseField, dialog, writer) as PwDatabase;

            // Whether the field exists, not what it holds. It holds nothing yet: the copy is made
            // while the dialog loads, and this runs during that load.
            var workingField = FieldOf(dialog, KeePassContract.NodeFormCustomDataField, writer);

            // Asked before anything else, because on these two the tab must not write at all
            // and `Store` answers null for them below. Read-only here means the same thing it
            // means everywhere on this tab: there is nothing to write through.
            //
            // **Whether each field exists, and then what it holds**, because the two answers are
            // not the same. Read through `Field` alone, a KeePass that renamed either field read
            // as one holding nothing, which is a single entry as it stands: the working copy of a
            // merged selection, whose OK deletes whatever it no longer carries from every entry in
            // it, or of a revision KeePass keeps nothing of, handed to the tab to write into.
            var mergedField = isEntry ? FieldOf(dialog, KeePassContract.EntryFormMultipleField, writer) : null;
            var modeField = isEntry ? FieldOf(dialog, KeePassContract.EntryFormEditModeField, writer) : null;
            var untold = isEntry && (mergedField == null || modeField == null);
            var merged = mergedField != null && ValueOf(mergedField, dialog, writer) != null;
            var revision = modeField != null && IsReadOnlyRevision(ValueOf(modeField, dialog, writer));

            string readOnly = null;
            if (merged)
            {
                // The dialog is about several entries, and the value behind it is a merge in
                // which everything they disagree about has become a cue string. Reading that
                // as a document says the rules are unreadable, which is false of every one of
                // them, and writing it reaches all of them at once.
                readOnly = "Several entries are being edited together, so this is not one "
                    + "entry's rules. Open a single entry to read or change them.";
            }
            else if (revision)
            {
                // A history revision. KeePass keeps nothing typed on this dialog, and the
                // rules below are that old revision's rather than the ones in force.
                readOnly = "This is an earlier version of the entry, so these are the rules "
                    + "it had then. Nothing here can be changed.";
            }
            else if (untold)
            {
                // Named without naming the fields, because the sentence is read by an operator
                // rather than by whoever will fix it. The log carries the detail.
                readOnly = "This KeePass does not say whether this dialog is about one entry as it "
                    + "is now, so the rules below can be read but not changed.";

                writer.Warn(typeName + " no longer exposes " +
                            (mergedField == null
                                ? KeePassContract.EntryFormMultipleField
                                : KeePassContract.EntryFormEditModeField) +
                            ", so the grant editor is read only");
            }
            else if (workingField == null)
            {
                // Named without naming the field, because the sentence is read by an operator
                // rather than by whoever will fix it. The log carries the detail.
                readOnly = "This KeePass keeps a dialog's settings somewhere this plugin cannot "
                    + "reach, so the rules below can be read but not changed. Nothing typed here "
                    + "would be kept.";

                writer.Warn(typeName + " no longer exposes " + KeePassContract.NodeFormCustomDataField +
                            ", so the grant editor is read only");
            }
            else if (databaseField == null)
            {
                // Said rather than left to the pick list, whose one sentence for an empty list is
                // that this database names no profiles, which is not what happened.
                readOnly = "This KeePass does not say which database this dialog belongs to, so there "
                    + "are no profiles to choose from and the rules below can be read but not changed.";

                writer.Warn(typeName + " no longer exposes " + KeePassContract.NodeFormDatabaseField +
                            ", so the grant editor is read only");
            }
            else if (group == null && entry == null)
            {
                readOnly = "This dialog does not say which " + (isGroup ? "group" : "entry")
                    + " it is about, so a rule written here could not be attached to anything.";
            }

            // The field is kept out of the store for these four rather than merely reported,
            // because a reason with a live store behind it would leave the tab offering to
            // discard rules it must not touch.
            return new NodeDialog(
                dialog, group, entry, database,
                merged || revision || untold || databaseField == null ? null : workingField,
                merged,
                readOnly,
                writer);
        }

        /// <summary>Where a rule typed on this dialog is written, and where the rules are read from.</summary>
        public IGrantStore Store()
        {
            return _workingField == null || !HasNode ? null : new DialogStore(this);
        }

        /// <summary>The rules this node holds, as the tab should show them, or null for none.</summary>
        /// <remarks>
        /// **Read from the node when there is no store**, because a tab that cannot write still
        /// shows what is there: a history revision says these are the rules it had then, and a
        /// KeePass that renamed the dialog's copy says the rules can be read but not changed.
        /// Asking only the store left both tables with nothing of the node's own, so the inherited
        /// rows read as the whole of it while the node granted more, or sealed what they show.
        ///
        /// The one exception is several entries edited together. The value behind that dialog is
        /// a merge, which is no entry's rules, and its sentence already says to open one.
        /// </remarks>
        public string Document()
        {
            var store = Store();
            if (store != null)
            {
                return store.Read();
            }

            var live = _merged ? null : Live;
            return live == null ? null : live.Get(AclDocument.CustomDataKey);
        }

        /// <summary>Whether an entry dialog's mode is a history revision opened for reading.</summary>
        /// <remarks>
        /// The mode is an enum of KeePass's own, so it is compared by name: reading the value
        /// as a number would pin an ordering nothing promises, and a KeePass that reordered
        /// them would silently make a read-only revision editable again.
        /// </remarks>
        private static bool IsReadOnlyRevision(object mode)
        {
            return mode != null
                   && string.Equals(
                       mode.ToString(),
                       KeePassContract.EntryFormReadOnlyMode,
                       StringComparison.Ordinal);
        }

        private static FieldInfo FieldOf(Form dialog, string name, IGateLog log)
        {
            try
            {
                return dialog.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                log.Error("a dialog's " + name + " could not be found", error);
                return null;
            }
        }

        private static string TypeNameOf(Form dialog, IGateLog log)
        {
            try
            {
                return dialog.GetType().FullName;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                log.Error("a dialog could not be named", error);
                return null;
            }
        }

        /// <summary>What a dialog's field holds, or null when it has none or will not say.</summary>
        /// <remarks>
        /// Finding the field is <see cref="FieldOf"/>'s job rather than a second copy of it
        /// here: two lookups of the same shape are two places for the binding flags to drift,
        /// and this one is asked about fields that decide whether the tab may write at all.
        /// </remarks>
        private static T Field<T>(Form dialog, string name, IGateLog log) where T : class
        {
            var field = FieldOf(dialog, name, log);
            return field == null ? null : ValueOf(field, dialog, log) as T;
        }

        /// <summary>What a field the dialog has holds, or null when it will not say.</summary>
        private static object ValueOf(FieldInfo field, Form dialog, IGateLog log)
        {
            try
            {
                return field.GetValue(dialog);
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                log.Error("a dialog's " + field.Name + " could not be read", error);
                return null;
            }
        }

        /// <summary>
        /// A node's grants: read from the node, written into the copy the dialog will put back.
        /// </summary>
        /// <remarks>
        /// **The two halves come from different places, and that is not an oversight.** The tab is
        /// built while the dialog is loading, before KeePass has made its copy, so reading has to
        /// come from the node itself or an editor opened on a group with rules would show none.
        /// Writing has to go into the copy, because a rule written to the node survives Cancel and
        /// is destroyed by OK. The two agree at that moment by construction: the copy is a clone of
        /// the node, and nobody can type into a dialog that has not finished loading.
        ///
        /// The key is removed rather than set to nothing when a node has no rules left, so that a
        /// node whose grants were all taken away is indistinguishable from one that never had any.
        /// </remarks>
        private sealed class DialogStore : IGrantStore
        {
            private readonly NodeDialog _node;

            public DialogStore(NodeDialog node)
            {
                _node = node;
            }

            public string Read()
            {
                var working = _node.Working;
                var data = working ?? _node.Live;

                return data == null ? null : data.Get(AclDocument.CustomDataKey);
            }

            public void Write(string document)
            {
                var data = _node.Working;
                if (data == null)
                {
                    // The dialog has not made its copy yet, which cannot happen once it is on
                    // screen. Writing to the node instead would be writing somewhere OK destroys.
                    throw new InvalidOperationException(
                        "This dialog has not made its own copy of the node's settings yet.");
                }

                if (document == null)
                {
                    data.Remove(AclDocument.CustomDataKey);
                    return;
                }

                data.Set(AclDocument.CustomDataKey, document);
            }
        }
    }
}
