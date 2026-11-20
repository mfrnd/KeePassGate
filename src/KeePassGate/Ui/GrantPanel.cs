using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The KPG tab on a group or entry dialog: who may reach this, and how far.
    /// </summary>
    /// <remarks>
    /// The stock KeePass dialogs offer no way to set a rule, so without this tab no grant can be
    /// made at all and the whole access control model is inert.
    ///
    /// **The table is the tab.** Rows by profile, each rule as a `CRUSDLAP` mask in a monospaced
    /// column, the rules inherited from above shown among the node's own in gray, any of those
    /// the node's box refuses struck through, and a State column naming what keeps a row from
    /// being in force as shown, with what the state means in the tooltip of its cell. The rows of
    /// a profile bound together, the versions of a rule or a limit under its inherited rule, stand
    /// in a bracket on a light red, with the letters they differ in drawn in bold, `GrantMarks`.
    /// Everything the panel decides is in `GrantSheet`, which is tested without a message pump;
    /// what is left here is drawing the sheet and turning clicks into calls.
    ///
    /// **A press writes nothing**, `STORE.md` §629. A press is a choice in a picker, an X or the
    /// box, and each changes the sheet alone; the dialog's OK hands the sheet's changes to the
    /// writer as the dialog closes, and any other close throws the changes away. So OK and Cancel
    /// on the dialog do what the two words promise, and a row changed on the tab tells so in its
    /// tooltip, its accessible text and a light yellow until the dialog closes.
    ///
    /// Nothing in here throws. It runs on the UI thread of a password manager.
    /// </remarks>
    public sealed class GrantPanel : UserControl
    {
        private const int ProfileColumn = GrantTab.ProfileColumn;
        private const int MaskColumn = GrantTab.MaskColumn;
        private const int StateColumn = GrantTab.StateColumn;
        private const int ForgetColumn = GrantTab.ForgetColumn;

        /// <summary>What the button in the last column reads. A single character, and it means remove.</summary>
        private const string ForgetMark = "X";

        /// <summary>Every word the State column can show.</summary>
        private static readonly string[] StateWords =
            Enum.GetValues(typeof(GrantState)).Cast<GrantState>().Select(GrantTab.Word).ToArray();

        private readonly GrantSheet _sheet;
        private readonly IGateLog _log;

        /// <summary>
        /// The node's rules, what arrives and the names, read again, or null where nothing is read
        /// again.
        /// </summary>
        private readonly Func<GrantReading> _again;

        /// <summary>Told when a record lands, or null where nothing is listened to.</summary>
        private readonly IAdmissionNotice _admissions;

        private readonly Label _scope;
        private readonly Label _warning;
        private readonly DataGridView _grid;
        private readonly CheckBox _sealed;
        private readonly ToolTip _tips;

        /// <summary>The strip between the table and the box.</summary>
        private readonly Panel _air;

        /// <summary>
        /// How tall the strip between the table and the box stands where the strip lends nothing,
        /// scaled with the panel.
        /// </summary>
        private int _band = Spacing.Gap;

        /// <summary>
        /// How many presses the sheet took, so that a failure after a press can tell whether the
        /// press is on the sheet.
        /// </summary>
        private int _pressed;

        /// <summary>The masks' own font, built again whenever the panel's changes.</summary>
        private Font _monospace;

        /// <summary>
        /// The same font with a line through it, for a mask that no longer arrives.
        /// </summary>
        /// <remarks>
        /// Derived from the monospaced one rather than built beside it, so that a refused mask is
        /// the same eight characters in the same places as every other mask in the column, with
        /// one thing added.
        /// </remarks>
        private Font _refusedMask;

        private bool _populating;

        /// <summary>Whether drawing the marks failed since the table was last filled, `OnCellPainting`.</summary>
        private bool _marksFailed;

        /// <summary>
        /// The picker that is showing, or null.
        /// </summary>
        /// <remarks>
        /// One at a time, always. A second dropdown opened over the first leaves the first one's
        /// closing to run against a grid the second is already using, and it leaves two suspensions
        /// of the dialog's accept and cancel buttons where only the inner one is ever undone.
        /// </remarks>
        private Picker _open;

        /// <summary>
        /// The tab over a sheet, reading the node's rules and the database's profiles again when a
        /// record lands.
        /// </summary>
        /// <remarks>
        /// A pairing approved while a group or an entry dialog is open names a profile the list
        /// did not, and a synchronize a trigger starts can bring versions of the node's rules in:
        /// a row the operator did not touch follows the store, and a row the operator touched keeps
        /// its press, `GrantSheet.Rebase`.
        /// </remarks>
        /// <param name="again">
        /// The node's rules, what arrives and the database's list, read again, or null for a tab
        /// that reads nothing again.
        /// </param>
        /// <param name="admissions">
        /// Told when a record lands, or null for a tab told nothing.
        /// </param>
        public GrantPanel(
            GrantSheet sheet,
            string scope,
            IGateLog log,
            Func<GrantReading> again,
            IAdmissionNotice admissions)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            _sheet = sheet;
            _log = log ?? NullGateLog.Instance;
            _again = again;
            _admissions = admissions;

            Name = GrantTab.PanelControlName;

            // A name of its own: with none, UI automation names the panel after the label standing
            // before the page on KeePass's dialog, which is "UUID:".
            AccessibleName = GrantTab.PanelLabel;

            // No scaling mode of its own. The page is scaled once, as it is attached, by as much as
            // the dialog around it was; a font mode here, with nothing to measure against, did
            // nothing then and scaled the panel a second time on any later change of font.
            Padding = new Padding(Spacing.Gap);

            _monospace = MaskFont(Font);
            _refusedMask = new Font(_monospace, FontStyle.Strikeout);
            _tips = new ToolTip { AutoPopDelay = 32000, InitialDelay = 300, ReshowDelay = 100 };

            // **Both of these are as tall as what is in them.** They were written down as 32 and
            // 44 pixels, which is two guesses at how many lines a sentence takes at a font size
            // and a tab width neither of them knows: too few and the last line is cut, too many
            // and the table below loses the difference for nothing. Sized to their text, one line
            // takes one line and three take three.
            _scope = new TextLabel
            {
                Name = GrantTab.ScopeLabelName,
                Text = scope ?? string.Empty,
                AutoSize = true,
                Dock = DockStyle.Top,

                // Under it, because the table is what comes next. The scope is a sentence about
                // the table and not its heading.
                Padding = new Padding(0, 0, 0, Spacing.Gap),
            };

            _warning = new TextLabel
            {
                Name = GrantTab.WarningLabelName,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, Spacing.Gap),
                ForeColor = Color.Firebrick,

                // **Nothing at all when there is nothing to say.** Hidden rather than empty: a
                // docked control that is not visible is left out of the layout entirely, so the
                // band it would take costs the table nothing on the visits where no rule is
                // broken, which is most of them.
                Visible = false,
            };

            _sealed = new CheckBox
            {
                Name = GrantTab.SealCheckBoxName,
                Text = GrantTab.SealCheckBoxText,
                AutoSize = true,
                Dock = DockStyle.Bottom,

                // Three marks to show and two to press: the mixed mark is shown where copies of
                // the database set the switch differently, and a press only ticks or clears, so
                // the box takes no mark of its own from a click and the sheet decides.
                ThreeState = true,
                AutoCheck = false,
            };

            // A few pixels of air, so the box does not read as the table's last row: a strip of
            // its own between the two rather than a padding of the box's, which the box's color
            // fills, so a box in the mixed mark or changed takes its color on its own line and not
            // up against the table. A strip rather than a margin, because a docked control is laid
            // out edge to edge and its margin is not consulted at all. The strip lends the table
            // what the table's last row lacks, `Lend`.
            _air = new Panel { Dock = DockStyle.Bottom, Height = _band };

            _grid = BuildGrid();

            Controls.Add(_grid);
            Controls.Add(_air);
            Controls.Add(_sealed);
            Controls.Add(_warning);
            Controls.Add(_scope);

            _sealed.Click += (sender, e) => WindowHandlers.Run("the box could not be pressed", PressBox);

            // The help of the box's accessible object is the box's whole tooltip, as its
            // description is, where the object gives no help unanswered. UI automation, through
            // its own proxy for a Windows button, reads the help off the tooltip's window instead,
            // with the tooltip's breaks and marks and cut after 127 characters, measured: nothing
            // on the box changes that reading.
            _sealed.QueryAccessibilityHelp += (sender, e) => e.HelpString = _sealed.AccessibleDescription;

            Populate();

            // Last, once there is a whole panel to tell, for the reason the KPG tab on the database
            // settings gives: the notice outlives the dialog, and a constructor that failed after
            // subscribing would leave a panel nobody disposes listening to it.
            if (_admissions != null)
            {
                _admissions.Landed += OnAdmitted;
            }
        }

        /// <summary>
        /// A record landed, and with it perhaps a profile the database did not name before.
        /// </summary>
        /// <remarks>
        /// Raised on the user interface thread, inside the write it follows, so this reads again
        /// directly. Disposal is the only thing asked about, as on the database settings' tab.
        /// </remarks>
        private void OnAdmitted(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                Resync();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A table that could not redraw is a table brought forward again. The record is
                // written and the client is being answered.
                _log.Warn("the rules could not be read again after a record landed: " + error.Message);
            }
        }

        /// <summary>
        /// The rows on screen, in order, as text: the profile, the mask and the word of the State
        /// column where the row has one. For tests and for automation.
        /// </summary>
        public IList<string> Lines
        {
            get
            {
                var lines = new List<string>();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (!row.IsNewRow)
                    {
                        var state = TextOf(row, StateColumn);
                        lines.Add(TextOf(row, ProfileColumn) + " " + TextOf(row, MaskColumn)
                                  + (state.Length == 0 ? string.Empty : " " + state));
                    }
                }

                return lines;
            }
        }

        /// <summary>What the tab is saying went wrong, or null. Never a database's own content.</summary>
        public string Warning
        {
            get { return _warning.Visible ? _warning.Text : null; }
        }

        /// <summary>
        /// Whether a picker is showing. A second one is never opened over the first.
        /// </summary>
        /// <remarks>
        /// Public because it is the one thing about a dropdown a test can ask without a pointer,
        /// and because what it guards is a real gesture: a cell pressed twice in a row.
        /// </remarks>
        public bool Picking
        {
            get { return _open != null; }
        }

        /// <summary>
        /// Types one key into whichever picker is open. False when nothing took it.
        /// </summary>
        /// <remarks>
        /// Public because a test cannot type into a dropdown, and because what the keyboard does
        /// here is a decision rather than a rendering: `R` checks read, Enter takes what is
        /// checked, Escape leaves the rule where it was.
        /// </remarks>
        public bool Type(Keys key)
        {
            var open = _open;
            return open != null && open.Press(key);
        }

        /// <summary>Closes whatever picker is open, exactly as clicking away does.</summary>
        public void ClosePicker()
        {
            var open = _open;
            if (open == null)
            {
                return;
            }

            try
            {
                open.Close();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // Let go of as every other failure here lets go of a picker: forgotten alone, it
                // left the dialog's Enter and Escape suspended and the dropdown undisposed, while
                // the panel read as picking nothing. A close that threw after its deferred finish
                // was queued releases the keys a second time there, which does nothing.
                _log.Warn("a grant picker could not be closed: " + error.Message);
                Abandon(open, open.Held ?? new NothingHeld());
            }
        }

        /// <summary>The keys of a picker that never took any.</summary>
        private sealed class NothingHeld : IDisposable
        {
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Chooses a rule, exactly as choosing it in the rule picker does.
        /// </summary>
        /// <remarks>
        /// Public for the same reason the tab's other gestures are: `Button.PerformClick` does
        /// nothing on a form that was never shown, silently, so a test that pressed a button would
        /// assert against a table nobody touched. What the panel does is reachable without a window.
        /// </remarks>
        public string SetGrant(string profile, string mask)
        {
            var complaint = _sheet.Set(profile, mask);
            if (complaint == null)
            {
                Pressed(profile);
            }

            return complaint;
        }

        /// <summary>Presses the box, exactly as clicking the box does.</summary>
        /// <remarks>
        /// A press only ticks or clears, and the sheet decides which: a cleared box and a box in
        /// the mixed mark are ticked, a ticked box and a lost box are cleared.
        /// </remarks>
        public void PressBox()
        {
            if (!_populating && _sheet.PressBox())
            {
                Pressed(null);
            }
        }

        /// <summary>
        /// Reads the node's rules, what arrives and the database's profiles again, and redraws the
        /// table: a row nobody pressed follows the reading, a row pressed keeps its press.
        /// </summary>
        /// <remarks>
        /// Asked when a record lands, which is when a pairing may have named a profile, and a
        /// write of the plugin's own may have brought versions of the node's rules. Public for the
        /// reason `SetGrant` is.
        /// </remarks>
        public void Resync()
        {
            if (Picking || _again == null)
            {
                return;
            }

            var reading = _again();
            if (reading == null)
            {
                return;
            }

            _sheet.Rebase(reading.Rule, reading.Above, reading.Names, reading.ReadOnlyReason);
            Populate();
        }

        /// <summary>A press the sheet took: counted, and the table drawn again on the profile pressed.</summary>
        private void Pressed(string profile)
        {
            _pressed++;
            Populate(profile);
        }

        /// <summary>
        /// Opens the picker for a row, which is what clicking its rule does.
        /// </summary>
        /// <remarks>
        /// A dropdown of check boxes rather than typing, because a mask is eight positions and
        /// choosing what a profile may do is a list of decisions rather than a string. The mask is
        /// what the cell then shows, because that is what reads down a column.
        /// </remarks>
        public void OpenPicker(int rowIndex)
        {
            if (Picking || !_sheet.CanEdit || rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            {
                return;
            }

            var row = _grid.Rows[rowIndex];
            var shown = RowOf(row);
            if (row.IsNewRow || shown == null || !shown.TakesRule)
            {
                // A version of a rule in several versions and a limit offer their X and no list,
                // `STORE.md` §657: no single press on such a row grants more than a version held.
                return;
            }

            var profile = shown.Profile;

            // A complaint of an earlier press goes, and what the tab always tells stays.
            Warn(_sheet.Warning);

            var held = VerbMask.Read(TextOf(row, MaskColumn)) ?? new List<AclVerb>();

            var list = new CheckedListBox
            {
                Name = GrantTab.RulePickerName,
                AccessibleName = GrantTab.RulePickerLabel,
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
            };

            // No height or width set here. `ToolStripControlHost` sizes what it hosts to the
            // list's own preferred size, so anything written here is computed and thrown away:
            // a width of 330 came back as 80, and a height that looked right only because the
            // number in it happened to equal this machine's `ItemHeight`. On a machine where
            // that differs the arithmetic was wrong and harmless, which is the worst way for a
            // number to be wrong. It reads as though something depends on it.

            foreach (var verb in AclVerbs.Grantable)
            {
                list.Items.Add(Choice(verb), held.Contains(verb));
            }

            // Escape means "as it was", so the closing handler has to know which way it was closed.
            var discarded = false;

            // And Enter means "this", whatever the row shows: the rule chosen goes to the sheet,
            // which holds no change where the choice gives what the store holds already.
            var confirmed = false;

            // What it looked like when it opened. Clicking away is how a dropdown is usually
            // left, and on an inherited row every box is already checked from the rule above, so
            // without this a glance at one writes a local rule nobody asked for. That rule
            // shadows the parent for good, so narrowing the parent later leaves this node wide.
            //
            // **Enter is no glance.** On a row this node's box refuses, the same check marks are
            // the only way to let the rule above through; on a lost row an empty rule is a rule
            // that replaces the lost version; and on the row an X left, the rule the X took off
            // chosen again takes the X back. Clicking away still chooses nothing there.
            var before = VerbMask.Of(held);

            var drop = new Picker(key =>
            {
                if (key == Keys.Enter || key == Keys.Escape)
                {
                    discarded = key == Keys.Escape;
                    confirmed = key == Keys.Enter;
                    return true;
                }

                return Toggle(list, key);
            });

            drop.Items.Add(new ToolStripControlHost(list)
            {
                Name = GrantTab.PickerHostName,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            });

            // Enter and Escape belong to the picker while it is open. Without this the form's own
            // accept and cancel buttons take them, and choosing verbs then presses OK or Cancel on
            // the group dialog, which closes it with the rule half made.
            var keys = DialogKeys.Suspend(_grid);

            drop.Closed += (sender, e) => Finish(drop, keys, () =>
            {
                // Escape leaves the rule where it was. Everything else takes what is checked:
                // pressing Enter, and clicking away, which is how a dropdown is usually left.
                if (discarded)
                {
                    return;
                }

                var chosen = new List<AclVerb>();
                for (var i = 0; i < AclVerbs.Grantable.Count; i++)
                {
                    if (list.GetItemChecked(i))
                    {
                        chosen.Add(AclVerbs.Grantable[i]);
                    }
                }

                var mask = VerbMask.Of(chosen);
                if (string.Equals(mask, before, StringComparison.Ordinal) && !confirmed)
                {
                    // Nothing was checked or cleared and nothing confirmed, so nothing is chosen.
                    // On a rule of this node's own that keeps a change nobody made off the sheet;
                    // on an inherited rule it is the whole difference between reading the table
                    // and rewriting it.
                    return;
                }

                // Shown, not dropped. A picker that closed on a refusal it never mentioned is a
                // control that did nothing.
                var complaint = SetGrant(profile, mask);
                if (complaint != null)
                {
                    Warn(complaint);
                }
            });

            Open(drop, list, MaskColumn, rowIndex, keys);
        }

        /// <summary>
        /// Opens the list of profiles a rule could still be written for, which is what clicking the
        /// empty line at the bottom of the table does.
        /// </summary>
        /// <remarks>
        /// A list rather than a text box, because a profile name is not something to invent here. A
        /// rule for a profile no client holds and no page names grants nobody anything and reads
        /// exactly like one that works, so a single mistyped letter is a permission quietly not
        /// given, or worse, one the operator believes they have taken away.
        ///
        /// What it offers is what is left: a profile already on the table, whether this node grants
        /// it or a group above does, is not offered again. There is one row per profile, and the
        /// way to change an inherited one is to choose a rule on the row that is already there.
        /// </remarks>
        public void OpenProfilePicker(int rowIndex)
        {
            if (Picking || !_sheet.CanEdit || rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            {
                return;
            }

            // A complaint of an earlier press goes, and what the tab always tells stays: a database
            // naming no profile is told over the table before any press, `GrantSheet.Warning`.
            Warn(_sheet.Warning);

            var free = ProfileChoices;
            if (free.Count == 0)
            {
                // Nothing at all when every profile is already on the table: a sentence telling
                // that the rows the operator is looking at are all of them tells what the rows
                // tell themselves, at the cost of lines of a page that has few to spend.
                return;
            }

            var list = new ListBox
            {
                Name = GrantTab.ProfilePickerName,
                AccessibleName = GrantTab.ProfilePickerLabel,
                BorderStyle = BorderStyle.None,
            };

            // Sized by the host, for the reason the rule picker gives, unless it holds more names
            // than its rows: `Bounded` gives the reason.
            foreach (var profile in free)
            {
                list.Items.Add(profile);
            }

            Bounded(list, Screen.FromControl(_grid).WorkingArea);
            list.SelectedIndex = 0;

            // Only an explicit choice counts. Clicking away leaves the table as it was, which is
            // the opposite of the rule picker and is right for the opposite reason: a list opens
            // with something highlighted, and taking that on the way past would write a rule for a
            // profile nobody chose.
            string chosen = null;

            var drop = new Picker(key =>
            {
                if (key != Keys.Enter && key != Keys.Escape)
                {
                    return Moved(list, key);
                }

                chosen = key == Keys.Enter ? Picked(list) : null;
                return true;
            });

            drop.Items.Add(new ToolStripControlHost(list)
            {
                Name = GrantTab.PickerHostName,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            });

            // As with the rule picker: while this is open, Enter and Escape are its own, or the
            // group dialog's OK and Cancel take them and the dialog closes mid-choice.
            var keys = DialogKeys.Suspend(_grid);

            list.Click += (sender, e) => WindowHandlers.Run("a profile could not be chosen", () =>
            {
                chosen = Picked(list);
                drop.Close();
            });

            drop.Closed += (sender, e) => Finish(drop, keys, () =>
            {
                if (chosen == null)
                {
                    return;
                }

                var complaint = AddGrant(chosen);
                if (complaint != null)
                {
                    Warn(complaint);
                    return;
                }

                // Straight on to what it may do, because choosing a profile is half a decision:
                // the rule that exists now grants nothing, and nobody opened this to say that.
                var profile = chosen;
                Later(Opened, () => OpenPicker(IndexOf(profile)));
            });

            Open(drop, list, ProfileColumn, rowIndex, keys);
        }

        /// <summary>
        /// Holds a list of more names than its rows to a whole number of rows, with room beside
        /// them for the scroll bar it then shows; a list whose names all fit is left to the host.
        /// </summary>
        /// <remarks>
        /// **The list of profile names keeps no ceiling**, `STORE.md` §616, and the host sizes what
        /// it holds to the list's own preferred size: a hundred names made a drop-down taller than
        /// the screen, cut at its edge with nothing to scroll, so the names past it could not be
        /// reached. The rows are <see cref="GrantTab.MostProfileRows"/> at most, and fewer where
        /// the working area of the table's screen, less a row for the drop-down's own edges, holds
        /// fewer. The width is the list's own and the scroll bar's beside it, so a name the list
        /// shows whole without the bound it shows whole with it, and nothing scrolls across.
        ///
        /// **Below the bound nothing is set**, so the host sizes the list exactly as it always
        /// did and the drop-down is the same pixel for pixel. A list of more names scrolls as a
        /// list box does: the keys that move the choice keep it in view, and so does the wheel.
        /// </remarks>
        private static void Bounded(ListBox list, Rectangle area)
        {
            var rows = Math.Max(1, Math.Min(GrantTab.MostProfileRows, (area.Height / list.ItemHeight) - 1));
            if (list.Items.Count <= rows)
            {
                return;
            }

            // Measured with a window of its own, as the host measures it: a list without a window
            // answers its default size rather than what its names take.
            list.CreateControl();
            var natural = list.GetPreferredSize(Size.Empty);
            var bounded = new Size(natural.Width + SystemInformation.VerticalScrollBarWidth, rows * list.ItemHeight);
            list.MinimumSize = bounded;
            list.MaximumSize = bounded;
        }

        /// <summary>The profiles the pick list would offer. Public because a test cannot click.</summary>
        public IList<string> ProfileChoices
        {
            get { return new List<string>(_sheet.Available); }
        }

        /// <summary>
        /// Starts a rule for a profile that has none here, exactly as choosing one from the list
        /// does: the profile arrives granting nothing and is opened deliberately.
        /// </summary>
        /// <returns>Null when the rule is now on the table, or one sentence saying what is wrong.</returns>
        public string AddGrant(string profile)
        {
            if (_sheet.Shows(profile))
            {
                return GrantTab.AlreadyShown;
            }

            return SetGrant(profile, VerbMask.Of(null));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // First, for the reason the KPG tab on the database settings lets go first: the
                // notice outlives every dialog this tab is on.
                if (_admissions != null)
                {
                    _admissions.Landed -= OnAdmitted;
                }

                _tips.Dispose();
                _monospace.Dispose();
                _refusedMask.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Gives the masks a monospaced font a point larger than the panel's new font, the masks
        /// that no longer arrive the same font struck through, and a bound line's bracket the room
        /// the new font needs.
        /// </summary>
        /// <remarks>
        /// Both are the panel's own, and a font set on a column or a cell is not a font it
        /// inherits: built once, at the size the panel had in its constructor, they stayed there
        /// while the headings and the profiles followed the panel's font. A panel is given a larger
        /// font by a dialog whose font is not the system's, as the page is attached, and by the
        /// scaled fit test, which measured a column as wide as its cells with the masks at the
        /// unscaled size nobody sees. Nothing happens before the table exists, because the
        /// constructor sets properties that lay the panel out.
        /// </remarks>
        protected override void OnFontChanged(EventArgs e)
        {
            WindowHandlers.Run("the rules could not take their new font", () =>
            {
                base.OnFontChanged(e);

                if (_grid == null)
                {
                    return;
                }

                var monospace = _monospace;
                var refused = _refusedMask;
                _monospace = MaskFont(Font);
                _refusedMask = new Font(_monospace, FontStyle.Strikeout);
                _grid.Columns[MaskColumn].DefaultCellStyle.Font = _monospace;

                // The masks already struck through as well, which carry the font on their own cell.
                // Asked of the cell before its style, since reading a cell's style gives it a style.
                foreach (var struck in _grid.Rows.Cast<DataGridViewRow>()
                             .Select(row => row.Cells[MaskColumn])
                             .Where(mask => mask.HasStyle && ReferenceEquals(mask.Style.Font, refused))
                             .ToList())
                {
                    struck.Style.Font = _refusedMask;
                }

                // And the room before a bound line's name, which the bracket grows into.
                foreach (var bound in _grid.Rows.OfType<RuleRow>().Where(row => row.Marks != null && row.Marks.Bound).ToList())
                {
                    bound.Cells[ProfileColumn].Style.Padding = Bracketed();
                }

                monospace.Dispose();
                refused.Dispose();
            });
        }

        /// <summary>The font a mask is drawn in, beside text in <paramref name="beside"/>.</summary>
        private static Font MaskFont(Font beside)
        {
            return new Font(FontFamily.GenericMonospace, beside.Size + 1f, FontStyle.Regular);
        }

        /// <summary>
        /// How a verb reads in the picker: its letter, then what it means.
        /// </summary>
        /// <remarks>
        /// Each switch needs a sentence rather than a name, because its name is a noun and it is not
        /// a verb: on its own it grants nothing, and what it does is widen whatever else is checked
        /// so that the same permissions reach an entry's attachments, or its protected values, and
        /// not only its other fields.
        /// </remarks>
        private static string Choice(AclVerb verb)
        {
            var text = VerbMask.LetterOf(verb) + "   " + AclVerbs.Write(verb);

            return AclVerbs.IsScope(verb)
                ? text + " " + GrantTab.SwitchApplies
                : text;
        }

        /// <summary>
        /// Checks whatever the typed letter names, which is the verb that letter stands for.
        /// </summary>
        /// <remarks>
        /// `C` is create, `R` is read, and so on down the mask, because the letter in the mask is
        /// the verb's own first letter and `VerbKeys` derives both from the same place. So the
        /// keyboard spells a rule in exactly the characters the column then shows.
        ///
        /// **Suppressed rather than passed on**, and that is not tidiness. A `CheckedListBox` reads
        /// a letter as "move to the next item beginning with it", and since every item here begins
        /// with its own letter, the built-in behavior would land on the right row and check
        /// nothing, which looks like a key that half works.
        /// </remarks>
        private static bool Toggle(CheckedListBox list, Keys key)
        {
            if ((key & Keys.Modifiers) != 0)
            {
                return false;
            }

            var letter = key & Keys.KeyCode;
            if (letter < Keys.A || letter > Keys.Z)
            {
                return false;
            }

            var position = VerbKeys.PositionOf((char)letter);
            if (position < 0 || position >= list.Items.Count)
            {
                return false;
            }

            list.SetItemChecked(position, !list.GetItemChecked(position));

            // Moved to as well as checked, so the change is where the eye already is.
            list.SelectedIndex = position;
            return true;
        }

        private static string TextOf(DataGridViewRow row, int column)
        {
            var value = row.Cells[column].Value;
            return value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>What the sheet shows on a line of the table, or null on the empty line.</summary>
        /// <remarks>
        /// Carried on the row, so that what is drawn and what a press is about cannot part: a
        /// profile can show on several rows, and each row of a group is a version of its own.
        /// </remarks>
        private static GrantRow RowOf(DataGridViewRow row)
        {
            return row.Tag as GrantRow;
        }

        /// <summary>The profile a line of the table is for, or null on the empty line.</summary>
        private static string ProfileOf(DataGridViewRow row)
        {
            var shown = RowOf(row);
            return shown == null ? null : shown.Profile;
        }

        /// <summary>
        /// A dropdown that reads the keyboard itself.
        /// </summary>
        /// <remarks>
        /// **The hosted control does not get the keys, and this is not a focus bug to work around.**
        /// While a `ToolStripDropDown` is up, `ToolStripManager.ModalMenuFilter` takes the message
        /// loop and routes keystrokes to the dropdown rather than to whatever is inside it, so a
        /// `KeyDown` handler on the list hears nothing until a click has moved the focus into it.
        /// Which is exactly what it looked like: the letters worked, but only after checking
        /// something with the pointer first.
        ///
        /// `ProcessCmdKey` is where the dropdown is offered them, so that is where they are
        /// answered. True means taken, and nothing above sees it.
        /// </remarks>
        private sealed class Picker : ToolStripDropDown
        {
            private readonly Func<Keys, bool> _pressed;

            public Picker(Func<Keys, bool> pressed)
            {
                Name = GrantTab.PickerName;
                _pressed = pressed;
                Padding = Padding.Empty;
                AutoClose = true;
            }

            /// <summary>
            /// The dialog's Enter and Escape this picker holds while it is open, so that a close
            /// that fails can still give them back. Releasing it twice releases it once.
            /// </summary>
            public IDisposable Held { get; set; }

            /// <summary>
            /// What one key does to this picker. True when it was taken.
            /// </summary>
            /// <remarks>
            /// Reachable without the message loop, because a test cannot type into a dropdown and
            /// what these keys do is the whole of what the picker is for.
            /// </remarks>
            public bool Press(Keys keyData)
            {
                if (_pressed == null || !_pressed(keyData))
                {
                    return false;
                }

                // Enter and Escape are answered by closing, and the closing handler is what knows
                // which of them it was. A letter merely checked something and the picker stays up.
                var key = keyData & Keys.KeyCode;
                if (key == Keys.Enter || key == Keys.Escape)
                {
                    Close();
                }

                return true;
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                var copy = message;
                var answer = WindowHandlers.Run("a key in a grant picker could not be handled", () =>
                {
                    return Press(keyData) || base.ProcessCmdKey(ref copy, keyData);
                }, true);
                message = copy;
                return answer;
            }
        }

        /// <summary>
        /// Moves the profile picker's choice for a key, as a list box moves it, keeping the row
        /// chosen in view; true where the key moved it.
        /// </summary>
        /// <remarks>
        /// **The keys reach the picker and not its list.** A drop-down opened from the table is not
        /// made active, so the focus stays on the table and every key is offered to the open
        /// drop-down, `Picker.ProcessCmdKey`, as the rule picker's letters are; the list box
        /// never sees them, and its choice could not be moved from the keyboard. So the picker
        /// moves it: Up and Down a row, Page Up and Page Down a page less a row, Home and End to
        /// either end, and a letter or a digit to the next name it starts, from the row after the
        /// choice and round. Setting the choice scrolls it into view. A key held with Alt or
        /// Control is not a move, and is left to whatever wants it.
        /// </remarks>
        private static bool Moved(ListBox list, Keys key)
        {
            var count = list.Items.Count;
            if (count == 0 || (key & (Keys.Alt | Keys.Control)) != 0)
            {
                return false;
            }

            var at = Math.Max(0, list.SelectedIndex);
            var page = Math.Max(1, (list.ClientSize.Height / list.ItemHeight) - 1);
            int next;
            switch (key & Keys.KeyCode)
            {
                case Keys.Up:
                    next = at - 1;
                    break;
                case Keys.Down:
                    next = at + 1;
                    break;
                case Keys.PageUp:
                    next = at - page;
                    break;
                case Keys.PageDown:
                    next = at + page;
                    break;
                case Keys.Home:
                    next = 0;
                    break;
                case Keys.End:
                    next = count - 1;
                    break;
                default:
                    next = Starting(list, key & Keys.KeyCode, at);
                    if (next < 0)
                    {
                        return false;
                    }

                    break;
            }

            list.SelectedIndex = Math.Max(0, Math.Min(count - 1, next));
            return true;
        }

        /// <summary>
        /// The next row after this row, and round, whose name starts with the letter or digit of
        /// this key, ignoring case; -1 where the key is neither or no name starts with it.
        /// </summary>
        private static int Starting(ListBox list, Keys key, int after)
        {
            char first;
            if (key >= Keys.A && key <= Keys.Z)
            {
                first = (char)('a' + (key - Keys.A));
            }
            else if (key >= Keys.D0 && key <= Keys.D9)
            {
                first = (char)('0' + (key - Keys.D0));
            }
            else
            {
                return -1;
            }

            var count = list.Items.Count;
            for (var step = 1; step <= count; step++)
            {
                var at = (after + step) % count;
                var name = Convert.ToString(list.Items[at], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(name) && char.ToLowerInvariant(name[0]) == first)
                {
                    return at;
                }
            }

            return -1;
        }

        /// <summary>
        /// The grid, which chooses nothing for itself.
        /// </summary>
        /// <remarks>
        /// A `DataGridView` makes its first cell current and selects it as its window is made,
        /// which is after the panel's own redraw has cleared both, so the first rule on every tab
        /// opened painted in the selection color as though somebody had chosen it. The current
        /// cell is left where the grid puts it, since that is where the keyboard starts; the
        /// selection is taken off again.
        ///
        /// **Enter and Escape are the dialog's**, as on every other page of the dialog, wherever
        /// the keyboard stands on the table and no picker is open. A `DataGridView` on .NET
        /// Framework 4.8 answers Enter on every row as its own input, a move down a row, and Escape
        /// on its empty line as its own input as well, the end of an edit no cell here ever starts,
        /// measured: left to the grid, Enter would never press OK, and Escape would leave the
        /// dialog open once the empty line is pressed. Escape handed to the dialog reaches its
        /// Cancel through the grid's own handling of a dialog key; Enter is answered as the dialog
        /// answers Enter, its accept button pressed. A picker takes both keys before they reach
        /// the table, `Picker.ProcessCmdKey`.
        ///
        /// **The columns keep their widths for the life of the dialog.** The profile's column takes
        /// the width the others leave with the room of the vertical scroll bar kept whether or not
        /// the bar shows, so a press that takes rows away, and the bar with them, moves no column;
        /// and a column held to the longest text the column can show is as wide with that text
        /// shown as without, `HoldTo`.
        /// </remarks>
        private sealed class Table : DataGridView
        {
            private readonly List<Floor> _floors = new List<Floor>();

            private bool _fitting;

            /// <summary>The column given the width the other columns leave, or null for none.</summary>
            public DataGridViewColumn Widest { get; set; }

            /// <summary>
            /// The width of the table's own border on each side, as the table draws the border.
            /// </summary>
            public int Edge
            {
                get
                {
                    switch (BorderStyle)
                    {
                        case BorderStyle.FixedSingle:
                            return 1;
                        case BorderStyle.Fixed3D:
                            return Application.RenderWithVisualStyles ? 1 : SystemInformation.Border3DSize.Width;
                        default:
                            return 0;
                    }
                }
            }

            /// <summary>
            /// The line the table draws before its first column where no row headings stand,
            /// which the room of the columns leaves out.
            /// </summary>
            private int Lead
            {
                get
                {
                    var single = AdvancedCellBorderStyle.All == DataGridViewAdvancedCellBorderStyle.Single
                                 || CellBorderStyle == DataGridViewCellBorderStyle.SingleVertical;
                    return !RowHeadersVisible && single ? 1 : 0;
                }
            }

            /// <summary>
            /// Holds a column at least as wide as the widest of these texts in the font the
            /// column's cells are drawn in, with this much beside the text, and never under the
            /// least given, at every font the table takes: a column sized to its cells is then as
            /// wide whichever of the texts the cells show, and a column of a fixed width stands at
            /// that width.
            /// </summary>
            /// <param name="drawn">
            /// The font the column's cells are drawn in beside the table's font, made afresh for
            /// each measure, or null for the table's own font.
            /// </param>
            public void HoldTo(DataGridViewColumn column, int least, int beside, IEnumerable<string> texts, Func<Font, Font> drawn)
            {
                var floor = new Floor(column, least, beside, texts, drawn);
                _floors.Add(floor);
                floor.Apply(Font);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                WindowHandlers.Run("the rules could not be shown with nothing chosen", () =>
                {
                    base.OnHandleCreated(e);
                    ClearSelection();
                });
            }

            protected override void OnFontChanged(EventArgs e)
            {
                WindowHandlers.Run("the rules could not take the table's new font", () =>
                {
                    base.OnFontChanged(e);
                    Fit();
                });
            }

            protected override void OnLayout(LayoutEventArgs e)
            {
                WindowHandlers.Run("the rules could not be laid out", () =>
                {
                    base.OnLayout(e);

                    // Laid out again where a width changed: a layout asked for inside a layout is
                    // dropped, and the scroll bars would stand as the old widths had them.
                    if (Fit())
                    {
                        base.OnLayout(e);
                    }
                });
            }

            protected override void OnColumnWidthChanged(DataGridViewColumnEventArgs e)
            {
                WindowHandlers.Run("the rules could not be laid out after a column changed width", () =>
                {
                    base.OnColumnWidthChanged(e);
                    if (e.Column != Widest)
                    {
                        Fit();
                    }
                });
            }

            protected override bool IsInputKey(Keys keyData)
            {
                return !Dialogs(keyData) && base.IsInputKey(keyData);
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (!Dialogs(keyData) || (keyData & Keys.KeyCode) != Keys.Enter)
                {
                    return base.ProcessDialogKey(keyData);
                }

                // Outside any catch of this plugin's, as the dialog presses the button itself:
                // what the button does is the dialog's.
                var form = FindForm();
                var accept = form == null ? null : form.AcceptButton;
                if (accept != null)
                {
                    accept.PerformClick();
                }

                return true;
            }

            /// <summary>Whether a key is Enter or Escape, held with neither Alt nor Control.</summary>
            private static bool Dialogs(Keys keyData)
            {
                var key = keyData & Keys.KeyCode;
                return (keyData & (Keys.Alt | Keys.Control)) == Keys.None && (key == Keys.Enter || key == Keys.Escape);
            }

            /// <summary>
            /// Holds every column to its texts in the fonts the cells are drawn in now, and gives
            /// the widest column the width the other columns leave beside the room of the vertical
            /// scroll bar, whether or not the bar shows; true where a width changed.
            /// </summary>
            /// <remarks>
            /// **The room of the columns is the table's inside less a line**: with no row headings,
            /// the table draws a line of its own before the first column, measured on .NET
            /// Framework 4.8, and columns a pixel wider than that room put a scroll bar across.
            /// </remarks>
            private bool Fit()
            {
                var widest = Widest;
                if (_fitting || !IsHandleCreated)
                {
                    return false;
                }

                _fitting = true;
                try
                {
                    var changed = false;
                    foreach (var floor in _floors)
                    {
                        changed |= floor.Apply(Font);
                    }

                    if (widest == null)
                    {
                        return changed;
                    }

                    var others = Columns.Cast<DataGridViewColumn>()
                        .Where(column => column.Visible && column != widest)
                        .Sum(column => column.Width);
                    var room = ClientSize.Width - (2 * Edge) - Lead - VerticalScrollBar.Width;
                    var width = Math.Max(widest.MinimumWidth, room - others);
                    if (widest.Width == width)
                    {
                        return changed;
                    }

                    widest.Width = width;
                    return true;
                }
                finally
                {
                    _fitting = false;
                }
            }

            /// <summary>A column held to the longest of its texts, `HoldTo`.</summary>
            private sealed class Floor
            {
                private readonly DataGridViewColumn _column;
                private readonly int _least;
                private readonly int _beside;
                private readonly string[] _texts;
                private readonly Func<Font, Font> _drawn;

                public Floor(DataGridViewColumn column, int least, int beside, IEnumerable<string> texts, Func<Font, Font> drawn)
                {
                    _column = column;
                    _least = least;
                    _beside = beside;
                    _texts = texts.ToArray();
                    _drawn = drawn;
                }

                /// <summary>
                /// Holds the column to the texts in the font the column's cells are drawn in beside
                /// this font of the table's; true where a width changed.
                /// </summary>
                /// <remarks>
                /// The font made from the table's rather than the column's own font read, since a
                /// column given a font of its own takes the font after the table has taken its own.
                /// </remarks>
                public bool Apply(Font table)
                {
                    int longest;
                    if (_drawn == null)
                    {
                        longest = Longest(table);
                    }
                    else
                    {
                        using (var font = _drawn(table))
                        {
                            longest = Longest(font);
                        }
                    }

                    var width = Math.Max(_least, longest + _beside);
                    var fixedWidth = _column.AutoSizeMode == DataGridViewAutoSizeColumnMode.None;
                    if (_column.MinimumWidth == width && (!fixedWidth || _column.Width == width))
                    {
                        return false;
                    }

                    var before = _column.Width;
                    _column.MinimumWidth = width;
                    if (fixedWidth)
                    {
                        _column.Width = width;
                    }

                    return _column.Width != before;
                }

                /// <summary>How wide the longest of the texts is in this font.</summary>
                private int Longest(Font font)
                {
                    return _texts.Max(text => TextRenderer.MeasureText(text, font).Width);
                }
            }
        }

        /// <summary>What a list has highlighted, or null when it has nothing.</summary>
        private static string Picked(ListBox list)
        {
            return list.SelectedItem == null
                ? null
                : Convert.ToString(list.SelectedItem, CultureInfo.InvariantCulture);
        }

        private static bool Any(IEnumerable<string> names)
        {
            return names != null && Enumerable.Any(names);
        }

        /// <summary>
        /// Puts a dropdown under a cell, gives it the keyboard, and remembers it is open.
        /// </summary>
        /// <remarks>
        /// A dropdown that cannot be shown is torn down here rather than left holding the dialog's
        /// accept and cancel buttons, which is a state that would leave a form nobody can close.
        /// A grid that has never been drawn has no cell rectangle, which is the ordinary way that
        /// happens and is what the panel's own methods are public for.
        ///
        /// **The row is brought into view first**, because a row that is not displayed has no
        /// rectangle either, and an empty one puts the dropdown at the grid's own origin: over the
        /// headings, next to a profile it is not about, while the rule it writes is off the bottom
        /// of the table. That is reachable without scrolling anything, since `Populate` rebuilds
        /// every row on every edit and the rule picker opens on whichever row a profile just
        /// landed on.
        /// </remarks>
        private void Open(Picker drop, Control inside, int column, int rowIndex, IDisposable keys)
        {
            try
            {
                Reveal(rowIndex);

                var cell = _grid.GetCellDisplayRectangle(column, rowIndex, false);
                if (cell.Height <= 0 || cell.Width <= 0)
                {
                    // Nowhere to put it, which is a table with no room for a row at all rather
                    // than a row that had to be scrolled to. Said and abandoned, because a
                    // dropdown at the grid's origin is a dropdown about a row nobody can see.
                    _log.Warn("a grant picker had nowhere to open: row " + rowIndex
                        + " cannot be shown");
                    Warn(GrantTab.NoRoomForRow);
                    Abandon(drop, keys);
                    return;
                }

                drop.Held = keys;
                _open = drop;
                drop.Show(_grid, new Point(cell.Left, cell.Bottom));

                // So the keyboard reaches the list rather than the grid underneath.
                inside.Select();
                return;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a grant picker could not be shown: " + error.Message);

                // Inside the catch, not after it. This class may not let anything reach
                // KeePass's message loop, and giving the dialog its keys back is itself a
                // call on a form that may be going away. Nothing has been seen to throw here;
                // the point is that the promise the class makes should not depend on that.
                Abandon(drop, keys);
            }
        }

        /// <summary>
        /// Scrolls the table until a row is on it, doing nothing when it already is.
        /// </summary>
        /// <remarks>
        /// Only when it is not, because a table that jumps under a pointer costs the reader the
        /// rows they were comparing, and the ordinary press is on a row somebody is looking at.
        ///
        /// **Upwards as well as downwards.** A profile sorting before every other one lands on the
        /// first row, and the table can be scrolled past that just as easily as it can be short of
        /// the last one.
        /// </remarks>
        private void Reveal(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || _grid.Rows[rowIndex].Displayed)
            {
                return;
            }

            _grid.FirstDisplayedScrollingRowIndex = rowIndex;
        }

        /// <summary>
        /// Puts the table back where it was after every row on it has been replaced.
        /// </summary>
        /// <remarks>
        /// `Populate` clears the rows and adds them again, which takes the grid back to the top.
        /// Without this, a rule chosen on a row that had to be scrolled to is written and scrolled
        /// away in the one gesture, leaving a table that reads exactly as it did before the edit.
        ///
        /// Its own try, because failing to scroll must not cost the rest of the redraw: the seal,
        /// the empty line and whatever the tab has to warn about all come after it.
        /// </remarks>
        private void ScrollTo(int top)
        {
            if (top <= 0 || _grid.Rows.Count == 0)
            {
                return;
            }

            try
            {
                _grid.FirstDisplayedScrollingRowIndex = Math.Min(top, _grid.Rows.Count - 1);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("the grant table could not be put back where it was: " + error.Message);
            }
        }

        /// <summary>
        /// Lets go of a picker that never opened, or of a picker whose window went while it was
        /// open, writing a failure to the log rather than throwing it.
        /// </summary>
        private void Abandon(Picker drop, IDisposable keys)
        {
            _open = null;

            try
            {
                keys.Dispose();
                drop.Dispose();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a grant picker could not be let go of: " + error.Message);
            }
        }

        /// <summary>
        /// Everything a closing picker has to do, done in the next message rather than this one.
        /// </summary>
        /// <remarks>
        /// **This is the whole reason a picker does not take the dialog down with it.** `Closed` is
        /// raised from inside the dropdown's own `SetVisibleCore`, while WinForms is still
        /// delivering the click, the keystroke or the change of activation that closed it, and
        /// `SetVisibleCore` asks the dropdown for its handle once the handler returns. Disposing
        /// the dropdown there throws from inside the message loop, which `GrantPickerTests`
        /// reproduces, and an exception there is not a message on the tab: it is KeePass closing
        /// the dialog with the rule half made, or ending altogether.
        ///
        /// Two more things wait with it for the same reason, and neither has been made to fail:
        /// rebuilding the grid would replace the row the click arrived on, and giving the dialog
        /// its accept and cancel buttons back could hand the keystroke still in flight to a
        /// button. Moved back into the handler, neither fails a test, a picker closed by a posted
        /// press elsewhere or a posted Enter included.
        ///
        /// So the handler records what was chosen and nothing else, and this runs afterwards.
        /// </remarks>
        private void Finish(Picker drop, IDisposable keys, Action work)
        {
            WindowHandlers.Run("a grant picker's close could not be handled", () =>
            {
                // Cleared at once, so a click arriving between the close and the next message opens
                // the next picker rather than being ignored as though one were still up. The
                // dialog's keys stay taken across that: the next picker's token joins this one's
                // rather than recording the buttons afresh, and `DialogKeys` gives them back only
                // when the last of the two is let go, whichever that is.
                _open = null;

                // **A picker can outlive its window.** Every way an operator has of leaving the
                // dialog closes the picker first. A dialog closed in code, by a close another
                // program posts to it, goes with the picker still open, and WinForms closes the
                // picker at the next change of activation, after the dialog and this panel have
                // gone. The choice is dropped then: there is no table to show it on, and the
                // dialog's close has handed the sheet to the writer or thrown the sheet away
                // already.
                if (!IsHandleCreated)
                {
                    AbandonLater(drop, keys);
                    return;
                }

                // A post refused here leaves the dialog's keys taken and the dropdown undisposed,
                // and nothing is done about either from here, because this runs inside the
                // dropdown's own `Closed` and both are what that handler must not do: the reason
                // this is posted at all. With the panel's window there, nothing has been seen to
                // refuse a post.
                Later(Closed, () =>
                {
                    using (drop)
                    {
                        var pressed = _pressed;
                        try
                        {
                            keys.Dispose();
                            work();
                        }
                        catch (Exception error) when (Expected.OfUserInterface(error))
                        {
                            // Named rather than swallowed whole, and reported on the tab, because
                            // an ordinary failure of the user interface is no defect for
                            // `WindowHandlers` to record, and the operator at this tab is the one
                            // to tell. The sentence tells which happened: a choice the sheet took
                            // before the redraw failed is written at OK.
                            _log.Error("a grant picker could not be closed cleanly", error);
                            Warn(_pressed > pressed ? GrantTab.ChoiceNotDrawn : GrantTab.ChoiceNotTaken);
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Lets go of a picker whose panel has no window, in a message posted to the dropdown's own
        /// window.
        /// </summary>
        /// <remarks>
        /// **Still not from inside its `Closed`**, which is where this is asked from:
        /// `SetVisibleCore` asks the dropdown for its handle once that handler returns, and a
        /// dropdown disposed by then throws from `ToolStripManager.ModalMenuFilter`'s message hook,
        /// past every handler, which KeePass answers with its fatal error and an exit. The
        /// dropdown still has its window when it is closed after its dialog has gone, measured on
        /// a shown form closed under an open picker, and keeps it until it is disposed. A dropdown
        /// with no window either is left undisposed and written to the log, at the cost of a hidden
        /// window.
        ///
        /// **Not disposed as the panel goes instead**, which was measured too: a dropdown disposed
        /// while open stays on `ModalMenuFilter`'s list, is closed at the next change of activation
        /// all the same, and throws there for the same reason.
        /// </remarks>
        private void AbandonLater(Picker drop, IDisposable keys)
        {
            try
            {
                drop.BeginInvoke(new Action(() =>
                    WindowHandlers.Run("a grant picker could not be let go of", () => Abandon(drop, keys))));
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a grant picker whose dialog went could not be let go of: " + error.Message);
            }
        }

        /// <summary>
        /// Draws the Permissions cell of the empty line as unavailable, because it is.
        /// </summary>
        /// <remarks>
        /// A rule is a profile first. Until one is chosen there is nothing for permissions to be
        /// about, and a cell that looks like the ones above it but does nothing when pressed is
        /// worse than a cell that looks unavailable: the first is a fault, the second is an
        /// instruction. Pressing it does nothing at all, which is what unavailable means.
        /// </remarks>
        private void DimTheEmptyLine()
        {
            if (!_grid.AllowUserToAddRows || _grid.NewRowIndex < 0)
            {
                return;
            }

            var row = _grid.Rows[_grid.NewRowIndex];

            var adding = row.Cells[MaskColumn];
            adding.Style.ForeColor = SystemColors.GrayText;
            adding.Style.BackColor = SystemColors.Control;
            adding.Style.SelectionForeColor = SystemColors.GrayText;
            adding.Style.SelectionBackColor = SystemColors.Control;
            adding.ToolTipText = Tooltips.Wrapped(GrantTab.MaskNeedsAProfile);

            // The State cell beside it as well, for the same reason: no rule, so no state.
            var state = row.Cells[StateColumn];
            state.Style.BackColor = SystemColors.Control;
            state.Style.SelectionBackColor = SystemColors.Control;

            // And nothing in the last column, because there is no rule on this line to remove.
            // `OnCellPainting` is what keeps the button from being drawn at all; this is the color
            // of the space that leaves, which is the gray the permissions cell is wearing to the
            // left, for the same reason and on the same line.
            var forget = (DataGridViewButtonCell)row.Cells[ForgetColumn];
            forget.Value = string.Empty;
            forget.Style.BackColor = SystemColors.Control;
            forget.Style.SelectionBackColor = SystemColors.Control;
            forget.ToolTipText = string.Empty;
        }

        /// <summary>Which row a profile is on, or -1 when the table has no row for it.</summary>
        private int IndexOf(string profile)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!row.IsNewRow
                    && string.Equals(ProfileOf(row), profile, StringComparison.Ordinal))
                {
                    return row.Index;
                }
            }

            return -1;
        }

        /// <summary>What `Later` is told a picker's work does as the picker opens, for the log and the tab.</summary>
        private const string Opened = "opened";

        /// <summary>The same as the picker closes.</summary>
        private const string Closed = "closed";

        /// <summary>
        /// Runs something once the current message is finished with.
        /// </summary>
        /// <remarks>
        /// One dropdown opens the next, and opening it from inside the first one's closing handler
        /// would be showing a window while another is being torn down.
        ///
        /// **Never at once, not even with no window to post to.** It ran straight through then, on
        /// the belief that only a test came that way. What came that way was a picker closed after
        /// its dialog had gone, whose work then disposed it inside its own `Closed`. A post refused
        /// for want of a window is now written to the log, and `Finish` lets go of such a picker
        /// itself.
        ///
        /// **Guarded where it runs, and not only where it is posted.** Posted work runs later on
        /// KeePass's message loop, after the try around the post has long returned, and a failure
        /// of a kind the user interface throws gets a sentence on this tab there, while anything
        /// else is a defect, which `WindowHandlers` records, since the work runs through it. Nothing has been seen to throw there; the point is that the
        /// promise this class makes should not depend on that.
        /// </remarks>
        /// <param name="done">
        /// What the work does to a picker, "opened" or "closed", for the sentence a failure leaves:
        /// both go through here, and a picker that would not close reported as a picker that would
        /// not open sends the reader after the wrong half.
        /// </param>
        private void Later(string done, Action work)
        {
            Action guarded = () => WindowHandlers.Run("a grant picker could not be " + done, () =>
            {
                try
                {
                    work();
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("a grant picker could not be " + done, error);
                    Warn(done == Opened ? GrantTab.PickerNotOpened : GrantTab.PickerNotClosed);
                }
            });

            try
            {
                BeginInvoke(guarded);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a grant picker could not be " + done + ": " + error.Message);
            }
        }

        private DataGridView BuildGrid()
        {
            var grid = new Table
            {
                Name = GrantTab.GridControlName,

                // A phrase to be read aloud rather than the name, which is a handle for tests: the
                // table has no text of its own, and without this the whole page was announced by
                // its control type alone.
                AccessibleName = GrantTab.GridLabel,
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,

                // The heading row is as tall as what is written in it. A `DataGridView` ships with
                // 23 pixels written down and a font that follows the desktop's scaling, so on a
                // display at 200 percent the headings are taller than the row they are drawn in
                // and lose their tops. The row gives way rather than the text: shrinking the font
                // to fit a fixed height would make this table the one control on the tab reading
                // smaller than everything around it, and it would do it only on the displays where
                // the text was already hardest to read.
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,

                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
            };

            var profile = new DataGridViewTextBoxColumn
            {
                Name = GrantTab.ProfileColumnName,
                HeaderText = GrantTab.ProfileColumnHeader,

                // The only column with nothing known about what it holds, so it takes whatever the
                // others leave beside the room of the vertical scroll bar, `Table`. A profile name
                // is as long as somebody made it.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,

                // Nothing in this table sorts. The rows are already in an order somebody chose:
                // by profile, with the inherited ones among the local ones rather than under
                // them, because a reader comparing two profiles wants them next to each other.
                // A click that reordered them would take that arrangement away and give back an
                // alphabetical list of things that are not all the same kind.
                SortMode = DataGridViewColumnSortMode.NotSortable,

                // Never typed into either, and for a stronger reason than the mask beside it. A
                // typed name is a rule for a profile that may not exist, which grants nobody
                // anything and looks exactly like one that does; a misspelling is silent and the
                // table cannot tell the operator, because a profile it has never heard of is also
                // what a rule written before the client paired looks like. Chosen from the list,
                // the name is one this database names.
                ReadOnly = true,
                ToolTipText = Tooltips.Wrapped(GrantTab.ProfileColumnHelp),
            };

            // What the empty line at the bottom says. Set on the column rather than written into
            // the row, because writing into the new row is what turns it into a real one.
            profile.DefaultCellStyle.NullValue = GrantTab.AddRowPrompt;
            profile.DefaultCellStyle.ForeColor = SystemColors.ControlText;

            var mask = new DataGridViewTextBoxColumn
            {
                Name = GrantTab.MaskColumnName,
                HeaderText = GrantTab.MaskColumnHeader,

                // As wide as the widest thing in it, heading included. The heading is the longer
                // of the two and is what the letters below it are explained by, so a column that
                // left it out drew a word nobody could read over a column of masks nobody had a
                // word for. What that costs the profile column beside it is the difference between
                // "Permissions" and eight characters, which is tens of pixels rather than the
                // stranded space leaving the heading out was avoiding.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,

                // And no sorting here either, for a reason of its own: a mask is a set written
                // down in a fixed order, so one is not more than another and putting them in
                // order sorts by whichever verb happens to come first. Saying so also gets the
                // width back that a sort glyph reserves beside the heading, which on this column
                // is the difference between the heading fitting and the column being padded out
                // to hold an arrow nobody can press.
                SortMode = DataGridViewColumnSortMode.NotSortable,

                // Never typed into. A mask is eight positions in a fixed order and typing one is
                // a way to write a rule nobody meant: the letters out of order, the wrong case,
                // seven characters, nine. The cell is a place the picker opens from, and the picker
                // is where a rule is made.
                ReadOnly = true,
                ToolTipText = Tooltips.Wrapped(GrantTab.MaskColumnHelp),
            };

            mask.DefaultCellStyle.Font = _monospace;

            // Left, so that every mask starts in the same place as the one above it. Centering
            // eight fixed width characters in a column that is not eight characters wide moves the
            // letters about between rows, and the whole point of a mask is that the eye reads down
            // a column of them: `C` is always first and `P` is always last.
            //
            // No floor written down for this column: the floor is measured off a mask in the
            // column's own font, `Table.HoldTo` below, since a mask may be wider than the heading
            // and a table with no rule yet is to be as wide as a table of rules.
            mask.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            mask.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;

            // **A state has a column of its own**, `STORE.md` §657, and is never joined to a name
            // or a mask: a word that the cell's tooltip explains, and that the cell's accessible
            // description carries for a screen reader. A word a column wide, so the column is as
            // wide as its longest word and heading, as the Permissions column is.
            var state = new DataGridViewTextBoxColumn
            {
                Name = GrantTab.StateColumnName,
                HeaderText = GrantTab.StateColumnHeader,
                CellTemplate = new StateCell(),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                ToolTipText = Tooltips.Wrapped(GrantTab.StateColumnHelp),
            };

            var forget = new DataGridViewButtonColumn
            {
                Name = GrantTab.ForgetColumnName,

                // The name of every X under it, and never drawn. The heading is a bin, painted in
                // `OnCellPainting`, and the column is sized to its cells alone, since a column
                // sized to its heading as well would be as wide as these words.
                HeaderText = GrantTab.ForgetColumnHeader,
                Text = ForgetMark,

                // Off, so that a cell can say nothing. With it on, every button in the column
                // draws the column's own text whatever the cell holds, and the row that has
                // nothing to remove offers to remove it anyway.
                UseColumnTextForButtonValue = false,

                // One character or none, so the mark is all there is to fit, and the column stands
                // at the width of the bin or of the X, whichever is wider, whether or not a row
                // carries an X: `Table.HoldTo` below.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                ToolTipText = Tooltips.Wrapped(GrantTab.ForgetColumnHelp),
            };

            // One line, so that words nobody sees do not make the heading row taller.
            forget.HeaderCell.Style.WrapMode = DataGridViewTriState.False;

            // Every row a row that tells a screen reader whether the row changed on the tab.
            grid.RowTemplate = new RuleRow();

            grid.Columns.Add(profile);
            grid.Columns.Add(mask);
            grid.Columns.Add(state);
            grid.Columns.Add(forget);

            // Widths that no press moves, `Table`: the profile's column takes what the others
            // leave, the Permissions column is as wide as a mask whether or not a row shows a
            // mask, the State column as its longest word whichever words the rows show, and the
            // last column as the bin or the X. The room beside each text is more than a cell of
            // each kind draws around its text.
            grid.Widest = profile;
            grid.HoldTo(mask, mask.MinimumWidth, Spacing.Tight, new[] { VerbMask.Letters }, MaskFont);
            grid.HoldTo(state, state.MinimumWidth, Spacing.Gap, StateWords, null);
            grid.HoldTo(forget, GateIcon.Room, 2 * Spacing.Gap, new[] { ForgetMark }, null);

            // No cell-value handler, because no column can be typed into: each is `ReadOnly`, and
            // the only writes to them are this panel's own redraw. A rule is
            // chosen from the picker rather than typed, which is the decision the table is
            // built around, and `FINDINGS.md` records why automation cannot type into one
            // either. A read-only cell exposes no pattern that operates it, so the end to end
            // test sends real keystrokes at the picker instead.
            grid.CellClick += (sender, e) =>
                WindowHandlers.Run("a press on the rules could not be carried out", () => OnCellClick(sender, e));
            grid.KeyDown += (sender, e) =>
                WindowHandlers.Run("a key on the rules could not be handled", () => OnGridKey(sender, e));
            grid.CellPainting += (sender, e) =>
                WindowHandlers.Run("the rules could not be drawn", () => OnCellPainting(sender, e));
            grid.DataError += (sender, e) =>
                WindowHandlers.Run("a value on the rules could not be refused", () => e.Cancel = true);

            return grid;
        }

        private void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            Press(e.RowIndex, e.ColumnIndex);
        }

        /// <summary>
        /// Draws a cell in the last column as the space it is wherever there is nothing to remove,
        /// and the marks of a line in the other columns, `PaintMarks`.
        /// </summary>
        /// <remarks>
        /// A button column paints a button on every cell it owns whatever that cell has been
        /// given. An empty value takes the caption away and leaves the button; `FlatStyle.Flat`
        /// takes the raised edges away and leaves a frame. Either way a line with nothing to
        /// remove is a line carrying a box around nothing, which reads as a control that is broken
        /// rather than one that is not available.
        ///
        /// So the cell is painted here instead, as its background and the table's own line and
        /// nothing else: the button lives in the content parts, and what is never asked for is
        /// never drawn.
        ///
        /// **Keyed on the mark rather than on which line it is**, because two lines have nothing
        /// to remove and they are unavailable for different reasons. The empty line has no rule
        /// yet; an inherited line has one that belongs to a group above, which is what its own
        /// column heading says. The one thing they share is the thing this draws.
        /// </remarks>
        private void OnCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex != ForgetColumn)
            {
                // A fault drawing the marks is recorded once, by the handler around this, and the
                // table is drawn by the grid alone until it is filled again: a fault in a paint
                // is a fault in every cell of every repaint, and the words, the tooltips and the
                // grid's own drawing tell every state without the marks.
                if (_marksFailed)
                {
                    return;
                }

                var drawn = false;
                try
                {
                    PaintMarks(e);
                    drawn = true;
                }
                finally
                {
                    _marksFailed = !drawn;
                }

                return;
            }

            // The heading is a bin rather than a word. What the column is for is one idea, and a
            // picture carries it in the width the column already has; a word would be a word the
            // column then has to be wide enough for, on a table whose other two are the ones with
            // something to say. The words it holds are its name to a screen reader, and are never
            // drawn, bin or no bin.
            if (e.RowIndex < 0)
            {
                e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
                e.Handled = true;

                var bin = GateIcon.Bin;
                if (bin != null)
                {
                    e.Graphics.DrawImageUnscaled(
                        bin,
                        e.CellBounds.Left + ((e.CellBounds.Width - bin.Width) / 2),
                        e.CellBounds.Top + ((e.CellBounds.Height - bin.Height) / 2));
                }

                return;
            }

            var mark = e.Value as string;
            if (!string.IsNullOrEmpty(mark))
            {
                return;
            }

            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            e.Handled = true;
        }

        /// <summary>
        /// Draws a line's bracket over its profile's cell, and its mask a letter at a time, in bold
        /// where the lines of its group differ.
        /// </summary>
        /// <remarks>
        /// **Every mask is drawn here, bold letters or none**, so that the letters of every line
        /// stand in the same places and a bold letter reads as the only difference between two
        /// masks. The cell paints its background, border, selection and focus as ever, and only
        /// its text is drawn here.
        ///
        /// **The bracket in the table's own text color**, the same on every line of a group, so the
        /// gray of an inherited rule bound to its limit does not break the bracket in two; in a
        /// selected cell, the selection's text color, the same on every kind of line.
        /// </remarks>
        private void PaintMarks(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            {
                return;
            }

            var marks = (_grid.Rows[e.RowIndex] as RuleRow)?.Marks;
            if (marks == null)
            {
                return;
            }

            var selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var rightToLeft = _grid.RightToLeft == RightToLeft.Yes;

            if (e.ColumnIndex == ProfileColumn && marks.Bound)
            {
                e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
                marks.DrawBracket(
                    e.Graphics,
                    e.CellBounds,
                    selected ? _grid.DefaultCellStyle.SelectionForeColor : _grid.DefaultCellStyle.ForeColor,
                    _grid.Font,
                    rightToLeft);
                e.Handled = true;
                return;
            }

            var mask = e.Value as string;
            if (e.ColumnIndex == MaskColumn && !string.IsNullOrEmpty(mask))
            {
                e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
                marks.DrawMask(
                    e.Graphics,
                    e.CellBounds,
                    e.CellStyle,
                    mask,
                    selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor,
                    rightToLeft);
                e.Handled = true;
            }
        }

        /// <summary>
        /// What a press on one cell does, which is the whole of what this table can be told.
        /// </summary>
        /// <remarks>
        /// Public for the reason the rest of the panel's gestures are: a click cannot be delivered
        /// to a control that was never drawn, so a test that tried would assert against a table
        /// nobody touched. What each cell does is a decision, and decisions are reachable here.
        /// </remarks>
        public void Press(int rowIndex, int columnIndex)
        {
            if (_populating || rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            {
                return;
            }

            if (_grid.Rows[rowIndex].IsNewRow)
            {
                // The profile column only. Permissions on this line has nothing to be about yet,
                // and it is drawn as unavailable, so pressing it does nothing rather than
                // explaining itself: a rule is a profile first.
                if (columnIndex == ProfileColumn)
                {
                    OpenProfilePicker(rowIndex);
                }

                return;
            }

            if (columnIndex == MaskColumn)
            {
                OpenPicker(rowIndex);
                return;
            }

            if (columnIndex == ProfileColumn)
            {
                // Nothing. A row's profile is what its rule is kept under, and changing it in place
                // would be two edits wearing one gesture: the old profile silently loses its rule
                // and the new one silently gains whatever was in the row. Remove it with the X and
                // choose again, which is two deliberate acts and reads as two on the table.
                return;
            }

            if (columnIndex == ForgetColumn)
            {
                Forget(RowOf(_grid.Rows[rowIndex]));
            }
        }

        /// <summary>
        /// The X on a row: the limit settled on a limit row, the version removed on a row bound
        /// with other versions, and the rule taken off on a row standing alone.
        /// </summary>
        private void Forget(GrantRow row)
        {
            if (row != null && _sheet.Forget(row))
            {
                Pressed(row.Profile);
            }
        }

        /// <summary>
        /// The keyboard's way into the same two pickers the mouse opens.
        /// </summary>
        /// <remarks>
        /// Neither column can be typed into, so without this the tab needs a mouse, which a settings
        /// page inside a password manager does not get to require.
        ///
        /// **F2 and Alt+Down, and deliberately not Enter.** The picker takes Enter once it is open,
        /// but until then the grid is on somebody else's dialog and Enter is that dialog's OK: a
        /// key that opened a picker here would be a key that closed the group dialog when the table
        /// happened not to have the focus.
        /// </remarks>
        private void OnGridKey(object sender, KeyEventArgs e)
        {
            var opens = e.KeyCode == Keys.F2 || (e.Alt && e.KeyCode == Keys.Down);
            if (!opens || _grid.CurrentCell == null)
            {
                return;
            }

            var rowIndex = _grid.CurrentCell.RowIndex;
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (_grid.Rows[rowIndex].IsNewRow)
            {
                OpenProfilePicker(rowIndex);
                return;
            }

            OpenPicker(rowIndex);
        }

        private void Warn(string text)
        {
            _warning.Text = text ?? string.Empty;
            _warning.Visible = !string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// Rebuilds the table from the sheet, scrolled where it was, and brings the box and the
        /// warning beside it up to date.
        /// </summary>
        /// <param name="settle">
        /// The profile a press was about, whose rule the keyboard is on once the table is rebuilt,
        /// or null to leave it where it was.
        /// </param>
        private void Populate(string settle = null)
        {
            _populating = true;
            try
            {
                // Read before the rows go, because clearing them is what loses both.
                var top = _grid.FirstDisplayedScrollingRowIndex;
                var was = Where();

                _grid.Rows.Clear();

                var editable = _sheet.CanEdit;
                var rows = _sheet.Rows;
                var marks = GrantMarks.Of(rows, editable);
                _marksFailed = false;
                for (var at = 0; at < rows.Count; at++)
                {
                    // No X on a table that cannot be changed: pressing it would remove nothing,
                    // and a button that does nothing is a button somebody presses again.
                    var row = rows[at];
                    var mark = row.Removable && editable ? ForgetMark : string.Empty;
                    var index = _grid.Rows.Add(row.Profile, row.Mask, GrantTab.WordOf(row), mark);
                    var added = _grid.Rows[index];
                    added.Tag = row;
                    Draw(row, added, editable, marks[at]);
                }

                ScrollTo(top);

                var box = _sheet.Box;
                _sealed.CheckState = box == GrantBox.Cleared
                    ? CheckState.Unchecked
                    : box == GrantBox.Mixed ? CheckState.Indeterminate : CheckState.Checked;
                _sealed.Enabled = editable;
                Tint();
                var boxTip = GrantTab.BoxTip(box, _sheet.BoxChanged, editable);
                Tooltips.Set(_tips, _sealed, boxTip);
                _sealed.AccessibleDescription = boxTip;

                // The table stays enabled when nothing on it can be changed, because a disabled grid
                // takes neither its scroll bar nor the wheel, and the rules past the rows that fit
                // were out of reach on the very tabs that promise the rules stay readable: an
                // entry's earlier version, several entries at once. What stops a change is what
                // stops a change everywhere else, the sheet refusing, and neither picker opens on a
                // sheet that cannot change. Nor is the empty line offered where the database names
                // no profile, since no profile can be chosen on the line: the tab tells so over
                // the table.
                _grid.AllowUserToAddRows = editable && Any(_sheet.Offered);

                // The heading's instruction only where a rule can be chosen.
                _grid.Columns[MaskColumn].ToolTipText = Tooltips.Wrapped(
                    editable ? GrantTab.MaskColumnHelp : GrantTab.MaskColumnReadOnlyHelp);

                DimTheEmptyLine();

                // Nothing chosen, and somewhere for the keyboard to be, after the empty line has
                // come or gone. A grid selects the cell it makes current, and on an entry whose
                // only rules come from a group above that painted an inherited rule in the
                // selection color, which is exactly the color it must not be: the gray is the
                // whole of what tells the node does not set that rule. So the selection goes and
                // the current cell stays, because F2 and Alt+Down open a picker on the current
                // cell.
                Settle(settle, was);
                _grid.ClearSelection();

                // The line about where rules come from and what a rule chosen does only over a
                // table that takes a press; on a tab that takes none, the reason stands alone.
                _scope.Visible = editable;
                Warn(_sheet.Warning);
                Lend();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the grant table could not be drawn", error);
            }
            finally
            {
                _populating = false;
            }
        }

        /// <summary>
        /// Gives a line of the table the tooltips, the accessible texts and the marks of what the
        /// sheet shows on the line.
        /// </summary>
        /// <remarks>
        /// **Every state is told by its word, its tooltip and its accessible text**, and a line
        /// changed on the tab by a line of its tooltip and its accessible text, so that nothing on
        /// the tab is told by color alone. Gray tells that a rule comes from above, and a line
        /// through the mask that the rule grants nothing here; both are marks beside the word. The
        /// light yellow of a change and the light red of a group are marks beside them, and so are
        /// the bracket and the letters in bold, which `OnCellPainting` draws from the line's marks.
        /// A change no OK can write takes no yellow, `GrantMarks.Of`, and `Tint` gives the colors.
        /// </remarks>
        private void Draw(GrantRow row, DataGridViewRow added, bool editable, GrantMarks marks)
        {
            // The profile's whole name and nothing else, since a long name is cut in its column
            // and a cell with a tip of its own never shows its cut value whole: two names sharing a
            // long start drew as the same row. What a press on the cell does is the heading's.
            added.Cells[ProfileColumn].ToolTipText = Tooltips.Wrapped(row.Profile);

            var maskTip = GrantTab.MaskTip(row, editable);
            added.Cells[MaskColumn].ToolTipText = Tooltips.Wrapped(maskTip);

            var stateTip = GrantTab.StateTip(row, editable);
            var state = (StateCell)added.Cells[StateColumn];
            state.ToolTipText = Tooltips.Wrapped(stateTip);
            state.Description = stateTip;

            ((RuleRow)added).Description = row.Changed ? (editable ? GrantTab.ChangedHere : GrantTab.ChangedNotWritten) : null;

            if (row.Kind == GrantRowKind.Inherited)
            {
                // Inherited, so the node does not set the rule: shown so the table is the whole
                // answer, and grayed so the rule is not mistaken for a rule of this node's own.
                // A selected cell takes the selection's colors here as on every line, since the
                // selection is where the keyboard is; the row's other cells keep the gray, and the
                // State cell its word.
                added.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }

            // Struck through where the rule grants nothing here whatever the mask holds: a name
            // the database no longer names, and a rule from above the node's box refuses. Kept on
            // the table rather than dropped, since seeing what the box refuses is what an operator
            // ticked the box for, and marked on the mask because a mask is where the eye already is.
            if (row.States.Contains(GrantState.NoProfile) || row.States.Contains(GrantState.RefusedHere))
            {
                added.Cells[MaskColumn].Style.Font = _refusedMask;
            }

            ((RuleRow)added).Marks = marks;

            if (marks.Bound)
            {
                added.Cells[ProfileColumn].Style.Padding = Bracketed();
            }
        }

        /// <summary>
        /// Gives every line and the box the color behind what each shows, for the display as it is
        /// set now: the light yellow of a change OK writes over the light red of a group, of a
        /// limit and of a box in the mixed mark, and none at all under a high contrast setting.
        /// </summary>
        /// <remarks>
        /// **Behind the line and never over the selection**: a selected cell takes the selection's
        /// colors on every line, since the selection is where the keyboard is and the lines that
        /// carry a color are the lines that need attention; the line's other cells keep the color.
        /// A change no OK can write takes no yellow, since the yellow tells that OK writes it.
        /// </remarks>
        private void Tint()
        {
            var tints = RowTints.For(SystemInformation.HighContrast);
            foreach (var line in _grid.Rows.OfType<RuleRow>().Where(line => line.Marks != null))
            {
                line.DefaultCellStyle.BackColor = line.Marks.Tint(tints);
            }

            _sealed.BackColor = _sheet.BoxChanged && _sheet.CanEdit
                ? tints.Changed
                : _sheet.Box == GrantBox.Mixed ? tints.Bound : Color.Empty;
        }

        /// <summary>Gives the lines and the box the colors the display now asks for, at once.</summary>
        /// <remarks>
        /// A high contrast setting turned on or off while the dialog stands changes the colors the
        /// text is drawn in, and a light yellow left behind light text is a line nobody reads, on a
        /// tab that takes no press as on any other. The control's own notice of the change, so that
        /// nothing outlives the control.
        /// </remarks>
        protected override void OnSystemColorsChanged(EventArgs e)
        {
            WindowHandlers.Run("the rules could not take the display's colors", () =>
            {
                base.OnSystemColorsChanged(e);

                if (_grid != null)
                {
                    Tint();
                }
            });
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            WindowHandlers.Run("the rules could not be laid out", () =>
            {
                base.OnLayout(e);

                // Laid out again here: a layout asked for inside a layout is dropped, so the
                // strip's new height would otherwise move nothing until something else asks.
                if (Lend())
                {
                    base.OnLayout(e);
                }
            });
        }

        /// <summary>Scales the strip's full height as the page is scaled.</summary>
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            WindowHandlers.Run("the rules could not be scaled", () =>
            {
                base.ScaleControl(factor, specified);
                if ((specified & BoundsSpecified.Height) != BoundsSpecified.None)
                {
                    _band = (int)Math.Round(_band * factor.Height);
                }
            });
        }

        /// <summary>
        /// Lends the table the pixels its rows lack from the strip under it, where the strip holds
        /// that many, and nothing where the rows lack more; true where the strip's height changed.
        /// </summary>
        /// <remarks>
        /// **A row cut by a few pixels is a row in view**, and the table answers a row cut by its
        /// own bottom line with a scroll bar: in KeePass's group dialog the last row of a table of
        /// four lacks two pixels, measured off pictures of a real dialog, and the table would put
        /// its scroll bar beside four rows in view. The strip under the table lends up to its
        /// height, so such a row is whole and no bar stands; a row cut by more than the strip
        /// holds is a row out of view, and the table scrolls to it as ever.
        /// </remarks>
        private bool Lend()
        {
            if (_air == null || _grid == null || !_grid.IsHandleCreated)
            {
                return false;
            }

            var table = (Table)_grid;
            var room = _grid.Height + _air.Height - _band;
            var needed = (_grid.ColumnHeadersVisible ? _grid.ColumnHeadersHeight : 0)
                         + _grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible)
                         + (2 * table.Edge);
            var lacking = needed - room;
            var height = lacking > 0 && lacking <= _band ? _band - lacking : _band;
            if (_air.Height == height)
            {
                return false;
            }

            _air.Height = height;
            return true;
        }

        /// <summary>The padding before a bound line's name that the bracket stands in, at the table's size of text.</summary>
        private Padding Bracketed()
        {
            return new Padding(GrantMarks.Room(_grid.Font), 0, 0, 0);
        }

        /// <summary>Where the keyboard is on the table, or null when it is nowhere on it.</summary>
        private Place Where()
        {
            var current = _grid.CurrentCell;
            if (current == null || current.RowIndex < 0)
            {
                return null;
            }

            var row = _grid.Rows[current.RowIndex];
            return new Place(
                row.IsNewRow ? null : ProfileOf(row), current.RowIndex, current.ColumnIndex, row.IsNewRow);
        }

        /// <summary>
        /// Puts the keyboard back on the rebuilt table: on the rule an edit was about, or where it
        /// was, or as near there as the table still reaches.
        /// </summary>
        /// <remarks>
        /// Only where it was on the table before, because a table the keyboard never reached has
        /// nothing to be put back, and the grid gives itself a cell when it is first given the
        /// focus. Its own try, because failing to do this must not cost the rest of the redraw.
        /// </remarks>
        private void Settle(string profile, Place was)
        {
            if (was == null)
            {
                return;
            }

            try
            {
                var row = profile == null ? -1 : IndexOf(profile);
                if (row < 0)
                {
                    row = was.NewRow ? _grid.NewRowIndex : IndexOf(was.Profile);
                }

                if (row < 0)
                {
                    row = Math.Min(was.Row, _grid.Rows.Count - 1);
                }

                if (row < 0)
                {
                    return;
                }

                _grid.CurrentCell = _grid.Rows[row].Cells[profile == null ? was.Column : MaskColumn];
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("the grant table could not keep its place: " + error.Message);
            }
        }

        /// <summary>Which rule the keyboard was on, recorded before a rebuild loses it.</summary>
        private sealed class Place
        {
            public Place(string profile, int row, int column, bool newRow)
            {
                Profile = profile;
                Row = row;
                Column = column;
                NewRow = newRow;
            }

            /// <summary>The profile of that row, or null on the empty line at the bottom.</summary>
            public string Profile { get; }

            public int Row { get; }

            public int Column { get; }

            public bool NewRow { get; }
        }

        /// <summary>
        /// A State cell that tells UI automation what its word means.
        /// </summary>
        /// <remarks>
        /// As the cell's description rather than its name, which UI automation builds from the
        /// word a screen reader already reads: what the word means is something more about the
        /// cell, in the words its tooltip uses, without the marks that keep a tooltip read left to
        /// right.
        /// </remarks>
        private sealed class StateCell : DataGridViewTextBoxCell
        {
            public string Description { get; set; }

            public override object Clone()
            {
                var clone = (StateCell)base.Clone();
                clone.Description = Description;
                return clone;
            }

            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new Described(this);
            }

            private sealed class Described : DataGridViewCellAccessibleObject
            {
                private readonly StateCell _cell;

                public Described(StateCell cell)
                    : base(cell)
                {
                    _cell = cell;
                }

                public override string Description
                {
                    get { return string.IsNullOrEmpty(_cell.Description) ? base.Description : _cell.Description; }
                }

                /// <summary>
                /// The same text as the cell's help, which is what UI automation reads as the help
                /// text, through its bridge to the accessible object and through the table's own
                /// provider alike; the description reaches UI automation only in the legacy pattern
                /// of the accessible object, where nothing reads a help text.
                /// </summary>
                public override string Help
                {
                    get { return string.IsNullOrEmpty(_cell.Description) ? base.Help : _cell.Description; }
                }
            }
        }

        /// <summary>
        /// A line of the table that tells UI automation whether the line changed on the tab.
        /// </summary>
        /// <remarks>
        /// The row's description, beside the State cell's, because a changed rule of the node's own
        /// shows no word in its State cell, and a screen reader reading the row hears that the row
        /// waits for OK.
        /// </remarks>
        private sealed class RuleRow : DataGridViewRow
        {
            public string Description { get; set; }

            /// <summary>What the line draws beyond its texts, or null on the empty line.</summary>
            public GrantMarks Marks { get; set; }

            public override object Clone()
            {
                var clone = (RuleRow)base.Clone();
                clone.Description = Description;
                clone.Marks = Marks;
                return clone;
            }

            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new Described(this);
            }

            private sealed class Described : DataGridViewRowAccessibleObject
            {
                private readonly RuleRow _row;

                public Described(RuleRow row)
                    : base(row)
                {
                    _row = row;
                }

                public override string Description
                {
                    get { return _row.Description ?? base.Description; }
                }

                /// <summary>
                /// The same text as the row's help, which UI automation reads, as the State
                /// cell's is.
                /// </summary>
                public override string Help
                {
                    get { return _row.Description ?? base.Help; }
                }
            }
        }
    }
}
