using System;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate
{
    /// <summary>
    /// The plugin's lifetime, expressed without any reference to KeePass. It watches for dialogs
    /// opening, decorates the ones it recognizes, and undoes all of it on the way out.
    /// </summary>
    /// <remarks>
    /// Start and stop are idempotent and safe in any order, because the entry point stops
    /// everything itself when it refuses to load part way through starting, KeePass calling
    /// Terminate only on a plugin that loaded, and because a dialog can be announced while the
    /// plugin is being torn down.
    /// </remarks>
    public sealed class GateHost : IDisposable
    {
        private readonly IWindowEventSource _windows;
        private readonly IFormMatcher _matcher;
        private readonly IFormDecorator _decorator;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;
        private readonly object _stateLock = new object();
        private bool _running;

        public GateHost(IWindowEventSource windows, IFormMatcher matcher, IFormDecorator decorator, IGateLog log)
            : this(windows, matcher, decorator, null, log)
        {
        }

        public GateHost(
            IWindowEventSource windows,
            IFormMatcher matcher,
            IFormDecorator decorator,
            GateIncidents incidents,
            IGateLog log)
        {
            if (windows == null)
            {
                throw new ArgumentNullException(nameof(windows));
            }

            if (matcher == null)
            {
                throw new ArgumentNullException(nameof(matcher));
            }

            if (decorator == null)
            {
                throw new ArgumentNullException(nameof(decorator));
            }

            _windows = windows;
            _matcher = matcher;
            _decorator = decorator;
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        public bool IsRunning
        {
            get
            {
                lock (_stateLock)
                {
                    return _running;
                }
            }
        }

        public bool Start()
        {
            lock (_stateLock)
            {
                if (_running)
                {
                    return true;
                }

                try
                {
                    _windows.WindowAdded += OnWindowAdded;
                    _windows.WindowRemoved += OnWindowRemoved;
                    _running = true;
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("failed to start", error);
                    Unsubscribe();
                    _running = false;
                    return false;
                }
            }

            _log.Info("started, watching for " + _matcher);
            return true;
        }

        public void Stop()
        {
            bool wasRunning;
            lock (_stateLock)
            {
                // Unsubscribed whether or not the start finished. `Start` sets `_running` only
                // after both subscriptions succeed, so a throw between them leaves one live
                // with the flag still false, and returning early here left it subscribed to a
                // static event for the life of the process.
                wasRunning = _running;
                _running = false;
                Unsubscribe();

                // Swept while still holding the lock the window handler takes, so a dialog
                // opening at this moment either finishes before the sweep and is taken off
                // by it, or finds the flag already false and never attaches. Releasing here
                // first left a third case: attached after the sweep, on a live dialog, with
                // the plugin already saying it had stopped.
                if (wasRunning)
                {
                    try
                    {
                        _decorator.DetachAll();
                    }
                    catch (Exception error) when (Expected.OfUserInterface(error))
                    {
                        _log.Error("failed to detach while stopping", error);
                    }
                }
            }

            if (!wasRunning)
            {
                return;
            }

            _log.Info("stopped");
        }

        public void Dispose()
        {
            Stop();
        }

        private void Unsubscribe()
        {
            try
            {
                _windows.WindowAdded -= OnWindowAdded;
                _windows.WindowRemoved -= OnWindowRemoved;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("failed to unsubscribe from window events", error);
            }
        }

        private void OnWindowAdded(object sender, GateWindowEventArgs e)
        {
            Handle(e, true);
        }

        private void OnWindowRemoved(object sender, GateWindowEventArgs e)
        {
            Handle(e, false);
        }

        private void Handle(GateWindowEventArgs e, bool opening)
        {
            // Nothing below may throw: this is a KeePass event handler on the UI thread.
            try
            {
                if (e == null)
                {
                    return;
                }

                Form form = e.Form;
                if (!_matcher.Matches(form))
                {
                    return;
                }

                // The running check and the decoration are one step. Reading the flag and then
                // acting on it let a stop land in between: the sweep in `Stop` passed while this
                // was still building a page, and the tab it went on to record then outlived it on
                // a live dialog. `Stop` sweeps under this same lock, so the two cannot interleave.
                // Both take this lock before the decorator's own and nothing takes them the other
                // way round, so the order is fixed.
                lock (_stateLock)
                {
                    if (!_running)
                    {
                        return;
                    }

                    if (opening)
                    {
                        _decorator.Attach(form);
                    }
                    else
                    {
                        _decorator.Detach(form);
                    }
                }
            }
            catch (Exception error)
            {
                // One of the catch-alls: KeePass raises its window events on its own thread, from
                // inside a dialog that is loading or closing, with nothing around them. An
                // exception that escapes stops that dialog's own code part way and then reaches
                // WinForms, which puts its own error dialog over KeePass, offering to quit.
                _incidents.Record(
                    opening ? "failed to handle an opening window" : "failed to handle a closing window", error);
            }
        }
    }
}
