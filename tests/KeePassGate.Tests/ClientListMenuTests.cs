using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The clients page's row menu, on a real window with a real message loop: what it holds for a
    /// client with a standing answer and for one without, how the pointer and the keyboard open
    /// it, and that a choice made on it acts on the row it was opened on once it has closed.
    /// </summary>
    /// <remarks>
    /// **Through the loop rather than through the list's methods**, for the reason
    /// `SplitButtonTests` gives: what is pinned is the route a press or a key takes. The keyboard's
    /// request is sent as what it arrives as, `WM_CONTEXTMENU` with no position, which is what
    /// Shift and F10 and the menu key both become. Neither key can be posted instead: a modifier is
    /// read from the keyboard rather than from a message, and a menu key posted to the grid opened
    /// nothing, measured, since the window manager turns only the one that really arrives into a
    /// request. The end to end tier presses Shift and F10 on KeePass's own dialog.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class ClientListMenuTests
    {
        // cspell:ignore CONTEXTMENU

        private const int ContextMenu = 0x007B;
        private const int KeyDown = 0x0100;
        private const int KeyUp = 0x0101;
        private const int RightDown = 0x0204;
        private const int RightUp = 0x0205;

        private readonly List<Exception> _loop = new List<Exception>();
        private readonly List<string> _lifted = new List<string>();
        private readonly List<string> _revoked = new List<string>();

        private AdmissionRecord _refused;
        private AdmissionRecord _asked;

        [OneTimeSetUp]
        public void MakeTheClients()
        {
            _refused = Record("agent-fictitious");
            _asked = Record("agent-invented");
        }

        [SetUp]
        public void WatchTheMessageLoop()
        {
            _loop.Clear();
            _lifted.Clear();
            _revoked.Clear();
            Application.ThreadException += OnThreadException;
        }

        [TearDown]
        public void StopWatching()
        {
            Application.ThreadException -= OnThreadException;
        }

        [Test]
        public void The_keyboard_opens_the_selected_rows_menu_stating_its_answer_with_Allow_renewal_highlighted()
        {
            Using((list, grid) =>
            {
                Select(grid, 0);

                AskFromTheKeyboard(grid);

                var menu = list.Menu;
                Assert.That(menu.Visible, Is.True, "Shift and F10 opened nothing");
                Assert.That(Shown(menu), Is.EqualTo(new[]
                {
                    "Renewal refused forever", "-", ClientList.AllowRenewalText, ClientList.RevokeText
                }));

                var answer = Item(menu, ClientList.AnswerItemName);
                Assert.That(answer.Enabled, Is.False, "the answer is a statement, grayed out");
                Assert.That(Item(menu, ClientList.AllowRenewalItemName).Enabled, Is.True);
                Assert.That(Item(menu, ClientList.AllowRenewalItemName).Selected, Is.True,
                    "Enter would have nothing to choose");
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void A_client_with_no_answer_has_no_answer_line_and_Allow_renewal_grayed_out()
        {
            Using((list, grid) =>
            {
                Select(grid, 1);

                AskFromTheKeyboard(grid);

                var menu = list.Menu;
                Assert.That(menu.Visible, Is.True);
                Assert.That(Shown(menu), Is.EqualTo(new[] { ClientList.AllowRenewalText, ClientList.RevokeText }));
                Assert.That(Item(menu, ClientList.AllowRenewalItemName).Enabled, Is.False);
                Assert.That(Item(menu, ClientList.RevokeItemName).Enabled, Is.True);
                Assert.That(Item(menu, ClientList.RevokeItemName).Selected, Is.True,
                    "the first choice that can be made is the one highlighted");
            });
        }


        [Test]
        public void The_right_button_on_a_row_selects_it_and_opens_its_menu_with_nothing_highlighted()
        {
            Using((list, grid) =>
            {
                Select(grid, 1);

                RightClick(grid, Middle(grid.GetCellDisplayRectangle(0, 0, false)));

                Assert.That(list.Menu.Visible, Is.True, "the right button opened nothing");
                Assert.That(grid.CurrentCell.RowIndex, Is.EqualTo(0), "the row pressed on is the selected one");
                Assert.That(Item(list.Menu, ClientList.AnswerItemName).Text, Is.EqualTo("Renewal refused forever"));
                Assert.That(list.Menu.Items.Cast<ToolStripItem>().Any(item => item.Selected), Is.False,
                    "a menu opened by the pointer has no choice made for it");
            });
        }

        [Test]
        public void The_right_button_under_the_last_row_opens_nothing()
        {
            Using((list, grid) =>
            {
                RightClick(grid, new Point(grid.ClientSize.Width / 2, grid.ClientSize.Height - 4));

                Assert.That(list.Menu.Visible, Is.False, "there is no client there to act on");
            });
        }

        [Test]
        public void Enter_on_Allow_renewal_lifts_the_answer_of_the_row_it_was_opened_on_after_the_menu_closes()
        {
            Using((list, grid) =>
            {
                Select(grid, 0);
                AskFromTheKeyboard(grid);
                Assume.That(Item(list.Menu, ClientList.AllowRenewalItemName).Selected, Is.True);

                Key(grid, Keys.Enter);

                Assert.That(list.Menu.Visible, Is.False);
                Assert.That(_lifted, Is.EqualTo(new[] { _refused.AdmissionKey }));
                Assert.That(_revoked, Is.Empty);
                Assert.That(list.StandingAt(0), Is.Null, "the name stops being red at once");
                Assert.That(grid.Rows[0].Cells[0].Style.ForeColor, Is.EqualTo(Color.Empty));
                Assert.That(_loop, Is.Empty, "a choice acted on inside the menu's own event");
            });
        }

        [Test]
        public void Revoke_on_the_menu_takes_the_row_away_as_the_X_does()
        {
            Using((list, grid) =>
            {
                Select(grid, 0);
                AskFromTheKeyboard(grid);

                Key(grid, Keys.Down);
                Key(grid, Keys.Enter);

                Assert.That(_revoked, Is.EqualTo(new[] { _refused.AdmissionKey }));
                Assert.That(_lifted, Is.Empty);
                Assert.That(list.Clients, Is.EqualTo(new[] { _asked }));
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void Escape_puts_the_menu_away_and_does_nothing()
        {
            Using((list, grid) =>
            {
                Select(grid, 0);
                AskFromTheKeyboard(grid);

                Key(grid, Keys.Escape);

                Assert.That(list.Menu.Visible, Is.False);
                Assert.That(_lifted, Is.Empty);
                Assert.That(_revoked, Is.Empty);
                Assert.That(list.StandingAt(0).IsForever, Is.True);
            });
        }

        private void Using(Action<ClientList, DataGridView> test)
        {
            using (var form = OffScreenForm.New())
            using (var list = new ClientList(
                       key =>
                       {
                           _revoked.Add(key);
                           return null;
                       },
                       key =>
                       {
                           _lifted.Add(key);
                           return null;
                       }))
            {
                form.Controls.Add(list);
                form.Show();

                list.Fill(
                    new AdmissionListing(new[] { _refused, _asked }, true),
                    null,
                    null,
                    new Dictionary<string, RenewalRefusal> { { _refused.AdmissionKey, RenewalRefusal.Forever() } });
                Pump();

                var grid = ControlTree.FindByName<DataGridView>(list, ClientList.ListName);
                Assert.That(grid, Is.Not.Null);

                try
                {
                    test(list, grid);
                }
                finally
                {
                    if (list.Menu.Visible)
                    {
                        Key(grid, Keys.Escape);
                    }
                }
            }
        }

        private static void Select(DataGridView grid, int row)
        {
            grid.CurrentCell = grid.Rows[row].Cells[0];
            Pump();
        }

        /// <summary>What Shift and F10 and the menu key send: a request with no position.</summary>
        private static void AskFromTheKeyboard(DataGridView grid)
        {
            User32.SendMessage(grid.Handle, ContextMenu, grid.Handle, (IntPtr)(-1));
            Pump();
        }

        /// <summary>The right button pressed and released at a point on the grid, through the loop.</summary>
        private static void RightClick(DataGridView grid, Point at)
        {
            var where = (IntPtr)((at.Y << 16) | (at.X & 0xFFFF));
            User32.PostMessage(grid.Handle, RightDown, (IntPtr)2, where);
            Pump();
            User32.PostMessage(grid.Handle, RightUp, IntPtr.Zero, where);
            Pump();
        }

        private static void Key(Control control, Keys key)
        {
            User32.PostMessage(control.Handle, KeyDown, (IntPtr)(int)key, IntPtr.Zero);
            User32.PostMessage(control.Handle, KeyUp, (IntPtr)(int)key, IntPtr.Zero);
            Pump();
        }

        private static Point Middle(Rectangle area)
        {
            return new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        }

        /// <summary>What the menu shows, in order, a separator as a dash.</summary>
        private static string[] Shown(ContextMenuStrip menu)
        {
            return menu.Items.Cast<ToolStripItem>()
                .Where(item => item.Available)
                .Select(item => item is ToolStripSeparator ? "-" : item.Text)
                .ToArray();
        }

        private static ToolStripItem Item(ContextMenuStrip menu, string name)
        {
            var item = menu.Items[name];
            Assert.That(item, Is.Not.Null, "the menu has no " + name);
            return item;
        }

        private static void Pump()
        {
            OffScreenForm.Pump();
        }

        private void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            _loop.Add(e.Exception);
        }

        private static AdmissionRecord Record(string name)
        {
            using (var key = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (var certificate = request.CreateSelfSigned(
                           DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)))
                {
                    return AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                }
            }
        }
    }
}
