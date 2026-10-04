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
        HostKeyMaterial Host { get; }

        /// <summary>The authority every client certificate has to chain to.</summary>
        X509Certificate2 Authority { get; }

        /// <summary>
        /// The record admitting this certificate, or null when there is none.
        /// </summary>
        /// <remarks>
        /// Read from the database on every call rather than cached with the certificates, because
        /// a revocation beside the record has to take effect on the next connection
        /// rather than on the next time something else changed.
        ///
        /// **Throws `VaultUnsettled` when the records could not be listed**, which is not the same
        /// as there being none: answered as none, a write landing at the same instant told an
        /// admitted client it was never admitted. **And `DatabaseShut` when the database closed
        /// under the caller**, a lock nearly always, which is answered as one.
        /// </remarks>
        AdmissionRecord Admission(string admissionKey);

        /// <summary>
        /// Why a record cannot be written right now, or null when one can. Checked before an
        /// invitation is spent and before the operator is asked, so that Database Maintenance
        /// left open on this database costs a pairing attempt rather than the invitation behind it.
        /// </summary>
        string AdmissionBlocked { get; }

        /// <summary>
        /// The profiles this database names now, read on every call rather than cached.
        /// </summary>
        /// <remarks>
        /// Three readers. A client's profiles are its record's less every name missing from here, so
        /// taking a name off the list takes it from every client without touching a record. The
        /// pairing dialog says which of the profiles being asked for are new. And a renewal offers
        /// only the record's names the list still holds, and says which it no longer does.
        ///
        /// Empty when the list cannot be read, which is the safe direction for the first reader:
        /// every client reaches nothing. Not for the other two, which write the list or a record
        /// from it, so both ask <see cref="ProfilesUnreadable"/> first and refuse.
        /// </remarks>
        IReadOnlyList<string> KnownProfiles { get; }

        /// <summary>
        /// Why the profiles this database names cannot be read now, or null when they can, an
        /// empty list included.
        /// </summary>
        /// <remarks>
        /// For the readers for which empty is not the safe direction: a renewal writes a record from
        /// the names on the list, so a list that would not read would have been written into the
        /// record as no names at all, and the client would have lost them for good; and a pairing
        /// writes the names it approves onto the list, which read as empty would have replaced
        /// every name the damaged list held.
        /// </remarks>
        string ProfilesUnreadable { get; }

        /// <summary>
        /// The admitted clients whose records hold a profile, read now. Empty when none do or the
        /// records cannot be read.
        /// </summary>
        /// <remarks>
        /// For the approval dialog. A record keeps every name it was approved for, so a profile
        /// the operator removed is still held, granting nothing, by every client approved for it
        /// before; approving a pairing for that name puts it back on the list and gives it back
        /// to all of them. The dialog names them, so that is not a side effect nobody saw.
        /// </remarks>
        IReadOnlyList<AdmissionRecord> Holding(string profile);

        /// <summary>
        /// Writes an admission record and makes it durable, on whichever thread owns the database.
        /// </summary>
        /// <remarks>
        /// The one write the transport ever asks for, and it marshals synchronously like every
        /// write this plugin makes, so what it answers is whether the record is on disk. It is
        /// checked again inside the write, because the answer to <see cref="AdmissionBlocked"/>
        /// can change between asking and writing, and a pairing that cannot land its record has
        /// to fail rather than hand over a certificate nobody can see.
        /// </remarks>
        /// <param name="condition">
        /// Why the record may no longer be written, or null when it may, asked of the database's
        /// own custom data on KeePass's thread, under the writer's lock, once the database is known
        /// to be writable and before anything is changed. Where an approval is held, spent and
        /// compared with what it was given against. A refusal is answered as withdrawn. Null for
        /// none.
        /// </param>
        /// <param name="audit">
        /// The audit line for the record, which the write itself writes once the record has landed
        /// and before anything else is told, so that nothing after the write can lose it. Run once,
        /// on KeePass's own thread, where it must not wait; never when the record did not land, so
        /// the caller writes no line of its own about it. Null for no line.
        /// </param>
        AdmissionWrite Admit(AdmissionRecord record, Func<IKeyValueStore, string> condition, Action audit);

        /// <summary>
        /// Revokes the client holding this admission key, at that client's own request, and makes
        /// the revocation durable the way every change is.
        /// </summary>
        /// <remarks>
        /// What the Clients page writes, a revocation beside the record and the record emptied,
        /// marshaled and saved like <see cref="Admit"/> and answered with what happened to it:
        /// done, busy, not saved and taken back out, saved and not verified, or failed. Nothing to
        /// revoke is answered as missing, which is a record that went before the write could make
        /// it.
        /// </remarks>
        /// <param name="audit">
        /// The audit line for the revocation, written as <see cref="Admit"/> writes a record's:
        /// once, by the write itself, whenever the revocation is left in force, saved and verified
        /// or saved and not verified, and never otherwise. Null for no line.
        /// </param>
        VaultWrite Revoke(string admissionKey, Action audit);

        /// <summary>
        /// The operator's standing answer to renewing this certificate, read now, or null when
        /// there is none.
        /// </summary>
        RenewalRefusal StandingRefusal(string admissionKey);

        /// <summary>
        /// Writes the operator's standing answer to renewing this certificate, and makes it durable
        /// the way every change is.
        /// </summary>
        /// <remarks>
        /// Asked for in the same breath as the refusal it belongs to, and answered with what came
        /// of it; the refusal stands whatever this answers, and a failure costs only the standing
        /// answer, which the caller says in the log.
        /// </remarks>
        VaultWrite RefuseRenewal(string admissionKey, RenewalRefusal refusal);

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
        /// The one word every connection to this database is answered with under `503`, or null
        /// when it is served.
        /// </summary>
        /// <remarks>
        /// `locked` for a locked database, and `undecided` for one holding several authorities
        /// while the operator has not chosen between them. Both are answered before a request is
        /// judged at all, because each has the same answer for every path, method and body.
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
