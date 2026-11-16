using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Probe for the storage option "access rules in standard fields of the node they govern".
// Throwaway databases with invented content only, all under the work directory given first.
// No window: only KeePassLib types, and keepassxc-cli as a console child process.
//
// The store it models:
//   an entry's rule       the custom string KPG on that entry
//   a group's rule        the custom string KPG.Group on a "rule entry" inside the group,
//                         with KPG.Node naming the group
//   a seal                one entry per rule version in the group "KeePassGate", strings
//                         KPG.Node (the node), KPG.Rule (sha256 of the rule text, or "none"),
//                         KPG.After (the seals it replaces, comma separated), never changed
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
    const string R2 = "{\"default\":\"deny\",\"reader\":[\"list\"]}";
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
        {
            return MemUtil.ByteArrayToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }
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

    // An edit as KeePass makes one: the state before goes into history, then the time moves.
    static void Edit(PwEntry e, DateTime t, Action<PwEntry> change)
    {
        e.CreateBackup(null);
        change(e);
        e.LastModificationTime = t;
    }

    static PwGroup Kpg(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group KeePassGate"), false); }

    // A seal: one entry, never changed. Its identifier is derived from what it says, so the same
    // seal made in two copies is the same entry.
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

    // The base every scenario starts from.
    //   U          a user's group, ruled RG0 by its rule entry
    //     X        an entry ruled R0
    //     Y        an entry with no rule
    //     Z        an entry with no rule
    //   V          a second ruled group, the same rule text as U's (for the duplicate cleanup)
    //   KeePassGate  the dedicated group, searching and auto-type off, holding the seals
    static string[] MakeBase(string path)
    {
        var db = NewDb("base-new.kdbx");
        var root = db.RootGroup;
        var u = AddGroup(root, "U", T0);
        var v = AddGroup(root, "V", T0);
        var kpg = AddGroup(root, "KeePassGate", T0);
        kpg.EnableSearching = false;
        kpg.EnableAutoType = false;

        var x = AddEntry(u, "X", "X", T0);
        x.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-x-0"));
        Set(x, RuleField, R0);
        var y = AddEntry(u, "Y", "Y", T0);
        y.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-y-0"));
        var z = AddEntry(u, "Z", "Z", T0);

        var ru = AddEntry(u, "rule U", "KeePassGate rule", T0);
        Set(ru, GroupRuleField, RG0);
        Set(ru, NodeField, u.Uuid.ToHexString());
        ru.AutoType.Enabled = false;
        var rv = AddEntry(v, "rule V", "KeePassGate rule", T0);
        Set(rv, GroupRuleField, RG0);
        Set(rv, NodeField, v.Uuid.ToHexString());
        rv.AutoType.Enabled = false;

        var sx = Seal(db, x.Uuid, R0, null, T0);
        var su = Seal(db, u.Uuid, RG0, null, T0);
        Seal(db, v.Uuid, RG0, null, T0);
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
        foreach (var e in kpg.Entries)
        {
            if (e.Strings.ReadSafe(NodeField) != hex || !e.Strings.Exists(SealRule)) continue;
            var after = e.Strings.ReadSafe(SealAfter);
            rows.Add(new SealRow
            {
                Id = e.Uuid.ToHexString(),
                Rule = e.Strings.ReadSafe(SealRule),
                After = after.Length == 0 ? new string[0] : after.Split(','),
            });
        }
        return rows;
    }

    static string Short(string text)
    {
        if (text == R0) return "R0";
        if (text == R1) return "R1";
        if (text == R2) return "R2";
        if (text == RG0) return "RG0";
        if (text == RG1) return "RG1";
        return "'" + text + "'";
    }

    // The read rule of the design, for one node.
    static string Resolve(PwDatabase db, PwUuid node, bool isGroup)
    {
        // What the node carries now, and what its carriers' histories carry.
        var current = new List<string>();
        var past = new List<string>();
        if (isGroup)
        {
            var g = db.RootGroup.Uuid.Equals(node) ? db.RootGroup : db.RootGroup.FindGroup(node, true);
            if (g == null) return "absent";
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
            if (e == null) return "absent";
            if (e.Strings.Exists(RuleField)) current.Add(e.Strings.ReadSafe(RuleField));
            foreach (var h in e.History)
                if (h.Strings.Exists(RuleField)) past.Add(h.Strings.ReadSafe(RuleField));
        }

        var seals = SealsOf(db, node);
        if (seals.Count == 0)
            return current.Count == 0 ? "silence (no seal, no text)" : "REFUSE (a text and no seal)";

        var named = new HashSet<string>();
        var known = new HashSet<string>();
        foreach (var s in seals) { known.Add(s.Rule); foreach (var a in s.After) named.Add(a); }
        var heads = seals.FindAll(s => !named.Contains(s.Id));
        if (heads.Count != 1) return "REFUSE (" + heads.Count.ToString(Inv) + " heads)";
        var head = heads[0];

        foreach (var text in current)
            if (!known.Contains(Digest(text))) return "REFUSE (a text no seal names: " + Short(text) + ")";

        if (head.Rule == "none") return "silence (sealed as no rule)";

        foreach (var text in current)
            if (Digest(text) == head.Rule) return "RULE " + Short(text) + " (on the node)";
        foreach (var text in past)
            if (Digest(text) == head.Rule) return "RULE " + Short(text) + " (from history)";
        return "REFUSE (the sealed text is nowhere)";
    }

    static string State(PwDatabase db)
    {
        var sb = new StringBuilder();
        var x = db.RootGroup.FindEntry(Fixed("entry X"), true);
        var y = db.RootGroup.FindEntry(Fixed("entry Y"), true);
        sb.Append("X: ").Append(Resolve(db, Fixed("entry X"), false));
        if (x != null) sb.Append(" [pw ").Append(x.Strings.ReadSafe(PwDefs.PasswordField)).Append(", hist ").Append(x.History.UCount.ToString(Inv)).Append("]");
        sb.Append(" | Y: ").Append(Resolve(db, Fixed("entry Y"), false));
        if (y != null) sb.Append(" [hist ").Append(y.History.UCount.ToString(Inv)).Append("]");
        sb.Append(" | U: ").Append(Resolve(db, Fixed("group U"), true));
        return sb.ToString();
    }

    sealed class Scenario
    {
        public string Name;
        public string What;
        public Action<PwDatabase, string[]> A;
        public Action<PwDatabase, string[]> B;
    }

    static PwEntry X(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry X"), true); }
    static PwEntry Y(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry Y"), true); }
    static PwEntry Z(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry Z"), true); }
    static PwEntry RU(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry rule U"), true); }
    static PwGroup U(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group U"), true); }

    static void Password(PwEntry e, string value) { e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value)); }

    static List<Scenario> Scenarios()
    {
        var list = new List<Scenario>();

        list.Add(new Scenario
        {
            Name = "01-rollback",
            What = "A narrows X's rule to R1 at +10 and seals it; B, not knowing, changes X's password at +20",
            A = (db, s) => { Edit(X(db), At(10), e => Set(e, RuleField, R1)); Seal(db, X(db).Uuid, R1, new[] { s[0] }, At(10)); },
            B = (db, s) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); },
        });
        list.Add(new Scenario
        {
            Name = "02-tie",
            What = "as 01, both in the same second (+10)",
            A = (db, s) => { Edit(X(db), At(10), e => Set(e, RuleField, R1)); Seal(db, X(db).Uuid, R1, new[] { s[0] }, At(10)); },
            B = (db, s) => { Edit(X(db), At(10), e => Password(e, "pw-x-B")); },
        });
        list.Add(new Scenario
        {
            Name = "03-fork",
            What = "A sets X's rule to R1 at +10, B sets it to R2 at +20, each sealing after the base seal",
            A = (db, s) => { Edit(X(db), At(10), e => Set(e, RuleField, R1)); Seal(db, X(db).Uuid, R1, new[] { s[0] }, At(10)); },
            B = (db, s) => { Edit(X(db), At(20), e => Set(e, RuleField, R2)); Seal(db, X(db).Uuid, R2, new[] { s[0] }, At(20)); },
        });
        list.Add(new Scenario
        {
            Name = "04-removal",
            What = "A removes X's rule at +10 and seals 'none'; B changes X's password at +20",
            A = (db, s) => { Edit(X(db), At(10), e => e.Strings.Remove(RuleField)); Seal(db, X(db).Uuid, null, new[] { s[0] }, At(10)); },
            B = (db, s) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); },
        });
        list.Add(new Scenario
        {
            Name = "05-first-rule",
            What = "A gives Y its first rule (R1) at +10 and seals it; B changes Y's password at +20",
            A = (db, s) => { Edit(Y(db), At(10), e => Set(e, RuleField, R1)); Seal(db, Y(db).Uuid, R1, null, At(10)); },
            B = (db, s) => { Edit(Y(db), At(20), e => Password(e, "pw-y-B")); },
        });
        list.Add(new Scenario
        {
            Name = "06-hand-edit",
            What = "B changes X's rule text by hand in another program at +20 (no seal); A does nothing",
            A = (db, s) => { },
            B = (db, s) => { Edit(X(db), At(20), e => Set(e, RuleField, "{\"default\":\"inherit\",\"reader\":[\"read\",\"list\",\"update\"]}")); },
        });
        list.Add(new Scenario
        {
            Name = "07-hand-delete",
            What = "B deletes the KPG field of X by hand at +20; A does nothing",
            A = (db, s) => { },
            B = (db, s) => { Edit(X(db), At(20), e => e.Strings.Remove(RuleField)); },
        });
        list.Add(new Scenario
        {
            Name = "08-group-rule",
            What = "A changes U's rule to RG1 at +10 (the rule entry edited, sealed); B edits Z at +20, adds an entry to U, and U's own time moves to +20",
            A = (db, s) => { Edit(RU(db), At(10), e => Set(e, GroupRuleField, RG1)); Seal(db, U(db).Uuid, RG1, new[] { s[1] }, At(10)); },
            B = (db, s) =>
            {
                Edit(Z(db), At(20), e => Set(e, PwDefs.UserNameField, "z-B"));
                AddEntry(U(db), "N", "N", At(20));
                U(db).LastModificationTime = At(20);
                U(db).Notes = "B's notes";
            },
        });
        list.Add(new Scenario
        {
            Name = "09-rule-entry-deleted",
            What = "B deletes U's rule entry for good at +20 (a deletion record); A does nothing",
            A = (db, s) => { },
            B = (db, s) => { var r = RU(db); r.ParentGroup.Entries.Remove(r); db.DeletedObjects.Add(new PwDeletedObject(r.Uuid, At(20))); },
        });
        list.Add(new Scenario
        {
            Name = "10-seal-deleted",
            What = "B deletes X's seal for good at +20 (a deletion record); A does nothing",
            A = (db, s) => { },
            B = (db, s) =>
            {
                PwEntry seal = null;
                foreach (var e in Kpg(db).Entries) if (e.Uuid.ToHexString() == s[0]) seal = e;
                Kpg(db).Entries.Remove(seal);
                db.DeletedObjects.Add(new PwDeletedObject(seal.Uuid, At(20)));
            },
        });
        list.Add(new Scenario
        {
            Name = "11-two-seals",
            What = "A rules Y (R1) at +10, B rules Z (R2) at +20: a seal added in each copy",
            A = (db, s) => { Edit(Y(db), At(10), e => Set(e, RuleField, R1)); Seal(db, Y(db).Uuid, R1, null, At(10)); },
            B = (db, s) => { Edit(Z(db), At(20), e => Set(e, RuleField, R2)); Seal(db, Z(db).Uuid, R2, null, At(20)); },
        });
        return list;
    }

    static string Prepare(Scenario sc, out string a, out string b)
    {
        var dir = Path.Combine(Work, sc.Name);
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        var seals = MakeBase(basePath);
        a = Path.Combine(dir, "A.kdbx");
        b = Path.Combine(dir, "B.kdbx");
        var da = Open(basePath); sc.A(da, seals); SaveTo(da, a);
        var db = Open(basePath); sc.B(db, seals); SaveTo(db, b);
        return dir;
    }

    static void RunKeePass(string only)
    {
        Out.WriteLine("== KeePass " + PwDefs.VersionString + " (KeePassLib), PwDatabase.MergeIn, each result saved and reopened ==");
        foreach (var sc in Scenarios())
        {
            if (only != null && sc.Name != only) continue;
            string a, b;
            var dir = Prepare(sc, out a, out b);
            Out.WriteLine();
            Out.WriteLine(sc.Name + ": " + sc.What);
            foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting })
            {
                foreach (var into in new[] { "A", "B" })
                {
                    var target = Open(into == "A" ? a : b);
                    var source = Open(into == "A" ? b : a);
                    target.MergeIn(source, method, null);
                    source.Close();
                    var result = Path.Combine(dir, "kp-" + method + "-into" + into + ".kdbx");
                    SaveTo(target, result);
                    var read = Open(result);
                    Out.WriteLine("  " + method.ToString().PadRight(17) + " into " + into + ": " + State(read)
                        + (sc.Name == "11-two-seals" ? " | Z: " + Resolve(read, Fixed("entry Z"), false) : ""));
                    read.Close();
                }
            }
        }
    }

    static void RunNewUuids()
    {
        Out.WriteLine();
        Out.WriteLine("== KeePass " + PwDefs.VersionString + ", CreateNewUuids (what File then Import preselects) ==");
        var dir = Path.Combine(Work, "20-new-uuids");
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase(basePath);
        var target = Open(basePath);
        var source = Open(basePath);
        target.MergeIn(source, PwMergeMethod.CreateNewUuids, null);
        source.Close();
        var result = Path.Combine(dir, "result.kdbx");
        SaveTo(target, result);
        var read = Open(result);
        Out.WriteLine("  the originals: " + State(read));
        foreach (var g in read.RootGroup.GetGroups(true))
        {
            if (g.Name != "U" || g.Uuid.Equals(Fixed("group U"))) continue;
            Out.WriteLine("  the copy of U (new identifier): " + Resolve(read, g.Uuid, true));
            foreach (var e in g.Entries)
                if (e.Strings.ReadSafe(PwDefs.TitleField) == "X")
                    Out.WriteLine("  the copy of X (new identifier): " + Resolve(read, e.Uuid, false));
        }
        read.Close();
    }

    static void RunHistory()
    {
        Out.WriteLine();
        Out.WriteLine("== How long a rolled back rule can be found in history (KeePass " + PwDefs.VersionString + ", limit 10 items) ==");
        string a, b;
        var sc = Scenarios()[0];
        sc.Name = "21-history";
        var dir = Prepare(sc, out a, out b);
        var target = Open(a);
        var source = Open(b);
        target.MergeIn(source, PwMergeMethod.Synchronize, null);
        source.Close();
        var path = Path.Combine(dir, "merged.kdbx");
        SaveTo(target, path);
        for (var round = 1; round <= 12; round++)
        {
            var db = Open(path);
            var x = X(db);
            x.CreateBackup(db);
            Password(x, "pw-x-" + round.ToString(Inv));
            x.LastModificationTime = At(100 + round);
            SaveTo(db, path);
            var read = Open(path);
            Out.WriteLine("  without repair, after " + round.ToString(Inv).PadLeft(2) + " later edits of X: " + Resolve(read, Fixed("entry X"), false)
                + " [hist " + X(read).History.UCount.ToString(Inv) + "]");
            read.Close();
        }

        // With the repair riding the first write to the entry: the sealed text put back with it.
        var merged2 = Path.Combine(dir, "merged-repaired.kdbx");
        target = Open(a);
        source = Open(b);
        target.MergeIn(source, PwMergeMethod.Synchronize, null);
        source.Close();
        SaveTo(target, merged2);
        for (var round = 1; round <= 12; round++)
        {
            var db = Open(merged2);
            var x = X(db);
            x.CreateBackup(db);
            Password(x, "pw-x-" + round.ToString(Inv));
            if (round == 1) Set(x, RuleField, R1);
            x.LastModificationTime = At(100 + round);
            SaveTo(db, merged2);
            if (round == 1 || round == 12)
            {
                var read = Open(merged2);
                Out.WriteLine("  with the repair in the first write, after " + round.ToString(Inv).PadLeft(2) + " edits: " + Resolve(read, Fixed("entry X"), false));
                read.Close();
            }
        }
    }

    static void RunDuplicates()
    {
        Out.WriteLine();
        Out.WriteLine("== Delete Duplicate Entries (PwDatabase.DeleteDuplicateEntries) ==");
        var dir = Path.Combine(Work, "22-duplicates");
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase(basePath);
        var db = Open(basePath);
        var before = db.RootGroup.GetEntriesCount(true);
        var deleted = db.DeleteDuplicateEntries(null);
        Out.WriteLine("  rule entries of U and V hold the same rule text and title, and differ in KPG.Node: " + deleted.ToString(Inv)
            + " deleted of " + before.ToString(Inv));
        // The counterfactual: without KPG.Node.
        foreach (var name in new[] { "entry rule U", "entry rule V" })
            db.RootGroup.FindEntry(Fixed(name), true).Strings.Remove(NodeField);
        deleted = db.DeleteDuplicateEntries(null);
        Out.WriteLine("  the same two without KPG.Node: " + deleted.ToString(Inv) + " deleted; U: " + Resolve(db, Fixed("group U"), true)
            + " | V: " + Resolve(db, Fixed("group V"), true));
        db.Close();
    }

    static void RunCost()
    {
        Out.WriteLine();
        Out.WriteLine("== Cost of the read path, in memory (KeePass " + PwDefs.VersionString + ") ==");
        var db = NewDb("cost.kdbx");
        var root = db.RootGroup;
        var kpg = AddGroup(root, "KeePassGate", T0);
        var groups = new List<PwGroup>();
        var entries = new List<PwEntry>();
        for (var g = 0; g < 200; g++)
        {
            var grp = AddGroup(root, "G" + g.ToString(Inv), T0);
            groups.Add(grp);
            for (var i = 0; i < 100; i++)
            {
                var e = AddEntry(grp, null, "E" + i.ToString(Inv), T0);
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw"));
                e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "user"));
                e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.invalid/"));
                entries.Add(e);
            }
        }
        // 2000 seals: ten versions each for 200 nodes.
        for (var n = 0; n < 200; n++)
        {
            string last = null;
            for (var v = 0; v < 10; v++)
                last = Seal(db, entries[n * 100].Uuid, R0 + v.ToString(Inv), last == null ? null : new[] { last }, T0);
        }

        var sw = Stopwatch.StartNew();
        var rounds = 20;
        var count = 0;
        for (var r = 0; r < rounds; r++)
        {
            var index = new Dictionary<string, List<SealRow>>(StringComparer.Ordinal);
            foreach (var e in kpg.Entries)
            {
                var node = e.Strings.ReadSafe(NodeField);
                List<SealRow> rows;
                if (!index.TryGetValue(node, out rows)) { rows = new List<SealRow>(); index[node] = rows; }
                var after = e.Strings.ReadSafe(SealAfter);
                rows.Add(new SealRow { Id = e.Uuid.ToHexString(), Rule = e.Strings.ReadSafe(SealRule), After = after.Length == 0 ? new string[0] : after.Split(',') });
            }
            count = index.Count;
        }
        sw.Stop();
        Out.WriteLine("  reading 2000 seals into an index by node (" + count.ToString(Inv) + " nodes): "
            + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.00", Inv) + " ms");

        sw.Restart();
        var found = 0;
        for (var r = 0; r < rounds; r++)
            foreach (var grp in groups)
                foreach (var e in grp.Entries)
                    if (e.Strings.Exists(GroupRuleField)) found++;
        sw.Stop();
        Out.WriteLine("  asking each of 20000 entries in 200 groups whether it carries KPG.Group: "
            + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.00", Inv) + " ms");

        sw.Restart();
        for (var r = 0; r < rounds; r++)
            foreach (var e in entries)
                if (e.Strings.Exists(RuleField)) found++;
        sw.Stop();
        Out.WriteLine("  asking each of 20000 entries whether it carries KPG: "
            + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.00", Inv) + " ms");

        sw.Restart();
        var d = 0;
        for (var i = 0; i < 1000; i++) d += Digest(R0 + i.ToString(Inv)).Length;
        sw.Stop();
        Out.WriteLine("  1000 SHA-256 digests of a rule text of about 50 characters: "
            + sw.Elapsed.TotalMilliseconds.ToString("0.00", Inv) + " ms");

        var one = db.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(Kpg(db).Entries.GetAt(0).Uuid.ToHexString())), true);
        long chars = 0;
        foreach (var kv in one.Strings) chars += kv.Key.Length + kv.Value.ReadString().Length;
        Out.WriteLine("  one seal entry holds " + chars.ToString(Inv) + " characters of strings");
        db.Close();
    }

    static string Cli(string exe, string dir, params string[] args)
    {
        var info = new ProcessStartInfo();
        info.FileName = exe;
        var sb = new StringBuilder();
        foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
        info.Arguments = sb.ToString();
        info.WorkingDirectory = dir;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.BaseStream.Flush();
            p.StandardInput.Close();
            p.WaitForExit();
            return p.ExitCode.ToString(Inv);
        }
    }

    static void RunXc(string version, string exe)
    {
        Out.WriteLine();
        Out.WriteLine("== KeePassXC " + version + ", keepassxc-cli merge -s <target> <source>, the target read back with KeePassLib " + PwDefs.VersionString + " ==");
        foreach (var sc in Scenarios())
        {
            sc.Name = "xc" + version + "-" + sc.Name;
            string a, b;
            var dir = Prepare(sc, out a, out b);
            Out.WriteLine();
            Out.WriteLine(sc.Name + ": " + sc.What);
            foreach (var into in new[] { "A", "B" })
            {
                var cell = Path.Combine(dir, "into" + into);
                Directory.CreateDirectory(cell);
                var target = Path.Combine(cell, "target.kdbx");
                var source = Path.Combine(cell, "source.kdbx");
                File.Copy(into == "A" ? a : b, target, true);
                File.Copy(into == "A" ? b : a, source, true);
                var exit = Cli(exe, cell, "merge", "-s", target, source);
                var read = Open(target);
                Out.WriteLine("  into " + into + " (exit " + exit + "): " + State(read)
                    + (sc.Name.EndsWith("11-two-seals") ? " | Z: " + Resolve(read, Fixed("entry Z"), false) : ""));
                read.Close();
            }
        }
    }

    // What a plain save by KeePassXC leaves of the store: add one entry elsewhere, read back.
    static void RunXcSave(string version, string exe)
    {
        var dir = Path.Combine(Work, "xc" + version + "-save");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path);
        var before = Open(path);
        var was = State(before);
        before.Close();
        var exit = Cli(exe, dir, "add", path, "V/xc-added");
        var read = Open(path);
        Out.WriteLine();
        Out.WriteLine("== KeePassXC " + version + ", a plain save (keepassxc-cli add, exit " + exit + ") ==");
        Out.WriteLine("  before: " + was);
        Out.WriteLine("  after:  " + State(read) + " | V: " + Resolve(read, Fixed("group V"), true));
        var ru = RU(read);
        Out.WriteLine("  rule entry of U: time " + ru.LastModificationTime.ToString("o", Inv) + ", strings " + string.Join(",", new List<string>(ru.Strings.GetKeys()).ToArray()));
        read.Close();
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        var what = args[1];
        using (Out = new StreamWriter(args[2], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString + ", " + what);
            if (what == "keepass") { RunKeePass(null); RunNewUuids(); RunHistory(); RunDuplicates(); }
            else if (what == "cost") RunCost();
            else if (what == "xc") { RunXcSave(args[3], args[4]); RunXc(args[3], args[4]); }
            else return 2;
        }
        return 0;
    }
}
