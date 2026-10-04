using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The split button, on a real window with a real message loop, as the renewal dialog's Refuse
    /// button uses it.
    /// </summary>
    /// <remarks>
    /// **Through the loop rather than through its methods**, because what each test pins is a route
    /// a press or a key takes: whether a press on the arrow becomes a click, whether a key reaches
    /// the menu or the dialog behind it. A call on the button would pass either way. The window is
    /// the dialog's shape in one respect that matters: the button is both its accept and its cancel
    /// button, so a key the menu let through would press it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class SplitButtonTests
    {
        private const int ButtonDown = 0x0201;
        private const int ButtonUp = 0x0202;
        private const int KeyDown = 0x0100;
        private const int KeyUp = 0x0101;

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
        public void A_press_on_the_caption_is_a_click_and_opens_nothing()
        {
            Using((form, button, clicks, chosen) =>
            {
                Click(button, new Point(Spacing.Gap, button.Height / 2));

                Assert.That(clicks.Count, Is.EqualTo(1));
                Assert.That(button.MenuShown, Is.False);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_press_on_the_arrow_opens_the_menu_and_is_not_a_click()
        {
            Using((form, button, clicks, chosen) =>
            {
                Click(button, Middle(button.ArrowBounds));

                Assert.That(button.MenuShown, Is.True);
                Assert.That(clicks, Is.Empty, "the arrow pressed the button as well");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_press_on_the_arrow_while_the_menu_is_open_closes_it()
        {
            // The press that closes the menu arrives at the button once the menu has gone,
            // measured, so without a mark it opened the menu straight back up and the arrow could
            // never close what it had opened.
            Using((form, button, clicks, chosen) =>
            {
                Click(button, Middle(button.ArrowBounds));
                Assume.That(button.MenuShown, Is.True);

                Click(button, Middle(button.ArrowBounds));

                Assert.That(button.MenuShown, Is.False);
                Assert.That(clicks, Is.Empty);

                Click(button, Middle(button.ArrowBounds));
                Assert.That(button.MenuShown, Is.True, "and the next press opens it again");
            });
        }

        [Test]
        public void Alt_and_Down_open_the_menu_with_its_first_choice_highlighted()
        {
            Using((form, button, clicks, chosen) =>
            {
                Assert.That(button.Press(Keys.Alt | Keys.Down), Is.True, "nothing took the keys");

                Assert.That(button.MenuShown, Is.True);
                Assert.That(button.Menu.Items[0].Selected, Is.True, "Enter would have nothing to choose");
                Assert.That(button.Press(Keys.Down), Is.False, "Down alone is the dialog's");
            });
        }

        [Test]
        public void Enter_on_the_open_menu_chooses_what_is_highlighted_and_presses_nothing_behind_it()
        {
            Using((form, button, clicks, chosen) =>
            {
                button.ShowMenu(true);
                Pump();

                Key(button, Keys.Down);
                Key(button, Keys.Down);
                Key(button, Keys.Enter);

                Assert.That(chosen.Select(item => item.Name), Is.EqualTo(new[] { "third" }));
                Assert.That(clicks, Is.Empty, "the dialog's accept button was pressed under the menu");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void Enter_on_a_menu_with_nothing_highlighted_chooses_nothing()
        {
            Using((form, button, clicks, chosen) =>
            {
                button.ShowMenu(false);
                Pump();

                Key(button, Keys.Enter);

                Assert.That(chosen, Is.Empty);
                Assert.That(clicks, Is.Empty);
                Assert.That(button.MenuShown, Is.True, "and it is still open to choose from");
            });
        }

        [Test]
        public void Escape_on_the_open_menu_closes_it_and_reaches_nothing_behind_it()
        {
            Using((form, button, clicks, chosen) =>
            {
                button.ShowMenu(true);
                Pump();

                Key(button, Keys.Escape);

                Assert.That(button.MenuShown, Is.False);
                Assert.That(chosen, Is.Empty);
                Assert.That(clicks, Is.Empty, "the dialog's cancel button was pressed under the menu");
                Assert.That(form.Visible, Is.True);
            });
        }

        [Test]
        public void A_choice_is_acted_on_once_the_menu_has_gone()
        {
            Using((form, button, clicks, chosen) =>
            {
                var shownWhenChosen = true;
                button.ItemChosen += (sender, e) => shownWhenChosen = button.MenuShown;

                button.ShowMenu(true);
                Pump();
                Key(button, Keys.Enter);

                Assert.That(chosen.Select(item => item.Name), Is.EqualTo(new[] { "first" }));
                Assert.That(shownWhenChosen, Is.False);
            });
        }

        [Test]
        public void A_choice_that_fails_is_written_to_the_log_rather_than_thrown_into_the_loop()
        {
            var log = new RecordingGateLog();

            Using(
                log,
                (form, button, clicks, chosen) =>
                {
                    button.ItemChosen += (sender, e) => { throw new InvalidOperationException("invented"); };

                    button.ShowMenu(true);
                    Pump();
                    Key(button, Keys.Enter);

                    Assert.That(_loop, Is.Empty);
                    Assert.That(log.Errors.Select(entry => entry.Message), Has.Some.Contains("could not be carried out"));
                });
        }

        [Test]
        public void It_is_wide_enough_for_its_caption_and_its_arrow()
        {
            // Sized to what it holds, as on the dialogs, rather than to the width a button starts
            // at, which both of these fit inside.
            using (var button = new SplitButton(null) { Text = "&Refuse", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink })
            using (var plain = new Button { Text = "&Refuse", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink })
            {
                var arrow = SplitButton.ArrowPartWidth(button.Font);

                Assert.That(
                    button.GetPreferredSize(Size.Empty).Width,
                    Is.GreaterThanOrEqualTo(plain.GetPreferredSize(Size.Empty).Width + arrow));
            }
        }

        [Test]
        public void The_arrow_part_grows_with_the_text()
        {
            using (var larger = new Font(Control.DefaultFont.FontFamily, Control.DefaultFont.Size * 2))
            {
                Assert.That(SplitButton.ArrowPartWidth(larger), Is.GreaterThan(SplitButton.ArrowPartWidth(Control.DefaultFont)));
            }
        }

        private void Using(Action<Form, SplitButton, List<EventArgs>, List<ToolStripItem>> test)
        {
            Using(null, test);
        }

        private void Using(RecordingGateLog log, Action<Form, SplitButton, List<EventArgs>, List<ToolStripItem>> test)
        {
            var clicks = new List<EventArgs>();
            var chosen = new List<ToolStripItem>();

            using (var form = OffScreenForm.New())
            {
                var button = new SplitButton(log)
                {
                    Name = "KpgSplitButtonUnderTest",
                    Text = "&Refuse",
                    AutoSize = true,
                    Location = new Point(Spacing.Window, Spacing.Window)
                };

                foreach (var name in new[] { "first", "second", "third", "fourth" })
                {
                    button.Menu.Items.Add(new ToolStripMenuItem(name) { Name = name });
                }

                button.Click += (sender, e) => clicks.Add(e);
                button.ItemChosen += (sender, e) => chosen.Add(e.ClickedItem);

                form.Controls.Add(button);
                form.AcceptButton = button;
                form.CancelButton = button;

                // A press of the button is a click and nothing else here: the form is not closed by
                // it, which would leave the remaining assertions about a window that has gone.
                button.DialogResult = DialogResult.None;

                form.Show();
                Pump();
                button.Focus();
                Pump();

                try
                {
                    test(form, button, clicks, chosen);
                }
                finally
                {
                    // Closed the way an operator closes it, with Escape. A menu closed by code and
                    // then gone with its window left the next window's menu closing the moment it
                    // opened, as an `AppFocusChange`, measured: that is the following test failing
                    // for this one's ending, which is why nothing here closes the menu by code.
                    if (button.MenuShown)
                    {
                        Key(button, Keys.Escape);
                    }
                }
            }
        }

        private static Point Middle(Rectangle area)
        {
            return new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        }

        /// <summary>A press and a release at a point on the control, through the loop.</summary>
        private static void Click(Control control, Point at)
        {
            var where = (IntPtr)((at.Y << 16) | (at.X & 0xFFFF));
            PostMessage(control.Handle, ButtonDown, (IntPtr)1, where);
            Pump();
            PostMessage(control.Handle, ButtonUp, IntPtr.Zero, where);
            Pump();
        }

        /// <summary>A key down and up on the control, through the loop.</summary>
        private static void Key(Control control, Keys key)
        {
            PostMessage(control.Handle, KeyDown, (IntPtr)(int)key, IntPtr.Zero);
            PostMessage(control.Handle, KeyUp, (IntPtr)(int)key, IntPtr.Zero);
            Pump();
        }

        private static void Pump()
        {
            OffScreenForm.Pump();
        }

        private void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            _loop.Add(e.Exception);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr low);
    }
}
