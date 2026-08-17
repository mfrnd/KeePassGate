using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
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
                new AuthorityPanelFactory(new FakeDatabaseSource(_database), new FakePrompt(false), _log, null),
                _log);
            _host = new GateHost(_windows, new TypeNameFormMatcher(typeof(TestDialogForm).FullName), _injector, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
        }

        [Test]
        public void A_settings_dialog_opening_gains_a_KPG_tab_showing_the_authority()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General", "Security"))
            {
                _windows.RaiseWindowAdded(form);

                var page = ControlTree.FindByName<TabPage>(form, GateTab.PageControlName);
                Assert.That(page, Is.Not.Null);
                Assert.That(page.Text, Is.EqualTo(GateTab.Text));

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

        [Test]
        public void A_failure_applying_the_tab_on_OK_is_counted_where_the_Tools_window_reads_it()
        {
            // H042: the handler applying what the tab held is one of KeePass's window event
            // handlers that catch everything, and it logged at the level that means a defect
            // without counting one, so the Tools window's incidents never showed it.
            var incidents = new GateIncidents(_log);
            var factory = new AuthorityPanelFactory(
                new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents);

            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                panel.CreateAuthority();
                _database.MarkThrows = new InvalidOperationException("invented for the test");

                form.DialogResult = DialogResult.OK;
                typeof(Form).GetMethod(
                        "OnFormClosed",
                        BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new[] { typeof(FormClosedEventArgs) },
                        null)
                    .Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });

                Assert.That(incidents.Count, Is.EqualTo(1), "the failure was not counted");
                Assert.That(incidents.Recent.Single(), Does.Contain("could not be written"));
            }
        }
    }
}
