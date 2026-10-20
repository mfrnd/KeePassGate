using System;
using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// One of several authorities a database holds, none of them retired, while the operator has
    /// not yet said which to keep.
    /// </summary>
    /// <remarks>
    /// **Named by the digest its key is filed under**, which is what a choice retires the others
    /// by. A document that will not read is still a candidate, because it is still an authority
    /// nobody has retired: it carries its problems rather than its material, and it can be retired
    /// by keeping another but never kept itself.
    /// </remarks>
    public sealed class AuthorityCandidate : IDisposable
    {
        private readonly string _fingerprint;
        private readonly AuthorityMaterial _material;
        private readonly List<string> _problems;

        private AuthorityCandidate(string fingerprint, AuthorityMaterial material, IEnumerable<string> problems)
        {
            if (string.IsNullOrEmpty(fingerprint))
            {
                throw new ArgumentException("A candidate is named by its digest.", nameof(fingerprint));
            }

            _fingerprint = fingerprint;
            _material = material;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
        }

        /// <summary>The digest its key is filed under, lowercase hex.</summary>
        public string Fingerprint
        {
            get { return _fingerprint; }
        }

        /// <summary>Null when the document will not read, and <see cref="Problems"/> says why.</summary>
        public AuthorityMaterial Material
        {
            get { return _material; }
        }

        /// <summary>What is wrong with it, in words an operator can act on. Empty when nothing is.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return _problems; }
        }

        /// <summary>Whether it could be kept: whole, and readable.</summary>
        public bool IsWhole
        {
            get { return _material != null; }
        }

        public static AuthorityCandidate Whole(string fingerprint, AuthorityMaterial material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            return new AuthorityCandidate(fingerprint, material, null);
        }

        public static AuthorityCandidate Damaged(string fingerprint, IEnumerable<string> problems)
        {
            return new AuthorityCandidate(fingerprint, null, problems);
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
