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
    /// reach an answer. A candidate whose own authority does not stand among several by the time
    /// a connection asks is answered `busy`, as what the gate last served with is: there is no
    /// decision left to wait for, and a connection made now is answered by what the store holds
    /// then.
    ///
    /// **And what the gate last served a database with**, where the store gives no authority to
    /// answer with and holds the gate back: the host certificate and the gate key of the
    /// authority it was serving, kept since, answering what the store is read as now, to a
    /// connection that took the target before the reading changed and asks again: the store's
    /// word, `lost` where the file holds no store key, `undecided` where the store is whole and
    /// several authorities stand, the authority it was serving among them, and `busy` once the
    /// store holds nothing back and that authority does not stand among several: a single
    /// authority stands, or none, or several without it.
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
        /// What a target held back is answered with where its store holds nothing back and its
        /// own authority does not stand among several by the time a connection asks: a single
        /// authority stands, or none, or several without it. A connection made now is answered by
        /// what the store holds then: served where the authority standing alone is its own, and
        /// given no target where another stands alone, none stands, or several stand without it.
        /// </summary>
        public const string Busy = "busy";

        /// <summary>
        /// Why a database is answered this word under `503`, for the log: several authorities
        /// standing for <see cref="Undecided"/>, and the plugin's store holding the gate back for
        /// the store's own words.
        /// </summary>
        public static string Why(string word)
        {
            return word == Undecided
                ? "several authorities stand and the operator has kept none"
                : "the plugin's store holds the gate back: " + word;
        }

        private readonly string _databaseId;
        private readonly byte[] _gateKey;
        private readonly CertifiedKey _host;
        private readonly Func<string> _store;

        // The cache holds the first of these; every caller `Current` hands it to takes one more,
        // exactly as `LockedTarget` counts them.
        private int _holders = 1;

        /// <summary>A target answering under this gate key and host certificate.</summary>
        /// <param name="store">
        /// The word a connection to the database under this target's authority is answered with,
        /// read now: the store's word, or <see cref="Undecided"/> where the store is whole and
        /// several authorities stand, this target's among them; null where neither holds, which
        /// is answered <see cref="Busy"/>.
        /// </param>
        public HeldBackTarget(string databaseId, byte[] gateKey, CertifiedKey host, Func<string> store)
        {
            if (string.IsNullOrEmpty(databaseId))
            {
                throw new ArgumentException("A target held back is still named by its authority.", nameof(databaseId));
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
        }

        /// <summary>
        /// Why this target refuses a write, true of every target of the kind: the word a
        /// connection is answered with tells why the database is held back.
        /// </summary>
        private static string Reason
        {
            get { return "the gate holds the database back"; }
        }

        /// <summary>Nothing: the name is only read onto an approval dialog, and pairing is refused here.</summary>
        public string DatabaseName
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// Nothing but the word: pairing is refused here, so no dialog is built, and the request is
        /// answered as every connection to this target is, <see cref="Unavailable"/>.
        /// </summary>
        public AskedProfiles Asked(IReadOnlyCollection<string> profiles)
        {
            return AskedProfiles.Unserved(Unavailable);
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

        public Admitted Admission(string admissionKey)
        {
            return null;
        }

        public string AdmissionBlocked
        {
            get { return Reason; }
        }

        public AdmissionWrite Admit(AdmissionRecord record, Func<IWriteGround, string> condition, Action audit)
        {
            // Busy rather than failed, for the reason a locked database gives: what is held back
            // is read anew, and the invitee should try again rather than start over.
            return AdmissionWrite.Busy(Reason);
        }

        /// <summary>Busy, for the same reason: what is held back is read anew.</summary>
        public KeePassGate.Vault.VaultWrite GiveUp(string admissionKey, Action audit)
        {
            return KeePassGate.Vault.VaultWrite.Busy(Reason);
        }

        /// <summary>Busy, for the same reason: what is held back is read anew.</summary>
        public KeePassGate.Vault.VaultWrite AnswerRenewal(string clientKey, RenewalRefusal refusal, IReadOnlyCollection<string> shown)
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
        /// The store's word where the store holds the gate back, `undecided` where it is whole and
        /// several authorities stand, this target's among them, and `busy` otherwise.
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
            get { return _store() ?? Busy; }
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
