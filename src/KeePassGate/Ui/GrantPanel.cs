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
    /// This exists because `CustomData` is not editable in the stock KeePass dialogs, so without it
    /// no grant can be made at all and the whole access control model is inert.
    ///
    /// **The table is the tab.** One row per profile, its rule as a `CRUSDLAP` mask in a monospaced
    /// column, the rules inherited from above shown among the local ones in gray, and any of those
    /// this node's own seal refuses struck through. Everything the panel decides is in
    /// `GrantSheet`, which is tested without a message pump; what is left here is drawing it and
    /// turning clicks into calls.
    ///
    /// **It writes on every commit, and only when something changed.** A dialog's OK is what makes
    /// the write durable, because what this edits is the dialog's own copy of the node's custom
    /// data. Cancel therefore discards it, which is what an operator pressing Cancel means.
    ///
    /// Nothing in here throws. It runs on the UI thread of a password manager.
    /// </remarks>
    public sealed class GrantPanel : UserControl
    {
        private const int ProfileColumn = GrantTab.ProfileColumn;
        private const int MaskColumn = GrantTab.MaskColumn;
        private const int ForgetColumn = GrantTab.ForgetColumn;

        /// <summary>What the button in the last column says. One character, and it means remove.</summary>
        private const string ForgetMark = "X";

        private readonly GrantSheet _sheet;
        private readonly IGrantStore _store;
        private readonly IGateLog _log;

        private readonly Label _scope;
        private readonly Label _warning;
        private readonly DataGridView _grid;
        private readonly CheckBox _sealed;
        private readonly Button _discard;
        private readonly ToolTip _tips;
        private readonly Font _monospace;

        /// <summary>
        /// The same font with a line through it, for a mask that no longer arrives.
        /// </summary>
        /// <remarks>
        /// Derived from the monospaced one rather than built beside it, so that a refused mask is
        /// the same eight characters in the same places as every other mask in the column, with
        /// one thing added.
        /// </remarks>
        private readonly Font _refusedMask;

        private bool _populating;

        /// <summary>
        /// The picker that is showing, or null.
        /// </summary>
        /// <remarks>
        /// One at a time, always. A second dropdown opened over the first leaves the first one's
        /// closing to run against a grid the second is already using, and it leaves two suspensions
        /// of the dialog's accept and cancel buttons where only the inner one is ever undone.
        /// </remarks>
        private Picker _open;

        public GrantPanel(GrantSheet sheet, IGrantStore store, string scope, IGateLog log)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            _sheet = sheet;
            _store = store;
            _log = log ?? NullGateLog.Instance;

            Name = GrantTab.PanelControlName;

            // No scaling mode of its own. The page is scaled once, as it is attached, by as much as
            // the dialog around it was; a font mode here, with nothing to measure against, did
            // nothing then and scaled the panel a second time on any later change of font.
            Padding = new Padding(Spacing.Gap);

            _monospace = new Font(FontFamily.GenericMonospace, Font.Size + 1f, FontStyle.Regular);
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

                // A few pixels of air, so the box does not read as the table's last row. Padding
                // rather than a margin, because a docked control is laid out edge to edge and its
                // margin is not consulted at all.
                Padding = new Padding(0, Spacing.Gap, 0, 0),
            };

            _discard = new Button
            {
                Name = GrantTab.DiscardButtonName,
                Text = GrantTab.DiscardButtonText,
                AutoSize = true,
                Dock = DockStyle.Bottom,
                Visible = false,
            };

            _grid = BuildGrid();

            Controls.Add(_grid);
            Controls.Add(_sealed);
            Controls.Add(_discard);
            Controls.Add(_warning);
            Controls.Add(_scope);

            Tooltips.Set(_tips, _sealed, GrantTab.SealCheckBoxHelp);
            Tooltips.Set(_tips, _discard, GrantTab.DiscardButtonHelp);

            _sealed.CheckedChanged += (sender, e) => OnSealChanged();
            _discard.Click += (sender, e) => Discard();

            // Every time the tab is brought forward, which is the only way back to it from the
            // Plugin Data tab that shares its copy.
            VisibleChanged += (sender, e) =>
            {
                if (Visible)
                {
                    Resync();
                }
            };

            Populate();
        }

        /// <summary>
        /// How many times this panel has actually written.
        /// </summary>
        /// <remarks>
        /// For the tests, and it is the number that matters: the grid commits for reasons that are
        /// not edits, and a panel that wrote on every one of them would mark a database as modified
        /// for having been looked at.
        /// </remarks>
        public int Writes { get; private set; }

        /// <summary>The rows on screen, in order, as text. For tests and for automation.</summary>
        public IList<string> Lines
        {
            get
            {
                var lines = new List<string>();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (!row.IsNewRow)
                    {
                        lines.Add(TextOf(row, ProfileColumn) + " " + TextOf(row, MaskColumn));
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
                _log.Warn("a grant picker could not be closed: " + error.Message);
                _open = null;
            }
        }

        /// <summary>
        /// Writes a rule, exactly as typing one into the table does.
        /// </summary>
        /// <remarks>
        /// Public for the same reason the tab's buttons are: `Button.PerformClick` does nothing on a
        /// form that was never shown, silently, so a test that pressed one would assert against a
        /// table nobody touched. What the panel does is reachable without a window.
        /// </remarks>
        public string SetGrant(string profile, string mask)
        {
            var complaint = _sheet.Set(profile, mask);
            if (complaint == null)
            {
                Apply(profile);
            }

            return complaint;
        }

        /// <summary>Takes a rule off this node, exactly as pressing the X does.</summary>
        public bool RemoveGrant(string profile)
        {
            if (!_sheet.Remove(profile))
            {
                return false;
            }

            Apply(profile);
            return true;
        }

        /// <summary>Throws away a document nobody can read, exactly as the button does.</summary>
        public void Discard()
        {
            if (!_sheet.Unreadable)
            {
                return;
            }

            _sheet.DiscardUnreadable();
            Apply();
        }

        /// <summary>
        /// Reads the store again, and redraws the table when something other than this tab has
        /// changed what it holds.
        /// </summary>
        /// <remarks>
        /// Asked whenever the tab is brought forward, because what can change the dialog's copy
        /// behind this tab's back is on the same dialog: KeePass's Plugin Data tab, whose Delete
        /// takes the `KPG` key out. `GrantSheet.Reload` says what a stale table cost. Public for the
        /// reason `SetGrant` is.
        /// </remarks>
        public void Resync()
        {
            if (_store == null || Picking)
            {
                return;
            }

            string stored;
            try
            {
                stored = _store.Read();
            }
            catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfReflection(error))
            {
                _log.Warn("the rules on this dialog could not be read again: " + error.Message);
                return;
            }

            if (_sheet.Reload(stored))
            {
                Populate();
            }
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
            if (row.IsNewRow)
            {
                return;
            }

            var profile = ProfileOf(row);
            if (profile.Length == 0)
            {
                // A rule stored for no profile at all, which only a document written by hand can
                // hold. The empty line at the bottom never reaches here, since `Press` and the
                // keyboard send it to the profile list, so asking for a profile first was a
                // sentence about a different row. Said rather than ignored, because a cell that
                // does nothing when pressed is a cell somebody presses again.
                Warn(GrantTab.RuleWithoutProfile);
                return;
            }

            Warn(null);

            var held = VerbMask.Read(TextOf(row, MaskColumn)) ?? new List<AclVerb>();

            var list = new CheckedListBox
            {
                Name = GrantTab.RulePickerName,
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

            // And Enter means "this", which on a row the seal refuses is not the same as leaving it.
            var confirmed = false;

            // What it looked like when it opened. Clicking away is how a dropdown is usually
            // left, and on an inherited row every box is already checked from the rule above, so
            // without this a glance at one writes a local rule nobody asked for. That rule
            // shadows the parent for good, so narrowing the parent later leaves this node wide.
            var before = VerbMask.Of(held);

            // **Except where the rule above stops here.** On a row this node's seal refuses, the
            // same check marks written here are not a copy of the rule above but the only way to
            // let it through, which is what the row's own tooltip tells the operator to do. So
            // Enter writes them. Clicking away still does not: that is how a dropdown is left
            // after a glance, and a glance must not undo a seal.
            var refused = Refused(profile);

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
                if (string.Equals(mask, before, StringComparison.Ordinal) && !(refused && confirmed))
                {
                    // Nothing was checked or cleared, so nothing is written. On a local rule
                    // that saves a change nobody made; on an inherited one it is the whole
                    // difference between reading the table and rewriting it.
                    return;
                }

                // Said, not dropped. The sheet refuses a name no certificate could carry, and a
                // document written by hand can hold one, so this is reachable. A picker that
                // closed on a refusal it never mentioned is a control that did nothing.
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

            var free = ProfileChoices;
            if (free.Count == 0)
            {
                // Nothing at all when every profile is already on the table: what the sentence
                // said was that the rows the operator is looking at are all of them, which the
                // rows say themselves, and it cost four lines of a page that has few to spend.
                //
                // A database naming no profiles is the other case and keeps its sentence, because
                // there the table is empty and nothing on the page says where one comes from.
                Warn(Any(_sheet.Offered) ? null : GrantTab.NoProfilesYet);
                return;
            }

            Warn(null);

            var list = new ListBox
            {
                Name = GrantTab.ProfilePickerName,
                BorderStyle = BorderStyle.None,
            };

            // Sized by the host, for the reason the rule picker gives.

            foreach (var profile in free)
            {
                list.Items.Add(profile);
            }

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
                    return false;
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

            list.Click += (sender, e) =>
            {
                chosen = Picked(list);
                drop.Close();
            };

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
                Later(() => OpenPicker(RowOf(profile)));
            });

            Open(drop, list, ProfileColumn, rowIndex, keys);
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
            if (_sheet.Owns(profile))
            {
                return "There is already a rule for \"" + profile + "\" here.";
            }

            return SetGrant(profile, VerbMask.Of(null));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
                _monospace.Dispose();
                _refusedMask.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// How a verb reads in the picker: its letter, then what it means.
        /// </summary>
        /// <remarks>
        /// The switch needs a sentence rather than a name, because its name is a noun and it is not
        /// a verb: on its own it grants nothing, and what it does is widen whatever else is checked
        /// so that the same permissions reach an entry's attachments and not only its fields.
        /// </remarks>
        private static string Choice(AclVerb verb)
        {
            var text = VerbMask.LetterOf(verb) + "   " + AclVerbs.Write(verb);

            return AclVerbs.IsScope(verb)
                ? text + " (apply checked permissions)"
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

        /// <summary>
        /// What a rule here does to the one a group above grants the same profile, as the word
        /// that starts the sentence saying so.
        /// </summary>
        /// <remarks>
        /// Worked out rather than assumed. Nearest wins, so a rule here replaces the one above it
        /// in full, and one that grants more than that rule is in force in full: said to narrow it,
        /// the tooltip told an operator widening an entry that the group's rule still bounded it,
        /// which is the direction a sentence about permissions must never be wrong in.
        /// </remarks>
        private static string Replacing(string mask, string inherited)
        {
            var here = VerbMask.Read(mask);
            var above = VerbMask.Read(inherited);
            if (here == null || above == null)
            {
                return "Replaces";
            }

            var inside = Within(here, above);
            var around = Within(above, here);

            if (inside && around)
            {
                return "The same as";
            }

            if (inside)
            {
                return "Narrows";
            }

            return around ? "Widens" : "Replaces";
        }

        /// <summary>Whether every verb in the first set is in the second.</summary>
        private static bool Within(IList<AclVerb> some, IList<AclVerb> all)
        {
            foreach (var verb in some)
            {
                if (!all.Contains(verb))
                {
                    return false;
                }
            }

            return true;
        }

        private static string TextOf(DataGridViewRow row, int column)
        {
            var value = row.Cells[column].Value;
            return value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>The profile this row is for, exactly as the document spells it.</summary>
        /// <remarks>
        /// Not <see cref="TextOf"/>, which trims. A document written by hand can name a profile
        /// with spaces round it, because the reader takes any member name, and the sheet keys that
        /// rule under the padded name. Asking it to remove the trimmed one misses, so the X on
        /// that row did nothing and said nothing; setting one added a second rule beside it.
        /// The name is carried on the cell so that what is drawn and what is keyed cannot part.
        /// </remarks>
        private static string ProfileOf(DataGridViewRow row)
        {
            var exact = row.Cells[ProfileColumn].Tag as string;
            return exact ?? TextOf(row, ProfileColumn);
        }

        /// <summary>
        /// Whether this profile's row is a rule from a group above that stops at this node.
        /// </summary>
        private bool Refused(string profile)
        {
            foreach (var row in _sheet.Rows)
            {
                if (string.Equals(row.Profile, profile, StringComparison.Ordinal))
                {
                    return !row.Local && !row.Reaches;
                }
            }

            return false;
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
                return Press(keyData) || base.ProcessCmdKey(ref message, keyData);
            }
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
        /// </remarks>
        private sealed class Table : DataGridView
        {
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ClearSelection();
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
                    Warn("There is no room to show that rule. A taller dialog gives the table "
                        + "more room.");
                    Abandon(drop, keys);
                    return;
                }

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

        /// <summary>Lets go of a picker that never opened, saying so rather than throwing.</summary>
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
        /// raised from inside the hosted control's own event, while WinForms is still delivering the
        /// click or the keystroke that closed the dropdown. Three of the things that have to happen
        /// next are things it will not survive being told to do at that moment: disposing the
        /// dropdown disposes the control whose event is running, rebuilding the grid replaces the
        /// row the click arrived on, and giving the dialog its accept and cancel buttons back hands
        /// the keystroke that is still in flight to a button. Any of them throws from inside the
        /// message loop, and an exception there is not a message on the tab: it is KeePass closing
        /// the dialog with the rule half made.
        ///
        /// So the handler records what was chosen and nothing else, and this runs afterwards.
        /// </remarks>
        private void Finish(ToolStripDropDown drop, IDisposable keys, Action work)
        {
            // Cleared at once, so a click arriving between the close and the next message opens the
            // next picker rather than being ignored as though one were still up. The dialog's keys
            // stay taken across that: the next picker's token joins this one's rather than
            // recording the buttons afresh, and `DialogKeys` gives them back only when the last of
            // the two is let go, whichever that is.
            _open = null;

            Later(() =>
            {
                try
                {
                    keys.Dispose();
                    work();
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    // Named rather than swallowed whole, and reported on the tab, because this runs
                    // on the message loop of a password manager and what escapes here closes the
                    // dialog the operator is working in.
                    _log.Error("a grant picker could not be closed cleanly", error);
                    Warn("That choice could not be applied: " + Clipped.Message(error.Message));
                }
                finally
                {
                    drop.Dispose();
                }
            });
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

            // And nothing in the last column, because there is no rule on this line to remove.
            // `OnCellPainting` is what keeps the button from being drawn at all; this is the color
            // of the space that leaves, which is the gray the permissions cell is wearing two
            // columns to the left, for the same reason and on the same line.
            var forget = (DataGridViewButtonCell)row.Cells[ForgetColumn];
            forget.Value = string.Empty;
            forget.Style.BackColor = SystemColors.Control;
            forget.Style.SelectionBackColor = SystemColors.Control;
            forget.ToolTipText = string.Empty;
        }

        /// <summary>Which row a profile is on, or -1 when the table has no row for it.</summary>
        private int RowOf(string profile)
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

        /// <summary>
        /// Runs something once the current message is finished with.
        /// </summary>
        /// <remarks>
        /// One dropdown opens the next, and opening it from inside the first one's closing handler
        /// would be showing a window while another is being torn down. Straight through when there
        /// is no window at all, which is how a test reaches the same path.
        ///
        /// **Guarded where it runs, and not only where it is posted.** Posted work runs later on
        /// KeePass's message loop, after the try around the post has long returned, and what
        /// escapes there is WinForms' own error dialog with a Quit button in it rather than a
        /// sentence on this tab. Nothing has been seen to throw there; the point is that the
        /// promise this class makes should not depend on that.
        /// </remarks>
        private void Later(Action work)
        {
            Action guarded = () =>
            {
                try
                {
                    work();
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("a grant picker could not be opened", error);
                    Warn("That picker could not be opened: " + Clipped.Message(error.Message));
                }
            };

            try
            {
                if (!IsHandleCreated)
                {
                    guarded();
                    return;
                }

                BeginInvoke(guarded);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a grant picker could not be opened: " + error.Message);
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
                HeaderText = "Profile",

                // The only column with nothing known about what it holds, so it takes whatever the
                // other two leave. A profile name is as long as somebody made it.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,

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
                ToolTipText = Tooltips.Wrapped(
                    // Both ways a profile becomes one this database knows: named on the
                    // profiles page, or approved when a client asks for it at pairing.
                    "The profile this rule is for, chosen from the ones this database "
                    + "names. Add one on the " + GateName.OnATab + " tab of the database "
                    + "settings, or approve one when a client asks for it."),
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
                ToolTipText = Tooltips.Wrapped("Click to choose what a profile may do here."),
            };

            mask.DefaultCellStyle.Font = _monospace;

            // Left, so that every mask starts in the same place as the one above it. Centering
            // eight fixed width characters in a column that is not eight characters wide moves the
            // letters about between rows, and the whole point of a mask is that the eye reads down
            // a column of them: `C` is always first and `P` is always last.
            //
            // No floor written down for this one. A width that fits the heading fits the masks,
            // which are eight characters whatever they grant, so the heading is what it measures.
            mask.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            mask.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;

            var forget = new DataGridViewButtonColumn
            {
                Name = GrantTab.ForgetColumnName,

                // Nothing written. The heading is a bin, painted in `OnCellPainting`, and a column
                // sized to its content would otherwise be as wide as whatever word stood here.
                HeaderText = string.Empty,
                Text = ForgetMark,

                // Off, so that a cell can say nothing. With it on, every button in the column
                // draws the column's own text whatever the cell holds, and the row that has
                // nothing to remove offers to remove it anyway.
                UseColumnTextForButtonValue = false,

                // One character or none, so the heading and the mark are all there is to fit.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                MinimumWidth = GateIcon.Room,
                ToolTipText = Tooltips.Wrapped(
                    "Removes the rule this node stores, which puts back whatever is "
                    + "inherited. A rule that is purely inherited has nothing here to remove."),
            };

            grid.Columns.Add(profile);
            grid.Columns.Add(mask);
            grid.Columns.Add(forget);

            // No cell-value handler, because neither column can be typed into: both are
            // `ReadOnly`, and the only writes to them are this panel's own redraw. A rule is
            // chosen from the picker rather than typed, which is the decision the table is
            // built around, and `FINDINGS.md` records why automation cannot type into one
            // either. A read-only cell exposes no pattern that operates it, so the end to end
            // test sends real keystrokes at the picker instead.
            grid.CellClick += OnCellClick;
            grid.KeyDown += OnGridKey;
            grid.CellPainting += OnCellPainting;
            grid.DataError += (sender, e) => e.Cancel = true;

            return grid;
        }

        private void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            Press(e.RowIndex, e.ColumnIndex);
        }

        /// <summary>
        /// Draws a cell in the last column as the space it is wherever there is nothing to remove.
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
                return;
            }

            // The heading is a bin rather than a word. What the column is for is one idea, and a
            // picture carries it in the width the column already has; a word would be a word the
            // column then has to be wide enough for, on a table whose other two are the ones with
            // something to say.
            if (e.RowIndex < 0)
            {
                var bin = GateIcon.Bin;
                if (bin != null)
                {
                    e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
                    e.Graphics.DrawImageUnscaled(
                        bin,
                        e.CellBounds.Left + ((e.CellBounds.Width - bin.Width) / 2),
                        e.CellBounds.Top + ((e.CellBounds.Height - bin.Height) / 2));
                    e.Handled = true;
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
                RemoveGrant(ProfileOf(_grid.Rows[rowIndex]));
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

        private void OnSealChanged()
        {
            if (_populating || !_sheet.CanEdit)
            {
                return;
            }

            _sheet.Default = _sealed.Checked ? AclDefault.Deny : AclDefault.Inherit;
            Apply();
        }

        /// <summary>
        /// Marks a row whose profile the database no longer names, answering with whether it did.
        /// </summary>
        /// <remarks>
        /// Struck through on the mask, which is where the eye already is and what a seal's refusal
        /// looks like too: in both the rule is there and grants nothing here. The sentence replaces
        /// whatever the row said before, because narrowing or letting through a rule that reaches
        /// nobody is not what the operator needs to hear about it. Local and inherited alike, since
        /// a removed name is inert wherever its rule was written.
        /// </remarks>
        private bool Inert(GrantRow row, DataGridViewRow added)
        {
            if (row.Named)
            {
                return false;
            }

            added.Cells[MaskColumn].Style.Font = _refusedMask;

            // A rule stored for no profile at all is inert too, and not because a name went: no
            // name can be added to put it back in force, so it says what it is instead.
            added.Cells[MaskColumn].ToolTipText = Tooltips.Wrapped(
                string.IsNullOrEmpty(row.Profile) ? GrantTab.RuleWithoutProfile : GrantTab.ProfileNotNamed);
            return true;
        }

        private void Warn(string text)
        {
            _warning.Text = text ?? string.Empty;
            _warning.Visible = !string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// Redraw, then store. In that order, because storing is what can fail and what it says
        /// when it does has to be the last thing written to the tab rather than the first.
        /// </summary>
        /// <param name="settle">The profile the edit was about, which `Populate` takes the keyboard to.</param>
        private void Apply(string settle = null)
        {
            Populate(settle);
            Save();
        }

        /// <param name="settle">
        /// The profile an edit was about, whose rule the keyboard is on once the table is rebuilt,
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

                foreach (var row in _sheet.Rows)
                {
                    // No X on a table that cannot be changed: pressing one would remove nothing,
                    // and a button that does nothing is a button somebody presses again.
                    var mark = row.Local && _sheet.CanEdit ? ForgetMark : string.Empty;
                    var index = _grid.Rows.Add(row.Profile, row.Mask, mark);
                    _grid.Rows[index].Cells[ProfileColumn].Tag = row.Profile;
                    var added = _grid.Rows[index];

                    // Which profile a row is for is settled once it is on the table, so the cell
                    // says how to change it rather than pretending it can be edited.
                    added.Cells[ProfileColumn].ToolTipText = Tooltips.Wrapped(GrantTab.ProfileIsSettled);

                    if (row.Local)
                    {
                        // What this rule replaced, on the rule doing the replacing, and which way:
                        // narrower, wider, the same or neither. A profile is one row however it
                        // got there, so a local rule replaces the inherited one on the table and
                        // there is nowhere else left to say what it replaced. On the mask rather
                        // than in a column of its own, because nothing on this tab scrolls and the
                        // group dialog gives it the least room of the three it is put on.
                        // `GrantTabFitsTests` measures that rather than assuming it.
                        if (row.InheritedMask != null)
                        {
                            added.Cells[MaskColumn].ToolTipText = Tooltips.Wrapped(
                                Replacing(row.Mask, row.InheritedMask) + " " + row.InheritedMask
                                + ", inherited from a group above.");
                        }

                        Inert(row, added);
                        continue;
                    }

                    // Inherited, so nothing here owns it: shown so the table is the whole answer,
                    // and grayed so it is not mistaken for a rule this node made.
                    added.DefaultCellStyle.ForeColor = SystemColors.GrayText;

                    // Gray while selected too. A grid takes a row the moment it is given the
                    // focus, and a row painted in the selection color is a row that looks local:
                    // the operator would be reading a rule this node does not have as one it does.
                    added.DefaultCellStyle.SelectionForeColor = SystemColors.GrayText;
                    added.DefaultCellStyle.SelectionBackColor = _grid.DefaultCellStyle.BackColor;

                    if (Inert(row, added))
                    {
                        continue;
                    }

                    if (row.Reaches)
                    {
                        // Replace rather than narrow, because a rule chosen here replaces this one in
                        // full for this node and everything under it, whether it grants less or
                        // more.
                        added.Cells[MaskColumn].ToolTipText = Tooltips.Wrapped(
                            "Inherited from a group above. Choose a rule here to replace it.");
                        continue;
                    }

                    // Struck through, because this node refuses every profile it does not name and
                    // that includes this one: the rule is still above and it stops here. Kept on
                    // the table rather than dropped, since seeing what the seal refuses is what an
                    // operator turned it on for, and marked on the mask because a mask is where
                    // the eye already is. Gray alone said only that the rule came from elsewhere,
                    // which was as true before the seal as after it.
                    added.Cells[MaskColumn].Style.Font = _refusedMask;

                    // And the sentence is replaced rather than added to. What the other one offers
                    // is to replace a rule that arrives here, and on this row nothing arrives to be
                    // replaced: `deny` refuses what this node does not name, so naming the profile
                    // is what lets it through again. Under rules here that will not read, nothing
                    // can be chosen until they are discarded, so that is what it says instead.
                    added.Cells[MaskColumn].ToolTipText = Tooltips.Wrapped(_sheet.Unreadable
                        ? "The rules here cannot be read, so what a group above grants this one "
                          + "no longer arrives. Discard them to let it through."
                        : "This node refuses every profile it does not name, so what a group above "
                          + "grants this one no longer arrives. Choose a rule here to let it through.");
                }

                ScrollTo(top);

                _sealed.Checked = _sheet.Default == AclDefault.Deny;
                _sealed.Enabled = _sheet.CanEdit;

                // The table stays enabled when nothing on it can be changed, because a disabled grid
                // takes neither its scroll bar nor the wheel, and the rules past the ones that fit
                // were out of reach on the very tabs that promise they stay readable: an entry's
                // earlier version, several entries at once. What stops an edit is what stops it
                // everywhere else, the sheet refusing, and neither picker opens on a sheet that
                // cannot change.
                _grid.AllowUserToAddRows = _sheet.CanEdit;
                _discard.Visible = _sheet.Unreadable;

                // After the flag it reads, not before it. Discarding restores editing, and on
                // that redraw the flag still said otherwise, so the line came back undimmed and
                // without the text that says what it is for.
                DimTheEmptyLine();

                // Nothing chosen, and somewhere for the keyboard to be, after the empty line has
                // come or gone. A grid selects the cell it makes current, and on an entry whose
                // only rules come from a group above that painted an inherited rule in the
                // selection color, which is exactly the color it must not be: the gray is the
                // whole of what says this node does not own it. So the selection goes and the
                // current cell stays, because F2 and Alt+Down open a picker on the current cell:
                // taken away as well, as it was on every rebuild, it left the keyboard nothing to
                // open until an arrow key gave the table a cell again.
                Settle(settle, was);
                _grid.ClearSelection();

                Warn(_sheet.Warning);
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
                var row = profile == null ? -1 : RowOf(profile);
                if (row < 0)
                {
                    row = was.NewRow ? _grid.NewRowIndex : RowOf(was.Profile);
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

        private void Save()
        {
            if (_store == null || !_sheet.Changed)
            {
                return;
            }

            try
            {
                _store.Write(_sheet.Document);
                _sheet.Stored();
                Writes++;
            }
            catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfReflection(error))
            {
                _log.Error("a grant could not be stored", error);

                // Back to what is stored, so that the table and the sentence agree. Left as it
                // was, the refused rule stayed on the sheet with the sheet still counting as
                // changed, and the next write of anything at all would have carried it. A
                // grant reported as refused would land later, with nothing said the second time.
                // The one store this has cannot fail twice in a way that reaches there, which
                // is a fact about a different file.
                _sheet.Revert();
                Populate();

                // After the redraw. That redraw ends by saying whatever the sheet has to say,
                // which is nothing once the refused rule is off it. Said first, this sentence
                // would be wiped by the very redraw that makes it true.
                //
                // With the reason, from the innermost exception: the store reaches the dialog's copy
                // through reflection, whose own exception says only that something it called threw.
                // The reason is said here rather than left to the log, which is a file the person
                // at this dialog would have to go and find.
                Warn("That could not be stored, so it is not in force: "
                    + Clipped.Message(error.GetBaseException().Message));
            }
        }
    }
}
