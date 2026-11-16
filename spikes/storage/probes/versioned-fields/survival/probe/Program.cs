// Probe for the storage option "versioned fields": every write a JSON document in an entry field
// named from a timestamp, each write in an entry of its own, reconciled when read.
// Invented content only. Console only: KeePassLib 2.62 loaded from KeePass.exe, no window.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Program
{
    const string Password = "probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string Home = "5D3F0A77C2E94B1886F0A1D2C3B4E5F6";      // invented store identifier
    const string Foreign = "00112233445566778899AABBCCDDEEFF";
    const string WriteNotes = "KeePassGate data. Do not edit or delete: it keeps revoked clients out. Managed on the KPG tab in KeePass.";
    const string MarkerNotes = "KeePassGate: the access rule of the group this entry is in. Managed on the group's KPG tab in KeePass.";
    static readonly Regex VersionName = new Regex(@"^KPG\.(\d{8}T\d{6}Z\.[0-9A-F]{16}\.\d{2})$", RegexOptions.CultureInvariant);

    static byte[] Sha(string s) { using (var h = SHA256.Create()) return h.ComputeHash(Encoding.UTF8.GetBytes(s)); }
    static string Hex(byte[] b, int off, int n) { var sb = new StringBuilder(); for (int i = off; i < off + n; i++) sb.Append(b[i].ToString("X2")); return sb.ToString(); }
    static readonly byte[] MarkW = Sha("KPG/1/write").Take(4).ToArray();
    static readonly byte[] MarkM = Sha("KPG/1/marker").Take(4).ToArray();
    static readonly byte[] MarkG = Sha("KPG/1/group").Take(4).ToArray();

    static int Main(string[] args)
    {
        try
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
            if (args.Length >= 2 && args[0] == "fold") { for (int i = 1; i < args.Length; i++) FoldFile(args[i]); return 0; }
            if (args.Length >= 2 && args[0] == "dump") { for (int i = 1; i < args.Length; i++) Dump(args[i]); return 0; }
            if (args.Length >= 3 && args[0] == "dup") { Dup(args[1], args[2]); return 0; }
            if (args.Length >= 4 && args[0] == "newids") { NewIds(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 2 && args[0] == "sentinel") { Sentinel(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "walk") { Walk(); return 0; }
            if (args.Length >= 1 && args[0] == "marks") { Marks(); return 0; }
            // Added for the survival critique.
            if (args.Length >= 2 && args[0] == "fold2") { CondHist = true; for (int i = 1; i < args.Length; i++) FoldFile(args[i]); return 0; }
            if (args.Length >= 2 && args[0] == "fold3") { CondHist = true; Refined = true; for (int i = 1; i < args.Length; i++) FoldFile(args[i]); return 0; }
            if (args.Length >= 2 && args[0] == "foldcopies") { for (int i = 1; i < args.Length; i++) { var d = Open(args[i]); Fold(d, Path.GetFileName(args[i]) + " (copies under other identifiers searched too)", true); d.Close(); } return 0; }
            if (args.Length >= 3 && args[0] == "recreate") { Recreate(args[1], args[2]); return 0; }
            if (args.Length >= 4 && args[0] == "gone") { Gone(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 3 && args[0] == "close") { Close(args[1], int.Parse(args[2])); return 0; }
            if (args.Length >= 2 && args[0] == "count") { for (int i = 1; i < args.Length; i++) Count(args[i]); return 0; }
            if (args.Length >= 2 && args[0] == "cdcontrol") { CdControl(args[1]); return 0; }
            Console.Error.WriteLine("usage: make <dir> | kpmerge <target> <source> <method> <out> | fold <file>... | dump <file>... | dup <in> <out> | newids <target> <source> <out> | walk | marks");
            return 2;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static bool CondHist;
    static bool Refined;   // a write entry a deletion record names does not count as holding the node's copy

    // ---- added for the survival critique ------------------------------------------------------

    // Every version held in a current field of a write entry carrying the mark, one per identifier.
    static List<Ver> CurrentVersions(PwDatabase db)
    {
        var byId = new Dictionary<string, Ver>(StringComparer.Ordinal);
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (!HasMark(e.Uuid, MarkW)) continue;
            foreach (var kv in e.Strings)
            {
                var m = VersionName.Match(kv.Key);
                if (!m.Success) continue;
                var v = Parse(kv.Value.ReadString());
                if (v == null) continue;
                v.Protect = kv.Value.IsProtected;
                if (!byId.ContainsKey(v.Id)) byId[v.Id] = v;
            }
        }
        return byId.Values.ToList();
    }

    static bool HoldsKind(PwEntry e, string kind)
    {
        foreach (var kv in e.Strings)
        {
            if (!VersionName.IsMatch(kv.Key)) continue;
            var v = Parse(kv.Value.ReadString());
            if (v != null && v.Kind == kind) return true;
        }
        return false;
    }

    // A closing write as the design describes it: a state pair holding what is carried and what is new,
    // then every other write entry deleted with the plugin's own record, except an authority's entry.
    static void StateWrite(PwDatabase db, W w, string what, IEnumerable<Ver> carry, bool keepAuthority)
    {
        var all = carry.ToArray();
        var keep = new HashSet<string>(StringComparer.Ordinal) { w.Uuid.ToHexString(), Twin(w).Uuid.ToHexString() };
        var victims = db.RootGroup.GetEntries(true).Where(e => HasMark(e.Uuid, MarkW) && !keep.Contains(e.Uuid.ToHexString()) && !(keepAuthority && HoldsKind(e, "authority"))).Select(e => e.Uuid).ToList();
        AddTwice(db, w, what, all);
        foreach (var u in victims) PluginDelete(db, u);
    }

    // From a tidied copy: Recreate. A state pair with the retirement of fpA, the entry of fpA deleted, a new authority fpB.
    static void Recreate(string input, string output)
    {
        File.Copy(input, output, true);
        var db = Open(output);
        var t = T0.AddHours(4);
        var wret = NewW("recreate-retire", t);
        var retire = Make(wret, 1, Home, "retire", "authority", "fpA");
        var carry = CurrentVersions(db).Where(v => v.Kind != "authority" && v.Kind != "admit" && v.Kind != "revoke" && v.Kind != "answer").ToList();   // records, revocations and answers under fpA go with it
        carry.Add(retire);
        StateWrite(db, wret, "authority recreated", carry, false);
        var wb = NewW("recreate-new", t);
        var vB = Make(wb, 1, Home, "authority", "authority", "fpB", "doc", "{\"v\":\"1\",\"ca\":\"" + Filler(1400, "caB") + "\",\"host\":\"" + Filler(1400, "hostB") + "\",\"sniKey\":\"" + Filler(44, "sniB") + "\"}");
        vB.Protect = true;
        Kpg(db).AddEntry(WriteEntry(wb, "authority", vB), true);
        var w9 = NewW("recreate-pair", t.AddMinutes(5));
        Kpg(db).AddEntry(WriteEntry(w9, "pairing", Make(w9, 1, Home, "admit", "certificate", "c9", "doc", "v 1\nsubject c9\nprofiles reader\nauthority fpB\ncertificate " + Filler(650, "certc9"))), true);
        db.Save(null); db.Close();
    }

    // From a tidied copy that narrowed X: X deleted for good as KeePass deletes (a record dated now), then
    // the Data page's "Remove rules of nodes that are gone": a state pair without X's rule versions.
    static void Gone(string input, string outDeleted, string outPressed)
    {
        File.Copy(input, outDeleted, true);
        var db = Open(outDeleted);
        var x = E(db, EntryX);
        x.ParentGroup.Entries.Remove(x);
        db.DeletedObjects.Add(new PwDeletedObject(EntryX, T0.AddHours(3)));
        db.Save(null); db.Close();
        File.Copy(outDeleted, outPressed, true);
        db = Open(outPressed);
        var wg = NewW("press-gone", T0.AddHours(4));
        var carry = CurrentVersions(db).Where(v => !(v.Kind == "rule" && v.Get("node") == EntryX.ToHexString()) && v.Kind != "authority").ToList();
        StateWrite(db, wg, "state", carry, true);
        db.Save(null); db.Close();
    }

    // One round of a renewal as the design writes it: the new certificate's record in a small entry, then the
    // client gives the old certificate up, which is a revocation: a state pair carrying everything in force.
    static void Close(string file, int n)
    {
        var db = Open(file);
        var t = T0.AddDays(1 + n);
        var wa = NewW("renew-admit-" + n, t);
        Kpg(db).AddEntry(WriteEntry(wa, "client renewed", Admit(wa, 1, "k" + n)), true);
        var wr = NewW("renew-revoke-" + n, t.AddMinutes(1));
        var revoke = Revoke(wr, 1, "k" + (n - 1));
        var current = CurrentVersions(db).Where(v => v.Kind != "authority").ToList();
        var revoked = new HashSet<string>(current.Where(v => v.Kind == "revoke").Select(v => v.Get("certificate")).Concat(new[] { "k" + (n - 1) }), StringComparer.Ordinal);
        var carry = current.Where(v => !(v.Kind == "admit" && revoked.Contains(v.Get("certificate")))).ToList();
        carry.Add(revoke);
        StateWrite(db, wr, "state", carry, true);
        db.Save(null); db.Close();
    }

    static void Count(string file)
    {
        var db = Open(file);
        int entries = 0, fields = 0; long chars = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (!HasMark(e.Uuid, MarkW)) continue;
            entries++;
            foreach (var kv in e.Strings) { if (!VersionName.IsMatch(kv.Key)) continue; fields++; chars += kv.Value.Length; ids.Add(kv.Key); }
        }
        int records = db.DeletedObjects.Count(d => HasMark(d.Uuid, MarkW));
        Console.WriteLine(Path.GetFileName(file) + ": write entries " + entries + ", version fields " + fields + " (" + ids.Count + " distinct), characters in version fields " + chars
            + ", deletion records naming write entries " + records + ", file bytes " + new FileInfo(file).Length);
        db.Close();
    }

    // Today's store, for comparison: a revocation key only the source holds, through File then Import with new identifiers.
    static void CdControl(string dir)
    {
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "cd-base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        db.KdfParameters = Kdf();
        db.CustomData.Set("KPG.Client.c1", "record of c1");
        db.CustomData.Set("KPG.Authority.fpA", "authority document");
        db.Save(null); db.Close();
        var a = Path.Combine(dir, "cd-A.kdbx"); File.Copy(basePath, a, true);
        db = Open(a); db.CustomData.Set("KPG.Revoked.c1", "2027-01-01T00:00:00Z fpA"); db.CustomData.Set("KPG.Client.c1", ""); db.CustomData.Set("KPG.RetiredAuthority.fpA", "retired"); db.Save(null); db.Close();
        foreach (var method in new[] { PwMergeMethod.CreateNewUuids, PwMergeMethod.Synchronize })
        {
            var t = Open(basePath); var s = Open(a);
            t.MergeIn(s, method, null);
            var outPath = Path.Combine(dir, "cd-A-into-base-" + method + ".kdbx");
            t.SaveAs(IOConnectionInfo.FromPath(outPath), false, null);
            t.Close(); s.Close();
            var r = Open(outPath);
            Console.WriteLine("today's store, revoking copy imported into the stale copy by " + method + ": "
                + string.Join(", ", r.CustomData.Select(kv => kv.Key + "=" + (kv.Value.Length == 0 ? "(empty)" : kv.Value))));
            r.Close();
        }
    }

    static void Marks()
    {
        Console.WriteLine("write entry mark  " + Hex(MarkW, 0, 4) + "  (first 4 bytes of SHA-256 over 'KPG/1/write')");
        Console.WriteLine("marker mark       " + Hex(MarkM, 0, 4) + "  (first 4 bytes of SHA-256 over 'KPG/1/marker')");
        Console.WriteLine("group mark        " + Hex(MarkG, 0, 4) + "  (first 4 bytes of SHA-256 over 'KPG/1/group')");
        var w = NewW("example", new DateTime(2026, 11, 15, 10, 15, 2, DateTimeKind.Utc));
        Console.WriteLine("example write entry UUID " + w.Uuid.ToHexString() + ", version name KPG." + Id(w, 1));
    }

    // ---- identity -------------------------------------------------------------------------

    class W { public string Label; public DateTime Time; public string Origin; public PwUuid Uuid; }

    static string Stamp(DateTime t) { return t.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"); }

    static W NewW(string label, DateTime t)
    {
        var rnd = Sha("origin/" + label);
        var b = new byte[16];
        Array.Copy(MarkW, b, 4);
        uint secs = (uint)(t - Epoch).TotalSeconds;
        b[4] = (byte)(secs >> 24); b[5] = (byte)(secs >> 16); b[6] = (byte)(secs >> 8); b[7] = (byte)secs;
        Array.Copy(rnd, 0, b, 8, 8);
        return new W { Label = label, Time = t, Origin = Hex(rnd, 0, 8), Uuid = new PwUuid(b) };
    }

    static string Id(W w, int n) { return Stamp(w.Time) + "." + w.Origin + "." + n.ToString("00"); }

    static bool HasMark(PwUuid u, byte[] mark) { var b = u.UuidBytes; return b[0] == mark[0] && b[1] == mark[1] && b[2] == mark[2] && b[3] == mark[3]; }

    static DateTime StampOf(PwUuid u)
    {
        var b = u.UuidBytes;
        uint secs = ((uint)b[4] << 24) | ((uint)b[5] << 16) | ((uint)b[6] << 8) | b[7];
        return Epoch.AddSeconds(secs);
    }

    static PwUuid MarkerUuid(PwUuid group)
    {
        var d = Sha("marker/" + group.ToHexString());
        var b = new byte[16];
        Array.Copy(MarkM, b, 4);
        Array.Copy(d, 0, b, 4, 12);
        return new PwUuid(b);
    }

    static string Hash16(string id) { return Hex(Sha("gone/" + id), 0, 8); }

    // ---- versions -------------------------------------------------------------------------

    class Ver
    {
        public string Id, Kind, Db, Json;
        public bool Protect;
        public Dictionary<string, string> S = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string[]> A = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public string Name { get { return "KPG." + Id; } }
        public string Get(string k) { string v; return S.TryGetValue(k, out v) ? v : null; }
        public string[] After { get { return (Get("after") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); } }
        public bool Replaces(Ver other) { return After.Contains(Hash16(other.Id)); }
    }

    static string Canon(SortedDictionary<string, object> m)
    {
        var sb = new StringBuilder();
        foreach (var kv in m)
        {
            if (kv.Key == "sum") continue;
            var arr = kv.Value as string[];
            sb.Append(kv.Key).Append('=').Append(arr != null ? string.Join(" ", arr) : (string)kv.Value).Append('\n');
        }
        return sb.ToString();
    }

    static string Q(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\""; }

    static Ver Make(W w, int n, string db, string kind, params object[] kv)
    {
        var m = new SortedDictionary<string, object>(StringComparer.Ordinal);
        m["v"] = "1"; m["db"] = db; m["id"] = Id(w, n); m["kind"] = kind;
        for (int i = 0; i < kv.Length; i += 2) m[(string)kv[i]] = kv[i + 1];
        var sum = Hex(Sha(Canon(m)), 0, 8);
        var sb = new StringBuilder("{");
        foreach (var kvp in m)
        {
            var arr = kvp.Value as string[];
            sb.Append(Q(kvp.Key)).Append(':');
            if (arr != null) sb.Append('[').Append(string.Join(",", arr.Select(Q))).Append(']');
            else sb.Append(Q((string)kvp.Value));
            sb.Append(',');
        }
        sb.Append("\"sum\":").Append(Q(sum)).Append('}');
        var v = Parse(sb.ToString());
        if (v == null) throw new InvalidOperationException("a version the probe made does not read");
        return v;
    }

    static Ver Parse(string json)
    {
        Dictionary<string, object> d;
        try { d = new JavaScriptSerializer { MaxJsonLength = 1 << 22 }.DeserializeObject(json) as Dictionary<string, object>; }
        catch (Exception) { return null; }
        if (d == null) return null;
        var m = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var v = new Ver { Json = json };
        foreach (var kv in d)
        {
            if (kv.Value is string) { v.S[kv.Key] = (string)kv.Value; m[kv.Key] = kv.Value; }
            else if (kv.Value is IEnumerable)
            {
                var list = new List<string>();
                foreach (var o in (IEnumerable)kv.Value) { if (!(o is string)) return null; list.Add((string)o); }
                v.A[kv.Key] = list.ToArray(); m[kv.Key] = list.ToArray();
            }
            else return null;
        }
        if (v.Get("v") != "1" || v.Get("db") == null || v.Get("id") == null || v.Get("kind") == null || v.Get("sum") == null) return null;
        if (Hex(Sha(Canon(m)), 0, 8) != v.Get("sum")) return null;
        v.Id = v.Get("id"); v.Kind = v.Get("kind"); v.Db = v.Get("db");
        return v;
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

    static PwUuid Fixed(byte[] mark, string label)
    {
        var d = Sha("fixed/" + label);
        var b = new byte[16];
        if (mark != null) { Array.Copy(mark, b, 4); Array.Copy(d, 0, b, 4, 12); } else Array.Copy(d, b, 16);
        return new PwUuid(b);
    }

    static string Filler(int n, string seed)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (sb.Length < n) { sb.Append(Convert.ToBase64String(Sha(seed + i))); i++; }
        return sb.ToString(0, n);
    }

    static PwEntry Plain(string title, DateTime t, PwUuid uuid)
    {
        var e = new PwEntry(false, false);
        e.Uuid = uuid;
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, ""));
        Stamp(e, t);
        return e;
    }

    static PwEntry WriteEntry(W w, string what, params Ver[] vs)
    {
        var e = Plain("KPG " + w.Time.ToString("yyyy-MM-dd HH:mm:ss") + " " + what, w.Time, w.Uuid);
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, WriteNotes));
        e.Strings.Set("KPG.Write", new ProtectedString(false, w.Origin));
        foreach (var v in vs) e.Strings.Set(v.Name, new ProtectedString(v.Protect, v.Json));
        return e;
    }

    static PwEntry Marker(PwGroup g, Ver rule, DateTime t)
    {
        var e = Plain("KPG rule for this group", t, MarkerUuid(g.Uuid));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, MarkerNotes));
        e.Strings.Set("KPG.GroupRule", new ProtectedString(false, rule.Json));
        return e;
    }

    // The second copy of a refusal: into the carrier of what it refuses, in place of it.
    static void Witness(PwEntry carrier, string removeName, Ver add, DateTime t)
    {
        if (removeName != null) carrier.Strings.Remove(removeName);
        carrier.Strings.Set(add.Name, new ProtectedString(false, add.Json));
        carrier.LastModificationTime = t;
    }

    // The plugin's own deletion: a deletion record stamped one second after the time in the identifier.
    static void PluginDelete(PwDatabase db, PwUuid u)
    {
        var e = db.RootGroup.FindEntry(u, true);
        if (e != null) e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(u, StampOf(u).AddSeconds(1)));
    }

    static void HandDelete(PwDatabase db, PwUuid u, DateTime? record)
    {
        var e = db.RootGroup.FindEntry(u, true);
        if (e != null) e.ParentGroup.Entries.Remove(e);
        if (record.HasValue) db.DeletedObjects.Add(new PwDeletedObject(u, record.Value));
    }

    static readonly PwUuid GroupKpg = Fixed(MarkG, "store group");
    static readonly PwUuid GroupU = Fixed(null, "user group U");
    static readonly PwUuid EntryX = Fixed(null, "user entry X");
    static readonly PwUuid EntryY = Fixed(null, "user entry Y");

    static PwGroup Kpg(PwDatabase db) { return db.RootGroup.FindGroup(GroupKpg, true); }
    static PwEntry E(PwDatabase db, PwUuid u) { return db.RootGroup.FindEntry(u, true); }

    // Every version this one replaces, and everything those replaced: one string of hashes.
    static string AfterOf(params Ver[] after)
    {
        var all = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var a in after) { all.Add(Hash16(a.Id)); foreach (var h in a.After) all.Add(h); }
        return all.Count == 0 ? "none" : string.Join(" ", all);
    }

    static Ver Rule(W w, int n, PwUuid node, string doc, params Ver[] after)
    {
        return Make(w, n, Home, "rule", "node", node.ToHexString(), "doc", doc, "after", AfterOf(after));
    }

    static Ver Name(W w, int n, string name, string act, params Ver[] after)
    {
        return Make(w, n, Home, "name", "name", name, "act", act, "after", AfterOf(after));
    }

    static Ver Admit(W w, int n, string c)
    {
        return Make(w, n, Home, "admit", "certificate", c, "doc", "v 1\nsubject " + c + "\nprofiles reader\ncertificate " + Filler(650, "cert" + c));
    }

    static Ver Revoke(W w, int n, string c)
    {
        return Make(w, n, Home, "revoke", "certificate", c, "notAfter", "2027-01-01T00:00:00Z", "authority", "fpA");
    }

    static W Twin(W w) { return NewW(w.Label + " twin", w.Time); }

    // A write that closes something is made twice: the entry and a second copy holding the same versions.
    static void AddTwice(PwDatabase db, W w, string what, params Ver[] vs)
    {
        Kpg(db).AddEntry(WriteEntry(w, what, vs), true);
        Kpg(db).AddEntry(WriteEntry(Twin(w), what + " (second copy)", vs), true);
    }

    // The entries that carried what a write replaces go, with the plugin's own deletion record.
    static void Retire(PwDatabase db, params W[] ws)
    {
        foreach (var w in ws)
        {
            if (E(db, w.Uuid) != null) PluginDelete(db, w.Uuid);
            if (E(db, Twin(w).Uuid) != null) PluginDelete(db, Twin(w).Uuid);
        }
    }

    static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        var h1 = T0.AddHours(1); var h15 = T0.AddMinutes(90); var h2 = T0.AddHours(2); var h3 = T0.AddHours(3);

        // The versions every copy starts with.
        var wAuth = NewW("auth", T0); var w1 = NewW("w1", T0); var w2 = NewW("w2", T0); var w3 = NewW("w3", T0); var w4 = NewW("w4", T0);
        var vAuth = Make(wAuth, 1, Home, "authority", "authority", "fpA", "doc", "{\"v\":\"1\",\"ca\":\"" + Filler(1400, "ca") + "\",\"host\":\"" + Filler(1400, "host") + "\",\"sniKey\":\"" + Filler(44, "sni") + "\"}");
        vAuth.Protect = true;
        var admitC1 = Admit(w1, 1, "c1");
        var addReader = Name(w1, 2, "reader", "add");
        var admitC2 = Admit(w2, 1, "c2");
        var u1 = Rule(w3, 1, GroupU, "default=inherit;reader=read,list");
        var x1 = Rule(w4, 1, EntryX, "default=inherit;reader=read,list,update");

        var basePath = Path.Combine(dir, "R-base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.Name = "Root"; Stamp(db.RootGroup, T0);
        var general = new PwGroup(true, false) { Name = "General" }; Stamp(general, T0); db.RootGroup.AddGroup(general, true);
        var ue = Plain("user entry", T0, Fixed(null, "user entry")); ue.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password")); general.AddEntry(ue, true);
        var gu = new PwGroup(false, false) { Uuid = GroupU, Name = "U" }; Stamp(gu, T0); db.RootGroup.AddGroup(gu, true);
        var ex = Plain("X", T0, EntryX); ex.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "x-secret-0")); ex.Strings.Set("KPG.Rule", new ProtectedString(false, x1.Json)); gu.AddEntry(ex, true);
        var ey = Plain("Y", T0, EntryY); ey.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "y-secret-0")); gu.AddEntry(ey, true);
        gu.AddEntry(Marker(gu, u1, T0), true);
        var kpg = new PwGroup(false, false) { Uuid = GroupKpg, Name = "KeePassGate", IconId = PwIcon.TerminalEncrypted, Notes = WriteNotes, EnableSearching = false, EnableAutoType = false, IsExpanded = false };
        Stamp(kpg, T0); db.RootGroup.AddGroup(kpg, true);
        kpg.AddEntry(WriteEntry(wAuth, "authority", vAuth), true);
        kpg.AddEntry(WriteEntry(w1, "pairing", admitC1, addReader), true);
        kpg.AddEntry(WriteEntry(w2, "pairing", admitC2), true);
        AddTwice(db, w3, "rule", u1);
        AddTwice(db, w4, "rule", x1);
        db.Save(null); db.Close();

        Func<string, PwDatabase> from = name => { var p = Path.Combine(dir, name + ".kdbx"); File.Copy(basePath, p, true); return Open(p); };
        Action<string, string> copy = (a, b) => File.Copy(Path.Combine(dir, a + ".kdbx"), Path.Combine(dir, b + ".kdbx"), true);

        // ---- set R, copy A1: revoke c1 and change X's rule
        // The revocation is written twice, takes along what else the record's entry held, and that entry goes.
        // The new rule is written twice and on the node, and the entries of the version it replaces go.
        var wr = NewW("wr", h1); var w5 = NewW("w5", h1);
        var revC1 = Revoke(wr, 1, "c1");
        var x2 = Rule(w5, 1, EntryX, "default=inherit;reader=read", x1);
        db = from("R-A1");
        AddTwice(db, wr, "revocation", revC1, addReader);
        Retire(db, w1);
        AddTwice(db, w5, "rule", x2);
        Retire(db, w4);
        var x = E(db, EntryX); x.CreateBackup(db); x.Strings.Set("KPG.Rule", new ProtectedString(false, x2.Json)); x.LastModificationTime = h1;
        db.Save(null); db.Close();

        // ---- set R, copy A2: A1 and then a tidy: one entry and its second copy carry what is in force, the rest goes
        var wt = NewW("tidy", h15);
        copy("R-A1", "R-A2");
        db = Open(Path.Combine(dir, "R-A2.kdbx"));
        AddTwice(db, wt, "tidy", admitC2, addReader, u1, x2, revC1);
        Retire(db, w2, w3, w5, wr);
        db.Save(null); db.Close();

        // ---- set R, copy B: the stale copy, with changes of its own
        var w6 = NewW("w6", h2); var w7 = NewW("w7", h2);
        var admitC4 = Admit(w6, 1, "c4");
        var u2 = Rule(w7, 1, GroupU, "default=deny;reader=list", u1);
        db = from("R-B");
        x = E(db, EntryX); x.CreateBackup(db); x.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "x-secret-B")); x.LastModificationTime = h2;
        Kpg(db).AddEntry(WriteEntry(w6, "pairing", admitC4), true);
        AddTwice(db, w7, "rule", u2);
        Retire(db, w3);
        var mk = E(db, MarkerUuid(GroupU)); mk.CreateBackup(db); mk.Strings.Set("KPG.GroupRule", new ProtectedString(false, u2.Json)); mk.LastModificationTime = h2;
        db.Save(null); db.Close();

        // ---- set C: two copies change the same rule; one removes a name while the other adds another
        var w8 = NewW("w8", h1); var wn = NewW("wn", h1); var w9 = NewW("w9", h2); var w10 = NewW("w10", h2);
        var xa = Rule(w8, 1, EntryX, "default=inherit;reader=read", x1);
        var remReader = Name(wn, 1, "reader", "remove", addReader);
        var xb = Rule(w9, 1, EntryX, "default=deny;reader=read,list;ops=read", x1);
        var admitC5 = Admit(w10, 1, "c5"); var addOps = Name(w10, 2, "ops", "add");
        db = from("C-A");
        AddTwice(db, w8, "rule", xa);
        Retire(db, w4);
        x = E(db, EntryX); x.CreateBackup(db); x.Strings.Set("KPG.Rule", new ProtectedString(false, xa.Json)); x.LastModificationTime = h1;
        AddTwice(db, wn, "profile removed", remReader, admitC1);
        Retire(db, w1);
        db.Save(null); db.Close();
        db = from("C-B");
        AddTwice(db, w9, "rule", xb);
        Retire(db, w4);
        x = E(db, EntryX); x.CreateBackup(db); x.Strings.Set("KPG.Rule", new ProtectedString(false, xb.Json)); x.LastModificationTime = h2;
        Kpg(db).AddEntry(WriteEntry(w10, "pairing", admitC5, addOps), true);
        db.Save(null); db.Close();

        // ---- set P: one copy removes a name and adds it back; the other, not knowing, removes it later
        var wp1 = NewW("wp1", h1); var wp2 = NewW("wp2", h2); var wp3 = NewW("wp3", h3);
        var remB = Name(wp1, 1, "reader", "remove", addReader);
        var addB2 = Name(wp2, 1, "reader", "add", remB);
        var remA = Name(wp3, 1, "reader", "remove", addReader);
        db = from("P-B");
        AddTwice(db, wp1, "profile removed", remB, admitC1);
        Retire(db, w1);
        Kpg(db).AddEntry(WriteEntry(wp2, "profile added", addB2, admitC1), true);
        Retire(db, wp1);
        db.Save(null); db.Close();
        db = from("P-A");
        AddTwice(db, wp3, "profile removed", remA, admitC1);
        Retire(db, w1);
        db.Save(null); db.Close();

        // ---- set H: damage by hand, on top of A1 or A2
        copy("R-A1", "H1"); db = Open(Path.Combine(dir, "H1.kdbx")); HandDelete(db, wr.Uuid, null); db.Save(null); db.Close();                                        // one of the revocation's two entries, no trace
        copy("R-A1", "H2"); db = Open(Path.Combine(dir, "H2.kdbx")); HandDelete(db, wr.Uuid, null); HandDelete(db, Twin(wr).Uuid, null); db.Save(null); db.Close();   // both, no trace
        copy("R-A2", "H3"); db = Open(Path.Combine(dir, "H3.kdbx")); HandDelete(db, wt.Uuid, null); db.Save(null); db.Close();                                        // one of the two tidy entries, no trace
        copy("R-A1", "H4"); db = Open(Path.Combine(dir, "H4.kdbx")); HandDelete(db, wr.Uuid, h3); db.Save(null); db.Close();                                          // one of the revocation's entries, with a deletion record
        copy("R-A2", "H5"); db = Open(Path.Combine(dir, "H5.kdbx")); HandDelete(db, wt.Uuid, null); HandDelete(db, Twin(wt).Uuid, null); db.Save(null); db.Close();   // both tidy entries, no trace
        copy("R-A2", "H6"); db = Open(Path.Combine(dir, "H6.kdbx"));                                                                                                  // the whole group deleted for good, as KeePass does: a record for each entry and the group
        foreach (var e in Kpg(db).Entries.ToList()) HandDelete(db, e.Uuid, h3);
        db.RootGroup.Groups.Remove(Kpg(db)); db.DeletedObjects.Add(new PwDeletedObject(GroupKpg, h3));
        db.Save(null); db.Close();

        // ---- set F: versions of another database planted in a marked entry, and a field edited by hand
        copy("R-A1", "F1"); db = Open(Path.Combine(dir, "F1.kdbx"));
        var wf = NewW("foreign", h2);
        var planted = Make(wf, 1, Foreign, "retire", "authority", "fpA");
        var plantedRule = Make(wf, 2, Foreign, "rule", "node", EntryY.ToHexString(), "doc", "default=inherit;reader=read,list,update,delete", "after", "none");
        db.RootGroup.FindGroup(GroupU, true).AddEntry(WriteEntry(wf, "planted", planted, plantedRule), true);
        db.Save(null); db.Close();
        copy("R-A1", "F2"); db = Open(Path.Combine(dir, "F2.kdbx"));
        foreach (var w in new[] { wr, Twin(wr) })
        {
            var c = E(db, w.Uuid); c.CreateBackup(db);
            c.Strings.Set(revC1.Name, new ProtectedString(false, revC1.Json.Replace("\"certificate\":\"c1\"", "\"certificate\":\"c9\""))); c.LastModificationTime = h3;
        }
        db.Save(null); db.Close();
    }

    // ---- which deletion time removes an unchanged entry in another copy ----------------------

    static void Sentinel(string dir)
    {
        Directory.CreateDirectory(dir);
        var w = NewW("sentinel", T0);
        var basePath = Path.Combine(dir, "S-base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.AddEntry(WriteEntry(w, "rule", Rule(w, 1, EntryX, "default=inherit;reader=read")), true);
        db.Save(null); db.Close();
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var deleted = Path.Combine(dir, "S-deleted" + offset + ".kdbx");
            File.Copy(basePath, deleted, true);
            db = Open(deleted);
            HandDelete(db, w.Uuid, T0.AddSeconds(offset));
            db.Save(null); db.Close();
            foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting })
            {
                foreach (var intoDeleted in new[] { true, false })
                {
                    var t = Open(intoDeleted ? deleted : basePath);
                    var s = Open(intoDeleted ? basePath : deleted);
                    t.MergeIn(s, method, null);
                    var outPath = Path.Combine(dir, "S-result.kdbx");
                    t.SaveAs(IOConnectionInfo.FromPath(outPath), false, null);
                    t.Close(); s.Close();
                    var r = Open(outPath);
                    Console.WriteLine("deletion record at the entry's time " + (offset >= 0 ? "+" : "") + offset + " s, " + method + ", "
                        + (intoDeleted ? "the copy that still holds the entry merged into the copy that deleted it" : "the copy that deleted it merged into the copy that still holds the entry")
                        + ": entry " + (r.RootGroup.FindEntry(w.Uuid, true) != null ? "PRESENT" : "gone") + ", deletion records " + r.DeletedObjects.UCount);
                    r.Close();
                }
            }
        }
    }

    // ---- duplicate a ruled group as KeePass's Duplicate Group does (PwGroup.Duplicate) ---------

    static void Dup(string input, string output)
    {
        var db = Open(input);
        var gu = db.RootGroup.FindGroup(GroupU, true);
        var copy = gu.Duplicate();
        copy.Name = "U - Copy";
        db.RootGroup.AddGroup(copy, true);
        db.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        db.Close();
    }

    static void NewIds(string target, string source, string output)
    {
        var t = Open(target); var s = Open(source);
        t.MergeIn(s, PwMergeMethod.CreateNewUuids, null);
        t.SaveAs(IOConnectionInfo.FromPath(output), true, null);
        Console.WriteLine("# after File then Import with new identifiers");
        Fold(t, Path.GetFileName(output), false);
        uint n = t.DeleteDuplicateEntries(null);
        t.Save(null);
        Console.WriteLine("# Delete Duplicate Entries deleted " + n);
        Fold(t, Path.GetFileName(output) + " after Delete Duplicate Entries", false);
        Fold(t, Path.GetFileName(output) + " after Delete Duplicate Entries, copies searched", true);
        t.Close(); s.Close();
    }

    // ---- reading: the fold ----------------------------------------------------------------

    class Seen { public Ver V; public PwEntry Carrier; public bool History; public string Kind; }

    static void FoldFile(string file)
    {
        var db = Open(file);
        Fold(db, Path.GetFileName(file) + " kdbx " + FormatVersion(file), false);
        db.Close();
    }

    static string FormatVersion(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    static Dictionary<string, HashSet<string>> ParseDoc(string doc, out string def)
    {
        def = "inherit";
        var d = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var part in doc.Split(';'))
        {
            var kv = part.Split('=');
            if (kv[0] == "default") def = kv[1];
            else d[kv[0]] = new HashSet<string>(kv[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }
        return d;
    }

    static string Meet(IEnumerable<string> docs)
    {
        string def = "inherit";
        Dictionary<string, HashSet<string>> acc = null;
        var all = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<Dictionary<string, HashSet<string>>>();
        foreach (var doc in docs)
        {
            string d; var p = ParseDoc(doc, out d);
            if (d == "deny") def = "deny";
            parsed.Add(p);
            foreach (var k in p.Keys) all.Add(k);
        }
        acc = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var k in all)
        {
            HashSet<string> words = null;
            foreach (var p in parsed)
            {
                HashSet<string> w;
                if (!p.TryGetValue(k, out w)) { words = new HashSet<string>(); break; }
                if (words == null) words = new HashSet<string>(w, StringComparer.Ordinal); else words.IntersectWith(w);
            }
            acc[k] = words ?? new HashSet<string>();
        }
        return "default=" + def + string.Concat(acc.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => ";" + k.Key + "=" + string.Join(",", k.Value.OrderBy(s => s, StringComparer.Ordinal))));
    }

    static void Fold(PwDatabase db, string label, bool searchCopies)
    {
        var o = Console.Out;
        o.WriteLine("# " + label);
        var seen = new List<Seen>();
        var pendingHistory = new List<Seen>();
        var damaged = new List<string>();
        var nodeNotes = new Dictionary<string, List<string>>(StringComparer.Ordinal);   // node uuid -> reasons to refuse
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        int writeEntries = 0, markers = 0, copies = 0, fields = 0;

        Action<string, string> refuse = (node, why) => { List<string> l; if (!nodeNotes.TryGetValue(node, out l)) nodeNotes[node] = l = new List<string>(); l.Add(why); };
        Action<PwEntry, PwEntry, bool> collect = (e, carrier, history) =>
        {
            foreach (var kv in e.Strings)
            {
                var m = VersionName.Match(kv.Key);
                if (!m.Success) continue;
                fields++;
                var v = Parse(kv.Value.ReadString());
                if (v == null || v.Id != m.Groups[1].Value) { damaged.Add(kv.Key + (history ? " (history)" : "")); seen.Add(new Seen { V = null, Carrier = carrier, History = history, Kind = kv.Key }); continue; }
                seen.Add(new Seen { V = v, Carrier = carrier, History = history, Kind = "write" });
            }
        };

        foreach (var g in db.RootGroup.GetGroups(true)) names[g.Uuid.ToHexString()] = "group " + g.Name;
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            names[e.Uuid.ToHexString()] = "entry " + e.Strings.ReadSafe(PwDefs.TitleField) + "@" + e.ParentGroup.Name;
            bool isW = HasMark(e.Uuid, MarkW);
            bool copy = !isW && e.Strings.Exists("KPG.Write");
            if (isW) writeEntries++;
            if (copy) copies++;
            if (isW || (copy && searchCopies))
            {
                collect(e, e, false);
                foreach (var h in e.History) collect(h, e, true);
                continue;
            }
            if (copy) continue;
            var own = e.Strings.Get("KPG.Rule");
            if (own != null)
            {
                var v = Parse(own.ReadString());
                var node = e.Uuid.ToHexString();
                if (v == null || v.Kind != "rule") refuse(node, "its rule field does not read");
                else if (v.Db != Home) refuse(node, "its rule field is another database's");
                else if (v.Get("node") != node) refuse(node, "copied from a ruled entry (" + Short(v.Get("node")) + ")");
                else seen.Add(new Seen { V = v, Carrier = e, History = false, Kind = "node" });
            }
            foreach (var h in e.History)
            {
                var old = h.Strings.Get("KPG.Rule") ?? h.Strings.Get("KPG.GroupRule");
                if (old == null) continue;
                var v = Parse(old.ReadString());
                // A version that closes counts from history too; one that does not read there is nothing.
                if (v != null && v.Kind == "rule" && v.Db == Home)
                {
                    var hs = new Seen { V = v, Carrier = e, History = true, Kind = "node" };
                    if (CondHist) pendingHistory.Add(hs); else seen.Add(hs);
                }
            }
            var grp = e.Strings.Get("KPG.GroupRule");
            if (grp != null)
            {
                markers++;
                var parent = e.ParentGroup.Uuid;
                var node = parent.ToHexString();
                bool isOwn = e.Uuid.Equals(MarkerUuid(parent));
                bool moved = !isOwn && HasMark(e.Uuid, MarkM);
                var v = Parse(grp.ReadString());
                if (moved) { /* another group's own marker dragged here: not this group's, ignored */ if (v != null && v.Db == Home) seen.Add(new Seen { V = v, Carrier = e, History = false, Kind = "node" }); }
                else if (v == null || v.Kind != "rule") refuse(node, "its marker does not read");
                else if (v.Db != Home) refuse(node, "its marker is another database's");
                else if (v.Get("node") != node) refuse(node, "copied from a ruled group (" + Short(v.Get("node")) + ")");
                else seen.Add(new Seen { V = v, Carrier = e, History = false, Kind = "node" });
            }
        }

        // The design's own rule (section 5.3): a node's history is read only when the node's copy is the
        // newest version found and no write entry holds it.
        if (CondHist)
        {
            var found = seen.Where(s => s.V != null).ToList();
            foreach (var grp2 in pendingHistory.GroupBy(h => h.V.Get("node")))
            {
                var node = grp2.Key;
                var copiesOnNode = found.Where(s => s.Kind == "node" && !s.History && s.V.Get("node") == node).Select(s => s.V).ToList();
                bool read = false;
                foreach (var v in copiesOnNode)
                {
                    bool replaced = found.Any(p => p.V.Replaces(v));
                    bool heldByWrite = found.Any(p => p.Kind == "write" && p.V.Id == v.Id
                        && !(Refined && db.DeletedObjects.Any(d => d.Uuid.Equals(p.Carrier.Uuid))));
                    if (!replaced && !heldByWrite) read = true;
                }
                o.WriteLine("  (design's history rule) node " + Short(node) + ": history " + (read ? "READ" : "not read") + (copiesOnNode.Count == 0 ? " (no copy on the node)" : ""));
                if (read) seen.AddRange(grp2);
            }
        }

        // One content per identifier.
        var byId = new Dictionary<string, Ver>(StringComparer.Ordinal);
        var where = new Dictionary<string, List<Seen>>(StringComparer.Ordinal);
        foreach (var s in seen.Where(s => s.V != null))
        {
            Ver have;
            if (byId.TryGetValue(s.V.Id, out have)) { if (have.Json != s.V.Json) damaged.Add("KPG." + s.V.Id + " (two contents)"); }
            else byId[s.V.Id] = s.V;
            List<Seen> l; if (!where.TryGetValue(s.V.Id, out l)) where[s.V.Id] = l = new List<Seen>(); l.Add(s);
        }
        // A field that does not read is only damage when no other carrier holds the same version whole.
        damaged = damaged.Where(d => { var m = VersionName.Match(d.Split(' ')[0]); return !(m.Success && byId.ContainsKey(m.Groups[1].Value) && !d.EndsWith("(two contents)")); }).Distinct().ToList();

        var home = byId.Values.Where(v => v.Db == Home).ToList();
        int foreign = byId.Values.Count(v => v.Db != Home);

        // Deletion records naming write entries.
        int ownRecords = 0; var hand = new List<PwUuid>();
        foreach (var d in db.DeletedObjects)
        {
            if (!HasMark(d.Uuid, MarkW)) continue;
            if (d.DeletionTime.ToUniversalTime() == StampOf(d.Uuid).AddSeconds(1)) ownRecords++; else hand.Add(d.Uuid);
        }
        var handPresent = new HashSet<string>(hand.Where(u => db.RootGroup.FindEntry(u, true) != null).Select(u => u.ToHexString()));

        var named = new HashSet<string>(db.DeletedObjects.Where(d => HasMark(d.Uuid, MarkW)).Select(d => d.Uuid.ToHexString()), StringComparer.Ordinal);
        Func<Ver, IEnumerable<Ver>, bool> superseded = (v, peers) => peers.Any(p => p.Replaces(v));
        // What opens counts only from a current field of an entry that no deletion record names.
        Func<Ver, bool> grantCounts = v => where[v.Id].Any(s => !s.History && s.Kind == "write" && !named.Contains(s.Carrier.Uuid.ToHexString()));
        Func<Ver, string> carriers = v => string.Join(", ", where[v.Id].Select(s => (s.Kind == "node" ? "node " : "") + "'" + s.Carrier.Strings.ReadSafe(PwDefs.TitleField) + "'" + (s.History ? " history" : "")).Distinct());

        o.WriteLine("  write entries " + writeEntries + " (version fields " + fields + "), markers " + markers + ", copies of write entries under other identifiers " + copies
            + ", deletion records: the plugin's own " + ownRecords + ", made elsewhere " + hand.Count + (hand.Count > 0 ? " (entry present again: " + handPresent.Count + ")" : "")
            + ", versions of another database " + foreign);
        if (damaged.Count > 0) o.WriteLine("  DAMAGED, serves nothing: " + string.Join("; ", damaged));
        if (hand.Count > 0) o.WriteLine("  CLOSED until accepted on the tab: " + hand.Count + " write entr" + (hand.Count == 1 ? "y was" : "ies were") + " deleted outside the plugin");

        // Authorities.
        var retired = new HashSet<string>(home.Where(v => v.Kind == "retire").Select(v => v.Get("authority")), StringComparer.Ordinal);
        foreach (var a in home.Where(v => v.Kind == "authority").OrderBy(v => v.Id, StringComparer.Ordinal))
            o.WriteLine("  authority " + a.Get("authority") + ": " + (retired.Contains(a.Get("authority")) ? "retired" : (grantCounts(a) ? "in service" : "not counted (its entry was deleted elsewhere)")) + "  [" + carriers(a) + "]");

        // Clients.
        var certs = new SortedSet<string>(home.Where(v => v.Kind == "admit" || v.Kind == "revoke").Select(v => v.Get("certificate")), StringComparer.Ordinal);
        foreach (var c in certs)
        {
            var admits = home.Where(v => v.Kind == "admit" && v.Get("certificate") == c).ToList();
            var revokes = home.Where(v => v.Kind == "revoke" && v.Get("certificate") == c).ToList();
            string verdict;
            if (revokes.Count > 0) verdict = "REFUSED, revoked  [revocation in: " + string.Join(" | ", revokes.Select(carriers)) + "]" + (admits.Count > 0 ? "  [record in: " + string.Join(" | ", admits.Select(carriers)) + "]" : "  [no record]");
            else if (admits.Any(grantCounts)) verdict = "admitted  [record in: " + string.Join(" | ", admits.Select(carriers)) + "]";
            else verdict = "refused, no record that counts  [only in: " + string.Join(" | ", admits.Select(carriers)) + "]";
            o.WriteLine("  client " + c + ": " + verdict);
        }

        // Names.
        var nameVers = home.Where(v => v.Kind == "name").ToList();
        var list = new List<string>();
        foreach (var n in nameVers.Select(v => v.Get("name")).Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            var adds = nameVers.Where(v => v.Get("name") == n && v.Get("act") == "add").ToList();
            var removes = nameVers.Where(v => v.Get("name") == n && v.Get("act") == "remove").ToList();
            bool on = adds.Any(a => grantCounts(a) && !removes.Any(r => r.Replaces(a)) && removes.All(r => a.Replaces(r)));
            o.WriteLine("  profile " + n + ": " + (on ? "on the list" : "OFF the list") + "  (additions " + adds.Count + ", removals " + removes.Count + ")");
        }

        // Rules.
        var ruleVers = home.Where(v => v.Kind == "rule").ToList();
        var nodes = new SortedSet<string>(ruleVers.Select(v => v.Get("node")).Concat(nodeNotes.Keys), StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            string name; if (!names.TryGetValue(node, out name)) name = "(node not in the file) " + Short(node);
            var vs = ruleVers.Where(v => v.Get("node") == node).ToList();
            var newest = vs.Where(v => !superseded(v, vs)).ToList();
            List<string> why;
            string verdict;
            if (nodeNotes.TryGetValue(node, out why)) verdict = "REFUSES: " + string.Join("; ", why);
            else if (newest.Count == 0) verdict = "no rule";
            else if (newest.Count == 1) verdict = newest[0].Get("doc");
            else verdict = "two versions, what both allow: " + Meet(newest.Select(v => v.Get("doc")));
            o.WriteLine("  rule on " + name + ": " + verdict + (newest.Count > 0 ? "  [newest in: " + string.Join(" | ", newest.Select(carriers)) + "]" : "") + "  (versions " + vs.Count + ")");
        }
    }

    static string Short(string hex) { return hex == null ? "-" : hex.Substring(0, Math.Min(8, hex.Length)); }

    // ---- dump -----------------------------------------------------------------------------

    static string Tm(DateTime t) { return t.ToUniversalTime().ToString("MM-dd'T'HH:mm:ss"); }

    static void Dump(string file)
    {
        var db = Open(file);
        Console.WriteLine("# " + Path.GetFileName(file) + " kdbx " + FormatVersion(file) + " dbCustomData=[" + string.Join(",", db.CustomData.Select(kv => kv.Key)) + "]");
        foreach (var d in db.DeletedObjects.Select(d => "  deleted " + d.Uuid.ToHexString().Substring(0, 16) + " @" + Tm(d.DeletionTime)).OrderBy(s => s, StringComparer.Ordinal)) Console.WriteLine(d);
        DumpGroup(db.RootGroup, "");
        db.Close();
    }

    static string StringsOf(PwEntry e)
    {
        var parts = new List<string>();
        foreach (var kv in e.Strings)
        {
            if (kv.Key == PwDefs.TitleField) continue;
            var text = kv.Value.ReadString();
            if (text.Length == 0 && PwDefs.IsStandardField(kv.Key)) continue;
            parts.Add(kv.Key + (kv.Value.IsProtected ? "[P]" : "") + "(" + text.Length + ")");
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join(" ", parts);
    }

    static void DumpGroup(PwGroup g, string path)
    {
        var p = (path + "/" + g.Name).TrimStart('/');
        Console.WriteLine("  G '" + p + "' u=" + g.Uuid.ToHexString().Substring(0, 8) + " mod=" + Tm(g.LastModificationTime)
            + " search=" + (g.EnableSearching.HasValue ? g.EnableSearching.Value.ToString() : "inherit") + " autotype=" + (g.EnableAutoType.HasValue ? g.EnableAutoType.Value.ToString() : "inherit"));
        foreach (var e in g.Entries.OrderBy(x => x.Strings.ReadSafe(PwDefs.TitleField), StringComparer.Ordinal))
        {
            Console.WriteLine("    E '" + e.Strings.ReadSafe(PwDefs.TitleField) + "' u=" + e.Uuid.ToHexString().Substring(0, 16) + " mod=" + Tm(e.LastModificationTime) + " :: " + StringsOf(e));
            foreach (var h in e.History) Console.WriteLine("      history @" + Tm(h.LastModificationTime) + " :: " + StringsOf(h));
        }
        foreach (var s in g.Groups.OrderBy(x => x.Name, StringComparer.Ordinal)) DumpGroup(s, p);
    }

    // ---- cost -----------------------------------------------------------------------------

    static void Walk()
    {
        foreach (var size in new[] { 500, 2500 })
        {
            var db = new PwDatabase();
            db.New(IOConnectionInfo.FromPath(Path.Combine(Path.GetTempPath(), "kpg-vf-walk-never-saved.kdbx")), Key());
            var rnd = new Random(1);
            int perGroup = 20, tombstones = 5000;
            var userEntries = new List<PwEntry>();
            for (int i = 0; i < size; i++)
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
                    userEntries.Add(e);
                }
            }
            for (int i = 0; i < tombstones; i++) db.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), DateTime.UtcNow));
            var store = new PwGroup(false, false) { Uuid = GroupKpg, Name = "KeePassGate" };
            db.RootGroup.AddGroup(store, true);
            var now = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
            // A store after a tidy: one authority, one tidy entry carrying 20 records, 32 names and 60 rules,
            // 12 standing revocations in entries of their own, 16 writes since the tidy.
            var wa = NewW("walk-auth", now);
            var va = Make(wa, 1, Home, "authority", "authority", "fpA", "doc", Filler(2900, "auth")); va.Protect = true;
            store.AddEntry(WriteEntry(wa, "authority", va), true);
            var wt = NewW("walk-tidy", now);
            var carried = new List<Ver> { Make(wt, 1, Home, "rule", "node", userEntries[1].Uuid.ToHexString(), "doc", "default=inherit;reader=read", "after", string.Join(" ", Enumerable.Range(0, 200).Select(i => Hash16("old" + i)))) };
            for (int i = 0; i < 20; i++) carried.Add(Admit(NewW("walk-admit" + i, now), 1, "c" + i));
            for (int i = 0; i < 32; i++) carried.Add(Name(NewW("walk-name" + i, now), 1, "p" + i, "add"));
            for (int i = 0; i < 60; i++) carried.Add(Rule(NewW("walk-rule" + i, now), 1, userEntries[i * 7].Uuid, "default=inherit;reader=read,list;ops=read,update"));
            store.AddEntry(WriteEntry(wt, "tidy", carried.ToArray()), true);
            for (int i = 0; i < 12; i++) { var w = NewW("walk-rev" + i, now); store.AddEntry(WriteEntry(w, "revocation", Revoke(w, 1, "r" + i)), true); }
            for (int i = 0; i < 16; i++) { var w = NewW("walk-new" + i, now.AddMinutes(i + 1)); store.AddEntry(WriteEntry(w, "rule", Rule(w, 1, userEntries[i * 11 + 3].Uuid, "default=deny;reader=list")), true); }
            for (int i = 0; i < 60; i++) userEntries[i * 7].Strings.Set("KPG.Rule", new ProtectedString(false, carried[53 + i].Json));

            Console.WriteLine("entries " + db.RootGroup.GetEntriesCount(true) + ", groups " + db.RootGroup.GetGroups(true).UCount + ", deletion records " + db.DeletedObjects.UCount + ", write entries " + store.Entries.UCount);

            // (a) find the plugin's entries by the first four bytes of each identifier, and the deletion records that carry the mark
            Func<List<PwEntry>> find = delegate
            {
                var found = new List<PwEntry>();
                var stack = new Stack<PwGroup>();
                stack.Push(db.RootGroup);
                while (stack.Count > 0)
                {
                    var g = stack.Pop();
                    var entries = g.Entries;
                    for (uint i = 0; i < entries.UCount; i++) { var e = entries.GetAt(i); if (HasMark(e.Uuid, MarkW)) found.Add(e); }
                    var below = g.Groups;
                    for (uint i = 0; i < below.UCount; i++) stack.Push(below.GetAt(i));
                }
                int marked = 0;
                var deleted = db.DeletedObjects;
                for (uint i = 0; i < deleted.UCount; i++) { if (HasMark(deleted.GetAt(i).Uuid, MarkW)) marked++; }
                return found;
            };
            // (b) the same, then every version field's name and stored object, compared with what the last request saw
            List<KeyValuePair<string, ProtectedString>> last = null;
            Func<bool> unchanged = delegate
            {
                var now2 = new List<KeyValuePair<string, ProtectedString>>();
                foreach (var e in find())
                {
                    foreach (var kv in e.Strings) if (kv.Key.StartsWith("KPG.", StringComparison.Ordinal)) now2.Add(kv);
                    foreach (var h in e.History) foreach (var kv in h.Strings) if (kv.Key.StartsWith("KPG.", StringComparison.Ordinal)) now2.Add(kv);
                }
                bool same = last != null && last.Count == now2.Count;
                for (int i = 0; same && i < now2.Count; i++) same = ReferenceEquals(last[i].Value, now2[i].Value) && last[i].Key == now2[i].Key;
                last = now2;
                return same;
            };
            // (c) reading and checking every version from nothing: JSON, checksum
            Func<int> parseAll = delegate
            {
                int n = 0;
                foreach (var e in find())
                    foreach (var kv in e.Strings)
                        if (VersionName.IsMatch(kv.Key)) { if (Parse(kv.Value.ReadString()) != null) n++; }
                return n;
            };
            // (d) one lookup of the node's own copy for each user entry, which a full listing makes
            Func<int> nodeCopies = delegate { int n = 0; foreach (var e in userEntries) if (e.Strings.Get("KPG.Rule") != null) n++; return n; };

            int reps = 200;
            for (int i = 0; i < 20; i++) { find(); unchanged(); }
            var sw = Stopwatch.StartNew(); for (int i = 0; i < reps; i++) find(); sw.Stop();
            Console.WriteLine("  find the plugin's entries and deletion records by identifier: " + (sw.Elapsed.TotalMilliseconds / reps).ToString("0.000") + " ms");
            sw.Restart(); bool u = false; for (int i = 0; i < reps; i++) u = unchanged(); sw.Stop();
            Console.WriteLine("  the same, and compare every version field with the last request's (unchanged: " + u + "): " + (sw.Elapsed.TotalMilliseconds / reps).ToString("0.000") + " ms");
            parseAll();
            sw.Restart(); int parsed = 0; for (int i = 0; i < 20; i++) parsed = parseAll(); sw.Stop();
            Console.WriteLine("  read and check every version from nothing (" + parsed + " versions, the protected authority included): " + (sw.Elapsed.TotalMilliseconds / 20).ToString("0.000") + " ms");
            sw.Restart(); int copies = 0; for (int i = 0; i < reps; i++) copies = nodeCopies(); sw.Stop();
            Console.WriteLine("  one lookup of KPG.Rule on each user entry (" + copies + " found): " + (sw.Elapsed.TotalMilliseconds / reps).ToString("0.000") + " ms");
        }
    }
}
