using System;
using System.Collections.Generic;
using System.Drawing;
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
        public void Revoking_one_of_a_renewed_clients_two_rows_says_which_certificate_went()
        {
            // A renewal leaves the client two rows with one client key until it gives the older
            // up, and taking one away leaves it reaching the database through the other: the line
            // used to say the client could no longer reach it.
            var older = Record("agent-fictitious", "ops");
            var renewed = Renewal(older);

            using (var list = ListOf(Allows, renewed, older))
            {
                var said = list.Revoke(1);

                Assert.That(said, Does.Contain("other certificate still reaches this database"));
                Assert.That(said, Does.Not.Contain("can no longer reach"));
                Assert.That(list.Clients, Is.EqualTo(new[] { renewed }));

                said = list.Revoke(0);
                Assert.That(said, Does.Contain("agent-fictitious can no longer reach this database"),
                    "the last of them is the client going");
            }
        }

        [Test]
        public void Revoking_one_of_two_programs_under_one_name_says_that_one_went_and_which()
        {
            // Two programs that chose one name are two clients: taking one away is that client
            // gone, not a certificate of one still reaching the database, and the line names it
            // with the start of its client key so it is not read as the other.
            var first = Record("agent", "ops");
            var second = Record("agent", "ops");

            using (var list = ListOf(Allows, first, second))
            {
                var said = list.Revoke(1);

                Assert.That(said, Is.EqualTo(
                    "agent (" + second.ClientKey.Substring(0, ClientList.ShortKeyLength) + ") can no longer reach this database."));
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

            using (var list = ListOf(Allows, record, Record("agent-invented", "ops")))
            {
                Assume.That(list.Revoke(1), Does.Contain("can no longer reach"));

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
            using (var list = ListOf(Allows, Record("agent-fictitious", "ops")))
            {
                list.Revoke(0);
                Assert.That(NoteOn(list), Does.Contain("agent-fictitious"));

                // Reading the database again is what the tab does after a revocation. If that
                // read fails, `no longer admitted` over an empty grid reads as though the
                // revocation is why the grid is empty.
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

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), Judged(record, ClientReach.Stranger), null);

                Assert.That(Reach(list, 0), Is.EqualTo(ClientList.StrangerText));

                // And the row stays, because it is still a record and taking it away is still the
                // operator's to do. That is why it is marked rather than hidden.
                Assert.That(list.Clients.Count, Is.EqualTo(1));
                Assert.That(list.Revoke(0), Does.Contain("no longer"));
            }

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, null);

                Assert.That(Reach(list, 0), Is.Not.EqualTo(ClientList.StrangerText),
                    "a record nobody called a stranger is answered with its dates");
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

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), Judged(record, reach), null);

                Assert.That(Reach(list, 0), Is.EqualTo(said));
                Assert.That(list.Clients.Count, Is.EqualTo(1), "and it is still a record to take away");
            }
        }

        [Test]
        public void A_record_that_will_not_read_is_listed_by_its_key_and_its_X_takes_it_away()
        {
            // K024: a stored record that will not read admits nobody, and it had no row, so the
            // warning on the authority page named something this page never showed and the one way
            // to be rid of it on the tab was to recreate the authority. It is listed after the
            // clients, by the start of its key, and taken away by that key.
            var client = Record("agent-fictitious", "ops");
            var damaged = new string('a', 64);
            var asked = new List<string>();

            using (var list = new ClientList(key =>
            {
                asked.Add(key);
                return null;
            }))
            {
                list.Fill(new AdmissionListing(new[] { client }, new[] { damaged }, 0, true), null, null);

                Assert.That(list.Clients.Count, Is.EqualTo(1), "a record that will not read is no client");
                Assert.That(list.Unreadable, Is.EqualTo(new[] { damaged }));
                Assert.That(Rows(list), Is.EqualTo(2), "one row per stored record");
                Assert.That(Cell(list, 1, 0), Is.EqualTo(CertificateDigest.Shorten(damaged)));
                Assert.That(Reach(list, 1), Is.EqualTo(ClientList.UnreadableRecordText));

                var said = list.Revoke(1);

                Assert.That(asked, Is.EqualTo(new[] { damaged }), "taken away by its key");
                Assert.That(said, Does.Contain("taken away"));
                Assert.That(list.Unreadable, Is.Empty);
                Assert.That(Rows(list), Is.EqualTo(1), "and its row went with it");
                Assert.That(list.Clients.Count, Is.EqualTo(1), "the client beside it was not touched");
            }
        }

        [Test]
        public void A_refused_removal_of_a_record_that_will_not_read_leaves_its_row()
        {
            var damaged = new string('b', 64);

            using (var list = new ClientList(Refuses))
            {
                list.Fill(
                    new AdmissionListing(Array.Empty<AdmissionRecord>(), new[] { damaged }, 0, true), null, null);

                var said = list.Revoke(0);

                Assert.That(said, Does.StartWith("That record is still there:"));
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
            using (var list = new ClientList(Allows))
            {
                list.Fill(
                    new AdmissionListing(Array.Empty<AdmissionRecord>(), new[] { new string('c', 64) }, 0, true),
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
        public void Revoked_clients_a_merge_brought_back_are_counted_under_the_list_rather_than_listed()
        {
            // A stale copy merged in brings a record back and the revocation beside it refuses
            // it. A row would read as a client, which is a page promising access nobody has; saying
            // nothing would hide that an old copy was merged in. So it is one line, until the next
            // write this plugin makes takes the records away.
            var client = Record("agent-fictitious", "ops");

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { client }, 2, true), null, null);

                Assert.That(list.Clients.Count, Is.EqualTo(1), "not clients, so not rows");
                Assert.That(list.Reappeared,
                    Is.EqualTo("2 revoked clients reappeared in a merge; they stay refused."));

                list.Revoke(0);
                Assert.That(list.Reappeared, Is.Not.Null, "the answer to a gesture does not hide it");

                list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), 1, true), null, null);
                Assert.That(list.Reappeared,
                    Is.EqualTo("1 revoked client reappeared in a merge; it stays refused."), "one is one");

                list.Fill(new AdmissionListing(new[] { client }, true), null, null);
                Assert.That(list.Reappeared, Is.Null, "and nothing is said once they are gone");
            }
        }

        [Test]
        [Apartment(System.Threading.ApartmentState.STA)]
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
            // and the other one after.
            var client = Record("agent-fictitious", "ops");

            using (var form = Fakes.OffScreenForm.New())
            using (var list = new ClientList(Allows))
            {
                // The gesture's line is up from the start on an empty list, which says nobody is
                // admitted; with a client on it the line waits for a gesture.
                var listing = mergeFirst
                    ? new AdmissionListing(gestureFirst ? Array.Empty<AdmissionRecord>() : new[] { client }, 2, true)
                    : new AdmissionListing(Array.Empty<AdmissionRecord>(), true);
                list.Fill(listing, null, null);

                form.Controls.Add(list);
                form.Show();
                Fakes.OffScreenForm.Pump();

                if (!mergeFirst)
                {
                    list.Fill(new AdmissionListing(Array.Empty<AdmissionRecord>(), 2, true), null, null);
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

                Assert.That(row.Cells[ClientList.RevokeColumnName].ToolTipText, Is.Empty, "the X says what it does");
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
                Assert.That(line, Does.Contain("agent-fictitious").And.Contain("raises the dialog again"));
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
                Assert.That(list.AllowRenewal(3), Does.Contain("no such client"));
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

        /// <summary>What a row's tooltip says, without the marks: the answer, if any, and the two keys.</summary>
        private static string Keys(string answer, AdmissionRecord record)
        {
            return (answer == null ? string.Empty : answer + Environment.NewLine)
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

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, new[] { "reader" });

                Assert.That(Held(list, 0), Is.EqualTo(new[] { "reader", "ops" + ClientList.RemovedSuffix }));
            }

            using (var list = new ClientList(Allows))
            {
                list.Fill(new AdmissionListing(new[] { record }, true), null, null);

                Assert.That(Held(list, 0), Is.EqualTo(new[] { "reader", "ops" }),
                    "a list nobody was asked about marks nothing");
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
            var list = new ClientList(revoke);
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

        private static AdmissionRecord Record(string name, params string[] profiles)
        {
            using (var certificate = Certificate(name))
            {
                return AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles);
            }
        }

        /// <summary>The record a renewal of this one writes: the same client, a new certificate.</summary>
        private static AdmissionRecord Renewal(AdmissionRecord of)
        {
            using (var certificate = Certificate(ClientList.Describe(of)))
            {
                return AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, of.Profiles, of.ClientKey);
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
