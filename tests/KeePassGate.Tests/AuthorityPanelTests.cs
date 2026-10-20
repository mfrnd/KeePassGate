using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The tab as an operator uses it: three buttons, one of which is destructive and two of which
    /// revoke everything. Driven by pressing the buttons, because that is how the defects arrive.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthorityPanelTests
    {
        private FakeGateDatabase _database;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _database = new FakeGateDatabase();
            _log = new RecordingGateLog();
        }

        [Test]
        public void It_opens_saying_there_is_no_authority_yet()
        {
            using (var panel = NewPanel(true))
            {
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
                Assert.That(Button(panel, GateTab.CreateButtonName).Enabled, Is.True);
                Assert.That(Button(panel, GateTab.RecreateButtonName).Enabled, Is.False);
                Assert.That(Button(panel, GateTab.DeleteButtonName).Enabled, Is.False);
            }
        }

        [Test]
        public void It_says_which_button_makes_any_of_this_real()
        {
            using (var panel = NewPanel(true))
            {
                var notice = Label(panel, GateTab.NoticeLabelName).Text;

                Assert.That(notice, Is.EqualTo(AuthorityPanel.ImmediateNotice));
                Assert.That(notice, Does.Contain("OK"), "the operator is told what commits it");
                Assert.That(notice, Does.Contain("saved"),
                    "and that the file is not written until the database is saved");

                // One line, because the tab does not scroll and this sits under both pages, where
                // every line it takes is a line neither page has. `GateTabFitsTests` measures it.
                Assert.That(notice.Length, Is.LessThanOrEqualTo(60),
                    "the standing notice has to fit on one line of the tab");
            }
        }

        [Test]
        public void Creating_writes_an_authority_and_says_so()
        {
            using (var panel = NewPanel(true))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();

                using (var reading = new AuthorityService(_database.Store, null).Read())
                {
                    Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                }

                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.PresentHeadline));
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty,
                    "the table is what says it happened");
            }
        }

        [Test]
        public void Creating_tells_the_host_there_is_something_to_save()
        {
            using (var panel = NewPanel(true))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();

                Assert.That(_database.MarkedModifiedCount, Is.EqualTo(1));
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty,
                    "the table is what says it happened");

                // Where the operator is told to save is the standing notice, once, rather than
                // every answer repeating it.
                Assert.That(Label(panel, GateTab.NoticeLabelName).Text, Does.Contain("saved"));
            }
        }

        [Test]
        public void Creating_swaps_the_buttons_over()
        {
            using (var panel = NewPanel(true))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();

                Assert.That(Button(panel, GateTab.CreateButtonName).Enabled, Is.False);
                Assert.That(Button(panel, GateTab.RecreateButtonName).Enabled, Is.True);
                Assert.That(Button(panel, GateTab.DeleteButtonName).Enabled, Is.True);
            }
        }

        [Test]
        public void Recreating_asks_first_and_does_nothing_when_the_answer_is_no()
        {
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();
                var digest = Digest();

                Button(panel, GateTab.RecreateButtonName).PerformClick();

                Assert.That(prompt.Asked, Is.EqualTo(1));
                Assert.That(Digest(), Is.EqualTo(digest), "A refused confirmation must change nothing.");
            }
        }

        [Test]
        public void Recreating_replaces_the_authority_when_the_answer_is_yes()
        {
            using (var panel = NewPanel(true))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();
                var digest = Digest();

                Button(panel, GateTab.RecreateButtonName).PerformClick();

                Assert.That(Digest(), Is.Not.EqualTo(digest));
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty,
                    "the fingerprint on the table is different, which is the cue");
            }
        }

        [Test]
        public void The_question_before_recreating_states_what_it_costs()
        {
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();
                _database.Store.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
                _database.Store.Plant(AuthorityKeys.ClientPrefix + "bbb", "record");

                Button(panel, GateTab.RecreateButtonName).PerformClick();

                Assert.That(prompt.LastQuestion, Does.Contain("revokes every client"));
                Assert.That(prompt.LastQuestion, Does.Contain("2 clients are admitted"));
            }
        }

        [Test]
        public void Deleting_asks_first_and_then_empties_the_database()
        {
            var prompt = new FakePrompt(true);
            using (var panel = NewPanel(prompt))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();

                Button(panel, GateTab.DeleteButtonName).PerformClick();

                Assert.That(prompt.Asked, Is.EqualTo(1));
                Assert.That(_database.Store.Keys, Is.Empty);
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
            }
        }

        [Test]
        public void Deleting_does_nothing_when_the_answer_is_no()
        {
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();

                Button(panel, GateTab.DeleteButtonName).PerformClick();

                Assert.That(_database.Store.Keys.Count(), Is.EqualTo(1),
                    "the authority is one key, and refusing the question leaves it there");
            }
        }

        [Test]
        public void A_dialog_with_no_database_shows_that_and_offers_nothing()
        {
            using (var panel = new AuthorityPanel(null, new FakePrompt(true), _log, "1.0.0"))
            {
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.NoDatabaseHeadline));
                Assert.That(Button(panel, GateTab.CreateButtonName).Enabled, Is.False);
                Assert.That(Button(panel, GateTab.RecreateButtonName).Enabled, Is.False);
                Assert.That(Button(panel, GateTab.DeleteButtonName).Enabled, Is.False);
            }
        }

        [Test]
        public void A_closed_database_is_treated_as_no_database()
        {
            _database.IsOpen = false;

            using (var panel = NewPanel(true))
            {
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.NoDatabaseHeadline));
            }
        }

        [Test]
        public void A_database_that_refuses_the_write_reports_it_and_stays_upright()
        {
            _database.Store.ThrowOnWrite = new InvalidOperationException("the database said no");

            using (var panel = NewPanel(true))
            {
                Assert.DoesNotThrow(() => Button(panel, GateTab.CreateButtonName).PerformClick());

                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Does.Contain("the database said no"));
                Assert.That(_database.MarkedModifiedCount, Is.Zero);
            }
        }

        [Test]
        public void A_write_that_vanishes_is_reported_rather_than_celebrated()
        {
            _database.Store.SwallowWrites = true;

            using (var panel = NewPanel(true))
            {
                Button(panel, GateTab.CreateButtonName).PerformClick();

                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Does.Contain("could not be read back"));
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
                Assert.That(_database.MarkedModifiedCount, Is.Zero);
            }
        }

        [Test]
        public void Damage_is_shown_and_only_the_two_buttons_that_can_help_are_offered()
        {
            _database.Store.Plant(AuthorityKeys.Authority, "rubbish");

            using (var panel = NewPanel(true))
            {
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.DamagedHeadline));
                Assert.That(Button(panel, GateTab.CreateButtonName).Enabled, Is.False);
                Assert.That(Button(panel, GateTab.RecreateButtonName).Enabled, Is.True);
                Assert.That(Button(panel, GateTab.DeleteButtonName).Enabled, Is.True);
            }
        }

        [Test]
        public void The_panel_carries_the_names_ui_automation_looks_for()
        {
            using (var panel = NewPanel(true))
            {
                Assert.That(panel.Name, Is.EqualTo(GateTab.PanelControlName));
                Assert.That(Button(panel, GateTab.CreateButtonName).Text, Is.EqualTo(GateTab.CreateButtonText));
                Assert.That(Button(panel, GateTab.RecreateButtonName).Text, Is.EqualTo(GateTab.RecreateButtonText));
                Assert.That(Button(panel, GateTab.DeleteButtonName).Text, Is.EqualTo(GateTab.DeleteButtonText));
            }
        }

        [Test]
        public void It_shows_the_version_it_was_built_with()
        {
            using (var panel = NewPanel(true))
            {
                Assert.That(Label(panel, GateTab.VersionLabelName).Text, Is.EqualTo("Version 1.0.0"));
            }
        }

        private AuthorityPanel NewPanel(bool confirm)
        {
            return NewPanel(new FakePrompt(confirm));
        }

        private AuthorityPanel NewPanel(IOperatorPrompt prompt)
        {
            return new AuthorityPanel(_database, prompt, _log, "1.0.0");
        }

        private string Digest()
        {
            using (var reading = new AuthorityService(_database.Store, null).Read())
            {
                return reading.IsPresent ? reading.Material.AuthorityDigest : null;
            }
        }

        private static Label Label(Control panel, string name)
        {
            var label = ControlTree.FindByName<Label>(panel, name);
            Assert.That(label, Is.Not.Null, "no label called " + name);
            return label;
        }

        private static Button Button(Control panel, string name)
        {
            var button = ControlTree.FindByName<Button>(panel, name);
            Assert.That(button, Is.Not.Null, "no button called " + name);
            return button;
        }
    }
}
