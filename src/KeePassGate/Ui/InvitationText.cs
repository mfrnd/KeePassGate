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
        /// nobody can redeem and nothing can bring back: `PAIRING.md` §311 refuses the redemption,
        /// and recreating the authority changes the digest the book answers on. This line used to
        /// say the authority had expired, which is the sentence the notice above the button is
        /// already showing whenever this state is reached, so the page said one thing twice on a
        /// tab that is 449 by 314. The reason belongs to the notice, because that is the band that
        /// also names the remedy.
        /// </remarks>
        public const string None = "No code is outstanding.";

        /// <summary>
        /// Said after the copy button, because a copy that worked and a copy that did nothing look
        /// identical on screen.
        /// </summary>
        public const string Copied = "The code is on the clipboard.";

        /// <summary>
        /// Said when the copy button is pressed over a code that is no longer outstanding.
        /// </summary>
        /// <remarks>
        /// It has run out or been spent since the page last looked, and the gate drops a
        /// connection that presents it without saying anything, so nothing is copied and the box
        /// is emptied: handing it over would send somebody off to fail.
        /// </remarks>
        public const string NotCopied = "That code is no longer outstanding, so nothing was copied.";

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
        /// Said when a profile edit on this dialog has not reached the database yet.
        /// </summary>
        /// <remarks>
        /// The button is dead in this state, so nothing an operator does with it reaches this.
        /// What does is a profile typed on the second page after the clients page was last drawn,
        /// which leaves a live button over a list that is about to change under whoever is
        /// approved next.
        /// </remarks>
        public const string ProfilesWaiting =
            "A profile change has not been applied yet. Press OK, reopen this dialog, and invite then.";

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
