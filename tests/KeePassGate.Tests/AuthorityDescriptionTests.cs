using System;
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
            Assert.That(description.IsServed, Is.False);
        }

        [Test]
        public void An_empty_database_offers_creation_and_nothing_else()
        {
            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, Now);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.AbsentHeadline));
                Assert.That(description.CanCreate, Is.True);
                Assert.That(description.CanRecreate, Is.False);
                Assert.That(description.CanDelete, Is.False);
                Assert.That(description.IsServed, Is.False);
            }
        }

        [Test]
        public void Records_without_an_authority_are_called_inert()
        {
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, Now);

                Assert.That(string.Join(" ", description.Details), Does.Contain("inert"));
            }
        }

        [Test]
        public void A_healthy_authority_offers_recreation_and_deletion_but_not_creation()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, Now);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.PresentHeadline));
                Assert.That(description.CanCreate, Is.False);
                Assert.That(description.CanRecreate, Is.True);
                Assert.That(description.CanDelete, Is.True);
                Assert.That(description.IsServed, Is.True);
            }
        }

        [Test]
        public void A_healthy_authority_shows_its_fingerprint_dates_and_record_count()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow);

                Assert.That(Named(description, "Fingerprint"),
                    Is.EqualTo(CertificateDigest.Shorten(reading.Material.AuthorityDigest)));
                Assert.That(Named(description, "Authority valid until"), Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"));
                Assert.That(Named(description, "Host valid until"), Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"));
                Assert.That(Named(description, "Admission records"), Is.EqualTo("0"));

                // A table of four, and nothing else unless something is wrong. The tab has to fit
                // without scrolling, every row costs the same height, and the sentences are the
                // part of it that grows.
                Assert.That(description.Facts.Count, Is.EqualTo(4));
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
                foreach (var fact in AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow).Facts)
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
        public void Timestamps_are_written_the_same_way_in_every_language()
        {
            _service.Create();

            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                using (var reading = _service.Read())
                {
                    var description = AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow);
                    Assert.That(Named(description, "Authority valid until"), Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"));
                }
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void An_expiry_inside_the_warning_window_is_mentioned()
        {
            var soon = DateTimeOffset.UtcNow;
            var service = new AuthorityService(
                _values, new AuthorityIssuer(() => soon - AuthorityIssuer.AuthorityLifetime + TimeSpan.FromDays(10)), null);
            service.Create();

            using (var reading = service.Read())
            {
                var text = string.Join(" ", AuthorityDescription.Of(reading, null, soon).Details);

                Assert.That(text, Does.Contain("Authority expires in"));
            }
        }

        [Test]
        public void An_expiry_far_away_is_not_mentioned()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var text = string.Join(" ", AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Not.Contain("expires in"));
            }
        }

        [Test]
        public void An_authority_that_has_expired_says_so()
        {
            var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(4000);
            var service = new AuthorityService(_values, new AuthorityIssuer(() => longAgo), null);
            service.Create();

            using (var reading = service.Read())
            {
                var text = string.Join(" ", AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Contain("Authority expired on"));
            }
        }

        [Test]
        public void An_authority_that_has_expired_is_one_nobody_can_be_invited_to()
        {
            // A narrower question than the three buttons on the first page answer. Recreating and
            // deleting are both offered here, because recreating is the remedy; what cannot be
            // offered is a code, since `PAIRING.md` §311 refuses a request on this authority
            // before anybody is asked anything.
            var longAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(4000);
            var service = new AuthorityService(_values, new AuthorityIssuer(() => longAgo), null);
            service.Create();

            using (var reading = service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow);

                Assert.That(description.HasExpired, Is.True);
                Assert.That(description.CanInvite, Is.False);
                Assert.That(description.CanRecreate, Is.True, "recreating is the way out of it");
            }
        }

        [Test]
        public void An_authority_inside_its_dates_is_one_anybody_can_be_invited_to()
        {
            _service.Create();

            using (var reading = _service.Read())
            {
                var description = AuthorityDescription.Of(reading, null, DateTimeOffset.UtcNow);

                Assert.That(description.HasExpired, Is.False);
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
                var description = AuthorityDescription.Of(reading, null, Now);
                var text = string.Join(Environment.NewLine, description.Details);

                Assert.That(description.Headline, Is.EqualTo(AuthorityDescription.DamagedHeadline));
                Assert.That(text, Does.Contain("Nothing is served"));
                Assert.That(text, Does.Contain("One admission record"));
                Assert.That(description.CanCreate, Is.False, "Creating over damage would hide it.");
                Assert.That(description.CanRecreate, Is.True);
                Assert.That(description.CanDelete, Is.True);
                Assert.That(description.IsServed, Is.False,
                    "damage is served to nobody, whatever the buttons offer");
                Assert.That(description.CanInvite, Is.False,
                    "an authority that cannot be read cannot issue either");
                Assert.That(description.HasExpired, Is.False,
                    "and its dates are not what is wrong with it");
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
                var description = AuthorityDescription.Of(reading, null, Now);

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
                var description = AuthorityDescription.Of(reading, null, Now);

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
                        AuthorityDescription.Of(reading, Listed(record), almostExpired).Details);

                    Assert.That(text, Does.Contain("One client expires within 30 days"));
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
                    var text = string.Join(" ", AuthorityDescription.Of(reading, Listed(record), afterwards).Details);

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
                        AuthorityDescription.Of(reading, Listed(record), DateTimeOffset.UtcNow).Details);

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
                    reading, Listed(), DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Contain("One admission record cannot be read"));
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
                var text = string.Join(" ", AuthorityDescription.Of(reading, unread, DateTimeOffset.UtcNow).Details);

                Assert.That(text, Does.Not.Contain("cannot be read"),
                    "a listing that was never made is not a count of damaged records");
                Assert.That(text, Does.Contain("could not be read"),
                    "and it says which it was");
            }
        }

        [Test]
        public void A_missing_reading_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => AuthorityDescription.Of(null, null, Now));
        }
    }

    /// <summary>
    /// The one line telling an operator whether anything can reach a database at all. It is the
    /// difference between "nothing has paired yet" and "nothing can".
    /// </summary>
    [TestFixture]
    public sealed class GateStatusTextTests
    {
        [Test]
        public void A_listening_gate_with_an_authority_says_where_it_is()
        {
            Assert.That(GateStatusText.For(new Gate(true, 51776), true), Is.EqualTo("Listening on 127.0.0.1:51776."));
        }

        [Test]
        public void A_listening_gate_without_an_authority_says_that_it_serves_nothing_here()
        {
            var text = GateStatusText.For(new Gate(true, 51776), false);

            Assert.That(text, Does.StartWith("Listening on 127.0.0.1:51776."));
            Assert.That(text, Does.Contain("until this database has an authority"));
        }

        [Test]
        public void A_gate_that_is_not_listening_says_so_and_says_where_to_look()
        {
            Assert.That(GateStatusText.For(new Gate(false, 0), true), Is.EqualTo(GateStatusText.NotListening));
        }

        [Test]
        public void No_gate_at_all_says_nothing_rather_than_guessing()
        {
            Assert.That(GateStatusText.For(null, true), Is.Empty);
        }

        [Test]
        public void The_port_is_written_the_same_way_in_every_language()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

                Assert.That(GateStatusText.For(new Gate(true, 51776), true), Does.Contain("51776"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        private sealed class Gate : IGateStatus
        {
            public Gate(bool listening, int port)
            {
                IsListening = listening;
                Port = port;
            }

            public bool IsListening { get; private set; }

            public int Port { get; private set; }
        }
    }
}
