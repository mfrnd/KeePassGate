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
        /// For the approval dialog and nothing else. A dialog that identifies the database only by
        /// <see cref="DatabaseId"/> asks the operator to recognize sixteen hexadecimal characters
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
        /// **Throws `DatabaseShut` when the database closed under the caller**, a lock nearly
        /// always, which is answered as one rather than as there being no record.
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
        /// Two readers. A client's profiles are its record's less every name missing from here, so
        /// taking a name off the list takes it from every client without touching a record. And
        /// the approval dialog says which of the profiles being asked for are new.
        ///
        /// Empty when the list cannot be read, which is the safe direction for both: every client
        /// reaches nothing, and the operator is told there is something to create rather than not.
        /// </remarks>
        IReadOnlyList<string> KnownProfiles { get; }

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
        AdmissionWrite Admit(AdmissionRecord record);

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
        VaultWrite Revoke(string admissionKey);

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
