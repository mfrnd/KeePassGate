using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The list the operator revokes from.
    /// </summary>
    /// <remarks>
    /// Every decision is reached through a method rather than by pressing a control, because a
    /// button does nothing on a control that was never shown and does it silently, so a test that
    /// pressed one would pass while proving nothing.
    ///
    /// The list writes to no database. It is handed a function that takes an admission away and
    /// answers with why it could not, which is what lets the refusals be tested without a password
    /// manager in the room.
    /// </remarks>
    [TestFixture]
    public sealed class ClientListTests
    {
        [Test]
        public void Every_admitted_client_is_listed_with_what_it_may_reach()
        {
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops", "readonly")))
            {
                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(list.Clients[0].Profiles, Is.EqualTo(new[] { "ops", "readonly" }),
                    "the profiles are shown beside the name, so revoking is not a guess");
            }
        }

        [Test]
        public void Revoking_a_row_that_is_not_there_says_so()
        {
            using (var list = ListOf(Refuses, Record("agent-fictitious", "ops")))
            {
                var said = list.Revoke(4);

                Assert.That(said, Does.Contain("no such client"));
                Assert.That(list.Clients.Count, Is.EqualTo(1), "no row was pressed, so nothing was taken away");
            }
        }

        [Test]
        public void A_revoked_client_leaves_the_list()
        {
            using (var list = ListOf(
                Allows, Record("agent-fictitious", "ops"), Record("agent-invented", "readonly")))
            {
                list.Revoke(0);

                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(list.Clients[0].Subject, Does.Contain("agent-invented"),
                    "the wrong client was taken away");
            }
        }

        [Test]
        public void A_refused_revocation_leaves_the_client_where_it_was()
        {
            // The refusal that matters is a dialog open on the group, which is temporary. A list
            // that dropped the row anyway would be showing the operator a database that does not
            // exist, and the client would be back on the next look.
            using (var list = ListOf(Refuses, Record("agent-fictitious", "ops")))
            {
                var said = list.Revoke(0);

                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(said, Does.Contain("still admitted"));
                Assert.That(said, Does.Contain("a dialog is open"), "the operator is told what to do about it");
            }
        }

        [Test]
        public void What_happened_is_said_in_words_and_names_the_client()
        {
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops")))
            {
                list.Revoke(0);

                Assert.That(list.Result, Does.Contain("agent-fictitious"));
                Assert.That(list.Result, Does.Contain("no longer"));
            }
        }

        [Test]
        public void A_database_that_has_admitted_nobody_says_so()
        {
            using (var list = ListOf(Allows))
            {
                Assert.That(list.Clients, Is.Empty);
                Assert.That(NoteOn(list), Is.EqualTo(ClientList.NobodyText));
            }
        }

        [Test]
        public void A_listing_that_could_not_be_read_does_not_claim_nobody_is_admitted()
        {
            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), false), null);

                // The grid is empty either way. What must not happen is the sentence under it
                // telling the operator that nobody has access, when nobody could be counted.
                Assert.That(list.Clients, Is.Empty);
                Assert.That(NoteOn(list), Is.EqualTo(ClientList.UnreadableText));
                Assert.That(NoteOn(list), Is.Not.EqualTo(ClientList.NobodyText));
            }
        }

        [Test]
        public void A_listing_that_could_not_be_read_outranks_the_last_gesture()
        {
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops")))
            {
                list.Revoke(0);
                Assert.That(NoteOn(list), Does.Contain("agent-fictitious"));

                // Reading the database again is what the tab does after a revocation. If that
                // read fails, `no longer admitted` over an empty grid reads as though the
                // revocation is why the grid is empty.
                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), false), null);

                Assert.That(NoteOn(list), Is.EqualTo(ClientList.UnreadableText));
            }
        }

        [Test]
        public void The_last_client_leaving_turns_the_list_into_that_sentence()
        {
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops")))
            {
                list.Revoke(0);

                Assert.That(list.Clients, Is.Empty);
            }
        }

        [Test]
        public void A_client_admitted_to_nothing_is_still_listed()
        {
            // An empty profile list is a client that was approved for nothing, which is unusual
            // enough to be worth seeing rather than a row to hide.
            using (var list = ListOf(Allows, Record("agent-fictitious")))
            {
                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(list.Clients[0].Profiles, Is.Empty);
            }
        }

        [Test]
        public void The_last_moment_of_a_certificate_is_not_called_expired_yet()
        {
            // The gate serves a certificate up to and including its `notAfter` and refuses it
            // afterwards, so a list saying `expired` one second early reports access as gone
            // while the gate is still handing it out. That is a disagreement an operator can see
            // and cannot explain, and it is the one `CertificateValidity` was written to end.
            var record = Record("agent-fictitious", "ops");

            DateTimeOffset notAfter;
            using (var certificate = record.Certificate())
            {
                notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                Assert.That(CertificateValidity.HasExpired(certificate, notAfter), Is.False,
                    "the gate is the other half of this and still serves at that instant");
            }

            Assert.That(ClientList.Until(record, notAfter), Is.Not.EqualTo(ClientList.ExpiredText),
                "the page called a client expired while the gate was still serving it");
            Assert.That(ClientList.Until(record, notAfter.AddSeconds(1)),
                Is.EqualTo(ClientList.ExpiredText),
                "a certificate past its last moment is expired on the page as well");
        }

        [Test]
        public void A_record_this_authority_did_not_issue_says_so_where_a_date_would_be()
        {
            // The gate pins the anchor before it looks at the usage, the dates or the record
            // itself, so a certificate chaining somewhere else is refused whatever its own dates
            // say. The row carried a date in the future anyway, which reads as a client that
            // works until then: a page promising access nobody has.
            var record = Record("agent-fictitious", "ops");

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), new[] { record.AdmissionKey });

                Assert.That(Reach(list, 0), Is.EqualTo(ClientList.StrangerText));

                // And the row stays, because it is still a record and taking it away is still the
                // operator's to do. That is why it is marked rather than hidden.
                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(list.Revoke(0), Does.Contain("no longer"));
            }

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null);

                Assert.That(Reach(list, 0), Is.Not.EqualTo(ClientList.StrangerText),
                    "a record nobody called a stranger is answered with its dates");
            }
        }

        /// <summary>
        /// What one row says about how long it reaches this database, read off the grid.
        /// </summary>
        /// <remarks>
        /// By index, because the column is one of the two the grid shares its width between and
        /// carries a heading rather than a name of its own. The order is Client, Profiles,
        /// Admitted, Until, and then the mark that takes the record away.
        /// </remarks>
        private static string Reach(ClientList list, int row)
        {
            var grid = ControlTree.FindByName<DataGridView>(list, ClientList.ListName);
            Assert.That(grid, Is.Not.Null, "the list has no grid on it");

            return Convert.ToString(grid.Rows[row].Cells[3].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A list with those clients on it. Filling is separate from constructing because the tab
        /// builds the list once and reads the database many times.
        /// </summary>
        private static ClientList ListOf(Func<AdmissionRecord, string> revoke, params AdmissionRecord[] records)
        {
            var list = new ClientList(revoke);
            list.Fill(new AdmissionListing(records, true), null);
            return list;
        }

        /// <summary>
        /// The one line under the grid, read off the control rather than off the constant. A test
        /// comparing two constants passes whatever the list decided to show.
        /// </summary>
        private static string NoteOn(ClientList list)
        {
            return list.Controls.Find(ClientList.ResultLabelName, true).Single().Text;
        }

        private static string Allows(AdmissionRecord record)
        {
            return null;
        }

        private static string Refuses(AdmissionRecord record)
        {
            return "a dialog is open on the group";
        }

        private static AdmissionRecord Record(string name, params string[] profiles)
        {
            using (var certificate = Certificate(name))
            {
                return AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles);
            }
        }

        private static X509Certificate2 Certificate(string name)
        {
            using (var key = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                return request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
            }
        }
    }
}
