using System;
using System.Collections.Generic;
using KeePassGate.Authority;

namespace KeePassGate.Gate
{
    /// <summary>
    /// The admission of a certificate as the gate reads it, with the profiles the database names
    /// in the same reading.
    /// </summary>
    /// <remarks>
    /// **A single reading for both**, so that the names a client is granted and the names it is
    /// refused for are two halves of a single list, and nothing lands between the admission and
    /// the names. Read in two holds, a lock landing between them would meet a database already
    /// shut, whose list names nothing, and the client would be told that every profile it holds
    /// had been taken away; and a hold of its own for the names would hold KeePass's thread once
    /// more at every connection.
    /// </remarks>
    public sealed class Admitted
    {
        /// <summary>An admission and what the database names in the reading that found it.</summary>
        /// <param name="named">The profiles the database names, none where it names none.</param>
        /// <param name="unreadable">
        /// Why the profiles the database names would not read in that reading, or null where they
        /// read, an empty list included.
        /// </param>
        public Admitted(AdmissionRecord record, IReadOnlyList<string> named, string unreadable)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            Record = record;
            Named = named ?? Array.Empty<string>();
            Unreadable = unreadable;
        }

        /// <summary>The admission of the certificate.</summary>
        public AdmissionRecord Record { get; private set; }

        /// <summary>
        /// The profiles the database names, read in the reading that found the admission. Empty
        /// where the list will not read, and without the names it cannot hold where it holds some
        /// it can: a client reaches no profile the reading left out.
        /// </summary>
        public IReadOnlyList<string> Named { get; private set; }

        /// <summary>
        /// Why the profiles the database names would not read in that reading, or null where they
        /// read: what tells the names a damaged list lost from the names the operator removed.
        /// </summary>
        public string Unreadable { get; private set; }
    }
}
