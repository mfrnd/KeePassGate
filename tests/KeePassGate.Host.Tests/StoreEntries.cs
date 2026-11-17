using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Security;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A record the shared composer made, as the entry `STORE.md` §607 has it: under the
    /// identifier its content gives, with the strings of the store and the fields KeePass shows.
    /// </summary>
    internal static class StoreEntries
    {
        /// <summary>A new entry holding the record, in no group yet.</summary>
        public static PwEntry Of(StoreComposer.Composed record)
        {
            var entry = new PwEntry(true, true) { Uuid = new PwUuid(record.Identifier) };
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "KPG " + record.Strings[StoreComposer.Kind]));
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "KeePassGate data."));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, string.Empty));
            Put(entry, record);
            return entry;
        }

        /// <summary>
        /// The record's strings on an entry, the content protected where the record carries the
        /// store key, as `STORE.md` §607 has it.
        /// </summary>
        public static void Put(PwEntry entry, StoreComposer.Composed record)
        {
            var kind = record.Strings[StoreComposer.Kind];
            foreach (var pair in record.Strings)
            {
                var carriesKey = pair.Key == StoreComposer.Data && (kind == "authority" || kind == "retired");
                entry.Strings.Set(pair.Key, new ProtectedString(carriesKey, pair.Value));
            }
        }
    }
}
