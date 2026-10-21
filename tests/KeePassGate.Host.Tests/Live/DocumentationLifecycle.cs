using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Transport;
using KeePassGate.Ui;
using NUnit.Framework;
using Run = KeePassGate.Host.Tests.Live.ReferenceClient.Run;
using Side = KeePassGate.Host.Tests.Live.Storyboard.Side;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The moving picture: the whole journey from a database holding nothing but entries to a
    /// client refused after its revocation, every click on the way, with the operator's KeePass on
    /// one side and the client's terminal on the other.
    /// </summary>
    /// <remarks>
    /// **A journey somebody can follow.** It starts where a reader starts, at a database with
    /// entries in two folders and nothing else, and every dialog is opened the way an operator
    /// opens it, from the menu bar, with the pointer going there first. The authority is created,
    /// an invitation handed out and copied, the client pairs and is approved, the client is shown
    /// to see nothing until a rule says otherwise, the rules are written on the group dialogs, one
    /// folder opened and the other closed, and the client reads one and is refused the other. Then
    /// it is revoked, and refused everything.
    ///
    /// **All of it real, with one exception said here.** The client is PyKeePassGate's own command
    /// line, run through `uv` as the reference client tests run it, typing the commands the
    /// documentation gives; the dialogs are KeePass's, pressed where an operator presses them; and
    /// every answer in the terminal is what the gate answered. The exception is Create authority:
    /// the press is drawn and not made, and the pictures after it are of the written down
    /// authority, put into the file between two sessions, for the reason `Capture` gives. A minted
    /// one shows a fingerprint that is new on every run, here and in the client's answer.
    ///
    /// **Its own database, the one the fixture started from.** A copy is taken before anything
    /// else is written into the file and put back here, last, since a client paired and revoked
    /// for real leaves its record and revocation behind. The entry the root holds moves into
    /// Automation first, see <see cref="ThrowawayKeePass.MoveRootEntryToAutomation"/>.
    ///
    /// **The invitation is pasted, as a person pastes it.** The code is copied with the tab's Copy
    /// button, and the client asks for it at a prompt that does not echo it, which is what keeps
    /// it out of a shell's history. What really reaches the client is the same code, written to its
    /// standard input; at a terminal it would have read the paste.
    ///
    /// **Steadied as the stills are, and where that cannot reach, replaced.** The port is the
    /// documented one; the entries and groups carry written down identifiers; the authority, and
    /// so the database the client is told it paired with, is the seeded one. What is left is made
    /// by the client itself: its certificate's key and the moment it expires, which the terminal
    /// shows as invented values, and the directory it stored them in, a `LOCALAPPDATA` this session
    /// made, which the terminal shows by the variable's name. On the KeePass side the moments on
    /// the client's row are covered, see `Redacted.Moments`.
    /// </remarks>
    public sealed partial class DocumentationScreenshots
    {
        /// <summary>The copy of the database as the fixture filled it, before any authority or rule.</summary>
        private const string PristineCopy = "before-the-authority.kdbx";

        /// <summary>What KeePass's tree calls the root group of the invented database.</summary>
        private const string VaultGroup = "Invented Vault";

        /// <summary>The folder the client may read, and the entry in it whose password it changes.</summary>
        private const string AutomationGroup = "Automation";

        private const string BuildAgentTitle = "Build agent";

        /// <summary>
        /// The button beside an entry's password that shows it, as KeePass's entry dialog names it:
        /// a check box drawn as a button.
        /// </summary>
        private const string RevealPassword = "m_cbHidePassword";

        /// <summary>KeePass's entry view, the details of the chosen entry under the main window's list.</summary>
        private const string EntryView = "m_richEntryView";

        /// <summary>The verbs the rule on the root grants, as the rule picker lists them.</summary>
        private static readonly string[] ReaderVerbs =
        {
            "R   read", "L   list", "P   protected (apply checked permissions)"
        };

        /// <summary>
        /// What the terminal shows for the client's admission key, which is new every run because
        /// the client makes its own key.
        /// </summary>
        private const string InventedKey = "5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed";

        /// <summary>
        /// What it shows for when the certificate expires: a year after the seeded client was
        /// admitted, which is the lifetime the dialog offers a client that asks for none.
        /// </summary>
        private const string InventedExpiry = "2027-08-22T08:38:01Z";

        /// <summary>What it shows for the `LOCALAPPDATA` the client was given.</summary>
        private const string InventedHome = "%LOCALAPPDATA%";

        private static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

        private string _pristine;

        /// <summary>
        /// Whether a client has changed an entry in this film yet. KeePass's entry view under the
        /// main window's list says when the chosen entry was last changed, which is a time written
        /// into the database until a client changes one, and this minute after it, so from then
        /// on every picture of the main window covers it.
        /// </summary>
        private static bool _entryChangedNow;

        private void FilmTheLifecycle()
        {
            var keePass = _session.KeePass;
            _entryChangedNow = false;
            keePass.RestoreDatabase(_pristine);
            keePass.MoveRootEntryToAutomation();
            keePass.GiveTheBuildAgentAPassword();

            // On the port nobody has to name, so the command typed is the one a reader types.
            keePass.UseTheDefaultPort();

            var home = Path.Combine(Path.GetDirectoryName(keePass.DatabasePath), "home");
            Directory.CreateDirectory(home);
            var environment = ClientEnvironment(home);

            using (var story = new Storyboard())
            {
                story.Invent(home, InventedHome);
                FilmTheJourney(story, keePass, environment);

                var path = Path.Combine(_directory, DocumentationImages.FileName(DocumentationImages.Lifecycle));
                story.Write(path);
                TestContext.Out.WriteLine("wrote " + path + " (" + new FileInfo(path).Length + " bytes)");
                _written.Add(DocumentationImages.Lifecycle);
            }
        }

        private void FilmTheJourney(Storyboard story, ThrowawayKeePass keePass, IDictionary<string, string> environment)
        {
            // The first session, with no authority: the database as a reader has it, and the tab
            // before Create authority is pressed.
            var log = keePass.ReadPluginLog();
            var driver = KeePassDriver.Start(keePass);
            WaitUntilListening(driver, Occurrences(log, ListeningLine));

            var opened = TakeMain(story, driver, "the main window as it opens");
            var search = SearchBox(driver);
            var searched = Search(story, driver, search);

            var settingsMenu = OpenMenu(story, driver, "File", "Database Settings");
            var settings = driver.OpenDatabaseSettings();
            var general = TakeOpened(story, driver, settings, "the settings as they open");
            var gateTab = Click(settings, Tab(settings, GateTab.Text));
            var noAuthority = Take(story, driver, settings, "the KPG tab with no authority", null);
            var create = KeePassDriver.Bounds(Button(settings, GateTab.CreateButtonText));
            driver.CloseDialog(settings);
            Assert.That(driver.ShutDown(), Is.True, "KeePass did not exit cleanly before the authority");

            story.Step(Side.Operator, "A database with entries in two folders. Searching for nothing lists every one");
            story.Show(opened);
            story.PointerAt(Middle(opened.Area));
            story.Hold(150);
            story.MoveTo(search);
            story.Click();
            story.Keys("Enter");
            story.Show(searched);
            story.Hold(200);

            story.Step(Side.Operator, "File, Database Settings opens the database's own settings");
            Choose(story, settingsMenu, searched, general);
            story.Hold(80);

            // The second session, with the written down authority, for the reason `Capture` gives.
            keePass.SeedAuthority();
            log = keePass.ReadPluginLog();
            driver = KeePassDriver.Start(keePass);
            WaitUntilServing(driver, Occurrences(log, ListeningLine), Occurrences(log, ServingLine));

            searched = Search(story, driver, SearchBox(driver));
            settings = driver.OpenDatabaseSettings();
            Click(settings, Tab(settings, GateTab.Text));
            var created = Take(story, driver, settings, "the KPG tab with its authority", null);
            var ok = Button(settings, "OK");
            var okArea = KeePassDriver.Bounds(ok);
            Press(settings, ok);

            story.Step(Side.Operator, "On the KPG tab, Create authority makes the database its own certificate authority");
            story.MoveTo(gateTab);
            story.Click(searched, noAuthority);
            story.Hold(100);
            story.MoveTo(create);
            story.Click(searched, created);
            story.Hold(160);

            story.Step(Side.Operator, "OK applies it, and from then on KeePass serves this database");
            story.MoveTo(okArea);
            story.Click(searched);
            story.Hold(120);

            // The invitation, copied, and the dialog closed with OK, which keeps it: Cancel would
            // take it back.
            settingsMenu = OpenMenu(story, driver, "File", "Database Settings");
            settings = driver.OpenDatabaseSettings();
            general = TakeOpened(story, driver, settings, "the settings as they open");
            gateTab = Click(settings, Tab(settings, GateTab.Text));
            var authorityPage = Take(story, driver, settings, "the KPG tab", null);
            var clientsTab = Click(settings, Tab(settings, GateTab.ClientsPageText));
            var uninvited = Take(story, driver, settings, "the clients page", null);
            var invite = Click(settings, Button(settings, GateTab.InviteButtonText));
            Thread.Sleep(700);
            var invited = Take(story, driver, settings, "the clients page with an invitation out", null);
            var token = driver.ValueOf(settings, GateTab.InvitationBoxName);
            Assert.That(token, Is.Not.Null.And.Not.Empty, "the tab handed out no invitation");
            var copy = Click(settings, Uia.ById(settings, ControlType.Button, GateTab.CopyButtonName));
            Thread.Sleep(500);
            var copied = Take(story, driver, settings, "the clients page once the code is copied", null);
            var kept = Click(settings, Button(settings, "OK"));
            Thread.Sleep(700);

            story.Step(Side.Operator, "Back in the settings, the Clients page hands out an invitation for one client");
            Choose(story, settingsMenu, searched, general);
            story.MoveTo(gateTab);
            story.Click(searched, authorityPage);
            story.MoveTo(clientsTab);
            story.Click(searched, uninvited);
            story.Hold(60);
            story.MoveTo(invite);
            story.Click(searched, invited);
            story.Hold(160);

            story.Step(Side.Operator, "Copy takes the code, and OK keeps it: Cancel would take the invitation back");
            story.MoveTo(copy);
            story.Click(searched, copied);
            story.Hold(100);
            story.MoveTo(kept);
            story.Click(searched);
            story.Hold(100);

            // The pairing, typed as the documentation gives it, and the code pasted when asked.
            // `--port` only when something else held the default and the gate had to go elsewhere,
            // which makes a picture that differs from the published one and says why.
            var port = keePass.Port == GateOptions.DefaultPort ? string.Empty : " --port " + keePass.Port;
            var pairing = "pykeepassgate pair" + port + " --name " + ClientName + " --profile-name " + Profile;

            story.Step(Side.Client, "The client asks to pair, and the code is pasted at a prompt that shows only stars");
            story.Hold(60);
            story.Type(pairing);
            story.Ask("Invitation: ");
            story.Hold(60);

            // A star for every character, as the client's own prompt shows a paste at a terminal.
            // The real run hands the code over a pipe, where nothing is shown, so this is drawn.
            story.Echo(new string('*', token.Length));
            story.Keys("Ctrl+V");
            story.Keys("Enter");
            story.Answered();
            story.Hold(60);

            Storyboard.Shot approval;
            Rectangle approve;
            var paired = Pair(driver, story, pairing, token, environment, out approval, out approve);
            Assert.That(paired.ExitCode, Is.Zero, paired.ToString());

            story.Invent(Printed(paired, "admission key:"), InventedKey);
            story.Invent(Printed(paired, "expires:"), InventedExpiry);

            story.Step(Side.Operator,
                "KeePass asks the operator, naming the client and the profile it asked for, and the operator approves");
            story.Show(searched, approval);
            story.Hold(260);
            story.MoveTo(approve);
            story.Click(searched);
            story.Hold(80);

            story.Step(Side.Client, "The client holds a certificate now");
            story.Print(paired);
            story.Hold(200);

            var nothing = Command(story, "pykeepassgate entries", environment,
                "But no rule grants reader anything yet, so to the client the database is empty");
            Assert.That(nothing.ExitCode, Is.EqualTo(1), nothing.ToString());

            // The rule on the top group, written on its KPG tab.
            var vault = Click(driver.MainWindow, TreeItem(driver, VaultGroup));
            Thread.Sleep(500);
            var vaultChosen = TakeMain(story, driver, "the top group chosen");
            var groupMenu = OpenMenu(story, driver, "Group", "Edit Group");
            var group = driver.OpenGroupDialog();
            var groupOpened = TakeOpened(story, driver, group, "the group dialog as it opens");
            var groupTab = Click(group, Tab(group, GrantTab.Text));
            var noRule = Take(story, driver, group, "the group's KPG tab with no rule", null);

            var addRow = GrantCell(group, GrantTab.AddRowPrompt);
            var profiles = Take(story, driver, group, "the profiles offered", new PickerOpen(driver, addRow, GrantTab.ProfilePickerName));
            var reader = PickerItem(driver, GrantTab.ProfilePickerName, Profile);

            // The rule picker runs past the dialog's right edge, so it is opened before the picture
            // rather than inside it, and the picture reaches as far as it does: a picture of the
            // dialog alone cut its rows off halfway, and the ticks landed beside what was drawn.
            new PickerOpen(driver, reader, GrantTab.RulePickerName).WaitForIt();
            var picker = KeePassDriver.Bounds(driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.FromSeconds(5)));
            var verbs = Take(story, driver, group, "the rule picker", new PickerOpen(driver, Rectangle.Empty, GrantTab.RulePickerName), reach: picker);
            var verbAreas = ReaderVerbs.Select(verb => CheckBox(PickerItem(driver, GrantTab.RulePickerName, verb))).ToArray();
            // One picture per tick, so each press shows what it did as it lands: a picture of all of
            // them drew the first presses as doing nothing and the last as ticking every box.
            var ticks = new Storyboard.Shot[verbAreas.Length];
            for (var i = 0; i < verbAreas.Length; i++)
            {
                ticks[i] = Take(story, driver, group, ReaderVerbs[i] + " ticked", new Ticked(driver, verbAreas[i]), reach: picker);
            }

            Keyboard.SendEnter();
            Thread.Sleep(700);
            var ruled = Take(story, driver, group, "the rule on the top group", null, null, Seal(group));
            var groupOk = Click(group, Button(group, "OK"));
            Thread.Sleep(700);

            story.Step(Side.Operator, "Choose the top group, then Group, Edit Group");
            story.Show(searched);
            story.MoveTo(vault);
            story.Click(vaultChosen);
            story.Hold(60);
            Choose(story, groupMenu, vaultChosen, groupOpened);
            story.Hold(60);

            story.Step(Side.Operator, "On its KPG tab, choose the profile reader and tick what it may do: read, list, and see passwords");
            story.MoveTo(groupTab);
            story.Click(vaultChosen, noRule);
            story.Hold(60);
            story.MoveTo(addRow);
            story.Click(vaultChosen, profiles);
            story.Hold(60);
            story.MoveTo(reader);
            story.Click(vaultChosen, verbs);
            story.Hold(60);
            for (var i = 0; i < verbAreas.Length; i++)
            {
                story.MoveTo(verbAreas[i]);
                story.Click(vaultChosen, ticks[i]);
            }

            story.Hold(60);
            story.Keys("Enter");
            story.Show(vaultChosen, ruled);
            story.Hold(120);

            story.Step(Side.Operator, "OK keeps the rule, and it holds for every folder and entry below");
            story.MoveTo(groupOk);
            story.Click(vaultChosen);
            story.Hold(100);

            // The seal on the other folder, which closes it to every profile it does not name.
            var services = Click(driver.MainWindow, TreeItem(driver, ThrowawayKeePass.InventedServices));
            Thread.Sleep(500);
            var servicesChosen = TakeMain(story, driver, "the second folder chosen");
            groupMenu = OpenMenu(story, driver, "Group", "Edit Group");
            group = driver.OpenGroupDialog();
            groupOpened = TakeOpened(story, driver, group, "the group dialog as it opens");
            groupTab = Click(group, Tab(group, GrantTab.Text));
            var inherited = Take(story, driver, group, "the inherited rule", null, null, Seal(group));
            var seal = Click(group, Seal(group));
            Thread.Sleep(500);
            var sealedOff = Take(story, driver, group, "the folder sealed", null, null, Seal(group));
            groupOk = Click(group, Button(group, "OK"));
            Thread.Sleep(700);

            story.Step(Side.Operator, "Invented Services is to stay private: choose it, then Group, Edit Group");
            story.MoveTo(services);
            story.Click(servicesChosen);
            story.Hold(60);
            Choose(story, groupMenu, servicesChosen, groupOpened);
            story.Hold(60);

            story.Step(Side.Operator, "Its KPG tab shows the rule arriving from above. Refusing any profile not named here stops it");
            story.MoveTo(groupTab);
            story.Click(servicesChosen, inherited);
            story.Hold(160);
            story.MoveTo(seal);
            story.Click(servicesChosen, sealedOff);
            story.Hold(160);
            story.MoveTo(groupOk);
            story.Click(servicesChosen);
            story.Hold(80);

            searched = Search(story, driver, SearchBox(driver));
            story.Step(Side.Operator, "Searching for nothing again: the database holds four entries");
            story.MoveTo(search);
            story.Click();
            story.Keys("Enter");
            story.Show(searched);
            story.Hold(160);

            var listed = Command(story, "pykeepassgate entries", environment,
                "The client sees the two in Automation, and nothing of Invented Services");
            Assert.That(listed.ExitCode, Is.Zero, listed.ToString());
            Assert.That(listed.Output, Does.Contain(ThrowawayKeePass.BuildAgentUuid), listed.ToString());
            Assert.That(listed.Output, Does.Contain(ThrowawayKeePass.MailboxUuid), listed.ToString());
            Assert.That(listed.Output, Does.Not.Contain(ThrowawayKeePass.FictitiousApiUuid), listed.ToString());

            var shown = Command(story, "pykeepassgate show " + ThrowawayKeePass.BuildAgentUuid, environment,
                "An entry in Automation reads in full, its password included");
            Assert.That(shown.ExitCode, Is.Zero, shown.ToString());
            Assert.That(shown.Output, Does.Contain(ThrowawayKeePass.BuildAgentPassword),
                "the rule grants P, so the password is printed rather than withheld: " + shown);

            var refused = Command(story, "pykeepassgate show " + ThrowawayKeePass.FictitiousApiUuid, environment,
                "One in Invented Services is refused, answered as if it were not there");
            Assert.That(refused.ExitCode, Is.EqualTo(1), refused.ToString());

            // A new password, refused while the rule holds no U, and set once it does.
            var update = "pykeepassgate update " + ThrowawayKeePass.BuildAgentUuid + " --password";
            var tooSoon = Update(story, update, environment,
                "Setting a new password is refused: the rule lets the client read, not update");
            Assert.That(tooSoon.ExitCode, Is.EqualTo(1), tooSoon.ToString());

            // U added to the rule on the top group, in the picker its mask opens.
            vault = Click(driver.MainWindow, TreeItem(driver, VaultGroup));
            Thread.Sleep(500);
            vaultChosen = TakeMain(story, driver, "the top group chosen again");
            groupMenu = OpenMenu(story, driver, "Group", "Edit Group");
            group = driver.OpenGroupDialog();
            groupOpened = TakeOpened(story, driver, group, "the group dialog opened again");
            groupTab = Click(group, Tab(group, GrantTab.Text));
            var ruleBefore = Take(story, driver, group, "the rule before update", null, null, Seal(group));
            var mask = GrantCell(group, ".R...L.P");
            new PickerOpen(driver, mask, GrantTab.RulePickerName).WaitForIt();
            picker = KeePassDriver.Bounds(driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.FromSeconds(5)));
            var ruleOpen = Take(story, driver, group, "the rule picker on the rule", new PickerOpen(driver, Rectangle.Empty, GrantTab.RulePickerName), reach: picker);
            var updateBox = CheckBox(PickerItem(driver, GrantTab.RulePickerName, "U   update"));
            var updateTicked = Take(story, driver, group, "update ticked", new Ticked(driver, updateBox), reach: picker);
            Keyboard.SendEnter();
            Thread.Sleep(700);
            var ruleAfter = Take(story, driver, group, "the rule with update", null, null, Seal(group));
            groupOk = Click(group, Button(group, "OK"));
            Thread.Sleep(700);

            story.Step(Side.Operator, "To allow it, the rule on the top group gains U: Group, Edit Group, KPG, then the rule's mask");
            story.Show(searched);
            story.MoveTo(vault);
            story.Click(vaultChosen);
            story.Hold(60);
            Choose(story, groupMenu, vaultChosen, groupOpened);
            story.MoveTo(groupTab);
            story.Click(vaultChosen, ruleBefore);
            story.Hold(60);
            story.MoveTo(mask);
            story.Click(vaultChosen, ruleOpen);
            story.Hold(60);
            story.MoveTo(updateBox);
            story.Click(vaultChosen, updateTicked);
            story.Hold(60);
            story.Keys("Enter");
            story.Show(vaultChosen, ruleAfter);
            story.Hold(120);
            story.MoveTo(groupOk);
            story.Click(vaultChosen);
            story.Hold(80);

            var changed = Update(story, update, environment, "The same command now sets the new password");
            Assert.That(changed.ExitCode, Is.Zero, changed.ToString());

            // From here on the entry view under the main window's list says the entry was changed
            // this minute whenever it is chosen, so every picture of the main window covers it.
            _entryChangedNow = true;

            // KeePass shows it: the entry's dialog, and the button beside the password that reveals it.
            var automation = Click(driver.MainWindow, TreeItem(driver, AutomationGroup));
            Thread.Sleep(500);
            var automationChosen = TakeMain(story, driver, "the Automation folder chosen");
            var entryRow = Click(driver.MainWindow, EntryRow(driver, BuildAgentTitle));
            Thread.Sleep(400);
            var entryChosen = TakeMain(story, driver, "the entry chosen");
            var entryMenu = OpenMenu(story, driver, "Entry", "Edit Entry");
            var entry = driver.OpenEntryDialog();
            var entryOpened = TakeOpened(story, driver, entry, "the entry dialog as it opens");
            var reveal = Click(entry, Uia.ById(entry, RevealPassword));
            Thread.Sleep(500);
            var revealed = Take(story, driver, entry, "the new password shown", null, null, Strip(entry));
            var cancel = Click(entry, Button(entry, "Cancel"));
            Thread.Sleep(700);

            story.Step(Side.Operator, "In KeePass: choose the entry, then Entry, Edit Entry, and show its password");
            story.Show(vaultChosen);
            story.MoveTo(automation);
            story.Click(automationChosen);
            story.Hold(60);
            story.MoveTo(entryRow);
            story.Click(entryChosen);
            story.Hold(60);
            Choose(story, entryMenu, entryChosen, entryOpened);
            story.Hold(60);
            story.MoveTo(reveal);
            story.Click(entryChosen, revealed);
            story.Hold(220);
            story.MoveTo(cancel);
            story.Click(entryChosen);
            story.Hold(80);

            // What the main window shows from here on, under the dialogs that follow.
            searched = entryChosen;

            // The revocation, from the row's menu, landing on OK.
            settingsMenu = OpenMenu(story, driver, "File", "Database Settings");
            settings = driver.OpenDatabaseSettings();
            general = TakeOpened(story, driver, settings, "the settings as they open");
            gateTab = Click(settings, Tab(settings, GateTab.Text));
            authorityPage = Take(story, driver, settings, "the KPG tab", null);
            clientsTab = Click(settings, Tab(settings, GateTab.ClientsPageText));
            Thread.Sleep(500);
            var admitted = Take(story, driver, settings, "the clients page with the client", null, ClientName);

            var row = KeePassDriver.ClientCell(settings, ClientName);
            Assert.That(row, Is.Not.Null, "the clients page does not list " + ClientName + ". " + driver.Diagnostics);
            var rowArea = KeePassDriver.Bounds(row);
            var menuOpen = Take(
                story, driver, settings, "the client's menu", new ClientMenuOpen(driver, settings, ClientName), ClientName);
            var menu = driver.WaitForWindowById(ClientList.MenuName, TimeSpan.FromSeconds(5));
            Assert.That(menu, Is.Not.Null, "the menu closed after the picture. " + driver.Diagnostics);
            var revoke = Uia.Descendant(menu, ControlType.MenuItem, name => name == "Revoke");
            Assert.That(revoke, Is.Not.Null, "the menu offers no Revoke: " + Uia.Inventory(menu));
            var revokeArea = KeePassDriver.Bounds(revoke);
            Mouse.Click(revoke);
            Thread.Sleep(700);

            var revoked = Take(story, driver, settings, "the clients page once the row is gone", null);
            ok = Button(settings, "OK");
            okArea = KeePassDriver.Bounds(ok);
            Press(settings, ok);
            Thread.Sleep(900);

            story.Step(Side.Operator, "To revoke: File, Database Settings, KPG, Clients, and Revoke on the client's row");
            Choose(story, settingsMenu, searched, general);
            story.MoveTo(gateTab);
            story.Click(searched, authorityPage);
            story.MoveTo(clientsTab);
            story.Click(searched, admitted);
            story.Hold(60);
            story.MoveTo(rowArea);
            story.RightClick(searched, menuOpen);
            story.Hold(80);
            story.MoveTo(revokeArea);
            story.Click(searched, revoked);

            story.Step(Side.Operator, "The row goes at once, and the revocation lands when OK accepts the dialog");
            story.Hold(160);
            story.MoveTo(okArea);
            story.Click(searched);
            story.Hold(100);

            var turnedAway = Command(story, "pykeepassgate status", environment,
                "The database refuses the certificate from then on, so the client's next call is refused");
            Assert.That(turnedAway.ExitCode, Is.EqualTo(1), turnedAway.ToString());
            Assert.That(turnedAway.Errors, Does.Contain("NotAdmitted: "), turnedAway.ToString());
            story.Hold(300);

            driver.Save();
            Assert.That(driver.ShutDown(), Is.True, "KeePass did not exit cleanly after the moving picture");
        }

        /// <summary>
        /// The pairing, for real: the command line waits for the operator's answer, so it runs as
        /// its own process while this thread waits for the dialog, takes its picture and presses
        /// Approve with the pointer.
        /// </summary>
        private Run Pair(
            KeePassDriver driver,
            Storyboard story,
            string command,
            string token,
            IDictionary<string, string> environment,
            out Storyboard.Shot approval,
            out Rectangle approve)
        {
            using (var pairing = ReferenceClient.Start(Quietly(command), environment))
            {
                var output = pairing.StandardOutput.ReadToEndAsync();
                var errors = pairing.StandardError.ReadToEndAsync();

                pairing.StandardInput.WriteLine(token);
                pairing.StandardInput.Close();

                try
                {
                    var dialog = driver.WaitForWindow("asking to pair", DialogTimeout);
                    if (dialog == null)
                    {
                        var gone = ReferenceClient.Finish(pairing, output, errors, TimeSpan.FromSeconds(5));
                        Assert.Fail("The approval dialog never appeared. The client said: " + gone + " " +
                                    driver.Diagnostics);
                    }

                    // Five seconds in, as the still of this dialog is.
                    var second = (int)new KeePassGate.Pairing.PairingOptions().ApprovalTimeout.TotalSeconds - 5;
                    approval = Take(story, driver, dialog, "the approval dialog", new Countdown(dialog, second));

                    var button = Button(dialog, GateTab.ApproveButtonText);
                    approve = KeePassDriver.Bounds(button);
                    Press(dialog, button);

                    return ReferenceClient.Finish(pairing, output, errors, CommandTimeout);
                }
                finally
                {
                    ReferenceClient.End(pairing);
                }
            }
        }

        /// <summary>One command typed, run and printed, as a step of its own on the client's side.</summary>
        private static Run Command(
            Storyboard story, string command, IDictionary<string, string> environment, string caption)
        {
            story.Step(Side.Client, caption);
            story.Hold(60);
            story.Type(command);

            var run = ReferenceClient.Execute(Quietly(command), CommandTimeout, null, environment);
            story.Print(run);
            story.Hold(220);
            return run;
        }

        /// <summary>
        /// The entry's new password set from the terminal: the command typed, the password pasted at
        /// the prompt it asks at, and what it printed.
        /// </summary>
        /// <remarks>
        /// The real run hands the password over a pipe, where the command shows no prompt, so the
        /// prompt, the stars and the two keys are drawn, as the pairing's are: a star for each
        /// character pasted, as the command's own prompt shows one at a terminal.
        /// </remarks>
        private static Run Update(
            Storyboard story, string command, IDictionary<string, string> environment, string caption)
        {
            story.Step(Side.Client, caption);
            story.Hold(60);
            story.Type(command);
            story.Ask("New password: ");
            story.Hold(40);
            story.Echo(new string('*', ThrowawayKeePass.RotatedPassword.Length));
            story.Keys("Ctrl+V");
            story.Keys("Enter");
            story.Answered();

            var run = ReferenceClient.Execute(
                Quietly(command), CommandTimeout, ThrowawayKeePass.RotatedPassword + "\n", environment);
            story.Print(run);
            story.Hold(220);
            return run;
        }

        /// <summary>
        /// A menu on the bar, opened as far as one item, photographed over the main window, and
        /// closed again: the picture and where the menu's name and the item are on the screen.
        /// </summary>
        /// <remarks>
        /// The menu has to fall inside the main window for one picture to hold both, and KeePass's
        /// do, at the size its main window opens at here. Asserted rather than assumed, because a
        /// menu cut off at the window's edge would be a picture of half a menu.
        /// </remarks>
        private static Menu OpenMenu(Storyboard story, KeePassDriver driver, string menu, string item)
        {
            var open = new MenuOpen(driver, menu, item);
            var picture = Take(story, driver, driver.MainWindow, "the " + menu + " menu", open, null, EntryList(driver));
            driver.CollapseMenu();

            Assert.That(picture.Area.Contains(open.Places[1]), Is.True,
                "the " + menu + " menu falls outside the main window, so its picture would cut it off");
            return new Menu(picture, open.Places[0], open.Places[1]);
        }

        /// <summary>The pointer to a menu on the bar, a click, to the item, and a click that leaves `after`.</summary>
        private static void Choose(Storyboard story, Menu menu, Storyboard.Shot main, Storyboard.Shot after)
        {
            story.MoveTo(menu.Bar);
            story.Click(menu.Open);
            story.Hold(40);
            story.MoveTo(menu.Item);
            story.Click(main, after);
        }

        /// <summary>
        /// A search for nothing, which KeePass answers with every entry in the database, and the
        /// main window photographed once it has.
        /// </summary>
        private static Storyboard.Shot Search(Storyboard story, KeePassDriver driver, Rectangle box)
        {
            Keyboard.BringToFront(driver.MainWindow);
            Mouse.Click(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            Keyboard.SendEnter();
            Thread.Sleep(700);
            return TakeMain(story, driver, "every entry");
        }

        /// <summary>
        /// The main window, with the focus on the entry list, where no caret blinks.
        /// </summary>
        private static Storyboard.Shot TakeMain(Storyboard story, KeePassDriver driver, string name)
        {
            return Take(story, driver, driver.MainWindow, name, null, null, EntryList(driver));
        }

        /// <summary>
        /// A dialog as it comes up, with the focus on its tab strip, where no caret blinks.
        /// </summary>
        /// <remarks>
        /// KeePass opens the settings and the group dialog with the focus in their name box, whose
        /// caret blinks, and two runs caught it in two phases: measured, that was the whole of the
        /// difference between them. The tab strip is where the pointer goes next.
        /// </remarks>
        private static Storyboard.Shot TakeOpened(
            Storyboard story, KeePassDriver driver, AutomationElement dialog, string name)
        {
            var strip = Uia.WaitFor(
                "the tab strip of " + name,
                () => Uia.Descendant(dialog, ControlType.Tab, each => true));
            return Take(story, driver, dialog, name, null, null, strip);
        }

        /// <summary>KeePass's quick search box on the toolbar.</summary>
        private static Rectangle SearchBox(KeePassDriver driver)
        {
            var box = Uia.WaitFor(
                "the search box",
                () => Uia.Descendant(driver.MainWindow, ControlType.ComboBox, name => true));
            return KeePassDriver.Bounds(box);
        }

        /// <summary>KeePass's entry list, by the name its designer gave it.</summary>
        private static AutomationElement EntryList(KeePassDriver driver)
        {
            var list = Uia.ById(driver.MainWindow, "m_lvEntries");
            Assert.That(list, Is.Not.Null, "the main window has no entry list: " + Uia.Inventory(driver.MainWindow));
            return list;
        }

        /// <summary>A group in KeePass's tree, by its name.</summary>
        private static AutomationElement TreeItem(KeePassDriver driver, string name)
        {
            return Uia.WaitFor(
                "the group " + name,
                () => Uia.Descendant(driver.MainWindow, ControlType.TreeItem, each => each == name));
        }

        /// <summary>
        /// A row of KeePass's entry list, by its title, under whichever control type the list
        /// reports its rows as.
        /// </summary>
        private static AutomationElement EntryRow(KeePassDriver driver, string title)
        {
            return Uia.WaitFor(
                "the entry " + title,
                () => Uia.Descendant(EntryList(driver), ControlType.ListItem, each => each == title)
                      ?? Uia.Descendant(EntryList(driver), ControlType.DataItem, each => each == title));
        }

        /// <summary>A dialog's tab strip, where the focus rests while the picture is taken.</summary>
        private static AutomationElement Strip(AutomationElement dialog)
        {
            return Uia.WaitFor("the tab strip", () => Uia.Descendant(dialog, ControlType.Tab, each => true));
        }

        /// <summary>A tab of a dialog, or a page of the KPG tab, by its caption.</summary>
        private static AutomationElement Tab(AutomationElement dialog, string caption)
        {
            return Uia.WaitFor(
                "the " + caption + " tab",
                () => Uia.Descendant(dialog, ControlType.TabItem, name => name == caption));
        }

        /// <summary>The cell on the grant editor's table whose value is this text.</summary>
        private static Rectangle GrantCell(AutomationElement dialog, string text)
        {
            var cell = Uia.WaitForOrNull(
                () =>
                {
                    var grid = Uia.ById(dialog, GrantTab.GridControlName);
                    if (grid == null)
                    {
                        return null;
                    }

                    foreach (AutomationElement element in grid.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                    {
                        object pattern;
                        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)
                            && string.Equals(((ValuePattern)pattern).Current.Value, text, StringComparison.Ordinal))
                        {
                            return element;
                        }
                    }

                    return null;
                },
                TimeSpan.FromSeconds(10));
            Assert.That(cell, Is.Not.Null, "the grant editor has no cell saying '" + text + "': " + Uia.Inventory(dialog));
            return KeePassDriver.Bounds(cell);
        }

        /// <summary>
        /// The box at the start of a row on the rule picker, which is where a person ticks it and
        /// so where the press goes and the pointer is drawn: a square as tall as the row.
        /// </summary>
        private static Rectangle CheckBox(Rectangle row)
        {
            return new Rectangle(row.X, row.Y, row.Height, row.Height);
        }

        /// <summary>An item on one of the grant editor's pickers while it is open, by its text.</summary>
        private static Rectangle PickerItem(KeePassDriver driver, string pickerName, string text)
        {
            var item = Uia.WaitForOrNull(
                () =>
                {
                    var window = driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.Zero);
                    var list = window == null ? null : Uia.ById(window, pickerName);
                    return list == null ? null : Uia.Descendant(list, ControlType.ListItem, name => name == text);
                },
                TimeSpan.FromSeconds(10));
            Assert.That(item, Is.Not.Null, "the picker offers no '" + text + "'. " + driver.Diagnostics);
            return KeePassDriver.Bounds(item);
        }

        /// <summary>A press with the pointer on a control, answering with where it was.</summary>
        private static Rectangle Click(AutomationElement window, AutomationElement control)
        {
            Assert.That(control, Is.Not.Null, "nothing to press on " + Uia.Name(window));
            var area = KeePassDriver.Bounds(control);
            Press(window, control);
            return area;
        }

        /// <summary>
        /// The client's own `LOCALAPPDATA`, inside this session, which is where it stores what it
        /// paired with when told nothing, so that the command it types is the documentation's own.
        /// </summary>
        /// <remarks>
        /// `uv` keeps its cache under the same variable, so the cache is named outright, as it was
        /// before the variable moved: a cache that moved with it would be empty, and this machine
        /// fetches nothing.
        /// </remarks>
        private static Dictionary<string, string> ClientEnvironment(string home)
        {
            var cache = Environment.GetEnvironmentVariable("UV_CACHE_DIR");
            if (string.IsNullOrEmpty(cache))
            {
                cache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uv", "cache");
            }

            return new Dictionary<string, string>
            {
                { "LOCALAPPDATA", home },
                { "UV_CACHE_DIR", cache }
            };
        }

        /// <summary>
        /// A command as `uv` is asked to run it: with nothing of `uv`'s own on either stream, since
        /// both are in the picture, and a warning about somebody's shell is not part of the story.
        /// </summary>
        private static string Quietly(string command)
        {
            return "--quiet " + command;
        }

        /// <summary>
        /// A picture of a window for the story, with the moments on one client's row covered when a
        /// client is named, and leaving alone the row's menu if it is open over them.
        /// </summary>
        /// <remarks>
        /// **And the main window's entry view once a client has changed an entry**, see
        /// <see cref="_entryChangedNow"/>, on every picture of that window from then on, a menu
        /// photographed over it included.
        /// </remarks>
        private static Storyboard.Shot Take(
            Storyboard story,
            KeePassDriver driver,
            AutomationElement window,
            string name,
            ISteadyMoment moment,
            string client = null,
            AutomationElement parkFocusOn = null,
            Rectangle reach = default(Rectangle))
        {
            Rectangle area;
            var picture = Screenshot.Take(window, name, parkFocusOn, moment, reach, out area);
            if (client != null)
            {
                var menu = driver.WaitForWindowById(ClientList.MenuName, TimeSpan.Zero);
                Redacted.Moments(picture, area, window, client, menu == null ? Rectangle.Empty : KeePassDriver.Bounds(menu));
            }

            if (_entryChangedNow && Automation.Compare(window, driver.MainWindow))
            {
                Redacted.Over(picture, area, window, EntryView);
            }

            return story.Keep(picture, area);
        }

        /// <summary>A button by its caption, whichever letter it underlines.</summary>
        private static AutomationElement Button(AutomationElement window, string caption)
        {
            var button = Uia.WaitForOrNull(
                () => Uia.Descendant(window, ControlType.Button, name => (name ?? string.Empty).Replace("&", string.Empty) == caption),
                TimeSpan.FromSeconds(10));
            Assert.That(button, Is.Not.Null,
                "no button called '" + caption + "': " + Uia.Describe(window, ControlType.Button));
            return button;
        }

        /// <summary>
        /// A press with the pointer, in the middle of the control, which is where the picture draws
        /// the click.
        /// </summary>
        private static void Press(AutomationElement window, AutomationElement control)
        {
            Keyboard.BringToFront(window);
            Mouse.Click(control);
            Thread.Sleep(500);
        }

        private static Point Middle(Rectangle area)
        {
            return new Point(area.X + (area.Width / 2), area.Y + (area.Height / 2));
        }

        /// <summary>What a command printed after a label at the start of a line, or null.</summary>
        private static string Printed(Run run, string label)
        {
            var line = run.Output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(each => each.StartsWith(label, StringComparison.Ordinal));

            return line == null ? null : line.Substring(label.Length).Trim();
        }

        /// <summary>
        /// Runs the command line once before anything starts, which settles the environment where a
        /// slow first sync cannot eat into the time an approval dialog is waited for.
        /// </summary>
        private static void SettleTheClient()
        {
            var settled = ReferenceClient.Execute("pykeepassgate --help", SettleTimeout, null);
            Assert.That(settled.ExitCode, Is.Zero, "the client would not run: " + settled);
        }

        /// <summary>A menu photographed open, and where its name on the bar and the item were.</summary>
        private sealed class Menu
        {
            public Menu(Storyboard.Shot open, Rectangle bar, Rectangle item)
            {
                Open = open;
                Bar = bar;
                Item = item;
            }

            public Storyboard.Shot Open { get; }

            public Rectangle Bar { get; }

            public Rectangle Item { get; }
        }

        /// <summary>
        /// A menu of the main window opened at the last moment through the tree, for the reason
        /// <see cref="KeePassDriver.ExpandMenu"/> gives, and asked again after the shutter.
        /// </summary>
        private sealed class MenuOpen : ISteadyMoment
        {
            private readonly KeePassDriver _driver;
            private readonly string[] _path;

            public MenuOpen(KeePassDriver driver, params string[] path)
            {
                _driver = driver;
                _path = path;
            }

            /// <summary>Where the menu's name on the bar and each item on the way were.</summary>
            public Rectangle[] Places { get; private set; }

            public void WaitForIt()
            {
                Places = _driver.ExpandMenu(_path);
                Thread.Sleep(400);
            }

            public void ConfirmItHeld()
            {
                Assert.That(Places, Is.Not.Null, "the menu never opened");
            }
        }

        /// <summary>
        /// One of the grant editor's pickers, opened at the last moment by a press with the
        /// pointer, and asked again after the shutter.
        /// </summary>
        /// <remarks>
        /// Opened inside the wait for the shutter, for the reason `RefuseMenuOpen` gives: what the
        /// capture does first, bringing the dialog forward, is what an open dropdown might not
        /// survive. The press is on the table's cell for the profile picker, and on the profile
        /// chosen for the rule picker, which the editor opens straight after a profile is chosen.
        /// </remarks>
        private sealed class PickerOpen : ISteadyMoment
        {
            private readonly KeePassDriver _driver;
            private readonly Rectangle _press;
            private readonly string _list;

            public PickerOpen(KeePassDriver driver, Rectangle press, string list)
            {
                _driver = driver;
                _press = press;
                _list = list;
            }

            /// <summary>
            /// Presses, and waits for the list to be open. With no press, only waits: the list was
            /// opened before the picture, and this checks it is still there.
            /// </summary>
            public void WaitForIt()
            {
                if (!_press.IsEmpty)
                {
                    Mouse.Click(_press.X + (_press.Width / 2), _press.Y + (_press.Height / 2));
                }

                Assert.That(Open(), Is.True, "the press opened no " + _list + ". " + _driver.Diagnostics);
                Thread.Sleep(400);
            }

            public void ConfirmItHeld()
            {
                Assert.That(Open(), Is.True, "the " + _list + " closed before the shutter did, so the picture is not of it");
            }

            private bool Open()
            {
                return Uia.WaitForOrNull(
                    () =>
                    {
                        var window = _driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.Zero);
                        return window == null ? null : Uia.ById(window, _list);
                    },
                    TimeSpan.FromSeconds(5)) != null;
            }
        }

        /// <summary>
        /// The rule picker's boxes ticked with the pointer at the last moment, the picker still open
        /// after the shutter.
        /// </summary>
        private sealed class Ticked : ISteadyMoment
        {
            private readonly KeePassDriver _driver;
            private readonly Rectangle[] _boxes;

            public Ticked(KeePassDriver driver, params Rectangle[] boxes)
            {
                _driver = driver;
                _boxes = boxes;
            }

            public void WaitForIt()
            {
                foreach (var box in _boxes)
                {
                    Mouse.Click(box.X + (box.Width / 2), box.Y + (box.Height / 2));
                    Thread.Sleep(300);
                }

                Thread.Sleep(300);
            }

            public void ConfirmItHeld()
            {
                Assert.That(
                    _driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.Zero),
                    Is.Not.Null,
                    "the rule picker closed before the shutter did, so the picture is not of it");
            }
        }
    }
}
