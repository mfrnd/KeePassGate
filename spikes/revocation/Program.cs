using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

// Revocation probe. Throwaway databases only, all under the probe's own work directory.
static partial class Probe
{
    const string Rec = "KPG.Client.h1";
    const string Tomb = "KPG.Revoked.h1";
    const string Auth = "KPG.Authority";
    const string RetiredPrefix = "KPG.RetiredAuthority.";

    static readonly MethodInfo SetT = typeof(StringDictionaryEx).GetMethod("Set",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(string), typeof(string), typeof(DateTime?) }, null);
    static readonly MethodInfo GetT = typeof(StringDictionaryEx).GetMethod("GetLastModificationTime",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static string Work;
    static DateTime Now = Floor(DateTime.UtcNow);
    static DateTime T0 = Now.AddHours(-1);
    static int Counter;

    static DateTime Floor(DateTime t) { return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc); }
    static void Set(PwDatabase db, string k, string v, DateTime? t) { SetT.Invoke(db.CustomData, new object[] { k, v, t }); }
    static DateTime? Time(PwDatabase db, string k) { return (DateTime?)GetT.Invoke(db.CustomData, new object[] { k }); }
    static string Iso(DateTime t) { return t.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture); }

    static CompositeKey Key() { var k = new CompositeKey(); k.AddUserKey(new KcpPassword("probe-only")); return k; }

    static PwDatabase Open(string path)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        return db;
    }

    static PwDatabase RoundTrip(PwDatabase db)
    {
        var path = Path.Combine(Work, "rt" + (++Counter) + ".kdbx");
        db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        db.Close();
        return Open(path);
    }

    static string Base()
    {
        var path = Path.Combine(Work, "base.kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        var kdf = new AesKdf();
        var p = kdf.GetDefaultParameters();
        p.SetUInt64(AesKdf.ParamRounds, 1);
        db.KdfParameters = p;
        Set(db, Rec, "rec:" + Iso(T0), T0);
        Set(db, Auth, "X", T0);
        db.Save(null);
        db.Close();
        return path;
    }

    enum Design { Today, A, ARestamp, B, C }

    static DateTime MaxKpg(PwDatabase db)
    {
        var max = DateTime.MinValue;
        foreach (var kv in db.CustomData)
        {
            if (!kv.Key.StartsWith("KPG.", StringComparison.Ordinal)) continue;
            var t = Time(db, kv.Key);
            if (t.HasValue && t.Value > max) max = t.Value;
        }
        return max;
    }

    // Revoke the client and recreate the authority, on the revoking copy, at its own clock.
    static void Revoke(Design d, PwDatabase db, DateTime clock)
    {
        var t = clock;
        if (d == Design.B)
        {
            var seen = MaxKpg(db).AddSeconds(1);
            if (seen > t) t = seen;
        }
        switch (d)
        {
            case Design.Today:
            case Design.B:
                Set(db, Rec, "", t);
                Set(db, Auth, "Y", t);
                break;
            case Design.A:
            case Design.ARestamp:
                Set(db, Rec, "revoked:" + Iso(t), t);
                Set(db, Auth, "Y@" + Iso(t), t);
                break;
            case Design.C:
                Set(db, Rec, "", t);
                Set(db, Tomb, "revoked", t);
                Set(db, Auth, "Y", t);
                Set(db, RetiredPrefix + "X", "retired", t);
                break;
        }
    }

    // What the plugin could do to the database it has open before a merge: put back a time the
    // value carries, where the key has lost its own.
    static void Restamp(PwDatabase db)
    {
        foreach (var kv in db.CustomData.ToList())
        {
            if (!kv.Key.StartsWith("KPG.", StringComparison.Ordinal) || Time(db, kv.Key).HasValue) continue;
            var v = kv.Value;
            int at = v.IndexOfAny(new[] { ':', '@' });
            DateTime t;
            if (at >= 0 && DateTime.TryParse(v.Substring(at + 1), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                Set(db, kv.Key, v, t);
        }
    }

    static void Strip(PwDatabase db)
    {
        foreach (var kv in db.CustomData.ToList()) Set(db, kv.Key, kv.Value, null);
    }

    static bool Admitted(Design d, PwDatabase db)
    {
        var v = db.CustomData.Get(Rec);
        if (string.IsNullOrEmpty(v)) return false;
        if (v.StartsWith("revoked:", StringComparison.Ordinal)) return false;
        if (d == Design.C && db.CustomData.Exists(Tomb)) return false;
        return true;
    }

    static string Serves(Design d, PwDatabase db)
    {
        var v = db.CustomData.Get(Auth) ?? "(none)";
        var name = v.Split('@')[0];
        if (d == Design.C && db.CustomData.Exists(RetiredPrefix + name)) return "refuse";
        return name;
    }

    static void Main(string[] args)
    {
        Work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "revocation-probe-work");
        Directory.CreateDirectory(Work);
        if (args.Length > 1 && args[1] == "rules") { Rules(); return; }
        if (args.Length > 1 && args[1] == "layout") { Layout(); return; }

        var basePath = Base();
        var conditions = new[] { "kept", "skew", "stripRevoker", "stripHolder", "stripBoth", "stripBoth+holderSettingsNewer", "stripBoth+revokerSettingsNewer" };
        var methods = new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids };
        var designs = (Design[])Enum.GetValues(typeof(Design));

        Console.WriteLine("direction\tmethod\tcondition\t" + string.Join("\t", designs.Select(x => x + ":client/authority")) + "\ttombstoneKept\tkeys");
        foreach (var into in new[] { "intoRevoker", "intoHolder" })
            foreach (var m in methods)
                foreach (var c in conditions)
                {
                    var cells = new List<string>();
                    string tombKept = "-", keys = "-";
                    foreach (var d in designs)
                    {
                        var revoker = Open(basePath);
                        var holder = Open(basePath);
                        // The holder also went on being used, which is why a revocation can lose.
                        var clock = c == "skew" ? T0.AddSeconds(-60) : Now;
                        Revoke(d, revoker, clock);
                        revoker = RoundTrip(revoker);
                        if (c == "stripRevoker" || c.StartsWith("stripBoth")) Strip(revoker);
                        if (c == "stripHolder" || c.StartsWith("stripBoth")) Strip(holder);
                        if (c.EndsWith("holderSettingsNewer")) holder.SettingsChanged = Now.AddMinutes(5);
                        if (c.EndsWith("revokerSettingsNewer")) revoker.SettingsChanged = Now.AddMinutes(5);
                        revoker = RoundTrip(revoker);
                        holder = RoundTrip(holder);

                        var target = into == "intoRevoker" ? revoker : holder;
                        var source = into == "intoRevoker" ? holder : revoker;
                        if (d == Design.ARestamp) Restamp(target);
                        target.MergeIn(source, m);
                        target = RoundTrip(target);

                        cells.Add((Admitted(d, target) ? "ADMIT" : "revoked") + "/" + Serves(d, target));
                        if (d == Design.C)
                        {
                            tombKept = (target.CustomData.Exists(Tomb) ? "yes" : "no") + "," + (target.CustomData.Exists(RetiredPrefix + "X") ? "yes" : "no");
                            keys = target.CustomData.Count.ToString();
                        }
                        target.Close();
                        source.Close();
                    }
                    Console.WriteLine(into + "\t" + m + "\t" + c + "\t" + string.Join("\t", cells) + "\t" + tombKept + "\t" + keys);
                }
    }

    // The merge rule for PwDatabase.CustomData, one key at a time, for every method.
    static void Rules()
    {
        var basePath = Path.Combine(Work, "rules.kdbx");
        var db0 = new PwDatabase();
        db0.New(IOConnectionInfo.FromPath(basePath), Key());
        var p = new AesKdf().GetDefaultParameters(); p.SetUInt64(AesKdf.ParamRounds, 1); db0.KdfParameters = p;
        db0.Save(null); db0.Close();

        var tOld = T0; var tNew = Now;
        var cases = new[]
        {
            // name, target (value,time) or null, source (value,time) or null
            new object[] { "source only, stamped",          null, Tuple.Create("S", (DateTime?)tNew) },
            new object[] { "source only, unstamped",        null, Tuple.Create("S", (DateTime?)null) },
            new object[] { "target only, stamped",          Tuple.Create("T", (DateTime?)tNew), null },
            new object[] { "both stamped, source newer",    Tuple.Create("T", (DateTime?)tOld), Tuple.Create("S", (DateTime?)tNew) },
            new object[] { "both stamped, target newer",    Tuple.Create("T", (DateTime?)tNew), Tuple.Create("S", (DateTime?)tOld) },
            new object[] { "both stamped, equal",           Tuple.Create("T", (DateTime?)tNew), Tuple.Create("S", (DateTime?)tNew) },
            new object[] { "target stamped only (old)",     Tuple.Create("T", (DateTime?)tOld), Tuple.Create("S", (DateTime?)null) },
            new object[] { "source stamped only (old)",     Tuple.Create("T", (DateTime?)null), Tuple.Create("S", (DateTime?)tOld) },
            new object[] { "neither stamped, settings equal", Tuple.Create("T", (DateTime?)null), Tuple.Create("S", (DateTime?)null) },
            new object[] { "neither stamped, source settings newer", Tuple.Create("T", (DateTime?)null), Tuple.Create("S", (DateTime?)null), "srcSettings" },
        };
        var methods = new[] { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids };
        Console.WriteLine("case\t" + string.Join("\t", methods));
        foreach (var cs in cases)
        {
            var cells = new List<string>();
            foreach (var m in methods)
            {
                var t = Open(basePath); var s = Open(basePath);
                var tv = cs[1] as Tuple<string, DateTime?>; var sv = cs[2] as Tuple<string, DateTime?>;
                if (tv != null) Set(t, "K", tv.Item1, tv.Item2);
                if (sv != null) Set(s, "K", sv.Item1, sv.Item2);
                if (cs.Length > 3) s.SettingsChanged = Now.AddMinutes(5);
                t.MergeIn(s, m);
                var v = t.CustomData.Get("K");
                var tm = Time(t, "K");
                cells.Add(v == null ? "absent" : v + (tm.HasValue ? (tm.Value == tNew ? "@new" : tm.Value == tOld ? "@old" : "@?") : "@none"));
                t.Close(); s.Close();
            }
            Console.WriteLine(cs[0] + "\t" + string.Join("\t", cells));
        }
    }
}
