using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// The certificates whose renewals the operator refused in this session, and the standing
    /// answers given for longer that the database has not taken yet, in plugin memory and nowhere
    /// else.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §312. **Per certificate**, by admission key, as the stored answers are, so
    /// pairing again is a new certificate this says nothing about. **Under one lock**, because the
    /// handler that adds to it and the handlers that read it run on connection threads.
    ///
    /// **Nothing about this session is written to a database**, as nothing about an invitation is,
    /// and that is the point rather than a limitation: the answer is about this session, and an
    /// answer kept in the file would outlast it, in every copy of the file and through every merge.
    /// So it goes when KeePass exits, and a renewal asked for after that raises the dialog again.
    ///
    /// **An answer for longer is held here too while the database cannot take it**, which is the
    /// operator's ruling of 2026-10-14. Refused forever or until a date while KeePass was busy, it
    /// used to be lost with a line in the log, and the next renewal raised the dialog again. Now it
    /// takes effect at once, from here, like an answer about this session, and the plugin's next
    /// write to that database that lands writes it as well, through <see cref="IOwedAnswers"/>,
    /// after which the database's own copy is what answers. One still owed when KeePass exits is
    /// lost then, and the operator is told so on the way out rather than only in the log.
    ///
    /// **One for the plugin, shared by the gate and the KPG tab.** The renewal handler adds to it
    /// and asks it; the clients page shows what it holds beside each client, and takes one out
    /// when the operator allows renewal there and the settings dialog is accepted. Two of them
    /// would be a page lifting an answer the gate goes on giving.
    /// </remarks>
    public sealed class SessionRefusals : IOwedAnswers
    {
        private readonly object _lock = new object();
        private readonly HashSet<string> _refused = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, OwedAnswer> _owed = new Dictionary<string, OwedAnswer>(StringComparer.Ordinal);

        /// <summary>Refuses every later renewal of this certificate until KeePass exits.</summary>
        public void Refuse(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                throw new ArgumentException("An admission key is required.", nameof(admissionKey));
            }

            lock (_lock)
            {
                _refused.Add(admissionKey);
            }
        }

        /// <summary>
        /// Holds a standing answer for longer than this session that the database could not take
        /// when it was given, so that it is answered from here until it is written.
        /// </summary>
        public void Owe(string databaseId, string admissionKey, RenewalRefusal refusal)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                throw new ArgumentException("An admission key is required.", nameof(admissionKey));
            }

            if (refusal == null || !refusal.IsWritten)
            {
                throw new ArgumentException("Only an answer the database keeps is owed to it.", nameof(refusal));
            }

            lock (_lock)
            {
                _owed[admissionKey] = new OwedAnswer(databaseId, admissionKey, refusal);
            }
        }

        /// <summary>
        /// Takes back the answer about renewing this certificate, whichever this holds, so that its
        /// next renewal raises the dialog again, and says whether there was one.
        /// </summary>
        public bool Lift(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            lock (_lock)
            {
                var session = _refused.Remove(admissionKey);
                var owed = _owed.Remove(admissionKey);
                return session || owed;
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

        /// <summary>
        /// The answer this holds about renewing this certificate, or null: one still owed to the
        /// database before one for this session, since it says more.
        /// </summary>
        public RenewalRefusal Answer(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            lock (_lock)
            {
                OwedAnswer owed;
                if (_owed.TryGetValue(admissionKey, out owed))
                {
                    return owed.Refusal;
                }

                return _refused.Contains(admissionKey) ? RenewalRefusal.ThisSession() : null;
            }
        }

        /// <summary>Every answer still owed to a database.</summary>
        public IReadOnlyList<OwedAnswer> Owed()
        {
            lock (_lock)
            {
                return _owed.Values.ToList();
            }
        }

        /// <summary>
        /// Says an owed answer is in its database now, which then answers in its place. One given
        /// again since, which is a different answer, stays owed.
        /// </summary>
        public void Kept(OwedAnswer answer)
        {
            if (answer == null)
            {
                return;
            }

            lock (_lock)
            {
                OwedAnswer held;
                if (_owed.TryGetValue(answer.AdmissionKey, out held) && ReferenceEquals(held, answer))
                {
                    _owed.Remove(answer.AdmissionKey);
                }
            }
        }
    }
}
