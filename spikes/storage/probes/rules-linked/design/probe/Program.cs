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

    // The base every scenario starts from.
    //   U   a group opened to agent: read, list (a grant; the root grants nothing)
    //     X   an entry narrowed to nothing for agent (or another rule when asked, or none)
    //     Y, Z  entries with no rule
    //   S   a group sealed (default deny)
    //     C   a group with no rule
    //       Q   an entry with no rule
    //   W   a group with no rule
    //     V   an entry with no rule
    //   KeePassGate        the store group: one record per rule version
    //   KeePassGate rules  the rule versions
    static void MakeBase(string path, string xDoc)
    {
        var db = NewDb("base-new.kdbx");
        PluginGroup(db, StoreUuid, "KeePassGate");
        PluginGroup(db, RulesUuid, "KeePassGate rules");
        var u = AddGroup(db.RootGroup, "U", Fixed("group U"), T0);
        var x = AddEntry(u, Fixed("entry X"), "X", T0); Password(x, "x-secret-0");
        var y = AddEntry(u, Fixed("entry Y"), "Y", T0); Password(y, "y-secret-0");
        var z = AddEntry(u, Fixed("entry Z"), "Z", T0); Password(z, "z-secret-0");
        var s = AddGroup(db.RootGroup, "S", Fixed("group S"), T0);
        var c = AddGroup(s, "C", Fixed("group C"), T0);
        var q = AddEntry(c, Fixed("entry Q"), "Q", T0); Password(q, "q-secret-0");
        var w = AddGroup(db.RootGroup, "W", Fixed("group W"), T0);
        var vv = AddEntry(w, Fixed("entry V"), "V", T0); Password(vv, "v-secret-0");
        FirstRule(db, u, RG0, "base-U", T0);
        FirstRule(db, s, SEAL, "base-S", T0);
        if (xDoc != null) FirstRule(db, x, xDoc, "base-X", T0, false);
        SaveTo(db, path);
    }

    static PwEntry E(PwDatabase db, string name) { return db.RootGroup.FindEntry(Fixed("entry " + name), true); }
    static PwGroup G(PwDatabase db, string name) { return db.RootGroup.FindGroup(Fixed("group " + name), true); }
    static string AnchorOfX(PwDatabase db) { return E(db, "X").Strings.ReadSafe(AnchorF); }

    sealed class Scenario { public string Name, What, XDoc = RXN; public Action<PwDatabase> A, B; }

    static List<Scenario> Scenarios()
    {
        var l = new List<Scenario>();
        l.Add(new Scenario
        {
            Name = "01", What = "A gives Y its first rule, a narrowing (agent: nothing), at +10; B changes Y's password at +20",
            A = db => FirstRule(db, E(db, "Y"), RXN, "A-Y", At(10), true),
            B = db => Edit(E(db, "Y"), At(20), e => Password(e, "y-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "02", What = "A gives V its first rule, a grant (agent: read), at +10; B changes V's password at +20",
            A = db => FirstRule(db, E(db, "V"), RXR, "A-V", At(10), true),
            B = db => Edit(E(db, "V"), At(20), e => Password(e, "v-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "03", What = "as 01, both in the same second (+10)",
            A = db => FirstRule(db, E(db, "Y"), RXN, "A-Y", At(10), true),
            B = db => Edit(E(db, "Y"), At(10), e => Password(e, "y-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "04", What = "X is ruled in the base (agent: nothing). A changes X's rule to agent: list at +10; B changes X's password at +20",
            A = db => ChangeRule(db, AnchorOfX(db), RXL, "A-X2", At(10)),
            B = db => Edit(E(db, "X"), At(20), e => Password(e, "x-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "05", What = "X ruled agent: read, list, protected in the base. A changes it to agent: list, B to agent: read, neither seeing the other",
            XDoc = RXW,
            A = db => ChangeRule(db, AnchorOfX(db), RXL, "A-X2", At(10)),
            B = db => ChangeRule(db, AnchorOfX(db), RXR, "B-X2", At(20))
        });
        l.Add(new Scenario
        {
            Name = "06", What = "A gives Y a first rule (agent: nothing) at +10, B gives Y a first rule (agent: list) at +20: two anchors",
            A = db => FirstRule(db, E(db, "Y"), RXN, "A-Y", At(10), true),
            B = db => FirstRule(db, E(db, "Y"), RXL, "B-Y", At(20), true)
        });
        l.Add(new Scenario
        {
            Name = "07", What = "A gives group W its first rule, a grant (agent: read, list), at +10; B adds an entry to W and edits W's notes at +20",
            A = db => FirstRule(db, G(db, "W"), RG0, "A-W", At(10)),
            B = db => { var w = G(db, "W"); var n = AddEntry(w, Fixed("entry N"), "N", At(20)); Password(n, "n-secret"); w.Notes = "edited in B"; w.LastModificationTime = At(20); }
        });
        l.Add(new Scenario
        {
            Name = "08", What = "A gives group W a first rule (agent: read, list) at +10, B gives W a first rule (agent: read) at +20: two links, two anchors",
            A = db => FirstRule(db, G(db, "W"), RG0, "A-W", At(10)),
            B = db => FirstRule(db, G(db, "W"), RXR, "B-W", At(20))
        });
        l.Add(new Scenario
        {
            Name = "09", What = "A duplicates entry X inside U, group S and group U (PwEntry.Duplicate, PwGroup.Duplicate, as KeePass's commands do); B changes Z's password",
            A = db =>
            {
                var x = E(db, "X"); var x2 = x.Duplicate(); Set(x2, PwDefs.TitleField, "X - Copy"); x.ParentGroup.AddEntry(x2, true, true);
                var s2 = G(db, "S").Duplicate(); s2.Name = "S - Copy"; db.RootGroup.AddGroup(s2, true);
                var u2 = G(db, "U").Duplicate(); u2.Name = "U - Copy"; db.RootGroup.AddGroup(u2, true);
            },
            B = db => Edit(E(db, "Z"), At(20), e => Password(e, "z-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "10", What = "A deletes X's rule entry for good, by hand, with a deletion record, at +10; B changes X's password at +20",
            A = db => { var v = Read(db); foreach (var i in v.Insts) if (i.Node == NodeOf(E(db, "X"))) DeleteForGood(db, i.E, At(10), true); },
            B = db => Edit(E(db, "X"), At(20), e => Password(e, "x-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "11", What = "A moves U's link entry into W by hand at +10 (location only); B changes Y's password at +20",
            A = db => { var u = G(db, "U"); PwEntry link = null; foreach (var e in u.Entries) if (Get(e, KindF) == "link") link = e; u.Entries.Remove(link); G(db, "W").AddEntry(link, true, true); link.LocationChanged = At(10); },
            B = db => Edit(E(db, "Y"), At(20), e => Password(e, "y-secret-B"))
        });
        l.Add(new Scenario
        {
            Name = "12", What = "X is ruled in the base (agent: nothing). A deletes X's back reference by hand (an ordinary edit at +30); B changes Z's password at +20",
            XDoc = RXN,
            A = db => Edit(E(db, "X"), At(30), e => e.Strings.Remove(AnchorF)),
            B = db => Edit(E(db, "Z"), At(20), e => Password(e, "z-secret-B"))
        });
        return l;
    }

    static string Prepare(Scenario sc, out string a, out string b)
    {
        var dir = Path.Combine(Work, sc.Name);
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase(basePath, sc.XDoc);
        a = Path.Combine(dir, "A.kdbx"); b = Path.Combine(dir, "B.kdbx");
        var da = Open(basePath); sc.A(da); SaveTo(da, a);
        var dbb = Open(basePath); sc.B(dbb); SaveTo(dbb, b);
        return dir;
    }

    static void Inputs(string a, string b)
    {
        var da = Open(a); Out.WriteLine("  A alone: " + State(da)); da.Close();
        var db = Open(b); Out.WriteLine("  B alone: " + State(db)); db.Close();
    }

    static void RunKeePass()
    {
        Out.WriteLine();
        Out.WriteLine("== KeePassLib " + PwDefs.VersionString + ", PwDatabase.MergeIn, the result saved, reopened and read ==");
        var methods = new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting };
        foreach (var sc in Scenarios())
        {
            string a, b;
            var dir = Prepare(sc, out a, out b);
            Out.WriteLine();
            Out.WriteLine(sc.Name + ": " + sc.What);
            Inputs(a, b);
            var results = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var m in methods)
                foreach (var into in new[] { "A", "B" })
                {
                    var target = Open(into == "A" ? a : b);
                    var source = Open(into == "A" ? b : a);
                    target.MergeIn(source, m);
                    source.Close();
                    var res = Path.Combine(dir, "kp-" + m + "-into" + into + ".kdbx");
                    SaveTo(target, res);
                    var read = Open(res);
                    var st = State(read) + "  (X back ref " + (E(read, "X").Strings.Exists(AnchorF) ? "yes" : "no") + ", Y back ref " + (E(read, "Y").Strings.Exists(AnchorF) ? "yes" : "no") + ", V back ref " + (E(read, "V").Strings.Exists(AnchorF) ? "yes" : "no") + ")";
                    read.Close();
                    if (!results.ContainsKey(st)) { results[st] = new List<string>(); order.Add(st); }
                    results[st].Add(m + " into " + into);
                }
            foreach (var st in order) Out.WriteLine("  [" + string.Join("; ", results[st].ToArray()) + "]: " + st);
        }
    }

    // ---- single-file cases ----

    static void One(string name, string what, string xDoc, Action<PwDatabase> act)
    {
        var dir = Path.Combine(Work, name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, xDoc);
        var db = Open(path);
        var before = State(db);
        act(db);
        var res = Path.Combine(dir, "after.kdbx");
        SaveTo(db, res);
        var read = Open(res);
        Out.WriteLine();
        Out.WriteLine(name + ": " + what);
        Out.WriteLine("  before: " + before);
        Out.WriteLine("  after:  " + State(read));
        read.Close();
    }

    static List<Inst> InstsOf(PwDatabase db, string node) { var r = new List<Inst>(); foreach (var i in Read(db).Insts) if (i.Node == node) r.Add(i); return r; }

    static PwEntry RecordOf(PwDatabase db, string id)
    {
        PwEntry found = null;
        Walk(db.RootGroup, e => { if (Get(e, KindF) == "issued" && Get(e, VersionF) == id) found = e; });
        return found;
    }

    static void RunSingles()
    {
        Out.WriteLine();
        Out.WriteLine("== single file: what a person can do by hand, the result saved, reopened and read (KeePassLib " + PwDefs.VersionString + ") ==");
        One("h01", "X's back reference changed by hand to text that is no anchor", RXN, db => Edit(E(db, "X"), At(10), e => Set(e, AnchorF, "not an anchor")));
        One("h02", "X's back reference changed by hand to a well formed anchor nothing carries", RXN, db => Edit(E(db, "X"), At(10), e => Set(e, AnchorF, Hex(Fixed("nothing")))));
        One("h03", "X's back reference changed by hand to U's anchor (a rule that names another node)", RXN, db => Edit(E(db, "X"), At(10), e => Set(e, AnchorF, Hex(Fixed("link base-U")))));
        One("h04", "X's back reference deleted by hand", RXN, db => Edit(E(db, "X"), At(10), e => e.Strings.Remove(AnchorF)));
        One("h05", "Y, unruled, given X's anchor by hand", RXN, db => Edit(E(db, "Y"), At(10), e => Set(e, AnchorF, AnchorOfX(db))));
        One("h06", "V, unruled and outside every grant, given U's anchor by hand (a grant's anchor)", RXN, db => Edit(E(db, "V"), At(10), e => Set(e, AnchorF, Hex(Fixed("link base-U")))));
        One("h07", "X's rule entry edited by hand: KPG.Data widened to agent: read, list, protected", RXN, db => { foreach (var i in InstsOf(db, NodeOf(E(db, "X")))) Edit(i.E, At(10), e => Set(e, DataF, RXW)); });
        One("h08", "X's rule entry deleted with no trace (no deletion record), its record kept", RXN, db => { foreach (var i in InstsOf(db, NodeOf(E(db, "X")))) DeleteForGood(db, i.E, At(10), false); });
        One("h09", "X's rule entry and its record both deleted with no trace", RXN, db => { foreach (var i in InstsOf(db, NodeOf(E(db, "X")))) { DeleteForGood(db, RecordOf(db, i.Id), At(10), false); DeleteForGood(db, i.E, At(10), false); } });
        One("h10", "as h09, and X's back reference deleted too: three acts in three places", RXN, db => { foreach (var i in InstsOf(db, NodeOf(E(db, "X")))) { DeleteForGood(db, RecordOf(db, i.Id), At(10), false); DeleteForGood(db, i.E, At(10), false); } Edit(E(db, "X"), At(10), e => e.Strings.Remove(AnchorF)); });
        One("h11", "the whole rules group deleted with no trace", RXN, db => db.RootGroup.Groups.Remove(Rules(db)));
        One("h12", "the rules group and the store group both deleted with no trace", RXN, db => { db.RootGroup.Groups.Remove(Rules(db)); db.RootGroup.Groups.Remove(Store(db)); });
        One("h13", "U's link entry deleted for good", RXN, db => { var u = G(db, "U"); PwEntry link = null; foreach (var e in u.Entries) if (Get(e, KindF) == "link") link = e; DeleteForGood(db, link, At(10), true); });
        One("h14", "S's link entry (the seal's) deleted for good", RXN, db => { var s = G(db, "S"); PwEntry link = null; foreach (var e in s.Entries) if (Get(e, KindF) == "link") link = e; DeleteForGood(db, link, At(10), true); });
        One("h15", "X's rule entry moved to the root by hand, its record into U", RXN, db => { foreach (var i in InstsOf(db, NodeOf(E(db, "X")))) { var r = RecordOf(db, i.Id); r.ParentGroup.Entries.Remove(r); G(db, "U").AddEntry(r, true, true); i.E.ParentGroup.Entries.Remove(i.E); db.RootGroup.AddEntry(i.E, true, true); } });
        One("h16", "KeePass's Delete Duplicate Entries after X was copied by a drag (PwEntry.Duplicate, same strings)", RXN, db =>
        {
            var x = E(db, "X"); var x2 = x.Duplicate(); x.ParentGroup.AddEntry(x2, true, true);
            var n = db.DeleteDuplicateEntries(null);
            Out.WriteLine(); Out.WriteLine("  h16: DeleteDuplicateEntries deleted " + n.ToString(Inv) + "; the original X is " + (E(db, "X") == null ? "gone, the copy stayed" : "still there"));
        });
        One("h17", "X restored to its revision from before its first rule (the rule given at +10 as an edit, then the revision restored)", null, db =>
        {
            var x = E(db, "X");
            FirstRule(db, x, RXN, "late-X", At(10), true);
            x.CreateBackup(null); x.RestoreFromBackup(0, null); x.LastModificationTime = At(20);
        });
    }

    // ---- copies ----

    static string Strings(PwEntry e)
    {
        var keys = new List<string>(e.Strings.GetKeys()); keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var k in keys) sb.Append(k).Append(e.Strings.Get(k).IsProtected ? "[p]" : "").Append('=').Append(k == PwDefs.PasswordField ? "(" + e.Strings.ReadSafe(k).Length.ToString(Inv) + " chars)" : e.Strings.ReadSafe(k)).Append("; ");
        return sb.ToString();
    }

    static void RunCopies()
    {
        Out.WriteLine();
        Out.WriteLine("== what KeePass's own copying carries (KeePassLib " + PwDefs.VersionString + ") ==");
        var dir = Path.Combine(Work, "copies"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, RXN);
        var db = Open(path);
        var x = E(db, "X");
        var d = x.Duplicate();
        Out.WriteLine("PwEntry.Duplicate (Duplicate Entry, a copying drag, Add Entry from a template): new identifier " + (!d.Uuid.Equals(x.Uuid)) + ", KPG.Anchor kept " + (Get(d, AnchorF) == Get(x, AnchorF)));
        Out.WriteLine("  original: " + Strings(x));
        Out.WriteLine("  copy:     " + Strings(d));
        var s = G(db, "S"); var s2 = s.Duplicate();
        PwEntry l1 = null, l2 = null;
        foreach (var e in s.Entries) if (Get(e, KindF) == "link") l1 = e;
        foreach (var e in s2.Entries) if (Get(e, KindF) == "link") l2 = e;
        Out.WriteLine("PwGroup.Duplicate (Duplicate Group, a copying drag of a group): new group identifier " + (!s2.Uuid.Equals(s.Uuid)) + ", link entry copied " + (l2 != null) + ", its identifier new " + (l2 != null && !l2.Uuid.Equals(l1.Uuid)) + ", its KPG.Anchor still the original's " + (l2 != null && Get(l2, AnchorF) == Get(l1, AnchorF)) + ", so the copy's identifier is no longer its anchor " + (l2 != null && Hex(l2.Uuid) != Get(l2, AnchorF)));
        // The clipboard path: KeePass writes the entries with KdbxFile.WriteEntries and reads them back with ReadEntries.
        using (var ms = new MemoryStream())
        {
            KdbxFile.WriteEntries(ms, db, new[] { x });
            using (var rd = new MemoryStream(ms.ToArray()))
            {
                var back = KdbxFile.ReadEntries(rd, db, true);
                Out.WriteLine("KdbxFile.WriteEntries then ReadEntries (copy and paste of entries): " + back.Count.ToString(Inv) + " entry, new identifier " + (!back[0].Uuid.Equals(x.Uuid)) + ", KPG.Anchor kept " + (Get(back[0], AnchorF) == Get(x, AnchorF)));
            }
        }
        db.Close();

        // File then Import with Create new IDs, a copy of the database into itself.
        var self = Open(path); var again = Open(path);
        self.MergeIn(again, PwMergeMethod.CreateNewUuids); again.Close();
        var res = Path.Combine(dir, "newids.kdbx"); SaveTo(self, res);
        var read = Open(res);
        Out.WriteLine();
        Out.WriteLine("Create new IDs, a copy of the base imported into itself:");
        Out.WriteLine("  " + State(read));
        read.Close();
    }

    // ---- a foreign file that plants a rule ----

    static void RunPlant()
    {
        Out.WriteLine();
        Out.WriteLine("== a foreign file, sharing only public identifiers with the victim, merged in (KeePassLib " + PwDefs.VersionString + ") ==");
        var dir = Path.Combine(Work, "plant"); Directory.CreateDirectory(dir);
        var victim = Path.Combine(dir, "victim.kdbx");
        MakeBase(victim, RXN);
        foreach (var withLink in new[] { false, true })
        {
            var f = NewDb("foreign-new.kdbx");
            f.RootGroup.Uuid = Fixed("foreign root"); f.RootGroup.Name = "foreign";
            var anchorC = Hex(Fixed("link planted-C"));
            // a rule for group C (under the seal S, no rule of its own) and one for entry Q
            AddVersion(f, anchorC, "group/" + Hex(Fixed("group C")), RXW, new string[0], "plant-C", At(50), true);
            AddVersion(f, Hex(Fixed("anchor planted-V")), "entry/" + Hex(Fixed("entry V")), RXW, new string[0], "plant-V", At(50), true);
            if (withLink)
            {
                var s = AddGroup(f.RootGroup, "S", Fixed("group S"), T0);
                var c = AddGroup(s, "C", Fixed("group C"), T0);
                var l = PluginEntry(c, FromHex(anchorC), "KeePassGate link", At(50));
                Set(l, KindF, "link"); Set(l, AnchorF, anchorC);
            }
            var fpath = Path.Combine(dir, withLink ? "foreign-link.kdbx" : "foreign.kdbx");
            SaveTo(f, fpath);
            Out.WriteLine();
            Out.WriteLine(withLink ? "the foreign file holds rule entries naming group C and entry V, and a link entry inside a group that carries C's identifier:" : "the foreign file holds rule entries naming group C and entry V, and nothing else:");
            foreach (var m in new[] { PwMergeMethod.Synchronize, PwMergeMethod.KeepExisting, PwMergeMethod.OverwriteExisting, PwMergeMethod.CreateNewUuids })
            {
                var t = Open(victim); var src = Open(fpath);
                t.MergeIn(src, m); src.Close();
                var res = Path.Combine(dir, "res-" + (withLink ? "link-" : "") + m + ".kdbx");
                SaveTo(t, res);
                var read = Open(res);
                Out.WriteLine("  " + m + ": " + State(read));
                read.Close();
            }
        }
    }

    // ---- a lost newest version, and a stale copy ----

    static void RunRollback(string xcExe, string xcVersion)
    {
        Out.WriteLine();
        Out.WriteLine("== the newest version lost, and a stale copy merged in afterwards" + (xcExe != null ? " by keepassxc-cli " + xcVersion : " (KeePassLib Synchronize)") + " ==");
        var dir = Path.Combine(Work, "rollback" + (xcVersion ?? "")); Directory.CreateDirectory(dir);
        var stale = Path.Combine(dir, "stale.kdbx");
        MakeBase(stale, RXW);
        Out.WriteLine("the stale copy holds X ruled agent: read, list, protected. The current copy narrowed X to agent: nothing (the old version deleted with its deletion record).");
        foreach (var how in new[] { "rule entry lost, no trace", "rule entry and its record lost, no trace" })
        {
            var cur = Open(stale);
            var id = ChangeRule(cur, AnchorOfX(cur), RXN, "cur-X2", At(10));
            foreach (var i in InstsOf(cur, NodeOf(E(cur, "X")))) if (i.Id == id) DeleteForGood(cur, i.E, At(20), false);
            if (how.StartsWith("rule entry and")) DeleteForGood(cur, RecordOf(cur, id), At(20), false);
            var curPath = Path.Combine(dir, (how.StartsWith("rule entry and") ? "cur-both" : "cur-one") + ".kdbx");
            Out.WriteLine("  " + how + ", before any merge: " + State(cur));
            SaveTo(cur, curPath);
            if (xcExe == null)
            {
                var t = Open(curPath); var src = Open(stale);
                t.MergeIn(src, PwMergeMethod.Synchronize); src.Close();
                var res = curPath + ".merged.kdbx"; SaveTo(t, res);
                var read = Open(res);
                Out.WriteLine("  " + how + ", then the stale copy synchronized in: " + State(read));
                read.Close();
            }
            else
            {
                var source = Path.Combine(dir, "stale-copy.kdbx"); File.Copy(stale, source, true);
                var exit = Cli(xcExe, dir, "merge", "-s", curPath, source);
                var read = Open(curPath);
                Out.WriteLine("  " + how + ", then the stale copy merged in (exit " + exit + "): " + State(read));
                read.Close();
            }
        }
    }

    // ---- KeePassXC ----

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

    static string PluginListing(PwDatabase db)
    {
        var rows = new List<string>();
        Walk(db.RootGroup, e =>
        {
            if (!IsPlugin(e) && !e.Strings.Exists(AnchorF)) return;
            rows.Add(Hex(e.Uuid) + " in " + e.ParentGroup.Name + " " + e.LastModificationTime.ToString("o", Inv) + " h" + e.History.UCount.ToString(Inv) + " " + Strings(e));
        });
        rows.Sort(StringComparer.Ordinal);
        return string.Join("\n", rows.ToArray());
    }

    static void RunXc(string version, string exe)
    {
        Out.WriteLine();
        Out.WriteLine("== KeePassXC " + version + ": keepassxc-cli, every result read back with KeePassLib " + PwDefs.VersionString + " ==");

        // a plain save, and an edit of another field of a ruled entry
        var dir = Path.Combine(Work, "xc" + version + "-save"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, RXN);
        var before = Open(path); var was = PluginListing(before); var stWas = State(before); before.Close();
        var exit = Cli(exe, dir, "add", path, "W/xc-added");
        var read = Open(path); var now = PluginListing(read);
        Out.WriteLine("a plain save (add, exit " + exit + "), file then KDBX " + KdbxVersion(path) + ": every plugin entry and every entry carrying KPG.Anchor unchanged in identifier, group, time, history count and strings: " + (was == now ? "yes" : "NO"));
        if (was != now) { Out.WriteLine("was:\n" + was); Out.WriteLine("now:\n" + now); }
        Out.WriteLine("  before: " + stWas);
        Out.WriteLine("  after:  " + State(read));
        read.Close();
        exit = Cli(exe, dir, "edit", "-u", "edited-in-xc", path, "U/X");
        read = Open(path);
        var x = E(read, "X");
        Out.WriteLine("an edit of X's user name (edit -u, exit " + exit + "): X's KPG.Anchor kept " + (x.Strings.Exists(AnchorF)) + ", user name now '" + x.Strings.ReadSafe(PwDefs.UserNameField) + "', history items " + x.History.UCount.ToString(Inv) + ", the history item carries KPG.Anchor " + (x.History.UCount > 0 && x.History.GetAt(x.History.UCount - 1).Strings.Exists(AnchorF)));
        Out.WriteLine("  after:  " + State(read));
        read.Close();

        foreach (var sc in Scenarios())
        {
            sc.Name = "xc" + version + "-" + sc.Name;
            string a, b;
            var sdir = Prepare(sc, out a, out b);
            Out.WriteLine();
            Out.WriteLine(sc.Name + ": " + sc.What);
            foreach (var into in new[] { "A", "B" })
            {
                var cell = Path.Combine(sdir, "into" + into); Directory.CreateDirectory(cell);
                var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
                File.Copy(into == "A" ? a : b, target, true); File.Copy(into == "A" ? b : a, source, true);
                var ex = Cli(exe, cell, "merge", "-s", target, source);
                var r = Open(target);
                Out.WriteLine("  into " + into + " (exit " + ex + "): " + State(r) + "  (X back ref " + (E(r, "X").Strings.Exists(AnchorF) ? "yes" : "no") + ", Y back ref " + (E(r, "Y").Strings.Exists(AnchorF) ? "yes" : "no") + ", V back ref " + (E(r, "V").Strings.Exists(AnchorF) ? "yes" : "no") + ")");
                r.Close();
            }
        }
        RunRollback(exe, version);
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        var what = args[1];
        using (Out = new StreamWriter(args[2], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString + ", " + what);
            Out.WriteLine("store group " + Hex(StoreUuid) + ", rules group " + Hex(RulesUuid) + ", profile read: " + Profile);
            if (what == "keepass") { RunKeePass(); RunSingles(); RunCopies(); RunPlant(); RunRollback(null, null); }
            else if (what == "xc") { RunXc(args[3], args[4]); }
            else return 2;
        }
        return 0;
    }
}
