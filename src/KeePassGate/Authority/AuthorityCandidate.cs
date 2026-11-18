using System;
using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// One of several authorities a database holds, none of them retired, while the operator has
    /// not yet said which to keep.
    /// </summary>
    /// <remarks>
    /// **Named by its record's `id`**, which is what a choice retires the others by. An authority
    /// that will not read is still a candidate, because it is still an authority nobody has
    /// retired: it carries its problems rather than its material, and it can be retired by keeping
    /// another but never kept itself. **The name is the `id` as the record holds it**, a text the
    /// store's core does not look into, `STORE.md` §658, so an `id` that is empty or not
    /// hexadecimal, which nothing this plugin writes and a crafted `.kdbx` can hold, names a
    /// damaged candidate like any other: refused here, it threw out of reading the database.
    /// </remarks>
    public sealed class AuthorityCandidate : IDisposable
    {
        private readonly string _fingerprint;
        private readonly AuthorityMaterial _material;
        private readonly List<string> _problems;

        private AuthorityCandidate(string fingerprint, AuthorityMaterial material, IEnumerable<string> problems)
        {
            if (fingerprint == null)
            {
                throw new ArgumentNullException(nameof(fingerprint), "A candidate is named by its digest.");
            }

            _fingerprint = fingerprint;
            _material = material;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
        }

        /// <summary>
        /// Its record's `id`: lowercase hex for every authority this plugin wrote, and whatever a
        /// record holds otherwise, empty included.
        /// </summary>
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
