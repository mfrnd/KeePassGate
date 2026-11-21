using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The two dropdowns on the grant editor, driven on a real window with a real message loop.
    /// </summary>
    /// <remarks>
    /// **This fixture exists because of a defect, and the defect was a dialog closing.** Pressing
    /// the profile cell twice, or choosing a profile from the list, took the whole group dialog
    /// with it. Nothing on the tab reported anything, because what failed was not the tab: a
    /// dropdown was being torn down from inside its own closing event, while WinForms was still
    /// delivering the click that closed it, and an exception raised there does not reach a label on
    /// a page. It reaches the message loop, and KeePass closes the dialog.
    ///
    /// So this is the tier that would have caught it: an actual `Form`, shown, with messages
    /// pumped, and every exception the loop sees recorded rather than lost. `GrantPanelTests`
    /// cannot see any of this, because a panel with no handle never opens a dropdown at all.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class GrantPickerTests
    {
        private readonly List<Exception> _loop = new List<Exception>();

        [SetUp]
        public void WatchTheMessageLoop()
        {
            _loop.Clear();
            Application.ThreadException += OnThreadException;
        }

        [TearDown]
        public void StopWatching()
        {
            Application.ThreadException -= OnThreadException;
        }

        [Test]
        public void The_empty_line_carries_no_button_and_is_drawn_unavailable()
        {
            // Measured off the pixels, because what was wrong was neither the cell's value nor its
            // style. A button cell paints a button whatever it holds, so the one line with nothing
            // to remove was the line wearing a framed box around nothing: on a table where every
            // other line's box removes a rule, that reads as a control that is broken rather than
            // one that is not available yet.
            Using((form, panel, grid) =>
            {
                var line = grid.Rows[grid.NewRowIndex];

                Assert.That(
                    line.Cells[ForgetColumn].Style.BackColor,
                    Is.EqualTo(line.Cells[MaskColumn].Style.BackColor),
                    "two cells unavailable for the same reason, drawn two different ways");

                Assert.That(
                    Darkest(grid, ForgetColumn, grid.NewRowIndex),
                    Is.GreaterThan(128),
                    "something is drawn on the line that has nothing to draw");
            });
        }

        [Test]
        public void An_inherited_line_carries_no_button_either()
        {
            // For the same reason and by the same drawing: the rule on this line belongs to a
            // group above, so there is nothing here to remove, and the column's own heading says
            // so. What it must not do is offer a box that does nothing when pressed.
            Inheriting((form, panel, grid) =>
            {
                var line = grid.Rows[0];

                Assert.That(line.Cells[ProfileColumn].Value, Is.EqualTo("reader"));
                Assert.That(line.Cells[ForgetColumn].Value, Is.Empty, "an inherited line owns no rule");

                Assert.That(
                    Darkest(grid, ForgetColumn, 0),
                    Is.GreaterThan(128),
                    "something is drawn on a line that has nothing to draw");
            });
        }

        [Test]
        public void Pressing_the_profile_cell_twice_leaves_the_window_open()
        {
            // The reported gesture, exactly: press, press again. The second press arrives while the
            // first dropdown is up, and what it must never do is open a second one over it.
            Using((form, panel, grid) =>
            {
                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.True, "the first press opened nothing");

                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.True, "and the second opened a second one");
                Assert.That(form.IsDisposed, Is.False, "the window closed");
                Assert.That(_loop, Is.Empty);
            });
        }

        [TestCase(ProfileColumn, GrantTab.ProfilePickerName, GrantTab.ProfilePickerLabel)]
        [TestCase(MaskColumn, GrantTab.RulePickerName, GrantTab.RulePickerLabel)]
        public void Each_picker_is_named_to_a_screen_reader(int column, string name, string label)
        {
            // A list with no text of its own, announced as a bare list: the keyboard's only way
            // to write a rule, named to nobody using it without the screen.
            Using(Rules.Node(false, ("reader", "R")), (form, panel, grid) =>
            {
                panel.Press(column == ProfileColumn ? grid.NewRowIndex : 0, column);
                OffScreenForm.Pump();
                Assume.That(panel.Picking, Is.True);

                var open = (ToolStripDropDown)typeof(GrantPanel)
                    .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                var list = open.Items.OfType<ToolStripControlHost>().Select(host => host.Control)
                    .Single(control => control.Name == name);

                Assert.That(list.AccessibleName, Is.EqualTo(label));

                panel.ClosePicker();
                OffScreenForm.Pump();
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A_profile_picker_of_a_hundred_names_scrolls_inside_its_screen_and_reaches_the_last_name(bool nearTheBottom)
        {
            // The list of profile names keeps no ceiling, `STORE.md` §616: the picker offers every
            // name, holds its height to whole rows inside the working area whether it opens
            // downward or upward, and scrolls down through the rest, never across.
            var names = Enumerable.Range(0, 99)
                .Select(index => "p" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))
                .Concat(new[] { "zulu" })
                .ToArray();

            OnScreen(names, nearTheBottom, (form, panel, grid, open, list) =>
            {
                var area = Screen.FromControl(grid).WorkingArea;
                TestContext.Out.WriteLine("list " + list.Size + " client " + list.ClientSize + " rows of " + list.ItemHeight
                                          + ", drop-down " + open.Bounds + ", working area " + area);

                Assert.That(list.Items.Cast<string>(), Is.EqualTo(names), "every name is offered");
                Assert.That(area.Contains(open.Bounds), Is.True, "the drop-down lies inside the working area");
                Assert.That(list.ClientSize.Height % list.ItemHeight, Is.Zero, "a whole number of rows");
                Assert.That(list.ClientSize.Height / list.ItemHeight, Is.LessThanOrEqualTo(GrantTab.MostProfileRows));
                Assert.That(list.ClientSize.Height, Is.LessThan(list.ItemHeight * names.Length), "it scrolls down");
                Assert.That(list.Width - list.ClientSize.Width, Is.EqualTo(SystemInformation.VerticalScrollBarWidth), "with a scroll bar");
                Assert.That(
                    names.Max(name => TextRenderer.MeasureText(name, list.Font).Width),
                    Is.LessThanOrEqualTo(list.ClientSize.Width),
                    "and never across: no name is cut by it");

                // The keyboard keeps the chosen row in view.
                foreach (var (key, index) in new[] { (Keys.Down, 1), (Keys.End, 99), (Keys.Home, 0), (Keys.Next, -1), (Keys.Down, -1), (Keys.Prior, -1), (Keys.Z, 99), (Keys.Up, 98) })
                {
                    PressKey(open, key);
                    if (index >= 0)
                    {
                        Assert.That(list.SelectedIndex, Is.EqualTo(index), key.ToString());
                    }

                    Assert.That(list.ClientRectangle.Contains(list.GetItemRectangle(list.SelectedIndex)), Is.True, key + " leaves the row chosen out of view");
                }

                Assert.That(panel.Picking, Is.True, "and the picker stays open");

                // The wheel scrolls it, and the last name is a click away.
                PressKey(list, Keys.Home);
                TestNativeMethods.PostMessage(list.Handle, WheelMessage, (IntPtr)(-120 << 16), IntPtr.Zero);
                OffScreenForm.Pump();
                Assert.That(list.TopIndex, Is.GreaterThan(0), "the wheel scrolls the list");

                for (var turn = 0; turn < names.Length && !list.ClientRectangle.Contains(list.GetItemRectangle(names.Length - 1)); turn++)
                {
                    TestNativeMethods.PostMessage(list.Handle, WheelMessage, (IntPtr)(-120 << 16), IntPtr.Zero);
                    OffScreenForm.Pump();
                }

                PressAt(list, list.GetItemRectangle(names.Length - 1));
                Assert.That(PumpUntil(() => panel.Lines.Any(line => line.StartsWith("zulu", StringComparison.Ordinal)), TimeSpan.FromSeconds(2)), Is.True,
                    "the last name was chosen by the mouse");
                Assert.That(_loop, Is.Empty);
            });
        }

        [TestCase(3)]
        [TestCase(GrantTab.MostProfileRows)]
        public void A_profile_picker_of_as_many_names_as_fit_is_the_list_it_was(int count)
        {
            // Where every name fits, nothing bounds the list: the host sizes it to the list's own
            // preferred size with no constraint on it, as it always did, so the drop-down is the
            // same pixel for pixel, with no scroll bar, as every picture of it shows.
            var names = Enumerable.Range(0, count)
                .Select(index => "profile-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            OnScreen(names, false, (form, panel, grid, open, list) =>
            {
                Assert.That(list.MinimumSize, Is.EqualTo(Size.Empty), "nothing bounds the list");
                Assert.That(list.MaximumSize, Is.EqualTo(Size.Empty));
                Assert.That(list.Size, Is.EqualTo(list.GetPreferredSize(Size.Empty)), "the size the host gives a list it does not bound");
                Assert.That(list.Width, Is.EqualTo(list.ClientSize.Width), "no scroll bar");
                Assert.That(list.ClientSize.Height, Is.EqualTo(list.ItemHeight * count), "every name shown");
                Assert.That(_loop, Is.Empty);
            });
        }

        /// <summary>
        /// The profile picker opened over these names, on a window shown on the screen, near its
        /// top or near its bottom, where the drop-down has to open upward.
        /// </summary>
        private void OnScreen(string[] names, bool nearTheBottom, Action<Form, GrantPanel, DataGridView, ToolStripDropDown, ListBox> work)
        {
            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(GrantSheet.Of(NodeRule.None, null, names, null), GrantTab.GroupScope, null, null, null))
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                form.ClientSize = new Size(420, 220);
                form.Location = nearTheBottom
                    ? new Point(area.Left + 40, area.Bottom - form.Height - 4)
                    : new Point(area.Left + 40, area.Top + 40);
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();
                Assume.That(panel.Picking, Is.True);

                var open = (ToolStripDropDown)typeof(GrantPanel)
                    .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                var list = (ListBox)open.Items.OfType<ToolStripControlHost>().Select(host => host.Control)
                    .Single(control => control.Name == GrantTab.ProfilePickerName);
                try
                {
                    work(form, panel, grid, open, list);
                }
                finally
                {
                    if (panel.Picking)
                    {
                        panel.ClosePicker();
                        OffScreenForm.Pump();
                    }

                    form.Close();
                    OffScreenForm.Pump();
                }
            }
        }

        [Test]
        public void Closing_a_picker_leaves_the_window_open()
        {
            // The other half of the same defect: the dropdown going away is what disposed the
            // control whose event was running.
            Using((form, panel, grid) =>
            {
                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();

                panel.ClosePicker();
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.False);
                Assert.That(form.IsDisposed, Is.False);
                Assert.That(form.Visible, Is.True);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_picker_can_be_opened_again_after_it_has_been_closed()
        {
            // Because refusing a second one while the first is open is only right if closing the
            // first actually gives the table back.
            Using((form, panel, grid) =>
            {
                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();
                panel.ClosePicker();
                OffScreenForm.Pump();

                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.True);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void The_dialogs_own_buttons_are_given_back_when_a_picker_closes()
        {
            // While a picker is open, Enter and Escape are its own, or the form's accept and cancel
            // take them and choosing a profile presses OK on somebody else's dialog. Handing them
            // back is part of the closing that now happens a message later, so this is also what
            // says that deferral did not simply drop it.
            Using((form, panel, grid) =>
            {
                var accept = new Button();
                var cancel = new Button();
                form.Controls.Add(accept);
                form.Controls.Add(cancel);
                form.AcceptButton = accept;
                form.CancelButton = cancel;

                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();

                Assert.That(form.AcceptButton, Is.Null, "the picker did not take Enter");
                Assert.That(form.CancelButton, Is.Null, "the picker did not take Escape");

                panel.ClosePicker();
                OffScreenForm.Pump();

                Assert.That(form.AcceptButton, Is.SameAs(accept));
                Assert.That(form.CancelButton, Is.SameAs(cancel));
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_picker_closed_by_a_click_on_another_rule_leaves_the_table_and_the_dialog_standing()
        {
            // Closed the way an operator closes a picker, by pressing somewhere else, rather than
            // by a call: the press goes through the loop, where the dropdown closes itself on it
            // before the table is handed it, and the table and the dialog are left as they were.
            Using(
                Rules.Node(false, ("reader", "R"), ("ops", "RL")),
                (form, panel, grid) =>
                {
                    grid.Focus();
                    grid.CurrentCell = grid.Rows[0].Cells[MaskColumn];
                    OffScreenForm.Pump();
                    PressKey(grid, Keys.F2);
                    Assert.That(panel.Picking, Is.True, "F2 opened nothing, so nothing below is measured");

                    PressAt(grid, grid.GetCellDisplayRectangle(ProfileColumn, 1, false));

                    Assert.That(panel.Picking, Is.False, "the press elsewhere did not close the picker");
                    Assert.That(form.IsDisposed, Is.False, "the window closed");
                    Assert.That(panel.Lines, Is.EqualTo(new[] { "ops .R...L..", "reader .R......" }));
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void Enter_in_an_open_picker_chooses_and_presses_nothing_behind_it()
        {
            // The keystroke itself, posted rather than called, so that it reaches the picker the
            // way a key does, through the menu filter and the picker's own `ProcessCmdKey`, which
            // every test calling `Type` goes round, and presses nothing of the dialog behind it.
            Using((form, panel, grid) =>
            {
                var accept = new Button();
                var cancel = new Button();
                var pressed = 0;
                accept.Click += (sender, e) => pressed++;
                cancel.Click += (sender, e) => pressed++;
                form.Controls.Add(accept);
                form.Controls.Add(cancel);
                form.AcceptButton = accept;
                form.CancelButton = cancel;

                grid.Focus();
                grid.CurrentCell = grid.Rows[grid.NewRowIndex].Cells[ProfileColumn];
                OffScreenForm.Pump();
                PressKey(grid, Keys.F2);
                Assert.That(panel.Picking, Is.True, "F2 opened nothing, so nothing below is measured");

                var open = (ToolStripDropDown)typeof(GrantPanel)
                    .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                PressKey(open, Keys.Down);
                PressKey(open, Keys.Enter);

                Assert.That(PumpUntil(() => open.IsDisposed, TimeSpan.FromSeconds(2)), Is.True, "Enter did not close the picker");
                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader ........" }), "nor chose what Down highlighted");
                Assert.That(pressed, Is.Zero, "Enter pressed a button of the dialog under the picker");

                // The rule picker opens straight after a profile is chosen, and holds the keys
                // while it is open; Escape leaves it, and the dialog has its keys back.
                if (panel.Picking)
                {
                    var rules = (ToolStripDropDown)typeof(GrantPanel)
                        .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                    PressKey(rules, Keys.Escape);
                    PumpUntil(() => !panel.Picking, TimeSpan.FromSeconds(2));
                }

                Assert.That(pressed, Is.Zero, "Escape pressed a button of the dialog under the picker");
                Assert.That(form.AcceptButton, Is.SameAs(accept), "and the dialog has its keys back");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_picker_opened_before_the_last_has_finished_closing_leaves_the_dialog_its_keys()
        {
            // A picker finishes closing a message later, and a click landing in between opens the
            // next one. Each took the dialog's buttons for itself, so the first gave OK and Cancel
            // back under the second, and the second then put back the nothing it had found.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    var accept = new Button();
                    var cancel = new Button();
                    form.Controls.Add(accept);
                    form.Controls.Add(cancel);
                    form.AcceptButton = accept;
                    form.CancelButton = cancel;

                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    // Closed, and pressed again before the loop has run the first one's finish.
                    panel.ClosePicker();
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Picking, Is.True, "the second picker did not open");
                    Assert.That(form.AcceptButton, Is.Null, "Enter would press OK under the open picker");
                    Assert.That(form.CancelButton, Is.Null);

                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(form.AcceptButton, Is.SameAs(accept), "the dialog no longer answers Enter");
                    Assert.That(form.CancelButton, Is.SameAs(cancel));
                    Assert.That(_loop, Is.Empty);
                });
        }

        [TestCase(ProfileColumn)]
        [TestCase(MaskColumn)]
        public void A_picker_open_when_its_window_goes_is_let_go_of_and_the_next_picker_stays_open(int column)
        {
            // Closed as a program closes a dialog, by a close sent to the window, which leaves the
            // picker open: a press outside it, or Alt and F4, closes the picker first. WinForms
            // closed it at the next change of activation, after the window and the panel had gone,
            // and the picker was disposed inside its own `Closed`. What WinForms asked of it next
            // threw from a message hook, past every handler, and ended the process: KeePass with
            // its fatal error, and this test host rather than this assertion.
            ToolStripDropDown open;

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                GrantSheet.Of(Rules.Node(false, ("reader", "R")), null, new[] { "reader", "ops" }, null), GrantTab.GroupScope, null, null, null))
            {
                var accept = new Button();
                var cancel = new Button();
                form.Controls.Add(accept);
                form.Controls.Add(cancel);
                form.AcceptButton = accept;
                form.CancelButton = cancel;

                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                panel.Press(column == ProfileColumn ? grid.NewRowIndex : 0, column);
                OffScreenForm.Pump();
                Assert.That(panel.Picking, Is.True, "nothing opened, so nothing below is measured");

                open = (ToolStripDropDown)typeof(GrantPanel)
                    .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);

                form.Close();
            }

            // The next window to take the activation is what had WinForms close a picker left open.
            Using((form, panel, grid) =>
            {
                Assert.That(PumpUntil(() => open.IsDisposed, TimeSpan.FromSeconds(2)), Is.True,
                    "the picker left open was never let go of");
                Assert.That(_loop, Is.Empty);

                // And the next picker is not closed the moment it opens, which is what a dropdown
                // closed by code before its window goes leaves behind.
                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();
                PumpUntil(() => !panel.Picking, TimeSpan.FromMilliseconds(500));

                Assert.That(panel.Picking, Is.True, "the next picker closed by itself");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_verb_can_be_typed_the_moment_the_picker_opens()
        {
            // It could not, and the reason was not a focus bug to work around: while a dropdown is
            // up, `ToolStripManager.ModalMenuFilter` routes keystrokes to the dropdown rather than
            // to whatever is inside it, so a handler on the list heard nothing until a click had
            // moved the focus into it. The letters worked, but only after checking one with the
            // pointer first.
            Using(
                Rules.Node(false, ("reader", "")),
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Picking, Is.True);
                    Assert.That(panel.Type(Keys.R), Is.True, "nothing took the letter");
                    Assert.That(panel.Type(Keys.L), Is.True);

                    Assert.That(panel.Type(Keys.Enter), Is.True);
                    OffScreenForm.Pump();

                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.." }));
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void Looking_at_an_inherited_rule_and_clicking_away_writes_nothing()
        {
            // The picker opens with the inherited rule already checked, and clicking away is how
            // a dropdown is usually left. Without this, opening one to read it wrote a
            // local rule shadowing the parent for good. Narrowing the parent later would then
            // leave this node holding whatever the parent used to grant.
            Inheriting(
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    // Not Escape. Escape already meant "as it was"; this is the other way out.
                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.. " + GrantTab.InheritedWord }));
                    Assert.That(Pressed, Is.False, "nothing was checked, so nothing was decided");
                });
        }

        [Test]
        public void Changing_an_inherited_rule_and_clicking_away_still_narrows_it()
        {
            // The other half, or the fix above would have made the picker unusable by mouse.
            Inheriting(
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    panel.Type(Keys.L);
                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R......" }));
                    Assert.That(Pressed, Is.True);
                });
        }

        [Test]
        public void A_rule_the_sheet_refuses_is_said_rather_than_swallowed()
        {
            // A profile name no certificate could carry, which a record composed by something
            // other than the plugin can hold, and the table shows. A picker that took a rule on it
            // and closed without a word would be a control that did nothing.
            Using(
                Rules.Stored(("Default", "row:R")),
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    panel.Type(Keys.L);
                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(panel.Warning, Is.Not.Null.And.Not.Empty,
                        "a picker that closes on a refusal it never mentions is a control that did nothing");
                    Assert.That(Pressed, Is.False);
                });
        }

        [Test]
        public void A_rule_whose_name_arrived_with_spaces_round_it_can_still_be_taken_off()
        {
            // The editor cannot write such a name, but a record composed elsewhere can carry one.
            // The table draws it trimmed, and the X has to take off the rule under the name the
            // record holds, which the row carries, rather than the name the cell shows.
            Using(
                Rules.Stored((" reader ", "row:R")),
                (form, panel, grid) =>
                {
                    Assert.That(panel.Lines.Count, Is.EqualTo(1));

                    panel.Press(0, ForgetColumn);
                    OffScreenForm.Pump();

                    Assert.That(Pressed, Is.True);
                    Assert.That(((GrantRow)grid.Rows[0].Tag).Profile, Is.EqualTo(" reader "));
                    Assert.That(((GrantRow)grid.Rows[0].Tag).Kind, Is.EqualTo(GrantRowKind.Inherited),
                        "taken off, and shown as what arrives until OK");
                });
        }

        [Test]
        public void Escape_leaves_the_rule_where_it_was()
        {
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    panel.Type(Keys.D);
                    panel.Type(Keys.Escape);
                    OffScreenForm.Pump();

                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R......" }));
                    Assert.That(panel.Picking, Is.False);
                });
        }

        [Test]
        public void A_letter_that_names_no_verb_is_left_for_whatever_wants_it()
        {
            Using(
                Rules.Node(false, ("reader", "")),
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Type(Keys.Z), Is.False);
                    Assert.That(panel.Type(Keys.Control | Keys.R), Is.False, "a shortcut is not a letter");

                    panel.ClosePicker();
                    OffScreenForm.Pump();
                });
        }

        [Test]
        public void The_permissions_cell_of_the_empty_line_does_nothing()
        {
            // A rule is a profile first, and that cell is drawn as unavailable. What it must not do
            // is look unavailable and act like a button.
            Using((form, panel, grid) =>
            {
                panel.Press(grid.NewRowIndex, MaskColumn);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.False);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void The_profile_of_a_rule_that_exists_cannot_be_changed()
        {
            // Changing it in place would be two edits wearing one gesture: the old profile silently
            // loses its rule and the new one silently gains whatever the row held.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    panel.Press(0, ProfileColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Picking, Is.False, "the chooser opened on a row that has one");
                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R......" }));
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void The_permissions_of_a_rule_that_exists_can_be_changed()
        {
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Picking, Is.True);
                    Assert.That(_loop, Is.Empty);

                    panel.ClosePicker();
                    OffScreenForm.Pump();
                });
        }

        [Test]
        public void Nothing_is_drawn_chosen_when_the_table_is_first_shown()
        {
            // The redraw clears the selection before the grid has a window, and a grid makes its
            // first cell current and selects it as the window is made, so the first rule on every
            // tab opened in the selection color as though somebody had chosen it.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    Assert.That(grid.SelectedCells, Is.Empty, "the first rule opened drawn as chosen");
                    Assert.That(grid.SelectedRows, Is.Empty);
                });
        }

        [Test]
        public void F2_still_opens_the_picker_after_a_rule_has_been_chosen()
        {
            // F2 and Alt+Down open a picker on the current cell. Every edit rebuilds the table, and
            // the rebuild took the current cell away while the grid kept the focus, so once a rule
            // had been chosen the keyboard opened nothing until an arrow key gave it a cell back.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    grid.Focus();
                    grid.CurrentCell = grid.Rows[0].Cells[MaskColumn];
                    OffScreenForm.Pump();

                    PressKey(grid, Keys.F2);
                    Assert.That(panel.Picking, Is.True, "F2 opened nothing to begin with");

                    panel.Type(Keys.L);
                    panel.Type(Keys.Enter);
                    OffScreenForm.Pump();
                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.." }));

                    PressKey(grid, Keys.F2);
                    Assert.That(panel.Picking, Is.True, "F2 opened nothing once a rule was chosen");
                    Assert.That(grid.SelectedCells, Is.Empty, "and the rule is not drawn as chosen");
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void Alt_and_Down_open_either_picker_as_F2_does()
        {
            // S197: F2 and Alt+Down are the keyboard's two ways into the pickers, and only F2 was
            // ever posted. Alt+Down arrives as a system key and passes `IsInputKey` and every
            // `ProcessDialogKey` up to the dialog before the table hears it, which is a route of
            // its own, and a dialog that took Alt+Down for itself would have gone unnoticed.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    grid.Focus();
                    grid.CurrentCell = grid.Rows[0].Cells[MaskColumn];
                    OffScreenForm.Pump();

                    Keystrokes.AltDown(grid);
                    Assert.That(panel.Picking, Is.True, "Alt+Down opened no rule picker on a rule");

                    panel.Type(Keys.Escape);
                    OffScreenForm.Pump();
                    Assert.That(panel.Picking, Is.False);

                    grid.CurrentCell = grid.Rows[grid.NewRowIndex].Cells[ProfileColumn];
                    OffScreenForm.Pump();

                    Keystrokes.AltDown(grid);
                    Assert.That(panel.Picking, Is.True, "Alt+Down opened no profile list on the empty line");
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void F2_opens_the_rule_of_a_profile_just_chosen_after_its_picker_was_escaped()
        {
            // The route a keyboard takes to a new rule: F2 on the empty line, Enter on a profile,
            // and the rule picker that opens by itself. Escaping that one and pressing F2 to come
            // back to it is where the keyboard found nothing to open.
            Using((form, panel, grid) =>
            {
                grid.Focus();
                grid.CurrentCell = grid.Rows[grid.NewRowIndex].Cells[ProfileColumn];
                OffScreenForm.Pump();

                PressKey(grid, Keys.F2);
                Assert.That(panel.Picking, Is.True, "F2 opened no profile list on the empty line");

                panel.Type(Keys.Enter);
                OffScreenForm.Pump();
                Assert.That(panel.Picking, Is.True, "choosing a profile opened no rule picker");

                panel.Type(Keys.Escape);
                OffScreenForm.Pump();
                Assert.That(panel.Picking, Is.False);

                PressKey(grid, Keys.F2);
                Assert.That(panel.Picking, Is.True, "F2 opened nothing on the profile just chosen");

                panel.Type(Keys.R);
                panel.Type(Keys.Enter);
                OffScreenForm.Pump();

                Assert.That(panel.Lines, Is.EqualTo(new[] { "ops .R......" }),
                    "and what it opened was that profile's rule");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_defect_drawing_a_choice_is_recorded_with_the_keys_given_back_and_the_choice_on_the_sheet()
        {
            // What a picker's close does runs a message later, posted, and what it catches there by
            // name is what the user interface throws. A defect goes on to the window's handler,
            // which records it, `DESIGN.md` §141. What it must not leave behind is the dialog's
            // Enter and Escape still taken; the choice the sheet took before the table was drawn
            // again stays on the sheet, and OK writes it. The grid's own event is where a test
            // can make the drawing fail.
            var incidents = new GateIncidents(new RecordingGateLog());
            WindowHandlers.Incidents = incidents;

            try
            {
                Using(
                    Rules.Node(false, ("reader", "R")),
                    (form, panel, grid) =>
                    {
                        var accept = new Button();
                        var cancel = new Button();
                        form.Controls.Add(accept);
                        form.Controls.Add(cancel);
                        form.AcceptButton = accept;
                        form.CancelButton = cancel;

                        panel.Press(0, MaskColumn);
                        OffScreenForm.Pump();
                        Assert.That(panel.Picking, Is.True, "nothing opened, so nothing below is measured");

                        var armed = true;
                        grid.RowsAdded += (sender, e) =>
                        {
                            if (armed)
                            {
                                throw new NullReferenceException("a defect drawing the rules");
                            }
                        };

                        panel.Type(Keys.L);
                        panel.ClosePicker();
                        OffScreenForm.Pump();
                        armed = false;

                        Assert.That(_loop, Is.Empty, "a defect on the plugin's own window reached WinForms");
                        Assert.That(incidents.Recent, Has.Exactly(1).Contains(nameof(NullReferenceException)));
                        Assert.That(form.AcceptButton, Is.SameAs(accept), "Enter was left with the closed picker");
                        Assert.That(form.CancelButton, Is.SameAs(cancel), "Escape was left with the closed picker");
                        Assert.That(panel.Picking, Is.False);
                        Assert.That(Pressed, Is.True, "the choice the sheet took is written at OK");
                    });
            }
            finally
            {
                WindowHandlers.Incidents = null;
            }
        }

        [Test]
        public void A_closing_pickers_work_that_fails_is_shown_on_the_tab_as_a_choice_not_applied()
        {
            // What a picker's close does runs a message later, posted, and what the user interface
            // throws there is caught by name and put on the tab, with the choice left off the
            // table. Giving the dialog its accept button back is the first thing it does.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    var accept = new RefusingButton();
                    form.Controls.Add(accept);
                    form.AcceptButton = accept;

                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();
                    Assert.That(panel.Picking, Is.True, "nothing opened, so nothing below is measured");

                    accept.Refuses = true;
                    panel.Type(Keys.L);
                    panel.ClosePicker();
                    OffScreenForm.Pump();
                    accept.Refuses = null;

                    Assert.That(panel.Warning, Is.EqualTo(GrantTab.ChoiceNotTaken));
                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R......" }));
                    Assert.That(Pressed, Is.False);
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void A_closed_picker_that_cannot_be_let_go_of_is_shown_on_the_tab_as_not_closed()
        {
            // The same posted work, failing where its own catch does not reach: in letting go of
            // the dropdown, after the choice was taken. `Later` catches that by name instead.
            Using(
                Rules.Node(false, ("reader", "R")),
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();
                    Assert.That(panel.Picking, Is.True, "nothing opened, so nothing below is measured");

                    var open = (ToolStripDropDown)typeof(GrantPanel)
                        .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                    var thrown = false;
                    open.Disposed += (sender, e) =>
                    {
                        if (!thrown)
                        {
                            thrown = true;
                            throw new InvalidOperationException(RefusingButton.Refusal);
                        }
                    };

                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(thrown, Is.True, "the dropdown was never let go of, so nothing below is measured");
                    Assert.That(panel.Warning, Is.EqualTo(GrantTab.PickerNotClosed));
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void A_rule_picker_that_fails_to_open_after_a_profile_is_shown_on_the_tab_as_not_opened()
        {
            // Choosing a profile opens its rule picker a message later, through `Later`, and taking
            // the dialog's accept button away is the first thing a picker does as it opens.
            Using((form, panel, grid) =>
            {
                var accept = new RefusingButton();
                form.Controls.Add(accept);
                form.AcceptButton = accept;

                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();
                Assert.That(panel.Picking, Is.True, "nothing opened, so nothing below is measured");

                accept.Refuses = false;
                panel.Type(Keys.Enter);
                OffScreenForm.Pump();
                accept.Refuses = null;

                Assert.That(panel.Warning, Is.EqualTo(GrantTab.PickerNotOpened));
                Assert.That(panel.Lines, Is.EqualTo(new[] { "ops ........" }), "and the profile chosen is on the table");
                Assert.That(panel.Picking, Is.False);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_database_naming_no_profile_is_told_before_any_press_and_a_press_on_the_table_opens_nothing_of_a_profile()
        {
            // The state a database is in before anything has been set up at all: no client, no
            // profile. The tab tells so as it opens, offers no empty line to choose a profile on,
            // and a press on the table, a press on the rule picker of a name gone among them,
            // leaves the notice standing.
            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                GrantSheet.Of(Rules.Node(false, ("retired", "R")), null, Array.Empty<string>(), null), GrantTab.GroupScope, null, null, null))
            {
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(panel.Warning, Is.EqualTo(GrantTab.NoProfilesYet), "told before any press");
                Assert.That(grid.NewRowIndex, Is.EqualTo(-1), "no empty line");

                PressAt(grid, grid.GetCellDisplayRectangle(ProfileColumn, 0, false));
                PressAt(grid, grid.GetCellDisplayRectangle(MaskColumn, 0, false));
                Assert.That(panel.Picking, Is.True, "a rule is chosen on the row of a name gone as on any row");
                panel.ClosePicker();
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.False);
                Assert.That(panel.Warning, Is.EqualTo(GrantTab.NoProfilesYet), "the notice stands after a press");
                Assert.That(form.IsDisposed, Is.False);
                Assert.That(_loop, Is.Empty);

                form.Close();
                OffScreenForm.Pump();
            }
        }

        [Test]
        public void The_box_s_accessible_object_answers_the_whole_of_its_tooltip_as_its_help()
        {
            // Shown, because a box with a window is what an accessibility client asks. Unanswered,
            // the box's accessible object gives no help at all, while UI automation's own proxy
            // for a Windows button reads the tooltip's window, cut after 127 characters.
            Using(Rules.Node(false, ("reader", "R")), (form, panel, grid) =>
            {
                var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);
                var tip = GrantTab.BoxTip(GrantBox.Cleared, false, true);
                Assert.That(tip.Length, Is.GreaterThan(127), "a tip short enough to arrive whole tells nothing");

                Assert.That(box.AccessibleDescription, Is.EqualTo(tip));
                Assert.That(box.AccessibilityObject.Help, Is.EqualTo(tip));

                panel.PressBox();
                Assert.That(box.AccessibilityObject.Help, Is.EqualTo(GrantTab.BoxTip(GrantBox.Ticked, true, true)), "and follows a press");
            });
        }

        /// <summary>
        /// Every way a press on the table can end, after each of which Enter and Escape are the
        /// dialog's again.
        /// </summary>
        private static readonly string[] Ends =
        {
            NothingPressed, PressOnTheEmptyLine, ProfilePickerEscaped, ProfilePickerPressedAway, ProfilePickerPressedBeside,
            RuleChosenWithEnter, NoRoomToOpen,
        };

        private const string NothingPressed = "nothing pressed";
        private const string PressOnTheEmptyLine = "a press on the empty line where every profile has a row";
        private const string ProfilePickerEscaped = "the profile picker left with Escape";
        private const string ProfilePickerPressedAway = "the profile picker closed by a press on a rule";
        private const string ProfilePickerPressedBeside = "the profile picker closed by a press beside the table";
        private const string RuleChosenWithEnter = "a rule chosen with Enter";
        private const string NoRoomToOpen = "a picker with no room to open under its row";

        [Test]
        public void Escape_after_a_press_on_the_table_presses_the_dialog_s_Cancel([ValueSource(nameof(Ends))] string end)
        {
            // Escape is the dialog's Cancel, as on every other page of the dialog: left to the
            // table, Escape is the table's own while the keyboard stands on its empty line, for an
            // edit no cell here ever starts, and the dialog stays open.
            Keyed(end, Keys.Escape);
        }

        [Test]
        public void Enter_after_a_press_on_the_table_presses_the_dialog_s_OK([ValueSource(nameof(Ends))] string end)
        {
            // Enter is the dialog's OK while no picker is open: left to the table, Enter is the
            // table's own on every row, a move down a row.
            Keyed(end, Keys.Enter);
        }

        /// <summary>
        /// A dialog with an OK and a Cancel of its own, the table pressed as the end names, and
        /// then the key, posted to the table holding the keyboard: the button the key belongs to is
        /// pressed once and the other never, and no key of the press itself pressed either.
        /// </summary>
        private void Keyed(string end, Keys key)
        {
            var names = end == PressOnTheEmptyLine ? new[] { "reader", "ops" } : new[] { "reader", "ops", "deploy" };
            var sheet = Shown(GrantSheet.Of(Rules.Node(false, ("reader", "R"), ("ops", "RL")), null, names, null));

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(sheet, GrantTab.GroupScope, null, null, null))
            using (var accept = new Button { Text = "OK" })
            using (var cancel = new Button { Text = "Cancel" })
            {
                var accepted = 0;
                var canceled = 0;
                accept.Click += (sender, e) => accepted++;
                cancel.Click += (sender, e) => canceled++;

                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Controls.Add(accept);
                form.Controls.Add(cancel);
                form.AcceptButton = accept;
                form.CancelButton = cancel;
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                try
                {
                    grid.Focus();
                    OffScreenForm.Pump();
                    End(end, form, panel, grid);

                    Assert.That(panel.Picking, Is.False, "a picker is still open after " + end);
                    Assert.That(accepted + canceled, Is.Zero, "a key of the press itself pressed a button of the dialog: " + end);
                    Assert.That(form.AcceptButton, Is.SameAs(accept), "the dialog's OK was not given back after " + end);
                    Assert.That(form.CancelButton, Is.SameAs(cancel), "the dialog's Cancel was not given back after " + end);

                    // Posted where a key goes, to whatever holds the keyboard: the table, after
                    // every press on the table.
                    var holding = Holding(form);
                    Assert.That(holding, Is.Not.Null, "nothing holds the keyboard after " + end);
                    Assert.That(holding == grid || end == ProfilePickerPressedBeside, Is.True, "the keyboard left the table after " + end);

                    PressKey(holding, key);

                    Assert.That(key == Keys.Escape ? canceled : accepted, Is.EqualTo(1), key + " after " + end + " left the dialog open");
                    Assert.That(key == Keys.Escape ? accepted : canceled, Is.Zero, key + " after " + end + " pressed the other button");
                    Assert.That(_loop, Is.Empty);
                }
                finally
                {
                    panel.ClosePicker();
                    OffScreenForm.Pump();
                    form.Close();
                    OffScreenForm.Pump();
                }
            }
        }

        /// <summary>Presses the table as the end names, a picker opened by a real press where a picker opens.</summary>
        private static void End(string end, Form form, GrantPanel panel, DataGridView grid)
        {
            switch (end)
            {
                case NothingPressed:
                    return;

                case PressOnTheEmptyLine:
                    PressAt(grid, grid.GetCellDisplayRectangle(ProfileColumn, grid.NewRowIndex, false));
                    Assert.That(grid.CurrentCell.RowIndex, Is.EqualTo(grid.NewRowIndex), "the press did not reach the empty line");
                    Assert.That(panel.Picking, Is.False, "every profile has a row, so nothing opens");
                    return;

                case ProfilePickerEscaped:
                case ProfilePickerPressedAway:
                case ProfilePickerPressedBeside:
                    PressAt(grid, grid.GetCellDisplayRectangle(ProfileColumn, grid.NewRowIndex, false));
                    Assert.That(panel.Picking, Is.True, "the press on the empty line opened no picker");
                    if (end == ProfilePickerEscaped)
                    {
                        PressKey(Open(panel), Keys.Escape);
                    }
                    else if (end == ProfilePickerPressedAway)
                    {
                        PressAt(grid, grid.GetCellDisplayRectangle(ProfileColumn, 0, false));
                    }
                    else
                    {
                        PressAt(panel, new Rectangle(0, 0, panel.Padding.Left, panel.Padding.Top));
                    }

                    Assert.That(PumpUntil(() => !panel.Picking, TimeSpan.FromSeconds(2)), Is.True, "the picker did not close: " + end);
                    OffScreenForm.Pump();
                    return;

                case RuleChosenWithEnter:
                    PressAt(grid, grid.GetCellDisplayRectangle(MaskColumn, 0, false));
                    Assert.That(panel.Picking, Is.True, "the press on a rule opened no picker");
                    PressKey(Open(panel), Keys.C);
                    PressKey(Open(panel), Keys.Enter);
                    Assert.That(PumpUntil(() => !panel.Picking, TimeSpan.FromSeconds(2)), Is.True, "Enter did not close the picker");
                    OffScreenForm.Pump();
                    Assert.That(Pressed, Is.True, "Enter chose nothing");
                    return;

                default:
                    // The table held to its heading, so that no row has room for a picker under
                    // it, and F2 on a rule, which opens the picker the keyboard opens. The table
                    // refuses to scroll a row into a view with no room, and the picker is let go
                    // of unshown.
                    grid.CurrentCell = grid.Rows[0].Cells[MaskColumn];
                    form.ClientSize = new Size(form.ClientSize.Width, form.ClientSize.Height - (grid.Height - grid.ColumnHeadersHeight - 2));
                    OffScreenForm.Pump();
                    Assert.That(grid.GetCellDisplayRectangle(MaskColumn, 0, false).Height, Is.Zero, "the row still has room");
                    PressKey(grid, Keys.F2);
                    return;
            }
        }

        /// <summary>The control of a window that holds the keyboard, or null where none does.</summary>
        private static Control Holding(Control within)
        {
            if (within.Focused)
            {
                return within;
            }

            return within.Controls.Cast<Control>().Select(Holding).FirstOrDefault(control => control != null);
        }

        /// <summary>The picker that is open.</summary>
        private static ToolStripDropDown Open(GrantPanel panel)
        {
            var open = (ToolStripDropDown)typeof(GrantPanel)
                .GetField("_open", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            Assert.That(open, Is.Not.Null, "no picker is open");
            return open;
        }

        [Test]
        public void A_table_that_cannot_be_changed_can_still_be_scrolled_to_its_last_rule()
        {
            // An entry's earlier version, several entries at once: the tab says nothing can be
            // changed and that the rules stay readable. The table was disabled for it, and a
            // disabled grid takes neither its scroll bar nor the wheel, so only the rules that fit
            // could be read.
            var sheet = GrantSheet.Of(Numbered(MoreRulesThanFit, "RL"), null, null, GrantTab.EarlierVersion);

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(sheet, GrantTab.EntryScope, null, null, null))
            {
                form.ClientSize = TooShortForEveryRow;
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(grid.Rows[grid.Rows.Count - 1].Displayed, Is.False,
                    "the window is not short enough for this to be about anything");

                TestNativeMethods.PostMessage(grid.Handle, WheelMessage, (IntPtr)(-120 << 16), IntPtr.Zero);
                OffScreenForm.Pump();

                Assert.That(grid.FirstDisplayedScrollingRowIndex, Is.GreaterThan(0),
                    "the rules past the ones that fit cannot be reached");

                // And still nothing on it can be changed.
                Assert.That(grid.Rows[0].Cells[ForgetColumn].Value, Is.Empty,
                    "a table that cannot be changed offers to remove a rule");
                panel.Press(0, MaskColumn);
                panel.Press(0, ForgetColumn);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.False);
                Assert.That(sheet.Rows.Any(row => row.Changed), Is.False);
                Assert.That(_loop, Is.Empty);

                form.Close();
                OffScreenForm.Pump();
            }
        }

        [Test]
        public void The_rule_picker_opens_under_the_row_it_is_about_rather_than_at_the_top()
        {
            // `Populate` clears the table and adds every row again on every edit, which puts it
            // back at the top, so a rule sorting below the rows that fit is edited on a row nobody
            // can see. A row that is not displayed has no rectangle at all, and the dropdown was
            // then shown at the grid's own origin: over the headings and over a row belonging to a
            // different profile, while the rule it writes is off the bottom of the table.
            Cramped((form, panel, grid) =>
            {
                var last = grid.Rows.Count - 2;

                Assert.That(grid.Rows[last].Displayed, Is.False,
                    "the window is not short enough for this to be about anything");

                panel.OpenPicker(last);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.True);
                Assert.That(grid.Rows[last].Displayed, Is.True,
                    "the picker is about a row that is not on the screen");
                Assert.That(
                    grid.GetCellDisplayRectangle(MaskColumn, last, false).Height,
                    Is.GreaterThan(0),
                    "so there is nowhere under it for the dropdown to go");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void The_rule_picker_opens_on_a_row_above_the_ones_on_screen_as_well()
        {
            // A profile sorting before every other one lands on the first row, and the table can
            // be scrolled past that as easily as it can be short of the last one. Scrolling only
            // downwards leaves that row with no rectangle either, and the picker then either goes
            // to the grid's origin or is refused on a table with plenty of room.
            Cramped((form, panel, grid) =>
            {
                grid.FirstDisplayedScrollingRowIndex = grid.Rows.Count - 1;
                OffScreenForm.Pump();

                Assert.That(grid.Rows[0].Displayed, Is.False, "the table did not scroll");

                panel.OpenPicker(0);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.True, "nothing opened at all");
                Assert.That(grid.Rows[0].Displayed, Is.True);
                Assert.That(panel.Warning, Is.Null, "and it said there was no room on a table with room");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_rule_chosen_on_a_row_that_had_to_be_scrolled_to_is_still_on_the_table()
        {
            // The other half of the same rebuild: the row the operator just wrote a rule on goes
            // off the bottom with the rest of them, so the table reads exactly as it did before
            // the edit and nothing on it says what happened.
            Cramped((form, panel, grid) =>
            {
                var last = grid.Rows.Count - 2;

                panel.OpenPicker(last);
                OffScreenForm.Pump();

                panel.Type(Keys.C);
                panel.Type(Keys.Enter);
                OffScreenForm.Pump();

                Assert.That(Pressed, Is.True);
                Assert.That(grid.Rows[last].Displayed, Is.True,
                    "the rule was written and scrolled away in the one gesture");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_grant_the_seal_refuses_is_not_drawn_like_one_in_force()
        {
            // Off the pixels, because the difference has to be on the screen rather than in a
            // style nobody looks at. Both tables hold the same row, the same text and the same
            // gray, and the only rule on either comes from the group above; what the seal adds is
            // the line drawn through the mask that no longer arrives.
            var arriving = Ink(null);
            var refused = Ink(Rules.Node(true));

            Assert.That(refused, Is.GreaterThan(arriving),
                "a grant the seal is refusing is drawn exactly like one in force");
        }

        [Test]
        public void Confirming_a_grant_the_seal_refuses_lets_it_through_as_it_is()
        {
            // The struck row's tooltip says to choose a rule here to let it through, and the
            // picker opens with the rule above already checked. Pressing Enter on those check
            // marks wrote nothing, because they matched what the picker opened with, so the one
            // thing the row offered did nothing and said nothing.
            Sealed(
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Type(Keys.Enter), Is.True);
                    OffScreenForm.Pump();

                    Assert.That(Pressed, Is.True, "the rule is this node's own now");
                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.." }));

                    var font = grid.Rows[0].Cells[MaskColumn].Style.Font;
                    Assert.That(font == null || !font.Strikeout, Is.True, "and no longer refused");
                });
        }

        [Test]
        public void Glancing_at_a_grant_the_seal_refuses_and_clicking_away_writes_nothing()
        {
            // The other half: clicking away is how a dropdown is left after a glance, and a
            // glance must not undo a seal.
            Sealed(
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(Pressed, Is.False);
                    Assert.That(grid.Rows[0].Cells[MaskColumn].Style.Font.Strikeout, Is.True);
                });
        }

        [Test]
        public void A_version_of_a_group_and_a_limit_offer_no_list_and_the_inherited_row_of_a_cut_does()
        {
            // A row of a group offers its X and no list, `STORE.md` §657: no single press on a
            // group grants more than some version of the group held. A limit offers its X alone
            // too, and a rule is chosen on the inherited row above it.
            Using(
                Rules.Stored(("ops", "row:R"), ("ops", "row:RL"), ("reader", "off:R")),
                ReaderAbove(),
                (form, panel, grid) =>
                {
                    Assert.That(panel.Lines, Is.EqualTo(new[]
                    {
                        "ops .R...... " + GrantTab.UnmergedWord,
                        "ops .R...L.. " + GrantTab.UnmergedWord,
                        "reader .R...L.. " + GrantTab.InheritedWord,
                        "reader .R...... " + GrantTab.AtMostWord,
                    }));

                    foreach (var bound in new[] { 0, 1, 3 })
                    {
                        panel.Press(bound, MaskColumn);
                        OffScreenForm.Pump();
                        Assert.That(panel.Picking, Is.False, panel.Lines[bound]);
                    }

                    panel.Press(2, MaskColumn);
                    OffScreenForm.Pump();
                    Assert.That(panel.Picking, Is.True, "a rule is chosen on the inherited row of a cut");
                    Assert.That(_loop, Is.Empty);
                });
        }

        [Test]
        public void Enter_on_a_lost_row_with_nothing_checked_chooses_an_empty_rule()
        {
            // Enter is no glance: an empty rule is a rule, and replaces the lost version.
            Using(Rules.Stored(("reader", null)), ReaderAbove(), (form, panel, grid) =>
            {
                panel.OpenPicker(0);
                OffScreenForm.Pump();
                Assert.That(panel.Picking, Is.True, "no picker opened on the lost row");
                panel.Type(Keys.Enter);
                OffScreenForm.Pump();

                var row = _sheet.Rows.Single();
                Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Own), "an empty rule replaces the lost version");
                Assert.That(row.Changed, Is.True);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void An_X_on_a_rule_equal_to_the_inherited_rule_is_taken_back_by_choosing_the_rule_again()
        {
            Using(Rules.Node(false, ("reader", "RL")), ReaderAbove(), (form, panel, grid) =>
            {
                panel.Press(0, ForgetColumn);
                Assert.That(_sheet.Rows.Single().Changed, Is.True, "the X took the rule off");

                panel.OpenPicker(0);
                OffScreenForm.Pump();
                panel.Type(Keys.Enter);
                OffScreenForm.Pump();

                Assert.That(_sheet.Rows.Single().Kind, Is.EqualTo(GrantRowKind.Own));
                Assert.That(_sheet.Changes.IsEmpty, Is.True, "the rule chosen again takes the X back");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_press_whose_rows_are_drawn_again_before_the_release_is_taken_for_no_row()
        {
            // The press goes down on the X of a row further down, which closes the picker; what the
            // picker chose turns the inherited row and its limit into a single row before the
            // release arrives, so the row under the pointer at the release is the next one. Posted
            // a message at a time, as a hand makes them, with the loop run between: the table
            // raises no click for a release over rows drawn since the press went down.
            Using(
                Rules.Stored(("aa", "row:R"), ("reader", "off:R"), ("x1", "row:R"), ("x2", "row:R")),
                ReaderAbove(),
                (form, panel, grid) =>
                {
                    Assert.That(panel.Lines.Count, Is.EqualTo(5), string.Join(" | ", panel.Lines));
                    var downs = new List<int>();
                    var clicks = new List<int>();
                    grid.CellMouseDown += (sender, e) => downs.Add(e.RowIndex);
                    grid.CellClick += (sender, e) => clicks.Add(e.RowIndex);

                    grid.Focus();
                    panel.Press(1, MaskColumn);
                    OffScreenForm.Pump();
                    Assert.That(panel.Picking, Is.True, "no picker opened on the inherited row");
                    panel.Type(Keys.C);

                    var cell = grid.GetCellDisplayRectangle(ForgetColumn, 3, false);
                    var where = (IntPtr)(((cell.Top + (cell.Height / 2)) << 16) | ((cell.Left + (cell.Width / 2)) & 0xFFFF));
                    TestNativeMethods.PostMessage(grid.Handle, ButtonDownMessage, (IntPtr)1, where);
                    OffScreenForm.Pump();
                    Assert.That(panel.Picking, Is.False, "the press did not close the picker");
                    Assert.That(downs, Is.EqualTo(new[] { 3 }), "the press reached the table on the row of x1");
                    Assert.That(panel.Lines.Count, Is.EqualTo(4), "the choice was not drawn before the release: " + string.Join(" | ", panel.Lines));
                    TestNativeMethods.PostMessage(grid.Handle, ButtonUpMessage, IntPtr.Zero, where);
                    OffScreenForm.Pump();

                    Assert.That(clicks, Is.Empty, "a release over rows drawn since the press was taken as a click");
                    Assert.That(_sheet.Rows.Where(row => row.Changed).Select(row => row.Profile), Is.EqualTo(new[] { "reader" }),
                        "a press reached a row it did not go down on: " + string.Join(" | ", panel.Lines));
                    Assert.That(_loop, Is.Empty);
                });
        }

        private const int ProfileColumn = GrantTab.ProfileColumn;
        private const int MaskColumn = GrantTab.MaskColumn;
        private const int ForgetColumn = GrantTab.ForgetColumn;

        /// <summary>
        /// A window too short to show every row, which is about what a group dialog gives this tab.
        /// </summary>
        /// <remarks>
        /// Not a measurement and not asserted as one: what the group dialog's page comes to is
        /// `GrantTabFitsTests`'s question, it is a different number on every display, and this only
        /// has to be small enough that some row is off the bottom. Each test that uses it says so
        /// before it does anything else, so a window that stopped being cramped fails here rather
        /// than quietly testing the ordinary case twice.
        /// </remarks>
        private static readonly Size TooShortForEveryRow = new Size(356, 172);

        /// <summary>How many rules the cramped window is given, which is more than it can show.</summary>
        private const int MoreRulesThanFit = 12;

        private void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            _loop.Add(e.Exception);
        }

        /// <summary>
        /// An accept button that throws, once told to, when it is made the dialog's default or when
        /// it stops being it.
        /// </summary>
        /// <remarks>
        /// What the user interface throws, and from where a picker meets it: a picker takes the
        /// dialog's accept button away as it opens and gives it back as its close finishes.
        /// </remarks>
        private sealed class RefusingButton : Button
        {
            public const string Refusal = "the default button would not change";

            /// <summary>
            /// Which change throws: true for becoming the default, false for no longer being it,
            /// and null for neither.
            /// </summary>
            public bool? Refuses { get; set; }

            public override void NotifyDefault(bool value)
            {
                if (Refuses == value)
                {
                    throw new InvalidOperationException(Refusal);
                }

                base.NotifyDefault(value);
            }
        }

        /// <summary>
        /// Runs the message loop until it has happened, or long enough to tell it did not.
        /// </summary>
        private static bool PumpUntil(Func<bool> happened, TimeSpan patience)
        {
            var until = DateTime.UtcNow + patience;
            while (DateTime.UtcNow < until)
            {
                if (happened())
                {
                    return true;
                }

                Application.DoEvents();
                Thread.Sleep(10);
            }

            return happened();
        }

        /// <summary>Puts a key on the queue for a control, and lets the loop deliver it.</summary>
        /// <remarks>
        /// Posted rather than sent, and not `Press`: what is measured is the key's own way into the
        /// table, the grid's key handler, which a call on the panel never reaches.
        /// </remarks>
        private static void PressKey(Control control, Keys key)
        {
            TestNativeMethods.PostMessage(control.Handle, KeyDownMessage, (IntPtr)(int)key, IntPtr.Zero);
            TestNativeMethods.PostMessage(control.Handle, KeyUpMessage, (IntPtr)(int)key, IntPtr.Zero);
            OffScreenForm.Pump();
        }

        /// <summary>
        /// Puts a press and a release of the left button on the queue for a control, at the middle
        /// of an area of it, and lets the loop deliver them.
        /// </summary>
        private static void PressAt(Control control, Rectangle area)
        {
            var where = (IntPtr)(((area.Top + (area.Height / 2)) << 16) | ((area.Left + (area.Width / 2)) & 0xFFFF));
            TestNativeMethods.PostMessage(control.Handle, ButtonDownMessage, (IntPtr)1, where);
            TestNativeMethods.PostMessage(control.Handle, ButtonUpMessage, IntPtr.Zero, where);
            OffScreenForm.Pump();
        }

        private const int KeyDownMessage = 0x0100;
        private const int KeyUpMessage = 0x0101;
        private const int ButtonDownMessage = 0x0201;
        private const int ButtonUpMessage = 0x0202;
        private const int WheelMessage = 0x020A;

        /// <summary>The sheet of the panel the last `Using` showed.</summary>
        private static GrantSheet _sheet;

        /// <summary>Keeps a sheet as the sheet of the panel shown, and hands the sheet back.</summary>
        private static GrantSheet Shown(GrantSheet sheet)
        {
            _sheet = sheet;
            return sheet;
        }

        /// <summary>Whether a press on the shown panel left a change on its sheet.</summary>
        private static bool Pressed
        {
            get { return _sheet.Rows.Any(row => row.Changed) || _sheet.BoxChanged; }
        }

        /// <summary>A node granting a mask to each of as many profiles as asked for, named `profile-00` onwards.</summary>
        private static NodeRule Numbered(int count, string mask)
        {
            return Rules.Node(
                false,
                Enumerable.Range(0, count)
                    .Select(i => ("profile-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), mask))
                    .ToArray());
        }

        /// <summary>What arrives under a group granting `reader` read and list.</summary>
        private static AclInheritance ReaderAbove()
        {
            return Sheets.Arriving(("reader", "RL"));
        }

        private static void Using(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(null, work);
        }

        /// <summary>A node whose only rule comes from the group above it.</summary>
        private static void Inheriting(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(null, ReaderAbove(), work);
        }

        /// <summary>A node whose box refuses, under a group granting a profile, which the box refuses.</summary>
        private static void Sealed(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(Rules.Node(true), ReaderAbove(), work);
        }

        private static void Using(NodeRule rule, Action<Form, GrantPanel, DataGridView> work)
        {
            Using(rule, null, work);
        }

        /// <summary>
        /// A shown window with the editor on it, torn down whatever the test does.
        /// </summary>
        /// <remarks>
        /// Shown rather than merely constructed, because a dropdown needs somewhere to appear and a
        /// grid that has never been drawn reports no cell to put a dropdown under. That is the
        /// difference between this fixture and the fixture next door, and the difference that
        /// matters.
        /// </remarks>
        private static void Using(
            NodeRule rule, AclInheritance above, Action<Form, GrantPanel, DataGridView> work)
        {
            Using(rule, above, Size.Empty, work);
        }

        /// <summary>
        /// A window carrying more rules than it can show, for what happens off the bottom of a
        /// table.
        /// </summary>
        private static void Cramped(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(Numbered(MoreRulesThanFit, "RL"), null, TooShortForEveryRow, work);
        }

        /// <summary>The same, at a size of the test's choosing.</summary>
        /// <param name="size">
        /// How big the window is, or an empty size for the ordinary window. A short window is the
        /// only way a row comes to be off the bottom of the table, and every other test here wants
        /// the room rather than the crowding.
        /// </param>
        private static void Using(
            NodeRule rule, AclInheritance above, Size size, Action<Form, GrantPanel, DataGridView> work)
        {
            _sheet = GrantSheet.Of(rule, above, new[] { "reader", "ops" }, null);

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(_sheet, GrantTab.GroupScope, null, null, null))
            {
                if (!size.IsEmpty)
                {
                    form.ClientSize = size;
                }

                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(grid, Is.Not.Null);

                try
                {
                    work(form, panel, grid);
                }
                finally
                {
                    panel.ClosePicker();
                    OffScreenForm.Pump();
                    form.Close();
                    OffScreenForm.Pump();
                }
            }
        }

        /// <summary>
        /// How much is drawn in the mask of a node's single inherited rule, with the node as given.
        /// </summary>
        private static int Ink(NodeRule rule)
        {
            var drawn = 0;

            Using(rule, ReaderAbove(), (form, panel, grid) => drawn = Marks(grid, MaskColumn, 0));

            return drawn;
        }

        /// <summary>How many pixels in a cell are darker than the paper under them.</summary>
        private static int Marks(DataGridView grid, int column, int rowIndex)
        {
            var area = grid.GetCellDisplayRectangle(column, rowIndex, false);
            area.Inflate(-1, -1);

            Assert.That(area.Width, Is.GreaterThan(0), "the cell was never laid out");

            using (var picture = new Bitmap(grid.Width, grid.Height))
            {
                grid.DrawToBitmap(picture, new Rectangle(0, 0, grid.Width, grid.Height));

                var marks = 0;
                for (var x = area.Left; x < area.Right; x++)
                {
                    for (var y = area.Top; y < area.Bottom; y++)
                    {
                        var pixel = picture.GetPixel(x, y);
                        if (((pixel.R + pixel.G + pixel.B) / 3) < 224)
                        {
                            marks++;
                        }
                    }
                }

                return marks;
            }
        }

        /// <summary>The darkest pixel drawn inside a cell, ignoring the line around it.</summary>
        private static int Darkest(DataGridView grid, int column, int rowIndex)
        {
            var area = grid.GetCellDisplayRectangle(column, rowIndex, false);
            area.Inflate(-1, -1);

            Assert.That(area.Width, Is.GreaterThan(0), "the cell was never laid out");

            using (var picture = new Bitmap(grid.Width, grid.Height))
            {
                grid.DrawToBitmap(picture, new Rectangle(0, 0, grid.Width, grid.Height));

                var darkest = 255;
                for (var x = area.Left; x < area.Right; x++)
                {
                    for (var y = area.Top; y < area.Bottom; y++)
                    {
                        var pixel = picture.GetPixel(x, y);
                        var value = (pixel.R + pixel.G + pixel.B) / 3;
                        if (value < darkest)
                        {
                            darkest = value;
                        }
                    }
                }

                return darkest;
            }
        }

    }
}
