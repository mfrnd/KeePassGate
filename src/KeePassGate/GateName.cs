namespace KeePassGate
{
    /// <summary>
    /// What this plugin is called on screen, in one place.
    /// </summary>
    /// <remarks>
    /// The name appears wherever a person is being told who put something there: the tab on the
    /// database settings dialog, the tab on the group and entry dialogs, the item under KeePass's
    /// Tools menu, and the title of the window that item opens. All of them read it from here, so
    /// renaming the plugin is one edit and there is no way for three of them to agree while the
    /// fourth does not.
    ///
    /// **Three forms, and which one goes where is about space rather than about style.** A tab strip
    /// is shared: KeePass puts four or five of its own tabs on those dialogs and any other plugin
    /// may add one, so a long caption there is width taken from everybody. The short form and the
    /// icon together are enough to say whose tab it is. A menu item is a line of its own and costs
    /// nobody anything, so it says the name in full and the short form after it, which is what
    /// tells a reader that the `KPG` on those tabs is this.
    ///
    /// This is not the assembly's product name. That one has to be exactly `KeePass Plugin`, which
    /// KeePass reads out of the file before it opens the assembly and silently skips anything else.
    /// </remarks>
    public static class GateName
    {
        /// <summary>The name in full, for prose and for anywhere with room for it.</summary>
        public const string Product = "KeePassGate";

        /// <summary>The name on a tab strip, where width belongs to whoever else wants a tab.</summary>
        public const string OnATab = "KPG";

        /// <summary>
        /// Both, for the one place with a line to itself. It is what says that the short form on
        /// three tabs and this are the same plugin.
        /// </summary>
        public const string Full = Product + " (" + OnATab + ")";
    }
}
