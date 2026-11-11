// The writer and reader the Android measurement ran, kept as it ran. `Paired.cs` beside the probe
// writes the same keys, values, times and rules; this file is here because the databases the
// Android programs were given came out of it, under the password below, and because the recorded
// Android output is in its notation rather than the probe's. `kp.ps1` compiles and runs it.
//
// Builds the throwaway databases C and P with KeePassLib 2.61.1 and reads any .kdbx back,
// classifying every KPG.* key, its value and its per-key modification time against what C and P
// were written with.
// C# 5 only: Windows PowerShell 5.1's Add-Type compiles with the in-box CodeDOM provider.
// Commands (args[0]):
//   make <dir> <set> <kdf> [<kdf args>]    writes <dir>\C-<set>.kdbx, P-plain-<set>.kdbx and
//                                           P-moved-<set>.kdbx
//        kdf: argon2d <iterations> <memoryKiB> <parallelism> | aes <rounds>
//   dump <file>...                          full read-back of each file
//   sum <file>...                           one summary line per file
//   sump <file>...                          as sum, with each file's full path
//   list <file of paths>                    as sump, the paths read from a file, one per line
//   settings <src> <dst> <UTC time>         a copy of src whose SettingsChanged is that moment
//   time <file> <n>                         opens a file n times and prints each open's duration
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public static class Kpg
{
    static bool FullPath;
    public const string Password = "dummy-password-for-a-throwaway-file";

    static string Hex(string two) { return string.Concat(Enumerable.Repeat(two, 32)); }
    static readonly string A0 = Hex("a0"), A1 = Hex("a1"), A2 = Hex("a2");
    static readonly string C0 = Hex("c0"), C1 = Hex("c1"), C2 = Hex("c2");

    static DateTime U(int month, int hour) { return new DateTime(2026, month, 1, hour, 0, 0, DateTimeKind.Utc); }

    // label, key, value in C, time in C, value in P, time in P (null value: absent there)
    sealed class K
    {
        public string Label, Key, CV, PV; public DateTime CT, PT;
        public K(string l, string k, string cv, DateTime ct, string pv, DateTime pt) { Label = l; Key = k; CV = cv; CT = ct; PV = pv; PT = pt; }
    }

    const string RecC1 = "{\"profiles\":[\"reader\"],\"name\":\"agent-one\",\"notAfter\":\"2027-01-01T00:00:00Z\"}";
    const string RecC2 = "{\"profiles\":[\"reader\",\"writer\"],\"name\":\"agent-two\",\"notAfter\":\"2027-02-01T00:00:00Z\"}";
    const string ProfC = "[\"reader\",\"writer\"]";
    const string ProfP = "[\"reader\"]";
    const string RuleGC = "{\"reader\":[\"read\",\"list\"],\"writer\":[\"create\",\"read\",\"update\",\"list\"]}";
    const string RuleGP = "{\"reader\":[\"list\"]}";
    const string RuleEC = "{\"reader\":[\"read\",\"list\"]}";
    const string RuleEP = "{\"reader\":[]}";

    static readonly DateTime Never = DateTime.MinValue;

    static readonly K[] Keys = {
        new K("A0",   "KPG.Authority." + A0,        "authority-document-A0", U(1, 1), "authority-document-A0", U(1, 1)),
        new K("A0r",  "KPG.RetiredAuthority." + A0, "retired",               U(1, 2), "retired",               U(1, 2)),
        new K("A1",   "KPG.Authority." + A1,        "authority-document-A1", U(1, 3), "authority-document-A1", U(1, 3)),
        new K("C1",   "KPG.Client." + C1,           RecC1,                   U(1, 4), RecC1,                   U(1, 4)),
        new K("C2",   "KPG.Client." + C2,           RecC2,                   U(1, 5), "",                      U(2, 1)),
        new K("C0",   "KPG.Client." + C0,           "",                      U(1, 6), "",                      U(1, 6)),
        new K("C0v",  "KPG.Revoked." + C0,          "2027-01-01T00:00:00Z " + A1, U(1, 7), "2027-01-01T00:00:00Z " + A1, U(1, 7)),
        new K("Prof", "KPG.Profiles",               ProfC,                   U(1, 8), ProfP,                   U(2, 5)),
        new K("C2v",  "KPG.Revoked." + C2,          null,                    Never,   "2027-02-01T00:00:00Z " + A1, U(2, 2)),
        new K("A1r",  "KPG.RetiredAuthority." + A1, null,                    Never,   "retired",               U(2, 3)),
        new K("A2",   "KPG.Authority." + A2,        null,                    Never,   "authority-document-A2", U(2, 4)),
    };

    static CompositeKey Key()
    {
        var k = new CompositeKey();
        k.AddUserKey(new KcpPassword(Password));
        return k;
    }

    static readonly MethodInfo SetWithTime = typeof(StringDictionaryEx).GetMethod("Set",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
    static readonly MethodInfo TimeOf = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static void SetT(StringDictionaryEx d, string key, string value, DateTime t) { SetWithTime.Invoke(d, new object[] { key, value, (DateTime?)t }); }
    static DateTime? TimeAt(StringDictionaryEx d, string key) { return (DateTime?)TimeOf.Invoke(d, new object[] { key }); }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwEntry AddEntry(PwGroup g, string title, DateTime t)
    {
        var e = new PwEntry(true, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not-a-real-secret"));
        g.AddEntry(e, true);
        Stamp(e, t);
        return e;
    }

    static KdfParameters Kdf(string[] a, int i)
    {
        if (a[i] == "aes")
        {
            var p = new AesKdf().GetDefaultParameters();
            p.SetUInt64(AesKdf.ParamRounds, ulong.Parse(a[i + 1], CultureInfo.InvariantCulture));
            return p;
        }
        if (a[i] == "argon2d")
        {
            var p = new Argon2Kdf().GetDefaultParameters();
            p.SetUInt64(Argon2Kdf.ParamIterations, ulong.Parse(a[i + 1], CultureInfo.InvariantCulture));
            p.SetUInt64(Argon2Kdf.ParamMemory, ulong.Parse(a[i + 2], CultureInfo.InvariantCulture) * 1024UL);
            p.SetUInt32(Argon2Kdf.ParamParallelism, uint.Parse(a[i + 3], CultureInfo.InvariantCulture));
            return p;
        }
        throw new ArgumentException("kdf: argon2d <it> <memKiB> <par> | aes <rounds>");
    }

    static void Make(string dir, string set, KdfParameters kdf)
    {
        Directory.CreateDirectory(dir);
        var pd = new PwDatabase();
        var c = Path.Combine(dir, "C-" + set + ".kdbx");
        pd.New(IOConnectionInfo.FromPath(c), Key());
        pd.KdfParameters = kdf;
        var root = pd.RootGroup; root.Name = "Root"; Stamp(root, U(1, 0));
        var g = new PwGroup(true, true, "G", PwIcon.Folder); root.AddGroup(g, true);
        var h = new PwGroup(true, true, "H", PwIcon.Folder); root.AddGroup(h, true);
        Stamp(h, U(1, 0));
        var e1 = AddEntry(g, "E1", U(1, 0));
        var e2 = AddEntry(g, "E2", U(1, 0));
        // A set whose name ends in "b" is bare: no rule on any group or entry, only the database's
        // own keys.
        bool bare = set.EndsWith("b", StringComparison.Ordinal);
        if (!bare) g.CustomData.Set("KPG", RuleGC);
        Stamp(g, U(1, 0)); g.LastModificationTime = U(1, 9);
        if (!bare) e1.CustomData.Set("KPG", RuleEC);
        e1.LastModificationTime = U(1, 10);
        pd.RecycleBinEnabled = true;
        foreach (var k in Keys) if (k.CV != null) SetT(pd.CustomData, k.Key, k.CV, k.CT);
        // A set whose name ends in "m" has E2 moved before C is taken, so C and P both hold a
        // PreviousParentGroup and KeePassXC 2.7+ writes either as 4.1, which keeps the per-key
        // times visible.
        bool movedFirst = set.EndsWith("m", StringComparison.Ordinal);
        if (movedFirst) { g.Entries.Remove(e2); h.AddEntry(e2, true, true); e2.PreviousParentGroup = g.Uuid; e2.LocationChanged = U(1, 11); }
        pd.Save(null);
        Console.WriteLine("wrote " + c);

        // P: newer than C in every way the plugin cares about.
        foreach (var k in Keys) if (k.PV != null && (k.CV != k.PV || k.CT != k.PT)) SetT(pd.CustomData, k.Key, k.PV, k.PT);
        if (!bare) g.CustomData.Set("KPG", RuleGP);
        g.LastModificationTime = U(2, 6);
        if (!bare) e1.CustomData.Set("KPG", RuleEP);
        e1.LastModificationTime = U(2, 7);
        var p = Path.Combine(dir, "P-plain-" + set + ".kdbx");
        pd.SaveAs(IOConnectionInfo.FromPath(p), false, null);
        Console.WriteLine("wrote " + p);

        // P-moved: P plus an entry moved by KeePassLib, which records where it came from.
        if (movedFirst) { pd.Close(); return; }
        g.Entries.Remove(e2);
        h.AddEntry(e2, true, true);
        e2.PreviousParentGroup = g.Uuid;
        e2.LocationChanged = U(2, 8);
        var m = Path.Combine(dir, "P-moved-" + set + ".kdbx");
        pd.SaveAs(IOConnectionInfo.FromPath(m), false, null);
        Console.WriteLine("wrote " + m);
        pd.Close();
    }

    static string Version(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    static string Iso(DateTime? t) { return t.HasValue ? t.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "none"; }

    static string KdfText(KdfParameters p)
    {
        var u = p.KdfUuid;
        if (u.Equals(new AesKdf().Uuid)) return "AES-KDF rounds=" + p.GetUInt64(AesKdf.ParamRounds, 0);
        string name = u.Equals(new Argon2Kdf().Uuid) ? "Argon2d" : ("kdf " + u.ToHexString());
        return name + " it=" + p.GetUInt64(Argon2Kdf.ParamIterations, 0) + " mem=" + (p.GetUInt64(Argon2Kdf.ParamMemory, 0) / 1024) + "KiB par=" + p.GetUInt32(Argon2Kdf.ParamParallelism, 0);
    }

    static PwDatabase Open(string file)
    {
        var pd = new PwDatabase();
        pd.Open(IOConnectionInfo.FromPath(file), Key(), null);
        return pd;
    }

    static string Cls(string have, string c, string p)
    {
        if (have == null) return "-";
        bool isC = c != null && have == c, isP = p != null && have == p;
        if (isC && isP) return "=";
        if (isP) return "P";
        if (isC) return "C";
        return "?";
    }

    static string ClsT(DateTime? have, DateTime c, DateTime p, bool cHas, bool pHas)
    {
        if (!have.HasValue) return "none";
        var t = have.Value.ToUniversalTime();
        bool isC = cHas && t == c, isP = pHas && t == p;
        if (isC && isP) return "=";
        if (isP) return "P";
        if (isC) return "C";
        return Iso(t);
    }

    static PwGroup FindGroup(PwDatabase pd, string name) { return pd.RootGroup.GetGroups(true).FirstOrDefault(x => x.Name == name); }
    static PwEntry FindEntry(PwDatabase pd, string title) { return pd.RootGroup.GetEntries(true).FirstOrDefault(x => x.Strings.ReadSafe(PwDefs.TitleField) == title); }

    static string Short(string v) { if (v == null) return "null"; return v.Length > 40 ? v.Substring(0, 40) + "...(" + v.Length + ")" : v; }

    static void Read(string file, bool full)
    {
        var sw = Stopwatch.StartNew();
        var pd = Open(file);
        sw.Stop();
        try
        {
            var cd = pd.CustomData;
            var parts = new List<string>();
            foreach (var k in Keys)
            {
                string have = cd.Exists(k.Key) ? cd.Get(k.Key) : null;
                if (have == null) { parts.Add(k.Label + ":-"); continue; }
                parts.Add(k.Label + ":" + Cls(have, k.CV, k.PV) + "/" + ClsT(TimeAt(cd, k.Key), k.CT, k.PT, k.CV != null, k.PV != null));
            }
            var known = new HashSet<string>(Keys.Select(x => x.Key));
            var other = new List<string>();
            foreach (var kv in cd)
            {
                if (known.Contains(kv.Key)) continue;
                string v = kv.Key == "KPXC_RANDOM_SLUG" ? ("len" + kv.Value.Length) : kv.Value;
                other.Add(kv.Key + "=" + v + "@" + Iso(TimeAt(cd, kv.Key)));
            }
            var g = FindGroup(pd, "G");
            var e1 = FindEntry(pd, "E1");
            string gr = g == null ? "noG" : Cls(g.CustomData.Exists("KPG") ? g.CustomData.Get("KPG") : null, RuleGC, RuleGP);
            string er = e1 == null ? "noE1" : Cls(e1.CustomData.Exists("KPG") ? e1.CustomData.Get("KPG") : null, RuleEC, RuleEP);
            var titles = pd.RootGroup.GetEntries(true).Select(x => x.Strings.ReadSafe(PwDefs.TitleField) + "@" + x.ParentGroup.Name
                + (x.PreviousParentGroup.Equals(PwUuid.Zero) ? "" : "(prev)")).ToList();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | kdbx {1} | {2} | G:{3} E1:{4} | others: {5} | entries: {6} | open {7:0.0}s",
                (FullPath ? file : Path.GetFileName(file)), Version(file), string.Join(" ", parts), gr, er,
                other.Count == 0 ? "(none)" : string.Join("; ", other), string.Join(",", titles), sw.Elapsed.TotalSeconds));
            if (!full) return;
            Console.WriteLine("  path: " + file);
            using (var s = File.OpenRead(file)) using (var sha = SHA256.Create())
                Console.WriteLine("  size: " + new FileInfo(file).Length + " sha256: " + BitConverter.ToString(sha.ComputeHash(s)).Replace("-", ""));
            Console.WriteLine("  kdbx " + Version(file) + ", " + KdfText(pd.KdfParameters) + ", SettingsChanged " + Iso(pd.SettingsChanged));
            Console.WriteLine("  database CustomData (" + cd.Count + "):");
            foreach (var kv in cd) Console.WriteLine("    " + kv.Key + " = " + Short(kv.Value) + "  @" + Iso(TimeAt(cd, kv.Key)));
            Console.WriteLine("  PublicCustomData count: " + pd.PublicCustomData.Count);
            foreach (var x in pd.RootGroup.GetGroups(true).Concat(new[] { pd.RootGroup }))
            {
                var parts2 = new List<string>();
                foreach (var kv in x.CustomData) parts2.Add(kv.Key + "=" + kv.Value);
                Console.WriteLine("  group " + x.Name + " mtime " + Iso(x.LastModificationTime) + " prev " + (x.PreviousParentGroup.Equals(PwUuid.Zero) ? "-" : x.PreviousParentGroup.ToHexString().Substring(0, 8))
                    + " customdata {" + string.Join("; ", parts2) + "}");
            }
            foreach (var x in pd.RootGroup.GetEntries(true))
            {
                var parts2 = new List<string>();
                foreach (var kv in x.CustomData) parts2.Add(kv.Key + "=" + kv.Value);
                Console.WriteLine("  entry " + x.Strings.ReadSafe(PwDefs.TitleField) + " in " + x.ParentGroup.Name + " mtime " + Iso(x.LastModificationTime)
                    + " prev " + (x.PreviousParentGroup.Equals(PwUuid.Zero) ? "-" : x.PreviousParentGroup.ToHexString().Substring(0, 8))
                    + " history " + x.History.UCount + " customdata {" + string.Join("; ", parts2) + "}");
            }
            Console.WriteLine("  deleted objects: " + pd.DeletedObjects.UCount);
        }
        finally { pd.Close(); }
    }

    public static void Run(string[] a)
    {
        var cmd = a[0];
        if (cmd == "make") { Make(a[1], a[2], Kdf(a, 3)); return; }
        if (cmd == "list")
        {
            // list <file of paths, one per line>: as sump, for more paths than a command line
            // holds.
            var paths = File.ReadAllLines(a[1]).Where(x => x.Trim().Length > 0).ToList();
            paths.Insert(0, "sump");
            a = paths.ToArray(); cmd = "sump";
        }
        if (cmd == "dump" || cmd == "sum" || cmd == "sump")
        {
            FullPath = cmd == "sump";
            for (int i = 1; i < a.Length; i++)
            {
                try { Read(a[i], cmd == "dump"); }
                catch (Exception ex) { Console.WriteLine(Path.GetFileName(a[i]) + " | READ FAILED: " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ")); }
            }
            return;
        }
        if (cmd == "settings")
        {
            // settings <src> <dst> <yyyy-MM-ddTHH:mm:ssZ>: a copy of src whose database
            // SettingsChanged is the given moment and nothing else differs (added for the Android
            // measurement).
            var t = DateTime.Parse(a[3], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            File.Copy(a[1], a[2], true);
            File.SetAttributes(a[2], FileAttributes.Normal);
            var pd = Open(a[2]);
            Console.WriteLine("SettingsChanged was " + Iso(pd.SettingsChanged) + ", now " + Iso(t));
            pd.SettingsChanged = t;
            pd.Save(null);
            pd.Close();
            return;
        }
        if (cmd == "time")
        {
            int n = int.Parse(a[2], CultureInfo.InvariantCulture);
            for (int i = 0; i < n; i++) { var sw = Stopwatch.StartNew(); var pd = Open(a[1]); sw.Stop(); pd.Close(); Console.WriteLine("open " + sw.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s"); }
            return;
        }
        throw new ArgumentException("unknown command " + cmd);
    }
}
