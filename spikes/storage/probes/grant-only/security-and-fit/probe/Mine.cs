using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

// Probe for the critique "grant-only, security and fit". Console only, invented content only.
//
//   store    KeePassLib 2.62 databases holding the grant-only store as option-grant-only.md section 5
//            defines it (grant and withdrawal entries, the withdrawal's database custom data key),
//            under two folds: "design", the design's rule (a grant's live letters are its mask less
//            the letters of every withdrawal of its row it did not see), and "cap", a variant in
//            which a withdrawal also records the letters its row kept and an unseen grant is held
//            within them. Merged under KeePassLib's five methods, both directions, and by
//            keepassxc-cli when its path is given.
//   guard    the strip guard of VaultHandlers.CouldPutBackUnprotected, re-implemented over the
//            grant-only resolver, before and after one grant record is lost.
//   legacy   a database carrying 0.1.0 rules in node custom data, read by today's AclResolver
//            (compiled from the worktree at a42e38aa) and by the grant-only fold.
//   example  an invented, ordinarily organized tree under four arrangements of one policy, and
//            five ordinary events.
static class Q
{
    static string Work;
    static TextWriter Out;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string Pw = "probe-only";
    const string Letters = "CRUSDLAP";
    const int C = 1, R = 2, U = 4, S = 8, D = 16, L = 32, A = 64, P = 128;
    const string Sid = "6f1d2c3b4a5968778695a4b3c2d1e0f9";
    const string KindField = "KPG.Kind", StoreField = "KPG.Store", IdField = "KPG.Id", NodeField = "KPG.Node",
        ProfileField = "KPG.Profile", MaskField = "KPG.Mask", SawField = "KPG.Saw", KeepField = "KPG.Keep";
    const string KeyPrefix = "KPG.Withdrawn.";

    static string MaskText(int m)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 8; i++) sb.Append((m & (1 << i)) != 0 ? Letters[i] : '.');
        return sb.ToString();
    }

    static int MaskOf(string text)
    {
        if (text == null || text.Length != 8) return -1;
        var m = 0;
        for (var i = 0; i < 8; i++)
        {
            if (text[i] == Letters[i]) m |= 1 << i;
            else if (text[i] != '.') return -1;
        }
        return m;
    }

    static byte[] Sha(string text) { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text)); }
    static byte[] First(byte[] from, int count) { var b = new byte[count]; Array.Copy(from, b, count); return b; }
    static PwUuid Fixed(string name) { return new PwUuid(First(Sha("probe " + name), 16)); }
    static string Hex(PwUuid u) { return u.ToHexString(); }
    static string IdOf(string name) { return MemUtil.ByteArrayToHexString(First(Sha("id " + name), 16)).ToLowerInvariant(); }
    static readonly PwUuid StoreUuid = Fixed("store group");
    static DateTime At(int s) { return T0.AddSeconds(s); }
    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup AddGroup(PwGroup parent, string name, PwUuid uuid, DateTime t)
    {
        var g = new PwGroup(false, false) { Uuid = uuid, Name = name };
        Stamp(g, t);
        parent.AddGroup(g, true);
        g.LocationChanged = t;
        return g;
    }

    static PwEntry AddEntry(PwGroup parent, PwUuid uuid, string title, DateTime t)
    {
        var e = new PwEntry(false, false) { Uuid = uuid };
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented"));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, ""));
        Stamp(e, t);
        parent.AddEntry(e, true);
        e.LocationChanged = t;
        return e;
    }

    static void Set(PwEntry e, string name, string value) { e.Strings.Set(name, new ProtectedString(false, value)); }
    static string Get(PwEntry e, string name) { return e.Strings.Exists(name) ? e.Strings.ReadSafe(name) : null; }
    static PwGroup Store(PwDatabase db) { return db.RootGroup.FindGroup(StoreUuid, true); }

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

    static PwDatabase Open(string path) { var db = new PwDatabase(); db.Open(IOConnectionInfo.FromPath(path), Key(), null); return db; }
    static void SaveTo(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }

    static bool InBin(PwDatabase db, PwEntry e)
    {
        if (db.RecycleBinUuid == null || db.RecycleBinUuid.Equals(PwUuid.Zero)) return false;
        var bin = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
        return bin != null && e.IsContainedIn(bin);
    }

    // =====================================================================================
    // The store
    // =====================================================================================

    sealed class Wd { public string Id, Node, Profile; public int Mask, Keep; }

    static List<Wd> Withdrawals(PwDatabase db)
    {
        var found = new Dictionary<string, Wd>(StringComparer.Ordinal);
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (Get(e, KindField) != "withdrawn") continue;
            var id = Get(e, IdField);
            var m = MaskOf(Get(e, MaskField));
            var k = MaskOf(Get(e, KeepField));
            if (id == null || m < 0) continue;
            found[id] = new Wd { Id = id, Node = Get(e, NodeField), Profile = Get(e, ProfileField), Mask = m, Keep = k < 0 ? 0 : k };
        }
        foreach (var kv in db.CustomData)
        {
            if (!kv.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)) continue;
            var id = kv.Key.Substring(KeyPrefix.Length);
            if (found.ContainsKey(id)) continue;
            var parts = kv.Value.Split(' ');
            if (parts.Length != 4) continue;
            found[id] = new Wd { Id = id, Node = parts[0], Profile = parts[1], Mask = MaskOf(parts[2]), Keep = Math.Max(0, MaskOf(parts[3])) };
        }
        return found.Values.ToList();
    }

    // Rows: node -> profile -> letters.
    static Dictionary<string, Dictionary<string, int>> Fold(PwDatabase db, bool cap)
    {
        var ws = Withdrawals(db);
        var rows = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (Get(e, KindField) != "grant" || Get(e, StoreField) != Sid || InBin(db, e)) continue;
            var m = MaskOf(Get(e, MaskField));
            var node = Get(e, NodeField);
            var profile = Get(e, ProfileField);
            if (m < 0 || node == null || profile == null) continue;
            var seen = new HashSet<string>((Get(e, SawField) ?? "").Split(' '), StringComparer.Ordinal);
            foreach (var w in ws)
            {
                if (w.Node != node || w.Profile != profile || seen.Contains(w.Id)) continue;
                m = cap ? (m & w.Keep) : (m & ~w.Mask);
            }
            if (m == 0) continue;
            Dictionary<string, int> r;
            if (!rows.TryGetValue(node, out r)) rows[node] = r = new Dictionary<string, int>(StringComparer.Ordinal);
            int was; r.TryGetValue(profile, out was);
            r[profile] = was | m;
        }
        return rows;
    }

    static int Row(Dictionary<string, Dictionary<string, int>> rows, PwUuid node, string profile)
    {
        Dictionary<string, int> r; int m;
        return rows.TryGetValue(Hex(node), out r) && r.TryGetValue(profile, out m) ? m : 0;
    }

    static string NewGrant(PwDatabase db, string name, PwUuid node, string profile, string mask, DateTime t)
    {
        var saw = Withdrawals(db).Where(w => w.Node == Hex(node) && w.Profile == profile).Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var id = IdOf(name);
        var e = AddEntry(Store(db), Fixed("record " + name), "KPG grant " + profile + " " + mask, t);
        Set(e, KindField, "grant");
        Set(e, StoreField, Sid);
        Set(e, IdField, id);
        Set(e, NodeField, Hex(node));
        Set(e, ProfileField, profile);
        Set(e, MaskField, mask);
        Set(e, SawField, saw.Count == 0 ? "none" : string.Join(" ", saw.ToArray()));
        return id;
    }

    // A withdrawal of these letters from the row as this copy holds it. It records the letters the
    // row kept as well, which only the "cap" fold reads.
    static string Withdraw(PwDatabase db, string name, PwUuid node, string profile, string mask, DateTime t)
    {
        var before = Row(Fold(db, false), node, profile);
        var keep = before & ~MaskOf(mask);
        var id = IdOf(name);
        var e = AddEntry(Store(db), Fixed("record " + name), "KPG withdrawn " + profile + " " + mask, t);
        Set(e, KindField, "withdrawn");
        Set(e, IdField, id);
        Set(e, NodeField, Hex(node));
        Set(e, ProfileField, profile);
        Set(e, MaskField, mask);
        Set(e, KeepField, MaskText(keep));
        db.CustomData.Set(KeyPrefix + id, Hex(node) + " " + profile + " " + mask + " " + MaskText(keep));

        // A grant this leaves nothing of is deleted in the same write.
        var ws = Withdrawals(db);
        foreach (var g in db.RootGroup.GetEntries(true).ToList())
        {
            if (Get(g, KindField) != "grant" || Get(g, NodeField) != Hex(node) || Get(g, ProfileField) != profile) continue;
            var m = MaskOf(Get(g, MaskField));
            var seen = new HashSet<string>((Get(g, SawField) ?? "").Split(' '), StringComparer.Ordinal);
            foreach (var w in ws) if (w.Node == Hex(node) && w.Profile == profile && !seen.Contains(w.Id)) m &= ~w.Mask;
            if (m == 0) { g.ParentGroup.Entries.Remove(g); db.DeletedObjects.Add(new PwDeletedObject(g.Uuid, t)); }
        }
        return id;
    }

    static PwGroup Gr(PwDatabase db, string name) { return db.RootGroup.FindGroup(Fixed("group " + name), true); }

    static void StoreBase(string path, string mask)
    {
        var db = NewDb("base-new.kdbx");
        var g = AddGroup(db.RootGroup, "G", Fixed("group G"), T0);
        AddEntry(g, Fixed("entry X"), "X", T0);
        var bin = AddGroup(db.RootGroup, "Recycle Bin", Fixed("bin"), T0);
        db.RecycleBinEnabled = true;
        db.RecycleBinUuid = bin.Uuid;
        var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, T0);
        store.EnableSearching = false;
        store.EnableAutoType = false;
        NewGrant(db, "g1", Fixed("group G"), "agent", mask, T0);
        SaveTo(db, path);
    }

    sealed class Case { public string Name, What, Base, Meet; public Action<PwDatabase> A, B; }

    static List<Case> Cases()
    {
        return new List<Case>
        {
            new Case
            {
                Name = "N1", Base = "CRU.DL..", Meet = ".R...L..",
                What = "A narrows agent's row on G from CRU.DL.. to .R...L.. (withdraws C, U, D); B, not knowing, adds S to that row",
                A = db => Withdraw(db, "w1", Fixed("group G"), "agent", "C.U.D...", At(100)),
                B = db => NewGrant(db, "g2", Fixed("group G"), "agent", "...S....", At(50)),
            },
            new Case
            {
                Name = "N2", Base = "CRU.DL..", Meet = ".R...L..",
                What = "as N1, but B's addition is the later act in time",
                A = db => Withdraw(db, "w1", Fixed("group G"), "agent", "C.U.D...", At(100)),
                B = db => NewGrant(db, "g2", Fixed("group G"), "agent", "...S....", At(200)),
            },
            new Case
            {
                Name = "N3", Base = "CR...L.P", Meet = "CR...L..",
                What = "A takes protected values away from agent on G (withdraws P); B, not knowing, adds U with P (..U....P)",
                A = db => Withdraw(db, "w1", Fixed("group G"), "agent", ".......P", At(100)),
                B = db => NewGrant(db, "g2", Fixed("group G"), "agent", "..U....P", At(200)),
            },
            new Case
            {
                Name = "D03", Base = ".R...L..", Meet = "........",
                What = "the design's 03: A withdraws L, B withdraws R",
                A = db => Withdraw(db, "w1", Fixed("group G"), "agent", ".....L..", At(100)),
                B = db => Withdraw(db, "w2", Fixed("group G"), "agent", ".R......", At(200)),
            },
            new Case
            {
                Name = "D04", Base = ".R...L..", Meet = "........",
                What = "the design's 04: A takes the whole row away, B adds U",
                A = db => Withdraw(db, "w1", Fixed("group G"), "agent", "CRUSDLAP", At(100)),
                B = db => NewGrant(db, "g2", Fixed("group G"), "agent", "..U.....", At(200)),
            },
            new Case
            {
                Name = "ADD", Base = ".R...L..", Meet = "(union expected)",
                What = "two additions to one row: A adds C, B adds S",
                A = db => NewGrant(db, "g2", Fixed("group G"), "agent", "C.......", At(100)),
                B = db => NewGrant(db, "g3", Fixed("group G"), "agent", "...S....", At(200)),
            },
        };
    }

    static string Rows(PwDatabase db)
    {
        return "design " + MaskText(Row(Fold(db, false), Fixed("group G"), "agent")) + "  cap " + MaskText(Row(Fold(db, true), Fixed("group G"), "agent"));
    }

    static string Cli(string exe, string dir, params string[] args)
    {
        var info = new ProcessStartInfo { FileName = exe, WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var sb = new StringBuilder();
        foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
        info.Arguments = sb.ToString();
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n" + Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.BaseStream.Flush();
            p.StandardInput.Close();
            p.WaitForExit();
            o.Wait(); e.Wait();
            return p.ExitCode.ToString(Inv);
        }
    }

    static void RunStore(string xcExe)
    {
        Out.WriteLine("== store: concurrent edits of one row, KeePassLib " + PwDefs.VersionString + " MergeIn" + (xcExe != null ? " and keepassxc-cli merge -s" : "") + " ==");
        Out.WriteLine("   each cell: agent's row on G under the design's fold and under the cap fold");
        foreach (var c in Cases())
        {
            var dir = Path.Combine(Work, "store-" + c.Name);
            Directory.CreateDirectory(dir);
            var basePath = Path.Combine(dir, "base.kdbx");
            StoreBase(basePath, c.Base);
            var a = Path.Combine(dir, "A.kdbx");
            var b = Path.Combine(dir, "B.kdbx");
            var da = Open(basePath); c.A(da); SaveTo(da, a);
            var dbb = Open(basePath); c.B(dbb); SaveTo(dbb, b);
            Out.WriteLine();
            Out.WriteLine(c.Name + ": " + c.What);
            Out.WriteLine("    base row " + c.Base + "; what both copies' rows allow (their meet): " + c.Meet);
            var oa = Open(a); Out.WriteLine("    A alone: " + Rows(oa)); oa.Close();
            var ob = Open(b); Out.WriteLine("    B alone: " + Rows(ob)); ob.Close();
            foreach (var method in new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids })
            {
                foreach (var into in new[] { "A", "B" })
                {
                    var target = Open(into == "A" ? a : b);
                    var source = Open(into == "A" ? b : a);
                    target.MergeIn(source, method, null);
                    source.Close();
                    var result = Path.Combine(dir, method + "-into" + into + ".kdbx");
                    SaveTo(target, result);
                    var read = Open(result);
                    Out.WriteLine("    " + method.ToString().PadRight(17) + " into " + into + ": " + Rows(read));
                    read.Close();
                }
            }
            if (xcExe != null)
            {
                foreach (var into in new[] { "A", "B" })
                {
                    var cell = Path.Combine(dir, "xc-into" + into);
                    Directory.CreateDirectory(cell);
                    var target = Path.Combine(cell, "target.kdbx");
                    var source = Path.Combine(cell, "source.kdbx");
                    File.Copy(into == "A" ? a : b, target, true);
                    File.Copy(into == "A" ? b : a, source, true);
                    var exit = Cli(xcExe, cell, "merge", "-s", target, source);
                    var read = Open(target);
                    Out.WriteLine("    keepassxc-cli     into " + into + " (exit " + exit + "): " + Rows(read));
                    read.Close();
                }
            }

            if (c.Name == "N1")
            {
                // After the merge, the operator adds S again on purpose: the new grant lists the
                // withdrawal it saw, so neither fold cuts it.
                var merged = Open(Path.Combine(dir, PwMergeMethod.Synchronize + "-intoA.kdbx"));
                NewGrant(merged, "g9", Fixed("group G"), "agent", "...S....", At(300));
                var again = Path.Combine(dir, "readded.kdbx");
                SaveTo(merged, again);
                var read = Open(again);
                Out.WriteLine("    then S granted again on the Synchronize result, after the merge: " + Rows(read));
                read.Close();
            }
        }
    }

    // =====================================================================================
    // The strip guard, over the grant-only resolver
    // =====================================================================================

    static bool Permits(Dictionary<string, Dictionary<string, int>> rows, PwEntry e, string profile, int verb, AclReach reach)
    {
        var path = new List<PwUuid> { e.Uuid };
        for (var g = e.ParentGroup; g != null; g = g.ParentGroup) path.Add(g.Uuid);
        foreach (var n in path)
        {
            var row = Row(rows, n, profile);
            if ((row & verb) == 0) continue;
            if (reach == AclReach.Fields || verb == S || verb == D) return true;
            if (reach == AclReach.Attachments && (row & A) != 0) return true;
            if (reach == AclReach.Protected && (row & P) != 0) return true;
        }
        return false;
    }

    static string Guard(PwDatabase db, PwEntry x)
    {
        var rows = Fold(db, false);
        // VaultHandlers.CouldPutBackUnprotected, one profile: may create, and may not create a protected value.
        var guard = Permits(rows, x, "agent", C, AclReach.Fields) && !Permits(rows, x, "agent", C, AclReach.Protected);
        var strip = Permits(rows, x, "agent", S, AclReach.Fields);
        return "agent may strip at X: " + strip + "; guard fires: " + guard + "; so stripping X's protected Password is "
            + (strip && !guard ? "PERMITTED" : "refused");
    }

    static void RunGuard()
    {
        Out.WriteLine();
        Out.WriteLine("== the strip guard over the grant-only resolver ==");
        var db = NewDb("guard.kdbx");
        var g = AddGroup(db.RootGroup, "G", Fixed("group G"), T0);
        var x = AddEntry(g, Fixed("entry X"), "X", T0);
        var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, T0);
        NewGrant(db, "g0", db.RootGroup.Uuid, "agent", "C.......", T0);
        NewGrant(db, "g1", Fixed("group G"), "agent", "...S....", T0);
        Out.WriteLine("    grants: agent C....... on the root, ...S.... on G; X in G holds a protected Password");
        Out.WriteLine("    before: " + Guard(db, x));
        var g0 = db.RootGroup.FindEntry(Fixed("record g0"), true);
        g0.ParentGroup.Entries.Remove(g0);
        Out.WriteLine("    the root's grant record deleted by hand, no deletion record");
        Out.WriteLine("    after:  " + Guard(db, x));
    }

    // =====================================================================================
    // A database holding 0.1.0 rules
    // =====================================================================================

    sealed class KNode : IAclNode
    {
        readonly PwGroup g; readonly PwEntry e;
        public KNode(PwGroup g) { this.g = g; }
        public KNode(PwEntry e) { this.e = e; }
        public string Document { get { return e != null ? e.CustomData.Get("KPG") : g.CustomData.Get("KPG"); } }
        public IAclNode Parent { get { return e != null ? new KNode(e.ParentGroup) : (g.ParentGroup == null ? null : new KNode(g.ParentGroup)); } }
        public string Name { get { return e != null ? e.Uuid.ToHexString() : g.Uuid.ToHexString(); } }
        public T Held<T>(Func<T> walk) { return walk(); }
    }

    static string Legacy(PwDatabase db, string title)
    {
        var e = db.RootGroup.GetEntries(true).First(x => Get(x, PwDefs.TitleField) == title);
        var today = AclResolver.Permits(new KNode(e), new[] { "reader" }, AclVerb.Read, AclReach.Protected, new AclDocuments());
        var go = Permits(Fold(db, false), e, "reader", R, AclReach.Protected);
        return title + ": today " + (today ? "reads" : "refused") + ", grant-only " + (go ? "READS" : "refused");
    }

    static string RootList(PwDatabase db)
    {
        var today = AclResolver.Permits(new KNode(db.RootGroup), new[] { "reader" }, AclVerb.List, AclReach.Fields, new AclDocuments());
        var go = (Row(Fold(db, false), db.RootGroup.Uuid, "reader") & L) != 0;
        return "list at the root (the flat sweeps' anchor): today " + (today ? "yes" : "no") + ", grant-only " + (go ? "yes" : "no");
    }

    static void RunLegacy()
    {
        Out.WriteLine();
        Out.WriteLine("== a database holding 0.1.0 rules, the README's shape: reader .R...L.P on the root, the folder Personal sealed ==");
        var db = NewDb("legacy.kdbx");
        var work = AddGroup(db.RootGroup, "Work", Fixed("group Work"), T0);
        AddEntry(work, Fixed("entry X"), "X", T0);
        var personal = AddGroup(db.RootGroup, "Personal", Fixed("group Personal"), T0);
        AddEntry(personal, Fixed("entry Q"), "Q", T0);
        AddEntry(db.RootGroup, Fixed("entry Z"), "Z", T0);
        var store = AddGroup(db.RootGroup, "KeePassGate", StoreUuid, T0);
        db.RootGroup.CustomData.Set("KPG", AclDocument.Of(AclDefault.Inherit, new Dictionary<string, IEnumerable<AclVerb>>
        {
            { "reader", new[] { AclVerb.Read, AclVerb.List, AclVerb.Protected } },
        }).Write());
        personal.CustomData.Set("KPG", AclDocument.Of(AclDefault.Deny, new Dictionary<string, IEnumerable<AclVerb>>()).Write());
        Out.WriteLine("    stored: root " + db.RootGroup.CustomData.Get("KPG") + ", Personal " + personal.CustomData.Get("KPG"));
        Out.WriteLine("  0.2.0 grant-only reading it, nothing converted:");
        foreach (var t in new[] { "X", "Q", "Z" }) Out.WriteLine("    " + Legacy(db, t));
        Out.WriteLine("    " + RootList(db));

        NewGrant(db, "habit", db.RootGroup.Uuid, "reader", ".R...L.P", At(100));
        Out.WriteLine("  the operator grants reader .R...L.P on the root again, as before:");
        foreach (var t in new[] { "X", "Q", "Z" }) Out.WriteLine("    " + Legacy(db, t));
        Out.WriteLine("    " + RootList(db));

        Withdraw(db, "unhabit", db.RootGroup.Uuid, "reader", ".R...L.P", At(200));
        NewGrant(db, "down1", work.Uuid, "reader", ".R...L.P", At(201));
        NewGrant(db, "down2", Fixed("entry Z"), "reader", ".R...L.P", At(202));
        Out.WriteLine("  instead the section 8 conversion, the root's grant pushed down to Work and to the entry Z at the root:");
        foreach (var t in new[] { "X", "Q", "Z" }) Out.WriteLine("    " + Legacy(db, t));
        Out.WriteLine("    " + RootList(db));
    }

    // =====================================================================================
    // A worked example: an ordinary tree, one policy, four arrangements, five events
    // =====================================================================================

    sealed class N : IAclNode
    {
        public string Label; public N Up; public bool Group; public List<N> Kids = new List<N>();
        public string Doc;
        public Dictionary<string, int> Rows = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Marks = new Dictionary<string, int>(StringComparer.Ordinal);
        string IAclNode.Document { get { return Doc; } }
        IAclNode IAclNode.Parent { get { return Up; } }
        string IAclNode.Name { get { return Label; } }
        T IAclNode.Held<T>(Func<T> walk) { return walk(); }
        public override string ToString() { return Label; }
    }

    static N G(N up, string label) { var n = new N { Label = label, Up = up, Group = true }; if (up != null) up.Kids.Add(n); return n; }
    static N E(N up, string label) { var n = new N { Label = label, Up = up }; up.Kids.Add(n); return n; }
    static IEnumerable<N> All(N n) { yield return n; foreach (var k in n.Kids) foreach (var d in All(k)) yield return d; }
    static N Find(N root, string label) { return All(root).First(n => n.Label == label); }
    static void MoveTo(N n, N to) { n.Up.Kids.Remove(n); n.Up = to; to.Kids.Add(n); }

    static N Tree()
    {
        var root = G(null, "Vault");
        var personal = G(root, "Personal");
        var banking = G(personal, "Banking");
        foreach (var e in new[] { "Bank A", "Bank B", "Broker" }) E(banking, e);
        var health = G(personal, "Health"); E(health, "Clinic portal"); E(health, "Insurance");
        var shopping = G(personal, "Shopping"); E(shopping, "Shop 1"); E(shopping, "Shop 2"); E(shopping, "Shop 3");
        var email = G(personal, "Email"); E(email, "Mail personal");
        var family = G(root, "Family"); E(family, "Alice phone PIN"); E(family, "Bob school portal");
        var work = G(root, "Work"); E(work, "Work email"); E(work, "VPN");
        var a = G(work, "ProjA");
        foreach (var e in new[] { "AWS root", "AWS deploy key", "GitHub token", "DB prod admin", "Staging DB", "Slack bot" }) E(a, e);
        var b = G(work, "ProjB");
        foreach (var e in new[] { "GCP owner", "CI token", "Sentry DSN" }) E(b, e);
        var home = G(root, "Home");
        foreach (var e in new[] { "Router admin", "NAS admin", "Home Assistant token", "Wi-Fi" }) E(home, e);
        return root;
    }

    static readonly string[] Profiles = { "ci", "home", "assistant" };

    // What each profile is meant to read, on the tree as it is.
    static bool Meant(N root, string profile, N e)
    {
        var path = new List<string>();
        for (var n = e.Up; n != null; n = n.Up) path.Add(n.Label);
        switch (profile)
        {
            case "ci": return new[] { "AWS deploy key", "GitHub token", "Staging DB", "Slack bot", "CI token", "Sentry DSN" }.Contains(e.Label);
            case "home": return new[] { "Home Assistant token", "Wi-Fi" }.Contains(e.Label);
            default: return !path.Contains("Health") && !path.Contains("Family");
        }
    }

    // ci and home read with protected values; the assistant reads without them.
    static AclReach ReachOf(string profile) { return profile == "assistant" ? AclReach.Fields : AclReach.Protected; }

    static string Doc(bool deny, params object[] pairs)
    {
        var d = new Dictionary<string, IEnumerable<AclVerb>>(StringComparer.Ordinal);
        for (var i = 0; i < pairs.Length; i += 2) d[(string)pairs[i]] = (AclVerb[])pairs[i + 1];
        return AclDocument.Of(deny ? AclDefault.Deny : AclDefault.Inherit, d).Write();
    }

    static readonly AclVerb[] ReadP = { AclVerb.Read, AclVerb.List, AclVerb.Protected };
    static readonly AclVerb[] ReadOnly = { AclVerb.Read, AclVerb.List };
    static readonly AclVerb[] Nothing = new AclVerb[0];

    sealed class Arrangement
    {
        public string Name, Model; public int Records, OnUserNodes, Moves, NewGroups;
        public Func<N> Build;
    }

    static List<Arrangement> Arrangements()
    {
        var list = new List<Arrangement>();
        list.Add(new Arrangement
        {
            Name = "today", Model = "today",
            Build = () =>
            {
                var t = Tree();
                Find(t, "ProjA").Doc = Doc(false, "ci", ReadP);
                Find(t, "AWS root").Doc = Doc(false, "ci", Nothing);
                Find(t, "DB prod admin").Doc = Doc(false, "ci", Nothing);
                Find(t, "ProjB").Doc = Doc(false, "ci", ReadP);
                Find(t, "GCP owner").Doc = Doc(false, "ci", Nothing);
                Find(t, "Home").Doc = Doc(false, "home", ReadP);
                Find(t, "Router admin").Doc = Doc(false, "home", Nothing);
                Find(t, "NAS admin").Doc = Doc(false, "home", Nothing);
                t.Doc = Doc(false, "assistant", ReadOnly);
                Find(t, "Health").Doc = Doc(true);
                Find(t, "Family").Doc = Doc(true);
                return t;
            },
        });
        list.Add(new Arrangement
        {
            Name = "grant-only, a grant per entry", Model = "grant",
            Build = () =>
            {
                var t = Tree();
                foreach (var e in new[] { "AWS deploy key", "GitHub token", "Staging DB", "Slack bot", "CI token", "Sentry DSN" }) Find(t, e).Rows["ci"] = R | L | P;
                foreach (var e in new[] { "Home Assistant token", "Wi-Fi" }) Find(t, e).Rows["home"] = R | L | P;
                foreach (var g in new[] { "Banking", "Shopping", "Email", "Work", "Home" }) Find(t, g).Rows["assistant"] = R | L;
                return t;
            },
        });
        list.Add(new Arrangement
        {
            Name = "grant-only, the tree split", Model = "grant", Moves = 5, NewGroups = 3,
            Build = () =>
            {
                var t = Tree();
                var aAdmin = G(Find(t, "Work"), "ProjA admin");
                var bAdmin = G(Find(t, "Work"), "ProjB admin");
                var hAdmin = G(t, "Home admin");
                MoveTo(Find(t, "AWS root"), aAdmin);
                MoveTo(Find(t, "DB prod admin"), aAdmin);
                MoveTo(Find(t, "GCP owner"), bAdmin);
                MoveTo(Find(t, "Router admin"), hAdmin);
                MoveTo(Find(t, "NAS admin"), hAdmin);
                Find(t, "ProjA").Rows["ci"] = R | L | P;
                Find(t, "ProjB").Rows["ci"] = R | L | P;
                Find(t, "Home").Rows["home"] = R | L | P;
                foreach (var g in new[] { "Banking", "Shopping", "Email", "Work", "Home", "Home admin" }) Find(t, g).Rows["assistant"] = R | L;
                return t;
            },
        });
        list.Add(new Arrangement
        {
            Name = "middle form, today's grants and marks", Model = "middle",
            Build = () =>
            {
                var t = Tree();
                Find(t, "ProjA").Rows["ci"] = R | L | P;
                Find(t, "ProjB").Rows["ci"] = R | L | P;
                Find(t, "Home").Rows["home"] = R | L | P;
                t.Rows["assistant"] = R | L;
                foreach (var e in new[] { "AWS root", "DB prod admin", "GCP owner" }) Find(t, e).Marks["ci"] = 255;
                foreach (var e in new[] { "Router admin", "NAS admin" }) Find(t, e).Marks["home"] = 255;
                foreach (var g in new[] { "Health", "Family" }) Find(t, g).Marks["*"] = 255;
                return t;
            },
        });
        return list;
    }

    static bool Reads(Arrangement a, N e, string profile)
    {
        var reach = ReachOf(profile);
        if (a.Model == "today") return AclResolver.Permits(e, new[] { profile }, AclVerb.Read, reach, new AclDocuments());
        var held = 0;
        if (a.Model == "middle")
            for (var n = e; n != null; n = n.Up)
            {
                int w;
                if (n.Marks.TryGetValue(profile, out w)) held |= w;
                if (n.Marks.TryGetValue("*", out w)) held |= w;
            }
        if ((held & R) != 0 || (reach == AclReach.Protected && (held & P) != 0)) return false;
        for (var n = e; n != null; n = n.Up)
        {
            int row;
            if (!n.Rows.TryGetValue(profile, out row) || (row & R) == 0) continue;
            if (reach == AclReach.Fields || (row & P) != 0) return true;
        }
        return false;
    }

    static bool ListsAtRoot(Arrangement a, N root, string profile)
    {
        if (a.Model == "today") return AclResolver.Permits(root, new[] { profile }, AclVerb.List, AclReach.Fields, new AclDocuments());
        int row; return root.Rows.TryGetValue(profile, out row) && (row & L) != 0;
    }

    static string Readers(Arrangement a, N e)
    {
        var who = Profiles.Where(p => Reads(a, e, p)).ToArray();
        return who.Length == 0 ? "nobody" : string.Join(", ", who);
    }

    static void RunExample()
    {
        Out.WriteLine();
        Out.WriteLine("== a worked example: an invented tree organized by subject, one policy, four arrangements ==");
        Out.WriteLine("   ci reads, with protected values: the deploy tokens of ProjA and ProjB, not AWS root, DB prod admin, GCP owner");
        Out.WriteLine("   home reads, with protected values: Home Assistant token and Wi-Fi, not Router admin, NAS admin");
        Out.WriteLine("   assistant reads, without protected values: everything but Personal/Health and Family");
        foreach (var a in Arrangements())
        {
            var t = a.Build();
            var entries = All(t).Where(n => !n.Group).ToList();
            var groups = All(t).Count(n => n.Group);
            var records = All(t).Count(n => n.Doc != null) + All(t).Sum(n => n.Rows.Count) + All(t).Sum(n => n.Marks.Count);
            var wrong = 0;
            foreach (var e in entries) foreach (var p in Profiles) if (Reads(a, e, p) != Meant(t, p, e)) wrong++;
            Out.WriteLine();
            Out.WriteLine("  " + a.Name + ": " + records + " records (" + (a.Model == "today" ? "rule documents in node custom data" : a.Model == "middle" ? All(t).Sum(n => n.Rows.Count) + " grants, " + All(t).Sum(n => n.Marks.Count) + " marks, each mark with a reference on its node" : "grants, nothing on any user node")
                + "), " + a.Moves + " moves, " + a.NewGroups + " new groups; answers differing from the policy on the " + entries.Count + " entries: " + wrong);

            var e1 = a.Build(); var n1 = E(Find(e1, "ProjA"), "AWS billing admin");
            Out.WriteLine("    E1 a new sensitive entry AWS billing admin made in Work/ProjA, on the phone: read by " + Readers(a, n1));
            var e2 = a.Build(); var n2 = E(G(e2, "Crypto"), "Exchange login");
            Out.WriteLine("    E2 a new top-level group Crypto with Exchange login, made on the phone: read by " + Readers(a, n2));
            var e3 = a.Build(); MoveTo(Find(e3, "Health"), Find(e3, "Shopping"));
            Out.WriteLine("    E3 Personal/Health dragged into Personal/Shopping: Clinic portal read by " + Readers(a, Find(e3, "Clinic portal")));
            var e4 = a.Build(); MoveTo(Find(e4, "AWS root"), Find(e4, "ProjB"));
            Out.WriteLine("    E4 AWS root moved into Work/ProjB: read by " + Readers(a, Find(e4, "AWS root")));
            var e5 = a.Build();
            var listers = Profiles.Where(p => ListsAtRoot(a, e5, p)).ToArray();
            Out.WriteLine("    E5 may list at the root, which the flat sweeps and the client's defaults need: " + (listers.Length == 0 ? "nobody" : string.Join(", ", listers)));
        }
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        using (Out = new StreamWriter(args[1], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString);
            RunStore(args.Length > 2 ? args[2] : null);
            RunGuard();
            RunLegacy();
            RunExample();
        }
        return 0;
    }
}
