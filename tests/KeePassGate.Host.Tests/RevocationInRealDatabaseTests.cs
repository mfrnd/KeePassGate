using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A revocation reaching the file, rather than only the database in memory.
    /// </summary>
    /// <remarks>
    /// The panel's own tests prove that the record leaves the store and that KeePass is told there
    /// is something to save. Neither of those is the property that matters to the operator, who is
    /// told at approval time that a client can be revoked later and then walks away.
    ///
    /// **The dangerous case is a revocation that only happened in memory**, because the client is
    /// back the next time KeePass opens that file and nothing said so. That is what this asks: the
    /// panel revokes, KeePass saves, the file is closed and opened again, and the record has to
    /// still be gone. It is the same question the pairing side already asks of a write.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class RevocationInRealDatabaseTests
    {
        private TemporaryDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            var created = new AuthorityService(new CustomDataStore(_database.Database), null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_revoked_client_is_gone_from_the_file_and_not_only_from_memory()
        {
            var record = Admit();

            using (var panel = NewPanel())
            {
                Assume.That(panel.AdmittedClients().Records.Count, Is.EqualTo(1));
                Assert.That(panel.Revoke(record), Is.Null);
            }

            // What the tab does is change the database and say there is something to save. This is
            // the operator doing the saving, which is the half the tab deliberately leaves to them.
            _database.SaveCloseAndReopen();

            var found = new AdmissionStore(new CustomDataStore(_database.Database), null)
                .Find(record.AdmissionKey);

            Assert.That(found, Is.Null, "the client is admitted again tomorrow");
        }

        [Test]
        public void A_revocation_that_is_never_saved_leaves_the_client_admitted()
        {
            // The other half of the same sentence. A panel built straight on the live database
            // writes as soon as it is asked, which is what this drives; the tab wraps it in
            // `PendingDatabase` so that nothing lands before OK. Saving is what keeps it either
            // way, and a test that only proved the happy path would let that stop being true.
            var record = Admit();

            using (var panel = NewPanel())
            {
                Assume.That(panel.Revoke(record), Is.Null);
            }

            _database.CloseAndReopenWithoutSaving();

            var found = new AdmissionStore(new CustomDataStore(_database.Database), null)
                .Find(record.AdmissionKey);

            Assert.That(found, Is.Not.Null, "an unsaved revocation must not read as a saved one");
        }

        private AuthorityPanel NewPanel()
        {
            return new AuthorityPanel(new OpenDatabase(_database.Database), null, NullGateLog.Instance);
        }

        private AdmissionRecord Admit()
        {
            using (var certificate = IssueClient())
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                new AdmissionStore(new CustomDataStore(_database.Database), null).Write(record);
                _database.Save();
                return record;
            }
        }

        private X509Certificate2 IssueClient()
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), null).Read())
            {
                return new AuthorityIssuer().IssueClient(
                    reading.Material.Authority, "agent-fictitious", new[] { "ops" });
            }
        }
    }
}
