// Change detection by list versions. The reader walks the tree itself, notes the private
// List<T>._version of each list it enumerates, and checks them all again after the walk.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using KeePassLib;
using KeePassLib.Collections;

static class VersionWalk
{
    static readonly Func<PwObjectList<PwGroup>, List<PwGroup>> GroupsOf = Inner<PwGroup>();
    static readonly Func<PwObjectList<PwEntry>, List<PwEntry>> EntriesOf = Inner<PwEntry>();
    static readonly Func<List<PwGroup>, int> GroupVersion = Version<PwGroup>();
    static readonly Func<List<PwEntry>, int> EntryVersion = Version<PwEntry>();

    static Func<PwObjectList<T>, List<T>> Inner<T>() where T : class, KeePassLib.Interfaces.IDeepCloneable<T>
    {
        var p = Expression.Parameter(typeof(PwObjectList<T>));
        var f = typeof(PwObjectList<T>).GetField("m_l", BindingFlags.Instance | BindingFlags.NonPublic);
        return Expression.Lambda<Func<PwObjectList<T>, List<T>>>(Expression.Field(p, f), p).Compile();
    }

    static Func<List<T>, int> Version<T>()
    {
        var p = Expression.Parameter(typeof(List<T>));
        var f = typeof(List<T>).GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic);
        return Expression.Lambda<Func<List<T>, int>>(Expression.Field(p, f), p).Compile();
    }

    sealed class Seen
    {
        public readonly List<List<PwGroup>> Groups = new List<List<PwGroup>>();
        public readonly List<int> GroupVersions = new List<int>();
        public readonly List<List<PwEntry>> Entries = new List<List<PwEntry>>();
        public readonly List<int> EntryVersions = new List<int>();

        public bool Moved()
        {
            Thread.MemoryBarrier();
            for (int i = 0; i < Groups.Count; i++) if (GroupVersion(Groups[i]) != GroupVersions[i]) return true;
            for (int i = 0; i < Entries.Count; i++) if (EntryVersion(Entries[i]) != EntryVersions[i]) return true;
            return false;
        }
    }

    static PwEntry FindEntry(PwGroup g, PwUuid id, Seen seen)
    {
        var el = EntriesOf(g.Entries);
        seen.Entries.Add(el); seen.EntryVersions.Add(EntryVersion(el));
        Thread.MemoryBarrier();
        foreach (var e in g.Entries) { if (e != null && e.Uuid.Equals(id)) return e; }
        var gl = GroupsOf(g.Groups);
        seen.Groups.Add(gl); seen.GroupVersions.Add(GroupVersion(gl));
        Thread.MemoryBarrier();
        foreach (var sub in g.Groups) { if (sub == null) continue; var r = FindEntry(sub, id, seen); if (r != null) return r; }
        return null;
    }

    static void Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 8;
        string mode = args.Length > 1 ? args[1] : "checked";
        string churnKind = args.Length > 2 ? args[2] : "pair";
        bool atEnd = args.Length > 3 && args[3] == "last";
        int spin = args.Length > 4 ? int.Parse(args[4]) : 0;
        var clock = Stopwatch.StartNew();
        var root = new PwGroup(true, true, "root", PwIcon.Folder);
        var deep = new PwGroup(true, true, "deep", PwIcon.Folder);
        var target = new PwEntry(true, true);
        for (int i = 0; i < 10; i++) root.AddGroup(new PwGroup(true, true, "f" + i, PwIcon.Folder), true);
        root.AddGroup(deep, true);
        if (!atEnd) for (int i = 0; i < 10; i++) root.AddGroup(new PwGroup(true, true, "t" + i, PwIcon.Folder), true);
        deep.AddEntry(target, true);

        var stop = false;
        var churn = new Thread(() =>
        {
            while (!stop)
            {
                // Something ahead of `deep` in the same list is added and removed, as a delete,
                // a move or a reorder in KeePass would shift it.
                var g = new PwGroup(true, true, "g", PwIcon.Folder);
                root.Groups.Insert(0, g);
                if (churnKind == "pair") root.Groups.Remove(g);
                else if (root.Groups.UCount > 21) root.Groups.Remove(root.Groups.GetAt(0));
            }
        });
        churn.Start();

        long found = 0, threw = 0, missDetected = 0, missUndetected = 0, foundButFlagged = 0;
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            var seen = new Seen();
            PwEntry hit;
            try { hit = mode == "keepass" ? root.FindEntry(target.Uuid, true) : FindEntry(root, target.Uuid, seen); }
            catch (InvalidOperationException) { threw++; continue; }
            catch (ArgumentException) { threw++; continue; }
            if (mode == "checked" && spin > 0) { var t0 = clock.Elapsed.Ticks; while (clock.Elapsed.Ticks - t0 < spin * 10) { } }
            var moved = mode == "checked" && seen.Moved();
            if (hit != null) { found++; if (moved) foundButFlagged++; }
            else if (moved) missDetected++;
            else missUndetected++;
        }
        stop = true; churn.Join();
        Console.WriteLine("walks=" + (found + threw + missDetected + missUndetected));
        Console.WriteLine("found=" + found + " (of them flagged as moved=" + foundButFlagged + ")");
        Console.WriteLine("threw=" + threw);
        Console.WriteLine("missed, caught by the version check=" + missDetected);
        Console.WriteLine("missed, not caught=" + missUndetected);

        // The cost on a still tree, against KeePass's own FindEntry, over a larger tree.
        var big = new PwGroup(true, true, "big", PwIcon.Folder);
        PwEntry last = null;
        for (int i = 0; i < 1000; i++)
        {
            var g = new PwGroup(true, true, "g" + i, PwIcon.Folder);
            big.AddGroup(g, true);
            for (int j = 0; j < 10; j++) { last = new PwEntry(true, true); g.AddEntry(last, true); }
        }
        const int n = 200;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) big.FindEntry(last.Uuid, true);
        var plain = sw.Elapsed.TotalMilliseconds / n;
        sw.Restart();
        for (int i = 0; i < n; i++) { var s = new Seen(); FindEntry(big, last.Uuid, s); s.Moved(); }
        var checkedWalk = sw.Elapsed.TotalMilliseconds / n;
        Console.WriteLine("still tree of 1000 groups and 10000 entries, the last entry: KeePass FindEntry " +
                          plain.ToString("0.000") + " ms, version-checked walk " + checkedWalk.ToString("0.000") + " ms");
    }
}
