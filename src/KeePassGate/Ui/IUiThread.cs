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
    }

    /// <summary>
    /// The real one, over any control whose handle belongs to the user interface thread: one queue
    /// of turns on that thread, for the work this plugin runs there.
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
    /// shown inside one of them is a WinForms loop and does raise Idle. The queue is served in the
    /// order it was joined.
    ///
    /// **Re-entered rather than serialized.** Work that shows a dialog runs a loop of its own,
    /// which raises Idle, and the turns behind it are served from inside that loop, as posted work
    /// was.
    /// </remarks>
    public sealed class ControlUiThread : IUiThread, IDisposable
    {
        private readonly Control _control;
        private readonly object _gate = new object();
        private readonly List<Turn> _turns = new List<Turn>();
        private bool _disposed;

        // Made on the user interface thread, and only ever touched there.
        private bool _listening;

        public ControlUiThread(Control control)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            _control = control;
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
                _listening = false;
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

        /// <summary>On the user interface thread: listens for Idle, from the first turn on.</summary>
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
                Application.Idle += OnIdle;
                _listening = true;
            }
        }

        private void OnIdle(object sender, EventArgs e)
        {
            Serve();
        }

        /// <summary>On the user interface thread: one turn, the first in the queue.</summary>
        private void Serve()
        {
            Turn next;
            lock (_gate)
            {
                _turns.RemoveAll(turn => turn.Withdrawn);

                if (_turns.Count == 0)
                {
                    return;
                }

                next = _turns[0];
                _turns.RemoveAt(0);
            }

            next.Take();

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
        /// One turn: work to run on the user interface thread.
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
            private Exception _failure;

            /// <summary>A turn for this work.</summary>
            public Turn(Action work)
            {
                _work = work;
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
            /// Whether it is ready: the work has run, or it was refused.
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

            /// <summary>On the user interface thread: runs the work.</summary>
            public void Take()
            {
                lock (_lock)
                {
                    if (_withdrawn)
                    {
                        return;
                    }

                    _started = true;
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
        }
    }

    /// <summary>
    /// Runs it here and now. For tests, and for anything assembled without a window, where there is
    /// no other thread to be on.
    /// </summary>
    public sealed class CurrentThread : IUiThread
    {
        public static readonly CurrentThread Instance = new CurrentThread();

        public bool Run(Action work, TimeSpan timeout)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            work();
            return true;
        }
    }
}
