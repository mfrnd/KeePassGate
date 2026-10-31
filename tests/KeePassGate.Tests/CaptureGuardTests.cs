using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Pairing;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// KeePass's "Prevent certain screen captures" reaching this plugin's own windows, which KeePass
    /// applies only to windows it registers.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class CaptureGuardTests
    {
        private const uint MonitorOnly = 0x00000001;

        [TearDown]
        public void TearDown()
        {
            CaptureGuard.Wanted = () => false;
        }

        [TestCase(true, MonitorOnly)]
        [TestCase(false, 0u)]
        public void An_approval_dialog_keeps_out_of_captures_exactly_when_KeePass_does(bool wanted, uint affinity)
        {
            CaptureGuard.Wanted = () => wanted;

            using (var dialog = new RenewalApprovalForm(
                       new RenewalRequest(
                           "aaaabbbbccccdddd",
                           "Invented Vault",
                           "agent-fictitious",
                           DateTimeOffset.UtcNow,
                           DateTimeOffset.UtcNow.AddDays(30),
                           new[] { "ops" },
                           Array.Empty<string>(),
                           "127.0.0.1:60001",
                           null),
                       TimeSpan.FromMinutes(2),
                       null))
            {
                dialog.CreateControl();
                var handle = dialog.Handle;

                uint read;
                Assert.That(User32.GetWindowDisplayAffinity(handle, out read), Is.True);
                Assert.That(read, Is.EqualTo(affinity));
            }
        }

        [Test]
        public void The_window_under_Tools_keeps_out_of_captures_too()
        {
            CaptureGuard.Wanted = () => true;

            using (var window = new GateStatusForm(new Quiet(), null))
            {
                window.CreateControl();

                uint read;
                Assert.That(User32.GetWindowDisplayAffinity(window.Handle, out read), Is.True);
                Assert.That(read, Is.EqualTo(MonitorOnly));
            }
        }

        private sealed class Quiet : IGateStatus
        {
            public bool IsListening
            {
                get { return false; }
            }

            public int Port
            {
                get { return 0; }
            }

            public string Refusal
            {
                get { return null; }
            }
        }
    }
}
