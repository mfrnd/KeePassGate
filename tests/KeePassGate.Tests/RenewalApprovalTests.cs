using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    /// The renewal dialog, built and pressed without putting a window where anybody sees it.
    /// </summary>
    /// <remarks>
    /// What is tested is the part that decides, as for the pairing dialog: which press leads to
    /// which answer, what the dialog shows when it does, and that nothing leads to an approval or
    /// to a standing refusal by default. The menu on the Refuse button is driven through the
    /// message loop, because the keys that reach it are the point.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class RenewalApprovalFormTests
    {
        /// <summary>How wide the smallest screen the dialog is held to is, at 100%.</summary>
        private const int SmallestScreenWidth = 1024;

        /// <summary>
        /// How tall the working area of the smallest screen the dialog is held to is, at 100%: a
        /// screen 768 pixels tall less a taskbar of 48.
        /// </summary>
        private const int SmallestWorkingHeight = 720;

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        private static readonly DateTime Today = new DateTime(2026, 10, 3);

        [Test]
        public void It_says_who_is_asking_and_when_what_it_holds_now_ends()
        {
            var facts = RenewalApprovalForm.Facts(Request(new[] { "reader" }, 40));
            var said = facts.ToDictionary(fact => fact.Name, fact => fact.Value);

            Assert.That(said["Database"], Is.EqualTo("Invented Vault"));
            Assert.That(said["Client"], Is.EqualTo("agent-fictitious"));
            Assert.That(said[GateTab.RenewalExpiresFactName], Is.EqualTo("2026-11-12 12:00:00"));
            Assert.That(said[GateTab.PeerFactName], Is.EqualTo("127.0.0.1:60001"));
            Assert.That(GateTab.RenewalExpiresFactName, Is.EqualTo("Certificate expires (UTC)"));
        }

        [Test]
        public void Its_words_are_the_words_chosen()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Assert.That(dialog.Text, Is.EqualTo("KeePassGate: a client is asking to renew a certificate"));
                Assert.That(Texts(dialog), Does.Contain("A client is asking to renew a certificate."));
                Assert.That(Texts(dialog), Does.Contain(
                    "Approve issues a new certificate and saves this database. The current certificate keeps every profile "
                    + "until the client gives that certificate up."));
                Assert.That(Texts(dialog), Does.Contain("Profiles the client holds:"));
                Assert.That(Profiles(dialog).AccessibleName, Is.EqualTo("Profiles the client holds"), "the line above the table, without its colon");
                Assert.That(RenewalApprovalForm.NoProfilesHeld, Is.EqualTo("The new certificate reaches no profile."));
                Assert.That(RenewalApprovalForm.OmitsTip, Is.EqualTo(
                    "This database no longer names the profile, so the new certificate omits the profile. Approve restores no profile."));
            }

            using (var dialog = New(Request(Array.Empty<string>(), 40)))
            {
                Assert.That(Texts(dialog), Does.Contain("The new certificate reaches no profile."));
            }
        }

        [Test]
        public void A_renewal_reaching_no_profile_states_so_in_a_notice_above_the_line_naming_the_table()
        {
            // The line naming the table stays, since the table still lists what the client holds;
            // the sentence is a notice of its own above that line, in bold and on a single line, as
            // the pairing dialog shows its notice, and Approve issues the certificate it states.
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(Today.AddDays(40), TimeSpan.Zero),
                Array.Empty<string>(), new[] { "gone-a", "gone-b" }, "127.0.0.1:60001", null);
            using (var dialog = New(request))
            {
                dialog.CreateControl();
                dialog.PerformLayout();
                var notice = ControlTree.FindByName<Label>(dialog, "KpgRenewalNotice");
                var line = ControlTree.FindByName<Label>(dialog, "KpgRenewalProfilesLine");

                Assert.That(notice, Is.Not.Null, "no notice");
                Assert.That(notice.Text, Is.EqualTo("The new certificate reaches no profile."));
                Assert.That(notice.Font.Bold, Is.True, "a notice in bold");
                Assert.That(notice.Height, Is.EqualTo(line.Height), "a notice of a single line");
                Assert.That(line.Text, Is.EqualTo("Profiles the client holds:"), "the line naming the table stays");
                Assert.That(notice.Bottom, Is.LessThan(line.Top), "the notice above the line");
                Assert.That(ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName).Enabled, Is.True);
            }

            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Assert.That(ControlTree.FindByName<Label>(dialog, "KpgRenewalNotice"), Is.Null, "a notice where a profile is reached");
            }
        }

        [Test]
        public void Every_sentence_names_its_nouns_and_no_profile_or_client()
        {
            // As on the pairing dialog: the table's cells hold the names, and no other text does,
            // the tooltip of a profile the database no longer names among them.
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "emu-client",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero),
                new[] { "aardvark-listed" }, new[] { "bison-dropped" }, "127.0.0.1:60001", null);

            using (var dialog = New(request))
            {
                var details = ControlTree.FindByName<Control>(dialog, "KpgRenewalDetail");
                var table = ProfileRows.Table(dialog, GateTab.RenewalProfilesListName);
                var tips = ProfileRows.Tips(table);
                var sentences = ControlTree.Descendants(dialog).OfType<Label>()
                    .Where(label => !ControlTree.Descendants(details).Contains(label) && !ControlTree.Descendants(table).Contains(label))
                    .Select(label => label.Text)
                    .Concat(new[] { dialog.Text, RenewalApprovalForm.NoProfilesHeld })
                    .Concat(ProfileRows.Of(table)
                        .SelectMany(row => new[] { tips.GetToolTip(row), tips.GetToolTip(ProfileRows.Cell(row, GateTab.RenewalProfileColumnName)) })
                        .Where(tip => !string.IsNullOrEmpty(tip))
                        .Select(tip => tip.Replace(Tooltips.LeftToRightMark.ToString(), string.Empty).Replace(Environment.NewLine, " ")))
                    .ToList();

                Assert.That(sentences, Does.Contain(RenewalApprovalForm.OmitsTip));
                foreach (var sentence in sentences)
                {
                    foreach (var name in new[] { "aardvark-listed", "bison-dropped", "emu-client" })
                    {
                        Assert.That(sentence, Does.Not.Contain(name), "a sentence names " + name);
                    }

                    foreach (var pronoun in new[] { " it ", " it.", " it;", " it,", "It ", " its ", " them", " one ", " you", "You " })
                    {
                        Assert.That(sentence, Does.Not.Contain(pronoun), "a pronoun in: " + sentence);
                    }
                }
            }
        }

        private static string Texts(Control form)
        {
            return string.Join(Environment.NewLine, ControlTree.Descendants(form).OfType<Label>().Select(label => label.Text));
        }

        [Test]
        public void The_moments_it_prints_name_their_zone()
        {
            // Printed in UTC, as every moment on screen is, and a bare moment reads as local time:
            // wrong by the operator's offset on the dialog where the date decides something.
            var facts = RenewalApprovalForm.Facts(Request(new[] { "reader" }, 40));

            Assert.That(facts.Single(fact => fact.Value == "2026-11-12 12:00:00").Name, Does.Contain("UTC"));
            Assert.That(facts.Single(fact => fact.Value == "2026-04-01 09:00:00").Name, Does.Contain("UTC"));
        }

        [Test]
        public void The_menu_of_ways_to_refuse_is_named_to_a_screen_reader()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                var refuse = ControlTree.FindByName<SplitButton>(dialog, GateTab.RefuseButtonName);

                Assert.That(refuse.Menu.AccessibleName, Is.EqualTo(GateTab.RefuseMenuLabel));
            }
        }

        [Test]
        public void The_countdown_says_that_nothing_gets_issued()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                var countdown = ControlTree.FindByName<Label>(dialog, GateTab.RenewalCountdownName);

                Assert.That(countdown.Text, Is.EqualTo("Times out in 120 s; nothing is issued."));
            }
        }

        [Test]
        public void The_profiles_held_arrive_checked_and_leave_as_what_is_still_checked()
        {
            using (var dialog = New(Request(new[] { "reader", "ops" }, 40)))
            {
                var list = Profiles(dialog);
                Assert.That(ProfileRows.Checks(list), Is.EqualTo(new[] { true, true }), "agreeing is still a decision");

                ProfileRows.Boxes(list)[1].Checked = false;
                dialog.Approve();

                var answer = dialog.Answer();
                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
                Assert.That(answer.Profiles, Is.EqualTo(new[] { "reader" }));
                Assert.That(answer.Standing, Is.Null);
            }
        }

        [Test]
        public void A_list_longer_than_it_shows_ends_on_a_row_cut_in_half_as_the_pairing_dialog_s_does()
        {
            // As on the pairing dialog: a row cut in half at the bottom of the table shows that
            // the table scrolls, and no line under the table states it in words; a table that
            // shows every row ends where the rows end.
            var many = Enumerable.Range(1, PairingApprovalForm.MostRows + 2).Select(at => "p" + at).ToArray();
            var few = many.Take(PairingApprovalForm.MostRows).ToArray();

            using (var dialog = New(Request(many, 40)))
            {
                Show(dialog);
                var list = Profiles(dialog);
                var view = ProfileRows.View(list);
                var single = ProfileRows.Of(list)[0].Height;
                Assert.That(view.ClientSize.Height, Is.EqualTo((PairingApprovalForm.MostRows * single) + (single / 2)),
                    "the view other than ten rows and half the row after them");
                Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table");
            }

            using (var dialog = New(Request(few, 40)))
            {
                Show(dialog);
                var list = Profiles(dialog);
                Assert.That(ProfileRows.Of(list)[ProfileRows.Of(list).Count - 1].Bottom, Is.EqualTo(ProfileRows.View(list).ClientSize.Height),
                    "a table that shows every row ends other than where its rows end");
                Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table");
            }
        }

        [Test]
        public void Page_Down_on_a_focused_box_scrolls_the_table_and_the_view_stays_where_the_key_put_it()
        {
            // As on the pairing dialog: the box keeps the focus out of sight, and neither a layout
            // of the dialog nor a tick of its countdown brings the box back into view.
            var many = Enumerable.Range(1, PairingApprovalForm.MostRows + 2).Select(at => "p" + at).ToArray();
            using (var dialog = New(Request(many, 40)))
            {
                Show(dialog);
                var list = Profiles(dialog);
                var view = ProfileRows.View(list);
                var row = ProfileRows.Of(list)[0];
                var box = ProfileRows.Box(row);
                box.Focus();
                OffScreenForm.Pump();
                Assert.That(box.Focused, Is.True, "the key has to start where an operator would press it");
                var end = view.DisplayRectangle.Height - view.ClientSize.Height;

                Press(box, Keys.PageDown);
                Assert.That(-view.AutoScrollPosition.Y, Is.EqualTo(end), "Page Down scrolled elsewhere than to the end of the rows");
                Assert.That(row.Top + box.Bottom, Is.LessThanOrEqualTo(0), "Page Down left the box in sight");

                PairingApprovalFormTests.PaintLayoutAndTick(dialog, GateTab.RenewalCountdownName);
                Assert.That(-view.AutoScrollPosition.Y, Is.EqualTo(end), "a paint, a layout or the countdown's tick moved the view");
                Assert.That(box.Focused, Is.True, "the focus moved");
            }
        }

        [Test]
        public void Ticks_paint_no_row_and_not_the_view_round_a_focused_box()
        {
            // As on the pairing dialog: the countdown's words changing every second lay nothing
            // out, so that no tick draws the view of the rows again round a box holding the focus.
            // On the screen, since a view off it paints nothing at all.
            var many = Enumerable.Range(1, PairingApprovalForm.MostRows + 2).Select(at => "p" + at).ToArray();
            using (var dialog = New(Request(many, 40)))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(0, 0);
                dialog.ShowInTaskbar = false;
                dialog.Show();
                OffScreenForm.Pump();
                var list = Profiles(dialog);
                var box = ProfileRows.Box(ProfileRows.Of(list)[0]);
                box.Focus();
                OffScreenForm.Pump();
                Assert.That(box.Focused, Is.True, "the ticks have to come with a box holding the focus");
                PairingApprovalFormTests.NextTick(dialog, GateTab.RenewalCountdownName);
                var painted = PairingApprovalFormTests.PaintedWhile(list, () =>
                {
                    PairingApprovalFormTests.NextTick(dialog, GateTab.RenewalCountdownName);
                    PairingApprovalFormTests.NextTick(dialog, GateTab.RenewalCountdownName);
                });
                Assert.That(painted, Is.Empty, "ticks painted the table");
            }
        }

        /// <summary>
        /// The names of the lines of text the dialog shows under its table, in order.
        /// </summary>
        private static IReadOnlyList<string> LinesUnder(Form dialog)
        {
            var table = Profiles(dialog);
            var controls = table.Parent.Controls.Cast<Control>().ToList();
            return controls.Skip(controls.IndexOf(table) + 1).OfType<Label>()
                .Where(line => !string.IsNullOrEmpty(line.Text))
                .Select(line => line.Name)
                .ToList();
        }

        [Test]
        public void The_question_of_a_day_gives_Refuse_no_letter_a_stray_key_could_press()
        {
            using (var form = new RefuseUntilForm(Today.AddDays(1), Today.AddDays(30), Today.AddDays(7), null))
            {
                var refuse = ControlTree.FindByName<Button>(form, GateTab.RefuseUntilAcceptName);

                Assert.That(refuse.Text, Does.Not.Contain("&"));
                Assert.That(form.AcceptButton, Is.SameAs(refuse));
            }
        }

        [Test]
        public void A_name_the_database_no_longer_holds_is_a_row_with_a_red_X_in_place_of_a_box_and_its_name_in_gray()
        {
            // Two columns, the X drawn on the row where a box sits on the rows above it, and the
            // sentence on the X and on the name alike, as the row is read out.
            using (var dialog = New(Renewing(new[] { "reader", "ops" }, "retired")))
            {
                Show(dialog);
                var list = Profiles(dialog);
                var rows = ProfileRows.Of(list);
                var tips = ProfileRows.Tips(list);
                Assert.That(Headings(list), Is.EqualTo(new[] { "Grant", "Profile" }));
                Assert.That(ProfileRows.Texts(list), Is.EqualTo(new[] { new[] { "reader" }, new[] { "ops" }, new[] { "retired" } }));
                Assert.That(rows.Select(row => ProfileRows.Box(row) != null), Is.EqualTo(new[] { true, true, false }), "a box on the list alone");
                Assert.That(ProfileRows.Checks(list), Is.EqualTo(new[] { true, true }));

                var place = ProfileRows.Box(rows[0]).Bounds;
                Assert.That(CrossedRows.Faults(rows[2], place, "on a plain row"), Is.Empty);
                Assert.That(CrossedRows.Red(rows[0]), Is.Empty, "a row with a box drawing red");

                var name = ProfileRows.Cell(rows[2], GateTab.RenewalProfileColumnName);
                Assert.That(name.ForeColor, Is.EqualTo(SystemColors.GrayText), "the name of a profile not granted");
                Assert.That(ProfileRows.Cell(rows[0], GateTab.RenewalProfileColumnName).ForeColor, Is.Not.EqualTo(SystemColors.GrayText),
                    "the name of a profile granted in gray");
                Assert.That(tips.GetToolTip(rows[2]), Is.EqualTo(Tooltips.Wrapped(RenewalApprovalForm.OmitsTip)), "the tooltip of the X");
                Assert.That(tips.GetToolTip(name), Is.EqualTo(Tooltips.Wrapped(RenewalApprovalForm.OmitsTip)), "the tooltip of the name");
                Assert.That(tips.GetToolTip(rows[0]), Is.Empty);
                Assert.That(tips.GetToolTip(ProfileRows.Cell(rows[0], GateTab.RenewalProfileColumnName)), Is.Empty);
                Assert.That(rows[2].AccessibleName, Is.EqualTo("retired. " + RenewalApprovalForm.OmitsTip),
                    "the row with no box is read out by its name and the sentence of the X");
                Assert.That(ProfileRows.Spoken(list), Is.EqualTo(new[] { "reader", "ops" }), "a box is read out by its name");
                Assert.That(ControlTree.Descendants(dialog).OfType<Label>().Select(label => label.Text),
                    Has.None.StartsWith("Not carried over").And.None.EqualTo("omits the profile").And.None.EqualTo("New certificate"));

                // Highlighted, the X stays where it was and as red.
                ((ProfileTable)list).Mark("retired", RenewalApprovalForm.OmitsTip, PairingApprovalForm.ChangedBackground, SystemColors.WindowText);
                OffScreenForm.Pump();
                Assert.That(CrossedRows.Faults(rows[2], place, "on a highlighted row"), Is.Empty);

                ProfileRows.Boxes(list)[0].Checked = false;
                dialog.Approve();
                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops" }), "the profiles checked on the list, and never a profile off it");
            }

            using (var dialog = New(Renewing(new[] { "reader", "ops" }, "retired")))
            {
                dialog.Approve();
                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "reader", "ops" }), "a profile off the list is never granted");
            }
        }

        [Test]
        public void The_X_is_a_mark_that_takes_neither_the_focus_nor_a_press()
        {
            // An X in the grant editor is a button that removes a rule; this one is drawn on the
            // row, so no control stands where it is, and a press there changes nothing.
            using (var dialog = New(Renewing(new[] { "reader" }, "retired")))
            {
                Show(dialog);
                var list = Profiles(dialog);
                var rows = ProfileRows.Of(list);
                var place = ProfileRows.Box(rows[0]).Bounds;
                var middle = new Point(place.Left + (place.Width / 2), place.Top + (place.Height / 2));
                Assert.That(CrossedRows.Faults(rows[1], place, "before the press"), Is.Empty);
                Assert.That(rows[1].GetChildAtPoint(middle), Is.Null, "a control where the X stands");
                Assert.That(ControlTree.Descendants(rows[1]).Where(control => control.CanSelect), Is.Empty,
                    "a control on the row that takes the focus");

                var lifetime = ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName);
                lifetime.Focus();
                OffScreenForm.Pump();
                Assert.That(lifetime.Focused, Is.True, "the press has to start where the focus is elsewhere");
                var at = (IntPtr)((middle.Y << 16) | (middle.X & 0xFFFF));
                TestNativeMethods.PostMessage(rows[1].Handle, 0x0201, (IntPtr)1, at);
                TestNativeMethods.PostMessage(rows[1].Handle, 0x0202, IntPtr.Zero, at);
                OffScreenForm.Pump();

                Assert.That(lifetime.Focused, Is.True, "the press moved the focus");
                Assert.That(ProfileRows.Checks(list), Is.EqualTo(new[] { true }), "the press changed a check");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "the press answered the dialog");
                Assert.That(CrossedRows.Faults(rows[1], place, "after the press"), Is.Empty);
            }
        }

        [Test]
        public void A_renewal_of_profiles_all_on_the_list_draws_two_columns_and_no_X_and_of_profiles_all_off_it_an_X_on_every_row()
        {
            using (var dialog = New(Renewing(new[] { "reader", "ops" })))
            {
                Show(dialog);
                var list = Profiles(dialog);
                Assert.That(Headings(list), Is.EqualTo(new[] { "Grant", "Profile" }));
                Assert.That(ProfileRows.Of(list).SelectMany(CrossedRows.Red), Is.Empty, "a row with a box drawing red");
            }

            Rectangle place;
            using (var dialog = New(Renewing(new[] { "reader" }, "gone-a", "gone-b")))
            {
                Show(dialog);
                place = ProfileRows.Box(ProfileRows.Of(Profiles(dialog))[0]).Bounds;
            }

            using (var dialog = New(Renewing(Array.Empty<string>(), "gone-a", "gone-b")))
            {
                Show(dialog);
                var list = Profiles(dialog);
                Assert.That(Headings(list), Is.EqualTo(new[] { "Grant", "Profile" }));
                Assert.That(ProfileRows.Boxes(list), Is.Empty);
                Assert.That(ProfileRows.Of(list).SelectMany(row => CrossedRows.Faults(row, place, "on " + row.AccessibleName)), Is.Empty);
                dialog.Approve();
                Assert.That(dialog.Answer().Profiles, Is.Empty, "a profile off the list is never granted");
            }
        }

        [Test]
        public void The_X_is_red_in_a_color_of_its_own_legible_on_the_table_and_on_a_highlight_and_the_text_color_under_high_contrast()
        {
            Assert.That(ProfileTable.CrossColor(false), Is.EqualTo(ProfileTable.CrossRed));
            Assert.That(ProfileTable.CrossColor(true), Is.EqualTo(SystemColors.WindowText), "the theme's text color under high contrast");
            Assert.That(SystemInformation.HighContrast, Is.False, "the colors of the normal theme are measured");
            Assert.That(Contrast(ProfileTable.CrossRed, SystemColors.Window), Is.GreaterThanOrEqualTo(4.5), "against the table");
            Assert.That(Contrast(ProfileTable.CrossRed, PairingApprovalForm.ChangedBackground), Is.GreaterThanOrEqualTo(4.5), "against a highlight");
        }

        /// <summary>The contrast ratio of two colors, as WCAG 2 defines it.</summary>
        private static double Contrast(Color one, Color other)
        {
            Func<Color, double> luminance = color =>
            {
                Func<int, double> linear = channel =>
                {
                    var c = channel / 255.0;
                    return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
                };

                return (0.2126 * linear(color.R)) + (0.7152 * linear(color.G)) + (0.0722 * linear(color.B));
            };

            var lighter = Math.Max(luminance(one), luminance(other));
            var darker = Math.Min(luminance(one), luminance(other));
            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>The headings of a table, in order.</summary>
        private static string[] Headings(Control list)
        {
            return ControlTree.FindByName<Control>(list, ProfileTable.HeadingsName).Controls.OfType<Label>().Select(label => label.Text).ToArray();
        }

        /// <summary>A renewal of a record holding these profiles on the list and these off it.</summary>
        private static RenewalRequest Renewing(string[] listed, params string[] dropped)
        {
            return new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(Today.AddDays(40).AddHours(12), TimeSpan.Zero),
                listed, dropped, "127.0.0.1:60001", null);
        }

        [Test]
        public void A_table_of_profiles_the_database_no_longer_names_is_reached_by_Tab_and_scrolled_by_the_keys()
        {
            // No row has a box to take the focus, so the view the rows scroll in takes it, from
            // Tab, and the keys that scroll the table under a box scroll it there.
            var dropped = Enumerable.Range(1, PairingApprovalForm.MostRows + 4).Select(at => "gone-" + at).ToArray();
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(Today.AddDays(40).AddHours(12), TimeSpan.Zero),
                Array.Empty<string>(), dropped, "127.0.0.1:60001", null);

            using (var dialog = New(request))
            {
                Show(dialog);
                var list = Profiles(dialog);
                var view = ProfileRows.View(list);
                var rows = ProfileRows.Of(list);
                var line = ProfileRows.Cell(rows[0], GateTab.RenewalProfileColumnName).Height;
                Assert.That(rows, Has.Count.EqualTo(dropped.Length));
                Assert.That(ProfileRows.Boxes(list), Is.Empty, "a row with a box");
                var approve = ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName);
                approve.Focus();
                OffScreenForm.Pump();
                Assert.That(approve.Focused, Is.True, "the key has to start where an operator would press it");

                Press(approve, Keys.Tab);
                Assert.That(view.Focused, Is.True, "Tab goes elsewhere than the view");

                Press(view, Keys.Down);
                Assert.That(rows[0].Top, Is.EqualTo(-line), "Down scrolls by a line");
                Press(view, Keys.End);
                Assert.That(view.ClientSize.Height - rows[rows.Count - 1].Bottom, Is.InRange(0, rows[0].Height - 1),
                    "End leaves the last row other than whole, or a row or more empty below it");
                Assert.That(rows.Count(row => row.Top == 0), Is.EqualTo(1), "End leaves the view's top other than on a row's top");
                Press(view, Keys.Home);
                Assert.That(rows[0].Top, Is.Zero, "Home");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "a key answered the dialog");
            }
        }

        [Test]
        public void The_lifetime_asked_for_is_preselected_and_is_what_approving_gives()
        {
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero),
                new[] { "reader" }, Array.Empty<string>(), "127.0.0.1:60001", TimeSpan.FromDays(30));

            using (var dialog = New(request))
            {
                var lifetime = ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName);
                Assert.That(lifetime.SelectedItem, Is.EqualTo("30 days (asked)"));

                dialog.Approve();

                Assert.That(dialog.Answer().Lifetime, Is.EqualTo(TimeSpan.FromDays(30)));
            }
        }

        [Test]
        public void The_menu_offers_what_refusing_can_mean_from_the_narrowest_answer_to_the_widest()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                var items = Refuse(dialog).Menu.Items.Cast<ToolStripItem>().ToArray();

                Assert.That(items.Select(item => item.Text), Is.EqualTo(new[]
                {
                    "Refuse once", "Refuse in this session", "Refuse until a date...", "Refuse forever"
                }));
                Assert.That(items.All(item => item.Enabled), Is.True);
                Assert.That(Refuse(dialog).AccessibleDescription, Is.EqualTo(GateTab.RefuseButtonDescription));
            }
        }

        [Test]
        public void Pressing_Refuse_refuses_once_and_keeps_nothing()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                dialog.Refuse();

                var answer = dialog.Answer();
                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Refused));
                Assert.That(answer.Standing, Is.Null);
            }
        }

        [TestCase(0, null)]
        [TestCase(1, "this session")]
        [TestCase(3, "forever")]
        public void A_choice_from_the_menu_is_the_answer_given(int downs, string standing)
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Show(dialog);
                Refuse(dialog).Focus();
                OffScreenForm.Pump();

                Keystrokes.AltDown(Refuse(dialog));
                Assert.That(Refuse(dialog).MenuShown, Is.True, "Alt and Down did not open the menu");

                for (var i = 0; i < downs; i++)
                {
                    Press(Refuse(dialog), Keys.Down);
                }

                Press(Refuse(dialog), Keys.Enter);

                var answer = dialog.Answer();
                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Refused));
                Assert.That(Described(answer.Standing), Is.EqualTo(standing),
                    "Enter chose from the menu, and pressed nothing behind it");
            }
        }

        [Test]
        public void Choosing_until_a_date_from_the_menu_asks_for_a_day_and_refuses_until_it()
        {
            // The one choice the test above leaves out, driven through the menu as the others are,
            // with the question of a day answered by a stand-in.
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                var asked = false;
                dialog.AskForADay = asking =>
                {
                    asked = true;
                    asking.Choose(Today.AddDays(10));
                    return DialogResult.OK;
                };

                Show(dialog);
                Refuse(dialog).Focus();
                OffScreenForm.Pump();

                Keystrokes.AltDown(Refuse(dialog));
                Assert.That(Refuse(dialog).MenuShown, Is.True, "Alt and Down did not open the menu");

                Press(Refuse(dialog), Keys.Down);
                Press(Refuse(dialog), Keys.Down);
                Press(Refuse(dialog), Keys.Enter);

                Assert.That(asked, Is.True, "choosing it asks for a day");
                var answer = dialog.Answer();
                Assert.That(Described(answer.Standing), Is.EqualTo("too early"));
                Assert.That(answer.Standing.NotBefore,
                    Is.EqualTo(RenewalApprovalForm.StartOf(Today.AddDays(10), TimeZoneInfo.Local).ToUniversalTime()));
            }
        }

        [Test]
        public void Tomorrow_is_read_from_the_clock_when_the_day_is_asked_for_not_when_the_dialog_was_built()
        {
            // A dialog raised before midnight and answered after it offered a day that had
            // begun, and stored a refusal that refused nothing.
            var today = Today;
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, 40), Timeout, null, () => today, null))
            {
                today = Today.AddDays(1);

                MonthCalendar calendar = null;
                dialog.AskForADay = asking =>
                {
                    calendar = ControlTree.FindByName<MonthCalendar>(asking, GateTab.RefuseUntilCalendarName);
                    return DialogResult.Cancel;
                };

                dialog.AskToRefuseUntil();

                Assert.That(calendar.MinDate, Is.EqualTo(Today.AddDays(2)), "tomorrow as of now");
                Assert.That(() => dialog.RefuseUntil(Today.AddDays(1)), Throws.InstanceOf<ArgumentOutOfRangeException>(),
                    "a day that has begun is not offered");
            }
        }

        [Test]
        public void A_day_that_began_while_it_was_being_confirmed_is_asked_for_again()
        {
            // Tomorrow, chosen just before midnight and confirmed just after, names a day that has
            // begun. It used to be thrown into the menu's handler, which logged it and left the
            // operator's choice gone without a word; it is asked for again over the days there are.
            var today = Today;
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, 40), Timeout, null, () => today, null))
            {
                var asked = 0;
                dialog.AskForADay = asking =>
                {
                    asked++;
                    if (asked == 1)
                    {
                        asking.Choose(Today.AddDays(1));
                        today = Today.AddDays(1);
                    }
                    else
                    {
                        asking.Choose(Today.AddDays(5));
                    }

                    return DialogResult.OK;
                };

                Assert.That(() => dialog.AskToRefuseUntil(), Throws.Nothing);

                Assert.That(asked, Is.EqualTo(2));
                var answer = dialog.Answer();
                Assert.That(Described(answer.Standing), Is.EqualTo("too early"));
                Assert.That(answer.Standing.NotBefore,
                    Is.EqualTo(RenewalApprovalForm.StartOf(Today.AddDays(5), TimeZoneInfo.Local).ToUniversalTime()));
            }
        }

        [Test]
        public void Midnight_between_asking_whether_there_is_a_day_and_asking_for_it_still_asks()
        {
            // On the eve of the certificate's last day there is one day to name. Tomorrow was read
            // once to ask whether there was any day at all and again for the first day the question
            // offers, and a midnight between the two handed the question a first day after its
            // last, which it refuses by throwing.
            var asking = false;
            var reads = 0;
            Func<DateTime> clock = () => asking && reads++ > 0 ? Today.AddDays(1) : Today;

            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, 1), Timeout, null, clock, null))
            {
                var asked = 0;
                dialog.AskForADay = question =>
                {
                    asked++;
                    return DialogResult.Cancel;
                };

                asking = true;

                Assert.That(() => dialog.AskToRefuseUntil(), Throws.Nothing);
                Assert.That(asked, Is.EqualTo(1), "the question is put over the day there was when it was asked");
            }
        }

        [Test]
        public void A_certificate_whose_last_day_comes_while_the_dialog_is_up_stops_offering_a_day()
        {
            var today = Today;
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, 1), Timeout, null, () => today, null))
            {
                Assume.That(dialog.CanRefuseUntil, Is.True);

                today = Today.AddDays(1);

                Assert.That(dialog.CanRefuseUntil, Is.False);
            }
        }

        [Test]
        public void A_day_whose_clocks_fall_back_at_midnight_begins_at_its_first_midnight()
        {
            // The Azores fall back from 01:00 to 00:00 on the last Sunday of October, so that
            // day's midnight comes twice, and a refusal until that day begins was stored at the
            // second, an hour into it.
            TimeZoneInfo azores;
            try
            {
                azores = TimeZoneInfo.FindSystemTimeZoneById("Azores Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                Assert.Ignore("this machine has no Azores zone");
                return;
            }

            var day = new DateTime(2026, 10, 25);
            Assume.That(azores.IsAmbiguousTime(day), Is.True, "the zone's rule moved");

            Assert.That(RenewalApprovalForm.StartOf(day, azores), Is.EqualTo(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(RenewalApprovalForm.StartOf(day.AddDays(1), azores),
                Is.EqualTo(new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.Zero)), "an ordinary day begins at its one midnight");
        }

        [Test]
        public void A_click_that_puts_the_open_menu_away_does_not_approve_on_the_way()
        {
            // WinForms closes an open menu on a press outside it and then delivers the press to
            // whatever it landed on, so one click meant to put the Refuse menu away, landing on
            // Approve, approved the renewal and saved the database. The next click is a click.
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                // The focus first, as a pointer on the arrow would leave it. A menu opened while
                // another window holds the activation closes at once, and an earlier fixture's
                // window can be holding it. An assertion rather than an assumption, because an
                // assumption that never holds leaves this measuring nothing without a word.
                Show(dialog);
                Refuse(dialog).Focus();
                OffScreenForm.Pump();
                Refuse(dialog).ShowMenu(false);
                OffScreenForm.Pump();
                Assert.That(Refuse(dialog).MenuShown, Is.True, "the menu did not open, so nothing below is measured");

                var approve = ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName);
                Click(approve);

                Assert.That(Refuse(dialog).MenuShown, Is.False, "the click put the menu away");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "and decided nothing");

                Click(approve);
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Approved), "the next one approves");
            }
        }

        [Test]
        public void Escape_on_the_open_menu_closes_it_and_decides_nothing()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Show(dialog);
                Refuse(dialog).Focus();
                OffScreenForm.Pump();
                Refuse(dialog).ShowMenu(true);
                OffScreenForm.Pump();

                Press(Refuse(dialog), Keys.Escape);

                Assert.That(Refuse(dialog).MenuShown, Is.False);
                Assert.That(dialog.Visible, Is.True, "Escape left the menu and not the dialog");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "nothing is decided yet");
            }
        }

        [Test]
        public void Refusing_until_a_date_asks_for_a_day_and_answers_until_its_start_where_the_operator_is()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                RefuseUntilForm asked = null;
                dialog.AskForADay = asking =>
                {
                    asked = asking;
                    asking.Choose(Today.AddDays(10));
                    return DialogResult.OK;
                };

                dialog.AskToRefuseUntil();

                Assert.That(asked, Is.Not.Null, "nobody was asked for a day");
                var standing = dialog.Answer().Standing;
                Assert.That(standing.IsForever, Is.False);
                Assert.That(standing.IsThisSession, Is.False);
                Assert.That(standing.NotBefore,
                    Is.EqualTo(RenewalApprovalForm.StartOf(Today.AddDays(10), TimeZoneInfo.Local).ToUniversalTime()));
            }
        }

        [Test]
        public void Going_back_from_the_question_of_a_day_decides_nothing_and_leaves_Refuse_refusing_once()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                dialog.AskForADay = asking => DialogResult.Cancel;

                dialog.AskToRefuseUntil();

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "nothing is decided yet");

                dialog.Refuse();
                Assert.That(dialog.Answer().Standing, Is.Null);
            }
        }

        [Test]
        public void The_day_can_be_from_tomorrow_to_the_day_the_certificate_ends_and_starts_a_week_on()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                MonthCalendar calendar = null;
                DateTime offered = default(DateTime);
                dialog.AskForADay = asking =>
                {
                    calendar = ControlTree.FindByName<MonthCalendar>(asking, GateTab.RefuseUntilCalendarName);
                    offered = asking.Day;
                    return DialogResult.Cancel;
                };

                dialog.AskToRefuseUntil();

                Assert.That(calendar, Is.Not.Null);
                Assert.That(calendar.MinDate, Is.EqualTo(Today.AddDays(1)));
                Assert.That(calendar.MaxDate, Is.EqualTo(new DateTime(2026, 11, 12)));
                Assert.That(offered, Is.EqualTo(Today.AddDays(7)));
                Assert.That(TimeZoneInfo.ConvertTime(Request(new[] { "reader" }, 40).Expires, TimeZoneInfo.Local).Date,
                    Is.EqualTo(new DateTime(2026, 11, 12)), "the last day is the day it ends where the operator is");
            }
        }

        [Test]
        public void A_certificate_ending_within_the_week_starts_the_question_on_its_last_day()
        {
            using (var dialog = New(Request(new[] { "reader" }, 3)))
            {
                var offered = default(DateTime);
                dialog.AskForADay = asking =>
                {
                    offered = asking.Day;
                    return DialogResult.Cancel;
                };

                dialog.AskToRefuseUntil();

                Assert.That(offered, Is.EqualTo(Today.AddDays(3)));
            }
        }

        [Test]
        public void A_certificate_ending_tomorrow_leaves_exactly_that_day_to_name()
        {
            using (var dialog = New(Request(new[] { "reader" }, 1)))
            {
                MonthCalendar calendar = null;
                dialog.AskForADay = asking =>
                {
                    calendar = ControlTree.FindByName<MonthCalendar>(asking, GateTab.RefuseUntilCalendarName);
                    return DialogResult.Cancel;
                };

                Assert.That(dialog.CanRefuseUntil, Is.True);
                Assert.That(Item(dialog, GateTab.RefuseUntilName).Enabled, Is.True);

                dialog.AskToRefuseUntil();
                Assert.That(calendar.MinDate, Is.EqualTo(Today.AddDays(1)));
                Assert.That(calendar.MaxDate, Is.EqualTo(Today.AddDays(1)));
            }
        }

        [TestCase(0, "today")]
        [TestCase(-1, "a day that has passed")]
        [TestCase(41, "a day after the certificate ends")]
        public void A_day_outside_the_range_is_refused_while_days_inside_it_can_be_named(int fromToday, string which)
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Assume.That(dialog.CanRefuseUntil, Is.True);

                Assert.That(() => dialog.RefuseUntil(Today.AddDays(fromToday)), Throws.InstanceOf<ArgumentOutOfRangeException>(), which);
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut), "and nothing is decided");

                dialog.RefuseUntil(Today.AddDays(40));
                Assert.That(dialog.Answer().Standing, Is.Not.Null, "the last day is inside");
            }
        }

        [Test]
        public void Far_east_of_greenwich_a_certificate_with_a_local_day_left_still_offers_it()
        {
            // 22:00 UTC on the third is eleven in the morning of the fourth at UTC+13. Measured
            // against the date printed in UTC, the choice was grayed out on the third as ending
            // today, while a refusal until the fourth still began before the certificate ended.
            var ends = new DateTimeOffset(Today.AddDays(3).AddHours(22), TimeSpan.Zero);

            using (LocalTimeZone.At(13))
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, ends), Timeout, null, () => Today.AddDays(3), null))
            {
                Assert.That(dialog.CanRefuseUntil, Is.True);

                dialog.RefuseUntil(Today.AddDays(4));

                Assert.That(dialog.Answer().Standing.NotBefore, Is.LessThan(ends));
            }
        }

        [Test]
        public void Far_west_of_greenwich_no_day_after_the_certificate_s_local_end_is_offered()
        {
            // 03:00 UTC on the fourth is five in the afternoon of the third at UTC-10. Measured
            // against the date printed in UTC, the fourth was offered, and a refusal until it began
            // seven hours after the certificate had stopped.
            var ends = new DateTimeOffset(Today.AddDays(4).AddHours(3), TimeSpan.Zero);

            using (LocalTimeZone.At(-10))
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, ends), Timeout, null, () => Today, null))
            {
                MonthCalendar calendar = null;
                dialog.AskForADay = asking =>
                {
                    calendar = ControlTree.FindByName<MonthCalendar>(asking, GateTab.RefuseUntilCalendarName);
                    return DialogResult.Cancel;
                };

                dialog.AskToRefuseUntil();

                Assert.That(calendar.MaxDate, Is.EqualTo(Today.AddDays(3)));
                Assert.That(() => dialog.RefuseUntil(Today.AddDays(4)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            }
        }

        [Test]
        public void A_certificate_ending_before_tomorrow_leaves_no_day_to_name()
        {
            using (var dialog = New(Request(new[] { "reader" }, 0)))
            {
                var asked = false;
                dialog.AskForADay = asking =>
                {
                    asked = true;
                    return DialogResult.OK;
                };

                Assert.That(dialog.CanRefuseUntil, Is.False);
                Assert.That(Item(dialog, GateTab.RefuseUntilName).Enabled, Is.False);
                Assert.That(Item(dialog, GateTab.RefuseUntilName).Text, Is.EqualTo(GateTab.RefuseUntilEndingText),
                    "and it says why");

                dialog.AskToRefuseUntil();
                Assert.That(asked, Is.False);
                Assert.That(() => dialog.RefuseUntil(Today.AddDays(1)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            }
        }

        [Test]
        public void A_day_answered_after_the_dialog_ended_is_ignored_and_the_question_is_closed_with_it()
        {
            // The window closed from the taskbar while the question was open: that refused once,
            // and the question has to go with the dialog it belongs to.
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                // A window, so that closing it is a close rather than a disposal nobody hears.
                dialog.CreateControl();
                Assume.That(dialog.Handle, Is.Not.EqualTo(IntPtr.Zero));

                var closedWithIt = false;
                dialog.AskForADay = asking =>
                {
                    dialog.Close();
                    closedWithIt = asking.DialogResult == DialogResult.Cancel;
                    return DialogResult.OK;
                };

                dialog.AskToRefuseUntil();

                Assert.That(closedWithIt, Is.True, "the question outlived the dialog");
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
                Assert.That(dialog.Answer().Standing, Is.Null, "a day given after the dialog had ended");
            }
        }

        [Test]
        public void The_countdown_running_out_while_a_day_is_asked_for_is_a_timeout()
        {
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, 40), TimeSpan.Zero, null, Today, null))
            {
                dialog.CreateControl();
                Assume.That(dialog.Handle, Is.Not.EqualTo(IntPtr.Zero));

                dialog.AskForADay = asking =>
                {
                    var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                    while (asking.DialogResult != DialogResult.Cancel && DateTime.UtcNow < until)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    return DialogResult.OK;
                };

                dialog.AskToRefuseUntil();

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut));
            }
        }

        [Test]
        public void Enter_refuses_once()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Show(dialog);
                var lifetime = ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName);
                lifetime.Focus();
                OffScreenForm.Pump();

                Press(lifetime, Keys.Enter);

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
                Assert.That(dialog.Answer().Standing, Is.Null);
            }
        }

        [Test]
        public void Escape_refuses_once()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Show(dialog);
                var lifetime = ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName);
                lifetime.Focus();
                OffScreenForm.Pump();

                Press(lifetime, Keys.Escape);

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
                Assert.That(dialog.Answer().Standing, Is.Null);
            }
        }

        [Test]
        public void Closing_it_refuses_once()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                dialog.CreateControl();

                TestNativeMethods.SendMessage(dialog.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero);

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
                Assert.That(dialog.Answer().Standing, Is.Null);
            }
        }

        [Test]
        public void A_dialog_nobody_touches_has_timed_out()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut));
            }
        }

        [Test]
        public void A_dialog_the_countdown_closes_has_timed_out_rather_than_refused()
        {
            using (var dialog = new RenewalApprovalForm(Request(new[] { "reader" }, 40), TimeSpan.Zero, null, Today, null))
            {
                var closed = false;
                dialog.FormClosed += delegate { closed = true; };
                dialog.CreateControl();
                var handle = dialog.Handle;
                Assume.That(handle, Is.Not.EqualTo(IntPtr.Zero));

                var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (!closed && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }

                Assert.That(closed, Is.True);
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut));
            }
        }

        [TestCase(1.0f, 3)]
        [TestCase(1.5f, 3)]
        [TestCase(2.0f, 14)]
        public void Everything_on_it_can_be_seen_at_any_scaling(float scaling, int held)
        {
            var names = Enumerable.Range(1, held).Select(at => "profile-" + at).ToArray();

            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * scaling))
            using (var dialog = new RenewalApprovalForm(Renewing(names, "retired"), Timeout, larger, Today, null))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                    "the dialog is wider than the smallest screen at " + scaling);
                Assert.That(dialog.PreferredSize.Height, Is.LessThanOrEqualTo(SmallestWorkingHeight * scaling),
                    "the dialog is taller than the smallest screen at " + scaling);

                var list = Profiles(dialog);
                var single = ProfileRows.Of(list)[0].Height;
                Assert.That(ProfileRows.View(list).ClientSize.Height,
                    Is.EqualTo(held + 1 > PairingApprovalForm.MostRows ? (PairingApprovalForm.MostRows * single) + (single / 2) : (held + 1) * single),
                    "the view other than its rows, or ten and half the row after them, at " + scaling);

                foreach (var name in new[]
                         {
                             GateTab.RenewalProfilesListName, GateTab.ApprovalLifetimeName, GateTab.RenewalCountdownName,
                             GateTab.ApproveButtonName, GateTab.RefuseButtonName
                         })
                {
                    var control = ControlTree.FindByName<Control>(dialog, name);
                    Assert.That(control, Is.Not.Null, name + " is gone at " + scaling);

                    var where = InDialog(control, dialog);
                    Assert.That(where.Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                        name + " is off the bottom of the dialog at " + scaling);
                    Assert.That(where.Right, Is.LessThanOrEqualTo(Asked(dialog).Width),
                        name + " is off the side of the dialog at " + scaling);
                }

                var refuse = Refuse(dialog);
                Assert.That(
                    refuse.Width,
                    Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(GateTab.RefuseButtonText, larger).Width + refuse.ArrowBounds.Width),
                    "the caption and the arrow do not both fit at " + scaling);
            }
        }

        [TestCase(1.0f)]
        [TestCase(2.0f)]
        public void A_line_the_dialog_does_not_show_takes_no_room(float scaling)
        {
            // The notice of a renewal reaching no profile stands between the sentence of what
            // Approve costs and the line naming the table only where shown, otherwise the ordinary
            // gap, with no band kept for a line not shown; and under a table that scrolls and a
            // table that does not alike, the lifetime stands the ordinary gap below the table.
            var many = Enumerable.Range(1, PairingApprovalForm.MostRows + 4).Select(at => "profile-" + at).ToArray();
            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * scaling))
            {
                foreach (var held in new[] { new[] { "reader" }, Array.Empty<string>(), many })
                {
                    using (var dialog = new RenewalApprovalForm(Renewing(held, "retired"), Timeout, larger, Today, null))
                    {
                        Show(dialog);
                        var consequence = InDialog(ControlTree.FindByName<Label>(dialog, "KpgRenewalConsequence"), dialog);
                        var naming = ControlTree.FindByName<Label>(dialog, "KpgRenewalProfilesLine");
                        var notice = ControlTree.FindByName<Label>(dialog, RenewalApprovalForm.NoticeName);
                        var gap = naming.Margin.Bottom;
                        Assert.That(InDialog(naming, dialog).Top - consequence.Bottom,
                            Is.EqualTo(held.Length == 0 ? gap + notice.Height + gap : gap), "the room above the line naming the table at " + scaling);

                        var list = InDialog(Profiles(dialog), dialog);
                        var row = ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName).Parent;
                        Assert.That(InDialog(row, dialog).Top - list.Bottom, Is.EqualTo(row.Margin.Top),
                            "the room between the table and the lifetime holding " + held.Length + " at " + scaling);
                        Assert.That(LinesUnder(dialog), Is.Empty, "a line under the table holding " + held.Length + " at " + scaling);
                    }
                }
            }
        }

        [TestCase(1.0f)]
        [TestCase(1.5f)]
        [TestCase(2.0f)]
        public void The_X_stands_where_a_box_sits_at_any_scaling(float scaling)
        {
            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * scaling))
            using (var dialog = new RenewalApprovalForm(Renewing(new[] { "reader" }, "retired"), Timeout, larger, Today, null))
            {
                Show(dialog);
                var rows = ProfileRows.Of(Profiles(dialog));
                Assert.That(rows[1].Height, Is.EqualTo(rows[0].Height), "a row with the X as tall as a row with a box at " + scaling);
                Assert.That(CrossedRows.Faults(rows[1], ProfileRows.Box(rows[0]).Bounds, "at " + scaling), Is.Empty);
            }
        }

        [TestCase(1.0f)]
        [TestCase(2.0f)]
        public void A_profile_as_long_as_a_name_may_be_is_cut_at_its_column_and_shown_whole_in_its_tooltip(float scaling)
        {
            // What is kept or dropped here is a name, whole: a name as long as a profile name may
            // be is cut at the edge of its column, as on the pairing dialog, and the pointer on it
            // shows it whole, so that two names alike but for their ends are told apart.
            var longest = new string('W', 64);
            var held = new[] { "reader", longest };

            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * scaling))
            using (var dialog = new RenewalApprovalForm(Request(held, 40), Timeout, larger, Today, null))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                var list = Profiles(dialog);
                var name = ProfileRows.Cell(ProfileRows.Of(list)[1], GateTab.RenewalProfileColumnName);
                Assert.That(name.Text, Is.EqualTo(longest));
                Assert.That(name.Width, Is.LessThan(TextRenderer.MeasureText(longest, list.Font).Width), "a name this long is cut");
                Assert.That(ProfileRows.Tips(list).GetToolTip(name), Is.EqualTo(Tooltips.Wrapped(longest)), "and shown whole");

                var approve = ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName);
                Assert.That(InDialog(approve, dialog).Right, Is.EqualTo(InDialog(list, dialog).Right),
                    "the buttons do not end where the list does at " + scaling);
                Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                    "the dialog is wider than the smallest screen at " + scaling);
            }
        }

        [TestCase(1.0f)]
        [TestCase(2.0f)]
        public void The_question_of_a_day_can_be_seen_whole_at_any_scaling(float scaling)
        {
            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * scaling))
            using (var asking = new RefuseUntilForm(Today.AddDays(1), Today.AddDays(40), Today.AddDays(7), larger))
            {
                asking.CreateControl();
                asking.PerformLayout();

                Assert.That(asking.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                    "the question is wider than the smallest screen at " + scaling);

                foreach (var name in new[]
                         {
                             GateTab.RefuseUntilCalendarName, GateTab.RefuseUntilAcceptName, GateTab.RefuseUntilCancelName
                         })
                {
                    var control = ControlTree.FindByName<Control>(asking, name);
                    Assert.That(control, Is.Not.Null, name + " is gone at " + scaling);

                    var where = InDialog(control, asking);
                    Assert.That(where.Bottom, Is.LessThanOrEqualTo(Asked(asking).Height),
                        name + " is off the bottom of the question at " + scaling);
                    Assert.That(where.Right, Is.LessThanOrEqualTo(Asked(asking).Width),
                        name + " is off the side of the question at " + scaling);
                }

                Assert.That(asking.AcceptButton, Is.SameAs(ControlTree.FindByName<Button>(asking, GateTab.RefuseUntilAcceptName)));
                Assert.That(asking.CancelButton, Is.SameAs(ControlTree.FindByName<Button>(asking, GateTab.RefuseUntilCancelName)));
            }
        }

        [Test]
        public void A_plugin_with_no_screen_renews_nothing_and_says_why()
        {
            var answer = NoRenewalApproval.Instance.Ask(Request(new[] { "reader" }, 40), Timeout, Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(answer.Reason, Is.Not.Empty);
        }

        /// <summary>
        /// Where a control sits in the dialog's client area, added up through its parents rather
        /// than read off the screen, which a dialog nobody has shown is not on.
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
        /// The client area a dialog asks for, which is the one it has until the screen it is on
        /// is too small for it: its preferred size is not held to the screen, its size is.
        /// </summary>
        private static Size Asked(Form dialog)
        {
            return dialog.PreferredSize - (dialog.Size - dialog.ClientSize);
        }

        /// <summary>What a standing answer is, in the words the gate answers it with, or null for none.</summary>
        private static string Described(RenewalRefusal standing)
        {
            if (standing == null)
            {
                return null;
            }

            if (standing.IsThisSession)
            {
                return "this session";
            }

            return standing.IsForever ? "forever" : "too early";
        }

        private static RenewalApprovalForm New(RenewalRequest request)
        {
            return new RenewalApprovalForm(request, Timeout, null, Today, null);
        }

        private static RenewalRequest Request(string[] held, int daysLeft)
        {
            return Request(held, new DateTimeOffset(Today.AddDays(daysLeft).AddHours(12), TimeSpan.Zero));
        }

        private static RenewalRequest Request(string[] held, DateTimeOffset expires)
        {
            return new RenewalRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                expires,
                held,
                Array.Empty<string>(),
                "127.0.0.1:60001",
                null);
        }

        private static Control Profiles(Control form)
        {
            return ProfileRows.Table(form, GateTab.RenewalProfilesListName);
        }

        private static SplitButton Refuse(Control form)
        {
            var refuse = ControlTree.FindByName<SplitButton>(form, GateTab.RefuseButtonName);
            Assert.That(refuse, Is.Not.Null, "Refuse is not a split button");
            return refuse;
        }

        private static ToolStripItem Item(Control form, string name)
        {
            var item = Refuse(form).Menu.Items[name];
            Assert.That(item, Is.Not.Null, "no choice called " + name);
            return item;
        }

        private static void Show(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            OffScreenForm.Pump();
        }

        /// <summary>A press and a release in the middle of a control, through the loop.</summary>
        private static void Click(Control control)
        {
            var where = (IntPtr)(((control.Height / 2) << 16) | ((control.Width / 2) & 0xFFFF));
            TestNativeMethods.PostMessage(control.Handle, 0x0201, (IntPtr)1, where);
            OffScreenForm.Pump();
            TestNativeMethods.PostMessage(control.Handle, 0x0202, IntPtr.Zero, where);
            OffScreenForm.Pump();
        }

        private static void Press(Control control, Keys key)
        {
            TestNativeMethods.PostMessage(control.Handle, 0x0100, (IntPtr)(int)key, IntPtr.Zero);
            TestNativeMethods.PostMessage(control.Handle, 0x0101, (IntPtr)(int)key, IntPtr.Zero);
            OffScreenForm.Pump();
        }
    }

    /// <summary>
    /// The X a row of a profile not granted draws in place of a box, read off the row drawn.
    /// </summary>
    internal static class CrossedRows
    {
        /// <summary>
        /// Every point of a row's column of boxes drawn in red, in the row's own coordinates: left
        /// of the name, whose smoothed text can draw a red fringe that is no part of an X.
        /// </summary>
        public static IReadOnlyList<Point> Red(Control row)
        {
            var name = ProfileRows.Cell(row, GateTab.RenewalProfileColumnName);
            using (var drawn = new Bitmap(row.Width, row.Height))
            {
                row.DrawToBitmap(drawn, new Rectangle(Point.Empty, row.Size));
                return Enumerable.Range(0, name.Left)
                    .SelectMany(x => Enumerable.Range(0, row.Height).Select(y => new Point(x, y)))
                    .Where(point => IsRed(drawn.GetPixel(point.X, point.Y)))
                    .ToList();
            }
        }

        /// <summary>
        /// What is wrong with the X a row draws against the place a box sits in a row of the same
        /// table: none where red stands in that place alone in the column of boxes, at its middle,
        /// and toward each of its four corners, as the arms of an X do.
        /// </summary>
        public static string[] Faults(Control row, Rectangle place, string where)
        {
            var red = Red(row);
            var faults = new List<string>();
            var middle = new Point(place.Left + (place.Width / 2), place.Top + (place.Height / 2));
            if (!red.Any(point => Math.Abs(point.X - middle.X) <= 1 && Math.Abs(point.Y - middle.Y) <= 1))
            {
                faults.Add("no red at the middle " + middle + " of the place of a box " + place + " " + where);
            }

            var outside = red.Where(point => !place.Contains(point)).ToList();
            if (outside.Count > 0)
            {
                faults.Add(outside.Count + " points of red outside the place of a box " + place + " " + where + ", " + string.Join(" ", outside.Take(4)));
            }

            var arms = new[]
            {
                red.Any(point => point.X < middle.X && point.Y < middle.Y),
                red.Any(point => point.X > middle.X && point.Y < middle.Y),
                red.Any(point => point.X < middle.X && point.Y > middle.Y),
                red.Any(point => point.X > middle.X && point.Y > middle.Y)
            };
            if (arms.Contains(false))
            {
                faults.Add("an arm of the X missing " + where + ": " + string.Join(", ", arms));
            }

            return faults.ToArray();
        }

        /// <summary>Whether a pixel is red, as the X draws it: the core of a line rather than its smoothed edge.</summary>
        public static bool IsRed(Color color)
        {
            return color.R >= 150 && color.G <= 100 && color.B <= 100;
        }
    }
}
