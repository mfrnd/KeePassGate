using System;
using System.Collections.Generic;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// The certificates whose renewals stand refused until KeePass exits, in plugin memory and
    /// nowhere else.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §312. **Per certificate**, by admission key, as the stored answers are, so
    /// pairing again is a new certificate this says nothing about. **Under one lock**, because the
    /// handler that adds to it and the handlers that read it run on connection threads.
    ///
    /// **Nothing here is written to a database**, as nothing about an invitation is, and that is
    /// the point rather than a limitation: the answer is about this session, and an answer kept in
    /// the file would outlast it, in every copy of the file and through every merge. So it goes
    /// when KeePass exits, and a renewal asked for after that raises the dialog again.
    ///
    /// **Two answers end up here**: "Refuse in this session", and an answer forever or until a day
    /// whose write the writer refused, or whose save did not land, which the operator is told of
    /// at once. Nothing here waits for a save of any database.
    ///
    /// **One for the plugin, shared by the gate and the KPG tab.** The renewal handler adds to it
    /// and asks it; the clients page shows what it holds beside each client, and takes one out
    /// when the operator allows renewal there and the settings dialog is accepted. Two of them
    /// would be a page lifting an answer the gate goes on giving.
    /// </remarks>
    public sealed class SessionRefusals
    {
        private readonly object _lock = new object();
        private readonly HashSet<string> _refused = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Raised once this holds an answer it did not hold before, on the thread that gave it and
        /// outside the lock.
        /// </summary>
        /// <remarks>
        /// **For the clients page**, which draws a client's name red from what this holds as it
        /// does from the database's standing answers, and is told when a standing answer lands.
        /// Nothing writes an answer held here to a database, so nothing else could tell the page
        /// of it: a renewal refused that way while the settings dialog showed it left the client's
        /// name black until something else drew the page again. `SessionAnswerNotice` carries it
        /// to KeePass's thread.
        /// </remarks>
        public event EventHandler Answered;

        /// <summary>Refuses every later renewal of this certificate until KeePass exits.</summary>
        public void Refuse(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                throw new ArgumentException("An admission key is required.", nameof(admissionKey));
            }

            bool added;
            lock (_lock)
            {
                added = _refused.Add(admissionKey);
            }

            if (added)
            {
                var answered = Answered;
                if (answered != null)
                {
                    answered(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Takes back the answer a clients page lifted about renewing this certificate, so that its
        /// next renewal raises the dialog again, and answers whether there was such an answer.
        /// </summary>
        public bool Lift(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            lock (_lock)
            {
                return _refused.Remove(admissionKey);
            }
        }

        /// <summary>Whether renewals of this certificate are refused until KeePass exits.</summary>
        public bool Refuses(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            lock (_lock)
            {
                return _refused.Contains(admissionKey);
            }
        }
    }
}
