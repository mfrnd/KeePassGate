using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            var log = new TraceGateLog();

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
            var log = new TraceGateLog(Path.Combine(_directory, "no-such-directory", "gate.log"));

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
