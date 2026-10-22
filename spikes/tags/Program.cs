using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

// What a version of a group or an entry could be made of. Throwaway databases only, created in
// memory under a path inside the probe's own work directory and never saved there.
static class Probe
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Probe.exe <work directory> [times|survey|binary|cost]");
            return 2;
        }

        var work = args[0];
        Directory.CreateDirectory(work);
        var question = args.Length > 1 ? args[1] : "times";

        switch (question)
        {
            case "times": Clock(); Storage(work); Merge(work); return 0;
            case "survey": Survey(typeof(KeePass.Program).Assembly.Location); return 0;
            case "binary": Binary(); return 0;
            case "cost": Cost(); return 0;
            default: Console.Error.WriteLine("no such question: " + question); return 2;
        }
    }

    // How finely the clock `Touch` stamps from moves.
    static void Clock()
    {
        var seen = new List<long>();
        long last = DateTime.UtcNow.Ticks;
        var until = DateTime.UtcNow.AddSeconds(1);
        while (seen.Count < 200 && DateTime.UtcNow < until)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now != last) { seen.Add(now - last); last = now; }
        }
        seen.Sort();
        Console.WriteLine("clock: {0} steps in 1 s; smallest {1:F4} ms, median {2:F4} ms, largest {3:F4} ms",
            seen.Count, seen[0] / 1e4, seen[seen.Count / 2] / 1e4, seen[seen.Count - 1] / 1e4);
    }

    static PwDatabase NewDb(string path)
    {
        var db = new PwDatabase();
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword("probe-only"));
        db.New(IOConnectionInfo.FromPath(path), key);
        return db;
    }

    // A KDBX 4 save read back, in memory, as a second machine's copy of the file is.
    static PwDatabase RoundTrip(PwDatabase db)
    {
        var ms = new MemoryStream();
        new KdbxFile(db).Save(ms, null, KdbxFormat.Default, null);
        var back = new PwDatabase();
        back.MasterKey = db.MasterKey;
        back.RootGroup = new PwGroup(true, true);
        var f = new KdbxFile(back);
        f.Load(new MemoryStream(ms.ToArray()), KdbxFormat.Default, null);
        return back;
    }

    // What a save keeps of the time, and whether two changes back to back can share one.
    static void Storage(string dir)
    {
        var db = NewDb(Path.Combine(dir, "a.kdbx"));
        var e = new PwEntry(true, true);
        db.RootGroup.AddEntry(e, true);
        e.Strings.Set("Title", new ProtectedString(false, "x"));
        e.Touch(true, false);
        long before = e.LastModificationTime.Ticks;
        var back = RoundTrip(db);
        var e2 = back.RootGroup.FindEntry(e.Uuid, true);
        Console.WriteLine("kdbx: in memory {0} ({1}), read back {2} ({3}), lost {4:F4} ms",
            before, e.LastModificationTime.Kind, e2.LastModificationTime.Ticks,
            e2.LastModificationTime.Kind, (before - e2.LastModificationTime.Ticks) / 1e4);

        int same = 0;
        for (int i = 0; i < 1000; i++)
        {
            e.Touch(true, false); long a = e.LastModificationTime.Ticks;
            e.Strings.Set("Title", new ProtectedString(false, "y" + i));
            e.Touch(true, false); if (e.LastModificationTime.Ticks == a) same++;
        }
        Console.WriteLine("back-to-back touches with the same time: {0} of 1000", same);
    }

    // What a synchronize does to the time and the strings, and what a move stamps.
    static void Merge(string dir)
    {
        var target = NewDb(Path.Combine(dir, "t.kdbx"));
        var e = new PwEntry(true, true);
        target.RootGroup.AddEntry(e, true);
        e.Strings.Set("Title", new ProtectedString(false, "local"));
        e.Touch(true, false);
        var source = RoundTrip(target);
        var s = source.RootGroup.FindEntry(e.Uuid, true);
        System.Threading.Thread.Sleep(1100);
        s.Strings.Set("Title", new ProtectedString(false, "remote"));
        s.Touch(true, false);
        long localBefore = e.LastModificationTime.Ticks;
        target.MergeIn(source, PwMergeMethod.Synchronize, null);
        var m = target.RootGroup.FindEntry(e.Uuid, true);
        Console.WriteLine("merge, newer remote: title {0}, time moved {1}, now the remote's {2}",
            m.Strings.ReadSafe("Title"), m.LastModificationTime.Ticks != localBefore,
            m.LastModificationTime.Ticks == s.LastModificationTime.Ticks);

        // The other copy changed, with an older time, as a slow clock gives it.
        var source2 = RoundTrip(target);
        var s2 = source2.RootGroup.FindEntry(e.Uuid, true);
        s2.Strings.Set("Title", new ProtectedString(false, "older-remote"));
        s2.LastModificationTime = m.LastModificationTime.AddSeconds(-30);
        long before2 = m.LastModificationTime.Ticks;
        target.MergeIn(source2, PwMergeMethod.Synchronize, null);
        var m2 = target.RootGroup.FindEntry(e.Uuid, true);
        Console.WriteLine("merge, older remote: title {0}, time moved {1}",
            m2.Strings.ReadSafe("Title"), m2.LastModificationTime.Ticks != before2);

        // The other copy changed, at the very same time.
        var source3 = RoundTrip(target);
        var s3 = source3.RootGroup.FindEntry(e.Uuid, true);
        var t3 = target.RootGroup.FindEntry(e.Uuid, true);
        s3.Strings.Set("Title", new ProtectedString(false, "same-time-remote"));
        s3.LastModificationTime = t3.LastModificationTime;
        long before3 = t3.LastModificationTime.Ticks;
        target.MergeIn(source3, PwMergeMethod.Synchronize, null);
        var m3 = target.RootGroup.FindEntry(e.Uuid, true);
        Console.WriteLine("merge, equal times: title {0}, time moved {1}",
            m3.Strings.ReadSafe("Title"), m3.LastModificationTime.Ticks != before3);

        // The other copy moved the entry to another group.
        var source4 = RoundTrip(target);
        var g = new PwGroup(true, true); g.Name = "G";
        source4.RootGroup.AddGroup(g, true);
        var s4 = source4.RootGroup.FindEntry(e.Uuid, true);
        System.Threading.Thread.Sleep(1100);
        source4.RootGroup.Entries.Remove(s4); g.AddEntry(s4, true, true);
        var t4 = target.RootGroup.FindEntry(e.Uuid, true);
        long mod4 = t4.LastModificationTime.Ticks, loc4 = t4.LocationChanged.Ticks;
        target.MergeIn(source4, PwMergeMethod.Synchronize, null);
        var m4 = target.RootGroup.FindEntry(e.Uuid, true);
        Console.WriteLine("merge, moved remote: now in {0}, modified moved {1}, location moved {2}",
            m4.ParentGroup.Name, m4.LastModificationTime.Ticks != mod4, m4.LocationChanged.Ticks != loc4);

        // A move inside one database, as the main window makes one.
        var h = new PwGroup(true, true); h.Name = "H";
        target.RootGroup.AddGroup(h, true);
        long loc5 = m4.LocationChanged.Ticks, mod5 = m4.LastModificationTime.Ticks;
        System.Threading.Thread.Sleep(20);
        m4.ParentGroup.Entries.Remove(m4); h.AddEntry(m4, true, true);
        Console.WriteLine("local move: modified moved {0}, location moved {1}",
            m4.LastModificationTime.Ticks != mod5, m4.LocationChanged.Ticks != loc5);
    }

    // Every method in KeePass.exe and KeePassLib that changes something a route would serve of an
    // entry or a group, read off the IL, and whether the same method moves the modification time.
    // Per method: a caller may stamp what the method itself does not, so NOTOUCH is a lead to read,
    // not a verdict.
    static void Survey(string exe)
    {
        var asm = Assembly.LoadFrom(exe);
        var ops = new Dictionary<short, OpCode>();
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        { var o = (OpCode)f.GetValue(null); ops[o.Value] = o; }

        var mutators = new HashSet<string> {
            "KeePassLib.Collections.ProtectedStringDictionary.Set",
            "KeePassLib.Collections.ProtectedStringDictionary.Remove",
            "KeePassLib.Collections.ProtectedBinaryDictionary.Set",
            "KeePassLib.Collections.ProtectedBinaryDictionary.Remove",
            "KeePassLib.PwEntry.set_Expires", "KeePassLib.PwEntry.set_ExpiryTime",
            "KeePassLib.PwEntry.set_Strings", "KeePassLib.PwEntry.set_Binaries",
            "KeePassLib.PwEntry.AssignProperties",
            "KeePassLib.PwGroup.set_Name", "KeePassLib.PwGroup.set_Notes",
            "KeePassLib.PwGroup.AddEntry", "KeePassLib.PwGroup.AddGroup",
            "KeePassLib.PwGroup.AssignProperties",
            "KeePassLib.Collections.PwObjectList`1.Remove",
        };
        var touches = new HashSet<string> { "KeePassLib.PwEntry.Touch", "KeePassLib.PwGroup.Touch",
            "KeePassLib.PwEntry.set_LastModificationTime", "KeePassLib.PwGroup.set_LastModificationTime",
            "KeePassLib.PwEntry.CreateBackup" };
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
        foreach (var t in types.OrderBy(x => x.FullName))
        foreach (var m in t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            MethodBody body; try { body = m.GetMethodBody(); } catch { continue; }
            if (body == null) continue;
            var il = body.GetILAsByteArray();
            var calls = new List<string>();
            int i = 0;
            while (i < il.Length)
            {
                OpCode op; short v = il[i];
                if (il[i] == 0xFE && i + 1 < il.Length) { v = (short)(0xFE00 | il[i + 1]); i += 2; } else i += 1;
                if (!ops.TryGetValue(v, out op)) break;
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: size = 4; break;
                }
                if (op.OperandType == OperandType.InlineMethod)
                {
                    try
                    {
                        var mb = m.Module.ResolveMethod(BitConverter.ToInt32(il, i),
                            t.IsGenericType ? t.GetGenericArguments() : null,
                            m.IsGenericMethod ? m.GetGenericArguments() : null);
                        var dt = mb.DeclaringType;
                        var name = (dt.IsGenericType ? dt.GetGenericTypeDefinition().FullName : dt.FullName) + "." + mb.Name;
                        calls.Add(name);
                    }
                    catch { }
                }
                i += size;
            }
            var hit = calls.Where(c => mutators.Contains(c)).Distinct().ToList();
            if (hit.Count == 0) continue;
            bool touched = calls.Any(c => touches.Contains(c));
            Console.WriteLine("{0} {1}.{2}: {3}", touched ? "touch  " : "NOTOUCH", t.FullName, m.Name, string.Join(", ", hit.Select(h => h.Substring(h.LastIndexOf('.', h.LastIndexOf('.') - 1) + 1))));
        }
    }

    // Whether an attachment can change once it is made: its fields, and every method it declares.
    static void Binary()
    {
        var t = typeof(ProtectedBinary);
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Console.WriteLine("field {0} {1} readonly={2}", f.FieldType.Name, f.Name, f.IsInitOnly);
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            Console.WriteLine("method {0} {1}", m.IsPublic ? "public " : "private", m.Name);
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            Console.WriteLine("property {0} settable={1}", p.Name, p.CanWrite);
    }

    static double Time(Action a, int n)
    {
        a(); var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) a();
        return sw.Elapsed.TotalMilliseconds / n;
    }

    // What an HMAC over an entry, an attachment and a group costs, beside decrypting the attachment.
    static void Cost()
    {
        var key = new byte[32]; new RNGCryptoServiceProvider().GetBytes(key);
        var e = new PwEntry(true, true);
        e.Strings.Set("Title", new ProtectedString(false, "an invented service"));
        e.Strings.Set("UserName", new ProtectedString(false, "agent-fictitious"));
        e.Strings.Set("Password", new ProtectedString(true, "not-a-real-password"));
        e.Strings.Set("URL", new ProtectedString(false, "https://invented.example"));
        e.Strings.Set("Notes", new ProtectedString(false, new string('n', 2000)));
        for (int i = 0; i < 5; i++) e.Strings.Set("Custom" + i, new ProtectedString(i % 2 == 0, "value " + i));

        Func<HashAlgorithm, byte[]> strings = h => {
            foreach (var kv in e.Strings) {
                var k = Encoding.UTF8.GetBytes(kv.Key); h.TransformBlock(k, 0, k.Length, null, 0);
                var v = kv.Value.ReadUtf8(); h.TransformBlock(v, 0, v.Length, null, 0);
            }
            h.TransformFinalBlock(new byte[0], 0, 0); return h.Hash; };
        Console.WriteLine("entry strings only (10 strings, 2 KB notes): HMACSHA256 {0:F4} ms",
            Time(() => { using (var h = new HMACSHA256(key)) strings(h); }, 2000));

        var rnd = new Random(1);
        foreach (int mb in new[] { 1, 10, 100 })
        {
            var data = new byte[mb << 20]; rnd.NextBytes(data);
            var plain = new ProtectedBinary(false, data);
            var guarded = new ProtectedBinary(true, data);
            int n = mb == 100 ? 3 : 20;
            double read = Time(() => guarded.ReadData(), n);
            double hm = Time(() => { using (var h = new HMACSHA256(key)) h.ComputeHash(plain.ReadData()); }, n);
            double cng = Time(() => { using (var h = new SHA256Cng()) h.ComputeHash(plain.ReadData()); }, n);
            double man = Time(() => { using (var h = new SHA256Managed()) h.ComputeHash(plain.ReadData()); }, n);
            Console.WriteLine("{0,3} MB attachment: protected ReadData {1,8:F2} ms, HMACSHA256 {2,8:F2} ms ({5:F0} MB/s), SHA256Cng {3,8:F2} ms ({6:F0} MB/s), SHA256Managed {4,8:F2} ms",
                mb, read, hm, cng, man, mb * 1000 / hm, mb * 1000 / cng);
        }
        var g = new PwGroup(true, true); g.Name = "Invented"; g.Notes = new string('g', 500);
        Console.WriteLine("group (name, 500 B notes, parent): HMACSHA256 {0:F4} ms",
            Time(() => { using (var h = new HMACSHA256(key)) h.ComputeHash(Encoding.UTF8.GetBytes(g.Name + "\0" + g.Notes + "\0" + g.Uuid.ToHexString())); }, 5000));
    }
}
