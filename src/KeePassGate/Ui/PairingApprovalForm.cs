using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The dialog that asks whether a stranger may have a certificate for this database.
    /// </summary>
    /// <remarks>
    /// The only place an API request reaches a screen, and everything about it assumes the operator
    /// is being interrupted by somebody else's program.
    ///
    /// - **Nothing is approved by default.** Refuse is the button Escape and Enter both reach, and
    ///   approving takes a deliberate press. A dialog that can be dismissed into a yes is a dialog
    ///   that will be.
    /// - **The profiles are confirmed rather than accepted.** They arrive as a request and leave as
    ///   whatever is still checked, because the caller asking for something is not the same as the
    ///   operator agreeing to it.
    /// - **It closes itself.** Nobody being at the keyboard need not end in a refusal that costs the
    ///   invitee its invitation, so an unanswered dialog is a timeout, which spends nothing.
    /// - **Dismissing it is refusing it.** The close box, Alt and F4, the window menu and the
    ///   taskbar all end the dialog without a button being pressed, and somebody was at the screen
    ///   for every one of them. Only the countdown answers for an operator who is not.
    /// - **Every string on it is what the request said**, and the request came from outside. Names
    ///   and profiles have already been checked to be plain names by the time they reach here.
    /// </remarks>
    public sealed class PairingApprovalForm : Form
    {
        private readonly CheckedListBox _profiles;
        private readonly Label _scrolling;
        private readonly Label _regaining;
        private readonly Label _countdown;
        private readonly Timer _clock;
        /// <summary>
        /// What approving costs, said once and in full. Separate from the table above it because
        /// this is a sentence to read rather than a value to check.
        /// </summary>
        public const string Consequence =
            "Approving issues it a certificate and saves this database, so the admission is on " +
            "disk before the client is told anything. It can be revoked later from the " +
            GateName.OnATab + " tab.";

        /// <summary>
        /// What the list underneath is, said in terms of what the client asked for rather than as
        /// a bare "these". A reader arriving at that word has to look back up to find out what it
        /// refers to, and what it refers to is the whole decision.
        /// </summary>
        public const string ProfilesAsked =
            "The client is asking for binding to these profiles. " +
            "Uncheck anything it should not have:";

        /// <summary>The same when it asked for nothing, which is unusual enough to say plainly.</summary>
        public const string NoProfilesAsked =
            "The client is asking for no profiles, so it would be admitted and reach nothing.";

        /// <summary>
        /// What marks a profile this database does not name now and no client holds. Approving one
        /// adds it to the list.
        /// </summary>
        /// <remarks>
        /// **Not "never named"**: a name the operator removed, whose every holder has since been
        /// revoked or has expired, looks the same from here, and the rules naming it were left in
        /// place when it was removed. Whether any rule names it is a walk of the whole tree, which
        /// a connection does not make, so the dialog says what approving does either way.
        /// </remarks>
        public const string NewSuffix = " (new)";

        /// <summary>Said under the list when any of them is new.</summary>
        public const string NewProfilesAsked =
            "The client is asking for binding to these profiles. One or more are marked" +
            NewSuffix + ", which means this database does not name them now: approving adds them, " +
            "and any rule already naming one grants it to this client at once. A name no rule names " +
            "grants nothing until one does. Uncheck anything it should not have:";

        /// <summary>
        /// Said under the list when any of them would be added and the database's list is already
        /// full, so approving adds none of them.
        /// </summary>
        public static readonly string FullProfilesAsked =
            "The client is asking for binding to these profiles. One or more are marked" + NewSuffix +
            " or (removed), and this database already names as many profiles as it can hold, " +
            KeePassGate.Acl.ProfileNames.Most.ToString(CultureInfo.InvariantCulture) + ": approving adds none of them, " +
            "so the client holds them and reaches nothing through them until one is added on the " +
            "Profiles page, after another is taken off. Uncheck anything it should not have:";

        /// <summary>
        /// What marks a profile this database named once and removed, which clients still hold.
        /// Approving one gives it back to all of them.
        /// </summary>
        public const string RemovedSuffix = " (removed)";

        /// <summary>
        /// How a profile reads in the list: its name, and whether approving it would create it or
        /// bring back one that was removed.
        /// </summary>
        public static string Listed(string profile, bool isNew, bool removed)
        {
            return removed ? profile + RemovedSuffix : isNew ? profile + NewSuffix : profile;
        }

        /// <summary>The name back out of what the list shows, which is what gets approved.</summary>
        public static string Asked(string shown)
        {
            foreach (var suffix in new[] { NewSuffix, RemovedSuffix })
            {
                if (shown != null && shown.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return shown.Substring(0, shown.Length - suffix.Length);
                }
            }

            return shown;
        }

        /// <summary>
        /// What the line under the list says about the clients that regain a removed profile, or
        /// null when approving gives nothing back to anybody.
        /// </summary>
        /// <remarks>
        /// The same warning the Profiles page gives before a removed name is added back, because
        /// approving one here is exactly that: pairing writes the approved names onto the list.
        /// Every client by name, as the Clients page calls it, since which clients regain the name
        /// is what the operator is being asked to agree to. The rules come back with it, which the
        /// page counts and this cannot: counting them walks the whole tree, and this is asked from
        /// a connection rather than from the window that owns the tree.
        /// </remarks>
        public static string Regaining(PairingRequest request)
        {
            if (request == null || request.ListIsFull)
            {
                // A full list takes no name back, so nobody regains anything: `FullProfilesAsked`
                // says that instead.
                return null;
            }

            var said = new List<string>();

            foreach (var profile in request.Profiles)
            {
                var holding = request.Returning(profile);
                if (holding.Count == 0)
                {
                    continue;
                }

                var names = new List<string>();
                foreach (var record in holding)
                {
                    names.Add(ClientList.Describe(record));
                }

                names.Sort(StringComparer.Ordinal);

                said.Add((names.Count == 1
                        ? "One client still holds " + profile + " from before and will regain it: "
                        : names.Count + " clients still hold " + profile + " from before and will regain it: ")
                    + string.Join(", ", names) + ".");
            }

            if (said.Count == 0)
            {
                return null;
            }

            said.Add("Approving a profile marked" + RemovedSuffix + " adds it back to this database, "
                + "and every rule still naming it applies again.");
            return string.Join(" ", said);
        }

        /// <summary>
        /// What the line under the list says when the list holds more profiles than it shows at
        /// once, or null when every one of them is on screen.
        /// </summary>
        /// <remarks>
        /// **Approve grants every profile still checked, whether it was ever on screen or not**,
        /// and a request may carry 32 of them in the order the client chose, every one checked.
        /// Past the rows the list shows, the only sign of the rest was a scroll bar, so ten harmless
        /// names first and the wanted ones after were approved by a glance at the ten, and a
        /// profile marked as new could be the one out of sight. This says how many there are, next
        /// to the button that grants them. Absent for a list that shows everything, so the
        /// ordinary dialog is unchanged.
        /// </remarks>
        public static string Scrolling(PairingRequest request)
        {
            if (request == null || request.Profiles.Count <= MostRows)
            {
                return null;
            }

            return "The list holds " + request.Profiles.Count.ToString(CultureInfo.InvariantCulture)
                + " profiles and shows " + MostRows.ToString(CultureInfo.InvariantCulture)
                + " at a time: scroll through it, because Approve grants every one still checked, "
                + "seen or not.";
        }

        /// <summary>What the line above the list says, which depends on whether any are new.</summary>
        public static string Asking(PairingRequest request)
        {
            if (request == null || request.Profiles.Count == 0)
            {
                return NoProfilesAsked;
            }

            var adding = false;
            var anyNew = false;
            foreach (var profile in request.Profiles)
            {
                var isNew = request.IsNew(profile);
                anyNew |= isNew;
                adding |= isNew || request.Returning(profile).Count > 0;
            }

            if (adding && request.ListIsFull)
            {
                return FullProfilesAsked;
            }

            return anyNew ? NewProfilesAsked : ProfilesAsked;
        }

        private readonly Font _titleFont;
        private readonly Font _valueFont;

        /// <summary>
        /// How long the operator gets, and how much of it has gone.
        /// </summary>
        /// <remarks>
        /// A stopwatch rather than two readings of the wall clock. The connection thread waiting on
        /// this dialog is waiting on a handle, which counts monotonically, and a clock that steps
        /// while the dialog is up would put the two out of step: backwards and the dialog never
        /// closes itself while the caller gives up anyway, leaving a live-looking Approve button
        /// that issues nothing; forwards and it slams shut in the middle of being read. Both happen
        /// for ordinary reasons: a time correction, or a laptop coming back from sleep.
        /// </remarks>
        private readonly TimeSpan _allowed;
        private readonly int _wrap;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();

        private PairingDecision _decision = PairingDecision.TimedOut;

        /// <summary>Whether the answer above is one that was given, or the one it started at.</summary>
        private bool _decided;

        public PairingApprovalForm(PairingRequest request, TimeSpan timeout)
            : this(request, timeout, null)
        {
        }

        /// <summary>
        /// The same, built at a font other than the system's, which is how a test stands in for a
        /// display that was scaled before the dialog was made.
        /// </summary>
        /// <remarks>
        /// **Everything that sizes this dialog is measured while it is built**, from the font it is
        /// built at: how wide a line may be, and how tall the list is for the rows it holds. A
        /// larger font handed in here is measured exactly the way a larger system font is on a
        /// scaled display, which is the path the dialog takes in use. Changing the font of a dialog
        /// already built is a different path, and nothing takes it: KeePass never touches this
        /// dialog's font.
        /// </remarks>
        internal PairingApprovalForm(PairingRequest request, TimeSpan timeout, Font font)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (font != null)
            {
                Font = font;
            }

            _allowed = timeout;

            Name = GateTab.ApprovalFormName;
            Text = "KeePassGate: a client is asking to pair";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;

            // Sized by what is in it rather than by a number written here, which is what
            // `GateStatusForm` does and for the same reason. A fixed client area is right at one
            // display scaling and wrong at every other, and here it failed in the granting
            // direction: at 125% the profile list showed one row of what was asked for and at 150%
            // it showed none, while every profile stayed checked and Approve granted all of them.
            //
            // **No automatic scaling**, because there is nothing for it to do. What the content is
            // sized from is measured below in the font the dialog is built at, which on a scaled
            // display is already the larger one; `AutoScaleMode.Font` was set here with no
            // dimensions to scale from, so it did nothing at any display scaling, and acted only
            // when the font changed after the dialog was built, which nothing does.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // The prototype is disposed, because `SystemFonts.DefaultFont` hands back a new font
            // every time it is read and `new Font(prototype, style)` copies rather than adopts.
            using (var prototype = font == null ? SystemFonts.DefaultFont : new Font(font, font.Style))
            {
                _titleFont = new Font(prototype, FontStyle.Bold);
                _valueFont = new Font(prototype, FontStyle.Bold);
                _wrap = Wrap(prototype);
            }

            Padding = new Padding(Spacing.Window);

            // Three regions rather than one column. The list of profiles is the part that grows
            // with what is being asked, so it takes whatever the other two leave: a fixed height
            // for it means scrolling through four profiles while the dialog holds empty space
            // underneath. The countdown and the buttons stay at the bottom, where a reader looks
            // for them and where they cannot be pushed off by a long list.
            var layout = new FlowLayoutPanel
            {
                Name = "KpgApprovalLayout",
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.Controls.Add(new TextLabel
            {
                Name = "KpgApprovalTitle",
                Text = "A client is asking to pair with this database.",
                Font = _titleFont,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            });

            // A table rather than a paragraph. Every one of these is a value with a name, and the
            // operator is checking them against what they expect rather than reading them: a
            // database they know, a name they were told, an invitation they handed out. Bold on
            // the values, because those are the halves being compared.
            var details = new TableLayoutPanel
            {
                Name = GateTab.ApprovalDetailLabelName,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            };

            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var facts = Facts(request);
            details.RowCount = facts.Count;

            for (var at = 0; at < facts.Count; at++)
            {
                details.Controls.Add(Cell(facts[at].ControlName(false), facts[at].Name, false), 0, at);
                details.Controls.Add(Cell(facts[at].ControlName(true), facts[at].Value, true), 1, at);
            }

            layout.Controls.Add(details);

            layout.Controls.Add(new TextLabel
            {
                Name = "KpgApprovalConsequence",
                Text = Consequence,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            });

            // A gap under it, like everything else on this dialog and like the gap the footer keeps
            // above itself. The list is the one thing here an operator acts on, and less room
            // between it and the line introducing it than between it and the row after it would
            // make it look attached to the wrong one of the two.
            layout.Controls.Add(new TextLabel
            {
                Text = Asking(request),
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            });

            _profiles = new CheckedListBox
            {
                Name = GateTab.ApprovalProfilesListName,
                AccessibleName = GateTab.ApprovalProfilesListLabel,
                Width = _wrap,
                CheckOnClick = true,

                // The dialog's own, given before it is measured below: a list not yet on the dialog
                // has no font of the dialog's to inherit, and its row height is what that measures.
                Font = Font,

                // Off, so the height set below is the height it keeps rather than a whole number
                // of rows. With it on, the list rounds itself down and leaves a strip of the
                // dialog's background under the last row.
                IntegralHeight = false,
                Margin = new Padding(0)
            };

            foreach (var profile in request.Profiles)
            {
                _profiles.Items.Add(
                    Listed(profile, request.IsNew(profile), request.Returning(profile).Count > 0), true);
            }

            // Tall enough for what is being asked, up to a point, and never zero. This is the one
            // control the operator has to act on, so it is sized from the list it holds rather
            // than from whatever two docked bands leave over.
            _profiles.Height = ListHeight(_profiles, request.Profiles.Count);

            // Straight under the list, because it is about the list: a list longer than it shows
            // grants what is scrolled out of sight too.
            var scrolling = Scrolling(request);
            _scrolling = scrolling == null ? null : new TextLabel
            {
                Name = GateTab.ApprovalScrollingName,
                Text = scrolling,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, Spacing.Gap, 0, 0)
            };

            // Under the list rather than above it, because it is about what checking one of those
            // does, and the operator reads it with the check marks in front of them. Absent
            // unless a removed profile is being asked for, so the ordinary dialog is unchanged.
            var regaining = Regaining(request);
            _regaining = regaining == null ? null : new TextLabel
            {
                Name = GateTab.ApprovalRegainingName,
                Text = regaining,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, Spacing.Gap, 0, 0)
            };

            // A table rather than a flow, because the buttons go against the right edge and a flow
            // panel lays every child out at its own left one. A cell is what `Anchor` answers to.
            // The width is the width of everything else on the dialog, since a column that sized
            // itself to its widest child would put the right edge wherever the buttons happen to
            // end and right align them against themselves.
            // One row of two. What is left is a sentence and what is right is the pair of
            // buttons, and putting them on separate rows spent a line of the dialog on saying
            // nothing: the countdown is short, the buttons are narrow, and between them they do
            // not fill one width. The left column takes the slack so the right one ends where the
            // content does, which is what puts the buttons against the edge.
            var footer = new TableLayoutPanel
            {
                Name = "KpgApprovalFooter",
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                MinimumSize = new Size(_wrap, 0),

                // A gap above, which is what separates this row from the list it answers
                // about. Nothing below: the form's own padding closes the dialog.
                Margin = new Padding(0, Spacing.Gap, 0, 0)
            };

            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _countdown = new TextLabel
            {
                Name = GateTab.ApprovalCountdownName,
                Text = Remaining(timeout),
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0),

                // Left, and nothing else: a control anchored to one side of a cell is centered on
                // the other axis, which is what levels this line with the buttons beside it rather
                // than with their top edge.
                Anchor = AnchorStyles.Left
            };

            footer.Controls.Add(_countdown, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgApprovalButtonRow",
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),

                // Right, and nothing else, for the same two reasons the countdown is anchored
                // left: it decides which end of the cell they sit at, and it levels them with the
                // line beside them.
                Anchor = AnchorStyles.Right
            };

            // Both margins are written down, and the vertical halves have to match. A flow panel
            // puts each control at the top of the row plus that control's own top margin, so a
            // button left with the default margin sits a few pixels below one given none, and two
            // buttons of the same size on the same row read as a mistake nobody can name.
            var refuse = new Button
            {
                Name = GateTab.RefuseButtonName,
                Text = "&" + GateTab.RefuseButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            };

            var approve = new Button
            {
                Name = GateTab.ApproveButtonName,
                Text = "&" + GateTab.ApproveButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 0)
            };

            refuse.Click += delegate { Refuse(); };
            approve.Click += delegate { Approve(); };

            buttons.Controls.Add(refuse);
            buttons.Controls.Add(approve);
            footer.Controls.Add(buttons, 1, 0);

            // One column, in reading order. It used to be three docked bands with the list
            // filling what the other two left, which is how the list came to be the part that
            // disappeared: the bands grow with the font and the client area did not.
            layout.Controls.Add(_profiles);
            if (_scrolling != null)
            {
                layout.Controls.Add(_scrolling);
            }

            if (_regaining != null)
            {
                layout.Controls.Add(_regaining);
            }

            layout.Controls.Add(footer);

            // Filled rather than dropped in at the origin. A form lays a child out against its
            // display rectangle only when the child is docked, so one that is not sits at the
            // client area's own corner and the twelve pixels above are spent on nothing: the
            // title and the list touched the window's left edge.
            layout.Dock = DockStyle.Fill;
            Controls.Add(layout);

            // Every way a dialog gets dismissed without being read leads to a refusal, and none of
            // them leads to a certificate. These two are the keys; the close box and everything
            // else that reaches `Close` without a button are `OnFormClosing` below.
            CancelButton = refuse;
            AcceptButton = refuse;

            _clock = new Timer { Interval = 1000 };
            _clock.Tick += OnTick;
            _clock.Start();
        }

        /// <summary>
        /// How wide a line of this dialog's text is allowed to be, measured in this font.
        /// </summary>
        /// <remarks>
        /// Fifty-six characters is the measurement, because that is a decision about reading
        /// rather than about pixels: it holds at any font size and any display scaling, where a
        /// number of pixels holds at one. The widest character the font has is what it is measured
        /// in, so this is generous rather than tight, which is the safe direction for a dialog
        /// that must not clip.
        ///
        /// **Reading alone would ask for seventy, and the countdown is what rules it out.**
        /// That line and the two buttons share the last row, and at seventy they sit a hundred
        /// and fifty pixels apart with nothing between them: a dialog wide enough that the two
        /// things on its final row read as belonging to different windows. Fifty-six leaves
        /// about thirty, which is a gap rather than a gulf. The labels above wrap a line or so
        /// more for it, which is the trade and is the cheaper half: they are read once and the
        /// row is what gets acted on.
        /// </remarks>
        private static int Wrap(Font font)
        {
            return TextRenderer.MeasureText(new string('M', 56), font).Width;
        }

        /// <summary>
        /// How tall the profile list should be for this many profiles.
        /// </summary>
        /// <remarks>
        /// Between two rows and <see cref="MostRows"/>. Never fewer than two, because a list
        /// showing one row of two profiles reads as a list of one and the operator is then
        /// approving what they cannot see; never more than that, because past it the dialog is
        /// taller than the answer is worth and the list scrolls, which it can, and the line under
        /// it says so. The border is added because a list box's height includes it and its rows
        /// do not.
        /// </remarks>
        private static int ListHeight(CheckedListBox list, int profiles)
        {
            var rows = Math.Max(2, Math.Min(MostRows, profiles));
            return (list.ItemHeight * rows) + (SystemInformation.BorderSize.Height * 4);
        }

        /// <summary>The most rows the profile list shows at once, past which it scrolls.</summary>
        private const int MostRows = 10;

        /// <summary>
        /// Approves it, exactly as the button does. Public because a button press needs a window on
        /// the screen and a test of what the button means does not.
        /// </summary>
        public void Approve()
        {
            Finish(PairingDecision.Approved);
        }

        /// <summary>Refuses it, exactly as the button does.</summary>
        public void Refuse()
        {
            Finish(PairingDecision.Refused);
        }

        /// <summary>
        /// What the operator decided. A dialog dismissed rather than answered has refused, and one
        /// nobody touches at all has timed out.
        /// </summary>
        public PairingApproval Answer()
        {
            switch (_decision)
            {
                case PairingDecision.Approved:
                    return PairingApproval.Approved(Checked());

                case PairingDecision.Refused:
                    return PairingApproval.Refused();

                default:
                    return PairingApproval.TimedOut();
            }
        }

        /// <summary>The lines of the request, so the wording can be tested without a screen.</summary>
        public static IReadOnlyList<Fact> Facts(PairingRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var facts = new List<Fact>();

            // The name first, because that is what the operator knows this database as. The
            // fingerprint stays under it: it is what actually identifies the database, and two
            // databases can carry the same name.
            if (!string.IsNullOrEmpty(request.DatabaseName))
            {
                facts.Add(new Fact("Database", request.DatabaseName));
                facts.Add(new Fact("Fingerprint", CertificateDigest.Shorten(request.DatabaseId)));
            }
            else
            {
                facts.Add(new Fact("Database", CertificateDigest.Shorten(request.DatabaseId)));
            }

            facts.Add(new Fact("Asking to be called", request.Name));

            // Captioned as an identifier rather than as the invitation, because the clients page
            // shows the same identifier beside the code and this is what it is checked against. A
            // row saying "Invitation" reads as though it were the code, which is the one thing this
            // dialog does not show.
            facts.Add(new Fact(GateTab.InvitationIdFactName, request.InvitationId));

            if (!string.IsNullOrEmpty(request.Peer))
            {
                facts.Add(new Fact(GateTab.PeerFactName, request.Peer));
            }

            return facts;
        }

        private Label Cell(string name, string text, bool value)
        {
            return new TextLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Font = value ? _valueFont : Font,
                MaximumSize = new Size(_wrap * 2 / 3, 0),
                Margin = value ? new Padding(0) : new Padding(0, 0, Spacing.Gap, 0)
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_clock != null)
                {
                    _clock.Stop();
                    _clock.Dispose();
                }

                if (_valueFont != null)
                {
                    _valueFont.Dispose();
                }

                if (_titleFont != null)
                {
                    _titleFont.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private static string Remaining(TimeSpan left)
        {
            var seconds = (int)Math.Max(0, Math.Ceiling(left.TotalSeconds));

            return string.Format(
                CultureInfo.InvariantCulture, GateTab.ApprovalCountdownFormat, seconds);
        }

        /// <summary>
        /// What is still checked, by name, which is what the operator approved.
        /// </summary>
        /// <remarks>
        /// The names are what went into the list, and the list is built from the request after the
        /// handler has collapsed anything repeated in it. Two identical rows would be two rows the
        /// operator has to clear to refuse one profile, and clearing one of them would grant it.
        /// </remarks>
        private string[] Checked()
        {
            var approved = new List<string>();
            foreach (var item in _profiles.CheckedItems)
            {
                approved.Add(Asked(Convert.ToString(item, CultureInfo.InvariantCulture)));
            }

            return approved.ToArray();
        }

        private void OnTick(object sender, EventArgs e)
        {
            var left = _allowed - _elapsed.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                Finish(PairingDecision.TimedOut);
                return;
            }

            _countdown.Text = Remaining(left);
        }

        /// <summary>
        /// A dialog dismissed without an answer has refused, whichever way it was dismissed.
        /// </summary>
        /// <remarks>
        /// The close box, Alt and F4, the window menu and the taskbar all reach `Close` without
        /// pressing either button, so none of them runs `Refuse`, and what a dialog that was never
        /// answered holds is a timeout. That answer is wrong in both directions at once: the
        /// operator who dismissed the request believes it was turned down, while the invitee is
        /// told nobody was at the screen and keeps an invitation it can present again, raising this
        /// dialog as often as it likes until the token runs out. Somebody was at the screen, and
        /// the default here is denial, so a dismissal is a refusal and spends the invitation.
        ///
        /// **`CloseReason` cannot tell those apart**, which is the trap in doing this the obvious
        /// way: `Form.Close` reports `UserClosing` whoever called it, so the countdown's own close
        /// arrives looking exactly like a press on the title bar and would spend the invitation of
        /// an operator who was never there. What separates them is whether an answer has already
        /// been given, which is what `Decide` records.
        /// </remarks>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (e.Cancel)
            {
                // The dialog is staying open, so nothing has been decided and the operator can
                // still answer it.
                return;
            }

            Decide(PairingDecision.Refused);
        }

        /// <summary>
        /// Records an answer, unless one has already been recorded.
        /// </summary>
        /// <remarks>
        /// The first answer wins, because the ways this dialog can end overlap. Every press ends in
        /// a close, which arrives at `OnFormClosing` immediately afterwards, and the countdown can
        /// expire while a button is going down. A later answer overwriting an earlier one would let
        /// a close take back the press that caused it.
        ///
        /// Stopping the clock belongs here for the same reason: `KillTimer` does not withdraw a
        /// tick already sitting in the queue, so a decision has to survive one arriving after it.
        /// </remarks>
        private void Decide(PairingDecision decision)
        {
            if (_decided)
            {
                return;
            }

            _decided = true;
            _decision = decision;
            _clock.Stop();
        }

        private void Finish(PairingDecision decision)
        {
            Decide(decision);

            // Read back rather than taken from the argument, so that a press arriving after the
            // clock has answered closes on the answer that was kept rather than on its own.
            DialogResult = _decision == PairingDecision.Approved ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }
}
