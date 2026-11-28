using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using KeePassGate.Bridge;
using KeePassGate.Gate;
using KeePassGate.Spec;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A store a later plugin wrote, started again in a real KeePass through the KPG tab and its
    /// warning, `STORE.md` §632.
    /// </summary>
    /// <remarks>
    /// **What no other tier shows**: the press of Start store again on the real settings dialog,
    /// the warning raised over it and answered Yes through its own button, the start held until
    /// the settings dialog's OK and written there on KeePass's thread, the client of the earlier
    /// store refused at the handshake from the next connection on, and a pairing under the new
    /// authority landing. The seeded store holds a version record of a later format, put in before
    /// KeePass opens the file, so the page reads outdated and a client is answered `503 outdated`,
    /// since a running KeePass overwrites what is written underneath it.
    ///
    /// **What is waited for at OK is the tab's own line**, `what the KeePassGate tab held was put
    /// into the database`: the line of a database served again is written by the reading a
    /// connection makes, and only once a client has asked.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveStartAgainTests
    {
        private const string Profile = "ops";

        /// <summary>
        /// What KeePass puts between the file's name and its own in the main window's title while
        /// the database holds changes nobody has saved.
        /// </summary>
        private const string UnsavedMark = "* - ";

        /// <summary>The line the settings dialog's OK writes once what the tab held is in the database.</summary>
        private const string Applied = "what the " + GateName.Product + " tab held was put into the database";

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private LiveReply _beforeStarting;
        private string _headline;
        private string _startLine;
        private bool _warningAppeared;
        private string _warning;
        private string _headlineAfterThePress;
        private LiveReply _whileTheSettingsDialogStood;
        private LiveReply _earlierClientAfterOk;
        private PairingReply _paired;
        private LiveReply _pairedClientAsked;
        private string _titleAfterOk;
        private string _titleAfterSaving;
        private bool _exited;
        private string _earlierAuthority;
        private string _authorityInTheFile;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, Profile);
                byte[] earlierSniKey;
                using (var reading = keePass.ReadAuthority())
                {
                    _earlierAuthority = reading.Material.AuthorityDigest;
                    earlierSniKey = (byte[])reading.Material.SniKey.Clone();
                }

                // A version record of a later format under the store key, as a later plugin's write
                // leaves it: the store reads outdated, `STORE.md` §651.
                keePass.ChangeTheStore(database =>
                {
                    byte[] key;
                    using (var reading = new KdbxStore(database, GateVersion.Current, null).Read())
                    {
                        key = (byte[])reading.InForce.Key.Bytes.Clone();
                    }

                    try
                    {
                        var later = StoreComposer.Record(key, StoreKinds.Format, "format:2", StoreComposer.Content(("format", "2"), ("plugin", "9.0.0")), 2);
                        database.RootGroup.AddEntry(StoreEntries.Of(later), true);
                    }
                    finally
                    {
                        Array.Clear(key, 0, key.Length);
                    }
                });

                var driver = KeePassDriver.Start(keePass);
                WaitForLog(keePass, driver, "listening on 127.0.0.1:" + keePass.Port, "The plugin never reported a listening gate.");

                _beforeStarting = Ask(keePass, keePass.NewGateName(), admitted);

                var dialog = driver.OpenDatabaseSettings();
                Assert.That(dialog, Is.Not.Null, "The database settings dialog never opened. " + driver.Diagnostics);
                Assert.That(driver.SelectGateTab(dialog), Is.Not.Null, driver.Diagnostics);
                Assert.That(driver.SelectPage(dialog, GateTab.AuthorityPageText), Is.Not.Null, driver.Diagnostics);
                _headline = driver.TextStartingWith(dialog, "The store of this database is ");
                _startLine = driver.TextMatching(dialog, Regex.Escape(AuthorityDescription.StartLine));

                // On a thread of its own: the press raises the warning, and the button's invocation
                // answers only once the warning has gone.
                Assert.That(Press(dialog, GateTab.StartAgainButtonText), Is.True, driver.Diagnostics);
                var warning = driver.WaitForWindow(AuthorityPanel.StartAgainTitle, DialogTimeout);
                _warningAppeared = warning != null;
                Assert.That(warning, Is.Not.Null,
                    "No warning appeared after Start store again. Windows: " + NativeWindows.Titles(keePass.Process) +
                    Environment.NewLine + driver.Diagnostics + Environment.NewLine + keePass.ReadPluginLog());
                _warning = Uia.Describe(warning, ControlType.Text);
                Assert.That(driver.PressButton(warning, "Yes"), Is.True, driver.Diagnostics);

                _headlineAfterThePress = driver.TextMatching(dialog, Regex.Escape(AuthorityDescription.PresentHeadline));
                _whileTheSettingsDialogStood = Ask(keePass, SniName.For(earlierSniKey), admitted);

                Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);
                WaitForLog(keePass, driver, Applied, "The settings dialog's OK never put what the tab held into the database.");
                _titleAfterOk = WaitForTitle(driver, true);

                _earlierClientAfterOk = Ask(keePass, SniName.For(earlierSniKey), admitted);
                Array.Clear(earlierSniKey, 0, earlierSniKey.Length);

                var token = Invite(driver);
                var client = new PairingClient(keePass.Port, token);
                Assert.That(client.Hello().ServerProved, Is.True, "the hello did not prove the new authority's gate");
                _paired = Approve(driver, client, "agent-invented", Profile);
                if (_paired.Status == 200)
                {
                    _pairedClientAsked = LiveGateClient.Get(keePass.Port, SniName.For(client.GateKey), client.Identity, GateOperations.StatusPath);
                }

                driver.Save();
                _titleAfterSaving = WaitForTitle(driver, false);

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            using (var reading = keePass.ReadAuthority())
            {
                _authorityInTheFile = reading.IsPresent ? reading.Material.AuthorityDigest : null;
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("The warning:");
            TestContext.Out.WriteLine(_warning);
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
        public void Before_the_start_the_client_is_answered_that_the_store_is_outdated()
        {
            Assert.That(_beforeStarting.Status, Is.EqualTo(503), Said(_beforeStarting));
            Assert.That(_beforeStarting.Body, Does.Contain("\"outdated\""));
        }

        [Test]
        public void The_page_states_the_word_and_names_Start_store_again_as_the_way_out()
        {
            Assert.That(_headline, Is.EqualTo(AuthorityDescription.HeldBackHeadline(StoreWords.Outdated)));
            Assert.That(_startLine, Is.Not.Null, "the line naming Start store again");
        }

        [Test]
        public void The_press_asks_first_and_names_what_has_to_be_entered_again()
        {
            Assert.That(_warningAppeared, Is.True);
            Assert.That(_warning, Does.Contain("Every client has to be paired again."));
            Assert.That(_warning, Does.Contain("Start it again?"));
        }

        [Test]
        public void After_the_press_the_page_reads_the_new_store_and_nothing_lands_before_OK()
        {
            Assert.That(_headlineAfterThePress, Is.Not.Null, "the page reads the new store's authority");
            Assert.That(_whileTheSettingsDialogStood.Status, Is.EqualTo(503), Said(_whileTheSettingsDialogStood));
        }

        [Test]
        public void After_OK_the_client_of_the_earlier_store_is_refused_at_the_handshake()
        {
            Assert.That(_earlierClientAfterOk.Answered, Is.False, Said(_earlierClientAfterOk));
        }

        [Test]
        public void A_pairing_under_the_new_authority_lands_and_is_served()
        {
            Assert.That(_paired, Is.Not.Null);
            Assert.That(_paired.Status, Is.EqualTo(200), _paired.ToString());
            Assert.That(_pairedClientAsked, Is.Not.Null);
            Assert.That(_pairedClientAsked.Status, Is.EqualTo(200), Said(_pairedClientAsked));
        }

        [Test]
        public void The_start_marked_the_database_changed_and_the_operator_s_save_kept_it()
        {
            Assert.That(_titleAfterOk, Does.Contain(UnsavedMark), "KeePass's title bar was '" + _titleAfterOk + "'");
            Assert.That(_titleAfterSaving, Does.Not.Contain(UnsavedMark), "KeePass's title bar was '" + _titleAfterSaving + "'");
            Assert.That(_authorityInTheFile, Is.Not.Null.And.Not.EqualTo(_earlierAuthority), "the file serves the new authority");
        }

        [Test]
        public void The_log_names_the_keys()
        {
            Assert.That(_pluginLog, Does.Contain("Start again: the store was started again under the key "));
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly after the start.");
        }

        [Test]
        public void Nothing_was_reported_but_the_word()
        {
            var unexpected = LiveSession.FailuresIn(_pluginLog)
                .Where(line => !line.Contains("not serving a database whose store is outdated"))
                .ToArray();

            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        private static LiveReply Ask(ThrowawayKeePass keePass, string gateName, X509Certificate2 certificate)
        {
            return LiveGateClient.Get(keePass.Port, gateName, certificate, GateOperations.StatusPath);
        }

        /// <summary>An invitation made on the Clients page and kept by the settings dialog's OK; its token.</summary>
        private static string Invite(KeePassDriver driver)
        {
            var dialog = driver.OpenDatabaseSettings();
            Assert.That(driver.SelectGateTab(dialog), Is.Not.Null, driver.Diagnostics);
            Assert.That(driver.SelectPage(dialog, GateTab.ClientsPageText), Is.Not.Null, driver.Diagnostics);
            Assert.That(driver.PressButton(dialog, GateTab.InviteButtonText), Is.True, driver.Diagnostics);
            var token = driver.ValueOf(dialog, GateTab.InvitationBoxName);
            Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);
            return token;
        }

        /// <summary>
        /// A request to pair on a thread of its own, approved on the dialog it raises: the request
        /// blocks until the operator answers.
        /// </summary>
        private static PairingReply Approve(KeePassDriver driver, PairingClient client, string name, params string[] profiles)
        {
            PairingReply reply = null;
            Exception failed = null;
            var asking = new Thread(() => { failed = Thrown.By(() => reply = client.Request(name, profiles)); }) { IsBackground = true };
            asking.Start();

            var approval = driver.WaitForWindow("asking to pair", DialogTimeout);
            Assert.That(approval, Is.Not.Null,
                "The approval dialog never appeared. " + (failed == null ? string.Empty : "The request failed first: " + failed + " ") + driver.Diagnostics);
            Assert.That(driver.PressButton(approval, GateTab.ApproveButtonText), Is.True, driver.Diagnostics);
            Assert.That(asking.Join(DialogTimeout), Is.True, "the client was still waiting after the operator answered");
            Assert.That(failed, Is.Null, "the request failed after the operator answered: " + failed);
            return reply;
        }

        /// <summary>
        /// Invokes a button found by its caption on a task of its own, waiting a little for it:
        /// a button whose press raises a modal dialog answers its invocation once that dialog has
        /// gone.
        /// </summary>
        private static bool Press(AutomationElement dialog, string caption)
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
