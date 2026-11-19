using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePassLib;
using KeePassLib.Serialization;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Locking and unlocking a real KeePass database, as the gate sees it.
    /// </summary>
    /// <remarks>
    /// KeePass locks a workspace by closing the database and remembering the file it came from, so
    /// what the plugin actually meets is a `PwDatabase` that has been emptied. That is arranged here
    /// exactly the way KeePass arranges it, and the assertions are the rows of the table in
    /// `PAIRING.md` §308: the responder and the gate key survive, the authority key and the
    /// records do not.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LockInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private RecordingLog _log;
        private OpenDatabaseTargets _targets;
        private List<PwDatabase> _open;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _log = new RecordingLog();

            var created = DatabaseAuthority.Of(_database.Database, _log).Create();
            Assert.That(created.Succeeded, Is.True, created.Message);
            _database.Save();

            _open = new List<PwDatabase> { _database.Database };
            _targets = new OpenDatabaseTargets(delegate { return _open; }, _log);

            // What the plugin does when KeePass says a database has been opened, and what mints the
            // responder that a later lock will answer with.
            Assert.That(_targets.Unlock(_database.Path), Is.Empty, "a first open lifts no lock");
        }

        [TearDown]
        public void TearDown()
        {
            _targets.Dispose();
            _database.Dispose();
        }

        [Test]
        public void An_open_database_is_served_in_full()
        {
            var target = _targets.Current().Single();

            Assert.That(target.IsLocked, Is.False);
            Assert.That(target.Authority, Is.Not.Null);
        }

        [Test]
        public void A_served_database_whose_key_carrier_goes_answers_lost_with_what_it_served_and_nothing_once_locked_and_unlocked()
        {
            // `STORE.md` §656: the authority's entry, the store key's only carrier, deleted for
            // good under a database being served, as the edits in KeePass's window or a
            // synchronize leave it. The file then holds no store key, and the gate answers `lost`
            // from the host certificate and the gate key it served with, until the database is
            // locked; the file unlocked again holds no key either, and nothing answers for it.
            var served = _targets.Current().Single();
            var databaseId = served.DatabaseId;
            var gateKey = served.GateKey;
            var host = served.Host.CertificateDer;

            DeleteForGood(Entry(StoreKinds.Authority));

            var lost = _targets.Current().Single();
            Assert.That(lost.Unavailable, Is.EqualTo("lost"));
            Assert.That(lost.DatabaseId, Is.EqualTo(databaseId));
            Assert.That(SniName.Matches(SniName.For(lost.GateKey), gateKey), Is.True, "under the gate key it served with");
            Assert.That(lost.Host.CertificateDer, Is.EqualTo(host), "and its host certificate");
            Assert.That(lost.Authority, Is.Null, "judging no certificate");
            Assert.That(lost.Vault, Is.Null, "and reaching nothing of the database");

            _database.Save();
            Lock();

            Assert.That(_targets.Current(), Is.Empty, "nothing answers once the database is locked");

            Reopen();
            _targets.Unlock(_database.Path);

            Assert.That(_targets.Current(), Is.Empty, "nor once it is unlocked");
        }

        [Test]
        public void A_served_database_whose_key_carrier_goes_answers_nothing_once_closed_and_opened_again()
        {
            Assume.That(_targets.Current(), Has.Count.EqualTo(1));
            DeleteForGood(Entry(StoreKinds.Authority));
            Assume.That(_targets.Current().Single().Unavailable, Is.EqualTo("lost"));
            _database.Save();

            var path = _database.Path;
            _database.Database.Close();
            _open.Clear();
            _targets.Forget(path);

            Reopen();
            _targets.Unlock(path);

            Assert.That(_targets.Current(), Is.Empty);
        }

        [Test]
        public void A_served_database_whose_authority_a_synchronize_retires_into_a_stopped_store_answers_nothing()
        {
            // A retired authority's clients are told nothing, `STORE.md` §656, and a store held
            // back for another reason changes none of that: a retirement of the authority served,
            // holding the store key as another copy's Delete writes it, arrives under the database
            // being served, and its summary entry is then deleted for good, which stops the store.
            var served = _targets.Current().Single().DatabaseId;
            var store = Store();
            StoreSeeds.Retire(store, served);
            DeleteForGood(Entry(StoreKinds.Head));
            Assume.That(State(store), Is.EqualTo(StoreState.Stopped));

            Assert.That(_targets.Current(), Is.Empty);
        }

        [Test]
        public void A_served_database_whose_authority_does_not_read_in_a_stopped_store_answers_stopped_from_what_it_served()
        {
            // The authority served stands in a record that passes and will not read, as an edit by
            // another program leaves it, and the store is stopped: retired by nothing, its clients
            // are answered the store's word under the gate key and the host certificate they knew.
            var served = _targets.Current().Single();
            var databaseId = served.DatabaseId;
            var gateKey = served.GateKey;
            var store = Store();
            var original = Entry(StoreKinds.Authority);
            StoreSeeds.Damaged(store, databaseId);
            DeleteForGood(original);
            DeleteForGood(Entry(StoreKinds.Head));
            Assume.That(State(store), Is.EqualTo(StoreState.Stopped));

            var stopped = _targets.Current().Single();
            Assert.That(stopped.Unavailable, Is.EqualTo("stopped"));
            Assert.That(stopped.DatabaseId, Is.EqualTo(databaseId));
            Assert.That(SniName.Matches(SniName.For(stopped.GateKey), gateKey), Is.True, "under the gate key it served with");
            Assert.That(stopped.Authority, Is.Null, "judging no certificate");
            Assert.That(stopped.Vault, Is.Null, "and reaching nothing of the database");
        }

        [Test]
        public void What_a_database_was_served_with_is_carried_through_a_choice_and_answers_lost_once_the_key_goes()
        {
            // An authority from another copy arrives beside the authority served, and each is
            // answered `undecided`; then every entry holding the store key goes, and what the gate
            // served with before the choice answers `lost`.
            var served = _targets.Current().Single();
            var databaseId = served.DatabaseId;
            var gateKey = served.GateKey;
            var store = Store();
            StoreSeeds.Beside(store);
            Assume.That(_targets.Current().Select(target => target.Unavailable), Is.All.EqualTo(HeldBackTarget.Undecided));

            foreach (var authority in Entries(StoreKinds.Authority))
            {
                DeleteForGood(authority);
            }

            Assume.That(State(store), Is.EqualTo(StoreState.None));

            var lost = _targets.Current().Single();
            Assert.That(lost.Unavailable, Is.EqualTo("lost"));
            Assert.That(lost.DatabaseId, Is.EqualTo(databaseId));
            Assert.That(SniName.Matches(SniName.For(lost.GateKey), gateKey), Is.True, "under the gate key it served with");
        }

        [Test]
        public void A_database_whose_authority_was_deleted_answers_nothing()
        {
            // A retired authority's clients are told nothing, `STORE.md` §656: what the gate
            // served with is let go with the authority, and so nothing answers once the
            // retirements the Delete wrote, which hold the store key, are deleted for good too and
            // the file holds no key.
            Assume.That(_targets.Current(), Has.Count.EqualTo(1));

            Assert.That(DatabaseAuthority.Of(_database.Database, NullGateLog.Instance).Delete().Succeeded, Is.True);

            Assert.That(_targets.Current(), Is.Empty);

            foreach (var retirement in Entries(StoreKinds.Retired))
            {
                DeleteForGood(retirement);
            }

            Assume.That(State(Store()), Is.EqualTo(StoreState.None));
            Assert.That(_targets.Current(), Is.Empty);
        }

        [Test]
        public void A_locked_database_is_still_served_and_still_answers_to_its_name()
        {
            var before = _targets.Current().Single();
            var databaseId = before.DatabaseId;
            var gateKey = before.GateKey;

            Lock();

            var after = _targets.Current().Single();

            Assert.That(after.IsLocked, Is.True);
            Assert.That(after.DatabaseId, Is.EqualTo(databaseId), "it is the same database, just locked");
            Assert.That(SniName.Matches(SniName.For(after.GateKey), gateKey), Is.True,
                "the gate resolves names before any TLS work, so the gate key has to survive a lock");
        }

        [Test]
        public void A_locked_database_given_back_by_one_connection_still_answers_the_next()
        {
            // Every connection gives back what it was handed when it ends. Handed out bare, the
            // first to do that zeroed the gate key, and every later client was dropped at the name.
            var gateKey = _targets.Current().Single().GateKey;
            Lock();

            for (var connection = 0; connection < 2; connection++)
            {
                var handed = _targets.Current();

                Assert.That(SniName.Matches(SniName.For(gateKey), handed.Single().GateKey), Is.True,
                    "connection " + connection + " was handed a locked database that answers no name");

                foreach (var target in handed)
                {
                    target.Dispose();
                }
            }
        }

        [Test]
        public void It_has_a_responder_to_answer_with()
        {
            _targets.Current();

            Lock();

            var after = _targets.Current().Single();

            Assert.That(after.Host, Is.Not.Null);
        }

        [Test]
        public void The_authority_and_the_records_are_gone()
        {
            using (var certificate = IssueAndAdmit())
            {
                var key = CertificateDigest.Of(certificate);
                Assume.That(_targets.Current().Single().Admission(key), Is.Not.Null);

                Lock();

                var after = _targets.Current().Single();

                Assert.That(after.Authority, Is.Null, "nothing is left to judge a certificate with");
                Assert.That(after.AuthorityKey, Is.Null, "the key that signs is what a lock exists to drop");
                Assert.That(after.Admission(key), Is.Null);
                Assert.That(after.AdmissionBlocked, Is.Not.Null);
            }
        }

        [Test]
        public void A_client_that_was_admitted_a_moment_ago_is_told_the_database_is_locked()
        {
            using (var certificate = IssueAndAdmit())
            {
                Assume.That(ClientCredentials.For(certificate, _targets.Current().Single(), _log).IsPaired, Is.True);

                Lock();

                var identity = ClientCredentials.For(certificate, _targets.Current().Single(), _log);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(identity.IsLocked, Is.True);
            }
        }

        [Test]
        public void A_connection_made_before_a_second_authority_stood_is_told_undecided_as_a_new_connection_is()
        {
            // The target was built while a single authority stood, and a connection holds it for
            // as long as it lasts: a synchronize then brings an authority a copy made, and the
            // connection is judged, and judged again in its turn, as a connection made now is.
            using (var certificate = IssueAndAdmit())
            {
                var target = _targets.Current().Single();
                var paired = ClientCredentials.For(certificate, target, _log);
                Assume.That(paired.IsPaired, Is.True);

                StoreSeeds.Beside(Store());

                Assert.Multiple(() =>
                {
                    Assert.That(ClientCredentials.For(certificate, target, _log).UnavailableError, Is.EqualTo(HeldBackTarget.Undecided));
                    Assert.That(ClientCredentials.Again(paired, target, _log).UnavailableError, Is.EqualTo(HeldBackTarget.Undecided));
                    Assert.That(
                        _targets.Current().Select(current => current.Unavailable), Is.All.EqualTo(HeldBackTarget.Undecided),
                        "as a connection made now is told");
                });
            }
        }

        [Test]
        public void A_candidate_whose_own_authority_went_while_several_others_stand_is_answered_busy()
        {
            // A connection that took the candidate of its own authority, while two authorities
            // stood, and a synchronize then retires that authority and brings a third: no
            // decision can serve its clients, so it is answered as the target kept from what was
            // served is once its authority went, and not told to wait for a decision. Beside it
            // the candidate of an authority still among those standing.
            var first = StoreSeeds.Served(Store());
            StoreSeeds.Beside(Store());
            var candidates = _targets.Current();
            var own = candidates.Single(candidate => candidate.DatabaseId == first);
            var other = candidates.Single(candidate => candidate.DatabaseId != first);
            Assume.That(own.Unavailable, Is.EqualTo(HeldBackTarget.Undecided));

            StoreSeeds.Beside(Store());
            StoreSeeds.Retire(Store(), first);

            Assert.That(ClientCredentials.For(null, own, _log).UnavailableError, Is.EqualTo(HeldBackTarget.Busy));
            Assert.That(ClientCredentials.For(null, other, _log).UnavailableError, Is.EqualTo(HeldBackTarget.Undecided));
        }

        [Test]
        public void A_connection_whose_authority_went_while_several_others_came_to_stand_is_answered_as_its_authority_went()
        {
            // Beside the case above: the authority the connection arrived under is retired and two
            // others stand, which no decision of the operator's can make serve this certificate.
            // The connection is answered as a connection whose authority went is, a certificate
            // with no admission, and not told to wait.
            using (var certificate = IssueAndAdmit())
            {
                var target = _targets.Current().Single();
                var paired = ClientCredentials.For(certificate, target, _log);
                Assume.That(paired.IsPaired, Is.True);

                new StoredAdmissions(_database.Database).RetiredBesideOthers();

                var judged = ClientCredentials.For(certificate, target, _log);
                var again = ClientCredentials.Again(paired, target, _log);
                Assert.Multiple(() =>
                {
                    Assert.That(judged.UnavailableError, Is.Null);
                    Assert.That(judged.IsPaired, Is.False);
                    Assert.That(judged.Refusal, Is.Not.Null);
                    Assert.That(again.UnavailableError, Is.Null);
                    Assert.That(again.IsPaired, Is.False);
                });
            }
        }

        [Test]
        public void What_a_database_was_served_with_answers_busy_once_its_authority_went_while_several_others_stand()
        {
            // Beside the case below: the authority the target was kept from is retired and two
            // others stand, and the target answers as it answers once its authority went.
            var authority = Entry(StoreKinds.Authority);
            var group = authority.ParentGroup;
            DeleteForGood(authority);
            var kept = _targets.Current().Single();
            Assume.That(kept.Unavailable, Is.EqualTo(StoreWords.Lost));
            Assume.That(kept.Authority, Is.Null, "the target kept from what was served");

            group.AddEntry(authority, true);
            _database.Database.DeletedObjects.Remove(
                _database.Database.DeletedObjects.Single(deleted => deleted.Uuid.Equals(authority.Uuid)));
            Assume.That(State(Store()), Is.EqualTo(StoreState.Whole));
            new StoredAdmissions(_database.Database).RetiredBesideOthers();

            Assert.That(kept.Unavailable, Is.EqualTo(HeldBackTarget.Busy));
        }

        [Test]
        public void What_a_database_was_served_with_answers_undecided_once_it_holds_several_authorities()
        {
            // The target kept from what the gate served answers `lost` while the file holds no
            // store key; once the store is whole again with a second authority beside the first, a
            // connection that took it is answered as a connection made now is.
            var authority = Entry(StoreKinds.Authority);
            var group = authority.ParentGroup;
            DeleteForGood(authority);
            var kept = _targets.Current().Single();
            Assume.That(kept.Unavailable, Is.EqualTo(StoreWords.Lost));
            Assume.That(kept.Authority, Is.Null, "the target kept from what was served");

            // The authority's entry back, as a synchronize from a copy that kept it brings it, and
            // an authority made in another copy beside it.
            group.AddEntry(authority, true);
            _database.Database.DeletedObjects.Remove(
                _database.Database.DeletedObjects.Single(deleted => deleted.Uuid.Equals(authority.Uuid)));
            Assume.That(State(Store()), Is.EqualTo(StoreState.Whole));
            StoreSeeds.Beside(Store());

            Assert.That(kept.Unavailable, Is.EqualTo(HeldBackTarget.Undecided));
        }

        [Test]
        public void A_lock_between_reading_the_word_and_the_admission_is_answered_as_a_lock()
        {
            // Each read holds KeePass's thread on its own, so a lock can land between the two. An
            // admission looked up in a database already shut is not missing, and a client told it
            // was would be told to pair again. The profiles are read with the admission, so no
            // lock lands between those.
            using (var certificate = IssueAndAdmit())
            {
                var hold = new LockAfterFirstRead(() => _database.Database.Close());
                using (var targets = new OpenDatabaseTargets(delegate { return _open; }, null, hold, _log))
                using (var target = targets.Current().Single())
                {
                    hold.Armed = true;

                    var identity = ClientCredentials.For(certificate, target, _log);

                    Assert.That(identity.IsLocked, Is.True);
                    Assert.That(identity.IsPaired, Is.False);
                }
            }
        }

        [Test]
        public void A_database_opened_again_is_served_as_itself_before_the_unlock_is_announced()
        {
            // KeePass opens the file into its document and asks its own questions before it raises
            // `FileOpened`, which is what lifts the lock here. In between, the responder and the
            // database answered to one name, and every connection was dropped as ambiguous.
            _targets.Current();
            Lock();

            _database.Reopen();
            _open.Clear();
            _open.Add(_database.Database);

            var targets = _targets.Current();
            Assert.That(targets, Has.Count.EqualTo(1));

            using (var target = targets[0])
            {
                Assert.That(target.IsLocked, Is.False, "the database it is, rather than the responder it was");
            }

            Assert.That(_targets.Unlock(_database.Path), Is.Not.Empty, "and the unlock still lifts the lock");
        }

        [Test]
        public void Unlocking_serves_it_in_full_again()
        {
            using (var certificate = IssueAndAdmit())
            {
                Lock();
                Assume.That(_targets.Current().Single().IsLocked, Is.True);

                Unlock();

                var after = _targets.Current().Single();

                Assert.That(after.IsLocked, Is.False);
                Assert.That(after.Authority, Is.Not.Null);
                Assert.That(ClientCredentials.For(certificate, after, _log).IsPaired, Is.True,
                    "the client that was admitted before the lock is admitted again after it");
            }
        }

        [Test]
        public void A_responder_is_minted_at_every_unlock_and_again_at_the_lock()
        {
            // Counted at the mint, because the responders a lock answers with differ whether or
            // not the unlock mints one: the lock mints its own either way, and only a mint at the
            // lock that fails ever shows the unlock's, which the test after this one makes happen.
            var minted = 0;
            _targets.MintResponder = authority =>
            {
                minted++;
                return new AuthorityIssuer().IssueResponder(authority);
            };

            Lock();
            Assert.That(minted, Is.EqualTo(1), "the lock mints the responder it answers with");

            Unlock();
            Assert.That(minted, Is.EqualTo(2), "the unlock mints the one a failed mint at the next lock falls back on");

            _targets.Current();
            Assert.That(minted, Is.EqualTo(2), "and serving the database again mints nothing");

            Lock();
            Assert.That(minted, Is.EqualTo(3));
        }

        [Test]
        public void A_lock_whose_own_mint_fails_answers_with_the_responder_minted_at_the_unlock()
        {
            // What the mint at the first serve is for: `Responder` fails for an authority whose
            // dates have passed, and the lock then answers with the one it holds while that one is
            // current, rather than with nothing at all.
            byte[] held = null;
            _targets.MintResponder = authority =>
            {
                var responder = new AuthorityIssuer().IssueResponder(authority);
                held = responder.CertificateDer;
                return responder;
            };
            Lock();
            Unlock();
            Assume.That(held, Is.Not.Null);

            _targets.MintResponder = authority => throw new InvalidOperationException("an invented failure to mint");
            Lock();

            var locked = _targets.Current().Single();
            Assert.That(locked.IsLocked, Is.True);
            Assert.That(locked.Host, Is.Not.Null, "the lock answered with no responder at all");
            Assert.That(locked.Host.CertificateDer, Is.EqualTo(held), "the lock answered with a responder other than the one held");
        }

        [Test]
        public void The_responder_that_answers_a_lock_is_minted_at_the_lock()
        {
            // Its life has to cover the lock. Minted only when the database was opened, whatever
            // the session lasted is spent before the lock even starts, and a certificate that
            // expires mid-lock is one the client refuses: the operator then gets the bare
            // handshake failure the responder exists to replace.
            //
            // Measured against the moment this database was first served, because what was minted
            // then is held inside the cache and is not reachable from here. Certificate times are
            // whole seconds, so the wait has to be more than one of them.
            _targets.Current();
            var served = DateTime.UtcNow;
            Thread.Sleep(TimeSpan.FromSeconds(2));

            Lock();

            using (var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                _targets.Current().Single().Host.CertificateDer))
            {
                Assert.That(certificate.NotBefore.ToUniversalTime() + AuthorityIssuer.Backdate,
                    Is.GreaterThan(served),
                    "otherwise this is the one minted when the database was opened");
            }
        }

        [Test]
        public void What_it_answers_with_while_locked_is_marked_as_a_responder()
        {
            _targets.Current();

            Lock();

            using (var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                _targets.Current().Single().Host.CertificateDer))
            {
                Assert.That(CertificateUris.Of(certificate), Is.EqualTo(new[] { AuthorityIssuer.ResponderClaim }));
            }
        }

        [Test]
        public void Closing_a_database_for_real_leaves_nothing_to_serve()
        {
            var served = _targets.Current().Single().DatabaseId;

            _open.Clear();

            // Its authority, so that the connections still reading it are closed, as they are at a
            // lock, and no other database's. It answered only for a lock, and a served database
            // closed without a lock closed nothing.
            Assert.That(_targets.Forget(_database.Path), Is.EqualTo(new[] { served }), "there was something being served");
            Assert.That(_targets.Current(), Is.Empty, "a closed database answers nothing, unlike a locked one");
        }

        [Test]
        public void An_open_that_a_connection_read_first_lifts_no_lock()
        {
            // KeePass can announce an open after a connection has read the database already, and
            // an unlock that lifted no lock must close nothing: that connection never saw a
            // responder to be promoted from.
            using (var later = TemporaryDatabase.New())
            {
                var created = DatabaseAuthority.Of(later.Database, _log).Create();
                Assert.That(created.Succeeded, Is.True, created.Message);
                _open.Add(later.Database);

                Assume.That(_targets.Current().Count, Is.EqualTo(2), "a connection read it before the event");

                Assert.That(_targets.Unlock(later.Path), Is.Empty);
            }
        }

        [Test]
        public void A_database_is_served_to_the_approval_dialogs_until_it_locks()
        {
            // Asked on KeePass's thread just before a pairing or renewal dialog goes up, so a
            // request that waited for the screen while KeePass locked is never shown.
            var databaseId = _targets.Current().Single().DatabaseId;

            Assert.That(_targets.Serving(databaseId), Is.True);
            Assert.That(_targets.Serving("0000000000000000"), Is.False, "an authority nobody serves");

            _database.Database.Close();
            _open.Clear();
            Assume.That(_targets.Lock(_database.Path), Is.True);

            Assert.That(_targets.Serving(databaseId), Is.False);
        }

        [Test]
        public void A_connection_that_lands_between_the_close_and_the_lock_leaves_it_answering()
        {
            // KeePass clears the database first and announces the lock afterwards, so a connection
            // arriving in between asks for the targets and finds the database shut. What it must
            // not do is take away what the lock is about to answer with.
            var before = _targets.Current().Single();
            var databaseId = before.DatabaseId;
            var gateKey = before.GateKey;
            var path = _database.Path;

            _database.Database.Close();
            _open.Clear();
            Assume.That(_targets.Current(), Is.Empty,
                "nothing is served for a database that has gone until the event says which way it went");

            Assert.That(_targets.Lock(path), Is.True,
                "a lock that follows such a connection is still a lock, and the connections still have to be closed");

            var after = _targets.Current().Single();

            Assert.That(after.IsLocked, Is.True);
            Assert.That(after.DatabaseId, Is.EqualTo(databaseId), "it is the same database, just locked");
            Assert.That(after.Host, Is.Not.Null,
                "without the responder a locked database cannot complete a handshake at all");
            Assert.That(SniName.Matches(SniName.For(after.GateKey), gateKey), Is.True,
                "the gate resolves names before any TLS work, so the gate key has to survive that connection too");
        }

        [Test]
        public void A_connection_that_lands_between_the_close_and_a_real_close_leaves_nothing()
        {
            // The other half of the same window. What is kept there is kept in case the event says
            // lock, and a close is the event saying it does not.
            _targets.Current();
            var path = _database.Path;

            _database.Database.Close();
            _open.Clear();
            _targets.Current();

            Assert.That(_targets.Forget(path), Is.Not.Empty,
                "it was served until that connection found it shut, and what was reading it then may still be");
            Assert.That(_targets.Current(), Is.Empty, "a closed database answers nothing, unlike a locked one");
        }

        [Test]
        public void A_database_that_was_never_served_can_still_be_locked_without_complaint()
        {
            // Locked before anything reached it, or with no authority at all. There is nothing to
            // answer with and nothing to drop.
            Assert.DoesNotThrow(() => _targets.Lock("C:\\invented\\never-served.kdbx"));
            Assert.That(_targets.Forget("C:\\invented\\never-served.kdbx"), Is.Empty,
                "and closing one holds nothing either, so no connection is closed for it");
            Assert.That(_targets.Current().Count, Is.EqualTo(1));
        }

        [Test]
        public void Two_spellings_KeePass_keeps_apart_are_kept_apart_here_too()
        {
            // KeePass decides whether a database is already open by trimming its path and lowering
            // it in the current culture, and under Turkish a capital I lowers to an i without its
            // dot, so these two are two documents to KeePass. Compared without case they were one
            // here: the close of one released the lock of the other, which then answered nothing.
            var was = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            try
            {
                using (var other = TemporaryDatabase.New())
                {
                    var created = DatabaseAuthority.Of(other.Database, _log).Create();
                    Assert.That(created.Succeeded, Is.True, created.Message);

                    string locked;
                    using (var reading = DatabaseAuthority.Of(_database.Database, _log).Read())
                    {
                        locked = reading.Material.AuthorityDigest;
                    }

                    _database.Database.IOConnectionInfo.Path = "C:\\invented\\Ideas.kdbx";
                    other.Database.IOConnectionInfo.Path = "C:\\invented\\ideas.kdbx";
                    var open = new List<PwDatabase> { _database.Database, other.Database };
                    using (var targets = new OpenDatabaseTargets(delegate { return open; }, _log))
                    {
                        Assume.That(targets.Current().Count, Is.EqualTo(2));

                        _database.Database.Close();
                        open.Remove(_database.Database);
                        Assert.That(targets.Lock("C:\\invented\\Ideas.kdbx"), Is.True);
                        Assert.That(targets.Forget("C:\\invented\\ideas.kdbx"), Is.Not.Empty, "the other database closes");

                        var answering = targets.Current().Where(target => target.IsLocked).ToList();
                        Assert.That(answering, Has.Count.EqualTo(1), "and the lock of the first still answers");
                        Assert.That(answering[0].DatabaseId, Is.EqualTo(locked));
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }
        }

        [Test]
        public void The_spellings_stay_apart_on_a_thread_of_another_culture()
        {
            // The comparison takes KeePass's culture once, as it is made on KeePass's thread, and a
            // connection thread looks paths up too. Read at each comparison instead, the test above
            // would still pass, everything in it running on one thread.
            KeePassPaths paths;
            var was = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            try
            {
                paths = new KeePassPaths();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }

            var equal = true;
            var sameHash = true;
            var elsewhere = new Thread(() =>
            {
                equal = paths.Equals("C:\\invented\\Ideas.kdbx", "C:\\invented\\ideas.kdbx");
                sameHash = paths.GetHashCode("C:\\invented\\Ideas.kdbx") == paths.GetHashCode("C:\\invented\\ideas.kdbx");
            })
            {
                CurrentCulture = new System.Globalization.CultureInfo("en-US"),
            };
            elsewhere.Start();
            Assert.That(elsewhere.Join(TimeSpan.FromSeconds(10)), Is.True);

            Assert.That(equal, Is.False, "an en-US thread joined two paths KeePass's Turkish thread keeps apart");
            Assert.That(sameHash, Is.False, "and hashed them alike, so a lookup from there finds the other one");
        }

        [Test]
        public void A_lock_after_a_save_as_still_answers_for_the_database()
        {
            // Save As moves the path under the same `PwDatabase` and KeePass says so only as a
            // save. With no connection between it and the lock, the entry still carried the old
            // path, the lock matched nothing and the locked database answered nothing at all.
            var databaseId = _targets.Current().Single().DatabaseId;
            var moved = Paths.Join(System.IO.Path.GetDirectoryName(_database.Path), "moved.kdbx");
            _database.Database.SaveAs(IOConnectionInfo.FromPath(moved), true, null);

            _targets.Refresh();
            _database.Database.Close();
            _open.Clear();

            Assert.That(_targets.Lock(moved), Is.True, "the lock is announced under the path it has now");

            var locked = _targets.Current().Single();
            Assert.That(locked.IsLocked, Is.True);
            Assert.That(locked.DatabaseId, Is.EqualTo(databaseId));
        }

        [Test]
        public void A_real_close_after_a_save_as_leaves_nothing()
        {
            // The close names the new path. Matched against the old one it dropped nothing, and the
            // next connection parked a responder and a gate key under the old path until exit.
            var original = _database.Path;
            var moved = Paths.Join(System.IO.Path.GetDirectoryName(original), "moved.kdbx");
            _database.Database.SaveAs(IOConnectionInfo.FromPath(moved), true, null);

            _targets.Refresh();
            _database.Database.Close();
            _open.Clear();
            Assert.That(_targets.Forget(moved), Is.Not.Empty, "it was served under the path it has now");

            Assert.That(_targets.Current(), Is.Empty);
            Assert.That(_targets.Lock(original), Is.False,
                "nothing may be held under the old path for a lock that will never come");
            Assert.That(_targets.Current(), Is.Empty);
        }

        [Test]
        public void An_authority_created_after_the_open_is_answered_for_at_the_first_lock()
        {
            // Opened with no authority, so the open built nothing; the authority arrives from the
            // KPG tab afterwards, and nothing connects before the lock.
            using (var later = TemporaryDatabase.New())
            {
                _open.Add(later.Database);
                _targets.Unlock(later.Path);

                var created = DatabaseAuthority.Of(later.Database, _log).Create();
                Assert.That(created.Succeeded, Is.True, created.Message);

                _targets.Refresh();
                later.Database.Close();
                _open.Remove(later.Database);

                Assert.That(_targets.Lock(later.Path), Is.True);
                Assert.That(_targets.Current().Count(target => target.IsLocked), Is.EqualTo(1),
                    "a database locked before anybody connected answers 503 like any other");
            }
        }

        [Test]
        public void An_authority_deleted_since_the_last_connection_is_not_answered_for()
        {
            // The lock minted a responder from the entry it found, and answered `503 locked` in the
            // name of an authority the operator had deleted.
            _targets.Current();

            var deleted = DatabaseAuthority.Of(_database.Database, _log).Delete();
            Assert.That(deleted.Succeeded, Is.True, deleted.Message);

            _targets.Refresh();
            var path = _database.Path;
            _database.Database.Close();
            _open.Clear();

            Assert.That(_targets.Lock(path), Is.False, "there was nothing being served to lock");
            Assert.That(_targets.Current(), Is.Empty, "and nothing answers for the authority that went");
        }

        [Test]
        public void Locking_twice_is_the_same_as_locking_once()
        {
            _targets.Current();

            Lock();
            _targets.Lock(_database.Path);

            Assert.That(_targets.Current().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What KeePass does when the workspace is locked: the database is closed and the file it
        /// came from is remembered elsewhere. The plugin is told the path, which is all that is
        /// left to recognize it by.
        /// </summary>
        private void Lock()
        {
            var path = _database.Path;
            _database.Database.Close();
            _open.Clear();

            _targets.Lock(path);
        }

        /// <summary>The database's file opened again, as KeePass opens it at an unlock.</summary>
        private void Reopen()
        {
            _database.Reopen();
            _open.Clear();
            _open.Add(_database.Database);
        }

        private KdbxStore Store()
        {
            return new KdbxStore(_database.Database, GateVersion.Current, _log);
        }

        private static StoreState State(KdbxStore store)
        {
            using (var reading = store.Read())
            {
                return reading.State;
            }
        }

        private IEnumerable<PwEntry> Entries(string kind)
        {
            return _database.RootGroup.GetEntries(true).Where(entry => entry.Strings.ReadSafe("KPG.Kind") == kind).ToList();
        }

        private PwEntry Entry(string kind)
        {
            return Entries(kind).Single();
        }

        /// <summary>An entry deleted for good, with the deletion record KeePass writes for it.</summary>
        private void DeleteForGood(PwEntry entry)
        {
            entry.ParentGroup.Entries.Remove(entry);
            _database.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
        }

        private void Unlock()
        {
            Reopen();

            using (var reading = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance).Read())
            {
                Assert.That(reading.IsPresent, Is.True,
                    "the file this unlock reopened has no authority in it: " + string.Join("; ", reading.Problems));
            }

            Assert.That(_targets.Unlock(_database.Path), Is.Not.Empty, "an unlock after a lock lifts it");
        }

        private System.Security.Cryptography.X509Certificates.X509Certificate2 IssueAndAdmit()
        {
            using (var reading = DatabaseAuthority.Of(_database.Database, _log).Read())
            {
                var certificate = new AuthorityIssuer().IssueClient(
                    reading.Material.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read();

                new StoredAdmissions(_database.Database)
                    .Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

                _database.Save();
                return certificate;
            }
        }

        /// <summary>
        /// Runs every read where it is asked, and closes the database once the first read of the
        /// word a connection is answered with has returned, as a lock landing between that read
        /// and the admission's would.
        /// </summary>
        private sealed class LockAfterFirstRead : IReadHold
        {
            private readonly Action _lock;

            public LockAfterFirstRead(Action lockIt)
            {
                _lock = lockIt;
            }

            public bool Armed { get; set; }

            public long Stamp
            {
                get { return 0; }
            }

            public T Hold<T>(Func<T> read)
            {
                var value = read();
                if (Armed && typeof(T) == typeof(string))
                {
                    Armed = false;
                    _lock();
                }

                return value;
            }
        }

        private sealed class RecordingLog : IGateLog
        {
            public void Info(string message)
            {
                TestContext.Out.WriteLine("INFO  " + message);
            }

            public void Warn(string message)
            {
                TestContext.Out.WriteLine("WARN  " + message);
            }

            public void Error(string message, Exception error)
            {
                TestContext.Out.WriteLine("ERROR " + message + " | " + error);
            }

            public void Unexpected(string message, Exception error)
            {
                Assert.Fail("Something nobody predicted happened: " + message + " | " + error);
            }
        }
    }
}
