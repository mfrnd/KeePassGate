using System;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Running something on the thread that owns the windows, and giving up if it does not happen.
    /// </summary>
    /// <remarks>
    /// Pairing is the only path in this plugin that marshals synchronously, and `DESIGN.md` §119
    ///  says why the exception is confined to it: a certificate must not be handed over until the
    /// record behind it is on disk, and that needs the answer rather than a promise.
    ///
    /// **The wait is bounded, and that is what keeps the deadlock case closed.** A connection thread
    /// that cannot get the user interface's attention fails its pairing rather than holding a socket
    /// and a lock until somebody notices. `Control.Invoke` offers no timeout at all, which is why
    /// the implementation uses `BeginInvoke` and waits on the handle it returns.
    /// https://learn.microsoft.com/dotnet/api/system.windows.forms.control.begininvoke
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
        /// already been turned away. TODO(step 8): pairing and its approval dialog arrive with
        /// this step.
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
        /// it never started. Every caller already stands under one of the catch-alls, so an
        /// exception nothing on the way up names is counted and logged there; the implementation
        /// over a `Control` says why it has to carry it rather than let it travel.
        /// </remarks>
        bool Run(Action work, TimeSpan timeout);
    }

    /// <summary>The real one, over any control whose handle belongs to the user interface thread.</summary>
    public sealed class ControlUiThread : IUiThread
    {
        private readonly Control _control;

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

            var marshaled = new Marshaled(work);
            var pending = _control.BeginInvoke((MethodInvoker)marshaled.Run);

            if (pending.AsyncWaitHandle.WaitOne(timeout))
            {
                // `EndInvoke` is what ends the call. What the work threw is not carried by it and
                // is rethrown separately, for the reason on `Marshaled.Run`.
                _control.EndInvoke(pending);
                marshaled.Rethrow();
                return true;
            }

            if (marshaled.Abandon())
            {
                // Queued and never started, so nothing happened and nothing will. `EndInvoke` is
                // deliberately not called: it would block this thread until the user interface
                // drains its queue, which is the wait this timeout exists to escape. What is left
                // behind is one queued no-op.
                return false;
            }

            // Already running, so the only honest answer is to wait for it. Bounded by the work
            // rather than by a clock, and the work is nearly always bounded: the write is a save,
            // and the dialog closes itself. The exception is a save that stops to ask something or
            // has already gone wrong, which KeePass puts in a message box this wait then lasts
            // until somebody answers. Returning here instead would let a caller release a lock
            // while its own write was still going on.
            pending.AsyncWaitHandle.WaitOne();
            _control.EndInvoke(pending);
            marshaled.Rethrow();
            return false;
        }

        /// <summary>
        /// Work that can still be called off, up to the instant it starts, and that never lets an
        /// exception out on the user interface thread.
        /// </summary>
        /// <remarks>
        /// `BeginInvoke` offers no way to take something back out of the queue, and the queue is
        /// exactly where work sits while a user interface is too busy to answer. The latch is what
        /// makes the timeout mean "this did not happen" rather than "this has not happened yet".
        ///
        /// **The catch is total, and it is not a fifth catch-all.** A marshaled callback that lets
        /// an exception out of it does not simply fail the `EndInvoke`: `Control` hands the
        /// exception to `Application.OnThreadException` on the user interface thread as well,
        /// because `BeginInvoke` is an asynchronous entry, and KeePass registers no
        /// `Application.ThreadException` handler and sets no unhandled exception mode. WinForms
        /// therefore shows its own "Unhandled exception has occurred in your application" dialog
        /// inside the password manager, whose Quit ends the process outside KeePass's own shutdown,
        /// while the connection thread separately turns the same exception into a refusal. Measured
        /// on .NET Framework 4.8 with a form on one thread and a throwing `BeginInvoke` from
        /// another: with a handler registered it fired on the user interface thread before
        /// `EndInvoke` was ever called, and with none registered WinForms went modal instead.
        ///
        /// Nothing is absorbed here, which is what separates this from the places that catch
        /// everything: the exception is carried to the thread that asked for the work and thrown
        /// there by `Rethrow`, which is where a filter can name it or a catch-all can count it.
        /// Recording it here instead would take a `GateFailure` a caller names away from the caller
        /// and report a defect twice.
        /// </remarks>
        private sealed class Marshaled
        {
            private readonly object _gate = new object();
            private readonly Action _work;

            private bool _started;
            private bool _abandoned;
            private Exception _failure;

            public Marshaled(Action work)
            {
                _work = work;
            }

            public void Run()
            {
                lock (_gate)
                {
                    if (_abandoned)
                    {
                        return;
                    }

                    _started = true;
                }

                try
                {
                    _work();
                }
                catch (Exception error)
                {
                    lock (_gate)
                    {
                        _failure = error;
                    }
                }
            }

            /// <summary>Calls it off. False when it had already begun and cannot be.</summary>
            public bool Abandon()
            {
                lock (_gate)
                {
                    if (_started)
                    {
                        return false;
                    }

                    _abandoned = true;
                    return true;
                }
            }

            /// <summary>
            /// Throws whatever the work threw, here, and does nothing when it threw nothing.
            /// </summary>
            /// <remarks>
            /// `ExceptionDispatchInfo` rather than `throw` so that the stack the work failed on
            /// survives the crossing, which is the whole value of the exception to whoever reads
            /// the log. `Control.EndInvoke` rethrows the bare object and loses it.
            /// </remarks>
            public void Rethrow()
            {
                Exception failure;
                lock (_gate)
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
