using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    // cspell:ignore NTFS

    /// <summary>
    /// A Windows lock arriving while the plugin saves, with KeePass set to lock when Windows does.
    /// </summary>
    /// <remarks>
    /// **KeePass drops a lock asked for while its interaction is blocked**, and every save blocks
    /// it. Measured before the plugin made the lock itself: the lock posted during a write's save
    /// left KeePass unlocked and the gate answering, and the same lock posted with KeePass idle
    /// locked it at once. So this posts the lock during the save and holds the plugin to having
    /// locked KeePass once that write was answered.
    ///
    /// **Posted rather than locked**, to the hidden window .NET keeps for `SystemEvents`, as the
    /// message Windows sends there when the session locks: locking this desktop would end the
    /// session the tests run in. KeePass's own notifier and the plugin's both hear it from there.
    ///
    /// **Posted once the save is seen to have begun, rather than a fixed time after the write is
    /// sent.** A wait counted from the request assumes how long the handshake, the change and the
    /// save take, and none of the three is the same on every machine: the key derivation
    /// KeePassLib gives a new database takes milliseconds, so the whole save is over in a fraction
    /// of a second, and a lock posted a fixed time after the write can arrive with KeePass idle,
    /// which then locks by itself and leaves the plugin nothing to make. So the fixture watches for
    /// the file KeePass's transaction writes first and moves over the database last, and posts the
    /// lock the moment it appears. KeePass is given a temporary directory of its own, so that no
    /// other program's transaction can be taken for this one's.
    ///
    /// **And the save is made long enough to be seen**: the key derivation is calibrated to take
    /// <see cref="KeyDerivation"/> on the machine running the test, and a save derives the key
    /// after that file is open and before it writes the tree, handling KeePass's messages on the
    /// way. The lock is therefore posted, and handled, while the key is being derived, whatever the
    /// speed of the machine. A save that is never seen fails the fixture as a save never seen,
    /// rather than as a lock the plugin did not make.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveSessionLockTests
    {
        private const int SessionChange = 0x02B1;
        private const int SessionLocked = 7;

        /// <summary>
        /// How long deriving the key takes, which is most of what the write's save spends, and
        /// what KeePass's own open of the file spends as well.
        /// </summary>
        private static readonly TimeSpan KeyDerivation = TimeSpan.FromSeconds(2);

        /// <summary>How long the write's save is given to begin, and then to end.</summary>
        private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(30);

        /// <summary>How often the file a save writes is looked for.</summary>
        private const int PollMilliseconds = 10;

        /// <summary>
        /// What KeePass 2.61.1's `FileTransactionEx` calls the file it writes in its temporary
        /// directory under Transactional NTFS, before an identifier of its own.
        /// </summary>
        private const string TransactionPrefix = "KeePass_TxF_";

        /// <summary>
        /// What it adds to that file's name, and to the database's own for the file it writes
        /// beside the database where Transactional NTFS is not available.
        /// </summary>
        private const string TransactionSuffix = ".tmp";

        private const string LockedLine = "locked, answering with the responder";

        private const string MadeLine = "locking, for Lock, which KeePass was asked for while the plugin was saving";

        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

        private static readonly string CreatePath = "/groups/" + ThrowawayKeePass.AutomationUuid + "/entries";

        private LiveSession _session;
        private string _unseen;
        private bool _posted;
        private LiveReply _write;
        private string _timeline;
        private bool _locked;
        private LiveReply _afterwards;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            keePass.FillWithInventedContent();
            keePass.Grant(Rules.Granting(("agent", "CRUSDLA")));
            keePass.LockWhenWindowsLocks();
            keePass.KeepTemporaryFilesInside();

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, "agent");

                // Named before the key derivation is made long, since naming the gate opens the
                // file.
                var name = keePass.NewGateName();
                var afterwards = keePass.NewGateName();
                keePass.DeriveTheKeyFor(KeyDerivation);

                var driver = KeePassDriver.Start(keePass);
                LiveApiTests.WaitUntilServing(keePass, keePass.Process);

                var broadcast = NativeWindows.BroadcastWindow(keePass.Process);
                var sent = DateTime.UtcNow;
                var write = Task.Run(() => LiveGateClient.Send(
                    keePass.Port, name, admitted, "POST", CreatePath, Bodies.Of(("Title", "Invented"))));

                var begun = SaveBegun(keePass, write);
                DateTime? posted = null;
                DateTime? over = null;
                if (begun == null)
                {
                    _unseen = write.IsCompleted
                        ? "the write was answered before its save was seen"
                        : "no save was seen in " + SaveTimeout.TotalSeconds + "s";
                }
                else
                {
                    posted = DateTime.UtcNow;
                    _posted = broadcast != IntPtr.Zero &&
                              User32.PostMessage(broadcast, SessionChange, new IntPtr(SessionLocked), IntPtr.Zero);
                    over = SaveOver(keePass);
                }

                _write = write.Wait(TimeSpan.FromSeconds(60)) ? write.Result : new LiveReply { Failure = "no answer in time" };
                _timeline = "sent " + Moment(sent) + ", save seen " + Moment(begun) + ", lock posted " +
                            Moment(posted) + ", save over " + Moment(over) + ", answered " + Moment(DateTime.UtcNow);

                _locked = Logged(keePass, LockedLine, LockTimeout);
                _afterwards = LiveGateClient.Get(keePass.Port, afterwards, admitted, "/gate/status");

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine(_timeline);
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
        }

        [Test]
        public void The_write_whose_save_was_running_is_answered()
        {
            Assert.That(_unseen, Is.Null, Unseen());
            Assert.That(_posted, Is.True, "the lock could not be posted to KeePass");
            Assert.That(_write.Answered, Is.True, _write.Failure + "; " + _timeline);
            Assert.That(_write.Status, Is.EqualTo(201), _write.Body);
        }

        [Test]
        public void KeePass_is_locked_once_that_write_has_been_answered()
        {
            Assert.That(_unseen, Is.Null, Unseen());
            Assert.That(_locked, Is.True, "KeePass was never locked; " + _timeline + Environment.NewLine + _pluginLog);
            Assert.That(_pluginLog, Does.Contain(MadeLine), "and it was the plugin that made the lock KeePass dropped");
            Assert.That(_afterwards.Status, Is.EqualTo(503), _afterwards.Failure ?? _afterwards.Body);
            Assert.That(_afterwards.Body, Does.Contain("\"locked\""));
        }

        [Test]
        public void KeePass_shut_down_by_itself_while_locked()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly: something was still holding it, or it ended with an error.");
        }

        [Test]
        public void Nothing_was_reported_as_a_failure_in_the_whole_session()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private string Unseen()
        {
            return "the write's save was never seen under way, so no lock was posted during it: " + _unseen +
                   ", looking for " + TransactionPrefix + "*" + TransactionSuffix + " in KeePass's temporary " +
                   "directory and for the database's name and " + TransactionSuffix + " beside it; " + _timeline;
        }

        /// <summary>
        /// When the write's save was first seen under way, or null when the write was answered
        /// first or nothing was seen in time.
        /// </summary>
        private static DateTime? SaveBegun(ThrowawayKeePass keePass, Task write)
        {
            var deadline = DateTime.UtcNow + SaveTimeout;
            while (!write.IsCompleted && DateTime.UtcNow < deadline)
            {
                if (Saving(keePass))
                {
                    return DateTime.UtcNow;
                }

                Thread.Sleep(PollMilliseconds);
            }

            return null;
        }

        /// <summary>When the save seen under way was first seen to be over, or null when it was not in time.</summary>
        private static DateTime? SaveOver(ThrowawayKeePass keePass)
        {
            var deadline = DateTime.UtcNow + SaveTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!Saving(keePass))
                {
                    return DateTime.UtcNow;
                }

                Thread.Sleep(PollMilliseconds);
            }

            return null;
        }

        /// <summary>
        /// Whether KeePass is part way through saving the database, read off the file its
        /// transaction writes before the key is derived and moves over the database once the tree
        /// is written.
        /// </summary>
        /// <remarks>
        /// In KeePass's temporary directory where Transactional NTFS serves both that directory and
        /// the database, which is the case measured, and beside the database where it does not.
        /// </remarks>
        private static bool Saving(ThrowawayKeePass keePass)
        {
            return File.Exists(keePass.DatabasePath + TransactionSuffix) ||
                   Directory.EnumerateFiles(
                       keePass.TemporaryDirectory, TransactionPrefix + "*" + TransactionSuffix).Any();
        }

        private static string Moment(DateTime? at)
        {
            return at == null ? "never" : at.Value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static bool Logged(ThrowawayKeePass keePass, string marker, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().Contains(marker))
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return false;
        }
    }
}
