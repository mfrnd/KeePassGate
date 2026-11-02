// Locking by parking KeePass's thread. The tree is changed only on a WinForms thread, one change
// per message, as KeePass changes it inside its handlers. A reader posts a callback that holds
// that thread while the read runs on the reader's own thread, then lets it go.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassLib;

static class ParkWalk
{
    static Form _ui;
    static PwGroup _root;
    static volatile bool _stop;
    static int _slowEvery;
    static int _slowMs;
    static long _changes;

    static void Change()
    {
        if (_stop) return;
        var g = new PwGroup(true, true, "g", PwIcon.Folder);
        _root.Groups.Insert(0, g);
        if (_root.Groups.UCount > 21) _root.Groups.Remove(_root.Groups.GetAt(0));
        var n = Interlocked.Increment(ref _changes);
        if (_slowEvery > 0 && n % _slowEvery == 0) Thread.Sleep(_slowMs);   // a save, say
        _ui.BeginInvoke((MethodInvoker)Change);
    }

    // Holds KeePass's thread for the length of `read`, or gives up after `wait`.
    static bool Parked(Action read, TimeSpan wait, out double waitedMs)
    {
        var parked = new ManualResetEventSlim(false);
        var done = new ManualResetEventSlim(false);
        var clock = Stopwatch.StartNew();
        _ui.BeginInvoke((MethodInvoker)delegate
        {
            parked.Set();
            done.Wait(TimeSpan.FromSeconds(5));   // bounded on this side too
        });
        var got = parked.Wait(wait);
        waitedMs = clock.Elapsed.TotalMilliseconds;
        try
        {
            if (got) read();
        }
        finally
        {
            done.Set();
        }
        return got;
    }

    static void Main(string[] args)
    {
        int seconds = int.Parse(args[0]);
        _slowEvery = int.Parse(args[1]);
        _slowMs = int.Parse(args[2]);

        _root = new PwGroup(true, true, "root", PwIcon.Folder);
        var deep = new PwGroup(true, true, "deep", PwIcon.Folder);
        var target = new PwEntry(true, true);
        for (int i = 0; i < 10; i++) _root.AddGroup(new PwGroup(true, true, "f" + i, PwIcon.Folder), true);
        _root.AddGroup(deep, true);
        deep.AddEntry(target, true);

        var ready = new ManualResetEventSlim(false);
        var ui = new Thread(() =>
        {
            _ui = new Form();
            var h = _ui.Handle;
            _ui.Load += delegate { };
            ready.Set();
            _ui.BeginInvoke((MethodInvoker)Change);
            Application.Run();
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ready.Wait();

        // The control: a reader that does not park, beside the one that does.
        long freeWalks = 0, freeMissed = 0, freeThrew = 0;
        var free = new Thread(() =>
        {
            while (!_stop)
            {
                try { if (_root.FindEntry(target.Uuid, true) == null) freeMissed++; }
                catch (Exception) { freeThrew++; }
                freeWalks++;
            }
        });
        free.Start();

        long walks = 0, missed = 0, threw = 0, timedOut = 0;
        var waits = new List<double>();
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            double waited;
            var got = Parked(delegate
            {
                try { if (_root.FindEntry(target.Uuid, true) == null) missed++; }
                catch (Exception) { threw++; }
            }, TimeSpan.FromSeconds(2), out waited);
            if (!got) timedOut++;
            walks++;
            waits.Add(waited);
        }
        _stop = true;
        free.Join();
        _ui.BeginInvoke((MethodInvoker)Application.ExitThread);
        ui.Join();

        waits.Sort();
        Func<double, double> at = q => waits[Math.Min(waits.Count - 1, (int)(q * waits.Count))];
        Console.WriteLine("changes made on the UI thread=" + _changes);
        Console.WriteLine("parked reader: walks=" + walks + " missed=" + missed + " threw=" + threw + " gave up=" + timedOut);
        Console.WriteLine("  wait to park ms: p50=" + at(0.5).ToString("0.000") + " p99=" + at(0.99).ToString("0.000") +
                          " max=" + waits.Last().ToString("0.000"));
        Console.WriteLine("free reader: walks=" + freeWalks + " missed=" + freeMissed + " threw=" + freeThrew);
    }
}
