// Does a read that holds the window's thread starve the operator's input, and does checking the
// queue first and stepping aside on a timer give input priority?
//
//   UiPriority.exe <mode> <readers> <readMs> <moves> [box | dialog | doevents]
//     mode: idle | plain | priority | queued
//
// plain: every hold is its own BeginInvoke. priority: each such callback first asks whether input
// or a repaint is waiting and steps aside on a timer if so. queued: holds wait in one queue in the
// order they asked, each started on Application.Idle, with a slow timer standing in for Idle in a
// loop that never raises it. A 100 ms timer on the window measures whether timers starve.
//
// The readers each hold the window's thread for readMs at a time, as a sweep would, while the
// mouse is moved over the window through SendInput every 40 ms; the "latency" lines below are those
// moves. box, dialog and doevents put the window's thread inside a message box, a WinForms dialog
// or a DoEvents loop for three seconds first, and count where holds started meanwhile.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class UiPriority
{
    const uint QS_KEY = 0x0001, QS_MOUSEMOVE = 0x0002, QS_MOUSEBUTTON = 0x0004, QS_PAINT = 0x0020,
        QS_RAWINPUT = 0x0400;
    const uint Waiting = QS_KEY | QS_MOUSEMOVE | QS_MOUSEBUTTON | QS_PAINT | QS_RAWINPUT;
    const uint QS_TIMER = 0x0010, QS_SENDMESSAGE = 0x0040, QS_ALLPOSTMESSAGE = 0x0100;

    [DllImport("user32.dll")] static extern uint GetQueueStatus(uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    static bool _box, _dialog, _doEvents;
    static volatile bool _pumping;
    static int _servedInPump, _servedInPumpOnIdle;
    static IntPtr _handle;
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }

    static Form _form;
    static string _mode;
    static int _readMs;
    static volatile bool _stop;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly object Lock = new object();
    static readonly List<double> KeyLatency = new List<double>();
    static readonly List<double> HoldWait = new List<double>();
    static int _holds, _deferrals;
    static double _sentAt;
    static int _timeouts;

    static double Now() { return Clock.Elapsed.TotalMilliseconds; }

    static readonly List<double> TickGap = new List<double>();
    static readonly List<string> LongGaps = new List<string>();
    static double _windowStart, _windowEnd;
    static int _windowHolds;
    static double _lastTick;
    static int _yieldInput, _yieldTimer, _yieldPosted;

    // One queue of holds, in the order they asked, served by one callback on the window's thread.
    sealed class Pending
    {
        public readonly ManualResetEventSlim Parked = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public bool Abandoned;
    }
    static readonly Queue<Pending> Holds = new Queue<Pending>();
    static bool _scheduled;
    static System.Windows.Forms.Timer _resume;

    // A hold starts only on Application.Idle, which WinForms raises once everything else in the
    // queue has been handled: posted work, input, repaints and timers. An empty posted message is
    // what brings the next Idle round promptly, and a slow timer stands in for Idle inside a loop
    // that is not WinForms', such as a message box's, where Idle is never raised.
    static int _servedIdle, _servedTimer;
    static void Wake() { _resume.Start(); }
    static bool Queued(Action read, TimeSpan wait)
    {
        var p = new Pending();
        double asked = Now();
        lock (Holds)
        {
            Holds.Enqueue(p);
            if (!_scheduled) { _scheduled = true; _form.BeginInvoke((MethodInvoker)Wake); }
        }
        bool got = p.Parked.Wait(wait);
        if (!got) lock (Holds) { if (p.Parked.IsSet) got = true; else p.Abandoned = true; }
        try
        {
            if (got)
            {
                lock (Lock) HoldWait.Add(Now() - asked);
                read();
            }
        }
        finally { p.Done.Set(); }
        return got;
    }

    // On the window's thread. Serves holds one after another until the queue is empty or something
    // else wants the thread, and then gives way: input, repaints and timers by resuming on a timer
    // of its own, which Windows delivers only when none of those is waiting, and other posted work
    // by posting itself behind it after every hold.
    static void Serve(bool idle)
    {
        Pending next;
        lock (Holds)
        {
            if (!_scheduled) return;
            while (Holds.Count > 0 && Holds.Peek().Abandoned) Holds.Dequeue();
            if (Holds.Count == 0) { _scheduled = false; _resume.Stop(); return; }
        }
        uint waiting = GetQueueStatus(Waiting) >> 16;
        if ((waiting & Waiting) != 0) { _yieldInput++; return; }       // Idle comes round again after it
        _resume.Stop();
        if (idle) _servedIdle++; else _servedTimer++;
        if (_pumping) { _servedInPump++; if (idle) _servedInPumpOnIdle++; }
        lock (Holds)
        {
            next = Holds.Dequeue();
            if (!next.Abandoned) next.Parked.Set();
        }
        if (!next.Abandoned) next.Done.Wait(TimeSpan.FromSeconds(5));
        _yieldPosted++;
        _form.BeginInvoke((MethodInvoker)Wake);                          // brings the next Idle round
    }

    // A read that holds the window's thread for its whole length.
    static bool Hold(Action read, TimeSpan wait)
    {
        var parked = new ManualResetEventSlim(false);
        var done = new ManualResetEventSlim(false);
        var gate = new object();
        bool abandoned = false;
        double asked = Now();
        Action park = null;
        park = delegate
        {
            lock (gate) { if (abandoned) return; }
            if (_mode == "priority" && (GetQueueStatus(Waiting) >> 16) != 0)
            {
                // Something of the operator's is waiting: step aside until it has been handled.
                Interlocked.Increment(ref _deferrals);
                var timer = new System.Windows.Forms.Timer { Interval = 1 };
                timer.Tick += delegate { timer.Stop(); timer.Dispose(); park(); };
                timer.Start();
                return;
            }
            lock (gate) { if (abandoned) return; parked.Set(); }
            done.Wait(TimeSpan.FromSeconds(5));
        };
        _form.BeginInvoke((MethodInvoker)delegate { park(); });
        bool got = parked.Wait(wait);
        if (!got)
        {
            lock (gate) { if (parked.IsSet) got = true; else abandoned = true; }
        }
        try
        {
            if (got)
            {
                lock (Lock) HoldWait.Add(Now() - asked);
                read();
            }
        }
        finally { done.Set(); }
        return got;
    }

    static void Spin(int ms)
    {
        var until = Now() + ms;
        while (Now() < until) { }
    }

    // A mouse move over the window, which reaches it whichever window has the focus.
    static int _side;
    static int _debug = 0;
    static void Press()
    {
        _side ^= 1;
        var screen = Screen.PrimaryScreen.Bounds;
        int x = 150 + 20 * _side, y = 55;   // above whatever else is on the desktop
        var move = new INPUT { type = 0, mi = new MOUSEINPUT {
            dx = x * 65535 / (screen.Width - 1), dy = y * 65535 / (screen.Height - 1),
            dwFlags = 0x0001 | 0x8000 } };                                   // MOVE | ABSOLUTE
        _sentAt = Now();
        if (SendInput(1, new[] { move }, Marshal.SizeOf(typeof(INPUT))) != 1)
            Console.WriteLine("SendInput failed: {0}", Marshal.GetLastWin32Error());
        if (_debug-- > 0)
        {
            Thread.Sleep(50);
            var under = WindowFromPoint(Cursor.Position);
            Console.WriteLine("cursor {0}; under {1} (top {2}); form {3}", Cursor.Position, under, GetAncestor(under, 2), _handle);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        _mode = args[0];
        int readers = int.Parse(args[1]);
        _readMs = int.Parse(args[2]);
        int keys = int.Parse(args[3]);
        _box = args.Length > 4 && args[4] == "box";
        _dialog = args.Length > 4 && args[4] == "dialog";
        _doEvents = args.Length > 4 && args[4] == "doevents";

        _form = new Form { Text = "UiPriority", Width = 400, Height = 300, TopMost = true,
            StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0) };
        var pending = new AutoResetEvent(false);
        _form.MouseMove += delegate (object s, MouseEventArgs e)
        {
            lock (Lock) KeyLatency.Add(Now() - _sentAt);
            pending.Set();
        };
        _resume = new System.Windows.Forms.Timer { Interval = 50 };
        _resume.Tick += delegate { Serve(false); };
        Application.Idle += delegate { if (_mode == "queued") Serve(true); };
        var ticker = new System.Windows.Forms.Timer { Interval = 100 };
        ticker.Tick += delegate
        {
            double now = Now();
            if (_lastTick > 0 && _windowStart > 0 && _windowEnd == 0) lock (Lock) TickGap.Add(now - _lastTick);
            if (_lastTick > 0 && now - _lastTick > 200) lock (Lock) LongGaps.Add(string.Format("{0:F0}-{1:F0}", _lastTick - _windowStart, now - _windowStart));
            _lastTick = now;
        };
        ticker.Start();
        _form.Shown += delegate
        {
            _handle = _form.Handle;
            Console.WriteLine("form bounds {0}", _form.Bounds);
            SetForegroundWindow(_form.Handle);
            _form.Activate();
            var threads = new List<Thread>();
            if (_mode != "idle")
            {
                for (int i = 0; i < readers; i++)
                {
                    var t = new Thread(delegate ()
                    {
                        while (!_stop)
                        {
                            if (_mode == "queued" ? Queued(delegate { Spin(_readMs); }, TimeSpan.FromSeconds(10))
                                                  : Hold(delegate { Spin(_readMs); }, TimeSpan.FromSeconds(10)))
                            {
                                Interlocked.Increment(ref _holds);
                                double at = Now();
                                if (_windowStart > 0 && at >= _windowStart && (_windowEnd == 0 || at <= _windowEnd))
                                    Interlocked.Increment(ref _windowHolds);
                            }
                            else
                                Interlocked.Increment(ref _timeouts);
                        }
                    }) { IsBackground = true };
                    threads.Add(t);
                }
            }
            var driver = new Thread(delegate ()
            {
                Thread.Sleep(500);
                foreach (var t in threads) t.Start();
                Thread.Sleep(500);
                int lost = 0;
                double start = Now();
                _windowStart = start;
                if (_box)
                {
                    // A native message box on the window's thread for three seconds: its loop is not
                    // WinForms', so Application.Idle is never raised while it is up.
                    _form.BeginInvoke((MethodInvoker)delegate { MessageBox.Show("probe", "UiPriority box"); });
                    Thread.Sleep(3000);
                    PostMessage(FindWindow("#32770", "UiPriority box"), 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
                    Thread.Sleep(200);
                }
                if (_doEvents)
                {
                    // KeePass's long operations pump this way: a loop on the window's thread calling
                    // Application.DoEvents, as a save does once per percent and a merge per step.
                    _form.BeginInvoke((MethodInvoker)delegate
                    {
                        _pumping = true;
                        var until = Now() + 3000;
                        while (Now() < until) { Application.DoEvents(); Spin(2); }
                        _pumping = false;
                    });
                    Thread.Sleep(3200);
                }
                if (_dialog)
                {
                    // A WinForms dialog, as KeePass's own are: its loop is WinForms', so Idle should be raised.
                    Form shown = null;
                    _form.BeginInvoke((MethodInvoker)delegate { shown = new Form { Text = "UiPriority dialog" }; shown.ShowDialog(_form); });
                    Thread.Sleep(3000);
                    shown.BeginInvoke((MethodInvoker)shown.Close);
                    Thread.Sleep(200);
                }
                for (int k = 0; k < keys; k++)
                {
                    Press();
                    if (!pending.WaitOne(3000)) lost++;
                    Thread.Sleep(40);
                }
                _windowEnd = Now();
                double seconds = (_windowEnd - start) / 1000;
                _stop = true;
                foreach (var t in threads) t.Join();
                Report(seconds, lost);
                _form.BeginInvoke((MethodInvoker)_form.Close);
            }) { IsBackground = true };
            driver.Start();
        };
        Application.Run(_form);
    }

    static string Stats(List<double> xs)
    {
        if (xs.Count == 0) return "none";
        var s = xs.OrderBy(x => x).ToList();
        Func<double, double> p = q => s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        return string.Format("n={0} p50={1:F1} p90={2:F1} p99={3:F1} max={4:F1} ms", s.Count, p(0.5), p(0.9), p(0.99), s[s.Count - 1]);
    }

    static void Report(double seconds, int lost)
    {
        lock (Lock)
        {
            Console.WriteLine("mode={0} readMs={1} over {2:F1} s", _mode, _readMs, seconds);
            Console.WriteLine("  input latency: {0}, lost {1}", Stats(KeyLatency), lost);
            Console.WriteLine("  holds in the window: {0} ({1:F0}/s, {2:P0} of the thread), timeouts {3}, deferrals {4}",
                _windowHolds, _windowHolds / seconds, _windowHolds * _readMs / (seconds * 1000), _timeouts, _deferrals);
            Console.WriteLine("  wait to hold: {0}", Stats(HoldWait));
            Console.WriteLine("  100 ms timer, gap between ticks: {0}", Stats(TickGap));
            Console.WriteLine("  gaps over 200 ms, ms from the window's start: {0}", LongGaps.Count == 0 ? "none" : string.Join(", ", LongGaps));
            if (_mode == "queued")
                Console.WriteLine("  gave way to input or a repaint {0}; holds started on Idle {1}, on the fallback timer {2}; inside DoEvents {3}, of them on Idle {4}", _yieldInput, _servedIdle, _servedTimer, _servedInPump, _servedInPumpOnIdle);
        }
    }
}
