using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the KPG tab shows while there is nothing yet to configure: what the plugin is, which
    /// version is loaded, and what it is currently able to do. Built in code rather than in a
    /// designer file, so that the layout is reviewable in a diff.
    /// </summary>
    /// <remarks>
    /// Only the panel carries an accessible name. Setting one on a label would replace the text
    /// that a screen reader, and the end to end test, are supposed to read.
    /// </remarks>
    public sealed class GateStatusPanel : UserControl
    {
        private readonly Font _titleFont;

        public GateStatusPanel(string version, string status)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            Name = GateTab.PanelControlName;
            Dock = DockStyle.Fill;
            Padding = new Padding(12);
            AutoScroll = true;

            var layout = new FlowLayoutPanel
            {
                Name = GateTab.LayoutControlName,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            // Held so it can be disposed: a control does not dispose a font handed to it.
            _titleFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

            var title = new Label
            {
                Name = GateTab.TitleLabelName,
                Text = "KeePassGate",
                AutoSize = true,
                Font = _titleFont,
                Margin = new Padding(0, 0, 0, 6)
            };

            var versionLabel = new Label
            {
                Name = GateTab.VersionLabelName,
                Text = "Version " + version,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };

            var statusLabel = new Label
            {
                Name = GateTab.StatusLabelName,
                Text = status,
                AutoSize = true,
                MaximumSize = new Size(420, 0)
            };

            layout.Controls.Add(title);
            layout.Controls.Add(versionLabel);
            layout.Controls.Add(statusLabel);
            Controls.Add(layout);
        }

        protected override void Dispose(bool disposing)
        {
            // The label holding this font is disposed by the base call, so the font goes
            // after it. Freeing it first left a live control drawing with a released handle
            // for the length of that call.
            base.Dispose(disposing);

            if (disposing && _titleFont != null)
            {
                _titleFont.Dispose();
            }
        }
    }
}
