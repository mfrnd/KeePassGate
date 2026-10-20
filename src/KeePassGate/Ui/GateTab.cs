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

        /// <summary>Whether the gate is listening, and where.</summary>
        public const string ServingLabelName = "KpgServingLabel";

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

        public const string InviteButtonName = "KpgInviteButton";

        /// <summary>
        /// What the button that hands a code out says while no code is outstanding.
        /// </summary>
        /// <remarks>
        /// Not the caption it has once a code is out, because the first press renews nothing: the
        /// line above the box says no code is outstanding, and a button beside it offering to renew
        /// one was offering something the page had just said does not exist.
        /// </remarks>
        public const string InviteButtonText = "Invite";

        /// <summary>
        /// What the same button says while a code is outstanding. "Renew" rather than "Invite",
        /// because pressing it again does not add a second code: it replaces the one that is there.
        /// </summary>
        /// <remarks>
        /// Two words, and it used to be five. "One-time" and "client" were both said again in the
        /// line above the box, which now says the code is consumed by the first client or renewed,
        /// so the button was spending width to repeat its own explanation. **The width is what it
        /// was spending it on**: the box beside it holds a forty-three character code and shares
        /// this row with the button, so every character of caption is width taken from the box,
        /// which has to hold the whole code and at 100 percent has little more than that. This is
        /// the caption that is up while the box holds one.
        /// </remarks>
        public const string RenewInviteButtonText = "Renew invite";

        /// <summary>
        /// The pages inside the KPG tab. Two subjects live here, the authority and the clients, and
        /// they answer different questions: what this database can issue, and who holds what it
        /// already issued. One page showing both means every line competing for the same 449 by
        /// 314, which is a tab that has to be shortened rather than read.
        /// </summary>
        public const string SubTabsName = "KpgSubTabs";

        public const string AuthorityPageName = "KpgAuthorityPage";

        /// <summary>Spelled out rather than abbreviated, because the tab strip above it says KPG.</summary>
        public const string AuthorityPageText = "Certificate Authority";

        public const string ClientsPageName = "KpgClientsPage";

        public const string ClientsPageText = "Clients";

        /// <summary>
        /// The token itself, in a box rather than a label so the operator can select and copy it.
        /// It is never written to the database, so nothing here survives KeePass closing.
        /// </summary>
        public const string InvitationBoxName = "KpgInvitationBox";

        /// <summary>The icon button beside the box, which puts the token on the clipboard.</summary>
        public const string CopyButtonName = "KpgCopyButton";

        /// <summary>
        /// What a screen reader calls the tab's contents, which have no caption.
        /// TODO(step 18): nothing reads this yet. The panel is found by `Name` today, and setting
        /// an `AccessibleName` on it would replace the name the desktop tests look it up by.
        /// </summary>
        public const string PanelAccessibleLabel = "KeePassGate settings for this database";

        /// <summary>What a screen reader calls the box holding an invitation code.</summary>
        public const string InvitationBoxLabel = "Invitation code";

        /// <summary>
        /// What a screen reader calls the list of addresses to listen on.
        /// TODO(step 18): the window under the Tools menu is to carry these, and to take over
        /// saying where the gate listens from the tab, because that answer is the same whichever
        /// database is open.
        /// </summary>
        public const string StatusAddressListLabel = "Addresses to listen on";

        /// <summary>
        /// What a screen reader calls the port box.
        /// TODO(step 18): as above.
        /// </summary>
        public const string StatusPortBoxLabel = "Port to listen on";

        /// <summary>What a screen reader calls the profiles being approved.</summary>
        public const string ApprovalProfilesListLabel = "Profiles this client may use";

        /// <summary>
        /// What UI automation and a screen reader read off the copy button, since an icon has no
        /// caption for them to read. Every other button on this tab is found by its own text.
        /// </summary>
        public const string CopyButtonLabel = "Copy the invitation";

        /// <summary>What is outstanding, and until when.</summary>
        public const string InvitationLabelName = "KpgInvitationLabel";

        /// <summary>The dialog raised when somebody asks to pair.</summary>
        public const string ApprovalFormName = "KpgApprovalForm";

        public const string ApprovalDetailLabelName = "KpgApprovalDetailLabel";

        public const string ApprovalProfilesListName = "KpgApprovalProfilesList";

        public const string ApproveButtonName = "KpgApproveButton";

        public const string RefuseButtonName = "KpgRefuseButton";

        public const string ApproveButtonText = "Approve";

        public const string RefuseButtonText = "Refuse";
    }
}
