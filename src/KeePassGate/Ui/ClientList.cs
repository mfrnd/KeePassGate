using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Every client this database has admitted, and the one gesture that takes an admission away.
    /// </summary>
    /// <remarks>
    /// **One list, not two.** What a client may reach is shown beside its name, in the same row the
    /// revoke gesture acts on. A separate list of names with the profiles somewhere else would be
    /// two places to look, two places to keep in step, and a revoke that says nothing about what is
    /// being taken away.
    ///
    /// **On the tab rather than in a dialog.** The clients page is a page of its own, so the list
    /// has room to grow without pushing anything else off the tab, and the operator sees who is
    /// admitted without pressing anything. The tab itself still does not scroll: what scrolls is
    /// the list, inside the fixed area it is given, which is what a list is for.
    ///
    /// **Taking one away is a mark in its own row**, in the last column, rather than a button
    /// somewhere else that acts on whatever is selected. A button off to one side has to be read
    /// together with the selection to know what it would do, and the row it would act on is the one
    /// thing it does not say. The same mark, in the same place, takes a rule away in the grant
    /// editor.
    ///
    /// **It waits for the dialog's OK, like everything else here.** An earlier version acted at
    /// once, on the argument that immediacy is what a revocation is for. What settled it the other
    /// way is that a tab cannot carry two models: whichever one an operator assumes on the page
    /// they are not looking at is the one that costs them something.
    ///
    /// A grid rather than a list view, because one column is now a thing to press. Nothing else
    /// here is editable: every text column is read only and the grid adds no row of its own.
    /// </remarks>
    public sealed class ClientList : UserControl
    {
        /// <summary>What the control is called, for the tests that drive it.</summary>
        public const string ControlName = "KpgClientList";

        /// <summary>The column that takes an admission away, by name.</summary>
        public const string RevokeColumnName = "KpgRevokeColumn";

        /// <summary>The grid, by name.</summary>
        public const string ListName = "KpgClientListView";

        /// <summary>
        /// What a screen reader calls the grid, which has no caption of its own to be called by.
        /// </summary>
        /// <remarks>
        /// A phrase to be read aloud rather than a lookup name, which is the whole distinction
        /// `CONVENTIONS.md` draws: the desktop tests point at controls by their `AutomationId`,
        /// which comes from `Name` and is untouched by this, so a grid announcing
        /// "KpgClientListView" would have been reading an identifier to somebody who cannot see
        /// the table it belongs to. Without one the grid announces nothing at all, and a table is
        /// exactly the kind of control the convention names.
        /// </remarks>
        public const string ListLabel = "Clients admitted to this database";

        /// <summary>What is said when the database has admitted nobody.</summary>
        public const string NobodyText = "No client has been admitted to this database.";

        /// <summary>What is said when the admitted clients could not be listed at all.</summary>
        public const string UnreadableText =
            "The admitted clients could not be read. This list is not what the database holds.";

        /// <summary>Said beside a certificate that is past its dates.</summary>
        public const string ExpiredText = "expired";

        /// <summary>
        /// Said beside a certificate this database's authority did not issue.
        /// </summary>
        /// <remarks>
        /// In place of the date rather than beside it, because the date is the one thing about
        /// such a row that is not true: the gate pins the anchor before it looks at anything else,
        /// so a certificate that chains somewhere else is refused whatever its own dates say. A
        /// row reading `2034-01-01` there is a row promising access nobody has.
        ///
        /// The row stays, for the reason an unreadable certificate's row stays: it is still a
        /// record, and taking it away is still the operator's to do.
        /// </remarks>
        public const string StrangerText = "not this authority";

        /// <summary>What the last column says, and what pressing it means.</summary>
        public const string RevokeMark = "X";

        /// <summary>
        /// Said after a profile a client holds that the database no longer names.
        /// </summary>
        /// <remarks>
        /// A record keeps every name it was approved for, and the gate grants a client only the
        /// ones the database still names, so such a name is held and grants nothing. It is listed,
        /// dimmed, rather than left out, because adding the name back gives it to this client
        /// again, and the row is where the operator looks to see who holds what.
        /// </remarks>
        public const string RemovedSuffix = " (removed)";

        private const int ClientColumn = 0;
        private const int ProfilesColumn = 1;
        private const int AdmittedColumn = 2;
        private const int UntilColumn = 3;
        private const int RevokeColumn = 4;

        private readonly Func<AdmissionRecord, string> _revoke;
        private readonly DataGridView _grid;
        private readonly Label _note;

        /// <summary>The profiles the database names, or null when nobody said.</summary>
        private ICollection<string> _named;

        /// <summary>What the last gesture said, kept because the label also says other things.</summary>
        private string _said = string.Empty;
        private readonly List<AdmissionRecord> _records = new List<AdmissionRecord>();

        private bool _filling;

        // Whether the last thing filled in was the whole truth. Starts true because an empty
        // list that nobody has filled yet is empty, not unreadable.
        private bool _readable = true;

        private readonly Label _reappeared;

        /// <summary>How many revoked clients a merge brought back, as the last listing counted.</summary>
        private int _cameBack;

        /// <param name="revoke">
        /// Takes an admission away and answers with why it could not, or null when it did. The list
        /// does not write to a database itself: it is a list.
        /// </param>
        public ClientList(Func<AdmissionRecord, string> revoke)
        {
            if (revoke == null)
            {
                throw new ArgumentNullException(nameof(revoke));
            }

            _revoke = revoke;

            Name = ControlName;
            Dock = DockStyle.Fill;

            // The air between the invitation above and this table belongs here rather than under
            // the line up there: a widget does not carry padding for what comes after it, and the
            // last thing in that band would then be padded against the edge of its own container.
            Padding = new Padding(0, Spacing.Gap, 0, 0);

            _grid = BuildGrid();

            // **One line under the grid rather than two.** There were two: one saying nobody is
            // admitted and one saying what the last gesture did, and both are gray, both sit in
            // the same place, and both are about the same list. Removing the only client made both
            // appear at once and spent two bands of this page saying one thing twice.
            //
            // What the last gesture did wins where they would collide, because it is the newer
            // answer and it is the one the operator is waiting for; that the list is now empty is
            // in front of them anyway, in the empty grid above.
            //
            // Under the grid rather than over it. An empty grid still has its headings, which say
            // what would be listed here, and a message covering them says less. Keeping the grid on
            // screen also keeps it laid out: a hidden control is never given a width, so its columns
            // keep whatever they were built with until somebody is admitted.
            _note = new TextLabel
            {
                Name = ResultLabelName,
                Dock = DockStyle.Bottom,

                // Sized to its text, so the band ends where the sentence does. A written down
                // height leaves a strip of nothing under the last thing on the page, which is
                // height this tab does not have to give away.
                AutoSize = true,
                Padding = new Padding(0, Spacing.Gap, 0, 0),
                ForeColor = SystemColors.GrayText,
                Visible = false
            };

            // **A line of its own for revoked clients a merge brought back**, rather than a turn on
            // the one above. That line is the newest answer and gives way to the next; this one is
            // a standing fact about the file, true until the next write this plugin makes takes
            // those records away, and a gesture on this page must not hide it meanwhile. It is
            // there only after a merge brought one back, so the page is otherwise as it was.
            _reappeared = new TextLabel
            {
                Name = ReappearedLabelName,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(0, Spacing.Gap, 0, 0),
                ForeColor = SystemColors.GrayText,
                Visible = false
            };

            // The filling control goes in first. Docking is applied from the end of the collection
            // backwards, so whatever is added last takes its band first and index zero is left with
            // what remains: the gesture's line at the bottom, and this one straight under the list.
            // `OnLayout` keeps them in that order, which adding them is not enough to do.
            Controls.Add(_grid);
            Controls.Add(_reappeared);
            Controls.Add(_note);

            UpdateEmptiness();
        }

        /// <summary>
        /// Puts the list and the two lines under it back in the order docking reads, before it
        /// reads it.
        /// </summary>
        /// <remarks>
        /// **WinForms moves a child in `Controls` to match its window**, and the two lines had
        /// swapped by the time the page was first in front. A line made visible before the page
        /// has a window is given a window of its own there and then, and when the page's window
        /// arrives it is moved to where that window lands: straight after the last sibling that
        /// already has one. The gesture's line is visible from the start, saying nobody is
        /// admitted, and the line between it and the grid has no window until a merge shows it,
        /// so the gesture's line took its place under the grid and the count of revoked clients
        /// went to the bottom. Docking reads that order and nothing else here does, so it is put
        /// back where it is about to be read, whichever window was made first.
        /// </remarks>
        protected override void OnLayout(LayoutEventArgs e)
        {
            // Every child that has been added so far, since the constructor lays this out as it
            // adds them.
            var order = new Control[] { _grid, _reappeared, _note };
            for (var at = 0; at < order.Length; at++)
            {
                var child = order[at];
                if (child != null && child.Parent == this && Controls.GetChildIndex(child) != at)
                {
                    Controls.SetChildIndex(child, at);
                }
            }

            base.OnLayout(e);
        }

        /// <summary>What the last gesture said, by name.</summary>
        public const string ResultLabelName = "KpgClientResultLabel";

        /// <summary>The line counting revoked clients a merge brought back, by name.</summary>
        public const string ReappearedLabelName = "KpgClientReappearedLabel";

        /// <summary>
        /// What is said under the list while records of revoked clients are back in the database.
        /// </summary>
        /// <remarks>
        /// Only a merge does that: a copy of the file older than a revocation brought the record
        /// back, profiles and all. The revocation beside it never moves, so the gate refuses the
        /// certificate, and the record is not listed as a client. It is counted here rather than
        /// passed over, because it tells the operator a stale copy was merged in; the next write
        /// this plugin makes takes the record away, and the line with it.
        /// </remarks>
        public static string ReappearedText(int count)
        {
            return count == 1
                ? "1 revoked client reappeared in a merge; it stays refused."
                : count.ToString(CultureInfo.InvariantCulture)
                  + " revoked clients reappeared in a merge; they stay refused.";
        }

        /// <summary>
        /// What the line under the list says about revoked clients a merge brought back, or null
        /// when it says nothing.
        /// </summary>
        public string Reappeared
        {
            get { return _cameBack > 0 ? _reappeared.Text : null; }
        }

        /// <summary>What the list is showing, in order. The test seam, and the tab's seam.</summary>
        public IReadOnlyList<AdmissionRecord> Clients
        {
            get { return _records; }
        }

        /// <summary>What the last gesture said, or null when it has said nothing.</summary>
        public string Result
        {
            get { return _said.Length == 0 ? null : _said; }
        }

        /// <summary>
        /// Take one client's admission away, by its place in the list.
        /// </summary>
        /// <remarks>
        /// Public rather than only wired to the column, because a cell cannot be pressed on a
        /// control that was never shown, silently, and a test that pressed one would pass while
        /// proving nothing.
        /// </remarks>
        public string Revoke(int index)
        {
            if (index < 0 || index >= _records.Count)
            {
                return Said("There is no such client.");
            }

            var record = _records[index];
            var refused = _revoke(record);

            if (refused != null)
            {
                return Said("That client is still admitted: " + refused);
            }

            // Found again rather than trusting the index, because what the callback does is the
            // host's business: a host that refills the list in answer to the change is a reasonable
            // host, and one that removed a row by a stale index would be removing somebody else's.
            var at = _records.IndexOf(record);
            if (at >= 0)
            {
                _records.RemoveAt(at);

                if (at < _grid.Rows.Count)
                {
                    _grid.Rows.RemoveAt(at);
                }
            }

            UpdateEmptiness();

            return Said(Describe(record) + " can no longer reach this database.");
        }

        /// <summary>
        /// Show a set of records, replacing whatever is there. Named so as not to overload
        /// `Control.Show`, which does something else entirely and would be reached by accident.
        /// </summary>
        /// <param name="strangers">
        /// The admission keys of the records this database's authority did not issue, or null
        /// when nothing here is one. Decided by the caller rather than here, because answering it
        /// means reading the authority out of the database and this control reads nothing: it is
        /// a list.
        /// </param>
        /// <param name="named">
        /// The profiles the database names, so that one a client holds and the database no longer
        /// names is shown as removed. Null when nothing here needs marking, which is not the same
        /// as an empty list: a database whose list names nothing marks every profile any client
        /// holds, as the gate grants none of them.
        /// </param>
        public void Fill(AdmissionListing listing, ICollection<string> strangers, ICollection<string> named)
        {
            _filling = true;

            try
            {
                _named = named;
                _records.Clear();
                _grid.Rows.Clear();

                // Taken as a listing rather than as records, because the difference between a
                // database that admitted nobody and one that would not say is invisible in the
                // rows and is the whole of what this page is for.
                _readable = listing == null || listing.Complete;

                _cameBack = listing == null ? 0 : listing.CameBack;

                if (listing != null)
                {
                    foreach (var record in listing.Records)
                    {
                        if (record == null)
                        {
                            continue;
                        }

                        _records.Add(record);
                        _grid.Rows.Add(
                            Describe(record),
                            record.Profiles.Count == 0
                                ? "nothing"
                                : string.Join(Environment.NewLine, Profiles(record)),
                            Stamp(record.Admitted),
                            Reach(record, strangers, DateTimeOffset.UtcNow),
                            RevokeMark);
                    }
                }
            }
            finally
            {
                _filling = false;
            }

            UpdateEmptiness();
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
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,

                // **One row per client, however many profiles it holds.** A client is one thing
                // that was admitted once and is revoked once, so it is one row; what varies is how
                // much it was admitted to. Comma separated, that ran along a column narrow enough
                // to need reading twice, and a profile that wrapped mid-name read as two. A line
                // each is the shape of the thing: a list.
                //
                // The row grows to hold them rather than the list being cut, and the cells are
                // read across as well as down, so everything in the row starts at the same line:
                // a name level with the middle of three profiles is a name that looks like it
                // belongs to the second one.
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };

            // Top, and read across as well as down: a name level with the middle of three
            // profiles is a name that looks like it belongs to the second one.
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;

            // **Nothing wraps except the profiles.** A wrapped name, date or fingerprint is one
            // value that reads as two, and the columns holding those are sized to what they hold
            // rather than sharing what is left, so they have no reason to fold. The profiles are a
            // list and the only cell here whose height is somebody else's decision.
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

            // The two that hold text of no known length share whatever the rest leave: a name or a
            // list of profiles is as long as somebody made it. The two timestamps and the mark are
            // as wide as their content, because what they hold has one shape and room beyond it is
            // room nothing can use.
            grid.Columns.Add(Filling("Client", 60));
            grid.Columns.Add(Filling("Profiles", 40));
            grid.Columns.Add(Fitted("Admitted"));
            grid.Columns.Add(Fitted("Until"));

            var revoke = new DataGridViewButtonColumn
            {
                Name = RevokeColumnName,
                HeaderText = string.Empty,
                Text = RevokeMark,
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                MinimumWidth = GateIcon.Room,
                ToolTipText = Tooltips.Wrapped(
                    "Takes this client's admission away when OK is pressed. It is then "
                    + "refused from its next request onwards, and saving the database keeps that.")
            };

            grid.Columns.Add(revoke);

            grid.Columns[ProfilesColumn].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.CellClick += OnCellClick;
            grid.CellPainting += OnCellPainting;

            return grid;
        }

        /// <summary>
        /// Draws a bin over the heading of the column that takes something away, and dims the
        /// removed profiles a client holds.
        /// </summary>
        /// <remarks>
        /// The heading only. The buttons under it keep their mark: what a button does is said by
        /// the column it is in, and a picture in every row is a picture repeated once per row.
        /// </remarks>
        private void OnCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex == ProfilesColumn && e.RowIndex >= 0)
            {
                PaintProfiles(e);
                return;
            }

            if (e.ColumnIndex != RevokeColumn || e.RowIndex >= 0)
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

        private static DataGridViewTextBoxColumn Filling(string header, int weight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight,
                ReadOnly = true
            };
        }

        private static DataGridViewTextBoxColumn Fitted(string header)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                ReadOnly = true
            };
        }

        private void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0 || e.ColumnIndex != RevokeColumn)
            {
                return;
            }

            Revoke(e.RowIndex);
        }

        private string[] Profiles(AdmissionRecord record)
        {
            var names = new string[record.Profiles.Count];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = Removed(record.Profiles[i])
                    ? record.Profiles[i] + RemovedSuffix
                    : record.Profiles[i];
            }

            return names;
        }

        /// <summary>Whether a profile a client holds is one the database no longer names.</summary>
        private bool Removed(string profile)
        {
            return _named != null && !_named.Contains(profile);
        }

        /// <summary>
        /// Draws a profiles cell holding a removed name one line at a time, the removed ones in
        /// gray.
        /// </summary>
        /// <remarks>
        /// A cell has one color, and the point is that a row holding `reader` and a removed `ops`
        /// says which of the two still grants anything; the words say it and the gray says it at a
        /// glance. Drawn only where there is something to dim, so every other cell is the grid's own
        /// painting and nothing about it can drift from the rest.
        /// </remarks>
        private void PaintProfiles(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= _records.Count)
            {
                return;
            }

            var record = _records[e.RowIndex];
            var any = false;
            foreach (var profile in record.Profiles)
            {
                any |= Removed(profile);
            }

            if (!any)
            {
                return;
            }

            e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

            var selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var ink = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;

            // Gray on the selection's blue is barely there, so a selected row dims to a light shade
            // of its own background instead, which reads as dimmer than the lines beside it.
            var dim = selected
                ? ControlPaint.Light(e.CellStyle.SelectionBackColor)
                : SystemColors.GrayText;

            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                        | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

            // Where a wrapping text box cell draws its text: inside the border it owns on the right
            // and at the bottom, inside the padding, and one pixel down. Anything narrower wraps a
            // line the grid sized the row for as one, and the row cuts it off.
            var bounds = new Rectangle(
                e.CellBounds.Left + e.CellStyle.Padding.Left,
                e.CellBounds.Top + e.CellStyle.Padding.Top + 1,
                Math.Max(1, e.CellBounds.Width - 1 - e.CellStyle.Padding.Horizontal),
                Math.Max(1, e.CellBounds.Height - 1 - e.CellStyle.Padding.Vertical - 2));

            var top = bounds.Top;
            foreach (var profile in record.Profiles)
            {
                var removed = Removed(profile);
                var line = removed ? profile + RemovedSuffix : profile;
                var height = TextRenderer.MeasureText(
                    e.Graphics, line, e.CellStyle.Font, new Size(bounds.Width, 0), flags).Height;

                TextRenderer.DrawText(
                    e.Graphics,
                    line,
                    e.CellStyle.Font,
                    new Rectangle(bounds.Left, top, bounds.Width, height),
                    removed ? dim : ink,
                    flags);

                top += height;
            }

            e.Handled = true;
        }

        /// <summary>
        /// What to call a client. The subject as issued, without the attribute that wraps it, since
        /// every one of these reads `CN=` and a column of identical prefixes says nothing.
        /// </summary>
        /// <remarks>
        /// Internal, because the warnings that name clients, on the Profiles page and on the
        /// approval dialog, have to call each one what this page calls it.
        /// </remarks>
        internal static string Describe(AdmissionRecord record)
        {
            var subject = record.Subject ?? string.Empty;
            return subject.StartsWith("CN=", StringComparison.Ordinal) ? subject.Substring(3) : subject;
        }

        /// <summary>
        /// How long this record still reaches the database, or why it does not reach it at all.
        /// </summary>
        /// <remarks>
        /// The anchor is asked before the dates, because that is the order the gate asks in and
        /// because an answer about the dates of a certificate this authority never issued is an
        /// answer about the wrong thing. `AnchorPin` refuses first, so the record is inert
        /// whatever its `notAfter` says.
        /// </remarks>
        private static string Reach(AdmissionRecord record, ICollection<string> strangers, DateTimeOffset now)
        {
            if (strangers != null && strangers.Contains(record.AdmissionKey))
            {
                return StrangerText;
            }

            return Until(record, now);
        }

        /// <summary>
        /// When the certificate stops being usable, or that it already has.
        /// </summary>
        /// <remarks>
        /// Said in the cell rather than only in a color, because a row that has expired and a row
        /// somebody revoked look the same from across a desk, and only one of them needs doing
        /// anything about.
        ///
        /// **Asked through `CertificateValidity`, because the gate draws this line and one page
        /// drawing it again draws it somewhere else.** `expires &lt;= now` calls a certificate
        /// expired at the instant `now &gt; notAfter` is still serving it, which is the same
        /// boundary the tab and the gate had already drifted apart on once and the reason that
        /// class exists. `AuthorityDescription` counts the expired clients through it too, so a
        /// row and the sentence above it cannot disagree either.
        ///
        /// Public, and given the moment rather than reading a clock, because the disagreement is
        /// one second wide and no test can stand on it through a clock it does not hold.
        /// </remarks>
        public static string Until(AdmissionRecord record, DateTimeOffset now)
        {
            try
            {
                using (var certificate = record.Certificate())
                {
                    var expires = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                    return CertificateValidity.HasExpired(expires, now)
                        ? ExpiredText
                        : Stamp(expires);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                // Every record here parsed its certificate once already: a record is loaded only
                // when the bytes it stores are a certificate whose digest is its key, and one that
                // is not is no record and has no row. What this catches is the platform failing to
                // parse the same bytes a second time, and then the row keeps its place and its
                // revocation, and says nothing about dates it could not read.
                return string.Empty;
            }
        }

        /// <summary>
        /// A moment, to the second, in UTC. The same shape the authority's own dates are written
        /// in, because these sit on two pages of one tab and reading two formats is reading twice.
        /// </summary>
        private static string Stamp(DateTimeOffset moment)
        {
            return Moments.OnScreen(moment);
        }

        private string Said(string what)
        {
            _said = what ?? string.Empty;
            Note();
            return what;
        }

        private void UpdateEmptiness()
        {
            Note();
        }

        /// <summary>
        /// Puts the one line under the grid, which is the newer of the two things it can say.
        /// </summary>
        private void Note()
        {
            // What could not be read outranks the gesture, where everything else loses to it.
            // `Revoked agent-x.` over a grid that is empty because nothing could be listed reads
            // as though the revocation emptied it, which is the one wrong thing this page can
            // say about who still has access.
            var text = !_readable
                ? UnreadableText
                : _said.Length > 0
                    ? _said
                    : _records.Count == 0 ? NobodyText : string.Empty;

            _note.Text = text;
            _note.Visible = text.Length > 0;

            _reappeared.Text = _cameBack > 0 ? ReappearedText(_cameBack) : string.Empty;
            _reappeared.Visible = _cameBack > 0;
        }
    }
}
