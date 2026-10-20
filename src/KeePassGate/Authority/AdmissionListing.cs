using System;
using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The admitted clients that could be read, and whether that is all of them.
    /// </summary>
    /// <remarks>
    /// A store that will not answer and a database that has admitted nobody both produce no
    /// records, and telling them apart only in the log made them one answer to every caller. On
    /// the gate that costs nothing, because the safe reply to "may this certificate in" is no
    /// either way. Wherever the count is shown to a person it is not: an empty list reads as
    /// nobody being admitted, which is a sentence about who has access, and saying it when
    /// nothing could be counted tells the operator something false about their own file.
    ///
    /// **Deliberately not a list.** Being enumerable would let every caller written against the
    /// old answer keep working untouched, which is convenient and is exactly the failure: the
    /// next person wanting the records writes a `foreach` and never learns there is a question to
    /// ask. Reaching them through `Records` costs one word and puts `Complete` in front of
    /// whoever reads it. Most callers go on ignoring it, which is right for them: one that only
    /// warns under-reports on a failed listing, and that is a warning going missing rather than a
    /// claim that is wrong.
    /// Only the screen stating how many clients there are turns it into a lie.
    /// </remarks>
    public sealed class AdmissionListing
    {
        private readonly IReadOnlyList<AdmissionRecord> _records;
        private readonly int _cameBack;
        private readonly bool _complete;

        public AdmissionListing(IReadOnlyList<AdmissionRecord> records, bool complete)
            : this(records, 0, complete)
        {
        }

        public AdmissionListing(IReadOnlyList<AdmissionRecord> records, int cameBack, bool complete)
        {
            _records = records ?? Array.Empty<AdmissionRecord>();
            _cameBack = Math.Max(0, cameBack);
            _complete = complete;
        }

        /// <summary>Everything that was listed, could be read, and admits its client.</summary>
        public IReadOnlyList<AdmissionRecord> Records
        {
            get { return _records; }
        }

        /// <summary>
        /// How many records admit nobody because the client was revoked and a merge brought the
        /// record back beside its revocation.
        /// </summary>
        /// <remarks>
        /// A number rather than records, because nothing is done to them one by one: the next
        /// write this plugin makes takes them away, `AdmissionStore.PruneCameBack` says which. It
        /// is kept at all because the clients page is where an operator learns that a copy of the
        /// file older than a revocation was merged in, and says so under the list until then.
        /// TODO(step 8): the clients page arrives with the invitation control.
        /// </remarks>
        public int CameBack
        {
            get { return _cameBack; }
        }

        /// <summary>
        /// Whether the listing itself was read. False says nothing about how many clients the
        /// database has admitted, only that this does not know.
        /// </summary>
        public bool Complete
        {
            get { return _complete; }
        }
    }
}
