using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Probe for the storage option "rules-linked": a rule lives in an entry of its own, outside the
// node it is about, and the two point at each other. Throwaway databases with invented content
// only, all under the work directory given first. No window: KeePassLib types, and keepassxc-cli
// as a console child process.
//
// The store it models:
//   a rule version   an entry in the rules group, written once and never changed
//                      KPG.Kind=rule  KPG.Anchor  KPG.Node (entry/<hex> or group/<hex>)
//                      KPG.After (ids it replaces, or none)  KPG.Lost (records it answers, or none)
//                      KPG.Data (the rule document)  KPG.Nonce
//                    its UUID is the first 16 bytes of SHA-256 over those strings: the version id
//   a record         an entry in the store group, one per version, written once
//                      KPG.Kind=issued  KPG.Version  KPG.Anchor  KPG.Node  KPG.After
//   the back reference
//     on an entry    the custom string KPG.Anchor on the user's entry itself
//     in a group     a link entry directly inside the group: KPG.Kind=link, KPG.Anchor, and its
//                    own UUID is the anchor
static class P
{
    static string Work;
    static TextWriter Out;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Pw = "probe-only";
    const string Profile = "agent";

    const string KindF = "KPG.Kind", AnchorF = "KPG.Anchor", NodeF = "KPG.Node", AfterF = "KPG.After",
        LostF = "KPG.Lost", DataF = "KPG.Data", NonceF = "KPG.Nonce", VersionF = "KPG.Version";
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    const string RG0 = "{\"default\":\"inherit\",\"agent\":[\"read\",\"list\"]}";
    const string SEAL = "{\"default\":\"deny\"}";
    const string RXN = "{\"default\":\"inherit\",\"agent\":[]}";
    const string RXL = "{\"default\":\"inherit\",\"agent\":[\"list\"]}";
    const string RXR = "{\"default\":\"inherit\",\"agent\":[\"read\"]}";
    const string RXW = "{\"default\":\"inherit\",\"agent\":[\"read\",\"list\",\"protected\"]}";

    static readonly PwUuid StoreUuid = new PwUuid(First(Sha("KPG/1/group/"), 16));
    static readonly PwUuid RulesUuid = new PwUuid(First(Sha("KPG/1/rules/"), 16));

    static byte[] Sha(string text) { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text)); }
    static byte[] First(byte[] from, int count) { var b = new byte[count]; Array.Copy(from, b, count); return b; }
    static DateTime At(int s) { return T0.AddSeconds(s); }
    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }
    static PwUuid Fixed(string name) { return new PwUuid(First(Sha("probe " + name), 16)); }
    static string Hex(PwUuid u) { return u.ToHexString(); }
    static PwUuid FromHex(string hex) { return new PwUuid(MemUtil.HexStringToByteArray(hex)); }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup AddGroup(PwGroup parent, string name, PwUuid uuid, DateTime t)
    {
        var g = new PwGroup(false, false);
        g.Uuid = uuid; g.Name = name; Stamp(g, t);
        parent.AddGroup(g, true); g.LocationChanged = t;
        return g;
    }

    static PwEntry AddEntry(PwGroup parent, PwUuid uuid, string title, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = uuid;
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        Stamp(e, t);
        parent.AddEntry(e, true); e.LocationChanged = t;
        return e;
    }

    static void Set(PwEntry e, string name, string value) { e.Strings.Set(name, new ProtectedString(false, value)); }
    static string Get(PwEntry e, string name) { return e.Strings.Exists(name) ? e.Strings.ReadSafe(name) : null; }
    static void Password(PwEntry e, string value) { e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value)); }

    // An edit as KeePass makes one: the state before goes into history, then the time moves.
    static void Edit(PwEntry e, DateTime t, Action<PwEntry> change) { e.CreateBackup(null); change(e); e.LastModificationTime = t; }

    static PwGroup Store(PwDatabase db) { return db.RootGroup.FindGroup(StoreUuid, true); }
    static PwGroup Rules(PwDatabase db) { return db.RootGroup.FindGroup(RulesUuid, true); }

    static PwGroup PluginGroup(PwDatabase db, PwUuid uuid, string name)
    {
        var g = db.RootGroup.FindGroup(uuid, true);
        if (g != null) return g;
        g = AddGroup(db.RootGroup, name, uuid, T0);
        g.EnableSearching = false; g.EnableAutoType = false; g.Notes = NotesLine;
        return g;
    }

    // ---- writing ----

    static string VersionId(string anchor, string node, string after, string lost, string data, string nonce)
    {
        return MemUtil.ByteArrayToHexString(First(Sha("KPG/1/rule/" + anchor + "\n" + node + "\n" + after + "\n" + lost + "\n" + data + "\n" + nonce), 16));
    }

    static string Ids(IEnumerable<string> ids)
    {
        var l = new List<string>(ids); l.Sort(StringComparer.Ordinal);
        return l.Count == 0 ? "none" : string.Join(" ", l.ToArray());
    }

    static PwEntry PluginEntry(PwGroup g, PwUuid uuid, string title, DateTime t)
    {
        var e = AddEntry(g, uuid, title, t);
        Set(e, PwDefs.UserNameField, ""); Password(e, ""); Set(e, PwDefs.UrlField, ""); Set(e, PwDefs.NotesField, NotesLine);
        return e;
    }

    static string AddVersion(PwDatabase db, string anchor, string node, string data, IEnumerable<string> after, string label, DateTime t, bool record)
    {
        var afterText = Ids(after);
        var nonce = Hex(Fixed("nonce " + label));
        var id = VersionId(anchor, node, afterText, "none", data, nonce);
        var e = PluginEntry(PluginGroup(db, RulesUuid, "KeePassGate rules"), FromHex(id), "KPG rule " + anchor.Substring(0, 8), t);
        Set(e, KindF, "rule"); Set(e, AnchorF, anchor); Set(e, NodeF, node); Set(e, AfterF, afterText); Set(e, LostF, "none"); Set(e, DataF, data); Set(e, NonceF, nonce);
        if (record)
        {
            var w = PluginEntry(PluginGroup(db, StoreUuid, "KeePassGate"), new PwUuid(First(Sha("KPG/1/issued/" + id), 16)), "KPG rule record " + anchor.Substring(0, 8) + " " + id.Substring(0, 8), t);
            Set(w, KindF, "issued"); Set(w, VersionF, id); Set(w, AnchorF, anchor); Set(w, NodeF, node); Set(w, AfterF, afterText);
        }
        return id;
    }

    static string NodeOf(PwEntry e) { return "entry/" + Hex(e.Uuid); }
    static string NodeOf(PwGroup g) { return "group/" + Hex(g.Uuid); }

    // A first rule on an entry: the anchor is minted, written on the entry as KeePass's dialog
    // would (one revision, the time moves), and the first version and its record are added.
    static string FirstRule(PwDatabase db, PwEntry x, string data, string label, DateTime t, bool asEdit)
    {
        var anchor = Hex(Fixed("anchor " + label));
        if (asEdit) Edit(x, t, e => Set(e, AnchorF, anchor)); else Set(x, AnchorF, anchor);
        AddVersion(db, anchor, NodeOf(x), data, new string[0], label, t, true);
        return anchor;
    }

    // A first rule on a group: a link entry inside the group, whose own UUID is the anchor.
    static string FirstRule(PwDatabase db, PwGroup g, string data, string label, DateTime t)
    {
        var uuid = Fixed("link " + label);
        var anchor = Hex(uuid);
        var l = PluginEntry(g, uuid, "KeePassGate link", t);
        Set(l, KindF, "link"); Set(l, AnchorF, anchor);
        AddVersion(db, anchor, NodeOf(g), data, new string[0], label, t, true);
        return anchor;
    }

    // A later rule: a new version naming everything known of that anchor; what it replaces is
    // deleted for good with a deletion record, as KeePass's own permanent delete does.
    static string ChangeRule(PwDatabase db, string anchor, string data, string label, DateTime t)
    {
        var v = Read(db);
        var known = new SortedSet<string>(StringComparer.Ordinal);
        string node = null;
        foreach (var i in v.Insts) if (i.Anchor == anchor) { known.Add(i.Id); foreach (var a in i.After) known.Add(a); node = i.Node; }
        foreach (var w in v.Wits) if (w.Anchor == anchor) { known.Add(w.Version); foreach (var a in w.After) known.Add(a); node = w.Node; }
        var id = AddVersion(db, anchor, node, data, known, label, t, true);
        foreach (var i in v.Insts)
            if (i.Anchor == anchor && i.Intact) DeleteForGood(db, i.E, t, true);
        return id;
    }

    static void DeleteForGood(PwDatabase db, PwEntry e, DateTime t, bool record)
    {
        e.ParentGroup.Entries.Remove(e);
        if (record) db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    // ---- the documents ----

    sealed class Doc
    {
        public bool Bad;
        public bool Deny;
        public Dictionary<string, SortedSet<string>> Names = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    }

    static Doc Parse(string text)
    {
        var d = new Doc();
        var m = Regex.Match(text ?? "", "^\\{\"default\":\"(inherit|deny)\"((,\"[A-Za-z0-9_-]+\":\\[[^\\]]*\\])*)\\}$");
        if (!m.Success) { d.Bad = true; return d; }
        d.Deny = m.Groups[1].Value == "deny";
        foreach (Match p in Regex.Matches(m.Groups[2].Value, "\"([A-Za-z0-9_-]+)\":\\[([^\\]]*)\\]"))
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match w in Regex.Matches(p.Groups[2].Value, "\"([a-z]+)\"")) set.Add(w.Groups[1].Value);
            d.Names[p.Groups[1].Value] = set;
        }
        return d;
    }

    // The narrower of several documents: deny when any denies; a profile all name keeps what all
    // give it; a profile only some name keeps nothing.
    static Doc Meet(List<Doc> docs)
    {
        if (docs.Count == 1) return docs[0];
        var r = new Doc();
        var all = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in docs) { if (d.Bad) r.Bad = true; if (d.Deny) r.Deny = true; foreach (var k in d.Names.Keys) all.Add(k); }
        foreach (var k in all)
        {
            SortedSet<string> acc = null; var every = true;
            foreach (var d in docs)
            {
                SortedSet<string> s;
                if (!d.Names.TryGetValue(k, out s)) { every = false; break; }
                if (acc == null) acc = new SortedSet<string>(s, StringComparer.Ordinal); else acc.IntersectWith(s);
            }
            r.Names[k] = every ? acc : new SortedSet<string>(StringComparer.Ordinal);
        }
        return r;
    }

    // ---- reading ----

    sealed class Inst { public PwEntry E; public string Id, Anchor, Node, Data; public bool Intact; public List<string> After = new List<string>(); public List<string> Lost = new List<string>(); }
    sealed class Wit { public string Version, Anchor, Node; public List<string> After = new List<string>(); }
    sealed class Link { public PwEntry E; public string Anchor; public bool Original; }
    sealed class AInfo { public bool Known; public string Home; public string Broken; public bool Demand; public bool Untrusted; public Doc D; }

    sealed class View
    {
        public List<Inst> Insts = new List<Inst>();
        public List<Wit> Wits = new List<Wit>();
        public Dictionary<string, List<Link>> LinksIn = new Dictionary<string, List<Link>>(StringComparer.Ordinal);
        public Dictionary<string, AInfo> Infos = new Dictionary<string, AInfo>(StringComparer.Ordinal);
    }

    static readonly Regex HexId = new Regex("^[0-9A-F]{32}$");
    static readonly Regex NodeId = new Regex("^(entry|group)/[0-9A-F]{32}$");

    static bool IdList(string text, List<string> into)
    {
        if (text == null) return false;
        if (text == "none") return true;
        foreach (var part in text.Split(' ')) { if (!HexId.IsMatch(part)) return false; into.Add(part); }
        return true;
    }

    static void Walk(PwGroup g, Action<PwEntry> each)
    {
        foreach (var e in g.Entries) each(e);
        foreach (var s in g.Groups) Walk(s, each);
    }

    static View Read(PwDatabase db)
    {
        var v = new View();
        Walk(db.RootGroup, e =>
        {
            var kind = Get(e, KindF);
            if (kind == "rule")
            {
                var i = new Inst { E = e, Anchor = Get(e, AnchorF), Node = Get(e, NodeF), Data = Get(e, DataF) };
                var nonce = Get(e, NonceF);
                if (i.Anchor == null || !HexId.IsMatch(i.Anchor) || i.Node == null || !NodeId.IsMatch(i.Node) || i.Data == null || nonce == null || !HexId.IsMatch(nonce)) return;
                if (!IdList(Get(e, AfterF), i.After) || !IdList(Get(e, LostF), i.Lost)) return;
                i.Id = VersionId(i.Anchor, i.Node, Get(e, AfterF), Get(e, LostF), i.Data, nonce);
                i.Intact = Hex(e.Uuid) == i.Id;
                v.Insts.Add(i);
            }
            else if (kind == "issued")
            {
                var w = new Wit { Version = Get(e, VersionF), Anchor = Get(e, AnchorF), Node = Get(e, NodeF) };
                if (w.Version == null || !HexId.IsMatch(w.Version) || w.Anchor == null || !HexId.IsMatch(w.Anchor) || w.Node == null || !NodeId.IsMatch(w.Node)) return;
                if (!IdList(Get(e, AfterF), w.After)) return;
                v.Wits.Add(w);
            }
            else if (kind == "link")
            {
                var key = NodeOf(e.ParentGroup);
                if (!v.LinksIn.ContainsKey(key)) v.LinksIn[key] = new List<Link>();
                var a = Get(e, AnchorF);
                v.LinksIn[key].Add(new Link { E = e, Anchor = a, Original = a != null && Hex(e.Uuid) == a });
            }
        });
        return v;
    }

    static AInfo Info(View v, string anchor)
    {
        AInfo info;
        if (v.Infos.TryGetValue(anchor, out info)) return info;
        info = new AInfo();
        v.Infos[anchor] = info;
        var dead = new HashSet<string>(StringComparer.Ordinal);
        var answered = new HashSet<string>(StringComparer.Ordinal);
        var homes = new SortedSet<string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, List<Inst>>(StringComparer.Ordinal);
        foreach (var i in v.Insts)
        {
            if (i.Anchor != anchor) continue;
            info.Known = true; homes.Add(i.Node);
            foreach (var a in i.After) dead.Add(a);
            if (i.Intact) foreach (var a in i.Lost) answered.Add(a);
            if (!byId.ContainsKey(i.Id)) byId[i.Id] = new List<Inst>();
            byId[i.Id].Add(i);
        }
        foreach (var w in v.Wits)
        {
            if (w.Anchor != anchor) continue;
            info.Known = true; homes.Add(w.Node);
            foreach (var a in w.After) dead.Add(a);
        }
        if (!info.Known) return info;
        foreach (var h in homes) { info.Home = h; break; }
        if (homes.Count > 1) info.Broken = "its rule names two nodes";
        foreach (var w in v.Wits)
            if (w.Anchor == anchor && !dead.Contains(w.Version) && !answered.Contains(w.Version) && !byId.ContainsKey(w.Version))
            { info.Demand = true; if (info.Broken == null) info.Broken = "a recorded rule version is missing"; }
        var docs = new List<Doc>();
        foreach (var kv in byId)
        {
            if (dead.Contains(kv.Key)) continue;
            var intact = false;
            foreach (var i in kv.Value) if (i.Intact) intact = true;
            if (!intact) info.Untrusted = true;
            docs.Add(Parse(kv.Value[0].Data));
        }
        if (docs.Count == 0) { if (info.Broken == null) info.Broken = "no rule version is left"; return info; }
        info.D = Meet(docs);
        if (info.D.Bad && info.Broken == null) info.Broken = "its rule will not read";
        return info;
    }

    sealed class NodeState { public string Refuse; public List<Doc> Confirmed = new List<Doc>(); public List<Doc> Limits = new List<Doc>(); public List<string> Notes = new List<string>(); }

    static NodeState StateOf(View v, string node, List<string> back, List<bool> originalLink)
    {
        var s = new NodeState();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var n = 0; n < back.Count; n++)
        {
            var a = back[n];
            if (a == null || !HexId.IsMatch(a)) { s.Refuse = "its reference will not read"; s.Notes.Add("refuse"); continue; }
            seen.Add(a);
            var info = Info(v, a);
            if (!info.Known) { s.Refuse = "it refers to a rule that is not in this database"; s.Notes.Add("dangling"); continue; }
            if (info.Home == node)
            {
                if (info.Broken != null) { s.Refuse = info.Broken; s.Notes.Add("broken"); continue; }
                if (info.Untrusted) { s.Refuse = "its rule entry is not the entry the plugin wrote"; s.Notes.Add("untrusted"); continue; }
                s.Confirmed.Add(info.D); s.Notes.Add("agree");
            }
            else if (originalLink != null && originalLink[n]) s.Notes.Add("a moved link, ignored");
            else
            {
                if (info.Broken != null || info.Untrusted || info.D == null) { s.Refuse = "it is a copy and the original's rule cannot be used"; s.Notes.Add("copy-broken"); continue; }
                s.Limits.Add(info.D); s.Notes.Add("copy");
            }
        }
        var anchors = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var i in v.Insts) if (i.Node == node) anchors.Add(i.Anchor);
        foreach (var w in v.Wits) if (w.Node == node) anchors.Add(w.Anchor);
        foreach (var a in anchors)
        {
            if (seen.Contains(a)) continue;
            var info = Info(v, a);
            if (info.Home != node) continue;
            if (info.Demand) { s.Refuse = info.Broken; s.Notes.Add("unconfirmed-broken"); continue; }
            if (info.D == null) continue;
            if (info.D.Bad) { s.Refuse = "a rule naming it will not read"; s.Notes.Add("unconfirmed-unreadable"); continue; }
            s.Limits.Add(info.D); s.Notes.Add("unconfirmed");
        }
        return s;
    }

    static NodeState StateOf(View v, PwEntry e)
    {
        var back = new List<string>();
        if (e.Strings.Exists(AnchorF)) back.Add(e.Strings.ReadSafe(AnchorF));
        return StateOf(v, NodeOf(e), back, null);
    }

    static NodeState StateOf(View v, PwGroup g)
    {
        var back = new List<string>(); var orig = new List<bool>();
        List<Link> links;
        if (v.LinksIn.TryGetValue(NodeOf(g), out links)) foreach (var l in links) { back.Add(l.Anchor); orig.Add(l.Original); }
        return StateOf(v, NodeOf(g), back, orig);
    }

    // What the profile is granted at an entry: nearest confirmed rule that names it, capped by
    // every limit met on the way up. A refusal anywhere on the walk refuses.
    static string Resolve(View v, PwEntry e, out string own)
    {
        SortedSet<string> cap = null;
        var first = StateOf(v, e);
        own = first.Notes.Count == 0 ? "" : string.Join("+", first.Notes.ToArray());
        NodeState s = first;
        PwGroup g = e.ParentGroup;
        var where = "itself";
        while (true)
        {
            if (s.Refuse != null) return "REFUSE(" + where + ": " + s.Refuse + ")";
            foreach (var l in s.Limits)
            {
                SortedSet<string> set;
                if (l.Names.TryGetValue(Profile, out set)) { if (cap == null) cap = new SortedSet<string>(set, StringComparer.Ordinal); else cap.IntersectWith(set); }
                else if (l.Deny) return "-";
            }
            if (s.Confirmed.Count > 0)
            {
                var d = Meet(s.Confirmed);
                SortedSet<string> set;
                if (d.Names.TryGetValue(Profile, out set))
                {
                    var r = new SortedSet<string>(set, StringComparer.Ordinal);
                    if (cap != null) r.IntersectWith(cap);
                    return r.Count == 0 ? "-" : string.Join(",", new List<string>(r).ToArray());
                }
                if (d.Deny) return "-";
            }
            if (g == null) return "-";
            s = StateOf(v, g); where = g.Name; g = g.ParentGroup;
        }
    }

    static bool IsPlugin(PwEntry e) { return e.Strings.Exists(KindF); }

    // Every user entry: what the profile gets, and the entry's own state.
    static string State(PwDatabase db)
    {
        var v = Read(db);
        var rows = new List<string>();
        Walk(db.RootGroup, e =>
        {
            if (IsPlugin(e)) return;
            if (e.ParentGroup.Uuid.Equals(StoreUuid) || e.ParentGroup.Uuid.Equals(RulesUuid)) return;
            string own;
            var a = Resolve(v, e, out own);
            rows.Add(e.Strings.ReadSafe(PwDefs.TitleField) + "@" + e.ParentGroup.Name + "=" + a + (own.Length > 0 ? "[" + own + "]" : ""));
        });
        rows.Sort(StringComparer.Ordinal);
        var groups = new List<string>();
        Action<PwGroup> each = null;
        each = g =>
        {
            if (!g.Uuid.Equals(StoreUuid) && !g.Uuid.Equals(RulesUuid))
            {
                var s = StateOf(v, g);
                if (s.Notes.Count > 0) groups.Add(g.Name + ":" + string.Join("+", s.Notes.ToArray()));
            }
            foreach (var c in g.Groups) each(c);
        };
        each(db.RootGroup);
        groups.Sort(StringComparer.Ordinal);
        return string.Join(" | ", rows.ToArray()) + "  || groups " + string.Join(", ", groups.ToArray());
    }

    // ---- the databases ----

    static PwDatabase NewDb(string name)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Work, name)), Key());
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        db.KdfParameters = a;
        db.SettingsChanged = T0;
        db.RootGroup.Uuid = Fixed("root"); db.RootGroup.Name = "root";
        Stamp(db.RootGroup, T0);
        return db;
    }

    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }

    static string KdbxVersion(string path)
    {
        var b = new byte[12];
        using (var f = File.OpenRead(path)) f.Read(b, 0, 12);
        return BitConverter.ToUInt16(b, 10).ToString(Inv) + "." + BitConverter.ToUInt16(b, 8).ToString(Inv);
    }

    // ---- copied from the design probe, lines 527-529, 690-697, 865-886 ----
    static PwEntry E(PwDatabase db, string name) { return db.RootGroup.FindEntry(Fixed("entry " + name), true); }
    static PwGroup G(PwDatabase db, string name) { return db.RootGroup.FindGroup(Fixed("group " + name), true); }
    static string AnchorOfX(PwDatabase db) { return E(db, "X").Strings.ReadSafe(AnchorF); }
    static List<Inst> InstsOf(PwDatabase db, string node) { var r = new List<Inst>(); foreach (var i in Read(db).Insts) if (i.Node == node) r.Add(i); return r; }

    static PwEntry RecordOf(PwDatabase db, string id)
    {
        PwEntry found = null;
        Walk(db.RootGroup, e => { if (Get(e, KindF) == "issued" && Get(e, VersionF) == id) found = e; });
        return found;
    }
    static string Cli(string exe, string dir, params string[] args)
    {
        var info = new ProcessStartInfo();
        info.FileName = exe;
        var sb = new StringBuilder();
        foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
        info.Arguments = sb.ToString();
        info.WorkingDirectory = dir;
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n" + Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.BaseStream.Flush();
            p.StandardInput.Close();
            p.WaitForExit(); o.Wait(); e.Wait();
            return p.ExitCode.ToString(Inv);
        }
    }

    // ======================================================================================
    // rules-linked-survival: sequences the design's probe did not run. The read and write path
    // above is the design's own probe code, copied unchanged (Program.cs lines 1-494 and Cli).
    // ======================================================================================

    static string Xc242, Xc280;

    // My base. The root grants nothing.
    //   U   opened to agent: read, list (link)
    //     X   narrowed to nothing by its own rule (KPG.Anchor)
    //     Y   no rule
    //   W   no rule
    //     S   sealed (link)
    //       Q   a secret
    //     V   no rule
    static void MakeBase2(string path, string xDoc)
    {
        var db = NewDb("base2-new.kdbx");
        PluginGroup(db, StoreUuid, "KeePassGate");
        PluginGroup(db, RulesUuid, "KeePassGate rules");
        var u = AddGroup(db.RootGroup, "U", Fixed("group U"), T0);
        var x = AddEntry(u, Fixed("entry X"), "X", T0); Password(x, "x-secret-0");
        var y = AddEntry(u, Fixed("entry Y"), "Y", T0); Password(y, "y-secret-0");
        var w = AddGroup(db.RootGroup, "W", Fixed("group W"), T0);
        var s = AddGroup(w, "S", Fixed("group S"), T0);
        var q = AddEntry(s, Fixed("entry Q"), "Q", T0); Password(q, "q-secret-0");
        var vv = AddEntry(w, Fixed("entry V"), "V", T0); Password(vv, "v-secret-0");
        FirstRule(db, u, RG0, "base-U", T0);
        FirstRule(db, s, SEAL, "base-S", T0);
        FirstRule(db, x, xDoc ?? RXN, "base-X", T0, false);
        SaveTo(db, path);
    }

    static PwEntry Titled(PwDatabase db, string title)
    {
        PwEntry found = null;
        Walk(db.RootGroup, e => { if (found == null && !IsPlugin(e) && e.Strings.ReadSafe(PwDefs.TitleField) == title) found = e; });
        return found;
    }

    static PwEntry LinkIn(PwGroup g) { foreach (var e in g.Entries) if (Get(e, KindF) == "link") return e; return null; }

    static string Agent(PwDatabase db, PwEntry e) { if (e == null) return "(absent)"; string own; return Resolve(Read(db), e, out own) + (own.Length > 0 ? " [" + own + "]" : ""); }

    static string Line(string label, PwDatabase db) { return "  " + label + ": " + State(db); }

    // ---- C1: a copy of a sealed group, and the one entry that keeps it sealed ----
    static void RunCopies2()
    {
        Out.WriteLine();
        Out.WriteLine("== C1, C2: copies, and the reference the copy carries (KeePassLib " + PwDefs.VersionString + ") ==");
        var dir = Path.Combine(Work, "c1"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase2(basePath, null);

        // C1a: a copying drag of S into U, as MainForm.cs:1317-1321 does it: PwGroup.Duplicate, no
        // dialog, appended to the drop target's groups.
        {
            var db = Open(basePath);
            Out.WriteLine(Line("base", db));
            var s2 = G(db, "S").Duplicate(); G(db, "U").AddGroup(s2, true, true);
            var l1 = LinkIn(G(db, "S")); var l2 = LinkIn(s2);
            Out.WriteLine("C1a copying drag of sealed S into U (granting): copy link uuid==anchor " + (Hex(l2.Uuid) == Get(l2, AnchorF)) + ", copy link time " + l2.LastModificationTime.ToString("o", Inv) + " original " + l1.LastModificationTime.ToString("o", Inv) + ", titles equal " + (Get(l1, PwDefs.TitleField) == Get(l2, PwDefs.TitleField)));
            var p = Path.Combine(dir, "c1a.kdbx"); SaveTo(db, p); var r = Open(p);
            Out.WriteLine(Line("C1a after the drag", r));
            r.Close();
        }
        // C1b: the same drag, the copy's Q edited (a new secret put into the copy), then KeePass's
        // Delete Duplicate Entries, as Tools then Database Tools runs it (PwDatabase.cs:1969-2036).
        {
            var db = Open(basePath);
            var s2 = G(db, "S").Duplicate(); G(db, "U").AddGroup(s2, true, true);
            PwEntry q2 = null; foreach (var e in s2.Entries) if (!IsPlugin(e)) q2 = e;
            Edit(q2, At(10), e => { Password(e, "a-new-secret-put-in-the-copy"); Set(e, PwDefs.TitleField, "Q2"); });
            var before = State(db);
            var deleted = new List<string>();
            var uuidsBefore = new Dictionary<string, string>(StringComparer.Ordinal);
            Walk(db.RootGroup, e => uuidsBefore[Hex(e.Uuid)] = (e.Strings.ReadSafe(PwDefs.TitleField)) + "@" + e.ParentGroup.Name + (Hex(e.Uuid) == Get(e, AnchorF) ? "(original link)" : (Get(e, KindF) == "link" ? "(copy link)" : "")));
            var n = db.DeleteDuplicateEntries(null);
            var after = new HashSet<string>(StringComparer.Ordinal); Walk(db.RootGroup, e => after.Add(Hex(e.Uuid)));
            foreach (var kv in uuidsBefore) if (!after.Contains(kv.Key)) deleted.Add(kv.Value);
            Out.WriteLine("C1b drag, Q edited in the copy, then Delete Duplicate Entries: deleted " + n.ToString(Inv) + ": " + string.Join(", ", deleted.ToArray()));
            Out.WriteLine("  before Delete Duplicate Entries: " + before);
            var p = Path.Combine(dir, "c1b.kdbx"); SaveTo(db, p); var r = Open(p);
            Out.WriteLine(Line("C1b after, saved and reopened", r));
            r.Close();
        }
        // C1c: the same drag, then the copy's link deleted for good by hand (or the bin emptied).
        {
            var db = Open(basePath);
            var s2 = G(db, "S").Duplicate(); G(db, "U").AddGroup(s2, true, true);
            DeleteForGood(db, LinkIn(s2), At(10), true);
            var p = Path.Combine(dir, "c1c.kdbx"); SaveTo(db, p); var r = Open(p);
            Out.WriteLine(Line("C1c drag, then the copy's link deleted for good", r));
            r.Close();
        }
        // C1d: the control. Group then Duplicate Group with its dialog's default (append "- Copy"
        // to every entry title, DuplicationForm.cs:43, :99-105), inserted after S (MainForm.cs:2313-2342),
        // then moved into U, then Delete Duplicate Entries.
        {
            var db = Open(basePath);
            var s = G(db, "S"); var s2 = s.Duplicate();
            foreach (var e in s2.GetEntries(true)) Set(e, PwDefs.TitleField, e.Strings.ReadSafe(PwDefs.TitleField) + " - Copy");
            G(db, "U").AddGroup(s2, true, true);
            var n = db.DeleteDuplicateEntries(null);
            var p = Path.Combine(dir, "c1d.kdbx"); SaveTo(db, p); var r = Open(p);
            Out.WriteLine(Line("C1d Duplicate Group with '- Copy', moved into U, Delete Duplicate Entries deleted " + n.ToString(Inv), r));
            r.Close();
        }
        // C2: Duplicate Entry of X (narrowed) with the default title, then the copy's KPG.Anchor
        // deleted by hand (an edit), as h04 did to the original.
        {
            var db = Open(basePath);
            var x = E(db, "X"); var x2 = x.Duplicate(); Set(x2, PwDefs.TitleField, "X - Copy"); x.ParentGroup.AddEntry(x2, true, true);
            Out.WriteLine("C2 Duplicate Entry X: X - Copy is " + Agent(db, x2));
            Edit(x2, At(10), e => e.Strings.Remove(AnchorF));
            var p = Path.Combine(dir, "c2.kdbx"); SaveTo(db, p); var r = Open(p);
            Out.WriteLine(Line("C2 then the copy's KPG.Anchor deleted by hand", r));
            r.Close();
        }
    }

    // ---- C3: the newest version and its record deleted WITH deletion records, then a stale copy ----
    static void RunRollback2()
    {
        Out.WriteLine();
        Out.WriteLine("== C3: the newest version and its record deleted with deletion records (as emptying the bin does), then a stale copy ==");
        var dir = Path.Combine(Work, "c3"); Directory.CreateDirectory(dir);
        var stale = Path.Combine(dir, "stale.kdbx");
        MakeBase2(stale, RXW);
        {
            var cur = Open(stale);
            Out.WriteLine(Line("stale copy", cur));
            var id = ChangeRule(cur, AnchorOfX(cur), RXN, "cur-X2", At(10));
            Out.WriteLine(Line("current copy after the narrowing", cur));
            foreach (var i in InstsOf(cur, NodeOf(E(cur, "X")))) if (i.Id == id) DeleteForGood(cur, i.E, At(20), true);
            DeleteForGood(cur, RecordOf(cur, id), At(20), true);
            Out.WriteLine(Line("C3 version and record deleted with records, before any merge", cur));
            var curPath = Path.Combine(dir, "cur.kdbx"); SaveTo(cur, curPath);
            foreach (var m in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting })
            {
                var t = Open(curPath); var src = Open(stale);
                t.MergeIn(src, m); src.Close();
                var res = Path.Combine(dir, "kp-" + m + ".kdbx"); SaveTo(t, res);
                var r = Open(res);
                Out.WriteLine(Line("C3 then the stale copy merged into the current one by KeePassLib " + m, r));
                r.Close();
            }
            foreach (var xc in new[] { new[] { "2.4.2", Xc242 }, new[] { "2.8.0", Xc280 } })
            {
                var cell = Path.Combine(dir, "xc" + xc[0]); Directory.CreateDirectory(cell);
                var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
                File.Copy(curPath, target, true); File.Copy(stale, source, true);
                var ex = Cli(xc[1], cell, "merge", "-s", target, source);
                var r = Open(target);
                Out.WriteLine(Line("C3 then the stale copy merged into the current one by keepassxc-cli " + xc[0] + " (exit " + ex + ", deletion records in the result " + r.DeletedObjects.UCount.ToString(Inv) + ")", r));
                r.Close();
            }
        }

        // C3b: changes made since the stale copy (X narrowed, Y given a first rule that narrows),
        // then both plugin groups moved to the bin and the bin emptied: every version and every
        // record deleted with a deletion record, as PwGroup.DeleteAllObjects does.
        Out.WriteLine();
        {
            var cur = Open(stale);
            ChangeRule(cur, AnchorOfX(cur), RXN, "cur3-X2", At(10));
            FirstRule(cur, E(cur, "Y"), RXN, "cur3-Y", At(11), true);
            Out.WriteLine(Line("C3b current copy: X narrowed, Y given a first rule (agent: nothing)", cur));
            foreach (var g in new[] { Rules(cur), Store(cur) })
            {
                foreach (var e in g.GetEntries(true)) cur.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(30)));
                cur.DeletedObjects.Add(new PwDeletedObject(g.Uuid, At(30)));
                g.ParentGroup.Groups.Remove(g);
            }
            Out.WriteLine(Line("C3b both plugin groups deleted for good, with records", cur));
            var curPath = Path.Combine(dir, "cur3b.kdbx"); SaveTo(cur, curPath);
            {
                var t = Open(curPath); var src = Open(stale);
                t.MergeIn(src, PwMergeMethod.Synchronize); src.Close();
                var res = Path.Combine(dir, "kp3b-sync.kdbx"); SaveTo(t, res);
                var r = Open(res);
                Out.WriteLine(Line("C3b then the stale copy synchronized in by KeePassLib", r));
                r.Close();
            }
            {
                // C3c: KeePass's own way back to the lost groups: Database Maintenance's "Remove deleted
                // objects information" (DatabaseOperationsForm.cs:159-170 clears the list), then a
                // synchronize with the older copy.
                var t = Open(curPath); t.DeletedObjects.Clear(); var src = Open(stale);
                t.MergeIn(src, PwMergeMethod.Synchronize); src.Close();
                var res = Path.Combine(dir, "kp3c-cleared-sync.kdbx"); SaveTo(t, res);
                var r = Open(res);
                Out.WriteLine(Line("C3c deletion records removed, then the stale copy synchronized in by KeePassLib", r));
                r.Close();
            }
            foreach (var xc in new[] { new[] { "2.4.2", Xc242 }, new[] { "2.8.0", Xc280 } })
            {
                var cell = Path.Combine(dir, "xc3b" + xc[0]); Directory.CreateDirectory(cell);
                var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
                File.Copy(curPath, target, true); File.Copy(stale, source, true);
                var ex = Cli(xc[1], cell, "merge", "-s", target, source);
                var r = Open(target);
                Out.WriteLine(Line("C3b then the stale copy merged in by keepassxc-cli " + xc[0] + " (exit " + ex + ")", r));
                r.Close();
            }
        }
    }

    // ---- C4: a phone whose clock is ahead, and a Confirm that does not stick ----
    static void RunClock()
    {
        Out.WriteLine();
        Out.WriteLine("== C4: a phone clock a day ahead; KeePass's Confirm undone by every synchronize until the clock passes ==");
        var dir = Path.Combine(Work, "c4"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase2(basePath, null);
        var k = Path.Combine(dir, "K.kdbx"); var ph = Path.Combine(dir, "Ph.kdbx");
        { var d = Open(basePath); FirstRule(d, E(d, "V"), RXR, "K-V", At(10), true); SaveTo(d, k); }
        { var d = Open(basePath); Edit(E(d, "V"), At(86400), e => Password(e, "edited-on-the-phone")); SaveTo(d, ph); }
        // round 1: the phone's file synchronized into KeePass's copy
        var m1 = Path.Combine(dir, "M1.kdbx");
        { var t = Open(k); var s = Open(ph); t.MergeIn(s, PwMergeMethod.Synchronize); s.Close(); SaveTo(t, m1); }
        { var r = Open(m1); Out.WriteLine("  round 1 (phone into KeePass): V is " + Agent(r, E(r, "V")) + ", V carries KPG.Anchor " + E(r, "V").Strings.Exists(AnchorF)); r.Close(); }
        // the operator presses Confirm in KeePass at KeePass's now (+20): KPG.Anchor written back, as an edit
        var m2 = Path.Combine(dir, "M2.kdbx");
        string anchor;
        { var d = Open(k); anchor = E(d, "V").Strings.ReadSafe(AnchorF); d.Close(); }
        { var d = Open(m1); Edit(E(d, "V"), At(20), e => Set(e, AnchorF, anchor)); SaveTo(d, m2); }
        { var r = Open(m2); Out.WriteLine("  after Confirm at +20: V is " + Agent(r, E(r, "V"))); r.Close(); }
        // round 2: the phone still holds its own copy (V at +86400, no reference); synchronize both ways
        foreach (var into in new[] { "KeePass", "phone" })
        {
            var t = Open(into == "KeePass" ? m2 : ph); var s = Open(into == "KeePass" ? ph : m2);
            t.MergeIn(s, PwMergeMethod.Synchronize); s.Close();
            var res = Path.Combine(dir, "M3-into-" + into + ".kdbx"); SaveTo(t, res);
            var r = Open(res); Out.WriteLine("  round 2, synchronized into " + into + ": V is " + Agent(r, E(r, "V")) + ", carries KPG.Anchor " + E(r, "V").Strings.Exists(AnchorF)); r.Close();
        }
        // the phone receives M1 (the merge result) and edits nothing further; KeePass confirms again later at +86401
        {
            var d = Open(m1); Edit(E(d, "V"), At(86401), e => Set(e, AnchorF, anchor)); var m4 = Path.Combine(dir, "M4.kdbx"); SaveTo(d, m4);
            var t = Open(m4); var s = Open(ph); t.MergeIn(s, PwMergeMethod.Synchronize); s.Close(); var res = Path.Combine(dir, "M5.kdbx"); SaveTo(t, res);
            var r = Open(res); Out.WriteLine("  Confirm again after the phone's stamp (+86401), then synchronized: V is " + Agent(r, E(r, "V"))); r.Close();
        }
    }

    // ---- C5: the rules group emptied out of the bin in one copy, then one synchronize ----
    static void RunRulesGroupGone()
    {
        Out.WriteLine();
        Out.WriteLine("== C5: the rules group deleted for good in one copy (a phone tidy-up, then the bin emptied), then one KeePassLib synchronize ==");
        var dir = Path.Combine(Work, "c5"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase2(basePath, null);
        var ph = Path.Combine(dir, "Ph.kdbx");
        {
            var d = Open(basePath); var g = Rules(d);
            foreach (var e in g.GetEntries(true)) d.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(30)));
            d.DeletedObjects.Add(new PwDeletedObject(g.Uuid, At(30))); g.ParentGroup.Groups.Remove(g);
            SaveTo(d, ph);
        }
        { var r = Open(ph); Out.WriteLine(Line("phone copy after the bin was emptied", r)); r.Close(); }
        foreach (var into in new[] { "KeePass", "phone" })
        {
            var t = Open(into == "KeePass" ? basePath : ph); var s = Open(into == "KeePass" ? ph : basePath);
            t.MergeIn(s, PwMergeMethod.Synchronize); s.Close();
            var res = Path.Combine(dir, "sync-into-" + into + ".kdbx"); SaveTo(t, res);
            var r = Open(res);
            var texts = 0; Walk(r.RootGroup, e => { if (Get(e, KindF) == "rule") texts++; });
            Out.WriteLine(Line("synchronized into " + into + " (rule texts left in the file: " + texts.ToString(Inv) + ")", r));
            r.Close();
        }
        {
            var cell = Path.Combine(dir, "xc"); Directory.CreateDirectory(cell);
            var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
            File.Copy(ph, target, true); File.Copy(basePath, source, true);
            var ex = Cli(Xc280, cell, "merge", "-s", target, source);
            var r = Open(target); Out.WriteLine(Line("KeePassXC 2.8.0 merge of the intact copy into the phone's (exit " + ex + ")", r)); r.Close();
        }
    }

    // ---- C6: File then Import with its preselected Create new IDs, then Delete Duplicate Entries ----
    static void RunImportCleanup()
    {
        Out.WriteLine();
        Out.WriteLine("== C6: a copy of the database imported with Create new IDs (ImportMethodForm.cs:37, :69), then Delete Duplicate Entries to clean up ==");
        var dir = Path.Combine(Work, "c6"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase2(basePath, null);
        var self = Open(basePath); var again = Open(basePath);
        self.MergeIn(again, PwMergeMethod.CreateNewUuids); again.Close();
        var p1 = Path.Combine(dir, "imported.kdbx"); SaveTo(self, p1);
        { var r = Open(p1); Out.WriteLine(Line("after the import", r)); r.Close(); }
        var d = Open(p1);
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(d.RootGroup, e => kinds[Hex(e.Uuid)] = (Get(e, KindF) ?? "user") + ":" + e.Strings.ReadSafe(PwDefs.TitleField) + "@" + e.ParentGroup.Name);
        var n = d.DeleteDuplicateEntries(null);
        var left = new HashSet<string>(StringComparer.Ordinal); Walk(d.RootGroup, e => left.Add(Hex(e.Uuid)));
        var gone = new List<string>(); foreach (var kv in kinds) if (!left.Contains(kv.Key)) gone.Add(kv.Value);
        gone.Sort(StringComparer.Ordinal);
        Out.WriteLine("  Delete Duplicate Entries deleted " + n.ToString(Inv) + ": " + string.Join(", ", gone.ToArray()));
        var intact = 0; var displaced = 0;
        foreach (var i in Read(d).Insts) { if (i.Intact) intact++; else displaced++; }
        Out.WriteLine("  rule versions left: intact " + intact.ToString(Inv) + ", displaced " + displaced.ToString(Inv));
        var p2 = Path.Combine(dir, "cleaned.kdbx"); SaveTo(d, p2);
        { var r = Open(p2); Out.WriteLine(Line("after the clean-up", r)); r.Close(); }
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        Xc242 = args[2]; Xc280 = args[3];
        using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("rules-linked-survival probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString);
            Out.WriteLine("store group " + Hex(StoreUuid) + ", rules group " + Hex(RulesUuid) + ", profile read: " + Profile);
            Out.WriteLine("notation: <entry>@<group>=<what agent gets>[<the entry's own state>]; '-' is nothing; REFUSE(...) a refusal");
            RunCopies2();
            RunRollback2();
            RunClock();
            RunRulesGroupGone();
            RunImportCleanup();
        }
        return 0;
    }
}
