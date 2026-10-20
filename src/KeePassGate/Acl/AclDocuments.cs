using System;
using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The rules documents one request has already read, so that each is parsed once.
    /// </summary>
    /// <remarks>
    /// **For a listing, which asks about every node it walks.** Every walk from a node to the root
    /// reads each document on the way, so a listing of twenty thousand entries under one rule at
    /// the root parsed that rule twenty thousand times, and more for every profile a client holds.
    /// Nearly all of them are the same few documents, and this answers the second time a document
    /// is asked for with what the first time found.
    ///
    /// **Keyed by the text, compared byte for byte**, so a rule changed while the listing is being
    /// written is a different key and is read again: nothing here can answer with a document that
    /// is no longer the one on the node. A document that will not read is remembered as that too,
    /// and refuses wherever it is met, exactly as it does read afresh.
    ///
    /// Held for one request, on the thread serving it, and never shared.
    /// </remarks>
    public sealed class AclDocuments
    {
        private readonly Dictionary<string, AclDocument> _read =
            new Dictionary<string, AclDocument>(StringComparer.Ordinal);

        /// <summary>The document this text is, or null when it will not read.</summary>
        public AclDocument Read(string text)
        {
            AclDocument document;
            if (!_read.TryGetValue(text, out document))
            {
                document = AclDocument.Read(text);
                _read[text] = document;
            }

            return document;
        }
    }
}
