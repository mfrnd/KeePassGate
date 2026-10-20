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
        Present
    }

    /// <summary>
    /// What a read of the store found. Damage is a state of its own rather than a failure to
    /// report, because the operator's answer differs: an absent authority is created, a damaged one
    /// has to be recreated, and telling the two apart is the difference between one click and a
    /// support question.
    /// </summary>
    public sealed class AuthorityReading : IDisposable
    {
        private readonly AuthorityPresence _presence;
        private readonly AuthorityMaterial _material;
        private readonly List<string> _problems;
        private readonly int _clientRecordCount;

        private AuthorityReading(
            AuthorityPresence presence, AuthorityMaterial material, IEnumerable<string> problems, int clientRecordCount)
        {
            _presence = presence;
            _material = material;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
            _clientRecordCount = clientRecordCount;
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

        /// <summary>How many admission records are stored, whatever the state of the authority.</summary>
        public int ClientRecordCount
        {
            get { return _clientRecordCount; }
        }

        public bool IsPresent
        {
            get { return _presence == AuthorityPresence.Present; }
        }

        public static AuthorityReading Absent(int clientRecordCount)
        {
            return new AuthorityReading(AuthorityPresence.None, null, null, clientRecordCount);
        }

        public static AuthorityReading Damaged(IEnumerable<string> problems, int clientRecordCount)
        {
            return new AuthorityReading(AuthorityPresence.Damaged, null, problems, clientRecordCount);
        }

        public static AuthorityReading Whole(AuthorityMaterial material, int clientRecordCount)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            return new AuthorityReading(AuthorityPresence.Present, material, null, clientRecordCount);
        }

        public void Dispose()
        {
            if (_material != null)
            {
                _material.Dispose();
            }
        }
    }
}
