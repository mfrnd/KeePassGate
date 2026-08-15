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

        /// <summary>
        /// The dialog behind Edit Group. Pairing refuses to write while one of these is open,
        /// because it holds a copy of the group's custom data and puts it back on OK.
        /// </summary>
        public const string GroupFormTypeName = "KeePass.Forms.GroupForm";

        /// <summary>
        /// The group that dialog is editing. Read by reflection, and a KeePass that renames it
        /// leaves pairing refused while any group dialog is open, which is the safe direction.
        /// </summary>
        public const string GroupFormGroupField = "m_pwGroup";

        /// <summary>Whether that dialog is Add Group rather than Edit Group.</summary>
        /// <remarks>
        /// KeePass puts a new group into the tree before it shows the dialog and takes it back out
        /// on Cancel, with no deleted object and no recycle bin, so whatever was put into it in
        /// between goes with it. A KeePass that renames this leaves every group dialog taken for
        /// an Add Group, which refuses additions to a group being edited rather than allowing them
        /// into one that may vanish.
        /// </remarks>
        public const string GroupFormCreatingField = "m_bCreatingNew";

        /// <summary>The dialog behind Edit Entry, where an entry's own grants are edited.</summary>
        public const string EntryFormTypeName = "KeePass.Forms.PwEntryForm";

        /// <summary>The entry that dialog is editing.</summary>
        public const string EntryFormEntryField = "m_pwEntry";

        /// <summary>
        /// The merged-selection context, present only while several entries are edited at once.
        /// </summary>
        /// <remarks>
        /// Present means the entry behind the dialog is a synthetic one KeePass built by merging
        /// the selection, and every value the selection disagrees about has been replaced by a
        /// cue string. Its `CustomData` is therefore not a document, and what is written back is
        /// applied to every selected entry. For a key the merged copy no longer carries, that
        /// means deleting it from all of them.
        /// </remarks>
        public const string EntryFormMultipleField = "m_mvec";

        /// <summary>Which of the four things the entry dialog is doing, `PwEditMode`.</summary>
        /// <remarks>
        /// `ViewReadOnlyEntry` is a history revision opened for reading. `SaveEntry` returns on
        /// its first instruction in that mode, so nothing typed anywhere on the dialog is kept,
        /// and the entry behind it is the old revision rather than the one in force.
        /// </remarks>
        public const string EntryFormEditModeField = "m_pwEditMode";

        /// <summary>The `PwEditMode` value that means a history revision opened for reading.</summary>
        public const string EntryFormReadOnlyMode = "ViewReadOnlyEntry";

        /// <summary>
        /// How many days KeePass's options give every new entry before it expires, negative when
        /// they give none: "By default, new entries expire in the following number of days", on the
        /// Security page of Tools then Options. What a create asking for `auto` applies.
        /// </summary>
        /// <remarks>
        /// Read directly rather than found by name, so the compiler already pins that it exists, and
        /// `nameof` makes this constant the name the read binds to rather than a second copy of it.
        /// Listed so that this list is the whole of what the plugin reaches for, and so that the
        /// host tests pin what it means, which no compiler can: switched off unless somebody switched
        /// it on, and a number of days rather than anything else.
        /// </remarks>
        public const string NewEntryExpiresInDaysProperty =
            nameof(KeePass.App.Configuration.AceDefaults.NewEntryExpiresInDays);

        /// <summary>The dialog behind Tools then TAN Wizard.</summary>
        /// <remarks>
        /// It is given the selected group when it opens and adds one entry per TAN to that group on
        /// OK, so while it is open the group is usually empty and holds nothing of what it is about
        /// to receive. Removed underneath, the TANs go into the recycle bin, or with no bin into a
        /// group that is no longer in the tree and never reaches the file.
        /// </remarks>
        public const string TanWizardFormTypeName = "KeePass.Forms.TanWizardForm";

        /// <summary>The group that wizard fills on OK.</summary>
        public const string TanWizardFormGroupField = "m_pgStorage";

        /// <summary>
        /// The tab strip on the group and entry dialogs, which both call it what the database
        /// settings dialog does.
        /// </summary>
        public const string NodeFormTabControlName = "m_tabMain";

        /// <summary>
        /// **The dialog's own copy of a node's custom data**, on the group dialog and the entry
        /// dialog alike, and the only correct place for the grant editor to write.
        /// </summary>
        /// <remarks>
        /// Both dialogs take a copy when they open and write that copy back over the node when
        /// they are accepted. A grant written straight to the live group or entry therefore
        /// survives Cancel and is destroyed by OK, silently and completely, which is the worst way
        /// for an access control editor to fail: the rule is on screen as the dialog closes and
        /// gone from the database afterwards.
        ///
        /// A KeePass that renames this leaves the editor with nowhere safe to write, so it shows
        /// the grants and refuses to edit them rather than writing somewhere that will be thrown
        /// away.
        /// </remarks>
        public const string NodeFormCustomDataField = "m_sdCustomData";

        /// <summary>
        /// The database behind a group or entry dialog, which is what says which profiles are worth
        /// offering. Absent, the editor has no profile to offer, and a rule is started by choosing
        /// one, so it shows the rules, changes none of them, and says why.
        /// </summary>
        public const string NodeFormDatabaseField = "m_pwDatabase";

        /// <summary>
        /// The dialog behind Tools then Database Maintenance, which holds a copy of the database's
        /// own custom data and puts it back on Close whenever the live one has grown or shrunk.
        /// </summary>
        /// <remarks>
        /// The same hazard as the group dialog and easier to miss, because the write-back is
        /// conditional: it compares the count it copied against the count it finds, so a pairing
        /// landing while this is open is destroyed even though the operator touched nothing. That
        /// its one button is both the accept and the cancel button, with `DialogResult.Cancel`,
        /// means there is no way for an operator to decline it either.
        /// </remarks>
        public const string MaintenanceFormTypeName = "KeePass.Forms.DatabaseOperationsForm";

        /// <summary>The database that dialog is working on.</summary>
        public const string MaintenanceFormDatabaseField = "m_pwDatabase";

        /// <summary>
        /// How a named database is saved. Not `UIFileSave`, which saves whichever document is
        /// active: pairing names the database it is writing to and must not save a different one.
        /// </summary>
        public const string MainFormSaveDatabaseMethod = "SaveDatabase";
    }
}
