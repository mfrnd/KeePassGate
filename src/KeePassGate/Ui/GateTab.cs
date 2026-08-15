namespace KeePassGate.Ui
{
    /// <summary>
    /// What the plugin's page on a KeePass dialog is called. The control names are part of the
    /// contract rather than decoration: the end to end test drives a real KeePass through UI
    /// automation and finds the page, its text and its buttons by these.
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

        /// <summary>One sentence saying what state the authority is in.</summary>
        public const string StatusLabelName = "KpgStatusLabel";

        /// <summary>
        /// What the authority is, as a table: a name in one column and a value in the other.
        /// </summary>
        public const string FactsTableName = "KpgFactsTable";

        /// <summary>The sentences under it, which are the warnings and nothing else.</summary>
        public const string DetailLabelName = "KpgDetailLabel";

        /// <summary>The standing notice that nothing on this tab lands before OK.</summary>
        public const string NoticeLabelName = "KpgNoticeLabel";

        /// <summary>What the last button press did.</summary>
        public const string ResultLabelName = "KpgResultLabel";

        public const string CreateButtonName = "KpgCreateButton";

        public const string RecreateButtonName = "KpgRecreateButton";

        public const string DeleteButtonName = "KpgDeleteButton";

        public const string CreateButtonText = "Create authority";

        public const string RecreateButtonText = "Recreate authority";

        public const string DeleteButtonText = "Delete authority";
    }
}
