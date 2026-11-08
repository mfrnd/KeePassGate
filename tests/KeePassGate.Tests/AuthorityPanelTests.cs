using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Pairing;
using KeePassGate.Acl;
using KeePassGate.Ui;
using KeePassGate.Testing;
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
        public void The_tab_has_a_page_for_each_of_its_three_subjects()
        {
            // What this database can issue, the names a rule can grant to, and who holds what was
            // issued. Three questions asked on different occasions, so three pages.
            using (var panel = NewPanel(true))
            {
                var tabs = ControlTree.FindByName<TabControl>(panel, GateTab.SubTabsName);

                Assert.That(tabs, Is.Not.Null);
                Assert.That(tabs.TabPages.Count, Is.EqualTo(3));
                Assert.That(tabs.TabPages[0].Text, Is.EqualTo(GateTab.AuthorityPageText));
                Assert.That(tabs.TabPages[1].Text, Is.EqualTo(GateTab.ProfilesPageText));
                Assert.That(tabs.TabPages[2].Text, Is.EqualTo(GateTab.ClientsPageText));
            }
        }

        [Test]
        public void It_says_which_button_makes_any_of_this_real()
        {
            // Which used to be the opposite sentence. The tab wrote at once, and a tab that has
            // already acted, inside a dialog offering OK and Cancel, is a tab lying about the
            // buttons around it.
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

                // The table is what says it happened. A sentence saying so as well was a line of
                // height spent naming something already on screen.
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.PresentHeadline));
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty);
                Assert.That(Label(panel, GateTab.ResultLabelName).Visible, Is.False);
            }
        }

        [Test]
        public void Creating_tells_the_host_there_is_something_to_save()
        {
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();

                Assert.That(_database.MarkedModifiedCount, Is.EqualTo(1));

                // Where the operator is told to press OK and then save is the standing notice,
                // once, rather than every answer repeating it.
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

                // The fingerprint on the table is different, which is the cue, and what recreating
                // cost was in the question the operator answered before pressing anything.
                Assert.That(Digest(), Is.Not.EqualTo(digest));
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty);
            }
        }

        [Test]
        public void The_question_before_recreating_states_what_it_costs()
        {
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                panel.CreateAuthority();
                var store = new AdmissionStore(_database.PluginCustomData, null);
                using (var first = IssueOwnClient("agent-one"))
                using (var second = IssueOwnClient("agent-two"))
                {
                    store.Write(AdmissionRecord.For(first, DateTimeOffset.UtcNow, new[] { "ops" }));
                    store.Write(AdmissionRecord.For(second, DateTimeOffset.UtcNow, new[] { "ops" }));
                }

                panel.RecreateAuthority();

                Assert.That(prompt.LastQuestion, Does.Contain("revokes every client"));
                Assert.That(prompt.LastQuestion, Does.Contain("2 clients are admitted"));
            }
        }

        [Test]
        public void Recreating_between_undecided_authorities_costs_the_clients_of_each()
        {
            // Nothing is served while several authorities wait to be chosen between, but choosing
            // would keep the clients of the authority kept, and recreating keeps none.
            var candidates = Undecided();
            var prompt = new FakePrompt(false);
            var store = new AdmissionStore(_database.PluginCustomData, null);
            foreach (var candidate in candidates)
            {
                var key = CertificateBlob.Load(
                    AuthorityDocument.Read(_database.Store.Get(AuthorityKeys.AuthorityKey(candidate)), null).Authority);
                using (var client = new AuthorityIssuer().IssueClient(key, "agent-" + candidate.Substring(0, 4), new[] { "ops" }).Read())
                {
                    store.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }));
                }
            }

            using (var panel = NewPanel(prompt))
            {
                panel.RecreateAuthority();

                Assert.That(prompt.LastQuestion, Does.Contain("2 clients are admitted"));
            }
        }

        [Test]
        public void A_record_the_gate_refuses_costs_nothing_to_recreate()
        {
            // Counted as the clients page judges its rows: a record of another authority, a record
            // not for a client to present and a record that will not read admit nobody, so
            // recreating takes nobody's admission away. All three were counted as clients that
            // would pair again.
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            using (var elsewhere = TestAuthority.New())
            using (var foreign = elsewhere.IssueClient("agent-elsewhere", "ops"))
            {
                panel.CreateAuthority();
                var store = new AdmissionStore(_database.PluginCustomData, null);
                store.Write(AdmissionRecord.For(foreign, DateTimeOffset.UtcNow, new[] { "ops" }));
                using (var host = new AuthorityIssuer().IssueHost(OwnAuthority()).Read())
                {
                    store.Write(AdmissionRecord.For(host, DateTimeOffset.UtcNow, new[] { "ops" }));
                }

                _database.Store.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
                Assume.That(panel.AdmittedClients().Records, Has.Count.EqualTo(2));

                panel.RecreateAuthority();

                Assert.That(prompt.LastQuestion, Does.Contain("No client has been admitted yet"));
            }
        }

        [Test]
        public void Deleting_records_left_without_an_authority_says_they_admit_nobody()
        {
            // Delete is offered with no authority when records outlived it, and the question
            // written for a live authority told the operator that deleting stops the database
            // being served and that its clients would pair again, beside a page calling the
            // records inert.
            var prompt = new FakePrompt(false);
            using (var elsewhere = TestAuthority.New())
            using (var client = elsewhere.IssueClient("agent-left", "ops"))
            {
                new AdmissionStore(_database.PluginCustomData, null)
                    .Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }));
            }

            using (var panel = NewPanel(prompt))
            {
                Assume.That(Button(panel, GateTab.DeleteButtonName).Enabled, Is.True);

                panel.DeleteAuthority();

                Assert.That(prompt.LastQuestion, Does.Contain("admit nobody"));
                Assert.That(prompt.LastQuestion, Does.Not.Contain("stops this database being served"));
                Assert.That(prompt.LastQuestion, Does.Not.Contain("pair again"));
            }
        }

        [Test]
        public void A_client_that_renewed_is_one_client_in_what_recreating_costs()
        {
            // Two records with one client key until it gives the older certificate up, and it was
            // two clients that would have to pair again.
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                panel.CreateAuthority();
                using (var older = IssueOwnClient("agent-renewing"))
                using (var renewed = IssueOwnClient("agent-renewing"))
                {
                    var store = new AdmissionStore(_database.PluginCustomData, null);
                    var paired = AdmissionRecord.For(older, DateTimeOffset.UtcNow.AddDays(-1), new[] { "ops" });
                    store.Write(paired);
                    store.Write(AdmissionRecord.For(renewed, DateTimeOffset.UtcNow, new[] { "ops" }, paired.ClientKey));
                }

                panel.RecreateAuthority();

                Assert.That(prompt.LastQuestion, Does.Contain("One client is admitted"));
            }
        }

        [Test]
        public void Two_programs_under_one_name_are_two_clients_in_what_recreating_costs()
        {
            // Each would have to pair again, whatever name they chose.
            var prompt = new FakePrompt(false);
            using (var panel = NewPanel(prompt))
            {
                panel.CreateAuthority();
                var store = new AdmissionStore(_database.PluginCustomData, null);
                using (var first = IssueOwnClient("agent"))
                using (var second = IssueOwnClient("agent"))
                {
                    store.Write(AdmissionRecord.For(first, DateTimeOffset.UtcNow.AddDays(-1), new[] { "ops" }));
                    store.Write(AdmissionRecord.For(second, DateTimeOffset.UtcNow, new[] { "ops" }));
                }

                panel.RecreateAuthority();

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
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.RetiredHeadline),
                    "and the page shows the authority as retired rather than as never made");
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
            using (var panel = new AuthorityPanel(null, new FakePrompt(true), _log))
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

            using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log))
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

            using (var panel = new AuthorityPanel(_database, new FakePrompt(true), _log, null, null, notice))
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

            using (var panel = new AuthorityPanel(_database, new FakePrompt(true), _log, null, null, notice))
            {
                panel.CreateAuthority();
                panel.Describe();
            }

            Admit("agent-fictitious", DateTimeOffset.UtcNow);

            Assert.DoesNotThrow(() => notice.Raise(),
                "a disposed tab let go of the notice when it went");
        }

        [Test]
        public void A_tab_that_failed_to_be_built_is_not_told_about_the_next_pairing()
        {
            // H086: the tab subscribed before it built anything, so a constructor that failed left
            // a panel nobody would dispose listening for the life of the plugin, and the next
            // pairing drew into controls that were never made, after its record was saved.
            var notice = new AdmissionNotice();

            Assert.Throws<InvalidOperationException>(
                () => new AuthorityPanel(new UnreadableDatabase(), new FakePrompt(true), _log, null, null, notice));

            Assert.DoesNotThrow(() => notice.Raise(), "a tab that was never built was told about a pairing");
        }

        [Test]
        public void The_profiles_page_is_counted_as_it_is_built_and_again_only_when_in_front()
        {
            // J036: the page walks every rule in the tree and every record, the slowest thing on
            // the tab on a large database, and it was counted twice as the dialog was built and
            // again for every button on the other two pages.
            var profiles = new CountedProfiles();

            using (var panel = new AuthorityPanel(_database, new FakePrompt(true), _log, null, profiles))
            {
                Assert.That(profiles.Counted, Is.EqualTo(1), "once, by the page itself, as the tab was built");

                panel.CreateAuthority();
                panel.Describe();
                Assert.That(profiles.Counted, Is.EqualTo(1), "a button on the first page counts nothing on the second");

                panel.CreateControl();
                var tabs = ControlTree.FindByName<TabControl>(panel, GateTab.SubTabsName);
                tabs.SelectedTab = ControlTree.FindByName<TabPage>(panel, GateTab.ProfilesPageName);
                Assert.That(profiles.Counted, Is.EqualTo(2), "bringing the page forward counts it again");

                panel.Describe();
                Assert.That(profiles.Counted, Is.EqualTo(3), "and so does a redraw while it is in front");
            }
        }

        /// <summary>Profiles that count how often the page asked for its counts, and name none.</summary>
        private sealed class CountedProfiles : IProfiles
        {
            public int Counted { get; private set; }

            public string LastRemoval
            {
                get { return null; }
            }

            public IReadOnlyList<string> Names()
            {
                return Array.Empty<string>();
            }

            public IReadOnlyDictionary<string, int> Rules()
            {
                Counted++;
                return new Dictionary<string, int>();
            }

            public IReadOnlyDictionary<string, int> Clients()
            {
                return new Dictionary<string, int>();
            }

            public string Add(string name)
            {
                return null;
            }

            public string Returning(string name)
            {
                return null;
            }

            public string Remove(string name)
            {
                return null;
            }
        }

        /// <summary>A database whose custom data cannot even be asked for, which stops a tab being built.</summary>
        private sealed class UnreadableDatabase : IGateDatabase
        {
            public bool IsOpen
            {
                get { return true; }
            }

            public string Name
            {
                get { return "Invented Vault"; }
            }

            public IKeyValueStore PluginCustomData
            {
                get { throw new InvalidOperationException("invented for the test"); }
            }

            public IRuledNodes Nodes
            {
                get { return null; }
            }

            public void MarkModified()
            {
            }
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

                var refused = panel.Revoke(record.AdmissionKey);

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

                Assume.That(panel.Revoke(going.AdmissionKey), Is.Null);

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

                var refused = panel.Revoke(record.AdmissionKey);

                Assert.That(refused, Is.Not.Null,
                    "a removal that did not land must not be reported as one that did");
                Assert.That(panel.AdmittedClients().Records, Is.Not.Empty,
                    "and the client is still admitted");
            }
        }

        [Test]
        public void A_record_that_will_not_read_is_listed_and_its_X_takes_it_away_at_OK()
        {
            // K024: such a record admits nobody, the first page counted it and warned about it, and
            // nothing on the tab could take it away short of recreating the authority, which makes
            // every client pair again.
            new AuthorityService(_database.Store, null).Create();
            Admit("agent-fictitious", DateTimeOffset.UtcNow);
            var damaged = new string('d', 64);
            var stored = AuthorityKeys.ClientRecordKey(damaged);
            _database.Store.Plant(stored, "v=1\nsubject=x\n");

            var pending = new PendingDatabase(_database, _log);

            using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log))
            {
                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);

                Assert.That(list.Unreadable, Is.EqualTo(new[] { damaged }), "listed, by its key");
                Assert.That(Label(panel, GateTab.DetailLabelName).Text,
                    Does.Contain("cannot be read and admits nobody. The Clients page lists it."));
                Assert.That(Fact(panel, "Admission records"), Is.EqualTo("2"));

                Assert.That(list.Revoke(list.Clients.Count), Does.Contain("taken away"));

                Assert.That(list.Unreadable, Is.Empty);
                Assert.That(Label(panel, GateTab.DetailLabelName).Text, Does.Not.Contain("cannot be read"));
                Assert.That(Fact(panel, "Admission records"), Is.EqualTo("1"));
                Assert.That(_database.Store.Get(stored), Is.Not.Empty, "nothing lands before OK");
            }

            pending.Apply();

            Assert.That(_database.Store.Get(stored), Is.Empty);
            Assert.That(_database.Store.Get(AuthorityKeys.RevocationKey(damaged)), Is.Empty,
                "revoked with the empty value a record with no date is given");

            var left = new AdmissionStore(_database.Store, null).All();
            Assert.That(left.Unreadable, Is.Empty);
            Assert.That(left.Records.Count, Is.EqualTo(1), "and the client beside it is still admitted");
        }

        [Test]
        public void A_removal_of_a_record_that_will_not_read_the_store_swallows_is_reported()
        {
            // `Find` answers null for such a record, so a read back asking only it would call a
            // removal that did not land one that did, and the row would go while the record stayed.
            var damaged = new string('d', 64);
            _database.Store.Plant(AuthorityKeys.ClientRecordKey(damaged), "v=1\nsubject=x\n");

            using (var panel = NewPanel(true))
            {
                _database.Store.SwallowWrites = true;

                Assert.That(panel.Revoke(damaged), Is.EqualTo("the record is still in the database"));
                Assert.That(panel.AdmittedClients().Unreadable, Is.EqualTo(new[] { damaged }));
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
                Assume.That(panel.Revoke(record.AdmissionKey), Is.Null);

                Assert.That(panel.Revoke(record.AdmissionKey), Is.Null);
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
                Assume.That(panel.Revoke(record.AdmissionKey), Is.Null);

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

                    Assert.That(AnchorPin.Chains(certificate, reading.Material.Authority),
                        Is.False,
                        "the gate refuses this certificate, which is what the page has to agree with");

                    // And one this authority did issue, so that the page is not simply calling
                    // everything a stranger.
                    using (var issued = new AuthorityIssuer().IssueClient(
                        reading.Material.AuthorityKey, "agent-invented", new[] { "ops" }).Read())
                    {
                        new AdmissionStore(_database.Store, null).Write(
                            AdmissionRecord.For(issued, DateTimeOffset.UtcNow, new[] { "ops" }));
                    }
                }

                var reaches = panel.Reaches(panel.AdmittedClients());

                Assert.That(panel.AdmittedClients().Records, Has.Count.EqualTo(2));
                Assert.That(reaches.Values.Count(reach => reach == ClientReach.Stranger), Is.EqualTo(1));
                Assert.That(reaches[stranger.AdmissionKey], Is.EqualTo(ClientReach.Stranger));
            }
        }

        [Test]
        public void A_record_the_gate_refuses_before_its_dates_is_named_for_what_refuses_it()
        {
            // H055 and J064: `ClientCredentials` asks for client authentication and for a
            // certificate that has begun before it looks at the record, and the page asked only
            // where the certificate came from. A record carrying this authority's own host
            // certificate, which only a crafted or merged file holds, and one whose certificate
            // begins in ten days, which a wrong clock produces, were both listed as clients that
            // work until next year.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();

                AdmissionRecord host;
                AdmissionRecord early;
                AdmissionRecord ordinary;
                var now = DateTimeOffset.UtcNow;

                using (var reading = new AuthorityService(_database.Store, null).Read())
                {
                    Assume.That(reading.IsPresent, Is.True);
                    var authority = reading.Material.AuthorityKey;

                    host = AdmissionRecord.For(reading.Material.Host, now, new[] { "ops" });

                    using (var issued = new AuthorityIssuer(() => now.AddDays(10)).IssueClient(
                        authority, "agent-early", new[] { "ops" }).Read())
                    {
                        early = AdmissionRecord.For(issued, now, new[] { "ops" });
                    }

                    using (var issued = new AuthorityIssuer().IssueClient(authority, "agent-invented", new[] { "ops" }).Read())
                    {
                        ordinary = AdmissionRecord.For(issued, now, new[] { "ops" });
                    }
                }

                var store = new AdmissionStore(_database.Store, null);
                store.Write(host);
                store.Write(early);
                store.Write(ordinary);

                var reaches = panel.Reaches(panel.AdmittedClients());

                Assert.That(reaches[host.AdmissionKey], Is.EqualTo(ClientReach.NotForClients));
                Assert.That(reaches[early.AdmissionKey], Is.EqualTo(ClientReach.NotYetValid));
                Assert.That(reaches[ordinary.AdmissionKey], Is.EqualTo(ClientReach.Reaches),
                    "and one this authority issued to a client, inside its dates, reaches");

                panel.Describe();
                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                var shown = Enumerable.Range(0, list.Clients.Count)
                    .ToDictionary(at => list.Clients[at].AdmissionKey, at => UntilOf(list, at));

                Assert.That(shown[host.AdmissionKey], Is.EqualTo(ClientList.NotForClientsText));
                Assert.That(shown[early.AdmissionKey], Is.EqualTo(ClientList.NotYetValidText));
            }
        }

        [Test]
        public void A_record_from_another_authority_draws_no_expiry_warning_on_the_first_page()
        {
            // J065: one record, two answers on one tab. The clients page said "not this authority"
            // beside it, and the first page said a client expires within 30 days, which sends the
            // operator to pair again a program that never reached the database.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();

                // Signed by nobody this database knows, and in the last third of its life, where
                // one of this database's own clients would be warned about.
                var stranger = Admit("agent-fictitious", DateTimeOffset.UtcNow);
                panel.Describe();

                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                Assume.That(list.Clients.Single().AdmissionKey, Is.EqualTo(stranger.AdmissionKey));

                Assert.That(UntilOf(list, 0), Is.EqualTo(ClientList.StrangerText));
                Assert.That(Label(panel, GateTab.DetailLabelName).Text, Does.Not.Contain("expire"));
            }
        }

        /// <summary>What a row of the clients page says in the column that says how long.</summary>
        private static string UntilOf(ClientList list, int row)
        {
            var grid = ControlTree.FindByName<DataGridView>(list, ClientList.ListName);
            return Convert.ToString(grid.Rows[row].Cells[3].Value, System.Globalization.CultureInfo.InvariantCulture);
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

                Assert.That(panel.Reaches(panel.AdmittedClients())[record.AdmissionKey],
                    Is.EqualTo(ClientReach.Stranger));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void A_pairing_that_lands_while_a_recreate_or_delete_waits_is_kept_and_named_a_stranger(bool recreating)
        {
            // The one case the key by key replay leaves behind on purpose, run whole rather than
            // in its parts. The tab buffers the recreate or the delete, which lists the records
            // there are as it is pressed; a pairing then lands on the live database, a record under
            // the authority the gate still serves and the profile it approved named in the same
            // write; and OK replays only what was buffered.
            Assume.That(new AuthorityService(_database.Store, null).Create().Succeeded, Is.True);
            var before = IssuedAndAdmitted("agent-before");

            var pending = new PendingDatabase(_database, null);
            var held = new AuthorityService(pending.PluginCustomData, null);
            Assume.That((recreating ? held.Recreate() : held.Delete()).Succeeded, Is.True);

            var landed = IssuedAndAdmitted("agent-landed");
            var names = ProfileNames.Read(_database.Store.Get(ProfileNames.CustomDataKey));
            Assume.That(names.Add("reader"), Is.Null);
            _database.Store.Set(ProfileNames.CustomDataKey, names.Write());

            pending.Apply();

            var admissions = new AdmissionStore(_database.Store, null);
            Assert.That(admissions.Find(before.AdmissionKey), Is.Null, "the records it listed went with it");
            Assert.That(admissions.Find(landed.AdmissionKey), Is.Not.Null, "the one that landed meanwhile is kept");
            Assert.That(ProfileNames.Read(_database.Store.Get(ProfileNames.CustomDataKey)).Names,
                Does.Contain("reader"), "and so is the name it brought");

            using (var panel = NewPanel(true))
            {
                Assert.That(panel.Reaches(panel.AdmittedClients())[landed.AdmissionKey],
                    Is.EqualTo(ClientReach.Stranger),
                    "its certificate chains to an authority that is gone, which the clients page says on its row");
            }
        }

        /// <summary>A client of the authority the database holds now, admitted in the live store.</summary>
        private AdmissionRecord IssuedAndAdmitted(string name)
        {
            using (var reading = new AuthorityService(_database.Store, null).Read())
            using (var issued = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, new[] { "ops" }).Read())
            {
                var record = AdmissionRecord.For(issued, DateTimeOffset.UtcNow, new[] { "ops" });
                new AdmissionStore(_database.Store, null).Write(record);
                return record;
            }
        }

        [Test]
        public void A_record_that_cannot_be_checked_is_its_own_answer_and_no_other_record_s()
        {
            // A certificate this authority signed, whose extended key usage will not decode, passes
            // the pin, which reads no usage, and throws from the usage check. One try around the
            // whole list ended it there, so every older record had no answer, and the page shows a
            // record with no answer as a client admitted until its date: this authority's own host
            // certificate and a foreign authority's client among them.
            var service = new AuthorityService(_database.Store, null);
            Assume.That(service.Create().Succeeded, Is.True);

            var now = DateTimeOffset.UtcNow;
            AdmissionRecord host, foreign, healthy, crafted;
            using (var reading = service.Read())
            {
                var key = reading.Material.AuthorityKey;
                host = Admitted(AdmissionRecord.For(reading.Material.Host, now.AddDays(-3), new[] { "ops" }));

                using (var elsewhere = new AuthorityIssuer().IssueClient(new AuthorityIssuer().CreateAuthority(), "agent-elsewhere", new[] { "ops" }).Read())
                {
                    foreign = Admitted(AdmissionRecord.For(elsewhere, now.AddDays(-2), new[] { "ops" }));
                }

                using (var issued = new AuthorityIssuer().IssueClient(key, "agent-fictitious", new[] { "ops" }).Read())
                {
                    healthy = Admitted(AdmissionRecord.For(issued, now.AddDays(-1), new[] { "ops" }));
                }

                crafted = AdmitReshaped(key, now, der => Reshaped.SignedAgain(der, key, tbs => Reshaped.WithExtensionValue(
                    tbs, Org.BouncyCastle.Asn1.X509.X509Extensions.ExtendedKeyUsage, new byte[] { 0x30, 0x05, 0x06, 0x01 })));
            }

            using (var certificate = crafted.Certificate())
            {
                Assume.That(() => KeePassGate.Pipeline.ClientCredentials.IsForClientAuthentication(certificate),
                    Throws.InstanceOf<System.Security.Cryptography.CryptographicException>());
            }

            using (var panel = NewPanel(true))
            {
                var reaches = panel.Reaches(panel.AdmittedClients());

                Assert.That(reaches[crafted.AdmissionKey], Is.EqualTo(ClientReach.Stranger), "the gate's answer for it");
                Assert.That(reaches[host.AdmissionKey], Is.EqualTo(ClientReach.NotForClients));
                Assert.That(reaches[foreign.AdmissionKey], Is.EqualTo(ClientReach.Stranger));
                Assert.That(reaches[healthy.AdmissionKey], Is.EqualTo(ClientReach.Reaches));

                panel.Reaches(panel.AdmittedClients());
                Assert.That(_log.Errors.Count(e => e.Message.Contains(CertificateDigest.Shorten(crafted.AdmissionKey))), Is.EqualTo(1),
                    "named once for the authority rather than at every redraw");
            }
        }

        [Test]
        public void A_record_naming_an_algorithm_no_authority_can_check_leaves_create_working()
        {
            // With no authority the tab is built whatever the records hold, since a page with no
            // anchor asks the pin nothing. Create then drew the page against the authority it had
            // just made, and the pin threw `InvalidCastException` for a record naming Ed25519 out
            // of the button's handler, which has no catch, into WinForms' own dialog: the page went
            // on reading as though there were no authority while the dialog held a new one, and
            // every page switch threw the same.
            var service = new AuthorityService(_database.Store, null);
            Assume.That(service.Create().Succeeded, Is.True);

            AdmissionRecord crafted;
            using (var reading = service.Read())
            {
                var key = reading.Material.AuthorityKey;
                crafted = AdmitReshaped(key, DateTimeOffset.UtcNow, der => NamingEd25519(der, key));
            }

            _database.Store.Remove(_database.Store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null));

            using (var panel = NewPanel(true))
            {
                Assert.That(() => panel.CreateAuthority(), Throws.Nothing);

                Assert.That(Button(panel, GateTab.CreateButtonName).Enabled, Is.False, "the page shows what Create did");
                Assert.That(Button(panel, GateTab.RecreateButtonName).Enabled, Is.True);
                Assert.That(() => panel.Describe(), Throws.Nothing, "which every page switch runs");
                Assert.That(panel.Reaches(panel.AdmittedClients())[crafted.AdmissionKey], Is.EqualTo(ClientReach.Stranger));
            }
        }

        [Test]
        public void A_record_naming_an_algorithm_no_candidate_can_check_leaves_choose_working()
        {
            // The same record in a database holding two authorities, where Choose threw it out of
            // the pin, past the filter `AuthorityChoice` carries, before the chooser opened.
            var candidates = Undecided();
            var key = CertificateBlob.Load(
                AuthorityDocument.Read(_database.Store.Get(AuthorityKeys.AuthorityKey(candidates[0])), null).Authority);
            var crafted = AdmitReshaped(key, DateTimeOffset.UtcNow, der => NamingEd25519(der, key));

            using (var panel = NewPanel(true))
            {
                AuthorityChoice asked = null;
                panel.AskWhichToKeep = (choice, name) =>
                {
                    asked = choice;
                    return candidates[1];
                };

                Assert.That(() => panel.ChooseAuthority(), Throws.Nothing);

                Assert.That(asked, Is.Not.Null, "the operator was asked");
                Assert.That(asked.Options.SelectMany(o => o.Clients).Select(c => c.ClientKey), Does.Not.Contain(crafted.ClientKey),
                    "and the record counted against neither candidate");
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.PresentHeadline));
            }
        }

        [Test]
        public void A_record_naming_an_algorithm_its_authority_cannot_check_does_not_stop_the_tab_being_built()
        {
            // The panel draws itself as it is built, and the pin threw `InvalidCastException` out
            // of that for such a record, which the injector took for an attach that failed: the
            // database's settings dialog had no tab, and nothing was counted anywhere.
            var service = new AuthorityService(_database.Store, null);
            Assume.That(service.Create().Succeeded, Is.True);

            AdmissionRecord crafted;
            using (var reading = service.Read())
            {
                var key = reading.Material.AuthorityKey;
                crafted = AdmitReshaped(key, DateTimeOffset.UtcNow, der => NamingEd25519(der, key));
            }

            AuthorityPanel panel = null;
            Assert.That(() => panel = NewPanel(true), Throws.Nothing);
            using (panel)
            {
                Assert.That(panel.Reaches(panel.AdmittedClients())[crafted.AdmissionKey], Is.EqualTo(ClientReach.Stranger));
            }
        }

        /// <summary>The record, written to the live store.</summary>
        private AdmissionRecord Admitted(AdmissionRecord record)
        {
            new AdmissionStore(_database.Store, null).Write(record);
            return record;
        }

        /// <summary>
        /// A client of the authority given, admitted in the live store at the moment given, its
        /// certificate put into a shape that authority would never write.
        /// </summary>
        private AdmissionRecord AdmitReshaped(CertifiedKey authority, DateTimeOffset when, Func<byte[], byte[]> reshape)
        {
            var issued = new AuthorityIssuer().IssueClient(authority, "agent-crafted", new[] { "ops" }).CertificateDer;
            using (var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(reshape(issued)))
            {
                return Admitted(AdmissionRecord.For(certificate, when, new[] { "ops" }));
            }
        }

        /// <summary>The certificate naming Ed25519 in both places, which no elliptic curve key can check.</summary>
        private static byte[] NamingEd25519(byte[] der, CertifiedKey signer)
        {
            var named = new Org.BouncyCastle.Asn1.X509.AlgorithmIdentifier(new Org.BouncyCastle.Asn1.DerObjectIdentifier("1.3.101.112"));
            return Reshaped.Naming(der, named, named, signer);
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
        public void The_band_is_measured_against_a_line_as_long_as_it_can_hold()
        {
            // J031: the line the tab is measured against is only the longest there is while it is
            // long enough to be cut, and nothing said so. Shortened, every test stayed green.
            var longest = AuthorityPanel.LongestResult();

            Assert.That(longest.Length, Is.EqualTo(Clipped.Limit + 3));
            Assert.That(longest, Does.EndWith("..."));
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
            using (var panel = new AuthorityPanel(null, new FakePrompt(true), _log))
            {
                Assert.That(panel.AdmittedClients().Records, Is.Empty);
                Assert.That(panel.Revoke(new string('a', 64)), Is.EqualTo("the database is not open"));
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
                    var value = table.GetControlFromPosition(1, row) as Label
                        ?? throw new InvalidOperationException("the row for " + name + " has no value");
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
            return Minted.Named("CN=" + name)
                .Lasting(DateTimeOffset.UtcNow.AddDays(-25), DateTimeOffset.UtcNow.AddDays(5))
                .SelfSigned()
                .Read();
        }

        [Test]
        public void The_clients_page_says_when_the_gate_cannot_see_the_authority_yet()
        {
            // The one place the waiting arrangement costs something, and it is invisible from where
            // the operator is standing: they pressed Create, the first page says there is an
            // authority, and a client pairing against the gate is answered out of the database
            // KeePass is holding, which has not been told.
            var pending = new PendingDatabase(_database, null);

            using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log))
            {
                Assert.That(panel.AuthorityIsWaiting, Is.False, "before anybody pressed anything");
                Assert.That(Label(panel, GateTab.InviteNoticeName).Visible, Is.False);

                panel.CreateAuthority();

                Assert.That(panel.AuthorityIsWaiting, Is.True);
                Assert.That(Label(panel, GateTab.InviteNoticeName).Text,
                    Is.EqualTo(GateTab.PendingAuthorityNotice));
                Assert.That(GateTab.PendingAuthorityNotice, Does.Contain("Press OK"));

                pending.Apply();
                panel.Describe();

                Assert.That(panel.AuthorityIsWaiting, Is.False, "once it is in the database");
            }
        }

        [Test]
        public void Nobody_can_be_invited_while_a_profile_name_is_still_waiting()
        {
            // `KPG.Profiles` is the one key this tab and pairing both write, and they write it
            // differently: the tab holds its version until OK, pairing writes live and saves.
            // `PendingStore` reconciles the two, so what is left is the direction no merge can
            // decide: a client approved for a profile the operator is part way through removing is
            // admitted to a name that OK takes out of the list, where it grants nothing.
            var pending = new PendingDatabase(_database, null);

            using (var panel = new AuthorityPanel(
                pending, new FakePrompt(true), _log, new InvitationBook(_log), null))
            {
                panel.CreateAuthority();
                pending.Apply();
                panel.Describe();

                Assume.That(panel.ProfilesAreWaiting, Is.False, "before anybody typed anything");
                Assume.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.True,
                    "so what the assertion below reads is this edit and not the authority");

                var names = ProfileNames.Read(null);
                Assume.That(names.Add("ops"), Is.Null);
                pending.PluginCustomData.Set(ProfileNames.CustomDataKey, names.Write());
                panel.Describe();

                Assert.That(panel.ProfilesAreWaiting, Is.True);
                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False,
                    "so a pairing cannot write the list this dialog is about to write over");

                // The band under the button, which is the only place this reason can be given. A
                // dead button says nothing about why it is dead, and this was the one of the three
                // reasons the page left unsaid. The band is sized to its text and hidden when
                // there is none, so the text is what says whether it is showing.
                Assert.That(Label(panel, GateTab.InviteNoticeName).Text,
                    Is.EqualTo(GateTab.PendingProfilesNotice));
                Assert.That(GateTab.PendingProfilesNotice, Does.Contain("Press OK"));

                pending.Apply();
                panel.Describe();

                Assert.That(panel.ProfilesAreWaiting, Is.False, "once it is in the database");
                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.True);
                Assert.That(Label(panel, GateTab.InviteNoticeName).Text, Is.Empty,
                    "the band costs a line of the page, so it goes when it has nothing to say");
            }
        }

        [Test]
        public void Inviting_while_a_profile_name_is_waiting_refuses_the_way_the_button_does()
        {
            // The button is decided by `Describe`, so a profile typed on the second page after
            // the clients page was last drawn leaves a live button over a state that must not
            // hand out a code. The method behind the button has to refuse it too, exactly as it
            // already refuses a waiting authority, or the guard is only as good as the last
            // redraw.
            var pending = new PendingDatabase(_database, null);

            using (var panel = new AuthorityPanel(
                pending, new FakePrompt(true), _log, new InvitationBook(_log), null))
            {
                panel.CreateAuthority();
                pending.Apply();
                panel.Describe();

                var names = ProfileNames.Read(null);
                Assume.That(names.Add("ops"), Is.Null);
                pending.PluginCustomData.Set(ProfileNames.CustomDataKey, names.Write());

                // Deliberately not redrawn, which is the state the button cannot cover.
                panel.InviteClient();

                Assert.That(Label(panel, GateTab.ResultLabelName).Text,
                    Is.EqualTo(InvitationText.ProfilesWaiting));
                Assert.That(Box(panel, GateTab.InvitationBoxName).Text, Is.Empty,
                    "no code was made, so there is nothing to copy and hand over");
            }
        }

        [Test]
        public void Nobody_can_be_invited_against_an_authority_the_gate_cannot_see_yet()
        {
            // A code handed out now either cannot be spent, or is spent against the authority this
            // dialog is about to replace: the client pairs, is issued a certificate, and OK
            // invalidates it. The notice says what to do; the dead button is what stops somebody
            // doing it anyway.
            var pending = new PendingDatabase(_database, null);

            using (var panel = new AuthorityPanel(
                pending, new FakePrompt(true), _log, new InvitationBook(_log), null))
            {
                panel.CreateAuthority();

                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);

                pending.Apply();
                panel.Describe();

                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.True);
            }
        }

        [Test]
        public void A_tab_on_a_database_that_holds_nothing_back_never_says_that()
        {
            // The panel is given whatever database it is given. One that writes as it goes has
            // nothing waiting, ever, and a notice about a dialog it is not on would be nonsense.
            using (var panel = NewPanel(true))
            {
                panel.CreateAuthority();

                Assert.That(panel.AuthorityIsWaiting, Is.False);
                Assert.That(Label(panel, GateTab.InviteNoticeName).Visible, Is.False);
            }
        }

        [Test]
        public void Both_tables_on_the_tab_have_something_to_be_read_aloud()
        {
            // A grid carries no text, so what UI automation reports as its name is whatever
            // `AccessibleName` says and nothing at all otherwise. `CONVENTIONS.md` names a grid as
            // one of the few controls that needs one, and names the trap in the same paragraph:
            // the tests that drive a real KeePass point at a control by its `AutomationId`, which
            // comes from `Name`, so the phrase must be a phrase rather than the identifier and
            // must not displace it.
            using (var panel = NewPanel(true))
            {
                var clients = ControlTree.FindByName<DataGridView>(panel, ClientList.ListName);
                var profiles = ControlTree.FindByName<DataGridView>(panel, ProfileList.ListName);

                Assert.That(clients, Is.Not.Null, "the clients page has no grid on it");
                Assert.That(profiles, Is.Not.Null, "the profiles page has no grid on it");

                Assert.That(clients.AccessibleName, Is.EqualTo(ClientList.ListLabel));
                Assert.That(profiles.AccessibleName, Is.EqualTo(ProfileList.ListLabel));

                Assert.That(clients.AccessibleName, Is.Not.EqualTo(clients.Name),
                    "a table announcing its own lookup name tells a listener nothing");
                Assert.That(profiles.AccessibleName, Is.Not.EqualTo(profiles.Name),
                    "a table announcing its own lookup name tells a listener nothing");
            }
        }

        [Test]
        public void Several_authorities_offer_the_chooser_in_the_place_create_has()
        {
            Undecided();

            using (var panel = NewPanel(true))
            {
                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.UndecidedHeadline));
                Assert.That(Shown(panel, Button(panel, GateTab.ChooseButtonName)), Is.True);
                Assert.That(Shown(panel, Button(panel, GateTab.CreateButtonName)), Is.False,
                    "there is nothing to create, and four buttons do not fit the row");
                Assert.That(Button(panel, GateTab.RecreateButtonName).Enabled, Is.True);
                Assert.That(Button(panel, GateTab.InviteButtonName).Enabled, Is.False);
            }
        }

        [Test]
        public void One_authority_puts_create_back_where_the_chooser_was()
        {
            using (var panel = NewPanel(true))
            {
                Assert.That(Shown(panel, Button(panel, GateTab.ChooseButtonName)), Is.False);
                Assert.That(Shown(panel, Button(panel, GateTab.CreateButtonName)), Is.True);
            }
        }

        /// <summary>
        /// Whether a control on the authority page is one an operator would see, for the reason
        /// `InvitationPanelTests.OnScreen` gives: `Visible` answers for the whole chain above it,
        /// and a page no window has drawn answers no whatever it was told.
        /// </summary>
        private static bool Shown(AuthorityPanel panel, Control control)
        {
            panel.CreateControl();

            var tabs = ControlTree.FindByName<TabControl>(panel, GateTab.SubTabsName);
            tabs.SelectedTab = ControlTree.FindByName<TabPage>(panel, GateTab.AuthorityPageName);
            panel.PerformLayout();

            return control.Visible;
        }

        [Test]
        public void Choosing_from_the_tab_waits_for_the_dialog_s_OK_like_everything_else_on_it()
        {
            var candidates = Undecided();

            foreach (var accepted in new[] { false, true })
            {
                var pending = new PendingDatabase(_database, null);
                using (var panel = new AuthorityPanel(pending, new FakePrompt(true), _log))
                {
                    panel.AskWhichToKeep = (choice, name) =>
                    {
                        Assert.That(choice.Options.Select(o => o.Fingerprint), Is.EquivalentTo(candidates));
                        return candidates[1];
                    };

                    panel.ChooseAuthority();

                    Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.PresentHeadline),
                        "the page reads what it is holding");
                    Assert.That(Digest(), Is.Null, "nothing lands before OK");

                    if (accepted)
                    {
                        pending.Apply();
                        Assert.That(Digest(), Is.EqualTo(candidates[1]));
                    }
                    else
                    {
                        pending.Discard();
                        Assert.That(Digest(), Is.Null, "Cancel means Cancel");
                    }
                }
            }
        }

        [Test]
        public void Choosing_nothing_changes_nothing()
        {
            Undecided();

            using (var panel = NewPanel(true))
            {
                panel.AskWhichToKeep = (choice, name) => null;

                panel.ChooseAuthority();

                Assert.That(Label(panel, GateTab.StatusLabelName).Text, Is.EqualTo(AuthorityDescription.UndecidedHeadline));
                Assert.That(Label(panel, GateTab.ResultLabelName).Text, Is.Empty);
            }
        }

        /// <summary>
        /// Makes the database undecided, as copies that each recreated an authority leave it once
        /// they are merged: authorities side by side and none of them retired, two here. Answers
        /// with their digests.
        /// </summary>
        private string[] Undecided()
        {
            var service = new AuthorityService(_database.Store, null);
            Assume.That(service.Create().Succeeded, Is.True);
            var first = _database.Store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            var document = _database.Store.Get(first);
            _database.Store.Remove(first);

            Assume.That(service.Create().Succeeded, Is.True);
            var second = _database.Store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            _database.Store.Plant(first, document);

            return new[] { AuthorityKeys.FingerprintInAuthority(first), AuthorityKeys.FingerprintInAuthority(second) };
        }

        private AuthorityPanel NewPanel(bool confirm)
        {
            return NewPanel(new FakePrompt(confirm));
        }

        private AuthorityPanel NewPanel(IOperatorPrompt prompt)
        {
            return new AuthorityPanel(_database, prompt, _log);
        }

        /// <summary>The authority the panel created, as the key it issues with.</summary>
        private CertifiedKey OwnAuthority()
        {
            var stored = _database.Store.Get(AuthorityKeys.AuthorityKey(Digest()));
            return CertificateBlob.Load(AuthorityDocument.Read(stored, null).Authority);
        }

        /// <summary>A client of the panel's own authority, for the caller to dispose.</summary>
        private X509Certificate2 IssueOwnClient(string name)
        {
            return new AuthorityIssuer().IssueClient(OwnAuthority(), name, new[] { "ops" }).Read();
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
}
