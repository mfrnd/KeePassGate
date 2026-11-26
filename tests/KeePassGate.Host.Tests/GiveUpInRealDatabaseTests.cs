using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A client giving its own certificate up against a real KeePass database, saved to a real
    /// file and opened again, `STORE.md` §648.
    /// </summary>
    /// <remarks>
    /// What `DELETE /gate/certificate` writes is a record of kind `withdrawn` in the plugin's
    /// store, and what decides whether it happened is what decides it for every change: KeePass's
    /// own save, the modified flag it clears, and the file hashed back afterwards. Each way that
    /// can go wrong leaves the certificate admitted or not, and these tell which.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class GiveUpInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private int _saves;

        // The authority the database served when the fixture made it, which every certificate
        // given up here arrived under.
        private string _authority;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _dialogs.Start();
            _saves = 0;

            _writer = new DatabaseWriter(CurrentThread.Instance, Save, null, _dialogs, null);

            var created = DatabaseAuthority.Of(_database.Database, null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
            _authority = Served();
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_certificate_given_up_leaves_the_record_that_takes_it_away_in_the_file()
        {
            var key = Admitted();

            var result = _writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(_saves, Is.EqualTo(1));

            _database.SaveCloseAndReopen();

            var admissions = Admissions();
            Assert.That(admissions.Holds("withdrawn", key), Is.True,
                "the record that takes the certificate away is in the file, not only in memory");
            Assert.That(admissions.Holds("client", key), Is.False, "and the write deleted the admission it refuses");
            Assert.That(admissions.Find(key), Is.Null);
        }

        [Test]
        public void The_certificate_a_renewal_issued_is_still_admitted_after_the_old_certificate_is_given_up()
        {
            // What the reference client does after every renewal: it gives the old certificate
            // up once the new identity is stored, and the record names the renewal it read.
            using (var old = Issue("agent-fictitious"))
            using (var renewed = Issue("agent-fictitious"))
            {
                var first = AdmissionRecord.For(old, DateTimeOffset.UtcNow, new[] { "ops" }, Served());
                Assume.That(_writer.WriteAdmission(_database.Database, first).Ok, Is.True);
                var renewal = AdmissionRecord.For(
                    renewed, DateTimeOffset.UtcNow, new[] { "ops" }, Served(), first.ClientKey, first.AdmissionKey);
                Assume.That(_writer.WriteAdmission(_database.Database, renewal).Ok, Is.True);

                var result = _writer.GiveUp(_database.Database, first.AdmissionKey, _authority);
                Assert.That(result.Ok, Is.True, result.Reason);

                _database.SaveCloseAndReopen();

                var admissions = Admissions();
                Assert.That(admissions.Find(first.AdmissionKey), Is.Null, "the old certificate is refused");
                Assert.That(admissions.Find(renewal.AdmissionKey), Is.Not.Null, "and the renewed certificate still admitted");
                Assert.That(admissions.Holds("withdrawn", renewal.AdmissionKey), Is.True,
                    "since the record that takes the old certificate away names it");
            }
        }

        [Test]
        public void A_client_that_gives_up_the_certificate_between_and_then_the_one_before_it_keeps_its_newest()
        {
            // The reference client's order where a give-up was answered busy and tried again after
            // the next renewal: the certificate between is given up first, then the certificate
            // before it.
            // The second statement names the certificate between by the first statement, so the
            // newest certificate counts.
            using (var paired = Issue("agent-fictitious"))
            using (var between = Issue("agent-fictitious"))
            using (var newest = Issue("agent-fictitious"))
            {
                var first = AdmissionRecord.For(paired, DateTimeOffset.UtcNow, new[] { "ops" }, Served());
                Assume.That(_writer.WriteAdmission(_database.Database, first).Ok, Is.True);
                var second = AdmissionRecord.For(
                    between, DateTimeOffset.UtcNow, new[] { "ops" }, Served(), first.ClientKey, first.AdmissionKey);
                Assume.That(_writer.WriteAdmission(_database.Database, second).Ok, Is.True);
                var third = AdmissionRecord.For(
                    newest, DateTimeOffset.UtcNow, new[] { "ops" }, Served(), first.ClientKey, second.AdmissionKey);
                Assume.That(_writer.WriteAdmission(_database.Database, third).Ok, Is.True);

                var gaveUpBetween = _writer.GiveUp(_database.Database, second.AdmissionKey, _authority);
                Assert.That(gaveUpBetween.Ok, Is.True, gaveUpBetween.Reason);
                var gaveUpPaired = _writer.GiveUp(_database.Database, first.AdmissionKey, _authority);
                Assert.That(gaveUpPaired.Ok, Is.True, gaveUpPaired.Reason);

                _database.SaveCloseAndReopen();

                var admissions = Admissions();
                Assert.That(admissions.Find(third.AdmissionKey), Is.Not.Null, "the newest certificate counts");
                Assert.That(admissions.Find(second.AdmissionKey), Is.Null);
                Assert.That(admissions.Find(first.AdmissionKey), Is.Null);
            }
        }

        [Test]
        public void A_certificate_given_up_that_lands_is_announced_and_one_that_does_not_is_not()
        {
            // What the Clients page listens to, so that a client leaving while the page is open
            // leaves the list too.
            var announced = 0;
            var landing = new DatabaseWriter(CurrentThread.Instance, Save, null, () => announced++, _dialogs, null);
            var failing = new DatabaseWriter(CurrentThread.Instance, database => { }, null, () => announced++, _dialogs, null);

            Assume.That(failing.GiveUp(_database.Database, Admitted(), _authority).Ok, Is.False);
            Assert.That(announced, Is.Zero, "a certificate given up and taken back out changed nothing a page shows");

            Assert.That(landing.GiveUp(_database.Database, Admitted(), _authority).Ok, Is.True);
            Assert.That(announced, Is.EqualTo(1));
        }

        [Test]
        public void A_certificate_given_up_that_lands_rebuilds_the_tree_and_the_entry_list()
        {
            // It adds an entry to the plugin's group and deletes another, both of which the tree
            // and the entry list show.
            var rebuilt = new List<bool>();
            var writer = new DatabaseWriter(
                CurrentThread.Instance, Save, (database, lists) => rebuilt.Add(lists), _dialogs, null);

            Assert.That(writer.GiveUp(_database.Database, Admitted(), _authority).Ok, Is.True);
            Assert.That(rebuilt, Has.Member(true));
        }

        [Test]
        public void A_page_that_fails_after_the_certificate_given_up_landed_cannot_lose_its_audit_line()
        {
            // The writer writes the line on its own landed path, before it tells anything drawn
            // from the store, so a defect in a listener reaches the connection thread with the
            // record on the disk and written down once.
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

            Assert.Throws<NullReferenceException>(() => writer.GiveUp(_database.Database, key, _authority, () => lines++));

            Assert.That(lines, Is.EqualTo(1));
            Assert.That(linesWhenTold, Is.EqualTo(1), "written before the page was told");

            _database.SaveCloseAndReopen();

            Assert.That(Admissions().Holds("withdrawn", key), Is.True,
                "and the record the line is about is in the file");
        }

        [Test]
        public void A_window_that_fails_to_redraw_after_the_certificate_given_up_landed_cannot_lose_its_audit_line()
        {
            // KeePass's `UpdateUI` raises `UIStateUpdated` to every other plugin loaded, and a
            // defect in a handler there is nothing the writer's filters name.
            var key = Admitted();
            var lines = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                Save,
                (database, lists) => { throw new NullReferenceException("a UIStateUpdated handler's defect"); },
                _dialogs,
                null);

            Assert.Throws<NullReferenceException>(() => writer.GiveUp(_database.Database, key, _authority, () => lines++));

            Assert.That(lines, Is.EqualTo(1));

            _database.SaveCloseAndReopen();

            Assert.That(Admissions().Holds("withdrawn", key), Is.True,
                "and the record the line is about is in the file");
        }

        [Test]
        public void A_save_that_fails_after_writing_the_file_cannot_lose_the_audit_line()
        {
            // `MainForm.SaveDatabase` raises `FileSaved` to every other plugin once the file is
            // written, and a defect there is nothing the writer's filters name.
            var key = Admitted();
            var lines = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    throw new NullReferenceException("a FileSaved handler's defect");
                },
                null,
                _dialogs,
                null);

            Assert.Throws<NullReferenceException>(() => writer.GiveUp(_database.Database, key, _authority, () => lines++));

            Assert.That(lines, Is.EqualTo(1));
            Assert.That(Admissions().Find(key), Is.Null, "the record the line is about is in force");

            _database.CloseAndReopenWithoutSaving();

            Assert.That(Admissions().Holds("withdrawn", key), Is.True, "and in the file");
        }

        [Test]
        public void A_save_that_fails_before_writing_the_file_takes_the_certificate_given_up_back_out()
        {
            // A `FileSavingPre` handler's defect, before anything reached the disk.
            var key = Admitted();
            var lines = 0;
            var before = Admissions().Snapshot();
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new NullReferenceException("a FileSavingPre handler's defect"); },
                null,
                _dialogs,
                null);

            Assert.Throws<NullReferenceException>(() => writer.GiveUp(_database.Database, key, _authority, () => lines++));

            Assert.That(lines, Is.Zero, "a certificate given up and taken back out took nothing away");
            Assert.That(Admissions().Find(key), Is.Not.Null, "the certificate is still admitted");
            Assert.That(Admissions().Snapshot(), Is.EqualTo(before), "and the store is as it was");
            Assert.That(_database.Database.Modified, Is.False, "and the database agrees with the file");
        }

        [Test]
        public void The_audit_line_is_written_once_for_a_certificate_given_up_left_in_force_and_never_otherwise()
        {
            var lines = 0;
            var failing = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);

            Assume.That(failing.GiveUp(_database.Database, Admitted(), _authority, () => lines++).IsUnsaved, Is.True);
            Assert.That(lines, Is.Zero, "a certificate given up and taken back out took nothing away");

            Assume.That(_writer.GiveUp(_database.Database, new string('e', 64), _authority, () => lines++).Ok, Is.False);
            Assert.That(lines, Is.Zero, "nor did a certificate nothing admits");

            Assert.That(_writer.GiveUp(_database.Database, Admitted(), _authority, () => lines++).Ok, Is.True);
            Assert.That(lines, Is.EqualTo(1));
        }

        [Test]
        public void A_certificate_given_up_that_the_file_did_not_keep_is_written_down_since_it_stays_in_force()
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

            var result = writer.GiveUp(_database.Database, key, _authority, () => lines++);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(lines, Is.EqualTo(1));
        }

        [Test]
        public void A_certificate_given_up_that_the_file_did_not_keep_is_announced_since_it_stays_in_force()
        {
            // Saved and not verified, the record is left in place and the certificate is refused
            // from its next request: a page not told would go on listing a client that every
            // request refuses.
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

            var result = writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(announced, Is.EqualTo(1));
        }

        [Test]
        public void A_certificate_given_up_whose_database_closed_before_its_save_wrote_the_file_is_not_written_down()
        {
            // An operator's trigger on Saving database file that closes the database, answered by
            // discarding the changes, or another plugin's handler closing it there: the database
            // closes holding the record, KeePass's save then fails on the closed database and
            // raises `FileSaved` as a failure, and the file still admits the certificate. A line
            // stating it was given up would stand over a certificate the next open admits again.
            var key = Admitted();
            var lines = 0;
            DatabaseWriter writer = null;
            writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    database.Close();
                    writer.AfterSave(database, false);
                },
                null,
                _dialogs,
                null);

            var result = writer.GiveUp(_database.Database, key, _authority, () => lines++);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(lines, Is.Zero, "nothing holds the record the line would be about");

            _database.Reopen();

            Assert.That(Admissions().Find(key), Is.Not.Null, "the file still admits the certificate");
        }

        [Test]
        public void A_certificate_given_up_whose_database_closed_after_its_save_wrote_the_file_is_written_down()
        {
            // A trigger on Saved database file, which KeePass runs once it has raised `FileSaved`
            // for a save that landed.
            var key = Admitted();
            var lines = 0;
            DatabaseWriter writer = null;
            writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    writer.AfterSave(database, true);
                    database.Close();
                },
                null,
                _dialogs,
                null);

            var result = writer.GiveUp(_database.Database, key, _authority, () => lines++);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(lines, Is.EqualTo(1));

            _database.Reopen();

            Assert.That(Admissions().Holds("withdrawn", key), Is.True,
                "and the record the line is about is in the file");
        }

        [Test]
        public void A_certificate_given_up_saved_by_the_close_that_ended_its_save_is_written_down()
        {
            // The trigger on Saving database file again, answered by saving the changes: KeePass's
            // close saves the database itself, which raises `FileSaved` for a save that landed,
            // and the save the writer began then fails on the closed database and raises it as a
            // failure. The first is the save that wrote the record.
            var key = Admitted();
            var lines = 0;
            DatabaseWriter writer = null;
            writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    writer.AfterSave(database, true);
                    database.Close();
                    writer.AfterSave(database, false);
                },
                null,
                _dialogs,
                null);

            var result = writer.GiveUp(_database.Database, key, _authority, () => lines++);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(lines, Is.EqualTo(1));

            _database.Reopen();

            Assert.That(Admissions().Holds("withdrawn", key), Is.True,
                "and the record the line is about is in the file");
        }

        [Test]
        public void The_maintenance_dialog_open_on_the_database_refuses_the_certificate_given_up()
        {
            // `STORE.md` §626: that dialog's buttons delete history items and deletion records,
            // and a press of one inside the writer's own save was never measured.
            var key = Admitted();

            using (var form = MaintenanceDialog())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var result = _writer.GiveUp(_database.Database, key, _authority);

                    Assert.That(result.IsBusy, Is.True, result.Reason);
                    Assert.That(_saves, Is.Zero);
                    Assert.That(Admissions().Find(key), Is.Not.Null,
                        "nothing was written, so the certificate is still admitted and may ask again");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_certificate_given_up_that_no_save_kept_is_taken_back_out_and_stays_admitted()
        {
            var key = Admitted();
            _database.Database.Modified = false;
            var before = Admissions().Snapshot();

            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.IsUnsaved, Is.True, result.Reason);

            var admissions = Admissions();
            Assert.That(admissions.Find(key), Is.Not.Null, "still admitted, so asking again is safe");
            Assert.That(admissions.Holds("withdrawn", key), Is.False, "and no record takes it away");
            Assert.That(admissions.Snapshot(), Is.EqualTo(before), "the store is as it was, so memory agrees with the file");
            Assert.That(_database.Database.Modified, Is.False);
        }

        [Test]
        public void A_certificate_given_up_that_no_save_kept_puts_back_what_its_write_deleted()
        {
            // What is taken back out is everything the write changed and nothing it did not: the
            // admission of a revoked client that came back beside its revocation, which the write
            // deleted with a deletion record, is put back as it was, and the revocation stays.
            var key = Admitted();
            string cameBack;
            using (var other = Issue("agent-other-fictitious"))
            {
                var record = AdmissionRecord.For(other, DateTimeOffset.UtcNow, new[] { "ops" }, Served());
                Admissions().BackBesideItsRevocation(record);
                cameBack = record.AdmissionKey;
            }

            _database.Database.Modified = false;
            var before = Admissions().Snapshot();

            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.IsUnsaved, Is.True, result.Reason);

            var admissions = Admissions();
            Assert.That(admissions.Holds("client", cameBack), Is.True, "the admission the write deleted is back");
            Assert.That(admissions.Find(cameBack), Is.Null, "and still refused by the revocation beside it");
            Assert.That(admissions.Snapshot(), Is.EqualTo(before), "with every deletion record as it was");
        }

        [Test]
        public void A_certificate_given_up_leaves_the_standing_answer_of_its_client_standing()
        {
            // `STORE.md` §648: a certificate taken away leaves the client's answer untouched, and
            // nothing is removed for a certificate no admission names, `STORE.md` §618. The answer
            // is about the client, and goes with no certificate of it.
            var key = Admitted();
            Admissions().Refuse(key, RenewalRefusal.Forever());
            Assume.That(Admissions().Answer(key).IsForever, Is.True);
            _database.Database.Modified = false;

            var result = _writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.Ok, Is.True, result.Reason);
            var afterwards = Admissions();
            Assert.That(afterwards.Find(key), Is.Null, "the certificate is given up");
            Assert.That(afterwards.Answer(key), Is.Not.Null.And.Property("IsForever").True,
                "and the client's answer still refuses its renewals");
        }

        [Test]
        public void A_certificate_given_up_that_the_file_did_not_keep_stays_in_memory_and_the_database_stays_modified()
        {
            // Saved, and the file read back is not the one written. Nothing knows which of the two
            // the file holds, so the record is not taken back out: the certificate is refused from
            // here on, and KeePass asks before it discards the copy that holds it.
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

            var result = writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.IsUnverified, Is.True, result.Reason);
            Assert.That(Admissions().Find(key), Is.Null,
                "a retry is answered 401, since the certificate is already refused");
            Assert.That(_database.Database.Modified, Is.True);
        }

        [Test]
        public void A_certificate_no_longer_admitted_before_the_write_changes_nothing()
        {
            var key = Admitted();
            Assume.That(Admissions().Remove(key), Is.True);
            _database.Save();
            var saves = _saves;

            var result = _writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.IsBusy || result.IsFailed || result.IsUnsaved || result.IsUnverified, Is.False,
                "missing, which the route answers as the refusal this certificate gets from now on");
            Assert.That(_saves, Is.EqualTo(saves), "nothing to write, so nothing to save");
        }

        [TestCase("stopped")]
        [TestCase("split")]
        [TestCase("outdated")]
        [TestCase("lost")]
        public void A_certificate_given_up_over_a_store_that_holds_the_gate_back_is_answered_its_word_with_nothing_written(
            string word)
        {
            // The store read in the write's own turn, `STORE.md` §627, and answered as a connection
            // to the database is answered, `STORE.md` §656: `lost` for a file whose key went, and
            // the store's own word otherwise.
            AnsweredWithNothingWritten(word);
        }

        [Test]
        public void A_certificate_given_up_while_several_authorities_stand_is_answered_undecided_with_nothing_written()
        {
            // A second authority standing beside the first, which serves nothing until the
            // operator keeps an authority, as a connection to the database is answered.
            AnsweredWithNothingWritten(HeldBackTarget.Undecided);
        }

        [Test]
        public void A_certificate_given_up_once_its_authority_was_retired_beside_several_others_is_answered_missing_with_nothing_written()
        {
            // Beside the case of a second authority arriving: the authority the connection arrived
            // under is retired and two others stand, which no decision of the operator's can make
            // serve this certificate, so it is answered as a certificate no longer admitted.
            var key = Admitted();
            Admissions().RetiredBesideOthers();
            var before = Admissions().Snapshot();

            var result = _writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Word, Is.Null, result.Reason);
            Assert.That(result.IsBusy || result.IsFailed || result.IsUnsaved || result.IsUnverified, Is.False,
                "missing, which the route answers 401 unpaired");
            Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
            Assert.That(Admissions().Snapshot(), Is.EqualTo(before));
        }

        /// <summary>
        /// A certificate given up once the database is brought to be answered with this word, and
        /// what came of it.
        /// </summary>
        private void AnsweredWithNothingWritten(string word)
        {
            var key = Admitted();
            Admissions().HoldBack(word);
            var before = Admissions().Snapshot();

            var result = _writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Word, Is.EqualTo(word), result.Reason);
            Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
            Assert.That(Admissions().Snapshot(), Is.EqualTo(before));
        }

        [Test]
        public void A_certificate_given_up_that_the_store_has_no_room_for_is_answered_stopped_with_nothing_written()
        {
            // Taking a certificate away writes a statement for each origin its admissions hold,
            // and an admission a revocation refuses still holds an origin until a write deletes
            // it. A store with room for a further item and not for two refuses that write whole,
            // `STORE.md` §643, and the client is answered as a store with no room for its next
            // write is answered, `STORE.md` §656.
            string key;
            using (var certificate = Issue("agent-fictitious"))
            {
                var first = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }, Served());
                var written = _writer.WriteAdmission(_database.Database, first);
                Assume.That(written.Ok, Is.True, written.Reason);
                key = first.AdmissionKey;

                // A second admission of the certificate, under a client key a revocation names
                // and renewed from a certificate of its own, back beside its revocation as a merge
                // from a copy that touched it later leaves it.
                var second = AdmissionRecord.For(
                    certificate, DateTimeOffset.UtcNow, new[] { "ops" }, Served(), new string('e', 64), new string('f', 64));
                StoreSeeds.Client(Admissions().Store, second.Members());
                var entry = Admissions().Entries(StoreKinds.Client)
                    .Single(found => found.Strings.ReadSafe("KPG.Data").Contains(second.ClientKey));
                var group = entry.ParentGroup;
                var copy = entry.CloneDeep();
                StoreSeeds.Revoke(Admissions().Store, second.ClientKey);
                Assume.That(Admissions().Holds(StoreKinds.Client, second.ClientKey), Is.False, "the revocation deleted it");
                group.AddEntry(copy, true);
                _database.Database.DeletedObjects.Remove(
                    _database.Database.DeletedObjects.Single(deleted => deleted.Uuid.Equals(copy.Uuid)));
            }

            Admissions().FillTo(StoreFormat.MaxItems - 1);
            using (var reading = Admissions().Store.Read())
            {
                Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            }

            Assume.That(Admissions().Find(key), Is.Not.Null, "admitted by its first admission alone");
            var before = Admissions().Snapshot();
            _saves = 0;

            var result = _writer.GiveUp(_database.Database, key, _authority);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Word, Is.EqualTo(StoreWords.Stopped), result.Reason);
            Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
            Assert.That(Admissions().Snapshot(), Is.EqualTo(before));
        }

        /// <summary>
        /// A client admitted in the real database by a pairing, answering with its admission key.
        /// </summary>
        private string Admitted()
        {
            using (var certificate = Issue("agent-fictitious"))
            {
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }, Served());
                var written = _writer.WriteAdmission(_database.Database, record);
                Assume.That(written.Ok, Is.True, written.Reason);
                _saves = 0;
                return record.AdmissionKey;
            }
        }

        private X509Certificate2 Issue(string name)
        {
            using (var reading = DatabaseAuthority.Of(_database.Database, null).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, new[] { "ops" }).Read();
            }
        }

        private string Served()
        {
            return StoreSeeds.Served(Admissions().Store);
        }

        private StoredAdmissions Admissions()
        {
            return new StoredAdmissions(_database.Database);
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
