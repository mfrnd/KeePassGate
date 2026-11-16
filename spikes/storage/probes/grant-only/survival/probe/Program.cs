// Probe for critique-grant-only-survival.md. Invented databases only, no window.
// Implements the store and the resolver exactly as option-grant-only.md section 1.2 and 5.2 state
// them, and as the design's own probe folds them (grant-only\probe\Program.cs, Read and GrantOnly):
//   row(N, p)  = union of the live letters of every grant naming (N, p)
//   permitted  = some node on the path from X to the root holds v in row(N, p) and, where the reach
//                needs a switch and v is not a removal, holds the switch in that same row
//   a grant counts with this store id and outside the recycle bin (RecycleBinUuid); a withdrawal
//   counts wherever it is found, and its key KPG.Withdrawn.<id> in database custom data as well.
// C# 5, for the .NET Framework's own csc.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class P
{
    static string Work;
    static TextWriter Out;
    const string Pw = "probe-only";
    const string Sid = "store-5e1f";
    const string Letters = "CRUSDLAP";
    const int C = 1, R = 2, U = 4, S = 8, D = 16, L = 32, A = 64, Pr = 128;
    const int Fields = 0, Attach = 1, Prot = 2;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly PwMergeMethod[] Methods = { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer,
        PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids };
    static List<KeyValuePair<string, string>> Xc = new List<KeyValuePair<string, string>>();

    static int Main(string[] args)
    {
        Work = args[0];
        Out = new StreamWriter(args[1], false, new UTF8Encoding(false));
        for (var i = 2; i + 1 < args.Length; i += 2) Xc.Add(new KeyValuePair<string, string>(args[i], args[i + 1]));
        Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o") + ", KeePassLib " + PwDefs.VersionString);
        Out.WriteLine("each line: user entries by path, with what agent may do there as one mask (switch shown where some verb reaches it)");
        Out.WriteLine("  and the question the scenario asks, answered yes or no");
        Out.WriteLine();
        try
        {
            S1(); S1b(); S2(); S3(); S4(); S4b();
        }
        catch (Exception ex) { Out.WriteLine("FAILED: " + ex); Out.Flush(); return 1; }
        Out.Flush(); Out.Close();
        return 0;
    }

    // ---------------------------------------------------------------- scenarios

    // S1: two copies change one entry's row apart. A adds R (A's row .RU....., no protected value
    // read); B adds P (B's row ..U....P, protected values written, not read). Question: may agent
    // read Y's protected values (its password)?
    static void S1()
    {
        var q = new Question { Text = "agent reads Y's protected values", Entry = "Y", Verb = R, Reach = Prot };
        var b = Base("s1", delegate(PwDatabase db) { NewGrant(db, "g0", Fixed("Y"), "agent", "..U.....", T0, "none"); });
        Run("S1: one entry's row changed apart. Base: Y granted ..U.....; A adds .R...... (A shows .RU.....); B adds .......P (B shows ..U....P)",
            "neither copy lets agent read Y's password", q, b,
            delegate(PwDatabase a) { NewGrant(a, "gA", Fixed("Y"), "agent", ".R......", T0.AddHours(1), "none"); },
            delegate(PwDatabase bb) { NewGrant(bb, "gB", Fixed("Y"), "agent", ".......P", T0.AddHours(2), "none"); });
    }

    // S1b: the design's own example turned around. Base: G granted .R...L..; A adds C on G (a program
    // may add plain fields and entries there); B adds P on G (it may read protected values there).
    // Question: may agent create a protected value in X?
    static void S1b()
    {
        var q = new Question { Text = "agent creates a protected value in Y", Entry = "Y", Verb = C, Reach = Prot };
        var b = Base("s1b", delegate(PwDatabase db) { NewGrant(db, "g0", Fixed("G"), "agent", ".R...L..", T0, "none"); });
        Run("S1b: one group's row changed apart. Base: G granted .R...L..; A adds C....... on G (A shows CR...L..); B adds .......P on G (B shows .R...L.P)",
            "neither copy lets agent create a protected value", q, b,
            delegate(PwDatabase a) { NewGrant(a, "gA", Fixed("G"), "agent", "C.......", T0.AddHours(1), "none"); },
            delegate(PwDatabase bb) { NewGrant(bb, "gB", Fixed("G"), "agent", ".......P", T0.AddHours(2), "none"); });
    }

    // S2: a place from one copy and a grant from the other. P has no grant and holds nothing secret;
    // H is private and holds X. A moves X into P (P has no grant in A). B grants agent on P (X is
    // not in P in B). Question: may agent read X?
    static void S2()
    {
        var q = new Question { Text = "agent reads X", Entry = "X", Verb = R, Reach = Fields };
        var b = Base("s2", null);
        Run("S2: A moves X from H into P (P ungranted in A); B grants .R...L.. on P (X not in P in B)",
            "in neither copy is X under a grant", q, b,
            delegate(PwDatabase a) { Move(a, "X", "P", T0.AddHours(1)); },
            delegate(PwDatabase bb) { NewGrant(bb, "gB", Fixed("P"), "agent", ".R...L..", T0.AddHours(2), "none"); });
    }

    // S3: the KeePassXC location time. G granted, X in G. A moves X out of G into H (the narrowing
    // under this model). B changes X's password later. Then KeePassXC merges B into A, and the result
    // into B, as a person syncing two files both ways does; then KeePassLib's Synchronize.
    static void S3()
    {
        var q = new Question { Text = "agent reads X", Entry = "X", Verb = R, Reach = Fields };
        var b = Base("s3", delegate(PwDatabase db) { NewGrant(db, "g0", Fixed("G"), "agent", ".R...L..", T0, "none"); Move(db, "X", "G", T0); });
        Out.WriteLine("S3: G granted, X in G. A moves X out of G into H at +1h; B changes X's password at +2h");
        Out.WriteLine("    expected by the operator: X closed (it was moved out from under the grant)");
        var dir = Path.Combine(Work, "s3"); Directory.CreateDirectory(dir);
        var pa = Path.Combine(dir, "A.kdbx"); var pb = Path.Combine(dir, "B.kdbx");
        Copy(b, pa, delegate(PwDatabase a) { Move(a, "X", "H", T0.AddHours(1)); });
        Copy(b, pb, delegate(PwDatabase bb) { Edit(bb, "X", "pw-B", T0.AddHours(2)); });
        Out.WriteLine("    A alone: " + Describe(pa, q));
        Out.WriteLine("    B alone: " + Describe(pb, q));
        foreach (var x in Xc)
        {
            var cell = Path.Combine(dir, "xc-" + x.Key); Directory.CreateDirectory(cell);
            var a1 = Path.Combine(cell, "A1.kdbx"); var b1 = Path.Combine(cell, "Bsrc.kdbx");
            File.Copy(pa, a1, true); File.Copy(pb, b1, true);
            Cli(x.Value, cell, "merge", "-s", a1, b1);                       // A <- B
            var b2 = Path.Combine(cell, "B2.kdbx"); var a1src = Path.Combine(cell, "A1src.kdbx");
            File.Copy(pb, b2, true); File.Copy(a1, a1src, true);
            Cli(x.Value, cell, "merge", "-s", b2, a1src);                    // B <- (A <- B)
            var bd = Path.Combine(cell, "Bdirect.kdbx"); var asrc = Path.Combine(cell, "Asrc.kdbx");
            File.Copy(pb, bd, true); File.Copy(pa, asrc, true);
            Cli(x.Value, cell, "merge", "-s", bd, asrc);                     // B <- A, for comparison
            Out.WriteLine("    KeePassXC " + x.Key + ", A <- B:              " + Describe(a1, q) + "  X location " + Loc(a1, "X"));
            Out.WriteLine("    KeePassXC " + x.Key + ", then B <- that:      " + Describe(b2, q) + "  X location " + Loc(b2, "X"));
            Out.WriteLine("    KeePassXC " + x.Key + ", B <- A directly:     " + Describe(bd, q));
            var k1 = Path.Combine(cell, "kp-into-A1.kdbx"); var k2 = Path.Combine(cell, "kp-into-B2.kdbx");
            Merge(a1, b2, PwMergeMethod.Synchronize, k1);
            Merge(b2, a1, PwMergeMethod.Synchronize, k2);
            Out.WriteLine("    then KeePass Synchronize, into A1:     " + Describe(k1, q));
            Out.WriteLine("    then KeePass Synchronize, into B2:     " + Describe(k2, q));
        }
        Out.WriteLine();
    }

    // S4: the only take-away a phone has: deleting the grant entry by hand. A deletes g0 for good,
    // with a deletion record, as Keepass2Android and KeePassDroid do from inside the bin. B is an
    // older copy that added an entry later. Question: may agent read X?
    static void S4()
    {
        var q = new Question { Text = "agent reads X", Entry = "X", Verb = R, Reach = Fields };
        var b = Base("s4", delegate(PwDatabase db) { NewGrant(db, "g0", Fixed("G"), "agent", ".R...L..", T0, "none"); Move(db, "X", "G", T0); });
        Run("S4: G granted, X in G. A deletes the grant entry for good, with a deletion record (a hand take-away, not the tab); B, older, adds an entry in H at +2h",
            "X closed, as A decided", q, b,
            delegate(PwDatabase a) { DeleteForGood(a, Fixed("record g0"), T0.AddHours(1)); },
            delegate(PwDatabase bb) { AddEntry(Grp(bb, "H"), Fixed("Bonly"), "Bonly", T0.AddHours(2)); });
    }

    // S4b: the same take-away by recycling the grant entry, in a database that had no recycle bin
    // yet: A's delete makes the bin, as KeePass and KeePassXC do on a first delete.
    static void S4b()
    {
        var q = new Question { Text = "agent reads X", Entry = "X", Verb = R, Reach = Fields };
        var b = Base("s4b", delegate(PwDatabase db) { NewGrant(db, "g0", Fixed("G"), "agent", ".R...L..", T0, "none"); Move(db, "X", "G", T0); db.RecycleBinEnabled = true; db.RecycleBinUuid = PwUuid.Zero; });
        Run("S4b: G granted, X in G, no recycle bin yet. A recycles the grant entry (the bin is made by that delete); B, older, adds an entry in H at +2h",
            "X closed, as A decided", q, b,
            delegate(PwDatabase a) { Recycle(a, Fixed("record g0"), T0.AddHours(1)); },
            delegate(PwDatabase bb) { AddEntry(Grp(bb, "H"), Fixed("Bonly"), "Bonly", T0.AddHours(2)); });
    }

    // ---------------------------------------------------------------- running cells

    sealed class Question { public string Text, Entry; public int Verb, Reach; }

    static string Base(string name, Action<PwDatabase> extra)
    {
        var dir = Path.Combine(Work, name); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        if (File.Exists(path)) File.Delete(path);
        var db = NewDb(path);
        var store = AddGroup(db.RootGroup, "store", "KeePassGate", T0);
        store.EnableSearching = false; store.EnableAutoType = false;
        var g = AddGroup(db.RootGroup, "G", "G", T0);
        AddGroup(db.RootGroup, "P", "P", T0);
        var h = AddGroup(db.RootGroup, "H", "H", T0);
        AddEntry(g, Fixed("Y"), "Y", T0);
        AddEntry(h, Fixed("X"), "X", T0);
        if (extra != null) extra(db);
        SaveTo(db, path);
        return path;
    }

    static void Copy(string basePath, string to, Action<PwDatabase> change)
    {
        File.Copy(basePath, to, true);
        var db = Open(to);
        change(db);
        db.Save(null); db.Close();
    }

    static void Run(string title, string expected, Question q, string basePath, Action<PwDatabase> changeA, Action<PwDatabase> changeB)
    {
        Out.WriteLine(title);
        Out.WriteLine("    expected by the operator: " + expected);
        var dir = Path.GetDirectoryName(basePath);
        var pa = Path.Combine(dir, "A.kdbx"); var pb = Path.Combine(dir, "B.kdbx");
        Copy(basePath, pa, changeA); Copy(basePath, pb, changeB);
        Out.WriteLine("    A alone: " + Describe(pa, q));
        Out.WriteLine("    B alone: " + Describe(pb, q));
        foreach (var m in Methods)
            foreach (var into in new[] { "A", "B" })
            {
                var res = Path.Combine(dir, m + "-into" + into + ".kdbx");
                Merge(into == "A" ? pa : pb, into == "A" ? pb : pa, m, res);
                Out.WriteLine("    " + m.ToString().PadRight(17) + " into " + into + ": " + Describe(res, q));
            }
        foreach (var x in Xc)
            foreach (var into in new[] { "A", "B" })
            {
                var cell = Path.Combine(dir, "xc-" + x.Key + "-into" + into); Directory.CreateDirectory(cell);
                var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
                File.Copy(into == "A" ? pa : pb, target, true); File.Copy(into == "A" ? pb : pa, source, true);
                var exit = Cli(x.Value, cell, "merge", "-s", target, source);
                Out.WriteLine("    KeePassXC " + x.Key.PadRight(7) + "   into " + into + ": " + Describe(target, q) + (exit == 0 ? "" : "  (exit " + exit + ")"));
            }
        Out.WriteLine();
    }

    static void Merge(string target, string source, PwMergeMethod m, string result)
    {
        var t = Open(target); var s = Open(source);
        t.MergeIn(s, m);
        s.Close();
        t.SaveAs(IOConnectionInfo.FromPath(result), false, null); t.Close();
    }

    static int Cli(string exe, string dir, params string[] args)
    {
        var info = new ProcessStartInfo { FileName = exe, WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var sb = new StringBuilder();
        foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
        info.Arguments = sb.ToString();
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n" + Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.BaseStream.Flush();
            p.StandardInput.Close();
            p.WaitForExit();
            o.Wait(); e.Wait();
            File.WriteAllText(Path.Combine(dir, "cli-" + Path.GetFileNameWithoutExtension(args[args.Length - 2]) + ".txt"), o.Result + "\n--\n" + e.Result);
            return p.ExitCode;
        }
    }

    // ---------------------------------------------------------------- the fold and the resolver

    sealed class Fold { public Dictionary<string, Dictionary<string, int>> Rows = new Dictionary<string, Dictionary<string, int>>(); public int Live, Cut, InBin; }

    static bool InBin(PwDatabase db, PwEntry e)
    {
        if (db.RecycleBinUuid == null || db.RecycleBinUuid.Equals(PwUuid.Zero)) return false;
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        return bin != null && e.IsContainedIn(bin);
    }

    static Fold Read(PwDatabase db)
    {
        var f = new Fold();
        var ws = new List<string[]>();
        foreach (var e in db.RootGroup.GetEntries(true))
            if (Get(e, "KPG.Kind") == "withdrawn") ws.Add(new[] { Get(e, "KPG.Id"), Get(e, "KPG.Node"), Get(e, "KPG.Profile"), Get(e, "KPG.Mask") });
        foreach (var kv in db.CustomData)
            if (kv.Key.StartsWith("KPG.Withdrawn.", StringComparison.Ordinal))
            {
                var parts = kv.Value.Split(' ');
                ws.Add(new[] { kv.Key.Substring("KPG.Withdrawn.".Length), parts[0], parts[1], parts[2] });
            }
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (Get(e, "KPG.Kind") != "grant" || Get(e, "KPG.Store") != Sid) continue;
            if (InBin(db, e)) { f.InBin++; continue; }
            var m = MaskOf(Get(e, "KPG.Mask"));
            var node = Get(e, "KPG.Node"); var profile = Get(e, "KPG.Profile");
            var seen = new HashSet<string>((Get(e, "KPG.Saw") ?? "").Split(' '));
            foreach (var w in ws) if (w[1] == node && w[2] == profile && !seen.Contains(w[0])) m &= ~MaskOf(w[3]);
            if (m == 0) { f.Cut++; continue; }
            f.Live++;
            Dictionary<string, int> rows;
            if (!f.Rows.TryGetValue(node, out rows)) f.Rows[node] = rows = new Dictionary<string, int>();
            int was; rows.TryGetValue(profile, out was);
            rows[profile] = was | m;
        }
        return f;
    }

    static int RowAt(Fold f, PwUuid node, string profile)
    {
        Dictionary<string, int> rows; int m;
        return f.Rows.TryGetValue(node.ToHexString(), out rows) && rows.TryGetValue(profile, out m) ? m : 0;
    }

    static List<int> Path_(Fold f, PwEntry e, string profile)
    {
        var rows = new List<int> { RowAt(f, e.Uuid, profile) };
        for (var g = e.ParentGroup; g != null; g = g.ParentGroup) rows.Add(RowAt(f, g.Uuid, profile));
        return rows;
    }

    static bool May(Fold f, PwEntry e, string profile, int verb, int reach)
    {
        foreach (var row in Path_(f, e, profile))
        {
            if ((row & verb) == 0) continue;
            if (reach == Fields || verb == S || verb == D) return true;
            if (reach == Attach && (row & A) != 0) return true;
            if (reach == Prot && (row & Pr) != 0) return true;
        }
        return false;
    }

    static string Effective(Fold f, PwEntry e, string profile)
    {
        var m = 0;
        foreach (var row in Path_(f, e, profile))
        {
            m |= row & (C | R | U | S | D | L);
            if ((row & (C | R | U | L)) != 0) m |= row & (A | Pr);
        }
        return MaskText(m);
    }

    static string Describe(string path, Question q)
    {
        var db = Open(path);
        var f = Read(db);
        var parts = new List<string>();
        string answer = "absent";
        foreach (var e in db.RootGroup.GetEntries(true).OrderBy(x => PathOf(x), StringComparer.Ordinal))
        {
            if (Get(e, "KPG.Kind") != null || InBin(db, e)) continue;
            var title = Get(e, PwDefs.TitleField);
            var copy = !e.Uuid.Equals(Fixed(title)) ? "*" : "";
            parts.Add(PathOf(e) + copy + "=" + Effective(f, e, "agent"));
            if (title == q.Entry && copy == "") answer = May(f, e, "agent", q.Verb, q.Reach) ? "YES" : "no";
        }
        db.Close();
        return string.Join("  ", parts.ToArray()) + "  [live " + f.Live + ", cut " + f.Cut + ", in bin " + f.InBin + "]  " + q.Text + ": " + answer;
    }

    static string Loc(string path, string title)
    {
        var db = Open(path);
        foreach (var e in db.RootGroup.GetEntries(true))
            if (Get(e, PwDefs.TitleField) == title && e.Uuid.Equals(Fixed(title)))
            {
                var s = e.ParentGroup.Name + " @ +" + (e.LocationChanged - T0).TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + "h";
                db.Close(); return s;
            }
        db.Close(); return "absent";
    }

    // ---------------------------------------------------------------- KeePassLib helpers

    static PwUuid Fixed(string name)
    {
        using (var md5 = MD5.Create()) return new PwUuid(md5.ComputeHash(Encoding.UTF8.GetBytes("gos:" + name)));
    }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }

    static PwDatabase NewDb(string path)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        db.KdfParameters = a;
        db.SettingsChanged = T0;
        db.RootGroup.Uuid = Fixed("root");
        db.RootGroup.Name = "probe";
        Stamp(db.RootGroup, T0);
        db.RecycleBinEnabled = false;
        return db;
    }

    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup AddGroup(PwGroup parent, string id, string name, DateTime t)
    {
        var g = new PwGroup(true, true, name, PwIcon.Folder);
        g.Uuid = Fixed(id);
        parent.AddGroup(g, true);
        Stamp(g, t);
        return g;
    }

    static PwEntry AddEntry(PwGroup g, PwUuid id, string title, DateTime t)
    {
        var e = new PwEntry(true, true);
        e.SetUuid(id, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-" + title));
        g.AddEntry(e, true);
        Stamp(e, t);
        return e;
    }

    static PwGroup Grp(PwDatabase db, string id) { return db.RootGroup.FindGroup(Fixed(id), true); }
    static PwEntry Ent(PwDatabase db, PwUuid id) { return db.RootGroup.FindEntry(id, true); }

    static string Get(PwEntry e, string name) { var s = e.Strings.Get(name); return s == null ? null : s.ReadString(); }
    static void Set(PwEntry e, string name, string v) { e.Strings.Set(name, new ProtectedString(false, v)); }

    static string PathOf(PwEntry e)
    {
        var parts = new List<string> { Get(e, PwDefs.TitleField) };
        for (var g = e.ParentGroup; g != null && g.ParentGroup != null; g = g.ParentGroup) parts.Insert(0, g.Name);
        return string.Join("/", parts.ToArray());
    }

    static int MaskOf(string text)
    {
        if (text == null || text.Length != 8) return 0;
        var m = 0;
        for (var i = 0; i < 8; i++) if (text[i] == Letters[i]) m |= 1 << i;
        return m;
    }

    static string MaskText(int m)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 8; i++) sb.Append((m & (1 << i)) != 0 ? Letters[i] : '.');
        return sb.ToString();
    }

    static string IdOf(string name)
    {
        using (var sha = SHA256.Create())
        {
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes("gos-id:" + name));
            return BitConverter.ToString(h, 0, 16).Replace("-", "").ToLowerInvariant();
        }
    }

    static void NewGrant(PwDatabase db, string name, PwUuid node, string profile, string mask, DateTime t, string saw)
    {
        var id = IdOf(name);
        var e = AddEntry(Grp(db, "store"), Fixed("record " + name), "KPG grant " + profile + " " + mask + " " + node.ToHexString().Substring(0, 8) + " " + id.Substring(0, 6), t);
        Set(e, "KPG.Kind", "grant");
        Set(e, "KPG.Store", Sid);
        Set(e, "KPG.Id", id);
        Set(e, "KPG.Node", node.ToHexString());
        Set(e, "KPG.Profile", profile);
        Set(e, "KPG.Mask", mask);
        Set(e, "KPG.Saw", saw);
        e.Strings.Remove(PwDefs.PasswordField);
    }

    // A move as KeePass makes one: the location time moves, the previous parent is kept, the
    // modification time does not move.
    static void Move(PwDatabase db, string title, string toGroup, DateTime t)
    {
        var e = Ent(db, Fixed(title));
        var from = e.ParentGroup; var to = Grp(db, toGroup);
        if (from == to) return;
        from.Entries.Remove(e);
        to.AddEntry(e, true);
        e.LocationChanged = t;
        e.PreviousParentGroup = from.Uuid;
    }

    // An edit as KeePass's entry dialog makes one: the version before into history, then the change.
    static void Edit(PwDatabase db, string title, string password, DateTime t)
    {
        var e = Ent(db, Fixed(title));
        e.CreateBackup(db);
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        e.LastModificationTime = t; e.LastAccessTime = t;
    }

    static void DeleteForGood(PwDatabase db, PwUuid id, DateTime t)
    {
        var e = Ent(db, id);
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(id, t));
    }

    // A recycle as KeePass makes one on a first delete: the bin is made, then the entry moved into
    // it with its location time and previous parent; its modification time does not move.
    static void Recycle(PwDatabase db, PwUuid id, DateTime t)
    {
        var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
        bin.Uuid = Fixed("bin");
        bin.EnableAutoType = false; bin.EnableSearching = false;
        db.RootGroup.AddGroup(bin, true);
        Stamp(bin, t);
        db.RecycleBinEnabled = true; db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = t;
        var e = Ent(db, id);
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        bin.AddEntry(e, true);
        e.LocationChanged = t;
        e.PreviousParentGroup = from.Uuid;
    }
}
