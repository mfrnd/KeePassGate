using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TabPageInjectorTests
    {
        private RecordingGateLog _log;
        private StubPanelFactory _panels;
        private TabPageInjector _injector;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
            _panels = new StubPanelFactory();
            _injector = new TabPageInjector(_panels, _log);
        }

        [Test]
        public void Attaching_adds_one_page_captioned_KPG_at_the_end()
        {
            using (var form = Dialogs.WithNestedTabControl("General", "Security"))
            {
                Assert.That(_injector.Attach(form), Is.True);

                var tabs = Dialogs.TabsOf(form);
                Assert.That(tabs.TabPages.Cast<TabPage>().Select(p => p.Text),
                    Is.EqualTo(new[] { "General", "Security", GateTab.Text }));
            }
        }

        [Test]
        public void The_page_carries_the_control_name_everything_finds_it_by()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(form);

                var page = PageOf(form);
                Assert.That(page.Name, Is.EqualTo(GateTab.PageControlName));
            }
        }

        [Test]
        public void The_page_holds_the_factory_panel_filling_it()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(form);

                var page = PageOf(form);
                Assert.That(_panels.CreatedCount, Is.EqualTo(1));
                Assert.That(page.Controls.Cast<Control>().Single(), Is.SameAs(_panels.Last));
                Assert.That(_panels.Last.Dock, Is.EqualTo(DockStyle.Fill));
            }
        }

        [Test]
        public void Attaching_twice_leaves_one_page_and_builds_one_panel()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(_injector.Attach(form), Is.True);
                Assert.That(_injector.Attach(form), Is.True);

                Assert.That(Dialogs.TabsOf(form).TabPages.Count, Is.EqualTo(2));
                Assert.That(_panels.CreatedCount, Is.EqualTo(1));
                Assert.That(_injector.AttachedCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Two_dialogs_open_at_once_each_get_their_own_page()
        {
            using (var first = Dialogs.WithNestedTabControl("General"))
            using (var second = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(first);
                _injector.Attach(second);

                Assert.That(_injector.AttachedCount, Is.EqualTo(2));
                Assert.That(PageOf(first), Is.Not.SameAs(PageOf(second)));
            }
        }

        [Test]
        public void A_dialog_without_a_tab_control_is_refused_and_reported()
        {
            using (var form = Dialogs.WithoutTabControl())
            {
                Assert.That(_injector.Attach(form), Is.False);
                Assert.That(_injector.AttachedCount, Is.Zero);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("no tab control")), Is.True);
            }
        }

        [Test]
        public void A_null_form_is_refused_rather_than_throwing()
        {
            Assert.That(_injector.Attach(null), Is.False);
            Assert.That(_log.Warnings, Is.Not.Empty);
        }

        [Test]
        public void A_renamed_tab_control_is_used_as_a_fallback_and_the_fallback_is_reported()
        {
            using (var form = Dialogs.WithTabControlNamed("m_tabSomethingElse", "General"))
            {
                Assert.That(_injector.Attach(form), Is.True);
                Assert.That(PageOf(form), Is.Not.Null);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("falling back")), Is.True);
            }
        }

        [Test]
        public void The_named_tab_control_wins_over_an_earlier_unnamed_one()
        {
            using (var form = new Form())
            {
                var decoy = new TabControl { Name = "m_tabDecoy" };
                var real = new TabControl { Name = KeePassContract.DatabaseSettingsTabControlName };
                form.Controls.Add(decoy);
                form.Controls.Add(real);

                _injector.Attach(form);

                Assert.That(decoy.TabPages.Count, Is.Zero);
                Assert.That(real.TabPages.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_panel_that_throws_leaves_no_page_behind_and_is_reported()
        {
            var injector = new TabPageInjector(new ThrowingPanelFactory(), _log);
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(injector.Attach(form), Is.False);
                Assert.That(Dialogs.TabsOf(form).TabPages.Count, Is.EqualTo(1));
                Assert.That(injector.AttachedCount, Is.Zero);
                Assert.That(_log.Errors.Any(e => e.Error != null && e.Error.Message == ThrowingPanelFactory.Message), Is.True);
            }
        }

        [Test]
        public void A_panel_that_is_silently_missing_leaves_no_empty_page()
        {
            var injector = new TabPageInjector(new NullPanelFactory(), _log);
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(injector.Attach(form), Is.False);
                Assert.That(Dialogs.TabsOf(form).TabPages.Count, Is.EqualTo(1));
                Assert.That(_log.Errors, Is.Not.Empty);
            }
        }

        [Test]
        public void A_panel_the_page_would_not_take_is_let_go_of_with_the_page()
        {
            // Built, then docked and added, and a failure in either of the last two disposed the
            // page alone. The panel had never reached the page, so nothing disposed it, and
            // whatever it had subscribed to as it was built went on holding it.
            var panels = new RefusedPanelFactory();
            var injector = new TabPageInjector(panels, _log);

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(injector.Attach(form), Is.False);
                Assert.That(Dialogs.TabsOf(form).TabPages.Count, Is.EqualTo(1));

                Assert.That(panels.Last, Is.Not.Null, "no panel was built, so nothing was measured");
                Assert.That(panels.Last.IsDisposed, Is.True, "the refused panel was left to live on");
            }
        }

        [Test]
        public void Detaching_removes_the_page_and_leaves_the_hosts_own_tabs_in_order()
        {
            using (var form = Dialogs.WithNestedTabControl("General", "Security", "Advanced"))
            {
                _injector.Attach(form);
                _injector.Detach(form);

                var tabs = Dialogs.TabsOf(form);
                Assert.That(tabs.TabPages.Cast<TabPage>().Select(p => p.Text),
                    Is.EqualTo(new[] { "General", "Security", "Advanced" }));
                Assert.That(_injector.AttachedCount, Is.Zero);
            }
        }

        [Test]
        public void Detaching_disposes_the_page_and_the_panel_it_held()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(form);
                var page = PageOf(form);
                var panel = _panels.Last;

                _injector.Detach(form);

                Assert.That(page.IsDisposed, Is.True);
                Assert.That(panel.IsDisposed, Is.True);
            }
        }

        [Test]
        public void Detaching_a_form_that_was_never_attached_does_nothing()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                Assert.DoesNotThrow(() => _injector.Detach(form));
                Assert.DoesNotThrow(() => _injector.Detach(null));
                Assert.That(_log.Errors, Is.Empty);
            }
        }

        [Test]
        public void Attaching_again_after_a_detach_works()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(form);
                _injector.Detach(form);

                Assert.That(_injector.Attach(form), Is.True);
                Assert.That(PageOf(form), Is.Not.Null);
                Assert.That(_panels.CreatedCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void DetachAll_clears_every_dialog()
        {
            using (var first = Dialogs.WithNestedTabControl("General"))
            using (var second = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(first);
                _injector.Attach(second);

                _injector.DetachAll();

                Assert.That(_injector.AttachedCount, Is.Zero);
                Assert.That(PageOf(first), Is.Null);
                Assert.That(PageOf(second), Is.Null);
            }
        }

        [Test]
        public void DetachAll_on_nothing_is_harmless()
        {
            Assert.DoesNotThrow(() => _injector.DetachAll());
            Assert.That(_log.Errors, Is.Empty);
        }

        [Test]
        public void A_dialog_that_closes_without_being_announced_still_detaches()
        {
            // KeePass does announce closing windows, so this is defense in depth rather than the
            // expected path. It is the difference between a leak and a bug report.
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _injector.Attach(form);

                form.RaiseClosed();

                Assert.That(_injector.AttachedCount, Is.Zero);
            }
        }

        [Test]
        public void IsAttached_reports_what_the_dialog_actually_carries()
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                Assert.That(_injector.IsAttached(form), Is.False);
                _injector.Attach(form);
                Assert.That(_injector.IsAttached(form), Is.True);
                _injector.Detach(form);
                Assert.That(_injector.IsAttached(form), Is.False);
            }
        }

        [Test]
        public void The_page_carries_the_plugins_icon_and_gives_it_back()
        {
            // A tab strip shows pictures only if the whole control has an image list, and the
            // KeePass dialogs this lands on bring none. So one is made here, which makes every tab
            // on that dialog a little taller: WinForms has no way to put a picture on one tab and
            // leave the others alone. Taking it away again is what leaves the dialog as it was.
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                var tabs = Dialogs.TabsOf(form);
                Assert.That(tabs.ImageList, Is.Null, "the dialog brought its own");

                var injector = new TabPageInjector(
                    _panels,
                    _log,
                    KeePassContract.DatabaseSettingsTabControlName,
                    () => new System.Drawing.Bitmap(16, 16));

                Assert.That(injector.Attach(form), Is.True);

                Assert.That(tabs.ImageList, Is.Not.Null);
                Assert.That(tabs.ImageList.Images.Count, Is.EqualTo(1));
                Assert.That(PageOf(form).ImageIndex, Is.EqualTo(0));

                injector.DetachAll();

                Assert.That(tabs.ImageList, Is.Null, "the dialog is left as it was found");
            }
        }

        [Test]
        public void A_dialog_with_no_icon_to_offer_still_gets_its_tab()
        {
            // Which is every test of this, and every KeePass with no main window to read icons off.
            // A tab without a picture on it is a tab.
            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                var injector = new TabPageInjector(
                    _panels, _log, KeePassContract.DatabaseSettingsTabControlName, () => null);

                Assert.That(injector.Attach(form), Is.True);
                Assert.That(PageOf(form), Is.Not.Null);
                Assert.That(Dialogs.TabsOf(form).ImageList, Is.Null);
            }
        }

        [Test]
        public void A_dialog_that_brought_its_own_image_list_keeps_it_untouched()
        {
            // The list belongs to whoever made it, so nothing of ours goes into it: an entry
            // added here would still be there after the detach, and taking it out again would
            // renumber what that plugin had already put in. A tab without a picture is a tab.
            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var theirs = new ImageList())
            {
                var tabs = Dialogs.TabsOf(form);
                theirs.Images.Add(new System.Drawing.Bitmap(16, 16));
                tabs.ImageList = theirs;

                var injector = new TabPageInjector(
                    _panels,
                    _log,
                    KeePassContract.DatabaseSettingsTabControlName,
                    () => new System.Drawing.Bitmap(16, 16));

                Assert.That(injector.Attach(form), Is.True);

                Assert.That(tabs.ImageList, Is.SameAs(theirs), "the list is somebody else's");
                Assert.That(theirs.Images.Count, Is.EqualTo(1), "and holds only what it held");
                Assert.That(PageOf(form).ImageIndex, Is.EqualTo(-1));

                injector.DetachAll();

                Assert.That(tabs.ImageList, Is.SameAs(theirs), "left exactly as it was found");
                Assert.That(theirs.Images.Count, Is.EqualTo(1));
            }
        }

        private static TabPage PageOf(Form form)
        {
            return ControlTree.FindByName<TabPage>(form, GateTab.PageControlName);
        }

        /// <summary>A factory whose panel cannot be docked, which is one way a page refuses it.</summary>
        private sealed class RefusedPanelFactory : IGatePanelFactory
        {
            public Control Last { get; private set; }

            public Control CreatePanel(Form dialog)
            {
                Last = new PanelThatWillNotDock();
                return Last;
            }
        }

        private sealed class PanelThatWillNotDock : Panel
        {
            public override DockStyle Dock
            {
                get { return base.Dock; }
                set { throw new System.InvalidOperationException("this panel cannot be docked"); }
            }
        }
    }
}
