using KeePass.Util;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// What <see cref="DroppedLocks"/> asks of KeePass about locking its workspace.
    /// </summary>
    public interface IWorkspaceLocking
    {
        /// <summary>Whether the operator asked KeePass to lock for this reason.</summary>
        bool Asks(SessionLockReason reason);

        /// <summary>
        /// Whether KeePass is blocking its own interaction, which is when it drops a lock.
        /// </summary>
        bool Blocked { get; }

        /// <summary>Whether any database is open, which is when KeePass locks.</summary>
        bool AnyOpen { get; }

        /// <summary>Locks every database, as KeePass's own lock does.</summary>
        void LockAll();
    }
}
