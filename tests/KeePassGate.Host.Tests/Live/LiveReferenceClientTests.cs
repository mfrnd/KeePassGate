using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The reference client against the real plugin: PyKeePassGate pairs with a throwaway KeePass
    /// through an invitation from the real tab, then reads through the gate.
    /// </summary>
    /// <remarks>
    /// **The only place the two halves meet.** Every other tier runs PyKeePassGate against a
    /// stand-in, a vector or the document, and the pairing test beside this one pairs with the C# client
    /// in `tests/Shared`. So three things are held here and nowhere else: the identity the plugin
    /// exports with .NET Framework's PKCS#12 and the client reads with `cryptography`, the TLS 1.3
    /// handshake between Bouncy Castle's server and Python's `ssl`, and the plugin's framing under
    /// the client's reader.
    ///
    /// **The real package, run the way the gate runs it**: its command line through `uv run
    /// --frozen` in the repository, so what is exercised is what the lockfile pins and not a copy.
    /// Asked once before KeePass starts, which settles the environment where a slow first sync
    /// cannot eat into the time the approval dialog is waited for. Without `uv` the fixture is
    /// ignored, as a missing KeePass is.
    ///
    /// **The grant is written before KeePass starts**, for the reason `LiveApiTests` gives, and
    /// names the profile the client asks for, which the approval then names in the database. The
    /// invitation is read off the screen and handed over on standard input, the way the command
    /// line reads one it was not given.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveReferenceClientTests
    {
        private const string Name = "agent-python";
        private const string Profile = "ops";
        private const string Title = "an invented service";
        private const string User = "agent-invented";

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

        private LiveSession _session;
        private string _identity;

        private string _token;
        private string _approvalText;
        private Run _paired;
        private Run _status;
        private Run _added;
        private Run _listed;
        private Run _shown;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            // First, and on its own: it is the one step that can take minutes, and a failure in it
            // is about the environment rather than about the gate.
            var settled = Client("--help", SettleTimeout);
            Assert.That(settled.ExitCode, Is.Zero, "the client would not run: " + settled);

            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            _identity = Path.Combine(Path.GetDirectoryName(keePass.DatabasePath), "client");

            keePass.Grant("{\"" + Profile + "\":[\"create\",\"read\",\"list\"]}");

            var driver = KeePassDriver.Start(keePass);
            WaitUntilServing(keePass, driver);

            InviteFromTheTab(driver);
            Assert.That(_token, Is.Not.Null.And.Not.Empty, "nothing was on the tab to give the client");

            _paired = PairAndApprove(driver, keePass.Port);

            if (_paired.ExitCode == 0)
            {
                _status = Client("status" + Where(), CommandTimeout);
                _added = Client(
                    "add --title \"" + Title + "\" --username \"" + User + "\"" + Where(), CommandTimeout);
                _listed = Client("entries" + Where(), CommandTimeout);
                _shown = Client("show " + Created() + Where(), CommandTimeout);
            }

            _exited = driver.ShutDown();
            _pluginLog = keePass.ReadPluginLog();

            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
            TestContext.Out.WriteLine(driver.Diagnostics);
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
        public void The_client_pairs_through_an_invitation_from_the_tab()
        {
            Assert.That(_approvalText, Does.Contain(Name), "the approval dialog does not say who is asking");
            Assert.That(_paired.ExitCode, Is.Zero, _paired.ToString());
            Assert.That(_paired.Output, Does.Contain("paired as CN=" + Name));
            Assert.That(_paired.Output, Does.Contain("profiles approved: " + Profile));
        }

        [Test]
        public void The_identity_the_plugin_exported_is_one_the_client_can_read()
        {
            // The PKCS#12 half, which no other tier holds: written by .NET Framework's export and
            // read by `cryptography`, then converted to the PEM `ssl` loads.
            Assert.That(File.Exists(Path.Combine(_identity, "identity.pem")), Is.True, _paired.ToString());
            Assert.That(File.Exists(Path.Combine(_identity, "client.json")), Is.True, _paired.ToString());
        }

        [Test]
        public void The_status_route_answers_the_paired_client()
        {
            Assert.That(_status, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_status.ExitCode, Is.Zero, _status.ToString());
            Assert.That(_status.Output, Does.Contain("serving database"));
        }

        [Test]
        public void An_entry_the_client_creates_is_listed()
        {
            Assert.That(_added, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_added.ExitCode, Is.Zero, _added.ToString());
            Assert.That(Created(), Does.Match("^[0-9A-F]{32}$"), _added.ToString());

            Assert.That(_listed.ExitCode, Is.Zero, _listed.ToString());
            Assert.That(_listed.Output, Does.Contain(Created() + "  " + Title));
        }

        [Test]
        public void The_entry_reads_back_through_the_gate()
        {
            Assert.That(_shown, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_shown.ExitCode, Is.Zero, _shown.ToString());
            Assert.That(_shown.Output, Does.Contain("uuid:  " + Created()));
            Assert.That(_shown.Output, Does.Contain("Title: " + Title));
            Assert.That(_shown.Output, Does.Contain("UserName: " + User));
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so something was still holding it.");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        /// <summary>The identity to use, as every command after pairing names it.</summary>
        private string Where()
        {
            return " --directory \"" + _identity + "\"";
        }

        /// <summary>The identifier `add` printed, which is the whole of what it prints.</summary>
        private string Created()
        {
            return _added == null ? string.Empty : _added.Output.Trim();
        }

        /// <summary>Phase 0, on the real tab: the operator invites somebody and reads out the token.</summary>
        private void InviteFromTheTab(KeePassDriver driver)
        {
            var dialog = driver.OpenDatabaseSettings();
            Assert.That(driver.SelectGateTab(dialog), Is.Not.Null, "The KPG tab is missing, so nobody can be invited.");
            Assert.That(driver.SelectPage(dialog, GateTab.ClientsPageText), Is.Not.Null,
                "The clients page is missing, so nobody can be invited.");

            Assert.That(driver.PressButton(dialog, GateTab.InviteButtonText), Is.True, driver.Diagnostics);
            _token = driver.ValueOf(dialog, GateTab.InvitationBoxName);

            // With OK, which keeps the code: closing the dialog any other way takes it back.
            Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);
        }

        /// <summary>
        /// Phase 4, for real. The command line waits for the operator's answer, so it runs as its
        /// own process while this thread waits for the dialog and presses Approve.
        /// </summary>
        private Run PairAndApprove(KeePassDriver driver, int port)
        {
            var arguments = "pair --port " + port + " --name " + Name + " --profile-name " + Profile + Where();

            using (var pairing = Start(arguments))
            {
                var output = pairing.StandardOutput.ReadToEndAsync();
                var errors = pairing.StandardError.ReadToEndAsync();

                pairing.StandardInput.WriteLine(_token);
                pairing.StandardInput.Close();

                try
                {
                    var approval = driver.WaitForWindow("asking to pair", DialogTimeout);
                    if (approval == null)
                    {
                        var gone = Finish(pairing, output, errors, TimeSpan.FromSeconds(5));
                        Assert.Fail("The approval dialog never appeared. The client said: " + gone + " " +
                                    driver.Diagnostics);
                    }

                    _approvalText = Uia.Describe(approval, ControlType.Text);
                    Assert.That(driver.PressButton(approval, GateTab.ApproveButtonText), Is.True, driver.Diagnostics);

                    return Finish(pairing, output, errors, CommandTimeout);
                }
                finally
                {
                    End(pairing);
                }
            }
        }

        /// <summary>One command, from start to exit, with nothing on standard input.</summary>
        private static Run Client(string arguments, TimeSpan timeout)
        {
            Process process;
            try
            {
                process = Start(arguments);
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                Assert.Ignore("uv was not found, and PyKeePassGate is run through it: " + error.Message);
                throw;
            }

            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                process.StandardInput.Close();

                try
                {
                    return Finish(process, output, errors, timeout);
                }
                finally
                {
                    End(process);
                }
            }
        }

        /// <summary>
        /// `uv run --frozen pykeepassgate`, from the repository, the way the Client task runs it.
        /// </summary>
        private static Process Start(string arguments)
        {
            var info = new ProcessStartInfo
            {
                FileName = "uv",
                Arguments = "run --frozen pykeepassgate " + arguments,
                WorkingDirectory = Repository.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            return Process.Start(info);
        }

        private static Run Finish(Process process, Task<string> output, Task<string> errors, TimeSpan timeout)
        {
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                return new Run(int.MinValue, string.Empty, "still running after " + timeout.TotalSeconds + "s");
            }

            // The overload without a timeout, which is the one that waits for the redirected
            // streams to be read to their end as well.
            process.WaitForExit();
            return new Run(process.ExitCode, output.Result, errors.Result);
        }

        /// <summary>Ends a command that is still running, which only a failure leaves.</summary>
        private static void End(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(10000);
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception)
            {
                // Gone already, or going.
            }
        }

        /// <summary>
        /// Ready is the database being served, not the socket being open. `LivePairingTests` says
        /// why both lines, in that order.
        /// </summary>
        private static void WaitUntilServing(ThrowawayKeePass keePass, KeePassDriver driver)
        {
            WaitForLog(keePass, driver, "listening on 127.0.0.1:" + keePass.Port,
                "The plugin never reported a listening gate.");

            WaitForLog(keePass, driver, "serving a database with authority",
                "The plugin never reported serving the database KeePass was started with.");
        }

        private static void WaitForLog(
            ThrowawayKeePass keePass, KeePassDriver driver, string marker, string failure)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().Contains(marker))
                {
                    return;
                }

                Thread.Sleep(250);
            }

            Assert.Fail(failure + " " + driver.Diagnostics + Environment.NewLine +
                        "Log so far:" + Environment.NewLine + keePass.ReadPluginLog());
        }

        /// <summary>What one run of the command line printed, and how it ended.</summary>
        private sealed class Run
        {
            public Run(int exitCode, string output, string errors)
            {
                ExitCode = exitCode;
                Output = output ?? string.Empty;
                Errors = errors ?? string.Empty;
            }

            public int ExitCode { get; }

            public string Output { get; }

            public string Errors { get; }

            public override string ToString()
            {
                return "exit " + ExitCode + Environment.NewLine + Output + Errors;
            }
        }
    }
}
