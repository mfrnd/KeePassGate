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
    ///
    /// **A standing answer to a client's renewals is shown on its row and lifted from it**, which
    /// is the operator's ruling on review 3, chosen on real screenshots over a column and a button
    /// that did not fit. There is no column for it: the client's name is drawn in the tab's own red
    /// while an answer of any of the three kinds stands, the row's text cells say which in their
    /// tooltip, and the row tells a screen reader the same words as its description. The row's
    /// menu, opened by the right button, Shift and F10 or the menu key, states the answer on a
    /// grayed first line and offers **Allow renewal** under it, beside **Revoke**. Allow renewal
    /// waits for the dialog's OK as a revocation does, and the name stops being red at once as a
    /// revoked row goes at once.
    ///
    /// **Each row's client key is in its tooltip**, whole, with the certificate's own key under
    /// it, which is the operator's ruling of 2026-10-16: a client chooses its own name, so two rows
    /// under one name are one client that renewed or two programs that chose it, and the key is
    /// what tells which. The ruling asked for the key beside the name, shortened, where there is
    /// room, and there is none: measured, a column of eight characters in the code font left the
    /// names `GateTabFitsTests` holds the page to cut at 100, 125 and 150 percent, "agent-fictitious"
    /// needing 76 pixels of 67 at the first, and the page does not scroll, so the tooltip is where
    /// it went.
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
        /// The row stays: it is still a record, and taking it away is still the operator's to do.
        /// </remarks>
        public const string StrangerText = "not this authority";

        /// <summary>
        /// Said beside a certificate this authority issued for something other than a client.
        /// </summary>
        /// <remarks>
        /// In place of the date, for the reason <see cref="StrangerText"/> is: the gate asks for
        /// client authentication before it looks at the dates, and refuses a certificate issued
        /// for the host whatever they say. Only a file written by hand or merged from another
        /// copy holds such a record, since pairing records only the client certificates it issues.
        /// </remarks>
        public const string NotForClientsText = "not for client use";

        /// <summary>Said beside a certificate whose first valid moment has not come yet.</summary>
        /// <remarks>
        /// The gate honors `notBefore` exactly and refuses the certificate until then, so a date a
        /// year away beside it read as a client that works. Reached by a clock that is wrong on
        /// the machine that issued it or on the one reading the file now.
        /// </remarks>
        public const string NotYetValidText = "not yet valid";

        /// <summary>
        /// Said where the date would be beside a stored record that will not read.
        /// </summary>
        /// <remarks>
        /// Such a record has nothing else to say: no subject, no profiles, no admission time. It
        /// admits nobody, and it is listed all the same, by its key, because this page is the one
        /// place its X can take it away; otherwise the only way to be rid of it was to recreate the
        /// authority, which makes every client pair again, or to reach into the database's plugin
        /// data by hand.
        /// </remarks>
        public const string UnreadableRecordText = "cannot be read";

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

        /// <summary>The row's menu, by name.</summary>
        public const string MenuName = "KpgClientMenu";

        /// <summary>The menu's first line, stating the answer that stands, by name.</summary>
        public const string AnswerItemName = "KpgClientAnswerItem";

        /// <summary>The menu's Allow renewal, by name.</summary>
        public const string AllowRenewalItemName = "KpgAllowRenewalItem";

        /// <summary>The menu's Revoke, by name.</summary>
        public const string RevokeItemName = "KpgRevokeItem";

        /// <summary>What lifts a standing answer, with its access key.</summary>
        public const string AllowRenewalText = "&Allow renewal";

        /// <summary>What takes an admission away from the menu, as the X does, with its access key.</summary>
        public const string RevokeText = "&Revoke";

        /// <summary>
        /// What a screen reader calls the row's menu, which has no caption of its own.
        /// </summary>
        public const string MenuLabel = "Client actions";

        /// <summary>
        /// The red a client's name is drawn in while an answer to its renewals stands: the tab's
        /// own, the notice over the invite button's, 6.68 to 1 on the window's white.
        /// </summary>
        /// <remarks>
        /// The foreground only, so a selected row keeps the grid's own colors, as a dimmed profile
        /// on a selected row does: red on the selection's blue would be read by nobody.
        /// </remarks>
        public static readonly Color StandingColor = Color.Firebrick;

        private const int ClientColumn = 0;
        private const int ProfilesColumn = 1;
        private const int AdmittedColumn = 2;
        private const int UntilColumn = 3;
        private const int RevokeColumn = 4;

        private readonly Func<string, string> _revoke;
        private readonly Func<string, string> _allowRenewal;
        private readonly DataGridView _grid;
        private readonly Label _note;

        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _answerItem;
        private readonly ToolStripSeparator _answerSeparator;
        private readonly ToolStripMenuItem _allowItem;
        private readonly ToolStripMenuItem _revokeItem;

        /// <summary>
        /// The key of the record the menu was opened on, so that a choice acts on that client
        /// whatever a redraw has done to the rows meanwhile.
        /// </summary>
        private string _menuKey;

        /// <summary>What was chosen while the menu was open, acted on once it has closed.</summary>
        private ToolStripItem _chosen;

        /// <summary>Each listed client's standing answer, by admission key; absent is none.</summary>
        private readonly Dictionary<string, RenewalRefusal> _standing =
            new Dictionary<string, RenewalRefusal>(StringComparer.Ordinal);

        /// <summary>The profiles the database names, or null when nobody said.</summary>
        private ICollection<string> _named;

        /// <summary>What the last gesture said, kept because the label also says other things.</summary>
        private string _said = string.Empty;
        private readonly List<AdmissionRecord> _records = new List<AdmissionRecord>();

        /// <summary>The admission keys of the stored records that would not read, listed after the rest.</summary>
        private readonly List<string> _unreadable = new List<string>();

        private bool _filling;

        // Whether the last thing filled in was the whole truth. Starts true because an empty
        // list that nobody has filled yet is empty, not unreadable.
        private bool _readable = true;

        private readonly Label _reappeared;

        /// <summary>How many revoked clients a merge brought back, as the last listing counted.</summary>
        private int _cameBack;

        /// <summary>The list, with nothing behind Allow renewal.</summary>
        /// <param name="revoke">
        /// Takes an admission away, by its key, and answers with why it could not, or null when it
        /// did. By key, because a record that will not read has nothing else to be named by. The
        /// list does not write to a database itself: it is a list.
        /// </param>
        public ClientList(Func<string, string> revoke)
            : this(revoke, key => "nothing here lifts an answer")
        {
        }

        /// <summary>The list, with the host behind Allow renewal as well as each row's X.</summary>
        /// <param name="revoke">As above.</param>
        /// <param name="allowRenewal">
        /// Lifts the standing answer to renewing one certificate, by its key, and answers with why
        /// it could not, or null when it did. Like a revocation it waits for the dialog's OK, and
        /// like one it is the host's to do: the list reads and writes no database.
        /// </param>
        public ClientList(Func<string, string> revoke, Func<string, string> allowRenewal)
        {
            if (revoke == null)
            {
                throw new ArgumentNullException(nameof(revoke));
            }

            if (allowRenewal == null)
            {
                throw new ArgumentNullException(nameof(allowRenewal));
            }

            _revoke = revoke;
            _allowRenewal = allowRenewal;

            Name = ControlName;
            Dock = DockStyle.Fill;

            // The air between the invitation above and this table belongs here rather than under
            // the line up there: a widget does not carry padding for what comes after it, and the
            // last thing in that band would then be padded against the edge of its own container.
            Padding = new Padding(0, Spacing.Gap, 0, 0);

            _grid = BuildGrid();

            // **Allow renewal sits on the row's own menu, beside Revoke**, so what it acts on is
            // the row it was opened on rather than whatever a button off to one side finds
            // selected, which is the argument the X in each row already won. A menu holding
            // nothing but its items owns Enter and Escape while it is open, measured for the
            // renewal dialog's Refuse menu, so the settings dialog's OK and Cancel never see them
            // and nothing is taken from the dialog here.
            _menu = new ContextMenuStrip
            {
                Name = MenuName,
                AccessibleName = MenuLabel,
                ShowImageMargin = false,
                ShowItemToolTips = true
            };

            // **The answer as the first line, grayed out**, because it is a statement rather than
            // a command: the red on the row says that an answer stands, and this says which,
            // straight above the item that lifts it. Shown only while one does.
            _answerItem = new ToolStripMenuItem { Name = AnswerItemName, Enabled = false };
            _answerSeparator = new ToolStripSeparator();

            _allowItem = new ToolStripMenuItem(AllowRenewalText)
            {
                Name = AllowRenewalItemName,
                ToolTipText = Tooltips.Wrapped(
                    "Lifts the standing answer to renewing this certificate when OK is pressed. Its "
                    + "next renewal then raises the dialog again.")
            };

            _revokeItem = new ToolStripMenuItem(RevokeText)
            {
                Name = RevokeItemName,
                ToolTipText = Tooltips.Wrapped(
                    "Takes this certificate's admission away when OK is pressed, as the X does.")
            };

            _menu.Items.AddRange(new ToolStripItem[] { _answerItem, _answerSeparator, _allowItem, _revokeItem });

            // **A choice is acted on a message after the menu has closed**, as on the Refuse
            // menu: `ItemClicked` is raised while the menu is still on screen, and what both
            // choices do redraws the grid underneath it, which is what `GrantPanel.Finish` records
            // a dropdown not surviving. The menu is never disposed there either; it goes with this
            // list. And nothing closes it by code, which left the next window's menu closing as it
            // opened.
            _menu.ItemClicked += (sender, e) => _chosen = e.ClickedItem;
            _menu.Closed += OnMenuClosed;
            ((ClientGrid)_grid).MenuAsked = ShowMenu;

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

        /// <summary>
        /// The clients the list is showing, in order: every row but those of records that will not
        /// read, which follow them. The test seam, and the tab's seam.
        /// </summary>
        public IReadOnlyList<AdmissionRecord> Clients
        {
            get { return _records; }
        }

        /// <summary>
        /// The admission keys of the stored records that will not read, in the order their rows
        /// follow the clients.
        /// </summary>
        public IReadOnlyList<string> Unreadable
        {
            get { return _unreadable; }
        }

        /// <summary>What the last gesture said, or null when it has said nothing.</summary>
        public string Result
        {
            get { return _said.Length == 0 ? null : _said; }
        }

        /// <summary>The row's menu, for the tests that open it.</summary>
        internal ContextMenuStrip Menu
        {
            get { return _menu; }
        }

        /// <summary>
        /// The standing answer to renewing one client, by its place in the list, or null when none
        /// stands or there is no such client.
        /// </summary>
        public RenewalRefusal StandingAt(int index)
        {
            return index < 0 || index >= _records.Count ? null : Standing(_records[index]);
        }

        /// <summary>
        /// What the row, its tooltip and its menu say about a standing answer, or null for none.
        /// </summary>
        /// <remarks>
        /// **The day of an until-date is the operator's own**, read in this computer's zone, where
        /// every other moment on this page is UTC. The dialog asked for a day on a calendar and the
        /// answer ends where that day begins, so the day chosen is the one to give back: the same
        /// moment in UTC would name the evening before for anybody east of Greenwich.
        /// </remarks>
        public static string AnswerText(RenewalRefusal standing)
        {
            if (standing == null)
            {
                return null;
            }

            if (standing.IsThisSession)
            {
                return "Renewal refused in this session";
            }

            return standing.IsForever
                ? "Renewal refused forever"
                : "Renewal refused until " + standing.NotBefore.Value.ToLocalTime()
                    .ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Lift the standing answer to renewing one client, by its place in the list.
        /// </summary>
        /// <remarks>
        /// Whichever of the three it is. Like a revocation it goes into what the dialog holds and
        /// reaches the database and the gate when the dialog is accepted, and like a revocation the
        /// row shows it at once: the name stops being red.
        ///
        /// Public rather than only wired to the menu, for the reason <see cref="Revoke"/> is.
        /// </remarks>
        public string AllowRenewal(int index)
        {
            if (index < 0 || index >= _records.Count)
            {
                return Said("There is no such client.");
            }

            var record = _records[index];
            var name = Named(record);
            if (Standing(record) == null)
            {
                return Said(name + " has no standing answer: its next renewal raises the dialog.");
            }

            var refused = _allowRenewal(record.AdmissionKey);
            if (refused != null)
            {
                return Said("The answer to renewing that client still stands: " + refused);
            }

            // Found again rather than trusting the index, for the reason a revoked row is: a host
            // that redraws the list in answer to the change has already drawn it without the red.
            _standing.Remove(record.AdmissionKey);
            var at = _records.IndexOf(record);
            if (at >= 0 && at < _grid.Rows.Count)
            {
                Mark(_grid.Rows[at], record, null);
            }

            return Said("The next renewal of " + name + " raises the dialog again.");
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
            if (index >= _records.Count && index < _records.Count + _unreadable.Count)
            {
                return RevokeUnreadable(_unreadable[index - _records.Count]);
            }

            if (index < 0 || index >= _records.Count)
            {
                return Said("There is no such client.");
            }

            var record = _records[index];
            var refused = _revoke(record.AdmissionKey);

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
                _standing.Remove(record.AdmissionKey);

                if (at < _grid.Rows.Count)
                {
                    _grid.Rows.RemoveAt(at);
                }
            }

            UpdateEmptiness();

            var name = Named(record);

            // A renewed client is two rows with one client key until it gives the older up, and
            // taking one of them away leaves the client reaching the database through the other,
            // so the sentence says which certificate went rather than that the client did.
            if (_records.Exists(other => string.Equals(other.ClientKey, record.ClientKey, StringComparison.Ordinal)))
            {
                return Said("The certificate admitted for " + name + " at " + Stamp(record.Admitted) +
                            " is revoked; its other certificate still reaches this database.");
            }

            return Said(name + " can no longer reach this database.");
        }

        /// <summary>
        /// What a sentence under the list calls this record's client: its name, and the start of
        /// its client key as well when another client listed here goes by the same name.
        /// </summary>
        /// <remarks>
        /// Asked of the rows as they stand and of this record, which a revocation has just taken
        /// off them, so that the client it named is told apart from one still listed.
        /// </remarks>
        private string Named(AdmissionRecord record)
        {
            var listed = new List<AdmissionRecord>(_records);
            if (!listed.Contains(record))
            {
                listed.Add(record);
            }

            var name = Describe(record);
            var shared = listed.Exists(other =>
                string.Equals(Describe(other), name, StringComparison.Ordinal)
                && !string.Equals(other.ClientKey, record.ClientKey, StringComparison.Ordinal));

            return shared
                ? name + " (" + ShortKeys(listed.ConvertAll(other => other.ClientKey))[record.ClientKey] + ")"
                : name;
        }

        /// <summary>Takes away a record that will not read, which admitted nobody.</summary>
        private string RevokeUnreadable(string admissionKey)
        {
            var refused = _revoke(admissionKey);

            if (refused != null)
            {
                return Said("That record is still there: " + refused);
            }

            // Found again, for the reason a client's row is.
            var at = _unreadable.IndexOf(admissionKey);
            if (at >= 0)
            {
                _unreadable.RemoveAt(at);

                var row = _records.Count + at;
                if (row < _grid.Rows.Count)
                {
                    _grid.Rows.RemoveAt(row);
                }
            }

            UpdateEmptiness();

            return Said("The record that could not be read is taken away.");
        }

        /// <summary>
        /// Show a set of records, replacing whatever is there. Named so as not to overload
        /// `Control.Show`, which does something else entirely and would be reached by accident.
        /// </summary>
        /// <param name="reaches">
        /// How far each record's certificate gets with the gate before its record is looked at, by
        /// admission key, or null when nothing is known about that. Decided by the caller rather
        /// than here, because answering it means reading the authority out of the database and
        /// this control reads nothing: it is a list.
        /// </param>
        /// <param name="named">
        /// The profiles the database names, so that one a client holds and the database no longer
        /// names is shown as removed. Null when nothing here needs marking, which is not the same
        /// as an empty list: a database whose list names nothing marks every profile any client
        /// holds, as the gate grants none of them.
        /// </param>
        public void Fill(
            AdmissionListing listing, IDictionary<string, ClientReach> reaches, ICollection<string> named)
        {
            Fill(listing, reaches, named, null);
        }

        /// <summary>The same, naming in red each client whose renewals stand refused.</summary>
        /// <param name="listing">As above.</param>
        /// <param name="reaches">As above.</param>
        /// <param name="named">As above.</param>
        /// <param name="standing">
        /// The standing answer to renewing each client, by admission key, the database's and this
        /// session's asked in the order the gate asks them. A client with none is absent, and so is
        /// one whose day has come. Null when nobody said, which shows none. Decided by the caller,
        /// for the reason the reaches are: answering it means reading the database.
        /// </param>
        public void Fill(
            AdmissionListing listing,
            IDictionary<string, ClientReach> reaches,
            ICollection<string> named,
            IDictionary<string, RenewalRefusal> standing)
        {
            _filling = true;

            // A listing read from the database is the newer answer than whatever the last press
            // said about it: a redraw a landing caused, a renewal or a client revoking itself,
            // could otherwise leave a client listed under a line saying it can no longer reach
            // the database. A press says its line after the redraw it causes, so it keeps it.
            _said = string.Empty;

            try
            {
                _named = named;
                _records.Clear();
                _standing.Clear();
                _unreadable.Clear();
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

                        RenewalRefusal answer;
                        if (standing != null && standing.TryGetValue(record.AdmissionKey, out answer) && answer != null)
                        {
                            _standing[record.AdmissionKey] = answer;
                        }

                        _records.Add(record);
                        var row = _grid.Rows.Add(
                            Describe(record),
                            record.Profiles.Count == 0
                                ? "nothing"
                                : string.Join(Environment.NewLine, Profiles(record)),
                            Stamp(record.Admitted),
                            Reach(record, reaches, DateTimeOffset.UtcNow),
                            RevokeMark);
                        Mark(_grid.Rows[row], record, Standing(record));
                    }

                    // **One row per stored record, including those that will not read**, after the
                    // clients. Such a record admits nobody and has nothing to show but its key,
                    // which is the start of the name it is stored under; it is listed because its
                    // X is the one way to take it away that does not make every client pair again.
                    foreach (var key in listing.Unreadable)
                    {
                        _unreadable.Add(key);
                        _grid.Rows.Add(
                            CertificateDigest.Shorten(key), string.Empty, string.Empty, UnreadableRecordText, RevokeMark);
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
            var grid = new ClientGrid
            {
                Name = ListName,

                // Rows that tell a screen reader the standing answer about their client.
                RowTemplate = new ClientRow(),
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

                // **One row per admission record, however many profiles it holds.** A record is one
                // thing that was admitted once and is revoked once, so it is one row; what varies is
                // how much it was admitted to. A client that renewed holds two, and is two rows
                // with one client key until it gives the older certificate up. Comma separated,
                // that ran along a column narrow enough to need reading twice, and a profile that
                // wrapped mid-name read as two. A line each is the shape of the thing: a list.
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
                    "Takes this certificate's admission away when OK is pressed. It is then "
                    + "refused from its next request onwards, and saving the database keeps that. "
                    + "A client listed twice with one client key, which each row's tooltip names, has "
                    + "renewed, and keeps the other.")
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

        private RenewalRefusal Standing(AdmissionRecord record)
        {
            RenewalRefusal standing;
            return _standing.TryGetValue(record.AdmissionKey, out standing) ? standing : null;
        }

        /// <summary>
        /// Draws one client's name in red while an answer to its renewals stands, and says which
        /// in the tooltip of every text cell and in the row's description.
        /// </summary>
        /// <remarks>
        /// **The name alone is red**, and the rest of the row as it was: the operator chose that
        /// over a whole red row on real screenshots, since the name is what the eye is looking for
        /// and four red cells read as an error. The tooltip is on every text cell, because a
        /// pointer resting anywhere on the row is asking about the row. The X keeps its column's
        /// tooltip, which says what it does.
        ///
        /// **The tooltip always names the client key and the certificate in full**, under the
        /// answer when one stands: nothing else on the row shows either, the client key is what
        /// tells two clients under one name apart, and the audit file names a certificate by its
        /// whole key. The description a screen reader is told stays the answer alone.
        /// </remarks>
        private static void Mark(DataGridViewRow row, AdmissionRecord record, RenewalRefusal standing)
        {
            var answer = AnswerText(standing);

            var described = row as ClientRow;
            if (described != null)
            {
                described.Answer = answer;
            }

            var tip = Tooltips.Wrapped(Identified(record, answer));
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (cell.ColumnIndex == RevokeColumn)
                {
                    continue;
                }

                cell.ToolTipText = tip;
            }

            row.Cells[ClientColumn].Style.ForeColor = answer == null ? Color.Empty : StandingColor;
        }

        /// <summary>
        /// What a client's row says when the pointer rests on it: the standing answer, when one
        /// stands, then the client key and the certificate's own key, each whole on a line.
        /// </summary>
        /// <remarks>
        /// Broken into lines here, which `Tooltips.Wrapped` leaves as they are, because a key is
        /// sixty-four characters with no space to fold at, and one folded onto a line of its own
        /// would leave its label behind.
        /// </remarks>
        internal static string Identified(AdmissionRecord record, string answer)
        {
            var lines = new List<string>();
            if (answer != null)
            {
                lines.Add(answer);
            }

            lines.Add("Client key " + record.ClientKey);
            lines.Add("Certificate " + record.AdmissionKey);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Opens the row's menu: at the pointer, on the row it was pressed on, or under the
        /// selected row's name when the keyboard asked.
        /// </summary>
        /// <remarks>
        /// **The row pressed on becomes the selected one**, as in every list Windows draws, so the
        /// highlight and the menu are about the same client. A press under the last row, or on the
        /// headings, opens nothing: there is no client there to act on.
        ///
        /// **Opened from the keyboard, its first choice that can be made is highlighted**, as the
        /// Refuse menu's is, so that Enter has something to choose. Opened by the pointer, nothing
        /// is, which is how a menu comes up under a click.
        ///
        /// Run from the grid's window procedure, where whatever escapes reaches KeePass's message
        /// loop and closes the dialog: a failure of a kind the user interface throws is said under
        /// the list instead.
        /// </remarks>
        private void ShowMenu(Point? screen)
        {
            if (_filling || _menu.Visible)
            {
                return;
            }

            try
            {
                int row;
                Point at;
                if (screen.HasValue)
                {
                    at = _grid.PointToClient(screen.Value);
                    row = _grid.HitTest(at.X, at.Y).RowIndex;
                    if (row < 0)
                    {
                        return;
                    }

                    _grid.CurrentCell = _grid.Rows[row].Cells[ClientColumn];
                }
                else
                {
                    row = _grid.CurrentCell == null ? -1 : _grid.CurrentCell.RowIndex;
                    if (row < 0)
                    {
                        return;
                    }

                    var cell = _grid.GetCellDisplayRectangle(ClientColumn, row, false);
                    at = cell.IsEmpty ? new Point(0, _grid.ColumnHeadersHeight) : new Point(cell.Left, cell.Bottom);
                }

                var key = KeyAt(row);
                if (key == null)
                {
                    return;
                }

                var answer = AnswerText(StandingAt(row));
                _answerItem.Text = answer ?? string.Empty;
                _answerItem.Visible = answer != null;
                _answerSeparator.Visible = answer != null;
                _allowItem.Enabled = answer != null;

                _menuKey = key;
                _chosen = null;
                _menu.Show(_grid, at);

                if (screen.HasValue)
                {
                    return;
                }

                foreach (ToolStripItem item in _menu.Items)
                {
                    if (item.Enabled && item.Available && item is ToolStripMenuItem)
                    {
                        item.Select();
                        break;
                    }
                }
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                Said("The menu could not be opened: " + error.Message);
            }
        }

        /// <summary>The admission key of the row at this place, readable or not, or null.</summary>
        private string KeyAt(int row)
        {
            if (row >= 0 && row < _records.Count)
            {
                return _records[row].AdmissionKey;
            }

            var unreadable = row - _records.Count;
            return unreadable >= 0 && unreadable < _unreadable.Count ? _unreadable[unreadable] : null;
        }

        /// <summary>The place in the list of the row stored under this key, or -1.</summary>
        private int RowOf(string key)
        {
            var at = _records.FindIndex(record => string.Equals(record.AdmissionKey, key, StringComparison.Ordinal));
            if (at >= 0)
            {
                return at;
            }

            at = _unreadable.IndexOf(key);
            return at < 0 ? -1 : _records.Count + at;
        }

        /// <summary>
        /// Acts on what was chosen, in the message after the menu closed, on the client it was
        /// opened on, wherever that row is now.
        /// </summary>
        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            var chosen = _chosen;
            var key = _menuKey;
            _chosen = null;

            if (chosen != _allowItem && chosen != _revokeItem)
            {
                return;
            }

            Later(() =>
            {
                var row = RowOf(key);
                if (chosen == _allowItem)
                {
                    AllowRenewal(row);
                }
                else
                {
                    Revoke(row);
                }
            });
        }

        /// <summary>
        /// Runs something once the current message is finished with, guarded where it runs.
        /// </summary>
        /// <remarks>
        /// Posted work runs on KeePass's message loop after the code that posted it has returned,
        /// where an escape is WinForms' own error dialog rather than a line under this list. Straight
        /// through when there is no window yet, which is how a test reaches the same path.
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
                    Said("That could not be done: " + error.Message);
                }
            };

            if (!IsHandleCreated || IsDisposed)
            {
                guarded();
                return;
            }

            try
            {
                BeginInvoke(guarded);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                Said("That could not be done: " + error.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Not in `Controls`, so nothing else would: a menu is a window of its own.
                _menu.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// A grid that hands a request for a menu, from the pointer or the keyboard, to the list.
        /// </summary>
        /// <remarks>
        /// `WM_CONTEXTMENU` is what all three send: the right button released over the grid, Shift
        /// and F10, and the menu key, the keyboard's with no position. Taken here rather than by
        /// giving the grid a `ContextMenuStrip`, which opens wherever the pointer is, or in the
        /// middle of the grid for the keyboard, rather than at the row the menu is about, and acts
        /// on no row at all.
        /// </remarks>
        private sealed class ClientGrid : DataGridView
        {
            // cspell:ignore CONTEXTMENU
            private const int ContextMenuMessage = 0x007B;

            public Action<Point?> MenuAsked { get; set; }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == ContextMenuMessage && MenuAsked != null)
                {
                    // Minus one in both halves when the keyboard asked, and a point on the screen,
                    // each half signed, when the pointer did.
                    var position = unchecked((int)m.LParam.ToInt64());
                    if (position == -1)
                    {
                        MenuAsked(null);
                    }
                    else
                    {
                        MenuAsked(new Point((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF)));
                    }

                    return;
                }

                base.WndProc(ref m);
            }
        }

        /// <summary>A row that tells UI automation the standing answer about its client.</summary>
        /// <remarks>
        /// As the row's description rather than its name, which UI automation builds from the
        /// cells and a screen reader already reads: the answer is something more about the row,
        /// in the words its tooltip uses.
        /// </remarks>
        private sealed class ClientRow : DataGridViewRow
        {
            public string Answer { get; set; }

            public override object Clone()
            {
                var clone = (ClientRow)base.Clone();
                clone.Answer = Answer;
                return clone;
            }

            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new Described(this);
            }

            private sealed class Described : DataGridViewRowAccessibleObject
            {
                private readonly ClientRow _row;

                public Described(ClientRow row)
                    : base(row)
                {
                    _row = row;
                }

                public override string Description
                {
                    get { return _row.Answer ?? base.Description; }
                }
            }
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
        /// approval dialog, and the chooser listing each authority's clients, have to call each
        /// one what this page calls it. One method rather than a copy in each, because two copies
        /// of a rule are two places for it to change in.
        /// </remarks>
        internal static string Describe(AdmissionRecord record)
        {
            var subject = record.Subject ?? string.Empty;
            return subject.StartsWith("CN=", StringComparison.Ordinal) ? subject.Substring(3) : subject;
        }

        /// <summary>
        /// The clients among these records, each once by its client key, sorted by name: what a
        /// sentence or a column calling them clients counts and names.
        /// </summary>
        /// <remarks>
        /// **A client is its client key**, the digest of the certificate it paired with, which is
        /// the operator's ruling of 2026-10-16. A client that renewed holds two records until it
        /// gives the older certificate up, both carrying its client key, so it is one client here
        /// and named once; the rows of the Clients page stay one per record, because each is a
        /// certificate that can be taken away on its own.
        ///
        /// **And one name is not always one client**: a client chooses its own name when it pairs,
        /// and nothing holds two programs to different ones, such as one program on two machines.
        /// Those are two clients, and where two of them share a name each is named with the start
        /// of its client key as the Clients page shows it, "agent (3f2a9c1e)", so a sentence naming
        /// them names two clients the operator can tell apart. Counting by name, and the count of
        /// certificates that once followed such a name, are what this replaces.
        /// </remarks>
        internal static IReadOnlyList<string> ClientNames(IEnumerable<AdmissionRecord> records)
        {
            var clients = new Dictionary<string, string>(StringComparer.Ordinal);
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record != null && !clients.ContainsKey(record.ClientKey))
                    {
                        clients[record.ClientKey] = Describe(record);
                    }
                }
            }

            var shared = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in clients.Values)
            {
                if (!seen.Add(name))
                {
                    shared.Add(name);
                }
            }

            var shortened = ShortKeys(clients.Keys);
            var named = new List<KeyValuePair<string, string>>(clients);
            named.Sort((left, right) =>
            {
                var byName = string.CompareOrdinal(left.Value, right.Value);
                return byName != 0 ? byName : string.CompareOrdinal(left.Key, right.Key);
            });

            var names = new List<string>();
            foreach (var client in named)
            {
                names.Add(shared.Contains(client.Value)
                    ? client.Value + " (" + shortened[client.Key] + ")"
                    : client.Value);
            }

            return names;
        }

        /// <summary>
        /// How many characters of a client key are shown where it is shortened, unless two shown
        /// together would then read the same.
        /// </summary>
        internal const int ShortKeyLength = 8;

        /// <summary>
        /// Each of these client keys as it is shown together with the others: its first
        /// <see cref="ShortKeyLength"/> characters, and more only where two would otherwise read
        /// the same.
        /// </summary>
        /// <remarks>
        /// Lengthened only for the keys that collide, and until they no longer do, so the ordinary
        /// page shows eight characters everywhere. Two different keys always part somewhere, since
        /// both are whole digests.
        /// </remarks>
        internal static IDictionary<string, string> ShortKeys(IEnumerable<string> clientKeys)
        {
            var keys = new List<string>();
            if (clientKeys != null)
            {
                foreach (var key in clientKeys)
                {
                    if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
                    {
                        keys.Add(key);
                    }
                }
            }

            var shortened = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var length = Math.Min(ShortKeyLength, key.Length);
                while (length < key.Length && keys.Exists(other =>
                           !string.Equals(other, key, StringComparison.Ordinal)
                           && other.StartsWith(key.Substring(0, length), StringComparison.Ordinal)))
                {
                    length++;
                }

                shortened[key] = key.Substring(0, length);
            }

            return shortened;
        }

        /// <summary>
        /// How long this record still reaches the database, or why it does not reach it at all.
        /// </summary>
        /// <remarks>
        /// What the gate asks before the dates is answered before them, because that is the order
        /// the gate asks in and because an answer about the dates of a certificate it refuses
        /// anyway is an answer about the wrong thing: the anchor, then the usage, then whether the
        /// certificate has begun. Its end is the date itself.
        /// </remarks>
        private static string Reach(
            AdmissionRecord record, IDictionary<string, ClientReach> reaches, DateTimeOffset now)
        {
            ClientReach reach;
            if (reaches != null && reaches.TryGetValue(record.AdmissionKey, out reach))
            {
                switch (reach)
                {
                    case ClientReach.Stranger:
                        return StrangerText;

                    case ClientReach.NotForClients:
                        return NotForClientsText;

                    case ClientReach.NotYetValid:
                        return NotYetValidText;
                }
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
        /// A moment, to the second, in UTC: the one shape `Moments.OnScreen` gives every moment the
        /// plugin shows, because reading two formats is reading twice.
        /// </summary>
        private static string Stamp(DateTimeOffset moment)
        {
            return Moments.OnScreen(moment);
        }

        /// <summary>
        /// Puts one line under the list, cut to what the page can hold, and answers with it.
        /// </summary>
        /// <remarks>
        /// Cut here rather than by whoever composed it, for the reason `Clipped` gives: a refused
        /// revocation carries whatever the store said, which is not this plugin's to choose the
        /// length of, and nothing on the page scrolls.
        /// </remarks>
        private string Said(string what)
        {
            _said = Clipped.Message(what);
            Note();
            return _said;
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
                    : _records.Count + _unreadable.Count == 0 ? NobodyText : string.Empty;

            _note.Text = text;
            _note.Visible = text.Length > 0;

            _reappeared.Text = _cameBack > 0 ? ReappearedText(_cameBack) : string.Empty;
            _reappeared.Visible = _cameBack > 0;
        }
    }

    /// <summary>
    /// How far a record's certificate gets with the gate before the gate looks at the record,
    /// asked in the order the gate asks it.
    /// </summary>
    /// <remarks>
    /// `ClientCredentials` pins the anchor, then asks for client authentication, then the dates,
    /// and only then the record. The pages of the KPG tab ask the same questions in the same
    /// order, through the same methods, so that neither of them lists as admitted a client the
    /// gate refuses on every connection. The last question, whether the certificate has ended, is
    /// left to the date the clients page shows.
    /// </remarks>
    public enum ClientReach
    {
        /// <summary>Nothing before the record refuses it, and its date says for how long.</summary>
        Reaches,

        /// <summary>
        /// This database's authority did not issue it, or the database has no authority that
        /// could have.
        /// </summary>
        Stranger,

        /// <summary>This authority issued it, but not for a client to present.</summary>
        NotForClients,

        /// <summary>Its first valid moment has not come yet.</summary>
        NotYetValid
    }
}
