using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The whole plugin except the two classes that cannot exist without KeePass: real host, real
    /// injector, real panel, with only the window notifications faked. This is the closest a
    /// hermetic test gets to what a user sees, and it fails if any of the pieces stop fitting.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateWiringTests
    {
        private FakeWindowEventSource _windows;
        private RecordingGateLog _log;
        private TabPageInjector _injector;
        private FakeGateDatabase _database;
        private GateHost _host;

        [SetUp]
        public void SetUp()
        {
            _windows = new FakeWindowEventSource();
            _log = new RecordingGateLog();
            _database = new FakeGateDatabase();
            _injector = new TabPageInjector(
                new AuthorityPanelFactory(new FakeDatabaseSource(_database), new FakePrompt(false), _log, "9.9.9"),
                _log);
            _host = new GateHost(_windows, new TypeNameFormMatcher(typeof(TestDialogForm).FullName), _injector, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
        }

        [Test]
        public void A_settings_dialog_opening_gains_a_KPG_tab_showing_the_version()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General", "Security"))
            {
                _windows.RaiseWindowAdded(form);

                var page = ControlTree.FindByName<TabPage>(form, GateTab.PageControlName);
                Assert.That(page, Is.Not.Null);
                Assert.That(page.Text, Is.EqualTo(GateTab.Text));

                var version = ControlTree.FindByName<Label>(form, GateTab.VersionLabelName);
                Assert.That(version, Is.Not.Null);
                Assert.That(version.Text, Is.EqualTo("Version 9.9.9"));

                var status = ControlTree.FindByName<Label>(form, GateTab.StatusLabelName);
                Assert.That(status.Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
            }
        }

        [Test]
        public void The_dialog_closing_takes_the_tab_with_it()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _windows.RaiseWindowAdded(form);
                _windows.RaiseWindowRemoved(form);

                Assert.That(ControlTree.FindByName<TabPage>(form, GateTab.PageControlName), Is.Null);
                Assert.That(Dialogs.TabsOf(form).TabPages.Cast<TabPage>().Select(p => p.Text),
                    Is.EqualTo(new[] { "General" }));
            }
        }

        [Test]
        public void Stopping_with_a_dialog_still_open_takes_the_tab_back()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _windows.RaiseWindowAdded(form);

                _host.Stop();

                Assert.That(ControlTree.FindByName<TabPage>(form, GateTab.PageControlName), Is.Null);
                Assert.That(_injector.AttachedCount, Is.Zero);
            }
        }

        [Test]
        public void Nothing_in_the_whole_run_was_reported_as_a_failure()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _windows.RaiseWindowAdded(form);
                _windows.RaiseWindowRemoved(form);
            }

            _host.Stop();

            Assert.That(_log.Errors, Is.Empty);
            Assert.That(_log.Warnings, Is.Empty);
        }
    }
}
