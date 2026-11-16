using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

// A console probe over KeePassLib 2.62: makes a base database and two copies of it, A and B,
// each changed in its own way, and prints any database as a canonical text dump.
static class Xcp
{
    const string Password = "probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static DateTime At(double hours) { return T0.AddHours(hours); }

    static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "make") { Make(args[1], args[2]); return 0; }
        if (args.Length >= 3 && args[0] == "make2") { Make2(args[1], args[2]); return 0; }
        if (args.Length >= 4 && args[0] == "kpsync")
        {
            // KeePassLib's own Synchronize, the merge behind KeePass's File then Synchronize.
            var target = Open(args[1]);
            var source = Open(args[2]);
            target.MergeIn(source, PwMergeMethod.Synchronize, null);
            target.SaveAs(IOConnectionInfo.FromPath(args[3]), false, null);
            target.Close();
            source.Close();
            return 0;
        }
        if (args.Length >= 2 && args[0] == "dump") { for (int i = 1; i < args.Length; i++) Dump(args[i]); return 0; }
        Console.Error.WriteLine("usage: make <dir> <fast|slow|aes> | dump <file>...");
        return 2;
    }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Password)); return k; }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static KdfParameters Kdf(string name)
    {
        if (name == "aes")
        {
            var p = new AesKdf().GetDefaultParameters();
            p.SetUInt64(AesKdf.ParamRounds, 100000UL);
            return p;
        }
        var a = new Argon2Kdf().GetDefaultParameters();
        bool slow = name == "slow";
        a.SetUInt64(Argon2Kdf.ParamIterations, slow ? 200UL : 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, (slow ? 65536UL : 1024UL) * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, slow ? 2U : 1U);
        return a;
    }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup G(PwGroup parent, string name, DateTime t)
    {
        var g = new PwGroup(true, true, name, PwIcon.Folder);
        parent.AddGroup(g, true, false);
        Stamp(g, t);
        return g;
    }

    static PwEntry E(PwGroup parent, string title, DateTime t)
    {
        var e = new PwEntry(true, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        parent.AddEntry(e, true, false);
        Stamp(e, t);
        return e;
    }

    static void S(PwEntry e, string key, string value, bool prot = false) { e.Strings.Set(key, new ProtectedString(prot, value)); }

    static byte[] Bytes(string seed, int length)
    {
        var result = new byte[length];
        using (var sha = SHA256.Create())
        {
            int at = 0, n = 0;
            while (at < length)
            {
                var block = sha.ComputeHash(Encoding.UTF8.GetBytes(seed + ":" + (n++)));
                int take = Math.Min(block.Length, length - at);
                Array.Copy(block, 0, result, at, take);
                at += take;
            }
        }
        return result;
    }

    // A change as KeePass makes one: the current state goes into history first.
    static void Change(PwEntry e, DateTime t, Action what)
    {
        e.CreateBackup(null);
        what();
        e.LastModificationTime = t;
        e.LastAccessTime = t;
    }

    static PwEntry Find(PwDatabase db, string title)
    {
        foreach (var e in db.RootGroup.GetEntries(true))
            if (e.Strings.ReadSafe(PwDefs.TitleField) == title) return e;
        throw new InvalidOperationException("no entry " + title);
    }

    static PwGroup FindG(PwDatabase db, string name)
    {
        foreach (var g in db.RootGroup.GetGroups(true))
            if (g.Name == name) return g;
        throw new InvalidOperationException("no group " + name);
    }

    static void Move(PwEntry e, PwGroup to, DateTime t)
    {
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        to.AddEntry(e, true, false);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = t;
    }

    static void Delete(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    static readonly string[] KpgEntries =
    {
        "both-diff", "both-tie", "both-samefield", "one-A", "rewound-B", "prot-A", "att-A", "att-both",
        "tag-A", "tag-both", "cd-both", "notes-A", "move-A", "move-A-edit-B", "del-A-unch",
        "del-A-chB-after", "del-A-chB-before", "bin-A", "hist", "hist-many", "hist-big",
    };

    static void Make(string dir, string kdf)
    {
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        db.KdfParameters = Kdf(kdf);
        db.Name = "xcp";
        db.HistoryMaxItems = 10;
        db.HistoryMaxSize = 1024 * 1024;
        db.RecycleBinEnabled = true;
        var root = db.RootGroup;
        root.Name = "Root";
        Stamp(root, T0);

        var general = G(root, "General", T0);
        for (int i = 1; i <= 3; i++)
        {
            var o = E(general, "Ordinary" + i, T0);
            S(o, PwDefs.UserNameField, "user" + i);
            S(o, PwDefs.PasswordField, "invented-password-" + i, true);
            S(o, PwDefs.UrlField, "https://example.invalid/" + i);
        }

        var kpg = G(root, "KPG", T0);
        kpg.Notes = "kpg notes v0";
        G(kpg, "Sub", T0);
        G(kpg, "GNotesBoth", T0).Notes = "n0";
        G(kpg, "GNotesA", T0).Notes = "n0";
        G(kpg, "GTagsA", T0);
        G(kpg, "GDelA", T0);

        foreach (var name in KpgEntries)
        {
            var e = E(kpg, name, T0);
            S(e, "Data", "0");
        }
        S(Find(db, "both-samefield"), "Shared", "0");
        S(Find(db, "prot-A"), "Secret", "s0", true);
        Find(db, "att-A").Binaries.Set("blob.bin", new ProtectedBinary(false, Bytes("blob-v0", 1000)));
        Find(db, "att-both").Binaries.Set("base.bin", new ProtectedBinary(false, Bytes("base", 1000)));
        Find(db, "tag-both").Tags.Add("t0");
        Find(db, "cd-both").CustomData.Set("cd0", "0");
        S(Find(db, "notes-A"), PwDefs.NotesField, "n0");

        var hist = Find(db, "hist");
        Stamp(hist, At(-2));
        S(hist, "Data", "h1");
        Change(hist, At(-1), () => S(hist, "Data", "h2"));
        Change(hist, T0, () => S(hist, "Data", "0"));

        var many = Find(db, "hist-many");
        Stamp(many, At(-9));
        S(many, "Data", "m1");
        for (int i = 2; i <= 9; i++) { int k = i; Change(many, At(-10 + i), () => S(many, "Data", "m" + k)); }
        Change(many, T0, () => S(many, "Data", "0"));

        var big = Find(db, "hist-big");
        Stamp(big, At(-4));
        big.Binaries.Set("big.bin", new ProtectedBinary(false, Bytes("big1", 400 * 1024)));
        for (int i = 2; i <= 4; i++) { int k = i; Change(big, At(-5 + i), () => big.Binaries.Set("big.bin", new ProtectedBinary(false, Bytes("big" + k, 400 * 1024)))); }
        Change(big, T0, () => big.Binaries.Set("big.bin", new ProtectedBinary(false, Bytes("big5", 400 * 1024))));
        foreach (var e in new[] { hist, many, big }) { e.CreationTime = At(-10); e.LocationChanged = At(-10); }

        db.Save(null);
        db.Close();
        Console.WriteLine("wrote " + basePath);

        SideA(basePath, Path.Combine(dir, "A.kdbx"));
        SideB(basePath, Path.Combine(dir, "B.kdbx"));
    }

    static PwDatabase CopyOpen(string from, string to)
    {
        File.Copy(from, to, true);
        return Open(to);
    }

    static void SideA(string basePath, string path)
    {
        var db = CopyOpen(basePath, path);
        var a = At(1);
        var kpg = FindG(db, "KPG");
        var sub = FindG(db, "Sub");

        var e = Find(db, "both-diff"); Change(e, a, () => S(e, "FieldA", "a"));
        var f = Find(db, "both-tie"); Change(f, a, () => S(f, "FieldA", "a"));
        var g = Find(db, "both-samefield"); Change(g, a, () => S(g, "Shared", "A"));
        var h = Find(db, "one-A"); Change(h, a, () => S(h, "Data", "A"));
        var p = Find(db, "prot-A"); Change(p, a, () => S(p, "Secret", "sA", true));
        var at = Find(db, "att-A"); Change(at, a, () => at.Binaries.Set("blob.bin", new ProtectedBinary(false, Bytes("blob-v1", 1000))));
        var ab = Find(db, "att-both"); Change(ab, a, () => ab.Binaries.Set("a.bin", new ProtectedBinary(false, Bytes("a", 1000))));
        var t = Find(db, "tag-A"); Change(t, a, () => t.Tags.Add("tagA"));
        var tb = Find(db, "tag-both"); Change(tb, a, () => tb.Tags.Add("tagA"));
        var cd = Find(db, "cd-both"); Change(cd, a, () => cd.CustomData.Set("cdA", "a"));
        var n = Find(db, "notes-A"); Change(n, a, () => S(n, PwDefs.NotesField, "nA"));
        Move(Find(db, "move-A"), sub, a);
        Move(Find(db, "move-A-edit-B"), sub, a);
        Delete(db, Find(db, "del-A-unch"), a);
        Delete(db, Find(db, "del-A-chB-after"), a);
        Delete(db, Find(db, "del-A-chB-before"), a);

        var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
        bin.EnableAutoType = false;
        bin.EnableSearching = false;
        db.RootGroup.AddGroup(bin, true, false);
        Stamp(bin, a);
        db.RecycleBinUuid = bin.Uuid;
        db.RecycleBinChanged = a;
        Move(Find(db, "bin-A"), bin, a);

        var hi = Find(db, "hist"); Change(hi, a, () => S(hi, "Data", "A"));
        var hm = Find(db, "hist-many"); Change(hm, a, () => S(hm, "Data", "A"));
        var hb = Find(db, "hist-big"); Change(hb, a, () => hb.Binaries.Set("big.bin", new ProtectedBinary(false, Bytes("bigA", 400 * 1024))));

        var added = E(kpg, "added-A", a); S(added, "Data", "A");
        var newA = G(kpg, "NewA", a);
        var inNew = E(newA, "in-newA", a); S(inNew, "Data", "A");

        var gnb = FindG(db, "GNotesBoth"); gnb.Notes = "nA"; gnb.LastModificationTime = a;
        var gna = FindG(db, "GNotesA"); gna.Notes = "nA"; gna.LastModificationTime = a;
        var gta = FindG(db, "GTagsA"); gta.Tags.Add("gtA"); gta.LastModificationTime = a;
        var gdel = FindG(db, "GDelA"); gdel.ParentGroup.Groups.Remove(gdel); db.DeletedObjects.Add(new PwDeletedObject(gdel.Uuid, a));

        db.Save(null);
        db.Close();
        Console.WriteLine("wrote " + path);
    }

    static void SideB(string basePath, string path)
    {
        var db = CopyOpen(basePath, path);
        var b = At(2);
        var kpg = FindG(db, "KPG");

        var e = Find(db, "both-diff"); Change(e, b, () => S(e, "FieldB", "b"));
        var f = Find(db, "both-tie"); Change(f, At(1), () => S(f, "FieldB", "b"));
        var g = Find(db, "both-samefield"); Change(g, b, () => S(g, "Shared", "B"));
        var r = Find(db, "rewound-B"); Change(r, At(-1), () => S(r, "Data", "B"));
        var ab = Find(db, "att-both"); Change(ab, b, () => ab.Binaries.Set("b.bin", new ProtectedBinary(false, Bytes("b", 1000))));
        var tb = Find(db, "tag-both"); Change(tb, b, () => tb.Tags.Add("tagB"));
        var cd = Find(db, "cd-both"); Change(cd, b, () => cd.CustomData.Set("cdB", "b"));
        var me = Find(db, "move-A-edit-B"); Change(me, b, () => S(me, "Data", "B"));
        var da = Find(db, "del-A-chB-after"); Change(da, b, () => S(da, "Data", "B"));
        var dbf = Find(db, "del-A-chB-before"); Change(dbf, At(0.5), () => S(dbf, "Data", "B"));
        var hi = Find(db, "hist"); Change(hi, b, () => S(hi, "Data", "B"));
        var hm = Find(db, "hist-many"); Change(hm, b, () => S(hm, "Data", "B"));

        var added = E(kpg, "added-B", b); S(added, "Data", "B");
        var gnb = FindG(db, "GNotesBoth"); gnb.Notes = "nB"; gnb.LastModificationTime = b;

        db.Save(null);
        db.Close();
        Console.WriteLine("wrote " + path);
    }

    static string FieldName(DateTime t) { return "kpg-" + t.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture); }

    // The second set: group flags and custom data, an exclusion from reports, and a meta entry
    // given a field named after a time on each side, the way the operator's option would.
    static void Make2(string dir, string kdf)
    {
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        db.KdfParameters = Kdf(kdf);
        db.HistoryMaxItems = 10;
        db.HistoryMaxSize = 6 * 1024 * 1024;
        var root = db.RootGroup;
        root.Name = "Root";
        Stamp(root, T0);
        var kpg = G(root, "KPG", T0);
        G(kpg, "GFlags", T0);
        G(kpg, "GCustom", T0);
        foreach (var name in new[] { "meta", "meta2", "excl" }) S(E(kpg, name, T0), "Data", "0");
        db.Save(null);
        db.Close();

        var a = CopyOpen(basePath, Path.Combine(dir, "A.kdbx"));
        var gf = FindG(a, "GFlags"); gf.EnableSearching = false; gf.EnableAutoType = false; gf.LastModificationTime = At(1);
        var gc = FindG(a, "GCustom"); gc.CustomData.Set("KPG", "rule-A"); gc.LastModificationTime = At(1);
        var m = Find(a, "meta"); Change(m, At(1), () => S(m, FieldName(At(1)), "jsonA"));
        var m2 = Find(a, "meta2");
        Change(m2, At(1), () => S(m2, FieldName(At(1)), "jsonA1"));
        Change(m2, At(3), () => S(m2, FieldName(At(3)), "jsonA2"));
        var ex = Find(a, "excl"); Change(ex, At(1), () => ex.QualityCheck = false);
        a.Save(null);
        a.Close();

        var b = CopyOpen(basePath, Path.Combine(dir, "B.kdbx"));
        var mb = Find(b, "meta");
        for (int i = 2; i <= 12; i++)
        {
            int k = i;
            Change(mb, At(k), () => S(mb, FieldName(At(k)), "jsonB" + k));
            mb.MaintainBackups(b);
        }
        var mb2 = Find(b, "meta2"); Change(mb2, At(2), () => S(mb2, FieldName(At(2)), "jsonB1"));
        b.Save(null);
        b.Close();
        Console.WriteLine("wrote " + dir);
    }

    // ---- the dump ----

    static readonly MethodInfo TimeOf = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static string Tm(DateTime t)
    {
        var u = t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;
        var d = u - T0;
        if (Math.Abs(d.TotalHours) <= 24)
            return (d < TimeSpan.Zero ? "-" : "+") + d.Duration().ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return u.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    static string Short(PwUuid u) { return u == null || u.Equals(PwUuid.Zero) ? "-" : u.ToHexString().Substring(0, 8).ToLowerInvariant(); }

    static string Cut(string s) { s = s.Replace("\r", "\\r").Replace("\n", "\\n"); return s.Length > 48 ? s.Substring(0, 48) + "..." : s; }

    static string Cd(StringDictionaryEx d)
    {
        var parts = new List<string>();
        foreach (var kv in d)
        {
            object t = TimeOf == null ? null : TimeOf.Invoke(d, new object[] { kv.Key });
            var dt = t as DateTime?;
            parts.Add(kv.Key + "=" + Cut(kv.Value) + "@" + (dt.HasValue ? Tm(dt.Value) : "none"));
        }
        parts.Sort(StringComparer.Ordinal);
        return "{" + string.Join("; ", parts) + "}";
    }

    static string Tags(List<string> tags) { return "[" + string.Join("|", tags) + "]"; }

    static string Sha(ProtectedBinary b)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(b.ReadData())).Replace("-", "").Substring(0, 8).ToLowerInvariant();
    }

    static string Summary(PwEntry e)
    {
        var parts = new List<string>();
        foreach (var kv in e.Strings)
        {
            if (kv.Key == PwDefs.TitleField) continue;
            parts.Add(kv.Key + (kv.Value.IsProtected ? "[P]" : "") + "=" + Cut(kv.Value.ReadString()));
        }
        foreach (var kv in e.Binaries) parts.Add("bin:" + kv.Key + (kv.Value.IsProtected ? "[P]" : "") + "=" + Sha(kv.Value));
        if (e.Tags.Count > 0) parts.Add("tags=" + Tags(e.Tags));
        if (e.CustomData.Count > 0) parts.Add("cd=" + Cd(e.CustomData));
        parts.Sort(StringComparer.Ordinal);
        return string.Join(" ", parts);
    }

    static string FormatVersion(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    static string PathOf(PwGroup g) { return g.ParentGroup == null ? "" : (PathOf(g.ParentGroup) + "/" + g.Name).TrimStart('/'); }

    static void Dump(string file)
    {
        var db = Open(file);
        Console.WriteLine("# " + Path.GetFileName(file) + " kdbx " + FormatVersion(file));
        string bin = "none";
        if (!db.RecycleBinUuid.Equals(PwUuid.Zero))
        {
            var bg = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
            bin = bg == null ? ("missing " + Short(db.RecycleBinUuid)) : ("'" + PathOf(bg) + "' " + Short(bg.Uuid));
        }
        Console.WriteLine("meta recycleBin=" + (db.RecycleBinEnabled ? "on" : "off") + " bin=" + bin
            + " historyMaxItems=" + db.HistoryMaxItems + " historyMaxSize=" + db.HistoryMaxSize
            + " dbCustomData=[" + string.Join(",", db.CustomData.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal)) + "]");
        var dels = db.DeletedObjects.Select(d => "deleted " + Short(d.Uuid) + " @" + Tm(d.DeletionTime)).OrderBy(s => s, StringComparer.Ordinal);
        foreach (var d in dels) Console.WriteLine(d);
        DumpGroup(db.RootGroup);
        db.Close();
    }

    static void DumpGroup(PwGroup g)
    {
        Console.WriteLine("G '" + PathOf(g) + "' u=" + Short(g.Uuid) + " mod=" + Tm(g.LastModificationTime) + " loc=" + Tm(g.LocationChanged)
            + " prev=" + Short(g.PreviousParentGroup) + " notes=\"" + Cut(g.Notes) + "\" tags=" + Tags(g.Tags) + " cd=" + Cd(g.CustomData) + " search=" + (g.EnableSearching.HasValue ? g.EnableSearching.Value.ToString() : "inherit") + " autotype=" + (g.EnableAutoType.HasValue ? g.EnableAutoType.Value.ToString() : "inherit"));
        foreach (var e in g.Entries.OrderBy(x => x.Strings.ReadSafe(PwDefs.TitleField), StringComparer.Ordinal))
        {
            Console.WriteLine("  E '" + e.Strings.ReadSafe(PwDefs.TitleField) + "' u=" + Short(e.Uuid) + " mod=" + Tm(e.LastModificationTime)
                + " loc=" + Tm(e.LocationChanged) + " prev=" + Short(e.PreviousParentGroup) + (e.QualityCheck ? "" : " qc=off") + " :: " + Summary(e));
            if (e.History.UCount > 0)
            {
                var items = new List<string>();
                foreach (var h in e.History) items.Add(Tm(h.LastModificationTime) + " {" + Summary(h) + "}");
                Console.WriteLine("    H " + e.History.UCount + ": " + string.Join(" ; ", items));
            }
        }
        foreach (var c in g.Groups.OrderBy(x => x.Name, StringComparer.Ordinal)) DumpGroup(c);
    }
}
