namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// One name for the tests that need more than a compiler. The default run excludes them, so
    /// spelling the category differently in two places would silently take a test out of every
    /// run rather than out of one.
    /// </summary>
    public static class TestCategories
    {
        /// <summary>
        /// Starts a real KeePass and drives it through the desktop. Needs an interactive session,
        /// takes seconds rather than milliseconds, and cannot run beside another copy of itself.
        /// </summary>
        public const string Live = "Live";
    }
}
