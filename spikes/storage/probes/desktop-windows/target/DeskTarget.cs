// An auto-type target for measurement 3: a window with the title given and a text box, which writes
// whatever is typed into it to a file twice a second.
//
//   DeskTarget.exe <title> <outfile>
//   DeskTarget.exe @<file holding the title> <outfile>
//
// The second form is for a title with spaces, which does not survive being passed through a
// command line handed from one PowerShell to another.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class DeskTarget
{
    [STAThread]
    static void Main(string[] a)
    {
        string title = a[0].StartsWith("@") ? File.ReadAllText(a[0].Substring(1)).Trim() : a[0];
        var f = new Form { Text = title, Width = 520, Height = 240, StartPosition = FormStartPosition.Manual, Location = new Point(700, 40), TopMost = false };
        var t = new TextBox { Multiline = true, Dock = DockStyle.Fill, AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 10f) };
        f.Controls.Add(t);
        var timer = new Timer { Interval = 500 };
        timer.Tick += (s, e) => { try { File.WriteAllText(a[1], t.Text); } catch (IOException) { } };
        timer.Start();
        f.Shown += (s, e) => t.Focus();
        Application.Run(f);
    }
}
