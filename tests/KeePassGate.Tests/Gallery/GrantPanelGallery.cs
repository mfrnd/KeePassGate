using System;
using System.Collections.Generic;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The grant tab in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// What varies here is not the words in the table but how many bands the page has and how much
    /// room the table is left with: a warning appearing takes a band, an unreadable document takes
    /// two and disables the rest, and more rules than fit take a scroll bar.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class GrantPanelGallery
    {
        private static readonly string[] Offered = { "reader", "editor", "ops", "analyst" };

        [Test]
        public void Screenshot()
        {
            // Nothing on the node and nothing to grant to: the state a database is in before
            // anybody has done anything, and the one where the page has to say where a profile
            // comes from rather than showing an empty table.
            Shoot("grant-01-no-profiles-named", null, null, null);

            // Nothing on the node, profiles to choose from. The empty line is the whole table.
            Shoot("grant-02-empty", null, null, Offered);

            // One rule, which is the ordinary case and the baseline everything else is read
            // against.
            Shoot(
                "grant-03-one-rule",
                "{\"reader\":[\"read\",\"list\"]}",
                null,
                Offered);

            // A rule of its own and one from the group above, in one table. The inherited row is
            // gray and has nothing to remove, which is the difference this page exists to draw.
            Shoot(
                "grant-04-local-and-inherited",
                "{\"editor\":[\"read\",\"update\"]}",
                Above(),
                Offered);

            // Only inherited. Every row is gray and the last column is empty down its whole length.
            Shoot("grant-05-inherited-only", null, Above(), Offered);

            // The widest a mask gets and the narrowest, on one page, because the column is sized to
            // what is in it and these are the two ends of that.
            Shoot(
                "grant-06-widest-and-narrowest-masks",
                "{\"reader\":[\"create\",\"read\",\"update\",\"strip\",\"delete\",\"list\",\"attachments\",\"protected\"],"
                + "\"editor\":[]}",
                null,
                Offered);

            // Sealed. One more thing is true about this node and the checkbox is the only place it
            // is said.
            Shoot(
                "grant-07-sealed",
                "{\"default\":\"deny\",\"reader\":[\"read\"]}",
                null,
                Offered);

            // Sealed over a rule from the group above, which is the one state where the table and
            // the check box under it are about the same profile: the row is still listed and the
            // seal is refusing it, so the mask carries the line that says it no longer arrives.
            Shoot(
                "grant-08-sealed-over-inherited",
                "{\"default\":\"deny\"}",
                Above(),
                Offered);

            // A document nobody can read. The warning band appears, the discard button appears with
            // it, and the table is left with what those two leave.
            Shoot("grant-09-unreadable-here", "{this will not read", null, Offered);

            // Readable here and unreadable above, which is a different warning about a different
            // node and the same band.
            Shoot("grant-10-unreadable-above", "{\"reader\":[\"read\"]}", Broken(), Offered);

            // More rules than the table can show at once.
            Shoot("grant-11-scrolling", Many(12), null, Offered);

            // A name nobody would type and everybody eventually has, against a column that shares
            // what the other two leave.
            Shoot(
                "grant-12-long-profile-name",
                "{\"a-profile-with-a-name-somebody-really-did-type\":[\"read\",\"list\"]}",
                null,
                Offered);
        }

        /// <summary>
        /// Draws the tab as the group dialog shows it, in the room that dialog gives it.
        /// </summary>
        /// <remarks>
        /// The group dialog's room rather than the settings dialog's, because it is the least this
        /// tab is ever given and the states drawn here are about what that room leaves the table.
        /// </remarks>
        private static void Shoot(string name, string stored, IAclNode above, IEnumerable<string> offered)
        {
            var sheet = new GrantSheet(stored, above, offered, null);

            using (var panel = new GrantPanel(sheet, new FixedStore(stored), GrantTab.GroupScope, null))
            {
                Gallery.Shot(panel, Gallery.GroupPage, name);
            }
        }

        /// <summary>A group above with a rule on it, so the table has something to inherit.</summary>
        private static IAclNode Above()
        {
            return TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.List, AclVerb.Attachments);
        }

        /// <summary>A group above whose document will not read, which refuses everything under it.</summary>
        private static IAclNode Broken()
        {
            return TestNode.Root("root").Holding("{this will not read either");
        }

        /// <summary>A document naming enough profiles to overflow the table.</summary>
        private static string Many(int count)
        {
            var text = new System.Text.StringBuilder("{");

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append("\"profile-").Append(i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).Append("\":[\"read\",\"list\"]");
            }

            return text.Append('}').ToString();
        }

        /// <summary>A store that answers with one document and keeps whatever it is given.</summary>
        private sealed class FixedStore : IGrantStore
        {
            private string _current;

            public FixedStore(string current)
            {
                _current = current;
            }

            public string Read()
            {
                return _current;
            }

            public void Write(string document)
            {
                _current = document;
            }
        }
    }
}
