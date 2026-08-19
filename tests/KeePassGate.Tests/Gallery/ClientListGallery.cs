using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The clients page in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// The row height is the interesting axis here rather than the row count: a client is one row
    /// however many profiles it holds, and the row grows to fit the list. So the states worth
    /// screenshotting are one profile against several, a message under the table against none, and
    /// enough clients that the list has to scroll while the row heights vary down it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class ClientListGallery
    {
        private static TestAuthority _authority;

        [OneTimeSetUp]
        public void IssueOne()
        {
            _authority = TestAuthority.New();
        }

        [OneTimeTearDown]
        public void Done()
        {
            // The authority, because it is what holds them. `Client` issues one per row and each
            // is a key container of its own under the user's profile; disposing the authority is
            // what resets every one of them, and there are two dozen.
            if (_authority != null)
            {
                _authority.Dispose();
                _authority = null;
            }
        }

        [Test]
        public void Screenshot()
        {
            // Nobody admitted. The table keeps its headings, because they say what would be listed
            // here, and the line under it says why there is nothing.
            Shoot("clients-01-empty", Gallery.SettingsPage);

            // One client, one profile: the row every other state is read against.
            Shoot("clients-02-one-client", Gallery.SettingsPage, Client("agent-invented", "reader"));

            // One client holding three. Still one row, and the row is three lines tall while
            // everything beside it stays on the first.
            Shoot(
                "clients-03-many-profiles",
                Gallery.SettingsPage,
                Client("agent-invented", "reader", "ops", "analyst"));

            // Rows of different heights next to each other, which is where a badly aligned cell
            // shows and a single row never would.
            Shoot(
                "clients-04-mixed-heights",
                Gallery.SettingsPage,
                Client("agent-one", "reader"),
                Client("agent-two", "reader", "ops"),
                Client("agent-three", "reader", "ops", "analyst", "auditor"));

            // More than fits, so the list scrolls and the columns lose what the scroll bar takes.
            Shoot("clients-05-scrolling", Gallery.SettingsPage, Crowd(14));

            // A name and a profile nobody would choose and somebody will have.
            Shoot(
                "clients-06-long-names",
                Gallery.SettingsPage,
                Client(
                    "an-agent-with-a-name-that-somebody-really-did-type",
                    "a-profile-named-at-length",
                    "another-one-just-as-long"));

            // And a listing that could not be made at all, which is the state this page is
            // easiest to get wrong: the same empty grid as a database that has admitted
            // nobody, under a sentence that must not say so.
            using (var list = new ClientList(key => null))
            {
                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), false), null, null);
                Gallery.Shot(list, Gallery.SettingsPage, "clients-09-an-unreadable-listing");
            }

            // A record this database's authority did not issue, beside one it did. The column
            // that answers with a date answers with a phrase instead, which is the one place on
            // this page where a cell changes shape rather than value.
            using (var list = new ClientList(key => null))
            {
                var stranger = Client("agent-from-a-replaced-authority", "reader");

                list.Fill(
                    Listing(Client("agent-invented", "reader"), stranger),
                    new Dictionary<string, ClientReach> { { stranger.AdmissionKey, ClientReach.Stranger } },
                    null);

                Gallery.Shot(list, Gallery.SettingsPage, "clients-10-a-record-from-another-authority");
            }

            // A client holding a profile the database has since removed, beside the one it still
            // names. The removed one is marked and dimmed in its line, which is the one cell here
            // drawn a line at a time.
            using (var list = new ClientList(key => null))
            {
                list.Fill(
                    Listing(Client("agent-invented", "reader", "ops"), Client("agent-other", "ops")),
                    null,
                    new[] { "reader" });

                Gallery.Shot(list, Gallery.SettingsPage, "clients-11-a-removed-profile");
            }

            // Revoked clients a merge brought back, which are not rows: a line of their own under
            // the list, and the band that makes it, until the next write takes the records away.
            using (var list = new ClientList(key => null))
            {
                list.Fill(new AdmissionListing(new[] { Client("agent-invented", "reader") }, 2, true), null, null);
                Gallery.Shot(list, Gallery.SettingsPage, "clients-12-revoked-clients-reappeared");
            }

            // A stored record that will not read, after the client beside it. It has nothing to
            // show but the start of its key and why there is no date, and its X is the one way to
            // take it away that does not make every client pair again.
            using (var list = new ClientList(key => null))
            {
                var damaged = CertificateDigest.Of(Encoding.ASCII.GetBytes("a record that will not read"));

                list.Fill(
                    new AdmissionListing(new[] { Client("agent-invented", "reader") }, new[] { damaged }, 0, true),
                    null,
                    null);

                Gallery.Shot(list, Gallery.SettingsPage, "clients-13-a-record-that-will-not-read");
            }
        }

        [Test]
        public void ScreenshotAfterAGesture()
        {
            // What the page says when something has just happened. The line under the table is
            // shared, so this is also the state that used to say one thing twice: the last client
            // is gone, and both "that client can no longer reach this database" and "no client has
            // been admitted" were true at once.
            using (var list = new ClientList(key => null))
            {
                list.Fill(Listing(Client("agent-invented", "reader")), null, null);
                list.Revoke(0);

                Gallery.Shot(list, Gallery.SettingsPage, "clients-07-after-a-revocation");
            }

            // And when it did not happen, which on the tab is a database gone from under the dialog.
            // The row stays, because it is still admitted, and the same line has to carry a
            // refusal as well as a result.
            using (var list = new ClientList(key => "the database is not open"))
            {
                list.Fill(Listing(Client("agent-invented", "reader"), Client("agent-other", "ops")), null, null);
                list.Revoke(0);

                Gallery.Shot(list, Gallery.SettingsPage, "clients-08-a-refused-revocation");
            }
        }

        private static void Shoot(string name, Size size, params AdmissionRecord[] records)
        {
            using (var list = new ClientList(key => null))
            {
                list.Fill(Listing(records), null, null);
                Gallery.Shot(list, size, name);
            }
        }

        /// <summary>
        /// These records, listed in full. Every frame here but one is a database that answered;
        /// the one that did not is written out in full where it is drawn, so that it cannot be
        /// reached by accident through this.
        /// </summary>
        private static AdmissionListing Listing(params AdmissionRecord[] records)
        {
            return new AdmissionListing(records, true);
        }

        private static AdmissionRecord[] Crowd(int count)
        {
            var many = new List<AdmissionRecord>();

            for (var i = 0; i < count; i++)
            {
                var profiles = i % 3 == 0
                    ? new[] { "reader" }
                    : (i % 3 == 1 ? new[] { "reader", "ops" } : new[] { "reader", "ops", "analyst" });

                many.Add(Client("agent-" + Numbered(i), profiles));
            }

            return many.ToArray();
        }

        private static AdmissionRecord Client(string name, params string[] profiles)
        {
            // A certificate of its own per row, because a record's key is the digest of the bytes
            // it carries and rows sharing one certificate would share one key. Keying on the name
            // instead is what a record now refuses, and that refusal is what makes a lost record
            // fail closed rather than admit somebody.
            var der = _authority.IssueAdmittedClient(name).RawData;
            return new AdmissionRecord(
                CertificateDigest.Of(der),
                "CN=" + name,
                new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero),
                profiles,
                der);
        }

        /// <summary>Two digits, so the pictures sort the way they are meant to be read.</summary>
        private static string Numbered(int i)
        {
            return i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
