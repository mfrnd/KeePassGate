namespace KeePassGate
{
    /// <summary>
    /// The names inside KeePass that this plugin reaches for. They are constants in one place
    /// because they are the parts most likely to move under a KeePass upgrade, and because the
    /// host tests assert every one of them against the KeePass build actually installed.
    /// </summary>
    public static class KeePassContract
    {
        /// <summary>The dialog behind File, Database Settings.</summary>
        public const string DatabaseSettingsFormTypeName = "KeePass.Forms.DatabaseSettingsForm";

        /// <summary>
        /// The tab strip on that dialog. A name rather than a position, and the injector falls
        /// back to the first tab control it finds if KeePass ever renames the field.
        /// </summary>
        public const string DatabaseSettingsTabControlName = "m_tabMain";

        /// <summary>
        /// The property on that dialog naming the database it is editing. Reached by reflection,
        /// so that a KeePass which renames it leaves the plugin showing an empty tab rather than
        /// failing to load.
        /// </summary>
        public const string DatabaseSettingsFormDatabaseProperty = "DatabaseEx";
    }
}
