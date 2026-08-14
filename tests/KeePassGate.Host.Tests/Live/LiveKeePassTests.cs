using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for step 4 of the build order, stated as the step states it:
    /// the plugin loads into KeePass, shows a KPG tab on the database settings dialog, and stops
    /// cleanly.
    /// </summary>
    /// <remarks>
    /// It drives a throwaway KeePass through UI automation, so it needs an interactive desktop and
    /// it takes some seconds. That is why it carries the Live category and is left out of the
    /// default test run. Everything it touches is created under `CommonApplicationData` and deleted
    /// afterwards, including the database.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveKeePassTests
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

        private string _workingDirectory;
        private ThrowawayKeePass _keePass;

        private bool _tabWasFound;
        private string _tabsOnTheDialog;
        private string _versionText;
        private string _textOnTheTab;
        private bool _tabWasSelected;
        private string _statusText;
        private bool _exited;
        private int _exitCode;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            string reason;
            if (!KeePassDev.MatchesTheAssemblyUnderTest(out reason))
            {
                Assert.Ignore(reason);
            }

            _workingDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KeePassGateLive",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workingDirectory);
            _keePass = ThrowawayKeePass.Create(_workingDirectory);

            var process = _keePass.Start();

            var mainWindow = Uia.WaitForOrNull(
                () => Window(process, title => title.IndexOf("KeePass", StringComparison.OrdinalIgnoreCase) >= 0),
                StartTimeout);

            if (mainWindow == null)
            {
                Assert.Fail(process.HasExited
                    ? "KeePass exited during start up with code " + process.ExitCode +
                      ". A KeePass that hands its command line to another instance does exactly this."
                    : "No KeePass main window after " + StartTimeout.TotalSeconds + "s. Windows were: " +
                      NativeWindows.Titles(process));
            }

            var dialog = OpenDatabaseSettings(process, mainWindow);

            var tab = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.TabItem, name => name == GateTab.Text),
                TimeSpan.FromSeconds(10));

            _tabWasFound = tab != null;
            _tabsOnTheDialog = Uia.Describe(dialog, ControlType.TabItem);

            if (tab != null)
            {
                _tabWasSelected = Select(tab);
                _versionText = TextStartingWith(dialog, "Version ");
                _statusText = TextStartingWith(dialog, "The plugin is loaded");
                _textOnTheTab = Uia.Describe(dialog, ControlType.Text);
            }

            CloseDialog(dialog);
            _exited = ShutDown(process);
            _exitCode = _exited ? process.ExitCode : int.MinValue;
            _pluginLog = _keePass.ReadPluginLog();

            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_keePass != null)
            {
                _keePass.Dispose();
            }

            try
            {
                if (_workingDirectory != null && Directory.Exists(_workingDirectory))
                {
                    Directory.Delete(_workingDirectory, true);
                }
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException)
            {
                // A KeePass still letting go of its own files holds them either way round:
                // locked reads as one of these and denied as the other. Neither is worth
                // failing a teardown over, and `ThrowawayKeePass` already tolerates both.
            }
        }

        [Test]
        public void The_database_settings_dialog_shows_a_KPG_tab()
        {
            Assert.That(_tabWasFound, Is.True, "Tabs on the dialog were: " + _tabsOnTheDialog);
        }

        [Test]
        public void The_tab_sits_after_the_tabs_KeePass_brings()
        {
            Assert.That(_tabsOnTheDialog, Does.EndWith(GateTab.Text));
        }

        [Test]
        public void The_tab_reports_the_version_that_was_loaded()
        {
            Assert.That(_versionText, Is.EqualTo("Version " + GateVersion.Current),
                "Tab selected: " + _tabWasSelected + ". Text on the dialog was: " + _textOnTheTab);
        }

        [Test]
        public void The_tab_says_plainly_that_nothing_is_served_yet()
        {
            Assert.That(_statusText, Is.EqualTo(GateStatusPanelFactory.DefaultStatus),
                "Tab selected: " + _tabWasSelected + ". Text on the dialog was: " + _textOnTheTab);
        }

        [Test]
        public void The_plugin_recorded_that_it_started_and_decorated_the_dialog()
        {
            Assert.That(_pluginLog, Does.Contain("initializing version " + GateVersion.Current));
            Assert.That(_pluginLog, Does.Contain("attached the " + GateTab.Text + " tab"));
        }

        [Test]
        public void KeePass_shut_down_by_itself_and_reported_success()
        {
            Assert.That(_exited, Is.True, "KeePass was still running after " + ShutdownTimeout.TotalSeconds + "s.");
            Assert.That(_exitCode, Is.Zero);
        }

        [Test]
        public void The_plugin_stopped_before_KeePass_did()
        {
            Assert.That(_pluginLog, Does.Contain("detached the " + GateTab.Text + " tab"));
            Assert.That(_pluginLog, Does.Contain("stopped"));
        }

        [Test]
        public void Nothing_was_reported_as_a_failure_in_the_whole_session()
        {
            var failures = _pluginLog
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(" ERROR ") || line.Contains(" WARN ") || line.Contains(" UNEXPECTED "))
                .ToList();

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// Opens File, Database Settings and waits for the dialog.
        /// </summary>
        /// <remarks>
        /// In two phases, and the split is the whole trick. Reading the accessibility tree while a
        /// menu is open dismisses the menu, and choosing from a menu through the accessibility tree
        /// does not return until the dialog it opens is closed. So the keys are discovered first,
        /// with the menu opened and closed through the tree, and then the menu is driven purely
        /// from the keyboard with nothing watching. Driven twice if the first attempt produces
        /// nothing, because a menu that loses the foreground swallows the keystroke in silence.
        /// </remarks>
        private static AutomationElement OpenDatabaseSettings(Process process, AutomationElement mainWindow)
        {
            var diagnostics = new System.Text.StringBuilder();

            var file = Uia.WaitFor(
                "the File menu",
                () => Uia.Descendant(mainWindow, ControlType.MenuItem, name => Strip(name) == "File"));

            var openMenu = Keyboard.ParseAccessKey(AccessKeyOf(file));
            Assert.That(openMenu, Is.Not.Null, "The File menu reports no access key.");

            Uia.Expand(file);
            var settings = Uia.WaitFor(
                "the Database Settings menu item",
                () => MenuItemStartingWith(process, mainWindow, "Database Settings"));
            var chooseItem = Keyboard.ParseAccessKey(AccessKeyOf(settings));
            Uia.Collapse(file);

            Assert.That(chooseItem, Is.Not.Null, "The Database Settings item reports no access key.");
            diagnostics.AppendLine("keys: " + openMenu + " then " + chooseItem);

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var inFront = Keyboard.BringToFront(mainWindow);
                diagnostics.AppendLine("attempt " + attempt + ": KeePass in front: " + inFront);

                Keyboard.Send(openMenu.Value);
                Keyboard.Send(chooseItem.Value);

                var dialog = Uia.WaitForOrNull(
                    () => Window(process, title =>
                        title.IndexOf("Database Settings", StringComparison.OrdinalIgnoreCase) >= 0),
                    TimeSpan.FromSeconds(15));

                if (dialog != null)
                {
                    TestContext.Out.WriteLine(diagnostics.ToString());
                    return dialog;
                }

                diagnostics.AppendLine("  no dialog. Windows: " + NativeWindows.Titles(process));
                Keyboard.SendEscape();
            }

            throw new TimeoutException(
                "The database settings dialog never opened." + Environment.NewLine + diagnostics);
        }

        /// <summary>
        /// A window found through the window manager, handed back as an accessibility element.
        /// Looking it up natively is the reliable half; reading it is what the tree is good at.
        /// </summary>
        private static AutomationElement Window(Process process, Func<string, bool> titleMatches)
        {
            var window = NativeWindows.Find(process, titleMatches);
            if (window == null)
            {
                return null;
            }

            try
            {
                return AutomationElement.FromHandle(window.Handle);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        private static string AccessKeyOf(AutomationElement element)
        {
            try
            {
                return element.Current.AccessKey;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        /// <summary>
        /// A drop down menu is its own window, so the item is looked for both under the main
        /// window and among the process's other top level windows.
        /// </summary>
        private static AutomationElement MenuItemStartingWith(Process process, AutomationElement mainWindow, string prefix)
        {
            var underMainWindow = Uia.Descendant(mainWindow, ControlType.MenuItem,
                name => Strip(name).StartsWith(prefix, StringComparison.Ordinal));
            if (underMainWindow != null)
            {
                return underMainWindow;
            }

            var windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));

            return windows
                .Cast<AutomationElement>()
                .Select(w => Uia.Descendant(w, ControlType.MenuItem,
                    name => Strip(name).StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(item => item != null);
        }

        /// <summary>
        /// Selects the tab, because a tab page that has never been shown puts nothing in the
        /// accessibility tree to read.
        /// </summary>
        private static bool Select(AutomationElement tab)
        {
            object pattern;
            if (!tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                return false;
            }

            var selection = (SelectionItemPattern)pattern;
            selection.Select();
            Thread.Sleep(500);
            return selection.Current.IsSelected;
        }

        private static string TextStartingWith(AutomationElement dialog, string prefix)
        {
            var element = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.Text, name => name.StartsWith(prefix, StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));

            return element == null ? null : Uia.Name(element);
        }

        /// <summary>
        /// Escape rather than the Cancel button, and Cancel rather than OK. Escape keeps the
        /// automation client out of the modal loop, and canceling is what the design demands of
        /// any test that opens this dialog: KeePass writes back a snapshot of the group's custom
        /// data on OK, so pressing it is never incidental.
        /// </summary>
        private static void CloseDialog(AutomationElement dialog)
        {
            Keyboard.BringToFront(dialog);
            Keyboard.SendEscape();
        }

        private static bool ShutDown(Process process)
        {
            process.CloseMainWindow();
            if (process.WaitForExit((int)ShutdownTimeout.TotalMilliseconds))
            {
                return true;
            }

            return false;
        }

        private static string Strip(string menuText)
        {
            return (menuText ?? string.Empty).Replace("&", string.Empty).Trim();
        }
    }
}
