using System;
using System.Threading;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A real KeePass locked from its own shortcut, and what the gate tells a real client then.
    /// </summary>
    /// <remarks>
    /// **The one place the chain runs whole**: KeePass's own `LockAllDocuments`, the events it
    /// raises in its own order, `MainWindowDatabases` and `LockWatch` as the plugin wires them up
    /// inside KeePass, and an answer on a socket. Every link is tested on its own elsewhere, the
    /// lock itself against a model of the order KeePass raises its events in, and a model agrees
    /// only with the KeePass it was read off.
    ///
    /// Locked with Ctrl+L, because Escape, KeePass's other way in, is turned off in the throwaway
    /// configuration. It ends locked, and KeePass is closed as it is.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveLockTests
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The status route, which reads nothing and is asked of the same client throughout.</summary>
        private const string StatusPath = "/gate/status";

        /// <summary>What the plugin logs once a lock has left it answering with the responder.</summary>
        private const string LockedLine = "locked, answering with the responder";

        private LiveSession _session;
        private LiveReply _beforeTheLock;
        private LiveReply _whileLocked;
        private string _notLocked;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                // Before KeePass starts, because a running KeePass serves the database it holds in
                // memory and would never see a record written to the file underneath it.
                keePass.Admit(admitted, "ops");

                var driver = KeePassDriver.Start(keePass);

                // Both lines, in that order, for the reason every live fixture waits for them: a
                // name resolves only once the database KeePass opened is served.
                WaitForLog(keePass, driver, "listening on 127.0.0.1:" + keePass.Port, StartTimeout,
                    "The plugin never reported a listening gate.");
                WaitForLog(keePass, driver, "serving a database with authority", StartTimeout,
                    "The plugin never reported serving the database KeePass was started with.");

                _beforeTheLock = LiveGateClient.Get(keePass.Port, keePass.NewGateName(), admitted, StatusPath);

                driver.LockWorkspace();

                // Told apart from a lock that never happened, because what the client is answered
                // afterwards means nothing without it. What KeePass shows is the likeliest reason
                // it did not lock: a question in front of the main window.
                if (!Logged(keePass, LockedLine, LockTimeout))
                {
                    _notLocked = "KeePass was showing " + NativeWindows.Titles(keePass.Process) +
                                 Environment.NewLine + driver.Diagnostics;
                }

                _whileLocked = LiveGateClient.Get(keePass.Port, keePass.NewGateName(), admitted, StatusPath);

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            _pluginLog = keePass.ReadPluginLog();
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
        public void The_client_is_answered_before_the_lock()
        {
            Assert.That(_beforeTheLock.Status, Is.EqualTo(200), _beforeTheLock.Failure);
        }

        [Test]
        public void Locking_the_workspace_from_KeePass_reaches_the_plugin()
        {
            Assert.That(_notLocked, Is.Null, "The plugin never reported the lock. " + _notLocked);
        }

        [Test]
        public void A_locked_KeePass_answers_503_locked()
        {
            Assert.That(_whileLocked.Status, Is.EqualTo(503), _whileLocked.Failure ?? _whileLocked.Body);
            Assert.That(_whileLocked.Body, Does.Contain("\"locked\""));
        }

        [Test]
        public void KeePass_shut_down_by_itself_while_locked()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so something was still holding it.");
            Assert.That(_pluginLog, Does.Contain("stopped listening"));
        }

        [Test]
        public void Nothing_was_reported_as_a_failure_in_the_whole_session()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private static void WaitForLog(
            ThrowawayKeePass keePass, KeePassDriver driver, string marker, TimeSpan timeout, string failure)
        {
            if (!Logged(keePass, marker, timeout))
            {
                Assert.Fail(failure + " " + driver.Diagnostics + Environment.NewLine +
                            "Log so far:" + Environment.NewLine + keePass.ReadPluginLog());
            }
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
