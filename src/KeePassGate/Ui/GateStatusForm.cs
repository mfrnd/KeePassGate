using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the plugin is and whether it is serving, on its own window, reached from the Tools
    /// menu.
    /// </summary>
    /// <remarks>
    /// **This is about the plugin, not about a database**, which is why it is not on the database
    /// settings tab where it used to be. The socket that is listening is the same answer whichever
    /// database is open, and is wanted at moments when no database is open at all: after an
    /// install, and while working out why a client cannot connect. A tab inside a database's
    /// settings can answer neither, and it was spending its 314 pixels of height on them.
    ///
    /// **It does not say which version is loaded**, and that is a deletion rather than an
    /// oversight. KeePass has a plugins dialog of its own that lists every plugin with its version,
    /// which is where somebody checking one looks and where the answer is right for all of them at
    /// once. Repeating it here was a row on a small window that another window already owns.
    ///
    /// A table, because every line here is a value with a name and the operator is checking each
    /// against something they expect: a port against what they configured a client with.
    /// </remarks>
    public sealed class GateStatusForm : Form
    {
        /// <summary>What the window is called, for the tests that drive it.</summary>
        public const string FormName = "KpgStatusForm";

        /// <summary>What the title bar says, and what the end to end test looks for.</summary>
        public const string Caption = GateName.Product;

        /// <summary>
        /// How far below its own top edge a box starts drawing what it holds.
        /// </summary>
        /// <remarks>
        /// A label beside a box is read with the first line inside it, so the box sits this much
        /// higher than the label and the two lines of text come out level, a gap below the row
        /// above. It lines things up rather than putting air between them, which is why it is not a
        /// `Spacing`.
        /// </remarks>
        private const int TextInset = 3;

        private readonly Font _titleFont;
        private readonly Func<int, string> _apply;

        /// <summary>What the box was showing when the window opened.</summary>
        private readonly decimal _opened;

        /// <summary>The socket the gate is on right now, or zero when it is on none.</summary>
        private readonly int _where;
        private readonly NumericUpDown _port;

        private readonly CheckedListBox _addresses;

        private readonly ToolTip _tips;
        private readonly Label _result;

        public GateStatusForm(IGateStatus gate, GateIncidents incidents)
            : this(gate, incidents, null, 0)
        {
        }

        /// <param name="apply">
        /// Remembers a port and answers with why it could not be remembered, or null when it was.
        /// **It does not open one.** The window does not own a socket, and neither does anything it
        /// can reach: a port is what the gate binds the next time KeePass starts.
        /// </param>
        /// <param name="chosen">
        /// What the operator has chosen before, or zero when they have chosen nothing and the gate
        /// is on whatever it defaulted to.
        /// </param>
        public GateStatusForm(
            IGateStatus gate, GateIncidents incidents, Func<int, string> apply, int chosen)
        {
            _apply = apply;
            _where = gate != null && gate.IsListening ? gate.Port : 0;

            Name = FormName;
            Text = Caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            // Sized by what is in it rather than by a number written here. A number is right at one
            // font size and one display scaling and wrong at every other, and what it costs is not
            // a tidier layout: it is a port box that shows four of five digits and a sentence with
            // its last line cut off, on the one window an operator opens to read two values.
            //
            // No automatic scaling, because there is nothing for it to do: the widths are measured
            // in the font the window is built at, or converted to the display's units as they are
            // written. `AutoScaleMode.Font` stood here with no dimensions to scale from, which does
            // nothing at any display scaling and acts only on a font changed afterwards.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // The prototype is disposed, because `SystemFonts.DefaultFont` hands back a new font
            // every time it is read and `new Font(prototype, style)` copies rather than adopts.
            // Without the `using`, opening this window leaves one behind on each visit.
            using (var prototype = SystemFonts.DefaultFont)
            {
                _titleFont = new Font(prototype, FontStyle.Bold);
            }

            var layout = new FlowLayoutPanel
            {
                Name = "KpgStatusLayout",
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,

                // Every amount of space on this window is scaled with the display, as its widths
                // are. Left raw, the insets and gaps stayed at their 100 percent pixels on a
                // display at 200 percent while everything they separate had doubled.
                Padding = new Padding(LogicalToDeviceUnits(Spacing.Window)),
                AutoScroll = false,

                // The window takes its size from this, so this has to take its size from its
                // content rather than filling whatever it is given.
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.Controls.Add(new TextLabel
            {
                Text = GateName.Product + " serves this KeePass's databases to programs that have been let in.",
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(390), 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            });

            var facts = new TableLayoutPanel
            {
                Name = GateTab.StatusFactsName,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            };

            facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var rows = Facts(gate, incidents);

            // Two more rows than there are facts, because the address and the port belong in
            // this table rather than under it. They are the two things here that are settings
            // rather than reports, and they were in a band of their own: the label lined up with
            // the names above it and the box
            // did not line up with anything, because two panels sizing themselves separately agree
            // only by accident. In the table, the names are one column and the answers are the
            // other, and the socket that is listening sits directly above the box that changes it.
            facts.RowCount = rows.Count + 2;

            for (var at = 0; at < rows.Count; at++)
            {
                facts.Controls.Add(Cell(rows[at].ControlName(false), rows[at].Name, false), 0, at);
                facts.Controls.Add(Cell(rows[at].ControlName(true), rows[at].Value, true), 1, at);
            }

            // **The addresses, as a list of choices with one choice on it.** Loopback is the only
            // address this plugin binds, and that is the threat model's answer rather than the
            // socket layer's: everything here concedes local code execution as the user and
            // defends against a legitimate client asking for too much, and an address another
            // machine can reach is a different argument that nobody has made yet.
            //
            // A list that cannot be emptied says both true things at once. It is a decision the
            // plugin made, not a fact of the network, and there is exactly one option today. The
            // day a threat model admits a second interface, this grows a row instead of a design.
            _addresses = new CheckedListBox
            {
                Name = GateTab.StatusAddressListName,
                AccessibleName = GateTab.StatusAddressListLabel,
                CheckOnClick = true,
                IntegralHeight = true,
                MultiColumn = false,
                BorderStyle = BorderStyle.FixedSingle,
                Width = LogicalToDeviceUnits(120),
                Margin = new Padding(0, 0, 0, 0)
            };

            _addresses.Items.Add(GateTab.LoopbackAddress, true);

            // As tall as what is in it. A list box defaults to room for about six rows, and six
            // rows holding one entry reads as five addresses that failed to load rather than as
            // the only one there is. `IntegralHeight` keeps it to whole rows as it grows.
            // The six is the border. `IntegralHeight` rounds down to whole rows and counts the
            // frame against the space, so a height of exactly one row leaves room for none and the
            // control collapses to two pixels.
            _addresses.Height = (_addresses.ItemHeight * Math.Max(1, _addresses.Items.Count)) + 6;

            // **The one entry cannot be turned off**, and it is refused rather than disabled. A
            // disabled list reads as a setting that is unavailable for now and might come back; a
            // live list whose only entry will not clear reads as the one thing it is, which is the
            // choice the plugin has already made.
            _addresses.ItemCheck += delegate(object sender, ItemCheckEventArgs check)
            {
                if (check.NewValue != CheckState.Checked)
                {
                    check.NewValue = CheckState.Checked;
                }
            };

            _tips = new ToolTip();
            Tooltips.Set(_tips, _addresses, GateTab.StatusAddressHelp);

            var addressName = Cell("KpgStatusAddressLabel", GateTab.StatusAddressLabelText, false);
            addressName.Margin = new Padding(addressName.Margin.Left, LogicalToDeviceUnits(Spacing.Gap), addressName.Margin.Right, 0);
            _addresses.Margin = new Padding(0, LogicalToDeviceUnits(Spacing.Gap - TextInset), 0, 0);

            facts.Controls.Add(addressName, 0, rows.Count);
            facts.Controls.Add(_addresses, 1, rows.Count);

            // What the next start takes, so that pressing OK without touching it changes nothing:
            // `_opened` is what an untouched OK is measured against. The socket the gate is on now
            // is the row above, and after a port is written down the two differ for the rest of
            // the session.
            _opened = Starting(gate, chosen);
            _port = new NumericUpDown
            {
                Name = GateTab.StatusPortBoxName,
                AccessibleName = GateTab.StatusPortBoxLabel,
                Minimum = GatePort.Lowest,
                Maximum = GatePort.Highest,
                Value = _opened,
                Width = WidestPort(),
                Margin = new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };

            _port.Enabled = apply != null;

            var setting = new FlowLayoutPanel
            {
                Name = "KpgStatusPortRow",
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0)
            };

            setting.Controls.Add(_port);

            // A little air above it, because the rows above are one line each and this one is a
            // box: without it the table reads as four lines of the same kind of thing.
            var name = Cell("KpgStatusPortLabel", "Listen on port", false);
            name.Margin = new Padding(name.Margin.Left, LogicalToDeviceUnits(Spacing.Gap), name.Margin.Right, 0);
            setting.Margin = new Padding(0, LogicalToDeviceUnits(Spacing.Gap - TextInset), 0, 0);

            facts.Controls.Add(name, 0, rows.Count + 1);
            facts.Controls.Add(setting, 1, rows.Count + 1);

            layout.Controls.Add(facts);

            _result = new TextLabel
            {
                Name = GateTab.StatusResultLabelName,
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(390), 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            };

            layout.Controls.Add(_result);

            // A port waiting for the next start first, because it is the one thing on the window
            // that is about to change and the box showing it is the only other place it appears.
            var waiting = Waiting(gate, chosen);

            layout.Controls.Add(new TextLabel
            {
                Name = GateTab.StatusAdviceName,
                Text = waiting == null ? Advice(gate) : waiting + " " + Advice(gate),
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(390), 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            });

            // **OK and Cancel, in Windows's order and Windows's corner.** Right to left, so the
            // first one added is the rightmost: Cancel on the outside and OK beside it, which is
            // where every dialog KeePass shows puts them and where a hand already is.
            var ok = new Button
            {
                Name = GateTab.StatusOkButtonName,
                Text = GateTab.StatusOkButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(LogicalToDeviceUnits(75), 0),
                DialogResult = DialogResult.OK,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };

            // Taking what is in the box is what OK now does, and it is the only thing that does
            // it: pressing Cancel, or closing the window, leaves the port where it was.
            ok.Click += delegate
            {
                Keep();

                if (!Applied)
                {
                    // Refused. The window stays open, because the reason is written on it and
                    // closing would be answering the operator by taking the answer away.
                    DialogResult = DialogResult.None;
                }
            };

            var cancel = new Button
            {
                Name = GateTab.StatusCancelButtonName,
                Text = GateTab.StatusCancelButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(LogicalToDeviceUnits(75), 0),
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(0)
            };

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgStatusButtonRow",
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),

                // Along the bottom of what is above it rather than docked to the window, because
                // the window has no size of its own to dock against: it takes one from this column.
                Anchor = AnchorStyles.Right
            };

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);

            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;

            // The port, because it is the one thing on this window that can be changed. The
            // address list comes first in the tab order and would take the focus otherwise, and a
            // list whose only entry cannot be cleared is a poor place to arrive: the entry is
            // drawn focused, which reads as something the operator has just done to it. It stays
            // in the tab order, because a keyboard is still how somebody reads it.
            ActiveControl = _port;
        }

        /// <summary>Whether the last press of OK took the port it was given.</summary>
        public bool Applied { get; private set; }

        /// <summary>Whether a port was actually written down, rather than merely accepted.</summary>
        /// <remarks>
        /// Separate from `Applied`, which is also true of an OK on a box nobody moved. That one is
        /// the question "was the operator refused"; this one is "is there anything to tell them",
        /// and there is nothing to tell somebody about a port that is where it already was.
        /// </remarks>
        public bool Changed { get; private set; }

        /// <summary>What the last press of OK said, or null when it has said nothing.</summary>
        public string Result
        {
            get { return _result.Text.Length == 0 ? null : _result.Text; }
        }

        /// <summary>What the box is showing, and what OK would take.</summary>
        public int ChosenPort
        {
            get { return (int)_port.Value; }
            set { _port.Value = value; }
        }

        /// <summary>
        /// Keeps the port in the box, exactly as OK does.
        /// </summary>
        /// <remarks>
        /// **Kept rather than applied, and that is the whole of what this does now.** It used to
        /// close the socket and open another one while the operator watched, which is a setting
        /// that acts on a running server from a window that also offers Cancel. Worse, it is a
        /// setting whose failure mode is the gate being on nothing: the old socket was given up
        /// before the new one was asked for, so a port something else had already taken left this
        /// KeePass serving nobody until it was restarted anyway.
        ///
        /// A port is now what the gate binds at the next start. Nothing is torn down, nothing can
        /// half succeed, and a client that is connected stays connected.
        ///
        /// Public because a button press needs a window that has been shown, and the decision is
        /// worth testing without one.
        /// </remarks>
        public string Keep()
        {
            Applied = false;
            Changed = false;

            if (_apply == null)
            {
                return Said("Nothing here can change the port.");
            }

            var wanted = (int)_port.Value;

            if (wanted == _opened)
            {
                // Untouched, so there is nothing to write. The box opened on what the next start
                // takes, so writing it again would change nothing and would still write KeePass's
                // configuration for it.
                Applied = true;
                return Said(Untouched(wanted));
            }

            var refused = GatePort.Refuse(wanted);
            if (refused != null)
            {
                return Said(refused);
            }

            refused = _apply(wanted);
            if (refused != null)
            {
                return Said("The port was not remembered: " + refused);
            }

            Applied = true;
            Changed = true;

            return Said(Kept(wanted));
        }

        /// <summary>What OK says when the box was not moved, which writes nothing.</summary>
        /// <remarks>
        /// Only a box showing the socket the gate is on can say the gate is listening there. One
        /// that opened on the choice or on the default, because the gate is on nothing or on a port
        /// the box cannot show, says that nothing changed: it used to say "Saved", naming a save
        /// that never happened and a port the next start would not take.
        /// </remarks>
        private string Untouched(int wanted)
        {
            return _where == wanted
                ? "The gate is already listening on 127.0.0.1:" + wanted + "."
                : "Nothing was changed.";
        }

        /// <summary>What OK says once the port has been written down.</summary>
        /// <remarks>
        /// Typing the port the gate is listening on is how a port waiting for the next start is
        /// taken back, and it says so, rather than telling the operator to reconfigure clients
        /// that are already on the right port.
        /// </remarks>
        private string Kept(int wanted)
        {
            if (_where == wanted)
            {
                return "Saved. The gate stays on 127.0.0.1:" + wanted + " when KeePass next starts, "
                    + "where it is listening now, so no client has anything to change.";
            }

            return "Saved. The gate takes 127.0.0.1:" + wanted + " the next time KeePass starts, "
                + "and clients paired against the old port have to be told the new one.";
        }

        private string Said(string what)
        {
            _result.Text = what;
            return what;
        }

        /// <summary>
        /// What the box starts on: what the next start takes, which is what was chosen, or the
        /// port the gate is on when nothing was, or the default when neither says anything.
        /// </summary>
        /// <remarks>
        /// **The choice rather than the socket**, and it used to be the socket. The two differ for
        /// the whole of the session after a port is written down, since nothing rebinds, and a box
        /// showing the socket then left the port waiting for the next start shown nowhere at all,
        /// under a label saying the box is the port to listen on. Typing the socket's port back
        /// was an untouched OK and wrote nothing, so a waiting port could be moved but never taken
        /// back from here. The socket is the row above the box, and the box is the setting.
        /// </remarks>
        private static decimal Starting(IGateStatus gate, int chosen)
        {
            if (chosen >= GatePort.Lowest && chosen <= GatePort.Highest)
            {
                return chosen;
            }

            var listening = gate != null && gate.IsListening
                            && gate.Port >= GatePort.Lowest && gate.Port <= GatePort.Highest;

            return listening ? gate.Port : Transport.GateOptions.DefaultPort;
        }

        /// <summary>
        /// Where the gate moves at the next start, when that is not where it is listening now, or
        /// null.
        /// </summary>
        /// <remarks>
        /// Said in words as well as shown in the box, because the box alone is a number beside a
        /// row naming another one, and which of the two is waiting is the thing to be sure of.
        /// </remarks>
        public static string Waiting(IGateStatus gate, int chosen)
        {
            if (gate == null || !gate.IsListening
                || chosen < GatePort.Lowest || chosen > GatePort.Highest || chosen == gate.Port)
            {
                return null;
            }

            var port = chosen.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return "The gate moves to 127.0.0.1:" + port + " the next time KeePass starts.";
        }

        /// <summary>
        /// What the window says, without a window.
        /// </summary>
        /// <remarks>
        /// Public and static for the same reason every other decision here is: the wording is worth
        /// testing and a form is a poor place to test it through.
        /// </remarks>
        public static IReadOnlyList<Fact> Facts(IGateStatus gate, GateIncidents incidents)
        {
            var facts = new List<Fact>
            {
                new Fact("Listening", Where(gate))
            };

            var summary = incidents == null ? null : incidents.Summary;
            if (!string.IsNullOrEmpty(summary))
            {
                facts.Add(new Fact("Incidents", summary));
            }

            return facts;
        }

        /// <summary>
        /// Where the gate is listening, or that it is not. The address is always loopback, and
        /// saying so is the answer to the question this is usually opened for.
        /// </summary>
        private static string Where(IGateStatus gate)
        {
            if (gate == null)
            {
                return "not known";
            }

            return gate.IsListening
                ? "127.0.0.1:" + gate.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "no, nothing can reach any database";
        }

        /// <summary>
        /// What to do about what the table says.
        /// </summary>
        /// <remarks>
        /// **A gate that is down is down for the session**, because the port is bound once at
        /// startup and never rebound, so this is the sentence that has to carry both halves: what
        /// went wrong, and that only starting KeePass again will change it. The other half matters
        /// as much: nothing else about the plugin has stopped, and an operator who reads "not
        /// listening" and closes the window would never find out that everything on it still works.
        /// </remarks>
        public static string Advice(IGateStatus gate)
        {
            if (gate != null && !gate.IsListening)
            {
                var why = string.IsNullOrEmpty(gate.Refusal)
                    ? "The gate is not listening. The log says why."
                    : Sentence(gate.Refusal);

                return why
                    + " Nothing reaches any database until KeePass is started again. Everything else "
                    + "here still works: settings, authorities and profiles can all be changed, and "
                    + "they are waiting for the next start too.";
            }

            return "A database is served only once it has an authority, which is set up on the "
                + GateName.OnATab + " tab of that database's settings.";
        }

        /// <summary>
        /// Ends a fragment so the next sentence can follow it.
        /// </summary>
        /// <remarks>
        /// What goes in front is a reason from somewhere else, and Windows's own socket messages
        /// end without a stop: without this, "Only one usage of each socket address is normally
        /// permitted Nothing reaches any database" is one run-on line and reads as one sentence
        /// nobody finished writing.
        /// </remarks>
        private static string Sentence(string fragment)
        {
            var trimmed = fragment.TrimEnd();

            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            var last = trimmed[trimmed.Length - 1];

            return last == '.' || last == '!' || last == '?' ? trimmed : trimmed + ".";
        }

        /// <summary>
        /// Wide enough for the largest port there is, and no wider.
        /// </summary>
        /// <remarks>
        /// It was eighty logical pixels, which is a number somebody chose rather than measured: a
        /// port is at most five digits and the box held about eight, so it sat there looking like
        /// it wanted a longer answer than it can take.
        ///
        /// Measured instead, so it follows the font and the display rather than the guess. The
        /// spinner is as wide as a scroll bar, which is what `NumericUpDown` gives it, and the
        /// border and the inset between the border and the digits are the rest.
        /// </remarks>
        private int WidestPort()
        {
            // Measured with the padding a text box puts around its own content rather than
            // without it: the digits are not drawn against the border, and a box measured as if
            // they were is a box that clips them.
            var digits = TextRenderer.MeasureText(
                GatePort.Highest.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Font);

            return digits.Width
                + SystemInformation.VerticalScrollBarWidth
                + (SystemInformation.BorderSize.Width * 2);
        }

        private Label Cell(string name, string text, bool value)
        {
            return new TextLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Font = value ? _titleFont : Font,
                Margin = value ? new Padding(0) : new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };
        }

        /// <summary>Kept out of screen captures when KeePass keeps its own windows out.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CaptureGuard.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_titleFont != null)
                {
                    _titleFont.Dispose();
                }

                if (_tips != null)
                {
                    _tips.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
