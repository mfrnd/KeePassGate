namespace KeePassGate.Authority
{
    /// <summary>
    /// What an admission's write is held to in its own turn on KeePass's thread: the authority the
    /// database serves, read from its store in that turn, and where the records and the profile
    /// names are kept.
    /// </summary>
    /// <remarks>
    /// Asked by the condition a pairing or a renewal hands the writer, last of the questions and
    /// first of the changes, so that what it compares cannot move before the write that relies
    /// on it.
    /// </remarks>
    public interface IWriteGround
    {
        /// <summary>
        /// The `id` of the authority the database's store serves, or null where it serves none:
        /// a record written under any other is of a certificate that chains to nothing the gate serves.
        /// </summary>
        string Served { get; }

        /// <summary>The database's own custom data, which holds the admission records and the profile names.</summary>
        IKeyValueStore Names { get; }
    }
}
