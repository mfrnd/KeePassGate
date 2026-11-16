// The console probe of the desktop-windows family, against KeePass 2.62's KeePassLib. It makes the
// throwaway databases the KeePass and KeePassXC windows are given, and reads every file a window
// leaves back into a line-per-item dump and a one-line verdict. Invented content only, one invented
// password, no window. The helpers and the dump are those of the Android family's probe
// (`spikes\storage\android\probe\overwrite\Store.cs`), copied so that the two read files alike.
//
//   m1base <dir>                   OLD, NEW-fixed, NEW-same and NEW-ident for measurements 1, 2, 5
//   m1new <old> <out> <q3>         NEW with Q3's password changed at <q3> (ISO, UTC)
//   m3base <path>                  the user's entries with KPG.Ref, for measurement 3
//   m3other <path>                 a second, nearly empty database (drag and paste target)
//   m4base <path>                  the store made of entries, for measurement 4
//   dump <file> [<out>]            the sorted line-per-item dump of one file
//   verdict <file>                 one line: what a file holds of the two copies' items
//   sync <target> <source> <out>   open target, MergeIn(source, Synchronize), save as out
//   merge <target> <source> <out> <method>   the same with any PwMergeMethod
//
// Every line of a dump starts with what it is about and is then sorted: F the file, M the
// database's meta, D a deletion record, G a group, E an entry, "E <id> H<time>" a history item.
// A node made here carries a fixed identifier ('KPG' and an ASCII alias), printed as the alias;
// any other identifier prints as ~ and its first eight hex digits.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Desk
{
    public const string Password = "desk-probe-invented-password";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime T0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TD = T0.AddHours(8);
    static readonly DateTime Q2Time = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static DateTime At(int minutes) { return T0.AddMinutes(minutes); }
    static DateTime Day(int y, int m, int d) { return new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Utc); }

    // ---------------------------------------------------------------- identifiers

    static PwUuid Id(string alias)
    {
        var b = new byte[16];
        b[0] = 0x4b; b[1] = 0x50; b[2] = 0x47;
        var a = Encoding.ASCII.GetBytes(alias);
        if (a.Length > 13) throw new ArgumentException(alias);
        Array.Copy(a, 0, b, 3, a.Length);
        return new PwUuid(b);
    }

    static string Alias(PwUuid u)
    {
        if (u == null || u.Equals(PwUuid.Zero)) return "-";
        var b = u.UuidBytes;
        if (b[0] == 0x4b && b[1] == 0x50 && b[2] == 0x47)
        {
            int n = 3;
            while (n < 16 && b[n] != 0) n++;
            bool rest = true;
            for (int i = n; i < 16; i++) if (b[i] != 0) rest = false;
            if (rest && n > 3) return Encoding.ASCII.GetString(b, 3, n - 3);
        }
        return "~" + u.ToHexString().Substring(0, 8).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- making

    static CompositeKey Key()
    {
        var k = new CompositeKey();
        k.AddUserKey(new KcpPassword(Password));
        return k;
    }

    static KdfParameters Argon2d()
    {
        var p = new Argon2Kdf().GetDefaultParameters();
        p.SetUInt64(Argon2Kdf.ParamIterations, 2);
        p.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        p.SetUInt32(Argon2Kdf.ParamParallelism, 1);
        return p;
    }

    static readonly MethodInfo SetWithTime = typeof(StringDictionaryEx).GetMethod("Set",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
    static readonly MethodInfo TimeOf = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static void SetCd(StringDictionaryEx d, string key, string value, DateTime? t)
    {
        if (SetWithTime == null) throw new InvalidOperationException("StringDictionaryEx.Set(string, string, DateTime?) not found");
        SetWithTime.Invoke(d, new object[] { key, value, t });
    }

    static DateTime? CdTime(StringDictionaryEx d, string key)
    {
        if (TimeOf == null) return null;
        return (DateTime?)TimeOf.Invoke(d, new object[] { key });
    }

    static PwGroup NewGroup(PwGroup parent, string alias, string name, DateTime t)
    {
        var g = new PwGroup(false, false);
        g.Uuid = Id(alias);
        g.Name = name;
        g.IconId = PwIcon.Folder;
        parent.AddGroup(g, true, false);
        g.CreationTime = g.LastModificationTime = g.LastAccessTime = g.LocationChanged = t;
        g.ExpiryTime = T0;
        return g;
    }

    static PwEntry NewEntry(PwDatabase pd, PwGroup g, string alias, string title, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = Id(alias);
        var mp = pd.MemoryProtection;
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(mp.ProtectTitle, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(mp.ProtectUserName, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(mp.ProtectPassword, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(mp.ProtectUrl, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(mp.ProtectNotes, ""));
        g.AddEntry(e, true, false);
        e.CreationTime = e.LastModificationTime = e.LastAccessTime = e.LocationChanged = t;
        e.ExpiryTime = T0;
        return e;
    }

    static void Std(PwDatabase pd, PwEntry e, string user, string pw, string url, string notes)
    {
        var mp = pd.MemoryProtection;
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(mp.ProtectUserName, user));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(mp.ProtectPassword, pw));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(mp.ProtectUrl, url));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(mp.ProtectNotes, notes));
    }

    static void Str(PwEntry e, string name, bool prot, string value) { e.Strings.Set(name, new ProtectedString(prot, value)); }

    // A new version of an entry, as KeePass's entry dialog makes one: the current state goes into
    // history first, then the change, then the time.
    static void Version(PwEntry e, DateTime t, Action change)
    {
        e.CreateBackup(null);
        change();
        e.LastModificationTime = e.LastAccessTime = t;
    }

    static byte[] Bytes(int seed, int n)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    static string Pad(int seed, int n)
    {
        const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var r = new Random(seed);
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++) sb.Append(abc[r.Next(abc.Length)]);
        return sb.ToString();
    }

    static PwDatabase NewDatabase(string path, string name, string rootName)
    {
        if (File.Exists(path)) File.Delete(path);
        var pd = new PwDatabase();
        pd.New(IOConnectionInfo.FromPath(path), Key());
        pd.KdfParameters = Argon2d();
        pd.Name = name;
        pd.NameChanged = pd.DescriptionChanged = pd.DefaultUserNameChanged = pd.MasterKeyChanged = pd.SettingsChanged = T0;
        var root = new PwGroup(false, false);
        root.Uuid = Id("Root");
        root.Name = rootName;
        root.IconId = PwIcon.FolderOpen;
        root.IsExpanded = true;
        root.CreationTime = root.LastModificationTime = root.LastAccessTime = root.LocationChanged = T0;
        root.ExpiryTime = T0;
        pd.RootGroup = root;
        pd.EntryTemplatesGroupChanged = T0;
        return pd;
    }

    static PwGroup StoreGroup(PwGroup root, string alias)
    {
        var kg = NewGroup(root, alias, "KeePassGate", At(5));
        kg.EnableSearching = false;
        kg.EnableAutoType = false;
        return kg;
    }

    static PwGroup Bin(PwDatabase pd)
    {
        var bin = NewGroup(pd.RootGroup, "Bin", "Recycle Bin", At(5));
        bin.IconId = PwIcon.TrashBin;
        bin.EnableSearching = false;
        bin.EnableAutoType = false;
        pd.RecycleBinEnabled = true;
        pd.RecycleBinUuid = bin.Uuid;
        pd.RecycleBinChanged = At(5);
        return bin;
    }

    static void SaveClose(PwDatabase pd, string path)
    {
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + path + " (" + new FileInfo(path).Length.ToString(Inv) + " bytes)");
    }

    static PwDatabase Open(string file)
    {
        var pd = new PwDatabase();
        pd.Open(IOConnectionInfo.FromPath(file), Key(), null);
        return pd;
    }

    static PwEntry Entry(PwDatabase pd, string alias)
    {
        var id = Id(alias);
        return pd.RootGroup.GetEntries(true).First(e => e.Uuid.Equals(id));
    }

    static PwEntry FindEntry(PwDatabase pd, string alias)
    {
        var id = Id(alias);
        return pd.RootGroup.GetEntries(true).FirstOrDefault(e => e.Uuid.Equals(id));
    }

    static PwGroup Group(PwDatabase pd, string alias)
    {
        var id = Id(alias);
        if (pd.RootGroup.Uuid.Equals(id)) return pd.RootGroup;
        return pd.RootGroup.GetGroups(true).First(x => x.Uuid.Equals(id));
    }

    static void Delete(PwDatabase pd, string alias, DateTime t)
    {
        var e = Entry(pd, alias);
        e.ParentGroup.Entries.Remove(e);
        pd.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    static void Pw(PwDatabase pd, string alias, string value, DateTime t)
    {
        var e = Entry(pd, alias);
        Version(e, t, () => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(pd.MemoryProtection.ProtectPassword, value)));
    }

    // ---------------------------------------------------------------- measurements 1, 2 and 5
    //
    // OLD, the copy the window opens: group KeePassGate (KG), searching and auto-type off, holding
    // the write-once entries KG1 and KG2 with KPG.* custom strings; group Passwords (PW) holding
    // P1 (the other writer changes its password), P2 (the window does), Q1, Q2, Q3 (both do), X1
    // (the other writer deletes it; its notes are padding, so that a NEW of OLD's length can be
    // found) and K (nobody touches it); the database custom data key KPG.Profiles; a deletion
    // record D1. Every time is explicit, from T0.
    //
    // NEW, the other writer's later save, all at TD = T0 + 8 h unless named: KG3 "KPG revocation"
    // (KPG.Revoked.c2) in KeePassGate, its notes a padding of <pad> characters; the database custom
    // data key KPG.NewKey; a deletion record for DZ, which names no entry in either copy; X1 deleted
    // with its record; P1's password changed; Q1's at TD, older than any edit in the window; Q2's
    // at 2027-01-01, newer than any; Q3's at <q3>, the second of the window's edit of Q3 when a run
    // knows it; NO "other-new", a new entry in Passwords.

    static void BuildOld(string path)
    {
        var pd = NewDatabase(path, "Desk probe (invented)", "Desk probe");
        var kg = StoreGroup(pd.RootGroup, "KG");
        var pw = NewGroup(pd.RootGroup, "PW", "Passwords", At(5));
        Bin(pd);
        var kg1 = NewEntry(pd, kg, "KG1", "KPG grant 1", At(10));
        Str(kg1, "KPG.Grant", false, "{\"kind\":\"grant\",\"profile\":\"reader\",\"node\":\"Passwords\",\"mask\":\".R...L..\"}");
        var kg2 = NewEntry(pd, kg, "KG2", "KPG admission 1", At(11));
        Str(kg2, "KPG.Admission", false, "{\"kind\":\"admission\",\"client\":\"client-two\",\"profiles\":[\"reader\"]}");
        Str(kg2, "KPG.Secret", true, "{\"key\":\"invented-secret\"}");
        foreach (var a in new[] { "P1", "P2", "Q1", "Q2", "Q3", "X1", "K" })
        {
            var e = NewEntry(pd, pw, a, a, At(20));
            Std(pd, e, "user-" + a.ToLowerInvariant(), a.ToLowerInvariant() + "-old", "", "");
        }
        Str(Entry(pd, "X1"), PwDefs.NotesField, pd.MemoryProtection.ProtectNotes, Pad(11, 1500));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("D1"), At(6)));
        SetCd(pd.CustomData, "KPG.Profiles", "[\"reader\",\"writer\"]", At(15));
        SaveClose(pd, path);
    }

    static void MakeNew(string oldPath, string outPath, DateTime q3, int pad)
    {
        File.Copy(oldPath, outPath, true);
        var pd = Open(outPath);
        var kg3 = NewEntry(pd, Group(pd, "KG"), "KG3", "KPG revocation", TD);
        Str(kg3, "KPG.Revoked.c2", false, "{\"kind\":\"revocation\",\"client\":\"client-two\"}");
        Str(kg3, PwDefs.NotesField, pd.MemoryProtection.ProtectNotes, Pad(99, pad));
        SetCd(pd.CustomData, "KPG.NewKey", "{\"added\":\"by the other writer\"}", TD);
        pd.DeletedObjects.Add(new PwDeletedObject(Id("DZ"), TD));
        Delete(pd, "X1", TD);
        Pw(pd, "P1", "p1-other", TD);
        Pw(pd, "Q1", "q1-other", TD);
        Pw(pd, "Q2", "q2-other", Q2Time);
        Pw(pd, "Q3", "q3-other", q3);
        var no = NewEntry(pd, Group(pd, "PW"), "NO", "other-new", TD);
        Std(pd, no, "user-no", "no-other", "", "");
        pd.Save(null);
        pd.Close();
    }

    static void M1Base(string dir)
    {
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "OLD.kdbx");
        BuildOld(old);
        long want = new FileInfo(old).Length;

        var same = Path.Combine(dir, "NEW-same.kdbx");
        bool found = false;
        for (int pad = 0; pad <= 2000 && !found; pad++)
        {
            MakeNew(old, same, TD, pad);
            long got = new FileInfo(same).Length;
            if (got == want) { found = true; Console.WriteLine("NEW-same pad=" + pad.ToString(Inv) + " " + got.ToString(Inv) + " bytes"); }
            else if (got > want + 64) break;
        }
        if (!found) throw new InvalidOperationException("no NEW of OLD's length found");

        var fixedNew = Path.Combine(dir, "NEW-fixed.kdbx");
        MakeNew(old, fixedNew, TD, 3000);
        Console.WriteLine("wrote " + fixedNew + " (" + new FileInfo(fixedNew).Length.ToString(Inv) + " bytes)");
        File.Copy(old, Path.Combine(dir, "NEW-ident.kdbx"), true);
        Console.WriteLine("OLD " + want.ToString(Inv) + " bytes");
    }

    // ---------------------------------------------------------------- measurement 3
    //
    // A user's entries in Passwords. U1 "Mail account" carries KPG.Ref in plain text beside every
    // other carrier an entry has: the user's own string Extra, an attachment, a tag, a custom data
    // item of another plugin, and two history items. U2 "Bank" carries KPG.Ref protected. U3
    // "Shop" carries none, for a string added while its dialog is open. KeePassGate holds two
    // store entries, one with a protected string. Every value is a token no other field holds, so
    // that a search or an export shows exactly which string it came from.

    static void M3Base(string path)
    {
        var pd = NewDatabase(path, "Desk entries (invented)", "Desk entries");
        var pw = NewGroup(pd.RootGroup, "PW", "Passwords", At(5));
        var kg = StoreGroup(pd.RootGroup, "KG");
        Bin(pd);
        var u1 = NewEntry(pd, pw, "U1", "Mail account", At(20));
        Std(pd, u1, "alice-invented", "u1-pw-invented", "https://mail.example.invalid/", "user notes of U1");
        Version(u1, At(100), () => Str(u1, "Extra", false, "extra-u1-v1"));
        Version(u1, At(200), () =>
        {
            Str(u1, "Extra", false, "zqxextra-u1");
            Str(u1, "KPG.Ref", false, "zqxref-u1 {\"rule\":\"KG1\"}");
            u1.Binaries.Set("u1.txt", new ProtectedBinary(false, Encoding.ASCII.GetBytes("zqxattach-u1 invented attachment\r\n")));
            u1.Tags.Add("work");
            u1.CustomData.Set("Other.Plugin", "zqxcd-u1");
        });
        var u2 = NewEntry(pd, pw, "U2", "Bank", At(21));
        Std(pd, u2, "bob-invented", "u2-pw-invented", "https://bank.example.invalid/", "");
        Str(u2, "KPG.Ref", true, "zqxref-u2-protected");
        var u3 = NewEntry(pd, pw, "U3", "Shop", At(22));
        Std(pd, u3, "carol-invented", "u3-pw-invented", "https://shop.example.invalid/", "");
        var s1 = NewEntry(pd, kg, "S1", "KPG grant 1", At(10));
        Str(s1, "KPG.Grant", false, "zqxstore-grant {\"profile\":\"reader\",\"mask\":\".R...L..\"}");
        Str(s1, "KPG.Secret", true, "zqxstore-secret");
        var s2 = NewEntry(pd, kg, "S2", "KPG admission 1", At(11));
        Str(s2, "KPG.Admission", false, "zqxstore-adm {\"client\":\"client-two\"}");
        SaveClose(pd, path);
    }

    static void M3Other(string path)
    {
        var pd = NewDatabase(path, "Desk other (invented)", "Desk other");
        NewGroup(pd.RootGroup, "IM", "Imported", At(5));
        SaveClose(pd, path);
    }

    // ---------------------------------------------------------------- measurement 4
    //
    // The store made of entries. KeePassGate (KG), searching and auto-type off, holds S1 to S4 and
    // an empty subgroup Archive (KGA); S1c is S1's content under another identifier and a later
    // time; S6c is S6's content later, in KG, while S6 sits in the recycle bin; S7c is S7's content
    // later, in the recycle bin, while S7 sits in KG; S5 is a store entry in the recycle bin alone.
    // Passwords holds U1 with three history items 533, 107 and 6 days before 2026-11-16 and U2
    // without history. "Empty group" is empty. Deletion records: W-old and W-new name identifiers
    // that never existed (as a witness does), R-old and R-new entries that were deleted for real,
    // C-old and C-new name S3 and S4, which are present (as after a merge that applied no record);
    // each pair at 2025-01-01 and 2026-11-15. One custom icon is used by S2, one by nothing.

    static byte[] Png(Color c)
    {
        using (var bmp = new Bitmap(16, 16))
        {
            using (var g = Graphics.FromImage(bmp)) g.Clear(c);
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }

    static void M4Base(string path)
    {
        var pd = NewDatabase(path, "Desk store (invented)", "Desk store");
        var kg = StoreGroup(pd.RootGroup, "KG");
        var kga = NewGroup(kg, "KGA", "Archive", At(6));
        var pw = NewGroup(pd.RootGroup, "PW", "Passwords", At(5));
        NewGroup(pd.RootGroup, "EG", "Empty group", At(5));
        var bin = Bin(pd);

        Func<PwGroup, string, string, int, PwEntry> store = (g, alias, kind, minute) =>
        {
            var e = NewEntry(pd, g, alias, "KPG " + kind, At(minute));
            Str(e, "KPG.Kind", false, kind);
            Str(e, "KPG.Body", false, "{\"kind\":\"" + kind + "\",\"n\":\"" + alias.TrimEnd('c') + "\"}");
            return e;
        };
        store(kg, "S1", "grant 1", 10);
        var s2 = store(kg, "S2", "admission 1", 11);
        store(kg, "S3", "revocation 1", 12);
        store(kg, "S4", "profile 1", 13);
        var s1c = store(kg, "S1c", "grant 1", 60);
        s1c.Strings.Set("KPG.Body", Entry(pd, "S1").Strings.Get("KPG.Body"));
        var s5 = store(bin, "S5", "grant 5", 14);
        s5.PreviousParentGroup = kg.Uuid;
        var s6 = store(bin, "S6", "grant 6", 15);
        s6.PreviousParentGroup = kg.Uuid;
        store(kg, "S6c", "grant 6", 61).Strings.Set("KPG.Body", s6.Strings.Get("KPG.Body"));
        var s7 = store(kg, "S7", "grant 7", 16);
        var s7c = store(bin, "S7c", "grant 7", 62);
        s7c.Strings.Set("KPG.Body", s7.Strings.Get("KPG.Body"));
        s7c.PreviousParentGroup = kg.Uuid;

        var u1 = NewEntry(pd, pw, "U1", "Mail account", Day(2025, 6, 1));
        Std(pd, u1, "alice-invented", "u1-v1", "", "");
        Version(u1, Day(2026, 8, 1), () => Std(pd, u1, "alice-invented", "u1-v2", "", ""));
        Version(u1, Day(2026, 11, 10), () => Std(pd, u1, "alice-invented", "u1-v3", "", ""));
        Version(u1, Day(2026, 11, 12), () => Std(pd, u1, "alice-invented", "u1-v4", "", ""));
        var u2 = NewEntry(pd, pw, "U2", "Bank", At(30));
        Std(pd, u2, "bob-invented", "u2-pw", "", "");

        pd.DeletedObjects.Add(new PwDeletedObject(Id("W-old"), Day(2025, 1, 1)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("W-new"), Day(2026, 11, 15)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("R-old"), Day(2025, 1, 1)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("R-new"), Day(2026, 11, 15)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("S3"), Day(2025, 1, 1)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("S4"), Day(2026, 11, 15)));

        var used = new PwCustomIcon(Id("IconUsed"), Png(Color.SteelBlue));
        var unused = new PwCustomIcon(Id("IconUnused"), Png(Color.OrangeRed));
        pd.CustomIcons.Add(used);
        pd.CustomIcons.Add(unused);
        s2.CustomIconUuid = used.Uuid;
        SetCd(pd.CustomData, "KPG.Profiles", "[\"reader\",\"writer\"]", At(15));
        SaveClose(pd, path);
    }

    // ---------------------------------------------------------------- reading

    static string Iso(DateTime t) { return t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv); }

    static string Sha(byte[] b)
    {
        using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            if (ch == '\\') sb.Append("\\\\");
            else if (ch == '"') sb.Append("\\\"");
            else if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else if (ch < 0x20 || ch > 0x7e) sb.Append("\\u").Append(((int)ch).ToString("x4", Inv));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    static string Val(string s)
    {
        if (s == null) return "null";
        var e = Esc(s);
        if (e.Length <= 160) return "\"" + e + "\"";
        return "len=" + s.Length.ToString(Inv) + " sha=" + Sha(Encoding.UTF8.GetBytes(s)).Substring(0, 16) + " \"" + e.Substring(0, 60) + "...\"";
    }

    static string Bool(bool? b) { return b.HasValue ? (b.Value ? "1" : "0") : "inherit"; }

    static string Version(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16).ToString(Inv) + "." + (v & 0xFFFF).ToString(Inv);
    }

    static string KdfText(KdfParameters p)
    {
        var u = p.KdfUuid;
        if (u.Equals(new AesKdf().Uuid)) return "AES-KDF rounds=" + p.GetUInt64(AesKdf.ParamRounds, 0).ToString(Inv);
        string name = u.Equals(new Argon2Kdf().Uuid) ? "Argon2d" : ("kdf " + u.ToHexString());
        return name + " it=" + p.GetUInt64(Argon2Kdf.ParamIterations, 0).ToString(Inv) + " mem=" + (p.GetUInt64(Argon2Kdf.ParamMemory, 0) / 1024).ToString(Inv)
            + "KiB par=" + p.GetUInt32(Argon2Kdf.ParamParallelism, 0).ToString(Inv);
    }

    static void EntryLines(List<string> o, string p, PwEntry e, bool current)
    {
        foreach (var kv in e.Strings)
            o.Add(p + " S " + kv.Key + " p=" + (kv.Value.IsProtected ? "1" : "0") + " " + Val(kv.Value.ReadString()));
        foreach (var kv in e.Binaries)
        {
            var d = kv.Value.ReadData();
            o.Add(p + " A " + kv.Key + " p=" + (kv.Value.IsProtected ? "1" : "0") + " len=" + d.Length.ToString(Inv) + " sha=" + Sha(d).Substring(0, 16));
        }
        o.Add(p + " tags [" + string.Join(";", e.Tags) + "]");
        foreach (var kv in e.CustomData) o.Add(p + " CD " + kv.Key + " = " + Val(kv.Value));
        o.Add(p + " t.created " + Iso(e.CreationTime));
        o.Add(p + " t.modified " + Iso(e.LastModificationTime));
        o.Add(p + " t.accessed " + Iso(e.LastAccessTime));
        o.Add(p + " t.location " + Iso(e.LocationChanged));
        o.Add(p + " t.expires " + (e.Expires ? "1 " : "0 ") + Iso(e.ExpiryTime));
        o.Add(p + " usage " + e.UsageCount.ToString(Inv));
        o.Add(p + " misc icon=" + ((int)e.IconId).ToString(Inv) + " customIcon=" + Alias(e.CustomIconUuid)
            + " fg=" + e.ForegroundColor.ToArgb().ToString("x8", Inv) + " bg=" + e.BackgroundColor.ToArgb().ToString("x8", Inv)
            + " overrideUrl=" + Val(e.OverrideUrl) + " quality=" + (e.QualityCheck ? "1" : "0"));
        o.Add(p + " autotype enabled=" + (e.AutoType.Enabled ? "1" : "0") + " obfuscation=" + ((int)e.AutoType.ObfuscationOptions).ToString(Inv)
            + " seq=" + Val(e.AutoType.DefaultSequence) + " assoc=" + e.AutoType.AssociationsCount.ToString(Inv));
        o.Add(p + " prev " + Alias(e.PreviousParentGroup));
        if (!current) return;
        o.Add(p + " parent " + Alias(e.ParentGroup.Uuid) + " (" + Esc(e.ParentGroup.Name) + ")");
        o.Add(p + " index " + e.ParentGroup.Entries.IndexOf(e).ToString(Inv));
        o.Add(p + " history.count " + e.History.UCount.ToString(Inv));
        var seen = new Dictionary<string, int>();
        foreach (var h in e.History)
        {
            var k = Iso(h.LastModificationTime);
            int c;
            seen.TryGetValue(k, out c);
            seen[k] = c + 1;
            var hp = p + " H" + k + (c > 0 ? "#" + (c + 1).ToString(Inv) : "");
            if (!h.Uuid.Equals(e.Uuid)) o.Add(hp + " uuid " + Alias(h.Uuid));
            EntryLines(o, hp, h, false);
        }
    }

    static List<string> Dump(string file)
    {
        var head = new List<string>();
        var o = new List<string>();
        var bytes = File.ReadAllBytes(file);
        head.Add("# file " + Path.GetFileName(file) + " size=" + bytes.Length.ToString(Inv) + " sha256=" + Sha(bytes));
        PwDatabase pd;
        try { pd = Open(file); }
        catch (Exception ex)
        {
            head.Add("# READ FAILED " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " "));
            return head;
        }
        try
        {
            o.Add("F version " + Version(file));
            o.Add("F kdf " + KdfText(pd.KdfParameters));
            var cipher = CipherPool.GlobalPool.GetCipher(pd.DataCipherUuid);
            o.Add("F cipher " + (cipher != null ? cipher.DisplayName : pd.DataCipherUuid.ToHexString()));
            o.Add("F compression " + pd.Compression);
            o.Add("F groups " + (pd.RootGroup.GetGroups(true).UCount + 1).ToString(Inv) + " entries " + pd.RootGroup.GetEntries(true).UCount.ToString(Inv));
            o.Add("M name " + Val(pd.Name) + " changed " + Iso(pd.NameChanged));
            o.Add("M description " + Val(pd.Description) + " changed " + Iso(pd.DescriptionChanged));
            o.Add("M defaultUserName " + Val(pd.DefaultUserName) + " changed " + Iso(pd.DefaultUserNameChanged));
            o.Add("M settingsChanged " + Iso(pd.SettingsChanged));
            o.Add("M masterKeyChanged " + Iso(pd.MasterKeyChanged));
            o.Add("M historyMaxItems " + pd.HistoryMaxItems.ToString(Inv) + " historyMaxSize " + pd.HistoryMaxSize.ToString(Inv)
                + " maintenanceHistoryDays " + pd.MaintenanceHistoryDays.ToString(Inv));
            o.Add("M recycleBin enabled=" + (pd.RecycleBinEnabled ? 1 : 0) + " group=" + Alias(pd.RecycleBinUuid) + " changed " + Iso(pd.RecycleBinChanged));
            o.Add("M templatesGroup " + Alias(pd.EntryTemplatesGroup) + " changed " + Iso(pd.EntryTemplatesGroupChanged));
            o.Add("M customIcons " + pd.CustomIcons.Count.ToString(Inv) + " [" + string.Join(";", pd.CustomIcons.Select(i => Alias(i.Uuid))) + "]");
            o.Add("M publicCustomData " + pd.PublicCustomData.Count.ToString(Inv));
            foreach (var kv in pd.CustomData)
            {
                var t = CdTime(pd.CustomData, kv.Key);
                o.Add("M CD " + kv.Key + " = " + Val(kv.Value) + " @" + (t.HasValue ? Iso(t.Value) : "none"));
            }
            foreach (var d in pd.DeletedObjects) o.Add("D " + Alias(d.Uuid) + " " + Iso(d.DeletionTime));
            o.Add("D count " + pd.DeletedObjects.UCount.ToString(Inv));
            foreach (var g in new[] { pd.RootGroup }.Concat(pd.RootGroup.GetGroups(true)))
            {
                var p = "G " + Alias(g.Uuid);
                o.Add(p + " name " + Val(g.Name));
                o.Add(p + " parent " + (g.ParentGroup == null ? "-" : Alias(g.ParentGroup.Uuid) + " (" + Esc(g.ParentGroup.Name) + ")"));
                o.Add(p + " index " + (g.ParentGroup == null ? "-" : g.ParentGroup.Groups.IndexOf(g).ToString(Inv)));
                o.Add(p + " notes " + Val(g.Notes));
                o.Add(p + " tags [" + string.Join(";", g.Tags) + "]");
                foreach (var kv in g.CustomData) o.Add(p + " CD " + kv.Key + " = " + Val(kv.Value));
                o.Add(p + " t.created " + Iso(g.CreationTime));
                o.Add(p + " t.modified " + Iso(g.LastModificationTime));
                o.Add(p + " t.accessed " + Iso(g.LastAccessTime));
                o.Add(p + " t.location " + Iso(g.LocationChanged));
                o.Add(p + " t.expires " + (g.Expires ? "1 " : "0 ") + Iso(g.ExpiryTime));
                o.Add(p + " usage " + g.UsageCount.ToString(Inv));
                o.Add(p + " prev " + Alias(g.PreviousParentGroup));
                o.Add(p + " flags icon=" + ((int)g.IconId).ToString(Inv) + " customIcon=" + Alias(g.CustomIconUuid) + " search=" + Bool(g.EnableSearching)
                    + " autotype=" + Bool(g.EnableAutoType) + " expanded=" + (g.IsExpanded ? 1 : 0) + " seq=" + Val(g.DefaultAutoTypeSequence)
                    + " lastTop=" + Alias(g.LastTopVisibleEntry));
                o.Add(p + " children groups=" + g.Groups.UCount.ToString(Inv) + " entries=" + g.Entries.UCount.ToString(Inv));
            }
            foreach (var e in pd.RootGroup.GetEntries(true)) EntryLines(o, "E " + Alias(e.Uuid), e, true);
        }
        finally { pd.Close(); }
        o.Sort(StringComparer.Ordinal);
        head.AddRange(o);
        return head;
    }

    static void DumpTo(string file, string outFile)
    {
        var lines = Dump(file);
        if (outFile == null) foreach (var l in lines) Console.WriteLine(l);
        else { File.WriteAllLines(outFile, lines, new UTF8Encoding(false)); Console.WriteLine("dumped " + Path.GetFileName(file) + " (" + lines.Count + " lines)"); }
    }

    // ---------------------------------------------------------------- the verdict
    //
    // One line per file for measurements 1, 2 and 5: each item of the two copies, as the file
    // holds it. An entry's password is printed as its current value with the values its history
    // holds in brackets, oldest first; "gone" is an entry absent, "rec" a deletion record present.

    static string PwOf(PwEntry e) { return e.Strings.ReadSafe(PwDefs.PasswordField); }

    static string Item(PwDatabase pd, string alias)
    {
        var e = FindEntry(pd, alias);
        bool rec = pd.DeletedObjects.Any(d => d.Uuid.Equals(Id(alias)));
        if (e == null) return "gone" + (rec ? "+rec" : "");
        var h = string.Join(",", e.History.Select(PwOf));
        return PwOf(e) + "[" + h + "]" + (rec ? "+rec" : "") + (e.ParentGroup.Uuid.Equals(Id("Bin")) ? "@bin" : "");
    }

    static string ByTitle(PwDatabase pd, string title)
    {
        var l = pd.RootGroup.GetEntries(true).Where(e => e.Strings.ReadSafe(PwDefs.TitleField) == title).ToList();
        if (l.Count == 0) return "absent";
        return string.Join("|", l.Select(e => PwOf(e) + "@" + Esc(e.ParentGroup.Name)));
    }

    static string Verdict(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(file)).Append(" sha=").Append(Sha(bytes).Substring(0, 12)).Append(" v").Append(Version(file));
        PwDatabase pd;
        try { pd = Open(file); }
        catch (Exception ex) { return sb.Append(" READ FAILED ").Append(ex.GetType().Name).ToString(); }
        try
        {
            sb.Append(" KG1=").Append(FindEntry(pd, "KG1") != null ? "kept" : "gone");
            sb.Append(" KG2=").Append(FindEntry(pd, "KG2") != null ? "kept" : "gone");
            sb.Append(" KG3=").Append(FindEntry(pd, "KG3") != null ? "kept" : "gone");
            sb.Append(" NewKey=").Append(pd.CustomData.Exists("KPG.NewKey") ? "kept" : "gone");
            sb.Append(" DZ=").Append(pd.DeletedObjects.Any(d => d.Uuid.Equals(Id("DZ"))) ? "rec" : "none");
            sb.Append(" D1=").Append(pd.DeletedObjects.Any(d => d.Uuid.Equals(Id("D1"))) ? "rec" : "none");
            sb.Append(" X1=").Append(Item(pd, "X1"));
            foreach (var a in new[] { "P1", "P2", "Q1", "Q2", "Q3", "K" }) sb.Append(' ').Append(a).Append('=').Append(Item(pd, a));
            sb.Append(" NO=").Append(ByTitle(pd, "other-new"));
            sb.Append(" NW=").Append(ByTitle(pd, "window-new"));
            sb.Append(" entries=").Append(pd.RootGroup.GetEntries(true).UCount.ToString(Inv));
            sb.Append(" records=").Append(pd.DeletedObjects.UCount.ToString(Inv));
        }
        finally { pd.Close(); }
        return sb.ToString();
    }

    static void Merge(string target, string source, string outFile, PwMergeMethod m)
    {
        var t = Open(target);
        var s = Open(source);
        t.MergeIn(s, m, null);
        s.Close();
        if (File.Exists(outFile)) File.Delete(outFile);
        t.SaveAs(IOConnectionInfo.FromPath(outFile), false, null);
        t.Close();
        Console.WriteLine("merged (" + m + ") " + Path.GetFileName(source) + " into " + Path.GetFileName(target) + " -> " + outFile);
    }

    static int Main(string[] a)
    {
        if (a.Length == 0) { Console.Error.WriteLine("m1base | m1new | m3base | m3other | m4base | dump | verdict | sync | merge"); return 2; }
        switch (a[0])
        {
            case "m1base": M1Base(a[1]); return 0;
            case "m1new":
                MakeNew(a[1], a[2], DateTime.Parse(a[3], Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), 3000);
                Console.WriteLine("wrote " + a[2] + " (Q3 at " + a[3] + ")");
                return 0;
            case "m3base": M3Base(a[1]); return 0;
            case "m3other": M3Other(a[1]); return 0;
            case "m4base": M4Base(a[1]); return 0;
            case "dump": DumpTo(a[1], a.Length > 2 ? a[2] : null); return 0;
            case "verdict":
                for (int i = 1; i < a.Length; i++) Console.WriteLine(Verdict(a[i]));
                return 0;
            case "sync": Merge(a[1], a[2], a[3], PwMergeMethod.Synchronize); return 0;
            case "merge": Merge(a[1], a[2], a[3], (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), a[4])); return 0;
        }
        Console.Error.WriteLine("unknown command " + a[0]);
        return 2;
    }
}
