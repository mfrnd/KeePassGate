using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Audit;
using KeePassGate.Pairing;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// KeePass exited from its tray icon's menu while an approval dialog stands, and the audit
    /// read once it has gone.
    /// </summary>
    /// <remarks>
    /// The approval dialogs are not registered with KeePass's window manager, `PAIRING.md` §308,
    /// so the tray menu offers Exit under such a dialog, and Exit closes the main window from
    /// inside the dialog's own modal loop: KeePass closes every database, which takes the dialog
    /// away, and unloads the plugin, all in the same press, and only then can the dialog's loop
    /// hand its answer back. The unload waits for the audit lines queued by then and for nothing
    /// after, so the dialog's `unanswered` line has to be recorded as the close takes the dialog
    /// away. Written by the handler once the answer came back, it was missing from the file after
    /// most exits.
    ///
    /// **The waiting client is told nothing**, which is what `PAIRING.md` §308 states of an exit:
    /// the unload stops the gate, and stopping it closes every connection it holds, the connection
    /// spared for the dialog's answer included, before that answer exists.
    ///
    /// The menu is opened on the real notification icon where the taskbar or its overflow shows
    /// it, and otherwise by posting the shell's message for a right press to the icon's window,
    /// which is where WinForms' `NotifyIcon` shows its menu. Exit is then pressed with the pointer.
    /// </remarks>
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public abstract class LiveExitUnderApprovalTests
    {
        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(45);

        private LiveSession _session;
        private bool _dialogAppeared;
        private bool _exitPressed;
        private bool _exited;
        private string _outcome;
        private string[] _audit;
        private string _pluginLog;

        /// <summary>Part of the title of the dialog the request raises.</summary>
        protected abstract string DialogTitle { get; }

        /// <summary>The operation the dialog's audit line names.</summary>
        protected abstract string Operation { get; }

        /// <summary>What the database holds before KeePass starts.</summary>
        private protected abstract void Seed(ThrowawayKeePass keePass);

        /// <summary>What the operator does before the request is made.</summary>
        private protected abstract void Prepare(KeePassDriver driver, ThrowawayKeePass keePass);

        /// <summary>
        /// The request, which waits on the dialog, and null when it ended with no answer, or what
        /// it was answered.
        /// </summary>
        private protected abstract string Request(ThrowawayKeePass keePass);

        /// <summary>Lets go of what the fixture held for the request.</summary>
        protected abstract void Release();

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            Seed(keePass);

            var driver = KeePassDriver.Start(keePass);
            LivePairingTests.WaitUntilServing(keePass, driver);
            Prepare(driver, keePass);

            var asking = new Thread(() =>
            {
                var failed = Thrown.By(() => _outcome = Request(keePass));
                if (failed != null)
                {
                    _outcome = "threw " + failed;
                }
            }) { IsBackground = true };
            asking.Start();

            _dialogAppeared = driver.WaitForWindow(DialogTitle, DialogTimeout) != null;
            if (_dialogAppeared)
            {
                // Long enough for the dialog to have come up whole, as an operator would see it.
                Thread.Sleep(1000);
                _exitPressed = TrayExit.Press(driver);
            }

            var deadline = DateTime.UtcNow + ExitTimeout;
            while (_exitPressed && driver.ExitCode == int.MinValue && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }

            _exited = driver.ExitCode == 0;
            if (driver.ExitCode == int.MinValue)
            {
                // Whatever is left is closed the ordinary way, so the next fixture finds a clear
                // desktop; the tests below then fail on the exit that never happened.
                driver.ShutDown();
            }

            asking.Join(ExitTimeout);
            Release();

            _audit = File.Exists(keePass.AuditPath) ? File.ReadAllLines(keePass.AuditPath) : Array.Empty<string>();
            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Audit:");
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, _audit));
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
        public void KeePass_exits_from_its_tray_menu_under_the_dialog()
        {
            Assert.That(_dialogAppeared, Is.True, "the dialog never appeared");
            Assert.That(_exitPressed, Is.True, "the tray menu offered no Exit to press");
            Assert.That(_exited, Is.True, "KeePass did not exit by itself with code zero");
        }

        [Test]
        public void The_audit_holds_the_dialog_the_exit_took_away_as_unanswered()
        {
            Assert.That(_exitPressed, Is.True, "Exit was never pressed");
            Assert.That(
                _audit.Count(line => line.Contains("\"operation\":\"" + Operation + "\"")
                    && line.Contains("\"decision\":\"" + AuditEntry.Unanswered + "\"")),
                Is.EqualTo(1),
                "written down once, as the close at exit took the dialog away: " +
                string.Join(Environment.NewLine, _audit));
        }

        [Test]
        public void The_waiting_client_sees_its_connection_end_without_an_answer()
        {
            Assert.That(_exitPressed, Is.True, "Exit was never pressed");
            Assert.That(_outcome, Is.Null, "the exit stops the gate before the dialog's answer exists");
        }

        [Test]
        public void Nothing_escapes_to_the_incidents()
        {
            Assert.That(_pluginLog, Does.Not.Contain(" UNEXPECTED "));
        }
    }

    /// <summary>The exit under a renewal's dialog.</summary>
    [TestFixture]
    public sealed class LiveExitUnderARenewalTests : LiveExitUnderApprovalTests
    {
        private X509Certificate2 _renewing;

        protected override string DialogTitle
        {
            get { return "asking to renew"; }
        }

        protected override string Operation
        {
            get { return GateOperations.RenewCertificate; }
        }

        private protected override void Seed(ThrowawayKeePass keePass)
        {
            _renewing = keePass.IssueClientCertificate("agent-fictitious");
            keePass.Admit(_renewing, "reader");
        }

        private protected override void Prepare(KeePassDriver driver, ThrowawayKeePass keePass)
        {
        }

        private protected override string Request(ThrowawayKeePass keePass)
        {
            var reply = LiveGateClient.Send(
                keePass.Port, keePass.NewGateName(), _renewing, "POST", RenewalHandler.Path, "{}",
                LiveGateClient.OnTheOperator);
            return reply.Answered ? reply.Status + " " + reply.Body : null;
        }

        protected override void Release()
        {
            if (_renewing != null)
            {
                _renewing.Dispose();
            }
        }
    }

    /// <summary>
    /// The exit under a pairing's dialog, over a saved database, so that KeePass asks nothing
    /// before it closes it.
    /// </summary>
    [TestFixture]
    public sealed class LiveExitUnderAPairingTests : LiveExitUnderApprovalTests
    {
        private PairingClient _client;

        protected override string DialogTitle
        {
            get { return "asking to pair"; }
        }

        protected override string Operation
        {
            get { return GateOperations.PairRequest; }
        }

        private protected override void Seed(ThrowawayKeePass keePass)
        {
        }

        private protected override void Prepare(KeePassDriver driver, ThrowawayKeePass keePass)
        {
            var dialog = driver.OpenDatabaseSettings();
            Assert.That(driver.SelectGateTab(dialog), Is.Not.Null, "The KPG tab is missing.");
            Assert.That(driver.SelectPage(dialog, GateTab.ClientsPageText), Is.Not.Null, "The clients page is missing.");
            Assert.That(driver.PressButton(dialog, GateTab.InviteButtonText), Is.True, driver.Diagnostics);
            var token = driver.ValueOf(dialog, GateTab.InvitationBoxName);
            Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);

            // Accepting the settings dialog marks the database changed, and an exit over a
            // changed database stops at KeePass's question whether to save first.
            driver.Save();

            // Longer than the pairing client's own half minute, so the client never gives up
            // first.
            _client = new PairingClient(keePass.Port, token)
            {
                Timeout = (int)LiveGateClient.OnTheOperator.TotalMilliseconds
            };
            var hello = _client.Hello();
            Assert.That(hello.ServerProved, Is.True, "the hello did not prove the server: " + hello);
        }

        private protected override string Request(ThrowawayKeePass keePass)
        {
            var reply = _client.Request("agent-fictitious", "ops");
            return reply.Answered ? reply.ToString() : null;
        }

        protected override void Release()
        {
        }
    }

    /// <summary>Exit pressed on KeePass's tray menu with the real pointer.</summary>
    internal static class TrayExit
    {
        private const int TrayCallback = 0x0800;
        private const int RightButtonDown = 0x0204;
        private const int RightButtonUp = 0x0205;

        private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Opens the menu and presses Exit, and answers whether it was pressed.</summary>
        public static bool Press(KeePassDriver driver)
        {
            var process = System.Diagnostics.Process.GetProcessById(driver.MainWindow.Current.ProcessId);
            var before = new HashSet<IntPtr>(NativeWindows.VisibleUntitledToo(process).Select(window => window.Handle));

            var exit = OnTheRealIcon(process, before) ?? OnTheIconsWindow(process, before);
            if (exit == null || !exit.Current.IsEnabled)
            {
                TestContext.Progress.WriteLine("[exit under approval] the tray menu offered no Exit to press");
                Keyboard.SendEscape();
                return false;
            }

            Mouse.Click(exit);
            return true;
        }

        /// <summary>A right press on the notification area's button for KeePass.</summary>
        private static AutomationElement OnTheRealIcon(System.Diagnostics.Process process, HashSet<IntPtr> before)
        {
            var taskbar = AutomationElement.RootElement.FindFirst(
                TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
            if (taskbar == null)
            {
                return null;
            }

            var button = TrayButton(taskbar);
            if (button == null)
            {
                var chevron = taskbar
                    .FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>()
                    .FirstOrDefault(element =>
                        Uia.Name(element).IndexOf("hidden icons", StringComparison.OrdinalIgnoreCase) >= 0);
                if (chevron == null)
                {
                    return null;
                }

                Mouse.Click(chevron);
                Thread.Sleep(1200);
                foreach (AutomationElement top in AutomationElement.RootElement.FindAll(
                             TreeScope.Children, Condition.TrueCondition))
                {
                    if (ClassOf(top).IndexOf("Overflow", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        button = TrayButton(top);
                        if (button != null)
                        {
                            break;
                        }
                    }
                }
            }

            if (button == null)
            {
                Keyboard.SendEscape();
                return null;
            }

            Mouse.RightClick(button);
            var exit = Uia.WaitForOrNull(() => ExitItem(process, before), MenuTimeout);
            if (exit == null)
            {
                Keyboard.SendEscape();
                Thread.Sleep(500);
            }

            return exit;
        }

        /// <summary>
        /// The shell's message for a right press, posted to the window KeePass registered its
        /// notification icon with, with the pointer where the notification area is.
        /// </summary>
        private static AutomationElement OnTheIconsWindow(System.Diagnostics.Process process, HashSet<IntPtr> before)
        {
            IntPtr icon;
            uint id;
            if (!FindIcon(process, out icon, out id))
            {
                return null;
            }

            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            User32.SetPhysicalCursorPos(screen.Right - 160, screen.Bottom - 60);
            Thread.Sleep(200);
            User32.PostMessage(icon, TrayCallback, new IntPtr(id), new IntPtr(RightButtonDown));
            User32.PostMessage(icon, TrayCallback, new IntPtr(id), new IntPtr(RightButtonUp));
            return Uia.WaitForOrNull(() => ExitItem(process, before), MenuTimeout);
        }

        /// <summary>
        /// The window and identifier KeePass registered its notification icon under, found by
        /// asking the shell where the icon of each of its windows is.
        /// </summary>
        private static bool FindIcon(System.Diagnostics.Process process, out IntPtr icon, out uint id)
        {
            var wanted = (uint)process.Id;
            var candidates = new List<IntPtr>();
            User32.EnumWindows((window, parameter) =>
            {
                uint owner;
                if (User32.GetWindowThreadProcessId(window, out owner) != 0 && owner == wanted)
                {
                    candidates.Add(window);
                }

                return true;
            }, IntPtr.Zero);

            foreach (var window in candidates)
            {
                for (uint candidate = 0; candidate < 8; candidate++)
                {
                    var identifier = new User32.NotifyIconIdentifier
                    {
                        Size = (uint)Marshal.SizeOf<User32.NotifyIconIdentifier>(),
                        Window = window,
                        Id = candidate
                    };

                    User32.Rect where;
                    if (User32.Shell_NotifyIconGetRect(ref identifier, out where) == 0)
                    {
                        icon = window;
                        id = candidate;
                        return true;
                    }
                }
            }

            icon = IntPtr.Zero;
            id = 0;
            return false;
        }

        /// <summary>The notification area's button for KeePass under this element, or null.</summary>
        private static AutomationElement TrayButton(AutomationElement root)
        {
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                if (Uia.Name(element).IndexOf("KeePass", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // A running program's own button on the taskbar names KeePass as well, and a
                // press on that opens the taskbar's menu rather than KeePass's.
                var className = ClassOf(element);
                if ((className.StartsWith("SystemTray", StringComparison.Ordinal)
                        || string.Equals(Uia.Identifier(element), "NotifyItemIcon", StringComparison.Ordinal))
                    && !element.Current.BoundingRectangle.IsEmpty)
                {
                    return element;
                }
            }

            return null;
        }

        /// <summary>The Exit item of a menu KeePass opened since <paramref name="before"/>.</summary>
        private static AutomationElement ExitItem(System.Diagnostics.Process process, HashSet<IntPtr> before)
        {
            foreach (var window in NativeWindows.VisibleUntitledToo(process))
            {
                if (before.Contains(window.Handle))
                {
                    continue;
                }

                AutomationElement element;
                try
                {
                    element = AutomationElement.FromHandle(window.Handle);
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                var exit = element
                    .FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
                    .Cast<AutomationElement>()
                    .FirstOrDefault(item =>
                        string.Equals(Uia.Name(item).Replace("&", string.Empty), "Exit", StringComparison.Ordinal));
                if (exit != null)
                {
                    return exit;
                }
            }

            return null;
        }

        private static string ClassOf(AutomationElement element)
        {
            try
            {
                return element.Current.ClassName ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                return string.Empty;
            }
        }
    }
}
