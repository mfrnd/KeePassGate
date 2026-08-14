using System;
using KeePass.Plugins;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate
{
    /// <summary>
    /// The entry point KeePass looks for. The name is not a choice: KeePass loads
    /// <c>KeePassGate.dll</c> and instantiates the type called <c>KeePassGateExt</c> deriving from
    /// <see cref="Plugin"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately thin. It is the one class that cannot be exercised without KeePass loaded, so
    /// it does composition and nothing else, and every decision it could have made lives in a type
    /// that a test can reach.
    /// </remarks>
    public sealed class KeePassGateExt : Plugin, IDisposable
    {
        private IGateLog _log;

        /// <summary>
        /// The host, kept for the one thing that needs it after startup: KeePass's own icons, which
        /// live on the main window and are read fresh every time one is wanted.
        /// </summary>
        private IPluginHost _pluginHost;
        private GateIncidents _incidents;
        private GlobalWindowManagerWindows _windows;
        private GateHost _host;

        public override string UpdateUrl
        {
            // There is no update source to point KeePass at. An empty string keeps it from
            // checking, which is what we want rather than pointing it at nothing.
            get { return string.Empty; }
        }

        /// <summary>
        /// A fresh copy of the picture this plugin puts beside its own name, or null.
        /// </summary>
        /// <remarks>
        /// Fresh every time, because each one is handed to a dialog's image list and disposed with
        /// it. KeePass's own icons are read rather than a picture of ours shipped, so the plugin
        /// looks like the window it is in and follows a KeePass that restyles them.
        /// </remarks>
        private System.Drawing.Image Icon()
        {
            try
            {
                var window = _pluginHost == null ? null : _pluginHost.MainWindow;
                return window == null ? null : GateIcon.From(window.ClientIcons, GateIcon.Standard);
            }
            catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfReflection(error))
            {
                // Read into a local first, the way `Report` does: `Terminate` sets this field
                // to null, and a picture asked for after that would raise a null reference
                // here, inside the handler meant to keep one from escaping.
                var log = _log;
                if (log != null)
                {
                    log.Warn("KeePass's icons could not be read: " + error.Message);
                }

                return null;
            }
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null)
            {
                return false;
            }

            try
            {
                _pluginHost = host;
                _log = new TraceGateLog();
                _incidents = new GateIncidents(_log);
                _log.Info("initializing version " + GateVersion.Current);

                _windows = new GlobalWindowManagerWindows();
                _host = new GateHost(
                    _windows,
                    new TypeNameFormMatcher(KeePassContract.DatabaseSettingsFormTypeName),
                    new TabPageInjector(
                        new GateStatusPanelFactory(),
                        _log,
                        KeePassContract.DatabaseSettingsTabControlName,
                        Icon),
                    _incidents,
                    _log);

                if (!_host.Start())
                {
                    // Undone here rather than left to `Terminate`. A refused load has already
                    // subscribed to KeePass's window manager, which is static and outlives this
                    // object, and what KeePass does with a plugin that returned false is not a
                    // claim this code is in a position to make.
                    Terminate();
                    return false;
                }

                return true;
            }
            catch (Exception error)
            {
                // One of the catch-alls, and not because an exception would stop the password
                // manager: KeePass 2.61.1 catches whatever `Initialize` throws, shows a warning
                // naming the file and the exception, and goes on to the next plugin. It is here
                // for what KeePass would not do, which is undo what was set up before the throw,
                // since the window manager's events are static and outlive this object. A plugin
                // that answers false is left out without a word and never has `Terminate` called,
                // so this calls it, and the refusal is the one line `Report` writes to the log,
                // which reaches a file only when `KEEPASSGATE_LOG_FILE` names one.
                Report("initialization failed", error);
                Terminate();
                return false;
            }
        }

        public override void Terminate()
        {
            // Called by KeePass when it unloads a plugin that loaded, and by `Initialize` itself
            // when it refuses to load, which KeePass then never calls it for. Every step below
            // tolerates having nothing to do and being run twice, because `Dispose` calls it as
            // well and nothing orders the two.
            try
            {
                if (_host != null)
                {
                    _host.Stop();
                    _host = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to stop", error);
            }

            try
            {
                if (_windows != null)
                {
                    _windows.Dispose();
                    _windows = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to release the window events", error);
            }

            _incidents = null;
            _log = null;

            // KeePass's own object, and holding it after an unload keeps this plugin's graph
            // reachable from it for as long as the process lives.
            _pluginHost = null;
        }

        /// <summary>
        /// The same teardown, under the name the framework expects of a type holding disposables.
        /// </summary>
        /// <remarks>
        /// KeePass never calls this: a plugin's life is `Initialize` and `Terminate`, and
        /// `Terminate` is what releases everything here. It exists because a type that owns
        /// disposable fields and cannot be disposed is a defect in every case but this one, and a
        /// rule that has to be argued with at each site is a rule nobody keeps. Calling it twice,
        /// or calling it and then `Terminate`, is safe: every step tolerates having nothing to do.
        /// </remarks>
        public void Dispose()
        {
            Terminate();
        }

        /// <summary>
        /// Where everything this class catches goes. It is one of the places that catch
        /// every exception, so what it caught is counted as an incident and shown on the KPG tab
        /// rather than left in a log nobody opens. TODO(step 6): the showing half arrives with
        /// the tab's status line; the counting half is here.
        /// </summary>
        private void Report(string message, Exception error)
        {
            var incidents = _incidents;
            if (incidents != null)
            {
                incidents.Record(message, error);
                return;
            }

            // Before the log exists, or after it has gone. Nothing to report to and nothing to be
            // done about it.
            var log = _log;
            if (log != null)
            {
                log.Error(message, error);
            }
        }
    }
}
