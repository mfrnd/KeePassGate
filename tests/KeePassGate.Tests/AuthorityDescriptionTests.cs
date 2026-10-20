using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the tab says, and which buttons that leaves. The wording is tested because it is the
    /// only thing standing between an operator and a decision that revokes every client at once.
    /// </summary>
    [TestFixture]
    public sealed class AuthorityDescriptionTests
    {
        /// <summary>These records, listed in full. A test that means the other case says so.</summary>
        private static AdmissionListing Listed(params AdmissionRecord[] records)
        {
            return new AdmissionListing(records, true);
        }

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

        private InMemoryKeyValueStore _values;
        private AuthorityService _service;

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _service = new AuthorityService(_values, null);
        }

        [Test]
        public void Without_a_database_nothing_can_be_done()
        {
            var description = AuthorityDescription.NoDatabase();

            Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.NoDatabaseHeadline));
            Assert.That(description.CanCreate, Is.False);
            Assert.That(description.CanRecreate, Is.False);
            Assert.That(description.CanDelete, Is.False);
        }

        [Test]
        public void An_empty_database_offers_creation_and_nothing_else()
        {
            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, Now);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.AbsentHeadline));
                Assert.That(description.CanCreate, Is.True);
                Assert.That(description.CanRecreate, Is.False);
                Assert.That(description.CanDelete, Is.False);
            }
        }

        [Test]
        public void Records_without_an_authority_are_called_inert()
        {
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, Now);

                Assert.That(string.Join(" ", description.Details), Does.Contain("inert"));
            }
        }

        [Test]
        public void A_healthy_authority_offers_recreation_and_deletion_but_not_creation()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, Now);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.PresentHeadline));
                Assert.That(description.CanCreate, Is.False);
                Assert.That(description.CanRecreate, Is.True);
                Assert.That(description.CanDelete, Is.True);
            }
        }

        [Test]
        public void A_healthy_authority_shows_its_fingerprint_and_record_count()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, DateTimeOffset.UtcNow);

                Assert.That(Named(description, "Fingerprint"),
                    Is.EqualTo(CertificateDigest.Shorten(reading.Material.AuthorityDigest)));
                Assert.That(Named(description, "Admission records"), Is.EqualTo("0"));

                // A table of two, and nothing else unless something is wrong. The tab has to fit
                // without scrolling, every row costs the same height, and the sentences are the
                // part of it that grows. Until when the authority and its host certificate are
                // valid is not a row: they are issued for a hundred years.
                Assert.That(description.Facts.Count, Is.EqualTo(2));
                Assert.That(description.Details, Is.Empty);
            }
        }

        [Test]
        public void A_fact_is_a_name_and_a_value_rather_than_a_sentence()
        {
            // Which is what lets the tab lay them out as a table. A value that carried its own
            // label would put the label in the wrong column.
            _service.Create();

            using (var reading = _service.Read())
            {
                foreach (var fact in AuthorityDescription.Of(reading, null, null, DateTimeOffset.UtcNow).Facts)
                {
                    Assert.That(fact.Name, Is.Not.Empty);
                    Assert.That(fact.Value, Is.Not.Empty);
                    Assert.That(fact.Name, Does.Not.EndWith(":"), "the layout separates them");
                    Assert.That(fact.Value, Does.Not.Contain(fact.Name));
                }
            }
        }

        private static string Named(AuthorityDescription description, string name)
        {
            foreach (var fact in description.Facts)
            {
                if (fact.Name == name)
                {
                    return fact.Value;
                }
            }

            Assert.Fail("no fact called " + name);
            return null;
        }

        [Test]
        public void The_authority_and_its_host_are_never_warned_about_or_refused_on_their_dates()
        {
            // `CA.md` §219: issued for a hundred years, so nothing on the page counts down to their
            // end and nothing refuses an invitation ahead of it. Ten days before the end and a day
            // after it read the same as a fresh authority, which is what a database a century old
            // gets: `PAIRING.md` §311 answers the pairing itself, once the issuer refuses to sign.
            //
            // Asked of a clock a century on rather than of an authority made a century ago, because
            // `CertificateRequest` refuses to write a date before 1950 at all.
            _service.Create();

            using (var reading = _service.Read())
            {
                var end = new DateTimeOffset(reading.Material.Authority.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                foreach (var at in new[] { end - TimeSpan.FromDays(10), end + TimeSpan.FromDays(1) })
                {
                    var description = AuthorityDescription.Of(reading, null, null, at);

                    Assert.That(description.Details, Is.Empty, "at " + Moments.Wire(at));
                    Assert.That(description.CanInvite, Is.True, "at " + Moments.Wire(at));
                }
            }
        }

        [Test]
        public void A_readable_authority_is_one_anybody_can_be_invited_to()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, DateTimeOffset.UtcNow);

                Assert.That(description.CanInvite, Is.True);
            }
        }

        [Test]
        public void Damage_is_shown_with_what_is_wrong_and_what_it_costs()
        {
            _values.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "rubbish");
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, Now);
                var text = string.Join(Environment.NewLine, description.Details);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.DamagedHeadline));
                Assert.That(text, Does.Contain("Nothing is served"));
                Assert.That(text, Does.Contain("One admission record"));
                Assert.That(description.CanCreate, Is.False, "Creating over damage would hide it.");
                Assert.That(description.CanRecreate, Is.True);
                Assert.That(description.CanDelete, Is.True);
                Assert.That(description.CanInvite, Is.False,
                    "an authority that cannot be read cannot issue either");
            }
        }

        [Test]
        public void Several_authorities_say_that_nothing_is_served_and_offer_no_creation()
        {
            _service.Create();
            string first;
            using (var reading = _service.Read())
            {
                first = AuthorityKeys.AuthorityKey(reading.Material.AuthorityDigest);
            }

            var document = _values.Get(first);
            _values.Remove(first);
            _service.Create();
            _values.Plant(first, document);

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, Now);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.UndecidedHeadline));
                Assert.That(description.Facts, Is.Empty, "there is no one authority to describe");
                Assert.That(string.Join(" ", description.Details), Does.Contain("2 authorities"));
                Assert.That(description.CanCreate, Is.False, "there is no absence to fill");
                Assert.That(description.CanRecreate, Is.True, "recreating retires every one of them");
                Assert.That(description.CanDelete, Is.True);
                Assert.That(description.CanInvite, Is.False, "a code for any one of them would resolve to a refusal");
            }
        }

        [Test]
        public void Nothing_it_says_is_empty()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, null, Now);

                Assert.That(description.Headline, Is.Not.Empty);
                Assert.That(description.Details.All(d => !string.IsNullOrWhiteSpace(d)), Is.True);
            }
        }

        [Test]
        public void An_admitted_client_about_to_expire_is_mentioned()
        {
            // A client that expires simply stops being admitted one morning, which is exactly the
            // sort of failure a tab should see coming.
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null);
                var almostExpired = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)
                    .AddDays(-10);

                using (var reading = _service.Read())
                {
                    var text = string.Join(" ",
                        AuthorityDescription.Of(reading, Listed(record), null, almostExpired).Details);

                    Assert.That(text, Does.Contain("One client expires within 10 days."));
                }
            }
        }

        [TestCase(1, 8)]
        [TestCase(7, 56)]
        [TestCase(30, 240)]
        [TestCase(90, 720)]
        [TestCase(365, 720)]
        public void A_client_is_warned_about_in_the_last_third_of_its_own_life_and_thirty_days_at_most(
            int days, int hours)
        {
            // A fixed thirty days would warn about a week-long certificate from the moment it was
            // issued. The five minutes the issuer backdates by count as part of the life, which
            // moves each uncapped third by a hundred seconds.
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious", TimeSpan.FromDays(days)))
            {
                var warning = AuthorityDescription.ClientWarning(certificate);
                var expected = TimeSpan.FromHours(hours);

                Assert.That(warning, Is.EqualTo(expected).Within(TimeSpan.FromMinutes(2)));
            }
        }

        [TestCase(7)]
        [TestCase(30)]
        [TestCase(365)]
        public void A_client_is_warned_about_from_where_its_warning_starts_and_not_before(int days)
        {
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious", TimeSpan.FromDays(days)))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null);
                var end = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                var starts = end - AuthorityDescription.ClientWarning(certificate);

                using (var reading = _service.Read())
                {
                    Assert.That(Said(reading, record, starts.AddMinutes(-1)), Does.Not.Contain("expire"));
                    Assert.That(Said(reading, record, starts.AddMinutes(1)), Does.Contain("One client expires within"));
                }
            }
        }

        [Test]
        public void A_week_long_client_is_not_expiring_on_the_day_it_was_issued()
        {
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious", TimeSpan.FromDays(7)))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null);

                using (var reading = _service.Read())
                {
                    Assert.That(Said(reading, record, DateTimeOffset.UtcNow.AddHours(1)), Does.Not.Contain("expire"));
                }
            }
        }

        [Test]
        public void The_days_said_are_those_the_last_client_warned_about_expires_within()
        {
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var day = authority.IssueClient("agent-fictitious", TimeSpan.FromDays(1)))
            using (var week = authority.IssueClient("agent-invented", TimeSpan.FromDays(7)))
            {
                var records = new[]
                {
                    AdmissionRecord.For(day, DateTimeOffset.UtcNow, null),
                    AdmissionRecord.For(week, DateTimeOffset.UtcNow, null)
                };

                var dayEnd = new DateTimeOffset(day.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                var weekEnd = new DateTimeOffset(week.NotAfter.ToUniversalTime(), TimeSpan.Zero);

                using (var reading = _service.Read())
                {
                    // An hour before the day ends, only the day-long one is in its last third, and
                    // what it expires within is one day rather than one days.
                    Assert.That(
                        string.Join(" ", AuthorityDescription.Of(reading, Listed(records), null, dayEnd.AddHours(-1)).Details),
                        Does.Contain("One client expires within 1 day."));

                    // Two days before the week ends the day-long one has expired, and the week is
                    // in its last third.
                    var text = string.Join(" ",
                        AuthorityDescription.Of(reading, Listed(records), null, weekEnd.AddDays(-2)).Details);
                    Assert.That(text, Does.Contain("One client has expired."));
                    Assert.That(text, Does.Contain("One client expires within 2 days."));
                }
            }
        }

        private static string Said(AuthorityReading reading, AdmissionRecord record, DateTimeOffset now)
        {
            return string.Join(" ", AuthorityDescription.Of(reading, Listed(record), null, now).Details);
        }

        [TestCase(ClientReach.Stranger)]
        [TestCase(ClientReach.NotForClients)]
        [TestCase(ClientReach.NotYetValid)]
        public void A_record_the_gate_refuses_before_its_dates_is_not_warned_about(ClientReach reach)
        {
            // J065: the warning says a client is about to stop being admitted, and a record from
            // another authority never was: the clients page says "not this authority" beside it
            // while this page told the operator to expect it to expire.
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null);
                var almostExpired = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)
                    .AddDays(-10);
                var afterwards = almostExpired.AddDays(11);
                var reaches = new Dictionary<string, ClientReach> { { record.AdmissionKey, reach } };

                using (var reading = _service.Read())
                {
                    Assert.That(string.Join(" ",
                            AuthorityDescription.Of(reading, Listed(record), reaches, almostExpired).Details),
                        Does.Not.Contain("expire"));
                    Assert.That(string.Join(" ",
                            AuthorityDescription.Of(reading, Listed(record), reaches, afterwards).Details),
                        Does.Not.Contain("expire"));

                    reaches[record.AdmissionKey] = ClientReach.Reaches;
                    Assert.That(string.Join(" ",
                            AuthorityDescription.Of(reading, Listed(record), reaches, almostExpired).Details),
                        Does.Contain("One client expires within 10 days."),
                        "one the gate gets as far as the dates of is still warned about");
                }
            }
        }

        [Test]
        public void An_admitted_client_that_has_expired_is_mentioned()
        {
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null);
                var afterwards = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)
                    .AddDays(1);

                using (var reading = _service.Read())
                {
                    var text = string.Join(" ", AuthorityDescription.Of(reading, Listed(record), null, afterwards).Details);

                    Assert.That(text, Does.Contain("One client has expired"));
                }
            }
        }

        [Test]
        public void A_client_with_plenty_of_time_left_is_not_mentioned()
        {
            _service.Create();

            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, null);

                using (var reading = _service.Read())
                {
                    var text = string.Join(" ",
                        AuthorityDescription.Of(reading, Listed(record), null, DateTimeOffset.UtcNow).Details);

                    // On what the description can actually say. "admitted client" is not a phrase
                    // it produces in any state, so asserting its absence passed whatever the text
                    // said; expiry is what a client is ever mentioned for.
                    Assert.That(text, Does.Not.Contain("expire"),
                        "a client with plenty of time left is not worth a line");
                }
            }
        }

        [Test]
        public void A_record_the_table_counts_and_the_warnings_could_not_read_is_said_so()
        {
            // Otherwise the tab counts one thing and warns about another, with nothing on screen to
            // reconcile them: the fact is every stored key and only a readable record can be checked.
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + new string('a', 64), "not a record");

            using (var reading = _service.Read())
            {
                var text = string.Join(" ", AuthorityDescription.Of(
                    reading, Listed(), null, DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Contain(
                    "One admission record cannot be read and admits nobody. The Clients page lists it."));
            }

            // K024: the sentence says where the operator can act, which is the clients page and
            // its X, and says it in the plural once there is more than one.
            _values.Plant(AuthorityKeys.ClientPrefix + new string('b', 64), "not a record either");

            using (var reading = _service.Read())
            {
                var text = string.Join(" ", AuthorityDescription.Of(
                    reading, Listed(), null, DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Contain(
                    "2 admission records cannot be read and admit nobody. The Clients page lists them."));
            }
        }

        [Test]
        public void A_listing_that_could_not_be_made_is_not_reported_as_damage()
        {
            // The count and the records are two reads of the same dictionary. When the read that
            // produced the records fails, every stored record looks unreadable, and saying so
            // points the operator at Recreate, which destroys the authority and all of them.
            _service.Create();

            using (var reading = _service.Read())
            {
                var unread = new AdmissionListing(Array.Empty<AdmissionRecord>(), false);
                var text = string.Join(" ", AuthorityDescription.Of(reading, unread, null, DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Not.Contain("cannot be read"),
                    "a listing that was never made is not a count of damaged records");
                Assert.That(text, Does.Contain("could not be read"),
                    "and it says which it was");
            }
        }

        [Test]
        public void The_longest_pages_are_composed_by_what_composes_the_page_a_line_each()
        {
            // J031: the tab was measured against one paragraph written out by hand, which held
            // neither of the lines `Damage` adds, mixed damage with warnings never shown beside it,
            // and was joined with spaces; cut to six characters, it failed nothing.
            var longest = AuthorityDescription.LongestDescriptions();

            Assert.That(longest.Select(description => description.Headline), Is.EquivalentTo(new[]
            {
                AuthorityDescription.DamagedHeadline,
                AuthorityDescription.PresentHeadline,
                AuthorityDescription.UndecidedHeadline,
                AuthorityDescription.AbsentHeadline
            }), "every headline, since which is tallest is not something to assume");

            var damaged = longest.Single(description => description.Headline == AuthorityDescription.DamagedHeadline);
            var lines = damaged.DetailText.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var problems = AuthorityStore.LongestProblems(string.Empty);

            Assert.That(lines.Length, Is.EqualTo(problems.Count + 2), "every problem, then the two lines under them");
            Assert.That(lines[0], Is.EqualTo(problems[0]));
            Assert.That(lines[1], Is.EqualTo(problems[1]));
            Assert.That(lines[2], Does.StartWith(problems[2]));
            Assert.That(lines[2].Length, Is.EqualTo(Clipped.Limit + 3), "a message long enough to be cut");
            Assert.That(lines[3], Does.StartWith("Nothing is served while this is true."));
            Assert.That(lines[4], Is.EqualTo("9999 admission records would be removed with it."));

            var whole = longest.Single(description => description.Headline == AuthorityDescription.PresentHeadline);
            Assert.That(whole.Details.Count, Is.EqualTo(1), "the warnings are one line, as they are drawn");
            Assert.That(whole.DetailText, Does.Contain("clients have expired"));
            Assert.That(whole.DetailText, Does.Not.Contain("Authority"),
                "the authority and its host certificate are not warned about, CA.md §219");
            Assert.That(whole.Facts, Is.Not.Empty);
        }

        [Test]
        public void A_missing_reading_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => AuthorityDescription.Of(null, null, null, Now));
        }
    }
}
