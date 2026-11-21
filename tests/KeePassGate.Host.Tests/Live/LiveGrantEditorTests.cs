using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The end to end test of the grant editor: a rule made on the KPG tab of a real KeePass's
    /// group dialog, entry dialog and Add Entry dialog, in force for a connected client from the
    /// dialog's OK, and in the file once the operator has saved.
    /// </summary>
    /// <remarks>
    /// This is the test that closes the loop the editor is built around, `STORE.md` §629: the
    /// plugin writes the rule into its store as the dialog closes with OK and marks the database,
    /// the gate answers by it before anything is saved, KeePass saves it at the operator's save,
    /// and it is in the `.kdbx` when the process is gone. Cancel and Escape write nothing.
    ///
    /// **On the entry dialog, with nothing of the entry changed**, because there KeePass marks
    /// nothing at OK: the mark the save needs is the store's own. On the group dialog KeePass marks
    /// the database at every OK, so a write that marked nothing would be saved all the same. **The
    /// mark is told apart from a title marked whatever happened** by the title the same session
    /// reads after the same entry dialog closes with Cancel, a rule made on its tab and nothing
    /// written: unmarked there, before the session's first OK, and marked after the entry dialog's
    /// OK, which comes next.
    ///
    /// **The client is a real client**, an ordinary TLS client in this process holding a certificate
    /// the database's own authority issued, asking the gate what it may do at each node. Asking
    /// writes nothing, so the questions change nothing the session goes on to read.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveGrantEditorTests
    {
        private const string Profile = "reader";

        private const string Root = "Invented Vault";

        private const string Mailbox = "Shared Mailbox";

        private const string Automation = "Automation";

        /// <summary>The title of the entry the Add Entry dialog makes.</summary>
        private const string Added = "Invented Addition";

        /// <summary>The first dialog of the session, the entry dialog closed with Cancel over a rule made on its tab.</summary>
        private const string EntryCanceled = "the entry dialog, Cancel";

        /// <summary>What the client is answered with at each node, before any dialog is opened.</summary>
        private readonly Dictionary<string, string> _before = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Each dialog closed without OK, in the order closed.</summary>
        private readonly List<Discarded> _discarded = new List<Discarded>();

        private LiveSession _session;
        private KeePassDriver _driver;
        private PluginLog _log;
        private GateAsker _asker;

        private string _scopeOnTheTab;
        private string _fileBefore;
        private string _fileAfterDiscarding;
        private Accepted _entry;
        private Accepted _group;
        private Accepted _added;
        private string _addedUuid;
        private string _addedInherited;
        private string _servicesAfterTheAddition;
        private bool _exited;
        private StoredRules _stored;
        private string _addedTitleInFile;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            keePass.FillWithInventedContent();

            // Before KeePass opens the file, because the editor writes rules for profiles this
            // database names and offers no way to invent one. Naming it here is the same thing the
            // Profiles page does, done to a file rather than through a window.
            keePass.NameProfiles(Profile);

            // What the Add Entry dialog's tab shows arriving from the group the entry is added to.
            keePass.ChangeTheStore(database => StoreSeeds.Rule(
                DatabaseAuthority.Store(database), LiveRules.Node(ThrowawayKeePass.ServicesUuid), (Profile, "L")));

            using (var client = keePass.IssueClientCertificate("agent-reader"))
            {
                keePass.Admit(client, Profile);
                _asker = new GateAsker(keePass, client, Profile);
                _log = new PluginLog(keePass);
                try
                {
                    _driver = KeePassDriver.Start(keePass);
                    _log.WaitUntilServing(0, keePass);
                    Drive(keePass);
                }
                finally
                {
                    LiveRules.PointerAway();
                    if (_driver != null)
                    {
                        // As progress, which a set up that throws still prints.
                        TestContext.Progress.WriteLine(_driver.Diagnostics);
                    }
                }
            }

            var read = keePass.ReadRules();
            _stored = read.Rules;
            var added = keePass.ReadEntries().FirstOrDefault(entry => entry.Uuid.ToHexString() == _addedUuid);
            _addedTitleInFile = added == null ? null : added.Strings.ReadSafe(PwDefs.TitleField);

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
        }

        [Test]
        public void The_access_control_tab_is_on_the_real_entry_dialog()
        {
            Assert.That(_scopeOnTheTab, Is.EqualTo(GrantTab.EntryScope),
                "A real KeePass's entry dialog showed no " + GrantTab.Text + " tab with its line above the table.");
        }

        [Test]
        public void Cancel_and_Escape_write_nothing_and_leave_the_rules_as_they_were()
        {
            Assert.That(_discarded.Select(closed => closed.Case), Is.EqualTo(new[]
            {
                EntryCanceled, "the entry dialog, Escape", "the group dialog, Cancel", "the group dialog, Escape",
            }));

            foreach (var closed in _discarded)
            {
                Assert.That(closed.Closed, Is.True, closed.Case + ": the dialog did not close");
                Assert.That(closed.Written, Is.False, closed.Case + ": the plugin logged a write");
                Assert.That(LiveRules.Marked(closed.Title), Is.False, closed.Case + ": KeePass's title is '" + closed.Title + "'");
                Assert.That(closed.Answer, Is.EqualTo(_before[closed.Node]), closed.Case + ": the client is answered otherwise");
            }

            Assert.That(_fileAfterDiscarding, Is.EqualTo(_fileBefore), "the file changed");
        }

        [Test]
        public void A_rule_set_in_the_entry_dialog_is_in_force_before_any_save()
        {
            Assert.That(_before[Mailbox], Is.EqualTo(GateAsker.Refused), "the client was granted something at the entry before the rule");
            Assert.That(_entry.Written, Is.True, "The plugin never logged the rules as written after the entry dialog's OK.");
            Assert.That(_entry.Answers[Mailbox], Is.EqualTo(".R......"));
            Assert.That(_entry.InTheFile, Is.EqualTo("none"), "the file held the rule before any save");
        }

        [Test]
        public void The_entry_dialog_s_OK_marks_the_database_with_nothing_of_the_entry_changed()
        {
            // First the title after the same entry dialog's Cancel, a rule made on its tab and
            // nothing written, in the same session and before its first OK: a title marked
            // whatever happened fails here rather than passing below.
            var canceled = _discarded.Single(closed => closed.Case == EntryCanceled);
            Assert.That(canceled.Written, Is.False, "the entry dialog's Cancel wrote the rule");
            Assert.That(LiveRules.Marked(canceled.Title), Is.False,
                "KeePass's title shows an unsaved change after the entry dialog's Cancel, so a mark after the OK tells nothing: " + canceled.Title);

            Assert.That(_entry.Closed, Is.True, "The entry dialog was never accepted.");
            Assert.That(LiveRules.Marked(_entry.Title), Is.True,
                "KeePass's title shows no unsaved change after the OK, so the store's write did not mark the database: " + _entry.Title);
        }

        [Test]
        public void The_operator_s_save_writes_the_entry_s_rule()
        {
            Assert.That(LiveRules.Marked(_entry.TitleSaved), Is.False, "the save left the title '" + _entry.TitleSaved + "'");
            Assert.That(_entry.Saved, Is.EqualTo("Grants .R......"));
        }

        [Test]
        public void A_rule_set_in_the_group_dialog_is_in_force_before_any_save_there_and_below()
        {
            Assert.That(_before[Automation], Is.EqualTo(GateAsker.Refused));
            Assert.That(_before[ThrowawayKeePass.BuildAgentUuid], Is.EqualTo(GateAsker.Refused));
            Assert.That(_group.Written, Is.True, "The plugin never logged the rules as written after the group dialog's OK.");
            Assert.That(_group.Answers[Automation], Is.EqualTo(".R...L.."));
            Assert.That(_group.Answers[ThrowawayKeePass.BuildAgentUuid], Is.EqualTo(".R...L.."), "the entry below follows the group");
            Assert.That(_group.InTheFile, Is.EqualTo("none"), "the file held the rule before any save");
        }

        [Test]
        public void The_group_dialog_s_OK_marks_the_database_and_the_operator_s_save_writes_the_rule()
        {
            Assert.That(_group.Closed, Is.True, "The group dialog was never accepted.");
            Assert.That(LiveRules.Marked(_group.Title), Is.True, "KeePass's title is '" + _group.Title + "'");
            Assert.That(LiveRules.Marked(_group.TitleSaved), Is.False, "the save left the title '" + _group.TitleSaved + "'");
            Assert.That(_group.Saved, Is.EqualTo("Grants .R...L.."));
        }

        [Test]
        public void The_Add_Entry_dialog_s_tab_shows_what_arrives_from_the_group_the_entry_is_added_to()
        {
            Assert.That(_addedUuid, Does.Match("^[0-9A-F]{32}$"), "the Add Entry dialog showed no identifier");
            Assert.That(_addedInherited, Is.EqualTo(".....L.. " + GrantTab.InheritedWord));
        }

        [Test]
        public void A_rule_made_in_the_Add_Entry_dialog_is_in_force_for_the_entry_it_adds()
        {
            Assert.That(_added.Answers[Added + " before"], Is.EqualTo(GateAsker.Refused), "the entry is there before the OK");
            Assert.That(_added.Written, Is.True, "The plugin never logged the rules as written after the Add Entry dialog's OK.");
            Assert.That(LiveRules.Marked(_added.Title), Is.True, "KeePass's title is '" + _added.Title + "'");
            Assert.That(_added.Answers[Added], Is.EqualTo(".R...L.."));
            Assert.That(_servicesAfterTheAddition, Is.EqualTo(".....L.."), "the group the entry was added to changed");
            Assert.That(_added.Saved, Is.EqualTo("Grants .R...L.."));
        }

        [Test]
        public void Every_rule_is_in_the_file_after_KeePass_has_gone()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly.");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(ThrowawayKeePass.MailboxUuid), Profile), Is.EqualTo("Grants .R......"));
            Assert.That(_stored.At(LiveRules.Node(ThrowawayKeePass.MailboxUuid)).Switch.Refuses, Is.True, "the box that was ticked is not in the file");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(ThrowawayKeePass.AutomationUuid), Profile), Is.EqualTo("Grants .R...L.."));
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(_addedUuid ?? string.Empty), Profile), Is.EqualTo("Grants .R...L.."));
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(ThrowawayKeePass.ServicesUuid), Profile), Is.EqualTo("Grants .....L.."));
            Assert.That(_addedTitleInFile, Is.EqualTo(Added), "the entry the Add Entry dialog made is not in the file");
        }

        [Test]
        public void The_rule_answers_for_a_client_holding_that_profile()
        {
            // Read as the walk reads a node's rules, so this is about permission rather than about
            // the records.
            var stored = _stored.At(LiveRules.Node(ThrowawayKeePass.MailboxUuid));
            Assert.That(stored.Row(Profile).Answers, Is.True);
            Assert.That(VerbMask.Of(stored.Row(Profile).Grants), Is.EqualTo(".R......"));
            Assert.That(stored.Row("somebody-else").Versions, Is.Empty);
            Assert.That(stored.Switch.Refuses, Is.True, "the box that was ticked refuses every profile the node does not name");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private void Drive(ThrowawayKeePass keePass)
        {
            _before[Mailbox] = _asker.AtEntry(ThrowawayKeePass.MailboxUuid);
            _before[Automation] = _asker.AtGroup(ThrowawayKeePass.AutomationUuid);
            _before[ThrowawayKeePass.BuildAgentUuid] = _asker.AtEntry(ThrowawayKeePass.BuildAgentUuid);
            _fileBefore = Digest(keePass.DatabasePath);

            Discard(EntryCanceled, true, true);
            Discard("the entry dialog, Escape", true, false);
            Discard("the group dialog, Cancel", false, true);
            Discard("the group dialog, Escape", false, false);
            _fileAfterDiscarding = Digest(keePass.DatabasePath);

            // OK rather than Escape, deliberately, and it is what this test is about: the rule is
            // written into the plugin's store as the dialog closes with OK, and at no other close.
            var mark = _log.Mark;
            var entry = RuleTab.OfEntry(_driver, Root, Mailbox);
            _scopeOnTheTab = entry.Scope;
            entry.AddRule(Profile, "R", ".R......");
            entry.TickBox();
            _entry = Accept(entry, mark, ThrowawayKeePass.MailboxUuid);
            _entry.Answers[Mailbox] = _asker.AtEntry(ThrowawayKeePass.MailboxUuid);
            Save(keePass, _entry, ThrowawayKeePass.MailboxUuid);

            mark = _log.Mark;
            var group = RuleTab.OfGroup(_driver, Automation);
            group.AddRule(Profile, "RL", ".R...L..");
            _group = Accept(group, mark, ThrowawayKeePass.AutomationUuid);
            _group.Answers[Automation] = _asker.AtGroup(ThrowawayKeePass.AutomationUuid);
            _group.Answers[ThrowawayKeePass.BuildAgentUuid] = _asker.AtEntry(ThrowawayKeePass.BuildAgentUuid);
            Save(keePass, _group, ThrowawayKeePass.AutomationUuid);

            Add(keePass);

            _exited = _driver.ShutDown();
        }

        /// <summary>
        /// The Add Entry dialog on the group of invented services: a title typed, the identifier
        /// the new entry will have read off the Properties page, and a rule chosen on the row that
        /// arrives from the group.
        /// </summary>
        private void Add(ThrowawayKeePass keePass)
        {
            var mark = _log.Mark;
            _driver.SelectGroup(ThrowawayKeePass.InventedServices);
            var dialog = _driver.OpenAddEntryDialog();
            LiveRules.Type(dialog, "m_tbTitle", Added);
            LiveRules.Page(dialog, "Properties");

            // KeePass shows the identifier in hexadecimal and then, after a comma, in base64.
            var shown = _driver.ValueOf(dialog, "m_tbUuid") ?? string.Empty;
            _addedUuid = shown.Split(',')[0].Trim();

            var tab = RuleTab.Of(_driver, dialog);
            var inherited = tab.Line("the profile's row", line => line.Profile == Profile);
            _addedInherited = inherited.Mask + " " + inherited.State;
            var before = _asker.AtEntry(_addedUuid);

            tab.SetRule(inherited, "R", ".R...L..");
            _added = Accept(tab, mark, _addedUuid);
            _added.Answers[Added + " before"] = before;
            _added.Answers[Added] = _asker.AtEntry(_addedUuid);
            _servicesAfterTheAddition = _asker.AtGroup(ThrowawayKeePass.ServicesUuid);
            Save(keePass, _added, _addedUuid);
        }

        /// <summary>
        /// A rule made on a node's tab and the dialog closed without OK: whether the plugin logged a
        /// write, KeePass's title, and what the client is answered at the node afterwards.
        /// </summary>
        private void Discard(string name, bool entry, bool cancel)
        {
            var mark = _log.Mark;
            var tab = entry ? RuleTab.OfEntry(_driver, Root, Mailbox) : RuleTab.OfGroup(_driver, Automation);
            tab.AddRule(Profile, entry ? "R" : "RL", entry ? ".R......" : ".R...L..");
            if (entry)
            {
                tab.TickBox();
            }

            var closed = cancel ? LiveRules.Cancel(_driver, tab.Dialog) : LiveRules.Escape(tab.Dialog);

            // The client first: the gate answers on KeePass's own thread, so by the time it has,
            // KeePass has handled the dialog's result, and the title and the log are what it left.
            var node = entry ? Mailbox : Automation;
            var answer = entry ? _asker.AtEntry(ThrowawayKeePass.MailboxUuid) : _asker.AtGroup(ThrowawayKeePass.AutomationUuid);
            _discarded.Add(new Discarded
            {
                Case = name,
                Node = node,
                Closed = closed,
                Answer = answer,
                Title = LiveRules.Title(_driver),
                Written = _log.Since(mark).IndexOf(LiveRules.WrittenLine, StringComparison.Ordinal) >= 0,
            });
        }

        /// <summary>
        /// The dialog accepted, the plugin's write waited for by its line in the log, KeePass's
        /// title once it shows the mark, and the node's rule in the file before any save.
        /// </summary>
        private Accepted Accept(RuleTab tab, int mark, string uuid)
        {
            var accepted = new Accepted { Closed = LiveRules.Accept(_driver, tab.Dialog) };
            accepted.Written = accepted.Closed && _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);
            accepted.Title = LiveRules.WaitForTitle(_driver, true, LiveRules.TitleTimeout);
            accepted.InTheFile = LiveRules.Versions(LiveRules.RulesIn(_session.KeePass.DatabasePath), LiveRules.Node(uuid), Profile);
            return accepted;
        }

        /// <summary>The operator's save, and the node's rule in the file after it.</summary>
        private void Save(ThrowawayKeePass keePass, Accepted accepted, string uuid)
        {
            accepted.TitleSaved = LiveRules.Save(_driver);
            accepted.Saved = LiveRules.Versions(LiveRules.RulesIn(keePass.DatabasePath), LiveRules.Node(uuid), Profile);
        }

        /// <summary>The file's bytes as a digest, read from a copy.</summary>
        private static string Digest(string path)
        {
            var copy = path + ".digest";
            LiveRules.CopyWhenFree(path, copy);
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(copy))
                {
                    return Hex.Of(sha.ComputeHash(stream));
                }
            }
            finally
            {
                File.Delete(copy);
            }
        }

        /// <summary>What a dialog closed without OK left.</summary>
        private sealed class Discarded
        {
            public string Case { get; set; }

            public string Node { get; set; }

            public bool Closed { get; set; }

            public bool Written { get; set; }

            public string Title { get; set; }

            public string Answer { get; set; }
        }

        /// <summary>What a dialog accepted with a rule left, from the OK to the operator's save.</summary>
        private sealed class Accepted
        {
            public bool Closed { get; set; }

            public bool Written { get; set; }

            public string Title { get; set; }

            /// <summary>What the client is answered at each node named, after the OK and before any save.</summary>
            public Dictionary<string, string> Answers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>The node's rule in the file after the OK and before any save.</summary>
            public string InTheFile { get; set; }

            public string TitleSaved { get; set; }

            /// <summary>The node's rule in the file after the operator's save.</summary>
            public string Saved { get; set; }
        }
    }
}
