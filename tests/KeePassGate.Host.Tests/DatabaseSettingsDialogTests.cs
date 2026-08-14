using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The injector against the real dialog rather than against a stand-in shaped like it. This
    /// is what catches a KeePass whose settings dialog builds its tabs differently.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class DatabaseSettingsDialogTests
    {
        private Form _form;
        private TabPageInjector _injector;
        private IReadOnlyList<string> _originalCaptions;

        [SetUp]
        public void SetUp()
        {
            _form = KeePassDialogs.NewDatabaseSettingsForm();
            _injector = new TabPageInjector(new GateStatusPanelFactory(), NullGateLog.Instance);
            _originalCaptions = Captions();
        }

        [TearDown]
        public void TearDown()
        {
            _injector.DetachAll();
            _form.Dispose();
        }

        [Test]
        public void KeePass_brings_its_own_tabs_and_we_add_exactly_one_after_them()
        {
            Assume.That(_originalCaptions, Is.Not.Empty);

            Assert.That(_injector.Attach(_form), Is.True);

            Assert.That(Captions(), Is.EqualTo(_originalCaptions.Concat(new[] { GateTab.Text }).ToList()));
        }

        [Test]
        public void The_tab_lands_on_the_tab_control_KeePass_names_m_tabMain()
        {
            _injector.Attach(_form);

            var tabs = ControlTree.FindByName<TabControl>(_form, KeePassContract.DatabaseSettingsTabControlName);
            Assert.That(tabs.TabPages.Cast<TabPage>().Any(p => p.Name == GateTab.PageControlName), Is.True);
        }

        [Test]
        public void The_tab_shows_the_plugin_version_and_what_it_can_do()
        {
            _injector.Attach(_form);

            var version = ControlTree.FindByName<Label>(_form, GateTab.VersionLabelName);
            var status = ControlTree.FindByName<Label>(_form, GateTab.StatusLabelName);

            Assert.That(version.Text, Is.EqualTo("Version " + GateVersion.Current));
            Assert.That(status.Text, Is.EqualTo(GateStatusPanelFactory.DefaultStatus));
        }

        [Test]
        public void Attaching_does_not_disturb_which_tab_KeePass_had_selected()
        {
            var tabs = ControlTree.FindByName<TabControl>(_form, KeePassContract.DatabaseSettingsTabControlName);
            var selected = tabs.SelectedTab;

            _injector.Attach(_form);

            Assert.That(tabs.SelectedTab, Is.SameAs(selected));
        }

        [Test]
        public void Detaching_leaves_the_dialog_exactly_as_KeePass_built_it()
        {
            _injector.Attach(_form);

            _injector.Detach(_form);

            Assert.That(Captions(), Is.EqualTo(_originalCaptions));
            Assert.That(ControlTree.FindByName<TabPage>(_form, GateTab.PageControlName), Is.Null);
        }

        [Test]
        public void The_dialog_is_only_ever_decorated_once()
        {
            _injector.Attach(_form);
            _injector.Attach(_form);

            Assert.That(Captions().Count(c => c == GateTab.Text), Is.EqualTo(1));
        }

        private IReadOnlyList<string> Captions()
        {
            var tabs = ControlTree.FindByName<TabControl>(_form, KeePassContract.DatabaseSettingsTabControlName);
            return tabs == null
                ? new List<string>()
                : tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToList();
        }
    }
}
