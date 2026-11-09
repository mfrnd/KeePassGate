using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The keys that could answer the authority chooser, pressed on one shown where nobody sees it,
    /// since a key reaches a window only once it is shown.
    /// </summary>
    /// <remarks>
    /// Keeping retires every other authority for good, so it takes a press: no letter and no Enter
    /// keeps, wherever the focus is. A button that has the focus hands a bare letter to the dialog's
    /// mnemonics, and becomes the button Enter presses, which the approval dialogs measured first.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class AuthorityChooserKeysTests
    {
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmChar = 0x0102;

        [TestCase(GateTab.ChooserCancelButtonName)]
        [TestCase(GateTab.KeepButtonName)]
        public void A_bare_K_on_either_button_keeps_nothing(string name)
        {
            using (var dialog = Shown())
            {
                var button = Focus(ControlTree.FindByName<Button>(dialog, name));

                User32.PostMessage(button.Handle, WmChar, (IntPtr)'k', IntPtr.Zero);
                OffScreenForm.Pump();

                Assert.That(dialog.Chosen, Is.Null, "a K on " + name + " kept");
            }
        }

        [Test]
        public void Enter_on_a_focused_Keep_keeps_nothing()
        {
            using (var dialog = Shown())
            {
                var keep = Focus(ControlTree.FindByName<Button>(dialog, GateTab.KeepButtonName));
                Assume.That(keep.Enabled, Is.True);

                User32.PostMessage(keep.Handle, WmKeyDown, (IntPtr)(int)Keys.Enter, IntPtr.Zero);
                User32.PostMessage(keep.Handle, WmKeyUp, (IntPtr)(int)Keys.Enter, IntPtr.Zero);
                OffScreenForm.Pump();

                Assert.That(dialog.Chosen, Is.Null);
            }
        }

        [Test]
        public void Space_on_a_focused_Keep_still_keeps()
        {
            // The press that is left, beside a click: what makes the two above about stray keys
            // rather than about a button that cannot be pressed at all.
            using (var dialog = Shown())
            {
                var keep = Focus(ControlTree.FindByName<Button>(dialog, GateTab.KeepButtonName));

                User32.PostMessage(keep.Handle, WmKeyDown, (IntPtr)(int)Keys.Space, IntPtr.Zero);
                User32.PostMessage(keep.Handle, WmKeyUp, (IntPtr)(int)Keys.Space, IntPtr.Zero);
                OffScreenForm.Pump();

                Assert.That(dialog.Chosen, Is.Not.Null);
            }
        }

        /// <summary>A chooser over two authorities, shown where nobody sees it.</summary>
        private static AuthorityChooserForm Shown()
        {
            var values = new InMemoryKeyValueStore();
            var service = new AuthorityService(values, null);

            Assume.That(service.Create().Succeeded, Is.True);
            var first = values.Keys.Single(key => AuthorityKeys.FingerprintInAuthority(key) != null);
            var document = values.Get(first);
            values.Remove(first);
            Assume.That(service.Create().Succeeded, Is.True);
            values.Plant(first, document);

            AuthorityChoice choice;
            using (var reading = service.Read())
            {
                Assume.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                choice = AuthorityChoice.From(reading, new AdmissionStore(values, null).All(), null, null);
            }

            var dialog = new AuthorityChooserForm(choice, null)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false
            };

            dialog.Show();
            OffScreenForm.Pump();
            return dialog;
        }

        private static T Focus<T>(T control)
            where T : Control
        {
            control.Focus();
            OffScreenForm.Pump();
            Assert.That(control.Focused, Is.True, "the key has to start where an operator would press it");
            return control;
        }
    }
}
