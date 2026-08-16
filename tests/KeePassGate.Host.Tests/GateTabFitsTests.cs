using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The KPG tab has to fit the space KeePass gives it, without scroll bars.
    /// </summary>
    /// <remarks>
    /// A rule rather than a preference, and it is measured against the real dialog because only
    /// KeePass knows how big its own tab pages are. **Nothing in KPG scrolls horizontally at all**,
    /// and vertical scrolling is a failure of the content rather than a feature of the panel: if
    /// what is on the tab does not fit, the words are too long, not the tab too small.
    ///
    /// This is the sort of thing that is obvious on somebody's screen and invisible in every other
    /// test, which is exactly why it is worth pinning.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateTabFitsTests
    {
        /// <summary>
        /// The least a list of clients may be given and still be a list. Four rows and a header,
        /// roughly: less than that and the operator is scrolling to see who is admitted.
        /// </summary>
        private const int MinimumListHeight = 90;

        /// <summary>
        /// The least that has to be left for the pages themselves beside the notice under them,
        /// which is what stops a long line about what just happened eating the page above it.
        /// </summary>
        private const int MinimumPageHeight = 200;

        private TemporaryDatabase _database;
        private Form _dialog;
        private TabPage _page;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _dialog = KeePassDialogs.NewDatabaseSettingsForm();
            KeePassDialogs.InitializeFor(_dialog, _database.Database);

            var tabs = ControlTree.FindByName<TabControl>(_dialog, KeePassContract.DatabaseSettingsTabControlName);
            Assert.That(tabs, Is.Not.Null, "KeePass no longer names its tab control what the injector looks for.");
            Assert.That(tabs.TabPages.Count, Is.GreaterThan(0));

            _page = tabs.TabPages[0];
        }

        [TearDown]
        public void TearDown()
        {
            _dialog.Dispose();
            _database.Dispose();
        }

        [Test]
        public void The_tab_KeePass_offers_is_the_size_the_panel_is_built_for()
        {
            // Printed rather than asserted exactly, so that a KeePass which changes its dialog
            // says so in the output of the tests that follow rather than only in their failure.
            TestContext.Out.WriteLine("KeePass tab page display area: " + _page.DisplayRectangle.Size);

            Assert.That(_page.DisplayRectangle.Width, Is.GreaterThan(100));
            Assert.That(_page.DisplayRectangle.Height, Is.GreaterThan(100));
        }

        [Test]
        public void Everything_on_the_tab_fits_without_a_scroll_bar()
        {
            foreach (var state in States())
            {
                using (var panel = state.Panel)
                {
                    var area = _page.DisplayRectangle.Size;
                    LayOut(panel, area);

                    foreach (var column in Columns(panel))
                    {
                        // The page this stack sits on, brought to the front first. A page behind
                        // another has invisible children, and measuring one would report that
                        // everything on it fits because there is nothing on it.
                        Show(column);

                        var room = Room(column);
                        var needed = Needed(column);

                        TestContext.Out.WriteLine(
                            state.Description + ", " + Where(column) + ": needs " + needed + " of " + room);

                        Assert.That(needed.Width, Is.LessThanOrEqualTo(room.Width),
                            state.Description + ", " + Where(column) + ", is " + needed.Width +
                            " wide in " + room.Width + ".");
                        Assert.That(needed.Height, Is.LessThanOrEqualTo(room.Height),
                            state.Description + ", " + Where(column) + ", is " + needed.Height +
                            " tall in " + room.Height +
                            ". Shorten what is on the tab rather than scrolling it.");
                    }
                }
            }
        }

        [Test]
        public void The_clients_page_keeps_room_for_the_list_under_the_invitation()
        {
            // The invitation widgets are a band at the top of that page and the list takes what is
            // left, so a band that grows takes the list with it. A page showing how to invite
            // somebody and not who has been invited would be a list behind a button, without the
            // button.
            foreach (var state in States())
            {
                using (var panel = state.Panel)
                {
                    LayOut(panel, _page.DisplayRectangle.Size);

                    var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                    Assert.That(list, Is.Not.Null, "the clients page has no list on it");

                    TestContext.Out.WriteLine(state.Description + ": the list has " + list.Height);
                    Assert.That(list.Height, Is.GreaterThanOrEqualTo(MinimumListHeight),
                        state.Description + " leaves the list " + list.Height +
                        " pixels, which is not a list.");
                }
            }
        }

        [Test]
        public void The_columns_of_the_client_list_fit_the_width_it_is_given()
        {
            // Columns that overflow would go unnoticed until somebody looked at the tab: the test
            // below cannot see a grid's scroll bars, and a column with a width written into it
            // fits the page it was written for and no other.
            foreach (var state in States())
            {
                using (var panel = state.Panel)
                {
                    LayOut(panel, _page.DisplayRectangle.Size);

                    var clients = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                    Assert.That(clients, Is.Not.Null);

                    // A page behind another is never laid out, so its controls keep the size the
                    // designer gave them and would be measured against nothing.
                    Show(clients);

                    var list = ControlTree.FindFirst<DataGridView>(clients);
                    Assert.That(list, Is.Not.Null);

                    var columns = 0;
                    foreach (DataGridViewColumn column in list.Columns)
                    {
                        columns += column.Width;
                    }

                    // The grid's own width, because the grid is what the columns are shared out
                    // across and it reserves what a scroll bar needs itself once there are rows to
                    // scroll. What this catches is a column going back to a written down width.
                    var room = list.ClientSize.Width;
                    TestContext.Out.WriteLine(state.Description + ": columns " + columns + " of " + room);

                    Assert.That(columns, Is.LessThanOrEqualTo(room),
                        state.Description + ": the client list's columns are " + columns +
                        " wide in " + room + ", so it scrolls sideways.");
                }
            }
        }

        [Test]
        public void The_invitation_box_takes_the_width_the_page_leaves()
        {
            // The row shares the page between a button, a box and an icon, and the box is the part
            // that grows. A box that ends up a few characters wide still passes every other test
            // here, because a narrow control fits: what it stops being is readable, and reading a
            // code out of it is the whole reason it is on screen.
            //
            // This pins the arithmetic rather than the hazard. A page that has never been in front
            // is sized differently by a real dialog than by a panel a test built and laid out, so
            // the way this actually went wrong is only visible on the desktop, which is what
            // `DocumentationScreenshots` walks into every time it runs. TODO(step 19): that fixture
            // arrives with the documentation.
            foreach (var state in States())
            {
                using (var panel = state.Panel)
                {
                    LayOut(panel, _page.DisplayRectangle.Size);

                    var box = ControlTree.FindByName<TextBox>(panel, GateTab.InvitationBoxName);
                    Assert.That(box, Is.Not.Null);
                    Show(box);

                    var page = Page(box);
                    Assert.That(page, Is.Not.Null);

                    var room = page.ClientSize.Width - page.Padding.Horizontal;
                    TestContext.Out.WriteLine(state.Description + ": the box has " + box.Width + " of " + room);

                    Assert.That(box.Width, Is.GreaterThanOrEqualTo(room / 2),
                        state.Description + ": the invitation box is " + box.Width +
                        " wide on a page offering " + room + ".");
                }
            }
        }

        /// <summary>
        /// Every state the tab has, because the tallest one is what has to fit. An authority makes
        /// it longer, and an invitation longer still.
        /// </summary>
        private PanelState[] States()
        {
            return new[]
            {
                new PanelState("A tab with no authority", NewPanel(false, false)),
                new PanelState("A tab with an authority", NewPanel(true, false)),
                new PanelState("A tab with an invitation outstanding", NewPanel(true, true)),
                new PanelState("A tab showing the longest of everything", Worst())
            };
        }

        /// <summary>
        /// The tab with the longest text every one of its labels can ever hold, all at once. What
        /// fits today is not the question: an authority about to expire, a fleet of expired clients
        /// and a long answer from the last button press can all be on screen together, and that is
        /// the state that has to fit.
        /// </summary>
        private AuthorityPanel Worst()
        {
            var panel = NewPanel(true, true);

            Set(panel, GateTab.ServingLabelName, GateStatusText.NotListening);
            Set(panel, GateTab.StatusLabelName, AuthorityDescription.DamagedHeadline);
            Set(panel, GateTab.DetailLabelName, AuthorityDescription.LongestDetail());
            panel.DrawFacts(AuthorityDescription.LongestFacts());
            Set(panel, GateTab.ResultLabelName,
                "The authority was recreated. 16 clients lost their admission.");
            Set(panel, GateTab.InvitationLabelName,
                "The in-memory code is good for another 10 minutes until the first client "
                + "consumes it or it gets renewed. "
                + "The approval dialog will call it 0123456789abcdef.");

            return panel;
        }

        private static void Set(Control panel, string name, string text)
        {
            var label = ControlTree.FindByName<Label>(panel, name);
            Assert.That(label, Is.Not.Null, "no label called " + name);
            label.Text = text;
        }

        private AuthorityPanel NewPanel(bool withAuthority, bool withInvitation)
        {
            var store = new CustomDataStore(_database.Database);
            var book = new InvitationBook(NullGateLog.Instance);

            if (withAuthority)
            {
                var service = new AuthorityService(store, NullGateLog.Instance);
                using (var reading = service.Read())
                {
                    if (!reading.IsPresent)
                    {
                        // Only once per database. Creating over an existing authority is refused,
                        // which is the point of that refusal and would leave this test inconclusive
                        // rather than failing.
                        var created = service.Create();
                        Assert.That(created.Succeeded, Is.True, created.Message);
                    }
                }
            }

            var panel = new AuthorityPanel(
                new OpenDatabase(_database.Database), null, NullGateLog.Instance, "1.2.3", null, book);

            if (withInvitation)
            {
                panel.InviteClient();
            }

            return panel;
        }

        /// <summary>
        /// Sizes the panel the way the dialog would and lets it settle.
        /// </summary>
        /// <remarks>
        /// The handle is created rather than left alone, because a `TabControl` without one never
        /// sizes its pages: they stay the 200 by 100 the designer gives them, and everything
        /// measured on one would be measured against a page no operator will ever see.
        /// </remarks>
        private static void LayOut(Control panel, Size area)
        {
            panel.Size = area;
            panel.CreateControl();
            panel.PerformLayout();
        }

        /// <summary>
        /// Every stack of content on the tab: one for each page, the invitation band above the
        /// client list, and the notice below the pages.
        /// </summary>
        /// <remarks>
        /// Measured one stack at a time rather than as a whole, because the pages take turns. What
        /// has to fit is each page in the room that page is given, not the sum of both, which is
        /// the whole reason there are three.
        /// </remarks>
        private static FlowLayoutPanel[] Columns(Control panel)
        {
            var found = new List<FlowLayoutPanel>();

            foreach (var control in ControlTree.Descendants(panel))
            {
                var column = control as FlowLayoutPanel;

                // The stacks, not the rows inside them. A row is as wide as its buttons and is
                // measured by the stack that holds it.
                if (column != null && column.FlowDirection == FlowDirection.TopDown)
                {
                    found.Add(column);
                }
            }

            Assert.That(found.Count, Is.GreaterThanOrEqualTo(3),
                "the panel is not laid out the way this test measures it");

            return found.ToArray();
        }

        /// <summary>
        /// Brings the page this stack sits on to the front, so that what is on it has a size.
        /// </summary>
        private static void Show(Control control)
        {
            var page = Page(control);
            if (page == null)
            {
                return;
            }

            var tabs = page.Parent as TabControl;
            Assert.That(tabs, Is.Not.Null);

            tabs.SelectedTab = page;
            page.PerformLayout();
        }

        /// <summary>The page a control is on, however deep inside it sits, or null.</summary>
        private static TabPage Page(Control control)
        {
            for (var at = control; at != null; at = at.Parent)
            {
                var page = at.Parent as TabPage;
                if (page != null)
                {
                    return page;
                }
            }

            return null;
        }

        /// <summary>How much room whatever holds this stack is giving it.</summary>
        private static Size Room(Control column)
        {
            var parent = column.Parent;
            Assert.That(parent, Is.Not.Null);

            return new Size(
                parent.ClientSize.Width - parent.Padding.Horizontal,
                parent.ClientSize.Height - parent.Padding.Vertical);
        }

        /// <summary>
        /// How much room the contents actually want. Measured from the laid out controls rather
        /// than from a preferred size, because a stack that fills its page would report back
        /// whatever it was given.
        /// </summary>
        private static Size Needed(FlowLayoutPanel column)
        {
            var width = 0;
            var height = 0;

            foreach (Control child in column.Controls)
            {
                if (!child.Visible)
                {
                    continue;
                }

                width = Math.Max(width, child.Width + child.Margin.Horizontal);
                height += child.Height + child.Margin.Vertical;
            }

            // A stack docked to an edge shares its room with whatever fills the rest, so what it
            // needs includes the room that has to be left over.
            if (column.Dock == DockStyle.Top || column.Dock == DockStyle.Bottom)
            {
                height += RoomForTheRest(column);
            }

            return new Size(width + column.Padding.Horizontal, height + column.Padding.Vertical);
        }

        /// <summary>
        /// What a docked band has to leave for whatever fills the rest of the room it is in.
        /// </summary>
        /// <remarks>
        /// Two bands and two answers. The notice is docked to the bottom of the tab and shares it
        /// with the pages, so what has to be left is a page. The invitation band is docked to the
        /// top of the clients page and shares it with the list, so what has to be left is a list.
        ///
        /// **Told apart by the edge rather than by looking for the list**, because looking found it
        /// either way: `FindByName` walks everything under what it is given, and the notice's
        /// parent is the panel, which holds the clients page and therefore the list. Both bands
        /// answered `MinimumListHeight`, so the notice was measured against ninety pixels where two
        /// hundred was meant and `MinimumPageHeight` was named by nothing.
        /// </remarks>
        private static int RoomForTheRest(FlowLayoutPanel column)
        {
            return column.Dock == DockStyle.Bottom ? MinimumPageHeight : MinimumListHeight;
        }

        private static string Where(Control column)
        {
            var page = Page(column);
            if (page != null)
            {
                return "the " + page.Text + " page";
            }

            return column.Dock == DockStyle.Bottom ? "below the pages" : "above the pages";
        }

        private sealed class PanelState
        {
            public PanelState(string description, AuthorityPanel panel)
            {
                Description = description;
                Panel = panel;
            }

            public string Description { get; private set; }

            public AuthorityPanel Panel { get; private set; }
        }
    }
}
