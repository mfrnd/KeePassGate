// Critique probe for the option "access rules as rule entries inside the group they govern".
// Invented content only. Console only: KeePassLib 2.62 loaded from KeePass.exe, no window.
//
// It reuses the design's identifier derivation, write step and read path exactly as the design's
// own probe implements them (rules-marker-entry\probe\Program.cs: RuleUuid, Save, View.Document),
// and asks one question: what happens when an operator restores a lost rule with the tab's Save,
// the remedy the design's section 4.3 offers, after the rule entry was lost in an ordinary way.
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
    const string Password = "critique-probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string Narrow = "{\"default\":\"inherit\",\"reader\":[\"read\"]}";
    const string Wide = "{\"default\":\"inherit\",\"ops\":[\"read\",\"list\"],\"reader\":[\"read\",\"list\"]}";
    const string Silent = "{\"default\":\"inherit\"}";

    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("usage: Rmc <work dir>"); return 2; }
        var work = args[0];
        Directory.CreateDirectory(work);
        Console.WriteLine("KeePassLib " + typeof(PwDatabase).Assembly.GetName().Version);

        BinThenSave(work, binFirst: true);
        BinThenSave(work, binFirst: false);
        DeletedThenSave(work);
        DuplicatedThenDeduped(work);
        TamperedThenRemoved(work);
        RootLostThenNextRule(work);
        return 0;
    }

    // ---- the design's identity, write step and read path, as its own probe has them ------------

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
        g.EnableSearching = false;
        g.EnableAutoType = false;
        Stamp(g, T0);
        db.RootGroup.AddGroup(g, true);
        return g;
    }

    static PwEntry Shell(string title, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "Access rule for KeePassGate."));
        e.IconId = PwIcon.TerminalEncrypted;
        e.AutoType.Enabled = false;
        Stamp(e, t);
        return e;
    }

    static void Witness(PwDatabase db, string node, DateTime t)
    {
        var id = WitnessUuid(node);
        if (db.RootGroup.FindEntry(id, true) != null) return;
        var e = Shell("KPG ruled " + node, t);
        e.Uuid = id;
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "ruled"));
        e.Strings.Set("KPG.Data", new ProtectedString(false, "{\"v\":\"1\",\"node\":\"" + node + "\"}"));
        Store(db).AddEntry(e, true);
    }

    static PwEntry RuleEntry(string node, string after, string rule, DateTime t)
    {
        var e = Shell("KeePassGate rule (this group)", t);
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
        var e = Shell("KPG replaced rule " + hex.Substring(0, 8), t);
        e.Uuid = id;
        e.Strings.Set("KPG.Kind", new ProtectedString(false, "replaced"));
        e.Strings.Set("KPG.Data", new ProtectedString(false, "{\"v\":\"1\",\"entry\":\"" + hex + "\"}"));
        Store(db).AddEntry(e, true);
    }

    // Section 4.1 step 3, as the design's probe implements it.
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
            if (heads.Count == 1) { why = "one rule"; return heads[0].Text; }
            why = heads.Count + " rules that never saw each other";
            return "narrower-of-" + heads.Count;
        }
    }

    // ---- helpers ------------------------------------------------------------------------------

    static string Label(string doc)
    {
        if (doc == null) return "SILENCE (inherits)";
        if (doc == "!") return "REFUSES";
        if (doc == Narrow) return "narrow rule in force";
        if (doc == Wide) return "wide rule in force";
        if (doc == Silent) return "empty root rule in force";
        return doc;
    }

    static string State(PwDatabase db, PwGroup g)
    {
        string why;
        var doc = new View(db).Document(NodeOf(g), g, out why);
        return Label(doc) + " (" + why + ")";
    }

    static bool HasDuplicateUuids(PwDatabase db)
    {
        var m = typeof(PwDatabase).GetMethod("HasDuplicateUuids", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)m.Invoke(db, null);
    }

    static void FixDuplicateUuids(PwDatabase db)
    {
        var m = typeof(PwDatabase).GetMethod("FixDuplicateUuids", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(db, null);
    }

    static PwGroup UserGroup(PwDatabase db, string name, DateTime t)
    {
        var g = new PwGroup(true, false) { Name = name };
        Stamp(g, t);
        db.RootGroup.AddGroup(g, true);
        var e = new PwEntry(true, false);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "db-primary"));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password"));
        Stamp(e, t);
        g.AddEntry(e, true);
        return g;
    }

    static PwDatabase NewDb(string path, bool binFirst, out PwGroup g)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        Store(db);
        if (binFirst) Bin(db, T0);
        g = UserGroup(db, "Servers", T0);
        if (!binFirst) Bin(db, T0);
        // The operator's first rule: on Servers, the narrow one. The root gets its empty rule entry
        // and its record, as section 2.3 says.
        Save(db, g, NodeOf(g), Narrow, T0);
        return db;
    }

    static PwEntry RuleOf(PwGroup g) { return g.Entries.First(e => e.Strings.Exists("KPG.Rule")); }

    static string Where(PwDatabase db, PwEntry e)
    {
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        return ReferenceEquals(e.ParentGroup, bin) ? "the bin" : "'" + e.ParentGroup.Name + "'";
    }

    // ---- the scenarios ------------------------------------------------------------------------

    // The operator moves the rule entry to the bin (tidying up, or Ctrl+A, Delete in the group),
    // sees the group refuse on the KPG tab, and presses Save to put the shown rule back.
    static void BinThenSave(string work, bool binFirst)
    {
        Console.WriteLine();
        Console.WriteLine("== S1 rule entry to the bin, then the tab's Save, bin made " + (binFirst ? "BEFORE" : "AFTER") + " the group");
        PwGroup g;
        var db = NewDb(Path.Combine(work, "s1-" + binFirst + ".kdbx"), binFirst, out g);
        var r1 = RuleOf(g);
        var r1Id = r1.Uuid.ToHexString();
        Console.WriteLine("  base: Servers " + State(db, g) + ", rule entry " + r1Id);
        Recycle(db, r1, T0.AddHours(1));
        Console.WriteLine("  after the move to the bin: Servers " + State(db, g));
        var r2 = Save(db, g, NodeOf(g), Narrow, T0.AddHours(2));
        Console.WriteLine("  Save writes rule entry " + r2.Uuid.ToHexString() + (r2.Uuid.Equals(r1.Uuid) ? "  == the binned one's identifier" : "  (new identifier)"));
        Console.WriteLine("  duplicate identifiers in the database: " + HasDuplicateUuids(db) + "; Servers " + State(db, g));
        // What KeePass's MainForm.PreSavingEx does before every save, after a warning box naming
        // the database as corrupted by an external program: the later one in pre-order is renamed.
        FixDuplicateUuids(db);
        Console.WriteLine("  after KeePass's repair before the save: the entry in Servers is " + RuleOf(g).Uuid.ToHexString()
            + ", the one in the bin is " + db.RootGroup.FindGroup(db.RecycleBinUuid, true).Entries.GetAt(0).Uuid.ToHexString());
        Console.WriteLine("  Servers " + State(db, g));
        db.Close();
    }

    // The operator deletes the rule entry for good (Shift+Del, or emptying the bin), sees the
    // group refuse, and presses Save.
    static void DeletedThenSave(string work)
    {
        Console.WriteLine();
        Console.WriteLine("== S2 rule entry deleted for good, then the tab's Save");
        PwGroup g;
        var basePath = Path.Combine(work, "s2-base.kdbx");
        var db = NewDb(basePath, true, out g);
        db.Save(null);
        var gId = g.Uuid;
        db.Close();
        var aPath = Path.Combine(work, "s2-A.kdbx");
        File.Copy(basePath, aPath, true);

        db = Open(aPath);
        g = db.RootGroup.FindGroup(gId, true);
        var r1 = RuleOf(g);
        Delete(db, r1, T0.AddHours(1));
        Console.WriteLine("  after the deletion: Servers " + State(db, g));
        var r2 = Save(db, g, NodeOf(g), Narrow, T0.AddHours(2));
        Console.WriteLine("  Save writes rule entry " + r2.Uuid.ToHexString() + (r2.Uuid.Equals(r1.Uuid) ? "  == the deleted one's identifier, which a deletion record names" : ""));
        Console.WriteLine("  Servers " + State(db, g));
        db.Save(null);
        db.Close();

        foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.KeepExisting })
        {
            foreach (var into in new[] { "A", "base" })
            {
                var target = Open(into == "A" ? aPath : basePath);
                var source = Open(into == "A" ? basePath : aPath);
                target.MergeIn(source, method, null);
                var tg = target.RootGroup.FindGroup(gId, true);
                Console.WriteLine("  " + method + ", " + (into == "A" ? "base merged into A" : "A merged into base")
                    + ": Servers " + State(target, tg) + ", deletion records naming the rule entry: "
                    + target.DeletedObjects.Count(d => d.Uuid.Equals(RuleOf(tg).Uuid)));
                target.Close(); source.Close();
            }
        }
    }

    // A person duplicates the rule entry (Duplicate Entry, Ctrl+C/Ctrl+V, a template), and later
    // runs Delete Duplicate Entries; then removes what is left that does not read, and presses Save.
    static void DuplicatedThenDeduped(string work)
    {
        Console.WriteLine();
        Console.WriteLine("== S3 rule entry duplicated by hand, then Delete Duplicate Entries, then Remove and Save on the tab");
        PwGroup g;
        var db = NewDb(Path.Combine(work, "s3.kdbx"), true, out g);
        var original = RuleOf(g);
        var originalId = original.Uuid;
        var copy = original.CloneDeep();
        copy.SetUuid(new PwUuid(true), true);
        copy.LastModificationTime = original.LastModificationTime.AddSeconds(1);
        copy.CreationTime = original.CreationTime.AddSeconds(1);
        g.AddEntry(copy, true);
        Console.WriteLine("  with the copy beside it: Servers " + State(db, g));
        uint deleted = db.DeleteDuplicateEntries(null);
        var left = g.Entries.Where(e => e.Strings.Exists("KPG.Rule")).ToList();
        Console.WriteLine("  DeleteDuplicateEntries deleted " + deleted + "; left in Servers: "
            + string.Join(", ", left.Select(e => e.Uuid.Equals(originalId) ? "the original" : "the copy"))
            + "; deletion record for the original: " + db.DeletedObjects.Any(d => d.Uuid.Equals(originalId)));
        Console.WriteLine("  Servers " + State(db, g));
        foreach (var e in left.Where(x => !x.Uuid.Equals(originalId)).ToList()) Delete(db, e, T0.AddHours(3));
        Console.WriteLine("  after the tab's Remove of what does not read: Servers " + State(db, g));
        var r2 = Save(db, g, NodeOf(g), Narrow, T0.AddHours(4));
        Console.WriteLine("  Save writes " + r2.Uuid.ToHexString() + (r2.Uuid.Equals(originalId) ? "  == the original's identifier" : ""));
        Console.WriteLine("  Servers " + State(db, g));
        db.Close();
    }

    // A person edits the rule text by hand (the Advanced tab in KeePass, a phone), the group closes,
    // the tab offers Remove, then Save puts the shown rule back.
    static void TamperedThenRemoved(string work)
    {
        Console.WriteLine();
        Console.WriteLine("== S4 rule text edited by hand, then Remove and Save on the tab");
        PwGroup g;
        var db = NewDb(Path.Combine(work, "s4.kdbx"), true, out g);
        var r1 = RuleOf(g);
        var r1Id = r1.Uuid;
        r1.CreateBackup(db);
        r1.Strings.Set("KPG.Rule", new ProtectedString(false, Wide));
        r1.LastModificationTime = T0.AddHours(1);
        Console.WriteLine("  after the hand edit: Servers " + State(db, g));
        Delete(db, r1, T0.AddHours(2));
        Console.WriteLine("  after Remove: Servers " + State(db, g));
        var r2 = Save(db, g, NodeOf(g), Narrow, T0.AddHours(3));
        Console.WriteLine("  Save writes " + r2.Uuid.ToHexString() + (r2.Uuid.Equals(r1Id) ? "  == the removed one's identifier" : ""));
        Console.WriteLine("  Servers " + State(db, g));
        db.Close();
    }

    // The root's rule entry, which every ruled database shows at the top of its tree, is deleted
    // for good on a phone or in KeePass; the operator then changes some other rule.
    static void RootLostThenNextRule(string work)
    {
        Console.WriteLine();
        Console.WriteLine("== S5 the root's rule entry deleted for good, then the operator saves the root's rule on its tab");
        PwGroup g;
        var db = NewDb(Path.Combine(work, "s5.kdbx"), true, out g);
        var rootRule = RuleOf(db.RootGroup);
        Delete(db, rootRule, T0.AddHours(1));
        Console.WriteLine("  the root " + State(db, db.RootGroup) + "; Servers " + State(db, g));
        var again = Save(db, db.RootGroup, NodeOf(db.RootGroup), Silent, T0.AddHours(2));
        Console.WriteLine("  Save of the root's (empty) rule writes " + again.Uuid.ToHexString() + (again.Uuid.Equals(rootRule.Uuid) ? "  == the deleted one's identifier" : ""));
        Console.WriteLine("  the root " + State(db, db.RootGroup));
        db.Close();
    }
}
