using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
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
        public void Records_that_do_not_read_of_a_client_no_group_lists_stand_together_under_the_first_of_them()
        {
            // The first and third name a client no admission that reads names, the second another
            // client, and the fourth a client key that does not read: the third is drawn under the
            // first and never under the second client's row, and the fourth stands alone.
            string first = new string('1', 32), second = new string('2', 32), third = new string('3', 32), fourth = new string('4', 32);
            var listing = Listings.Of(
                Array.Empty<AdmissionRecord>(),
                new[]
                {
                    new UnreadAdmission(first, new string('a', 64), null),
                    new UnreadAdmission(second, new string('b', 64), null),
                    new UnreadAdmission(third, new string('a', 64), null),
                    new UnreadAdmission(fourth, "not a client key", null),
                },
                0,
                true);

            var rows = ClientList.Grouped(listing, true);

            Assert.That(rows.Select(row => row.Unread.Entry), Is.EqualTo(new[] { first, third, second, fourth }));
            Assert.That(rows.Select(row => row.Own), Is.EqualTo(new[] { true, false, true, true }));
        }

        [Test]
        public void A_certificate_listed_twice_and_a_record_that_does_not_read_show_why_in_their_own_words()
        {
            var record = Record("agent-invented", "ops");
            var members = record.Members().ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            members["client"] = new string('d', 64);
            var other = AdmissionRecord.Read(new string('7', 32), members);
            var listing = new AdmissionListing(
                new[] { record, other },
                new Dictionary<AdmissionRecord, AdmissionStanding>
                {
                    { record, AdmissionStanding.Twice },
                    { other, AdmissionStanding.Twice },
                },
                new[] { new UnreadAdmission(new string('8', 32), null, null) },
                0,
                true);

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(listing, null, null, null);

                var rows = Grid(list).Rows;
                for (var at = 0; at < 2; at++)
                {
                    Assert.That(Reach(list, at), Is.EqualTo("listed twice"));
                    Assert.That(
                        Plain(rows[at].Cells[0].ToolTipText).Replace("\r\n", " ").Replace("\n", " "),
                        Does.Contain("More than one admission names this certificate, so none of them admits it."));
                }

                Assert.That(Reach(list, 2), Is.EqualTo("not counted"));
                Assert.That(
                    Plain(rows[2].Cells[0].ToolTipText).Replace("\r\n", " ").Replace("\n", " "),
                    Does.Contain("This record does not read as an admission, so it admits nobody."));
            }
        }

        [Test]
        public void A_renewal_that_does_not_count_reads_not_counted_with_why_in_its_tooltip()
        {
            var record = Record("agent-invented", "ops");
            var listing = new AdmissionListing(
                new[] { record },
                new Dictionary<AdmissionRecord, AdmissionStanding> { { record, AdmissionStanding.Uncounted } },
                Array.Empty<UnreadAdmission>(),
                0,
                true);

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(listing, null, null, null);

                Assert.That(Reach(list, 0), Is.EqualTo("not counted"));
                Assert.That(
                    Plain(Grid(list).Rows[0].Cells[0].ToolTipText).Replace("\r\n", " ").Replace("\n", " "),
                    Does.Contain(
                        "Renewed, directly or further back, from a certificate that was taken away without naming the "
                        + "renewal made from it, or that no single admission admits, so it admits nobody."));
            }
        }

        [Test]
        public void The_X_shows_what_it_does_on_its_own_cell_and_a_row_without_an_X_shows_nothing_there()
        {
            // A column's tooltip is its heading's, so the text of the X has to be its cell's own.
            var record = Record("agent-invented", "ops");
            var listing = Listings.Of(new[] { record }, new[] { new UnreadAdmission(new string('9', 32), null, null) }, 0, true);

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(listing, null, null, null);

                var revoke = Grid(list).Rows[0].Cells[ClientList.RevokeColumnName];
                Assert.That(revoke, Is.InstanceOf<DataGridViewButtonCell>(), "the client's own row holds the X");
                Assert.That(
                    Plain(revoke.ToolTipText).Replace("\r\n", " ").Replace("\n", " "),
                    Is.EqualTo("Revokes this client, with every certificate it holds, when OK is pressed."));

                var alone = Grid(list).Rows[1].Cells[ClientList.RevokeColumnName];
                Assert.That(alone, Is.Not.InstanceOf<DataGridViewButtonCell>(), "a record naming no client key holds no X");
                Assert.That(alone.ToolTipText, Is.Empty);
            }
        }

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
        public void Every_X_is_named_to_a_screen_reader_by_what_pressing_it_does()
        {
            // S422: the heading was left empty for the bin painted over it, so each X was read
            // out as its row and an X, and nothing told a listener that it revokes.
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops")))
            {
                ButtonColumns.AssertNamedBy(list, Grid(list), ClientList.RevokeColumnName, ClientList.RevokeHeading);
            }
        }

        [Test]
        public void A_renewed_client_is_a_group_whose_first_row_carries_the_X_that_revokes_it_whole()
        {
            // A renewal leaves the client two certificates under one client key until it gives the
            // older up: a group, the certificate admitted last on the client's own row and the
            // older indented under it, and the X on the first row revokes the client, every row of
            // the group going at once.
            var older = Record("agent-fictitious", "ops");
            var renewed = Renewal(older);
            var asked = new List<string>();

            using (var list = ListOf(key => { asked.Add(key); return null; }, older, renewed))
            {
                Assert.That(list.Clients, Is.EqualTo(new[] { renewed, older }), "the certificate admitted last first");
                Assert.That(Cell(list, 0, 0), Is.EqualTo("agent-fictitious"));
                Assert.That(Cell(list, 1, 0), Is.EqualTo(ClientList.EarlierText));
                Assert.That(list.Rows[1].Revocable, Is.False, "an indented row has no X");

                Assert.That(list.Revoke(1), Does.Contain("first row"));
                Assert.That(asked, Is.Empty);

                Assert.That(list.Revoke(0), Is.Null);
                Assert.That(asked, Is.EqualTo(new[] { older.ClientKey }), "the client, by its client key");
                Assert.That(list.Clients, Is.Empty);
                Assert.That(NoteOn(list), Is.EqualTo(ClientList.EmptiedText),
                    "and with the last gone, the list states that it waits for OK");
            }
        }

        [Test]
        public void Revoking_one_of_two_programs_under_one_name_takes_that_one_alone()
        {
            // Two programs that chose one name are two clients, and taking one away leaves the
            // other listed, which each row's tooltip tells apart by its client key.
            var first = Record("agent", "ops");
            var second = Record("agent", "ops");

            using (var list = ListOf(Allows, first, second))
            {
                Assert.That(list.Revoke(1), Is.Null);
                Assert.That(list.Clients, Is.EqualTo(new[] { first }));
            }
        }

        [Test]
        public void A_client_key_is_shortened_to_eight_characters_unless_two_would_read_the_same()
        {
            var apart = ClientList.ShortKeys(new[] { "ab" + new string('1', 62), "cd" + new string('1', 62) });
            Assert.That(apart.Values, Is.EquivalentTo(new[] { "ab111111", "cd111111" }));

            var close = ClientList.ShortKeys(new[]
            {
                "abcdef0123" + new string('1', 54),
                "abcdef0123" + new string('2', 54),
                "99" + new string('0', 62)
            });

            Assert.That(close["abcdef0123" + new string('1', 54)], Is.EqualTo("abcdef01231"));
            Assert.That(close["abcdef0123" + new string('2', 54)], Is.EqualTo("abcdef01232"));
            Assert.That(close["99" + new string('0', 62)], Is.EqualTo("99000000"), "only the two that collide grow");
        }

        [Test]
        public void Names_for_sentences_count_each_client_once_and_tell_shared_names_apart()
        {
            var older = Record("agent-renewing", "ops");
            var renewed = Renewal(older);
            var one = Record("agent", "ops");
            var other = Record("agent", "ops");

            var named = ClientList.ClientNames(new[] { renewed, older, one, other });

            var apart = new[] { one.ClientKey, other.ClientKey }
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => "agent (" + key.Substring(0, ClientList.ShortKeyLength) + ")");
            Assert.That(named, Is.EqualTo(apart.Concat(new[] { "agent-renewing" })));
        }

        [Test]
        public void A_redraw_from_the_database_puts_the_last_press_line_away()
        {
            // A renewal landing while the page is open redraws it; the line from the last press
            // used to stay, so a client could be listed under one saying it could not reach the
            // database.
            var record = Record("agent-fictitious", "ops");

            using (var list = ListOf(Refuses, record, Record("agent-invented", "ops")))
            {
                Assume.That(list.Revoke(1), Does.Contain("still admitted"));

                list.Fill(new AdmissionListing(new[] { record, Record("agent-invented", "ops") }, true), null, null);

                Assert.That(list.Result, Is.Null);
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
        public void Where_the_store_takes_no_write_no_row_carries_the_X()
        {
            // `STORE.md` §636: no command on the store where nothing is written into it in the
            // session, as the first page turns its buttons off.
            var client = Record("agent-fictitious", "ops");
            using (var list = ListOf(Allows, client))
            {
                list.Writable = false;
                list.Fill(new AdmissionListing(new[] { client }, true), null, null);

                Assert.That(list.Rows[0].Revocable, Is.False);
                Assert.That(Cell(list, 0, Grid(list).Columns[ClientList.RevokeColumnName].Index), Is.Empty);
                Assert.That(Grid(list).Rows[0].Cells[ClientList.RevokeColumnName].ToolTipText, Is.Empty);

                list.Writable = true;
                list.Fill(new AdmissionListing(new[] { client }, true), null, null);

                Assert.That(Cell(list, 0, Grid(list).Columns[ClientList.RevokeColumnName].Index), Is.EqualTo(ClientList.RevokeMark));
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
        public void A_press_on_a_heading_leaves_every_X_on_the_client_its_row_shows()
        {
            // A text column sorts its grid at a press on its heading unless told otherwise, and
            // a row is acted on by its place in the list as it was filled: with the grid in
            // another order, the X beside a client revoked another client.
            var later = Record("zulu-fictitious", "ops");
            var earlier = Record("alpha-fictitious", "ops");
            var revoked = new List<string>();

            using (var list = ListOf(
                key =>
                {
                    revoked.Add(key);
                    return null;
                },
                later,
                earlier))
            {
                var grid = Grid(list);
                Raise(grid, "OnColumnHeaderMouseClick", new DataGridViewCellMouseEventArgs(
                    0, -1, 0, 0, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)));

                var shown = grid.Rows.Cast<DataGridViewRow>()
                    .Single(row => Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture)
                        .Contains("alpha-fictitious"));
                Raise(grid, "OnCellClick", new DataGridViewCellEventArgs(
                    grid.Columns[ClientList.RevokeColumnName].Index, shown.Index));

                Assert.That(revoked, Is.EqualTo(new[] { earlier.ClientKey }),
                    "the X beside a client revokes that client, whatever was pressed before it");
                Assert.That(list.Clients, Is.EqualTo(new[] { later }));
            }
        }

        /// <summary>Raises an event of the grid as its window does for a press of the mouse.</summary>
        private static void Raise(DataGridView grid, string method, EventArgs pressed)
        {
            typeof(DataGridView)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(grid, new object[] { pressed });
        }

        [Test]
        public void A_refused_revocation_leaves_the_client_where_it_was()
        {
            // On the tab a revocation goes into the dialog's buffer, so the press is refused only
            // for a database gone from under the dialog, and what reaches the database is decided
            // at OK. A list that dropped the row anyway would be showing the operator a database
            // that does not exist, and the client would be back on the next look.
            using (var list = ListOf(Refuses, Record("agent-fictitious", "ops")))
            {
                var said = list.Revoke(0);

                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(said, Does.Contain("still admitted"));
                Assert.That(said, Does.Contain("the database is not open"), "the operator is told why");
            }
        }

        [Test]
        public void A_refusal_carrying_somebody_elses_message_is_cut_to_what_the_page_can_hold()
        {
            // K088: the band under the pages and the detail on the first page cut what they are
            // given, and this third place a refusal lands did not. Nothing on the tab scrolls.
            var rambling = string.Join(" ", Enumerable.Repeat("the store said something long", 30));

            using (var list = ListOf(key => rambling, Record("agent-fictitious", "ops")))
            {
                var said = list.Revoke(0);

                Assert.That(said, Does.StartWith("That client is still admitted:"));
                Assert.That(said, Does.EndWith("..."));
                Assert.That(said.Length, Is.LessThanOrEqualTo(Clipped.Limit + 3));
                Assert.That(list.Result, Is.EqualTo(said));
            }
        }

        [Test]
        public void A_revocation_that_went_through_puts_nothing_under_the_list()
        {
            // The operator's ruling of 2026-11-09. The row going is what shows it, and the note
            // under the pages states that changes apply on OK; a sentence here stating both again
            // took up to two of the list's lines. Only a gesture that did not go through puts
            // words under the list.
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops"), Record("agent-invented", "ops")))
            {
                Assert.That(list.Revoke(0), Is.Null);
                Assert.That(list.Result, Is.Null);
                Assert.That(NoteOn(list), Is.Empty);
            }
        }

        [Test]
        public void A_list_emptied_by_revoking_is_not_called_a_database_that_admitted_nobody()
        {
            // The operator's ruling of 2026-11-09: every client revoked here still reaches the
            // database until OK, so the line under the emptied list names the wait, and keeps
            // naming it when the host draws the list again from the page's own copy.
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops")))
            {
                Assert.That(list.Revoke(0), Is.Null);
                Assert.That(NoteOn(list), Is.EqualTo(ClientList.EmptiedText));

                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), true), null, null);
                Assert.That(NoteOn(list), Is.EqualTo(ClientList.EmptiedText), "after a redraw");
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
            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), false), null, null);

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
            using (var list = ListOf(Refuses, Record("agent-fictitious", "ops")))
            {
                list.Revoke(0);
                Assert.That(NoteOn(list), Does.Contain("still admitted"));

                // Reading the database again is what the tab does after a gesture. If that read
                // fails, the gesture's line over an empty grid reads as though it were about what
                // is listed, when nothing could be.
                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), false), null, null);

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

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), Judged(record, ClientReach.Stranger), null);

                Assert.That(Reach(list, 0), Is.EqualTo(ClientList.StrangerText));

                // And the row stays, because it is still a record and taking it away is still the
                // operator's to do. That is why it is marked rather than hidden.
                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(list.Revoke(0), Is.Null);
                Assert.That(list.Clients, Is.Empty);
            }

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, null);

                Assert.That(Reach(list, 0), Is.Not.EqualTo(ClientList.StrangerText),
                    "a record nobody called a stranger is answered with its dates");
            }
        }

        [Test]
        public void An_admission_naming_an_authority_that_is_not_served_says_so_where_a_date_would_be()
        {
            // `STORE.md` §615: an admission whose `issuer` is not the authority served admits
            // nobody, whatever its certificate chains to. What the listing judges is what the row
            // shows, so the certificate's date is not shown as a client that works until then.
            var record = Record("agent-fictitious", "ops");
            var listing = new AdmissionListing(
                new[] { record },
                new Dictionary<AdmissionRecord, AdmissionStanding> { { record, AdmissionStanding.NotServed } },
                null,
                0,
                true);

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(listing, Judged(record, ClientReach.Reaches), null);

                Assert.That(Reach(list, 0), Is.EqualTo(ClientList.StrangerText));
                Assert.That(list.Clients.Count, Is.EqualTo(1), "and the row stays, its client the operator's to revoke");
            }
        }

        [TestCase(ClientReach.NotForClients, ClientList.NotForClientsText)]
        [TestCase(ClientReach.NotYetValid, ClientList.NotYetValidText)]
        public void What_the_gate_refuses_before_the_dates_is_said_where_a_date_would_be(ClientReach reach, string said)
        {
            // H055 and J064: the gate asks for client authentication and for a certificate that
            // has begun before it looks at the record, and the page asked neither, so a record
            // carrying a host certificate, or one not valid yet, read as a client admitted until
            // next year.
            var record = Record("agent-fictitious", "ops");

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), Judged(record, reach), null);

                Assert.That(Reach(list, 0), Is.EqualTo(said));
                Assert.That(list.Clients.Count, Is.EqualTo(1), "and it is still a record to take away");
            }
        }

        [Test]
        public void A_record_that_will_not_read_is_listed_under_its_client_and_goes_with_the_client()
        {
            // A stored record that does not read admits nobody, and it has a row, so the warning on
            // the authority page names something this page shows: under the client its client key
            // names, by the start of the certificate it names, marked as not counted.
            var client = Record("agent-fictitious", "ops");
            var damaged = new UnreadAdmission(new string('e', 32), client.ClientKey, new string('a', 64));
            var asked = new List<string>();

            using (var list = new ClientList(key =>
            {
                asked.Add(key);
                return null;
            }, Allows))
            {
                list.Fill(Listings.Of(new[] { client }, new[] { damaged }, 0, true), null, null);

                Assert.That(list.Clients.Count, Is.EqualTo(1), "a record that will not read is no client");
                Assert.That(list.Unreadable, Is.EqualTo(new[] { damaged }));
                Assert.That(Rows(list), Is.EqualTo(2), "one row per stored record");
                Assert.That(Cell(list, 1, 0), Is.EqualTo("Certificate aaaaaaaa"));
                Assert.That(Reach(list, 1), Is.EqualTo("not counted"));
                Assert.That(list.Rows[1].Revocable, Is.False, "under its client, it has no X of its own");

                Assert.That(list.Revoke(0), Is.Null);
                Assert.That(asked, Is.EqualTo(new[] { client.ClientKey }));
                Assert.That(list.Unreadable, Is.Empty, "its row went with its client");
                Assert.That(NoteOn(list), Is.EqualTo(ClientList.EmptiedText),
                    "the records are still stored until OK, as a client is");
            }
        }

        [Test]
        public void A_record_that_will_not_read_and_names_no_client_offers_no_revocation()
        {
            var damaged = new UnreadAdmission(new string('e', 32), null, new string('b', 64));

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(Listings.Of(Array.Empty<AdmissionRecord>(), new[] { damaged }, 0, true), null, null);

                Assert.That(list.Rows[0].Revocable, Is.False);
                Assert.That(list.Revoke(0), Does.Contain("names no client"));
                Assert.That(Rows(list), Is.EqualTo(1));
            }
        }

        [Test]
        public void A_refused_revocation_of_a_client_whose_record_will_not_read_leaves_its_row()
        {
            var damaged = new UnreadAdmission(new string('e', 32), new string('c', 64), new string('b', 64));

            using (var list = new ClientList(Refuses, Allows))
            {
                list.Fill(Listings.Of(Array.Empty<AdmissionRecord>(), new[] { damaged }, 0, true), null, null);

                Assert.That(list.Rows[0].Revocable, Is.True, "standing alone, it is its client's own row");
                var said = list.Revoke(0);

                Assert.That(said, Does.StartWith("That client is still admitted:"));
                Assert.That(said, Does.Contain("the database is not open"), "the operator is told why");
                Assert.That(list.Unreadable, Is.EqualTo(new[] { damaged }));
                Assert.That(Rows(list), Is.EqualTo(1));
            }
        }

        [Test]
        public void A_list_of_records_that_will_not_read_is_not_called_empty()
        {
            // Nobody is admitted by such a record, and a sentence under its row saying nobody ever
            // was would contradict the row it sits under.
            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(
                    Listings.Of(Array.Empty<AdmissionRecord>(), new[] { new UnreadAdmission(new string('e', 32), null, new string('c', 64)) }, 0, true),
                    null,
                    null);

                Assert.That(NoteOn(list), Is.Not.EqualTo(ClientList.NobodyText));
            }
        }

        private static IDictionary<string, ClientReach> Judged(AdmissionRecord record, ClientReach reach)
        {
            return new Dictionary<string, ClientReach> { { record.AdmissionKey, reach } };
        }

        [Test]
        public void Removed_clients_a_merge_brought_back_are_counted_under_the_list_rather_than_listed()
        {
            // A stale copy merged in brings a record back and the revocation beside it refuses
            // it. A row would read as a client, which is a page promising access nobody has; saying
            // nothing would hide that an old copy was merged in. So it is one line, until the next
            // write this plugin makes takes the records away.
            var client = Record("agent-fictitious", "ops");

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(Listings.Of(new[] { client }, 2, true), null, null);

                Assert.That(list.Clients.Count, Is.EqualTo(1), "not clients, so not rows");
                Assert.That(list.Reappeared,
                    Is.EqualTo("2 removed clients came back in a merge; still refused."));

                list.Revoke(0);
                Assert.That(list.Reappeared, Is.Not.Null, "the answer to a gesture does not hide it");

                list.Fill(Listings.Of(Array.Empty<AdmissionRecord>(), 1, true), null, null);
                Assert.That(list.Reappeared,
                    Is.EqualTo("1 removed client came back in a merge; still refused."), "one is one");

                list.Fill(new AdmissionListing(new[] { client }, true), null, null);
                Assert.That(list.Reappeared, Is.Null, "and nothing is said once they are gone");
            }
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        [NonParallelizable]
        [Desktop]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void The_line_a_merge_adds_stays_straight_under_the_list_whichever_line_came_first(
            bool gestureFirst, bool mergeFirst)
        {
            // The count of revoked clients sits between the list and the line a gesture answers
            // on, and the two had swapped once the page was on screen: a line shown before its
            // page has a window is moved when the page gets one, to after the last line that
            // already has one. So each line is shown first in turn, before the list is on screen,
            // and the other one after. Shown, so it runs alone and holds the desktop: the window
            // takes the activation from whatever held it, in this process or another.
            var client = Record("agent-fictitious", "ops");

            using (var form = Fakes.OffScreenForm.New())
            using (var list = new ClientList(Allows, Allows))
            {
                // The gesture's line is up from the start on an empty list, which says nobody is
                // admitted; with a client on it the line waits for a gesture.
                var listing = mergeFirst
                    ? Listings.Of(gestureFirst ? Array.Empty<AdmissionRecord>() : new[] { client }, 2, true)
                    : new AdmissionListing(Array.Empty<AdmissionRecord>(), true);
                list.Fill(listing, null, null);

                form.Controls.Add(list);
                form.Show();
                Fakes.OffScreenForm.Pump();

                if (!mergeFirst)
                {
                    list.Fill(Listings.Of(Array.Empty<AdmissionRecord>(), 2, true), null, null);
                }

                if (!gestureFirst)
                {
                    list.Revoke(0);
                }

                Fakes.OffScreenForm.Pump();
                list.PerformLayout();

                var grid = ControlTree.FindByName<DataGridView>(list, ClientList.ListName);
                var reappeared = ControlTree.FindByName<Label>(list, ClientList.ReappearedLabelName);
                var said = ControlTree.FindByName<Label>(list, ClientList.ResultLabelName);

                Assert.That(reappeared.Visible && said.Visible, Is.True, "both lines are on screen");
                Assert.That(reappeared.Top, Is.EqualTo(grid.Bottom), "the count sits straight under the list");
                Assert.That(said.Top, Is.EqualTo(reappeared.Bottom), "and the gesture's line under that");

                form.Controls.Remove(list);
            }
        }

        /// <summary>The three standing answers, by what the row says about each.</summary>
        private static IEnumerable<TestCaseData> Answers()
        {
            yield return new TestCaseData(RenewalRefusal.Forever(), "Renewal refused forever").SetName("forever");
            yield return new TestCaseData(RenewalRefusal.ThisSession(), "Renewal refused in this session")
                .SetName("in this session");

            // A day the operator chose, which begins at midnight where they are.
            var day = new DateTime(2031, 12, 28, 0, 0, 0, DateTimeKind.Local);
            yield return new TestCaseData(RenewalRefusal.Until(new DateTimeOffset(day)), "Renewal refused until 2031-12-28")
                .SetName("until a date");
        }

        [TestCaseSource(nameof(Answers))]
        public void A_client_with_a_standing_answer_has_its_name_in_red_and_the_answer_on_its_row(
            RenewalRefusal standing, string said)
        {
            // M001, as the operator decided it on real screenshots: no column, the name in the
            // tab's own red, the answer in the tooltip of every text cell and in the row's
            // description, and a selected row in the grid's own colors.
            var refused = Record("agent-fictitious", "ops");
            var asked = Record("agent-invented", "ops");

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(
                    new AdmissionListing(new[] { refused, asked }, true),
                    null,
                    null,
                    new Dictionary<string, RenewalRefusal> { { refused.AdmissionKey, standing } });

                var row = Grid(list).Rows[0];
                Assert.That(ClientList.AnswerText(standing), Is.EqualTo(said));
                Assert.That(list.StandingAt(0), Is.SameAs(standing));
                Assert.That(row.Cells[0].InheritedStyle.ForeColor, Is.EqualTo(ClientList.StandingColor));
                Assert.That(ClientList.StandingColor, Is.EqualTo(Color.Firebrick));
                Assert.That(row.Cells[0].InheritedStyle.SelectionForeColor,
                    Is.EqualTo(Grid(list).DefaultCellStyle.SelectionForeColor), "a selected row keeps the grid's colors");
                for (var column = 1; column < 4; column++)
                {
                    Assert.That(row.Cells[column].Style.ForeColor, Is.EqualTo(Color.Empty), "only the name is red");
                }

                for (var column = 0; column < 4; column++)
                {
                    Assert.That(Plain(row.Cells[column].ToolTipText), Is.EqualTo(Keys(said, refused)),
                        "every text cell names it, above the keys");
                }

                Assert.That(Plain(row.Cells[ClientList.RevokeColumnName].ToolTipText), Does.Not.Contain(said),
                    "the X shows what it does");
                Assert.That(row.AccessibilityObject.Description, Is.EqualTo(said));

                var other = Grid(list).Rows[1];
                Assert.That(list.StandingAt(1), Is.Null);
                Assert.That(other.Cells[0].Style.ForeColor, Is.EqualTo(Color.Empty));
                Assert.That(Plain(other.Cells[0].ToolTipText), Is.EqualTo(Keys(null, asked)), "the keys alone");
                Assert.That(other.AccessibilityObject.Description, Is.Null);
            }
        }

        [TestCaseSource(nameof(Answers))]
        public void Allowing_renewal_lifts_the_answer_through_the_host_and_the_red_goes_at_once(
            RenewalRefusal standing, string said)
        {
            var record = Record("agent-fictitious", "ops");
            var lifted = new List<string>();

            using (var list = new ClientList(Allows, key =>
                   {
                       lifted.Add(key);
                       return null;
                   }))
            {
                list.Fill(
                    new AdmissionListing(new[] { record }, true),
                    null,
                    null,
                    new Dictionary<string, RenewalRefusal> { { record.AdmissionKey, standing } });

                var line = list.AllowRenewal(0);

                Assert.That(lifted, Is.EqualTo(new[] { record.AdmissionKey }), "by the certificate's key");
                Assert.That(line, Is.Null, "the name stops being red, which is what shows it");
                Assert.That(list.StandingAt(0), Is.Null);
                Assert.That(Grid(list).Rows[0].Cells[0].Style.ForeColor, Is.EqualTo(Color.Empty));
                Assert.That(Plain(Grid(list).Rows[0].Cells[0].ToolTipText), Is.EqualTo(Keys(null, record)));
                Assert.That(Grid(list).Rows[0].AccessibilityObject.Description, Is.Null);
                Assert.That(list.Clients.Count, Is.EqualTo(1), "and the client stays admitted");
            }
        }

        [Test]
        public void Allowing_renewal_where_no_answer_stands_asks_the_host_nothing()
        {
            var asked = 0;
            using (var list = new ClientList(Allows, key =>
                   {
                       asked++;
                       return null;
                   }))
            {
                list.Fill(new AdmissionListing(new[] { Record("agent-fictitious", "ops") }, true), null, null, null);

                Assert.That(list.AllowRenewal(0), Does.Contain("no standing answer"));
                Assert.That(list.AllowRenewal(3), Does.Contain("no such certificate"));
                Assert.That(asked, Is.Zero);
            }
        }

        [Test]
        public void A_refused_lift_leaves_the_name_red_and_says_why()
        {
            var record = Record("agent-fictitious", "ops");

            using (var list = new ClientList(Allows, Refuses))
            {
                list.Fill(
                    new AdmissionListing(new[] { record }, true),
                    null,
                    null,
                    new Dictionary<string, RenewalRefusal> { { record.AdmissionKey, RenewalRefusal.Forever() } });

                var line = list.AllowRenewal(0);

                Assert.That(line, Does.Contain("still stands").And.Contain("the database is not open"));
                Assert.That(list.StandingAt(0).IsForever, Is.True);
                Assert.That(Grid(list).Rows[0].Cells[0].Style.ForeColor, Is.EqualTo(ClientList.StandingColor));
            }
        }

        [Test]
        public void An_until_date_is_named_by_the_day_the_operator_chose_where_they_are()
        {
            // Every other moment on the page is UTC; this one is the day picked on a calendar, and
            // the answer ends where that day begins, so the UTC moment would name the day before
            // for anybody east of Greenwich.
            var start = new DateTime(2031, 3, 1, 0, 0, 0, DateTimeKind.Local);

            Assert.That(
                ClientList.AnswerText(RenewalRefusal.Until(new DateTimeOffset(start))),
                Is.EqualTo("Renewal refused until 2031-03-01"));
            Assert.That(ClientList.AnswerText(null), Is.Null);
        }

        /// <summary>A tooltip without the marks that keep it left to right.</summary>
        private static string Plain(string tip)
        {
            return tip.Replace(Tooltips.LeftToRightMark.ToString(), string.Empty);
        }

        /// <summary>
        /// What a row's tooltip holds, without the marks: the name, the answer, if any, the
        /// profiles, none of them removed, and the two keys.
        /// </summary>
        private static string Keys(string answer, AdmissionRecord record)
        {
            return ClientList.Describe(record) + Environment.NewLine
                   + (answer == null ? string.Empty : answer + Environment.NewLine)
                   + string.Concat(record.Profiles.Select(profile => "Profile " + profile + Environment.NewLine))
                   + "Client key " + record.ClientKey + Environment.NewLine
                   + "Certificate " + record.AdmissionKey;
        }

        [Test]
        public void Each_row_names_its_client_key_and_its_certificate_in_full()
        {
            // Two rows under one name are one client that renewed or two that chose the name, and
            // the client key in the tooltip is what tells which; the certificate's own is the key
            // the audit names it by.
            var older = Record("agent", "ops");
            var renewed = Renewal(older);
            var other = Record("agent", "ops");

            using (var list = ListOf(Allows, renewed, older, other))
            {
                var tips = Grid(list).Rows.Cast<DataGridViewRow>()
                    .Select(row => Plain(row.Cells[0].ToolTipText))
                    .ToList();

                Assert.That(tips, Is.EqualTo(new[] { Keys(null, renewed), Keys(null, older), Keys(null, other) }));
                Assert.That(tips[0], Does.Contain("Client key " + older.AdmissionKey), "a renewal is the client that paired");
                Assert.That(tips[2], Does.Not.Contain(older.ClientKey), "another program under the name is another client");
            }
        }

        [Test]
        public void A_held_profile_the_database_no_longer_names_is_marked_as_removed()
        {
            // A record keeps every name it was approved for, and the gate grants only the ones the
            // database still names. The row lists the other one too, marked, because adding the
            // name back gives it to this client again.
            var record = Record("agent-fictitious", "reader", "ops");

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, new[] { "reader" });

                Assert.That(Held(list, 0), Is.EqualTo(new[] { "ops" + ClientList.RemovedSuffix, "reader" }));
            }

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, null);

                Assert.That(Held(list, 0), Is.EqualTo(new[] { "ops", "reader" }),
                    "a list nobody was asked about marks nothing");
            }
        }

        [Test]
        public void Every_profile_a_row_holds_is_named_whole_in_its_tooltip()
        {
            // A profile name may be as long as `Longest`, and the column is about sixteen
            // characters wide on the settings page, so a name is cut there. The tooltip is where it
            // is read whole, marked as removed where it is, because the row is where the operator
            // looks to see what revoking the client takes away.
            var record = Record("agent-fictitious", Longest, "ops");

            using (var list = new ClientList(Allows, Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, new[] { "ops" });

                var tip = Plain(Grid(list).Rows[0].Cells[1].ToolTipText)
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                Assert.That(tip, Does.Contain("Profile " + Longest + ClientList.RemovedSuffix), string.Join(" | ", tip));
                Assert.That(tip, Does.Contain("Profile ops"), string.Join(" | ", tip));
            }
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        [NonParallelizable]
        [Desktop]
        public void A_profile_after_a_name_wider_than_its_column_is_drawn_in_the_row()
        {
            // The grid sized the row a line per profile, since a name has no space to fold at,
            // and then drew the list folded into the column: the long name took more lines than
            // it was given, and `ops` after it went below the row, where nobody could see that
            // the client holds it. Shown, so it runs alone and holds the desktop.
            Drawn(Record("agent-fictitious", Longest, "ops"), null, (picture, text, line, font) =>
            {
                Assert.That(text.Height, Is.GreaterThanOrEqualTo(2 * line).And.LessThan(3 * line),
                    "the row is not a line per profile");
                Assert.That(Ink(picture, Band(text, line, 0)), Is.Not.Null, "the long name is not drawn");
                AssertDrawn(picture, Band(text, line, 1), "ops", font, SystemColors.WindowText);
            });
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        [NonParallelizable]
        [Desktop]
        public void A_removed_name_wider_than_its_column_keeps_its_mark_whole()
        {
            // The mark is what tells the operator the name grants nothing, so it is the part that
            // may not be cut: the name is cut where it has to be, and the mark goes on the next
            // line, where the grid already gave it room when it sized the row.
            Drawn(Record("agent-fictitious", Longest, "ops"), new[] { "ops" }, (picture, text, line, font) =>
            {
                Assert.That(text.Height, Is.GreaterThanOrEqualTo(3 * line).And.LessThan(4 * line),
                    "the row is not a line each for the name, its mark and ops");
                Assert.That(Ink(picture, Band(text, line, 0)), Is.Not.Null, "the long name is not drawn");
                AssertDrawn(picture, Band(text, line, 1), ClientList.RemovedSuffix.Trim(), font, SystemColors.GrayText);
                AssertDrawn(picture, Band(text, line, 2), "ops", font, SystemColors.WindowText);
            });
        }

        /// <summary>
        /// The longest name a profile may have, with no space in it for a line to fold at.
        /// </summary>
        private static readonly string Longest = "a-profile-named-at-the-greatest-length-" + new string('x', 25);

        /// <summary>
        /// The one row of a list holding this record, drawn at the size of the settings page with
        /// nothing selected, handed over as a picture of the grid, the rectangle the profiles cell
        /// draws its text in, the height of one line of it and its font.
        /// </summary>
        private static void Drawn(
            AdmissionRecord record, ICollection<string> named, Action<Bitmap, Rectangle, int, Font> look)
        {
            Assert.That(Longest.Length, Is.EqualTo(64));
            Assert.That(AuthorityIssuer.IsUsableProfileName(Longest), Is.True);

            using (var form = Fakes.OffScreenForm.New())
            using (var list = new ClientList(Allows, Allows) { Dock = DockStyle.Fill })
            {
                form.ClientSize = Gallery.Gallery.SettingsPage;
                form.Controls.Add(list);
                form.Show();
                list.Fill(new AdmissionListing(new[] { record }, true), null, named);
                Fakes.OffScreenForm.Pump();

                var grid = Grid(list);
                grid.ClearSelection();
                Fakes.OffScreenForm.Pump();

                var style = grid.Rows[0].Cells[1].InheritedStyle;
                var cell = grid.GetCellDisplayRectangle(1, 0, false);
                Assert.That(cell.Width, Is.GreaterThan(0), "the cell was never laid out");

                // Where a wrapping text box cell draws: inside the border it owns on the right and
                // at the bottom, inside the padding, a pixel down and a pixel short of the border.
                var text = new Rectangle(
                    cell.Left + style.Padding.Left,
                    cell.Top + style.Padding.Top + 1,
                    cell.Width - 1 - style.Padding.Horizontal,
                    cell.Height - 1 - style.Padding.Vertical - 2);

                using (var picture = new Bitmap(grid.Width, grid.Height))
                {
                    grid.DrawToBitmap(picture, new Rectangle(Point.Empty, grid.Size));
                    look(picture, text, TextRenderer.MeasureText("ops", style.Font).Height, style.Font);
                }

                form.Controls.Remove(list);
            }
        }

        /// <summary>The band of a cell's text one line holds, counted from the top.</summary>
        private static Rectangle Band(Rectangle text, int line, int index)
        {
            return new Rectangle(text.Left, text.Top + (index * line), text.Width, line);
        }

        /// <summary>
        /// Asserts that a band holds this text and nothing else: drawn from its left edge, and as
        /// wide as the same text drawn alone in the same font and color.
        /// </summary>
        private static void AssertDrawn(Bitmap picture, Rectangle band, string text, Font font, Color color)
        {
            var drawn = Ink(picture, band);
            Assert.That(drawn, Is.Not.Null, "nothing is drawn where \"" + text + "\" belongs");

            int[] alone;
            var size = TextRenderer.MeasureText(text, font);
            using (var reference = new Bitmap(size.Width, size.Height))
            {
                using (var canvas = Graphics.FromImage(reference))
                {
                    canvas.Clear(SystemColors.Window);
                    TextRenderer.DrawText(canvas, text, font, Point.Empty, color, SystemColors.Window, TextFormatFlags.NoPrefix);
                }

                alone = Ink(reference, new Rectangle(Point.Empty, reference.Size));
            }

            Assert.That(drawn[0] - band.Left, Is.EqualTo(alone[0]).Within(1),
                "\"" + text + "\" does not start where its line does");
            Assert.That(drawn[1] - drawn[0], Is.EqualTo(alone[1] - alone[0]).Within(1),
                "what is drawn where \"" + text + "\" belongs is not that text, whole");
        }

        /// <summary>
        /// The first and last columns of a band that hold anything darker than the paper, or null
        /// when the band is empty.
        /// </summary>
        private static int[] Ink(Bitmap picture, Rectangle band)
        {
            band.Intersect(new Rectangle(Point.Empty, picture.Size));

            int[] found = null;
            for (var x = band.Left; x < band.Right; x++)
            {
                for (var y = band.Top; y < band.Bottom; y++)
                {
                    var pixel = picture.GetPixel(x, y);
                    if (((pixel.R + pixel.G + pixel.B) / 3) < 224)
                    {
                        found = found == null ? new[] { x, x } : new[] { found[0], x };
                        break;
                    }
                }
            }

            return found;
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
        [NonParallelizable]
        [Desktop]
        public void A_moment_heading_stays_on_one_line_over_an_empty_list()
        {
            // Sized to what it holds, a moment column with no rows under it holds only its heading,
            // and a heading free to wrap was given the width that folds it: the README's film
            // showed "Admitted (UTC)" and "Until (UTC)" on two lines each, over a page that had
            // admitted nobody yet. On a form that is shown, since a grid that only has a handle
            // never wraps its headings and passes whatever they are set to. Shown, so it runs
            // alone and holds the desktop: the window takes the activation from whatever held it,
            // in this process or another.
            using (var form = new Form { ClientSize = new Size(431, 200) })
            using (var list = new ClientList(Allows, Allows) { Dock = DockStyle.Fill })
            {
                form.Controls.Add(list);
                form.ShowInTaskbar = false;
                form.Show();
                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), true), null, null);
                form.PerformLayout();
                Application.DoEvents();

                var grid = Grid(list);
                var font = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
                var line = TextRenderer.MeasureText("Admitted", font).Height;

                Assert.That(grid.ColumnHeadersHeight, Is.LessThan(2 * line), "the heading row is two lines tall");
                foreach (var header in new[] { ClientList.AdmittedHeader, ClientList.UntilHeader })
                {
                    var column = grid.Columns.Cast<DataGridViewColumn>().Single(c => c.HeaderText == header);
                    Assert.That(column.Width, Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(header, font).Width),
                        "\"" + header + "\" has less room than one line of it");
                }
            }
        }

        /// <summary>What one row says its client holds, a line per profile, read off the grid.</summary>
        private static string[] Held(ClientList list, int row)
        {
            var grid = ControlTree.FindByName<DataGridView>(list, ClientList.ListName);
            Assert.That(grid, Is.Not.Null, "the list has no grid on it");

            return Convert.ToString(grid.Rows[row].Cells[1].Value, CultureInfo.InvariantCulture)
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
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
            return Cell(list, row, 3);
        }

        /// <summary>What one cell says, read off the grid.</summary>
        private static string Cell(ClientList list, int row, int column)
        {
            return Convert.ToString(Grid(list).Rows[row].Cells[column].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>How many rows the grid holds, which is what the operator counts.</summary>
        private static int Rows(ClientList list)
        {
            return Grid(list).Rows.Count;
        }

        private static DataGridView Grid(ClientList list)
        {
            var grid = ControlTree.FindByName<DataGridView>(list, ClientList.ListName);
            Assert.That(grid, Is.Not.Null, "the list has no grid on it");
            return grid;
        }

        /// <summary>
        /// A list with those clients on it. Filling is separate from constructing because the tab
        /// builds the list once and reads the database many times.
        /// </summary>
        private static ClientList ListOf(Func<string, string> revoke, params AdmissionRecord[] records)
        {
            var list = new ClientList(revoke, Allows);
            list.Fill(new AdmissionListing(records, true), null, null);
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

        private static string Allows(string admissionKey)
        {
            return null;
        }

        private static string Refuses(string admissionKey)
        {
            return "the database is not open";
        }

        private static readonly DateTimeOffset Made = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

        private static int _made;

        private static AdmissionRecord Record(string name, params string[] profiles)
        {
            using (var certificate = Certificate(name))
            {
                // Each admitted a minute before the record made just before it, so the page,
                // which lists the certificate admitted last first, lists them in the order made.
                _made++;
                return AdmissionRecord.For(certificate, Made.AddMinutes(-_made), profiles, InventedIssuer.Id);
            }
        }

        /// <summary>The record a renewal of this one writes: the same client, a new certificate.</summary>
        private static AdmissionRecord Renewal(AdmissionRecord of)
        {
            using (var certificate = Certificate(ClientList.Describe(of)))
            {
                return AdmissionRecord.For(certificate, of.Admitted.AddSeconds(1), of.Profiles, InventedIssuer.Id, of.ClientKey, of.AdmissionKey);
            }
        }

        private static X509Certificate2 Certificate(string name)
        {
            return Minted.Named("CN=" + name)
                .Lasting(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30))
                .SelfSigned()
                .Read();
        }
    }
}
