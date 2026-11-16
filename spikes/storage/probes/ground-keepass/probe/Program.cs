using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Storage probe against KeePass 2.62's KeePassLib. Throwaway databases with invented content only,
// all under the work directory given as the first argument. No window is ever created.
static class P
{
    static string Work;
    static TextWriter Out;
    static DateTime T0;
    static int Counter;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly PwMergeMethod[] Methods = {
        PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting,
        PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids };

    static string M(PwMergeMethod m)
    {
        switch (m)
        {
            case PwMergeMethod.Synchronize: return "Sync";
            case PwMergeMethod.OverwriteIfNewer: return "IfNewer";
            case PwMergeMethod.OverwriteExisting: return "Overwrite";
            case PwMergeMethod.KeepExisting: return "Keep";
            case PwMergeMethod.CreateNewUuids: return "NewUuids";
        }
        return m.ToString();
    }

    static DateTime At(int s) { return T0.AddSeconds(s); }
    static string Rel(DateTime t)
    {
        var s = (TimeUtil.ToUtc(t, true) - T0).TotalSeconds;
        return (s >= 0 ? "+" : "") + s.ToString("0", Inv);
    }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword("probe-only")); return k; }

    static PwDatabase NewDb()
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Work, "new.kdbx")), Key());
        var p = new AesKdf().GetDefaultParameters();
        p.SetUInt64(AesKdf.ParamRounds, 1);
        db.KdfParameters = p;
        db.SettingsChanged = T0;
        db.RootGroup.CreationTime = db.RootGroup.LastModificationTime = T0;
        return db;
    }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static string Save(PwDatabase db, string name)
    {
        var path = Path.Combine(Work, name);
        db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        db.Close();
        return path;
    }

    static string Next(string stem) { return stem + "-" + (++Counter).ToString(Inv) + ".kdbx"; }

    static PwGroup AddGroup(PwGroup parent, string name, DateTime t)
    {
        var g = new PwGroup(true, true, name, PwIcon.Folder);
        parent.AddGroup(g, true, true);
        g.CreationTime = g.LastModificationTime = g.LastAccessTime = g.LocationChanged = t;
        return g;
    }

    static PwEntry AddEntry(PwGroup g, string title, DateTime t, params string[] kv)
    {
        var pe = new PwEntry(true, true);
        pe.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        for (int i = 0; i < kv.Length; i += 2) pe.Strings.Set(kv[i], new ProtectedString(false, kv[i + 1]));
        g.AddEntry(pe, true, true);
        pe.CreationTime = pe.LastModificationTime = pe.LastAccessTime = pe.LocationChanged = t;
        return pe;
    }

    static PwGroup Group(PwDatabase db, string name)
    {
        if (db.RootGroup.Name == name) return db.RootGroup;
        return db.RootGroup.GetGroups(true).FirstOrDefault(g => g.Name == name);
    }

    static List<PwEntry> Entries(PwDatabase db, string title)
    {
        return db.RootGroup.GetEntries(true).Where(e => e.Strings.ReadSafe(PwDefs.TitleField) == title).ToList();
    }

    static PwEntry One(PwDatabase db, string title) { return Entries(db, title).Single(); }

    static string Fields(PwEntry pe)
    {
        var parts = new List<string>();
        foreach (var kv in pe.Strings)
        {
            if (kv.Key == PwDefs.TitleField) continue;
            if (PwDefs.IsStandardField(kv.Key) && kv.Value.IsEmpty) continue;
            parts.Add(kv.Key + "=" + kv.Value.ReadString());
        }
        foreach (var kv in pe.Binaries) parts.Add("att:" + kv.Key);
        foreach (var kv in pe.CustomData) parts.Add("cd:" + kv.Key + "=" + kv.Value);
        foreach (var t in pe.Tags) parts.Add("tag:" + t);
        return "{" + string.Join(",", parts) + "}";
    }

    static string Short(PwEntry pe) { return Fields(pe) + "@" + Rel(pe.LastModificationTime); }

    static string Desc(PwEntry pe)
    {
        return Short(pe) + " hist[" + string.Join(" ", pe.History.Select(Short)) + "]";
    }

    static string Tree(PwDatabase db)
    {
        var sb = new StringBuilder();
        var groups = new List<PwGroup> { db.RootGroup };
        groups.AddRange(db.RootGroup.GetGroups(true));
        foreach (var g in groups)
        {
            if (g != db.RootGroup && g.Entries.UCount == 0 && g.Groups.UCount == 0) { sb.Append(g.Name).Append("[] "); continue; }
            if (g.Entries.UCount == 0) continue;
            sb.Append(g.Name).Append("[");
            sb.Append(string.Join(" ", g.Entries.Select(e => e.Strings.ReadSafe(PwDefs.TitleField) + Short(e) + "h" + e.History.UCount.ToString(Inv))));
            sb.Append("] ");
        }
        var names = new List<string>();
        foreach (var g in db.RootGroup.GetGroups(true)) names.Add(g.Name);
        sb.Append("groups=").Append(string.Join("/", names));
        sb.Append(" tomb=").Append(db.DeletedObjects.UCount.ToString(Inv));
        return sb.ToString();
    }

    static PwDatabase MergeInto(string targetPath, string sourcePath, PwMergeMethod mm)
    {
        var t = Open(targetPath);
        var s = Open(sourcePath);
        t.MergeIn(s, mm);
        s.Close();
        var r = Path.Combine(Work, Next("r"));
        t.SaveAs(IOConnectionInfo.FromPath(r), false, null);
        t.Close();
        var back = Open(r);
        return back;
    }

    static void Cells(string label, string a, string b, Func<PwDatabase, string> describe)
    {
        foreach (var mm in Methods)
        {
            using (var dummy = new Disposer())
            {
                var ra = MergeInto(a, b, mm);
                Out.WriteLine(label + " | into A | " + M(mm) + " | " + describe(ra));
                ra.Close();
                var rb = MergeInto(b, a, mm);
                Out.WriteLine(label + " | into B | " + M(mm) + " | " + describe(rb));
                rb.Close();
            }
        }
    }

    sealed class Disposer : IDisposable { public void Dispose() { } }

    // ---------------------------------------------------------------- X1: two different entries added
    static void X1()
    {
        Out.WriteLine("# X1 two different entries added to one group, one in each copy");
        var db = NewDb();
        var g = AddGroup(db.RootGroup, "KPG", At(0));
        AddEntry(g, "E0", At(0), "Rec", "0");
        var bas = Save(db, Next("x1-base"));
        var a = Open(bas); AddEntry(Group(a, "KPG"), "XA", At(10), "Rec", "a"); var pa = Save(a, Next("x1-A"));
        var b = Open(bas); AddEntry(Group(b, "KPG"), "XB", At(20), "Rec", "b"); var pb = Save(b, Next("x1-B"));
        Cells("X1", pa, pb, Tree);

        // the same, with each new entry carrying the same content (two copies writing the same record)
        var a2 = Open(bas); AddEntry(Group(a2, "KPG"), "Same", At(10), "Rec", "s"); var pa2 = Save(a2, Next("x1-A2"));
        var b2 = Open(bas); AddEntry(Group(b2, "KPG"), "Same", At(20), "Rec", "s"); var pb2 = Save(b2, Next("x1-B2"));
        Cells("X1-same-content", pa2, pb2, Tree);
    }

    // ---------------------------------------------------------------- X2: one entry, each copy changes it
    sealed class Variant
    {
        public string Name; public Action<PwEntry> A; public Action<PwEntry> B;
        public Func<PwEntry, bool> HasA; public Func<PwEntry, bool> HasB;
    }

    sealed class Timing { public string Name; public int TA; public int TB; public bool BStale; }

    static void X2()
    {
        Out.WriteLine("# X2 one entry in both copies, each copy changes it");
        var variants = new List<Variant>
        {
            new Variant { Name = "diff-custom-string",
                A = e => e.Strings.Set("FA", new ProtectedString(false, "a")),
                B = e => e.Strings.Set("FB", new ProtectedString(false, "b")),
                HasA = e => e.Strings.Exists("FA"), HasB = e => e.Strings.Exists("FB") },
            new Variant { Name = "diff-protected-string",
                A = e => e.Strings.Set("PA", new ProtectedString(true, "a")),
                B = e => e.Strings.Set("PB", new ProtectedString(true, "b")),
                HasA = e => e.Strings.Exists("PA"), HasB = e => e.Strings.Exists("PB") },
            new Variant { Name = "same-field",
                A = e => e.Strings.Set("Base", new ProtectedString(false, "a")),
                B = e => e.Strings.Set("Base", new ProtectedString(false, "b")),
                HasA = e => e.Strings.ReadSafe("Base") == "a", HasB = e => e.Strings.ReadSafe("Base") == "b" },
            new Variant { Name = "only-A-changes",
                A = e => e.Strings.Set("FA", new ProtectedString(false, "a")),
                B = null,
                HasA = e => e.Strings.Exists("FA"), HasB = e => false },
            new Variant { Name = "entry-custom-data",
                A = e => e.CustomData.Set("kA", "a"), B = e => e.CustomData.Set("kB", "b"),
                HasA = e => e.CustomData.Exists("kA"), HasB = e => e.CustomData.Exists("kB") },
            new Variant { Name = "attachment",
                A = e => e.Binaries.Set("a.bin", new ProtectedBinary(false, new byte[] { 1, 2, 3 })),
                B = e => e.Binaries.Set("b.bin", new ProtectedBinary(false, new byte[] { 4, 5, 6 })),
                HasA = e => e.Binaries.Get("a.bin") != null, HasB = e => e.Binaries.Get("b.bin") != null },
            new Variant { Name = "tag",
                A = e => e.AddTag("ta"), B = e => e.AddTag("tb"),
                HasA = e => e.HasTag("ta"), HasB = e => e.HasTag("tb") },
            new Variant { Name = "notes",
                A = e => e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "{\"20261115T000010Z\":\"a\"}")),
                B = e => e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "{\"20261115T000020Z\":\"b\"}")),
                HasA = e => e.Strings.ReadSafe(PwDefs.NotesField).Contains("\"a\""), HasB = e => e.Strings.ReadSafe(PwDefs.NotesField).Contains("\"b\"") },
        };
        var timings = new List<Timing>
        {
            new Timing { Name = "A-newer", TA = 20, TB = 10 },
            new Timing { Name = "B-newer", TA = 10, TB = 20 },
            new Timing { Name = "equal", TA = 10, TB = 10 },
            new Timing { Name = "B-time-not-moved", TA = 10, TB = 0 },
            new Timing { Name = "neither-time-moved", TA = 0, TB = 0 },
            new Timing { Name = "B-stale-time-rewritten", TA = 10, TB = 30, BStale = true },
        };
        var summary = new List<string>();
        foreach (var v in variants)
            foreach (var hist in new[] { "backup", "nobackup" })
                foreach (var t in timings)
                {
                    if (v.B == null && !t.BStale && t.Name != "A-newer" && t.Name != "B-time-not-moved") continue;
                    var db = NewDb();
                    var g = AddGroup(db.RootGroup, "KPG", At(0));
                    AddEntry(g, "store", At(0), "Base", "1");
                    var bas = Save(db, Next("x2-base"));

                    var a = Open(bas); var ea = One(a, "store");
                    if (hist == "backup") ea.CreateBackup(null);
                    v.A(ea); ea.LastModificationTime = At(t.TA);
                    var pa = Save(a, Next("x2-A"));

                    var b = Open(bas); var eb = One(b, "store");
                    bool bChanges = (v.B != null) && !t.BStale;
                    if (bChanges)
                    {
                        if (hist == "backup") eb.CreateBackup(null);
                        v.B(eb);
                    }
                    if (bChanges || t.BStale) eb.LastModificationTime = At(t.TB);
                    var pb = Save(b, Next("x2-B"));

                    foreach (var mm in Methods)
                        foreach (var into in new[] { "A", "B" })
                        {
                            var r = into == "A" ? MergeInto(pa, pb, mm) : MergeInto(pb, pa, mm);
                            var es = Entries(r, "store");
                            var cells = new List<string>();
                            foreach (var e in es)
                            {
                                string cur = (v.HasA(e) ? "A" : "") + (v.HasB(e) ? "B" : "");
                                if (cur == "") cur = "-";
                                bool ha = e.History.Any(h => v.HasA(h)), hb = e.History.Any(h => v.HasB(h));
                                string hs = (ha ? "A" : "") + (hb ? "B" : "");
                                if (hs == "") hs = "-";
                                cells.Add("cur=" + cur + " hist=" + hs + " n=" + e.History.UCount.ToString(Inv) + " lm=" + Rel(e.LastModificationTime));
                                Out.WriteLine("X2 | " + v.Name + " | " + hist + " | " + t.Name + " | into " + into + " | " + M(mm) + " | " + Desc(e));
                            }
                            summary.Add(v.Name + " | " + hist + " | " + t.Name + " | into " + into + " | " + M(mm) + " | " + string.Join(" ; ", cells));
                            r.Close();
                        }
                }
        Out.WriteLine("# X2 summary: cur = whose change the current entry holds, hist = whose change some history item holds");
        foreach (var s in summary) Out.WriteLine("X2S | " + s);
    }

    // ---------------------------------------------------------------- X3: deletions, recycling, moves
    static string BaseWithBin(string stem, out Dictionary<string, PwUuid> ids)
    {
        var db = NewDb();
        var g = AddGroup(db.RootGroup, "KPG", At(0));
        var g2 = AddGroup(db.RootGroup, "G2", At(0));
        var g3 = AddGroup(db.RootGroup, "G3", At(0));
        var bin = AddGroup(db.RootGroup, "Bin", At(0));
        db.RecycleBinEnabled = true; db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = At(0);
        var e = AddEntry(g, "E", At(0), "Rec", "0");
        var e2 = AddEntry(g2, "E2", At(0), "Rec", "0");
        ids = new Dictionary<string, PwUuid> { { "E", e.Uuid }, { "E2", e2.Uuid }, { "G2", g2.Uuid } };
        return Save(db, Next(stem));
    }

    static void Change(PwDatabase db, string title, int t, string value)
    {
        var e = One(db, title);
        e.CreateBackup(null);
        e.Strings.Set("Rec", new ProtectedString(false, value));
        e.LastModificationTime = At(t);
    }

    static void DeletePermanently(PwDatabase db, string title, int t)
    {
        var e = One(db, title);
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(t)));
    }

    static void Recycle(PwDatabase db, string title, int t)
    {
        var e = One(db, title);
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        Group(db, "Bin").AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = At(t);
    }

    static void Move(PwDatabase db, string title, string to, int t)
    {
        var e = One(db, title);
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        Group(db, to).AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = At(t);
    }

    static void DeleteGroup(PwDatabase db, string name, int t)
    {
        var g = Group(db, name);
        g.ParentGroup.Groups.Remove(g);
        foreach (var e in g.GetEntries(true)) db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(t)));
        foreach (var sg in g.GetGroups(true)) db.DeletedObjects.Add(new PwDeletedObject(sg.Uuid, At(t)));
        db.DeletedObjects.Add(new PwDeletedObject(g.Uuid, At(t)));
    }

    static void X3()
    {
        Out.WriteLine("# X3 deletion, recycling, moving, a deleted group (A acts at +20 unless noted)");
        Dictionary<string, PwUuid> ids;
        var scen = new List<KeyValuePair<string, Action<PwDatabase, PwDatabase>>>
        {
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("deleted-on-A, B-unchanged",
                (a, b) => DeletePermanently(a, "E", 20)),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("deleted-on-A, B-changed-later(+30)",
                (a, b) => { DeletePermanently(a, "E", 20); Change(b, "E", 30, "b"); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("deleted-on-A, B-changed-earlier(+10)",
                (a, b) => { DeletePermanently(a, "E", 20); Change(b, "E", 10, "b"); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("recycled-on-A, B-unchanged",
                (a, b) => Recycle(a, "E", 20)),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("recycled-on-A, B-changed-later(+30)",
                (a, b) => { Recycle(a, "E", 20); Change(b, "E", 30, "b"); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("moved-on-A-to-G2, B-unchanged",
                (a, b) => Move(a, "E", "G2", 20)),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("moved-on-A-to-G2, B-changed-later(+30)",
                (a, b) => { Move(a, "E", "G2", 20); Change(b, "E", 30, "b"); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("moved-on-A-to-G2(+20), moved-on-B-to-G3(+30)",
                (a, b) => { Move(a, "E", "G2", 20); Move(b, "E", "G3", 30); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("group-G2-deleted-on-A, B-unchanged",
                (a, b) => DeleteGroup(a, "G2", 20)),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("group-G2-deleted-on-A, B-added-N-to-G2(+30)",
                (a, b) => { DeleteGroup(a, "G2", 20); AddEntry(Group(b, "G2"), "N", At(30), "Rec", "n"); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("group-G2-deleted-on-A, B-added-N-to-G2(+10)",
                (a, b) => { DeleteGroup(a, "G2", 20); AddEntry(Group(b, "G2"), "N", At(10), "Rec", "n"); }),
            new KeyValuePair<string, Action<PwDatabase, PwDatabase>>("group-G2-deleted-on-A, B-changed-E2(+30)",
                (a, b) => { DeleteGroup(a, "G2", 20); Change(b, "E2", 30, "b"); }),
        };
        foreach (var s in scen)
        {
            var bas = BaseWithBin("x3-base", out ids);
            var a = Open(bas); var b = Open(bas);
            s.Value(a, b);
            var pa = Save(a, Next("x3-A")); var pb = Save(b, Next("x3-B"));
            Cells("X3 | " + s.Key, pa, pb, Tree);
        }
    }

    // ---------------------------------------------------------------- X4: tombstones
    static void X4()
    {
        Out.WriteLine("# X4 tombstones");
        // a tombstone for a uuid nothing holds: does it survive saves and merges?
        var db = NewDb();
        AddGroup(db.RootGroup, "KPG", At(0));
        db.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), At(5)));
        var bas = Save(db, Next("x4-base"));
        var r1 = Open(bas); Out.WriteLine("X4 | orphan tombstone after save and reopen | tomb=" + r1.DeletedObjects.UCount); r1.Close();
        var other = NewDb(); AddGroup(other.RootGroup, "KPG", At(0)); var po = Save(other, Next("x4-other"));
        Cells("X4 | orphan tombstone only in A", bas, po, d => "tomb=" + d.DeletedObjects.UCount.ToString(Inv));

        // the target's own tombstone, and an arriving entry it names
        var db2 = NewDb();
        var g = AddGroup(db2.RootGroup, "KPG", At(0));
        var e = AddEntry(g, "E", At(0), "Rec", "0");
        var bas2 = Save(db2, Next("x4-base2"));
        var a = Open(bas2); var pa = Save(a, Next("x4-A"));
        var b = Open(bas2); DeletePermanently(b, "E", 20); var pb = Save(b, Next("x4-B"));
        Cells("X4 | E deleted on B (+20), A unchanged (lm +0)", pa, pb, Tree);

        // the same with A's E changed after the deletion: the entry comes back and the tombstone goes
        var a3 = Open(bas2); Change(a3, "E", 30, "a"); var pa3 = Save(a3, Next("x4-A3"));
        Cells("X4 | E deleted on B (+20), A changed E (+30)", pa3, pb, Tree);
    }

    // ---------------------------------------------------------------- X5: history growth and limits
    static void X5()
    {
        Out.WriteLine("# X5 history growth and limits");
        foreach (var backup in new[] { true, false })
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            AddEntry(g, "E", At(0), "Rev", "0");
            var bas = Save(db, Next("x5-base"));
            var pa = Path.Combine(Work, Next("x5-A")); var pb = Path.Combine(Work, Next("x5-B"));
            File.Copy(bas, pa, true); File.Copy(bas, pb, true);
            for (int k = 1; k <= 14; ++k)
            {
                bool onA = (k % 2) == 1;
                var ed = Open(onA ? pa : pb);
                var ee = One(ed, "E");
                if (backup) ee.CreateBackup(null);
                ee.Strings.Set("Rev", new ProtectedString(false, k.ToString(Inv)));
                ee.LastModificationTime = At(10 * k);
                if (backup) ee.MaintainBackups(ed);
                ed.Save(null); ed.Close();
                // the other copy synchronizes with the edited one, and the result is written to both
                var target = onA ? pb : pa; var source = onA ? pa : pb;
                var t = Open(target); var s = Open(source);
                t.MergeIn(s, PwMergeMethod.Synchronize); s.Close();
                t.Save(null); t.Close();
                File.Copy(target, source, true);
                var r = Open(target); var re = One(r, "E");
                Out.WriteLine("X5 | cycles, editor backup=" + backup + " | round " + k + " | n=" + re.History.UCount + " revs=" +
                    string.Join(",", re.History.Select(h => h.Strings.ReadSafe("Rev"))) + " cur=" + re.Strings.ReadSafe("Rev"));
                r.Close();
            }
        }

        // the target's history limit governs, and a merge does not carry it
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            var e = AddEntry(g, "E", At(0), "Rev", "0");
            for (int k = 1; k <= 8; ++k) { e.CreateBackup(null); e.Strings.Set("Rev", new ProtectedString(false, k.ToString(Inv))); e.LastModificationTime = At(k); }
            var bas = Save(db, Next("x5-lim-base"));
            var a = Open(bas); a.HistoryMaxItems = 3; var ea = One(a, "E"); ea.CreateBackup(null); ea.Strings.Set("Rev", new ProtectedString(false, "a")); ea.LastModificationTime = At(20); var pa = Save(a, Next("x5-lim-A"));
            var b = Open(bas); var pb = Save(b, Next("x5-lim-B"));
            var rA = MergeInto(pa, pb, PwMergeMethod.Synchronize);
            Out.WriteLine("X5 | limits A=3 B=10, A edited | into A | n=" + One(rA, "E").History.UCount + " limitAfter=" + rA.HistoryMaxItems); rA.Close();
            var rB = MergeInto(pb, pa, PwMergeMethod.Synchronize);
            Out.WriteLine("X5 | limits A=3 B=10, A edited | into B | n=" + One(rB, "E").History.UCount + " limitAfter=" + rB.HistoryMaxItems); rB.Close();
            var a2 = Open(pa); Out.WriteLine("X5 | A as saved with limit 3, its own entry | n=" + One(a2, "E").History.UCount + " (the limit is applied by whoever calls MaintainBackups; the probe did not)"); a2.Close();
        }

        // the size limit, with a 1 MiB attachment
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            var e = AddEntry(g, "E", At(0), "Rev", "0");
            var rnd = new Random(7); var big = new byte[1024 * 1024]; rnd.NextBytes(big);
            e.Binaries.Set("big.bin", new ProtectedBinary(false, big));
            var bas = Save(db, Next("x5-size-base"));
            var pa = Path.Combine(Work, Next("x5-size-A")); var pb = Path.Combine(Work, Next("x5-size-B"));
            File.Copy(bas, pa, true); File.Copy(bas, pb, true);
            for (int k = 1; k <= 10; ++k)
            {
                var ed = Open(pa); var ee = One(ed, "E");
                ee.Strings.Set("Rev", new ProtectedString(false, k.ToString(Inv)));
                ee.LastModificationTime = At(10 * k);
                ed.Save(null); ed.Close();
                var t = Open(pb); var s = Open(pa);
                t.MergeIn(s, PwMergeMethod.Synchronize); s.Close(); t.Save(null); t.Close();
                File.Copy(pb, pa, true);
            }
            var r = Open(pb); var re = One(r, "E");
            Out.WriteLine("X5 | 1 MiB attachment, 10 synchronized edits without editor backup, size limit " + r.HistoryMaxSize + " | n=" + re.History.UCount + " revs=" + string.Join(",", re.History.Select(h => h.Strings.ReadSafe("Rev"))));
            r.Close();
        }

        // two history items with the same modification time, one from each copy
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            AddEntry(g, "E", At(0), "Rev", "0");
            var bas = Save(db, Next("x5-col-base"));
            var a = Open(bas); var ea = One(a, "E");
            ea.CreateBackup(null); ea.Strings.Set("Rev", new ProtectedString(false, "a1")); ea.LastModificationTime = At(10);
            ea.CreateBackup(null); ea.Strings.Set("Rev", new ProtectedString(false, "a2")); ea.LastModificationTime = At(20);
            var pa = Save(a, Next("x5-col-A"));
            var b = Open(bas); var eb = One(b, "E");
            eb.CreateBackup(null); eb.Strings.Set("Rev", new ProtectedString(false, "b1")); eb.LastModificationTime = At(10);
            eb.CreateBackup(null); eb.Strings.Set("Rev", new ProtectedString(false, "b2")); eb.LastModificationTime = At(30);
            var pb = Save(b, Next("x5-col-B"));
            foreach (var mm in Methods)
            {
                var r = MergeInto(pa, pb, mm);
                foreach (var e in Entries(r, "E")) Out.WriteLine("X5 | colliding history times | into A | " + M(mm) + " | " + Desc(e));
                r.Close();
                r = MergeInto(pb, pa, mm);
                foreach (var e in Entries(r, "E")) Out.WriteLine("X5 | colliding history times | into B | " + M(mm) + " | " + Desc(e));
                r.Close();
            }
        }
    }

    // ---------------------------------------------------------------- X6: groups
    static string GDesc(PwGroup g)
    {
        if (g == null) return "absent";
        return "notes=" + g.Notes + " tags=" + string.Join("/", g.Tags) + " search=" + (g.EnableSearching.HasValue ? g.EnableSearching.Value.ToString() : "inherit") +
            " autotype=" + (g.EnableAutoType.HasValue ? g.EnableAutoType.Value.ToString() : "inherit") + " expanded=" + g.IsExpanded +
            " cd=" + string.Join(",", g.CustomData.Select(kv => kv.Key + "=" + kv.Value)) + " lm=" + Rel(g.LastModificationTime);
    }

    static void X6()
    {
        Out.WriteLine("# X6 groups: notes, tags, searching, auto-type, expanded, custom data");
        foreach (var t in new[] { new[] { 20, 10 }, new[] { 10, 20 }, new[] { 10, 10 } })
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KG", At(0));
            g.Notes = "N0"; g.Tags = new List<string> { "t0" }; g.EnableSearching = false; g.EnableAutoType = false; g.IsExpanded = false;
            g.CustomData.Set("k", "v0");
            g.LastModificationTime = At(0);
            var bas = Save(db, Next("x6-base"));
            var a = Open(bas); var ga = Group(a, "KG"); ga.Notes = "NA"; ga.LastModificationTime = At(t[0]);
            var na = AddGroup(a.RootGroup, "NewOnA", At(5)); na.IsExpanded = false; na.EnableSearching = false; na.Notes = "new";
            var pa = Save(a, Next("x6-A"));
            var b = Open(bas); var gb = Group(b, "KG"); gb.Tags = new List<string> { "tB" }; gb.CustomData.Set("k", "vB"); gb.IsExpanded = true; gb.LastModificationTime = At(t[1]);
            var pb = Save(b, Next("x6-B"));
            string label = "X6 | A notes at +" + t[0] + ", B tags, custom data and expanded at +" + t[1];
            Cells(label, pa, pb, d => "KG: " + string.Join(" || ", d.RootGroup.GetGroups(true).Where(x => x.Name == "KG").Select(GDesc)) +
                " ;; NewOnA: " + string.Join(" || ", d.RootGroup.GetGroups(true).Where(x => x.Name == "NewOnA").Select(GDesc)));
        }
    }

    // ---------------------------------------------------------------- X7: which KDBX version each feature forces
    static string Version(string path)
    {
        var b = File.ReadAllBytes(path);
        int minor = b[8] | (b[9] << 8), major = b[10] | (b[11] << 8);
        return major.ToString(Inv) + "." + minor.ToString(Inv);
    }

    static void X7()
    {
        Out.WriteLine("# X7 file version KeePass 2.62 writes for each feature (AES-KDF, one round)");
        var cases = new List<KeyValuePair<string, Action<PwDatabase>>>
        {
            new KeyValuePair<string, Action<PwDatabase>>("empty database", d => { }),
            new KeyValuePair<string, Action<PwDatabase>>("entry: standard, custom, protected strings, attachment, entry tag, notes, standard icon, expiry, history, colors, override URL, auto-type off", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0));
                var e = AddEntry(g, "E", At(0), "Custom", "c");
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw"));
                e.Strings.Set("Secret", new ProtectedString(true, "s"));
                e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "{\"json\":1}"));
                e.Binaries.Set("x.bin", new ProtectedBinary(false, new byte[] { 1 }));
                e.AddTag("kpg"); e.IconId = PwIcon.Key; e.Expires = true; e.ExpiryTime = At(1000);
                e.CreateBackup(null); e.Strings.Set("Custom", new ProtectedString(false, "c2"));
                e.ForegroundColor = System.Drawing.Color.Red; e.OverrideUrl = "cmd://x"; e.AutoType.Enabled = false; }),
            new KeyValuePair<string, Action<PwDatabase>>("entry excluded from reports (QualityCheck false)", d => {
                var e = AddEntry(d.RootGroup, "E", At(0)); e.QualityCheck = false; }),
            new KeyValuePair<string, Action<PwDatabase>>("group tag", d => { var g = AddGroup(d.RootGroup, "KPG", At(0)); g.Tags = new List<string> { "kpg" }; }),
            new KeyValuePair<string, Action<PwDatabase>>("group notes, searching off, auto-type off, collapsed", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0)); g.Notes = "{\"json\":1}"; g.EnableSearching = false; g.EnableAutoType = false; g.IsExpanded = false; }),
            new KeyValuePair<string, Action<PwDatabase>>("entry custom data", d => { var e = AddEntry(d.RootGroup, "E", At(0)); e.CustomData.Set("KPG", "{}"); }),
            new KeyValuePair<string, Action<PwDatabase>>("group custom data", d => { var g = AddGroup(d.RootGroup, "KPG", At(0)); g.CustomData.Set("KPG", "{}"); }),
            new KeyValuePair<string, Action<PwDatabase>>("database custom data, public Set (stamped)", d => d.CustomData.Set("KPG.x", "1")),
            new KeyValuePair<string, Action<PwDatabase>>("header public custom data", d => d.PublicCustomData.SetString("KPG.x", "1")),
            new KeyValuePair<string, Action<PwDatabase>>("custom icon, no name, no time", d => d.CustomIcons.Add(new PwCustomIcon(new PwUuid(true), Png()))),
            new KeyValuePair<string, Action<PwDatabase>>("custom icon with a name", d => { var ci = new PwCustomIcon(new PwUuid(true), Png()); ci.Name = "KPG"; d.CustomIcons.Add(ci); }),
            new KeyValuePair<string, Action<PwDatabase>>("custom icon with a time", d => { var ci = new PwCustomIcon(new PwUuid(true), Png()); ci.LastModificationTime = At(0); d.CustomIcons.Add(ci); }),
            new KeyValuePair<string, Action<PwDatabase>>("deleted object", d => d.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), At(0)))),
            new KeyValuePair<string, Action<PwDatabase>>("entry previous parent only", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0)); var e = AddEntry(d.RootGroup, "E", At(0)); e.PreviousParentGroup = g.Uuid; }),
            new KeyValuePair<string, Action<PwDatabase>>("entry previous parent and a group tag", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0)); g.Tags = new List<string> { "kpg" }; var e = AddEntry(d.RootGroup, "E", At(0)); e.PreviousParentGroup = g.Uuid; }),
        };
        foreach (var c in cases)
        {
            var db = NewDb();
            c.Value(db);
            var p = Save(db, Next("x7"));
            var r = Open(p);
            string survived = "";
            var e = r.RootGroup.GetEntries(true).FirstOrDefault();
            if (c.Key.StartsWith("entry previous parent", StringComparison.Ordinal)) survived = " previousParentAfterReopen=" + (e != null && !e.PreviousParentGroup.IsZero);
            if (c.Key.StartsWith("entry:", StringComparison.Ordinal)) survived = " reopened=" + Desc(e) + " expires=" + e.Expires + " qc=" + e.QualityCheck;
            if (c.Key.StartsWith("header public", StringComparison.Ordinal)) survived = " pcd=" + (r.PublicCustomData.GetString("KPG.x") ?? "(none)");
            Out.WriteLine("X7 | " + c.Key + " | " + Version(p) + survived);
            r.Close();
        }
        // the header can be read without the key: public custom data is readable by anyone holding the file
        {
            var db = NewDb(); db.PublicCustomData.SetString("KPG.x", "visible"); var p = Save(db, Next("x7-pcd"));
            var h = PwDatabase.LoadHeader(IOConnectionInfo.FromPath(p));
            Out.WriteLine("X7 | public custom data read with LoadHeader and no key | " + (h.PublicCustomData.GetString("KPG.x") ?? "(none)"));
        }
    }

    static byte[] Png()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    }

    // ---------------------------------------------------------------- X8: header public custom data
    static void X8()
    {
        Out.WriteLine("# X8 header public custom data: A changes k0 and adds kA, B adds kB");
        foreach (var t in new[] { new[] { 20, 10 }, new[] { 10, 20 }, new[] { 10, 10 } })
        {
            var db = NewDb(); db.PublicCustomData.SetString("k0", "v0"); var bas = Save(db, Next("x8-base"));
            var a = Open(bas); a.PublicCustomData.SetString("k0", "vA"); a.PublicCustomData.SetString("kA", "a"); a.SettingsChanged = At(t[0]); var pa = Save(a, Next("x8-A"));
            var b = Open(bas); b.PublicCustomData.SetString("kB", "b"); b.SettingsChanged = At(t[1]); var pb = Save(b, Next("x8-B"));
            Cells("X8 | settings changed A +" + t[0] + ", B +" + t[1], pa, pb, d =>
            {
                var keys = new List<string>();
                foreach (var k in d.PublicCustomData.KnownKeys()) keys.Add(k + "=" + d.PublicCustomData.GetString(k));
                return string.Join(",", keys);
            });
        }
    }

    static IEnumerable<string> KnownKeys(this VariantDictionary vd)
    {
        var l = new List<string>();
        foreach (var k in new[] { "k0", "kA", "kB" }) if (vd.GetString(k) != null) l.Add(k);
        return l;
    }

    // ---------------------------------------------------------------- X9: custom icons
    static void X9()
    {
        Out.WriteLine("# X9 custom icons: base I0; A adds I1 and deletes I0; B adds I2");
        foreach (var timed in new[] { false, true })
        {
            var db = NewDb();
            var i0 = new PwCustomIcon(new PwUuid(true), Png()); i0.Name = timed ? "I0" : ""; if (timed) i0.LastModificationTime = At(0);
            db.CustomIcons.Add(i0);
            var names = new Dictionary<string, string> { { i0.Uuid.ToHexString(), "I0" } };
            var bas = Save(db, Next("x9-base"));
            var a = Open(bas);
            var i1 = new PwCustomIcon(new PwUuid(true), Png()); if (timed) { i1.Name = "I1"; i1.LastModificationTime = At(10); }
            a.CustomIcons.Add(i1); names[i1.Uuid.ToHexString()] = "I1";
            a.DeleteCustomIcons(new List<PwUuid> { i0.Uuid });
            var pa = Save(a, Next("x9-A"));
            var b = Open(bas);
            var i2 = new PwCustomIcon(new PwUuid(true), Png()); if (timed) { i2.Name = "I2"; i2.LastModificationTime = At(20); }
            b.CustomIcons.Add(i2); names[i2.Uuid.ToHexString()] = "I2";
            var pb = Save(b, Next("x9-B"));
            Cells("X9 | icon times " + (timed ? "kept" : "absent") + " | version A " + Version(pa), pa, pb, d =>
                string.Join(",", d.CustomIcons.Select(ci => names.ContainsKey(ci.Uuid.ToHexString()) ? names[ci.Uuid.ToHexString()] : "?")) + " tomb=" + d.DeletedObjects.UCount);
        }
    }

    // ---------------------------------------------------------------- X10: KeePassLib functions KeePass's menus call
    static void X10()
    {
        Out.WriteLine("# X10 Delete Duplicate Entries and Delete Empty Groups (the KeePassLib calls behind the Tools menu)");
        var cases = new List<KeyValuePair<string, Action<PwDatabase>>>
        {
            new KeyValuePair<string, Action<PwDatabase>>("two entries in one group, same strings, different entry custom data", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0));
                var x = AddEntry(g, "R", At(0), "Rule", "r"); x.CustomData.Set("k", "1");
                var y = AddEntry(g, "R", At(5), "Rule", "r"); y.CustomData.Set("k", "2"); }),
            new KeyValuePair<string, Action<PwDatabase>>("two entries, same strings, different tags", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0));
                var x = AddEntry(g, "R", At(0), "Rule", "r"); x.AddTag("a");
                var y = AddEntry(g, "R", At(5), "Rule", "r"); y.AddTag("b"); }),
            new KeyValuePair<string, Action<PwDatabase>>("two meta entries in two different user groups, same strings", d => {
                var g1 = AddGroup(d.RootGroup, "Mail", At(0)); var g2 = AddGroup(d.RootGroup, "Bank", At(0));
                AddEntry(g1, "KPG", At(0), "Rule", "{\"agent\":\"R.L.....\"}");
                AddEntry(g2, "KPG", At(5), "Rule", "{\"agent\":\"R.L.....\"}"); }),
            new KeyValuePair<string, Action<PwDatabase>>("two meta entries in two groups, same strings but the group's uuid in a field", d => {
                var g1 = AddGroup(d.RootGroup, "Mail", At(0)); var g2 = AddGroup(d.RootGroup, "Bank", At(0));
                AddEntry(g1, "KPG", At(0), "Rule", "{}", "Group", g1.Uuid.ToHexString());
                AddEntry(g2, "KPG", At(5), "Rule", "{}", "Group", g2.Uuid.ToHexString()); }),
            new KeyValuePair<string, Action<PwDatabase>>("two entries, same strings, different attachment names with the same bytes", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0));
                var x = AddEntry(g, "R", At(0)); x.Binaries.Set("a", new ProtectedBinary(false, new byte[] { 1 }));
                var y = AddEntry(g, "R", At(5)); y.Binaries.Set("b", new ProtectedBinary(false, new byte[] { 1 })); }),
            new KeyValuePair<string, Action<PwDatabase>>("two entries, same strings, one in the recycle bin and newer", d => {
                var g = AddGroup(d.RootGroup, "KPG", At(0)); var bin = AddGroup(d.RootGroup, "Bin", At(0));
                d.RecycleBinEnabled = true; d.RecycleBinUuid = bin.Uuid;
                AddEntry(g, "R", At(0), "Rule", "r"); AddEntry(bin, "R", At(50), "Rule", "r"); }),
        };
        foreach (var c in cases)
        {
            var db = NewDb(); c.Value(db);
            uint n = db.DeleteDuplicateEntries(null);
            Out.WriteLine("X10 | DeleteDuplicateEntries | " + c.Key + " | deleted=" + n + " | " + Tree(db));
            db.Close();
        }
        {
            var db = NewDb();
            var kg = AddGroup(db.RootGroup, "KPG-notes-only", At(0)); kg.Notes = "{\"store\":1}"; kg.CustomData.Set("KPG", "{}");
            var kg2 = AddGroup(db.RootGroup, "KPG-with-entry", At(0)); AddEntry(kg2, "E", At(0));
            var bin = AddGroup(db.RootGroup, "Bin", At(0)); db.RecycleBinEnabled = true; db.RecycleBinUuid = bin.Uuid;
            var parent = AddGroup(db.RootGroup, "Parent", At(0)); AddGroup(parent, "EmptyChild", At(0));
            uint n = db.DeleteEmptyGroups();
            Out.WriteLine("X10 | DeleteEmptyGroups | a notes-only group, a group with an entry, an empty recycle bin, a parent of an empty child | deleted=" + n + " | " + Tree(db) + " | recycleBinUuidStill=" + (db.RecycleBinUuid.Equals(bin.Uuid)));
        }
    }

    // ---------------------------------------------------------------- X11: search
    static void X11()
    {
        Out.WriteLine("# X11 search: what KeePass's searches find in a group with searching off");
        var db = NewDb();
        var kg = AddGroup(db.RootGroup, "KPG", At(0)); kg.EnableSearching = false;
        var e = AddEntry(kg, "kpg-store", At(0), "KPG.20261115T000010Z", "{\"needle\":1}");
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "needle in notes"));
        var u = AddGroup(db.RootGroup, "User", At(0));
        AddEntry(u, "user-entry", At(0), "Other", "no match here");
        foreach (var respect in new[] { true, false })
        {
            var sp = SearchParameters.None;
            sp.SearchString = "needle"; sp.SearchInOther = true; sp.SearchInNotes = true;
            sp.RespectEntrySearchingDisabled = respect;
            var found = new PwObjectList<PwEntry>();
            db.RootGroup.SearchEntries(sp, found);
            Out.WriteLine("X11 | 'needle' in other fields and notes, RespectEntrySearchingDisabled=" + respect + " | found=" + string.Join(",", found.Select(x => x.Strings.ReadSafe(PwDefs.TitleField))));
        }
        var spd = new SearchParameters();
        Out.WriteLine("X11 | SearchParameters defaults | RespectEntrySearchingDisabled=" + spd.RespectEntrySearchingDisabled + " SearchInOther=" + spd.SearchInOther + " SearchInNotes=" + spd.SearchInNotes);
        // a field reference {REF:T@O:needle} resolves with FindRefTarget's parameters: SearchParameters.None, one field, RespectEntrySearchingDisabled false
        var spr = SearchParameters.None; spr.SearchString = "needle"; spr.SearchInOther = true; spr.RespectEntrySearchingDisabled = false;
        var fr = new PwObjectList<PwEntry>(); db.RootGroup.SearchEntries(spr, fr);
        Out.WriteLine("X11 | what {REF:x@O:needle} would search, parameters as SprEngine.FindRefTarget sets them | first=" + (fr.UCount > 0 ? fr.GetAt(0).Strings.ReadSafe(PwDefs.TitleField) : "(none)"));
    }

    // ---------------------------------------------------------------- X12: an entry whose file carries no modification time
    static void X12()
    {
        Out.WriteLine("# X12 an entry read from XML with no LastModificationTime element");
        var db = NewDb();
        var g = AddGroup(db.RootGroup, "KPG", At(0));
        AddEntry(g, "E", At(0), "Rec", "0");
        string xml;
        using (var ms = new MemoryStream())
        {
            new KdbxFile(db).Save(ms, null, KdbxFormat.PlainXml, null);
            xml = Encoding.UTF8.GetString(ms.ToArray());
        }
        int ie = xml.IndexOf("<Entry>", StringComparison.Ordinal);
        int s = xml.IndexOf("<LastModificationTime>", ie, StringComparison.Ordinal);
        int t = xml.IndexOf("</LastModificationTime>", s, StringComparison.Ordinal) + "</LastModificationTime>".Length;
        var stripped = xml.Substring(0, s) + xml.Substring(t);
        var pd = new PwDatabase();
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(stripped)))
            new KdbxFile(pd).Load(ms, KdbxFormat.PlainXml, null);
        var e = pd.RootGroup.GetEntries(true).Single();
        Out.WriteLine("X12 | entry with no LastModificationTime | lm=" + Rel(e.LastModificationTime) + " PwDefs.DtDefaultNow=" + Rel(PwDefs.DtDefaultNow) + " now=" + Rel(DateTime.UtcNow));
    }

    // ---------------------------------------------------------------- X13: the timestamp-named field design
    static HashSet<string> Kpg(PwEntry e, bool withHistory)
    {
        var s = new HashSet<string>();
        foreach (var kv in e.Strings) if (kv.Key.StartsWith("KPG.", StringComparison.Ordinal)) s.Add(kv.Key);
        if (withHistory)
            foreach (var h in e.History)
                foreach (var kv in h.Strings) if (kv.Key.StartsWith("KPG.", StringComparison.Ordinal)) s.Add(kv.Key);
        return s;
    }

    // what a plugin could do on load and before a save: copy back into the entry every KPG field
    // only a history item still holds, and move the time on past everything it has seen
    static void Preprocess(PwDatabase db)
    {
        var e = One(db, "E");
        bool changed = false;
        DateTime max = e.LastModificationTime;
        foreach (var h in e.History)
        {
            if (h.LastModificationTime > max) max = h.LastModificationTime;
            foreach (var kv in h.Strings)
                if (kv.Key.StartsWith("KPG.", StringComparison.Ordinal) && !e.Strings.Exists(kv.Key))
                { e.Strings.Set(kv.Key, kv.Value); changed = true; }
        }
        if (changed) e.LastModificationTime = max.AddSeconds(1);
    }

    static void X13()
    {
        Out.WriteLine("# X13 each copy adds a field named by its own timestamp every round, then a Synchronize writes the result to both");
        foreach (var pre in new[] { false, true })
            foreach (var dB in new[] { 5, 0 })
            {
                var db = NewDb();
                var g = AddGroup(db.RootGroup, "KPG", At(0));
                AddEntry(g, "E", At(0));
                var bas = Save(db, Next("x13-base"));
                var pa = Path.Combine(Work, Next("x13-A")); var pb = Path.Combine(Work, Next("x13-B"));
                File.Copy(bas, pa, true); File.Copy(bas, pb, true);
                var written = new HashSet<string>();
                for (int k = 1; k <= 12; ++k)
                {
                    var a = Open(pa); if (pre) Preprocess(a);
                    var ea = One(a, "E"); var fa = "KPG.A" + k.ToString("00", Inv);
                    ea.Strings.Set(fa, new ProtectedString(false, "{\"by\":\"A\"}")); ea.LastModificationTime = At(100 * k);
                    a.Save(null); a.Close(); written.Add(fa);
                    var b = Open(pb); if (pre) Preprocess(b);
                    var eb = One(b, "E"); var fb = "KPG.B" + k.ToString("00", Inv);
                    eb.Strings.Set(fb, new ProtectedString(false, "{\"by\":\"B\"}")); eb.LastModificationTime = At(100 * k + dB);
                    b.Save(null); b.Close(); written.Add(fb);
                    var t = Open((k % 2) == 1 ? pb : pa); var s = Open((k % 2) == 1 ? pa : pb);
                    t.MergeIn(s, PwMergeMethod.Synchronize); s.Close();
                    if (pre) Preprocess(t);
                    t.Save(null); t.Close();
                    if ((k % 2) == 1) File.Copy(pb, pa, true); else File.Copy(pa, pb, true);
                    var r = Open(pa); var re = One(r, "E");
                    var cur = Kpg(re, false); var all = Kpg(re, true);
                    Out.WriteLine("X13 | preprocess=" + pre + " | B writes " + dB + " s after A | round " + k + " | written=" + written.Count +
                        " inEntry=" + cur.Count + " inEntryOrHistory=" + all.Count + " hist=" + re.History.UCount +
                        " lost=" + string.Join(",", written.Where(w => !all.Contains(w)).OrderBy(w => w)));
                    r.Close();
                }
            }

        // the same with preprocessing, B 5 s after A, but the database keeps no history (an operator setting)
        foreach (var limit in new[] { 0, 1 })
        {
            var db = NewDb();
            db.HistoryMaxItems = limit;
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            AddEntry(g, "E", At(0));
            var bas = Save(db, Next("x13-h-base"));
            var pa = Path.Combine(Work, Next("x13-h-A")); var pb = Path.Combine(Work, Next("x13-h-B"));
            File.Copy(bas, pa, true); File.Copy(bas, pb, true);
            var written = new HashSet<string>();
            for (int k = 1; k <= 4; ++k)
            {
                var a = Open(pa); Preprocess(a);
                var ea = One(a, "E"); var fa = "KPG.A" + k.ToString("00", Inv);
                ea.Strings.Set(fa, new ProtectedString(false, "a")); ea.LastModificationTime = At(100 * k);
                a.Save(null); a.Close(); written.Add(fa);
                var b = Open(pb); Preprocess(b);
                var eb = One(b, "E"); var fb = "KPG.B" + k.ToString("00", Inv);
                eb.Strings.Set(fb, new ProtectedString(false, "b")); eb.LastModificationTime = At(100 * k + 5);
                b.Save(null); b.Close(); written.Add(fb);
                var t = Open(pb); var s = Open(pa);
                t.MergeIn(s, PwMergeMethod.Synchronize); s.Close(); Preprocess(t); t.Save(null); t.Close();
                File.Copy(pb, pa, true);
                var r = Open(pa); var re = One(r, "E"); var all = Kpg(re, true);
                Out.WriteLine("X13 | preprocess=True, HistoryMaxItems=" + limit + " | round " + k + " | written=" + written.Count + " inEntryOrHistory=" + all.Count +
                    " lost=" + string.Join(",", written.Where(w => !all.Contains(w)).OrderBy(w => w)));
                r.Close();
            }
        }

        // three copies, one Synchronize after another, no preprocessing
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            AddEntry(g, "E", At(0));
            var bas = Save(db, Next("x13-3-base"));
            var paths = new List<string>();
            for (int i = 0; i < 3; ++i)
            {
                var c = Open(bas); var ec = One(c, "E");
                ec.Strings.Set("KPG.C" + i, new ProtectedString(false, "x")); ec.LastModificationTime = At(10 * (i + 1));
                paths.Add(Save(c, Next("x13-3-C" + i)));
            }
            var t = Open(paths[0]);
            foreach (var p in paths.Skip(1)) { var s = Open(p); t.MergeIn(s, PwMergeMethod.Synchronize); s.Close(); }
            var rp = Save(t, Next("x13-3-r"));
            var r = Open(rp); var re = One(r, "E");
            Out.WriteLine("X13 | three copies each adding one field, merged into the first | inEntry=" + string.Join(",", Kpg(re, false).OrderBy(x => x)) +
                " inEntryOrHistory=" + string.Join(",", Kpg(re, true).OrderBy(x => x)) + " | " + Desc(re));
            r.Close();
        }

        // the same content rewritten on both sides with new times: history fills with copies that differ only in time
        {
            var db = NewDb();
            var g = AddGroup(db.RootGroup, "KPG", At(0));
            AddEntry(g, "E", At(0), "Rec", "same");
            var bas = Save(db, Next("x13-same-base"));
            var pa = Path.Combine(Work, Next("x13-same-A")); var pb = Path.Combine(Work, Next("x13-same-B"));
            File.Copy(bas, pa, true); File.Copy(bas, pb, true);
            for (int k = 1; k <= 12; ++k)
            {
                var ed = Open((k % 2) == 1 ? pa : pb); var ee = One(ed, "E");
                ee.Strings.Set("Rec", new ProtectedString(false, "same")); ee.LastModificationTime = At(10 * k);
                ed.Save(null); ed.Close();
                var t = Open((k % 2) == 1 ? pb : pa); var s = Open((k % 2) == 1 ? pa : pb);
                t.MergeIn(s, PwMergeMethod.Synchronize); s.Close(); t.Save(null); t.Close();
                if ((k % 2) == 1) File.Copy(pb, pa, true); else File.Copy(pa, pb, true);
            }
            var r = Open(pa); var re = One(r, "E");
            Out.WriteLine("X13 | identical value rewritten with a new time, 12 synchronized rounds | hist=" + re.History.UCount + " distinctValuesInHistory=" +
                re.History.Select(h => h.Strings.ReadSafe("Rec")).Distinct().Count());
            r.Close();
        }
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        var now = DateTime.UtcNow;
        T0 = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc).AddHours(-2);
        var which = args.Length > 1 ? args[1] : "all";
        var outPath = args.Length > 2 ? args[2] : Path.Combine(Work, "out-" + which + ".txt");
        using (Out = new StreamWriter(outPath, false, new UTF8Encoding(false)))
        {
            Out.WriteLine("KeePassLib " + PwDefs.VersionString + ", T0 " + T0.ToString("o", Inv) + ", process " + (Environment.Is64BitProcess ? "x64" : "x86") + ", " + Environment.Version);
            var map = new Dictionary<string, Action> {
                { "x1", X1 }, { "x2", X2 }, { "x3", X3 }, { "x4", X4 }, { "x5", X5 }, { "x6", X6 },
                { "x7", X7 }, { "x8", X8 }, { "x9", X9 }, { "x10", X10 }, { "x11", X11 }, { "x12", X12 }, { "x13", X13 } };
            foreach (var kv in map)
                if (which == "all" || which == kv.Key) kv.Value();
        }
        Console.WriteLine("wrote " + outPath);
        return 0;
    }
}
