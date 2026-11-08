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
    /// takes effect at once, from here, like an answer about this session, and the next save of
    /// that database that lands writes it as well, whoever asks for it, through
    /// <see cref="IOwedAnswers"/>, after which the database's own copy is what answers. One still owed when KeePass exits is
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
        private readonly HashSet<OwedAnswer> _withheld = new HashSet<OwedAnswer>();

        /// <summary>
        /// Raised once this holds an answer it did not hold before, on the thread that gave it and
        /// outside the lock.
        /// </summary>
        /// <remarks>
        /// **For the clients page**, which draws a client's name red from what this holds as it
        /// does from the database's standing answers, and is told when a standing answer lands.
        /// Nothing writes an answer for this session to a database, and an answer owed is written
        /// only later, so nothing else could tell the page of either: a renewal refused while the
        /// settings dialog showed it left the client's name black until something else drew the
        /// page again. `SessionAnswerNotice` carries it to KeePass's thread.
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
                Raise();
            }
        }

        /// <summary>
        /// Holds a standing answer for longer than this session that the database could not take
        /// when it was given, so that it is answered from here until it is written.
        /// </summary>
        /// <param name="baseline">
        /// What the database held about the certificate when the answer was given, or null for
        /// none: <see cref="OwedAnswer.Baseline"/>.
        /// </param>
        public void Owe(string databaseId, string admissionKey, RenewalRefusal refusal, RenewalRefusal baseline)
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
                _owed[admissionKey] = new OwedAnswer(databaseId, admissionKey, refusal, baseline);
            }

            Raise();
        }

        /// <summary>Tells whoever listens that this holds an answer it did not.</summary>
        private void Raise()
        {
            var answered = Answered;
            if (answered != null)
            {
                answered(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Takes back this session's answer about renewing this certificate, and the owed answer a
        /// clients page held back with <see cref="Withhold"/>, so that its next renewal raises the
        /// dialog again, and answers whether there was either.
        /// </summary>
        /// <remarks>
        /// **The owed answer that was held back and no other**: an answer given on a renewal dialog
        /// since the press is newer than the lift, and stands.
        /// </remarks>
        public bool Lift(string admissionKey, OwedAnswer withheld)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            lock (_lock)
            {
                var session = _refused.Remove(admissionKey);
                var owed = false;

                OwedAnswer held;
                if (withheld != null && _owed.TryGetValue(admissionKey, out held) && ReferenceEquals(held, withheld))
                {
                    owed = _owed.Remove(admissionKey);
                }

                if (withheld != null)
                {
                    _withheld.Remove(withheld);
                }

                return session || owed;
            }
        }

        /// <summary>
        /// Holds the owed answer about renewing this certificate back from every write until it is
        /// lifted or released, and answers with it, or null when none is owed.
        /// </summary>
        /// <remarks>
        /// **The clients page's Allow renewal**, which waits for the settings dialog's OK. An owed
        /// answer has no key in the database yet, so the press buffers nothing there, and a write
        /// that landed before OK wrote the answer and let it go from here: OK then found nothing to
        /// lift, and the client stayed refused. Held back, it is written by no write meanwhile,
        /// and it still answers, so the gate goes on refusing until OK, as the page promises.
        /// </remarks>
        public OwedAnswer Withhold(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            lock (_lock)
            {
                OwedAnswer owed;
                if (!_owed.TryGetValue(admissionKey, out owed))
                {
                    return null;
                }

                _withheld.Add(owed);
                return owed;
            }
        }

        /// <summary>
        /// Gives an answer held back by <see cref="Withhold"/> back to the next write.
        /// </summary>
        public void Release(OwedAnswer withheld)
        {
            if (withheld == null)
            {
                return;
            }

            lock (_lock)
            {
                _withheld.Remove(withheld);
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
        /// The answer this holds about renewing this certificate that refuses at this moment, or
        /// null: an answer still owed to the database before this session's answer, since it says
        /// more.
        /// </summary>
        /// <remarks>
        /// **Asked of the moment**, because an owed answer until a date stops refusing once its day
        /// begins and nothing takes it out of memory then. Answered whether or not it still
        /// refused, it stood in front of this session's answer, and both readers let the passed
        /// date go and found nothing: the renewal raised the dialog again on every request, and
        /// the clients page showed the client as refused by nothing.
        /// </remarks>
        public RenewalRefusal Answer(string admissionKey, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            lock (_lock)
            {
                OwedAnswer owed;
                if (_owed.TryGetValue(admissionKey, out owed) && owed.Refusal.Refuses(now))
                {
                    return owed.Refusal;
                }

                return _refused.Contains(admissionKey) ? RenewalRefusal.ThisSession() : null;
            }
        }

        /// <summary>
        /// Whether this holds any answer about renewing this certificate, an answer owed until a
        /// date that has passed among them, which is what there is to lift.
        /// </summary>
        public bool Holds(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            lock (_lock)
            {
                return _refused.Contains(admissionKey) || _owed.ContainsKey(admissionKey);
            }
        }

        /// <summary>
        /// Every answer still owed to a database, less the answers a clients page holds back.
        /// </summary>
        public IReadOnlyList<OwedAnswer> Owed()
        {
            lock (_lock)
            {
                return _owed.Values.Where(answer => !_withheld.Contains(answer)).ToList();
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
