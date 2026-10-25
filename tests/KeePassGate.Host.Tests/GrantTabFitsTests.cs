using System;
using System.Globalization;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassGate.Testing;
using KeePassGate;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The grant editor has to fit the room the group and entry dialogs give it, which is less
    /// than the database settings dialog gives the other tab.
    /// </summary>
    /// <remarks>
    /// **Measured, not carried over.** `GateTabFitsTests` builds the real settings dialog and asks
    /// it how big its tab page is, and at the size this runs at the answer is 449 by 314. That
    /// figure ended up quoted in comments about *this* tab as though it applied here. It
    /// does not: this tab is on the group and entry dialogs, and the group dialog's page is a good
    /// deal shorter. One design decision had already been justified in those numbers, so the number
    /// was doing work on a page nobody had ever measured.
    ///
    /// The absolute figures move with the display and the font, which is the other half of why they
    /// are asked for rather than written down: two machines measure the same dialog differently and
    /// both are right.
    /// </remarks>
    [TestFixture]
    public sealed class GrantTabFitsTests
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
        public void The_group_dialog_gives_this_tab_less_room_than_the_settings_dialog_gives_the_other()
        {
            // Printed as well as asserted, because the numbers are what somebody reasoning about
            // the layout needs and they are not the same on two machines.
            using (var settings = KeePassDialogs.NewDatabaseSettingsForm())
            using (var group = NewGroupForm())
            {
                var wide = PageOf(settings, KeePassContract.DatabaseSettingsTabControlName);
                var narrow = PageOf(group, KeePassContract.NodeFormTabControlName);

                TestContext.Out.WriteLine("database settings page: " + wide);
                TestContext.Out.WriteLine("group dialog page:      " + narrow);

                Assert.That(narrow.Height, Is.LessThan(wide.Height),
                    "the editor is on the shorter of the two, so its room is the one to measure");
            }
        }

        [Test]
        public void Everything_on_the_grant_tab_fits_the_group_dialog()
        {
            AssertFits(NewGroupForm, KeePassContract.NodeFormTabControlName, GrantTab.GroupScope, 1.00f);
        }

        [Test]
        public void Everything_on_the_grant_tab_fits_the_entry_dialog()
        {
            AssertFits(NewEntryForm, KeePassContract.NodeFormTabControlName, GrantTab.EntryScope, 1.00f);
        }

        [Test]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void Everything_on_the_grant_tab_fits_a_scaled_display(float scaling)
        {
            // The dialog is built at the larger font, which is what a scaled display hands it:
            // both of these are `AutoScaleMode.Font`, so the room grows with the text rather than
            // staying behind it. What this catches is a control of ours sized in pixels, which
            // would then be the one thing that did not grow.
            AssertFits(NewGroupForm, KeePassContract.NodeFormTabControlName, GrantTab.GroupScope, scaling);
            AssertFits(NewEntryForm, KeePassContract.NodeFormTabControlName, GrantTab.EntryScope, scaling);
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void The_table_keeps_its_heading_and_a_row_with_visual_styles_on(float scaling)
        {
            // The tests above measure the tab as this process draws it, which is without visual
            // styles. KeePass turns them on as it starts, and under them a column heading is three
            // pixels taller, which is three more pixels the warning over the table has to leave.
            foreach (var fit in Themed(scaling))
            {
                TestContext.Out.WriteLine(fit);
                AssertKeepsARow(fit);
            }
        }

        private void AssertFits(Func<Form> newDialog, string tabControlName, string scope, float scaling)
        {
            foreach (var stored in States)
            {
                using (var dialog = newDialog())
                using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                using (var panel = NewPanel(stored, Above(_database), scope))
                {
                    SizeF scale;
                    var area = LayOut(dialog, tabControlName, font, panel, out scale);

                    Assert.That(panel.Padding.Left, Is.EqualTo((int)Math.Round(Spacing.Gap * scale.Width)),
                        "the grant tab kept an unscaled inset at " +
                        scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text");

                    foreach (Control child in Everything(panel))
                    {
                        // Every edge, because a layout that runs off the page runs off the side it
                        // grows from, and under a right-to-left dialog that is the left.
                        Assert.That(child.Left, Is.GreaterThanOrEqualTo(0),
                            Describe(child, stored) + " runs past the left of the page at " +
                            scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text");
                        Assert.That(child.Top, Is.GreaterThanOrEqualTo(0),
                            Describe(child, stored) + " runs past the top of the page at " +
                            scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text");
                        Assert.That(child.Bottom, Is.LessThanOrEqualTo(area.Height),
                            Describe(child, stored) + " runs past the bottom of the page at " +
                            scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text");
                        Assert.That(child.Right, Is.LessThanOrEqualTo(area.Width),
                            Describe(child, stored) + " runs past the right of the page at " +
                            scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text");
                    }

                    AssertKeepsARow(GridFit.Of(panel, dialog, stored, scaling));
                }
            }
        }

        /// <summary>
        /// Every state the tab has a different height in: no rules, a rule, and a document that
        /// will not read.
        /// </summary>
        /// <remarks>
        /// The unreadable one is the tallest, because it adds a warning and a button under it, and
        /// it is also the one an operator meets when something is already wrong.
        /// </remarks>
        internal static readonly string[] States = { null, Rules.Granting(("reader", "RL")), "not a document" };

        /// <summary>
        /// Whether the table keeps its heading and one row, which staying inside the page does not
        /// ask: a table squeezed to its heading still fits.
        /// </summary>
        /// <remarks>
        /// **The warning over the table is what squeezes it.** At four lines it left the group
        /// dialog's table 24 pixels at 100 percent, where the heading and one row need 40: the one
        /// state in which nothing on the table can be changed kept no row to show what is there.
        /// </remarks>
        private static void AssertKeepsARow(GridFit fit)
        {
            Assert.That(fit.Height, Is.GreaterThanOrEqualTo(fit.Heading + fit.Row),
                fit + ", so it shows its heading and no row.");
        }

        /// <summary>The same measurement with visual styles on, in a domain made for it.</summary>
        /// <remarks>
        /// **In a domain of its own**, for the reason `ChooserListsFitTests` gives: visual styles
        /// are turned on for a whole domain and cannot be turned off again, so turning them on here
        /// would change what every later test in this process measures.
        /// </remarks>
        private static GridFit[] Themed(float scaling)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
            };

            var domain = AppDomain.CreateDomain("themed grant tab", null, setup);
            try
            {
                var themed = (ThemedGrantTab)domain.CreateInstanceAndUnwrap(
                    typeof(ThemedGrantTab).Assembly.FullName, typeof(ThemedGrantTab).FullName);

                return themed.Measure(scaling);
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }

        /// <summary>
        /// The panel as a dialog built at this font shows it: in the room the dialog's tab strip
        /// leaves the page, scaled as the page is when it is attached.
        /// </summary>
        internal static Size LayOut(Form dialog, string tabControlName, Font font, Control panel, out SizeF scale)
        {
            // The dialog first, so the page it reports is the room a scaled display
            // would give it rather than the room this one does.
            dialog.Font = font;

            var area = PageOf(dialog, tabControlName);

            // Scaled as `TabPageInjector` scales the page it attaches, by as much as
            // KeePass scaled the dialog, because that is the tab a scaled display shows.
            scale = Spacing.ScaleOf(dialog);
            if (scale != Spacing.AsDesigned)
            {
                panel.Scale(scale);
            }

            panel.Font = font;
            panel.Size = area;
            panel.CreateControl();
            panel.PerformLayout();

            return area;
        }

        /// <summary>The tab for one stored state, under a group granting something.</summary>
        internal static GrantPanel NewPanel(string stored, IAclNode above, string scope)
        {
            return new GrantPanel(new GrantSheet(stored, above, new[] { "reader", "editor" }, null), null, scope, null);
        }

        /// <summary>A group above granting something, so that the table has an inherited row too.</summary>
        internal static IAclNode Above(TemporaryDatabase database)
        {
            var group = new PwGroup(true, true, "A group above", PwIcon.Folder);
            database.RootGroup.AddGroup(group, true);
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("editor", "R")));

            return KdbxNodes.Of(group);
        }

        private Form NewGroupForm()
        {
            return NewGroupForm(_database);
        }

        internal static Form NewGroupForm(TemporaryDatabase database)
        {
            var dialog = new GroupForm();
            dialog.InitEx(database.RootGroup, false, null, database.Database);
            return dialog;
        }

        private Form NewEntryForm()
        {
            return NewEntryForm(_database);
        }

        internal static Form NewEntryForm(TemporaryDatabase database)
        {
            var entry = new PwEntry(true, true);
            database.RootGroup.AddEntry(entry, true);

            var dialog = new PwEntryForm();
            dialog.InitEx(entry, PwEditMode.EditExistingEntry, database.Database, new ImageList(), false, false);
            return dialog;
        }

        /// <summary>
        /// The room a page has on this dialog once the tab is on it, icon and all, which is what
        /// makes every tab on these dialogs a little taller.
        /// </summary>
        private static Size PageOf(Form dialog, string tabControlName)
        {
            return TabRoom.Of(dialog, tabControlName);
        }

        private static IEnumerable<Control> Everything(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (!child.Visible)
                {
                    continue;
                }

                yield return child;

                foreach (var inside in Everything(child))
                {
                    yield return inside;
                }
            }
        }

        private static string Describe(Control control, string stored)
        {
            return (string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name) + " (" + StateOf(stored) + ")";
        }

        internal static string StateOf(string stored)
        {
            return stored == null ? "empty" : stored.StartsWith("{", StringComparison.Ordinal) ? "with a rule" : "unreadable";
        }
    }

    /// <summary>How tall the grant table was laid out, and what its heading and one row need.</summary>
    [Serializable]
    internal sealed class GridFit
    {
        public string Where { get; set; }

        public int Height { get; set; }

        public int Heading { get; set; }

        public int Row { get; set; }

        /// <summary>What the table on a panel that has been laid out came to.</summary>
        public static GridFit Of(Control panel, Form dialog, string stored, float scaling)
        {
            var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
            Assert.That(grid, Is.Not.Null, "the grant tab has no table on it");

            return new GridFit
            {
                Where = "The table on the " + (dialog is GroupForm ? "group" : "entry") + " dialog, "
                        + GrantTabFitsTests.StateOf(stored) + ", at "
                        + scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text,",
                Height = grid.Height,
                Heading = grid.ColumnHeadersHeight,
                Row = grid.RowTemplate.Height
            };
        }

        public override string ToString()
        {
            return Where + " is " + Height.ToString(CultureInfo.InvariantCulture) + " high where its heading and one row need "
                   + Heading.ToString(CultureInfo.InvariantCulture) + " and "
                   + Row.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The grant tab laid out with visual styles on, in whichever domain this is created in. Only
    /// ever created in one made for it: see <see cref="GrantTabFitsTests"/>.
    /// </summary>
    internal sealed class ThemedGrantTab : MarshalByRefObject
    {
        public GridFit[] Measure(float scaling)
        {
            Application.EnableVisualStyles();

            var fits = new List<GridFit>();
            using (var database = TemporaryDatabase.New())
            {
                Measure(() => GrantTabFitsTests.NewGroupForm(database), GrantTab.GroupScope, database, scaling, fits);
                Measure(() => GrantTabFitsTests.NewEntryForm(database), GrantTab.EntryScope, database, scaling, fits);
            }

            return fits.ToArray();
        }

        private static void Measure(
            Func<Form> newDialog, string scope, TemporaryDatabase database, float scaling, List<GridFit> fits)
        {
            foreach (var stored in GrantTabFitsTests.States)
            {
                using (var dialog = newDialog())
                using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                using (var panel = GrantTabFitsTests.NewPanel(stored, GrantTabFitsTests.Above(database), scope))
                {
                    GrantTabFitsTests.LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out _);
                    fits.Add(GridFit.Of(panel, dialog, stored, scaling));
                }
            }
        }
    }
}
