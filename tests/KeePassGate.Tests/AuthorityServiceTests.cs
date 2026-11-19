using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The three things an operator can do, and the two rules that run through all of them:
    /// nothing is destroyed implicitly, and nothing is believed without being read back.
    /// </summary>
    [TestFixture]
    public sealed class AuthorityServiceTests
    {
        private MemoryStore _records;
        private RecordingGateLog _log;
        private AuthorityService _service;

        [SetUp]
        public void SetUp()
        {
            _records = new MemoryStore();
            _log = new RecordingGateLog();
            _service = new AuthorityService(_records, _log);
        }

        [Test]
        public void Creating_puts_an_authority_in_the_database()
        {
            var result = _service.Create();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(reading.Material.AuthorityKey.PrivateKey.IsPrivate, Is.True);
                Assert.That(reading.Material.HostKey.PrivateKey.IsPrivate, Is.True);
                Assert.That(reading.Material.SniKey.Length, Is.EqualTo(AuthorityMaterial.SniKeyLength));
            }
        }

        [Test]
        public void A_change_that_was_made_carries_no_words_for_the_tab_to_repeat()
        {
            // What changed is on the tab in what it draws next, and the tab shows nothing for a
            // change that was made: a sentence composed here had nobody to read it.
            Assert.That(_service.Create().Message, Is.Empty);
            Assert.That(_service.Recreate().Message, Is.Empty);
            Assert.That(_service.Delete().Message, Is.Empty);
        }

        [Test]
        public void A_press_that_waits_for_the_dialog_s_OK_logs_nothing_that_reads_as_done()
        {
            // On the tab every command's act waits in the dialog's buffer, and a Cancel drops it:
            // a line that the authority was created, recreated or deleted would then stand in the
            // log over an authority that was never touched.
            StoreSeeds.Authority(_records);
            var buffer = new PendingRecords(() => _records);
            var pressed = new AuthorityService(buffer, _log);

            Assume.That(pressed.Recreate().Succeeded, Is.True);
            Assume.That(pressed.Delete().Succeeded, Is.True);
            Assume.That(pressed.Create().Succeeded, Is.True);
            buffer.Discard();

            Assert.That(
                _log.Entries.Select(entry => entry.Message).Where(line =>
                    line.StartsWith("authority created", StringComparison.Ordinal)
                    || line.StartsWith("authority recreated", StringComparison.Ordinal)
                    || line.StartsWith("authority deleted", StringComparison.Ordinal)),
                Is.Empty);

            Assert.That(_service.Recreate().Succeeded, Is.True);
            Assert.That(_log.Entries.Select(entry => entry.Message), Has.Some.StartsWith("authority recreated"),
                "a command written at once is logged as done");
        }

        [Test]
        public void Keeping_an_authority_through_the_dialog_s_buffer_logs_nothing_that_reads_as_done()
        {
            // The chooser on the tab keeps an authority through the dialog's buffer as well, and a
            // Cancel drops it: a line that the authority was kept would then stand over
            // authorities nobody kept.
            var candidates = StoreSeeds.Undecided(_records);
            var buffer = new PendingRecords(() => _records);

            Assume.That(new AuthorityService(buffer, _log).Keep(candidates[0], candidates).Succeeded, Is.True);
            buffer.Discard();

            Assert.That(_log.Entries.Select(entry => entry.Message), Has.None.Contains("kept, others retired"));

            Assert.That(_service.Keep(candidates[0], candidates).Succeeded, Is.True);
            Assert.That(_log.Entries.Select(entry => entry.Message), Has.Some.Contains("kept, others retired"),
                "a choice written at once is logged as done");
        }

        [Test]
        public void Every_gate_key_is_a_new_one()
        {
            _service.Create();
            byte[] first;
            using (var reading = _service.Read())
            {
                first = reading.Material.SniKey;
            }

            _service.Recreate();
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.SniKey, Is.Not.EqualTo(first));
            }
        }

        [Test]
        public void Creating_over_an_authority_is_refused_rather_than_replacing_it()
        {
            _service.Create();
            string digest;
            using (var reading = _service.Read())
            {
                digest = reading.Material.AuthorityDigest;
            }

            var second = _service.Create();

            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Message, Does.Contain("already has an authority"));
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(digest), "Nothing may be replaced silently.");
            }
        }

        [Test]
        public void Creating_over_damage_is_refused_and_says_what_to_do_instead()
        {
            StoreSeeds.Damaged(_records);

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("Recreate"));
        }

        [Test]
        public void Recreating_replaces_the_authority()
        {
            _service.Create();
            string first;
            using (var reading = _service.Read())
            {
                first = reading.Material.AuthorityDigest;
            }

            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.Not.EqualTo(first));
            }
        }

        [Test]
        public void Recreating_repairs_damage()
        {
            StoreSeeds.Damaged(_records);

            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
            }
        }

        [Test]
        public void Recreating_deletes_every_admission_of_the_authority_it_retires_in_the_same_write()
        {
            _service.Create();
            using (var issuer = TestAuthority.New())
            using (var first = issuer.IssueClient("agent-fictitious"))
            using (var second = issuer.IssueClient("agent-invented"))
            {
                StoreSeeds.Admit(_records, first, DateTimeOffset.UtcNow, "ops");
                StoreSeeds.Admit(_records, second, DateTimeOffset.UtcNow, "ops");
            }

            var writes = _records.Writes;
            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(_records.Writes, Is.EqualTo(writes + 1), "a single write");
            using (var reading = _service.Read())
            {
                Assert.That(reading.ClientRecordCount, Is.Zero);
            }
        }

        [Test]
        public void Deleting_leaves_the_database_with_nothing_of_ours_but_the_retirement()
        {
            _service.Create();
            var deleted = Digest();
            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious"))
            {
                StoreSeeds.Admit(_records, client, DateTimeOffset.UtcNow, "ops");
            }

            var result = _service.Delete();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.ClientRecordCount, Is.Zero, "the admissions go with the authority");
            }

            Assert.That(Retired(), Does.Contain(deleted),
                "the retirement is what keeps a stale copy of the file from bringing it back");
        }

        [Test]
        public void A_recreated_authority_stays_replaced_when_a_stale_copy_brings_the_old_one_back()
        {
            // What a merge with a copy taken before the recreation does to the file: it brings back
            // every entry that copy holds, the authority it served among them.
            _service.Create();
            var stale = _records.Copy();

            _service.Recreate();
            var current = Digest();
            _records.Synchronize(stale);

            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(current));
            }
        }

        [Test]
        public void Creating_over_several_authorities_is_refused_and_says_what_to_do_instead()
        {
            Undecided();

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("more than one authority"));
        }

        [Test]
        public void Recreating_over_several_retires_every_one_of_them()
        {
            var digests = Undecided();

            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            foreach (var digest in digests)
            {
                Assert.That(Retired(), Does.Contain(digest));
            }

            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(digests, Does.Not.Contain(reading.Material.AuthorityDigest));
            }
        }

        [Test]
        public void Keeping_one_of_several_serves_it_and_logs_how_many_went()
        {
            var digests = Undecided();

            var result = _service.Keep(digests[0], digests);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(_log.Entries.Any(e => e.Message.Contains("others retired: 1")), Is.True);
            Assert.That(Digest(), Is.EqualTo(digests[0]));
        }

        [Test]
        public void Keeping_the_one_authority_served_alone_retires_nothing_and_changes_nothing()
        {
            // Reached through the service alone, since a chooser asks only of a database serving
            // several and an answer about several is refused once the database serves one of them
            // alone. It keeps the database as it was and has nothing to put in words.
            _service.Create();
            var only = Digest();

            var result = _service.Keep(only, new[] { only });

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.Message, Is.Empty);
            Assert.That(_log.Entries.Any(e => e.Message.Contains("others retired: 0")), Is.True);
            Assert.That(Digest(), Is.EqualTo(only));
        }

        [Test]
        public void Keeping_something_that_is_not_a_choice_is_refused_rather_than_thrown()
        {
            var digests = Undecided();

            var result = _service.Keep(new string('d', 64), digests);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.IsOvertaken, Is.False, "the database holds what the question listed");
            Assert.That(result.Message, Does.Contain("could not be kept"));
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void An_answer_is_refused_once_the_database_holds_an_authority_the_question_did_not_list()
        {
            // A synchronize under the question brought it. Kept all the same, the answer retired
            // it, and every client it admitted, with nobody having been shown either.
            var digests = Undecided();

            var result = _service.Keep(digests[0], new[] { digests[0] });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.IsOvertaken, Is.True, "a question to put again, rather than an answer to try again");
            Assert.That(Retired(), Does.Not.Contain(digests[1]), "nothing is retired");
        }

        [Test]
        public void An_answer_is_refused_once_an_authority_the_question_listed_is_no_longer_held()
        {
            // Retired by another copy's answer, which a synchronize under the question brought:
            // what is left to choose from is not what the operator chose among.
            var digests = Undecided();

            var result = _service.Keep(digests[0], digests.Concat(new[] { new string('d', 64) }));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.IsOvertaken, Is.True);
            Assert.That(Retired(), Does.Not.Contain(digests[1]), "nothing is retired");
        }

        [Test]
        public void A_choice_that_does_not_land_is_not_reported_as_made()
        {
            var digests = Undecided();
            _records.SwallowWrites = true;

            var result = _service.Keep(digests[1], digests);

            Assert.That(result.Succeeded, Is.False, result.Message);
            Assert.That(result.Message, Does.Contain("Read the page again"));
        }

        /// <summary>
        /// Makes the database undecided, as copies that each recreated an authority leave it once
        /// they are merged: authorities side by side and none of them retired, two here. Answers
        /// with their digests.
        /// </summary>
        private string[] Undecided()
        {
            return StoreSeeds.Undecided(_records);
        }

        /// <summary>The `id` of every authority the store holds a retirement of.</summary>
        private IReadOnlyCollection<string> Retired()
        {
            return _records.Read().InForce.Taken(StoreKinds.Retired);
        }

        /// <summary>The digest of the one authority the store serves now.</summary>
        private string Digest()
        {
            using (var reading = _service.Read())
            {
                Assume.That(reading.IsPresent, "there has to be exactly one authority to name");
                return reading.Material.AuthorityDigest;
            }
        }

        [Test]
        public void Deleting_nothing_is_not_an_error()
        {
            var result = _service.Delete();

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(_log.Warnings, Is.Empty);
        }

        [Test]
        public void A_write_that_vanishes_is_reported_rather_than_believed()
        {
            // A store that loses a write: it is accepted and it is not there.
            _records.SwallowWrites = true;

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("could not be read back"));
            Assert.That(result.Message, Does.Contain("nothing was stored"));
            Assert.That(_log.Warnings, Is.Not.Empty);
        }

        [Test]
        public void A_write_that_lands_unusable_is_not_reported_as_nothing_having_happened()
        {
            // The other half of a failed read back. The authority is in the database and cannot
            // be read, so the next press of Create is answered with "recreate it": a refusal
            // stating nothing changed contradicts the very next thing the tab does.
            _records.DamageAuthorities = true;

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("now holds an authority that cannot be used"));
            Assert.That(result.Message, Does.Not.Contain("nothing"));

            _records.DamageAuthorities = false;
            var second = _service.Create();
            Assert.That(second.Message, Does.Contain("cannot be read"),
                "which is the answer the refusal above has to be consistent with");
        }

        [Test]
        public void A_recreation_that_did_not_land_is_not_reported_as_having_worked()
        {
            // A swallowed write leaves the authority that was already there, and reading back only
            // far enough to find one present is satisfied by it: the operator was told the
            // recreation had worked, and told how many clients it had revoked, while the authority
            // and all of those clients were untouched.
            _service.Create();
            string before;
            using (var reading = _service.Read())
            {
                before = reading.Material.AuthorityDigest;
            }

            _records.SwallowWrites = true;
            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.False, result.Message);

            _records.SwallowWrites = false;
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(before),
                    "the refusal has to be about the authority that is really there");
            }
        }

        [Test]
        public void A_deletion_that_does_not_land_does_not_claim_the_database_is_untouched()
        {
            // A delete the store swallowed leaves the authority standing, which is the case being
            // reported, and reporting it as a change that changed nothing sends the operator away
            // from a database that needs looking at.
            _service.Create();
            _records.SwallowWrites = true;

            var result = _service.Delete();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("still in the database"));
            Assert.That(result.Message, Does.Not.Contain("Nothing was changed"));
        }

        [Test]
        public void A_store_that_throws_is_reported_rather_than_thrown()
        {
            _records.ThrowOnWrite = new DatabaseShut("the database refused it");

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("the database refused it"));
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void Reading_a_store_that_throws_reports_damage_rather_than_absence()
        {
            var store = new MemoryStore { ThrowOnRead = new DatabaseShut("the database refused it") };
            var service = new AuthorityService(store, _log);

            using (var reading = service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Is.Not.Empty);
            }
        }

        [Test]
        public void The_log_never_carries_the_material_itself()
        {
            _service.Create();
            _service.Recreate();
            _service.Delete();

            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }

            // Asked of the whole line the file is given, any exception logged included.
            foreach (var entry in _log.Entries)
            {
                Assert.That(entry.Line, Does.Not.Contain("MII"), "A certificate blob has no business in a log.");
                Assert.That(entry.Message.Length, Is.LessThan(200));
            }
        }

        [Test]
        public void A_missing_store_is_refused_at_construction()
        {
            Assert.Throws<ArgumentNullException>(() => new AuthorityService(null, _log));
        }
    }
}
