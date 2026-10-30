using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
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
        public void A_picker_opened_before_the_last_has_finished_closing_leaves_the_dialog_its_keys()
        {
            // A picker finishes closing a message later, and a click landing in between opens the
            // next one. Each took the dialog's buttons for itself, so the first gave OK and Cancel
            // back under the second, and the second then put back the nothing it had found.
            Using(
                Rules.Granting(("reader", "R")),
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

        [Test]
        public void A_verb_can_be_typed_the_moment_the_picker_opens()
        {
            // It could not, and the reason was not a focus bug to work around: while a dropdown is
            // up, `ToolStripManager.ModalMenuFilter` routes keystrokes to the dropdown rather than
            // to whatever is inside it, so a handler on the list heard nothing until a click had
            // moved the focus into it. The letters worked, but only after checking one with the
            // pointer first.
            Using(
                Rules.Granting(("reader", "")),
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

                    Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.." }));
                    Assert.That(panel.Writes, Is.Zero, "nothing was checked, so nothing was decided");
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
                    Assert.That(panel.Writes, Is.EqualTo(1));
                });
        }

        [Test]
        public void A_rule_the_sheet_refuses_is_said_rather_than_swallowed()
        {
            // A profile name no certificate could carry. The reader takes any member name, so a
            // document written by hand can hold one and the table shows it. The picker then
            // opened on it, took a rule, and closed without writing or saying anything.
            Using(
                "{\"Default\":[\"read\"]}",
                (form, panel, grid) =>
                {
                    panel.Press(0, MaskColumn);
                    OffScreenForm.Pump();

                    panel.Type(Keys.L);
                    panel.ClosePicker();
                    OffScreenForm.Pump();

                    Assert.That(panel.Warning, Is.Not.Null.And.Not.Empty,
                        "a picker that closes on a refusal it never mentions is a control that did nothing");
                    Assert.That(panel.Writes, Is.Zero);
                });
        }

        [Test]
        public void A_rule_whose_name_arrived_with_spaces_round_it_can_still_be_taken_off()
        {
            // The editor cannot write such a name, but the reader accepts any member name, so a
            // hand-written document can carry one. The table draws it trimmed, and the X used to
            // ask for the trimmed name. That is not the key the rule is under, so the button
            // removed nothing and said nothing. The name is carried on the row now.
            Using(
                "{\" reader \":[\"read\"]}",
                (form, panel, grid) =>
                {
                    Assert.That(panel.Lines.Count, Is.EqualTo(1));

                    panel.Press(0, ForgetColumn);
                    OffScreenForm.Pump();

                    Assert.That(panel.Lines, Is.Empty);
                    Assert.That(panel.Writes, Is.EqualTo(1));
                });
        }

        [Test]
        public void Escape_leaves_the_rule_where_it_was()
        {
            Using(
                Rules.Granting(("reader", "R")),
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
                Rules.Granting(("reader", "")),
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
                Rules.Granting(("reader", "R")),
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
                Rules.Granting(("reader", "R")),
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
                Rules.Granting(("reader", "R")),
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
                Rules.Granting(("reader", "R")),
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
        public void A_database_naming_no_profiles_says_so_twice_rather_than_once()
        {
            // The state a database is in before anything has been set up at all: no authority, no
            // client, no profile. Pressing the cell has nothing to open, and pressing it again has
            // to be as harmless as the first time.
            var store = new FakeGrantStore(null);

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                new GrantSheet(null, null, null, null), store, GrantTab.GroupScope, null))
            {
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();
                panel.Press(grid.NewRowIndex, ProfileColumn);
                OffScreenForm.Pump();

                Assert.That(panel.Picking, Is.False);
                Assert.That(panel.Warning, Is.EqualTo(GrantTab.NoProfilesYet));
                Assert.That(form.IsDisposed, Is.False);
                Assert.That(_loop, Is.Empty);

                form.Close();
                OffScreenForm.Pump();
            }
        }

        [Test]
        public void A_table_that_cannot_be_changed_can_still_be_scrolled_to_its_last_rule()
        {
            // An entry's earlier version, several entries at once: the tab says nothing can be
            // changed and that the rules stay readable. The table was disabled for it, and a
            // disabled grid takes neither its scroll bar nor the wheel, so only the rules that fit
            // could be read.
            var store = new FakeGrantStore(Rules.Numbered(MoreRulesThanFit, "RL"));

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                new GrantSheet(store.Read(), null, null, "This is an earlier version of the entry."),
                store,
                GrantTab.EntryScope,
                null))
            {
                form.ClientSize = TooShortForEveryRow;
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(grid.Rows[grid.Rows.Count - 1].Displayed, Is.False,
                    "the window is not short enough for this to be about anything");

                User32.PostMessage(grid.Handle, WheelMessage, (IntPtr)(-120 << 16), IntPtr.Zero);
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
                Assert.That(store.Writes, Is.Zero);
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

                Assert.That(panel.Writes, Is.EqualTo(1));
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
            var refused = Ink(Rules.Sealing());

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

                    Assert.That(panel.Writes, Is.EqualTo(1), "the rule is this node's own now");
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

                    Assert.That(panel.Writes, Is.Zero);
                    Assert.That(grid.Rows[0].Cells[MaskColumn].Style.Font.Strikeout, Is.True);
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

        /// <summary>Puts a key on the queue for a control, and lets the loop deliver it.</summary>
        /// <remarks>
        /// Posted rather than sent, and not `Press`: what is measured is the key's own way into the
        /// table, the grid's key handler, which a call on the panel never reaches.
        /// </remarks>
        private static void PressKey(Control control, Keys key)
        {
            User32.PostMessage(control.Handle, KeyDownMessage, (IntPtr)(int)key, IntPtr.Zero);
            User32.PostMessage(control.Handle, KeyUpMessage, (IntPtr)(int)key, IntPtr.Zero);
            OffScreenForm.Pump();
        }

        private const int KeyDownMessage = 0x0100;
        private const int KeyUpMessage = 0x0101;
        private const int WheelMessage = 0x020A;

        private static void Using(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(null, work);
        }

        /// <summary>A node whose only rule comes from the group above it.</summary>
        private static void Inheriting(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(null, TestNode.Root("a group above").Granting("reader", AclVerb.Read, AclVerb.List), work);
        }

        /// <summary>A sealed node under a group granting one profile, which the seal refuses.</summary>
        private static void Sealed(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(
                Rules.Sealing(),
                TestNode.Root("a group above").Granting("reader", AclVerb.Read, AclVerb.List),
                work);
        }

        private static void Using(string stored, Action<Form, GrantPanel, DataGridView> work)
        {
            Using(stored, null, work);
        }

        /// <summary>
        /// A shown window with the editor on it, torn down whatever the test does.
        /// </summary>
        /// <remarks>
        /// Shown rather than merely constructed, because a dropdown needs somewhere to appear and a
        /// grid that has never been drawn reports no cell to put one under. That is the difference
        /// between this fixture and the one next door, and it is the difference that matters.
        /// </remarks>
        private static void Using(
            string stored, IAclNode above, Action<Form, GrantPanel, DataGridView> work)
        {
            Using(stored, above, Size.Empty, work);
        }

        /// <summary>
        /// A window carrying more rules than it can show, for what happens off the bottom of one.
        /// </summary>
        private static void Cramped(Action<Form, GrantPanel, DataGridView> work)
        {
            Using(Rules.Numbered(MoreRulesThanFit, "RL"), null, TooShortForEveryRow, work);
        }

        /// <param name="size">
        /// How big the window is, or an empty size for the ordinary one. A short window is the only
        /// way a row comes to be off the bottom of the table, and every other test here wants the
        /// room rather than the crowding.
        /// </param>
        private static void Using(
            string stored, IAclNode above, Size size, Action<Form, GrantPanel, DataGridView> work)
        {
            var store = new FakeGrantStore(stored);

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                new GrantSheet(store.Read(), above, new[] { "reader", "ops" }, null),
                store,
                GrantTab.GroupScope,
                null))
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
        /// How much is drawn in the mask of a node's one inherited rule, with the node as given.
        /// </summary>
        private static int Ink(string stored)
        {
            var drawn = 0;

            Using(
                stored,
                TestNode.Root("a group above").Granting("reader", AclVerb.Read, AclVerb.List),
                (form, panel, grid) => drawn = Marks(grid, MaskColumn, 0));

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
