using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Every name the plugin reaches for inside KeePass, checked against the KeePass actually
    /// installed. A KeePass upgrade that moves one of these breaks the plugin quietly at run time
    /// and loudly here, which is the whole point of the fixture.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class KeePassContractTests
    {
        private static Assembly KeePassAssembly
        {
            get { return typeof(Plugin).Assembly; }
        }

        [Test]
        public void The_database_settings_dialog_still_has_the_name_we_look_for()
        {
            var type = KeePassAssembly.GetType(KeePassContract.DatabaseSettingsFormTypeName, false);

            Assert.That(type, Is.Not.Null,
                KeePassContract.DatabaseSettingsFormTypeName + " is gone from " + KeePassAssembly.GetName().Name);
            Assert.That(typeof(Form).IsAssignableFrom(type), Is.True);
        }

        [Test]
        public void That_dialog_still_carries_a_tab_control_called_m_tabMain()
        {
            using (var form = KeePassDialogs.NewDatabaseSettingsForm())
            {
                var tabs = ControlTree.FindByName<TabControl>(form, KeePassContract.DatabaseSettingsTabControlName);

                Assert.That(tabs, Is.Not.Null,
                    "KeePass no longer names its settings tab control " +
                    KeePassContract.DatabaseSettingsTabControlName +
                    ", so the injector is running on its fallback.");
                Assert.That(tabs.TabPages.Count, Is.GreaterThan(0));
            }
        }

        [Test]
        public void The_plugin_base_class_still_has_the_two_methods_the_entry_point_overrides()
        {
            var initialize = typeof(Plugin).GetMethod("Initialize", new[] { typeof(IPluginHost) });
            var terminate = typeof(Plugin).GetMethod("Terminate", Type.EmptyTypes);

            Assert.That(initialize, Is.Not.Null);
            Assert.That(initialize.ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(terminate, Is.Not.Null);
        }

        [Test]
        public void The_window_manager_still_announces_dialogs_the_way_the_adapter_expects()
        {
            var added = typeof(GlobalWindowManager).GetEvent("WindowAdded");
            var removed = typeof(GlobalWindowManager).GetEvent("WindowRemoved");

            Assert.That(added, Is.Not.Null);
            Assert.That(removed, Is.Not.Null);
            Assert.That(added.EventHandlerType, Is.EqualTo(typeof(EventHandler<GwmWindowEventArgs>)));
            Assert.That(removed.EventHandlerType, Is.EqualTo(typeof(EventHandler<GwmWindowEventArgs>)));
            Assert.That(typeof(GwmWindowEventArgs).GetProperty("Form"), Is.Not.Null);
        }

        [Test]
        public void The_window_manager_can_still_be_driven_by_a_test()
        {
            // AddWindow and RemoveWindow are how this suite simulates KeePass opening a dialog.
            Assert.That(typeof(GlobalWindowManager).GetMethod("AddWindow", new[] { typeof(Form) }), Is.Not.Null);
            Assert.That(typeof(GlobalWindowManager).GetMethod("RemoveWindow", new[] { typeof(Form) }), Is.Not.Null);
        }

        [Test]
        public void The_settings_dialog_still_names_the_database_it_is_editing()
        {
            var type = KeePassAssembly.GetType(KeePassContract.DatabaseSettingsFormTypeName, false);
            var property = type.GetProperty(
                KeePassContract.DatabaseSettingsFormDatabaseProperty, BindingFlags.Public | BindingFlags.Instance);

            Assert.That(property, Is.Not.Null,
                "The tab reaches the database through this, and degrades to an empty tab without it.");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(KeePassLib.PwDatabase)));
        }

        [Test]
        public void The_settings_dialog_still_holds_its_own_copy_of_the_recycle_bin_choice()
        {
            // Why a removal that would make the first recycle bin is refused while this dialog is
            // open. The combo is filled from `RecycleBinUuid` when the dialog loads and the
            // dictionary says which group each of its items means; an index the dictionary does not
            // hold is the item naming no group, and that is the branch whose OK sets
            // `RecycleBinUuid` back to zero. A KeePass that drops either of these is one where the
            // refusal is guarding nothing, which is worth being told about rather than carrying.
            var type = KeePassAssembly.GetType(KeePassContract.DatabaseSettingsFormTypeName, false);

            var combo = type.GetField(
                KeePassContract.DatabaseSettingsFormRecycleBinComboField,
                BindingFlags.NonPublic | BindingFlags.Instance);
            var groups = type.GetField(
                KeePassContract.DatabaseSettingsFormRecycleBinGroupsField,
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(combo, Is.Not.Null,
                KeePassContract.DatabaseSettingsFormRecycleBinComboField + " is gone, so that dialog no "
                + "longer offers the choice the refusal exists for.");
            Assert.That(combo.FieldType, Is.EqualTo(typeof(ComboBox)));
            Assert.That(groups, Is.Not.Null,
                KeePassContract.DatabaseSettingsFormRecycleBinGroupsField + " is gone, so which item "
                + "means which group is decided somewhere else now.");
        }

        [Test]
        public void The_main_window_still_lists_the_open_databases()
        {
            // This is how the gate knows what it can serve.
            var documentManager = typeof(KeePass.Forms.MainForm).GetProperty("DocumentManager");
            Assert.That(documentManager, Is.Not.Null);

            var open = documentManager.PropertyType.GetMethod("GetOpenDatabases", Type.EmptyTypes);
            Assert.That(open, Is.Not.Null);
            Assert.That(typeof(System.Collections.Generic.IEnumerable<KeePassLib.PwDatabase>)
                .IsAssignableFrom(open.ReturnType), Is.True);
        }

        [Test]
        public void The_maintenance_dialog_still_has_the_name_pairing_watches_for()
        {
            // Recognized by this name alone, so a KeePass that renamed the dialog would have it go
            // unnoticed: nothing would refuse a record while it is open, and its Close would put
            // back its stale copy of the database's custom data over the one that landed.
            var type = KeePassAssembly.GetType(KeePassContract.MaintenanceFormTypeName, false);

            Assert.That(type, Is.Not.Null,
                KeePassContract.MaintenanceFormTypeName + " is gone, so an admission record could be "
                + "written while it is open and destroyed when it closes.");
            Assert.That(typeof(Form).IsAssignableFrom(type), Is.True);
        }

        [Test]
        public void The_maintenance_dialog_still_names_the_database_it_is_working_on()
        {
            var type = KeePassAssembly.GetType(KeePassContract.MaintenanceFormTypeName, false);
            var field = type.GetField(
                KeePassContract.MaintenanceFormDatabaseField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "Without this, no record may be written while that dialog is open on any database, "
                + "which is safe and useless.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.PwDatabase)));
        }

        [Test]
        public void The_group_dialog_still_has_the_name_the_writer_watches_for()
        {
            var type = KeePassAssembly.GetType(KeePassContract.GroupFormTypeName, false);

            Assert.That(type, Is.Not.Null,
                KeePassContract.GroupFormTypeName + " is gone, so nothing would ever be recognized as one.");
            Assert.That(typeof(Form).IsAssignableFrom(type), Is.True);
        }

        [Test]
        public void The_group_dialog_still_names_the_group_it_is_editing()
        {
            var type = KeePassAssembly.GetType(KeePassContract.GroupFormTypeName, false);
            var field = type.GetField(
                KeePassContract.GroupFormGroupField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "Without this, every write to a group is refused while any group dialog is open, " +
                "which is safe and useless.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.PwGroup)));
        }

        [Test]
        public void The_tan_wizard_still_names_the_group_it_fills()
        {
            var type = KeePassAssembly.GetType(KeePassContract.TanWizardFormTypeName, false);

            Assert.That(type, Is.Not.Null,
                KeePassContract.TanWizardFormTypeName + " is gone, so the group it fills is never "
                + "recognized and can be removed underneath it.");
            Assert.That(typeof(Form).IsAssignableFrom(type), Is.True);

            var field = type.GetField(
                KeePassContract.TanWizardFormGroupField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "Without this, no group may be removed while the wizard is open, which is safe and useless.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.PwGroup)));
        }

        [Test]
        public void The_group_dialog_still_says_when_it_is_adding_a_group()
        {
            var type = KeePassAssembly.GetType(KeePassContract.GroupFormTypeName, false);
            var field = type.GetField(
                KeePassContract.GroupFormCreatingField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                KeePassContract.GroupFormCreatingField + " is gone, so every group dialog is taken for "
                + "Add Group and nothing may be added to a group while one is open on it.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));
        }

        [Test]
        public void The_entry_dialog_still_says_when_it_is_about_several_entries()
        {
            // Without this the tab reads a merged selection's cue string as a document, calls the
            // rules unreadable, and offers a button that removes the key from every entry chosen.
            var type = KeePassAssembly.GetType(KeePassContract.EntryFormTypeName, false);
            var field = type.GetField(
                KeePassContract.EntryFormMultipleField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                KeePassContract.EntryFormMultipleField + " is gone, so a multiple selection would "
                + "look like one entry and its rules would be written over all of them.");
        }

        [Test]
        public void The_entry_dialog_still_says_when_it_is_only_being_read()
        {
            // A history revision. KeePass keeps nothing typed on that dialog, so an editable tab
            // there is one that throws the operator's work away and shows an old revision's rules
            // as though they were in force.
            var type = KeePassAssembly.GetType(KeePassContract.EntryFormTypeName, false);
            var field = type.GetField(
                KeePassContract.EntryFormEditModeField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                KeePassContract.EntryFormEditModeField + " is gone, so a revision opened for reading "
                + "would take edits nothing keeps.");
            Assert.That(Enum.GetNames(field.FieldType), Contains.Item(KeePassContract.EntryFormReadOnlyMode),
                "the mode is compared by name, so the name has to be one this enum still has");
        }

        [Test]
        public void The_entry_dialog_still_has_the_name_the_editor_mounts_on()
        {
            var type = KeePassAssembly.GetType(KeePassContract.EntryFormTypeName, false);

            Assert.That(type, Is.Not.Null,
                KeePassContract.EntryFormTypeName + " is gone, so an entry could never be given a grant.");
            Assert.That(typeof(Form).IsAssignableFrom(type), Is.True);
        }

        [Test]
        public void The_entry_dialog_still_names_the_entry_it_is_editing()
        {
            var type = KeePassAssembly.GetType(KeePassContract.EntryFormTypeName, false);
            var field = type.GetField(
                KeePassContract.EntryFormEntryField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "Without this the editor cannot tell which entry a dialog is about.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.PwEntry)));
        }

        [Test]
        public void The_expiry_KeePass_gives_new_entries_is_still_a_number_of_days_and_off_until_switched_on()
        {
            // What a create asking for `auto` applies, read off the options where KeePass's own
            // Add Entry reads it. A number of days is what the create hands `AddDays`, and a
            // negative one is the option switched off, which is how KeePass ships: one that
            // shipped with it on would give every entry `auto` makes an expiry nobody chose.
            var property = typeof(KeePass.App.Configuration.AceDefaults).GetProperty(
                KeePassContract.NewEntryExpiresInDaysProperty, BindingFlags.Public | BindingFlags.Instance);

            Assert.That(property, Is.Not.Null,
                KeePassContract.NewEntryExpiresInDaysProperty + " is gone from KeePass's options");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(new KeePass.App.Configuration.AceDefaults().NewEntryExpiresInDays, Is.LessThan(0),
                "a KeePass that ships the option switched on");
            Assert.That(
                typeof(KeePass.App.Configuration.AppConfigEx).GetProperty("Defaults").PropertyType,
                Is.EqualTo(typeof(KeePass.App.Configuration.AceDefaults)),
                "the options are read through `Program.Config.Defaults`, so that is where they have to be");
        }

        [Test]
        public void An_attachment_still_cannot_be_changed_once_it_is_made()
        {
            // What `NodeTags` keeps a digest against: an attachment's bytes, hashed once per object,
            // on the strength of the object never changing. What this pins is the public surface:
            // every public instance member reads and no public property can be set, so nothing
            // outside KeePassLib, this plugin or another, can change one in place. KeePassLib's own
            // code is not pinned, and what stands for it is that KeePass keeps the object's hash
            // code inside it once asked, which it could not do of an object whose bytes moved; a
            // KeePass that stopped keeping it is told apart below. A KeePass that added a way to
            // change one in place would leave a tag standing over bytes it no longer describes.
            var type = typeof(KeePassLib.Security.ProtectedBinary);

            // cspell:ignore Xorred
            var readers = new[]
            {
                "get_IsProtected", "get_Length", "ReadData", "ReadXorredData", "GetHashCode", "Equals",
            };

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.That(readers, Does.Contain(method.Name),
                    "ProtectedBinary has a public " + method.Name + " that may change it in place");
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(property.CanWrite, Is.False, "ProtectedBinary." + property.Name + " can be set");
            }

            Assert.That(type.GetField("m_oiHash", BindingFlags.NonPublic | BindingFlags.Instance), Is.Not.Null,
                "KeePass no longer keeps an attachment's hash code inside it, so it may no longer be relying on "
                + "the bytes staying put either");
        }

        [TestCase(KeePassContract.GroupFormTypeName)]
        [TestCase(KeePassContract.EntryFormTypeName)]
        public void Both_node_dialogs_still_keep_their_own_copy_of_custom_data(string typeName)
        {
            // The field the grant editor writes into, and the reason it can. Both dialogs copy a
            // node's custom data when they open and write the copy back on OK, so a grant written
            // to the live node survives Cancel and is destroyed by OK. A KeePass that renames this
            // leaves the editor showing grants it will not let anybody change, which is the safe
            // direction and a visible one.
            var type = KeePassAssembly.GetType(typeName, false);
            var field = type.GetField(
                KeePassContract.NodeFormCustomDataField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                typeName + " no longer keeps a working copy, so the editor has nowhere safe to write.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.Collections.StringDictionaryEx)));
        }

        [TestCase(KeePassContract.GroupFormTypeName)]
        [TestCase(KeePassContract.EntryFormTypeName)]
        public void Both_node_dialogs_still_call_their_tab_strip_what_the_settings_dialog_does(string typeName)
        {
            var type = KeePassAssembly.GetType(typeName, false);
            var field = type.GetField(
                KeePassContract.NodeFormTabControlName, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                typeName + " renamed its tab strip, so the editor falls back to the first one it finds.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(TabControl)));
        }

        [TestCase(KeePassContract.GroupFormTypeName)]
        [TestCase(KeePassContract.EntryFormTypeName)]
        public void Both_node_dialogs_still_carry_the_database_they_belong_to(string typeName)
        {
            // What `NodeDialog` reads a node's database out of, which is how the grant editor knows
            // which profile list to offer. A KeePass that renames it leaves the editor with no
            // database and nothing to offer, so it is pinned like the rest of them.
            var type = KeePassAssembly.GetType(typeName, false);
            var field = type.GetField(
                KeePassContract.NodeFormDatabaseField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                typeName + " no longer names the database it belongs to, so the editor cannot find it.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.PwDatabase)));
        }

        [Test]
        public void The_main_window_can_still_save_a_named_database()
        {
            // Not `UIFileSave`, which saves whichever document is active. Pairing writes to the
            // database the connection resolved to and has to save that one.
            var save = typeof(KeePass.Forms.MainForm).GetMethod(
                KeePassContract.MainFormSaveDatabaseMethod,
                new[] { typeof(KeePassLib.PwDatabase), typeof(object) });

            Assert.That(save, Is.Not.Null,
                "KeePass no longer takes a database to save, so pairing cannot make a record durable.");
        }

        [Test]
        public void The_main_window_still_says_when_it_has_blocked_itself()
        {
            // The window a write refuses behind. Answered from the count `UIBlockInteraction`
            // keeps, which is what a save, an import and a synchronize raise. Asked of the shape
            // rather than driven: blocking the window reaches `Enabled` on a main window that a
            // test cannot build, and the count is only ever written by that one method.
            var method = typeof(MainForm).GetMethod(
                KeePassContract.MainFormInteractionBlockedMethod, Type.EmptyTypes);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(typeof(MainForm).GetMethod("UIBlockInteraction", new[] { typeof(bool) }), Is.Not.Null,
                "what raises the answer is gone, so a save may no longer be what makes it true");
        }

        [Test]
        public void Both_loggers_an_import_can_choose_still_block_the_main_window()
        {
            // File, Import and File, Synchronize both reach `ImportUtil.Import`, which picks one of
            // two status loggers and hands it to `PwDatabase.MergeIn`, whose loops pump through it.
            // A write is kept out of that merge only because both loggers block the main window
            // before the merge starts: the default one in `StartLogging`, and the one
            // `ShowImportStatusDialog` asks for when it builds its window, which it does at the
            // first status line, before any file is read. Read off the IL, because both need a
            // main window to be driven and a test cannot build one.
            var block = typeof(MainForm).GetMethod("UIBlockInteraction", new[] { typeof(bool) });
            var import = Array.Find(
                typeof(KeePass.DataExchange.ImportUtil).GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
                method => method.Name == "Import" && method.GetParameters().Length == 9);

            Assert.That(import, Is.Not.Null, "the import every merge goes through is gone");
            Assert.That(Calls(import, typeof(UIBlockerStatusLogger).GetConstructor(new[] { typeof(Form) })), Is.True,
                "the import no longer chooses the logger that blocks the window");
            Assert.That(Calls(import, typeof(OnDemandStatusDialog).GetConstructor(new[] { typeof(bool), typeof(Form) })), Is.True,
                "the import no longer chooses the status dialog");
            Assert.That(Calls(typeof(UIBlockerStatusLogger).GetMethod("StartLogging"), block), Is.True,
                "the default import logger no longer blocks the window, so a write can land inside a merge");
            Assert.That(Calls(
                    typeof(OnDemandStatusDialog).GetMethod("ConstructStatusDialog", BindingFlags.Instance | BindingFlags.NonPublic),
                    block),
                Is.True,
                "the import status dialog no longer blocks the window, so a write can land inside a merge");
        }

        [Test]
        public void Emptying_the_recycle_bin_asks_while_the_count_is_raised()
        {
            // KeePass lists what the bin holds, asks, and on yes destroys what the bin holds by
            // then, so an entry an API delete moved in while the question was up would be destroyed
            // without having been on the list the operator agreed to. The writer refuses every
            // change while `MessageService` counts a window KeePass is waiting on, and the question
            // is counted: a task dialog raises the count for as long as it is shown, and where no
            // task dialog can be shown KeePass asks in a message box, which it counts as well.
            var empty = typeof(MainForm).GetMethod("EmptyRecycleBin", BindingFlags.Instance | BindingFlags.NonPublic);
            var shown = typeof(VistaTaskDialog).GetMethod("ShowDialog", new[] { typeof(Form) });

            Assert.That(empty, Is.Not.Null, "the method behind Empty Recycle Bin is gone");
            Assert.That(Calls(empty, typeof(VistaTaskDialog).GetMethod("ShowDialog", Type.EmptyTypes)), Is.True,
                "emptying the bin no longer asks in a task dialog");
            Assert.That(
                Calls(empty, typeof(KeePassLib.Utility.MessageService).GetMethod("AskYesNo", new[] { typeof(string) })),
                Is.True,
                "nor in a message box where no task dialog can be shown");
            Assert.That(
                Calls(shown, typeof(KeePassLib.Utility.MessageService).GetMethod("ExternalIncrementMessageCount")),
                Is.True,
                "a task dialog no longer counts itself, so a delete can land while the bin is being emptied");
        }

        [Test]
        public void The_export_a_trigger_runs_swaps_neither_the_key_nor_the_root_group()
        {
            // The count refuses a write while an export has the database's key or root group
            // swapped for its own, and the trigger action that exports a database raises no count.
            // It needs none only while it asks for neither swap, which the export dialog alone
            // does, through the two flags it copies off its own check boxes.
            // Internal to KeePass, so asked for by name.
            var provider = typeof(MainForm).Assembly.GetType("KeePass.Ecas.EcasDefaultActionProvider");
            var action = provider == null
                ? null
                : provider.GetMethod("ExportDatabaseFile", BindingFlags.Static | BindingFlags.NonPublic);
            var export = typeof(KeePass.DataExchange.ExportUtil).GetMethod(
                "Export",
                new[]
                {
                    typeof(KeePass.DataExchange.PwExportInfo), typeof(string),
                    typeof(KeePassLib.Serialization.IOConnectionInfo),
                });
            var swaps = new[] { "ExportMasterKeySpec", "ExportParentGroups" };

            Assert.That(action, Is.Not.Null, "the trigger action that exports a database is gone");
            Assert.That(Calls(action, export), Is.True, "the trigger no longer exports through this overload");

            foreach (var swap in swaps)
            {
                var setter = typeof(KeePass.DataExchange.PwExportInfo)
                    .GetProperty(swap, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .GetSetMethod(true);

                Assert.That(Calls(action, setter), Is.False, swap + " is asked for by the trigger");
                Assert.That(Calls(export, setter), Is.False, swap + " is asked for on the trigger's way in");
                Assert.That(Calls(typeof(ExchangeDataForm).GetMethod(
                        "PrepareExchangeEx", BindingFlags.Instance | BindingFlags.NonPublic), setter),
                    Is.True, swap + " is no longer what the export dialog sets");
            }
        }

        [Test]
        public void The_settings_dialog_leaves_the_window_manager_in_a_close_handler_it_subscribes_when_built()
        {
            // The KPG tab applies what it held from a `FormClosed` handler of its own, subscribed
            // as the tab is built, which is after the dialog's constructor. The dialog subscribes
            // its own handler there, and that one takes it off the window manager, which detaches
            // the page and disposes the panel: the tab's handler therefore runs with the page gone
            // and must need nothing from it. `AuthorityPanelFactory.Commit` says so, on this.
            var dialog = typeof(DatabaseSettingsForm);
            var closed = dialog.GetMethod(
                "OnFormClosed",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new[] { typeof(object), typeof(FormClosedEventArgs) },
                null);
            var build = dialog.GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(closed, Is.Not.Null, "the dialog's own close handler is gone");
            Assert.That(build, Is.Not.Null, "the dialog no longer builds itself in InitializeComponent");
            Assert.That(Calls(dialog.GetConstructor(Type.EmptyTypes), build), Is.True,
                "the dialog no longer builds itself as it is constructed");
            Assert.That(Calls(build, typeof(Form).GetEvent("FormClosed").GetAddMethod()), Is.True,
                "the dialog no longer subscribes to its own close as it is built");
            Assert.That(Calls(closed, typeof(GlobalWindowManager).GetMethod("RemoveWindow", new[] { typeof(Form) })),
                Is.True,
                "the dialog no longer takes itself off the window manager when it closes");
        }

        /// <summary>Whether a method's body calls, or constructs through, the other one.</summary>
        /// <remarks>
        /// The token after every call, virtual call and constructor call opcode in the body is
        /// resolved against its module.
        /// The scan is byte by byte rather than instruction by instruction, so an operand that
        /// happens to hold one of those opcodes resolves to something else or to nothing, and can
        /// only ever fail to match rather than match wrongly.
        /// </remarks>
        private static bool Calls(MethodBase caller, MethodBase callee)
        {
            var body = caller.GetMethodBody().GetILAsByteArray();

            for (var i = 0; i + 4 < body.Length; i++)
            {
                if (body[i] != 0x28 && body[i] != 0x6F && body[i] != 0x73)
                {
                    continue;
                }

                try
                {
                    var called = caller.Module.ResolveMethod(BitConverter.ToInt32(body, i + 1));
                    if (called.Module == callee.Module && called.MetadataToken == callee.MetadataToken)
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // Not a method token, which the byte by byte scan meets in operands.
                }
                catch (BadImageFormatException)
                {
                    // The same, for a token that names no row.
                }
            }

            return false;
        }

        [Test]
        public void An_export_still_shows_in_the_count_of_windows_KeePass_is_waiting_on()
        {
            // `PerformExport` raises the count rather than blocking the window, so this is the only
            // signal a write has that an export is walking the live tree.
            var property = typeof(KeePassLib.Utility.MessageService).GetProperty(
                KeePassContract.MessageServiceCountProperty, BindingFlags.Public | BindingFlags.Static);

            Assert.That(property, Is.Not.Null);

            var before = KeePassLib.Utility.MessageService.CurrentMessageCount;

            KeePassLib.Utility.MessageService.ExternalIncrementMessageCount();
            try
            {
                Assert.That(KeePassLib.Utility.MessageService.CurrentMessageCount, Is.EqualTo(before + 1),
                    "raising the count no longer shows in the answer a write asks for");
            }
            finally
            {
                KeePassLib.Utility.MessageService.ExternalDecrementMessageCount();
            }

            Assert.That(KeePassLib.Utility.MessageService.CurrentMessageCount, Is.EqualTo(before));
        }

        [Test]
        public void KeePass_still_announces_a_database_opening_and_closing()
        {
            // How the plugin learns about a lock. Without these it never drops the authority key
            // and never mints a responder, and nothing about that would be visible at run time.
            var opened = typeof(KeePass.Forms.MainForm).GetEvent("FileOpened");
            var closed = typeof(KeePass.Forms.MainForm).GetEvent("FileClosed");

            Assert.That(opened, Is.Not.Null);
            Assert.That(closed, Is.Not.Null);
            Assert.That(typeof(KeePass.Forms.FileOpenedEventArgs).GetProperty("Database"), Is.Not.Null);
            Assert.That(typeof(KeePass.Forms.FileClosedEventArgs).GetProperty("IOConnectionInfo"), Is.Not.Null);
        }

        [Test]
        public void A_lock_is_still_told_apart_from_a_close_by_a_flag()
        {
            // The whole difference between a database the gate keeps answering for and one it
            // forgets. KeePass raises the same event for both and only this says which.
            var flags = typeof(KeePass.Forms.FileClosedEventArgs).GetProperty("Flags");

            Assert.That(flags, Is.Not.Null);
            Assert.That(Enum.IsDefined(flags.PropertyType, "Locking"), Is.True,
                "without this the plugin cannot tell a lock from a close and would drop the responder.");
        }

        [Test]
        public void A_closing_file_still_says_which_file_it_was()
        {
            // The path is what identifies a database across a lock, because `PwDatabase.Close`
            // clears the object it belonged to. This is the surface the plugin actually reads it
            // from: the event argument, not `PwDocument`, which nothing here touches.
            var closing = typeof(FileClosedEventArgs).GetProperty("IOConnectionInfo");
            var flags = typeof(FileClosedEventArgs).GetProperty("Flags");

            Assert.That(closing, Is.Not.Null);
            Assert.That(closing.PropertyType, Is.EqualTo(typeof(KeePassLib.Serialization.IOConnectionInfo)));
            Assert.That(flags, Is.Not.Null, "a lock and a close arrive on the same event and differ by this");
            Assert.That(flags.PropertyType, Is.EqualTo(typeof(FileEventFlags)));
        }

        [Test]
        public void The_KeePass_under_test_is_the_release_this_repository_pins()
        {
            // The name promised more than the assertion under it, which asked only for a major
            // version of 2 and was true of every KeePass 2.x. The release `keepass.version` names
            // is written into this assembly by its project, so a build pointed at another KeePass
            // with `KeePassDevDir` is told so here rather than measuring that one quietly.
            var version = KeePassAssembly.GetName().Version;
            var pinned = PinnedRelease();

            TestContext.Out.WriteLine("KeePass " + version + " at " + KeePassAssembly.Location);
            Assert.That(version, Is.EqualTo(pinned),
                "these tests ran against KeePass " + version + ", and this repository pins " + pinned);
        }

        /// <summary>The release `keepass.version` names, as the four numbers an assembly carries.</summary>
        private static Version PinnedRelease()
        {
            foreach (AssemblyMetadataAttribute recorded in typeof(KeePassContractTests).Assembly
                         .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                Version pinned;
                if (recorded.Key == "KeePassPinnedVersion" && Version.TryParse(recorded.Value, out pinned))
                {
                    return new Version(
                        pinned.Major, pinned.Minor, Math.Max(pinned.Build, 0), Math.Max(pinned.Revision, 0));
                }
            }

            Assert.Fail("this build did not record which KeePass release the repository pins");
            return null;
        }
    }
}
