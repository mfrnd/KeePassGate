// Edited refusals: what a hand edit of one record of a write-once store, made in one copy of a
// database, does once the two copies are merged. `make` builds copies A and B of a database holding
// the store and edits one record in B as a person in KeePass's entry dialog would; `reads` dumps
// both copies as they are; `kp` merges them with KeePassLib's five methods and `xc` with
// `keepassxc-cli merge`, each in both directions, and both dump every result. A dump is one JSON
// line holding the whole file as text. Nothing here applies a reading rule: `rules.py`, one
// directory up, does that over the dumps.
//
// Console only: KeePassLib 2.62 loaded from KeePass.exe, `keepassxc-cli` started without a window.
// Every database, key and certificate is invented.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

static class Program
{
    const string Password = "probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string NotesLine = "KeePassGate data. Managed on the KPG tab in KeePass.";

    // B's edit is made at TB seconds after T0. A's own edit of the same entry's title, where a
    // scenario has one, falls 1800 seconds after it (B's edit older), 1800 before it (B's edit
    // newer), or in the same second.
    const int TB = 7200;

    static DateTime At(int seconds) { return T0.AddSeconds(seconds); }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 3 && args[0] == "make") { Make(args[1], args[2], Degree(args, 3)); return 0; }
            if (args.Length >= 2 && args[0] == "history") { ExtMake(args[1], Degree(args, 2)); return 0; }
            if (args.Length >= 3 && args[0] == "reads") { Reads(args[1], args[2]); return 0; }
            if (args.Length >= 4 && args[0] == "kp") { Kp(args[1], args[2], args[3], Degree(args, 4)); return 0; }
            if (args.Length >= 6 && args[0] == "xc") { Xc(args[1], args[2], args[3], args[4], int.Parse(args[5], CultureInfo.InvariantCulture), args.Skip(6).ToArray()); return 0; }
            if (args.Length >= 2 && args[0] == "dump") { var w = new World(); for (int i = 1; i < args.Length; i++) Console.WriteLine(Dump(w, args[i])); return 0; }
            if (args.Length >= 1 && args[0] == "scenarios") { foreach (var s in Scenarios()) Console.WriteLine(string.Join("\t", s)); return 0; }
        }
        catch (Exception x)
        {
            Console.Error.WriteLine(x.ToString());
            return 1;
        }
        Console.Error.WriteLine("usage: make <dir> <fast|slow> [degree] | history <dir> [degree] | reads <dir> <out.jsonl> | kp <dir> <work> <out.jsonl> [degree]"
            + " | xc <slowDir> <work> <out.jsonl> <times.tsv> <degree> <version>=<keepassxc-cli>... | dump <file>... | scenarios");
        return 2;
    }

    static int Degree(string[] args, int i) { return args.Length > i ? int.Parse(args[i], CultureInfo.InvariantCulture) : 1; }

    // ---- the world: every identifier and key, drawn in one fixed order from one seed ------------

    sealed class World
    {
        public readonly Dictionary<string, PwUuid> U = new Dictionary<string, PwUuid>();
        public readonly Dictionary<string, string> Label = new Dictionary<string, string>();
        public readonly byte[] Key0, Key1;
        public readonly string Doc0, Doc1, Fp0, Fp1;
        public readonly string Cert1, Cert2, Cert3;
        public readonly string ActRetire, ActReader, ActWriter, ActAuditor, ActLegacy, ActRemove, ActBin;

        public World()
        {
            var rng = new Random(20261116);
            foreach (var name in new[] {
                "g:root", "g:general", "g:store", "g:bin",
                "e:user", "e:user-deleted",
                "e:authority-a0", "e:authority-a1", "e:retired-a0",
                "e:client-c1", "e:client-c2", "e:client-c3", "e:revoked-c3",
                "e:added-reader", "e:added-writer", "e:added-auditor", "e:added-legacy",
                "e:removed-legacy", "e:bin-marker", "e:copy" })
            {
                var u = new PwUuid(Draw(rng, 16));
                U[name] = u;
                Label[u.ToHexString()] = name;
            }
            Key0 = Draw(rng, 32);
            Key1 = Draw(rng, 32);
            ActRetire = Hex(Draw(rng, 16));
            ActReader = Hex(Draw(rng, 16));
            ActWriter = Hex(Draw(rng, 16));
            ActAuditor = Hex(Draw(rng, 16));
            ActLegacy = Hex(Draw(rng, 16));
            ActRemove = Hex(Draw(rng, 16));
            ActBin = Hex(Draw(rng, 16));
            Doc0 = AuthorityDocument("a0", Key0, out Fp0);
            Doc1 = AuthorityDocument("a1", Key1, out Fp1);
            Cert1 = Sha256Hex("invented certificate c1");
            Cert2 = Sha256Hex("invented certificate c2");
            Cert3 = Sha256Hex("invented certificate c3");
            Label[Witness(Key0, "retired", Fp0).ToHexString()] = "w:retired-a0";
            Label[Witness(Key1, "revoked", Cert3).ToHexString()] = "w:revoked-c3";
        }

        static byte[] Draw(Random rng, int n) { var b = new byte[n]; rng.NextBytes(b); return b; }
    }

    // The authority's document, one protected string: a certificate, its private key, the host
    // certificate, the gate key and the store key. Invented text of about the real length. `fp` is
    // the SHA-256 of the certificate field, written beside it so that a reader of a dump, where the
    // long fields are cut short, still has it.
    static string AuthorityDocument(string name, byte[] storeKey, out string fp)
    {
        string certificate = Filler(1400, "certificate " + name);
        fp = Sha256Hex(certificate);
        return "{\"v\":\"1\",\"fp\":\"" + fp + "\",\"certificate\":\"" + certificate
            + "\",\"key\":\"" + Filler(1150, "key " + name) + "\",\"host\":\"" + Filler(260, "host " + name)
            + "\",\"gateKey\":\"" + Filler(44, "gate " + name) + "\",\"storeKey\":\"" + Hex(storeKey) + "\"}";
    }

    static string Filler(int n, string seed)
    {
        var sb = new StringBuilder();
        using (var sha = SHA256.Create())
        {
            for (int i = 0; sb.Length < n; i++) sb.Append(Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(seed + i))));
        }
        return sb.ToString(0, n);
    }

    // ---- the check and the witness ---------------------------------------------------------------

    // What the check covers: every string of the entry whose name begins `KPG.`, other than
    // `KPG.Mac`, in ordinal order of name, each as UTF-8 name, a NUL, UTF-8 value, a line feed. The
    // check is `HMACSHA256` under the store key over the SHA-256 of that text, in lower-case hex.
    static byte[] Canon(PwEntry e)
    {
        var names = e.Strings.Select(kv => kv.Key)
            .Where(k => k.StartsWith("KPG.", StringComparison.Ordinal) && k != "KPG.Mac")
            .OrderBy(k => k, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var k in names) sb.Append(k).Append('\0').Append(e.Strings.ReadSafe(k)).Append('\n');
        using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    static string Mac(byte[] key, PwEntry e)
    {
        using (var h = new HMACSHA256(key)) return Hex(h.ComputeHash(Canon(e)));
    }

    // A witness: a deletion record that names no entry, its identifier the first 16 bytes of
    // `HMACSHA256` under the store key over `KPG-witness/1/<kind>/<target>`.
    static PwUuid Witness(byte[] key, string kind, string target)
    {
        using (var h = new HMACSHA256(key))
        {
            var d = h.ComputeHash(Encoding.UTF8.GetBytes("KPG-witness/1/" + kind + "/" + target));
            var b = new byte[16];
            Array.Copy(d, b, 16);
            return new PwUuid(b);
        }
    }

    // ---- building ------------------------------------------------------------------------------

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword(Password)); return k; }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    // Fast: the derivation `store-entries` used. Slow: the one `ground-keepassxc` used, so that the
    // two loads of a `keepassxc-cli merge` fall in different seconds.
    static KdfParameters Kdf(string speed)
    {
        bool slow = speed == "slow";
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, slow ? 200UL : 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, (slow ? 65536UL : 1024UL) * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, slow ? 2U : 1U);
        return a;
    }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static PwGroup Group(World w, string label, string name, PwIcon icon)
    {
        var g = new PwGroup(false, false);
        g.Uuid = w.U[label];
        g.Name = name;
        g.IconId = icon;
        Stamp(g, T0);
        return g;
    }

    static string J(string s) { return "\"" + s + "\""; }

    static PwEntry Record(World w, string label, string title, string kind, string data, bool protect, byte[] key, int t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = w.U[label];
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        e.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, ""));
        e.Strings.Set(PwDefs.UrlField, new ProtectedString(false, ""));
        e.Strings.Set(PwDefs.NotesField, new ProtectedString(false, NotesLine));
        e.Strings.Set("KPG.Kind", new ProtectedString(false, kind));
        e.Strings.Set("KPG.Data", new ProtectedString(protect, data));
        e.Strings.Set("KPG.Mac", new ProtectedString(false, Mac(key, e)));
        Stamp(e, At(t));
        return e;
    }

    static string Client(World w, string subject, string cert, string profiles)
    {
        return "{\"v\":\"1\",\"certificate\":" + J(cert) + ",\"subject\":" + J(subject) + ",\"authority\":" + J(w.Fp1)
            + ",\"profiles\":[" + profiles + "],\"notAfter\":\"2027-01-01T00:00:00Z\"}";
    }

    static string Added(string name, string act) { return "{\"v\":\"1\",\"name\":" + J(name) + ",\"act\":" + J(act) + ",\"seen\":[]}"; }

    static PwDatabase BuildBase(World w, string path)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = Kdf("fast");
        db.Name = "invented";
        db.NameChanged = T0; db.DescriptionChanged = T0; db.DefaultUserNameChanged = T0;
        db.MasterKeyChanged = T0; db.EntryTemplatesGroupChanged = T0; db.SettingsChanged = T0;
        db.RootGroup.Uuid = w.U["g:root"];
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);

        var general = Group(w, "g:general", "General", PwIcon.Folder);
        db.RootGroup.AddGroup(general, true);
        var store = Group(w, "g:store", "KeePassGate", PwIcon.TerminalEncrypted);
        store.Notes = NotesLine;
        store.EnableSearching = false;
        store.EnableAutoType = false;
        db.RootGroup.AddGroup(store, true);
        var bin = Group(w, "g:bin", "Recycle Bin", PwIcon.TrashBin);
        bin.EnableSearching = false;
        bin.EnableAutoType = false;
        db.RootGroup.AddGroup(bin, true);
        db.RecycleBinEnabled = true;
        db.RecycleBinUuid = bin.Uuid;
        db.RecycleBinChanged = T0;

        var user = new PwEntry(false, false);
        user.Uuid = w.U["e:user"];
        user.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "user entry"));
        user.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "invented"));
        user.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password"));
        Stamp(user, T0);
        general.AddEntry(user, true);

        var deleted = new PwEntry(false, false);
        deleted.Uuid = w.U["e:user-deleted"];
        deleted.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "deleted user entry"));
        deleted.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password-2"));
        Stamp(deleted, T0);
        bin.AddEntry(deleted, true);
        deleted.PreviousParentGroup = general.Uuid;
        deleted.LocationChanged = At(840);

        string binHex = bin.Uuid.ToHexString();
        store.AddEntry(Record(w, "e:authority-a0", "KPG authority a0", "authority", w.Doc0, true, w.Key0, 60), true);
        store.AddEntry(Record(w, "e:authority-a1", "KPG authority a1", "authority", w.Doc1, true, w.Key1, 120), true);
        store.AddEntry(Record(w, "e:retired-a0", "KPG retired a0", "retired",
            "{\"v\":\"1\",\"authority\":" + J(w.Fp0) + ",\"act\":" + J(w.ActRetire) + "}", false, w.Key0, 120), true);
        store.AddEntry(Record(w, "e:client-c3", "KPG client c3", "client", Client(w, "c3", w.Cert3, "\"auditor\""), false, w.Key1, 180), true);
        store.AddEntry(Record(w, "e:client-c1", "KPG client c1", "client", Client(w, "c1", w.Cert1, "\"reader\",\"legacy\""), false, w.Key1, 240), true);
        var c2 = Record(w, "e:client-c2", "KPG client c2", "client", Client(w, "c2", w.Cert2, "\"writer\""), false, w.Key1, 300);
        bin.AddEntry(c2, true);
        c2.PreviousParentGroup = store.Uuid;
        c2.LocationChanged = At(780);
        store.AddEntry(Record(w, "e:revoked-c3", "KPG revoked c3", "revoked",
            "{\"v\":\"1\",\"certificate\":" + J(w.Cert3) + ",\"authority\":" + J(w.Fp1) + ",\"notAfter\":\"2027-01-01T00:00:00Z\"}", false, w.Key1, 360), true);
        store.AddEntry(Record(w, "e:added-reader", "KPG profile added reader", "profile-added", Added("reader", w.ActReader), false, w.Key1, 420), true);
        store.AddEntry(Record(w, "e:added-writer", "KPG profile added writer", "profile-added", Added("writer", w.ActWriter), false, w.Key1, 480), true);
        store.AddEntry(Record(w, "e:added-auditor", "KPG profile added auditor", "profile-added", Added("auditor", w.ActAuditor), false, w.Key1, 540), true);
        store.AddEntry(Record(w, "e:added-legacy", "KPG profile added legacy", "profile-added", Added("legacy", w.ActLegacy), false, w.Key1, 600), true);
        store.AddEntry(Record(w, "e:removed-legacy", "KPG profile removed legacy", "profile-removed",
            "{\"v\":\"1\",\"name\":\"legacy\",\"act\":" + J(w.ActRemove) + "}", false, w.Key1, 660), true);
        store.AddEntry(Record(w, "e:bin-marker", "KPG recycle bin", "recycle-bin",
            "{\"v\":\"1\",\"group\":" + J(binHex) + ",\"act\":" + J(w.ActBin) + "}", false, w.Key1, 720), true);

        db.DeletedObjects.Add(new PwDeletedObject(Witness(w.Key0, "retired", w.Fp0), At(120)));
        db.DeletedObjects.Add(new PwDeletedObject(Witness(w.Key1, "revoked", w.Cert3), At(360)));
        return db;
    }

    // ---- the scenarios -----------------------------------------------------------------------

    static readonly string[] Targets = { "retirement", "revocation", "removal", "marker", "admission" };
    static readonly string[] Edits = { "character", "rename", "delete-string", "empty", "title", "move", "recycle", "delete-record", "delete-no-record", "duplicate" };
    static readonly string[] Times = { "untouched", "older", "newer", "same" };

    // name, target, edit, time; the name is the other three joined by dots. `untouched`: A as it was
    // made. `older`, `newer`, `same`: A edits the same entry's title, and B's edit is older than A's,
    // newer, or in the same second. `title-only`: A's title edit alone, with no edit in B.
    static IEnumerable<string[]> Scenarios()
    {
        yield return new[] { "none.none.untouched", "none", "none", "untouched" };
        foreach (var target in Targets)
        {
            yield return new[] { target + ".none.title-only", target, "none", "title-only" };
            foreach (var edit in Edits)
                foreach (var time in Times)
                    yield return new[] { target + "." + edit + "." + time, target, edit, time };
        }
    }

    static string TargetLabel(string target)
    {
        switch (target)
        {
            case "retirement": return "e:retired-a0";
            case "revocation": return "e:revoked-c3";
            case "removal": return "e:removed-legacy";
            case "marker": return "e:bin-marker";
            case "admission": return "e:client-c1";
        }
        return null;
    }

    static string TargetField(string target)
    {
        switch (target)
        {
            case "retirement": return "authority";
            case "revocation": return "certificate";
            case "removal": return "name";
            case "marker": return "group";
            case "admission": return "certificate";
        }
        return null;
    }

    // One character changed: the last character of the value of the field that names the record's
    // target, to the next hex digit or the next letter.
    static string ChangeOne(string data, string field)
    {
        var m = Regex.Match(data, "\"" + field + "\":\"([^\"]*)\"");
        if (!m.Success || m.Groups[1].Length == 0) throw new InvalidOperationException("no " + field + " in " + data);
        int i = m.Groups[1].Index + m.Groups[1].Length - 1;
        char c = data[i], n;
        if (c >= '0' && c <= '8') n = (char)(c + 1);
        else if (c == '9') n = '0';
        else if ((c >= 'a' && c <= 'e') || (c >= 'A' && c <= 'E')) n = (char)(c + 1);
        else if (c == 'f') n = 'a';
        else if (c == 'F') n = 'A';
        else if (c >= 'g' && c < 'z') n = (char)(c + 1);
        else n = 'y';
        return data.Substring(0, i) + n + data.Substring(i + 1);
    }

    // An edit in KeePass's entry dialog: the version before it goes into history, then the change,
    // then the modification time moves.
    static void Dialog(PwDatabase db, PwEntry e, int t, Action<PwEntry> change, bool backup = true)
    {
        if (backup) e.CreateBackup(db);
        change(e);
        e.LastModificationTime = At(t);
        e.LastAccessTime = At(t);
    }

    // A move to another group, as KeePass's drag, Move To Group and Delete into the recycle bin do it,
    // and as `crdt` and `grant-only` stand for it: the location time moves and the previous parent is
    // set; the modification time and the history do not change.
    static void Move(PwEntry e, PwGroup to, int t)
    {
        var from = e.ParentGroup;
        from.Entries.Remove(e);
        to.AddEntry(e, true, true);
        e.PreviousParentGroup = from.Uuid;
        e.LocationChanged = At(t);
    }

    static void EditTitle(PwDatabase db, PwEntry e, int t, string suffix)
    {
        Dialog(db, e, t, x => x.Strings.Set(PwDefs.TitleField, new ProtectedString(false, x.Strings.ReadSafe(PwDefs.TitleField) + suffix)));
    }

    static void EditB(World w, PwDatabase db, string target, string edit, int t, bool backup = true)
    {
        if (edit == "none") return;
        var e = db.RootGroup.FindEntry(w.U[TargetLabel(target)], true);
        string field = TargetField(target);
        switch (edit)
        {
            case "character":
                Dialog(db, e, t, x => x.Strings.Set("KPG.Data", new ProtectedString(x.Strings.Get("KPG.Data").IsProtected, ChangeOne(x.Strings.ReadSafe("KPG.Data"), field))), backup);
                break;
            case "rename":
                Dialog(db, e, t, x => { var v = x.Strings.Get("KPG.Data"); x.Strings.Remove("KPG.Data"); x.Strings.Set("KPG.Datum", v); }, backup);
                break;
            case "delete-string":
                Dialog(db, e, t, x => x.Strings.Remove("KPG.Data"), backup);
                break;
            case "empty":
                Dialog(db, e, t, x =>
                {
                    foreach (var k in x.Strings.Select(kv => kv.Key).ToList())
                        x.Strings.Set(k, new ProtectedString(x.Strings.Get(k).IsProtected, ""));
                }, backup);
                break;
            case "title":
                EditTitle(db, e, t, " (seen)");
                break;
            case "move":
                Move(e, db.RootGroup.FindGroup(w.U["g:general"], true), t);
                break;
            case "recycle":
                Move(e, db.RootGroup.FindGroup(w.U["g:bin"], true), t);
                break;
            case "delete-record":
                e.ParentGroup.Entries.Remove(e);
                db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, At(t)));
                break;
            case "delete-no-record":
                e.ParentGroup.Entries.Remove(e);
                break;
            case "duplicate":
                // KeePass's Duplicate Entry with its default of appending "- Copy" to the title: a new
                // identifier, every string, the history; stamped as made at the edit's time. Then the
                // original edited as `character` edits it.
                var d = e.CloneDeep();
                d.SetUuid(w.U["e:copy"], true);
                d.Strings.Set(PwDefs.TitleField, new ProtectedString(false, d.Strings.ReadSafe(PwDefs.TitleField) + " - Copy"));
                Stamp(d, At(t));
                e.ParentGroup.AddEntry(d, true, false);
                Dialog(db, e, t, x => x.Strings.Set("KPG.Data", new ProtectedString(x.Strings.Get("KPG.Data").IsProtected, ChangeOne(x.Strings.ReadSafe("KPG.Data"), field))));
                break;
            default:
                throw new ArgumentException(edit);
        }
    }

    static int? TimeOfA(string time)
    {
        switch (time)
        {
            case "older": return TB + 1800;
            case "newer": return TB - 1800;
            case "same": return TB;
            case "title-only": return TB;
        }
        return null;
    }

    static void Make(string dir, string speed, int degree)
    {
        var w = new World();
        Directory.CreateDirectory(dir);
        var list = Scenarios().ToList();
        File.WriteAllLines(Path.Combine(dir, "scenarios.tsv"), list.Select(s => string.Join("\t", s)));
        Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = degree }, s =>
        {
            string sd = Path.Combine(dir, s[0]);
            Directory.CreateDirectory(sd);
            foreach (var side in new[] { "A", "B" })
            {
                string path = Path.Combine(sd, side + ".kdbx");
                var db = BuildBase(w, path);
                int? ta = TimeOfA(s[3]);
                if (side == "A" && ta.HasValue) EditTitle(db, db.RootGroup.FindEntry(w.U[TargetLabel(s[1])], true), ta.Value, " (A)");
                if (side == "B") EditB(w, db, s[1], s[2], TB);
                db.KdfParameters = Kdf(speed);
                db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
                db.Close();
            }
        });
    }

    static List<string[]> ReadScenarios(string dir)
    {
        return File.ReadAllLines(Path.Combine(dir, "scenarios.tsv")).Select(l => l.Split('\t')).ToList();
    }

    static void WriteLines(string path, IEnumerable<KeyValuePair<string, string>> cells)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllLines(path, cells.OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => "{\"cell\":" + Q(c.Key) + ",\"file\":" + c.Value + "}"), new UTF8Encoding(false));
    }

    // ---- the reads and the merges ------------------------------------------------------------

    static void Reads(string dir, string output)
    {
        var w = new World();
        var cells = new List<KeyValuePair<string, string>>();
        foreach (var s in ReadScenarios(dir))
            foreach (var side in new[] { "A", "B" })
                cells.Add(new KeyValuePair<string, string>(s[0] + "|" + side, Dump(w, Path.Combine(dir, s[0], side + ".kdbx"))));
        WriteLines(output, cells);
    }

    static readonly string[] Methods = { "Synchronize", "OverwriteIfNewer", "OverwriteExisting", "KeepExisting", "CreateNewUuids" };

    static void Kp(string dir, string work, string output, int degree)
    {
        var w = new World();
        var jobs = new List<string[]>();
        foreach (var s in ReadScenarios(dir))
            foreach (var m in Methods)
                foreach (var into in new[] { "A", "B" })
                    jobs.Add(new[] { s[0], m, into, s.Length > 4 ? s[4] : "" });
        var cells = new ConcurrentBag<KeyValuePair<string, string>>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = degree }, j =>
        {
            string from = j[2] == "A" ? "B" : "A";
            string cell = j[0] + "|kp-" + j[1] + "-into" + j[2];
            string od = Path.Combine(work, j[0]);
            Directory.CreateDirectory(od);
            string result = Path.Combine(od, "kp-" + j[1] + "-into" + j[2] + ".kdbx");
            var target = Open(Path.Combine(dir, j[0], j[2] + ".kdbx"));
            var source = Open(Path.Combine(dir, j[0], from + ".kdbx"));
            target.MergeIn(source, (PwMergeMethod)Enum.Parse(typeof(PwMergeMethod), j[1]), null);
            AfterMerge(target, j[3]);
            target.SaveAs(IOConnectionInfo.FromPath(result), false, null);
            target.Close();
            source.Close();
            cells.Add(new KeyValuePair<string, string>(cell, Dump(w, result)));
        });
        WriteLines(output, cells);
    }

    sealed class Run { public int Exit; public double Seconds; public string Text; }

    static Run Cli(string cli, string workDir, params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = cli,
            Arguments = string.Join(" ", arguments.Select(a => "\"" + a + "\"")),
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var sw = Stopwatch.StartNew();
        using (var p = Process.Start(info))
        {
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            p.StandardInput.WriteLine(Password);
            p.StandardInput.WriteLine(Password);
            p.StandardInput.Close();
            p.WaitForExit();
            sw.Stop();
            return new Run { Exit = p.ExitCode, Seconds = sw.Elapsed.TotalSeconds, Text = o.Result + e.Result };
        }
    }

    static string FileSha(string path)
    {
        using (var sha = SHA256.Create()) using (var s = File.OpenRead(path)) return Hex(sha.ComputeHash(s));
    }

    static void Xc(string slowDir, string work, string output, string timesPath, int degree, string[] programs)
    {
        var w = new World();
        var cli = programs.Select(c => c.Split(new[] { '=' }, 2)).ToDictionary(p => p[0], p => p[1]);
        var times = new List<string>();

        // One load per release, alone, on an input file: what a single load of the slow derivation
        // takes here, which is the gap between the two loads of a merge.
        var first = ReadScenarios(slowDir)[0][0];
        foreach (var v in cli.Keys)
        {
            var r = Cli(cli[v], Path.Combine(slowDir, first), "ls", Path.Combine(slowDir, first, "A.kdbx"));
            times.Add("load\t" + v + "\t" + r.Exit + "\t" + r.Seconds.ToString("0.00", CultureInfo.InvariantCulture));
        }

        var jobs = new List<string[]>();
        foreach (var s in ReadScenarios(slowDir))
            foreach (var v in cli.Keys)
                foreach (var into in new[] { "A", "B" })
                    jobs.Add(new[] { s[0], v, into, s.Length > 4 ? s[4] : "" });
        var cache = new ConcurrentDictionary<string, Lazy<string>>();
        var cells = new ConcurrentBag<KeyValuePair<string, string>>();
        var merged = new ConcurrentBag<string>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = degree }, j =>
        {
            string from = j[2] == "A" ? "B" : "A";
            string name = "xc-" + j[1] + "-into" + j[2];
            string cd = Path.Combine(work, j[0], name);
            Directory.CreateDirectory(cd);
            string target = Path.Combine(cd, "target.kdbx"), source = Path.Combine(cd, "source.kdbx");
            File.Copy(Path.Combine(slowDir, j[0], j[2] + ".kdbx"), target, true);
            File.Copy(Path.Combine(slowDir, j[0], from + ".kdbx"), source, true);
            string before = FileSha(target);
            var r = Cli(cli[j[1]], cd, "merge", "-s", target, source);
            File.WriteAllText(Path.Combine(cd, "merge.txt"), r.Text, new UTF8Encoding(false));
            string merged1 = FileSha(target);
            if (j[3].Length > 0)
            {
                var db = Open(target);
                AfterMerge(db, j[3]);
                db.Save(null);
                db.Close();
            }
            string after = FileSha(target);
            // The same bytes give the same dump, so a file is opened once whatever number of cells
            // left it unchanged.
            string dump = cache.GetOrAdd(after, h => new Lazy<string>(() => Dump(w, target))).Value;
            cells.Add(new KeyValuePair<string, string>(j[0] + "|" + name, dump));
            merged.Add("merge\t" + j[0] + "\t" + j[1] + "\tinto" + j[2] + "\t" + r.Exit + "\t" + (before == merged1 ? "unchanged" : "written")
                + "\t" + r.Seconds.ToString("0.00", CultureInfo.InvariantCulture));
        });
        times.AddRange(merged.OrderBy(x => x, StringComparer.Ordinal));
        File.WriteAllLines(timesPath, times, new UTF8Encoding(false));
        WriteLines(output, cells);
    }

    // ---- the extension: history ---------------------------------------------------------------
    //
    // Five parts, every scenario on each of the five records, B's edits at TB and A untouched unless
    // the part names A. 1: the history limits, B's one-character edit then N-1 title edits, each in
    // the dialog; 2: Database Maintenance's deletion of old history items in B before the merge, or
    // in the result after it; 3: the payload edits with no history item, by KeePassLib with the
    // backup left out, or by pykeepass (`scripts\pk_edit.py`, which this program leaves B unedited
    // for); 4: B's dialog edit and A's edit with no history, of another character of the target, at
    // three times; 5: B's dialog edit, then its history item edited or removed by a script.

    static readonly string[] Limits = { "default", "one", "none", "unlimited", "byte" };
    static readonly int[] EditCounts = { 1, 2, 3, 10, 11, 12 };

    static IEnumerable<string[]> ExtScenarios()
    {
        foreach (var target in Targets)
        {
            foreach (var l in Limits)
                foreach (var n in EditCounts)
                    yield return Ext(target, "limits-" + l + "-" + n.ToString(CultureInfo.InvariantCulture), "untouched", "");
            yield return Ext(target, "maintenance-before-30", "untouched", "");
            yield return Ext(target, "maintenance-before-365", "untouched", "");
            yield return Ext(target, "maintenance-after-30", "untouched", "maintenance-30");
            foreach (var e in new[] { "character", "rename", "delete-string", "empty" })
                yield return Ext(target, "library-" + e, "untouched", "");
            foreach (var e in new[] { "character", "delete-string" })
                yield return Ext(target, "pykeepass-" + e, "untouched", "");
            foreach (var time in new[] { "older", "newer", "same" })
                yield return Ext(target, "both-character", time, "");
            yield return Ext(target, "tamper-edit", "untouched", "");
            yield return Ext(target, "tamper-remove", "untouched", "");
        }
    }

    static string[] Ext(string target, string edit, string time, string after)
    {
        return new[] { target + "." + edit + "." + time, target, edit, time, after };
    }

    // The database's history limits: KeePass's defaults (10 items, 6 MiB), a single item, no item,
    // none at all (-1 for both), and a single byte.
    static void SetLimits(PwDatabase db, string edit)
    {
        db.HistoryMaxItems = 10;
        db.HistoryMaxSize = 6L * 1024 * 1024;
        if (!edit.StartsWith("limits-", StringComparison.Ordinal)) return;
        string l = edit.Split('-')[1];
        if (l == "one") db.HistoryMaxItems = 1;
        if (l == "none") db.HistoryMaxItems = 0;
        if (l == "unlimited") { db.HistoryMaxItems = -1; db.HistoryMaxSize = -1; }
        if (l == "byte") db.HistoryMaxSize = 1;
    }

    // KeePass's Database Maintenance, "Delete history entries older than (days)", as the desktop
    // measurement saw the window do it: every history item whose modification time lies that many
    // days or more before now goes, with no question.
    static void Maintain(PwDatabase db, int days)
    {
        var now = DateTime.UtcNow;
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            for (uint i = 0; i < e.History.UCount;)
            {
                if ((now - e.History.GetAt(i).LastModificationTime.ToUniversalTime()).TotalDays >= days) e.History.RemoveAt(i);
                else i++;
            }
        }
    }

    static void AfterMerge(PwDatabase db, string after)
    {
        if (after == "maintenance-30") Maintain(db, 30);
    }

    // The first character of the target's value changed, as `ChangeOne` changes the last.
    static string ChangeFirst(string data, string field)
    {
        var m = Regex.Match(data, "\"" + field + "\":\"([^\"]*)\"");
        int i = m.Groups[1].Index;
        char c = data[i];
        char n = c == 'z' || c == 'Z' || c == '9' || c == 'f' || c == 'F' ? (char)(c - 1) : (char)(c + 1);
        return data.Substring(0, i) + n + data.Substring(i + 1);
    }

    static void ExtMake(string dir, int degree)
    {
        var w = new World();
        Directory.CreateDirectory(dir);
        var list = ExtScenarios().ToList();
        File.WriteAllLines(Path.Combine(dir, "scenarios.tsv"), list.Select(s => string.Join("\t", s)));
        File.WriteAllLines(Path.Combine(dir, "pykeepass.tsv"), list.Where(s => s[2].StartsWith("pykeepass-", StringComparison.Ordinal))
            .Select(s => s[0] + "\t" + w.U[TargetLabel(s[1])].ToHexString() + "\t" + TargetField(s[1]) + "\t" + s[2].Substring(10)));
        Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = degree }, s =>
        {
            string sd = Path.Combine(dir, s[0]);
            Directory.CreateDirectory(sd);
            foreach (var side in new[] { "A", "B" })
            {
                string path = Path.Combine(sd, side + ".kdbx");
                var db = BuildBase(w, path);
                SetLimits(db, s[2]);
                var e = db.RootGroup.FindEntry(w.U[TargetLabel(s[1])], true);
                string field = TargetField(s[1]);
                if (side == "A" && s[2] == "both-character")
                    Dialog(db, e, TimeOfA(s[3]).Value, x => x.Strings.Set("KPG.Data", new ProtectedString(x.Strings.Get("KPG.Data").IsProtected, ChangeFirst(x.Strings.ReadSafe("KPG.Data"), field))), false);
                if (side == "B") ExtEditB(w, db, s, e, field);
                db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
                db.Close();
            }
        });
    }

    static void ExtEditB(World w, PwDatabase db, string[] s, PwEntry e, string field)
    {
        string target = s[1], edit = s[2];
        if (edit.StartsWith("limits-", StringComparison.Ordinal))
        {
            int n = int.Parse(edit.Substring(edit.LastIndexOf('-') + 1), CultureInfo.InvariantCulture);
            EditB(w, db, target, "character", TB);
            for (int k = 2; k <= n; k++) EditTitle(db, e, TB + 60 * (k - 1), " (" + k.ToString(CultureInfo.InvariantCulture) + ")");
        }
        else if (edit.StartsWith("maintenance-", StringComparison.Ordinal))
        {
            EditB(w, db, target, "character", TB);
            if (edit == "maintenance-before-30") Maintain(db, 30);
            if (edit == "maintenance-before-365") Maintain(db, 365);
        }
        else if (edit.StartsWith("library-", StringComparison.Ordinal)) EditB(w, db, target, edit.Substring(8), TB, false);
        else if (edit == "both-character") EditB(w, db, target, "character", TB);
        else if (edit == "tamper-edit")
        {
            EditB(w, db, target, "character", TB);
            var h = e.History.GetAt(0);
            h.Strings.Set("KPG.Data", new ProtectedString(h.Strings.Get("KPG.Data").IsProtected, ChangeFirst(h.Strings.ReadSafe("KPG.Data"), field)));
        }
        else if (edit == "tamper-remove")
        {
            EditB(w, db, target, "character", TB);
            e.History.RemoveAt(0);
        }
    }

    // ---- the dump ------------------------------------------------------------------------------

    static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static string Tm(DateTime t)
    {
        var d = t.ToUniversalTime() - T0;
        return d.TotalSeconds >= 0 && d.TotalDays < 1 ? "+" + ((long)d.TotalSeconds).ToString(CultureInfo.InvariantCulture) : "clock";
    }

    static readonly Regex LongField = new Regex("\"([^\"\\\\]{101,})\"");

    // A value longer than 300 characters is cut short in the dump: inside a JSON text every string
    // longer than 100 characters, otherwise the whole value, becomes `<len:N sha:XXXXXX>`. Only the
    // authority's document is that long. The check is computed over the full value, as `canon`.
    static string Short(string value)
    {
        if (value.Length <= 300) return value;
        if (value.StartsWith("{", StringComparison.Ordinal))
            return LongField.Replace(value, m => "\"<len:" + m.Groups[1].Length + " sha:" + Sha256Hex(m.Groups[1].Value).Substring(0, 6) + ">\"");
        return "<len:" + value.Length + " sha:" + Sha256Hex(value).Substring(0, 6) + ">";
    }

    static readonly HashSet<string> Standard = new HashSet<string> { PwDefs.TitleField, PwDefs.UserNameField, PwDefs.PasswordField, PwDefs.UrlField, PwDefs.NotesField };

    static string Dump(World w, string file)
    {
        var db = Open(file);
        try
        {
            var labels = new Dictionary<string, string>(w.Label);
            int fresh = 0;
            Func<PwUuid, string> label = u =>
            {
                string h = u.ToHexString(), l;
                if (!labels.TryGetValue(h, out l)) { l = "new:" + (++fresh); labels[h] = l; }
                return l;
            };
            var groups = new List<string>();
            var entries = new List<string>();
            Action<PwGroup> walk = null;
            walk = g =>
            {
                // A group's identifier in hex only where the world drew it: a group Create new IDs
                // made has a random one, which would make two runs differ for nothing.
                string gl = label(g.Uuid);
                groups.Add("[" + Q(gl) + "," + Q(g.Name) + "," + (g.ParentGroup == null ? "null" : Q(label(g.ParentGroup.Uuid))) + ","
                    + (w.Label.ContainsKey(g.Uuid.ToHexString()) ? Q(g.Uuid.ToHexString()) : "null") + "]");
                foreach (var e in g.Entries)
                {
                    string el = label(e.Uuid);
                    entries.Add(Version(e, el, gl, "cur", Tm(e.LocationChanged)));
                    int i = 0;
                    foreach (var h in e.History) entries.Add(Version(h, el, gl, "h" + (i++), null));
                }
                foreach (var c in g.Groups) walk(c);
            };
            walk(db.RootGroup);
            var deleted = db.DeletedObjects.Select(d =>
            {
                string h = d.Uuid.ToHexString(), l;
                return "[" + Q(h) + "," + (labels.TryGetValue(h, out l) ? Q(l) : "null") + "," + Q(Tm(d.DeletionTime)) + "]";
            }).OrderBy(x => x, StringComparer.Ordinal);
            string bin = db.RecycleBinEnabled && !db.RecycleBinUuid.Equals(PwUuid.Zero) ? Q(label(db.RecycleBinUuid)) : "null";
            return "{\"bin\":" + bin + ",\"groups\":[" + string.Join(",", groups) + "],\"deleted\":[" + string.Join(",", deleted)
                + "],\"entries\":[" + string.Join(",", entries) + "]}";
        }
        finally
        {
            db.Close();
        }
    }

    static string Version(PwEntry e, string label, string group, string slot, string location)
    {
        var s = e.Strings.Where(kv => !Standard.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => Q(kv.Key) + ":" + Q(Short(kv.Value.ReadString())));
        return "{\"u\":" + Q(label) + ",\"g\":" + Q(group) + ",\"slot\":" + Q(slot) + ",\"mod\":" + Q(Tm(e.LastModificationTime))
            + (location == null ? "" : ",\"loc\":" + Q(location))
            + ",\"title\":" + Q(e.Strings.ReadSafe(PwDefs.TitleField)) + ",\"s\":{" + string.Join(",", s) + "},\"canon\":" + Q(Hex(Canon(e))) + "}";
    }

    // ---- small things --------------------------------------------------------------------------

    static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }

    static string Sha256Hex(string text)
    {
        using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
}
