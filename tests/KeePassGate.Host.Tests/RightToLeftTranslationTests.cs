using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own right-to-left translation does to the pages this plugin adds: nothing.
    /// </summary>
    /// <remarks>
    /// **KeePass mirrors a dialog whose language reads right to left**, and does it in the dialog's
    /// constructor, before this plugin's page exists. The page is built left to right whatever the
    /// dialog is, because what it draws is this plugin's own untranslated English and the names it
    /// keeps, and `TabPageInjector.BuildPage` says what inheriting the mirror did to those. So each
    /// dialog is built twice, under a right-to-left translation and without one, and every control
    /// on the page has to lay out the same in both: the flow and table panels are where mirroring
    /// would show, since they place their contents from the side the text starts on.
    ///
    /// **The translation is applied by hand**, because `GlobalWindowManager.InitializeForm`, which
    /// is what applies it inside KeePass, returns early while `Program.DesignMode` is true, and it is
    /// true outside KeePass's own `Main`.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class RightToLeftTranslationTests
    {
        private TemporaryDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void The_settings_dialog_page_lays_out_as_it_does_left_to_right()
        {
            AssertLaidOutAlike(
                () =>
                {
                    var dialog = KeePassDialogs.NewDatabaseSettingsForm();
                    KeePassDialogs.InitializeFor(dialog, _database.Database);
                    return dialog;
                },
                () => new TabPageInjector(
                    new AuthorityPanelFactory(
                        new DialogDatabases(NullGateLog.Instance), new NeverAsks(), NullGateLog.Instance, null),
                    NullGateLog.Instance),
                KeePassContract.DatabaseSettingsTabControlName,
                GateTab.PageControlName);
        }

        [Test]
        public void The_group_dialog_page_lays_out_as_it_does_left_to_right()
        {
            AssertLaidOutAlike(
                () =>
                {
                    var dialog = new GroupForm();
                    dialog.InitEx(_database.RootGroup, false, null, _database.Database);
                    return dialog;
                },
                NodeInjector,
                KeePassContract.NodeFormTabControlName,
                GrantTab.PageControlName);
        }

        [Test]
        public void The_entry_dialog_page_lays_out_as_it_does_left_to_right()
        {
            using (var icons = new ImageList())
            {
                AssertLaidOutAlike(
                    () =>
                    {
                        var entry = new PwEntry(true, true);
                        _database.RootGroup.AddEntry(entry, true);

                        var dialog = new PwEntryForm();
                        dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, icons, false, false);
                        return dialog;
                    },
                    NodeInjector,
                    KeePassContract.NodeFormTabControlName,
                    GrantTab.PageControlName);
            }
        }

        private static TabPageInjector NodeInjector()
        {
            return new TabPageInjector(
                new GrantPanelFactory(new NodeDialogs(null), null),
                null,
                KeePassContract.NodeFormTabControlName,
                GrantTab.Text,
                GrantTab.PageControlName);
        }

        private static void AssertLaidOutAlike(
            Func<Form> dialog, Func<TabPageInjector> injector, string tabControlName, string pageName)
        {
            var mirrored = LaidOut(dialog, injector, tabControlName, pageName, true);
            var plain = LaidOut(dialog, injector, tabControlName, pageName, false);

            Assert.That(mirrored.Keys, Is.EquivalentTo(plain.Keys), "the page holds different controls");
            foreach (var control in plain)
            {
                Assert.That(mirrored[control.Key], Is.EqualTo(control.Value),
                    control.Key + " moved under a right-to-left translation");
            }
        }

        /// <summary>
        /// Where every named control on the plugin's page sits, on a dialog built with or without
        /// a right-to-left translation.
        /// </summary>
        private static IDictionary<string, Rectangle> LaidOut(
            Func<Form> make, Func<TabPageInjector> injector, string tabControlName, string pageName, bool rightToLeft)
        {
            var translation = KeePass.Program.Translation;
            var was = translation.Properties.RightToLeft;
            translation.Properties.RightToLeft = rightToLeft;

            try
            {
                using (var dialog = make())
                {
                    translation.ApplyTo(dialog);
                    Assert.That(dialog.RightToLeft, Is.EqualTo(rightToLeft ? RightToLeft.Yes : RightToLeft.No),
                        "the translation did not reach the dialog, so nothing here is measured");

                    var handle = dialog.Handle;
                    Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero));

                    var attached = injector();
                    Assert.That(attached.Attach(dialog), Is.True);

                    try
                    {
                        var tabs = ControlTree.FindByName<TabControl>(dialog, tabControlName);
                        var page = ControlTree.FindByName<TabPage>(dialog, pageName);
                        Assert.That(page, Is.Not.Null, "no page called " + pageName);
                        Assert.That(page.RightToLeft, Is.EqualTo(RightToLeft.No), "the page is not left to right");

                        // Laid out at the size it is given on screen: a dialog that is never shown
                        // does not size a page added after its handle, and one left at a default
                        // size would be measured against room nobody has.
                        tabs.SelectedTab = page;
                        page.Bounds = tabs.DisplayRectangle;
                        page.PerformLayout();

                        return ControlTree.Descendants(page)
                            .Where(control => !string.IsNullOrEmpty(control.Name))
                            .GroupBy(control => control.Name)
                            .ToDictionary(named => named.Key, named => named.First().Bounds);
                    }
                    finally
                    {
                        attached.Detach(dialog);
                    }
                }
            }
            finally
            {
                translation.Properties.RightToLeft = was;
            }
        }

        /// <summary>Nothing on these pages asks the operator anything while it is being built.</summary>
        private sealed class NeverAsks : IOperatorPrompt
        {
            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
            }
        }
    }
}
