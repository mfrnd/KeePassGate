using System;
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;

namespace KeePassGate.Gate
{
    /// <summary>
    /// A database that is locked: enough to answer a connection with `503 locked`, and nothing else.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §308 is the specification, and the table there is this class. Locking
    /// closes the database, so the authority, the admission records and the normal host key are all
    /// gone. Two things are deliberately kept.
    ///
    /// **The responder certificate**, minted while the database was open and held only in memory.
    /// Without it there is nothing to complete a handshake with, and a client gets a bare
    /// connection failure indistinguishable from a wrong port, a stopped KeePass, or an attack.
    /// With it, a locked KeePass says "locked" and a client can wait rather than guess.
    ///
    /// **The gate key**, because the gate resolves a name before any TLS work. Drop it and every
    /// connection is refused at the gate with the responder sitting unused. Of the three secrets it
    /// is the right one to keep: it authenticates nothing, and every paired client already holds it.
    ///
    /// What it cannot do is the point. <see cref="Authority"/> is null, so no client certificate can
    /// be validated and every caller is a stranger by construction rather than by a check somebody
    /// has to remember. There are no records to find and nothing may be written, so pairing is
    /// refused here for the same reason everything else is.
    /// </remarks>
    public sealed class LockedTarget : IGateTarget, IDisposable
    {
        /// <summary>
        /// Why this target refuses, for the log and for the write it turns away.
        /// </summary>
        /// <remarks>
        /// Not what the gate answers with. That is `GateConversation.LockedError`, which is the
        /// one word `locked` and is written before a request is read; this is the sentence a
        /// person reads.
        /// </remarks>
        public const string Reason = "the database is locked";

        private readonly string _databaseId;
        private readonly byte[] _gateKey;
        private readonly HostKeyMaterial _responder;
        // The cache holds the first of these; every caller `Current` hands it to takes one more.
        // It was a bare flag, and the first caller to give the target back zeroed the gate key for
        // everybody: the next connection matched its name against 32 zero bytes and was dropped.
        private int _holders = 1;

        public LockedTarget(string databaseId, byte[] gateKey, HostKeyMaterial responder)
        {
            if (string.IsNullOrEmpty(databaseId))
            {
                throw new ArgumentException("A locked database is still named by its authority.", nameof(databaseId));
            }

            if (gateKey == null)
            {
                throw new ArgumentNullException(nameof(gateKey));
            }

            if (responder == null)
            {
                throw new ArgumentNullException(nameof(responder));
            }

            _databaseId = databaseId;
            _gateKey = (byte[])gateKey.Clone();
            _responder = responder;
        }

        public string DatabaseId
        {
            get { return _databaseId; }
        }

        /// <summary>A copy, as everywhere: the caller may zero what it is given.</summary>
        public byte[] GateKey
        {
            get { return (byte[])_gateKey.Clone(); }
        }

        /// <summary>The responder, which is the only identity left.</summary>
        public HostKeyMaterial Host
        {
            get { return _responder; }
        }

        /// <summary>
        /// Null, and that is the whole of the privilege drop. Validating a client certificate needs
        /// the authority, the authority went with the database, and nothing here can pretend
        /// otherwise.
        /// </summary>
        public X509Certificate2 Authority
        {
            get { return null; }
        }

        /// <summary>Nobody is admitted while there is nothing to read records from.</summary>
        public AdmissionRecord Admission(string admissionKey)
        {
            return null;
        }

        /// <summary>
        /// Empty, because a locked database says nothing about itself.
        /// </summary>
        /// <remarks>
        /// The name is only ever read to put on an approval dialog, and pairing is refused here
        /// before anything reaches one. A locked database names the lock and nothing else: not
        /// which database it is, not what it is called, not whether the caller would have been
        /// admitted.
        /// </remarks>
        public string DatabaseName
        {
            get { return string.Empty; }
        }

        public string AdmissionBlocked
        {
            get { return Reason; }
        }

        public AdmissionWrite Admit(AdmissionRecord record)
        {
            // Busy rather than failed: unlocking is all it takes, and the invitee should try again
            // rather than be told to start over.
            return AdmissionWrite.Busy(Reason);
        }

        public bool IsLocked
        {
            get { return true; }
        }

        /// <summary>
        /// Takes one more reference, for one more caller, which that caller gives back with
        /// <see cref="Dispose"/>.
        /// </summary>
        public void Retain()
        {
            Interlocked.Increment(ref _holders);
        }

        /// <summary>
        /// Gives a reference back, and when it is the last one zeroes the gate key, which is what
        /// closing a database for real means as opposed to locking it. The responder goes with
        /// this object: it is bytes and a key on the heap, held by nothing else, so releasing this
        /// is what releases it.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Decrement(ref _holders) == 0)
            {
                Array.Clear(_gateKey, 0, _gateKey.Length);
            }
        }
    }
}
