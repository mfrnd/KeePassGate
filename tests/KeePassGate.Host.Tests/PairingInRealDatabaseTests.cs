using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using KeePassGate.Spec;
using KeePassGate.Store;
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

            var created = DatabaseAuthority.Of(_database.Database, null).Create();
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
        public void A_write_whose_plan_leaves_the_admission_out_is_not_believed_and_spends_nothing()
        {
            // An act the plan leaves out: an admission whose issuer is not the authority standing
            // alone, which the plan then writes nothing for. Believed on a save of nothing, a
            // pairing would hand a certificate over with no admission in the file. Refused before
            // the condition is asked, and answered for what left it out.
            using (var certificate = IssueClient())
            {
                var lines = 0;
                var asked = 0;
                var before = Snapshot();
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer);

                var result = _writer.WriteAdmission(_database.Database, record, () => lines++, ground =>
                {
                    asked++;
                    return null;
                });

                Assert.That(result.Ok, Is.False, "a certificate would go out for an admission nobody wrote");
                Assert.That(result.IsUnserved, Is.True, "its issuer is not the authority served");
                Assert.That(asked, Is.Zero, "the approval and the invitation are left as a refused write leaves them");
                Assert.That(lines, Is.Zero);
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void An_admission_missing_from_the_tree_after_its_save_is_taken_back_out_and_hands_nothing_over()
        {
            // The read back of a pairing's write is of its own entries: each entry the plan added
            // is in the tree under its identifier, with the strings it was composed with, once the
            // save has ended. A save that loses the admission and writes the rest is not believed.
            using (var certificate = IssueClient())
            {
                var lines = 0;
                var before = Snapshot();
                var record = Record(certificate, "ops");
                var writer = new DatabaseWriter(
                    CurrentThread.Instance,
                    database =>
                    {
                        var admission = new StoredAdmissions(database).Entries("client")
                            .Single(entry => entry.Strings.ReadSafe("KPG.Data").Contains(record.AdmissionKey));
                        admission.ParentGroup.Entries.Remove(admission);
                        database.Save(null);
                    },
                    null,
                    _dialogs,
                    _log);

                var result = writer.WriteAdmission(_database.Database, record, () => lines++);

                Assert.That(result.Ok, Is.False, "a certificate would go out for an admission the tree does not hold");
                Assert.That(result.Reason, Does.Contain("did not survive"));
                Assert.That(lines, Is.Zero);
                Assert.That(Snapshot(), Is.EqualTo(before), "everything the write did is taken back out");
            }
        }

        [Test]
        public void A_pairing_into_a_store_whose_group_is_in_the_recycle_bin_makes_a_group_and_takes_it_back_with_the_write()
        {
            // `STORE.md` §606: a write puts nothing into a group in the recycle bin, where Empty
            // Recycle Bin would delete it, and makes a group of the plugin's own under the root. A
            // pairing whose save does not land takes that group back out with everything else it
            // did; a pairing that lands leaves its admission there, with the records in the bin
            // still counting.
            var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
            _database.RootGroup.AddGroup(bin, true);
            _database.Database.RecycleBinEnabled = true;
            _database.Database.RecycleBinUuid = bin.Uuid;
            var group = PluginGroups().Single();
            _database.RootGroup.Groups.Remove(group);
            bin.AddGroup(group, true);

            using (var refused = IssueClient())
            using (var admitted = IssueClient())
            {
                var failing = new DatabaseWriter(
                    CurrentThread.Instance,
                    delegate { throw new System.IO.IOException("the disk is full"); },
                    null,
                    _dialogs,
                    _log);

                Assume.That(failing.WriteAdmission(_database.Database, Record(refused, "ops")).Ok, Is.False);
                Assert.That(PluginGroups(), Is.EqualTo(new[] { group }), "the group the write made is taken back with it");
                Assert.That(group.ParentGroup, Is.SameAs(bin));

                var written = _writer.WriteAdmission(_database.Database, Record(admitted, "ops"));

                Assert.That(written.Ok, Is.True, written.Reason);
                var made = PluginGroups().Single(found => found != group);
                Assert.That(made.ParentGroup, Is.SameAs(_database.RootGroup));
                Assert.That(made.Entries.Count(entry => entry.Strings.ReadSafe("KPG.Kind") == "client"), Is.EqualTo(1),
                    "the admission is outside the bin");
                Assert.That(new StoredAdmissions(_database.Database).Find(CertificateDigest.Of(admitted)), Is.Not.Null,
                    "and admits, the authority in the bin still counting");
            }
        }

        [Test]
        public void A_pairing_while_an_entry_dialog_holds_the_summary_entry_is_busy_and_spends_nothing()
        {
            // `STORE.md` §626: every write deletes the summary entry it finds, and an entry dialog
            // puts its copy of an entry back at its OK, so a pairing is refused for as long as that
            // dialog stands, before its condition is asked.
            using (var icons = new System.Windows.Forms.ImageList())
            using (var form = new PwEntryForm())
            using (var certificate = IssueClient())
            {
                var summary = _database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "head");
                form.InitEx(summary, PwEditMode.EditExistingEntry, _database.Database, icons, false, false);
                var asked = 0;
                var before = Snapshot();

                GlobalWindowManager.AddWindow(form);
                try
                {
                    var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"), null, ground =>
                    {
                        asked++;
                        return null;
                    });

                    Assert.That(result.IsBusy, Is.True, result.Reason);
                    Assert.That(asked, Is.Zero, "the approval and the invitation are left for a retry");
                    Assert.That(_saves, Is.Zero);
                    Assert.That(Snapshot(), Is.EqualTo(before));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }

                Assert.That(_writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.True,
                    "and the same write goes through once the dialog closes");
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
                    new StoredAdmissions(_database.Database).Find(record.AdmissionKey),
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

                var found = new StoredAdmissions(_database.Database)
                    .Find(record.AdmissionKey);

                Assert.That(found, Is.Not.Null, "a record that is not in the file admits nobody tomorrow");
                Assert.That(found.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void The_profiles_the_record_carries_are_named_in_the_same_write()
        {
            // A profile the operator approved is created by their approval, and it has to exist by
            // the time anybody opens the tab to write a rule for it: an addition of the admission's
            // own act, `STORE.md` §616, the same thread, the same lock, the same save, and refused
            // by the same open dialog.
            using (var certificate = IssueClient())
            {
                Assume.That(_writer.WriteAdmission(_database.Database, Record(certificate, "invented")).Ok, Is.True);

                Assert.That(Names(), Does.Contain("invented"),
                    "a profile in a record and in no list is a name no rule can be written against");

                _database.SaveCloseAndReopen();

                Assert.That(Names(), Does.Contain("invented"), "and the save that keeps the record keeps it");
            }
        }

        [Test]
        public void A_pairing_adds_its_new_name_however_many_names_the_list_holds()
        {
            // The list keeps no ceiling, `STORE.md` §616.
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            for (var index = 0; index < 33; index++)
            {
                StoreSeeds.Name(store, "invented-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
            }

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "one-more"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(Names(), Has.Count.EqualTo(34));
                Assert.That(Names(), Does.Contain("one-more"));
            }
        }

        [Test]
        public void A_pairing_for_a_name_that_was_removed_names_the_removal_and_deletes_the_addition_it_defeated()
        {
            // `STORE.md` §616 and §627 step 3: the addition the pairing writes names the removal it
            // read, and an addition that did not see the removal, as a merge brings from a copy,
            // is deleted by the same write with a deletion record.
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Unname(store, "ops");
            string removal;
            using (var reading = store.Read())
            {
                removal = reading.InForce.RemovalsOf("ops").Single();
                Assume.That(reading.InForce.Additions, Is.Empty, "the removal's own write deleted the addition it defeated");
            }

            var stale = StaleAddition(store, "ops");

            using (var certificate = IssueClient())
            {
                Assert.That(_writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.True);
            }

            using (var reading = store.Read())
            {
                Assert.That(ProfileNames.Of(reading).Holds("ops"), Is.True);
                var addition = reading.InForce.Additions.Single();
                Assert.That(addition.Form.Member("after"), Is.EqualTo(removal));
                Assert.That(reading.Deletions.Select(KeePassGate.Hex.Of), Does.Contain(stale));
            }
        }

        [Test]
        public void A_pairing_whose_save_fails_puts_back_the_addition_its_write_deleted()
        {
            // The undo the application hands back puts back what the plan deleted, with its
            // deletion record taken away, and takes out what it added: the admission and the
            // pairing's own addition among them, `STORE.md` §627.
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Unname(store, "ops");
            var stale = StaleAddition(store, "ops");
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.False,
                    "a save that writes nothing is not believed");
            }

            using (var reading = store.Read())
            {
                Assert.That(reading.InForce.Additions.Select(addition => addition.Hex), Is.EqualTo(new[] { stale }),
                    "the addition the write deleted is back, and the pairing's own is gone");
                Assert.That(reading.Deletions.Select(KeePassGate.Hex.Of), Does.Not.Contain(stale), "with no deletion record");
                Assert.That(ProfileNames.Of(reading).Holds("ops"), Is.False);
            }
        }

        /// <summary>
        /// An addition of this name naming no removal, as a copy that never read the removals of
        /// the name writes it, answering its identifier.
        /// </summary>
        private static string StaleAddition(KdbxStore store, string name)
        {
            byte[] key;
            string against;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
                against = reading.InForce.Key.Name;
            }

            var record = KeePassGate.Acl.ProfileActs.Addition(key, name, Array.Empty<string>());
            var act = new KeePassGate.Store.StoreAct("an addition from a copy", key, against, new[] { record }, null);
            try
            {
                Assume.That(store.Write(new[] { act }).Taken, Is.True);
                return record.Hex;
            }
            finally
            {
                act.Forget();
                Array.Clear(key, 0, key.Length);
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

                    Assert.That(Names(), Does.Not.Contain("invented"));
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
                    Assert.That(Stored(record.AdmissionKey), Is.False);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_pairing_deletes_an_admission_that_came_back_beside_its_revocation()
        {
            // Reading the database, which is all the gate does when that client connects, changes
            // nothing in it; the next write this plugin makes anyway deletes the admission, with a
            // deletion record, so a merge that brings it again finds it out of force.
            var cameBack = PlantCameBackAdmission();
            _database.Database.Modified = false;

            var store = new StoredAdmissions(_database.Database);
            Assume.That(store.Find(cameBack), Is.Null, "the revocation beside it refuses it meanwhile");
            Assume.That(store.All().Waiting, Is.EqualTo(1));
            Assert.That(Stored(cameBack), Is.True);
            Assert.That(_database.Database.Modified, Is.False, "reading it marks nothing modified");

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));
                Assert.That(result.Ok, Is.True, result.Reason);

                _database.SaveCloseAndReopen();

                Assert.That(Stored(cameBack), Is.False, "gone from the file, not only from memory");
                Assert.That(new StoredAdmissions(_database.Database).Entries("revoked"), Is.Not.Empty,
                    "and the revocation stays, for a stale copy that brings it back again");
                Assert.That(new StoredAdmissions(_database.Database).All().Waiting, Is.Zero);
            }
        }

        [Test]
        public void A_pairing_that_does_not_land_puts_an_admission_that_came_back_back_as_well()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);
            var cameBack = PlantCameBackAdmission();
            _database.Database.Modified = false;
            var before = Snapshot();

            using (var certificate = IssueClient())
            {
                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "ops")).Ok, Is.False);

                Assert.That(Stored(cameBack), Is.True, "memory has to go on agreeing with the file the write never reached");
                Assert.That(Snapshot(), Is.EqualTo(before), "and the store is as it was, deletion records and all");
                Assert.That(_database.Database.Modified, Is.False);
            }
        }

        [Test]
        public void A_pairing_leaves_the_admission_of_an_expired_certificate_where_it_is()
        {
            // Nothing is deleted for its age, `STORE.md` §643: the gate refuses an expired
            // certificate on its dates before any admission is read.
            var expired = PlantExpiredAdmission();
            _database.Database.Modified = false;

            var store = new StoredAdmissions(_database.Database);
            Assume.That(store.Find(expired), Is.Not.Null, "the admission is there, and its dates are what refuse it");
            Assert.That(_database.Database.Modified, Is.False, "reading it marks nothing modified");

            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));
                Assert.That(result.Ok, Is.True, result.Reason);

                _database.SaveCloseAndReopen();

                Assert.That(Stored(expired), Is.True, "still in the file");
                Assert.That(Stored(CertificateDigest.Of(certificate)), Is.True, "beside the admission the pairing wrote");
            }
        }

        [Test]
        public void A_group_dialog_open_on_the_root_group_refuses_nothing()
        {
            // An admission touches no group that dialog holds a copy of, so there is nothing of
            // this write for a group dialog's OK to put back.
            using (var certificate = IssueClient())
            using (var form = GroupDialogOnTheRoot())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var record = Record(certificate, "ops");

                    Assert.That(_writer.Blocked(_database.Database), Is.Null);
                    Assert.That(_writer.WriteAdmission(_database.Database, record).Ok, Is.True);
                    Assert.That(Stored(record.AdmissionKey), Is.True);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_pairing_that_lands_rebuilds_the_tree_and_the_entry_list()
        {
            // An admission and its summary entry are entries of the plugin's group, so the tree
            // and the entry list are out of date once the write stands, as for every change that
            // adds an entry.
            var redraws = new List<bool>();
            var writer = new DatabaseWriter(
                CurrentThread.Instance, Save, (database, lists) => redraws.Add(lists), _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(redraws, Is.EqualTo(new[] { true }));
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
        public void A_record_taken_back_out_over_a_file_another_program_rewrote_redraws_the_mark_left_up()
        {
            // A save KeePass abandoned before writing returns without `UpdateUIState`, so the
            // flag the record raised was never drawn. Over a file that is not what KeePass read
            // the flag stays up when the record goes back out, and the window has to be told.
            _database.Save();
            _database.Database.Modified = false;

            var redraws = 0;
            var rebuilt = true;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => System.IO.File.AppendAllText(_database.Path, "rewritten by another program"),
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
                Assume.That(_database.Database.Modified, Is.True);
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
                Assert.That(Stored(record.AdmissionKey), Is.False,
                    "and the admission is taken back out of the database in memory as well");
                Assert.That(_database.Database.Modified, Is.True,
                    "which leaves KeePass asking before it discards the copy that holds the truth");
            }
        }

        [Test]
        public void A_record_the_file_did_not_keep_leaves_the_database_marked_even_if_the_file_then_reads_whole()
        {
            // Two reads of the file: the verification, and the undo's question whether the file is
            // as KeePass left it. A copy that is part way through at the first and done by the
            // second disagrees and then agrees, which the hash KeePass verifies against, set aside
            // after the save, stands in for here.
            using (var certificate = IssueClient())
            {
                _database.Save();

                var writer = new DatabaseWriter(
                    CurrentThread.Instance,
                    database =>
                    {
                        Save(database);
                        typeof(PwDatabase)
                            .GetField("m_pbHashOfLastIO", BindingFlags.NonPublic | BindingFlags.Instance)
                            .SetValue(database, new byte[32]);
                    },
                    null,
                    _dialogs,
                    _log);

                var record = Record(certificate, "ops");
                var result = writer.WriteAdmission(_database.Database, record);

                Assert.That(result.Ok, Is.False);
                Assert.That(Stored(record.AdmissionKey), Is.False);
                Assert.That(_database.Database.Modified, Is.True,
                    "memory lost a record the file may hold, so KeePass asks before discarding it");
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
                Assert.That(new StoredAdmissions(_database.Database).Find(record.AdmissionKey), Is.Not.Null,
                    "and the admission the client was admitted by is the one on the disk");
            }
        }

        [Test]
        public void A_save_that_fails_after_writing_the_record_cannot_lose_its_audit_line()
        {
            // A `FileSaved` handler's defect, which no filter of the writer's names, once the file
            // holds the record: the pairing ends with no certificate handed over, and the record
            // that stays in force is written down before it does.
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
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");

                Assert.Throws<NullReferenceException>(
                    () => writer.WriteAdmission(_database.Database, record, () => lines++));

                Assert.That(lines, Is.EqualTo(1));

                _database.CloseAndReopenWithoutSaving();

                Assert.That(new StoredAdmissions(_database.Database).Find(record.AdmissionKey), Is.Not.Null,
                    "and the admission the line is about is in the file");
            }
        }

        [Test]
        public void A_save_that_fails_before_writing_the_record_leaves_no_record_behind()
        {
            // A `FileSavingPre` handler's defect, before anything reached the disk. The record
            // stayed in the database for the operator's next save to keep, admitting a
            // certificate nobody was handed and written down nowhere.
            var lines = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new NullReferenceException("a FileSavingPre handler's defect"); },
                null,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                _database.Database.Modified = false;
                var before = Snapshot();

                Assert.Throws<NullReferenceException>(
                    () => writer.WriteAdmission(_database.Database, record, () => lines++));

                Assert.That(lines, Is.Zero, "an admission taken back out admitted nobody");
                Assert.That(Snapshot(), Is.EqualTo(before), "the store is as it was, the summary entry and the deletion records among it");
                Assert.That(_database.Database.Modified, Is.False, "and the database agrees with the file");
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

                // From a database with nothing outstanding, so that anything left over afterwards
                // is this write's doing and nobody else's.
                _database.Database.Modified = false;
                var before = Snapshot();

                Assume.That(writer.WriteAdmission(_database.Database, record).Ok, Is.False);

                Assert.That(Stored(record.AdmissionKey), Is.False,
                    "an admission that did not land must not be left in the group");
                Assert.That(Snapshot(), Is.EqualTo(before),
                    "and every entry and deletion record of the store is as it was");
                Assert.That(_database.Database.Modified, Is.False,
                    "and the database must not be left asking to be saved for it");
            }
        }

        [Test]
        public void A_save_that_does_not_happen_leaves_no_profile_name_behind_either()
        {
            // The other half of the same write. The additions of the names a pairing was approved
            // for are records of the admission's own act, so a save that does not happen takes
            // them back out with it. A name left behind would be a name the operator never typed,
            // in a database KeePass had been told has nothing to save, committed by the next
            // unrelated edit and shown on the profiles page with no record holding it.
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                _database.Database.Modified = false;

                Assume.That(writer.WriteAdmission(_database.Database, Record(certificate, "invented")).Ok,
                    Is.False);

                Assert.That(Names(), Does.Not.Contain("invented"),
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
                var before = Snapshot();

                Assume.That(writer.WriteAdmission(_database.Database, record).Ok, Is.False);

                Assert.That(Stored(record.AdmissionKey), Is.False);
                Assert.That(Snapshot(), Is.EqualTo(before));
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

                var result = writer.WriteAdmission(_database.Database, record);

                Assert.That(result.Ok, Is.False);
                Assert.That(Stored(record.AdmissionKey), Is.False);
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
            // The whole of pairing against a real database: an invitation, both proofs, an operator
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
        public void A_pairing_whose_authority_is_recreated_before_its_write_is_told_its_invitation_is_gone()
        {
            // A Recreate landing while the operator reads the dialog retires the authority the
            // invitation was made under. The write's plan leaves the admission out before anything
            // is asked or spent, and the answer is the answer for that authority going: no retry
            // under it can be written, so a `503` would invite a retry that cannot work.
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                string before = null;
                var operatorAt = new AgreeingAfter(() =>
                {
                    var recreated = DatabaseAuthority.Of(_database.Database, null).Recreate();
                    Assume.That(recreated.Succeeded, Is.True, recreated.Message);
                    before = Snapshot();
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(context.Status, Is.EqualTo(409), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("no invitation")));
                Assert.That(book.Outstanding().Count, Is.EqualTo(1), "the invitation is not spent");
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void A_summary_entry_deleted_for_good_while_the_dialog_stands_is_answered_stopped_with_nothing_spent()
        {
            // The store asked again in the write's own turn, `STORE.md` §627: a store that came to
            // hold the gate back while the operator read the dialog is answered its word, as a
            // connection is, with nothing written and the invitation unspent.
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                string before = null;
                var operatorAt = new AgreeingAfter(() =>
                {
                    var store = new StoredAdmissions(_database.Database).Store;
                    StoreSeeds.Revoke(store, new string('e', 64));
                    var head = _database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "head");
                    head.ParentGroup.Entries.Remove(head);
                    _database.Database.DeletedObjects.Add(new PwDeletedObject(head.Uuid, DateTime.UtcNow));
                    before = Snapshot();
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("stopped")));
                Assert.That(book.Outstanding().Count, Is.EqualTo(1), "the invitation is not spent");
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [TestCase("lost")]
        [TestCase("split")]
        [TestCase("outdated")]
        public void An_admission_over_a_store_that_holds_the_gate_back_is_answered_its_word_before_anything_is_asked(
            string word)
        {
            // The store read in the write's own turn, `STORE.md` §627, and answered as a connection
            // to the database is answered, `STORE.md` §656: before the plan, the condition and the
            // save, so an approval and an invitation are spent on nothing.
            AnsweredBeforeAnythingIsAsked(word);
        }

        [Test]
        public void An_admission_while_several_authorities_stand_is_answered_undecided_before_anything_is_asked()
        {
            // A second authority standing beside the first serves nothing until the operator keeps
            // an authority, and is answered as a connection to the database is.
            AnsweredBeforeAnythingIsAsked(HeldBackTarget.Undecided);
        }

        /// <summary>
        /// An admission written once the database is brought to be answered with this word, and
        /// what came of it.
        /// </summary>
        private void AnsweredBeforeAnythingIsAsked(string word)
        {
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var lines = 0;
                var asked = 0;
                new StoredAdmissions(_database.Database).HoldBack(word);
                var before = Snapshot();

                var result = _writer.WriteAdmission(_database.Database, record, () => lines++, ground =>
                {
                    asked++;
                    return null;
                });

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Word, Is.EqualTo(word), result.Reason);
                Assert.That(asked, Is.Zero, "the condition spends nothing");
                Assert.That(lines, Is.Zero);
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void An_admission_whose_authority_was_retired_beside_several_others_is_answered_unserved_before_anything_is_asked()
        {
            // Several authorities stand and the authority the invitation was made under is not
            // among them: no decision of the operator's can serve it, so the plan refuses the write
            // and it is answered as under an authority that went, with nothing asked or spent.
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var lines = 0;
                var asked = 0;
                new StoredAdmissions(_database.Database).RetiredBesideOthers();
                var before = Snapshot();

                var result = _writer.WriteAdmission(_database.Database, record, () => lines++, ground =>
                {
                    asked++;
                    return null;
                });

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Word, Is.Null, result.Reason);
                Assert.That(result.IsUnserved, Is.True, result.Reason);
                Assert.That(asked, Is.Zero, "the condition spends nothing");
                Assert.That(lines, Is.Zero);
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void An_authority_retired_beside_several_others_while_the_dialog_stands_is_answered_no_invitation_with_nothing_spent()
        {
            // Beside the case of a second authority arriving: the authority the invitation was made
            // under is retired and two others stand, so the pairing is told as an invitation that
            // went, and the invitation itself is not spent.
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                string before = null;
                var operatorAt = new AgreeingAfter(() =>
                {
                    new StoredAdmissions(_database.Database).RetiredBesideOthers();
                    before = Snapshot();
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(context.Status, Is.EqualTo(409), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("no invitation")));
                Assert.That(book.Outstanding().Count, Is.EqualTo(1), "the invitation is not spent");
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void A_second_authority_arriving_while_the_dialog_stands_is_answered_undecided_with_nothing_spent()
        {
            // A synchronize bringing a copy that made an authority of its own, while the operator
            // reads the dialog: a connection to the database is answered `undecided` from then on,
            // and so is the pairing at its write, with its invitation good once the operator keeps
            // the authority it was made under.
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                string before = null;
                var operatorAt = new AgreeingAfter(() =>
                {
                    new StoredAdmissions(_database.Database).HoldBack(HeldBackTarget.Undecided);
                    before = Snapshot();
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(HeldBackTarget.Undecided)));
                Assert.That(book.Outstanding().Count, Is.EqualTo(1), "the invitation is not spent");
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before));
            }
        }

        [TestCase(AsRecordAndItem)]
        [TestCase(AsDeletionRecordAlone)]
        [TestCase(OnlyInTheOtherCopy)]
        public void A_removal_synchronized_in_before_the_dialog_is_built_is_a_removal_the_approval_puts_the_name_back_over(string how)
        {
            // `ops` removed in another copy, and here as well or not, whose removal KeePass's
            // Synchronize brings in before the dialog is built, and no client holds `ops`: the
            // real form marks `ops` removed with the line about every rule still naming it, and
            // approving puts it back over removals the operator was shown, `PAIRING.md` §311.
            using (var copy = RemovedInAnotherCopy(how))
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                Synchronized(copy, how == OnlyInTheOtherCopy ? 1 : 2);
                var target = targets.Current().Single();
                string shown = null;

                var context = Pair(
                    book, target, book.Invite(target.DatabaseId), "agent-fictitious", new[] { "ops" },
                    new LookingAndAgreeing(request => shown = Shown(request)));

                Assert.That(shown, Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(shown, Does.Not.Contain("ops" + PairingApprovalForm.NewSuffix));
                Assert.That(shown, Does.Contain("every rule still naming it applies again"));
                Assert.That(shown, Does.Not.Contain(ShownAgain));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops" }));
            }
        }

        [TestCase(AsRecordAndItem)]
        [TestCase(AsDeletionRecordAlone)]
        public void A_removal_synchronized_in_while_the_dialog_stands_makes_the_approval_of_its_name_stale(string how)
        {
            // The same removal, brought in while the operator reads the dialog: the write finds a
            // removal the reading taken before the dialog did not hold, and nothing is written or
            // spent, `PAIRING.md` §311. The same request puts the real form to the operator again,
            // with its notice, and its approval writes the name back.
            using (var copy = RemovedInAnotherCopy(how))
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                string synchronized = null;
                string atSecond = null;
                var shown = new List<string>();
                var operatorAt = new LookingAndAgreeing(request =>
                {
                    shown.Add(Shown(request));
                    if (shown.Count == 1)
                    {
                        Synchronized(copy, 2);
                        synchronized = Snapshot();
                    }
                    else
                    {
                        atSecond = Standing(book, synchronized);
                    }
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(shown, Has.Count.EqualTo(2));
                Assert.That(atSecond, Is.EqualTo(NothingWritten), "nothing written, saved or spent before the second Approve");
                Assert.That(_log.Warnings, Has.Some.Contains("the store holds a removal of the profile ops"));
                Assert.That(shown[0], Does.Not.Contain(ShownAgain));
                Assert.That(shown[1], Does.Contain(StateChanged));
                Assert.That(shown[1], Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void A_name_taken_off_the_list_while_the_dialog_stands_brings_the_dialog_back_with_the_name_removed()
        {
            // The other cause: `ops` on the list as the dialog showed it, and taken off it before
            // the write, as the Profiles page's OK or a synchronize does. The same request puts
            // the real form to the operator again with the same notice as for a removal, marking
            // `ops` removed, as its reading holds it, and the log names the cause.
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                string taken = null;
                string atSecond = null;
                var shown = new List<string>();
                var operatorAt = new LookingAndAgreeing(request =>
                {
                    shown.Add(Shown(request));
                    if (shown.Count == 1)
                    {
                        StoreSeeds.Unname(store, "ops");
                        taken = Snapshot();
                    }
                    else
                    {
                        atSecond = Standing(book, taken);
                    }
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(_log.Warnings, Has.Some.Contains("the profile ops is off the list, and the dialog showed it on the list"));
                Assert.That(atSecond, Is.EqualTo(NothingWritten));
                Assert.That(shown[0], Does.Not.Contain(ShownAgain));
                Assert.That(shown[1], Does.Contain(StateChanged));
                Assert.That(shown[1], Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void The_real_form_shown_again_opens_with_what_the_operator_approved_before()
        {
            // The client asks for `reader`, `admin` and `ops`; the operator unchecks `admin`, picks
            // seven days and approves while `ops` is put on the list. The real form the same
            // request puts to the operator again opens with `reader` checked, `admin` unchecked as
            // the operator left it, `ops` unchecked as a row whose mark changed, and seven days
            // chosen, and a single Approve on it writes `reader` alone.
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "reader");
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                var opened = new List<string>();
                var answers = 0;
                var operatorAt = new Answering(request =>
                {
                    answers++;
                    if (answers == 1)
                    {
                        StoreSeeds.Name(store, "ops");
                        return PairingApproval.Approved(new[] { "reader", "ops" }, TimeSpan.FromDays(7));
                    }

                    return OpenedAndApproved(request, opened);
                });

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "reader", "admin", "ops" }, operatorAt);

                Assert.That(answers, Is.EqualTo(2));
                Assert.That(opened, Is.EqualTo(new[]
                {
                    "reader checked",
                    "admin" + PairingApprovalForm.NewSuffix + " unchecked",
                    "ops unchecked",
                    LifetimeChoice.Listed(TimeSpan.FromDays(7), null),
                }));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops", "reader" }));
            }
        }

        [TestCase(AsRecordAndItem)]
        [TestCase(AsDeletionRecordAlone)]
        public void A_removal_synchronized_in_while_an_approval_is_held_makes_it_stale_at_the_retry(string how)
        {
            // The operator approves `ops` while Database Maintenance is open on the database, so
            // the write is answered busy and the approval held. The dialog is closed, the removal
            // is brought in, and the client's retry, written without the dialog, finds a removal
            // the reading taken before the dialog did not hold: nothing is written or spent, and
            // the retry puts the real form to the operator with its notice, opening as the approval
            // held, which is answered once its approval is written.
            using (var copy = RemovedInAnotherCopy(how))
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            using (var form = MaintenanceDialog())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);
                var held = new ApprovalBook();
                string synchronized = null;
                string atSecond = null;
                var shown = new List<string>();
                var operatorAt = new LookingAndAgreeing(request =>
                {
                    shown.Add(Shown(request));
                    if (shown.Count == 1)
                    {
                        GlobalWindowManager.AddWindow(form);
                    }
                    else
                    {
                        atSecond = Standing(book, synchronized);
                    }
                });
                var handler = new PairingHandler(
                    book, operatorAt, new PairingOptions { ApprovalSettle = TimeSpan.Zero }, new AuthorityIssuer(), null, null, _log, held);

                try
                {
                    var busy = Pair(handler, target, invitation, "agent-fictitious", new[] { "ops" });

                    Assert.That(busy.Status, Is.EqualTo(503), busy.Json);
                    Assert.That(JsonBody.Read(busy.Json).Text("heldUntil"), Is.Not.Null, busy.Json);
                    Assert.That(held.Count, Is.EqualTo(1));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }

                Synchronized(copy, 2);
                synchronized = Snapshot();
                var context = Pair(handler, target, invitation, "agent-fictitious", new[] { "ops" });

                Assert.That(shown, Has.Count.EqualTo(2), "the retry puts the dialog up once its write finds the approval stale");
                Assert.That(atSecond, Is.EqualTo(NothingWritten));
                Assert.That(shown[1], Does.Contain(StateChanged));
                Assert.That(shown[1], Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(held.Count, Is.Zero, "the approval is used up");
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void A_name_the_file_does_not_know_whose_removal_a_deletion_record_alone_tells_of_is_put_back_at_the_second_approval()
        {
            // Another copy named `ops` and removed it, deleted the removal for good and lost the
            // summary entry restating it with no deletion record; this copy wrote a summary entry of
            // its own, and a Synchronize brings the deletion records in. Nothing in this file names
            // `ops` but the deletion record of its removal. The reading taken before the dialog takes
            // no key and cannot tie that deletion record to `ops`, so the real form shows `ops` as
            // new; the write, counting with the key, finds the removal and writes nothing. The same
            // request puts the form to the operator again with its notice, marking `ops` removed,
            // and its approval puts `ops` back over that removal, stale no more, `STORE.md` §616.
            using (var copy = RemovedApartInAnotherCopy())
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                Synchronized(copy, 1);
                using (var reading = new KdbxStore(_database.Database, GateVersion.Current, null).Read())
                {
                    Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty, "a name the file does not know");
                }

                var target = targets.Current().Single();
                var before = Snapshot();
                string atSecond = null;
                var shown = new List<string>();
                var operatorAt = new LookingAndAgreeing(request =>
                {
                    shown.Add(Shown(request));
                    if (shown.Count == 2)
                    {
                        atSecond = Standing(book, before);
                    }
                });

                var context = Pair(book, target, book.Invite(target.DatabaseId), "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(shown, Has.Count.EqualTo(2));
                Assert.That(shown[0], Does.Contain("ops" + PairingApprovalForm.NewSuffix));
                Assert.That(shown[0], Does.Not.Contain(ShownAgain));
                Assert.That(atSecond, Is.EqualTo(NothingWritten));
                Assert.That(shown[1], Does.Contain(StateChanged));
                Assert.That(shown[1], Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops", "reader" }));
                Assert.That(
                    _log.Warnings.Count(line => line.Contains("no longer holds what the dialog showed")), Is.EqualTo(1),
                    "not stale again over the removal it was shown");
            }
        }

        [Test]
        public void A_name_a_synchronize_takes_off_the_list_while_the_dialog_stands_is_marked_removed_at_the_next_dialog()
        {
            // `ops` on the list as the first dialog showed it. While it stands, KeePass's
            // Synchronize brings in another copy that removed `ops`, deleted the removal for good
            // and lost its summary entry: `ops` is off the list, and nothing in this file names it
            // but the deletion record of its removal. The write finds the move and, counting with
            // the store key, that removal, which the real target's next reading cannot tie to
            // `ops`; the next dialog of the same request, the real form, marks `ops` as removed
            // with its line all the same, and approving it puts `ops` back. A name this file never
            // knew cannot arrive removed this way: the copy then writes twice and loses its last
            // summary entry, which leaves the deletion of its first summary entry replaced by
            // nothing in force and stops the store, so that case is composed in the unit tier.
            using (var copy = RemovedApartInAnotherCopy())
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                string synchronized = null;
                string atSecond = null;
                var shown = new List<string>();
                var operatorAt = new LookingAndAgreeing(request =>
                {
                    shown.Add(Shown(request));
                    if (shown.Count == 1)
                    {
                        Synchronized(copy, 1);
                        synchronized = Snapshot();
                    }
                    else
                    {
                        atSecond = Standing(book, synchronized);
                    }
                });

                var context = Pair(book, target, book.Invite(target.DatabaseId), "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(shown[0], Does.Not.Contain("ops" + PairingApprovalForm.NewSuffix), "on the list");
                Assert.That(shown[0], Does.Not.Contain("ops" + PairingApprovalForm.RemovedSuffix), "on the list");
                Assert.That(shown[0], Does.Not.Contain(ShownAgain), "a first dialog carries no notice");
                Assert.That(atSecond, Is.EqualTo(NothingWritten));
                Assert.That(shown[1], Does.Contain(StateChanged));
                Assert.That(shown[1], Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(shown[1], Does.Not.Contain("ops" + PairingApprovalForm.NewSuffix));
                Assert.That(shown[1], Does.Contain("every rule still naming it applies again"));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops", "reader" }));
            }
        }

        [Test]
        public void A_refusal_of_the_dialog_shown_again_spends_the_invitation_and_the_next_request_starts_from_its_own_reading()
        {
            // The operator refuses on the dialog shown again: answered `403 refused` and the
            // invitation spent, as a refusal of a first dialog is, with nothing written or saved.
            // Nothing of that request reaches the request on the code handed out next: its first
            // dialog carries no notice and shows `ops` as its own reading shows it, new, its own
            // write finds the removal, and its own dialog shown again marks `ops` removed.
            using (var copy = RemovedApartInAnotherCopy())
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                Synchronized(copy, 1);
                var target = targets.Current().Single();
                var shown = new List<string>();
                var operatorAt = new Answering(request =>
                {
                    shown.Add(Shown(request));
                    return shown.Count == 2 ? PairingApproval.Refused() : PairingApproval.Approved(request.Profiles);
                });
                var before = Snapshot();

                var refused = Pair(book, target, book.Invite(target.DatabaseId), "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(refused.Status, Is.EqualTo(403), refused.Json);
                Assert.That(refused.Json, Is.EqualTo(HttpResponse.ErrorBody("refused")));
                Assert.That(book.Outstanding(), Is.Empty, "a refusal spends the invitation");
                Assert.That(_saves, Is.Zero);
                Assert.That(Snapshot(), Is.EqualTo(before));

                var context = Pair(book, target, book.Invite(target.DatabaseId), "agent-other", new[] { "ops" }, operatorAt);

                Assert.That(shown, Has.Count.EqualTo(4));
                Assert.That(shown[2], Does.Not.Contain(ShownAgain), "the first dialog of a request carries no notice");
                Assert.That(shown[2], Does.Contain("ops" + PairingApprovalForm.NewSuffix));
                Assert.That(shown[3], Does.Contain(StateChanged));
                Assert.That(shown[3], Does.Contain("ops" + PairingApprovalForm.RemovedSuffix));
                Assert.That(context.Status, Is.EqualTo(200), context.Json);
                Assert.That(Names(), Is.EqualTo(new[] { "ops", "reader" }));
            }
        }

        /// <summary>
        /// Whether anything was saved, how many invitations are outstanding, and whether the store
        /// is as this snapshot of it, as a line.
        /// </summary>
        private string Standing(InvitationBook book, string snapshot)
        {
            return _saves.ToString(System.Globalization.CultureInfo.InvariantCulture) + " saved, "
                   + book.Outstanding().Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " outstanding, "
                   + (Snapshot() == snapshot ? "unchanged" : "changed");
        }

        /// <summary>What stands as a dialog comes up with nothing written, saved or spent.</summary>
        private const string NothingWritten = "0 saved, 1 outstanding, unchanged";

        /// <summary>
        /// What the real form opens with for this request, every row with whether it is checked and
        /// then the lifetime chosen, noted here, and the approval of exactly that.
        /// </summary>
        private static PairingApproval OpenedAndApproved(PairingRequest request, List<string> opened)
        {
            using (var dialog = new PairingApprovalForm(request, TimeSpan.FromMinutes(2)))
            {
                var list = ControlTree.FindByName<System.Windows.Forms.CheckedListBox>(dialog, GateTab.ApprovalProfilesListName);
                var lifetime = ControlTree.FindByName<System.Windows.Forms.ComboBox>(dialog, GateTab.ApprovalLifetimeName);
                opened.AddRange(list.Items.Cast<object>().Select((row, at) => row + (list.GetItemChecked(at) ? " checked" : " unchecked")));
                opened.Add(lifetime.SelectedItem.ToString());
                return PairingApproval.Approved(
                    list.CheckedItems.Cast<object>().Select(row => PairingApprovalForm.Asked(row.ToString())).ToList(),
                    LifetimeChoice.Offered(request.Lifetime)[lifetime.SelectedIndex]);
            }
        }

        [TestCase(StoreWords.Split, 503, StoreWords.Split, "holds the gate back")]
        [TestCase(StoreWords.Outdated, 503, StoreWords.Outdated, "holds the gate back")]
        [TestCase(StoreWords.Stopped, 503, StoreWords.Stopped, "holds the gate back")]
        [TestCase(HeldBackTarget.Undecided, 503, HeldBackTarget.Undecided, "several authorities stand")]
        [TestCase(Retired, 409, "no invitation", "no longer the authority served")]
        public void A_store_that_stopped_serving_before_the_dialog_is_answered_as_its_write_would_be(
            string how, int status, string word, string why)
        {
            // The real target, served as the pairing arrived, and by the time the dialog's reading
            // is taken a store that holds the gate back, several authorities standing, or the
            // authority retired: no dialog is raised over that reading, the client is told what
            // the write would tell it, and nothing is spent or written. A database closed by then
            // is turned away by the check before the reading, and where it closes between the
            // two, the reading throws, `OpenDatabaseTargetsTests`, which the handler answers
            // `locked`, `PairingHandlerTests`.
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            using (var target = targets.Current().Single())
            {
                var invitation = book.Invite(target.DatabaseId);
                if (how == Retired)
                {
                    StoreSeeds.Retire(store, target.DatabaseId);
                }
                else
                {
                    new StoredAdmissions(_database.Database).HoldBack(how);
                }

                var before = Snapshot();
                var shown = 0;
                var operatorAt = new LookingOperator(request => shown++);

                var answered = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" }, operatorAt);

                Assert.That(shown, Is.Zero, "no dialog");
                Assert.That(answered.Status, Is.EqualTo(status), answered.Json);
                Assert.That(answered.Json, Is.EqualTo(HttpResponse.ErrorBody(word)));
                Assert.That(book.Outstanding(), Has.Count.EqualTo(1), "the invitation is not spent");
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(Snapshot(), Is.EqualTo(before), "nothing was written");
                Assert.That(_log.Warnings, Has.Some.Contains("pairing on invitation " + invitation.Id)
                    .And.Contains(CertificateDigest.Shorten(target.DatabaseId)).And.Contains(why));
            }
        }

        private const string Retired = "retired";

        /// <summary>What the real form shows for this request: every label and every row.</summary>
        private static string Shown(PairingRequest request)
        {
            using (var dialog = new PairingApprovalForm(request, TimeSpan.FromMinutes(2)))
            {
                return Texts(dialog) + Rows(dialog);
            }
        }

        private const string ShownAgain = "Shown again";
        private const string StateChanged = "Shown again: the state of the profiles changed.";

        private const string AsRecordAndItem = "as a record and its item";
        private const string AsDeletionRecordAlone = "as a deletion record alone";
        private const string OnlyInTheOtherCopy = "only in the other copy";

        /// <summary>
        /// `ops` named and saved, then removed in a copy of the file, and here as well unless only
        /// the other copy removes it; the copy keeps its removal as a record with the summary entry
        /// restating it, or as its deletion record alone. The copy, for the caller to synchronize
        /// in and let go.
        /// </summary>
        private TemporaryDatabase RemovedInAnotherCopy(string how)
        {
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            _database.Save();
            var copy = TemporaryDatabase.New();
            System.IO.File.Copy(_database.Path, copy.Path, true);
            copy.Reopen();
            if (how != OnlyInTheOtherCopy)
            {
                StoreSeeds.Unname(store, "ops");
            }

            StoreSeeds.Unname(new KdbxStore(copy.Database, GateVersion.Current, null), "ops");
            if (how == AsDeletionRecordAlone)
            {
                LostButItsDeletionRecord(copy);
            }

            return copy;
        }

        /// <summary>
        /// `ops` named here, then removed in a copy of the file whose removal was deleted for good
        /// and whose summary entry was lost with no deletion record, and `reader` named here after
        /// the copy was made, so that this file keeps a summary entry of its own. The copy, for the
        /// caller to synchronize in and let go: after that, nothing in this file names `ops` but
        /// the deletion record of its removal.
        /// </summary>
        private TemporaryDatabase RemovedApartInAnotherCopy()
        {
            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            StoreSeeds.Name(store, "ops");
            foreach (var addition in new StoredAdmissions(_database.Database).Entries(StoreKinds.Profile))
            {
                // Dated a minute back before the copy, as an addition written before a removal is:
                // a Synchronize keeps an entry last changed in the second of its deletion.
                addition.LastModificationTime = DateTime.UtcNow.AddMinutes(-1);
            }

            _database.Save();
            var copy = TemporaryDatabase.New();
            System.IO.File.Copy(_database.Path, copy.Path, true);
            copy.Reopen();
            StoreSeeds.Unname(new KdbxStore(copy.Database, GateVersion.Current, null), "ops");
            LostButItsDeletionRecord(copy);
            StoreSeeds.Name(store, "reader");
            return copy;
        }

        /// <summary>
        /// The copy's removal deleted for good, as KeePass's delete leaves it, and its summary entry
        /// deleted by a program that keeps no deletion record.
        /// </summary>
        private static void LostButItsDeletionRecord(TemporaryDatabase copy)
        {
            var copied = new StoredAdmissions(copy.Database);
            var removal = copied.Entries(StoreKinds.Removed).Single();
            removal.ParentGroup.Entries.Remove(removal);
            copy.Database.DeletedObjects.Add(new PwDeletedObject(removal.Uuid, DateTime.UtcNow.AddSeconds(1)));
            var head = copied.Entries(StoreKinds.Head).Single();
            head.ParentGroup.Entries.Remove(head);
        }

        /// <summary>
        /// The copy brought in by KeePass's Synchronize, leaving the store whole and holding this
        /// many removals of `ops` once the name is known.
        /// </summary>
        private void Synchronized(TemporaryDatabase copy, int removals)
        {
            _database.Database.MergeIn(copy.Database, PwMergeMethod.Synchronize, null);
            using (var reading = new KdbxStore(_database.Database, GateVersion.Current, null).Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Has.Count.EqualTo(removals));
            }
        }

        [Test]
        public void The_dialog_is_built_from_the_request_the_handler_made_out_of_the_database()
        {
            // Every other dialog test builds its request by hand, so what the real target answers
            // for the Regaining line, `MaterialTarget.Asked`, is pinned here: an answer naming
            // nobody, everybody or the revoked would change or silence the warning with every
            // other test green.
            using (var kept = Issue("agent-kept", "ops"))
            using (var revoked = Issue("agent-revoked", "ops"))
            using (var elsewhere = Issue("agent-elsewhere", "audit"))
            {
                Assume.That(_writer.WriteAdmission(_database.Database, Record(kept, "ops")).Ok, Is.True);
                Assume.That(_writer.WriteAdmission(_database.Database, Record(revoked, "ops")).Ok, Is.True);
                Assume.That(_writer.WriteAdmission(_database.Database, Record(elsewhere, "audit")).Ok, Is.True);

                var admissions = new StoredAdmissions(_database.Database);
                Assume.That(admissions.Remove(CertificateDigest.Of(revoked)), Is.True);

                // And a holder whose certificate has expired, which regains nothing.
                using (var ended = Minted.Named("CN=agent-expired")
                           .Lasting(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1))
                           .SelfSigned()
                           .Read())
                {
                    admissions.Write(AdmissionRecord.For(ended, DateTimeOffset.UtcNow.AddDays(-30), new[] { "ops" }, StoredAdmissions.AnyIssuer));
                }

                // "ops" taken off the list, as the Profiles page does, so that its holders would
                // regain it; "audit" left on it.
                StoreSeeds.Unname(new KdbxStore(_database.Database, GateVersion.Current, null), "ops");

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
            return Pair(new PairingHandler(book, operatorAt, null, _log), target, invitation, name, profiles);
        }

        /// <summary>
        /// The same, through this handler, whose book of approvals a retry of the same request finds.
        /// </summary>
        private GateContext Pair(PairingHandler handler, IGateTarget target, Invitation invitation, string name, string[] profiles)
        {
            var token = invitation.Token;
            var binding = PairingProof.Binding(target.Host.CertificateDer);
            var nonce = new byte[SniName.NonceLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            var connection = new PairingConnection(invitation, target, binding, nonce, "127.0.0.1:60001");

            var hello = Post(
                GateOperations.PathOf(GateOperations.PairHello),
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

            var body = Bodies.Of(("name", name), ("profiles", profiles), ("proof", Convert.ToBase64String(proof)));
            var request = Post(GateOperations.PathOf(GateOperations.PairRequest), body, connection);
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

            var identity = CertificateBlob.Load(body.Text("identity"));
            Assert.That(identity, Is.Not.Null, "the bundle carries a certificate with its own key");
            return identity.Read();
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

        /// <summary>The names on the list of the database's store as it stands.</summary>
        private IReadOnlyList<string> Names()
        {
            using (var reading = new KdbxStore(_database.Database, GateVersion.Current, null).Read())
            {
                return ProfileNames.Of(reading).Names;
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

        /// <summary>A pairing's admission under the authority the database serves.</summary>
        private AdmissionRecord Record(X509Certificate2 certificate, params string[] profiles)
        {
            return AdmissionRecord.For(
                certificate, DateTimeOffset.UtcNow, profiles, StoreSeeds.Served(new StoredAdmissions(_database.Database).Store));
        }

        /// <summary>
        /// The groups of the plugin's own the tree holds, the recycle bin searched as well.
        /// </summary>
        private List<PwGroup> PluginGroups()
        {
            return _database.RootGroup.GetGroups(true)
                .Where(found => KeePassGate.Store.StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes))
                .ToList();
        }

        private GroupForm GroupDialogOnTheRoot()
        {
            var form = new GroupForm();
            form.InitEx(_database.RootGroup, false, null, _database.Database);
            return form;
        }

        /// <summary>
        /// Whether the tree holds an admission's entry for this certificate, in force or not.
        /// </summary>
        private bool Stored(string admissionKey)
        {
            return new StoredAdmissions(_database.Database).Holds("client", admissionKey);
        }

        private string Snapshot()
        {
            return new StoredAdmissions(_database.Database).Snapshot();
        }

        /// <summary>
        /// What a stale copy merged in after a revocation leaves: the admission back in the tree
        /// beside the revocation that refuses it, answering with its admission key.
        /// </summary>
        private string PlantCameBackAdmission()
        {
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                new StoredAdmissions(_database.Database).BackBesideItsRevocation(record);
                return record.AdmissionKey;
            }
        }

        /// <summary>
        /// The admission of a client certificate that ended days ago, answering with its admission
        /// key.
        /// </summary>
        /// <remarks>
        /// From an authority of its own made a week ago, because nothing can be issued from before
        /// its authority began and this database's was made a moment ago. Whose authority it is
        /// does not matter here, where its dates are what refuse it.
        /// </remarks>
        private string PlantExpiredAdmission()
        {
            var then = DateTimeOffset.UtcNow.AddDays(-7);
            var issuer = new AuthorityIssuer(() => then);

            var authorityKey = issuer.CreateAuthority();
            using (var certificate = issuer.IssueClient(authorityKey, "agent-nonexistent", new[] { "ops" }, TimeSpan.FromDays(1)).Read())
            {
                var record = AdmissionRecord.For(certificate, then, new[] { "ops" }, StoredAdmissions.AnyIssuer);
                new StoredAdmissions(_database.Database).Write(record);
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
            public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
            {
                return PairingApproval.Approved(request.Profiles);
            }
        }

        /// <summary>
        /// An operator who agrees to exactly what was asked for, once something else has happened.
        /// </summary>
        private sealed class AgreeingAfter : IPairingApproval
        {
            private readonly Action _meanwhile;

            public AgreeingAfter(Action meanwhile)
            {
                _meanwhile = meanwhile;
            }

            public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
            {
                _meanwhile();
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

            public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
            {
                _look(request);
                return PairingApproval.Refused();
            }
        }

        /// <summary>An operator whose every answer the test decides.</summary>
        private sealed class Answering : IPairingApproval
        {
            private readonly Func<PairingRequest, PairingApproval> _answer;

            public Answering(Func<PairingRequest, PairingApproval> answer)
            {
                _answer = answer;
            }

            public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
            {
                return _answer(request);
            }
        }

        /// <summary>
        /// An operator who looks at what is asked and agrees to exactly that.
        /// </summary>
        private sealed class LookingAndAgreeing : IPairingApproval
        {
            private readonly Action<PairingRequest> _look;

            public LookingAndAgreeing(Action<PairingRequest> look)
            {
                _look = look;
            }

            public PairingApproval Ask(PairingRequest request, TimeSpan timeout, TimeSpan left)
            {
                _look(request);
                return PairingApproval.Approved(request.Profiles);
            }
        }

        /// <summary>Every row of every list a dialog shows, each on a line of its own.</summary>
        private static string Rows(System.Windows.Forms.Control form)
        {
            var text = new System.Text.StringBuilder();
            foreach (var list in ControlTree.Descendants(form).OfType<System.Windows.Forms.ListBox>())
            {
                foreach (var item in list.Items)
                {
                    text.AppendLine(item.ToString());
                }
            }

            return text.ToString();
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
            using (var reading = DatabaseAuthority.Of(_database.Database, null).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, profiles).Read();
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

            public string Stalled
            {
                get { return null; }
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
            public string Stalled
            {
                get { return null; }
            }

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
