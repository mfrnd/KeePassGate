using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The rows a store seeded before KeePass starts shows on a real KeePass's group dialog, read
    /// through UI automation, and what each press and the dialog's OK do to the file and to a
    /// connected client: a rule in several versions, also scrolled, and the X on a version; a
    /// limit and its X; a change at a group above a node that follows it; and the message after an
    /// OK that left a press out.
    /// </summary>
    /// <remarks>
    /// **Seeded through the product's own acts**, in copies of the file merged as a synchronize
    /// merges them, since a running KeePass serves the database it holds in memory and nothing
    /// written underneath it is seen. The seeding is held to what each step needs before KeePass
    /// starts, so that a step that finds otherwise is about the window and not about the seed.
    ///
    /// **What UI automation reads is held to the tab's own sentences**: each State cell's word and
    /// its help text, which is the cell's tooltip, and the help text of a line a press changed,
    /// `STORE.md` §657.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveRuleSeededStatesTests
    {
        private const string Profile = "reader";

        private const string Root = "Invented Vault";

        /// <summary>A group whose rule for the profile stands in three versions, under four rules of other profiles.</summary>
        private const string Bound = "Bound";

        private const string BoundUuid = "AA000000000000000000000000000011";

        /// <summary>A group that cuts what arrives for the profile.</summary>
        private const string Cut = "Cut";

        private const string CutUuid = "AA000000000000000000000000000012";

        /// <summary>A group whose rule the entry in it follows.</summary>
        private const string Above = "Above";

        private const string AboveUuid = "AA000000000000000000000000000013";

        private const string BelowUuid = "EE000000000000000000000000000011";

        /// <summary>A group whose rule for the profile stands in a version no new version naming it fits beside.</summary>
        private const string Full = "Full";

        private const string FullUuid = "AA000000000000000000000000000014";

        /// <summary>The profiles with a rule of their own at <see cref="Bound"/>, each before the profile in the table's order.</summary>
        private static readonly string[] Others = { "agent", "builder", "deploy", "ops" };

        private static readonly string Changed = Environment.NewLine + GrantTab.ChangedHere;

        private LiveSession _session;
        private KeePassDriver _driver;
        private PluginLog _log;
        private GateAsker _asker;

        private readonly Dictionary<string, string> _before = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _after = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _written = new Dictionary<string, bool>(StringComparer.Ordinal);

        private IReadOnlyList<TabLine> _boundShown;
        private IReadOnlyList<TabLine> _boundScrolled;
        private IReadOnlyList<TabLine> _boundAfterTheX;
        private IReadOnlyList<TabLine> _cutShown;
        private IReadOnlyList<TabLine> _cutAfterTheX;
        private IReadOnlyList<TabLine> _aboveShown;
        private IReadOnlyList<TabLine> _aboveChosen;
        private IReadOnlyList<TabLine> _fullChosen;
        private bool _fullClosed;
        private string _message;
        private bool _messageClosed;
        private string _savedTitle;
        private bool _exited;
        private StoredRules _stored;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            Seed(keePass);

            using (var client = keePass.IssueClientCertificate("agent-reader"))
            {
                keePass.Admit(client, Profile);
                _asker = new GateAsker(keePass, client, Profile);
                _log = new PluginLog(keePass);
                try
                {
                    _driver = KeePassDriver.Start(keePass);
                    _log.WaitUntilServing(0, keePass);
                    Ask(_before);

                    Versions();
                    Limit();
                    Follow();
                    NotKept();

                    Ask(_after);
                    _savedTitle = LiveRules.Save(_driver);
                    _exited = _driver.ShutDown();
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

            _stored = keePass.ReadRules().Rules;
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
        public void A_rule_in_several_versions_shows_a_row_for_each_with_its_state_and_tooltip()
        {
            var grants = Grants(".R......");
            Assert.That(Read(_boundShown), Is.EqualTo(Others.Select(other => Own(other, ".R......"))
                .Concat(new[]
                {
                    Line(".R......", GrantTab.TakenOffWord, Tip(GrantTab.TakenOffTip, grants, GrantTab.VersionRemoval)),
                    Line(".R...L..", GrantTab.VersionWord, Tip(Count(3), grants, GrantTab.VersionRemoval)),
                    Line("CR...L..", GrantTab.VersionWord, Tip(Count(3), grants, GrantTab.VersionRemoval)),
                })));
        }

        [Test]
        public void The_rows_of_a_rule_in_several_versions_are_out_of_view_until_the_table_is_scrolled_and_read_the_same_scrolled()
        {
            Assert.That(Ours(_boundShown).Select(line => line.Offscreen), Is.All.True, "before the scroll: " + string.Join(" / ", _boundShown));
            Assert.That(Ours(_boundScrolled).Count(line => !line.Offscreen), Is.GreaterThan(1),
                "after the scroll, the rows of the versions in view: " + string.Join(" / ", _boundScrolled));
            Assert.That(Ours(_boundScrolled).Any(line => line.State == GrantTab.TakenOffWord && !line.Offscreen), Is.True,
                "after the scroll, the version that takes the rule off in view: " + string.Join(" / ", _boundScrolled));
            Assert.That(Read(_boundScrolled), Is.EqualTo(Read(_boundShown)));
        }

        [Test]
        public void The_X_on_a_version_leaves_the_others_bound_and_marked_as_changed()
        {
            var grants = Grants(".R...L..");
            Assert.That(Read(_boundAfterTheX), Is.EqualTo(Others.Select(other => Own(other, ".R......"))
                .Concat(new[]
                {
                    Line(".R...L..", GrantTab.VersionWord, Tip(Count(2), grants, GrantTab.VersionRemoval) + Changed, GrantTab.ChangedHere),
                    Line("CR...L..", GrantTab.VersionWord, Tip(Count(2), grants, GrantTab.VersionRemoval) + Changed, GrantTab.ChangedHere),
                })));
        }

        [Test]
        public void The_X_on_a_version_and_OK_write_each_version_left_again_and_the_client_is_granted_what_they_share()
        {
            Assert.That(_written[Bound], Is.True, "the plugin never logged the rules as written");
            Assert.That(_before[Bound], Is.EqualTo(".R......"), "what all three versions allow");
            Assert.That(_after[Bound], Is.EqualTo(".R...L.."), "what the two versions left allow");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(BoundUuid), Profile), Is.EqualTo("Grants .R...L.., Grants CR...L.."));
            var left = _stored.At(LiveRules.Node(BoundUuid)).Row(Profile).Versions;
            Assert.That(left.Select(version => version.After.Count), Is.All.GreaterThan(0), "a version left is no new version naming the versions it replaces");
            foreach (var other in Others)
            {
                Assert.That(LiveRules.Versions(_stored, LiveRules.Node(BoundUuid), other), Is.EqualTo("Grants .R......"), other);
            }
        }

        [Test]
        public void A_limit_shows_two_rows_bound_together_with_their_states_and_tooltips()
        {
            Assert.That(Read(_cutShown), Is.EqualTo(new[]
            {
                Line(".R...L..", GrantTab.InheritedWord, Tip(GrantTab.InheritedCutTip, GrantTab.InheritedCutGrants, GrantTab.InheritedChoice)),
                Line(".R......", GrantTab.LimitWord, Tip(GrantTab.LimitTip, GrantTab.LimitGrants, GrantTab.LimitRemoval)),
            }));
        }

        [Test]
        public void The_X_on_a_limit_leaves_the_inherited_row_alone_and_marked_as_changed()
        {
            Assert.That(Read(_cutAfterTheX), Is.EqualTo(new[]
            {
                Line(".R...L..", GrantTab.InheritedWord, Tip(GrantTab.InheritedTip, GrantTab.InheritedGrants, GrantTab.InheritedChoice) + Changed, GrantTab.ChangedHere),
            }));
        }

        [Test]
        public void The_X_on_a_limit_and_OK_let_the_client_through()
        {
            Assert.That(_written[Cut], Is.True, "the plugin never logged the rules as written");
            Assert.That(_before[Cut], Is.EqualTo(".R......"), "the limit cut what arrives");
            Assert.That(_after[Cut], Is.EqualTo(".R...L.."), "what arrives");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(CutUuid), Profile), Is.EqualTo("TakenOff .R...L.."));
        }

        [Test]
        public void A_rule_chosen_on_the_tab_is_told_as_changed_on_its_line()
        {
            Assert.That(Read(_aboveShown), Is.EqualTo(new[] { Own(Profile, ".....L..") }));
            Assert.That(Read(_aboveChosen), Is.EqualTo(new[] { Line(".R...L..", string.Empty, GrantTab.ChangedHere, GrantTab.ChangedHere) }));
        }

        [Test]
        public void An_OK_at_a_group_above_carries_the_node_below_that_follows_it()
        {
            Assert.That(_written[Above], Is.True, "the plugin never logged the rules as written");
            Assert.That(_before[Above], Is.EqualTo(".....L.."));
            Assert.That(_before[BelowUuid], Is.EqualTo(".....L.."), "the entry below lets through what arrives");
            Assert.That(_after[Above], Is.EqualTo(".R...L.."));
            Assert.That(_after[BelowUuid], Is.EqualTo(".R...L.."), "the entry below did not follow the change above it");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(AboveUuid), Profile), Is.EqualTo("Grants .R...L.."));
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(BelowUuid), Profile), Is.EqualTo("TakenOff .R...L.."));
        }

        [Test]
        public void An_OK_that_left_a_press_out_is_told_in_a_message_after_the_dialog_has_closed()
        {
            Assert.That(Read(_fullChosen), Is.EqualTo(new[]
            {
                Own("ops", ".RU..L..", GrantTab.ChangedHere, GrantTab.ChangedHere),
                Line(".RU.....", string.Empty, GrantTab.ChangedHere, GrantTab.ChangedHere),
            }));
            Assert.That(_fullClosed, Is.True, "the group dialog did not close at its OK");
            Assert.That(_message, Is.EqualTo(string.Join("\n", GrantTab.NotAllKept, GrantTab.NoRoom, GrantTab.ShownAsKept)),
                "the message, as UI automation reads it");
            Assert.That(_messageClosed, Is.True, "the message did not close at its OK");
        }

        [Test]
        public void The_press_left_out_is_not_in_force_and_the_rest_of_the_OK_is_written()
        {
            Assert.That(_written[Full], Is.True, "the plugin never logged the rules as written");
            Assert.That(_before[Full], Is.EqualTo(".R......"));
            Assert.That(_after[Full], Is.EqualTo(".R......"), "the press left out is in force");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(FullUuid), Profile), Is.EqualTo("Grants .R......"));
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(FullUuid), "ops"), Is.EqualTo("Grants .RU..L.."));
        }

        [Test]
        public void The_operator_s_save_writes_every_change_and_KeePass_exits()
        {
            Assert.That(LiveRules.Marked(_savedTitle), Is.False, "the save left the title '" + _savedTitle + "'");
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at_but_the_press_left_out()
        {
            var named = "the rule of " + Profile + " at " + LiveRules.Node(FullUuid).Substring(0, 8) + " changed on the " +
                        GrantTab.Text + " tab was not written: ";
            var failures = LiveSession.FailuresIn(_pluginLog);
            var leftOut = failures.Where(line => line.Contains(named)).ToArray();
            Assert.That(leftOut, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(failures.Except(leftOut), Is.Empty);
        }

        /// <summary>
        /// The group whose rule stands in three versions: read, scrolled to its rows, the version
        /// that takes the rule off removed with its X, and the dialog's OK.
        /// </summary>
        private void Versions()
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfGroup(_driver, Bound);
            _boundShown = tab.Lines();

            // A notch of the wheel at a time, until the version that takes the rule off is in view:
            // the table shows fewer rows than the group and the empty line take, so scrolled to its
            // end the first row of the group is out of view again.
            Func<TabLine, bool> inView = line => line.Profile == Profile && line.State == GrantTab.TakenOffWord && !line.Offscreen;
            for (var notch = 0; notch < 8 && !tab.Lines().Any(inView); notch++)
            {
                tab.ScrollDown(1);
                Uia.WaitForOrNull(() => tab.Lines().Any(inView) ? "in view" : null, TimeSpan.FromSeconds(2));
            }

            _boundScrolled = tab.Lines();
            var takenOff = tab.Line("the version that takes the rule off, in view", inView);
            tab.PressX(takenOff);
            _boundAfterTheX = Shown(tab, lines => Ours(lines).Count() == 2);
            _written[Bound] = Accept(tab, mark);
        }

        /// <summary>The group that cuts what arrives: read, the limit removed with its X, and the dialog's OK.</summary>
        private void Limit()
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfGroup(_driver, Cut);
            _cutShown = tab.Lines();
            tab.PressX(tab.Line("the limit", line => line.State == GrantTab.LimitWord));
            _cutAfterTheX = Shown(tab, lines => Ours(lines).Count() == 1);
            _written[Cut] = Accept(tab, mark);
        }

        /// <summary>The group above the entry that follows it: its rule widened, and the dialog's OK.</summary>
        private void Follow()
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfGroup(_driver, Above);
            _aboveShown = tab.Lines();
            tab.SetRule(tab.Line("the profile's rule", line => line.Profile == Profile), "R", ".R...L..");
            _aboveChosen = tab.Lines();
            _written[Above] = Accept(tab, mark);
        }

        /// <summary>
        /// The group whose rule's new version would not fit: a rule of another profile and that rule
        /// both changed, the dialog's OK, and the message the plugin shows once the dialog has
        /// closed, read and closed with its OK.
        /// </summary>
        private void NotKept()
        {
            var mark = _log.Mark;
            var tab = RuleTab.OfGroup(_driver, Full);
            tab.SetRule(tab.Line("the other profile's rule", line => line.Profile == "ops"), "U", ".RU..L..");
            tab.SetRule(tab.Line("the profile's rule", line => line.Profile == Profile), "U", ".RU.....");
            _fullChosen = tab.Lines();

            // The dialog goes at its OK without anything pressed on the message: a message shown
            // inside the close would hold the dialog up until it was answered.
            _fullClosed = LiveRules.Accept(_driver, tab.Dialog);
            _written[Full] = _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);

            var box = _driver.WaitForWindow(GrantTab.NotKeptTitle, LiveRules.WindowTimeout);
            if (box == null)
            {
                return;
            }

            var text = Uia.Descendant(box, ControlType.Text, shown => shown.Length > 0);
            _message = text == null ? Uia.Inventory(box) : Uia.Name(text).Replace("\r\n", "\n");
            var window = LiveRules.HandleOf(box);
            _messageClosed = _driver.PressButton(box, "OK") && LiveRules.Gone(window, LiveRules.WindowTimeout);
        }

        /// <summary>The lines of the table once they answer, read again until they do or the time is up.</summary>
        private static IReadOnlyList<TabLine> Shown(RuleTab tab, Func<IReadOnlyList<TabLine>, bool> done)
        {
            return Uia.WaitForOrNull(
                () =>
                {
                    var lines = tab.Lines();
                    return done(lines) ? lines : null;
                },
                TimeSpan.FromSeconds(10)) ?? tab.Lines();
        }

        /// <summary>The dialog accepted, and whether the plugin logged its write.</summary>
        private bool Accept(RuleTab tab, int mark)
        {
            return LiveRules.Accept(_driver, tab.Dialog) && _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);
        }

        /// <summary>What the client is answered at each node of the session.</summary>
        private void Ask(IDictionary<string, string> answers)
        {
            foreach (var group in new[] { (Bound, BoundUuid), (Cut, CutUuid), (Above, AboveUuid), (Full, FullUuid) })
            {
                answers[group.Item1] = _asker.AtGroup(group.Item2);
            }

            answers[BelowUuid] = _asker.AtEntry(BelowUuid);
        }

        /// <summary>The lines of the profile's own, which are what the steps press.</summary>
        private static IEnumerable<TabLine> Ours(IEnumerable<TabLine> lines)
        {
            return lines.Where(line => line.Profile == Profile);
        }

        /// <summary>Every line but the empty line, as a line of text holding what UI automation read of it.</summary>
        private static IEnumerable<string> Read(IEnumerable<TabLine> lines)
        {
            return lines.Where(line => !line.Empty).Select(line => Seen(line.Profile, line.Mask, line.State, line.StateHelp, line.LineHelp)).ToList();
        }

        private static string Own(string profile, string mask)
        {
            return Seen(profile, mask, string.Empty, string.Empty, string.Empty);
        }

        private static string Own(string profile, string mask, string stateHelp, string lineHelp)
        {
            return Seen(profile, mask, string.Empty, stateHelp, lineHelp);
        }

        private static string Line(string mask, string state, string stateHelp)
        {
            return Seen(Profile, mask, state, stateHelp, string.Empty);
        }

        private static string Line(string mask, string state, string stateHelp, string lineHelp)
        {
            return Seen(Profile, mask, state, stateHelp, lineHelp);
        }

        private static string Seen(string profile, string mask, string state, string stateHelp, string lineHelp)
        {
            return profile + " | " + mask + " | " + state + " | " + stateHelp + " | " + lineHelp;
        }

        private static string Tip(params string[] sentences)
        {
            return string.Join(" ", sentences);
        }

        private static string Grants(string mask)
        {
            return string.Format(CultureInfo.InvariantCulture, GrantTab.GroupGrants, mask);
        }

        private static string Count(int versions)
        {
            return string.Format(CultureInfo.InvariantCulture, GrantTab.VersionTip, versions);
        }

        /// <summary>
        /// The tree, the names, the client's rules at each group and the versions of the rule in
        /// several versions, made in copies of the file and merged, then the version no new version
        /// fits beside; held to what each step needs.
        /// </summary>
        private void Seed(ThrowawayKeePass keePass)
        {
            string root = null;
            keePass.ChangeTheStore(database =>
            {
                database.RootGroup.Name = Root;
                root = Hex.Of(database.RootGroup.Uuid.UuidBytes);
                LiveRules.AddGroup(database.RootGroup, Bound, BoundUuid);
                LiveRules.AddGroup(database.RootGroup, Cut, CutUuid);
                LiveRules.AddEntry(LiveRules.AddGroup(database.RootGroup, Above, AboveUuid), "Below", BelowUuid);
                LiveRules.AddGroup(database.RootGroup, Full, FullUuid);
            });
            keePass.NameProfiles(Others.Concat(new[] { Profile }).ToArray());

            keePass.ChangeTheStore(database =>
            {
                var store = DatabaseAuthority.Store(database);
                StoreSeeds.Rule(store, root, (Profile, "RL"));
                StoreSeeds.Rule(store, LiveRules.Node(BoundUuid), Others.Select(other => (other, "R")).ToArray());
                StoreSeeds.TakeOff(store, LiveRules.Node(CutUuid), Profile, "R");
                StoreSeeds.Rule(store, LiveRules.Node(AboveUuid), (Profile, "L"));
                StoreSeeds.TakeOff(store, LiveRules.Node(BelowUuid), Profile, "L");
                StoreSeeds.Rule(store, LiveRules.Node(FullUuid), ("ops", "RL"));
            });

            // Three copies that each change the profile's rule at the group, merged as a
            // synchronize merges them: a version granting, a wider version, and a version taking
            // the rule off.
            var wider = keePass.CopyDatabase("apart-wider.kdbx");
            var off = keePass.CopyDatabase("apart-off.kdbx");
            keePass.ChangeTheStore(database => StoreSeeds.Rule(DatabaseAuthority.Store(database), LiveRules.Node(BoundUuid), (Profile, "RL")));
            ChangeACopy(wider, database => StoreSeeds.Rule(DatabaseAuthority.Store(database), LiveRules.Node(BoundUuid), (Profile, "CRL")));
            ChangeACopy(off, database => StoreSeeds.TakeOff(DatabaseAuthority.Store(database), LiveRules.Node(BoundUuid), Profile, "R"));
            keePass.MergeIntoDatabase(wider);
            keePass.MergeIntoDatabase(off);

            keePass.ChangeTheStore(database => Crowded(database, LiveRules.Node(FullUuid), Profile, "row:R"));

            var seeded = keePass.ReadRules().Rules;
            Assert.That(LiveRules.Versions(seeded, LiveRules.Node(BoundUuid), Profile), Is.EqualTo("TakenOff .R......, Grants .R...L.., Grants CR...L.."), "seeded at " + Bound);
            Assert.That(LiveRules.Versions(seeded, LiveRules.Node(CutUuid), Profile), Is.EqualTo("TakenOff .R......"), "seeded at " + Cut);
            Assert.That(LiveRules.Versions(seeded, LiveRules.Node(AboveUuid), Profile), Is.EqualTo("Grants .....L.."), "seeded at " + Above);
            Assert.That(LiveRules.Versions(seeded, LiveRules.Node(BelowUuid), Profile), Is.EqualTo("TakenOff .....L.."), "seeded below " + Above);
            Assert.That(LiveRules.Versions(seeded, LiveRules.Node(FullUuid), Profile), Is.EqualTo("Grants .R......"), "seeded at " + Full);
        }

        private static void ChangeACopy(string path, Action<PwDatabase> change)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(path), LiveRules.Key(), null);
                change(database);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// The rule of the profile at the node set again in a single version that names as many
        /// identifiers a version of its object could have as the version and its item fit a
        /// string with, as `GrantEditorInRealDialogsTests.CrowdedBelow` makes a version below: a new
        /// version naming that version as well fits no string.
        /// </summary>
        private static void Crowded(PwDatabase database, string node, string profile, string doc)
        {
            var store = DatabaseAuthority.Store(database);
            byte[] key;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            try
            {
                var objectName = StoreComposer.RowObject(node, profile);
                var place = StoreComposer.Version(key, objectName, StoreComposer.Content(("doc", doc))).Hex.Substring(0, 20);
                var names = new List<string>();
                string Named(int at) => place + at.ToString("x12", CultureInfo.InvariantCulture);
                string Item(IEnumerable<string> after)
                {
                    var listed = after.ToArray();
                    return StoreComposer.RuleVersionItem(StoreComposer.RuleVersion(key, objectName, doc, listed), objectName, doc, listed);
                }

                for (var at = 0; at < (StoreFormat.MaxWritten / 33) - 20; at++)
                {
                    names.Add(Named(at));
                }

                while (Item(names.Concat(new[] { Named(names.Count) })).Length <= StoreFormat.MaxWritten)
                {
                    names.Add(Named(names.Count));
                }

                var version = StoreRecord.Compose(key, StoreKinds.Version, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.After, string.Join(" ", names) },
                    { StoreKinds.Doc, doc },
                    { StoreKinds.ObjectMember, objectName },
                });
                var written = store.Write(new[] { new StoreAct("a rule crowded", key, StoreFormat.KeyName(key), new[] { version }, null) });
                Assert.That(written.Taken, Is.True, written.Reason);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
