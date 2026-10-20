using System;
using System.Collections.Generic;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The invitation book as one settings dialog sees it: what is made on the dialog is taken back
    /// again unless the dialog is accepted.
    /// </summary>
    /// <remarks>
    /// **Live at once, and withdrawn on anything but OK.** Everything else on the tab waits in
    /// <see cref="PendingDatabase"/> for the dialog's answer, because it is the database's. An
    /// invitation is the plugin's memory and never reaches the database, so nothing would take it
    /// back, and a code copied off a dialog that was then canceled would stay good for its whole
    /// lifetime. A dialog offering OK and Cancel says nothing has happened yet, so Cancel takes
    /// back every invitation made on it, and so does closing it any other way.
    ///
    /// **Not held back the way the database's changes are**, because a code that only began to
    /// work at OK would be refused to a program given it while the dialog was still open, which is
    /// the worse surprise. So a program can pair while the dialog is open, and a code spent that
    /// way stays spent whatever the dialog says next: the record it wrote is the database's, and
    /// taking that back is a revocation.
    ///
    /// **A code this replaced stays replaced.** Renewing drops the old code at once, and Cancel
    /// does not bring it back, since a code its operator has replaced is one they have stopped
    /// expecting anybody to use.
    /// </remarks>
    public sealed class PendingInvitations : IInvitations
    {
        private readonly IInvitations _book;
        private readonly object _lock = new object();
        private readonly List<string> _made = new List<string>();

        public PendingInvitations(IInvitations book)
        {
            if (book == null)
            {
                throw new ArgumentNullException(nameof(book));
            }

            _book = book;
        }

        public Invitation Invite(string databaseId)
        {
            var invitation = _book.Invite(databaseId);
            if (invitation != null)
            {
                lock (_lock)
                {
                    _made.Add(invitation.Id);
                }
            }

            return invitation;
        }

        public IReadOnlyList<Invitation> Outstanding()
        {
            return _book.Outstanding();
        }

        public IReadOnlyList<Invitation> For(string databaseId)
        {
            return _book.For(databaseId);
        }

        public bool Burn(string invitationId)
        {
            return _book.Burn(invitationId);
        }

        public bool Withdraw(string invitationId)
        {
            return _book.Withdraw(invitationId);
        }

        public IReadOnlyList<Invitation> Matching(string serverName)
        {
            return _book.Matching(serverName);
        }

        /// <summary>The dialog was accepted, so what was made on it stays outstanding.</summary>
        public void Apply()
        {
            lock (_lock)
            {
                _made.Clear();
            }
        }

        /// <summary>
        /// The dialog closed without OK, so every invitation made on it that nobody has used goes.
        /// </summary>
        /// <returns>How many were still outstanding and are now withdrawn.</returns>
        public int Discard()
        {
            List<string> made;
            lock (_lock)
            {
                made = new List<string>(_made);
                _made.Clear();
            }

            var withdrawn = 0;
            foreach (var id in made)
            {
                if (_book.Withdraw(id))
                {
                    withdrawn++;
                }
            }

            return withdrawn;
        }
    }
}
