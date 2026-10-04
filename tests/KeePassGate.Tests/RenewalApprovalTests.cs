using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Pairing;
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

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        private static readonly DateTime Today = new DateTime(2026, 10, 3);

        [Test]
        public void It_says_who_is_asking_and_when_what_it_holds_now_ends()
        {
            var facts = RenewalApprovalForm.Facts(Request(new[] { "reader" }, 40));
            var said = facts.ToDictionary(fact => fact.Name, fact => fact.Value);

            Assert.That(said["Database"], Is.EqualTo("Invented Vault"));
            Assert.That(said["Client"], Is.EqualTo("agent-fictitious"));
            Assert.That(said["Its certificate expires"], Is.EqualTo("2026-11-12 12:00:00"));
            Assert.That(said[GateTab.PeerFactName], Is.EqualTo("127.0.0.1:60001"));
        }

        [Test]
        public void The_countdown_says_that_nothing_gets_issued()
        {
            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                var countdown = ControlTree.FindByName<Label>(dialog, GateTab.RenewalCountdownName);

                Assert.That(countdown.Text, Is.EqualTo(
                    "This renewal request times out in 120 seconds, and nothing gets issued."));
            }
        }

        [Test]
        public void The_profiles_held_arrive_checked_and_leave_as_what_is_still_checked()
        {
            using (var dialog = New(Request(new[] { "reader", "ops" }, 40)))
            {
                var list = Profiles(dialog);
                Assert.That(list.CheckedItems.Count, Is.EqualTo(2), "agreeing is still a decision");

                list.SetItemChecked(1, false);
                dialog.Approve();

                var answer = dialog.Answer();
                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
                Assert.That(answer.Profiles, Is.EqualTo(new[] { "reader" }));
                Assert.That(answer.Standing, Is.Null);
            }
        }

        [Test]
        public void A_list_longer_than_it_shows_says_so_in_the_pairing_dialog_s_words()
        {
            var many = Enumerable.Range(1, PairingApprovalForm.MostRows + 2).Select(at => "p" + at).ToArray();
            var few = many.Take(PairingApprovalForm.MostRows).ToArray();

            Assert.That(RenewalApprovalForm.Scrolling(Request(many, 40)),
                Is.EqualTo(PairingApprovalForm.Scrolling(many.Length)).And.Not.Null);
            Assert.That(RenewalApprovalForm.Scrolling(Request(few, 40)), Is.Null);
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
        public void A_name_the_database_no_longer_holds_is_said_and_not_offered()
        {
            var request = new RenewalRequest(
                "aaaabbbbccccdddd", "Invented Vault", "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero),
                new[] { "reader" }, new[] { "retired" }, "127.0.0.1:60001", null);

            using (var dialog = New(request))
            {
                Assert.That(Profiles(dialog).Items.Cast<string>(), Is.EqualTo(new[] { "reader" }));
                Assert.That(ControlTree.FindByName<Label>(dialog, GateTab.RenewalDroppedName).Text,
                    Is.EqualTo("Not carried over, because this database no longer names it: retired."));
            }

            using (var dialog = New(Request(new[] { "reader" }, 40)))
            {
                Assert.That(ControlTree.FindByName<Label>(dialog, GateTab.RenewalDroppedName), Is.Null,
                    "nothing to say when everything is carried");
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

                Assert.That(Refuse(dialog).Press(Keys.Alt | Keys.Down), Is.True);
                OffScreenForm.Pump();

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

                Assert.That(Refuse(dialog).Press(Keys.Alt | Keys.Down), Is.True);
                OffScreenForm.Pump();

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
                Assert.Inconclusive("this machine has no Azores zone");
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
                Assert.That(Moments.OnScreen(Request(new[] { "reader" }, 40).Expires), Does.StartWith("2026-11-12"),
                    "the last day is the one the dialog prints");
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

                SendMessage(dialog.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero);

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
            using (var dialog = new RenewalApprovalForm(Request(names, 40), Timeout, larger, Today, null))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                Assert.That(dialog.PreferredSize.Width, Is.LessThanOrEqualTo(SmallestScreenWidth * scaling),
                    "the dialog is wider than the smallest screen at " + scaling);

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
            var answer = NoRenewalApproval.Instance.Ask(Request(new[] { "reader" }, 40), Timeout);

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
            return new RenewalRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(Today.AddDays(daysLeft).AddHours(12), TimeSpan.Zero),
                held,
                Array.Empty<string>(),
                "127.0.0.1:60001",
                null);
        }

        private static CheckedListBox Profiles(Control form)
        {
            var list = ControlTree.FindByName<CheckedListBox>(form, GateTab.RenewalProfilesListName);
            Assert.That(list, Is.Not.Null);
            return list;
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
            PostMessage(control.Handle, 0x0201, (IntPtr)1, where);
            OffScreenForm.Pump();
            PostMessage(control.Handle, 0x0202, IntPtr.Zero, where);
            OffScreenForm.Pump();
        }

        private static void Press(Control control, Keys key)
        {
            PostMessage(control.Handle, 0x0100, (IntPtr)(int)key, IntPtr.Zero);
            PostMessage(control.Handle, 0x0101, (IntPtr)(int)key, IntPtr.Zero);
            OffScreenForm.Pump();
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr key, IntPtr detail);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);
    }
}
