using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using KeePassGate.Diagnostics;

namespace KeePassGate.Pairing
{
    /// <summary>The invitations an operator has outstanding, as everything else sees them.</summary>
    public interface IInvitations
    {
        /// <summary>
        /// A new invitation for one database, replacing whatever that database had outstanding.
        /// </summary>
        Invitation Invite(string databaseId);

        /// <summary>Everything still live, expired ones having been dropped first.</summary>
        IReadOnlyList<Invitation> Outstanding();

        /// <summary>Only the ones for one database.</summary>
        IReadOnlyList<Invitation> For(string databaseId);

        /// <summary>
        /// Spends an invitation. False when it was already gone, spent or run out, which is what
        /// makes it single use: two connections racing to pair cannot both win.
        /// </summary>
        bool Burn(string invitationId);

        /// <summary>
        /// Every live invitation this name was built from. A list rather than one, because
        /// resolution counts what matched instead of stopping at the first.
        /// </summary>
        IReadOnlyList<Invitation> Matching(string serverName);
    }

    /// <summary>
    /// Every outstanding invitation, in plugin memory and nowhere else.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §306 is the whole of this class. The lifetime is in memory under one lock,
    /// so a burn cannot be reverted and two connections cannot spend the same invitation. Expiry is
    /// swept before anything is read or spent, so an expired invitation is indistinguishable from
    /// one that never existed: the gate drops the connection either way, without a word.
    ///
    /// A timeout does not burn. An absent operator costs the invitee nothing, and only an approval
    /// or a refusal spends the invitation.
    ///
    /// Nothing here is written to a database, so nothing here survives KeePass exiting, and that is
    /// not a limitation to be worked around. It is the reason a stale dialog snapshot cannot bring a
    /// spent invitation back.
    /// </remarks>
    public sealed class InvitationBook : IInvitations, IDisposable
    {
        /// <summary>Long enough to paste into another window, short enough to be worth little.</summary>
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How many invitations one database may have outstanding: one.
        /// </summary>
        /// <remarks>
        /// A new one replaces the old one rather than joining it. Two live codes for one database
        /// is a state nobody can reason about: the operator cannot tell which of them somebody is
        /// about to use, cannot tell which one they sent yesterday, and cannot take one back
        /// without taking back the other. One code, renewed by pressing the button again, is the
        /// whole of what an operator has to hold in their head.
        ///
        /// It also bounds the resolver's per connection work at one, which is what the ceiling
        /// this replaces was for.
        /// </remarks>
        public const int MaxPerDatabase = 1;

        private readonly TimeSpan _lifetime;
        private readonly Func<DateTimeOffset> _clock;
        private readonly IGateLog _log;
        private readonly object _lock = new object();
        private readonly List<Invitation> _invitations = new List<Invitation>();

        private bool _disposed;

        public InvitationBook(IGateLog log)
            : this(DefaultLifetime, null, log)
        {
        }

        public InvitationBook(TimeSpan lifetime, Func<DateTimeOffset> clock, IGateLog log)
        {
            _lifetime = lifetime;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _log = log ?? NullGateLog.Instance;
        }

        public int Count
        {
            get { return Outstanding().Count; }
        }

        public Invitation Invite(string databaseId)
        {
            if (string.IsNullOrEmpty(databaseId))
            {
                throw new ArgumentException("An invitation is for one database.", nameof(databaseId));
            }

            var now = _clock();
            var invitation = new Invitation(NewId(), databaseId, PairingToken.New(), now, now + _lifetime);

            // Written after the lock is given up, along with whatever the sweep found. The log can
            // reach a file, and holding a lock the connection threads need across somebody else's
            // file system is a stall this has no reason to take.
            var said = new List<string>();

            lock (_lock)
            {
                if (_disposed)
                {
                    invitation.Dispose();
                    return null;
                }

                Sweep(now, said);

                // Whatever this database had is gone the moment a new one is made. Dropped rather
                // than kept until it expires, because a code the operator has replaced is a code
                // they have stopped expecting anybody to use.
                for (var at = _invitations.Count - 1; at >= 0; at--)
                {
                    if (!string.Equals(_invitations[at].DatabaseId, databaseId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    said.Add("invitation " + _invitations[at].Id + " replaced by a new one");
                    _invitations[at].Dispose();
                    _invitations.RemoveAt(at);
                }

                _invitations.Add(invitation);
            }

            Say(said);
            _log.Info("invitation " + invitation.Id + " outstanding until " + Moments.Wire(invitation.Expires));
            return invitation;
        }

        public IReadOnlyList<Invitation> Outstanding()
        {
            var said = new List<string>();
            List<Invitation> live;

            lock (_lock)
            {
                Sweep(_clock(), said);
                live = new List<Invitation>(_invitations);
            }

            Say(said);
            return live;
        }

        public IReadOnlyList<Invitation> For(string databaseId)
        {
            var mine = new List<Invitation>();
            if (string.IsNullOrEmpty(databaseId))
            {
                return mine;
            }

            foreach (var invitation in Outstanding())
            {
                if (string.Equals(invitation.DatabaseId, databaseId, StringComparison.Ordinal))
                {
                    mine.Add(invitation);
                }
            }

            return mine;
        }

        public bool Burn(string invitationId)
        {
            if (string.IsNullOrEmpty(invitationId))
            {
                return false;
            }

            Invitation burned = null;
            var said = new List<string>();

            lock (_lock)
            {
                // Swept first, as every read of the book is. An invitation whose time ran out while
                // the operator was deciding is gone then, whether or not anything else happened to
                // touch the book in the meantime, rather than spendable until something did.
                Sweep(_clock(), said);

                for (var i = 0; i < _invitations.Count; i++)
                {
                    if (!string.Equals(_invitations[i].Id, invitationId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    burned = _invitations[i];
                    _invitations.RemoveAt(i);
                    break;
                }
            }

            Say(said);

            if (burned == null)
            {
                return false;
            }

            burned.Dispose();
            _log.Info("invitation " + invitationId + " spent");
            return true;
        }

        /// <summary>
        /// Every live invitation this name was built from. Every one is tried and none of them
        /// stops the loop, so the work does not depend on which invitation matched or on whether
        /// any did.
        /// </summary>
        public IReadOnlyList<Invitation> Matching(string serverName)
        {
            var matched = new List<Invitation>();
            if (string.IsNullOrEmpty(serverName))
            {
                return matched;
            }

            foreach (var invitation in Outstanding())
            {
                if (invitation.Answers(serverName))
                {
                    matched.Add(invitation);
                }
            }

            return matched;
        }

        /// <summary>
        /// Everything goes, which is what KeePass exiting means. Called rather than relied upon:
        /// the process ending would take these with it anyway, and zeroing them is the same hygiene
        /// the authority material gets.
        /// </summary>
        public void Clear()
        {
            List<Invitation> going;

            lock (_lock)
            {
                going = new List<Invitation>(_invitations);
                _invitations.Clear();
            }

            foreach (var invitation in going)
            {
                invitation.Dispose();
            }

            if (going.Count > 0)
            {
                _log.Info("dropped " + going.Count + " outstanding invitation" + (going.Count == 1 ? "" : "s"));
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
            }

            Clear();
        }

        /// <summary>
        /// Called with the lock held. What it wants said goes on the list for the caller to say
        /// once the lock is back, because logging can reach a file.
        /// </summary>
        private void Sweep(DateTimeOffset now, ICollection<string> said)
        {
            for (var i = _invitations.Count - 1; i >= 0; i--)
            {
                if (!_invitations[i].HasExpired(now))
                {
                    continue;
                }

                var expired = _invitations[i];
                _invitations.RemoveAt(i);
                expired.Dispose();
                said.Add("invitation " + expired.Id + " expired unused");
            }
        }

        private void Say(List<string> said)
        {
            foreach (var line in said)
            {
                _log.Info(line);
            }
        }

        private static string NewId()
        {
            var bytes = new byte[8];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return Hex.Of(bytes);
        }
    }
}
