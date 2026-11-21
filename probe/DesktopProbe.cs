using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

// What a process on a private desktop can and cannot do: where it runs, whether a form gets a
// window and paints, whether it can be activated and focused, and what the calls that reach for
// the input desktop (pointer, synthesized input, screen capture) answer there.
public static class Probe
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformationW(IntPtr obj, int index, StringBuilder info, int length, out int needed);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
    static uint LastInput() { var i = new LASTINPUTINFO(); i.cbSize = 8; GetLastInputInfo(ref i); return i.dwTime; }

    static string Name(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return "(null)";
        var s = new StringBuilder(256); int n;
        return GetUserObjectInformationW(obj, 2, s, 512, out n) ? s.ToString() : "?err" + Marshal.GetLastWin32Error();
    }

    static int Distinct(Bitmap b)
    {
        var seen = new HashSet<int>();
        for (int y = 0; y < b.Height; y += 2) for (int x = 0; x < b.Width; x += 2) seen.Add(b.GetPixel(x, y).ToArgb());
        return seen.Count;
    }

    public static void Run(Action<string> note)
    {
        note("thread desktop: " + Name(GetThreadDesktop(GetCurrentThreadId())));
        note("window station: " + Name(GetProcessWindowStation()));
        var input = OpenInputDesktop(0, false, 0x0001);
        note("input desktop: " + (input == IntPtr.Zero ? "OpenInputDesktop failed " + Marshal.GetLastWin32Error() : Name(input)));
        if (input != IntPtr.Zero) CloseDesktop(input);
        note("clipboard sequence: " + GetClipboardSequenceNumber());
        POINT before; bool got = GetCursorPos(out before);
        note("GetCursorPos: " + got + " (" + before.X + "," + before.Y + ") error " + Marshal.GetLastWin32Error());
        note("foreground before the form: " + GetForegroundWindow());
        note("screen: " + Screen.PrimaryScreen.Bounds + " at dpi of a new graphics");

        var form = new Form { Text = "private desktop probe", StartPosition = FormStartPosition.Manual, Location = new Point(100, 100), Size = new Size(400, 300) };
        form.BackColor = Color.White;
        var panel = new Panel { BackColor = Color.SteelBlue, Location = new Point(20, 20), Size = new Size(150, 80) };
        var label = new Label { Text = "painted text", Location = new Point(20, 120), AutoSize = true, Font = new Font("Segoe UI", 14f) };
        var box = new TextBox { Location = new Point(20, 170), Width = 200 };
        form.Controls.Add(panel); form.Controls.Add(label); form.Controls.Add(box);
        var shownAt = DateTime.Now;
        form.Show();
        for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(25); }
        note("form handle: " + form.Handle + " created " + form.IsHandleCreated + " visible(win32) " + IsWindowVisible(form.Handle) + " shown in " + (DateTime.Now - shownAt).TotalMilliseconds + " ms; size " + form.Size + " state " + form.WindowState);
        if (form.WindowState != FormWindowState.Normal) { form.WindowState = FormWindowState.Normal; Application.DoEvents(); note("restored: size " + form.Size); }

        using (var b = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
            note("DrawToBitmap distinct colors (every second pixel): " + Distinct(b) + "; pixel in panel " + b.GetPixel(Math.Min(60, b.Width - 1), Math.Min(60, b.Height - 1)) + "");
        }
        using (var b = new Bitmap(form.Width, form.Height))
        {
            bool ok;
            using (var g = Graphics.FromImage(b)) { var hdc = g.GetHdc(); ok = PrintWindow(form.Handle, hdc, 2); g.ReleaseHdc(hdc); }
            note("PrintWindow(PW_RENDERFULLCONTENT) " + ok + " distinct colors: " + Distinct(b));
            b.Save(System.IO.Path.Combine(Environment.GetEnvironmentVariable("PROBE_OUT"), "printwindow.png"), ImageFormat.Png);
        }
        try
        {
            using (var b = new Bitmap(form.Width, form.Height))
            {
                using (var g = Graphics.FromImage(b)) g.CopyFromScreen(form.Location, Point.Empty, b.Size);
                note("CopyFromScreen distinct colors: " + Distinct(b) + "; pixel in panel " + b.GetPixel(Math.Min(60, b.Width - 1), Math.Min(80, b.Height - 1)));
                b.Save(System.IO.Path.Combine(Environment.GetEnvironmentVariable("PROBE_OUT"), "copyfromscreen.png"), ImageFormat.Png);
            }
        }
        catch (Exception e) { note("CopyFromScreen threw " + e.GetType().Name + ": " + e.Message); }

        form.Activate();
        Application.DoEvents();
        note("after Activate: foreground " + GetForegroundWindow() + " active " + GetActiveWindow() + " focus " + GetFocus() + " form " + form.Handle + " ContainsFocus " + form.ContainsFocus + " ActiveForm " + (Form.ActiveForm != null));
        note("SetForegroundWindow(form): " + SetForegroundWindow(form.Handle) + " error " + Marshal.GetLastWin32Error() + "; foreground now " + GetForegroundWindow());
        box.Focus(); Application.DoEvents();
        note("box.Focus: box focused " + box.Focused + " GetFocus==box " + (GetFocus() == box.Handle));

        bool set = SetCursorPos(before.X - 37, before.Y - 23);
        int setError = Marshal.GetLastWin32Error();
        POINT after; GetCursorPos(out after);
        note("SetCursorPos(-37,-23): " + set + " error " + setError + "; GetCursorPos now (" + after.X + "," + after.Y + ")");
        if (after.X != before.X || after.Y != before.Y) { SetCursorPos(before.X, before.Y); note("put the pointer back"); }

        uint lastBefore = LastInput();
        Thread.Sleep(50);
        var move = new INPUT[] { new INPUT { type = 0, mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = 0x0001 } } };
        uint sent = SendInput(1, move, Marshal.SizeOf(typeof(INPUT)));
        int sendError = Marshal.GetLastWin32Error();
        Thread.Sleep(100);
        uint lastAfterSend = LastInput();
        note("SendInput(zero mouse move): " + sent + " error " + sendError + "; last input tick " + lastBefore + " -> " + lastAfterSend);
        Thread.Sleep(50);
        keybd_event(0x97, 0, 0, UIntPtr.Zero);
        keybd_event(0x97, 0, 2, UIntPtr.Zero);
        Thread.Sleep(100);
        note("keybd_event(unassigned key 0x97): last input tick " + lastAfterSend + " -> " + LastInput());
        mouse_event(0x0001, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(100);
        POINT afterMouse; GetCursorPos(out afterMouse);
        note("mouse_event(zero move): last input tick now " + LastInput() + " pointer (" + afterMouse.X + "," + afterMouse.Y + ")");

        try
        {
            var root = AutomationElement.RootElement;
            note("UIA root: " + root.Current.Name + " class " + root.Current.ClassName + " handle " + root.Current.NativeWindowHandle);
            var element = AutomationElement.FromHandle(form.Handle);
            note("UIA FromHandle(form): name '" + element.Current.Name + "' bounds " + element.Current.BoundingRectangle);
            var found = root.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, "private desktop probe"));
            note("UIA root child named like the form: " + (found != null));
        }
        catch (Exception e) { note("UIA threw " + e.GetType().Name + ": " + e.Message); }

        var tip = new ToolTip();
        tip.Show("tooltip text", form, 30, 30, 2000);
        for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(30); }
        note("tooltip shown (no exception)");

        form.Close();
        form.Dispose();
        note("clipboard sequence at the end: " + GetClipboardSequenceNumber());
    }
}
