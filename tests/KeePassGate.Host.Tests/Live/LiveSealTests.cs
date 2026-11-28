using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using KeePassGate.Spec;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A stopped store sealed again in a real KeePass, through the KPG tab and the seal's own
    /// dialog, `STORE.md` §625.
    /// </summary>
    /// <remarks>
    /// **What no other tier shows**: the press of Seal store again on the real settings dialog,
    /// the seal's dialog raised over it and answered through its own button, the seal held until
    /// the settings dialog's OK and written there on KeePass's thread, and the gate serving again
    /// from the next request with nothing reloaded. The seeded store loses its summary entry and
    /// the version of the root's rule for the client's profile, each deleted for good with a
    /// deletion record before KeePass opens the file, as KeePass's own delete and an emptied
    /// recycle bin leave them, since a running KeePass overwrites what is written underneath it.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveSealTests
    {
        private const string Profile = "ops";

        /// <summary>
        /// What KeePass puts between the file's name and its own in the main window's title while
        /// the database holds changes nobody has saved.
        /// </summary>
        private const string UnsavedMark = "* - ";

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private LiveReply _beforeSealing;
        private string _headline;
        private string _sealLine;
        private bool _sealDialogAppeared;
        private List<string> _rows = new List<string>();
        private LiveReply _whileTheSettingsDialogStood;
        private LiveReply _afterOk;
        private string _titleAfterOk;
        private string _titleAfterSaving;
        private bool _exited;
        private bool _fileHoldsTheSealsVersion;
        private bool _fileHoldsTheAuthority;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                // Admitted and granted while the store still reads, since both are records of the
                // store. The entries deleted afterwards are the summary entry the grant's write
                // left and the version it wrote.
                keePass.Admit(admitted, Profile);
                keePass.Grant((Profile, "RL"));
                keePass.ChangeTheStore(database =>
                {
                    foreach (var kind in new[] { StoreKinds.Version, StoreKinds.Head })
                    {
                        var entry = database.RootGroup.GetEntries(true)
                            .Single(found => found.Strings.ReadSafe(StoreFormat.KindField) == kind);
                        entry.ParentGroup.Entries.Remove(entry);
                        database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
                    }
                });

                var driver = KeePassDriver.Start(keePass);
                LivePairingTests.WaitUntilServing(keePass, driver);

                _beforeSealing = Ask(keePass, admitted);

                var dialog = driver.OpenDatabaseSettings();
                Assert.That(dialog, Is.Not.Null, "The database settings dialog never opened. " + driver.Diagnostics);
                Assert.That(driver.SelectGateTab(dialog), Is.Not.Null, driver.Diagnostics);
                Assert.That(driver.SelectPage(dialog, GateTab.AuthorityPageText), Is.Not.Null, driver.Diagnostics);
                _headline = driver.TextStartingWith(dialog, "The store of this database is ");
                _sealLine = driver.TextMatching(dialog, Regex.Escape(AuthorityDescription.SealLine));

                // On a thread of its own: the press raises a modal dialog, and the button's
                // invocation answers only once that dialog has gone.
                Assert.That(Press(dialog, GateTab.SealButtonText), Is.True, driver.Diagnostics);
                var sealDialog = driver.WaitForWindowById(GateTab.SealFormName, DialogTimeout);
                _sealDialogAppeared = sealDialog != null;
                Assert.That(sealDialog, Is.Not.Null,
                    "No dialog appeared after Seal store again. Windows: " + NativeWindows.Titles(keePass.Process) +
                    Environment.NewLine + driver.Diagnostics + Environment.NewLine + keePass.ReadPluginLog());

                _rows = Rows(sealDialog);
                Assert.That(driver.PressButton(sealDialog, GateTab.SealConfirmButtonText), Is.True, driver.Diagnostics);
                Assert.That(driver.WaitForWindowToClose(GateTab.SealFormName, DialogTimeout), Is.True,
                    "The seal's dialog was still open after Seal again. " + driver.Diagnostics);

                _whileTheSettingsDialogStood = Ask(keePass, admitted);

                Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);
                WaitForLog(keePass, driver, "what the " + GateName.Product + " tab held was put into the database",
                    "The plugin never reported putting the seal into the database.");

                // The line about serving again is written by the reading a connection makes, so
                // it is waited for once the client has asked.
                _afterOk = Ask(keePass, admitted);
                WaitForLog(keePass, driver, "serving a database whose store held it back as stopped again",
                    "The plugin never reported serving the database again.");
                _titleAfterOk = WaitForTitle(driver, true);

                driver.Save();
                _titleAfterSaving = WaitForTitle(driver, false);

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            _fileHoldsTheSealsVersion = keePass.FileHolds(StoreKinds.Version, StoreKinds.Doc, "row:");
            using (var reading = keePass.ReadAuthority())
            {
                _fileHoldsTheAuthority = reading.IsPresent;
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Rows of the seal's dialog:");
            foreach (var row in _rows)
            {
                TestContext.Out.WriteLine(row);
            }

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
        public void Before_the_seal_the_client_is_answered_that_the_store_is_stopped()
        {
            Assert.That(_beforeSealing.Status, Is.EqualTo(503), Said(_beforeSealing));
            Assert.That(_beforeSealing.Body, Does.Contain("\"stopped\""));
        }

        [Test]
        public void The_page_states_the_stop_and_offers_the_seal()
        {
            Assert.That(_headline, Is.EqualTo(AuthorityDescription.HeldBackHeadline(StoreWords.Stopped)));
            Assert.That(_sealLine, Is.Not.Null, "the line naming Seal store again");
        }

        [Test]
        public void The_press_raised_the_seal_s_dialog_listing_the_lost_rule_and_the_summary_entry_deleted()
        {
            // The summary entry deleted last is listed beside the rule, `StoreLosses.Heads`.
            Assert.That(_sealDialogAppeared, Is.True);
            Assert.That(_rows, Has.Count.EqualTo(2), string.Join(Environment.NewLine, _rows));
            Assert.That(_rows, Has.Some.StartsWith(SealRows.RuleOfAProfile), string.Join(Environment.NewLine, _rows));
            Assert.That(_rows, Has.Some.StartsWith(SealRows.SummaryEntry), string.Join(Environment.NewLine, _rows));
        }

        [Test]
        public void Nothing_lands_before_the_settings_dialog_s_OK()
        {
            Assert.That(_whileTheSettingsDialogStood.Status, Is.EqualTo(503), Said(_whileTheSettingsDialogStood));
        }

        [Test]
        public void After_OK_the_client_is_served()
        {
            Assert.That(_afterOk.Status, Is.EqualTo(200), Said(_afterOk));
        }

        [Test]
        public void The_seal_marked_the_database_changed_and_the_operator_s_save_kept_it()
        {
            Assert.That(_titleAfterOk, Does.Contain(UnsavedMark), "KeePass's title bar was '" + _titleAfterOk + "'");
            Assert.That(_titleAfterSaving, Does.Not.Contain(UnsavedMark), "KeePass's title bar was '" + _titleAfterSaving + "'");
            Assert.That(_fileHoldsTheSealsVersion, Is.True, "the file holds the version granting nothing");
            Assert.That(_fileHoldsTheAuthority, Is.True, "the file's store is whole and serves its authority");
        }

        [Test]
        public void The_log_names_what_the_seal_wrote()
        {
            Assert.That(_pluginLog, Does.Contain("Seal again: the store was stopped: "));
            Assert.That(_pluginLog, Does.Contain("serving a database whose store held it back as stopped again"));
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly after the seal.");
        }

        [Test]
        public void Nothing_was_reported_but_the_stop()
        {
            var unexpected = LiveSession.FailuresIn(_pluginLog)
                .Where(line => !line.Contains("not serving a database whose store is stopped"))
                .ToArray();

            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        private static LiveReply Ask(ThrowawayKeePass keePass, X509Certificate2 certificate)
        {
            return LiveGateClient.Get(keePass.Port, keePass.NewGateName(), certificate, GateOperations.StatusPath);
        }

        /// <summary>
        /// Invokes a button found by its caption on a task of its own, waiting a little for it:
        /// a button whose press raises a modal dialog answers its invocation once that dialog has
        /// gone. The pictures of a stopped store press Seal store again with it as well.
        /// </summary>
        internal static bool Press(AutomationElement dialog, string caption)
        {
            var button = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.Button, name => name.Replace("&", string.Empty).Trim() == caption),
                TimeSpan.FromSeconds(10));
            object pattern;
            if (button == null || !button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                return false;
            }

            var invoke = (InvokePattern)pattern;
            Task.Run(() => Thrown.By(invoke.Invoke)).Wait(TimeSpan.FromSeconds(5));
            return true;
        }

        /// <summary>Each row of the seal's list, its cells joined by a bar.</summary>
        private static List<string> Rows(AutomationElement sealDialog)
        {
            var list = Uia.ById(sealDialog, GateTab.SealListName);
            if (list == null)
            {
                return new List<string> { "no list called " + GateTab.SealListName };
            }

            // A list in details view publishes its rows as data items rather than list items.
            var items = list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
            var rows = new List<string>();
            foreach (AutomationElement item in items)
            {
                var cells = item.FindAll(TreeScope.Children, Condition.TrueCondition);
                rows.Add(cells.Count == 0
                    ? Uia.Name(item)
                    : string.Join(" | ", cells.Cast<AutomationElement>().Select(Uia.Name)));
            }

            return rows;
        }

        private static string Said(LiveReply reply)
        {
            return reply.Failure ?? reply.Body;
        }

        private static string WaitForTitle(KeePassDriver driver, bool unsaved)
        {
            var deadline = DateTime.UtcNow + DialogTimeout;
            var title = driver.MainWindowTitle;
            while (title.Contains(UnsavedMark) != unsaved && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
                title = driver.MainWindowTitle;
            }

            return title;
        }

        private static void WaitForLog(ThrowawayKeePass keePass, KeePassDriver driver, string marker, string failure)
        {
            var deadline = DateTime.UtcNow + DialogTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().Contains(marker))
                {
                    return;
                }

                Thread.Sleep(250);
            }

            Assert.Fail(failure + " " + driver.Diagnostics + Environment.NewLine + "Log so far:" + Environment.NewLine + keePass.ReadPluginLog());
        }
    }
}
