using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// How a handler on a window of the plugin's own runs: what it did not name is recorded rather
    /// than let through to WinForms' own dialog.
    /// </summary>
    /// <remarks>
    /// `DESIGN.md` §141: these handlers are dispatched wherever KeePass pumps messages, its own save
    /// and lock among it, and a dialog that comes up there holds every lock off while the gate goes
    /// on serving reads. With nothing set to record to, as in every other fixture, nothing is
    /// caught.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class WindowHandlersTests
    {
        private RecordingGateLog _log;
        private GateIncidents _incidents;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
            _incidents = new GateIncidents(_log);
            WindowHandlers.Incidents = _incidents;
        }

        [TearDown]
        public void TearDown()
        {
            WindowHandlers.Incidents = null;
        }

        [Test]
        public void What_a_handler_did_not_name_is_recorded_rather_than_thrown()
        {
            Assert.DoesNotThrow(() => WindowHandlers.Run("Approve could not be carried out", Defect));

            Assert.That(_incidents.Recent, Is.EqualTo(new[] { "Approve could not be carried out: NullReferenceException" }));
            Assert.That(_log.Surprises.Single().Message, Does.Contain("Approve could not be carried out"));
        }

        [Test]
        public void With_nothing_to_record_to_it_arrives_as_it_was_thrown()
        {
            // As in every fixture that sets nothing, so that a defect a test drives is seen there.
            WindowHandlers.Incidents = null;

            Assert.Throws<NullReferenceException>(() => WindowHandlers.Run("Approve could not be carried out", Defect));
            Assert.That(_incidents.Count, Is.Zero);
        }

        [Test]
        public void A_handler_that_answers_answers_what_it_ran_or_what_was_given_for_a_failure()
        {
            Assert.That(WindowHandlers.Run("a key could not be handled", () => false, true), Is.False);
            Assert.That(WindowHandlers.Run("a key could not be handled", () => Defect<bool>(), true), Is.True,
                "a key on an approval dialog that could not be handled is taken, so that Enter presses nothing");
            Assert.That(_incidents.Count, Is.EqualTo(1));
        }

        [Test]
        public void Nothing_is_recorded_when_nothing_fails()
        {
            var ran = 0;
            WindowHandlers.Run("nothing could not be done", () => ran++);

            Assert.That(ran, Is.EqualTo(1));
            Assert.That(_incidents.Count, Is.Zero);
        }

        private static void Defect()
        {
            throw new NullReferenceException("a defect in a handler");
        }

        private static T Defect<T>()
        {
            throw new NullReferenceException("a defect in a handler");
        }
    }

    /// <summary>
    /// The routes a handler on the plugin's own windows is reached by, each driven to a defect on a
    /// real window: a click, a timer's tick, a change of a box and work posted for later.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class WindowHandlerRouteTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 3);

        private readonly List<Exception> _loop = new List<Exception>();
        private GateIncidents _incidents;

        [SetUp]
        public void SetUp()
        {
            _loop.Clear();
            _incidents = new GateIncidents(new RecordingGateLog());
            WindowHandlers.Incidents = _incidents;
            Application.ThreadException += OnThreadException;
        }

        [TearDown]
        public void TearDown()
        {
            Application.ThreadException -= OnThreadException;
            WindowHandlers.Incidents = null;
        }

        [Test]
        public void A_defect_behind_OK_on_the_plugins_window_is_recorded_and_the_window_stays()
        {
            // S211: what OK does runs inside the window's own modal loop, where the catch around
            // showing the window never sees it.
            using (var window = new GateStatusForm(new ListeningGate(), null, port => throw new NullReferenceException("a defect"), 51776))
            {
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-4000, -4000);
                window.ShowInTaskbar = false;
                window.Show();
                OffScreenForm.Pump();

                window.ChosenPort = 51777;
                ControlTree.FindByName<Button>(window, GateTab.StatusOkButtonName).PerformClick();
                OffScreenForm.Pump();

                Assert.That(_loop, Is.Empty);
                Assert.That(_incidents.Recent, Has.Exactly(1).StartsWith("OK on the plugin's window could not be carried out"));
                Assert.That(window.IsDisposed, Is.False);
            }
        }

        [Test]
        public void A_defect_in_a_countdown_tick_is_recorded()
        {
            // A timer ticks wherever KeePass pumps, its own save among it, with no press to have
            // caused it. The renewal dialog asks the clock on every tick.
            var broken = false;
            Func<DateTime> clock = () => broken ? throw new NullReferenceException("a defect in the clock") : Today;

            using (var dialog = new RenewalApprovalForm(Request(), TimeSpan.FromMinutes(5), null, clock, null))
            {
                broken = true;

                var watch = Stopwatch.StartNew();
                while (_incidents.Count == 0 && watch.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }

                Assert.That(_loop, Is.Empty);
                Assert.That(_incidents.Recent[0], Does.StartWith("the renewal dialog's countdown could not be moved on"));
                Assert.That(dialog.IsDisposed, Is.False, "and the dialog is left standing");
            }
        }

        [Test]
        public void A_defect_drawing_the_box_pressed_is_recorded_and_the_tab_left_standing()
        {
            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                GrantSheet.Of(Rules.Node(false, ("reader", "R")), null, new[] { "reader", "ops" }, null), GrantTab.GroupScope, null, null, null))
            {
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                var armed = true;
                grid.RowsAdded += (sender, e) =>
                {
                    if (armed)
                    {
                        throw new NullReferenceException("a defect while the rules were drawn");
                    }
                };

                // The box's own click, as a press raises it: the box takes no mark of its own.
                typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName), new object[] { EventArgs.Empty });
                armed = false;

                Assert.That(_loop, Is.Empty);
                Assert.That(_incidents.Recent, Has.Exactly(1).StartsWith("the box could not be pressed"));
                Assert.That(form.IsDisposed, Is.False);
            }
        }

        [Test]
        public void A_defect_writing_at_the_close_of_a_node_s_dialog_is_recorded_and_the_close_goes_on()
        {
            var rules = new FakeNodeRules { Thrown = new NullReferenceException("a defect while the rules were written") };
            var prompt = new FakePrompt(true);

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                window.Show();
                var sheet = GrantSheet.Of(NodeRule.None, null, new[] { "reader" }, null);
                var factory = new GrantPanelFactory(
                    new FakeNodeDialogs(new NodeTab(sheet, GrantTab.GroupScope, null, rules)),
                    null,
                    null,
                    prompt,
                    window,
                    _incidents);

                using (var panel = (GrantPanel)factory.CreatePanel(dialog))
                {
                    dialog.Show();
                    Assert.That(panel.AddGrant("reader"), Is.Null);
                }

                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                OffScreenForm.Pump();

                Assert.That(_loop, Is.Empty);
                Assert.That(_incidents.Recent, Has.Exactly(1).StartsWith("the OK of the " + GateName.OnATab + " tab of a group or an entry did not run to its end"));
                Assert.That(_incidents.Recent.Single(), Does.EndWith(nameof(NullReferenceException)), "the defect was in the write");
                Assert.That(dialog.IsDisposed, Is.True, "the dialog closed");
                Assert.That(prompt.Told, Is.EqualTo(1), "a narrowing the defect kept from the store is seen to be missing");
                Assert.That(prompt.LastNews, Is.EqualTo(GrantTab.NotKeptDefect));
            }
        }

        private static RenewalRequest Request()
        {
            return new RenewalRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "agent-fictitious",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(Today.AddDays(40).AddHours(12), TimeSpan.Zero),
                new[] { "reader" },
                Array.Empty<string>(),
                "127.0.0.1:60001",
                null);
        }

        private void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            _loop.Add(e.Exception);
        }

        /// <summary>A gate listening on a port and on nothing else.</summary>
        private sealed class ListeningGate : IGateStatus
        {
            public bool IsListening
            {
                get { return true; }
            }

            public int Port
            {
                get { return 45678; }
            }

            public string Refusal
            {
                get { return null; }
            }
        }
    }
}
