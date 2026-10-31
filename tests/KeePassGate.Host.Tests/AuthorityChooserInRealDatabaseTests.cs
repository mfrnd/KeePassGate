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
            _candidates = TwoAuthorities();
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
                new NoEvents(), _targets, _writer, _owner, null, null, _prompt, null, NullGateLog.Instance);

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

            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(1));
            Assert.That(_saves, Is.Zero, "the operator saves, as they would any other change");
            Assert.That(_database.Database.Modified, Is.True, "and KeePass knows there is something to save");
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RetirementKey(_candidates[1])), Is.True);
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(_candidates[1])), Is.False,
                "the loser's document is pruned in the same write");

            using (var served = _targets.Current().Single())
            {
                Assert.That(served.DatabaseId, Is.EqualTo(_candidates[0]));
                Assert.That(served.Unavailable, Is.Null);
            }
        }

        [Test]
        public void Cancel_serves_nothing_and_is_not_asked_again_for_the_same_authorities()
        {
            _chooser.AskAll();
            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(1), "once per state: the tab is where it is asked again");
            Assert.That(_targets.Current().All(t => t.Unavailable == Gate.UndecidedTarget.Error), Is.True);
        }

        [Test]
        public void A_different_set_of_authorities_is_asked_about_again()
        {
            _chooser.AskAll();

            // A third arrives, as another merge would bring it.
            var service = new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance);
            var store = new CustomDataStore(_database.Database);
            var held = store.Keys.Where(k => AuthorityKeys.FingerprintInAuthority(k) != null)
                            .ToDictionary(k => k, k => store.Get(k));
            foreach (var key in held.Keys)
            {
                store.Remove(key);
            }

            Assume.That(service.Create().Succeeded, Is.True);
            var third = AuthorityKeys.FingerprintInAuthority(
                store.Keys.Single(k => AuthorityKeys.FingerprintInAuthority(k) != null));
            foreach (var pair in held)
            {
                store.Set(pair.Key, pair.Value);
            }

            _candidates = _candidates.Concat(new[] { third }).ToArray();

            _chooser.AskAll();

            Assert.That(_asked, Is.EqualTo(2));
        }

        [Test]
        public void Database_Maintenance_open_refuses_the_choice_and_the_prune_and_says_why()
        {
            // Its write-back is conditional on a key count, so a pick with a prune beside it is
            // exactly what it undoes. Refused, the operator is told, and nothing is written.
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
            Assert.That(_prompt.Told, Is.Not.Null.And.Contains("was not kept"));
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RetirementKey(_candidates[1])), Is.False);
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(_candidates[1])), Is.True);
        }

        [Test]
        public void A_choice_the_writer_refuses_for_a_moment_is_kept_without_asking_again()
        {
            // Another client's write, or KeePass saving: over in a moment, and not something the
            // operator should have to choose again for. Tried again for a few seconds first.
            var refusals = 2;
            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => { }, () => refusals-- > 0 ? "KeePass is saving" : null, null, null,
                _dialogs, NullGateLog.Instance);
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, writer, _owner, null, null, _prompt, null, NullGateLog.Instance)
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
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RetirementKey(_candidates[1])), Is.True);
        }

        [Test]
        public void KeePass_busy_with_something_of_its_own_is_waited_for_rather_than_asked_through()
        {
            var chooser = new AuthorityChooser(
                new NoEvents(), _targets, _writer, _owner, () => "KeePass is synchronizing", null, _prompt, null,
                NullGateLog.Instance);
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
        public void The_writer_refuses_to_keep_while_KeePass_is_part_way_through_something()
        {
            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => { }, () => "KeePass is importing", null, null, _dialogs,
                NullGateLog.Instance);

            var kept = writer.Keep(_database.Database, _candidates[0]);

            Assert.That(kept.Succeeded, Is.False);
            Assert.That(kept.Message, Does.Contain("importing"));
            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RetirementKey(_candidates[1])), Is.False);
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
                var kept = writer.Keep(_database.Database, _candidates[0]);
                spent.Stop();

                Assert.That(kept.Succeeded, Is.False);
                Assert.That(kept.Message, Does.Contain("choose again"));
                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "refused at once rather than waited");
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

        private sealed class TellingPrompt : IOperatorPrompt
        {
            public string Told { get; private set; }

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                Told = news;
            }
        }
    }
}
