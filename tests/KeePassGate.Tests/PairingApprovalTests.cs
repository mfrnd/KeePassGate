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

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

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
            // screen. Every one can still be reached, and the line under the list says how many
            // there are (J056).
            var names = Enumerable.Range(1, asked).Select(at => "profile-" + at).ToArray();

            using (var larger = Scaled(Control.DefaultFont, scaling))
            using (var dialog = New(larger, names))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                var list = ControlTree.FindByName<CheckedListBox>(dialog, GateTab.ApprovalProfilesListName);

                Assert.That(list, Is.Not.Null);
                Assert.That(list.Items.Count, Is.EqualTo(asked), "every one is being asked for");
                Assert.That(list.CheckedItems.Count, Is.EqualTo(asked), "and every one arrives checked");
                Assert.That(list.Height, Is.GreaterThanOrEqualTo(list.ItemHeight * Math.Min(asked, 10)),
                    "a list showing fewer rows than it was sized for is a list the operator cannot read");

                var scrolling = ControlTree.FindByName<Label>(dialog, GateTab.ApprovalScrollingName);
                if (asked > 10)
                {
                    Assert.That(scrolling, Is.Not.Null, "a list that scrolls says nothing about what it hides");
                    Assert.That(scrolling.Text, Does.Contain(asked + " profiles"));
                    Assert.That(InDialog(scrolling, dialog).Bottom, Is.LessThanOrEqualTo(Asked(dialog).Height),
                        "the line under the list is off the bottom of the dialog at " + scaling);

                    list.TopIndex = asked - 1;
                    Assert.That(list.TopIndex, Is.GreaterThan(0), "the list does not scroll to the rows it hides");
                }
                else
                {
                    Assert.That(scrolling, Is.Null, "a list that shows everything needs no line about scrolling");
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
                Profiles(dialog).SetItemChecked(1, false);
                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void Approving_with_everything_unchecked_is_an_admission_that_reaches_nothing()
        {
            using (var dialog = New("ops"))
            {
                Profiles(dialog).SetItemChecked(0, false);
                dialog.Approve();

                var answer = dialog.Answer();

                Assert.That(answer.IsApproved, Is.True);
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

        /// <summary>The dialog shown where nobody sees it, with the focus on the lifetime.</summary>
        private static ComboBox Focused(PairingApprovalForm dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Show();
            OffScreenForm.Pump();

            var lifetime = Lifetime(dialog);
            lifetime.Focus();
            OffScreenForm.Pump();

            Assert.That(lifetime.Focused, Is.True, "the key has to start where an operator would press it");
            return lifetime;
        }

        /// <summary>Puts a key on the queue for the list, and lets the loop deliver it.</summary>
        private static void Press(Control control, Keys key)
        {
            User32.PostMessage(control.Handle, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
            User32.PostMessage(control.Handle, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
            OffScreenForm.Pump();
        }

        private const int WmKeyDown = 0x0100;

        private const int WmKeyUp = 0x0101;

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

                User32.SendMessage(window, message, (IntPtr)command, IntPtr.Zero);

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
                var profiles = Profiles(dialog);

                Assert.That(profiles.Items.Count, Is.EqualTo(2));
                Assert.That(profiles.CheckedItems.Count, Is.EqualTo(2));
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
                Assert.That(PairingApprovalForm.Consequence, Does.Contain("revoked later"));

                // No second person, which `CONVENTIONS.md` requires of every string an operator
                // reads. This one said "You can revoke it later".
                foreach (var pronoun in new[] { "You ", "you ", "your ", "Your " })
                {
                    Assert.That(PairingApprovalForm.Consequence, Does.Not.Contain(pronoun), pronoun);
                }
            }
        }

        [Test]
        public void The_list_says_what_it_is_a_list_of()
        {
            // "It is asking for these" made the reader look back up to find out what "these" meant,
            // and what it meant was the whole decision.
            using (var dialog = New("ops"))
            {
                Assert.That(Texts(dialog), Does.Contain(PairingApprovalForm.ProfilesAsked));
                Assert.That(PairingApprovalForm.ProfilesAsked, Does.Contain("profiles"));
                Assert.That(PairingApprovalForm.ProfilesAsked, Does.Contain("Uncheck"));
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
                var shown = Texts(dialog);

                Assert.That(PairingApprovalForm.Listed("invented", true, false), Is.EqualTo("invented (new)"));
                Assert.That(PairingApprovalForm.Listed("ops", false, false), Is.EqualTo("ops"));
                Assert.That(shown, Does.Contain(PairingApprovalForm.NewProfilesAsked));
                Assert.That(PairingApprovalForm.NewProfilesAsked, Does.Contain("approving adds them"));

                // Not "never named": a name removed once, whose holders have all gone, reads the
                // same here, and a rule still naming it grants it to this client at once.
                Assert.That(PairingApprovalForm.NewProfilesAsked, Does.Not.Contain("never named"));
                Assert.That(PairingApprovalForm.NewProfilesAsked, Does.Contain("any rule already naming one grants it"));
            }
        }

        [Test]
        public void A_list_longer_than_it_shows_says_Approve_grants_what_is_out_of_sight_too()
        {
            // J056: ten harmless names first and the wanted one after, marked as new below the
            // fold, and a glance at the ten approved all of them. The line under the list is what
            // says there is more.
            var asked = Enumerable.Range(1, 32).Select(at => "profile-" + at).ToArray();
            var request = Request("agent-fictitious", asked, asked.Take(31).ToArray());

            var said = PairingApprovalForm.Scrolling(request);

            Assert.That(said, Does.Contain("32 profiles").And.Contain("10 at a time"));
            Assert.That(said, Does.Contain("Approve grants every one still checked"));
            Assert.That(PairingApprovalForm.Scrolling(Request("agent-fictitious", asked.Take(10).ToArray())),
                Is.Null, "ten fit, so nothing is out of sight");

            using (var dialog = new PairingApprovalForm(request, Timeout))
            {
                Assert.That(Texts(dialog), Does.Contain(said));

                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(asked),
                    "what the line warns about is exactly what happens");
            }
        }

        [Test]
        public void What_is_approved_is_the_profile_rather_than_what_the_list_called_it()
        {
            // The mark is for the reader. A record naming "invented (new)" would be a record for a
            // profile no certificate can carry and no rule can name.
            Assert.That(PairingApprovalForm.Asked("invented (new)"), Is.EqualTo("invented"));
            Assert.That(PairingApprovalForm.Asked("retired (removed)"), Is.EqualTo("retired"));
            Assert.That(PairingApprovalForm.Asked("ops"), Is.EqualTo("ops"));

            using (var dialog = new PairingApprovalForm(
                Request("agent-fictitious", new[] { "invented" }, Array.Empty<string>()), Timeout))
            {
                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "invented" }));
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
                    AdmissionRecord.For(runner, DateTimeOffset.UtcNow, new[] { "retired" }),
                    AdmissionRecord.For(seven, DateTimeOffset.UtcNow, new[] { "retired", "ops" })
                };

                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "ops", "retired" }, new[] { "ops" }, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "retired", holding } });

                Assert.That(request.IsNew("retired"), Is.False, "it was named once, so approving it creates nothing");

                using (var dialog = new PairingApprovalForm(request, Timeout))
                {
                    var list = ControlTree.FindByName<CheckedListBox>(dialog, GateTab.ApprovalProfilesListName);
                    Assert.That(list.Items.Cast<string>(), Is.EqualTo(new[] { "ops", "retired (removed)" }));

                    var regaining = ControlTree.FindByName<Label>(dialog, GateTab.ApprovalRegainingName);
                    Assert.That(regaining, Is.Not.Null);
                    Assert.That(regaining.Text, Does.StartWith(
                        "2 clients still hold retired from before and will regain it: agent-seven, ci-runner."));
                    Assert.That(regaining.Text, Does.Contain("every rule still naming it applies again"));

                    dialog.Approve();
                    Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops", "retired" }),
                        "what is approved is the name, not what the list called it");
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
                var paired = AdmissionRecord.For(older, DateTimeOffset.UtcNow, new[] { "retired" });
                var first = AdmissionRecord.For(one, DateTimeOffset.UtcNow, new[] { "retired" });
                var second = AdmissionRecord.For(other, DateTimeOffset.UtcNow, new[] { "retired" });
                var holding = new[]
                {
                    paired,
                    AdmissionRecord.For(renewed, DateTimeOffset.UtcNow, new[] { "retired" }, paired.ClientKey),
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

                Assert.That(PairingApprovalForm.Regaining(request), Does.StartWith(
                    "3 clients still hold retired from before and will regain it: "
                    + string.Join(", ", apart) + ", agent-renewing."));
            }
        }

        [Test]
        public void Many_removed_profiles_held_by_many_clients_are_said_in_one_bounded_sentence()
        {
            // One sentence per profile, each naming every holder, had no bound: twelve names held
            // by fifteen clients pushed Refuse, Approve and the countdown off the screen.
            using (var authority = TestAuthority.New())
            {
                var holders = Enumerable.Range(0, 15)
                    .Select(i => authority.IssueClient("agent-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList();
                try
                {
                    var removed = Enumerable.Range(0, 12).Select(i => "gone-" + i).ToArray();
                    var records = holders.Select(c => AdmissionRecord.For(c, DateTimeOffset.UtcNow, removed)).ToList();
                    var returning = removed.ToDictionary(
                        name => name, name => (IReadOnlyList<AdmissionRecord>)records, StringComparer.Ordinal);

                    var request = new PairingRequest(
                        "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                        removed, new[] { "ops" }, "127.0.0.1:60001", returning);

                    var said = PairingApprovalForm.Regaining(request);

                    Assert.That(said, Does.StartWith(
                        "12 of these profiles were removed and are still held from before by 15 clients, who will "
                        + "regain them: agent-00, agent-01, agent-02, and 12 more."));
                    Assert.That(said.Length, Is.LessThan(400), "bounded however many there are");
                }
                finally
                {
                    holders.ForEach(certificate => certificate.Dispose());
                }
            }
        }

        [Test]
        public void At_the_ceiling_the_dialog_promises_to_add_nothing_and_names_nobody_regaining()
        {
            // The writer skips a name the full list cannot take, and the pairing lands without it,
            // so promising to create a "(new)" name or to give a "(removed)" one back to its
            // holders promised what does not happen.
            var known = Enumerable.Range(0, KeePassGate.Acl.ProfileNames.Most).Select(i => "named-" + i).ToArray();

            using (var authority = TestAuthority.New())
            using (var runner = authority.IssueClient("ci-runner"))
            {
                var holding = new[] { AdmissionRecord.For(runner, DateTimeOffset.UtcNow, new[] { "retired" }) };
                var request = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "named-0", "fresh", "retired" }, known, "127.0.0.1:60001",
                    new Dictionary<string, IReadOnlyList<AdmissionRecord>> { { "retired", holding } });

                Assert.That(request.ListIsFull, Is.True);
                Assert.That(PairingApprovalForm.Asking(request), Is.EqualTo(PairingApprovalForm.FullProfilesAsked));
                Assert.That(PairingApprovalForm.FullProfilesAsked, Does.Contain("approving adds none of them"));
                Assert.That(PairingApprovalForm.Regaining(request), Is.Null);

                var listed = new PairingRequest(
                    "aaaabbbbccccdddd", "Invented Vault", "abcdef01", "agent-fictitious",
                    new[] { "named-0" }, known, "127.0.0.1:60001");
                Assert.That(PairingApprovalForm.Asking(listed), Is.EqualTo(PairingApprovalForm.ProfilesAsked),
                    "a full list says nothing about names already on it");
            }
        }

        [Test]
        public void A_dialog_giving_nothing_back_to_anybody_has_no_such_line()
        {
            using (var dialog = New("ops"))
            {
                Assert.That(ControlTree.FindByName<Label>(dialog, GateTab.ApprovalRegainingName), Is.Null);
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
            Assert.That(PairingApprovalForm.Consequence, Does.Contain("before the client is told"));
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
                Assert.That(Texts(dialog), Does.Contain("asking for no profiles"));
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

                Assert.That(shown, Does.Contain("times out"),
                    "a dialog that answers for the operator has to say that it will");
                Assert.That(shown, Does.Contain("nothing gets issued"),
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

        private static CheckedListBox Profiles(Control form)
        {
            var list = ControlTree.FindByName<CheckedListBox>(form, GateTab.ApprovalProfilesListName);
            Assert.That(list, Is.Not.Null);
            return list;
        }

        private static string Texts(Control form)
        {
            var text = new System.Text.StringBuilder();
            foreach (var control in ControlTree.Descendants(form))
            {
                var label = control as Label;
                if (label != null)
                {
                    text.AppendLine(label.Text);
                }
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
        public void A_user_interface_that_never_runs_the_work_is_a_timeout_rather_than_a_wait()
        {
            var prompt = new PairingApprovalPrompt(new FakeUiThread { Runs = false }, _log);

            var answer = prompt.Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.TimedOut));
            Assert.That(_log.Warnings, Is.Not.Empty);
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

            var answer = prompt.Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(decided));
        }

        [Test]
        public void A_dialog_that_never_answered_is_a_timeout_however_late_it_ran()
        {
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true }, _log)
            {
                Dialog = (request, timeout) => null
            };

            Assert.That(prompt.Ask(Request(), Timeout).Decision, Is.EqualTo(PairingDecision.TimedOut));
        }

        [Test]
        public void It_waits_longer_than_the_dialog_does_so_the_dialog_is_what_gives_up()
        {
            var ui = new FakeUiThread { Runs = false };
            new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout);

            Assert.That(ui.LastTimeout, Is.EqualTo(Timeout + PairingApprovalPrompt.Grace));
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
            var first = new Thread(() => prompt.Ask(Request(), Timeout));

            first.IsBackground = true;
            first.Start();

            try
            {
                Assert.That(ui.Reached(Patience), Is.True, "the first pairing has the screen");

                var second = prompt.Ask(Request(), TimeSpan.FromMilliseconds(50));

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
            var first = new Thread(() => prompt.Ask(Request(), Timeout));

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
                    TimeSpan.FromMilliseconds(50));

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
            prompt.Ask(Request(), Timeout);
            prompt.Ask(Request(), Timeout);

            Assert.That(ui.Asked, Is.EqualTo(2));
        }

        [Test]
        public void A_user_interface_that_throws_is_reported_and_approves_nothing()
        {
            var ui = new FakeUiThread { Throws = new InvalidOperationException("no handle") };

            var answer = new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void There_is_nothing_to_ask_about_without_a_request()
        {
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);

            Assert.Throws<ArgumentNullException>(() => prompt.Ask((PairingRequest)null, Timeout));
            Assert.Throws<ArgumentNullException>(() => prompt.Ask((RenewalRequest)null, Timeout));
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

            Assert.That(prompt.Ask(Request(), Timeout).Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(prompt.Ask(Renewal(), Timeout).Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(shown, Is.Zero);
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

                    var timer = new System.Windows.Forms.Timer { Interval = 100 };
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        prompt.Locked();
                    };

                    timer.Start();
                    prompt.Show(dialog);
                    timer.Dispose();
                    return dialog.Answer();
                }
            };

            var answer = prompt.Ask(Renewal(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Locked));
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("closed unanswered"));

            prompt.Locked();
            Assert.That(_log.Warnings.Count(entry => entry.Message.Contains("closed unanswered")), Is.EqualTo(1),
                "a lock with nothing on the screen closes nothing");
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

            var answer = prompt.Ask(Request(), Timeout);

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

            var pairing = prompt.Ask(Request(), Timeout);
            var renewal = prompt.Ask(Renewal(), Timeout);

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

            var answer = prompt.Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(ui.Asked, Is.Zero, "nothing was posted to KeePass's thread at all");
            Assert.That(_log.Warnings.Select(entry => entry.Message), Has.Some.Contains("own save"));
        }

        [Test]
        public void A_plugin_with_no_screen_approves_nothing_and_says_why()
        {
            var answer = NoPairingApproval.Instance.Ask(Request(), Timeout);

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
                null);
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
