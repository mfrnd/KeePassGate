using System;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Where a modal dialog's close is raised when its OK is pressed inside a pump that runs under
    /// the dialog's own loop, as a save of the plugin's own pumps under the settings dialog.
    /// </summary>
    /// <remarks>
    /// **The press is handled inside the pump, and the close only once the pump has returned.**
    /// `FormClosing` and `FormClosed` of a modal form are raised by the form's own modal loop, and
    /// `Application.DoEvents` runs a loop that raises neither. So what the KPG tab does at the
    /// settings dialog's OK never runs inside a save of the plugin's own, which runs from
    /// `Application.Idle` inside that same modal loop.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class ModalDialogCloseTests
    {
        [Test]
        public void A_modal_dialog_s_close_is_raised_only_once_a_pump_under_it_has_returned()
        {
            using (var dialog = new Form
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Left = -2000,
                Top = -2000
            })
            using (var ok = new Button { DialogResult = DialogResult.OK })
            using (var guard = new System.Windows.Forms.Timer { Interval = 30000 })
            {
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;

                var inside = false;
                var ran = false;
                var clickedInside = false;
                var closingInside = false;
                var closed = false;
                var closedInside = false;

                ok.Click += (sender, e) => clickedInside |= inside;
                dialog.FormClosing += (sender, e) => closingInside |= inside;
                dialog.FormClosed += (sender, e) =>
                {
                    closed = true;
                    closedInside |= inside;
                };

                // Closes the dialog where nothing else does, so that a failure ends the test
                // rather than the run.
                guard.Tick += (sender, e) =>
                {
                    guard.Stop();
                    dialog.DialogResult = DialogResult.Abort;
                };
                guard.Start();

                // A pump under the dialog's loop, as KeePass's save runs a pump: the press is
                // posted to the dialog and handled while the pump runs.
                EventHandler idle = null;
                idle = (sender, e) =>
                {
                    Application.Idle -= idle;
                    ran = true;
                    inside = true;
                    dialog.BeginInvoke(new Action(ok.PerformClick));
                    Application.DoEvents();
                    Application.DoEvents();
                    Thread.Sleep(50);
                    Application.DoEvents();
                    inside = false;
                };
                Application.Idle += idle;

                var result = dialog.ShowDialog();

                Assert.That(ran, Is.True, "the pump under the dialog never ran");
                Assert.That(result, Is.EqualTo(DialogResult.OK));
                Assert.That(clickedInside, Is.True, "the press is handled inside the pump");
                Assert.That(closed, Is.True);
                Assert.That(closingInside, Is.False, "FormClosing is not raised inside the pump");
                Assert.That(closedInside, Is.False, "FormClosed is not raised inside the pump");
            }
        }
    }
}
