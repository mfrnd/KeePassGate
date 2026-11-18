using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;

namespace KeePassGate.Gate
{
    /// <summary>
    /// A database the gate holds back while it can still answer for it: enough to answer a
    /// connection with a word under `503`, and nothing else, `STORE.md` §656.
    /// </summary>
    /// <remarks>
    /// **Each of several authorities a database holds while the operator has not chosen which to
    /// keep** is answered by a target of this kind, with `undecided`. Copies of a database that
    /// each made an authority of their own and were then merged hold both, and serving either
    /// would be choosing for the operator. So nothing is served, and a client told so can wait
    /// rather than be left with a connection that fails for no reason it can see: a target per
    /// candidate, each under that candidate's own gate key, so the clients of either authority
    /// reach an answer.
    ///
    /// **And what the gate last served a database with**, where the store gives no authority to
    /// answer with and holds the gate back: the host certificate and the gate key of the
    /// authority it was serving, kept since, answering the store's word, `lost` where the file
    /// holds no store key, and `busy` once the store holds nothing back, to a connection that
    /// took the target before the reading changed and asks again.
    ///
    /// **It presents a host certificate it was handed and mints nothing.** The responder a
    /// locked database answers with is minted from an authority, and minting from one of several
    /// would be the choice again, while an authority the store does not give now cannot mint. The
    /// host certificate is stored beside the authority that issued it, or was kept from the last
    /// target served, so the handshake completes with bytes that exist whatever comes next.
    ///
    /// What it cannot do is what `LockedTarget` cannot do, for the same reason by construction:
    /// <see cref="Authority"/> is null, so no client certificate is validated and every caller
    /// is answered alike, and there are no records to find and nothing may be written.
    /// </remarks>
    public sealed class HeldBackTarget : IGateTarget
    {
        /// <summary>What a candidate among several authorities is answered with, under `503`.</summary>
        public const string Undecided = "undecided";

        /// <summary>
        /// What a target kept from the last served is answered with where its store holds nothing
        /// back by the time a connection asks: worth trying again, since what is held is read anew.
        /// </summary>
        public const string Busy = "busy";

        private readonly string _databaseId;
        private readonly byte[] _gateKey;
        private readonly CertifiedKey _host;
        private readonly Func<string> _store;
        private readonly string _otherwise;

        // The cache holds the first of these; every caller `Current` hands it to takes one more,
        // exactly as `LockedTarget` counts them.
        private int _holders = 1;

        /// <summary>A target answering under this gate key and host certificate.</summary>
        /// <param name="store">
        /// The word the database's store holds the gate back with, read now, or null where it
        /// holds nothing back.
        /// </param>
        /// <param name="otherwise">
        /// What is answered where the store holds nothing back: <see cref="Undecided"/> for a
        /// candidate among several authorities, <see cref="Busy"/> for what the gate last served
        /// with.
        /// </param>
        public HeldBackTarget(string databaseId, byte[] gateKey, CertifiedKey host, Func<string> store, string otherwise)
        {
            if (string.IsNullOrEmpty(databaseId))
            {
                throw new ArgumentException("A target held back is still named by its authority.", nameof(databaseId));
            }

            if (string.IsNullOrEmpty(otherwise))
            {
                throw new ArgumentException("A target held back answers a word.", nameof(otherwise));
            }

            if (gateKey == null)
            {
                throw new ArgumentNullException(nameof(gateKey));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _databaseId = databaseId;
            _gateKey = (byte[])gateKey.Clone();
            _host = host;
            _store = store;
            _otherwise = otherwise;
        }

        /// <summary>Why this target refuses, for the log and for the write it turns away.</summary>
        private string Reason
        {
            get
            {
                return _otherwise == Undecided
                    ? "the database holds more than one authority and the operator has chosen none of them"
                    : "the database's store holds the gate back";
            }
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

        public CertifiedKey Host
        {
            get { return _host; }
        }

        /// <summary>Null, so that no client certificate can be judged against a candidate.</summary>
        public X509Certificate2 Authority
        {
            get { return null; }
        }

        /// <summary>Null, so that nothing is issued from a candidate either.</summary>
        public CertifiedKey AuthorityKey
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

        public AdmissionWrite Admit(AdmissionRecord record, Func<IWriteGround, string> condition, Action audit)
        {
            // Busy rather than failed, for the reason a locked database gives: the operator
            // choosing is all it takes, and the invitee should try again rather than start over.
            return AdmissionWrite.Busy(Reason);
        }

        /// <summary>Busy, for the same reason: the operator choosing is all it takes.</summary>
        public KeePassGate.Vault.VaultWrite GiveUp(string admissionKey, Action audit)
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

        /// <summary>
        /// The store's word where the store holds the gate back, and what this target was made to
        /// answer otherwise.
        /// </summary>
        /// <remarks>
        /// **The store first, as `STORE.md` §656 orders the words**: which authority is served is
        /// asked only of a store that is whole, so a database holding several authorities and a
        /// store that is split, outdated or stopped is answered the store's word, under each of
        /// its authorities. A reading that could not be made throws, as for a target that is
        /// served.
        /// </remarks>
        public string Unavailable
        {
            get { return _store() ?? _otherwise; }
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
