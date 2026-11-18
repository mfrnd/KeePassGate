using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using KeePassGate.Store;
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
    /// its own work while requests arrive. The seeded authority's store has its summary entry
    /// deleted for good before KeePass opens the file, with a deletion record as KeePass's own
    /// delete and an emptied recycle bin leave it: every request is answered `503 stopped`, and
    /// the log names the stop once for all of them.
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

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                // Admitted while the store still reads, since an admission is a record of the
                // store and names the authority served. The summary entry deleted afterwards is
                // the entry the admission's write left.
                keePass.Admit(admitted, "ops");

                keePass.ChangeTheStore(database =>
                {
                    var head = database.RootGroup.GetEntries(true)
                        .Single(entry => entry.Strings.ReadSafe(StoreFormat.KindField) == StoreKinds.Head);
                    head.ParentGroup.Entries.Remove(head);
                    database.DeletedObjects.Add(new PwDeletedObject(head.Uuid, DateTime.UtcNow));
                });

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
