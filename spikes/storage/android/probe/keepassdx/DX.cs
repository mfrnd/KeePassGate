// The KeePassDX measurement of 2026-11-16: the bases it gives the program, the copies its merges
// and overwrites use, the item verdicts, and a reader of the raw file (header, inner header, and a
// census of the XML's elements) that shows what KeePassLib does not keep: the generator name, the
// memory protection flags as the file holds them, per-key times as elements, and unknown elements.
// Invented content only, the invented password of the earlier measurements, no window.
//
//   makedx <dir>            every base, pair and hazard set below, into <dir>
//   mverdict <file> ...     one line per item of the merge design, for each file
//   owverdict <file> ...    the overwrite verdict (NEW's changes kept or lost), for each file
//   roundtrip <in> <out>    open with KeePassLib 2.62 and save as <out> (KeePass's own open and save)
//   unknown <in> <out>      <in> re-encrypted with three elements no reader knows added to its XML
//   raw <file>              the raw lines only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static partial class Store
{
    // ---------------------------------------------------------------- random-looking identifiers
    //
    // The recommended store gives its entries random identifiers. These are the first 16 bytes of
    // SHA-256 over a name, so that they look random to every program and still read back by name.

    static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal);

    static PwUuid RandId(string alias)
    {
        byte[] h;
        using (var s = SHA256.Create()) h = s.ComputeHash(Encoding.UTF8.GetBytes("kpdx-measure-2026-11-16:" + alias));
        var b = new byte[16];
        Array.Copy(h, b, 16);
        var u = new PwUuid(b);
        Known[u.ToHexString()] = alias;
        return u;
    }

    static string Hex(string alias, int n)
    {
        byte[] h;
        using (var s = SHA256.Create()) h = s.ComputeHash(Encoding.UTF8.GetBytes("kpdx-hex:" + alias));
        return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant().Substring(0, n);
    }

    static int Seed(string s) { using (var h = SHA256.Create()) return BitConverter.ToInt32(h.ComputeHash(Encoding.UTF8.GetBytes(s)), 0) & 0x7fffffff; }

    static void RegisterAll()
    {
        foreach (var a in RandAliases) RandId(a);
    }

    static readonly string[] RandAliases =
    {
        "AD1", "AD2", "AD3", "PA1", "PA2", "GR1", "GR2", "GR3", "LF1", "RA1", "RT1", "RV1", "RV2", "PR1", "PR2",
        "WD1", "WD2", "MK1", "MK2", "MR", "DRT1", "DRV1", "DRV2", "DZ", "ND", "NPh", "A1", "A2", "A3", "A4", "H1",
    };

    // ---------------------------------------------------------------- the base

    // The store's fingerprints and keys, invented.
    static readonly string Fp1 = Hex("authority-1", 64), Fp0 = Hex("authority-0", 64);
    static readonly string C1 = Hex("client-revoked", 64), C2 = Hex("client-admitted", 64), C3 = Hex("client-revoked-later", 64);

    static KdfParameters Argon2id()
    {
        var p = new Argon2Kdf(Argon2Type.ID).GetDefaultParameters();
        p.SetUInt64(Argon2Kdf.ParamIterations, 2);
        p.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        p.SetUInt32(Argon2Kdf.ParamParallelism, 1);
        return p;
    }

    static PwEntry Rec(PwDatabase pd, PwGroup g, string alias, string title, string kind, string record, bool check, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = RandId(alias);
        var mp = pd.MemoryProtection;
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(mp.ProtectTitle, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(mp.ProtectUserName, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(mp.ProtectPassword, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(mp.ProtectUrl, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(mp.ProtectNotes, ""));
        Str(e, "KPG.Kind", false, kind);
        Str(e, "KPG.Record", false, record);
        if (check) Str(e, "KPG.Check", false, Hex("check-" + alias, 64));
        g.AddEntry(e, true, false);
        e.CreationTime = e.LastModificationTime = e.LastAccessTime = e.LocationChanged = t;
        e.ExpiryTime = T0;
        return e;
    }

    static string AuthorityDoc(string fp, string storeKeyName)
    {
        var sb = new StringBuilder("{\"kind\":\"authority\",\"fingerprint\":\"").Append(fp).Append("\",\"certificate\":\"");
        sb.Append(Pad(Seed(fp), 900)).Append("\",\"privateKey\":\"").Append(Pad(Seed(fp) + 1, 1200));
        sb.Append("\",\"host\":\"").Append(Pad(Seed(fp) + 2, 400)).Append("\",\"gateKey\":\"").Append(Hex("gate-" + fp, 64));
        sb.Append("\",\"storeKey\":\"").Append(Hex(storeKeyName, 64)).Append("\",\"note\":\"invented\"}");
        return sb.ToString();
    }

    // opts: v41 (4.1 features: group tags, per-key times, previous parent, quality check off)
    static void BuildDX(string path, KdfParameters kdf, bool v41, Action<PwDatabase> extra = null)
    {
        if (File.Exists(path)) File.Delete(path);
        RegisterAll();
        var pd = new PwDatabase();
        pd.New(IOConnectionInfo.FromPath(path), Key());
        pd.KdfParameters = kdf;
        pd.Name = "Storage probe DX (invented)";
        pd.Description = "Throwaway database, invented content only";
        pd.DefaultUserName = "invented-default-user";
        pd.Color = System.Drawing.Color.FromArgb(0x33, 0x66, 0xCC);
        pd.NameChanged = pd.DescriptionChanged = pd.DefaultUserNameChanged = pd.MasterKeyChanged = T0;
        pd.SettingsChanged = T0;
        pd.HistoryMaxItems = 8;
        pd.HistoryMaxSize = 5L * 1024 * 1024;
        pd.MaintenanceHistoryDays = 200;

        var root = new PwGroup(false, false);
        root.Uuid = Id("Root");
        root.Name = "Storage probe";
        root.IconId = PwIcon.FolderOpen;
        root.IsExpanded = true;
        root.CreationTime = root.LastModificationTime = root.LastAccessTime = root.LocationChanged = T0;
        root.ExpiryTime = T0;
        pd.RootGroup = root;
        pd.EntryTemplatesGroupChanged = T0;

        // The plugin's group of the recommended store, first under the root.
        var kg = NewGroup(root, "KG", "KeePassGate", At(5));
        kg.Notes = "The plugin's own group (invented).";
        kg.EnableSearching = false;
        kg.EnableAutoType = false;
        kg.IsExpanded = false;

        // Generic carriers, as in measured-android.md 1.3.
        var g = NewGroup(root, "G", "G", T0);
        g.Notes = "{\"kpg.rules\":{\"reader\":\".R...L..\",\"writer\":\"CRU..L..\"}}";
        if (v41) { g.Tags.Add("kpg.reader"); g.Tags.Add("team"); }
        g.CustomData.Set("KPG", "{\"reader\":[\"read\",\"list\"]}");
        g.LastModificationTime = At(30);
        var n = NewGroup(g, "N", "N", At(20));
        var car = NewGroup(root, "GKPG", "Carriers", At(10));
        car.Notes = "Generic carriers (invented).";
        var pw = NewGroup(root, "PW", "Passwords", At(5));
        var gd = NewGroup(root, "GD", "GD", At(5));
        var na = NewGroup(root, "NA", "NA", At(5));
        var nb = NewGroup(na, "NB", "NB", At(5));
        var nc = NewGroup(nb, "NC", "NC", At(5));
        var ps = NewGroup(root, "PS", "Personal", At(5));
        var he = NewGroup(ps, "HE", "Health", At(5));
        var bin = NewGroup(root, "Bin", "Recycle Bin", At(5));
        bin.IconId = PwIcon.TrashBin;
        bin.EnableSearching = false;
        bin.EnableAutoType = false;
        pd.RecycleBinEnabled = true;
        pd.RecycleBinUuid = bin.Uuid;
        pd.RecycleBinChanged = At(5);

        var r1 = NewEntry(pd, car, "R1", "KPG record 1", At(60));
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
            r1.Tags.Add("kpg");
        });
        Version(r1, At(240), () =>
        {
            Str(r1, "KPG.Secret", true, "{\"key\":\"invented-secret-v1\"}");
            r1.Tags.Add("record");
            r1.CustomData.Set("KPG.cd", "{\"control\":\"entry custom data on R1\"}");
        });
        var r2 = NewEntry(pd, car, "R2", "KPG record 2", At(40));
        Str(r2, "KPG.20261115T101502Z.e5f6", false, "{\"at\":\"2026-11-15T10:15:02Z\",\"op\":\"grant\",\"who\":\"agent-two\"}");
        r2.Binaries.Set("copy.p12", new ProtectedBinary(true, P12));
        r2.Tags.Add("kpg");
        if (v41) r2.QualityCheck = false;

        var gm = NewEntry(pd, g, "Gm", "KPG marker", At(35));
        Str(gm, "KPG.Marker", false, "{\"marker\":\"G\",\"rules\":{\"reader\":\".R...L..\"}}");
        var e1 = NewEntry(pd, g, "E1", "E1", At(60));
        Std(pd, e1, "alice-invented", "pw-e1-not-real", "https://e1.example.invalid/", "ordinary notes");
        Version(e1, At(120), () => { Str(e1, "Extra", false, "extra-v1"); });
        Version(e1, At(180), () => { Str(e1, "Extra", false, "extra-v2"); Str(e1, "Old", false, "old-field"); });
        Version(e1, At(240), () =>
        {
            Str(e1, "Extra", false, "extra-value-e1");
            Str(e1, "Secret", true, "protected-custom-e1");
            e1.Strings.Remove("Old");
            e1.Binaries.Set("e1.txt", new ProtectedBinary(false, E1Att));
            e1.Tags.Add("work");
            e1.CustomData.Set("KPG", "{\"reader\":[\"read\",\"list\"]}");
        });
        var e2 = NewEntry(pd, g, "E2", "E2", At(45));
        Std(pd, e2, "bob-invented", "pw-e2-not-real", "", "");
        var e3 = NewEntry(pd, g, "E3", "E3", At(45));
        Std(pd, e3, "carol-invented", "pw-e3-not-real", "", "a marked entry");
        Str(e3, "KPG.Mark", false, RandId("MK2").ToHexString());
        var n1 = NewEntry(pd, n, "N1", "N1", At(25));
        Std(pd, n1, "user-n1", "pw-n1", "", "");
        if (v41) n1.PreviousParentGroup = g.Uuid;

        foreach (var a in new[] { "P1", "P2", "Q1", "Q2", "Q3", "X1", "X2", "X3", "X4", "Y3", "Y4", "Y5", "Y6", "K" })
        {
            var x = NewEntry(pd, pw, a, a, At(20));
            Std(pd, x, "user-" + a.ToLowerInvariant(), a.ToLowerInvariant() + "-old", "", "");
        }
        var gd1 = NewEntry(pd, gd, "GD1", "GD1", At(20));
        Std(pd, gd1, "user-gd1", "gd1-old", "", "");
        foreach (var t in new[] { Tuple.Create(na, "NA1"), Tuple.Create(nb, "NB1"), Tuple.Create(nc, "NC1") })
        {
            var x = NewEntry(pd, t.Item1, t.Item2, t.Item2, At(20));
            Std(pd, x, "user-" + t.Item2.ToLowerInvariant(), t.Item2.ToLowerInvariant() + "-old", "", "");
        }
        var he1 = NewEntry(pd, he, "HE1", "HE1", At(20));
        Std(pd, he1, "user-he1", "he1-old", "", "health notes, invented");

        // The recommended store (EVALUATION-final.md 6.1), every kind once or twice.
        string mk1 = RandId("MK1").ToHexString(), mk2 = RandId("MK2").ToHexString();
        Rec(pd, kg, "AD1", "KPG client build-agent", "admission",
            "{\"certificate\":\"" + C2 + "\",\"name\":\"build-agent\",\"profiles\":[\"reader\"],\"notAfter\":\"2027-11-01T00:00:00Z\"}", true, At(60));
        Rec(pd, kg, "AD2", "KPG client ci-runner", "admission",
            "{\"certificate\":\"" + Hex("client-ci", 64) + "\",\"name\":\"ci-runner\",\"profiles\":[\"writer\"],\"notAfter\":\"2027-11-01T00:00:00Z\"}", true, At(61));
        Rec(pd, kg, "PA1", "KPG profile reader", "profile-addition", "{\"name\":\"reader\",\"sawRemovals\":[]}", true, At(50));
        Rec(pd, kg, "PA2", "KPG profile writer", "profile-addition", "{\"name\":\"writer\",\"sawRemovals\":[]}", true, At(51));
        Rec(pd, kg, "GR1", "KPG grant reader .R...L.P Passwords", "grant",
            "{\"node\":\"" + Id("PW").ToHexString() + "\",\"profile\":\"reader\",\"letters\":\".R...L.P\",\"sawWithdrawals\":[]}", true, At(62));
        Rec(pd, kg, "GR2", "KPG grant writer CRU..L.. Passwords", "grant",
            "{\"node\":\"" + Id("PW").ToHexString() + "\",\"profile\":\"writer\",\"letters\":\"CRU..L..\",\"sawWithdrawals\":[\"" + RandId("WD1").ToHexString() + "\"]}", true, At(63));
        Rec(pd, kg, "LF1", "KPG lift (all) Personal/Health", "lift", "{\"lifts\":[\"" + mk1 + "\"]}", true, At(70));
        Rec(pd, kg, "RA1", "KPG renewal refused ci-runner", "renewal-answer",
            "{\"certificate\":\"" + Hex("client-ci", 64) + "\",\"answer\":\"refused\",\"until\":\"2027-01-01T00:00:00Z\",\"replaces\":[]}", true, At(71));
        Rec(pd, kg, "RT1", "KPG retired " + Fp0.Substring(0, 12), "retirement", "{\"authority\":\"" + Fp0 + "\"}", true, At(40));
        Rec(pd, kg, "RV1", "KPG revoked old-agent", "revocation", "{\"certificate\":\"" + C1 + "\",\"name\":\"old-agent\"}", false, At(65));
        Rec(pd, kg, "PR1", "KPG profile removed legacy", "profile-removal", "{\"name\":\"legacy\"}", false, At(52));
        Rec(pd, kg, "WD1", "KPG withdrawn writer CRUSDL.. Passwords", "withdrawal",
            "{\"node\":\"" + Id("PW").ToHexString() + "\",\"profile\":\"writer\",\"letters\":\"...SD...\"}", false, At(53));
        Rec(pd, kg, "MK1", "KPG withheld (all) Personal/Health", "mark",
            "{\"node\":\"" + Id("HE").ToHexString() + "\",\"profile\":\"(all)\",\"letters\":\"CRUSDLAP\"}", false, At(66));
        Rec(pd, kg, "MK2", "KPG withheld reader G/E3", "mark",
            "{\"node\":\"" + Id("E3").ToHexString() + "\",\"profile\":\"reader\",\"letters\":\".R......\"}", false, At(67));
        var mr = NewEntry(pd, he, "MRx", "KPG mark", At(66));
        mr.Uuid = RandId("MR");
        Str(mr, "KPG.Mark", false, mk1);

        pd.DeletedObjects.Add(new PwDeletedObject(Id("D1"), At(6)));
        pd.DeletedObjects.Add(new PwDeletedObject(Id("D2"), At(7)));
        pd.DeletedObjects.Add(new PwDeletedObject(RandId("DRT1"), At(40)));
        pd.DeletedObjects.Add(new PwDeletedObject(RandId("DRV1"), At(65)));

        Func<int, DateTime?> tt = m => v41 ? (DateTime?)At(m) : null;
        SetCd(pd.CustomData, "KPG.Authority." + Fp0, AuthorityDoc(Fp0, "store-key-0"), tt(3));
        SetCd(pd.CustomData, "KPG.Authority." + Fp1, AuthorityDoc(Fp1, "store-key-1"), tt(41));
        SetCd(pd.CustomData, "KPG.RetiredAuthority." + Fp0, Hex("retirement-check", 64), tt(40));
        SetCd(pd.CustomData, "KPG.Revoked." + C1, "{\"at\":\"2026-11-01T11:05:00Z\"}", tt(65));
        SetCd(pd.CustomData, "KPG.ProfileRemoved." + RandId("PR1").ToHexString(), "{\"name\":\"legacy\"}", tt(52));
        SetCd(pd.CustomData, "KPG.Withdrawn." + RandId("WD1").ToHexString(), "{\"grant\":\"writer Passwords\",\"letters\":\"...SD...\"}", tt(53));
        SetCd(pd.CustomData, "KPG.Withheld." + mk1, "{\"node\":\"Personal/Health\"}", tt(66));
        SetCd(pd.CustomData, "KPG.Withheld." + mk2, "{\"node\":\"G/E3\"}", tt(67));
        // Today's controls.
        SetCd(pd.CustomData, "KPG.Client." + C2, "{\"name\":\"build-agent\",\"profiles\":[\"reader\"]}", tt(60));
        SetCd(pd.CustomData, "KPG.Client." + C1, "", tt(65));
        SetCd(pd.CustomData, "KPG.Profiles", "[\"reader\",\"writer\"]", tt(15));
        SetCd(pd.CustomData, "KPG.Untimed", "no time on this key", null);

        if (extra != null) extra(pd);
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + path + " (" + new FileInfo(path).Length.ToString(Inv) + " bytes)");
    }

    // ---------------------------------------------------------------- the merge pair
    //
    // PHONE stands for the phone's copy with the phone's edits saved in it; DESK for the desktop's
    // later save. Both are the base B41 changed independently, every change at an explicit time.

    static void Move(PwDatabase pd, string alias, string toGroup, DateTime location, DateTime? modified)
    {
        var e = Entry(pd, alias);
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        Group(pd, toGroup).AddEntry(e, true, false);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = location;
        if (modified.HasValue) e.LastModificationTime = e.LastAccessTime = modified.Value;
    }

    // A group deleted as KeePass deletes one: everything inside it removed and recorded, then the group.
    static void DeleteGroup(PwDatabase pd, string alias, DateTime t)
    {
        var g = Group(pd, alias);
        var before = new HashSet<string>(pd.DeletedObjects.Select(d => d.Uuid.ToHexString()));
        g.ParentGroup.Groups.Remove(g);
        g.DeleteAllObjects(pd);
        pd.DeletedObjects.Add(new PwDeletedObject(g.Uuid, t));
        foreach (var d in pd.DeletedObjects)
            if (!before.Contains(d.Uuid.ToHexString())) d.DeletionTime = t;
    }

    static void MakeDesk(string basePath, string outPath)
    {
        File.Copy(basePath, outPath, true);
        var pd = Open(outPath);
        DateTime td = At(480);
        var kg = Group(pd, "KG");
        Rec(pd, kg, "RV2", "KPG revoked ci-runner", "revocation", "{\"certificate\":\"" + C3 + "\",\"name\":\"ci-runner\"}", false, td);
        SetCd(pd.CustomData, "KPG.Revoked." + C3, "{\"at\":\"2026-11-01T18:00:00Z\"}", td);
        pd.DeletedObjects.Add(new PwDeletedObject(RandId("DRV2"), td));
        Rec(pd, kg, "AD3", "KPG client deploy-bot", "admission",
            "{\"certificate\":\"" + Hex("client-deploy", 64) + "\",\"name\":\"deploy-bot\",\"profiles\":[\"writer\"]}", true, td);
        Rec(pd, kg, "GR3", "KPG grant reader .R...L.. Personal", "grant",
            "{\"node\":\"" + Id("PS").ToHexString() + "\",\"profile\":\"reader\",\"letters\":\".R...L..\",\"sawWithdrawals\":[]}", true, td);
        Rec(pd, kg, "PR2", "KPG profile removed writer", "profile-removal", "{\"name\":\"writer\"}", false, td);
        SetCd(pd.CustomData, "KPG.ProfileRemoved." + RandId("PR2").ToHexString(), "{\"name\":\"writer\"}", td);
        Rec(pd, kg, "WD2", "KPG withdrawn reader .R...L.P Passwords", "withdrawal",
            "{\"node\":\"" + Id("PW").ToHexString() + "\",\"profile\":\"reader\",\"letters\":\".......P\"}", false, td);
        SetCd(pd.CustomData, "KPG.Withdrawn." + RandId("WD2").ToHexString(), "{\"grant\":\"reader Passwords\",\"letters\":\".......P\"}", td);
        SetCd(pd.CustomData, "KPG.NewKey", "{\"added\":\"by the desktop\"}", td);
        pd.DeletedObjects.Add(new PwDeletedObject(RandId("DZ"), td));
        Delete(pd, "X1", td);
        Delete(pd, "X2", At(300));
        var x3 = Entry(pd, "X3");
        Version(x3, At(420), () => Str(x3, PwDefs.UserNameField, false, "x3-changed-on-desktop"));
        Pw(pd, "P1", "p1-desktop", td);
        Pw(pd, "Q1", "q1-desktop", At(300));
        Pw(pd, "Q2", "q2-desktop", At(420));
        Pw(pd, "Q3", "q3-desktop", At(360));
        var nd = NewEntry(pd, Group(pd, "PW"), "ND", "ND desktop new", td);
        nd.Uuid = RandId("ND");
        Std(pd, nd, "user-nd", "nd-desktop", "", "");
        Move(pd, "Y3", "N", At(360), null);
        Move(pd, "Y4", "N", At(360), null);
        DeleteGroup(pd, "GD", At(300));
        DeleteGroup(pd, "NA", td);
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + outPath);
    }

    static void MakePhone(string basePath, string outPath)
    {
        File.Copy(basePath, outPath, true);
        var pd = Open(outPath);
        DateTime tp = At(360);
        Pw(pd, "P2", "p2-phone", tp);
        Pw(pd, "Q1", "q1-phone", tp);
        Pw(pd, "Q2", "q2-phone", tp);
        Pw(pd, "Q3", "q3-phone", tp);
        var np = NewEntry(pd, Group(pd, "PW"), "NPh", "NPh phone new", tp);
        np.Uuid = RandId("NPh");
        Std(pd, np, "user-nph", "nph-phone", "", "");
        var x2 = Entry(pd, "X2");
        Version(x2, tp, () => Str(x2, PwDefs.UserNameField, false, "x2-changed-on-phone"));
        Delete(pd, "X3", tp);
        Delete(pd, "X4", tp);
        Move(pd, "Y5", "N", tp, tp);
        Move(pd, "Y6", "N", tp, null);
        Move(pd, "Y4", "G", At(420), At(420));
        var gd1 = Entry(pd, "GD1");
        Version(gd1, tp, () => gd1.Strings.Set(PwDefs.PasswordField, new ProtectedString(pd.MemoryProtection.ProtectPassword, "gd1-phone")));
        pd.Save(null);
        pd.Close();
        Console.WriteLine("wrote " + outPath);
    }

    // ---------------------------------------------------------------- the overwrite set

    static void MakeNewDX(string oldPath, string outPath, int pad)
    {
        File.Copy(oldPath, outPath, true);
        var pd = Open(outPath);
        DateTime td = At(480);
        var rv = Rec(pd, Group(pd, "KG"), "RV2", "KPG revoked ci-runner", "revocation", "{\"certificate\":\"" + C3 + "\",\"name\":\"ci-runner\"}", false, td);
        Str(rv, PwDefs.NotesField, pd.MemoryProtection.ProtectNotes, Pad(99, pad));
        SetCd(pd.CustomData, "KPG.Revoked." + C3, "{\"at\":\"2026-11-01T18:00:00Z\"}", td);
        pd.DeletedObjects.Add(new PwDeletedObject(RandId("DRV2"), td));
        Pw(pd, "P1", "p1-desktop", td);
        Delete(pd, "X1", td);
        pd.Save(null);
        pd.Close();
    }

    static void MakeOverwriteDX(string dir, string b41)
    {
        var old = Path.Combine(dir, "OLD.kdbx");
        File.Copy(b41, old, true);
        var pd = Open(old);
        Str(Entry(pd, "X1"), PwDefs.NotesField, pd.MemoryProtection.ProtectNotes, Pad(11, 1500));
        pd.Save(null);
        pd.Close();
        long want = new FileInfo(old).Length;
        var same = Path.Combine(dir, "NEW-same.kdbx");
        bool found = false;
        for (int tries = 0; tries < 60 && !found; tries++)
            for (int pad = 0; pad <= 1700 && !found; pad += 1)
            {
                MakeNewDX(old, same, pad);
                long got = new FileInfo(same).Length;
                if (got == want) { found = true; Console.WriteLine("NEW-same pad=" + pad.ToString(Inv) + " " + got.ToString(Inv) + " bytes, OLD " + want.ToString(Inv)); }
                else if (got > want + 64) break;
            }
        if (!found) throw new InvalidOperationException("no NEW of OLD's length found");
        MakeNewDX(old, Path.Combine(dir, "NEW-diff.kdbx"), 4000);
        File.Copy(old, Path.Combine(dir, "NEW-ident.kdbx"), true);
        Console.WriteLine("OLD " + want + ", NEW-diff " + new FileInfo(Path.Combine(dir, "NEW-diff.kdbx")).Length);
    }

    // ---------------------------------------------------------------- hazard sets

    static void MakeHazards(string dir, string b41, string pairDir)
    {
        var a = File.ReadAllBytes(Path.Combine(pairDir, "a.bin"));
        var b = File.ReadAllBytes(Path.Combine(pairDir, "b.bin"));
        // Attachments whose first four MD5 bytes agree: in two entries, in one entry, and one arriving.
        Action<PwDatabase> att = pd =>
        {
            var at = NewGroup(pd.RootGroup, "AT", "Attach", At(5));
            var a1 = NewEntry(pd, at, "A1x", "A1", At(20)); a1.Uuid = RandId("A1");
            a1.Binaries.Set("a.bin", new ProtectedBinary(false, a));
            var a2 = NewEntry(pd, at, "A2x", "A2", At(20)); a2.Uuid = RandId("A2");
            a2.Binaries.Set("b.bin", new ProtectedBinary(false, b));
            var a3 = NewEntry(pd, at, "A3x", "A3", At(20)); a3.Uuid = RandId("A3");
            a3.Binaries.Set("first-a.bin", new ProtectedBinary(false, a));
            a3.Binaries.Set("second-b.bin", new ProtectedBinary(false, b));
        };
        BuildDX(Path.Combine(dir, "HATT.kdbx"), Argon2d(), true, att);
        Action<PwDatabase> attOpen = pd =>
        {
            var at = NewGroup(pd.RootGroup, "AT", "Attach", At(5));
            var a1 = NewEntry(pd, at, "A1x", "A1", At(20)); a1.Uuid = RandId("A1");
            a1.Binaries.Set("a.bin", new ProtectedBinary(false, a));
        };
        BuildDX(Path.Combine(dir, "HATTo.kdbx"), Argon2d(), true, attOpen);
        var arr = Path.Combine(dir, "HATTa.kdbx");
        File.Copy(Path.Combine(dir, "HATTo.kdbx"), arr, true);
        var p = Open(arr);
        var a4 = NewEntry(p, Group(p, "AT"), "A4x", "A4", At(360)); a4.Uuid = RandId("A4");
        a4.Binaries.Set("b.bin", new ProtectedBinary(false, b));
        p.Save(null); p.Close();

        // Attachments only in arriving history: E1 changed three times on the arriving side, the
        // attachments it gains and loses kept only in its history; H1 a new entry the same way.
        var hh = Path.Combine(dir, "HHISTa.kdbx");
        File.Copy(b41, hh, true);
        p = Open(hh);
        var e1 = Entry(p, "E1");
        Version(e1, At(300), () => e1.Binaries.Set("hist-a.bin", new ProtectedBinary(false, Bytes(301, 700))));
        Version(e1, At(360), () => { e1.Binaries.Remove("hist-a.bin"); e1.Binaries.Set("hist-b.bin", new ProtectedBinary(false, Bytes(302, 800))); });
        Version(e1, At(420), () => e1.Binaries.Remove("hist-b.bin"));
        var h1 = NewEntry(p, Group(p, "PW"), "H1x", "H1", At(300)); h1.Uuid = RandId("H1");
        h1.Binaries.Set("h1-old.bin", new ProtectedBinary(false, Bytes(303, 900)));
        Version(h1, At(360), () => { h1.Binaries.Remove("h1-old.bin"); h1.Binaries.Set("h1-cur.bin", new ProtectedBinary(false, Bytes(304, 1000))); });
        p.Save(null); p.Close();

        // Nested groups deleted on one side, with something inside changed later on the other.
        var nd = Path.Combine(dir, "HNESTa.kdbx");
        File.Copy(b41, nd, true);
        p = Open(nd); DeleteGroup(p, "NA", At(480)); p.Save(null); p.Close();
        var ng = Path.Combine(dir, "HNESTg.kdbx");
        File.Copy(b41, ng, true);
        p = Open(ng);
        var nc = Group(p, "NC"); nc.Notes = "changed after the deletion"; nc.LastModificationTime = At(540);
        p.Save(null); p.Close();
        var ne = Path.Combine(dir, "HNESTe.kdbx");
        File.Copy(b41, ne, true);
        p = Open(ne);
        var nb1 = Entry(p, "NB1");
        Version(nb1, At(540), () => nb1.Strings.Set(PwDefs.PasswordField, new ProtectedString(p.MemoryProtection.ProtectPassword, "nb1-changed-later")));
        p.Save(null); p.Close();

        // History limits: 3 items and 6000 bytes; HL1 at the item limit, HL2 near the size limit.
        Action<PwDatabase> hl = pd =>
        {
            pd.HistoryMaxItems = 3;
            pd.HistoryMaxSize = 6000;
            var hg = NewGroup(pd.RootGroup, "HG", "History", At(5));
            var e = NewEntry(pd, hg, "HL1", "HL1", At(20));
            Std(pd, e, "user-hl1", "hl1-v0", "", "");
            for (int i = 1; i <= 3; i++) { int k = i; Version(e, At(20 + 10 * k), () => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, "hl1-v" + k))); }
            var f = NewEntry(pd, hg, "HL2", "HL2", At(20));
            Std(pd, f, "user-hl2", "hl2-v0", "", Pad(201, 2000));
            Version(f, At(30), () => f.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Pad(202, 2000))));
            Version(f, At(40), () => f.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Pad(203, 2000))));
        };
        BuildDX(Path.Combine(dir, "HL.kdbx"), Argon2d(), true, hl);
        var hla = Path.Combine(dir, "HLa.kdbx");
        File.Copy(Path.Combine(dir, "HL.kdbx"), hla, true);
        p = Open(hla);
        var hl1 = Entry(p, "HL1");
        for (int i = 4; i <= 7; i++) { int k = i; Version(hl1, At(300 + 10 * k), () => hl1.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, "hl1-v" + k))); }
        var hl2 = Entry(p, "HL2");
        for (int i = 4; i <= 6; i++) { int k = i; Version(hl2, At(300 + 10 * k), () => hl2.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Pad(200 + k, 2000)))); }
        p.Save(null); p.Close();
    }

    static void MakeDX(string dir, string pairDir)
    {
        Directory.CreateDirectory(dir);
        var b41 = Path.Combine(dir, "B41.kdbx");
        BuildDX(b41, Argon2d(), true);
        BuildDX(Path.Combine(dir, "B41id.kdbx"), Argon2id(), true);
        BuildDX(Path.Combine(dir, "B41aes.kdbx"), Aes(), true);
        BuildDX(Path.Combine(dir, "B40.kdbx"), Argon2d(), false);
        BuildDX(Path.Combine(dir, "B40id.kdbx"), Argon2id(), false);
        BuildDX(Path.Combine(dir, "B40aes.kdbx"), Aes(), false);
        MakeDesk(b41, Path.Combine(dir, "DESK.kdbx"));
        MakePhone(b41, Path.Combine(dir, "PHONE.kdbx"));
        MakeOverwriteDX(dir, b41);
        MakeHazards(dir, b41, pairDir);
        Unknown(b41, Path.Combine(dir, "B41unk.kdbx"));
        File.Copy(Path.Combine(pairDir, "a.bin"), Path.Combine(dir, "a.bin"), true);
        File.Copy(Path.Combine(pairDir, "b.bin"), Path.Combine(dir, "b.bin"), true);
        File.WriteAllBytes(Path.Combine(dir, "attach-added.txt"), Encoding.ASCII.GetBytes(Pad(77, 1200)));
    }

    // ---------------------------------------------------------------- verdicts

    static string Where(PwDatabase pd, string alias)
    {
        var id = Known.ContainsValue(alias) ? RandId(alias) : Id(alias);
        var e = pd.RootGroup.GetEntries(true).FirstOrDefault(x => x.Uuid.Equals(id));
        if (e == null) return "absent";
        return "in " + Alias(e.ParentGroup.Uuid);
    }

    static PwEntry Find(PwDatabase pd, string alias)
    {
        var id = Known.ContainsValue(alias) ? RandId(alias) : Id(alias);
        return pd.RootGroup.GetEntries(true).FirstOrDefault(x => x.Uuid.Equals(id));
    }

    static bool Rec(PwDatabase pd, string alias)
    {
        var id = Known.ContainsValue(alias) ? RandId(alias) : Id(alias);
        return pd.DeletedObjects.Any(d => d.Uuid.Equals(id));
    }

    static string PwState(PwDatabase pd, string alias)
    {
        var e = Find(pd, alias);
        if (e == null) return "absent" + (Rec(pd, alias) ? " (record)" : " (no record)");
        var cur = e.Strings.ReadSafe(PwDefs.PasswordField);
        var hist = e.History.Select(h => h.Strings.ReadSafe(PwDefs.PasswordField)).Where(v => v != cur).Distinct().ToList();
        return cur + " in " + Alias(e.ParentGroup.Uuid) + (hist.Count > 0 ? " [history: " + string.Join(",", hist) + "]" : "") + (Rec(pd, alias) ? " (record too)" : "");
    }

    static string GroupState(PwDatabase pd, string alias)
    {
        var id = Id(alias);
        PwGroup g = pd.RootGroup.GetGroups(true).FirstOrDefault(x => x.Uuid.Equals(id));
        string r = Rec(pd, alias) ? ", record" : ", no record";
        if (g == null) return "absent" + r;
        return "under " + Alias(g.ParentGroup.Uuid) + r;
    }

    static void MVerdict(string file)
    {
        RegisterAll();
        var pd = Open(file);
        var o = new List<string>();
        Func<string, bool> key = k => pd.CustomData.Exists(k);
        Action<string, string> L = (k, v) => o.Add(k.PadRight(28) + v);
        L("RV2 revocation entry", Where(pd, "RV2"));
        L("RV2 key KPG.Revoked.c3", key("KPG.Revoked." + C3) ? "present" : "ABSENT");
        L("RV2 record DRV2", Rec(pd, "DRV2") ? "present" : "ABSENT");
        L("AD3 admission entry", Where(pd, "AD3"));
        L("GR3 grant entry", Where(pd, "GR3"));
        L("PR2 removal entry", Where(pd, "PR2"));
        L("PR2 key", key("KPG.ProfileRemoved." + RandId("PR2").ToHexString()) ? "present" : "ABSENT");
        L("WD2 withdrawal entry", Where(pd, "WD2"));
        L("WD2 key", key("KPG.Withdrawn." + RandId("WD2").ToHexString()) ? "present" : "ABSENT");
        L("NewKey custom data", key("KPG.NewKey") ? "present" : "ABSENT");
        L("DZ record (no entry)", Rec(pd, "DZ") ? "present" : "ABSENT");
        L("X1 desk-deleted", PwState(pd, "X1"));
        L("X2 desk-del, phone-later", PwState(pd, "X2") + " user=" + (Find(pd, "X2") != null ? Find(pd, "X2").Strings.ReadSafe(PwDefs.UserNameField) : "-"));
        L("X3 phone-del, desk-later", PwState(pd, "X3") + " user=" + (Find(pd, "X3") != null ? Find(pd, "X3").Strings.ReadSafe(PwDefs.UserNameField) : "-"));
        L("X4 phone-deleted", PwState(pd, "X4"));
        L("P1 desk only", PwState(pd, "P1"));
        L("P2 phone only", PwState(pd, "P2"));
        L("Q1 desk older", PwState(pd, "Q1"));
        L("Q2 desk newer", PwState(pd, "Q2"));
        L("Q3 equal times", PwState(pd, "Q3"));
        L("ND desk new", Where(pd, "ND"));
        L("NPh phone new", Where(pd, "NPh"));
        L("Y3 desk move PW->N", Where(pd, "Y3") + " prev=" + (Find(pd, "Y3") != null ? Alias(Find(pd, "Y3").PreviousParentGroup) : "-"));
        L("Y4 desk->N, phone->G", Where(pd, "Y4"));
        L("Y5 phone ->N, mod time", Where(pd, "Y5"));
        L("Y6 phone ->N, no mod", Where(pd, "Y6"));
        L("GD group desk-deleted", GroupState(pd, "GD"));
        L("GD1 phone-later", PwState(pd, "GD1"));
        L("NA tree desk-deleted", GroupState(pd, "NA") + "; NB " + GroupState(pd, "NB") + "; NC " + GroupState(pd, "NC"));
        L("NA1 NB1 NC1", PwState(pd, "NA1") + "; " + PwState(pd, "NB1") + "; " + PwState(pd, "NC1"));
        L("base records", string.Join(" ", new[] { "AD1", "AD2", "PA1", "PA2", "GR1", "GR2", "LF1", "RA1", "RT1", "RV1", "PR1", "WD1", "MK1", "MK2", "MR" }.Select(x => x + ":" + (Find(pd, x) != null ? "y" : "N"))));
        L("base keys", pd.CustomData.Count(kv => kv.Key.StartsWith("KPG.")).ToString(Inv) + " KPG. keys");
        L("deletion records", pd.DeletedObjects.UCount.ToString(Inv) + ": " + string.Join(" ", pd.DeletedObjects.Select(d => Alias(d.Uuid)).OrderBy(x => x, StringComparer.Ordinal)));
        L("deletion order", string.Join(" ", pd.DeletedObjects.Select(d => Alias(d.Uuid))));
        pd.Close();
        Console.WriteLine("== " + Path.GetFileName(file));
        foreach (var l in o) Console.WriteLine("  " + l);
    }

    static void OwVerdict(string file)
    {
        RegisterAll();
        var pd = Open(file);
        bool rv = Find(pd, "RV2") != null, key = pd.CustomData.Exists("KPG.Revoked." + C3), dr = Rec(pd, "DRV2");
        var p1 = Find(pd, "P1") != null ? Find(pd, "P1").Strings.ReadSafe(PwDefs.PasswordField) : "absent";
        bool x1gone = Find(pd, "X1") == null, x1rec = Rec(pd, "X1");
        string k;
        if (rv && key && dr && p1 == "p1-desktop" && x1gone && x1rec) k = "NEW's changes KEPT";
        else if (!rv && !key && !dr && p1 == "p1-old" && !x1gone && !x1rec) k = "NEW's changes LOST";
        else k = "NEW's changes PARTLY";
        var phone = pd.RootGroup.GetEntries(true).Select(e => e.Strings.ReadSafe(PwDefs.TitleField)).Where(t => t.StartsWith("phone-", StringComparison.Ordinal)).OrderBy(t => t, StringComparer.Ordinal);
        Console.WriteLine(Path.GetFileName(file) + ": " + k + " | RV2 " + (rv ? "entry" : "no entry") + ", key " + (key ? "yes" : "no") + ", record " + (dr ? "yes" : "no")
            + ", P1 " + p1 + ", X1 " + (x1gone ? "gone" : "there") + (x1rec ? " with record" : " no record") + " | phone: " + string.Join(",", phone));
        pd.Close();
    }

    static void RoundTrip(string inFile, string outFile)
    {
        var pd = Open(inFile);
        if (File.Exists(outFile)) File.Delete(outFile);
        pd.SaveAs(IOConnectionInfo.FromPath(outFile), false, null);
        pd.Close();
        Console.WriteLine("round trip " + Path.GetFileName(inFile) + " -> " + outFile);
    }

    // ---------------------------------------------------------------- the raw file

    sealed class RawFile
    {
        public byte[] HeaderBytes;      // signature through the end-of-header field
        public byte[] HeaderHash, HeaderHmac;
        public uint Version;
        public Dictionary<byte, byte[]> Fields = new Dictionary<byte, byte[]>();
        public byte[] CipherKey, HmacKey64, IV;
        public PwUuid Cipher;
        public bool Gzip;
        public byte[] Inner;            // the inner header bytes
        public byte[] Xml;
        public int InnerStreamId;
        public List<byte> BinaryFlags = new List<byte>();
    }

    static RawFile ReadRaw(string file)
    {
        var all = File.ReadAllBytes(file);
        var r = new RawFile();
        r.Version = BitConverter.ToUInt32(all, 8);
        if ((r.Version >> 16) < 4) return r;
        int pos = 12;
        while (true)
        {
            byte id = all[pos];
            int size = BitConverter.ToInt32(all, pos + 1);
            var data = new byte[size];
            Array.Copy(all, pos + 5, data, 0, size);
            pos += 5 + size;
            r.Fields[id] = data;
            if (id == 0) break;
        }
        r.HeaderBytes = new byte[pos];
        Array.Copy(all, r.HeaderBytes, pos);
        r.HeaderHash = all.Skip(pos).Take(32).ToArray();
        r.HeaderHmac = all.Skip(pos + 32).Take(32).ToArray();
        var kdf = KdfParameters.DeserializeExt(r.Fields[11]);
        var k32 = Key().GenerateKey32(kdf).ReadData();
        var seed = r.Fields[4];
        var buf = new byte[65];
        Array.Copy(seed, buf, 32);
        Array.Copy(k32, 0, buf, 32, 32);
        buf[64] = 1;
        using (var s = SHA512.Create()) r.HmacKey64 = s.ComputeHash(buf);
        using (var s = SHA256.Create()) r.CipherKey = s.ComputeHash(buf, 0, 64);
        r.Cipher = new PwUuid(r.Fields[2]);
        r.IV = r.Fields[7];
        r.Gzip = BitConverter.ToUInt32(r.Fields[3], 0) == 1;
        var eng = CipherPool.GlobalPool.GetCipher(r.Cipher);
        byte[] plain;
        using (var ms = new MemoryStream(all, pos + 64, all.Length - pos - 64))
        using (var hb = new HmacBlockStream(ms, false, true, r.HmacKey64))
        using (var dec = eng.DecryptStream(hb, r.CipherKey, r.IV))
        using (var outp = new MemoryStream())
        {
            Stream s = dec;
            if (r.Gzip) s = new GZipStream(dec, CompressionMode.Decompress);
            s.CopyTo(outp);
            plain = outp.ToArray();
        }
        int ip = 0;
        while (true)
        {
            byte id = plain[ip];
            int size = BitConverter.ToInt32(plain, ip + 1);
            if (id == 1) r.InnerStreamId = BitConverter.ToInt32(plain, ip + 5);
            if (id == 3) r.BinaryFlags.Add(plain[ip + 5]);
            ip += 5 + size;
            if (id == 0) break;
        }
        r.Inner = plain.Take(ip).ToArray();
        r.Xml = plain.Skip(ip).ToArray();
        return r;
    }

    static readonly HashSet<string> MetaText = new HashSet<string>(StringComparer.Ordinal)
    {
        "KeePassFile/Meta/Generator", "KeePassFile/Meta/MemoryProtection/ProtectTitle", "KeePassFile/Meta/MemoryProtection/ProtectUserName",
        "KeePassFile/Meta/MemoryProtection/ProtectPassword", "KeePassFile/Meta/MemoryProtection/ProtectURL", "KeePassFile/Meta/MemoryProtection/ProtectNotes",
        "KeePassFile/Meta/Color", "KeePassFile/Meta/HistoryMaxItems", "KeePassFile/Meta/HistoryMaxSize", "KeePassFile/Meta/SettingsChanged",
        "KeePassFile/Meta/DatabaseName", "KeePassFile/Meta/DefaultUserName", "KeePassFile/Meta/RecycleBinEnabled", "KeePassFile/Meta/EntryTemplatesGroup",
        "KeePassFile/Meta/MaintenanceHistoryDays", "KeePassFile/Meta/MasterKeyChangeRec", "KeePassFile/Meta/MasterKeyChangeForce",
    };

    static List<string> RawLines(string file)
    {
        var o = new List<string>();
        RawFile r;
        try { r = ReadRaw(file); }
        catch (Exception ex) { o.Add("R READ FAILED " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ")); return o; }
        if (r.Xml == null) { o.Add("R kdbx " + (r.Version >> 16) + "." + (r.Version & 0xFFFF) + ": the raw reader reads KDBX 4 only"); return o; }
        var kdf = KdfParameters.DeserializeExt(r.Fields[11]);
        var parts = new List<string>();
        var u = kdf.GetByteArray("$UUID");
        parts.Add("$UUID=" + (u != null ? new PwUuid(u).ToHexString() : "none"));
        foreach (var k in new[] { "R", "I", "M", "P", "V" })
        {
            ulong v64 = kdf.GetUInt64(k, ulong.MaxValue);
            uint v32 = kdf.GetUInt32(k, uint.MaxValue);
            if (v64 != ulong.MaxValue) parts.Add(k + "=" + v64.ToString(Inv));
            else if (v32 != uint.MaxValue) parts.Add(k + "=" + v32.ToString(Inv));
        }
        foreach (var k in new[] { "S", "K", "A" })
        {
            var bb = kdf.GetByteArray(k);
            if (bb != null) parts.Add(k + "=bytes" + bb.Length.ToString(Inv));
        }
        parts.Add("count=" + kdf.Count.ToString(Inv));
        parts.Sort(StringComparer.Ordinal);
        o.Add("R header kdf " + string.Join(" ", parts));
        o.Add("R header fields " + string.Join(",", r.Fields.Keys.OrderBy(x => x).Select(x => x.ToString(Inv))) + " compression=" + (r.Gzip ? "gzip" : "none") + " cipher=" + r.Cipher.ToHexString());
        o.Add("R header publicCustomData " + (r.Fields.ContainsKey(12) ? r.Fields[12].Length.ToString(Inv) + " bytes" : "none"));
        o.Add("R inner streamId=" + r.InnerStreamId.ToString(Inv) + " binaries=" + r.BinaryFlags.Count.ToString(Inv) + " flags=" + string.Join(",", r.BinaryFlags.Select(f => f.ToString(Inv))));
        var census = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true };
        using (var xr = XmlReader.Create(new MemoryStream(r.Xml), settings))
        {
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element)
                {
                    stack.Add(xr.Name);
                    var p = string.Join("/", stack);
                    int c; census.TryGetValue(p, out c); census[p] = c + 1;
                    if (xr.HasAttributes)
                    {
                        while (xr.MoveToNextAttribute())
                        {
                            var ap = p + "@" + xr.Name + (xr.Name == "Protected" || xr.Name == "ProtectInMemory" || xr.Name == "Compressed" ? "=" + xr.Value : "");
                            census.TryGetValue(ap, out c); census[ap] = c + 1;
                        }
                        xr.MoveToElement();
                    }
                    if (xr.IsEmptyElement) stack.RemoveAt(stack.Count - 1);
                }
                else if (xr.NodeType == XmlNodeType.EndElement) stack.RemoveAt(stack.Count - 1);
                else if (xr.NodeType == XmlNodeType.Text)
                {
                    var p = string.Join("/", stack);
                    if (MetaText.Contains(p)) o.Add("R meta " + p.Substring("KeePassFile/Meta/".Length) + " = " + Val(xr.Value));
                }
            }
        }
        foreach (var m in MetaText)
            if (census.ContainsKey(m) && !o.Any(l => l.StartsWith("R meta " + m.Substring("KeePassFile/Meta/".Length) + " = ", StringComparison.Ordinal)))
                o.Add("R meta " + m.Substring("KeePassFile/Meta/".Length) + " = (empty)");
        foreach (var kv in census) o.Add("X " + kv.Key + " " + kv.Value.ToString(Inv));
        return o;
    }

    // Three elements no KDBX reader knows, put into the XML of a KeePassLib file, which is then
    // encrypted again under its own header (unchanged, so its hash and HMAC still hold).
    static void Unknown(string inFile, string outFile)
    {
        var r = ReadRaw(inFile);
        var xml = Encoding.UTF8.GetString(r.Xml);
        Func<string, string, string, string> after = (s, anchor, ins) =>
        {
            int i = s.IndexOf(anchor, StringComparison.Ordinal);
            if (i < 0) throw new InvalidOperationException("anchor not found: " + anchor);
            return s.Substring(0, i + anchor.Length) + ins + s.Substring(i + anchor.Length);
        };
        xml = after(xml, "</Generator>", "<KPGUnknownMeta>an element no reader knows, in Meta (invented)</KPGUnknownMeta>");
        int root = xml.IndexOf("<Root>", StringComparison.Ordinal);
        var head = xml.Substring(0, root);
        var tail = xml.Substring(root);
        tail = after(tail, "</UUID>", "<KPGUnknownGroup>an element no reader knows, in the root group</KPGUnknownGroup>");
        int ent = tail.IndexOf("<Entry>", StringComparison.Ordinal);
        tail = tail.Substring(0, ent) + after(tail.Substring(ent), "</UUID>", "<KPGUnknownEntry>an element no reader knows, in an entry</KPGUnknownEntry>");
        xml = head + tail;
        var body = new MemoryStream();
        body.Write(r.Inner, 0, r.Inner.Length);
        var xb = new UTF8Encoding(false).GetBytes(xml);
        body.Write(xb, 0, xb.Length);
        var eng = CipherPool.GlobalPool.GetCipher(r.Cipher);
        var outp = new MemoryStream();
        outp.Write(r.HeaderBytes, 0, r.HeaderBytes.Length);
        outp.Write(r.HeaderHash, 0, 32);
        outp.Write(r.HeaderHmac, 0, 32);
        var blocks = new MemoryStream();
        var hb = new HmacBlockStream(blocks, true, true, r.HmacKey64);
        var enc = eng.EncryptStream(hb, r.CipherKey, r.IV);
        Stream w = enc;
        if (r.Gzip) w = new GZipStream(enc, CompressionMode.Compress);
        var bb = body.ToArray();
        w.Write(bb, 0, bb.Length);
        w.Close();
        var blk = blocks.ToArray();
        outp.Write(blk, 0, blk.Length);
        File.WriteAllBytes(outFile, outp.ToArray());
        Console.WriteLine("wrote " + outFile + " with three unknown elements");
    }
}
