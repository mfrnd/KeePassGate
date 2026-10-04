using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The pairing and renewal dialogs against keystrokes nobody meant for them: shown without
    /// taking the focus, and refusing every key path but a click or Space on Approve.
    /// </summary>
    /// <remarks>
    /// Every key is posted through a real message loop, because which control the loop decides a
    /// key belongs to is the question, and a key handed straight to a handler skips it. Both
    /// dialogs, case by case, because they share <see cref="ApprovalDialog"/> and the hazard.
    /// </remarks>
    [TestFixture("pairing")]
    [TestFixture("renewal")]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class ApprovalDialogTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmChar = 0x0102;
        private const int WmSysChar = 0x0106;
        private const int ExStyle = -20;
        private const int TopmostStyle = 0x00000008;

        private readonly string _kind;

        public ApprovalDialogTests(string kind)
        {
            _kind = kind;
        }

        [Test]
        public void It_comes_up_on_top_without_taking_the_focus_and_flashes_the_window_that_had_it()
        {
            // The operator may be typing into KeePass when a client raises this, and the dialog
            // that took the focus took the next keystroke too. Shown modally, as the prompt shows
            // it, over a window holding the focus in a text box.
            using (var owner = OffScreenForm.New())
            using (var dialog = New())
            {
                var box = new TextBox { Name = "KpgTestTyping" };
                owner.Controls.Add(box);
                owner.Show();
                OffScreenForm.Pump();
                SetActiveWindow(owner.Handle);
                box.Focus();
                OffScreenForm.Pump();
                Assume.That(GetActiveWindow(), Is.EqualTo(owner.Handle));

                Place(dialog);

                IntPtr active = IntPtr.Zero;
                bool dialogHasFocus = true;
                bool boxHasFocus = false;
                int style = 0;
                var timer = new System.Windows.Forms.Timer { Interval = 200 };
                timer.Tick += delegate
                {
                    timer.Stop();
                    active = GetActiveWindow();
                    dialogHasFocus = dialog.ContainsFocus;
                    boxHasFocus = GetFocus() == box.Handle;
                    style = GetWindowLong(dialog.Handle, ExStyle);
                    dialog.Close();
                };

                timer.Start();
                dialog.ShowDialog();
                timer.Dispose();

                Assert.That(active, Is.EqualTo(owner.Handle), "the window that was active stays active");
                Assert.That(dialogHasFocus, Is.False, "the dialog took no focus");
                Assert.That(boxHasFocus, Is.True, "the text box kept it");
                Assert.That(style & TopmostStyle, Is.EqualTo(TopmostStyle), "and the dialog is on top all the same");
                Assert.That(dialog.TopMost, Is.False, "by its style, since the property focuses on showing");
                Assert.That(dialog.Flashed, Is.EqualTo(owner.Handle), "the taskbar entry flashed is its owner's");
            }
        }

        [Test]
        public void The_focus_starts_somewhere_other_than_Approve()
        {
            using (var dialog = Shown())
            {
                Assert.That(Approve(dialog).Focused, Is.False);
            }
        }

        [Test]
        public void Refuse_comes_before_Approve_in_the_tab_order()
        {
            using (var dialog = New())
            {
                var refuse = ControlTree.FindByName<Button>(dialog, GateTab.RefuseButtonName);
                var approve = Approve(dialog);

                Assert.That(refuse.Parent, Is.SameAs(approve.Parent));
                Assert.That(refuse.TabIndex, Is.LessThan(approve.TabIndex));
            }
        }

        [TestCase(Keys.Enter)]
        [TestCase(Keys.Escape)]
        public void A_key_on_a_focused_Approve_refuses(Keys key)
        {
            // A focused button becomes the form's default button, so Enter on Approve used to
            // approve whatever AcceptButton said.
            using (var dialog = Shown())
            {
                var approve = Focus(Approve(dialog));

                Press(approve, key);

                Assert.That(Decision(dialog), Is.EqualTo(PairingDecision.Refused));
            }
        }

        [TestCase(Keys.Enter)]
        [TestCase(Keys.Escape)]
        public void A_key_on_the_profile_list_refuses(Keys key)
        {
            using (var dialog = Shown())
            {
                var list = Focus(Profiles(dialog));

                Press(list, key);

                Assert.That(Decision(dialog), Is.EqualTo(PairingDecision.Refused));
            }
        }

        [TestCase(WmChar, "a bare letter")]
        [TestCase(WmSysChar, "Alt and the letter")]
        public void The_letter_of_Approve_approves_nothing_on_either_button(int message, string how)
        {
            // Approve carried a mnemonic, and a button that has the focus hands a bare letter to the
            // dialog's mnemonics: an 'a' typed while either button had the focus approved.
            foreach (var name in new[] { GateTab.ApproveButtonName, GateTab.RefuseButtonName })
            {
                using (var dialog = Shown())
                {
                    var button = Focus(ControlTree.FindByName<Button>(dialog, name));

                    PostMessage(button.Handle, message, (IntPtr)'a', IntPtr.Zero);
                    OffScreenForm.Pump();

                    Assert.That(Decision(dialog), Is.Not.EqualTo(PairingDecision.Approved),
                        how + " on " + name + " approved");
                    Assert.That(dialog.Visible, Is.True, how + " on " + name + " ended the dialog");
                }
            }
        }

        [Test]
        public void Approve_is_never_drawn_as_the_default_button()
        {
            using (var dialog = Shown())
            {
                var approve = Focus(Approve(dialog));

                Assert.That(dialog.AcceptButton, Is.Not.SameAs(approve));
                Assert.That(approve.UseMnemonic, Is.False);
                Assert.That(approve.Text, Does.Not.Contain("&"));
            }
        }

        [Test]
        public void Space_on_a_focused_Approve_is_the_one_key_that_approves()
        {
            // The deliberate act left to a keyboard: Tab to the button, then Space, which no
            // dialog key and no letter can stand in for.
            using (var dialog = Shown())
            {
                var approve = Focus(Approve(dialog));

                Press(approve, Keys.Space);

                Assert.That(Decision(dialog), Is.EqualTo(PairingDecision.Approved));
            }
        }

        [Test]
        public void A_click_on_Approve_approves()
        {
            using (var dialog = Shown())
            {
                Approve(dialog).PerformClick();

                Assert.That(Decision(dialog), Is.EqualTo(PairingDecision.Approved));
            }
        }

        [TestCase(Keys.Down)]
        [TestCase(Keys.Up)]
        [TestCase(Keys.End)]
        [TestCase(Keys.Home)]
        [TestCase(Keys.PageDown)]
        public void A_key_on_the_closed_lifetime_list_does_not_change_it(Keys key)
        {
            using (var dialog = Shown())
            {
                var lifetime = Focus(ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName));
                var before = lifetime.SelectedIndex;

                Press(lifetime, key);

                Assert.That(lifetime.SelectedIndex, Is.EqualTo(before));
            }
        }

        [TestCase('3')]
        [TestCase('1')]
        [TestCase('d')]
        public void A_character_on_the_closed_lifetime_list_does_not_change_it(char typed)
        {
            // '3' on "30 days (asked)" chose "365 days": twelve times what the client asked.
            using (var dialog = Shown())
            {
                var lifetime = Focus(ControlTree.FindByName<ComboBox>(dialog, GateTab.ApprovalLifetimeName));
                var before = lifetime.SelectedIndex;

                PostMessage(lifetime.Handle, WmChar, (IntPtr)typed, IntPtr.Zero);
                OffScreenForm.Pump();

                Assert.That(lifetime.SelectedIndex, Is.EqualTo(before));
            }
        }

        [Test]
        public void A_letter_on_the_profile_list_checks_nothing_and_Space_does()
        {
            // The list has the focus when the dialog is first activated, and a letter selected the
            // profile it begins and toggled it.
            using (var dialog = Shown())
            {
                var list = Focus(Profiles(dialog));
                list.SelectedIndex = 0;
                OffScreenForm.Pump();
                Assume.That(list.GetItemChecked(0), Is.True);

                PostMessage(list.Handle, WmChar, (IntPtr)'o', IntPtr.Zero);
                OffScreenForm.Pump();
                Assert.That(list.GetItemChecked(0), Is.True, "a letter unchecked a profile");

                Press(list, Keys.Space);
                PostMessage(list.Handle, WmChar, (IntPtr)' ', IntPtr.Zero);
                OffScreenForm.Pump();
                Assert.That(list.GetItemChecked(0), Is.False, "Space still toggles");
            }
        }

        [Test]
        public void A_lock_closes_it_as_unanswered()
        {
            using (var dialog = Shown())
            {
                dialog.Withdraw();
                OffScreenForm.Pump();

                Assert.That(Decision(dialog), Is.EqualTo(PairingDecision.Locked));
                Assert.That(dialog.Visible, Is.False);
            }
        }

        [Test]
        public void A_lock_after_an_answer_leaves_the_answer()
        {
            using (var dialog = Shown())
            {
                Approve(dialog).PerformClick();
                dialog.Withdraw();

                Assert.That(Decision(dialog), Is.EqualTo(PairingDecision.Approved));
            }
        }

        private ApprovalDialog New()
        {
            if (_kind == "pairing")
            {
                return new PairingApprovalForm(
                    new PairingRequest(
                        "aaaabbbbccccdddd",
                        "Invented Vault",
                        "abcdef01",
                        "agent-fictitious",
                        new[] { "ops" },
                        new[] { "ops" },
                        "127.0.0.1:60001"),
                    Timeout);
            }

            var today = new DateTime(2026, 10, 3);
            return new RenewalApprovalForm(
                new RenewalRequest(
                    "aaaabbbbccccdddd",
                    "Invented Vault",
                    "agent-fictitious",
                    new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(today.AddDays(40).AddHours(12), TimeSpan.Zero),
                    new[] { "ops" },
                    Array.Empty<string>(),
                    "127.0.0.1:60001",
                    null),
                Timeout,
                null,
                today,
                null);
        }

        private static PairingDecision Decision(ApprovalDialog dialog)
        {
            var pairing = dialog as PairingApprovalForm;
            return pairing != null ? pairing.Answer().Decision : ((RenewalApprovalForm)dialog).Answer().Decision;
        }

        /// <summary>The dialog shown where nobody sees it.</summary>
        private ApprovalDialog Shown()
        {
            var dialog = New();
            Place(dialog);
            dialog.Show();
            OffScreenForm.Pump();
            return dialog;
        }

        private static void Place(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
        }

        private static T Focus<T>(T control)
            where T : Control
        {
            control.Focus();
            OffScreenForm.Pump();
            Assert.That(control.Focused, Is.True, "the key has to start where an operator would press it");
            return control;
        }

        private static Button Approve(Control dialog)
        {
            var approve = ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName);
            Assert.That(approve, Is.InstanceOf<ApproveButton>());
            return approve;
        }

        private static CheckedListBox Profiles(Control dialog)
        {
            var list = ControlTree.FindByName<CheckedListBox>(dialog, GateTab.ApprovalProfilesListName)
                       ?? ControlTree.FindByName<CheckedListBox>(dialog, GateTab.RenewalProfilesListName);
            Assert.That(list, Is.Not.Null);
            return list;
        }

        private static void Press(Control control, Keys key)
        {
            PostMessage(control.Handle, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
            PostMessage(control.Handle, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
            OffScreenForm.Pump();
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr key, IntPtr detail);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);
    }
}
