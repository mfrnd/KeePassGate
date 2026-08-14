using System;
using KeePass.UI;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The one adapter that knows how KeePass announces its dialogs. It is deliberately the
    /// thinnest thing in the plugin: it translates and forwards, and holds no state of its own
    /// beyond the subscription, because nothing above it can be unit tested against a real
    /// KeePass.
    /// </summary>
    public sealed class GlobalWindowManagerWindows : IWindowEventSource, IDisposable
    {
        private readonly object _stateLock = new object();
        private bool _disposed;

        public GlobalWindowManagerWindows()
        {
            GlobalWindowManager.WindowAdded += OnWindowAdded;
            GlobalWindowManager.WindowRemoved += OnWindowRemoved;
        }

        private EventHandler<GateWindowEventArgs> _windowAdded;
        private EventHandler<GateWindowEventArgs> _windowRemoved;

        /// <summary>
        /// Raised when KeePass announces a window opening.
        /// </summary>
        /// <remarks>
        /// Written out rather than left to the compiler so that subscribing after `Dispose`
        /// is refused. Disposing unsubscribes from KeePass's own manager, so a handler added
        /// afterwards would never be called and the caller would be told it had started
        /// watching when nothing could reach it. `ObjectDisposedException` is in
        /// `Expected.OfUserInterface`, so a start that hits this reports failure.
        /// </remarks>
        public event EventHandler<GateWindowEventArgs> WindowAdded
        {
            add { lock (_stateLock) { Refuse(); _windowAdded += value; } }
            remove { lock (_stateLock) { _windowAdded -= value; } }
        }

        /// <summary>
        /// Raised when KeePass announces a window closing. Refused after `Dispose`, as above.
        /// </summary>
        public event EventHandler<GateWindowEventArgs> WindowRemoved
        {
            add { lock (_stateLock) { Refuse(); _windowRemoved += value; } }
            remove { lock (_stateLock) { _windowRemoved -= value; } }
        }

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            GlobalWindowManager.WindowAdded -= OnWindowAdded;
            GlobalWindowManager.WindowRemoved -= OnWindowRemoved;

            lock (_stateLock)
            {
                _windowAdded = null;
                _windowRemoved = null;
            }
        }

        private void Refuse()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(typeof(GlobalWindowManagerWindows).FullName);
            }
        }

        private void OnWindowAdded(object sender, GwmWindowEventArgs e)
        {
            Raise(_windowAdded, e);
        }

        private void OnWindowRemoved(object sender, GwmWindowEventArgs e)
        {
            Raise(_windowRemoved, e);
        }

        private void Raise(EventHandler<GateWindowEventArgs> handler, GwmWindowEventArgs e)
        {
            if (handler == null || e == null)
            {
                return;
            }

            handler(this, new GateWindowEventArgs(e.Form));
        }
    }
}
