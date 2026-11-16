// Transport probe for option `crdt`: real .kdbx files in the design's shape, changed only by
// KeePassLib 2.62 (KeePass.exe loaded as a library). Invented content, password "test", AES-KDF one
// round. Console only, no window. C# 5 (CodeDom). One command per call of Run; the host loops.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

public static class Crdt
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
    static string Mac(string kind, string data) { using (var s = SHA256.Create()) return Hex(s.ComputeHash(Encoding.UTF8.GetBytes(kind + "\n" + data))); }

    static bool IsStore(PwGroup g) { var u = g.Uuid.UuidBytes; for (int i = 0; i < 8; i++) if (u[i] != Prefix[i]) return false; return true; }
    static PwGroup Store(PwDatabase db)
    {
        foreach (var g in db.RootGroup.GetGroups(true)) if (IsStore(g)) return g;
        return null;
    }
    static PwGroup Bin(PwDatabase db)
    {
        if (!db.RecycleBinEnabled || db.RecycleBinUuid.Equals(PwUuid.Zero)) return null;
        return db.RootGroup.FindGroup(db.RecycleBinUuid, true);
    }
    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    // new <path> <store-suffix-hex16> : a database with a user group, the store group and a recycle bin.
    static string New(string path, string suffix)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, 1UL);
        db.KdfParameters = kdf;
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        var gen = new PwGroup(true, false); gen.Name = "General"; Stamp(gen, T0); db.RootGroup.AddGroup(gen, true);
        var ue = new PwEntry(true, false);
        ue.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "user entry"));
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

    static PwEntry Element(string uuid, long t, string kind, string id, string data, string extra)
    {
        var e = new PwEntry(false, false);
        e.Uuid = new PwUuid(Unhex(uuid));
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "KPG " + kind + " " + id));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, Notes));
        e.Strings.Set("KPG.Kind", new ProtectedString(false, kind));
        e.Strings.Set("KPG.Data", new ProtectedString(kind == "auth", data));
        e.Strings.Set("KPG.Mac", new ProtectedString(false, Mac(kind, data)));
        e.Strings.Set("KPG.Id", new ProtectedString(false, id));
        if (!string.IsNullOrEmpty(extra))
        {
            int k = 0;
            foreach (var s in extra.Split(';')) { k++; e.Strings.Set("KPG.S." + k.ToString("0000"), new ProtectedString(false, s)); }
        }
        e.AutoType.Enabled = false;
        Stamp(e, At(t));
        return e;
    }

    // patch <path> <spec>: lines "A uuid t kind id data extra", "R uuid", "D uuid t" (tab separated)
    static string Patch(string path, string spec)
    {
        var db = Open(path);
        var st = Store(db);
        foreach (var line in File.ReadAllLines(spec))
        {
            if (line.Length == 0) continue;
            var p = line.Split('\t');
            if (p[0] == "A") st.AddEntry(Element(p[1], long.Parse(p[2]), p[3], p[4], p[5], p.Length > 6 ? p[6] : ""), true);
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

    // bin <path> <uuid> <t>: what KeePass, Keepass2Android and KeePassDroid do on Delete: the entry
    // goes into the recycle bin, LocationChanged moves, LastModificationTime does not.
    static string BinIt(string path, string uuid, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        var b = Bin(db);
        if (e == null || b == null) { db.Close(); return "absent"; }
        var from = e.ParentGroup; from.Entries.Remove(e); b.AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid; e.LocationChanged = At(t);
        SaveClose(db);
        return "ok";
    }

    // touch <path> <uuid> <t>: KeePassDroid's editor Save with nothing changed: a backup into
    // history and a new modification time, every field as it was.
    static string Touch(string path, string uuid, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        if (e == null) { db.Close(); return "absent"; }
        e.CreateBackup(db); e.LastModificationTime = At(t); e.LastAccessTime = At(t);
        SaveClose(db);
        return "ok";
    }

    // edit <path> <uuid> <t>: a change to KPG.Data, as a hand edit in the entry dialog.
    static string Edit(string path, string uuid, long t)
    {
        var db = Open(path);
        var e = db.RootGroup.FindEntry(new PwUuid(Unhex(uuid)), true);
        if (e == null) { db.Close(); return "absent"; }
        e.CreateBackup(db);
        e.Strings.Set("KPG.Data", new ProtectedString(false, e.Strings.ReadSafe("KPG.Data") + " "));
        e.LastModificationTime = At(t);
        SaveClose(db);
        return "ok";
    }

    // delrec <path> <uuid> <t>: a permanent delete: removed, with a deletion record at t.
    static string DelRec(string path, string uuid, long t)
    {
        var db = Open(path);
        var u = new PwUuid(Unhex(uuid));
        var e = db.RootGroup.FindEntry(u, true);
        if (e == null) { db.Close(); return "absent"; }
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(u, At(t)));
        SaveClose(db);
        return "ok";
    }

    // empty <path> <t>: Empty Recycle Bin: everything in the bin removed, each with a record.
    static string Empty(string path, long t)
    {
        var db = Open(path);
        var b = Bin(db);
        int n = 0;
        foreach (var e in b.GetEntries(true).ToList()) { e.ParentGroup.Entries.Remove(e); db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(t))); n++; }
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

    static string Place(PwDatabase db, PwGroup g)
    {
        var b = Bin(db);
        for (var p = g; p != null; p = p.ParentGroup) if (b != null && p == b) return p == g ? "B" : "B+";
        if (IsStore(g)) return "S";
        return "O:" + g.Name;
    }

    static string Dump(string path)
    {
        var db = Open(path);
        var sb = new StringBuilder();
        var b = Bin(db);
        sb.Append("R\t" + (b == null ? "-" : Hex(b.Uuid.UuidBytes)) + "\n");
        foreach (var g in db.RootGroup.GetGroups(true))
            if (IsStore(g)) sb.Append("G\t" + Hex(g.Uuid.UuidBytes) + "\t" + g.Name + "\t" + Place(db, g.ParentGroup) + "\n");
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (!e.Strings.Exists("KPG.Id")) continue;
            var kind = e.Strings.ReadSafe("KPG.Kind"); var data = e.Strings.ReadSafe("KPG.Data");
            bool intact = e.Strings.ReadSafe("KPG.Mac") == Mac(kind, data);
            sb.Append("E\t" + Hex(e.Uuid.UuidBytes) + "\t" + Sec(e.LastModificationTime) + "\t" + Sec(e.LocationChanged) + "\t"
                + Place(db, e.ParentGroup) + "\t" + e.Strings.ReadSafe("KPG.Id") + "\t" + (intact ? "1" : "0") + "\t" + e.History.UCount + "\n");
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
            case "new": return New(p[1], p[2]);
            case "patch": return Patch(p[1], p[2]);
            case "merge": return Merge(p[1], p[2], p[3], p[4]);
            case "bin": return BinIt(p[1], p[2], long.Parse(p[3]));
            case "touch": return Touch(p[1], p[2], long.Parse(p[3]));
            case "edit": return Edit(p[1], p[2], long.Parse(p[3]));
            case "delrec": return DelRec(p[1], p[2], long.Parse(p[3]));
            case "empty": return Empty(p[1], long.Parse(p[2]));
            case "clear": return Clear(p[1]);
            case "dedupe": return Dedupe(p[1]);
            case "dump": return Dump(p[1]);
            default: throw new InvalidOperationException(line);
        }
    }
}
