using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The dialog the authority page's Seal store again opens, `STORE.md` §625: why the store
    /// stopped, what the deletion records show as deleted and what the seal makes of each, what
    /// cannot be shown, and what the seal keeps; and the press that holds the seal for the
    /// settings dialog's OK.
    /// </summary>
    /// <remarks>
    /// **A dialog of its own**, sized like the authority chooser, since a list of what was lost
    /// does not fit the tab's room beside its sentences and its buttons, and a store can have
    /// lost many versions. The list scrolls vertically and never sideways: its columns share
    /// the list's width less a scroll bar's.
    ///
    /// **The two sentences about what cannot be shown are on the dialog and never in a tooltip**:
    /// confirming over a loss with no record is the consequence that can make a security answer
    /// silently not hold, `STORE.md` §639 row 5.
    ///
    /// **A kind in a column of its own, and no legend**: each row's tooltip holds the whole
    /// identifier of what it tells of and the sentence its third column stands for.
    /// </remarks>
    public sealed class SealForm : Form
    {
        /// <summary>The dialog's title.</summary>
        public const string Title = "Seal the store again";

        /// <summary>What the first line begins with, the store's reason following it.</summary>
        public const string StoppedStart = "The store of this database is stopped: ";

        /// <summary>The line over the list.</summary>
        public const string Over = "Deletion records show these as deleted:";

        /// <summary>The line over the list where a start set this store aside, `STORE.md` §632.</summary>
        public const string OverSetAside = "A start and deletion records show these:";

        /// <summary>The line in the list's place where no deletion record shows a loss.</summary>
        public const string NoneShown = "No deletion record shows a loss.";

        /// <summary>What cannot be shown and what confirming does, visible under the list.</summary>
        public const string Consequence =
            "What was deleted with no deletion record cannot be shown. Sealing makes the state shown the state, " +
            "in this copy and in every copy that takes the file in.";

        private const int FewestRows = 3;
        private const int MostRows = 12;

        private static readonly string[] Columns = { "Deleted", "Of", "After sealing" };
        private static readonly int[] Shares = { 3, 5, 4 };

        private readonly int _wrap;
        private readonly ListView _list;
        private readonly int _rows;

        internal SealForm(string reason, IReadOnlyList<SealRow> rows, string kept)
            : this(reason, rows, kept, null)
        {
        }

        /// <summary>The same, built at a font other than the system's, as a scaled display builds it.</summary>
        internal SealForm(string reason, IReadOnlyList<SealRow> rows, string kept, Font font)
        {
            if (font != null)
            {
                Font = font;
            }

            rows = rows ?? Array.Empty<SealRow>();
            _rows = rows.Count;

            Name = GateTab.SealFormName;
            Text = Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            using (var prototype = font == null ? SystemFonts.DefaultFont : new Font(font, font.Style))
            {
                _wrap = TextRenderer.MeasureText(new string('M', 56), prototype).Width;
            }

            Padding = new Padding(LogicalToDeviceUnits(Spacing.Window));

            var layout = new FlowLayoutPanel
            {
                Name = "KpgSealLayout",
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };

            layout.Controls.Add(Line("KpgSealReason", StoppedStart + Clipped.Message(reason), LogicalToDeviceUnits(Spacing.Gap)));

            _list = new ListView
            {
                Name = GateTab.SealListName,
                AccessibleName = GateTab.SealListLabel,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
                Width = _wrap,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            };

            if (rows.Count == 0)
            {
                layout.Controls.Add(Line("KpgSealNoneShown", NoneShown, LogicalToDeviceUnits(Spacing.Gap)));
            }
            else
            {
                var setAside = false;
                foreach (var row in rows)
                {
                    setAside |= row.Kind == SealRows.SetAside;
                }

                layout.Controls.Add(Line("KpgSealOver", setAside ? OverSetAside : Over, LogicalToDeviceUnits(Spacing.Tight)));
                AddColumns();
                foreach (var row in rows)
                {
                    var item = new ListViewItem(row.Kind) { ToolTipText = Tooltips.Laid(row.Tip) };
                    item.SubItems.Add(row.Of);
                    item.SubItems.Add(row.After);
                    _list.Items.Add(item);
                }

                _list.Height = (_list.Font.Height + 5) * (Math.Max(FewestRows, Math.Min(MostRows, rows.Count)) + 1)
                               + (SystemInformation.BorderSize.Height * 4) + Spacing.Tight;
                layout.Controls.Add(_list);
            }

            layout.Controls.Add(Line("KpgSealConsequence", Consequence, LogicalToDeviceUnits(Spacing.Gap)));
            layout.Controls.Add(Line(GateTab.SealKeptLabelName, kept ?? string.Empty, 0));

            var footer = new TableLayoutPanel
            {
                Name = "KpgSealFooter",
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                MinimumSize = new Size(_wrap, 0),
                Margin = new Padding(0, LogicalToDeviceUnits(Spacing.Gap), 0, 0)
            };

            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgSealButtonRow",
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Right
            };

            var seal = new Button
            {
                Name = GateTab.SealConfirmButtonName,
                Text = GateTab.SealConfirmButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };

            var cancel = new Button
            {
                Name = GateTab.SealCancelButtonName,
                Text = GateTab.SealCancelButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            seal.Click += delegate { WindowHandlers.Run("Seal again on the seal's dialog could not be carried out", Seal); };
            cancel.Click += delegate { WindowHandlers.Run("Cancel on the seal's dialog could not be carried out", Cancel); };

            buttons.Controls.Add(seal);
            buttons.Controls.Add(cancel);
            footer.Controls.Add(buttons, 1, 0);
            layout.Controls.Add(footer);
            Controls.Add(layout);

            // Seal again is the default, and Escape cancels.
            AcceptButton = seal;
            CancelButton = cancel;
        }

        /// <summary>Whether the operator pressed Seal again.</summary>
        public bool Sealed { get; private set; }

        /// <summary>Holds the seal, exactly as the button does.</summary>
        public void Seal()
        {
            Sealed = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Holds nothing, exactly as the button and Escape do.</summary>
        public void Cancel()
        {
            Sealed = false;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// Sizes the list from what it reports once it has a window, before the dialog is centered
        /// and shown, as the authority chooser does and for its reason.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            WindowHandlers.Run("the seal's dialog could not be fitted to its list", () =>
            {
                if (_list.IsHandleCreated && _list.Items.Count > 0)
                {
                    var first = _list.GetItemRect(0);
                    if (first.Top > 0 && first.Height > 0)
                    {
                        var frame = _list.Height - _list.ClientSize.Height;
                        _list.Height = frame + first.Top + (first.Height * Math.Max(FewestRows, Math.Min(MostRows, _rows)));
                    }
                }

                base.OnLoad(e);
            });
        }

        /// <summary>Kept out of screen captures when KeePass keeps its own windows out.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            WindowHandlers.Run("the seal's dialog could not be kept out of screen captures", () =>
            {
                base.OnHandleCreated(e);
                CaptureGuard.Apply(this);
            });
        }

        /// <summary>Columns in shares of the list's width less a scroll bar's, so nothing scrolls sideways.</summary>
        private void AddColumns()
        {
            var total = 0;
            foreach (var share in Shares)
            {
                total += share;
            }

            var room = _wrap - SystemInformation.VerticalScrollBarWidth - (SystemInformation.BorderSize.Width * 4);
            for (var at = 0; at < Columns.Length; at++)
            {
                _list.Columns.Add(Columns[at], room * Shares[at] / total);
            }
        }

        private Label Line(string name, string text, int below)
        {
            return new TextLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, below)
            };
        }
    }
}
