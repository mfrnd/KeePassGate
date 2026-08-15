namespace KeePassGate.Ui
{
    /// <summary>
    /// The one thing this plugin ever does with the clipboard, behind a seam.
    /// </summary>
    /// <remarks>
    /// A seam rather than a direct call because the alternative is a test suite that takes the
    /// developer's clipboard away from them every time it runs.
    /// </remarks>
    internal interface IClipboard
    {
        /// <summary>Puts text on the clipboard. Throws what the clipboard throws.</summary>
        void Set(string text);
    }
}
