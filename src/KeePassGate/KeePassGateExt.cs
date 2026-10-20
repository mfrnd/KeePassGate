using System;
using KeePass.Plugins;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Transport;
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
        private GateHost _grants;
        private GroupDialogs _groupDialogs;
        private MainWindowDatabases _databases;
        private LockWatch _locks;
        private InvitationBook _invitations;

        /// <summary>
        /// What the writer says a landed record into and every KPG tab listens to.
        /// </summary>
        /// <remarks>
        /// One for the plugin rather than one per dialog, because the writer is built once
        /// and outlives every tab: a tab subscribes when it is injected and lets go when its
        /// dialog closes.
        /// </remarks>
        private readonly AdmissionNotice _admissions = new AdmissionNotice();
        private OpenDatabaseTargets _targets;
        private GateListener _listener;

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

        /// <summary>
        /// Tell KeePass that the database it is showing has changed.
        /// </summary>
        /// <remarks>
        /// The last argument is the one that matters: it sets the modified flag and repaints, so
        /// the title bar stops claiming there is nothing to save. Nothing else is asked for, since
        /// this plugin changes no group, no entry and no selection.
        ///
        /// Wrapped because it runs from a button on a dialog, and an exception escaping there ends
        /// KeePass. A window that failed to repaint is worth a log line, not a crash.
        /// </remarks>
        private void Changed(IPluginHost host)
        {
            try
            {
                // Setting `PwDatabase.Modified` records the fact and nothing else: the title bar
                // keeps its old text and the toolbar keeps its old state, so the operator is
                // looking at a window that says everything is saved while it is not. This is the
                // call that makes the change visible, and the last argument is the one that sets
                // the flag.
                host.MainWindow.UpdateUI(false, null, false, null, false, null, true);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("KeePass was not told the database changed: " + error.Message);
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

                // KeePass's own icons, lent to the tabs for as long as this plugin is loaded. The
                // columns that remove things head themselves with its waste bin, and without a
                // KeePass to borrow from they simply have no icon.
                var main = host.MainWindow;
                if (main != null)
                {
                    GateIcon.Borrow(main.ClientIcons);
                }

                _log = new TraceGateLog();
                _incidents = new GateIncidents(_log);
                _log.Info("initializing version " + GateVersion.Current);
                // Before anything reaches for one, so that a dependency missing from beside the
                // plugin, or replaced by another version, refuses the start here, named in the
                // log, rather than failing wherever the plugin first uses it.
                Dependencies.Load();

                // The window events come first, because the gate refuses to write an admission
                // record while a group dialog is open and has to have been watching from the start
                // to know whether one is.
                _windows = new GlobalWindowManagerWindows();
                _groupDialogs = new GroupDialogs(_windows, _incidents, _log);

                if (!_groupDialogs.Start())
                {
                    // Reported rather than ignored, and the plugin still loads: everything except
                    // pairing works without this. What a watch that never started means is that
                    // `GroupDialogs.Blocking` refuses every write from here on, so the gate serves
                    // and reads and admits nobody, which is the safe half of being wrong.
                    _log.Error(
                        "the group dialogs could not be watched, so no client can be paired with " +
                        "this session", null);
                }

                _invitations = new InvitationBook(_log);

                StartListening(host);

                _host = new GateHost(
                    _windows,
                    new TypeNameFormMatcher(KeePassContract.DatabaseSettingsFormTypeName),
                    new TabPageInjector(
                        new AuthorityPanelFactory(
                            new DialogDatabases(_log, () => Changed(host)),
                            _log, _listener, _invitations, _incidents, _admissions),
                        _log,
                        KeePassContract.DatabaseSettingsTabControlName,
                        GateTab.Text,
                        GateTab.PageControlName,
                        Icon),
                    _incidents,
                    _log);

                // The grant editor watches for the other two dialogs, with its own decorator,
                // because a group dialog and the database settings carry different tabs and one
                // decorator holding both would have to be told which each time.
                _grants = new GateHost(
                    _windows,
                    new AnyFormMatcher(
                        new TypeNameFormMatcher(KeePassContract.GroupFormTypeName),
                        new TypeNameFormMatcher(KeePassContract.EntryFormTypeName)),
                    new TabPageInjector(
                        new GrantPanelFactory(new NodeDialogs(_log), _log),
                        _log,
                        KeePassContract.NodeFormTabControlName,
                        GrantTab.Text,
                        GrantTab.PageControlName,
                        Icon),
                    _incidents,
                    _log);


                // `&` rather than `&&`, so that both are started and a refusal by either still
                // leaves the other for the teardown below to take down.
                if (!(_host.Start() & _grants.Start()))
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
                GateIcon.Return();

                if (_grants != null)
                {
                    _grants.Stop();
                    _grants = null;
                }

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
                if (_listener != null)
                {
                    _listener.Dispose();
                    _listener = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to stop listening", error);
            }

            try
            {
                if (_locks != null)
                {
                    _locks.Dispose();
                    _locks = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to stop watching for locks", error);
            }

            try
            {
                if (_databases != null)
                {
                    _databases.Dispose();
                    _databases = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to release the database events", error);
            }

            try
            {
                if (_invitations != null)
                {
                    // KeePass exiting takes every outstanding invitation with it, which is what
                    // holding them in memory means and what stops a spent one coming back.
                    _invitations.Dispose();
                    _invitations = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to drop the outstanding invitations", error);
            }

            try
            {
                if (_groupDialogs != null)
                {
                    _groupDialogs.Dispose();
                    _groupDialogs = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to stop watching the group dialogs", error);
            }

            try
            {
                if (_targets != null)
                {
                    // Disposing these is what drops the host certificates. Nothing comes off
                    // the disk, because nothing was put there: every key this plugin loads is
                    // loaded with `EphemeralKeySet`, which is what `CertificateBlob` is for.
                    _targets.Dispose();
                    _targets = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to release the served databases", error);
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
        /// Starts the gate. A port that cannot be opened is reported and nothing else: the KPG tab
        /// says the gate is not listening, and KeePass carries on regardless, because a password
        /// manager that will not start because a plugin wanted a socket is a worse outcome than a
        /// plugin that does nothing.
        /// </summary>
        private void StartListening(IPluginHost host)
        {
            var mainWindow = host.MainWindow;
            if (mainWindow == null)
            {
                _log.Warn("no main window, so nothing is served");
                return;
            }

            var ui = new ControlUiThread(mainWindow);

            // The one writer, shared by every database, because pairing is a single writer by
            // design rather than by luck. `SaveDatabase` rather than `UIFileSave`: the second
            // saves whichever document is active, and pairing names the database it wrote to.
            _targets = new OpenDatabaseTargets(
                delegate { return mainWindow.DocumentManager.GetOpenDatabases(); },
                new AdmissionWriter(
                    ui,
                    database => mainWindow.SaveDatabase(database, null),
                    _admissions.Raise,
                    _groupDialogs,
                    _log),
                _log);

            _listener = new GateListener(
                GateOptions.FromEnvironment(),
                new GateResolver(_targets, _invitations, _log),
                GateOperations.Router(
                    GateVersion.Current,
                    new PairingHandler(_invitations, new PairingApprovalPrompt(ui, _log), _log)),
                _incidents,
                _log);

            _listener.Start();

            // Locking has to be watched from here rather than from the tab, because it happens
            // whether or not anybody has the settings dialog open, and because both transitions
            // close every connection the listener is holding.
            _databases = new MainWindowDatabases(mainWindow);
            // The plugin's own incidents, not a second tally: these handlers are two of the
            // places that catch everything, and the KPG tab is where the count is read.
            _locks = new LockWatch(_databases, _targets, _listener, _log, _incidents);
            _locks.Start();
        }

        /// <summary>
        /// Where everything this class catches goes. It is one of the places that catch
        /// every exception, so what it caught is counted as an incident and shown on the KPG tab
        /// rather than left in a log nobody opens.
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
