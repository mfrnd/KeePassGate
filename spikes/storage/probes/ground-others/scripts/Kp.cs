using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

// Invented test content only. Every database is made here, under the scratch directory.
public static class Kp
{
    static readonly MethodInfo SetT = typeof(StringDictionaryEx).GetMethod("Set",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new Type[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
    static readonly MethodInfo GetT = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static CompositeKey Key()
    {
        var k = new CompositeKey();
        k.AddUserKey(new KcpPassword("test"));
        return k;
    }

    static DateTime T(int seconds)
    {
        return new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
    }

    static string Iso(DateTime t) { return t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture); }

    public static PwDatabase Open(string path)
    {
        var pd = new PwDatabase();
        pd.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return pd;
    }

    static PwGroup FindGroup(PwDatabase pd, string name)
    {
        foreach (PwGroup g in pd.RootGroup.GetGroups(true)) if (g.Name == name) return g;
        return null;
    }

    static PwEntry FindEntry(PwGroup g, string title)
    {
        foreach (PwEntry e in g.Entries) if (e.Strings.ReadSafe(PwDefs.TitleField) == title) return e;
        return null;
    }

    // base, A (E gains Note-A at +10 s, EA added), B (E gains Note-B at +20 s, EB added),
    // Btie (as B but E at +10 s, the same second as A), Bcd (E's custom data KPG changed at +20 s, no string change)
    public static void Make(string dir) { Make(dir, true); }

    public static void Make(string dir, bool stamped)
    {
        string basePath = Path.Combine(dir, "base.kdbx");
        if (File.Exists(basePath)) File.Delete(basePath);
        var pd = new PwDatabase();
        pd.New(IOConnectionInfo.FromPath(basePath), Key());
        var p = new AesKdf().GetDefaultParameters();
        p.SetUInt64(AesKdf.ParamRounds, 1);
        pd.KdfParameters = p;
        pd.Name = "invented";
        SetT.Invoke(pd.CustomData, new object[] { "KPG.Key", "v-base", stamped ? (DateTime?)T(0) : null });
        pd.CustomData.Set("KPG.Empty", "");
        SetT.Invoke(pd.CustomData, new object[] { "KPG.Empty", "", stamped ? (DateTime?)T(0) : null });

        var g = new PwGroup(true, true, "G", PwIcon.Folder);
        pd.RootGroup.AddGroup(g, true);
        g.CustomData.Set("KPG", "grule-base");
        g.LastModificationTime = T(0); g.CreationTime = T(0);

        var e = new PwEntry(true, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "E"));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "u"));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "p"));
        e.Strings.Set("Base", new ProtectedString(false, "b"));
        e.Strings.Set("Secret", new ProtectedString(true, "s"));
        e.Binaries.Set("a.txt", new ProtectedBinary(false, Encoding.UTF8.GetBytes("attachment")));
        e.Tags.Add("t1");
        e.CustomData.Set("KPG", "rule-base");
        g.AddEntry(e, true);
        e.CreationTime = T(-3600);
        e.LastModificationTime = T(-3600);
        e.CreateBackup(pd);           // one older version in history, at -3600
        e.Strings.Set("Base", new ProtectedString(false, "b"));
        e.LastModificationTime = T(0);
        pd.Save(null);
        pd.Close();

        Derive(dir, "A", "Note-A", 10, "EA", null);
        Derive(dir, "B", "Note-B", 20, "EB", null);
        Derive(dir, "Btie", "Note-B", 10, "EB", null);
        Derive(dir, "Bcd", null, 20, null, "rule-B");
        DeriveGroup(dir, "Bg", "grule-B", 20);
    }

    // G's custom data changed and G's time moved to +at, nothing else
    static void DeriveGroup(string dir, string name, string gcd, int at)
    {
        var pd = Open(Path.Combine(dir, "base.kdbx"));
        var g = FindGroup(pd, "G");
        g.CustomData.Set("KPG", gcd);
        g.LastModificationTime = T(at);
        string path = Path.Combine(dir, name + ".kdbx");
        if (File.Exists(path)) File.Delete(path);
        pd.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        pd.Close();
    }

    static void Derive(string dir, string name, string field, int at, string newEntry, string cd)
    {
        var pd = Open(Path.Combine(dir, "base.kdbx"));
        var g = FindGroup(pd, "G");
        var e = FindEntry(g, "E");
        e.CreateBackup(pd);
        if (field != null) e.Strings.Set(field, new ProtectedString(false, "from " + name));
        if (cd != null) e.CustomData.Set("KPG", cd);
        e.LastModificationTime = T(at);
        if (newEntry != null)
        {
            var n = new PwEntry(true, true);
            n.Strings.Set(PwDefs.TitleField, new ProtectedString(false, newEntry));
            g.AddEntry(n, true);
            n.CreationTime = T(at); n.LastModificationTime = T(at);
        }
        string path = Path.Combine(dir, name + ".kdbx");
        if (File.Exists(path)) File.Delete(path);
        pd.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        pd.Close();
    }

    public static void Merge(string target, string source, string output, string method)
    {
        var t = Open(target);
        var s = Open(source);
        t.MergeIn(s, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), method));
        if (File.Exists(output)) File.Delete(output);
        t.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        s.Close(); t.Close();
    }

    public static void Resave(string input, string output)
    {
        var pd = Open(input);
        if (File.Exists(output)) File.Delete(output);
        pd.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        pd.Close();
    }

    static string Version(string path)
    {
        byte[] b = new byte[12];
        using (var f = File.OpenRead(path)) f.Read(b, 0, 12);
        return BitConverter.ToUInt16(b, 10) + "." + BitConverter.ToUInt16(b, 8);
    }

    static string Cd(StringDictionaryEx d)
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> kv in d)
        {
            var t = (DateTime?)GetT.Invoke(d, new object[] { kv.Key });
            sb.Append(kv.Key + "=" + kv.Value + (t.HasValue ? "@" + Iso(t.Value) : "@none") + "; ");
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    static string Strings(PwEntry e)
    {
        var names = new List<string>();
        foreach (KeyValuePair<string, ProtectedString> kv in e.Strings)
            if (!PwDefs.IsStandardField(kv.Key) || kv.Key == PwDefs.PasswordField)
                names.Add(kv.Key + (kv.Value.IsProtected ? "[p]" : "") + "=" + kv.Value.ReadString());
        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names.ToArray());
    }

    public static string Dump(string path)
    {
        var sb = new StringBuilder();
        var pd = Open(path);
        sb.AppendLine(Path.GetFileName(path) + ": kdbx " + Version(path) + ", db customdata " + Cd(pd.CustomData) +
            " deleted objects " + pd.DeletedObjects.UCount);
        var g = FindGroup(pd, "G");
        var titles = new List<string>();
        foreach (PwEntry x in g.Entries) titles.Add(x.Strings.ReadSafe(PwDefs.TitleField));
        sb.AppendLine("  G @" + Iso(g.LastModificationTime) + " customdata " + Cd(g.CustomData) + " entries " + string.Join(",", titles.ToArray()));
        var e = FindEntry(g, "E");
        if (e != null)
        {
            sb.AppendLine("  E @" + Iso(e.LastModificationTime) + " strings " + Strings(e) + " customdata " + Cd(e.CustomData) +
                " tags " + string.Join(",", e.Tags.ToArray()) + " binaries " + e.Binaries.UCount);
            foreach (PwEntry h in e.History)
                sb.AppendLine("    history @" + Iso(h.LastModificationTime) + " strings " + Strings(h) + " customdata " + Cd(h.CustomData));
        }
        pd.Close();
        return sb.ToString();
    }
}
