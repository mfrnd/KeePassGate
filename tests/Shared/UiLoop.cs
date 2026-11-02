using System;
using System.Threading;
using System.Windows.Forms;

namespace KeePassGate.Testing
{
    /// <summary>KeePass's thread: a control with a handle, on a message loop of its own.</summary>
    /// <remarks>
    /// A real `Application.Run` loop rather than a test pumping by hand, because what is under test
    /// is when that loop gets round to things: it raises `Application.Idle` once its queue is
    /// empty, which is when `ControlUiThread` starts a turn, and a pump made of `DoEvents` never
    /// does.
    /// </remarks>
    internal sealed class UiLoop : IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim();

        public UiLoop()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "KeePass's thread" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(Patience);
        }

        public Control Control { get; private set; }

        public void Invoke(Action action)
        {
            Control.Invoke(action);
        }

        /// <summary>Makes something on the loop's thread, to be disposed there again.</summary>
        /// <remarks>
        /// For what belongs to the thread that made it: a WinForms timer, or a `ControlUiThread`
        /// listening for that thread's Idle. Disposed from the test's own thread instead, either
        /// would go on running on the loop's.
        /// </remarks>
        public Owned<T> Own<T>(Func<T> make)
            where T : class, IDisposable
        {
            T made = null;
            Invoke(() => made = make());
            return new Owned<T>(this, made);
        }

        /// <summary>What <see cref="Own"/> made, disposed on the loop's thread with this.</summary>
        public sealed class Owned<T> : IDisposable
            where T : class, IDisposable
        {
            private readonly UiLoop _loop;

            public Owned(UiLoop loop, T value)
            {
                _loop = loop;
                Value = value;
            }

            public T Value { get; private set; }

            public void Dispose()
            {
                var value = Value;
                if (value == null)
                {
                    return;
                }

                Value = null;
                _loop.Invoke(value.Dispose);
            }
        }

        public void Dispose()
        {
            try
            {
                Control.BeginInvoke((MethodInvoker)(() =>
                {
                    Control.Dispose();
                    Application.ExitThread();
                }));
            }
            catch (InvalidOperationException)
            {
                // Gone already.
            }

            _thread.Join(Patience);
            _ready.Dispose();
        }

        private void Run()
        {
            Control = new Control();
            Control.CreateControl();
            GC.KeepAlive(Control.Handle);
            _ready.Set();
            using (var context = new ApplicationContext())
            {
                Application.Run(context);
            }
        }
    }
}
