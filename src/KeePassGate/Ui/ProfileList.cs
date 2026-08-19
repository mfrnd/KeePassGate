using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The profiles this database knows about, and the two things an operator does to them.
    /// </summary>
    /// <remarks>
    /// **A profile is a name, and this is where names come from.** What one reaches is written on
    /// the groups and entries; who holds one is in the admission records. Before this page there was
    /// no third place, so the set was whatever somebody had typed, and a typo made a profile that
    /// existed, granted nothing, and looked exactly like the one that was meant.
    ///
    /// **Deleting one takes it off this list and nothing else.** A client holds only the names its
    /// record carries that this list still names, so a name taken off it grants nothing, whatever
    /// a rule says; the rules stay where they are, inert, and adding the name again puts them back
    /// in force. The page says how many rules stay.
    ///
    /// It waits the way everything else on this tab waits: the dialog's OK is what puts any of it
    /// into the database, and saving is what puts it on disk.
    /// </remarks>
    public sealed class ProfileList : UserControl
    {
        public const string ControlName = "KpgProfileList";

        public const string ListName = "KpgProfileGrid";

        /// <summary>
        /// What a screen reader calls the grid, which has no caption of its own to be called by.
        /// </summary>
        /// <remarks>
        /// The same rule that gives the box below it one, and for the same reason: a control with
        /// no text announces nothing, and a table is one of the kinds `CONVENTIONS.md` names. It
        /// is a phrase rather than the lookup name, which stays where the desktop tests read it,
        /// on `Name`.
        /// </remarks>
        public const string ListLabel = "Profiles this database names";

        public const string NameBoxName = "KpgProfileNameBox";

        /// <summary>What a screen reader calls the box a profile name is typed into.</summary>
        public const string NameBoxLabel = "Profile name";

        public const string AddButtonName = "KpgProfileAddButton";

        public const string AddButtonText = "Add";

        public const string NameLabelName = "KpgProfileNameLabel";

        /// <summary>
        /// What a profile is, said where one is made.
        /// </summary>
        /// <remarks>
        /// A name on its own explains nothing: somebody typing into an empty box beside a button
        /// has no way of knowing whether they are naming a person, a machine or a permission. It
        /// is none of those, and saying what it is here is the difference between the page being
        /// usable on a first visit and being a form with a box on it.
        ///
        /// In front of the box rather than above it, because the row is one gesture and a line
        /// over it would be a second band on a page that has none to spare. **The button gives up
        /// its noun to pay for it**: "Add profile" beside a label that has just said the word
        /// profile was saying it twice, and "Add" is unambiguous next to the only box on the row.
        /// </remarks>
        public const string NameLabelText = "A profile is what a rule grants to:";

        public const string ResultLabelName = "KpgProfileResultLabel";

        /// <summary>The column that takes a profile away, and what it says.</summary>
        public const string RemoveColumnName = "KpgProfileRemoveColumn";

        public const string RemoveMark = "X";

        /// <summary>What the page says when the database names no profiles at all.</summary>
        public const string NobodyText =
            "No profile is named yet. Add one here, or approve a new one when a client asks to pair.";

        private const int NameColumn = 0;
        private const int UsedColumn = 1;
        private const int HeldColumn = 2;
        private const int RemoveColumn = 3;

        private readonly IProfiles _profiles;
        private readonly IOperatorPrompt _prompt;
        private readonly DataGridView _grid;
        private readonly TextBox _name;
        private readonly Label _empty;
        private readonly Label _result;

        /// <summary>The name the last gesture said is available to grant, while the line says so.</summary>
        private string _added;
        private readonly List<string> _shown = new List<string>();

        /// <summary>
        /// What the box drops by to sit centered on the Add button beside it.
        /// </summary>
        /// <remarks>
        /// At the default font a button is 24 pixels tall and a one line box 20, so two is half the
        /// difference. It lines two controls up rather than putting air between them, which is why
        /// it is not a `Spacing`.
        /// </remarks>
        private const int BoxNudge = 2;

        /// <summary>
        /// What the label drops by to sit level with the text inside the box beside it.
        /// </summary>
        /// <remarks>
        /// A box draws its own border and inset above the first line of what it holds, three pixels
        /// below its own top edge, so a label aligned with the top of the box is not aligned with
        /// the sentence it introduces. The row takes this off its own top padding, so the label's
        /// text still starts one `Spacing.Gap` below the page's edge.
        /// </remarks>
        private const int Nudge = BoxNudge + 3;

        private bool _filling;

        /// <param name="prompt">
        /// Asks the operator before a name comes back that rules still name. Null answers no, so a
        /// page nobody can be asked on brings nothing back into force.
        /// </param>
        public ProfileList(IProfiles profiles, IOperatorPrompt prompt)
        {
            if (profiles == null)
            {
                throw new ArgumentNullException(nameof(profiles));
            }

            _profiles = profiles;
            _prompt = prompt;

            Name = ControlName;
            Dock = DockStyle.Fill;

            _grid = BuildGrid();

            var explains = new TextLabel
            {
                Name = NameLabelName,
                Text = NameLabelText,
                AutoSize = true,

                // Level with the text in the box beside it rather than with the top of the box,
                // because a label and a field on one row are read as one sentence.
                Margin = new Padding(0, Nudge, Spacing.Gap, 0)
            };

            _name = new TextBox
            {
                Name = NameBoxName,
                AccessibleName = NameBoxLabel,
                Margin = new Padding(0, BoxNudge, Spacing.Gap, 0)
            };

            var add = new Button
            {
                Name = AddButtonName,
                Text = AddButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            add.Click += delegate { Add(_name.Text); };

            // Enter belongs to this box while the focus is in it. The dialog this page sits on has
            // an accept button, and a form with one treats Enter as that button wherever the focus
            // is, so typing a name and confirming it the way anybody confirms a one-line form
            // pressed OK: the dialog closed, every other buffered change on the tab committed, and
            // the name that had just been typed was thrown away with the box.
            //
            // Claiming the key takes two hooks, and asking for it is the first. WinForms offers
            // each key to the control it is addressed to before delivering it, and gives the ones
            // no control claims to the form's own buttons instead. A single line text box claims
            // Enter from nobody, so the key never arrived as a key at all and a `KeyDown` handler
            // for it never ran: this hook is where the box says the key is one of its own.
            _name.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs about)
            {
                Claiming(about);
            };

            // And this is what it then does with it. `SuppressKeyPress` takes the character away
            // from the box, which is what a text box handed a Return it cannot show answers with a
            // beep; `Handled` alone marks the key dealt with and lets the character travel.
            _name.KeyDown += delegate(object sender, KeyEventArgs typed) { Typing(typed); };

            var adding = new FlowLayoutPanel
            {
                Name = "KpgProfileAddingRow",
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),

                // Under it, because the table is what comes next and the two are not the same
                // subject: this row makes a profile and the table lists the ones there are. A
                // docked control is laid out edge to edge against the band it is given, so its
                // margin is never consulted and the space has to be padding.
                // A gap above and below, like the first thing on every other page of this tab. The
                // nudge comes off the top, because what has to sit a gap below the page's edge is
                // the label's text rather than the row holding it: added to it instead, this page
                // opens five pixels lower than the other two and the tab reads ragged as the
                // pages are changed.
                Padding = new Padding(0, Spacing.Gap - Nudge, 0, Spacing.Gap)
            };

            adding.Controls.Add(explains);
            adding.Controls.Add(_name);
            adding.Controls.Add(add);

            _empty = new TextLabel
            {
                Name = "KpgProfileEmptyLabel",
                Text = NobodyText,
                Dock = DockStyle.Bottom,

                // Sized to its text: a written down height is a strip of nothing under the last
                // thing on the page.
                AutoSize = true,
                Padding = new Padding(0, Spacing.Gap, 0, 0),
                ForeColor = SystemColors.GrayText,
                Visible = false
            };

            _result = new TextLabel
            {
                Name = ResultLabelName,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(0, Spacing.Gap, 0, 0),
                ForeColor = SystemColors.GrayText,
                Visible = false
            };

            // The filling control goes in first. Docking is applied from the end of the collection
            // backwards, so whatever is added last takes its band first.
            Controls.Add(_grid);
            Controls.Add(adding);
            Controls.Add(_result);
            Controls.Add(_empty);

            Describe();
        }

        /// <summary>What the list is showing, in order.</summary>
        public IReadOnlyList<string> Shown
        {
            get { return _shown; }
        }

        /// <summary>What the last gesture said, or null when it has said nothing.</summary>
        public string Result
        {
            get { return _result.Text.Length == 0 ? null : _result.Text; }
        }

        /// <summary>What the box holds, which is what Add would take.</summary>
        public string Typed
        {
            get { return _name.Text; }
            set { _name.Text = value ?? string.Empty; }
        }

        /// <summary>
        /// Which keys this box wants delivered to it rather than to the dialog it is on.
        /// </summary>
        /// <remarks>
        /// Enter and nothing else. Escape belongs to the dialog, which is where an operator backing
        /// out of a half typed name expects it to go, and every other key a text box already
        /// claims for itself.
        ///
        /// The dropdowns on the grant editor need both keys and take them a different way, by
        /// unhooking the dialog's buttons for as long as one is open. Nothing here is open: the box
        /// has the focus or it does not, and a control that claims a key while it is focused is
        /// what that describes, without anything to put back afterwards.
        ///
        /// Private, unlike the decision below it, because there is nothing here a test could ask
        /// that would mean anything: what this answers only matters if a message loop is asking.
        /// </remarks>
        private static void Claiming(PreviewKeyDownEventArgs about)
        {
            if (about == null || about.KeyCode != Keys.Enter)
            {
                return;
            }

            about.IsInputKey = true;
        }

        /// <summary>What a key pressed in the name box does.</summary>
        /// <remarks>
        /// Separate from the handler, and public, for the reason every decision on every panel here
        /// is: a key cannot be delivered to a form that was never shown, so a test driving the
        /// control would be driving nothing. This is the whole of what the handler does.
        ///
        /// What it cannot say is whether the key ever gets here, which is decided by `Claiming` and
        /// by the form, and is measured on a window that has been shown.
        /// </remarks>
        public void Typing(KeyEventArgs typed)
        {
            if (typed == null || typed.KeyCode != Keys.Enter)
            {
                return;
            }

            typed.Handled = true;
            typed.SuppressKeyPress = true;
            Add(_name.Text);
        }

        /// <summary>
        /// Adds a profile, exactly as the button does.
        /// </summary>
        /// <remarks>
        /// Public because a button press needs a control tree that has been drawn, and what the
        /// button decides is worth testing without one.
        /// </remarks>
        public string Add(string name)
        {
            var wanted = (name ?? string.Empty).Trim();

            if (wanted.Length == 0)
            {
                return Said("Type a name first.");
            }

            // Asked before the name goes on the list, because putting it there is what brings back
            // whatever was left waiting for it. A name that was never removed has nothing waiting,
            // and is added without a question.
            var returning = _profiles.Returning(wanted);
            if (returning != null
                && (_prompt == null
                    || !_prompt.Confirm("Add a profile back", returning + Environment.NewLine + Environment.NewLine
                        + "Add it anyway?")))
            {
                return Said("The profile " + wanted + " was not added.");
            }

            var refused = _profiles.Add(wanted);
            if (refused != null)
            {
                return Said(refused);
            }

            _name.Text = string.Empty;
            Describe();
            var said = Said("The profile " + wanted + " is available to grant.");
            _added = wanted;
            return said;
        }

        /// <summary>
        /// Removes the profile in one row, which is what makes every rule naming it grant nothing.
        /// </summary>
        public string Remove(int index)
        {
            if (index < 0 || index >= _shown.Count)
            {
                return Said("There is no such profile.");
            }

            var name = _shown[index];

            var refused = _profiles.Remove(name);
            if (refused != null)
            {
                return Said("The profile " + name + " is still there: " + refused);
            }

            Describe();
            return Said(_profiles.LastRemoval ?? ("The profile " + name + " is gone."));
        }

        /// <summary>Reads the database again and redraws. Safe to call at any time.</summary>
        public void Describe()
        {
            _filling = true;

            try
            {
                _shown.Clear();
                _grid.Rows.Clear();

                // Counted once for the whole page and looked up per row: each count is a walk of
                // the whole database, and a walk per row is what made the page cost its row count
                // times as much.
                var rules = _profiles.Rules();
                var clients = _profiles.Clients();

                foreach (var name in _profiles.Names())
                {
                    _shown.Add(name);
                    _grid.Rows.Add(name, Counted(rules, name), Counted(clients, name), RemoveMark);
                }
            }
            finally
            {
                _filling = false;
            }

            _empty.Visible = _shown.Count == 0;

            // A redraw a pairing caused can take off the page a name the last
            // gesture said is available, when the list filled to its ceiling meanwhile, and the
            // line has to stop saying it is.
            if (_added != null && !_shown.Contains(_added))
            {
                Said("The profile " + _added + " no longer fits: the list filled to " + ProfileNames.Most +
                     " while this dialog was open, so OK will leave it out.");
            }
        }

        /// <summary>One profile's count, which is none when the count does not name it.</summary>
        private static int Counted(IReadOnlyDictionary<string, int> counts, string name)
        {
            int found;
            return counts != null && counts.TryGetValue(name, out found) ? found : 0;
        }

        private DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Name = ListName,
                AccessibleName = ListLabel,
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackgroundColor = SystemColors.Window,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Profile",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true
            });

            // How many groups and entries have a rule for it, which is how many rules removing it
            // leaves inert and adding it back puts back in force.
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Rules",

                // As wide as the word and no wider. What it holds is a count, which is one or two
                // characters against a five letter heading, so sizing to the cells as well only
                // ever measures something narrower and the column would be the heading's width
                // anyway; saying so directly leaves the profile name the rest.
                AutoSizeMode = DataGridViewAutoSizeColumnMode.ColumnHeader,
                ReadOnly = true
            });

            // How many admitted clients hold it, which is the other half of what removing it would
            // cost. The rules count says what would stop being granted; this says who would stop
            // being granted it, and the two are different databases' worth of consequence: a
            // profile with rules and no clients is one somebody wrote ahead of the client, and a
            // profile with clients and no rules is one that grants nothing to anybody holding it.
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Clients",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.ColumnHeader,
                ReadOnly = true
            });

            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = RemoveColumnName,
                HeaderText = string.Empty,
                Text = RemoveMark,
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                MinimumWidth = GateIcon.Room,
                ToolTipText = Tooltips.Wrapped(
                    "Removes this profile, so it grants nothing. The rules naming it stay, and "
                    + "come back into force if it is added again.")
            });

            grid.CellClick += OnCellClick;
            grid.CellPainting += OnCellPainting;
            return grid;
        }

        /// <summary>
        /// Draws a bin over the heading of the column that takes something away.
        /// </summary>
        /// <remarks>
        /// The heading only. The buttons under it keep their mark: what a button does is said by
        /// the column it is in, and a picture in every row is a picture repeated once per row.
        /// </remarks>
        private void OnCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex != RemoveColumn || e.RowIndex >= 0)
            {
                return;
            }

            var bin = GateIcon.Bin;
            if (bin == null)
            {
                return;
            }

            e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            e.Graphics.DrawImageUnscaled(
                bin,
                e.CellBounds.Left + ((e.CellBounds.Width - bin.Width) / 2),
                e.CellBounds.Top + ((e.CellBounds.Height - bin.Height) / 2));
            e.Handled = true;
        }

        private void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0 || e.ColumnIndex != RemoveColumn)
            {
                return;
            }

            Remove(e.RowIndex);
        }

        private string Said(string what)
        {
            _added = null;
            _result.Text = what ?? string.Empty;
            _result.Visible = _result.Text.Length > 0;
            return what;
        }
    }

    /// <summary>
    /// What the profiles page reads and changes.
    /// </summary>
    /// <remarks>
    /// A seam because everything behind it is a database: the list in its custom data, the rules on
    /// every node, and the write that marks KeePass as having something to save. What the page does
    /// with the answers is worth testing without any of that.
    /// </remarks>
    public interface IProfiles
    {
        /// <summary>Every profile this database names, in the order they should be shown.</summary>
        IReadOnlyList<string> Names();

        /// <summary>
        /// How many groups and entries have a rule for each profile, whatever it grants, all of
        /// them in one count. A profile no node names is absent rather than zero.
        /// </summary>
        IReadOnlyDictionary<string, int> Rules();

        /// <summary>
        /// How many admitted clients hold each profile, all of them in one count. A profile no
        /// client holds is absent rather than zero.
        /// </summary>
        IReadOnlyDictionary<string, int> Clients();

        /// <summary>Adds one. Answers with why it could not, or null when it did.</summary>
        string Add(string name);

        /// <summary>
        /// What adding a name would bring back into force, in words, or null when nothing would.
        /// </summary>
        /// <remarks>
        /// Removing a profile leaves every rule naming it where it is, inert, so adding the same
        /// name again is not a fresh start: it is those rules granting again. The page asks before
        /// it does that, which is the one moment the operator can still decide against it.
        /// </remarks>
        string Returning(string name);

        /// <summary>
        /// Removes one from the list, which leaves every rule naming it in place and granting
        /// nothing. Answers with why it could not, or null when it did.
        /// </summary>
        string Remove(string name);

        /// <summary>What the last removal did, in words, or null when it has done nothing.</summary>
        string LastRemoval { get; }
    }

    /// <summary>
    /// The answers for a dialog with no database behind it: no names, and nothing can be changed.
    /// </summary>
    /// <remarks>
    /// A page that is present and says so beats a page that is missing, which is the same reason
    /// the tab appears at all on a dialog it cannot find a database for.
    /// </remarks>
    public sealed class NoProfiles : IProfiles
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        public string LastRemoval
        {
            get { return null; }
        }

        public IReadOnlyList<string> Names()
        {
            return Nothing;
        }

        public IReadOnlyDictionary<string, int> Rules()
        {
            return new Dictionary<string, int>();
        }

        public IReadOnlyDictionary<string, int> Clients()
        {
            return new Dictionary<string, int>();
        }

        public string Add(string name)
        {
            return "No database is open on this dialog.";
        }

        public string Returning(string name)
        {
            return null;
        }

        public string Remove(string name)
        {
            return "No database is open on this dialog.";
        }
    }
}
