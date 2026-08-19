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
        public void A_gate_that_could_not_bind_says_why_and_that_only_a_restart_changes_it()
        {
            // The port is taken once, at startup, so a refusal lasts the whole session. A line
            // saying the log knows why is a line asking somebody to go and look for something they
            // can do nothing about.
            var down = new Gate(false, 0) { Refusal = "127.0.0.1:51100 could not be taken: in use" };

            Assert.That(GateStatusForm.Advice(down), Does.Contain("could not be taken"));
            Assert.That(GateStatusForm.Advice(down), Does.Contain("started again"));
        }

        [Test]
        public void A_variable_that_names_no_port_is_the_reason_given_and_a_restart_is_still_needed()
        {
            // The operator's ruling on K004: a `KEEPASSGATE_PORT` that is not a port stops the gate,
            // and the window says which variable, what it holds, what it may hold, and that nothing
            // is listening, in the sentence the log and the box at startup carry.
            string refused;
            Transport.GateOptions.PortToTake(0, "80", out refused);
            var down = new Gate(false, 0) { Refusal = refused };

            var advice = GateStatusForm.Advice(down);

            Assert.That(advice, Does.StartWith(refused));
            Assert.That(advice, Does.Contain("KEEPASSGATE_PORT").And.Contain("\"80\""));
            Assert.That(advice, Does.Contain("1024").And.Contain("65535"));
            Assert.That(advice, Does.Contain("started again"));
            Assert.That(GateStatusForm.Facts(down, null)[0].Value, Does.StartWith("no"));
        }

        [Test]
        public void A_gate_that_is_down_still_says_the_rest_of_the_plugin_works()
        {
            // The half that is easy to leave out and expensive to leave out. Nothing else has
            // stopped, and an operator who reads "not listening" and closes the window would never
            // find out that every setting on it still does what it says.
            var down = new Gate(false, 0) { Refusal = "127.0.0.1:51100 could not be taken: in use" };

            var advice = GateStatusForm.Advice(down);

            Assert.That(advice, Does.Contain("settings"));
            Assert.That(advice, Does.Contain("still works"));
        }

        [Test]
        public void The_box_starts_on_the_port_the_next_start_takes()
        {
            // The choice rather than the socket. The two differ after a port is written down, for
            // the rest of the session, and a box showing the socket then showed the waiting port
            // nowhere and could not take it back (H106). The socket is the row above.
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 51776))
            {
                Assert.That(window.ChosenPort, Is.EqualTo(51776));
            }
        }

        [Test]
        public void With_nothing_chosen_the_box_starts_on_the_port_the_gate_is_using()
        {
            using (var window = new GateStatusForm(Listening(45678), null, Takes, 0))
            {
                Assert.That(window.ChosenPort, Is.EqualTo(45678));
            }
        }

        [Test]
        public void A_port_waiting_for_the_next_start_is_said_in_words_before_the_advice()
        {
            var waiting = GateStatusForm.Waiting(Listening(51776), 52000);

            Assert.That(waiting, Does.Contain("127.0.0.1:52000").And.Contain("next time KeePass starts"));

            using (var window = new GateStatusForm(Listening(51776), null, Takes, 52000))
            {
                Assert.That(Advice(window), Does.StartWith(waiting));
            }

            Assert.That(GateStatusForm.Waiting(Listening(51776), 51776), Is.Null, "nothing is waiting");
            Assert.That(GateStatusForm.Waiting(Listening(51776), 0), Is.Null, "nothing was chosen");
            Assert.That(GateStatusForm.Waiting(new Gate(false, 0), 52000), Is.Null,
                "a gate on nothing moves nowhere, and says why it is on nothing instead");
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
            // port is written down and the socket is not rebound. An operator who reopens the
            // window to check the change, and answers the dialog the way a dialog is answered,
            // once wrote the old port back over the new one; the box now opens on the waiting
            // port, and an OK that did not move it writes nothing.
            var written = new List<int>();

            using (var window = new GateStatusForm(
                Listening(51776), null, port => { written.Add(port); return null; }, 52000))
            {
                Assume.That(window.ChosenPort, Is.EqualTo(52000), "the choice, not the socket");

                var said = window.Keep();

                Assert.That(written, Is.Empty, "nothing was typed here, so nothing may be written");
                Assert.That(window.Applied, Is.True, "and OK is not reported as having failed");
                Assert.That(said, Is.EqualTo("Nothing was changed."));
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
        public void Typing_the_port_the_gate_is_on_takes_a_waiting_one_back()
        {
            // The other half, and the one that says the check above is about the box not moving
            // rather than about the gate: the socket's own port, typed over a waiting one, is
            // written, which is how a change is taken back from this window (H106).
            var written = new List<int>();

            using (var window = new GateStatusForm(
                Listening(51776), null, port => { written.Add(port); return null; }, 52000))
            {
                window.ChosenPort = 51776;

                var said = window.Keep();

                Assert.That(written, Is.EqualTo(new[] { 51776 }));
                Assert.That(window.Changed, Is.True);
                Assert.That(said, Does.Contain("stays on 127.0.0.1:51776"));
                Assert.That(said, Does.Not.Contain("told the new one"),
                    "the clients are on this port already, and have nothing to be told");
            }
        }

        [Test]
        public void Only_a_port_that_actually_moved_is_worth_telling_the_operator_about()
        {
            // `Changed` is what decides whether anything is said once the window has gone, and the
            // sentence it carries is not a receipt: it says the port waits for the next start, and
            // that every client paired against the old one has to be told the new one. Saying that
            // about a port nobody moved would train somebody to dismiss it.
            using (var window = new GateStatusForm(Listening(51776), null, Takes, 52000))
            {
                window.Keep();

                Assert.That(window.Applied, Is.True, "the operator was not refused");
                Assert.That(window.Changed, Is.False, "and nothing moved, so there is nothing to say");
            }

            using (var window = new GateStatusForm(Listening(51776), null, Takes, 52000))
            {
                window.ChosenPort = 53000;

                window.Keep();

                Assert.That(window.Changed, Is.True);
                Assert.That(window.Result, Does.Contain("53000"),
                    "and what is said is what the window answered");
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

            public string Refusal { get; set; }
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
