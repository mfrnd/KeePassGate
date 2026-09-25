using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Serialization;

// Layout "2c amended": KPG.Authority.<fp> per authority, KPG.RetiredAuthority.<fp> constant and
// never removed, prune deletes a document whose retirement exists, the gate serves the single
// unretired authority, more than one means the operator dialog.
static partial class Probe
{
    const string APre = "KPG.Authority.";
    const string RPre = "KPG.RetiredAuthority.";
    static string AK(string fp) { return APre + fp; }
    static string RK(string fp) { return RPre + fp; }

    static IEnumerable<string> Docs(PwDatabase db) { return db.CustomData.Select(kv => kv.Key).Where(k => k.StartsWith(APre, StringComparison.Ordinal)).Select(k => k.Substring(APre.Length)).ToList(); }
    static IEnumerable<string> Retired(PwDatabase db) { return db.CustomData.Select(kv => kv.Key).Where(k => k.StartsWith(RPre, StringComparison.Ordinal)).Select(k => k.Substring(RPre.Length)).ToList(); }
    static SortedSet<string> Unretired(PwDatabase db) { return new SortedSet<string>(Docs(db).Where(fp => !db.CustomData.Exists(RK(fp)))); }

    static void Create(PwDatabase db, string fp, DateTime t) { Set(db, AK(fp), "doc:" + fp, t); }
    static void RetireAll(PwDatabase db, DateTime t, string except)
    {
        foreach (var fp in Unretired(db)) if (fp != except) { Set(db, RK(fp), "retired", t); db.CustomData.Remove(AK(fp)); }
    }
    static void Recreate(PwDatabase db, string fp, DateTime t) { RetireAll(db, t, null); Create(db, fp, t); }
    static void DeleteAuthority(PwDatabase db, DateTime t) { RetireAll(db, t, null); }
    static void PickAuthority(PwDatabase db, string fp, DateTime t) { RetireAll(db, t, fp); }
    static int Prune(PwDatabase db)
    {
        int n = 0;
        foreach (var fp in Docs(db)) if (db.CustomData.Exists(RK(fp))) { db.CustomData.Remove(AK(fp)); n++; }
        return n;
    }

    static string Show(IEnumerable<string> s) { var l = s.ToList(); return l.Count == 0 ? "{}" : "{" + string.Join(",", l) + "}"; }
    static string Decide(SortedSet<string> s) { return s.Count == 1 ? "serve " + s.First() : s.Count == 0 ? "none" : "dialog(" + s.Count + ")"; }

    static string LBase(string name)
    {
        var path = Path.Combine(Work, name + ".kdbx");
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        var p = new AesKdf().GetDefaultParameters(); p.SetUInt64(AesKdf.ParamRounds, 1); db.KdfParameters = p;
        Create(db, "X", T0);
        db.Save(null); db.Close();
        return path;
    }

    static PwDatabase Snapshot(PwDatabase db, string name)
    {
        var path = Path.Combine(Work, name + ".kdbx");
        db.SaveAs(IOConnectionInfo.FromPath(path), false, null);
        db.Close();
        return Open(path);
    }

    static readonly PwMergeMethod[] Methods = { PwMergeMethod.Synchronize, PwMergeMethod.OverwriteIfNewer, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting, PwMergeMethod.CreateNewUuids };

    // Merge source into target, save, prune on the open copy, save. Returns the decision line.
    static string MergeAndDecide(PwDatabase target, PwDatabase source, PwMergeMethod m, out PwDatabase result, out string detail)
    {
        var ideal = new SortedSet<string>(Docs(target).Union(Docs(source)).Except(Retired(target).Union(Retired(source))));
        var before = Unretired(target);
        target.MergeIn(source, m);
        target = RoundTrip(target);
        var docsAfterMerge = Show(Docs(target).OrderBy(x => x));
        var pruned = Prune(target);
        target = RoundTrip(target);
        var after = Unretired(target);
        string verdict;
        if (after.SetEquals(ideal)) verdict = ideal.Count == 1 ? "one, correct" : ideal.Count == 0 ? "none, correct" : "more than one, dialog";
        else if (after.SetEquals(before)) verdict = "not learned";
        else if (after.Count == 1) verdict = "WRONG ONE";
        else verdict = "WRONG SET";
        detail = "docsAfterMerge=" + docsAfterMerge + " pruned=" + pruned + " ideal=" + Show(ideal);
        result = target;
        return Show(after) + "\t" + Decide(after) + "\t" + verdict + "\t" + target.CustomData.Count;
    }

    static void Layout()
    {
        var basePath = LBase("lbase");
        var conditions = new[] { "kept", "skew", "stripP", "stripQ", "stripBoth", "stripBoth+PsettingsNewer", "stripBoth+QsettingsNewer" };
        // P is the copy that acted, Q the other one.
        var scenarios = new Dictionary<string, Action<PwDatabase, PwDatabase, DateTime>>
        {
            { "a recreate on P", (P, Q, t) => Recreate(P, "Y", t) },
            { "b both recreate", (P, Q, t) => { Recreate(P, "Y", t); Recreate(Q, "Z", Now); } },
            { "d delete on P", (P, Q, t) => DeleteAuthority(P, t) },
            { "e pick Y on P, loser Q", (P, Q, t) => {
                Recreate(P, "Y", t); Recreate(Q, "Z", Now);
                var q2 = Open(basePath); Recreate(q2, "Z", Now); // the same Z as Q's: same key, same value, same time
                P.MergeIn(q2, PwMergeMethod.Synchronize); q2.Close(); Prune(P);
                PickAuthority(P, "Y", t.AddSeconds(1)); } },
            { "f recreate twice on P, stale Q", (P, Q, t) => { Recreate(P, "Y", t); Recreate(P, "W", t.AddSeconds(1)); } },
        };

        Console.WriteLine("scenario\tdirection\tmethod\tcondition\tunretired after prune\tdecision\tverdict\tkeys\tdetail");
        foreach (var sc in scenarios)
            foreach (var into in new[] { "into P", "into Q" })
                foreach (var m in Methods)
                    foreach (var c in conditions)
                    {
                        var P = Open(basePath); var Q = Open(basePath);
                        var clock = c == "skew" ? T0.AddSeconds(-60) : Now;
                        sc.Value(P, Q, clock);
                        P = RoundTrip(P); Q = RoundTrip(Q);
                        if (c == "stripP" || c.StartsWith("stripBoth")) Strip(P);
                        if (c == "stripQ" || c.StartsWith("stripBoth")) Strip(Q);
                        if (c.EndsWith("PsettingsNewer")) P.SettingsChanged = Now.AddMinutes(5);
                        if (c.EndsWith("QsettingsNewer")) Q.SettingsChanged = Now.AddMinutes(5);
                        P = RoundTrip(P); Q = RoundTrip(Q);
                        var target = into == "into P" ? P : Q;
                        var source = into == "into P" ? Q : P;
                        PwDatabase r; string detail;
                        var line = MergeAndDecide(target, source, m, out r, out detail);
                        Console.WriteLine(sc.Key + "\t" + into + "\t" + m + "\t" + c + "\t" + line + "\t" + detail);
                        r.Close(); source.Close();
                    }

        // (c) three copies recreate independently, merged pairwise in every order, two shapes.
        Console.WriteLine();
        Console.WriteLine("c three copies\tmethod\torder\tstep1\tstep2\tthen pick + merge each loser both ways");
        var fps = new[] { "Y", "Z", "W" };
        var perms = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        foreach (var m in Methods)
            foreach (var pm in perms)
                foreach (var shape in new[] { "p<-q, p<-r", "p<-q, r<-p" })
                {
                    Func<int, PwDatabase> make = i => { var d = Open(basePath); Recreate(d, fps[i], Now.AddSeconds(i)); return RoundTrip(d); };
                    var p = make(pm[0]); var q = make(pm[1]); var r = make(pm[2]);
                    string d1, d2;
                    PwDatabase s1, s2;
                    var l1 = MergeAndDecide(p, q, m, out s1, out d1);
                    string l2;
                    if (shape == "p<-q, p<-r") { l2 = MergeAndDecide(s1, r, m, out s2, out d2); }
                    else { l2 = MergeAndDecide(r, s1, m, out s2, out d2); }
                    // The operator picks the lowest unretired, if the dialog is shown, then each
                    // loser (fresh from its own recreate) is merged in both directions.
                    var set = Unretired(s2);
                    var pick = "-";
                    var loserResults = new List<string>();
                    if (set.Count > 1)
                    {
                        pick = set.First();
                        PickAuthority(s2, pick, Now.AddSeconds(10));
                        s2 = RoundTrip(s2);
                        var pickedPath = Path.Combine(Work, "c-picked.kdbx");
                        s2.SaveAs(IOConnectionInfo.FromPath(pickedPath), false, null);
                        foreach (var i in Enumerable.Range(0, 3).Where(i => fps[i] != pick))
                        {
                            foreach (var dir in new[] { "loser into pick", "pick into loser" })
                            {
                                var loser = make(i); var picked = Open(pickedPath);
                                PwDatabase res; string dd;
                                var t = dir == "loser into pick" ? picked : loser;
                                var s = dir == "loser into pick" ? loser : picked;
                                var ln = MergeAndDecide(t, s, m, out res, out dd);
                                var parts = ln.Split('\t');
                                loserResults.Add(fps[i] + " " + dir + ": " + parts[0] + " " + parts[2] + " keys=" + parts[3]);
                                res.Close(); s.Close();
                            }
                        }
                    }
                    Console.WriteLine("c\t" + m + "\t" + string.Join("", pm.Select(i => fps[i])) + " " + shape + "\t" + l1.Replace('\t', ' ') + "\t" + l2.Replace('\t', ' ') + "\tpick=" + pick + "; " + string.Join("; ", loserResults));
                    s2.Close();
                    q.Close(); if (shape == "p<-q, p<-r") r.Close(); else s1.Close();
                }

        // DatabaseOperationsForm: snapshot on load (CloneDeep), write back on Close when the key
        // count differs (KeePass 2.61.1 source, `KeePass/Forms/DatabaseOperationsForm.cs:75` and
        // `:87-90`). Modeled on the real types.
        Console.WriteLine();
        Console.WriteLine("form case\tsnapshot at open\tlive before close\twrite-back\tafter close\tafter next prune");
        FormCase("prune while open (retirement already there)", db => { Create(db, "Y", Now); Set(db, RK("X"), "retired", Now); }, db => Prune(db), null);
        FormCase("recreate while open (retire X, add Y)", db => { }, db => Recreate(db, "Y", Now), null);
        FormCase("pick while open (dialog state {Y,Z})", db => { db.CustomData.Remove(AK("X")); Set(db, RK("X"), "retired", Now); Create(db, "Y", Now); Create(db, "Z", Now); }, db => PickAuthority(db, "Y", Now), null);
        FormCase("pick + prune while open ({X unpruned, Y, Z})", db => { Set(db, RK("X"), "retired", Now); Create(db, "Y", Now); Create(db, "Z", Now); }, db => { PickAuthority(db, "Y", Now); Prune(db); }, null);
        FormCase("operator deletes RetiredAuthority.X by hand (X unpruned)", db => { Create(db, "Y", Now); Set(db, RK("X"), "retired", Now); }, null, snap => snap.Remove(RK("X")));
        FormCase("operator deletes RetiredAuthority.X and Authority.Y by hand (X unpruned)", db => { Create(db, "Y", Now); Set(db, RK("X"), "retired", Now); }, null, snap => { snap.Remove(RK("X")); snap.Remove(AK("Y")); });
        FormCase("operator deletes RetiredAuthority.X by hand (X pruned)", db => { db.CustomData.Remove(AK("X")); Create(db, "Y", Now); Set(db, RK("X"), "retired", Now); }, null, snap => snap.Remove(RK("X")));
    }

    static void FormCase(string name, Action<PwDatabase> setup, Action<PwDatabase> whileOpen, Action<StringDictionaryEx> operatorEdits)
    {
        var db = Open(LBase("form"));
        setup(db);
        db = RoundTrip(db);
        var snap = db.CustomData.CloneDeep();                       // OnFormLoad
        var atOpen = Show(snap.Select(kv => kv.Key).OrderBy(x => x));
        if (operatorEdits != null) operatorEdits(snap);             // Plugin Data tab, Delete
        if (whileOpen != null) whileOpen(db);                       // the plugin, while the form is open
        var live = Show(db.CustomData.Select(kv => kv.Key).OrderBy(x => x));
        var wb = snap.Count != db.CustomData.Count;                 // OnBtnClose
        if (wb) typeof(PwDatabase).GetProperty("CustomData").GetSetMethod(true).Invoke(db, new object[] { snap });
        db = RoundTrip(db);
        var after = Show(db.CustomData.Select(kv => kv.Key).OrderBy(x => x)) + " -> " + Decide(Unretired(db));
        Prune(db);
        var next = Decide(Unretired(db));
        Console.WriteLine(name + "\t" + atOpen + "\t" + live + "\t" + (wb ? "YES" : "no") + "\t" + after + "\t" + next);
        db.Close();
    }
}
