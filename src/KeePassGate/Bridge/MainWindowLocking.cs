using System;
using KeePass.App.Configuration;
using KeePass.Forms;
using KeePass.Util;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// KeePass's own answers about locking its workspace, from its main window and its
    /// configuration.
    /// </summary>
    /// <remarks>
    /// **Which setting answers for which reason is `MainForm.OnSessionLock`'s**, read off KeePass
    /// 2.61.1: a change of remote control asks `LockOnRemoteControlChange`; a Windows lock, a
    /// session ending and a user switch ask `LockOnSessionSwitch`; a suspend asks `LockOnSuspend`.
    /// `KeePassContractTests` pins that it still reads those three and still locks through
    /// `LockAllDocuments`, which still refuses while interaction is blocked.
    /// </remarks>
    public sealed class MainWindowLocking : IWorkspaceLocking
    {
        private readonly MainForm _mainWindow;

        public MainWindowLocking(MainForm mainWindow)
        {
            if (mainWindow == null)
            {
                throw new ArgumentNullException(nameof(mainWindow));
            }

            _mainWindow = mainWindow;
        }

        public bool Asks(SessionLockReason reason)
        {
            return Asks(KeePass.Program.Config.Security.WorkspaceLocking, reason);
        }

        /// <summary>Whether these settings ask KeePass to lock for this reason.</summary>
        internal static bool Asks(AceWorkspaceLocking locking, SessionLockReason reason)
        {
            if (locking == null)
            {
                return false;
            }

            switch (reason)
            {
                case SessionLockReason.RemoteControlChange:
                    return locking.LockOnRemoteControlChange;
                case SessionLockReason.Lock:
                case SessionLockReason.Ending:
                case SessionLockReason.UserSwitch:
                    return locking.LockOnSessionSwitch;
                case SessionLockReason.Suspend:
                    return locking.LockOnSuspend;
                default:
                    return false;
            }
        }

        public bool Blocked
        {
            get { return _mainWindow.UIIsInteractionBlocked(); }
        }

        public bool AnyOpen
        {
            get { return _mainWindow.IsAtLeastOneFileOpen(); }
        }

        public void LockAll()
        {
            _mainWindow.LockAllDocuments();
        }
    }
}
