using System;
using System.Globalization;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
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
        private TemporaryDatabase _several;
        private TemporaryDatabase _damaged;
        private Form _dialog;
        private TabPage _page;
        private ImageList _icons;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _dialog = KeePassDialogs.NewDatabaseSettingsForm();
            KeePassDialogs.InitializeFor(_dialog, _database.Database);

            var tabs = ControlTree.FindByName<TabControl>(_dialog, KeePassContract.DatabaseSettingsTabControlName);
            Assert.That(tabs, Is.Not.Null, "KeePass no longer names its tab control what the injector looks for.");
            Assert.That(tabs.TabPages.Count, Is.GreaterThan(0));

            TestContext.Out.WriteLine("page without icons: " + tabs.TabPages[0].DisplayRectangle.Size);

            // With the icon on, because that is what ships. A tab strip showing pictures can be
            // taller than one showing only words, and every pixel it takes is a pixel this page
            // does not have. Both sizes are printed rather than asserted: this dialog is never
            // shown here, so the strip is not always re-measured, and what the two lines are for is
            // a KeePass where they stop agreeing.
            _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
            _icons.Images.Add(new Bitmap(16, 16));
            tabs.ImageList = _icons;

            _page = tabs.TabPages[0];
            TestContext.Out.WriteLine("page with icons:    " + _page.DisplayRectangle.Size);
        }

        [TearDown]
        public void TearDown()
        {
            _dialog.Dispose();

            if (_icons != null)
            {
                _icons.Dispose();
                _icons = null;
            }

            _database.Dispose();

            if (_several != null)
            {
                _several.Dispose();
                _several = null;
            }

            if (_damaged != null)
            {
                _damaged.Dispose();
                _damaged = null;
            }
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
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void Everything_still_fits_when_the_display_is_scaled(float scaling)
        {
            // **The room scales too, and that is the whole answer.** KeePass's dialogs are
            // `AutoScaleMode.Font` against `AutoScaleDimensions` of 6 by 13, so a larger system
            // font makes a larger dialog: measured on 2.61.1, the tab page goes from 449 by 314 at
            // font height 13 to 599 by 410 at 16 and 674 by 482 at 19. The room grows faster than
            // the text does, which is why a page that fits at one scaling fits at all of them.
            //
            // What this test is for is the case where that stops being true: a control of ours
            // sized in pixels rather than in text, which then stays the same while everything
            // around it grows. `PairingApprovalForm` was exactly that.
            using (var scaled = ScaledDialog(scaling))
            {
                var area = PageOf(scaled.Dialog);

                TestContext.Out.WriteLine(
                    "at " + scaling.ToString("0.00", CultureInfo.InvariantCulture) +
                    " the tab page is " + area);

                Assert.That(area.Width, Is.GreaterThan(_page.DisplayRectangle.Width),
                    "KeePass stopped scaling its dialog, so nothing below is measuring what it claims");

                // What `TabPageInjector` scales a page by as it attaches it: as much as KeePass
                // scaled the dialog. Measured with it, because a tab measured without it is a tab no
                // scaled display shows.
                var scale = Spacing.ScaleOf(scaled.Dialog);

                foreach (var state in States())
                {
                    using (var panel = state.Panel)
                    {
                        panel.Scale(scale);
                        panel.Font = scaled.Font;
                        LayOut(panel, area);

                        Assert.That(panel.Padding.Left, Is.EqualTo((int)Math.Round(Spacing.Gap * scale.Width)),
                            state.Description + " kept an unscaled inset at " +
                            scaling.ToString("0.00", CultureInfo.InvariantCulture) + " scaling");

                        // The invitation box has a font of its own, and the row holding it is
                        // measured below: at the size the panel was built at, it was measured
                        // smaller than any operator sees it (K087).
                        var box = ControlTree.FindByName<TextBox>(panel, GateTab.InvitationBoxName);
                        Assert.That(box.Font.SizeInPoints, Is.EqualTo(scaled.Font.SizeInPoints).Within(0.01f),
                            state.Description + ": the invitation box kept the unscaled font at " +
                            scaling.ToString("0.00", CultureInfo.InvariantCulture) + " scaling");

                        foreach (var column in Columns(panel))
                        {
                            Show(column);

                            var room = Room(column);
                            var needed = Needed(column);
                            var where = state.Description + ", " + Where(column) + ", at " +
                                        scaling.ToString("0.00", CultureInfo.InvariantCulture) + " scaling";

                            Assert.That(needed.Width, Is.LessThanOrEqualTo(room.Width),
                                where + ", is " + needed.Width + " wide in " + room.Width + ".");
                            Assert.That(needed.Height, Is.LessThanOrEqualTo(room.Height),
                                where + ", is " + needed.Height + " tall in " + room.Height +
                                ". Shorten what is on the tab rather than scrolling it.");
                        }
                    }
                }

                // And what the page builds after it was scaled: the facts are drawn again on every
                // reading, which a scaling of the controls already there never reaches.
                using (var panel = NewPanel(true, false))
                {
                    panel.Scale(scale);
                    panel.DrawFacts(AuthorityDescription.LongestFacts());

                    var facts = ControlTree.FindByName<TableLayoutPanel>(panel, GateTab.FactsTableName);
                    var name = facts.GetControlFromPosition(0, 0);

                    Assert.That(name.Margin.Right, Is.EqualTo((int)Math.Round(Spacing.Gap * scale.Width)),
                        "a fact drawn after the page was scaled kept the unscaled gap");
                }

                // And the profile name box, which the page never sizes: it kept the width a text
                // box is born with at every scaling while the label and the button beside it grew,
                // so at 200 percent it showed half as many characters of a name as at 100.
                using (var plain = NewPanel(false, false))
                using (var panel = NewPanel(false, false))
                {
                    panel.Scale(scale);

                    // The room inside it, since a box's frame is drawn at the same width whatever
                    // the scaling and only what it holds grows.
                    var before = ControlTree.FindByName<TextBox>(plain, ProfileList.NameBoxName).ClientSize.Width;
                    var after = ControlTree.FindByName<TextBox>(panel, ProfileList.NameBoxName).ClientSize.Width;

                    Assert.That(after, Is.EqualTo(before * scale.Width).Within(1),
                        "the profile name box did not grow with the dialog");
                }
            }
        }

        /// <summary>
        /// What every page needs against what it is given, at four text sizes, with the room held
        /// at what one scaling gives it.
        /// </summary>
        /// <remarks>
        /// **An instrument rather than a gate**, and the difference from the test above is the
        /// point of it: this one grows the text and keeps the room, which is not what a scaled
        /// display does. It answers a narrower question, what headroom the page has against the
        /// room it has today, and the answer is about a tenth in the worst case, where every label
        /// carries the longest text it can hold at once.
        ///
        /// Run it with `dotnet test --filter Headroom_at_one_room_size` and read the output.
        /// </remarks>
        [Test]
        [Explicit("a measurement to read, not a gate to pass")]
        public void Headroom_at_one_room_size()
        {
            foreach (var scaling in new[] { 1.00f, 1.10f, 1.25f, 1.50f })
            {
                foreach (var state in States())
                {
                    using (var panel = state.Panel)
                    using (var font = Scaled(panel.Font, scaling))
                    {
                        panel.Font = font;
                        LayOut(panel, _page.DisplayRectangle.Size);

                        foreach (var column in Columns(panel))
                        {
                            Show(column);
                            var room = Room(column);
                            var needed = Needed(column);
                            TestContext.Out.WriteLine(
                                scaling.ToString("0.00", CultureInfo.InvariantCulture) +
                                "  " + state.Description + ", " + Where(column) +
                                ": needs " + needed + " of " + room +
                                (needed.Height > room.Height || needed.Width > room.Width ? "   OVER" : ""));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// KeePass's own settings dialog, built at a larger font, which is what a scaled display
        /// hands it.
        /// </summary>
        private ScaledSettingsDialog ScaledDialog(float scaling)
        {
            var dialog = KeePassDialogs.NewDatabaseSettingsForm();
            KeePassDialogs.InitializeFor(dialog, _database.Database);

            var font = Scaled(dialog.Font, scaling);
            dialog.Font = font;
            dialog.CreateControl();
            dialog.PerformLayout();

            return new ScaledSettingsDialog(dialog, font);
        }

        /// <summary>
        /// The room the tab strip leaves on a dialog, with this plugin's tab and its icon at the
        /// size a display scaled this far hands it over.
        /// </summary>
        private static Size PageOf(Form dialog)
        {
            return TabRoom.Of(dialog, KeePassContract.DatabaseSettingsTabControlName);
        }

        /// <summary>The same font, larger, which is what a scaled display amounts to here.</summary>
        private static Font Scaled(Font font, float scaling)
        {
            return new Font(font.FontFamily, font.Size * scaling, font.Style);
        }

        /// <summary>A dialog built at one scaling, and the font it was built at.</summary>
        private sealed class ScaledSettingsDialog : IDisposable
        {
            private readonly Font _font;

            public ScaledSettingsDialog(Form dialog, Font font)
            {
                Dialog = dialog;
                _font = font;
            }

            public Form Dialog { get; }

            public Font Font
            {
                get { return _font; }
            }

            public void Dispose()
            {
                Dialog.Dispose();
                _font.Dispose();
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

                    // Its own page in front, because a page behind another is never laid out and
                    // what is on it then has whatever size it was built with.
                    Show(list);

                    TestContext.Out.WriteLine(state.Description + ": the list has " + list.Height);
                    Assert.That(list.Height, Is.GreaterThanOrEqualTo(MinimumListHeight),
                        state.Description + " leaves the list " + list.Height +
                        " pixels, which is not a list.");
                }
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void The_line_counting_revoked_clients_a_merge_brought_back_fits_under_the_list(float scaling)
        {
            // A band this page has only after a merge, under the list and above the line a gesture
            // answers on. Both are shown at once with the longest thing each can say, a count four
            // digits wide and the sentence for a listing that could not be read, and the list still
            // has to be a list. At every scaling, because the room grows with the display and a
            // band of ours that did not would be the one thing measured against the wrong room.
            using (var scaled = scaling > 1.00f ? ScaledDialog(scaling) : null)
            {
                var area = scaled == null ? _page.DisplayRectangle.Size : PageOf(scaled.Dialog);
                var where = " at " + scaling.ToString("0.00", CultureInfo.InvariantCulture) + " scaling";

                foreach (var state in States())
                {
                    using (var panel = state.Panel)
                    {
                        if (scaled != null)
                        {
                            panel.Font = scaled.Font;
                        }

                        LayOut(panel, area);

                        var clients = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                        Assert.That(clients, Is.Not.Null);

                        // The page in front first, because bringing it forward reads the database
                        // again and fills the list with what is really there.
                        Show(clients);
                        clients.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), 9999, true), null, null);
                        Show(clients, ClientList.ResultLabelName, ClientList.UnreadableText);
                        clients.PerformLayout();

                        var line = ControlTree.FindByName<Label>(clients, ClientList.ReappearedLabelName);
                        Assert.That(line, Is.Not.Null);
                        Assert.That(clients.Reappeared, Is.EqualTo(ClientList.ReappearedText(9999)));

                        var grid = ControlTree.FindFirst<DataGridView>(clients);
                        var needed = line.GetPreferredSize(new Size(line.Width, 0));

                        TestContext.Out.WriteLine(state.Description + where + ": the line is " + line.Size +
                                                  " needing " + needed.Height + ", the list has " + grid.Height);

                        Assert.That(line.Right, Is.LessThanOrEqualTo(clients.ClientSize.Width),
                            state.Description + where + ": the line runs off the side of the page.");
                        Assert.That(line.Height, Is.GreaterThanOrEqualTo(needed.Height),
                            state.Description + where + ": the line is cut short.");
                        Assert.That(grid.Height, Is.GreaterThanOrEqualTo(MinimumListHeight),
                            state.Description + where + " leaves the list " + grid.Height +
                            " pixels, which is not a list.");
                    }
                }
            }
        }

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void The_notice_over_the_invite_button_leaves_the_list_its_room(float scaling)
        {
            // The band over the invite button says why a code handed out now could not be
            // redeemed, and the client list under it takes whatever the band leaves. Bringing the
            // page forward reads the database again and hides a notice this database has no reason
            // for, and every measurement of the page brings it forward first, so no fit test had a
            // notice on screen. Each one is put back afterwards, on the most crowded page it can
            // really be on: beside the longest line it can share the band with, over the two lines
            // a merge adds under the list, and above the longest answer the footer holds.
            //
            // The two waiting for OK can sit beside a code still counting down, handed out before
            // the change was made. An authority or a host certificate that has run out offers no
            // code, so theirs only ever sit beside the line saying there is none.
            using (var scaled = scaling > 1.00f ? ScaledDialog(scaling) : null)
            {
                var area = scaled == null ? _page.DisplayRectangle.Size : PageOf(scaled.Dialog);
                var where = " at " + scaling.ToString("0.00", CultureInfo.InvariantCulture) + " scaling";

                var besideACountdown = new Dictionary<string, bool>
                {
                    { GateTab.PendingAuthorityNotice, true },
                    { GateTab.PendingProfilesNotice, true },
                    { GateTab.ExpiredAuthorityNotice, false },
                    { GateTab.ExpiredHostNotice, false }
                };

                foreach (var notice in besideACountdown)
                {
                    using (var panel = NewPanel(true, true))
                    {
                        // Scaled as `TabPageInjector` scales the page it attaches, which is the
                        // page a scaled display shows.
                        if (scaled != null)
                        {
                            panel.Scale(Spacing.ScaleOf(scaled.Dialog));
                            panel.Font = scaled.Font;
                        }

                        LayOut(panel, area);

                        var clients = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                        Assert.That(clients, Is.Not.Null);
                        Show(clients);

                        var line = notice.Value ? LongestInvitationLine(panel) : InvitationText.None;
                        Set(panel, GateTab.InvitationLabelName, line);
                        Show(panel, GateTab.InviteNoticeName, notice.Key);
                        Set(panel, GateTab.ResultLabelName, AuthorityPanel.LongestResult());
                        clients.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), 9999, false), null, null);

                        panel.PerformLayout();
                        Page(clients).PerformLayout();
                        clients.PerformLayout();

                        var shown = ControlTree.FindByName<Label>(panel, GateTab.InviteNoticeName);
                        var grid = ControlTree.FindFirst<DataGridView>(clients);

                        TestContext.Out.WriteLine("\"" + notice.Key + "\"" + where + ": the notice is " +
                                                  shown.Size + ", the list has " + grid.Height);

                        Assert.That(shown.Visible, Is.True, "the notice is not on screen, so nothing here measures it");
                        Assert.That(clients.Reappeared, Is.Not.Null, "and neither is the line a merge adds");
                        Assert.That(grid.Height, Is.GreaterThanOrEqualTo(MinimumListHeight),
                            "\"" + notice.Key + "\"" + where + " leaves the list " + grid.Height +
                            " pixels, which is not a list.");
                    }
                }
            }
        }

        [Test]
        public void The_profiles_page_keeps_room_for_the_list_under_the_line_that_adds_one()
        {
            // The same shape as the clients page and the same hazard: the row that adds a profile
            // is a band at the top and the list takes what is left. This page is measured on its
            // own because nothing else measures it: the whole-tab test walks the top-down stacks,
            // and this page has none, so a list squeezed to nothing here would show up on somebody's
            // screen rather than in a test.
            foreach (var state in States())
            {
                using (var panel = state.Panel)
                {
                    LayOut(panel, _page.DisplayRectangle.Size);

                    var list = ControlTree.FindByName<ProfileList>(panel, ProfileList.ControlName);
                    Assert.That(list, Is.Not.Null, "the profiles page has no list on it");

                    // With the longest thing a removal says under it, which is a band this page
                    // does not otherwise have: the longest name a profile can have, and counts
                    // four digits wide, because the sentence names both.
                    var said = ControlTree.FindByName<Label>(list, ProfileList.ResultLabelName);
                    Assert.That(said, Is.Not.Null);
                    said.Text = DatabaseProfiles.Said(new string('p', 64), 9999, 9999);
                    said.Visible = true;

                    // Its own page in front, for the reason the clients page needs it: a page
                    // behind another is never laid out.
                    Show(list);

                    TestContext.Out.WriteLine(state.Description + ": the profile list has " + list.Height);
                    Assert.That(list.Height, Is.GreaterThanOrEqualTo(MinimumListHeight),
                        state.Description + " leaves the profile list " + list.Height +
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

        [Test]
        [TestCase(1.00f)]
        [TestCase(1.25f)]
        [TestCase(1.50f)]
        [TestCase(2.00f)]
        public void The_invitation_box_shows_the_whole_of_a_code(float scaling)
        {
            // The hazard the test above leaves alone. A box wide enough to read out of still cut
            // the last character off at 100 percent, which is what the row left it beside "Renew
            // invite", and nothing here noticed: a narrower box fits the page as well as a wide
            // one. So the edit control itself is asked, with a code in it, where it puts the last
            // character, against the room it draws text in.
            using (var scaled = scaling > 1.00f ? ScaledDialog(scaling) : null)
            {
                var area = scaled == null ? _page.DisplayRectangle.Size : PageOf(scaled.Dialog);
                var where = " at " + scaling.ToString("0.00", CultureInfo.InvariantCulture) + " scaling";

                foreach (var state in States())
                {
                    using (var panel = state.Panel)
                    {
                        // Scaled as `TabPageInjector` scales the page it attaches, which is the
                        // page a scaled display shows.
                        if (scaled != null)
                        {
                            panel.Scale(Spacing.ScaleOf(scaled.Dialog));
                            panel.Font = scaled.Font;
                        }

                        LayOut(panel, area);

                        var box = ControlTree.FindByName<TextBox>(panel, GateTab.InvitationBoxName);
                        Assert.That(box, Is.Not.Null);
                        Show(box);

                        // Any code will do in a monospaced font, since every one is as wide as
                        // every other, and a letter this wide makes one whatever the font.
                        box.Text = new string('W', PairingToken.TextLength);
                        box.Select(0, 0);
                        box.ScrollToCaret();

                        var room = new EditRect();
                        SendMessage(box.Handle, GetRect, IntPtr.Zero, ref room);

                        var first = PositionOf(box, 0);
                        var last = PositionOf(box, box.TextLength - 1) + (PositionOf(box, 1) - first);

                        TestContext.Out.WriteLine(state.Description + where + ": the code runs from " + first +
                                                  " to " + last + " in a box drawing text from " +
                                                  room.Left + " to " + room.Right);

                        Assert.That(first, Is.GreaterThanOrEqualTo(room.Left),
                            state.Description + where + ": the box has scrolled the start of the code away.");
                        Assert.That(last, Is.LessThanOrEqualTo(room.Right),
                            state.Description + where + ": the invitation box cuts the code off at " +
                            room.Right + ", where its last character ends at " + last + ".");
                    }
                }
            }
        }

        /// <summary>Where an edit control draws one of its characters, from its left edge.</summary>
        private static int PositionOf(TextBox box, int index)
        {
            return (short)(SendMessage(box.Handle, PositionFromCharacter, new IntPtr(index), IntPtr.Zero).ToInt64() & 0xFFFF);
        }

        /// <summary>The message asking an edit control for the rectangle it draws its text in.</summary>
        private const int GetRect = 0x00B2;

        /// <summary>The message asking it where it draws one character.</summary>
        private const int PositionFromCharacter = 0x00D6;

        [StructLayout(LayoutKind.Sequential)]
        private struct EditRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref EditRect low);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr low);

        /// <summary>
        /// Every state the tab has, because the tallest one is what has to fit. An authority makes
        /// it longer, and an invitation longer still.
        /// </summary>
        private PanelState[] States()
        {
            var states = new List<PanelState>
            {
                new PanelState("A tab with no authority", NewPanel(false, false)),
                new PanelState("A tab with an authority", NewPanel(true, false)),
                new PanelState("A tab with an invitation outstanding", NewPanel(true, true))
            };

            // One under each headline, because which of them is tallest is not something to
            // assume: each with the longest detail that headline comes with.
            foreach (var longest in AuthorityDescription.LongestDescriptions())
            {
                states.Add(new PanelState(
                    "A tab showing the longest of everything under \"" + longest.Headline + "\"",
                    Worst(longest)));
            }

            states.Add(new PanelState("A tab holding several authorities", Undecided()));
            states.Add(new PanelState("A tab holding an authority that cannot be used", Damaged()));

            return states.ToArray();
        }

        /// <summary>
        /// A database whose one authority will not read, on a database of its own for the reason
        /// <see cref="Undecided"/> gives.
        /// </summary>
        /// <remarks>
        /// Drawn by the panel rather than told what to say. The states above set their longest
        /// text by hand, which measures words; this is the page a damaged reading really draws,
        /// with the buttons it really offers, which no state measured before.
        /// </remarks>
        private AuthorityPanel Damaged()
        {
            _damaged?.Dispose();
            _damaged = TemporaryDatabase.New();
            new CustomDataStore(_damaged.Database).Set(AuthorityKeys.AuthorityKey(new string('d', 64)), "not an authority");

            var panel = new AuthorityPanel(new OpenDatabase(_damaged.Database), null, NullGateLog.Instance);
            Assert.That(ControlTree.FindByName<Label>(panel, GateTab.StatusLabelName).Text,
                Is.EqualTo(AuthorityDescription.DamagedHeadline), "the state is not the one it is named for");

            Set(panel, GateTab.ResultLabelName, AuthorityPanel.LongestResult());
            return panel;
        }

        /// <summary>
        /// A database holding two authorities nobody has chosen between, on a database of its own
        /// so that the other states go on reading theirs: a page brought forward reads again.
        /// </summary>
        private AuthorityPanel Undecided()
        {
            // The last one first, since the measurement that is only ever run by hand asks for
            // every state once per text size, and its panels are gone by then.
            _several?.Dispose();
            _several = TemporaryDatabase.New();
            var store = new CustomDataStore(_several.Database);
            var service = new AuthorityService(store, NullGateLog.Instance);

            Assert.That(service.Create().Succeeded, Is.True);
            var first = store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            var document = store.Get(first);
            store.Remove(first);
            Assert.That(service.Create().Succeeded, Is.True);
            store.Set(first, document);

            var panel = new AuthorityPanel(new OpenDatabase(_several.Database), null, NullGateLog.Instance);
            Set(panel, GateTab.ResultLabelName, AuthorityPanel.LongestResult());
            return panel;
        }

        /// <summary>
        /// The tab with the longest text every one of its labels can ever hold, all at once, under
        /// one headline and the longest detail it comes with. What fits today is not the question:
        /// an authority about to expire, a fleet of expired clients and a long answer from the last
        /// button press can all be on screen together, and that is the state that has to fit.
        /// </summary>
        /// <remarks>
        /// The facts are drawn whatever the headline, which is more than a damaged page ever
        /// shows, so this measures more than can happen rather than less.
        /// </remarks>
        private AuthorityPanel Worst(AuthorityDescription longest)
        {
            var panel = NewPanel(true, true);

            Set(panel, GateTab.StatusLabelName, longest.Headline);
            Set(panel, GateTab.DetailLabelName, longest.DetailText);
            panel.DrawFacts(AuthorityDescription.LongestFacts());
            Set(panel, GateTab.ResultLabelName, AuthorityPanel.LongestResult());

            // Written again whenever a page is brought forward, which every measurement of the
            // clients page does first: the panel reads the database again then and puts back the
            // countdown of the code it really holds, under whatever digits happened to name it.
            // Handlers run in the order they were added, so this runs after the panel's own.
            var line = LongestInvitationLine(panel);
            Set(panel, GateTab.InvitationLabelName, line);
            ControlTree.FindFirst<TabControl>(panel).SelectedIndexChanged +=
                delegate { Set(panel, GateTab.InvitationLabelName, line); };

            // Shown, because it can be: an operator who created an authority and went straight to
            // the clients page is looking at this and at everything above it at the same time.
            // Whichever of its sentences is longer, since the band holds one at a time and the
            // question here is how much room the longest one needs.
            //
            // **Put back whenever a page comes forward, as the line is**, because that reads the
            // database again and hides it: nothing in this one is waiting or has run out. Left
            // hidden, no measurement of the clients page had it on, and at three lines it left the
            // client list 72 of its 90 pixels under the two lines a merge adds. Each notice beside
            // what it can really share the page with is measured by
            // `The_notice_over_the_invite_button_leaves_the_list_its_room`.
            Show(panel, GateTab.InviteNoticeName, GateTab.LongestInviteNotice);
            ControlTree.FindFirst<TabControl>(panel).SelectedIndexChanged +=
                delegate { Show(panel, GateTab.InviteNoticeName, GateTab.LongestInviteNotice); };

            return panel;
        }

        /// <summary>
        /// The longest sentence the invitation line can hold, which is the countdown under a code
        /// that has just been made.
        /// </summary>
        /// <remarks>
        /// **Composed by `InvitationText.For` rather than typed here**, so that a longer wording, a
        /// lifetime with more digits or a longer identifier is what this measures on the day it
        /// arrives. The code runs out a whole `DefaultLifetime` from now, which is the most minutes
        /// the line ever counts, and its identifier is as long as the book makes one and spelled in
        /// whichever hex digit is widest in the line's own font. The fixed sentences are measured
        /// against it, since the line holds one of those instead whenever there is nothing to count
        /// down, and the widest of them all is what the line is given.
        /// </remarks>
        private static string LongestInvitationLine(Control panel)
        {
            var font = ControlTree.FindByName<Label>(panel, GateTab.InvitationLabelName).Font;

            int length;
            using (var book = new InvitationBook(NullGateLog.Instance))
            {
                length = book.Invite("measured").Id.Length;
            }

            var digit = '0';
            foreach (var candidate in "0123456789abcdef")
            {
                if (WidthOf(new string(candidate, length), font) > WidthOf(new string(digit, length), font))
                {
                    digit = candidate;
                }
            }

            var now = DateTimeOffset.UtcNow;
            string longest;
            using (var invitation = new Invitation(
                new string(digit, length), "measured", PairingToken.New(), now, now + InvitationBook.DefaultLifetime))
            {
                longest = InvitationText.For(new[] { invitation }, now);
            }

            foreach (var field in typeof(InvitationText).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                var said = field.IsLiteral ? field.GetRawConstantValue() as string : null;
                if (said != null && WidthOf(said, font) > WidthOf(longest, font))
                {
                    longest = said;
                }
            }

            return longest;
        }

        private static int WidthOf(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font).Width;
        }

        /// <summary>A label that hides itself when it has nothing to say, made to say it.</summary>
        private static void Show(Control panel, string name, string text)
        {
            Set(panel, name, text);
            ControlTree.FindByName<Label>(panel, name).Visible = true;
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

            var panel = new AuthorityPanel(new OpenDatabase(_database.Database), null, NullGateLog.Instance, book);

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
