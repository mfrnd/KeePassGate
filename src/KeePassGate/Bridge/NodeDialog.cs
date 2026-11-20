using System;
using System.Reflection;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassLib;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// A KeePass group or entry dialog, as the grant editor needs it: which node it is about, which
    /// group the node hangs under, which database it belongs to, and whether the dialog itself
    /// keeps the tab from writing.
    /// </summary>
    /// <remarks>
    /// **What the dialog holds and nothing of the rules.** A node's rules are records of the
    /// plugin's store, `STORE.md` §617, which the tab reads and an OK writes as the dialog closes;
    /// the dialog's own copy of the node is never read for a rule and never written.
    ///
    /// Everything here is read on the UI thread while a dialog is being built, so nothing throws
    /// and every failure is a reason in words.
    /// </remarks>
    public sealed class NodeDialog
    {
        private readonly bool _entryDialog;

        private NodeDialog(bool entryDialog, PwGroup group, PwEntry entry, PwDatabase database, string readOnly)
        {
            _entryDialog = entryDialog;
            Group = group;
            Entry = entry;
            Database = database;
            ReadOnlyReason = readOnly;
        }

        /// <summary>The group this dialog is about, or null when it is about an entry.</summary>
        public PwGroup Group { get; private set; }

        /// <summary>The entry this dialog is about, or null when it is about a group.</summary>
        public PwEntry Entry { get; private set; }

        /// <summary>The database behind it, or null when the dialog does not tell.</summary>
        public PwDatabase Database { get; private set; }

        /// <summary>Why the dialog itself keeps the tab from writing, or null where it does not.</summary>
        public string ReadOnlyReason { get; private set; }

        /// <summary>Whether this dialog is about a node at all.</summary>
        public bool HasNode
        {
            get { return Group != null || Entry != null; }
        }

        /// <summary>The node's identifier, as the 32 lowercase hexadecimal characters the store names a node by.</summary>
        public string Node
        {
            get
            {
                var uuid = Entry != null ? Entry.Uuid : Group == null ? null : Group.Uuid;
                return uuid == null ? null : Hex.Of(uuid.UuidBytes);
            }
        }

        /// <summary>
        /// The group the node hangs under, for what arrives at the node: an entry's group, the
        /// group the Add Entry dialog adds its entry to among them, and a group's parent; null at
        /// the root group, which nothing arrives at.
        /// </summary>
        public PwGroup Parent
        {
            get { return Entry != null ? Entry.ParentGroup : Group == null ? null : Group.ParentGroup; }
        }

        /// <summary>What the editor's first line reads, which differs by dialog.</summary>
        /// <remarks>
        /// By the kind of dialog rather than by the node found in it, so that a dialog that does
        /// not name its node is still headed as the group or entry dialog it is; the dialog of the
        /// root group, which nothing is inherited at, has a line of its own.
        /// </remarks>
        public string Scope
        {
            get
            {
                if (_entryDialog)
                {
                    return GrantTab.EntryScope;
                }

                return Group != null && Group.ParentGroup == null ? GrantTab.RootScope : GrantTab.GroupScope;
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

            // Found before it is read, like the fields below: a dialog that does not tell which
            // database it belongs to has no store to read and none to write.
            var databaseField = Typed(
                FieldOf(dialog, KeePassContract.NodeFormDatabaseField, writer), typeof(PwDatabase), typeName, writer);
            var database = databaseField == null ? null : ValueOf(databaseField, dialog, writer) as PwDatabase;

            // **Whether each field exists, and then what it holds**, because the two answers are
            // not the same. Read through `Field` alone, a KeePass that renamed either field read as
            // a field holding nothing, which is a single entry as the entry stands: a merged
            // selection, whose entry is no entry of the database, or a revision, whose rules are
            // the entry's and whose OK keeps nothing.
            var mergedField = isEntry ? FieldOf(dialog, KeePassContract.EntryFormMultipleField, writer) : null;
            var modeField = isEntry ? FieldOf(dialog, KeePassContract.EntryFormEditModeField, writer) : null;
            var untold = isEntry && (mergedField == null || modeField == null);
            var merged = mergedField != null && ValueOf(mergedField, dialog, writer) != null;
            var revision = modeField != null && IsReadOnlyRevision(ValueOf(modeField, dialog, writer));

            string readOnly = null;
            if (merged)
            {
                // The dialog is about several entries, and the entry behind it is a merge made for
                // the dialog, which is no entry the store holds rules for.
                readOnly = GrantTab.SeveralEntries;
                entry = null;
            }
            else if (revision)
            {
                // A history revision. KeePass keeps nothing typed on this dialog, and a rule
                // belongs to the entry and to no version of it, so the rules shown are the entry's.
                readOnly = GrantTab.EarlierVersion;
                entry = AsTheEntryStands(entry, database);
            }
            else if (untold)
            {
                // Named without naming the fields, because the sentence is read by an operator
                // rather than by whoever will fix it. The log carries the detail.
                readOnly = GrantTab.EntryUntold;
                writer.Warn(typeName + " no longer exposes " +
                            (mergedField == null
                                ? KeePassContract.EntryFormMultipleField
                                : KeePassContract.EntryFormEditModeField) +
                            ", so the grant editor is read only");
            }
            else if (databaseField == null || database == null)
            {
                readOnly = GrantTab.NoDatabase;
                writer.Warn(typeName + " names no database in " + KeePassContract.NodeFormDatabaseField +
                            ", so the grant editor is read only");
            }
            else if (group == null && entry == null)
            {
                readOnly = isGroup ? GrantTab.NoGroup : GrantTab.NoEntry;
                writer.Warn(typeName + " names no " + (isGroup ? "group" : "entry") + " in " +
                            (isGroup ? KeePassContract.GroupFormGroupField : KeePassContract.EntryFormEntryField) +
                            ", so the grant editor is read only");
            }

            return new NodeDialog(isEntry, group, entry, database, readOnly);
        }

        /// <summary>
        /// The entry a revision is a version of, as the entry stands in the database: the entry of
        /// the revision's identifier, or the revision itself where the database holds no such
        /// entry or the dialog names no database.
        /// </summary>
        /// <remarks>
        /// **A revision read from the file hangs under no group**: KeePass reads a revision into
        /// the history of its entry and gives the revision no parent, measured on KeePass 2.62.
        /// Read through the revision, nothing would arrive from above, and the tab of an earlier
        /// version would show no rule from a group above, no limit and no rule of a name gone,
        /// beside a reason telling that the rules shown are the entry's as the rules stand. The
        /// entry found by the identifier is the node whose rules those are, under the group the
        /// entry stands in.
        /// </remarks>
        private static PwEntry AsTheEntryStands(PwEntry revision, PwDatabase database)
        {
            var root = revision == null || database == null ? null : database.RootGroup;
            var entry = root == null ? null : root.FindEntry(revision.Uuid, true);
            return entry ?? revision;
        }

        /// <summary>
        /// The field, or null when it holds a type this plugin cannot use, which is read as the
        /// field being gone.
        /// </summary>
        /// <remarks>
        /// **A field of another type is a field this plugin cannot reach**, whatever its name, so it
        /// gets the sentence and the log line a renamed field gets, and the log names the type.
        /// </remarks>
        private static FieldInfo Typed(FieldInfo field, Type expected, string typeName, IGateLog writer)
        {
            if (field == null || expected.IsAssignableFrom(field.FieldType))
            {
                return field;
            }

            writer.Warn(typeName + " keeps " + field.Name + " as " + field.FieldType.FullName +
                        " rather than " + expected.FullName);
            return null;
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

        /// <summary>What a dialog's field holds, or null when it has none or will not tell.</summary>
        /// <remarks>
        /// Finding the field is <see cref="FieldOf"/>'s job rather than a second copy of it
        /// here: two lookups of the same shape are two places for the binding flags to drift,
        /// and this lookup is asked about fields that decide whether the tab may write at all.
        /// </remarks>
        private static T Field<T>(Form dialog, string name, IGateLog log) where T : class
        {
            var field = FieldOf(dialog, name, log);
            return field == null ? null : ValueOf(field, dialog, log) as T;
        }

        /// <summary>What a field the dialog has holds, or null when it will not tell.</summary>
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
    }
}
