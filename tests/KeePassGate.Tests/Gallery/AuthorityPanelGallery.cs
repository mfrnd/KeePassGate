using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Pairing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The certificate authority page in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// This page has more bands than either of the others and they come and go: a database with no
    /// authority shows a line where the facts would be and three buttons of which only Create can
    /// be pressed, and one
    /// with an invitation outstanding grows a code, a box to copy it out of and a line saying how
    /// long it lasts. It is also where the wording is doing the most work, so it is the page where
    /// a sentence that has grown a line longer than the room shows up first.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class AuthorityPanelGallery
    {
        [Test]
        public void Screenshot()
        {
            // No authority at all: the state every database starts in, and the one where nothing on
            // the page can be pressed except the one button that makes the rest possible.
            Shoot("authority-01-none", Gallery.SettingsPage, Authority, panel => { });

            // An authority and nothing else. The facts arrive, and the three buttons swap over.
            Shoot("authority-02-created", Gallery.SettingsPage, Authority, panel => panel.CreateAuthority());

            // The profiles page with the tab strip above it, which is the geometry the page is
            // actually given: the same table as `ProfileListGallery` shows, in the room that is left
            // once the strip has taken its share.
            Shoot("authority-03-profiles-page", Gallery.SettingsPage, Profiles, panel => panel.CreateAuthority());

            // The clients page with nobody on it, which is where the invitation lives.
            Shoot("authority-04-clients-page", Gallery.SettingsPage, Clients, panel => panel.CreateAuthority());

            // An invitation outstanding: the code in the box, and the line saying how long it is
            // good for and what the approval dialog will call it.
            Shoot(
                "authority-05-one-invitation",
                Gallery.SettingsPage,
                Clients,
                panel =>
                {
                    panel.CreateAuthority();
                    panel.InviteClient();
                });

            // The band over the invite button, which shows whenever a code handed out now could
            // not be redeemed. Two sentences can appear there, one at a time and each of them one
            // line, so this one says how tall the band gets and how much of the list underneath it
            // takes away. The button under it is dead in the same state: here, an authority made on
            // the first page and still waiting for the dialog's OK.
            Shoot(
                "authority-06-a-notice-over-the-invite-button",
                Gallery.SettingsPage,
                Clients,
                () => new PendingDatabase(new FakeGateDatabase(), null),
                panel => panel.CreateAuthority());
        }

        private const int Authority = 0;

        private const int Profiles = 1;

        private const int Clients = 2;

        private static void Shoot(string name, Size size, int page, Action<AuthorityPanel> arrange)
        {
            Shoot(name, size, page, () => new FakeGateDatabase(), arrange);
        }

        /// <summary>
        /// The same, for a state that is in the database before the page is built rather than
        /// something a button does to it.
        /// </summary>
        /// <remarks>
        /// A change waiting for the dialog's OK is the one this exists for. The page writes into
        /// whatever database it is given, so a page that holds its changes until OK has to be given
        /// the wrapper that holds them, and that is arranged where the database is made.
        /// </remarks>
        private static void Shoot(
            string name, Size size, int page, Func<IGateDatabase> database, Action<AuthorityPanel> arrange)
        {
            var log = new RecordingGateLog();

            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, log))
            using (var panel = new AuthorityPanel(database(), new FakePrompt(true), log, book))
            {
                arrange(panel);
                Turn(panel, page);
                Gallery.Shot(panel, size, name);
            }
        }

        /// <summary>Brings one of the three pages to the front, which is a state of its own.</summary>
        private static void Turn(Control panel, int page)
        {
            var tabs = panel.Controls.OfType<TabControl>().FirstOrDefault();

            if (tabs != null && page < tabs.TabPages.Count)
            {
                tabs.SelectedIndex = page;
            }
        }
    }
}
