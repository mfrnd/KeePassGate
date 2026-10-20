using System.Collections.Generic;
using System.Linq;
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
    /// The tab against the real dialog and a real database, rather than against a stand-in shaped
    /// like them. This is what catches a KeePass whose settings dialog builds its tabs
    /// differently, and a plugin that cannot find the database it is meant to be editing.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class DatabaseSettingsDialogTests
    {
        private TemporaryDatabase _database;
        private Form _form;
        private TabPageInjector _injector;
        private IReadOnlyList<string> _originalCaptions;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _form = KeePassDialogs.NewDatabaseSettingsForm();
            KeePassDialogs.InitializeFor(_form, _database.Database);

            _injector = new TabPageInjector(
                new AuthorityPanelFactory(
                    new DialogDatabases(NullGateLog.Instance),
                    new AlwaysConfirms(),
                    NullGateLog.Instance,
                    "1.2.3"),
                NullGateLog.Instance);

            _originalCaptions = Captions();
        }

        [TearDown]
        public void TearDown()
        {
            _injector.DetachAll();
            _form.Dispose();
            _database.Dispose();
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
        public void The_tab_finds_the_database_the_dialog_is_editing()
        {
            _injector.Attach(_form);

            Assert.That(Text(GateTab.StatusLabelName), Is.EqualTo(AuthorityDescription.AbsentHeadline),
                "A tab that cannot reach the database says so, and this one must not have to.");
            Assert.That(Text(GateTab.VersionLabelName), Is.EqualTo("Version 1.2.3"));
        }

        [Test]
        public void Creating_from_the_tab_writes_into_the_real_database_when_the_dialog_is_accepted()
        {
            _injector.Attach(_form);

            Panel().CreateAuthority();

            Assert.That(HoldsAnAuthority(), Is.False,
                "the authority reached the database before anybody pressed OK");

            // Read before the dialog is accepted, because accepting closes it and the page goes
            // with it: what the tab says is only there while the tab is.
            Assert.That(Text(GateTab.StatusLabelName), Is.EqualTo(AuthorityDescription.PresentHeadline),
                "the page has to show what OK would produce");

            KeePassDialogs.Accept(_form);

            Assert.That(HoldsAnAuthority(), Is.True);
        }

        [Test]
        public void Creating_from_the_tab_marks_the_database_as_needing_a_save()
        {
            _injector.Attach(_form);

            // Established rather than assumed. A fresh database does not reliably start unmodified
            // once other fixtures have loaded KeePass's own statics, and an assumption there turns
            // this into a test that quietly does not run whenever the whole tier does.
            _database.Database.Modified = false;

            Panel().CreateAuthority();

            Assert.That(_database.Database.Modified, Is.False, "nothing has landed yet");

            KeePassDialogs.Accept(_form);

            Assert.That(_database.Database.Modified, Is.True,
                "Without this KeePass has no reason to write the authority to disk.");
        }

        [Test]
        public void The_tab_shows_what_is_already_in_the_database()
        {
            new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance).Create();

            _injector.Attach(_form);

            Assert.That(Text(GateTab.StatusLabelName), Is.EqualTo(AuthorityDescription.PresentHeadline));
            Assert.That(Button(GateTab.CreateButtonName).Enabled, Is.False);
            Assert.That(Button(GateTab.DeleteButtonName).Enabled, Is.True);
        }

        [Test]
        public void Deleting_from_the_tab_empties_the_real_database()
        {
            new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance).Create();
            _injector.Attach(_form);

            Panel().DeleteAuthority();

            Assert.That(Text(GateTab.StatusLabelName), Is.EqualTo(AuthorityDescription.AbsentHeadline));
            Assert.That(HoldsAnAuthority(), Is.True,
                "gone from the database before anybody pressed OK");

            KeePassDialogs.Accept(_form);

            Assert.That(HoldsAnAuthority(), Is.False);
        }

        [Test]
        public void A_dialog_with_no_database_behind_it_says_so_rather_than_guessing()
        {
            using (var bare = KeePassDialogs.NewDatabaseSettingsForm())
            {
                _injector.Attach(bare);

                var status = ControlTree.FindByName<Label>(bare, GateTab.StatusLabelName);
                Assert.That(status.Text, Is.EqualTo(AuthorityDescription.NoDatabaseHeadline));

                _injector.Detach(bare);
            }
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

        /// <summary>Whether the real database holds a document for any authority at all.</summary>
        private bool HoldsAnAuthority()
        {
            return _database.Database.CustomData.Any(p => AuthorityKeys.FingerprintInAuthority(p.Key) != null);
        }

        private AuthorityPanel Panel()
        {
            var panel = ControlTree.FindByName<AuthorityPanel>(_form, GateTab.PanelControlName);
            Assert.That(panel, Is.Not.Null, "the tab holds no panel");
            return panel;
        }

        private string Text(string controlName)
        {
            var label = ControlTree.FindByName<Label>(_form, controlName);
            Assert.That(label, Is.Not.Null, "no label called " + controlName);
            return label.Text;
        }

        private Button Button(string controlName)
        {
            var button = ControlTree.FindByName<Button>(_form, controlName);
            Assert.That(button, Is.Not.Null, "no button called " + controlName);
            return button;
        }

        private IReadOnlyList<string> Captions()
        {
            var tabs = ControlTree.FindByName<TabControl>(_form, KeePassContract.DatabaseSettingsTabControlName);
            return tabs == null
                ? new List<string>()
                : tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToList();
        }

        private sealed class AlwaysConfirms : IOperatorPrompt
        {
            public bool Confirm(string title, string question)
            {
                return true;
            }
        }
    }
}
