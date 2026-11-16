using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Survival probe for the storage option "rules-on-node", written to attack it.
// Throwaway databases with invented content only, under the work directory given first.
// No window: only KeePassLib types from KeePass 2.62.
//
// The store is modelled exactly as the design's own probe models it (rules-on-node\probe\Program.cs):
//   an entry's rule   custom string KPG on the entry
//   a group's rule    custom string KPG.Group on a rule entry inside the group, KPG.Node naming it
//   a seal            an entry in the group "KeePassGate": KPG.Node, KPG.Rule (sha256 or none),
//                     KPG.After; identifier derived from those three
// "D" is the design's read rule as its probe implements it (section 3 of the design).
// "V" is a stricter variant: steps 3 and 5 also look at the carriers' history.
static class P
{
    static string Work;
    static TextWriter Out;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Pw = "probe-only";

    const string RuleField = "KPG";
    const string GroupRuleField = "KPG.Group";
    const string NodeField = "KPG.Node";
    const string SealRule = "KPG.Rule";
    const string SealAfter = "KPG.After";

    const string R0 = "{\"default\":\"inherit\",\"reader\":[\"read\",\"list\"]}";
    const string R1 = "{\"default\":\"inherit\",\"reader\":[]}";
    const string RG0 = "{\"default\":\"inherit\",\"agent\":[\"read\",\"list\"]}";
    const string RG1 = "{\"default\":\"deny\"}";

    static DateTime At(int s) { return T0.AddSeconds(s); }
    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }

    static PwUuid Fixed(string name)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes("probe " + name));
            var b = new byte[16];
            Array.Copy(d, b, 16);
            return new PwUuid(b);
        }
    }

    static string Digest(string text)
    {
        using (var sha = SHA256.Create())
            return MemUtil.ByteArrayToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup AddGroup(PwGroup parent, string name, DateTime t)
    {
        var g = new PwGroup(false, false);
        g.Uuid = Fixed("group " + name);
        g.Name = name;
        Stamp(g, t);
        parent.AddGroup(g, true);
        g.LocationChanged = t;
        return g;
    }

    static PwEntry AddEntry(PwGroup parent, string uuidName, string title, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = uuidName == null ? new PwUuid(true) : Fixed("entry " + uuidName);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        Stamp(e, t);
        parent.AddEntry(e, true);
        e.LocationChanged = t;
        return e;
    }

    static void Set(PwEntry e, string name, string value) { e.Strings.Set(name, new ProtectedString(false, value)); }
    static void Password(PwEntry e, string value) { e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value)); }

    static void Edit(PwEntry e, DateTime t, Action<PwEntry> change)
    {
        e.CreateBackup(null);
        change(e);
        e.LastModificationTime = t;
    }

    static PwGroup Kpg(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group KeePassGate"), true); }

    static string SealId(PwUuid node, string ruleText, string[] after)
    {
        var rule = ruleText == null ? "none" : Digest(ruleText);
        var afterText = string.Join(",", after ?? new string[0]);
        return Fixed("seal " + node.ToHexString() + " " + rule + " " + afterText).ToHexString();
    }

    static string Seal(PwDatabase db, PwUuid node, string ruleText, string[] after, DateTime t)
    {
        var rule = ruleText == null ? "none" : Digest(ruleText);
        var afterText = string.Join(",", after ?? new string[0]);
        var e = new PwEntry(false, false);
        e.Uuid = Fixed("seal " + node.ToHexString() + " " + rule + " " + afterText);
        Set(e, PwDefs.TitleField, "Rule seal " + node.ToHexString().Substring(0, 8));
        Set(e, NodeField, node.ToHexString());
        Set(e, SealRule, rule);
        Set(e, SealAfter, afterText);
        e.AutoType.Enabled = false;
        Stamp(e, t);
        Kpg(db).AddEntry(e, true);
        e.LocationChanged = t;
        return e.Uuid.ToHexString();
    }

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
        db.RootGroup.Uuid = Fixed("root");
        db.RootGroup.Name = "probe";
        Stamp(db.RootGroup, T0);
        return db;
    }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static void SaveTo(PwDatabase db, string path)
    {
        db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        db.Close();
    }

    // Base:  U (rule entry RU: RG0), X in U ruled R0, Y and Z in U unruled, W unruled, KeePassGate with seals.
    // Returns { seal of X, seal of U }.
    static string[] MakeBase(string path, bool bigAttachment)
    {
        var db = NewDb("base-new.kdbx");
        var root = db.RootGroup;
        var u = AddGroup(root, "U", T0);
        AddGroup(root, "W", T0);
        var kpg = AddGroup(root, "KeePassGate", T0);
        kpg.EnableSearching = false;
        kpg.EnableAutoType = false;

        var x = AddEntry(u, "X", "X", T0);
        Password(x, "pw-x-0");
        Set(x, RuleField, R0);
        if (bigAttachment)
        {
            var bytes = new byte[1536 * 1024];
            new Random(7).NextBytes(bytes);
            x.Binaries.Set("blob.bin", new ProtectedBinary(false, bytes));
            db.HistoryMaxSize = 1024L * 1024L;
        }
        var y = AddEntry(u, "Y", "Y", T0);
        Password(y, "pw-y-0");
        AddEntry(u, "Z", "Z", T0);

        var ru = AddEntry(u, "rule U", "KeePassGate rule", T0);
        Set(ru, GroupRuleField, RG0);
        Set(ru, NodeField, u.Uuid.ToHexString());
        ru.AutoType.Enabled = false;

        var sx = Seal(db, x.Uuid, R0, null, T0);
        var su = Seal(db, u.Uuid, RG0, null, T0);
        SaveTo(db, path);
        return new[] { sx, su };
    }

    sealed class SealRow { public string Id; public string Rule; public string[] After; }

    static List<SealRow> SealsOf(PwDatabase db, PwUuid node)
    {
        var rows = new List<SealRow>();
        var kpg = Kpg(db);
        if (kpg == null) return rows;
        var hex = node.ToHexString();
        foreach (var e in kpg.Entries)   // the design: an index of the seal group only
        {
            if (e.Strings.ReadSafe(NodeField) != hex || !e.Strings.Exists(SealRule)) continue;
            var after = e.Strings.ReadSafe(SealAfter);
            rows.Add(new SealRow { Id = e.Uuid.ToHexString(), Rule = e.Strings.ReadSafe(SealRule), After = after.Length == 0 ? new string[0] : after.Split(',') });
        }
        return rows;
    }

    static string Short(string text)
    {
        if (text == R0) return "R0";
        if (text == R1) return "R1";
        if (text == RG0) return "RG0";
        if (text == RG1) return "RG1";
        return "'" + text + "'";
    }

    static void Carriers(PwDatabase db, PwUuid node, bool isGroup, List<string> current, List<string> past, out bool absent)
    {
        absent = false;
        if (isGroup)
        {
            var g = db.RootGroup.Uuid.Equals(node) ? db.RootGroup : db.RootGroup.FindGroup(node, true);
            if (g == null) { absent = true; return; }
            foreach (var e in g.Entries)
            {
                if (!e.Strings.Exists(GroupRuleField)) continue;
                current.Add(e.Strings.ReadSafe(GroupRuleField));
                foreach (var h in e.History)
                    if (h.Strings.Exists(GroupRuleField)) past.Add(h.Strings.ReadSafe(GroupRuleField));
            }
        }
        else
        {
            var e = db.RootGroup.FindEntry(node, true);
            if (e == null) { absent = true; return; }
            if (e.Strings.Exists(RuleField)) current.Add(e.Strings.ReadSafe(RuleField));
            foreach (var h in e.History)
                if (h.Strings.Exists(RuleField)) past.Add(h.Strings.ReadSafe(RuleField));
        }
    }

    // The design's read rule, as the design's probe implements it.
    static string ResolveD(PwDatabase db, PwUuid node, bool isGroup)
    {
        var current = new List<string>(); var past = new List<string>(); bool absent;
        Carriers(db, node, isGroup, current, past, out absent);
        if (absent) return "absent";
        var seals = SealsOf(db, node);
        if (seals.Count == 0)
            return current.Count == 0 ? "SILENCE (no seal, no text)" : "REFUSE (a text and no seal)";
        var named = new HashSet<string>(); var known = new HashSet<string>();
        foreach (var s in seals) { known.Add(s.Rule); foreach (var a in s.After) named.Add(a); }
        var heads = seals.FindAll(s => !named.Contains(s.Id));
        if (heads.Count != 1) return "REFUSE (" + heads.Count.ToString(Inv) + " heads)";
        var head = heads[0];
        foreach (var text in current)
            if (!known.Contains(Digest(text))) return "REFUSE (a text no seal names)";
        if (head.Rule == "none") return "SILENCE (sealed as no rule)";
        foreach (var text in current) if (Digest(text) == head.Rule) return "RULE " + Short(text) + " (on the node)";
        foreach (var text in past) if (Digest(text) == head.Rule) return "RULE " + Short(text) + " (from history)";
        return "REFUSE (the sealed text is nowhere)";
    }

    // The stricter variant: a text in a carrier's history counts as a text for step 3, and must be named
    // by a seal of the node for step 5, as a current text must.
    static string ResolveV(PwDatabase db, PwUuid node, bool isGroup)
    {
        var current = new List<string>(); var past = new List<string>(); bool absent;
        Carriers(db, node, isGroup, current, past, out absent);
        if (absent) return "absent";
        var seals = SealsOf(db, node);
        if (seals.Count == 0)
            return (current.Count == 0 && past.Count == 0) ? "SILENCE (no seal, no text anywhere)" : "REFUSE (a text, current or past, and no seal)";
        var named = new HashSet<string>(); var known = new HashSet<string>();
        foreach (var s in seals) { known.Add(s.Rule); foreach (var a in s.After) named.Add(a); }
        var heads = seals.FindAll(s => !named.Contains(s.Id));
        if (heads.Count != 1) return "REFUSE (" + heads.Count.ToString(Inv) + " heads)";
        var head = heads[0];
        foreach (var text in current) if (!known.Contains(Digest(text))) return "REFUSE (a current text no seal names)";
        foreach (var text in past) if (!known.Contains(Digest(text))) return "REFUSE (a past text no seal names)";
        if (head.Rule == "none") return "SILENCE (sealed as no rule)";
        foreach (var text in current) if (Digest(text) == head.Rule) return "RULE " + Short(text) + " (on the node)";
        foreach (var text in past) if (Digest(text) == head.Rule) return "RULE " + Short(text) + " (from history)";
        return "REFUSE (the sealed text is nowhere)";
    }

    static string Both(PwDatabase db, PwUuid node, bool isGroup)
    {
        return "D: " + ResolveD(db, node, isGroup) + " / V: " + ResolveV(db, node, isGroup);
    }

    static PwEntry X(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry X"), true); }
    static PwEntry Y(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry Y"), true); }
    static PwGroup U(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group U"), true); }
    static PwGroup W(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group W"), true); }

    static PwEntry SealEntry(PwDatabase db, string id)
    {
        foreach (var e in db.RootGroup.GetEntries(true)) if (e.Uuid.ToHexString() == id) return e;
        return null;
    }

    static void RemoveForGood(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    // As KeePass deletes a group for good: every entry and subgroup inside, then the group, each with a record.
    static void DeleteGroupForGood(PwDatabase db, PwGroup g, DateTime t)
    {
        foreach (var e in g.GetEntries(true)) db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
        foreach (var sg in g.GetGroups(true)) db.DeletedObjects.Add(new PwDeletedObject(sg.Uuid, t));
        db.DeletedObjects.Add(new PwDeletedObject(g.Uuid, t));
        g.ParentGroup.Groups.Remove(g);
    }

    // The design's prune (section 4.5): the seals of a node no longer in the database, bin counted in.
    static int Prune(PwDatabase db, DateTime t, bool withRecords)
    {
        var gone = new List<PwEntry>();
        foreach (var e in Kpg(db).Entries)
        {
            if (!e.Strings.Exists(SealRule)) continue;
            var node = new PwUuid(MemUtil.HexStringToByteArray(e.Strings.ReadSafe(NodeField)));
            var asEntry = db.RootGroup.FindEntry(node, true);
            var asGroup = db.RootGroup.Uuid.Equals(node) ? db.RootGroup : db.RootGroup.FindGroup(node, true);
            if (asEntry == null && asGroup == null) gone.Add(e);
        }
        foreach (var e in gone)
        {
            Kpg(db).Entries.Remove(e);
            if (withRecords) db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
        }
        return gone.Count;
    }

    static readonly PwMergeMethod[] Methods = { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting };

    static string MergeTo(string dir, string a, string b, PwMergeMethod m, string into)
    {
        var target = Open(into == "A" ? a : b);
        var source = Open(into == "A" ? b : a);
        target.MergeIn(source, m, null);
        source.Close();
        var result = Path.Combine(dir, "kp-" + m + "-into" + into + ".kdbx");
        SaveTo(target, result);
        return result;
    }

    static void Prepare(string name, bool big, Action<PwDatabase, string[]> fa, Action<PwDatabase, string[]> fb, out string dir, out string a, out string b, out string[] seals)
    {
        dir = Path.Combine(Work, name);
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        seals = MakeBase(basePath, big);
        a = Path.Combine(dir, "A.kdbx");
        b = Path.Combine(dir, "B.kdbx");
        var s = seals;
        var da = Open(basePath); fa(da, s); SaveTo(da, a);
        var dbb = Open(basePath); fb(dbb, s); SaveTo(dbb, b);
    }

    static string Cell(PwMergeMethod m, string into) { return "  " + m.ToString().PadRight(17) + " into " + into + ": "; }

    // C1. The newest seal of a node is lost after the routine rollback merge of the design's scenario 01.
    static void C1()
    {
        Out.WriteLine();
        Out.WriteLine("C1 newest-seal-lost: A narrows X R0 -> R1 at +10 and seals S1 (after S0); B changes X's password at +20;");
        Out.WriteLine("   merge; then S1 alone is lost: (a) removed for good, (b) moved into the recycle bin, (c) moved to the root");
        string dir, a, b; string[] s;
        string s1 = null;
        Prepare("C1", false,
            (db, z) => { Edit(X(db), At(10), e => Set(e, RuleField, R1)); s1 = Seal(db, X(db).Uuid, R1, new[] { z[0] }, At(10)); },
            (db, z) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); },
            out dir, out a, out b, out s);
        foreach (var m in Methods)
            foreach (var into in new[] { "A", "B" })
            {
                var r = MergeTo(dir, a, b, m, into);
                var db = Open(r);
                var line = Cell(m, into) + "merged " + Both(db, X(db).Uuid, false);
                db.Close();
                foreach (var post in new[] { "a", "b", "c" })
                {
                    db = Open(r);
                    var e = SealEntry(db, s1);
                    if (post == "a") RemoveForGood(db, e, At(40));
                    else if (post == "b")
                    {
                        var bin = AddGroup(db.RootGroup, "Recycle Bin", At(40));
                        db.RecycleBinEnabled = true; db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = At(40);
                        e.ParentGroup.Entries.Remove(e); bin.AddEntry(e, true, true);
                    }
                    else { e.ParentGroup.Entries.Remove(e); db.RootGroup.AddEntry(e, true, true); }
                    var p = Path.Combine(dir, Path.GetFileNameWithoutExtension(r) + "-post" + post + ".kdbx");
                    SaveTo(db, p);
                    var rd = Open(p);
                    line += " || (" + post + ") " + Both(rd, X(rd).Uuid, false) + " [current KPG " + Short(X(rd).Strings.ReadSafe(RuleField)) + "]";
                    rd.Close();
                }
                Out.WriteLine(line);
            }
    }

    // C2. A first rule whose version lost the merge (the design's scenario 05), then its only seal is lost.
    static void C2()
    {
        Out.WriteLine();
        Out.WriteLine("C2 first-rule-seal-lost: A gives Y its first rule R1 at +10 and seals it; B changes Y's password at +20; merge;");
        Out.WriteLine("   then Y's one seal is removed for good");
        string dir, a, b; string[] s;
        string sy = null;
        Prepare("C2", false,
            (db, z) => { Edit(Y(db), At(10), e => Set(e, RuleField, R1)); sy = Seal(db, Y(db).Uuid, R1, null, At(10)); },
            (db, z) => { Edit(Y(db), At(20), e => Password(e, "pw-y-B")); },
            out dir, out a, out b, out s);
        foreach (var m in Methods)
            foreach (var into in new[] { "A", "B" })
            {
                var r = MergeTo(dir, a, b, m, into);
                var db = Open(r);
                var line = Cell(m, into) + "merged " + Both(db, Y(db).Uuid, false);
                RemoveForGood(db, SealEntry(db, sy), At(40));
                var p = Path.Combine(dir, Path.GetFileNameWithoutExtension(r) + "-post.kdbx");
                SaveTo(db, p);
                var rd = Open(p);
                line += " || seal lost: " + Both(rd, Y(rd).Uuid, false) + " [Y has KPG now: " + Y(rd).Strings.Exists(RuleField) + "]";
                rd.Close();
                Out.WriteLine(line);
            }
    }

    // C3. The KPG field deleted by hand (the design's scenario 07), then every seal lost with the KPG group.
    static void C3()
    {
        Out.WriteLine();
        Out.WriteLine("C3 field-then-seals: B deletes X's KPG field by hand at +20; merge; then the KeePassGate group is deleted for good");
        string dir, a, b; string[] s;
        Prepare("C3", false,
            (db, z) => { },
            (db, z) => { Edit(X(db), At(20), e => e.Strings.Remove(RuleField)); },
            out dir, out a, out b, out s);
        foreach (var into in new[] { "A", "B" })
        {
            var m = PwMergeMethod.Synchronize;
            var r = MergeTo(dir, a, b, m, into);
            var db = Open(r);
            var line = Cell(m, into) + "merged X " + Both(db, X(db).Uuid, false) + " | U " + Both(db, U(db).Uuid, true);
            DeleteGroupForGood(db, Kpg(db), At(40));
            var p = Path.Combine(dir, Path.GetFileNameWithoutExtension(r) + "-post.kdbx");
            SaveTo(db, p);
            var rd = Open(p);
            line += " || seals gone: X " + Both(rd, X(rd).Uuid, false) + " | U " + Both(rd, U(rd).Uuid, true);
            rd.Close();
            Out.WriteLine(line);
        }
    }

    // C4. A deletes group U for good; the plugin's next write in A prunes the seals of nodes no longer there;
    //     B, stale, adds entry N to U later. Then each merge method, both directions.
    static void C4(bool withRecords)
    {
        Out.WriteLine();
        Out.WriteLine("C4 group-deleted-pruned (" + (withRecords ? "prune leaves deletion records" : "prune leaves no deletion records")
            + "): A deletes U for good at +20, prunes at +21; B adds N to U at +30");
        string dir, a, b; string[] s;
        int pruned = 0;
        Prepare(withRecords ? "C4-records" : "C4-norecords", false,
            (db, z) => { DeleteGroupForGood(db, U(db), At(20)); pruned = Prune(db, At(21), withRecords); },
            (db, z) => { AddEntry(U(db), "N", "N", At(30)); },
            out dir, out a, out b, out s);
        Out.WriteLine("  seals pruned in A: " + pruned.ToString(Inv));
        foreach (var m in Methods)
            foreach (var into in new[] { "A", "B" })
            {
                var r = MergeTo(dir, a, b, m, into);
                var db = Open(r);
                var u = U(db);
                var names = new List<string>();
                if (u != null) foreach (var e in u.Entries) names.Add(e.Strings.ReadSafe(PwDefs.TitleField));
                var line = Cell(m, into) + "U " + (u == null ? "absent" : Both(db, u.Uuid, true) + " holding [" + string.Join(",", names.ToArray()) + "]")
                    + " | seals of U: " + SealsOf(db, Fixed("group U")).Count.ToString(Inv);
                db.Close();
                Out.WriteLine(line);
            }
    }

    // C5. The seal identifier is derived from what it says. A gives W its first rule RG1 and later takes it
    //     away; B, independently, gives W the same first rule later. B's seal is A's first seal.
    static void C5()
    {
        Out.WriteLine();
        Out.WriteLine("C5 derived-identity: A rules W RG1 at +10, takes it away at +20 (rule entry deleted for good, seal none);");
        Out.WriteLine("   B rules W RG1 at +30 with a rule entry of its own");
        string dir, a, b; string[] s;
        string sa = null, sb = null;
        Prepare("C5", false,
            (db, z) =>
            {
                var re = AddEntry(W(db), null, "KeePassGate rule", At(10));
                Set(re, GroupRuleField, RG1); Set(re, NodeField, W(db).Uuid.ToHexString()); re.AutoType.Enabled = false;
                sa = Seal(db, W(db).Uuid, RG1, null, At(10));
                RemoveForGood(db, re, At(20));
                Seal(db, W(db).Uuid, null, new[] { sa }, At(20));
            },
            (db, z) =>
            {
                var re = AddEntry(W(db), null, "KeePassGate rule", At(30));
                Set(re, GroupRuleField, RG1); Set(re, NodeField, W(db).Uuid.ToHexString()); re.AutoType.Enabled = false;
                sb = Seal(db, W(db).Uuid, RG1, null, At(30));
            },
            out dir, out a, out b, out s);
        Out.WriteLine("  A's first seal and B's seal are one identifier: " + (sa == sb));
        foreach (var m in Methods)
            foreach (var into in new[] { "A", "B" })
            {
                var r = MergeTo(dir, a, b, m, into);
                var db = Open(r);
                var carriers = 0;
                foreach (var e in W(db).Entries) if (e.Strings.Exists(GroupRuleField)) carriers++;
                Out.WriteLine(Cell(m, into) + "W " + Both(db, W(db).Uuid, true) + " | rule entries in W: " + carriers.ToString(Inv));
                db.Close();
            }
    }

    // C6. An entry whose history cannot hold a revision under the size limit (1.5 MiB attachment, 1 MiB limit).
    static void C6()
    {
        Out.WriteLine();
        Out.WriteLine("C6 big-entry: X carries a 1.5 MiB attachment, HistoryMaxSize 1 MiB; A narrows X to R1 at +10 and seals it;");
        Out.WriteLine("   B changes X's password at +20");
        string dir, a, b; string[] s;
        Prepare("C6", true,
            (db, z) => { Edit(X(db), At(10), e => Set(e, RuleField, R1)); Seal(db, X(db).Uuid, R1, new[] { z[0] }, At(10)); },
            (db, z) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); },
            out dir, out a, out b, out s);
        foreach (var into in new[] { "A", "B" })
        {
            var m = PwMergeMethod.Synchronize;
            var r = MergeTo(dir, a, b, m, into);
            var db = Open(r);
            Out.WriteLine(Cell(m, into) + "X " + Both(db, X(db).Uuid, false) + " [hist " + X(db).History.UCount.ToString(Inv) + "]");
            db.Close();
        }
    }

    // C7. The design's own recoveries (its scenarios 01, 05, 07) under both rules, Synchronize both ways,
    //     to show whether the stricter variant keeps them.
    static void C7()
    {
        Out.WriteLine();
        Out.WriteLine("C7 the design's recoveries under both rules (its scenarios 01, 05, 07), Synchronize");
        var cases = new List<KeyValuePair<string, Action<PwDatabase, string[]>[]>>();
        cases.Add(new KeyValuePair<string, Action<PwDatabase, string[]>[]>("01", new Action<PwDatabase, string[]>[] {
            (db, z) => { Edit(X(db), At(10), e => Set(e, RuleField, R1)); Seal(db, X(db).Uuid, R1, new[] { z[0] }, At(10)); },
            (db, z) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); } }));
        cases.Add(new KeyValuePair<string, Action<PwDatabase, string[]>[]>("05", new Action<PwDatabase, string[]>[] {
            (db, z) => { Edit(Y(db), At(10), e => Set(e, RuleField, R1)); Seal(db, Y(db).Uuid, R1, null, At(10)); },
            (db, z) => { Edit(Y(db), At(20), e => Password(e, "pw-y-B")); } }));
        cases.Add(new KeyValuePair<string, Action<PwDatabase, string[]>[]>("07", new Action<PwDatabase, string[]>[] {
            (db, z) => { },
            (db, z) => { Edit(X(db), At(20), e => e.Strings.Remove(RuleField)); } }));
        foreach (var c in cases)
        {
            string dir, a, b; string[] s;
            Prepare("C7-" + c.Key, false, c.Value[0], c.Value[1], out dir, out a, out b, out s);
            foreach (var into in new[] { "A", "B" })
            {
                var r = MergeTo(dir, a, b, PwMergeMethod.Synchronize, into);
                var db = Open(r);
                var node = c.Key == "05" ? Y(db).Uuid : X(db).Uuid;
                Out.WriteLine("  " + c.Key + " into " + into + ": " + Both(db, node, false));
                db.Close();
            }
        }
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString);
            C1(); C2(); C3(); C4(true); C4(false); C5(); C6(); C7();
        }
        return 0;
    }
}
