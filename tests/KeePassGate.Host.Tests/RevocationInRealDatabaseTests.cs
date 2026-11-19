using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A revocation reaching the file, rather than only the database in memory.
    /// </summary>
    /// <remarks>
    /// The panel's own tests prove that the store refuses the client and that KeePass is told
    /// there is something to save. Neither of those is the property that matters to the operator,
    /// who is told at approval time that a client can be revoked later and then walks away.
    ///
    /// **The dangerous case is a revocation that only happened in memory**, because the client is
    /// back the next time KeePass opens that file and nothing said so. That is what this asks: the
    /// panel revokes, KeePass saves, the file is closed and opened again, and the client has to
    /// still be refused. It is the same question the pairing side already asks of a write.
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

            var created = DatabaseAuthority.Of(_database.Database, null).Create();
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
                Assert.That(panel.Revoke(record.ClientKey), Is.Null);
            }

            // What the tab does is change the database and say there is something to save. This is
            // the operator doing the saving, which is the half the tab deliberately leaves to them.
            _database.SaveCloseAndReopen();

            var found = new StoredAdmissions(_database.Database)
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
                Assume.That(panel.Revoke(record.ClientKey), Is.Null);
            }

            _database.CloseAndReopenWithoutSaving();

            var found = new StoredAdmissions(_database.Database)
                .Find(record.AdmissionKey);

            Assert.That(found, Is.Not.Null, "an unsaved revocation must not read as a saved one");
        }

        [Test]
        public void A_certificate_taken_away_on_the_tab_is_gone_from_the_file_after_OK_and_a_save()
        {
            var record = Admit();
            var pending = new PendingDatabase(Live(), NullGateLog.Instance);
            using (var panel = new AuthorityPanel(pending, null, NullGateLog.Instance))
            {
                Assert.That(panel.TakeAway(record.AdmissionKey), Is.Null);
            }

            Assert.That(pending.Apply(), Is.True);
            _database.SaveCloseAndReopen();

            var admissions = new StoredAdmissions(_database.Database);
            Assert.That(admissions.Find(record.AdmissionKey), Is.Null, "the certificate is admitted again tomorrow");
            Assert.That(admissions.Holds(KeePassGate.Store.StoreKinds.Withdrawn, KeePassGate.Store.StoreKinds.Certificate, record.AdmissionKey), Is.True);
        }

        [Test]
        public void A_certificate_taken_away_on_the_tab_that_is_never_saved_stays_admitted()
        {
            var record = Admit();
            var pending = new PendingDatabase(Live(), NullGateLog.Instance);
            using (var panel = new AuthorityPanel(pending, null, NullGateLog.Instance))
            {
                Assume.That(panel.TakeAway(record.AdmissionKey), Is.Null);
            }

            Assume.That(pending.Apply(), Is.True);
            _database.CloseAndReopenWithoutSaving();

            Assert.That(new StoredAdmissions(_database.Database).Find(record.AdmissionKey), Is.Not.Null);
        }

        [Test]
        public void A_certificate_given_up_by_its_client_and_the_certificate_before_it_taken_away_on_the_tab_leave_the_newest_admitted()
        {
            // A client paired, renewed twice and gave the certificate between up, while giving up
            // the first never landed. The operator then takes the first away on the tab: the
            // record OK writes names the certificate between, which its client's own record holds
            // as renewed from the first, and the newest certificate keeps admitting.
            var paired = Admit();
            var between = Admit(paired);
            var newest = Admit(between);
            var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            Assume.That(writer.GiveUp(_database.Database, between.AdmissionKey, Served()).Ok, Is.True);

            var pending = new PendingDatabase(Live(), NullGateLog.Instance);
            using (var panel = new AuthorityPanel(pending, null, NullGateLog.Instance))
            {
                Assert.That(panel.TakeAway(paired.AdmissionKey), Is.Null);
            }

            Assert.That(pending.Apply(), Is.True);
            _database.SaveCloseAndReopen();

            var admissions = new StoredAdmissions(_database.Database);
            Assert.That(admissions.Find(newest.AdmissionKey), Is.Not.Null);
            Assert.That(admissions.Find(paired.AdmissionKey), Is.Null);
            Assert.That(admissions.Find(between.AdmissionKey), Is.Null);
        }

        private AuthorityPanel NewPanel()
        {
            return new AuthorityPanel(Live(), null, NullGateLog.Instance);
        }

        /// <summary>The database as the gate serves it, written through the writer.</summary>
        private OpenDatabase Live()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            return new OpenDatabase(_database.Database, null, writer, NullGateLog.Instance);
        }

        private string Served()
        {
            return StoreSeeds.Served(new StoredAdmissions(_database.Database).Store);
        }

        /// <summary>A pairing's admission, or a renewal's from the origin given, saved.</summary>
        private AdmissionRecord Admit(AdmissionRecord origin = null)
        {
            using (var certificate = IssueClient())
            {
                var record = origin == null
                    ? AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer)
                    : AdmissionRecord.For(
                        certificate, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer, origin.ClientKey, origin.AdmissionKey);
                new StoredAdmissions(_database.Database).Write(record);
                _database.Save();
                return record;
            }
        }

        private X509Certificate2 IssueClient()
        {
            using (var reading = DatabaseAuthority.Of(_database.Database, null).Read())
            {
                return new AuthorityIssuer().IssueClient(
                    reading.Material.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read();
            }
        }
    }
}
