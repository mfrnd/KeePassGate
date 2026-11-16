// Survival probe for the storage option "database-level data as ordinary entries in a dedicated group".
// Invented content only, password "test", AES-KDF at one round so that kdbxweb can open the files.
// Console only: KeePassLib 2.62 loaded from KeePass.exe; no window. C# 5 (CodeDom).
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

public static class Surv
{
    const string Password = "test";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    // ---- identity, as the design derives it ----------------------------------------------
    public static PwUuid Derive(string kind, string subject)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes("KPG/1/" + kind + "/" + subject));
            var b = new byte[16];
            Array.Copy(d, b, 16);
            return new PwUuid(b);
        }
    }
    static PwUuid GroupUuid() { return Derive("group", ""); }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Password)); return k; }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup StoreGroup(DateTime t)
    {
        var g = new PwGroup(false, false);
        g.Uuid = GroupUuid();
        g.Name = "KeePassGate";
        g.IconId = PwIcon.TerminalEncrypted;
        g.Notes = NotesLine;
        g.EnableSearching = false;
        g.EnableAutoType = false;
        Stamp(g, t);
        return g;
    }

    static PwEntry Record(string kind, string subject, string words, string data, bool protect, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = Derive(kind, subject);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "KPG " + words + " " + subject));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, NotesLine));
        e.Strings.Set("KPG.Kind", new ProtectedString(false, kind));
        e.Strings.Set("KPG.Data", new ProtectedString(protect, data));
        Stamp(e, t);
        return e;
    }

    static PwEntry Authority(string fp, DateTime t)
    {
        return Record("authority", fp, "authority", "{\"v\":\"1\",\"authority\":\"" + fp + "\",\"ca\":\"invented-" + fp + "\"}", true, t);
    }
    static PwEntry Client(string c, string fp, DateTime t)
    {
        return Record("client", c, "client", "{\"v\":\"1\",\"client\":\"" + c + "\",\"authority\":\"" + fp + "\"}", false, t);
    }
    static PwEntry Revoked(string c, string fp, DateTime t)
    {
        return Record("revoked", c, "revoked client", "{\"v\":\"1\",\"certificate\":\"" + c + "\",\"authority\":\"" + fp + "\"}", false, t);
    }

    static PwDatabase NewDatabase(string path)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, 1UL);
        db.KdfParameters = kdf;
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        var general = new PwGroup(true, false);
        general.Name = "General";
        Stamp(general, T0);
        db.RootGroup.AddGroup(general, true);
        var e = new PwEntry(true, false);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "user entry"));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password"));
        Stamp(e, T0);
        general.AddEntry(e, true);
        return db;
    }

    // The plugin's revocation in this design: add the revocation entry, delete the record with a
    // deletion record, both at time t.
    static void Revoke(PwDatabase db, string c, string fp, DateTime t)
    {
        var g = db.RootGroup.FindGroup(GroupUuid(), true);
        g.AddEntry(Revoked(c, fp, t), true);
        var rec = db.RootGroup.FindEntry(Derive("client", c), true);
        rec.ParentGroup.Entries.Remove(rec);
        db.DeletedObjects.Add(new PwDeletedObject(rec.Uuid, t));
    }

    // KeePass's permanent delete of a group (MainForm_Functions.cs:5257-5260): a deletion record
    // for every entry and group below it (PwGroup.DeleteAllObjects), then one for the group.
    // DeleteAllObjects stamps "now"; here every record carries the explicit time t instead.
    static void DeleteGroupLikeKeePass(PwDatabase db, PwGroup g, DateTime t)
    {
        foreach (var e in g.GetEntries(true)) db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
        foreach (var s in g.GetGroups(true)) db.DeletedObjects.Add(new PwDeletedObject(s.Uuid, t));
        g.ParentGroup.Groups.Remove(g);
        db.DeletedObjects.Add(new PwDeletedObject(g.Uuid, t));
    }

    static void Save(PwDatabase db) { db.Save(null); db.Close(); }

    public static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        var h1 = T0.AddHours(1); var h2 = T0.AddHours(2); var h3 = T0.AddHours(3);

        // D0: the store with one authority and two clients.
        var d0 = Path.Combine(dir, "D0.kdbx");
        var db = NewDatabase(d0);
        var g = StoreGroup(T0);
        db.RootGroup.AddGroup(g, true);
        g.AddEntry(Authority("fpA", T0), true);
        g.AddEntry(Client("c1", "fpA", T0), true);
        g.AddEntry(Client("c2", "fpA", T0), true);
        Save(db);

        // P: the KeePass copy revokes c1 at +1h. S: the stale copy, unchanged.
        File.Copy(d0, Path.Combine(dir, "P.kdbx"), true);
        File.Copy(d0, Path.Combine(dir, "S.kdbx"), true);
        db = Open(Path.Combine(dir, "P.kdbx"));
        Revoke(db, "c1", "fpA", h1);
        Save(db);

        // X1: a copy in which somebody deleted the store group for good at +1h, KeePass's way.
        File.Copy(d0, Path.Combine(dir, "X1.kdbx"), true);
        db = Open(Path.Combine(dir, "X1.kdbx"));
        DeleteGroupLikeKeePass(db, db.RootGroup.FindGroup(GroupUuid(), true), h1);
        Save(db);

        // P1: X1 after the operator created a new authority at +2h. The design makes the store group
        // again under the same fixed UUID and removes the deletion record naming it (section 5).
        // Then a client c5 pairs. Q: a stale copy taken here. P2: P1 after c5 is revoked at +3h.
        File.Copy(Path.Combine(dir, "X1.kdbx"), Path.Combine(dir, "P1.kdbx"), true);
        db = Open(Path.Combine(dir, "P1.kdbx"));
        var gu = GroupUuid();
        var keep = db.DeletedObjects.Where(o => !o.Uuid.Equals(gu)).ToList();
        db.DeletedObjects.Clear();
        foreach (var o in keep) db.DeletedObjects.Add(o);
        g = StoreGroup(h2);
        db.RootGroup.AddGroup(g, true);
        g.AddEntry(Authority("fpB", h2), true);
        g.AddEntry(Client("c5", "fpB", h2), true);
        Save(db);
        File.Copy(Path.Combine(dir, "P1.kdbx"), Path.Combine(dir, "Q.kdbx"), true);
        File.Copy(Path.Combine(dir, "P1.kdbx"), Path.Combine(dir, "P2.kdbx"), true);
        db = Open(Path.Combine(dir, "P2.kdbx"));
        Revoke(db, "c5", "fpB", h3);
        Save(db);
    }

    public static void Merge(string target, string source, string method, string output)
    {
        var t = Open(target);
        var s = Open(source);
        t.MergeIn(s, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), method), null);
        t.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        t.Close(); s.Close();
    }

    // ---- reading, and the gate's fold as the design describes it (section 4) -------------
    static readonly string[] Subjects = { "fpA", "fpB", "c0", "c1", "c2", "c5" };
    static string Name(PwUuid u)
    {
        if (u.Equals(GroupUuid())) return "group";
        foreach (var k in new[] { "authority", "retired", "client", "revoked" })
            foreach (var s in Subjects)
                if (u.Equals(Derive(k, s))) return k + ":" + s;
        return u.ToHexString().Substring(0, 8).ToLowerInvariant();
    }

    static bool InBin(PwDatabase db, PwGroup g)
    {
        if (!db.RecycleBinEnabled || db.RecycleBinUuid.Equals(PwUuid.Zero)) return false;
        for (var p = g; p != null; p = p.ParentGroup) if (p.Uuid.Equals(db.RecycleBinUuid)) return true;
        return false;
    }

    static string Field(string json, string name)
    {
        var key = "\"" + name + "\":\"";
        int i = json.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        i += key.Length;
        int j = json.IndexOf('"', i);
        return j < 0 ? null : json.Substring(i, j - i);
    }

    public static string Dump(string file)
    {
        var sb = new StringBuilder();
        var db = Open(file);
        var dr = new HashSet<PwUuid>(db.DeletedObjects.Select(o => o.Uuid));
        sb.AppendLine("# " + Path.GetFileName(file));
        sb.AppendLine("  deletion records: " + (dr.Count == 0 ? "none" : string.Join(", ", dr.Select(Name).OrderBy(x => x, StringComparer.Ordinal))));
        var all = db.RootGroup.GetEntries(true).ToList();
        var stores = db.RootGroup.GetGroups(true).Where(x => x.Uuid.Equals(GroupUuid())).ToList();
        sb.AppendLine("  store groups: " + stores.Count + (stores.Count == 1 ? (InBin(db, stores[0]) ? " (in the bin)" : "") : ""));
        foreach (var e in all.Where(x => x.Strings.Exists("KPG.Kind")).OrderBy(x => x.Strings.ReadSafe(PwDefs.TitleField), StringComparer.Ordinal))
        {
            var place = (stores.Count == 1 && e.ParentGroup == stores[0]) ? "in store" : ("in '" + e.ParentGroup.Name + "'");
            sb.AppendLine("    " + e.Strings.ReadSafe(PwDefs.TitleField) + " [" + place + "]" + (e.Uuid.Equals(Derive(e.Strings.ReadSafe("KPG.Kind"), Field(e.Strings.ReadSafe("KPG.Data"), e.Strings.ReadSafe("KPG.Kind") == "authority" ? "authority" : (e.Strings.ReadSafe("KPG.Kind") == "client" ? "client" : "certificate")) ?? "")) ? "" : " (uuid not derived)"));
        }

        // Granting: in the one store group, outside the bin, UUID derived from content, no deletion record.
        PwGroup store = (stores.Count == 1 && !InBin(db, stores[0])) ? stores[0] : null;
        Func<string, string, bool> grants = delegate (string kind, string subject)
        {
            if (store == null) return false;
            var u = Derive(kind, subject);
            if (dr.Contains(u)) return false;
            return store.Entries.Any(x => x.Uuid.Equals(u) && x.Strings.ReadSafe("KPG.Kind") == kind);
        };
        // Refusing: anywhere by UUID, by a deletion record of its UUID, or by kind and content.
        Func<string, string, string, bool> refuses = delegate (string kind, string subject, string field)
        {
            var u = Derive(kind, subject);
            if (dr.Contains(u)) return true;
            return all.Any(x => x.Uuid.Equals(u) || (x.Strings.ReadSafe("KPG.Kind") == kind && Field(x.Strings.ReadSafe("KPG.Data"), field) == subject));
        };
        var unretired = new[] { "fpA", "fpB" }.Where(fp => grants("authority", fp) && !refuses("retired", fp, "authority")).ToList();
        string served = unretired.Count == 1 ? unretired[0] : (unretired.Count == 0 ? null : "undecided");
        sb.AppendLine("  served authority: " + (served ?? "none"));
        foreach (var c in new[] { "c1", "c2", "c5" })
        {
            string verdict;
            var rec = store == null ? null : store.Entries.FirstOrDefault(x => x.Uuid.Equals(Derive("client", c)));
            bool hasRecord = grants("client", c);
            bool revoked = refuses("revoked", c, "certificate");
            string chain = rec == null ? null : Field(rec.Strings.ReadSafe("KPG.Data"), "authority");
            if (!hasRecord && rec == null && !all.Any(x => x.Uuid.Equals(Derive("client", c)))) verdict = "no record";
            else if (served == null || served == "undecided") verdict = "not served (authority " + (served ?? "none") + ")";
            else if (!hasRecord) verdict = "refused (record does not count" + (dr.Contains(Derive("client", c)) ? ": deletion record" : "") + ")";
            else if (revoked) verdict = "refused (revoked" + (dr.Contains(Derive("revoked", c)) ? ", by deletion record" : "") + ")";
            else if (chain != served) verdict = "refused (chains to " + chain + ")";
            else verdict = "ADMITTED";
            sb.AppendLine("  " + c + ": " + verdict);
        }
        db.Close();
        return sb.ToString();
    }
}
