using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Pairing against a real KeePass database, saved to a real file and opened again.
    /// </summary>
    /// <remarks>
    /// The loopback tests prove the protocol. This proves the half of phase 5 that only a real
    /// database can answer: that the record reaches the file before the certificate is handed over,
    /// that a dialog holding a snapshot of the records stops it, and that KeePass's own save is
    /// what "written" is made of.
    ///
    /// The one thing it cannot cover is `MainForm.SaveDatabase`, because there is no main window
    /// here. The end to end tier pairs against a real KeePass and finds the record in the file
    /// afterwards, which is where that is settled.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class PairingInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private RecordingLog _log;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _dialogs.Start();
            _log = new RecordingLog();
            _saves = 0;

            _writer = new DatabaseWriter(CurrentThread.Instance, Save, null, _dialogs, _log);

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
        public void A_list_of_profiles_that_will_not_read_is_never_written_over()
        {
            // Read, a damaged list names nothing, and a list of only the approved names written in
            // its place drops every name it held and the sign that it was damaged.
            const string Damaged = "{ not a list";
            _database.Database.CustomData.Set(ProfileNames.CustomDataKey, Damaged);

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(_database.Database.CustomData.Get(ProfileNames.CustomDataKey), Is.EqualTo(Damaged));
                Assert.That(_log.Warnings.Any(line => line.Contains("will not read")), Is.True);
            }
        }

        [Test]
        public void A_record_is_written_and_the_database_is_saved()
        {
            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(_saves, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_record_that_lands_is_announced_once_it_is_on_the_disk()
        {
            // What the clients page listens to. The moment worth announcing is this one and not
            // the write before it: a record put in the database and not saved is taken back out
            // again, and a page told about that would list a client the next request refuses.
            var announced = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance, Save, null, () => announced++, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(announced, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_write_that_never_landed_is_not_announced()
        {
            // A save that does not reach the disk is undone, and the record is gone with it. A
            // page told about that would put a client on the list that is in no database.
            var announced = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                delegate { throw new System.IO.IOException("the disk said no"); },
                null,
                () => announced++,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assume.That(result.Ok, Is.False, "this write was supposed to fail");
                Assert.That(announced, Is.EqualTo(0));
            }
        }

        [Test]
        public void A_page_that_fails_after_the_record_landed_cannot_lose_its_audit_line()
        {
            // The writer writes the line on its own landed path, before it tells anything drawn
            // from the records, so a defect in a listener reaches the connection thread with the
            // record on the disk and written down once.
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
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");

                Assert.Throws<NullReferenceException>(
                    () => writer.WriteAdmission(_database.Database, record, () => lines++));

                Assert.That(lines, Is.EqualTo(1));
                Assert.That(linesWhenTold, Is.EqualTo(1), "written before the page was told");

                _database.SaveCloseAndReopen();

                Assert.That(
                    new AdmissionStore(new CustomDataStore(_database.Database), null).Find(record.AdmissionKey),
                    Is.Not.Null,
                    "and the record the line is about is in the file");
            }
        }

        [Test]
        public void The_audit_line_is_written_once_for_a_record_that_landed_and_never_otherwise()
        {
            var lines = 0;
            var failing = new DatabaseWriter(
                CurrentThread.Instance,
                delegate { throw new System.IO.IOException("the disk said no"); },
                null,
                _dialogs,
                _log);

            using (var refused = IssueClient())
            using (var admitted = IssueClient())
            {
                Assume.That(failing.WriteAdmission(_database.Database, Record(refused, "ops"), () => lines++).Ok,
                    Is.False);
                Assert.That(lines, Is.Zero, "a record taken back out admitted nobody");

                Assert.That(_writer.WriteAdmission(_database.Database, Record(admitted, "ops"), () => lines++).Ok,
                    Is.True);
                Assert.That(lines, Is.EqualTo(1));
            }
        }

        [Test]
        public void KeePass_saying_it_has_no_unsaved_changes_is_what_written_is_made_of()
        {
            // Measured rather than assumed. KeePass's save tells its caller nothing, so the writer
            // reads this instead, and it would be reading a lie if this ever stopped being true.
            using (var certificate = IssueClient())
            {
                _database.Database.Modified = true;
                _database.Save();

                Assert.That(_database.Database.Modified, Is.False,
                    "a saved database that still says it is modified would break the pairing readback");
                Assert.That(certificate, Is.Not.Null);
            }
        }

        [Test]
        public void The_record_is_in_the_file_and_not_only_in_memory()
        {
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                Assume.That(_writer.WriteAdmission(_database.Database, record).Ok, Is.True);

                _database.SaveCloseAndReopen();

                var found = new AdmissionStore(new CustomDataStore(_database.Database), null)
                    .Find(record.AdmissionKey);

                Assert.That(found, Is.Not.Null, "a record that is not in the file admits nobody tomorrow");
                Assert.That(found.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void The_profiles_the_record_carries_are_named_in_the_same_write()
        {
            // A profile the operator approved is created by their approval, and it has to exist by
            // the time anybody opens the tab to write a rule for it. It is done here rather than
            // beside the approval because it is the same decision as the record: the same thread,
            // the same lock, the same save, and refused by the same open dialog.
            using (var certificate = IssueClient())
            {
                Assume.That(_writer.WriteAdmission(_database.Database, Record(certificate, "invented")).Ok, Is.True);

                var names = ProfileNames.Read(
                    _database.Database.CustomData.Get(ProfileNames.CustomDataKey));

                Assert.That(names.Holds("invented"), Is.True,
                    "a profile in a record and in no list is a name no rule can be written against");

                _database.SaveCloseAndReopen();

                var kept = ProfileNames.Read(
                    _database.Database.CustomData.Get(ProfileNames.CustomDataKey));

                Assert.That(kept.Holds("invented"), Is.True, "and the save that keeps the record keeps it");
            }
        }

        [Test]
        public void A_profile_past_what_the_list_holds_is_left_off_it_and_said_so()
        {
            // An operator can approve a name the database has never held while it already names as
            // many as the list takes. The pairing stands, since refusing it would leave a client
            // holding a certificate for a record nobody wrote. What it costs is that the name
            // grants nothing, a client reaching only what the list names, and the log says so.
            var names = ProfileNames.Empty();
            for (var i = 0; i < ProfileNames.Most; i++)
            {
                Assume.That(names.Add("invented-" + i), Is.Null);
            }

            _database.Database.CustomData.Set(ProfileNames.CustomDataKey, names.Write());

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "one-too-many"));

                Assert.That(result.Ok, Is.True, result.Reason);

                var kept = ProfileNames.Read(_database.Database.CustomData.Get(ProfileNames.CustomDataKey));
                Assert.That(kept.Holds("one-too-many"), Is.False);
                Assert.That(kept.Names.Count, Is.EqualTo(ProfileNames.Most));
                Assert.That(_log.Warnings, Has.Some.Contains("reaches nothing through it"));
                Assert.That(_log.Warnings, Has.None.Contains("one-too-many"),
                    "and it says so without naming what the client may reach");
            }
        }

        [Test]
        public void A_refused_write_names_nothing()
        {
            // The half that made this worth moving. Written anywhere but inside the marshaled
            // write, a name could be added while the record was refused, leaving a database naming
            // a profile for a client it never admitted.
            using (var certificate = IssueClient())
            using (var form = MaintenanceDialog())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assume.That(_writer.WriteAdmission(_database.Database, Record(certificate, "invented")).Ok,
                        Is.False);

                    var names = ProfileNames.Read(
                        _database.Database.CustomData.Get(ProfileNames.CustomDataKey));

                    Assert.That(names.Holds("invented"), Is.False);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void The_maintenance_dialog_open_on_the_database_refuses_the_write()
        {
            using (var certificate = IssueClient())
            using (var form = MaintenanceDialog())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var record = Record(certificate, "ops");

                    Assert.That(_writer.Blocked(_database.Database), Is.Not.Null);

                    var result = _writer.WriteAdmission(_database.Database, record);

                    Assert.That(result.Ok, Is.False);
                    Assert.That(result.IsBusy, Is.True, "it is worth trying again once that dialog is gone");
                    Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                    Assert.That(_database.Database.CustomData.Exists(record.StorageKey), Is.False);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_pairing_takes_away_the_documents_of_retired_authorities()
        {
            // The prune rides on a write this plugin makes anyway, and this is one.
            var retired = PlantRetiredDocument();

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(retired)), Is.False);
                Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RetirementKey(retired)), Is.True,
                    "the retirement is never removed");
            }
        }

        [Test]
        public void A_pairing_that_does_not_land_puts_what_it_pruned_back()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);
            var retired = PlantRetiredDocument();
            _database.Database.Modified = false;

            using (var certificate = IssueClient())
            {
                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.False);

                Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(retired)), Is.True,
                    "memory has to go on agreeing with the file the write never reached");
                Assert.That(_database.Database.Modified, Is.False);
            }
        }

        [Test]
        public void A_pairing_takes_away_a_record_that_came_back_beside_its_revocation()
        {
            // Reading the database, which is all the gate does when that client connects, changes
            // nothing in it; the next write this plugin makes anyway takes the record with it.
            var cameBack = PlantCameBackRecord();
            _database.Database.Modified = false;

            var store = new AdmissionStore(new CustomDataStore(_database.Database), null);
            Assume.That(store.Find(cameBack), Is.Null, "the revocation beside it refuses it meanwhile");
            Assume.That(store.All().CameBack, Is.EqualTo(1));
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.ClientRecordKey(cameBack)), Is.True);
            Assert.That(_database.Database.Modified, Is.False, "reading it marks nothing modified");

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));
                Assert.That(result.Ok, Is.True, result.Reason);

                _database.SaveCloseAndReopen();

                Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.ClientRecordKey(cameBack)), Is.False,
                    "gone from the file, not only from memory");
                Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RevocationKey(cameBack)), Is.True,
                    "and the revocation stays, for a stale copy that brings it back again");
            }
        }

        [Test]
        public void A_pairing_that_does_not_land_puts_a_record_that_came_back_back_as_well()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);
            var cameBack = PlantCameBackRecord();
            _database.Database.Modified = false;

            using (var certificate = IssueClient())
            {
                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.False);

                Assert.That(_database.Database.CustomData.Get(AuthorityKeys.ClientRecordKey(cameBack)),
                    Is.EqualTo("a record that came back"),
                    "memory has to go on agreeing with the file the write never reached");
                Assert.That(_database.Database.Modified, Is.False);
            }
        }

        [Test]
        public void A_pairing_takes_away_the_record_of_an_expired_certificate()
        {
            var expired = PlantExpiredRecord();
            _database.Database.Modified = false;

            var store = new AdmissionStore(new CustomDataStore(_database.Database), null);
            Assume.That(store.Find(expired), Is.Not.Null, "the record is there, and its dates are what refuse it");
            Assert.That(_database.Database.Modified, Is.False, "reading it marks nothing modified");

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));
                Assert.That(result.Ok, Is.True, result.Reason);

                _database.SaveCloseAndReopen();

                Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.ClientRecordKey(expired)), Is.False,
                    "gone from the file, not only from memory");
                Assert.That(_database.Database.CustomData.Exists(Record(certificate, "ops").StorageKey), Is.True,
                    "and the record the pairing wrote is the one left");
            }
        }

        [Test]
        public void A_pairing_that_does_not_land_puts_the_record_of_an_expired_certificate_back()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);
            var expired = PlantExpiredRecord();
            var stored = _database.Database.CustomData.Get(AuthorityKeys.ClientRecordKey(expired));
            _database.Database.Modified = false;

            using (var certificate = IssueClient())
            {
                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.False);

                Assert.That(_database.Database.CustomData.Get(AuthorityKeys.ClientRecordKey(expired)), Is.EqualTo(stored),
                    "memory has to go on agreeing with the file the write never reached");
                Assert.That(_database.Database.Modified, Is.False);
            }
        }

        [Test]
        public void The_maintenance_dialog_open_on_the_database_refuses_the_prune_with_the_write()
        {
            // Database Maintenance writes its copy back when the key count differs, so a prune
            // made under it comes undone, and so does a retirement written beside it.
            var retired = PlantRetiredDocument();

            using (var certificate = IssueClient())
            using (var form = MaintenanceDialog())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assume.That(_writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.False);

                    Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(retired)), Is.True);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_group_dialog_open_on_the_root_group_refuses_nothing()
        {
            // A record touches no group, so there is nothing of this write for a group dialog's OK
            // to put back. The root group was asked about while the authority lived on it.
            using (var certificate = IssueClient())
            using (var form = GroupDialogOnTheRoot())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var record = Record(certificate, "ops");

                    Assert.That(_writer.Blocked(_database.Database), Is.Null);
                    Assert.That(_writer.WriteAdmission(_database.Database, record).Ok, Is.True);
                    Assert.That(_database.Database.CustomData.Exists(record.StorageKey), Is.True);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_record_does_not_redraw_a_window_that_never_showed_it()
        {
            // A database write rebuilds KeePass's group tree and entry list, because it changes
            // what they are showing. A record does not: it lives in the database's own custom data,
            // which the main window has no view of, and the save has already refreshed the title
            // bar. Rebuilding anyway would take an operator's entry list away from them because
            // somebody paired.
            var redraws = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance, Save, (database, lists) => redraws++, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(redraws, Is.Zero);
            }
        }

        [Test]
        public void A_record_taken_back_out_redraws_the_title_bar_alone()
        {
            // A save that failed ended in `UpdateUIState` with the record still in, so the title
            // bar says the database has something to save; the record then goes back out and the
            // flag with it. What is left wrong on screen is that mark, and nothing else.
            _database.Database.Modified = false;

            var redraws = 0;
            var rebuilt = true;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { },
                (database, lists) =>
                {
                    redraws++;
                    rebuilt = lists;
                },
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assume.That(result.Ok, Is.False);
                Assume.That(_database.Database.Modified, Is.False);
                Assert.That(redraws, Is.EqualTo(1));
                Assert.That(rebuilt, Is.False);
            }
        }

        [Test]
        public void A_record_the_file_did_not_keep_refuses_the_pairing()
        {
            // The read-back above this one is of the store, which is memory. KeePass clears
            // `Modified` as soon as it has committed the bytes and only then hashes the file back,
            // and when that disagrees it warns the operator and changes nothing. A record that may
            // not be on the disk is the one thing this class exists to prevent: the certificate
            // would go out and the record would not be there for anybody to see or to revoke.
            using (var certificate = IssueClient())
            {
                // After the certificate, so that nothing issuing did is what this is measuring:
                // the file is written and the database says it has nothing unsaved.
                _database.Save();
                var theCopyAsItWas = System.IO.File.ReadAllBytes(_database.Path);

                var writer = new DatabaseWriter(
                    CurrentThread.Instance,
                    database =>
                    {
                        Save(database);

                        // A sync client putting its own copy back between the write and the
                        // verification. Failing storage has the same shape.
                        System.IO.File.WriteAllBytes(_database.Path, theCopyAsItWas);
                    },
                    null,
                    _dialogs,
                    _log);

                var record = Record(certificate, "ops");
                var result = writer.WriteAdmission(_database.Database, record);

                Assert.That(result.Ok, Is.False, "or a certificate goes out for a record nobody can see");
                Assert.That(new CustomDataStore(_database.Database).Get(record.StorageKey), Is.Null,
                    "and the record is taken back out of the database in memory as well");
                Assert.That(_database.Database.Modified, Is.True,
                    "which leaves KeePass asking before it discards the copy that holds the truth");
            }
        }

        [Test]
        public void A_save_that_wrote_the_record_and_then_threw_still_admits_the_client()
        {
            // `MainForm.SaveDatabase` raises `FileSaved` to every other plugin after the file has
            // been written and outside the try that wraps the save, so an exception from there
            // arrives here over a record that is already on the disk. Undoing it would refuse the
            // client its certificate while the file keeps the record: a client on the clients page
            // that never existed, admitted by a certificate nobody holds, which is the outcome
            // `Undo` exists to prevent, arriving by the other road.
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    Save(database);
                    throw new System.IO.IOException("a FileSaved handler failed after the write");
                },
                null,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var result = writer.WriteAdmission(_database.Database, record);

                Assert.That(result.Ok, Is.True, result.Reason);

                _database.CloseAndReopenWithoutSaving();
                Assert.That(new CustomDataStore(_database.Database).Get(record.StorageKey), Is.Not.Null,
                    "and the record the client was admitted by is the one on the disk");
            }
        }

        [Test]
        public void A_save_that_does_not_happen_is_reported_rather_than_believed()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Reason, Does.Contain("unsaved changes"));
            }
        }

        [Test]
        public void A_save_that_does_not_happen_leaves_no_record_behind_in_the_database()
        {
            // **The refusal has to be true of the database as well as of the answer.** The record
            // is put into the group before the save is attempted, so a save that does not reach the
            // disk leaves it sitting in memory: the client is told the pairing failed and its
            // certificate is destroyed unsent, and then whatever the operator saves next writes an
            // admission record for a certificate nobody was ever issued: a client on the tab that
            // cannot be accounted for and was never handed anything.
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var store = new CustomDataStore(_database.Database);

                // From a database with nothing outstanding, so that anything left over afterwards
                // is this write's doing and nobody else's.
                _database.Database.Modified = false;

                Assume.That(writer.WriteAdmission(_database.Database, record).Ok, Is.False);

                Assert.That(store.Get(record.StorageKey), Is.Null,
                    "a record that did not land must not be left in the group");
                Assert.That(_database.Database.Modified, Is.False,
                    "and the database must not be left asking to be saved for it");
            }
        }

        [Test]
        public void A_save_that_does_not_happen_leaves_no_profile_name_behind_either()
        {
            // The other half of the same write, and the half that outlives it. `Name` writes the
            // profile list *before* the record, so a pairing that failed after that point left a
            // name the operator never typed in a database KeePass had been told has nothing to
            // save. The next unrelated edit commits it. What is then on the profiles page is
            // a name no record holds and nobody can account for, and removing it walks every rule
            // in the database for a profile that never granted anything.
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var store = new CustomDataStore(_database.Database);
                _database.Database.Modified = false;

                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "invented")).Ok,
                    Is.False);

                Assert.That(ProfileNames.Read(store.Get(ProfileNames.CustomDataKey)).Names,
                    Does.Not.Contain("invented"),
                    "a name written for a pairing that did not land must not be left in the group");
                Assert.That(_database.Database.Modified, Is.False,
                    "and the database must not be left asking to be saved for it");
            }
        }

        [Test]
        public void A_save_that_throws_leaves_no_record_behind_either()
        {
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new InvalidOperationException("the disk went away"); },
                null,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var store = new CustomDataStore(_database.Database);

                Assume.That(writer.WriteAdmission(_database.Database, record).Ok, Is.False);

                Assert.That(store.Get(record.StorageKey), Is.Null);
            }
        }

        [Test]
        public void A_save_that_throws_is_reported_rather_than_thrown()
        {
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new InvalidOperationException("the disk went away"); },
                null,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Reason, Does.Contain("disk went away"));
                Assert.That(_log.Errors, Is.Not.Empty);
            }
        }

        [Test]
        public void A_handler_refused_its_file_before_the_save_leaves_no_record_behind()
        {
            // Another plugin's `FileSavingPre` handler refused access to a path of its own, which
            // `MainForm.SaveDatabase` does not catch. Read by its type, that escaped with the
            // record still in the database for the next save to make permanent: a client on the
            // clients page that was never handed its certificate.
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new UnauthorizedAccessException("another plugin was refused its own folder"); },
                null,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var store = new CustomDataStore(_database.Database);

                var result = writer.WriteAdmission(_database.Database, record);

                Assert.That(result.Ok, Is.False);
                Assert.That(store.Get(record.StorageKey), Is.Null);
            }
        }

        [Test]
        public void A_user_interface_that_never_answers_fails_the_pairing_rather_than_holding_it()
        {
            var writer = new DatabaseWriter(new NeverRuns(), Save, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.IsBusy, Is.True);
            }
        }

        [Test]
        public void There_is_nothing_to_write_to_when_the_database_is_closed()
        {
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                _database.Database.Close();

                Assert.That(_writer.Blocked(_database.Database), Is.Not.Null);
                Assert.That(_writer.WriteAdmission(_database.Database, record).Ok, Is.False);
            }
        }

        [Test]
        public void A_database_that_closed_while_the_record_waited_is_not_called_busy()
        {
            // The same database closing on KeePass's thread ahead of the marshaled write. The
            // pairing is abandoned either way, and what the log says it might have been worth is
            // the difference: a database that has gone is not one that would have worked a moment
            // later.
            var writer = new DatabaseWriter(new ClosesFirst(_database.Database), Save, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.IsBusy, Is.False, result.Reason);
                Assert.That(result.Reason, Does.Contain("closed"));
                Assert.That(_saves, Is.Zero);
            }
        }

        [Test]
        public void Two_writers_at_once_take_their_turn_rather_than_interleaving()
        {
            // The single writer, which is the whole of the concurrency story on this path.
            using (var one = IssueClient())
            using (var two = IssueClient())
            {
                var inside = 0;
                var overlapped = false;

                var writer = new DatabaseWriter(
                    CurrentThread.Instance,
                    database =>
                    {
                        if (Interlocked.Increment(ref inside) > 1)
                        {
                            overlapped = true;
                        }

                        Thread.Sleep(50);
                        Interlocked.Decrement(ref inside);
                        Save(database);
                    },
                    null,
                    _dialogs,
                    _log);

                var first = new Thread(() => writer.WriteAdmission(_database.Database, Record(one, "ops")));
                var second = new Thread(() => writer.WriteAdmission(_database.Database, Record(two, "ops")));

                first.Start();
                second.Start();
                first.Join();
                second.Join();

                Assert.That(overlapped, Is.False, "two pairings wrote at the same time");
            }
        }

        [Test]
        public void A_client_pairs_against_a_real_database_and_the_record_survives_the_file()
        {
            // The whole of step 8 against a real database: an invitation, both proofs, an operator
            // who agrees, and a certificate that the reopened file still admits.
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" });

                Assert.That(context.Status, Is.EqualTo(200), context.Json);

                using (var issued = IdentityIn(context))
                {
                    _database.SaveCloseAndReopen();

                    using (var afterwards = Targets())
                    {
                        var reopened = afterwards.Current().Single();
                        var identity = ClientCredentials.For(issued, reopened, _log);

                        Assert.That(identity.IsPaired, Is.True,
                            "the certificate handed over is not admitted by the file it was recorded in");
                        Assert.That(identity.Profiles, Is.EqualTo(new[] { "ops" }));
                    }
                }
            }
        }

        [Test]
        public void The_dialog_is_built_from_the_request_the_handler_made_out_of_the_database()
        {
            // Every other dialog test builds its request by hand, so what the real target answers
            // for the Regaining line, `MaterialTarget.Holding`, was pinned by nothing: a Holding
            // returning nobody, everybody or the revoked would have changed or silenced the
            // warning with every tier green.
            using (var kept = Issue("agent-kept", "ops"))
            using (var revoked = Issue("agent-revoked", "ops"))
            using (var elsewhere = Issue("agent-elsewhere", "audit"))
            {
                Assume.That(_writer.WriteAdmission(_database.Database, Record(kept, "ops")).Ok, Is.True);
                Assume.That(_writer.WriteAdmission(_database.Database, Record(revoked, "ops")).Ok, Is.True);
                Assume.That(_writer.WriteAdmission(_database.Database, Record(elsewhere, "audit")).Ok, Is.True);

                var admissions = new AdmissionStore(new CustomDataStore(_database.Database), null);
                Assume.That(admissions.Remove(CertificateDigest.Of(revoked)), Is.True);

                // And a holder whose certificate has expired, which regains nothing.
                using (var key = ECDsa.Create())
                {
                    var request = new CertificateRequest("CN=agent-expired", key, HashAlgorithmName.SHA256);
                    using (var ended = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1)))
                    {
                        admissions.Write(AdmissionRecord.For(ended, DateTimeOffset.UtcNow.AddDays(-30), new[] { "ops" }));
                    }
                }

                // "ops" taken off the list, as the Profiles page does, so that its holders would
                // regain it; "audit" left on it.
                var names = ProfileNames.Read(null);
                Assume.That(names.Add("audit"), Is.Null);
                _database.Database.CustomData.Set(ProfileNames.CustomDataKey, names.Write());

                using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
                using (var targets = Targets())
                {
                    var target = targets.Current().Single();
                    var invitation = book.Invite(target.DatabaseId);

                    PairingRequest asked = null;
                    string shown = null;
                    var operatorAt = new LookingOperator(request =>
                    {
                        asked = request;
                        using (var dialog = new PairingApprovalForm(request, TimeSpan.FromMinutes(2)))
                        {
                            shown = Texts(dialog);
                        }
                    });

                    var context = Pair(book, target, invitation, "agent-new", new[] { "ops", "audit", "fresh" }, operatorAt);

                    Assume.That(context.Status, Is.EqualTo(403), context.Json);
                    Assert.That(asked, Is.Not.Null);

                    Assert.That(asked.Returning("ops").Select(r => r.Subject), Is.EqualTo(new[] { "CN=agent-kept" }),
                        "the live holder of a removed name, and neither the revoked one nor the expired one");
                    Assert.That(asked.Returning("audit"), Is.Empty, "a listed name gives nothing back");
                    Assert.That(asked.IsNew("fresh"), Is.True);
                    Assert.That(asked.IsNew("ops"), Is.False, "held, so removed rather than new");
                    Assert.That(asked.IsNew("audit"), Is.False);

                    Assert.That(shown, Does.Contain(PairingApprovalForm.Regaining(asked)));
                    Assert.That(shown, Does.Contain("agent-kept"));
                    Assert.That(shown, Does.Not.Contain("agent-revoked"));
                    Assert.That(shown, Does.Not.Contain("agent-expired"));
                    Assert.That(shown, Does.Contain(PairingApprovalForm.NewProfilesAsked));
                }
            }
        }

        [Test]
        public void The_maintenance_dialog_refuses_the_pairing_and_keeps_the_invitation()
        {
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            using (var form = MaintenanceDialog())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);

                GlobalWindowManager.AddWindow(form);

                try
                {
                    var context = Pair(book, target, invitation, "agent-fictitious", Array.Empty<string>());

                    Assert.That(context.Status, Is.EqualTo(503));
                    Assert.That(book.Outstanding().Count, Is.EqualTo(1),
                        "an open dialog costs an attempt, never the invitation");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        private OpenDatabaseTargets Targets()
        {
            var databases = new List<PwDatabase> { _database.Database };
            return new OpenDatabaseTargets(delegate { return databases; }, _writer, _log);
        }

        /// <summary>The whole exchange, at the level of the handler, against a real target.</summary>
        private GateContext Pair(
            IInvitations book, IGateTarget target, Invitation invitation, string name, string[] profiles)
        {
            return Pair(book, target, invitation, name, profiles, new AgreeingOperator());
        }

        /// <summary>The same, put to the operator given.</summary>
        private GateContext Pair(
            IInvitations book,
            IGateTarget target,
            Invitation invitation,
            string name,
            string[] profiles,
            IPairingApproval operatorAt)
        {
            var token = invitation.Token;
            var binding = PairingProof.Binding(target.Host.CertificateDer);
            var nonce = new byte[SniName.NonceLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            var connection = new PairingConnection(invitation, target, binding, nonce, "127.0.0.1:60001");
            var handler = new PairingHandler(book, operatorAt, null, _log);

            var hello = Post(
                PairingHandler.HelloPath,
                Bodies.Of(("proof", Convert.ToBase64String(PairingProof.Client(token, binding, nonce)))),
                connection);

            handler.Hello(hello);

            if (hello.Status != 200)
            {
                return hello;
            }

            var challenge = Convert.FromBase64String(JsonBody.Read(hello.Json).Text("challenge"));
            var proof = PairingProof.Request(
                token, binding, nonce, challenge, PairingProof.Asked(name, profiles, null));

            var body = new System.Text.StringBuilder();
            body.Append("{\"name\":\"").Append(name).Append("\",\"profiles\":[");
            for (var i = 0; i < profiles.Length; i++)
            {
                if (i > 0)
                {
                    body.Append(',');
                }

                body.Append('"').Append(profiles[i]).Append('"');
            }

            body.Append("],\"proof\":\"").Append(Convert.ToBase64String(proof)).Append("\"}");

            var request = Post(PairingHandler.RequestPath, body.ToString(), connection);
            handler.Request(request);
            return request;
        }

        private static GateContext Post(string path, string body, PairingConnection connection)
        {
            var head = new HttpRequestHead(
                "POST",
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Bodies.LengthOf(body));

            var context = new GateContext(head, body, ClientIdentity.Stranger("database"));
            connection.PutOn(context);
            return context;
        }

        private static X509Certificate2 IdentityIn(GateContext context)
        {
            var body = JsonBody.Read(context.Json);
            Assert.That(body, Is.Not.Null, context.Json);

            return new X509Certificate2(
                Convert.FromBase64String(body.Text("identity")),
                (string)null,
                CertificateBlob.SigningFlags);
        }

        [Test]
        public void A_record_whose_save_failed_over_a_file_that_cannot_be_read_back_says_so_and_stays_unsaved()
        {
            // Something holding the file open is all it takes: the file is intact, only
            // unreadable, and the undo leaves the database marked unsaved since nothing shows the
            // file intact. That used to happen without a word on this path, and the change path's
            // line called it damage.
            _database.Save();
            _database.Database.Modified = false;
            // codeql[cs/missed-using-statement] opened in the save's callback and kept open for the assertions after it
            System.IO.FileStream held = null;

            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database =>
                {
                    held = new System.IO.FileStream(
                        _database.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
                    throw new System.IO.IOException("another program is holding the file");
                },
                null,
                _dialogs,
                _log);

            try
            {
                using (var certificate = IssueClient())
                {
                    var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                    Assume.That(result.Ok, Is.False);
                    Assert.That(_database.Database.Modified, Is.True);
                    Assert.That(_log.Errors, Has.Some.Contains("could not be read back"));
                    Assert.That(_log.Errors, Has.None.Contains("neither what it was nor what was written"),
                        "an unreadable file is not called damaged");
                }
            }
            finally
            {
                if (held != null)
                {
                    held.Dispose();
                }
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

        private static AdmissionRecord Record(X509Certificate2 certificate, params string[] profiles)
        {
            return AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles);
        }

        private GroupForm GroupDialogOnTheRoot()
        {
            var form = new GroupForm();
            form.InitEx(_database.RootGroup, false, null, _database.Database);
            return form;
        }

        /// <summary>
        /// A retired authority's document back beside its retirement, which is what a stale copy
        /// merged in leaves, answering with its digest.
        /// </summary>
        private string PlantRetiredDocument()
        {
            var retired = new string('e', 64);
            _database.Database.CustomData.Set(AuthorityKeys.AuthorityKey(retired), "a document from a stale copy");
            _database.Database.CustomData.Set(AuthorityKeys.RetirementKey(retired), AuthorityKeys.RetiredValue);
            return retired;
        }

        /// <summary>
        /// What a stale copy merged in after a revocation leaves: the record back beside the
        /// revocation that refuses it.
        /// </summary>
        private string PlantCameBackRecord()
        {
            var revoked = new string('d', 64);
            _database.Database.CustomData.Set(AuthorityKeys.ClientRecordKey(revoked), "a record that came back");
            _database.Database.CustomData.Set(AuthorityKeys.RevocationKey(revoked), "2999-01-01T00:00:00Z");
            return revoked;
        }

        /// <summary>
        /// The record of a client certificate that ended days ago, answering with its admission
        /// key.
        /// </summary>
        /// <remarks>
        /// From an authority of its own made a week ago, because nothing can be issued from before
        /// its authority began and this database's was made a moment ago. Whose authority it is
        /// does not matter to the prune, which reads the certificate's dates and nothing else.
        /// </remarks>
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

        /// <summary>An operator who agrees to exactly what was asked for.</summary>
        private sealed class AgreeingOperator : IPairingApproval
        {
            public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
            {
                return PairingApproval.Approved(request.Profiles);
            }
        }

        /// <summary>An operator who looks at what they are shown, and refuses.</summary>
        private sealed class LookingOperator : IPairingApproval
        {
            private readonly Action<PairingRequest> _look;

            public LookingOperator(Action<PairingRequest> look)
            {
                _look = look;
            }

            public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
            {
                _look(request);
                return PairingApproval.Refused();
            }
        }

        /// <summary>Every label a dialog shows, one to a line.</summary>
        private static string Texts(System.Windows.Forms.Control form)
        {
            var text = new System.Text.StringBuilder();
            foreach (var label in ControlTree.Descendants(form).OfType<System.Windows.Forms.Label>())
            {
                text.AppendLine(label.Text);
            }

            return text.ToString();
        }

        private X509Certificate2 Issue(string name, params string[] profiles)
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), null).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.Authority, name, profiles);
            }
        }

        /// <summary>
        /// Closes the database before it runs the write, which is a lock or a File > Close that
        /// KeePass's thread took first.
        /// </summary>
        private sealed class ClosesFirst : IUiThread
        {
            private readonly PwDatabase _closing;

            public ClosesFirst(PwDatabase closing)
            {
                _closing = closing;
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                _closing.Close();
                work();
                return true;
            }
        }

        /// <summary>A user interface thread that is busy elsewhere and never gets to the work.</summary>
        private sealed class NeverRuns : IUiThread
        {
            public bool Run(Action work, TimeSpan timeout)
            {
                return false;
            }
        }

        /// <summary>A log that remembers, so that a failure can be shown to have been reported.</summary>
        private sealed class RecordingLog : IGateLog
        {
            private readonly List<string> _errors = new List<string>();
            private readonly List<string> _warnings = new List<string>();

            public IReadOnlyList<string> Errors
            {
                get { return _errors; }
            }

            public IReadOnlyList<string> Warnings
            {
                get { return _warnings; }
            }

            public void Info(string message)
            {
                TestContext.Out.WriteLine("INFO  " + message);
            }

            public void Warn(string message)
            {
                _warnings.Add(message);
                TestContext.Out.WriteLine("WARN  " + message);
            }

            public void Error(string message, Exception error)
            {
                _errors.Add(message);
                TestContext.Out.WriteLine("ERROR " + message + " | " + error);
            }

            public void Unexpected(string message, Exception error)
            {
                // Its own level, because these are the ones that mean there is a defect.
                Error(message, error);
            }
        }
    }
}
