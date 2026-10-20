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
    /// revoke everything.
    /// </summary>
    /// <remarks>
    /// Driven through the methods the buttons call rather than through the buttons. `PerformClick`
    /// does nothing on a control that cannot be selected, silently, and a button on a tab page that
    /// no window has ever drawn cannot be. A test that pressed one would pass while proving
    /// nothing, which is why what a button does is public on this panel. The end to end tier
    /// presses the real buttons on a real KeePass, which is where the wiring is worth checking.
    /// </remarks>
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
        public void The_tab_has_a_page_for_the_authority_and_a_page_for_the_clients()
        {
            using (var panel = NewPanel(true))
            {
                var tabs = ControlTree.FindByName<TabControl>(panel, GateTab.SubTabsName);

                Assert.That(tabs, Is.Not.Null);
                Assert.That(tabs.TabPages.Count, Is.EqualTo(2));
                Assert.That(tabs.TabPages[0].Text, Is.EqualTo(GateTab.AuthorityPageText));
                Assert.That(tabs.TabPages[1].Text, Is.EqualTo(GateTab.ClientsPageText));
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
                panel.CreateAuthority();

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
                panel.CreateAuthority();

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
                panel.CreateAuthority();

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
                panel.CreateAuthority();
                var digest = Digest();

                panel.RecreateAuthority();

                Assert.That(prompt.Asked, Is.EqualTo(1));
                Assert.That(Digest(), Is.EqualTo(digest), "A refused confirmation must change nothing.");
            }
        }

        [Test]
        public void Recreating_replaces_the_authority_when_the_answer_is_yes()
        {
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();
                var digest = Digest();

                panel.RecreateAuthority();

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
                panel.CreateAuthority();
                _database.Store.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
                _database.Store.Plant(AuthorityKeys.ClientPrefix + "bbb", "record");

                panel.RecreateAuthority();

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
                panel.CreateAuthority();

                panel.DeleteAuthority();

                Assert.That(prompt.Asked, Is.EqualTo(1));
                Assert.That(_database.Store.Keys.All(k => AuthorityKeys.FingerprintInRetirement(k) != null), Is.True,
                    "nothing is left but the retirement that keeps it deleted");
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
            }
        }

        [Test]
        public void Deleting_does_nothing_when_the_answer_is_no()
        {
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                panel.CreateAuthority();

                panel.DeleteAuthority();

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
                Assert.DoesNotThrow(() => panel.CreateAuthority());

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
                panel.CreateAuthority();

                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Does.Contain("could not be read back"));
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
                Assert.That(_database.MarkedModifiedCount, Is.Zero);
            }
        }

        [Test]
        public void Damage_is_shown_and_only_the_two_buttons_that_can_help_are_offered()
        {
            _database.Store.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");

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

        [Test]
        public void A_database_that_went_away_under_the_dialog_is_said_to_be_gone()
        {
            // KeePass locks or closes the database while this dialog is open, which
            // `PendingDatabase.Apply` names as a state it has to answer. Whether the database is
            // still there is asked of `IsOpen` rather than of its store, because no store here is
            // ever null: every one of them is built on demand and the tab's buffer wraps whatever
            // that answers with. Reading through one finds no keys, which reads exactly like a
            // database that has no authority yet, so a tab asking it that way would offer Create
            // for a database that was not there.
            var pending = new PendingDatabase(_database, _log);

            using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log, "1.0.0"))
            {
                panel.CreateAuthority();
                pending.Apply();
                panel.Describe();

                Assume.That(Label(panel, GateTab.StatusLabelName).Text,
                    Is.EqualTo(AuthorityDescription.PresentHeadline));

                _database.IsOpen = false;
                panel.Describe();

                Assert.That(pending.PluginCustomData, Is.Not.Null,
                    "the store never goes, which is why the question cannot be asked of it");
                Assert.That(Label(panel, GateTab.StatusLabelName).Text,
                    Is.EqualTo(AuthorityDescription.NoDatabaseHeadline));
                Assert.That(Button(panel, GateTab.CreateButtonName).Enabled, Is.False,
                    "and nothing offers to change a database that is not there");
            }
        }

        [Test]
        public void The_tab_shows_who_has_been_admitted_without_being_asked()
        {
            // The approval dialog tells the operator a client can be revoked later from this tab.
            // Until this list existed, that sentence was not true, and it is on the page rather
            // than behind a button because a list nobody presses for is a list nobody forgets to
            // check.
            using (var panel = NewPanel(true))
            {
                Admit("agent-fictitious", DateTimeOffset.UtcNow);
                panel.Describe();

                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);

                Assert.That(list, Is.Not.Null, "the clients page has no list on it");
                Assert.That(list.Clients.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_record_landing_while_this_tab_is_open_puts_the_client_on_the_list()
        {
            // Pairing is invited from this tab and writes its record from a connection thread, so
            // the moment the clients page is most likely to be wrong is the moment somebody is
            // watching it. The page used to read on a page coming forward and on a revocation and
            // at no other time, so an operator who paired while looking at the list watched the
            // pairing succeed, saw nothing change, and had to switch away and back.
            var notice = new AdmissionNotice();

            using (var panel = new AuthorityPanel(
                       _database, new FakePrompt(true), _log, "1.0.0", null, null, null, notice))
            {
                panel.CreateAuthority();
                panel.Describe();

                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                Assume.That(list.Clients.Count, Is.EqualTo(0), "nobody has paired yet");

                Admit("agent-fictitious", DateTimeOffset.UtcNow);

                Assert.That(list.Clients.Count, Is.EqualTo(0),
                    "a record in the database is not on the page until the page is told");

                notice.Raise();

                Assert.That(list.Clients.Count, Is.EqualTo(1),
                    "the page reads its own database again when it hears a record landed");
            }
        }

        [Test]
        public void A_tab_that_has_been_disposed_is_not_told_about_the_next_pairing()
        {
            // The notice outlives every tab: it is the plugin's and the tab belongs to a dialog.
            // A subscription left behind is a disposed control handed an event on the next
            // pairing, once for every settings dialog that was ever opened.
            var notice = new AdmissionNotice();

            using (var panel = new AuthorityPanel(
                       _database, new FakePrompt(true), _log, "1.0.0", null, null, null, notice))
            {
                panel.CreateAuthority();
                panel.Describe();
            }

            Admit("agent-fictitious", DateTimeOffset.UtcNow);

            Assert.DoesNotThrow(() => notice.Raise(),
                "a disposed tab let go of the notice when it went");
        }

        [Test]
        public void A_tab_that_was_given_no_notice_still_works()
        {
            // Every other test here builds one that way, and so does anything embedding this
            // outside the plugin.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();
                Assert.DoesNotThrow(() => panel.Describe());
            }
        }

        [Test]
        public void A_tab_that_cannot_read_its_authority_lists_nobody()
        {
            // A damaged authority is exactly when the operator most wants to believe what the tab
            // says. Going on showing the clients it listed a minute ago would be a lie about who
            // can reach this database, and the safe answer is nobody.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();
                Admit("agent-fictitious", DateTimeOffset.UtcNow);
                panel.Describe();

                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                Assume.That(list.Clients.Count, Is.EqualTo(1));

                _database.Store.ThrowOnRead = new InvalidOperationException("the database said no");
                panel.Describe();

                // The headline says which state this is and the detail says why. A store that
                // refuses a read is damage the operator is told about, not an authority that
                // silently lists nobody.
                Assert.That(Label(panel, GateTab.StatusLabelName).Text,
                    Is.EqualTo(AuthorityDescription.DamagedHeadline));
                Assert.That(Label(panel, GateTab.DetailLabelName).Text,
                    Does.Contain("could not be read"));
                Assert.That(list.Clients, Is.Empty);
            }
        }

        [Test]
        public void Bringing_the_clients_page_forward_reads_the_database_again()
        {
            // The database changes while this dialog is open: pairing is invited from this page and
            // writes its record from somewhere else. A page showing only what was true when the tab
            // was built would say nobody is admitted about the client just approved.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();

                // A `TabControl` with no window handle never changes its selection, so it never
                // raises the event this is about. The same reason a button on a page nobody drew
                // cannot be pressed.
                panel.CreateControl();

                var tabs = ControlTree.FindByName<TabControl>(panel, GateTab.SubTabsName);
                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                Assume.That(list.Clients, Is.Empty);

                Admit("agent-fictitious", DateTimeOffset.UtcNow);

                // Authority is 0, Profiles 1, Clients 2.
                tabs.SelectedIndex = 2;

                Assert.That(list.Clients.Count, Is.EqualTo(1),
                    "the page came forward and did not look again");
                Assert.That(Fact(panel, "Admission records"), Is.EqualTo("1"));
            }
        }

        [Test]
        public void Revoking_from_the_list_leaves_the_tab_agreeing_with_the_database()
        {
            // The list and the record count on the other page are read from the same database, and
            // a revocation that updated only one of them would leave the tab arguing with itself.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();
                Admit("agent-fictitious", DateTimeOffset.UtcNow);
                panel.Describe();

                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);

                // The mark is in the row it acts on, so the row is what it is told rather than a
                // selection somewhere else.
                Assert.That(list.Revoke(0), Does.Contain("agent-fictitious"));
                Assert.That(list.Clients, Is.Empty);
                Assert.That(panel.AdmittedClients().Records, Is.Empty);
                Assert.That(Fact(panel, "Admission records"), Is.EqualTo("0"));
            }
        }

        [Test]
        public void A_database_that_has_admitted_nobody_lists_nobody()
        {
            using (var panel = NewPanel(true))
            {
                Assert.That(panel.AdmittedClients().Records, Is.Empty);
            }
        }

        [Test]
        public void Every_admitted_client_is_listed_newest_first()
        {
            // Newest first because the one somebody is looking for is usually the one they just
            // approved, and a list that grows downwards puts it furthest away.
            using (var panel = NewPanel(true))
            {
                Admit("agent-fictitious", DateTimeOffset.UtcNow.AddDays(-2));
                Admit("agent-invented", DateTimeOffset.UtcNow);

                var listed = panel.AdmittedClients().Records;

                Assert.That(listed.Count, Is.EqualTo(2));
                Assert.That(listed[0].Subject, Does.Contain("agent-invented"));
            }
        }

        [Test]
        public void Revoking_a_client_takes_its_record_out_and_asks_for_a_save()
        {
            using (var panel = NewPanel(true))
            {
                var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                var before = _database.MarkedModifiedCount;

                var refused = panel.Revoke(record);

                Assert.That(refused, Is.Null);
                Assert.That(panel.AdmittedClients().Records, Is.Empty);
                Assert.That(_database.MarkedModifiedCount, Is.GreaterThan(before),
                    "a revocation KeePass was not told about is one the operator cannot save");
            }
        }

        [Test]
        public void Revoking_one_client_leaves_the_others_admitted()
        {
            using (var panel = NewPanel(true))
            {
                var going = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                Admit("agent-invented", DateTimeOffset.UtcNow);

                Assume.That(panel.Revoke(going), Is.Null);

                var left = panel.AdmittedClients().Records;
                Assert.That(left.Count, Is.EqualTo(1));
                Assert.That(left[0].Subject, Does.Contain("agent-invented"),
                    "revoking one client must not be recreating the authority in disguise");
            }
        }

        [Test]
        public void A_revocation_the_store_swallows_is_reported_rather_than_announced()
        {
            // The one wrong answer on this page that nothing notices until the client connects
            // again. `AdmissionStore.Remove` reads the key back before it says yes, precisely so
            // this cannot happen, and the panel used to throw that answer away and report success
            // whatever it said: the row vanished, the operator was told the client could no longer
            // reach the database, and the certificate went on opening it.
            using (var panel = NewPanel(true))
            {
                var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                _database.Store.SwallowWrites = true;

                var refused = panel.Revoke(record);

                Assert.That(refused, Is.Not.Null,
                    "a removal that did not land must not be reported as one that did");
                Assert.That(panel.AdmittedClients().Records, Is.Not.Empty,
                    "and the client is still admitted");
            }
        }

        [Test]
        public void Revoking_a_record_that_was_already_gone_is_not_a_failure()
        {
            // False from the store also means it was not there to begin with, which is a stale list
            // rather than a write that did not land. The operator asked for it to be gone and it is
            // gone, so this is not something to refuse.
            using (var panel = NewPanel(true))
            {
                var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                Assume.That(panel.Revoke(record), Is.Null);

                Assert.That(panel.Revoke(record), Is.Null);
            }
        }

        [Test]
        public void A_revocation_and_the_prune_it_carries_wait_for_OK()
        {
            // The revocation is a key of its own now, and it lands the way every other key on the
            // tab does: replayed at OK, and not at all on Cancel. So does the prune riding on it,
            // which is what keeps a dialog somebody only looked at from changing the database.
            var expired = AuthorityKeys.RevocationKey(new string('a', 64));
            _database.Store.Plant(expired, "2020-01-01T00:00:00Z");

            var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);
            var revocation = AuthorityKeys.RevocationKey(record.AdmissionKey);

            foreach (var accepted in new[] { false, true })
            {
                // Through the store the tab's pages write to, which is what the panel's `Revoke`
                // does, so this holds whichever way the panel happens to be constructed.
                var pending = new PendingDatabase(_database, null);
                var held = new AdmissionStore(pending.PluginCustomData, null);

                Assume.That(held.Remove(record.AdmissionKey), Is.True);
                Assume.That(held.All().Records, Is.Empty, "the page reads what it is holding");

                Assert.That(_database.Store.Get(revocation), Is.Null, "nothing lands before OK");
                Assert.That(_database.Store.Get(expired), Is.Not.Null);

                if (accepted)
                {
                    pending.Apply();

                    Assert.That(_database.Store.Get(revocation), Is.Not.Null);
                    Assert.That(_database.Store.Get(record.StorageKey), Is.Empty);
                    Assert.That(_database.Store.Get(expired), Is.Null, "and the prune landed with it");
                }
                else
                {
                    pending.Discard();

                    Assert.That(new AdmissionStore(_database.Store, null).Find(record.AdmissionKey),
                        Is.Not.Null, "Cancel means Cancel");
                    Assert.That(_database.Store.Get(expired), Is.Not.Null);
                }
            }
        }

        [Test]
        public void Deleting_the_authority_takes_the_revocations_with_it_and_waits_for_OK()
        {
            // Through the store the tab's pages write to, like the revocation above: the delete's
            // removals of revocation keys land at OK with the rest of it, and not at all on Cancel.
            new AuthorityService(_database.Store, null).Create();
            var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);
            Assume.That(new AdmissionStore(_database.Store, null).Remove(record.AdmissionKey), Is.True);
            var revocation = AuthorityKeys.RevocationKey(record.AdmissionKey);

            foreach (var accepted in new[] { false, true })
            {
                var pending = new PendingDatabase(_database, null);
                Assume.That(new AuthorityService(pending.PluginCustomData, null).Delete().Succeeded, Is.True);

                Assert.That(pending.PluginCustomData.Get(revocation), Is.Null, "the page reads what it is holding");
                Assert.That(_database.Store.Get(revocation), Is.Not.Null, "nothing lands before OK");

                if (accepted)
                {
                    pending.Apply();

                    Assert.That(_database.Store.Get(revocation), Is.Null);
                    Assert.That(_database.Store.Get(record.StorageKey), Is.Null, "and the record it emptied");
                }
                else
                {
                    pending.Discard();

                    Assert.That(_database.Store.Get(revocation), Is.Not.Null, "Cancel means Cancel");
                }
            }
        }

        [Test]
        public void A_record_that_came_back_is_counted_apart_from_the_clients()
        {
            using (var panel = NewPanel(true))
            {
                var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                Admit("agent-invented", DateTimeOffset.UtcNow);
                Assume.That(panel.Revoke(record), Is.Null);

                // What a stale copy merged in does.
                _database.Store.Plant(record.StorageKey, record.Save());

                var listing = panel.AdmittedClients();

                Assert.That(listing.Records.Single().Subject, Does.Contain("agent-invented"));
                Assert.That(listing.CameBack, Is.EqualTo(1));
                Assert.That(_database.Store.Get(record.StorageKey), Is.Not.Empty,
                    "reading the page changes nothing in the database");
            }
        }

        [Test]
        public void A_record_that_came_back_goes_with_the_next_revocation_and_waits_for_OK_like_it()
        {
            // The same rule as every prune: only inside a write this plugin makes anyway, which on
            // this tab is replayed at OK and not at all on Cancel.
            var revoked = Admit("agent-fictitious", DateTimeOffset.UtcNow);
            var other = Admit("agent-invented", DateTimeOffset.UtcNow);
            Assume.That(new AdmissionStore(_database.Store, null).Remove(revoked.AdmissionKey), Is.True);
            _database.Store.Plant(revoked.StorageKey, revoked.Save());

            foreach (var accepted in new[] { false, true })
            {
                var pending = new PendingDatabase(_database, null);
                var held = new AdmissionStore(pending.PluginCustomData, null);

                Assume.That(held.Remove(other.AdmissionKey), Is.True);
                Assert.That(held.All().CameBack, Is.Zero, "the page reads what it is holding");
                Assert.That(_database.Store.Get(revoked.StorageKey), Is.Not.Empty, "nothing lands before OK");

                if (accepted)
                {
                    pending.Apply();
                    Assert.That(_database.Store.Get(revoked.StorageKey), Is.Null, "and the record went with OK");
                }
                else
                {
                    pending.Discard();
                    Assert.That(_database.Store.Get(revoked.StorageKey), Is.Not.Empty, "Cancel means Cancel");
                }
            }
        }

        [Test]
        public void A_record_this_authority_did_not_issue_is_named_as_a_stranger()
        {
            // A recreate or a delete landing while a pairing does leaves the record behind, and a
            // merge or a partial restore carries records past the authority that made sense of
            // them. `ClientCredentials` pins the anchor first, so every one of those certificates
            // is refused; the clients page listed them as admitted clients all the same.
            using (var panel = NewPanel(true))
            {
                var stranger = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                panel.CreateAuthority();

                using (var reading = new AuthorityService(_database.Store, null).Read())
                using (var certificate = stranger.Certificate())
                {
                    Assume.That(reading.IsPresent, Is.True);

                    Assert.That(AnchorPin.Chains(certificate, reading.Material.Authority, true),
                        Is.False,
                        "the gate refuses this certificate, which is what the page has to agree with");

                    // And one this authority did issue, so that the page is not simply calling
                    // everything a stranger.
                    using (var issued = new AuthorityIssuer().IssueClient(
                        reading.Material.Authority, "agent-invented", new[] { "ops" }))
                    {
                        new AdmissionStore(_database.Store, null).Write(
                            AdmissionRecord.For(issued, DateTimeOffset.UtcNow, new[] { "ops" }));
                    }
                }

                var strangers = panel.Strangers(panel.AdmittedClients());

                Assert.That(panel.AdmittedClients().Records, Has.Count.EqualTo(2));
                Assert.That(strangers, Has.Count.EqualTo(1));
                Assert.That(strangers, Does.Contain(stranger.AdmissionKey));
            }
        }

        [Test]
        public void Every_record_is_a_stranger_once_the_authority_is_gone()
        {
            // Nothing is served for a database with no authority, so no record in it reaches
            // anything. The first page says that in words and counts them; this is the clients
            // page agreeing, which is the whole point of asking the same question twice.
            using (var panel = NewPanel(true))
            {
                var record = Admit("agent-fictitious", DateTimeOffset.UtcNow);

                Assume.That(panel.AdmittedClients().Records, Has.Count.EqualTo(1));

                Assert.That(panel.Strangers(panel.AdmittedClients()),
                    Does.Contain(record.AdmissionKey));
            }
        }

        [Test]
        public void A_refusal_carrying_somebody_elses_message_is_cut_to_what_the_band_can_hold()
        {
            // `AuthorityChange.Message` ends in whatever the store threw, and the band showed it
            // whole while the two sentences this page composes itself were cut. Nothing on the tab
            // scrolls, so an untrimmed one wraps, grows the page past the tab, and pushes the
            // three buttons out of reach with no way to scroll to them.
            _database.Store.ThrowOnWrite = new InvalidOperationException(Rambling());

            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();

                var said = Label(panel, GateTab.ResultLabelName).Text;

                Assert.That(said, Does.StartWith("The authority could not be created:"),
                    "the refusal is still reported, and still says which action failed");
                Assert.That(said, Does.EndWith("..."), "and reads as cut rather than as unfinished");
                Assert.That(said.Length, Is.LessThanOrEqualTo(Clipped.Limit + 3),
                    "which is the bound `GateTabFitsTests` measures the band against");
            }
        }

        [Test]
        public void A_problem_carrying_somebody_elses_message_is_cut_the_same_way()
        {
            // The other half of the same gap. A reading that will not read reports the problem as
            // `The authority could not be read: ` and whatever threw, and the sentences under the
            // facts table showed that whole. There has to be an authority for the read to reach.
            _database.Store.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");
            _database.Store.ThrowOnRead = new InvalidOperationException(Rambling());

            using (var panel = NewPanel(true))
            {
                var detail = Label(panel, GateTab.DetailLabelName).Text;

                Assert.That(detail, Does.Contain("The authority could not be read:"));

                foreach (var line in detail.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    Assert.That(line.Length, Is.LessThanOrEqualTo(Clipped.Limit + 3), line);
                }
            }
        }

        /// <summary>
        /// A message longer than the tab can hold, in ordinary words.
        /// </summary>
        /// <remarks>
        /// Ordinary words rather than a run of one character, because what is at stake is how many
        /// lines the label wraps to and a string with nowhere to break does not wrap at all. An
        /// exception message is composed for a log by whoever threw it, and nothing here chooses
        /// its length, which is the whole of the problem.
        /// </remarks>
        private static string Rambling()
        {
            return string.Join(" ", Enumerable.Repeat(
                "The key set for this certificate is not available on this machine.", 6));
        }

        [Test]
        public void Revoking_with_no_database_says_so_rather_than_throwing()
        {
            using (var panel = new AuthorityPanel(null, new FakePrompt(true), _log, "1.0.0"))
            {
                Assert.That(panel.AdmittedClients().Records, Is.Empty);
                Assert.That(panel.Revoke(null), Is.Not.Null);
            }
        }

        /// <summary>
        /// What the facts table says beside a name. The count lives there rather than in the
        /// sentences below it, so this is where a test asks about it.
        /// </summary>
        private static string Fact(Control panel, string name)
        {
            var table = ControlTree.FindByName<TableLayoutPanel>(panel, GateTab.FactsTableName);
            Assert.That(table, Is.Not.Null, "the tab has no facts table");

            for (var row = 0; row < table.RowCount; row++)
            {
                var label = table.GetControlFromPosition(0, row) as Label;
                if (label != null && label.Text == name)
                {
                    var value = table.GetControlFromPosition(1, row) as Label;
                    Assert.That(value, Is.Not.Null, "the row for " + name + " has no value");
                    return value.Text;
                }
            }

            Assert.Fail("no fact called " + name);
            return null;
        }

        private AdmissionRecord Admit(string name, DateTimeOffset when)
        {
            using (var certificate = SelfSigned(name))
            {
                var record = AdmissionRecord.For(certificate, when, new[] { "ops" });
                new AdmissionStore(_database.Store, null).Write(record);
                return record;
            }
        }

        private static System.Security.Cryptography.X509Certificates.X509Certificate2 SelfSigned(string name)
        {
            using (var key = System.Security.Cryptography.RSA.Create(2048))
            {
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=" + name,
                    key,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                return request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
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
