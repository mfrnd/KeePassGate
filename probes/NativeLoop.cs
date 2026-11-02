// Can another thread tell, cheaply and without touching it, that a window's thread is inside a
// native dialog's loop? A message box, a task dialog and a common file dialog are all of class
// #32770; a WinForms form, KeePass's dialogs among them, is not.
//
//   NativeLoop.exe
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class NativeLoop
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder b, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    static bool InNativeDialog(uint thread)
    {
        var found = false;
        var name = new StringBuilder(16);
        EnumThreadWindows(thread, delegate (IntPtr hwnd, IntPtr l)
        {
            name.Length = 0;
            if (IsWindowVisible(hwnd) && GetClassName(hwnd, name, name.Capacity) > 0 && name.ToString() == "#32770")
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static void Report(string when, uint thread)
    {
        var clock = Stopwatch.StartNew();
        bool answer = false;
        for (int i = 0; i < 1000; i++) answer = InNativeDialog(thread);
        Console.WriteLine("{0,-40} {1,-5}  ({2:F1} us a check)", when, answer, clock.Elapsed.TotalMilliseconds);
    }

    [STAThread]
    static void Main()
    {
        var form = new Form { Text = "NativeLoop" };
        form.Shown += delegate
        {
            uint thread = GetWindowThreadProcessId(form.Handle, IntPtr.Zero);
            new Thread(delegate ()
            {
                Thread.Sleep(300);
                Report("a form, nothing else", thread);

                form.BeginInvoke((MethodInvoker)delegate { MessageBox.Show(form, "probe", "NativeLoop box"); });
                Thread.Sleep(500);
                Report("a MessageBox over it", thread);
                PostMessage(FindWindow("#32770", "NativeLoop box"), 0x0010, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(300);
                Report("the box closed", thread);

                Form dialog = null;
                form.BeginInvoke((MethodInvoker)delegate { dialog = new Form { Text = "NativeLoop dialog" }; dialog.ShowDialog(form); });
                Thread.Sleep(500);
                Report("a WinForms ShowDialog over it", thread);
                dialog.BeginInvoke((MethodInvoker)dialog.Close);
                Thread.Sleep(300);

                form.BeginInvoke((MethodInvoker)delegate { using (var open = new OpenFileDialog()) open.ShowDialog(form); });
                Thread.Sleep(1500);
                Report("an OpenFileDialog over it", thread);
                PostMessage(FindWindow("#32770", null), 0x0010, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(500);
                Report("the file dialog closed", thread);

                form.BeginInvoke((MethodInvoker)form.Close);
            }) { IsBackground = true }.Start();
        };
        Application.Run(form);
    }
}
