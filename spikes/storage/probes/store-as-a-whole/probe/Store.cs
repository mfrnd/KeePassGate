// The recommended store over real .kdbx files, changed only by KeePassLib 2.62 (KeePass.exe loaded
// as a library). A copy of the survival critique's probes\crdt\survival\probe\Crdt.cs, extended for
// the family store-as-a-whole: an element carries any KPG. strings the caller gives (the check and
// the identifier are made by the caller, store.py), a dump carries every KPG. string, the user tree
// has a group and an entry that rules can name, and a database can be made under the slow key
// derivation of ground-keepassxc. Invented content, password "test". Console only, no window. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

public static class Store
{
    const string Password = "test";
    public static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string Notes = "KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing.";
    static readonly byte[] Prefix = Encoding.ASCII.GetBytes("KPG/STOR");

    static DateTime At(long s) { return T0.AddSeconds(s); }
    static long Sec(DateTime d) { return (long)Math.Round((d.ToUniversalTime() - T0).TotalSeconds); }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Password)); return k; }
    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveClose(PwDatabase db) { db.Save(null); db.Close(); }

    static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
    static byte[] Unhex(string h) { var b = new byte[h.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16); return b; }
    static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
    static string Unb64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }

    static bool IsStore(PwGroup g) { var u = g.Uuid.UuidBytes; for (int i = 0; i < 8; i++) if (u[i] != Prefix[i]) return false; return true; }
    static PwGroup StoreGroup(PwDatabase db)
    {
        foreach (var g in db.RootGroup.GetGroups(true)) if (IsStore(g)) return g;
        return null;
    }
    static PwGroup Bin(PwDatabase db)
    {
        if (!db.RecycleBinEnabled || db.RecycleBinUuid.Equals(PwUuid.Zero)) return null;
        return db.RootGroup.FindGroup(db.RecycleBinUuid, true);
    }
    static PwGroup Named(PwDatabase db, string name)
    {
        foreach (var g in db.RootGroup.GetGroups(true)) if (g.Name == name) return g;
        return null;
    }
    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    // The slow derivation is ground-keepassxc's (Argon2d, 200 iterations, 64 MiB, 2 lanes), so that
    // the two loads of a keepassxc-cli merge fall in different seconds; the fast one is the
    // critique's (AES-KDF, one round).
    static KdfParameters Kdf(string speed)
    {
        if (speed == "slow")
        {
            var a = new Argon2Kdf().GetDefaultParameters();
            a.SetUInt64(Argon2Kdf.ParamIterations, 200UL);
            a.SetUInt64(Argon2Kdf.ParamMemory, 65536UL * 1024UL);
            a.SetUInt32(Argon2Kdf.ParamParallelism, 2U);
            return a;
        }
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, 1UL);
        return kdf;
    }

    // new <path> <store-suffix-hex16> <fast|slow> <tree-hex16>: a root, a group "General" holding a
    // user entry N, a group "Other", the store group and a recycle bin. The tree's identifiers are
    // fixed by the last argument, so that every copy and every run names the same nodes.
    static string New(string path, string suffix, string speed, string tree)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = Kdf(speed);
        db.RootGroup.Name = "Root";
        db.RootGroup.Uuid = new PwUuid(Unhex(tree.Substring(0, 2) + "00" + tree.Substring(2, 28)));
        Stamp(db.RootGroup, T0);
        var gen = new PwGroup(false, false); gen.Uuid = new PwUuid(Unhex(tree.Substring(0, 2) + "01" + tree.Substring(2, 28)));
        gen.Name = "General"; Stamp(gen, T0); db.RootGroup.AddGroup(gen, true);
        var oth = new PwGroup(false, false); oth.Uuid = new PwUuid(Unhex(tree.Substring(0, 2) + "02" + tree.Substring(2, 28)));
        oth.Name = "Other"; Stamp(oth, T0); db.RootGroup.AddGroup(oth, true);
        var ue = new PwEntry(false, false); ue.Uuid = new PwUuid(Unhex(tree.Substring(0, 2) + "03" + tree.Substring(2, 28)));
        ue.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "N"));
        ue.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password"));
        Stamp(ue, T0); gen.AddEntry(ue, true);
        var st = new PwGroup(false, false);
        var u = new byte[16]; Array.Copy(Prefix, u, 8); Array.Copy(Unhex(suffix), 0, u, 8, 8);
        st.Uuid = new PwUuid(u); st.Name = "KeePassGate"; st.IconId = PwIcon.TerminalEncrypted;
        st.Notes = "KeePassGate data."; st.EnableSearching = false; st.EnableAutoType = false; Stamp(st, T0);
        db.RootGroup.AddGroup(st, true);
        var bin = new PwGroup(true, false); bin.Name = "Recycle Bin"; bin.IconId = PwIcon.TrashBin;
        bin.EnableSearching = false; bin.EnableAutoType = false; Stamp(bin, T0);
        db.RootGroup.AddGroup(bin, true);
        db.RecycleBinEnabled = true; db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = T0;
        db.Save(null); db.Close();
        return "ok";
    }

    // kdf <path> <fast|slow>: the same database under the other derivation.
    static string SetKdf(string path, string speed)
    {
        var db = Open(path); db.KdfParameters = Kdf(speed); SaveClose(db); return "ok";
    }

    // An element: line "A uuid t title name=b64 name=b64 ..."; a name starting with ! is protected.
    static PwEntry Element(string[] p)
    {
        var e = new PwEntry(false, false);
        e.Uuid = new PwUuid(Unhex(p[1]));
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Unb64(p[3])));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Notes));
        for (int i = 4; i < p.Length; i++)
        {
            int k = p[i].IndexOf('=');
            string name = p[i].Substring(0, k);
            bool prot = name.StartsWith("!");
            if (prot) name = name.Substring(1);
            e.Strings.Set(name, new ProtectedString(prot, Unb64(p[i].Substring(k + 1))));
        }
        e.AutoType.Enabled = false;
        Stamp(e, At(long.Parse(p[2])));
        return e;
    }

    // patch <path> <spec>: lines "A ...", "R uuid" (removed, no record), "D uuid t" (a record).
    static string Patch(string path, string spec)
    {
        var db = Open(path);
        var st = StoreGroup(db);
        foreach (var line in File.ReadAllLines(spec))
        {
            if (line.Length == 0) continue;
            var p = line.Split('\t');
            if (p[0] == "A")
            {
                var old = db.RootGroup.FindEntry(new PwUuid(Unhex(p[1])), true);
                if (old != null) old.ParentGroup.Entries.Remove(old);
                st.AddEntry(Element(p), true);
            }
            else if (p[0] == "R")
            {
                var e = db.RootGroup.FindEntry(new PwUuid(Unhex(p[1])), true);
                if (e != null) e.ParentGroup.Entries.Remove(e);
            }
            else if (p[0] == "D")
            {
                var u = new PwUuid(Unhex(p[1]));
                var ex = db.DeletedObjects.FirstOrDefault(o => o.Uuid.Equals(u));
                if (ex != null) ex.DeletionTime = At(long.Parse(p[2]));
                else db.DeletedObjects.Add(new PwDeletedObject(u, At(long.Parse(p[2]))));
            }
            else throw new InvalidOperationException(line);
        }
        SaveClose(db);
        return "ok";
    }

    static string Merge(string target, string source, string method, string output)
    {
        var t = Open(target); var s = Open(source);
        t.MergeIn(s, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), method), null);
        t.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        t.Close(); s.Close();
        return "ok";
    }

    // bin <path> <uuid> <t>: KeePass's Delete of an entry outside the bin: into the recycle bin,
    // LocationChanged moves, LastModificationTime does not. The same for a group.
    static string BinIt(string path, string uuid, long t)
    {
        var db = Open(path);
        var b = Bin(db);
        var u = new PwUuid(Unhex(uuid));
        var e = db.RootGroup.FindEntry(u, true);
        if (e != null && b != null)
        {
            var from = e.ParentGroup; from.Entries.Remove(e); b.AddEntry(e, true, true);
            e.PreviousParentGroup = from.Uuid; e.LocationChanged = At(t);
            SaveClose(db); return "ok";
        }
        var g = db.RootGroup.FindGroup(u, true);
        if (g != null && b != null)
        {
            var from = g.ParentGroup; from.Groups.Remove(g); b.AddGroup(g, true, true);
            g.PreviousParentGroup = from.Uuid; g.LocationChanged = At(t);
            SaveClose(db); return "ok";
        }
        db.Close(); return "absent";
    }

    // move <path> <uuid> <group name> <t>: an entry moved into another group, as a drag does.
    static string Move(string path, string uuid, string group, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        var g = group == "Root" ? db.RootGroup : Named(db, group);
        if (e == null || g == null) { db.Close(); return "absent"; }
        var from = e.ParentGroup; from.Entries.Remove(e); g.AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid; e.LocationChanged = At(t);
        SaveClose(db);
        return "ok";
    }

    // dup <path> <uuid> <newuuid> <t>: KeePass's Duplicate Entry: a copy under another identifier
    // beside the original, every string kept.
    static string Dup(string path, string uuid, string nu, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        if (e == null) { db.Close(); return "absent"; }
        var c = e.Duplicate(); c.Uuid = new PwUuid(Unhex(nu)); Stamp(c, At(t));
        e.ParentGroup.AddEntry(c, true, true);
        SaveClose(db);
        return "ok";
    }

    static string Touch(string path, string uuid, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        if (e == null) { db.Close(); return "absent"; }
        e.CreateBackup(db); e.LastModificationTime = At(t); e.LastAccessTime = At(t);
        SaveClose(db);
        return "ok";
    }

    static string Edit(string path, string uuid, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        if (e == null) { db.Close(); return "absent"; }
        e.CreateBackup(db);
        e.Strings.Set("KPG.Data", new ProtectedString(e.Strings.GetSafe("KPG.Data").IsProtected, e.Strings.ReadSafe("KPG.Data") + " "));
        e.LastModificationTime = At(t);
        SaveClose(db);
        return "ok";
    }

    // delrec <path> <uuid> <t>: a permanent delete of an entry or a group, with a record at t.
    static string DelRec(string path, string uuid, long t)
    {
        var db = Open(path);
        var u = new PwUuid(Unhex(uuid));
        var e = db.RootGroup.FindEntry(u, true);
        if (e != null) { e.ParentGroup.Entries.Remove(e); db.DeletedObjects.Add(new PwDeletedObject(u, At(t))); SaveClose(db); return "ok"; }
        var g = db.RootGroup.FindGroup(u, true);
        if (g != null)
        {
            foreach (var x in g.GetEntries(true).ToList()) db.DeletedObjects.Add(new PwDeletedObject(x.Uuid, At(t)));
            foreach (var x in g.GetGroups(true).ToList()) db.DeletedObjects.Add(new PwDeletedObject(x.Uuid, At(t)));
            g.ParentGroup.Groups.Remove(g); db.DeletedObjects.Add(new PwDeletedObject(u, At(t)));
            SaveClose(db); return "ok";
        }
        db.Close(); return "absent";
    }

    // restore <path> <uuid> <t>: an entry taken out of the recycle bin into the group it came from.
    static string Restore(string path, string uuid, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        if (e == null) { db.Close(); return "absent"; }
        var g = db.RootGroup.FindGroup(e.PreviousParentGroup, true) ?? db.RootGroup;
        var from = e.ParentGroup; from.Entries.Remove(e); g.AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid; e.LocationChanged = At(t);
        SaveClose(db);
        return "ok";
    }

    static string Empty(string path, long t)
    {
        var db = Open(path);
        var b = Bin(db);
        int n = 0;
        foreach (var e in b.GetEntries(true).ToList()) { e.ParentGroup.Entries.Remove(e); db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(t))); n++; }
        foreach (var g in b.GetGroups(false).ToList()) { b.Groups.Remove(g); db.DeletedObjects.Add(new PwDeletedObject(g.Uuid, At(t))); n++; }
        SaveClose(db);
        return "ok " + n;
    }

    static string Clear(string path) { var db = Open(path); db.DeletedObjects.Clear(); SaveClose(db); return "ok"; }

    static string Dedupe(string path)
    {
        var db = Open(path);
        uint n = db.DeleteDuplicateEntries(null);
        SaveClose(db);
        return "ok " + n;
    }

    // deg <path>: Delete Empty Groups, as the command does it (each with a record).
    static string Deg(string path)
    {
        var db = Open(path);
        uint n = db.DeleteEmptyGroups();
        SaveClose(db);
        return "ok " + n;
    }

    // dupgroup <path> <suffix 0|1> <t>: Duplicate Group of the plugin's group: a second group under
    // a new identifier beside it, every entry copied under a new identifier, " - Copy" appended to
    // each title when asked, as the Duplication Options do; searching and auto-type kept.
    static string DupGroup(string path, string suffix, long t)
    {
        var db = Open(path);
        var st = StoreGroup(db);
        var g = new PwGroup(true, false); g.Name = st.Name; g.IconId = st.IconId; g.Notes = st.Notes;
        g.EnableSearching = st.EnableSearching; g.EnableAutoType = st.EnableAutoType; Stamp(g, At(t));
        st.ParentGroup.AddGroup(g, true);
        foreach (var e in st.GetEntries(false).ToList())
        {
            var c = e.Duplicate();
            if (suffix == "1") c.Strings.Set(PwDefs.TitleField, new ProtectedString(false, e.Strings.ReadSafe(PwDefs.TitleField) + " - Copy"));
            Stamp(c, At(t));
            g.AddEntry(c, true, true);
        }
        SaveClose(db);
        return "ok";
    }

    static string Place(PwDatabase db, PwGroup g)
    {
        var b = Bin(db);
        for (var p = g; p != null; p = p.ParentGroup) if (b != null && p == b) return p == g ? "B" : "B+";
        if (IsStore(g)) return "S";
        return "O:" + g.Name;
    }

    // dump <path>: the bin; every group with its parent; every entry with a KPG. string or the
    // title N, with its times, place, parent and every KPG. string; every deletion record.
    static string Dump(string path)
    {
        var db = Open(path);
        var sb = new StringBuilder();
        var b = Bin(db);
        sb.Append("R\t" + (b == null ? "-" : Hex(b.Uuid.UuidBytes)) + "\t" + Hex(db.RootGroup.Uuid.UuidBytes) + "\n");
        foreach (var g in db.RootGroup.GetGroups(true))
            sb.Append("G\t" + Hex(g.Uuid.UuidBytes) + "\t" + Hex(g.ParentGroup.Uuid.UuidBytes) + "\t" + B64(g.Name) + "\t" + (IsStore(g) ? "1" : "0") + "\n");
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            var names = e.Strings.GetKeys().Where(k => k.StartsWith("KPG.")).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var title = e.Strings.ReadSafe(PwDefs.TitleField);
            var line = new StringBuilder();
            line.Append("E\t" + Hex(e.Uuid.UuidBytes) + "\t" + Sec(e.LastModificationTime) + "\t" + Sec(e.LocationChanged) + "\t"
                + Place(db, e.ParentGroup) + "\t" + Hex(e.ParentGroup.Uuid.UuidBytes) + "\t" + B64(title) + "\t" + e.History.UCount);
            foreach (var k in names) line.Append("\t" + k + "=" + B64(e.Strings.ReadSafe(k)));
            sb.Append(line.ToString() + "\n");
        }
        foreach (var o in db.DeletedObjects) sb.Append("D\t" + Hex(o.Uuid.UuidBytes) + "\t" + Sec(o.DeletionTime) + "\n");
        db.Close();
        return sb.ToString().TrimEnd('\n');
    }

    public static string Run(string line)
    {
        var p = line.Split('\t');
        switch (p[0])
        {
            case "new": return New(p[1], p[2], p[3], p[4]);
            case "kdf": return SetKdf(p[1], p[2]);
            case "patch": return Patch(p[1], p[2]);
            case "merge": return Merge(p[1], p[2], p[3], p[4]);
            case "bin": return BinIt(p[1], p[2], long.Parse(p[3]));
            case "move": return Move(p[1], p[2], p[3], long.Parse(p[4]));
            case "dup": return Dup(p[1], p[2], p[3], long.Parse(p[4]));
            case "touch": return Touch(p[1], p[2], long.Parse(p[3]));
            case "edit": return Edit(p[1], p[2], long.Parse(p[3]));
            case "delrec": return DelRec(p[1], p[2], long.Parse(p[3]));
            case "restore": return Restore(p[1], p[2], long.Parse(p[3]));
            case "empty": return Empty(p[1], long.Parse(p[2]));
            case "clear": return Clear(p[1]);
            case "dedupe": return Dedupe(p[1]);
            case "deg": return Deg(p[1]);
            case "dupgroup": return DupGroup(p[1], p[2], long.Parse(p[3]));
            case "dump": return Dump(p[1]);
            default: throw new InvalidOperationException(line);
        }
    }
}
