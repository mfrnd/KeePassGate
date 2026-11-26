using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Vault;

namespace KeePassGate.Gate
{
    /// <summary>
    /// One database as the transport sees it: an identifier, a gate key to recognize names by, and
    /// the certificates to serve and validate with.
    /// </summary>
    /// <remarks>
    /// Deliberately not a KeePass database. The transport has no business reaching entries, and the
    /// seam is what lets every one of these tests run without a password manager.
    ///
    /// **Disposable, because a target is held rather than owned.** The cache that builds them and
    /// the connection that serves from one are two holders of the same thing, and the material
    /// goes when the last of them lets go. A caller that took one from `Current` gives it back.
    /// </remarks>
    public interface IGateTarget : IDisposable
    {
        /// <summary>
        /// The authority's digest, lowercase hex. Stable for the life of the authority, derived
        /// from public bytes, and meaningless to anyone who has not already been let in.
        /// </summary>
        string DatabaseId { get; }

        /// <summary>
        /// What KeePass calls this database, or empty when it has nothing to be called.
        /// </summary>
        /// <remarks>
        /// For the two approval dialogs and nothing else. A dialog that identifies the database only
        /// by <see cref="DatabaseId"/> asks the operator to recognize sixteen hexadecimal characters
        /// before letting a stranger in, which is exactly the check nobody performs. The digest
        /// stays beside it, because it is what actually identifies the database and two of them can
        /// carry the same name.
        ///
        /// It never leaves the machine: a name is somebody's own word for their database, and the
        /// wire carries the digest, which identifies it without describing it.
        /// </remarks>
        string DatabaseName { get; }

        /// <summary>A copy of the gate key, which is what connection names are built from.</summary>
        byte[] GateKey { get; }

        /// <summary>
        /// The certificate to present and the key to prove it with, in memory and nowhere else.
        /// </summary>
        CertifiedKey Host { get; }

        /// <summary>The authority every client certificate has to chain to.</summary>
        X509Certificate2 Authority { get; }

        /// <summary>
        /// The authority with its key, which a client certificate is issued with. Null wherever
        /// <see cref="Authority"/> is.
        /// </summary>
        CertifiedKey AuthorityKey { get; }

        /// <summary>
        /// The admission of this certificate where it counts, with the profiles this database
        /// names and the standing answers about its client's renewals, or null when there is none,
        /// `STORE.md` §615 and §618.
        /// </summary>
        /// <remarks>
        /// Read from the database's store on every call rather than cached with the certificates,
        /// because a revocation has to take effect on the next connection rather than on the next
        /// time something else changed.
        ///
        /// **The names in the same hold, from the same reading**, which is what a client's
        /// profiles are made of: the admission's names less every name the list does not hold, so
        /// taking a name off the list takes it from every client without touching an admission. A
        /// renewal offers the admission's names the list still holds from the same answer, so the
        /// dialog shows the list as the reading that found the admission had it, and is answered
        /// by the standing answer of the same reading.
        ///
        /// **Throws `VaultUnsettled` when KeePass's thread could not be held for the reading**,
        /// which is not the same as there being none. **And `DatabaseShut` when the database closed
        /// under the caller**, a lock nearly always, which is answered as one.
        /// </remarks>
        Admitted Admission(string admissionKey);

        /// <summary>
        /// Why a record cannot be written right now, or null when one can. Checked before an
        /// invitation is spent and before the operator is asked, so that Database Maintenance
        /// left open on this database costs a pairing attempt rather than the invitation behind it.
        /// </summary>
        string AdmissionBlocked { get; }

        /// <summary>
        /// What the store holds now of the profiles a pairing asks for, from a single reading made
        /// afresh with the store key: the names on the list, and of each of these profiles off it,
        /// the admitted clients whose admissions hold it and the removals of it.
        /// </summary>
        /// <remarks>
        /// For the pairing dialog, read on KeePass's thread as it comes up and on each tick while it
        /// stands, and for the approval it gives, `PairingRequest.Read`. The
        /// names are what the store's records give, `STORE.md` §616, and a damaged record of a name
        /// stops the store, which is answered its word before anything is read of it: so there is no
        /// list that fails to read on its own. <see cref="AskedProfiles.Unserved"/>, with the word
        /// the write answers the same store with, where the store does not serve this target's
        /// authority, and no dialog is built from it, `AskedProfiles.Served`. A client's profiles
        /// and a renewal's are read with the admission, <see cref="Admission"/>.
        ///
        /// **The clients holding a name off the list**: an admission keeps every name it was
        /// approved for, so a profile the operator removed is still held, granting nothing, by every
        /// client approved for it before; approving a pairing for that name puts it back on the list
        /// and gives it back to all of them. The dialog names them, so that is not a side effect
        /// nobody saw. Only clients that get in: an admission whose certificate has expired regains
        /// nothing. Not asked of a name on the list, which is held by whoever holds it already, and
        /// approving it changes nothing for them.
        ///
        /// **The removals of a name off the list**: an approval of such a name is compared at its
        /// write with what this held, and a removal of it beyond those makes the approval stale,
        /// `PAIRING.md` §311, as an Add on the Profiles page that a removal overtook writes nothing,
        /// `STORE.md` §627.
        /// </remarks>
        AskedProfiles Asked(IReadOnlyCollection<string> profiles);

        /// <summary>
        /// Writes an admission into the plugin's store and makes it durable, on whichever thread
        /// owns the database.
        /// </summary>
        /// <remarks>
        /// One of the three writes a connection asks of its target, beside <see cref="GiveUp"/>
        /// and <see cref="AnswerRenewal"/>, the API's own going through <see cref="Vault"/>. It
        /// marshals synchronously like every write this plugin makes, so what it answers is
        /// whether the admission is on disk. It is checked again inside the write, because the
        /// answer to <see cref="AdmissionBlocked"/> can change between asking and writing, and a
        /// pairing that cannot land its admission has to fail rather than hand over a certificate
        /// nobody can see.
        /// </remarks>
        /// <param name="condition">
        /// Why the admission may not be written, or null when it may, asked of the store as
        /// it stands in the write's own turn on KeePass's thread, under the
        /// writer's lock, once the database is known to be writable and before anything is
        /// changed. Where an approval is held, spent and compared with what it was given against.
        /// A refusal is answered as a failure, the caller knowing which part refused. Null for
        /// none.
        /// </param>
        /// <param name="audit">
        /// The audit line for the admission, which the write itself writes once it has landed and
        /// before anything else is told, so that nothing after the write can lose it. Run once, on
        /// KeePass's own thread, where it must not wait; never when the admission did not land, so
        /// the caller writes no line of its own about it. Null for no line.
        /// </param>
        AdmissionWrite Admit(AdmissionRecord record, Func<IWriteGround, string> condition, Action audit);

        /// <summary>
        /// Takes the certificate of this admission key away, at its own client's request, and
        /// makes that durable the way every change is, `STORE.md` §648.
        /// </summary>
        /// <remarks>
        /// A record of kind `withdrawn` written into the plugin's store, marshaled and saved like
        /// <see cref="Admit"/> and answered with what happened to it: done, busy, not saved and
        /// taken back out, saved and not verified, the store's word, or failed. A certificate no
        /// longer admitted is answered as missing, which is an admission that went before the
        /// write could be made.
        /// </remarks>
        /// <param name="audit">
        /// The audit line, written as <see cref="Admit"/> writes an admission's: once, by the write
        /// itself, whenever the record is left in force, saved and verified or saved and not
        /// verified, and never otherwise. Null for no line.
        /// </param>
        VaultWrite GiveUp(string admissionKey, Action audit);

        /// <summary>
        /// Writes the operator's standing answer to a client's renewals into the plugin's store,
        /// `STORE.md` §618, and makes it durable the way every change is.
        /// </summary>
        /// <remarks>
        /// Asked for in the same breath as the refusal it belongs to, and answered with what came
        /// of it; the refusal stands whatever this answers, and the caller holds an answer that was
        /// not written for this session and tells the operator.
        /// </remarks>
        /// <param name="clientKey">The client whose renewals are refused.</param>
        /// <param name="shown">
        /// The identifiers of the standing versions of the client's answer that the reading before
        /// the dialog held, which the version written names and nothing else.
        /// </param>
        VaultWrite AnswerRenewal(string clientKey, RenewalRefusal refusal, IReadOnlyCollection<string> shown);

        /// <summary>
        /// What the API reads and changes, or null when there is nothing to reach.
        /// </summary>
        /// <remarks>
        /// The transport still has no business with entries: it carries this to the handlers and
        /// never asks it anything. Null on a locked database, which answers every route with the
        /// lock and nothing else.
        /// </remarks>
        IVault Vault { get; }

        /// <summary>
        /// Whether this database is locked, in which case it can answer a connection and do
        /// nothing else.
        /// </summary>
        /// <remarks>
        /// Asked rather than inferred. A locked database has no authority, so every caller is a
        /// stranger anyway, but a stranger and a locked database deserve different answers: one is
        /// told to pair and the other is told to wait. `PAIRING.md` §308.
        /// </remarks>
        bool IsLocked { get; }

        /// <summary>
        /// The word a connection to this database under this target's authority is answered
        /// with under `503`, or null when it is served.
        /// </summary>
        /// <remarks>
        /// `locked` for a locked database, `undecided` for a database holding several authorities
        /// while the operator has not chosen between them, this target's among them, and a word
        /// of <c>StoreWords</c> for a database whose store holds the gate back, `STORE.md` §656.
        /// Each is answered before a request is judged at all, because each has the same answer
        /// for every path, method and body. Where several authorities stand and this target's is
        /// not among them, no decision can serve its clients: a target that serves an authority
        /// has no word then, and a request is answered as under an authority that went, while a
        /// target held back answers `busy`.
        ///
        /// **Asking it may read the database**, with KeePass's thread held, so it throws
        /// `VaultUnsettled` where the thread could not be held and `DatabaseShut` where the
        /// database has gone, as <see cref="Admission"/> does: never an answer that it is served.
        /// </remarks>
        string Unavailable { get; }
    }

    /// <summary>
    /// The databases that can be served right now. Read afresh per connection, because a database
    /// can be opened, locked or closed between any two of them.
    /// </summary>
    public interface IGateTargets
    {
        IReadOnlyList<IGateTarget> Current();
    }
}
