using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The approval dialog, built and pressed without ever putting a window on the screen.
    /// </summary>
    /// <remarks>
    /// What is being tested is the part that decides: which button leads to which answer, what the
    /// dialog is showing when it does, and the fact that nothing at all leads to an approval by
    /// default. The end to end tier presses these buttons for real.
    /// </remarks>
    // Alone, because some of these show the dialog and ask where the focus went, and a window
    // another fixture shows meanwhile takes it.
    [TestFixture]
    [NonParallelizable]
    [Desktop]
    [Apartment(ApartmentState.STA)]
    public sealed class PairingApprovalFormTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        [Test]
        public void The_two_answers_sit_on_the_same_line()
        {
            // A flow panel places each control at the top of its row plus that control's own top
            // margin, so a button left with the default margin sits below one that was given none.
            // It fits, it works, and it is the first thing anybody notices about the dialog.
            using (var dialog = New("ops"))
            {
                dialog.CreateControl();
                dialog.PerformLayout();

                var refuse = ControlTree.FindByName<Button>(dialog, GateTab.RefuseButtonName);
                var approve = ControlTree.FindByName<Button>(dialog, GateTab.ApproveButtonName);

                Assert.That(refuse, Is.Not.Null);
                Assert.That(approve, Is.Not.Null);
                Assert.That(approve.Top, Is.EqualTo(refuse.Top));
                Assert.That(approve.Height, Is.EqualTo(refuse.Height));
            }
        }

        [Test]
        public void A_dialog_nobody_touches_has_timed_out_rather_than_agreed()
        {
            using (var dialog = New())
            {
                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.TimedOut));
            }
        }

        [Test]
        public void Approving_approves_what_is_still_checked()
        {
            using (var dialog = New("ops", "admin"))
            {
                dialog.Approve();

                var answer = dialog.Answer();

                Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Approved));
                Assert.That(answer.Profiles, Is.EqualTo(new[] { "ops", "admin" }));
            }
        }

        [Test]
        public void Unchecking_something_takes_it_away_from_what_is_approved()
        {
            using (var dialog = New("ops", "admin"))
            {
                Profiles(dialog).SetItemChecked(1, false);
                dialog.Approve();

                Assert.That(dialog.Answer().Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void Approving_with_everything_unchecked_is_an_admission_that_reaches_nothing()
        {
            using (var dialog = New("ops"))
            {
                Profiles(dialog).SetItemChecked(0, false);
                dialog.Approve();

                var answer = dialog.Answer();

                Assert.That(answer.IsApproved, Is.True);
                Assert.That(answer.Profiles, Is.Empty);
            }
        }

        [Test]
        public void Refusing_refuses()
        {
            using (var dialog = New("ops"))
            {
                dialog.Refuse();

                Assert.That(dialog.Answer().Decision, Is.EqualTo(PairingDecision.Refused));
            }
        }

        [Test]
        public void Escape_and_Enter_both_reach_the_refusal()
        {
            using (var dialog = New("ops"))
            {
                var refuse = Button(dialog, GateTab.RefuseButtonName);

                Assert.That(dialog.CancelButton, Is.SameAs(refuse), "Escape must not approve anything");
                Assert.That(dialog.AcceptButton, Is.SameAs(refuse), "nor must a stray Enter");
            }
        }

        [Test]
        public void What_is_asked_for_arrives_checked_so_that_agreeing_is_still_a_decision()
        {
            using (var dialog = New("ops", "readonly"))
            {
                var profiles = Profiles(dialog);

                Assert.That(profiles.Items.Count, Is.EqualTo(2));
                Assert.That(profiles.CheckedItems.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void The_dialog_says_which_database_and_which_client()
        {
            // A table, because every one of these is a value the operator is checking against what
            // they expect rather than a sentence they are reading.
            var facts = PairingApprovalForm.Facts(Request("agent-fictitious", new[] { "ops" }));

            Assert.That(Named(facts, "Database"), Is.EqualTo("Invented Vault"));
            Assert.That(Named(facts, "Fingerprint"), Is.EqualTo("aaaabbbbccccdddd"));
            Assert.That(Named(facts, "Asking to be called"), Is.EqualTo("agent-fictitious"));
            Assert.That(Named(facts, "Invitation id"), Is.EqualTo("abcdef01"));
            Assert.That(Named(facts, "Connected from"), Is.EqualTo("127.0.0.1:60001"));

            foreach (var fact in facts)
            {
                Assert.That(fact.Value, Does.Not.Contain(fact.Name), "the value carries no label of its own");
            }
        }

        [Test]
        public void What_approving_costs_is_said_where_it_can_be_read()
        {
            using (var dialog = New("ops"))
            {
                Assert.That(Texts(dialog), Does.Contain(PairingApprovalForm.Consequence));
                Assert.That(PairingApprovalForm.Consequence, Does.Contain("revoked later"));

                // No second person, which `CONVENTIONS.md` requires of every string an operator
                // reads. This one said "You can revoke it later".
                foreach (var pronoun in new[] { "You ", "you ", "your ", "Your " })
                {
                    Assert.That(PairingApprovalForm.Consequence, Does.Not.Contain(pronoun), pronoun);
                }
            }
        }

        [Test]
        public void The_list_says_what_it_is_a_list_of()
        {
            // "It is asking for these" made the reader look back up to find out what "these" meant,
            // and what it meant was the whole decision.
            using (var dialog = New("ops"))
            {
                Assert.That(Texts(dialog), Does.Contain(PairingApprovalForm.ProfilesAsked));
                Assert.That(PairingApprovalForm.ProfilesAsked, Does.Contain("profiles"));
                Assert.That(PairingApprovalForm.ProfilesAsked, Does.Contain("Uncheck"));
            }
        }

        [Test]
        public void The_invitation_row_names_the_invitation_rather_than_the_code()
        {
            var request = new PairingRequest(
                "aaaabbbbccccdddd", "abcdef01", "agent-fictitious", Array.Empty<string>(), null);

            Assert.That(
                Named(PairingApprovalForm.Facts(request), "Invitation id"), Is.EqualTo("abcdef01"));
        }

        [Test]
        public void It_says_when_nothing_at_all_is_being_asked_for()
        {
            using (var dialog = New())
            {
                Assert.That(Texts(dialog), Does.Contain("asking for no profiles"));
            }
        }

        private static string Named(IReadOnlyList<Fact> facts, string name)
        {
            foreach (var fact in facts)
            {
                if (fact.Name == name)
                {
                    return fact.Value;
                }
            }

            Assert.Fail("no fact called " + name);
            return null;
        }

        [Test]
        public void It_says_that_it_times_out_and_what_that_costs()
        {
            using (var dialog = New("ops"))
            {
                var shown = Texts(dialog);

                Assert.That(shown, Does.Contain("times out"),
                    "a dialog that answers for the operator has to say that it will");
                Assert.That(shown, Does.Contain("nothing is issued"),
                    "and which way it answers, because the two are not the same news");
            }
        }

        [Test]
        public void A_request_is_needed_to_ask_about()
        {
            Assert.Throws<ArgumentNullException>(() => new PairingApprovalForm(null, Timeout));
            Assert.Throws<ArgumentNullException>(() => PairingApprovalForm.Facts(null));
        }

        [Test]
        public void The_dialog_names_the_database_rather_than_only_its_fingerprint()
        {
            // Sixteen hexadecimal characters is not something anybody recognizes. The fingerprint
            // stays, because it is what actually identifies a database and two can share a name.
            var facts = PairingApprovalForm.Facts(Request("agent-fictitious", new[] { "ops" }));

            Assert.That(Named(facts, "Database"), Is.EqualTo("Invented Vault"));
            Assert.That(Named(facts, "Fingerprint"), Is.EqualTo("aaaabbbbccccdddd"));
        }

        [Test]
        public void A_database_with_no_name_is_still_named_by_its_fingerprint()
        {
            var facts = PairingApprovalForm.Facts(new PairingRequest(
                "aaaabbbbccccdddd", "abcdef01", "agent-fictitious", Array.Empty<string>(), null));

            Assert.That(Named(facts, "Database"), Is.EqualTo("aaaabbbbccccdddd"));
        }

        [Test]
        public void It_says_that_approving_saves_the_database()
        {
            // Which is the one thing on this dialog that touches the file. An operator who does not
            // expect it finds a saved database and cannot tell what else went with it.
            Assert.That(PairingApprovalForm.Consequence, Does.Contain("saves this database"));
            Assert.That(PairingApprovalForm.Consequence, Does.Contain("before the client is told"));
        }

        private static PairingApprovalForm New(params string[] profiles)
        {
            return new PairingApprovalForm(Request("agent-fictitious", profiles), Timeout);
        }

        private static PairingRequest Request(string name, string[] profiles)
        {
            return new PairingRequest(
                "aaaabbbbccccdddd",
                "Invented Vault",
                "abcdef01",
                name,
                profiles,
                "127.0.0.1:60001");
        }

        private static Button Button(Control form, string name)
        {
            var button = ControlTree.FindByName<Button>(form, name);
            Assert.That(button, Is.Not.Null, "no button called " + name);
            return button;
        }

        private static CheckedListBox Profiles(Control form)
        {
            var list = ControlTree.FindByName<CheckedListBox>(form, GateTab.ApprovalProfilesListName);
            Assert.That(list, Is.Not.Null);
            return list;
        }

        private static string Texts(Control form)
        {
            var text = new System.Text.StringBuilder();
            foreach (var control in ControlTree.Descendants(form))
            {
                var label = control as Label;
                if (label != null)
                {
                    text.AppendLine(label.Text);
                }
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Putting the dialog on the screen from a connection thread, and what happens when it cannot
    /// be put there at all.
    /// </summary>
    [TestFixture]
    public sealed class PairingApprovalPromptTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>How long a test waits on another thread before calling it a failure.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
        }

        [Test]
        public void A_user_interface_that_never_runs_the_work_is_a_timeout_rather_than_a_wait()
        {
            var prompt = new PairingApprovalPrompt(new FakeUiThread { Runs = false }, _log);

            var answer = prompt.Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.TimedOut));
            Assert.That(_log.Warnings, Is.Not.Empty);
        }

        [TestCase(PairingDecision.Refused)]
        [TestCase(PairingDecision.Approved)]
        public void An_answer_given_after_the_wait_ran_out_is_still_the_answer(PairingDecision decided)
        {
            // H006: `IUiThread.Run` answers false for work that began too late to finish inside the
            // wait and was then waited out. The dialog's own countdown starts when it is built, so
            // on a thread that reached it late the operator's answer comes after the prompt's wait,
            // and it used to be thrown away: a refusal became a timeout that spends nothing, and an
            // approval issued nothing.
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true }, _log)
            {
                Dialog = (request, timeout) => decided == PairingDecision.Refused
                    ? PairingApproval.Refused()
                    : PairingApproval.Approved(new[] { "ops" })
            };

            var answer = prompt.Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(decided));
        }

        [Test]
        public void A_dialog_that_never_answered_is_a_timeout_however_late_it_ran()
        {
            var prompt = new PairingApprovalPrompt(new FakeUiThread { RunsLate = true }, _log)
            {
                Dialog = (request, timeout) => null
            };

            Assert.That(prompt.Ask(Request(), Timeout).Decision, Is.EqualTo(PairingDecision.TimedOut));
        }

        [Test]
        public void It_waits_longer_than_the_dialog_does_so_the_dialog_is_what_gives_up()
        {
            var ui = new FakeUiThread { Runs = false };
            new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout);

            Assert.That(ui.LastTimeout, Is.EqualTo(Timeout + PairingApprovalPrompt.Grace));
        }

        [Test]
        public void One_pairing_is_put_to_the_operator_at_a_time()
        {
            // `PAIRING.md` §303. Two connections can be pairing at once and each marshals its
            // dialog onto the one thread that has the windows. The second would open inside the
            // first one's modal loop, which disables every other window on that thread: the request
            // the operator is reading goes gray, cannot be answered, and times out in front of
            // them. So the second waits for the screen, gives up on it inside the time its own
            // operator was going to be given, and spends nothing.
            var ui = new HeldUiThread();
            var prompt = new PairingApprovalPrompt(ui, _log);
            var first = new Thread(() => prompt.Ask(Request(), Timeout));

            first.IsBackground = true;
            first.Start();

            try
            {
                Assert.That(ui.Reached(Patience), Is.True, "the first pairing has the screen");

                var second = prompt.Ask(Request(), TimeSpan.FromMilliseconds(50));

                Assert.That(second.Decision, Is.EqualTo(PairingDecision.Unavailable));
                Assert.That(second.Reason, Is.Not.Empty, "the log is told which pairing this was");
                Assert.That(ui.Asked, Is.EqualTo(1),
                    "a second dialog behind the first one is exactly what must not be raised");
                Assert.That(_log.Warnings, Is.Not.Empty);
            }
            finally
            {
                ui.Release();
            }

            Assert.That(first.Join(Patience), Is.True, "the first pairing finished with the screen");
        }

        [Test]
        public void The_screen_goes_back_to_whoever_asks_next()
        {
            // The other half of the gate: a pairing that has finished must leave the lock behind
            // it, or the first request of the day is the last one anybody is asked about.
            var ui = new HeldUiThread();
            var prompt = new PairingApprovalPrompt(ui, _log);

            ui.Release();
            prompt.Ask(Request(), Timeout);
            prompt.Ask(Request(), Timeout);

            Assert.That(ui.Asked, Is.EqualTo(2));
        }

        [Test]
        public void A_user_interface_that_throws_is_reported_and_approves_nothing()
        {
            var ui = new FakeUiThread { Throws = new InvalidOperationException("no handle") };

            var answer = new PairingApprovalPrompt(ui, _log).Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void There_is_nothing_to_ask_about_without_a_request()
        {
            var prompt = new PairingApprovalPrompt(CurrentThread.Instance, _log);

            Assert.Throws<ArgumentNullException>(() => prompt.Ask(null, Timeout));
        }

        [Test]
        public void A_plugin_with_no_screen_approves_nothing_and_says_why()
        {
            var answer = NoPairingApproval.Instance.Ask(Request(), Timeout);

            Assert.That(answer.Decision, Is.EqualTo(PairingDecision.Unavailable));
            Assert.That(answer.Reason, Is.Not.Empty);
        }

        private static PairingRequest Request()
        {
            return new PairingRequest("db", "abcdef01", "agent-fictitious", Array.Empty<string>(), null);
        }

        /// <summary>
        /// A user interface thread that stays inside the work until a test lets it out, which is
        /// what an operator reading a dialog looks like from a connection thread.
        /// </summary>
        /// <remarks>
        /// The work itself is never run, because running it puts a real modal dialog on a real
        /// screen and nothing would ever answer it. What is under test is the gate in front of the
        /// dialog rather than the dialog behind it.
        ///
        /// Every wait is bounded, including the one inside `Run`, so a test that fails fails with
        /// an assertion rather than by hanging the suite.
        /// </remarks>
        private sealed class HeldUiThread : IUiThread
        {
            private readonly object _gate = new object();

            private int _asked;
            private bool _reached;
            private bool _released;

            public int Asked
            {
                get
                {
                    lock (_gate)
                    {
                        return _asked;
                    }
                }
            }

            /// <summary>Waits until the work has been reached, which is the screen being taken.</summary>
            public bool Reached(TimeSpan patience)
            {
                lock (_gate)
                {
                    var until = DateTime.UtcNow + patience;
                    while (!_reached && DateTime.UtcNow < until)
                    {
                        Monitor.Wait(_gate, TimeSpan.FromMilliseconds(20));
                    }

                    return _reached;
                }
            }

            /// <summary>Lets go of the screen, and of anybody waiting for it afterwards.</summary>
            public void Release()
            {
                lock (_gate)
                {
                    _released = true;
                    Monitor.PulseAll(_gate);
                }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                lock (_gate)
                {
                    _asked++;
                    _reached = true;
                    Monitor.PulseAll(_gate);

                    var until = DateTime.UtcNow + timeout;
                    while (!_released && DateTime.UtcNow < until)
                    {
                        Monitor.Wait(_gate, TimeSpan.FromMilliseconds(20));
                    }
                }

                return false;
            }
        }
    }
}
