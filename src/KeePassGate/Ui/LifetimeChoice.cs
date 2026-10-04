using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Authority;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The row on an approval dialog that says how long the certificate will last, and lets the
    /// operator change it before approving.
    /// </summary>
    /// <remarks>
    /// **The client proposes and the operator picks**, `CA.md` §207. What is offered is the five
    /// presets `Lifetime` names, and what the client asked for when that is not one of them, as a
    /// choice of its own in its place by length. The one asked for is marked, so the operator can
    /// see that the preselected choice came from the client rather than from the dialog; a client
    /// that asked for nothing has a year preselected, which is what every client was given before
    /// it could ask.
    ///
    /// **A list rather than a box to type into**, for the reason nothing on the grant editor is
    /// typed into: a number typed by hand is a lifetime nobody meant, and the ones worth giving
    /// are few. It drops down rather than standing open, because the dialog's room goes to the
    /// profiles and the answer here is one line.
    ///
    /// **Enter and Escape stay the dialog's.** A closed list does not take either key, so both
    /// still reach the refusal they reach everywhere else on the dialog; an open one takes them to
    /// pick or to close, as every list does, and the dialog is untouched until it is closed. **And
    /// nothing else typed on it closed changes it**: `SteadyChoice` takes no letter, digit or
    /// arrow until it is opened, so a stray key cannot lengthen what Approve issues.
    ///
    /// A control rather than something that builds one, so that the dialog's own control tree
    /// owns it and disposes it with everything else.
    /// </remarks>
    internal sealed class LifetimeChoice : TableLayoutPanel
    {
        /// <summary>What marks the lifetime the client asked for.</summary>
        public const string AskedSuffix = " (asked)";

        /// <summary>What the row says in front of the choice.</summary>
        public const string Caption = "The certificate lasts";

        private readonly IReadOnlyList<TimeSpan> _offered;
        private readonly ComboBox _box;

        /// <param name="asked">What the client asked for, or null for nothing.</param>
        /// <param name="font">The dialog's own, which every width here is measured in.</param>
        public LifetimeChoice(TimeSpan? asked, Font font)
        {
            _offered = Offered(asked);

            Name = "KpgApprovalLifetimeRow";
            Font = font;

            // Two columns, each anchored left and so centered on the row's height, which levels the
            // caption with the text inside the list rather than with its top edge.
            ColumnCount = 2;
            RowCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            Margin = new Padding(0, Spacing.Gap, 0, 0);

            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(
                new TextLabel
                {
                    Name = "KpgApprovalLifetimeCaption",
                    Text = Caption,
                    AutoSize = true,
                    Margin = new Padding(0, 0, Spacing.Gap, 0),
                    Anchor = AnchorStyles.Left
                },
                0,
                0);

            _box = new SteadyChoice
            {
                Name = GateTab.ApprovalLifetimeName,
                AccessibleName = GateTab.ApprovalLifetimeLabel,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Left
            };

            foreach (var lifetime in _offered)
            {
                _box.Items.Add(Listed(lifetime, asked));
            }

            _box.SelectedIndex = Preselected(_offered, asked);

            // As wide as the longest choice and the arrow beside it, measured in the font the list
            // is drawn in, so no choice is cut at any display scaling.
            _box.Width = _box.Items.Cast<string>()
                             .Select(text => TextRenderer.MeasureText(text, font).Width)
                             .Max()
                         + SystemInformation.VerticalScrollBarWidth + Spacing.Gap;

            Controls.Add(_box, 1, 0);
        }

        /// <summary>
        /// What is chosen now, which is what approving gives the certificate, and a year when
        /// nothing is, as when nothing was asked for.
        /// </summary>
        public TimeSpan Chosen
        {
            get
            {
                var at = _box.SelectedIndex;
                return at < 0 ? Lifetime.Unasked : _offered[at];
            }
        }

        /// <summary>
        /// What is offered, shortest first: the presets, and what the client asked for when that
        /// is not one of them.
        /// </summary>
        public static IReadOnlyList<TimeSpan> Offered(TimeSpan? asked)
        {
            var offered = Lifetime.PresetDays.Select(days => TimeSpan.FromDays(days)).ToList();

            if (asked.HasValue && Lifetime.IsAllowed(asked.Value) && IndexOf(offered, asked.Value) < 0)
            {
                offered.Add(asked.Value);
                offered.Sort();
            }

            return offered;
        }

        /// <summary>How a choice reads: "30 days", and "30 days (asked)" for the one asked for.</summary>
        public static string Listed(TimeSpan lifetime, TimeSpan? asked)
        {
            return Lifetime.Shown(lifetime) + (asked.HasValue && asked.Value == lifetime ? AskedSuffix : string.Empty);
        }

        /// <summary>Which choice the dialog opens on: the one asked for, or a year.</summary>
        public static int Preselected(IReadOnlyList<TimeSpan> offered, TimeSpan? asked)
        {
            var at = asked.HasValue ? IndexOf(offered, asked.Value) : -1;
            return at >= 0 ? at : IndexOf(offered, Lifetime.Unasked);
        }

        private static int IndexOf(IReadOnlyList<TimeSpan> offered, TimeSpan lifetime)
        {
            for (var at = 0; at < offered.Count; at++)
            {
                if (offered[at] == lifetime)
                {
                    return at;
                }
            }

            return -1;
        }
    }
}
