using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The profiles page: what it shows, what it says, and what it asks the database to do.
    /// </summary>
    /// <remarks>
    /// Driven through the page's own methods rather than its buttons, for the reason every panel
    /// here is: `Button.PerformClick` does nothing on a form that was never shown, silently.
    ///
    /// **One thing on this page cannot be tested that way**, and it is what Enter does in the name
    /// box. Which handler a key reaches is decided before the key reaches anything, by a form that
    /// exists and a message loop that is running, so the two tests about it use a real window and
    /// post the key onto the queue. Calling the handler passes whether or not the key would ever
    /// have arrived, which is how a box that never once saw Enter kept a green test for it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class ProfileListTests
    {
        [Test]
        public void A_database_naming_nothing_says_where_a_profile_comes_from()
        {
            using (var page = new ProfileList(new FakeProfiles(), null))
            {
                Assert.That(page.Shown, Is.Empty);
                Assert.That(ProfileList.NobodyText, Does.Contain("pair"),
                    "the other way a profile arrives, which is the one most people meet first");
            }
        }

        [Test]
        public void Enter_on_a_dialog_with_an_accept_button_adds_the_name_rather_than_pressing_it()
        {
            // The way anybody confirms a one-line form, and it was the way to lose one. A form with
            // an accept button treats Enter as that button wherever the focus is, so typing a name
            // and pressing Enter pressed OK: the dialog closed, every other buffered change on the
            // tab committed, and the name in the box went with the box.
            //
            // On a real window with the key on the queue, because nothing short of that measures
            // it. The page had a key handler for this and the handler had never run: a single line
            // text box tells WinForms that Enter is not a key for it, and a key a control does not
            // claim goes to the form rather than to the control.
            var database = new FakeProfiles();

            using (var dialog = new Dialog())
            {
                var page = dialog.Show(database);
                page.Typed = "ops";

                dialog.Press(Keys.Enter);

                Assert.That(dialog.Accepted, Is.EqualTo(0), "Enter pressed the dialog's own OK");
                Assert.That(page.Shown, Is.EqualTo(new[] { "ops" }), "and the name was never added");
                Assert.That(page.Typed, Is.Empty, "the box is cleared, so the next one is typed fresh");
                Assert.That(page.Result, Does.Contain("ops"));
            }
        }

        [Test]
        public void Escape_still_reaches_the_dialog_that_the_page_is_on()
        {
            // Only Enter is claimed, and only in this box. Escape means cancel on any dialog, and
            // a page that took it away would leave an operator typing into a box they could not
            // back out of. This is also why the box does not simply unhook the dialog's buttons
            // while the focus is in it, which is what the grant picker does for a dropdown that
            // needs both keys.
            var database = new FakeProfiles();

            using (var dialog = new Dialog())
            {
                var page = dialog.Show(database);
                page.Typed = "ops";

                dialog.Press(Keys.Escape);

                Assert.That(dialog.Canceled, Is.EqualTo(1), "Escape was swallowed by the name box");
                Assert.That(page.Shown, Is.Empty, "and Escape added the name");
            }
        }

        [Test]
        public void Enter_is_reported_as_dealt_with_so_the_box_neither_keeps_it_nor_beeps()
        {
            // The other half of claiming the key: having asked for it, the page has to say it is
            // finished with it. `SuppressKeyPress` is what takes the character away from the box,
            // which is what a text box handed a Return it cannot show otherwise answers with a
            // beep. The decision, called directly, because it is a decision.
            var database = new FakeProfiles();

            using (var page = new ProfileList(database, null))
            {
                page.Typed = "ops";
                var typed = new KeyEventArgs(Keys.Enter);

                page.Typing(typed);

                Assert.That(page.Shown, Is.EqualTo(new[] { "ops" }));
                Assert.That(typed.SuppressKeyPress, Is.True);
                Assert.That(typed.Handled, Is.True);
            }
        }

        [Test]
        public void Every_other_key_is_left_to_the_box()
        {
            // Because the box is a text box and typing into it is what it is for.
            var database = new FakeProfiles();

            using (var page = new ProfileList(database, null))
            {
                page.Typed = "ops";
                var typed = new KeyEventArgs(Keys.O);

                page.Typing(typed);

                Assert.That(page.Shown, Is.Empty);
                Assert.That(typed.Handled, Is.False);
                Assert.That(typed.SuppressKeyPress, Is.False);
            }
        }

        [Test]
        public void A_profile_added_is_a_profile_shown()
        {
            var database = new FakeProfiles();

            using (var page = new ProfileList(database, null))
            {
                Assert.That(page.Add("ops"), Does.Contain("ops"));

                Assert.That(page.Shown, Is.EqualTo(new[] { "ops" }));
                Assert.That(page.Typed, Is.Empty, "the box is cleared, so the next one is typed fresh");
            }
        }

        [Test]
        public void A_name_the_database_refuses_is_reported_and_nothing_is_shown()
        {
            var database = new FakeProfiles { Refusal = "A group dialog is open." };

            using (var page = new ProfileList(database, null))
            {
                Assert.That(page.Add("ops"), Is.EqualTo("A group dialog is open."));
                Assert.That(page.Shown, Is.Empty);
            }
        }

        [Test]
        public void A_name_a_redraw_takes_off_the_page_is_no_longer_said_to_be_available()
        {
            // A pairing filling the list to its ceiling while the dialog is open takes
            // the name added here out of what OK will write, and the line went on saying it was
            // available to grant.
            var database = new FakeProfiles();

            using (var page = new ProfileList(database, null))
            {
                Assume.That(page.Add("ops"), Does.Contain("available to grant"));

                database.Remove("ops");
                page.Describe();

                Assert.That(page.Result, Does.Contain("ops").And.Contain("no longer fits"));
            }
        }

        [Test]
        public void Adding_nothing_asks_for_a_name_rather_than_doing_nothing()
        {
            var database = new FakeProfiles();

            using (var page = new ProfileList(database, null))
            {
                Assert.That(page.Add("   "), Does.Contain("Type a name"));
                Assert.That(database.Added, Is.Empty, "the database is not asked about an empty name");
            }
        }

        [Test]
        public void Removing_a_profile_says_what_it_did_to_the_rules()
        {
            // Which is the number that matters on this page. A deletion that reached rules took
            // something away from somebody, and the operator has just done it in one click.
            var database = new FakeProfiles();
            database.Add("ops");
            database.LastRemoval = "The profile ops is gone, and was taken out of 3 rules.";

            using (var page = new ProfileList(database, null))
            {
                Assert.That(page.Remove(0), Does.Contain("3 rules"));
                Assert.That(page.Shown, Is.Empty);
            }
        }

        [Test]
        public void A_row_that_is_not_there_is_refused_rather_than_reaching_the_database()
        {
            var database = new FakeProfiles();

            using (var page = new ProfileList(database, null))
            {
                Assert.That(page.Remove(0), Does.Contain("no such profile"));
                Assert.That(page.Remove(-1), Does.Contain("no such profile"));
                Assert.That(database.Removed, Is.Empty);
            }
        }

        [Test]
        public void A_removal_the_database_refuses_leaves_the_profile_on_the_page()
        {
            var database = new FakeProfiles();
            database.Add("ops");
            database.Refusal = "A group dialog is open.";

            using (var page = new ProfileList(database, null))
            {
                Assert.That(page.Remove(0), Does.Contain("still there"));
                Assert.That(page.Shown, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void A_row_says_how_many_rules_name_it_and_how_many_clients_hold_it()
        {
            // Two different costs of removing it, and they do not move together: a profile with
            // rules and no clients was written ahead of the client that will hold it, and one with
            // clients and no rules grants nothing to anybody holding it.
            var profiles = new FakeProfiles();
            profiles.Add("ops");
            profiles.RuleCounts["ops"] = 3;
            profiles.ClientCounts["ops"] = 2;

            using (var list = new ProfileList(profiles, null))
            {
                var grid = ControlTree.FindByName<DataGridView>(list, ProfileList.ListName);
                var row = grid.Rows[0];

                Assert.That(row.Cells[0].Value, Is.EqualTo("ops"));
                Assert.That(row.Cells[1].Value, Is.EqualTo(3));
                Assert.That(row.Cells[2].Value, Is.EqualTo(2));
            }
        }

        [Test]
        public void A_page_with_no_database_behind_it_is_present_and_says_so()
        {
            // Rather than absent, which is the same choice the tab makes about being there at all:
            // a page that explains itself beats one that is missing without explanation.
            using (var page = new ProfileList(new NoProfiles(), null))
            {
                Assert.That(page.Shown, Is.Empty);
                Assert.That(page.Add("ops"), Does.Contain("No database"));
            }
        }

        [Test]
        public void Adding_back_a_name_rules_still_name_asks_first_and_no_adds_nothing()
        {
            // Removing a profile left its rules where they were, so adding the name again is those
            // rules granting again. The page asks, and an operator who says no has changed nothing.
            var profiles = new FakeProfiles { Waiting = "Adding ops again puts the rule naming it back in force." };
            var prompt = new FakePrompt(false);

            using (var page = new ProfileList(profiles, prompt))
            {
                Assert.That(page.Add("ops"), Does.Contain("was not added"));

                Assert.That(prompt.Asked, Is.EqualTo(1));
                Assert.That(prompt.LastQuestion, Does.StartWith(profiles.Waiting).And.EndWith("Add it anyway?"));
                Assert.That(profiles.Added, Is.Empty);
                Assert.That(page.Shown, Is.Empty);
            }
        }

        [Test]
        public void And_yes_adds_it()
        {
            var profiles = new FakeProfiles { Waiting = "Adding ops again puts the rule naming it back in force." };

            using (var page = new ProfileList(profiles, new FakePrompt(true)))
            {
                Assert.That(page.Add("ops"), Does.Contain("available to grant"));
                Assert.That(page.Shown, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void A_page_with_nobody_to_ask_brings_nothing_back()
        {
            // No answer is not a yes. A page built without a prompt refuses rather than putting
            // rules back in force on nobody's say.
            var profiles = new FakeProfiles { Waiting = "Adding ops again puts the rule naming it back in force." };

            using (var page = new ProfileList(profiles, null))
            {
                Assert.That(page.Add("ops"), Does.Contain("was not added"));
                Assert.That(profiles.Added, Is.Empty);
            }
        }

        [Test]
        public void A_name_nothing_waits_for_is_added_without_a_question()
        {
            var prompt = new FakePrompt(false);

            using (var page = new ProfileList(new FakeProfiles(), prompt))
            {
                Assert.That(page.Add("ops"), Does.Contain("available to grant"));
                Assert.That(prompt.Asked, Is.Zero);
            }
        }

        /// <summary>
        /// A shown window holding the page, with the two buttons a dialog has and a count of what
        /// pressed them.
        /// </summary>
        /// <remarks>
        /// KeePass's database settings dialog, which is where this page is injected, sets both:
        /// `AcceptButton` is its OK and `CancelButton` its Cancel. It overrides none of the key
        /// handling and its `KeyPreview` is off, so a plain form with those two buttons sends a key
        /// the same way that dialog does, and does it without a KeePass to open.
        /// </remarks>
        private sealed class Dialog : IDisposable
        {
            private const int KeyDown = 0x0100;
            private const int KeyUp = 0x0101;

            private readonly Form _form = OffScreenForm.New();

            private TextBox _box;

            /// <summary>How often the dialog's own OK was pressed.</summary>
            public int Accepted { get; private set; }

            /// <summary>How often its Cancel was.</summary>
            public int Canceled { get; private set; }

            /// <summary>Shows the page on it, with the focus where an operator would put it.</summary>
            public ProfileList Show(IProfiles profiles)
            {
                var page = new ProfileList(profiles, null);

                var ok = new Button { Name = "TheDialogsOwnOk", Dock = DockStyle.Bottom };
                ok.Click += delegate { Accepted++; };

                var cancel = new Button { Name = "TheDialogsOwnCancel", Dock = DockStyle.Bottom };
                cancel.Click += delegate { Canceled++; };

                _form.Controls.Add(page);
                _form.Controls.Add(ok);
                _form.Controls.Add(cancel);
                _form.AcceptButton = ok;
                _form.CancelButton = cancel;
                _form.Show();
                OffScreenForm.Pump();

                _box = ControlTree.FindByName<TextBox>(page, ProfileList.NameBoxName);
                _box.Focus();
                OffScreenForm.Pump();

                return page;
            }

            /// <summary>Puts a key on the queue for the name box, and lets the loop deliver it.</summary>
            /// <remarks>
            /// Posted rather than sent, because what is measured is the part of a key's journey
            /// that happens before any window sees it: the loop offers each key to the control it
            /// is addressed to and gives the ones no control claims to the form's own buttons. A
            /// sent message reaches the window procedure with all of that skipped, which is the
            /// half that was wrong.
            /// </remarks>
            public void Press(Keys key)
            {
                User32.PostMessage(_box.Handle, KeyDown, (IntPtr)(int)key, IntPtr.Zero);
                User32.PostMessage(_box.Handle, KeyUp, (IntPtr)(int)key, IntPtr.Zero);
                OffScreenForm.Pump();
            }

            public void Dispose()
            {
                _form.Dispose();
            }
        }

        /// <summary>The profiles of a database that is a list in a field.</summary>
        private sealed class FakeProfiles : IProfiles
        {
            private readonly List<string> _names = new List<string>();

            public List<string> Added { get; } = new List<string>();

            public List<string> Removed { get; } = new List<string>();

            /// <summary>What every gesture answers with, or null when they all work.</summary>
            public string Refusal { get; set; }

            public string LastRemoval { get; set; }

            public IReadOnlyList<string> Names()
            {
                return _names;
            }

            /// <summary>How many nodes name it, by name, for the tests that show a count.</summary>
            public Dictionary<string, int> RuleCounts { get; } = new Dictionary<string, int>();

            /// <summary>How many clients hold it, the same way.</summary>
            public Dictionary<string, int> ClientCounts { get; } = new Dictionary<string, int>();

            public IReadOnlyDictionary<string, int> Rules()
            {
                return RuleCounts;
            }

            public IReadOnlyDictionary<string, int> Clients()
            {
                return ClientCounts;
            }

            public string Add(string name)
            {
                Added.Add(name);

                if (Refusal != null)
                {
                    return Refusal;
                }

                if (!_names.Contains(name))
                {
                    _names.Add(name);
                    _names.Sort(StringComparer.Ordinal);
                }

                return null;
            }

            /// <summary>What adding any name would bring back, or null when nothing would.</summary>
            public string Waiting { get; set; }

            public string Returning(string name)
            {
                return Waiting;
            }

            public string Remove(string name)
            {
                Removed.Add(name);

                if (Refusal != null)
                {
                    return Refusal;
                }

                _names.Remove(name);
                return null;
            }
        }
    }
}
