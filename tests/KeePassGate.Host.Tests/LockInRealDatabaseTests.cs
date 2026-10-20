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
using KeePassGate.Testing;
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

            var created = new AuthorityService(new CustomDataStore(_database.Database), _log).Create();
            Assert.That(created.Succeeded, Is.True, created.Message);
            _database.Save();

            _open = new List<PwDatabase> { _database.Database };
            _targets = new OpenDatabaseTargets(delegate { return _open; }, _log);

            // What the plugin does when KeePass says a database has been opened, and what mints the
            // responder that a later lock will answer with.
            Assert.That(_targets.Unlock(_database.Path), Is.False, "a first open lifts no lock");
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

                Assert.That(after.Authority, Is.Null, "the key that signs is what a lock exists to drop");
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
        public void The_responder_is_a_different_one_after_every_unlock()
        {
            _targets.Current();
            Lock();
            var first = _targets.Current().Single().Host.CertificateDer;

            Unlock();
            _targets.Current();
            Lock();
            var second = _targets.Current().Single().Host.CertificateDer;

            Assert.That(second, Is.Not.EqualTo(first), "a responder is minted at unlock, not kept for ever");
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
            _targets.Current();

            _open.Clear();

            // True, so that the connections still reading it are closed, as they are at a lock. It
            // answered only for a lock, and a served database closed without one closed nothing.
            Assert.That(_targets.Forget(_database.Path), Is.True, "there was something being served");
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
                var created = new AuthorityService(new CustomDataStore(later.Database), _log).Create();
                Assert.That(created.Succeeded, Is.True, created.Message);
                _open.Add(later.Database);

                Assume.That(_targets.Current().Count, Is.EqualTo(2), "a connection read it before the event");

                Assert.That(_targets.Unlock(later.Path), Is.False);
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

            Assert.That(_targets.Forget(path), Is.True,
                "it was served until that connection found it shut, and what was reading it then may still be");
            Assert.That(_targets.Current(), Is.Empty, "a closed database answers nothing, unlike a locked one");
        }

        [Test]
        public void A_database_that_was_never_served_can_still_be_locked_without_complaint()
        {
            // Locked before anything reached it, or with no authority at all. There is nothing to
            // answer with and nothing to drop.
            Assert.DoesNotThrow(() => _targets.Lock("C:\\invented\\never-served.kdbx"));
            Assert.That(_targets.Forget("C:\\invented\\never-served.kdbx"), Is.False,
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
                    var created = new AuthorityService(new CustomDataStore(other.Database), _log).Create();
                    Assert.That(created.Succeeded, Is.True, created.Message);

                    string locked;
                    using (var reading = new AuthorityService(new CustomDataStore(_database.Database), _log).Read())
                    {
                        locked = reading.Material.AuthorityDigest;
                    }

                    _database.Database.IOConnectionInfo.Path = "C:\\invented\\Ideas.kdbx";
                    other.Database.IOConnectionInfo.Path = "C:\\invented\\ideas.kdbx";
                    var open = new List<PwDatabase> { _database.Database, other.Database };
                    var targets = new OpenDatabaseTargets(delegate { return open; }, _log);

                    try
                    {
                        Assume.That(targets.Current().Count, Is.EqualTo(2));

                        _database.Database.Close();
                        open.Remove(_database.Database);
                        Assert.That(targets.Lock("C:\\invented\\Ideas.kdbx"), Is.True);
                        Assert.That(targets.Forget("C:\\invented\\ideas.kdbx"), Is.True, "the other one closes");

                        var answering = targets.Current().Where(target => target.IsLocked).ToList();
                        Assert.That(answering, Has.Count.EqualTo(1), "and the lock of the first still answers");
                        Assert.That(answering[0].DatabaseId, Is.EqualTo(locked));
                    }
                    finally
                    {
                        targets.Dispose();
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }
        }

        [Test]
        public void A_lock_after_a_save_as_still_answers_for_the_database()
        {
            // Save As moves the path under the same `PwDatabase` and KeePass says so only as a
            // save. With no connection between it and the lock, the entry still carried the old
            // path, the lock matched nothing and the locked database answered nothing at all.
            var databaseId = _targets.Current().Single().DatabaseId;
            var moved = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_database.Path), "moved.kdbx");
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
            var moved = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(original), "moved.kdbx");
            _database.Database.SaveAs(IOConnectionInfo.FromPath(moved), true, null);

            _targets.Refresh();
            _database.Database.Close();
            _open.Clear();
            Assert.That(_targets.Forget(moved), Is.True, "it was served under the path it has now");

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

                var created = new AuthorityService(new CustomDataStore(later.Database), _log).Create();
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

            var deleted = new AuthorityService(new CustomDataStore(_database.Database), _log).Delete();
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

        private void Unlock()
        {
            _database.Reopen();
            _open.Clear();
            _open.Add(_database.Database);

            using (var reading = new AuthorityStore(new CustomDataStore(_database.Database), NullGateLog.Instance).Read())
            {
                Assert.That(reading.IsPresent, Is.True,
                    "the file this unlock reopened has no authority in it: " + string.Join("; ", reading.Problems));
            }

            Assert.That(_targets.Unlock(_database.Path), Is.True, "an unlock after a lock lifts it");
        }

        private System.Security.Cryptography.X509Certificates.X509Certificate2 IssueAndAdmit()
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), _log).Read())
            {
                var certificate = new AuthorityIssuer().IssueClient(
                    reading.Material.Authority, "agent-fictitious", new[] { "ops" });

                new AdmissionStore(new CustomDataStore(_database.Database), _log)
                    .Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));

                _database.Save();
                return certificate;
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
