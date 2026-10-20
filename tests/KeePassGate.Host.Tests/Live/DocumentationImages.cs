namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The pictures the documentation is written around, named once.
    /// </summary>
    /// <remarks>
    /// One place rather than two, because a name that drifts between the fixture that writes a file
    /// and the page that shows it produces a broken image on a published page and nothing anywhere
    /// that says so. `build.ps1 -Task Lint` closes the other half of that loop: it refuses an image
    /// no page refers to and a reference to an image that is not there.
    ///
    /// The order is the order an operator meets them, which is also the order the fixture takes
    /// them in and the order the pages read in.
    /// </remarks>
    internal static class DocumentationImages
    {
        /// <summary>The window the Tools menu opens: what is loaded, and whether it is serving.</summary>
        public const string PluginStatus = "plugin-status";

        /// <summary>The KPG tab on a database that has never had an authority.</summary>
        public const string AuthorityBeforeCreating = "authority-before-creating";

        /// <summary>The same tab once there is one: fingerprint, dates and record count.</summary>
        public const string AuthorityAfterCreating = "authority-after-creating";

        /// <summary>The profiles page: the names this database keeps, and what grants them.</summary>
        public const string Profiles = "profiles";

        /// <summary>The clients page with an invitation outstanding and its code on screen.</summary>
        public const string ClientsInvitation = "clients-invitation";

        /// <summary>The dialog raised while a client waits, naming what it asked for.</summary>
        public const string PairingApproval = "pairing-approval";

        /// <summary>The clients page, with an admitted client on it.</summary>
        public const string ClientsAdmitted = "clients-admitted";

        /// <summary>The access tab on a group dialog, with a rule written into it.</summary>
        public const string GroupAccess = "group-access";

        /// <summary>The same tab on an entry dialog, showing what the group above already grants.</summary>
        public const string EntryAccess = "entry-access";

        /// <summary>
        /// The dialog a synchronize raises when two copies each made an authority of their own,
        /// asking which one to keep.
        /// </summary>
        public const string AuthorityChooser = "authority-chooser";

        /// <summary>Every one of them, in the order one session takes them.</summary>
        /// <remarks>
        /// The fixture's order rather than any reading order, because that is the one this file
        /// can be checked against: the pages that show these arrive later and put them in an order
        /// of their own. What the list is actually for is the count, and the test compares it
        /// without regard to order, so a sequence written here that matched nothing would be a
        /// sentence nothing could contradict.
        /// </remarks>
        public static readonly string[] Expected =
        {
            PluginStatus,
            AuthorityBeforeCreating,
            AuthorityAfterCreating,
            ClientsInvitation,
            PairingApproval,
            ClientsAdmitted,
            Profiles,
            GroupAccess,
            EntryAccess,
            AuthorityChooser
        };
    }
}
