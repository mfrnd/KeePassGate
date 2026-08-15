using System;
using System.Collections.Generic;
using System.Globalization;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the tab says about outstanding invitations. Separate from the panel because the wording
    /// carries a warning the operator has to act on, and a form is a poor place to test one.
    /// </summary>
    public static class InvitationText
    {
        /// <summary>
        /// Said when there is no code an operator could hand to anybody.
        /// </summary>
        /// <remarks>
        /// **Including when one is in the book and this authority has expired**, which is a code
        /// nobody can redeem and nothing can bring back: `PairingHandler` refuses the redemption,
        /// and recreating the authority changes the digest the book answers on. This line used to
        /// say the authority had expired, which the authority page already says whenever this
        /// state is reached, with the date and above the button that recreates it. This line is
        /// about what is on offer.
        /// </remarks>
        public const string None = "No code is outstanding.";

        /// <summary>
        /// Said after the copy button, because a copy that worked and a copy that did nothing look
        /// identical on screen.
        /// </summary>
        public const string Copied = "The code is on the clipboard.";

        /// <summary>
        /// The clipboard is one lock the whole desktop shares, so any program can be holding it
        /// when we ask. The box is still on screen, so there is something the operator can do.
        /// </summary>
        public const string ClipboardBusy =
            "Another program is holding the clipboard. Select the code and copy it.";

        public const string NoAuthority =
            "This database has no authority, so there is nothing to invite anybody to.";

        /// <summary>
        /// Said when the authority on screen has not reached the database yet.
        /// </summary>
        /// <remarks>
        /// The tab reads its own unapplied changes, so an authority created a moment ago describes
        /// itself here perfectly well. The gate reads the live database and would find nothing, so
        /// a code minted now would look right, count down, and resolve to nothing at all.
        /// </remarks>
        public const string AuthorityWaiting =
            "This authority has not been applied yet. Press OK, reopen this dialog, and invite then.";

        /// <summary>
        /// Said when this database's authority has run out.
        /// </summary>
        /// <remarks>
        /// Short, like the line above it and for the same reason: this is the footer under both
        /// pages.
        ///
        /// The button is dead in this state, so nothing an operator does reaches this. What does
        /// is a clock that crosses the expiry between the page being drawn and the button being
        /// pressed, and the answer to that has to be a sentence rather than a code that no program
        /// can redeem.
        /// </remarks>
        public const string AuthorityExpired =
            "This authority has expired. Recreate it on the first page; every client pairs again.";

        /// <summary>
        /// What is outstanding, which is one code or none, and what an approval dialog will call
        /// it.
        /// </summary>
        /// <remarks>
        /// A list arrives rather than one invitation because that is what the book holds, and it
        /// holds at most one per database. The soonest expiry is taken rather than the first, so
        /// this stays right whatever order they come in, and the identifier is read off that same
        /// invitation: a countdown about one and a name about another is worse than either alone.
        ///
        /// **The identifier is here because it is the only thing an approval dialog can be checked
        /// against.** The code is never shown there, so without this the dialog names the
        /// invitation with something the operator has never seen.
        /// </remarks>
        public static string For(IReadOnlyList<Invitation> invitations, DateTimeOffset now)
        {
            if (invitations == null || invitations.Count == 0)
            {
                return None;
            }

            var soonest = invitations[0];
            foreach (var invitation in invitations)
            {
                if (invitation.Expires < soonest.Expires)
                {
                    soonest = invitation;
                }
            }

            var minutes = (int)Math.Ceiling((soonest.Expires - now).TotalMinutes);

            if (minutes <= 0)
            {
                return "The code has just run out.";
            }

            var window = minutes == 1
                ? "1 minute"
                : minutes.ToString(CultureInfo.InvariantCulture) + " minutes";

            // Three things about a code used to be a line of their own under it: that it lives in
            // memory, that renewing replaces it, and that the first client to use it spends it.
            // They are said here instead, in the sentence the operator is already reading, because
            // a second line saying what the first line implies is a second line nobody reads and
            // this page is short of room rather than short of words.
            return "The in-memory code is good for another " + window
                + " until the first client consumes it or it gets renewed. "
                + "The approval dialog will call it " + soonest.Id + ".";
        }
    }
}
