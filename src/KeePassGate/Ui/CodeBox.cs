using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A read-only box for a code, which shows in gray what it means to be empty while it is.
    /// </summary>
    /// <remarks>
    /// **Drawn here, because Windows draws a cue banner only in a box that can be typed into.**
    /// `EM_SETCUEBANNER` is accepted by a read-only edit control and read back from one, and nothing
    /// is drawn, whether the box is focused or not: measured on Windows 11 under visual styles,
    /// beside an editable box that drew the same banner. And a box made editable to get one would be a box inviting
    /// somebody to type a code into the page that hands codes out.
    ///
    /// **The placeholder is never the box's text**, so nothing that reads the text can take it for a
    /// code: the copy button, which copies the text and is dead while there is none, selecting and
    /// copying by hand, and UI automation reading the value. A screen reader is given it as the
    /// box's description instead, and only while the box is empty.
    ///
    /// It starts where a code would, at the edit control's own formatting rectangle, which holds
    /// the margins Windows chose for the font and which WinForms does not expose.
    /// </remarks>
    internal sealed class CodeBox : TextBox
    {
        /// <summary>`WM_PAINT`, after which the empty box is drawn over.</summary>
        private const int WmPaint = 0x000F;

        /// <summary>
        /// One line, cut short rather than wrapped, in the box's own font and without the padding
        /// `TextRenderer` adds by default, which the edit control does not.
        /// </summary>
        private const TextFormatFlags Shape =
            TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.Top
            | TextFormatFlags.Left;

        private string _placeholder = string.Empty;

        /// <summary>What the box shows in gray while it holds nothing, which is never its text.</summary>
        public string Placeholder
        {
            get
            {
                return _placeholder;
            }

            set
            {
                _placeholder = value ?? string.Empty;
                Describe();
                Invalidate();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            WindowHandlers.Run("the code box could not be described", Describe);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmPaint && TextLength == 0 && _placeholder.Length > 0)
            {
                WindowHandlers.Run("the code box's placeholder could not be drawn", DrawPlaceholder);
            }
        }

        private void DrawPlaceholder()
        {
            var where = NativeMethods.GetEditRectangle(Handle);
            using (var graphics = CreateGraphics())
            {
                TextRenderer.DrawText(graphics, _placeholder, Font, where, SystemColors.GrayText, Shape);
            }
        }

        private void Describe()
        {
            AccessibleDescription = TextLength == 0 && _placeholder.Length > 0 ? _placeholder : null;
        }
    }
}
