using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
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
        private TemporaryDatabase _database;
        private Form _dialog;
        private TabPage _page;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _dialog = KeePassDialogs.NewDatabaseSettingsForm();

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
                    panel.Size = area;
                    panel.PerformLayout();

                    var needed = Needed(panel);
                    TestContext.Out.WriteLine(state.Description + " needs " + needed + " of " + area);

                    // Width is asserted here and nowhere else. A second test used to assert
                    // `HorizontalScroll.Visible` is false on every control, which cannot fail:
                    // that property is hard wired false whenever `AutoScroll` is false, which
                    // it is for everything on this tab. Measured by giving a panel a child
                    // forty times its width and reading the property: still false, and true
                    // the moment `AutoScroll` is set. With no horizontal scrolling possible
                    // the question is whether the content fits, which is this line.
                    Assert.That(needed.Width, Is.LessThanOrEqualTo(area.Width),
                        state.Description + " is " + needed.Width + " wide in a tab " + area.Width + " wide.");
                    Assert.That(needed.Height, Is.LessThanOrEqualTo(area.Height),
                        state.Description + " is " + needed.Height + " tall in a tab " + area.Height +
                        " tall. Shorten what is on the tab rather than scrolling it.");
                }
            }
        }

        /// <summary>Every state the tab has, because the tallest one is what has to fit.</summary>
        private PanelState[] States()
        {
            return new[]
            {
                new PanelState("A tab with no authority", NewPanel(false)),
                new PanelState("A tab with an authority", NewPanel(true)),
                new PanelState("A tab showing the longest of everything", Worst())
            };
        }

        private AuthorityPanel NewPanel(bool withAuthority)
        {
            var store = new CustomDataStore(_database.Database);

            if (withAuthority)
            {
                var service = new AuthorityService(store, NullGateLog.Instance);
                using (var reading = service.Read())
                {
                    if (!reading.IsPresent)
                    {
                        // Only once per database. Creating over an existing authority is refused,
                        // which is the point of that refusal.
                        var created = service.Create();
                        Assert.That(created.Succeeded, Is.True, created.Message);
                    }
                }
            }

            return new AuthorityPanel(
                new OpenDatabase(_database.Database), new NeverAsked(), NullGateLog.Instance, "1.2.3");
        }

        /// <summary>
        /// The tab with the longest text every one of its labels can ever hold, all at once. What
        /// fits today is not the question: an authority about to expire and a long answer from the
        /// last button press can be on screen together, and that is the state that has to fit.
        /// </summary>
        private AuthorityPanel Worst()
        {
            var panel = NewPanel(true);

            Set(panel, GateTab.StatusLabelName, AuthorityDescription.DamagedHeadline);
            Set(panel, GateTab.DetailLabelName, AuthorityDescription.LongestDetail());
            panel.DrawFacts(AuthorityDescription.LongestFacts());
            Set(panel, GateTab.ResultLabelName,
                "The authority was recreated. 16 clients lost their admission.");

            return panel;
        }

        private static void Set(Control panel, string name, string text)
        {
            var label = ControlTree.FindByName<Label>(panel, name);
            Assert.That(label, Is.Not.Null, "no label called " + name);
            label.Text = text;
        }

        /// <summary>
        /// How much room the contents actually want. Measured from the laid out controls rather
        /// than from a preferred size, because the panel's own child is docked to fill it and would
        /// report back whatever it was given.
        /// </summary>
        private static System.Drawing.Size Needed(Control panel)
        {
            var flow = ControlTree.FindFirst<FlowLayoutPanel>(panel);
            Assert.That(flow, Is.Not.Null, "the panel is not laid out the way this test measures it");

            var width = 0;
            var height = 0;

            foreach (Control child in flow.Controls)
            {
                if (!child.Visible)
                {
                    continue;
                }

                width = Math.Max(width, child.Width + child.Margin.Horizontal);
                height += child.Height + child.Margin.Vertical;
            }

            return new System.Drawing.Size(
                width + flow.Padding.Horizontal + panel.Padding.Horizontal,
                height + flow.Padding.Vertical + panel.Padding.Vertical);
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
