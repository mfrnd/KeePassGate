using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Critique probe for option-rules-central.md, lens "survival across programs".
// Invented databases only, under the work directory given first. No window: KeePassLib types,
// keepassxc-cli and pykeepass as console children.
//
// The store is the design's (section 2.2): rule versions as entries in a group with a fixed UUID,
// UUID = mark(4) | node tag(6) | digest(6), strings KPG.Kind, KPG.Node, KPG.After ("none" for a
// first rule), KPG.Data. The read rule is the design's section 3. The repair is section 5.2: one
// version whose KPG.After names every newest version, every entry changed by hand and every
// unanswered deletion record carrying the node's tag, holding what the table shows, which in the
// refusing state is "the newest version that still reads"; then the entries changed by hand are
// deleted.
//
// What answers for KeePassDX 4.5.8 here is a model, not the program: DxMerge follows
// DatabaseKDBXMerger.kt at tag 4.5.8 (merge :190-312, deleteEntry :317-323, deleteGroup
// :358-395, getAttachedParent :439-458, mergeEntry :463-527, mergeGroup :557-593) for entries,
// groups and deletion records. Every line it prints is labeled "DX-model".
static class P
{
    static string Work;
    static TextWriter Out;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Pw = "probe-only";
    static string Xc28, Xc242, Uv, PkScript;

    const string KindField = "KPG.Kind";
    const string NodeField = "KPG.Node";
    const string AfterField = "KPG.After";
    const string DataField = "KPG.Data";
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    // agent's reach: U grants read, list, update; X's first rule narrows to read, list; its second
    // to list only.
    const string RU = "{\"default\":\"inherit\",\"agent\":[\"read\",\"update\",\"list\"]}";
    const string RU2 = "{\"default\":\"inherit\",\"agent\":[\"create\",\"read\",\"update\",\"list\"]}";
    const string RX1 = "{\"default\":\"inherit\",\"agent\":[\"read\",\"list\"]}";
    const string RX2 = "{\"default\":\"inherit\",\"agent\":[\"list\"]}";
    const string NoRule = "{\"default\":\"inherit\"}";

    static readonly byte[] Mark = First(Sha("KPG/1/rule/"), 4);
    static readonly PwUuid StoreUuid = new PwUuid(First(Sha("KPG/1/group/"), 16));

    static byte[] Sha(string text) { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text)); }
    static byte[] First(byte[] from, int count) { var b = new byte[count]; Array.Copy(from, b, count); return b; }
    static DateTime At(int s) { return T0.AddSeconds(s); }
    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }
    static PwUuid Fixed(string name) { return new PwUuid(First(Sha("probe " + name), 16)); }
    static string Hex(PwUuid u) { return u.ToHexString(); }
    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }
    static void Set(PwEntry e, string name, string value) { e.Strings.Set(name, new ProtectedString(false, value)); }

    static PwGroup AddGroup(PwGroup parent, string name, PwUuid uuid, DateTime t)
    {
        var g = new PwGroup(false, false); g.Uuid = uuid; g.Name = name; Stamp(g, t);
        parent.AddGroup(g, true); g.LocationChanged = t; return g;
    }

    static PwEntry AddEntry(PwGroup parent, string uuidName, string title, DateTime t)
    {
        var e = new PwEntry(false, false); e.Uuid = Fixed("entry " + uuidName);
        Set(e, PwDefs.TitleField, title); e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-" + title + "-0"));
        Stamp(e, t); parent.AddEntry(e, true); e.LocationChanged = t; return e;
    }

    static void Edit(PwEntry e, DateTime t, Action<PwEntry> change) { e.CreateBackup(null); change(e); e.LastModificationTime = t; }

    static PwGroup Store(PwDatabase db) { return db.RootGroup.FindGroup(StoreUuid, true); }

    // The design makes the same group again with the first rule (section 2.1), and the group of
    // option-store-entries.md section 5 removes a deletion record naming it when it does.
    static PwGroup EnsureStore(PwDatabase db, DateTime t)
    {
        var s = Store(db);
        if (s != null) return s;
        s = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, t);
        s.EnableSearching = false; s.EnableAutoType = false; s.Notes = NotesLine;
        for (var i = (int)db.DeletedObjects.UCount - 1; i >= 0; i--)
            if (db.DeletedObjects.GetAt((uint)i).Uuid.Equals(StoreUuid)) db.DeletedObjects.RemoveAt((uint)i);
        return s;
    }

    static bool Marked(PwUuid u) { var b = u.UuidBytes; return b[0] == Mark[0] && b[1] == Mark[1] && b[2] == Mark[2] && b[3] == Mark[3]; }
    static string TagOfBytes(byte[] uuid) { var t = new byte[6]; Array.Copy(uuid, 4, t, 0, 6); return MemUtil.ByteArrayToHexString(t); }
    static string TagOfNode(PwUuid node) { return Hex(node).Substring(0, 12); }

    static PwUuid VersionId(string nodeHex, string afterText, string data)
    {
        if (nodeHex == null || nodeHex.Length != 32) return null;
        byte[] node;
        try { node = MemUtil.HexStringToByteArray(nodeHex); } catch (Exception) { return null; }
        if (node == null || node.Length != 16) return null;
        var d = Sha("KPG/1/rule/" + nodeHex + "/" + afterText + "/" + data);
        var b = new byte[16];
        Array.Copy(Mark, 0, b, 0, 4); Array.Copy(node, 0, b, 4, 6); Array.Copy(d, 0, b, 10, 6);
        return new PwUuid(b);
    }

    static string AfterText(IEnumerable<string> after)
    {
        var list = new List<string>(after ?? new string[0]);
        list.Sort(StringComparer.Ordinal);
        return list.Count == 0 ? "none" : string.Join(" ", list.ToArray());
    }

    static string AddVersion(PwDatabase db, PwUuid node, string data, IEnumerable<string> after, DateTime t)
    {
        var afterText = AfterText(after);
        var id = VersionId(Hex(node), afterText, data);
        var store = EnsureStore(db, t);
        if (db.RootGroup.FindEntry(id, true) != null) return Hex(id);
        var e = new PwEntry(false, false);
        e.Uuid = id;
        Set(e, PwDefs.TitleField, "KPG rule " + Hex(node).Substring(0, 8));
        Set(e, PwDefs.UserNameField, string.Empty);
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, string.Empty));
        Set(e, PwDefs.UrlField, string.Empty);
        Set(e, PwDefs.NotesField, NotesLine);
        Set(e, KindField, "rule"); Set(e, NodeField, Hex(node)); Set(e, AfterField, afterText); Set(e, DataField, data);
        Stamp(e, t); store.AddEntry(e, true); e.LocationChanged = t;
        return Hex(id);
    }

    static PwEntry ById(PwDatabase db, string id) { return db.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(id)), true); }

    // KeePass's own permanent delete of an entry: MainForm_Functions.cs:5154-5158 at 2.62.
    static void DeleteForGood(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    // ---- the design's read rule, section 3 ----

    sealed class Ver { public string Id, Node, Data; public string[] After; public bool Copy; }

    sealed class View
    {
        public Dictionary<string, List<Ver>> ByNode = new Dictionary<string, List<Ver>>(StringComparer.Ordinal);
        public Dictionary<string, string> Damage = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> HandChanged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Unanswered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public HashSet<string> Present = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Named = new HashSet<string>(StringComparer.Ordinal);
        public int Versions, Copies, Records;
    }

    static void Walk(PwGroup g, Action<PwEntry> each) { foreach (var e in g.Entries) each(e); foreach (var s in g.Groups) Walk(s, each); }
    static void Flag(View v, string tag, string reason) { if (!v.Damage.ContainsKey(tag)) v.Damage[tag] = reason; }
    static void AddTo(Dictionary<string, List<string>> d, string k, string v) { List<string> l; if (!d.TryGetValue(k, out l)) { l = new List<string>(); d[k] = l; } l.Add(v); }

    static View Read(PwDatabase db)
    {
        var v = new View();
        Walk(db.RootGroup, e =>
        {
            var marked = Marked(e.Uuid);
            var kind = e.Strings.ReadSafe(KindField);
            if (!marked && kind != "rule") return;
            var node = e.Strings.ReadSafe(NodeField);
            var after = e.Strings.ReadSafe(AfterField);
            var data = e.Strings.ReadSafe(DataField);
            var derived = VersionId(node, after, data);
            if (marked && (derived == null || !derived.Equals(e.Uuid) || kind != "rule"))
            {
                var tag = TagOfBytes(e.Uuid.UuidBytes);
                Flag(v, tag, "a rule entry was changed by hand");
                AddTo(v.HandChanged, tag, Hex(e.Uuid));
                if (node.Length == 32) Flag(v, node.Substring(0, 12), "a rule entry was changed by hand");
                return;
            }
            if (derived == null) return;
            var ver = new Ver();
            ver.Id = Hex(derived); ver.Node = node; ver.Data = data; ver.Copy = !marked;
            ver.After = after == "none" ? new string[0] : after.Split(' ');
            if (ver.Copy) v.Copies++; else v.Versions++;
            List<Ver> rows;
            if (!v.ByNode.TryGetValue(node, out rows)) { rows = new List<Ver>(); v.ByNode[node] = rows; }
            rows.Add(ver);
            v.Present.Add(ver.Id);
            foreach (var a in ver.After) v.Named.Add(a);
        });
        foreach (var d in db.DeletedObjects)
        {
            if (!Marked(d.Uuid)) continue;
            v.Records++;
            var id = Hex(d.Uuid);
            if (v.Present.Contains(id) || v.Named.Contains(id)) continue;
            var tag = TagOfBytes(d.Uuid.UuidBytes);
            Flag(v, tag, "a rule entry was deleted");
            AddTo(v.Unanswered, tag, id);
        }
        return v;
    }

    static List<Ver> Heads(View v, string nodeHex)
    {
        var heads = new List<Ver>();
        List<Ver> rows;
        if (!v.ByNode.TryGetValue(nodeHex, out rows)) return heads;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rows) if (!v.Named.Contains(r.Id) && seen.Add(r.Id)) heads.Add(r);
        return heads;
    }

    static readonly Regex Member = new Regex("\"([^\"]+)\":(?:\"([^\"]*)\"|\\[([^\\]]*)\\])");

    static Dictionary<string, List<string>> Parse(string doc, out bool deny)
    {
        deny = false;
        var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match m in Member.Matches(doc))
        {
            var name = m.Groups[1].Value;
            if (name == "default") { deny = m.Groups[2].Value == "deny"; continue; }
            var words = new List<string>();
            foreach (var w in m.Groups[3].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) words.Add(w.Trim().Trim('"'));
            d[name] = words;
        }
        return d;
    }

    static string Meet(List<string> docs)
    {
        var anyDeny = false;
        var per = new List<Dictionary<string, List<string>>>();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var doc in docs) { bool deny; var d = Parse(doc, out deny); if (deny) anyDeny = true; per.Add(d); foreach (var k in d.Keys) names.Add(k); }
        var sb = new StringBuilder("{\"default\":\"" + (anyDeny ? "deny" : "inherit") + "\"");
        foreach (var name in names)
        {
            List<string> kept = null; var all = true;
            foreach (var d in per)
            {
                List<string> words;
                if (!d.TryGetValue(name, out words)) { all = false; break; }
                if (kept == null) kept = new List<string>(words); else kept.RemoveAll(w => !words.Contains(w));
            }
            if (!all) kept = new List<string>();
            sb.Append(",\"").Append(name).Append("\":[");
            for (var i = 0; i < kept.Count; i++) sb.Append(i == 0 ? "\"" : ",\"").Append(kept[i]).Append("\"");
            sb.Append("]");
        }
        return sb.Append("}").ToString();
    }

    // What the node's own rule is: null for silence, "!reason" for a refusal, else a document.
    static string DocAt(View v, PwUuid node)
    {
        string reason;
        if (v.Damage.TryGetValue(TagOfNode(node), out reason)) return "!" + reason;
        var hex = Hex(node);
        if (!v.ByNode.ContainsKey(hex)) return null;
        var heads = Heads(v, hex);
        if (heads.Count == 0) return "!no version is the newest";
        if (heads.Count == 1) return heads[0].Data;
        var docs = new List<string>(); foreach (var h in heads) docs.Add(h.Data);
        return Meet(docs);
    }

    // What the gate would give the profile "agent" at an entry: nearest wins, per profile.
    static string Agent(PwDatabase db, View v, PwUuid entryUuid)
    {
        var e = db.RootGroup.FindEntry(entryUuid, true);
        if (e == null) return "entry absent";
        var chain = new List<KeyValuePair<PwUuid, string>>();
        chain.Add(new KeyValuePair<PwUuid, string>(e.Uuid, e.Strings.ReadSafe(PwDefs.TitleField)));
        for (var g = e.ParentGroup; g != null; g = g.ParentGroup) chain.Add(new KeyValuePair<PwUuid, string>(g.Uuid, g.Name));
        var prefix = v.Copies > 0 ? "[whole database 503 undecided: " + v.Copies.ToString(Inv) + " copies] " : string.Empty;
        foreach (var n in chain)
        {
            var doc = DocAt(v, n.Key);
            if (doc == null) continue;
            if (doc.StartsWith("!", StringComparison.Ordinal)) return prefix + "REFUSED at " + n.Value + " (" + doc.Substring(1) + ")";
            bool deny;
            var d = Parse(doc, out deny);
            List<string> words;
            if (d.TryGetValue("agent", out words)) return prefix + "agent: [" + string.Join(",", words.ToArray()) + "] from " + n.Value;
            if (deny) return prefix + "agent: nothing (sealed at " + n.Value + ")";
        }
        return prefix + "agent: nothing (no rule grants it)";
    }

    static string Show(PwDatabase db, PwUuid x)
    {
        var v = Read(db);
        var s = Store(db);
        return Agent(db, v, x) + " | store " + (s == null ? "ABSENT" : "present") + ", " + v.Versions.ToString(Inv) + " versions, "
            + v.Records.ToString(Inv) + " deletion records of rule entries, " + v.Copies.ToString(Inv) + " copies";
    }

    static string Show(string path, PwUuid x) { var db = Open(path); try { return Show(db, x); } finally { db.Close(); } }

    // ---- the repair of section 5.2 ----

    static string Repair(PwDatabase db, PwUuid node, DateTime t)
    {
        var v = Read(db);
        var hex = Hex(node);
        var tag = TagOfNode(node);
        var heads = Heads(v, hex);
        string content;
        if (heads.Count == 0) content = NoRule;
        else if (heads.Count == 1) content = heads[0].Data;
        else { var docs = new List<string>(); foreach (var h in heads) docs.Add(h.Data); content = Meet(docs); }
        var after = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var h in heads) after.Add(h.Id);
        List<string> l;
        if (v.HandChanged.TryGetValue(tag, out l)) foreach (var id in l) after.Add(id);
        if (v.Unanswered.TryGetValue(tag, out l)) foreach (var id in l) after.Add(id);
        var newId = AddVersion(db, node, content, after, t);
        if (v.HandChanged.TryGetValue(tag, out l)) foreach (var id in l) { var e = ById(db, id); if (e != null) DeleteForGood(db, e, t); }
        return "repair wrote " + content + " naming " + after.Count.ToString(Inv) + " predecessor(s), as " + newId.Substring(0, 8);
    }

    // ---- files ----

    static PwDatabase NewDb()
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Work, "new.kdbx")), Key());
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, 2UL); a.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL); a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        db.KdfParameters = a;
        db.SettingsChanged = T0;
        db.RootGroup.Uuid = Fixed("root"); db.RootGroup.Name = "probe"; Stamp(db.RootGroup, T0);
        return db;
    }

    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }
    static string F(string chain, string name) { var d = Path.Combine(Work, chain); Directory.CreateDirectory(d); return Path.Combine(d, name); }

    static PwUuid UX { get { return Fixed("entry X"); } }
    static PwUuid UY { get { return Fixed("entry Y"); } }
    static PwUuid UU { get { return Fixed("group U"); } }

    // root > U (rule RU) > X (rule RX1, and RX2 after it when asked), Y; root > W > Q; root > KeePassGate.
    static Dictionary<string, string> Base(string path, bool second)
    {
        var db = NewDb();
        var u = AddGroup(db.RootGroup, "U", UU, T0);
        var w = AddGroup(db.RootGroup, "W", Fixed("group W"), T0);
        EnsureStore(db, T0);
        var x = AddEntry(u, "X", "X", T0);
        AddEntry(u, "Y", "Y", T0);
        AddEntry(w, "Q", "Q", T0);
        var ids = new Dictionary<string, string>();
        ids["U1"] = AddVersion(db, u.Uuid, RU, null, T0);
        ids["X1"] = AddVersion(db, x.Uuid, RX1, null, T0);
        if (second) ids["X2"] = AddVersion(db, x.Uuid, RX2, new[] { ids["X1"] }, At(5));
        SaveTo(db, path);
        return ids;
    }

    static string Derive(string from, string to, Action<PwDatabase> act) { var db = Open(from); act(db); SaveTo(db, to); return to; }

    static string KpMerge(string target, string source, PwMergeMethod m, string to)
    {
        var t = Open(target); var s = Open(source); t.MergeIn(s, m, null); s.Close(); SaveTo(t, to); return to;
    }

    // ---- KeePassDX 4.5.8's merge, as a model of its source ----

    static PwDeletedObject RecordIn(PwDatabase db, PwUuid u) { foreach (var d in db.DeletedObjects) if (d.Uuid.Equals(u)) return d; return null; }

    static PwGroup AttachedParent(PwDatabase open, PwGroup arrivingParent)
    {
        if (arrivingParent == null) return open.RootGroup;
        if (arrivingParent.Uuid.Equals(open.RootGroup.Uuid)) return open.RootGroup;
        var have = open.RootGroup.FindGroup(arrivingParent.Uuid, true);
        if (have != null) return have;
        var g = new PwGroup(false, false); g.Uuid = arrivingParent.Uuid; g.AssignProperties(arrivingParent, false, true);
        AttachedParent(open, arrivingParent.ParentGroup).AddGroup(g, true);
        return g;
    }

    static void DxGroup(PwDatabase open, PwGroup src)
    {
        var have = open.RootGroup.FindGroup(src.Uuid, true);
        var rec = RecordIn(open, src.Uuid);
        var parent = AttachedParent(open, src.ParentGroup);
        if (have == null)
        {
            if (rec == null || rec.DeletionTime < src.LastModificationTime)
            {
                var g = new PwGroup(false, false); g.Uuid = src.Uuid; g.AssignProperties(src, false, true); parent.AddGroup(g, true);
            }
        }
        else if (have.LastModificationTime < src.LastModificationTime) have.AssignProperties(src, false, true);
    }

    static void DxEntry(PwDatabase open, PwEntry src)
    {
        var have = open.RootGroup.FindEntry(src.Uuid, true);
        var rec = RecordIn(open, src.Uuid);
        var parent = AttachedParent(open, src.ParentGroup);
        if (have == null)
        {
            if (rec == null || rec.DeletionTime < src.LastModificationTime) parent.AddEntry(src.CloneDeep(), true);
        }
        else if (have.LastModificationTime < src.LastModificationTime)
        {
            have.AssignProperties(src, false, true, true);
            if (have.ParentGroup != parent) { have.ParentGroup.Entries.Remove(have); parent.AddEntry(have, true); }
        }
    }

    static void DxWalk(PwDatabase open, PwGroup g)
    {
        foreach (var e in g.Entries) DxEntry(open, e);
        foreach (var s in g.Groups) { DxGroup(open, s); DxWalk(open, s); }
    }

    static void DxMerge(PwDatabase open, PwDatabase arriving)
    {
        DxWalk(open, arriving.RootGroup);
        var named = new HashSet<PwUuid>();
        foreach (var d in arriving.DeletedObjects) named.Add(d.Uuid);
        foreach (var d in arriving.DeletedObjects)
        {
            // deleteEntry: removed when the record is later; no record is added to the open copy.
            var e = open.RootGroup.FindEntry(d.Uuid, true);
            if (e != null && d.DeletionTime > e.LastModificationTime) e.ParentGroup.Entries.Remove(e);
            // deleteGroup: children not named move to the first parent not deleted; the group goes.
            var g = open.RootGroup.FindGroup(d.Uuid, true);
            if (g != null && g != open.RootGroup && d.DeletionTime > g.LastModificationTime)
            {
                var up = g.ParentGroup;
                while (up != null && named.Contains(up.Uuid)) up = up.ParentGroup;
                foreach (var child in new List<PwEntry>(g.Entries)) if (!named.Contains(child.Uuid)) { g.Entries.Remove(child); up.AddEntry(child, true); }
                foreach (var sub in new List<PwGroup>(g.Groups)) { g.Groups.Remove(sub); up.AddGroup(sub, true); }
                g.ParentGroup.Groups.Remove(g);
            }
        }
    }

    static string DxMergeFiles(string open, string arriving, string to)
    {
        var o = Open(open); var a = Open(arriving); DxMerge(o, a); a.Close(); SaveTo(o, to); return to;
    }

    // ---- other programs ----

    static string Run(string exe, string dir, string stdin, params string[] args)
    {
        var info = new ProcessStartInfo(); info.FileName = exe;
        var sb = new StringBuilder(); foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
        info.Arguments = sb.ToString(); info.WorkingDirectory = dir; info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            if (stdin != null) { var bytes = new UTF8Encoding(false).GetBytes(stdin); p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); p.StandardInput.BaseStream.Flush(); }
            p.StandardInput.Close(); p.WaitForExit(); o.Wait(); e.Wait();
            var text = (o.Result + " " + e.Result).Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 160) text = text.Substring(0, 160);
            return "exit " + p.ExitCode.ToString(Inv) + (text.Length > 0 ? ", said: " + text : string.Empty);
        }
    }

    static string Xc(string exe, string target, string source, string to)
    {
        var dir = Path.GetDirectoryName(to);
        File.Copy(target, to, true);
        var src = to + ".source.kdbx"; File.Copy(source, src, true);
        var r = Run(exe, dir, Pw + "\n" + Pw + "\n", "merge", "-s", to, src);
        File.Delete(src);
        return r;
    }

    static string PkDelete(string from, string to, string uuidHex)
    {
        File.Copy(from, to, true);
        return Run(Uv, Path.GetDirectoryName(to), null, "run", "--no-project", "--with", "pykeepass", "python", "-I", "-X", "utf8", PkScript, to, Pw, uuidHex);
    }

    static void L(string s) { Out.WriteLine(s); Out.Flush(); }

    // ---- the chains ----

    static void C1()
    {
        L(""); L("== C1: the Rules page's Remove, the node back from another copy, then a KeePassDX merge ==");
        var b = F("c1", "base.kdbx"); var ids = Base(b, false);
        L("base: " + Show(b, UX));
        var a = Derive(b, F("c1", "A.kdbx"), db => { DeleteForGood(db, db.RootGroup.FindEntry(UX, true), At(10)); DeleteForGood(db, ById(db, ids["X1"]), At(10)); });
        var bb = Derive(b, F("c1", "B.kdbx"), db => Edit(db.RootGroup.FindEntry(UX, true), At(20), e => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-X-B"))));
        L("A (X deleted for good at +10, then Remove of its rule entry, as the Rules page does): " + Show(a, UX));
        L("B (X's password changed at +20, B never saw the deletion): " + Show(bb, UX));
        var bdx = DxMergeFiles(bb, a, F("c1", "B-dx.kdbx"));
        L("DX-model, B open, A arriving: " + Show(bdx, UX));
        L("DX-model, A open, B arriving: " + Show(DxMergeFiles(a, bb, F("c1", "A-dx.kdbx")), UX));
        foreach (var m in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.KeepExisting })
            L("KeePassLib " + m + ", B open, A arriving: " + Show(KpMerge(bb, a, m, F("c1", "kp-" + m + "-intoB.kdbx")), UX));
        L("KeePassLib Synchronize, the DX result open, A arriving: " + Show(KpMerge(bdx, a, PwMergeMethod.Synchronize, F("c1", "kp-sync-dx+A.kdbx")), UX));
        var x1 = F("c1", "xc280-dx+A.kdbx");
        L("keepassxc-cli 2.8.0 merge -s, the DX result as target, A as source (" + Xc(Xc28, bdx, a, x1) + "): " + Show(x1, UX));
        var x2 = F("c1", "xc242-dx+A.kdbx");
        L("keepassxc-cli 2.4.2 merge -s, the DX result as target, A as source (" + Xc(Xc242, bdx, a, x2) + "): " + Show(x2, UX));
    }

    static void C2()
    {
        L(""); L("== C2: the newest rule entry deleted for good in one copy, then a KeePassDX merge into another; and pykeepass ==");
        var b = F("c2", "base.kdbx"); var ids = Base(b, true);
        L("base (X: RX1 then RX2): " + Show(b, UX));
        var p = Derive(b, F("c2", "P.kdbx"), db => DeleteForGood(db, ById(db, ids["X2"]), At(10)));
        var q = Derive(b, F("c2", "Q.kdbx"), db => Edit(db.RootGroup.FindEntry(UY, true), At(5), e => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-Y-Q"))));
        L("P (RX2's entry deleted for good at +10): " + Show(p, UX));
        L("Q (an unrelated edit of Y at +5): " + Show(q, UX));
        L("KeePassLib Synchronize, Q open, P arriving: " + Show(KpMerge(q, p, PwMergeMethod.Synchronize, F("c2", "kp-sync-intoQ.kdbx")), UX));
        var qdx = DxMergeFiles(q, p, F("c2", "Q-dx.kdbx"));
        L("DX-model, Q open, P arriving: " + Show(qdx, UX));
        L("KeePassLib Synchronize, the DX result open, P arriving: " + Show(KpMerge(qdx, p, PwMergeMethod.Synchronize, F("c2", "kp-sync-dx+P.kdbx")), UX));
        L("KeePassLib Synchronize, the DX result open, base arriving: " + Show(KpMerge(qdx, b, PwMergeMethod.Synchronize, F("c2", "kp-sync-dx+base.kdbx")), UX));
        var pk = F("c2", "pk.kdbx");
        L("pykeepass deletes RX2's entry from a copy of the base (" + PkDelete(b, pk, ids["X2"]) + "): " + Show(pk, UX));
        L("KeePassLib Synchronize, the pykeepass file open, base arriving: " + Show(KpMerge(pk, b, PwMergeMethod.Synchronize, F("c2", "kp-sync-pk+base.kdbx")), UX));
    }

    static void C3()
    {
        L(""); L("== C3: the repair of section 5.2 on an OK the operator pressed for something else, and where it travels ==");
        var b = F("c3", "base.kdbx"); var ids = Base(b, true);
        var p = Derive(b, F("c3", "P.kdbx"), db => DeleteForGood(db, ById(db, ids["X2"]), At(10)));
        L("P (RX2's entry deleted for good): " + Show(p, UX));
        string said = null;
        var prep = Derive(p, F("c3", "P-repaired.kdbx"), db =>
        {
            Edit(db.RootGroup.FindEntry(UX, true), At(30), e => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-X-P")));
            said = Repair(db, UX, At(30));
        });
        L("P after X's dialog was used to change X's password and OK pressed (" + said + "): " + Show(prep, UX));
        var s = Derive(b, F("c3", "S.kdbx"), db => Edit(db.RootGroup.FindEntry(UY, true), At(7), e => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-Y-S"))));
        L("S (a copy that still holds RX2 and never saw the deletion): " + Show(s, UX));
        foreach (var m in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting })
            L("KeePassLib " + m + ", S open, repaired P arriving: " + Show(KpMerge(s, prep, m, F("c3", "kp-" + m + "-intoS.kdbx")), UX));
        var x1 = F("c3", "xc280-S+P.kdbx");
        L("keepassxc-cli 2.8.0 merge -s, S as target, repaired P as source (" + Xc(Xc28, s, prep, x1) + "): " + Show(x1, UX));
        var x2 = F("c3", "xc242-S+P.kdbx");
        L("keepassxc-cli 2.4.2 merge -s, S as target, repaired P as source (" + Xc(Xc242, s, prep, x2) + "): " + Show(x2, UX));
        L("DX-model, S open, repaired P arriving: " + Show(DxMergeFiles(s, prep, F("c3", "S-dx.kdbx")), UX));
    }

    static void C4()
    {
        L(""); L("== C4: the store group deleted for good, a later grant in another copy, then a KeePassDX merge; and the repair after it ==");
        var b = F("c4", "base.kdbx"); var ids = Base(b, true);
        DateTime deleted = DateTime.MinValue;
        var g = Derive(b, F("c4", "G.kdbx"), db =>
        {
            // KeePass's own permanent delete of a group: MainForm_Functions.cs:5254-5258 at 2.62.
            var store = Store(db);
            store.ParentGroup.Groups.Remove(store);
            store.DeleteAllObjects(db);
            deleted = DateTime.UtcNow;
            db.DeletedObjects.Add(new PwDeletedObject(store.Uuid, deleted));
        });
        L("G (the KeePassGate group deleted for good, with KeePass's own DeleteAllObjects): " + Show(g, UX));
        var later = deleted.AddSeconds(60);
        var q = Derive(b, F("c4", "Q.kdbx"), db => AddVersion(db, UU, RU2, new[] { ids["U1"] }, later));
        L("Q (never saw the deletion; the operator widened U's rule to add create, a minute after it): " + Show(q, UX));
        L("KeePassLib Synchronize, Q open, G arriving: " + Show(KpMerge(q, g, PwMergeMethod.Synchronize, F("c4", "kp-sync-intoQ.kdbx")), UX));
        L("DX-model, Q open, G arriving: " + Show(DxMergeFiles(q, g, F("c4", "Q-dx.kdbx")), UX));
        string saidU = null, saidX = null;
        var grep = Derive(g, F("c4", "G-repaired.kdbx"), db =>
        {
            // The operator gives U its grant again on U's dialog; then X's dialog is used for X's password.
            var v = Read(db);
            var after = new List<string>(); List<string> l;
            if (v.Unanswered.TryGetValue(TagOfNode(UU), out l)) after.AddRange(l);
            AddVersion(db, UU, RU, after, later); saidU = "U given RU again, naming " + after.Count.ToString(Inv);
            Edit(db.RootGroup.FindEntry(UX, true), later.AddSeconds(5), e => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-X-G")));
            saidX = Repair(db, UX, later.AddSeconds(5));
        });
        L("G after U's rule was entered again and X's dialog OK'd for a password (" + saidU + "; X: " + saidX + "): " + Show(grep, UX));
        var x1 = F("c4", "xc280-base+Grep.kdbx");
        L("keepassxc-cli 2.8.0 merge -s, the base as target, repaired G as source (" + Xc(Xc28, b, grep, x1) + "): " + Show(x1, UX));
    }

    static void C5()
    {
        L(""); L("== C5: a rule entry edited by hand, the repair, and a KeePassXC merge ==");
        var b = F("c5", "base.kdbx"); var ids = Base(b, true);
        var hand = Derive(b, F("c5", "H.kdbx"), db => Edit(ById(db, ids["X2"]), At(20), e => Set(e, DataField, "{\"default\":\"inherit\", \"agent\":[\"list\"]}")));
        L("H (RX2's KPG.Data retyped by hand with one space added, at +20): " + Show(hand, UX));
        string said = null;
        var rep = Derive(hand, F("c5", "H-repaired.kdbx"), db => { said = Repair(db, UX, At(30)); });
        L("H after OK on X's dialog (" + said + "): " + Show(rep, UX));
        var x1 = F("c5", "xc280-Hrep+H.kdbx");
        L("keepassxc-cli 2.8.0 merge -s, repaired H as target, an unrepaired copy of H as source (" + Xc(Xc28, rep, hand, x1) + "): " + Show(x1, UX));
        L("KeePassLib Synchronize, repaired H open, H arriving: " + Show(KpMerge(rep, hand, PwMergeMethod.Synchronize, F("c5", "kp-sync.kdbx")), UX));
    }

    static int Main(string[] args)
    {
        Work = args[0]; Directory.CreateDirectory(Work);
        Xc28 = args[2]; Xc242 = args[3]; Uv = args[4]; PkScript = args[5];
        using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false)))
        {
            L("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString);
            L("store group " + Hex(StoreUuid) + ", mark " + MemUtil.ByteArrayToHexString(Mark) + "; U grants agent [read,update,list]; RX1 narrows X to [read,list]; RX2 to [list]");
            C1(); C2(); C3(); C4(); C5();
        }
        return 0;
    }
}
