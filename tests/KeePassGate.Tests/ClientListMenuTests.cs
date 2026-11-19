using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
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
        private readonly List<string> _taken = new List<string>();

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
            _taken.Clear();
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
                    FirstLine(list.Rows[0]), "Renewal refused forever", "-", ClientList.RevokeText, ClientList.TakeAwayText, ClientList.AllowRenewalText
                }));
                Assert.That(Item(menu, ClientList.CertificateItemName).Enabled, Is.False, "the certificate is a statement, grayed out");

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
                Assert.That(Shown(menu), Is.EqualTo(new[]
                {
                    FirstLine(list.Rows[1]), "-", ClientList.RevokeText, ClientList.TakeAwayText, ClientList.AllowRenewalText
                }));
                Assert.That(Item(menu, ClientList.AllowRenewalItemName).Enabled, Is.False);
                Assert.That(Item(menu, ClientList.RevokeItemName).Enabled, Is.True);
                Assert.That(Item(menu, ClientList.RevokeItemName).Selected, Is.True,
                    "the first choice that can be made is the one highlighted");
            });
        }

        [Test]
        public void Where_the_store_takes_no_write_the_menu_offers_Allow_renewal_alone()
        {
            Using((list, grid) =>
            {
                list.Writable = false;
                list.Fill(
                    new AdmissionListing(new[] { _refused, _asked }, true),
                    null,
                    null,
                    new Dictionary<string, RenewalRefusal> { { _refused.AdmissionKey, RenewalRefusal.Forever() } });
                Pump();
                Select(grid, 0);

                AskFromTheKeyboard(grid);

                Assert.That(Shown(list.Menu), Is.EqualTo(new[]
                {
                    FirstLine(list.Rows[0]), "Renewal refused forever", "-", ClientList.AllowRenewalText
                }));
                Assert.That(Item(list.Menu, ClientList.AllowRenewalItemName).Enabled, Is.True);
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
        public void Where_the_store_takes_no_write_the_menu_offers_no_Revoke_and_sets_Allow_renewal_apart()
        {
            // `STORE.md` §636: no command on the store where nothing is written into it in the
            // session. Allow renewal stays, set apart from the line that states the certificate.
            Using((list, grid) =>
            {
                list.Writable = false;
                list.Fill(
                    new AdmissionListing(new[] { _refused, _asked }, true),
                    null,
                    null,
                    new Dictionary<string, RenewalRefusal> { { _refused.AdmissionKey, RenewalRefusal.Forever() } });
                Pump();
                Select(grid, 1);

                AskFromTheKeyboard(grid);

                Assert.That(Shown(list.Menu), Is.EqualTo(new[] { FirstLine(list.Rows[1]), "-", ClientList.AllowRenewalText }));
            });
        }

        [Test]
        public void Enter_on_Allow_renewal_lifts_the_answer_of_the_row_it_was_opened_on_after_the_menu_closes()
        {
            Using((list, grid) =>
            {
                Select(grid, 0);
                AskFromTheKeyboard(grid);
                Assert.That(Item(list.Menu, ClientList.AllowRenewalItemName).Selected, Is.True,
                    "the menu did not open on Allow renewal, so nothing below is measured");

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
        public void The_menu_of_a_record_that_does_not_read_offers_what_the_record_names()
        {
            // Revoke where it names a client key and stands alone, Take certificate away where it
            // names a certificate, and no command otherwise; Allow renewal on no such row.
            var named = new UnreadAdmission(new string('e', 32), new string('c', 64), new string('b', 64));
            var nameless = new UnreadAdmission(new string('f', 32), null, null);

            Using((list, grid) =>
            {
                list.Fill(Listings.Of(Array.Empty<AdmissionRecord>(), new[] { named, nameless }, 0, true), null, null, null);
                Pump();

                var at = list.Rows.ToList().FindIndex(row => row.Unread == named);
                Select(grid, at);
                AskFromTheKeyboard(grid);

                Assert.That(Shown(list.Menu).Skip(1), Is.EqualTo(new[] { "-", ClientList.RevokeText, ClientList.TakeAwayText }));
                Key(grid, Keys.Escape);
                Pump();

                Select(grid, list.Rows.ToList().FindIndex(row => row.Unread == nameless));
                AskFromTheKeyboard(grid);

                Assert.That(Shown(list.Menu).Skip(1), Is.Empty, "a record that names neither offers no command");
                Key(grid, Keys.Escape);
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void Revoke_on_the_menu_takes_the_row_away_as_the_X_does()
        {
            Using((list, grid) =>
            {
                Select(grid, 0);
                AskFromTheKeyboard(grid);

                Key(grid, Keys.Up);
                Key(grid, Keys.Up);
                Key(grid, Keys.Enter);

                Assert.That(_revoked, Is.EqualTo(new[] { _refused.AdmissionKey }));
                Assert.That(_lifted, Is.Empty);
                Assert.That(_taken, Is.Empty);
                Assert.That(list.Clients, Is.EqualTo(new[] { _asked }));
                Assert.That(_loop, Is.Empty);
            });
        }

        [Test]
        public void An_earlier_certificate_s_menu_offers_taking_it_away_and_no_revocation()
        {
            Using((list, grid) =>
            {
                list.Fill(Group(), null, null, null);
                Pump();
                Assume.That(list.Rows[1].Own, Is.False, "the second row is an earlier certificate");
                Select(grid, 1);

                AskFromTheKeyboard(grid);

                Assert.That(Shown(list.Menu), Is.EqualTo(new[]
                {
                    FirstLine(list.Rows[1]), "-", ClientList.TakeAwayText, ClientList.AllowRenewalText
                }));
                Assert.That(Item(list.Menu, ClientList.TakeAwayItemName).Selected, Is.True,
                    "the first choice that can be made is highlighted");
            });
        }

        [Test]
        public void Take_certificate_away_on_the_menu_takes_that_row_alone_and_the_next_becomes_the_client_s_own()
        {
            Using((list, grid) =>
            {
                var group = Group();
                list.Fill(group, null, null, null);
                Pump();
                var first = list.Rows[0].Record;
                var earlier = list.Rows[1].Record;
                Select(grid, 0);
                AskFromTheKeyboard(grid);
                Assume.That(Item(list.Menu, ClientList.RevokeItemName).Selected, Is.True);

                Key(grid, Keys.Down);
                Key(grid, Keys.Enter);

                Assert.That(_taken, Is.EqualTo(new[] { first.AdmissionKey }));
                Assert.That(_revoked, Is.Empty, "the client is not revoked");
                Assert.That(list.Clients, Is.EqualTo(new[] { earlier }));
                Assert.That(list.Rows[0].Own, Is.True, "the earlier certificate is the client's own row now");
                Assert.That(grid.Rows[0].Cells[ClientList.RevokeColumnName].Value, Is.EqualTo(ClientList.RevokeMark),
                    "and it carries the client's X");
                Assert.That(_loop, Is.Empty);
            });
        }

        /// <summary>
        /// A client that renewed: the certificate admitted last, and the certificate it was renewed
        /// from, under a client key of its own.
        /// </summary>
        private static AdmissionListing Group()
        {
            var first = Record("agent-renewing");
            using (var certificate = Minted.Named("CN=agent-renewing")
                       .Lasting(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30))
                       .SelfSigned()
                       .Read())
            {
                var renewed = AdmissionRecord.For(
                    certificate, first.Admitted.AddMinutes(1), new[] { "ops" }, InventedIssuer.Id, first.ClientKey, first.AdmissionKey);
                return new AdmissionListing(new[] { renewed, first }, true);
            }
        }

        [Test]
        public void A_choice_that_fails_is_said_under_the_list_rather_than_thrown_into_the_loop()
        {
            // What a choice does runs a message after the menu has closed, posted, where whatever
            // escapes reaches WinForms' own dialog rather than this list. A failure of the kind
            // the user interface throws is an ordinary failure, written under the list instead, and
            // the remark on that guard holds that the promise should not depend on nothing
            // throwing.
            Using(
                (list, grid) =>
                {
                    Select(grid, 0);
                    AskFromTheKeyboard(grid);

                    Key(grid, Keys.Up);
                    Key(grid, Keys.Up);
                    Key(grid, Keys.Enter);

                    Assert.That(_loop, Is.Empty);
                    Assert.That(list.Result, Does.StartWith("That could not be done: "));
                    Assert.That(list.Clients, Is.EqualTo(new[] { _refused, _asked }), "nothing was taken away");
                },
                key => throw new InvalidOperationException("the revocation could not be buffered"));
        }

        [Test]
        public void A_defect_in_a_choice_is_recorded_rather_than_thrown_into_the_loop()
        {
            // The other half of the guard above: what it does not name is a defect, which the
            // posted work's own handler records, `DESIGN.md` §141.
            var incidents = new GateIncidents(new RecordingGateLog());
            WindowHandlers.Incidents = incidents;
            try
            {
                Using(
                    (list, grid) =>
                    {
                        Select(grid, 0);
                        AskFromTheKeyboard(grid);

                        Key(grid, Keys.Up);
                        Key(grid, Keys.Up);
                        Key(grid, Keys.Enter);

                        Assert.That(_loop, Is.Empty);
                        Assert.That(incidents.Recent, Has.Exactly(1).Contains(nameof(NullReferenceException)));
                        Assert.That(list.Clients, Is.EqualTo(new[] { _refused, _asked }), "nothing was taken away");
                    },
                    key => throw new NullReferenceException("a defect in the revocation"));
            }
            finally
            {
                WindowHandlers.Incidents = null;
            }
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
            Using(test, key =>
            {
                _revoked.Add(key);
                return null;
            });
        }

        private void Using(Action<ClientList, DataGridView> test, Func<string, string> revoke)
        {
            using (var form = OffScreenForm.New())
            using (var list = new ClientList(
                       revoke,
                       key =>
                       {
                           _lifted.Add(key);
                           return null;
                       },
                       key =>
                       {
                           _taken.Add(key);
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
            TestNativeMethods.SendMessage(grid.Handle, ContextMenu, grid.Handle, (IntPtr)(-1));
            Pump();
        }

        /// <summary>The right button pressed and released at a point on the grid, through the loop.</summary>
        private static void RightClick(DataGridView grid, Point at)
        {
            var where = (IntPtr)((at.Y << 16) | (at.X & 0xFFFF));
            TestNativeMethods.PostMessage(grid.Handle, RightDown, (IntPtr)2, where);
            Pump();
            TestNativeMethods.PostMessage(grid.Handle, RightUp, IntPtr.Zero, where);
            Pump();
        }

        private static void Key(Control control, Keys key)
        {
            TestNativeMethods.PostMessage(control.Handle, KeyDown, (IntPtr)(int)key, IntPtr.Zero);
            TestNativeMethods.PostMessage(control.Handle, KeyUp, (IntPtr)(int)key, IntPtr.Zero);
            Pump();
        }

        private static Point Middle(Rectangle area)
        {
            return new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        }

        /// <summary>
        /// The menu's first line for a row's certificate, written out here rather than asked of the
        /// member that builds it: the first eight characters of the admission key, and the day the
        /// certificate was admitted.
        /// </summary>
        private static string FirstLine(ClientListRow row)
        {
            return "Certificate " + row.Record.AdmissionKey.Substring(0, 8) + ", admitted "
                   + row.Record.Admitted.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
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

        private static readonly DateTimeOffset Admitted = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

        private static int _made;

        private static AdmissionRecord Record(string name)
        {
            using (var certificate = Minted.Named("CN=" + name)
                       .Lasting(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30))
                       .SelfSigned()
                       .Read())
            {
                // Each admitted a minute before the record made just before it, so the page,
                // which lists the certificate admitted last first, lists them in the order made.
                _made++;
                return AdmissionRecord.For(certificate, Admitted.AddMinutes(-_made), new[] { "ops" }, InventedIssuer.Id);
            }
        }
    }
}
