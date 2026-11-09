using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using KeePassGate.Audit;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What KeePass's exit waits for: the lines the audit and the log still have queued, both
    /// together for <see cref="PluginFiles.ExitWait"/> at most, and then the process is let end.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-11-08. The writers are background threads, so that storage
    /// that does not answer never keeps KeePass's process alive or holds up a logoff, and the wait
    /// at the end of `Terminate` is what keeps the normal case from losing what KeePass recorded
    /// and logged as it closed.
    /// </remarks>
    [TestFixture]
    public sealed class ExitWaitTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Paths.Join(Path.GetTempPath(), "KeePassGateTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // A file still open somewhere as the test ends, in a directory of this test's own.
            }
        }

        [Test]
        public void Lines_still_queued_past_the_bound_do_not_hold_the_exit()
        {
            // Each writer is held behind a connection that holds its file's lock and is not let go:
            // the audit's by the log it reports a refused write to, the log's by the clock it reads
            // the moment of its own refused write from. KeePass's thread then queues a line on each.
            var auditPath = Paths.Join(_directory, "audit.jsonl");
            var logPath = Paths.Join(_directory, "plugin.log");
            using (var release = new ManualResetEventSlim(false))
            using (var logHeld = new ManualResetEventSlim(false))
            {
                Thread keePass = null;
                Thread logConnection = null;
                Func<bool> onKeePass = () => Thread.CurrentThread == keePass;

                var reporting = new HoldingLog(release);
                var audit = new JsonLinesAudit(auditPath, reporting, null, onKeePass);
                var asked = 0;
                Func<DateTime> clock = () =>
                {
                    if (Thread.CurrentThread == logConnection && ++asked == 2)
                    {
                        logHeld.Set();
                        release.Wait(TimeSpan.FromSeconds(30));
                    }

                    return DateTime.UtcNow;
                };
                var log = new TraceGateLog(logPath, null, clock, onKeePass);

                var auditConnection = new Thread(() => audit.Record(Entry("readEntry")));
                logConnection = new Thread(() => log.Info("from a connection"));

                try
                {
                    // The files are let go before the connections are, so the writers find them free.
                    using (new FileStream(auditPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    using (new FileStream(logPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        auditConnection.Start();
                        logConnection.Start();
                        Assume.That(reporting.Holding.Wait(TimeSpan.FromSeconds(10)), Is.True, "the audit's lock is held");
                        Assume.That(logHeld.Wait(TimeSpan.FromSeconds(10)), Is.True, "and the log's");

                        keePass = new Thread(() =>
                        {
                            audit.Record(Entry("revokeCertificate"));
                            log.Info("stopped listening");
                        });
                        keePass.Start();
                        Assume.That(keePass.Join(TimeSpan.FromSeconds(10)), Is.True, "KeePass's thread queued both");

                        var waited = Stopwatch.StartNew();
                        var written = PluginFiles.WaitForQueuedLines(TimeSpan.FromMilliseconds(300), audit.Drain, log.Drain);
                        waited.Stop();

                        Assert.That(written, Is.False, "the lines are still queued");
                        Assert.That(waited.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "and the exit was let go at the bound");
                    }
                }
                finally
                {
                    release.Set();
                    auditConnection.Join();
                    logConnection.Join();
                }

                Assert.That(PluginFiles.WaitForQueuedLines(TimeSpan.FromSeconds(10), audit.Drain, log.Drain), Is.True,
                    "once the files answer, the same wait finds everything written");
                Assert.That(File.ReadAllText(auditPath), Does.Contain("revokeCertificate"));
                Assert.That(File.ReadAllText(logPath), Does.Contain("stopped listening"));
            }
        }

        [Test]
        public void Lines_queued_on_a_disk_that_answers_are_all_written_before_the_exit_goes_on()
        {
            var auditPath = Paths.Join(_directory, "audit.jsonl");
            var logPath = Paths.Join(_directory, "plugin.log");
            Thread keePass = null;
            Func<bool> onKeePass = () => Thread.CurrentThread == keePass;
            var audit = new JsonLinesAudit(auditPath, null, null, onKeePass);
            var log = new TraceGateLog(logPath, null, null, onKeePass);

            keePass = new Thread(() =>
            {
                audit.Record(Entry("revokeCertificate"));
                log.Info("stopped listening");
            });
            keePass.Start();
            keePass.Join();

            Assert.That(PluginFiles.WaitForQueuedLines(PluginFiles.ExitWait, audit.Drain, log.Drain), Is.True);
            Assert.That(File.ReadAllText(auditPath), Does.Contain("revokeCertificate"));
            Assert.That(File.ReadAllText(logPath), Does.Contain("stopped listening"));
        }

        private static AuditEntry Entry(string operationId)
        {
            return new AuditEntry(
                DateTimeOffset.UtcNow, "d1e2f3", "9f86d081", "9f86d081", operationId, "7b1c", "3a9f", true, "why");
        }

        /// <summary>A log whose first error is not answered until the test lets it go.</summary>
        private sealed class HoldingLog : IGateLog
        {
            private readonly ManualResetEventSlim _release;
            private int _errors;

            public HoldingLog(ManualResetEventSlim release)
            {
                _release = release;
            }

            public ManualResetEventSlim Holding { get; } = new ManualResetEventSlim(false);

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
            }

            public void Error(string message, Exception error)
            {
                if (Interlocked.Increment(ref _errors) == 1)
                {
                    Holding.Set();
                    _release.Wait(TimeSpan.FromSeconds(30));
                }
            }

            public void Unexpected(string message, Exception error)
            {
            }
        }
    }
}
