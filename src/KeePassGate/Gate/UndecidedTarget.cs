using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;

namespace KeePassGate.Gate
{
    /// <summary>
    /// One of several authorities a database holds while the operator has not said which to keep:
    /// enough to answer a connection with `503 undecided`, and nothing else.
    /// </summary>
    /// <remarks>
    /// Copies of a database that each made an authority of their own and were then merged hold
    /// both, and serving either would be choosing for the operator. So nothing is served, and a
    /// client told so can wait rather than be left with a connection that fails for no reason it
    /// can see: one of these per candidate, each under that candidate's own gate key, so the
    /// clients of either one reach an answer.
    ///
    /// **It presents the candidate's own host certificate and mints nothing.** The responder a
    /// locked database answers with is minted from an authority, and minting from one of several
    /// would be the choice again. The host certificate is already stored beside the authority
    /// that issued it, so the handshake completes with bytes that exist whatever is chosen.
    ///
    /// What it cannot do is what `LockedTarget` cannot do, for the same reason by construction:
    /// <see cref="Authority"/> is null, so no client certificate is validated and every caller
    /// is answered alike, and there are no records to find and nothing may be written.
    /// </remarks>
    public sealed class UndecidedTarget : IGateTarget
    {
        /// <summary>What every connection to one of these is answered with, under `503`.</summary>
        public const string Error = "undecided";

        /// <summary>Why this target refuses, for the log and for the write it turns away.</summary>
        public const string Reason =
            "the database holds more than one authority and the operator has not chosen one";

        private readonly string _databaseId;
        private readonly byte[] _gateKey;
        private readonly HostKeyMaterial _host;

        // The cache holds the first of these; every caller `Current` hands it to takes one more,
        // exactly as `LockedTarget` counts them.
        private int _holders = 1;

        public UndecidedTarget(string databaseId, byte[] gateKey, HostKeyMaterial host)
        {
            if (string.IsNullOrEmpty(databaseId))
            {
                throw new ArgumentException("A candidate is still named by its authority.", nameof(databaseId));
            }

            if (gateKey == null)
            {
                throw new ArgumentNullException(nameof(gateKey));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            _databaseId = databaseId;
            _gateKey = (byte[])gateKey.Clone();
            _host = host;
        }

        /// <summary>Nothing: the name is only read onto an approval dialog, and pairing is refused here.</summary>
        public string DatabaseName
        {
            get { return string.Empty; }
        }

        public IReadOnlyList<string> KnownProfiles
        {
            get { return Array.Empty<string>(); }
        }

        /// <summary>Nothing read here, so nothing that would not read.</summary>
        public string ProfilesUnreadable
        {
            get { return null; }
        }

        /// <summary>Nobody: pairing is refused here, so no dialog asks.</summary>
        public IReadOnlyList<AdmissionRecord> Holding(string profile)
        {
            return Array.Empty<AdmissionRecord>();
        }

        /// <summary>The candidate's own digest, which is what its clients and invitations name.</summary>
        public string DatabaseId
        {
            get { return _databaseId; }
        }

        /// <summary>A copy, as everywhere: the caller may zero what it is given.</summary>
        public byte[] GateKey
        {
            get { return (byte[])_gateKey.Clone(); }
        }

        public HostKeyMaterial Host
        {
            get { return _host; }
        }

        /// <summary>Null, so that no client certificate can be judged against a candidate.</summary>
        public X509Certificate2 Authority
        {
            get { return null; }
        }

        public AdmissionRecord Admission(string admissionKey)
        {
            return null;
        }

        public string AdmissionBlocked
        {
            get { return Reason; }
        }

        public AdmissionWrite Admit(AdmissionRecord record, Func<IKeyValueStore, string> condition)
        {
            // Busy rather than failed, for the reason a locked database gives: the operator
            // choosing is all it takes, and the invitee should try again rather than start over.
            return AdmissionWrite.Busy(Reason);
        }

        /// <summary>Busy, for the same reason: the operator choosing is all it takes.</summary>
        public KeePassGate.Vault.VaultWrite Revoke(string admissionKey)
        {
            return KeePassGate.Vault.VaultWrite.Busy(Reason);
        }

        /// <summary>None, while there is nothing to read one from.</summary>
        public RenewalRefusal StandingRefusal(string admissionKey)
        {
            return null;
        }

        /// <summary>Busy, for the same reason: the operator choosing is all it takes.</summary>
        public KeePassGate.Vault.VaultWrite RefuseRenewal(string admissionKey, RenewalRefusal refusal)
        {
            return KeePassGate.Vault.VaultWrite.Busy(Reason);
        }

        public Vault.IVault Vault
        {
            get { return null; }
        }

        public bool IsLocked
        {
            get { return false; }
        }

        public string Unavailable
        {
            get { return Error; }
        }

        public void Retain()
        {
            Interlocked.Increment(ref _holders);
        }

        /// <summary>Zeroes the gate key once the last holder lets go.</summary>
        public void Dispose()
        {
            if (Interlocked.Decrement(ref _holders) == 0)
            {
                Array.Clear(_gateKey, 0, _gateKey.Length);
            }
        }
    }
}
