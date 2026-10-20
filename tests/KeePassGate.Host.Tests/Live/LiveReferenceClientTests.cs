using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// through an invitation from the real tab, then reads and writes through the gate until
    /// KeePass is locked under it.
    /// </summary>
    /// <remarks>
    /// **The only place the two halves meet.** Every other tier runs PyKeePassGate against a
    /// stand-in, a vector or the document, and the pairing test beside this one pairs with the C# client
    /// in `tests/Shared`. So three things are held here and nowhere else: the identity the plugin
    /// exports with .NET Framework's PKCS#12 and the client reads with `cryptography`, the TLS 1.3
    /// handshake between Bouncy Castle's server and Python's `ssl`, and the plugin's framing under
    /// the client's reader. And two of the client's answers are held against the plugin's own: a
    /// protected value reaches it through the field route only because the rule grants `P`, and
    /// the two refusals a client waits out, a dialog open on the entry and a lock, arrive under the
    /// names a program catches them by.
    ///
    /// **The real package, run the way the gate runs it**: its command line through `uv run
    /// --frozen` in the repository, so what is exercised is what the lockfile pins and not a copy.
    /// Asked once before KeePass starts, which settles the environment where a slow first sync
    /// cannot eat into the time the approval dialog is waited for. Without `uv` the fixture is
    /// ignored, as a missing KeePass is. What the command line does not offer, reading one field
    /// on its own route and changing a password, goes through the library with
    /// `tests/pykeepassgate/live_calls.py`, run the same way.
    ///
    /// **The grant is written before KeePass starts**, for the reason `LiveApiTests` gives, and
    /// names the profile the client asks for, which the approval then names in the database. It
    /// grants `protected` with `create`, `read`, `update` and `list`, so the password the client
    /// stores is one it may read back and change. The invitation is read off the screen and handed
    /// over on standard input, the way the command line reads one it was not given.
    ///
    /// **The lock comes last**, because nothing after it answers anything but `503 locked`, and a
    /// test unlocking KeePass again would be typing the master password into it.
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
        private const string Stored = "not-a-real-password-live";
        private const string Rotated = "a-rotated-invented-password";
        private const string TurnedAway = "a-password-a-dialog-turns-away";

        /// <summary>What the plugin logs when the lock reaches it, as `LiveLockTests` reads it.</summary>
        private const string LockedLine = "locked, answering with the responder";

        /// <summary>What the plugin logs, at a level worth a look, for the write this test turns away.</summary>
        private const string BusyLine =
            "a change was refused as busy: an entry dialog is open on the entry this would be written to";

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(20);

        private LiveSession _session;
        private string _identity;

        private string _token;
        private string _approvalText;
        private Run _paired;
        private Run _status;
        private Run _added;
        private Run _listed;
        private Run _shown;
        private Run _readProtected;
        private Run _rotated;
        private Run _readRotated;
        private Run _busy;
        private Run _afterBusy;
        private string _notLocked;
        private Run _locked;
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

            keePass.Grant("{\"" + Profile + "\":[\"create\",\"read\",\"update\",\"list\",\"protected\"]}");

            var driver = KeePassDriver.Start(keePass);
            WaitUntilServing(keePass, driver);

            InviteFromTheTab(driver);
            Assert.That(_token, Is.Not.Null.And.Not.Empty, "nothing was on the tab to give the client");

            _paired = PairAndApprove(driver, keePass.Port);

            if (_paired.ExitCode == 0)
            {
                _status = Client("status" + Where(), CommandTimeout);
                _added = Client(
                    "add --title \"" + Title + "\" --username \"" + User + "\" --password" + Where(),
                    CommandTimeout, Stored);
                _listed = Client("entries" + Where(), CommandTimeout);
                _shown = Client("show " + Created() + Where(), CommandTimeout);

                _readProtected = Calls("field " + Created() + " Password", CommandTimeout);
                _rotated = Calls("password " + Created(), CommandTimeout, Rotated);
                _readRotated = Calls("field " + Created() + " Password", CommandTimeout);

                // The entry the client made is the only one there is, so it is the one the list
                // shows first. While its dialog is open a write to it is one that dialog's OK
                // would take back, so the gate turns it away, and Escape then leaves the entry
                // as it was.
                var dialog = driver.OpenEntryDialog();
                _busy = Calls("password " + Created(), CommandTimeout, TurnedAway);
                driver.CloseDialog(dialog);
                _afterBusy = Calls("field " + Created() + " Password", CommandTimeout);

                driver.LockWorkspace();
                if (!Logged(keePass, LockedLine, LockTimeout))
                {
                    _notLocked = "KeePass was showing " + NativeWindows.Titles(keePass.Process) +
                                 Environment.NewLine + driver.Diagnostics;
                }

                _locked = Client("status" + Where(), CommandTimeout);
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
            Assert.That(_shown.Output, Does.Contain("Password: " + Stored + " (protected)"),
                "the rule grants protected, so the value arrives rather than being withheld");
        }

        [Test]
        public void A_protected_value_reads_through_the_field_route()
        {
            Assert.That(_readProtected, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_readProtected.ExitCode, Is.Zero, _readProtected.ToString());
            Assert.That(_readProtected.Output.Trim(), Is.EqualTo(Stored));
        }

        [Test]
        public void A_password_the_client_changes_reads_back_as_the_new_one()
        {
            Assert.That(_rotated, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_rotated.ExitCode, Is.Zero, _rotated.ToString());
            Assert.That(_readRotated.ExitCode, Is.Zero, _readRotated.ToString());
            Assert.That(_readRotated.Output.Trim(), Is.EqualTo(Rotated));
        }

        [Test]
        public void A_write_under_an_open_entry_dialog_is_refused_as_busy_and_changes_nothing()
        {
            Assert.That(_busy, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_busy.ExitCode, Is.EqualTo(1), _busy.ToString());
            Assert.That(_busy.Errors, Does.Contain("GateBusy: "), _busy.ToString());

            Assert.That(_afterBusy.ExitCode, Is.Zero, _afterBusy.ToString());
            Assert.That(_afterBusy.Output.Trim(), Is.EqualTo(Rotated), "the refused write wrote nothing");
        }

        [Test]
        public void A_locked_KeePass_is_met_as_a_locked_database()
        {
            Assert.That(_locked, Is.Not.Null, "the client never paired, so nothing was asked");
            Assert.That(_notLocked, Is.Null, "The plugin never reported the lock. " + _notLocked);
            Assert.That(_locked.ExitCode, Is.EqualTo(1), _locked.ToString());
            Assert.That(_locked.Errors, Does.Contain("DatabaseLocked: "), _locked.ToString());
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so something was still holding it.");
        }

        [Test]
        public void The_only_thing_reported_was_the_write_this_test_turned_away()
        {
            // Once, for that write: filtering the phrase alone would also pass a session in which
            // every write was refused as busy, which is the failure the writes above exist to see.
            var failures = LiveSession.FailuresIn(_pluginLog);
            var busy = failures.Where(line => line.Contains(BusyLine)).ToArray();
            var unexpected = failures.Except(busy).ToArray();

            Assert.That(busy, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
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

            using (var pairing = Start("pykeepassgate " + arguments))
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

        /// <summary>One command of the command line, from start to exit.</summary>
        private static Run Client(string arguments, TimeSpan timeout, string input = null)
        {
            return Execute("pykeepassgate " + arguments, timeout, input);
        }

        /// <summary>
        /// One call through the library, with `live_calls.py`, for the identity this session paired.
        /// </summary>
        private Run Calls(string arguments, TimeSpan timeout, string input = null)
        {
            var program = Path.Combine("tests", "pykeepassgate", "live_calls.py");
            return Execute("python \"" + program + "\" --directory \"" + _identity + "\" " + arguments, timeout, input);
        }

        /// <summary>
        /// One program `uv` runs, from start to exit, with one line on standard input if given and
        /// nothing otherwise.
        /// </summary>
        private static Run Execute(string arguments, TimeSpan timeout, string input)
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
                if (input != null)
                {
                    process.StandardInput.WriteLine(input);
                }

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
        /// `uv run --frozen` and what it is to run, from the repository, the way the Client task
        /// runs it.
        /// </summary>
        private static Process Start(string arguments)
        {
            var info = new ProcessStartInfo
            {
                FileName = "uv",
                Arguments = "run --frozen " + arguments,
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
            if (!Logged(keePass, marker, StartTimeout))
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

        /// <summary>What one run of a program printed, and how it ended.</summary>
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
