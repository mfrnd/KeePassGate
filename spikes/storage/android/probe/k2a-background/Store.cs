// Storage probe for the Android measurement of 2026-11-15, against KeePass 2.62's KeePassLib.
// Modeled on the spike's Kpg.cs: it makes the throwaway databases the Android programs are given,
// reads any .kdbx back into a line-per-item dump, and runs KeePass's own Synchronize headless.
// Invented content only, one invented password, no window.
//
//   make <dir>                         the base databases and the merge pair, into <dir>
//   dump <file> [<out>]                the sorted line-per-item dump of one file
//   dumpdir <dir>                      <file>.dump.txt beside every .kdbx under <dir>
//   sync <target> <source> <out>       open target, MergeIn(source, Synchronize), save as out
//   makeow <dir>                       OLD, NEW-same, NEW-diff and NEW-ident for the overwrite runs
//
// Every line of a dump starts with what it is about and is then sorted, so that two dumps diff
// item by item: F the file, M the database's meta, D a deletion record, G a group, E an entry,
// "E <id> H<time>" one of its history items. A node is named by its identifier, not its title:
// the base's nodes carry fixed identifiers ('KPG' and an ASCII alias), which print as the alias;
// any other identifier prints as ~ and its first eight hex digits.
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
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Store
{
    public const string Password = "dummy-password-for-a-throwaway-file";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime T0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    static DateTime At(int minutes) { return T0.AddMinutes(minutes); }

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

    static KdfParameters Aes()
    {
        var p = new AesKdf().GetDefaultParameters();
        p.SetUInt64(AesKdf.ParamRounds, 60000);
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

    static string LongJson()
    {
        var sb = new StringBuilder("{\"kind\":\"record\",\"version\":3,\"items\":[");
        for (int i = 0; i < 24; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"i\":").Append(i.ToString(Inv)).Append(",\"name\":\"invented-item-").Append(i.ToString("00", Inv))
              .Append("\",\"profiles\":[\"reader\",\"writer\"],\"note\":\"padding to make a value of a few kilobytes\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    static readonly byte[] P12 = Bytes(12, 3000);
    static readonly byte[] E1Att = Bytes(1, 600);
    static readonly byte[] AddedC = Bytes(77, 1500);

    // The base: every candidate carrier, the controls beside them.
    //   nodeCd   custom data on G, E1 and R1 (the controls)
    //   tags     tags on G (a KDBX 4.1 feature) and on entries
    //   dbTimes  the database's custom data key KPG.Profiles carries a modification time (4.1)
    static void Build(string path, KdfParameters kdf, bool nodeCd, bool tags, bool dbTimes)
    {
        if (File.Exists(path)) File.Delete(path);
        var pd = new PwDatabase();
        pd.New(IOConnectionInfo.FromPath(path), Key());
        pd.KdfParameters = kdf;
        pd.Name = "Storage probe (invented)";
        pd.Description = "Throwaway database, invented content only";
        pd.NameChanged = pd.DescriptionChanged = pd.DefaultUserNameChanged = pd.MasterKeyChanged = T0;
        pd.SettingsChanged = T0;
        pd.HistoryMaxItems = 8;
        pd.HistoryMaxSize = 5L * 1024 * 1024;
        pd.MaintenanceHistoryDays = 200;
        // MemoryProtection is left at KeePassLib's defaults: KeePassLib 2.62 resets it at every load
        // and writes the standard fields' flags from it, so a non-default value would not survive
        // KeePass itself (measured: logs\keepasslib-memprot-reset.txt).

        var root = new PwGroup(false, false);
        root.Uuid = Id("Root");
        root.Name = "Storage probe";
        root.IconId = PwIcon.FolderOpen;
        root.IsExpanded = true;
        root.CreationTime = root.LastModificationTime = root.LastAccessTime = root.LocationChanged = T0;
        root.ExpiryTime = T0;
        pd.RootGroup = root;
        pd.EntryTemplatesGroupChanged = T0;

        var g = NewGroup(root, "G", "G", T0);
        g.Notes = "{\"kpg.rules\":{\"reader\":\".R...L..\",\"writer\":\"CRU..L..\"}}";
        if (tags) { g.Tags.Add("kpg.reader"); g.Tags.Add("team"); }
        if (nodeCd) g.CustomData.Set("KPG", "{\"reader\":[\"read\",\"list\"]}");
        g.LastModificationTime = At(30);
        var n = NewGroup(g, "N", "N", At(20));
        var kpg = NewGroup(root, "GKPG", "KPG", At(10));
        kpg.Notes = "The plugin's own group (invented).";
        kpg.EnableSearching = false;
        kpg.EnableAutoType = false;
        kpg.IsExpanded = false;
        var bin = NewGroup(root, "Bin", "Recycle Bin", At(5));
        bin.IconId = PwIcon.TrashBin;
        bin.EnableSearching = false;
        bin.EnableAutoType = false;
        pd.RecycleBinEnabled = true;
        pd.RecycleBinUuid = bin.Uuid;
        pd.RecycleBinChanged = At(5);

        // R1: every carrier an entry has, with three older versions whose custom fields differ.
        var r1 = NewEntry(pd, kpg, "R1", "KPG record 1", At(60));
        Std(pd, r1, "kpg-r1", "pw-r1-not-real", "kpg://record/1", "{\"kind\":\"record\",\"n\":1,\"in\":\"notes\"}");
        Str(r1, "KPG.Record", false, "{\"kind\":\"record\",\"version\":1}");
        Version(r1, At(120), () =>
        {
            Str(r1, "KPG.Record", false, "{\"kind\":\"record\",\"version\":2}");
            Str(r1, "KPG.20261115T101500Z.a1b2", false, "{\"at\":\"2026-11-15T10:15:00Z\",\"op\":\"grant\"}");
        });
        Version(r1, At(180), () =>
        {
            Str(r1, "KPG.Record", false, LongJson());
            Str(r1, "KPG.20261115T101501Z.c3d4", true, "{\"at\":\"2026-11-15T10:15:01Z\",\"op\":\"revoke\"}");
            Str(r1, "KPG.Secret", true, "{\"key\":\"invented-secret-v0\"}");
            r1.Binaries.Set("authority.p12", new ProtectedBinary(true, P12));
            if (tags) r1.Tags.Add("kpg");
        });
        Version(r1, At(240), () =>
        {
            Str(r1, "KPG.Secret", true, "{\"key\":\"invented-secret-v1\"}");
            if (tags) r1.Tags.Add("record");
            if (nodeCd) r1.CustomData.Set("KPG.cd", "{\"control\":\"entry custom data on R1\"}");
        });

        // R2: the same attachment bytes under another name, a timestamp-named field, a tag.
        var r2 = NewEntry(pd, kpg, "R2", "KPG record 2", At(40));
        Str(r2, "KPG.20261115T101502Z.e5f6", false, "{\"at\":\"2026-11-15T10:15:02Z\",\"op\":\"grant\",\"who\":\"agent-two\"}");
        r2.Binaries.Set("copy.p12", new ProtectedBinary(true, P12));
        if (tags) r2.Tags.Add("kpg");

        // M1 and M2: entries the merge pair changes on both sides.
        var m1 = NewEntry(pd, kpg, "M1", "KPG merge 1", At(50));
        Str(m1, "KPG.base", false, "{\"v\":0}");
        var m2 = NewEntry(pd, kpg, "M2", "KPG merge 2", At(50));
        Str(m2, "KPG.base", false, "{\"v\":0}");

        // G's marker entry, E1 the ordinary entry with carriers of its own, E2 to be deleted, and the
        // entries the merge pair deletes, changes and moves.
        var gm = NewEntry(pd, g, "Gm", "KPG marker", At(35));
        Str(gm, "KPG.Marker", false, "{\"marker\":\"G\",\"rules\":{\"reader\":\".R...L..\"}}");

        var e1 = NewEntry(pd, g, "E1", "E1", At(60));
        Std(pd, e1, "alice-invented", "pw-e1-not-real", "https://e1.example.invalid/", "ordinary notes");
        Version(e1, At(120), () => { Str(e1, "Extra", false, "extra-v1"); });
        Version(e1, At(180), () => { Str(e1, "Extra", false, "extra-v2"); Str(e1, "Old", false, "old-field"); });
        Version(e1, At(240), () =>
        {
            Str(e1, "Extra", false, "extra-value-e1");
            e1.Strings.Remove("Old");
            e1.Binaries.Set("e1.txt", new ProtectedBinary(false, E1Att));
            if (tags) e1.Tags.Add("work");
            if (nodeCd) e1.CustomData.Set("KPG", "{\"reader\":[\"read\",\"list\"]}");
        });

        var e2 = NewEntry(pd, g, "E2", "E2", At(45));
        Std(pd, e2, "bob-invented", "pw-e2-not-real", "", "");
        foreach (var a in new[] { "X1", "X2", "Y1", "Y2", "Y3" })
        {
            var x = NewEntry(pd, g, a, a, At(55));
            Std(pd, x, "user-" + a.ToLowerInvariant(), "pw-" + a.ToLowerInvariant(), "", "");
        }
        var n1 = NewEntry(pd, n, "N1", "N1", At(25));
        Std(pd, n1, "user-n1", "pw-n1", "", "");

        // Two entries deleted beforehand: their deletion records only.
        pd.DeletedObjects.Add(new PwDeletedObject(Id("D1"), At(6)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("D2"), At(7)));

        SetCd(pd.CustomData, "KPG.Profiles", "[\"reader\",\"writer\"]", dbTimes ? (DateTime?)At(15) : null);
        SetCd(pd.CustomData, "KPG.Untimed", "no time on this key", null);

        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + path);
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

    // The merge pair. Both are the base changed independently, every change at an explicit time.
    //   M1: a different timestamp-named field on each side, C at +5h, P at +6h
    //   M2: a different timestamp-named field on each side, both at +7h
    //   NC, NP: a new entry in the KPG group on one side each
    //   X1: deleted in P (+6h), unchanged in C;  X2: deleted in C (+5h), changed in P later (+6h)
    //   Y1: an attachment added in C (+5h);  Y2: a tag added in P (+6h)
    //   G's notes changed in C (+5h);  Y3 moved from G to N in P (+6h)
    static void MakePair(string dir, string basePath)
    {
        var c = Path.Combine(dir, "C.kdbx");
        File.Copy(basePath, c, true);
        var pd = Open(c);
        var m1 = Entry(pd, "M1");
        Version(m1, At(300), () => Str(m1, "KPG.20261115T120000Z.c", false, "{\"side\":\"C\",\"cell\":\"different times\"}"));
        var m2 = Entry(pd, "M2");
        Version(m2, At(420), () => Str(m2, "KPG.20261115T130000Z.c", false, "{\"side\":\"C\",\"cell\":\"equal times\"}"));
        var nc = NewEntry(pd, Group(pd, "GKPG"), "NC", "KPG new C", At(300));
        Str(nc, "KPG.20261115T120500Z.nc", false, "{\"side\":\"C\",\"cell\":\"new entry\"}");
        Delete(pd, "X2", At(300));
        var y1 = Entry(pd, "Y1");
        Version(y1, At(300), () => y1.Binaries.Set("added-c.bin", new ProtectedBinary(false, AddedC)));
        var g = Group(pd, "G");
        g.Notes = "{\"kpg.rules\":{\"reader\":\".R...L..\",\"writer\":\"CRU..L..\"},\"changedBy\":\"C\"}";
        g.LastModificationTime = At(300);
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + c);

        var p = Path.Combine(dir, "P.kdbx");
        File.Copy(basePath, p, true);
        pd = Open(p);
        m1 = Entry(pd, "M1");
        Version(m1, At(360), () => Str(m1, "KPG.20261115T140000Z.p", false, "{\"side\":\"P\",\"cell\":\"different times\"}"));
        m2 = Entry(pd, "M2");
        Version(m2, At(420), () => Str(m2, "KPG.20261115T130000Z.p", false, "{\"side\":\"P\",\"cell\":\"equal times\"}"));
        var np = NewEntry(pd, Group(pd, "GKPG"), "NP", "KPG new P", At(360));
        Str(np, "KPG.20261115T140500Z.np", false, "{\"side\":\"P\",\"cell\":\"new entry\"}");
        Delete(pd, "X1", At(360));
        var x2 = Entry(pd, "X2");
        Version(x2, At(360), () => Str(x2, PwDefs.UserNameField, pd.MemoryProtection.ProtectUserName, "changed-in-P"));
        var y2 = Entry(pd, "Y2");
        Version(y2, At(360), () => y2.Tags.Add("added-p"));
        var y3 = Entry(pd, "Y3");
        var from = y3.ParentGroup;
        from.Entries.Remove(y3);
        Group(pd, "N").AddEntry(y3, true, false);
        y3.PreviousParentGroup = from.Uuid;
        y3.LocationChanged = At(360);
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + p);
    }

    // ---------------------------------------------------------------- the overwrite pair
    //
    // OLD is the main base (S-argon) with X1's notes carrying 1500 random characters, so that NEW,
    // which deletes X1, has room to be made exactly as long as OLD. NEW is OLD with three changes
    // standing for what KeePass wrote later, all at +8 h:
    //   RV    a new entry "KPG revocation" in the KPG group, custom string KPG.Revoked.c2
    //   M1    its custom string KPG.base changed from {"v":0} to {"v":1,"by":"KeePass"} (old version
    //         into history first, as KeePass's dialog does)
    //   X1    deleted, with its deletion record
    // RV's notes are a padding of random characters whose length is the one free parameter:
    // NEW-same is searched until its file is exactly OLD's length, NEW-diff has 3000 more.
    // NEW-ident is OLD's bytes exactly (for a replacement that changes the time and nothing else).

    static string Pad(int seed, int n)
    {
        const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var r = new Random(seed);
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++) sb.Append(abc[r.Next(abc.Length)]);
        return sb.ToString();
    }

    static void MakeNew(string oldPath, string outPath, int pad)
    {
        File.Copy(oldPath, outPath, true);
        var pd = Open(outPath);
        var rv = NewEntry(pd, Group(pd, "GKPG"), "RV", "KPG revocation", At(480));
        Str(rv, "KPG.Revoked.c2", false, "{\"revoked\":\"client-two\",\"at\":\"2026-11-01T18:00:00Z\"}");
        Str(rv, PwDefs.NotesField, pd.MemoryProtection.ProtectNotes, Pad(99, pad));
        var m1 = Entry(pd, "M1");
        Version(m1, At(480), () => Str(m1, "KPG.base", false, "{\"v\":1,\"by\":\"KeePass\"}"));
        Delete(pd, "X1", At(480));
        pd.Save(null);
        pd.Close();
    }

    static void MakeOverwrite(string dir)
    {
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "OLD.kdbx");
        Build(old, Argon2d(), true, true, true);
        var pd = Open(old);
        var x1 = Entry(pd, "X1");
        Str(x1, PwDefs.NotesField, pd.MemoryProtection.ProtectNotes, Pad(11, 1500));
        pd.Save(null);
        pd.Close();
        long want = new FileInfo(old).Length;
        Console.WriteLine("OLD " + want.ToString(Inv) + " bytes");

        var same = Path.Combine(dir, "NEW-same.kdbx");
        bool found = false;
        for (int tries = 0; tries < 40 && !found; tries++)
        {
            for (int pad = 0; pad <= 1600 && !found; pad += 1)
            {
                MakeNew(old, same, pad);
                long got = new FileInfo(same).Length;
                if (got == want) { found = true; Console.WriteLine("NEW-same pad=" + pad.ToString(Inv) + " " + got.ToString(Inv) + " bytes (try " + tries.ToString(Inv) + ")"); }
                else if (got > want + 64) break;
            }
        }
        if (!found) throw new InvalidOperationException("no NEW of OLD's length found");

        var diff = Path.Combine(dir, "NEW-diff.kdbx");
        MakeNew(old, diff, 4000);
        Console.WriteLine("NEW-diff " + new FileInfo(diff).Length.ToString(Inv) + " bytes");
        File.Copy(old, Path.Combine(dir, "NEW-ident.kdbx"), true);
    }

    // ---------------------------------------------------------------- the synchronize pair
    //
    // SOLD, the copy the phone opens from the server: group KeePassGate (KG) holding write-once
    // entries KG1, KG2 with KPG.* custom strings; group Passwords (PW) holding ordinary entries
    // P1 (the desktop changes its password), P2 (the phone does), Q1, Q2, Q3 (both do), X1 (the
    // desktop deletes it) and K (nobody touches it); database custom data KPG.Profiles; a
    // deletion record D1. Every time is explicit, from T0.
    // SNEW = SOLD with the desktop's changes, all at TD = T0 + 8 h unless said: KG3 "KPG revocation"
    // (KPG.Revoked.c2) in KeePassGate; database custom data KPG.NewKey; a deletion record for DZ,
    // which names no entry in either copy; X1 deleted with its record; P1's password changed; Q1's
    // at TD (older than any phone edit); Q2's one hour after the phone's edit of Q2; Q3's at the
    // phone's edit of Q3 exactly. The phone's times are read from the phone's file when one is
    // given; without one, Q2 is at 2027-01-01T00:00:00Z and Q3 at TD.

    static void MakeSync(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        var pd = new PwDatabase();
        pd.New(IOConnectionInfo.FromPath(path), Key());
        pd.KdfParameters = Argon2d();
        pd.Name = "Synchronize probe (invented)";
        pd.NameChanged = pd.DescriptionChanged = pd.DefaultUserNameChanged = pd.MasterKeyChanged = pd.SettingsChanged = T0;
        var root = new PwGroup(false, false);
        root.Uuid = Id("Root");
        root.Name = "Synchronize probe";
        root.IconId = PwIcon.FolderOpen;
        root.IsExpanded = true;
        root.CreationTime = root.LastModificationTime = root.LastAccessTime = root.LocationChanged = T0;
        root.ExpiryTime = T0;
        pd.RootGroup = root;
        pd.EntryTemplatesGroupChanged = T0;
        var kg = NewGroup(root, "KG", "KeePassGate", At(5));
        kg.EnableSearching = false;
        kg.EnableAutoType = false;
        var pw = NewGroup(root, "PW", "Passwords", At(5));
        var bin = NewGroup(root, "Bin", "Recycle Bin", At(5));
        bin.IconId = PwIcon.TrashBin;
        pd.RecycleBinEnabled = true;
        pd.RecycleBinUuid = bin.Uuid;
        pd.RecycleBinChanged = At(5);
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
        pd.DeletedObjects.Add(new PwDeletedObject(Id("D1"), At(6)));
        SetCd(pd.CustomData, "KPG.Profiles", "[\"reader\",\"writer\"]", At(15));
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + path);
    }

    static void Pw(PwDatabase pd, string alias, string value, DateTime t)
    {
        var e = Entry(pd, alias);
        Version(e, t, () => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(pd.MemoryProtection.ProtectPassword, value)));
    }

    static void MakeSyncNew(string oldPath, string outPath, string phonePath)
    {
        DateTime td = At(480), q2 = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), q3 = td;
        if (!string.IsNullOrEmpty(phonePath))
        {
            var ph = Open(phonePath);
            q2 = Entry(ph, "Q2").LastModificationTime.ToUniversalTime().AddHours(1);
            q3 = Entry(ph, "Q3").LastModificationTime.ToUniversalTime();
            ph.Close();
        }
        File.Copy(oldPath, outPath, true);
        var pd = Open(outPath);
        var kg3 = NewEntry(pd, Group(pd, "KG"), "KG3", "KPG revocation", td);
        Str(kg3, "KPG.Revoked.c2", false, "{\"kind\":\"revocation\",\"client\":\"client-two\"}");
        SetCd(pd.CustomData, "KPG.NewKey", "{\"added\":\"by the desktop\"}", td);
        pd.DeletedObjects.Add(new PwDeletedObject(Id("DZ"), td));
        Delete(pd, "X1", td);
        Pw(pd, "P1", "p1-desktop", td);
        Pw(pd, "Q1", "q1-desktop", td);
        Pw(pd, "Q2", "q2-desktop", q2);
        Pw(pd, "Q3", "q3-desktop", q3);
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + outPath + " (Q2 at " + Iso(q2) + ", Q3 at " + Iso(q3) + ")");
    }

    // One of the desktop's three turns in the alternating sequence (measured-k2a-background.md 3),
    // applied to the server's file as it is now, as KeePass's dialog makes each change:
    //   a  KG3 (a write-once KPG. entry), the custom data key KPG.NewKey, P1's password, at TD
    //   b  X1 deleted with its record, the record DZ naming no entry, Q1's password at TD (older
    //      than the phone's later edit)
    //   c  Q2's password one hour after the phone's edit of Q2, Q3's at the phone's edit of Q3
    //      exactly, the times read from the phone's file
    static void DeskStep(string inPath, string outPath, string step, string phonePath)
    {
        DateTime td = At(480);
        if (inPath != outPath) File.Copy(inPath, outPath, true);
        var pd = Open(outPath);
        switch (step)
        {
            case "a":
                var kg3 = NewEntry(pd, Group(pd, "KG"), "KG3", "KPG revocation", td);
                Str(kg3, "KPG.Revoked.c2", false, "{\"kind\":\"revocation\",\"client\":\"client-two\"}");
                SetCd(pd.CustomData, "KPG.NewKey", "{\"added\":\"by the desktop\"}", td);
                Pw(pd, "P1", "p1-desktop", td);
                break;
            case "b":
                pd.DeletedObjects.Add(new PwDeletedObject(Id("DZ"), td));
                Delete(pd, "X1", td);
                Pw(pd, "Q1", "q1-desktop", td);
                break;
            case "c":
                var ph = Open(phonePath);
                DateTime q2 = Entry(ph, "Q2").LastModificationTime.ToUniversalTime().AddHours(1);
                DateTime q3 = Entry(ph, "Q3").LastModificationTime.ToUniversalTime();
                ph.Close();
                Pw(pd, "Q2", "q2-desktop", q2);
                Pw(pd, "Q3", "q3-desktop", q3);
                Console.WriteLine("Q2 at " + Iso(q2) + ", Q3 at " + Iso(q3));
                break;
            default: throw new ArgumentException("step a, b or c");
        }
        pd.Save(null);
        pd.Close();
        Console.WriteLine("desktop step " + step + " wrote " + outPath);
    }

    static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        var main = Path.Combine(dir, "S-argon.kdbx");
        Build(main, Argon2d(), true, true, true);
        Build(Path.Combine(dir, "S-aes.kdbx"), Aes(), true, true, true);
        Build(Path.Combine(dir, "S-aes-nocd.kdbx"), Aes(), false, true, true);
        Build(Path.Combine(dir, "S-aes-plain.kdbx"), Aes(), false, false, false);
        MakePair(dir, main);
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
            // No MemoryProtection line: KeePassLib 2.62 resets it to its defaults at every load
            // (KdbxFile.Read.cs:246-248), so the file's own setting cannot be read back here. The
            // protection flag of every string, which the reader takes from the file, is printed.
            o.Add("M historyMaxItems " + pd.HistoryMaxItems.ToString(Inv) + " historyMaxSize " + pd.HistoryMaxSize.ToString(Inv)
                + " maintenanceHistoryDays " + pd.MaintenanceHistoryDays.ToString(Inv));
            o.Add("M recycleBin enabled=" + (pd.RecycleBinEnabled ? 1 : 0) + " group=" + Alias(pd.RecycleBinUuid) + " changed " + Iso(pd.RecycleBinChanged));
            o.Add("M templatesGroup " + Alias(pd.EntryTemplatesGroup) + " changed " + Iso(pd.EntryTemplatesGroupChanged));
            o.Add("M customIcons " + pd.CustomIcons.Count.ToString(Inv));
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
        else { File.WriteAllLines(outFile, lines, new UTF8Encoding(false)); Console.WriteLine("dumped " + file + " -> " + outFile + " (" + lines.Count + " lines)"); }
    }

    static void Sync(string target, string source, string outFile)
    {
        var t = Open(target);
        var s = Open(source);
        t.MergeIn(s, PwMergeMethod.Synchronize, null);
        s.Close();
        if (File.Exists(outFile)) File.Delete(outFile);
        t.SaveAs(IOConnectionInfo.FromPath(outFile), false, null);
        t.Close();
        Console.WriteLine("synchronized " + Path.GetFileName(source) + " into " + Path.GetFileName(target) + " -> " + outFile);
    }

    static int Main(string[] a)
    {
        if (a.Length == 0) { Console.Error.WriteLine("make <dir> | dump <file> [<out>] | dumpdir <dir> | sync <target> <source> <out>"); return 2; }
        Console.Error.WriteLine("KeePassLib: " + typeof(PwDatabase).Assembly.FullName + ", PwDefs.VersionString " + PwDefs.VersionString);
        switch (a[0])
        {
            case "make": Make(a[1]); return 0;
            case "makeow": MakeOverwrite(a[1]); return 0;
            case "makesync": MakeSync(a[1]); return 0;
            case "newsync": MakeSyncNew(a[1], a[2], a.Length > 3 ? a[3] : null); return 0;
            case "deskstep": DeskStep(a[1], a[2], a[3], a.Length > 4 ? a[4] : null); return 0;
            case "dump": DumpTo(a[1], a.Length > 2 ? a[2] : null); return 0;
            case "dumpdir":
                foreach (var f in Directory.GetFiles(a[1], "*.kdbx", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                    DumpTo(f, f + ".dump.txt");
                return 0;
            case "sync": Sync(a[1], a[2], a[3]); return 0;
        }
        Console.Error.WriteLine("unknown command " + a[0]);
        return 2;
    }
}
