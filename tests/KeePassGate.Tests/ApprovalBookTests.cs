using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The approvals held for a retry: what one is keyed by, how long it lasts, and what lets go of
    /// it.
    /// </summary>
    [TestFixture]
    public sealed class ApprovalBookTests
    {
        private DateTimeOffset _now;
        private ApprovalBook _book;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
            _book = new ApprovalBook(() => _now);
        }

        [Test]
        public void A_request_differing_in_anything_is_another_request()
        {
            var asked = ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops" }, "P30D");

            Assert.That(ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops" }, "P30D"), Is.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-2", "agent-fictitious", new[] { "ops" }, "P30D"), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-1", "agent-other", new[] { "ops" }, "P30D"), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops", "audit" }, "P30D"), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Pairing("inv-1", "agent-fictitious", new[] { "ops" }, null), Is.Not.EqualTo(asked));
            Assert.That(ApprovalBook.Renewal("inv-1", "P30D"), Is.Not.EqualTo(asked));

            // Each part carries its length, so moving a character from one part to the next is not
            // the same request either.
            Assert.That(
                ApprovalBook.Pairing("ab", "c", Array.Empty<string>(), null),
                Is.Not.EqualTo(ApprovalBook.Pairing("a", "bc", Array.Empty<string>(), null)));
        }

        [Test]
        public void An_approval_is_found_for_its_own_database_until_its_time_is_up()
        {
            var held = Hold("key");

            Assert.That(_book.Find("key", "db"), Is.SameAs(held));
            Assert.That(_book.Find("key", "another db"), Is.Null);

            _now = held.Until;
            Assert.That(_book.Find("key", "db"), Is.Null);
            Assert.That(held.Cleared, Is.False, "running out is not being let go");
            Assert.That(held.Taken, Is.False, "nor being taken");
        }

        [Test]
        public void An_approval_is_used_once()
        {
            var held = Hold("key");

            Assert.That(_book.Take(held), Is.True);
            Assert.That(_book.Take(held), Is.False);
            Assert.That(_book.Holds(held), Is.False);
            Assert.That(held.Taken, Is.True, "so a request that finds it gone can tell why");
            Assert.That(held.Cleared, Is.False);
        }

        [Test]
        public void A_lock_lets_go_of_every_approval_and_says_so_on_each()
        {
            var one = Hold("one");
            var two = Hold("two", "another db");

            ((IApprovalScreen)_book).Locked();

            Assert.That(_book.Count, Is.Zero);
            Assert.That(one.Cleared && two.Cleared, Is.True);
            Assert.That(_book.Take(one), Is.False);
        }

        [Test]
        public void A_close_lets_go_of_its_own_database_s_approvals_and_no_others()
        {
            // The operator's ruling R1. It let every approval go, so closing a database told a
            // pairing approved for another database that its database was locked.
            var closing = Hold("one");
            var open = Hold("two", "another db");

            ((IApprovalScreen)_book).Closed(new[] { "db" });

            Assert.That(closing.Cleared, Is.True);
            Assert.That(_book.Holds(closing), Is.False);
            Assert.That(open.Cleared, Is.False);
            Assert.That(_book.Find("two", "another db"), Is.SameAs(open));
        }

        [Test]
        public void A_close_that_names_no_database_lets_go_of_every_approval()
        {
            var one = Hold("one");
            var two = Hold("two", "another db");

            ((IApprovalScreen)_book).Closed(null);

            Assert.That(_book.Count, Is.Zero);
            Assert.That(one.Cleared && two.Cleared, Is.True);
        }

        [Test]
        public void Holding_the_same_request_again_replaces_what_was_held()
        {
            var first = Hold("key");
            var second = Hold("key");

            Assert.That(_book.Holds(first), Is.False);
            Assert.That(_book.Find("key", "db"), Is.SameAs(second));
            Assert.That(first.Taken, Is.True, "the same request approved again took its place");
        }

        [Test]
        public void An_answer_a_lock_overtook_is_let_go_as_it_arrives()
        {
            // S083: the operator answered, and KeePass locked before the answer was held. The
            // book the lock had emptied took it all the same.
            var mark = _book.Mark();
            ((IApprovalScreen)_book).Locked();

            var held = Hold("key", "db", mark);

            Assert.That(held.Cleared, Is.True, "answered as the lock it is");
            Assert.That(_book.Count, Is.Zero);
            Assert.That(_book.Find("key", "db"), Is.Null);
        }

        [Test]
        public void An_answer_given_after_its_database_closed_is_let_go_and_another_database_s_is_held()
        {
            // D005 and D017 for the database that closed, and the operator's ruling R1 for every
            // other: a close takes its own database's approvals and no others.
            var mark = _book.Mark();
            ((IApprovalScreen)_book).Closed(new[] { "db" });

            var closed = Hold("one", "db", mark);
            var open = Hold("two", "another db", mark);

            Assert.That(closed.Cleared, Is.True);
            Assert.That(_book.Find("one", "db"), Is.Null);
            Assert.That(open.Cleared, Is.False);
            Assert.That(_book.Find("two", "another db"), Is.SameAs(open));
        }

        [Test]
        public void A_question_asked_after_a_clear_is_held_as_any_other()
        {
            ((IApprovalScreen)_book).Locked();
            ((IApprovalScreen)_book).Closed(new[] { "db" });
            var mark = _book.Mark();

            var held = Hold("key", "db", mark);

            Assert.That(held.Cleared, Is.False);
            Assert.That(_book.Find("key", "db"), Is.SameAs(held));
        }

        [Test]
        public void An_answer_let_go_as_it_arrives_leaves_what_the_book_already_held()
        {
            // A later question for the same request, asked after the clear, was held under the same
            // key: the answer the clear overtook takes nothing from it.
            var before = _book.Mark();
            ((IApprovalScreen)_book).Locked();
            var later = Hold("key");

            Hold("key", "db", before);

            Assert.That(_book.Find("key", "db"), Is.SameAs(later));
        }

        [Test]
        public void A_renewal_is_written_only_while_the_admission_approved_against_is_in_force_and_reads()
        {
            // `STORE.md` §627: the admission a renewal renews is compared at the write by the
            // identifier of its entry, read from the store in the write's own turn. A revocation
            // and the certificate taken away are written here by the product's own acts, whose
            // write deletes that entry; a second admission of the same certificate leaves it where
            // it is and admits nobody. The test below keeps the entry beside each decision.
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var key = CertificateDigest.Of(certificate);
                var ways = new Dictionary<string, Action<MemoryStore, string>>
                {
                    { "in force", (store, served) => { } },
                    { "revoked", (store, served) => StoreSeeds.Revoke(store, key) },
                    { "taken away", (store, served) => StoreSeeds.TakeAway(store, key) },
                    {
                        "named by a second admission",
                        (store, served) => StoreSeeds.Client(
                            store, AdmissionRecord.For(certificate, _now, new[] { "ops" }, served).Members())
                    },
                };

                foreach (var way in ways)
                {
                    var store = new MemoryStore();
                    var served = StoreSeeds.Authority(store);
                    var admission = StoreSeeds.Admit(store, certificate, _now, "ops");
                    string entry;
                    using (var reading = store.Read())
                    {
                        entry = StoreAdmissions.Admitted(reading, admission.AdmissionKey).Entry;
                    }

                    var held = Renewal(served, entry);
                    way.Value(store, served);

                    using (var reading = store.Read())
                    {
                        Assert.That(
                            held.Stale(new Ground(reading), key),
                            Is.EqualTo(way.Key == "in force" ? HeldStaleness.None : HeldStaleness.Unpaired),
                            way.Key);
                    }
                }
            }
        }

        [TestCase("revoked")]
        [TestCase("withdrawn")]
        public void A_renewal_is_stale_while_a_decision_refuses_the_admission_whose_entry_is_still_there(string kind)
        {
            // The admission's entry left in the tree beside a revocation of its client, or beside a
            // statement taking its certificate away, as a copy that has not written since holds
            // what a merge brought: present under the identifier approved against, and admitting
            // nobody. Composed without a write, so that no plan deletes the entry.
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var key = CertificateDigest.Of(certificate);
                var store = new ComposedStore(StoreRecords.One);
                var members = AdmissionRecord.For(certificate, _now, new[] { "ops" }, StoreRecords.Hex('a')).Members();
                store.Add(StoreComposer.Record(
                    StoreRecords.One, "client", "client:" + key, StoreComposer.Content(members.Select(member => (member.Key, member.Value)).ToArray())));
                string entry;
                using (var reading = store.Read())
                {
                    entry = StoreAdmissions.Admitted(reading, key).Entry;
                }

                var held = Renewal(StoreRecords.Hex('a'), entry);
                var decision = kind == "revoked"
                    ? StoreComposer.Record(StoreRecords.One, "revoked", "client:" + key, StoreComposer.Content(("client", key)))
                    : StoreComposer.Record(
                        StoreRecords.One, "withdrawn", "certificate:" + key, StoreComposer.Content(("certificate", key), ("renewed", string.Empty)));
                store.Add(decision);
                store.Restate(kind == "revoked"
                    ? StoreComposer.Item(("client", key), ("kind", "revoked"))
                    : StoreComposer.Item(
                        ("certificate", key), ("id", KeePassGate.Hex.Of(decision.Identifier)), ("kind", "withdrawn"), ("renewed", string.Empty)));

                using (var reading = store.Read())
                {
                    Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                    Assume.That(
                        reading.InForce.Admissions.Any(record => record.Hex == entry && record.InForce), Is.True,
                        "the admission's entry is where it was");
                    Assert.That(held.Stale(new Ground(reading), key), Is.EqualTo(HeldStaleness.Unpaired));
                }
            }
        }

        [Test]
        public void A_renewal_cannot_be_held_without_the_entry_it_was_approved_against()
        {
            // Held without it, a renewal's approval would be compared as a pairing's, with no
            // admission asked about at the write.
            Assert.Throws<ArgumentException>(() => Renewal("db", null));
            Assert.Throws<ArgumentException>(() => Renewal("db", string.Empty));
            Assert.That(_book.Count, Is.Zero);
        }

        private HeldApproval Renewal(string databaseId, string entry)
        {
            return _book.HoldRenewal(
                "renewal", databaseId, new[] { "ops" }, TimeSpan.FromDays(30), new[] { "ops" }, entry,
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), _book.Mark(), null);
        }

        [Test]
        public void A_held_approval_is_stale_where_a_name_it_rested_on_moved_on_the_list_and_for_no_other_name()
        {
            // `PAIRING.md` §311: compared at the write, name by name, with the names on the list in
            // the reading of the write's turn.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "ops");
            var held = _book.Hold(
                "pairing", "db", new[] { "ops", "new" }, TimeSpan.FromDays(30), new[] { "ops" },
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), _book.Mark());

            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.None), "as the dialog showed the list");

            StoreSeeds.Name(store, "unrelated");
            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.None), "a name the decision did not rest on");

            StoreSeeds.Name(store, "new");
            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.Profiles), "a name shown as new is on the list by now");

            StoreSeeds.Unname(store, "new");
            StoreSeeds.Unname(store, "ops");
            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.Profiles), "a name shown as held is off the list by now");
        }

        private static HeldStaleness StaleIn(MemoryStore store, HeldApproval held)
        {
            using (var reading = store.Read())
            {
                return held.Stale(new Ground(reading), null);
            }
        }

        /// <summary>What a write is held to, read from a store held in memory in the write's turn.</summary>
        private sealed class Ground : IWriteGround
        {
            private readonly StoreReading _reading;

            public Ground(StoreReading reading)
            {
                _reading = reading;
            }

            public AdmissionRecord Admission(string admissionKey)
            {
                return StoreAdmissions.Admitted(_reading, admissionKey);
            }

            public bool Named(string profile)
            {
                return ProfileNames.Of(_reading).Holds(profile);
            }
        }

        private HeldApproval Hold(string key, string databaseId = "db")
        {
            return Hold(key, databaseId, _book.Mark());
        }

        private HeldApproval Hold(string key, string databaseId, long mark)
        {
            return _book.Hold(
                key, databaseId, new[] { "ops" }, TimeSpan.FromDays(30), new[] { "ops" },
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), mark);
        }
    }
}
