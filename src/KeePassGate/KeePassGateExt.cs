using System;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePassGate.Audit;
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
        private AuthorityChooser _chooser;
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

        /// <summary>
        /// The renewals the operator refused in this session, which the gate answers from and
        /// every KPG tab shows and lifts.
        /// </summary>
        /// <remarks>
        /// The plugin's own, for the reason the notice above is: the handler is built once and
        /// outlives every tab, and a tab holding a set of its own would show answers the gate is
        /// not giving and lift ones it goes on giving.
        /// </remarks>
        private readonly SessionRefusals _sessionRefusals = new SessionRefusals();

        /// <summary>
        /// The approvals the operator gave that KeePass was too busy to write, held for the client's
        /// retry, which pairing and renewal share and a lock, a close and an exit clear.
        /// </summary>
        private readonly ApprovalBook _approvals = new ApprovalBook();
        private JsonLinesAudit _audit;
        private OpenDatabaseTargets _targets;
        private GateListener _listener;
        // The seam rather than the class, because the seam is what the window is written
        // against and a field typed at the implementation is a claim about testing that nothing
        // is making good on.
        private IGateSettings _settings;

        public override string UpdateUrl
        {
            // There is no update source to point KeePass at. An empty string keeps it from
            // checking, which is what we want rather than pointing it at nothing.
            get { return string.Empty; }
        }

        /// <summary>
        /// Which port to listen on: what the operator chose, or what the environment says, or the
        /// default. Zero when the environment names something that is not a port.
        /// </summary>
        /// <remarks>
        /// The operator's choice comes first, because it is the only one of the three a person
        /// made deliberately. The environment variable stays underneath it so that a test can take
        /// a free port without touching anybody's settings, which is what it was always for.
        /// `GateOptions.PortToTake` is where that order is decided, and where a variable that is
        /// set to something other than a port stops the gate rather than being passed over.
        /// </remarks>
        private int Wanted()
        {
            string refused;
            return Wanted(out refused);
        }

        /// <summary>The same, and why there is no port when there is none.</summary>
        private int Wanted(out string refused)
        {
            var chosen = _settings == null ? 0 : _settings.Port;
            return GateOptions.PortToTake(chosen, GateOptions.PortFromEnvironment(), out refused);
        }

        /// <summary>
        /// Opens the socket on the port `Wanted` names. Answers with why it could not, or null
        /// when it did.
        /// </summary>
        /// <remarks>
        /// Called once, from `Initialize`. A port is bound once and never rebound, which is what
        /// `ChangePort` explains: an operator's new port is saved and taken at the next start, so
        /// there is never a second listener and never an old one to close first.
        /// </remarks>
        private string Listen(GateResolver resolver, IGateHandler router)
        {
            // No old listener to close. This runs once, from `Initialize`, because a port is bound
            // once and never rebound, which is what `ChangePort` explains.
            string refused;
            var port = Wanted(out refused);

            _listener = new GateListener(
                new GateOptions { Port = port }, resolver, router, _incidents, _log);

            // **A variable naming something that is not a port stops the gate** rather than the
            // default being bound in its place. The listener keeps the reason and is never
            // started, so the plugin's window says why nothing is listening in the words the log
            // and the box at startup use.
            if (refused != null)
            {
                _log.Error(refused, null);
                _listener.Refuse(refused);
                return refused;
            }

            if (_listener.Start())
            {
                return null;
            }

            // What the listener kept, which is the whole point of its having kept it. Sending the
            // operator to the log here would be this commit building a surface to reach them with
            // and then using it to say the answer is somewhere else. The fallback is for a later
            // refusal that forgets to record one: an empty box says even less than the log line.
            return _listener.Refusal
                ?? "nothing is listening on 127.0.0.1:" + port + ". The log says why.";
        }

        /// <summary>
        /// Takes a port from the operator and writes it down. **It does not open one.**
        /// </summary>
        /// <remarks>
        /// **A port is bound once, at startup, and never again**, and this reverses an earlier
        /// decision rather than refining it. Changing it used to close the socket and open another
        /// while the operator watched, which had two things wrong with it. It acted at once from a
        /// window that also offers Cancel, so a setting was live before the dialog it was typed
        /// into had been answered. And it could half fail: the old socket is given up before the
        /// new one is asked for, so a port something else already held left this KeePass serving
        /// nobody, from a gesture whose worst outcome should have been nothing happening.
        ///
        /// Written down and read at the next start. The one thing that is lost is immediacy, and
        /// what is bought is that the gate a client is talking to cannot be moved out from under
        /// it by somebody looking at a settings window.
        /// </remarks>
        private string ChangePort(int port)
        {
            return _settings.Save(port);
        }

        /// <summary>
        /// The plugin's entry in KeePass's own menus: one item, under Tools.
        /// </summary>
        /// <remarks>
        /// Through `GetMenuItem` rather than by reaching into `ToolsMenu.DropDownItems`, because
        /// this is the seam KeePass offers: it decides where the item goes and removes it when the
        /// plugin is unloaded, and a plugin that added and removed its own items has to get the
        /// second half right on every path out.
        ///
        /// **Only under Tools.** The group and entry menus already lead to the dialogs this plugin
        /// puts a tab on, and an item in the tray menu would be a second route to a window that
        /// says nothing about whichever database is open.
        ///
        /// It opens rather than answers. What it shows is what the plugin is and whether the gate
        /// is listening, which are questions about the plugin and are wanted when no database is
        /// open at all, which is exactly when a tab inside a database's settings cannot be reached.
        /// </remarks>
        public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
        {
            if (t != PluginMenuType.Main)
            {
                return null;
            }

            // With a mnemonic, because every item KeePass puts in that menu has one and an item
            // without one is the odd entry a keyboard cannot reach. `K` is free: the menu already
            // spends g, l, t, d, a, r, p and o on its own items.
            var item = new ToolStripMenuItem("&" + GateTab.MenuItemText)
            {
                Name = GateTab.MenuItemName,

                // The same picture the three tabs carry, so that everything this plugin puts into
                // KeePass is recognizable as one plugin's doing.
                Image = Icon()
            };

            item.Click += (sender, e) => ShowStatus();
            return item;
        }

        /// <summary>
        /// A fresh copy of the picture this plugin puts beside its own name, or null.
        /// </summary>
        /// <remarks>
        /// Fresh every time, because each one is handed somewhere that copies it rather than
        /// borrows it: an `ImageList` copies into its own handle, and a menu item keeps the
        /// reference it was given. **Neither disposes what it was handed**, which is why
        /// `TabPageInjector.Decorate` disposes its copy itself and says so; the one this hands to
        /// the Tools menu is 16 by 16 and lives as long as the menu does, which is the process.
        /// KeePass's own icons are read rather than a picture of ours shipped, so the plugin looks
        /// like the window it is in and follows a KeePass that restyles them.
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
        /// Opens the status window.
        /// </summary>
        /// <remarks>
        /// A catch-all, like the handlers of KeePass's own events: a menu item runs on KeePass's
        /// own thread, and an exception escaping it reaches WinForms, which puts its own error
        /// dialog over KeePass, with a Quit that ends the process outside KeePass's own shutdown. A
        /// window that would not open is worth a log line and an incident of its own, not that
        /// dialog.
        /// </remarks>
        private void ShowStatus()
        {
            try
            {
                string said = null;

                using (var window = new GateStatusForm(_listener, _incidents, ChangePort, Wanted()))
                {
                    window.ShowDialog();

                    // What OK answered, taken before the window goes. A button carrying
                    // `DialogResult.OK` has already set it by the time its `Click` runs, so the
                    // sentence the handler writes onto the window is painted onto a form the modal
                    // loop is closing: nobody has ever read it. It matters because it is not a
                    // receipt. It says the port waits for the next start, and that every client
                    // paired against the old one has to be told the new one.
                    if (window.Changed)
                    {
                        said = window.Result;
                    }
                }

                if (said != null)
                {
                    new MessageBoxPrompt().Tell(GateName.Full, said);
                }
            }
            catch (Exception error)
            {
                _incidents.Record("the status window could not be shown", error);
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
        /// Wrapped because it runs from a button on a dialog, and an exception escaping there
        /// reaches WinForms' own error dialog, which offers to quit KeePass. A window that failed
        /// to repaint is worth a log line, not that dialog.
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

        /// <summary>
        /// Rebuild the group tree and the entry list, after a write through the API changed what
        /// they are showing, or redraw only the title bar and the menus when they are not.
        /// </summary>
        /// <remarks>
        /// **These are `MainForm.UpdateUISyncPost`'s seven arguments**, which is the call KeePass
        /// makes to itself after a synchronize. That is the same situation as this one and the only
        /// other one KeePass has: the database changed without anybody typing in the window, and
        /// the change is already on disk. Its own editing paths pass different arguments because
        /// they know more, adding one row or removing the rows they deleted rather than rebuilding.
        ///
        /// So: no tab bar, because the set of open documents did not change; no document selected,
        /// because a write must not pull a background database to the front; both lists rebuilt;
        /// neither a group nor an entry source named, which is what keeps the operator's selection,
        /// their scroll position and their expanded nodes, since `UpdateGroupList` and
        /// `UpdateEntryList` fall back to what is selected now; and not modified, because
        /// `DatabaseWriter` has saved already and saying otherwise would put the title bar back to
        /// claiming there is something to save.
        ///
        /// **Only for the database in front.** The tree and the list show the active document and
        /// nothing else, and KeePass rebuilds both when another one is brought forward, so a write
        /// to a database in another tab rebuilds nothing: it would replace what the operator is
        /// looking at in one database for a change made to a different one. That write, and a
        /// change taken back out, redraw the title bar, the menus and the tabs alone, which is the
        /// same call with both lists left as they are.
        ///
        /// **What it costs, said rather than denied.** A rebuild replaces whatever the entry list
        /// is showing with the selected group's entries, so an operator looking at search results
        /// when a write lands in that database loses them. KeePass accepts that for a synchronize;
        /// there is no cheaper call that would show a created or deleted entry, and the alternative
        /// is a window that goes on showing a database that is no longer there. It resets the
        /// inactivity timer through `NotifyUserActivity`, which changes nothing: `SaveDatabase`
        /// ends in `UpdateUIState`, which already does that, so every write already postponed the
        /// lock.
        ///
        /// Not wrapped here. `DatabaseWriter.Redraw` catches, because it is the one that knows the
        /// write has landed and must not report otherwise.
        /// </remarks>
        private static void Redrawn(KeePass.Forms.MainForm main, KeePassLib.PwDatabase database, bool lists)
        {
            var rebuild = lists && ReferenceEquals(main.ActiveDatabase, database);
            main.UpdateUI(false, null, rebuild, null, rebuild, null, false);
        }

        /// <summary>
        /// Why KeePass is part way through an operation of its own, or null when it is not.
        /// </summary>
        /// <remarks>
        /// `DatabaseWriter` says why this is asked at all: a write is a message, and every
        /// operation KeePass takes a noticeable time over pumps the message queue while it runs,
        /// so a write is delivered in the middle of one unless somebody asks first.
        ///
        /// **Two answers rather than one, because the operations do not announce themselves the
        /// same way.** `UIIsInteractionBlocked` is what KeePass's own save raises, and what both
        /// merges raise through their status loggers, so it covers `SaveDatabase`, File > Import,
        /// File > Synchronize and opening a database. It is not what an export raises:
        /// `PerformExport` never blocks interaction and brackets the whole export with
        /// `MessageService.ExternalIncrementMessageCount` instead, so the count is the only signal
        /// there is for the one operation that walks the live tree for as long as a save does.
        /// The count also covers every message box KeePass is waiting on an answer to, which is
        /// the plainest case of it being part way through something.
        ///
        /// **What the count holds shut during an export is read off KeePass 2.61.1, not driven.**
        /// `ExportUtil.Export` sets the database's `MasterKey` and `RootGroup` to the export's
        /// when the export dialog is told to use a key of its own or to include the parent groups,
        /// and puts them back in a `finally`, so a save landing in between would write the live
        /// file under the export's key or with the export's filtered tree. `MainForm.PerformExport`,
        /// the one path behind File, Group and Entry > Export, raises the count before calling it
        /// and lowers it after, in its IL, with no `finally`: an export that throws out of it
        /// leaves the count raised, which refuses writes rather than letting one through. No test
        /// drives it, because `PerformExport` opens KeePass's export dialog and so needs a running
        /// KeePass and somebody to answer it. The trigger action that exports a database raises no
        /// count and needs none: it builds its export without that dialog, so nothing is swapped,
        /// and a write landing inside it would save the database exactly as it is.
        ///
        /// **The count is not this plugin's own.** Nothing here shows a `MessageService` box, and
        /// the pairing prompt is a form of its own, so the count is KeePass's word about KeePass.
        /// **The block can be**: the save this writer makes raises it for as long as it runs. A
        /// write never meets its own, because the question is asked before the save rather than
        /// during it, but a pairing or a renewal asking from another connection does, and
        /// `DatabaseWriter.Blocked` tells that save apart and waits for it rather than reading it
        /// as KeePass being busy.
        /// </remarks>
        private static string Interrupting(KeePass.Forms.MainForm main)
        {
            if (main.UIIsInteractionBlocked())
            {
                return "KeePass is in the middle of an operation of its own, such as saving, " +
                       "importing or synchronizing";
            }

            if (KeePassLib.Utility.MessageService.CurrentMessageCount != 0)
            {
                return "KeePass is exporting, or is waiting for an answer to a window of its own";
            }

            return null;
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null)
            {
                return false;
            }

            _pluginHost = host;

            try
            {
                // KeePass's own icons, lent to the tabs for as long as this plugin is loaded. The
                // columns that remove things head themselves with its waste bin, and without a
                // KeePass to borrow from they simply have no icon.
                var main = host.MainWindow;
                if (main != null)
                {
                    GateIcon.Borrow(main.ClientIcons);
                }

                // KeePass's own windows keep out of screen captures when it is set to, and it does
                // that only for windows it registers, which this plugin's do not: `CaptureGuard`
                // applies the same to them, asking KeePass's setting as each is made.
                CaptureGuard.Wanted = () => KeePass.Program.Config.Security.PreventScreenCapture;

                _log = new TraceGateLog();
                _incidents = new GateIncidents(_log);
                _log.Info("initializing version " + GateVersion.Current);
                // Before anything reaches for one, so that a dependency missing from beside the
                // plugin, or replaced by another version, refuses the start here, named in the
                // log, rather than failing wherever the plugin first uses it.
                Dependencies.Load();

                // The window events come first, because the gate refuses to write while a dialog
                // that would take the write back is open, and has to have been watching from the
                // start to know whether one is.
                _windows = new GlobalWindowManagerWindows();
                _groupDialogs = new GroupDialogs(_windows, _incidents, _log);

                if (!_groupDialogs.Start())
                {
                    // Reported rather than ignored, and the plugin still loads: reading works
                    // without this. What a watch that never started means is that every question
                    // `GroupDialogs` answers refuses from here on, so the gate serves and reads,
                    // and writes nothing and admits nobody, which is the safe half of being wrong.
                    _log.Error(
                        "the dialogs that would take a write back could not be watched, so nothing " +
                        "is written and no client can be paired with this session", null);
                }

                _invitations = new InvitationBook(_log);

                // The audit is opened before anything can be decided, and it is the plugin's own
                // file rather than the database's: a record of what an agent reached, kept inside
                // the thing it reached, is a record whoever compromised the database can edit.
                _audit = new JsonLinesAudit(_log);

                // One check for the gate's decisions and the record of what happens to admissions,
                // the operator's revocations on the tab among them, so that the file has one clock
                // and a client one allowance for everything it can make the file say.
                var access = new AccessCheck(_audit);

                StartListening(host, access);

                _host = new GateHost(
                    _windows,
                    new TypeNameFormMatcher(KeePassContract.DatabaseSettingsFormTypeName),
                    new TabPageInjector(
                        new AuthorityPanelFactory(
                            new DialogDatabases(_log, () => Changed(host)),
                            new MessageBoxPrompt(),
                            _log,
                            _invitations,
                            _admissions,
                            _incidents,
                            access.Admissions,
                            _sessionRefusals),
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
                // a file beside the audit unless `KEEPASSGATE_LOG_FILE` names another.
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
                CaptureGuard.Wanted = () => false;

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

                // An exit takes every held approval with it, as a lock does.
                _approvals.Clear();

                TellWhatIsStillOwed();
            }
            catch (Exception error)
            {
                Report("failed to stop watching for locks", error);
            }

            try
            {
                if (_chooser != null)
                {
                    _chooser.Dispose();
                    _chooser = null;
                }
            }
            catch (Exception error)
            {
                Report("failed to stop the authority chooser", error);
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
        /// Starts the gate. A port that cannot be opened is reported and nothing else: the window says
        /// the gate is not listening, and KeePass carries on regardless, because a password manager
        /// that will not start because a plugin wanted a socket is a worse outcome than a plugin
        /// that does nothing.
        /// </summary>
        /// <param name="access">
        /// The record and the rules, combined and written down, and with them the record of what
        /// happens to admissions, which pairing, renewal and a client's revocation write to.
        /// </param>
        private void StartListening(IPluginHost host, AccessCheck access)
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
            //
            // The redraw beside it is the other half of a write landing, and `Redrawn` says what
            // its seven arguments are and where they came from. `Interrupting` is the question
            // that keeps a write out of the middle of whatever KeePass is already doing, and it
            // is wired here because it is the one part of the writer that has to reach into
            // KeePass's own window.
            //
            // `WindowEnabling` is around the save rather than inside it because the save is
            // KeePass's: `SaveDatabase` ends by assigning `Enabled = true` to the main window,
            // which is harmless for KeePass, whose own callers never save inside a modal loop,
            // and is this plugin re-enabling the window behind an operator's open dialog.
            var writer = new DatabaseWriter(
                ui,
                database => WindowEnabling.Preserve(
                    mainWindow, () => mainWindow.SaveDatabase(database, null)),
                () => Interrupting(mainWindow),
                (database, lists) => Redrawn(mainWindow, database, lists),
                _admissions.Raise,
                _groupDialogs,
                _log);

            // The standing answers the operator gave while a database could not take them, which
            // the writer's next write to that database that lands writes as well.
            writer.Owed = _sessionRefusals;

            _targets = new OpenDatabaseTargets(
                delegate { return mainWindow.DocumentManager.GetOpenDatabases(); },
                writer,
                _log);

            _settings = new KeePassSettings(_log);

            // Locals rather than fields, because nothing outside this call needs either and a
            // field would be a second way to reach KeePass's own window: the router holds the
            // pairing handler, which holds the prompt, which holds the control every approval is
            // marshaled to. `Terminate` lets go of the listener, and letting go of the listener
            // has to be letting go of all of it.
            // One prompt for both, because a pairing and a renewal wait their turn for the same
            // screen. The writer too, because a dialog is a marshaled call like a write and is
            // delivered inside whatever is pumping: the prompt waits out the writer's own save and
            // refuses inside KeePass's, as the writer does.
            var prompt = new PairingApprovalPrompt(ui, _targets.Serving, writer, _log);
            var router = GateOperations.Router(
                GateVersion.Current,
                GateVersion.Revision,
                new PairingHandler(_invitations, prompt, _approvals, access.Admissions, _log),
                new RenewalHandler(prompt, _sessionRefusals, _approvals, access.Admissions, _log),
                access);

            var resolver = new GateResolver(_targets, _invitations, _log);

            // **A gate that cannot bind says so once, and stays down for the session**, and so does
            // one given no port it may take, because `KEEPASSGATE_PORT` names something else.
            // Nothing rebinds after this: the port is taken here or it is not taken at all. So the
            // reason has to reach the person in front of the screen rather than only the log,
            // because otherwise the first they hear of it is a client that will not connect, and
            // nothing they can do inside KeePass would have told them.
            //
            // Shown once the window is, rather than from here. A modal box raised from `Initialize`
            // holds up the load of every plugin after this one and of the window itself, and one
            // posted from here does not wait long enough: `Announce` says what it met instead.
            var refused = Listen(resolver, router);
            if (refused != null)
            {
                Announce(mainWindow, refused);
            }

            // Locking has to be watched from here rather than from the tab, because it happens
            // whether or not anybody has the settings dialog open, and because both transitions
            // close every connection the listener is holding.
            _databases = new MainWindowDatabases(mainWindow);
            // The listener is asked for each time rather than handed over once. That is about
            // construction order rather than about rebinding, which this commit takes away: the
            // port is bound once at startup, so the listener this hands back is the only one there
            // will be. The incidents are the plugin's own rather than a second
            // tally: these handlers catch everything, as every handler of KeePass's own events
            // here does, and the window under Tools is where the count is read.
            // The prompt too, because a lock closes the question it has on the screen, and the
            // approvals held for a retry, because a lock or a close lets go of every one of them.
            _locks = new LockWatch(
                _databases, _targets, () => _listener, _log, _incidents, new ApprovalScreens(prompt, _approvals));
            _locks.Start();

            // After the lock watch, so that an unlock has been read by the time the question is
            // posted; and through the one writer, whose guard is what a choice has to pass. The
            // audit is asked for where it is writing each time, for the last request of each
            // client the dialog lists.
            _chooser = new AuthorityChooser(
                _databases,
                _targets,
                writer,
                mainWindow,
                () => Interrupting(mainWindow),
                () => _audit == null ? null : _audit.Path,
                new MessageBoxPrompt(),
                _incidents,
                _log);
            _chooser.Start();
        }

        /// <summary>
        /// Tells the operator about standing answers to renewals that no database took before
        /// KeePass exits, which go with it.
        /// </summary>
        /// <remarks>
        /// The operator's ruling of 2026-10-14: an answer forever or until a date given while
        /// KeePass was busy holds from memory and is written by the plugin's next write to that
        /// database, and one that still cannot be written by then is told to the operator rather
        /// than only to the log. Exit is when that becomes true for good: a lock or a close keeps
        /// it, and the next write after an unlock or an open writes it. Said once, on the way out,
        /// so that the operator knows which programs will be asked about again.
        /// </remarks>
        private void TellWhatIsStillOwed()
        {
            var owed = _sessionRefusals.Owed();
            if (owed.Count == 0)
            {
                return;
            }

            var log = _log;
            if (log != null)
            {
                log.Warn(owed.Count + (owed.Count == 1 ? " standing answer" : " standing answers") +
                         " to renewing a certificate never reached the database, and go now that KeePass exits");
            }

            new MessageBoxPrompt().Tell(
                GateName.Full,
                (owed.Count == 1
                    ? "A standing answer to renewing a client's certificate, refused forever or until a "
                      + "date, was never written to its database, because KeePass was busy whenever it "
                      + "was given and nothing was written after it."
                    : owed.Count + " standing answers to renewing clients' certificates, refused forever "
                      + "or until a date, were never written to their databases, because KeePass was busy "
                      + "whenever they were given and nothing was written after them.")
                + Environment.NewLine + Environment.NewLine
                + "They held for this session and go now. The next time those clients ask to renew, "
                + "the dialog comes up again.");
        }

        /// <summary>
        /// Puts one sentence in front of the operator, once KeePass has a window to put it over.
        /// </summary>
        /// <remarks>
        /// The plugin is still loaded and everything on its window still works. What is gone until
        /// the next start is the socket, and saying only that half would read as the plugin having
        /// failed altogether.
        ///
        /// **Once the main window has been shown, and not before.** `Initialize` runs inside
        /// `MainForm.OnFormLoad`, which goes on from loading the plugins to opening the database
        /// named on the command line or the one last used, and KeePass opens the last one unless
        /// the operator has told it not to. The master key prompt that asks for it is a modal loop,
        /// and a modal loop pumps whatever was posted: a box posted from here arrived over that
        /// prompt, before the main window existed, and took the keyboard, so an Enter meant for
        /// the prompt dismissed it unread. The window's `Shown` comes after all of that, so the box
        /// is posted from there, once. A KeePass that starts in the tray shows it when the window
        /// is first brought up, which is when the operator is there to read it.
        ///
        /// **The box is raised on the other side of a post, which is where the guard has to be.**
        /// A `try` around `BeginInvoke` covers the posting and nothing else: the work runs later,
        /// on the user interface thread, with this method long since returned. `Unattended.Post`
        /// is what carries the catch across, and its remark says why the marshaling
        /// `ControlUiThread` already owns is the wrong machinery for a caller that is not waiting.
        /// </remarks>
        private void Announce(Form mainWindow, string refused)
        {
            EventHandler shown = null;
            shown = delegate
            {
                mainWindow.Shown -= shown;

                // A plugin that was let go before the window came up has nothing to announce: its
                // load was refused, and the sentence below would say everything else works.
                if (_pluginHost != null)
                {
                    PostRefusal(mainWindow, refused);
                }
            };

            mainWindow.Shown += shown;
        }

        private void PostRefusal(Control mainWindow, string refused)
        {
            // One sentence for both halves, because they are one fact: whichever way it failed,
            // the operator has not been told. Which route reported it says which half.
            const string unsaid = "the operator could not be told that the gate is not listening";

            try
            {
                Unattended.Post(
                    mainWindow,
                    delegate
                    {
                        new MessageBoxPrompt().Tell(
                            GateName.Full,
                            refused + Environment.NewLine + Environment.NewLine
                            + "No client can connect until KeePass is started again. Everything else "
                            + "works as usual: settings, authorities and profiles can all be changed, "
                            + "and the gate takes the port it is set to at the next start.");
                    },
                    error => Report(unsaid, error));
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A window that has gone, or never had a handle. Foreseen, so it stays a log line
                // rather than an incident: nothing here is a defect in this plugin. Read into a
                // local first, the way `Report` does, since `Terminate` sets the field to null.
                var log = _log;
                if (log != null)
                {
                    log.Error(unsaid, error);
                }
            }
        }

        /// <summary>
        /// Where everything this class catches goes. It is one of the places that catch
        /// every exception, so what it caught is counted as an incident and shown on the plugin's
        /// own window under the Tools menu rather than left in a log nobody opens.
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
