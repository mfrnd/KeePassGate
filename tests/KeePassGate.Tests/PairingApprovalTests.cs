using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The approval dialog, built and pressed without ever putting a window on the screen.
    /// </summary>
    /// <remarks>
    /// What is being tested is the part that decides: which button leads to which answer, what the
    /// dialog is showing when it does, and the fact that nothing at all leads to an approval by
    /// default. The end to end tier presses these buttons for real.
    /// </remarks>
    // Alone, because some of these show the dialog and ask where the focus went, and a window
    // another fixture shows meanwhile takes it.
    [TestFixture]
    [NonParallelizable]
    [Desktop]
    [Apartment(ApartmentState.STA)]
    public sealed class PairingApprovalFormTests
    {
        /// <summary>How wide the smallest screen the dialog is held to is, at 100%.</summary>
        private const int SmallestScreenWidth = 1024;

        /// <summary>
        /// How tall the working area of the smallest screen the dialog is held to is, at 100%: a
        /// screen 768 pixels tall less a taskbar of 48.
        /// </summary>
        private const int SmallestWorkingHeight = 720;

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        [Test]
        public void Every_control_on_it_carries_a_name()
        {
            using (var dialog = New("reader", "ops"))
            {
                Assert.That(Unnamed.On(dialog), Is.Empty);
            }
        }

        [Test]
        public void The_two_answers_sit_on_the_same_line()
        {
            // A flow panel places each control at the top of its row plus that control's own top
            // margin, so a button left with the default margin sits below one that was given none.
            // It fits, it works, and it is the first thing anybody notices about the dialog.
            using (var dialog = New("ops"))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                var refuse = ControlTree.FindByName<Button>(dialog, GateTab.RefuseButtonName);
                var approve = ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName);

                Assert.That(refuse, Is.Not.Null);
                Assert.That(approve, Is.Not.Null);
                Assert.That(approve.Top, Is.EqualTo(refuse.Top));
                Assert.That(approve.Height, Is.EqualTo(refuse.Height));
            }
        }

        [Test]
        [TestCase(1.00f, 3)]
        [TestCase(1.25f, 3)]
        [TestCase(1.50f, 3)]
        [TestCase(2.00f, 3)]
        [TestCase(1.00f, 32)]
        [TestCase(1.25f, 32)]
        [TestCase(1.50f, 32)]
        [TestCase(2.00f, 32)]
        public void Every_profile_asked_for_can_be_seen_and_unchecked_at_any_scaling(float scaling, int asked)
        {
            // The dialog used to be a fixed 460 by 340 with the list filling whatever the two
            // docked bands left. Those bands grow with the font and the client area did not, so at
            // 125% the list showed one row of what was being asked for and at 150% it showed none,
            // while every profile stayed checked and Approve granted all of them. A dialog that
            // hides what it is granting is worse than one that is ugly.
            //
            // **Built at the larger font rather than given it afterwards**, because a scaled
            // display hands the dialog its font before it is built and everything is measured then.
            // Giving it the font afterwards measured a path no display takes, and this passed only
            // because an automatic scaling nothing else used scaled the result (H156).
            //
            // **And a list longer than the rows it shows**, which is the other way to hide what
            // Approve grants: 32 profiles is the most a request carries, all checked, ten on
            // screen. Every profile can still be reached, and a line cut in half at the bottom of
            // the table shows that more are there (J056). The dialog fits the smallest screen.
            var names = Enumerable.Range(1, asked).Select(at => "profile-" + at).ToArray();

            using (var larger = Scaled(Control.DefaultFont, scaling))
            using (var dialog = New(larger, names))
            {
                ShownAway(dialog);

                var list = Profiles(dialog);
                var rows = ProfileRows.Of(list);
                var view = ProfileRows.View(list);

                Assert.That(rows.Count, Is.EqualTo(asked), "every profile is being asked for");
                Assert.That(Checks(list), Has.Length.EqualTo(asked).And.All.True, "and every profile arrives checked");
                Assert.That(view.ClientSize.Height, Is.GreaterThanOrEqualTo(rows[0].Height * Math.Min(asked, 10)),
                    "a table showing fewer rows than it was sized for is a table the operator cannot read");
                Assert.That(Scrolls(view, HorizontalScrollBar), Is.False, "the table scrolls sideways at " + scaling);

                Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table at " + scaling);
                if (asked > 10)
                {
                    AssertCutInHalf(list, asked + " profiles at " + scaling);
                    Assert.That(Scrolls(view, VerticalScrollBar), Is.True, "the table hides rows it cannot scroll to");

                    var last = Focus(BoxOf(list, asked - 1));
                    Assert.That(rows[asked - 1].Top, Is.GreaterThanOrEqualTo(0), "the last row starts above the view");
                    Assert.That(rows[asked - 1].Bottom, Is.LessThanOrEqualTo(view.ClientSize.Height),
                        "the table does not scroll to the rows it hides");
                    Assert.That(last.Checked, Is.True);
                }
                else
                {
                    Assert.That(rows[asked - 1].Bottom, Is.EqualTo(view.ClientSize.Height), "a view other than ending where its rows end at " + scaling);
                    Assert.That(Scrolls(view, VerticalScrollBar), Is.False);
                }

                foreach (var name in new[] { GateTab.ApproveButtonName, GateTab.RefuseButtonName })
                {
                    var button = ControlTree.FindByName<Button>(dialog, name);
                    Assert.That(button, Is.Not.Null, name + " is gone at " + scaling);
                    Assert.That(InDialog(button, dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                        name + " is off the bottom of the dialog at " + scaling);
                    Assert.That(InDialog(button, dialog).Right, Is.LessThanOrEqualTo(Asked(dialog).Width),
                        name + " is off the side of the dialog at " + scaling);
                }

                Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                    "the dialog is wider than the smallest screen at " + scaling);
                Assert.That(dialog.PreferredSize.Height, Is.LessThanOrEqualTo(SmallestWorkingHeight * scaling),
                    "the dialog is taller than the smallest screen at " + scaling);
                Assert.That(InDialog(list, dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                    "the list is off the bottom of the dialog at " + scaling);

                // The lifetime is part of what Approve grants, so it is held to the same room.
                var lifetime = Lifetime(dialog);
                var row = lifetime.Parent;
                Assert.That(InDialog(row, dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                    "the lifetime is off the bottom of the dialog at " + scaling);
                Assert.That(InDialog(row, dialog).Right, Is.LessThanOrEqualTo(Asked(dialog).Width),
                    "the lifetime is off the side of the dialog at " + scaling);
                Assert.That(lifetime.Width, Is.GreaterThan(
                        lifetime.Items.Cast<string>().Max(text => TextRenderer.MeasureText(text, larger).Width)),
                    "the longest choice is cut at " + scaling);
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(2.00f)]
        public void A_profile_as_long_as_a_name_may_be_leaves_its_state_whole_in_a_column_of_its_own(float scaling)
        {
            // The client chooses its profile names, up to sixty-four letters, digits, hyphens and
            // underscores. A name that long is cut at the edge of its column, and what the operator
            // acts on, that approving adds the name or gives it back to the clients holding it,
            // is in a column of its own that no name reaches, held whole at every scaling.
            var added = new string('W', 64);
            var returned = "W-" + new string('W', 62);

            using (var authority = TestAuthority.New())
            using (var holder = authority.IssueClient("ci-runner"))
            using (var larger = Scaled(Control.DefaultFont, scaling))
            {
                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "reader", added, returned }, new[] { "reader" }, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>>
                    {
                        { returned, new[] { AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { returned }, InventedIssuer.Id) } }
                    });

                using (var dialog = new PairingApprovalForm(request, Timeout, larger))
                {
                    ShownAway(dialog);

                    var list = Profiles(dialog);
                    Assert.That(Rows(list), Is.EqualTo(new[]
                    {
                        new[] { "reader", string.Empty, string.Empty },
                        new[] { added, PairingApprovalForm.NewState, string.Empty },
                        new[] { returned, PairingApprovalForm.RemovedState, "ci-runner" }
                    }));

                    StatesWhole(list);
                    BoxFirst(list);
                    InItsRoom(list);
                    Assert.That(CellOf(list, 1, GateTab.ApprovalProfileColumnName).Width,
                        Is.LessThan(TextRenderer.MeasureText(added, list.Font).Width), "a name this long is cut at " + scaling);

                    var approve = Button(dialog, GateTab.ApproveButtonName);
                    Assert.That(InDialog(approve, dialog).Right, Is.EqualTo(InDialog(list, dialog).Right),
                        "the buttons do not end where the table does at " + scaling);
                    Assert.That(InDialog(list, dialog).Right, Is.LessThanOrEqualTo(Asked(dialog).Width),
                        "the table is off the side of the dialog at " + scaling);
                    Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                        "the dialog is wider than the smallest screen at " + scaling);
                }
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void The_notice_of_a_dialog_shown_again_fits_beside_the_longest_of_every_other_line(float scaling)
        {
            // Every line and every cell at its longest at once: as many profiles as a request
            // carries, each as long as a name may be, all but the last removed and held by more
            // clients than a row names, each client's name as long as a name may be, the last new,
            // so that both states are in the table, which scrolls, and the notice, which reads the
            // same whatever Approve found. Nothing is cut, nothing scrolls sideways, and the dialog
            // fits the smallest screen.
            var names = Enumerable.Range(0, 32)
                .Select(at => at.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + new string('W', 62))
                .ToArray();
            var removed = names.Take(31).ToArray();
            var added = names[31];

            using (var authority = TestAuthority.New())
            {
                using (var holders = new IssuedClients(Enumerable.Range(0, 15)
                    .Select(at => authority.IssueClient(new string('W', 62) + at.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))))
                {
                    var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, removed, InventedIssuer.Id)).ToList();
                    var returning = removed.ToDictionary(
                        name => name, name => (IReadOnlyList<AdmissionRecord>)records, StringComparer.Ordinal);
                    var request = new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", names, new[] { "ops" },
                        "127.0.0.1:60001", returning, TimeSpan.FromDays(365), removed, PairingApproval.Approved(names, TimeSpan.FromDays(365)));
                    var heldBy = string.Join(Environment.NewLine, ClientList.ClientNames(records));

                    using (var larger = Scaled(Control.DefaultFont, scaling))
                    using (var dialog = new PairingApprovalForm(request, Timeout, larger))
                    {
                        ShownAway(dialog);

                        // Every text from the product, the notice's among them, so that what is
                        // measured is whatever the dialog states at the time.
                        var notice = Line(dialog, GateTab.ApprovalAgainName, PairingApprovalForm.Again(request), scaling);
                        Line(dialog, "KpgApprovalAskingLabel", PairingApprovalForm.Asking(request), scaling);

                        var list = Profiles(dialog);
                        Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table");
                        AssertCutInHalf(list, "every line at its longest at " + scaling);
                        Assert.That(InDialog(notice, dialog).Bottom, Is.LessThanOrEqualTo(InDialog(list, dialog).Top),
                            "the notice is not above the table at " + scaling);
                        Assert.That(
                            Rows(list),
                            Is.EqualTo(removed.Select(name => new[] { name, PairingApprovalForm.RemovedState, heldBy })
                                .Concat(new[] { new[] { added, PairingApprovalForm.NewState, string.Empty } })));
                        Assert.That(Scrolls(ProfileRows.View(list), VerticalScrollBar), Is.True, "the table hides rows it cannot scroll to");
                        StatesWhole(list);
                        BoxFirst(list);
                        InItsRoom(list);

                        foreach (var name in new[] { GateTab.ApproveButtonName, GateTab.RefuseButtonName })
                        {
                            Assert.That(InDialog(Button(dialog, name), dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                                name + " is off the bottom of the dialog at " + scaling);
                        }

                        Assert.That(InDialog(Lifetime(dialog).Parent, dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                            "the lifetime is off the bottom of the dialog at " + scaling);
                        Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                            "the dialog is wider than the smallest screen at " + scaling);
                        Assert.That(dialog.PreferredSize.Height, Is.LessThanOrEqualTo(SmallestWorkingHeight * scaling),
                            "the dialog is taller than the smallest screen at " + scaling);

                        TestContext.Out.WriteLine(
                            "at " + scaling.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                            + " the dialog shown again asks for " + dialog.PreferredSize + ", the table " + list.Size
                            + " in cells of " + string.Join(", ", ProfileRows.Of(list)[0].Controls.Cast<Control>().Select(cell => cell.Width))
                            + ", the notice " + notice.Size + ": " + notice.Text);
                    }
                }
            }
        }

        /// <summary>The names of the lines of text the dialog shows under its table, in order.</summary>
        private static IReadOnlyList<string> LinesUnder(Form dialog)
        {
            var layout = ControlTree.FindByName<FlowLayoutPanel>(dialog, "KpgApprovalLayout");
            var controls = layout.Controls.Cast<Control>().ToList();
            return controls.Skip(controls.IndexOf(Profiles(dialog)) + 1).OfType<Label>()
                .Where(line => !string.IsNullOrEmpty(line.Text))
                .Select(line => line.Name)
                .ToList();
        }

        /// <summary>
        /// Asserts that a line of the dialog holds this text, whole and within the dialog.
        /// </summary>
        private static Label Line(Form dialog, string name, string text, float scaling)
        {
            var line = ControlTree.FindByName<Label>(dialog, name);
            Assert.That(line, Is.Not.Null, "no line " + name + " at " + scaling);
            Assert.That(line.Text, Is.EqualTo(text), name);
            Assert.That(InDialog(line, dialog).Right, Is.LessThanOrEqualTo(Asked(dialog).Width),
                name + " is off the side of the dialog at " + scaling);
            Assert.That(InDialog(line, dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                name + " is off the bottom of the dialog at " + scaling);
            Assert.That(
                TextRenderer.MeasureText(line.Text, line.Font, new Size(line.Width, 0), TextFormatFlags.WordBreak).Height,
                Is.LessThanOrEqualTo(line.Height),
                name + " is cut at " + scaling);
            return line;
        }

        /// <summary>
        /// Asserts that the column of states holds every state and its heading whole, which no
        /// name and no list of clients beside it can push out.
        /// </summary>
        private static void StatesWhole(Control list)
        {
            var heading = ControlTree.FindByName<Label>(list, GateTab.ApprovalStateColumnName);
            Assert.That(heading, Is.Not.Null, "no column of states");
            Assert.That(heading.Text, Is.EqualTo(PairingApprovalForm.StateHeading));
            var cells = ProfileRows.Of(list).Select(row => ProfileRows.Cell(row, GateTab.ApprovalStateColumnName)).ToList();
            foreach (var cell in cells.Concat(new[] { heading }))
            {
                foreach (var text in new[] { PairingApprovalForm.NewState, PairingApprovalForm.RemovedState, heading.Text })
                {
                    var width = TextRenderer.MeasureText(text, list.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                    Assert.That(width, Is.LessThanOrEqualTo(cell.Width),
                        "\"" + text + "\" is " + width + " pixels in a cell " + cell.Width + " wide");
                }
            }
        }

        /// <summary>
        /// Asserts that the column of check boxes is the first, under its heading held whole, and
        /// that each row's box stands before the row's name and is read out by the row's text,
        /// which starts with the name.
        /// </summary>
        private static void BoxFirst(Control list)
        {
            var grant = ControlTree.FindByName<Label>(list, GateTab.ApprovalGrantColumnName);
            Assert.That(grant, Is.Not.Null, "no column of check boxes");
            Assert.That(grant.Text, Is.EqualTo(PairingApprovalForm.GrantHeading));
            Assert.That(TextRenderer.MeasureText(grant.Text, list.Font, Size.Empty, TextFormatFlags.NoPadding).Width,
                Is.LessThanOrEqualTo(grant.Width), "the heading of the check boxes is cut");
            foreach (var row in ProfileRows.Of(list))
            {
                var box = ProfileRows.Box(row);
                var name = ProfileRows.Cell(row, GateTab.ApprovalProfileColumnName);
                Assert.That(box, Is.Not.Null, "a row with no box");
                Assert.That(box.Right, Is.LessThanOrEqualTo(name.Left), "the box is not before the name");
                Assert.That(box.Right, Is.LessThanOrEqualTo(grant.Right + grant.Margin.Right), "the box is wider than its column");
                Assert.That(box.AccessibleName, Is.EqualTo(name.Text).Or.StartsWith(name.Text + ". "),
                    "what a screen reader reads of the box of " + name.Text + " is not its profile");
            }
        }

        /// <summary>
        /// Asserts that the rows fit the view and their cells the view with room left for a vertical
        /// scroll bar, so that the table never scrolls sideways, whether or not it scrolls down.
        /// </summary>
        private static void InItsRoom(Control list)
        {
            var view = ProfileRows.View(list);
            var room = view.ClientSize.Width - (Scrolls(view, VerticalScrollBar) ? 0 : SystemInformation.VerticalScrollBarWidth);
            foreach (var row in ProfileRows.Of(list))
            {
                Assert.That(row.Right, Is.LessThanOrEqualTo(view.ClientSize.Width), "a row " + row.Right + " wide in a view with " + view.ClientSize.Width);
                foreach (var cell in row.Controls.OfType<Label>())
                {
                    Assert.That(row.Left + cell.Right, Is.LessThanOrEqualTo(room), "a cell " + (row.Left + cell.Right) + " wide in a room of " + room);
                }
            }

            Assert.That(Scrolls(view, HorizontalScrollBar), Is.False, "the table scrolls sideways");
        }

        [Test]
        public void The_dialog_grows_with_the_font_rather_than_clipping()
        {
            // The other half of the same claim: the window is sized by what is in it, so a larger
            // font makes a larger dialog rather than the same dialog with less showing.
            using (var larger = Scaled(Control.DefaultFont, 1.5f))
            using (var ordinary = New("ops", "reader", "deploy"))
            using (var enlarged = New(larger, "ops", "reader", "deploy"))
            {
                ordinary.CreateControl();
                ordinary.PerformLayout();

                enlarged.CreateControl();
                enlarged.PerformLayout();

                Assert.That(enlarged.ClientSize.Height, Is.GreaterThan(ordinary.ClientSize.Height));
                Assert.That(enlarged.ClientSize.Width, Is.GreaterThan(ordinary.ClientSize.Width));
            }
        }

        /// <summary>The same font at a larger size, which is what a scaled display amounts to here.</summary>
        private static Font Scaled(Font font, float scaling)
        {
            return new Font(font.FontFamily, font.Size * scaling, font.Style);
        }

        /// <summary>
        /// Where a control sits in the dialog's client area, added up through its parents. A
        /// control's own bounds are in its parent's coordinates, and a button measured against the
        /// dialog inside the row that holds it can never be found off the side.
        /// </summary>
        private static Rectangle InDialog(Control control, Form dialog)
        {
            var where = control.Bounds;
            for (var parent = control.Parent; parent != null && parent != dialog; parent = parent.Parent)
            {
                where.Offset(parent.Location);
            }

            return where;
        }

        /// <summary>
        /// The client area the dialog asks for, which is the one it has until the screen it is on
        /// is too small for it: its preferred size is not held to the screen, its size is.
        /// </summary>
        private static Size Asked(Form dialog)
        {
            return dialog.PreferredSize - (dialog.Size - dialog.ClientSize);
        }

        [Test]
        public void A_dialog_nobody_touches_has_timed_out_rather_than_agreed()
        {
            using (var dialog = New())
            {
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut));
            }
        }

        [Test]
        public void Approving_approves_what_is_still_checked()
        {
            using (var dialog = New("ops", "admin"))
            {
                dialog.Approve();

                var answer = dialog.Answer();

                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
                Assert.That(answer.Profiles, Is.EqualTo(new[] { "ops", "admin" }));
            }
        }

        [Test]
        public void Unchecking_something_takes_it_away_from_what_is_approved()
        {
            using (var dialog = New("ops", "admin"))
            {
                BoxOf(Profiles(dialog), 1).Checked = false;
                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void Approving_with_everything_unchecked_is_an_admission_that_reaches_nothing()
        {
            using (var dialog = New("ops"))
            {
                BoxOf(Profiles(dialog), 0).Checked = false;
                dialog.Approve();

                var answer = dialog.Answer();

                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
                Assert.That(answer.Profiles, Is.Empty);
            }
        }

        [Test]
        public void The_lifetime_offered_first_is_the_one_asked_for_and_is_marked_as_asked()
        {
            using (var dialog = New(TimeSpan.FromDays(30), "ops"))
            {
                var lifetime = Lifetime(dialog);

                Assert.That(lifetime.Items.Cast<string>(),
                    Is.EqualTo(new[] { "1 day", "7 days", "30 days (asked)", "90 days", "365 days" }));
                Assert.That(lifetime.SelectedItem, Is.EqualTo("30 days (asked)"));
            }
        }

        [Test]
        public void A_lifetime_asked_for_that_is_no_preset_is_offered_as_a_choice_of_its_own()
        {
            using (var dialog = New(TimeSpan.FromDays(45), "ops"))
            {
                var lifetime = Lifetime(dialog);

                Assert.That(lifetime.Items.Cast<string>(),
                    Is.EqualTo(new[] { "1 day", "7 days", "30 days", "45 days (asked)", "90 days", "365 days" }),
                    "in its place by length, among the presets");
                Assert.That(lifetime.SelectedItem, Is.EqualTo("45 days (asked)"));
            }
        }

        [Test]
        public void A_client_that_asked_for_no_lifetime_is_offered_a_year()
        {
            using (var dialog = New("ops"))
            {
                var lifetime = Lifetime(dialog);

                Assert.That(lifetime.SelectedItem, Is.EqualTo("365 days"));
                Assert.That(lifetime.Items.Cast<string>(), Has.None.Contains(LifetimeChoice.AskedSuffix),
                    "nothing is marked as asked for when nothing was");
            }
        }

        [Test]
        public void A_list_with_nothing_chosen_gives_a_year_rather_than_the_shortest()
        {
            using (var dialog = New(TimeSpan.FromDays(30), "ops"))
            {
                Lifetime(dialog).SelectedIndex = -1;
                dialog.Approve();

                Assert.That(dialog.Answer().Lifetime, Is.EqualTo(Authority.Lifetime.Unasked));
            }
        }

        [Test]
        public void Approving_gives_the_certificate_the_lifetime_chosen()
        {
            // Asked for thirty days, given seven: what the client proposed is preselected and is
            // not what is approved unless the operator leaves it.
            using (var dialog = New(TimeSpan.FromDays(30), "ops"))
            {
                Lifetime(dialog).SelectedItem = "7 days";
                dialog.Approve();

                Assert.That(dialog.Answer().Lifetime, Is.EqualTo(TimeSpan.FromDays(7)));
            }
        }

        [Test]
        public void Approving_without_touching_the_lifetime_gives_what_was_preselected()
        {
            using (var asked = New(TimeSpan.FromDays(90), "ops"))
            using (var unasked = New("ops"))
            {
                asked.Approve();
                unasked.Approve();

                Assert.That(asked.Answer().Lifetime, Is.EqualTo(TimeSpan.FromDays(90)));
                Assert.That(unasked.Answer().Lifetime, Is.EqualTo(TimeSpan.FromDays(365)));
            }
        }

        [TestCase(Keys.Enter)]
        [TestCase(Keys.Escape)]
        public void A_key_pressed_on_the_closed_lifetime_list_still_reaches_the_refusal(Keys key)
        {
            // A closed list claims neither key, so the loop hands both to the dialog's own buttons,
            // and both of those refuse. Posted through a real loop, because the question is which
            // control the loop decides a key belongs to, and a key sent straight to a window has
            // skipped that question.
            using (var dialog = New(TimeSpan.FromDays(30), "ops"))
            {
                var lifetime = Focused(dialog);

                Press(lifetime, key);

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
            }
        }

        [TestCase(Keys.Enter)]
        [TestCase(Keys.Escape)]
        public void A_key_pressed_on_the_open_lifetime_list_is_the_lists_alone(Keys key)
        {
            // An open list takes Enter to pick and Escape to close, as every list does, so an
            // operator closing the choices is not refusing the client in the same keystroke.
            using (var dialog = New(TimeSpan.FromDays(30), "ops"))
            {
                var lifetime = Focused(dialog);
                lifetime.DroppedDown = true;
                OffScreenForm.Pump();

                Press(lifetime, key);

                Assert.That(lifetime.DroppedDown, Is.False, "the key closed the list");
                Assert.That(dialog.Visible, Is.True, "and nothing else");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut),
                    "nobody has answered yet");
            }
        }

        [Test]
        public void Approve_on_a_dialog_shown_again_takes_no_press_for_a_moment_through_the_real_loop()
        {
            // A dialog shown again comes up where the dialog before stood, just after Approve on it
            // was pressed, so a press still on its way would answer it unread. Approve is held back
            // for `ApproveHeldBack` from when the dialog comes up, by a timer on the dialog's own
            // loop, takes no press meanwhile, and is enabled without taking the focus.
            using (var dialog = new PairingApprovalForm(ShownAgain(), TimeSpan.FromMinutes(2)))
            {
                var approve = Button(dialog, GateTab.ApproveButtonName);
                var since = ShownOffScreen(dialog);

                Assert.That(approve.Enabled, Is.False, "Approve takes no press as the dialog comes up");
                approve.PerformClick();
                OffScreenForm.Pump();
                Assert.That(dialog.Visible, Is.True, "a press in flight answers nothing");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "nobody has answered yet");

                Assert.That(PumpUntil(() => approve.Enabled, TimeSpan.FromSeconds(10)), Is.True, "Approve is enabled again");
                Assert.That(since.Elapsed, Is.GreaterThanOrEqualTo(PairingApprovalForm.ApproveHeldBack - TimeSpan.FromMilliseconds(50)));
                Assert.That(approve.Focused, Is.False, "enabling it takes no focus");

                approve.PerformClick();
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Approved));
            }
        }

        [Test]
        public void Enter_on_a_dialog_shown_again_refuses_while_Approve_is_held_back()
        {
            using (var dialog = new PairingApprovalForm(ShownAgain(), TimeSpan.FromMinutes(2)))
            {
                var lifetime = Focused(dialog);
                Assert.That(Button(dialog, GateTab.ApproveButtonName).Enabled, Is.False);

                Press(lifetime, Keys.Enter);

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
            }
        }

        [Test]
        public void A_first_dialog_holds_Approve_back_for_no_moment_and_carries_no_notice()
        {
            using (var dialog = New("ops"))
            {
                ShownOffScreen(dialog);

                Assert.That(Button(dialog, GateTab.ApproveButtonName).Enabled, Is.True);
                Assert.That(ControlTree.FindByName<Label>(dialog, GateTab.ApprovalAgainName), Is.Null);
            }
        }

        /// <summary>
        /// A request whose earlier dialog's approval a write found stale: `ops` asked for and on the
        /// list, approved and marked the same before.
        /// </summary>
        private static PairingRequest ShownAgain()
        {
            var known = new[] { "ops" };
            return new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", known, known, "127.0.0.1:60001",
                null, null, null, PairingApproval.Approved(known));
        }

        /// <summary>
        /// The dialog shown where nobody sees it, and the time since it came up, once it has.
        /// </summary>
        private static System.Diagnostics.Stopwatch ShownOffScreen(PairingApprovalForm dialog)
        {
            var since = new System.Diagnostics.Stopwatch();
            dialog.Shown += delegate { since.Start(); };
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            Assert.That(PumpUntil(() => since.IsRunning, TimeSpan.FromSeconds(10)), Is.True, "the dialog came up");
            return since;
        }

        /// <summary>The dialog shown where nobody sees it, with the focus on the lifetime.</summary>
        private static ComboBox Focused(PairingApprovalForm dialog)
        {
            ShownAway(dialog);
            return Focus(Lifetime(dialog));
        }

        /// <summary>
        /// The dialog shown where nobody sees it, which is where its table has a window and its
        /// rows and columns a size.
        /// </summary>
        private static void ShownAway(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            OffScreenForm.Pump();
        }

        private static T Focus<T>(T control)
            where T : Control
        {
            control.Focus();
            OffScreenForm.Pump();
            Assert.That(control.Focused, Is.True, "the key has to start where an operator would press it");
            return control;
        }

        /// <summary>Puts a key on the queue for the list, and lets the loop deliver it.</summary>
        private static void Press(Control control, Keys key)
        {
            PostKey(control, key);
            OffScreenForm.Pump();
        }

        /// <summary>Puts a key on the queue for a control, its press and its release.</summary>
        private static void PostKey(Control control, Keys key)
        {
            TestNativeMethods.PostMessage(control.Handle, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
            TestNativeMethods.PostMessage(control.Handle, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
        }

        private const int WmKeyDown = 0x0100;

        private const int WmKeyUp = 0x0101;

        private const int WmChar = 0x0102;

        private const int WmLButtonDown = 0x0201;

        private const int WmLButtonUp = 0x0202;

        private const int WmLButtonDoubleClick = 0x0203;

        [Test]
        public void Each_profile_is_a_row_of_its_name_its_state_and_the_clients_still_holding_it()
        {
            // A name on the list has no state. A name off it is new where the database knows no
            // removal of it, and removed where it knows a removal or where clients still hold it,
            // with those clients named in a column of their own, each on a line of its own however
            // many there are. The state is never joined to the name.
            using (var authority = TestAuthority.New())
            using (var runner = authority.IssueClient("ci-runner"))
            using (var first = authority.IssueClient("agent-a"))
            using (var second = authority.IssueClient("agent-b"))
            using (var third = authority.IssueClient("agent-c"))
            using (var fourth = authority.IssueClient("agent-d"))
            using (var fifth = authority.IssueClient("agent-e"))
            {
                var crowd = new[] { fifth, third, first, fourth, second }
                    .Select(client => AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "crowded" }, InventedIssuer.Id))
                    .ToList();
                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "reader", "fresh", "gone", "kept", "crowded" }, new[] { "reader" }, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>>
                    {
                        { "kept", new[] { AdmissionRecord.For(runner, DateTimeOffset.UtcNow, new[] { "kept" }, InventedIssuer.Id) } },
                        { "crowded", crowd }
                    },
                    null,
                    new[] { "gone" });
                var rows = new[]
                {
                    new[] { "reader", string.Empty, string.Empty },
                    new[] { "fresh", PairingApprovalForm.NewState, string.Empty },
                    new[] { "gone", PairingApprovalForm.RemovedState, string.Empty },
                    new[] { "kept", PairingApprovalForm.RemovedState, "ci-runner" },
                    new[]
                    {
                        "crowded", PairingApprovalForm.RemovedState,
                        string.Join(Environment.NewLine, "agent-a", "agent-b", "agent-c", "agent-d", "agent-e")
                    }
                };

                Assert.That(request.Profiles.Select(profile => PairingApprovalForm.Cells(request, profile)), Is.EqualTo(rows));

                using (var dialog = new PairingApprovalForm(request, Timeout))
                {
                    ShownAway(dialog);
                    var list = Profiles(dialog);
                    var headings = ControlTree.FindByName<Control>(list, ProfileTable.HeadingsName);
                    Assert.That(headings, Is.Not.Null, "no headings");
                    Assert.That(
                        headings.Controls.OfType<Label>().Select(heading => heading.Text),
                        Is.EqualTo(new[] { "Grant", "Profile", "Approve also", "Regained by" }));
                    Assert.That(ProfileRows.Of(list).Select(row => row.Controls.Count), Has.All.EqualTo(4), "a box and three cells");
                    Assert.That(Rows(list), Is.EqualTo(rows));
                    Assert.That(
                        ProfileRows.Of(list).Select(row => ProfileRows.Cell(row, GateTab.ApprovalProfileColumnName).Text),
                        Is.EqualTo(request.Profiles), "the names in the column of names");
                    BoxFirst(list);
                    Assert.That(list.AccessibleName, Is.EqualTo("Profiles the client asks for"), "the line above the table, without its colon");
                    Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table, whatever its states");
                    Assert.That(Texts(dialog), Does.Not.Contain(PairingApprovalForm.NewTip).And.Not.Contain(PairingApprovalForm.RemovedTip));

                    dialog.Approve();
                    Assert.That(dialog.Answer().Profiles, Is.EqualTo(request.Profiles), "the names, and nothing of their states");
                }
            }
        }

        [Test]
        public void A_cell_too_narrow_for_its_text_shows_it_whole_in_a_tooltip_and_a_cell_that_holds_it_shows_none()
        {
            var cut = new string('W', 64);
            using (var authority = TestAuthority.New())
            {
                using (var holders = new IssuedClients(Enumerable.Range(0, 4)
                    .Select(at => authority.IssueClient(new string('W', 62) + at.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))))
                {
                    var request = new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                        new[] { cut, "ops", "gone" }, new[] { "ops" }, "127.0.0.1:60001",
                        new Dictionary<string, IReadOnlyList<AdmissionRecord>>
                        {
                            {
                                "gone",
                                holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "gone" }, InventedIssuer.Id)).ToList()
                            }
                        });

                    using (var dialog = new PairingApprovalForm(request, Timeout))
                    {
                        ShownAway(dialog);
                        var list = Profiles(dialog);
                        var clients = CellOf(list, 2, GateTab.ApprovalHeldByColumnName).Text;

                        Assert.That(TipOf(list, CellOf(list, 0, GateTab.ApprovalProfileColumnName)), Is.EqualTo(Tooltips.Wrapped(cut)),
                            "a name cut at its column's edge");
                        Assert.That(TipOf(list, BoxOf(list, 0)), Is.Empty, "the box of a row whose name is cut");
                        Assert.That(TipOf(list, CellOf(list, 0, GateTab.ApprovalStateColumnName)), Is.EqualTo(Tooltips.Wrapped(PairingApprovalForm.NewTip)),
                            "a state held whole, with what Approve does for a profile in it");
                        Assert.That(TipOf(list, CellOf(list, 1, GateTab.ApprovalStateColumnName)), Is.Empty, "a name on the list, which has no state");
                        Assert.That(TipOf(list, CellOf(list, 1, GateTab.ApprovalProfileColumnName)), Is.Empty, "a name held whole");

                        Assert.That(clients.Split(new[] { Environment.NewLine }, StringSplitOptions.None), Has.Length.EqualTo(4), "a line for each client");
                        Assert.That(TipOf(list, CellOf(list, 2, GateTab.ApprovalHeldByColumnName)), Is.EqualTo(Tooltips.Wrapped(clients)),
                            "clients cut at their column's edge, shown whole");
                    }
                }
            }
        }

        [Test]
        public void On_a_dialog_shown_again_the_row_whose_state_changed_is_highlighted_and_no_other_row_is()
        {
            // `ops` on the list before and removed now, approved there: unchecked and highlighted.
            // `deploy` new before and new now, unchecked there by the operator: unchecked and not
            // highlighted, so that unchecked alone keeps meaning the operator's own choice.
            // `reader` on the list before and now, approved there: checked and not highlighted.
            Assert.That(SystemInformation.HighContrast, Is.False, "the colors of the normal theme are measured");
            using (var dialog = new PairingApprovalForm(ShownAgainOverAChange("ops"), Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);

                Assert.That(Rows(list).Select(row => row[1]), Is.EqualTo(new[]
                {
                    PairingApprovalForm.RemovedState, PairingApprovalForm.NewState, string.Empty
                }));
                Assert.That(Checks(list), Is.EqualTo(new[] { false, false, true }));
                Assert.That(Backgrounds(list), Is.EqualTo(new[] { PairingApprovalForm.ChangedBackground, Unmarked(list), Unmarked(list) }));
                Assert.That(ProfileRows.Of(list).Select(row => row.ForeColor), Is.All.EqualTo(SystemColors.WindowText));
                Assert.That(
                    ProfileRows.Of(list).SelectMany(row => row.Controls.Cast<Control>().Select(cell => cell.BackColor == row.BackColor)),
                    Is.All.True, "the whole row");
            }
        }

        [Test]
        public void A_first_dialog_highlights_no_row()
        {
            // The same names in the same states, on the first dialog of a request: nothing was
            // shown before, so no state changed since.
            var request = new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", new[] { "ops", "deploy", "reader" },
                new[] { "reader" }, "127.0.0.1:60001", null, null, new[] { "ops" });

            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);

                Assert.That(Rows(list).Select(row => row[1]), Is.EqualTo(new[]
                {
                    PairingApprovalForm.RemovedState, PairingApprovalForm.NewState, string.Empty
                }), "the states are there");
                Assert.That(Checks(list), Is.All.True);
                Assert.That(Backgrounds(list), Is.All.EqualTo(Unmarked(list)));
                Assert.That(ProfileRows.Spoken(list), Is.EqualTo(new[]
                {
                    "ops. " + PairingApprovalForm.RemovedTip, "deploy. " + PairingApprovalForm.NewTip, "reader"
                }));

                Assert.That(TipOf(list, CellOf(list, 0, GateTab.ApprovalProfileColumnName)), Is.Empty);
                Assert.That(TipOf(list, CellOf(list, 0, GateTab.ApprovalStateColumnName)),
                    Is.EqualTo(Tooltips.Wrapped(PairingApprovalForm.RemovedTip)), "the state alone, and no change");
            }
        }

        [Test]
        public void The_table_laid_again_once_it_has_a_window_keeps_every_check()
        {
            // The dialog lays its table again once the table has a window, and the box measured
            // there may be drawn at another size under the visual styles KeePass turns on: every
            // check stays as it stood, a row unchecked among them.
            using (var dialog = New("ops", "reader", "deploy"))
            {
                ShownAway(dialog);
                var list = (ProfileTable)Profiles(dialog);
                Assert.That(Checks(list), Is.All.True, "a first dialog checks every row");
                BoxOf(list, 1).Checked = false;

                list.Lay(true);
                OffScreenForm.Pump();

                Assert.That(Checks(list), Is.EqualTo(new[] { true, false, true }));
                Assert.That(list.Checked, Is.EqualTo(new[] { "ops", "deploy" }));
            }
        }

        [Test]
        public void The_row_whose_state_changed_states_so_in_its_tooltip_and_in_its_accessible_text()
        {
            using (var dialog = new PairingApprovalForm(ShownAgainOverAChange("ops"), Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);

                Assert.That(TipOf(list, CellOf(list, 0, GateTab.ApprovalStateColumnName)),
                    Is.EqualTo(Tooltips.Wrapped(PairingApprovalForm.RemovedTip + Environment.NewLine + Changed)), "on the state");
                Assert.That(TipOf(list, BoxOf(list, 0)), Is.EqualTo(Tooltips.Wrapped(Changed)), "on the box");
                Assert.That(TipOf(list, ProfileRows.Of(list)[0]), Is.EqualTo(Tooltips.Wrapped(Changed)), "between the cells");
                Assert.That(TipOf(list, CellOf(list, 1, GateTab.ApprovalProfileColumnName)), Is.Empty, "a row unchecked by the operator, unchanged");
                Assert.That(TipOf(list, CellOf(list, 2, GateTab.ApprovalProfileColumnName)), Is.Empty, "a row checked, unchanged");

                // What UI automation reads as each row's box; the name alone is drawn, in the
                // column of names.
                Assert.That(ProfileRows.Spoken(list), Is.EqualTo(new[]
                {
                    "ops. " + PairingApprovalForm.RemovedTip + " " + Changed, "deploy. " + PairingApprovalForm.NewTip, "reader"
                }));
                Assert.That(Rows(list).Select(row => row[0]), Is.EqualTo(new[] { "ops", "deploy", "reader" }));
            }
        }

        [Test]
        public void A_name_cut_on_a_row_whose_state_changed_shows_the_whole_name_and_the_change_in_its_tooltip()
        {
            var cut = new string('W', 64);
            using (var dialog = new PairingApprovalForm(ShownAgainOverAChange(cut), Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);

                Assert.That(TipOf(list, CellOf(list, 0, GateTab.ApprovalProfileColumnName)),
                    Is.EqualTo(Tooltips.Wrapped(cut + Environment.NewLine + Changed)));
            }
        }

        [Test]
        public void The_highlight_stays_whatever_the_box_and_Approve_grants_the_names_checked()
        {
            // The highlight states that the state changed since the dialog before, which a check
            // does not undo: a row checked or unchecked again keeps it.
            using (var dialog = new PairingApprovalForm(ShownAgainOverAChange("ops"), Timeout))
            {
                ShownWhereClicksLand(dialog);
                var list = Profiles(dialog);

                Click(BoxOf(list, 0));
                Assert.That(Checks(list), Is.EqualTo(new[] { true, false, true }), "checked by the operator");
                Assert.That(Backgrounds(list)[0], Is.EqualTo(PairingApprovalForm.ChangedBackground), "highlighted, checked");

                Click(BoxOf(list, 0));
                Assert.That(Checks(list), Is.EqualTo(new[] { false, false, true }), "unchecked again");
                Assert.That(Backgrounds(list)[0], Is.EqualTo(PairingApprovalForm.ChangedBackground), "highlighted, unchecked again");

                Click(BoxOf(list, 0));
                dialog.Approve();
                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops", "reader" }), "the names, never the text read out");
            }
        }

        [Test]
        public void The_highlight_is_a_light_yellow_legible_with_the_row_s_text_and_a_contrast_theme_s_own_pair_under_high_contrast()
        {
            // Light: the yellow of a highlighter, well clear of the window's white, and as legible
            // with the window's text as black on white nearly is. Under a high contrast setting no
            // fixed color is drawn: the row keeps the theme's background and takes its hyperlink
            // color, the pair a contrast theme keeps legible and apart from its ordinary text.
            Assert.That(SystemColors.WindowText.ToArgb(), Is.EqualTo(Color.Black.ToArgb()), "the normal theme's text");
            var yellow = PairingApprovalForm.ChangedBackground;
            Assert.That(yellow.GetHue(), Is.InRange(45f, 60f), "yellow");
            Assert.That(yellow.GetBrightness(), Is.GreaterThanOrEqualTo(0.8f), "light");
            Assert.That(Contrast(yellow, SystemColors.WindowText), Is.GreaterThanOrEqualTo(15.0), "legible with the row's text");
            Assert.That(Contrast(yellow, Color.White), Is.GreaterThanOrEqualTo(1.1), "apart from the window's white");

            var normal = PairingApprovalForm.ChangedColors(false);
            Assert.That(normal.Back, Is.EqualTo(yellow));
            Assert.That(normal.Fore, Is.EqualTo(SystemColors.WindowText));

            var contrast = PairingApprovalForm.ChangedColors(true);
            Assert.That(contrast.Back, Is.EqualTo(SystemColors.Window));
            Assert.That(contrast.Fore, Is.EqualTo(SystemColors.HotTrack));
            Assert.That(contrast.Back.IsSystemColor && contrast.Fore.IsSystemColor, Is.True, "the theme's own colors");
        }

        /// <summary>The tooltip and the text read out of a row whose profile Approve now restores.</summary>
        private const string Changed = PairingApprovalForm.NowRestoresTip;

        [Test]
        public void What_changed_in_a_row_reads_as_written()
        {
            Assert.That(PairingApprovalForm.NowAddsTip, Is.EqualTo(
                "The row changed after the profile was first shown: Approve now also adds the profile."));
            Assert.That(PairingApprovalForm.NowRestoresTip, Is.EqualTo(
                "The row changed after the profile was first shown: Approve now also restores the profile."));
            Assert.That(PairingApprovalForm.NowListedTip, Is.EqualTo(
                "The row changed after the profile was first shown: this database names the profile now."));
        }

        [Test]
        public void A_row_whose_state_changed_states_what_the_state_cell_reads_now()
        {
            // On a dialog shown again: `gone` on the list before and removed now, `back` new before
            // and on the list now, and `fresh` on the list of the copy the dialog before read and
            // never named in the copy read now. Each row's tooltip and text read out state the
            // change, by what its state cell reads now; `kept`, unchanged, states none.
            var asked = new[] { "gone", "back", "fresh", "kept" };
            var request = ShownAgainOver(asked, new[] { "gone", "fresh", "kept" }, new[] { "back", "kept" }, new[] { "gone" });
            Assert.That(asked.Select(request.MarkOf), Is.EqualTo(new[]
            {
                KeePassGate.Gate.ProfileMark.Removed, KeePassGate.Gate.ProfileMark.Listed, KeePassGate.Gate.ProfileMark.New, KeePassGate.Gate.ProfileMark.Listed
            }));

            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);
                var told = new[] { PairingApprovalForm.NowRestoresTip, PairingApprovalForm.NowListedTip, PairingApprovalForm.NowAddsTip };

                Assert.That(Enumerable.Range(0, 3).Select(row => TipOf(list, BoxOf(list, row))), Is.EqualTo(told.Select(tip => Tooltips.Wrapped(tip))));
                Assert.That(TipOf(list, BoxOf(list, 3)), Is.Empty);
                Assert.That(ProfileRows.Spoken(list), Is.EqualTo(new[]
                {
                    "gone. " + PairingApprovalForm.RemovedTip + " " + PairingApprovalForm.NowRestoresTip,
                    "back. " + PairingApprovalForm.NowListedTip,
                    "fresh. " + PairingApprovalForm.NewTip + " " + PairingApprovalForm.NowAddsTip,
                    "kept"
                }));
            }
        }

        /// <summary>The background of every row, in order.</summary>
        private static Color[] Backgrounds(Control table)
        {
            return ProfileRows.Of(table).Select(row => row.BackColor).ToArray();
        }

        /// <summary>The background of a row with no highlight: the view's, which a row takes on.</summary>
        private static Color Unmarked(Control table)
        {
            return ProfileRows.View(table).BackColor;
        }

        /// <summary>The contrast ratio of two colors, as WCAG 2 measures it.</summary>
        private static double Contrast(Color one, Color other)
        {
            var lighter = Math.Max(Luminance(one), Luminance(other));
            var darker = Math.Min(Luminance(one), Luminance(other));
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double Luminance(Color color)
        {
            Func<int, double> linear = value =>
            {
                var channel = value / 255.0;
                return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            };
            return (0.2126 * linear(color.R)) + (0.7152 * linear(color.G)) + (0.0722 * linear(color.B));
        }

        /// <summary>
        /// A dialog shown again for a request asking for this name, `deploy` and `reader`: the
        /// dialog before showed the name and `reader` on the list and `deploy` new, and the operator
        /// approved the name and `reader` there; now the name is removed, so its state changed, and
        /// the other two stand as they stood.
        /// </summary>
        private static PairingRequest ShownAgainOverAChange(string changed)
        {
            var asked = new[] { changed, "deploy", "reader" };
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, changed);
            StoreSeeds.Name(store, "reader");
            KeePassGate.Gate.AskedProfiles before;
            using (var reading = store.Read())
            {
                before = KeePassGate.Gate.AskedProfiles.Of(reading, asked, DateTimeOffset.UtcNow);
            }

            Assert.That(asked.Select(before.MarkOf), Is.EqualTo(new[]
            {
                KeePassGate.Gate.ProfileMark.Listed, KeePassGate.Gate.ProfileMark.New, KeePassGate.Gate.ProfileMark.Listed
            }), "the states the dialog before showed");
            return new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, new[] { "reader" },
                "127.0.0.1:60001", null, null, new[] { changed }, PairingApproval.Approved(new[] { changed, "reader" }), before);
        }

        [Test]
        public void A_click_on_a_box_or_Space_on_its_row_changes_its_check_and_Approve_grants_the_checks_kept()
        {
            using (var dialog = New("ops", "reader", "deploy"))
            {
                ShownWhereClicksLand(dialog);
                var list = Profiles(dialog);

                Click(BoxOf(list, 1));
                Assert.That(Checks(list), Is.EqualTo(new[] { true, false, true }), "a click on a box");

                Click(BoxOf(list, 1));
                Assert.That(Checks(list), Is.EqualTo(new[] { true, true, true }), "a click on the same box again");

                Click(CellOf(list, 0, GateTab.ApprovalProfileColumnName));
                Click(CellOf(list, 0, GateTab.ApprovalStateColumnName));
                Click(ProfileRows.Of(list)[0]);
                Assert.That(Checks(list), Is.EqualTo(new[] { true, true, true }), "a click on a name, a state, or between the cells");

                Press(Focus(BoxOf(list, 2)), Keys.Space);
                Assert.That(Checks(list), Is.EqualTo(new[] { true, true, false }), "Space on the box focused");

                dialog.Approve();
                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops", "reader" }), "what is granted is what is still checked");
            }
        }

        [Test]
        public void A_double_click_is_the_single_click_it_begins_with()
        {
            // A check box double clicked toggles twice, which reads as a click that did nothing.
            using (var dialog = New("ops", "reader"))
            {
                ShownWhereClicksLand(dialog);
                var list = Profiles(dialog);

                DoubleClick(BoxOf(list, 0));
                Assert.That(Checks(list), Is.EqualTo(new[] { false, true }), "a double click on a box");

                DoubleClick(CellOf(list, 1, GateTab.ApprovalProfileColumnName));
                Assert.That(Checks(list), Is.EqualTo(new[] { false, true }), "a double click on a name");

                dialog.Approve();
                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "reader" }));
            }
        }

        [Test]
        public void A_letter_typed_on_the_table_moves_nothing_and_checks_nothing()
        {
            // A letter on a box would reach the dialog's mnemonics without Alt, as on a button:
            // "r" refused and spent the invitation, and "f" pressed Refresh. On a box it does
            // nothing, and Space then toggles the box in front of the operator.
            using (var dialog = New("ops", "reader"))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);
                var box = Focus(BoxOf(list, 0));

                foreach (var letter in new[] { 'r', 'f', 'o' })
                {
                    TestNativeMethods.PostMessage(box.Handle, WmChar, (IntPtr)letter, IntPtr.Zero);
                    OffScreenForm.Pump();
                }

                Assert.That(dialog.Visible, Is.True, "a letter ended the dialog");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "a letter answered the dialog");
                Assert.That(box.Focused, Is.True, "a letter moved the focus");
                Assert.That(Checks(list), Is.EqualTo(new[] { true, true }), "a letter changed a check");

                Press(box, Keys.Space);
                Assert.That(Checks(list), Is.EqualTo(new[] { false, true }), "Space toggled a box other than the box focused");
            }
        }

        [Test]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        [TestCase(30)]
        public void Every_client_regaining_a_name_is_a_line_of_its_own_and_the_other_cells_keep_to_the_first(int clients)
        {
            // No cap on the lines: the row is as tall as its clients, and every other cell, the
            // box among them, stays at the row's top, level with the first client.
            using (var authority = TestAuthority.New())
            {
                using (var holders = new IssuedClients(Enumerable.Range(0, clients)
                    .Select(at => authority.IssueClient("agent-" + at.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))))
                {
                    var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "gone" }, InventedIssuer.Id)).ToList();
                    var request = new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                        new[] { "gone", "reader" }, new[] { "reader" }, "127.0.0.1:60001",
                        new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "gone", records } });

                    using (var dialog = new PairingApprovalForm(request, Timeout))
                    {
                        ShownAway(dialog);
                        var list = Profiles(dialog);
                        var rows = ProfileRows.Of(list);
                        var name = CellOf(list, 0, GateTab.ApprovalProfileColumnName);
                        var state = CellOf(list, 0, GateTab.ApprovalStateColumnName);
                        var heldBy = CellOf(list, 0, GateTab.ApprovalHeldByColumnName);
                        var box = BoxOf(list, 0);
                        var line = name.Height;

                        Assert.That(heldBy.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None),
                            Is.EqualTo(ClientList.ClientNames(records)), "a line for each client, in order");
                        Assert.That(heldBy.Height, Is.EqualTo(line * clients), "as tall as its lines");
                        Assert.That(state.Height, Is.EqualTo(line), "a state of a single line");
                        Assert.That(new[] { state.Top, heldBy.Top }, Is.All.EqualTo(name.Top), "the cells at the row's top");
                        Assert.That(box.Top + (box.Height / 2), Is.EqualTo(name.Top + (line / 2)).Within(1), "the box level with the first line");
                        Assert.That(rows[0].Height, Is.GreaterThanOrEqualTo(heldBy.Bottom), "the row holds every line");
                        Assert.That(rows[1].Top, Is.EqualTo(rows[0].Bottom), "the next row under the last line");
                        Assert.That(ProfileRows.Spoken(list)[0], Is.EqualTo(
                            "gone. " + PairingApprovalForm.RemovedTip + " Regained by: " + string.Join(", ", ClientList.ClientNames(records)) + "."),
                            "read out with every client");
                    }
                }
            }
        }

        [Test]
        public void The_cells_beside_a_cell_of_several_lines_are_drawn_on_its_first_line_and_a_highlight_over_the_whole_row()
        {
            // Measured in pixels of the row drawn: the name's ink lies in the band of the first
            // line alone, the clients' ink in every band, and the highlight fills the row to its
            // bottom under the box and the name as well as under the clients.
            using (var authority = TestAuthority.New())
            {
                using (var holders = new IssuedClients(Enumerable.Range(0, 6).Select(at => authority.IssueClient("agent-" + at))))
                {
                    var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "ops" }, InventedIssuer.Id)).ToList();
                    var asked = new[] { "ops", "reader" };
                    var store = new MemoryStore();
                    StoreSeeds.Authority(store);
                    StoreSeeds.Name(store, "ops");
                    StoreSeeds.Name(store, "reader");
                    KeePassGate.Gate.AskedProfiles before;
                    using (var reading = store.Read())
                    {
                        before = KeePassGate.Gate.AskedProfiles.Of(reading, asked, DateTimeOffset.UtcNow);
                    }

                    var request = new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, new[] { "reader" },
                        "127.0.0.1:60001", new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "ops", records } },
                        null, null, PairingApproval.Approved(asked), before);

                    Assert.That(SystemInformation.HighContrast, Is.False, "the colors of the normal theme are measured");
                    using (var dialog = new PairingApprovalForm(request, Timeout))
                    {
                        ShownAway(dialog);
                        var list = Profiles(dialog);
                        var row = ProfileRows.Of(list)[0];
                        var name = CellOf(list, 0, GateTab.ApprovalProfileColumnName);
                        var heldBy = CellOf(list, 0, GateTab.ApprovalHeldByColumnName);
                        var line = name.Height;
                        Assert.That(row.BackColor, Is.EqualTo(PairingApprovalForm.ChangedBackground), "the changed row is highlighted");

                        using (var picture = new Bitmap(row.Width, row.Height))
                        {
                            row.DrawToBitmap(picture, new Rectangle(Point.Empty, row.Size));
                            var background = PairingApprovalForm.ChangedBackground;
                            for (var band = 0; band < 6; band++)
                            {
                                var top = name.Top + (band * line);
                                Assert.That(Inked(picture, new Rectangle(name.Left, top, name.Width, line), background), Is.EqualTo(band == 0),
                                    "the name's ink in band " + band);
                                Assert.That(Inked(picture, new Rectangle(heldBy.Left, top, heldBy.Width, line), background), Is.True,
                                    "no client drawn in band " + band);
                            }

                            // To the line parting the row from the next, which is the row's last
                            // line of pixels.
                            foreach (var x in new[] { BoxOf(list, 0).Left + 1, name.Left + 1, heldBy.Right - 2 })
                            {
                                Assert.That(picture.GetPixel(x, row.Height - 2).ToArgb(), Is.EqualTo(background.ToArgb()),
                                    "the highlight stops short of the row's bottom at " + x);
                                Assert.That(picture.GetPixel(x, row.Height - 1).ToArgb(), Is.EqualTo(ProfileTable.RuleColor(false).ToArgb()),
                                    "no line under the row at " + x);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Whether a part of a picture holds a pixel other than the background.</summary>
        private static bool Inked(Bitmap picture, Rectangle part, Color background)
        {
            for (var y = Math.Max(0, part.Top); y < Math.Min(picture.Height, part.Bottom); y++)
            {
                for (var x = Math.Max(0, part.Left); x < Math.Min(picture.Width, part.Right); x++)
                {
                    if (picture.GetPixel(x, y).ToArgb() != background.ToArgb())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [Test]
        public void A_row_taller_than_the_table_is_read_to_its_last_line_by_the_wheel_and_back()
        {
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownAway(dialog);
                    var view = ProfileRows.View(list);
                    var row = ProfileRows.Of(list)[0];
                    var heldBy = CellOf(list, 0, GateTab.ApprovalHeldByColumnName);
                    Assert.That(row.Height, Is.GreaterThan(view.ClientSize.Height), "the row is taller than the table");
                    Assert.That(Scrolls(view, VerticalScrollBar), Is.True);
                    Assert.That(Scrolls(view, HorizontalScrollBar), Is.False);
                    AssertCutInHalf(list, "a row taller than the table");

                    var before = row.Top;
                    Wheel(heldBy, -1);
                    var lines = SystemInformation.MouseWheelScrollLines;
                    Assert.That(before - row.Top, Is.EqualTo(lines < 0 ? view.ClientSize.Height : lines * CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height),
                        "a notch scrolls by the lines the system names");

                    for (var notch = 0; notch < 50 && row.Top + heldBy.Bottom > view.ClientSize.Height; notch++)
                    {
                        Wheel(heldBy, -1);
                    }

                    Assert.That(row.Top + heldBy.Bottom, Is.LessThanOrEqualTo(view.ClientSize.Height), "the last line is never shown");
                    Assert.That(row.Top + heldBy.Bottom, Is.GreaterThan(view.ClientSize.Height - heldBy.Height), "scrolled past the last line");

                    for (var notch = 0; notch < 50 && row.Top < 0; notch++)
                    {
                        Wheel(heldBy, 1);
                    }

                    Assert.That(row.Top, Is.Zero, "the wheel does not bring the first line back");
                }
            }
        }

        [Test]
        public void The_small_turns_a_precision_touchpad_sends_add_up_to_the_scroll_of_a_notch()
        {
            // A precision touchpad sends a notch of the wheel as many small turns, and a turn of a
            // single unit of the 120 in a notch, divided on its own, scrolled by nothing at all.
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownAway(dialog);
                    var view = ProfileRows.View(list);
                    var row = ProfileRows.Of(list)[0];
                    var heldBy = CellOf(list, 0, GateTab.ApprovalHeldByColumnName);
                    var lines = SystemInformation.MouseWheelScrollLines;
                    var notch = lines < 0 ? view.ClientSize.Height : lines * CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height;
                    Assert.That(notch % WheelNotch, Is.Not.Zero, "a notch this desk scrolls by divides into whole pixels for every turn");

                    var screen = heldBy.PointToScreen(Middle(heldBy));
                    for (var turn = 0; turn < WheelNotch; turn++)
                    {
                        TestNativeMethods.PostMessage(heldBy.Handle, WmMouseWheel, (IntPtr)(-1 << 16), At(screen));
                    }

                    OffScreenForm.Pump();
                    Assert.That(-row.Top, Is.EqualTo(notch), "the turns of a notch scroll otherwise than the notch");
                }
            }
        }

        /// <summary>What a single notch of a wheel turns by.</summary>
        private const int WheelNotch = 120;

        [Test]
        public void A_row_taller_than_the_table_is_read_to_its_last_line_by_the_keyboard_and_back()
        {
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownAway(dialog);
                    var view = ProfileRows.View(list);
                    var row = ProfileRows.Of(list)[0];
                    var heldBy = CellOf(list, 0, GateTab.ApprovalHeldByColumnName);
                    var line = CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height;
                    var box = Focus(BoxOf(list, 0));

                    Press(box, Keys.Down);
                    Assert.That(row.Top, Is.EqualTo(-line), "Down scrolls by a line");
                    Press(box, Keys.Up);
                    Assert.That(row.Top, Is.Zero, "Up scrolls back by a line");

                    for (var press = 0; press < 100 && row.Top + heldBy.Bottom > view.ClientSize.Height; press++)
                    {
                        Press(box, Keys.Down);
                    }

                    Assert.That(row.Top + heldBy.Bottom, Is.LessThanOrEqualTo(view.ClientSize.Height), "the last line is never shown by Down");
                    Assert.That(box.Focused, Is.True, "the keys moved the focus");
                    Assert.That(Checks(list), Is.All.True, "the keys changed a check");

                    Press(box, Keys.Home);
                    Assert.That(row.Top, Is.Zero, "Home");
                    Press(box, Keys.PageDown);
                    Assert.That(row.Top, Is.EqualTo(-(view.ClientSize.Height - line)), "Page Down scrolls by the view less a line");
                    Press(box, Keys.PageUp);
                    Assert.That(row.Top, Is.Zero, "Page Up");
                    Press(box, Keys.End);
                    AssertAtTheEnd(list, "End");

                    // Tab to the next box, which is out of sight: its row comes as near the top as
                    // the rows reach, which for the last row is the end of the rows, the view's top
                    // on the edge of a line of the row above.
                    Press(box, Keys.Home);
                    Press(box, Keys.Tab);
                    Assert.That(BoxOf(list, 1).Focused, Is.True, "Tab moves to the next box");
                    Assert.That(ProfileRows.Of(list)[1].Top, Is.GreaterThanOrEqualTo(0), "the next box's row is in view");
                    AssertAtTheEnd(list, "the next box's row");

                    // Back to the first box, whose row's last lines are in view and whose box is out
                    // of sight with the row's first line: the row's top comes to the top of the view.
                    Assert.That(row.Top + box.Top, Is.LessThan(0), "the box before in sight");
                    Assert.That(row.Bottom, Is.GreaterThan(0), "the row before out of sight");
                    Focus(box);
                    Assert.That(row.Top, Is.Zero, "the view scrolled the box's row elsewhere than to its top");
                }
            }
        }

        [TestCase(Keys.Down)]
        [TestCase(Keys.PageDown)]
        [TestCase(Keys.End)]
        [TestCase(Keys.Up)]
        [TestCase(Keys.PageUp)]
        [TestCase(Keys.Home)]
        public void A_key_on_a_focused_box_scrolls_the_table_and_the_view_stays_where_the_key_put_it(Keys key)
        {
            // The box keeps the focus wherever the key leaves its row, as the focused item of any
            // scrolled list does. A layout of the dialog, which every tick of the countdown brings,
            // brought the box back into view, and the view back to where the key found it.
            // Home is pressed on the box of the last row, which the top of the rows leaves out of
            // sight; Up and Page Up from the end of the rows; every other key from the top.
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    var view = ProfileRows.View(list);
                    var line = CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height;
                    var page = view.ClientSize.Height - line;
                    var end = view.DisplayRectangle.Height - view.ClientSize.Height;
                    var box = Focus(BoxOf(list, key == Keys.Home ? 1 : 0));
                    if (key == Keys.Up || key == Keys.PageUp)
                    {
                        Press(box, Keys.End);
                    }

                    var from = -view.AutoScrollPosition.Y;
                    var expected = key == Keys.Down ? from + line
                        : key == Keys.PageDown ? Math.Min(end, from + page)
                        : key == Keys.End ? end
                        : key == Keys.Up ? from - line
                        : key == Keys.PageUp ? Math.Max(0, from - page)
                        : 0;
                    Assert.That(expected, Is.Not.EqualTo(from), "the key has to scroll the table");

                    Press(box, key);
                    Assert.That(-view.AutoScrollPosition.Y, Is.EqualTo(expected), key + " from " + from);
                    Assert.That(OutOfSight(view, box), Is.True, key + " left the box in sight, which tells nothing");
                    Assert.That(box.Focused, Is.True, key + " moved the focus");

                    PaintLayoutAndTick(dialog, GateTab.ApprovalCountdownName);
                    Assert.That(-view.AutoScrollPosition.Y, Is.EqualTo(expected), "a paint, a layout or the countdown's tick moved the view after " + key);
                    Assert.That(box.Focused, Is.True, "the focus moved after " + key);
                }
            }
        }

        [TestCase(Keys.PageDown)]
        [TestCase(Keys.Down)]
        public void The_last_client_line_of_a_tall_row_is_reached_by_a_key_alone_while_the_row_s_box_holds_the_focus(Keys key)
        {
            // Each press followed by a paint and a layout of the dialog, which brought the box
            // back into view after every press and so held the view at the top of the row.
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    var view = ProfileRows.View(list);
                    var row = ProfileRows.Of(list)[0];
                    var heldBy = CellOf(list, 0, GateTab.ApprovalHeldByColumnName);
                    var box = Focus(BoxOf(list, 0));
                    var presses = 0;
                    for (; presses < 60 && row.Top + heldBy.Bottom > view.ClientSize.Height; presses++)
                    {
                        Press(box, key);
                        dialog.Refresh();
                        dialog.PerformLayout();
                        OffScreenForm.Pump();
                    }

                    Assert.That(row.Top + heldBy.Bottom, Is.LessThanOrEqualTo(view.ClientSize.Height), "the last client line never shown by " + key + " alone, " + presses + " presses");
                    Assert.That(box.Focused, Is.True, key + " moved the focus");

                    PaintLayoutAndTick(dialog, GateTab.ApprovalCountdownName);
                    Assert.That(row.Top + heldBy.Bottom, Is.LessThanOrEqualTo(view.ClientSize.Height), "the countdown's tick took the last client line out of sight");
                }
            }
        }

        [Test]
        public void Space_on_a_focused_box_out_of_sight_brings_its_row_into_view_and_checks_nothing_until_pressed_again()
        {
            // A check the operator cannot see would change unseen, and Approve grants every
            // profile checked: the press brings the row into view, and the press after it, on a
            // box in sight, changes the check. A Space held down repeats while the row comes into
            // view, and the repeats change nothing either.
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    var row = ProfileRows.Of(list)[0];
                    var box = Focus(BoxOf(list, 0));
                    var before = Checks(list);
                    Press(box, Keys.PageDown);
                    Assert.That(row.Top + box.Bottom, Is.LessThanOrEqualTo(0), "Page Down left the box in sight");

                    TestNativeMethods.PostMessage(box.Handle, WmKeyDown, (IntPtr)(int)Keys.Space, (IntPtr)1);
                    TestNativeMethods.PostMessage(box.Handle, WmKeyDown, (IntPtr)(int)Keys.Space, (IntPtr)0x40000001);
                    TestNativeMethods.PostMessage(box.Handle, WmKeyDown, (IntPtr)(int)Keys.Space, (IntPtr)0x40000001);
                    TestNativeMethods.PostMessage(box.Handle, WmKeyUp, (IntPtr)(int)Keys.Space, unchecked((IntPtr)0xC0000001));
                    OffScreenForm.Pump();
                    Assert.That(Checks(list), Is.EqualTo(before), "Space changed a check out of sight");
                    Assert.That(row.Top, Is.Zero, "Space brought the box's row elsewhere than to the top of the view");
                    Assert.That(box.Focused, Is.True, "Space moved the focus");

                    Press(box, Keys.Space);
                    Assert.That(Checks(list)[0], Is.EqualTo(!before[0]), "Space on the box in sight changed nothing");
                    Assert.That(Checks(list).Skip(1), Is.EqualTo(before.Skip(1)), "Space changed another row's check");
                    Assert.That(row.Top, Is.Zero, "Space on a box in sight scrolled the table");
                }
            }
        }

        [Test]
        public void A_box_scrolled_out_of_sight_keeps_the_focus_and_its_row_draws_none_of_it()
        {
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    TestNativeMethods.SendMessage(dialog.Handle, ChangeUiStateMessage, (IntPtr)ShowFocusCues, IntPtr.Zero);
                    OffScreenForm.Pump();
                    var view = ProfileRows.View(list);
                    var row = ProfileRows.Of(list)[0];
                    var box = Focus(BoxOf(list, 0));
                    Press(box, Keys.PageDown);
                    var top = row.Top;
                    Assert.That(OutOfSight(view, box), Is.True, "Page Down left the box in sight");
                    Assert.That(box.Focused, Is.True, "Page Down moved the focus");
                    using (var focused = Drawn(row))
                    {
                        Focus(Lifetime(dialog));
                        Assert.That(row.Top, Is.EqualTo(top), "the focus leaving the box scrolled the table");
                        using (var away = Drawn(row))
                        {
                            var drawn = Enumerable.Range(0, row.Width).SelectMany(x => Enumerable.Range(0, row.Height).Select(y => new Point(x, y)))
                                .Where(point => focused.GetPixel(point.X, point.Y).ToArgb() != away.GetPixel(point.X, point.Y).ToArgb())
                                .Select(point => point.Y)
                                .Distinct()
                                .ToList();
                            Assert.That(drawn, Is.Empty, "the focus of a box out of sight drawn on its row");
                        }
                    }
                }
            }
        }

        /// <summary>Whether a box is out of sight in the view, in part or whole.</summary>
        private static bool OutOfSight(Control view, Control box)
        {
            var top = box.Parent.Top + box.Top;
            return top < 0 || top + box.Height > view.ClientSize.Height;
        }

        /// <summary>
        /// Lets the dialog go through what follows a key in a loop that runs on: a paint and a
        /// layout of the whole dialog, and the next tick of its countdown, whose new words lay the
        /// dialog out again.
        /// </summary>
        internal static void PaintLayoutAndTick(Form dialog, string countdownName)
        {
            dialog.Refresh();
            dialog.PerformLayout();
            OffScreenForm.Pump();
            NextTick(dialog, countdownName);
        }

        /// <summary>Runs the loop until the dialog's countdown has ticked, and the tick's work after it.</summary>
        internal static void NextTick(Form dialog, string countdownName)
        {
            var countdown = ControlTree.FindByName<Label>(dialog, countdownName);
            Assert.That(countdown, Is.Not.Null, "no countdown called " + countdownName);
            var words = countdown.Text;
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (countdown.Text == words && DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Application.RaiseIdle(EventArgs.Empty);
                Thread.Sleep(5);
            }

            Assert.That(countdown.Text, Is.Not.EqualTo(words), "the countdown never ticked");
            OffScreenForm.Pump();
        }

        /// <summary>
        /// The parts of a table that paint while this runs, by name and clip: the view the rows
        /// scroll in and every row.
        /// </summary>
        internal static IReadOnlyList<string> PaintedWhile(Control list, Action run)
        {
            var painted = new List<string>();
            PaintEventHandler log = (sender, e) => painted.Add(((Control)sender).Name + " " + e.ClipRectangle);
            var parts = new[] { ProfileRows.View(list) }.Concat(ProfileRows.Of(list)).ToList();
            parts.ForEach(part => part.Paint += log);
            try
            {
                run();
            }
            finally
            {
                parts.ForEach(part => part.Paint -= log);
            }

            return painted;
        }

        [Test]
        public void Ticks_that_find_nothing_changed_paint_no_row_and_not_the_view_round_a_focused_box()
        {
            // The countdown's words change every second. Measured as they changed, they laid the
            // whole dialog out every second, and a layout of the dialog has WinForms bring the
            // control holding the focus into view, which drew the view of the rows again round a
            // box holding it, every second, with nothing changed. A tick first to settle, then two
            // measured, through the real loop.
            using (var dialog = TallRow(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    Focus(BoxOf(list, 0));
                    NextTick(dialog, GateTab.ApprovalCountdownName);
                    var painted = PaintedWhile(list, () =>
                    {
                        NextTick(dialog, GateTab.ApprovalCountdownName);
                        NextTick(dialog, GateTab.ApprovalCountdownName);
                    });
                    Assert.That(painted, Is.Empty, "ticks that found nothing changed painted the table");
                }
            }
        }

        [Test]
        public void A_box_brought_into_view_at_the_end_of_the_rows_leaves_no_row_cut_at_the_top()
        {
            // Ten names on the list and `gone` last, regained by three clients, cut at the bottom of
            // a view of ten rows of a single line and half the line after them: the rows end below the view by less than the
            // rows before `gone`, so that the view cannot put `gone` at its top, and by other than
            // a whole number of rows. Tab to the box of `gone`: the view's top falls on a row's
            // top, with no sliver of the row above it in sight, the last row whole, and the rest
            // of the view below it, less than a row.
            using (var authority = TestAuthority.New())
            {
                var listed = Enumerable.Range(1, 10).Select(at => "s" + at).ToArray();
                using (var holders = new IssuedClients(Enumerable.Range(0, 3).Select(at => authority.IssueClient("agent-" + at))))
                {
                    var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "gone" }, InventedIssuer.Id)).ToList();
                    using (var dialog = new PairingApprovalForm(
                               new PairingRequest(
                                   "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                                   listed.Concat(new[] { "gone" }).ToArray(), listed, "127.0.0.1:60001",
                                   new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "gone", records } }),
                               Timeout))
                    {
                        ShownAway(dialog);
                        var list = Profiles(dialog);
                        var view = ProfileRows.View(list);
                        var rows = ProfileRows.Of(list);
                        var last = rows[rows.Count - 1];
                        Assert.That(Scrolls(view, VerticalScrollBar), Is.True);
                        Assert.That(last.Top + ProfileRows.Box(last).Bottom, Is.GreaterThan(view.ClientSize.Height), "the box of the last row wholly in sight");
                        Assert.That(last.Top, Is.GreaterThan(last.Bottom - view.ClientSize.Height), "the view can put the last row at its top");
                        Assert.That((last.Bottom - view.ClientSize.Height) % rows[0].Height, Is.Not.Zero, "the end of the rows falls on a row's top");

                        var box = Focus(BoxOf(list, rows.Count - 2));
                        Assert.That(view.AutoScrollPosition.Y, Is.Zero, "the box before scrolled the view");
                        Press(box, Keys.Tab);

                        Assert.That(BoxOf(list, rows.Count - 1).Focused, Is.True, "Tab moves to the next box");
                        Assert.That(rows.Where(row => row.Top < 0 && row.Bottom > 0).Select(row => row.Controls.OfType<Label>().First().Text + " at " + row.Top),
                            Is.Empty, "a row cut at the top of the view");
                        Assert.That(last.Top, Is.GreaterThanOrEqualTo(0), "the row brought into view is out of sight");
                        AssertAtTheEnd(list, "the row brought into view");
                    }
                }
            }
        }

        /// <summary>
        /// Asserts that the view is scrolled as far as the rows scroll: the view's top on the edge
        /// of a line, a row's top or the top of a line of a row past its first, the last row whole
        /// in view, and less than a row of the view below it.
        /// </summary>
        private static void AssertAtTheEnd(Control list, string what)
        {
            var view = ProfileRows.View(list);
            var rows = ProfileRows.Of(list);
            var single = rows.Min(row => row.Height);
            var line = CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height;
            var pad = (single - line) / 2;
            var last = rows[rows.Count - 1];
            foreach (var row in rows.Where(row => row.Top < 0 && row.Bottom > 0))
            {
                Assert.That(-row.Top - pad, Is.GreaterThan(0), what + ": the top of the view cuts a row's first line");
                Assert.That((-row.Top - pad) % line, Is.Zero, what + ": the top of the view cuts a line");
            }

            Assert.That(last.Bottom, Is.LessThanOrEqualTo(view.ClientSize.Height), what + ": the last row out of sight");
            Assert.That(view.ClientSize.Height - last.Bottom, Is.LessThan(single), what + ": short of the end of the rows");
        }

        /// <summary>
        /// A first dialog whose first row, a removed name thirty clients regain, is taller than the
        /// table, with a row of a name on the list after it; and the clients' certificates, for the
        /// caller to let go.
        /// </summary>
        private static PairingApprovalForm TallRow(out Control list, out IssuedClients holders)
        {
            using (var authority = TestAuthority.New())
            {
                holders = new IssuedClients(Enumerable.Range(0, 30)
                    .Select(at => authority.IssueClient("agent-" + at.ToString("00", System.Globalization.CultureInfo.InvariantCulture))));
                var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "gone" }, InventedIssuer.Id)).ToList();
                var dialog = new PairingApprovalForm(
                    new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                        new[] { "gone", "reader" }, new[] { "reader" }, "127.0.0.1:60001",
                        new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "gone", records } }),
                    Timeout);
                list = Profiles(dialog);
                return dialog;
            }
        }

        [Test]
        public void Rows_are_parted_by_a_thin_line_highlighted_rows_too_and_a_highlight_reaches_the_edge_of_a_table_that_does_not_scroll()
        {
            // `s1` and `s2` on the list before and removed now, so both highlighted, the second
            // under the first; `s3` unchanged and last. Measured in pixels of the view drawn: a
            // line in the system's light edge under each row but the last, across the view, and
            // the yellow to the view's right edge, where no scroll bar stands.
            var asked = new[] { "s1", "s2", "s3" };
            Assert.That(SystemInformation.HighContrast, Is.False, "the colors of the normal theme are measured");
            using (var dialog = new PairingApprovalForm(ShownAgainOver(asked, asked, new[] { "s3" }, new[] { "s1", "s2" }), Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);
                var view = ProfileRows.View(list);
                var rows = ProfileRows.Of(list);
                Assert.That(Scrolls(view, VerticalScrollBar), Is.False);
                Assert.That(Backgrounds(list), Is.EqualTo(new[] { PairingApprovalForm.ChangedBackground, PairingApprovalForm.ChangedBackground, Unmarked(list) }));

                using (var picture = Drawn(view))
                {
                    var rule = ProfileTable.RuleColor(false).ToArgb();
                    var width = view.ClientSize.Width;
                    foreach (var x in new[] { 0, width / 2, width - 1 })
                    {
                        Assert.That(picture.GetPixel(x, rows[0].Bottom - 1).ToArgb(), Is.EqualTo(rule), "no line under the first row at " + x);
                        Assert.That(picture.GetPixel(x, rows[1].Bottom - 1).ToArgb(), Is.EqualTo(rule), "no line under the second row at " + x);
                        Assert.That(picture.GetPixel(x, rows[2].Bottom - 1).ToArgb(), Is.EqualTo(view.BackColor.ToArgb()), "a line under the last row at " + x);
                    }

                    Assert.That(rows.Select(row => row.Width), Is.All.EqualTo(width), "a row short of the view's edge");
                    Assert.That(picture.GetPixel(width - 1, rows[0].Top + (rows[0].Height / 2)).ToArgb(), Is.EqualTo(PairingApprovalForm.ChangedBackground.ToArgb()),
                        "the yellow stops short of the edge");
                }
            }

            Assert.That(ProfileTable.RuleColor(false), Is.EqualTo(SystemColors.ControlLight));
            Assert.That(ProfileTable.RuleColor(true), Is.EqualTo(SystemColors.WindowText), "a contrast theme's text color");
        }

        [Test]
        public void A_highlight_reaches_the_scroll_bar_of_a_table_that_scrolls()
        {
            var asked = Enumerable.Range(1, PairingApprovalForm.MostRows + 2).Select(at => "s" + at).ToArray();
            using (var dialog = new PairingApprovalForm(ShownAgainOver(asked, asked, asked.Skip(1).ToArray(), new[] { "s1" }), Timeout))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);
                var view = ProfileRows.View(list);
                var row = ProfileRows.Of(list)[0];
                Assert.That(Scrolls(view, VerticalScrollBar), Is.True);
                Assert.That(row.BackColor, Is.EqualTo(PairingApprovalForm.ChangedBackground));
                Assert.That(row.Right, Is.EqualTo(view.ClientSize.Width), "a row short of the scroll bar");
                using (var picture = Drawn(view))
                {
                    Assert.That(picture.GetPixel(view.ClientSize.Width - 1, row.Top + (row.Height / 2)).ToArgb(), Is.EqualTo(PairingApprovalForm.ChangedBackground.ToArgb()));
                }
            }
        }

        [Test]
        public void A_tall_row_scrolls_whole_with_the_rest_by_the_wheel_the_keys_and_the_scroll_bar()
        {
            AssertScrolledWhole(view => PostWheel(view, -1), view => PostWheel(view, 1));
            AssertScrolledWhole(view => PostKey(BoxOf(view.Parent, 0), Keys.Down), view => PostKey(BoxOf(view.Parent, 0), Keys.Up), focusFirstBox: true);
            AssertScrolledWhole(view => PostKey(BoxOf(view.Parent, 0), Keys.PageDown), view => PostKey(BoxOf(view.Parent, 0), Keys.PageUp), focusFirstBox: true);
            AssertScrolledWhole(view => SendScrollBar(view, LineDown), view => SendScrollBar(view, LineUp));
            AssertScrolledWhole(view => SendScrollBar(view, PageDown), view => SendScrollBar(view, PageUp));
        }

        [Test]
        public void A_box_brought_into_view_by_Tab_scrolls_whole_and_paints_the_whole_view()
        {
            // Tab from a box to the next brings a box out of sight to the top of the view, through
            // the place a box is scrolled to: the whole view is painted once, with the focus.
            using (var dialog = TallRows(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    var view = ProfileRows.View(list);
                    TestNativeMethods.SendMessage(dialog.Handle, ChangeUiStateMessage, (IntPtr)ShowFocusCues, IntPtr.Zero);
                    Focus(BoxOf(list, 0));
                    var moved = 0;
                    using (var log = new PaintLog(view))
                    {
                        for (var box = 1; box < ProfileRows.Boxes(list).Count; box++)
                        {
                            var at = view.AutoScrollPosition;
                            log.Step(dialog, () => PostKey(ProfileRows.Boxes(list).First(each => each.Focused), Keys.Tab));
                            Assert.That(BoxOf(list, box).Focused, Is.True, "Tab went elsewhere than box " + box);
                            moved += view.AutoScrollPosition == at ? 0 : 1;
                            log.AssertWholeOnce(list, at, "Tab to box " + box);
                        }
                    }

                    Assert.That(moved, Is.GreaterThan(1), "the boxes brought into view scrolled too few times to measure");
                }
            }
        }

        [Test]
        public void The_view_the_rows_scroll_in_is_painted_in_a_single_buffer_with_its_rows()
        {
            // The view and every row in it are painted bottom to top into a single buffer, which
            // reaches the screen once the paint is over, `WS_EX_COMPOSITED`: no picture of the
            // screen shows part of a scroll's paint. The headings and the frame round the view
            // stand outside it. A window made again, as a right to left layout makes it, keeps it.
            using (var dialog = New("ops", "reader"))
            {
                ShownAway(dialog);
                var list = Profiles(dialog);
                Assert.That(Composited(ProfileRows.View(list)), Is.True, "the view is painted a window at a time");
                Assert.That(Composited(list), Is.False, "the table round the view is painted into the view's buffer");
                Assert.That(Composited(dialog), Is.False, "the dialog is painted into the view's buffer");

                dialog.RightToLeft = RightToLeft.Yes;
                OffScreenForm.Pump();
                Assert.That(Composited(ProfileRows.View(list)), Is.True, "the view made again is painted a window at a time");
            }
        }

        /// <summary>The index `GetWindowLong` reads a window's extended style at.</summary>
        private const int ExtendedStyleIndex = -20;

        /// <summary>Whether a control's window carries `WS_EX_COMPOSITED`.</summary>
        private static bool Composited(Control control)
        {
            return (TestNativeMethods.GetWindowLong(control.Handle, ExtendedStyleIndex) & 0x02000000) != 0;
        }

        /// <summary>
        /// Scrolls the table of tall rows step by step to its end and back, each step put on the
        /// queue by <paramref name="down"/> or <paramref name="up"/> and handled by the loop, and
        /// asserts after every step that the rows moved whole, as the rest of the content: each row
        /// straight under the row before it, the first where the view's place puts it, and each
        /// row's box and cells on the row's first line, its clients from there down, so that no
        /// line of the table stands twice or nowhere.
        /// </summary>
        /// <remarks>
        /// **And that the step painted the whole view in its own handling and left nothing of the
        /// table to a later paint**, <see cref="PaintLog"/>. What no test without a screen holds
        /// is the pictures the screen shows meanwhile, which a series of pictures of a real screen
        /// does.
        /// </remarks>
        private static void AssertScrolledWhole(Action<Control> down, Action<Control> up, bool focusFirstBox = false)
        {
            using (var dialog = TallRows(out var list, out var holders))
            {
                using (holders)
                {
                    ShownWhereClicksLand(dialog);
                    var view = ProfileRows.View(list);
                    if (focusFirstBox)
                    {
                        // The cues shown before the first key, which would otherwise draw the
                        // focus again once the key has been handled.
                        TestNativeMethods.SendMessage(dialog.Handle, ChangeUiStateMessage, (IntPtr)ShowFocusCues, IntPtr.Zero);
                        Focus(BoxOf(list, 0));
                    }

                    var steps = 0;
                    using (var log = new PaintLog(view))
                    {
                        foreach (var scroll in new[] { down, up })
                        {
                            for (var step = 0; step < 200; step++)
                            {
                                var at = view.AutoScrollPosition;
                                log.Step(dialog, () => scroll(view));
                                var where = view.AutoScrollPosition.Y + " after " + steps + " steps";
                                Assert.Multiple(() =>
                                {
                                    log.AssertWhole(list, at, where);
                                    AssertWhole(list, where);
                                });
                                steps++;
                                if (view.AutoScrollPosition == at)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    Assert.That(steps, Is.GreaterThan(4), "the table scrolled by too few steps to measure");
                    Assert.That(view.AutoScrollPosition.Y, Is.Zero, "back at the top");
                }
            }
        }

        /// <summary>
        /// What the view and every part inside it paint during a step and after it: a step is put
        /// on the queue, a message behind it marks where its handling ends, and the loop runs.
        /// </summary>
        /// <remarks>
        /// **A step that scrolls paints the whole view**: the view and every row in sight painting,
        /// each row over all of it that is in sight, since a view painted where the scroll uncovered
        /// it alone shows of what the scroll copied only what a paint covers. **A scroll by the wheel, the
        /// keys or the scroll bar paints in its own handling and nothing after it**, since
        /// whatever a step left to paint waits for a loop that more turns of a wheel keep busy.
        /// **A later paint with nothing to paint is no part of the table left to it**: the system
        /// draws the view's scroll bar again where a real pointer rests on it and the thumb moves
        /// under the pointer, a while after the step, and the view painted into a single buffer
        /// with its rows is then painted with an empty clip, which draws no pixel.
        /// **A box brought into view by the focus paints once, after the focus has moved**, so
        /// that the scroll and the focus reach the screen in the same paint. A view shown off the
        /// screen paints nothing at all, which is why the steps are taken on the screen and the
        /// paint is looked for rather than its absence alone.
        /// </remarks>
        private sealed class PaintLog : IDisposable
        {
            private readonly Control _view;
            private readonly List<Control> _parts;
            private readonly List<Control> _during = new List<Control>();
            private readonly List<Control> _after = new List<Control>();

            /// <summary>What painted after the step with something to paint, a clip not empty.</summary>
            private readonly List<Control> _drawnAfter = new List<Control>();

            private readonly Dictionary<Control, List<Rectangle>> _clips = new Dictionary<Control, List<Rectangle>>();
            private bool _handled = true;

            public PaintLog(Control view)
            {
                _view = view;
                _parts = new Control[] { view }.Concat(ControlTree.Descendants(view)).ToList();
                _parts.ForEach(part => part.Paint += Painted);
            }

            /// <summary>
            /// Puts a step on the queue, marks where its handling ends, and runs the loop.
            /// </summary>
            public void Step(Form dialog, Action step)
            {
                OffScreenForm.Pump();
                _during.Clear();
                _after.Clear();
                _drawnAfter.Clear();
                _clips.Clear();
                _handled = false;
                step();
                dialog.BeginInvoke((MethodInvoker)(() => _handled = true));
                OffScreenForm.Pump();
                Assert.That(_handled, Is.True, "the step was never handled");
            }

            /// <summary>
            /// Asserts that the step, where it scrolled the view from <paramref name="from"/>,
            /// painted the whole view and every row in sight in its own handling, and that
            /// nothing painted after it with anything to paint.
            /// </summary>
            public void AssertWhole(Control list, Point from, string where)
            {
                Assert.That(_drawnAfter.Select(Named), Is.Empty, "parts of the table left to a later paint, at " + where);
                if (((ScrollableControl)_view).AutoScrollPosition != from)
                {
                    AssertPaintedOnce(list, _during, where);
                }
            }

            /// <summary>
            /// Asserts that the step, where it scrolled the view from <paramref name="from"/>,
            /// painted the whole view and every row in sight once, in its handling or after it.
            /// </summary>
            public void AssertWholeOnce(Control list, Point from, string where)
            {
                if (((ScrollableControl)_view).AutoScrollPosition != from)
                {
                    AssertPaintedOnce(list, _during.Concat(_after).ToList(), where);
                }
            }

            public void Dispose()
            {
                _parts.ForEach(part => part.Paint -= Painted);
            }

            private static string Named(Control part)
            {
                return part.Name + " " + part.Text.Split('\n')[0].Trim();
            }

            private void AssertPaintedOnce(Control list, IReadOnlyList<Control> painted, string where)
            {
                Assert.That(painted, Does.Contain(_view), "the view left unpainted, at " + where);
                var inSight = ProfileRows.Of(list).Where(row => row.Bottom > 0 && row.Top < _view.ClientSize.Height).ToList();
                Assert.That(
                    inSight.Where(row => painted.Count(part => part == row) != 1).Select(row => ProfileRows.Cell(row, GateTab.ApprovalProfileColumnName).Text),
                    Is.Empty,
                    "rows in sight painted other than once, at " + where);
                foreach (var row in inSight)
                {
                    var shown = Rectangle.Intersect(row.Bounds, _view.ClientRectangle);
                    shown.Offset(-row.Left, -row.Top);
                    Assert.That(_clips[row], Is.EqualTo(new[] { shown }),
                        "the row of " + ProfileRows.Cell(row, GateTab.ApprovalProfileColumnName).Text + " painted other than where it is in sight, at " + where);
                }
            }

            private void Painted(object sender, PaintEventArgs e)
            {
                var part = (Control)sender;
                (_handled ? _after : _during).Add(part);
                if (_handled && !e.ClipRectangle.IsEmpty)
                {
                    _drawnAfter.Add(part);
                }

                if (!_clips.TryGetValue(part, out var clips))
                {
                    _clips[part] = clips = new List<Rectangle>();
                }

                clips.Add(e.ClipRectangle);
            }
        }

        /// <summary>
        /// Asserts of every row that it stands where the view's place puts it, straight under the
        /// row before it, with its box and its cells on its first line and its clients a line each
        /// from there down.
        /// </summary>
        private static void AssertWhole(Control list, string where)
        {
            var view = ProfileRows.View(list);
            var rows = ProfileRows.Of(list);
            var single = rows.Min(row => row.Height);
            var line = CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height;
            var pad = (single - line) / 2;
            Assert.That(rows[0].Top, Is.EqualTo(view.AutoScrollPosition.Y), "the first row other than where the view's place puts it, at " + where);
            for (var at = 0; at < rows.Count; at++)
            {
                var row = rows[at];
                var box = ProfileRows.Box(row);
                var name = ProfileRows.Cell(row, GateTab.ApprovalProfileColumnName);
                var heldBy = ProfileRows.Cell(row, GateTab.ApprovalHeldByColumnName);
                var what = name.Text + " at " + where;
                if (at > 0)
                {
                    Assert.That(row.Top, Is.EqualTo(rows[at - 1].Bottom), "the row of " + what + " other than under the row before it");
                }

                Assert.That(name.Top, Is.EqualTo(pad), "the name of " + what);
                Assert.That(box.Top + (box.Height / 2), Is.EqualTo(name.Top + (line / 2)).Within(1), "the box of " + what);
                if (heldBy.Visible)
                {
                    Assert.That(ProfileRows.Cell(row, GateTab.ApprovalStateColumnName).Top, Is.EqualTo(pad), "the state of " + what);
                    Assert.That(heldBy.Top, Is.EqualTo(pad), "the clients of " + what);
                    Assert.That(heldBy.Height, Is.EqualTo(line * heldBy.Text.Split('\n').Length), "the clients of " + what + " other than a line each");
                }
            }
        }

        /// <summary>
        /// A first dialog of four rows: `a`, `c` and `d` removed names twenty clients regain, each
        /// taller than the table, and `b` on the list, so that a tall row is first, in the middle
        /// and last, and two tall rows follow each other; and the clients' certificates.
        /// </summary>
        private static PairingApprovalForm TallRows(out Control list, out IssuedClients holders)
        {
            using (var authority = TestAuthority.New())
            {
                holders = new IssuedClients(Enumerable.Range(0, 20)
                    .Select(at => authority.IssueClient("agent-" + at.ToString("00", System.Globalization.CultureInfo.InvariantCulture))));
                var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "a", "c", "d" }, InventedIssuer.Id)).ToList();
                var dialog = new PairingApprovalForm(
                    new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                        new[] { "a", "b", "c", "d" }, new[] { "b" }, "127.0.0.1:60001",
                        new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "a", records }, { "c", records }, { "d", records } }),
                    Timeout);
                list = Profiles(dialog);
                return dialog;
            }
        }

        /// <summary>
        /// A dialog shown again for these profiles, all approved on the dialog before: the dialog
        /// before read these names on the list, and the reading now holds these on the list and
        /// these removed.
        /// </summary>
        private static PairingRequest ShownAgainOver(string[] asked, string[] listedBefore, string[] listedNow, string[] removedNow)
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            foreach (var name in listedBefore)
            {
                StoreSeeds.Name(store, name);
            }

            KeePassGate.Gate.AskedProfiles before;
            using (var reading = store.Read())
            {
                before = KeePassGate.Gate.AskedProfiles.Of(reading, asked, DateTimeOffset.UtcNow);
            }

            return new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, listedNow,
                "127.0.0.1:60001", null, null, removedNow, PairingApproval.Approved(asked), before);
        }

        /// <summary>What the scroll bar sends a view it scrolls, as a press on the bar sends it.</summary>
        private static void SendScrollBar(Control view, int how)
        {
            TestNativeMethods.SendMessage(view.Handle, WmVScroll, (IntPtr)how, IntPtr.Zero);
        }

        private const int WmVScroll = 0x0115;

        private const int LineUp = 0;

        private const int LineDown = 1;

        private const int PageUp = 2;

        private const int PageDown = 3;

        [Test]
        public void A_box_the_keyboard_reached_draws_the_focus_clear_of_itself_checked_or_not_and_on_a_highlighted_row()
        {
            // A check box draws the focus round its text, and a box with none drew it nowhere; a
            // rectangle on the box's own edge was dots on the blue of a checked box. The row draws
            // it a gap clear of the box, on the row's own background, highlighted or not.
            using (var dialog = new PairingApprovalForm(ShownAgainOverAChange("ops"), Timeout))
            {
                ShownAway(dialog);
                TestNativeMethods.SendMessage(dialog.Handle, ChangeUiStateMessage, (IntPtr)ShowFocusCues, IntPtr.Zero);
                OffScreenForm.Pump();
                AssertFocusClearOfEveryBox(Profiles(dialog));
            }
        }

        /// <summary>
        /// Gives each box of the table the focus in turn, unchecked and then checked, and asserts by
        /// the pixels of its row drawn that the box itself is drawn as away from the focus and that
        /// the focus is drawn on the row clear of the box, inside the row,
        /// <see cref="AssertFocusInside"/>.
        /// </summary>
        private static void AssertFocusClearOfEveryBox(Control list)
        {
            var rows = ProfileRows.Of(list);
            Assert.That(rows[0].BackColor, Is.EqualTo(PairingApprovalForm.ChangedBackground), "a highlighted row among them");
            foreach (var row in rows)
            {
                foreach (var isChecked in new[] { false, true })
                {
                    ProfileRows.Box(row).Checked = isChecked;
                    AssertFocusInside(list, row, "checked " + isChecked + " on " + row.BackColor);
                }
            }
        }

        /// <summary>
        /// Gives a row's box the focus, from the choice of lifetime under the table, which scrolls
        /// nothing, and asserts by the pixels of the row drawn either way that the box itself is
        /// drawn as away from the focus and that the focus is drawn round the box, clear of it, and
        /// a pixel inside the row and inside the view.
        /// </summary>
        /// <remarks>
        /// **A pixel inside the row**, since the row's top edge meets the line under the row above
        /// it, or the view's frame, and its bottom edge is the line under it: a focus drawn along
        /// either edge merges with that line.
        /// </remarks>
        private static void AssertFocusInside(Control list, Control row, string what)
        {
            var box = ProfileRows.Box(row);
            var gap = (ProfileRows.Of(list).Min(each => each.Height) - CellOf(list, 0, GateTab.ApprovalProfileColumnName).Height) / 2;
            Focus(Lifetime(list.FindForm()));
            var top = row.Top;
            using (var away = Drawn(row))
            {
                Focus(box);
                Assert.That(row.Top, Is.EqualTo(top), "the focus scrolled the view");
                using (var focused = Drawn(row))
                {
                    var drawn = Enumerable.Range(0, row.Width).SelectMany(x => Enumerable.Range(0, row.Height).Select(y => new Point(x, y)))
                        .Where(point => focused.GetPixel(point.X, point.Y).ToArgb() != away.GetPixel(point.X, point.Y).ToArgb())
                        .ToList();
                    var at = box.Bounds;
                    Assert.That(drawn.Where(at.Contains), Is.Empty, "the box itself drawn as holding the focus, " + what);

                    var first = Math.Max(1, 1 - row.Top);
                    var last = row.Height - 3;
                    Assert.That(drawn.Where(point => point.Y < first || point.Y > last).Select(point => point.Y).Distinct(), Is.Empty,
                        "the focus drawn on an edge of the row or of the view, rows " + first + " to " + last + " of the row clear, " + what);

                    var ring = Rectangle.FromLTRB(at.Left - gap, Math.Max(first, at.Top - gap), at.Right + gap, Math.Min(last + 1, at.Bottom + gap));
                    var edge = Enumerable.Range(ring.Left, ring.Width).SelectMany(x => new[] { new Point(x, ring.Top), new Point(x, ring.Bottom - 1) })
                        .Concat(Enumerable.Range(ring.Top, ring.Height).SelectMany(y => new[] { new Point(ring.Left, y), new Point(ring.Right - 1, y) }));
                    Assert.That(edge.Count(drawn.Contains), Is.GreaterThan(ring.Width), "no focus drawn round the box, clear of it, " + what);
                }
            }
        }

        [Test]
        public void A_box_on_a_tall_row_draws_the_focus_inside_its_row_and_inside_the_view()
        {
            // A focus round a box along the row's top edge meets the view's frame on the first row.
            using (var dialog = TallRows(out var list, out var holders))
            {
                using (holders)
                {
                    ShownAway(dialog);
                    TestNativeMethods.SendMessage(dialog.Handle, ChangeUiStateMessage, (IntPtr)ShowFocusCues, IntPtr.Zero);
                    OffScreenForm.Pump();
                    var row = ProfileRows.Of(list)[0];
                    Assert.That(row.Height, Is.GreaterThan(ProfileRows.View(list).ClientSize.Height), "the first row is taller than the table");
                    AssertFocusInside(list, row, "on a tall row at the top");
                }
            }
        }

        [Test]
        public void A_box_is_as_large_as_its_glyph_so_that_the_focus_round_the_box_has_room_in_its_row()
        {
            // A check box with no text is a pixel taller than its glyph and two wider, the glyph at
            // its top left, whatever its font: on a box of that size, the focus a pixel inside its
            // row runs under the box.
            using (var dialog = New("ops", "reader"))
            {
                ShownAway(dialog);
                var box = BoxOf(Profiles(dialog), 0);
                foreach (var size in new[] { 8.25f, 16f })
                {
                    using (var font = new Font(FontFamily.GenericSansSerif, size))
                    using (var form = OffScreenForm.New())
                    using (var stock = new CheckBox { AutoSize = true, Text = string.Empty, Checked = true })
                    {
                        form.Font = font;
                        form.Controls.Add(stock);
                        var preferred = stock.GetPreferredSize(Size.Empty);
                        stock.Size = preferred;
                        using (var drawn = Drawn(stock))
                        {
                            var background = drawn.GetPixel(preferred.Width - 1, preferred.Height - 1).ToArgb();
                            var glyph = Enumerable.Range(0, preferred.Width).SelectMany(x => Enumerable.Range(0, preferred.Height).Select(y => new Point(x, y)))
                                .Where(point => drawn.GetPixel(point.X, point.Y).ToArgb() != background)
                                .ToList();
                            Assert.That(new[] { glyph.Min(point => point.X), glyph.Min(point => point.Y), glyph.Max(point => point.X), glyph.Max(point => point.Y) },
                                Is.EqualTo(new[] { 0, 0, preferred.Width - 3, preferred.Height - 2 }), "the glyph of a box of " + preferred + " at " + size + " points");
                            Assert.That(box.Size, Is.EqualTo(new Size(preferred.Width - 2, preferred.Height - 1)), "the box against its glyph");
                        }
                    }
                }
            }
        }

        /// <summary>A control drawn into a picture of its size.</summary>
        private static Bitmap Drawn(Control control)
        {
            var picture = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(picture, new Rectangle(Point.Empty, control.Size));
            return picture;
        }

        /// <summary>What asks a window to show or hide its focus cues.</summary>
        private const int ChangeUiStateMessage = 0x0127;

        /// <summary>Clear the hiding of focus cues and of access keys, `UIS_CLEAR` of both.</summary>
        private const int ShowFocusCues = 0x30002;

        [Test]
        public void Rows_of_thirty_clients_are_built_and_shown_in_work_that_grows_with_the_rows_and_no_faster()
        {
            // Built and shown on KeePass's thread, which nothing else runs on meanwhile. Held to
            // the work counted rather than to a clock, since a bound in milliseconds fails on a
            // loaded desk or a hosted runner with nothing wrong: twice the rows make no more than
            // twice the controls of the table, and no more than twice the layouts the dialog's
            // controls take as the dialog comes up.
            using (var authority = TestAuthority.New())
            using (var holders = new IssuedClients(Enumerable.Range(0, 30).Select(at => authority.IssueClient("agent-" + at))))
            {
                var thirty = Built(holders, 30);
                var sixty = Built(holders, 60);
                TestContext.Out.WriteLine(
                    "thirty rows: " + thirty.Controls + " controls, " + thirty.Layouts + " layouts; sixty rows: "
                    + sixty.Controls + " controls, " + sixty.Layouts + " layouts");
                Assert.That(sixty.Controls, Is.GreaterThan(thirty.Controls), "the rows were not counted");
                Assert.That(sixty.Controls, Is.LessThanOrEqualTo(2 * thirty.Controls), "the controls grow faster than the rows");
                Assert.That(sixty.Layouts, Is.LessThanOrEqualTo(2 * thirty.Layouts), "the layouts grow faster than the rows");
            }
        }

        /// <summary>
        /// The controls of the table of a first dialog asking for this many names, each removed and
        /// regained by each of these clients, and the layouts the dialog and its controls take
        /// as the dialog comes up.
        /// </summary>
        private static (int Controls, int Layouts) Built(IssuedClients holders, int rows)
        {
            var asked = Enumerable.Range(0, rows).Select(at => "gone-" + at).ToArray();
            var records = holders.Select(holder => AdmissionRecord.For(holder, DateTimeOffset.UtcNow, asked, InventedIssuer.Id)).ToList();
            var request = new PairingRequest(
                "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious", asked, new[] { "ops" }, "127.0.0.1:60001",
                asked.ToDictionary(name => name, name => (IReadOnlyList<AdmissionRecord>)records, StringComparer.Ordinal));
            Assert.That(asked.Select(name => request.Returning(name).Count), Is.All.EqualTo(holders.Count), "every client regains every name");

            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                var layouts = 0;
                foreach (var control in ControlTree.Descendants(dialog).Concat(new Control[] { dialog }))
                {
                    control.Layout += delegate { layouts++; };
                }

                ShownAway(dialog);
                var table = Profiles(dialog);
                Assert.That(ProfileRows.Of(table), Has.Count.EqualTo(rows));
                return (ControlTree.Descendants(table).Count(), layouts);
            }
        }

        [Test]
        public void In_a_right_to_left_window_the_columns_run_from_the_right_and_nothing_scrolls_sideways()
        {
            var names = Enumerable.Range(1, 12).Select(at => "profile-" + at).ToArray();
            foreach (var asked in new[] { names.Take(2).ToArray(), names })
            {
                using (var dialog = New(asked))
                {
                    dialog.RightToLeft = RightToLeft.Yes;
                    ShownAway(dialog);
                    var list = Profiles(dialog);
                    var view = ProfileRows.View(list);
                    var grant = ControlTree.FindByName<Label>(list, GateTab.ApprovalGrantColumnName);
                    foreach (var row in ProfileRows.Of(list))
                    {
                        var box = ProfileRows.Box(row);
                        Assert.That(box.Left, Is.GreaterThan(ProfileRows.Cell(row, GateTab.ApprovalProfileColumnName).Left), "the box is not on the right");
                        Assert.That(row.Left, Is.GreaterThanOrEqualTo(0));
                        Assert.That(row.Right, Is.LessThanOrEqualTo(view.ClientSize.Width), "a row past the view");
                        Assert.That(box.PointToScreen(Point.Empty).X, Is.InRange(
                            grant.PointToScreen(Point.Empty).X - grant.Margin.Left, grant.PointToScreen(Point.Empty).X + grant.Width),
                            "a box away from its heading");
                    }

                    Assert.That(Scrolls(view, HorizontalScrollBar), Is.False, "the table scrolls sideways");
                }
            }
        }

        [Test]
        public void Refusing_refuses()
        {
            using (var dialog = New("ops"))
            {
                dialog.Refuse();

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
            }
        }

        [Test]
        public void Escape_and_Enter_both_reach_the_refusal()
        {
            using (var dialog = New("ops"))
            {
                var refuse = Button(dialog, GateTab.RefuseButtonName);

                Assert.That(dialog.CancelButton, Is.SameAs(refuse), "Escape must not approve anything");
                Assert.That(dialog.AcceptButton, Is.SameAs(refuse), "nor must a stray Enter");
            }
        }

        /// <summary>What the close box sends, and what Alt with F4 sends.</summary>
        private const int WmSysCommand = 0x0112;

        private const int ScClose = 0xF060;

        /// <summary>What the window menu and the taskbar send straight to the window.</summary>
        private const int WmClose = 0x0010;

        [TestCase(WmSysCommand, ScClose, "the close box, and Alt with F4")]
        [TestCase(WmClose, 0, "the window menu and the taskbar")]
        public void Dismissing_the_dialog_rather_than_answering_it_refuses(int message, int command, string how)
        {
            // A dialog dismissed unread used to answer that nobody had been at the screen, because
            // only Escape and Enter reach the refusal and none of these reaches a button at all.
            // That answer throws away the decision somebody just made: the invitation is not spent,
            // so the client that was turned away presents it again, and goes on presenting it until
            // the token runs out, raising this dialog every time.
            //
            // Driven at the window, because a button is precisely what is not being pressed here.
            using (var dialog = New("ops"))
            {
                var window = Windowed(dialog);

                TestNativeMethods.SendMessage(window, message, (IntPtr)command, IntPtr.Zero);

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused),
                    "somebody was at the screen, and what they did with " + how + " was say no");
            }
        }

        [Test]
        public void Approving_on_a_dialog_that_really_closes_is_still_an_approval()
        {
            // Approving closes the dialog, and that close arrives where a dismissal arrives. A
            // refusal recorded there without asking whether an answer had already been given would
            // turn every press of Approve on a real screen into a refusal, and no test on a dialog
            // that has no window can see it: such a form is disposed rather than closed.
            using (var dialog = New("ops"))
            {
                Windowed(dialog);
                dialog.Approve();

                var answer = dialog.Answer();

                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
                Assert.That(answer.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void Refusing_on_a_dialog_that_really_closes_is_still_a_refusal()
        {
            using (var dialog = New("ops"))
            {
                Windowed(dialog);
                dialog.Refuse();

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
            }
        }

        [Test]
        public void A_dialog_the_countdown_closes_has_timed_out_rather_than_refused()
        {
            // The trap in reading `CloseReason` instead of what was answered: `Form.Close` reports
            // `UserClosing` whoever called it, so the clock's own close is indistinguishable from a
            // press on the close box. Reading that as a refusal would spend the invitation of an
            // operator who was never at the screen, which is the one thing a timeout exists not to
            // do.
            using (var dialog = new PairingApprovalForm(
                Request("agent-fictitious", new[] { "ops" }), TimeSpan.Zero))
            {
                var closed = false;
                dialog.FormClosed += delegate { closed = true; };
                Windowed(dialog);

                Assert.That(PumpUntil(() => closed, TimeSpan.FromSeconds(10)), Is.True,
                    "the countdown closes the dialog it is counting down");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut));
            }
        }

        /// <summary>
        /// The dialog's window, forced into existence without the dialog being shown.
        /// </summary>
        /// <remarks>
        /// A form with no window of its own is disposed by `Close` rather than closed by it, so
        /// nothing about closing one can be seen without this.
        /// </remarks>
        private static IntPtr Windowed(Form dialog)
        {
            dialog.CreateControl();

            var window = dialog.Handle;

            Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(dialog.IsHandleCreated, Is.True);

            return window;
        }

        /// <summary>Runs the message loop until it has happened, or long enough to say it did not.</summary>
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

        [Test]
        public void What_is_asked_for_arrives_checked_so_that_agreeing_is_still_a_decision()
        {
            using (var dialog = New("ops", "readonly"))
            {
                Assert.That(Checks(Profiles(dialog)), Is.EqualTo(new[] { true, true }));
            }
        }

        [Test]
        public void The_dialog_says_which_database_and_which_client()
        {
            // A table, because every one of these is a value the operator is checking against what
            // they expect rather than a sentence they are reading.
            var facts = PairingApprovalForm.Facts(Request("agent-fictitious", new[] { "ops" }));

            Assert.That(Named(facts, "Database"), Is.EqualTo("Invented Vault"));
            Assert.That(Named(facts, "Fingerprint"), Is.EqualTo("aaaabbbbccccdddd"));
            Assert.That(Named(facts, "Asking to be called"), Is.EqualTo("agent-fictitious"));
            Assert.That(Named(facts, "Invitation id"), Is.EqualTo("abcdef01"));
            Assert.That(Named(facts, "Connected from"), Is.EqualTo("127.0.0.1:60001"));

            foreach (var fact in facts)
            {
                Assert.That(fact.Value, Does.Not.Contain(fact.Name), "the value carries no label of its own");
            }
        }

        [Test]
        public void What_approving_costs_is_said_where_it_can_be_read()
        {
            using (var dialog = New("ops"))
            {
                Assert.That(Texts(dialog), Does.Contain(PairingApprovalForm.Consequence));
                Assert.That(PairingApprovalForm.Consequence, Is.EqualTo(
                    "Approve issues a certificate and saves this database. The KPG tab revokes the certificate."));
            }
        }

        [Test]
        public void Every_sentence_names_its_nouns_and_no_profile_or_client()
        {
            // The table shows which rows a sentence is about, so no sentence names a profile or a
            // client, and no pronoun stands for a noun. Driven with every name that would be seen,
            // a profile of each state and clients holding a profile; the table's cells and the
            // facts above hold the names, and nothing else does.
            using (var authority = TestAuthority.New())
            using (var holder = authority.IssueClient("dromedary-holder"))
            {
                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "emu-client",
                    new[] { "aardvark-listed", "bison-new", "camel-removed", "gnu-removed" }, new[] { "aardvark-listed" },
                    "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>>
                    {
                        { "camel-removed", new[] { AdmissionRecord.For(holder, DateTimeOffset.UtcNow, new[] { "camel-removed" }, InventedIssuer.Id) } }
                    },
                    null,
                    new[] { "gnu-removed" });
                var names = request.Profiles.Concat(new[] { "dromedary-holder", "emu-client" }).ToArray();

                using (var dialog = new PairingApprovalForm(request, Timeout))
                {
                    var details = ControlTree.FindByName<Control>(dialog, GateTab.ApprovalDetailLabelName);
                    var table = Profiles(dialog);
                    var sentences = ControlTree.Descendants(dialog).OfType<Label>()
                        .Where(label => !ControlTree.Descendants(details).Contains(label) && !ControlTree.Descendants(table).Contains(label))
                        .Select(label => label.Text)
                        .Concat(new[] { dialog.Text })

                        // And the tooltip of every state of the table, and the lines of other
                        // dialogs: the notice and the tip of a dialog shown again.
                        .Concat(ProfileRows.Of(table)
                            .Select(row => TipOf(table, ProfileRows.Cell(row, GateTab.ApprovalStateColumnName)))
                            .Where(tip => !string.IsNullOrEmpty(tip))
                            .Select(tip => tip.Replace(Tooltips.LeftToRightMark.ToString(), string.Empty).Replace(Environment.NewLine, " ")))
                        .Concat(new[]
                        {
                            PairingApprovalForm.StateChanged, PairingApprovalForm.NowAddsTip, PairingApprovalForm.NowRestoresTip,
                            PairingApprovalForm.NowListedTip,
                            PairingApprovalForm.NoProfilesAsked
                        })
                        .ToList();

                    Assert.That(sentences, Does.Contain(PairingApprovalForm.NewTip).And.Contain(PairingApprovalForm.RemovedTip));
                    foreach (var sentence in sentences)
                    {
                        foreach (var name in names)
                        {
                            Assert.That(sentence, Does.Not.Contain(name), "a sentence names " + name);
                        }

                        foreach (var pronoun in new[] { " it ", " it.", " it;", " it,", "It ", " them", " one ", " you", "You " })
                        {
                            Assert.That(sentence, Does.Not.Contain(pronoun), "a pronoun in: " + sentence);
                        }
                    }

                    var cells = string.Join(" ", Rows(Profiles(dialog)).SelectMany(row => row));
                    Assert.That(cells, Does.Contain("camel-removed").And.Contain("dromedary-holder"), "the table holds the names");
                }
            }
        }

        [Test]
        public void The_line_above_the_table_names_what_the_table_lists()
        {
            // "It is asking for these" made the reader look back up to find out what "these" meant,
            // and what it meant was the whole decision. The heading of the boxes carries what a
            // check does.
            using (var dialog = New("ops"))
            {
                Assert.That(Texts(dialog), Does.Contain(PairingApprovalForm.ProfilesAsked));
                Assert.That(PairingApprovalForm.ProfilesAsked, Is.EqualTo("Profiles the client asks for:"));
                Assert.That(PairingApprovalForm.Asking(Request("agent-fictitious", new[] { "invented" }, Array.Empty<string>())),
                    Is.EqualTo(PairingApprovalForm.ProfilesAsked), "the same line whatever the states");
                Assert.That(ControlTree.FindByName<Label>(Profiles(dialog), GateTab.ApprovalGrantColumnName).Text, Is.EqualTo("Grant"));
            }
        }

        [Test]
        public void A_profile_this_database_has_never_named_is_marked_as_new()
        {
            // Approving one of these does two things rather than one: it admits the client, and it
            // creates a profile every rule can then grant to. A list showing a new name exactly
            // like an existing one would be asking somebody to notice a spelling.
            using (var dialog = new PairingApprovalForm(
                Request("agent-fictitious", new[] { "ops", "invented" }, new[] { "ops" }), Timeout))
            {
                Assert.That(Rows(Profiles(dialog)), Is.EqualTo(new[]
                {
                    new[] { "ops", string.Empty, string.Empty },
                    new[] { "invented", PairingApprovalForm.NewState, string.Empty }
                }));
                Assert.That(TipOf(Profiles(dialog), CellOf(Profiles(dialog), 1, GateTab.ApprovalStateColumnName)),
                    Is.EqualTo(Tooltips.Wrapped(PairingApprovalForm.NewTip)));
                Assert.That(PairingApprovalForm.NewTip, Is.EqualTo(
                    "Approve adds the profile to this database. Rules already naming the profile apply at once."));
                Assert.That(TipOf(Profiles(dialog), CellOf(Profiles(dialog), 0, GateTab.ApprovalStateColumnName)), Is.Empty,
                    "a name on the list has no state to explain");
                Assert.That(LinesUnder(dialog), Is.Empty);
            }
        }

        [Test]
        public void A_list_longer_than_it_shows_ends_on_a_line_cut_in_half_and_Approve_grants_what_is_out_of_sight_too()
        {
            // J056: ten harmless names first and the wanted name after, shown as new below the
            // fold, and a glance at the ten approved all of them. A line cut in half at the bottom
            // of the table shows that there is more, where the eye reaches the end of the table,
            // and no line under the table states it in words.
            var asked = Enumerable.Range(1, 32).Select(at => "profile-" + at).ToArray();
            var request = Request("agent-fictitious", asked, asked.Take(31).ToArray());

            using (var ten = new PairingApprovalForm(Request("agent-fictitious", asked.Take(10).ToArray()), Timeout))
            {
                ShownAway(ten);
                var list = Profiles(ten);
                Assert.That(ProfileRows.Of(list)[ProfileRows.Of(list).Count - 1].Bottom, Is.EqualTo(ProfileRows.View(list).ClientSize.Height),
                    "ten fit, and the view ends where the rows end, with no half line of empty room");
                Assert.That(LinesUnder(ten), Is.Empty);
            }

            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                ShownAway(dialog);
                AssertCutInHalf(Profiles(dialog), "thirty-two names");
                Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table");

                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(asked),
                    "what the line cut in half shows is there is exactly what is approved");
            }
        }

        /// <summary>
        /// Asserts that the view's bottom edge cuts the line it meets in half, as the view stands
        /// scrolled: of the line between two edges of lines, a row's top or the top of a line of a
        /// row past its first or the end of the rows, half stands in the view and the rest below
        /// it, a pixel less in the view where the line is an odd number of pixels tall.
        /// </summary>
        private static void AssertCutInHalf(Control list, string what)
        {
            var view = ProfileRows.View(list);
            var rows = ProfileRows.Of(list);
            var name = CellOf(list, 0, GateTab.ApprovalProfileColumnName);
            var line = name.Height;
            var pad = name.Top;
            var edges = rows
                .SelectMany(row => new[] { row.Top }.Concat(
                    Enumerable.Range(1, ((row.Height - (2 * pad)) / line) - 1).Select(at => row.Top + pad + (at * line))))
                .Concat(new[] { rows[rows.Count - 1].Bottom })
                .ToList();
            var bottom = view.ClientSize.Height;
            var above = edges.Last(edge => edge <= bottom);
            Assert.That(edges.Any(edge => edge > bottom), Is.True, what + ": nothing below the view to cut");
            var below = edges.First(edge => edge > bottom);
            Assert.That(bottom - above, Is.EqualTo((below - above) / 2),
                what + ": the view's bottom at " + bottom + " cuts the line from " + above + " to " + below + " other than in half");
        }

        [Test]
        public void What_is_approved_is_the_profile_rather_than_what_the_table_states_of_it()
        {
            // The state is for the reader, in a column of its own. A record naming "invented
            // (new)" would be a record for a profile no certificate can carry and no rule can name.
            using (var dialog = new PairingApprovalForm(
                new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "invented", "retired" }, Array.Empty<string>(), "127.0.0.1:60001",
                    null, null, new[] { "retired" }),
                Timeout))
            {
                Assert.That(Rows(Profiles(dialog)).Select(row => row[0]), Is.EqualTo(new[] { "invented", "retired" }));

                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "invented", "retired" }));
            }
        }

        [Test]
        public void A_removed_profile_is_marked_and_every_client_that_regains_it_is_named()
        {
            // Pairing writes the approved names onto the list, so approving a removed name adds it
            // back, and every client approved for it before regains it. The operator approving one
            // client is told who else that approves.
            using (var authority = TestAuthority.New())
            using (var seven = authority.IssueClient("agent-seven"))
            using (var runner = authority.IssueClient("ci-runner"))
            {
                var holding = new[]
                {
                    AdmissionRecord.For(runner, DateTimeOffset.UtcNow, new[] { "retired" }, InventedIssuer.Id),
                    AdmissionRecord.For(seven, DateTimeOffset.UtcNow, new[] { "retired", "ops" }, InventedIssuer.Id)
                };

                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "ops", "retired" }, new[] { "ops" }, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "retired", holding } });

                Assert.That(request.IsNew("retired"), Is.False, "it was named once, so approving it creates nothing");

                using (var dialog = new PairingApprovalForm(request, Timeout))
                {
                    Assert.That(Rows(Profiles(dialog)), Is.EqualTo(new[]
                    {
                        new[] { "ops", string.Empty, string.Empty },
                        new[] { "retired", PairingApprovalForm.RemovedState, "agent-seven" + Environment.NewLine + "ci-runner" }
                    }));

                    Assert.That(TipOf(Profiles(dialog), CellOf(Profiles(dialog), 1, GateTab.ApprovalStateColumnName)),
                        Is.EqualTo(Tooltips.Wrapped(PairingApprovalForm.RemovedTip)));
                    Assert.That(PairingApprovalForm.RemovedTip, Is.EqualTo(
                        "Approve restores the profile for every rule and client still naming the profile."));
                    Assert.That(LinesUnder(dialog), Is.Empty);

                    dialog.Approve();
                    Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops", "retired" }),
                        "what is approved is the name, not what the table states of it");
                }
            }
        }

        [Test]
        public void Clients_regaining_a_name_are_counted_by_client_key_and_told_apart()
        {
            // A client that renewed holds the name through two records and is one client; two
            // programs that chose one name are two, each named with the start of its client key.
            using (var authority = TestAuthority.New())
            using (var older = authority.IssueClient("agent-renewing"))
            using (var renewed = authority.IssueClient("agent-renewing"))
            using (var one = authority.IssueClient("agent"))
            using (var other = authority.IssueClient("agent"))
            {
                var paired = AdmissionRecord.For(older, DateTimeOffset.UtcNow, new[] { "retired" }, InventedIssuer.Id);
                var first = AdmissionRecord.For(one, DateTimeOffset.UtcNow, new[] { "retired" }, InventedIssuer.Id);
                var second = AdmissionRecord.For(other, DateTimeOffset.UtcNow, new[] { "retired" }, InventedIssuer.Id);
                var holding = new[]
                {
                    paired,
                    AdmissionRecord.For(renewed, DateTimeOffset.UtcNow, new[] { "retired" }, InventedIssuer.Id, paired.ClientKey, paired.AdmissionKey),
                    first,
                    second
                };

                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "retired" }, new[] { "ops" }, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "retired", holding } });

                var apart = new[] { first.ClientKey, second.ClientKey }
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .Select(key => "agent (" + key.Substring(0, ClientList.ShortKeyLength) + ")");

                Assert.That(PairingApprovalForm.Cells(request, "retired")[2], Is.EqualTo(
                    string.Join(Environment.NewLine, apart.Concat(new[] { "agent-renewing" }))));
            }
        }

        [Test]
        public void Many_removed_profiles_held_by_many_clients_name_every_client_in_a_table_no_taller()
        {
            // Every client holding a profile is named, a line each, and the dialog grows with the
            // profiles and the clients no further than the most the table shows: twelve names held
            // by fifteen clients pushed Refuse, Approve and the countdown off the screen when every
            // holder was named in a sentence under the list. The table stops within half a line of
            // the height of ten names of a single line, a line cut in half at its bottom, and
            // scrolls.
            using (var authority = TestAuthority.New())
            {
                using (var holders = new IssuedClients(Enumerable.Range(0, 15)
                    .Select(i => authority.IssueClient("agent-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))))
                {
                    var removed = Enumerable.Range(0, 12).Select(i => "gone-" + i).ToArray();
                    var records = holders.Select(c => AdmissionRecord.For(c, DateTimeOffset.UtcNow, removed, InventedIssuer.Id)).ToList();
                    var returning = removed.ToDictionary(
                        name => name, name => (IReadOnlyList<AdmissionRecord>)records, StringComparer.Ordinal);

                    var request = new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                        removed, new[] { "ops" }, "127.0.0.1:60001", returning);

                    Assert.That(
                        removed.Select(name => PairingApprovalForm.Cells(request, name)[2]),
                        Has.All.EqualTo(string.Join(Environment.NewLine, Enumerable.Range(0, 15).Select(i => "agent-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))));
                    Assert.That(removed.Select(name => PairingApprovalForm.StateTip(request, name)), Has.All.EqualTo(PairingApprovalForm.RemovedTip));

                    using (var crowded = new PairingApprovalForm(request, Timeout))
                    using (var single = New(removed))
                    {
                        ShownAway(crowded);
                        ShownAway(single);
                        var line = CellOf(Profiles(single), 0, GateTab.ApprovalProfileColumnName).Height;
                        var most = PairingApprovalForm.MostRows * ProfileRows.Of(Profiles(single))[0].Height;
                        Assert.That(ProfileRows.View(Profiles(crowded)).ClientSize.Height, Is.InRange(most - line, most + line),
                            "the table grew with the clients past the most it shows");
                        Assert.That(crowded.Width, Is.EqualTo(single.Width), "the dialog grew sideways with the clients");
                        AssertCutInHalf(Profiles(crowded), "twelve names held by fifteen clients");
                        AssertCutInHalf(Profiles(single), "twelve names");
                        Assert.That(LinesUnder(crowded), Is.Empty, "a line under the table");
                    }
                }
            }
        }

        [Test]
        public void However_many_names_the_list_holds_the_dialog_promises_what_approving_adds()
        {
            // The list keeps no ceiling, `STORE.md` §616: a pairing writes every name it approves,
            // so a new name is created and a removed name given back to its holders whatever the
            // list already holds.
            var known = Enumerable.Range(0, 100).Select(i => "named-" + i).ToArray();

            using (var authority = TestAuthority.New())
            using (var runner = authority.IssueClient("ci-runner"))
            {
                var holding = new[] { AdmissionRecord.For(runner, DateTimeOffset.UtcNow, new[] { "retired" }, InventedIssuer.Id) };
                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "named-0", "fresh", "retired" }, known, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "retired", holding } });

                Assert.That(request.Profiles.Select(profile => PairingApprovalForm.StateTip(request, profile)),
                    Is.EqualTo(new[] { null, PairingApprovalForm.NewTip, PairingApprovalForm.RemovedTip }));
                Assert.That(PairingApprovalForm.Cells(request, "retired")[2], Is.EqualTo("ci-runner"));

                var listed = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "named-0" }, known, "127.0.0.1:60001");
                Assert.That(PairingApprovalForm.StateTip(listed, "named-0"), Is.Null,
                    "a list that names every name asked for shows no name as new");
            }
        }

        [Test]
        public void A_dialog_of_names_on_the_list_has_no_line_under_the_table_and_no_tip_on_a_state()
        {
            using (var dialog = New("ops"))
            {
                Assert.That(LinesUnder(dialog), Is.Empty);
                Assert.That(TipOf(Profiles(dialog), CellOf(Profiles(dialog), 0, GateTab.ApprovalStateColumnName)), Is.Empty);
                Assert.That(ProfileRows.Spoken(Profiles(dialog)), Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void The_dialog_names_the_database_rather_than_only_its_fingerprint()
        {
            // Sixteen hexadecimal characters is not something anybody recognizes. The fingerprint
            // stays, because it is what actually identifies a database and two can share a name.
            var facts = PairingApprovalForm.Facts(Request("agent-fictitious", new[] { "ops" }));

            Assert.That(Named(facts, "Database"), Is.EqualTo("Invented Vault"));
            Assert.That(Named(facts, "Fingerprint"), Is.EqualTo("aaaabbbbccccdddd"));
        }

        [Test]
        public void A_database_with_no_name_is_still_named_by_its_fingerprint()
        {
            var facts = PairingApprovalForm.Facts(new PairingRequest(
                "aaaabbbbccccdddd", "abcdef01", "agent-fictitious", Array.Empty<string>(), null));

            Assert.That(Named(facts, "Database"), Is.EqualTo("aaaabbbbccccdddd"));
        }

        [Test]
        public void It_says_that_approving_saves_the_database()
        {
            // Which is the one thing on this dialog that touches the file. An operator who does not
            // expect it finds a saved database and cannot tell what else went with it.
            Assert.That(PairingApprovalForm.Consequence, Does.Contain("saves this database"));
        }

        [Test]
        public void The_invitation_row_names_the_invitation_rather_than_the_code()
        {
            var request = new PairingRequest(
                "aaaabbbbccccdddd", "abcdef01", "agent-fictitious", Array.Empty<string>(), null);

            Assert.That(
                Named(PairingApprovalForm.Facts(request), "Invitation id"), Is.EqualTo("abcdef01"));
        }

        [Test]
        public void It_says_when_nothing_at_all_is_being_asked_for()
        {
            using (var dialog = New())
            {
                Assert.That(Texts(dialog), Does.Contain("The client asks for no profiles"));
            }
        }

        private static string Named(IReadOnlyList<Fact> facts, string name)
        {
            foreach (var fact in facts)
            {
                if (fact.Name == name)
                {
                    return fact.Value;
                }
            }

            Assert.Fail("no fact called " + name);
            return null;
        }

        [Test]
        public void It_says_that_it_times_out_and_what_that_costs()
        {
            using (var dialog = New("ops"))
            {
                var shown = Texts(dialog);

                Assert.That(shown, Does.Contain("Times out"),
                    "a dialog that answers for the operator has to say that it will");
                Assert.That(shown, Does.Contain("nothing is issued"),
                    "and which way it answers, because the two are not the same news");
            }
        }

        [Test]
        public void A_request_is_needed_to_ask_about()
        {
            Assert.Throws<ArgumentNullException>(() => new PairingApprovalForm(null, Timeout));
            Assert.Throws<ArgumentNullException>(() => PairingApprovalForm.Facts(null));
        }

        /// <summary>A dialog for profiles this database already names, which is the usual case.</summary>
        private static PairingApprovalForm New(params string[] profiles)
        {
            return new PairingApprovalForm(Request("agent-fictitious", profiles, profiles), Timeout);
        }

        /// <summary>The same, for a client that asked for a lifetime.</summary>
        private static PairingApprovalForm New(TimeSpan lifetime, params string[] profiles)
        {
            return new PairingApprovalForm(
                new PairingRequest(
                    "aaaabbbbccccdddd",
                    "Invented Vault",
                    "abcdef01",
                    "agent-fictitious",
                    profiles,
                    profiles,
                    "127.0.0.1:60001",
                    null,
                    lifetime),
                Timeout);
        }

        private static ComboBox Lifetime(Control form)
        {
            var list = ControlTree.FindByName<ComboBox>(form, GateTab.ApprovalLifetimeName);
            Assert.That(list, Is.Not.Null, "no choice of lifetime");
            return list;
        }

        /// <summary>The same dialog, built at a font a scaled display would hand it.</summary>
        private static PairingApprovalForm New(Font font, params string[] profiles)
        {
            return new PairingApprovalForm(Request("agent-fictitious", profiles, profiles), Timeout, font);
        }

        private static PairingRequest Request(string name, string[] profiles)
        {
            return Request(name, profiles, profiles);
        }

        private static PairingRequest Request(string name, string[] profiles, string[] known)
        {
            return new PairingRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "abcdef01",
                name,
                profiles,
                known,
                "127.0.0.1:60001");
        }

        private static Button Button(Control form, string name)
        {
            var button = ControlTree.FindByName<Button>(form, name);
            Assert.That(button, Is.Not.Null, "no button called " + name);
            return button;
        }

        private static Control Profiles(Control form)
        {
            return ProfileRows.Table(form, GateTab.ApprovalProfilesListName);
        }

        /// <summary>
        /// Every row of the table, the cells it draws text in, in the order of the columns after
        /// the boxes: the name, the state, and the clients regaining the name, a line for each.
        /// </summary>
        private static IReadOnlyList<string[]> Rows(Control table)
        {
            return ProfileRows.Texts(table);
        }

        /// <summary>Whether each row is checked, in order.</summary>
        private static bool[] Checks(Control table)
        {
            return ProfileRows.Checks(table);
        }

        /// <summary>The box of a row, asserted to be there.</summary>
        private static CheckBox BoxOf(Control table, int row)
        {
            var rows = ProfileRows.Of(table);
            Assert.That(rows.Count, Is.GreaterThan(row), "no row " + row + " in the table");
            var box = ProfileRows.Box(rows[row]);
            Assert.That(box, Is.Not.Null, "no box in row " + row);
            return box;
        }

        /// <summary>The cell of a row under a column, asserted to be there.</summary>
        private static Label CellOf(Control table, int row, string column)
        {
            var rows = ProfileRows.Of(table);
            Assert.That(rows.Count, Is.GreaterThan(row), "no row " + row + " in the table");
            return ProfileRows.Cell(rows[row], column);
        }

        /// <summary>The tooltip a control of the table shows, empty for none.</summary>
        private static string TipOf(Control table, Control control)
        {
            return ProfileRows.Tips(table).GetToolTip(control);
        }

        /// <summary>
        /// Shows the dialog where a click lands on it, on the screen, so that a box pressed counts
        /// the press: a box counts a press only where its own window is under the point pressed.
        /// </summary>
        private static void ShownWhereClicksLand(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(0, 0);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            OffScreenForm.Pump();
        }

        /// <summary>The middle of a control, in its own coordinates.</summary>
        private static Point Middle(Control control)
        {
            return new Point(control.Width / 2, control.Height / 2);
        }

        private const int StyleIndex = -16;

        private const int HorizontalScrollBar = 0x00100000;

        private const int VerticalScrollBar = 0x00200000;

        /// <summary>Whether the table's window carries this scroll bar.</summary>
        private static bool Scrolls(Control list, int bar)
        {
            return (TestNativeMethods.GetWindowLong(list.Handle, StyleIndex) & bar) != 0;
        }

        private static IntPtr At(Point point)
        {
            return (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
        }

        /// <summary>A press and a release in the middle of a control, both on the queue before either is delivered.</summary>
        private static void Click(Control control)
        {
            TestNativeMethods.PostMessage(control.Handle, WmLButtonDown, (IntPtr)1, At(Middle(control)));
            TestNativeMethods.PostMessage(control.Handle, WmLButtonUp, IntPtr.Zero, At(Middle(control)));
            OffScreenForm.Pump();
        }

        /// <summary>
        /// What a double click in the middle of a control puts on the queue: a press, a release,
        /// the second press as a double click, and its release.
        /// </summary>
        private static void DoubleClick(Control control)
        {
            var at = At(Middle(control));
            TestNativeMethods.PostMessage(control.Handle, WmLButtonDown, (IntPtr)1, at);
            TestNativeMethods.PostMessage(control.Handle, WmLButtonUp, IntPtr.Zero, at);
            TestNativeMethods.PostMessage(control.Handle, WmLButtonDoubleClick, (IntPtr)1, at);
            TestNativeMethods.PostMessage(control.Handle, WmLButtonUp, IntPtr.Zero, at);
            OffScreenForm.Pump();
        }

        /// <summary>A notch of the wheel over a control, down where the notch is negative, as a wheel sends it.</summary>
        private static void Wheel(Control control, int notches)
        {
            PostWheel(control, notches);
            OffScreenForm.Pump();
        }

        /// <summary>Puts a notch of the wheel over a control on the queue, down where the notch is negative.</summary>
        private static void PostWheel(Control control, int notches)
        {
            var screen = control.PointToScreen(Middle(control));
            TestNativeMethods.PostMessage(control.Handle, WmMouseWheel, (IntPtr)((notches * 120) << 16), At(screen));
        }

        private const int WmMouseWheel = 0x020A;

        private static string Texts(Control form)
        {
            var text = new System.Text.StringBuilder();
            foreach (var label in ControlTree.Descendants(form).OfType<Label>())
            {
                text.AppendLine(label.Text);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Putting the dialog on the screen from a connection thread, and what happens when it cannot
    /// be put there at all.
    /// </summary>
    [TestFixture]
    public sealed class PairingApprovalPromptTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// What is left of a pairing budget with all of it still ahead, which gives a dialog the
        /// whole of its time.
        /// </summary>
        private static readonly TimeSpan Ample = TimeSpan.FromMinutes(10);

        /// <summary>How long a test waits on another thread before calling it a failure.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
        }

        [Test]
        public void The_log_line_reads_as_a_sentence_for_every_answer()
        {
            Assert.That(PairingApprovalPrompt.Outcome(PairingDecision.TimedOut, "nobody answered"),
                Is.EqualTo(" went unanswered"));
            Assert.That(PairingApprovalPrompt.Outcome(PairingDecision.Refused, "refused forever"),
                Is.EqualTo(" was refused forever"));
            Assert.That(PairingApprovalPrompt.Outcome(PairingDecision.Approved, "approved"),
                Is.EqualTo(" was approved"));
        }

        [Test]
        public void A_dialog_KeePass_s_thread_never_shows_is_answered_as_never_shown_rather_than_unanswered()
        {
            // KeePass's thread never took the turn, so nobody saw the dialog: answered as a request
            // that never got the screen, which the audit records nothing of, and not as a dialog
            // that went unanswered, for a pairing and a renewal alike.
            var prompt = new PairingApprovalPrompt(new FakeUiThread { Runs = false }, _log);

            var pairing = prompt.Ask(Request(), Timeout, Ample);
            var renewal = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(pairing.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(pairing.Shown, Is.False);
            Assert.That(renewal.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("was never shown"));
        }

        [Test]
        public void A_dialog_answered_in_its_last_seconds_is_not_logged_as_shown_late()
        {
            // `IUiThread.Run` answers false for a dialog that came up at once and outlasted the
            // wait for its turn, which a dialog given more time than that wait does: it was not
            // shown late, and its answer is the one given.
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true }, _log)
            {
                Dialog = (request, allowed) => PairingApproval.Approved(new[] { "ops" })
            };

            var answer = prompt.Ask(Request(), Timeout, PairingApprovalPrompt.Shortest + TimeSpan.FromSeconds(5));

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
            Assert.That(_log.Entries.Select(entry => entry.Message), Has.None.Contains("shown late"));
        }

        [Test]
        public void A_dialog_that_came_up_after_its_wait_is_logged_as_shown_late()
        {
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true, Late = TimeSpan.FromMilliseconds(400) }, _log)
            {
                Dialog = (request, allowed) => PairingApproval.Refused()
            };

            var answer = prompt.Ask(Request(), TimeSpan.FromMilliseconds(100), Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Refused));
            Assert.That(_log.Entries.Select(entry => entry.Message), Has.Some.Contains("shown late"));
        }

        [TestCase(PairingDecision.Refused)]
        [TestCase(PairingDecision.Approved)]
        public void An_answer_given_after_the_wait_ran_out_is_still_the_answer(PairingDecision decided)
        {
            // H006: `IUiThread.Run` answers false for work that began too late to finish inside the
            // wait and was then waited out. The dialog's own countdown starts when it is built, so
            // on a thread that reached it late the operator's answer comes after the prompt's wait,
            // and it used to be thrown away: a refusal became a timeout that spends nothing, and an
            // approval issued nothing.
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true }, _log)
            {
                Dialog = (request, timeout) => decided == PairingDecision.Refused
                    ? PairingApproval.Refused()
                    : PairingApproval.Approved(new[] { "ops" })
            };

            var answer = prompt.Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(decided));
        }

        [Test]
        public void A_dialog_that_never_answered_is_a_timeout_however_late_it_ran()
        {
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true }, _log)
            {
                Dialog = (request, timeout) => null
            };

            Assert.That(prompt.Ask(Request(), Timeout, Ample).Decision, Is.EqualTo(PairingDecision.TimedOut));
        }

        [Test]
        public void It_waits_longer_than_the_dialog_does_so_the_dialog_is_what_gives_up()
        {
            var ui = new FakeUiThread { Runs = false };
            new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout, Ample);

            Assert.That(ui.LastTimeout, Is.EqualTo(Timeout + PairingApprovalPrompt.Grace));
        }

        [Test]
        public void A_dialog_with_the_whole_budget_ahead_of_it_gets_the_operator_s_whole_time()
        {
            var given = TimeSpan.Zero;
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), _log)
            {
                Dialog = (request, allowed) =>
                {
                    given = allowed;
                    return PairingApproval.Refused();
                }
            };

            prompt.Ask(Request(), Timeout, Ample);

            Assert.That(given, Is.EqualTo(Timeout));
        }

        [Test]
        public void A_dialog_that_comes_up_late_is_given_what_is_left_rather_than_the_whole_time()
        {
            // A dialog behind another on the screen, or behind a busy KeePass, used to give the
            // operator the whole of their time from when it came up, and so took the pairing past
            // the budget its connection is closed at: the invitation spent and the record saved,
            // with no certificate handed back. What it is given now is what is left, counted from
            // when it was asked for, and its countdown starts from that.
            var late = TimeSpan.FromMilliseconds(300);
            var left = PairingApprovalPrompt.Shortest + TimeSpan.FromSeconds(10);
            var given = TimeSpan.Zero;
            var prompt = new PairingApprovalPrompt(new FakeUiThread { Late = late }, _log)
            {
                Dialog = (request, allowed) =>
                {
                    given = allowed;
                    return PairingApproval.Approved(new[] { "ops" });
                }
            };

            var answer = prompt.Ask(Request(), TimeSpan.FromMinutes(2), left);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved), "what the operator decided stands");
            Assert.That(given, Is.LessThanOrEqualTo(left - late),
                "the time the dialog waited for its turn was not taken off what it was given");
            Assert.That(given, Is.GreaterThan(left - late - Patience));
            Assert.That(_log.Entries.Select(entry => entry.Message), Has.Some.Contains("came up late"));
        }

        [Test]
        public void A_renewal_dialog_that_comes_up_late_is_given_what_is_left_as_well()
        {
            var left = PairingApprovalPrompt.Shortest + TimeSpan.FromSeconds(10);
            var given = TimeSpan.Zero;
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), _log)
            {
                RenewalDialog = (request, allowed) =>
                {
                    given = allowed;
                    return RenewalApproval.Refused(null);
                }
            };

            prompt.Ask(Renewal(), TimeSpan.FromMinutes(2), left);

            Assert.That(given, Is.LessThanOrEqualTo(left));
            Assert.That(given, Is.GreaterThan(left - Patience));
        }

        [Test]
        public void A_dialog_left_less_than_the_shortest_time_is_never_shown()
        {
            // A countdown of a few seconds is a dialog nobody can read, and an approval given in it
            // would not be a decision. So it is answered as a request that never got the screen,
            // which spends nothing and leaves the client free to ask again.
            var shown = 0;
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), _log)
            {
                Dialog = (request, allowed) =>
                {
                    shown++;
                    return PairingApproval.Approved(new[] { "ops" });
                },
                RenewalDialog = (request, allowed) =>
                {
                    shown++;
                    return RenewalApproval.Approved(new[] { "ops" }, null);
                }
            };

            var tooLittle = PairingApprovalPrompt.Shortest - TimeSpan.FromSeconds(1);
            var pairing = prompt.Ask(Request(), TimeSpan.FromMinutes(2), tooLittle);
            var renewal = prompt.Ask(Renewal(), TimeSpan.FromMinutes(2), tooLittle);

            Assert.That(shown, Is.Zero, "a dialog went up with less than the shortest time left");
            Assert.That(pairing.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(renewal.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("was not shown"));
        }

        [Test]
        public void A_dialog_left_little_of_its_time_waits_for_its_turn_no_longer_than_it_could_still_be_shown()
        {
            // A dialog the request puts to the operator again, once its write found the approval
            // stale, comes up with less than the operator's own time left. Every wait ahead of it
            // ends by the moment it could still be shown for the shortest time, so that the
            // request stays inside the budget it arrived in: the wait for KeePass's thread is that,
            // with the grace on it.
            var ui = new FakeUiThread { Runs = false };
            var beyond = TimeSpan.FromSeconds(5);

            new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout, PairingApprovalPrompt.Shortest + beyond);

            Assert.That(ui.LastTimeout, Is.EqualTo(beyond + PairingApprovalPrompt.Grace));
        }

        [Test]
        public void A_dialog_left_little_of_its_time_gives_up_on_the_screen_by_then()
        {
            // The same for the wait for the screen: a request behind another is answered as a
            // request that never got the screen once its dialog could no longer be shown for the
            // shortest time, rather than at the end of its operator's whole time.
            var ui = new HeldUiThread();
            var prompt = new PairingApprovalPrompt(ui, _log);
            var first = new Thread(() => prompt.Ask(Request(), Timeout, Ample)) { IsBackground = true };
            first.Start();

            try
            {
                Assert.That(ui.Reached(Patience), Is.True, "the first pairing has the screen");
                var waited = System.Diagnostics.Stopwatch.StartNew();

                var second = prompt.Ask(Request(), Timeout, PairingApprovalPrompt.Shortest + TimeSpan.FromMilliseconds(200));

                Assert.That(second.Decision, Is.EqualTo(PairingDecision.Unavailable));
                Assert.That(waited.Elapsed, Is.LessThan(Patience), "not the whole of its operator's time");
            }
            finally
            {
                ui.Release();
            }

            Assert.That(first.Join(Patience), Is.True, "the first pairing finished with the screen");
        }

        [Test]
        public void One_pairing_is_put_to_the_operator_at_a_time()
        {
            // `PAIRING.md` §303. Two connections can be pairing at once and each marshals its
            // dialog onto the one thread that has the windows. The second would open inside the
            // first one's modal loop, which disables every other window on that thread: the request
            // the operator is reading goes gray, cannot be answered, and times out in front of
            // them. So the second waits for the screen, gives up on it inside the time its own
            // operator was going to be given, and spends nothing.
            var ui = new HeldUiThread();
            var prompt = new PairingApprovalPrompt(ui, _log);
            var first = new Thread(() => prompt.Ask(Request(), Timeout, Ample));

            first.IsBackground = true;
            first.Start();

            try
            {
                Assert.That(ui.Reached(Patience), Is.True, "the first pairing has the screen");

                var second = prompt.Ask(Request(), TimeSpan.FromMilliseconds(50), Ample);

                Assert.That(second.Decision, Is.EqualTo(PairingDecision.Unavailable));
                Assert.That(second.Reason, Is.Not.Empty, "the log is told which pairing this was");
                Assert.That(ui.Asked, Is.EqualTo(1),
                    "a second dialog behind the first one is exactly what must not be raised");
                Assert.That(_log.Warnings, Is.Not.Empty);
            }
            finally
            {
                ui.Release();
            }

            Assert.That(first.Join(Patience), Is.True, "the first pairing finished with the screen");
        }

        [Test]
        public void A_renewal_waits_its_turn_for_the_screen_behind_a_pairing()
        {
            // A renewal raised inside a pairing's modal loop would gray it out exactly as a second
            // pairing would, so the two share the one screen rather than each having one.
            var ui = new HeldUiThread();
            var prompt = new PairingApprovalPrompt(ui, _log);
            var first = new Thread(() => prompt.Ask(Request(), Timeout, Ample));

            first.IsBackground = true;
            first.Start();

            try
            {
                Assert.That(ui.Reached(Patience), Is.True, "the pairing has the screen");

                var renewal = prompt.Ask(
                    new RenewalRequest(
                        "aaaabbbbccccdddd",
                        "Invented Vault",
                        "agent-fictitious",
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.AddDays(30),
                        new[] { "ops" },
                        Array.Empty<string>(),
                        "127.0.0.1:60001",
                        null),
                    TimeSpan.FromMilliseconds(50),
                    Ample);

                Assert.That(renewal.Decision, Is.EqualTo(PairingDecision.Unavailable));
                Assert.That(ui.Asked, Is.EqualTo(1), "no second dialog behind the first");
            }
            finally
            {
                ui.Release();
            }

            Assert.That(first.Join(Patience), Is.True);
        }

        [Test]
        public void The_screen_goes_back_to_whoever_asks_next()
        {
            // The other half of the gate: a pairing that has finished must leave the lock behind
            // it, or the first request of the day is the last one anybody is asked about.
            var ui = new HeldUiThread();
            var prompt = new PairingApprovalPrompt(ui, _log);

            ui.Release();
            prompt.Ask(Request(), Timeout, Ample);
            prompt.Ask(Request(), Timeout, Ample);

            Assert.That(ui.Asked, Is.EqualTo(2));
        }

        [Test]
        public void A_dialog_of_Windows_own_on_KeePass_s_thread_is_answered_before_anything_is_queued()
        {
            // A message box's loop never raises Idle, so the dialog would wait the whole of its
            // timeout for a turn and could not be shown under it anyway. Told at once instead.
            var ui = new FakeUiThread { Stalled = "a message box is open" };

            var answer = new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(ui.Asked, Is.Zero, "nothing was queued for KeePass's thread");
        }

        [Test]
        public void A_user_interface_that_throws_is_reported_and_approves_nothing()
        {
            var ui = new FakeUiThread { Throws = new InvalidOperationException("no handle") };

            var answer = new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void There_is_nothing_to_ask_about_without_a_request()
        {
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);

            Assert.Throws<ArgumentNullException>(() => prompt.Ask((PairingRequest)null, Timeout, Ample));
            Assert.Throws<ArgumentNullException>(() => prompt.Ask((RenewalRequest)null, Timeout, Ample));
        }

        [Test]
        public void A_request_whose_database_locked_while_it_waited_is_answered_as_locked_and_never_shown()
        {
            // Asked on KeePass's thread just before the dialog would go up, which is the thread a
            // lock runs on, so a request that waited for the screen while KeePass locked finds
            // its database no longer served.
            var shown = 0;
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), databaseId => false, _log)
            {
                Dialog = (request, timeout) =>
                {
                    shown++;
                    return PairingApproval.Approved(new[] { "ops" });
                },
                RenewalDialog = (request, timeout) =>
                {
                    shown++;
                    return RenewalApproval.Approved(new[] { "ops" });
                }
            };

            var pairing = prompt.Ask(Request(), Timeout, Ample);
            var renewal = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(pairing.Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(renewal.Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(shown, Is.Zero);

            // S081: and the answer holds that, so the audit writes down no dialog that never stood.
            Assert.That(pairing.Shown || renewal.Shown, Is.False);
            Assert.That(pairing.Reason, Does.Contain("never shown"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_lock_closes_the_dialog_on_the_screen_as_unanswered()
        {
            // `PAIRING.md` §308. The dialog holds no lock off, so a lock can fall under it, and
            // it used to stay up offering an Approve over a locked database.
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);
            prompt.RenewalDialog = (request, timeout) =>
            {
                using (var dialog = new RenewalApprovalForm(request, timeout, null, DateTime.Today, null))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new System.Drawing.Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;

                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            prompt.Locked();
                        };

                        timer.Start();
                        prompt.Show(dialog, request.DatabaseId);
                    }

                    return dialog.Answer();
                }
            };

            var answer = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(answer.Shown, Is.True, "a dialog the lock closed was put to the operator");
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("closed unanswered"));

            prompt.Locked();
            Assert.That(_log.Warnings.Count(entry => entry.Message.Contains("closed unanswered")), Is.EqualTo(1),
                "a lock with nothing on the screen closes nothing");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_lock_of_several_databases_closes_the_dialog_and_logs_it_once()
        {
            // S094. KeePass locks every open database in turn, inside the dialog's own modal loop,
            // and the close of each tells the prompt. The dialog was still on the screen for all of
            // them, so a request closed once was logged as closed once per database.
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);
            prompt.RenewalDialog = (request, timeout) =>
            {
                using (var dialog = new RenewalApprovalForm(request, timeout, null, DateTime.Today, null))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new System.Drawing.Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;

                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            prompt.Locked();
                            prompt.Locked();
                            prompt.Locked();
                        };

                        timer.Start();
                        prompt.Show(dialog, request.DatabaseId);
                    }

                    return dialog.Answer();
                }
            };

            var answer = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(_log.Warnings.Count(entry => entry.Message.Contains("closed unanswered")), Is.EqualTo(1));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_close_of_its_database_closes_the_dialog_on_the_screen_as_unanswered()
        {
            // The operator's ruling of 2026-11-08, "close it with the database". The dialog used to
            // stay up over a database closed for real, offering an Approve that could only find
            // the database gone, as a lock's dialog once did over a locked database.
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);
            OffScreenRenewal(prompt, () =>
            {
                prompt.Closed(new[] { Renewal().DatabaseId });
                prompt.Closed(new[] { Renewal().DatabaseId });
            });

            var answer = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Locked), "answered as a lock's is");
            Assert.That(answer.Reason, Is.EqualTo(PairingApproval.ClosedReason), "and named for the close");
            Assert.That(answer.Shown, Is.True, "it was put to the operator");
            Assert.That(_log.Warnings.Count(entry => entry.Message.Contains("database closed")), Is.EqualTo(1),
                "closed once, and logged once");
            Assert.That(_log.Warnings.Any(entry => entry.Message.Contains("KeePass locked")), Is.False);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_close_of_another_database_leaves_the_dialog_on_the_screen()
        {
            // R1: a close is about its own database, and every other database is still open.
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);
            OffScreenRenewal(prompt, () => prompt.Closed(new[] { "another database" }));

            var answer = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(answer.Reason, Is.EqualTo(PairingApproval.LockedReason),
                "still standing for the lock that ended it");
            Assert.That(_log.Warnings.Any(entry => entry.Message.Contains("database closed")), Is.False);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_close_that_names_no_database_closes_the_dialog_on_the_screen()
        {
            // A close whose database nothing could name lets every approval go, and so it takes
            // whatever dialog is up as well.
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);
            OffScreenRenewal(prompt, () => prompt.Closed(null));

            var answer = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(answer.Reason, Is.EqualTo(PairingApproval.ClosedReason));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_dialog_a_lock_or_a_close_takes_away_is_written_down_once_as_it_goes()
        {
            // At KeePass's exit, the close that takes the dialog away runs inside the press that
            // unloads the plugin, which waits for the lines queued by then and for nothing after.
            // So the line is recorded here, on KeePass's thread, before the dialog's loop returns.
            var audit = new RecordingAudit();
            var prompt = new PairingApprovalPrompt(
                CurrentThread.Instance, null, null, new KeePassGate.Pipeline.AdmissionAudit(audit), _log);
            var writtenAsItWent = -1;
            OffScreenRenewal(prompt, () =>
            {
                prompt.Closed(new[] { Renewal().DatabaseId });
                prompt.Locked();
                prompt.Closed(null);
                writtenAsItWent = audit.Entries.Count;
            });

            var answer = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(writtenAsItWent, Is.EqualTo(1), "written as the dialog went, and once");
            var line = audit.Entries.Single();
            Assert.That(line.Decision, Is.EqualTo(KeePassGate.Audit.AuditEntry.Unanswered));
            Assert.That(line.AdmissionKey, Is.EqualTo(Renewal().Client));
            Assert.That(line.ClientKey, Is.EqualTo(Renewal().ClientKey));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void A_dialog_answered_before_a_lock_takes_it_away_is_not_written_down_as_unanswered()
        {
            // The operator's answer stands, and the handler writes that down: the lock found it
            // already answered and closed nothing that was still a question.
            var audit = new RecordingAudit();
            var prompt = new PairingApprovalPrompt(
                CurrentThread.Instance, null, null, new KeePassGate.Pipeline.AdmissionAudit(audit), _log);
            prompt.Dialog = (request, timeout) =>
            {
                using (var dialog = new PairingApprovalForm(request, timeout))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new System.Drawing.Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;

                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            dialog.Refuse();
                            prompt.Locked();
                        };

                        timer.Start();
                        prompt.Show(dialog, request.DatabaseId);
                    }

                    return dialog.Answer();
                }
            };

            var answer = prompt.Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Refused));
            Assert.That(audit.Entries, Is.Empty);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        [NonParallelizable]
        [Desktop]
        public void The_main_window_stays_disabled_under_the_dialog_when_KeePass_unblocks_it()
        {
            // The dialog is not registered with `GlobalWindowManager`, so KeePass's periodic
            // trigger runs inside its modal loop, and a synchronize or a save it runs ends by
            // assigning `Enabled = true` to the main window: that enabled the window the loop had
            // disabled, and the operator could work in KeePass behind a request still standing.
            using (var main = OffScreenForm.New())
            {
                main.Show();
                OffScreenForm.Pump();

                var prompt = new PairingApprovalPrompt(CurrentThread.Instance, null, null, null, main, _log);
                var disabledUnderIt = false;
                var enabledAfterKeePass = true;
                OffScreenRenewal(prompt, () =>
                {
                    disabledUnderIt = !TestNativeMethods.IsWindowEnabled(main.Handle);

                    // `UIBlockInteraction(true)` and then `UIBlockInteraction(false)`.
                    main.Enabled = false;
                    main.Enabled = true;
                    enabledAfterKeePass = TestNativeMethods.IsWindowEnabled(main.Handle);
                });

                prompt.Ask(Renewal(), Timeout, Ample);

                Assert.That(disabledUnderIt, Is.True, "the dialog's modal loop disabled the main window");
                Assert.That(enabledAfterKeePass, Is.False,
                    "KeePass's unblock enabled the main window behind a request still standing");
                Assert.That(TestNativeMethods.IsWindowEnabled(main.Handle), Is.True, "and the window is back once it closes");
            }
        }

        /// <summary>
        /// A renewal dialog shown where nobody sees it, through the prompt's own show: what is given
        /// runs once it stands, and a lock comes after that to end it, should nothing else have.
        /// </summary>
        private static void OffScreenRenewal(PairingApprovalPrompt prompt, Action meanwhile)
        {
            prompt.RenewalDialog = (request, timeout) =>
            {
                using (var dialog = new RenewalApprovalForm(request, timeout, null, DateTime.Today, null))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new System.Drawing.Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;

                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        var ticks = 0;
                        timer.Tick += delegate
                        {
                            if (++ticks == 1)
                            {
                                meanwhile();
                                return;
                            }

                            timer.Stop();
                            prompt.Locked();
                        };

                        timer.Start();
                        prompt.Show(dialog, request.DatabaseId);
                    }

                    return dialog.Answer();
                }
            };
        }

        [Test]
        public void A_dialog_delivered_inside_the_plugins_own_save_is_put_back_and_shown_once_the_save_ends()
        {
            // A dialog is a marshaled call, and a save pumps: delivered there it held the save,
            // the writer and KeePass's interaction block for as long as the operator took. The
            // plugin's own save is a few waits from over, so the request waits for it.
            var writes = new ScriptedWrites { SavingFor = 2 };
            var shown = 0;
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), null, writes, _log)
            {
                Dialog = (request, timeout) =>
                {
                    shown++;
                    return PairingApproval.Approved(new[] { "ops" });
                }
            };

            var answer = prompt.Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
            Assert.That(shown, Is.EqualTo(1), "shown once, after the save");
            Assert.That(writes.SavingAsked, Is.EqualTo(3), "put back twice and shown on the third delivery");
            Assert.That(writes.QuietAsked, Is.EqualTo(3), "and the save was waited for before each");
        }

        [Test]
        public void KeePass_part_way_through_its_own_work_is_answered_busy_at_once_and_shows_nothing()
        {
            // Its own save, an import, a message box: not this plugin's to wait for, and answered
            // as a write meeting them is, `503 busy`, which spends nothing.
            var writes = new ScriptedWrites { Busy = "KeePass is importing" };
            var shown = 0;
            var prompt = new PairingApprovalPrompt(new FakeUiThread(), null, writes, _log)
            {
                Dialog = (request, timeout) =>
                {
                    shown++;
                    return PairingApproval.Approved(new[] { "ops" });
                },
                RenewalDialog = (request, timeout) =>
                {
                    shown++;
                    return RenewalApproval.Approved(new[] { "ops" });
                }
            };

            var pairing = prompt.Ask(Request(), Timeout, Ample);
            var renewal = prompt.Ask(Renewal(), Timeout, Ample);

            Assert.That(pairing.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(pairing.Reason, Is.EqualTo("KeePass is importing"));
            Assert.That(renewal.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(shown, Is.Zero);
        }

        [Test]
        public void A_save_of_the_plugins_own_that_outlasts_the_wait_for_the_screen_shows_nothing()
        {
            var writes = new ScriptedWrites { Quiets = false };
            var ui = new FakeUiThread();
            var prompt = new PairingApprovalPrompt(ui, null, writes, _log)
            {
                Dialog = (request, timeout) => PairingApproval.Approved(new[] { "ops" })
            };

            var answer = prompt.Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(ui.Asked, Is.Zero, "nothing was posted to KeePass's thread at all");
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("own save"));
        }

        [Test]
        public void A_plugin_with_no_screen_approves_nothing_and_says_why()
        {
            var answer = NoPairingApproval.Instance.Ask(Request(), Timeout, Ample);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(answer.Reason, Is.Not.Empty);
        }

        private static PairingRequest Request()
        {
            return new PairingRequest("db", "abcdef01", "agent-fictitious", Array.Empty<string>(), null);
        }

        private static RenewalRequest Renewal()
        {
            return new RenewalRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "agent-fictitious",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(30),
                new[] { "ops" },
                Array.Empty<string>(),
                "127.0.0.1:60001",
                null,
                "9f86d081",
                "c0ffee11");
        }

        /// <summary>The plugin's writes, saying what a test tells them to.</summary>
        private sealed class ScriptedWrites : IWriterState
        {
            public ScriptedWrites()
            {
                Quiets = true;
            }

            /// <summary>How many deliveries, from the first, find the plugin's save running.</summary>
            public int SavingFor { get; set; }

            /// <summary>What KeePass is part way through, or null for nothing.</summary>
            public string Busy { get; set; }

            /// <summary>Whether the write in flight ends within the wait.</summary>
            public bool Quiets { get; set; }

            public int SavingAsked { get; private set; }

            public int QuietAsked { get; private set; }

            public bool Saving
            {
                get { return ++SavingAsked <= SavingFor; }
            }

            public string Interrupting()
            {
                return Busy;
            }

            public bool Quiet(TimeSpan wait)
            {
                QuietAsked++;
                return Quiets;
            }
        }

        /// <summary>
        /// A user interface thread that stays inside the work until a test lets it out, which is
        /// what an operator reading a dialog looks like from a connection thread.
        /// </summary>
        /// <remarks>
        /// The work itself is never run, because running it puts a real modal dialog on a real
        /// screen and nothing would ever answer it. What is under test is the gate in front of the
        /// dialog rather than the dialog behind it.
        ///
        /// Every wait is bounded, including the one inside `Run`, so a test that fails fails with
        /// an assertion rather than by hanging the suite.
        /// </remarks>
        private sealed class HeldUiThread : IUiThread
        {
            private readonly object _gate = new object();

            private int _asked;
            private bool _reached;
            private bool _released;

            public int Asked
            {
                get
                {
                    lock (_gate)
                    {
                        return _asked;
                    }
                }
            }

            /// <summary>Waits until the work has been reached, which is the screen being taken.</summary>
            public bool Reached(TimeSpan patience)
            {
                lock (_gate)
                {
                    var until = DateTime.UtcNow + patience;
                    while (!_reached && DateTime.UtcNow < until)
                    {
                        Monitor.Wait(_gate, TimeSpan.FromMilliseconds(20));
                    }

                    return _reached;
                }
            }

            /// <summary>Lets go of the screen, and of anybody waiting for it afterwards.</summary>
            public void Release()
            {
                lock (_gate)
                {
                    _released = true;
                    Monitor.PulseAll(_gate);
                }
            }

            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                lock (_gate)
                {
                    _asked++;
                    _reached = true;
                    Monitor.PulseAll(_gate);

                    var until = DateTime.UtcNow + timeout;
                    while (!_released && DateTime.UtcNow < until)
                    {
                        Monitor.Wait(_gate, TimeSpan.FromMilliseconds(20));
                    }
                }

                return false;
            }
        }
    }
}
