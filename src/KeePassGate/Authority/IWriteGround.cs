namespace KeePassGate.Authority
{
    /// <summary>
    /// What an admission's write is held to in its own turn on KeePass's thread: the admissions the
    /// database's store holds, read from that store in that turn, and where the profile names are
    /// kept.
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

        /// <summary>The database's own custom data, which holds the profile names.</summary>
        IKeyValueStore Names { get; }
    }
}
