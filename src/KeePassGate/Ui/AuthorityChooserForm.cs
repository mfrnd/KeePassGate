using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The dialog that asks which of several authorities a database keeps.
    /// </summary>
    /// <remarks>
    /// Raised when copies of a database that each made an authority of their own have been merged,
    /// which serves nothing until somebody decides. Everything about it assumes the operator did
    /// not expect it and has to be told what happened before being asked anything.
    ///
    /// - **One list of authorities and the details of the selected one**, rather than a section
    ///   per authority, because there may be more than two and nothing here scrolls but a list.
    ///   Each authority is named by its short fingerprint, the one the tab shows, with its dates
    ///   and how many clients it admitted; the full fingerprint, the host certificate's expiry and
    ///   those clients follow for whichever one is selected.
    /// - **Which clients an authority admitted is the evidence**, so each one is listed with its
    ///   profiles, when it was admitted, and the last request the audit holds for it. The copy
    ///   that everything still uses is usually the one to keep.
    /// - **Keeping takes a press.** Enter reaches nothing and Escape cancels, and so does every
    ///   other way of dismissing the dialog: a choice retires the others for good, and a dialog
    ///   that can be dismissed into one is a dialog that will be.
    /// - **Only an authority that reads can be kept.** One that will not is listed, because it is
    ///   still one the database holds, and keeping another retires it.
    /// </remarks>
    public sealed class AuthorityChooserForm : Form
    {
        /// <summary>What choosing does, said once and in full.</summary>
        public const string Consequence =
            "Keeping one retires the others for good, and the clients they admitted stop working.";

        /// <summary>What a client with nothing in the audit shows as its last request.</summary>
        public const string NeverSeen = "none in the audit";

        /// <summary>What every client shows when the audit could not be read at all.</summary>
        public const string Unknown = "unknown";

        private const int FewestAuthorityRows = 2;
        private const int MostAuthorityRows = 5;
        private const int FewestClientRows = 3;
        private const int MostClientRows = 8;

        private static readonly string[] AuthorityColumns = { "Fingerprint", "Created", "Valid until", "Clients" };
        private static readonly int[] AuthorityShares = { 3, 4, 4, 2 };
        private static readonly string[] ClientColumns = { "Client", "Profiles", "Admitted", "Last request" };
        private static readonly int[] ClientShares = { 3, 3, 4, 4 };

        private readonly AuthorityChoice _choice;
        private readonly Font _titleFont;
        private readonly Font _fingerprintFont;
        private readonly int _wrap;
        private readonly ListView _authorities;
        private readonly TextBox _fingerprint;
        private readonly Label _detail;
        private readonly ListView _clients;
        private readonly Button _keep;

        private string _chosen;
        private AuthorityOption _selected;

        public AuthorityChooserForm(AuthorityChoice choice, string databaseName)
            : this(choice, databaseName, null)
        {
        }

        /// <summary>
        /// The same, built at a font other than the system's, which is how a test stands in for a
        /// display that was scaled before the dialog was made.
        /// </summary>
        /// <remarks>
        /// Everything that sizes this dialog is measured while it is built, from the font it is
        /// built at, as the approval dialog's is and for its reason: a larger font handed in here is
        /// measured the way a larger system font is on a scaled display, and nothing changes the
        /// font of a dialog already built.
        /// </remarks>
        internal AuthorityChooserForm(AuthorityChoice choice, string databaseName, Font font)
        {
            if (choice == null)
            {
                throw new ArgumentNullException(nameof(choice));
            }

            if (font != null)
            {
                Font = font;
            }

            _choice = choice;

            Name = GateTab.ChooserFormName;
            Text = GateName.Full + ": choose an authority";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            // Sized by what is in it, as the approval dialog is and for its reason: a fixed client
            // area is right at one display scaling and clips at every other. No automatic scaling,
            // for the approval dialog's reason too: with no dimensions to scale from it acted only
            // when the font changed after the dialog was built, and then scaled what had already
            // been measured in the larger font.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            using (var prototype = font == null ? SystemFonts.DefaultFont : new Font(font, font.Style))
            {
                _titleFont = new Font(prototype, FontStyle.Bold);
                _wrap = TextRenderer.MeasureText(new string('M', 56), prototype).Width;
            }

            // Monospaced, because the full fingerprint is sixty-four characters somebody compares
            // against another screen, and a proportional font makes that a matter of luck.
            _fingerprintFont = new Font(FontFamily.GenericMonospace, Font.Size, FontStyle.Regular);

            Padding = new Padding(Spacing.Window);

            var layout = new FlowLayoutPanel
            {
                Name = "KpgChooserLayout",
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };

            layout.Controls.Add(Line(Title(choice, databaseName), _titleFont, Spacing.Gap));
            layout.Controls.Add(Line(
                "Copies of this database each made one, and a merge brought them together. " +
                "Nothing is served until one is kept. " + Consequence,
                null,
                Spacing.Gap));

            layout.Controls.Add(Line(GateTab.ChooserAuthorityListLabel + ":", null, Spacing.Tight));
            _authorities = NewList(GateTab.ChooserAuthorityListName, GateTab.ChooserAuthorityListLabel);
            AddColumns(_authorities, AuthorityColumns, AuthorityShares);
            foreach (var option in choice.Options)
            {
                var row = new ListViewItem(option.ShortFingerprint) { Tag = option };
                row.SubItems.Add(Stamp(option.Created));
                row.SubItems.Add(Stamp(option.Expires));
                row.SubItems.Add(option.CanKeep
                    ? option.Clients.Count.ToString(CultureInfo.InvariantCulture)
                    : "unreadable");
                _authorities.Items.Add(row);
            }

            _authorities.Height = ListHeight(_authorities, choice.Options.Count, FewestAuthorityRows, MostAuthorityRows);
            _authorities.Margin = new Padding(0, 0, 0, Spacing.Gap);
            _authorities.SelectedIndexChanged += delegate { OnSelectionChanged(); };
            layout.Controls.Add(_authorities);

            layout.Controls.Add(Line(GateTab.ChooserFingerprintBoxLabel + ":", null, Spacing.Tight));
            _fingerprint = new TextBox
            {
                Name = GateTab.ChooserFingerprintBoxName,
                AccessibleName = GateTab.ChooserFingerprintBoxLabel,
                ReadOnly = true,
                Font = _fingerprintFont,
                Width = _wrap,
                Margin = new Padding(0, 0, 0, Spacing.Gap)
            };
            layout.Controls.Add(_fingerprint);

            _detail = Line(string.Empty, null, Spacing.Gap);
            _detail.Name = GateTab.ChooserDetailLabelName;
            layout.Controls.Add(_detail);

            layout.Controls.Add(Line(GateTab.ChooserClientListLabel + ":", null, Spacing.Tight));
            _clients = NewList(GateTab.ChooserClientListName, GateTab.ChooserClientListLabel);
            AddColumns(_clients, ClientColumns, ClientShares);
            _clients.Height = ListHeight(_clients, MostClients(choice), FewestClientRows, MostClientRows);
            _clients.Margin = new Padding(0);
            layout.Controls.Add(_clients);

            var footer = new TableLayoutPanel
            {
                Name = "KpgChooserFooter",
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                MinimumSize = new Size(_wrap, 0),
                Margin = new Padding(0, Spacing.Gap, 0, 0)
            };

            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgChooserButtonRow",
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Right
            };

            // Both margins written down, level with each other, for the reason the approval
            // dialog gives about two buttons on one row.
            var cancel = new Button
            {
                Name = GateTab.ChooserCancelButtonName,
                Text = GateTab.ChooserCancelButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, Spacing.Gap, 0)
            };

            _keep = new Button
            {
                Name = GateTab.KeepButtonName,
                Text = "&" + GateTab.KeepButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            cancel.Click += delegate { Cancel(); };
            _keep.Click += delegate { Keep(); };

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_keep);
            footer.Controls.Add(buttons, 1, 0);
            layout.Controls.Add(footer);

            Controls.Add(layout);

            // Escape cancels and Enter reaches nothing: keeping is a press, never a dismissal.
            CancelButton = cancel;

            if (choice.Options.Count > 0)
            {
                Select(choice.Options[0].Fingerprint);
            }
            else
            {
                ShowSelected();
            }
        }

        /// <summary>The digest the operator kept, or null when they did not keep one.</summary>
        public string Chosen
        {
            get { return _chosen; }
        }

        /// <summary>
        /// Selects the authority with this digest, as clicking its row does. Public because a click
        /// needs a window on the screen and a test of what the dialog shows does not.
        /// </summary>
        public void Select(string fingerprint)
        {
            _selected = null;
            foreach (ListViewItem row in _authorities.Items)
            {
                var option = (AuthorityOption)row.Tag;
                row.Selected = option.Fingerprint == fingerprint;
                if (row.Selected)
                {
                    _selected = option;
                }
            }

            ShowSelected();
        }

        /// <summary>
        /// Follows a click in the list. Kept apart from the list's own selection, which a list
        /// with no window yet does not keep, so that what is shown and what Keep keeps are one
        /// answer whether or not the dialog was ever on a screen.
        /// </summary>
        private void OnSelectionChanged()
        {
            if (_authorities.SelectedItems.Count == 0)
            {
                // A click between rows clears the list's selection. What was selected stays, so
                // Keep never keeps something other than what the details below describe.
                return;
            }

            _selected = (AuthorityOption)_authorities.SelectedItems[0].Tag;
            ShowSelected();
        }

        /// <summary>
        /// Keeps the selected authority, exactly as the button does, when it can be kept.
        /// </summary>
        public void Keep()
        {
            var option = Selected();
            if (option == null || !option.CanKeep)
            {
                return;
            }

            _chosen = option.Fingerprint;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Keeps nothing, exactly as the button does.</summary>
        public void Cancel()
        {
            _chosen = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>The authority whose details are shown, or null when none is selected.</summary>
        public AuthorityOption Selected()
        {
            return _selected;
        }

        /// <summary>What the heading says, which names the database when it has a name.</summary>
        public static string Title(AuthorityChoice choice, string databaseName)
        {
            var count = choice == null ? 0 : choice.Options.Count;
            var what = string.IsNullOrEmpty(databaseName) ? "This database" : databaseName;
            return what + " holds " + count.ToString(CultureInfo.InvariantCulture) + " certificate authorities.";
        }

        /// <summary>What a client's last request reads as.</summary>
        public static string LastRequest(ChoiceClient client, bool known)
        {
            if (!known)
            {
                return Unknown;
            }

            return client.LastSeen.HasValue ? Moments.OnScreen(client.LastSeen.Value) : NeverSeen;
        }

        /// <summary>
        /// Sizes both lists from what they report once they have window handles, before the dialog
        /// is centered and shown.
        /// </summary>
        /// <remarks>
        /// **Measured, because the estimate is wrong exactly where it matters.** A list in details
        /// view draws its header through the theme, and under the visual styles KeePass turns on, a
        /// themed header is taller than a row. Sized as though the header were one more row, the
        /// list of two authorities came out a few pixels short and grew a scroll bar with both rows
        /// in plain sight. Where the first row starts and how tall it is are asked of the list
        /// itself, which answers for the theme, the font and the scaling in use.
        ///
        /// Before `base.OnLoad`, which is where a modal dialog is centered on its owner, so that it
        /// is centered at the size it is shown at.
        /// </remarks>
        protected override void OnLoad(EventArgs e)
        {
            int header;
            int row;
            if (RowMetrics(out header, out row))
            {
                Fit(_authorities, header, row, RowsFor(_choice.Options.Count, FewestAuthorityRows, MostAuthorityRows));
                Fit(_clients, header, row, RowsFor(MostClients(_choice), FewestClientRows, MostClientRows));
            }

            base.OnLoad(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_titleFont != null)
                {
                    _titleFont.Dispose();
                }

                if (_fingerprintFont != null)
                {
                    _fingerprintFont.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void ShowSelected()
        {
            var option = Selected();

            _fingerprint.Text = option == null ? string.Empty : option.Fingerprint;
            _keep.Enabled = option != null && option.CanKeep;

            // One line for either, so that selecting a row does not make the dialog jump: what the
            // host certificate is good for, or why this one cannot be kept at all.
            if (option != null && !option.CanKeep)
            {
                _detail.Text = "This one cannot be read, so it cannot be kept: " + option.Problem;
                _detail.ForeColor = Color.Firebrick;
            }
            else
            {
                _detail.Text = option == null
                    ? string.Empty
                    : "Its host certificate is valid until " + Stamp(option.HostExpires) + ".";
                _detail.ForeColor = ForeColor;
            }

            _clients.BeginUpdate();
            try
            {
                _clients.Items.Clear();
                if (option == null)
                {
                    return;
                }

                foreach (var client in option.Clients)
                {
                    var row = new ListViewItem(client.Name);
                    row.SubItems.Add(string.Join(", ", client.Profiles));
                    row.SubItems.Add(Moments.OnScreen(client.Admitted));
                    row.SubItems.Add(LastRequest(client, _choice.LastSeenKnown));
                    _clients.Items.Add(row);
                }
            }
            finally
            {
                _clients.EndUpdate();
            }
        }

        private Label Line(string text, Font font, int below)
        {
            var label = new TextLabel
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, below)
            };

            if (font != null)
            {
                label.Font = font;
            }

            return label;
        }

        private ListView NewList(string name, string accessibleName)
        {
            return new ListView
            {
                Name = name,

                // A list has no text of its own to be announced by, so this is the phrase.
                AccessibleName = accessibleName,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Width = _wrap
            };
        }

        /// <summary>
        /// Columns in shares of the list's width, less the room a scroll bar takes, so that a list
        /// that grows one never grows a horizontal one beside it.
        /// </summary>
        private void AddColumns(ListView list, string[] headings, int[] shares)
        {
            var total = 0;
            foreach (var share in shares)
            {
                total += share;
            }

            var room = _wrap - SystemInformation.VerticalScrollBarWidth - (SystemInformation.BorderSize.Width * 4);
            for (var i = 0; i < headings.Length; i++)
            {
                list.Columns.Add(headings[i], room * shares[i] / total);
            }
        }

        /// <summary>
        /// How tall a list is for this many rows, header and border included, estimated for a list
        /// that has no window handle yet.
        /// </summary>
        /// <remarks>
        /// An estimate only, and not the size the dialog is shown at: <see cref="OnLoad(EventArgs)"/>
        /// replaces it with what the lists report. It is what a dialog that is laid out and never shown is
        /// measured at, which is how most of its tests see it.
        /// </remarks>
        private static int ListHeight(ListView list, int rows, int fewest, int most)
        {
            var row = list.Font.Height + 5;
            return (row * (RowsFor(rows, fewest, most) + 1)) + (SystemInformation.BorderSize.Height * 4) + Spacing.Tight;
        }

        /// <summary>How many rows a list is sized for: all of them, between the fewest and the most.</summary>
        private static int RowsFor(int rows, int fewest, int most)
        {
            return Math.Max(fewest, Math.Min(most, rows));
        }

        /// <summary>
        /// How tall the header of the lists is and how tall a row, read off whichever list has a
        /// row to read. Both lists are drawn in one font and one view, so what one reports is true
        /// of the other; with no row in either there is nothing to measure, and the estimate stands.
        /// </summary>
        private bool RowMetrics(out int header, out int row)
        {
            foreach (var list in new[] { _authorities, _clients })
            {
                if (!list.IsHandleCreated || list.Items.Count == 0)
                {
                    continue;
                }

                // In client coordinates, and with nothing scrolled yet, so the first row starts
                // exactly where the header ends.
                var first = list.GetItemRect(0);
                header = first.Top;
                row = first.Height;
                return header > 0 && row > 0;
            }

            header = 0;
            row = 0;
            return false;
        }

        /// <summary>
        /// Makes a list exactly as tall as its frame, its header and this many rows, so that rows it
        /// can show never need a scroll bar and the rows beyond them get one.
        /// </summary>
        private static void Fit(ListView list, int header, int row, int shown)
        {
            var frame = list.Height - list.ClientSize.Height;
            list.Height = frame + header + (row * shown);
        }

        private static int MostClients(AuthorityChoice choice)
        {
            var most = 0;
            foreach (var option in choice.Options)
            {
                most = Math.Max(most, option.Clients.Count);
            }

            return most;
        }

        private static string Stamp(DateTime? moment)
        {
            return moment.HasValue ? Moments.OnScreen(moment.Value) : string.Empty;
        }
    }
}
