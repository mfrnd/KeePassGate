using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Gate;

namespace KeePassGate.Pairing
{
    /// <summary>What the operator was asked.</summary>
    public sealed class PairingRequest
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private readonly string _databaseId;
        private readonly string _databaseName;
        private readonly string _invitationId;
        private readonly string _name;
        private readonly IReadOnlyList<string> _profiles;
        private readonly IReadOnlyList<string> _known;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> _returning;
        private readonly string _peer;
        private readonly TimeSpan? _lifetime;
        private readonly HashSet<string> _removed;
        private readonly PairingApproval _earlier;
        private readonly AskedProfiles _before;
        private readonly ScreenPlace _place;

        public PairingRequest(
            string databaseId,
            string invitationId,
            string name,
            IReadOnlyList<string> profiles,
            string peer)
            : this(databaseId, null, invitationId, name, profiles, null, peer)
        {
        }

        /// <summary>The same, with everything the dialog shows beside what was asked.</summary>
        /// <param name="databaseName">
        /// What KeePass calls this database. A dialog naming it only by its fingerprint asks the
        /// operator to recognize sixteen hexadecimal characters.
        /// </param>
        /// <param name="known">
        /// The profiles this database already names. What is asked for and is not among them is
        /// new, and approving it is what creates it, unless it is shown as removed.
        /// </param>
        /// <param name="returning">
        /// For each profile asked for that the database does not name, the admitted clients whose
        /// records still hold it. Approving such a profile puts the name back on the list, which
        /// gives it back to every one of them. Null or absent when nobody does.
        /// </param>
        /// <param name="lifetime">
        /// How long the client asked its certificate to last, already read off the wire and known
        /// to be one `Lifetime` allows, or null when it asked for nothing.
        /// </param>
        /// <param name="removed">
        /// The profiles asked for off the list that are shown as removed whether or not a client
        /// holds them: each whose removal the dialog's reading holds, and each a write of the same
        /// request found a removal of that the dialog before did not hold. Null or absent for none.
        /// </param>
        /// <param name="earlier">
        /// What the operator approved on the dialog before, for the dialog the request puts to the
        /// operator again once a write found that approval stale, which opens with it; null for a
        /// first dialog. An approval, and never from anything the client sent.
        /// </param>
        /// <param name="before">
        /// What the dialog before was built from, which gives how it marked each name; null for a
        /// first dialog.
        /// </param>
        /// <param name="place">
        /// The place on the screen the request keeps between its dialogs, or null for a request
        /// that keeps none.
        /// </param>
        public PairingRequest(
            string databaseId,
            string databaseName,
            string invitationId,
            string name,
            IReadOnlyList<string> profiles,
            IReadOnlyList<string> known,
            string peer,
            IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> returning = null,
            TimeSpan? lifetime = null,
            IReadOnlyCollection<string> removed = null,
            PairingApproval earlier = null,
            AskedProfiles before = null,
            ScreenPlace place = null)
        {
            _earlier = earlier;
            _before = before;
            _place = place;
            _databaseId = databaseId ?? string.Empty;
            _databaseName = databaseName ?? string.Empty;
            _invitationId = invitationId ?? string.Empty;
            _name = name ?? string.Empty;
            _profiles = profiles ?? Nothing;
            _known = known ?? Nothing;
            _returning = returning ?? new Dictionary<string, IReadOnlyList<AdmissionRecord>>();
            _peer = peer ?? string.Empty;
            _lifetime = lifetime;
            _removed = new HashSet<string>(removed ?? Nothing, StringComparer.Ordinal);
        }

        /// <summary>Which database is about to admit somebody, by its authority's digest.</summary>
        public string DatabaseId
        {
            get { return _databaseId; }
        }

        /// <summary>What KeePass calls it, or empty when it has nothing to be called.</summary>
        public string DatabaseName
        {
            get { return _databaseName; }
        }

        public string InvitationId
        {
            get { return _invitationId; }
        }

        /// <summary>What the caller wants to be called. Already checked to be a usable name.</summary>
        public string Name
        {
            get { return _name; }
        }

        /// <summary>What it is asking for. The operator confirms this rather than accepting it.</summary>
        public IReadOnlyList<string> Profiles
        {
            get { return _profiles; }
        }

        public string Peer
        {
            get { return _peer; }
        }

        /// <summary>
        /// How long the client asked its certificate to last, or null when it asked for nothing.
        /// The operator picks the lifetime, and this is what a first dialog offers first.
        /// </summary>
        public TimeSpan? Lifetime
        {
            get { return _lifetime; }
        }

        /// <summary>
        /// Whether the dialog is shown again, because a write found the approval of the dialog
        /// before stale; false for a first dialog. Never from anything the client sent.
        /// </summary>
        public bool Again
        {
            get { return _earlier != null; }
        }

        /// <summary>
        /// The lifetime the dialog opens on: the lifetime the operator picked on the dialog before,
        /// for a dialog shown again, and otherwise what the client asked for.
        /// </summary>
        public TimeSpan? Picked
        {
            get { return _earlier == null ? _lifetime : _earlier.Lifetime; }
        }

        /// <summary>
        /// Whether a profile's row opens checked: every row of a first dialog, and on a dialog
        /// shown again a row the operator left checked before and marked as it was marked then.
        /// </summary>
        /// <remarks>
        /// **A row whose mark changed opens unchecked**, whatever the operator had there: on the
        /// list before and removed now, new before and removed now, and so on. What the operator
        /// approved was the name as it was marked, so a single Approve on the dialog shown again
        /// grants no name in a state the operator did not approve it in, as putting a name back
        /// for every rule and client still holding it where the operator approved a name on the
        /// list. Unchecked as well where nothing tells how the dialog before marked it.
        /// </remarks>
        public bool Checks(string profile)
        {
            return _earlier == null
                   || (_earlier.Profiles.Contains(profile, StringComparer.Ordinal) && !Changed(profile));
        }

        /// <summary>
        /// Whether a profile's state changed since the dialog before: on a dialog shown again, a
        /// profile marked otherwise than the dialog before marked it, or where nothing tells how
        /// that dialog marked it; never on a first dialog.
        /// </summary>
        /// <remarks>
        /// **Such a row opens unchecked**, <see cref="Checks"/>, as a row the operator unchecked
        /// does, so the dialog shows the change apart from the check: the row is highlighted, and
        /// its tooltip and the text read out as the row state the change, `PAIRING.md` §311.
        /// </remarks>
        public bool Changed(string profile)
        {
            return _earlier != null && (_before == null || _before.MarkOf(profile) != MarkOf(profile));
        }

        /// <summary>How the dialog marks a profile being asked for.</summary>
        public ProfileMark MarkOf(string profile)
        {
            return IsRemoved(profile) ? ProfileMark.Removed : IsNew(profile) ? ProfileMark.New : ProfileMark.Listed;
        }

        /// <summary>
        /// The place on the screen the request keeps between its dialogs, or null for a request
        /// that keeps none: what the screen keeps for it from its first dialog on.
        /// </summary>
        public ScreenPlace Place
        {
            get { return _place; }
        }

        /// <summary>
        /// Whether a profile being asked for is a name this database does not name now and that is
        /// not shown as removed, <see cref="IsRemoved"/>.
        /// </summary>
        /// <remarks>
        /// Shown to the operator, because approving one of these does two things rather than one:
        /// it admits the client, and it puts a name on the list that every rule can then grant to.
        /// A dialog that showed a new name exactly like an existing one would be asking somebody to
        /// notice a spelling. **It is not "never named" in every case**: a name whose removal only
        /// a deletion record tells of, and that the file names nowhere else, reads as new on the
        /// first dialog of a request, until a write of that request finds the removal,
        /// <see cref="IsRemoved"/>. Any rule already naming a new name applies at once, as the
        /// dialog states of every new name.
        /// </remarks>
        public bool IsNew(string profile)
        {
            if (string.IsNullOrEmpty(profile) || IsRemoved(profile))
            {
                return false;
            }

            foreach (var known in _known)
            {
                if (string.Equals(known, profile, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The clients that regain a profile if it is approved, because it was removed and their
        /// records still hold it. Empty for every other profile.
        /// </summary>
        /// <remarks>
        /// Not new, and shown differently: approving it does not create a name, it restores one,
        /// and every client approved for it before gets it back along with every rule still
        /// naming it. The operator approving one client is otherwise approving all of those
        /// without being told.
        /// </remarks>
        public IReadOnlyList<AdmissionRecord> Returning(string profile)
        {
            IReadOnlyList<AdmissionRecord> holding;
            return profile != null && _returning.TryGetValue(profile, out holding) && holding != null
                ? holding
                : Array.Empty<AdmissionRecord>();
        }

        /// <summary>
        /// Whether a profile being asked for is shown as removed: a name off the list that clients
        /// still hold from before, a name whose removal the dialog's reading holds, made in this
        /// copy or brought in by a synchronize, or a name a write of the same request found a
        /// removal of that the dialog before did not hold.
        /// </summary>
        /// <remarks>
        /// **The last is what the write knows and the dialog's reading cannot**: a removal that
        /// only a deletion record tells of, of a name the file does not know otherwise, is tied to
        /// its name by a hash under the store key, which the reading of a connection does not
        /// take, so the dialog would show the name as new again.
        /// </remarks>
        public bool IsRemoved(string profile)
        {
            return Returning(profile).Count > 0 || (profile != null && _removed.Contains(profile));
        }
    }

    /// <summary>
    /// The place a pairing request keeps on the operator's screen from its first dialog until the
    /// request is answered, so that no other request's dialog comes up between its dialogs.
    /// </summary>
    /// <remarks>
    /// **A single request at a time is put to the operator**, and a request whose write finds its
    /// approval stale puts the dialog to the operator again. Between the two the request writes,
    /// and a request waiting for the screen would take it there: its dialog would come up between
    /// the operator's Approve and the dialog shown again, and use up time the request has left. So
    /// the screen, on giving the request its first dialog, keeps its lock for the place as well,
    /// <see cref="Keep"/>, and the request lets it go once it is answered, <see cref="Dispose"/>,
    /// on the thread that took it. A request waiting behind waits within its own wait for the
    /// screen, as behind any dialog.
    ///
    /// **No deadlock can come of it**: the screen's lock is taken on connection threads alone,
    /// always before the writer's lock, and never on KeePass's thread, which is all the write
    /// waits for.
    /// </remarks>
    public sealed class ScreenPlace : IDisposable
    {
        /// <summary>The screen's lock, while this place keeps it, and otherwise null.</summary>
        private object _screen;

        /// <summary>
        /// Keeps this screen's lock for the place, unless the place keeps it already. Called by the
        /// screen with that lock held, on the thread the request runs on.
        /// </summary>
        internal void Keep(object screen)
        {
            if (_screen == null)
            {
                Monitor.Enter(screen);
                _screen = screen;
            }
        }

        /// <summary>Lets the screen go, where this place keeps it.</summary>
        public void Dispose()
        {
            var screen = _screen;
            _screen = null;
            if (screen != null)
            {
                Monitor.Exit(screen);
            }
        }
    }

    public enum PairingDecision
    {
        /// <summary>Approved, with whatever profiles came back rather than whatever was asked for.</summary>
        Approved,

        /// <summary>Refused on purpose. This spends the invitation.</summary>
        Refused,

        /// <summary>Nobody answered. This does not spend the invitation.</summary>
        TimedOut,

        /// <summary>There was nobody to ask, so nothing was decided.</summary>
        Unavailable,

        /// <summary>
        /// KeePass locked while the question was up or waiting to be, which closes the dialog as
        /// unanswered. Nothing is spent, and the client is told the database is locked. A dialog
        /// closed this way was written down as unanswered by the screen that closed it, as it did.
        /// </summary>
        Locked
    }

    /// <summary>What came back from the screen.</summary>
    public sealed class PairingApproval
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private readonly PairingDecision _decision;
        private readonly IReadOnlyList<string> _profiles;
        private readonly TimeSpan _lifetime;
        private readonly string _reason;
        private readonly bool _shown;

        private PairingApproval(
            PairingDecision decision, IReadOnlyList<string> profiles, TimeSpan? lifetime, string reason)
            : this(decision, profiles, lifetime, reason, true)
        {
        }

        private PairingApproval(
            PairingDecision decision, IReadOnlyList<string> profiles, TimeSpan? lifetime, string reason, bool shown)
        {
            _decision = decision;
            _profiles = profiles ?? Nothing;
            _lifetime = lifetime ?? Authority.Lifetime.Unasked;
            _reason = reason ?? string.Empty;
            _shown = shown;
        }

        public PairingDecision Decision
        {
            get { return _decision; }
        }

        /// <summary>
        /// The profiles the operator approved, which is what the admission record gets. Never the
        /// ones that were asked for, unless the operator left them alone.
        /// </summary>
        public IReadOnlyList<string> Profiles
        {
            get { return _profiles; }
        }

        /// <summary>
        /// How long the operator gave the certificate, which is what it is issued for: what the
        /// client asked for if they left it, and a year when nobody chose.
        /// </summary>
        public TimeSpan Lifetime
        {
            get { return _lifetime; }
        }

        public string Reason
        {
            get { return _reason; }
        }

        /// <summary>
        /// Whether the question reached the operator's screen, which it did not when there was no
        /// screen to ask or a lock came while it waited for the screen. The audit writes down only
        /// a question somebody was asked.
        /// </summary>
        public bool Shown
        {
            get { return _shown; }
        }

        /// <summary>The operator's yes, and the profiles the record is to carry.</summary>
        /// <param name="lifetime">How long the certificate lasts, or null for a year.</param>
        public static PairingApproval Approved(IReadOnlyList<string> profiles, TimeSpan? lifetime = null)
        {
            return new PairingApproval(PairingDecision.Approved, profiles, lifetime, "approved");
        }

        public static PairingApproval Refused()
        {
            return new PairingApproval(PairingDecision.Refused, null, null, "refused");
        }

        public static PairingApproval TimedOut()
        {
            return new PairingApproval(PairingDecision.TimedOut, null, null, "nobody answered");
        }

        public static PairingApproval Unavailable(string reason)
        {
            return new PairingApproval(PairingDecision.Unavailable, null, null, reason, false);
        }

        /// <summary>A dialog the lock closed as unanswered.</summary>
        public static PairingApproval Locked()
        {
            return new PairingApproval(PairingDecision.Locked, null, null, LockedReason);
        }

        /// <summary>
        /// A dialog closed as unanswered because the database it is about closed: answered as a
        /// lock's is, since the database is no longer there to write to, and named for the close.
        /// </summary>
        public static PairingApproval Closed()
        {
            return new PairingApproval(PairingDecision.Locked, null, null, ClosedReason);
        }

        /// <summary>
        /// A request that waited for the screen while its database locked or closed, and was never
        /// shown: locked, as a dialog the lock closed is, and asked of nobody.
        /// </summary>
        public static PairingApproval LockedWhileWaiting()
        {
            return new PairingApproval(PairingDecision.Locked, null, null, LockedWhileWaitingReason, false);
        }

        /// <summary>What the log reads of a question a lock took away.</summary>
        internal const string LockedReason = "closed unanswered when KeePass locked";

        /// <summary>What the log reads of a question its database's close took away.</summary>
        internal const string ClosedReason = "closed unanswered when its database closed";

        /// <summary>
        /// What the log reads of a question a lock or a close kept off the screen, which the screen
        /// cannot tell apart: either leaves the database no longer served.
        /// </summary>
        internal const string LockedWhileWaitingReason =
            "never shown, because its database locked or closed while it waited for the screen";
    }

    /// <summary>
    /// Asking the person at the keyboard whether a stranger may have a certificate.
    /// </summary>
    /// <remarks>
    /// One of the two places in this plugin where a request reaches a screen, `IRenewalApproval`
    /// being the other, and both are answered by one prompt so that only one dialog is up at a
    /// time. `DESIGN.md` §119 says why the exception is confined to these: an unattended agent must
    /// not be able to raise a dialog, so every other route refuses rather than asks. These exist to
    /// be asked.
    ///
    /// An implementation must answer within the timeout even when nobody is there, because the
    /// connection thread is waiting on it and the invitation is not spent until it does.
    /// </remarks>
    public interface IPairingApproval
    {
        /// <summary>Puts the request to the operator and answers with what they decided.</summary>
        /// <param name="request">What the operator is asked about.</param>
        /// <param name="timeout">
        /// How long the operator is given to answer a dialog shown at once.
        /// </param>
        /// <param name="left">
        /// How long from now a dialog may stand at most, so that what follows it still ends inside
        /// the pairing budget the request arrived in: <see cref="PairingOptions.DialogLeft"/>.
        /// </param>
        PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left);
    }

    /// <summary>What a plugin with no screen answers: nothing is approved and nothing is spent.</summary>
    public sealed class NoPairingApproval : IPairingApproval
    {
        public static readonly NoPairingApproval Instance = new NoPairingApproval();

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
        {
            return PairingApproval.Unavailable("there is no screen to ask");
        }
    }
}
