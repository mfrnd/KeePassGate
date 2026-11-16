// Probe for the storage option "database-level data as ordinary entries in a dedicated group".
// Invented content only. Console only: KeePassLib 2.62 loaded from KeePass.exe, no window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Program
{
    const string Password = "probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "make") { Make(args[1]); return 0; }
        if (args.Length >= 5 && args[0] == "kpmerge")
        {
            var target = Open(args[1]);
            var source = Open(args[2]);
            target.MergeIn(source, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), args[3]), null);
            target.SaveAs(IOConnectionInfo.FromPath(args[4]), false, null);
            target.Close(); source.Close();
            return 0;
        }
        if (args.Length >= 2 && args[0] == "dump") { for (int i = 1; i < args.Length; i++) Dump(args[i]); return 0; }
        if (args.Length >= 1 && args[0] == "walk") { Walk(); return 0; }
        if (args.Length >= 1 && args[0] == "uuids") { Uuids(); return 0; }
        Console.Error.WriteLine("usage: make <dir> | kpmerge <target> <source> <method> <out> | dump <file>... | walk | uuids");
        return 2;
    }

    // ---- identity -------------------------------------------------------------------------

    static PwUuid Derive(string kind, string identity)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes("KPG/1/" + kind + "/" + identity));
            var b = new byte[16];
            Array.Copy(d, b, 16);
            return new PwUuid(b);
        }
    }

    static PwUuid GroupUuid() { return Derive("group", ""); }

    static void Uuids()
    {
        Console.WriteLine("group                         " + GroupUuid().ToHexString());
        Console.WriteLine("authority fpA                 " + Derive("authority", "fpA").ToHexString());
        Console.WriteLine("revoked c0                    " + Derive("revoked", "c0").ToHexString());
    }

    // ---- building -------------------------------------------------------------------------

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Password)); return k; }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static KdfParameters Kdf()
    {
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        return a;
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
        g.IsExpanded = false;
        Stamp(g, t);
        return g;
    }

    static string Filler(int n, string seed)
    {
        // Deterministic base64-looking text of n characters, standing in for a PKCS#12 document.
        var sb = new StringBuilder();
        using (var sha = SHA256.Create())
        {
            int i = 0;
            while (sb.Length < n)
            {
                sb.Append(Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(seed + i))));
                i++;
            }
        }
        return sb.ToString(0, n);
    }

    static PwEntry Record(string kind, string identity, string title, string data, bool protect, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = Derive(kind, identity);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
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
        return Record("authority", fp, "KPG authority " + fp,
            "{\"v\":\"1\",\"ca\":\"" + Filler(1400, "ca" + fp) + "\",\"host\":\"" + Filler(1400, "host" + fp) + "\",\"sniKey\":\"" + Filler(44, "sni" + fp) + "\"}", true, t);
    }

    static PwEntry Client(string c, DateTime t)
    {
        return Record("client", c, "KPG client " + c, "v 1\nsubject " + c + "\nprofiles reader\ncertificate " + Filler(650, "cert" + c), false, t);
    }

    static PwEntry Revoked(string c, DateTime t)
    {
        return Record("revoked", c, "KPG revoked " + c, "{\"v\":\"1\",\"certificate\":\"" + c + "\",\"notAfter\":\"2027-01-01T00:00:00Z\",\"authority\":\"fpA\"}", false, t);
    }

    static PwEntry Profile(string kind, string name, int generation, DateTime t)
    {
        return Record(kind, name + "/" + generation, "KPG " + kind + " " + name + " " + generation,
            "{\"v\":\"1\",\"profile\":\"" + name + "\",\"generation\":\"" + generation + "\"}", false, t);
    }

    static PwDatabase NewDatabase(string path)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        var general = new PwGroup(true, false) { Name = "General" };
        Stamp(general, T0);
        db.RootGroup.AddGroup(general, true);
        var e = new PwEntry(true, false);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "user entry"));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password"));
        Stamp(e, T0);
        general.AddEntry(e, true);
        return db;
    }

    static void Delete(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        var a = T0.AddHours(1);
        var b = T0.AddHours(2);

        // Set I: neither copy's base holds the store group; each copy makes it on its own.
        var baseI = Path.Combine(dir, "I-base.kdbx");
        var db = NewDatabase(baseI);
        db.Save(null); db.Close();
        File.Copy(baseI, Path.Combine(dir, "I-A.kdbx"), true);
        File.Copy(baseI, Path.Combine(dir, "I-B.kdbx"), true);

        db = Open(Path.Combine(dir, "I-A.kdbx"));
        var g = StoreGroup(a);
        db.RootGroup.AddGroup(g, true);
        g.AddEntry(Authority("fpA", a), true);
        g.AddEntry(Client("c1", a), true);
        g.AddEntry(Revoked("c0", a), true);
        g.AddEntry(Profile("profile", "reader", 1, a), true);
        db.Save(null); db.Close();

        db = Open(Path.Combine(dir, "I-B.kdbx"));
        g = StoreGroup(b);
        db.RootGroup.AddGroup(g, true);
        g.AddEntry(Authority("fpB", b), true);
        g.AddEntry(Client("c2", b), true);
        g.AddEntry(Revoked("c0", b), true);                       // the same revocation, made independently
        g.AddEntry(Profile("profile", "reader", 1, b), true);     // the same profile addition
        g.AddEntry(Profile("profile-removed", "reader", 1, b), true);
        db.Save(null); db.Close();

        // Set T: as set I, but both copies stamp the shared entries in the same second.
        File.Copy(baseI, Path.Combine(dir, "T-A.kdbx"), true);
        File.Copy(baseI, Path.Combine(dir, "T-B.kdbx"), true);
        foreach (var side in new[] { "A", "B" })
        {
            db = Open(Path.Combine(dir, "T-" + side + ".kdbx"));
            g = StoreGroup(a);
            db.RootGroup.AddGroup(g, true);
            g.AddEntry(Authority("fp" + side, a), true);
            g.AddEntry(Revoked("c0", a), true);
            db.Save(null); db.Close();
        }

        // Set D: the base holds the store; copy A revokes c1 the way the design does (adds the
        // revocation, deletes the record with a deletion record), deletes an older revocation by
        // hand (with a deletion record) and prunes an expired record; copy B is the stale copy.
        var baseD = Path.Combine(dir, "D-base.kdbx");
        db = NewDatabase(baseD);
        g = StoreGroup(T0);
        db.RootGroup.AddGroup(g, true);
        g.AddEntry(Authority("fpA", T0), true);
        g.AddEntry(Client("c1", T0), true);
        g.AddEntry(Client("c3", T0), true);
        g.AddEntry(Client("c9", T0), true);
        g.AddEntry(Revoked("c0", T0), true);
        db.Save(null); db.Close();
        File.Copy(baseD, Path.Combine(dir, "D-A.kdbx"), true);
        File.Copy(baseD, Path.Combine(dir, "D-B.kdbx"), true);

        db = Open(Path.Combine(dir, "D-A.kdbx"));
        g = db.RootGroup.FindGroup(GroupUuid(), true);
        g.AddEntry(Revoked("c1", a), true);
        Delete(db, db.RootGroup.FindEntry(Derive("client", "c1"), true), a);
        Delete(db, db.RootGroup.FindEntry(Derive("revoked", "c0"), true), a);
        Delete(db, db.RootGroup.FindEntry(Derive("client", "c9"), true), a);
        db.Save(null); db.Close();

        db = Open(Path.Combine(dir, "D-B.kdbx"));
        g = db.RootGroup.FindGroup(GroupUuid(), true);
        g.AddEntry(Client("c4", b), true);                        // the stale copy paired a client of its own
        db.Save(null); db.Close();
    }

    // ---- reading --------------------------------------------------------------------------

    static string Short(PwUuid u) { return u == null || u.Equals(PwUuid.Zero) ? "-" : u.ToHexString().Substring(0, 8).ToLowerInvariant(); }
    static string Tm(DateTime t) { return t.ToUniversalTime().ToString("MM-dd'T'HH:mm:ss"); }

    static string FormatVersion(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    static string PathOf(PwGroup g) { return g.ParentGroup == null ? "" : (PathOf(g.ParentGroup) + "/" + g.Name).TrimStart('/'); }

    static string Strings(PwEntry e)
    {
        var parts = new List<string>();
        foreach (var kv in e.Strings)
        {
            if (kv.Key == PwDefs.TitleField) continue;
            var text = kv.Value.ReadString();
            parts.Add(kv.Key + (kv.Value.IsProtected ? "[P]" : "") + "(" + text.Length + ":" + Sha(text) + ")");
        }
        parts.Sort(StringComparer.Ordinal);
        if (e.CustomData.Count > 0) parts.Add("cd=" + string.Join(",", e.CustomData.Select(kv => kv.Key)));
        return string.Join(" ", parts);
    }

    static string Sha(string text)
    {
        using (var sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)), 0, 3).Replace("-", "").ToLowerInvariant();
        }
    }

    static void Dump(string file)
    {
        var db = Open(file);
        Console.WriteLine("# " + Path.GetFileName(file) + " kdbx " + FormatVersion(file) + " dbCustomData=[" + string.Join(",", db.CustomData.Select(kv => kv.Key)) + "]");
        foreach (var d in db.DeletedObjects.Select(d => "  deleted " + Short(d.Uuid) + " @" + Tm(d.DeletionTime)).OrderBy(s => s, StringComparer.Ordinal)) Console.WriteLine(d);
        DumpGroup(db.RootGroup);
        db.Close();
    }

    static void DumpGroup(PwGroup g)
    {
        Console.WriteLine("  G '" + PathOf(g) + "' u=" + Short(g.Uuid) + " mod=" + Tm(g.LastModificationTime) + " icon=" + g.IconId
            + " search=" + (g.EnableSearching.HasValue ? g.EnableSearching.Value.ToString() : "inherit")
            + " autotype=" + (g.EnableAutoType.HasValue ? g.EnableAutoType.Value.ToString() : "inherit")
            + " notes=" + g.Notes.Length);
        foreach (var e in g.Entries.OrderBy(x => x.Strings.ReadSafe(PwDefs.TitleField), StringComparer.Ordinal))
        {
            Console.WriteLine("    E '" + e.Strings.ReadSafe(PwDefs.TitleField) + "' u=" + Short(e.Uuid) + " mod=" + Tm(e.LastModificationTime)
                + " hist=" + e.History.UCount + (e.History.UCount > 0 ? "[" + string.Join(",", e.History.Select(h => Tm(h.LastModificationTime))) + "]" : "")
                + " :: " + Strings(e));
        }
        foreach (var s in g.Groups.OrderBy(x => x.Name, StringComparer.Ordinal)) DumpGroup(s);
    }

    // ---- the cost of reading the store on every request ------------------------------------

    static void Walk()
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Path.GetTempPath(), "kpg-walk-never-saved.kdbx")), Key());
        var rnd = new Random(1);
        int groups = 500, perGroup = 20, tombstones = 5000;
        for (int i = 0; i < groups; i++)
        {
            var g = new PwGroup(true, true) { Name = "g" + i };
            (i % 5 == 0 ? db.RootGroup : db.RootGroup.Groups.GetAt((uint)rnd.Next((int)db.RootGroup.Groups.UCount))).AddGroup(g, true);
            for (int j = 0; j < perGroup; j++)
            {
                var e = new PwEntry(true, true);
                e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "entry " + i + "." + j));
                e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "user"));
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented"));
                e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.invalid/" + j));
                e.Strings.Set("Custom", new ProtectedString(false, "x"));
                g.AddEntry(e, true);
            }
        }
        for (int i = 0; i < tombstones; i++) db.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), DateTime.UtcNow));

        var store = StoreGroup(DateTime.UtcNow);
        db.RootGroup.AddGroup(store, true);
        store.AddEntry(Authority("fpA", DateTime.UtcNow), true);
        var wanted = new HashSet<PwUuid>();
        for (int i = 0; i < 20; i++)
        {
            store.AddEntry(Client("c" + i, DateTime.UtcNow), true);
            wanted.Add(Derive("revoked", "c" + i));
        }
        for (int i = 0; i < 32; i++)
        {
            store.AddEntry(Profile("profile", "p" + i, 1, DateTime.UtcNow), true);
            wanted.Add(Derive("profile-removed", "p" + i + "/1"));
        }
        wanted.Add(Derive("retired", "fpA"));
        // One stray: a revocation dragged into a user group.
        db.RootGroup.Groups.GetAt(3).AddEntry(Revoked("c7", DateTime.UtcNow), true);

        Console.WriteLine("entries " + db.RootGroup.GetEntriesCount(true) + ", groups " + db.RootGroup.GetGroups(true).UCount
            + ", deletion records " + db.DeletedObjects.UCount + ", wanted identifiers " + wanted.Count);

        Func<int> pass = delegate
        {
            int found = 0;
            var stack = new Stack<PwGroup>();
            stack.Push(db.RootGroup);
            while (stack.Count > 0)
            {
                var g = stack.Pop();
                foreach (var e in g.Entries)
                {
                    if (wanted.Contains(e.Uuid) || e.Strings.Exists("KPG.Kind")) found++;
                }
                foreach (var s in g.Groups) stack.Push(s);
            }
            foreach (var d in db.DeletedObjects) { if (wanted.Contains(d.Uuid)) found++; }
            return found;
        };

        for (int i = 0; i < 20; i++) pass();
        var sw = Stopwatch.StartNew();
        int n = 200, f = 0;
        for (int i = 0; i < n; i++) f = pass();
        sw.Stop();
        Console.WriteLine("one pass over the tree and the deletion records: " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms, found " + f);

        // The parts of that pass, one at a time.
        Func<int> byUuid = delegate
        {
            int found = 0;
            var stack = new Stack<PwGroup>();
            stack.Push(db.RootGroup);
            while (stack.Count > 0)
            {
                var g = stack.Pop();
                foreach (var e in g.Entries) { if (wanted.Contains(e.Uuid)) found++; }
                foreach (var s in g.Groups) stack.Push(s);
            }
            return found;
        };
        Func<int> byMarker = delegate
        {
            int found = 0;
            var stack = new Stack<PwGroup>();
            stack.Push(db.RootGroup);
            while (stack.Count > 0)
            {
                var g = stack.Pop();
                foreach (var e in g.Entries) { if (e.Strings.Exists("KPG.Kind")) found++; }
                foreach (var s in g.Groups) stack.Push(s);
            }
            return found;
        };
        Func<int> byRecords = delegate
        {
            int found = 0;
            foreach (var d in db.DeletedObjects) { if (wanted.Contains(d.Uuid)) found++; }
            return found;
        };
        Func<int> byIndex = delegate
        {
            // The same walk with indexed access rather than enumerators.
            int found = 0;
            var stack = new Stack<PwGroup>();
            stack.Push(db.RootGroup);
            while (stack.Count > 0)
            {
                var g = stack.Pop();
                var entries = g.Entries;
                for (uint i = 0; i < entries.UCount; i++) { if (wanted.Contains(entries.GetAt(i).Uuid)) found++; }
                var below = g.Groups;
                for (uint i = 0; i < below.UCount; i++) stack.Push(below.GetAt(i));
            }
            var deleted = db.DeletedObjects;
            for (uint i = 0; i < deleted.UCount; i++) { if (wanted.Contains(deleted.GetAt(i).Uuid)) found++; }
            return found;
        };
        foreach (var part in new KeyValuePair<string, Func<int>>[] {
            new KeyValuePair<string, Func<int>>("tree, by identifier only", byUuid),
            new KeyValuePair<string, Func<int>>("tree, by the KPG.Kind string only", byMarker),
            new KeyValuePair<string, Func<int>>("deletion records only", byRecords),
            new KeyValuePair<string, Func<int>>("tree and deletion records by identifier, indexed access", byIndex) })
        {
            for (int i = 0; i < 20; i++) part.Value();
            sw.Restart();
            for (int i = 0; i < n; i++) f = part.Value();
            sw.Stop();
            Console.WriteLine("  " + part.Key + ": " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms, found " + f);
        }

        var target = Derive("revoked", "c-absent");
        sw.Restart();
        PwEntry hit = null;
        for (int i = 0; i < n; i++) hit = db.RootGroup.FindEntry(target, true);
        sw.Stop();
        Console.WriteLine("PwGroup.FindEntry for one identifier: " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms, found " + (hit != null));

        // Reading the store group itself: every entry's kind and unprotected data.
        sw.Restart();
        int chars = 0;
        for (int i = 0; i < n; i++)
        {
            chars = 0;
            foreach (var e in store.Entries)
            {
                var kind = e.Strings.ReadSafe("KPG.Kind");
                var data = e.Strings.Get("KPG.Data");
                if (data != null && !data.IsProtected) chars += data.ReadString().Length;
                chars += kind.Length;
            }
        }
        sw.Stop();
        Console.WriteLine("reading the store group's " + store.Entries.UCount + " entries (kind, unprotected data): " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms, " + chars + " chars");

        // What decoding the protected authority document costs, which the design avoids per request.
        var auth = store.Entries.GetAt(0).Strings.Get("KPG.Data");
        sw.Restart();
        int len = 0;
        for (int i = 0; i < n; i++) len = auth.ReadString().Length;
        sw.Stop();
        Console.WriteLine("decoding the protected authority document (" + len + " chars): " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms");
        var again = store.Entries.GetAt(0).Strings.Get("KPG.Data");
        Console.WriteLine("the same ProtectedString object on a second Get: " + ReferenceEquals(auth, again));
    }
}
