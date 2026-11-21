using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the grant editor's page and controls are called, and every sentence the page shows.
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
    /// **Every sentence follows the same rules.** A sentence names no profile and no client, which
    /// a cell of the table may; no pronoun stands for a subject or an object, the noun is named; a
    /// state has a column of its own and is never joined to a name; and what a state means is in
    /// the tooltip of its cell, never printed under the table. A row or a box changed on the tab is
    /// told by a line of its tooltip and by its accessible text, never by color alone.
    /// </remarks>
    public static class GrantTab
    {
        /// <summary>The caption on the tab strip of the group and entry dialogs.</summary>
        public const string Text = GateName.OnATab;

        public const string PageControlName = "KpgGrantPage";

        public const string PanelControlName = "KpgGrantPanel";

        /// <summary>What a screen reader calls the tab's contents, which have no caption.</summary>
        public const string PanelLabel = GateName.Product + " rules for this group or entry";

        /// <summary>The line at the top of the page, which differs by dialog.</summary>
        public const string ScopeLabelName = "KpgGrantScopeLabel";

        /// <summary>Whatever is wrong, if anything is. Never a rule's own content.</summary>
        public const string WarningLabelName = "KpgGrantWarningLabel";

        public const string GridControlName = "KpgGrantGrid";

        /// <summary>What a screen reader calls the table, which has no text of its own.</summary>
        public const string GridLabel = "Rules at this node";

        /// <summary>The list of verbs a rule is chosen from.</summary>
        /// <remarks>
        /// Named like everything else, and worth naming more than most: it is inside a
        /// dropdown, so it is what a test has to point at to choose a rule at all, and a
        /// control with no name cannot be pointed at from outside the process.
        /// </remarks>
        public const string RulePickerName = "KpgGrantRulePicker";

        /// <summary>What a screen reader calls the rule picker, a list with no text of its own.</summary>
        public const string RulePickerLabel = "What a profile may do here";

        /// <summary>The list of profiles a rule is added for.</summary>
        public const string ProfilePickerName = "KpgGrantProfilePicker";

        /// <summary>What a screen reader calls the profile picker, for the same reason.</summary>
        public const string ProfilePickerLabel = "Profiles a rule can be chosen for";

        /// <summary>
        /// The most rows of names the profile picker shows before it scrolls, where its screen has
        /// room for that many: the list of names keeps no ceiling, `STORE.md` §616.
        /// </summary>
        public const int MostProfileRows = 20;

        /// <summary>The dropdown either list is shown in.</summary>
        public const string PickerName = "KpgGrantPicker";

        /// <summary>What holds a picker's list inside that dropdown.</summary>
        public const string PickerHostName = "KpgGrantPickerHost";

        /// <summary>Which column is which, in the order they are added.</summary>
        /// <remarks>
        /// Here rather than in the panel, because the tests point at cells by number, and a number
        /// a test writes down for itself is a number that can disagree.
        /// </remarks>
        public const int ProfileColumn = 0;

        public const int MaskColumn = 1;

        public const int StateColumn = 2;

        public const int ForgetColumn = 3;

        public const string ProfileColumnName = "KpgGrantProfileColumn";

        public const string ProfileColumnHeader = "Profile";

        /// <summary>What the Profile column's heading tells the operator.</summary>
        public const string ProfileColumnHelp =
            "The profile of the rule, chosen from the profiles this database names. The profile of a "
            + "rule cannot be changed, only removed and chosen again.";

        /// <summary>
        /// What the empty line at the bottom of the table reads, which is the whole of how a rule is
        /// started. Without it that line reads as somewhere to type, and there is nothing to type.
        /// </summary>
        public const string AddRowPrompt = "Click to choose a profile";

        /// <summary>What the Permissions cell of the empty line tells, which is why the cell is unavailable.</summary>
        public const string MaskNeedsAProfile = "Choose a profile first. The list of permissions opens next.";

        /// <summary>
        /// Shown over the table wherever this database names no profile, before any press: the
        /// table then offers no empty line, and nothing else on the page tells where a profile is
        /// named.
        /// </summary>
        /// <remarks>
        /// **Every text that stands above the table takes at most two lines** of the group dialog's
        /// page at every scaling, and a single line where the meaning holds: the room above the
        /// table is the room the rows do not get. `GrantTabFitsTests` measures the lines of each.
        /// </remarks>
        public const string NoProfilesYet =
            "This database names no profile. Profiles are named on the Profiles page of the "
            + GateName.OnATab + " tab in the database settings.";

        public const string MaskColumnName = "KpgGrantMaskColumn";

        /// <summary>What the Permissions column's heading tells the operator, where a rule can be chosen.</summary>
        public const string MaskColumnHelp = "Click to choose what a profile may do here.";

        /// <summary>The same, on a tab where nothing can be changed.</summary>
        public const string MaskColumnReadOnlyHelp = "What a profile may do here.";

        /// <summary>
        /// What the Permissions column is called on screen. The heading names what the column is
        /// for; the letters in it are what the mask means.
        /// </summary>
        public const string MaskColumnHeader = "Permissions";

        public const string StateColumnName = "KpgGrantStateColumn";

        public const string StateColumnHeader = "State";

        /// <summary>What the State column's heading tells the operator.</summary>
        public const string StateColumnHelp =
            "Where a rule comes from, and what keeps a rule from being in force as shown.";

        public const string ForgetColumnName = "KpgGrantForgetColumn";

        /// <summary>
        /// The last column's heading, which a screen reader names every X in it by, and which is
        /// never drawn: `ClientList.RevokeHeading` gives the reason.
        /// </summary>
        public const string ForgetColumnHeader = "Remove rule";

        /// <summary>What the last column's heading, a bin, tells the operator.</summary>
        public const string ForgetColumnHelp =
            "Removes the rule set at this node, or the rule of a copy on a row bound to other rows. On an "
            + AtMostWord + " row, lets the whole rule from above through.";

        /// <summary>The word that starts the tooltip of a rule here over an inherited rule, and the end of it.</summary>
        public const string InheritedFrom = ", inherited from a group above.";

        public const string SealCheckBoxName = "KpgGrantSealCheckBox";

        /// <summary>The caption of the check box, in every state the box shows.</summary>
        public const string SealCheckBoxText = "Refuse any profile not named here";

        /// <summary>The tooltip of the box where the box is cleared or ticked.</summary>
        public const string SealCheckBoxHelp =
            "Cleared: a profile with no rule here keeps what the groups above grant. Ticked: this node "
            + "refuses every profile with no rule here, and no grant from a group above reaches this "
            + "node or anything below.";

        /// <summary>The tooltip of the box in the mixed mark.</summary>
        public const string SealCheckBoxMixed =
            "Copies of this database set this box differently, so the gate refuses any profile not "
            + "named here until the box is set again.";

        /// <summary>The tooltip of the box where a version of the setting was lost or cannot be read.</summary>
        public const string SealCheckBoxLost =
            "A record of this setting was deleted outside the plugin or cannot be read, so the gate "
            + "refuses any profile not named here until the box is set again.";

        /// <summary>The further line of the tooltip and the accessible text of a row or a box changed on the tab.</summary>
        public const string ChangedHere = "Changed on this tab. OK writes the change, and Cancel discards the change.";

        /// <summary>The same, on a tab that stopped taking a press after a change was made.</summary>
        public const string ChangedNotWritten = "Changed on this tab. No change can be written now, so OK writes nothing.";

        /// <summary>The word of each state in the State column.</summary>
        /// <remarks>
        /// **A word is what the operator sees in the cell**, and the tooltip of the cell explains the
        /// word to an operator meeting the word for the first time: what happened, what the gate
        /// grants now, and what a press does. A sentence that names a state names the state by its
        /// word, through the constant here.
        /// </remarks>
        public const string NoProfileWord = "No profile";

        /// <summary>A version whose record was deleted outside the plugin, its content nowhere.</summary>
        public const string MissingWord = "Missing";

        /// <summary>A version whose record stands in a text that cannot be read.</summary>
        public const string UnreadableWord = "Unreadable";

        /// <summary>A version of a rule that copies of the database set apart before a synchronize.</summary>
        public const string UnmergedWord = "Unmerged";

        /// <summary>Among those versions, a version in which the rule was taken off.</summary>
        public const string RemovedWord = "Removed";

        /// <summary>The limit row: what this node lets through from above, at most.</summary>
        public const string AtMostWord = "At most";

        /// <summary>A rule from above that this node's box refuses.</summary>
        public const string RefusedWord = "Refused";

        public const string InheritedWord = "Inherited";

        /// <summary>Where the rule of an inherited row comes from.</summary>
        /// <remarks>
        /// **What a state is and what the gate grants are told apart**, every state's tooltip a
        /// sentence of each: the second is left out on a row whose profile the database no longer
        /// names, since a rule for such a profile grants nothing whatever the row shows.
        /// </remarks>
        public const string InheritedTip = "Inherited from a group above.";

        /// <summary>What the gate grants a profile whose rule comes from a group above.</summary>
        public const string InheritedGrants = "The gate grants the permissions shown at this node.";

        /// <summary>The same, for an inherited rule this node cuts, shown above its At most row.</summary>
        public const string InheritedCutTip = "Inherited from a group above with the permissions shown.";

        /// <summary>What the gate grants a profile this node cuts, on the inherited row above the At most row.</summary>
        public const string InheritedCutGrants =
            "The " + AtMostWord + " row under this row decides how much of the rule the gate grants at this node.";

        /// <summary>
        /// The row a rule removed on the tab leaves where no rule from a group above arrives, and
        /// what the gate then grants.
        /// </summary>
        public const string NothingArrives =
            "No rule from a group above arrives at this node, so the gate grants nothing here.";

        /// <summary>What a rule chosen on an inherited row does.</summary>
        public const string InheritedChoice =
            "A rule chosen here replaces the inherited rule at this node and below.";

        /// <summary>What a rule chosen on a row where nothing arrives does.</summary>
        public const string NothingArrivesChoice = "A rule chosen here grants at this node.";

        /// <summary>
        /// What the gate does for a profile this node's box refuses: a rule from above refused, and
        /// the row an X leaves where nothing arrives.
        /// </summary>
        public const string RefusedTip =
            "The box under the table refuses any profile not named here, so every rule from a group "
            + "above stops at this node and the gate grants nothing here.";

        /// <summary>What a rule chosen on a row the box refuses does.</summary>
        public const string RefusedChoice = "A rule chosen here grants at this node again.";

        /// <summary>How far this node lets a grant from above through, on an At most row.</summary>
        public const string AtMostTip =
            "The rule here was removed, or the box below cleared, when less arrived from above than "
            + "arrives now, so this node lets through at most the permissions shown.";

        /// <summary>What the gate grants a profile this node cuts, on the At most row.</summary>
        public const string AtMostGrants = "The gate grants the permissions that this row and the inherited row share.";

        /// <summary>The same, for a rule taken off while the node refused any profile it does not name.</summary>
        public const string AtMostRefusingTip =
            "The rule here was removed while this node refused any profile not named here, so nothing "
            + "from above passes this node and the gate grants nothing here.";

        /// <summary>What the X on an At most row does.</summary>
        public const string AtMostRemoval = "The X lets the whole rule from above through.";

        /// <summary>A rule in several versions made in copies of the database, with the count of rows.</summary>
        public const string UnmergedTip =
            "Copies of this database set this rule differently before the copies were synchronized, and "
            + "each of these {0} rows holds the rule of a copy.";

        /// <summary>Among those versions, a version in which the rule was taken off.</summary>
        public const string RemovedTip =
            "In a copy of this database this rule was removed, so this row lets a grant from above "
            + "through only as far as the permissions shown.";

        /// <summary>What the gate grants a profile whose rule stands in several versions, with the mask.</summary>
        public const string GroupGrants =
            "Until a single rule is left, the gate grants {0} at this node, the permissions all the rows share.";

        /// <summary>What the X on a row of a bound group does: that version goes, and every other row stays.</summary>
        public const string VersionRemoval = "The X removes this row.";

        /// <summary>
        /// What the X on a row does in a group where no version could be left that reads: the rule
        /// is taken off, every version with the rule.
        /// </summary>
        public const string EveryVersionRemoval = "The X removes the rule and every row of the rule.";

        /// <summary>What a rule chosen on a row of such a group does.</summary>
        public const string EveryVersionChoice = "A rule chosen here replaces every row of the rule.";

        /// <summary>What the gate does for a profile a version of whose rule was deleted, its content nowhere.</summary>
        public const string MissingTip =
            "The record of this row was deleted outside the plugin and the content of the record is "
            + "gone, so the gate grants nothing here.";

        /// <summary>What a rule chosen on a Missing row does.</summary>
        public const string MissingChoice = "A rule chosen here replaces the missing record.";

        /// <summary>What the gate does for a profile a version of whose rule cannot be read.</summary>
        public const string UnreadableTip =
            "The record of this row is there and cannot be read, so the gate grants nothing here.";

        /// <summary>What a rule chosen on an Unreadable row does.</summary>
        public const string UnreadableChoice = "A rule chosen here replaces the unreadable record.";

        /// <summary>What the gate does for a rule whose profile the database no longer names.</summary>
        public const string NoProfileTip =
            "This database no longer names this profile, so this rule grants nothing. Adding the profile "
            + "again on the " + GateName.OnATab + " tab of the database settings puts this rule back in force.";

        /// <summary>The same, on a rule from above this node refuses as well.</summary>
        /// <remarks>
        /// Adding the name back does not put such a rule in force here: the row turns into a rule
        /// the box refuses. Both reasons are given, because hearing only the first sent the
        /// operator to add the name back and find the access still refused.
        /// </remarks>
        public const string NoProfileRefusedTip =
            "This database no longer names this profile, so this rule grants nothing, and this node "
            + "refuses the profile even once the profile is added again on the " + GateName.OnATab
            + " tab of the database settings.";

        /// <summary>What a second rule chosen for a profile the table shows is answered with.</summary>
        public const string AlreadyShown = "This profile has a row in the table already.";

        /// <summary>What a rule chosen with no profile is answered with.</summary>
        public const string NameTheProfile = "Name the profile of the rule.";

        /// <summary>What a rule chosen for a name the issuer could not issue is answered with.</summary>
        public const string ProfileNameRule =
            "A profile name is up to 64 ASCII letters, digits, dashes and underscores.";

        /// <summary>What a mask that is not a mask is answered with.</summary>
        public static readonly string MaskRule =
            "A rule is " + Acl.VerbMask.Letters + ", with " + Acl.VerbMask.Absent + " for each permission not granted.";

        /// <summary>What follows the name of a switch in the rule picker: a switch is no verb.</summary>
        public const string SwitchApplies = "(apply checked permissions)";

        /// <summary>What the tab shows where a choice could not be applied; the log holds why.</summary>
        public const string ChoiceNotTaken = "The choice could not be applied. The log holds the reason.";

        /// <summary>What the tab shows where a choice was taken and the table could not be drawn again; the log holds why.</summary>
        public const string ChoiceNotDrawn = "The choice waits for OK, and the table could not be drawn again. The log holds the reason.";

        /// <summary>What the tab shows where a list of choices could not be opened; the log holds why.</summary>
        public const string PickerNotOpened = "The list could not be opened. The log holds the reason.";

        /// <summary>What the tab shows where a list of choices could not be closed; the log holds why.</summary>
        public const string PickerNotClosed = "The list could not be closed. The log holds the reason.";

        /// <summary>What the tab shows where a row has no room to open a picker under.</summary>
        public const string NoRoomForRow = "No room to show the rule. A taller dialog gives the table room.";

        /// <summary>
        /// The warning of a node deeper in the tree than the walk reaches, which no row can show.
        /// </summary>
        public const string TooDeep =
            "Deeper in the tree than the gate reads, so a profile with no rule here or in a group between is refused.";

        /// <summary>Read only: the database has no store key yet.</summary>
        public const string NoAuthorityYet =
            "No certificate authority yet. The " + GateName.OnATab + " tab of the database settings creates the authority.";

        /// <summary>Read only: the store holds the gate back or cannot be judged.</summary>
        public const string StoreUnusable =
            "The data of " + GateName.Product + " in this database cannot be used, so no rule can be written.";

        /// <summary>Read only: nothing is written into a store in this session.</summary>
        public const string NotWatched =
            "The dialogs of KeePass could not be watched in this session, so no rule can be written.";

        /// <summary>Read only: a group or an entry of the plugin's store.</summary>
        public const string StoreNode =
            "Data of " + GateName.Product + ". No client reaches the data, so no rule applies here.";

        /// <summary>Read only: several entries edited together.</summary>
        public const string SeveralEntries =
            "Several entries at once. Rules are shown and changed for a single entry.";

        /// <summary>Read only: an earlier version of an entry.</summary>
        public const string EarlierVersion =
            "An earlier version of the entry. The rules shown are the rules of the entry as the rules stand now.";

        /// <summary>Read only: KeePass names no database for the dialog.</summary>
        public const string NoDatabase =
            "KeePass does not tell the database of this dialog, so no rule can be written.";

        /// <summary>Read only: KeePass does not tell whether the entry dialog is about a single entry as the entry stands.</summary>
        public const string EntryUntold =
            "KeePass does not tell whether this dialog edits a single entry, so the rules are read only.";

        /// <summary>Read only: KeePass names no group for a group dialog.</summary>
        public const string NoGroup =
            "KeePass does not tell the group of this dialog, so no rule can be written.";

        /// <summary>Read only: KeePass names no entry for an entry dialog.</summary>
        public const string NoEntry =
            "KeePass does not tell the entry of this dialog, so no rule can be written.";

        /// <summary>The title of the message shown once a node's dialog has closed with rules that were not kept.</summary>
        public const string NotKeptTitle = GateName.Full;

        /// <summary>
        /// The message shown once a node's dialog has closed with OK and no change of the tab was
        /// written, before the reason.
        /// </summary>
        /// <remarks>
        /// **A change not kept is told, and the log is not enough**: a narrowing the operator takes
        /// to be in force has to be seen to be missing. Nothing is told where nothing was pressed.
        /// </remarks>
        public const string NotKept = "No change made on the " + GateName.OnATab + " tab was kept: ";

        /// <summary>The reason given where the tab stopped taking a press while the dialog was open.</summary>
        public const string NoLongerWritable = "the database stopped taking rules while the dialog was open.";

        /// <summary>
        /// The message shown once a node's dialog has closed with OK and a change of the tab was
        /// left out of what was written, before a line for each reason a change was left out.
        /// </summary>
        public const string NotAllKept = "Not every change made on the " + GateName.OnATab + " tab was kept.";

        /// <summary>The same, where every change of the tab was left out, before a line for each reason.</summary>
        public const string NoneKept = "No change made on the " + GateName.OnATab + " tab was kept.";

        /// <summary>A change left out because the new version would not fit the ceiling of a string.</summary>
        public const string NoRoom = "The new version of a rule no longer fits the room the store gives.";

        /// <summary>A change left out because the store, as the write read the store, no longer lets the change be written.</summary>
        public const string StoreChanged =
            "The rules of the group or entry changed in the database while the dialog was open.";

        /// <summary>A change left out because the change is none the tab can make.</summary>
        public const string ChangeOutOfForm = "A change was not in a form the store takes.";

        /// <summary>A change left out because the store could not be read as a whole at the write.</summary>
        public const string StoreUnreadable =
            "The data of " + GateName.Product + " in this database cannot be used as the data stands.";

        /// <summary>
        /// The last line of the message where some changes were kept and others left out: where the
        /// operator sees which, since no sentence names a profile.
        /// </summary>
        public const string ShownAsKept =
            "The " + GateName.OnATab + " tab of the group or entry shows the rules as kept.";

        /// <summary>The message shown once a node's dialog has closed with OK and the write stopped on a defect.</summary>
        public const string NotKeptDefect =
            "A defect stopped the write of the changes made on the " + GateName.OnATab + " tab, so the "
            + "changes may not have been kept. The log holds the defect.";

        /// <summary>
        /// The line at the top of the page on a group dialog: why there are rows here that the
        /// node does not set, and what happens when a rule is chosen over them.
        /// </summary>
        /// <remarks>
        /// **Only over a table that takes a press**, as every line of the kind: a rule that can be
        /// overwritten is no rule on a tab that takes no change, so there the reason stands alone.
        /// </remarks>
        public const string GroupScope =
            "Rules are inherited from parent groups and can be overwritten.";

        /// <summary>The same, on the dialog of the root group, which nothing is inherited at.</summary>
        public const string RootScope = "Rules set at the root group reach every group and entry below.";

        /// <summary>
        /// The same, on an entry dialog, and the same sentence, because the sentence is equally
        /// true there.
        /// </summary>
        /// <remarks>
        /// Two names for a single string, because which dialog a page is on is a real distinction,
        /// and the day the two differ only the text has to change.
        /// </remarks>
        public const string EntryScope = GroupScope;

        /// <summary>Read only: a dialog with no node behind it.</summary>
        public const string NoNode = "This dialog is about no group or entry.";

        /// <summary>Why nothing can be changed on that page, under the line above.</summary>
        public const string NothingToChange = "Nothing here can be changed.";

        /// <summary>The word the State column shows for a row: its first state, or nothing.</summary>
        public static string WordOf(GrantRow row)
        {
            return row == null || row.States.Count == 0 ? string.Empty : Word(row.States[0]);
        }

        /// <summary>The word of a state.</summary>
        public static string Word(GrantState state)
        {
            switch (state)
            {
                case GrantState.NoProfile:
                    return NoProfileWord;
                case GrantState.Missing:
                    return MissingWord;
                case GrantState.Unreadable:
                    return UnreadableWord;
                case GrantState.Unmerged:
                    return UnmergedWord;
                case GrantState.Removed:
                    return RemovedWord;
                case GrantState.AtMost:
                    return AtMostWord;
                case GrantState.Refused:
                    return RefusedWord;
                default:
                    return InheritedWord;
            }
        }

        /// <summary>
        /// The tooltip of a row's State cell, which is the cell's accessible description and help:
        /// what the gate does for the profile at this node in each state that holds, what a press
        /// on the row does where a press can be taken, and whether the row changed on the tab.
        /// </summary>
        /// <param name="editable">Whether the tab takes a press, which is when the press is told.</param>
        /// <remarks>
        /// **No grant is told for a profile the database no longer names**: the sentence of what
        /// the gate grants is left out of every state on such a row, since the rule grants nothing.
        /// </remarks>
        public static string StateTip(GrantRow row, bool editable)
        {
            if (row == null)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            var states = row.States;
            var named = !states.Contains(GrantState.NoProfile);
            foreach (var state in states)
            {
                switch (state)
                {
                    case GrantState.NoProfile:
                        lines.Add(states.Contains(GrantState.Refused) ? NoProfileRefusedTip : NoProfileTip);
                        break;

                    case GrantState.Missing:
                        lines.Add(Joined(MissingTip, editable ? LostPress(row, MissingChoice) : null));
                        break;

                    case GrantState.Unreadable:
                        lines.Add(Joined(UnreadableTip, editable ? LostPress(row, UnreadableChoice) : null));
                        break;

                    case GrantState.Unmerged:
                        lines.Add(Joined(
                            Joined(Formatted(UnmergedTip, row.Group), named ? Formatted(GroupGrants, row.Granted) : null),
                            editable && row.Removable ? VersionRemoval : null));
                        break;

                    case GrantState.Removed:
                        lines.Add(Joined(
                            Joined(RemovedTip, named ? Formatted(GroupGrants, row.Granted) : null),
                            editable && row.Removable ? VersionRemoval : null));
                        break;

                    case GrantState.AtMost:
                        lines.Add(Joined(
                            row.Refusing ? AtMostRefusingTip : Joined(AtMostTip, named ? AtMostGrants : null),
                            editable ? AtMostRemoval : null));
                        break;

                    case GrantState.Refused:
                        if (named)
                        {
                            lines.Add(Joined(RefusedTip, editable ? RefusedChoice : null));
                        }

                        break;

                    default:
                        lines.Add(InheritedLine(row, named, editable));
                        break;
                }
            }

            if (row.Changed)
            {
                lines.Add(editable ? ChangedHere : ChangedNotWritten);
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// The tooltip of a row's Permissions cell: on a rule here over an inherited rule, which
        /// way the rule here replaces the inherited rule; and whether the row changed on the tab.
        /// </summary>
        /// <param name="editable">Whether the tab takes a press, which is whether OK writes a change.</param>
        public static string MaskTip(GrantRow row, bool editable)
        {
            if (row == null)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            if (row.Kind == GrantRowKind.Own && row.InheritedMask != null)
            {
                lines.Add(Replacing(row.Mask, row.InheritedMask) + " " + row.InheritedMask + InheritedFrom);
            }

            if (row.Changed)
            {
                lines.Add(editable ? ChangedHere : ChangedNotWritten);
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>The tooltip of the check box, and its accessible description and help.</summary>
        /// <param name="editable">Whether the tab takes a press, which is whether OK writes a change.</param>
        public static string BoxTip(GrantBox box, bool changed, bool editable)
        {
            var tip = box == GrantBox.Mixed ? SealCheckBoxMixed : box == GrantBox.Lost ? SealCheckBoxLost : SealCheckBoxHelp;
            return changed ? tip + Environment.NewLine + (editable ? ChangedHere : ChangedNotWritten) : tip;
        }

        /// <summary>The line of an inherited row: where the rule comes from, what the gate grants, and what a choice does.</summary>
        private static string InheritedLine(GrantRow row, bool named, bool editable)
        {
            if (row.InheritedMask == null)
            {
                return Joined(NothingArrives, editable ? NothingArrivesChoice : null);
            }

            return row.Bound
                ? Joined(Joined(InheritedCutTip, named ? InheritedCutGrants : null), editable ? InheritedChoice : null)
                : Joined(Joined(InheritedTip, named ? InheritedGrants : null), editable ? InheritedChoice : null);
        }

        /// <summary>
        /// What a press on a lost row, missing or unreadable, does: the choice of its state standing
        /// alone, an X in a group.
        /// </summary>
        private static string LostPress(GrantRow row, string choice)
        {
            if (!row.Bound)
            {
                return choice;
            }

            if (row.TakesRule)
            {
                return Joined(EveryVersionChoice, EveryVersionRemoval);
            }

            return row.Removable ? VersionRemoval : null;
        }

        /// <summary>
        /// What a rule here does to the rule a group above grants the same profile, as the word
        /// that starts the sentence telling of it.
        /// </summary>
        /// <remarks>
        /// Worked out rather than assumed. Nearest wins, so a rule here replaces the rule above in
        /// full, and a rule that grants more is in force in full: a tooltip naming a widening as a
        /// narrowing is wrong in the direction a sentence about permissions must never be wrong in.
        ///
        /// **Compared as what the gate grants, not as letters.** A switch is not a verb: it widens
        /// the verbs beside it that read, and the resolver asks neither switch for a strip or a
        /// delete, so a switch on a rule that holds no other reading verb grants nothing.
        /// </remarks>
        public static string Replacing(string mask, string inherited)
        {
            var here = Effective(Acl.VerbMask.Read(mask));
            var above = Effective(Acl.VerbMask.Read(inherited));
            if (here == null || above == null)
            {
                return "Replaces";
            }

            var inside = here.All(above.Contains);
            var around = above.All(here.Contains);

            if (inside && around)
            {
                return "The same as";
            }

            if (inside)
            {
                return "Narrows";
            }

            return around ? "Widens" : "Replaces";
        }

        /// <summary>
        /// A mask as the gate grants it: its verbs, and its switches only beside a verb that reads,
        /// which is every verb but strip and delete. Null for a mask that will not read.
        /// </summary>
        private static IList<Acl.AclVerb> Effective(IList<Acl.AclVerb> mask)
        {
            if (mask == null)
            {
                return null;
            }

            var reading = mask.Any(verb => !Acl.AclVerbs.IsScope(verb) && verb != Acl.AclVerb.Strip && verb != Acl.AclVerb.Delete);
            return mask.Where(verb => reading || !Acl.AclVerbs.IsScope(verb)).ToList();
        }

        private static string Formatted(string format, object value)
        {
            return string.Format(CultureInfo.InvariantCulture, format, value);
        }

        private static string Joined(string first, string second)
        {
            return string.IsNullOrEmpty(second) ? first : first + " " + second;
        }
    }
}
