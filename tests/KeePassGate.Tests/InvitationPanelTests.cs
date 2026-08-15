using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The invite button, which is the only way an invitation is ever created.
    /// </summary>
    /// <remarks>
    /// One thing here is different from every other button on this tab, and the wording has to
    /// carry it: inviting changes nothing in the database. There is nothing for Cancel to undo and
    /// nothing for a save to keep, because a token that was never in custom data cannot be brought
    /// back by a dialog writing a stale copy of it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class InvitationPanelTests
    {
        private FakeGateDatabase _database;
        private RecordingGateLog _log;
        private InvitationBook _book;

        [SetUp]
        public void SetUp()
        {
            _database = new FakeGateDatabase();
            _log = new RecordingGateLog();
            _book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _book.Dispose();
        }

        [Test]
        public void There_is_nothing_to_invite_anybody_to_without_an_authority()
        {
            using (var panel = NewPanel())
            {
                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);
                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Is.EqualTo(InvitationText.NoAuthority));
            }
        }

        [Test]
        public void The_button_comes_alive_once_the_database_has_one()
        {
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();

                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.True);
                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Is.EqualTo(InvitationText.None));
            }
        }

        [Test]
        public void Inviting_shows_a_token_the_operator_can_copy()
        {
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();
                panel.InviteClient();

                var box = Box(panel, GateTab.InvitationBoxName);

                Assert.That(box.ReadOnly, Is.True, "it is to be copied, not edited");
                Assert.That(PairingToken.Decode(box.Text), Is.Not.Null, "what is on screen is not a token");
            }
        }

        [Test]
        public void The_invitation_is_for_this_database_and_nothing_else()
        {
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();
                panel.InviteClient();

                Assert.That(_book.For(Digest()).Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void Inviting_writes_nothing_to_the_database()
        {
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();
                var before = _database.MarkedModifiedCount;
                var keys = System.Linq.Enumerable.Count(_database.Store.Keys);

                panel.InviteClient();

                Assert.That(_database.MarkedModifiedCount, Is.EqualTo(before),
                    "an invitation is not a change to the file");
                Assert.That(System.Linq.Enumerable.Count(_database.Store.Keys), Is.EqualTo(keys));
            }
        }

        [Test]
        public void The_tab_says_what_is_outstanding()
        {
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();
                panel.InviteClient();

                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Does.Contain("good for"));

                // A second press replaces the first code rather than adding one, so the line says
                // the same thing about the same one outstanding code.
                panel.InviteClient();

                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Does.Contain("good for"));
            }
        }

        [Test]
        public void Renewing_replaces_the_code_rather_than_adding_one()
        {
            // The button says renew, and the tab has to mean it: pressing it again leaves one code
            // outstanding, and the one on screen is the new one.
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();

                panel.InviteClient();
                var first = Box(panel, GateTab.InvitationBoxName).Text;

                panel.InviteClient();
                var second = Box(panel, GateTab.InvitationBoxName).Text;

                Assert.That(second, Is.Not.EqualTo(first));
                Assert.That(_book.Outstanding().Count, Is.EqualTo(1));
                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Does.Contain("good for"));
            }
        }

        [Test]
        public void The_button_offers_to_renew_a_code_only_while_there_is_one()
        {
            // It said "Renew invite" in every state, so the first press was offered as renewing a
            // code the line above it said does not exist.
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();

                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Is.EqualTo(InvitationText.None));
                Assert.That(Button(panel, GateTab.InviteButtonName).Text, Is.EqualTo(GateTab.InviteButtonText));

                panel.InviteClient();

                Assert.That(Button(panel, GateTab.InviteButtonName).Text, Is.EqualTo(GateTab.RenewInviteButtonText));

                _book.Burn(_book.Outstanding()[0].Id);
                panel.Describe();

                Assert.That(Button(panel, GateTab.InviteButtonName).Text, Is.EqualTo(GateTab.InviteButtonText),
                    "a code that has been spent is nothing to renew");
            }
        }

        [Test]
        public void Deleting_the_authority_leaves_nothing_to_invite_to()
        {
            using (var panel = NewPanel())
            {
                panel.CreateAuthority();
                panel.InviteClient();

                panel.DeleteAuthority();

                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);
                Assert.That(Label(panel, GateTab.InvitationLabelName).Text, Is.EqualTo(InvitationText.NoAuthority));

                // **And the code goes with it.** The box used to keep showing one, with the copy
                // button still enabled beside it, because the clear only fired on the one sentence
                // that says nothing is outstanding and a deleted authority says a different one.
                // What was left on screen was exactly the string somebody would copy and hand over,
                // for an authority that no longer exists: every attempt to use it resolves to
                // nothing and is dropped without an answer.
                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Empty,
                    "a code for an authority that is gone must not stay on screen");
                Assert.That(Button(panel, GateTab.CopyButtonName).Enabled, Is.False,
                    "and must not be copyable");
            }
        }

        [Test]
        public void An_authority_that_has_run_out_cannot_be_invited_to()
        {
            // `PAIRING.md` §311 refuses a pairing request on an expired authority before the
            // operator is ever disturbed, because one past its own dates signs nothing valid. A
            // code handed out here is therefore a code no program can redeem, and the operator
            // finds that out from whoever they sent it to rather than from the page they made it
            // on.
            RunOut();

            using (var panel = NewPanel())
            {
                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);
            }
        }

        [Test]
        public void Nothing_is_minted_against_an_authority_that_has_run_out()
        {
            // The button is dead, so what this covers is the clock crossing the expiry between
            // the page being drawn and the button being pressed. An answer in words is the whole
            // of the remedy there; a code would be the defect with one more step in it.
            RunOut();

            using (var panel = NewPanel())
            {
                panel.InviteClient();

                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Empty);
                Assert.That(_book.Outstanding(), Is.Empty);
                Assert.That(Label(panel, GateTab.ResultLabelName).Text,
                    Is.EqualTo(InvitationText.AuthorityExpired));
            }
        }

        [Test]
        public void A_code_minted_before_the_authority_ran_out_stops_being_offered()
        {
            // The one state an operator reaches by pressing nothing at all: the code was minted
            // while the authority was current and the clock crossed its expiry with the dialog
            // still open. The line went on counting down, the copy button stayed alive, and the
            // string in the box was one whose redemption `PAIRING.md` §311 answers with
            // `500 expired authority`.
            //
            // Arranged rather than played out. Playing it out needs a clock this panel does not
            // take, so the book is given the invitation and the box is given the code the panel
            // would have put in it.
            RunOut();

            var invitation = _book.Invite(Digest());
            Assume.That(invitation, Is.Not.Null);

            using (var panel = NewPanel())
            {
                Box(panel, GateTab.InvitationBoxName).Text = invitation.Text;
                panel.Describe();

                Assert.That(Label(panel, GateTab.InvitationLabelName).Text,
                    Is.EqualTo(InvitationText.None),
                    "a line counting down for a code nothing can redeem is a line that is wrong");
                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Empty,
                    "a code this authority cannot honor must not stay on screen to be handed over");
                Assert.That(Button(panel, GateTab.CopyButtonName).Enabled, Is.False,
                    "and must not be copyable");
            }
        }

        [Test]
        public void A_damaged_authority_cannot_be_invited_to_either()
        {
            // The same question as the one above, and it used to be answered by asking whether the
            // authority could be recreated. Recreating is exactly what damage wants, so the button
            // was alive over an authority the page was already describing as unusable.
            _database.Store.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");

            using (var panel = NewPanel())
            {
                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);
                Assert.That(Label(panel, GateTab.InvitationLabelName).Text,
                    Is.EqualTo(InvitationText.NoAuthority));
            }
        }

        [Test]
        public void An_authority_that_has_not_been_applied_yet_cannot_be_invited_to()
        {
            // The tab reads its own unapplied writes, so an authority created a moment ago on this
            // very page describes itself here perfectly. The gate does not read through the pending
            // decorator: it reads the live database, and would find nothing under that digest. A
            // code minted now would look right, count down, and resolve nowhere.
            var pending = new PendingDatabase(_database, _log);

            using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log, "1.0.0", null, _book))
            {
                panel.CreateAuthority();

                panel.InviteClient();

                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Empty);
                Assert.That(Label(panel, GateTab.ResultLabelName).Text,
                    Is.EqualTo(InvitationText.AuthorityWaiting));
                Assert.That(_book.Outstanding(), Is.Empty,
                    "nothing may be minted against an authority the gate cannot see");
            }
        }

        [Test]
        public void Once_the_authority_is_applied_the_invitation_names_what_the_gate_will_find()
        {
            var pending = new PendingDatabase(_database, _log);

            using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log, "1.0.0", null, _book))
            {
                panel.CreateAuthority();
                pending.Apply();

                panel.InviteClient();

                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Not.Empty);
                Assert.That(_book.Outstanding(), Has.Count.EqualTo(1));
                Assert.That(_book.Outstanding()[0].DatabaseId, Is.EqualTo(Digest()),
                    "the digest an invitation carries has to be the one in the live database");
            }
        }

        [Test]
        public void There_is_nothing_to_copy_until_something_has_been_invited()
        {
            using (var panel = NewPanel())
            {
                Assert.That(Button(panel, GateTab.CopyButtonName).Enabled, Is.False);

                panel.CreateAuthority();

                Assert.That(Button(panel, GateTab.CopyButtonName).Enabled, Is.False,
                    "an authority is not an invitation");

                panel.InviteClient();

                Assert.That(Button(panel, GateTab.CopyButtonName).Enabled, Is.True);
            }
        }

        [Test]
        public void Copying_puts_exactly_what_is_on_screen_on_the_clipboard()
        {
            using (var panel = NewPanel())
            {
                var clipboard = new FakeClipboard();
                panel.Clipboard = clipboard;

                panel.CreateAuthority();
                panel.InviteClient();
                panel.CopyInvitation();

                Assert.That(clipboard.Last, Is.EqualTo(Box(panel, GateTab.InvitationBoxName).Text));
                Assert.That(PairingToken.Decode(clipboard.Last), Is.Not.Null, "what was copied is not a token");
            }
        }

        [Test]
        public void Copying_says_it_happened_because_nothing_on_screen_would_otherwise_change()
        {
            using (var panel = NewPanel())
            {
                panel.Clipboard = new FakeClipboard();

                panel.CreateAuthority();
                panel.InviteClient();
                panel.CopyInvitation();

                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.EqualTo(InvitationText.Copied));
            }
        }

        [Test]
        public void Copying_nothing_touches_nothing()
        {
            using (var panel = NewPanel())
            {
                var clipboard = new FakeClipboard();
                panel.Clipboard = clipboard;

                panel.CopyInvitation();

                Assert.That(clipboard.Copied, Is.Empty, "an empty box is not something to copy");
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty);
            }
        }

        [Test]
        public void A_clipboard_somebody_else_is_holding_says_so_and_leaves_the_token_on_screen()
        {
            using (var panel = NewPanel())
            {
                // What the clipboard throws when another program has it open, after the retries
                // inside `SetDataObject` have all failed.
                panel.Clipboard = new FakeClipboard
                {
                    Fails = () => new ExternalException("the clipboard is open in another program")
                };

                panel.CreateAuthority();
                panel.InviteClient();
                panel.CopyInvitation();

                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.EqualTo(InvitationText.ClipboardBusy));
                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Not.Empty,
                    "the operator still has to be able to copy it by hand");
                Assert.That(_log.Warnings, Is.Not.Empty);
            }
        }

        [Test]
        public void A_copied_code_asks_to_stay_out_of_clipboard_history_and_the_cloud()
        {
            // Windows keeps the last entries of clipboard history until somebody clears them by
            // hand, and the cloud clipboard sends them to a person's other machines, so a code
            // copied out of that box outlived its own ten minutes in two places nobody looks at.
            // KeePass marks everything it copies with these four names; this marks one thing with
            // them.
            //
            // The names are written out here rather than read off the code under test, because
            // they are Windows's names: a test that took them from the same constants would agree
            // with a typo. Read off the data object rather than off the clipboard, because a test
            // that took the developer's clipboard away every time it ran is a test nobody runs.
            var copied = SystemClipboard.Excluded("kpg1-invented-code");

            Assert.That(copied.GetText(), Is.EqualTo("kpg1-invented-code"));

            foreach (var format in new[]
            {
                "ExcludeClipboardContentFromMonitorProcessing",
                "Clipboard Viewer Ignore",
                "CanIncludeInClipboardHistory",
                "CanUploadToCloudClipboard"
            })
            {
                Assert.That(copied.GetDataPresent(format, false), Is.True,
                    "the copy is not marked " + format);
            }
        }

        [Test]
        public void The_copy_button_says_what_it_is_to_anything_that_cannot_see_the_icon()
        {
            using (var panel = NewPanel())
            {
                var copy = Button(panel, GateTab.CopyButtonName);

                Assert.That(copy.Text, Is.Empty, "it carries an icon, not a caption");
                Assert.That(copy.AccessibleName, Is.EqualTo(GateTab.CopyButtonLabel));
            }
        }

        [Test]
        public void A_tab_assembled_without_pairing_has_the_button_and_cannot_press_it()
        {
            using (var panel = new AuthorityPanel(_database, new FakePrompt(true), _log, "1.0.0"))
            {
                panel.CreateAuthority();

                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);
            }
        }

        private AuthorityPanel NewPanel()
        {
            return new AuthorityPanel(_database, new FakePrompt(true), _log, "1.0.0", null, _book);
        }

        /// <summary>
        /// Whether a control is one an operator would see, rather than one that has merely been
        /// told to show itself.
        /// </summary>
        /// <remarks>
        /// `Control.Visible` answers for the whole chain above it, and the chain here is a page of
        /// a `TabControl` that no window has ever drawn: every page is behind every other one, so
        /// a label on any of them answers no whatever it was told. The page is brought to the
        /// front over a real handle for the reason `GateTabFitsTests` gives, which is that a
        /// `TabControl` without one never brings a page forward at all.
        /// </remarks>
        private static bool OnScreen(AuthorityPanel panel, Control control)
        {
            panel.CreateControl();

            var tabs = ControlTree.FindByName<TabControl>(panel, GateTab.SubTabsName);
            Assert.That(tabs, Is.Not.Null, "the tab has no pages to bring forward");

            var page = ControlTree.FindByName<TabPage>(panel, GateTab.ClientsPageName);
            Assert.That(page, Is.Not.Null, "the clients page is not where this expects it");

            tabs.SelectedTab = page;
            panel.PerformLayout();

            return control.Visible;
        }

        /// <summary>Gives this database an authority that ran out a day ago.</summary>
        /// <remarks>
        /// Minted through the issuer against a clock in the past rather than planted as text, so
        /// what the tab reads is a real certificate whose dates have passed. `CA.md` §219 has no
        /// reissue, so this is a state a database reaches by being left alone for ten years and
        /// leaves only by being given a new authority.
        /// </remarks>
        private void RunOut()
        {
            var whenItWasMinted =
                DateTimeOffset.UtcNow - AuthorityIssuer.AuthorityLifetime - TimeSpan.FromDays(1);

            new AuthorityService(_database.Store, new AuthorityIssuer(() => whenItWasMinted), _log)
                .Create();
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

        private static TextBox Box(Control panel, string name)
        {
            var box = ControlTree.FindByName<TextBox>(panel, name);
            Assert.That(box, Is.Not.Null, "no box called " + name);
            return box;
        }
    }

    /// <summary>The wording, on its own, where it can be read.</summary>
    [TestFixture]
    public sealed class InvitationTextTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

        [Test]
        public void Nothing_outstanding_says_so()
        {
            Assert.That(InvitationText.For(Array.Empty<Invitation>(), Now), Is.EqualTo(InvitationText.None));
            Assert.That(InvitationText.For(null, Now), Is.EqualTo(InvitationText.None));
        }

        [Test]
        public void It_says_the_three_things_about_a_code_that_surprise_people()
        {
            // All three used to be a line of their own under the code. They are said in the line
            // about the code itself now, because a page this short cannot spend a line repeating
            // what the line above it implies, and a notice nobody reads says nothing at all.
            using (var invitation = At(Now.AddMinutes(7)))
            {
                var said = InvitationText.For(new[] { invitation }, Now);

                Assert.That(said, Does.Contain("in-memory"),
                    "a code does not travel with the file and does not survive KeePass closing");
                Assert.That(said, Does.Contain("consumes it"),
                    "the first client to use one is the only client that can");
                Assert.That(said, Does.Contain("renewed"),
                    "renewing replaces the code rather than adding one");
            }
        }

        [Test]
        public void One_outstanding_says_how_long_is_left()
        {
            using (var invitation = At(Now.AddMinutes(7)))
            {
                Assert.That(
                    InvitationText.For(new[] { invitation }, Now),
                    Is.EqualTo(
                        "The in-memory code is good for another 7 minutes until the first client "
                        + "consumes it or it gets renewed. "
                        + "The approval dialog will call it abcdef01."));
            }
        }

        [Test]
        public void A_single_minute_reads_as_one()
        {
            using (var invitation = At(Now.AddSeconds(30)))
            {
                Assert.That(InvitationText.For(new[] { invitation }, Now), Does.Contain("another 1 minute until"));
            }
        }

        [Test]
        public void The_countdown_and_the_name_are_both_the_soonest_invitation_s()
        {
            // The book holds one code per database, and this reads whatever list it is handed, so
            // the soonest is taken rather than the first. The identifier comes off that same one,
            // because a countdown about one invitation and a name about another would be worse
            // than either on its own.
            using (var soon = At(Now.AddMinutes(2), "sooner01"))
            using (var later = At(Now.AddMinutes(9), "later001"))
            {
                Assert.That(
                    InvitationText.For(new[] { later, soon }, Now),
                    Is.EqualTo(
                        "The in-memory code is good for another 2 minutes until the first client "
                        + "consumes it or it gets renewed. "
                        + "The approval dialog will call it sooner01."));
            }
        }

        [Test]
        public void One_that_has_just_run_out_says_that_rather_than_a_negative_number()
        {
            using (var invitation = At(Now.AddSeconds(-1)))
            {
                Assert.That(InvitationText.For(new[] { invitation }, Now), Does.Contain("just run out"));
            }
        }

        private static Invitation At(DateTimeOffset expires, string id = "abcdef01")
        {
            return new Invitation(id, "database", PairingToken.New(), Now, expires);
        }
    }
}
