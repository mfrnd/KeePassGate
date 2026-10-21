using System;
using System.Threading;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A client revoking its own certificate against a real KeePass database, saved to a real file
    /// and opened again.
    /// </summary>
    /// <remarks>
    /// What `DELETE /gate/certificate` writes is what the Clients page writes, which
    /// `RevocationInRealDatabaseTests` follows to the file, and what decides whether it happened is
    /// what decides it for every change: KeePass's own save, the modified flag it clears, and the
    /// file hashed back afterwards. Each way that can go wrong leaves the client admitted or not,
    /// and these say which.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class SelfRevocationInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _dialogs.Start();
            _saves = 0;

            _writer = new DatabaseWriter(CurrentThread.Instance, Save, null, _dialogs, null);

            var created = new AuthorityService(new CustomDataStore(_database.Database), null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_client_that_revokes_itself_leaves_its_revocation_in_the_file()
        {
            var key = Admitted();

            var result = _writer.Revoke(_database.Database, key);

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(_saves, Is.EqualTo(1));

            _database.SaveCloseAndReopen();

            var store = new CustomDataStore(_database.Database);
            Assert.That(store.Get(AuthorityKeys.ClientRecordKey(key)), Is.Empty, "the record is emptied");
            Assert.That(store.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null.And.Not.Empty,
                "and the revocation beside it is in the file, not only in memory");
            Assert.That(new AdmissionStore(store, null).Find(key), Is.Null);
        }

        [Test]
        public void A_revocation_that_lands_is_announced_and_one_that_does_not_is_not()
        {
            // What the Clients page listens to, so that a client leaving while the page is open
            // leaves the list too.
            var announced = 0;
            var landing = new DatabaseWriter(CurrentThread.Instance, Save, null, () => announced++, _dialogs, null);
            var failing = new DatabaseWriter(CurrentThread.Instance, database => { }, null, () => announced++, _dialogs, null);

            Assume.That(failing.Revoke(_database.Database, Admitted()).Ok, Is.False);
            Assert.That(announced, Is.Zero, "a revocation taken back out changed nothing a page shows");

            Assert.That(landing.Revoke(_database.Database, Admitted()).Ok, Is.True);
            Assert.That(announced, Is.EqualTo(1));
        }

        [Test]
        public void A_page_that_fails_after_the_revocation_landed_cannot_lose_its_audit_line()
        {
            // The writer writes the line on its own landed path, before it tells anything drawn
            // from the records, so a defect in a listener reaches the connection thread with the
            // revocation on the disk and written down once.
            var key = Admitted();
            var lines = 0;
            var linesWhenTold = -1;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                Save,
                null,
                () =>
                {
                    linesWhenTold = lines;
                    throw new NullReferenceException("a listener's defect");
                },
                _dialogs,
                null);

            Assert.Throws<NullReferenceException>(() => writer.Revoke(_database.Database, key, () => lines++));

            Assert.That(lines, Is.EqualTo(1));
            Assert.That(linesWhenTold, Is.EqualTo(1), "written before the page was told");

            _database.SaveCloseAndReopen();

            var store = new CustomDataStore(_database.Database);
            Assert.That(store.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null.And.Not.Empty,
                "and the revocation the line is about is in the file");
        }

        [Test]
        public void The_audit_line_is_written_once_for_a_revocation_left_in_force_and_never_otherwise()
        {
            var lines = 0;
            var failing = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);

            Assume.That(failing.Revoke(_database.Database, Admitted(), () => lines++).IsUnsaved, Is.True);
            Assert.That(lines, Is.Zero, "a revocation taken back out revoked nothing");

            Assume.That(_writer.Revoke(_database.Database, new string('e', 64), () => lines++).Ok, Is.False);
            Assert.That(lines, Is.Zero, "nor did one with nothing to revoke");

            Assert.That(_writer.Revoke(_database.Database, Admitted(), () => lines++).Ok, Is.True);
            Assert.That(lines, Is.EqualTo(1));
        }

        [Test]
        public void A_revocation_the_file_did_not_keep_is_written_down_since_it_stays_in_force()
        {
            var key = Admitted();
            _database.Save();
            var theCopyAsItWas = System.IO.File.ReadAllBytes(_database.Path);
            var lines = 0;

            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    System.IO.File.WriteAllBytes(_database.Path, theCopyAsItWas);
                },
                null,
                _dialogs,
                null);

            var result = writer.Revoke(_database.Database, key, () => lines++);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(lines, Is.EqualTo(1));
        }

        [Test]
        public void A_revocation_the_file_did_not_keep_is_announced_since_it_stays_in_force()
        {
            // Saved and not verified, the revocation is left in place and the certificate is
            // refused from its next request: a page not told would go on listing a client that
            // every request refuses.
            var key = Admitted();
            _database.Save();
            var theCopyAsItWas = System.IO.File.ReadAllBytes(_database.Path);
            var announced = 0;

            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    System.IO.File.WriteAllBytes(_database.Path, theCopyAsItWas);
                },
                null,
                () => announced++,
                _dialogs,
                null);

            var result = writer.Revoke(_database.Database, key);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(announced, Is.EqualTo(1));
        }

        [Test]
        public void The_maintenance_dialog_open_on_the_database_refuses_the_revocation()
        {
            // That dialog writes its copy of the database's custom data back whenever the number
            // of keys differs, and a revocation adds one: its OK would put the record back without
            // the revocation, and the client would be admitted again.
            var key = Admitted();

            using (var form = MaintenanceDialog())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var result = _writer.Revoke(_database.Database, key);

                    Assert.That(result.IsBusy, Is.True, result.Reason);
                    Assert.That(_saves, Is.Zero);
                    Assert.That(new AdmissionStore(new CustomDataStore(_database.Database), null).Find(key), Is.Not.Null,
                        "nothing was written, so the client is still admitted and may ask again");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_revocation_no_save_kept_is_taken_back_out_and_the_client_stays_admitted()
        {
            var key = Admitted();
            var expired = PlantExpiredRecord();
            _database.Database.Modified = false;

            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = writer.Revoke(_database.Database, key);

            Assert.That(result.IsUnsaved, Is.True, result.Reason);

            var store = new CustomDataStore(_database.Database);
            Assert.That(new AdmissionStore(store, null).Find(key), Is.Not.Null, "still admitted, so asking again is safe");
            Assert.That(store.Get(AuthorityKeys.RevocationKey(key)), Is.Null, "and no revocation is left beside it");
            Assert.That(store.Get(AuthorityKeys.ClientRecordKey(expired)), Is.Not.Null,
                "what the revocation pruned goes back with it, so memory agrees with the file");
            Assert.That(_database.Database.Modified, Is.False);
        }

        [Test]
        public void A_revocation_no_save_kept_puts_back_every_other_revocation_too()
        {
            // What is taken back out is everything the write changed and nothing it did not:
            // another client's revocation, and an expired one the write's prune took away with the
            // record it emptied. Both are revocations, which the snapshot has to hold, or putting
            // it back removes them.
            var key = Admitted();
            var standing = new string('b', 64);
            var expired = new string('c', 64);
            var data = _database.Database.CustomData;
            data.Set(AuthorityKeys.RevocationKey(standing), Moments.Wire(DateTimeOffset.UtcNow.AddDays(30)));
            data.Set(AuthorityKeys.ClientRecordKey(standing), string.Empty);
            data.Set(AuthorityKeys.RevocationKey(expired), Moments.Wire(DateTimeOffset.UtcNow.AddDays(-30)));
            data.Set(AuthorityKeys.ClientRecordKey(expired), string.Empty);
            _database.Database.Modified = false;

            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = writer.Revoke(_database.Database, key);

            Assert.That(result.IsUnsaved, Is.True, result.Reason);

            var store = new CustomDataStore(_database.Database);
            Assert.That(store.Get(AuthorityKeys.RevocationKey(standing)), Is.Not.Null.And.Not.Empty,
                "another client's revocation is still there");
            Assert.That(store.Get(AuthorityKeys.RevocationKey(expired)), Is.Not.Null.And.Not.Empty,
                "and the expired one the write pruned is back");
            Assert.That(store.Get(AuthorityKeys.ClientRecordKey(expired)), Is.Empty,
                "with the record it emptied");
            Assert.That(store.Get(AuthorityKeys.RevocationKey(key)), Is.Null);
        }

        [Test]
        public void A_revocation_no_save_kept_puts_back_the_standing_answers_its_prune_took()
        {
            // The last prune a revocation runs takes away every standing answer about a record
            // that is gone or revoked: the revoked client's own, and one beside a record the
            // write found expired. The undo put the records back and not the answers, so the
            // client stayed admitted with its operator's forever gone from memory while the file
            // kept it, and the next save wrote the loss.
            var key = Admitted();
            var expired = PlantExpiredRecord();
            var admissions = new AdmissionStore(new CustomDataStore(_database.Database), null);
            admissions.RefuseRenewal(key, RenewalRefusal.Forever());
            admissions.RefuseRenewal(expired, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(3)));
            _database.Database.Modified = false;

            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = writer.Revoke(_database.Database, key);

            Assert.That(result.IsUnsaved, Is.True, result.Reason);

            var afterwards = new AdmissionStore(new CustomDataStore(_database.Database), null);
            Assert.That(afterwards.Find(key), Is.Not.Null);
            Assert.That(afterwards.StandingRefusal(key), Is.Not.Null.And.Property("IsForever").True,
                "the operator's forever is still in force");
            Assert.That(afterwards.StandingRefusal(expired), Is.Not.Null,
                "and so is the answer beside the expired record the prune took");
            Assert.That(_database.Database.Modified, Is.False, "memory agrees with the file");
        }

        [Test]
        public void A_revocation_the_file_did_not_keep_stays_in_memory_and_the_database_stays_modified()
        {
            // Saved, and the file read back is not the one written. Nothing knows which of the two
            // the file holds, so the revocation is not taken back out: the certificate is refused
            // from here on, and KeePass asks before it discards the copy that holds it.
            var key = Admitted();
            _database.Save();
            var theCopyAsItWas = System.IO.File.ReadAllBytes(_database.Path);

            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    System.IO.File.WriteAllBytes(_database.Path, theCopyAsItWas);
                },
                null,
                _dialogs,
                null);

            var result = writer.Revoke(_database.Database, key);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(new AdmissionStore(new CustomDataStore(_database.Database), null).Find(key), Is.Null,
                "a retry is answered 401, since the certificate is already refused");
            Assert.That(_database.Database.Modified, Is.True);
        }

        [Test]
        public void A_record_gone_before_the_write_changes_nothing()
        {
            var key = Admitted();
            Assume.That(new AdmissionStore(new CustomDataStore(_database.Database), null).Remove(key), Is.True);
            _database.Save();
            var saves = _saves;

            var result = _writer.Revoke(_database.Database, key);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.IsBusy || result.IsFailed || result.IsUnsaved || result.IsUnverified, Is.False,
                "missing, which the route answers as the refusal this certificate gets from now on");
            Assert.That(_saves, Is.EqualTo(saves), "nothing to write, so nothing to save");
        }

        /// <summary>A client admitted in the real database, answering with its admission key.</summary>
        private string Admitted()
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), null).Read())
            using (var certificate = new AuthorityIssuer().IssueClient(
                       reading.Material.Authority, "agent-fictitious", new[] { "ops" }))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                var written = _writer.WriteAdmission(_database.Database, record);
                Assume.That(written.Ok, Is.True, written.Reason);
                _saves = 0;
                return record.AdmissionKey;
            }
        }

        /// <summary>The record of a client certificate that ended days ago, by its admission key.</summary>
        private string PlantExpiredRecord()
        {
            var then = DateTimeOffset.UtcNow.AddDays(-7);
            var issuer = new AuthorityIssuer(() => then);

            using (var authority = issuer.CreateAuthority())
            using (var certificate = issuer.IssueClient(authority, "agent-nonexistent", new[] { "ops" }, TimeSpan.FromDays(1)))
            {
                var record = AdmissionRecord.For(certificate, then, new[] { "ops" });
                _database.Database.CustomData.Set(record.StorageKey, record.Save());
                return record.AdmissionKey;
            }
        }

        private DatabaseOperationsForm MaintenanceDialog()
        {
            var form = new DatabaseOperationsForm();
            form.InitEx(_database.Database);
            return form;
        }

        private void Save(PwDatabase database)
        {
            _saves++;
            database.Save(null);
        }
    }
}
