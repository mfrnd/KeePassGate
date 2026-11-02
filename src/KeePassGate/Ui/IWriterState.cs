using System;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the approval prompt has to know about the plugin's writes before it puts a dialog up.
    /// </summary>
    /// <remarks>
    /// **A dialog is a turn on KeePass's thread, as a write is**, and meets what a write meets
    /// there. Posted as a message, as it once was, it was delivered by whatever was pumping when it
    /// arrived: KeePass's save pumps while it runs, so a dialog posted during one opened inside it,
    /// and held the save, the writer's lock and KeePass's interaction block for as long as the
    /// operator took. A workstation lock is dropped rather than deferred while that block is
    /// raised, so KeePass stayed unlocked behind a locked screen. Queued, it starts only when
    /// KeePass is idle, which no save pumping through `DoEvents` raises, `DESIGN.md` §119.
    ///
    /// What a turn can still meet is a dialog of WinForms' own shown inside a save, whose loop
    /// does raise Idle. So the prompt still asks, on KeePass's thread and just before it shows
    /// anything, whether this plugin's own save is what is underneath, which it waits out, and
    /// whether KeePass is part way through something of its own, which it answers `503 busy` at
    /// once, as a write does. `DatabaseWriter` is the one implementation.
    /// </remarks>
    public interface IWriterState
    {
        /// <summary>
        /// Whether this plugin's own save is running. On KeePass's thread, true means that save
        /// is what is pumping underneath whoever asks.
        /// </summary>
        bool Saving { get; }

        /// <summary>
        /// Why KeePass is part way through something of its own, asked on its thread, or null when
        /// it is not.
        /// </summary>
        string Interrupting();

        /// <summary>
        /// Waits for the write in flight, if there is one, to end, and says whether it did within
        /// the wait. Asked on a connection thread.
        /// </summary>
        bool Quiet(TimeSpan wait);
    }
}
