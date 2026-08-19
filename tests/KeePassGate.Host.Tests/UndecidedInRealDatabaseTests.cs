using System.Collections.Generic;
using System.Linq;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A real database holding two authorities nobody has chosen between, as the gate sees it.
    /// </summary>
    /// <remarks>
    /// Nothing is served: each candidate answers under its own gate key that nothing is decided,
    /// no responder is minted, and a lock therefore answers nothing at all. Keeping one serves it
    /// on the next connection, because the cache follows what is stored.
    /// </remarks>
    [TestFixture]
    public sealed class UndecidedInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private OpenDatabaseTargets _targets;
        private List<PwDatabase> _open;
        private string[] _candidates;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _candidates = TwoAuthorities();
            _database.Save();

            _open = new List<PwDatabase> { _database.Database };
            _targets = new OpenDatabaseTargets(delegate { return _open; }, NullGateLog.Instance);
            _targets.Unlock(_database.Path);
        }

        [TearDown]
        public void TearDown()
        {
            _targets.Dispose();
            _database.Dispose();
        }

        [Test]
        public void Each_candidate_answers_that_nothing_is_decided_and_none_is_served()
        {
            var targets = _targets.Current();

            Assert.That(targets.Select(t => t.DatabaseId), Is.EquivalentTo(_candidates),
                "one per candidate, so the clients of either reach an answer");

            foreach (var target in targets)
            {
                Assert.That(target.Unavailable, Is.EqualTo(UndecidedTarget.Error));
                Assert.That(target.IsLocked, Is.False);
                Assert.That(target.Authority, Is.Null, "no authority is loaded to judge a certificate with");
                Assert.That(target.Vault, Is.Null);
                target.Dispose();
            }
        }

        [Test]
        public void A_lock_answers_nothing_because_no_responder_was_minted()
        {
            var path = _database.Path;
            _targets.Refresh();
            _database.Database.Close();
            _open.Clear();

            _targets.Lock(path);

            Assert.That(_targets.Current(), Is.Empty,
                "minting a responder would have been choosing one of the authorities");
        }

        [Test]
        public void Keeping_one_serves_it_on_the_next_connection()
        {
            var kept = new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance)
                .Keep(_candidates[1]);
            Assume.That(kept.Succeeded, Is.True, kept.Message);

            var target = _targets.Current().Single();

            Assert.That(target.DatabaseId, Is.EqualTo(_candidates[1]));
            Assert.That(target.Unavailable, Is.Null);
            Assert.That(target.Authority, Is.Not.Null);
            target.Dispose();
        }

        /// <summary>
        /// Two authorities side by side and neither retired, which is what two copies each
        /// recreating and then being merged leave behind.
        /// </summary>
        private string[] TwoAuthorities()
        {
            var store = new CustomDataStore(_database.Database);
            var service = new AuthorityService(store, NullGateLog.Instance);

            Assume.That(service.Create().Succeeded, Is.True);
            var first = store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            var document = store.Get(first);
            store.Remove(first);

            Assume.That(service.Create().Succeeded, Is.True);
            var second = store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null);
            store.Set(first, document);

            return new[] { AuthorityKeys.FingerprintInAuthority(first), AuthorityKeys.FingerprintInAuthority(second) };
        }
    }
}
