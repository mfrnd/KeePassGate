using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Utility;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The operator's standing answer to a renewal, written into a real KeePass database, saved to
    /// a real file and opened again.
    /// </summary>
    /// <remarks>
    /// The renewed record itself is written exactly as pairing writes one, which
    /// `PairingInRealDatabaseTests` follows to the file. What is new here is the key a refusal
    /// leaves when the operator means more than this once.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class RenewalInRealDatabaseTests
    {
        private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
            _failSaves = false;
            _restoreAfterSave = null;

            _writer = new DatabaseWriter(CurrentThread.Instance, Save, null, _dialogs, null);
            _database.Save();
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_standing_answer_is_in_the_file_and_not_only_in_memory()
        {
            var result = _writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(_saves, Is.EqualTo(1));

            _database.SaveCloseAndReopen();

            var standing = new StoredAdmissions(_database.Database).Answers.StandingRefusal(Key);
            Assert.That(standing, Is.Not.Null);
            Assert.That(standing.IsForever, Is.True);
        }

        [Test]
        public void A_standing_answer_that_lands_is_announced_and_rebuilds_neither_list()
        {
            // The Clients page draws the client's name red from the answer, so it is told, as it
            // is of a record; the tree and the entry list show none of it, so neither is rebuilt.
            var announced = 0;
            var rebuilt = new List<bool>();
            var writer = new DatabaseWriter(
                CurrentThread.Instance, Save, (database, lists) => rebuilt.Add(lists), () => announced++, _dialogs, null);

            Assert.That(writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever()).Ok, Is.True);
            Assert.That(announced, Is.EqualTo(1));
            Assert.That(rebuilt, Has.None.True);
        }

        [Test]
        public void A_later_standing_answer_saved_replaces_the_one_before_it_in_the_file()
        {
            var earlier = RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero));
            Assume.That(_writer.RefuseRenewal(_database.Database, Key, earlier).Ok, Is.True);

            var result = _writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

            Assert.That(result.Ok, Is.True, result.Reason);
            _database.SaveCloseAndReopen();

            var standing = new StoredAdmissions(_database.Database).Answers.StandingRefusal(Key);
            Assert.That(standing.IsForever, Is.True);
        }

        [Test]
        public void The_maintenance_dialog_open_on_the_database_refuses_it()
        {
            using (var form = new DatabaseOperationsForm())
            {
                form.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var result = _writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

                    Assert.That(result.IsBusy, Is.True, result.Reason);
                    Assert.That(_saves, Is.Zero);
                    Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RenewalRefusalKey(Key)), Is.False);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void One_no_save_kept_is_taken_back_out_and_the_answer_before_it_put_back()
        {
            var earlier = RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero));
            Assume.That(_writer.RefuseRenewal(_database.Database, Key, earlier).Ok, Is.True);
            _database.Database.Modified = false;

            var failing = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = failing.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

            Assert.That(result.IsUnsaved, Is.True, result.Reason);
            Assert.That(_database.Database.CustomData.Get(AuthorityKeys.RenewalRefusalKey(Key)), Is.EqualTo(earlier.Write()));
            Assert.That(_database.Database.Modified, Is.False);
        }

        [Test]
        public void KeePass_busy_with_something_of_its_own_refuses_a_renewal_before_the_operator_is_asked()
        {
            // A renewal through the writer the plugin runs, asked whether KeePass is interrupting
            // the way the plugin asks it: here by the count of windows KeePass is waiting on,
            // which an export raises. The real writer used to be handed nothing to ask.
            using (var renewal = new RealRenewal(this))
            {
                MessageService.ExternalIncrementMessageCount();
                try
                {
                    var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                    Assert.That(context.Status, Is.EqualTo(503), context.Json);
                    Assert.That(renewal.Asked, Is.Zero, "nobody was disturbed");
                    Assert.That(renewal.Records, Is.EqualTo(1), "and nothing was written");
                }
                finally
                {
                    MessageService.ExternalDecrementMessageCount();
                }
            }
        }

        [Test]
        public void KeePass_busy_by_the_time_the_operator_approves_writes_nothing()
        {
            using (var renewal = new RealRenewal(this))
            {
                var raised = false;
                try
                {
                    var context = renewal.Ask(
                        RenewalApproval.Approved(new[] { "ops" }),
                        () =>
                        {
                            MessageService.ExternalIncrementMessageCount();
                            raised = true;
                        });

                    Assert.That(context.Status, Is.EqualTo(503), context.Json);
                    Assert.That(renewal.Asked, Is.EqualTo(1));
                    Assert.That(context.Json, Does.Contain("heldUntil"), "the approval is held for the retry");
                    Assert.That(renewal.Records, Is.EqualTo(1), "and nothing is issued");
                }
                finally
                {
                    if (raised)
                    {
                        MessageService.ExternalDecrementMessageCount();
                    }
                }
            }
        }

        [Test]
        public void The_dialog_is_built_from_the_request_the_handler_made_out_of_the_database()
        {
            // The step from what the handler read to what the operator sees, which every dialog
            // test skipped by building its request by hand.
            using (var renewal = new RealRenewal(this))
            {
                var store = new KdbxStore(_database.Database, GateVersion.Current, null);
                StoreSeeds.Unname(store, "ops");
                StoreSeeds.Name(store, "audit");

                Assume.That(renewal.Ask(RenewalApproval.Refused(null), null).Status, Is.EqualTo(403));

                var request = renewal.LastRequest;
                Assert.That(request.Profiles, Is.Empty, "\"ops\" is off the list, so it is not offered");
                Assert.That(request.Dropped, Is.EqualTo(new[] { "ops" }));

                using (var dialog = new RenewalApprovalForm(request, TimeSpan.FromMinutes(2), null))
                {
                    var table = ProfileRows.Table(dialog, GateTab.RenewalProfilesListName);
                    Assert.That(ProfileRows.Texts(table), Is.EqualTo(new[] { new[] { "ops" } }), "the name off the list is a row of its own");
                    Assert.That(ProfileRows.Of(table)[0].AccessibleName, Is.EqualTo("ops. " + RenewalApprovalForm.OmitsTip),
                        "read as a row the new certificate omits");
                    Assert.That(ProfileRows.Boxes(table), Is.Empty, "with no box to check");
                    Assert.That(ControlTree.Descendants(dialog).Select(control => control.Text), Has.Some.EqualTo("agent-fictitious"));
                }
            }
        }

        [Test]
        public void A_revocation_landing_between_the_last_read_and_the_write_is_not_overtaken()
        {
            // The handler reads the record again after the dialog, on its own thread; a revocation
            // that lands after that read and before the write takes the writer's lock was
            // overtaken, and the revoked certificate's holder was issued a new one. The write
            // compares the record under that lock now.
            using (var renewal = new RealRenewal(this))
            {
                renewal.BeforeTheWrite = () => Assume.That(
                    new StoredAdmissions(_database.Database).Remove(renewal.Key), Is.True);

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(renewal.Records, Is.Zero, "the revoked record is all there was, and nothing was added");
                Assert.That(_saves, Is.Zero, "nothing was written");
            }
        }

        [Test]
        public void A_refusal_on_the_dialog_is_saved_as_an_approval_is_and_leaves_the_database_unmarked()
        {
            // An answer forever or until a day is written, saved and hashed back as an approval's
            // record is, so the file holds it before the client is answered and nothing is left
            // for a later save to keep.
            using (var renewal = new RealRenewal(this))
            {
                var context = renewal.Ask(RenewalApproval.Refused(RenewalRefusal.Forever()), null);

                Assert.That(context.Status, Is.EqualTo(403), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("refused")));
                Assert.That(_saves, Is.EqualTo(1), "saved with the answer");
                Assert.That(_database.Database.Modified, Is.False, "and nothing is left to save");
                Assert.That(renewal.Told, Is.Empty, "and nothing is told");
                Assert.That(renewal.Session.Refuses(renewal.Key), Is.False, "nothing is held in memory");

                var key = renewal.Key;
                _database.Reopen();

                var standing = new StoredAdmissions(_database.Database).Answers.StandingRefusal(key);
                Assert.That(standing, Is.Not.Null, "the file opened afresh holds the answer");
                Assert.That(standing.IsForever, Is.True);
            }
        }

        [TestCase("busy", TestName = "a refusal KeePass is too busy to save holds for this session and is told")]
        [TestCase("maintenance", TestName = "a refusal Database Maintenance keeps from the database holds for this session and is told")]
        [TestCase("tan wizard", TestName = "a refusal the TAN wizard keeps from a save holds for this session and is told")]
        [TestCase("replaced", TestName = "a refusal over a file another copy replaced holds for this session and is told")]
        [TestCase("unwatched", TestName = "a refusal while the dialogs are not watched holds for this session and is told")]
        [TestCase("unsaved", TestName = "a refusal whose save did not land is taken back out, holds for this session and is told")]
        public void A_refusal_that_could_not_be_saved_holds_for_this_session_and_is_told(string cause)
        {
            // Each arises once the operator has answered, under the dialog, where the questions
            // before the dialog cannot have seen it.
            using (var renewal = new RealRenewal(this))
            using (var maintenance = new DatabaseOperationsForm())
            using (var wizard = new TanWizardForm())
            {
                var earlier = RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(-1));
                Assume.That(renewal.Writer.RefuseRenewal(_database.Database, renewal.Key, earlier).Ok, Is.True);
                Assume.That(_database.Database.Modified, Is.False);
                _saves = 0;

                var raised = false;
                System.Windows.Forms.Form opened = null;
                Action meanwhile = () =>
                {
                    switch (cause)
                    {
                        case "busy":
                            MessageService.ExternalIncrementMessageCount();
                            raised = true;
                            break;
                        case "maintenance":
                            maintenance.InitEx(_database.Database);
                            GlobalWindowManager.AddWindow(maintenance);
                            opened = maintenance;
                            break;
                        case "tan wizard":
                            wizard.InitEx(_database.Database, _database.RootGroup);
                            GlobalWindowManager.AddWindow(wizard);
                            opened = wizard;
                            break;
                        case "replaced":
                            RewriteTheFileUnderneath();
                            break;
                        case "unwatched":
                            _dialogs.Dispose();
                            break;
                        case "unsaved":
                            _failSaves = true;
                            break;
                    }
                };

                GateContext context;
                try
                {
                    context = renewal.Ask(RenewalApproval.Refused(RenewalRefusal.Forever()), meanwhile);
                }
                finally
                {
                    if (raised)
                    {
                        MessageService.ExternalDecrementMessageCount();
                    }

                    if (opened != null)
                    {
                        GlobalWindowManager.RemoveWindow(opened);
                    }
                }

                Assert.That(context.Status, Is.EqualTo(403), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("refused")));
                Assert.That(_saves, Is.EqualTo(cause == "unsaved" ? 1 : 0));
                Assert.That(renewal.Standing.Write(), Is.EqualTo(earlier.Write()),
                    "the answer the database held before is what it holds");
                Assert.That(_database.Database.Modified, Is.False, "and nothing is marked");
                Assert.That(renewal.Session.Refuses(renewal.Key), Is.True, "the answer holds until KeePass exits");
                Assert.That(renewal.Told, Has.Count.EqualTo(1), "told once");
                Assert.That(renewal.Told[0], Does.StartWith("The answer to the renewal was not written: "));
                Assert.That(renewal.Told[0], Does.EndWith("." + Environment.NewLine + "Renewal stays refused until KeePass exits."));

                _failSaves = false;
                var again = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(again.Status, Is.EqualTo(409), again.Json);
                Assert.That(again.Json, Is.EqualTo(HttpResponse.ErrorBody("this session")));
                Assert.That(renewal.Asked, Is.EqualTo(1), "answered without the dialog");
                Assert.That(renewal.Told, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void A_refusal_whose_database_closed_under_the_dialog_holds_for_this_session_and_is_told()
        {
            using (var renewal = new RealRenewal(this))
            {
                var context = renewal.Ask(
                    RenewalApproval.Refused(RenewalRefusal.Forever()), () => _database.Database.Close());

                Assert.That(context.Status, Is.EqualTo(403), context.Json);
                Assert.That(_saves, Is.Zero);
                Assert.That(renewal.Session.Refuses(renewal.Key), Is.True);
                Assert.That(renewal.Told, Has.Count.EqualTo(1));
                Assert.That(renewal.Told[0], Does.StartWith("The answer to the renewal was not written: "));
            }
        }

        [Test]
        public void A_refusal_whose_file_did_not_verify_stays_in_force_and_marked_and_is_told()
        {
            // The save ran and the file does not hold what KeePass wrote, which tells nothing of
            // which of the two the file holds: the answer stays in the open database, in force,
            // with the database marked, and the operator is told the save was not confirmed.
            using (var renewal = new RealRenewal(this))
            {
                _database.Save();
                _restoreAfterSave = System.IO.File.ReadAllBytes(_database.Path);

                var context = renewal.Ask(RenewalApproval.Refused(RenewalRefusal.Forever()), null);
                _restoreAfterSave = null;

                Assert.That(context.Status, Is.EqualTo(403), context.Json);
                Assert.That(renewal.Standing, Is.Not.Null, "the answer stays in the open database");
                Assert.That(renewal.Standing.IsForever, Is.True);
                Assert.That(_database.Database.Modified, Is.True, "marked, so KeePass asks before it is thrown away");
                Assert.That(renewal.Session.Refuses(renewal.Key), Is.False, "nothing is held beside it");
                Assert.That(renewal.Told, Is.EqualTo(new[]
                {
                    "The save of the answer to the renewal could not be confirmed: the file on disk does not hold " +
                    "what KeePass wrote to it.",
                }));

                var again = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(again.Json, Is.EqualTo(HttpResponse.ErrorBody("forever")));
                Assert.That(renewal.Asked, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Changes the bytes of the database's file from outside KeePass, as another copy saved
        /// over it would, so that the file is not the file KeePass read.
        /// </summary>
        private void RewriteTheFileUnderneath()
        {
            var bytes = System.IO.File.ReadAllBytes(_database.Path);
            bytes[bytes.Length - 1] ^= 0xFF;
            System.IO.File.WriteAllBytes(_database.Path, bytes);
        }

        [Test]
        public void A_renewal_whose_store_stops_before_its_write_is_answered_stopped_with_nothing_written()
        {
            // The store asked again in the write's own turn, `STORE.md` §627, as for a pairing.
            using (var renewal = new RealRenewal(this))
            {
                string before = null;
                renewal.BeforeTheWrite = () =>
                {
                    StoreSeeds.Revoke(new StoredAdmissions(_database.Database).Store, new string('e', 64));
                    var head = _database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "head");
                    head.ParentGroup.Entries.Remove(head);
                    _database.Database.DeletedObjects.Add(new PwDeletedObject(head.Uuid, DateTime.UtcNow));
                    before = new StoredAdmissions(_database.Database).Snapshot();
                };

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("stopped")));
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(new StoredAdmissions(_database.Database).Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void A_renewal_whose_database_comes_to_hold_a_second_authority_before_its_write_is_answered_undecided()
        {
            // The store asked again in the write's own turn, and answered as a connection to the
            // database is answered once a second authority stands beside the first.
            using (var renewal = new RealRenewal(this))
            {
                string before = null;
                renewal.BeforeTheWrite = () =>
                {
                    new StoredAdmissions(_database.Database).HoldBack(HeldBackTarget.Undecided);
                    before = new StoredAdmissions(_database.Database).Snapshot();
                };

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(HeldBackTarget.Undecided)));
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(new StoredAdmissions(_database.Database).Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void A_renewal_whose_authority_is_retired_beside_several_others_before_its_write_is_answered_unpaired()
        {
            // Beside the case of a second authority arriving: the authority the renewal was
            // approved under is retired and two others stand, which no decision of the operator's
            // can make serve this client, so the plan refuses the write and it answers as for an
            // authority that went, with nothing written.
            using (var renewal = new RealRenewal(this))
            {
                string before = null;
                renewal.BeforeTheWrite = () =>
                {
                    new StoredAdmissions(_database.Database).RetiredBesideOthers();
                    before = new StoredAdmissions(_database.Database).Snapshot();
                };

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(new StoredAdmissions(_database.Database).Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void A_renewal_whose_authority_is_retired_beside_several_others_while_the_dialog_stands_is_answered_unpaired()
        {
            using (var renewal = new RealRenewal(this))
            {
                string before = null;
                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), () =>
                {
                    new StoredAdmissions(_database.Database).RetiredBesideOthers();
                    before = new StoredAdmissions(_database.Database).Snapshot();
                });

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
                Assert.That(_saves, Is.Zero, "nothing was issued or written, so nothing was saved");
                Assert.That(new StoredAdmissions(_database.Database).Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void A_renewal_arriving_once_its_authority_was_retired_beside_several_others_is_answered_unpaired_before_the_dialog()
        {
            using (var renewal = new RealRenewal(this))
            {
                new StoredAdmissions(_database.Database).RetiredBesideOthers();

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
                Assert.That(renewal.Asked, Is.Zero, "the operator is asked nothing");
            }
        }

        [Test]
        public void A_renewal_arriving_once_a_second_authority_stands_is_answered_undecided_before_the_dialog()
        {
            using (var renewal = new RealRenewal(this))
            {
                new StoredAdmissions(_database.Database).HoldBack(HeldBackTarget.Undecided);

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(HeldBackTarget.Undecided)));
                Assert.That(renewal.Asked, Is.Zero, "the operator is asked nothing");
            }
        }

        [Test]
        public void A_renewal_whose_store_comes_to_hold_the_gate_back_while_the_dialog_stands_is_answered_its_word()
        {
            // Read again after the dialog, before anything is issued: a store holding the gate back
            // then is the store's word and not a record that went, so the client is told to wait
            // rather than that its certificate is refused.
            AnsweredAfterTheDialog(StoreWords.Stopped);
        }

        [Test]
        public void A_renewal_whose_database_comes_to_hold_a_second_authority_while_the_dialog_stands_is_answered_undecided()
        {
            // The same where a second authority comes to stand beside the first, which serves
            // nothing until the operator keeps an authority.
            AnsweredAfterTheDialog(HeldBackTarget.Undecided);
        }

        /// <summary>
        /// A renewal whose database is brought, while its dialog stands, to be answered with this
        /// word, and what came of it.
        /// </summary>
        private void AnsweredAfterTheDialog(string word)
        {
            using (var renewal = new RealRenewal(this))
            {
                string before = null;
                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), () =>
                {
                    new StoredAdmissions(_database.Database).HoldBack(word);
                    before = new StoredAdmissions(_database.Database).Snapshot();
                });

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(word)));
                Assert.That(_saves, Is.Zero, "nothing was issued or written, so nothing was saved");
                Assert.That(new StoredAdmissions(_database.Database).Snapshot(), Is.EqualTo(before));
            }
        }

        [Test]
        public void After_the_dialog_the_word_and_the_record_are_read_from_a_single_reading()
        {
            // The store stops the moment the first hold after the dialog ends. Read in two holds,
            // the word would be of a store that served and the record of a store that serves
            // nothing, and the renewal would be told its record went. Read in a single hold, both
            // are of the same store, and the write that follows meets the stopped store and
            // answers its word.
            var hold = new StopsAfterAHold();
            using (var renewal = new RealRenewal(this, hold))
            {
                hold.Between = () =>
                {
                    var head = _database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "head");
                    head.ParentGroup.Entries.Remove(head);
                    _database.Database.DeletedObjects.Add(new PwDeletedObject(head.Uuid, DateTime.UtcNow));
                };

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), () => hold.Armed = true);

                Assert.That(hold.Armed, Is.False, "the store stopped between two holds");
                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(StoreWords.Stopped)));
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
            }
        }

        [Test]
        public void The_dialog_offers_the_names_read_in_the_hold_that_read_the_admission_it_renews()
        {
            // The names are read with the admission, from the same reading. Taken off the list once
            // that hold has ended, a name is still offered as the reading had it, and the write,
            // which reads the list again in its own turn, finds the list is not what the dialog
            // showed: nothing is written, and the operator is asked again.
            var hold = new StopsAfterAHold();
            using (var renewal = new RealRenewal(this, hold))
            {
                var store = new KdbxStore(_database.Database, GateVersion.Current, null);
                using (var reading = store.Read())
                {
                    Assume.That(ProfileNames.Of(reading).Holds("ops"), Is.True, "the admission's act added the name");
                }

                hold.Between = () => StoreSeeds.Unname(store, "ops");
                hold.Armed = true;

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(hold.Armed, Is.False, "the name went between two holds");
                Assert.That(renewal.LastRequest.Profiles, Is.EqualTo(new[] { "ops" }), "the names of the admission's own reading");
                Assert.That(renewal.LastRequest.Dropped, Is.Empty);
                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
            }
        }

        [Test]
        public void A_renewal_arriving_over_a_store_that_holds_the_gate_back_is_answered_its_word_before_the_dialog()
        {
            using (var renewal = new RealRenewal(this))
            {
                new StoredAdmissions(_database.Database).HoldBack(StoreWords.Stopped);

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(503), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(StoreWords.Stopped)));
                Assert.That(renewal.Asked, Is.Zero, "the operator is asked nothing");
            }
        }

        [Test]
        public void A_renewal_whose_authority_is_recreated_before_its_write_is_answered_unpaired_with_nothing_written()
        {
            // A Recreate landing between the operator's answer and the write retires the authority
            // the renewal was approved under. The write's plan leaves the admission out before
            // anything is asked or spent, and the renewal is answered as a renewal whose record
            // went: no retry under that authority can be written, so a `503` would invite a retry
            // that cannot work.
            using (var renewal = new RealRenewal(this))
            {
                string before = null;
                renewal.BeforeTheWrite = () =>
                {
                    var recreated = DatabaseAuthority.Of(_database.Database, null).Recreate();
                    Assume.That(recreated.Succeeded, Is.True, recreated.Message);
                    before = new StoredAdmissions(_database.Database).Snapshot();
                };

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("unpaired")));
                Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                Assert.That(new StoredAdmissions(_database.Database).Snapshot(), Is.EqualTo(before));
            }
        }

        private bool _failSaves;

        /// <summary>The file as it was, put back after a save, which the writer then cannot read back.</summary>
        private byte[] _restoreAfterSave;

        /// <summary>A save as the writer is handed it: a save that does not land writes nothing.</summary>
        private void Save(PwDatabase database)
        {
            _saves++;
            if (!_failSaves)
            {
                database.Save(null);
            }

            if (_restoreAfterSave != null)
            {
                System.IO.File.WriteAllBytes(_database.Path, _restoreAfterSave);
            }
        }

        /// <summary>
        /// Holds on the calling thread, each outermost hold under a stamp of its own, and runs
        /// <see cref="Between"/> once, as the first outermost hold to end once armed ends.
        /// </summary>
        private sealed class StopsAfterAHold : IReadHold
        {
            private long _next;
            private long _stamp;
            private int _depth;

            public bool Armed { get; set; }

            public Action Between { get; set; }

            public long Stamp
            {
                get { return _depth > 0 ? _stamp : 0; }
            }

            public T Hold<T>(Func<T> read)
            {
                if (_depth > 0)
                {
                    return read();
                }

                _depth++;
                _stamp = ++_next;
                try
                {
                    return read();
                }
                finally
                {
                    _depth--;
                    if (Armed)
                    {
                        Armed = false;
                        Between();
                    }
                }
            }
        }

        /// <summary>
        /// A renewal of an admitted client through the targets and the writer the plugin builds,
        /// with the writer asking KeePass's count of the windows it is waiting on.
        /// </summary>
        private sealed class RealRenewal : IDisposable
        {
            private readonly RenewalInRealDatabaseTests _test;
            private readonly OpenDatabaseTargets _targets;
            private readonly IGateTarget _target;
            private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _client;
            private readonly string _key;

            public RealRenewal(RenewalInRealDatabaseTests test)
                : this(test, CurrentThread.Instance)
            {
            }

            /// <summary>The same, the targets reading the database in the holds given.</summary>
            public RealRenewal(RenewalInRealDatabaseTests test, IReadHold hold)
            {
                _test = test;
                var database = test._database.Database;
                var created = DatabaseAuthority.Of(database, null).Create();
                Assume.That(created.Succeeded, Is.True, created.Message);

                var writer = new DatabaseWriter(
                    new Hooked(this),
                    test.Save,
                    () => MessageService.CurrentMessageCount != 0 ? "KeePass is waiting for an answer of its own" : null,
                    null,
                    null,
                    test._dialogs,
                    null);

                var databases = new List<PwDatabase> { database };
                Writer = writer;
                _targets = new OpenDatabaseTargets(delegate { return databases; }, writer, hold, null);
                _target = _targets.Current().Single();

                _client = new AuthorityIssuer().IssueClient(_target.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read();
                var record = AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, _target.DatabaseId);
                Assume.That(writer.WriteAdmission(database, record).Ok, Is.True);
                _key = record.AdmissionKey;
                test._saves = 0;
            }

            public int Asked { get; private set; }

            /// <summary>The plugin's memory of standing answers, as the plugin wires it to the writer.</summary>
            private readonly SessionRefusals _session = new SessionRefusals();

            /// <summary>The writer the plugin would build, for a test that writes something else.</summary>
            public DatabaseWriter Writer { get; private set; }

            public SessionRefusals Session
            {
                get { return _session; }
            }

            /// <summary>What the operator was last shown.</summary>
            public RenewalRequest LastRequest { get; private set; }

            /// <summary>What the operator was told of answers not kept as given, in order.</summary>
            public List<string> Told { get; } = new List<string>();

            /// <summary>Run once, on the next write's turn, before the write itself.</summary>
            public Action BeforeTheWrite { get; set; }

            public string Key
            {
                get { return _key; }
            }

            public string DatabaseId
            {
                get { return _target.DatabaseId; }
            }

            public int Records
            {
                get { return new StoredAdmissions(_test._database.Database).All().Records.Count; }
            }

            public RenewalRefusal Standing
            {
                get { return new StoredAdmissions(_test._database.Database).Answers.StandingRefusal(_key); }
            }

            public GateContext Ask(RenewalApproval answer, Action meanwhile)
            {
                var head = new HttpRequestHead(
                    "POST",
                    RenewalHandler.Path,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    2);

                var context = new GateContext(
                    head, "{}", ClientIdentity.Paired(_target.DatabaseId, _client, new[] { "ops" }));
                new ServedDatabase(null, _target).PutOn(context);
                ConnectionPeer.PutOn(context, "127.0.0.1:60001");

                var handler = new RenewalHandler(new Operator(this, answer, meanwhile), _session, null, null);
                handler.Renew(context);
                return context;
            }

            public void Dispose()
            {
                _target.Dispose();
                _targets.Dispose();
                _client.Reset();
            }

            /// <summary>KeePass's thread as the writer sees it, with a moment in front of the next write.</summary>
            private sealed class Hooked : IUiThread
            {
                private readonly RealRenewal _renewal;

                public Hooked(RealRenewal renewal)
                {
                    _renewal = renewal;
                }

                public string Stalled
                {
                    get { return null; }
                }

                public bool Run(Action work, TimeSpan timeout)
                {
                    var before = _renewal.BeforeTheWrite;
                    _renewal.BeforeTheWrite = null;
                    if (before != null)
                    {
                        before();
                    }

                    work();
                    return true;
                }
            }

            private sealed class Operator : IRenewalApproval
            {
                private readonly RealRenewal _renewal;
                private readonly RenewalApproval _answer;
                private readonly Action _meanwhile;

                public Operator(RealRenewal renewal, RenewalApproval answer, Action meanwhile)
                {
                    _renewal = renewal;
                    _answer = answer;
                    _meanwhile = meanwhile;
                }

                public RenewalApproval Ask(RenewalRequest request, TimeSpan timeout, TimeSpan left)
                {
                    _renewal.Asked++;
                    _renewal.LastRequest = request;
                    if (_meanwhile != null)
                    {
                        _meanwhile();
                    }

                    return _answer;
                }

                public void Tell(string news)
                {
                    _renewal.Told.Add(news);
                }
            }
        }
    }
}
