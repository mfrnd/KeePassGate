using System;
using System.IO;
using System.Threading;
using System.Xml;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A rule written at a node dialog's OK waiting for the next save in a real KeePass: the save
    /// over a file another copy replaced, answered with each of KeePass's three answers, KeePass
    /// asking before it closes, locks and exits, and KeePass's automatic saving after an entry is
    /// modified.
    /// </summary>
    /// <remarks>
    /// **Every rule is set in the entry dialog with nothing of the entry changed**, but in the case
    /// of automatic saving, so that the only change KeePass has to save is the store's own: the
    /// mark it asks by is the write's, `STORE.md` §629.
    ///
    /// **The other copy is the file as KeePass last saved it, changed as another gate changes
    /// it**: a rule set at a group the window's rules leave alone, through the product's own act.
    /// It replaces the file the way a file synchronizer does, written beside it and moved into its
    /// place, so that KeePass meets a file it did not write at the operator's next save.
    ///
    /// **Two starts of the same installation.** The first runs under KeePass's default options and
    /// ends by exiting; the second under "Automatically save after modifying an entry using the
    /// entry editing dialog", written into KeePass's configuration between the two.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveRuleWaitsForTheSaveTests
    {
        private const string Profile = "reader";

        private const string Root = "Invented Vault";

        /// <summary>A group of the invented tree that no rule of the window's is set at, where the copy of the third save sets a rule.</summary>
        private const string Archive = "Invented Archive";

        private const string ArchiveUuid = "AA000000000000000000000000000003";

        /// <summary>What the entry is renamed to in the case of automatic saving.</summary>
        private const string Renamed = "Shared Mailbox renamed";

        private LiveSession _session;
        private KeePassDriver _driver;
        private PluginLog _log;
        private GateAsker _asker;
        private IntPtr _main;

        private Replaced _synchronized;
        private Replaced _overwritten;
        private Replaced _canceled;
        private Replaced _synchronizedAfterCancel;
        private Ending _closing;
        private Ending _locking;
        private Ending _exiting;
        private string _endedInTheFile;

        private bool _autoWritten;
        private bool _autoSaved;
        private string _autoTitle;
        private string _autoInTheFile;
        private string _autoTitleInTheFile;
        private Ending _discarding;
        private string _discardedInTheFile;
        private bool _exitedAfterDiscarding;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            keePass.FillWithInventedContent();
            keePass.ChangeTheStore(database => LiveRules.AddGroup(database.RootGroup, Archive, ArchiveUuid));
            keePass.NameProfiles(Profile);

            using (var client = keePass.IssueClientCertificate("agent-reader"))
            {
                keePass.Admit(client, Profile);
                _asker = new GateAsker(keePass, client, Profile);
                _log = new PluginLog(keePass);
                try
                {
                    _driver = KeePassDriver.Start(keePass);
                    _log.WaitUntilServing(0, keePass);
                    _main = LiveRules.HandleOf(_driver.MainWindow);

                    _synchronized = SaveOver(keePass, "Synchronize", Root, "Shared Mailbox", ThrowawayKeePass.MailboxUuid, ThrowawayKeePass.AutomationUuid);
                    _overwritten = SaveOver(keePass, "Overwrite", "Automation", "Build agent", ThrowawayKeePass.BuildAgentUuid, ThrowawayKeePass.ServicesUuid);
                    _canceled = SaveOver(keePass, "Cancel", ThrowawayKeePass.InventedServices, "Fictitious API", ThrowawayKeePass.FictitiousApiUuid, ArchiveUuid);
                    _synchronizedAfterCancel = SaveAgain(keePass, "Synchronize", ThrowawayKeePass.FictitiousApiUuid, ArchiveUuid);
                    End(keePass);

                    AutomaticSaving(keePass);
                    var mark = _log.Mark;
                    _driver = KeePassDriver.Start(keePass);
                    _log.WaitUntilServing(mark, keePass);
                    _main = LiveRules.HandleOf(_driver.MainWindow);
                    AutoSaved(keePass);
                    Discarded(keePass);
                    _exitedAfterDiscarding = _driver.ShutDown();
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
        public void KeePass_asks_at_a_save_over_a_file_another_copy_replaced()
        {
            foreach (var replaced in new[] { _synchronized, _overwritten, _canceled, _synchronizedAfterCancel })
            {
                Assert.That(replaced.Written, Is.True, replaced.Answer + ": the plugin never logged the rule as written");
                Assert.That(replaced.Asked, Is.EqualTo("Overwrite the existing file?"), replaced.Answer);
                Assert.That(replaced.Answered, Is.True, replaced.Answer + ": the question did not close");
            }
        }

        [Test]
        public void Synchronize_keeps_the_window_s_rule_and_the_other_copy_s()
        {
            Assert.That(LiveRules.Marked(_synchronized.Title), Is.False, _synchronized.Title);
            Assert.That(_synchronized.WindowInTheFile, Is.EqualTo("Grants .R......"));
            Assert.That(_synchronized.OtherInTheFile, Is.EqualTo("Grants .....L.."));
            Assert.That(_synchronized.WindowAnswer, Is.EqualTo(".R......"));
            Assert.That(_synchronized.OtherAnswer, Is.EqualTo(".....L.."), "the other copy's rule is not in force once merged");
        }

        [Test]
        public void Overwrite_keeps_the_window_s_rule_and_loses_the_other_copy_s()
        {
            Assert.That(LiveRules.Marked(_overwritten.Title), Is.False, _overwritten.Title);
            Assert.That(_overwritten.WindowInTheFile, Is.EqualTo("Grants .R......"));
            Assert.That(_overwritten.OtherInTheFile, Is.EqualTo("none"));
            Assert.That(_overwritten.WindowAnswer, Is.EqualTo(".R......"));
            Assert.That(_overwritten.OtherAnswer, Is.EqualTo(GateAsker.Refused));
        }

        [Test]
        public void Cancel_writes_nothing_leaves_the_database_marked_and_the_rule_in_force()
        {
            Assert.That(LiveRules.Marked(_canceled.Title), Is.True, _canceled.Title);
            Assert.That(_canceled.WindowInTheFile, Is.EqualTo("none"), "the file is the other copy's");
            Assert.That(_canceled.OtherInTheFile, Is.EqualTo("Grants .....L.."));
            Assert.That(_canceled.WindowAnswer, Is.EqualTo(".R......"), "the gate serves what KeePass holds");
            Assert.That(_canceled.OtherAnswer, Is.EqualTo(GateAsker.Refused));
        }

        [Test]
        public void The_next_save_after_Cancel_answered_Synchronize_writes_both()
        {
            Assert.That(LiveRules.Marked(_synchronizedAfterCancel.Title), Is.False, _synchronizedAfterCancel.Title);
            Assert.That(_synchronizedAfterCancel.WindowInTheFile, Is.EqualTo("Grants .R......"));
            Assert.That(_synchronizedAfterCancel.OtherInTheFile, Is.EqualTo("Grants .....L.."));
            Assert.That(_synchronizedAfterCancel.OtherAnswer, Is.EqualTo(".....L.."));
        }

        [Test]
        public void KeePass_asks_before_closing_and_Cancel_keeps_the_database_open_and_marked()
        {
            Assert.That(_closing.Asked, Is.EqualTo("Save database changes before closing the file?"));
            Assert.That(_closing.Answered, Is.True);
            Assert.That(LiveRules.Marked(_closing.Title), Is.True, _closing.Title);
            Assert.That(_closing.Answer, Is.EqualTo(".R......"), "the gate stopped serving the rule");
        }

        [Test]
        public void KeePass_asks_before_locking_and_Cancel_keeps_the_database_unlocked_and_marked()
        {
            Assert.That(_locking.Asked, Is.EqualTo("Save database changes before locking the workspace?"));
            Assert.That(_locking.Answered, Is.True);
            Assert.That(LiveRules.Marked(_locking.Title), Is.True, _locking.Title);
            Assert.That(_locking.Answer, Is.EqualTo(".R......"), "the gate stopped serving the rule");
        }

        [Test]
        public void KeePass_asks_before_exiting_and_Save_writes_the_rule()
        {
            Assert.That(_exiting.Asked, Is.EqualTo("Save database changes before exiting KeePass?"));
            Assert.That(_exiting.Answered, Is.True);
            Assert.That(_exiting.Exited, Is.True, "KeePass did not exit cleanly after the answer");
            Assert.That(_endedInTheFile, Is.EqualTo("Grants .R......"));
        }

        [Test]
        public void Under_automatic_saving_after_an_entry_is_modified_the_rule_is_saved_with_the_entry()
        {
            Assert.That(_autoWritten, Is.True, "the plugin never logged the rule as written");
            Assert.That(_autoSaved, Is.True, "KeePass did not save the file after the entry dialog");
            Assert.That(LiveRules.Marked(_autoTitle), Is.False, _autoTitle);
            Assert.That(_autoInTheFile, Is.EqualTo("Grants .R...L.."));
            Assert.That(_autoTitleInTheFile, Is.EqualTo(Renamed));
        }

        [Test]
        public void A_database_closed_with_its_changes_discarded_leaves_the_rule_out_of_the_file()
        {
            Assert.That(_discarding.Asked, Is.EqualTo("Save database changes before closing the file?"));
            Assert.That(_discarding.Answered, Is.True);
            Assert.That(_discardedInTheFile, Is.EqualTo("Grants .R......"), "the rule as the earlier save wrote it");
            Assert.That(_exitedAfterDiscarding, Is.True, "KeePass did not exit cleanly");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        /// <summary>
        /// A rule set in an entry's dialog, the file replaced by another copy that sets a rule at a
        /// group, the operator's save answered as given, and each rule's fate.
        /// </summary>
        private Replaced SaveOver(ThrowawayKeePass keePass, string answer, string group, string title, string entry, string other)
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfEntry(_driver, group, title);
            tab.Give(Profile, ".R......");
            var replaced = new Replaced { Answer = answer };
            replaced.Written = LiveRules.Accept(_driver, tab.Dialog) && _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);

            LiveRules.ReplaceTheFile(keePass, database => StoreSeeds.Rule(
                DatabaseAuthority.Store(database), LiveRules.Node(other), (Profile, "L")));
            Answered(keePass, replaced, answer, entry, other);
            return replaced;
        }

        /// <summary>The operator's save again, with nothing replaced since, answered as given.</summary>
        private Replaced SaveAgain(ThrowawayKeePass keePass, string answer, string entry, string other)
        {
            var replaced = new Replaced { Answer = answer + " after Cancel", Written = true };
            Answered(keePass, replaced, answer, entry, other);
            return replaced;
        }

        private void Answered(ThrowawayKeePass keePass, Replaced replaced, string answer, string entry, string other)
        {
            LiveRules.PressSave(_driver);
            var question = LiveRules.Question(keePass, _main, LiveRules.WindowTimeout);
            replaced.Asked = question == null ? null : LiveRules.Asked(question);
            replaced.Answered = question != null && LiveRules.Answer(question, answer);

            // Cancel leaves the mark, and the other answers save: KeePass's title is what tells.
            replaced.Title = LiveRules.WaitForTitle(_driver, answer == "Cancel", LiveRules.SaveTimeout);
            replaced.WindowAnswer = _asker.AtEntry(entry);
            replaced.OtherAnswer = _asker.AtGroup(other);
            var rules = LiveRules.RulesIn(keePass.DatabasePath);
            replaced.WindowInTheFile = LiveRules.Versions(rules, LiveRules.Node(entry), Profile);
            replaced.OtherInTheFile = LiveRules.Versions(rules, LiveRules.Node(other), Profile);
        }

        /// <summary>
        /// A rule set in an entry's dialog and left unsaved; the database closed, then locked, each
        /// answered Cancel; then KeePass exited, answered Save.
        /// </summary>
        private void End(ThrowawayKeePass keePass)
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfEntry(_driver, ThrowawayKeePass.InventedServices, "Nonexistent Mail");
            tab.Give(Profile, ".R......");
            LiveRules.Accept(_driver, tab.Dialog);
            _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);

            _closing = Ask(keePass, () => Keyboard.SendControl('w'), "Cancel", ThrowawayKeePass.NonexistentMailUuid);
            _locking = Ask(keePass, () => Keyboard.SendControl('l'), "Cancel", ThrowawayKeePass.NonexistentMailUuid);

            _exiting = new Ending();
            keePass.Process.CloseMainWindow();
            var question = LiveRules.Question(keePass, _main, LiveRules.WindowTimeout);
            _exiting.Asked = question == null ? null : LiveRules.Asked(question);
            _exiting.Answered = question != null && LiveRules.Answer(question, "Save");
            _exiting.Exited = keePass.Process.WaitForExit(60000) && keePass.Process.ExitCode == 0;
            _endedInTheFile = LiveRules.Versions(keePass.ReadRules().Rules, LiveRules.Node(ThrowawayKeePass.NonexistentMailUuid), Profile);
        }

        /// <summary>A shortcut that ends the database pressed, KeePass's question answered, and what stands after it.</summary>
        private Ending Ask(ThrowawayKeePass keePass, Action press, string answer, string entry)
        {
            var ending = new Ending();
            Keyboard.Front(_driver.MainWindow);
            press();
            var question = LiveRules.Question(keePass, _main, LiveRules.WindowTimeout);
            ending.Asked = question == null ? null : LiveRules.Asked(question);
            ending.Answered = question != null && LiveRules.Answer(question, answer);
            ending.Title = LiveRules.Title(_driver);
            ending.Answer = answer == "Cancel" ? _asker.AtEntry(entry) : null;
            return ending;
        }

        /// <summary>
        /// The entry dialog with the entry's title edited and the rule changed, under automatic
        /// saving: KeePass saves right after the dialog, and the file holds both.
        /// </summary>
        private void AutoSaved(ThrowawayKeePass keePass)
        {
            var mark = _log.Mark;
            _driver.SelectGroup(Root);
            _driver.SelectEntry("Shared Mailbox");
            var dialog = _driver.OpenChosenEntryDialog();
            LiveRules.Type(dialog, "m_tbTitle", Renamed);
            var tab = RuleTab.Of(_driver, dialog);
            tab.SetRule(tab.Line("the profile's rule", line => line.Profile == Profile), "L", ".R...L..");

            var before = File.GetLastWriteTimeUtc(keePass.DatabasePath);
            _autoWritten = LiveRules.Accept(_driver, dialog) && _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);
            _autoSaved = Uia.WaitForOrNull(
                () => File.GetLastWriteTimeUtc(keePass.DatabasePath) != before ? "saved" : null,
                LiveRules.SaveTimeout) != null;
            _autoTitle = LiveRules.WaitForTitle(_driver, false, LiveRules.TitleTimeout);
            _autoInTheFile = LiveRules.Versions(LiveRules.RulesIn(keePass.DatabasePath), LiveRules.Node(ThrowawayKeePass.MailboxUuid), Profile);
            _autoTitleInTheFile = LiveRules.FromTheFile(keePass.DatabasePath, database =>
            {
                var entry = database.RootGroup.FindEntry(new PwUuid(KeePassLib.Utility.MemUtil.HexStringToByteArray(ThrowawayKeePass.MailboxUuid)), true);
                return entry == null ? null : entry.Strings.ReadSafe(PwDefs.TitleField);
            });
        }

        /// <summary>
        /// A rule changed in an entry's dialog with nothing of the entry changed, which KeePass
        /// saves nothing for after the dialog, and the database closed with its changes discarded.
        /// </summary>
        private void Discarded(ThrowawayKeePass keePass)
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfEntry(_driver, "Automation", "Build agent");
            tab.SetRule(tab.Line("the profile's rule", line => line.Profile == Profile), "L", ".R...L..");
            LiveRules.Accept(_driver, tab.Dialog);
            _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);
            LiveRules.WaitForTitle(_driver, true, LiveRules.TitleTimeout);

            _discarding = Ask(keePass, () => Keyboard.SendControl('w'), "Discard", ThrowawayKeePass.BuildAgentUuid);
            _discardedInTheFile = LiveRules.Versions(LiveRules.RulesIn(keePass.DatabasePath), LiveRules.Node(ThrowawayKeePass.BuildAgentUuid), Profile);
        }

        /// <summary>
        /// "Automatically save after modifying an entry using the entry editing dialog" turned on in
        /// the installation's configuration, which KeePass wrote as it exited, before the next
        /// start.
        /// </summary>
        private static void AutomaticSaving(ThrowawayKeePass keePass)
        {
            var path = Paths.Join(Path.GetDirectoryName(keePass.ExePath), "KeePass.config.xml");
            var xml = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                xml.Load(reader);
            }

            var application = Child(xml, xml.DocumentElement, "Application");
            Child(xml, application, "AutoSaveAfterEntryEdit").InnerText = "true";
            xml.Save(path);
        }

        private static XmlElement Child(XmlDocument xml, XmlElement parent, string name)
        {
            var found = parent.SelectSingleNode(name) as XmlElement;
            if (found != null)
            {
                return found;
            }

            var made = xml.CreateElement(name);
            parent.AppendChild(made);
            return made;
        }

        /// <summary>What a save over a replaced file left of the window's rule and the other copy's.</summary>
        private sealed class Replaced
        {
            public string Answer { get; set; }

            public bool Written { get; set; }

            public string Asked { get; set; }

            public bool Answered { get; set; }

            public string Title { get; set; }

            public string WindowAnswer { get; set; }

            public string OtherAnswer { get; set; }

            public string WindowInTheFile { get; set; }

            public string OtherInTheFile { get; set; }
        }

        /// <summary>What KeePass asked as the database was to end, and what stood after the answer.</summary>
        private sealed class Ending
        {
            public string Asked { get; set; }

            public bool Answered { get; set; }

            public string Title { get; set; }

            public string Answer { get; set; }

            public bool Exited { get; set; }
        }
    }
}
