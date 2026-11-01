using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MonoProbe
{
    /// <summary>
    /// The WinForms facts the tab's layout rests on, asked without showing anything: which family
    /// a font asked for by name turns into, and how much a dialog laid out against 6 by 13 is
    /// scaled, which is what decides whether the KPG pages fit their room. A form is made, given a
    /// window and never shown; on Mono that needs a display, and the scripts give it Xvfb.
    /// </summary>
    internal static class FormsProbe
    {
        internal static void Run()
        {
            Report.Measure("forms.font-consolas", () => new Font("Consolas", 9f).FontFamily.Name);
            Report.Measure("forms.font-microsoft-sans-serif", () => new Font("Microsoft Sans Serif", 8.25f).FontFamily.Name);
            Report.Measure("forms.font-generic-monospace", () => FontFamily.GenericMonospace.Name);
            Report.Measure("forms.default-font", () => Describe(Control.DefaultFont));
            Report.Measure("forms.message-box-font", () => Describe(SystemFonts.MessageBoxFont));

            Report.Measure("forms.auto-scale", () =>
            {
                using (var form = new Form())
                {
                    form.AutoScaleMode = AutoScaleMode.Font;
                    form.AutoScaleDimensions = new SizeF(6f, 13f);
                    var handle = form.Handle;
                    return "current " + form.CurrentAutoScaleDimensions + ", handle " + (handle != IntPtr.Zero);
                }
            });

            Report.Measure("forms.measure-text", () =>
                TextRenderer.MeasureText("Certificate Authority", Control.DefaultFont).ToString());
        }

        private static string Describe(Font font)
        {
            return font.Name + " " + font.SizeInPoints.ToString(CultureInfo.InvariantCulture);
        }
    }
}
