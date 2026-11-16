// Probe for option-outside-file: what KeePass 2.62's KeePassLib does to the one thing the design
// leaves in the file, a key of the database's custom data that is written once and never changed.
// Console only. Invented content. C# 5, compiled by the framework's csc.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Interfaces;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

public static class Kp
{
    public const string Password = "probe-only";
    public const string StoreKey = "KPG.Store.3f9a61c07d2e4b18a5c2e90f4471bd06";
    public const string StoreValue = "{\"v\":\"1\",\"key\":\"q83vEjRWeJASNFZ4kBI0VniQEjRWeJASNFZ4kBI0Vng=\"}";
    public const string AuthorityKey = "KPG.Authority.5d0c1f6e9a2b47c38e1f0a9b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d";

    static CompositeKey Key()
    {
        var k = new CompositeKey();
        k.AddUserKey(new KcpPassword(Password));
        return k;
    }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static void SetTime(StringDictionaryEx d, string key, string value, DateTime? t)
    {
        var m = typeof(StringDictionaryEx).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new Type[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
        if (m == null) throw new InvalidOperationException("internal Set not found");
        m.Invoke(d, new object[] { key, value, t });
    }

    static DateTime? GetTime(StringDictionaryEx d, string key)
    {
        var m = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime", BindingFlags.Instance | BindingFlags.NonPublic);
        if (m == null) throw new InvalidOperationException("internal GetLastModificationTime not found");
        return (DateTime?)m.Invoke(d, new object[] { key });
    }

    static PwEntry AddEntry(PwDatabase db, string title)
    {
        var e = new PwEntry(true, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-" + title));
        db.RootGroup.AddEntry(e, true);
        return e;
    }

    public static string Version(string path)
    {
        var b = new byte[12];
        using (var f = File.OpenRead(path)) { f.Read(b, 0, 12); }
        int minor = b[8] | (b[9] << 8);
        int major = b[10] | (b[11] << 8);
        return major + "." + minor;
    }

    public static string Describe(string path)
    {
        var db = Open(path);
        var sb = new StringBuilder();
        sb.Append("kdbx " + Version(path) + "; custom data:");
        var names = new List<string>();
        foreach (var p in db.CustomData) names.Add(p.Key);
        names.Sort(StringComparer.Ordinal);
        foreach (var n in names)
        {
            var t = GetTime(db.CustomData, n);
            string v = db.CustomData.Get(n);
            string what = n == StoreKey ? (v == StoreValue ? "store(same)" : "store(CHANGED)")
                : n == AuthorityKey ? "authority(" + v.Length + ")" : n + "=" + (v.Length > 24 ? v.Substring(0, 24) + ".." : v);
            sb.Append(" " + what + (t.HasValue ? "@t" : "@none"));
        }
        if (names.Count == 0) sb.Append(" (none)");
        sb.Append("; entries " + db.RootGroup.GetEntriesCount(true));
        db.Close();
        return sb.ToString();
    }

    // make <dir> <iterations> <memoryMiB> <lanes>
    public static void Make(string dir, ulong it, ulong mem, uint lanes)
    {
        Directory.CreateDirectory(dir);
        string basePath = Path.Combine(dir, "base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        var kdf = new Argon2Kdf();
        var p = kdf.GetDefaultParameters();
        p.SetUInt64(Argon2Kdf.ParamIterations, it);
        p.SetUInt64(Argon2Kdf.ParamMemory, mem * 1024 * 1024);
        p.SetUInt32(Argon2Kdf.ParamParallelism, lanes);
        db.KdfParameters = p;
        AddEntry(db, "E1");
        AddEntry(db, "E2");
        db.Save(null);
        db.Close();

        // old: a copy from before the gate was set up. Holds nothing of KPG.
        File.Copy(basePath, Path.Combine(dir, "old.kdbx"), true);

        // anchored: the store key and an authority, written the way the plugin writes (a time on each).
        string a = Path.Combine(dir, "anchored.kdbx");
        File.Copy(basePath, a, true);
        db = Open(a);
        db.CustomData.Set(StoreKey, StoreValue);
        db.CustomData.Set(AuthorityKey, new string('A', 2903));
        db.Save(null);
        db.Close();

        // anchored2: the same file after somebody added an entry elsewhere. Same two keys.
        string a2 = Path.Combine(dir, "anchored2.kdbx");
        File.Copy(a, a2, true);
        db = Open(a2);
        AddEntry(db, "E3");
        db.Save(null);
        db.Close();

        // anchored-notimes: anchored as a program that drops the per-key times leaves it.
        string an = Path.Combine(dir, "anchored-notimes.kdbx");
        File.Copy(a, an, true);
        db = Open(an);
        SetTime(db.CustomData, StoreKey, StoreValue, null);
        SetTime(db.CustomData, AuthorityKey, new string('A', 2903), null);
        AddEntry(db, "E4");
        db.Save(null);
        db.Close();

        foreach (var n in new[] { "old", "anchored", "anchored2", "anchored-notimes" })
            Console.WriteLine(n + ": " + Describe(Path.Combine(dir, n + ".kdbx")));
    }

    // kpmerge <dir>: every method, both directions, three pairs.
    public static void KpMerge(string dir)
    {
        var methods = new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids };
        var pairs = new[] {
            new[] { "anchored", "old" }, new[] { "old", "anchored" },
            new[] { "anchored", "anchored2" }, new[] { "anchored2", "anchored" },
            new[] { "anchored-notimes", "old" }, new[] { "old", "anchored-notimes" },
            new[] { "anchored-notimes", "anchored2" }, new[] { "anchored2", "anchored-notimes" } };
        string outDir = Path.Combine(dir, "kpmerge");
        Directory.CreateDirectory(outDir);
        int kept = 0, cells = 0;
        foreach (var pair in pairs)
        {
            foreach (var m in methods)
            {
                string target = Path.Combine(outDir, pair[0] + "+" + pair[1] + "-" + m + ".kdbx");
                File.Copy(Path.Combine(dir, pair[0] + ".kdbx"), target, true);
                var t = Open(target);
                var s = Open(Path.Combine(dir, pair[1] + ".kdbx"));
                t.MergeIn(s, m, null);
                t.Save(null);
                t.Close();
                s.Close();
                string d = Describe(target);
                cells++;
                if (d.Contains("store(same)") && d.Contains("authority(2903)")) kept++;
                Console.WriteLine("into " + pair[0] + " from " + pair[1] + ", " + m + ": " + d);
            }
        }
        Console.WriteLine("cells " + cells + ", both keys present and unchanged in " + kept);
    }
}

public static class Kp2
{
    // other <dir>: a copy from before the gate was set up that holds some other program's key.
    public static void Other(string dir)
    {
        string o = Path.Combine(dir, "old-other.kdbx");
        File.Copy(Path.Combine(dir, "old.kdbx"), o, true);
        var db = new PwDatabase();
        var k = new CompositeKey();
        k.AddUserKey(new KcpPassword(Kp.Password));
        db.Open(IOConnectionInfo.FromPath(o), k, null);
        db.CustomData.Set("Other.Plugin", "1");
        db.Save(null);
        db.Close();
        Console.WriteLine("old-other: " + Kp.Describe(o));
    }
}
