using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The dialog that asks whether an admitted client may have a new certificate.
    /// </summary>
    /// <remarks>
    /// Modeled on <see cref="PairingApprovalForm"/>, whose layout, measurements, countdown and
    /// grace it shares, and which says why each of them is what it is. What differs is what a
    /// renewal is.
    ///
    /// - **Nothing is approved by default.** It comes up without taking the focus, Enter and
    ///   Escape refuse, and Approve has no mnemonic and is never the default button, as on the
    ///   pairing dialog: <see cref="ApprovalDialog"/> says why. Approving takes a press of its own.
    /// - **The profiles can be narrowed and never widened.** They are a row each for the profiles
    ///   the client's record holds, in the pairing dialog's table: a profile the database still
    ///   names checked, and a profile it no longer names with a red X in place of a box and its
    ///   name in gray, the tooltip of both stating that the new certificate omits the profile.
    /// - **Refuse is a split button**, because a refusal can answer every later renewal by the same
    ///   client as well, whichever certificate of the client asks. Pressing it refuses once, which
    ///   keeps nothing. Its arrow, or Alt and Down while it has the focus, opens a menu of what
    ///   else refusing can mean: once, in this session, until a date, or forever.
    /// - **A standing answer only ever comes from that menu.** Enter, Escape, the close box, Alt
    ///   and F4, the window menu and the taskbar all refuse once: each is somebody making the window
    ///   go away, and none of them is a choice about later renewals.
    /// - **It closes itself**, and an unanswered renewal changes nothing, so the client may ask
    ///   again.
    /// </remarks>
    public sealed class RenewalApprovalForm : ApprovalDialog
    {
        /// <summary>The window's title.</summary>
        public const string WindowTitle = GateName.Product + ": a client is asking to renew a certificate";

        /// <summary>The line at the top of the dialog.</summary>
        public const string Title = "A client is asking to renew a certificate.";

        /// <summary>What approving gives and what it leaves, stated once.</summary>
        /// <remarks>
        /// What happens to the client's current certificate, which is what the operator acts
        /// on: unchecking a profile narrows the new certificate and nothing else.
        /// </remarks>
        public const string Consequence =
            "Approve issues a new certificate. The current certificate keeps every profile until the " +
            "client gives that certificate up.";

        /// <summary>Which answers save this database, stated under <see cref="Consequence"/>.</summary>
        /// <remarks>
        /// Visible rather than in a tooltip, since a save is what decides whether the operator's
        /// answer outlasts KeePass: Approve's record and a refusal that stands are written and
        /// saved before the client is answered, and the two other ways to refuse keep nothing in
        /// the database. How the save is ordered against the answer is `PAIRING.md`'s.
        /// </remarks>
        public const string Saves = "Approve, Refuse until a date and Refuse forever save this database.";

        /// <summary>The control that shows <see cref="Saves"/>.</summary>
        public const string SavesName = "KpgRenewalSaves";

        /// <summary>
        /// What the table underneath is: every profile the client's record holds, a profile the new
        /// certificate omits with an X in place of a box, <see cref="OmitsTip"/>.
        /// </summary>
        public const string ProfilesHeld = ProfilesHeldName + ":";

        /// <summary>
        /// The table's name, which UI automation reads as the table's: the line above the table,
        /// <see cref="ProfilesHeld"/>, without its colon.
        /// </summary>
        public const string ProfilesHeldName = "Profiles the client holds";

        /// <summary>
        /// The notice above <see cref="ProfilesHeld"/> where the record holds no profile the
        /// database still names.
        /// </summary>
        public const string NoProfilesHeld = "The new certificate reaches no profile.";

        /// <summary>The name of the notice <see cref="NoProfilesHeld"/>.</summary>
        internal const string NoticeName = "KpgRenewalNotice";

        /// <summary>What UI automation reads as the whole of the dialog's content.</summary>
        internal const string LayoutRead = "Renewal request";

        /// <summary>
        /// The tooltip of the X in place of a box on the row of a profile the database no longer
        /// names, and of the profile's name: the new certificate goes without the profile.
        /// </summary>
        /// <remarks>
        /// A name the database took off its list grants nothing while it stays off, and a renewed
        /// record is written from the names that are on it, so the name does not travel to the new
        /// certificate. **A row of its own with an X in place of a box**, the name in gray, so that
        /// every profile the record holds is a row, and what is not carried over stands beside the
        /// names that are, reading as not to be granted, rather than in a line under them or in a
        /// column of its own that every renewal of profiles on the list leaves empty.
        /// </remarks>
        public const string OmitsTip =
            "This database no longer names the profile, so the new certificate omits the profile. " +
            "Approve restores no profile.";

        /// <summary>The lines of the request, so the wording can be tested without a screen.</summary>
        public static IReadOnlyList<Fact> Facts(RenewalRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var facts = new List<Fact>();

            if (!string.IsNullOrEmpty(request.DatabaseName))
            {
                facts.Add(new Fact("Database", request.DatabaseName));
                facts.Add(new Fact("Fingerprint", CertificateDigest.Shorten(request.DatabaseId)));
            }
            else
            {
                facts.Add(new Fact("Database", CertificateDigest.Shorten(request.DatabaseId)));
            }

            facts.Add(new Fact("Client", request.Name));
            facts.Add(new Fact(GateTab.RenewalAdmittedFactName, Moments.OnScreen(request.Admitted)));
            facts.Add(new Fact(GateTab.RenewalExpiresFactName, Moments.OnScreen(request.Expires)));

            if (!string.IsNullOrEmpty(request.Peer))
            {
                facts.Add(new Fact(GateTab.PeerFactName, request.Peer));
            }

            return facts;
        }

        private readonly ProfileTable _profiles;
        private readonly LifetimeChoice _lifetime;
        private readonly SplitButton _refuse;
        private readonly ToolStripMenuItem _until;
        private readonly Label _countdown;
        private readonly Timer _clock;
        private readonly Font _titleFont;
        private readonly Font _valueFont;
        private readonly int _wrap;

        /// <summary>Today where the operator is, read each time it is asked.</summary>
        private readonly Func<DateTime> _today;

        /// <summary>
        /// The latest day a refusal until a date can name: the day the certificate the client holds
        /// now ends, where the operator is.
        /// </summary>
        private readonly DateTime _lastDay;

        /// <summary>How long the operator gets, counted the way the pairing dialog counts it.</summary>
        private readonly TimeSpan _allowed;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();

        private PairingDecision _decision = PairingDecision.TimedOut;
        private RenewalRefusal _standing;
        private bool _decided;

        /// <summary>Whether a close of the database it is about withdrew it, rather than a lock.</summary>
        private bool _closedWithDatabase;

        /// <summary>The dialog asking for a day, while it is open.</summary>
        private Form _askingForADay;

        /// <summary>Whether the press being delivered is the one that put the Refuse menu away.</summary>
        private bool _closingTheMenu;

        public RenewalApprovalForm(RenewalRequest request, TimeSpan timeout, IGateLog log)
            : this(request, timeout, null, () => DateTime.Today, log)
        {
        }

        /// <summary>
        /// The same, built at a font other than the system's and on a day other than today, which
        /// is how a test stands in for a scaled display and for the calendar.
        /// </summary>
        internal RenewalApprovalForm(RenewalRequest request, TimeSpan timeout, Font font, DateTime today, IGateLog log)
            : this(request, timeout, font, () => today, log)
        {
        }

        /// <summary>
        /// The same, with a clock for the day, read whenever the first day a refusal can name is
        /// asked for, which is how a test stands in for midnight passing while the dialog is up.
        /// </summary>
        internal RenewalApprovalForm(
            RenewalRequest request, TimeSpan timeout, Font font, Func<DateTime> today, IGateLog log)
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

            Name = GateTab.RenewalFormName;
            Text = WindowTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;

            // Sized by what is in it, measured in the font it is built at, for the reasons the
            // pairing dialog gives.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            using (var prototype = font == null ? SystemFonts.DefaultFont : new Font(font, font.Style))
            {
                _titleFont = new Font(prototype, FontStyle.Bold);
                _valueFont = new Font(prototype, FontStyle.Bold);
                _wrap = PairingApprovalForm.Wrap(prototype);
            }

            Padding = new Padding(LogicalToDeviceUnits(Spacing.Window));

            var layout = new FlowLayoutPanel
            {
                Name = "KpgRenewalLayout",
                AccessibleName = LayoutRead,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.Controls.Add(Line("KpgRenewalTitle", Title, _titleFont));

            var details = new TableLayoutPanel
            {
                Name = "KpgRenewalDetail",
                AccessibleName = DetailsRead,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
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
            layout.Controls.Add(Line("KpgRenewalConsequence", Consequence, Font));
            layout.Controls.Add(Line(SavesName, Saves, Font));

            // Where the record holds no profile this database still names, a notice above the
            // line naming the table, in bold, as the pairing dialog shows its notice: the line
            // stays, since the table is still the profiles the client holds, and Approve issues a
            // certificate with no profile, which is what the notice states.
            if (request.Profiles.Count == 0)
            {
                layout.Controls.Add(Line(NoticeName, NoProfilesHeld, _valueFont));
            }

            layout.Controls.Add(Line("KpgRenewalProfilesLine", ProfilesHeld, Font));

            // The pairing dialog's table, a row for every profile the client's record holds: a
            // profile this database names with its box, checked, and a profile it no longer names
            // with an X in place of a box and its name in gray, since the new certificate goes
            // without it whatever is checked. As wide as a line of the dialog's text, a name too
            // long for its column cut with an ellipsis and shown whole in its tooltip.
            _profiles = new ProfileTable(
                new ProfileColumn(GateTab.RenewalGrantColumnName, PairingApprovalForm.GrantHeading),
                new[] { new ProfileColumn(GateTab.RenewalProfileColumnName, PairingApprovalForm.ProfileHeading) },
                PairingApprovalForm.MostRows)
            {
                Name = GateTab.RenewalProfilesListName,

                // The line above the table names the table, without its colon.
                AccessibleName = ProfilesHeldName,
                Font = Font,
                Width = _wrap,
                Margin = new Padding(0)
            };

            foreach (var profile in request.Profiles)
            {
                _profiles.Add(profile, true, true);
                _profiles.Show(profile, new[] { profile }, Array.Empty<string>(), profile);
            }

            // A row with an X in place of a box is read out as the row itself: the name, then the
            // sentence the X stands for, which is the tooltip of the X and of the name, so that the
            // tooltip is not the only place the sentence stands. The X is drawn on the row and is
            // nothing of its own to UI automation, so the sentence is read once.
            foreach (var profile in request.Dropped)
            {
                _profiles.Add(profile, false, false);
                _profiles.Show(profile, new[] { profile }, Array.Empty<string>(), profile + ". " + OmitsTip);
                _profiles.Omit(profile, OmitsTip);
            }

            // Sized from the rows, as the pairing dialog's table is as it comes up, a row cut in
            // half at its bottom where the rows are taller than it shows: nothing on this dialog
            // changes the rows while it stands.
            _profiles.Lay(true);
            layout.Controls.Add(_profiles);

            _lifetime = new LifetimeChoice(request.Lifetime, Font);
            layout.Controls.Add(_lifetime);

            // From tomorrow to the day the certificate it holds now ends, because a day before that
            // asks nothing of anybody and this certificate cannot ask on a day after it. Tomorrow
            // is asked of the clock each time, since the dialog can be answered after midnight.
            //
            // **Both ends where the operator is**, since the refusal begins at the start of the day
            // chosen there. The last day used to be the date the row above prints in UTC, against
            // a local tomorrow: far east of Greenwich the choice was grayed out a local day early,
            // and far west it offered a day after the certificate's local end, whose refusal began
            // after the certificate had stopped. The row names its zone, so a last day a date away
            // from it near midnight is read as what it is.
            _today = today ?? (() => DateTime.Today);
            _lastDay = TimeZoneInfo.ConvertTime(request.Expires, TimeZoneInfo.Local).Date;

            var footer = new TableLayoutPanel
            {
                Name = "KpgRenewalFooter",
                AccessibleName = AnswerRead,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,

                // As wide as the list, so that the buttons end where it does.
                MinimumSize = new Size(_profiles.Width, 0),
                Margin = new Padding(0, LogicalToDeviceUnits(Spacing.Gap), 0, 0)
            };

            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _countdown = new TextLabel
            {
                Name = GateTab.RenewalCountdownName,
                Text = Remaining(timeout),
                Font = Font,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Left
            };

            // Measured once, at its widest, and held at that size, so that its words changing
            // every second lay nothing out, `CountdownSize`.
            _countdown.Size = CountdownSize(_countdown, GateTab.RenewalCountdownFormat, timeout, _wrap);

            footer.Controls.Add(_countdown, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgRenewalButtonRow",
                AccessibleName = ButtonsRead,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Right
            };

            _refuse = new SplitButton(log)
            {
                Name = GateTab.RefuseButtonName,
                Text = "&" + GateTab.RefuseButtonText,
                AccessibleDescription = GateTab.RefuseButtonDescription,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };

            _refuse.Menu.Name = GateTab.RefuseMenuName;
            _refuse.Menu.AccessibleName = GateTab.RefuseMenuLabel;
            _refuse.Menu.Font = Font;
            _refuse.Menu.Items.Add(Choice(GateTab.RefuseOnceName, GateTab.RefuseOnceText));
            _refuse.Menu.Items.Add(Choice(GateTab.RefuseThisSessionName, GateTab.RefuseThisSessionText));
            _until = Choice(GateTab.RefuseUntilName, GateTab.RefuseUntilText);
            _refuse.Menu.Items.Add(_until);
            _refuse.Menu.Items.Add(Choice(GateTab.RefuseForeverName, GateTab.RefuseForeverText));

            // A certificate ending before tomorrow leaves no day to name.
            ShowUntil();

            var approve = new ApproveButton
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            // A press that puts the Refuse menu away is the end of the menu, as it is on the arrow,
            // and never the start of an approval: marked as the menu closes, which is before the
            // press reaches anything, and cleared once it has.
            _refuse.Menu.Closing += (sender, e) =>
            {
                WindowHandlers.Run("the renewal dialog's Refuse menu could not be closed", () =>
                {
                    if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && IsHandleCreated)
                    {
                        _closingTheMenu = true;
                        BeginInvoke((Action)(() => _closingTheMenu = false));
                    }
                });
            };

            approve.ClosingAMenu = () => _closingTheMenu;

            _refuse.Click += delegate
            {
                WindowHandlers.Run("Refuse on the renewal dialog could not be carried out", Refuse);
            };
            _refuse.ItemChosen += (sender, e) =>
                WindowHandlers.Run("a choice from the Refuse menu could not be carried out",
                    () => Chosen(e.ClickedItem));
            approve.Click += delegate
            {
                WindowHandlers.Run("Approve on the renewal dialog could not be carried out", Approve);
            };

            buttons.Controls.Add(_refuse);
            buttons.Controls.Add(approve);
            footer.Controls.Add(buttons, 1, 0);
            layout.Controls.Add(footer);

            layout.Dock = DockStyle.Fill;
            Controls.Add(layout);

            // Both keys refuse, as on the pairing dialog, and refuse once. What else refusing can
            // mean is on the menu and nowhere else, so no key and no dismissal can give an answer
            // that outlasts this request. `RefuseByKey` is what answers them; these two are what
            // draws Refuse as the button they reach.
            CancelButton = _refuse;
            AcceptButton = _refuse;

            AskForADay = asking => asking.ShowDialog(this);

            _clock = new Timer { Interval = 1000 };
            _clock.Tick += (sender, e) =>
                WindowHandlers.Run("the renewal dialog's countdown could not be moved on", () => OnTick(sender, e));
            _clock.Start();
        }

        /// <summary>
        /// Whether a refusal until a date can be given: not when the certificate the client holds
        /// now ends before tomorrow.
        /// </summary>
        public bool CanRefuseUntil
        {
            get { return FirstDay <= _lastDay; }
        }

        /// <summary>The earliest day a refusal until a date can name: tomorrow, by the clock now.</summary>
        private DateTime FirstDay
        {
            get { return _today().Date.AddDays(1); }
        }

        /// <summary>
        /// The moment a day begins in a zone, which on a day whose clocks fall back at midnight is
        /// the first of its two midnights.
        /// </summary>
        /// <remarks>
        /// `new DateTimeOffset(DateTime)` resolves an ambiguous local time with the standard
        /// offset, which is the second midnight: a refusal "until that day begins" then lasted an
        /// hour into it. A midnight that does not exist, where clocks spring forward, is the moment
        /// of the jump, which is midnight at the offset before it.
        /// </remarks>
        internal static DateTimeOffset StartOf(DateTime day, TimeZoneInfo zone)
        {
            var midnight = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
            if (zone.IsAmbiguousTime(midnight))
            {
                var offsets = zone.GetAmbiguousTimeOffsets(midnight);
                return new DateTimeOffset(midnight, offsets.Max());
            }

            if (zone.IsInvalidTime(midnight))
            {
                return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight.AddMinutes(-1)));
            }

            return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
        }

        /// <summary>
        /// Shows the dialog asking for a day and answers how it was closed.
        /// </summary>
        /// <remarks>
        /// Settable because a test cannot answer a modal dialog: it stands in for the operator
        /// choosing a day, or going back, the way the prompt's own dialog seam does.
        /// </remarks>
        internal Func<RefuseUntilForm, DialogResult> AskForADay { get; set; }

        /// <summary>
        /// Lays the table again once it has a window, before the dialog is centered and shown, as
        /// the pairing dialog does and for its reason: the boxes are measured there in the style the
        /// window draws them in.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            WindowHandlers.Run("the renewal dialog could not be fitted to its table", () =>
            {
                _profiles.Lay(true);
                base.OnLoad(e);
            });
        }

        /// <summary>Approves it, exactly as the button does.</summary>
        public void Approve()
        {
            Finish(PairingDecision.Approved, null);
        }

        /// <summary>
        /// Refuses it once, exactly as pressing Refuse does, keeping nothing for later renewals.
        /// </summary>
        public void Refuse()
        {
            Finish(PairingDecision.Refused, null);
        }

        /// <summary>Enter and Escape refuse once, as pressing Refuse does.</summary>
        protected override void RefuseByKey()
        {
            Refuse();
        }

        internal override bool Withdraw(bool databaseClosed)
        {
            var unanswered = !_decided;
            if (unanswered)
            {
                _closedWithDatabase = databaseClosed;
            }

            Finish(PairingDecision.Locked, null);
            return unanswered;
        }

        /// <summary>Refuses it and every later renewal by this client until KeePass exits.</summary>
        public void RefuseThisSession()
        {
            Finish(PairingDecision.Refused, RenewalRefusal.ThisSession());
        }

        /// <summary>
        /// Refuses it and every later renewal by this client until a day begins where the
        /// operator is, as the dialog asking for a day does once the day is chosen.
        /// </summary>
        public void RefuseUntil(DateTime day)
        {
            if (!Nameable(day))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(day), day, "A refusal until a date names a day from tomorrow to the day the certificate ends.");
            }

            RefusedUntil(day);
        }

        /// <summary>Refuses until a day already found nameable, without asking the clock again.</summary>
        private void RefusedUntil(DateTime day)
        {
            Finish(PairingDecision.Refused, RenewalRefusal.Until(StartOf(day, TimeZoneInfo.Local)));
        }

        /// <summary>Refuses it and every later renewal by this client.</summary>
        public void RefuseForever()
        {
            Finish(PairingDecision.Refused, RenewalRefusal.Forever());
        }

        /// <summary>
        /// Asks for a day and refuses until it, as choosing "Refuse until a date..." does. Going
        /// back from that question decides nothing, and this dialog stays where it was.
        /// </summary>
        public void AskToRefuseUntil()
        {
            // Tomorrow read once, so that the check and the days the question offers agree. Read
            // twice, a midnight between the two on the eve of the certificate's last day passed
            // the check with the first tomorrow and handed the question a first day after its last,
            // which threw into the menu's handler and took the operator's choice with it.
            var first = FirstDay;
            if (first > _lastDay || _decided)
            {
                return;
            }

            var week = first.AddDays(6);

            using (var asking = new RefuseUntilForm(first, _lastDay, week <= _lastDay ? week : _lastDay, Font))
            {
                _askingForADay = asking;

                DialogResult answer;
                try
                {
                    answer = AskForADay(asking);
                }
                finally
                {
                    _askingForADay = null;
                }

                // Asked again, because the countdown can run out while the question is open and
                // has already answered for an operator who was still choosing.
                // codeql[cs/constant-condition] the countdown can decide while the question's modal loop runs
                if (answer != DialogResult.OK || _decided)
                {
                    return;
                }

                // And the day itself, because midnight can pass while the question is open: a day
                // that was tomorrow when it was chosen is today when it is confirmed, which names
                // nothing. Put again with the days there are now, or not at all when the last of
                // them has gone, rather than thrown into a menu's handler that would only log it.
                if (Nameable(asking.Day))
                {
                    // Not through `RefuseUntil`, which asks the clock again: a midnight between
                    // the two would throw for the day just found nameable.
                    RefusedUntil(asking.Day);
                    return;
                }
            }

            AskToRefuseUntil();
        }

        /// <summary>Whether a day can still be named: from tomorrow to the certificate's last day.</summary>
        /// <remarks>Tomorrow read once, for the reason `AskToRefuseUntil` reads it once.</remarks>
        private bool Nameable(DateTime day)
        {
            var first = FirstDay;
            return day.Date >= first && day.Date <= _lastDay;
        }

        /// <summary>
        /// What the operator decided. A dialog dismissed rather than answered has refused once,
        /// and one nobody touches at all has timed out.
        /// </summary>
        public RenewalApproval Answer()
        {
            switch (_decision)
            {
                case PairingDecision.Approved:
                    return RenewalApproval.Approved(Checked(), _lifetime.Chosen);

                case PairingDecision.Refused:
                    return RenewalApproval.Refused(_standing);

                case PairingDecision.Locked:
                    return _closedWithDatabase ? RenewalApproval.Closed() : RenewalApproval.Locked();

                default:
                    return RenewalApproval.TimedOut();
            }
        }

        /// <summary>
        /// A dialog dismissed without an answer has refused once, whichever way it went.
        /// </summary>
        /// <remarks>
        /// As on the pairing dialog, and decided the same way: by whether an answer has already
        /// been given, since `CloseReason` cannot tell the countdown's own close from a press on
        /// the close box. A dismissal keeps nothing, so the client may ask again.
        /// </remarks>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            WindowHandlers.Run("the renewal dialog's close could not be handled", () =>
            {
                base.OnFormClosing(e);

                if (e.Cancel)
                {
                    return;
                }

                Decide(PairingDecision.Refused, null);
                Abandon();
            });
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

        private static ToolStripMenuItem Choice(string name, string text)
        {
            return new ToolStripMenuItem(text) { Name = name };
        }

        /// <summary>What a choice from the Refuse button's menu does.</summary>
        private void Chosen(ToolStripItem item)
        {
            if (item == null)
            {
                return;
            }

            switch (item.Name)
            {
                case GateTab.RefuseOnceName:
                    Refuse();
                    break;

                case GateTab.RefuseThisSessionName:
                    RefuseThisSession();
                    break;

                case GateTab.RefuseUntilName:
                    AskToRefuseUntil();
                    break;

                case GateTab.RefuseForeverName:
                    RefuseForever();
                    break;
            }
        }

        /// <summary>
        /// Closes the dialog asking for a day, when one is open over this, as this one ends.
        /// </summary>
        /// <remarks>
        /// It is modal over this one, so whatever ends this one, the countdown or the window being
        /// closed from the taskbar, has to end it too. Its answer is then ignored, since this one
        /// has already been given.
        /// </remarks>
        private void Abandon()
        {
            var asking = _askingForADay;
            if (asking != null && !asking.IsDisposed)
            {
                asking.DialogResult = DialogResult.Cancel;
            }
        }

        /// <summary>
        /// The profiles checked, by name: only rows of a profile on the list have a box, so a
        /// profile the database no longer names is never among them.
        /// </summary>
        private string[] Checked()
        {
            return _profiles.Checked.ToArray();
        }

        private Label Line(string name, string text, Font font)
        {
            return new TextLabel
            {
                Name = name,
                Text = text,
                Font = font,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            };
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
                Margin = value ? new Padding(0) : new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };
        }

        private static string Remaining(TimeSpan left)
        {
            var seconds = (int)Math.Max(0, Math.Ceiling(left.TotalSeconds));
            return string.Format(CultureInfo.InvariantCulture, GateTab.RenewalCountdownFormat, seconds);
        }

        private void OnTick(object sender, EventArgs e)
        {
            var left = _allowed - _elapsed.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                Finish(PairingDecision.TimedOut, null);
                return;
            }

            _countdown.Text = Remaining(left);

            // Asked again every second, so a certificate whose last day has become today since
            // the dialog was built stops offering a day that has begun.
            ShowUntil();
        }

        /// <summary>
        /// Offers a day while there is one to name, and says why not when there is none.
        /// </summary>
        private void ShowUntil()
        {
            var can = CanRefuseUntil;
            _until.Enabled = can;
            _until.Text = can ? GateTab.RefuseUntilText : GateTab.RefuseUntilEndingText;
        }

        /// <summary>Records an answer, unless one has already been recorded.</summary>
        private void Decide(PairingDecision decision, RenewalRefusal standing)
        {
            if (_decided)
            {
                return;
            }

            _decided = true;
            _decision = decision;
            _standing = standing;
            _clock.Stop();
        }

        private void Finish(PairingDecision decision, RenewalRefusal standing)
        {
            Decide(decision, standing);
            Abandon();
            DialogResult = _decision == PairingDecision.Approved ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }
}
