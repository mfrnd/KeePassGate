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
        public void The_group_dialog_still_has_the_name_pairing_watches_for()
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
                "Without this, pairing refuses while any group dialog is open, which is safe and useless.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(KeePassLib.PwGroup)));
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

        [TestCase(KeePassContract.GroupFormTypeName)]
        [TestCase(KeePassContract.EntryFormTypeName)]
        public void Both_node_dialogs_still_keep_their_own_copy_of_custom_data(string typeName)
        {
            // The field the grant editor writes into, and the reason it can. TODO(step 13): the
            // editor arrives with the grant table. Both dialogs copy a
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
            // which profile list to offer. TODO(step 13): `NodeDialog` arrives with the grant tab.
            // A KeePass that renames it leaves the editor with no
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
