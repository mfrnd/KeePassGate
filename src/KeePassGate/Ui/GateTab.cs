namespace KeePassGate.Ui
{
    /// <summary>
    /// What the plugin's page on a KeePass dialog is called. The control names are part of the
    /// contract rather than decoration: the end to end test drives a real KeePass through UI
    /// automation and finds the page by these.
    /// </summary>
    public static class GateTab
    {
        /// <summary>
        /// The caption on the tab strip: the short name, with the plugin's icon beside it.
        /// </summary>
        /// <remarks>
        /// Short because this strip is not ours. KeePass puts several of its own tabs on the
        /// dialogs this plugin decorates and any other plugin may add one, so every character
        /// spent here is width taken from everybody, and the icon says whose tab it is in a
        /// third of the room.
        /// </remarks>
        public const string Text = GateName.OnATab;

        public const string PageControlName = "KpgTabPage";

        public const string PanelControlName = "KpgTabPanel";

        public const string TitleLabelName = "KpgTitleLabel";

        public const string VersionLabelName = "KpgVersionLabel";

        public const string StatusLabelName = "KpgStatusLabel";

        public const string LayoutControlName = "KpgStatusLayout";
    }
}
