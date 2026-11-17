using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using KeePassGate.Testing;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A real KeePass serving a database whose store is stopped, `STORE.md` §624 and §656.
    /// </summary>
    /// <remarks>
    /// **What no other tier shows**: the store read on KeePass's real thread through the real
    /// hold, at each request, with the plugin loaded beside it, and KeePass's window still taking
    /// its own work while requests arrive. The store is composed into the file before KeePass
    /// opens it, an authority's record and no summary entry, which is a store whose summary entry
    /// was deleted for good: every request is answered `503 stopped`, and the log names the stop
    /// once for all of them.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveStoreTests
    {
        private const int Requests = 10;


        private LiveSession _session;
        private readonly List<LiveReply> _replies = new List<LiveReply>();
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            keePass.ChangeTheStore(database =>
            {
                var key = StoreComposer.Key("live");
                var group = new PwGroup(true, true, "KeePassGate", PwIcon.TerminalEncrypted);
                database.RootGroup.AddGroup(group, true);
                group.AddEntry(StoreEntries.Of(StoreComposer.Record(
                    key, "authority", "authority:" + new string('a', 64),
                    StoreComposer.Content(("id", new string('a', 64)), ("storeKey", StoreComposer.HexOf(key))))), true);
            });

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, "ops");

                var driver = KeePassDriver.Start(keePass);
                LivePairingTests.WaitUntilServing(keePass, driver);

                for (var request = 0; request < Requests; request++)
                {
                    _replies.Add(LiveGateClient.Get(keePass.Port, keePass.NewGateName(), admitted, "/entries"));
                }

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
        public void Every_request_is_answered_that_the_store_is_stopped()
        {
            Assert.That(_replies, Has.Count.EqualTo(Requests));
            foreach (var reply in _replies)
            {
                Assert.That(reply.Status, Is.EqualTo(503), reply.Failure ?? reply.Body);
                Assert.That(reply.Body, Does.Contain("\"stopped\""));
            }
        }

        [Test]
        public void The_stop_is_logged_once_for_every_request_it_answered()
        {
            Assert.That(
                Regex.Matches(_pluginLog ?? string.Empty, "not serving a database whose store is stopped").Count,
                Is.EqualTo(1),
                _pluginLog);
        }

        [Test]
        public void KeePass_exited_cleanly()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly while its store was stopped.");
        }
    }
}
