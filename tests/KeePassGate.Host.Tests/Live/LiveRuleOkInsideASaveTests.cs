using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Spec;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    // cspell:ignore NTFS

    /// <summary>
    /// A node dialog's OK pressed while a save of the plugin's own runs under the dialog, in a real
    /// KeePass: the rule is written once that save has returned, so the save's file lacks it, the
    /// database stays marked, and the operator's next save writes it.
    /// </summary>
    /// <remarks>
    /// **The save is a client giving its own certificate up**, which the plugin writes and saves on
    /// KeePass's thread under the open dialog, under a key derivation made to take seconds so that
    /// the save is still running a known while after it has begun. The OK is pressed with the
    /// pointer once the save's transaction file is seen, as `NodeDialogOkBesideSaveTests` presses
    /// it inside a pairing's save over the real writer; KeePass's own handler of the OK runs inside
    /// the save, and the dialog's close, where the plugin writes, comes once the save has returned,
    /// `STORE.md` §629.
    ///
    /// **What the save left is read from the file before anything else saves it**, from a copy,
    /// so that KeePass's own file is never held.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveRuleOkInsideASaveTests
    {
        private const string Profile = "reader";

        private const string Root = "Invented Vault";

        /// <summary>
        /// How long deriving the database's key takes, which is most of what every save spends, the
        /// give-up's among them, and what every open of the file spends as well.
        /// </summary>
        private static readonly TimeSpan KeyDerivation = TimeSpan.FromSeconds(4);

        /// <summary>
        /// How far into the save the OK is pressed: inside the key derivation, which takes seconds,
        /// and clear of the moment the save opens its file.
        /// </summary>
        private static readonly TimeSpan IntoTheSave = TimeSpan.FromMilliseconds(300);

        /// <summary>How long the give-up's save is given to begin, and then to end.</summary>
        private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(60);

        /// <summary>How often the file a save writes is looked for.</summary>
        private const int PollMilliseconds = 5;

        /// <summary>
        /// What KeePass's `FileTransactionEx` calls the file it writes in its temporary directory
        /// under Transactional NTFS, before an identifier of its own.
        /// </summary>
        private const string TransactionPrefix = "KeePass_TxF_";

        /// <summary>What it adds to that file's name, and to the database's own beside the database.</summary>
        private const string TransactionSuffix = ".tmp";

        private LiveSession _session;
        private KeePassDriver _driver;
        private PluginLog _log;
        private GateAsker _asker;

        private Inside _group;
        private Inside _entry;
        private string _savedTitle;
        private bool _exited;
        private StoredRules _stored;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            keePass.FillWithInventedContent();
            keePass.NameProfiles(Profile);
            keePass.KeepTemporaryFilesInside();

            using (var reader = keePass.IssueClientCertificate("agent-reader"))
            using (var first = keePass.IssueClientCertificate("agent-giving-up-first"))
            using (var second = keePass.IssueClientCertificate("agent-giving-up-second"))
            {
                keePass.Admit(reader, Profile);
                keePass.Admit(first, Profile);
                keePass.Admit(second, Profile);

                // The gate's key read before the key derivation is made long, since reading it
                // opens the file.
                _asker = new GateAsker(keePass, reader, Profile);
                _log = new PluginLog(keePass);
                keePass.DeriveTheKeyFor(KeyDerivation);
                try
                {
                    _driver = KeePassDriver.Start(keePass);
                    _log.WaitUntilServing(0, keePass);
                    _group = InsideASave(keePass, true, first);
                    _entry = InsideASave(keePass, false, second);
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
            TestContext.Out.WriteLine("the group dialog: " + _group);
            TestContext.Out.WriteLine("the entry dialog: " + _entry);
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
        public void The_group_dialog_s_OK_is_pressed_inside_the_save_and_the_dialog_closes_once_the_save_has_returned()
        {
            Pressed(_group);
        }

        [Test]
        public void The_entry_dialog_s_OK_is_pressed_inside_the_save_and_the_dialog_closes_once_the_save_has_returned()
        {
            Pressed(_entry);
        }

        [Test]
        public void The_save_of_the_plugin_s_own_writes_its_change_and_not_the_rule()
        {
            foreach (var inside in new[] { _group, _entry })
            {
                Assert.That(inside.GivenUp, Is.EqualTo("200"), inside.Name + ": the client giving its certificate up");
                Assert.That(inside.GivenUpInTheFile, Is.True, inside.Name + ": the give-up is not in the file its save left");
                Assert.That(inside.InTheFile, Is.EqualTo("none"), inside.Name + ": the rule is in the file the give-up's save left");
            }
        }

        [Test]
        public void The_rule_is_written_after_that_save_marks_the_database_and_is_in_force()
        {
            foreach (var inside in new[] { _group, _entry })
            {
                Assert.That(inside.Before, Is.EqualTo(GateAsker.Refused), inside.Name + ": granted before the OK");
                Assert.That(inside.Written, Is.True, inside.Name + ": the plugin never logged the rule as written");
                Assert.That(inside.WrittenAt.HasValue && inside.Pressed.HasValue && inside.WrittenAt.Value > inside.Pressed.Value,
                    Is.True, inside.Name + ": the write is not logged after the OK: " + inside);
                Assert.That(LiveRules.Marked(inside.Title), Is.True, inside.Name + ": KeePass's title is '" + inside.Title + "'");
                Assert.That(inside.Answer, Is.EqualTo(".R......"), inside.Name + ": the client's answer");
            }
        }

        [Test]
        public void The_operator_s_next_save_writes_both_rules()
        {
            Assert.That(LiveRules.Marked(_savedTitle), Is.False, "the save left the title '" + _savedTitle + "'");
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(ThrowawayKeePass.AutomationUuid), Profile), Is.EqualTo("Grants .R......"));
            Assert.That(LiveRules.Versions(_stored, LiveRules.Node(ThrowawayKeePass.MailboxUuid), Profile), Is.EqualTo("Grants .R......"));
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private static void Pressed(Inside inside)
        {
            Assert.That(inside.Seen, Is.Not.Null, inside.Name + ": the give-up's save was never seen under way, looking for " +
                TransactionPrefix + "*" + TransactionSuffix + " in KeePass's temporary directory and beside the database");
            Assert.That(inside.Pressed, Is.Not.Null, inside.Name + ": the OK was never pressed");
            Assert.That(inside.Over, Is.Not.Null, inside.Name + ": the save was never seen to end");
            Assert.That(inside.Pressed.GetValueOrDefault() < inside.Over.GetValueOrDefault(), Is.True,
                inside.Name + ": the OK was pressed after the save: " + inside);
            Assert.That(inside.ClosedInsideTheSave, Is.False, inside.Name + ": the dialog closed while the save ran: " + inside);
            Assert.That(inside.Closed, Is.Not.Null, inside.Name + ": the dialog never closed");
        }

        /// <summary>
        /// A rule set on a node's tab, a client giving its certificate up, and the dialog's OK
        /// pressed with the pointer once that give-up's save is seen under way.
        /// </summary>
        private Inside InsideASave(ThrowawayKeePass keePass, bool group, X509Certificate2 givingUp)
        {
            var uuid = group ? ThrowawayKeePass.AutomationUuid : ThrowawayKeePass.MailboxUuid;
            var inside = new Inside { Name = group ? "the group dialog" : "the entry dialog" };
            var mark = _log.Mark;
            var tab = group ? RuleTab.OfGroup(_driver, "Automation") : RuleTab.OfEntry(_driver, Root, "Shared Mailbox");
            tab.AddRule(Profile, "R", ".R......");
            inside.Before = Ask(group, uuid);

            var ok = Uia.WaitFor("the dialog's OK", () => Uia.ById(tab.Dialog, ControlType.Button, "m_btnOK"));
            var box = ok.Current.BoundingRectangle;
            var x = (int)(box.Left + (box.Width / 2));
            var y = (int)(box.Top + (box.Height / 2));
            var window = LiveRules.HandleOf(tab.Dialog);
            Keyboard.Front(tab.Dialog);

            var name = _asker.NewName();
            LiveReply reply = null;
            var giving = new Thread(() => reply = LiveGateClient.Send(
                keePass.Port, name, givingUp, "DELETE", GateOperations.PathOf(GateOperations.RevokeCertificate), null,
                LiveGateClient.OnTheOperator)) { IsBackground = true };
            giving.Start();

            inside.Seen = When(() => Saving(keePass), SaveTimeout);
            if (inside.Seen != null)
            {
                // Pressed a moment into the save rather than as it begins, so that the press lands
                // in the key derivation, which the save spends seconds on.
                Thread.Sleep(IntoTheSave);
                Mouse.Click(x, y);
                inside.Pressed = DateTime.UtcNow;

                // Watched for as long as the save runs: a dialog gone before the save ended is a
                // close, and with the close a write, inside the save.
                var deadline = DateTime.UtcNow + SaveTimeout;
                while (Saving(keePass) && DateTime.UtcNow < deadline)
                {
                    inside.ClosedInsideTheSave |= !TestNativeMethods.IsWindowVisible(window);
                    Thread.Sleep(PollMilliseconds);
                }

                if (!Saving(keePass))
                {
                    inside.Over = DateTime.UtcNow;
                }
            }

            LiveRules.PointerAway();
            giving.Join(LiveGateClient.OnTheOperator + TimeSpan.FromSeconds(10));
            inside.Closed = When(() => !TestNativeMethods.IsWindowVisible(window), LiveRules.WindowTimeout);
            inside.GivenUp = reply == null
                ? "no reply"
                : reply.Answered ? reply.Status.ToString(System.Globalization.CultureInfo.InvariantCulture) : "no answer: " + reply.Failure;

            inside.Written = _log.WaitFor(mark, LiveRules.WrittenLine, LiveRules.WriteTimeout);
            inside.WrittenAt = _log.At(mark, LiveRules.WrittenLine);
            inside.Title = LiveRules.WaitForTitle(_driver, true, LiveRules.TitleTimeout);
            inside.Answer = Ask(group, uuid);

            var key = CertificateDigest.Of(givingUp);
            var left = LiveRules.FromTheFile(keePass.DatabasePath, database => new
            {
                Rule = LiveRules.Versions(LiveRules.RulesOf(database), LiveRules.Node(uuid), Profile),
                GivenUp = new StoredAdmissions(database).Holds(StoreKinds.Withdrawn, StoreKinds.Certificate, key),
            });
            inside.InTheFile = left.Rule;
            inside.GivenUpInTheFile = left.GivenUp;
            return inside;
        }

        private string Ask(bool group, string uuid)
        {
            return group ? _asker.AtGroup(uuid) : _asker.AtEntry(uuid);
        }

        /// <summary>When the condition first held, or null where it did not in time.</summary>
        private static DateTime? When(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                Thread.Sleep(PollMilliseconds);
            }

            return DateTime.UtcNow;
        }

        /// <summary>
        /// Whether KeePass is part way through saving the database, read off the file its
        /// transaction writes before the key is derived and moves over the database once the tree
        /// is written: in KeePass's temporary directory under Transactional NTFS, and beside the
        /// database where it is not available.
        /// </summary>
        private static bool Saving(ThrowawayKeePass keePass)
        {
            return File.Exists(keePass.DatabasePath + TransactionSuffix) ||
                   Directory.EnumerateFiles(keePass.TemporaryDirectory, TransactionPrefix + "*" + TransactionSuffix).Any();
        }

        /// <summary>What a dialog's OK pressed inside a save of the plugin's own left.</summary>
        private sealed class Inside
        {
            public string Name { get; set; }

            public string Before { get; set; }

            public DateTime? Seen { get; set; }

            public DateTime? Pressed { get; set; }

            public bool ClosedInsideTheSave { get; set; }

            public DateTime? Over { get; set; }

            public DateTime? Closed { get; set; }

            public string GivenUp { get; set; }

            public bool Written { get; set; }

            public DateTime? WrittenAt { get; set; }

            public string Title { get; set; }

            public string Answer { get; set; }

            public string InTheFile { get; set; }

            public bool GivenUpInTheFile { get; set; }

            public override string ToString()
            {
                return "save seen " + LiveRules.Moment(Seen) + ", OK pressed " + LiveRules.Moment(Pressed) +
                       ", save over " + LiveRules.Moment(Over) + ", dialog closed " + LiveRules.Moment(Closed) +
                       ", written " + LiveRules.Moment(WrittenAt) + ", give-up " + GivenUp + ", title " + Title +
                       ", the file the save left: " + InTheFile;
            }
        }
    }
}
