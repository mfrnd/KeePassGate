using System;
using System.Globalization;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
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
    /// it how big its tab page is; this tab is on the group and entry dialogs, and the group
    /// dialog's page is a good deal shorter, so its room is asked for here.
    ///
    /// **With the longest text every cell and line can hold at once**: every word the State column
    /// shows, masks of all eight letters, the longest reason the tab gives for taking no press, a
    /// name as long as a profile name gets, and more rows than fit, so that the table carries its
    /// vertical scroll bar. Nothing on the page may scroll sideways.
    ///
    /// The absolute figures move with the display and the font, which is the other half of why they
    /// are asked for rather than written down: two machines measure the same dialog differently and
    /// both are right.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GrantTabFitsTests
    {
        private TemporaryDatabase _database;
        private ImageList _icons;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            // The entry dialog refuses a null one, and what is in it is never looked at here.
            _icons = new ImageList();
        }

        [TearDown]
        public void TearDown()
        {
            _icons.Dispose();
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
            AssertFits(NewGroupForm, GrantTab.GroupScope, 1.00f);
        }

        [Test]
        public void Everything_on_the_grant_tab_fits_the_entry_dialog()
        {
            AssertFits(NewEntryForm, GrantTab.EntryScope, 1.00f);
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
            AssertFits(NewGroupForm, GrantTab.GroupScope, scaling);
            AssertFits(NewEntryForm, GrantTab.EntryScope, scaling);
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
            // pixels taller, which is three more pixels the reason over the table has to leave.
            foreach (var fit in Themed(scaling))
            {
                TestContext.Out.WriteLine(fit);
                AssertKeepsARow(fit);
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void Every_text_above_the_table_takes_at_most_two_lines(float scaling)
        {
            // The room above the table is the room the rows do not get, so a reason or a notice
            // standing there is held to two lines of the label as laid out in the dialog's room,
            // counted off the label rather than off a count of characters.
            foreach (var newDialog in new Func<Form>[] { NewGroupForm, NewEntryForm })
            {
                foreach (var text in AboveTheTable)
                {
                    using (var dialog = newDialog())
                    using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                    using (var panel = new GrantPanel(GrantSheet.Of(NodeRule.None, null, Names, text), GrantTab.GroupScope, null, null, null))
                    {
                        LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out _);

                        var label = ControlTree.FindByName<Label>(panel, GrantTab.WarningLabelName);
                        Assert.That(label.Text, Is.EqualTo(text), "the text stands over the table");
                        var lines = LinesOf(label);
                        TestContext.Out.WriteLine(lines + " " + (dialog is GroupForm ? "group" : "entry") + " "
                            + scaling.ToString("0.00", CultureInfo.InvariantCulture) + ": " + text);
                        Assert.That(lines, Is.LessThanOrEqualTo(2),
                            "\"" + text + "\" takes " + lines + " lines at " + scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text");
                    }
                }

                foreach (var scope in Scopes)
                {
                    using (var dialog = newDialog())
                    using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                    using (var panel = new GrantPanel(GrantSheet.Of(NodeRule.None, null, Names, null), scope, null, null, null))
                    {
                        LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out _);
                        var lines = LinesOf(ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName));
                        Assert.That(lines, Is.LessThanOrEqualTo(2), "\"" + scope + "\" takes " + lines + " lines");
                    }
                }
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void Where_the_database_names_no_profile_the_notice_stands_alone_and_the_table_takes_the_room_of_the_line_at_the_top(float scaling)
        {
            // The same tab over a database that names a profile and over a database that names
            // none, in the room of the real dialog: the table under the notice alone is taller by
            // the line at the top, less the notice.
            var dialogs = new (string Name, Func<Form> New, string Scope)[]
            {
                ("group", () => NewGroupForm(), GrantTab.GroupScope),
                ("root group", () => NewGroupForm(), GrantTab.RootScope),
                ("entry", () => NewEntryForm(), GrantTab.EntryScope),
            };
            var text = scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text";

            foreach (var kind in dialogs)
            {
                int named;
                int line;
                using (var dialog = kind.New())
                using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                using (var panel = new GrantPanel(GrantSheet.Of(NodeRule.None, null, Names, null), kind.Scope, null, null, null))
                {
                    LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out _);
                    var scope = ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName);
                    Assert.That(scope.Visible, Is.True, "the line at the top of the " + kind.Name + " dialog's tab at " + text);
                    Assert.That(panel.Warning, Is.Null);
                    line = scope.Height;
                    named = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName).Height;
                }

                using (var dialog = kind.New())
                using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                using (var panel = new GrantPanel(GrantSheet.Of(NodeRule.None, null, Array.Empty<string>(), null), kind.Scope, null, null, null))
                {
                    LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out _);
                    var scope = ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName);
                    var notice = ControlTree.FindByName<Label>(panel, GrantTab.WarningLabelName);
                    var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                    Assert.That(notice.Visible && notice.Text == GrantTab.NoProfilesYet, Is.True, "the notice on the " + kind.Name + " dialog's tab at " + text);
                    Assert.That(scope.Visible, Is.False, "the line at the top beside the notice on the " + kind.Name + " dialog's tab at " + text);
                    Assert.That(grid.Top, Is.EqualTo(notice.Bottom), "the table under the notice on the " + kind.Name + " dialog's tab at " + text);
                    Assert.That(grid.Height, Is.EqualTo(named + line - notice.Height),
                        "the table under the notice alone on the " + kind.Name + " dialog's tab at " + text);
                }
            }
        }

        /// <summary>The texts of the tab that stand somewhere other than above the table and are named for no suffix of their kind.</summary>
        private static readonly string[] Elsewhere =
        {
            nameof(GrantTab.Text), nameof(GrantTab.AddRowPrompt), nameof(GrantTab.MaskNeedsAProfile), nameof(GrantTab.InheritedFrom),
            nameof(GrantTab.SealCheckBoxText), nameof(GrantTab.SealCheckBoxMixed), nameof(GrantTab.SealCheckBoxLost),
            nameof(GrantTab.ChangedHere), nameof(GrantTab.ChangedNotWritten), nameof(GrantTab.NothingArrives),
            nameof(GrantTab.SwitchApplies), nameof(GrantTab.NotKeptTitle), nameof(GrantTab.NotKept), nameof(GrantTab.NoLongerWritable),
            nameof(GrantTab.NotAllKept), nameof(GrantTab.NoneKept), nameof(GrantTab.NoRoom), nameof(GrantTab.StoreChanged),
            nameof(GrantTab.ChangeOutOfForm),
            nameof(GrantTab.StoreUnreadable), nameof(GrantTab.NotKeptDefect), nameof(GrantTab.ShownAsKept),
        };

        /// <summary>
        /// Every reason and notice that can stand above the table: every text of the tab but the
        /// texts known to stand elsewhere, so that a text added later is measured unless named
        /// here as standing somewhere else.
        /// </summary>
        /// <remarks>
        /// Elsewhere is a control's name, a heading, a tooltip, a word of the State column, the
        /// line at the top of the page, which <see cref="Scopes"/> measures, the box, the table's
        /// empty line, the picker, and the message shown once a dialog has closed.
        /// </remarks>
        private static readonly string[] AboveTheTable = Texts(name =>
            !Elsewhere.Contains(name)
            && !new[] { "Name", "Label", "Header", "Help", "Word", "Tip", "Grants", "Choice", "Removal", "Scope" }
                .Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)));

        /// <summary>Every line that can stand at the top of the page.</summary>
        private static readonly string[] Scopes = Texts(name => name.EndsWith("Scope", StringComparison.Ordinal));

        /// <summary>The texts of the tab whose names this keeps.</summary>
        private static string[] Texts(Func<string, bool> keep)
        {
            return typeof(GrantTab)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(field => field.FieldType == typeof(string) && keep(field.Name))
                .Select(field => (string)field.GetValue(null))
                .Distinct()
                .ToArray();
        }

        /// <summary>How many lines a label laid out takes: its height inside its padding over the height of a line.</summary>
        private static int LinesOf(Label label)
        {
            var line = TextRenderer.MeasureText("Ag", label.Font, Size.Empty, TextFormatFlags.NoPadding).Height;
            return (int)Math.Round((label.Height - label.Padding.Vertical) / (double)line);
        }

        /// <summary>
        /// How far a mask of every letter in bold reaches when drawn as the table draws a mask:
        /// each letter where the ordinary weight puts the letter, the right edge of the furthest.
        /// </summary>
        private static int BoldReach(Font regular)
        {
            // Measured on a surface, as the table measures while it paints: measured with no
            // surface, a text keeps its padding whatever the flags ask.
            const TextFormatFlags Measured = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            using (var surface = new Bitmap(1, 1))
            using (var graphics = Graphics.FromImage(surface))
            using (var bold = new Font(regular, regular.Style | FontStyle.Bold))
            {
                return Enumerable.Range(0, VerbMask.Letters.Length)
                    .Select(at =>
                        (at == 0 ? 0 : TextRenderer.MeasureText(graphics, VerbMask.Letters.Substring(0, at), regular, Size.Empty, Measured).Width)
                        + TextRenderer.MeasureText(graphics, VerbMask.Letters.Substring(at, 1), bold, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width)
                    .Max();
            }
        }

        private void AssertFits(Func<Form> newDialog, string scope, float scaling)
        {
            var text = scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text";
            foreach (var state in States)
            {
                using (var dialog = newDialog())
                using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                using (var panel = new GrantPanel(state.Sheet(), scope, null, null, null))
                {
                    SizeF scale;
                    var area = LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out scale);

                    Assert.That(panel.Padding.Left, Is.EqualTo((int)Math.Round(Spacing.Gap * scale.Width)),
                        "the grant tab kept an unscaled inset at " + text);

                    // The masks have a font of their own, a point over the panel's, and the column
                    // is as wide as they are.
                    var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                    var masks = new List<Font> { grid.Columns[GrantTab.MaskColumn].DefaultCellStyle.Font };
                    masks.AddRange(grid.Rows.Cast<DataGridViewRow>()
                        .Select(row => row.Cells[GrantTab.MaskColumn])
                        .Where(cell => cell.HasStyle && cell.Style.Font != null)
                        .Select(cell => cell.Style.Font));

                    foreach (var mask in masks)
                    {
                        Assert.That(mask.SizeInPoints, Is.EqualTo(font.SizeInPoints + 1f).Within(0.01f),
                            "a mask " + state.Name + " kept the unscaled font at " + text);
                    }

                    // The letters the versions of a rule do not agree on are drawn in bold, each in
                    // the place the ordinary weight puts the letter, so the last letter in bold
                    // ends inside the cell, on every table that holds a mask.
                    if (grid.Rows.Cast<DataGridViewRow>().Any(row => !row.IsNewRow))
                    {
                        Assert.That(grid.Columns[GrantTab.MaskColumn].Width - 1, Is.GreaterThanOrEqualTo(BoldReach(masks[0])),
                            "the last letter in bold runs past the cell " + state.Name + " at " + text);
                    }

                    // A bound row's bracket stands in a padding before the name, and the name beside
                    // the bracket keeps the room the empty line's prompt is given, the longest name
                    // of these states among the names held to it.
                    foreach (var row in grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow && ((GrantRow)row.Tag).Bound))
                    {
                        var profile = ((GrantRow)row.Tag).Profile;
                        var padding = row.Cells[GrantTab.ProfileColumn].Style.Padding.Left;
                        Assert.That(padding, Is.EqualTo(GrantMarks.Room(grid.Font)), profile + " " + state.Name + " at " + text);
                        Assert.That(grid.Columns[GrantTab.ProfileColumn].Width - padding,
                            Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(GrantTab.AddRowPrompt, grid.Font).Width),
                            "the name beside the bracket of " + (profile == Longest ? "the longest name" : profile) + " has less room than the empty line's prompt "
                            + state.Name + " at " + text);
                    }

                    Assert.That(
                        state.Name != "bound rows, a version removed"
                        || grid.Rows.Cast<DataGridViewRow>().Any(row => !row.IsNewRow && ((GrantRow)row.Tag).Bound && ((GrantRow)row.Tag).Profile == Longest),
                        Is.True, "the longest name stands in a bracket " + state.Name);

                    foreach (Control child in Everything(panel))
                    {
                        // Every edge, because a layout that runs off the page runs off the side it
                        // grows from, and under a right-to-left dialog that is the left.
                        Assert.That(child.Left, Is.GreaterThanOrEqualTo(0), Describe(child, state) + " runs past the left of the page at " + text);
                        Assert.That(child.Top, Is.GreaterThanOrEqualTo(0), Describe(child, state) + " runs past the top of the page at " + text);
                        Assert.That(child.Bottom, Is.LessThanOrEqualTo(area.Height), Describe(child, state) + " runs past the bottom of the page at " + text);
                        Assert.That(child.Right, Is.LessThanOrEqualTo(area.Width), Describe(child, state) + " runs past the right of the page at " + text);
                    }

                    // Nothing scrolls sideways: the columns fit the table's width beside the
                    // vertical scroll bar, and the profile keeps the room of the empty line's
                    // prompt.
                    var columns = grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
                    Assert.That(columns, Is.LessThanOrEqualTo(grid.DisplayRectangle.Width),
                        "the columns " + state.Name + " are wider than the table at " + text);
                    Assert.That(grid.Controls.OfType<HScrollBar>().Any(bar => bar.Visible), Is.False,
                        "the table " + state.Name + " scrolls sideways at " + text + ": columns of "
                        + string.Join(", ", grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width)) + " in a table "
                        + grid.ClientSize.Width + " wide");

                    // More rows than fit, and the vertical scroll bar beside them, which the
                    // columns above were measured beside.
                    var overflows = grid.ColumnHeadersHeight + grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible) > grid.ClientSize.Height;
                    Assert.That(overflows || !state.Crowded, Is.True, "the table " + state.Name + " holds every row at " + text);
                    Assert.That(grid.Controls.OfType<VScrollBar>().Any(bar => bar.Visible), Is.EqualTo(overflows),
                        "the table " + state.Name + (overflows ? " shows no vertical scroll bar for rows that do not fit" : " scrolls with every row shown") + " at " + text);
                    Assert.That(grid.Columns[GrantTab.ProfileColumn].Width,
                        Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(GrantTab.AddRowPrompt, grid.Font).Width),
                        "the prompt of the empty line is cut " + state.Name + " at " + text);

                    var words = grid.Columns[GrantTab.StateColumn];
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        var word = Convert.ToString(row.Cells[GrantTab.StateColumn].Value, CultureInfo.InvariantCulture);
                        Assert.That(words.Width, Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(word, grid.Font).Width),
                            "\"" + word + "\" is cut at " + text);
                    }

                    var fit = GridFit.Of(panel, dialog, state.Name, scaling);
                    TestContext.Out.WriteLine(fit);
                    AssertKeepsARow(fit);
                }
            }
        }

        /// <summary>A state of the tab, by name, as a sheet built afresh for each dialog.</summary>
        internal sealed class TabState
        {
            public TabState(string name, Func<GrantSheet> sheet, bool crowded)
            {
                Name = name;
                Sheet = sheet;
                Crowded = crowded;
            }

            public string Name { get; }

            public Func<GrantSheet> Sheet { get; }

            /// <summary>Whether the state holds more rows than any dialog shows, which is when the vertical scroll bar is asserted.</summary>
            public bool Crowded { get; }
        }

        /// <summary>
        /// Every state the tab has a different height or width in: no rules; a database that names
        /// no profile, its notice alone over the table; a rule; every word of the State column with
        /// the box cleared and with the box ticked, among more rows than fit;
        /// and the longest reason the tab gives for taking no press, over versions and a limit.
        /// </summary>
        internal static readonly TabState[] States =
        {
            new TabState("empty", () => GrantSheet.Of(NodeRule.None, null, Names, null), false),
            new TabState(
                "naming no profile",
                () => GrantSheet.Of(Rules.Node(false, ("retired", "RL")), Arriving(("editor", "R")), Array.Empty<string>(), null),
                false),
            new TabState("with a rule", () => GrantSheet.Of(Rules.Node(false, ("reader", "RL")), Arriving(("editor", "R")), Names, null), false),
            new TabState("with every state, the box cleared", () => EveryState(false, null), true),
            new TabState("with every state, the box ticked", () => EveryState(true, null), true),
            new TabState("read only, the longest reason", () => EveryState(false, LongestReason), true),
            new TabState("bound rows, a version removed", BoundAndChanged, false),
        };

        /// <summary>
        /// A rule in three versions with a version removed on the tab, a rule of the longest name
        /// in two versions with every letter apart, and a limit under its inherited rule.
        /// </summary>
        private static GrantSheet BoundAndChanged()
        {
            var sheet = GrantSheet.Of(
                Rules.Stored(
                    ("deploy", "row:CRUSDLAP"), ("deploy", "row:"), ("deploy", "off:R"), ("ops", "off:R"),
                    (Longest, "row:CRUSDLAP"), (Longest, "row:")),
                Arriving(("ops", "CRUSDLAP")),
                Names,
                null);

            Assert.That(sheet.RemoveVersion("deploy", sheet.Rows.First(row => row.Profile == "deploy").Version), Is.True);
            return sheet;
        }

        /// <summary>A profile name as long as the issuer allows.</summary>
        private static readonly string Longest = new string('w', 64);

        private static readonly string[] Names = { "reader", "editor", "deploy", "lost", "damaged", "ops", Longest };

        /// <summary>The longest of the texts that can stand above the table.</summary>
        private static readonly string LongestReason = AboveTheTable.OrderByDescending(reason => reason.Length).First();

        private static GrantSheet EveryState(bool refusing, string reason)
        {
            var versions = new List<(string Profile, string Text)>
            {
                ("deploy", "row:CRUSDLAP"), ("deploy", "off:CRUSDLAP"), ("lost", null), ("damaged", "row:x"), ("ops", "off:R"),
                ("gone", "row:R"), (Longest, "row:CRUSDLAP"),
            };
            for (var i = 0; i < MoreRulesThanFit; i++)
            {
                versions.Add(("p" + i.ToString(CultureInfo.InvariantCulture), "row:CRUSDLAP"));
            }

            if (refusing)
            {
                versions.Add((null, "sw:deny"));
            }

            return GrantSheet.Of(
                Rules.Stored(versions.ToArray()),
                Arriving(("ops", "CRUSDLAP"), ("reader", "CRUSDLAP")),
                Names.Concat(Enumerable.Range(0, MoreRulesThanFit).Select(i => "p" + i.ToString(CultureInfo.InvariantCulture))),
                reason);
        }

        /// <summary>
        /// How many rules beside every state the crowded states hold: more rows than the entry
        /// dialog, the taller of the two, shows at any scaling measured.
        /// </summary>
        private const int MoreRulesThanFit = 30;

        /// <summary>What arrives from the groups above for each profile, as the editor is given it.</summary>
        internal static AclInheritance Arriving(params (string Profile, string Mask)[] rules)
        {
            var grants = rules.ToDictionary(rule => rule.Profile, rule => Rules.Letters(rule.Mask), StringComparer.Ordinal);
            return new AclInheritance(grants);
        }

        /// <summary>
        /// Whether the table keeps its heading and a row, which staying inside the page does not
        /// ask: a table squeezed to its heading still fits.
        /// </summary>
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
            return NewEntryForm(_database, _icons);
        }

        internal static Form NewEntryForm(TemporaryDatabase database, ImageList icons)
        {
            var entry = new PwEntry(true, true);
            database.RootGroup.AddEntry(entry, true);

            var dialog = new PwEntryForm();
            dialog.InitEx(entry, PwEditMode.EditExistingEntry, database.Database, icons, false, false);
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

        private static string Describe(Control control, TabState state)
        {
            return (string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name) + " (" + state.Name + ")";
        }
    }

    /// <summary>How tall the grant table was laid out, and what its heading and a row need.</summary>
    [Serializable]
    internal sealed class GridFit
    {
        public string Where { get; set; }

        public int Height { get; set; }

        public int Heading { get; set; }

        public int Row { get; set; }

        /// <summary>What the table on a panel that has been laid out came to.</summary>
        public static GridFit Of(Control panel, Form dialog, string state, float scaling)
        {
            var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
            Assert.That(grid, Is.Not.Null, "the grant tab has no table on it");

            return new GridFit
            {
                Where = "The table on the " + (dialog is GroupForm ? "group" : "entry") + " dialog, "
                        + state + ", at " + scaling.ToString("0.00", CultureInfo.InvariantCulture) + " text,",
                Height = grid.Height,
                Heading = grid.ColumnHeadersHeight,
                Row = grid.RowTemplate.Height
            };
        }

        /// <summary>How many rows the table shows under its heading.</summary>
        public int Rows
        {
            get { return Row <= 0 ? 0 : Math.Max(0, (Height - Heading) / Row); }
        }

        public override string ToString()
        {
            return Where + " is " + Height.ToString(CultureInfo.InvariantCulture) + " high where its heading and a row need "
                   + Heading.ToString(CultureInfo.InvariantCulture) + " and "
                   + Row.ToString(CultureInfo.InvariantCulture) + ", and shows "
                   + Rows.ToString(CultureInfo.InvariantCulture) + " rows";
        }
    }

    /// <summary>
    /// The grant tab laid out with visual styles on, in whichever domain this is created in. Only
    /// ever created in a domain made for it: see <see cref="GrantTabFitsTests"/>.
    /// </summary>
    internal sealed class ThemedGrantTab : MarshalByRefObject
    {
        public GridFit[] Measure(float scaling)
        {
            Application.EnableVisualStyles();

            var fits = new List<GridFit>();
            using (var database = TemporaryDatabase.New())
            using (var icons = new ImageList())
            {
                Measure(() => GrantTabFitsTests.NewGroupForm(database), GrantTab.GroupScope, scaling, fits);
                Measure(() => GrantTabFitsTests.NewEntryForm(database, icons), GrantTab.EntryScope, scaling, fits);
            }

            return fits.ToArray();
        }

        private static void Measure(Func<Form> newDialog, string scope, float scaling, List<GridFit> fits)
        {
            foreach (var state in GrantTabFitsTests.States)
            {
                using (var dialog = newDialog())
                using (var font = new Font(dialog.Font.FontFamily, dialog.Font.Size * scaling, dialog.Font.Style))
                using (var panel = new GrantPanel(state.Sheet(), scope, null, null, null))
                {
                    GrantTabFitsTests.LayOut(dialog, KeePassContract.NodeFormTabControlName, font, panel, out _);
                    fits.Add(GridFit.Of(panel, dialog, state.Name, scaling));
                }
            }
        }
    }
}
