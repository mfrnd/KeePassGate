using System;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A real KeePass's group dialog closed by another program while a grant picker is open on it.
    /// </summary>
    /// <remarks>
    /// Every way an operator has of leaving that dialog closes the picker first: a press outside
    /// it, and Alt and F4. A close posted to the dialog's window, which is what a program closing
    /// it sends, leaves the picker open, and WinForms closes the picker after the dialog and the
    /// panel under it have gone. The plugin disposed the dropdown inside its own `Closed` there,
    /// and KeePass showed its fatal error and ended without unloading the plugin.
    /// `GrantPickerTests` holds the same sequence on a form of its own; this holds it on KeePass's
    /// dialog, where the dialog is shown modally and destroyed as soon as it has closed.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveGrantPickerCloseTests
    {
        private const string Profile = "reader";
        private const int CloseMessage = 0x0010;

        private LiveSession _session;
        private bool _picker;
        private bool _dialogWent;
        private bool _running;
        private bool _exited;
        private string _diagnostics;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin();

            // A profile named, so that the profile picker has something to offer and opens.
            _session.KeePass.NameProfiles(Profile);

            var driver = KeePassDriver.Start(_session.KeePass);
            var dialog = driver.OpenGroupDialog();
            var window = new IntPtr(dialog.Current.NativeWindowHandle);

            var tab = Uia.WaitFor(
                "the KPG tab",
                () => Uia.Descendant(dialog, ControlType.TabItem, name => name == GrantTab.Text));
            ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Thread.Sleep(700);

            var grid = Uia.WaitFor(
                "the grant table",
                () => Uia.ById(dialog, ControlType.Table, GrantTab.GridControlName));
            var cell = Uia.WaitFor(
                "the Profile cell",
                () => Uia.Descendant(grid, ControlType.Edit,
                    name => name != null && name.StartsWith("Profile", StringComparison.Ordinal)));

            Keyboard.Front(dialog);
            Mouse.Click(cell);
            _picker = driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.FromSeconds(10)) != null;

            TestNativeMethods.PostMessage(window, CloseMessage, IntPtr.Zero, IntPtr.Zero);

            var process = _session.KeePass.Process;
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTime.UtcNow < until && !process.HasExited && TestNativeMethods.IsWindowVisible(window))
            {
                Thread.Sleep(150);
            }

            // Then long enough for the change of activation that has WinForms close the picker,
            // which follows the dialog's going by a message or two.
            Thread.Sleep(3000);

            _dialogWent = !TestNativeMethods.IsWindowVisible(window);
            process.Refresh();
            _running = !process.HasExited;

            if (_running)
            {
                _exited = driver.ShutDown();
                _diagnostics = driver.Diagnostics;
            }

            _pluginLog = _session.KeePass.ReadPluginLog();
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
        public void KeePass_goes_on_after_the_dialog_goes_and_then_exits_cleanly()
        {
            Assert.That(_picker, Is.True, "the picker did not open, so nothing is measured");
            Assert.That(_dialogWent, Is.True, "the close posted to the dialog did not close it");
            Assert.That(_running, Is.True, "KeePass ended with the dialog");
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly afterwards: " + _diagnostics);
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }
    }
}
