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
                            held.Stale(new Ground(reading), key, out _),
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
                    string why;
                    Assert.That(held.Stale(new Ground(reading), key, out why), Is.EqualTo(HeldStaleness.Unpaired));
                    Assert.That(why, Is.EqualTo("the record it renews went, or changed, after the operator answered"));
                }
            }
        }

        [Test]
        public void A_held_approval_is_stale_for_every_cause_its_write_finds_in_a_single_pass()
        {
            // `reader` shown on the list and taken off it, and a removal of `ops`, shown off the
            // list, that only a deletion record tells of: the write names both, and keeps the
            // removals of both names, neither of which the dialog held.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "reader");
            var held = _book.Hold(
                "pairing", "db", new[] { "ops", "reader" }, TimeSpan.FromDays(30), Shown(store, "ops", "reader"),
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), _book.Mark());
            StoreSeeds.Unname(store, "reader");
            var removal = StoreRecords.RemovedApart(store, "ops");

            using (var reading = store.Read())
            {
                string why;
                IReadOnlyDictionary<string, IReadOnlyCollection<string>> unseen;

                Assert.That(held.Stale(new Ground(reading), null, out why, out unseen), Is.EqualTo(HeldStaleness.Profiles));
                Assert.That(unseen.Keys, Is.EquivalentTo(new[] { "ops", "reader" }));
                Assert.That(unseen["ops"], Is.EqualTo(new[] { removal }));
                Assert.That(unseen["reader"], Is.EqualTo(reading.InForce.RemovalsOf("reader")));
                Assert.That(why, Is.EqualTo(
                    "the profile reader is off the list, and the dialog showed it on the list; "
                    + "the store holds a removal of the profile ops that the reading taken before the dialog did not hold"));
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
                "pairing", "db", new[] { "ops", "new" }, TimeSpan.FromDays(30), Shown(store, "ops", "new"),
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), _book.Mark());

            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.None), "as the dialog showed the list");

            StoreSeeds.Name(store, "unrelated");
            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.None), "a name the decision did not rest on");

            StoreSeeds.Name(store, "new");
            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.Profiles), "a name shown as new is on the list by now");
            Assert.That(WhyStale(store, held), Is.EqualTo("the profile new is on the list, and the dialog showed it off the list"));

            // Taking `new` off the list again leaves a removal of it that the dialog did not hold,
            // which the same pass names beside `ops` leaving the list.
            StoreSeeds.Unname(store, "new");
            StoreSeeds.Unname(store, "ops");
            Assert.That(StaleIn(store, held), Is.EqualTo(HeldStaleness.Profiles), "a name shown as held is off the list by now");
            Assert.That(WhyStale(store, held), Is.EqualTo(
                "the profile ops is off the list, and the dialog showed it on the list; "
                + "the store holds a removal of the profile new that the reading taken before the dialog did not hold"));
        }

        [Test]
        public void A_held_approval_of_a_name_off_the_list_is_stale_where_a_removal_of_it_beyond_those_the_dialog_held_stands()
        {
            // `PAIRING.md` §311: an approval puts a name off the list back over the removals of it
            // that the dialog's reading held, and a removal beyond them is a removal the operator
            // was not shown. A name the dialog showed on the list is held to its place there alone.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Name(store, "gone");
            StoreSeeds.Unname(store, "gone");
            var until = PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6));
            var back = _book.Hold("back", "db", new[] { "ops", "gone" }, TimeSpan.FromDays(30), Shown(store, "ops", "gone"), until, _book.Mark());
            var fresh = _book.Hold("fresh", "db", new[] { "new" }, TimeSpan.FromDays(30), Shown(store, "new"), until, _book.Mark());

            Assert.That(StaleIn(store, back), Is.EqualTo(HeldStaleness.None), "over the removal the dialog held");
            Assert.That(StaleIn(store, fresh), Is.EqualTo(HeldStaleness.None));

            StoreRecords.RemovedApart(store, "unrelated");
            Assert.That(StaleIn(store, back), Is.EqualTo(HeldStaleness.None), "a removal of a name the decision did not rest on");
            Assert.That(StaleIn(store, fresh), Is.EqualTo(HeldStaleness.None));

            StoreRecords.RemovedApart(store, "new");
            Assert.That(StaleIn(store, fresh), Is.EqualTo(HeldStaleness.Profiles), "a name shown as new was removed since");
            Assert.That(WhyStale(store, fresh),
                Is.EqualTo("the store holds a removal of the profile new that the reading taken before the dialog did not hold"));
            Assert.That(StaleIn(store, back), Is.EqualTo(HeldStaleness.None));

            StoreRecords.RemovedApart(store, "gone");
            Assert.That(StaleIn(store, back), Is.EqualTo(HeldStaleness.Profiles), "a name shown as removed was removed again");
            Assert.That(WhyStale(store, back),
                Is.EqualTo("the store holds a removal of the profile gone that the reading taken before the dialog did not hold"));
        }

        [Test]
        public void A_removal_only_a_deletion_record_tells_of_is_unseen_until_a_dialog_holds_what_the_write_found()
        {
            // The dialog's reading takes no key and cannot tie that deletion record to a name
            // nothing else in the file names, so it holds no removal of `ops`, and the write, which
            // counts with the key, finds that removal: stale. The removals the write found, carried into
            // the next dialog's reading, are held there, and the approval of that dialog is not
            // stale over them again.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            StoreRecords.RemovedApart(store, "ops");
            var until = PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6));
            var first = _book.Hold("pairing", "db", new[] { "ops" }, TimeSpan.FromDays(30), Shown(store, "ops"), until, _book.Mark());

            IReadOnlyCollection<string> found;
            using (var reading = store.Read())
            {
                Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty, "a name the file does not know");
                found = reading.InForce.RemovalsOnceKnown("ops");
                Assert.That(found, Has.Count.EqualTo(1), "and its removal, once it is");
            }

            Assert.That(StaleIn(store, first), Is.EqualTo(HeldStaleness.Profiles));
            Assert.That(WhyStale(store, first),
                Is.EqualTo("the store holds a removal of the profile ops that the reading taken before the dialog did not hold"));

            var shown = Shown(store, "ops").With(new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { { "ops", found } });
            var second = _book.Hold("pairing", "db", new[] { "ops" }, TimeSpan.FromDays(30), shown, until, _book.Mark());

            Assert.That(StaleIn(store, second), Is.EqualTo(HeldStaleness.None));
        }

        /// <summary>What the dialog is built from, read from the store as it stands.</summary>
        private AskedProfiles Shown(MemoryStore store, params string[] profiles)
        {
            using (var reading = store.Read())
            {
                return AskedProfiles.Of(reading, profiles, _now);
            }
        }

        private static HeldStaleness StaleIn(MemoryStore store, HeldApproval held)
        {
            using (var reading = store.Read())
            {
                return held.Stale(new Ground(reading), null, out _);
            }
        }

        private static string WhyStale(MemoryStore store, HeldApproval held)
        {
            using (var reading = store.Read())
            {
                string why;
                held.Stale(new Ground(reading), null, out why);
                return why;
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

            public IReadOnlyCollection<string> Removals(string profile)
            {
                return _reading.InForce == null ? (IReadOnlyCollection<string>)Array.Empty<string>() : _reading.InForce.RemovalsOnceKnown(profile);
            }
        }

        private HeldApproval Hold(string key, string databaseId = "db")
        {
            return Hold(key, databaseId, _book.Mark());
        }

        private HeldApproval Hold(string key, string databaseId, long mark)
        {
            return _book.Hold(
                key, databaseId, new[] { "ops" }, TimeSpan.FromDays(30), AskedProfiles.None,
                PairingClock.For(() => _now).Read().After(TimeSpan.FromMinutes(6)), mark);
        }
    }
}
