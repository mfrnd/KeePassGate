// Probe for option-outside-file: a prototype of the store itself. One sealed file for each
// installation that writes, each holding what that installation asserts; the store is their join.
// Console only. Invented content. C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

public sealed class Entry
{
    public string K;
    public string V;            // null: taken away
    public long N;              // the writer's count when it wrote this
    public Dictionary<string, long> Saw = new Dictionary<string, long>();
    public string Writer;       // not stored; the file's
}

public sealed class WriterFile
{
    public string Store;
    public string Writer;
    public long Head;
    public Dictionary<string, long> Saw = new Dictionary<string, long>();
    public Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    public string Digest;       // of the sealed bytes, as read
}

public sealed class Image
{
    public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<string> Problems = new List<string>();
    public Dictionary<string, WriterFile> Files = new Dictionary<string, WriterFile>(StringComparer.Ordinal);
    public bool Serves { get { return Problems.Count == 0; } }
}

public static class Seal
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("KPGW1");
    static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static string StoreId(byte[] key)
    {
        using (var sha = SHA256.Create())
        {
            var pre = Encoding.ASCII.GetBytes("KPG/1/store/");
            var all = new byte[pre.Length + key.Length];
            Buffer.BlockCopy(pre, 0, all, 0, pre.Length);
            Buffer.BlockCopy(key, 0, all, pre.Length, key.Length);
            var d = sha.ComputeHash(all);
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++) sb.Append(d[i].ToString("x2"));
            return sb.ToString();
        }
    }

    static byte[] Aad(string store, string writer)
    {
        return Encoding.ASCII.GetBytes("KPG/1/writer/" + store + "/" + writer);
    }

    public static byte[] Close(byte[] key, string store, string writer, byte[] plain)
    {
        var nonce = new byte[12];
        Rng.GetBytes(nonce);
        var gcm = new GcmBlockCipher(new AesEngine());
        gcm.Init(true, new AeadParameters(new KeyParameter(key), 128, nonce, Aad(store, writer)));
        var body = new byte[gcm.GetOutputSize(plain.Length)];
        int n = gcm.ProcessBytes(plain, 0, plain.Length, body, 0);
        gcm.DoFinal(body, n);
        var all = new byte[Magic.Length + nonce.Length + body.Length];
        Buffer.BlockCopy(Magic, 0, all, 0, Magic.Length);
        Buffer.BlockCopy(nonce, 0, all, Magic.Length, nonce.Length);
        Buffer.BlockCopy(body, 0, all, Magic.Length + nonce.Length, body.Length);
        return all;
    }

    // null when the bytes are not this store's, not this writer's, or changed.
    public static byte[] Open(byte[] key, string store, string writer, byte[] all)
    {
        try
        {
            if (all.Length < Magic.Length + 12 + 16) return null;
            for (int i = 0; i < Magic.Length; i++) if (all[i] != Magic[i]) return null;
            var nonce = new byte[12];
            Buffer.BlockCopy(all, Magic.Length, nonce, 0, 12);
            var gcm = new GcmBlockCipher(new AesEngine());
            gcm.Init(false, new AeadParameters(new KeyParameter(key), 128, nonce, Aad(store, writer)));
            int off = Magic.Length + 12;
            var plain = new byte[gcm.GetOutputSize(all.Length - off)];
            int n = gcm.ProcessBytes(all, off, all.Length - off, plain, 0);
            gcm.DoFinal(plain, n);
            return plain;
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException) { return null; }
    }
}

public sealed class Store
{
    public readonly string Dir;
    public readonly byte[] Key;
    public readonly string Id;
    public readonly string Me;
    // What this installation has loaded before, kept outside the store: writer -> head.
    public readonly Dictionary<string, long> Journal = new Dictionary<string, long>();
    public Image Now;

    public Store(string dir, byte[] key, string me)
    {
        Dir = dir; Key = key; Me = me; Id = Seal.StoreId(key);
        Directory.CreateDirectory(dir);
        Load();
    }

    static bool AddOnly(string k) { return k.StartsWith("KPG.Revoked.", StringComparison.Ordinal) || k.StartsWith("KPG.RetiredAuthority.", StringComparison.Ordinal); }

    static int Refusing(string v)
    {
        if (v == null) return 0;
        if (v == "forever") return int.MaxValue;
        return 1;
    }

    public static WriterFile Parse(byte[] key, string store, string writer, byte[] sealedBytes)
    {
        var plain = Seal.Open(key, store, writer, sealedBytes);
        if (plain == null) return null;
        var o = JObject.Parse(Encoding.UTF8.GetString(plain));
        var f = new WriterFile();
        f.Store = (string)o["store"]; f.Writer = (string)o["writer"]; f.Head = (long)o["head"];
        if (f.Store != store || f.Writer != writer) return null;
        foreach (var p in (JObject)o["saw"]) f.Saw[p.Key] = (long)p.Value;
        foreach (JObject e in (JArray)o["entries"])
        {
            var x = new Entry();
            x.K = (string)e["k"]; x.V = e["v"].Type == JTokenType.Null ? null : (string)e["v"]; x.N = (long)e["n"]; x.Writer = writer;
            foreach (var p in (JObject)e["saw"]) x.Saw[p.Key] = (long)p.Value;
            f.Entries[x.K] = x;
        }
        using (var sha = SHA256.Create()) f.Digest = BitConverter.ToString(sha.ComputeHash(sealedBytes)).Replace("-", "");
        return f;
    }

    public void Load()
    {
        var img = new Image();
        foreach (var path in Directory.GetFiles(Dir, "*.kpgw"))
        {
            string w = Path.GetFileNameWithoutExtension(path);
            var f = Parse(Key, Id, w, File.ReadAllBytes(path));
            if (f == null) { img.Problems.Add("file of writer " + w + " does not open"); continue; }
            img.Files[w] = f;
        }
        // Every file names what its writer had seen of the others. None of them may be older than that.
        foreach (var f in img.Files.Values)
            foreach (var s in f.Saw)
            {
                WriterFile other;
                if (!img.Files.TryGetValue(s.Key, out other)) { if (s.Value > 0) img.Problems.Add("writer " + s.Key + " is missing; " + f.Writer + " saw it at " + s.Value); }
                else if (other.Head < s.Value) img.Problems.Add("writer " + s.Key + " is at " + other.Head + "; " + f.Writer + " saw it at " + s.Value);
            }
        // Nor older than this installation loaded before.
        foreach (var j in Journal)
        {
            WriterFile f;
            if (!img.Files.TryGetValue(j.Key, out f)) img.Problems.Add("writer " + j.Key + " is missing; this installation saw it at " + j.Value);
            else if (f.Head < j.Value) img.Problems.Add("writer " + j.Key + " is at " + f.Head + "; this installation saw it at " + j.Value);
        }
        // The join.
        var byKey = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        foreach (var f in img.Files.Values)
            foreach (var e in f.Entries.Values)
            {
                List<Entry> l;
                if (!byKey.TryGetValue(e.K, out l)) byKey[e.K] = l = new List<Entry>();
                l.Add(e);
            }
        foreach (var kv in byKey)
        {
            string k = kv.Key;
            if (AddOnly(k))
            {
                var held = kv.Value.FirstOrDefault(e => e.V != null);
                if (held != null) img.Values[k] = held.V;
                continue;
            }
            var live = kv.Value.Where(y => !kv.Value.Any(x => x != y && Knows(x, y))).ToList();
            if (live.Count == 1) { if (live[0].V != null) img.Values[k] = live[0].V; continue; }
            // Several that do not know of each other: the family's closed answer.
            if (k.StartsWith("KPG.Rule.", StringComparison.Ordinal)) img.Values[k] = "!two versions";
            else if (k.StartsWith("KPG.Profile.", StringComparison.Ordinal)) { if (live.All(e => e.V != null)) img.Values[k] = live[0].V; }
            else if (k.StartsWith("KPG.RenewalRefused.", StringComparison.Ordinal))
            {
                var most = live.OrderByDescending(e => Refusing(e.V)).ThenByDescending(e => e.V, StringComparer.Ordinal).First();
                if (most.V != null) img.Values[k] = most.V;
            }
            else if (k.StartsWith("KPG.Client.", StringComparison.Ordinal))
            {
                if (live.Any(e => string.IsNullOrEmpty(e.V))) img.Values[k] = string.Empty;
                else if (live.All(e => e.V == live[0].V)) img.Values[k] = live[0].V;
                else img.Values[k] = string.Empty;
            }
        }
        if (img.Serves) foreach (var f in img.Files.Values) Journal[f.Writer] = f.Head;
        Now = img;
    }

    static bool Knows(Entry x, Entry y)
    {
        long c;
        return x.Saw.TryGetValue(y.Writer, out c) && c >= y.N;
    }

    public string Get(string k) { string v; return Now.Values.TryGetValue(k, out v) ? v : null; }

    public void Set(string k, string v) { Write(k, v, false); }
    public void Remove(string k) { Write(k, null, true); }

    void Write(string k, string v, bool remove)
    {
        Load();
        if (!Now.Serves) throw new InvalidOperationException("the store is not in a state to be written: " + string.Join("; ", Now.Problems));
        WriterFile mine;
        if (!Now.Files.TryGetValue(Me, out mine)) { mine = new WriterFile(); mine.Store = Id; mine.Writer = Me; }
        var saw = new Dictionary<string, long>();
        foreach (var f in Now.Files.Values) if (f.Writer != Me) saw[f.Writer] = f.Head;
        mine.Head++;
        mine.Saw = saw;
        if (remove && AddOnly(k)) mine.Entries.Remove(k);       // this writer stops asserting it; nobody else's is touched
        else
        {
            var e = new Entry(); e.K = k; e.V = v; e.N = mine.Head; e.Saw = new Dictionary<string, long>(saw); e.Writer = Me;
            mine.Entries[k] = e;
        }
        // Let go of what another writer's entry already knows of.
        foreach (var dead in mine.Entries.Values.Where(y => !AddOnly(y.K) && Now.Files.Values.Any(f => f.Writer != Me && f.Entries.ContainsKey(y.K) && Knows(f.Entries[y.K], y))).Select(y => y.K).ToList())
            mine.Entries.Remove(dead);
        Put(mine);
        Load();
    }

    public static byte[] Bytes(byte[] key, WriterFile f)
    {
        var o = new JObject();
        o["v"] = "1"; o["store"] = f.Store; o["writer"] = f.Writer; o["head"] = f.Head;
        var s = new JObject(); foreach (var p in f.Saw) s[p.Key] = p.Value; o["saw"] = s;
        var a = new JArray();
        foreach (var e in f.Entries.Values.OrderBy(x => x.K, StringComparer.Ordinal))
        {
            var j = new JObject(); j["k"] = e.K; j["v"] = e.V == null ? JValue.CreateNull() : (JToken)e.V; j["n"] = e.N;
            var es = new JObject(); foreach (var p in e.Saw) es[p.Key] = p.Value; j["saw"] = es;
            a.Add(j);
        }
        o["entries"] = a;
        return Seal.Close(key, f.Store, f.Writer, Encoding.UTF8.GetBytes(o.ToString(Formatting.None)));
    }

    void Put(WriterFile f)
    {
        var bytes = Bytes(Key, f);
        string final = Path.Combine(Dir, f.Writer + ".kpgw");
        string temp = Path.Combine(Dir, "~" + f.Writer + ".tmp");
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        if (File.Exists(final)) File.Replace(temp, final, null);
        else File.Move(temp, final);
    }

    // What a file synchronizer does between two folders: each writer's file goes where it is newer.
    public static void Sync(Store a, Store b)
    {
        var names = new HashSet<string>();
        foreach (var p in Directory.GetFiles(a.Dir, "*.kpgw")) names.Add(Path.GetFileName(p));
        foreach (var p in Directory.GetFiles(b.Dir, "*.kpgw")) names.Add(Path.GetFileName(p));
        foreach (var n in names)
        {
            string pa = Path.Combine(a.Dir, n), pb = Path.Combine(b.Dir, n);
            long ha = Head(a, pa), hb = Head(b, pb);
            if (ha > hb) File.Copy(pa, pb, true);
            else if (hb > ha) File.Copy(pb, pa, true);
        }
        a.Load(); b.Load();
    }

    static long Head(Store s, string path)
    {
        if (!File.Exists(path)) return 0;
        var f = Parse(s.Key, s.Id, Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));
        return f == null ? -1 : f.Head;
    }
}

public static class StoreProbe
{
    static void Reset(string d) { if (Directory.Exists(d)) Directory.Delete(d, true); Directory.CreateDirectory(d); }

    static void Say(string what, bool ok) { Console.WriteLine((ok ? "ok    " : "WRONG ") + what); }

    static string Problems(Store s) { return s.Now.Serves ? "serves" : "closed: " + string.Join("; ", s.Now.Problems); }

    public static void Scenarios(string work)
    {
        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)(i * 7 + 3);
        string da = Path.Combine(work, "A"), db = Path.Combine(work, "B");
        string rule = "KPG.Rule.4f0c91aa22b7d31e8a6640c5b9e2f7a1";
        string client = "KPG.Client.aa11", revoked = "KPG.Revoked.aa11", refused = "KPG.RenewalRefused.aa11";

        // 1. One writer.
        Reset(da); Reset(db);
        var a = new Store(da, key, "a1b2c3d4e5f60718");
        a.Set("KPG.Profile.reader", "1"); a.Set(rule, "{\"reader\":[\"read\",\"list\"]}"); a.Set(client, "record-1");
        Say("1 one writer: three keys read back, one file", a.Get(rule) != null && a.Get(client) == "record-1" && Directory.GetFiles(da, "*.kpgw").Length == 1);
        a.Remove("KPG.Profile.reader");
        Say("1 one writer: a name taken off is off", a.Get("KPG.Profile.reader") == null);

        // 2. Two installations, a revocation made on one while the other is behind.
        var b = new Store(db, key, "0f1e2d3c4b5a6978");
        Store.Sync(a, b);
        Say("2 the second installation reads the first one's record", b.Get(client) == "record-1");
        b.Set(revoked, "2027-01-01T00:00:00Z"); b.Set(client, string.Empty);
        a.Set(refused, "2026-12-01T00:00:00Z");                    // A, not yet knowing of the revocation, writes something else
        Store.Sync(a, b);
        Say("2 after the folders meet, revoked on both", a.Get(revoked) != null && b.Get(revoked) != null && a.Get(client) == string.Empty && b.Get(client) == string.Empty);
        a.Remove(revoked);                                          // A lets go of a revocation it never asserted
        Store.Sync(a, b);
        Say("2 a revocation another installation asserts cannot be taken away from here", a.Get(revoked) != null && b.Get(revoked) != null);

        // 3. The same node's rule changed on both between two meetings.
        a.Set(rule, "{\"reader\":[]}"); b.Set(rule, "{\"reader\":[\"read\",\"list\",\"update\"]}");
        Store.Sync(a, b);
        Say("3 two versions that do not know of each other: the node refuses on both", a.Get(rule) == "!two versions" && b.Get(rule) == "!two versions");
        a.Set(rule, "{\"reader\":[]}");                            // the operator settles it on A
        Store.Sync(a, b);
        Say("3 settled on one, settled on both", a.Get(rule) == "{\"reader\":[]}" && b.Get(rule) == "{\"reader\":[]}");
        b.Set(rule, "{\"reader\":[\"list\"]}");                    // a later change that knows of the settled one
        Store.Sync(a, b);
        Say("3 a later change that knows of the earlier one takes over", a.Get(rule) == "{\"reader\":[\"list\"]}");

        // 4. The profile list: a name added on one and taken off on the other at once stays off.
        b.Set("KPG.Profile.ops", "1"); a.Remove("KPG.Profile.ops");
        Store.Sync(a, b);
        Say("4 added here, taken off there, neither knowing: off", a.Get("KPG.Profile.ops") == null && b.Get("KPG.Profile.ops") == null);
        b.Set("KPG.Profile.ops", "1");
        Store.Sync(a, b);
        Say("4 added again knowing of the removal: on", a.Get("KPG.Profile.ops") == "1");
        a.Set("KPG.Profile.x", "1"); b.Set("KPG.Profile.y", "1");
        Store.Sync(a, b);
        Say("4 a name added on each: both", a.Get("KPG.Profile.x") == "1" && a.Get("KPG.Profile.y") == "1" && b.Get("KPG.Profile.x") == "1");

        // 5. Standing answers: the more refusing of two that do not know of each other.
        a.Set(refused, "forever"); b.Remove(refused);
        Store.Sync(a, b);
        Say("5 forever here, lifted there, neither knowing: forever", a.Get(refused) == "forever" && b.Get(refused) == "forever");

        // 6. One writer's file put back as it was earlier.
        string bInA = Path.Combine(da, "0f1e2d3c4b5a6978.kpgw");
        var earlier = File.ReadAllBytes(bInA);
        b.Set("KPG.Revoked.bb22", "2027-02-01T00:00:00Z");
        Store.Sync(a, b);
        Say("6 before: A reads the new revocation", a.Get("KPG.Revoked.bb22") != null);
        File.WriteAllBytes(bInA, earlier); a.Load();
        Say("6 B's file put back as it was: A is " + Problems(a), !a.Now.Serves);
        File.Delete(bInA); a.Load();
        Say("6 B's file deleted: A is " + Problems(a), !a.Now.Serves);
        Store.Sync(a, b);
        Say("6 the file back as it should be: A " + Problems(a), a.Now.Serves && a.Get("KPG.Revoked.bb22") != null);

        // 7. The same, seen by an installation with no memory of its own, through what the other files say.
        a.Set("KPG.Profile.z", "1");                                // A's file now says it saw B at its present head
        Store.Sync(a, b);
        string dc = Path.Combine(work, "C"); Reset(dc);
        foreach (var p in Directory.GetFiles(da, "*.kpgw")) File.Copy(p, Path.Combine(dc, Path.GetFileName(p)));
        File.WriteAllBytes(Path.Combine(dc, "0f1e2d3c4b5a6978.kpgw"), earlier);
        var c = new Store(dc, key, "77aa77aa77aa77aa");
        Say("7 a new installation given an older file of B beside A's: " + Problems(c), !c.Now.Serves);

        // 8. Bytes changed, a file renamed, another key.
        var good = File.ReadAllBytes(bInA);
        var bad = (byte[])good.Clone(); bad[bad.Length / 2] ^= 1;
        File.WriteAllBytes(bInA, bad); a.Load();
        Say("8 one bit changed: " + Problems(a), !a.Now.Serves);
        File.WriteAllBytes(bInA, good); a.Load();
        File.Copy(bInA, Path.Combine(da, "deadbeefdeadbeef.kpgw")); a.Load();
        Say("8 B's file copied under another writer's name: " + Problems(a), !a.Now.Serves);
        File.Delete(Path.Combine(da, "deadbeefdeadbeef.kpgw")); a.Load();
        var other = new byte[32]; other[0] = 1;
        var w = new Store(da, other, "a1b2c3d4e5f60718");
        Say("8 opened with another key (and so another id): " + Problems(w), !w.Now.Serves);
        Say("8 and with the right key again: " + Problems(a), a.Now.Serves);
    }

    public static void Timing(string work)
    {
        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)(i * 5 + 1);
        string d = Path.Combine(work, "T"); Reset(d);
        string id = Seal.StoreId(key);
        var rnd = new Random(7);
        // Three writers: 200 rules, 30 records, 30 revocations, 10 names, a few answers, spread over them.
        var files = new List<WriterFile>();
        string[] writers = { "a1b2c3d4e5f60718", "0f1e2d3c4b5a6978", "1122334455667788" };
        foreach (var wn in writers) { var f = new WriterFile(); f.Store = id; f.Writer = wn; f.Head = 0; files.Add(f); }
        Action<string, string> put = (k, v) =>
        {
            var f = files[rnd.Next(3)];
            f.Head++;
            var e = new Entry(); e.K = k; e.V = v; e.N = f.Head; e.Writer = f.Writer;
            foreach (var o in files) if (o != f) e.Saw[o.Writer] = o.Head;
            f.Entries[k] = e;
        };
        for (int i = 0; i < 200; i++) put("KPG.Rule." + Guid.NewGuid().ToString("N"), "{\"default\":\"deny\",\"ops\":[\"read\",\"update\",\"list\"],\"reader\":[\"read\",\"list\"],\"agent\":[]}");
        for (int i = 0; i < 30; i++) put("KPG.Client." + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), new string('r', 819));
        for (int i = 0; i < 30; i++) put("KPG.Revoked." + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), new string('v', 85));
        for (int i = 0; i < 10; i++) put("KPG.Profile.name" + i, "1");
        foreach (var f in files) { foreach (var o in files) if (o != f) f.Saw[o.Writer] = o.Head; }
        long total = 0;
        foreach (var f in files)
        {
            var bytes = Store.Bytes(key, f);
            File.WriteAllBytes(Path.Combine(d, f.Writer + ".kpgw"), bytes);
            total += bytes.Length;
        }
        Console.WriteLine("three writer files, 270 keys, " + total + " bytes in all");

        var s = new Store(d, key, writers[0]);
        Console.WriteLine("state: " + (s.Now.Serves ? "serves" : string.Join("; ", s.Now.Problems)) + ", keys in the image " + s.Now.Values.Count);

        var sw = Stopwatch.StartNew();
        const int loads = 200;
        for (int i = 0; i < loads; i++) s.Load();
        sw.Stop();
        Console.WriteLine("a whole load (read three files, open, parse, check, join): " + (sw.Elapsed.TotalMilliseconds / loads).ToString("F3") + " ms");

        // The question asked on every request: has anything in the folder changed.
        var di = new DirectoryInfo(d);
        sw.Restart();
        const int looks = 20000;
        long acc = 0;
        for (int i = 0; i < looks; i++)
            foreach (var fi in di.EnumerateFiles("*.kpgw")) acc += fi.Length ^ fi.LastWriteTimeUtc.Ticks;
        sw.Stop();
        Console.WriteLine("names, lengths and times of the folder's files, per look: " + (sw.Elapsed.TotalMilliseconds * 1000 / looks).ToString("F1") + " microseconds (" + acc + ")");

        // A lookup in the image, which is what a rule read becomes.
        var keys = s.Now.Values.Keys.ToArray();
        sw.Restart();
        const int gets = 2000000;
        int hit = 0;
        for (int i = 0; i < gets; i++) if (s.Get(keys[i % keys.Length]) != null) hit++;
        sw.Stop();
        Console.WriteLine("a lookup in the image: " + (sw.Elapsed.TotalMilliseconds * 1000000 / gets).ToString("F0") + " nanoseconds");

        // A write: this writer's file sealed, written beside, flushed to the disk and put in place.
        sw.Restart();
        const int writes = 50;
        for (int i = 0; i < writes; i++) s.Set("KPG.Profile.t" + (i % 5), "1");
        sw.Stop();
        Console.WriteLine("a write (load, seal, write, flush, replace, load again): " + (sw.Elapsed.TotalMilliseconds / writes).ToString("F2") + " ms");

        // Does the folder's own time move when a file in it is replaced, and when one is written in place?
        var before = Directory.GetLastWriteTimeUtc(d);
        System.Threading.Thread.Sleep(1100);
        s.Set("KPG.Profile.t9", "1");
        var afterReplace = Directory.GetLastWriteTimeUtc(d);
        System.Threading.Thread.Sleep(1100);
        string third = Path.Combine(d, writers[2] + ".kpgw");
        var b3 = File.ReadAllBytes(third);
        using (var fs = new FileStream(third, FileMode.Open, FileAccess.Write)) { fs.Write(b3, 0, b3.Length); }
        var afterInPlace = Directory.GetLastWriteTimeUtc(d);
        Console.WriteLine("folder time moved by a replace: " + (afterReplace > before) + "; by a write in place: " + (afterInPlace > afterReplace)
            + "; the file's own time moved by the write in place: " + (File.GetLastWriteTimeUtc(third) > afterReplace));

        // Seal and open alone, for a file of a megabyte.
        var big = new byte[1024 * 1024]; rnd.NextBytes(big);
        sw.Restart();
        var sealedBig = Seal.Close(key, id, writers[0], big);
        var t1 = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var back = Seal.Open(key, id, writers[0], sealedBig);
        var t2 = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine("AES-256-GCM over 1 MiB: seal " + t1.ToString("F1") + " ms, open " + t2.ToString("F1") + " ms, same bytes back: " + big.SequenceEqual(back));
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("# " + string.Join(" ", args) + "  (" + Environment.Version + ", " + (Environment.Is64BitProcess ? "x64" : "x86") + ")");
        switch (args[0])
        {
            case "make": Kp.Make(args[1], ulong.Parse(args[2]), ulong.Parse(args[3]), uint.Parse(args[4])); return 0;
            case "kpmerge": Kp.KpMerge(args[1]); return 0;
            case "other": Kp2.Other(args[1]); return 0;
            case "describe": for (int i = 1; i < args.Length; i++) Console.WriteLine(Path.GetFileName(args[i]) + ": " + Kp.Describe(args[i])); return 0;
            case "scenarios": StoreProbe.Scenarios(args[1]); return 0;
            case "timing": StoreProbe.Timing(args[1]); return 0;
        }
        return 2;
    }
}
