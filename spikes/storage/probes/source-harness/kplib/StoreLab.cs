// The recommended store's records under the phone programs' save and merge, as the harnesses run
// them, beside KeePassLib 2.62's own Synchronize of the same copies. Written for this family; built
// against KeePass 2.62's KeePassLib, which is KeePass's own reading and writing of a file.
//
//   gen <dir> <seed> <n2> <n3>   n2 histories of two copies and n3 of three, each in <dir>\hNNNN\
//                                as A.kdbx, B.kdbx [, C.kdbx] and steps.txt; and the jobs files
//                                jobs-k2a.txt and jobs-kpdx.txt, which write <dir>\hNNNN\<program>\
//   ref <dir>                    KeePassLib's Synchronize in the same orders, into hNNNN\kplib\
//   analyze <dir> <program>...   every store entry of every file a program wrote, held to the
//                                copies it came from and to KeePassLib's result; findings.txt and
//                                summary.txt in <dir>\<program>-*.txt
//   formats <dir>, dumpver <dir> the inputs of the format table, and the reading of what was
//                                written (Formats.cs)
//   long <dir>                   the long-string history, <dir>\h0001, and its jobs files
//   resave <in> <out>            KeePass's open and save of a file
//   sync <target> <source> <out> [<password>]   KeePass's Synchronize of two files
//
// The store, as EVALUATION.md 1 and 4 describe it: a group of the plugin's own, found by the prefix
// of its identifier; every datum an ordinary entry written once, with plain and protected custom
// strings named KPG.*, no custom data, nothing on any other entry; a head restating what is in
// force, replaced at every write of the plugin with a deletion record for what it replaces. The
// MAC is a stand-in (a hash), since nothing here verifies it: what is measured is what a program
// does to the entries, not what the plugin would conclude.
//
// The steps of a history (each at an explicit time; no step reads the clock):
//   plugin writes  admit (a record), revoke (a revocation of an admitted client), rule (a rule
//                  version for a group), narrow (a later version naming the earlier, which is
//                  deleted with a record), unname (a profile name removed); each with a new head,
//                  the old head deleted with a record
//   a person's     edit (an ordinary entry's password, with a history item), add (an ordinary
//   acts           entry), bin (a store entry moved to the recycle bin, as KeePass's Delete does),
//                  delrec (a store entry deleted for good, with a record), delnorec (deleted with
//                  no record, as a script or another program can), empty (the bin emptied, a
//                  record for each entry)
// Two copies fork from the common ancestor; a third forks from the ancestor or from the first copy
// after some of its steps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Lab
{
    public const string Password = "dummy-password-for-a-throwaway-file";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime T0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    const string Notes = "KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing.";
    static readonly byte[] GroupPrefix = Encoding.ASCII.GetBytes("KPG/STOR");

    static int Main(string[] a)
    {
        if (a.Length == 0) { Console.Error.WriteLine("gen <dir> <seed> <n2> <n3> | ref <dir> | analyze <dir> <program>... | formats <dir>"); return 2; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        switch (a[0])
        {
            case "gen": Gen(a[1], int.Parse(a[2], Inv), int.Parse(a[3], Inv), int.Parse(a[4], Inv)); break;
            case "ref": Ref(a[1]); break;
            case "analyze": Analyze(a[1], a.Skip(2).ToArray()); break;
            case "formats": Formats.Make(a[1]); break;
            case "long": Long(a[1]); break;
            case "resave":   // resave <in> <out>: KeePass's open and save
                {
                    var t = Open(a[1]);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(a[2])));
                    SaveAs(t, a[2]);
                    t.Close();
                    break;
                }
            case "sync":   // sync <target> <source> <out> [<password>]: KeePass's Synchronize of two files
                {
                    string pw = a.Length > 4 ? a[4] : Password;
                    var t = Open(a[1], pw); var s = Open(a[2], pw);
                    t.MergeIn(s, PwMergeMethod.Synchronize, null);
                    s.Close();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(a[3])));
                    SaveAs(t, a[3]);
                    t.Close();
                    break;
                }
            case "dumpver": Formats.Report(a[1]); break;
            default: Console.Error.WriteLine("unknown command " + a[0]); return 2;
        }
        Console.Error.WriteLine("StoreLab " + a[0] + ": " + sw.ElapsedMilliseconds.ToString(Inv) + " ms");
        return 0;
    }

    // ------------------------------------------------------------------ files

    public static CompositeKey Key(string pw = Password) { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(pw)); return k; }

    public static PwDatabase Open(string path, string pw = Password)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(pw), null);
        return db;
    }

    static void SaveAs(PwDatabase db, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
    }

    static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
    static string Sha(string s) { using (var h = SHA256.Create()) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(s))); }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = e.LastModificationTime = e.LastAccessTime = e.LocationChanged = t; e.ExpiryTime = T0; }
    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = g.LastModificationTime = g.LastAccessTime = g.LocationChanged = t; g.ExpiryTime = T0; }

    public static bool IsStoreGroup(PwGroup g)
    {
        var u = g.Uuid.UuidBytes;
        for (int i = 0; i < GroupPrefix.Length; i++) if (u[i] != GroupPrefix[i]) return false;
        return true;
    }

    public static bool IsStoreEntry(PwEntry e) { return e.Strings.Get("KPG.Kind") != null; }

    static PwGroup StoreGroup(PwDatabase db) { return db.RootGroup.GetGroups(true).FirstOrDefault(IsStoreGroup); }

    static PwGroup Bin(PwDatabase db)
    {
        if (!db.RecycleBinEnabled || db.RecycleBinUuid.Equals(PwUuid.Zero)) return null;
        return db.RootGroup.FindGroup(db.RecycleBinUuid, true);
    }

    // ------------------------------------------------------------------ the ancestor

    // AES-KDF with few rounds, so that thousands of files open quickly; KeePass 2.62 still writes
    // these KDBX 4.0, the format the store needs (formats\in\v40-aes shows the same). What a
    // program does to the store does not depend on the key derivation.
    static KdfParameters FastKdf()
    {
        var p = new AesKdf().GetDefaultParameters();
        p.SetUInt64(AesKdf.ParamRounds, 1000);
        return p;
    }

    sealed class Writer
    {
        public Random R;
        public PwDatabase Db;
        public int Clients;

        public PwEntry Element(string kind, string subject, string data, DateTime t, bool secret)
        {
            var id = new byte[16];
            R.NextBytes(id);
            var tag = Encoding.ASCII.GetBytes((kind + "___").Substring(0, 3).ToUpperInvariant());
            Array.Copy(tag, id, 3);
            var e = new PwEntry(false, false);
            e.Uuid = new PwUuid(id);
            string full = "{\"id\":\"" + Hex(id) + "\",\"kind\":\"" + kind + "\",\"subject\":\"" + subject + "\"" + data + "}";
            e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "KPG " + kind + " " + subject));
            e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
            e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
            e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
            e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Notes));
            e.Strings.Set("KPG.Kind", new ProtectedString(false, kind));
            e.Strings.Set("KPG.Data", new ProtectedString(false, full));
            e.Strings.Set("KPG.Mac", new ProtectedString(true, Sha(kind + "\n" + full)));
            if (secret) e.Strings.Set("KPG.Key", new ProtectedString(true, "invented-store-key-" + Hex(id).Substring(0, 16)));
            e.AutoType.Enabled = false;
            Stamp(e, t);
            StoreGroup(Db).AddEntry(e, true, false);
            return e;
        }

        IEnumerable<PwEntry> Elements(string kind)
        {
            return Db.RootGroup.GetEntries(true).Where(e => e.Strings.ReadSafe("KPG.Kind") == kind).ToList();
        }

        // A write of the plugin's own: the elements, then a head restating what is in force, the
        // heads it replaces deleted with a record.
        public void Write(DateTime t, Action body)
        {
            body();
            var heads = Elements("head").ToList();
            var standing = Db.RootGroup.GetEntries(true).Where(e => IsStoreEntry(e) && e.Strings.ReadSafe("KPG.Kind") != "head"
                && e.Strings.ReadSafe("KPG.Kind") != "authority" && e.Strings.ReadSafe("KPG.Kind") != "admission")
                .Select(e => e.Uuid.ToHexString().ToLowerInvariant()).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var replaced = heads.Select(h => h.Uuid.ToHexString().ToLowerInvariant()).ToList();
            Element("head", "store", ",\"restates\":[\"" + string.Join("\",\"", standing) + "\"],\"replaces\":[\"" + string.Join("\",\"", replaced) + "\"]", t, false);
            foreach (var h in heads) Delete(h, t);
        }

        public void Delete(PwEntry e, DateTime t)
        {
            e.ParentGroup.Entries.Remove(e);
            Db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
        }

        public void Admit(DateTime t)
        {
            Clients++;
            string c = "client-" + Clients.ToString(Inv) + "-" + R.Next(1000, 9999).ToString(Inv);
            Write(t, () => Element("admission", c, ",\"profiles\":[\"reader\"],\"record\":\"" + new string('r', 400) + "\"", t, false));
        }

        public bool Revoke(DateTime t)
        {
            var adm = Elements("admission").ToList();
            var revoked = new HashSet<string>(Elements("revocation").Select(e => e.Strings.ReadSafe("Title").Substring("KPG revocation ".Length)));
            var open = adm.Select(e => e.Strings.ReadSafe("Title").Substring("KPG admission ".Length)).Where(s => !revoked.Contains(s)).ToList();
            if (open.Count == 0) return false;
            var who = open[R.Next(open.Count)];
            Write(t, () => Element("revocation", who, ",\"key\":\"" + Sha(who).Substring(0, 32) + "\"", t, false));
            return true;
        }

        public void Rule(DateTime t, string node)
        {
            var before = Elements("rule").Where(e => e.Strings.ReadSafe("Title") == "KPG rule " + node).ToList();
            Write(t, () =>
            {
                string mask = before.Count == 0 ? ".R...L.." : ".R......";
                Element("rule", node, ",\"rules\":{\"reader\":\"" + mask + "\"},\"after\":[\"" + string.Join("\",\"", before.Select(b => b.Uuid.ToHexString().ToLowerInvariant())) + "\"]", t, false);
                foreach (var b in before) Delete(b, t);
            });
        }

        public void Unname(DateTime t)
        {
            Write(t, () => Element("removal", "profile-writer", ",\"name\":\"writer\"", t, false));
        }
    }

    static void Ancestor(string path, Random r, out int clients)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = FastKdf();
        db.Name = "Store histories (invented)";
        db.NameChanged = db.DescriptionChanged = db.DefaultUserNameChanged = db.MasterKeyChanged = db.SettingsChanged = T0;
        var root = new PwGroup(true, false) { Name = "Root", IconId = PwIcon.FolderOpen, IsExpanded = true };
        Stamp(root, T0);
        db.RootGroup = root;
        db.EntryTemplatesGroupChanged = T0;
        var gen = new PwGroup(true, false) { Name = "General", IconId = PwIcon.Folder };
        Stamp(gen, T0);
        root.AddGroup(gen, true);
        for (int i = 1; i <= 3; i++)
        {
            var e = new PwEntry(true, false);
            e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "O" + i.ToString(Inv)));
            e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "user-" + i.ToString(Inv)));
            e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-" + i.ToString(Inv)));
            Stamp(e, T0);
            gen.AddEntry(e, true);
        }
        var st = new PwGroup(false, false);
        var u = new byte[16];
        Array.Copy(GroupPrefix, u, 8);
        var rest = new byte[8]; r.NextBytes(rest); Array.Copy(rest, 0, u, 8, 8);
        st.Uuid = new PwUuid(u);
        st.Name = "KeePassGate"; st.IconId = PwIcon.TerminalEncrypted; st.Notes = "KeePassGate data.";
        st.EnableSearching = false; st.EnableAutoType = false;
        Stamp(st, T0);
        root.AddGroup(st, true);
        var bin = new PwGroup(true, false) { Name = "Recycle Bin", IconId = PwIcon.TrashBin, EnableSearching = false, EnableAutoType = false };
        Stamp(bin, T0);
        root.AddGroup(bin, true);
        db.RecycleBinEnabled = true; db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = T0;

        var w = new Writer { R = r, Db = db };
        var t = T0.AddMinutes(1);
        w.Write(t, () =>
        {
            w.Element("authority", "authority-1", ",\"document\":\"" + new string('a', 2800) + "\"", t, true);
            w.Element("addition", "profile-reader", ",\"name\":\"reader\"", t, false);
            w.Element("addition", "profile-writer", ",\"name\":\"writer\"", t, false);
        });
        w.Admit(T0.AddMinutes(2));
        w.Rule(T0.AddMinutes(3), "General");
        clients = w.Clients;
        db.Save(null);
        db.Close();
    }

    // ------------------------------------------------------------------ histories

    static readonly string[] PluginSteps = { "admit", "revoke", "rule", "narrow", "unname" };
    static readonly string[] PersonSteps = { "edit", "add", "bin", "delrec", "delnorec", "empty" };

    static string Step(PwDatabase db, Random r, int clients, ref int clientCount, DateTime t)
    {
        var w = new Writer { R = r, Db = db, Clients = clientCount };
        string kind = r.Next(2) == 0 ? PluginSteps[r.Next(PluginSteps.Length)] : PersonSteps[r.Next(PersonSteps.Length)];
        var store = db.RootGroup.GetEntries(true).Where(IsStoreEntry).ToList();
        PwEntry pick = store.Count > 0 ? store[r.Next(store.Count)] : null;
        string what = kind;
        switch (kind)
        {
            case "admit": w.Admit(t); what += " " + w.Clients.ToString(Inv); break;
            case "revoke": if (!w.Revoke(t)) { w.Admit(t); what = "admit (nothing to revoke)"; } break;
            case "rule": w.Rule(t, "Other-" + r.Next(1, 3).ToString(Inv)); break;
            case "narrow": w.Rule(t, "General"); break;
            case "unname": w.Unname(t); break;
            case "edit":
                {
                    var o = db.RootGroup.GetEntries(true).Where(e => !IsStoreEntry(e) && e.Strings.ReadSafe("Title").StartsWith("O")).ToList();
                    if (o.Count == 0) { what = "edit (none)"; break; }
                    var e = o[r.Next(o.Count)];
                    e.CreateBackup(db);
                    e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-" + r.Next(100000).ToString(Inv)));
                    e.LastModificationTime = e.LastAccessTime = t;
                    what += " " + e.Strings.ReadSafe("Title");
                    break;
                }
            case "add":
                {
                    var e = new PwEntry(true, false);
                    var gen = db.RootGroup.Groups.First(g => g.Name == "General");
                    e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "O-added-" + r.Next(100000).ToString(Inv)));
                    e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw"));
                    Stamp(e, t);
                    gen.AddEntry(e, true);
                    break;
                }
            case "bin":
                {
                    var bin = Bin(db);
                    if (pick == null || bin == null || pick.ParentGroup == bin) { what = "bin (none)"; break; }
                    var from = pick.ParentGroup;
                    from.Entries.Remove(pick);
                    bin.AddEntry(pick, true, true);
                    pick.PreviousParentGroup = from.Uuid;
                    pick.LocationChanged = t;
                    what += " " + pick.Strings.ReadSafe("KPG.Kind");
                    break;
                }
            case "delrec":
                if (pick == null) { what = "delrec (none)"; break; }
                w.Delete(pick, t);
                what += " " + pick.Strings.ReadSafe("KPG.Kind");
                break;
            case "delnorec":
                if (pick == null) { what = "delnorec (none)"; break; }
                pick.ParentGroup.Entries.Remove(pick);
                what += " " + pick.Strings.ReadSafe("KPG.Kind");
                break;
            case "empty":
                {
                    var bin = Bin(db);
                    if (bin == null || bin.Entries.UCount == 0) { what = "empty (none)"; break; }
                    foreach (var e in bin.Entries.ToList()) w.Delete(e, t);
                    break;
                }
        }
        clientCount = w.Clients;
        return what + " @" + t.ToString("HH:mm", Inv);
    }

    // Every history draws from its own generator, seeded from the run's seed and its number, so
    // that the histories can be made in parallel and still come out the same.
    static void Gen(string dir, int seed, int n2, int n3)
    {
        Directory.CreateDirectory(dir);
        int n = n2 + n3;
        var perK = new List<string>[n + 1];
        var perX = new List<string>[n + 1];
        System.Threading.Tasks.Parallel.For(1, n + 1, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Workers }, h =>
        {
            perK[h] = new List<string>();
            perX[h] = new List<string>();
            GenOne(dir, new Random(unchecked(seed * 100003 + h)), h, h <= n2 ? 2 : 3, perK[h], perX[h]);
        });
        File.WriteAllLines(Path.Combine(dir, "jobs-k2a.txt"), perK.Skip(1).SelectMany(x => x));
        File.WriteAllLines(Path.Combine(dir, "jobs-kpdx.txt"), perX.Skip(1).SelectMany(x => x));
        Console.WriteLine("gen: " + n.ToString(Inv) + " histories (" + n2.ToString(Inv) + " of two copies, " + n3.ToString(Inv) + " of three), seed " + seed.ToString(Inv));
    }

    public static int Workers = Math.Max(1, Environment.ProcessorCount / 2);

    static void GenOne(string dir, Random r, int h, int copies, List<string> jobsK, List<string> jobsX)
    {
        {
            string hd = Path.Combine(dir, "h" + h.ToString("0000", Inv));
            Directory.CreateDirectory(hd);
            var log = new List<string>();
            string anc = Path.Combine(hd, "ancestor.kdbx");
            int clients;
            Ancestor(anc, r, out clients);
            var names = copies == 2 ? new[] { "A", "B" } : new[] { "A", "B", "C" };
            var dbs = new Dictionary<string, PwDatabase>();
            var counts = new Dictionary<string, int>();
            int fork = -1;
            foreach (var c in names)
            {
                File.Copy(anc, Path.Combine(hd, c + ".kdbx"), true);
                counts[c] = clients;
            }
            dbs["A"] = Open(Path.Combine(hd, "A.kdbx"));
            dbs["B"] = Open(Path.Combine(hd, "B.kdbx"));
            int stepsA = r.Next(1, 5), stepsB = r.Next(1, 5), stepsC = copies == 3 ? r.Next(1, 4) : 0;
            if (copies == 3) fork = r.Next(0, stepsA + 1);   // C forks from A after `fork` of A's steps (0: from the ancestor)
            // Times: every copy's steps interleave on one clock; a tie now and then.
            var order = new List<string>();
            for (int i = 0; i < stepsA; i++) order.Add("A");
            for (int i = 0; i < stepsB; i++) order.Add("B");
            for (int i = 0; i < stepsC; i++) order.Add("C");
            for (int i = order.Count - 1; i > 0; i--) { int j = r.Next(i + 1); var x = order[i]; order[i] = order[j]; order[j] = x; }
            // C cannot step before it exists: move its steps after A's fork-th step.
            if (copies == 3)
            {
                var cs = order.Where(x => x == "C").ToList();
                order.RemoveAll(x => x == "C");
                int aSeen = 0, at = 0;
                for (; at < order.Count && aSeen < fork; at++) if (order[at] == "A") aSeen++;
                foreach (var x in cs) order.Insert(at + r.Next(0, order.Count - at + 1), x);
            }
            int minute = 10, doneA = 0;
            if (copies == 3 && fork == 0) dbs["C"] = Open(Path.Combine(hd, "C.kdbx"));
            log.Add("copies " + string.Join(",", names) + (copies == 3 ? " C-forks-from " + (fork == 0 ? "ancestor" : "A after step " + fork.ToString(Inv)) : ""));
            foreach (var c in order)
            {
                if (r.Next(6) != 0) minute += r.Next(1, 30);   // one step in six shares the time of the step before
                var t = T0.AddMinutes(minute);
                int cc = counts[c];
                var what = Step(dbs[c], r, clients, ref cc, t);
                counts[c] = cc;
                log.Add(c + " " + what);
                if (c == "A")
                {
                    doneA++;
                    if (copies == 3 && doneA == fork)
                    {
                        // C is A as it stands now: saved and reopened
                        SaveAs(dbs["A"], Path.Combine(hd, "C.kdbx"));
                        dbs["A"].Close();
                        dbs["A"] = Open(Path.Combine(hd, "C.kdbx"));
                        File.Copy(Path.Combine(hd, "C.kdbx"), Path.Combine(hd, "A.kdbx"), true);
                        dbs["C"] = Open(Path.Combine(hd, "C.kdbx"));
                        counts["C"] = counts["A"];
                    }
                }
            }
            foreach (var c in names)
            {
                SaveAs(dbs[c], Path.Combine(hd, c + ".kdbx"));
                dbs[c].Close();
            }
            File.Delete(anc);
            File.WriteAllLines(Path.Combine(hd, "steps.txt"), log);
            EmitJobs(hd, names, jobsK, jobsX);
        }
    }

    // The jobs: each copy opened and saved (an entry added, which is what makes a phone save), an
    // ordinary entry edited and saved; every ordered pair merged; with three copies every order of
    // all three.
    static void EmitJobs(string hd, string[] names, List<string> jobsK, List<string> jobsX)
    {
        foreach (var prog in new[] { "k2a", "kpdx" })
        {
            var jobs = prog == "k2a" ? jobsK : jobsX;
            string od = Path.Combine(hd, prog);
            foreach (var c in names)
            {
                jobs.Add("job " + Path.GetFileName(hd) + "/" + prog + "/save-" + c);
                jobs.Add("open " + Path.Combine(hd, c + ".kdbx"));
                jobs.Add("add Root phone-added");
                jobs.Add("save " + Path.Combine(od, "save-" + c + ".kdbx"));
                jobs.Add("end");
                jobs.Add("job " + Path.GetFileName(hd) + "/" + prog + "/edit-" + c);
                jobs.Add("open " + Path.Combine(hd, c + ".kdbx"));
                jobs.Add("edit =O1 Password append -phone");
                jobs.Add("save " + Path.Combine(od, "edit-" + c + ".kdbx"));
                jobs.Add("end");
            }
            foreach (var ord in Orders(names))
            {
                jobs.Add("job " + Path.GetFileName(hd) + "/" + prog + "/m-" + string.Concat(ord));
                jobs.Add("open " + Path.Combine(hd, ord[0] + ".kdbx"));
                if (prog == "k2a") jobs.Add("add Root phone-merge");   // the save whose check finds the file changed
                string sofar = ord[0];
                for (int i = 1; i < ord.Length; i++)
                {
                    sofar += ord[i];
                    jobs.Add("merge " + Path.Combine(hd, ord[i] + ".kdbx") + " " + Path.Combine(od, "m-" + sofar + ".kdbx"));
                }
                jobs.Add("end");
            }
        }
    }

    // Long strings: entries of the store whose one long string has 4, 16, 64 and 256 thousand
    // characters (4,096 to 262,144), plain (KPG.Data) and protected (KPG.Secret), in a store
    // otherwise as gen makes it. The text is JSON-safe ASCII with a two-byte and a three-byte UTF-8
    // character every 997 and 1,999 characters. Copy A is the ancestor with the eight entries;
    // copy B is A after a write of the plugin (an admission and a new head) and a 256-thousand
    // character entry of its own. The jobs are gen's, for <dir>\h0001.
    static string LongText(int n, int seed)
    {
        var r = new Random(seed);
        const string abc = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ,:{}[]-_";
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++)
            sb.Append(i % 1999 == 1998 ? '中' : (i % 997 == 996 ? 'é' : abc[r.Next(abc.Length)]));
        return sb.ToString();
    }

    static void Long(string dir)
    {
        var hd = Path.Combine(dir, "h0001");
        Directory.CreateDirectory(hd);
        var r = new Random(20261116);
        string a = Path.Combine(hd, "A.kdbx"), b = Path.Combine(hd, "B.kdbx");
        int clients;
        Ancestor(a, r, out clients);
        var log = new List<string> { "copies A,B: long strings" };
        var db = Open(a);
        var w = new Writer { R = r, Db = db, Clients = clients };
        var t = T0.AddMinutes(10);
        foreach (var k in new[] { 4, 16, 64, 256 })
            foreach (var prot in new[] { false, true })
            {
                var e = w.Element("long", (prot ? "protected-" : "plain-") + k.ToString(Inv) + "k", "", t, false);
                var text = LongText(k * 1024, k * 2 + (prot ? 1 : 0));
                e.Strings.Set(prot ? "KPG.Secret" : "KPG.Data", new ProtectedString(prot, text));
                log.Add("A long " + (prot ? "protected " : "plain ") + (k * 1024).ToString(Inv) + " characters");
            }
        SaveAs(db, a);
        db.Close();
        File.Copy(a, b, true);
        db = Open(b);
        w = new Writer { R = r, Db = db, Clients = clients };
        w.Admit(T0.AddMinutes(20));
        var e2 = w.Element("long", "plain-256k-B", "", T0.AddMinutes(21), false);
        e2.Strings.Set("KPG.Data", new ProtectedString(false, LongText(256 * 1024, 999)));
        log.Add("B admit; long plain 262144 characters");
        SaveAs(db, b);
        db.Close();
        File.WriteAllLines(Path.Combine(hd, "steps.txt"), log);
        var jk = new List<string>(); var jx = new List<string>();
        EmitJobs(hd, new[] { "A", "B" }, jk, jx);
        File.WriteAllLines(Path.Combine(dir, "jobs-k2a.txt"), jk);
        File.WriteAllLines(Path.Combine(dir, "jobs-kpdx.txt"), jx);
        Console.WriteLine("long: " + hd);
    }

    // Every order in which the copies can be merged: for two, both; for three, every order of the
    // three (whose first two are every ordered pair as well).
    static IEnumerable<string[]> Orders(string[] names)
    {
        if (names.Length == 2) { yield return new[] { "A", "B" }; yield return new[] { "B", "A" }; yield break; }
        foreach (var x in names)
            foreach (var y in names.Where(v => v != x))
                foreach (var z in names.Where(v => v != x && v != y))
                    yield return new[] { x, y, z };
    }

    static IEnumerable<string> Histories(string dir)
    {
        return Directory.GetDirectories(dir, "h*").Where(d => File.Exists(Path.Combine(d, "steps.txt"))).OrderBy(d => d, StringComparer.Ordinal);
    }

    static string[] Copies(string hd) { return new[] { "A", "B", "C" }.Where(c => File.Exists(Path.Combine(hd, c + ".kdbx"))).ToArray(); }

    // KeePassLib's own Synchronize over the same copies in the same orders: KeePass's File >
    // Synchronize, the open copy the target, the other file the source, the result saved.
    static void Ref(string dir)
    {
        System.Threading.Tasks.Parallel.ForEach(Histories(dir).ToList(), new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Workers }, hd =>
        {
            var names = Copies(hd);
            string od = Path.Combine(hd, "kplib");
            Directory.CreateDirectory(od);
            foreach (var ord in Orders(names))
            {
                var t = Open(Path.Combine(hd, ord[0] + ".kdbx"));
                string sofar = ord[0];
                for (int i = 1; i < ord.Length; i++)
                {
                    sofar += ord[i];
                    var s = Open(Path.Combine(hd, ord[i] + ".kdbx"));
                    t.MergeIn(s, PwMergeMethod.Synchronize, null);
                    s.Close();
                    SaveAs(t, Path.Combine(od, "m-" + sofar + ".kdbx"));
                }
                t.Close();
            }
        });
    }

    // ------------------------------------------------------------------ reading a file's store

    public sealed class StoreView
    {
        public bool Failed;
        public string Error;
        public string Version;
        public Dictionary<string, List<EntryFacts>> Entries = new Dictionary<string, List<EntryFacts>>();   // by identifier, every occurrence
        public Dictionary<string, string> Records = new Dictionary<string, string>();   // store deletion records: id -> time
        public HashSet<string> AllRecords = new HashSet<string>();
        public string BinId;
        public string StoreGroupId;
    }

    public sealed class EntryFacts
    {
        public string Id, Kind, Parent, Content, Created, Modified, Accessed, Location, Prev, History, Usage, Expires;
    }

    static string Iso(DateTime t) { return t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv); }

    static string Content(PwEntry e)
    {
        var sb = new StringBuilder();
        foreach (var kv in e.Strings.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append(kv.Key).Append(kv.Value.IsProtected ? "=p:" : "=:").Append(Sha(kv.Value.ReadString()).Substring(0, 12)).Append(';');
        foreach (var kv in e.Binaries.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append("A:").Append(kv.Key).Append(';');
        sb.Append("tags:").Append(string.Join(",", e.Tags)).Append(';');
        foreach (var kv in e.CustomData) sb.Append("CD:").Append(kv.Key).Append(';');
        sb.Append("icon:").Append(((int)e.IconId).ToString(Inv)).Append(";at:").Append(e.AutoType.Enabled ? "1" : "0")
          .Append(";fg:").Append(e.ForegroundColor.ToArgb().ToString("x8", Inv)).Append(";bg:").Append(e.BackgroundColor.ToArgb().ToString("x8", Inv))
          .Append(";ou:").Append(e.OverrideUrl).Append(";q:").Append(e.QualityCheck ? "1" : "0");
        return sb.ToString();
    }

    public static StoreView Read(string path, string pw = Password)
    {
        var v = new StoreView();
        if (!File.Exists(path)) { v.Failed = true; v.Error = "missing"; return v; }
        PwDatabase db;
        try { db = Open(path, pw); }
        catch (Exception ex) { v.Failed = true; v.Error = ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ').Replace('\r', ' '); return v; }
        try
        {
            var b = new byte[12];
            using (var s = File.OpenRead(path)) s.Read(b, 0, 12);
            uint ver = BitConverter.ToUInt32(b, 8);
            v.Version = (ver >> 16).ToString(Inv) + "." + (ver & 0xFFFF).ToString(Inv);
            var bin = Bin(db);
            v.BinId = bin == null ? null : bin.Uuid.ToHexString().ToLowerInvariant();
            var sg = StoreGroup(db);
            v.StoreGroupId = sg == null ? null : sg.Uuid.ToHexString().ToLowerInvariant();
            var storeIds = new HashSet<string>();
            foreach (var e in db.RootGroup.GetEntries(true))
            {
                if (!IsStoreEntry(e)) continue;
                var id = e.Uuid.ToHexString().ToLowerInvariant();
                storeIds.Add(id);
                var f = new EntryFacts
                {
                    Id = id,
                    Kind = e.Strings.ReadSafe("KPG.Kind"),
                    Parent = e.ParentGroup.Uuid.ToHexString().ToLowerInvariant(),
                    Content = Content(e),
                    Created = Iso(e.CreationTime),
                    Modified = Iso(e.LastModificationTime),
                    Accessed = Iso(e.LastAccessTime),
                    Location = Iso(e.LocationChanged),
                    Prev = e.PreviousParentGroup.ToHexString().ToLowerInvariant(),
                    History = e.History.UCount.ToString(Inv) + ":" + string.Join(",", e.History.Select(h => Iso(h.LastModificationTime) + "/" + Sha(Content(h)).Substring(0, 8))),
                    Usage = e.UsageCount.ToString(Inv),
                    Expires = (e.Expires ? "1 " : "0 ") + Iso(e.ExpiryTime),
                };
                List<EntryFacts> l;
                if (!v.Entries.TryGetValue(id, out l)) v.Entries[id] = l = new List<EntryFacts>();
                l.Add(f);
            }
            foreach (var d in db.DeletedObjects)
            {
                var id = d.Uuid.ToHexString().ToLowerInvariant();
                v.AllRecords.Add(id);
                // A store element's identifier begins with its kind's three-letter tag, upper case.
                var tag = Encoding.ASCII.GetString(d.Uuid.UuidBytes, 0, 3);
                if (StoreTags.Contains(tag)) v.Records[id] = Iso(d.DeletionTime);
            }
        }
        finally { db.Close(); }
        return v;
    }

    static readonly HashSet<string> StoreTags = new HashSet<string> { "AUT", "ADD", "ADM", "REV", "RUL", "REM", "HEA" };

    // ------------------------------------------------------------------ analysis

    sealed class Tally
    {
        public Dictionary<string, int> Counts = new Dictionary<string, int>();
        public Dictionary<string, List<string>> Examples = new Dictionary<string, List<string>>();
        public void Add(string cat, string example)
        {
            int c; Counts.TryGetValue(cat, out c); Counts[cat] = c + 1;
            List<string> l;
            if (!Examples.TryGetValue(cat, out l)) Examples[cat] = l = new List<string>();
            if (l.Count < 6) l.Add(example);
        }
    }

    // One history's findings for one program: (case kind, finding, the history's line).
    static List<Tuple<string, string, string>> AnalyzeOne(string hd, string prog)
    {
        var res = new List<Tuple<string, string, string>>();
        var cache = new Dictionary<string, StoreView>();
        Func<string, StoreView> view = p => { StoreView v; if (!cache.TryGetValue(p, out v)) cache[p] = v = Read(p); return v; };
        var h = Path.GetFileName(hd);
        var names = Copies(hd);
        var inputs = names.ToDictionary(c => c, c => view(Path.Combine(hd, c + ".kdbx")));
        string od = Path.Combine(hd, prog);
        if (prog != "kplib")
        {
            foreach (var c in names)
                foreach (var op in new[] { "save", "edit" })
                {
                    res.Add(Tuple.Create(op, (string)null, (string)null));
                    var outView = view(Path.Combine(od, op + "-" + c + ".kdbx"));
                    foreach (var f in CompareSave(inputs[c], outView)) res.Add(Tuple.Create(op, op + ": " + f.Item1, h + "\t" + op + "-" + c + "\t" + f.Item2));
                }
        }
        foreach (var ord in Orders(names))
        {
            string sofar = ord[0];
            for (int i = 1; i < ord.Length; i++)
            {
                sofar += ord[i];
                var key = i == 1 ? "merge of two" : "merge of three";
                res.Add(Tuple.Create(key, (string)null, (string)null));
                var outView = view(Path.Combine(od, "m-" + sofar + ".kdbx"));
                var used = sofar.Select(ch => inputs[ch.ToString()]).ToList();
                foreach (var f in CompareMerge(used, outView)) res.Add(Tuple.Create(key, key + ": " + f.Item1, h + "\tm-" + sofar + "\t" + f.Item2));
                if (prog != "kplib")
                {
                    var refView = view(Path.Combine(hd, "kplib", "m-" + sofar + ".kdbx"));
                    foreach (var f in CompareWithRef(outView, refView)) res.Add(Tuple.Create(key, key + " against KeePassLib: " + f.Item1, h + "\tm-" + sofar + "\t" + f.Item2));
                }
            }
        }
        return res;
    }

    static void Analyze(string dir, string[] programs)
    {
        var hs = Histories(dir).ToList();
        foreach (var prog in programs)
        {
            var tally = new Tally();
            var cases = new Dictionary<string, int>();
            var findings = new List<string>();
            var per = new List<Tuple<string, string, string>>[hs.Count];
            System.Threading.Tasks.Parallel.For(0, hs.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Workers }, i => { per[i] = AnalyzeOne(hs[i], prog); });
            foreach (var t in per.SelectMany(x => x))
            {
                if (t.Item2 == null) { int k; cases.TryGetValue(t.Item1, out k); cases[t.Item1] = k + 1; continue; }
                tally.Add(t.Item2, t.Item3.Replace('\t', ' '));
                findings.Add(prog + "\t" + t.Item2 + "\t" + t.Item3);
            }
            var sum = new List<string>();
            sum.Add("program " + prog);
            foreach (var kv in cases.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.Add("cases\t" + kv.Key + "\t" + kv.Value.ToString(Inv));
            foreach (var kv in tally.Counts.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sum.Add("finding\t" + kv.Key + "\t" + kv.Value.ToString(Inv));
                foreach (var ex in tally.Examples[kv.Key]) sum.Add("  e.g.\t" + ex);
            }
            File.WriteAllLines(Path.Combine(dir, prog + "-summary.txt"), sum);
            File.WriteAllLines(Path.Combine(dir, prog + "-findings.txt"), findings);
            foreach (var l in sum) Console.WriteLine(l);
        }
    }

    // A save: every store entry of the copy, held to the file the program wrote.
    static IEnumerable<Tuple<string, string>> CompareSave(StoreView input, StoreView output)
    {
        if (output.Failed) { yield return Tuple.Create("no file or unreadable", output.Error); yield break; }
        foreach (var kv in input.Entries)
        {
            var a = kv.Value[0];
            List<EntryFacts> l;
            if (!output.Entries.TryGetValue(kv.Key, out l)) { yield return Tuple.Create("entry lost", a.Kind + " " + kv.Key.Substring(0, 8)); continue; }
            foreach (var f in EntryDiff(a, l[0])) yield return Tuple.Create(f, a.Kind + " " + kv.Key.Substring(0, 8));
            if (l.Count > 1) yield return Tuple.Create("entry duplicated", a.Kind + " " + kv.Key.Substring(0, 8));
        }
        foreach (var kv in output.Entries) if (!input.Entries.ContainsKey(kv.Key)) yield return Tuple.Create("entry not in the copy", kv.Value[0].Kind + " " + kv.Key.Substring(0, 8));
        foreach (var kv in input.Records) if (!output.Records.ContainsKey(kv.Key)) yield return Tuple.Create("deletion record lost", kv.Key.Substring(0, 8));
        foreach (var kv in input.Records) { string t; if (output.Records.TryGetValue(kv.Key, out t) && t != kv.Value) yield return Tuple.Create("deletion record's time changed", kv.Key.Substring(0, 8) + " " + kv.Value + " -> " + t); }
        if (output.Version != input.Version) yield return Tuple.Create("format version " + input.Version + " -> " + output.Version, "");
    }

    static IEnumerable<string> EntryDiff(EntryFacts a, EntryFacts b)
    {
        if (a.Content != b.Content) yield return "content changed";
        if (a.Parent != b.Parent) yield return "moved";
        if (a.History != b.History) yield return "history changed";
        if (a.Created != b.Created) yield return "creation time changed";
        if (a.Modified != b.Modified) yield return "modification time changed";
        if (a.Location != b.Location) yield return "location time changed";
        if (a.Prev != b.Prev) yield return "previous parent changed";
        if (a.Accessed != b.Accessed || a.Usage != b.Usage) yield return "access time or usage changed";
        if (a.Expires != b.Expires) yield return "expiry changed";
    }

    // A merge: the store entries of every copy used, held to the result. An entry is expected when
    // some copy holds it and no copy holds a deletion record for it; it is then held to every
    // version the copies hold (a finding when it matches none).
    static IEnumerable<Tuple<string, string>> CompareMerge(List<StoreView> used, StoreView output)
    {
        if (output.Failed) { yield return Tuple.Create("no file or unreadable", output.Error); yield break; }
        var ids = new HashSet<string>(used.SelectMany(u => u.Entries.Keys));
        var recs = new HashSet<string>(used.SelectMany(u => u.Records.Keys));
        foreach (var id in ids)
        {
            var versions = used.Where(u => u.Entries.ContainsKey(id)).Select(u => u.Entries[id][0]).ToList();
            var kind = versions[0].Kind;
            List<EntryFacts> l;
            bool present = output.Entries.TryGetValue(id, out l);
            bool recorded = recs.Contains(id);
            bool inAll = used.All(u => u.Entries.ContainsKey(id));
            if (!present)
            {
                if (!recorded) yield return Tuple.Create(inAll ? "entry lost (held by every copy)" : "entry lost (held by some copies, no record)", kind + " " + id.Substring(0, 8));
                continue;
            }
            if (recorded) yield return Tuple.Create("entry kept beside a deletion record", kind + " " + id.Substring(0, 8));
            if (l.Count > 1) yield return Tuple.Create("entry duplicated", kind + " " + id.Substring(0, 8));
            var b = l[0];
            foreach (var cat in new[] { "content changed", "moved", "history changed", "creation time changed", "modification time changed", "location time changed", "previous parent changed", "access time or usage changed", "expiry changed" })
                if (versions.All(a => EntryDiff(a, b).Contains(cat))) yield return Tuple.Create(cat + " (from every copy's)", kind + " " + id.Substring(0, 8));
        }
        foreach (var kv in output.Entries) if (!ids.Contains(kv.Key)) yield return Tuple.Create("entry in no copy", kv.Value[0].Kind + " " + kv.Key.Substring(0, 8));
        foreach (var r in recs) if (!output.Records.ContainsKey(r)) yield return Tuple.Create("deletion record not kept", r.Substring(0, 8));
    }

    static IEnumerable<Tuple<string, string>> CompareWithRef(StoreView output, StoreView refView)
    {
        if (output.Failed || refView.Failed) yield break;
        foreach (var kv in refView.Entries)
        {
            List<EntryFacts> l;
            var a = kv.Value[0];
            if (!output.Entries.TryGetValue(kv.Key, out l)) { yield return Tuple.Create("entry KeePassLib keeps is missing", a.Kind + " " + kv.Key.Substring(0, 8)); continue; }
            foreach (var f in EntryDiff(a, l[0])) yield return Tuple.Create(f, a.Kind + " " + kv.Key.Substring(0, 8));
        }
        foreach (var kv in output.Entries) if (!refView.Entries.ContainsKey(kv.Key)) yield return Tuple.Create("entry KeePassLib does not keep is present", kv.Value[0].Kind + " " + kv.Key.Substring(0, 8));
        foreach (var r in refView.Records.Keys) if (!output.Records.ContainsKey(r)) yield return Tuple.Create("deletion record KeePassLib keeps is missing", r.Substring(0, 8));
        foreach (var r in output.Records.Keys) if (!refView.Records.ContainsKey(r)) yield return Tuple.Create("deletion record KeePassLib does not keep is present", r.Substring(0, 8));
    }
}
