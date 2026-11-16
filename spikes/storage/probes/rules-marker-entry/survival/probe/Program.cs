// Survival probe against the storage option "access rules as rule entries inside the group they
// govern" (option-rules-marker-entry.md). Invented content only. Console only: KeePassLib 2.62
// loaded from KeePass.exe, no window.
//
// The design's write path (Save) and read path (View, Meet, Reader) are copied from the design's
// own probe (spikes\storage\probes\rules-marker-entry\design\probe\Program.cs) without change in behavior,
// so that what is printed here is what the design's gate would enforce.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Program
{
    const string Password = "probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static DateTime H(double hours) { return T0.AddHours(hours); }

    const string Wide = "{\"default\":\"inherit\",\"ops\":[\"read\",\"list\"],\"reader\":[\"read\",\"list\"]}";
    const string Narrow = "{\"default\":\"inherit\",\"reader\":[\"read\"]}";
    const string Other = "{\"default\":\"inherit\",\"ops\":[\"read\"],\"reader\":[\"read\",\"list\"]}";
    const string Seal = "{\"default\":\"deny\"}";
    const string Silent = "{\"default\":\"inherit\"}";

    static int Main(string[] args)
    {
        var dir = args.Length > 1 ? args[1] : null;
        switch (args.Length > 0 ? args[0] : "")
        {
            case "s1": S1(dir, false); S1(dir, true); return 0;
            case "s2": S2(dir); return 0;
            case "s3": S3(dir); return 0;
            case "s4make": S4Make(dir); return 0;
            case "s5": S5(dir); return 0;
            case "s6make": S6Make(dir); return 0;
            case "s6dx": S6Dx(args[1], args[2], args[3]); return 0;
            case "s7": S7(dir); return 0;
            case "s8": S8(dir); return 0;
            case "s9": S9(dir); return 0;
            case "kpmerge":
                {
                    var target = Open(args[1]);
                    var source = Open(args[2]);
                    target.MergeIn(source, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), args[3]), null);
                    target.SaveAs(IOConnectionInfo.FromPath(args[4]), false, null);
                    target.Close(); source.Close();
                    return 0;
                }
            case "eval":
                for (int i = 1; i < args.Length; i++) { var db = Open(args[i]); Eval(db, Path.GetFileName(Path.GetDirectoryName(args[i])) + "/" + Path.GetFileName(args[i])); db.Close(); }
                return 0;
        }
        Console.Error.WriteLine("usage: s1|s2|s3|s4make|s5|s6make|s7|s8|s9 <dir> | s6dx <open> <arriving> <out> | kpmerge <t> <s> <method> <out> | eval <file>...");
        return 2;
    }

    // ---- identity (as the design) -----------------------------------------------------------

    static PwUuid First16(string text)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var b = new byte[16];
            Array.Copy(d, b, 16);
            return new PwUuid(b);
        }
    }

    static PwUuid StoreGroupUuid() { return First16("KPG/1/group/"); }
    static PwUuid WitnessUuid(string node) { return First16("KPG/1/ruled/" + node); }
    static PwUuid ReplacedUuid(PwUuid rule) { return First16("KPG/1/replaced/" + rule.ToHexString()); }
    static PwUuid RuleUuid(string node, string after, string rule) { return First16("KPG/1/rule/" + node + "\n" + after + "\n" + rule); }
    static string NodeOf(PwGroup g) { return "group/" + g.Uuid.ToHexString(); }
    static string NodeOf(PwEntry e) { return "entry/" + e.Uuid.ToHexString(); }

    // ---- building ------------------------------------------------------------------------------

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

    static PwDatabase NewDb(string path)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        Store(db);
        return db;
    }

    static PwGroup Store(PwDatabase db)
    {
        var g = db.RootGroup.FindGroup(StoreGroupUuid(), true);
        if (g != null) return g;
        g = new PwGroup(false, false);
        g.Uuid = StoreGroupUuid();
        g.Name = "KeePassGate";
        g.EnableSearching = false;
        g.EnableAutoType = false;
        Stamp(g, T0);
        db.RootGroup.AddGroup(g, true);
        return g;
    }

    static PwGroup Group(PwGroup parent, string name, DateTime t)
    {
        var g = new PwGroup(true, false) { Name = name };
        Stamp(g, t);
        parent.AddGroup(g, true);
        return g;
    }

    static PwEntry User(PwGroup g, string title, DateTime t)
    {
        var e = new PwEntry(true, false);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "user"));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-" + title));
        Stamp(e, t);
        g.AddEntry(e, true);
        return e;
    }

    static PwEntry Shell(string title, string notes, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, notes));
        e.IconId = PwIcon.TerminalEncrypted;
        e.AutoType.Enabled = false;
        Stamp(e, t);
        return e;
    }

    static void Witness(PwDatabase db, string node, DateTime t)
    {
        var id = WitnessUuid(node);
        if (db.RootGroup.FindEntry(id, true) != null) return;
        var e = Shell("KPG ruled " + node.Substring(0, node.IndexOf('/')) + " " + node.Substring(node.IndexOf('/') + 1, 8), "KeePassGate data.", t);
        e.Uuid = id;
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "ruled"));
        e.Strings.Set("KPG.Data", new ProtectedString(false, "{\"v\":\"1\",\"node\":\"" + node + "\"}"));
        Store(db).AddEntry(e, true);
    }

    static PwEntry RuleEntry(string node, string after, string rule, DateTime t)
    {
        bool group = node.StartsWith("group/");
        var e = Shell(group ? "KeePassGate rule (this group)" : "KeePassGate rule (entry " + node.Substring(6, 8) + ")", "Access rule for KeePassGate.", t);
        e.Uuid = RuleUuid(node, after, rule);
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "rule"));
        e.Strings.Set("KPG.Node", new ProtectedString(false, node));
        e.Strings.Set("KPG.After", new ProtectedString(false, after));
        e.Strings.Set("KPG.Rule", new ProtectedString(false, rule));
        return e;
    }

    static void Replaced(PwDatabase db, PwEntry rule, DateTime t)
    {
        var hex = rule.Uuid.ToHexString();
        var id = ReplacedUuid(rule.Uuid);
        if (db.RootGroup.FindEntry(id, true) != null) return;
        var e = Shell("KPG replaced rule " + hex.Substring(0, 8), "KeePassGate data.", t);
        e.Uuid = id;
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "replaced"));
        e.Strings.Set("KPG.Data", new ProtectedString(false, "{\"v\":\"1\",\"entry\":\"" + hex + "\"}"));
        Store(db).AddEntry(e, true);
    }

    // The design's editor at OK (design section 4.1, and Save in the design's probe), unchanged.
    static PwEntry Save(PwDatabase db, PwGroup carrier, string node, string rule, DateTime t)
    {
        var view = new View(db);
        var root = NodeOf(db.RootGroup);
        if (node != root && !view.ByNode.ContainsKey(root) && !view.HasWitness(root))
        {
            db.RootGroup.AddEntry(RuleEntry(root, "none", Silent, t), true);
            Witness(db, root, t);
        }
        var found = view.Alive(node);
        var ids = new List<string>();
        foreach (var r in found)
        {
            if (!ids.Contains(r.Entry.Uuid.ToHexString())) ids.Add(r.Entry.Uuid.ToHexString());
            foreach (var a in r.After) if (!ids.Contains(a)) ids.Add(a);
        }
        var after = ids.Count == 0 ? "none" : string.Join(",", ids.Take(128));
        var e = RuleEntry(node, after, rule, t);
        carrier.AddEntry(e, true);
        List<Rule> all;
        view.ByNode.TryGetValue(node, out all);
        foreach (var r in all ?? new List<Rule>()) { Replaced(db, r.Entry, t); Delete(db, r.Entry, t); }
        Witness(db, node, t);
        return e;
    }

    // A removal for good, as KeePass records one: the object goes and a deletion record names it.
    static void Delete(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    // KeePass's Delete to the bin (MainForm_Functions.cs:5138-5166): the bin made when there is none,
    // the entry moved, its location time set, its modification time left alone.
    static PwGroup Bin(PwDatabase db, DateTime t)
    {
        var bin = db.RecycleBinUuid.Equals(PwUuid.Zero) ? null : db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        if (bin != null) return bin;
        bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
        bin.EnableAutoType = false;
        bin.EnableSearching = false;
        Stamp(bin, t);
        db.RootGroup.AddGroup(bin, true);
        db.RecycleBinEnabled = true;
        db.RecycleBinUuid = bin.Uuid;
        db.RecycleBinChanged = t;
        return bin;
    }

    static void Recycle(PwDatabase db, PwEntry e, DateTime t)
    {
        var bin = Bin(db, t);
        var parent = e.ParentGroup;
        parent.Entries.Remove(e);
        e.PreviousParentGroup = parent.Uuid;
        bin.AddEntry(e, true);
        e.LocationChanged = t;
    }

    // KeePass's Empty Recycle Bin for entries: each removed with a deletion record dated now.
    static void EmptyBin(PwDatabase db, DateTime t)
    {
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        foreach (var e in bin.Entries.ToList()) Delete(db, e, t);
    }

    static void Move(PwEntry e, PwGroup to, DateTime t)
    {
        var parent = e.ParentGroup;
        parent.Entries.Remove(e);
        e.PreviousParentGroup = parent.Uuid;
        to.AddEntry(e, true);
        e.LocationChanged = t;
    }

    static PwGroup G(PwDatabase db, string name)
    {
        foreach (var g in db.RootGroup.GetGroups(true)) if (g.Name == name) return g;
        throw new InvalidOperationException("no group " + name);
    }

    static PwEntry E(PwDatabase db, string title)
    {
        foreach (var e in db.RootGroup.GetEntries(true)) if (e.Strings.ReadSafe(PwDefs.TitleField) == title) return e;
        throw new InvalidOperationException("no entry " + title);
    }

    static PwEntry RuleOf(PwDatabase db, string node)
    {
        return db.RootGroup.GetEntries(true).Where(e => e.Strings.ReadSafe("KPG.Kind") == "rule" && e.Strings.ReadSafe("KPG.Node") == node).Last();
    }

    static bool HasDuplicates(PwDatabase db)
    {
        return (bool)typeof(PwDatabase).GetMethod("HasDuplicateUuids", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(db, null);
    }

    static void FixDuplicates(PwDatabase db)
    {
        typeof(PwDatabase).GetMethod("FixDuplicateUuids", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(db, null);
    }

    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); }

    // ---- the design's read path, unchanged -----------------------------------------------------

    sealed class Rule { public PwEntry Entry; public string Node; public List<string> After; public string Text; }

    static readonly Regex NodePattern = new Regex("^(group|entry)/[0-9A-F]{32}$", RegexOptions.CultureInvariant);
    static readonly Regex AfterPattern = new Regex("^(none|[0-9A-F]{32}(,[0-9A-F]{32}){0,127})$", RegexOptions.CultureInvariant);
    static readonly Regex DocPattern = new Regex("^\\{\"default\":\"(inherit|deny)\"((,\"[A-Za-z0-9_-]{1,64}\":\\[(\"[a-z]+\"(,\"[a-z]+\")*)?\\])*)\\}$", RegexOptions.CultureInvariant);

    sealed class View
    {
        public readonly Dictionary<string, List<Rule>> ByNode = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<Rule>> Binned = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        public readonly HashSet<PwGroup> Closed = new HashSet<PwGroup>();
        public readonly HashSet<string> Witnessed = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> ReplacedIds = new HashSet<string>(StringComparer.Ordinal);
        public readonly PwGroup Bin;
        readonly HashSet<PwUuid> _deleted = new HashSet<PwUuid>();

        public View(PwDatabase db)
        {
            Bin = db.RecycleBinUuid == null || db.RecycleBinUuid.Equals(PwUuid.Zero) ? null : db.RootGroup.FindGroup(db.RecycleBinUuid, true);
            foreach (var d in db.DeletedObjects) _deleted.Add(d.Uuid);
            Walk(db.RootGroup, false);
        }

        void Walk(PwGroup g, bool binned)
        {
            binned = binned || ReferenceEquals(g, Bin);
            foreach (var e in g.Entries)
            {
                var kind = e.Strings.Get("KPG.Kind");
                if (kind == null) continue;
                var k = kind.ReadString();
                if (k == "ruled")
                {
                    var m = Regex.Match(e.Strings.ReadSafe("KPG.Data"), "\"node\":\"((group|entry)/[0-9A-F]{32})\"");
                    if (m.Success) Witnessed.Add(m.Groups[1].Value);
                    continue;
                }
                if (k == "replaced")
                {
                    var m = Regex.Match(e.Strings.ReadSafe("KPG.Data"), "\"entry\":\"([0-9A-F]{32})\"");
                    if (m.Success) ReplacedIds.Add(m.Groups[1].Value);
                    continue;
                }
                if (k != "rule") continue;
                var node = e.Strings.ReadSafe("KPG.Node");
                var after = e.Strings.ReadSafe("KPG.After");
                var text = e.Strings.ReadSafe("KPG.Rule");
                bool reads = NodePattern.IsMatch(node) && AfterPattern.IsMatch(after) && DocPattern.IsMatch(text)
                             && RuleUuid(node, after, text).Equals(e.Uuid);
                if (!reads) { if (!binned) Closed.Add(g); continue; }
                var into = binned ? Binned : ByNode;
                List<Rule> list;
                if (!into.TryGetValue(node, out list)) into[node] = list = new List<Rule>();
                list.Add(new Rule { Entry = e, Node = node, Text = text, After = after == "none" ? new List<string>() : after.Split(',').ToList() });
            }
            foreach (var s in g.Groups) Walk(s, binned);
        }

        public bool HasWitness(string node) { return Witnessed.Contains(node) || _deleted.Contains(WitnessUuid(node)); }

        bool Dead(Rule r, HashSet<string> named)
        {
            var hex = r.Entry.Uuid.ToHexString();
            return named.Contains(hex) || ReplacedIds.Contains(hex) || _deleted.Contains(r.Entry.Uuid) || _deleted.Contains(ReplacedUuid(r.Entry.Uuid));
        }

        public List<Rule> Alive(string node)
        {
            List<Rule> own, binned;
            ByNode.TryGetValue(node, out own);
            Binned.TryGetValue(node, out binned);
            own = own ?? new List<Rule>();
            var named = new HashSet<string>(own.Concat(binned ?? new List<Rule>()).SelectMany(r => r.After));
            return own.Where(r => !Dead(r, named)).ToList();
        }

        public string Document(string node, PwGroup sitsIn, out string why)
        {
            if (sitsIn != null && Closed.Contains(sitsIn)) { why = "closed: an entry here claims to be a rule and is not one"; return "!"; }
            List<Rule> own;
            ByNode.TryGetValue(node, out own);
            own = own ?? new List<Rule>();
            var heads = Alive(node);
            if (heads.Count == 0)
            {
                if (own.Count > 0) { why = "every rule entry left is replaced or deleted"; return "!"; }
                if (HasWitness(node)) { why = "ruled, and no rule entry counts"; return "!"; }
                why = "never ruled"; return null;
            }
            if (heads.Count == 1) { why = "one rule"; return heads[0].Text; }
            why = heads.Count + " rules: the narrower";
            return Meet(heads.Select(r => r.Text).ToList());
        }
    }

    static Dictionary<string, HashSet<string>> Profiles(string doc, out bool deny)
    {
        deny = doc.StartsWith("{\"default\":\"deny\"");
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(doc, ",\"([A-Za-z0-9_-]{1,64})\":\\[([^\\]]*)\\]"))
            result[m.Groups[1].Value] = new HashSet<string>(m.Groups[2].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('"')));
        return result;
    }

    static readonly string[] Words = { "create", "read", "update", "strip", "delete", "list", "attachments", "protected" };

    static string Meet(List<string> docs)
    {
        bool anyDeny = false;
        var parsed = new List<Dictionary<string, HashSet<string>>>();
        foreach (var d in docs) { bool deny; parsed.Add(Profiles(d, out deny)); anyDeny |= deny; }
        var names = new SortedSet<string>(parsed.SelectMany(p => p.Keys), StringComparer.Ordinal);
        var sb = new StringBuilder("{\"default\":\"" + (anyDeny ? "deny" : "inherit") + "\"");
        foreach (var name in names)
        {
            IEnumerable<string> words = Words;
            foreach (var p in parsed)
            {
                HashSet<string> w;
                words = p.TryGetValue(name, out w) ? words.Where(w.Contains).ToList() : new List<string>();
            }
            sb.Append(",\"" + name + "\":[" + string.Join(",", words.Select(w => "\"" + w + "\"")) + "]");
        }
        return sb.Append("}").ToString();
    }

    static string Answer(string doc)
    {
        if (doc == null) return null;
        if (doc == "!") return "REFUSED";
        bool deny;
        var profiles = Profiles(doc, out deny);
        HashSet<string> words;
        if (profiles.TryGetValue("reader", out words)) return words.Count == 0 ? "nothing" : string.Join("+", Words.Where(words.Contains));
        return deny ? "nothing" : null;
    }

    static string Reader(View view, PwEntry entry, PwGroup from, out string at)
    {
        string why;
        if (entry != null)
        {
            var a = Answer(view.Document(NodeOf(entry), null, out why));
            if (a != null) { at = "entry's own rule (" + why + ")"; return a; }
        }
        for (var g = from; g != null; g = g.ParentGroup)
        {
            var a = Answer(view.Document(NodeOf(g), g, out why));
            if (a != null) { at = "'" + g.Name + "' (" + why + ")"; return a; }
        }
        at = "nowhere";
        return "nothing";
    }

    static bool InBin(PwGroup g, PwGroup bin) { for (var c = g; c != null; c = c.ParentGroup) if (ReferenceEquals(c, bin)) return true; return false; }

    static string PathOf(PwGroup g) { return g.ParentGroup == null ? "Root" : PathOf(g.ParentGroup) + "/" + g.Name; }

    static void Eval(PwDatabase db, string label)
    {
        var view = new View(db);
        var rules = db.RootGroup.GetEntries(true).Where(e => e.Strings.ReadSafe("KPG.Kind") == "rule").ToList();
        Console.WriteLine("  [" + label + "] duplicates=" + HasDuplicates(db) + " deletionRecords=" + db.DeletedObjects.UCount
            + " bin=" + (view.Bin == null ? "none" : "'" + PathOf(view.Bin) + "'") + " ruleEntries=" + rules.Count);
        foreach (var r in rules)
        {
            bool bound = RuleUuid(r.Strings.ReadSafe("KPG.Node"), r.Strings.ReadSafe("KPG.After"), r.Strings.ReadSafe("KPG.Rule")).Equals(r.Uuid);
            Console.WriteLine("      rule " + r.Uuid.ToHexString().Substring(0, 8) + " in '" + PathOf(r.ParentGroup) + "'" + (InBin(r.ParentGroup, view.Bin) ? " (bin)" : "")
                + " " + Label(r.Strings.ReadSafe("KPG.Rule")) + " after=" + Short(r.Strings.ReadSafe("KPG.After")) + (bound ? "" : " UNBOUND"));
        }
        foreach (var g in new[] { db.RootGroup }.Concat(db.RootGroup.GetGroups(true)))
        {
            if (g.Uuid.Equals(StoreGroupUuid()) || InBin(g, view.Bin)) continue;
            string at;
            var a = Reader(view, null, g, out at);
            Console.WriteLine("    group " + PathOf(g) + " : reader=" + a + " decided at " + at);
            foreach (var e in g.Entries.Where(x => !x.Strings.Exists("KPG.Kind")))
                Console.WriteLine("      entry " + e.Strings.ReadSafe(PwDefs.TitleField) + " : reader=" + Reader(view, e, g, out at) + " decided at " + at);
        }
    }

    static string Short(string after) { return after == "none" ? "none" : string.Join(",", after.Split(',').Select(x => x.Substring(0, 8))); }

    static string Label(string doc)
    {
        if (doc == Wide) return "wide";
        if (doc == Narrow) return "narrow";
        if (doc == Other) return "other";
        if (doc == Seal) return "seal";
        if (doc == Silent) return "says-nothing";
        return doc;
    }

    static void Title(string s) { Console.WriteLine(); Console.WriteLine("== " + s); }

    // ---- S1: a head moved to the bin by hand, then the same rule saved again on the tab -----------

    static void S1(string dir, bool binFirst)
    {
        var tag = binFirst ? "s1-bin-before-N" : "s1-N-before-bin";
        Title(tag + ": first rule narrow on N; a person moves the rule entry to the bin; the operator saves narrow again");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, tag + ".kdbx");
        var db = NewDb(path);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        if (binFirst) { var old = User(db.RootGroup, "old entry", H(0)); Recycle(db, old, H(0.5)); }
        var n = Group(db.RootGroup, "N", H(0.6));
        User(n, "n1", H(0.6));
        var r1 = Save(db, n, NodeOf(n), Narrow, H(1));
        Recycle(db, r1, H(2));
        Eval(db, "after the person binned the rule entry");
        var r2 = Save(db, n, NodeOf(n), Narrow, H(3));
        Console.WriteLine("  R1 " + r1.Uuid.ToHexString() + "  R2 " + r2.Uuid.ToHexString() + "  same identifier: " + r1.Uuid.Equals(r2.Uuid));
        Eval(db, "after the operator saved narrow again, in memory");
        SaveTo(db, path); db.Close();
        db = Open(path);
        Eval(db, "saved and reopened by KeePassLib");
        FixDuplicates(db);
        Eval(db, "after KeePass's own repair (FixDuplicateUuids, as PreSavingEx runs it before every save)");
        SaveTo(db, System.IO.Path.Combine(dir, tag + "-repaired.kdbx"));
        db.Close();
        db = Open(path);
        EmptyBin(db, H(4));
        Eval(db, "unrepaired, then Empty Recycle Bin");
        SaveTo(db, System.IO.Path.Combine(dir, tag + "-emptied.kdbx"));
        db.Close();
    }

    // ---- S2: a head deleted for good by hand, then the same rule saved again -------------------

    static void S2(string dir)
    {
        Title("s2: first rule narrow on N; a person deletes the rule entry for good; the operator saves narrow again");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "s2.kdbx");
        var db = NewDb(path);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var n = Group(db.RootGroup, "N", H(0.5));
        User(n, "n1", H(0.5));
        var r1 = Save(db, n, NodeOf(n), Narrow, H(1));
        Delete(db, r1, H(2));
        var r2 = Save(db, n, NodeOf(n), Narrow, H(3));
        Console.WriteLine("  same identifier: " + r1.Uuid.Equals(r2.Uuid));
        Eval(db, "after the operator saved narrow again");
        SaveTo(db, path); db.Close();
        db = Open(path);
        r2 = RuleOf(db, NodeOf(G(db, "N")));
        var r3 = Save(db, G(db, "N"), NodeOf(G(db, "N")), Other, H(4));
        Eval(db, "the operator then tries another rule, other");
        db.Close();
    }

    // ---- S3: a rule replaced, the newer one binned, the first rule saved again -------------------

    static void S3(string dir)
    {
        Title("s3: narrow, then other (replaces narrow); a person bins the head; the operator saves narrow again");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "s3.kdbx");
        var db = NewDb(path);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var n = Group(db.RootGroup, "N", H(0.5));
        User(n, "n1", H(0.5));
        var r1 = Save(db, n, NodeOf(n), Narrow, H(1));
        var r2 = Save(db, n, NodeOf(n), Other, H(2));
        Recycle(db, r2, H(3));
        var r3 = Save(db, n, NodeOf(n), Narrow, H(4));
        Console.WriteLine("  new identifier equals the replaced first one: " + r3.Uuid.Equals(r1.Uuid));
        Eval(db, "after the operator saved narrow again");
        SaveTo(db, path); db.Close();
    }

    // ---- S4: the same rule text decided later in another copy ------------------------------------

    static void S4Make(string dir)
    {
        Title("s4: base N unruled under a wide root; A rules N narrow at +1h and widens it to other at +2h; B, from the base, rules N narrow at +3h");
        Directory.CreateDirectory(dir);
        var basePath = System.IO.Path.Combine(dir, "base.kdbx");
        var db = NewDb(basePath);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var n = Group(db.RootGroup, "N", H(0));
        User(n, "n1", H(0));
        SaveTo(db, basePath); db.Close();
        File.Copy(basePath, System.IO.Path.Combine(dir, "A.kdbx"), true);
        File.Copy(basePath, System.IO.Path.Combine(dir, "B.kdbx"), true);
        db = Open(System.IO.Path.Combine(dir, "A.kdbx"));
        var a1 = Save(db, G(db, "N"), NodeOf(G(db, "N")), Narrow, H(1));
        Save(db, G(db, "N"), NodeOf(G(db, "N")), Other, H(2));
        db.Save(null); Eval(db, "A"); db.Close();
        db = Open(System.IO.Path.Combine(dir, "B.kdbx"));
        var b1 = Save(db, G(db, "N"), NodeOf(G(db, "N")), Narrow, H(3));
        db.Save(null); Eval(db, "B"); db.Close();
        Console.WriteLine("  A's first rule entry and B's are one identifier: " + a1.Uuid.Equals(b1.Uuid));
    }

    // ---- S5: two concurrent heads, then a person removes one of the two look-alikes --------------

    static void S5(string dir)
    {
        Directory.CreateDirectory(dir);
        Title("s5a: N wide; A narrows (+1h), B sets other (+2h); KeePassLib Synchronize B into A; a person then deletes one of the two same-titled entries");
        var basePath = System.IO.Path.Combine(dir, "s5a-base.kdbx");
        var db = NewDb(basePath);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var n = Group(db.RootGroup, "N", H(0));
        User(n, "n1", H(0));
        Save(db, n, NodeOf(n), Wide, H(0));
        SaveTo(db, basePath); db.Close();
        var pa = System.IO.Path.Combine(dir, "s5a-A.kdbx"); var pb = System.IO.Path.Combine(dir, "s5a-B.kdbx");
        File.Copy(basePath, pa, true); File.Copy(basePath, pb, true);
        db = Open(pa); Save(db, G(db, "N"), NodeOf(G(db, "N")), Narrow, H(1)); db.Save(null); db.Close();
        db = Open(pb); Save(db, G(db, "N"), NodeOf(G(db, "N")), Other, H(2)); db.Save(null); db.Close();
        var a = Open(pa); var b = Open(pb);
        a.MergeIn(b, PwMergeMethod.Synchronize, null);
        var merged = System.IO.Path.Combine(dir, "s5a-merged.kdbx");
        SaveTo(a, merged); a.Close(); b.Close();
        db = Open(merged); Eval(db, "merged"); db.Close();
        db = Open(merged);
        var narrow = db.RootGroup.GetEntries(true).First(e => e.Strings.ReadSafe("KPG.Rule") == Narrow);
        Recycle(db, narrow, H(3));
        Eval(db, "merged, then the narrow one of the two moved to the bin by hand");
        SaveTo(db, System.IO.Path.Combine(dir, "s5a-binned.kdbx")); db.Close();
        db = Open(merged);
        narrow = db.RootGroup.GetEntries(true).First(e => e.Strings.ReadSafe("KPG.Rule") == Narrow);
        Delete(db, narrow, H(3));
        Eval(db, "merged, then the narrow one deleted for good by hand");
        db.Close();

        Title("s5b: entry E in G with its own rule wide; A narrows E's rule (+1h, rule entry in G); B moves E to H and sets other there (+2h); Synchronize; a person deletes the rule entry left in G");
        basePath = System.IO.Path.Combine(dir, "s5b-base.kdbx");
        db = NewDb(basePath);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var g = Group(db.RootGroup, "G", H(0));
        Group(db.RootGroup, "H", H(0));
        var e0 = User(g, "E", H(0));
        Save(db, g, NodeOf(e0), Wide, H(0));
        SaveTo(db, basePath); db.Close();
        pa = System.IO.Path.Combine(dir, "s5b-A.kdbx"); pb = System.IO.Path.Combine(dir, "s5b-B.kdbx");
        File.Copy(basePath, pa, true); File.Copy(basePath, pb, true);
        db = Open(pa); Save(db, G(db, "G"), NodeOf(E(db, "E")), Narrow, H(1)); db.Save(null); db.Close();
        db = Open(pb); Move(E(db, "E"), G(db, "H"), H(2)); Save(db, G(db, "H"), NodeOf(E(db, "E")), Other, H(2)); db.Save(null); db.Close();
        foreach (var into in new[] { "A", "B" })
        {
            var t = Open(into == "A" ? pa : pb); var s = Open(into == "A" ? pb : pa);
            t.MergeIn(s, PwMergeMethod.Synchronize, null);
            merged = System.IO.Path.Combine(dir, "s5b-into" + into + ".kdbx");
            SaveTo(t, merged); t.Close(); s.Close();
            db = Open(merged); Eval(db, "Synchronize into " + into);
            var left = G(db, "G").Entries.First(x => x.Strings.ReadSafe("KPG.Kind") == "rule");
            Recycle(db, left, H(3));
            Eval(db, "into " + into + ", then the rule entry left behind in G moved to the bin by hand");
            db.Close();
        }
    }

    // ---- S6: the rule entry and its ruled record deleted by hand --------------------------------

    static void S6Make(string dir)
    {
        Title("s6: N narrow under a wide root; the rule entry and the ruled record of N deleted by hand");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "s6.kdbx");
        var db = NewDb(path);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var n = Group(db.RootGroup, "N", H(0));
        User(n, "n1", H(0));
        Save(db, n, NodeOf(n), Narrow, H(1));
        SaveTo(db, path);
        Eval(db, "as written");
        var node = NodeOf(n);
        Delete(db, RuleOf(db, node), H(2));
        Delete(db, db.RootGroup.FindEntry(WitnessUuid(node), true), H(2));
        Eval(db, "both deleted for good the way KeePass records it");
        SaveTo(db, System.IO.Path.Combine(dir, "s6-kp-deleted.kdbx"));
        db.Close();
    }

    // KeePassDX 4.5.8's merge as the Android report reads it (DatabaseKDBXMerger.kt:304-323): every
    // arriving deletion record is applied to an object older than it, and none is added to the open
    // copy's list. A simulation of a read behavior, not a run of KeePassDX.
    static void S6Dx(string open, string arriving, string output)
    {
        var o = Open(open); var a = Open(arriving);
        foreach (var d in a.DeletedObjects)
        {
            var e = o.RootGroup.FindEntry(d.Uuid, true);
            if (e != null && e.LastModificationTime < d.DeletionTime) e.ParentGroup.Entries.Remove(e);
        }
        SaveTo(o, output);
        Eval(o, "open copy after a KeePassDX-style merge of the copy that deleted them");
        o.Close(); a.Close();
    }

    // ---- S7: File then Import with new identifiers, then the tab's Remove -----------------------

    static void S7(string dir)
    {
        Title("s7: root wide; O unruled holds E sealed by its own rule and F; S sealed holds s1. The operator imports an older backup with Create new IDs, then presses Remove on each entry that claims to be a rule and is not one");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "s7.kdbx");
        var db = NewDb(path);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var o = Group(db.RootGroup, "O", H(0));
        var e = User(o, "E", H(0));
        User(o, "F", H(0));
        Save(db, o, NodeOf(e), Seal, H(0));
        var s = Group(db.RootGroup, "S", H(0));
        User(s, "s1", H(0));
        Save(db, s, NodeOf(s), Seal, H(0));
        SaveTo(db, path); db.Close();
        var backup = System.IO.Path.Combine(dir, "s7-backup.kdbx");
        File.Copy(path, backup, true);
        db = Open(path);
        var src = Open(backup);
        db.MergeIn(src, PwMergeMethod.CreateNewUuids, null);
        src.Close();
        Eval(db, "after the import");
        SaveTo(db, System.IO.Path.Combine(dir, "s7-imported.kdbx"));
        var view = new View(db);
        foreach (var g in view.Closed.ToList())
            foreach (var x in g.Entries.ToList())
                if (x.Strings.ReadSafe("KPG.Kind") == "rule" && !RuleUuid(x.Strings.ReadSafe("KPG.Node"), x.Strings.ReadSafe("KPG.After"), x.Strings.ReadSafe("KPG.Rule")).Equals(x.Uuid))
                    Delete(db, x, H(1));
        Eval(db, "after Remove on every copy");
        SaveTo(db, System.IO.Path.Combine(dir, "s7-removed.kdbx"));
        db.Close();
    }

    // ---- S8: the root's rule entry removed by hand ---------------------------------------------

    static void S8(string dir)
    {
        Title("s8: the root grants wide; groups P (narrow) and Q (unruled) under it; a person bins the rule entry at the root");
        Directory.CreateDirectory(dir);
        var db = NewDb(System.IO.Path.Combine(dir, "s8.kdbx"));
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var p = Group(db.RootGroup, "P", H(0)); User(p, "p1", H(0));
        var q = Group(db.RootGroup, "Q", H(0)); User(q, "q1", H(0));
        Save(db, p, NodeOf(p), Narrow, H(1));
        Eval(db, "as written");
        Recycle(db, RuleOf(db, NodeOf(db.RootGroup)), H(2));
        Eval(db, "the root's entry moved to the bin");
        db.Close();
    }

    // ---- S9: the bin pointer decided by a time ---------------------------------------------------

    static void S9(string dir)
    {
        Title("s9: N narrow; A bins N's rule entry by hand at +1h, making bin X; B bins a user entry at +2h, making bin Y; KeePassLib Synchronize both ways");
        Directory.CreateDirectory(dir);
        var basePath = System.IO.Path.Combine(dir, "s9-base.kdbx");
        var db = NewDb(basePath);
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, H(0));
        var n = Group(db.RootGroup, "N", H(0)); User(n, "n1", H(0));
        var gen = Group(db.RootGroup, "General", H(0)); User(gen, "x", H(0));
        Save(db, n, NodeOf(n), Narrow, H(0.5));
        SaveTo(db, basePath); db.Close();
        var pa = System.IO.Path.Combine(dir, "s9-A.kdbx"); var pb = System.IO.Path.Combine(dir, "s9-B.kdbx");
        File.Copy(basePath, pa, true); File.Copy(basePath, pb, true);
        db = Open(pa); Recycle(db, RuleOf(db, NodeOf(G(db, "N"))), H(1)); db.Save(null); Eval(db, "A"); db.Close();
        db = Open(pb); Recycle(db, E(db, "x"), H(2)); db.Save(null); db.Close();
        foreach (var into in new[] { "A", "B" })
        {
            var t = Open(into == "A" ? pa : pb); var s = Open(into == "A" ? pb : pa);
            t.MergeIn(s, PwMergeMethod.Synchronize, null);
            Eval(t, "Synchronize into " + into);
            t.Close(); s.Close();
        }
    }
}
