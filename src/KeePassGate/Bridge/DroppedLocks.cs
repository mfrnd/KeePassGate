using System;
using System.Threading;
using KeePass.Util;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// A lock KeePass was asked for while this plugin's own save was running, which KeePass drops,
    /// made once the request that saved has been answered.
    /// </summary>
    /// <remarks>
    /// **KeePass locks on a Windows lock, a user switch, a suspend or a change of remote control
    /// only where the operator turned that on, and not while it blocks its own interaction**:
    /// `LockAllDocuments` then returns without locking and without noting that a lock was wanted.
    /// Every save blocks it, this plugin's among them, and this plugin saves whenever a client
    /// writes, so a lock asked for then was dropped for good while the gate went on serving the
    /// database. Windows has locked the screen already by then, since it waits for no program, so
    /// the lock made here afterwards is made behind it, or straight after a resume.
    ///
    /// **KeePass's own notifier, and KeePass's own lock.** The reason comes from a
    /// `SessionLockNotifier` of this plugin's, which turns Windows' events into reasons as
    /// KeePass's own notifier does; the setting asked is the setting `MainForm.OnSessionLock`
    /// reads for that reason; and the lock is `MainForm.LockAllDocuments`, so KeePass's own rules still decide: a
    /// dialog of KeePass's open still keeps the workspace unlocked, and exiting instead of locking
    /// still exits. KeePassXC does the same, retrying a lock asked for during a save until the save
    /// is over.
    ///
    /// **Once the request that saved has been answered**, rather than as soon as the save returns.
    /// A lock closes every connection the gate is holding, and the request whose save delayed it
    /// is still building its answer then, so a lock made at once would usually cut off the answer
    /// to a write that landed, and a client retrying a create it was never told about makes a
    /// second entry. The listener asks <see cref="Settle"/> as each conversation ends.
    ///
    /// **Only this plugin's saves.** A lock KeePass drops during an operation of its own, which
    /// the operator started, is KeePass's behavior and stays so.
    /// </remarks>
    public sealed class DroppedLocks : IDisposable
    {
        private readonly Func<bool> _saving;
        private readonly IWorkspaceLocking _workspace;
        private readonly IUiThread _ui;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;

        /// <summary>The reason of the lock owed, as an integer, or -1 for none.</summary>
        private int _owed = None;

        private SessionLockNotifier _notifier;

        private const int None = -1;

        public DroppedLocks(
            Func<bool> saving, IWorkspaceLocking workspace, IUiThread ui, GateIncidents incidents, IGateLog log)
        {
            if (saving == null)
            {
                throw new ArgumentNullException(nameof(saving));
            }

            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (ui == null)
            {
                throw new ArgumentNullException(nameof(ui));
            }

            _saving = saving;
            _workspace = workspace;
            _ui = ui;
            _incidents = incidents;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>Whether a lock is owed, for a test and for the log.</summary>
        public bool Owed
        {
            get { return Volatile.Read(ref _owed) != None; }
        }

        /// <summary>
        /// Listens for the events KeePass locks on. On KeePass's thread, which is where the
        /// notifier raises them.
        /// </summary>
        public void Start()
        {
            if (_notifier != null)
            {
                return;
            }

            var notifier = new SessionLockNotifier();
            notifier.Install(OnSessionLock);
            _notifier = notifier;
        }

        public void Dispose()
        {
            var notifier = _notifier;
            _notifier = null;
            if (notifier != null)
            {
                notifier.Uninstall();
            }
        }

        /// <summary>
        /// Notes a lock KeePass was asked for, when it arrived during this plugin's own save and
        /// the operator asked KeePass to lock for it. On KeePass's thread.
        /// </summary>
        internal void Asked(SessionLockReason reason)
        {
            if (!_saving() || !_workspace.Asks(reason))
            {
                return;
            }

            Volatile.Write(ref _owed, (int)reason);
            _log.Info("KeePass was asked to lock for " + reason + " while the plugin was saving, which it does " +
                      "not do then, so the lock is made once the request that saved has been answered");
        }

        /// <summary>
        /// Makes the lock owed, if a lock is owed, on KeePass's thread in a turn of its own. On
        /// a connection thread, once its conversation is over.
        /// </summary>
        /// <remarks>
        /// A turn rather than a post, so that it waits for KeePass to be idle and cannot land in
        /// the middle of another write's. A turn that does not come round in time leaves the lock
        /// owed, for the next conversation to make.
        /// </remarks>
        public void Settle()
        {
            if (!Owed)
            {
                return;
            }

            try
            {
                if (!_ui.Run(Make, DatabaseWriter.WriteTimeout))
                {
                    _log.Warn("KeePass's thread did not take the lock it was asked for in time; it is tried again " +
                              "after the next request");
                }
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the lock KeePass was asked for could not be made", error);
            }
        }

        /// <summary>On KeePass's thread: the lock owed, unless KeePass is busy again.</summary>
        internal void Make()
        {
            var owed = Volatile.Read(ref _owed);
            if (owed == None)
            {
                return;
            }

            var reason = (SessionLockReason)owed;
            if (_saving() || _workspace.Blocked)
            {
                // Dropped again if made now, so it stays owed for the next conversation's end.
                _log.Info("the lock KeePass was asked for, for " + reason + ", waits, because KeePass is busy");
                return;
            }

            Interlocked.CompareExchange(ref _owed, None, owed);
            if (!_workspace.AnyOpen)
            {
                return;
            }

            _log.Info("locking, for " + reason + ", which KeePass was asked for while the plugin was saving");
            _workspace.LockAll();
        }

        private void OnSessionLock(object sender, SessionLockEventArgs e)
        {
            try
            {
                if (e != null)
                {
                    Asked(e.Reason);
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // A kind of catch-all `DESIGN.md` §141 lists: KeePass's notifier raises this on
                // KeePass's thread with nothing around it, from inside whatever is pumping there,
                // which is this plugin's own save when it matters. An escape would reach WinForms'
                // own error dialog in the middle of that save.
                if (_incidents != null)
                {
                    _incidents.Record("a lock KeePass was asked for could not be noted", error);
                }
            }
        }
    }
}
