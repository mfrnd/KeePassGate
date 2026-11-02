using System;
using System.Collections.Generic;
using System.Threading;
using KeePassLib;

static class GroupRace
{
    static void Main()
    {
        var root = new PwGroup(true, true, "root", PwIcon.Folder);
        var target = new PwEntry(true, true);
        var deep = new PwGroup(true, true, "deep", PwIcon.Folder);
        root.AddGroup(deep, true);
        deep.AddEntry(target, true);
        var counts = new Dictionary<string, int>();
        var stop = false;
        var churn = new Thread(() =>
        {
            while (!stop)
            {
                var g = new PwGroup(true, true, "g", PwIcon.Folder);
                g.AddEntry(new PwEntry(true, true), true);
                root.Groups.Insert(0, g);

                if (root.Groups.UCount > 20) root.Groups.Remove(root.Groups.GetAt(0));
            }
        });
        churn.Start();
        var until = DateTime.UtcNow.AddSeconds(8);
        int ok = 0;
        while (DateTime.UtcNow < until)
        {
            foreach (var what in new[] { "GetEntries", "FindEntry", "FindGroup", "GetGroups" })
            {
                try
                {
                    if (what == "GetEntries") root.GetEntries(true);
                    else if (what == "FindEntry") { if (root.FindEntry(target.Uuid, true) == null) Bump(counts, what + ":missed"); }
                    else if (what == "FindGroup") { if (root.FindGroup(deep.Uuid, true) == null) Bump(counts, what + ":missed"); }
                    else root.GetGroups(true);
                    ok++;
                }
                catch (Exception e)
                {
                    var site = e.TargetSite == null ? "?" : e.TargetSite.DeclaringType.FullName + "." + e.TargetSite.Name;
                    Bump(counts, what + ":" + e.GetType().Name + "@" + site);
                }
            }
        }
        stop = true; churn.Join();
        Console.WriteLine("ok=" + ok);
        foreach (var p in counts) Console.WriteLine(p.Key + "=" + p.Value);
    }
    static void Bump(Dictionary<string, int> c, string k) { int n; c.TryGetValue(k, out n); c[k] = n + 1; }
}
