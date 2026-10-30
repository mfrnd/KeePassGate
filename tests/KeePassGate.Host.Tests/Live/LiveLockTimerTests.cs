using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// KeePass's own inactivity lock, falling due under a renewal dialog nobody answers.
    /// </summary>
    /// <remarks>
    /// The one fixture that runs KeePass's lock timer: every other session runs with none, and
    /// `LiveLockTests` locks with Ctrl and L, which reaches the same closing of the database and
    /// not the tick. The approval dialogs hold no lock off, so the timer falls due under one, and
    /// `PAIRING.md` §308 says what then happens: the dialog closes as unanswered and the client is
    /// told the database is locked.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveLockTimerTests
    {
        /// <summary>How long KeePass waits with nobody using it before it locks.</summary>
        private const int LockAfterSeconds = 10;

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Longer than the lock takes to fall due once the dialog is up, with room for a slow runner.</summary>
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(LockAfterSeconds + 50);

        private LiveSession _session;
        private bool _dialogAppeared;
        private bool _dialogWent;
        private LiveReply _reply;
        private Exception _failed;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true, LockAfterSeconds);
            var keePass = _session.KeePass;

            using (var renewing = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(renewing, "reader");

                var driver = KeePassDriver.Start(keePass);
                LivePairingTests.WaitUntilServing(keePass, driver);

                var name = keePass.NewGateName();
                var asking = new Thread(() => Ask(keePass, name, renewing)) { IsBackground = true };
                asking.Start();

                _dialogAppeared = driver.WaitForWindow("asking to renew", DialogTimeout) != null;

                // Nothing is pressed, so KeePass sees no activity and its timer falls due.
                var until = DateTime.UtcNow + LockTimeout;
                while (_dialogAppeared && DateTime.UtcNow < until && !_dialogWent)
                {
                    _dialogWent = driver.WaitForWindow("asking to renew", TimeSpan.FromSeconds(1)) == null;
                }

                asking.Join(LockTimeout);
                driver.ShutDown();
            }

            _pluginLog = _session.KeePass.ReadPluginLog();
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
        public void The_lock_closes_the_dialog_nobody_answered()
        {
            Assert.That(_dialogAppeared, Is.True, "the renewal dialog never appeared");
            Assert.That(_dialogWent, Is.True, "the dialog was still up after the lock fell due");
            Assert.That(_pluginLog, Does.Contain("closed unanswered"));
        }

        [Test]
        public void The_client_is_told_the_database_is_locked()
        {
            Assert.That(_failed, Is.Null, "the renewal failed: " + _failed);
            Assert.That(_reply, Is.Not.Null, "the renewal was never answered");
            Assert.That(_reply.Status, Is.EqualTo(503), _reply.Failure ?? _reply.Body);
            Assert.That(_reply.Body, Does.Contain("\"locked\""));
        }

        private void Ask(ThrowawayKeePass keePass, string name, X509Certificate2 renewing)
        {
            _failed = Thrown.By(() =>
            {
                _reply = LiveGateClient.Send(
                    keePass.Port, name, renewing, "POST", RenewalHandler.Path, "{}", LiveGateClient.OnTheOperator);
            });
        }
    }
}
