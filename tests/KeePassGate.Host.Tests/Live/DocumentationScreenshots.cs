using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Walks the whole operator flow through a real KeePass and screenshots it, so that the
    /// documentation shows what the plugin looks like rather than what it looked like once.
    /// </summary>
    /// <remarks>
    /// **The pictures are a build output that happens to be checked in**, which is the only
    /// arrangement that keeps them true. A screenshot pasted in by hand is correct on the day it is
    /// taken and silently wrong from the next change onwards, and nothing tells the reader which
    /// day that was. Regenerating them is one command, so a change to the tab is a change to the
    /// documentation in the same pass.
    ///
    /// **It is one session in one order**, because that is what the documentation describes: a
    /// database with no authority, an authority created, an invitation handed out, a client asking,
    /// the operator answering, and the client listed afterwards; and last, a copy of the database
    /// that made an authority of its own synchronized back in. Each step is screenshotted where it
    /// happens, so the images cannot show a sequence the plugin does not actually produce.
    ///
    /// The pairing half drives a real client from this process. Nothing else can make the approval
    /// dialog appear, and a dialog mocked up for a screenshot would be a drawing of the plugin
    /// rather than a picture of it.
    ///
    /// Every name in these images is invented. They are published, and what is legible in a
    /// screenshot is published with it.
    ///
    /// **Regenerate at one hundred per cent display scaling.** Nothing here declares itself
    /// aware of anything else, so the rectangle UI automation reports is in real pixels while
    /// the copy off the screen is of a surface Windows has scaled: the two disagree by whatever
    /// the scaling is, and every picture comes out cropped wrong. The failure is loud rather
    /// than quiet, since the first image already says it. Finding out still costs a run.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Category(TestCategories.Screenshots)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class DocumentationScreenshots
    {
        /// <summary>What the client is called while it pairs, and therefore in two of the images.</summary>
        private const string ClientName = "agent-invented";

        /// <summary>What the operator approves it for, and what the rules below then grant.</summary>
        private const string Profile = "reader";

        /// <summary>
        /// A second profile the same client is approved for.
        /// </summary>
        /// <remarks>
        /// **Two, because one screenshots the easy half of every surface that lists them.** A
        /// client holding one profile is a clients table that never shows a row growing to fit a
        /// list, an approval dialog with nothing to compare against, and a profiles page whose
        /// counts could all be ones by coincidence. It carries no rule anywhere, so the profiles
        /// page shows a name that costs a client something and grants that client nothing, which
        /// is the case the two counts beside a name exist to tell apart.
        /// </remarks>
        private const string SecondProfile = "ops";

        /// <summary>The second copy of the database, which the last picture merges back in.</summary>
        private const string OtherCopy = "other-copy.kdbx";

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private string _directory;

        private string _ruleOnTheRoot;
        private string _ruleOnTheEntry;

        private readonly List<string> _written = new List<string>();

        [OneTimeSetUp]
        public void ScreenshotTheWholeFlow()
        {
            _directory = Screenshot.DirectoryOrIgnore();
            _session = LiveSession.Begin();

            // The plugin's window shows the port twice, in the address and in its box, and every
            // example in the documentation uses this one. A session given another because this one
            // was taken would publish that instead, and the run would still end green.
            Assert.That(_session.KeePass.Port, Is.EqualTo(ThrowawayKeePass.Documented),
                "something on this machine holds port " + ThrowawayKeePass.Documented +
                ", so the pictures would show another. Free it and take them again.");

            _session.KeePass.FillWithInventedContent();

            // The rule the access pages are screenshotted showing. Written into the database rather
            // than chosen in the dialog: what the pictures are of is an editor reading a real rule,
            // and driving a dropdown to produce one would screenshot the automation instead.
            _session.KeePass.Grant("{\"" + Profile + "\":[\"read\",\"list\",\"attachments\"]}");

            // And the names that rule is written against, which is what the profiles page lists.
            // The second one carries no rule anywhere and is held by the same client, so the page
            // shows the two counts beside a name disagreeing, which is the pair of numbers it
            // exists to tell apart.
            _session.KeePass.NameProfiles(Profile, SecondProfile);

            Capture();
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
        public void Every_picture_the_documentation_needs_was_taken()
        {
            Assert.That(_written, Is.EquivalentTo(DocumentationImages.Expected),
                "The set of images written is not the set the documentation refers to.");

            foreach (var name in DocumentationImages.Expected)
            {
                var path = Path.Combine(_directory, name + ".png");
                Assert.That(File.Exists(path), Is.True, path + " was not written.");
                Assert.That(new FileInfo(path).Length, Is.GreaterThan(1024),
                    path + " is too small to be a picture of a dialog.");
            }
        }

        private void Capture()
        {
            // **Two sessions over one file, and the authority is why.** The pictures after the
            // first two show a fingerprint and two dates, and a minted authority has different ones
            // every run. A written down authority cannot simply be there from the start either,
            // because the first picture is of a database that has none. So: a session with no
            // authority for those two, then the file is seeded, then a session with it for the
            // rest. The banner reads the same throughout, because it is the same file.
            var driver = KeePassDriver.Start(_session.KeePass);

            // Listening is all there is to wait for here, and it is enough: this database has no
            // authority, so nothing is ever served for it and nothing is asked of it over the wire.
            WaitUntilListening(driver, 0);

            // The plugin's own window first, because it is what an operator opens first: it says
            // what is loaded and whether anything is listening, before any database is involved.
            var status = driver.OpenStatusWindow();
            Assert.That(status, Is.Not.Null, "the Tools menu opened nothing. " + driver.Diagnostics);

            // On OK rather than where the window puts it. The window opens with the port box
            // focused deliberately, and a focused text box blinks: measured, that is the whole of
            // the difference between two runs of this picture. Parking it on a button leaves the
            // window looking exactly as it does between blinks, which is half of what an operator
            // sees anyway.
            var ok = Uia.ById(status, ControlType.Button, GateTab.StatusOkButtonName);
            Assert.That(ok, Is.Not.Null, "the window has no OK button to rest the focus on.");

            Shoot(status, DocumentationImages.PluginStatus, ok);
            driver.CloseDialog(status);
            Thread.Sleep(500);

            AutomationElement settings = driver.OpenDatabaseSettings();
            driver.SelectGateTab(settings);

            Shoot(settings, DocumentationImages.AuthorityBeforeCreating);

            driver.CloseDialog(settings);
            Thread.Sleep(500);

            // Everything from here is about a database that has an authority, so it gets the one
            // that was written down rather than one made this minute.
            _session.KeePass.Stop();
            Thread.Sleep(1000);
            _session.KeePass.SeedAuthority();

            driver = KeePassDriver.Start(_session.KeePass);
            WaitUntilServing(driver, 1, 0);

            settings = driver.OpenDatabaseSettings();
            driver.SelectGateTab(settings);
            Shoot(settings, DocumentationImages.AuthorityAfterCreating);
            driver.CloseDialog(settings);
            Thread.Sleep(500);

            // **The admitted client arrives here and not with the authority.** The picture above is
            // captioned as the tab once the authority exists, and the page it is taken from is the
            // one the getting-started walkthrough puts directly under "Press Create authority": a
            // reader who has just pressed it has admitted nobody, so a picture counting one
            // admission record is a picture of somebody else's database. Everything below needs the
            // record, and nothing above it does.
            _session.KeePass.Stop();
            Thread.Sleep(1000);
            _session.KeePass.SeedAdmittedClient();

            driver = KeePassDriver.Start(_session.KeePass);
            WaitUntilServing(driver, 2, 1);

            settings = driver.OpenDatabaseSettings();
            driver.SelectGateTab(settings);
            driver.SelectPage(settings, GateTab.ClientsPageText);
            Assert.That(driver.PressButton(settings, GateTab.InviteButtonText), Is.True, driver.Diagnostics);
            Thread.Sleep(700);
            Shoot(settings, DocumentationImages.ClientsInvitation);

            var token = driver.ValueOf(settings, GateTab.InvitationBoxName);
            Assert.That(token, Is.Not.Null.And.Not.Empty, "the tab handed out no invitation");

            ApproveAClient(driver, token);

            // Away and back, because the page reads the database when it comes forward and the
            // counts on it are taken then. Selecting the page it is already on would change
            // nothing. The client in the list is the seeded one: the pairing above is refused, so
            // it writes no record.
            driver.SelectGateTab(settings);
            driver.SelectPage(settings, GateTab.AuthorityPageText);
            Thread.Sleep(400);
            driver.SelectPage(settings, GateTab.ClientsPageText);
            Thread.Sleep(900);
            Shoot(settings, DocumentationImages.ClientsAdmitted);

            // The profiles page last, and after the pairing rather than before it, because the two
            // counts beside a name are the whole of what that page has to show and both of them
            // are zero until a client holds something. Coming to the page is what recounts them:
            // the tab reads the database again whenever one of its pages comes forward.
            driver.SelectPage(settings, GateTab.ProfilesPageText);
            Thread.Sleep(900);
            Shoot(settings, DocumentationImages.Profiles);

            driver.CloseDialog(settings);
            Thread.Sleep(700);

            var group = driver.OpenGroupDialog();
            SelectAccessTab(group);

            // Let the page finish drawing. A tab that has just come forward reports its bounds
            // before it has painted, and what a camera gets then is a black rectangle.
            Thread.Sleep(700);
            Shoot(group, DocumentationImages.GroupAccess, Seal(group));
            driver.CloseDialog(group);
            Thread.Sleep(700);

            var entry = driver.OpenEntryDialog();
            SelectAccessTab(entry);

            // The same wait, for the same reason, and this tab needs it more than the one above:
            // it lists what the group grants as well as what the entry does, so there is more to
            // paint before the bounds it reports mean anything.
            Thread.Sleep(700);
            Shoot(entry, DocumentationImages.EntryAccess, Seal(entry));
            driver.CloseDialog(entry);

            driver.ShutDown();

            _ruleOnTheRoot = _session.KeePass.ReadRootCustomData(AclDocument.CustomDataKey);
            foreach (var stored in _session.KeePass.ReadEntries())
            {
                var found = stored.CustomData.Get(AclDocument.CustomDataKey);
                if (!string.IsNullOrEmpty(found))
                {
                    _ruleOnTheEntry = found;
                }
            }

            Assert.That(_ruleOnTheRoot, Does.Contain(Profile),
                "the rule the pictures are of is not in the database they were taken from");

            AskWhichAuthorityToKeep();
        }

        /// <summary>
        /// Two copies of this database that each replaced the authority they shared, merged by a
        /// real synchronize, and the dialog that raises: screenshotted with this copy's authority
        /// selected, and then canceled.
        /// </summary>
        /// <remarks>
        /// **Last, because it ends the database every picture before it describes.** A recreate
        /// retires the authority and takes every client with it, so nothing earlier in the
        /// walkthrough could be photographed after this.
        ///
        /// **Both authorities, their clients and the one request the audit remembers are written
        /// down**, in `SeededCopies`, for the reasons `SeededAuthority` and its client are: the
        /// dialog shows two fingerprints, when each authority was made and until when it and its
        /// host certificate hold, and for the selected one when each client was admitted and when
        /// it last made a request. Minted, paired or asked for, every one of those would differ on
        /// every run.
        ///
        /// **Merged by the menu, not written merged.** The file could have been given both
        /// authorities before KeePass opened it, and the dialog would have come up at the open. The
        /// page describes two copies synchronized, so that is what is photographed.
        ///
        /// **This copy's authority is selected before the shutter**, rather than whichever sorts
        /// first, because it is the one with two clients: one the audit remembers and one it does
        /// not, which are the two things its last column says. The focus is left on the list of
        /// authorities, where it rests when the dialog opens, rather than on the box of the full
        /// fingerprint below it, where a caret would blink.
        ///
        /// **Canceled rather than answered**, because the picture is of the question and nothing
        /// after it needs an answer.
        /// </remarks>
        private void AskWhichAuthorityToKeep()
        {
            var keePass = _session.KeePass;
            var other = keePass.CopyDatabase(OtherCopy);

            var ours = keePass.SeedRecreatedAuthority(
                keePass.DatabasePath,
                SeededCopies.ThisCopyAuthority,
                SeededCopies.ThisCopyHost,
                SeededCopies.ThisCopySniKey);
            keePass.SeedRecreatedAuthority(
                other,
                SeededCopies.OtherCopyAuthority,
                SeededCopies.OtherCopyHost,
                SeededCopies.OtherCopySniKey);

            keePass.SeedAdmittedClient(keePass.DatabasePath, SeededCopies.AgentKey, SeededCopies.AgentRecord);
            keePass.SeedAdmittedClient(keePass.DatabasePath, SeededCopies.BackupKey, SeededCopies.BackupRecord);
            keePass.SeedAdmittedClient(other, SeededCopies.OtherAgentKey, SeededCopies.OtherAgentRecord);

            keePass.SeedLastRequest(
                AuthorityKeys.AdmissionKeyIn(SeededCopies.AgentKey), ours, SeededCopies.AgentLastRequest);

            // Counted before this KeePass starts rather than written down, because how often an
            // earlier session said so is that session's business.
            var log = keePass.ReadPluginLog();
            var driver = KeePassDriver.Start(keePass);
            WaitUntilServing(driver, Occurrences(log, ListeningLine), Occurrences(log, ServingLine));

            driver.Synchronize(other);

            var chooser = driver.WaitForWindowById(GateTab.ChooserFormName, DialogTimeout);
            Assert.That(chooser, Is.Not.Null, "The synchronize raised no chooser. " + driver.Diagnostics);

            driver.SelectAuthority(chooser, CertificateDigest.Shorten(ours));
            // By its identifier alone, for the reason `KeePassDriver.SelectAuthority` gives: the list
            // is a `DataGrid` to UI automation, and a `List` lookup would park the focus nowhere.
            Shoot(
                chooser,
                DocumentationImages.AuthorityChooser,
                Uia.ById(chooser, GateTab.ChooserAuthorityListName));

            Assert.That(driver.PressButton(chooser, GateTab.ChooserCancelButtonText), Is.True, driver.Diagnostics);
            driver.ShutDown();
        }

        [Test]
        public void The_entry_picture_shows_an_inherited_rule_rather_than_its_own()
        {
            // What the entry page is worth showing at all: a rule made on the group above, seen
            // from an entry that does not carry one. If the rule had landed on the entry the
            // picture would teach the reader the opposite of how this works.
            Assert.That(_ruleOnTheRoot, Is.Not.Null.And.Not.Empty,
                "the rule the pictures were taken of is not on the group");
            Assert.That(_ruleOnTheEntry, Is.Null,
                "an entry carries a rule of its own, so the picture is not of an inherited one");
        }

        /// <summary>
        /// Runs a real pairing, screenshots the dialog KeePass raises, and refuses it.
        /// </summary>
        /// <remarks>
        /// KeePass's own dialog rather than one built here, which costs a wait and is worth it: a
        /// dialog constructed in the test host is drawn by a process nobody runs, and a picture of
        /// one would go on looking right through a layout change that only KeePass would show.
        ///
        /// The countdown is what made that hard, and <see cref="Countdown"/> is the answer.
        /// </remarks>
        private void ApproveAClient(KeePassDriver driver, string token)
        {
            var client = new PairingClient(_session.KeePass.Port, token);
            Assert.That(client.Hello().ServerProved, Is.True, "the gate did not prove itself");

            var asking = new Thread(
                () => client.Request(ClientName, new[] { Profile, SecondProfile })) { IsBackground = true };
            asking.Start();

            var approval = driver.WaitForWindow("asking to pair", DialogTimeout);
            Assert.That(approval, Is.Not.Null, "The approval dialog never appeared. " + driver.Diagnostics);

            // Five seconds in. The number in the picture is a real one and it is the same number
            // every run, because the wait is for the tick into that second rather than for the
            // value: see `Countdown`. Read off the options the plugin itself uses, so the total is
            // not a number written down in two places.
            var second = (int)new PairingOptions().ApprovalTimeout.TotalSeconds - 5;
            Shoot(approval, DocumentationImages.PairingApproval, null, new Countdown(approval, second));

            // Refused, not approved. The dialog is what this picture is of; the client in the list
            // below comes from the seed, and approving as well would put two in a list the
            // documentation describes as holding one.
            Assert.That(driver.PressButton(approval, GateTab.RefuseButtonText), Is.True, driver.Diagnostics);
            Assert.That(asking.Join(TimeSpan.FromSeconds(60)), Is.True,
                "the client was still waiting after the operator answered");
        }

        private static void SelectAccessTab(AutomationElement dialog)
        {
            var tab = Uia.WaitFor(
                "the " + GrantTab.Text + " tab",
                () => Uia.Descendant(dialog, ControlType.TabItem, name => name == GrantTab.Text));

            object pattern;
            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                ((SelectionItemPattern)pattern).Select();
                Thread.Sleep(700);
            }
        }

        /// <summary>
        /// The check box on the grant tab, which is where the focus rests for a picture of it.
        /// </summary>
        /// <remarks>
        /// Off the grid, in other words. A grid cell holding the focus draws itself selected, and a
        /// row that looks chosen reads as one the operator has just chosen, which is not a thing
        /// this picture is showing. A cell also publishes as an editable one, caret and all, which
        /// is the race `plugin-status.png` lost: see <see cref="Screenshot.Capture"/>'s
        /// `parkFocusOn`.
        /// </remarks>
        private static AutomationElement Seal(AutomationElement dialog)
        {
            return Uia.ById(dialog, ControlType.CheckBox, GrantTab.SealCheckBoxName);
        }

        private void Shoot(
            AutomationElement window,
            string name,
            AutomationElement parkFocusOn = null,
            ISteadyMoment moment = null)
        {
            // Before the shutter rather than at the end, because what it replaces is what the
            // picture would otherwise carry: a fingerprint minted this minute, a code of
            // thirty-two random bytes.
            Screenshot.Capture(_directory, window, name, parkFocusOn, moment);
            _written.Add(name);
        }

        /// <summary>
        /// The plugin is loaded and its gate is up. All a session gets whose database has no
        /// authority: nothing is served for one, so there is no later line to wait for.
        /// </summary>
        /// <param name="already">
        /// How many times the plugin has said it already, in an earlier session of this same
        /// KeePass. The log is opened for append and the port does not change, so asking whether
        /// the line is present answers yes on the first poll after a restart and proves nothing
        /// about the gate that has just started. Every KeePass this fixture starts after the first
        /// is such a restart, over the same log.
        /// </param>
        private void WaitUntilListening(KeePassDriver driver, int already)
        {
            WaitForLog(driver, ListeningLine, already, "The plugin never reported a listening gate.");
        }

        /// <summary>What the plugin logs once its gate is up on this session's port.</summary>
        private string ListeningLine
        {
            get { return "listening on 127.0.0.1:" + _session.KeePass.Port; }
        }

        /// <summary>What it logs once the database KeePass opened is served.</summary>
        private const string ServingLine = "serving a database with authority";

        /// <summary>
        /// Ready is the database being served, not the socket being open.
        /// </summary>
        /// <remarks>
        /// The listener starts inside the plugin's `Initialize`, and KeePass opens the database it
        /// was given on its command line only once its plugins are initialized. Until it does,
        /// there is no target: the tab has no authority to describe, and a connection resolves no
        /// name and is dropped before any TLS answer, which is the gate being fail-closed and is
        /// correct. `serving a database with authority` is written when the target is built, which
        /// the unlock that opening is does, so it is the moment both of those become true. The
        /// pairing below sends on it, and every picture from here on is of a page that read the
        /// database.
        ///
        /// Both lines, in that order, because the serving line on its own would not tell a gate
        /// that never started from one that started and has nothing to serve yet.
        /// </remarks>
        /// <param name="listened">
        /// How many times an earlier session of this same KeePass has reported a listening gate.
        /// </param>
        /// <param name="served">
        /// The same count for the serving line, which runs one behind: the first session's
        /// database has no authority yet, so that session is never served and never says so.
        /// </param>
        private void WaitUntilServing(KeePassDriver driver, int listened, int served)
        {
            WaitUntilListening(driver, listened);

            WaitForLog(driver, ServingLine, served,
                "The plugin never reported serving the database KeePass was started with.");
        }

        private void WaitForLog(KeePassDriver driver, string marker, int already, string failure)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Occurrences(_session.KeePass.ReadPluginLog(), marker) > already)
                {
                    return;
                }

                Thread.Sleep(250);
            }

            Assert.Fail(failure + " " + driver.Diagnostics);
        }

        private static int Occurrences(string log, string marker)
        {
            var found = 0;
            var at = log.IndexOf(marker, StringComparison.Ordinal);
            while (at >= 0)
            {
                found++;
                at = log.IndexOf(marker, at + marker.Length, StringComparison.Ordinal);
            }

            return found;
        }
    }
}
