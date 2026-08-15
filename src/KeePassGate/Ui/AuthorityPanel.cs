using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The KPG tab: what this database's authority is, who has been let in through it, and what an
    /// operator can do to either.
    /// </summary>
    /// <remarks>
    /// **Two pages, because there are two subjects.** The authority is what this database can
    /// issue; the clients are who it is issued to. They are changed on different occasions and read
    /// on different occasions, and one page carrying both meant every line competing for the same
    /// 449 by 314. The plugin's name, its version and whether the gate is serving sit above both
    /// pages, because they are true whichever page is in front.
    ///
    /// Two things are said on screen rather than assumed. What OK and Cancel mean, under both
    /// pages, because nothing here reaches the database until the dialog is accepted and saving is
    /// what writes it to disk. And what recreating costs, in the number of clients that lose their
    /// admission, because that is the number the operator cannot work out for themselves.
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
        /// One line rather than two, and that is a measurement rather than a preference. The tab
        /// is 449 by 314 and nothing on it scrolls, so a sentence that wraps is a whole line of
        /// the tab spent saying the same thing twice. It says the two things that are true and
        /// useful: which button makes any of this real, and that the file is written when the
        /// database is saved. `GateTabFitsTests` is what fails when it grows back.
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
        private readonly IGateStatus _gate;
        private readonly IInvitations _invitations;
        private readonly GateIncidents _incidents;

        private readonly IAdmissionNotice _admissions;
        private readonly AuthorityService _service;
        private readonly Font _titleFont;
        private readonly Font _valueFont;

        private readonly Font _tokenFont;
        private readonly TabControl _tabs;
        private readonly FlowLayoutPanel _inviting;

        /// <summary>The labels that wrap, which are the ones a narrow tab has to be told about.</summary>
        private readonly List<Label> _wrapping = new List<Label>();

        private readonly Label _serving;
        private readonly Label _status;
        private readonly TableLayoutPanel _facts;
        private readonly Label _detail;
        private readonly Label _result;
        private readonly Button _create;
        private readonly Button _recreate;
        private readonly Button _delete;
        private readonly Button _invite;
        private readonly TextBox _token;
        private readonly Button _copy;
        private readonly ToolTip _tips;
        private readonly Label _invitation;
        private readonly ClientList _clients;

        /// <summary>
        /// Where a copied invitation goes. Settable for the same reason the button's work is a
        /// public method: a test that took the developer's clipboard away every time it ran would
        /// be a test nobody runs.
        /// </summary>
        internal IClipboard Clipboard { get; set; }

        public AuthorityPanel(IGateDatabase database, IOperatorPrompt prompt, IGateLog log, string version)
            : this(database, prompt, log, version, null, null)
        {
        }

        public AuthorityPanel(
            IGateDatabase database, IOperatorPrompt prompt, IGateLog log, string version, IGateStatus gate)
            : this(database, prompt, log, version, gate, null)
        {
        }

        public AuthorityPanel(
            IGateDatabase database,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            IInvitations invitations)
            : this(database, prompt, log, version, gate, invitations, null)
        {
        }

        public AuthorityPanel(
            IGateDatabase database,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            IInvitations invitations,
            GateIncidents incidents)
            : this(database, prompt, log, version, gate, invitations, incidents, null)
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
            string version,
            IGateStatus gate,
            IInvitations invitations,
            GateIncidents incidents,
            IAdmissionNotice admissions)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            _database = database;
            Clipboard = SystemClipboard.Instance;
            _prompt = prompt ?? new MessageBoxPrompt();
            _log = log ?? NullGateLog.Instance;
            _gate = gate;
            _invitations = invitations;
            _incidents = incidents;
            _admissions = admissions;

            if (_admissions != null)
            {
                _admissions.Landed += OnAdmitted;
            }

            _service = database == null || !database.IsOpen
                ? null
                : new AuthorityService(database.PluginCustomData, _log);

            Name = GateTab.PanelControlName;
            Dock = DockStyle.Fill;
            Padding = new Padding(Spacing.Gap);

            // Neither axis scrolls. KeePass gives a settings tab a fixed area and the content has
            // to fit it: a scroll bar here would hide the half of the tab that says what the
            // buttons cost. `GateTabFitsTests` measures this against the real dialog, so content
            // that outgrows the tab fails a test rather than appearing on somebody's screen.
            AutoScroll = false;

            _titleFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            _valueFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

            // What the plugin is and whether it is serving, above both pages, because neither
            // answer depends on which page is in front.
            var header = NewColumn(DockStyle.Top);

            // The name and the version share a line rather than taking two, and the line about
            // the socket goes tight under them: all three answer questions about the plugin
            // rather than about this database, so they read as one band and are separated from
            // what follows once rather than twice. The room that buys is what the headline
            // under it spends on having air above as well as below.
            var heading = NewRow(new Padding(0, 0, 0, Spacing.Tight));
            heading.Controls.Add(new TextLabel
            {
                Name = GateTab.TitleLabelName,
                Text = "KeePassGate",
                Font = _titleFont,
                AutoSize = true,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            });

            heading.Controls.Add(new TextLabel
            {
                Name = GateTab.VersionLabelName,
                Text = "Version " + version,
                AutoSize = true,
                Margin = new Padding(0)
            });

            header.Controls.Add(heading);

            _serving = NewTextLabel(GateTab.ServingLabelName, new Padding(0, 0, 0, Spacing.Gap));
            header.Controls.Add(_serving);

            _tabs = new TabControl
            {
                Name = GateTab.SubTabsName,
                Dock = DockStyle.Fill
            };

            _tabs.TabPages.Add(NewPage(GateTab.AuthorityPageName, GateTab.AuthorityPageText));
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

            var authority = NewColumn(DockStyle.Fill);

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

            var buttons = NewRow(new Padding(0, 0, 0, Spacing.Tight));
            _create = NewButton(GateTab.CreateButtonName, GateTab.CreateButtonText, OnCreate);
            _recreate = NewButton(GateTab.RecreateButtonName, GateTab.RecreateButtonText, OnRecreate);
            _delete = NewButton(GateTab.DeleteButtonName, GateTab.DeleteButtonText, OnDelete);
            buttons.Controls.Add(_create);
            buttons.Controls.Add(_recreate);
            buttons.Controls.Add(_delete);
            authority.Controls.Add(buttons);

            _tabs.TabPages[0].Controls.Add(authority);

            // The button, the token it produces and the copy button sit on one line, and the box
            // takes whatever width is left over, so nothing here has a size that a wider or
            // narrower tab breaks.
            var invitation = NewColumn(DockStyle.Top);

            // A docked band is laid out edge to edge against what it is given, so its margin
            // is never consulted and the space above it has to be padding.
            invitation.Padding = new Padding(0, Spacing.Gap, 0, 0);

            // Tight above, because the line over it is about the code this row hands out and
            // the two read as one thing. Nothing below: what follows this band brings its own
            // space.
            _inviting = NewRow(new Padding(0, Spacing.Tight, 0, 0));
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

            invitation.Controls.Add(_inviting);

            // Under the widgets that hand an invitation out, because the list is what an invitation
            // turns into. The list fills what the invitation band leaves, so it goes in first:
            // docking is applied from the end of the collection backwards.
            _clients = new ClientList(RevokeAndDescribe);
            _tabs.TabPages[1].Controls.Add(_clients);
            _tabs.TabPages[1].Controls.Add(invitation);

            // What the last press did, below both pages rather than on one of them. Both have
            // something to say here: the authority page has three buttons and the clients page has
            // an invitation, a copy and a revocation, and while this label lived on the authority
            // page an invitation that could not be made reported itself onto a page the operator
            // was not looking at.
            var footer = NewColumn(DockStyle.Bottom);
            _result = NewTextLabel(GateTab.ResultLabelName, new Padding(0, Spacing.Gap, 0, 0));
            footer.Controls.Add(_result);

            // **Under every page rather than on one of them**, for the reason the line above it is:
            // what it says is true of the whole tab. It used to sit at the bottom of the authority
            // page, where an operator revoking a client on the clients page never saw it and was
            // never told that the revocation was waiting for OK like everything else. It is also
            // the last thing above the dialog's own OK button, which is what it is about.
            footer.Controls.Add(
                NewTextLabel(GateTab.NoticeLabelName, new Padding(0, Spacing.Tight, 0, 0), ImmediateNotice));

            Controls.Add(_tabs);
            Controls.Add(header);
            Controls.Add(footer);

            Describe();
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
            var pending = _database as IPendingChanges;
            if (pending != null && AuthorityWaiting(pending))
            {
                Said(InvitationText.AuthorityWaiting);
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

                    // **Nor against an authority that has run out.** `PAIRING.md` §311 refuses a
                    // request on one before the operator is disturbed, so a code minted here is a
                    // code its holder is told to throw away, and the operator finds that out from
                    // whoever was setting the program up rather than from the page they made it
                    // on. The button is dead in this state; this is what answers a clock that
                    // crossed the expiry after the page was last drawn.
                    if (CertificateValidity.HasExpired(reading.Material.Authority, DateTimeOffset.UtcNow))
                    {
                        Said(InvitationText.AuthorityExpired);
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
        /// Whether any of the authority's own keys is still waiting for the dialog to be accepted.
        /// </summary>
        /// <remarks>
        /// Only the authority's keys. A revocation waiting on the clients page changes nothing
        /// about what the gate would resolve a fresh code to, so refusing to invite because of one
        /// would be refusing for no reason.
        /// </remarks>
        private static bool AuthorityWaiting(IPendingChanges pending)
        {
            return pending.WaitingOn(AuthorityKeys.IsAuthorityState);
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
            try
            {
                var description = DescribeNow();
                _serving.Text = GateStatusText.For(
                    _gate, description.IsServed, _incidents == null ? null : _incidents.Summary);
                _status.Text = description.Headline;
                Draw(description.Facts);
                _detail.Text = string.Join(Environment.NewLine, description.Details);
                _create.Enabled = description.CanCreate;
                _recreate.Enabled = description.CanRecreate;
                _delete.Enabled = description.CanDelete;

                // Inviting needs an authority that can still issue, which is narrower than
                // what recreating and deleting need: `CanRecreate` is true of a damaged
                // authority and of one past its own dates, and a code handed out in either is
                // a code whose redemption answers with a failure about a state only the
                // operator can do anything about.
                _invite.Enabled = _invitations != null && description.CanInvite;

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
                var outstanding = OutstandingCodes();
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

                if (!outstanding.Any)
                {
                    _token.Text = string.Empty;
                }

                // Last, and after the box has been settled. Copying needs something to copy, which
                // is a different question from whether a new invitation can be made, and reading it
                // before the line above left the button enabled over an empty box: pressing it then
                // did nothing at all, said nothing, and looked exactly like a clipboard failure.
                _copy.Enabled = !string.IsNullOrEmpty(_token.Text);
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
            }

            // Outside the catch above, because a tab that failed to describe its authority and went
            // on showing the clients it listed a minute ago would be lying about who can reach this
            // database. Reading the records has its own refusal and answers with nobody.
            //
            // Inside one of its own, though, because filling a grid is not reading a database: rows
            // and window handles fail for reasons of their own, and this runs on the click of every
            // button on the tab. An exception leaving here would leave a click handler and reach
            // KeePass's message loop, which closes the dialog and reports nothing.
            try
            {
                var admitted = AdmittedClients();
                _clients.Fill(admitted, Strangers(admitted));
            }
            catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfCustomData(error))
            {
                _log.Error("the admitted clients could not be shown", error);
            }
        }

        /// <summary>
        /// What the clients page says about outstanding codes, and whether there is one at all.
        /// </summary>
        /// <remarks>
        /// Two answers rather than one sentence, because the caller has to decide whether the box
        /// may go on showing a code and cannot work that out by reading the sentence: three
        /// different ones mean "nothing is outstanding", and testing for one of them missed the
        /// other two.
        /// </remarks>
        private Outstanding OutstandingCodes()
        {
            if (_invitations == null || _service == null)
            {
                return new Outstanding(string.Empty, false);
            }

            using (var reading = _service.Read())
            {
                if (!reading.IsPresent)
                {
                    return new Outstanding(InvitationText.NoAuthority, false);
                }

                // **An authority that has run out is the same answer as one that is gone.** The
                // invitation is still in the book and still live by its own clock, so a page that
                // asked the book alone would count a code down with the copy button alive beside
                // it; `PAIRING.md` §311 refuses the redemption, so what an operator copied and
                // handed over would be answered with `500 expired authority`. Nothing burns it
                // here, because a recreated authority has a different digest and this one is
                // swept on its own expiry: what changes is only what the page offers.
                //
                // Said as nothing outstanding rather than as a reason. This line is about what is
                // on offer, and an authority that cannot issue is what the button under it
                // already refuses.
                if (CertificateValidity.HasExpired(reading.Material.Authority, DateTimeOffset.UtcNow))
                {
                    return new Outstanding(InvitationText.None, false);
                }

                var live = _invitations.For(reading.Material.AuthorityDigest);
                return new Outstanding(
                    InvitationText.For(live, DateTimeOffset.UtcNow), live != null && live.Count > 0);
            }
        }

        /// <summary>What the tab says about outstanding codes, and whether any of them is live.</summary>
        private sealed class Outstanding
        {
            private readonly string _text;
            private readonly bool _any;

            public Outstanding(string text, bool any)
            {
                _text = text ?? string.Empty;
                _any = any;
            }

            public string Text
            {
                get { return _text; }
            }

            public bool Any
            {
                get { return _any; }
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

                if (_titleFont != null)
                {
                    _titleFont.Dispose();
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
                // nothing disposes it for us.
                if (_tips != null)
                {
                    _tips.Dispose();
                }
            }

            base.Dispose(disposing);
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
        private static FlowLayoutPanel NewColumn(DockStyle dock)
        {
            return new FlowLayoutPanel
            {
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

        private static FlowLayoutPanel NewRow(Padding margin)
        {
            return new FlowLayoutPanel
            {
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
            var font = new Font(CodeFamily, size, FontStyle.Regular);
            if (string.Equals(font.Name, CodeFamily, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
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
                    _facts.Controls.Add(NewFactLabel(facts[at].Name, false), 0, at);
                    _facts.Controls.Add(NewFactLabel(facts[at].Value, true), 1, at);
                }
            }
            finally
            {
                _facts.ResumeLayout();
            }
        }

        private Label NewFactLabel(string text, bool value)
        {
            return new TextLabel
            {
                Text = text,
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

        private AuthorityDescription DescribeNow()
        {
            if (_service == null)
            {
                return AuthorityDescription.NoDatabase();
            }

            // **Asked of the database, not of the store it hands out.** The database can be gone
            // by now: KeePass locks or closes it while this dialog is open, which is a state
            // `PendingDatabase.Apply` names and answers. What that leaves is a store over nothing,
            // never a null one, because every implementation here builds one on demand and the
            // tab's own buffer wraps whatever that answers with. Reading through it finds no keys,
            // which reads exactly like a database that has no authority yet, so the tab offered
            // Create for a database that was not there. `IsOpen` is the question with an answer,
            // and `AdmittedClients` and `Revoke` were already asking it.
            if (!_database.IsOpen)
            {
                return AuthorityDescription.NoDatabase();
            }

            // Asked for again rather than remembered, for the reason `OpenDatabase` gives: a
            // database that is locked and opened again comes back with its custom data replaced.
            var store = _database.PluginCustomData;

            using (var reading = _service.Read())
            {
                // The records only. What this builds is the expiry warnings, and a listing that
                // The listing itself, not its records: whether it could be made at all is what
                // tells a database that admitted nobody from one this could not read, and the two
                // say opposite things to an operator deciding whether to recreate the authority.
                return AuthorityDescription.Of(
                    reading, new AdmissionStore(store, _log).All(), DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// How many clients are admitted, read now rather than remembered.
        /// </summary>
        /// <remarks>
        /// This reads the whole authority to get one integer, which imports two PKCS#12 blobs to
        /// parse certificates nothing here looks at. Caching it from the last `Describe` was
        /// tried and reverted: a panel can be asked to confirm before it has ever described
        /// itself, and the number would then be zero in a sentence telling the operator what
        /// they are about to lose. A wrong count there is worse than a slow one.
        ///
        /// It is also not slow. An import off a warm runtime is about 0.13 ms on .NET Framework
        /// 4.8, measured over two hundred of them, so the two here cost a quarter of a millisecond
        /// and the whole tab's worth costs a few. That number is written down because it is the
        /// reason not to cache this later either, and rediscovering it means measuring again.
        /// </remarks>
        private int CountRecords()
        {
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

        private void OnInvite(object sender, EventArgs e)
        {
            InviteClient();
        }

        /// <summary>
        /// What the list revokes through: the revocation is added, and the tab is read again so the
        /// count on the authority page stops naming a client that is no longer admitted.
        /// </summary>
        private string RevokeAndDescribe(AdmissionRecord record)
        {
            var refused = Revoke(record);
            if (refused == null)
            {
                Describe();
            }

            return refused;
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
                // carried through rather than re-decided here.
                return new AdmissionListing(ordered, found.CameBack, found.Complete);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the admitted clients could not be read", error);
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), false);
            }
        }

        /// <summary>
        /// Which of those records this database's authority did not issue, by admission key.
        /// </summary>
        /// <remarks>
        /// **Asked through `AnchorPin`, because that is the first thing the gate asks.**
        /// `ClientCredentials` pins the anchor before the usage, the dates and the record itself,
        /// so a certificate chaining to some other authority is refused whatever else is true of
        /// it. The clients page listed those rows as admitted clients with a date in the future
        /// beside them, which is a page promising access nobody has. They arrive by a recreate or
        /// a delete landing while a pairing does, and by a merge or a partial restore carrying
        /// records past the authority that made sense of them.
        ///
        /// **An absent or damaged authority makes every record one**, and that is an answer rather
        /// than a gap: nothing is served for such a database at all, so no record in it reaches
        /// anything. The first page says the same in words, and the two agreeing is the point.
        ///
        /// **An engine that could not evaluate the chain is not an answer**, which is the
        /// distinction `AnchorPin` takes an `out` parameter for. A locked down machine store or a
        /// CryptoAPI that will not load says nothing about where a certificate came from, and
        /// calling a healthy record foreign here points the operator at Recreate, which destroys
        /// the authority and every record with it.
        ///
        /// Public, like the listing it judges, because what the operator is shown is worth testing
        /// without a control tree to walk to reach it.
        /// </remarks>
        public ICollection<string> Strangers(AdmissionListing listing)
        {
            var strangers = new HashSet<string>(StringComparer.Ordinal);

            if (_service == null || listing == null || listing.Records.Count == 0)
            {
                return strangers;
            }

            try
            {
                using (var reading = _service.Read())
                {
                    var authority = reading.IsPresent ? reading.Material.Authority : null;

                    foreach (var record in listing.Records)
                    {
                        if (record == null)
                        {
                            continue;
                        }

                        using (var certificate = record.Certificate())
                        {
                            bool evaluated;
                            if (!AnchorPin.Chains(certificate, authority, true, out evaluated)
                                && evaluated)
                            {
                                strangers.Add(record.AdmissionKey);
                            }
                        }
                    }
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                _log.Error("the admitted clients could not be checked against this authority", error);
            }

            return strangers;
        }

        /// <summary>
        /// Take a client's admission away, answering with why it could not or null when it did.
        /// </summary>
        /// <remarks>
        /// **This writes the way every other button on this tab writes**, which is through the
        /// decorator holding the tab's changes until the dialog is accepted. Nothing reaches the
        /// database here: OK is what applies it and saving is what puts it on disk, which is what
        /// the notice under the pages says. A revocation that went straight to the database would
        /// be the one gesture on this tab behaving differently from the rest, on the page where an
        /// operator is least likely to expect it.
        ///
        /// **It takes effect once it is applied and saved, and not before**, which is the honest
        /// thing to say about it: a record is read from the live database on every connection, so
        /// from that moment the client is refused from its next request onwards without anything
        /// having to be told. Until then it is still admitted, and Cancel still means Cancel.
        /// </remarks>
        public string Revoke(AdmissionRecord record)
        {
            if (record == null)
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
                if (!store.Remove(record.AdmissionKey) && store.Find(record.AdmissionKey) != null)
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

                // Nothing said, and the line collapses so the page gets the height back.
                //
                // What a sentence would add is the word for something already on screen. The table
                // above fills in, empties, or shows a different fingerprint, and the buttons swap
                // over between create and delete. A failure still says so, because nothing else on
                // the page would.
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
        /// One setter, one bound, and `Clipped.Message` is where the bound is written down.
        /// </remarks>
        private void Said(string line)
        {
            _result.Text = Clipped.Message(line);
        }

    }
}
