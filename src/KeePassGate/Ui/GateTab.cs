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

        /// <summary>
        /// What the item under the Tools menu says: the name in full, and the short form after it.
        /// </summary>
        /// <remarks>
        /// A menu item is a line of its own and costs nobody any width, so this is the one place
        /// that can afford to say both. It is also the only place that can teach the abbreviation:
        /// somebody who has seen `KPG` on three tabs finds out here what it stands for.
        /// </remarks>
        public const string MenuItemText = GateName.Full;

        public const string MenuItemName = "KpgToolsMenuItem";

        /// <summary>The table on that window: where the gate is listening, and what has gone wrong.</summary>
        public const string StatusFactsName = "KpgStatusFacts";

        /// <summary>The line under it saying what to do about what it says.</summary>
        public const string StatusAdviceName = "KpgStatusAdvice";

        /// <summary>Where the operator types the port the gate should listen on.</summary>
        public const string StatusPortBoxName = "KpgStatusPortBox";

        /// <summary>
        /// The addresses the gate binds, as a list of choices with exactly one choice on it.
        /// </summary>
        /// <remarks>
        /// **A list rather than a line of text, and that is about what comes next.** Loopback is
        /// the only address this plugin will bind, and the reason is the threat model rather than
        /// the code: everything here concedes local code execution as the user and defends against
        /// a legitimate client asking for more than it should, and an address another machine can
        /// reach is a different argument that has not been made.
        ///
        /// Saying so as a list with one entry that cannot be turned off says two true things at
        /// once: that this is a choice the plugin makes rather than a fact of the network, and that
        /// there is exactly one option today. A second entry, when a threat model arrives that
        /// allows one, is then a row rather than a redesign.
        /// </remarks>
        public const string StatusAddressListName = "KpgStatusAddressList";

        public const string StatusAddressLabelText = "Listen on";

        /// <summary>The one address there is, and the only one there has ever been.</summary>
        public const string LoopbackAddress = "127.0.0.1";

        public const string StatusAddressHelp =
            "Loopback only. Nothing outside this computer can reach the gate, which is what the "
            + "rest of the plugin's security is written against. Other interfaces would need a "
            + "threat model that admits them, and there is not one yet.";

        /// <summary>
        /// The two buttons that end this window, in the order and the place every other dialog
        /// puts them.
        /// </summary>
        /// <remarks>
        /// **OK and Cancel, and nothing else**, which is a deletion rather than a rename. This
        /// window had Apply and Close: Apply acted at once and Close did nothing, so the pair said
        /// that a setting was already live and that leaving was free. Neither is a thing a dialog
        /// with two buttons in the bottom right corner is read as saying, and everything else this
        /// plugin puts in front of an operator is a dialog with two buttons in the bottom right
        /// corner. One shape for all of them, and it is Windows's shape rather than one of ours.
        /// </remarks>
        public const string StatusOkButtonName = "KpgStatusOkButton";

        public const string StatusOkButtonText = "OK";

        public const string StatusCancelButtonName = "KpgStatusCancelButton";

        public const string StatusCancelButtonText = "Cancel";

        /// <summary>What the last press of OK did.</summary>
        public const string StatusResultLabelName = "KpgStatusResultLabel";

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
        /// The pages inside the KPG tab. Three subjects live here, the authority, the
        /// profiles and the clients, and they answer different questions: what this database can
        /// issue, which names it can issue for, and who holds what it already issued. One page
        /// showing all of them means every line competing for the same 449 by 314, which is a tab
        /// that has to be shortened rather than read.
        /// </summary>
        public const string SubTabsName = "KpgSubTabs";

        public const string AuthorityPageName = "KpgAuthorityPage";

        /// <summary>Spelled out rather than abbreviated, because the tab strip above it says KPG.</summary>
        public const string AuthorityPageText = "Certificate Authority";

        public const string ClientsPageName = "KpgClientsPage";

        public const string ClientsPageText = "Clients";

        public const string ProfilesPageName = "KpgProfilesPage";

        /// <summary>
        /// The third subject: the names a rule can grant to and a client can be approved for. Its
        /// own page because it is neither what this database can issue nor who holds what it
        /// issued, and because whether it names a profile decides whether every rule for that
        /// profile grants anything.
        /// </summary>
        public const string ProfilesPageText = "Profiles";

        /// <summary>
        /// The token itself, in a box rather than a label so the operator can select and copy it.
        /// It is never written to the database, so nothing here survives KeePass closing.
        /// </summary>
        public const string InvitationBoxName = "KpgInvitationBox";

        /// <summary>The icon button beside the box, which puts the token on the clipboard.</summary>
        public const string CopyButtonName = "KpgCopyButton";

        /// <summary>What a screen reader calls the tab's contents, which have no caption.</summary>
        public const string PanelAccessibleLabel = "KeePassGate settings for this database";

        /// <summary>What a screen reader calls the box holding an invitation code.</summary>
        public const string InvitationBoxLabel = "Invitation code";

        /// <summary>What a screen reader calls the list of addresses to listen on.</summary>
        public const string StatusAddressListLabel = "Addresses to listen on";

        /// <summary>What a screen reader calls the port box.</summary>
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

        /// <summary>
        /// Where the clients page says why the button under it would hand out a code nothing
        /// could redeem.
        /// </summary>
        /// <remarks>
        /// One band for every such reason rather than one band each, because they answer the same
        /// question and the page is 449 by 314. It was named for the only reason it had, which
        /// stopped being true when an expired authority became a second one.
        ///
        /// **Every reason is one line**, and that is a measurement. The band sits over the client
        /// list, which takes whatever the band leaves, and on the most crowded page the longest
        /// reason, at three lines, left the list 72 pixels where `GateTabFitsTests` holds it to 90.
        /// So each sentence says what is wrong and what to do about it, and stops.
        /// </remarks>
        public const string InviteNoticeName = "KpgInviteNotice";

        /// <summary>
        /// What it says while an authority change is waiting for OK, which is the one thing the
        /// waiting arrangement costs an operator.
        /// </summary>
        /// <remarks>
        /// A client pairs against the gate, and the gate answers out of the database KeePass is
        /// holding. It cannot see a change that is still waiting for this dialog, so a client that
        /// paired now would be answered by the authority as it was: refused if there is none yet,
        /// or issued a certificate that OK is about to invalidate. Neither is a failure anybody
        /// would connect back to a dialog they had not finished with.
        ///
        /// Shown only while there is something to say, because a warning that is always there is a
        /// warning nobody reads.
        /// </remarks>
        public const string PendingAuthorityNotice =
            "The authority change is not applied yet. Press OK and reopen this dialog to pair.";

        /// <summary>
        /// What it says while a profile edit is waiting for OK.
        /// </summary>
        /// <remarks>
        /// `KPG.Profiles` is the one key this tab and pairing both write, and they write it
        /// differently: the tab holds its version until OK, pairing writes live and saves. A
        /// client approved while an edit is buffered is approved against a list that is about to
        /// change under it, so the invite button is dead for that span, and this is the third
        /// reason it can be dead and the last one that said nothing.
        ///
        /// The remedy is the remedy above, because the state is the same state: something on this
        /// tab has not reached the database, and OK is what puts it there.
        /// </remarks>
        public const string PendingProfilesNotice =
            "The profile change is not applied yet. Press OK and reopen this dialog to pair.";

        /// <summary>
        /// What it says when this database's authority has run out.
        /// </summary>
        /// <remarks>
        /// The remedy is named because there is only one and it is not on this page, and what the
        /// remedy costs is named with it because "Recreate authority" reads like a repair and is
        /// not: `CA.md` §219 has no reissue, so a new authority invalidates every certificate the
        /// old one signed and takes every admission record with it. An operator who reads this,
        /// presses that, and finds the clients list empty afterwards was told the wrong thing.
        ///
        /// It says nothing about pressing OK, because a recreate on the first page raises the
        /// waiting notice above and that one says it.
        /// </remarks>
        public const string ExpiredAuthorityNotice =
            "This authority has expired. Recreate it on the first page; every client pairs again.";

        /// <summary>
        /// What it says when the host certificate has run out and the authority has not.
        /// </summary>
        /// <remarks>
        /// The same remedy at the same cost, for the reason the notice above gives: `CA.md` §219
        /// has no reissue for a host certificate either. Its own sentence rather than the one
        /// above, because an operator told the authority had expired would look at the wrong date
        /// on the first page and find it years away.
        /// </remarks>
        public const string ExpiredHostNotice =
            "The host certificate has expired. Recreate the authority; every client pairs again.";

        /// <summary>
        /// The longest of them, which is what the tab has to be measured against.
        /// </summary>
        /// <remarks>
        /// Here rather than in the test that measures it, so that a further reason to say
        /// something above the invite button is measured by adding it to this rather than by
        /// somebody remembering to. `GateTabFitsTests` is what reads it. It went from two
        /// candidates to three the first time that happened, which is why the comparison is over a
        /// list rather than written out.
        /// </remarks>
        public static readonly string LongestInviteNotice = Longest(
            PendingAuthorityNotice, PendingProfilesNotice, ExpiredAuthorityNotice, ExpiredHostNotice);

        private static string Longest(params string[] candidates)
        {
            var longest = string.Empty;

            foreach (var candidate in candidates)
            {
                if (candidate.Length > longest.Length)
                {
                    longest = candidate;
                }
            }

            return longest;
        }

        /// <summary>The dialog raised when somebody asks to pair.</summary>
        public const string ApprovalFormName = "KpgApprovalForm";

        public const string ApprovalDetailLabelName = "KpgApprovalDetailLabel";

        /// <summary>The line saying the list holds more profiles than it shows at once.</summary>
        public const string ApprovalScrollingName = "KpgApprovalScrolling";

        /// <summary>The line naming the clients that regain a removed profile if it is approved.</summary>
        public const string ApprovalRegainingName = "KpgApprovalRegaining";

        public const string ApprovalProfilesListName = "KpgApprovalProfilesList";

        public const string ApproveButtonName = "KpgApproveButton";

        public const string RefuseButtonName = "KpgRefuseButton";

        public const string ApproveButtonText = "Approve";

        public const string RefuseButtonText = "Refuse";

        /// <summary>
        /// The authority page's button for a database holding several authorities, in the place
        /// Create has, since that one has nothing to do in that state.
        /// </summary>
        public const string ChooseButtonName = "KpgChooseButton";

        public const string ChooseButtonText = "Choose authority";

        /// <summary>The dialog that asks which of several authorities to keep.</summary>
        public const string ChooserFormName = "KpgChooserForm";

        public const string ChooserAuthorityListName = "KpgChooserAuthorityList";

        public const string ChooserAuthorityListLabel = "Authorities this database holds";

        public const string ChooserFingerprintBoxName = "KpgChooserFingerprintBox";

        public const string ChooserFingerprintBoxLabel = "Full fingerprint of the selected authority";

        public const string ChooserDetailLabelName = "KpgChooserDetailLabel";

        public const string ChooserClientListName = "KpgChooserClientList";

        public const string ChooserClientListLabel = "Clients the selected authority admitted";

        public const string KeepButtonName = "KpgKeepButton";

        public const string KeepButtonText = "Keep this one";

        public const string ChooserCancelButtonName = "KpgChooserCancelButton";

        public const string ChooserCancelButtonText = "Cancel";
    }
}
