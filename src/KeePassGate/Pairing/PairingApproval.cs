using System;
using System.Collections.Generic;

namespace KeePassGate.Pairing
{
    /// <summary>What the operator was asked.</summary>
    public sealed class PairingRequest
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private readonly string _databaseId;
        private readonly string _invitationId;
        private readonly string _name;
        private readonly IReadOnlyList<string> _profiles;
        private readonly string _peer;

        public PairingRequest(
            string databaseId,
            string invitationId,
            string name,
            IReadOnlyList<string> profiles,
            string peer)
        {
            _databaseId = databaseId ?? string.Empty;
            _invitationId = invitationId ?? string.Empty;
            _name = name ?? string.Empty;
            _profiles = profiles ?? Nothing;
            _peer = peer ?? string.Empty;
        }

        /// <summary>Which database is about to admit somebody, by its authority's digest.</summary>
        public string DatabaseId
        {
            get { return _databaseId; }
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
        Unavailable
    }

    /// <summary>What came back from the screen.</summary>
    public sealed class PairingApproval
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private readonly PairingDecision _decision;
        private readonly IReadOnlyList<string> _profiles;
        private readonly string _reason;

        private PairingApproval(PairingDecision decision, IReadOnlyList<string> profiles, string reason)
        {
            _decision = decision;
            _profiles = profiles ?? Nothing;
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

        public string Reason
        {
            get { return _reason; }
        }

        public bool IsApproved
        {
            get { return _decision == PairingDecision.Approved; }
        }

        public static PairingApproval Approved(IReadOnlyList<string> profiles)
        {
            return new PairingApproval(PairingDecision.Approved, profiles, "approved");
        }

        public static PairingApproval Refused()
        {
            return new PairingApproval(PairingDecision.Refused, null, "refused");
        }

        public static PairingApproval TimedOut()
        {
            return new PairingApproval(PairingDecision.TimedOut, null, "nobody answered");
        }

        public static PairingApproval Unavailable(string reason)
        {
            return new PairingApproval(PairingDecision.Unavailable, null, reason);
        }
    }

    /// <summary>
    /// Asking the person at the keyboard whether a stranger may have a certificate.
    /// </summary>
    /// <remarks>
    /// The one place in this plugin where a request reaches a screen, and `DESIGN.md` §119
    /// says why the exception is confined to here: an unattended agent must not be able to raise a
    /// dialog, so every other route refuses rather than asks. This one exists to be asked.
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
