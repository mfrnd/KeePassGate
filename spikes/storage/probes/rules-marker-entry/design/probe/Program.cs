// Probe for the storage option "access rules as a marker entry inside the group they govern".
// Invented content only. Console only: KeePassLib 2.62 loaded from KeePass.exe, no window.
//
// It makes one base database and two copies, A and B, that each change rules the way the design's
// editor would, then evaluates any result file with the design's own read path and prints, for
// every scenario, what the gate would enforce there.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    const string GroupNotes = "Access rule for KeePassGate. Change it in KeePass, on the KPG tab of this group. Deleting or editing this entry closes the group to KeePassGate clients.";
    const string EntryNotes = "Access rule for KeePassGate. Change it in KeePass, on the KPG tab of the entry it names. Deleting or editing this entry closes that entry to KeePassGate clients.";

    const string Wide = "{\"default\":\"inherit\",\"ops\":[\"read\",\"list\"],\"reader\":[\"read\",\"list\"]}";
    const string Narrow = "{\"default\":\"inherit\",\"reader\":[\"read\"]}";
    const string Other = "{\"default\":\"inherit\",\"ops\":[\"read\"],\"reader\":[\"read\",\"list\"]}";
    const string Sealed = "{\"default\":\"deny\",\"reader\":[\"list\"]}";

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
        if (args.Length >= 2 && args[0] == "eval") { for (int i = 1; i < args.Length; i++) Eval(args[i]); return 0; }
        if (args.Length >= 2 && args[0] == "dump") { for (int i = 1; i < args.Length; i++) Dump(args[i]); return 0; }
        if (args.Length >= 3 && args[0] == "dedupe") { Dedupe(args[1], args[2]); return 0; }
        if (args.Length >= 3 && args[0] == "handcopy") { HandCopy(args[1], args[2]); return 0; }
        if (args.Length >= 4 && args[0] == "killhead") { KillHead(args[1], args[2], args[3]); return 0; }
        if (args.Length >= 3 && args[0] == "tamper") { Tamper(args[1], args[2]); return 0; }
        if (args.Length >= 1 && args[0] == "cost") { Cost(); return 0; }
        Console.Error.WriteLine("usage: make <dir> | kpmerge <target> <source> <method> <out> | eval <file>... | dump <file>... | dedupe <in> <out> | handcopy <in> <out> | cost");
        return 2;
    }

    // ---- identity -------------------------------------------------------------------------

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

    static PwGroup Store(PwDatabase db)
    {
        var g = db.RootGroup.FindGroup(StoreGroupUuid(), true);
        if (g != null) return g;
        g = new PwGroup(false, false);
        g.Uuid = StoreGroupUuid();
        g.Name = "KeePassGate";
        g.IconId = PwIcon.TerminalEncrypted;
        g.Notes = "KeePassGate data. Managed on the KPG tab in KeePass.";
        g.EnableSearching = false;
        g.EnableAutoType = false;
        Stamp(g, T0);
        db.RootGroup.AddGroup(g, true);
        return g;
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
        var e = Shell("KPG ruled " + node.Substring(0, node.IndexOf('/')) + " " + node.Substring(node.IndexOf('/') + 1, 8),
            "KeePassGate data. Managed on the KPG tab in KeePass.", t);
        e.Uuid = id;
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "ruled"));
        e.Strings.Set("KPG.Data", new ProtectedString(false, "{\"v\":\"1\",\"node\":\"" + node + "\"}"));
        Store(db).AddEntry(e, true);
    }

    static PwEntry RuleEntry(string node, string after, string rule, DateTime t)
    {
        bool group = node.StartsWith("group/");
        var e = Shell(group ? "KeePassGate rule (this group)" : "KeePassGate rule (entry " + node.Substring(6, 8) + ")",
            group ? GroupNotes : EntryNotes, t);
        e.Uuid = RuleUuid(node, after, rule);
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "rule"));
        e.Strings.Set("KPG.Node", new ProtectedString(false, node));
        e.Strings.Set("KPG.After", new ProtectedString(false, after));
        e.Strings.Set("KPG.Rule", new ProtectedString(false, rule));
        return e;
    }

    // What the editor does at OK: one new rule entry naming every rule entry it replaces, the
    // replaced ones removed with a deletion record, the witness added if the node has none.
    const string Silent = "{\"default\":\"inherit\"}";

    static void Replaced(PwDatabase db, PwEntry rule, DateTime t)
    {
        var hex = rule.Uuid.ToHexString();
        var id = ReplacedUuid(rule.Uuid);
        if (db.RootGroup.FindEntry(id, true) != null) return;
        var e = Shell("KPG replaced rule " + hex.Substring(0, 8), "KeePassGate data. Managed on the KPG tab in KeePass.", t);
        e.Uuid = id;
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "replaced"));
        e.Strings.Set("KPG.Data", new ProtectedString(false, "{\"v\":\"1\",\"entry\":\"" + hex + "\"}"));
        Store(db).AddEntry(e, true);
    }

    static PwEntry Save(PwDatabase db, PwGroup carrier, string node, string rule, DateTime t)
    {
        var view = new View(db);
        // The root carries a rule entry from the first rule on, saying nothing until it is given a rule.
        var root = NodeOf(db.RootGroup);
        if (node != root && !view.ByNode.ContainsKey(root) && !view.HasWitness(root))
        {
            db.RootGroup.AddEntry(RuleEntry(root, "none", Silent, t), true);
            Witness(db, root, t);
        }
        // Every rule entry of this node that still counts, wherever it is outside the bin.
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

    static void Delete(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    static PwGroup Bin(PwDatabase db, DateTime t)
    {
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        if (bin != null) return bin;
        bin = new PwGroup(true, false, "Recycle Bin", PwIcon.TrashBin);
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

    static void Move(PwGroup g, PwGroup to, DateTime t)
    {
        var parent = g.ParentGroup;
        parent.Groups.Remove(g);
        g.PreviousParentGroup = parent.Uuid;
        to.AddGroup(g, true);
        g.LocationChanged = t;
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

    static readonly string[] Scenarios = {
        "G01-supersede-A", "G02-concurrent", "G03-first-both-differ", "G04-first-both-same", "G05-first-both-same-tie",
        "G06-marker-binned-A", "G07-marker-deleted-A", "G08-moved-A-rule-B", "G09-group-binned-A-rule-B",
        "G10-group-deleted-A-rule-B", "G11-group-touched-B", "G12-entry-rule-decoupled", "G13-entry-moved-A-rule-B",
        "G14-entry-moved-rule-both", "G15-tie-differ", "G16-marker-lost-no-record-A" };

    static readonly HashSet<string> WithBaseRule = new HashSet<string> {
        "G01-supersede-A", "G02-concurrent", "G06-marker-binned-A", "G07-marker-deleted-A", "G08-moved-A-rule-B",
        "G09-group-binned-A-rule-B", "G10-group-deleted-A-rule-B", "G11-group-touched-B", "G16-marker-lost-no-record-A" };

    static readonly HashSet<string> EntryRuled = new HashSet<string> {
        "G12-entry-rule-decoupled", "G13-entry-moved-A-rule-B", "G14-entry-moved-rule-both" };

    static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        var a = T0.AddHours(1);
        var b = T0.AddHours(2);

        var basePath = Path.Combine(dir, "base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(basePath), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        Store(db);
        var h = new PwGroup(true, false) { Name = "H-elsewhere" };
        Stamp(h, T0);
        db.RootGroup.AddGroup(h, true);
        User(h, "user in H", T0);
        var general = new PwGroup(true, false) { Name = "General" };
        Stamp(general, T0);
        db.RootGroup.AddGroup(general, true);
        User(general, "user in General", T0);
        foreach (var name in Scenarios)
        {
            var g = new PwGroup(true, false) { Name = name };
            Stamp(g, T0);
            db.RootGroup.AddGroup(g, true);
            var x = User(g, "X-" + name.Substring(0, 3), T0);
            if (WithBaseRule.Contains(name)) Save(db, g, NodeOf(g), Wide, T0);
            if (EntryRuled.Contains(name)) Save(db, g, NodeOf(x), Wide, T0);
        }
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Wide, T0);
        User(db.RootGroup, "user at the root", T0);
        db.Save(null); db.Close();
        File.Copy(basePath, Path.Combine(dir, "A.kdbx"), true);
        File.Copy(basePath, Path.Combine(dir, "B.kdbx"), true);

        // ---- copy A, at T0 + 1 h ----
        db = Open(Path.Combine(dir, "A.kdbx"));
        PwGroup g1;
        Save(db, db.RootGroup, NodeOf(db.RootGroup), Sealed, a);
        g1 = G(db, "G01-supersede-A"); Save(db, g1, NodeOf(g1), Sealed, a);
        g1 = G(db, "G02-concurrent"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G03-first-both-differ"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G04-first-both-same"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G05-first-both-same-tie"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G06-marker-binned-A"); Recycle(db, g1.Entries.First(e => e.Strings.Exists("KPG.Rule")), a);
        g1 = G(db, "G07-marker-deleted-A"); Delete(db, g1.Entries.First(e => e.Strings.Exists("KPG.Rule")), a);
        g1 = G(db, "G08-moved-A-rule-B"); Move(g1, G(db, "H-elsewhere"), a);
        g1 = G(db, "G09-group-binned-A-rule-B"); Move(g1, Bin(db, a), a);
        g1 = G(db, "G10-group-deleted-A-rule-B");
        foreach (var e in g1.Entries.ToList()) Delete(db, e, a);
        g1.ParentGroup.Groups.Remove(g1);
        db.DeletedObjects.Add(new PwDeletedObject(g1.Uuid, a));
        g1 = G(db, "G11-group-touched-B"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G12-entry-rule-decoupled"); Save(db, g1, NodeOf(E(db, "X-G12")), Narrow, a);
        Move(E(db, "X-G13"), G(db, "H-elsewhere"), a);
        g1 = G(db, "G14-entry-moved-rule-both"); Save(db, g1, NodeOf(E(db, "X-G14")), Narrow, a);
        g1 = G(db, "G15-tie-differ"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G16-marker-lost-no-record-A");
        g1.Entries.Remove(g1.Entries.First(e => e.Strings.Exists("KPG.Rule")));   // as a program that records no deletion would
        db.Save(null); db.Close();

        // ---- copy B, at T0 + 2 h unless a scenario is about a tie ----
        db = Open(Path.Combine(dir, "B.kdbx"));
        g1 = G(db, "G02-concurrent"); Save(db, g1, NodeOf(g1), Other, b);
        g1 = G(db, "G03-first-both-differ"); Save(db, g1, NodeOf(g1), Other, b);
        g1 = G(db, "G04-first-both-same"); Save(db, g1, NodeOf(g1), Narrow, b);
        g1 = G(db, "G05-first-both-same-tie"); Save(db, g1, NodeOf(g1), Narrow, a);
        g1 = G(db, "G08-moved-A-rule-B"); Save(db, g1, NodeOf(g1), Narrow, b);
        g1 = G(db, "G09-group-binned-A-rule-B"); Save(db, g1, NodeOf(g1), Narrow, b);
        g1 = G(db, "G10-group-deleted-A-rule-B"); Save(db, g1, NodeOf(g1), Narrow, b);
        g1 = G(db, "G11-group-touched-B");
        g1.Notes = "notes changed in B"; g1.LastModificationTime = b;
        var x11 = E(db, "X-G11"); x11.CreateBackup(db); x11.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "changed-in-B")); x11.LastModificationTime = b;
        var x12 = E(db, "X-G12"); x12.CreateBackup(db); x12.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "changed-in-B")); x12.LastModificationTime = b;
        g1 = G(db, "G13-entry-moved-A-rule-B"); Save(db, g1, NodeOf(E(db, "X-G13")), Narrow, b);
        var x14 = E(db, "X-G14"); Move(x14, G(db, "H-elsewhere"), b); Save(db, G(db, "H-elsewhere"), NodeOf(x14), Other, b);
        g1 = G(db, "G15-tie-differ"); Save(db, g1, NodeOf(g1), Other, a);
        db.Save(null); db.Close();
    }

    // ---- the design's read path -----------------------------------------------------------

    sealed class Rule
    {
        public PwEntry Entry;
        public string Node;
        public List<string> After;
        public string Text;
    }

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
        public int Seen, Candidates;

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
                Seen++;
                var kind = e.Strings.Get("KPG.Kind");
                if (kind == null) continue;
                Candidates++;
                var k = kind.ReadString();
                if (k == "ruled")
                {
                    // A refusing record: it counts wherever it is, the bin included, by what it says.
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

        // A rule entry no longer counts once a record says it was replaced, once a deletion record
        // names it or that record, or once another rule entry of its node names it, binned or not.
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

        // The document the resolver is handed for this node: null is silence, "!" is a document
        // that will not read, which refuses there.
        public string Document(string node, PwGroup sitsIn, out string why)
        {
            if (sitsIn != null && Closed.Contains(sitsIn)) { why = "an entry here claims to be a rule and is not one"; return "!"; }
            List<Rule> own;
            ByNode.TryGetValue(node, out own);
            own = own ?? new List<Rule>();
            var heads = Alive(node);
            if (heads.Count == 0)
            {
                if (own.Count > 0) { why = "every rule entry left is a replaced or deleted one"; return "!"; }
                if (HasWitness(node)) { why = "ruled, and no rule entry is left"; return "!"; }
                why = "never ruled"; return null;
            }
            if (heads.Count == 1) { why = "one rule" + (own.Count > 1 ? ", " + (own.Count - 1) + " replaced one(s) still here" : ""); return heads[0].Text; }
            why = heads.Count + " rules that never saw each other: the narrower of them";
            return Meet(heads.Select(r => r.Text).ToList());
        }
    }

    static Dictionary<string, HashSet<string>> Profiles(string doc, out bool deny)
    {
        deny = doc.StartsWith("{\"default\":\"deny\"");
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(doc, ",\"([A-Za-z0-9_-]{1,64})\":\\[([^\\]]*)\\]"))
        {
            var words = new HashSet<string>(m.Groups[2].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('"')));
            result[m.Groups[1].Value] = words;
        }
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

    static string Reader(View view, PwEntry entry, PwGroup from)
    {
        string why;
        if (entry != null)
        {
            var own = view.Document(NodeOf(entry), null, out why);
            var a = Answer(own);
            if (a != null) return a;
        }
        for (var g = from; g != null; g = g.ParentGroup)
        {
            var a = Answer(view.Document(NodeOf(g), g, out why));
            if (a != null) return a;
        }
        return "nothing";
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

    static string PathOf(PwGroup g) { return g.ParentGroup == null ? "" : (PathOf(g.ParentGroup) + "/" + g.Name).TrimStart('/'); }

    static bool In(PwGroup g, PwGroup bin)
    {
        for (var c = g; c != null; c = c.ParentGroup) if (ReferenceEquals(c, bin)) return true;
        return false;
    }

    static string Label(string doc)
    {
        if (doc == null) return "SILENCE";
        if (doc == "!") return "REFUSE";
        if (doc == Wide) return "wide";
        if (doc == Narrow) return "narrow";
        if (doc == Other) return "other";
        if (doc == Sealed) return "sealed";
        if (doc == Silent) return "says-nothing";
        return doc;
    }

    static void Eval(string file)
    {
        var db = Open(file);
        var view = new View(db);
        Console.WriteLine("# " + Path.GetFileName(Path.GetDirectoryName(file)) + "/" + Path.GetFileName(file) + " kdbx " + FormatVersion(file)
            + " entries=" + view.Seen + " kpg=" + view.Candidates + " deletionRecords=" + db.DeletedObjects.UCount
            + " bin=" + (view.Bin == null ? "none" : "'" + PathOf(view.Bin) + "'"));
        {
            string rwhy;
            var rdoc = view.Document(NodeOf(db.RootGroup), db.RootGroup, out rwhy);
            Console.WriteLine("  G00-root @'' : " + Label(rdoc) + " (" + rwhy + ") reader=" + Reader(view, null, db.RootGroup) + " entries=[" + string.Join("; ", db.RootGroup.Entries.Select(Describe)) + "]");
        }
        foreach (var name in Scenarios)
        {
            var groups = db.RootGroup.GetGroups(true).Where(g => g.Name == name).ToList();
            if (groups.Count == 0) { Console.WriteLine("  " + name + " : GROUP ABSENT"); continue; }
            foreach (var g in groups)
            {
                string why;
                var doc = view.Document(NodeOf(g), g, out why);
                var where = In(g, view.Bin) ? "[in the bin, out of reach] " : "";
                Console.WriteLine("  " + name + " @'" + PathOf(g.ParentGroup ?? g) + "' : " + where + Label(doc) + " (" + why + ")"
                    + " reader=" + (In(g, view.Bin) ? "out-of-reach" : Reader(view, null, g)) + " entries=[" + string.Join("; ", g.Entries.Select(Describe)) + "]");
            }
            if (!EntryRuled.Contains(name)) continue;
            foreach (var x in db.RootGroup.GetEntries(true).Where(e => e.Strings.ReadSafe(PwDefs.TitleField) == "X-" + name.Substring(0, 3)))
            {
                string why;
                var doc = view.Document(NodeOf(x), null, out why);
                string gwhy;
                var gdoc = view.Document(NodeOf(x.ParentGroup), x.ParentGroup, out gwhy);
                Console.WriteLine("    entry " + Describe(x) + " in '" + PathOf(x.ParentGroup) + "' : own " + Label(doc) + " (" + why + "); its group " + Label(gdoc)
                    + "; password " + x.Strings.ReadSafe(PwDefs.PasswordField) + " hist=" + x.History.UCount
                    + " reader=" + (In(x.ParentGroup, view.Bin) ? "out-of-reach" : Reader(view, x, x.ParentGroup)));
            }
        }
        // Rule entries that sit anywhere else, so that nothing is missed.
        foreach (var g in db.RootGroup.GetGroups(true).Where(g => !Scenarios.Contains(g.Name)))
        {
            var rules = g.Entries.Where(e => e.Strings.Exists("KPG.Rule")).ToList();
            if (rules.Count > 0) Console.WriteLine("  (in '" + PathOf(g) + "'" + (In(g, view.Bin) ? ", the bin" : "") + (view.Closed.Contains(g) ? ", CLOSED" : "") + ": " + string.Join("; ", rules.Select(Describe)) + ")");
        }
        db.Close();
    }

    static string Describe(PwEntry e)
    {
        if (!e.Strings.Exists("KPG.Kind")) return e.Strings.ReadSafe(PwDefs.TitleField);
        var text = e.Strings.ReadSafe("KPG.Rule");
        bool binds = RuleUuid(e.Strings.ReadSafe("KPG.Node"), e.Strings.ReadSafe("KPG.After"), text).Equals(e.Uuid);
        return "rule:" + Label(text) + (e.Strings.ReadSafe("KPG.Node").StartsWith("entry/") ? ":for-entry" : "") + (binds ? "" : ":UNBOUND")
            + (e.History.UCount > 0 ? ":hist" + e.History.UCount : "") + (e.Strings.GetKeys().Count != 9 ? ":strings" + e.Strings.GetKeys().Count : "");
    }

    static string FormatVersion(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    static void Dump(string file)
    {
        var db = Open(file);
        Console.WriteLine("# " + file);
        foreach (var e in db.RootGroup.GetEntries(true).Where(e => e.Strings.Exists("KPG.Kind")))
        {
            Console.WriteLine("  " + e.Uuid.ToHexString() + " in '" + PathOf(e.ParentGroup) + "' mod=" + e.LastModificationTime.ToString("s") + " autotype=" + e.AutoType.Enabled + " icon=" + e.IconId);
            foreach (var kv in e.Strings) Console.WriteLine("      " + kv.Key + (kv.Value.IsProtected ? " [protected]" : "") + " = " + kv.Value.ReadString());
        }
        db.Close();
    }

    // ---- KeePass's own Delete Duplicate Entries ---------------------------------------------

    static void Dedupe(string input, string output)
    {
        var db = Open(input);
        uint before = db.RootGroup.GetEntriesCount(true);
        uint deleted = db.DeleteDuplicateEntries(null);
        Console.WriteLine("DeleteDuplicateEntries on " + Path.GetFileName(input) + ": " + before + " entries, " + deleted + " deleted");
        db.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        db.Close();
    }

    // A person duplicates the rule entry of G01 by hand: a copy under a new identifier, a second later.
    static void HandCopy(string input, string output)
    {
        var db = Open(input);
        var g = G(db, "G01-supersede-A");
        var original = g.Entries.First(e => e.Strings.Exists("KPG.Rule"));
        var copy = original.CloneDeep();
        copy.SetUuid(new PwUuid(true), true);
        copy.LastModificationTime = original.LastModificationTime.AddSeconds(1);
        g.AddEntry(copy, true);
        db.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        db.Close();
    }

    // A person edits the rule text of G02's rule entry by hand, to the wide rule, a minute later.
    static void Tamper(string input, string output)
    {
        var db = Open(input);
        var g = G(db, "G02-concurrent");
        var rule = g.Entries.First(e => e.Strings.Exists("KPG.Rule"));
        rule.CreateBackup(db);
        rule.Strings.Set("KPG.Rule", new ProtectedString(false, Wide));
        rule.LastModificationTime = rule.LastModificationTime.AddMinutes(1);
        // and only the title of G11's, which the identifier does not cover
        var other = G(db, "G11-group-touched-B").Entries.First(e => e.Strings.Exists("KPG.Rule"));
        other.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "renamed by hand"));
        other.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "notes changed by hand"));
        other.LastModificationTime = other.LastModificationTime.AddMinutes(1);
        db.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        db.Close();
    }

    static void KillHead(string input, string output, string groupName)
    {
        var db = Open(input);
        var g = G(db, groupName);
        var view = new View(db);
        foreach (var r in view.Alive(NodeOf(g))) Delete(db, r.Entry, DateTime.UtcNow);
        db.SaveAs(IOConnectionInfo.FromPath(output), false, null);
        db.Close();
    }

    // ---- what the read costs ---------------------------------------------------------------

    static void Cost()
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Path.GetTempPath(), "kpg-rme-never-saved.kdbx")), Key());
        var rnd = new Random(1);
        int groups = 500, perGroup = 20;
        var all = new List<PwGroup>();
        for (int i = 0; i < groups; i++)
        {
            var g = new PwGroup(true, true) { Name = "g" + i };
            (i % 5 == 0 ? db.RootGroup : all[rnd.Next(all.Count)]).AddGroup(g, true);
            all.Add(g);
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
        for (int i = 0; i < 5000; i++) db.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), DateTime.UtcNow));
        Store(db);
        int ruledGroups = 0, ruledEntries = 0;
        for (int i = 0; i < groups; i += 5) { Save2(db, all[i], NodeOf(all[i]), Wide); ruledGroups++; }
        for (int i = 2; i < groups; i += 5) { Save2(db, all[i], NodeOf(all[i].Entries.GetAt(0)), Narrow); ruledEntries++; }
        Save2(db, db.RootGroup, NodeOf(db.RootGroup), Wide); ruledGroups++;

        Console.WriteLine("entries " + db.RootGroup.GetEntriesCount(true) + ", groups " + db.RootGroup.GetGroups(true).UCount
            + ", deletion records " + db.DeletedObjects.UCount + ", ruled groups " + ruledGroups + ", ruled entries " + ruledEntries);

        for (int i = 0; i < 20; i++) new View(db);
        int n = 200;
        var sw = Stopwatch.StartNew();
        View view = null;
        for (int i = 0; i < n; i++) view = new View(db);
        sw.Stop();
        Console.WriteLine("building the view (one pass over every entry, every rule entry checked against its identifier, the deletion records into a set): "
            + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms; entries seen " + view.Seen + ", KPG entries " + view.Candidates + ", ruled nodes " + view.ByNode.Count);

        // The pass alone, with no check of any rule entry.
        Func<int> bare = delegate
        {
            int found = 0;
            var stack = new Stack<PwGroup>();
            stack.Push(db.RootGroup);
            while (stack.Count > 0)
            {
                var g = stack.Pop();
                foreach (var e in g.Entries) if (e.Strings.Exists("KPG.Kind")) found++;
                foreach (var s in g.Groups) stack.Push(s);
            }
            return found;
        };
        for (int i = 0; i < 20; i++) bare();
        sw.Restart();
        int f = 0;
        for (int i = 0; i < n; i++) f = bare();
        sw.Stop();
        Console.WriteLine("the pass alone, testing each entry for the KPG.Kind string: " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.000") + " ms, found " + f);

        // A listing: every entry asks for its own document and for each group's above it.
        var entries = db.RootGroup.GetEntries(true);
        sw.Restart();
        int asked = 0, refused = 0;
        for (int i = 0; i < 20; i++)
        {
            asked = 0; refused = 0;
            foreach (var e in entries)
            {
                string why;
                if (view.Document(NodeOf(e), null, out why) == "!") refused++;
                asked++;
                for (var g = e.ParentGroup; g != null; g = g.ParentGroup)
                {
                    if (view.Document(NodeOf(g), g, out why) == "!") refused++;
                    asked++;
                }
            }
        }
        sw.Stop();
        Console.WriteLine("a listing of every entry, each asking for its own document and for every group's above it (" + asked + " lookups, nothing remembered between them): "
            + (sw.Elapsed.TotalMilliseconds / 20).ToString("0.000") + " ms, refused " + refused);

        // The same with the node names remembered, as a per-request cache would.
        sw.Restart();
        for (int i = 0; i < 20; i++)
        {
            var memo = new Dictionary<PwGroup, string>();
            foreach (var e in entries)
            {
                string why;
                view.Document(NodeOf(e), null, out why);
                for (var g = e.ParentGroup; g != null; g = g.ParentGroup)
                {
                    string doc;
                    if (!memo.TryGetValue(g, out doc)) memo[g] = view.Document(NodeOf(g), g, out why);
                }
            }
        }
        sw.Stop();
        Console.WriteLine("the same listing with each group's document remembered for the request: " + (sw.Elapsed.TotalMilliseconds / 20).ToString("0.000") + " ms");

        // One rule entry's check on its own.
        var one = view.ByNode.Values.First()[0];
        sw.Restart();
        bool ok = false;
        for (int i = 0; i < 100000; i++) ok = RuleUuid(one.Node, "none", one.Text).Equals(one.Entry.Uuid);
        sw.Stop();
        Console.WriteLine("checking one rule entry against its identifier (SHA-256 over " + (("KPG/1/rule/" + one.Node + "\nnone\n" + one.Text).Length) + " bytes): "
            + (sw.Elapsed.TotalMilliseconds * 1000 / 100000).ToString("0.00") + " microseconds, " + ok);
    }

    static void Save2(PwDatabase db, PwGroup carrier, string node, string rule)
    {
        var e = RuleEntry(node, "none", rule, DateTime.UtcNow);
        carrier.AddEntry(e, true);
        Witness(db, node, DateTime.UtcNow);
    }
}
