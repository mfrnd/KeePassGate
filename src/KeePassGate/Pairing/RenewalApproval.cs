using System;
using System.Collections.Generic;
using KeePassGate.Authority;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// What the operator is asked when an admitted client asks for a new certificate.
    /// </summary>
    /// <remarks>
    /// Everything here was read from the database or from the connection rather than from what the
    /// client sent, except the lifetime: who it is comes from its record, which profiles from the
    /// record less every name the database no longer holds, and where it connected from from the
    /// socket. A renewal is asked with the certificate it renews, so there is nothing for it to
    /// claim about itself.
    /// </remarks>
    public sealed class RenewalRequest
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        public RenewalRequest(
            string databaseId,
            string databaseName,
            string name,
            DateTimeOffset admitted,
            DateTimeOffset expires,
            IReadOnlyList<string> profiles,
            IReadOnlyList<string> dropped,
            string peer,
            TimeSpan? lifetime)
        {
            DatabaseId = databaseId ?? string.Empty;
            DatabaseName = databaseName ?? string.Empty;
            Name = name ?? string.Empty;
            Admitted = admitted;
            Expires = expires;
            Profiles = profiles ?? Nothing;
            Dropped = dropped ?? Nothing;
            Peer = peer ?? string.Empty;
            Lifetime = lifetime;
        }

        /// <summary>Which database, by its authority's digest.</summary>
        public string DatabaseId { get; private set; }

        /// <summary>What KeePass calls it, or empty when it has nothing to be called.</summary>
        public string DatabaseName { get; private set; }

        /// <summary>The name the client was issued under when it paired.</summary>
        public string Name { get; private set; }

        /// <summary>When the record it renews was written.</summary>
        public DateTimeOffset Admitted { get; private set; }

        /// <summary>When the certificate it holds now stops working.</summary>
        public DateTimeOffset Expires { get; private set; }

        /// <summary>
        /// The profiles the new certificate would carry: the ones its record holds that the
        /// database still names. The operator may take any of them away and can add none.
        /// </summary>
        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>
        /// The ones its record holds that the database no longer names, which are not carried over.
        /// </summary>
        public IReadOnlyList<string> Dropped { get; private set; }

        /// <summary>The address the request came from.</summary>
        public string Peer { get; private set; }

        /// <summary>How long the client asked the new certificate to last, or null for nothing.</summary>
        public TimeSpan? Lifetime { get; private set; }
    }

    /// <summary>What the operator decided about a renewal.</summary>
    public sealed class RenewalApproval
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private RenewalApproval(
            PairingDecision decision,
            IReadOnlyList<string> profiles,
            TimeSpan? lifetime,
            RenewalRefusal standing,
            string reason)
        {
            Decision = decision;
            Profiles = profiles ?? Nothing;
            Lifetime = lifetime ?? Authority.Lifetime.Unasked;
            Standing = standing;
            Reason = reason ?? string.Empty;
        }

        public PairingDecision Decision { get; private set; }

        /// <summary>What the new certificate carries, when it was approved.</summary>
        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>How long the new certificate lasts, when it was approved.</summary>
        public TimeSpan Lifetime { get; private set; }

        /// <summary>
        /// The answer to every later renewal of the same certificate, when the operator refused
        /// it and meant more than once; null otherwise.
        /// </summary>
        public RenewalRefusal Standing { get; private set; }

        public string Reason { get; private set; }

        /// <param name="lifetime">How long the new certificate lasts, or null for a year.</param>
        public static RenewalApproval Approved(IReadOnlyList<string> profiles, TimeSpan? lifetime = null)
        {
            return new RenewalApproval(PairingDecision.Approved, profiles, lifetime, null, "approved");
        }

        /// <param name="standing">
        /// What every later renewal of this certificate is answered with, or null for once only.
        /// </param>
        public static RenewalApproval Refused(RenewalRefusal standing = null)
        {
            return new RenewalApproval(PairingDecision.Refused, null, null, standing, RefusedReason(standing));
        }

        /// <summary>What the log says a refusal was, in the words of the menu it was chosen from.</summary>
        private static string RefusedReason(RenewalRefusal standing)
        {
            if (standing == null)
            {
                return "refused once";
            }

            if (standing.IsThisSession)
            {
                return "refused in this session";
            }

            return standing.IsForever ? "refused forever" : "refused until a date";
        }

        public static RenewalApproval TimedOut()
        {
            return new RenewalApproval(PairingDecision.TimedOut, null, null, null, "nobody answered");
        }

        public static RenewalApproval Unavailable(string reason)
        {
            return new RenewalApproval(PairingDecision.Unavailable, null, null, null, reason);
        }

        public static RenewalApproval Locked()
        {
            return new RenewalApproval(PairingDecision.Locked, null, null, null, PairingApproval.LockedReason);
        }
    }

    /// <summary>Asks the operator about a renewal and waits for the answer.</summary>
    public interface IRenewalApproval
    {
        RenewalApproval Ask(RenewalRequest request, TimeSpan timeout);
    }

    /// <summary>What a plugin with no screen answers: nothing is renewed and nothing is stored.</summary>
    public sealed class NoRenewalApproval : IRenewalApproval
    {
        public static readonly NoRenewalApproval Instance = new NoRenewalApproval();

        public RenewalApproval Ask(RenewalRequest request, TimeSpan timeout)
        {
            return RenewalApproval.Unavailable("there is no screen to ask");
        }
    }
}
