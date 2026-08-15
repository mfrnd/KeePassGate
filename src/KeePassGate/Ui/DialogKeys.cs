using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Takes Enter and Escape away from a dialog for as long as something else needs them, and
    /// gives them back afterwards.
    /// </summary>
    /// <remarks>
    /// **A dropdown on a dialog does not own the keyboard.** A form with an accept button treats
    /// Enter as that button and Escape as its cancel button, wherever the focus is, and a dropdown
    /// shown over it is not an exception: choosing verbs in the grant picker and pressing Enter
    /// pressed OK on the group dialog, which closed it. Escape did the same to Cancel, which threw
    /// the rule away.
    ///
    /// That is worse than it sounds on a dialog whose OK writes a copy of the group back over the
    /// group: the operator was one keystroke into a rule and the dialog was gone, with no way to
    /// tell which of the two had happened.
    ///
    /// So the buttons are unhooked while the dropdown is open and hooked back when it closes,
    /// which is the only way a form can be told that Enter means something else for a moment. A
    /// token rather than two calls, so that the restoring half cannot be forgotten.
    ///
    /// **One suspension per form, however many tokens hold it.** A picker lets go of its token a
    /// message after it closes, and a click landing in between opens the next picker, whose token
    /// is taken while the first one still holds the buttons. Each token recording the buttons for
    /// itself, the second recorded the nothing the first had left, the first gave OK and Cancel
    /// back while the second picker was still open, and the second then put its nothing back: the
    /// dialog answered neither Enter nor Escape for as long as it stayed open. So the first token
    /// takes the buttons away, every token counts, and the last one let go of puts back what the
    /// first one found, in whichever order they are let go of.
    /// </remarks>
    public sealed class DialogKeys : IDisposable
    {
        /// <summary>
        /// What each form had before its first outstanding token took it, and how many hold it.
        /// </summary>
        /// <remarks>
        /// Weakly keyed, so a form that closes with a token nobody let go of is not kept alive by
        /// this.
        /// </remarks>
        private static readonly ConditionalWeakTable<Form, Held> Outstanding =
            new ConditionalWeakTable<Form, Held>();

        private readonly Form _form;

        private bool _restored;

        private DialogKeys(Form form)
        {
            _form = form;

            if (form == null)
            {
                _restored = true;
                return;
            }

            Held held;
            if (!Outstanding.TryGetValue(form, out held))
            {
                held = new Held(form.AcceptButton, form.CancelButton);
                Outstanding.Add(form, held);

                form.AcceptButton = null;
                form.CancelButton = null;
            }

            held.Count++;
        }

        /// <summary>
        /// Suspends them on whatever form this control is on. A control on no form gets a token
        /// that does nothing, which is what a test that never showed anything has.
        /// </summary>
        public static DialogKeys Suspend(Control control)
        {
            return new DialogKeys(control == null ? null : control.FindForm());
        }

        /// <summary>Whether anything was actually taken away, which a test can ask.</summary>
        public bool Suspended
        {
            get { return !_restored; }
        }

        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;

            Held held;
            if (!Outstanding.TryGetValue(_form, out held))
            {
                return;
            }

            held.Count--;
            if (held.Count > 0)
            {
                return;
            }

            Outstanding.Remove(_form);
            _form.AcceptButton = held.Accept;
            _form.CancelButton = held.Cancel;
        }

        /// <summary>The buttons a form had before it was suspended, and how many tokens hold it.</summary>
        private sealed class Held
        {
            public Held(IButtonControl accept, IButtonControl cancel)
            {
                Accept = accept;
                Cancel = cancel;
            }

            public IButtonControl Accept { get; }

            public IButtonControl Cancel { get; }

            public int Count { get; set; }
        }
    }
}
