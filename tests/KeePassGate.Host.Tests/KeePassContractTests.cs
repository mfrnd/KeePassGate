using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassGate.Ui;
using KeePassLib.Collections;
using KeePassLib.Security;
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
            // dictionary holds which group each of its items means; the first item names no group
            // and the dictionary holds it as zero, which is what OK sets `RecycleBinUuid` back to
            // on a database that had no bin when the dialog opened. `RecycleBinDialogTests` drives
            // that OK. A KeePass that drops either of these is one where the refusal is guarding
            // nothing, which is worth being told about rather than carrying.
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
        public void The_attachment_editor_still_names_the_attachment_it_is_open_on()
        {
            var type = KeePassAssembly.GetType(KeePassContract.DataEditorFormTypeName, false);

            Assert.That(type, Is.Not.Null,
                KeePassContract.DataEditorFormTypeName + " is gone, so the attachment it writes back "
                + "is never recognized and a write to its entry is undone when it closes.");
            Assert.That(typeof(Form).IsAssignableFrom(type), Is.True);

            var field = type.GetField(
                KeePassContract.DataEditorFormNameField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "Without this, no entry is written while the editor is open, which is safe and useless.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(string)));

            // Set by the call that opens the editor, before it loads, which is when the watch
            // reads it.
            using (var form = new DataEditorForm())
            {
                form.InitEx("note.txt", new byte[] { 1 });
                Assert.That(field.GetValue(form), Is.EqualTo("note.txt"));
            }
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

        [Test]
        public void A_string_still_cannot_be_changed_once_it_is_made()
        {
            // What a read's view rests on: it keeps the objects an entry's strings were held in
            // when it was taken and reads them once the hold of KeePass's thread has ended, so a
            // value read then is the value the entry held inside the hold only while nothing
            // changes such an object in place. What this pins is the public surface: every
            // public instance member reads or hands back another string, and no public
            // property can be set. A KeePass that added a way to change a string in place would
            // let an operator's edit into an answer the rules were asked about before it.
            var type = typeof(ProtectedString);

            // cspell:ignore Xorred
            var readers = new[]
            {
                "get_IsProtected", "get_IsEmpty", "get_Length", "ReadString", "ReadChars", "ReadUtf8",
                "ReadXorredString", "Equals", "GetHashCode", "ToString",
            };
            var makers = new[] { "WithProtection", "Insert", "Remove", "Trim" };

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (makers.Contains(method.Name))
                {
                    Assert.That(method.ReturnType, Is.EqualTo(type),
                        "ProtectedString." + method.Name + " no longer hands back another string");
                    continue;
                }

                Assert.That(readers, Does.Contain(method.Name),
                    "ProtectedString has a public " + method.Name + " that may change it in place");
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(property.CanWrite, Is.False, "ProtectedString." + property.Name + " can be set");
            }

            // And what those members do to the string they are asked of, which is nothing.
            var kept = new ProtectedString(true, System.Text.Encoding.UTF8.GetBytes("  as it was made  "));
            Assert.That(kept.Insert(2, "not ").ReadString(), Is.EqualTo("  not as it was made  "));
            Assert.That(kept.Remove(0, 5).ReadString(), Is.EqualTo("it was made  "));
            Assert.That(kept.Trim().ReadString(), Is.EqualTo("as it was made"));
            Assert.That(kept.WithProtection(false).IsProtected, Is.False);
            Assert.That(kept.ReadString(), Is.EqualTo("  as it was made  "));
            Assert.That(kept.IsProtected, Is.True);

            // A value set under a name an entry already holds is another object in the
            // dictionary, and the object that was there is left as it was.
            var strings = new ProtectedStringDictionary();
            strings.Set("Password", kept);
            strings.Set("Password", new ProtectedString(true, "typed over it"));
            Assert.That(strings.Get("Password"), Is.Not.SameAs(kept));
            Assert.That(kept.ReadString(), Is.EqualTo("  as it was made  "));
        }

        [Test]
        public void Reading_a_protected_string_as_text_still_ends_its_protection_and_copying_its_bytes_does_not()
        {
            // Why the gate reads a protected value with `ReadUtf8` and hands KeePass a value to
            // store as bytes, `StoredStrings`. A protected string made from bytes is held
            // encrypted; asking its length or copying its bytes out leaves it so; `ReadString`
            // keeps the decoded text inside the object, where it stays; and a string made from
            // text keeps that text whatever its flag. `ProtectedInMemoryTests` asks the same
            // field what the gate left behind, so the field has to be there and mean this.
            var loaded = new ProtectedString(true, System.Text.Encoding.UTF8.GetBytes("not-a-real-password"));
            Assert.That(KeePassMemory.KeepsText(loaded), Is.False, "made from bytes");

            Assert.That(loaded.Length, Is.EqualTo("not-a-real-password".Length));
            Assert.That(KeePassMemory.KeepsText(loaded), Is.False, "measured");

            Assert.That(System.Text.Encoding.UTF8.GetString(loaded.ReadUtf8()), Is.EqualTo("not-a-real-password"));
            Assert.That(KeePassMemory.KeepsText(loaded), Is.False, "copied out as bytes");

            Assert.That(loaded.Equals(new ProtectedString(true, "not-a-real-password"), false), Is.True);
            Assert.That(KeePassMemory.KeepsText(loaded), Is.False, "compared");

            Assert.That(loaded.ReadString(), Is.EqualTo("not-a-real-password"));
            Assert.That(KeePassMemory.KeepsText(loaded), Is.True, "read as text");

            Assert.That(KeePassMemory.KeepsText(new ProtectedString(true, "not-a-real-password")), Is.True,
                "made from text, protected");
            Assert.That(
                KeePassMemory.KeepsText(
                    new ProtectedString(false, System.Text.Encoding.UTF8.GetBytes("an invented name"))),
                Is.True,
                "made from bytes, not protected");

            // The bytes a protected string is made from are copied, so the caller's can be
            // overwritten once the string is made.
            var bytes = System.Text.Encoding.UTF8.GetBytes("not-a-real-password");
            var made = new ProtectedString(true, bytes);
            Array.Clear(bytes, 0, bytes.Length);
            Assert.That(System.Text.Encoding.UTF8.GetString(made.ReadUtf8()), Is.EqualTo("not-a-real-password"));
        }

        [Test]
        public void A_string_and_an_attachment_can_still_be_read_from_two_threads_at_once()
        {
            // A read asks its view for values on the connection's thread while KeePass's own
            // thread is free to read the same objects: the entry list drawing a title, an
            // operator opening the entry. `ProtectedBinary.ReadData` copies under a lock of its
            // own and `ProtectedString.ReadString` keeps the text it decoded before it lets the
            // bytes go, read off 2.61.1. A race proves nothing by passing, and this fails,
            // sooner or later, on a KeePass that lost either property.
            const int Rounds = 12;
            var expected = new string('k', 150000);

            for (var round = 0; round < Rounds; round++)
            {
                var text = new ProtectedString(true, System.Text.Encoding.UTF8.GetBytes(expected));
                var bytes = new ProtectedBinary(true, System.Text.Encoding.UTF8.GetBytes(expected));

                string elsewhere = null;
                var length = -1;
                byte[] copied = null;

                using (var together = new Barrier(2))
                {
                    var other = System.Threading.Tasks.Task.Run(() =>
                    {
                        together.SignalAndWait();
                        length = text.Length;
                        elsewhere = text.ReadString();
                        copied = bytes.ReadData();
                    });

                    together.SignalAndWait();
                    var here = text.ReadString();
                    var measured = text.Length;
                    var read = bytes.ReadData();

                    // Whatever the other thread threw is thrown here.
                    other.GetAwaiter().GetResult();

                    Assert.That(here, Is.EqualTo(expected));
                    Assert.That(measured, Is.EqualTo(expected.Length));
                    Assert.That(System.Text.Encoding.UTF8.GetString(read), Is.EqualTo(expected));
                }

                Assert.That(elsewhere, Is.EqualTo(expected));
                Assert.That(length, Is.EqualTo(expected.Length));
                Assert.That(System.Text.Encoding.UTF8.GetString(copied), Is.EqualTo(expected));
            }
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
            // keeps, which is what a save, an import and a synchronize raise: the save is held to
            // it here, off the IL, and the other two in the next test. Asked of the shape rather
            // than driven: blocking the window reaches `Enabled` on a main window that a test
            // cannot build, and the count is only ever written by that one method.
            var method = typeof(MainForm).GetMethod(
                KeePassContract.MainFormInteractionBlockedMethod, Type.EmptyTypes);
            var block = typeof(MainForm).GetMethod("UIBlockInteraction", new[] { typeof(bool) });
            var save = typeof(MainForm).GetMethod(
                KeePassContract.MainFormSaveDatabaseMethod, new[] { typeof(KeePassLib.PwDatabase), typeof(object) });
            var saveAs = Array.Find(typeof(MainForm).GetMethods(), candidate => candidate.Name == "SaveDatabaseAs");

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(block, Is.Not.Null,
                "what raises the answer is gone, so a save may no longer be what makes it true");
            Assert.That(save, Is.Not.Null, "the save the plugin names a database to is gone");
            Assert.That(saveAs, Is.Not.Null, "the save under another name is gone");
            Assert.That(Calls(save, block), Is.True,
                "a save no longer blocks the window, so a write can land inside KeePass's own save");
            Assert.That(Calls(saveAs, block), Is.True, "a save under another name no longer blocks the window");
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

        [Test]
        public void KeePass_still_drops_a_lock_asked_for_while_its_interaction_is_blocked()
        {
            // What `DroppedLocks` exists for: every save blocks it, the plugin's among them.
            var locking = typeof(MainForm).GetMethod(nameof(MainForm.LockAllDocuments), Type.EmptyTypes);

            Assert.That(locking, Is.Not.Null);
            Assert.That(Calls(locking, typeof(MainForm).GetMethod(nameof(MainForm.UIIsInteractionBlocked))), Is.True,
                "KeePass's lock no longer asks whether its interaction is blocked, so a lock during a save may "
                + "no longer be dropped, and DroppedLocks may be locking twice");
        }

        [Test]
        public void KeePass_still_locks_on_a_session_event_by_the_three_settings_the_replay_reads()
        {
            // `MainWindowLocking.Asks` follows this handler reason by reason, and the replay locks
            // through what it locks through.
            var handler = typeof(MainForm).GetMethod(
                "OnSessionLock",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(object), typeof(KeePass.Util.SessionLockEventArgs) },
                null);
            var settings = typeof(KeePass.App.Configuration.AceWorkspaceLocking);

            Assert.That(handler, Is.Not.Null, "KeePass's handler of the session events is gone");
            foreach (var setting in new[]
            {
                nameof(KeePass.App.Configuration.AceWorkspaceLocking.LockOnSessionSwitch),
                nameof(KeePass.App.Configuration.AceWorkspaceLocking.LockOnSuspend),
                nameof(KeePass.App.Configuration.AceWorkspaceLocking.LockOnRemoteControlChange),
            })
            {
                Assert.That(Calls(handler, settings.GetProperty(setting).GetGetMethod()), Is.True,
                    "KeePass's handler no longer reads " + setting);
            }

            Assert.That(Calls(handler, typeof(MainForm).GetMethod(nameof(MainForm.IsAtLeastOneFileOpen))), Is.True);
            Assert.That(Calls(handler, typeof(MainForm).GetMethod(nameof(MainForm.LockAllDocuments), Type.EmptyTypes)),
                Is.True);
        }

        /// <summary>Whether a method's body calls, or constructs through, the other one.</summary>
        /// <remarks>
        /// The token after every call, virtual call and constructor call opcode in the body is
        /// resolved against its module.
        /// The scan is byte by byte rather than instruction by instruction, so an operand that
        /// happens to hold one of those opcodes resolves to something else or to nothing, and can
        /// only ever fail to match rather than match wrongly.
        /// </remarks>
        internal static bool Calls(MethodBase caller, MethodBase callee)
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
            // signal a write has that an export is walking the live tree. That it raises it is read
            // off the IL, since exporting needs the main window, and that raising it moves the
            // answer a write asks for is driven.
            var property = typeof(KeePassLib.Utility.MessageService).GetProperty(
                KeePassContract.MessageServiceCountProperty, BindingFlags.Public | BindingFlags.Static);
            var export = typeof(MainForm).GetMethod(
                nameof(MainForm.PerformExport), new[] { typeof(KeePassLib.PwGroup), typeof(bool) });

            Assert.That(property, Is.Not.Null);
            Assert.That(export, Is.Not.Null, "the export File, Export runs is gone");
            Assert.That(
                Calls(export, typeof(KeePassLib.Utility.MessageService).GetMethod(
                    nameof(KeePassLib.Utility.MessageService.ExternalIncrementMessageCount))),
                Is.True,
                "an export no longer raises the count, so a write can land while it walks the tree");
            Assert.That(
                Calls(export, typeof(KeePassLib.Utility.MessageService).GetMethod(
                    nameof(KeePassLib.Utility.MessageService.ExternalDecrementMessageCount))),
                Is.True,
                "nor lowers it again");

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
        public void Xml_replace_still_merges_under_a_progress_window_that_announces_itself()
        {
            // The one operation a write is kept out of by a window alone: XML Replace raises
            // neither busy signal, and `GroupDialogs.Working` sees it only because the progress
            // window it merges under registers itself with the window manager as it loads. Both
            // host fixtures that drive that window register it by hand, since it is never shown
            // there, so the two links on KeePass's side are read off the IL here.
            var progress = typeof(StatusProgressForm);
            var xml = typeof(KeePass.Util.XmlUtil);
            var all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var load = progress.GetMethod(
                "OnFormLoad", all, null, new[] { typeof(object), typeof(EventArgs) }, null);
            var construct = progress.GetMethod(nameof(StatusProgressForm.ConstructEx));
            var replace = xml.GetMethod(nameof(KeePass.Util.XmlUtil.Replace));
            var perform = xml.GetMethod("PerformXmlReplace", all);
            var merge = typeof(KeePassLib.PwDatabase).GetMethod(
                nameof(KeePassLib.PwDatabase.MergeIn),
                new[]
                {
                    typeof(KeePassLib.PwDatabase), typeof(KeePassLib.PwMergeMethod),
                    typeof(KeePassLib.Interfaces.IStatusLogger),
                });

            Assert.That(progress.FullName, Is.EqualTo(KeePassContract.StatusProgressFormTypeName));
            Assert.That(load, Is.Not.Null, "the progress window's load handler is gone");
            Assert.That(Calls(load, typeof(GlobalWindowManager).GetMethod("AddWindow", new[] { typeof(Form) })),
                Is.True,
                "the progress window no longer registers itself, so the dialog watch cannot see it");
            Assert.That(Calls(construct, typeof(UIUtil).GetMethod(
                    "Show", all, null, new[] { typeof(Form), typeof(IWin32Window) }, null)),
                Is.True,
                "the progress window is no longer shown as it is made, so it never loads");
            Assert.That(Calls(replace, construct), Is.True, "XML Replace no longer runs under the progress window");
            Assert.That(Calls(replace, perform), Is.True, "XML Replace no longer replaces in PerformXmlReplace");
            Assert.That(Calls(perform, merge), Is.True, "XML Replace no longer merges into the live database");
        }

        [Test]
        public void Every_merge_KeePass_runs_still_goes_through_the_method_a_read_looks_for()
        {
            // A read is kept out of a merge by finding `PwDatabase.MergeIn` on the stack of
            // KeePass's thread, `MergeWatch`, so a merge that ran somewhere else would be one no
            // read waits for. The overload without a logger is a few bytes that may be compiled
            // into whatever calls it, leaving no frame of its own, which costs nothing while it
            // calls the long one. That the long one keeps a frame is driven, in
            // `ReadsDuringAMergeTests`; who reaches it is read off the IL here, XML Replace in the
            // test above.
            var database = typeof(KeePassLib.PwDatabase);
            var quiet = database.GetMethod(
                KeePassContract.DatabaseMergeMethod,
                new[] { typeof(KeePassLib.PwDatabase), typeof(KeePassLib.PwMergeMethod) });
            var merge = database.GetMethod(
                KeePassContract.DatabaseMergeMethod,
                new[]
                {
                    typeof(KeePassLib.PwDatabase), typeof(KeePassLib.PwMergeMethod),
                    typeof(KeePassLib.Interfaces.IStatusLogger),
                });
            var import = Array.Find(
                typeof(KeePass.DataExchange.ImportUtil).GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
                method => method.Name == "Import" && method.GetParameters().Length == 9);

            Assert.That(merge, Is.Not.Null, "the merge every other one ends in is gone");
            Assert.That(quiet, Is.Not.Null, "the merge without a logger is gone");
            Assert.That(Calls(quiet, merge), Is.True,
                "the merge without a logger no longer runs inside the one a read looks for");
            Assert.That(import, Is.Not.Null, "the import every synchronize and import goes through is gone");
            Assert.That(Calls(import, merge), Is.True,
                "an import or a synchronize no longer merges inside the method a read looks for");
            Assert.That(
                Array.FindAll(
                    database.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                    method => method.Name == KeePassContract.DatabaseMergeMethod),
                Has.Length.EqualTo(2),
                "a third way into a merge, which may not end in the one a read looks for");
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
        public void A_dictionary_still_finds_a_key_only_in_the_culture_it_was_sorted_in()
        {
            // Why `GateListener` serves every connection in KeePass's culture. Custom data, an
            // entry's strings and its attachments are each sorted under the default comparer,
            // which collates in the culture of the thread asking, so what KeePass's thread put in
            // under its translation's culture is searched on a pool thread in the regional
            // format's order. Danish sorts `aa` after `z` and English sorts it before `b`: three
            // keys put in in Danish order leave the `aa` one to the right of the tree's root, and
            // English goes left to look for it. Listing finds it either way, since a walk compares
            // nothing. A KeePass whose dictionaries compared byte for byte would fail this, and the
            // culture the listener carries would then be protecting nothing.
            var danish = CultureInfo.GetCultureInfo("da-DK");
            var english = CultureInfo.GetCultureInfo("en-US");
            var thread = Thread.CurrentThread;
            var own = thread.CurrentCulture;

            try
            {
                thread.CurrentCulture = danish;
                var data = new StringDictionaryEx();
                var strings = new ProtectedStringDictionary();
                var binaries = new ProtectedBinaryDictionary();

                foreach (var key in new[] { "KPG.Client.b0", "KPG.Client.c0", "KPG.Client.aa0" })
                {
                    data.Set(key, "a record");
                }

                foreach (var name in new[] { "Bank", "Code", "Aa note" })
                {
                    strings.Set(name, new ProtectedString(false, "a value"));
                    binaries.Set(name, new ProtectedBinary(false, new byte[] { 1 }));
                }

                thread.CurrentCulture = english;
                Assert.That(data.Get("KPG.Client.aa0"), Is.Null, "custom data is looked up in the asker's culture");
                Assert.That(strings.Get("Aa note"), Is.Null, "and so are an entry's strings");
                Assert.That(binaries.Get("Aa note"), Is.Null, "and its attachments");
                Assert.That(data.Select(pair => pair.Key), Does.Contain("KPG.Client.aa0"),
                    "while a listing still finds the key the lookup missed");

                thread.CurrentCulture = danish;
                Assert.That(data.Get("KPG.Client.aa0"), Is.EqualTo("a record"),
                    "and the culture the tree was sorted in finds it");
                Assert.That(strings.Get("Aa note"), Is.Not.Null);
                Assert.That(binaries.Get("Aa note"), Is.Not.Null);
            }
            finally
            {
                thread.CurrentCulture = own;
            }
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
