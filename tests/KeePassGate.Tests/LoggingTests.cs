using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KeePassGate.Audit;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    public sealed class GateLogFormatTests
    {
        private static readonly DateTime Stamp = new DateTime(2026, 3, 4, 5, 6, 7, 89, DateTimeKind.Utc);

        [Test]
        public void A_line_leads_with_the_time_the_prefix_and_the_level()
        {
            var line = GateLogFormat.Line(Stamp, GateLogFormat.InfoLevel, "started", null);

            Assert.That(line, Is.EqualTo("2026-03-04T05:06:07.089Z KPG INFO started"));
        }

        [Test]
        public void An_exception_is_folded_onto_the_same_line()
        {
            var line = GateLogFormat.Line(Stamp, GateLogFormat.ErrorLevel, "attach failed",
                new InvalidOperationException("no tab control"));

            Assert.That(line, Does.StartWith("2026-03-04T05:06:07.089Z KPG ERROR attach failed | "));
            Assert.That(line, Does.Contain("InvalidOperationException"));
            Assert.That(line, Does.Not.Contain("\n"));
            Assert.That(line, Does.Not.Contain("\r"));
        }

        [Test]
        public void A_message_carrying_line_breaks_stays_one_line()
        {
            var line = GateLogFormat.Line(Stamp, GateLogFormat.WarnLevel, "first\r\nsecond\nthird\rfourth", null);

            Assert.That(line, Does.Not.Contain("\n"));
            Assert.That(line, Does.Not.Contain("\r"));
            Assert.That(line, Does.Contain("first | second third fourth"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void An_empty_message_is_formatted_rather_than_refused(string message)
        {
            Assert.That(GateLogFormat.Line(Stamp, GateLogFormat.InfoLevel, message, null),
                Is.EqualTo("2026-03-04T05:06:07.089Z KPG INFO "));
        }

        [Test]
        public void The_format_is_invariant_of_the_current_culture()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");

                Assert.That(GateLogFormat.Line(Stamp, GateLogFormat.InfoLevel, "started", null),
                    Is.EqualTo("2026-03-04T05:06:07.089Z KPG INFO started"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }
    }

    // Alone, because `Trace.Listeners` belongs to the process and the lines reaching it are counted
    // exactly.
    [TestFixture]
    [NonParallelizable]
    public sealed class TraceGateLogTests
    {
        private sealed class CapturingListener : TraceListener
        {
            public System.Collections.Generic.List<string> Lines { get; } = new System.Collections.Generic.List<string>();

            public override void Write(string message)
            {
                Lines.Add(message);
            }

            public override void WriteLine(string message)
            {
                Lines.Add(message);
            }
        }

        private CapturingListener _listener;
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _listener = new CapturingListener();
            Trace.Listeners.Add(_listener);
            _directory = Path.Combine(Path.GetTempPath(), "KeePassGateTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Trace.Listeners.Remove(_listener);
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public void Every_level_reaches_the_trace_listeners()
        {
            // No file, because the one a log with nothing named writes to is this account's own.
            var log = new TraceGateLog((string)null);

            log.Info("one");
            log.Warn("two");
            log.Error("three", new InvalidOperationException("boom"));

            Assert.That(_listener.Lines.Count(l => l.Contains("KPG INFO one")), Is.EqualTo(1));
            Assert.That(_listener.Lines.Count(l => l.Contains("KPG WARN two")), Is.EqualTo(1));
            Assert.That(_listener.Lines.Count(l => l.Contains("KPG ERROR three")), Is.EqualTo(1));
        }

        [Test]
        public void A_named_file_receives_one_line_per_event()
        {
            var path = Path.Combine(_directory, "gate.log");
            var log = new TraceGateLog(path);

            log.Info("one");
            log.Warn("two");

            var lines = File.ReadAllLines(path);
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.Contain("KPG INFO one"));
            Assert.That(lines[1], Does.Contain("KPG WARN two"));
        }

        [Test]
        public void A_second_log_appends_rather_than_truncates()
        {
            var path = Path.Combine(_directory, "gate.log");
            new TraceGateLog(path).Info("first run");
            new TraceGateLog(path).Info("second run");

            Assert.That(File.ReadAllLines(path).Length, Is.EqualTo(2));
        }

        [Test]
        public void The_file_can_be_read_while_the_plugin_is_still_writing_it()
        {
            var path = Path.Combine(_directory, "gate.log");
            var log = new TraceGateLog(path);
            log.Info("first");

            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Assert.DoesNotThrow(() => log.Info("second"));
                Assert.That(reader.Length, Is.GreaterThan(0));
            }
        }

        [Test]
        public void A_file_that_cannot_be_written_does_not_stop_the_plugin()
        {
            // A file where the directory would have to be, which no write can get past.
            var blocker = Path.Combine(_directory, "not-a-directory");
            File.WriteAllText(blocker, string.Empty);
            var log = new TraceGateLog(Path.Combine(blocker, "gate.log"));

            Assert.DoesNotThrow(() => log.Info("one"));
            Assert.DoesNotThrow(() => log.Info("two"));

            Assert.That(_listener.Lines.Any(l => l.Contains("log file disabled")), Is.True);
            // Reported once, then dropped: a broken log must not cost a line of work per event.
            Assert.That(_listener.Lines.Count(l => l.Contains("log file disabled")), Is.EqualTo(1));
        }

        [Test]
        public void Without_a_file_nothing_is_written_to_disk()
        {
            var log = new TraceGateLog((string)null);

            log.Info("one");

            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }

        [Test]
        public void A_directory_that_is_not_there_yet_is_made_rather_than_the_log_given_up()
        {
            // The default is under this account's application data, in a directory nothing makes
            // until something is written there.
            var path = Path.Combine(_directory, "not-yet", "gate.log");

            new TraceGateLog(path).Info("one");

            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(1));
        }

        [Test]
        public void The_file_rolls_rather_than_growing_without_end()
        {
            var path = Path.Combine(_directory, "gate.log");
            Grow(path, TraceGateLog.RollAt + 1);

            new TraceGateLog(path).Info("one");

            Assert.That(File.Exists(path + ".1"), Is.True, "the old lines are set aside, not deleted");
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(1), "and the new file starts clean");
        }

        [Test]
        public void Rolling_twice_keeps_one_previous_file_and_no_more()
        {
            var path = Path.Combine(_directory, "gate.log");
            File.WriteAllText(path + ".1", "older");
            Grow(path, TraceGateLog.RollAt + 1);

            new TraceGateLog(path).Info("one");

            Assert.That(File.ReadAllText(path + ".1"), Does.Not.Contain("older"));
        }

        [Test]
        public void A_file_that_cannot_be_set_aside_is_still_written_to()
        {
            // Setting the file aside bounds it and is no condition for writing to it. A viewer
            // holding the previous file stops it being deleted, and the log goes on regardless.
            var path = Path.Combine(_directory, "gate.log");
            Grow(path, TraceGateLog.RollAt + 1);

            using (new FileStream(path + ".1", FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                var log = new TraceGateLog(path);
                log.Info("one");
                log.Info("two");

                Assert.That(File.ReadAllText(path), Does.Contain("KPG INFO one").And.Contain("KPG INFO two"));
                Assert.That(_listener.Lines.Count(l => l.Contains("not set aside")), Is.EqualTo(1),
                    "said once rather than at every line");
                Assert.That(_listener.Lines.Any(l => l.Contains("log file disabled")), Is.False);
            }
        }

        [Test]
        public void A_log_with_nothing_named_writes_where_the_environment_says()
        {
            var path = Path.Combine(_directory, "named.log");
            var was = Environment.GetEnvironmentVariable(TraceGateLog.LogFileVariable);
            try
            {
                Environment.SetEnvironmentVariable(TraceGateLog.LogFileVariable, path);

                new TraceGateLog().Info("one");
            }
            finally
            {
                Environment.SetEnvironmentVariable(TraceGateLog.LogFileVariable, was);
            }

            Assert.That(File.ReadAllText(path), Does.Contain("KPG INFO one"));
        }

        [Test]
        public void Without_a_name_the_log_is_kept_beside_the_audit()
        {
            // Resolved rather than written to, since the answer is this account's own directory.
            var log = Environment.GetEnvironmentVariable(TraceGateLog.LogFileVariable);
            var audit = Environment.GetEnvironmentVariable(JsonLinesAudit.AuditFileVariable);
            try
            {
                Environment.SetEnvironmentVariable(TraceGateLog.LogFileVariable, null);
                Environment.SetEnvironmentVariable(JsonLinesAudit.AuditFileVariable, null);

                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logPath = PluginFiles.Resolve(TraceGateLog.LogFileVariable, TraceGateLog.DefaultFileName);

                Assert.That(logPath, Is.EqualTo(Path.Combine(local, "KeePassGate", "plugin.log")));
                Assert.That(Path.GetDirectoryName(JsonLinesAudit.DefaultPath()), Is.EqualTo(Path.GetDirectoryName(logPath)));
            }
            finally
            {
                Environment.SetEnvironmentVariable(TraceGateLog.LogFileVariable, log);
                Environment.SetEnvironmentVariable(JsonLinesAudit.AuditFileVariable, audit);
            }
        }

        /// <summary>A file of a given length, without the bytes to fill it.</summary>
        private static void Grow(string path, long bytes)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                file.SetLength(bytes);
            }
        }
    }

    [TestFixture]
    public sealed class NullGateLogTests
    {
        [Test]
        public void It_accepts_everything_and_does_nothing()
        {
            var log = NullGateLog.Instance;

            Assert.DoesNotThrow(() => log.Info(null));
            Assert.DoesNotThrow(() => log.Warn(null));
            Assert.DoesNotThrow(() => log.Error(null, null));
        }
    }
}
