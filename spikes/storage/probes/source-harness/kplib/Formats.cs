// The inputs of the format and key derivation table, and the reading of what a program wrote.
//
//   formats <dir>   writes the inputs, each made by KeePassLib 2.62 as KeePass saves it (the
//                   lowest version that holds what the file holds, never below 4.0), but where a
//                   version is forced:
//                     v31-aes               AES-KDF, nothing above 3.1, forced to 3.1 as KeePass's
//                                           export to KDBX 3.1 does
//                     v40-aes-nocd          AES-KDF and nothing above 3.1: KeePass writes 4.0
//                     v40-aes-nocd-chacha   the same with ChaCha20
//                     v40-aes, v40-argon2d, v40-argon2id, v40-chacha
//                                           an untimed database custom data item, and the key
//                                           derivation or cipher named: 4.0
//                     v41-argon2d, v41-argon2id, v41-aes
//                                           every 4.1 item below: 4.1
//                     v41only-<item>-aes    AES-KDF and that 4.1 item alone: 4.1 (forced for
//                                           `prev`, which KeePass does not count, see the next)
//                     v41only-prev-argon2d  the same with Argon2d
//                     v41-prev-saved        the previous parent groups alone, saved as KeePass
//                                           saves it
//                   The 4.1 items: a per-key time on a database custom data item (cdtime), a
//                   group's tags (gtags), an entry's QualityCheck off (quality), a custom icon's
//                   name and time (icon), a previous parent group on an entry and on a group (prev).
//   dumpver <dir>   one line per .kdbx under <dir>: header version, KDF, cipher, compression, and
//                   each 4.1 item: kept or gone
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Formats
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime T0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    static readonly PwUuid Icon = new PwUuid(Encoding.ASCII.GetBytes("KPGicon/format41"));
    static readonly PwUuid GTagged = new PwUuid(Encoding.ASCII.GetBytes("KPGgroup/tagged1"));
    static readonly PwUuid EQuality = new PwUuid(Encoding.ASCII.GetBytes("KPGentry/quality"));
    // A 1 x 1 PNG, so that a reader that decodes icons has something it can decode.
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    static readonly MethodInfo SetWithTime = typeof(StringDictionaryEx).GetMethod("Set",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
    static readonly MethodInfo TimeOf = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static KdfParameters Aes() { var p = new AesKdf().GetDefaultParameters(); p.SetUInt64(AesKdf.ParamRounds, 60000); return p; }

    static KdfParameters Argon(bool id)
    {
        var k = new Argon2Kdf(id ? Argon2Type.ID : Argon2Type.D);
        var p = k.GetDefaultParameters();
        p.SetUInt64(Argon2Kdf.ParamIterations, 2);
        p.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        p.SetUInt32(Argon2Kdf.ParamParallelism, 1);
        return p;
    }

    // One input: the key derivation, ChaCha20 or AES, the items it holds ("cd" an untimed database
    // custom data item, "cdtime" a timed one, "gtags" a group's tags, "quality" an entry's
    // QualityCheck off, "icon" a named and timed custom icon on the entry, "prev" a previous parent
    // group on the entry and the group), and the version forced (0: KeePass's own choice on a save).
    static void Build(string path, KdfParameters kdf, bool chacha, string items, uint force)
    {
        var it = new System.Collections.Generic.HashSet<string>(items.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        if (File.Exists(path)) File.Delete(path);
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Lab.Key());
        db.KdfParameters = kdf;
        if (chacha) db.DataCipherUuid = new ChaCha20Engine().CipherUuid;
        db.Name = "Format probe (invented)";
        db.NameChanged = db.DescriptionChanged = db.DefaultUserNameChanged = db.MasterKeyChanged = db.SettingsChanged = T0;
        var root = new PwGroup(true, false) { Name = "Root" };
        root.CreationTime = root.LastModificationTime = root.LastAccessTime = root.LocationChanged = T0;
        db.RootGroup = root;
        var g = new PwGroup(false, false) { Uuid = GTagged, Name = "Tagged" };
        g.CreationTime = g.LastModificationTime = g.LastAccessTime = g.LocationChanged = T0;
        root.AddGroup(g, true);
        var e = new PwEntry(false, false) { Uuid = EQuality };
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "Quality"));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw"));
        e.CreationTime = e.LastModificationTime = e.LastAccessTime = e.LocationChanged = T0;
        g.AddEntry(e, true);
        if (it.Contains("cd")) SetWithTime.Invoke(db.CustomData, new object[] { "KPG.Untimed", "a value without a time", null });
        if (it.Contains("cdtime")) SetWithTime.Invoke(db.CustomData, new object[] { "KPG.Timed", "a value with a time", (DateTime?)T0.AddMinutes(15) });
        if (it.Contains("gtags")) g.Tags.Add("kpg-group-tag");
        if (it.Contains("quality")) e.QualityCheck = false;
        if (it.Contains("icon"))
        {
            db.CustomIcons.Add(new PwCustomIcon(Icon, Png) { Name = "kpg-icon", LastModificationTime = T0.AddMinutes(20) });
            e.CustomIconUuid = Icon;
        }
        if (it.Contains("prev")) { e.PreviousParentGroup = root.Uuid; g.PreviousParentGroup = root.Uuid; }
        if (force == 0) db.Save(null);
        else
        {
            // KdbxFile.ForceVersion is internal in 2.62; KeePass sets it for its export to KDBX 3.1
            // ("KeePass KDBX (2.34)"), and it is reached the same way here.
            using (var s = File.Create(path))
            {
                var kf = new KdbxFile(db);
                typeof(KdbxFile).GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .SetValue(kf, force, null);
                kf.Save(s, null, KdbxFormat.Default, null);
            }
        }
        db.Close();
    }

    const string All41 = "cdtime,gtags,quality,icon,prev";

    public static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        int n = 0;
        Action<string, KdfParameters, bool, string, uint> b = (name, kdf, chacha, items, force) =>
        { Build(Path.Combine(dir, name + ".kdbx"), kdf, chacha, items, force); n++; };
        b("v31-aes", Aes(), false, "", 0x00030001u);
        b("v40-aes-nocd", Aes(), false, "", 0);
        b("v40-aes-nocd-chacha", Aes(), true, "", 0);
        b("v40-aes", Aes(), false, "cd", 0);
        b("v40-argon2d", Argon(false), false, "cd", 0);
        b("v40-argon2id", Argon(true), false, "cd", 0);
        b("v40-chacha", Argon(false), true, "cd", 0);
        b("v41-argon2d", Argon(false), false, "cd," + All41, 0);
        b("v41-argon2id", Argon(true), false, "cd," + All41, 0);
        b("v41-aes", Aes(), false, "cd," + All41, 0);
        foreach (var item in All41.Split(','))
            b("v41only-" + item + "-aes", Aes(), false, item, item == "prev" ? 0x00040001u : 0);
        b("v41only-prev-argon2d", Argon(false), false, "prev", 0x00040001u);
        b("v41-prev-saved", Aes(), false, "prev", 0);
        Console.WriteLine("formats: " + n.ToString(Inv) + " inputs in " + dir);
    }
    static string Header(string path)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(path)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16).ToString(Inv) + "." + (v & 0xFFFF).ToString(Inv);
    }

    public static void Report(string dir)
    {
        foreach (var f in Directory.GetFiles(dir, "*.kdbx", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            string rel = f.Substring(dir.Length).TrimStart('\\', '/');
            PwDatabase db;
            try { db = Lab.Open(f); }
            catch (Exception ex) { Console.WriteLine(rel + "\tUNREADABLE " + ex.GetType().Name); continue; }
            try
            {
                var u = db.KdfParameters.KdfUuid;
                string kdf = u.Equals(new AesKdf().Uuid) ? "AES-KDF " + db.KdfParameters.GetUInt64(AesKdf.ParamRounds, 0).ToString(Inv)
                    : (u.Equals(new Argon2Kdf(Argon2Type.D).Uuid) ? "Argon2d" : (u.Equals(new Argon2Kdf(Argon2Type.ID).Uuid) ? "Argon2id" : u.ToHexString()))
                      + " it=" + db.KdfParameters.GetUInt64(Argon2Kdf.ParamIterations, 0).ToString(Inv)
                      + " mem=" + (db.KdfParameters.GetUInt64(Argon2Kdf.ParamMemory, 0) / 1024).ToString(Inv) + "KiB";
                var cipher = CipherPool.GlobalPool.GetCipher(db.DataCipherUuid);
                var g = db.RootGroup.FindGroup(GTagged, true);
                var e = db.RootGroup.FindEntry(EQuality, true);
                var timed = db.CustomData.Get("KPG.Timed");
                var t = timed == null || TimeOf == null ? null : (DateTime?)TimeOf.Invoke(db.CustomData, new object[] { "KPG.Timed" });
                var ic = db.CustomIcons.FirstOrDefault(x => x.Uuid.Equals(Icon));
                string items = string.Join(" ", new[]
                {
                    "dbCustomData=" + (db.CustomData.Count == 0 ? "none" : string.Join(",", db.CustomData.Select(kv => kv.Key))),
                    "cdTime=" + (timed == null ? "-" : (t.HasValue ? "kept" : "gone")),
                    "groupTags=" + (g == null ? "-" : (g.Tags.Count > 0 ? "kept" : "gone")),
                    "qualityOff=" + (e == null ? "-" : (!e.QualityCheck ? "kept" : "gone")),
                    "iconName=" + (ic == null ? "-" : (!string.IsNullOrEmpty(ic.Name) ? "kept" : "gone")),
                    "iconTime=" + (ic == null ? "-" : (ic.LastModificationTime.HasValue ? "kept" : "gone")),
                    "entryPrev=" + (e == null ? "-" : (!e.PreviousParentGroup.Equals(PwUuid.Zero) ? "kept" : "gone")),
                    "groupPrev=" + (g == null ? "-" : (!g.PreviousParentGroup.Equals(PwUuid.Zero) ? "kept" : "gone")),
                });
                Console.WriteLine(rel + "\t" + Header(f) + "\t" + kdf + "\t" + (cipher != null ? cipher.DisplayName : "?") + "\t" + db.Compression + "\t" + items);
            }
            finally { db.Close(); }
        }
    }
}
