using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

// Probe for the storage option "access rules as entries in the plugin's own group, naming the
// node by UUID". Throwaway databases with invented content only, all under the work directory
// given first. No window: only KeePassLib types, and keepassxc-cli as a console child process.
//
// The store it models:
//   the store group     fixed UUID, the first 16 bytes of SHA-256("KPG/1/group/")
//   a rule version      one entry, written once and never changed, strings
//                         KPG.Kind  = rule
//                         KPG.Node  = the governed group's or entry's UUID, 32 hex digits
//                         KPG.After = the versions it replaces, their identifiers sorted and
//                                     separated by one space; empty for a first rule
//                         KPG.Data  = the rule document, as AclDocument writes it today
//                       its UUID is  mark (4 bytes) | node tag (6 bytes) | digest (6 bytes):
//                         mark   = the first 4 bytes of SHA-256("KPG/1/rule/")
//                         tag    = the first 6 bytes of the node's UUID
//                         digest = the first 6 bytes of SHA-256("KPG/1/rule/<node>/<after>/<data>")
//   nothing on the governed node
static class P
{
    static string Work;
    static TextWriter Out;
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Pw = "probe-only";

    const string KindField = "KPG.Kind";
    const string NodeField = "KPG.Node";
    const string AfterField = "KPG.After";
    const string DataField = "KPG.Data";
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    const string R0 = "{\"default\":\"inherit\",\"reader\":[\"read\",\"list\"]}";
    const string R1 = "{\"default\":\"inherit\",\"reader\":[]}";
    const string R2 = "{\"default\":\"deny\",\"ops\":[\"read\",\"update\",\"list\"],\"reader\":[\"list\"]}";
    const string RG0 = "{\"default\":\"inherit\",\"agent\":[\"read\",\"list\"]}";
    const string RG1 = "{\"default\":\"deny\"}";
    const string NoRule = "{\"default\":\"inherit\"}";

    static readonly byte[] Mark = First(Sha("KPG/1/rule/"), 4);
    static readonly PwUuid StoreUuid = new PwUuid(First(Sha("KPG/1/group/"), 16));

    static byte[] Sha(string text)
    {
        using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text));
    }

    static byte[] First(byte[] from, int count)
    {
        var b = new byte[count];
        Array.Copy(from, b, count);
        return b;
    }

    static DateTime At(int s) { return T0.AddSeconds(s); }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Pw)); return k; }

    static PwUuid Fixed(string name) { return new PwUuid(First(Sha("probe " + name), 16)); }

    static string Hex(PwUuid u) { return u.ToHexString(); }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup AddGroup(PwGroup parent, string name, PwUuid uuid, DateTime t)
    {
        var g = new PwGroup(false, false);
        g.Uuid = uuid;
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

    static PwGroup Store(PwDatabase db)
    {
        return db.RootGroup.FindGroup(StoreUuid, true);
    }

    // ---- the identifier of a rule version ----

    static bool Marked(PwUuid u)
    {
        var b = u.UuidBytes;
        return b[0] == Mark[0] && b[1] == Mark[1] && b[2] == Mark[2] && b[3] == Mark[3];
    }

    static string TagOf(byte[] uuid, int offset)
    {
        return MemUtil.ByteArrayToHexString(new[] { uuid[offset], uuid[offset + 1], uuid[offset + 2], uuid[offset + 3], uuid[offset + 4], uuid[offset + 5] });
    }

    static PwUuid VersionId(string nodeHex, string afterText, string data)
    {
        if (nodeHex == null || nodeHex.Length != 32) return null;
        byte[] node;
        try { node = MemUtil.HexStringToByteArray(nodeHex); }
        catch (Exception) { return null; }
        if (node == null || node.Length != 16) return null;
        var d = Sha("KPG/1/rule/" + nodeHex + "/" + afterText + "/" + data);
        var b = new byte[16];
        Array.Copy(Mark, 0, b, 0, 4);
        Array.Copy(node, 0, b, 4, 6);
        Array.Copy(d, 0, b, 10, 6);
        return new PwUuid(b);
    }

    // Writes one rule version, as the plugin would: an entry added, nothing else touched.
    static string AddVersion(PwDatabase db, PwUuid node, string data, string[] after, DateTime t)
    {
        var list = new List<string>(after ?? new string[0]);
        list.Sort(StringComparer.Ordinal);
        var afterText = string.Join(" ", list.ToArray());
        var id = VersionId(Hex(node), afterText, data);
        var store = Store(db);
        foreach (var have in store.Entries) if (have.Uuid.Equals(id)) return Hex(id);
        var e = new PwEntry(false, false);
        e.Uuid = id;
        Set(e, PwDefs.TitleField, "KPG rule " + Hex(node).Substring(0, 8));
        Set(e, PwDefs.UserNameField, string.Empty);
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, string.Empty));
        Set(e, PwDefs.UrlField, string.Empty);
        Set(e, PwDefs.NotesField, NotesLine);
        Set(e, KindField, "rule");
        Set(e, NodeField, Hex(node));
        Set(e, AfterField, afterText);
        Set(e, DataField, data);
        Stamp(e, t);
        store.AddEntry(e, true);
        e.LocationChanged = t;
        return Hex(id);
    }

    static PwEntry VersionEntry(PwDatabase db, string id)
    {
        return db.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(id)), true);
    }

    static void DeleteForGood(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    // ---- the read path of the design ----

    sealed class Ver
    {
        public string Id;
        public string Node;
        public string[] After;
        public string Data;
        public bool Copy;
        public string Where;
    }

    sealed class View
    {
        public Dictionary<string, List<Ver>> ByNode = new Dictionary<string, List<Ver>>(StringComparer.Ordinal);
        public Dictionary<string, string> Damage = new Dictionary<string, string>(StringComparer.Ordinal);
        public HashSet<string> Present = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Named = new HashSet<string>(StringComparer.Ordinal);
        public int Versions;
        public int Copies;
        public int TaggedRecords;
    }

    static void Walk(PwGroup g, Action<PwEntry> each)
    {
        foreach (var e in g.Entries) each(e);
        foreach (var sub in g.Groups) Walk(sub, each);
    }

    static void Flag(View v, string tag, string reason)
    {
        if (!v.Damage.ContainsKey(tag)) v.Damage[tag] = reason;
    }

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
            if (marked)
            {
                if (derived == null || !derived.Equals(e.Uuid) || kind != "rule")
                {
                    // A rule entry whose content no longer derives its identifier: changed by hand.
                    Flag(v, TagOf(e.Uuid.UuidBytes, 4), "a rule entry was changed by hand");
                    if (node.Length == 32) Flag(v, node.Substring(0, 12), "a rule entry was changed by hand");
                    return;
                }
            }
            else if (derived == null) return;
            var ver = new Ver();
            ver.Id = Hex(derived);
            ver.Node = node;
            ver.After = after.Length == 0 ? new string[0] : after.Split(' ');
            ver.Data = data;
            ver.Copy = !marked;
            ver.Where = e.ParentGroup.Name;
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
            v.TaggedRecords++;
            var id = Hex(d.Uuid);
            if (v.Present.Contains(id) || v.Named.Contains(id)) continue;
            Flag(v, TagOf(d.Uuid.UuidBytes, 4), "a rule entry was deleted");
        }
        return v;
    }

    static string Short(string text)
    {
        if (text == R0) return "R0";
        if (text == R1) return "R1";
        if (text == R2) return "R2";
        if (text == RG0) return "RG0";
        if (text == RG1) return "RG1";
        if (text == NoRule) return "none";
        return text;
    }

    static readonly Regex Member = new Regex("\"([^\"]+)\":(?:\"([^\"]*)\"|\\[([^\\]]*)\\])");

    // What every one of several versions allows, written as one document: a profile all of them
    // name keeps the words all of them give it, a profile only some name keeps nothing, and the
    // default is deny when any of them says deny.
    static string Meet(List<string> docs)
    {
        var deny = false;
        var per = new List<Dictionary<string, List<string>>>();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (Match m in Member.Matches(doc))
            {
                var name = m.Groups[1].Value;
                if (name == "default") { if (m.Groups[2].Value == "deny") deny = true; continue; }
                var words = new List<string>();
                foreach (var w in m.Groups[3].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) words.Add(w.Trim('"'));
                d[name] = words;
                names.Add(name);
            }
            per.Add(d);
        }
        var sb = new StringBuilder();
        sb.Append("{\"default\":\"").Append(deny ? "deny" : "inherit").Append("\"");
        foreach (var name in names)
        {
            List<string> kept = null;
            var all = true;
            foreach (var d in per)
            {
                List<string> words;
                if (!d.TryGetValue(name, out words)) { all = false; break; }
                if (kept == null) kept = new List<string>(words);
                else kept.RemoveAll(w => !words.Contains(w));
            }
            if (!all) kept = new List<string>();
            sb.Append(",\"").Append(name).Append("\":[");
            for (var i = 0; i < kept.Count; i++) sb.Append(i == 0 ? "\"" : ",\"").Append(kept[i]).Append("\"");
            sb.Append("]");
        }
        return sb.Append("}").ToString();
    }

    static string Resolve(View v, PwDatabase db, PwUuid node)
    {
        var present = db.RootGroup.Uuid.Equals(node) || db.RootGroup.FindGroup(node, true) != null || db.RootGroup.FindEntry(node, true) != null;
        var hex = Hex(node);
        List<Ver> rows;
        v.ByNode.TryGetValue(hex, out rows);
        if (!present) return "absent" + (rows == null ? string.Empty : " (" + rows.Count.ToString(Inv) + " rule entries left without a node)");
        string reason;
        if (v.Damage.TryGetValue(hex.Substring(0, 12), out reason)) return "REFUSE (" + reason + ")";
        if (rows == null) return "silence (no rule)";
        var heads = new List<Ver>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rows) if (!v.Named.Contains(r.Id) && seen.Add(r.Id)) heads.Add(r);
        if (heads.Count == 0) return "REFUSE (no version is the newest)";
        if (heads.Count == 1)
        {
            var h = heads[0];
            var where = h.Where == "KeePassGate" ? string.Empty : " [found in " + h.Where + "]";
            return (h.Data == NoRule ? "silence (rule taken away)" : "RULE " + Short(h.Data)) + (h.Copy ? " [a copy]" : string.Empty) + where;
        }
        var docs = new List<string>();
        var label = new List<string>();
        foreach (var h in heads) { docs.Add(h.Data); label.Add(Short(h.Data)); }
        label.Sort(StringComparer.Ordinal);
        return "MEET of " + string.Join("+", label.ToArray()) + " = " + Meet(docs);
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

    static string KdbxVersion(string path)
    {
        var b = new byte[12];
        using (var f = File.OpenRead(path)) f.Read(b, 0, 12);
        return BitConverter.ToUInt16(b, 10).ToString(Inv) + "." + BitConverter.ToUInt16(b, 8).ToString(Inv);
    }

    // The base every scenario starts from.
    //   U            a user's group, ruled RG0
    //     X          an entry ruled R0 (and, when asked, then R1)
    //     Y, Z       entries with no rule
    //   W            a user's group with no rule
    //     Q          an entry with no rule
    //   KeePassGate  the store group, searching and auto-type off, holding the rule versions
    static Dictionary<string, string> MakeBase(string path, bool second)
    {
        var db = NewDb("base-new.kdbx");
        var root = db.RootGroup;
        var u = AddGroup(root, "U", Fixed("group U"), T0);
        var w = AddGroup(root, "W", Fixed("group W"), T0);
        var store = AddGroup(root, "KeePassGate", StoreUuid, T0);
        store.EnableSearching = false;
        store.EnableAutoType = false;
        store.Notes = NotesLine;

        var x = AddEntry(u, "X", "X", T0);
        x.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-x-0"));
        var y = AddEntry(u, "Y", "Y", T0);
        y.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-y-0"));
        AddEntry(u, "Z", "Z", T0);
        var q = AddEntry(w, "Q", "Q", T0);
        q.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw-q-0"));

        var ids = new Dictionary<string, string>();
        ids["X1"] = AddVersion(db, x.Uuid, R0, null, T0);
        ids["U1"] = AddVersion(db, u.Uuid, RG0, null, T0);
        if (second) ids["X2"] = AddVersion(db, x.Uuid, R1, new[] { ids["X1"] }, At(5));
        SaveTo(db, path);
        return ids;
    }

    static PwEntry X(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry X"), true); }
    static PwEntry Y(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry Y"), true); }
    static PwEntry Z(PwDatabase db) { return db.RootGroup.FindEntry(Fixed("entry Z"), true); }
    static PwGroup U(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group U"), true); }
    static PwGroup W(PwDatabase db) { return db.RootGroup.FindGroup(Fixed("group W"), true); }
    static void Password(PwEntry e, string value) { e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value)); }

    static string State(PwDatabase db)
    {
        var v = Read(db);
        var sb = new StringBuilder();
        var x = X(db);
        sb.Append("X: ").Append(Resolve(v, db, Fixed("entry X")));
        if (x != null) sb.Append(" [in ").Append(x.ParentGroup.Name).Append(", pw ").Append(x.Strings.ReadSafe(PwDefs.PasswordField)).Append(", hist ").Append(x.History.UCount.ToString(Inv)).Append("]");
        sb.Append(" | Y: ").Append(Resolve(v, db, Fixed("entry Y")));
        sb.Append(" | Z: ").Append(Resolve(v, db, Fixed("entry Z")));
        sb.Append(" | U: ").Append(Resolve(v, db, Fixed("group U")));
        sb.Append(" | store: ").Append(v.Versions.ToString(Inv)).Append(" versions, ").Append(v.Copies.ToString(Inv)).Append(" copies, ")
            .Append(v.TaggedRecords.ToString(Inv)).Append(" deletion records of rule entries");
        var store = Store(db);
        if (store != null)
        {
            var hist = 0u;
            foreach (var e in store.Entries) hist += e.History.UCount;
            if (hist != 0) sb.Append(", ").Append(hist.ToString(Inv)).Append(" history items");
        }
        else sb.Append(", NO STORE GROUP");
        return sb.ToString();
    }

    sealed class Scenario
    {
        public string Name;
        public string What;
        public bool Second;
        public Action<PwDatabase, Dictionary<string, string>> A;
        public Action<PwDatabase, Dictionary<string, string>> B;
    }

    static List<Scenario> Scenarios()
    {
        var list = new List<Scenario>();

        list.Add(new Scenario
        {
            Name = "01-other-edits",
            What = "A narrows X to R1 and seals U off with RG1 at +10; B, not knowing, changes X's password, edits Z, adds an entry to U and U's own time and notes move, all at +20",
            A = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(10)); AddVersion(db, U(db).Uuid, RG1, new[] { s["U1"] }, At(10)); },
            B = (db, s) =>
            {
                Edit(X(db), At(20), e => Password(e, "pw-x-B"));
                Edit(Z(db), At(20), e => Set(e, PwDefs.UserNameField, "z-B"));
                AddEntry(U(db), "N", "N", At(20));
                U(db).LastModificationTime = At(20);
                U(db).Notes = "B's notes";
            },
        });
        list.Add(new Scenario
        {
            Name = "02-same-second",
            What = "as 01 for X, both in the same second (+10)",
            A = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(10)); },
            B = (db, s) => { Edit(X(db), At(10), e => Password(e, "pw-x-B")); },
        });
        list.Add(new Scenario
        {
            Name = "03-two-changes",
            What = "A sets X's rule to R1 at +10, B sets it to R2 at +20, each replacing the base version",
            A = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(10)); },
            B = (db, s) => { AddVersion(db, X(db).Uuid, R2, new[] { s["X1"] }, At(20)); },
        });
        list.Add(new Scenario
        {
            Name = "04-taken-away",
            What = "A takes X's rule away at +10 (a version saying nothing); B changes X's password at +20",
            A = (db, s) => { AddVersion(db, X(db).Uuid, NoRule, new[] { s["X1"] }, At(10)); },
            B = (db, s) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); },
        });
        list.Add(new Scenario
        {
            Name = "05-two-first-rules",
            What = "A rules Y (R1) at +10, B rules Z (R2) at +20: a rule entry added in each copy",
            A = (db, s) => { AddVersion(db, Y(db).Uuid, R1, null, At(10)); },
            B = (db, s) => { AddVersion(db, Z(db).Uuid, R2, null, At(20)); },
        });
        list.Add(new Scenario
        {
            Name = "06-same-decision",
            What = "A sets X's rule to R1 at +10 and B makes the same change at +20",
            A = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(10)); },
            B = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(20)); },
        });
        list.Add(new Scenario
        {
            Name = "07-newest-deleted",
            What = "both copies hold X's rule R0 then R1; B deletes the R1 entry for good at +20 (a deletion record); A does nothing",
            Second = true,
            A = (db, s) => { },
            B = (db, s) => { DeleteForGood(db, VersionEntry(db, s["X2"]), At(20)); },
        });
        list.Add(new Scenario
        {
            Name = "08-newest-binned",
            What = "both copies hold R0 then R1; B moves the R1 entry into a recycle bin at +20; A does nothing",
            Second = true,
            A = (db, s) => { },
            B = (db, s) =>
            {
                var bin = AddGroup(db.RootGroup, "Recycle Bin", Fixed("group bin"), At(20));
                bin.EnableSearching = false;
                bin.EnableAutoType = false;
                db.RecycleBinUuid = bin.Uuid;
                db.RecycleBinChanged = At(20);
                var e = VersionEntry(db, s["X2"]);
                e.ParentGroup.Entries.Remove(e);
                bin.AddEntry(e, true);
                e.LocationChanged = At(20);
            },
        });
        list.Add(new Scenario
        {
            Name = "09-edited-by-hand",
            What = "both copies hold R0 then R1; B widens the text of the R1 entry by hand in another program at +20; A does nothing",
            Second = true,
            A = (db, s) => { },
            B = (db, s) => { Edit(VersionEntry(db, s["X2"]), At(20), e => Set(e, DataField, "{\"default\":\"inherit\",\"reader\":[\"read\",\"list\",\"update\"]}")); },
        });
        list.Add(new Scenario
        {
            Name = "10-node-moved",
            What = "A narrows X to R1 at +10; B moves X from U to W at +20",
            A = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(10)); },
            B = (db, s) => { var x = X(db); x.ParentGroup.Entries.Remove(x); W(db).AddEntry(x, true); x.LocationChanged = At(20); },
        });
        list.Add(new Scenario
        {
            Name = "11-node-deleted",
            What = "A narrows X to R1 at +10; B deletes X for good at +20",
            A = (db, s) => { AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(10)); },
            B = (db, s) => { DeleteForGood(db, X(db), At(20)); },
        });
        list.Add(new Scenario
        {
            Name = "12-node-deleted-then-edited",
            What = "B deletes X for good at +10; A changes X's password at +20",
            A = (db, s) => { Edit(X(db), At(20), e => Password(e, "pw-x-A")); },
            B = (db, s) => { DeleteForGood(db, X(db), At(10)); },
        });
        list.Add(new Scenario
        {
            Name = "13-pruned-then-back",
            What = "A deletes X for good at +10 and prunes its rule entry with a deletion record; B changes X's password at +20",
            A = (db, s) => { DeleteForGood(db, X(db), At(10)); DeleteForGood(db, VersionEntry(db, s["X1"]), At(10)); },
            B = (db, s) => { Edit(X(db), At(20), e => Password(e, "pw-x-B")); },
        });
        list.Add(new Scenario
        {
            Name = "14-pruned-newer-unseen",
            What = "A narrows X to R1 at +5, then deletes X for good and prunes both rule entries with deletion records at +10; B, which never saw R1, changes X's password at +20",
            A = (db, s) =>
            {
                var x2 = AddVersion(db, X(db).Uuid, R1, new[] { s["X1"] }, At(5));
                DeleteForGood(db, X(db), At(10));
                DeleteForGood(db, VersionEntry(db, s["X1"]), At(10));
                DeleteForGood(db, VersionEntry(db, x2), At(10));
            },
            B = (db, s) => { Edit(X(db), At(20), x => Password(x, "pw-x-B")); },
        });
        return list;
    }

    static string Prepare(Scenario sc, out string a, out string b)
    {
        var dir = Path.Combine(Work, sc.Name);
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        var ids = MakeBase(basePath, sc.Second);
        a = Path.Combine(dir, "A.kdbx");
        b = Path.Combine(dir, "B.kdbx");
        var da = Open(basePath); sc.A(da, ids); SaveTo(da, a);
        var db = Open(basePath); sc.B(db, ids); SaveTo(db, b);
        return dir;
    }

    static void Inputs(string a, string b)
    {
        var da = Open(a);
        Out.WriteLine("  copy A alone: " + State(da));
        da.Close();
        var db = Open(b);
        Out.WriteLine("  copy B alone: " + State(db));
        db.Close();
    }

    static void RunKeePass()
    {
        Out.WriteLine("== KeePass " + PwDefs.VersionString + " (KeePassLib), PwDatabase.MergeIn, each result saved and reopened ==");
        var versions = new SortedSet<string>();
        foreach (var sc in Scenarios())
        {
            string a, b;
            var dir = Prepare(sc, out a, out b);
            Out.WriteLine();
            Out.WriteLine(sc.Name + ": " + sc.What);
            Inputs(a, b);
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
                    versions.Add(KdbxVersion(result));
                    var read = Open(result);
                    Out.WriteLine("  " + method.ToString().PadRight(17) + " into " + into + ": " + State(read));
                    read.Close();
                }
            }
        }
        Out.WriteLine();
        Out.WriteLine("KDBX versions of every file written above: " + string.Join(", ", new List<string>(versions).ToArray()));
    }

    static void RunNewUuids()
    {
        Out.WriteLine();
        Out.WriteLine("== KeePass " + PwDefs.VersionString + ", CreateNewUuids (what File then Import preselects), a copy of the database imported into itself ==");
        var dir = Path.Combine(Work, "20-new-uuids");
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        MakeBase(basePath, true);
        var target = Open(basePath);
        var source = Open(basePath);
        target.MergeIn(source, PwMergeMethod.CreateNewUuids, null);
        source.Close();
        var result = Path.Combine(dir, "result.kdbx");
        SaveTo(target, result);
        var read = Open(result);
        var v = Read(read);
        Out.WriteLine("  the originals: " + State(read));
        foreach (var g in read.RootGroup.GetGroups(true))
        {
            if (g.Name == "KeePassGate" && !g.Uuid.Equals(StoreUuid))
                Out.WriteLine("  a second group named KeePassGate under a new identifier, holding " + g.Entries.UCount.ToString(Inv) + " entries, in " + g.ParentGroup.Name);
            if (g.Name != "U" || g.Uuid.Equals(Fixed("group U"))) continue;
            Out.WriteLine("  the copy of U (new identifier, in " + g.ParentGroup.Name + "): " + Resolve(v, read, g.Uuid));
            foreach (var e in g.Entries)
                if (e.Strings.ReadSafe(PwDefs.TitleField) == "X")
                    Out.WriteLine("  the copy of X (new identifier): " + Resolve(v, read, e.Uuid));
        }
        var deleted = read.DeleteDuplicateEntries(null);
        Out.WriteLine("  then Delete Duplicate Entries: " + deleted.ToString(Inv) + " deleted");
        Out.WriteLine("  after it: " + State(read));
        read.Close();
    }

    static void RunDuplicates()
    {
        Out.WriteLine();
        Out.WriteLine("== Delete Duplicate Entries on the store as it is (PwDatabase.DeleteDuplicateEntries) ==");
        var dir = Path.Combine(Work, "22-duplicates");
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "base.kdbx");
        var ids = MakeBase(basePath, true);
        var db = Open(basePath);
        // Two nodes given the very same rule text, and a rule set back to an earlier text.
        AddVersion(db, Y(db).Uuid, R0, null, At(10));
        AddVersion(db, Z(db).Uuid, R0, null, At(10));
        AddVersion(db, X(db).Uuid, R0, new[] { ids["X2"] }, At(10));
        var before = Store(db).Entries.UCount;
        var deleted = db.DeleteDuplicateEntries(null);
        Out.WriteLine("  " + before.ToString(Inv) + " rule entries, three of them holding the text R0 for three nodes and one holding it again for X: " + deleted.ToString(Inv) + " deleted");
        Out.WriteLine("  " + State(db));
        db.Close();
    }

    static void RunCost(int groups, int perGroup)
    {
        var db = NewDb("cost.kdbx");
        var root = db.RootGroup;
        var store = AddGroup(root, "KeePassGate", StoreUuid, T0);
        var all = new List<PwEntry>();
        var allGroups = new List<PwGroup>();
        for (var g = 0; g < groups; g++)
        {
            var grp = AddGroup(root, "G" + g.ToString(Inv), new PwUuid(true), T0);
            allGroups.Add(grp);
            for (var i = 0; i < perGroup; i++)
            {
                var e = AddEntry(grp, null, "E" + i.ToString(Inv), T0);
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw"));
                e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "user"));
                e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.invalid/"));
                all.Add(e);
            }
        }
        // 20 ruled nodes, three versions each: 60 rule entries.
        for (var n = 0; n < 20; n++)
        {
            var node = n < 10 ? allGroups[n].Uuid : all[n * 7].Uuid;
            string last = null;
            for (var k = 0; k < 3; k++)
                last = AddVersion(db, node, k == 0 ? R0 : k == 1 ? R1 : R2, last == null ? null : new[] { last }, T0);
        }
        for (var i = 0; i < 5000; i++) db.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), T0));

        var total = all.Count + (int)store.Entries.UCount;
        Out.WriteLine();
        Out.WriteLine("== Cost, in memory, KeePass " + PwDefs.VersionString + ": " + total.ToString(Inv) + " entries in " + (groups + 1).ToString(Inv)
            + " groups, 60 rule entries for 20 nodes, 5000 deletion records ==");

        var rounds = 50;
        var found = 0;
        var sw = Stopwatch.StartNew();
        for (var r = 0; r < rounds; r++) Walk(root, e => { if (Marked(e.Uuid)) found++; });
        sw.Stop();
        Out.WriteLine("  every entry's identifier tested for the mark: " + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.000", Inv) + " ms (" + (found / rounds).ToString(Inv) + " found)");

        found = 0;
        sw.Restart();
        for (var r = 0; r < rounds; r++) Walk(root, e => { if (Marked(e.Uuid) || e.Strings.Exists(KindField)) found++; });
        sw.Stop();
        Out.WriteLine("  the same, and every unmarked entry asked for a KPG.Kind string: " + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.000", Inv) + " ms");

        found = 0;
        sw.Restart();
        for (var r = 0; r < rounds; r++) foreach (var d in db.DeletedObjects) if (Marked(d.Uuid)) found++;
        sw.Stop();
        Out.WriteLine("  5000 deletion records tested for the mark: " + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.000", Inv) + " ms");

        View v = null;
        sw.Restart();
        for (var r = 0; r < rounds; r++) v = Read(db);
        sw.Stop();
        Out.WriteLine("  the whole reading (walk, string test, 60 digests, the index, the deletion records): " + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.000", Inv)
            + " ms; " + v.Versions.ToString(Inv) + " versions for " + v.ByNode.Count.ToString(Inv) + " nodes");

        // What a walk asks per node once the index stands: one lookup by the node's identifier.
        var byNode = new Dictionary<PwUuid, int>();
        foreach (var kv in v.ByNode) byNode[new PwUuid(MemUtil.HexStringToByteArray(kv.Key))] = kv.Value.Count;
        var hits = 0;
        sw.Restart();
        for (var r = 0; r < rounds; r++)
            foreach (var e in all) { int n; if (byNode.TryGetValue(e.Uuid, out n)) hits++; }
        sw.Stop();
        Out.WriteLine("  one index lookup for each of " + all.Count.ToString(Inv) + " entries: " + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.000", Inv) + " ms");

        // Today's read, for comparison: one lookup in each entry's own custom data.
        foreach (var e in all) e.CustomData.Set("other", "x");
        hits = 0;
        sw.Restart();
        for (var r = 0; r < rounds; r++)
            foreach (var e in all) { if (e.CustomData.Get("KPG") != null) hits++; }
        sw.Stop();
        Out.WriteLine("  today's read for comparison, CustomData.Get(\"KPG\") on each of them: " + (sw.Elapsed.TotalMilliseconds / rounds).ToString("0.000", Inv) + " ms");

        long chars = 0;
        var one = store.Entries.GetAt(1);
        foreach (var kv in one.Strings) chars += kv.Key.Length + kv.Value.ReadString().Length;
        Out.WriteLine("  one rule entry with a 50 character rule and one predecessor holds " + chars.ToString(Inv) + " characters of strings");
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
            var bytes = new UTF8Encoding(false).GetBytes(Pw + "\n" + Pw + "\n");
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.BaseStream.Flush();
            p.StandardInput.Close();
            p.WaitForExit();
            o.Wait();
            e.Wait();
            return p.ExitCode.ToString(Inv);
        }
    }

    static void RunXc(string version, string exe)
    {
        Out.WriteLine();
        Out.WriteLine("== KeePassXC " + version + ", keepassxc-cli merge -s <target> <source>, the target read back with KeePassLib " + PwDefs.VersionString + " ==");
        var versions = new SortedSet<string>();
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
                versions.Add(KdbxVersion(target));
                var read = Open(target);
                Out.WriteLine("  into " + into + " (exit " + exit + "): " + State(read));
                read.Close();
            }
        }
        Out.WriteLine();
        Out.WriteLine("KDBX versions of the targets after the merges: " + string.Join(", ", new List<string>(versions).ToArray()));
    }

    // What a plain save by KeePassXC leaves of the store: add one entry elsewhere, read back.
    static void RunXcSave(string version, string exe)
    {
        var dir = Path.Combine(Work, "xc" + version + "-save");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "base.kdbx");
        MakeBase(path, true);
        var before = Open(path);
        var was = State(before);
        var listWas = Listing(before);
        before.Close();
        var exit = Cli(exe, dir, "add", path, "W/xc-added");
        var read = Open(path);
        Out.WriteLine();
        Out.WriteLine("== KeePassXC " + version + ", a plain save (keepassxc-cli add, exit " + exit + "), file then KDBX " + KdbxVersion(path) + " ==");
        Out.WriteLine("  before: " + was);
        Out.WriteLine("  after:  " + State(read));
        var listNow = Listing(read);
        Out.WriteLine("  every rule entry's identifier, strings, protection flags and times unchanged: " + (listWas == listNow ? "yes" : "NO"));
        if (listWas != listNow) { Out.WriteLine("  was: " + listWas); Out.WriteLine("  now: " + listNow); }
        var store = Store(read);
        Out.WriteLine("  store group: searching " + (store.EnableSearching.HasValue ? store.EnableSearching.Value.ToString() : "inherit")
            + ", auto-type " + (store.EnableAutoType.HasValue ? store.EnableAutoType.Value.ToString() : "inherit") + ", notes kept " + (store.Notes == NotesLine ? "yes" : "NO"));
        read.Close();
    }

    static string Listing(PwDatabase db)
    {
        var rows = new List<string>();
        foreach (var e in Store(db).Entries)
        {
            var sb = new StringBuilder();
            sb.Append(Hex(e.Uuid)).Append(' ').Append(e.LastModificationTime.ToString("o", Inv)).Append(' ').Append(e.History.UCount.ToString(Inv));
            var keys = new List<string>(e.Strings.GetKeys());
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(' ').Append(k).Append(e.Strings.Get(k).IsProtected ? "[p]=" : "=").Append(e.Strings.ReadSafe(k));
            rows.Add(sb.ToString());
        }
        rows.Sort(StringComparer.Ordinal);
        return string.Join(" ;; ", rows.ToArray());
    }

    static int Main(string[] args)
    {
        Work = args[0];
        Directory.CreateDirectory(Work);
        var what = args[1];
        using (Out = new StreamWriter(args[2], false, new UTF8Encoding(false)))
        {
            Out.WriteLine("probe run " + DateTime.UtcNow.ToString("o", Inv) + ", KeePassLib " + PwDefs.VersionString + ", " + what);
            Out.WriteLine("store group " + Hex(StoreUuid) + ", mark " + MemUtil.ByteArrayToHexString(Mark));
            if (what == "keepass") { RunKeePass(); RunNewUuids(); RunDuplicates(); }
            else if (what == "cost") { RunCost(500, 20); RunCost(500, 100); }
            else if (what == "xc") { RunXcSave(args[3], args[4]); RunXc(args[3], args[4]); }
            else return 2;
        }
        return 0;
    }
}
