using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
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
    /// The root group holds enough entries that the write's save takes a while, so that the
    /// message posted a little after the write is sent arrives while the save is running.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveSessionLockTests
    {
        private const int SessionChange = 0x02B1;
        private const int SessionLocked = 7;

        /// <summary>Enough that the save takes longer than the message takes to arrive.</summary>
        private const int RootEntries = 20000;

        /// <summary>When the lock is posted, counted from the write being sent.</summary>
        private const int PostAfterMilliseconds = 600;

        private const string LockedLine = "locked, answering with the responder";

        private const string MadeLine = "locking, for Lock, which KeePass was asked for while the plugin was saving";

        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

        private static readonly string CreatePath = "/groups/" + ThrowawayKeePass.AutomationUuid + "/entries";

        private LiveSession _session;
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
            keePass.FillTheRoot(RootEntries);
            keePass.Grant(Rules.Granting(("agent", "CRUSDLA")));
            keePass.LockWhenWindowsLocks();

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, "agent");

                var driver = KeePassDriver.Start(keePass);
                LiveApiTests.WaitUntilServing(keePass, keePass.Process);

                var broadcast = NativeWindows.BroadcastWindow(keePass.Process);
                var name = keePass.NewGateName();
                var sent = DateTime.UtcNow;
                var write = Task.Run(() => LiveGateClient.Send(
                    keePass.Port, name, admitted, "POST", CreatePath, Bodies.Of(("Title", "Invented"))));

                Thread.Sleep(PostAfterMilliseconds);
                var posted = DateTime.UtcNow;
                _posted = broadcast != IntPtr.Zero &&
                          User32.PostMessage(broadcast, SessionChange, new IntPtr(SessionLocked), IntPtr.Zero);

                _write = write.Wait(TimeSpan.FromSeconds(60)) ? write.Result : new LiveReply { Failure = "no answer in time" };
                _timeline = "sent " + Moment(sent) + ", lock posted " + Moment(posted) + ", answered " + Moment(DateTime.UtcNow);

                _locked = Logged(keePass, LockedLine, LockTimeout);
                _afterwards = LiveGateClient.Get(keePass.Port, keePass.NewGateName(), admitted, "/gate/status");

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
            Assert.That(_posted, Is.True, "the lock could not be posted to KeePass");
            Assert.That(_write.Answered, Is.True, _write.Failure + "; " + _timeline);
            Assert.That(_write.Status, Is.EqualTo(201), _write.Body);
        }

        [Test]
        public void KeePass_is_locked_once_that_write_has_been_answered()
        {
            Assert.That(_locked, Is.True, "KeePass was never locked; " + _timeline + Environment.NewLine + _pluginLog);
            Assert.That(_pluginLog, Does.Contain(MadeLine), "and it was the plugin that made the lock KeePass dropped");
            Assert.That(_afterwards.Status, Is.EqualTo(503), _afterwards.Failure ?? _afterwards.Body);
            Assert.That(_afterwards.Body, Does.Contain("\"locked\""));
        }

        [Test]
        public void KeePass_shut_down_by_itself_while_locked()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so something was still holding it.");
        }

        [Test]
        public void Nothing_was_reported_as_a_failure_in_the_whole_session()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private static string Moment(DateTime at)
        {
            return at.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
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
