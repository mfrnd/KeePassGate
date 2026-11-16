// Critique of option-outside-file, survival lens: the store's join and checks, run through three
// sequences the design's prototype did not run. The store classes follow the design's prototype
// (storage\outside-file\probe\Store.cs) with three changes that bring it to the design's own text:
// only <16 hex>.kpgw files are read (section 6), the seen file keeps a digest beside each count and
// compares digests where the counts are equal (section 6, check 3), and one Apply writes several keys
// in one file write (section 9.1). Letting go at expiry (section 9.6) is my reading of the text; the
// prototype does not have it. Console only. Invented content. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

public sealed class Line
{
    public string K; public string V; public long N; public string Writer;
    public Dictionary<string, long> Saw = new Dictionary<string, long>();
}

public sealed class WFile
{
    public string Store; public string Writer; public long Head; public string Digest;
    public Dictionary<string, long> Saw = new Dictionary<string, long>();
    public Dictionary<string, Line> Lines = new Dictionary<string, Line>(StringComparer.Ordinal);
}

public static class Seal
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("KPGW1");
    static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();
    public static string StoreId(byte[] key)
    {
        using (var sha = SHA256.Create())
        {
            var d = sha.ComputeHash(Encoding.ASCII.GetBytes("KPG/1/store/").Concat(key).ToArray());
            return string.Concat(d.Take(16).Select(b => b.ToString("x2")));
        }
    }
    static byte[] Aad(string s, string w) { return Encoding.ASCII.GetBytes("KPG/1/writer/" + s + "/" + w); }
    public static byte[] Close(byte[] key, string s, string w, byte[] plain)
    {
        var nonce = new byte[12]; Rng.GetBytes(nonce);
        var g = new GcmBlockCipher(new AesEngine());
        g.Init(true, new AeadParameters(new KeyParameter(key), 128, nonce, Aad(s, w)));
        var body = new byte[g.GetOutputSize(plain.Length)];
        int n = g.ProcessBytes(plain, 0, plain.Length, body, 0); g.DoFinal(body, n);
        return Magic.Concat(nonce).Concat(body).ToArray();
    }
    public static byte[] Open(byte[] key, string s, string w, byte[] all)
    {
        try
        {
            if (all.Length < 33 || !all.Take(5).SequenceEqual(Magic)) return null;
            var g = new GcmBlockCipher(new AesEngine());
            g.Init(false, new AeadParameters(new KeyParameter(key), 128, all.Skip(5).Take(12).ToArray(), Aad(s, w)));
            var plain = new byte[g.GetOutputSize(all.Length - 17)];
            int n = g.ProcessBytes(all, 17, all.Length - 17, plain, 0); g.DoFinal(plain, n);
            return plain;
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException) { return null; }
    }
}

public sealed class Inst
{
    public readonly string Dir, Id, Me, Name; public readonly byte[] Key;
    // The seen file: writer -> (head, digest), kept on the computer.
    public Dictionary<string, KeyValuePair<long, string>> Seen = new Dictionary<string, KeyValuePair<long, string>>();
    public Dictionary<string, string> Image = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<string> Problems = new List<string>();
    public Dictionary<string, WFile> Files = new Dictionary<string, WFile>(StringComparer.Ordinal);
    public DateTime Clock;
    static readonly Regex WriterName = new Regex("^[0-9a-f]{16}$");

    public Inst(string name, string dir, byte[] key, string me, DateTime clock)
    { Name = name; Dir = dir; Key = key; Me = me; Id = Seal.StoreId(key); Clock = clock; Directory.CreateDirectory(dir); Load(); }

    public bool Serves { get { return Problems.Count == 0; } }
    public string Get(string k) { string v; return Image.TryGetValue(k, out v) ? v : null; }
    static bool AddOnly(string k) { return k.StartsWith("KPG.Revoked.") || k.StartsWith("KPG.RetiredAuthority."); }
    static bool Knows(Line x, Line y) { long c; return x.Saw.TryGetValue(y.Writer, out c) && c >= y.N; }

    static WFile Parse(byte[] key, string store, string writer, byte[] bytes)
    {
        var plain = Seal.Open(key, store, writer, bytes);
        if (plain == null) return null;
        var o = JObject.Parse(Encoding.UTF8.GetString(plain));
        var f = new WFile { Store = (string)o["store"], Writer = (string)o["writer"], Head = (long)o["head"] };
        if (f.Store != store || f.Writer != writer) return null;
        foreach (var p in (JObject)o["saw"]) f.Saw[p.Key] = (long)p.Value;
        foreach (JObject e in (JArray)o["lines"])
        {
            var x = new Line { K = (string)e["k"], V = e["v"].Type == JTokenType.Null ? null : (string)e["v"], N = (long)e["n"], Writer = writer };
            foreach (var p in (JObject)e["saw"]) x.Saw[p.Key] = (long)p.Value;
            f.Lines[x.K] = x;
        }
        using (var sha = SHA256.Create()) f.Digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 12);
        return f;
    }

    public void Load()
    {
        Image.Clear(); Problems.Clear(); Files.Clear();
        foreach (var path in Directory.GetFiles(Dir, "*.kpgw"))
        {
            string w = Path.GetFileNameWithoutExtension(path);
            if (!WriterName.IsMatch(w)) continue;                     // a conflict copy's name is not read
            var f = Parse(Key, Id, w, File.ReadAllBytes(path));
            if (f == null) { Problems.Add("file of " + w + " does not open"); continue; }
            Files[w] = f;
        }
        foreach (var f in Files.Values)
            foreach (var s in f.Saw)
            {
                WFile o;
                if (!Files.TryGetValue(s.Key, out o)) Problems.Add(s.Key + " missing, " + f.Writer + " saw it at " + s.Value);
                else if (o.Head < s.Value) Problems.Add(s.Key + " at " + o.Head + ", " + f.Writer + " saw it at " + s.Value);
            }
        foreach (var j in Seen)
        {
            WFile f;
            if (!Files.TryGetValue(j.Key, out f)) Problems.Add(j.Key + " missing, this computer saw it at " + j.Value.Key);
            else if (f.Head < j.Value.Key) Problems.Add(j.Key + " at " + f.Head + ", this computer saw it at " + j.Value.Key);
            else if (f.Head == j.Value.Key && f.Digest != j.Value.Value) Problems.Add(j.Key + " at " + f.Head + " is not the file this computer saw at that count");
        }
        var byKey = new Dictionary<string, List<Line>>(StringComparer.Ordinal);
        foreach (var f in Files.Values) foreach (var e in f.Lines.Values)
        { List<Line> l; if (!byKey.TryGetValue(e.K, out l)) byKey[e.K] = l = new List<Line>(); l.Add(e); }
        foreach (var kv in byKey)
        {
            var k = kv.Key;
            if (AddOnly(k)) { var held = kv.Value.FirstOrDefault(e => e.V != null); if (held != null) Image[k] = held.V; continue; }
            var live = kv.Value.Where(y => !kv.Value.Any(x => x != y && Knows(x, y))).ToList();
            if (live.Count == 1) { if (live[0].V != null) Image[k] = live[0].V; continue; }
            if (k.StartsWith("KPG.Rule.")) Image[k] = "!two versions";
            else if (k.StartsWith("KPG.Profile.")) { if (live.All(e => e.V != null)) Image[k] = live[0].V; }
            else if (k.StartsWith("KPG.Client."))
            {
                if (live.Any(e => string.IsNullOrEmpty(e.V))) Image[k] = string.Empty;
                else if (live.All(e => e.V == live[0].V)) Image[k] = live[0].V; else Image[k] = string.Empty;
            }
        }
        if (Serves) foreach (var f in Files.Values) Seen[f.Writer] = new KeyValuePair<long, string>(f.Head, f.Digest);
    }

    // One file write of several keys (null value: taken away). Lets go of what another writer's line
    // knows of, and, riding on the write, of this writer's own records and revocations whose
    // certificate has expired by this computer's clock (section 9.6).
    public void Apply(Dictionary<string, string> changes)
    {
        Load();
        if (!Serves) throw new InvalidOperationException(Name + " cannot write: " + string.Join("; ", Problems));
        WFile mine;
        if (!Files.TryGetValue(Me, out mine)) mine = new WFile { Store = Id, Writer = Me };
        var saw = Files.Values.Where(f => f.Writer != Me).ToDictionary(f => f.Writer, f => f.Head);
        mine.Head++; mine.Saw = saw;
        foreach (var c in changes)
        {
            if (c.Value == null && AddOnly(c.Key)) { mine.Lines.Remove(c.Key); continue; }
            mine.Lines[c.Key] = new Line { K = c.Key, V = c.Value, N = mine.Head, Saw = new Dictionary<string, long>(saw), Writer = Me };
        }
        foreach (var dead in mine.Lines.Values.Where(y => !AddOnly(y.K) && Files.Values.Any(f => f.Writer != Me && f.Lines.ContainsKey(y.K) && Knows(f.Lines[y.K], y))).Select(y => y.K).ToList())
            mine.Lines.Remove(dead);
        foreach (var l in mine.Lines.Values.ToList())
        {
            DateTime notAfter;
            if ((l.K.StartsWith("KPG.Revoked.") || l.K.StartsWith("KPG.Client.")) && NotAfter(l.K, out notAfter) && notAfter < Clock)
                mine.Lines.Remove(l.K);
        }
        Put(mine); Load();
    }

    // The invented certificates' expiry, known to every installation (in the plugin it is in the record
    // and in the revocation's value).
    public static Dictionary<string, DateTime> Expiry = new Dictionary<string, DateTime>();
    static bool NotAfter(string key, out DateTime t)
    {
        var fp = key.Substring(key.LastIndexOf('.') + 1);
        return Expiry.TryGetValue(fp, out t);
    }

    void Put(WFile f)
    {
        var o = new JObject(); o["v"] = "1"; o["store"] = f.Store; o["writer"] = f.Writer; o["head"] = f.Head;
        var s = new JObject(); foreach (var p in f.Saw) s[p.Key] = p.Value; o["saw"] = s;
        var a = new JArray();
        foreach (var e in f.Lines.Values.OrderBy(x => x.K, StringComparer.Ordinal))
        {
            var j = new JObject(); j["k"] = e.K; j["v"] = e.V == null ? JValue.CreateNull() : (JToken)e.V; j["n"] = e.N;
            var es = new JObject(); foreach (var p in e.Saw) es[p.Key] = p.Value; j["saw"] = es; a.Add(j);
        }
        o["lines"] = a;
        var bytes = Seal.Close(Key, f.Store, f.Writer, Encoding.UTF8.GetBytes(o.ToString(Formatting.None)));
        string final = Path.Combine(Dir, f.Writer + ".kpgw"), temp = Path.Combine(Dir, "~" + f.Writer + ".tmp");
        File.WriteAllBytes(temp, bytes);
        if (File.Exists(final)) File.Replace(temp, final, null); else File.Move(temp, final);
    }

    public string State() { return Serves ? "serves" : "closed (" + string.Join("; ", Problems) + ")"; }
}

// A file synchronizer between two folders, in the shape Syncthing documents: a file changed on one
// side only goes to the other; a file changed on both since they last met is a conflict, the newer
// keeps the name and the older is renamed <name>.sync-conflict-<date>-<time>-<device>.<ext> on both.
public sealed class Sync
{
    readonly Dictionary<string, string> last = new Dictionary<string, string>();
    static string Hash(string p) { if (!File.Exists(p)) return null; using (var s = SHA256.Create()) return Convert.ToBase64String(s.ComputeHash(File.ReadAllBytes(p))); }
    public List<string> Run(Inst a, Inst b)
    {
        var said = new List<string>();
        var names = new HashSet<string>(Directory.GetFiles(a.Dir, "*.kpgw").Concat(Directory.GetFiles(b.Dir, "*.kpgw")).Select(Path.GetFileName));
        foreach (var n in names)
        {
            string pa = Path.Combine(a.Dir, n), pb = Path.Combine(b.Dir, n), ha = Hash(pa), hb = Hash(pb), hl;
            last.TryGetValue(n, out hl);
            if (ha == hb) { last[n] = ha; continue; }
            bool ca = ha != hl, cb = hb != hl;
            if (ca && !cb) { File.Copy(pa, pb, true); last[n] = ha; }
            else if (cb && !ca) { File.Copy(pb, pa, true); last[n] = hb; }
            else
            {
                bool aNewer = File.GetLastWriteTimeUtc(pa) >= File.GetLastWriteTimeUtc(pb);
                string win = aNewer ? pa : pb, lose = aNewer ? pb : pa;
                string conflict = Path.GetFileNameWithoutExtension(n) + ".sync-conflict-20261115-120000-DEVICE.kpgw";
                File.Copy(lose, Path.Combine(a.Dir, conflict), true); File.Copy(lose, Path.Combine(b.Dir, conflict), true);
                File.Copy(win, aNewer ? pb : pa, true);
                last[n] = Hash(win); last[conflict] = Hash(Path.Combine(a.Dir, conflict));
                said.Add("conflict on " + n + ": " + (aNewer ? a.Name : b.Name) + "'s version kept, the other renamed " + conflict);
            }
        }
        a.Load(); b.Load();
        return said;
    }
}

public static class Program
{
    static void Reset(string d) { if (Directory.Exists(d)) Directory.Delete(d, true); Directory.CreateDirectory(d); }
    static Dictionary<string, string> D(params string[] kv) { var d = new Dictionary<string, string>(); for (int i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1]; return d; }
    static void Say(string s) { Console.WriteLine(s); }
    static string Admits(Inst s, string fp)
    {
        if (!s.Serves) return s.Name + " " + s.State();
        var rec = s.Get("KPG.Client." + fp);
        bool revoked = s.Get("KPG.Revoked." + fp) != null;
        bool expired = Inst.Expiry[fp] < s.Clock;
        return s.Name + " serves; record " + (rec == null ? "absent" : rec == "" ? "emptied" : "present") + ", revocation " + (revoked ? "held" : "absent")
            + ", certificate " + (expired ? "expired" : "valid") + " by its clock: client " + ((rec != null && rec != "" && !revoked && !expired) ? "ADMITTED" : "refused");
    }

    public static int Main(string[] args)
    {
        string work = args[0];
        Say("# " + DateTime.UtcNow.ToString("u") + " " + Environment.Version + (Environment.Is64BitProcess ? " x64" : " x86"));
        var key = new byte[32]; for (int i = 0; i < 32; i++) key[i] = (byte)(i * 11 + 5);
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        Inst.Expiry["c1"] = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        Inst.Expiry["c2"] = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        // 1. One KeePass configuration on two computers (a portable KeePass copied, a roaming profile,
        // a cloned machine): both installations carry the same KPG.Writer.
        Say("");
        Say("## 1. two computers, one writer name, a synchronized folder");
        string d1 = Path.Combine(work, "s1-pc1"), d2 = Path.Combine(work, "s1-pc2"), d3 = Path.Combine(work, "s1-pc3");
        Reset(d1); Reset(d2); Reset(d3);
        const string W = "a1b2c3d4e5f60718";
        var pc1 = new Inst("pc1", d1, key, W, now); var pc2 = new Inst("pc2", d2, key, W, now);
        var sync = new Sync();
        pc1.Apply(D("KPG.Profile.reader", "1", "KPG.Client.c1", "record of c1, profiles reader"));
        sync.Run(pc1, pc2);
        Say("after pairing on pc1 and a sync: " + Admits(pc1, "c1") + " | " + Admits(pc2, "c1"));
        pc1.Apply(D("KPG.Revoked.c1", "2027-06-01T00:00:00Z a5", "KPG.Client.c1", ""));       // revoked on pc1
        System.Threading.Thread.Sleep(1100);
        pc2.Apply(D("KPG.Rule.00112233445566778899aabbccddeeff", "{\"reader\":[]}"));          // pc2 narrows a group, before the sync
        Say("before they meet: " + Admits(pc1, "c1") + " | " + Admits(pc2, "c1"));
        foreach (var s in sync.Run(pc1, pc2)) Say(s);
        Say("after the sync: " + Admits(pc1, "c1"));
        Say("after the sync: " + Admits(pc2, "c1"));
        foreach (var p in Directory.GetFiles(d1, "*.kpgw")) File.Copy(p, Path.Combine(d3, Path.GetFileName(p)), true);
        var pc3 = new Inst("pc3 (new, no seen file)", d3, key, "0f1e2d3c4b5a6978", now);
        Say(Admits(pc3, "c1"));
        // pc1's way out, as the Store page offers it, after KeePass was restarted: take a new name. What
        // it holds of its own is what the file under its old name holds, which is pc2's.
        var pc1b = new Inst("pc1 after Take a new name", d1, key, "77aa77aa77aa77aa", now);
        Say(Admits(pc1b, "c1"));
        Say("the revocation is now only in " + string.Join(", ", Directory.GetFiles(d1, "*sync-conflict*").Select(Path.GetFileName)) + ", which nothing reads");

        // 2. A computer whose clock runs two months ahead lets go of its own revocation and emptied record.
        Say("");
        Say("## 2. a fast clock lets go of a revocation while the issuer's record line still stands");
        string e1 = Path.Combine(work, "s2-a"), e2 = Path.Combine(work, "s2-b"); Reset(e1); Reset(e2);
        var a = new Inst("A", e1, key, "1111111111111111", now);
        var b = new Inst("B", e2, key, "2222222222222222", now);
        var sync2 = new Sync();
        a.Apply(D("KPG.Client.c2", "record of c2, notAfter 2026-12-31"));                       // A pairs c2
        sync2.Run(a, b);
        b.Apply(D("KPG.Revoked.c2", "2026-12-31T00:00:00Z a5", "KPG.Client.c2", ""));          // B revokes c2
        sync2.Run(a, b);
        Say("after the revocation meets A: " + Admits(a, "c2"));
        b.Clock = now.AddMonths(2);                                                             // B's clock jumps ahead
        b.Apply(D("KPG.Profile.ops", "1"));                                                     // any later write on B
        sync2.Run(a, b);
        Say("after B's next write, B's clock " + b.Clock.ToString("yyyy-MM-dd") + ": " + Admits(a, "c2"));
        string f1 = Path.Combine(work, "s2c-a"), f2 = Path.Combine(work, "s2c-b"); Reset(f1); Reset(f2);
        var ca = new Inst("A (control: B's clock right)", f1, key, "1111111111111111", now);
        var cb = new Inst("B", f2, key, "2222222222222222", now);
        var sync3 = new Sync();
        ca.Apply(D("KPG.Client.c2", "record of c2, notAfter 2026-12-31")); sync3.Run(ca, cb);
        cb.Apply(D("KPG.Revoked.c2", "2026-12-31T00:00:00Z a5", "KPG.Client.c2", "")); sync3.Run(ca, cb);
        cb.Apply(D("KPG.Profile.ops", "1")); sync3.Run(ca, cb);
        Say(Admits(ca, "c2"));

        // 3. One writer's file put back one version, on a computer with no seen file, when the other
        // writer has not written since it loaded the newer version.
        Say("");
        Say("## 3. one file rolled back one version, seen by a computer with no seen file");
        string g1 = Path.Combine(work, "s3-a"), g2 = Path.Combine(work, "s3-b"), g3 = Path.Combine(work, "s3-c");
        Reset(g1); Reset(g2); Reset(g3);
        var ra = new Inst("A", g1, key, "3333333333333333", now);
        var rb = new Inst("B", g2, key, "4444444444444444", now);
        var sync4 = new Sync();
        string rule = "KPG.Rule.ffeeddccbbaa99887766554433221100";
        rb.Apply(D("KPG.Profile.reader", "1")); sync4.Run(ra, rb);
        ra.Apply(D(rule, "{\"reader\":[\"read\",\"list\"]}")); sync4.Run(ra, rb);               // A opens a group
        string bOld = Path.Combine(g2, "4444444444444444.kpgw"); var older = File.ReadAllBytes(bOld);
        rb.Apply(D(rule, "{\"reader\":[]}")); sync4.Run(ra, rb);                                // B, knowing A's, closes it
        Say("A: " + ra.State() + ", rule " + ra.Get(rule) + " | B: " + rb.State() + ", rule " + rb.Get(rule));
        File.Copy(Path.Combine(g1, "3333333333333333.kpgw"), Path.Combine(g3, "3333333333333333.kpgw"));
        File.WriteAllBytes(Path.Combine(g3, "4444444444444444.kpgw"), older);                  // B's previous version
        var rc = new Inst("C (no seen file)", g3, key, "5555555555555555", now);
        Say("C: " + rc.State() + ", rule " + rc.Get(rule));
        File.WriteAllBytes(Path.Combine(g1, "4444444444444444.kpgw"), older); ra.Load();
        Say("A, which had loaded the newer version, given the same older file: " + ra.State());
        return 0;
    }
}
