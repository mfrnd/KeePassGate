// Drives Keepass2Android 1.15-r6's database code as the app does, from a script of steps.
// Written for this harness. Every step names the place in the app's source it stands for; what a
// step cannot stand for (a dialog, a setting, when the app reads or writes) is left to the script,
// which states the answer that was given, as the emulator flows did.
//
//   K2aHarness <script> [<script> ...]     run each script file
//   K2aHarness batch <jobs>                run every job of a jobs file (jobs separated by "end")
//
// A script is one step per line, '#' starts a comment:
//   password <text>                 the password of every later open (default: the bases' own)
//   open <file>                     Database.LoadData: PwDatabase.Open(stream, name, ioc, key, null,
//                                   KdbxDatabaseFormat(Default)); the file's hash is kept as
//                                   KdbxFile.HashOfFileOnDisk is
//   view <entry>                    EntryActivity: Entry.Touch(false) when an entry is shown
//   edit <entry> <field> append|set <text>
//                                   EntryEditActivity.SaveEntry and UpdateEntryFromUi, the entry
//                                   being the one the dialog edits (State.EntryInDatabase)
//   add <group> <title>             EntryEditActivity.CreateNewWithoutTemplate, SaveEntry (new) and
//                                   AddEntry.Run
//   rename-group <group> <suffix>   EditGroup.Run with the name the dialog returned
//   recycle <entry> | purge <entry> DeleteRunnable.DoDeleteEntry, "No" (recycle) or "Yes"
//                                   (permanently); in the bin only "permanently" exists
//   move-entry <entry> <group> | move-group <group> <group>
//                                   MoveElements.Run
//   save <out>                      SaveDb.PerformSaveWithoutCheck: KdbxDatabaseFormat.Save, which
//                                   is PwDatabase.Save(stream, null), to <out>
//   merge <file> <out>              SaveDb.MergeAndFinish then PerformSaveWithoutCheck: the file
//                                   <file> is the one now at the database's location (the answer
//                                   "Yes, merge", or AlwaysMergeOnConflict, or
//                                   SynchronizeCachedDatabase with the remote data); the result is
//                                   written to <out>
//   exec <program> | <arg> | ...    another program run while the session waits
// An <entry> or <group> is a probe alias (the 'KPG'-prefixed identifiers of the bases), a title
// prefixed by '=', or a 32-digit hexadecimal identifier.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace SourceHarness.K2a
{
    sealed class Session
    {
        public PwDatabase Db;
        public string Path;
        public string Password = Driver.Password;
    }

    static class Driver
    {
        public const string Password = "dummy-password-for-a-throwaway-file";

        static int Main(string[] a)
        {
            NativeLibrary.SetDllImportResolver(typeof(PwDatabase).Assembly, (name, asm, path) =>
                name == "argon2" ? NativeLibrary.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "libargon2.dll")) : IntPtr.Zero);
            if (a.Length == 0) { Console.Error.WriteLine("K2aHarness <script>... | batch <jobs>"); return 2; }
            var sw = Stopwatch.StartNew();
            int jobs = 0, failed = 0;
            IEnumerable<(string name, List<string> lines)> all;
            if (a[0] == "batch") all = Jobs(File.ReadAllLines(a[1]));
            else all = a.Select(f => (f, File.ReadAllLines(f).ToList()));
            foreach (var (name, lines) in all)
            {
                jobs++;
                try { Run(lines); }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAILED " + name + ": " + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ').Replace('\r', ' '));
                }
            }
            Console.Error.WriteLine("k2a: " + jobs + " jobs, " + failed + " failed, " + sw.ElapsedMilliseconds + " ms");
            return failed == 0 ? 0 : 1;
        }

        static IEnumerable<(string, List<string>)> Jobs(string[] lines)
        {
            var cur = new List<string>();
            string name = null;
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (l.StartsWith("job ")) { name = l.Substring(4); cur = new List<string>(); continue; }
                if (l == "end") { yield return (name ?? "?", cur); cur = new List<string>(); name = null; continue; }
                cur.Add(l);
            }
            if (cur.Count > 0) yield return (name ?? "?", cur);
        }

        static string[] Split(string l, int n)
        {
            var parts = new List<string>();
            int i = 0;
            while (parts.Count < n - 1)
            {
                while (i < l.Length && l[i] == ' ') i++;
                int j = l.IndexOf(' ', i);
                if (j < 0) { break; }
                parts.Add(l.Substring(i, j - i));
                i = j + 1;
            }
            if (i < l.Length) parts.Add(l.Substring(i));
            return parts.ToArray();
        }

        static void Run(List<string> lines)
        {
            var s = new Session();
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                var cmd = l.Split(' ')[0];
                switch (cmd)
                {
                    case "password": { var p = Split(l, 2); s.Password = p[1]; break; }
                    case "open": { var p = Split(l, 2); Open(s, p[1]); break; }
                    case "view": { var p = Split(l, 2); Entry(s, p[1]).Touch(false); break; }
                    case "edit": { var p = Split(l, 5); Edit(s, Entry(s, p[1]), p[2], p[3], p.Length > 4 ? p[4] : ""); break; }
                    case "add": { var p = Split(l, 3); Add(s, Group(s, p[1]), p[2]); break; }
                    case "rename-group": { var p = Split(l, 3); var g = Group(s, p[1]); g.Name = g.Name + p[2]; g.IconId = g.IconId; g.CustomIconUuid = g.CustomIconUuid; g.Touch(true); break; }
                    case "recycle": { var p = Split(l, 2); DeleteEntry(s, Entry(s, p[1]), false); break; }
                    case "purge": { var p = Split(l, 2); DeleteEntry(s, Entry(s, p[1]), true); break; }
                    case "move-entry": { var p = Split(l, 3); MoveEntry(Entry(s, p[1]), Group(s, p[2])); break; }
                    case "move-group": { var p = Split(l, 3); MoveGroup(Group(s, p[1]), Group(s, p[2])); break; }
                    case "save": { var p = Split(l, 2); Save(s, p[1]); break; }
                    case "merge": { var p = Split(l, 3); Merge(s, p[1]); Save(s, p[2]); break; }
                    case "exec": { Exec(l.Substring(5)); break; }
                    default: throw new InvalidOperationException("unknown step: " + l);
                }
            }
        }

        // Another program run while the session waits, as the emulator flows ran the desktop's side
        // (mksnew.sh) between two steps on the phone: the program and its arguments, separated by
        // " | ".
        static void Exec(string spec)
        {
            var parts = spec.Split(" | ");
            var psi = new ProcessStartInfo(parts[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in parts.Skip(1)) psi.ArgumentList.Add(arg);
            using var p = Process.Start(psi);
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException("exec failed: " + spec);
        }

        // ------------------------------------------------------------------ the database

        static CompositeKey Key(string password)
        {
            var k = new CompositeKey();
            k.AddUserKey(new KcpPassword(password));
            return k;
        }

        static IDatabaseFormat Format() { return new keepass2android.KdbxDatabaseFormat(KdbxFormat.Default); }

        // Database.LoadData and PopulateDatabaseFromStream; the stream is a MemoryStream as LoadDb's.
        static void Open(Session s, string file)
        {
            var ms = new MemoryStream(File.ReadAllBytes(file));
            var pd = new PwDatabase();
            pd.Open(ms, System.IO.Path.GetFileNameWithoutExtension(file), IOConnectionInfo.FromPath(file), Key(s.Password), null, Format());
            s.Db = pd;
            s.Path = file;
        }

        // Database.SaveData: DatabaseFormat.Save(KpDatabase, trans.OpenFile()), into <out>.
        static void Save(Session s, string outFile)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outFile));
            Directory.CreateDirectory(dir);
            using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
                Format().Save(s.Db, fs);
        }

        // SaveDb.MergeIn, line for line.
        static void Merge(Session s, string fileAtLocation)
        {
            PwDatabase pwImp = new PwDatabase();
            PwDatabase pwDatabase = s.Db;
            pwImp.New(new IOConnectionInfo(), pwDatabase.MasterKey, System.IO.Path.GetFileNameWithoutExtension(s.Path));
            pwImp.MemoryProtection = pwDatabase.MemoryProtection.CloneDeep();
            pwImp.MasterKey = pwDatabase.MasterKey;
            var stream = new MemoryStream(File.ReadAllBytes(fileAtLocation));
            Format().PopulateDatabaseFromStream(pwImp, stream, null);
            pwDatabase.MergeIn(pwImp, PwMergeMethod.Synchronize, null);
        }

        // ------------------------------------------------------------------ finding nodes

        static PwUuid Id(string alias)
        {
            if (alias.Length == 32 && alias.All(Uri.IsHexDigit)) return new PwUuid(MemUtil.HexStringToByteArray(alias));
            var b = new byte[16];
            b[0] = 0x4b; b[1] = 0x50; b[2] = 0x47;
            var t = Encoding.ASCII.GetBytes(alias);
            Array.Copy(t, 0, b, 3, t.Length);
            return new PwUuid(b);
        }

        static PwEntry Entry(Session s, string r)
        {
            var all = s.Db.RootGroup.GetEntries(true);
            if (r.StartsWith("=")) return all.First(e => e.Strings.ReadSafe(PwDefs.TitleField) == r.Substring(1));
            var id = Id(r);
            return all.First(e => e.Uuid.Equals(id));
        }

        static PwGroup Group(Session s, string r)
        {
            var root = s.Db.RootGroup;
            if (r == "Root" || r == "=") return root;
            var all = root.GetGroups(true);
            if (r.StartsWith("=")) return all.First(g => g.Name == r.Substring(1));
            var id = Id(r);
            if (root.Uuid.Equals(id)) return root;
            return all.First(g => g.Uuid.Equals(id));
        }

        // ------------------------------------------------------------------ the app's operations

        // EntryEditActivity.SaveEntry with UpdateEntryFromUi: the dialog's fields hold the entry's
        // values, one of them changed; every non-standard string is removed and set again from the
        // dialog with the protection its row shows; the attachments are the dialog's copy of them.
        static void Edit(Session s, PwEntry e, string field, string how, string text)
        {
            var db = s.Db;
            var ui = e.CloneDeep();   // State.Entry, the dialog's copy
            string Val(string f) { var v = ui.Strings.ReadSafe(f); return f == field ? (how == "append" ? v + text : text) : v; }

            PwEntry initialEntry = e.CloneDeep();
            PwEntry newEntry = e;
            newEntry.History = newEntry.History.CloneDeep();
            bool bCreateBackup = true;
            newEntry.CreateBackup(null);

            var mp = db.MemoryProtection;
            var extras = ui.Strings.Where(kv => !PwDefs.IsStandardField(kv.Key)).Select(kv => (kv.Key, kv.Value.IsProtected)).ToList();
            newEntry.Strings.Set(PwDefs.TitleField, new ProtectedString(mp.ProtectTitle, Val(PwDefs.TitleField)));
            newEntry.Strings.Set(PwDefs.UserNameField, new ProtectedString(mp.ProtectUserName, Val(PwDefs.UserNameField)));
            byte[] password = StrUtil.Utf8.GetBytes(Val(PwDefs.PasswordField));
            newEntry.Strings.Set(PwDefs.PasswordField, new ProtectedString(mp.ProtectPassword, password));
            MemUtil.ZeroByteArray(password);
            newEntry.Strings.Set(PwDefs.UrlField, new ProtectedString(mp.ProtectUrl, Val(PwDefs.UrlField)));
            newEntry.Strings.Set(PwDefs.NotesField, new ProtectedString(mp.ProtectNotes, Val(PwDefs.NotesField)));
            foreach (var key in newEntry.Strings.GetKeys())
                if (!PwDefs.IsStandardField(key)) newEntry.Strings.Remove(key);
            foreach (var (key, prot) in extras)
                newEntry.Strings.Set(key, new ProtectedString(prot, Val(key)));
            newEntry.OverrideUrl = field == "OverrideUrl" ? (how == "append" ? ui.OverrideUrl + text : text) : ui.OverrideUrl;
            List<string> vNewTags = StrUtil.StringToTags(StrUtil.TagsToString(ui.Tags, true));
            newEntry.Tags.Clear();
            foreach (string strTag in vNewTags) newEntry.AddTag(strTag);

            newEntry.Binaries = ui.Binaries;
            newEntry.Expires = ui.Expires;
            if (newEntry.Expires) newEntry.ExpiryTime = ui.ExpiryTime;

            newEntry.Touch(true, false);
            StrUtil.NormalizeNewLines(newEntry.Strings, true);

            bool bUndoBackup = false;
            PwCompareOptions cmpOpt = (PwCompareOptions.NullEmptyEquivStd | PwCompareOptions.IgnoreTimes);
            if (bCreateBackup) cmpOpt |= PwCompareOptions.IgnoreLastBackup;
            if (newEntry.EqualsEntry(initialEntry, cmpOpt, MemProtCmpMode.CustomOnly))
            {
                newEntry.LastModificationTime = initialEntry.LastModificationTime;
                bUndoBackup = bCreateBackup;
            }
            else if (bCreateBackup)
            {
                PwCompareOptions cmpOptNh = (cmpOpt | PwCompareOptions.IgnoreHistory);
                if (newEntry.EqualsEntry(initialEntry, cmpOptNh, MemProtCmpMode.CustomOnly)) bUndoBackup = true;
            }
            if (bUndoBackup) newEntry.History.RemoveAt(newEntry.History.UCount - 1);
            newEntry.MaintainBackups(db);
        }

        // EntryEditActivity.CreateNewWithoutTemplate, SaveEntry for a new entry, AddEntry.Run.
        static void Add(Session s, PwGroup parent, string title)
        {
            var db = s.Db;
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(db.MemoryProtection.ProtectUserName, db.DefaultUserName));
            if ((parent.IconId != PwIcon.Folder) && (parent.IconId != PwIcon.FolderOpen) && (parent.IconId != PwIcon.FolderPackage))
                entry.IconId = parent.IconId;
            else
                entry.IconId = PwIcon.Key;
            entry.CustomIconUuid = parent.CustomIconUuid;

            var mp = db.MemoryProtection;
            var ui = entry.CloneDeep();
            entry.History = entry.History.CloneDeep();
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(mp.ProtectTitle, title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(mp.ProtectUserName, ui.Strings.ReadSafe(PwDefs.UserNameField)));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(mp.ProtectPassword, StrUtil.Utf8.GetBytes(ui.Strings.ReadSafe(PwDefs.PasswordField))));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(mp.ProtectUrl, ui.Strings.ReadSafe(PwDefs.UrlField)));
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(mp.ProtectNotes, ui.Strings.ReadSafe(PwDefs.NotesField)));
            entry.OverrideUrl = ui.OverrideUrl;
            entry.Tags.Clear();
            entry.Binaries = ui.Binaries;
            entry.Expires = ui.Expires;
            entry.Touch(true, false);
            StrUtil.NormalizeNewLines(entry.Strings, true);
            entry.MaintainBackups(db);
            if (parent.FindEntry(entry.Uuid, false) == null) parent.AddEntry(entry, true);
        }

        // DeleteRunnable.DoDeleteEntry with CanRecycleGroup and EnsureRecycleBinExists.
        static void DeleteEntry(Session s, PwEntry pe, bool permanently)
        {
            PwDatabase pd = s.Db;
            PwGroup pgRecycleBin = pd.RootGroup.FindGroup(pd.RecycleBinUuid, true);
            PwGroup pgParent = pe.ParentGroup;
            bool canRecycle = CanRecycleGroup(pd, pgParent);
            if (pgParent == null) return;
            pgParent.Entries.Remove(pe);
            if (permanently || !canRecycle)
            {
                pd.DeletedObjects.Add(new PwDeletedObject(pe.Uuid, DateTime.Now));
            }
            else
            {
                if (pgRecycleBin == pd.RootGroup) pgRecycleBin = null;
                if (pgRecycleBin == null)
                {
                    pgRecycleBin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin)
                    { EnableAutoType = false, EnableSearching = false, IsExpanded = false };
                    pd.RootGroup.AddGroup(pgRecycleBin, true);
                    pd.RecycleBinUuid = pgRecycleBin.Uuid;
                }
                pgRecycleBin.AddEntry(pe, true, true);
                pe.Touch(false);
            }
        }

        static bool CanRecycleGroup(PwDatabase pd, PwGroup pgParent)
        {
            PwGroup pgRecycleBin = pd.RootGroup.FindGroup(pd.RecycleBinUuid, true);
            bool bPermanent = false;
            if (pgParent != null)
            {
                if (pd.RecycleBinEnabled == false) bPermanent = true;
                else if (pgRecycleBin == null) { }
                else if (pgParent == pgRecycleBin) bPermanent = true;
                else if (pgParent.IsContainedIn(pgRecycleBin)) bPermanent = true;
            }
            return !bPermanent;
        }

        // MoveElements.Run, for one element.
        static void MoveEntry(PwEntry entry, PwGroup target)
        {
            PwGroup pgParent = entry.ParentGroup;
            if (pgParent == target || pgParent == null) return;
            pgParent.Entries.Remove(entry);
            target.AddEntry(entry, true, true);
        }

        static void MoveGroup(PwGroup group, PwGroup target)
        {
            PwGroup pgParent = group.ParentGroup;
            if (pgParent == target || pgParent == null) return;
            if ((target == group) || target.IsContainedIn(group)) throw new InvalidOperationException("CannotMoveGroupHere");
            pgParent.Groups.Remove(group);
            target.AddGroup(group, true, true);
        }
    }
}
