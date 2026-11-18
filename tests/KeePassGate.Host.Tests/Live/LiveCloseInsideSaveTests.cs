using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using KeePassGate.Authority;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// An operator's trigger that closes the database inside the save a client's revocation of
    /// itself makes, in a real KeePass, and what the client, the audit and the file are left with.
    /// </summary>
    /// <remarks>
    /// **On Saved database file** the close comes after KeePass wrote the file and raised
    /// `FileSaved` for a save that landed: the revocation is in the file and in the audit. **On
    /// Saving database file**, answered by discarding the changes, it comes before: KeePass's save
    /// then fails on the closed database and raises `FileSaved` as a failure, the file still
    /// admits the client, and the audit holds no line about a revocation nothing holds. Either way
    /// the close takes the write's own connection with it, so the client reads a connection that
    /// ended rather than an answer.
    ///
    /// The host tier reads off the IL that a failed save still raises `FileSaved`; this is where
    /// KeePass is seen doing it. **Two fixtures rather than one with two arguments**, because a
    /// session directory is named after its fixture's class and kept when the fixture fails, and
    /// the second case would delete the first case's as it began.
    /// </remarks>
    public abstract class LiveCloseInsideSaveTests
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

        /// <summary>How long KeePass is watched for a question once the revocation is sent.</summary>
        private static readonly TimeSpan Watched = TimeSpan.FromSeconds(60);

        /// <summary>What the writer logs for a database that closed inside its save.</summary>
        private const string ClosedLine = "the database closed as its save ended";

        /// <summary>What the gate logs as it closes the connections to a database that closed.</summary>
        private const string ConnectionsLine = "a database was closed";

        /// <summary>The audit line for a client's revocation of itself.</summary>
        private const string RevokedLine = "\"decision\":\"revoked\"";

        private readonly bool _afterTheFile;

        private LiveSession _session;
        private LiveReply _revocation;
        private readonly StringBuilder _asked = new StringBuilder();
        private bool _discarded;
        private bool _exited;
        private string _pluginLog;
        private string _audit;
        private bool _fileRevokes;
        private bool _fileAdmits;

        protected LiveCloseInsideSaveTests(bool afterTheFile)
        {
            _afterTheFile = afterTheFile;
        }

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            keePass.CloseTheDatabaseOnSave(_afterTheFile);

            string admissionKey;
            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                // Before KeePass starts, because a running KeePass serves the database it holds in
                // memory and would never see a record written to the file underneath it.
                keePass.Admit(admitted, "ops");
                admissionKey = CertificateDigest.Of(admitted);

                var driver = KeePassDriver.Start(keePass);
                if (!Logged(keePass, "serving a database with authority", StartTimeout))
                {
                    Assert.Fail("The plugin never reported serving the database KeePass was started with. " +
                                driver.Diagnostics + Environment.NewLine + keePass.ReadPluginLog());
                }

                var main = new IntPtr(driver.MainWindow.Current.NativeWindowHandle);
                var revoking = Task.Run(() => LiveGateClient.Send(
                    keePass.Port, keePass.NewGateName(), admitted, "DELETE", "/gate/certificate", null,
                    LiveGateClient.OnTheOperator));

                AnswerWhatKeePassAsks(keePass, main, revoking);

                _revocation = revoking.Wait(TimeSpan.FromSeconds(30))
                    ? revoking.Result
                    : new LiveReply { Failure = "no answer in time" };

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            _pluginLog = keePass.ReadPluginLog();
            _audit = File.Exists(keePass.AuditPath) ? File.ReadAllText(keePass.AuditPath) : string.Empty;
            _fileRevokes = keePass.FileHolds(KeePassGate.Store.StoreKinds.Withdrawn, KeePassGate.Store.StoreKinds.Certificate, admissionKey);
            _fileAdmits = keePass.ReadAdmissions().Any(record => record.AdmissionKey == admissionKey);

            TestContext.Out.WriteLine("KeePass asked: " + _asked);
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
            TestContext.Out.WriteLine("Audit:");
            TestContext.Out.WriteLine(_audit);
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
        public void The_client_reads_a_connection_that_ended()
        {
            Assert.That(_revocation.Answered, Is.False,
                "answered " + _revocation.Status + " " + _revocation.Body + ", over a connection the close was to end");
        }

        [Test]
        public void The_close_ended_the_connection_before_the_write_was_settled()
        {
            var connections = _pluginLog.IndexOf(ConnectionsLine, StringComparison.Ordinal);
            var closed = _pluginLog.IndexOf(ClosedLine, StringComparison.Ordinal);

            Assert.That(connections, Is.GreaterThanOrEqualTo(0), _pluginLog);
            Assert.That(closed, Is.GreaterThan(connections), _pluginLog);
        }

        [Test]
        public void KeePass_asks_only_what_a_close_of_a_changed_database_asks()
        {
            if (_afterTheFile)
            {
                Assert.That(_asked.ToString(), Is.Empty, "the save had landed, so the close had nothing to ask");
            }
            else
            {
                Assert.That(_discarded, Is.True, "the close did not ask whether to keep the changes: " + _asked);
            }
        }

        [Test]
        public void The_audit_holds_the_revocation_only_where_the_file_does()
        {
            var lines = _audit
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains(RevokedLine));

            if (_afterTheFile)
            {
                Assert.That(_fileRevokes, Is.True, "the save landed before the close, so the file holds the revocation");
                Assert.That(_fileAdmits, Is.False);
                Assert.That(lines, Is.EqualTo(1), _audit);
            }
            else
            {
                Assert.That(_fileRevokes, Is.False, "the changes were discarded before the file was written");
                Assert.That(_fileAdmits, Is.True, "so the file still admits the client");
                Assert.That(lines, Is.Zero, "and no line stands over a revocation nothing holds: " + _audit);
            }
        }

        [Test]
        public void KeePass_shut_down_by_itself()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly: something was still holding it.");
        }

        /// <summary>
        /// Answers each window KeePass raises while the revocation is out, other than its main
        /// window: the close's question by discarding the changes, and anything after it by OK.
        /// </summary>
        /// <remarks>
        /// Pressed from a task of its own, because a press that closes one modal window can open
        /// the next before the press returns: discarding is followed by KeePass's warning that the
        /// save failed. Watched until the revocation is settled and a few seconds beyond, since
        /// that warning may stand after the client has its connection closed.
        /// </remarks>
        private void AnswerWhatKeePassAsks(ThrowawayKeePass keePass, IntPtr main, Task<LiveReply> revoking)
        {
            var deadline = DateTime.UtcNow + Watched;
            DateTime? settled = null;

            while (DateTime.UtcNow < deadline)
            {
                if (revoking.IsCompleted && settled == null)
                {
                    settled = DateTime.UtcNow;
                }

                if (settled != null && DateTime.UtcNow - settled.Value > TimeSpan.FromSeconds(4))
                {
                    return;
                }

                var window = NativeWindows.Visible(keePass.Process).FirstOrDefault(found => found.Handle != main);
                if (window == null)
                {
                    Thread.Sleep(200);
                    continue;
                }

                Thread.Sleep(600);
                _asked.Append('\'').Append(window.Title).Append("' ");

                if (!_discarded && Press(window, "Discard"))
                {
                    _discarded = true;
                }
                else if (!Press(window, "OK"))
                {
                    _asked.Append("(nothing to press) ");
                }

                Thread.Sleep(800);
            }
        }

        /// <summary>Presses the first button whose caption starts with this, and answers whether one did.</summary>
        private static bool Press(NativeWindows.Window window, string caption)
        {
            AutomationElement element;
            try
            {
                element = AutomationElement.FromHandle(window.Handle);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }

            var button = Uia.Descendant(
                element,
                ControlType.Button,
                name => name.Replace("&", string.Empty).Trim().StartsWith(caption, StringComparison.OrdinalIgnoreCase));

            object pattern;
            if (button == null || !button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                return false;
            }

            var invoke = (InvokePattern)pattern;
            Task.Run(() => Thrown.By(invoke.Invoke)).Wait(TimeSpan.FromSeconds(5));
            return true;
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

    /// <summary>The trigger on Saved database file, which closes the database after the file is written.</summary>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveCloseAfterTheFileTests : LiveCloseInsideSaveTests
    {
        public LiveCloseAfterTheFileTests()
            : base(true)
        {
        }
    }

    /// <summary>
    /// The trigger on Saving database file, answered by discarding the changes before the file is
    /// written.
    /// </summary>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveCloseBeforeTheFileTests : LiveCloseInsideSaveTests
    {
        public LiveCloseBeforeTheFileTests()
            : base(false)
        {
        }
    }
}
