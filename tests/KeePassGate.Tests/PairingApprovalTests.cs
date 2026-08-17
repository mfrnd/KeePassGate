using System;
using System.Collections.Generic;
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

                SendMessage(window, message, (IntPtr)command, IntPtr.Zero);

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

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);

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

            Assert.Throws<ArgumentNullException>(() => prompt.Ask(null, Timeout));
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
