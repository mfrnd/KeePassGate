using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Every client this database has admitted, each with the certificates it holds, and the one
    /// gesture that revokes a client.
    /// </summary>
    /// <remarks>
    /// **One list, not two.** What a client may reach is shown beside its name, in the same row the
    /// revoke gesture acts on. A separate list of names with the profiles somewhere else would be
    /// two places to look, two places to keep in step, and a revoke that says nothing about what is
    /// being taken away.
    ///
    /// **A client's certificates are a group of rows**, `STORE.md` §648. The rows are grouped by
    /// client key, the certificate admitted last first. The first row of a group is the client's
    /// own row: it carries the client's name and the X. Every further row of the group is indented
    /// under it and reads <see cref="EarlierText"/> where the client's own row reads the name, and
    /// has no X. Profiles, Admitted and Until are each row's own, and a row is still a certificate
    /// for everything else: its tooltip, its red, and its menu's Allow renewal.
    ///
    /// **On the tab rather than in a dialog.** The clients page is a page of its own, so the list
    /// has room to grow without pushing anything else off the tab, and the operator sees who is
    /// admitted without pressing anything. The tab itself still does not scroll: what scrolls is
    /// the list, inside the fixed area it is given, which is what a list is for.
    ///
    /// **Revoking a client is a mark on its own row**, in the last column, rather than a button
    /// somewhere else that acts on whatever is selected. A button off to one side has to be read
    /// together with the selection to know what it would do, and the row it would act on is the one
    /// thing it does not tell. The X revokes the client whole, every certificate it holds,
    /// `STORE.md` §615, and every row of the group goes at once.
    ///
    /// **Where the store takes no write in the session, no row offers a command on it**: no row
    /// carries the X, and a row's menu offers Allow renewal alone, `STORE.md` §636.
    ///
    /// **It waits for the dialog's OK, like everything else here.** What settled it is that a tab
    /// cannot carry two models: whichever one an operator assumes on the page they are not looking
    /// at is the one that costs them something.
    ///
    /// A grid rather than a list view, because one column is a thing to press. Nothing else here is
    /// editable: every text column is read only and the grid adds no row of its own.
    ///
    /// **A standing answer to a certificate's renewals is shown on its row and lifted from it.**
    /// There is no column for it: the row's name is drawn in the tab's own red while an answer of
    /// any of the three kinds stands, the row's text cells tell which in their tooltip, and the row
    /// tells a screen reader the same words as its description. The row's menu, opened by the right
    /// button, Shift and F10 or the menu key, names the certificate it acts on on a grayed first
    /// line, states the answer on a grayed line under it, and offers **Allow renewal**, and on a
    /// client's own row **Revoke** as well. Allow renewal waits for the dialog's OK as a revocation
    /// does, and the name stops being red at once as a revoked group goes at once.
    ///
    /// **Each row's client key is in its tooltip**, whole, with the certificate's own key under
    /// it: a client chooses its own name, so two groups under one name are two programs that chose
    /// it, and the key is what tells which. There is no room for a column of keys: measured, a
    /// column of eight characters in the code font left the names `GateTabFitsTests` holds the page
    /// to cut at 100, 125 and 150 percent, and the page does not scroll, so the tooltip is where it
    /// went.
    /// </remarks>
    public sealed class ClientList : UserControl
    {
        /// <summary>What the control is called, for the tests that drive it.</summary>
        public const string ControlName = "KpgClientList";

        /// <summary>The column that revokes a client, by name.</summary>
        public const string RevokeColumnName = "KpgRevokeColumn";

        /// <summary>The grid, by name.</summary>
        public const string ListName = "KpgClientListView";

        /// <summary>
        /// What a screen reader calls the grid, which has no caption of its own to be called by.
        /// </summary>
        /// <remarks>
        /// A phrase to be read aloud rather than a lookup name, which is the whole distinction
        /// `CONVENTIONS.md` draws: the desktop tests point at controls by their `AutomationId`,
        /// which comes from `Name` and is untouched by this.
        /// </remarks>
        public const string ListLabel = "Clients admitted to this database";

        /// <summary>What is said when the database has admitted nobody.</summary>
        public const string NobodyText = "No client has been admitted to this database.";

        /// <summary>
        /// The line under a list that revocations on this page emptied, while the database still
        /// holds every client they took off it.
        /// </summary>
        /// <remarks>
        /// <see cref="NobodyText"/> there read as though no client had ever been admitted, when
        /// each revoked client still reaches the database until the dialog is accepted.
        /// </remarks>
        public const string EmptiedText = "No clients once OK is pressed.";

        /// <summary>What is said when the admitted clients could not be listed at all.</summary>
        public const string UnreadableText =
            "The admitted clients could not be read. This list is not what the database holds.";

        /// <summary>Said beside a certificate that is past its dates.</summary>
        public const string ExpiredText = "expired";

        /// <summary>
        /// Shown beside a certificate this database's authority did not issue, and beside an
        /// admission that names another authority as its issuer.
        /// </summary>
        /// <remarks>
        /// In place of the date rather than beside it, because the date is the one thing about
        /// such a row that is not true: the gate pins the anchor before it looks at anything else,
        /// so a certificate that chains somewhere else is refused whatever its own dates say. A
        /// row reading `2034-01-01` there is a row promising access nobody has.
        ///
        /// The row stays: it is still an admission, and revoking its client is still the
        /// operator's to do.
        /// </remarks>
        public const string StrangerText = "not this authority";

        /// <summary>
        /// Said beside a certificate this authority issued for something other than a client.
        /// </summary>
        /// <remarks>
        /// In place of the date, for the reason <see cref="StrangerText"/> is: the gate asks for
        /// client authentication before it looks at the dates, and refuses a certificate issued
        /// for the host whatever they say. Only a file written by hand or merged from another
        /// copy holds such an admission, since pairing admits only the client certificates it
        /// issues.
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
        /// Shown where the date would be beside a certificate that several admissions in force
        /// name, which none of them admits, `STORE.md` §615 (S24).
        /// </summary>
        public const string ListedTwiceText = "listed twice";

        /// <summary>
        /// Shown where the date would be beside a record of kind `client` that does not read as an
        /// admission, which admits nobody, `STORE.md` §615.
        /// </summary>
        /// <remarks>
        /// Such a record has little else to show: no subject, no profiles, no admission time. It is
        /// listed all the same, by its entry and under its client where its client key reads,
        /// because revoking that client is the operator's to do.
        /// </remarks>
        public const string NotCountedText = "not counted";

        /// <summary>
        /// What a further row of a group reads where the client's own row reads its name.
        /// </summary>
        public const string EarlierText = "earlier certificate";

        /// <summary>What the last column says, and what pressing it means.</summary>
        public const string RevokeMark = "X";

        /// <summary>
        /// The last column's heading, which a screen reader names every X in the column by, and
        /// which is never drawn.
        /// </summary>
        /// <remarks>
        /// A table names a cell to UI automation by its column's heading and its row, and an X is
        /// no name, so with a heading left empty for the bin painted over it each button was read
        /// out as a row number with an X in it, and nothing about what pressing it does.
        /// </remarks>
        public const string RevokeHeading = "Revoke";

        /// <summary>What an X does, as its own cell and the column's heading show it.</summary>
        private static readonly string RevokeTip =
            Tooltips.Wrapped("Revokes this client, with every certificate it holds, when OK is pressed.");

        /// <summary>
        /// Said after a profile a client holds that the database no longer names.
        /// </summary>
        /// <remarks>
        /// An admission keeps every name it was approved for, and the gate grants a client only
        /// the ones the database still names, so such a name is held and grants nothing. It is
        /// listed, dimmed, rather than left out, because adding the name back gives it to this
        /// client again, and the row is where the operator looks to see who holds what.
        /// </remarks>
        public const string RemovedSuffix = " (removed)";

        /// <summary>The row's menu, by name.</summary>
        public const string MenuName = "KpgClientMenu";

        /// <summary>
        /// The menu's first line, naming the certificate the menu acts on, by name.
        /// </summary>
        public const string CertificateItemName = "KpgClientCertificateItem";

        /// <summary>The menu's line stating the answer that stands, by name.</summary>
        public const string AnswerItemName = "KpgClientAnswerItem";

        /// <summary>The menu's Allow renewal, by name.</summary>
        public const string AllowRenewalItemName = "KpgAllowRenewalItem";

        /// <summary>The menu's Revoke, by name.</summary>
        public const string RevokeItemName = "KpgRevokeItem";

        /// <summary>What lifts a standing answer, with its access key.</summary>
        public const string AllowRenewalText = "&Allow renewal";

        /// <summary>
        /// What revokes the client from the menu, as the X does, with its access key.
        /// </summary>
        public const string RevokeText = "&Revoke";

        /// <summary>
        /// What a screen reader calls the row's menu, which has no caption of its own.
        /// </summary>
        public const string MenuLabel = "Client actions";

        /// <summary>
        /// The red a row's name is drawn in while an answer to its certificate's renewals stands:
        /// the tab's own, the notice over the invite button's, 6.68 to 1 on the window's white.
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
        private readonly ToolStripMenuItem _certificateItem;
        private readonly ToolStripMenuItem _answerItem;
        private readonly ToolStripSeparator _commandsSeparator;
        private readonly ToolStripMenuItem _revokeItem;
        private readonly ToolStripMenuItem _allowItem;

        /// <summary>
        /// The row the menu was opened on, by <see cref="ClientListRow.Id"/>, so that a choice acts
        /// on that row whatever a redraw has done to the rows meanwhile.
        /// </summary>
        private string _menuRow;

        /// <summary>What was chosen while the menu was open, acted on once it has closed.</summary>
        private ToolStripItem _chosen;

        /// <summary>
        /// Each listed certificate's standing answer, by admission key; absent is none.
        /// </summary>
        private readonly Dictionary<string, RenewalRefusal> _standing =
            new Dictionary<string, RenewalRefusal>(StringComparer.Ordinal);

        /// <summary>The profiles the database names, or null when nobody said.</summary>
        private ICollection<string> _named;

        /// <summary>What the last gesture said, kept because the label also says other things.</summary>
        private string _said = string.Empty;

        /// <summary>The rows, in the order the grid shows them.</summary>
        private readonly List<ClientListRow> _rows = new List<ClientListRow>();

        private bool _filling;

        // Whether the last thing filled in was the whole truth. Starts true because an empty
        // list that nobody has filled yet is empty, not unreadable.
        private bool _readable = true;

        // Whether a revocation on this list went through, which lands only on OK. Kept across a
        // refill, since what the host lists is the page's own copy, with the revoked gone.
        private bool _revoked;

        private readonly Label _reappeared;

        /// <summary>
        /// How many refused clients a merge brought back, as the last listing counted.
        /// </summary>
        private int _cameBack;

        /// <summary>
        /// The list, with the host behind Allow renewal as well as each client's X.
        /// </summary>
        /// <param name="revoke">
        /// Revokes a client, by its client key, and answers with why it could not, or null when it
        /// did. The list does not write to a database itself: it is a list.
        /// </param>
        /// <param name="allowRenewal">
        /// Lifts the standing answer to renewing one certificate, by its admission key, and answers
        /// with why it could not, or null when it did. Like a revocation it waits for the dialog's
        /// OK, and like one it is the host's to do: the list reads and writes no database.
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

            // **The commands sit on the row's own menu**, so what each acts on is the row it was
            // opened on rather than whatever a button off to one side finds selected, which is the
            // argument the X in each client's row already won. A menu holding nothing but its
            // items owns Enter and Escape while it is open, measured for the renewal dialog's
            // Refuse menu, so the settings dialog's OK and Cancel never see them and nothing is
            // taken from the dialog here.
            _menu = new ContextMenuStrip
            {
                Name = MenuName,
                AccessibleName = MenuLabel,
                ShowImageMargin = false,
                ShowItemToolTips = true
            };

            // **The certificate the menu acts on as the first line, grayed out**, because a client
            // holds several certificates and a menu that does not tell which certificate its
            // commands are about leaves the operator reading the row again. Then the answer that
            // stands, grayed as well, since both are statements rather than commands.
            _certificateItem = new ToolStripMenuItem { Name = CertificateItemName, Enabled = false };
            _answerItem = new ToolStripMenuItem { Name = AnswerItemName, Enabled = false };
            _commandsSeparator = new ToolStripSeparator();

            _revokeItem = new ToolStripMenuItem(RevokeText)
            {
                Name = RevokeItemName,
                ToolTipText = Tooltips.Wrapped(
                    "Revokes this client, with every certificate it holds, when OK is pressed, as the X does.")
            };

            _allowItem = new ToolStripMenuItem(AllowRenewalText)
            {
                Name = AllowRenewalItemName,
                ToolTipText = Tooltips.Wrapped(
                    "Lifts the standing answer to renewing this certificate when OK is pressed. Its "
                    + "next renewal then raises the dialog again.")
            };

            _menu.Items.AddRange(new ToolStripItem[]
            {
                _certificateItem, _answerItem, _commandsSeparator, _revokeItem, _allowItem
            });

            // **A choice is acted on a message after the menu has closed**, as on the Refuse
            // menu: `ItemClicked` is raised while the menu is still on screen, and what both
            // choices do redraws the grid underneath it, which is what `GrantPanel.Finish` records
            // a dropdown not surviving. The menu is never disposed there either; it goes with this
            // list. And nothing closes it by code, which left the next window's menu closing as it
            // opened.
            _menu.ItemClicked += (sender, e) =>
                WindowHandlers.Run("a client's menu could not note the choice", () => _chosen = e.ClickedItem);
            _menu.Closed += (sender, e) =>
                WindowHandlers.Run("a client's menu could not be closed", () => OnMenuClosed(sender, e));
            ((ClientGrid)_grid).MenuAsked = ShowMenu;

            // **One line under the grid rather than two.** What the last gesture did wins where
            // it and the line stating nobody is admitted would collide, because it is the newer
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

            // **A line of its own for refused clients a merge brought back**, rather than a turn on
            // the one above. That line is the newest answer and gives way to the next; this one is
            // a standing fact about the file, true until the next write this plugin makes deletes
            // those admissions, and a gesture on this page must not hide it meanwhile. It is there
            // only after a merge brought one back, so the page is otherwise as it was.
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
        /// already has one. Docking reads that order and nothing else here does, so it is put
        /// back where it is about to be read, whichever window was made first.
        /// </remarks>
        protected override void OnLayout(LayoutEventArgs e)
        {
            WindowHandlers.Run("the clients page could not be laid out", () =>
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
            });
        }

        /// <summary>What the last gesture said, by name.</summary>
        public const string ResultLabelName = "KpgClientResultLabel";

        /// <summary>The line counting refused clients a merge brought back, by name.</summary>
        public const string ReappearedLabelName = "KpgClientReappearedLabel";

        /// <summary>
        /// What is shown under the list while admissions that a decision refuses are back in the
        /// database.
        /// </summary>
        /// <remarks>
        /// Only a merge does that: a copy of the file from before a revocation, a retirement or a
        /// certificate taken away brought the admission back. The decision refuses it, and it is
        /// not listed as a client. It is counted here rather than passed over, because it tells the
        /// operator a stale copy was merged in; the next write this plugin makes deletes the
        /// admission, `STORE.md` §627, and the line with it.
        /// </remarks>
        public static string ReappearedText(int count)
        {
            return count == 1
                ? "1 removed client came back in a merge; still refused."
                : count.ToString(CultureInfo.InvariantCulture)
                  + " removed clients came back in a merge; still refused.";
        }

        /// <summary>
        /// What the line under the list states about refused clients a merge brought back, or null
        /// when it says nothing.
        /// </summary>
        public string Reappeared
        {
            get { return _cameBack > 0 ? _reappeared.Text : null; }
        }

        /// <summary>
        /// Whether the database's store takes a write of the operator's in this session, read by
        /// the next fill: where it does not, no row offers a command on the store, no X and no
        /// command of the row's menu that writes into it, `STORE.md` §636. Allow renewal stays,
        /// since the standing answers are kept apart from the store.
        /// </summary>
        public bool Writable { get; set; } = true;

        /// <summary>
        /// The rows the list is showing, in order: what the tests read the page by. The tab reads
        /// none of them, and fills the list through `Fill`.
        /// </summary>
        public IReadOnlyList<ClientListRow> Rows
        {
            get { return _rows; }
        }

        /// <summary>The admissions that read, in the order their rows are shown.</summary>
        public IReadOnlyList<AdmissionRecord> Clients
        {
            get { return _rows.Where(row => row.Record != null).Select(row => row.Record).ToList(); }
        }

        /// <summary>The records that do not read, in the order their rows are shown.</summary>
        public IReadOnlyList<UnreadAdmission> Unreadable
        {
            get { return _rows.Where(row => row.Unread != null).Select(row => row.Unread).ToList(); }
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
        /// The standing answer to renewing the certificate of a row, by its place in the list, or
        /// null when none stands or there is no such row.
        /// </summary>
        public RenewalRefusal StandingAt(int index)
        {
            return index < 0 || index >= _rows.Count || _rows[index].Record == null ? null : Standing(_rows[index].Record);
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
        /// What the menu's first line states about the certificate of a row: the start of its
        /// admission key, and when it was admitted where the admission reads.
        /// </summary>
        public static string CertificateText(ClientListRow row)
        {
            if (row == null)
            {
                return string.Empty;
            }

            if (row.Record != null)
            {
                return "Certificate " + Start(row.Record.AdmissionKey) + ", admitted "
                       + row.Record.Admitted.ToUniversalTime().ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
            }

            return row.Unread.Certificate != null
                ? "Certificate " + Start(row.Unread.Certificate)
                : "Entry " + Start(row.Unread.Entry);
        }

        /// <summary>
        /// The first <see cref="ShortKeyLength"/> characters of a key, or the key where it is
        /// shorter.
        /// </summary>
        private static string Start(string key)
        {
            return string.IsNullOrEmpty(key) || key.Length <= ShortKeyLength ? key ?? string.Empty : key.Substring(0, ShortKeyLength);
        }

        /// <summary>
        /// Lift the standing answer to renewing the certificate of a row, by its place in the list.
        /// </summary>
        /// <remarks>
        /// Whichever of the three it is. Like a revocation it goes into what the dialog holds and
        /// reaches the database and the gate when the dialog is accepted, and like a revocation the
        /// row shows it at once: the name stops being red. Answers with what the line under the
        /// list now states, which is null when the lift went through, see <see cref="Done"/>.
        ///
        /// Public rather than only wired to the menu, for the reason <see cref="Revoke"/> is.
        /// </remarks>
        public string AllowRenewal(int index)
        {
            if (index < 0 || index >= _rows.Count || _rows[index].Record == null)
            {
                return Said("There is no such certificate.");
            }

            var row = _rows[index];
            var record = row.Record;
            var name = Named(row);
            if (Standing(record) == null)
            {
                return Said(name + " has no standing answer: its next renewal raises the dialog.");
            }

            var refused = _allowRenewal(record.AdmissionKey);
            if (refused != null)
            {
                return Said("The answer to renewing that certificate still stands: " + refused);
            }

            // Found again rather than trusting the index, for the reason a revoked group is: a host
            // that redraws the list in answer to the change has already drawn it without the red.
            _standing.Remove(record.AdmissionKey);
            var at = _rows.IndexOf(row);
            if (at >= 0 && at < _grid.Rows.Count)
            {
                Mark(_grid.Rows[at], row, null);
            }

            return Done();
        }

        /// <summary>
        /// Revoke the client of a row, by its place in the list: the client's own row, or a row
        /// that stands alone and names a client key.
        /// </summary>
        /// <remarks>
        /// Public rather than only wired to the column, because a cell cannot be pressed on a
        /// control that was never shown, silently, and a test that pressed one would pass while
        /// proving nothing. Answers with what the line under the list now states, which is null
        /// when the revocation went through, see <see cref="Done"/>. Every row of the client's
        /// group goes at once.
        /// </remarks>
        public string Revoke(int index)
        {
            if (index < 0 || index >= _rows.Count)
            {
                return Said("There is no such client.");
            }

            var row = _rows[index];
            if (!row.Own || row.ClientKey == null)
            {
                return Said(row.ClientKey == null
                    ? "That record names no client to revoke."
                    : "A client is revoked on its first row.");
            }

            var refused = _revoke(row.ClientKey);
            if (refused != null)
            {
                return Said("That client is still admitted: " + refused);
            }

            _revoked = true;

            // Found again rather than trusting the index, because what the callback does is the
            // host's business: a host that refills the list in answer to the change is a reasonable
            // host, and one that removed rows by a stale index would be removing somebody else's.
            for (var at = _rows.Count - 1; at >= 0; at--)
            {
                if (!string.Equals(_rows[at].ClientKey, row.ClientKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_rows[at].Record != null)
                {
                    _standing.Remove(_rows[at].Record.AdmissionKey);
                }

                _rows.RemoveAt(at);
                if (at < _grid.Rows.Count)
                {
                    _grid.Rows.RemoveAt(at);
                }
            }

            UpdateEmptiness();

            return Done();
        }

        /// <summary>
        /// What a sentence under the list calls the client of a row: its name, and the start of
        /// its client key as well when another client listed here goes by the same name.
        /// </summary>
        private string Named(ClientListRow row)
        {
            if (row.Record == null)
            {
                return CertificateText(row);
            }

            var record = row.Record;
            var name = Describe(record);
            var listed = Clients;
            var shared = listed.Any(other =>
                string.Equals(Describe(other), name, StringComparison.Ordinal)
                && !string.Equals(other.ClientKey, record.ClientKey, StringComparison.Ordinal));

            return shared
                ? name + " (" + ShortKeys(listed.Select(other => other.ClientKey))[record.ClientKey] + ")"
                : name;
        }

        /// <summary>
        /// Show a listing, replacing whatever is there. Named so as not to overload `Control.Show`,
        /// which does something else entirely and would be reached by accident.
        /// </summary>
        /// <param name="reaches">
        /// How far each admission's certificate gets with the gate before its admission is looked
        /// at, by admission key, or null when nothing is known about that. Decided by the caller
        /// rather than here, because answering it means reading the authority out of the database
        /// and this control reads nothing: it is a list.
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

        /// <summary>
        /// The same, naming in red each certificate whose renewals stand refused.
        /// </summary>
        /// <param name="listing">As above.</param>
        /// <param name="reaches">As above.</param>
        /// <param name="named">As above.</param>
        /// <param name="standing">
        /// The standing answer to renewing each certificate, by admission key, the database's and
        /// this session's asked in the order the gate asks them. A certificate with none is absent,
        /// and so is a certificate whose day has come. Null when nobody answered, which shows none.
        /// Decided by the caller, for the reason the reaches are: answering it means reading the
        /// database.
        /// </param>
        public void Fill(
            AdmissionListing listing,
            IDictionary<string, ClientReach> reaches,
            ICollection<string> named,
            IDictionary<string, RenewalRefusal> standing)
        {
            _filling = true;

            // A listing read from the database is the newer answer than whatever the last press
            // stated about it: a redraw a landing caused could otherwise leave a client listed
            // under a line naming it as revoked. A press writes its line after the redraw it
            // causes, so it keeps it.
            _said = string.Empty;

            try
            {
                _named = named;
                _rows.Clear();
                _standing.Clear();
                _grid.Rows.Clear();

                // Taken as a listing rather than as admissions, because the difference between a
                // database that admitted nobody and one that would not say is invisible in the
                // rows and is the whole of what this page is for.
                _readable = listing == null || listing.Complete;

                _cameBack = listing == null ? 0 : listing.Waiting;

                if (listing != null)
                {
                    foreach (var row in Grouped(listing, Writable))
                    {
                        if (row.Record != null)
                        {
                            RenewalRefusal answer;
                            if (standing != null && standing.TryGetValue(row.Record.AdmissionKey, out answer) && answer != null)
                            {
                                _standing[row.Record.AdmissionKey] = answer;
                            }
                        }

                        _rows.Add(row);
                        var at = _grid.Rows.Add(Cells(row, reaches, DateTimeOffset.UtcNow));
                        Shape(_grid.Rows[at], row);
                        Mark(_grid.Rows[at], row, row.Record == null ? null : Standing(row.Record));
                    }
                }
            }
            finally
            {
                _filling = false;
            }

            HoldIndented();
            UpdateEmptiness();
        }

        /// <summary>
        /// Holds the Client column to the width the text of every indented row needs, which is
        /// fixed: <see cref="EarlierText"/>, or a certificate's own text under its client.
        /// </summary>
        /// <remarks>
        /// A name on a client's own row is as long as somebody made it, and its tooltip holds it
        /// whole; a fixed text is the same on every row and has no other place to be read. Asked
        /// again when the font changes, since a page can be filled before its dialog scales it.
        /// </remarks>
        private void HoldIndented()
        {
            if (_grid == null)
            {
                // Raised while the constructor builds the grid, before there is a row to hold.
                return;
            }

            var needed = MinimumColumnWidth;
            for (var at = 0; at < _rows.Count && at < _grid.Rows.Count; at++)
            {
                if (!_rows[at].Own)
                {
                    needed = Math.Max(needed, _grid.Rows[at].Cells[ClientColumn].PreferredSize.Width);
                }
            }

            var column = _grid.Columns[ClientColumn];
            if (column.MinimumWidth != needed)
            {
                column.MinimumWidth = needed;
            }
        }

        /// <summary>
        /// The narrowest a column of the grid is, as WinForms makes a new column.
        /// </summary>
        private const int MinimumColumnWidth = 5;

        /// <summary>
        /// The rows of a listing in the order the page shows them: a group for each client key,
        /// the group whose newest certificate was admitted last first, each group's certificate
        /// admitted last as its own row; the records that do not read under the client their
        /// client key names, after its certificates that read; after every group, the records of
        /// a client no group holds kept together under the first of them; and a record naming no
        /// client key on a row of its own.
        /// </summary>
        /// <param name="writable">
        /// Whether the database's store takes a write of the operator's in this session, which
        /// every command on the store a row offers needs.
        /// </param>
        public static IReadOnlyList<ClientListRow> Grouped(AdmissionListing listing, bool writable)
        {
            var rows = new List<ClientListRow>();
            if (listing == null)
            {
                return rows;
            }

            var groups = listing.Records
                .GroupBy(record => record.ClientKey, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(record => record.Admitted).ToList())
                .OrderByDescending(group => group[0].Admitted)
                .ToList();
            var held = new HashSet<string>(groups.Select(group => group[0].ClientKey), StringComparer.Ordinal);

            foreach (var group in groups)
            {
                for (var at = 0; at < group.Count; at++)
                {
                    rows.Add(new ClientListRow(group[at], null, group[0].ClientKey, at == 0, listing.StandingOf(group[at]), writable));
                }

                foreach (var unread in listing.Unreadable.Where(
                    unread => string.Equals(unread.ClientKey, group[0].ClientKey, StringComparison.Ordinal)))
                {
                    rows.Add(new ClientListRow(null, unread, unread.ClientKey, false, AdmissionStanding.Admitted, writable));
                }
            }

            // A client whose only records do not read: kept together, in the order the listing
            // gives them, under the first of them, which is the client's own row. A record whose
            // client key does not read stands alone. So no indented row is drawn under another
            // client's row.
            var alone = new List<List<UnreadAdmission>>();
            var together = new Dictionary<string, List<UnreadAdmission>>(StringComparer.Ordinal);
            foreach (var unread in listing.Unreadable.Where(unread => unread.ClientKey == null || !held.Contains(unread.ClientKey)))
            {
                List<UnreadAdmission> block;
                if (unread.ClientKey == null || !together.TryGetValue(unread.ClientKey, out block))
                {
                    block = new List<UnreadAdmission>();
                    alone.Add(block);
                    if (unread.ClientKey != null)
                    {
                        together[unread.ClientKey] = block;
                    }
                }

                block.Add(unread);
            }

            foreach (var block in alone)
            {
                for (var at = 0; at < block.Count; at++)
                {
                    rows.Add(new ClientListRow(null, block[at], block[at].ClientKey, at == 0, AdmissionStanding.Admitted, writable));
                }
            }

            return rows;
        }

        /// <summary>The cells of a row, its X among them wherever the row has an X.</summary>
        private object[] Cells(ClientListRow row, IDictionary<string, ClientReach> reaches, DateTimeOffset now)
        {
            if (row.Record == null)
            {
                return new object[]
                {
                    CertificateText(row), string.Empty, string.Empty, NotCountedText, RevokeMark
                };
            }

            var record = row.Record;
            return new object[]
            {
                row.Own ? Describe(record) : EarlierText,
                record.Profiles.Count == 0 ? "nothing" : string.Join(Environment.NewLine, Profiles(record)),
                Stamp(record.Admitted),
                NotAdmitted(row.Standing) ?? Reach(record, reaches, now),
                RevokeMark
            };
        }

        /// <summary>
        /// What a row reads where its date would be when the listing judges that its admission
        /// admits nobody, `STORE.md` §615, or null where the listing has it admit.
        /// </summary>
        /// <remarks>
        /// From the listing's own judgment and from nowhere else, so that the page and the gate
        /// cannot come to disagree about an admission: a certificate that chains to the authority
        /// served is still refused where its admission names another issuer.
        /// </remarks>
        private static string NotAdmitted(AdmissionStanding standing)
        {
            switch (standing)
            {
                case AdmissionStanding.Twice:
                    return ListedTwiceText;
                case AdmissionStanding.NotServed:
                    return StrangerText;
                case AdmissionStanding.Uncounted:
                    return NotCountedText;
                default:
                    return null;
            }
        }

        /// <summary>
        /// A row's shape: indented under its client where it is not the client's own row, and an X
        /// only where pressing it revokes a client.
        /// </summary>
        private static void Shape(DataGridViewRow row, ClientListRow listed)
        {
            if (!listed.Own)
            {
                row.Cells[ClientColumn].Style.Padding = new Padding(Indent, 0, 0, 0);
            }

            if (!listed.Revocable)
            {
                row.Cells[RevokeColumn] = new DataGridViewTextBoxCell { Value = string.Empty };
            }
        }

        /// <summary>
        /// How far a further row of a group is indented under the client's own row.
        /// </summary>
        private const int Indent = Spacing.Gap * 2;

        private DataGridView BuildGrid()
        {
            var grid = new ClientGrid
            {
                Name = ListName,

                // Rows that tell a screen reader the standing answer about their certificate.
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

                // **One row per certificate, however many profiles it holds.** A certificate is
                // one thing that was admitted once, so it is one row; what varies is how much it
                // was admitted to. Comma separated, that ran along a column narrow enough to need
                // reading twice, and a profile that wrapped mid-name read as two. A line each is
                // the shape of the thing: a list.
                //
                // The row grows a line for each rather than the list being cut. A name wider than
                // the column keeps its one line and ends in an ellipsis, read whole in the row's
                // tooltip, as `PaintProfiles` sets out.
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
            grid.Columns.Add(Fitted(AdmittedHeader));
            grid.Columns.Add(Fitted(UntilHeader));

            var revoke = new DataGridViewButtonColumn
            {
                Name = RevokeColumnName,

                // The name of every X under it, and nothing else: the heading is a bin painted
                // over the words, and the column is as wide as the Xs, so the words cost no room.
                HeaderText = RevokeHeading,
                Text = RevokeMark,
                UseColumnTextForButtonValue = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCellsExceptHeader,
                MinimumWidth = GateIcon.Room,
                ToolTipText = RevokeTip
            };

            // One line, so that words nobody sees do not make the heading row taller.
            revoke.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            grid.Columns.Add(revoke);

            grid.Columns[ProfilesColumn].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.CellClick += (sender, e) =>
                WindowHandlers.Run("a press on the clients list could not be carried out",
                    () => OnCellClick(sender, e));
            grid.CellPainting += (sender, e) =>
                WindowHandlers.Run("the clients list could not be drawn", () => OnCellPainting(sender, e));
            grid.FontChanged += (sender, e) =>
                WindowHandlers.Run("the clients list could not be fitted to its font", HoldIndented);

            return grid;
        }

        /// <summary>
        /// Draws a bin over the heading of the column that revokes a client, and dims the removed
        /// profiles a client holds.
        /// </summary>
        /// <remarks>
        /// The heading only. The buttons under it keep their mark: what a button does is named by
        /// the column it is in, to the eye by the bin and to a screen reader by the heading's
        /// words, and a picture in every row is a picture repeated once per row. The words are
        /// never drawn, bin or no bin, since the column is as wide as an X and not as the words.
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
        }

        /// <summary>A column of text that shares the width the fitted columns leave.</summary>
        /// <remarks>
        /// **No column sorts at a press on its heading**, which a text column does unless told
        /// otherwise. A row is acted on by its place in the list as it was filled, so a grid that
        /// put its rows in another order left every X, every menu and every cell of profiles on
        /// another client's row: the X beside a client revoked another client.
        /// </remarks>
        private static DataGridViewTextBoxColumn Filling(string header, int weight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };
        }

        /// <summary>The heading of the column holding when a certificate was admitted.</summary>
        /// <remarks>
        /// Each of the two moment columns names its zone, because every moment on screen is printed
        /// in UTC and a bare moment reads as local time, wrong by the operator's offset.
        /// </remarks>
        public const string AdmittedHeader = "Admitted (UTC)";

        /// <summary>The heading of the column holding when a certificate stops working.</summary>
        public const string UntilHeader = "Until (UTC)";

        private static DataGridViewTextBoxColumn Fitted(string header)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };

            // One line, as the revoke column's heading is. Sized to what it holds, a column with no
            // rows under it holds only its heading, and a heading free to wrap was given the width
            // that folds it: "Admitted (UTC)" and "Until (UTC)" made the heading row two lines tall
            // on a page that had admitted nobody yet.
            column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            return column;
        }

        private void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0 || e.ColumnIndex != RevokeColumn
                || e.RowIndex >= _rows.Count || !_rows[e.RowIndex].Revocable)
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
        /// Draws a row's name in red while an answer to its certificate's renewals stands, and
        /// tells which in the tooltip of every text cell and in the row's description.
        /// </summary>
        /// <remarks>
        /// **The name alone is red**, and the rest of the row as it was: the name is what the eye
        /// is looking for and four red cells read as an error. The tooltip is on every text cell,
        /// because a pointer resting anywhere on the row is asking about the row. The X shows what
        /// it does, as its own cell's tooltip: a column's tooltip is its heading's, and shows over
        /// no cell. A row without an X shows nothing there.
        ///
        /// **The tooltip always names the client key and the certificate in full**, under the
        /// answer when one stands: nothing else on the row shows either, the client key is what
        /// tells two clients under one name apart, and the audit file names a certificate by its
        /// whole key. The description a screen reader is told stays the answer alone.
        /// </remarks>
        private void Mark(DataGridViewRow row, ClientListRow listed, RenewalRefusal standing)
        {
            var answer = AnswerText(standing);

            var described = row as ClientRow;
            if (described != null)
            {
                described.Answer = answer;
            }

            var tip = Tooltips.Wrapped(listed.Record == null
                ? Unidentified(listed.Unread)
                : Identified(listed.Record, answer, Profiles(listed.Record), listed.Standing));
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (cell.ColumnIndex == RevokeColumn)
                {
                    cell.ToolTipText = listed.Revocable ? RevokeTip : string.Empty;
                    continue;
                }

                cell.ToolTipText = tip;
            }

            row.Cells[ClientColumn].Style.ForeColor = answer == null ? Color.Empty : StandingColor;
        }

        /// <summary>
        /// What a row shows when the pointer rests on it: the client's whole name, why it admits
        /// nobody where it does not, the standing answer, when one stands, each profile it holds,
        /// as its cell shows it, then the client key and the certificate's own key, each whole on a
        /// line.
        /// </summary>
        /// <remarks>
        /// Broken into lines here, which `Tooltips.Wrapped` leaves as they are, because a key is
        /// sixty-four characters with no space to fold at, and one folded onto a line of its own
        /// would leave its label behind.
        ///
        /// **The name first**, because the column cuts a long name and a cell with a tip of its own
        /// never shows its cut value whole: two programs named alike but for their last characters
        /// read as the same row, and only a key told them apart.
        /// </remarks>
        internal static string Identified(
            AdmissionRecord record, string answer, IEnumerable<string> profiles, AdmissionStanding standing)
        {
            var lines = new List<string> { Describe(record) };
            if (standing == AdmissionStanding.Twice)
            {
                lines.Add(ListedTwiceReason);
            }
            else if (standing == AdmissionStanding.Uncounted)
            {
                lines.Add(UncountedReason);
            }

            if (answer != null)
            {
                lines.Add(answer);
            }

            lines.AddRange(profiles.Select(profile => "Profile " + profile));
            lines.Add("Client key " + record.ClientKey);
            lines.Add("Certificate " + record.AdmissionKey);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>Why a certificate listed twice admits nobody, in its row's tooltip.</summary>
        public const string ListedTwiceReason =
            "More than one admission names this certificate, so none of them admits it.";

        /// <summary>Why a record that does not read admits nobody, in its row's tooltip.</summary>
        public const string UnreadReason =
            "This record does not read as an admission, so it admits nobody.";

        /// <summary>
        /// Why a renewal that does not count admits nobody, in its row's tooltip: what fails may be
        /// the row's own origin or a certificate further back along its chain.
        /// </summary>
        public const string UncountedReason =
            "Renewed, directly or further back, from a certificate that was taken away without naming "
            + "the renewal made from it, or that no single admission admits, so it admits nobody.";

        /// <summary>
        /// What a row of a record that does not read shows when the pointer rests on it.
        /// </summary>
        internal static string Unidentified(UnreadAdmission unread)
        {
            var lines = new List<string> { UnreadReason };
            if (unread.ClientKey != null)
            {
                lines.Add("Client key " + unread.ClientKey);
            }

            if (unread.Certificate != null)
            {
                lines.Add("Certificate " + unread.Certificate);
            }

            lines.Add("Entry " + unread.Entry);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Opens the row's menu: at the pointer, on the row it was pressed on, or under the
        /// selected row's name when the keyboard asked.
        /// </summary>
        /// <remarks>
        /// **The row pressed on becomes the selected one**, as in every list Windows draws, so the
        /// highlight and the menu are about the same certificate. A press under the last row, or
        /// on the headings, opens nothing: there is no certificate there to act on.
        ///
        /// **Opened from the keyboard, its first choice that can be made is highlighted**, as the
        /// Refuse menu's is, so that Enter has something to choose. Opened by the pointer, nothing
        /// is, which is how a menu comes up under a click.
        ///
        /// Run from the grid's window procedure, where whatever escapes would reach WinForms' own
        /// error dialog over a settings dialog that stays open: a failure of a kind the user
        /// interface throws is written under the list instead, and what is left is a defect, which
        /// the window procedure records through `WindowHandlers`.
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

                if (row >= _rows.Count)
                {
                    return;
                }

                var listed = _rows[row];
                var answer = AnswerText(StandingAt(row));
                _certificateItem.Text = CertificateText(listed);
                _answerItem.Text = answer ?? string.Empty;
                // Set as Available, which is what is asked for, since Visible reads false for every
                // item of a menu that is not on screen yet.
                _answerItem.Available = answer != null;
                _revokeItem.Available = listed.Revocable;
                _allowItem.Available = listed.Record != null;
                _allowItem.Enabled = answer != null;
                _commandsSeparator.Available = listed.Revocable || listed.Record != null;

                _menuRow = listed.Id;
                _chosen = null;
                _menu.Show(_grid, at);

                if (screen.HasValue)
                {
                    return;
                }

                // Allow renewal where it can be chosen, as the command the line above it is about;
                // else the first command that can be chosen.
                if (_allowItem.Enabled && _allowItem.Available)
                {
                    _allowItem.Select();
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

        /// <summary>
        /// The place in the list of the row of this <see cref="ClientListRow.Id"/>, or -1.
        /// </summary>
        private int RowOf(string id)
        {
            return _rows.FindIndex(row => string.Equals(row.Id, id, StringComparison.Ordinal));
        }

        /// <summary>
        /// Acts on what was chosen, in the message after the menu closed, on the row it was opened
        /// on, wherever that row is now.
        /// </summary>
        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            var chosen = _chosen;
            var id = _menuRow;
            _chosen = null;

            if (chosen != _allowItem && chosen != _revokeItem)
            {
                return;
            }

            Later(() =>
            {
                var row = RowOf(id);
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
        /// where a failure of a kind the user interface throws gets a line under this list and
        /// anything else is a defect, which `WindowHandlers` records, since the work runs through
        /// it. Straight through when there is no window yet, which is how a test reaches the same
        /// path.
        /// </remarks>
        private void Later(Action work)
        {
            Action guarded = () => WindowHandlers.Run("a choice from a client's menu could not be carried out", () =>
            {
                try
                {
                    work();
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    Said("That could not be done: " + error.Message);
                }
            });

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
                var copy = m;
                WindowHandlers.Run("a message to the clients list could not be handled", () =>
                {
                    if (copy.Msg == ContextMenuMessage && MenuAsked != null)
                    {
                        // Minus one in both halves when the keyboard asked, and a point on the
                        // screen, each half signed, when the pointer did.
                        var position = unchecked((int)copy.LParam.ToInt64());
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

                    base.WndProc(ref copy);
                });
                m = copy;
            }
        }

        /// <summary>
        /// A row that tells UI automation the standing answer about its certificate.
        /// </summary>
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
        /// Draws a profiles cell one profile at a time, the removed ones in gray, a name wider than
        /// the column ending in an ellipsis and its mark whole.
        /// </summary>
        /// <remarks>
        /// A cell has one color, and the point is that a row holding `reader` and a removed `ops`
        /// says which of the two still grants anything; the words say it and the gray says it at a
        /// glance.
        ///
        /// **Every cell holding a profile is drawn here.** A profile name has no space to fold at,
        /// so the grid sizes the row a line per profile, and its own painting folds the whole list
        /// into the column: a name wider than the column took two lines where it was given a single
        /// line, and every profile after it was drawn below the row. A line at a time, each profile
        /// gets the lines the grid sized the row for it, so nothing after a long name moves.
        ///
        /// **The name is what is cut, never the mark.** A name wider than its line ends in an
        /// ellipsis and the row's tooltip holds it whole; a removed name that does not fit beside
        /// its mark has the mark on the next line, which is where the grid's measurement put it.
        /// </remarks>
        private void PaintProfiles(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= _rows.Count || _rows[e.RowIndex].Record == null || _rows[e.RowIndex].Record.Profiles.Count == 0)
            {
                return;
            }

            var record = _rows[e.RowIndex].Record;

            e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

            var selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var ink = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;

            // Gray on the selection's blue is barely there, so a selected row dims to a light shade
            // of its own background instead, which reads as dimmer than the lines beside it.
            var dim = selected
                ? ControlPaint.Light(e.CellStyle.SelectionBackColor)
                : SystemColors.GrayText;

            // How the grid measured the row, which decides how many lines each profile has.
            var measured = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                           | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

            // How each line is drawn: never folded, and cut with an ellipsis where it has to be.
            var drawn = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                        | TextFormatFlags.PreserveGraphicsClipping;

            // Where a wrapping text box cell draws its text: inside the border it owns on the right
            // and at the bottom, inside the padding, and one pixel down. A line that fits is then
            // drawn exactly where the grid's own painting drew it, and its measurement agrees with
            // the grid's about which lines fit.
            var bounds = new Rectangle(
                e.CellBounds.Left + e.CellStyle.Padding.Left,
                e.CellBounds.Top + e.CellStyle.Padding.Top + 1,
                Math.Max(1, e.CellBounds.Width - 1 - e.CellStyle.Padding.Horizontal),
                Math.Max(1, e.CellBounds.Height - 1 - e.CellStyle.Padding.Vertical - 2));

            var top = bounds.Top;
            foreach (var profile in record.Profiles)
            {
                var removed = Removed(profile);
                var color = removed ? dim : ink;
                var shown = removed ? profile + RemovedSuffix : profile;
                var height = TextRenderer.MeasureText(
                    e.Graphics, shown, e.CellStyle.Font, new Size(bounds.Width, 0), measured).Height;
                var line = TextRenderer.MeasureText(
                    e.Graphics, shown, e.CellStyle.Font, new Size(bounds.Width, 0), drawn).Height;

                if (removed && height > line)
                {
                    // Folded at the only space there is, the space before the mark.
                    Draw(e, profile, new Rectangle(bounds.Left, top, bounds.Width, line), color, drawn);
                    Draw(e, RemovedSuffix.Trim(), new Rectangle(bounds.Left, top + line, bounds.Width, height - line),
                        color, drawn);
                }
                else
                {
                    Draw(e, shown, new Rectangle(bounds.Left, top, bounds.Width, height), color, drawn);
                }

                top += height;
            }

            e.Handled = true;
        }

        private static void Draw(
            DataGridViewCellPaintingEventArgs e, string text, Rectangle where, Color color, TextFormatFlags flags)
        {
            TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font, where, color, flags);
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
        /// The clients among these admissions, each once by its client key, sorted by name: what a
        /// sentence or a column calling them clients counts and names.
        /// </summary>
        /// <remarks>
        /// **A client is its client key**, the digest of the certificate it paired with. A client
        /// that renewed holds two admissions until it gives the older certificate up, both carrying
        /// its client key, so it is one client here and named once.
        ///
        /// **And one name is not always one client**: a client chooses its own name when it pairs,
        /// and nothing holds two programs to different ones, such as one program on two machines.
        /// Those are two clients, and where two of them share a name each is named with the start
        /// of its client key as the Clients page shows it, "agent (3f2a9c1e)", so a sentence naming
        /// them names two clients the operator can tell apart.
        /// </remarks>
        internal static IReadOnlyList<string> ClientNames(IEnumerable<AdmissionRecord> records)
        {
            var clients = new Dictionary<string, string>(StringComparer.Ordinal);
            if (records != null)
            {
                foreach (var record in records.Where(
                    record => record != null && !clients.ContainsKey(record.ClientKey)))
                {
                    clients[record.ClientKey] = Describe(record);
                }
            }

            var shared = new HashSet<string>(
                clients.Values.GroupBy(name => name, StringComparer.Ordinal)
                              .Where(named => named.Count() > 1)
                              .Select(named => named.Key),
                StringComparer.Ordinal);

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
        /// How many characters of a key are shown where it is shortened, unless two shown together
        /// would then read the same.
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
                foreach (var key in clientKeys.Where(key => !string.IsNullOrEmpty(key) && !keys.Contains(key)))
                {
                    keys.Add(key);
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
        /// How long this admission still reaches the database, or why it does not reach it at all.
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
        /// expired at the instant `now &gt; notAfter` is still serving it.
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
                // Every admission here parsed its certificate once already: an admission reads
                // only where the bytes it stores are a certificate whose digest is its key. What
                // this catches is the platform failing to parse the same bytes a second time, and
                // then the row keeps its place, and shows nothing about dates it could not read.
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

        /// <summary>
        /// Takes the line under the list away after a gesture that went through, and answers null.
        /// </summary>
        /// <remarks>
        /// The rows show what went through: a revoked group goes at once and a lifted name stops
        /// being red, and the note under the pages states that changes apply on OK, so only a
        /// gesture that did not go through puts words here.
        /// </remarks>
        private string Done()
        {
            _said = string.Empty;
            Note();
            return null;
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
                    : _rows.Count > 0
                        ? string.Empty
                        : _revoked ? EmptiedText : NobodyText;

            _note.Text = text;
            _note.Visible = text.Length > 0;

            _reappeared.Text = _cameBack > 0 ? ReappearedText(_cameBack) : string.Empty;
            _reappeared.Visible = _cameBack > 0;
        }
    }

    /// <summary>
    /// A row of the clients page: a certificate whose admission reads, or a record of kind
    /// `client` that does not, under the client its client key names.
    /// </summary>
    public sealed class ClientListRow
    {
        public ClientListRow(
            AdmissionRecord record, UnreadAdmission unread, string clientKey, bool own, AdmissionStanding standing, bool writable)
        {
            if ((record == null) == (unread == null))
            {
                throw new ArgumentException("A row is an admission that reads or a record that does not, and not both.");
            }

            Record = record;
            Unread = unread;
            ClientKey = clientKey;
            Own = own;
            Standing = standing;
            Writable = writable;
        }

        /// <summary>The admission, or null for a record that does not read.</summary>
        public AdmissionRecord Record { get; private set; }

        /// <summary>The record that does not read, or null for an admission that reads.</summary>
        public UnreadAdmission Unread { get; private set; }

        /// <summary>
        /// The client key the row is grouped under, or null for a record that names none.
        /// </summary>
        public string ClientKey { get; private set; }

        /// <summary>
        /// Whether it is its client's own row, the first of its group, or a row that stands alone.
        /// </summary>
        public bool Own { get; private set; }

        /// <summary>
        /// What the admission comes to; admitted for a record that does not read, which shows its
        /// own text.
        /// </summary>
        public AdmissionStanding Standing { get; private set; }

        /// <summary>
        /// Whether the database's store takes a write of the operator's in this session, which
        /// every command on the store the row offers needs, `STORE.md` §636.
        /// </summary>
        public bool Writable { get; private set; }

        /// <summary>
        /// Whether pressing its X revokes a client: a client's own row that names a client key, in
        /// a store that takes a write.
        /// </summary>
        public bool Revocable
        {
            get { return Own && ClientKey != null && Writable; }
        }

        /// <summary>
        /// What tells this row from every other: its entry, or its certificate where it has none.
        /// </summary>
        public string Id
        {
            get
            {
                var entry = Record != null ? Record.Entry : Unread.Entry;
                return entry ?? (Record != null ? Record.AdmissionKey : Unread.Certificate) ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// How far an admission's certificate gets with the gate before the gate looks at the
    /// admission, asked in the order the gate asks it.
    /// </summary>
    /// <remarks>
    /// `ClientCredentials` pins the anchor, then asks for client authentication, then the dates,
    /// and only then the admission. The pages of the KPG tab ask the same questions in the same
    /// order, through the same methods, so that neither of them lists as admitted a client the
    /// gate refuses on every connection. The last question, whether the certificate has ended, is
    /// left to the date the clients page shows.
    /// </remarks>
    public enum ClientReach
    {
        /// <summary>
        /// Nothing before the admission refuses it, and its date tells for how long.
        /// </summary>
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
