using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Everything a person needs to choose which of several authorities a database keeps, taken
    /// out of the database once so the dialog showing it reads nothing itself.
    /// </summary>
    /// <remarks>
    /// Separate from the dialog for the reason `AuthorityDescription` is separate from the tab:
    /// what is said is worth testing, and a form is not worth testing it through.
    ///
    /// **Which clients an authority admitted is asked through `AnchorPin`**, the gate's own first
    /// question, rather than read off anything the record says about itself. A record carries no
    /// authority; its certificate chains to one or to none, and that is what decides which
    /// candidate losing would cost that client.
    /// </remarks>
    public sealed class AuthorityChoice
    {
        private readonly List<AuthorityOption> _options;
        private readonly bool _lastSeenKnown;

        private AuthorityChoice(IEnumerable<AuthorityOption> options, bool lastSeenKnown)
        {
            _options = new List<AuthorityOption>(options);
            _lastSeenKnown = lastSeenKnown;
        }

        /// <summary>One per candidate, in the order the reading gave them.</summary>
        public IReadOnlyList<AuthorityOption> Options
        {
            get { return _options; }
        }

        /// <summary>
        /// Whether the audit could be read at all. False makes every "last request" unknown, which
        /// is said as that rather than as "never".
        /// </summary>
        public bool LastSeenKnown
        {
            get { return _lastSeenKnown; }
        }

        /// <summary>
        /// The choice a reading of several authorities offers.
        /// </summary>
        /// <param name="lastSeen">
        /// When each admission key was last written down in the audit, or null when it could not
        /// be read. Asked once, for every client of every candidate at once.
        /// </param>
        public static AuthorityChoice From(
            AuthorityReading reading,
            AdmissionListing clients,
            Func<ICollection<string>, IDictionary<string, DateTimeOffset>> lastSeen,
            IGateLog log)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            var logger = log ?? NullGateLog.Instance;
            var records = clients == null ? new List<AdmissionRecord>() : clients.Records.ToList();

            IDictionary<string, DateTimeOffset> seen = null;
            if (lastSeen != null)
            {
                try
                {
                    seen = lastSeen(records.Select(record => record.AdmissionKey).ToList());
                }
                catch (Exception error) when (Expected.OfStorage(error))
                {
                    logger.Warn("the audit could not be read for when clients last connected: " + error.Message);
                }
            }

            var options = new List<AuthorityOption>();
            foreach (var candidate in reading.Candidates)
            {
                options.Add(Option(candidate, records, seen, logger));
            }

            return new AuthorityChoice(options, seen != null);
        }

        private static AuthorityOption Option(
            AuthorityCandidate candidate,
            IEnumerable<AdmissionRecord> records,
            IDictionary<string, DateTimeOffset> seen,
            IGateLog log)
        {
            if (!candidate.IsWhole)
            {
                return AuthorityOption.Unreadable(
                    candidate.Fingerprint,
                    Clipped.Message(string.Join(" ", candidate.Problems)));
            }

            var material = candidate.Material;
            var admitted = new List<ChoiceClient>();

            foreach (var record in records)
            {
                try
                {
                    using (var certificate = record.Certificate())
                    {
                        if (!AnchorPin.Chains(certificate, material.Authority, true))
                        {
                            continue;
                        }
                    }
                }
                catch (Exception error) when (Expected.OfCryptography(error))
                {
                    log.Warn("an admission record could not be checked against a candidate: " + error.Message);
                    continue;
                }

                DateTimeOffset when;
                admitted.Add(new ChoiceClient(
                    ClientList.Describe(record),
                    record.ClientKey,
                    record.Profiles,
                    record.Admitted,
                    seen != null && seen.TryGetValue(record.AdmissionKey, out when) ? when : (DateTimeOffset?)null));
            }

            admitted.Sort((left, right) => right.Admitted.CompareTo(left.Admitted));

            return AuthorityOption.Whole(candidate.Fingerprint, material.Authority.NotBefore, admitted);
        }
    }

    /// <summary>One authority the operator could keep, or one that will not read.</summary>
    public sealed class AuthorityOption
    {
        private AuthorityOption(
            string fingerprint,
            bool canKeep,
            string problem,
            DateTime? created,
            IReadOnlyList<ChoiceClient> clients)
        {
            Fingerprint = fingerprint;
            CanKeep = canKeep;
            Problem = problem;
            Created = created;
            Clients = clients ?? Array.Empty<ChoiceClient>();
        }

        /// <summary>The whole digest, lowercase hex.</summary>
        public string Fingerprint { get; }

        /// <summary>The digest as the tab shows it.</summary>
        public string ShortFingerprint
        {
            get { return CertificateDigest.Shorten(Fingerprint); }
        }

        /// <summary>Whether it reads whole, which is the only way it can be kept.</summary>
        public bool CanKeep { get; }

        /// <summary>What is wrong with it, cut to fit, or null when nothing is.</summary>
        public string Problem { get; }

        /// <summary>
        /// When it was made, which is what tells two copies' authorities apart to somebody who
        /// remembers making one. Until when it is valid is not shown: an authority and its host
        /// certificate are issued for a hundred years.
        /// </summary>
        public DateTime? Created { get; }

        /// <summary>The admitted clients whose certificates chain to it, newest first.</summary>
        public IReadOnlyList<ChoiceClient> Clients { get; }

        public static AuthorityOption Whole(
            string fingerprint, DateTime created, IReadOnlyList<ChoiceClient> clients)
        {
            return new AuthorityOption(fingerprint, true, null, created, clients);
        }

        public static AuthorityOption Unreadable(string fingerprint, string problem)
        {
            return new AuthorityOption(fingerprint, false, problem, null, null);
        }
    }

    /// <summary>One admitted client of one candidate.</summary>
    public sealed class ChoiceClient
    {
        public ChoiceClient(
            string name,
            string clientKey,
            IReadOnlyList<string> profiles,
            DateTimeOffset admitted,
            DateTimeOffset? lastSeen)
        {
            Name = name ?? string.Empty;
            ClientKey = clientKey ?? string.Empty;
            Profiles = profiles ?? Array.Empty<string>();
            Admitted = admitted;
            LastSeen = lastSeen;
        }

        public string Name { get; }

        /// <summary>
        /// Which client this certificate belongs to, `AdmissionRecord.ClientKey`, which is what the
        /// count of an authority's clients counts.
        /// </summary>
        public string ClientKey { get; }

        public IReadOnlyList<string> Profiles { get; }

        public DateTimeOffset Admitted { get; }

        /// <summary>When the audit last wrote it down, or null when it holds nothing for it.</summary>
        public DateTimeOffset? LastSeen { get; }
    }
}
