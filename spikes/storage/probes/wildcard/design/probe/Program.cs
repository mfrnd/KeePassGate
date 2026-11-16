// Probe for the storage option "wildcard": one sealed snapshot of everything, with deletion
// records as witnesses. Invented content only. Console only: KeePassLib 2.62 loaded from
// KeePass.exe, no window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Delegates;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

sealed class Rule
{
    public int N;
    public bool Gone;
    public SortedSet<string> Docs = new SortedSet<string>(StringComparer.Ordinal);
    public Rule Clone() { return new Rule { N = N, Gone = Gone, Docs = new SortedSet<string>(Docs, StringComparer.Ordinal) }; }
    public string Show() { return "n" + N + (Gone ? " gone" : Docs.Count == 0 ? " removed" : Docs.Count == 1 ? " " + Docs.First() : " SIBLINGS " + string.Join(" & ", Docs)); }
}

sealed class Answer
{
    public int N;
    public string Text;
    // forever refuses most, then a later moment, then an earlier one, then "lifted".
    public static int Compare(string a, string b)
    {
        return string.CompareOrdinal(Rank(a), Rank(b));
    }
    static string Rank(string a) { return a == "forever" ? "2" : a == "lifted" ? "0" : "1" + a; }
}

sealed class State
{
    public SortedDictionary<string, string> Authority = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public SortedSet<string> Retired = new SortedSet<string>(StringComparer.Ordinal);
    public SortedDictionary<string, string> Client = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public SortedDictionary<string, string> Revoked = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public SortedDictionary<string, Answer> Refused = new SortedDictionary<string, Answer>(StringComparer.Ordinal);
    public SortedDictionary<string, int> Profile = new SortedDictionary<string, int>(StringComparer.Ordinal);
    public SortedDictionary<string, Rule> Rules = new SortedDictionary<string, Rule>(StringComparer.Ordinal);
    public bool AuthorityConflict;

    public State Clone()
    {
        var s = new State();
        foreach (var kv in Authority) s.Authority[kv.Key] = kv.Value;
        foreach (var r in Retired) s.Retired.Add(r);
        foreach (var kv in Client) s.Client[kv.Key] = kv.Value;
        foreach (var kv in Revoked) s.Revoked[kv.Key] = kv.Value;
        foreach (var kv in Refused) s.Refused[kv.Key] = new Answer { N = kv.Value.N, Text = kv.Value.Text };
        foreach (var kv in Profile) s.Profile[kv.Key] = kv.Value;
        foreach (var kv in Rules) s.Rules[kv.Key] = kv.Value.Clone();
        s.AuthorityConflict = AuthorityConflict;
        return s;
    }

    // The join. Commutative, associative and idempotent by construction: unions, maxima, and for a
    // rule at equal generations the union of the documents (both then have to allow).
    public static State Join(State a, State b)
    {
        var s = a.Clone();
        foreach (var kv in b.Authority)
        {
            string have;
            if (s.Authority.TryGetValue(kv.Key, out have) && have != kv.Value) s.AuthorityConflict = true;
            else s.Authority[kv.Key] = kv.Value;
        }
        s.AuthorityConflict |= b.AuthorityConflict;
        foreach (var r in b.Retired) s.Retired.Add(r);
        foreach (var kv in b.Client) if (!s.Client.ContainsKey(kv.Key)) s.Client[kv.Key] = kv.Value;
        foreach (var kv in b.Revoked) if (!s.Revoked.ContainsKey(kv.Key)) s.Revoked[kv.Key] = kv.Value;
        foreach (var kv in b.Refused)
        {
            Answer have;
            if (!s.Refused.TryGetValue(kv.Key, out have) || kv.Value.N > have.N
                || (kv.Value.N == have.N && Answer.Compare(kv.Value.Text, have.Text) > 0))
                s.Refused[kv.Key] = new Answer { N = kv.Value.N, Text = kv.Value.Text };
        }
        foreach (var kv in b.Profile)
        {
            int have;
            if (!s.Profile.TryGetValue(kv.Key, out have) || kv.Value > have) s.Profile[kv.Key] = kv.Value;
        }
        foreach (var kv in b.Rules)
        {
            Rule have;
            if (!s.Rules.TryGetValue(kv.Key, out have) || kv.Value.N > have.N) s.Rules[kv.Key] = kv.Value.Clone();
            else if (kv.Value.N == have.N)
            {
                have.Gone |= kv.Value.Gone;
                foreach (var d in kv.Value.Docs) have.Docs.Add(d);
                if (have.Gone) have.Docs.Clear();
            }
        }
        return s;
    }

    public string Digest()
    {
        return Program.Sha(Program.Body(new Snapshot { Id = PwUuid.Zero, Seq = 0, Key = new byte[0], State = this }));
    }

    public string Show()
    {
        var sb = new StringBuilder();
        var serving = Authority.Keys.Where(k => !Retired.Contains(k)).ToList();
        sb.Append("authorities=[" + string.Join(",", Authority.Keys.Select(k => k + (Retired.Contains(k) ? "(retired)" : ""))) + "]");
        sb.Append(" retired=[" + string.Join(",", Retired) + "]");
        sb.Append(" clients=[" + string.Join(",", Client.Keys.Select(k => k + (Revoked.ContainsKey(k) ? "(revoked)" : ""))) + "]");
        sb.Append(" revoked=[" + string.Join(",", Revoked.Keys) + "]");
        sb.Append(" refused=[" + string.Join(",", Refused.Select(kv => kv.Key + ":n" + kv.Value.N + ":" + kv.Value.Text)) + "]");
        sb.Append(" profiles=[" + string.Join(",", Profile.Select(kv => kv.Key + ":" + kv.Value + (kv.Value % 2 == 1 ? "(on)" : "(off)"))) + "]");
        sb.Append(" rules=[" + string.Join("; ", Rules.Select(kv => Program.NodeName(kv.Key) + " " + kv.Value.Show())) + "]");
        if (AuthorityConflict) sb.Append(" AUTHORITY-CONFLICT");
        return sb.ToString();
    }
}

sealed class Snapshot
{
    public PwUuid Id;
    public int Seq;
    public byte[] Key;
    public List<string> Over = new List<string>();   // hex of the snapshots this one's line replaced, newest last
    public State State = new State();
}

static class Program
{
    const string Password = "probe-only";
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public const string Field = "KPG.Store";
    const string Title = "KeePassGate store";
    const string GroupNotes = "KeePassGate keeps its data here. Manage it on the KPG tab in KeePass; do not edit or delete.";
    const int OverMax = 32;
    static readonly byte[] SnapMagic = { 0x4B, 0x50, 0x47, 0x53 };   // "KPGS", a snapshot
    static readonly byte[] RevMagic = { 0x4B, 0x50, 0x47, 0x52 };    // "KPGR", a revocation witness
    static readonly byte[] RetMagic = { 0x4B, 0x50, 0x47, 0x41 };    // "KPGA", a retirement witness
    static readonly Dictionary<string, string> Names = new Dictionary<string, string>();

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
        if (args.Length >= 2 && args[0] == "dump") { for (int i = 1; i < args.Length; i++) Dump(args[i]); return 0; }
        if (args.Length >= 3 && args[0] == "sum") { Sum(args[1], args[2]); return 0; }
        if (args.Length >= 3 && args[0] == "dde") { var d = Open(args[1]); uint n = d.DeleteDuplicateEntries(null); Console.WriteLine("Delete Duplicate Entries removed " + n); Save(d, args[2]); return 0; }
        if (args.Length >= 3 && args[0] == "write") { WriteNext(args[1], args[2]); return 0; }
        if (args.Length >= 3 && args[0] == "big") { Big(args[1], int.Parse(args[2])); return 0; }
        if (args.Length >= 1 && args[0] == "bench") { Bench(); return 0; }
        if (args.Length >= 1 && args[0] == "lattice") { Lattice(); return 0; }
        Console.Error.WriteLine("usage: make <dir> | kpmerge <target> <source> <method> <out> | dump <file>... | write <file> <out> | big <file> <chars> | bench | lattice");
        return 2;
    }

    // ---- identifiers ----------------------------------------------------------------------

    static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    static PwUuid SnapshotId(int seq)
    {
        var b = new byte[16];
        Rng.GetBytes(b);
        Array.Copy(SnapMagic, b, 4);
        b[4] = (byte)(seq >> 24); b[5] = (byte)(seq >> 16); b[6] = (byte)(seq >> 8); b[7] = (byte)seq;
        return new PwUuid(b);
    }

    static PwUuid Witness(byte[] magic, string subject)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.UTF8.GetBytes(subject));
            var b = new byte[16];
            Array.Copy(magic, b, 4);
            Array.Copy(d, 0, b, 4, 12);
            return new PwUuid(b);
        }
    }

    static bool Has(PwUuid u, byte[] magic)
    {
        var b = u.UuidBytes;
        return b[0] == magic[0] && b[1] == magic[1] && b[2] == magic[2] && b[3] == magic[3];
    }

    static int SeqOf(byte[] b) { return (b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7]; }
    static int SeqOfHex(string hex) { return Convert.ToInt32(hex.Substring(8, 8), 16); }
    static PwUuid StoreGroupUuid() { return new PwUuid(new byte[] { 0x4B, 0x50, 0x47, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }); }

    // ---- the stored text -------------------------------------------------------------------

    static string Q(string s) { return "\"" + System.Web.HttpUtility.JavaScriptStringEncode(s) + "\""; }
    static string Arr(IEnumerable<string> items) { return "[" + string.Join(",", items.Select(Q)) + "]"; }

    // One flat JSON object of strings and arrays of strings, members in a fixed order.
    public static string Body(Snapshot s)
    {
        var m = new List<string>();
        m.Add(Q("v") + ":" + Q("1"));
        m.Add(Q("id") + ":" + Q(s.Id.ToHexString()));
        m.Add(Q("seq") + ":" + Q(s.Seq.ToString()));
        m.Add(Q("key") + ":" + Q(Convert.ToBase64String(s.Key)));
        m.Add(Q("over") + ":" + Arr(s.Over));
        foreach (var kv in s.State.Authority) m.Add(Q("authority." + kv.Key) + ":" + Q(kv.Value));
        foreach (var r in s.State.Retired) m.Add(Q("retired." + r) + ":" + Q("retired"));
        foreach (var kv in s.State.Client) m.Add(Q("client." + kv.Key) + ":" + Q(kv.Value));
        foreach (var kv in s.State.Revoked) m.Add(Q("revoked." + kv.Key) + ":" + Q(kv.Value));
        foreach (var kv in s.State.Refused) m.Add(Q("refused." + kv.Key) + ":" + Arr(new[] { kv.Value.N.ToString(), kv.Value.Text }));
        foreach (var kv in s.State.Profile) m.Add(Q("profile." + kv.Key) + ":" + Q(kv.Value.ToString()));
        foreach (var kv in s.State.Rules)
        {
            var items = new List<string> { kv.Value.N.ToString() };
            if (kv.Value.Gone) items.Add("gone"); else items.AddRange(kv.Value.Docs);
            m.Add(Q("rule." + kv.Key) + ":" + Arr(items));
        }
        return "{" + string.Join(",", m) + "}";
    }

    static string Seal(Snapshot s)
    {
        var body = Body(s);
        using (var h = new HMACSHA256(s.Key))
            return body + "\n" + Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    // Null when the text will not read or its seal does not hold.
    static Snapshot Unseal(string text)
    {
        try
        {
            int cut = text.LastIndexOf('\n');
            if (cut < 0) return null;
            var body = text.Substring(0, cut);
            var mac = Convert.FromBase64String(text.Substring(cut + 1));
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var o = ser.Deserialize<Dictionary<string, object>>(body);
            if ((string)o["v"] != "1") return null;
            var s = new Snapshot();
            s.Key = Convert.FromBase64String((string)o["key"]);
            using (var h = new HMACSHA256(s.Key))
            {
                var want = h.ComputeHash(Encoding.UTF8.GetBytes(body));
                if (want.Length != mac.Length) return null;
                int diff = 0;
                for (int i = 0; i < want.Length; i++) diff |= want[i] ^ mac[i];
                if (diff != 0) return null;
            }
            s.Id = new PwUuid(KeePassLib.Utility.MemUtil.HexStringToByteArray((string)o["id"]));
            s.Seq = int.Parse((string)o["seq"]);
            foreach (var x in (System.Collections.ArrayList)o["over"]) s.Over.Add((string)x);
            foreach (var kv in o)
            {
                var k = kv.Key;
                if (k.StartsWith("authority.")) s.State.Authority[k.Substring(10)] = (string)kv.Value;
                else if (k.StartsWith("retired.")) s.State.Retired.Add(k.Substring(8));
                else if (k.StartsWith("client.")) s.State.Client[k.Substring(7)] = (string)kv.Value;
                else if (k.StartsWith("revoked.")) s.State.Revoked[k.Substring(8)] = (string)kv.Value;
                else if (k.StartsWith("refused."))
                {
                    var a = (System.Collections.ArrayList)kv.Value;
                    s.State.Refused[k.Substring(8)] = new Answer { N = int.Parse((string)a[0]), Text = (string)a[1] };
                }
                else if (k.StartsWith("profile.")) s.State.Profile[k.Substring(8)] = int.Parse((string)kv.Value);
                else if (k.StartsWith("rule."))
                {
                    var a = (System.Collections.ArrayList)kv.Value;
                    var r = new Rule { N = int.Parse((string)a[0]) };
                    for (int i = 1; i < a.Count; i++)
                    {
                        if ((string)a[i] == "gone") r.Gone = true; else r.Docs.Add((string)a[i]);
                    }
                    s.State.Rules[k.Substring(5)] = r;
                }
            }
            return s;
        }
        catch (Exception) { return null; }
    }

    // ---- reading a database the way the gate would ------------------------------------------

    sealed class Found
    {
        public PwEntry Entry;
        public Snapshot Snap;       // null when unreadable
        public bool Copy;           // reads, but under another identifier than its entry's
        public bool Named;          // a deletion record names this entry
        public string Where;
    }

    sealed class Reading
    {
        public string Verdict;
        public string Why = "";
        public State State;
        public List<Found> All = new List<Found>();
        public List<Found> Heads = new List<Found>();
        public List<string> SnapTombs = new List<string>();
        public HashSet<string> RevTombs = new HashSet<string>();
        public HashSet<string> RetTombs = new HashSet<string>();
        public bool Revoked(string cert) { return (State != null && State.Revoked.ContainsKey(cert)) || RevTombs.Contains(Witness(RevMagic, cert).ToHexString()); }
        public bool Retired(string fp) { return (State != null && State.Retired.Contains(fp)) || RetTombs.Contains(Witness(RetMagic, fp).ToHexString()); }
    }

    static Reading Read(PwDatabase db)
    {
        var r = new Reading();
        var named = new HashSet<string>();
        foreach (var d in db.DeletedObjects)
        {
            var hex = d.Uuid.ToHexString();
            if (Has(d.Uuid, SnapMagic)) { r.SnapTombs.Add(hex); named.Add(hex); }
            else if (Has(d.Uuid, RevMagic)) r.RevTombs.Add(hex);
            else if (Has(d.Uuid, RetMagic)) r.RetTombs.Add(hex);
        }
        // One pass over the whole tree: a snapshot counts wherever it is found.
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            var ps = e.Strings.Get(Field);
            bool marked = Has(e.Uuid, SnapMagic);
            if (ps == null && !marked) continue;
            var f = new Found { Entry = e, Where = PathOf(e.ParentGroup) };
            f.Snap = ps == null ? null : Unseal(ps.ReadString());
            f.Copy = f.Snap != null && !f.Snap.Id.Equals(e.Uuid);
            f.Named = named.Contains(e.Uuid.ToHexString());
            r.All.Add(f);
        }
        var good = r.All.Where(f => f.Snap != null && !f.Copy).ToList();
        var keys = good.Select(f => Convert.ToBase64String(f.Snap.Key)).Distinct().ToList();
        var live = good.Where(f => !f.Named).ToList();
        bool trace = r.All.Count > 0 || r.SnapTombs.Count > 0 || r.RevTombs.Count > 0 || r.RetTombs.Count > 0;

        if (r.All.Any(f => f.Copy)) { r.Verdict = "UNDECIDED"; r.Why = "a copy of a snapshot under another identifier"; }
        else if (keys.Count > 1) { r.Verdict = "UNDECIDED"; r.Why = "snapshots of " + keys.Count + " stores"; }

        // Heads: live snapshots no other live snapshot's line replaced.
        var replaced = new HashSet<string>();
        foreach (var f in live) foreach (var o in f.Snap.Over) replaced.Add(o);
        r.Heads = live.Where(f => !replaced.Contains(f.Entry.Uuid.ToHexString())).ToList();

        if (r.Verdict == null)
        {
            if (live.Count == 0) { r.Verdict = trace ? "GONE" : "NONE"; r.Why = trace ? "traces of a store and no snapshot in force" : ""; }
            else
            {
                Func<string, bool> covered = hex => live.Any(f => f.Snap.Over.Contains(hex)
                    || (f.Snap.Over.Count >= OverMax && SeqOfHex(hex) < f.Snap.Over.Min(o => SeqOfHex(o))));
                var loose = r.SnapTombs.Where(t => !covered(t)).ToList();
                var broken = r.All.Where(f => f.Snap == null && !f.Named && !covered(f.Entry.Uuid.ToHexString())).ToList();
                if (loose.Count > 0) { r.Verdict = "BEHIND"; r.Why = "deleted and replaced by nothing here: " + string.Join(",", loose.Select(ShortHex)); }
                else if (broken.Count > 0) { r.Verdict = "BEHIND"; r.Why = "will not read and replaced by nothing here: " + string.Join(",", broken.Select(f => Short(f.Entry.Uuid))); }
            }
        }
        if (live.Count > 0)
        {
            State s = null;
            foreach (var f in r.Heads) s = s == null ? f.Snap.State.Clone() : State.Join(s, f.Snap.State);
            r.State = s;
            if (r.Verdict == null)
            {
                var serving = s.Authority.Keys.Where(k => !r.Retired(k)).ToList();
                if (s.AuthorityConflict) { r.Verdict = "UNDECIDED"; r.Why = "one authority, two documents"; }
                else if (serving.Count > 1) { r.Verdict = "UNDECIDED"; r.Why = serving.Count + " authorities, none retired"; }
                else if (serving.Count == 0) { r.Verdict = "IN FORCE, no authority"; }
                else r.Verdict = "IN FORCE";
            }
        }
        return r;
    }

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
        if (Environment.GetEnvironmentVariable("WC_KDF") == "aes") { var k = new AesKdf().GetDefaultParameters(); k.SetUInt64(AesKdf.ParamRounds, 1UL); return k; }
        var a = new Argon2Kdf().GetDefaultParameters();
        a.SetUInt64(Argon2Kdf.ParamIterations, 2UL);
        a.SetUInt64(Argon2Kdf.ParamMemory, 1024UL * 1024UL);
        a.SetUInt32(Argon2Kdf.ParamParallelism, 1U);
        return a;
    }

    static void Stamp(PwGroup g, DateTime t) { g.CreationTime = t; g.LastModificationTime = t; g.LastAccessTime = t; g.LocationChanged = t; }
    static void Stamp(PwEntry e, DateTime t) { e.CreationTime = t; e.LastModificationTime = t; e.LastAccessTime = t; e.LocationChanged = t; }

    static string Filler(int n, string seed)
    {
        var sb = new StringBuilder();
        using (var sha = SHA256.Create())
        {
            var block = Encoding.UTF8.GetBytes(seed);
            while (sb.Length < n) { block = sha.ComputeHash(block); sb.Append(Convert.ToBase64String(block)); }
        }
        return sb.ToString(0, n);
    }

    static PwGroup StoreGroup(PwDatabase db, DateTime t)
    {
        var g = db.RootGroup.FindGroup(StoreGroupUuid(), true);
        if (g != null) return g;
        g = new PwGroup(false, false);
        g.Uuid = StoreGroupUuid();
        g.Name = "KeePassGate";
        g.IconId = PwIcon.TerminalEncrypted;
        g.Notes = GroupNotes;
        g.EnableSearching = false;
        g.EnableAutoType = false;
        g.IsExpanded = false;
        Stamp(g, t);
        db.RootGroup.AddGroup(g, true);
        return g;
    }

    static PwDatabase NewDatabase(string path, out PwUuid groupG, out PwUuid entryE1)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        db.KdfParameters = Kdf();
        db.RootGroup.Name = "Root";
        Stamp(db.RootGroup, T0);
        var general = new PwGroup(true, false) { Name = "General" };
        Stamp(general, T0);
        db.RootGroup.AddGroup(general, true);
        for (int i = 0; i < 3; i++)
        {
            var e = new PwEntry(true, false);
            e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "user entry " + i));
            e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-password-" + i));
            Stamp(e, T0);
            general.AddEntry(e, true);
        }
        var g = new PwGroup(true, false) { Name = "Projects" };
        Stamp(g, T0);
        db.RootGroup.AddGroup(g, true);
        var e1 = new PwEntry(true, false);
        e1.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "E1"));
        e1.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "invented-e1"));
        Stamp(e1, T0);
        g.AddEntry(e1, true);
        groupG = g.Uuid; entryE1 = e1.Uuid;
        return db;
    }

    static State BaseState(string fp, PwUuid groupG, PwUuid entryE1)
    {
        var s = new State();
        s.Authority[fp] = Filler(2903, "authority " + fp);
        s.Client["c1"] = Filler(819, "client c1");
        s.Client["c2"] = Filler(819, "client c2");
        s.Profile["reader"] = 1;
        s.Profile["writer"] = 1;
        if (groupG != null) s.Rules[groupG.ToHexString()] = new Rule { N = 1, Docs = { "{\"default\":\"inherit\",\"reader\":[\"read\",\"list\"]}" } };
        if (entryE1 != null) s.Rules[entryE1.ToHexString()] = new Rule { N = 1, Docs = { "{\"reader\":[]}" } };
        return s;
    }

    static byte[] NewKey() { var k = new byte[32]; Rng.GetBytes(k); return k; }

    static PwEntry Put(PwDatabase db, Snapshot s, DateTime t)
    {
        var e = new PwEntry(false, false);
        e.Uuid = s.Id;
        e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Title));
        e.Strings.Set(Field, new ProtectedString(true, Seal(s)));
        Stamp(e, t);
        StoreGroup(db, t).AddEntry(e, true);
        return e;
    }

    static void Delete(PwDatabase db, PwEntry e, DateTime t)
    {
        e.ParentGroup.Entries.Remove(e);
        foreach (var d in db.DeletedObjects) if (d.Uuid.Equals(e.Uuid)) return;
        db.DeletedObjects.Add(new PwDeletedObject(e.Uuid, t));
    }

    static void AddWitness(PwDatabase db, byte[] magic, string subject, DateTime t)
    {
        var u = Witness(magic, subject);
        foreach (var d in db.DeletedObjects) if (d.Uuid.Equals(u)) return;
        db.DeletedObjects.Add(new PwDeletedObject(u, t));
    }

    // The plugin's write: join what is in force, change it, add one new snapshot, delete the rest.
    static PwEntry Write(PwDatabase db, DateTime t, Action<State> change)
    {
        var r = Read(db);
        var s = new Snapshot();
        var live = r.All.Where(f => f.Snap != null && !f.Copy && !f.Named).ToList();
        s.State = r.State == null ? new State() : r.State.Clone();
        s.Key = live.Count > 0 ? live[0].Snap.Key : NewKey();
        s.Seq = (live.Count > 0 ? live.Max(f => f.Snap.Seq) : 0) + 1;
        var over = new SortedSet<string>(Comparer<string>.Create((a, b) =>
        {
            int c = SeqOfHex(a).CompareTo(SeqOfHex(b));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }));
        foreach (var f in live) { foreach (var o in f.Snap.Over) over.Add(o); over.Add(f.Entry.Uuid.ToHexString()); }
        foreach (var f in r.All.Where(f => f.Snap == null || f.Copy)) over.Add(f.Entry.Uuid.ToHexString());
        s.Over = over.Skip(Math.Max(0, over.Count - OverMax)).ToList();
        change(s.State);
        s.Id = SnapshotId(s.Seq);
        var e = Put(db, s, t);
        foreach (var f in r.All) Delete(db, f.Entry, t);
        foreach (var c in s.State.Revoked.Keys) AddWitness(db, RevMagic, c, t);
        foreach (var a in s.State.Retired) AddWitness(db, RetMagic, a, t);
        return e;
    }

    static void Save(PwDatabase db, string path) { db.SaveAs(IOConnectionInfo.FromPath(path), false, null); db.Close(); }

    static void Make(string dir)
    {
        Directory.CreateDirectory(dir);
        string basePath = Path.Combine(dir, "base.kdbx");
        PwUuid g, e1;
        {
            var db = NewDatabase(basePath, out g, out e1);
            var s0 = new Snapshot { Seq = 1, Key = NewKey(), State = BaseState("fpA", g, e1) };
            s0.Id = SnapshotId(1);
            Put(db, s0, T0);
            Save(db, basePath);
        }
        File.WriteAllText(Path.Combine(dir, "names.txt"), g.ToHexString() + "=G\n" + e1.ToHexString() + "=E1\n");
        string e1hex = e1.ToHexString();

        // U: A revokes c1 and so replaces the snapshot; B is the base, unchanged.
        File.Copy(basePath, Path.Combine(dir, "U-B.kdbx"), true);
        {
            var db = Open(basePath);
            Write(db, T0.AddSeconds(10), s => s.Revoked["c1"] = "2027-01-01T00:00:00Z fpA");
            Save(db, Path.Combine(dir, "U-A.kdbx"));
        }
        // C: both copies write between two synchronizations.
        {
            var db = Open(basePath);
            Write(db, T0.AddSeconds(10), s =>
            {
                s.Client["c3"] = Filler(819, "client c3");
                s.Rules[e1hex] = new Rule { N = 2, Docs = { "{\"reader\":[\"read\"]}" } };
            });
            Save(db, Path.Combine(dir, "C-A.kdbx"));
        }
        {
            var db = Open(basePath);
            Write(db, T0.AddSeconds(20), s =>
            {
                s.Revoked["c2"] = "2027-01-01T00:00:00Z fpA";
                s.Rules[e1hex] = new Rule { N = 2, Docs = { "{\"reader\":[\"list\"]}" } };
                s.Profile["writer"] = 2;
            });
            Save(db, Path.Combine(dir, "C-B.kdbx"));
        }
        // H: A revokes c1 as in U, then somebody deletes the new snapshot by hand, for good.
        File.Copy(basePath, Path.Combine(dir, "H-B.kdbx"), true);
        {
            var db = Open(Path.Combine(dir, "U-A.kdbx"));
            var head = Read(db).Heads.Single().Entry;
            Delete(db, head, T0.AddSeconds(30));
            Save(db, Path.Combine(dir, "H-A.kdbx"));
        }
        // R: as H, but the snapshot is moved to the recycle bin and left there.
        File.Copy(basePath, Path.Combine(dir, "R-B.kdbx"), true);
        {
            var db = Open(Path.Combine(dir, "U-A.kdbx"));
            var head = Read(db).Heads.Single().Entry;
            var bin = new PwGroup(true, false) { Name = "Recycle Bin", IconId = PwIcon.TrashBin, EnableSearching = false, EnableAutoType = false };
            Stamp(bin, T0.AddSeconds(30));
            db.RootGroup.AddGroup(bin, true);
            db.RecycleBinUuid = bin.Uuid; db.RecycleBinChanged = T0.AddSeconds(30);
            head.ParentGroup.Entries.Remove(head);
            bin.AddEntry(head, true);
            head.LocationChanged = T0.AddSeconds(30);
            Save(db, Path.Combine(dir, "R-A.kdbx"));
        }
        // D: A as in U; in B somebody edits the old snapshot's text by hand, as an editor would.
        File.Copy(Path.Combine(dir, "U-A.kdbx"), Path.Combine(dir, "D-A.kdbx"), true);
        {
            var db = Open(basePath);
            var e = Read(db).Heads.Single().Entry;
            e.CreateBackup(db);
            var text = e.Strings.Get(Field).ReadString();
            e.Strings.Set(Field, new ProtectedString(true, text.Replace("profile.reader", "profile.Reader")));
            e.LastModificationTime = T0.AddSeconds(15);
            Save(db, Path.Combine(dir, "D-B.kdbx"));
        }
        // X: no store in the base; each copy makes one of its own.
        string bare = Path.Combine(dir, "bare.kdbx");
        {
            PwUuid g2, e2;
            var db = NewDatabase(bare, out g2, out e2);
            Save(db, bare);
        }
        {
            var db = Open(bare);
            Write(db, T0.AddSeconds(10), s => { s.Authority["fpA"] = Filler(2903, "authority fpA"); s.Profile["reader"] = 1; });
            Save(db, Path.Combine(dir, "X-A.kdbx"));
        }
        {
            var db = Open(bare);
            Write(db, T0.AddSeconds(20), s => { s.Authority["fpB"] = Filler(2903, "authority fpB"); s.Profile["reader"] = 1; });
            Save(db, Path.Combine(dir, "X-B.kdbx"));
        }
        // K: A recreates the authority (retires fpA, makes fpC, drops every record); B pairs c3 with fpA meanwhile.
        {
            var db = Open(basePath);
            Write(db, T0.AddSeconds(10), s =>
            {
                s.Retired.Add("fpA"); s.Authority.Remove("fpA"); s.Client.Clear(); s.Revoked.Clear();
                s.Authority["fpC"] = Filler(2903, "authority fpC");
            });
            Save(db, Path.Combine(dir, "K-A.kdbx"));
        }
        {
            var db = Open(basePath);
            Write(db, T0.AddSeconds(20), s => s.Client["c3"] = Filler(819, "client c3"));
            Save(db, Path.Combine(dir, "K-B.kdbx"));
        }
    }

    // One more write on top of whatever a file holds: what the plugin's next write makes of it.
    static void WriteNext(string file, string result)
    {
        var db = Open(file);
        Write(db, DateTime.UtcNow, s => { });
        Save(db, result);
    }

    // ---- showing --------------------------------------------------------------------------

    static string Short(PwUuid u) { return u == null || u.Equals(PwUuid.Zero) ? "-" : ShortHex(u.ToHexString()); }
    static string ShortHex(string hex)
    {
        hex = hex.ToLowerInvariant();
        return hex.StartsWith("4b504753") ? "S" + SeqOfHex(hex) + ":" + hex.Substring(16, 6)
            : hex.StartsWith("4b504752") ? "R:" + hex.Substring(8, 6)
            : hex.StartsWith("4b504741") ? "A:" + hex.Substring(8, 6)
            : hex.Substring(0, 8);
    }
    static string Tm(DateTime t) { return t.ToUniversalTime().ToString("MM-dd'T'HH:mm:ss"); }
    static string PathOf(PwGroup g) { return g == null ? "?" : g.ParentGroup == null ? "/" : (PathOf(g.ParentGroup).TrimEnd('/') + "/" + g.Name); }
    public static string NodeName(string hex) { string n; return Names.TryGetValue(hex, out n) ? n : hex.Substring(0, 8).ToLowerInvariant(); }

    static string FormatVersion(string file)
    {
        var b = new byte[12];
        using (var s = File.OpenRead(file)) s.Read(b, 0, 12);
        uint v = BitConverter.ToUInt32(b, 8);
        return (v >> 16) + "." + (v & 0xFFFF);
    }

    public static string Sha(string text)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)), 0, 4).Replace("-", "").ToLowerInvariant();
    }

    static void LoadNames(string file)
    {
        var p = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)), "names.txt");
        for (int up = 0; up < 3 && !File.Exists(p); up++)
            p = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(p)), "names.txt");
        var candidates = new[] { p, Environment.GetEnvironmentVariable("WC_NAMES") };
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c) || !File.Exists(c)) continue;
            foreach (var line in File.ReadAllLines(c))
            {
                var kv = line.Split('=');
                if (kv.Length == 2) Names[kv[0]] = kv[1];
            }
        }
    }

    // One line a file: what the gate would make of it.
    static void Sum(string label, string file)
    {
        LoadNames(file);
        var db = Open(file);
        var r = Read(db);
        var present = r.All.OrderBy(f => f.Snap == null ? 0 : f.Snap.Seq).ThenBy(f => f.Entry.Uuid.ToHexString(), StringComparer.Ordinal)
            .Select(f => Short(f.Entry.Uuid) + (f.Snap == null ? "!unreadable" : f.Copy ? "!copy" : "") + (f.Named ? "!named" : "") + (r.Heads.Contains(f) ? "*" : "")
                + (f.Where == "/KeePassGate" ? "" : "@" + f.Where));
        var hold = new List<string>();
        foreach (var c in new[] { "c1", "c2", "c3" }) if (r.Revoked(c)) hold.Add(c + (r.State != null && r.State.Revoked.ContainsKey(c) ? "" : "(witness)"));
        foreach (var a in new[] { "fpA", "fpB", "fpC" }) if (r.Retired(a)) hold.Add(a + (r.State != null && r.State.Retired.Contains(a) ? "" : "(witness)"));
        Console.WriteLine(label.PadRight(34) + " | " + (r.Verdict + (r.Why.Length > 0 ? ": " + r.Why : "")).PadRight(46)
            + " | present [" + string.Join(" ", present) + "]"
            + " | records S[" + string.Join(" ", r.SnapTombs.Select(ShortHex).OrderBy(x => x, StringComparer.Ordinal)) + "] R" + r.RevTombs.Count + " A" + r.RetTombs.Count
            + " | refused [" + string.Join(",", hold) + "]"
            + (r.State == null ? "" : " | state " + r.State.Digest() + " " + r.State.Show()));
        db.Close();
    }

    static void Dump(string file)
    {
        LoadNames(file);
        var db = Open(file);
        var r = Read(db);
        Console.WriteLine("# " + Path.GetFileName(file) + "  kdbx " + FormatVersion(file) + "  entries=" + db.RootGroup.GetEntriesCount(true)
            + "  dbCustomData=" + db.CustomData.Count);
        foreach (var f in r.All.OrderBy(f => f.Entry.Uuid.ToHexString(), StringComparer.Ordinal))
        {
            var ps = f.Entry.Strings.Get(Field);
            var others = f.Entry.Strings.Where(kv => kv.Key != Field && kv.Key != PwDefs.TitleField).Select(kv => kv.Key + "(" + kv.Value.ReadString().Length + ")");
            Console.WriteLine("  snapshot " + Short(f.Entry.Uuid) + " in " + f.Where + " mod=" + Tm(f.Entry.LastModificationTime)
                + " " + (f.Snap == null ? "WILL-NOT-READ" : f.Copy ? "COPY-OF " + Short(f.Snap.Id) : "seal-ok over=[" + string.Join(",", f.Snap.Over.Select(ShortHex)) + "]")
                + (f.Named ? " NAMED-BY-A-DELETION-RECORD" : "")
                + (r.Heads.Contains(f) ? " HEAD" : "")
                + " text=" + (ps == null ? "none" : ps.ReadString().Length + (ps.IsProtected ? "P" : "u"))
                + " history=" + f.Entry.History.UCount
                + " other-strings=[" + string.Join(",", others) + "]");
        }
        Console.WriteLine("  deletion records: " + db.DeletedObjects.UCount + " in all; snapshots [" + string.Join(",", r.SnapTombs.Select(ShortHex).OrderBy(x => x, StringComparer.Ordinal))
            + "] revoked [" + string.Join(",", r.RevTombs.Select(ShortHex).OrderBy(x => x, StringComparer.Ordinal))
            + "] retired [" + string.Join(",", r.RetTombs.Select(ShortHex).OrderBy(x => x, StringComparer.Ordinal)) + "]");
        Console.WriteLine("  verdict: " + r.Verdict + (r.Why.Length > 0 ? " (" + r.Why + ")" : ""));
        if (r.State != null)
        {
            Console.WriteLine("  joined: " + r.State.Show());
            var hold = new List<string>();
            foreach (var c in new[] { "c1", "c2", "c3" }) if (r.Revoked(c)) hold.Add(c + (r.State.Revoked.ContainsKey(c) ? "" : "(by witness only)"));
            foreach (var a in new[] { "fpA", "fpB", "fpC" }) if (r.Retired(a)) hold.Add(a + (r.State.Retired.Contains(a) ? "" : "(by witness only)"));
            Console.WriteLine("  refused whatever else: " + string.Join(",", hold));
        }
        else
        {
            var hold = new List<string>();
            foreach (var c in new[] { "c1", "c2", "c3" }) if (r.Revoked(c)) hold.Add(c + "(by witness only)");
            foreach (var a in new[] { "fpA", "fpB", "fpC" }) if (r.Retired(a)) hold.Add(a + "(by witness only)");
            Console.WriteLine("  refused whatever else: " + string.Join(",", hold));
        }
        db.Close();
    }

    // ---- a long protected string --------------------------------------------------------------

    static void Big(string file, int chars)
    {
        PwUuid g, e1;
        var db = NewDatabase(file, out g, out e1);
        var s = new Snapshot { Seq = 1, Key = NewKey(), State = BaseState("fpA", g, e1) };
        int i = 0;
        s.Id = SnapshotId(1);
        int need = Math.Max(0, (chars - Body(s).Length) / 835 + 1);
        for (; i < need; i++) s.State.Client["big" + i] = Filler(819, "big client " + i);
        s.Id = SnapshotId(1);
        Put(db, s, T0);
        Save(db, file);
        Console.WriteLine("big: " + Path.GetFileName(file) + " text " + Seal(s).Length + " chars, " + i + " extra records");
    }

    // ---- cost ---------------------------------------------------------------------------------

    static void Bench()
    {
        foreach (int entries in new[] { 1000, 10000, 50000 })
        {
            var db = new PwDatabase();
            db.New(new IOConnectionInfo(), Key());
            var rnd = new Random(7);
            var groups = new List<PwGroup> { db.RootGroup };
            for (int i = 0; i < entries / 20; i++)
            {
                var g = new PwGroup(true, true) { Name = "g" + i };
                groups[rnd.Next(groups.Count)].AddGroup(g, true);
                groups.Add(g);
            }
            for (int i = 0; i < entries; i++)
            {
                var e = new PwEntry(true, true);
                e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "entry " + i));
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "pw" + i));
                groups[rnd.Next(groups.Count)].AddEntry(e, true);
            }
            for (int i = 0; i < 5000; i++) db.DeletedObjects.Add(new PwDeletedObject(new PwUuid(true), DateTime.UtcNow));
            var state = BaseState("fpA", null, null);
            for (int i = 0; i < 20; i++) state.Client["c" + (10 + i)] = Filler(819, "client " + i);
            for (int i = 0; i < 100; i++) state.Rules[new PwUuid(true).ToHexString()] = new Rule { N = 1, Docs = { "{\"default\":\"inherit\",\"reader\":[\"read\",\"list\"],\"writer\":[\"create\",\"read\",\"update\",\"list\"]}" } };
            var s0 = new Snapshot { Seq = 1, Key = NewKey(), State = state };
            s0.Id = SnapshotId(1);
            var entry = Put(db, s0, DateTime.UtcNow);
            int textLength = entry.Strings.Get(Field).ReadString().Length;

            // The pass alone: every entry's identifier and whether it carries the field.
            int reps = entries >= 50000 ? 40 : 200;
            var sw = Stopwatch.StartNew();
            int found = 0;
            for (int k = 0; k < reps; k++)
            {
                found = 0;
                EntryHandler eh = e => { if (Has(e.Uuid, SnapMagic) || e.Strings.Exists(Field)) found++; return true; };
                db.RootGroup.TraverseTree(TraversalMethod.PreOrder, null, eh);
                foreach (var d in db.DeletedObjects) if (Has(d.Uuid, SnapMagic) || Has(d.Uuid, RevMagic) || Has(d.Uuid, RetMagic)) found++;
            }
            sw.Stop();
            double pass = sw.Elapsed.TotalMilliseconds / reps;

            // The same pass looking at identifiers only, by plain recursion.
            sw.Restart();
            for (int k = 0; k < reps; k++) { found = 0; found += Marked(db.RootGroup); foreach (var d in db.DeletedObjects) if (Has(d.Uuid, SnapMagic) || Has(d.Uuid, RevMagic) || Has(d.Uuid, RetMagic)) found++; }
            sw.Stop();
            double idsOnly = sw.Elapsed.TotalMilliseconds / reps;

            // Deletion records alone.
            sw.Restart();
            for (int k = 0; k < 2000; k++) { found = 0; foreach (var d in db.DeletedObjects) if (Has(d.Uuid, SnapMagic) || Has(d.Uuid, RevMagic) || Has(d.Uuid, RetMagic)) found++; }
            sw.Stop();
            double tombs = sw.Elapsed.TotalMilliseconds / 2000;

            // The store group and the recycle bin only, found by identifier each time.
            var bin = new PwGroup(true, true) { Name = "Recycle Bin" };
            db.RootGroup.AddGroup(bin, true);
            db.RecycleBinUuid = bin.Uuid;
            sw.Restart();
            for (int k = 0; k < 2000; k++)
            {
                found = 0;
                var sg = db.RootGroup.FindGroup(StoreGroupUuid(), true);
                if (sg != null) found += Marked(sg);
                var bg = db.RootGroup.FindGroup(db.RecycleBinUuid, true);
                if (bg != null) found += Marked(bg);
            }
            sw.Stop();
            double direct = sw.Elapsed.TotalMilliseconds / 2000;
            Console.WriteLine("entries " + entries + ": pass by identifier only " + idsOnly.ToString("0.000") + " ms; deletion records alone " + tombs.ToString("0.0000")
                + " ms; store group and bin found by identifier " + direct.ToString("0.0000") + " ms");

            // The whole reading with nothing kept: pass, unprotect, check the seal, parse, join.
            sw.Restart();
            int full = 50;
            for (int k = 0; k < full; k++) Read(db);
            sw.Stop();
            double whole = sw.Elapsed.TotalMilliseconds / full;

            // Unprotect and check the seal only.
            sw.Restart();
            for (int k = 0; k < 200; k++)
            {
                var text = entry.Strings.Get(Field).ReadString();
                int cut = text.LastIndexOf('\n');
                using (var h = new HMACSHA256(s0.Key)) h.ComputeHash(Encoding.UTF8.GetBytes(text.Substring(0, cut)));
            }
            sw.Stop();
            double seal = sw.Elapsed.TotalMilliseconds / 200;

            Console.WriteLine("entries " + entries + ", groups " + groups.Count + ", deletion records 5000, snapshot text " + textLength
                + " chars: pass " + pass.ToString("0.000") + " ms, unprotect and seal " + seal.ToString("0.000")
                + " ms, whole reading uncached " + whole.ToString("0.000") + " ms (found " + found + ")");
        }
    }

    static int Marked(PwGroup g)
    {
        int n = 0;
        foreach (var e in g.Entries) if (Has(e.Uuid, SnapMagic)) n++;
        foreach (var sub in g.Groups) n += Marked(sub);
        return n;
    }

    // ---- the join's laws, tried on random histories ---------------------------------------------

    static void Lattice()
    {
        var rnd = new Random(20261115);
        int checks = 0, bad = 0;
        string[] certs = { "c1", "c2", "c3", "c4" };
        string[] profiles = { "reader", "writer", "ops" };
        string[] nodes = { "n1", "n2", "n3" };
        string[] docs = { "{\"reader\":[]}", "{\"reader\":[\"read\"]}", "{\"default\":\"deny\"}", "{\"writer\":[\"list\"]}" };
        string[] answers = { "forever", "lifted", "2027-01-01T00:00:00Z", "2028-01-01T00:00:00Z" };
        Func<State, State> step = s0 =>
        {
            var s = s0.Clone();
            switch (rnd.Next(7))
            {
                case 0: { var c = certs[rnd.Next(certs.Length)]; if (!s.Client.ContainsKey(c)) s.Client[c] = "record " + c; break; }
                case 1: { var c = certs[rnd.Next(certs.Length)]; if (!s.Revoked.ContainsKey(c)) s.Revoked[c] = "revoked"; break; }
                case 2: { var p = profiles[rnd.Next(profiles.Length)]; int n; s.Profile.TryGetValue(p, out n); s.Profile[p] = n + 1; break; }
                case 3:
                    {
                        var k = nodes[rnd.Next(nodes.Length)]; Rule r; int n = s.Rules.TryGetValue(k, out r) ? r.N : 0;
                        var nr = new Rule { N = n + 1 }; if (rnd.Next(4) > 0) nr.Docs.Add(docs[rnd.Next(docs.Length)]);
                        s.Rules[k] = nr; break;
                    }
                case 4:
                    {
                        var c = certs[rnd.Next(certs.Length)]; Answer a; int n = s.Refused.TryGetValue(c, out a) ? a.N : 0;
                        s.Refused[c] = new Answer { N = n + 1, Text = answers[rnd.Next(answers.Length)] }; break;
                    }
                case 5: { if (!s.Authority.ContainsKey("fpA")) s.Authority["fpA"] = "doc A"; break; }
                case 6: { var k = nodes[rnd.Next(nodes.Length)]; Rule r; if (s.Rules.TryGetValue(k, out r) && !r.Gone) { var g = r.Clone(); g.Gone = true; g.Docs.Clear(); s.Rules[k] = g; } break; }
            }
            return s;
        };
        for (int round = 0; round < 2000; round++)
        {
            // One line of history, then two lines forking from a point on it.
            var line = new List<State> { new State() };
            for (int i = 0; i < 12; i++) line.Add(step(line[line.Count - 1]));
            var last = line[line.Count - 1];
            foreach (var old in line)
            {
                checks++;
                if (State.Join(last, old).Digest() != last.Digest() || State.Join(old, last).Digest() != last.Digest()) bad++;
            }
            var fork = line[rnd.Next(line.Count)];
            State a = fork, b = fork, c = fork;
            for (int i = 0; i < 5; i++) { a = step(a); b = step(b); c = step(c); }
            checks += 4;
            if (State.Join(a, b).Digest() != State.Join(b, a).Digest()) bad++;
            if (State.Join(State.Join(a, b), c).Digest() != State.Join(a, State.Join(b, c)).Digest()) bad++;
            if (State.Join(a, a).Digest() != a.Digest()) bad++;
            if (State.Join(State.Join(a, b), fork).Digest() != State.Join(a, b).Digest()) bad++;
            // Nothing refusing is ever lost by a join.
            var j = State.Join(a, b);
            checks += 3;
            if (a.Revoked.Keys.Concat(b.Revoked.Keys).Any(k => !j.Revoked.ContainsKey(k))) bad++;
            if (a.Retired.Concat(b.Retired).Any(k => !j.Retired.Contains(k))) bad++;
            bool widened = false;
            foreach (var p in profiles)
            {
                int pa, pb, pj; a.Profile.TryGetValue(p, out pa); b.Profile.TryGetValue(p, out pb); j.Profile.TryGetValue(p, out pj);
                if (pj != Math.Max(pa, pb)) widened = true;
            }
            if (widened) bad++;
        }
        Console.WriteLine("lattice: " + checks + " checks, " + bad + " failed");
    }
}
