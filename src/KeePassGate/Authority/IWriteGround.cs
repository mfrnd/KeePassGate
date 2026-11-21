using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// What an admission's write is held to in its own turn on KeePass's thread: the admissions,
    /// the profile names, their removals and the clients holding them that the database's store
    /// holds, read from that store in that turn.
    /// </summary>
    /// <remarks>
    /// Asked by the condition a pairing or a renewal hands the writer, last of the questions and
    /// first of the changes, so that what it compares cannot move before the write that relies
    /// on it. **The authority is not among them**: the write's plan holds the admission to the
    /// authority that issued its certificate standing alone before the condition is asked, and
    /// a write whose plan leaves it out is answered for that, `PAIRING.md` §311.
    /// </remarks>
    public interface IWriteGround
    {
        /// <summary>
        /// The admission of this certificate where it counts, read from the store in the write's
        /// own turn, or null: what a renewal is held to, `STORE.md` §627.
        /// </summary>
        AdmissionRecord Admission(string admissionKey);

        /// <summary>
        /// Whether this profile is on the list, read from the store in the write's own turn,
        /// `STORE.md` §616: what a held approval is compared with, name by name.
        /// </summary>
        bool Named(string profile);

        /// <summary>
        /// The removals of this profile that an addition written in this turn names, read from the
        /// store in the write's own turn, `StoreInForce.RemovalsOnceKnown`: what a held approval of
        /// a name off the list is compared with, `PAIRING.md` §311.
        /// </summary>
        IReadOnlyCollection<string> Removals(string profile);

        /// <summary>
        /// Of each of these profiles, the client keys of the clients that regain the profile were
        /// it put back on the list in this turn, a profile no client regains absent: read from a
        /// single listing of the admissions the store holds in the write's own turn,
        /// `AskedProfiles.Holding`, which is what a held approval of names off the list is
        /// compared with, `PAIRING.md` §311.
        /// </summary>
        /// <remarks>
        /// **A single listing for every name**, since the write asks it under the writer's lock on
        /// KeePass's thread, and a listing for each of the names an approval holds is that many
        /// walks of every admission.
        /// </remarks>
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> Regaining(IReadOnlyCollection<string> profiles);
    }
}
