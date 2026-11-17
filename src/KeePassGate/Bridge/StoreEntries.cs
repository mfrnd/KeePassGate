using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;
using KeePassLib;
using KeePassLib.Security;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// A write of the plugin's own as entries of a database, `STORE.md` §627: the entry of each
    /// record, the group it goes into, the deletions with their deletion records, and the records
    /// set back, each step with its undo.
    /// </summary>
    /// <remarks>
    /// **On KeePass's own thread, inside a turn of the writer's**, which is what keeps everything
    /// else that changes the tree out while this runs.
    ///
    /// **Whole or not at all.** An exception at any step runs the undo of every step before it, in
    /// reverse, and goes on to the caller: an entry added is removed with no deletion record, an
    /// entry deleted is put back where it was and its deletion record taken away, a deletion record
    /// added is taken away, an entry set back is given its strings, its history and its times back,
    /// a group made is removed, and the database's mark is put back as it was. Each step is a part
    /// of its own that pushes its undo, in the order the application takes them.
    /// </remarks>
    internal static class StoreEntries
    {
        /// <summary>Applies a write to a database, whole or not at all.</summary>
        /// <param name="reading">The reading the write was planned against, in the same turn.</param>
        public static void Apply(PwDatabase database, StoreWrite write, StoreReading reading)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (write == null || write.Refused != null || write.Empty)
            {
                throw new ArgumentException("A write refused, or with nothing left in it, is applied to nothing.", nameof(write));
            }

            var undo = new Stack<Action>();

            // Taken back in a `finally` rather than a `catch`, so that whatever was thrown goes on
            // to the caller untouched and this is no kind of catch-all, `DESIGN.md` §141.
            var applied = false;
            try
            {
                Mark(database, undo);
                var group = Group(database, write.Starts ? null : reading, undo);
                Add(group, write.Add, undo);
                var held = DeletionsHeld(database);
                Delete(database, write.Delete, held, undo);
                Record(database, write.Deletions, held, undo);
                SetBack(database, write.SetBack, undo);
                Protect(database, reading, write.Delete, undo);
                applied = true;
            }
            finally
            {
                if (!applied)
                {
                    Undo(undo);
                }
            }
        }

        /// <summary>Runs every undo pushed, the last first.</summary>
        internal static void Undo(Stack<Action> undo)
        {
            while (undo.Count > 0)
            {
                undo.Pop()();
            }
        }

        /// <summary>Marks the database as changed, its mark before put back by the undo.</summary>
        internal static void Mark(PwDatabase database, Stack<Action> undo)
        {
            var modified = database.Modified;
            undo.Push(() => database.Modified = modified);
            database.Modified = true;
        }

        /// <summary>
        /// The group the write goes into, `Into`, or a group of the plugin's own made at the root
        /// where none is found or the write starts the store, which the undo removes.
        /// </summary>
        /// <param name="reading">The reading to find the group by, or null for a store started.</param>
        internal static PwGroup Group(PwDatabase database, StoreReading reading, Stack<Action> undo)
        {
            var group = reading == null ? null : Into(database, reading);
            if (group != null)
            {
                return group;
            }

            var root = database.RootGroup;
            var made = NewGroup();
            root.AddGroup(made, true);
            undo.Push(() => root.Groups.Remove(made));
            return made;
        }

        /// <summary>The entry of each record added to the group, which the undo removes with no deletion record.</summary>
        internal static void Add(PwGroup group, IEnumerable<StoreRecord> records, Stack<Action> undo)
        {
            foreach (var entry in records.Select(Of))
            {
                group.AddEntry(entry, true);
                undo.Push(() => group.Entries.Remove(entry));
            }
        }

        /// <summary>The identifiers the database's deletion records name already, as hexadecimal.</summary>
        internal static ISet<string> DeletionsHeld(PwDatabase database)
        {
            return new HashSet<string>(
                database.DeletedObjects.Select(deleted => deleted.Uuid.ToHexString()), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Each entry deleted, with a deletion record; the undo puts the entry back where it was
        /// and takes the deletion record away.
        /// </summary>
        internal static void Delete(PwDatabase database, IEnumerable<string> entries, ISet<string> held, Stack<Action> undo)
        {
            var root = database.RootGroup;
            foreach (var hex in entries)
            {
                var entry = root.FindEntry(new PwUuid(Bytes(hex)), true);
                if (entry != null && entry.ParentGroup != null)
                {
                    var parent = entry.ParentGroup;
                    var at = parent.Entries.IndexOf(entry);
                    parent.Entries.Remove(entry);
                    undo.Push(() => parent.Entries.Insert((uint)Math.Max(0, at), entry));
                }

                Record(database, hex, held, undo);
            }
        }

        /// <summary>A deletion record for each identifier the file names none of yet, which the undo takes away.</summary>
        internal static void Record(PwDatabase database, IEnumerable<string> identifiers, ISet<string> held, Stack<Action> undo)
        {
            foreach (var hex in identifiers)
            {
                Record(database, hex, held, undo);
            }
        }

        /// <summary>Each record that counts by its history set back, `STORE.md` §628.</summary>
        internal static void SetBack(PwDatabase database, IEnumerable<KeyValuePair<string, int>> records, Stack<Action> undo)
        {
            var root = database.RootGroup;
            foreach (var pair in records)
            {
                var entry = root.FindEntry(new PwUuid(Bytes(pair.Key)), true);
                if (entry != null)
                {
                    SetBack(database, entry, pair.Value, undo);
                }
            }
        }

        /// <summary>
        /// The content of every record holding the store key protected again where its protection
        /// went, the same text, as an edited record is set back, `STORE.md` §628 (S18): every such
        /// record the reading found but those the write deletes.
        /// </summary>
        internal static void Protect(PwDatabase database, StoreReading reading, IEnumerable<string> deleted, Stack<Action> undo)
        {
            if (reading.InForce == null)
            {
                return;
            }

            var root = database.RootGroup;
            var deleting = new HashSet<string>(deleted, StringComparer.Ordinal);
            var carriers = reading.InForce.Records
                .Where(record => record.Form.StoreKey != null && !deleting.Contains(record.Hex))
                .Select(record => root.FindEntry(new PwUuid(Bytes(record.Hex)), true))
                .Where(entry => entry != null);
            foreach (var entry in carriers)
            {
                Protect(database, entry, undo);
            }
        }

        /// <summary>The entry that holds a record, in no group yet, `STORE.md` §607.</summary>
        public static PwEntry Of(StoreRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var entry = new PwEntry(true, true) { Uuid = new PwUuid(record.Identifier) };
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, record.Title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, string.Empty));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, string.Empty));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, string.Empty));
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, record.Notes));
            foreach (var pair in record.Strings)
            {
                var protect = record.Protected && string.Equals(pair.Key, StoreFormat.DataField, StringComparison.Ordinal);
                entry.Strings.Set(pair.Key, new ProtectedString(protect, pair.Value));
            }

            entry.AutoType.Enabled = false;
            return entry;
        }

        /// <summary>
        /// The group a write puts what it adds into, `STORE.md` §606 (S11): among the groups with
        /// the plugin's prefix outside the recycle bin, the group that directly holds the most
        /// records under the store key, the lowest identifier among several; or null where none
        /// holds such a record.
        /// </summary>
        /// <remarks>
        /// So a group a file from outside brings, which holds no record under the database's key,
        /// is never written into for carrying the prefix, and nothing is written into the recycle
        /// bin, where emptying it would delete what was just written.
        /// </remarks>
        public static PwGroup Into(PwDatabase database, StoreReading reading)
        {
            if (reading == null || reading.InForce == null)
            {
                return null;
            }

            var records = new HashSet<string>(reading.InForce.Records.Select(record => record.Hex), StringComparer.Ordinal);
            var bin = database.RecycleBinEnabled ? database.RootGroup.FindGroup(database.RecycleBinUuid, true) : null;

            PwGroup best = null;
            var most = 0;
            foreach (var group in database.RootGroup.GetGroups(true).Where(group => StoreFormat.IsGroupIdentifier(group.Uuid.UuidBytes)))
            {
                if (bin != null && (group == bin || group.IsContainedIn(bin)))
                {
                    continue;
                }

                var held = group.Entries.Count(entry => records.Contains(Hex.Of(entry.Uuid.UuidBytes)));
                if (held == 0)
                {
                    continue;
                }

                if (best == null || held > most || (held == most && string.CompareOrdinal(Hex.Of(group.Uuid.UuidBytes), Hex.Of(best.Uuid.UuidBytes)) < 0))
                {
                    best = group;
                    most = held;
                }
            }

            return best;
        }

        /// <summary>A group of the plugin's own, `STORE.md` §606, in no group yet.</summary>
        private static PwGroup NewGroup()
        {
            return new PwGroup(true, true, StoreFormat.GroupName, PwIcon.TerminalEncrypted)
            {
                Uuid = new PwUuid(StoreFormat.NewGroupIdentifier()),
                Notes = StoreFormat.GroupNotes,
                EnableSearching = false,
                EnableAutoType = false,
            };
        }

        /// <summary>A deletion record for an identifier, where the file holds none of it yet.</summary>
        private static void Record(PwDatabase database, string hex, ISet<string> held, Stack<Action> undo)
        {
            if (!held.Add(hex))
            {
                return;
            }

            var deleted = new PwDeletedObject(new PwUuid(Bytes(hex)), DateTime.UtcNow);
            database.DeletedObjects.Add(deleted);
            undo.Push(() =>
            {
                database.DeletedObjects.Remove(deleted);
                held.Remove(hex);
            });
        }

        /// <summary>
        /// Sets a record that counts by its history back to the version that passes, as KeePass's
        /// own edit would make it, `STORE.md` §628: the edited version into the history, every
        /// string of the store as the version that passes holds it, and the time moved.
        /// </summary>
        private static void SetBack(PwDatabase database, PwEntry entry, int index, Stack<Action> undo)
        {
            var history = entry.History;
            if (index < 1 || index > history.UCount)
            {
                return;
            }

            // Read before the backup, which may trim the history to the database's limits.
            var passing = history.GetAt(history.UCount - (uint)index);
            var strings = passing.Strings.Where(pair => StoreFormat.IsField(pair.Key)).ToList();

            Backup(database, entry, undo);
            foreach (var name in entry.Strings.Where(pair => StoreFormat.IsField(pair.Key)).Select(pair => pair.Key).ToList())
            {
                entry.Strings.Remove(name);
            }

            foreach (var pair in strings)
            {
                entry.Strings.Set(pair.Key, pair.Value);
            }

            entry.Touch(true, false);
        }

        /// <summary>A record whose content holds the store key, protected again where its protection went.</summary>
        private static void Protect(PwDatabase database, PwEntry entry, Stack<Action> undo)
        {
            var content = entry.Strings.Get(StoreFormat.DataField);
            if (content == null || content.IsProtected)
            {
                return;
            }

            Backup(database, entry, undo);
            entry.Strings.Set(StoreFormat.DataField, content.WithProtection(true));
            entry.Touch(true, false);
        }

        /// <summary>
        /// The entry's version into its history, as KeePass's own edit puts it, with an undo that
        /// gives the entry back its strings, its history and its times.
        /// </summary>
        private static void Backup(PwDatabase database, PwEntry entry, Stack<Action> undo)
        {
            var before = entry.CloneDeep();
            undo.Push(() => entry.AssignProperties(before, false, true, true));
            entry.CreateBackup(database);
        }

        private static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }
    }
}
