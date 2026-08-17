using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The window the Tools menu opens: what the plugin is, and whether it is serving.
    /// </summary>
    /// <remarks>
    /// Asked of `Facts` rather than of the window wherever it can be, because the wording is what
    /// matters and a form is a poor place to test wording through. The window itself is checked
    /// only for what only it can answer: that the facts reach the screen.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateStatusFormTests
    {
        [Test]
        public void It_says_where_the_gate_is_listening()
        {
            Assert.That(Named(GateStatusForm.Facts(Listening(45678), null), "Listening"),
                Is.EqualTo("127.0.0.1:45678"));
        }

        [Test]
        public void A_gate_that_is_not_listening_says_what_that_costs()
        {
            var where = Named(GateStatusForm.Facts(new Gate(false, 0), null), "Listening");

            Assert.That(where, Does.Contain("no"));
            Assert.That(where, Does.Contain("nothing can reach"));
        }

        [Test]
        public void A_gate_nobody_has_asked_about_says_it_does_not_know()
        {
            // Rather than reporting a port of zero, which reads as an answer.
            Assert.That(Named(GateStatusForm.Facts(null, null), "Listening"), Is.EqualTo("not known"));
        }

        [Test]
        public void Incidents_are_shown_only_when_there_have_been_some()
        {
            var quiet = GateStatusForm.Facts(Listening(1), new GateIncidents(NullGateLog.Instance));

            Assert.That(Names(quiet), Does.Not.Contain("Incidents"),
                "a row saying nothing went wrong is a row that teaches nobody to read the others");

            var incidents = new GateIncidents(NullGateLog.Instance);
            incidents.Record("the connection thread", new System.InvalidOperationException("invented"));

            Assert.That(Names(GateStatusForm.Facts(Listening(1), incidents)), Does.Contain("Incidents"));
        }

        [Test]
        public void Every_fact_is_a_name_and_a_value()
        {
            foreach (var fact in GateStatusForm.Facts(Listening(45678), null))
            {
                Assert.That(fact.Name, Is.Not.Empty);
                Assert.That(fact.Value, Is.Not.Empty);
                Assert.That(fact.Name, Does.Not.EndWith(":"), "the layout separates them");
            }
        }

        [Test]
        public void The_window_puts_them_on_the_screen()
        {
            using (var window = new GateStatusForm(Listening(45678), null))
            {
                var table = ControlTree.FindByName<TableLayoutPanel>(window, GateTab.StatusFactsName);
                Assert.That(table, Is.Not.Null);

                var shown = new List<string>();
                foreach (Control child in table.Controls)
                {
                    shown.Add(child.Text);
                }

                Assert.That(shown, Does.Contain("Listening"));
                Assert.That(shown, Does.Contain("127.0.0.1:45678"));
                Assert.That(shown, Does.Not.Contain("Version"),
                    "KeePass's own plugins dialog owns that, and owns it for every plugin at once");
            }
        }

        [Test]
        public void It_says_what_to_do_about_what_it_says()
        {
            using (var serving = new GateStatusForm(Listening(45678), null))
            {
                Assert.That(Advice(serving), Does.Contain("authority"),
                    "a listening gate serves nothing until a database has one");
            }

            using (var silent = new GateStatusForm(new Gate(false, 0), null))
            {
                Assert.That(Advice(silent), Does.Contain("log"));
            }
        }

        [Test]
        public void The_box_starts_on_the_port_the_gate_is_actually_using()
        {
            // Rather than on what was chosen, because those differ exactly when something went
            // wrong, and the one worth showing is the one clients can reach.
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 51776))
            {
                Assert.That(window.ChosenPort, Is.EqualTo(45678));
            }
        }

        [Test]
        public void A_gate_that_is_not_listening_starts_on_what_was_chosen()
        {
            using (var window = new GateStatusForm(new Gate(false, 0), null, Takes, 51999))
            {
                Assert.That(window.ChosenPort, Is.EqualTo(51999));
            }
        }

        [Test]
        public void A_port_is_written_down_and_says_it_waits_for_the_next_start()
        {
            var taken = 0;

            using (var window = new GateStatusForm(Listening(45678), null, port =>
            {
                taken = port;
                return null;
            }, 0))
            {
                window.ChosenPort = 52000;

                var said = window.Keep();

                Assert.That(taken, Is.EqualTo(52000));
                Assert.That(said, Does.Contain("52000"));
                Assert.That(said, Does.Contain("next time KeePass starts"),
                    "nothing rebinds while the operator watches, so the window has to say when it will");
                Assert.That(said, Does.Contain("paired"), "somebody has to tell the clients");
            }
        }

        [Test]
        public void Keeping_the_port_the_gate_is_already_on_promises_no_restart()
        {
            // Pressing OK without touching the box is the commonest thing that happens to this
            // window, and a sentence about a restart there would be asking for one that buys
            // nothing.
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 0))
            {
                var said = window.Keep();

                Assert.That(said, Does.Contain("already listening"));
                Assert.That(said, Does.Not.Contain("starts"));
            }
        }

        [Test]
        public void A_port_that_could_not_be_written_down_says_so_and_names_the_reason()
        {
            using (var window = new GateStatusForm(Listening(45678), null,
                port => "the configuration is read only", 0))
            {
                window.ChosenPort = 52000;

                var said = window.Keep();

                Assert.That(said, Does.Contain("not remembered"));
                Assert.That(said, Does.Contain("the configuration is read only"));
            }
        }

        [Test]
        public void An_untouched_OK_does_not_write_over_a_port_that_is_already_waiting()
        {
            // **The state every successful change leaves behind, and it lasts the session.** The
            // port is written down and the socket is not rebound, so from then on the box shows the
            // socket while the setting says something else. An operator who reopens the window to
            // check the change, and answers the dialog the way a dialog is answered, was writing
            // the old port back over the new one. The result line then told them the gate was
            // already listening there, which was true and was not what they had asked about.
            var written = new List<int>();

            using (var window = new GateStatusForm(
                Listening(51776), null, port => { written.Add(port); return null; }, 52000))
            {
                Assume.That(window.ChosenPort, Is.EqualTo(51776), "the socket, not the choice");

                var said = window.Keep();

                Assert.That(written, Is.Empty, "nothing was typed here, so nothing may be written");
                Assert.That(window.Applied, Is.True, "and OK is not reported as having failed");
                Assert.That(said, Does.Contain("51776"));
            }
        }

        [TestCase(false, 0)]
        [TestCase(true, 80)]
        public void An_untouched_OK_over_a_port_the_box_cannot_show_says_nothing_changed(bool listening, int port)
        {
            // A gate on nothing, or on a port below the range the box offers, opens the box on the
            // default. OK there writes nothing, and it used to answer "Saved" with a port the next
            // start would not take.
            var written = new List<int>();

            using (var window = new GateStatusForm(
                new Gate(listening, port), null, chosen => { written.Add(chosen); return null; }, 0))
            {
                var said = window.Keep();

                Assert.That(written, Is.Empty);
                Assert.That(window.Applied, Is.True);
                Assert.That(said, Does.Not.Contain("Saved"));
                Assert.That(said, Is.EqualTo("Nothing was changed."));
            }
        }

        [Test]
        public void A_port_that_was_typed_is_written_even_where_the_gate_is_already_on_one()
        {
            // The other half, and the one that says the check above is about the box not moving
            // rather than about the gate.
            var written = new List<int>();

            using (var window = new GateStatusForm(
                Listening(51776), null, port => { written.Add(port); return null; }, 52000))
            {
                window.ChosenPort = 52000;

                window.Keep();

                Assert.That(written, Is.EqualTo(new[] { 52000 }));
            }
        }

        [Test]
        public void A_window_with_nothing_behind_it_changes_nothing()
        {
            // The window is handed a way to change the port, or it is not. One that was not still
            // reports, and says plainly that it cannot act.
            using (var window = new GateStatusForm(Listening(45678), null))
            {
                Assert.That(window.Keep(), Does.Contain("Nothing here"));
            }
        }

        [Test]
        public void A_port_outside_the_range_is_refused_before_anything_is_asked()
        {
            // The box itself will not go outside the range, so this is about the check rather than
            // about the control: what the operator types is checked in terms of the number.
            Assert.That(GatePort.Refuse(80), Does.Contain("between"));
            Assert.That(GatePort.Refuse(70000), Does.Contain("between"));
            Assert.That(GatePort.Refuse(GatePort.Lowest), Is.Null);
            Assert.That(GatePort.Refuse(GatePort.Highest), Is.Null);
        }

        private static string Takes(int port)
        {
            return null;
        }

        [Test]
        public void The_window_does_not_say_which_version_is_loaded()
        {
            // KeePass has a plugins dialog of its own that lists every plugin with its version,
            // which is where somebody checking one looks and where the answer is right for all of
            // them at once. A row here was a small window repeating what another window owns.
            var facts = GateStatusForm.Facts(Listening(45678), null);

            Assert.That(facts.Count, Is.EqualTo(1));
            Assert.That(Named(facts, "Listening"), Is.Not.Null);
        }

        [Test]
        public void It_ends_in_OK_and_Cancel_where_every_other_dialog_puts_them()
        {
            // Apply and Close said between them that the setting was already live and that leaving
            // was free, and a pair of buttons in the bottom right corner of a dialog is read as
            // saying neither. Every other window this plugin shows is OK and Cancel.
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 0))
            {
                var ok = ControlTree.FindByName<Button>(window, GateTab.StatusOkButtonName);
                var cancel = ControlTree.FindByName<Button>(window, GateTab.StatusCancelButtonName);

                Assert.That(ok, Is.Not.Null);
                Assert.That(cancel, Is.Not.Null);

                Assert.That(window.AcceptButton, Is.SameAs(ok), "Enter takes what is in the box");
                Assert.That(window.CancelButton, Is.SameAs(cancel), "Escape leaves it alone");

                Assert.That(ok.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(cancel.DialogResult, Is.EqualTo(DialogResult.Cancel));

                // Cancel on the outside, OK beside it, which is the order Windows uses and the
                // order the hand is already going to.
                Assert.That(cancel.Left, Is.GreaterThan(ok.Left));
            }
        }

        [Test]
        public void There_is_no_Apply_and_no_Close()
        {
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 0))
            {
                foreach (var caption in Buttons(window))
                {
                    Assert.That(caption, Is.Not.EqualTo("Apply"));
                    Assert.That(caption, Is.Not.EqualTo("Close"));
                }
            }
        }

        /// <summary>Every button caption on the window, however deep it is nested.</summary>
        private static IEnumerable<string> Buttons(Control root)
        {
            var found = new List<string>();
            Collect(root, found);
            return found;
        }

        private static void Collect(Control root, List<string> into)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Button)
                {
                    into.Add(child.Text);
                }

                Collect(child, into);
            }
        }

        [Test]
        public void The_address_is_a_list_of_choices_with_one_choice_on_it()
        {
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 0))
            {
                var addresses = ControlTree.FindByName<CheckedListBox>(
                    window, GateTab.StatusAddressListName);

                Assert.That(addresses, Is.Not.Null);
                Assert.That(addresses.Items.Count, Is.EqualTo(1));
                Assert.That(addresses.Items[0], Is.EqualTo(GateTab.LoopbackAddress));
                Assert.That(addresses.GetItemChecked(0), Is.True);
            }
        }

        [Test]
        public void Loopback_cannot_be_turned_off()
        {
            // Refused rather than disabled. A disabled list reads as a setting that is unavailable
            // for now; a live list whose only entry will not clear reads as the decision it is.
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 0))
            {
                var addresses = ControlTree.FindByName<CheckedListBox>(
                    window, GateTab.StatusAddressListName);

                addresses.SetItemChecked(0, false);

                Assert.That(addresses.GetItemChecked(0), Is.True,
                    "an address the threat model does not admit is not one this window can give up");
            }
        }

        private static string Advice(Control window)
        {
            var label = ControlTree.FindByName<Label>(window, GateTab.StatusAdviceName);
            Assert.That(label, Is.Not.Null);
            return label.Text;
        }

        private static IGateStatus Listening(int port)
        {
            return new Gate(true, port);
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

        /// <summary>A gate that says what a test needs it to say, and listens to nothing.</summary>
        private sealed class Gate : IGateStatus
        {
            public Gate(bool listening, int port)
            {
                IsListening = listening;
                Port = port;
            }

            public bool IsListening { get; private set; }

            public int Port { get; private set; }
        }

        private static IEnumerable<string> Names(IReadOnlyList<Fact> facts)
        {
            var names = new List<string>();
            foreach (var fact in facts)
            {
                names.Add(fact.Name);
            }

            return names;
        }
    }
}
