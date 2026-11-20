using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassGate.Testing;
using KeePassGate;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The grant editor against KeePass's real group and entry dialogs and the plugin's store in a
    /// real database: what the tab reads, why the tab takes no press, and what an OK writes.
    /// </summary>
    /// <remarks>
    /// `GrantSheetTests` and `GrantPanelTests` settle what the editor decides. This settles what
    /// only KeePass and a real database can answer: **a rule changed on the tab is written into
    /// the plugin's store as the dialog closes with OK**, marking the database, and nothing of the
    /// node itself changes for it.
    ///
    /// KeePass's dialogs are constructed and read, never shown: shown outside a running KeePass
    /// they throw inside the message pump, `KeePassDialogs.Close`. Where the close itself is what
    /// is measured, the tab KeePass's dialog gives is put on a plain modal form, whose own loop
    /// raises the close as KeePass's dialog raises it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class GrantEditorInRealDialogsTests
    {
        private TemporaryDatabase _database;
        private ImageList _icons;
        private WarningLog _log;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _log = new WarningLog();

            // The entry dialog refuses a null one, and what is in it is never looked at here.
            _icons = new ImageList();
        }

        [TearDown]
        public void TearDown()
        {
            _icons.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_group_dialog_is_read_as_the_group_it_is_about()
        {
            var group = GroupUnderRoot("Fictitious");

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                var node = NodeDialog.Of(dialog, null);

                Assert.That(node.Group, Is.SameAs(group));
                Assert.That(node.Entry, Is.Null);
                Assert.That(node.Node, Is.EqualTo(Hex.Of(group.Uuid.UuidBytes)));
                Assert.That(node.Parent, Is.SameAs(_database.RootGroup));
                Assert.That(node.ReadOnlyReason, Is.Null);
                Assert.That(node.Scope, Is.EqualTo(GrantTab.GroupScope));
            }
        }

        [Test]
        public void The_root_group_s_dialog_is_headed_by_a_line_of_its_own()
        {
            // Nothing is inherited at the root group, so the line about inherited rules is no
            // line for the root.
            using (var dialog = GroupDialog(_database.RootGroup))
            {
                Assert.That(NodeDialog.Of(dialog, null).Scope, Is.EqualTo(GrantTab.RootScope));
            }
        }

        [Test]
        public void Several_entries_edited_together_take_no_press_and_show_no_entry_s_rules()
        {
            // KeePass merges the selection into a synthetic entry and writes the result back over
            // all of them, so that entry is no entry the store holds rules for.
            var group = GroupUnderRoot("Fictitious");
            var merged = new object[] { new[] { EntryIn(group), EntryIn(group) }, _database.Database, null };
            var context = typeof(PwEntryForm).Assembly
                .GetType("KeePass.Util.MultipleValues.MultipleValuesEntryContext")
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0]
                .Invoke(merged);

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx((PwEntry)merged[2], PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                typeof(PwEntryForm).GetProperty("MultipleValuesEntryContext").SetValue(dialog, context, null);

                var tab = Tabs().TabFor(dialog);

                Assert.That(tab.Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.SeveralEntries));
                Assert.That(tab.Sheet.Rows, Is.Empty);
                Assert.That(tab.Rules, Is.Null, "nothing is written from such a tab");
            }
        }

        [Test]
        public void A_dialog_that_names_no_database_takes_no_press_and_is_logged_once()
        {
            var group = GroupUnderRoot("Fictitious");

            using (var dialog = new GroupForm())
            using (var window = new Control())
            {
                dialog.InitEx(group, false, null, _database.Database);
                typeof(GroupForm).GetField(KeePassContract.NodeFormDatabaseField, BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(dialog, null);

                using (new GrantPanelFactory(Tabs(), _log, null, new Telling(), window, null).CreatePanel(dialog))
                {
                    Assert.That(_log.Warnings, Has.Count.EqualTo(1), string.Join(" | ", _log.Warnings));
                }

                Assert.That(Tabs().TabFor(dialog).Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.NoDatabase));
            }
        }

        [Test]
        public void A_dialog_about_no_node_states_its_reason_once()
        {
            using (var dialog = new Form())
            using (var window = new Control())
            using (var panel = new GrantPanelFactory(Tabs(), null, null, new Telling(), window, null).CreatePanel(dialog))
            {
                var scope = ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName);
                var warning = ControlTree.FindByName<Label>(panel, GrantTab.WarningLabelName);

                Assert.That(warning.Text, Is.EqualTo(GrantTab.NoNode));
                Assert.That(warning.Visible, Is.True);
                Assert.That(scope.Visible, Is.False, "the line at the top stands over no tab that takes no change");
            }
        }

        [Test]
        public void A_database_with_no_authority_yet_takes_no_press()
        {
            using (var dialog = GroupDialog(_database.RootGroup))
            {
                var tab = Tabs().TabFor(dialog);

                Assert.That(tab.Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.NoAuthorityYet));
                Assert.That(tab.Rules, Is.Null);
            }
        }

        [Test]
        public void A_session_whose_dialogs_are_not_watched_takes_no_press()
        {
            Seed();
            using (var dialog = GroupDialog(_database.RootGroup))
            {
                var tab = new NodeDialogs(_log, null).TabFor(dialog);

                Assert.That(tab.Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.NotWatched));
                Assert.That(tab.Rules, Is.Null);
            }
        }

        [Test]
        public void A_group_of_the_plugin_s_store_takes_no_press()
        {
            Seed();
            var store = _database.RootGroup.Groups.Single(group => StoreNodes.Holds(group));

            using (var dialog = GroupDialog(store))
            {
                Assert.That(Tabs().TabFor(dialog).Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.StoreNode));
            }
        }

        [Test]
        public void A_history_revision_takes_no_press_and_shows_the_rules_of_the_entry_as_the_rules_stand_now()
        {
            Seed();
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            StoreSeeds.Rule(Store(), NodeOf(entry), ("reader", "R"));
            entry.CreateBackup(_database.Database);
            StoreSeeds.Rule(Store(), NodeOf(entry), ("reader", "RL"));
            var revision = entry.History.GetAt(0);

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(revision, PwEditMode.ViewReadOnlyEntry, _database.Database, _icons, false, false);

                var tab = Tabs().TabFor(dialog);

                Assert.That(tab.Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.EarlierVersion));
                Assert.That(tab.Rules, Is.Null);
                Assert.That(tab.Sheet.Rows.Single().Mask, Is.EqualTo(".R...L.."), "the rule of the entry as it stands now");
            }
        }

        [Test]
        public void An_earlier_version_read_from_the_file_shows_every_row_the_entry_shows_as_the_rules_stand_now()
        {
            // KeePass reads a revision from the file with no group above it, and its History view
            // opens the entry dialog over the revision as read only: a tab reading what arrives at
            // the revision's own group would show no rule from above, no limit and no rule of a
            // name gone. The tab finds the entry as the entry stands by the revision's identifier.
            Seed();
            foreach (var name in new[] { "reader", "deploy", "ops" })
            {
                StoreSeeds.Name(Store(), name);
            }

            var group = GroupUnderRoot("Fictitious");
            var entry = EntryIn(group);
            StoreSeeds.Rule(Store(), NodeOf(group), ("reader", "RL"), ("deploy", "CRUL"), ("retired", "R"));
            StoreSeeds.Rule(Store(), NodeOf(entry), ("ops", "CR"));
            StoreSeeds.TakeOff(Store(), NodeOf(entry), "deploy", "R");
            entry.CreateBackup(_database.Database);
            _database.SaveCloseAndReopen();

            var current = _database.RootGroup.FindEntry(entry.Uuid, true);
            Assert.That(current, Is.Not.Null);
            var revision = current.History.GetAt(0);
            Assert.That(revision.ParentGroup, Is.Null, "a revision read from the file knows no group");

            using (var shown = EntryDialog(current, PwEditMode.EditExistingEntry))
            using (var dialog = EntryDialog(revision, PwEditMode.ViewReadOnlyEntry))
            {
                var now = Tabs().TabFor(shown).Sheet.Rows.Select(row => row.ToString()).ToList();
                var tab = Tabs().TabFor(dialog);

                Assert.That(tab.Sheet.ReadOnlyReason, Is.EqualTo(GrantTab.EarlierVersion));
                Assert.That(tab.Rules, Is.Null, "nothing is written from such a tab");
                Assert.That(now.Count, Is.EqualTo(5), string.Join(" | ", now));
                Assert.That(tab.Sheet.Rows.Select(row => row.ToString()), Is.EqualTo(now), "every row of the entry as the rules stand now");
                Assert.That(tab.Sheet.Rows.Select(row => row.Kind), Is.SupersetOf(new[] { GrantRowKind.Own, GrantRowKind.Inherited, GrantRowKind.Limit }));
                Assert.That(tab.Sheet.Rows.SelectMany(row => row.States), Does.Contain(GrantState.NoProfile));
            }
        }

        [Test]
        public void The_tab_shows_the_node_s_rules_and_what_arrives_from_the_group_above()
        {
            Seed();
            var group = GroupUnderRoot("Fictitious");
            var entry = EntryIn(group);
            StoreSeeds.Rule(Store(), NodeOf(group), ("reader", "RL"));
            StoreSeeds.Rule(Store(), NodeOf(entry), ("ops", "CR"));

            using (var dialog = EntryDialog(entry, PwEditMode.EditExistingEntry))
            {
                var tab = Tabs().TabFor(dialog);

                Assert.That(tab.Sheet.CanEdit, Is.True);
                Assert.That(tab.Sheet.Rows.Select(row => row.Profile + " " + row.Mask + " " + row.Kind), Is.EqualTo(new[]
                {
                    "ops CR...... Own",
                    "reader .R...L.. Inherited",
                }));
            }
        }

        [Test]
        public void The_Add_Entry_dialog_shows_what_arrives_at_the_group_the_entry_is_added_to()
        {
            Seed();
            var group = GroupUnderRoot("Fictitious");
            StoreSeeds.Rule(Store(), NodeOf(group), ("reader", "RL"));

            // Add Entry gives its entry the group and puts the entry into the group at OK, read off
            // KeePass 2.62's IL: `MainForm.AddEntryEx` sets `PwEntry.ParentGroup` before `InitEx`
            // and calls `PwGroup.AddEntry` after the dialog. Set here as that method sets it,
            // because `AddEntryEx` needs KeePass's main window, which this process has none of,
            // and the setter is internal to KeePass.
            var entry = new PwEntry(true, true);
            typeof(PwEntry).GetProperty("ParentGroup").SetValue(entry, group, null);
            Assume.That(group.Entries.UCount, Is.Zero);

            using (var dialog = EntryDialog(entry, PwEditMode.AddNewEntry))
            {
                var tab = Tabs().TabFor(dialog);

                Assert.That(tab.Sheet.Rows.Single().Kind, Is.EqualTo(GrantRowKind.Inherited));
                Assert.That(tab.Sheet.Rows.Single().Mask, Is.EqualTo(".R...L.."));

                tab.Sheet.Set("reader", ".R......");
                Assert.That(tab.Rules.Write(tab.Sheet.Changes), Is.Null);
                Assert.That(Read().At(NodeOf(entry)).Row("reader").Grants.Select(VerbMask.LetterOf), Is.EquivalentTo(new[] { 'R' }),
                    "the rule of the entry being added, under the entry's identifier");
            }
        }

        [Test]
        public void OK_writes_the_rule_into_the_store_and_marks_the_database_with_the_entry_unchanged()
        {
            Seed();
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            var modified = entry.LastModificationTime;
            var history = entry.History.UCount;
            var records = StoreEntries();
            _database.Database.Modified = false;

            using (var dialog = EntryDialog(entry, PwEditMode.EditExistingEntry))
            {
                var tab = Tabs().TabFor(dialog);
                Assert.That(tab.Sheet.Set("reader", ".R...L.."), Is.Null);
                Assert.That(StoreEntries(), Is.EqualTo(records), "a press writes nothing");

                Assert.That(tab.Rules.Write(tab.Sheet.Changes), Is.Null);
            }

            Assert.That(_database.Database.Modified, Is.True, "the store's write marks the database");
            Assert.That(StoreEntries(), Is.GreaterThan(records), "a version and a summary entry");
            Assert.That(entry.LastModificationTime, Is.EqualTo(modified), "a rule is no part of its node");
            Assert.That(entry.History.UCount, Is.EqualTo(history));
            Assert.That(entry.CustomData.Count, Is.Zero, "nothing in the node's custom data");

            var row = Read().At(NodeOf(entry)).Row("reader");
            Assert.That(row.Answers, Is.True, "the gate reads the rule at its next hold");
            Assert.That(VerbMask.Of(row.Grants), Is.EqualTo(".R...L.."));
        }

        [Test]
        public void OK_on_the_dialog_writes_once_the_dialog_has_closed_and_Cancel_writes_nothing()
        {
            Seed();
            var group = GroupUnderRoot("Fictitious");

            foreach (var result in new[] { DialogResult.Cancel, DialogResult.OK })
            {
                var records = StoreEntries();
                var prompt = new Telling();
                using (var groupDialog = GroupDialog(group))
                {
                    var tab = Tabs().TabFor(groupDialog);
                    Closed(tab, prompt, result, panel => panel.SetGrant("reader", ".R......"));
                }

                Assert.That(prompt.Told, Is.Zero, result.ToString());
                if (result == DialogResult.OK)
                {
                    Assert.That(StoreEntries(), Is.GreaterThan(records));
                    Assert.That(VerbMask.Of(Read().At(NodeOf(group)).Row("reader").Grants), Is.EqualTo(".R......"));
                }
                else
                {
                    Assert.That(StoreEntries(), Is.EqualTo(records), "Cancel and Escape write nothing");
                }
            }
        }

        [Test]
        public void A_refused_OK_adds_nothing_and_is_told_once_the_dialog_has_closed()
        {
            Seed();
            var group = GroupUnderRoot("Fictitious");
            var records = StoreEntries();
            var prompt = new Telling();
            const string Busy = "KeePass is saving a database";

            using (var groupDialog = GroupDialog(group))
            {
                var tab = new NodeDialogs(_log, Writer(() => Busy)).TabFor(groupDialog);
                Closed(tab, prompt, DialogResult.OK, panel => panel.SetGrant("reader", ".R......"));
            }

            Assert.That(StoreEntries(), Is.EqualTo(records), "a write refused adds nothing");
            Assert.That(prompt.Told, Is.EqualTo(1));
            Assert.That(prompt.News, Is.EqualTo(GrantTab.NotKept + Busy));
            Assert.That(_log.Warnings, Has.Some.Contains(NodeOf(group).Substring(0, 8)), "the log names the node");
        }

        [Test]
        public void The_message_after_a_refused_OK_waits_for_a_save_of_KeePass_s_own_to_end()
        {
            // KeePass saves right after an entry's dialog where it saves after every edit, and
            // pumps as it saves: the message is not shown inside that save, and is shown at the
            // first Idle once KeePass is no longer busy, asked as the writer asks it.
            Seed();
            var group = GroupUnderRoot("Fictitious");
            var prompt = new Telling();
            var saving = false;
            const string Refused = "the database maintenance dialog is open on this database";

            using (var groupDialog = GroupDialog(group))
            using (var window = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 })
            using (var dialog = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 })
            {
                window.Show();
                var tab = new NodeDialogs(_log, Writer(() => Refused)).TabFor(groupDialog);
                Assume.That(tab.Sheet.CanEdit, Is.True, tab.Sheet.ReadOnlyReason);
                var factory = new GrantPanelFactory(new TheTab(tab), null, null, prompt, window, null)
                {
                    Busy = () => saving ? "KeePass is in the middle of an operation of its own, such as saving" : null,
                };

                var panel = (GrantPanel)factory.CreatePanel(dialog);
                dialog.Controls.Add(panel);
                dialog.Shown += (sender, e) => dialog.BeginInvoke(new Action(() =>
                {
                    panel.SetGrant("reader", ".R......");
                    saving = true;
                    dialog.DialogResult = DialogResult.OK;
                }));

                dialog.ShowDialog();
                for (var i = 0; i < 8; i++)
                {
                    Application.DoEvents();
                }

                Assert.That(prompt.Told, Is.Zero, "the message stood inside the save");

                saving = false;
                Application.RaiseIdle(EventArgs.Empty);

                Assert.That(prompt.Told, Is.EqualTo(1), "the message was not shown once the save had ended");
                Assert.That(prompt.News, Is.EqualTo(GrantTab.NotKept + Refused));
            }
        }

        [Test]
        public void A_rule_whose_new_version_does_not_fit_is_told_as_not_kept_and_the_rest_is_written()
        {
            Seed();
            var group = GroupUnderRoot("Fictitious");
            Crowded(group, "reader");

            using (var groupDialog = GroupDialog(group))
            {
                var tab = Tabs().TabFor(groupDialog);
                Assert.That(tab.Sheet.CanEdit, Is.True, tab.Sheet.ReadOnlyReason);
                Assert.That(tab.Sheet.Rows.Count(row => row.Profile == "reader" && row.Kind == GrantRowKind.Lost), Is.EqualTo(Crowd));
                Assert.That(tab.Sheet.Set("reader", ".RU....."), Is.Null);
                Assert.That(tab.Sheet.Set("ops", ".R......"), Is.Null);

                Assert.That(tab.Rules.Write(tab.Sheet.Changes), Is.EqualTo(
                    GrantTab.NotAllKept + Environment.NewLine + GrantTab.NoRoom + Environment.NewLine + GrantTab.ShownAsKept));
            }

            var rules = Read().At(NodeOf(group));
            Assert.That(rules.Row("reader").Lost, Has.Count.EqualTo(Crowd), "the rule left stands as it was");
            Assert.That(VerbMask.Of(rules.Row("ops").Grants), Is.EqualTo(".R......"), "the rest of the OK is written");
            Assert.That(_log.Warnings, Has.Some.Contains("reader"), "the log names the rule");
            Assert.That(_log.Warnings, Has.Some.Contains(NodeOf(group).Substring(0, 8)), "and the node");
        }

        /// <summary>How many lost versions a crowded rule holds: more than a single version can name in a string.</summary>
        private const int Crowd = (StoreFormat.MaxWritten / 32) + 1;

        /// <summary>
        /// A rule of the profile at the group in a single version, and beside the version
        /// <see cref="Crowd"/> versions lost: deletion records of identifiers a version of the
        /// row's object could have, which nothing names. No version naming every version shown fits
        /// a string.
        /// </summary>
        private void Crowded(PwGroup group, string profile)
        {
            StoreSeeds.Rule(Store(), NodeOf(group), (profile, "R"));
            using (var reading = new KdbxStore(_database.Database, GateVersion.Current, null).Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    var objectName = StoreComposer.RowObject(NodeOf(group), profile);
                    var place = StoreComposer.Version(key, objectName, StoreComposer.Content(("doc", "row:R"))).Hex.Substring(0, 20);
                    for (var at = 0; at < Crowd; at++)
                    {
                        var lost = StoreComposer.BytesOf(place + at.ToString("x12", System.Globalization.CultureInfo.InvariantCulture));
                        _database.Database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(lost), DateTime.UtcNow));
                    }
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }
        }

        /// <summary>
        /// The tab a KeePass dialog gives, on a plain modal form closed with this result once the
        /// press has been made on the tab, and the message loop run until what was posted ran.
        /// </summary>
        private static void Closed(NodeTab tab, Telling prompt, DialogResult result, Action<GrantPanel> press)
        {
            using (var window = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 })
            using (var dialog = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 })
            {
                window.Show();
                var factory = new GrantPanelFactory(new TheTab(tab), null, null, prompt, window, null);
                var panel = (GrantPanel)factory.CreatePanel(dialog);
                dialog.Controls.Add(panel);
                dialog.Shown += (sender, e) => dialog.BeginInvoke(new Action(() =>
                {
                    press(panel);
                    dialog.DialogResult = result;
                }));

                dialog.ShowDialog();
                for (var i = 0; i < 8; i++)
                {
                    Application.DoEvents();
                }
            }
        }

        private NodeDialogs Tabs()
        {
            return new NodeDialogs(_log, Writer(() => null));
        }

        /// <summary>A writer over this thread, saving nothing, answering busy as told.</summary>
        private DatabaseWriter Writer(Func<string> busy)
        {
            return new DatabaseWriter(CurrentThread.Instance, saved => { }, busy, null, null, null, _log);
        }

        /// <summary>An authority in the database's store, which every write of a rule needs.</summary>
        private void Seed()
        {
            StoreSeeds.Authority(Store());
            _database.Database.Modified = false;
        }

        private IGateStore Store()
        {
            return DatabaseAuthority.Store(_database.Database);
        }

        /// <summary>The rules of a reading made now, as the gate's next hold reads them.</summary>
        private StoredRules Read()
        {
            using (var reading = new KdbxStore(_database.Database, GateVersion.Current, null).Read())
            {
                return StoredRules.Of(reading);
            }
        }

        /// <summary>How many entries the plugin's store holds.</summary>
        private int StoreEntries()
        {
            return _database.RootGroup.Groups.Where(group => StoreNodes.Holds(group)).Sum(group => (int)group.Entries.UCount);
        }

        private static string NodeOf(PwGroup group)
        {
            return Hex.Of(group.Uuid.UuidBytes);
        }

        private static string NodeOf(PwEntry entry)
        {
            return Hex.Of(entry.Uuid.UuidBytes);
        }

        private Form GroupDialog(PwGroup group)
        {
            var dialog = new GroupForm();
            dialog.InitEx(group, false, null, _database.Database);
            return dialog;
        }

        private Form EntryDialog(PwEntry entry, PwEditMode mode)
        {
            var dialog = new PwEntryForm();
            dialog.InitEx(entry, mode, _database.Database, _icons, false, false);
            return dialog;
        }

        private PwGroup GroupUnderRoot(string name)
        {
            var group = new PwGroup(true, true, name, PwIcon.Folder);
            _database.RootGroup.AddGroup(group, true);
            return group;
        }

        private static PwEntry EntryIn(PwGroup group)
        {
            var entry = new PwEntry(true, true);
            group.AddEntry(entry, true);
            return entry;
        }

        /// <summary>A source handing every dialog the tab a KeePass dialog gave.</summary>
        private sealed class TheTab : INodeDialogSource
        {
            private readonly NodeTab _tab;

            public TheTab(NodeTab tab)
            {
                _tab = tab;
            }

            public NodeTab TabFor(Form dialog)
            {
                return _tab;
            }
        }

        /// <summary>A log keeping its warnings, and nothing else.</summary>
        private sealed class WarningLog : IGateLog
        {
            public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
                Warnings.Add(message);
            }

            public void Error(string message, Exception error)
            {
            }

            public void Unexpected(string message, Exception error)
            {
            }
        }

        /// <summary>An operator told things, counted.</summary>
        private sealed class Telling : IOperatorPrompt
        {
            public int Told { get; private set; }

            public string News { get; private set; }

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                Told++;
                News = news;
            }
        }
    }
}
