using System;
using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>Whether a database carries an authority, and whether it is whole.</summary>
    public enum AuthorityPresence
    {
        /// <summary>
        /// No authority: no authority has been made yet, or every authority made has been retired,
        /// which <see cref="AuthorityReading.IsRetired"/> tells apart.
        /// </summary>
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
        private readonly bool _retired;

        private AuthorityReading(
            AuthorityPresence presence,
            AuthorityMaterial material,
            IEnumerable<string> problems,
            IEnumerable<AuthorityCandidate> candidates,
            int clientRecordCount,
            bool retired)
        {
            _presence = presence;
            _material = material;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
            _candidates = candidates == null
                ? new List<AuthorityCandidate>()
                : new List<AuthorityCandidate>(candidates);
            _clientRecordCount = clientRecordCount;
            _retired = retired;
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

        /// <summary>How many admission records are stored, whatever the state of the authority.</summary>
        public int ClientRecordCount
        {
            get { return _clientRecordCount; }
        }

        public bool IsPresent
        {
            get { return _presence == AuthorityPresence.Present; }
        }

        /// <summary>
        /// Whether a database with no authority has held an authority and retired every authority
        /// it held. Only a <see cref="AuthorityPresence.None"/> reading is ever retired, and nothing
        /// is served either way: what differs is what the operator is told, which is not that there
        /// has never been an authority.
        /// </summary>
        public bool IsRetired
        {
            get { return _retired; }
        }

        public static AuthorityReading Absent(int clientRecordCount)
        {
            return new AuthorityReading(
                AuthorityPresence.None, null, null, null, clientRecordCount, false);
        }

        /// <summary>
        /// No authority, in a database whose every authority is retired: deleted, or retired by
        /// copies that each kept a different authority, once their answers were synchronized.
        /// </summary>
        public static AuthorityReading Retired(int clientRecordCount)
        {
            return new AuthorityReading(
                AuthorityPresence.None, null, null, null, clientRecordCount, true);
        }

        public static AuthorityReading Damaged(
            IEnumerable<string> problems, int clientRecordCount)
        {
            return new AuthorityReading(
                AuthorityPresence.Damaged, null, problems, null, clientRecordCount, false);
        }

        public static AuthorityReading Whole(
            AuthorityMaterial material, int clientRecordCount)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            return new AuthorityReading(
                AuthorityPresence.Present, material, null, null, clientRecordCount, false);
        }

        /// <summary>
        /// Several authorities and no decision between them. The reading owns the candidates and
        /// disposes them with itself.
        /// </summary>
        public static AuthorityReading Undecided(
            IEnumerable<AuthorityCandidate> candidates, int clientRecordCount)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            return new AuthorityReading(
                AuthorityPresence.Undecided, null, null, candidates, clientRecordCount, false);
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
