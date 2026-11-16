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

// Attack probe for the storage option "rules-on-node". Invented databases only, no window.
// It models the design's store and read rule (option-rules-on-node.md sections 2 and 3) the same way
// the design's own probe does: seals are entries of one seal group, read only from that group.
static class A
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

    // R0 is the wide rule, R1 the narrowing.
    const string R0 = "{\"default\":\"inherit\",\"agent\":[\"read\",\"list\",\"protected\"]}";
    const string R1 = "{\"default\":\"inherit\",\"agent\":[]}";
    const string RG = "{\"default\":\"deny\",\"agent\":[\"list\"]}";

    static DateTime At(int s) { return T0.AddSeconds(s); }
    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }

    static PwUuid Fixed(string name)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes("attack " + name));
            var b = new byte[16]; Array.Copy(d, b, 16); return new PwUuid(b);
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
        var g = new PwGroup(false, false); g.Uuid = Fixed("group " + name); g.Name = name; Stamp(g, t);
        parent.AddGroup(g, true); g.LocationChanged = t; return g;
    }

    static PwEntry AddEntry(PwGroup parent, string name, DateTime t)
    {
        var e = new PwEntry(false, false); e.Uuid = Fixed("entry " + name);
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, name));
        Stamp(e, t); parent.AddEntry(e, true); e.LocationChanged = t; return e;
    }

    static void Set(PwEntry e, string name, string value) { e.Strings.Set(name, new ProtectedString(false, value)); }
    static void Edit(PwEntry e, DateTime t, Action<PwEntry> change) { e.CreateBackup(null); change(e); e.LastModificationTime = t; }

    static PwGroup Seals(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group Rule seals"), true); }

    // The seal's identifier is the first 16 bytes of the SHA-256 of its three strings (design 2.3).
    static PwUuid SealId(string node, string rule, string after)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes(node + "\n" + rule + "\n" + after));
            var b = new byte[16]; Array.Copy(d, b, 16); return new PwUuid(b);
        }
    }

    static string Seal(PwDatabase db, PwUuid node, string ruleText, string[] after, DateTime t)
    {
        var rule = ruleText == null ? "none" : Digest(ruleText);
        var afterText = string.Join(",", after ?? new string[0]);
        var e = new PwEntry(false, false);
        e.Uuid = SealId(node.ToHexString(), rule, afterText);
        Set(e, PwDefs.TitleField, "Rule seal " + node.ToHexString().Substring(0, 8));
        Set(e, NodeField, node.ToHexString()); Set(e, SealRule, rule); Set(e, SealAfter, afterText);
        e.AutoType.Enabled = false; Stamp(e, t);
        Seals(db).AddEntry(e, true); e.LocationChanged = t;
        return e.Uuid.ToHexString();
    }

    static PwDatabase NewDb()
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(Path.Combine(Work, "new.kdbx")), Key());
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, 2UL); a.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        db.KdfParameters = a; db.SettingsChanged = T0;
        db.RootGroup.Uuid = Fixed("root"); db.RootGroup.Name = "attack"; Stamp(db.RootGroup, T0);
        return db;
    }

    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }

    // Base: group U ruled RG by its rule entry; X in U ruled R0; a KPG group with a seal subgroup.
    static void MakeBase(string path)
    {
        var db = NewDb();
        var root = db.RootGroup;
        var u = AddGroup(root, "U", T0);
        var kpg = AddGroup(root, "KeePassGate", T0); kpg.EnableSearching = false; kpg.EnableAutoType = false;
        AddGroup(kpg, "Rule seals", T0);
        var x = AddEntry(u, "X", T0);
        x.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-x-0"));
        Set(x, RuleField, R0);
        var ru = AddEntry(u, "rule U", T0);
        Set(ru, PwDefs.TitleField, "KeePassGate rule"); Set(ru, GroupRuleField, RG); Set(ru, NodeField, u.Uuid.ToHexString());
        Seal(db, x.Uuid, R0, null, T0);
        Seal(db, u.Uuid, RG, null, T0);
        SaveTo(db, path);
    }

    sealed class Row { public string Id; public string Rule; public string[] After; public bool IdOk; }

    static List<Row> SealsOf(PwDatabase db, PwUuid node)
    {
        var rows = new List<Row>();
        var g = Seals(db);
        if (g == null) return rows;
        var hex = node.ToHexString();
        foreach (var e in g.Entries)
        {
            if (e.Strings.ReadSafe(NodeField) != hex || !e.Strings.Exists(SealRule)) continue;
            var after = e.Strings.ReadSafe(SealAfter);
            var rule = e.Strings.ReadSafe(SealRule);
            var ok = SealId(hex, rule, after).Equals(e.Uuid);
            if (!ok) continue; // a seal whose identifier does not match its strings is not read (design 2.3)
            rows.Add(new Row { Id = e.Uuid.ToHexString(), Rule = rule, After = after.Length == 0 ? new string[0] : after.Split(','), IdOk = ok });
        }
        return rows;
    }

    static string Short(string t) { return t == R0 ? "R0(wide)" : t == R1 ? "R1(narrow)" : t == RG ? "RG" : "'" + t + "'"; }

    // The design's section 3, steps 1 to 7.
    static string Resolve(PwDatabase db, PwUuid node, bool isGroup)
    {
        var current = new List<string>(); var past = new List<string>();
        if (isGroup)
        {
            var g = db.RootGroup.Uuid.Equals(node) ? db.RootGroup : db.RootGroup.FindGroup(node, true);
            if (g == null) return "absent";
            foreach (var e in g.Entries)
            {
                if (!e.Strings.Exists(GroupRuleField)) continue;
                current.Add(e.Strings.ReadSafe(GroupRuleField));
                foreach (var h in e.History) if (h.Strings.Exists(GroupRuleField)) past.Add(h.Strings.ReadSafe(GroupRuleField));
            }
        }
        else
        {
            var e = db.RootGroup.FindEntry(node, true);
            if (e == null) return "absent";
            if (e.Strings.Exists(RuleField)) current.Add(e.Strings.ReadSafe(RuleField));
            foreach (var h in e.History) if (h.Strings.Exists(RuleField)) past.Add(h.Strings.ReadSafe(RuleField));
        }
        var seals = SealsOf(db, node);
        if (seals.Count == 0) return current.Count == 0 ? "silence" : "REFUSE (a text and no seal)";
        var named = new HashSet<string>(); var known = new HashSet<string>();
        foreach (var s in seals) { known.Add(s.Rule); foreach (var a in s.After) named.Add(a); }
        var heads = seals.FindAll(s => !named.Contains(s.Id));
        if (heads.Count != 1) return "REFUSE (" + heads.Count.ToString(Inv) + " heads)";
        foreach (var t in current) if (!known.Contains(Digest(t))) return "REFUSE (a text no seal names)";
        if (heads[0].Rule == "none") return "silence (sealed none)";
        foreach (var t in current) if (Digest(t) == heads[0].Rule) return "RULE " + Short(t) + " on the node";
        foreach (var t in past) if (Digest(t) == heads[0].Rule) return "RULE " + Short(t) + " from history";
        return "REFUSE (the sealed text is nowhere)";
    }

    static PwEntry X(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry X"), true); }
    static PwGroup U(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group U"), true); }

    static PwEntry SealEntry(PwDatabase db, string id)
    {
        foreach (var e in Seals(db).Entries) if (e.Uuid.ToHexString() == id) return e;
        return null;
    }

    static string XState(PwDatabase db)
    {
        var x = X(db);
        var cur = x == null ? "-" : (x.Strings.Exists(RuleField) ? Short(x.Strings.ReadSafe(RuleField)) : "none");
        var n = Seals(db) == null ? 0 : (int)Seals(db).Entries.UCount;
        return "X: " + Resolve(db, Fixed("entry X"), false) + " [current text " + cur + ", seals in group " + n.ToString(Inv) + "]";
    }

    static void Recycle(PwDatabase db, PwEntry e, DateTime t)
    {
        // KeePass's own delete: into the bin, previous parent and location time set, modification time left alone.
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        if (bin == null)
        {
            bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
            bin.EnableAutoType = false; bin.EnableSearching = false; Stamp(bin, t);
            db.RootGroup.AddGroup(bin, true);
            db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = t; db.RecycleBinEnabled = true;
        }
        var from = e.ParentGroup; from.Entries.Remove(e);
        bin.AddEntry(e, true);
        e.PreviousParentGroup = from.Uuid; e.LocationChanged = t;
    }

    // S1: an ordinary rollback, then the head seal is recycled (or deleted) in one copy.
    static void S1()
    {
        Out.WriteLine("== S1: rollback, then the head seal recycled or deleted, KeePass " + PwDefs.VersionString + " Synchronize ==");
        var dir = Path.Combine(Work, "s1"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx"); MakeBase(basePath);
        var s0 = SealsOf(Open(basePath), Fixed("entry X"))[0].Id;

        var a = Path.Combine(dir, "A.kdbx"); var b = Path.Combine(dir, "B.kdbx");
        var da = Open(basePath);
        Edit(X(da), At(10), e => Set(e, RuleField, R1));
        var s1 = Seal(da, X(da).Uuid, R1, new[] { s0 }, At(10));
        SaveTo(da, a);
        var dbb = Open(basePath);
        Edit(X(dbb), At(20), e => e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-x-B")));
        SaveTo(dbb, b);

        var m = Path.Combine(dir, "M.kdbx");
        var t = Open(a); var s = Open(b); t.MergeIn(s, PwMergeMethod.Synchronize, null); s.Close(); SaveTo(t, m);
        var r = Open(m); Out.WriteLine("  after A <- B (narrowing in A, later password change in B): " + XState(r)); r.Close();

        foreach (var how in new[] { "recycled", "deleted with a deletion record" })
        {
            var c = Path.Combine(dir, "C-" + how.Split(' ')[0] + ".kdbx");
            var dc = Open(m);
            var e = SealEntry(dc, s1);
            if (how == "recycled") Recycle(dc, e, At(30));
            else { e.ParentGroup.Entries.Remove(e); dc.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(30))); }
            SaveTo(dc, c);
            var rc = Open(c); Out.WriteLine("  in the copy where the newest seal was " + how + ": " + XState(rc)); rc.Close();
            var m2 = Path.Combine(dir, "M2-" + how.Split(' ')[0] + ".kdbx");
            t = Open(m); s = Open(c); t.MergeIn(s, PwMergeMethod.Synchronize, null); s.Close(); SaveTo(t, m2);
            var r2 = Open(m2); Out.WriteLine("  after M <- that copy (Synchronize): " + XState(r2)); r2.Close();
        }

        // The same recycle after the repair has put the sealed text back on X.
        var rep = Path.Combine(dir, "M-repaired.kdbx");
        var dr = Open(m); Edit(X(dr), At(25), ex => Set(ex, RuleField, R1)); SaveTo(dr, rep);
        var dr2 = Open(rep); Recycle(dr2, SealEntry(dr2, s1), At(30)); Out.WriteLine("  the same recycle after the repair: " + XState(dr2)); dr2.Close();
    }

    static string Cli(string exe, string dir, params string[] args)
    {
        var info = new ProcessStartInfo();
        info.FileName = exe;
        var sb = new StringBuilder(); foreach (var x in args) sb.Append('"').Append(x).Append("\" ");
        info.Arguments = sb.ToString(); info.WorkingDirectory = dir; info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync(); var er = p.StandardError.ReadToEndAsync();
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); p.StandardInput.BaseStream.Flush(); p.StandardInput.Close();
            p.WaitForExit();
            File.WriteAllText(Path.Combine(dir, "cli.txt"), o.Result + "\n--- stderr\n" + er.Result);
            return p.ExitCode.ToString(Inv);
        }
    }

    // S2: the prune of section 4.5, then a stale copy merged by KeePassXC, which applies no deletion.
    static void S2(string xcVersion, string xc)
    {
        Out.WriteLine();
        Out.WriteLine("== S2: X narrowed, then deleted for good, its seals pruned; a stale copy merged in ==");
        var dir = Path.Combine(Work, "s2"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx"); MakeBase(basePath);
        var s0 = SealsOf(Open(basePath), Fixed("entry X"))[0].Id;
        var stale = Path.Combine(dir, "B-stale.kdbx"); File.Copy(basePath, stale, true);

        foreach (var prune in new[] { "pruned with deletion records", "pruned without deletion records", "not pruned" })
        {
            var tag = prune.Replace(' ', '-');
            var a = Path.Combine(dir, "A-" + tag + ".kdbx");
            var da = Open(basePath);
            Edit(X(da), At(10), e => Set(e, RuleField, R1));
            var s1 = Seal(da, X(da).Uuid, R1, new[] { s0 }, At(10));
            var x = X(da); x.ParentGroup.Entries.Remove(x); da.DeletedObjects.Add(new PwDeletedObject(x.Uuid, At(20)));
            if (prune != "not pruned")
                foreach (var id in new[] { s0, s1 })
                {
                    var e = SealEntry(da, id); e.ParentGroup.Entries.Remove(e);
                    if (prune == "pruned with deletion records") da.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(30)));
                }
            SaveTo(da, a);

            var cell = Path.Combine(dir, "xc-" + tag); Directory.CreateDirectory(cell);
            var target = Path.Combine(cell, "target.kdbx"); var source = Path.Combine(cell, "source.kdbx");
            File.Copy(a, target, true); File.Copy(stale, source, true);
            var exit = Cli(xc, cell, "merge", "-s", target, source);
            var r = Open(target);
            Out.WriteLine("  " + prune + "; keepassxc-cli " + xcVersion + " merge -s A B-stale (exit " + exit + "): " + XState(r));
            r.Close();

            var kp = Path.Combine(dir, "kp-" + tag + ".kdbx");
            var t = Open(a); var s = Open(stale); t.MergeIn(s, PwMergeMethod.Synchronize, null); s.Close(); SaveTo(t, kp);
            var rk = Open(kp); Out.WriteLine("  " + prune + "; KeePass Synchronize A <- B-stale: " + XState(rk)); rk.Close();
        }
    }

    // S3: one rule entry displaced to the root.
    static void S3()
    {
        Out.WriteLine();
        Out.WriteLine("== S3: U's rule entry moved to the root (by hand, or placed there by a merge) ==");
        var dir = Path.Combine(Work, "s3"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx"); MakeBase(basePath);
        var db = Open(basePath);
        var ru = db.RootGroup.FindEntry(Fixed("entry rule U"), true);
        ru.ParentGroup.Entries.Remove(ru); db.RootGroup.AddEntry(ru, true);
        Out.WriteLine("  root: " + Resolve(db, db.RootGroup.Uuid, true) + " | U: " + Resolve(db, Fixed("group U"), true));
        db.Close();
    }

    // S4: names that KeePass's string dictionary cannot tell from the reserved ones.
    static void S4()
    {
        Out.WriteLine();
        Out.WriteLine("== S4: look-alike names in an entry's string dictionary (culture " + CultureInfo.CurrentCulture.Name + ") ==");
        var cgj = "͏"; // COMBINING GRAPHEME JOINER, category Mn, not Cf
        Out.WriteLine("  U+034F category: " + CharUnicodeInfo.GetUnicodeCategory(cgj, 0));
        var e = new PwEntry(true, true);
        var name = "KPG.Gr" + cgj + "oup";
        e.Strings.Set(name, new ProtectedString(false, R0));
        Out.WriteLine("  an entry given only 'KPG.Gr<U+034F>oup': Strings.Exists(\"KPG.Group\") = " + e.Strings.Exists(GroupRuleField)
            + ", ReadSafe(\"KPG.Group\") is the planted text = " + (e.Strings.ReadSafe(GroupRuleField) == R0));
        bool ordinal = false; foreach (var kv in e.Strings) if (string.Equals(kv.Key, GroupRuleField, StringComparison.Ordinal)) ordinal = true;
        Out.WriteLine("  the same entry, keys compared ordinally: carries 'KPG.Group' = " + ordinal);
        var f = new PwEntry(true, true);
        f.Strings.Set("KP" + cgj + "G", new ProtectedString(false, R0));
        Out.WriteLine("  an entry given only 'KP<U+034F>G': Strings.Exists(\"KPG\") = " + f.Strings.Exists(RuleField));
        var g = new PwEntry(true, true);
        g.Strings.Set(RuleField, new ProtectedString(false, R1));
        g.Strings.Set("KPG" + cgj, new ProtectedString(false, R0));
        string keys = ""; foreach (var kv in g.Strings) keys += "[" + kv.Key.Length.ToString(Inv) + " chars]";
        Out.WriteLine("  an entry holding 'KPG', then Set('KPG<U+034F>'): keys " + keys + ", ReadSafe(\"KPG\") is now the second value = " + (g.Strings.ReadSafe(RuleField) == R0));
        // Names that do not begin with "KPG." or equal "KPG" byte for byte, so that an ordinal
        // reserved test lets them through.
        foreach (var n in new[] { "K" + cgj + "PG.Group", "KP" + cgj + "G.Group", cgj + "KPG.Group", "K" + cgj + "PG" })
        {
            var h = new PwEntry(true, true);
            h.Strings.Set(n, new ProtectedString(false, RG));
            var ordinalPrefix = n.StartsWith("KPG.", StringComparison.Ordinal) || string.Equals(n, "KPG", StringComparison.Ordinal);
            Out.WriteLine("  name " + Hex(n) + ": ordinal reserved test catches it = " + ordinalPrefix
                + ", Strings.Exists(\"KPG.Group\") = " + h.Strings.Exists(GroupRuleField)
                + ", Strings.Exists(\"KPG\") = " + h.Strings.Exists(RuleField)
                + ", culture IsPrefix(\"KPG.\") = " + CultureInfo.CurrentCulture.CompareInfo.IsPrefix(n, "KPG.", CompareOptions.None));
        }
        // The same entry put in group U of the base and read with the design's rule.
        var dir = Path.Combine(Work, "s4"); Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx"); MakeBase(basePath);
        var db = Open(basePath);
        var planted = AddEntry(db.RootGroup, "planted", At(40));
        planted.Strings.Set("K" + cgj + "PG.Group", new ProtectedString(false, R0));
        Out.WriteLine("  an entry with 'K<U+034F>PG.Group' added at the root, read with the design's rule as its probe reads it: root "
            + Resolve(db, db.RootGroup.Uuid, true) + " | U " + Resolve(db, Fixed("group U"), true) + " | X " + Resolve(db, Fixed("entry X"), false));
        db.Close();
    }

    static string Hex(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(c < 0x80 && !char.IsControl(c) ? c.ToString() : "<U+" + ((int)c).ToString("X4", Inv) + ">");
        return sb.ToString();
    }

    static int Main(string[] args)
    {
        Work = args[0]; Directory.CreateDirectory(Work);
        using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("attack probe " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString);
            S1();
            if (args.Length >= 4) S2(args[2], args[3]);
            S3();
            S4();
        }
        return 0;
    }
}
