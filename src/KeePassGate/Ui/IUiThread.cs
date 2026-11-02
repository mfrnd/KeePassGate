using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Running something on the thread that owns the windows, and giving up if it does not happen.
    /// </summary>
    /// <remarks>
    /// Every write this plugin makes marshals synchronously, through this, and `DESIGN.md` §119
    /// says why: a change and the save that keeps it are one call on the thread that owns the
    /// windows, and the caller needs the answer rather than a promise. For pairing that answer is
    /// what decides whether a certificate is handed over, which it must not be until the record
    /// behind it is on disk.
    ///
    /// **The wait is bounded, and that is what keeps the deadlock case closed.** A connection thread
    /// that cannot get the user interface's attention fails its pairing rather than holding a socket
    /// and a lock until somebody notices. `Control.Invoke` offers no timeout at all, which is why
    /// the implementation queues the work and waits on it with a clock.
    /// </remarks>
    public interface IUiThread
    {
        /// <summary>
        /// True when the work ran to completion within the timeout, and false when the timeout
        /// passed first, whether or not the work then ran.
        /// </summary>
        /// <remarks>
        /// **False means the timeout passed, and what happened depends on when the work began.**
        /// Work still sitting in the queue when the timeout expires is withdrawn rather than left
        /// to run later, because a caller told "no" has already given its answer: for pairing that
        /// answer is a refusal, and a write arriving after it would save an admission record for a
        /// certificate that was destroyed, or raise an approval dialog for a client that has
        /// already been turned away.
        ///
        /// The one thing an implementation cannot withdraw is work that has already begun. There it
        /// waits, because the caller's lock and the caller's belief about what the database now
        /// holds both depend on knowing that it has stopped, and it still answers false, because
        /// the work did not finish inside the timeout. **That work did happen**, so a caller reading
        /// false has to look at what the work left behind before treating it as undone.
        ///
        /// **Whatever the work threw is thrown here, on the calling thread**, rather than being
        /// reported as an answer, because there is no answer a failed write or a dialog that never
        /// opened could honestly give: true would claim it ran to completion and false would claim
        /// it never started. Every caller already stands under one of the four catch-alls, so an
        /// exception nothing on the way up names is counted and logged there; the implementation
        /// over a `Control` says why it has to carry it rather than let it travel.
        /// </remarks>
        bool Run(Action work, TimeSpan timeout);

        /// <summary>
        /// Why work could not start on that thread now, or null when nothing is known to stop it.
        /// </summary>
        /// <remarks>
        /// Asked before work is queued, so that a caller is told at once what it would otherwise
        /// learn from its timeout: work waits for the thread to be idle, and a thread inside a loop
        /// that never raises Idle does not come round until the loop ends. Null is no promise about
        /// how long the work will wait.
        /// </remarks>
        string Stalled { get; }
    }

    /// <summary>
    /// The real one, over any control whose handle belongs to the user interface thread: one queue
    /// of turns on that thread, for the work this plugin runs there and for the reads it holds that
    /// thread still for.
    /// </summary>
    /// <remarks>
    /// **A turn starts only once KeePass has nothing else to do**, which is `Application.Idle`:
    /// WinForms raises it when everything in the thread's queue has been handled, posted work,
    /// input, repaints and timers alike. Posted with `BeginInvoke`, as every write was before, a
    /// turn went ahead of all of them, because Windows hands out a posted message before input and
    /// makes a timer's message only once nothing else is queued. Thirty-two threads taking turns
    /// that way delivered one of sixty mouse moves to the window in three seconds, and turns posted
    /// one after another held a timer off for six hundred milliseconds whenever no input came,
    /// which is what KeePass's inactivity lock and its clipboard clearing run on. Started on Idle,
    /// the same load left input waiting at most for the turn it arrived during, and every timer on
    /// time. `UiPriority.cs` on `spikes/keepass-thread` measured all four.
    ///
    /// **Work starts on Idle and nowhere else, and that keeps it out of KeePass's own
    /// operations.** A save, an import, a synchronize, an export and XML Replace all pump through
    /// `Application.DoEvents`, read off the IL of KeePass 2.61.1, and WinForms never raises Idle
    /// inside it. Posted work was delivered inside each of them and had to be refused there by the
    /// writer; queued work waits for the operation to end. The writer still asks, because a dialog
    /// shown inside one of them is a WinForms loop and does raise Idle.
    ///
    /// **A read also starts on a slow timer**, because a read inside a pump is harmless and a loop
    /// that is not WinForms' never raises Idle: a message box's, or `DoEvents` itself. So reads
    /// are served during a long save, and the first read waiting may go ahead of work that cannot
    /// start yet. Otherwise the queue is served in the order it was joined.
    ///
    /// **A native dialog is answered at once rather than waited out.** A message box, a task dialog
    /// and a file dialog run a loop of Windows' own, which never raises Idle, so work queued under
    /// one waits for it to close or for its own timeout. Each is a window of class `#32770` on the
    /// thread, which a form never is, and any thread can see one there without asking the thread
    /// anything: measured, under a message box and a file dialog and not under a WinForms dialog,
    /// in about twenty microseconds. `Stalled` is that question, for a caller to ask before it
    /// queues. `NativeLoop.cs` on `spikes/keepass-thread` measured it.
    ///
    /// **Re-entered rather than serialized.** Work that shows a dialog runs a loop of its own,
    /// which raises Idle, and the turns behind it are served from inside that loop, as posted work
    /// was. A read holding the thread runs no loop, so nothing is served inside one.
    /// </remarks>
    public sealed class ControlUiThread : IUiThread, IReadHold, IDisposable
    {
        /// <summary>How long a read waits for its turn before it is answered as busy.</summary>
        /// <remarks>
        /// A turn normally comes round in under a millisecond, and in about half a second with
        /// every connection the gate allows reading at once. What holds one off longer is KeePass
        /// working without pumping, such as deriving a key, and a read that has waited this long
        /// is better told to come back.
        /// </remarks>
        public static readonly TimeSpan HoldWait = TimeSpan.FromSeconds(10);

        /// <summary>How long KeePass's thread is held for one read before it is let go.</summary>
        /// <remarks>
        /// A bound on a defect rather than on a read: a sweep of ten thousand entries holds the
        /// thread for about seventeen milliseconds. A read that is still going when this passes is
        /// answered as busy rather than trusted, because the tree was free to move under the end of
        /// it, and the operator's window was frozen for exactly this long.
        /// </remarks>
        public static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(5);

        /// <summary>How often a read is offered a turn inside a loop that never raises Idle.</summary>
        private const int FallbackMilliseconds = 20;

        /// <summary>The class Windows gives a message box, a task dialog and a file dialog.</summary>
        private const string NativeDialog = "#32770";

        /// <summary>How many holds the current thread is inside.</summary>
        [ThreadStatic]
        private static int t_holds;

        private readonly Control _control;
        private readonly TimeSpan _holdWait;
        private readonly TimeSpan _holdLimit;
        private readonly object _gate = new object();
        private readonly List<Turn> _turns = new List<Turn>();
        private bool _disposed;

        /// <summary>
        /// The thread the control's window belongs to, as Windows numbers it, or zero until it has
        /// been read on that thread.
        /// </summary>
        private uint _thread;

        // Made, started and stopped on the user interface thread, and only ever touched there.
        private System.Windows.Forms.Timer _fallback;
        private bool _listening;

        public ControlUiThread(Control control)
            : this(control, HoldWait, HoldLimit)
        {
        }

        internal ControlUiThread(Control control, TimeSpan holdWait, TimeSpan holdLimit)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            _control = control;
            _holdWait = holdWait;
            _holdLimit = holdLimit;

            if (control.IsHandleCreated && !control.InvokeRequired)
            {
                // Made on the thread, as the plugin makes it, so the window can be asked now.
                // Otherwise the first turn asks, on the thread.
                _thread = NativeMethods.GetWindowThreadProcessId(control.Handle);
            }
        }

        /// <summary>Whether the calling thread is inside a hold.</summary>
        internal static bool Holding
        {
            get { return t_holds > 0; }
        }

        public string Stalled
        {
            get
            {
                var thread = Volatile.Read(ref _thread);
                if (thread == 0)
                {
                    return null;
                }

                var stalled = false;
                NativeMethods.EnumThreadWindows(thread, (window, parameter) =>
                {
                    stalled = NativeMethods.IsWindowVisible(window)
                              && string.Equals(NativeMethods.GetClassName(window), NativeDialog, StringComparison.Ordinal);
                    return !stalled;
                });

                return stalled ? "KeePass is showing a dialog of Windows' own, such as a message box" : null;
            }
        }

        public bool Run(Action work, TimeSpan timeout)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (_control.IsDisposed || !_control.IsHandleCreated)
            {
                return false;
            }

            if (!_control.InvokeRequired)
            {
                // Already there, which is what happens when a test drives this directly.
                work();
                return true;
            }

            if (Holding)
            {
                // Waited out, this would hold KeePass's window for the length of the hold's limit
                // and then fail anyway, since the thread it asks is the one it is holding.
                throw new HoldMisused("work was asked of KeePass's thread by a thread holding it still");
            }

            var turn = new Turn(work);
            if (!Join(turn))
            {
                return false;
            }

            if (turn.AwaitReady(timeout))
            {
                if (turn.Withdrawn)
                {
                    // Turned away by the plugin stopping, before it began.
                    return false;
                }

                turn.Rethrow();
                return true;
            }

            if (turn.Withdraw())
            {
                // Queued and never started, so nothing happened and nothing will.
                return false;
            }

            // Already running, so the only honest answer is to wait for it. Returning instead
            // would let a caller release a lock while its own write was still going on.
            //
            // **Bounded by the work rather than by a clock, and the work is nearly always
            // bounded.** A file save ends, and the pairing dialog closes itself. The exceptions are
            // a save that has already gone wrong, which KeePass reports in a message box this wait
            // then lasts until somebody presses OK, and whatever the save runs that is not KeePass's
            // own: the operator's triggers and other plugins' save handlers take as long as they
            // take. `DatabaseWriter.Asking` forestalls the three questions that can be seen coming,
            // and `DESIGN.md` §119 says why the rest are left as they are.
            turn.AwaitReady(Timeout.InfiniteTimeSpan);
            turn.Rethrow();
            return false;
        }

        public T Hold<T>(Func<T> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            if (Holding)
            {
                // Inside a hold already, so the tree is standing still.
                return Inline(read);
            }

            if (_control.IsDisposed || !_control.IsHandleCreated)
            {
                // Before the window is made or after it has gone, which a connection does not
                // meet: the listener starts after the one and stops before the other.
                throw new VaultUnsettled("KeePass's window is not there to hold still");
            }

            if (!_control.InvokeRequired)
            {
                // KeePass's own thread, which cannot change the tree while it is running this.
                return Inline(read);
            }

            var turn = new Turn(null);
            if (!Join(turn))
            {
                throw new VaultUnsettled("KeePass's thread is no longer taking reads");
            }

            if (!turn.AwaitReady(_holdWait) && turn.Withdraw())
            {
                throw new VaultUnsettled("KeePass's thread could not be held still for a read");
            }

            // Ready by now, or about to be: a turn that has begun is parking, and parking waits on
            // nothing.
            turn.AwaitReady(Timeout.InfiniteTimeSpan);
            if (turn.Withdrawn)
            {
                throw new VaultUnsettled("KeePass's thread is no longer taking reads");
            }

            T result;
            try
            {
                result = Inline(read);
            }
            finally
            {
                turn.Release();
            }

            if (turn.Lapsed)
            {
                throw new VaultUnsettled("the read took longer than KeePass's thread could be held for");
            }

            return result;
        }

        /// <summary>
        /// Turns away every turn still waiting, and stops listening for Idle. On the user interface
        /// thread, where `Application.Idle` was subscribed.
        /// </summary>
        public void Dispose()
        {
            List<Turn> waiting;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                waiting = new List<Turn>(_turns);
                _turns.Clear();
            }

            foreach (var turn in waiting)
            {
                turn.Refuse();
            }

            if (_listening)
            {
                Application.Idle -= OnIdle;
                _fallback.Dispose();
                _listening = false;
            }
        }

        private static T Inline<T>(Func<T> read)
        {
            t_holds++;
            try
            {
                return read();
            }
            finally
            {
                t_holds--;
            }
        }

        /// <summary>Puts a turn in the queue, and false when nothing will ever serve it.</summary>
        private bool Join(Turn turn)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _turns.Add(turn);
            }

            // A turn whose wake could not be posted is withdrawn, so that another turn's wake
            // cannot serve it after its caller was told no. Unless one already has.
            return Wake() || !turn.Withdraw();
        }

        /// <summary>
        /// Posts the message that brings a round of Idle: it is handled, and Idle follows once
        /// nothing else is waiting.
        /// </summary>
        private bool Wake()
        {
            try
            {
                _control.BeginInvoke((MethodInvoker)Listen);
                return true;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // The window went between the check and the post, which is KeePass closing. What
                // is queued is turned away by `Dispose`, or waits out its own clock.
                return false;
            }
        }

        /// <summary>
        /// On the user interface thread: listens for Idle, from the first turn on, and keeps the
        /// fallback running while anything is waiting.
        /// </summary>
        private void Listen()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            if (!_listening)
            {
                if (Volatile.Read(ref _thread) == 0)
                {
                    Volatile.Write(ref _thread, NativeMethods.GetWindowThreadProcessId(_control.Handle));
                }

                Application.Idle += OnIdle;
                _fallback = new System.Windows.Forms.Timer { Interval = FallbackMilliseconds };
                _fallback.Tick += OnFallback;
                _listening = true;
            }

            _fallback.Start();
        }

        private void OnIdle(object sender, EventArgs e)
        {
            Serve(true);
        }

        private void OnFallback(object sender, EventArgs e)
        {
            Serve(false);
        }

        /// <summary>
        /// On the user interface thread: one turn, the first in the queue, or the first read when
        /// this is not Idle.
        /// </summary>
        private void Serve(bool idle)
        {
            Turn next = null;
            lock (_gate)
            {
                _turns.RemoveAll(turn => turn.Withdrawn);

                if (_turns.Count == 0)
                {
                    if (_fallback != null)
                    {
                        _fallback.Stop();
                    }

                    return;
                }

                foreach (var turn in _turns)
                {
                    if (idle || turn.IsRead)
                    {
                        next = turn;
                        break;
                    }
                }

                if (next == null)
                {
                    // Only work is waiting, and work waits for Idle.
                    return;
                }

                _turns.Remove(next);
            }

            next.Take(_holdLimit);

            bool more;
            lock (_gate)
            {
                more = _turns.Count > 0 && !_disposed;
            }

            if (more)
            {
                Wake();
            }
        }

        /// <summary>
        /// One turn: work to run on the user interface thread, or a read to hold it still for.
        /// </summary>
        /// <remarks>
        /// **It can be called off up to the instant it starts.** A caller whose clock ran out has
        /// given its answer already, so a turn it withdrew must never run afterwards. The lock is
        /// what makes the timeout mean "this did not happen" rather than "this has not happened yet".
        ///
        /// **The catch around the work is total, and it is not a fifth catch-all.** Work that lets
        /// an exception out on the user interface thread does not simply fail the caller: WinForms
        /// hands it to `Application.OnThreadException`, and KeePass registers no handler and sets
        /// no unhandled exception mode, so WinForms shows its own "Unhandled exception has occurred
        /// in your application" dialog inside the password manager, whose Quit ends the process
        /// outside KeePass's own shutdown. Measured on .NET Framework 4.8 with a form on one thread
        /// and a throwing `BeginInvoke` from another: with a handler registered it fired on the
        /// user interface thread before `EndInvoke` was ever called, and with none registered
        /// WinForms went modal instead.
        ///
        /// Nothing is absorbed here, which is what separates this from the four places that catch
        /// everything: the exception is carried to the thread that asked for the work and thrown
        /// there by `Rethrow`, which is where a filter can name it or a catch-all can count it.
        /// Recording it here instead would take a `GateFailure` a caller names away from the caller
        /// and report a defect twice.
        /// </remarks>
        private sealed class Turn
        {
            private readonly object _lock = new object();
            private readonly Action _work;

            private bool _started;
            private bool _withdrawn;
            private bool _ready;
            private bool _released;
            private bool _lapsed;
            private Exception _failure;

            /// <summary>A turn for this work, or for a read when there is none.</summary>
            /// <param name="work">What to run, or null for a read.</param>
            public Turn(Action work)
            {
                _work = work;
            }

            public bool IsRead
            {
                get { return _work == null; }
            }

            public bool Withdrawn
            {
                get
                {
                    lock (_lock)
                    {
                        return _withdrawn;
                    }
                }
            }

            /// <summary>Whether a read outlasted its hold and the thread was let go before it ended.</summary>
            public bool Lapsed
            {
                get
                {
                    lock (_lock)
                    {
                        return _lapsed;
                    }
                }
            }

            /// <summary>Calls it off. False when it had already begun and cannot be.</summary>
            public bool Withdraw()
            {
                lock (_lock)
                {
                    if (_started)
                    {
                        return false;
                    }

                    _withdrawn = true;
                    return true;
                }
            }

            /// <summary>Calls it off for the plugin stopping, and tells whoever is waiting.</summary>
            public void Refuse()
            {
                lock (_lock)
                {
                    if (_started)
                    {
                        return;
                    }

                    _withdrawn = true;
                    _ready = true;
                    Monitor.PulseAll(_lock);
                }
            }

            /// <summary>
            /// Whether it is ready: the work has run, the read may begin, or it was refused.
            /// </summary>
            public bool AwaitReady(TimeSpan timeout)
            {
                var clock = Stopwatch.StartNew();
                lock (_lock)
                {
                    while (!_ready)
                    {
                        if (timeout == Timeout.InfiniteTimeSpan)
                        {
                            Monitor.Wait(_lock);
                            continue;
                        }

                        var left = timeout - clock.Elapsed;
                        if (left <= TimeSpan.Zero || !Monitor.Wait(_lock, left))
                        {
                            return _ready;
                        }
                    }

                    return true;
                }
            }

            /// <summary>
            /// On the user interface thread: runs the work, or holds the thread until the read is
            /// over or the limit has passed.
            /// </summary>
            public void Take(TimeSpan limit)
            {
                lock (_lock)
                {
                    if (_withdrawn)
                    {
                        return;
                    }

                    _started = true;
                }

                if (_work == null)
                {
                    Hold(limit);
                    return;
                }

                Exception failure = null;
                try
                {
                    _work();
                }
                // codeql[cs/catch-of-all-exceptions] kept for `Rethrow` to throw where a catch-all sees it
                catch (Exception error)
                {
                    failure = error;
                }

                lock (_lock)
                {
                    _failure = failure;
                    _ready = true;
                    Monitor.PulseAll(_lock);
                }
            }

            /// <summary>Lets the user interface thread go, from the thread that read.</summary>
            public void Release()
            {
                lock (_lock)
                {
                    _released = true;
                    Monitor.PulseAll(_lock);
                }
            }

            /// <summary>
            /// Throws whatever the work threw, here, and does nothing when it threw nothing.
            /// </summary>
            /// <remarks>
            /// `ExceptionDispatchInfo` rather than `throw` so that the stack the work failed on
            /// survives the crossing, which is the whole value of the exception to whoever reads
            /// the log.
            /// </remarks>
            public void Rethrow()
            {
                Exception failure;
                lock (_lock)
                {
                    failure = _failure;
                }

                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            private void Hold(TimeSpan limit)
            {
                var clock = Stopwatch.StartNew();
                lock (_lock)
                {
                    _ready = true;
                    Monitor.PulseAll(_lock);

                    while (!_released)
                    {
                        var left = limit - clock.Elapsed;
                        if (left <= TimeSpan.Zero || !Monitor.Wait(_lock, left))
                        {
                            // Let go with the read still going, so it is not to be believed.
                            _lapsed = !_released;
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Runs it here and now. For tests, and for anything assembled without a window, where there is
    /// no other thread to be on and so nothing to hold still.
    /// </summary>
    public sealed class CurrentThread : IUiThread, IReadHold
    {
        public static readonly CurrentThread Instance = new CurrentThread();

        public string Stalled
        {
            get { return null; }
        }

        public bool Run(Action work, TimeSpan timeout)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            work();
            return true;
        }

        public T Hold<T>(Func<T> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            return read();
        }
    }
}
