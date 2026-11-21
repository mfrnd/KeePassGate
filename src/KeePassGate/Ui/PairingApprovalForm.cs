using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The dialog that asks whether a stranger may have a certificate for this database.
    /// </summary>
    /// <remarks>
    /// One of the two places an API request reaches a screen, the renewal dialog modeled on it being
    /// the other, and everything about it assumes the operator is being interrupted by somebody
    /// else's program.
    ///
    /// - **Nothing is approved by default.** It comes up without taking the focus, Escape and
    ///   Enter both refuse, and Approve has no mnemonic and is never the default button;
    ///   <see cref="ApprovalDialog"/> says why. Approving takes a deliberate press. A dialog that
    ///   can be dismissed into a yes is a dialog that will be.
    /// - **The profiles are confirmed rather than accepted.** They arrive as a request and leave as
    ///   whatever is still checked, because the caller asking for something is not the same as the
    ///   operator agreeing to it. So does the lifetime: what the client asked for is preselected,
    ///   a year when it asked for nothing, and the operator picks.
    /// - **It closes itself.** Nobody being at the keyboard need not end in a refusal that costs the
    ///   invitee its invitation, so an unanswered dialog is a timeout, which spends nothing.
    /// - **Dismissing it is refusing it.** The close box, Alt and F4, the window menu and the
    ///   taskbar all end the dialog without a button being pressed, and somebody was at the screen
    ///   for every one of them. Only the countdown answers for an operator who is not.
    /// - **Every string on it is what the request said**, and the request came from outside. Names
    ///   and profiles have already been checked to be plain names by the time they reach here.
    /// </remarks>
    public sealed class PairingApprovalForm : ApprovalDialog
    {
        private readonly ProfileTable _profiles;
        private readonly LifetimeChoice _lifetime;
        private readonly Label _countdown;
        private readonly Timer _clock;

        /// <summary>Approve, which a dialog shown again holds back for a moment.</summary>
        private readonly ApproveButton _approve;

        /// <summary>
        /// Refresh, a square button with KeePass's own picture and no text at the right end of the
        /// line above the table, as tall as Refuse and Approve, enabled exactly while a change of
        /// the database waits to be shown.
        /// </summary>
        private readonly Button _refresh;

        /// <summary>
        /// What shows the tooltip of Refresh while Refresh is disabled, once the pointer has rested
        /// on Refresh for the tooltips' delay, <see cref="Pointing"/>.
        /// </summary>
        private readonly Timer _resting;

        /// <summary>Whether the pointer stands on Refresh while Refresh is disabled.</summary>
        private bool _onRefresh;

        /// <summary>
        /// Where the latest move of the mouse over the line above the table was, on the screen:
        /// where the tooltip of a disabled Refresh comes up, <see cref="ShowRefreshTip"/>.
        /// </summary>
        private Point _moved;

        /// <summary>Whether the tooltip of a disabled Refresh stands, <see cref="ShowRefreshTip"/>.</summary>
        private bool _refreshTipShown;

        /// <summary>
        /// The picture Refresh shows, the dialog's own and let go with the dialog, or null where
        /// the reading of KeePass's picture answered nothing.
        /// </summary>
        private readonly Image _picture;

        /// <summary>
        /// The words beside Refresh: <see cref="DatabaseChanged"/> while a change waits to be
        /// shown, <see cref="CannotRead"/> while a reading fails, <see cref="ReadingStopped"/> once
        /// a reading threw, <see cref="StateChanged"/> on a dialog shown again until a change
        /// waiting replaces the notice, and empty otherwise.
        /// </summary>
        private readonly StatusLine _change;

        /// <summary>
        /// The tips of Refresh and of the words beside it, shown always, since the dialog comes up
        /// without being activated, as the table's are.
        /// </summary>
        private readonly ToolTip _tips = new ToolTip { ShowAlways = true, AutoPopDelay = 32000, InitialDelay = 300, ReshowDelay = 100 };

        /// <summary>
        /// Whether the words beside Refresh hold the notice of a dialog shown again: from the start
        /// on such a dialog, until a change waits to be shown.
        /// </summary>
        private bool _again;

        /// <summary>
        /// Whether KeePass is part way through work of its own, asked before every reading the
        /// dialog takes while it stands, `PairingApprovalPrompt.Busy`.
        /// </summary>
        private readonly Func<bool> _busy;

        /// <summary>
        /// The request as the dialog shows it: its content as the dialog came up or as a Refresh
        /// showed it, or as a reading that shows the same holds it.
        /// </summary>
        private PairingRequest _request;

        /// <summary>A reading that shows otherwise than the dialog, waiting for Refresh, or null.</summary>
        private AskedProfiles _pending;

        /// <summary>Whether the last reading failed or did not serve.</summary>
        private bool _unreadable;

        /// <summary>
        /// Whether Approve may take a press as far as the moment held back goes: from the start on
        /// a first dialog, and once <see cref="ApproveHeldBack"/> has passed on a dialog shown
        /// again.
        /// </summary>
        private bool _armed;

        /// <summary>
        /// The approval given when Approve was pressed, of the profiles then checked and held to
        /// what the dialog then showed, `PairingRequest.Approve`.
        /// </summary>
        private PairingApproval _approval;

        /// <summary>
        /// What ends a moment held back once <see cref="ApproveHeldBack"/> has passed: since a
        /// dialog shown again came up, since a Refresh, or since the focus was moved off a control
        /// being disabled, <see cref="Hold"/>.
        /// </summary>
        private readonly Timer _arming;

        /// <summary>
        /// Whether a key press is held back: while it is, no key but Enter and Escape does
        /// anything, no mnemonic, Tab, arrow or Space among them, and Enter and Escape refuse as
        /// ever, <see cref="ProcessCmdKey"/>, <see cref="ProcessDialogChar"/> and
        /// <see cref="ProcessKeyPreview"/>.
        /// </summary>
        private bool _held;

        /// <summary>The answer where a reading under the dialog found the store no longer serving.</summary>
        private PairingApproval _noLongerServed;

        /// <summary>
        /// Whether a reading under the dialog threw, after which the dialog reads nothing more and
        /// stands unreadable until it ends, <see cref="Reading"/>.
        /// </summary>
        private bool _stopped;

        /// <summary>
        /// How long Approve stays disabled once a dialog shown again comes up: twice the 500
        /// milliseconds that Windows takes by default for the two presses of a double click.
        /// </summary>
        /// <remarks>
        /// **A dialog shown again comes up where the dialog before stood, just after Approve on it
        /// was pressed**, so a press still on its way, the second press of a double click or a
        /// press made as the first dialog closed, would land on the same place and answer the
        /// dialog shown again unread. Held back for this moment, such a press lands on a button
        /// that takes none. Fixed rather than read from the system's double click setting, so that
        /// the moment is the same on every desk; Refuse stays enabled, Enter refuses wherever the
        /// focus is, as on every approval dialog, and no focus is taken when Approve is enabled.
        /// </remarks>
        public static readonly TimeSpan ApproveHeldBack = TimeSpan.FromMilliseconds(1000);

        /// <summary>
        /// What approving costs, stated once. Separate from the table above it because this is a
        /// sentence to read rather than a value to check.
        /// </summary>
        /// <remarks>
        /// The save and where to undo it, which is what the operator acts on. That the admission
        /// is on disk before the client is told anything is how the save is ordered, which
        /// `PAIRING.md` sets out. **Every sentence of this dialog names its nouns and no profile
        /// or client**: the table shows which rows a sentence is about, and a pronoun leaves the
        /// reader looking for what it stands for.
        /// </remarks>
        public const string Consequence =
            "Approve issues a certificate and saves this database. The " + GateName.OnATab +
            " tab revokes the certificate.";

        /// <summary>
        /// What the table underneath is, named in terms of what the client asked for rather than as
        /// a bare "these", whatever the states in it: the heading of the boxes carries what a check
        /// does, and the tooltip of each state's cell what Approve does for a profile in it.
        /// </summary>
        public const string ProfilesAsked = ProfilesAskedName + ":";

        /// <summary>
        /// The table's name, which UI automation reads as the table's: the line above the table,
        /// <see cref="ProfilesAsked"/>, without its colon.
        /// </summary>
        public const string ProfilesAskedName = "Profiles the client asks for";

        /// <summary>
        /// The same when it asked for nothing, which is unusual enough to state plainly; what
        /// approving then grants is the tooltip of the line, <see cref="NoProfilesTip"/>.
        /// </summary>
        /// <remarks>
        /// **Short enough to leave the words beside Refresh whole** at the size the dialog is
        /// designed at, since the line holds this, the words and Refresh.
        /// </remarks>
        public const string NoProfilesAsked = "The client asks for no profile.";

        /// <summary>The tooltip of <see cref="NoProfilesAsked"/>: what approving such a request grants.</summary>
        public const string NoProfilesTip = "Approved, the client reaches nothing.";

        /// <summary>
        /// The heading of the column of check boxes: whether the client is granted the profile of
        /// the row, which is what a check decides.
        /// </summary>
        public const string GrantHeading = "Grant";

        /// <summary>
        /// The tooltip of a cell of the column <see cref="StateHeading"/> holding
        /// <see cref="NewState"/>, and what is read out after the name as its row: what Approve
        /// does for such a profile.
        /// </summary>
        /// <remarks>
        /// Whether any rule names a new name is a walk of the whole tree, which a connection does
        /// not make, so the sentence states what approving does either way.
        /// </remarks>
        public const string NewTip = "Approve adds the profile to this database. Rules already naming the profile apply at once.";

        /// <summary>
        /// The tooltip of a cell of the column <see cref="StateHeading"/> holding
        /// <see cref="RemovedState"/>, and what is read out after the name as its row: what Approve
        /// does for such a profile.
        /// </summary>
        /// <remarks>
        /// The same warning the Profiles page gives before a removed name is added back, because
        /// approving a removed name here is exactly that: pairing writes the approved names onto
        /// the list. Which clients regain the name is in its row, in a column of its own.
        /// </remarks>
        public const string RemovedTip = "Approve restores the profile for every rule and client still naming the profile.";

        /// <summary>The heading of the table's column of names.</summary>
        public const string ProfileHeading = "Profile";

        /// <summary>
        /// The heading of the column stating what Approve does besides admitting the client: for a
        /// name off the list, that it adds the name or restores it, `NewState` and `RemovedState`.
        /// </summary>
        public const string StateHeading = "Approve also";

        /// <summary>The heading of the column of the clients that regain a restored name.</summary>
        public const string HeldByHeading = "Regained by";

        /// <summary>
        /// The background of a row that changed after the profile was first shown, on a dialog
        /// shown again or by a Refresh, a light yellow, #FFF3B0.
        /// </summary>
        /// <remarks>
        /// **Light enough to read the row's text on**: with the window's text of the normal theme,
        /// black, its contrast is near 19 to 1, and it stands clear of the window's white beside
        /// it. A row whose state changed opens unchecked, as a row the operator unchecked does, so
        /// unchecked alone would mean both; the color tells them apart, and the tooltip,
        /// <see cref="ChangeTip"/>, and the row's own text, which UI automation reads, state the
        /// change in words, so that the meaning rests on no color alone. Under a high contrast
        /// setting no fixed color is drawn, <see cref="ChangedColors"/>.
        /// </remarks>
        public static readonly Color ChangedBackground = Color.FromArgb(0xFF, 0xF3, 0xB0);

        /// <summary>
        /// The tooltip anywhere on a row whose state changed after the profile was first shown, on
        /// a dialog shown again or by a Refresh, where the state now reads
        /// <see cref="NewState"/>; and what is read out after its name as its box.
        /// </summary>
        /// <remarks>
        /// **What changed, and to what the cell now reads**, in a sentence that names no profile
        /// and no client: a highlight alone left the operator looking for the change, and a state
        /// cell that went empty showed nothing at all. **The state before the clients**: where the
        /// state changed, what Approve does is what changed, and the clients follow from the state,
        /// <see cref="ChangeTip"/>.
        /// </remarks>
        public const string NowAddsTip = "The row changed after the profile was first shown: Approve now also adds the profile.";

        /// <summary>The same where the state now reads <see cref="RemovedState"/>.</summary>
        public const string NowRestoresTip = "The row changed after the profile was first shown: Approve now also restores the profile.";

        /// <summary>The same where the profile is on the list now, so that its state cell is empty.</summary>
        public const string NowListedTip = "The row changed after the profile was first shown: this database names the profile now.";

        /// <summary>
        /// The tooltip anywhere on a row whose state reads as when the profile was first shown and
        /// whose clients changed: some added and none removed, on a dialog shown again or by a
        /// Refresh.
        /// </summary>
        public const string ClientsAddedTip = "The row changed after the profile was first shown: clients were added.";

        /// <summary>The same where some clients were removed and none added.</summary>
        public const string ClientsRemovedTip = "The row changed after the profile was first shown: clients were removed.";

        /// <summary>The same where some clients were added and others removed.</summary>
        public const string ClientsChangedTip = "The row changed after the profile was first shown: clients were added and removed.";

        /// <summary>
        /// What changed in a profile's row after the profile was first shown, the tooltip of the
        /// row and what is read out last as its box, or null for a row that did not change.
        /// </summary>
        /// <remarks>
        /// **Each sentence true of the row as it stands**: a state that changed is told by what the
        /// state cell reads now, whatever it read in between, and the clients by whether any were
        /// added or removed since the profile was first shown, `PairingRequest.ChangeOf`.
        /// </remarks>
        public static string ChangeTip(PairingRequest request, string profile)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var change = request.ChangeOf(profile);
            if ((change & RowChange.State) != 0)
            {
                return request.IsRemoved(profile) ? NowRestoresTip : request.IsNew(profile) ? NowAddsTip : NowListedTip;
            }

            var added = (change & RowChange.ClientsAdded) != 0;
            var removed = (change & RowChange.ClientsRemoved) != 0;
            return added && removed ? ClientsChangedTip : added ? ClientsAddedTip : removed ? ClientsRemovedTip : null;
        }

        /// <summary>
        /// The colors of a row that changed after the profile was first shown: its background and
        /// its text.
        /// </summary>
        /// <param name="highContrast">Whether a high contrast setting is on, `SystemInformation.HighContrast`.</param>
        /// <remarks>
        /// **Under a high contrast setting, the theme's own pair**: the theme's background, and its
        /// hyperlink color for the text, `SystemColors.HotTrack`, which a contrast theme keeps
        /// legible on that background and apart from its ordinary text. A fixed yellow there
        /// would be drawn behind the theme's text, which on a dark theme is light and unreadable on
        /// it. Read as the dialog is built, which a setting changed while it stands does not
        /// follow; the tooltip and the text read out stand either way.
        /// </remarks>
        internal static (Color Back, Color Fore) ChangedColors(bool highContrast)
        {
            return highContrast
                ? (SystemColors.Window, SystemColors.HotTrack)
                : (ChangedBackground, SystemColors.WindowText);
        }

        /// <summary>
        /// The state of a name this database does not name now and is not shown as removed,
        /// `PairingRequest.IsNew`. Approving such a name adds it to the list.
        /// </summary>
        public const string NewState = "adds the profile";

        /// <summary>
        /// The state of a name off the list that a removal known to this database names, made in
        /// this copy or brought in by a synchronize, that clients still hold, or that a write of
        /// the same request found a removal of, `PairingRequest.IsRemoved`. Approving such a name
        /// puts it back on the list, every rule still naming it applies again, and every client
        /// still holding it regains it.
        /// </summary>
        public const string RemovedState = "restores the profile";

        /// <summary>Every text a cell under <see cref="StateHeading"/> can hold, which its column is as wide as.</summary>
        private static readonly string[] States = { NewState, RemovedState };

        /// <summary>
        /// The cells of a profile's row, in the order of the columns: its name, its state, and the
        /// clients still holding it.
        /// </summary>
        /// <remarks>
        /// **The state in a column of its own**, never joined to the name: a name and its state
        /// read as one word are a spelling to notice, and a long name cut at the edge of the list
        /// would take its state with it. A name on the list has no state.
        ///
        /// **The clients each once, by their client keys**, as the Profiles page names them, and
        /// each on a line of its own, however many there are: the row is as tall as its clients,
        /// and the table scrolls to its last line. A client whose certificate has expired is not
        /// among them, since it regains nothing.
        /// </remarks>
        public static IReadOnlyList<string> Cells(PairingRequest request, string profile)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var state = request.IsRemoved(profile) ? RemovedState : request.IsNew(profile) ? NewState : string.Empty;
            var heldBy = string.Join(Environment.NewLine, ClientList.ClientNames(request.Returning(profile)));
            return new[] { profile, state, heldBy };
        }

        /// <summary>
        /// What Approve does for a profile in the state the dialog shows it in, the tooltip of its
        /// state's cell: <see cref="NewTip"/> or <see cref="RemovedTip"/>, and null for a name on
        /// the list, which has no state.
        /// </summary>
        /// <remarks>
        /// **On the cell rather than under the table**: a line for each state under the table
        /// would stand apart from the rows it explains, and add to the dialog's height for words
        /// an operator reads once.
        /// </remarks>
        public static string StateTip(PairingRequest request, string profile)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return request.IsRemoved(profile) ? RemovedTip : request.IsNew(profile) ? NewTip : null;
        }

        /// <summary>
        /// A row's text read out, which UI automation reads as the row's box: the name, then what
        /// Approve does for it in the state its row shows, <see cref="StateTip"/>, then the
        /// clients regaining it where any do, then what changed, <see cref="ChangeTip"/>, where
        /// the row changed after the profile was first shown.
        /// </summary>
        /// <remarks>
        /// **The tooltip of the state's cell is read here**, and the clients beside it, so that
        /// the box, which is what a keyboard reaches, carries the whole row.
        /// </remarks>
        public static string Spoken(PairingRequest request, string profile)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var lines = new List<string>();
            var tip = StateTip(request, profile);
            if (tip != null)
            {
                lines.Add(tip);
            }

            var holding = ClientList.ClientNames(request.Returning(profile));
            if (holding.Count > 0)
            {
                lines.Add(HeldByHeading + ": " + string.Join(", ", holding) + ".");
            }

            var change = ChangeTip(request, profile);
            if (change != null)
            {
                lines.Add(change);
            }

            return lines.Count == 0 ? profile : profile + ". " + string.Join(" ", lines);
        }

        /// <summary>
        /// The line under the renewal dialog's list of this many profiles,
        /// <see cref="ScrollingLine"/>, or null for a list that shows every profile.
        /// </summary>
        internal static string Scrolling(int profiles)
        {
            return profiles <= MostRows ? null : ScrollingLine;
        }

        /// <summary>
        /// The line under the renewal dialog's list while it holds more profiles than it shows, so
        /// that some are out of sight.
        /// </summary>
        /// <remarks>
        /// **Approve grants every profile checked, whether it was ever on screen or not**, and a
        /// list past the rows it shows has no other sign of the rest than its scroll bar. This
        /// dialog's table shows the rest by a line cut in half at its bottom instead, `ProfileTable`.
        /// </remarks>
        internal const string ScrollingLine = "The list scrolls: Approve grants every checked profile.";

        /// <summary>
        /// The words beside Refresh as the dialog shown again for a request whose approval Approve
        /// found stale comes up, <see cref="StateChanged"/>; null on any other dialog.
        /// </summary>
        /// <remarks>
        /// **From the request that puts the dialog up again**, `PairingRequest.Again`, and never
        /// from anything the client sends. **The words state only that the table changed**:
        /// never which cause nor which profile, since the rows show what changed, a state or the
        /// clients regaining a profile, and the plugin's log names each kind of cause.
        /// </remarks>
        public static string Again(PairingRequest request)
        {
            return request != null && request.Again ? StateChanged : null;
        }

        /// <summary>
        /// The words beside Refresh on a dialog shown again, whatever Approve found, until a change
        /// waiting to be shown replaces them: true where a state changed and where only the
        /// clients regaining a profile did.
        /// </summary>
        public const string StateChanged = "Shown again: the table changed.";

        /// <summary>
        /// The words beside Refresh while what the database holds differs from what the dialog
        /// shows, and Approve is disabled until Refresh shows it; the tooltip of Refresh then
        /// tells the rest, <see cref="RefreshShowsTip"/>.
        /// </summary>
        /// <remarks>
        /// **The dialog does not change by itself**: a row that moved or changed its state under
        /// the pointer would be a row approved unread. So the dialog states that the database
        /// changed, Refresh shows the change, and Approve waits for it.
        /// </remarks>
        public const string DatabaseChanged = "The database changed.";

        /// <summary>
        /// The words beside Refresh while the dialog's reading of the database fails and the next
        /// tick reads again: Approve and Refresh are disabled until a reading serves. A reading
        /// that finds the store not served ends the dialog instead, <see cref="Watch"/>.
        /// </summary>
        public const string CannotRead = "The database cannot be read for now.";

        /// <summary>
        /// The words beside Refresh once a reading under the dialog threw, after which the dialog
        /// reads nothing more: Approve and Refresh stay disabled and Refuse enabled until the
        /// dialog ends, <see cref="Reading"/>, so that the words promise no later reading.
        /// </summary>
        public const string ReadingStopped = "The database cannot be read.";

        /// <summary>
        /// What UI automation reads as Refresh, which has a picture and no text of its own to be
        /// read by.
        /// </summary>
        public const string RefreshName = "Refresh";

        /// <summary>
        /// The tooltip of Refresh while no change waits to be shown: its name and its key.
        /// </summary>
        public const string RefreshTip = "Refresh (F5)";

        /// <summary>
        /// The tooltip of Refresh while a change waits to be shown, under the words
        /// <see cref="DatabaseChanged"/> beside it: what a press does, and its key.
        /// </summary>
        public const string RefreshShowsTip = "Refresh shows the change. (F5)";

        /// <summary>What UI automation reads as the whole of the dialog's content.</summary>
        internal const string LayoutRead = "Pairing request";

        /// <summary>
        /// What UI automation reads as the line above the table: its name, the words beside
        /// Refresh and Refresh.
        /// </summary>
        internal const string StatusRead = "Status";

        /// <summary>The line above the table, which depends on whether any profile is asked for.</summary>
        public static string Asking(PairingRequest request)
        {
            return request == null || request.Profiles.Count == 0 ? NoProfilesAsked : ProfilesAsked;
        }

        private readonly Font _titleFont;
        private readonly Font _valueFont;

        /// <summary>
        /// How long the operator gets, and how much of it has gone.
        /// </summary>
        /// <remarks>
        /// A stopwatch rather than two readings of the wall clock. The connection thread waiting on
        /// this dialog is waiting on a handle, which counts monotonically, and a clock that steps
        /// while the dialog is up would put the two out of step: backwards and the dialog never
        /// closes itself while the caller gives up anyway, leaving a live-looking Approve button
        /// that issues nothing; forwards and it slams shut in the middle of being read. Both happen
        /// for ordinary reasons: a time correction, or a laptop coming back from sleep.
        /// </remarks>
        private readonly TimeSpan _allowed;
        private readonly int _wrap;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();

        private PairingDecision _decision = PairingDecision.TimedOut;

        /// <summary>Whether the answer above is one that was given, or the one it started at.</summary>
        private bool _decided;

        /// <summary>Whether a close of the database it is about withdrew it, rather than a lock.</summary>
        private bool _closedWithDatabase;

        public PairingApprovalForm(PairingRequest request, TimeSpan timeout)
            : this(request, timeout, null, null)
        {
        }

        /// <summary>
        /// The same, built at a font other than the system's, which is how a test stands in for a
        /// display that was scaled before the dialog was made.
        /// </summary>
        internal PairingApprovalForm(PairingRequest request, TimeSpan timeout, Font font)
            : this(request, timeout, font, null, null)
        {
        }

        /// <summary>
        /// The same at a font of its own, or the system's for null, reading the database again
        /// while it stands unless <paramref name="busy"/> answers that KeePass is part way through
        /// work of its own.
        /// </summary>
        /// <param name="busy">
        /// Whether KeePass is part way through work of its own, asked before every reading taken
        /// while the dialog stands; null for never.
        /// </param>
        internal PairingApprovalForm(PairingRequest request, TimeSpan timeout, Font font, Func<bool> busy)
            : this(request, timeout, font, busy, null)
        {
        }

        /// <summary>
        /// The same, with Refresh showing the picture this reads, as the dialog is built.
        /// </summary>
        /// <param name="picture">
        /// A picture of the caller's own for Refresh, which the dialog keeps and lets go with
        /// itself, answering null where there is none; null for no picture. **No text stands in
        /// for a missing picture**: Refresh is then a button with neither, named, with its tooltip
        /// and its key, and `KeePassContractTests` is what tells of a KeePass without the picture.
        /// </param>
        /// <remarks>
        /// **Everything that sizes this dialog is measured while it is built**, from the font it is
        /// built at: how wide a line may be, and how tall the table is for the rows it holds. A
        /// larger font handed in here is measured exactly the way a larger system font is on a
        /// scaled display, which is the path the dialog takes in use. Changing the font of a dialog
        /// already built is a different path, and nothing takes it: KeePass never touches this
        /// dialog's font.
        /// </remarks>
        internal PairingApprovalForm(PairingRequest request, TimeSpan timeout, Font font, Func<bool> busy, Func<Image> picture)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (font != null)
            {
                Font = font;
            }

            _allowed = timeout;
            _request = request;
            _busy = busy ?? (() => false);
            _armed = !request.Again;
            _again = request.Again;
            _picture = picture == null ? null : picture();

            Name = GateTab.ApprovalFormName;
            Text = GateName.Product + ": a client is asking to pair";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;

            // Sized by what is in it rather than by a number written here, which is what
            // `GateStatusForm` does and for the same reason. A fixed client area is right at one
            // display scaling and wrong at every other, and here it failed in the granting
            // direction: at 125% the profile list showed one row of what was asked for and at 150%
            // it showed none, while every profile stayed checked and Approve granted all of them.
            //
            // **No automatic scaling**, because there is nothing for it to do. What the content is
            // sized from is measured below in the font the dialog is built at, which on a scaled
            // display is already the larger one; `AutoScaleMode.Font` was set here with no
            // dimensions to scale from, so it did nothing at any display scaling, and acted only
            // when the font changed after the dialog was built, which nothing does. **The insets
            // and gaps are converted to the display's units as they are written**, as the window
            // under Tools converts its own: left raw they stayed at their 100 percent pixels on a
            // display at 200 percent while everything they separate had doubled, and so did those
            // of the renewal dialog, the question of a day and the authority chooser.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // The prototype is disposed, because `SystemFonts.DefaultFont` hands back a new font
            // every time it is read and `new Font(prototype, style)` copies rather than adopts.
            using (var prototype = font == null ? SystemFonts.DefaultFont : new Font(font, font.Style))
            {
                _titleFont = new Font(prototype, FontStyle.Bold);
                _valueFont = new Font(prototype, FontStyle.Bold);
                _wrap = Wrap(prototype);
            }

            Padding = new Padding(LogicalToDeviceUnits(Spacing.Window));

            // Three regions rather than one column. The list of profiles is the part that grows
            // with what is being asked, so it takes whatever the other two leave: a fixed height
            // for it means scrolling through four profiles while the dialog holds empty space
            // underneath. The countdown and the buttons stay at the bottom, where a reader looks
            // for them and where they cannot be pushed off by a long list.
            var layout = new FlowLayoutPanel
            {
                Name = "KpgApprovalLayout",
                AccessibleName = LayoutRead,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.Controls.Add(new TextLabel
            {
                Name = "KpgApprovalTitle",
                Text = "A client is asking to pair with this database.",
                Font = _titleFont,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            });

            // A table rather than a paragraph. Every one of these is a value with a name, and the
            // operator is checking them against what they expect rather than reading them: a
            // database they know, a name they were told, an invitation they handed out. Bold on
            // the values, because those are the halves being compared.
            var details = new TableLayoutPanel
            {
                Name = GateTab.ApprovalDetailLabelName,
                AccessibleName = DetailsRead,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            };

            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var facts = Facts(request);
            details.RowCount = facts.Count;

            for (var at = 0; at < facts.Count; at++)
            {
                details.Controls.Add(Cell(facts[at].ControlName(false), facts[at].Name, false), 0, at);
                details.Controls.Add(Cell(facts[at].ControlName(true), facts[at].Value, true), 1, at);
            }

            layout.Controls.Add(details);

            layout.Controls.Add(new TextLabel
            {
                Name = "KpgApprovalConsequence",
                Text = Consequence,
                AutoSize = true,
                MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap))
            });

            // **The line above the table**: its name at the start, Refresh at the end, level with
            // the table's edge, and between the two the words stating a change of the database,
            // against Refresh, so that neither takes a line of its own. A gap under it, like
            // everything else on this dialog: the table is the one thing here an operator acts on,
            // and less room between the table and the line introducing the table than between the
            // table and the row after the table would make the table look attached to that row.
            _refresh = new RefreshButton
            {
                Name = GateTab.ApprovalRefreshName,
                AccessibleName = RefreshName,
                Image = _picture,
                Margin = new Padding(LogicalToDeviceUnits(Spacing.Gap), 0, 0, 0),
                Anchor = AnchorStyles.Right
            };
            var side = TextButtonHeight();
            _refresh.Size = new Size(side, side);

            // Bold, as the notice of a dialog shown again always was, since the words are why
            // Approve waits; a single line, cut with an ellipsis where a scaling leaves the line
            // too little room, and then whole in its tooltip, `ShowStatus`.
            _change = new StatusLine
            {
                Name = GateTab.ApprovalChangeName,
                Font = _valueFont,
                Margin = new Padding(LogicalToDeviceUnits(Spacing.Gap), 0, 0, 0),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            _change.Height = _valueFont.Height;

            // Measured once, at most a line less Refresh wide, and held at that size, so that the
            // row lays the name as measured rather than measuring it again in a cell; in the
            // dialog's own font, given before it is measured, as the table's is.
            var asking = new TextLabel
            {
                Name = "KpgApprovalAskingLabel",
                Text = Asking(request),
                Font = Font,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Left
            };
            asking.Size = asking.GetPreferredSize(new Size(_wrap - _refresh.Width - _refresh.Margin.Horizontal, 0));
            asking.AutoSize = false;

            // As wide as the table, so that Refresh ends where the table does, and as tall as the
            // tallest of the three: nothing in it changes its size while the dialog stands.
            var heading = new TableLayoutPanel
            {
                Name = "KpgApprovalAskingRow",
                AccessibleName = StatusRead,
                ColumnCount = 3,
                RowCount = 1,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Spacing.Gap)),
                Padding = Padding.Empty
            };

            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            heading.Controls.Add(asking, 0, 0);
            heading.Controls.Add(_change, 1, 0);
            heading.Controls.Add(_refresh, 2, 0);
            heading.Size = new Size(_wrap, new[] { asking.Height, _change.Height, _refresh.Height }.Max());
            Tip(asking, request.Profiles.Count == 0 ? NoProfilesTip : null);
            layout.Controls.Add(heading);

            // A disabled control is told of no pointer, which goes to the control under it, here
            // the line above the table: the line shows the tooltip of Refresh where a disabled
            // Refresh stands, `Pointing`.
            _resting = new Timer { Interval = _tips.InitialDelay };
            _resting.Tick += (sender, e) =>
                WindowHandlers.Run("the tooltip of Refresh could not be shown", ShowRefreshTip);
            heading.MouseMove += (sender, e) =>
                WindowHandlers.Run("the tooltip of Refresh could not be shown", () =>
                {
                    _moved = heading.PointToScreen(e.Location);
                    Pointing(_refresh.Bounds.Contains(e.Location));
                });
            heading.MouseLeave += (sender, e) =>
                WindowHandlers.Run("the tooltip of Refresh could not be taken away", () => Pointing(false));

            // As tall as its rows, between two rows of a single line and `MostRows` of them, never
            // fewer than two, because a table showing a row of two reads as a table of a row and
            // the operator is then approving what they cannot see; past the most, it scrolls, and
            // at the top of its rows a line cut in half at its bottom shows so. Approve grants
            // every profile checked, whether it was ever on screen or not, and past the lines the
            // table shows harmless names first and wanted names after would otherwise be approved
            // by a glance at the first: the line cut where the eye reaches the bottom of the table
            // is what tells of the rest, in no words that name a profile or a count.
            _profiles = new ProfileTable(
                new ProfileColumn(GateTab.ApprovalGrantColumnName, GrantHeading),
                new[]
                {
                    new ProfileColumn(GateTab.ApprovalProfileColumnName, ProfileHeading),
                    new ProfileColumn(GateTab.ApprovalStateColumnName, StateHeading, States),
                    new ProfileColumn(GateTab.ApprovalHeldByColumnName, HeldByHeading)
                },
                MostRows)
            {
                Name = GateTab.ApprovalProfilesListName,

                // A table has no text of its own to be announced by, so the line above the table
                // names the table, whatever the states in it; the headings name its columns.
                AccessibleName = ProfilesAskedName,

                // The dialog's own, given before it is measured below: a table not yet on the
                // dialog has no font of the dialog's to inherit, and its rows are what that
                // measures.
                Font = Font,

                // A line of the dialog's text, which the columns share, so that a long name or a
                // long list of clients is cut at the edge of its own column rather than widening
                // the dialog past the screen.
                Width = _wrap,
                Margin = new Padding(0)
            };

            // Every row checked on a first dialog. On a dialog shown again, the rows the operator
            // left checked before and in the state they were in then, `PairingRequest.Checks`, so
            // that a single Approve grants no name in a state the operator did not approve it in;
            // and a row whose state changed, which opens unchecked, highlighted as well, with the
            // change in its tooltip and in the text read out, `ChangedBackground`.
            foreach (var profile in request.Profiles)
            {
                _profiles.Add(profile, request.Checks(profile));
            }

            ShowRows(request);

            // Tall enough for what is being asked, up to a point, and never zero. This is the one
            // control the operator has to act on, so it is sized from the rows it holds rather
            // than from whatever two docked bands leave over, and measured again once it has a
            // window, `OnLoad`.
            _profiles.Lay(true);

            // How long the certificate lasts, under the profiles because both are what approving
            // grants, and over the footer because the buttons are what acts on them.
            _lifetime = new LifetimeChoice(request.Lifetime, Font, request.Picked);

            // A table rather than a flow, because the buttons go against the right edge and a flow
            // panel lays every child out at its own left one. A cell is what `Anchor` answers to.
            // The width is the table's, the widest thing on the dialog, since a column that sized
            // itself to its widest child would put the right edge wherever the buttons happen to
            // end and right align them against themselves.
            // One row of two, the countdown and the answers, and nothing else: what states a change
            // of the database stands beside Refresh above the table. What is left is a sentence
            // and what is right is the pair of buttons, and putting them on separate rows spent a
            // line of the dialog on nothing. The left column takes the slack so the right column
            // ends where the content does, which is what puts the buttons against the edge.
            var footer = new TableLayoutPanel
            {
                Name = "KpgApprovalFooter",
                AccessibleName = AnswerRead,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                MinimumSize = new Size(_profiles.Width, 0),

                // A gap above, which is what separates this row from the table it answers
                // about. Nothing below: the form's own padding closes the dialog.
                Margin = new Padding(0, LogicalToDeviceUnits(Spacing.Gap), 0, 0)
            };

            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _countdown = new TextLabel
            {
                Name = GateTab.ApprovalCountdownName,
                Text = Remaining(timeout),
                Font = Font,
                Margin = new Padding(0),

                // Left, and nothing else: a control anchored to one side of a cell is centered on
                // the other axis, which is what levels this line with the buttons beside it rather
                // than with their top edge.
                Anchor = AnchorStyles.Left
            };

            // Measured once, at its widest, and held at that size, so that its words changing
            // every second lay nothing out, `CountdownSize`.
            _countdown.Size = CountdownSize(_countdown, GateTab.ApprovalCountdownFormat, timeout, _wrap);

            footer.Controls.Add(_countdown, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Name = "KpgApprovalButtonRow",
                AccessibleName = ButtonsRead,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),

                // Right, and nothing else, for the same two reasons the countdown is anchored
                // left: it decides which end of the cell they sit at, and it levels them with the
                // line beside them.
                Anchor = AnchorStyles.Right
            };

            // Both margins are written down, and the vertical halves have to match. A flow panel
            // puts each control at the top of the row plus that control's own top margin, so a
            // button left with the default margin sits a few pixels below one given none, and two
            // buttons of the same size on the same row read as a mistake nobody can name.
            var refuse = new Button
            {
                Name = GateTab.RefuseButtonName,
                Text = "&" + GateTab.RefuseButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };

            var approve = new ApproveButton
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 0)
            };

            _approve = approve;
            _held = request.Again;
            _arming = new Timer { Interval = (int)ApproveHeldBack.TotalMilliseconds };
            _arming.Tick += (sender, e) =>
                WindowHandlers.Run("Approve on the pairing dialog could not be enabled", Arm);

            // Above the table and before it in the tab order: it answers nothing, and is enabled
            // only while a change of the database waits to be shown. No mnemonic, having no text:
            // F5 presses it from anywhere on the dialog, `ProcessCmdKey`.
            _refresh.Click += delegate
            {
                WindowHandlers.Run("Refresh on the pairing dialog could not be carried out", ShowAsItStands);
            };

            refuse.Click += delegate
            {
                WindowHandlers.Run("Refuse on the pairing dialog could not be carried out", Refuse);
            };
            approve.Click += delegate
            {
                WindowHandlers.Run("Approve on the pairing dialog could not be carried out", Approve);
            };

            buttons.Controls.Add(refuse);
            buttons.Controls.Add(approve);
            footer.Controls.Add(buttons, 1, 0);

            // One column, in reading order. It used to be three docked bands with the list
            // filling what the other two left, which is how the list came to be the part that
            // disappeared: the bands grow with the font and the client area did not.
            layout.Controls.Add(_profiles);
            layout.Controls.Add(_lifetime);
            layout.Controls.Add(footer);

            // Filled rather than dropped in at the origin. A form lays a child out against its
            // display rectangle only when the child is docked, so one that is not sits at the
            // client area's own corner and the twelve pixels above are spent on nothing: the
            // title and the list touched the window's left edge.
            layout.Dock = DockStyle.Fill;
            Controls.Add(layout);

            // Every way a dialog gets dismissed without being read leads to a refusal, and none of
            // them leads to a certificate. `RefuseByKey` answers the keys, and these two draw Refuse
            // as the button they reach; the close box and everything else that reaches `Close`
            // without a button are `OnFormClosing` below.
            CancelButton = refuse;
            AcceptButton = refuse;

            Settle();

            _clock = new Timer { Interval = 1000 };
            _clock.Tick += (sender, e) =>
                WindowHandlers.Run("the pairing dialog's countdown could not be moved on", () => OnTick(sender, e));
            _clock.Start();
        }

        /// <summary>
        /// Shows each row as this request has it, in place: its cells, the tip of its state, the
        /// text its box is read out by, and the highlight where its state changed. A row's check is
        /// left as it is; the table is laid again by the caller.
        /// </summary>
        /// <remarks>
        /// **In place, row by row**, since the rows are the profiles asked for in the order asked
        /// and neither changes while the dialog stands: so showing them leaves every check, the
        /// focus and the place the table is scrolled to as they stood. A highlight is never taken
        /// away, since a row that changed once after the profile was first shown keeps having
        /// changed.
        /// </remarks>
        private void ShowRows(PairingRequest request)
        {
            var colors = ChangedColors(SystemInformation.HighContrast);
            foreach (var profile in request.Profiles)
            {
                _profiles.Show(profile, Cells(request, profile), new[] { null, StateTip(request, profile), null }, Spoken(request, profile));
                var change = ChangeTip(request, profile);
                if (change != null)
                {
                    _profiles.Mark(profile, change, colors.Back, colors.Fore);
                }
            }
        }

        /// <summary>
        /// Lays the table again after a Refresh, its view grown with rows grown taller than it, up
        /// to the most the view shows, and the dialog grown downward by as much, kept inside the
        /// working area of its screen.
        /// </summary>
        /// <remarks>
        /// **Worked out here, where the rows change**, and never in answer to a size or a layout.
        /// **The view grows and never shrinks**, `ProfileTable.Lay`: rows grown taller bring
        /// their lines into sight, up to the most the view shows, where a line cut in half at its
        /// bottom, at the top of the rows, shows that the table scrolls; rows grown shorter leave
        /// the view's height and an empty room under the last row, so that what stands below the
        /// table moves down with a growth and never back up. **The dialog grows downward**, its top
        /// edge where it stood, what stands below the table moving down by the growth, and where
        /// its bottom would leave the working area it moves up by what is missing, never above the
        /// working area's top.
        /// </remarks>
        private void Grow()
        {
            _profiles.Lay(false);
            PerformLayout();
            var area = Screen.FromControl(this).WorkingArea;
            if (Bottom > area.Bottom)
            {
                Top = Math.Max(area.Top, area.Bottom - Height);
            }
        }

        /// <summary>
        /// Reads the database again and compares it with what the dialog shows, unless the dialog
        /// is answered or KeePass is part way through work of its own; asked on each tick of the
        /// countdown.
        /// </summary>
        /// <remarks>
        /// **A reading that shows the same is held without a word**: the dialog shows what it
        /// showed, and its approval rests on the latest reading, so that a removal of a name already
        /// shown as removed makes no approval stale. **A reading that shows otherwise waits for
        /// Refresh**, and Approve with it. **A reading that fails** disables Approve and Refresh
        /// until a tick reads again. **A reading that finds the store not served ends the dialog**,
        /// answered with the store's word as a reading before a dialog is, since no later tick of
        /// a store holding the gate back, several authorities standing or the authority retired
        /// reads a store that serves: the dialog would stand disabled until its time ran out.
        /// A lock or a close of the database takes the dialog away, `PairingApprovalPrompt.Locked`.
        ///
        /// **Not while KeePass is busy**: a merge pumps between one group and the next, and a tree
        /// read there is part way between two databases. KeePass raises no event a dialog could
        /// rely on for a change of its tree, an import none at all, so the dialog asks rather than
        /// listens.
        /// </remarks>
        internal void Watch()
        {
            if (_decided || _stopped || _busy())
            {
                return;
            }

            var content = Reading();
            if (Ended(content))
            {
                return;
            }

            _unreadable = !content.Served;
            if (content.Served)
            {
                var next = _request.Refreshed(content);
                if (!next.Refreshes)
                {
                    _request = next;
                    _pending = null;
                }
                else
                {
                    _pending = content;
                    _again = false;
                }
            }

            Settle();
        }

        /// <summary>
        /// Reads the store as the request reads it; where the reading throws, the database is
        /// unreadable for the rest of the dialog and no further reading is made, and what was
        /// thrown goes on to the window's handler, which counts it once.
        /// </summary>
        /// <remarks>
        /// **No catch here**: the tick and the press already run under the handler that counts
        /// what a window event throws, `WindowHandlers.Run`, and catching it here as well would be a
        /// further place that catches everything. What this adds is that the dialog is left
        /// refusing nothing it cannot read: Approve and Refresh disabled, the line stating that the
        /// database cannot be read, Refuse enabled until the dialog ends.
        /// </remarks>
        private AskedProfiles Reading()
        {
            var read = false;
            try
            {
                var content = _request.Read();
                read = true;
                return content;
            }
            finally
            {
                if (!read)
                {
                    _stopped = true;
                    _unreadable = true;
                    _pending = null;
                    Settle();
                }
            }
        }

        /// <summary>
        /// Ends the dialog with the store's word where a reading finds the store read and no
        /// longer serving, `AskedProfiles.Ends`, and answers whether it did; a reading that failed
        /// does not end it.
        /// </summary>
        private bool Ended(AskedProfiles content)
        {
            if (!content.Ends)
            {
                return false;
            }

            _noLongerServed = PairingApproval.NoLongerServed(content);
            Finish(PairingDecision.Unserved);
            return true;
        }

        /// <summary>
        /// Shows the database as it stands, exactly as Refresh does: read again, or as the last
        /// tick read it while KeePass is part way through work of its own. Nothing while no change
        /// waits to be shown, as Refresh is then disabled.
        /// </summary>
        /// <remarks>
        /// **A row the Refresh makes Approve do more for opens unchecked**,
        /// `PairingRequest.Unchecks`, as on a dialog shown again, so that a single Approve grants
        /// no name in a state the operator did not approve it in, and every other row keeps its
        /// check. **The first row the Refresh changed is brought into view**, since the rows it
        /// changed can be out of sight, and **every key press is held back for a moment**,
        /// <see cref="Hold"/>, since the press that showed the change could press again.
        /// </remarks>
        public void ShowAsItStands()
        {
            if (_decided || _stopped || _pending == null || _unreadable)
            {
                return;
            }

            var content = _busy() ? _pending : Reading();
            _pending = null;
            if (Ended(content))
            {
                return;
            }

            _unreadable = !content.Served;
            if (content.Served)
            {
                var before = _request;
                _request = _request.Refreshed(content);
                foreach (var profile in _request.Profiles.Where(_request.Unchecks))
                {
                    _profiles.Check(profile, false);
                }

                ShowRows(_request);
                Grow();
                var moved = _request.Profiles.FirstOrDefault(profile => !Cells(before, profile).SequenceEqual(Cells(_request, profile)));
                if (moved != null)
                {
                    _profiles.Reveal(moved);
                }
            }

            Hold();
            Settle();
        }

        /// <summary>
        /// Enables Refresh exactly while a change waits to be shown, Approve while none does and
        /// the database reads, once its moment held back has passed, and states either in the
        /// words beside Refresh and in its tooltip. Takes no focus from another window.
        /// </summary>
        /// <remarks>
        /// **A control about to be disabled while it holds the focus hands the focus to the view
        /// the table's rows scroll in**, `ProfileTable.TakeFocus`, and every key press is held
        /// back for a moment, <see cref="Hold"/>: disabling a focused control moves the focus to
        /// the next control, which after Refresh is Refuse, so that a second Space would refuse
        /// and spend the invitation, and after Approve is a row, whose check a second Space would
        /// toggle. On the view, Space checks nothing.
        /// </remarks>
        private void Settle()
        {
            var refreshes = _pending != null && !_unreadable;
            var approves = _armed && _pending == null && !_unreadable;
            if (Losing(_refresh, refreshes) || Losing(_approve, approves))
            {
                if (!_profiles.TakeFocus())
                {
                    _lifetime.SelectNextControl(null, true, true, true, false);
                }

                Hold();
            }

            if (refreshes)
            {
                Pointing(false);
            }

            _refresh.Enabled = refreshes;
            _approve.Enabled = approves;
            Tip(_refresh, refreshes ? RefreshShowsTip : RefreshTip);
            ShowStatus(ChangeLine());
        }

        /// <summary>
        /// Tells the dialog whether the pointer stands on Refresh while Refresh is disabled: the
        /// tooltip of Refresh comes up once the pointer has rested there for the tooltips' delay,
        /// and goes as the pointer leaves Refresh or Refresh is enabled.
        /// </summary>
        /// <remarks>
        /// **A disabled control is told of no pointer**, so that left to itself the tooltip of a
        /// disabled Refresh would never come up, and "Refresh (F5)" would stand on the screen
        /// nowhere while nothing waits. The pointer there is the line's, which shows the words the
        /// tooltip of Refresh holds, the words UI automation reads as the help of Refresh.
        /// **Shown on the dialog, under the latest move**: a tooltip shown on a control of a window
        /// that is not active is not shown, and the dialog comes up without being activated. The
        /// place is the move's, read off the message the line was told of, and never the system's
        /// pointer, which stands wherever it stands by the time the tooltip comes up.
        /// </remarks>
        private void Pointing(bool onRefresh)
        {
            onRefresh &= !_refresh.Enabled;
            if (onRefresh == _onRefresh)
            {
                return;
            }

            _onRefresh = onRefresh;
            _resting.Stop();
            if (onRefresh)
            {
                _resting.Start();
            }
            else if (_refreshTipShown)
            {
                _refreshTipShown = false;
                _tips.Hide(this);
            }
        }

        /// <summary>
        /// Shows the tooltip of Refresh under the pointer while the pointer stands on a disabled
        /// Refresh, <see cref="Pointing"/>.
        /// </summary>
        private void ShowRefreshTip()
        {
            _resting.Stop();
            if (!_onRefresh || _refresh.Enabled)
            {
                return;
            }

            // Under the latest move, as a tooltip comes up under the pointer, in the dialog's own
            // window's coordinates.
            var under = new Point(_moved.X - Left, _moved.Y - Top + (SystemInformation.CursorSize.Height * 2 / 3));
            _tips.Show(_tips.GetToolTip(_refresh), this, under, _tips.AutoPopDelay);
            _refreshTipShown = true;
        }

        /// <summary>
        /// States the words beside Refresh, whole in their tooltip where the line cuts them, or
        /// hides them where they state nothing, since an empty text is read by UI automation by
        /// the name of the text before it. Their place keeps its size whatever they state.
        /// </summary>
        private void ShowStatus(string words)
        {
            _change.Text = words;
            _change.Visible = words.Length > 0;
            Tip(_change, _change.Cut ? words : null);
        }

        /// <summary>Gives a control of the dialog this tip, folded, or none for null.</summary>
        private void Tip(Control control, string text)
        {
            var tip = Tooltips.Wrapped(text);
            if (!string.Equals(_tips.GetToolTip(control), tip ?? string.Empty, StringComparison.Ordinal))
            {
                _tips.SetToolTip(control, tip);
            }
        }

        /// <summary>Whether a control holds the focus and is about to be disabled.</summary>
        private static bool Losing(Control control, bool enabled)
        {
            return control.Focused && !enabled;
        }

        /// <summary>
        /// The words beside Refresh as the dialog stands: once a reading threw, while a reading
        /// fails, while a change waits to be shown, on a dialog shown again until a change waits
        /// to be shown, and empty otherwise.
        /// </summary>
        private string ChangeLine()
        {
            if (_stopped)
            {
                return ReadingStopped;
            }

            if (_unreadable)
            {
                return CannotRead;
            }

            if (_pending != null)
            {
                return DatabaseChanged;
            }

            return _again ? StateChanged : string.Empty;
        }

        /// <summary>
        /// How tall a button of text is in the dialog's font, as Refuse and Approve are: the side
        /// of Refresh, a square, so that Refresh is as tall as the answers at every scaling, its
        /// picture in its middle, and as large with a picture or without one.
        /// </summary>
        /// <remarks>
        /// **Measured on a button of text built as Refuse is**: a button holding a picture and no
        /// text answers the size it has, 75 by 23 as it is made, whatever its picture and its
        /// font, which would leave Refresh as wide as a button of text and, at a larger font,
        /// shorter than the answers.
        /// </remarks>
        private int TextButtonHeight()
        {
            using (var measured = new Button
            {
                Text = "&" + GateTab.RefuseButtonText,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = Font
            })
            {
                return measured.GetPreferredSize(Size.Empty).Height;
            }
        }

        /// <summary>
        /// How wide a line of this dialog's text is allowed to be, measured in this font.
        /// </summary>
        /// <remarks>
        /// Fifty-six characters is the measurement, because that is a decision about reading
        /// rather than about pixels: it holds at any font size and any display scaling, where a
        /// number of pixels holds at one. The widest character the font has is what it is measured
        /// in, so this is generous rather than tight, which is the safe direction for a dialog
        /// that must not clip.
        ///
        /// **Reading alone would ask for seventy, and the countdown is what rules it out.**
        /// That line and the two buttons share the last row, and at seventy they sit a hundred
        /// and fifty pixels apart with nothing between them: a dialog wide enough that the two
        /// things on its final row read as belonging to different windows. Fifty-six leaves
        /// about thirty, which is a gap rather than a gulf. The labels above wrap a line or so
        /// more for it, which is the trade and is the cheaper half: they are read once and the
        /// row is what gets acted on.
        /// </remarks>
        internal static int Wrap(Font font)
        {
            return TextRenderer.MeasureText(new string('M', 56), font).Width;
        }

        /// <summary>The most rows of a single line the table shows at once, past which it scrolls.</summary>
        internal const int MostRows = 10;

        /// <summary>
        /// Lays the table again once it has a window, before the dialog is centered and shown, as
        /// the authority chooser sizes its lists and for its reason: the boxes are measured there
        /// in the style the window draws them in. The dialog's size is decided here, from what it
        /// shows as it comes up, and the words beside Refresh are measured against the room the
        /// line leaves them.
        /// </summary>
        /// <remarks>
        /// **The boxes' column as wide as the box with its margins or its heading**, whichever is
        /// wider, and **the state's as wide as its longest text**, both held whole whatever is
        /// beside them, the state being the one thing on screen telling the operator that approving
        /// adds the name or gives it back. The names and the clients holding them share what is
        /// left, and a line cut there ends in an ellipsis and shows its whole cell in a tooltip,
        /// `ProfileTable`.
        /// </remarks>
        protected override void OnLoad(EventArgs e)
        {
            WindowHandlers.Run("the pairing dialog could not be fitted to its table", () =>
            {
                _profiles.Lay(true);
                _change.Parent.PerformLayout();
                ShowStatus(ChangeLine());
                base.OnLoad(e);
            });
        }

        /// <summary>
        /// Approves it, exactly as the button does. Public because a button press needs a window on
        /// the screen and a test of what the button means does not.
        /// </summary>
        /// <remarks>
        /// **Nothing while a change of the database waits to be shown, or the database cannot be
        /// read**, as the button is then disabled: an approval of what the dialog shows would rest
        /// on a database that no longer holds it.
        /// </remarks>
        public void Approve()
        {
            if (_pending != null || _unreadable)
            {
                return;
            }

            Finish(PairingDecision.Approved);
        }

        /// <summary>Refuses it, exactly as the button does.</summary>
        public void Refuse()
        {
            Finish(PairingDecision.Refused);
        }

        /// <summary>Enter and Escape refuse, as pressing Refuse does.</summary>
        protected override void RefuseByKey()
        {
            Refuse();
        }

        internal override bool Withdraw(bool databaseClosed)
        {
            var unanswered = !_decided;
            if (unanswered)
            {
                _closedWithDatabase = databaseClosed;
            }

            Finish(PairingDecision.Locked);
            return unanswered;
        }

        /// <summary>
        /// What the operator decided. A dialog dismissed rather than answered has refused, and one
        /// nobody touches at all has timed out.
        /// </summary>
        public PairingApproval Answer()
        {
            switch (_decision)
            {
                case PairingDecision.Approved:
                    return _approval;

                case PairingDecision.Refused:
                    return PairingApproval.Refused();

                case PairingDecision.Locked:
                    return _closedWithDatabase ? PairingApproval.Closed() : PairingApproval.Locked();

                case PairingDecision.Unserved:
                    return _noLongerServed;

                default:
                    return PairingApproval.TimedOut();
            }
        }

        /// <summary>The lines of the request, so the wording can be tested without a screen.</summary>
        public static IReadOnlyList<Fact> Facts(PairingRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var facts = new List<Fact>();

            // The name first, because that is what the operator knows this database as. The
            // fingerprint stays under it: it is what actually identifies the database, and two
            // databases can carry the same name.
            if (!string.IsNullOrEmpty(request.DatabaseName))
            {
                facts.Add(new Fact("Database", request.DatabaseName));
                facts.Add(new Fact("Fingerprint", CertificateDigest.Shorten(request.DatabaseId)));
            }
            else
            {
                facts.Add(new Fact("Database", CertificateDigest.Shorten(request.DatabaseId)));
            }

            facts.Add(new Fact("Asking to be called", request.Name));

            // Captioned as an identifier rather than as the invitation, because the clients page
            // shows the same identifier beside the code and this is what it is checked against. A
            // row saying "Invitation" reads as though it were the code, which is the one thing this
            // dialog does not show.
            facts.Add(new Fact(GateTab.InvitationIdFactName, request.InvitationId));

            if (!string.IsNullOrEmpty(request.Peer))
            {
                facts.Add(new Fact(GateTab.PeerFactName, request.Peer));
            }

            return facts;
        }

        private Label Cell(string name, string text, bool value)
        {
            return new TextLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Font = value ? _valueFont : Font,
                MaximumSize = new Size(_wrap * 2 / 3, 0),
                Margin = value ? new Padding(0) : new Padding(0, 0, LogicalToDeviceUnits(Spacing.Gap), 0)
            };
        }

        /// <summary>
        /// Starts the moment Approve is held back for on a dialog shown again, from when the dialog
        /// comes up rather than from when it was built.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            WindowHandlers.Run("Approve on the pairing dialog could not be held back", () =>
            {
                base.OnShown(e);
                if (_held)
                {
                    _arming.Start();
                }
            });
        }

        /// <summary>
        /// Holds Approve and every key press back for <see cref="ApproveHeldBack"/> from now: after
        /// a Refresh, and after the focus was moved off a control being disabled.
        /// </summary>
        /// <remarks>
        /// **A press made for the control that was there lands on what is there now**: a Space
        /// that pressed Refresh, pressed again, would refuse or toggle a row where the focus went,
        /// and a press on Approve as it is enabled again would approve rows the operator has not
        /// yet seen change. Held back for this moment, such a press does nothing.
        /// </remarks>
        private void Hold()
        {
            _held = true;
            _armed = false;
            _arming.Stop();
            _arming.Start();
        }

        /// <summary>Ends the moment held back: enables Approve, and takes no focus.</summary>
        private void Arm()
        {
            _arming.Stop();
            _armed = true;
            _held = false;
            Settle();
        }

        /// <summary>
        /// Every key pressed on the dialog goes nowhere while a press is held back,
        /// <see cref="Hold"/>, but Enter and Escape: asked here before the key reaches the window
        /// it is typed on, so that no letter, with Alt or without, presses a button by its
        /// mnemonic, and neither Tab nor an arrow moves the focus.
        /// </summary>
        /// <remarks>
        /// **Enter and Escape refuse, as they do wherever the focus is**, `ApprovalDialog`: a
        /// refusal grants nothing, and either key refuses whatever the dialog shows, so that a
        /// press of either means on the dialog now what it meant on the dialog before. A list
        /// opened in that moment by a click, which takes either key for itself, takes neither
        /// until the moment has passed, <see cref="ProcessKeyPreview"/>.
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var message = msg;
            var answered = WindowHandlers.Run(
                "a key held back on the pairing dialog could not be handled",
                () => (_held && !Refusing(keyData)) || Refreshing(keyData) || base.ProcessCmdKey(ref message, keyData),
                true);
            msg = message;
            return answered;
        }

        /// <summary>
        /// F5, wherever the focus is, does what Refresh does, which is nothing while no change
        /// waits to be shown: Refresh has no text and so no mnemonic. Answers whether the key was
        /// F5.
        /// </summary>
        private bool Refreshing(Keys keyData)
        {
            if (keyData != Keys.F5)
            {
                return false;
            }

            ShowAsItStands();
            return true;
        }

        /// <summary>
        /// A letter the dialog would answer as a mnemonic, typed on a control that leaves letters
        /// to the dialog, goes nowhere while a press is held back: the letter of Refuse refused
        /// there, and the letter of Refresh pressed Refresh, typed on a focused button or with Alt.
        /// </summary>
        protected override bool ProcessDialogChar(char charCode)
        {
            return WindowHandlers.Run(
                "a letter held back on the pairing dialog could not be handled",
                () => _held || base.ProcessDialogChar(charCode),
                true);
        }

        /// <summary>
        /// Every key that reaches the window it is typed on goes nowhere while a press is held
        /// back, <see cref="Hold"/>: Space on a button or a box among them.
        /// </summary>
        protected override bool ProcessKeyPreview(ref Message m)
        {
            if (_held && KeyMessages.Contains(m.Msg))
            {
                return true;
            }

            return base.ProcessKeyPreview(ref m);
        }

        /// <summary>
        /// Whether a key is Enter or Escape, with Shift or without it and with neither Alt nor
        /// Control, which refuse as `ApprovalDialog` answers them.
        /// </summary>
        private static bool Refusing(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return (keyData & (Keys.Alt | Keys.Control)) == Keys.None && (key == Keys.Enter || key == Keys.Escape);
        }

        /// <summary>
        /// The messages of a key reaching a window: a key down and up, and the letter it types,
        /// each with Alt or without it.
        /// </summary>
        private static readonly int[] KeyMessages = { 0x0100, 0x0101, 0x0102, 0x0104, 0x0105, 0x0106 };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_clock != null)
                {
                    _clock.Stop();
                    _clock.Dispose();
                }

                if (_arming != null)
                {
                    _arming.Stop();
                    _arming.Dispose();
                }

                if (_resting != null)
                {
                    _resting.Stop();
                    _resting.Dispose();
                }

                if (_valueFont != null)
                {
                    _valueFont.Dispose();
                }

                if (_titleFont != null)
                {
                    _titleFont.Dispose();
                }

                _tips.Dispose();
            }

            base.Dispose(disposing);

            // Once the button that drew it is gone, so that no paint reaches a picture let go.
            if (disposing && _picture != null)
            {
                _picture.Dispose();
            }
        }

        private static string Remaining(TimeSpan left)
        {
            var seconds = (int)Math.Max(0, Math.Ceiling(left.TotalSeconds));

            return string.Format(
                CultureInfo.InvariantCulture, GateTab.ApprovalCountdownFormat, seconds);
        }

        /// <summary>
        /// What is still checked, by name, which is what the operator approved.
        /// </summary>
        /// <remarks>
        /// The names are each row's profile, never its text read out, and the table is built from
        /// the request after the handler has collapsed anything repeated in it. Two identical rows
        /// would be two rows the operator has to clear to refuse a profile, and clearing a single
        /// row of the two would grant it.
        /// </remarks>
        private string[] Checked()
        {
            return _profiles.Checked.ToArray();
        }

        private void OnTick(object sender, EventArgs e)
        {
            var left = _allowed - _elapsed.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                Finish(PairingDecision.TimedOut);
                return;
            }

            _countdown.Text = Remaining(left);
            Watch();
        }

        /// <summary>
        /// A dialog dismissed without an answer has refused, whichever way it was dismissed.
        /// </summary>
        /// <remarks>
        /// The close box, Alt and F4, the window menu and the taskbar all reach `Close` without
        /// pressing either button, so none of them runs `Refuse`, and what a dialog that was never
        /// answered holds is a timeout. That answer is wrong in both directions at once: the
        /// operator who dismissed the request believes it was turned down, while the invitee is
        /// told nobody was at the screen and keeps an invitation it can present again, raising this
        /// dialog as often as it likes until the token runs out. Somebody was at the screen, and
        /// the default here is denial, so a dismissal is a refusal and spends the invitation.
        ///
        /// **`CloseReason` cannot tell those apart**, which is the trap in doing this the obvious
        /// way: `Form.Close` reports `UserClosing` whoever called it, so the countdown's own close
        /// arrives looking exactly like a press on the title bar and would spend the invitation of
        /// an operator who was never there. What separates them is whether an answer has already
        /// been given, which is what `Decide` records.
        /// </remarks>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            WindowHandlers.Run("the pairing dialog's close could not be handled", () =>
            {
                base.OnFormClosing(e);

                if (e.Cancel)
                {
                    // The dialog is staying open, so nothing has been decided and the operator can
                    // still answer it.
                    return;
                }

                Decide(PairingDecision.Refused);
            });
        }

        /// <summary>
        /// Records an answer, unless one has already been recorded.
        /// </summary>
        /// <remarks>
        /// The first answer wins, because the ways this dialog can end overlap. Every press ends in
        /// a close, which arrives at `OnFormClosing` immediately afterwards, and the countdown can
        /// expire while a button is going down. A later answer overwriting an earlier one would let
        /// a close take back the press that caused it.
        ///
        /// Stopping the clock belongs here for the same reason: `KillTimer` does not withdraw a
        /// tick already sitting in the queue, so a decision has to survive one arriving after it.
        /// </remarks>
        private void Decide(PairingDecision decision)
        {
            if (_decided)
            {
                return;
            }

            _decided = true;
            _decision = decision;
            if (decision == PairingDecision.Approved)
            {
                // Read at the press rather than when the answer is asked for, so that the approval
                // is of the checks, the lifetime and the reading that the press found.
                _approval = _request.Approve(Checked(), _lifetime.Chosen);
            }

            _clock.Stop();
        }

        private void Finish(PairingDecision decision)
        {
            Decide(decision);

            // Read back rather than taken from the argument, so that a press arriving after the
            // clock has answered closes on the answer that was kept rather than on its own.
            DialogResult = _decision == PairingDecision.Approved ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// The words beside Refresh: a single line against Refresh, the far end of the line in
        /// either direction, cut with an ellipsis where the line leaves too little room, and read
        /// left to right in either direction.
        /// </summary>
        /// <remarks>
        /// **Left to right in a right to left window**, as the plugin's tooltips are,
        /// `Tooltips.Wrapped`: the words are English, and drawn in a right to left reading the full
        /// stop of a sentence stands at its start, ".The database changed". A left-to-right mark at
        /// each end of the line keeps the line in order and draws nothing.
        /// </remarks>
        private sealed class StatusLine : Label
        {
            /// <summary>
            /// How the line is both drawn and measured, which has to be a single answer.
            /// </summary>
            private const TextFormatFlags Shape = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            public StatusLine()
            {
                UseMnemonic = false;
                AutoSize = false;
            }

            /// <summary>Whether the words are wider than their place.</summary>
            public bool Cut
            {
                get { return TextRenderer.MeasureText(Marked, Font, Size.Empty, Shape).Width > ClientSize.Width; }
            }

            /// <summary>The words as drawn and measured, between two left-to-right marks.</summary>
            private string Marked
            {
                get { return Tooltips.LeftToRightMark + Text + Tooltips.LeftToRightMark; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                WindowHandlers.Run("the words beside Refresh could not be drawn", () =>
                {
                    // Not the base implementation, which wraps words; the background is painted
                    // by `OnPaintBackground`.
                    var toward = RightToLeft == RightToLeft.Yes ? TextFormatFlags.Left | TextFormatFlags.RightToLeft : TextFormatFlags.Right;
                    TextRenderer.DrawText(
                        e.Graphics, Marked, Font, ClientRectangle, ForeColor,
                        Shape | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | toward);
                });
            }
        }

        /// <summary>
        /// Refresh: a picture and no text, and the focus drawn round the picture wherever the
        /// focus is, whatever turned the focus cues on, as the table draws its own, since a button
        /// with no text shows no other sign of holding the focus.
        /// </summary>
        private sealed class RefreshButton : Button
        {
            protected override bool ShowFocusCues
            {
                get { return true; }
            }
        }
    }
}
