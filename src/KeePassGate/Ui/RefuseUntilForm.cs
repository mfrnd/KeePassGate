using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The dialog that asks which day renewals of one certificate are refused until.
    /// </summary>
    /// <remarks>
    /// Opened by the renewal dialog's "Refuse until a date..." and by nothing else, which is what
    /// the three dots on that choice promise: one more question before anything is decided.
    ///
    /// - **A calendar rather than a box to type into**, running from tomorrow to the day the
    ///   certificate the client holds now ends. A day before tomorrow would ask nothing of
    ///   anybody, and one after the certificate's end is a renewal that certificate can never ask
    ///   for.
    /// - **A week from today is chosen as it opens**, or the last day there is when the certificate
    ///   ends sooner.
    /// - **Refuse is the accept button and Cancel the cancel button.** Enter refuses until the
    ///   chosen day, unless a button has the focus, which then takes Enter itself, and Escape and
    ///   the close box go back to the renewal dialog with nothing decided, where Refuse still
    ///   refuses once. Refuse has no mnemonic, because the calendar the dialog opens on takes no
    ///   characters and a stray letter would otherwise write a standing answer.
    /// </remarks>
    internal sealed class RefuseUntilForm : Form
    {
        /// <summary>What choosing a day here does, said once.</summary>
        public const string Explanation =
            "Every renewal of this certificate is refused without asking until the day chosen " +
            "here begins, and asked about again from then on.";

        private readonly MonthCalendar _calendar;

        /// <param name="first">The earliest day that can be chosen: tomorrow.</param>
        /// <param name="last">The latest: the day the certificate the client holds now ends.</param>
        /// <param name="chosen">The day chosen as it opens.</param>
        /// <param name="font">The font to build it at, or null for the system's.</param>
        public RefuseUntilForm(DateTime first, DateTime last, DateTime chosen, Font font)
        {
            if (last < first)
            {
                throw new ArgumentException("There is no day between the first and the last.", nameof(last));
            }

            if (font != null)
            {
                Font = font;
            }

            Name = GateTab.RefuseUntilFormName;
            Text = "KeePassGate: refuse renewals until a day";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Spacing.Window);

            var layout = new FlowLayoutPanel
            {
                Name = "KpgRefuseUntilLayout",
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };

            layout.Controls.Add(new TextLabel
            {
                Name = "KpgRefuseUntilExplanation",
                Text = Explanation,
                AutoSize = true,
                MaximumSize = new Size(Wrap(Font), 0),
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            });

            _calendar = new MonthCalendar
            {
                Name = GateTab.RefuseUntilCalendarName,
                AccessibleName = GateTab.RefuseUntilCalendarLabel,
                MaxSelectionCount = 1,
                MinDate = first.Date,
                MaxDate = last.Date,

                // Today cannot be chosen, so a line offering to go to it, or a ring drawn round it,
                // would point at the one day that is not on offer.
                ShowToday = false,
                ShowTodayCircle = false,
                Margin = new Padding(0)
            };

            var day = chosen.Date < first.Date ? first.Date : chosen.Date > last.Date ? last.Date : chosen.Date;
            _calendar.SetDate(day);
            layout.Controls.Add(_calendar);

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgRefuseUntilButtonRow",
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, Spacing.Gap, 0, 0)
            };

            var refuse = new Button
            {
                Name = GateTab.RefuseUntilAcceptName,
                // No mnemonic: the calendar takes no characters, so a bare letter typed with it
                // focused would press the button and write a standing answer nobody chose a day for.
                Text = GateTab.RefuseUntilAcceptText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                DialogResult = DialogResult.OK,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            };

            var cancel = new Button
            {
                Name = GateTab.RefuseUntilCancelName,
                Text = GateTab.RefuseUntilCancelText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(0)
            };

            buttons.Controls.Add(refuse);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = refuse;
            CancelButton = cancel;
        }

        /// <summary>Kept out of screen captures when KeePass keeps its own windows out.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CaptureGuard.Apply(this);
        }

        /// <summary>The day chosen, which renewals are refused until the start of.</summary>
        public DateTime Day
        {
            get { return _calendar.SelectionStart.Date; }
        }

        /// <summary>Chooses a day, as clicking it on the calendar does.</summary>
        public void Choose(DateTime day)
        {
            _calendar.SetDate(day.Date);
        }

        /// <summary>
        /// How wide a line of the explanation is allowed to be: half the measure the renewal
        /// dialog reads at, since this holds one sentence over a calendar rather than a page.
        /// </summary>
        private static int Wrap(Font font)
        {
            return PairingApprovalForm.Wrap(font) / 2;
        }
    }
}
