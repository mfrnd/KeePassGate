using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The KPG tab: what this database's authority is, what it can grant, who has been let
    /// in through it, and what an operator can do to any of them.
    /// </summary>
    /// <remarks>
    /// **Three pages, because there are three subjects.** The authority is what this database can
    /// issue; the profiles are the names it can issue for; the clients are who it is issued to.
    /// They are changed on different occasions and read on different occasions, and one page
    /// carrying all of them meant every line competing for the same 449 by 314.
    ///
    /// Two things are said on screen rather than assumed, under both pages because both are true
    /// of both. That nothing here reaches the database until this dialog is accepted, because a tab
    /// inside a dialog with OK and Cancel on it has to mean the same thing they do. And what
    /// recreating costs, in the number of clients that lose their admission, because that is the
    /// number the operator cannot work out for themselves.
    ///
    /// **Nothing in here throws**, because it runs on the UI thread of a password manager and an
    /// exception leaving a click handler reaches the message loop, where nothing on this page can
    /// see it. That is a claim about every path, including the ones that only draw: filling a grid
    /// and creating a window handle both fail in ways that have nothing to do with certificates, so
    /// the filters here name <see cref="Expected.OfUserInterface"/> as well.
    /// </remarks>
    public sealed class AuthorityPanel : UserControl
    {
        /// <summary>
        /// What OK and Cancel mean for everything on this tab, in one line.
        /// </summary>
        /// <remarks>
        /// Kept to one line on purpose, and that is a measurement rather than a preference: the tab
        /// is 449 by 314, nothing on it scrolls, and a sentence that wraps costs a whole line the
        /// pages underneath do not have. It says the two things that are true and useful: which
        /// button makes any of this real, and that the file is written when the database is
        /// saved. `GateTabFitsTests` is what fails when it grows back.
        ///
        /// **This sentence used to say the opposite**, and the reversal is the point. The tab wrote
        /// at once, which was safe, because KeePass's settings dialog copies none of this plugin's
        /// custom data and so never undid any of it. It was still wrong: a dialog with OK and
        /// Cancel on it says nothing has happened yet, and a tab inside one that has already
        /// happened is a tab lying about the buttons around it.
        /// </remarks>
        public const string ImmediateNotice =
            "Changes apply on OK; the database must then be saved.";

        /// <summary>
        /// What the invitation box, and the copy button as tall as it, drop by to sit centered on
        /// the Invite button beside them.
        /// </summary>
        /// <remarks>
        /// At the default font a button is 24 pixels tall and a one line box 20, so two is half the
        /// difference. It lines two controls up rather than putting air between them, which is why
        /// it is not a `Spacing`.
        /// </remarks>
        private const int BoxNudge = 2;

        private readonly IGateDatabase _database;
        private readonly IOperatorPrompt _prompt;
        private readonly IGateLog _log;
        private readonly IInvitations _invitations;

        private readonly IAdmissionNotice _admissions;
        private readonly AuthorityService _service;
        private readonly Font _valueFont;

        /// <summary>The invitation box's own font, built again whenever the panel's changes.</summary>
        private Font _tokenFont;

        private readonly TabControl _tabs;
        private readonly FlowLayoutPanel _inviting;

        /// <summary>The labels that wrap, which are the ones a narrow tab has to be told about.</summary>
        private readonly List<Label> _wrapping = new List<Label>();

        /// <summary>
        /// What the anchor and the usage answered for each record's certificate, by admission key,
        /// under the authority named by <see cref="_judgedBy"/>.
        /// </summary>
        /// <remarks>
        /// Kept, because asking builds a chain per record, which was most of a redraw with a few
        /// hundred clients, and because nothing changes the answer but the two certificates: the
        /// admission key is the digest of the certificate its record holds, which loading the
        /// record checks, and the authority is named by its own digest. Emptied whenever that
        /// digest changes, and never given an answer from an engine that could not evaluate the
        /// chain, which is a failure to be asked again rather than a fact. The dates are asked on
        /// every redraw, since a clock does change them.
        /// </remarks>
        private readonly Dictionary<string, ClientReach> _judged =
            new Dictionary<string, ClientReach>(StringComparer.Ordinal);

        /// <summary>The digest of the authority <see cref="_judged"/> answers for, or null for none.</summary>
        private string _judgedBy;

        /// <summary>
        /// The digest of the authority the invitation band was last drawn against, or null for
        /// none, kept so that the band can follow the clock without reading the database again.
        /// </summary>
        private string _offered;

        /// <summary>
        /// The renewals refused in this session, as this dialog's OK would leave them, or null for
        /// a tab that shows none.
        /// </summary>
        private readonly PendingSessionRefusals _sessionRefusals;

        /// <summary>Wakes the invitation band when its line next changes, while a code is out.</summary>
        private readonly Timer _ticking = new Timer();

        private readonly Label _status;
        private readonly TableLayoutPanel _facts;
        private readonly Label _detail;
        private readonly Label _result;
        private readonly Button _create;
        private readonly Button _recreate;
        private readonly Button _delete;
        private readonly Button _choose;
        private readonly Button _invite;
        private readonly TextBox _token;
        private readonly Button _copy;
        private readonly ToolTip _tips;
        private readonly Label _invitation;
        private readonly ClientList _clients;
        private readonly Label _notice;
        private readonly ProfileList _profiles;

        /// <summary>
        /// Where a copied invitation goes. Settable for the same reason the button's work is a
        /// public method: a test that took the developer's clipboard away every time it ran would
        /// be a test nobody runs.
        /// </summary>
        internal IClipboard Clipboard { get; set; }

        public AuthorityPanel(IGateDatabase database, IOperatorPrompt prompt, IGateLog log)
            : this(database, prompt, log, null, null)
        {
        }

        public AuthorityPanel(
            IGateDatabase database, IOperatorPrompt prompt, IGateLog log, IInvitations invitations)
            : this(database, prompt, log, invitations, null)
        {
        }

        /// <summary>
        /// The tab, for one database.
        /// </summary>
        /// <remarks>
        /// It takes what it is about and nothing else. The plugin's version and whether the gate is
        /// listening used to arrive here too, and they were answers about the plugin rather than
        /// about this database: they are the same whichever one is open, and are wanted when none
        /// is. They live on the window the Tools menu opens, and this is what stopped needing them.
        /// </remarks>
        public AuthorityPanel(
            IGateDatabase database,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations,
            IProfiles profiles)
            : this(database, prompt, log, invitations, profiles, null)
        {
        }

        /// <summary>
        /// The same, and listening for a record landing while this tab is open.
        /// </summary>
        /// <remarks>
        /// Pairing is invited from this very tab and writes its record from a connection thread,
        /// so the one moment the clients page is most likely to be wrong is the moment somebody is
        /// watching it. Without this the list changes when a page comes forward and at no other
        /// time, and an operator who paired while looking at it would have to switch away and
        /// back.
        /// </remarks>
        public AuthorityPanel(
            IGateDatabase database,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations,
            IProfiles profiles,
            IAdmissionNotice admissions)
            : this(database, prompt, log, invitations, profiles, admissions, null)
        {
        }

        /// <summary>
        /// The same, and showing the renewals refused in this session beside the clients they are
        /// about.
        /// </summary>
        /// <remarks>
        /// The database's standing answers are read from the database; this session's are the
        /// plugin's memory, so they arrive here, through what the dialog holds back until OK. Null
        /// for a tab that shows none, which is what a tab built without the plugin around it is.
        /// </remarks>
        public AuthorityPanel(
            IGateDatabase database,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations,
            IProfiles profiles,
            IAdmissionNotice admissions,
            PendingSessionRefusals sessionRefusals)
        {
            _database = database;
            Clipboard = SystemClipboard.Instance;
            AskWhichToKeep = ShowChooser;
            _prompt = prompt ?? new MessageBoxPrompt();
            _log = log ?? NullGateLog.Instance;
            _invitations = invitations;
            _admissions = admissions;
            _sessionRefusals = sessionRefusals;

            _service = database == null || !database.IsOpen
                ? null
                : new AuthorityService(database.PluginCustomData, _log);

            Name = GateTab.PanelControlName;
            AccessibleName = GateTab.PanelAccessibleLabel;
            Dock = DockStyle.Fill;
            Padding = new Padding(Spacing.Gap);

            // Neither axis scrolls. KeePass gives a settings tab a fixed area and the content has
            // to fit it: a scroll bar here would hide the half of the tab that says what the
            // buttons cost. `GateTabFitsTests` measures this against the real dialog, so content
            // that outgrows the tab fails a test rather than appearing on somebody's screen.
            AutoScroll = false;

            // The prototype is disposed, because `SystemFonts.DefaultFont` hands back a new font
            // every time it is read and `new Font(prototype, style)` copies rather than adopts.
            // Without the `using`, every settings dialog opened leaves one behind.
            using (var prototype = SystemFonts.DefaultFont)
            {
                _valueFont = new Font(prototype, FontStyle.Bold);
            }

            _tabs = new TabControl
            {
                Name = GateTab.SubTabsName,
                Dock = DockStyle.Fill
            };

            _tabs.TabPages.Add(NewPage(GateTab.AuthorityPageName, GateTab.AuthorityPageText));
            _tabs.TabPages.Add(NewPage(GateTab.ProfilesPageName, GateTab.ProfilesPageText));
            _tabs.TabPages.Add(NewPage(GateTab.ClientsPageName, GateTab.ClientsPageText));

            // Read and fitted again whenever a page comes forward, which are two different repairs
            // that both hang off the same moment.
            //
            // Fitting, because a page that was behind another was never given the width it will
            // have, so anything measured against it while it was hidden stays measured that way:
            // labels wrap after a few words and the invitation box shrinks until a code cannot be
            // read out of it.
            //
            // Reading, because the database changes while this dialog is open. Pairing is invited
            // from this very page and writes its record from somewhere else entirely, so a page
            // that only ever showed what was true when the tab was built would answer "no client
            // has been admitted" about the client the operator had just approved. Bringing a page
            // to the front is a request to see what is there.
            _tabs.SelectedIndexChanged += delegate
            {
                Describe();
                PerformLayout();
            };

            var authority = NewColumn("KpgAuthorityColumn", DockStyle.Fill);

            // Air above as well as below, so it reads as the opening sentence of the page
            // rather than as a caption on whatever is over it or a label on the table under
            // it. A first line pressed against the top edge reads as one of those two, and
            // which one is not something the page gets to choose.
            _status = NewTextLabel(
                GateTab.StatusLabelName, new Padding(0, Spacing.Gap, 0, Spacing.Gap));

            // A table rather than sentences, because these are scanned rather than read. Two
            // columns that size themselves: the names are known and short, and the values are what
            // somebody came to look at.
            _facts = new TableLayoutPanel
            {
                Name = GateTab.FactsTableName,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0, 0, 0, Spacing.Tight)
            };

            _facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _detail = NewTextLabel(GateTab.DetailLabelName, new Padding(0, 0, 0, Spacing.Gap));
            authority.Controls.Add(_status);
            authority.Controls.Add(_facts);
            authority.Controls.Add(_detail);

            var buttons = NewRow("KpgAuthorityButtonRow", new Padding(0, 0, 0, Spacing.Tight));
            _create = NewButton(GateTab.CreateButtonName, GateTab.CreateButtonText, OnCreate);
            _recreate = NewButton(GateTab.RecreateButtonName, GateTab.RecreateButtonText, OnRecreate);
            _delete = NewButton(GateTab.DeleteButtonName, GateTab.DeleteButtonText, OnDelete);

            // In Create's place rather than beside it, and shown only while there is a choice to
            // make: Create has nothing to do in that state, and a fourth button in a row the page
            // is barely wide enough for three of would not fit.
            _choose = NewButton(GateTab.ChooseButtonName, GateTab.ChooseButtonText, OnChoose);
            _choose.Visible = false;
            buttons.Controls.Add(_choose);
            buttons.Controls.Add(_create);
            buttons.Controls.Add(_recreate);
            buttons.Controls.Add(_delete);
            authority.Controls.Add(buttons);

            _tabs.TabPages[0].Controls.Add(authority);

            // The button, the token it produces and the copy button sit on one line, and the box
            // takes whatever width is left over, so nothing here has a size that a wider or
            // narrower tab breaks.
            var invitation = NewColumn("KpgInvitationColumn", DockStyle.Top);

            // A docked band is laid out edge to edge against what it is given, so its margin is
            // never consulted and the space above it has to be padding.
            invitation.Padding = new Padding(0, Spacing.Gap, 0, 0);

            // Tight above, because the lines over it are about the code this row hands out and
            // the three read as one thing. Nothing below: the list is what separates itself from
            // this band, with its own top padding.
            _inviting = NewRow("KpgInvitingRow", new Padding(0, Spacing.Tight, 0, 0));
            _invite = NewButton(GateTab.InviteButtonName, GateTab.InviteButtonText, OnInvite);
            _invite.Margin = new Padding(0, 0, Spacing.Gap, 0);

            // Monospaced, because what is in it is forty-three characters of an alphabet with no
            // narrow letters and no wide ones to average out: in a proportional font the same
            // code is a different width every time it is minted, so a box that held one would cut
            // the next. Fixed width makes the widest code the only code, which is a width the box
            // can be built for.
            _tokenFont = CodeFont(Font.Size);

            _token = new TextBox
            {
                Name = GateTab.InvitationBoxName,

                // The words, not the lookup name. `AccessibleName` is what a screen reader says
                // about the control, and announcing "KpgInvitationBox" tells nobody anything;
                // `InvitationBoxLabel` is the phrase written for exactly this and had no reader.
                AccessibleName = GateTab.InvitationBoxLabel,
                ReadOnly = true,
                Font = _tokenFont,
                Margin = new Padding(0, BoxNudge, Spacing.Tight, 0)
            };

            // Behind the box, where the thing it acts on is already in view. The operator can still
            // select the text and copy it by hand, which is what happens when the clipboard is
            // busy, so this saves a gesture rather than being the only way through.
            _copy = new CopyButton
            {
                Name = GateTab.CopyButtonName,
                AccessibleName = GateTab.CopyButtonLabel,
                Margin = new Padding(0, BoxNudge, 0, 0)
            };

            _copy.Click += OnCopy;

            _tips = new ToolTip();
            Tooltips.Set(_tips, _copy, GateTab.CopyButtonLabel);

            _ticking.Tick += OnTick;

            _inviting.Controls.Add(_invite);
            _inviting.Controls.Add(_token);
            _inviting.Controls.Add(_copy);

            // What is said comes before what is pressed. The column flows top down and renders in
            // the order things are added, so these two go in first: an operator reads what this
            // page is about and why the button is or is not alive, and then reaches the button.
            // Reversed, the reason a dead button is dead sat underneath it, which is past the
            // point where somebody has already tried to press it.
            //
            // Nothing under this one. The line below it qualifies it and the two are one band.
            _invitation = NewTextLabel(GateTab.InvitationLabelName, new Padding(0));
            invitation.Controls.Add(_invitation);

            // Under the invitation, because it qualifies what was just handed out. The column
            // flows top down, so what is added after it renders below it. Collapsed when there is
            // nothing to say, so the page is not permanently a line shorter for a state most
            // visits are never in.
            _notice = NewTextLabel(GateTab.InviteNoticeName, new Padding(0));
            _notice.ForeColor = Color.Firebrick;
            _notice.Visible = false;
            invitation.Controls.Add(_notice);

            // And the button last, under both lines that say what it is for.
            invitation.Controls.Add(_inviting);

            // Under the widgets that hand an invitation out, because the list is what an invitation
            // turns into. The list fills what the invitation band leaves, so it goes in first:
            // docking is applied from the end of the collection backwards.
            _clients = new ClientList(RevokeAndDescribe, AllowRenewalAndDescribe);
            _tabs.TabPages[2].Controls.Add(_clients);
            _tabs.TabPages[2].Controls.Add(invitation);

            // The names a rule can grant to, which is neither of the other two subjects: what this
            // database can issue, and who holds what it issued.
            _profiles = new ProfileList(profiles ?? new NoProfiles(), _prompt);
            _tabs.TabPages[1].Controls.Add(_profiles);

            // What the last press did, below all three pages rather than on one of them. Every page has
            // something to say here: the authority page has three buttons and the clients page has
            // an invitation, a copy and a revocation, and while this label lived on the authority
            // page an invitation that could not be made reported itself onto a page the operator
            // was not looking at.
            var footer = NewColumn("KpgAuthorityFooter", DockStyle.Bottom);
            _result = NewTextLabel(GateTab.ResultLabelName, new Padding(0, Spacing.Gap, 0, 0));

            // Nothing to say means no room taken. The band sizes itself to its content, so an
            // empty label still reserves a line and the pages above it are a line shorter than
            // they could be, for the whole time the operator has pressed nothing.
            _result.Visible = false;
            _result.TextChanged += delegate { _result.Visible = _result.Text.Length > 0; };

            footer.Controls.Add(_result);

            // **Under every page rather than on one of them**, for the reason the line above it is:
            // what it says is true of the whole tab. It used to sit at the bottom of the authority
            // page, where an operator revoking a client on the clients page never saw it and was
            // never told that the revocation was waiting for OK like everything else. It is also
            // the last thing above the dialog's own OK button, which is what it is about.
            footer.Controls.Add(
                NewTextLabel(GateTab.NoticeLabelName, new Padding(0, Spacing.Tight, 0, 0), ImmediateNotice));

            Controls.Add(_tabs);
            Controls.Add(footer);

            Describe();

            // **Last, once there is a whole panel to tell.** The notice lives as long as the
            // plugin, and a constructor that failed after subscribing left a panel nobody would
            // ever dispose listening to it: the next pairing then drew into controls that were
            // never built, with its record already on the disk.
            if (_admissions != null)
            {
                _admissions.Landed += OnAdmitted;
            }
        }

        /// <summary>
        /// Creates an authority, exactly as the button does. Public because a button press needs a
        /// visible control tree and a test of the real dialog does not have one.
        /// </summary>
        public void CreateAuthority()
        {
            Act(() => _service.Create());
        }

        /// <summary>Recreates it, asking first. Nothing happens if the answer is no.</summary>
        public void RecreateAuthority()
        {
            Act(() =>
            {
                if (!_prompt.Confirm(
                        "Recreate the authority",
                        "Recreating the authority replaces it and revokes every client at once." +
                        Environment.NewLine + Environment.NewLine +
                        Cost(CountRecords()) + Environment.NewLine + Environment.NewLine +
                        "Recreate it?"))
                {
                    return null;
                }

                return _service.Recreate();
            });
        }

        /// <summary>Deletes it, asking first. Nothing happens if the answer is no.</summary>
        public void DeleteAuthority()
        {
            Act(() =>
            {
                if (!_prompt.Confirm(
                        "Delete the authority",
                        "Deleting the authority stops this database being served at all." +
                        Environment.NewLine + Environment.NewLine +
                        Cost(CountRecords()) + Environment.NewLine + Environment.NewLine +
                        "Delete it?"))
                {
                    return null;
                }

                return _service.Delete();
            });
        }

        /// <summary>
        /// Asks which of several authorities to keep, in the dialog KeePass's main window raises
        /// for the same question, and keeps the answer. Nothing happens if none is kept.
        /// </summary>
        /// <remarks>
        /// **The choice waits for this dialog's OK**, like everything else on the tab, and that is
        /// a difference from the same dialog raised over the main window, whose choice is written
        /// at once. A button on a tab inside a dialog with OK and Cancel on it that acted
        /// immediately would be the one gesture here that Cancel does not undo, and the page reads
        /// its own buffered writes, so it shows the kept authority straight away all the same.
        /// </remarks>
        public void ChooseAuthority()
        {
            Act(() =>
            {
                AuthorityChoice choice;
                using (var reading = _service.Read())
                {
                    if (reading.Presence != AuthorityPresence.Undecided)
                    {
                        return null;
                    }

                    choice = AuthorityChoice.From(
                        reading,
                        new AdmissionStore(_database.PluginCustomData, _log).All(),
                        clients => JsonLinesAudit.LastSeen(JsonLinesAudit.DefaultPath(), clients),
                        _log);
                }

                var chosen = AskWhichToKeep(choice, _database.Name);
                return chosen == null ? null : _service.Keep(chosen);
            });
        }

        /// <summary>
        /// Shows the chooser and answers with the digest kept, or null. Settable for the reason
        /// <see cref="Clipboard"/> is: a test that put a modal dialog up would wait for somebody.
        /// </summary>
        internal Func<AuthorityChoice, string, string> AskWhichToKeep { get; set; }

        private string ShowChooser(AuthorityChoice choice, string databaseName)
        {
            using (var form = new AuthorityChooserForm(choice, databaseName))
            {
                form.ShowDialog(FindForm());
                return form.Chosen;
            }
        }

        /// <summary>
        /// Creates an invitation and shows the token.
        /// </summary>
        /// <remarks>
        /// Nothing is written to the database, so this is the one button on the tab that Cancel
        /// does not undo: the invitation lives in `InvitationBook`, which is plugin memory, and
        /// `AuthorityPanelFactory.Commit` never touches it. An invitation that outlives a canceled
        /// dialog is a token somebody can still pair with, until its own expiry or KeePass exits.
        /// </remarks>
        public void InviteClient()
        {
            if (_service == null || _invitations == null)
            {
                return;
            }

            // **Not while the authority itself is still waiting for OK.** Everything else on this
            // tab reads through the decorator holding the dialog's changes, and reads its own
            // writes, so an authority created a moment ago on this very page describes itself
            // perfectly here. The gate does not read through it: it reads the live database, and
            // the digest of an authority that has not been applied yet names nothing it can find.
            // The code would look right, count down, and resolve nowhere, and every attempt to use
            // it would be dropped without an answer.
            if (AuthorityIsWaiting)
            {
                Said(InvitationText.AuthorityWaiting);
                return;
            }

            // **Nor while the profile list is.** The button already refuses this state, and this
            // is what makes the two agree: `Describe` decided the button, and a profile typed on
            // the second page after the clients page was last drawn leaves the button alive over a
            // list that is about to change under whoever is approved next.
            if (ProfilesAreWaiting)
            {
                Said(InvitationText.ProfilesWaiting);
                return;
            }

            try
            {
                string databaseId;
                using (var reading = _service.Read())
                {
                    if (!reading.IsPresent)
                    {
                        Said(InvitationText.NoAuthority);
                        return;
                    }

                    databaseId = reading.Material.AuthorityDigest;
                }

                var invitation = _invitations.Invite(databaseId);
                if (invitation == null)
                {
                    // The book refuses only when it has been disposed, which is KeePass shutting
                    // down under an open dialog. It used to say there were too many invitations
                    // outstanding, which cannot happen: a new one replaces whatever that database
                    // had, so the count never reaches a ceiling and the sentence named a state
                    // nobody could ever be in or do anything about.
                    Said("The plugin is shutting down, so no code was made.");
                    return;
                }

                _token.Text = invitation.Text;
                Said(string.Empty);
            }
            catch (Exception error) when (Expected.OfCustomData(error)
                                          || Expected.OfCryptography(error)
                                          || Expected.OfUserInterface(error))
            {
                _log.Error("an invitation could not be created", error);

                // Cut by the band itself, like everything else it is given. Nothing here scrolls,
                // so an untrimmed message pushes the buttons off the tab.
                Said("That did not work: " + error.Message);
            }

            Describe();
        }

        /// <summary>
        /// Puts the invitation on screen onto the clipboard. Nothing happens if there is none.
        /// </summary>
        /// <remarks>
        /// The clipboard is readable by everything running as this user, which is worth being clear
        /// about and is not a change: `DESIGN.md` §125 concedes local code execution as the user
        /// throughout, and the token is already on screen in a box built to be copied out of. What
        /// this adds is one gesture, not one exposure.
        ///
        /// What the copy is marked with is a different question, and `SystemClipboard` answers it:
        /// clipboard history and the cloud clipboard keep a copy where nobody looks and long past
        /// the invitation's own minutes, so the copy asks to be left out of both.
        ///
        /// Nothing clears the clipboard afterwards. A plugin that wiped somebody's clipboard on a
        /// timer would be taking away whatever they copied next, and the invitation expires on its
        /// own in minutes and is spent by the first client to use it.
        ///
        /// Public because a button press needs a visible control tree, and a test of the real
        /// KeePass dialog does not have one.
        /// </remarks>
        public void CopyInvitation()
        {
            if (string.IsNullOrEmpty(_token.Text))
            {
                return;
            }

            // **Asked again at the press**, because the code in the box can have run out, or been
            // spent, since the band was last brought up to date, and a copy of it is a code the
            // gate drops without a word. The band is brought up to date instead, which empties the
            // box and says there is no code.
            var outstanding = OutstandingCodes(DateTimeOffset.UtcNow);
            if (!outstanding.Any)
            {
                ShowOutstanding(outstanding);
                Said(InvitationText.NotCopied);
                return;
            }

            try
            {
                Clipboard.Set(_token.Text);
                Said(InvitationText.Copied);
            }
            catch (Exception error) when (Expected.OfClipboard(error))
            {
                _log.Warn("the invitation could not be put on the clipboard: " + error.Message);
                Said(InvitationText.ClipboardBusy);
            }
        }

        /// <summary>Reads the database again and redraws. Safe to call at any time.</summary>
        public void Describe()
        {
            // **Read once for the whole redraw**: the authority, the records, and how far each
            // record gets with the gate. The page reads again every time it is asked to, because
            // the database changes while this dialog is open; what it stopped doing is reading the
            // same things several times over in one redraw. The authority was read three times and
            // the records twice, and a chain was built for every record, which with two hundred
            // clients was about 0.4 seconds on KeePass's own thread for every page switch, every
            // button and every pairing that landed.
            using (var reading = Read())
            {
                var admitted = AdmittedClients();
                var reaches = Reaches(admitted, reading, DateTimeOffset.UtcNow);

                DescribeAuthority(reading, admitted, reaches);
                DescribeClients(admitted, reaches);
            }
        }

        /// <summary>Whether the profiles page is the one showing.</summary>
        /// <remarks>
        /// False before the pages have a window, which is while the panel is being built: the page
        /// has just drawn itself then, and nothing is in front of anybody yet.
        /// </remarks>
        private bool ProfilesInFront
        {
            get
            {
                var page = _tabs.SelectedTab;
                return page != null && string.Equals(page.Name, GateTab.ProfilesPageName, StringComparison.Ordinal);
            }
        }

        /// <summary>The first page, the invitation band and the buttons, from one reading.</summary>
        private void DescribeAuthority(
            AuthorityReading reading,
            AdmissionListing admitted,
            IDictionary<string, ClientReach> reaches)
        {
            try
            {
                var description = DescribeNow(reading, admitted, reaches);
                _status.Text = description.Headline;

                Draw(description.Facts);
                _detail.Text = description.DetailText;
                _create.Enabled = description.CanCreate;
                _recreate.Enabled = description.CanRecreate;
                _delete.Enabled = description.CanDelete;
                _choose.Visible = description.CanChoose;
                _create.Visible = !description.CanChoose;

                // Inviting needs an authority that can issue, which is narrower than what
                // recreating and deleting need: `CanRecreate` is true of a damaged authority, and
                // a code handed out for one is a code whose redemption answers with a failure
                // about a state only the operator can do anything about.
                //
                // And an authority the gate can see. One that is still waiting for this dialog's
                // OK would hand out a code that cannot be spent, or worse, on a recreate, one that
                // is spent against the authority this dialog is about to replace: the client pairs,
                // is issued a certificate, and OK invalidates it. The notice beside the button says
                // what to do; the button being dead is what stops somebody doing it anyway.
                _invite.Enabled = _invitations != null && description.CanInvite
                    && !AuthorityIsWaiting && !ProfilesAreWaiting;

                // A code that is no longer outstanding is a code that has been spent, has run out,
                // or belonged to an authority that has since been deleted or replaced. In every one
                // of those the box stops showing it: leaving it there put a code on screen beside a
                // line saying there is none, and the one on screen was the one that no longer
                // works, which is exactly the string somebody would copy and hand over.
                //
                // **Asked of the invitations rather than of the sentence.** Comparing the line
                // against one of the things it can say missed the other two, and the case it missed
                // by name was a deleted authority, where the box kept a code for something that no
                // longer exists.
                _offered = reading != null && reading.IsPresent ? reading.Material.AuthorityDigest : null;
                ShowOutstanding(OutstandingCodes(DateTimeOffset.UtcNow));
            }
            catch (Exception error) when (Expected.OfCustomData(error)
                                          || Expected.OfCryptography(error)
                                          || Expected.OfUserInterface(error))
            {
                _log.Error("the authority could not be described", error);
                _status.Text = "The authority could not be read.";
                Draw(null);
                _detail.Text = Clipped.Message(error.Message);
                _create.Enabled = false;
                _recreate.Enabled = false;
                _delete.Enabled = false;
                _invite.Enabled = false;
                _copy.Enabled = !string.IsNullOrEmpty(_token.Text);
                _ticking.Stop();
            }
        }

        /// <summary>The clients page, the profiles page and the notice, from the same reading.</summary>
        private void DescribeClients(AdmissionListing admitted, IDictionary<string, ClientReach> reaches)
        {
            // Apart from the first page's catch, because a tab that failed to describe its
            // authority and went on showing the clients it listed a minute ago would be lying about
            // who can reach this database. Reading the records has its own refusal and answers
            // with nobody, and so does reading the profiles.
            //
            // Inside a catch of its own, though, because drawing is not reading a database: rows
            // and window handles fail for reasons of their own, and this runs on the click of every
            // button on the tab. An exception leaving here would leave a click handler and reach
            // KeePass's message loop, which closes the dialog and reports nothing.
            try
            {
                _clients.Fill(admitted, reaches, NamedProfiles(), StandingAnswers(admitted, DateTimeOffset.UtcNow));

                // The profiles page only while it is the page in front. It counts every rule in the
                // tree and every record, which on a large database is the slowest thing on the tab,
                // and like every page it is read again whenever it is brought forward. Drawn on
                // every redraw, it was counted for each button on the other two pages and twice as
                // the dialog was built, once by its own constructor and once by this panel's.
                if (ProfilesInFront)
                {
                    _profiles.Describe();
                }

                _notice.Text = InviteNotice();
                _notice.Visible = _notice.Text.Length > 0;
            }
            catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfCustomData(error))
            {
                _log.Error("the tab could not be drawn", error);
            }
        }

        /// <summary>
        /// Why a code handed out now could not be redeemed, or nothing when it could.
        /// </summary>
        /// <remarks>
        /// **One band, whichever reason applies**, because they answer one question and the page
        /// is 449 by 314. Two of them share it: an authority waiting for this dialog's OK, and a
        /// profile list waiting for the same. Both are said, because a dead button with no
        /// sentence anywhere on the tab about it leaves the operator nothing to act on.
        ///
        /// The authority comes first, because it is the one that stops a code resolving at all.
        /// </remarks>
        private string InviteNotice()
        {
            if (AuthorityIsWaiting)
            {
                return GateTab.PendingAuthorityNotice;
            }

            return ProfilesAreWaiting ? GateTab.PendingProfilesNotice : string.Empty;
        }

        /// <summary>Whether a profile edit is buffered and the list is about to change under a client.</summary>
        /// <remarks>
        /// `KPG.Profiles` is the one key this tab and pairing both write, and they write it
        /// differently: the tab holds its version until OK, pairing writes live and saves.
        /// `PendingStore` reconciles the two, so a name an approval added is no longer thrown away
        /// by OK. What is left is the direction no merge can decide. An operator who is part way
        /// through taking a profile out, and approves a client for it in the middle of that,
        /// presses OK and removes the name from the list: the client is admitted to a profile that
        /// grants nothing anywhere.
        ///
        /// Separate from `AuthorityIsWaiting` rather than folded into it, because that one also
        /// decides whether the notice about invalidated invitations is shown, and a buffered
        /// profile name invalidates nothing.
        /// </remarks>
        public bool ProfilesAreWaiting
        {
            get
            {
                var pending = _database as IPendingChanges;
                return pending != null && pending.WaitingOn(ProfileNames.CustomDataKey);
            }
        }

        /// <summary>
        /// Whether the authority on screen is one the gate cannot see yet.
        /// </summary>
        /// <remarks>
        /// The clients page is the one place on this tab where waiting costs something, and it is
        /// not obvious from where the operator is standing: they created an authority, the first
        /// page says there is one, and a client pairing against the gate is answered out of the
        /// database KeePass is holding, which has not been told. Recreating counts for the same
        /// reason with a worse ending, since the certificate a client would be issued is one this
        /// dialog's OK is about to invalidate.
        /// </remarks>
        public bool AuthorityIsWaiting
        {
            get
            {
                var pending = _database as IPendingChanges;
                if (pending == null)
                {
                    return false;
                }

                return pending.WaitingOn(AuthorityKeys.IsAuthorityState);
            }
        }

        /// <summary>
        /// Puts what is outstanding on the clients page: the line, the caption, the box and the
        /// copy button, and when the line next has to change.
        /// </summary>
        private void ShowOutstanding(Outstanding outstanding)
        {
            _invitation.Text = outstanding.Text;

            // Offering to renew a code only while there is one to renew: the first press
            // renews nothing, and the line just set says so.
            var caption = outstanding.Any ? GateTab.RenewInviteButtonText : GateTab.InviteButtonText;
            if (!string.Equals(_invite.Text, caption, StringComparison.Ordinal))
            {
                _invite.Text = caption;

                // The box beside it takes whatever width the button leaves, and that is worked
                // out in this panel's own layout, which a caption changing inside a row further
                // down is not guaranteed to reach.
                PerformLayout();
            }

            // A code that is no longer outstanding is a code that has been spent, has run out,
            // or belonged to an authority that has since been deleted or replaced. In every one
            // of those the box stops showing it: leaving it there put a code on screen beside a
            // line saying there is none, and the one on screen was the one that no longer
            // works, which is exactly the string somebody would copy and hand over.
            //
            // **Asked of the invitations rather than of the sentence.** Comparing the line
            // against one of the things it can say missed the other two, and the case it missed
            // by name was a deleted authority, where the box kept a code for something that no
            // longer exists.
            if (!outstanding.Any)
            {
                _token.Text = string.Empty;
            }

            // Last, and after the box has been settled. Copying needs something to copy, which
            // is a different question from whether a new invitation can be made, and reading it
            // before the line above left the button enabled over an empty box: pressing it then
            // did nothing at all, said nothing, and looked exactly like a clipboard failure.
            _copy.Enabled = !string.IsNullOrEmpty(_token.Text);

            Schedule(outstanding);
        }

        /// <summary>
        /// Wakes the clients page when its line next changes, for as long as a code is out.
        /// </summary>
        /// <remarks>
        /// **The line counts down and the box empties on the clock, not only on a redraw.** It
        /// used to be worked out when the page was drawn and at no other time, so an operator who
        /// handed out a code and left the page in front was told it was good for ten more minutes
        /// at minute eleven, with the code in the box and the copy button alive: the gate had
        /// swept it by then and dropped every connection that used it, without a word. The line
        /// counts whole minutes rounding up, so it changes as what is left crosses a whole minute,
        /// and the code goes when nothing is left; the timer is set for the next of those and a
        /// moment more. It stops when nothing is outstanding and when the panel goes. What
        /// `AdmissionNotice` says against polling is about records, which only the writer can say
        /// have landed; nothing announces a clock.
        /// </remarks>
        private void Schedule(Outstanding outstanding)
        {
            _ticking.Stop();

            if (!outstanding.Any || outstanding.Expires == null)
            {
                return;
            }

            var left = outstanding.Expires.Value - DateTimeOffset.UtcNow;
            var next = TimeSpan.FromTicks(Math.Max(0, left.Ticks) % TimeSpan.TicksPerMinute);
            if (next <= TimeSpan.Zero)
            {
                next = TimeSpan.FromMinutes(1);
            }

            _ticking.Interval = (int)Math.Max(1, next.TotalMilliseconds + Late.TotalMilliseconds);
            _ticking.Start();
        }

        /// <summary>
        /// How long after a line's change or a code's end the page wakes, so that what it reads
        /// then is on the far side of it.
        /// </summary>
        private static readonly TimeSpan Late = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Brings the invitation band up to date with the clock. What the timer does, and public
        /// for the reason every button's work is: a test cannot wait on a timer that no message
        /// loop is running.
        /// </summary>
        /// <remarks>
        /// Asks the invitations again, which is memory, and not the database: the authority's
        /// digest is kept from the last reading.
        /// </remarks>
        public void Tick()
        {
            _ticking.Stop();

            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                ShowOutstanding(OutstandingCodes(DateTimeOffset.UtcNow));
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("the invitation on the clients page could not be brought up to date: " + error.Message);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            Tick();
        }

        /// <summary>
        /// What the clients page says about outstanding codes, and whether there is one at all.
        /// </summary>
        /// <remarks>
        /// Two answers rather than one sentence, because the caller has to decide whether the box
        /// may go on showing a code and cannot work that out by reading the sentence: three
        /// different ones mean "nothing is outstanding", and testing for one of them missed the
        /// other two. Against what the last reading found, kept in <see cref="_offered"/>.
        /// </remarks>
        private Outstanding OutstandingCodes(DateTimeOffset now)
        {
            if (_invitations == null || _service == null)
            {
                return new Outstanding(string.Empty, false, null);
            }

            if (_offered == null)
            {
                return new Outstanding(InvitationText.NoAuthority, false, null);
            }

            var live = _invitations.For(_offered);
            if (live == null || live.Count == 0)
            {
                return new Outstanding(InvitationText.For(live, now), false, null);
            }

            var soonest = live.Min(invitation => invitation.Expires);
            return new Outstanding(InvitationText.For(live, now), true, soonest);
        }

        /// <summary>
        /// What the tab says about outstanding codes, whether any of them is live, and when the
        /// soonest of them runs out.
        /// </summary>
        private sealed class Outstanding
        {
            private readonly string _text;
            private readonly bool _any;
            private readonly DateTimeOffset? _expires;

            public Outstanding(string text, bool any, DateTimeOffset? expires)
            {
                _text = text ?? string.Empty;
                _any = any;
                _expires = expires;
            }

            public string Text
            {
                get { return _text; }
            }

            public bool Any
            {
                get { return _any; }
            }

            /// <summary>When the soonest outstanding code runs out, or null when none is out.</summary>
            public DateTimeOffset? Expires
            {
                get { return _expires; }
            }
        }


        /// <summary>
        /// A record landed in some database, so what this tab draws from the records is stale.
        /// </summary>
        /// <remarks>
        /// Raised on the user interface thread, inside the write it follows, so this reads the
        /// database and redraws directly rather than marshaling.
        ///
        /// **Disposal is the only thing asked about**, and a window existing is deliberately not:
        /// `Describe` is what the constructor itself ends with, long before there is a handle, so
        /// a panel that has not been shown yet reads and draws exactly as one that has. Asking for
        /// a handle here meant the redraw was skipped for any tab built and not yet displayed.
        ///
        /// The whole tab rather than the clients page alone, because a pairing writes a profile
        /// name beside the record whenever it approved one this database did not have, so the
        /// profiles page is stale in exactly the same moment: it is drawn again here when it is the
        /// page in front, and when it is brought forward otherwise, like every page. Nothing here is
        /// typed into by the operator: `ProfileList.Describe` rebuilds its grid and leaves the name
        /// box alone.
        /// </remarks>
        private void OnAdmitted(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                Describe();
            }
            catch (Exception error) when (Expected.OfUserInterface(error)
                                          || Expected.OfCustomData(error))
            {
                // A tab that could not redraw is a tab an operator can bring forward again. The
                // record is already written and the client is already being answered.
                _log.Warn("the tab could not be redrawn after a record landed: " + error.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // First, and not conditional on anything below it. This tab lives as long as the
                // dialog it is injected into and the notice lives as long as the plugin, so a
                // subscription left behind is a disposed control handed an event on the next
                // pairing, for every settings dialog the operator ever opened.
                if (_admissions != null)
                {
                    _admissions.Landed -= OnAdmitted;
                }

                if (_valueFont != null)
                {
                    _valueFont.Dispose();
                }

                if (_tokenFont != null)
                {
                    _tokenFont.Dispose();
                }

                // A `ToolTip` is a component rather than a control, so it is not in `Controls` and
                // nothing disposes it for us. The timer is one too, and stopping it is what keeps a
                // tick from arriving at a panel that has gone.
                if (_tips != null)
                {
                    _tips.Dispose();
                }

                _ticking.Stop();
                _ticking.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Gives the invitation box a monospaced font at the panel's new size.
        /// </summary>
        /// <remarks>
        /// The box's font is its own, and a font set on a control is not one it inherits: built
        /// once, it stayed at the size the panel had in its constructor while everything around it
        /// followed the panel's font. A panel is given a larger font by a dialog whose font is not
        /// the system's, as the page is attached, and by the scaled fit test, which measured the
        /// row holding the box with the box at the unscaled size nobody sees. Nothing happens
        /// before the box exists, because the constructor sets properties that lay the panel out.
        /// </remarks>
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            if (_token == null)
            {
                return;
            }

            var before = _tokenFont;
            _tokenFont = CodeFont(Font.Size);
            _token.Font = _tokenFont;

            if (before != null)
            {
                before.Dispose();
            }
        }

        /// <summary>
        /// Keeps everything inside the width the tab actually has, whatever that turns out to be.
        /// </summary>
        /// <remarks>
        /// Wrapping labels are given a maximum width rather than a fixed one, so they grow
        /// downwards instead of sideways, and the token box takes the width left beside its button.
        /// Sizes are read from the panel rather than written into it, because a number chosen here
        /// is a number that is wrong at another font size, another display scaling, or on a KeePass
        /// whose dialog is not the size this one is.
        /// </remarks>
        protected override void OnLayout(LayoutEventArgs e)
        {
            FitToWidth();
            base.OnLayout(e);
        }

        private void FitToWidth()
        {
            // Setting a property in the constructor lays the panel out before it has been built,
            // so this runs at least once with nothing to fit.
            if (_tabs == null)
            {
                return;
            }

            foreach (var label in _wrapping)
            {
                var width = RoomAround(label) - label.Margin.Horizontal;

                // Only when it changes: assigning this asks for another layout pass.
                if (width > 0 && label.MaximumSize.Width != width)
                {
                    label.MaximumSize = new Size(width, 0);
                }
            }

            if (_token != null && _invite != null && _copy != null)
            {
                // Square, and as tall as the box it sits beside, so the row reads as one thing.
                // Taken from the box rather than written down, because a text box is as tall as the
                // font makes it.
                var side = _token.Height;
                if (side > 0 && (_copy.Width != side || _copy.Height != side))
                {
                    _copy.Size = new Size(side, side);
                }

                var available = RoomAround(_inviting) - _inviting.Margin.Horizontal;

                var width = available
                            - _invite.Width - _invite.Margin.Horizontal
                            - _copy.Width - _copy.Margin.Horizontal
                            - _token.Margin.Horizontal;

                // **Never narrower than a whole code.** What the row leaves is enough at every
                // scaling `GateTabFitsTests` measures, and where it is not, the row runs past the
                // page, which that test sees, rather than the box cutting the code off, which no
                // test saw: at 100 percent the last character was missing whenever there was a
                // code in the box, since that is when the button beside it says "Renew invite".
                width = Math.Max(width, CodeWidth());

                if (width > 0 && _token.Width != width)
                {
                    _token.Width = width;
                }
            }
        }

        /// <summary>
        /// How much width whatever this control sits on really offers.
        /// </summary>
        /// <remarks>
        /// Asked of the page, or of the panel above the pages, rather than of the band the control
        /// happens to be in. A `FlowLayoutPanel` that sizes itself to its content reports the width
        /// of that content, so a band docked to the top of a page answers with how wide it is
        /// rather than with how wide it may be. Everything measured against that comes out narrow,
        /// and it is visible rather than subtle: labels wrap after a few words and the invitation
        /// box shrinks until a code cannot be read out of it.
        /// </remarks>
        private int RoomAround(Control control)
        {
            for (var at = control.Parent; at != null; at = at.Parent)
            {
                var page = at as TabPage;
                if (page != null)
                {
                    return page.ClientSize.Width - page.Padding.Horizontal;
                }

                if (ReferenceEquals(at, this))
                {
                    return ClientSize.Width - Padding.Horizontal;
                }
            }

            return 0;
        }

        /// <summary>
        /// One of the pages inside the KPG tab, inset so its content does not touch the border the
        /// tab control draws around it.
        /// </summary>
        private static TabPage NewPage(string name, string text)
        {
            return new TabPage
            {
                Name = name,
                Text = text,
                UseVisualStyleBackColor = true,

                // Inset at the sides only. The border the tab control draws is what the content
                // has to keep off, and vertically the strip above and the frame below already do
                // that: a second helping of the panel's own padding, top and bottom, was sixteen
                // pixels of the tab spent on nothing, on the one control here that has none to
                // spare.
                //
                // Nothing at the top, because the first thing on every page carries a gap of its
                // own and the two together are one inset asked for twice. The Certificate
                // Authority page is the tallest thing this tab has to hold and was over its room
                // by exactly those two pixels.
                Padding = new Padding(Spacing.Gap, 0, Spacing.Gap, Spacing.Tight),

                // Same rule as the tab itself. What is on a page has to fit the page.
                AutoScroll = false
            };
        }

        /// <summary>A stack of things, one under the next, that never wraps and never scrolls.</summary>
        private static FlowLayoutPanel NewColumn(string name, DockStyle dock)
        {
            return new FlowLayoutPanel
            {
                Name = name,
                Dock = dock,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,

                // A band docked to an edge has to say how tall it is; one that fills does not, and
                // measuring it would fight whatever it was given.
                AutoSize = dock != DockStyle.Fill,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
        }

        private static FlowLayoutPanel NewRow(string name, Padding margin)
        {
            return new FlowLayoutPanel
            {
                Name = name,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = margin
            };
        }

        private Label NewTextLabel(string name, Padding margin, string text = "")
        {
            var label = new TextLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Margin = margin
            };

            _wrapping.Add(label);
            return label;
        }

        private static Button NewButton(string name, string text, EventHandler onClick)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            };

            button.Click += onClick;
            return button;
        }

        /// <summary>
        /// The font an invitation code is shown in, at a size.
        /// </summary>
        /// <remarks>
        /// **Consolas where it is installed, and the generic monospaced family where it is not.**
        /// Both are monospaced, which is what the box needs, and Consolas is narrower, which is
        /// what the row needed. In Courier New, the generic family on Windows, a code at the size
        /// of the rest of the page is 301 pixels wide at 100 percent, and the box beside "Renew
        /// invite" has 294 inside its frame, so the last character was cut off. Consolas sets the
        /// same code in 258, and it tells `0` from `O` and `1` from `l` and `I`, which a code read
        /// off the screen needs and Courier New barely does.
        ///
        /// **Asked for by name and checked**, because GDI+ answers a family it does not have with
        /// Microsoft Sans Serif rather than a refusal, and a proportional code is what this font is
        /// here to prevent. Windows has shipped Consolas since Vista.
        /// </remarks>
        private static Font CodeFont(float size)
        {
            // codeql[cs/missed-using-statement] disposed only when it is not kept, which no `using` can do
            var font = new Font(CodeFamily, size, FontStyle.Regular);

            // Let go of on every way out but the return, a throw while GDI+ is asked for the name
            // among them.
            var handedOver = false;
            try
            {
                if (string.Equals(font.Name, CodeFamily, StringComparison.OrdinalIgnoreCase))
                {
                    handedOver = true;
                    return font;
                }
            }
            finally
            {
                if (!handedOver)
                {
                    font.Dispose();
                }
            }

            return new Font(FontFamily.GenericMonospace, size, FontStyle.Regular);
        }

        /// <summary>The family <see cref="CodeFont"/> asks for first.</summary>
        private const string CodeFamily = "Consolas";

        /// <summary>
        /// How wide the box has to be to show a whole code, its frame included.
        /// </summary>
        /// <remarks>
        /// Measured the way a text box measures its own text, and in the box's own font, so that it
        /// follows the font to whatever size the panel is given. Any forty-three characters will do,
        /// since the font is monospaced.
        /// </remarks>
        private int CodeWidth()
        {
            var code = TextRenderer.MeasureText(
                new string('W', PairingToken.TextLength),
                _token.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            return code.Width + _token.Width - _token.ClientSize.Width;
        }

        /// <summary>
        /// Draws a set of facts this database does not hold.
        /// </summary>
        /// <remarks>
        /// For measurement. What has to fit the tab is the longest each value can ever be, and a
        /// database created a moment ago holds none of them. The next reading replaces whatever is
        /// put here, so this cannot leave anything on screen that outlives it.
        /// </remarks>
        public void DrawFacts(IReadOnlyList<Fact> facts)
        {
            Draw(facts);
        }

        /// <summary>
        /// Puts the facts into the table, replacing whatever was there.
        /// </summary>
        /// <remarks>
        /// Rebuilt rather than updated in place, because the number of rows changes with what the
        /// database holds and a table that kept a row from the last reading would be showing a fact
        /// about an authority that has since been deleted.
        ///
        /// The value is bold and the name is not. One of the two columns is what somebody came to
        /// read, and which one it is should not have to be worked out.
        /// </remarks>
        private void Draw(IReadOnlyList<Fact> facts)
        {
            _facts.SuspendLayout();

            try
            {
                var old = new List<Control>();
                foreach (Control child in _facts.Controls)
                {
                    old.Add(child);
                }

                _facts.Controls.Clear();
                _facts.RowStyles.Clear();
                _facts.RowCount = 0;

                foreach (var control in old)
                {
                    control.Dispose();
                }

                if (facts == null)
                {
                    return;
                }

                _facts.RowCount = facts.Count;

                for (var at = 0; at < facts.Count; at++)
                {
                    _facts.Controls.Add(NewFactLabel(facts[at], false), 0, at);
                    _facts.Controls.Add(NewFactLabel(facts[at], true), 1, at);
                }
            }
            finally
            {
                _facts.ResumeLayout();
            }
        }

        private Label NewFactLabel(Fact fact, bool value)
        {
            return new TextLabel
            {
                Name = fact.ControlName(value),
                Text = value ? fact.Value : fact.Name,
                AutoSize = true,
                Font = value ? _valueFont : Font,

                // Enough of a gap between the columns to read as two, and none above or below: the
                // rows are one line each, and leading between them costs height the tab does not
                // have to spare.
                //
                // The panel's own inset, which `Spacing` makes the same amount, rather than the
                // constant: this row is built on every reading, after the page was scaled to its
                // dialog, and a scaling reaches only the controls that are already there. The inset
                // was one of those.
                Margin = value ? new Padding(0) : new Padding(0, 0, Padding.Left, 0)
            };
        }

        /// <summary>
        /// The authority as the database holds it now, or null when there is no database to read.
        /// </summary>
        /// <remarks>
        /// **Asked of the database, not of the store it hands out.** The database can be gone by
        /// now: KeePass locks or closes it while this dialog is open, which is a state
        /// `PendingDatabase.Apply` names and answers. What that leaves is a store over nothing,
        /// never a null one, because every implementation here builds one on demand and the tab's
        /// own buffer wraps whatever that answers with. Reading through it finds no keys, which
        /// reads exactly like a database that has no authority yet, so the tab offered Create for a
        /// database that was not there. `IsOpen` is the question with an answer, and
        /// `AdmittedClients` and `Revoke` were already asking it.
        /// </remarks>
        private AuthorityReading Read()
        {
            return _service == null || !_database.IsOpen ? null : _service.Read();
        }

        /// <summary>What the first page says, from the redraw's one reading.</summary>
        /// <remarks>
        /// The listing itself rather than its records: whether it could be made at all is what
        /// tells a database that admitted nobody from one this could not read, and the two say
        /// opposite things to an operator deciding whether to recreate the authority. And how far
        /// each record gets with the gate, so that the expiry warnings count the records the
        /// clients page lists as clients and no others.
        /// </remarks>
        private static AuthorityDescription DescribeNow(
            AuthorityReading reading, AdmissionListing admitted, IDictionary<string, ClientReach> reaches)
        {
            return reading == null
                ? AuthorityDescription.NoDatabase()
                : AuthorityDescription.Of(reading, admitted, reaches, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// How many clients recreating or deleting the authority cuts off, read now rather than
        /// remembered: each client once, by its client key, and each record that will not read as
        /// one of its own.
        /// </summary>
        /// <remarks>
        /// A client that renewed holds two records until it gives the older up, and was counted
        /// as two clients that would have to pair again; two programs that paired under one name
        /// are two clients, and counted by name were one. A listing that could not be made falls
        /// back to counting the stored records, which says at least how many keys go.
        ///
        /// This reads every record to get one integer, and the whole authority when they cannot be
        /// listed, which imports two PKCS#12 blobs and builds the host's chain for certificates
        /// nothing here looks at. Caching it from the last
        /// `Describe` was tried and reverted: a panel can be asked to confirm before it has ever
        /// described itself, and the number would then be zero in a sentence telling the operator
        /// what they are about to lose. A wrong count there is worse than a slow one.
        ///
        /// **It is not cheap, and this remark used to say it was**: a quarter of a millisecond,
        /// counting the two imports alone. Measured whole, a read of the authority is about 44
        /// milliseconds on the machine the review ran on. That is still the right price for one
        /// press of Recreate or Delete, which is when this runs. What was not affordable was the
        /// redraw reading the authority three times and building a chain for every record, and
        /// `Describe` reads it once and keeps what the chains answered.
        /// </remarks>
        private int CountRecords()
        {
            var admitted = AdmittedClients();
            if (admitted.Complete)
            {
                return ClientList.ClientNames(admitted.Records).Count + admitted.Unreadable.Count;
            }

            using (var reading = _service.Read())
            {
                return reading.ClientRecordCount;
            }
        }

        private void OnCreate(object sender, EventArgs e)
        {
            CreateAuthority();
        }

        private void OnRecreate(object sender, EventArgs e)
        {
            RecreateAuthority();
        }

        private void OnDelete(object sender, EventArgs e)
        {
            DeleteAuthority();
        }

        private void OnChoose(object sender, EventArgs e)
        {
            ChooseAuthority();
        }

        private void OnInvite(object sender, EventArgs e)
        {
            InviteClient();
        }

        /// <summary>
        /// What the list revokes through: the revocation is added, and the tab is read again so the
        /// count on the authority page stops naming a client that is no longer admitted.
        /// </summary>
        private string RevokeAndDescribe(string admissionKey)
        {
            var refused = Revoke(admissionKey);
            if (refused == null)
            {
                Describe();
            }

            return refused;
        }

        /// <summary>
        /// What the list lifts a standing answer through: the answer is taken away in what the
        /// dialog holds, and the tab is read again so the row shows what OK would leave.
        /// </summary>
        private string AllowRenewalAndDescribe(string admissionKey)
        {
            var refused = AllowRenewal(admissionKey);
            if (refused == null)
            {
                Describe();
            }

            return refused;
        }

        /// <summary>
        /// Each listed client's standing answer to a renewal, asked as the gate asks it: the
        /// database's first, then this session's. A client with none is absent, and so is one
        /// whose day has come.
        /// </summary>
        /// <remarks>
        /// **Read through what the dialog holds**, so an answer lifted on the page and waiting for
        /// OK is already gone from it, and one the renewal dialog gave again meanwhile is back.
        ///
        /// **Nothing is logged here about a value that will not read.** It is answered as forever,
        /// as the gate answers it, and the gate says so in the log every time it answers; this
        /// runs on every redraw, and saying it again on each would bury the line that matters.
        ///
        /// Public, and given the moment, like `Reaches`: what the page shows beside each client is
        /// a decision worth testing without a control tree to walk.
        /// </remarks>
        public IDictionary<string, RenewalRefusal> StandingAnswers(AdmissionListing admitted, DateTimeOffset now)
        {
            var answers = new Dictionary<string, RenewalRefusal>(StringComparer.Ordinal);
            if (_database == null || !_database.IsOpen || admitted == null)
            {
                return answers;
            }

            try
            {
                var store = new AdmissionStore(_database.PluginCustomData, NullGateLog.Instance);
                foreach (var record in admitted.Records)
                {
                    var standing = store.StandingRefusal(record.AdmissionKey);
                    if (standing == null || !standing.Refuses(now))
                    {
                        standing = _sessionRefusals == null ? null : _sessionRefusals.Answer(record.AdmissionKey);
                        if (standing != null && !standing.Refuses(now))
                        {
                            standing = null;
                        }
                    }

                    if (standing != null)
                    {
                        answers[record.AdmissionKey] = standing;
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                // Shown as none rather than guessed at: the gate reads the same key when it is
                // asked, and answers from what it finds there, which this page cannot change.
                _log.Warn("the standing answers to renewals could not be read: " + error.Message);
            }

            return answers;
        }

        /// <summary>
        /// Lift the standing answer to renewing one certificate, whichever it is, so that its next
        /// renewal raises the dialog again. Answers with why it could not, or null when it did.
        /// </summary>
        /// <remarks>
        /// **It waits for the dialog's OK, like a revocation.** A stored answer, forever or until a
        /// date, is taken away in the dialog's buffer; this session's is taken away in what the
        /// dialog holds of the plugin's memory. Both land when the dialog is accepted, and Cancel
        /// leaves the gate refusing as it did.
        ///
        /// **Both, where both stand**, which only a merge bringing in another copy's answer does:
        /// the gate asks the database's first and the session's after it, so lifting one alone
        /// would leave the client refused by the other and the row red after the press.
        ///
        /// By admission key, as a revocation is, because an answer is about one certificate and a
        /// client that renewed is two rows with one client key.
        /// </remarks>
        public string AllowRenewal(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return "there is no client to allow";
            }

            if (_database == null || !_database.IsOpen)
            {
                return "the database is not open";
            }

            bool stored;
            try
            {
                stored = new AdmissionStore(_database.PluginCustomData, _log).LiftRenewalRefusal(admissionKey);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a standing answer to a renewal could not be lifted", error);
                return error.Message;
            }

            var session = _sessionRefusals != null && _sessionRefusals.Lift(admissionKey);
            if (!stored && !session)
            {
                return "no answer to its renewals stands";
            }

            if (stored)
            {
                _database.MarkModified();
            }

            _log.Info("the operator lifted the standing answer to renewing "
                      + CertificateDigest.Shorten(admissionKey));
            return null;
        }

        /// <summary>
        /// Every client this database has admitted, newest first.
        /// </summary>
        /// <remarks>
        /// Public, and separate from the list that shows it, because what the operator is shown is
        /// a decision worth testing on its own, without a control tree to walk to reach it.
        /// </remarks>
        public AdmissionListing AdmittedClients()
        {
            if (_database == null || !_database.IsOpen)
            {
                // Read rather than unreadable: there is no database to have admitted anybody,
                // and the tab says that in its own words on the page above this one.
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), true);
            }

            try
            {
                var found = new AdmissionStore(_database.PluginCustomData, _log).All();
                var ordered = new List<AdmissionRecord>(found.Records);
                ordered.Sort((left, right) => right.Admitted.CompareTo(left.Admitted));

                // Sorting does not make an incomplete listing complete, so the answer is
                // carried through rather than re-decided here, and so are the records that will
                // not read, which the clients page lists after the rest.
                return new AdmissionListing(ordered, found.Unreadable, found.CameBack, found.Complete);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the admitted clients could not be read", error);
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), false);
            }
        }

        /// <summary>
        /// The profiles this database names, which the clients page marks every other held
        /// profile against.
        /// </summary>
        /// <remarks>
        /// Read through the tab's own store, so a removal waiting for OK is shown as it will be
        /// once OK is pressed, the way everything else on the tab is. A list that cannot be read is
        /// an empty one, which marks every held profile, because that is what the gate grants
        /// then: nothing.
        /// </remarks>
        private ICollection<string> NamedProfiles()
        {
            if (_database == null || !_database.IsOpen)
            {
                return null;
            }

            try
            {
                return new HashSet<string>(
                    ProfileNames.Read(_database.PluginCustomData.Get(ProfileNames.CustomDataKey)).Names,
                    StringComparer.Ordinal);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the profiles this database names could not be read: " + error.Message);
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// How far each of those records' certificates gets with the gate before the gate looks
        /// at the record itself, by admission key.
        /// </summary>
        /// <remarks>
        /// **Asked in the gate's order, through the gate's own questions.** `ClientCredentials`
        /// pins the anchor, then asks for client authentication, then the dates, and only then
        /// looks the record up, so a certificate any of the first three refuses is refused
        /// whatever its record says. The clients page asked the first alone, and a record
        /// carrying this authority's own host certificate, or a certificate not valid yet, was
        /// listed as a client admitted until next year: a page promising access nobody has. Such
        /// records arrive by a recreate or a delete landing while a pairing does, by a merge or a
        /// partial restore carrying records past the authority that made sense of them, by a file
        /// edited by hand, and, for the dates, by a clock that is wrong on one of two machines
        /// sharing a file. The last date, the end, is the one the list shows, and it says
        /// "expired" through `CertificateValidity` as the gate does.
        ///
        /// **An absent, damaged or undecided authority makes every record a stranger**, and that
        /// is an answer rather than a gap: nothing is served for such a database at all, so no
        /// record in it reaches anything. The first page says the same in words, and the two
        /// agreeing is the point.
        ///
        /// **An engine that could not evaluate the chain is not an answer**, which is the
        /// distinction `AnchorPin` takes an `out` parameter for. A locked down machine store or a
        /// CryptoAPI that will not load says nothing about where a certificate came from, and
        /// calling a healthy record foreign here points the operator at Recreate, which destroys
        /// the authority and every record with it. Such a record is asked the other questions.
        ///
        /// Public, like the listing it judges, because what the operator is shown is worth testing
        /// without a control tree to walk to reach it.
        /// </remarks>
        public IDictionary<string, ClientReach> Reaches(AdmissionListing listing)
        {
            using (var reading = Read())
            {
                return Reaches(listing, reading, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>The same, against a reading the caller already holds.</summary>
        private IDictionary<string, ClientReach> Reaches(
            AdmissionListing listing, AuthorityReading reading, DateTimeOffset now)
        {
            var reaches = new Dictionary<string, ClientReach>(StringComparer.Ordinal);

            if (_service == null || listing == null || listing.Records.Count == 0)
            {
                return reaches;
            }

            var authority = reading != null && reading.IsPresent ? reading.Material.Authority : null;
            var digest = authority == null ? null : reading.Material.AuthorityDigest;

            if (!string.Equals(digest, _judgedBy, StringComparison.Ordinal))
            {
                _judged.Clear();
                _judgedBy = digest;
            }

            try
            {
                foreach (var record in listing.Records)
                {
                    if (record == null)
                    {
                        continue;
                    }

                    reaches[record.AdmissionKey] = Reach(record, authority, now);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                _log.Error("the admitted clients could not be checked against this authority", error);
            }

            return reaches;
        }

        /// <summary>What the gate asks of one record's certificate before the record, in its order.</summary>
        private ClientReach Reach(AdmissionRecord record, X509Certificate2 authority, DateTimeOffset now)
        {
            using (var certificate = record.Certificate())
            {
                ClientReach judged;
                if (!_judged.TryGetValue(record.AdmissionKey, out judged))
                {
                    bool evaluated;
                    judged = !AnchorPin.Chains(certificate, authority, true, out evaluated) && evaluated
                        ? ClientReach.Stranger
                        : ClientCredentials.IsForClientAuthentication(certificate)
                            ? ClientReach.Reaches
                            : ClientReach.NotForClients;

                    if (evaluated)
                    {
                        _judged[record.AdmissionKey] = judged;
                    }
                }

                if (judged != ClientReach.Reaches)
                {
                    return judged;
                }

                return !CertificateValidity.IsCurrent(certificate, now) && !CertificateValidity.HasExpired(certificate, now)
                    ? ClientReach.NotYetValid
                    : ClientReach.Reaches;
            }
        }

        /// <summary>
        /// Take a client's admission away, answering with why it could not or null when it did.
        /// </summary>
        /// <remarks>
        /// **This waits the way every other button on this tab waits**, which is to say it goes
        /// into what the dialog is holding and reaches the database when the dialog is accepted.
        /// A revocation that acted at once would be the one gesture on this tab behaving
        /// differently from the rest, and the one that loses a disagreement like that is always the
        /// one somebody assumed.
        ///
        /// **The cost of that is worth naming.** Revocation was immediate, and immediacy is
        /// something a security control would rather have: an operator who takes a client's
        /// admission away and then leaves the dialog open has taken nothing away. What it buys is
        /// one model for the whole tab, and a Cancel that means Cancel.
        ///
        /// **Once accepted it takes effect at once, and saving is what keeps it**: a record is
        /// read from the live database on every connection, so the client is refused from the next
        /// request onwards without anything being told.
        ///
        /// **By admission key**, which is how every record is stored and the one thing a record
        /// that will not read still has; the clients page lists those too, and this is how their X
        /// takes them away.
        /// </remarks>
        public string Revoke(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return "there is no client to revoke";
            }

            if (_database == null || !_database.IsOpen)
            {
                return "the database is not open";
            }

            try
            {
                var store = new AdmissionStore(_database.PluginCustomData, _log);

                // **The answer is not thrown away.** `AdmissionStore.Remove` reads the key back
                // before it says yes, precisely because telling somebody a client is revoked when
                // the write did not land is the one wrong answer here that nothing notices until
                // that client connects again. A false can also mean the record was already gone,
                // which is the operator's intent either way, so the two are told apart by looking.
                //
                // **On the tab this is the dialog's buffer**, where the read back finds what was
                // just written and a write cannot fail, so the press itself is refused only for a
                // database that has gone from under the dialog. Whether the revocation reached the
                // database is decided when the dialog is accepted, and said in the log there. The
                // read back and the catch below are for a panel handed a database of its own.
                if (!store.Remove(admissionKey) && Holds(store, admissionKey))
                {
                    _log.Error("an admission record would not go away", null);
                    return "the record is still in the database";
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("an admission record could not be removed", error);
                return error.Message;
            }

            _database.MarkModified();
            _log.Info("an admission record was removed by the operator");
            return null;
        }

        /// <summary>
        /// Whether a record is still stored under this key and not revoked, readable or not.
        /// </summary>
        /// <remarks>
        /// Not `Find` alone, which answers null for a record that will not read: a removal of one of
        /// those that did not land would be reported as one that did, and its row would go while
        /// the record stayed.
        /// </remarks>
        private static bool Holds(AdmissionStore store, string admissionKey)
        {
            return store.Find(admissionKey) != null
                   || store.All().Unreadable.Contains(admissionKey, StringComparer.Ordinal);
        }

        private void OnCopy(object sender, EventArgs e)
        {
            CopyInvitation();
        }

        private static string Cost(int records)
        {
            if (records <= 0)
            {
                return "No client has been admitted yet, so nothing else is lost.";
            }

            return records == 1
                ? "One client is admitted, and it would have to pair again."
                : records + " clients are admitted, and every one of them would have to pair again.";
        }

        /// <summary>
        /// Runs one operator action. A null result means they were asked and said no, which is not
        /// a failure and is not reported as one.
        /// </summary>
        private void Act(Func<AuthorityChange> action)
        {
            if (_service == null)
            {
                return;
            }

            AuthorityChange change;
            try
            {
                change = action();
            }
            catch (Exception error) when (Expected.OfCustomData(error)
                                          || Expected.OfCryptography(error)
                                          || Expected.OfUserInterface(error))
            {
                _log.Error("an authority action failed", error);
                Said("That did not work: " + error.Message);
                Describe();
                return;
            }

            if (change == null)
            {
                return;
            }

            if (change.Succeeded)
            {
                try
                {
                    _database.MarkModified();
                }
                catch (Exception error) when (Expected.OfCustomData(error))
                {
                    _log.Error("the database could not be marked as modified", error);
                }

                // Nothing said, and the line collapses so the page gets its height back.
                //
                // What a sentence would add is the word for something already on screen. The
                // table above fills in, empties, or shows a different fingerprint; the buttons
                // swap over between create and delete; and what recreating costs is in the
                // question the operator answers before pressing anything, which is the only
                // moment that number is any use to them.
                //
                // A failure still says so, because nothing else on the page would.
                Said(string.Empty);
            }
            else
            {
                Said(change.Message);
            }

            Describe();
        }

        /// <summary>
        /// Puts one line in the band under the pages, cut to something the band can hold.
        /// </summary>
        /// <remarks>
        /// **Every path goes through here, including the ones whose words are this plugin's own.**
        /// Two of them were not this plugin's: `AuthorityChange.Message` carries whatever the
        /// store threw, and so does the sentence this page composes after a failure. Cutting at
        /// each site meant the two that were missed were exactly the two that could not be
        /// bounded, and it meant the band's own limit was whatever the last author remembered.
        /// One setter, one bound, and <see cref="LongestResult"/> is that bound written down for
        /// the test that measures the band.
        /// </remarks>
        private void Said(string line)
        {
            _result.Text = Clipped.Message(line);
        }

        /// <summary>
        /// The longest line the band under the pages can ever hold.
        /// </summary>
        /// <remarks>
        /// For measurement, like <see cref="DrawFacts"/>. What has to fit the tab is the longest
        /// this band can be, which is a refusal carrying somebody else's exception message, rather
        /// than a sentence a third of that. A band measured against the short one fits while the
        /// long one arrives on somebody's screen, and the room for the long one is what the gate's
        /// address and the version leaving this tab pay for.
        ///
        /// The words are ordinary words in ordinary lengths rather than a run of one character,
        /// because what is being measured is how many lines the band wraps to and a string with
        /// nowhere to break does not wrap at all.
        ///
        /// It is the longest there is only while it is long enough to be cut, which is why a test
        /// holds it to the length <see cref="Said"/> cuts at rather than trusting the words.
        /// </remarks>
        public static string LongestResult()
        {
            return Clipped.Message(
                "The authority could not be recreated: the key set for this certificate is not "
                + "available on this machine, and the profile it would have been written to could "
                + "not be loaded either. Ask whoever administers this computer.");
        }
    }
}
