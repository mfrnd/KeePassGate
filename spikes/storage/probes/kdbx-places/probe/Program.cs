// The KeePassLib half of `kdbx-places`: makes the databases, with a recognizable value in every
// place KeePassLib 2.62 can write, and runs KeePass's own operations on them. The places
// KeePassLib cannot write are added afterwards by `scripts\places.py inject`, and everything is
// read back by `scripts\places.py dump`, which reads the file itself rather than through any
// program. Throwaway databases only; the password of every one is `probe-only`.
//
//   make <dir> <aes|argon|slow|phone> <41|40>
//                                           base, A-diff, B-diff, A-tie, B-tie
//   resave <in> <out>                       open and save, nothing changed
//   edit <in> <out>                         open, add an unrelated entry at the root, save
//   merge <open> <arriving> <out> <method>  MergeIn with a PwMergeMethod, then save the open copy
//   rekey <in> <out>                        a new master key (`probe-other`), then save
//   kdf <in> <out>                          the other key derivation (AES-KDF and Argon2d swap)
//   save-as <in> <out>                       SaveAs to another file, as File then Save As does
//   down40 <in> <out>                       remove every 4.1 feature, then save
//   cap <out> <place> <bytes> <aes|argon>   a base with a value of that size in that place
//   see <file>                              what KeePassLib exposes of the header places
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

static class Program
{
    const string Password = "probe-only";
    const string OtherPassword = "probe-other";

    // Fixed identifiers, so that every copy names the same objects; `places.py` labels them.
    static PwUuid U(int n) { return new PwUuid(Guid.Parse("4b50472d-0000-4000-8000-" + n.ToString("x12")).ToByteArray()); }
    static readonly PwUuid GroupPlaces = U(1), EntryPlaces = U(2), GroupBin = U(3), GroupBin2 = U(4),
        GroupTpl = U(5), GroupTpl2 = U(6), GroupPrev = U(7), EntryOther = U(8), GroupRoot = U(9),
        IconUsed = U(0x11), IconUnused = U(0x12), IconOnlyA = U(0x13), IconOnlyB = U(0x14),
        DelShared = U(0x21), DelOnlyA = U(0x22), DelOnlyB = U(0x23);

    static readonly DateTime TBase = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TOlder = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TNewer = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    static int Main(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "make": Make(args[1], args[2], args[3] == "41"); break;
                case "resave": Resave(args[1], args[2], null); break;
                case "edit": Resave(args[1], args[2], db => AddUnrelated(db)); break;
                case "merge": Merge(args[1], args[2], args[3], args[4]); break;
                case "rekey": Resave(args[1], args[2], db => { db.MasterKey = Key(OtherPassword); db.MasterKeyChanged = DateTime.UtcNow; }); break;
                case "kdf": Resave(args[1], args[2], db => db.KdfParameters = Kdf(db.KdfParameters.KdfUuid.Equals(new AesKdf().Uuid) ? "argon" : "aes", db.KdfParameters)); break;
                case "save-as": SaveAs(args[1], args[2]); break;
                case "down40": Resave(args[1], args[2], db => Down40(db)); break;
                case "cap": Cap(args[1], args[2], int.Parse(args[3]), args[4]); break;
                case "see": See(args[1]); break;
                default: Console.Error.WriteLine("unknown command " + args[0]); return 2;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    static CompositeKey Key(string password)
    {
        var k = new CompositeKey();
        k.AddUserKey(new KcpPassword(password));
        return k;
    }

    // Copies over every key of `keep` that is not the KDF's own, so that a KDF change shows
    // whether a key of the plugin's own survives it.
    static KdfParameters Kdf(string name, VariantDictionary keep = null)
    {
        KdfParameters p;
        if (name == "aes")
        {
            p = new AesKdf().GetDefaultParameters();
            p.SetUInt64(AesKdf.ParamRounds, 60000UL);
        }
        else
        {
            p = new Argon2Kdf().GetDefaultParameters();
            bool slow = name == "slow";
            // KeePassDX refuses Argon2 memory below 8 MiB (read), so the phone's files take 16 MiB.
            bool phone = name == "phone";
            p.SetUInt64(Argon2Kdf.ParamIterations, slow ? 200UL : 2UL);
            p.SetUInt64(Argon2Kdf.ParamMemory, (slow ? 65536UL : phone ? 16384UL : 1024UL) * 1024UL);
            p.SetUInt32(Argon2Kdf.ParamParallelism, slow ? 2U : 1U);
        }
        if (keep != null)
        {
            foreach (string n in new[] { "KPG_kdf", "KPG_kdf_onlyA", "KPG_kdf_onlyB" })
            {
                string v = keep.GetString(n);
                if (v != null) p.SetString(n, v);
            }
        }
        return p;
    }

    static readonly MethodInfo SetWithTime = typeof(StringDictionaryEx).GetMethod("Set",
        BindingFlags.NonPublic | BindingFlags.Instance, null,
        new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);

    static void SetCd(StringDictionaryEx d, string key, string value, DateTime? t)
    {
        SetWithTime.Invoke(d, new object[] { key, value, t });
    }

    static byte[] Png(Color c)
    {
        using (var bmp = new Bitmap(16, 16))
        {
            using (var g = Graphics.FromImage(bmp)) g.Clear(c);
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static string V(string place, string variant) { return "kpg:" + place + ":" + variant; }

    // The numbers a place of that kind takes in each variant: base, A, B.
    static int N(int b, string variant) { return variant == "A" ? b + 1 : variant == "B" ? b + 2 : b; }

    static void Make(string dir, string kdf, bool v41)
    {
        Directory.CreateDirectory(dir);
        Build(Path.Combine(dir, "base.kdbx"), kdf, v41, "base", TBase);
        Build(Path.Combine(dir, "A-diff.kdbx"), kdf, v41, "A", TOlder);
        Build(Path.Combine(dir, "B-diff.kdbx"), kdf, v41, "B", TNewer);
        Build(Path.Combine(dir, "A-tie.kdbx"), kdf, v41, "A", TOlder);
        Build(Path.Combine(dir, "B-tie.kdbx"), kdf, v41, "B", TOlder);
        Console.WriteLine("made " + kdf + (v41 ? " 4.1" : " 4.0") + " set");
    }

    static PwGroup NewGroup(PwUuid u, string name)
    {
        var g = new PwGroup(false, true) { Uuid = u, Name = name };
        return g;
    }

    static void StampTimes(ITimeLogger o, DateTime t)
    {
        o.CreationTime = TBase;
        o.LastModificationTime = t;
        o.LastAccessTime = t;
        o.LocationChanged = t;
    }

    static PwDatabase Build(string path, string kdf, bool v41, string v, DateTime t, Action<PwDatabase> extra = null)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key(Password));
        db.KdfParameters = Kdf(kdf);
        // A key of the plugin's own in the KDF parameters, written with them.
        db.KdfParameters.SetString("KPG_kdf", V("H.kdf", v));
        if (v == "A") db.KdfParameters.SetString("KPG_kdf_onlyA", V("H.kdf.onlyA", v));
        if (v == "B") db.KdfParameters.SetString("KPG_kdf_onlyB", V("H.kdf.onlyB", v));

        // The header's own public custom data.
        db.PublicCustomData.SetString("KPG_shared", V("H.pcd", v));
        db.PublicCustomData.SetUInt64("KPG_counter", (ulong)N(100, v));
        db.PublicCustomData.SetByteArray("KPG_bytes", Encoding.UTF8.GetBytes(V("H.pcd.bytes", v)));
        if (v == "A") db.PublicCustomData.SetString("KPG_onlyA", V("H.pcd.onlyA", v));
        if (v == "B") db.PublicCustomData.SetString("KPG_onlyB", V("H.pcd.onlyB", v));

        // Meta.
        db.Name = V("M.name", v); db.NameChanged = t;
        db.Description = V("M.desc", v); db.DescriptionChanged = t;
        db.DefaultUserName = V("M.user", v); db.DefaultUserNameChanged = t;
        db.MaintenanceHistoryDays = (uint)N(301, v);
        db.Color = v == "A" ? Color.FromArgb(0x11, 0x22, 0x33) : v == "B" ? Color.FromArgb(0x44, 0x55, 0x66) : Color.FromArgb(0x10, 0x20, 0x30);
        db.MasterKeyChanged = t;
        db.MasterKeyChangeRec = N(11, v);
        db.MasterKeyChangeForce = N(21, v);
        db.MasterKeyChangeForceOnce = v != "B";
        db.MemoryProtection.ProtectNotes = v == "base";
        db.MemoryProtection.ProtectUrl = v == "A";
        db.MemoryProtection.ProtectTitle = v == "B";
        db.HistoryMaxItems = N(31, v);
        db.HistoryMaxSize = N(4000001, v);

        var iconColor = v == "A" ? Color.Red : v == "B" ? Color.Blue : Color.Green;
        var icons = new List<KeyValuePair<PwUuid, string>> { new KeyValuePair<PwUuid, string>(IconUsed, "M.icon.used"), new KeyValuePair<PwUuid, string>(IconUnused, "M.icon.unused") };
        if (v == "A") icons.Add(new KeyValuePair<PwUuid, string>(IconOnlyA, "M.icon.onlyA"));
        if (v == "B") icons.Add(new KeyValuePair<PwUuid, string>(IconOnlyB, "M.icon.onlyB"));
        foreach (var kv in icons)
        {
            var ci = new PwCustomIcon(kv.Key, Png(iconColor));
            if (v41) { ci.Name = V(kv.Value + ".name", v); ci.LastModificationTime = t; }
            db.CustomIcons.Add(ci);
        }

        SetCd(db.CustomData, "kpg-shared", V("M.cd", v), v41 ? (DateTime?)t : null);
        if (v == "A") SetCd(db.CustomData, "kpg-onlyA", V("M.cd.onlyA", v), v41 ? (DateTime?)t : null);
        if (v == "B") SetCd(db.CustomData, "kpg-onlyB", V("M.cd.onlyB", v), v41 ? (DateTime?)t : null);

        // The tree. The root keeps its own name, so that only the places vary.
        PwGroup root = db.RootGroup;
        root.Uuid = GroupRoot;
        root.Name = "kpg-root";
        var g = NewGroup(GroupPlaces, V("G.name", v));
        var bin = NewGroup(GroupBin, "kpg-bin");
        var bin2 = NewGroup(GroupBin2, "kpg-bin2");
        var tpl = NewGroup(GroupTpl, "kpg-tpl");
        var tpl2 = NewGroup(GroupTpl2, "kpg-tpl2");
        var prev = NewGroup(GroupPrev, "kpg-prev");
        foreach (var x in new[] { g, bin, bin2, tpl, tpl2, prev }) root.AddGroup(x, true);

        db.RecycleBinEnabled = v != "B";
        db.RecycleBinUuid = v == "B" ? GroupBin2 : GroupBin; db.RecycleBinChanged = t;
        db.EntryTemplatesGroup = v == "B" ? GroupTpl2 : GroupTpl; db.EntryTemplatesGroupChanged = t;
        db.LastSelectedGroup = v == "A" ? GroupBin : v == "B" ? GroupTpl : GroupPlaces;
        db.LastTopVisibleGroup = v == "A" ? GroupTpl : v == "B" ? GroupBin : GroupPrev;

        g.Notes = V("G.notes", v);
        g.IconId = (PwIcon)N(41, v);
        g.CustomIconUuid = IconUsed;
        g.IsExpanded = v != "A";
        g.DefaultAutoTypeSequence = V("G.at-sequence", v);
        g.EnableAutoType = v == "base" ? true : v == "A" ? (bool?)false : null;
        g.EnableSearching = v == "base" ? false : v == "A" ? (bool?)true : null;
        g.LastTopVisibleEntry = v == "B" ? EntryOther : EntryPlaces;
        g.PreviousParentGroup = v == "A" ? GroupBin : v == "B" ? GroupTpl : GroupPrev;
        if (v41) g.Tags.Add(V("G.tags", v));
        g.CustomData.Set("kpg-shared", V("G.cd", v));
        if (v == "A") g.CustomData.Set("kpg-onlyA", V("G.cd.onlyA", v));
        if (v == "B") g.CustomData.Set("kpg-onlyB", V("G.cd.onlyB", v));

        var e = new PwEntry(false, true) { Uuid = EntryPlaces };
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "kpg-entry-before"));
        e.IconId = (PwIcon)N(51, v);
        e.CustomIconUuid = IconUsed;
        e.ForegroundColor = v == "A" ? Color.FromArgb(1, 2, 3) : v == "B" ? Color.FromArgb(4, 5, 6) : Color.FromArgb(7, 8, 9);
        e.BackgroundColor = v == "A" ? Color.FromArgb(11, 12, 13) : v == "B" ? Color.FromArgb(14, 15, 16) : Color.FromArgb(17, 18, 19);
        e.OverrideUrl = V("E.override-url", v);
        e.QualityCheck = v41 ? v == "B" : true;
        e.Tags.Add(V("E.tags", v));
        e.PreviousParentGroup = v == "A" ? GroupBin : v == "B" ? GroupTpl : GroupPrev;
        e.Expires = true;
        e.ExpiryTime = new DateTime(2030 + N(1, v), 1, 1, 0, 0, 0, DateTimeKind.Utc);
        e.UsageCount = (ulong)N(71, v);
        g.AddEntry(e, true);
        // A history item older than the base, the same in every copy.
        StampTimes(e, TBase.AddDays(-1));
        e.CreateBackup(null);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, V("E.title", v)));
        e.Strings.Set("kpg-plain", new ProtectedString(false, V("E.string", v)));
        e.Strings.Set("kpg-protected", new ProtectedString(true, V("E.protected", v)));
        e.Binaries.Set("kpg-att.txt", new ProtectedBinary(false, Encoding.UTF8.GetBytes(V("E.binary", v))));
        e.AutoType.Enabled = v != "A";
        e.AutoType.ObfuscationOptions = v == "A" ? AutoTypeObfuscationOptions.UseClipboard : AutoTypeObfuscationOptions.None;
        e.AutoType.DefaultSequence = V("E.at-sequence", v);
        e.AutoType.Add(new AutoTypeAssociation(V("E.at-window", v), V("E.at-keys", v)));
        e.CustomData.Set("kpg-shared", V("E.cd", v));
        if (v == "A") e.CustomData.Set("kpg-onlyA", V("E.cd.onlyA", v));
        if (v == "B") e.CustomData.Set("kpg-onlyB", V("E.cd.onlyB", v));

        db.DeletedObjects.Add(new PwDeletedObject(DelShared, t));
        if (v == "A") db.DeletedObjects.Add(new PwDeletedObject(DelOnlyA, t));
        if (v == "B") db.DeletedObjects.Add(new PwDeletedObject(DelOnlyB, t));

        if (extra != null) extra(db);

        // Times last, since adding and editing stamp them.
        foreach (var x in new PwGroup[] { root, bin, bin2, tpl, tpl2, prev }) { StampTimes(x, TBase); x.ExpiryTime = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc); }
        StampTimes(g, t);
        g.Expires = true;
        g.ExpiryTime = new DateTime(2030 + N(1, v), 6, 1, 0, 0, 0, DateTimeKind.Utc);
        g.UsageCount = (ulong)N(61, v);
        StampTimes(e, t);
        db.SettingsChanged = t;

        db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        db.Close();
        return db;
    }

    static PwDatabase Open(string path, string password = Password)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(password), null);
        return db;
    }

    static PwDatabase OpenAny(string path)
    {
        try { return Open(path); }
        catch (InvalidCompositeKeyException) { return Open(path, OtherPassword); }
    }

    static void AddUnrelated(PwDatabase db)
    {
        var e = new PwEntry(true, true);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "kpl-added"));
        db.RootGroup.AddEntry(e, true);
    }

    static void Resave(string input, string output, Action<PwDatabase> change)
    {
        File.Copy(input, output, true);
        var db = OpenAny(output);
        if (change != null) change(db);
        db.Save(null);
        db.Close();
        Console.WriteLine("saved " + Path.GetFileName(output));
    }

    static void SaveAs(string input, string output)
    {
        var db = OpenAny(input);
        db.SaveAs(IOConnectionInfo.FromPath(output), true, null);
        db.Close();
        Console.WriteLine("saved as " + Path.GetFileName(output));
    }

    static void Merge(string open, string arriving, string output, string method)
    {
        File.Copy(open, output, true);
        var db = Open(output);
        var src = Open(arriving);
        db.MergeIn(src, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), method));
        db.Save(null);
        db.Close();
        src.Close();
        Console.WriteLine("merged " + method + " " + Path.GetFileName(output));
    }

    static void Down40(PwDatabase db)
    {
        db.RootGroup.TraverseTree(TraversalMethod.PreOrder, pg => { pg.Tags.Clear(); return true; },
            pe => { pe.QualityCheck = true; foreach (PwEntry h in pe.History) h.QualityCheck = true; return true; });
        db.RootGroup.Tags.Clear();
        foreach (PwCustomIcon ci in db.CustomIcons) { ci.Name = string.Empty; ci.LastModificationTime = null; }
        var keys = new List<string>();
        foreach (var kv in db.CustomData) keys.Add(kv.Key);
        foreach (string k in keys) SetCd(db.CustomData, k, db.CustomData.Get(k), null);
    }

    // A value of `bytes` characters in one place, the rest as the 4.1 base.
    static void Cap(string output, string place, int bytes, string kdf)
    {
        // Letters and digits drawn from a fixed seed, so that compression does not make a large
        // value small in the file, and two runs write the same value.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random(4242);
        var chars = new char[bytes];
        for (int i = 0; i < bytes; ++i) chars[i] = alphabet[random.Next(alphabet.Length)];
        string big = new string(chars);
        Build(output, kdf, true, "base", TBase, db =>
        {
            PwGroup g = db.RootGroup.FindGroup(GroupPlaces, true);
            PwEntry e = db.RootGroup.FindEntry(EntryPlaces, true);
            switch (place)
            {
                case "H.pcd": db.PublicCustomData.SetString("KPG_shared", big); break;
                case "H.kdf": db.KdfParameters.SetString("KPG_kdf", big); break;
                case "M.name": db.Name = big; break;
                case "M.desc": db.Description = big; break;
                case "M.user": db.DefaultUserName = big; break;
                case "M.cd": SetCd(db.CustomData, "kpg-shared", big, TBase); break;
                case "M.icon.name": db.CustomIcons[0].Name = big; break;
                case "M.icon.data":
                    {
                        // A PNG made larger by a text chunk of that size, so that it stays an image.
                        byte[] png = Png(Color.Green);
                        db.CustomIcons[1] = new PwCustomIcon(IconUnused, PngWithText(png, big));
                        db.CustomIcons[1].Name = "kpg-big";
                        db.CustomIcons[1].LastModificationTime = TBase;
                        break;
                    }
                case "G.notes": g.Notes = big; break;
                case "G.cd": g.CustomData.Set("kpg-shared", big); break;
                case "G.at-sequence": g.DefaultAutoTypeSequence = big; break;
                case "G.tags": g.Tags.Clear(); g.Tags.Add(big); break;
                case "E.override-url": e.OverrideUrl = big; break;
                case "E.tags": e.Tags.Clear(); e.Tags.Add(big); break;
                case "E.at-window": e.AutoType.Clear(); e.AutoType.Add(new AutoTypeAssociation(big, "{TAB}")); break;
                case "E.cd": e.CustomData.Set("kpg-shared", big); break;
                default: throw new ArgumentException("no capacity place " + place);
            }
        });
        Console.WriteLine("cap " + place + " " + bytes);
    }

    static byte[] PngWithText(byte[] png, string body)
    {
        // Insert a text chunk after the header chunk: 8 signature bytes, then 25 bytes of header.
        byte[] keyword = Encoding.ASCII.GetBytes("Comment\0");
        byte[] value = Encoding.ASCII.GetBytes(body);
        byte[] text = new byte[keyword.Length + value.Length];
        Buffer.BlockCopy(keyword, 0, text, 0, keyword.Length);
        Buffer.BlockCopy(value, 0, text, keyword.Length, value.Length);
        byte[] type = Encoding.ASCII.GetBytes("tEXt");
        var crcInput = new byte[4 + text.Length];
        Buffer.BlockCopy(type, 0, crcInput, 0, 4);
        Buffer.BlockCopy(text, 0, crcInput, 4, text.Length);
        uint crc = Crc32(crcInput);
        using (var ms = new MemoryStream())
        {
            ms.Write(png, 0, 33);
            ms.Write(BE(text.Length), 0, 4);
            ms.Write(crcInput, 0, crcInput.Length);
            ms.Write(BE((int)crc), 0, 4);
            ms.Write(png, 33, png.Length - 33);
            return ms.ToArray();
        }
    }

    static byte[] BE(int v) { return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }

    static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; ++k) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }

    // What KeePassLib hands a plugin of the header places, after its own load.
    static void See(string path)
    {
        var db = OpenAny(path);
        var lines = new List<string>();
        foreach (string n in new[] { "KPG_shared", "KPG_onlyA", "KPG_onlyB" })
            lines.Add("kpl.pcd[" + n + "]\t" + (db.PublicCustomData.GetString(n) ?? "(absent)"));
        lines.Add("kpl.pcd[KPG_counter]\t" + db.PublicCustomData.GetUInt64("KPG_counter", 0));
        foreach (string n in new[] { "KPG_kdf", "KPG_kdf_onlyA", "KPG_kdf_onlyB" })
            lines.Add("kpl.kdf[" + n + "]\t" + (db.KdfParameters.GetString(n) ?? "(absent)"));
        lines.Add("kpl.memprot\tTitle=" + db.MemoryProtection.ProtectTitle + " URL=" + db.MemoryProtection.ProtectUrl + " Notes=" + db.MemoryProtection.ProtectNotes);
        lines.Add("kpl.icons\t" + db.CustomIcons.Count);
        foreach (string l in lines) Console.WriteLine(l);
        db.Close();
    }
}
