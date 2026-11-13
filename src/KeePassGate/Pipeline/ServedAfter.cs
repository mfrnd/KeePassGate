using System;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Takes the part of a read's answer that is made once the hold of KeePass's thread has
    /// ended.
    /// </summary>
    /// <remarks>
    /// **A read is decided inside one hold, and an entry's values are read after it.** Inside
    /// the hold the caller's record is read again, the node is found, the rules are asked and
    /// the caller's view of the entry is taken, `VaultEntryView`. The view keeps the string and
    /// attachment objects the entry held at that moment, and KeePass replaces those objects
    /// and changes none of them in place, so a value read from the view later is the value the
    /// entry held when the view was taken, whatever the operator has done to the entry since.
    /// So measuring the values, reading them, digesting them into the tag, asking `If-Match` of
    /// that tag and writing the answer need no hold, and each of them grows with the entry,
    /// which no write is refused for the size of. Left inside, a tag over fifty megabytes of
    /// strings held KeePass's thread for about forty-five milliseconds a request,
    /// `FINDINGS.md`.
    ///
    /// **Nothing left here may read the database.** No lookup, no walk of the rules and no
    /// node's name: only the view, and what the hold already decided. A line for the audit that
    /// depends on the rules is composed inside the hold and kept, and what is left here only
    /// adds it to the request's lines, <see cref="ProtectedWithheld"/>.
    ///
    /// Left by the router for a request it decides inside a hold, at `SpecRouter.AfterItem`. A
    /// request with nothing left there has no hold to wait for, and its handler runs the same
    /// work where it stands.
    /// </remarks>
    /// <param name="serve">What makes the answer, from the view and nothing else.</param>
    public delegate void ServedAfter(Action serve);
}
