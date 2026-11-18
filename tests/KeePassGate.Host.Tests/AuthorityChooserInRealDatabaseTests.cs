using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
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
    /// Asking the operator which authority to keep, and writing the answer, against a real
    /// database and KeePass's own dialogs.
    /// </summary>
    /// <remarks>
    /// The dialog itself is answered by the test rather than put on a screen: what is under test
    /// here is when it is asked, how often, and what the answer does to the database.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class AuthorityChooserInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private OpenDatabaseTargets _targets;
        private Form _owner;
        private TellingPrompt _prompt;
        private AuthorityChooser _chooser;
        private string[] _candidates;
        private int _saves;
        private int _asked;
        private string _answer;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _candidates = Undecided(_database);
            _database.Save();

            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _dialogs.Start();

            _saves = 0;
            _writer = new DatabaseWriter(CurrentThread.Instance, database => _saves++, null, _dialogs, NullGateLog.Instance);
            _targets = new OpenDatabaseTargets(
                delegate { return new List<PwDatabase> { _database.Database }; }, _writer, NullGateLog.Instance);

            _owner = new Form();
            _prompt = new TellingPrompt();
            _chooser = new AuthorityChooser(
                new NoEvents(), _targets, _writer, _owner, null, _prompt, null, NullGateLog.Instance);

            _asked = 0;
            _answer = null;
            _chooser.After = Waited;
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                _asked++;
                Assert.That(choice.Options.Select(o => o.Fingerprint), Is.EquivalentTo(_candidates));
                return _answer;
            };
        }

        [TearDown]
        public void TearDown()
        {
            _chooser.Dispose();
            _owner.Dispose();
            _targets.Dispose();
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_choice_is_written_marked_and_not_saved_and_serving_starts_at_once()
        {
            _answer = _candidates[0];
            _database.Database.Modified = false;
            var loser = EntryOf(_candidates[1]);

            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_saves, Is.Zero, "the operator saves, as they would any other change");
            Assert.That(_database.Database.Modified, Is.True, "and KeePass knows there is something to save");
            Assert.That(Retired(_candidates[1]), Is.True);
            Assert.That(_database.RootGroup.FindEntry(loser, true), Is.Null,
                "the entry of the authority not kept is deleted in the same write");
            Assert.That(_database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(loser)), Is.True,
                "with a deletion record");

            using (var served = _targets.Current().Single())
            {
                Assert.That(served.DatabaseId, Is.EqualTo(_candidates[0]));
                Assert.That(served.Unavailable, Is.Null);
            }
        }

        [Test]
        public void A_database_decided_before_it_was_asked_about_asks_nobody_and_logs_no_choice()
        {
            // The tab, or another save, can decide it between the notice and the question.
            var log = new LinesLog();
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, _writer, _owner, null, _prompt, null, log);
            chooser.AskWhichToKeep = (choice, name) =>
            {
                _asked++;
                return null;
            };

            using (chooser)
            {
                Assume.That(
                    DatabaseAuthority.Of(_database.Database, NullGateLog.Instance)
                        .Keep(_candidates[0], _candidates).Succeeded,
                    Is.True);

                Assert.That(chooser.Ask(_database.Database), Is.Null);
            }

            Assert.That(_asked, Is.Zero, "nobody was asked");
            Assert.That(log.Lines, Has.None.Contains("kept no authority"),
                "and the log does not credit the operator with a choice nobody put to them");
        }

        [Test]
        public void A_lock_closes_the_question_and_an_answer_given_as_it_lands_is_not_written()
        {
            // The chooser is not registered with KeePass's window manager, so KeePass locks under
            // it, and it went on listing every authority's clients over a locked KeePass.
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                _asked++;
                ((IApprovalScreen)_chooser).Locked();
                return _candidates[0];
            };

            Assert.That(_chooser.Ask(_database.Database), Is.Null);
            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(Retired(_candidates[1]), Is.False,
                "nothing was kept");
            Assert.That(_prompt.Told, Is.Null);

            ((IApprovalScreen)_chooser).Locked();
            Assert.That(_chooser.Ask(_database.Database), Is.Null, "a lock with no question standing closes nothing");
        }

        [Test]
        public void A_choice_its_database_closed_under_is_not_sent_back_to_the_tab()
        {
            // The unlock asks again by itself, so a box sending the operator to the settings tab
            // was about a question already on its way back.
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                _asked++;
                _database.Database.Close();
                return _candidates[0];
            };

            _chooser.Ask(_database.Database);

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_prompt.Told, Is.Null);
        }

        [Test]
        public void Cancel_serves_nothing_and_is_not_asked_again_for_the_same_authorities()
        {
            _chooser.AskAll();
            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(1), "once per state: the tab is where it is asked again");
            Assert.That(_targets.Current().All(t => t.Unavailable == Gate.HeldBackTarget.Undecided), Is.True);
        }

        [Test]
        public void A_different_set_of_authorities_is_asked_about_again()
        {
            _chooser.AskAll();

            Third();

            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(2));
        }

        [Test]
        public void An_answer_retires_no_authority_that_arrived_while_the_question_stood()
        {
            // The chooser is not registered with KeePass's window manager, so a periodic trigger
            // synchronizes under it. The answer was written to whatever the database held by
            // then, and retired an authority, with every client it admitted, that the question
            // never listed.
            var first = _candidates[0];
            var second = _candidates[1];
            string third = null;
            var listed = new List<string[]>();
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                listed.Add(choice.Options.Select(o => o.Fingerprint).ToArray());
                if (listed.Count == 1)
                {
                    // The synchronize, and the round its save posts, which the dialog's own loop
                    // delivers.
                    third = Third();
                    _chooser.AskAll();
                }

                return first;
            };

            _chooser.AskAll();

            Assert.That(listed.Count, Is.EqualTo(1), "nothing is asked on top of the question standing");
            Assert.That(Retired(third), Is.False,
                "the authority nobody was shown is not retired");
            Assert.That(Retired(second), Is.False,
                "and nothing else is, since the answer was about a database that is no longer there");
            Assert.That(_prompt.Told, Is.Null, "the operator is asked again rather than sent to the tab");

            // The round a moment later.
            _chooser.AskAll();

            Assert.That(listed.Count, Is.EqualTo(2), "the authorities the database holds now are asked about");
            Assert.That(listed[1], Is.EquivalentTo(new[] { first, second, third }));
            Assert.That(Retired(second), Is.True);
            Assert.That(Retired(third), Is.True,
                "and an answer about all of them retires the rest");

            using (var served = _targets.Current().Single())
            {
                Assert.That(served.DatabaseId, Is.EqualTo(first));
            }
        }

        [Test]
        public void An_authority_that_arrived_under_a_question_canceled_is_asked_about()
        {
            // The round for the new set came while the question about the old set stood, asked
            // nobody, and left the new set marked as asked about.
            var listed = new List<int>();
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                listed.Add(choice.Options.Count);
                if (listed.Count == 1)
                {
                    Third();
                    _chooser.AskAll();
                }

                return null;
            };

            _chooser.AskAll();
            _chooser.AskAll();
            _chooser.AskAll();

            Assert.That(listed, Is.EqualTo(new[] { 2, 3 }), "each set once, the set that arrived among them");
        }

        [Test]
        public void An_answer_is_not_written_once_an_authority_listed_has_been_retired_under_it()
        {
            // A retirement that arrives is another copy's answer, so the answer here was given
            // about a choice that has changed, and is asked for again.
            var third = Third();
            var first = _candidates[0];
            var second = _candidates[1];
            var listed = new List<string[]>();
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                listed.Add(choice.Options.Select(o => o.Fingerprint).ToArray());
                if (listed.Count == 1)
                {
                    StoreSeeds.Retire(Store(), third);
                    _chooser.AskAll();
                }

                return listed.Count == 1 ? first : null;
            };

            _chooser.AskAll();

            Assert.That(Retired(second), Is.False);
            Assert.That(_prompt.Told, Is.Null);

            _chooser.AskAll();

            Assert.That(listed.Count, Is.EqualTo(2));
            Assert.That(listed[1], Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public void A_choice_being_tried_again_is_not_written_once_another_authority_has_arrived()
        {
            // The same answer, refused for a moment and tried again: the synchronize that brings
            // the third authority can end between the two tries.
            var first = _candidates[0];
            var second = _candidates[1];
            string third = null;
            var questions = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { },
                () =>
                {
                    // The round's own question, then the first try, refused, then the second.
                    if (++questions != 2)
                    {
                        return null;
                    }

                    third = Third();
                    return "KeePass is synchronizing";
                },
                null,
                null,
                _dialogs,
                NullGateLog.Instance);
            var listed = new List<int>();
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, writer, _owner, null, _prompt, null, NullGateLog.Instance)
            {
                After = Waited,
                AskWhichToKeep = (choice, name) =>
                {
                    listed.Add(choice.Options.Count);
                    return listed.Count == 1 ? first : null;
                }
            };

            using (chooser)
            {
                chooser.AskAll();

                Assert.That(third, Is.Not.Null, "the first try was refused");
                Assert.That(Retired(third), Is.False);
                Assert.That(Retired(second), Is.False);
                Assert.That(_prompt.Told, Is.Null);

                chooser.AskAll();

                Assert.That(listed, Is.EqualTo(new[] { 2, 3 }));
            }
        }

        [Test]
        public void A_choice_another_copy_s_answer_made_moot_is_not_tried_again_and_names_what_is_served()
        {
            // While the question stood, a synchronize brought the retirement another copy wrote,
            // so the database serves what that copy kept. Tried again like any refusal, the
            // answer here was refused every quarter of a second, each try logged as an error,
            // and then reported as nothing being served, over a database being served.
            var log = new LinesLog();
            var tries = 0;
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, _writer, _owner, null, _prompt, null, log)
            {
                After = (delay, work) =>
                {
                    tries++;
                    Waited(delay, work);
                },
                AskWhichToKeep = (choice, name) =>
                {
                    StoreSeeds.Retire(Store(), _candidates[0]);
                    return _candidates[0];
                }
            };

            using (chooser)
            {
                chooser.AskAll();
            }

            Assert.That(tries, Is.Zero, "an answer no later try can make right is not tried again");
            Assert.That(log.Lines, Has.None.Contains("could not be kept"), "nor logged as an error");
            Assert.That(_prompt.Told, Does.Contain(AuthorityKeys.Shown(_candidates[1])),
                "the operator is told which authority is served");
            Assert.That(_prompt.Told, Does.Not.Contain("Nothing is served"));

            using (var served = _targets.Current().Single())
            {
                Assert.That(served.DatabaseId, Is.EqualTo(_candidates[1]), "the other copy's answer stands");
            }
        }

        [Test]
        public void A_choice_the_database_came_to_serve_alone_tells_nothing()
        {
            // The other copy kept the same authority, so the database is as the operator asked.
            var tries = 0;
            _chooser.After = (delay, work) =>
            {
                tries++;
                Waited(delay, work);
            };
            _chooser.AskWhichToKeep = (choice, name) =>
            {
                StoreSeeds.Retire(Store(), _candidates[1]);
                return _candidates[0];
            };

            _chooser.AskAll();
            _chooser.AskAll();

            Assert.That(tries, Is.Zero);
            Assert.That(_prompt.Told, Is.Null);

            using (var served = _targets.Current().Single())
            {
                Assert.That(served.DatabaseId, Is.EqualTo(_candidates[0]));
            }
        }

        [Test]
        public void A_defect_asking_about_one_database_leaves_the_others_to_be_asked()
        {
            // B167. Every database a round would ask about is marked as asked before the first
            // question, and a defect asking about the first left each database after it marked and
            // never asked until a lock and an unlock. Nothing catches the defect: it reaches
            // `Unattended`, which counts it. The next round, a moment later, asks about the rest.
            using (var second = TemporaryDatabase.New())
            {
                Undecided(second);
                second.Save();

                var targets = new OpenDatabaseTargets(
                    delegate { return new List<PwDatabase> { _database.Database, second.Database }; }, _writer,
                    NullGateLog.Instance);
                var asked = 0;
                var chooser = new AuthorityChooser(
                    new NoEvents(), targets, _writer, _owner, null, _prompt, null, NullGateLog.Instance)
                {
                    After = Waited,
                    AskWhichToKeep = (choice, name) =>
                    {
                        if (++asked == 1)
                        {
                            throw new InvalidOperationException("a defect asking about the first database");
                        }

                        return null;
                    }
                };

                using (targets)
                using (chooser)
                {
                    Assert.Throws<InvalidOperationException>(() => chooser.AskAll(), "not caught on the way");

                    chooser.AskAll();
                    Assert.That(asked, Is.EqualTo(2), "the database the failed round never reached is asked");

                    chooser.AskAll();
                    Assert.That(asked, Is.EqualTo(2), "and each database once, the database that failed among them");
                }
            }
        }

        [Test]
        public void Database_Maintenance_open_lets_the_choice_be_written()
        {
            // `STORE.md` §626: that dialog holds a copy of the custom data and no entry, and a
            // choice writes no custom data, so nothing of it is taken back.
            _answer = _candidates[0];

            using (var form = new DatabaseOperationsForm())
            {
                form.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    _chooser.AskAll();
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_prompt.Told, Is.Null);
            Assert.That(Retired(_candidates[1]), Is.True);
        }

        [Test]
        public void An_entry_dialog_on_the_authority_retired_refuses_the_choice_and_the_operator_is_told()
        {
            // The dialog puts its copy of the entry back at its OK, over the entry the choice
            // deletes. Refused, the operator is told, and nothing is written.
            _answer = _candidates[0];
            var loser = _database.RootGroup.FindEntry(EntryOf(_candidates[1]), true);

            using (var icons = new ImageList())
            using (var form = new PwEntryForm())
            {
                form.InitEx(loser, PwEditMode.EditExistingEntry, _database.Database, icons, false, false);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    _chooser.AskAll();
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_prompt.Told, Is.Not.Null.And.Contains("was not kept"));
            Assert.That(Retired(_candidates[1]), Is.False);
            Assert.That(_database.RootGroup.FindEntry(EntryOf(_candidates[1]), true), Is.Not.Null, "and no entry is deleted");
        }

        [Test]
        public void A_choice_the_writer_refuses_for_a_moment_is_kept_without_asking_again()
        {
            // Another client's write, or KeePass saving: over in a moment, and not something the
            // operator should have to choose again for. Tried again for a few seconds first. It
            // begins once the question is answered, since the question waits for a KeePass that
            // is already busy.
            var refusals = 2;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { },
                () => _asked > 0 && refusals-- > 0 ? "KeePass is saving" : null,
                null,
                null,
                _dialogs,
                NullGateLog.Instance);
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, writer, _owner, null, _prompt, null, NullGateLog.Instance)
            {
                After = Waited,
                AskWhichToKeep = (choice, name) =>
                {
                    _asked++;
                    return _candidates[0];
                }
            };

            using (chooser)
            {
                chooser.AskAll();
            }

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_prompt.Told, Is.Null, "the operator is not told about a refusal that cleared");
            Assert.That(Retired(_candidates[1]), Is.True);
        }

        [Test]
        public void A_choice_given_up_is_told_once_KeePass_is_no_longer_part_way_through_something()
        {
            // The message came from the tick on which the bound passed, which KeePass's own
            // pump delivers inside a save or a synchronize: the box stood inside that operation,
            // and a save waited under it for as long as it was left.
            var busy = false;
            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => { }, () => busy ? "KeePass is saving" : null, null, null,
                _dialogs, NullGateLog.Instance);

            bool? toldWhileBusy = null;
            _prompt.Telling = () => toldWhileBusy = busy;

            var waited = TimeSpan.Zero;
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, writer, _owner, null, _prompt, null, NullGateLog.Instance)
            {
                AskWhichToKeep = (choice, name) =>
                {
                    // KeePass starts a save as the question is answered.
                    busy = true;
                    return _candidates[0];
                },
                After = (delay, work) =>
                {
                    Thread.Sleep(delay);
                    waited += delay;

                    // And is done with it a second after the bound has passed.
                    if (waited > AuthorityChooser.KeepTrying + TimeSpan.FromSeconds(1))
                    {
                        busy = false;
                    }

                    work();
                }
            };

            using (chooser)
            {
                chooser.AskAll();
            }

            Assert.That(_prompt.Told, Is.Not.Null.And.Contains("was not kept"));
            Assert.That(toldWhileBusy, Is.False, "the message stood inside an operation of KeePass's own");
            Assert.That(Retired(_candidates[1]), Is.False,
                "and the answer given up when the bound passed is not written afterwards");
        }

        [Test]
        public void A_retry_still_waiting_when_the_plugin_is_let_go_never_runs()
        {
            // The retries above wait on timers of KeePass's thread. One still waiting when KeePass
            // let the plugin go ticked at the next pump, tried to keep the authority for a plugin
            // that had gone and told the operator it was not kept. KeePass is busy from the moment
            // the question is answered, since the question waits for a KeePass already busy.
            var tries = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => { }, () =>
                {
                    if (_asked == 0)
                    {
                        return null;
                    }

                    tries++;
                    return "KeePass is saving";
                }, null, null, _dialogs, NullGateLog.Instance);

            using (var owner = new Form())
            {
                Assume.That(owner.Handle, Is.Not.EqualTo(IntPtr.Zero), "the retries post onto this window");

                // Disposed twice, by the test and by the `using`, which a dispose has to take.
                using (var chooser = new AuthorityChooser(
                           new NoEvents(), _targets, writer, owner, null, _prompt, null, NullGateLog.Instance)
                       {
                           AskWhichToKeep = (choice, name) =>
                           {
                               _asked++;
                               return _candidates[0];
                           }
                       })
                {
                    chooser.AskAll();
                    Assume.That(tries, Is.EqualTo(1), "the first try was refused, which leaves a retry waiting");

                    chooser.Dispose();

                    var until = DateTime.UtcNow + AuthorityChooser.KeepEvery + AuthorityChooser.KeepEvery;
                    while (DateTime.UtcNow < until)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    Assert.That(tries, Is.EqualTo(1), "a retry ran after the chooser was disposed");
                    Assert.That(_prompt.Told, Is.Null, "and the operator was told about a plugin that had gone");
                }
            }
        }

        [Test]
        public void KeePass_busy_with_something_of_its_own_is_waited_for_rather_than_asked_through()
        {
            // The writer's own question, which is what the answer would be refused on.
            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => { }, () => "KeePass is synchronizing", null, null, _dialogs,
                NullGateLog.Instance);
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, writer, _owner, null, _prompt, null, NullGateLog.Instance);
            chooser.AskWhichToKeep = (choice, name) =>
            {
                _asked++;
                return null;
            };

            using (chooser)
            {
                chooser.AskAll();
            }

            Assert.That(_asked, Is.Zero);
        }

        [Test]
        public void KeePass_s_progress_window_is_waited_for_rather_than_asked_under()
        {
            // A synchronize shown in KeePass's status dialog pumps with that window open before
            // it blocks interaction, and the writer refuses every choice while the window stands:
            // the question was put over a synchronize that had not merged, for an answer that
            // could not be written.
            _answer = _candidates[0];

            using (var window = new StatusProgressForm())
            {
                window.InitEx("Synchronizing", true, false, null);
                GlobalWindowManager.AddWindow(window);

                try
                {
                    _chooser.AskAll();

                    Assert.That(_asked, Is.Zero, "nobody is asked while the answer would be refused");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(window);
                }
            }

            // The round a moment later, with the synchronize over.
            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_prompt.Told, Is.Null);
            Assert.That(Retired(_candidates[1]), Is.True);
        }

        [Test]
        public void A_merge_is_waited_for_rather_than_asked_inside()
        {
            // A merge another plugin runs pumps from its status logger and raises neither of
            // KeePass's signals nor its progress window. The round waiting was delivered there,
            // the question was put over a tree half merged, and the answer was written inside
            // the merge that could have been bringing another copy's.
            _writer.Merging = KeePassMerges.Watch();
            Assume.That((object)_writer.Merging, Is.Not.Null, "this KeePass has no merge to look for");
            _answer = _candidates[0];

            var rounds = Merged(() => _chooser.AskAll());

            Assert.That(rounds, Is.GreaterThan(0), "the merge came back to its logger, where it pumps");
            Assert.That(_asked, Is.Zero, "nobody is asked inside a merge");
            Assert.That(Retired(_candidates[1]), Is.False);

            // The round a moment later, with the merge over.
            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(Retired(_candidates[1]), Is.True);
        }

        [Test]
        public void The_writer_refuses_to_keep_inside_a_merge()
        {
            // The chooser's retries are timer ticks, which a merge's pump delivers as it does a
            // round, and this write takes no turn that would keep it out.
            _writer.Merging = KeePassMerges.Watch();
            Assume.That((object)_writer.Merging, Is.Not.Null, "this KeePass has no merge to look for");

            var answers = new List<AuthorityChange>();
            Merged(() => answers.Add(_writer.Keep(_database.Database, _candidates[0], _candidates)));

            Assert.That(answers, Is.Not.Empty);
            Assert.That(answers.Select(kept => kept.Succeeded), Has.All.False);
            Assert.That(answers.Select(kept => kept.Message), Has.All.Contains("merging"));
            Assert.That(Retired(_candidates[1]), Is.False);

            Assert.That(_writer.Keep(_database.Database, _candidates[0], _candidates).Succeeded, Is.True,
                "and takes the same answer once the merge is over");
        }

        [Test]
        public void The_writer_refuses_to_keep_while_KeePass_is_part_way_through_something()
        {
            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => { }, () => "KeePass is importing", null, null, _dialogs,
                NullGateLog.Instance);

            var kept = writer.Keep(_database.Database, _candidates[0], _candidates);

            Assert.That(kept.Succeeded, Is.False);
            Assert.That(kept.Message, Does.Contain("importing"));
            Assert.That(Retired(_candidates[1]), Is.False);
        }

        [Test]
        public void The_writer_refuses_at_once_to_keep_while_another_write_holds_it()
        {
            // Keep runs on KeePass's thread, and a write holding the lock waits for that very
            // thread to run its half: waiting for the lock there froze KeePass for the whole
            // timeout and then refused anyway.
            var held = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            var ui = new HoldingUi(held, release);
            var writer = new DatabaseWriter(ui, database => { }, null, null, null, _dialogs, NullGateLog.Instance);

            var other = new Thread(() =>
            {
                ui.Holding = Thread.CurrentThread;
                writer.Change(_database.Database, () => null);
            });

            other.IsBackground = true;
            other.Start();

            try
            {
                Assume.That(held.Wait(TimeSpan.FromSeconds(10)), Is.True, "the other write has the lock");

                var spent = System.Diagnostics.Stopwatch.StartNew();
                var kept = writer.Keep(_database.Database, _candidates[0], _candidates);
                spent.Stop();

                Assert.That(kept.Succeeded, Is.False);
                Assert.That(kept.IsBehindAnotherWrite, Is.True, "and as a refusal that ends by itself");
                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "refused at once rather than waited");
            }
            finally
            {
                release.Set();
                other.Join(TimeSpan.FromSeconds(10));
            }
        }

        [Test]
        public void A_choice_behind_another_write_is_kept_once_that_write_is_done_however_long_it_takes()
        {
            // A save is a whole-database write and a large one takes a minute and more. Tried for
            // three seconds and then given up, the choice was reported as not kept, and the
            // operator sent to the tab, for a wait that would have ended by itself.
            var held = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            var ui = new HoldingUi(held, release);
            var writer = new DatabaseWriter(ui, database => { }, null, null, null, _dialogs, NullGateLog.Instance);

            var other = new Thread(() =>
            {
                ui.Holding = Thread.CurrentThread;
                writer.Change(_database.Database, () => null);
            });

            other.IsBackground = true;
            other.Start();

            var tries = 0;
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, writer, _owner, null, _prompt, null, NullGateLog.Instance)
            {
                AskWhichToKeep = (choice, name) => _candidates[0],
                After = (delay, work) =>
                {
                    Thread.Sleep(delay);

                    // Past the bound a refusal that does not end by itself is given.
                    if (++tries * delay.TotalMilliseconds > AuthorityChooser.KeepTrying.TotalMilliseconds + 1000)
                    {
                        release.Set();
                        other.Join(TimeSpan.FromSeconds(10));
                    }

                    work();
                },
            };

            try
            {
                Assume.That(held.Wait(TimeSpan.FromSeconds(10)), Is.True, "the other write has the lock");

                using (chooser)
                {
                    chooser.Ask(_database.Database);
                }

                Assert.That(_prompt.Told, Is.Null, "the operator is not sent back to choose again");
                Assert.That(Retired(_candidates[1]), Is.True,
                    "and the choice is kept");
            }
            finally
            {
                release.Set();
                other.Join(TimeSpan.FromSeconds(10));
            }
        }

        /// <summary>The chooser's timer, as a wait where the test stands.</summary>
        private static void Waited(TimeSpan delay, Action work)
        {
            Thread.Sleep(delay);
            work();
        }

        /// <summary>
        /// A user interface that runs work where it is asked, except for one thread, whose work
        /// it holds until told to let go: a write holding the lock while its half waits.
        /// </summary>
        private sealed class HoldingUi : IUiThread
        {
            private readonly ManualResetEventSlim _held;
            private readonly ManualResetEventSlim _release;

            public HoldingUi(ManualResetEventSlim held, ManualResetEventSlim release)
            {
                _held = held;
                _release = release;
            }

            public Thread Holding { get; set; }

            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                if (Thread.CurrentThread == Holding)
                {
                    _held.Set();
                    _release.Wait(TimeSpan.FromSeconds(30));
                    return false;
                }

                work();
                return true;
            }
        }

        /// <summary>
        /// Makes the database undecided, as copies that each recreated an authority leave it once
        /// they are merged: authorities side by side and none of them retired, two here. Answers
        /// with their digests.
        /// </summary>
        private static string[] Undecided(TemporaryDatabase database)
        {
            return StoreSeeds.Undecided(
                new KdbxStore(database.Database, GateVersion.Current, null));
        }

        /// <summary>
        /// Adds one more authority beside those the database holds, as another synchronize would
        /// bring it, and answers with its digest.
        /// </summary>
        private string Third()
        {
            var third = StoreSeeds.Beside(Store());
            _candidates = _candidates.Concat(new[] { third }).ToArray();
            return third;
        }

        /// <summary>The database's store, read and written directly.</summary>
        private KdbxStore Store()
        {
            return new KdbxStore(_database.Database, GateVersion.Current, null);
        }

        /// <summary>Whether the database's store holds a retirement of this authority.</summary>
        private bool Retired(string id)
        {
            using (var reading = Store().Read())
            {
                return reading.InForce.Taken(StoreKinds.Retired).Contains(id);
            }
        }

        /// <summary>The identifier of the entry holding this authority's record.</summary>
        private KeePassLib.PwUuid EntryOf(string id)
        {
            using (var reading = Store().Read())
            {
                var record = reading.InForce.Authorities.Single(found => found.Form.Member(StoreKinds.Id) == id);
                return new KeePassLib.PwUuid(KeePassGate.Testing.StoreComposer.BytesOf(record.Hex));
            }
        }

        /// <summary>
        /// Merges another copy into the database with KeePass's own merge, doing something
        /// wherever the merge comes back to its status logger, which is where a merge pumps.
        /// Answers with how often it came back.
        /// </summary>
        private int Merged(Action inside)
        {
            using (var elsewhere = TemporaryDatabase.New())
            {
                // A copy of the same database, as KeePass matches them, with entries of its own
                // for the merge to bring.
                elsewhere.Database.RootGroup.Uuid = _database.Database.RootGroup.Uuid;
                for (var i = 0; i < 20; i++)
                {
                    var entry = new PwEntry(true, true);
                    entry.Strings.Set(
                        PwDefs.TitleField, new KeePassLib.Security.ProtectedString(false, "an invented service " + i));
                    elsewhere.Database.RootGroup.AddEntry(entry, true);
                }

                var calls = 0;
                _database.Database.MergeIn(
                    elsewhere.Database,
                    PwMergeMethod.Synchronize,
                    new Calling(() =>
                    {
                        calls++;
                        inside();
                    }));
                return calls;
            }
        }

        /// <summary>A status logger that does one thing wherever the merge comes back to it.</summary>
        private sealed class Calling : KeePassLib.Interfaces.IStatusLogger
        {
            private readonly Action _called;

            public Calling(Action called)
            {
                _called = called;
            }

            public void StartLogging(string strOperation, bool bWriteOperationToLog)
            {
            }

            public void EndLogging()
            {
            }

            public bool SetProgress(uint uPercent)
            {
                _called();
                return true;
            }

            public bool SetText(string strNewText, KeePassLib.Interfaces.LogStatusType lsType)
            {
                _called();
                return true;
            }

            public bool ContinueWork()
            {
                _called();
                return true;
            }
        }

        private sealed class NoEvents : IDatabaseEvents
        {
            public event EventHandler<GateDatabaseEventArgs> Opened
            {
                add { }
                remove { }
            }

            public event EventHandler Closing
            {
                add { }
                remove { }
            }

            public event EventHandler<GateDatabaseEventArgs> Closed
            {
                add { }
                remove { }
            }

            public event EventHandler Saved
            {
                add { }
                remove { }
            }
        }

        /// <summary>Every line logged, in order.</summary>
        private sealed class LinesLog : IGateLog
        {
            public List<string> Lines { get; } = new List<string>();

            public void Info(string message)
            {
                Lines.Add(message);
            }

            public void Warn(string message)
            {
                Lines.Add(message);
            }

            public void Error(string message, Exception error)
            {
                Lines.Add(message);
            }

            public void Unexpected(string message, Exception error)
            {
                Lines.Add(message);
            }
        }

        private sealed class TellingPrompt : IOperatorPrompt
        {
            public string Told { get; private set; }

            /// <summary>Called as the operator is told, for a test that asks what was going on then.</summary>
            public Action Telling { get; set; }

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                if (Telling != null)
                {
                    Telling();
                }

                Told = news;
            }
        }
    }
}
