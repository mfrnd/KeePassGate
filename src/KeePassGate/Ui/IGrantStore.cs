namespace KeePassGate.Ui
{
    /// <summary>
    /// Where one node's grants are read from and written to.
    /// </summary>
    /// <remarks>
    /// A seam because the answer is not the obvious one. What the editor writes into is the
    /// **dialog's** copy of a node's custom data, not the node's own: both KeePass dialogs take a
    /// copy when they open and write it back over the node when they are accepted, so a grant
    /// written to the live group or entry survives Cancel and is destroyed by OK. Putting that
    /// behind an interface keeps the reflection in one adapter and lets everything above it be
    /// tested without a dialog to be inside.
    ///
    /// A store that cannot be written is not represented here. The editor is told why in words
    /// instead, so that it can say so on the tab rather than failing at the moment somebody types.
    /// </remarks>
    public interface IGrantStore
    {
        /// <summary>The document stored on this node, or null when there is none.</summary>
        string Read();

        /// <summary>
        /// Stores a document, or removes the key when given null.
        /// </summary>
        /// <remarks>
        /// Called only when something actually changed. An implementation must not need to defend
        /// itself against an identical write, but it will get one if the caller is wrong. It
        /// changes nothing on the node, whose custom data keeps no modification times; what the
        /// caller guards against is the same rules written in other words, which an entry dialog
        /// takes for a changed entry.
        /// </remarks>
        void Write(string document);
    }
}
