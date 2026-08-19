namespace KeePassGate.Ui
{
    /// <summary>
    /// What the grant editor's page and controls are called.
    /// </summary>
    /// <remarks>
    /// Names rather than positions, for the same reason the KPG tab has them: the end to end tests
    /// drive a real KeePass through UI automation and find this page and its parts by these.
    ///
    /// The tab carries the plugin's name and nothing else. Another plugin may well put an access
    /// control page on these same dialogs, and two pages both called something like "Access
    /// control", governing different clients, is a trap rather than a coincidence. Whoever is
    /// looking for these rules knows what installed them.
    ///
    /// Not "ACL", which names the subject rather than the owner and is exactly the collision above.
    /// The short form of the plugin's name, with its icon, because this strip belongs to KeePass
    /// and to whoever else adds a tab: five of KeePass's own are already on the entry dialog. The
    /// line at the top of the page says what the page is for, which is the job a longer caption
    /// would have been doing, and the item under the Tools menu is where the abbreviation is spelled
    /// out.
    /// </remarks>
    public static class GrantTab
    {
        /// <summary>The caption on the tab strip of the group and entry dialogs.</summary>
        public const string Text = GateName.OnATab;

        public const string PageControlName = "KpgGrantPage";

        public const string PanelControlName = "KpgGrantPanel";

        /// <summary>One line saying what this page is about, which differs by dialog.</summary>
        public const string ScopeLabelName = "KpgGrantScopeLabel";

        /// <summary>Whatever is wrong, if anything is. Never a rule's own content.</summary>
        public const string WarningLabelName = "KpgGrantWarningLabel";

        public const string GridControlName = "KpgGrantGrid";

        /// <summary>
        /// What a screen reader says the table is. It has no text of its own to say, and it is the
        /// whole of this page.
        /// </summary>
        public const string GridLabel = "Rules here, one for each profile";

        /// <summary>The list of verbs a rule is chosen from.</summary>
        /// <remarks>
        /// Named like everything else, and worth naming more than most: it is inside a
        /// dropdown, so it is what a test has to point at to choose a rule at all, and a
        /// control with no name cannot be pointed at from outside the process.
        /// </remarks>
        public const string RulePickerName = "KpgGrantRulePicker";

        /// <summary>The list of profiles a rule is added for.</summary>
        public const string ProfilePickerName = "KpgGrantProfilePicker";

        /// <summary>The dropdown either list is shown in.</summary>
        public const string PickerName = "KpgGrantPicker";

        /// <summary>What holds a picker's list inside that dropdown.</summary>
        public const string PickerHostName = "KpgGrantPickerHost";

        /// <summary>Which column is which, in the order they are added.</summary>
        /// <remarks>
        /// Here rather than in the panel, because the tests point at cells by number and were
        /// declaring their own copies. There were three sets of the same three, one of which had
        /// already gone without the third. A number a test writes down is a number that can
        /// disagree.
        /// </remarks>
        public const int ProfileColumn = 0;

        public const int MaskColumn = 1;

        public const int ForgetColumn = 2;

        public const string ProfileColumnName = "KpgGrantProfileColumn";

        /// <summary>
        /// What the empty line at the bottom of the table says, which is the whole of how a rule is
        /// started. Without it that line reads as somewhere to type, and there is nothing to type.
        /// </summary>
        public const string AddRowPrompt = "Click to choose a profile";

        /// <summary>
        /// Said when this database names no profiles at all, which is the only empty pick list
        /// worth a sentence.
        /// </summary>
        /// <remarks>
        /// There was a second one, for the list being empty because every profile already has a
        /// rule here. It was true and it was four lines of a page that has 314 pixels, and what it
        /// said was that the rows already on the table are all of them, which the rows say. This
        /// one stays because there the table is empty and nothing on the page says where a profile
        /// comes from.
        /// </remarks>
        public const string NoProfilesYet =
            "This database names no profiles yet. Add one on the " + GateName.OnATab
            + " tab of the database settings, or pair a client and approve the profile it asks for.";

        /// <summary>
        /// What the Permissions cell of the empty line says, which is why it is unavailable.
        /// </summary>
        public const string MaskNeedsAProfile =
            "A rule is a profile first. Choose one on the left, and this opens by itself.";

        /// <summary>
        /// What a rule stored for no profile at all says, which only a document written by hand
        /// can hold, since the reader takes any member name.
        /// </summary>
        /// <remarks>
        /// No certificate can hold an empty name, so the rule grants nothing whatever it says, and
        /// no rule chosen on it would change that. Removing it is the one thing to do with it.
        /// </remarks>
        public const string RuleWithoutProfile =
            "This rule names no profile, so no client can hold it and it grants nothing. The X "
            + "removes it.";

        /// <summary>
        /// What a profile cell says once its row exists.
        /// </summary>
        /// <remarks>
        /// One line, because it is a tooltip on a cell somebody pressed expecting something to
        /// happen: it has to say why nothing did and what to do instead, and stop. Why it works
        /// this way is in `GrantPanel.Press`, where a reader who wants the reason will be.
        /// </remarks>
        public const string ProfileIsSettled =
            "A rule's profile cannot be changed, only removed and recreated.";

        /// <summary>
        /// What the mask of a rule for a profile the database no longer names says, which is why
        /// it is struck through.
        /// </summary>
        /// <remarks>
        /// A client holds only the names its record carries that the database still names, so a
        /// rule for any other name reaches nobody. It stays on the node, and on the table, because
        /// adding the name again puts it back in force, and that is worth seeing before doing it.
        /// </remarks>
        public const string ProfileNotNamed =
            "This database no longer names this profile, so this rule grants nothing. Adding the "
            + "profile again on the " + GateName.OnATab + " tab of the database settings puts it "
            + "back in force.";

        public const string MaskColumnName = "KpgGrantMaskColumn";

        /// <summary>
        /// What that column is called on screen. The heading says what the column is for; the
        /// letters in it say what the mask means, and a heading that was also the letters left the
        /// table with no word anywhere for the thing it holds.
        /// </summary>
        public const string MaskColumnHeader = "Permissions";

        public const string ForgetColumnName = "KpgGrantForgetColumn";

        public const string SealCheckBoxName = "KpgGrantSealCheckBox";

        public const string SealCheckBoxText = "Refuse any profile not named here";

        /// <summary>
        /// What the seal actually costs, which is the part an operator cannot work out from the
        /// caption: it stops inheritance dead rather than merely adding a rule.
        /// </summary>
        public const string SealCheckBoxHelp =
            "Without this, a profile with no rule here keeps whatever the groups above grant it. "
            + "With it, this node refuses every profile it does not name, and a grant from a group "
            + "above stops reaching here or anything under it.";

        public const string DiscardButtonName = "KpgGrantDiscardButton";

        public const string DiscardButtonText = "Discard the unreadable rules";

        /// <summary>
        /// Why the unreadable rules refuse rather than being skipped, on the button that discards
        /// them.
        /// </summary>
        /// <remarks>
        /// **A sentence the warning over the table used to carry.** The warning says what is
        /// wrong, what it does and what to do, and on the group dialog the table under it keeps
        /// what the warning leaves: at four lines it kept its heading and no row. Why refusing is
        /// right is for somebody deciding whether to discard, so it is on the button they would
        /// press.
        /// </remarks>
        public const string DiscardButtonHelp =
            "Refusing is the safe reading: a rule nobody can read may be doing real work.";

        /// <summary>
        /// What the page says it is about, on a group dialog.
        /// </summary>
        /// <remarks>
        /// One line, and the line that answers the question a reader of this table actually has:
        /// why there are rows here that this node does not carry, and what happens if they write
        /// one over the top. What each rule reaches is the table's own job to show.
        /// </remarks>
        public const string GroupScope =
            "Rules are inherited from parent groups and can be overwritten.";

        /// <summary>
        /// The same, on an entry dialog, and the same sentence, because it is equally true there:
        /// an entry inherits from the groups above it and a rule written here wins.
        /// </summary>
        /// <remarks>
        /// Two names for one string rather than one name used twice, because which dialog a page
        /// is on is a real distinction and the day the two have different things to say, only the
        /// text has to change.
        /// </remarks>
        public const string EntryScope = GroupScope;

        /// <summary>What the page says when it is on a dialog with no node behind it.</summary>
        public const string NoNodeScope =
            "This dialog is not about a group or an entry, so there is nothing here to grant.";
    }
}
