using System;
using System.Security.Cryptography;
using KeePassGate.Gate;
using KeePassGate.Diagnostics;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// One outstanding invitation: a token, the database it is for, and how long it has left.
    /// </summary>
    /// <remarks>
    /// **This is never written to the database, and that is the whole design.** `PAIRING.md`
    /// §307: a token stored in custom data can be brought back to life by a dialog writing a
    /// stale snapshot over the group, which resurrects a single-use credential after it has been
    /// spent. A token that was never in custom data cannot be restored by one.
    ///
    /// It carries at most one outstanding challenge, because the proof exchange is two requests and
    /// the transport serves one request per connection. A second hello replaces the first, and a
    /// challenge is spent the moment it is used.
    /// </remarks>
    public sealed class Invitation : IDisposable
    {
        private readonly string _id;
        private readonly string _databaseId;
        private readonly DateTimeOffset _created;
        private readonly DateTimeOffset _expires;
        private readonly byte[] _token;
        private readonly object _lock = new object();

        private PairingChallenge _challenge;
        private bool _disposed;

        public Invitation(
            string id, string databaseId, byte[] token, DateTimeOffset created, DateTimeOffset expires)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An invitation is named so a log can mention it.", nameof(id));
            }

            if (string.IsNullOrEmpty(databaseId))
            {
                throw new ArgumentException("An invitation belongs to one database.", nameof(databaseId));
            }

            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token.Length != PairingToken.Length)
            {
                throw new ArgumentException(
                    "A token is " + PairingToken.Length + " bytes, not " + token.Length + ".", nameof(token));
            }

            _id = id;
            _databaseId = databaseId;
            _token = (byte[])token.Clone();
            _created = created;
            _expires = expires;
        }

        /// <summary>
        /// Names it in a log and on screen without naming the token. Random rather than derived,
        /// because anything derived from the token is a thing to be careful with and this is not.
        /// </summary>
        /// <remarks>
        /// **This is what an approval dialog shows, and the tab shows it beside the code**, so the
        /// two can be checked against each other. The code itself is shown once: it is a single use
        /// credential, and a second place to read it off is a second place to lose it from.
        /// TODO(step 8): the approval dialog and the invitation control arrive with this step.
        /// </remarks>
        public string Id
        {
            get { return _id; }
        }

        /// <summary>The authority digest of the database this may pair with, and no other.</summary>
        public string DatabaseId
        {
            get { return _databaseId; }
        }

        public DateTimeOffset Created
        {
            get { return _created; }
        }

        public DateTimeOffset Expires
        {
            get { return _expires; }
        }

        /// <summary>A copy, which the caller may zero. Null once the invitation has been disposed.</summary>
        public byte[] Token
        {
            get
            {
                lock (_lock)
                {
                    return _disposed ? null : (byte[])_token.Clone();
                }
            }
        }

        /// <summary>The token as the operator reads it out.</summary>
        public string Text
        {
            get
            {
                lock (_lock)
                {
                    return _disposed ? string.Empty : PairingToken.Encode(_token);
                }
            }
        }

        public bool HasExpired(DateTimeOffset now)
        {
            return now >= _expires;
        }

        /// <summary>
        /// A name this invitation answers to, which is what a client puts in its ClientHello. Only
        /// tests build one: a real client is given the token and derives its own.
        /// </summary>
        public string NewName()
        {
            var token = Token;
            if (token == null)
            {
                return null;
            }

            try
            {
                return SniName.For(token);
            }
            finally
            {
                Array.Clear(token, 0, token.Length);
            }
        }

        /// <summary>Whether this name was built from this token, compared in fixed time.</summary>
        public bool Answers(string serverName)
        {
            var token = Token;
            if (token == null)
            {
                return false;
            }

            try
            {
                return SniName.Matches(serverName, token);
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                return false;
            }
            finally
            {
                Array.Clear(token, 0, token.Length);
            }
        }

        /// <summary>
        /// Starts a proof exchange, replacing any that was already outstanding. The binding and the
        /// nonce are kept with it, because the request that follows arrives on a different
        /// connection and has to prove itself against the connection the challenge was issued on.
        /// </summary>
        public PairingChallenge Challenge(byte[] binding, byte[] nonce, DateTimeOffset now, TimeSpan lifetime)
        {
            var challenge = new PairingChallenge(NewChallenge(), binding, nonce, now + lifetime);

            lock (_lock)
            {
                if (_disposed)
                {
                    return null;
                }

                _challenge = challenge;
            }

            return challenge;
        }

        /// <summary>
        /// Takes the outstanding challenge, if there is one that has not expired. It is spent by
        /// being taken, so a request that fails cannot be tried again against the same challenge.
        /// </summary>
        public PairingChallenge TakeChallenge(DateTimeOffset now)
        {
            lock (_lock)
            {
                var challenge = _challenge;
                _challenge = null;

                if (challenge == null || challenge.HasExpired(now))
                {
                    return null;
                }

                return challenge;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _challenge = null;
                Array.Clear(_token, 0, _token.Length);
            }
        }

        private static byte[] NewChallenge()
        {
            var challenge = new byte[PairingChallenge.Length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(challenge);
            }

            return challenge;
        }
    }

    /// <summary>
    /// What the server sent back in phase 3, and the connection it sent it on.
    /// </summary>
    /// <remarks>
    /// It exists because `tls-server-end-point` is a constant for the life of a certificate rather
    /// than a per-session value, so on its own it carries no freshness. `PAIRING.md` §307
    /// records that finding and this is the answer to it: the server picks a value the client
    /// cannot have seen before, and the request that follows has to be computed over it.
    /// </remarks>
    public sealed class PairingChallenge
    {
        public const int Length = 32;

        private readonly byte[] _challenge;
        private readonly byte[] _binding;
        private readonly byte[] _nonce;
        private readonly DateTimeOffset _expires;

        public PairingChallenge(byte[] challenge, byte[] binding, byte[] nonce, DateTimeOffset expires)
        {
            if (challenge == null)
            {
                throw new ArgumentNullException(nameof(challenge));
            }

            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (nonce == null)
            {
                throw new ArgumentNullException(nameof(nonce));
            }

            _challenge = (byte[])challenge.Clone();
            _binding = (byte[])binding.Clone();
            _nonce = (byte[])nonce.Clone();
            _expires = expires;
        }

        public byte[] Value
        {
            get { return (byte[])_challenge.Clone(); }
        }

        /// <summary>The host certificate the hello was answered with.</summary>
        public byte[] Binding
        {
            get { return (byte[])_binding.Clone(); }
        }

        /// <summary>The nonce out of the name the hello arrived on.</summary>
        public byte[] Nonce
        {
            get { return (byte[])_nonce.Clone(); }
        }

        public DateTimeOffset Expires
        {
            get { return _expires; }
        }

        public bool HasExpired(DateTimeOffset now)
        {
            return now >= _expires;
        }
    }
}
