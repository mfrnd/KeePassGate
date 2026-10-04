using System;
using System.Collections.Generic;
using KeePassGate.Authority;

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

        public PairingRequest(
            string databaseId,
            string invitationId,
            string name,
            IReadOnlyList<string> profiles,
            string peer)
            : this(databaseId, null, invitationId, name, profiles, null, peer)
        {
        }

        /// <param name="databaseName">
        /// What KeePass calls this database. A dialog naming it only by its fingerprint asks the
        /// operator to recognize sixteen hexadecimal characters.
        /// </param>
        /// <param name="known">
        /// The profiles this database already names. What is asked for and is not among them is
        /// new, and approving it is what creates it, unless a client already holds it.
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
        public PairingRequest(
            string databaseId,
            string databaseName,
            string invitationId,
            string name,
            IReadOnlyList<string> profiles,
            IReadOnlyList<string> known,
            string peer,
            IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> returning = null,
            TimeSpan? lifetime = null)
        {
            _databaseId = databaseId ?? string.Empty;
            _databaseName = databaseName ?? string.Empty;
            _invitationId = invitationId ?? string.Empty;
            _name = name ?? string.Empty;
            _profiles = profiles ?? Nothing;
            _known = known ?? Nothing;
            _returning = returning ?? new Dictionary<string, IReadOnlyList<AdmissionRecord>>();
            _peer = peer ?? string.Empty;
            _lifetime = lifetime;
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
        /// The operator picks the lifetime, and this is what the dialog offers first.
        /// </summary>
        public TimeSpan? Lifetime
        {
            get { return _lifetime; }
        }

        /// <summary>
        /// Whether a profile being asked for is one this database does not name now and no client
        /// holds.
        /// </summary>
        /// <remarks>
        /// Shown to the operator, because approving one of these does two things rather than one:
        /// it admits the client, and it puts a name on the list that every rule can then grant to.
        /// A dialog that showed a new name exactly like an existing one would be asking somebody to
        /// notice a spelling. **It is not "never named"**: a name removed once, whose holders have
        /// all been revoked or have expired since, reads the same here, and any rule still naming
        /// it applies at once, which the dialog says.
        /// </remarks>
        public bool IsNew(string profile)
        {
            if (string.IsNullOrEmpty(profile) || Returning(profile).Count > 0)
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
        /// Whether the database already names as many profiles as its list holds, so that approving
        /// adds no name to it, new or removed.
        /// </summary>
        /// <remarks>
        /// The writer skips a name that does not fit and says so in the log, and the pairing lands
        /// without it, so a dialog promising to create one or to put one back would promise what
        /// does not happen.
        /// </remarks>
        public bool ListIsFull
        {
            get { return _known.Count >= KeePassGate.Acl.ProfileNames.Most; }
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
        /// unanswered. Nothing is spent, and the client is told the database is locked.
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

        private PairingApproval(
            PairingDecision decision, IReadOnlyList<string> profiles, TimeSpan? lifetime, string reason)
        {
            _decision = decision;
            _profiles = profiles ?? Nothing;
            _lifetime = lifetime ?? Authority.Lifetime.Unasked;
            _reason = reason ?? string.Empty;
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

        public bool IsApproved
        {
            get { return _decision == PairingDecision.Approved; }
        }

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
            return new PairingApproval(PairingDecision.Unavailable, null, null, reason);
        }

        public static PairingApproval Locked()
        {
            return new PairingApproval(PairingDecision.Locked, null, null, LockedReason);
        }

        /// <summary>What the log says of a question a lock took away.</summary>
        internal const string LockedReason = "closed unanswered when KeePass locked";
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
        PairingApproval Ask(PairingRequest request, TimeSpan timeout);
    }

    /// <summary>What a plugin with no screen answers: nothing is approved and nothing is spent.</summary>
    public sealed class NoPairingApproval : IPairingApproval
    {
        public static readonly NoPairingApproval Instance = new NoPairingApproval();

        public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
        {
            return PairingApproval.Unavailable("there is no screen to ask");
        }
    }
}
