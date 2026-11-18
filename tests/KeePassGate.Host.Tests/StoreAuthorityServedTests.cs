using System;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The authority a database is served with is the store's, `STORE.md` §614: a client is judged
    /// against it by the anchor pin before any record is asked, and the target is held to what
    /// stands in the store.
    /// </summary>
    [TestFixture]
    public sealed class StoreAuthorityServedTests
    {
        private TemporaryDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_record_of_a_certificate_another_authority_issued_admits_nobody()
        {
            var store = new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance);
            var admissions = new StoredAdmissions(_database.Database);
            StoreSeeds.Authority(store);

            var issuer = new AuthorityIssuer();
            using (var served = new AuthorityService(store, NullGateLog.Instance).Read())
            using (var own = issuer.IssueClient(served.Material.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read())
            using (var foreign = issuer.IssueClient(issuer.CreateAuthority(), "agent-fictitious", new[] { "ops" }).Read())
            {
                admissions.Write(AdmissionRecord.For(own, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
                admissions.Write(AdmissionRecord.For(foreign, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

                using (var targets = new OpenDatabaseTargets(() => new[] { _database.Database }, NullGateLog.Instance))
                {
                    using (var target = targets.Current().Single())
                    {
                        Assert.That(ClientCredentials.For(own, target, NullGateLog.Instance).IsPaired, Is.True, "a client of the authority served");
                        Assert.That(ClientCredentials.For(foreign, target, NullGateLog.Instance).IsPaired, Is.False, "a record of another authority's certificate");
                    }
                }
            }
        }

        [Test]
        public void A_database_whose_authority_will_not_read_leaves_every_other_database_served()
        {
            // What one database holds is that database's business, `CA.md` §204.
            var served = StoreSeeds.Authority(
                new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance));

            using (var damaged = TemporaryDatabase.New())
            {
                StoreSeeds.Damaged(new KdbxStore(damaged.Database, GateVersion.Current, NullGateLog.Instance));

                using (var targets = new OpenDatabaseTargets(() => new[] { damaged.Database, _database.Database }, NullGateLog.Instance))
                {
                    using (var target = targets.Current().Single())
                    {
                        Assert.That(target.DatabaseId, Is.EqualTo(served), "the database whose authority reads is the database served");
                    }
                }
            }
        }

        [Test]
        public void A_database_with_no_store_key_is_served_nothing()
        {
            using (var targets = new OpenDatabaseTargets(() => new[] { _database.Database }, NullGateLog.Instance))
            {
                Assert.That(targets.Current(), Is.Empty);
            }
        }

        [Test]
        public void A_retirement_written_under_a_served_database_refuses_at_the_next_connection_and_inside_a_held_target()
        {
            var store = new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance);
            var admissions = new StoredAdmissions(_database.Database);
            StoreSeeds.Authority(store);

            var issuer = new AuthorityIssuer();
            using (var served = new AuthorityService(store, NullGateLog.Instance).Read())
            using (var client = issuer.IssueClient(served.Material.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read())
            using (var targets = new OpenDatabaseTargets(() => new[] { _database.Database }, NullGateLog.Instance))
            {
                admissions.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
                using (var held = targets.Current().Single())
                {
                    Assume.That(ClientCredentials.For(client, held, NullGateLog.Instance).IsPaired, Is.True);

                    // A retirement alone, written as a merge brings it: what refuses is the store,
                    // whose write deletes the admission the retirement refuses.
                    byte[] key;
                    string retired;
                    using (var reading = store.Read())
                    {
                        key = (byte[])reading.InForce.Key.Bytes.Clone();
                        retired = StoreAuthorities.Served(reading);
                    }

                    var retirement = StoreRecord.Compose(key, "retired", new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "authority", retired },
                        { "storeKey", KeePassGate.Hex.Of(key) },
                    });
                    Assert.That(store.Write(new[] { new StoreAct("a retirement", key, StoreFormat.KeyName(key), new[] { retirement }, null) }).Taken, Is.True);

                    Assert.That(ClientCredentials.For(client, held, NullGateLog.Instance).IsPaired, Is.False, "a target held across the retirement");
                    Assert.That(targets.Current(), Is.Empty, "nothing stands to serve");
                }
            }
        }

        [Test]
        public void Each_connection_walks_the_store_once_for_its_target_and_its_admission()
        {
            // The listener holds KeePass's thread once for each connection, and `Current` and the
            // admission each ask inside that hold. A walk is counted by what it finds: the store is
            // changed inside a hold once the target is read, which nothing on KeePass's thread can
            // do while the hold stands, so an admission that walked the store again would find its
            // authority retired and refuse, and the next connection, in a hold of its own, walks
            // it again and finds nothing served.
            var store = new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance);
            var admissions = new StoredAdmissions(_database.Database);
            var authority = StoreSeeds.Authority(store);

            var issuer = new AuthorityIssuer();
            var hold = new Holds();
            using (var served = new AuthorityService(store, NullGateLog.Instance).Read())
            using (var client = issuer.IssueClient(served.Material.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read())
            using (var targets = new OpenDatabaseTargets(() => new[] { _database.Database }, null, hold, NullGateLog.Instance))
            {
                admissions.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

                var paired = hold.Hold(() =>
                {
                    using (var target = targets.Current().Single())
                    {
                        StoreSeeds.Retire(store, authority);
                        return ClientCredentials.For(client, target, NullGateLog.Instance).IsPaired;
                    }
                });

                Assert.That(hold.Asked, Is.GreaterThanOrEqualTo(2), "the target and the admission each asked the store");
                Assert.That(paired, Is.True, "the admission walked nothing the target had not");
                Assert.That(hold.Hold(() => targets.Current()), Is.Empty, "and the next connection walked the store again");
            }
        }

        [Test]
        public void A_target_is_handed_out_as_it_is_while_what_stands_is_unchanged()
        {
            var store = new KdbxStore(_database.Database, GateVersion.Current, NullGateLog.Instance);
            StoreSeeds.Authority(store);

            using (var targets = new OpenDatabaseTargets(() => new[] { _database.Database }, NullGateLog.Instance))
            {
                using (var first = targets.Current().Single())
                using (var second = targets.Current().Single())
                {
                    Assert.That(second, Is.SameAs(first), "nothing parsed again for an unchanged store");
                }
            }
        }

        /// <summary>A hold that hands each outermost hold a stamp of its own, and counts how often a reading asked it.</summary>
        private sealed class Holds : IReadHold
        {
            private long _given;
            private long _stamp;

            /// <summary>How often a reading asked which hold it is in.</summary>
            public int Asked { get; private set; }

            public long Stamp
            {
                get
                {
                    Asked++;
                    return _stamp;
                }
            }

            public T Hold<T>(Func<T> read)
            {
                if (_stamp != 0)
                {
                    return read();
                }

                _stamp = ++_given;
                try
                {
                    return read();
                }
                finally
                {
                    _stamp = 0;
                }
            }
        }
    }
}