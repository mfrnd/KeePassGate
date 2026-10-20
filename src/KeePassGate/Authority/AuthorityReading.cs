using System;
using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>Whether a database carries an authority, and whether it is whole.</summary>
    public enum AuthorityPresence
    {
        /// <summary>Nothing of this plugin's is in the database.</summary>
        None,

        /// <summary>Something is there and cannot be used. Never treated as absence.</summary>
        Damaged,

        /// <summary>All three parts are present and readable.</summary>
        Present,

        /// <summary>
        /// More than one authority, none of them retired: copies of the database each made their
        /// own and a merge brought them together. Nothing is served until the operator keeps one.
        /// </summary>
        Undecided
    }

    /// <summary>
    /// What a read of the store found. Damage is a state of its own rather than a failure to
    /// report, because the operator's answer differs: an absent authority is created, a damaged one
    /// has to be recreated, and telling the two apart is the difference between one click and a
    /// support question.
    ///
    /// **Several authorities are a state of their own as well**, rather than a kind of damage.
    /// Nothing in them is wrong, and recreating is the wrong answer to them: what the operator has
    /// to do is say which one this database goes on being, so the reading carries every candidate
    /// and whatever asks can show what there is to choose between.
    /// </summary>
    public sealed class AuthorityReading : IDisposable
    {
        private readonly AuthorityPresence _presence;
        private readonly AuthorityMaterial _material;
        private readonly List<string> _problems;
        private readonly List<AuthorityCandidate> _candidates;
        private readonly int _clientRecordCount;
        private readonly bool _clientsComplete;

        private AuthorityReading(
            AuthorityPresence presence,
            AuthorityMaterial material,
            IEnumerable<string> problems,
            IEnumerable<AuthorityCandidate> candidates,
            int clientRecordCount,
            bool clientsComplete)
        {
            _presence = presence;
            _material = material;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
            _candidates = candidates == null
                ? new List<AuthorityCandidate>()
                : new List<AuthorityCandidate>(candidates);
            _clientRecordCount = clientRecordCount;
            _clientsComplete = clientsComplete;
        }

        public AuthorityPresence Presence
        {
            get { return _presence; }
        }

        /// <summary>Null unless <see cref="Presence"/> is <see cref="AuthorityPresence.Present"/>.</summary>
        public AuthorityMaterial Material
        {
            get { return _material; }
        }

        /// <summary>What is wrong, in words an operator can act on. Empty when nothing is.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return _problems; }
        }

        /// <summary>
        /// The authorities to choose between, in the order of their digests. Empty unless
        /// <see cref="Presence"/> is <see cref="AuthorityPresence.Undecided"/>.
        /// </summary>
        public IReadOnlyList<AuthorityCandidate> Candidates
        {
            get { return _candidates; }
        }

        /// <summary>
        /// Whether the admission records could be listed at all. False says nothing about how many
        /// there are, only that `ClientRecordCount` is not an answer to that question.
        /// </summary>
        public bool ClientsComplete
        {
            get { return _clientsComplete; }
        }

        /// <summary>How many admission records are stored, whatever the state of the authority.</summary>
        public int ClientRecordCount
        {
            get { return _clientRecordCount; }
        }

        public bool IsPresent
        {
            get { return _presence == AuthorityPresence.Present; }
        }

        public static AuthorityReading Absent(int clientRecordCount, bool clientsComplete)
        {
            return new AuthorityReading(
                AuthorityPresence.None, null, null, null, clientRecordCount, clientsComplete);
        }

        public static AuthorityReading Damaged(
            IEnumerable<string> problems, int clientRecordCount, bool clientsComplete)
        {
            return new AuthorityReading(
                AuthorityPresence.Damaged, null, problems, null, clientRecordCount, clientsComplete);
        }

        public static AuthorityReading Whole(
            AuthorityMaterial material, int clientRecordCount, bool clientsComplete)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            return new AuthorityReading(
                AuthorityPresence.Present, material, null, null, clientRecordCount, clientsComplete);
        }

        /// <summary>
        /// Several authorities and no decision between them. The reading owns the candidates and
        /// disposes them with itself.
        /// </summary>
        public static AuthorityReading Undecided(
            IEnumerable<AuthorityCandidate> candidates, int clientRecordCount, bool clientsComplete)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            return new AuthorityReading(
                AuthorityPresence.Undecided, null, null, candidates, clientRecordCount, clientsComplete);
        }

        public void Dispose()
        {
            if (_material != null)
            {
                _material.Dispose();
            }

            foreach (var candidate in _candidates)
            {
                candidate.Dispose();
            }
        }
    }
}
