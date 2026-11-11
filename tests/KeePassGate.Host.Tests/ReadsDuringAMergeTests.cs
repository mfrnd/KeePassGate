using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Interfaces;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A read asked for while KeePass merges, against KeePass's own merge of two real databases.
    /// </summary>
    /// <remarks>
    /// A merge pumps messages through its status logger between one entry and the next, and a
    /// timer ticks inside that pump, which is where a read used to start: on a tree holding part
    /// of one database and part of another. These drive the real `PwDatabase.MergeIn` with a
    /// logger that pumps as KeePass's own do, and hold two things: that the plugin's question is
    /// answered yes from inside every kind of merge and no on either side of it, and that a read
    /// asked for inside a merge is handed the tree the merge leaves. What the queue does with the
    /// answer, over a stand-in for the question, is the unit tier's, `ReadHoldTests`.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class ReadsDuringAMergeTests
    {
        private const int Entries = 40;

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        /// <summary>How long the merge stands still at its first entry, pumping.</summary>
        private static readonly TimeSpan Pumped = TimeSpan.FromMilliseconds(300);

        private TemporaryDatabase _here;
        private TemporaryDatabase _elsewhere;

        [SetUp]
        public void SetUp()
        {
            _here = TemporaryDatabase.New();
            _elsewhere = TemporaryDatabase.New();

            // Two copies of one database, as `RemovalMergeTests` makes them: KeePass matches nodes
            // by identifier, so the copies share a root. Everything else is on the other copy.
            _elsewhere.Database.RootGroup.Uuid = _here.Database.RootGroup.Uuid;

            var group = new PwGroup(true, true, "Invented", PwIcon.Folder);
            _elsewhere.Database.RootGroup.AddGroup(group, true);
            for (var i = 0; i < Entries; i++)
            {
                var entry = new PwEntry(true, true);
                entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service " + i));
                group.AddEntry(entry, true);
            }
        }

        [TearDown]
        public void TearDown()
        {
            _here.Dispose();
            _elsewhere.Dispose();
        }

        [TestCase(PwMergeMethod.Synchronize)]
        [TestCase(PwMergeMethod.OverwriteExisting)]
        [TestCase(PwMergeMethod.KeepExisting)]
        [TestCase(PwMergeMethod.OverwriteIfNewer)]
        [TestCase(PwMergeMethod.CreateNewUuids)]
        public void A_merge_is_seen_from_inside_it_and_from_nowhere_else(PwMergeMethod method)
        {
            // Whatever the method: File, Synchronize runs the first, File, Import offers all of
            // them, and XML Replace merges with the second.
            var merging = KeePassMerges.Watch();
            Assert.That((object)merging, Is.Not.Null, "this KeePass has no merge to look for");

            var answers = new List<bool>();
            var logger = new Logger(() => answers.Add(merging()));

            Assert.That(merging(), Is.False, "before the merge");

            _here.Database.MergeIn(_elsewhere.Database, method, logger);

            Assert.That(merging(), Is.False, "after the merge");
            Assert.That(answers, Is.Not.Empty,
                "the merge no longer comes back to its logger, which is where it pumps and where a read got in");
            Assert.That(answers, Has.All.True,
                "the merge called its logger from somewhere its own frame is not on the stack");
            Assert.That(Count(_here.Database.RootGroup), Is.EqualTo(Entries), "and it merged what it was given");
        }

        [Test]
        public void A_read_asked_for_inside_a_merge_is_handed_the_tree_the_merge_leaves()
        {
            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control, KeePassMerges.Watch())))
            {
                var read = ReadInsideAMerge(loop, ui.Value);

                Assert.That(read.ServedInside, Is.False, "the read started while the merge was still pumping");
                Assert.That(read.Seen, Is.EqualTo(Entries), "what was read is every entry the merge brought");
            }
        }

        [Test]
        public void With_nothing_looking_for_the_merge_the_same_read_is_handed_a_tree_half_merged()
        {
            // What the fixture above would let through if the question stopped being asked, shown
            // rather than argued: the timer ticks inside the logger's pump and the read is served
            // there, with the merge barely begun.
            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control, () => false)))
            {
                var read = ReadInsideAMerge(loop, ui.Value);

                Assert.That(read.ServedInside, Is.True);
                Assert.That(read.Seen, Is.LessThan(Entries), "and it read a tree the merge had not finished");
            }
        }

        /// <summary>
        /// Merges on the loop's thread with a logger that, at the first entry, has another thread
        /// ask for a read and then pumps as KeePass's loggers do.
        /// </summary>
        private Read ReadInsideAMerge(UiLoop loop, ControlUiThread ui)
        {
            var read = new Read();
            Thread reader = null;
            var pumping = 0;

            var logger = new Logger(() =>
            {
                if (reader != null)
                {
                    return;
                }

                reader = new Thread(() =>
                {
                    read.Seen = ui.Hold(() =>
                    {
                        read.ServedInside = Volatile.Read(ref pumping) == 1;
                        return Count(_here.Database.RootGroup);
                    });
                }) { IsBackground = true };
                reader.Start();

                // `DoEvents`, which is every logger KeePass merges under: timers tick and Idle is
                // never raised. Until the read was served or the time is up, whichever is first.
                Volatile.Write(ref pumping, 1);
                var until = DateTime.UtcNow + Pumped;
                while (reader.IsAlive && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }

                Volatile.Write(ref pumping, 0);
            });

            loop.Invoke(() => _here.Database.MergeIn(_elsewhere.Database, PwMergeMethod.Synchronize, logger));

            Assert.That(reader, Is.Not.Null, "the merge never came back to its logger");
            Assert.That(reader.Join(Patience), Is.True, "the read was never answered");
            return read;
        }

        private static int Count(PwGroup group)
        {
            return (int)group.GetEntriesCount(true);
        }

        /// <summary>What a read asked for inside a merge came back with.</summary>
        private sealed class Read
        {
            public int Seen;

            public bool ServedInside;
        }

        /// <summary>A status logger that does one thing wherever the merge comes back to it.</summary>
        private sealed class Logger : IStatusLogger
        {
            private readonly Action _called;

            public Logger(Action called)
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

            public bool SetText(string strNewText, LogStatusType lsType)
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
    }
}
