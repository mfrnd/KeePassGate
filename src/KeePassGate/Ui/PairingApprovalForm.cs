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
    /// - **Every string on it is what the request said**, and the request came from outside. Names
    ///   and profiles have already been checked to be plain names by the time they reach here.
    /// </remarks>
    public sealed class PairingApprovalForm : Form
    {
        private readonly CheckedListBox _profiles;
        private readonly Label _countdown;
        private readonly Timer _clock;
        /// <summary>
        /// What approving costs, said once and in full. Separate from the table above it because
        /// this is a sentence to read rather than a value to check.
        /// </summary>
        public const string Consequence =
            "Approving issues it a certificate and saves this database, so the admission is on " +
            "disk before the client is told anything. It can be revoked later by recreating " +
            "the authority on the " + GateName.OnATab + " tab, which revokes every client.";

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
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();

        private PairingDecision _decision = PairingDecision.TimedOut;

        public PairingApprovalForm(PairingRequest request, TimeSpan timeout)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            _allowed = timeout;

            Name = GateTab.ApprovalFormName;
            AccessibleName = GateTab.ApprovalFormName;
            Text = "KeePassGate: a client is asking to pair";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(460, 340);

            _titleFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            _valueFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

            Padding = new Padding(Spacing.Window);

            // Three regions rather than one column. The list of profiles is the part that grows
            // with what is being asked, so it takes whatever the other two leave: a fixed height
            // for it means scrolling through four profiles while the dialog holds empty space
            // underneath. The countdown and the buttons stay at the bottom, where a reader looks
            // for them and where they cannot be pushed off by a long list.
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.Controls.Add(new TextLabel
            {
                Text = "A client is asking to pair with this database.",
                Font = _titleFont,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
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
                details.Controls.Add(Cell(facts[at].Name, false), 0, at);
                details.Controls.Add(Cell(facts[at].Value, true), 1, at);
            }

            layout.Controls.Add(details);

            layout.Controls.Add(new TextLabel
            {
                Text = Consequence,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            });

            layout.Controls.Add(new TextLabel
            {
                Text = request.Profiles.Count == 0 ? NoProfilesAsked : ProfilesAsked,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            });

            _profiles = new CheckedListBox
            {
                Name = GateTab.ApprovalProfilesListName,
                AccessibleName = GateTab.ApprovalProfilesListName,
                Dock = DockStyle.Fill,
                CheckOnClick = true,

                // Off, so the list is the height it is given rather than a whole number of rows.
                // With it on, a docked list rounds itself down and leaves a strip of the dialog's
                // background under the last row.
                IntegralHeight = false
            };

            foreach (var profile in request.Profiles)
            {
                _profiles.Items.Add(profile, true);
            }

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            _countdown = new TextLabel
            {
                Text = Remaining(timeout),
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(0, Spacing.Gap, 0, Spacing.Gap)
            };

            footer.Controls.Add(_countdown);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0)
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
            footer.Controls.Add(buttons);

            // The filling control goes in first. Docking is applied from the end of the collection
            // backwards, so whatever is added last takes its band first and index zero is left
            // with what remains.
            Controls.Add(_profiles);
            Controls.Add(layout);
            Controls.Add(footer);

            // Both of the ways a dialog gets dismissed without being read lead to a refusal, and
            // neither of them leads to a certificate.
            CancelButton = refuse;
            AcceptButton = refuse;

            _clock = new Timer { Interval = 1000 };
            _clock.Tick += OnTick;
            _clock.Start();
        }

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

        /// <summary>What the operator decided. A dialog closed any other way has timed out.</summary>
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
            // dialog does not show. TODO(step 8): the clients page arrives with the invitation
            // control.
            facts.Add(new Fact("Invitation id", request.InvitationId));

            if (!string.IsNullOrEmpty(request.Peer))
            {
                facts.Add(new Fact("Connected from", request.Peer));
            }

            return facts;
        }

        private Label Cell(string text, bool value)
        {
            return new TextLabel
            {
                Text = text,
                AutoSize = true,
                Font = value ? _valueFont : Font,
                MaximumSize = new Size(280, 0),
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

            return "This pairing request times out in " + seconds.ToString(CultureInfo.InvariantCulture) +
                   " seconds, and nothing is issued if it does.";
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
                approved.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
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

        private void Finish(PairingDecision decision)
        {
            _decision = decision;
            _clock.Stop();
            DialogResult = decision == PairingDecision.Approved ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }
}
