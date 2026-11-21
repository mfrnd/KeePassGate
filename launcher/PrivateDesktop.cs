using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Creates a desktop of its own in the caller's window station, starts a command line on it, and
// waits for it, sampling which processes own top-level windows on that desktop and on the
// caller's own desktop while it runs. Loaded by `private-desktop.ps1` through `Add-Type` under
// Windows PowerShell 5.1.
public static class PrivateDesktop
{
    const uint GENERIC_ALL = 0x10000000;
    const uint CREATE_NEW_CONSOLE = 0x00000010;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint WAIT_TIMEOUT = 0x102;
    const int UOI_NAME = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr devmode, uint flags, uint access, IntPtr sa);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformationW(IntPtr obj, int index, StringBuilder info, int length, out int needed);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumDesktopWindows(IntPtr desk, EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);

    public static string NameOf(IntPtr desktop)
    {
        var text = new StringBuilder(256);
        int needed;
        return GetUserObjectInformationW(desktop, UOI_NAME, text, text.Capacity * 2, out needed) ? text.ToString() : "?";
    }

    public static string OwnDesktopName() { return NameOf(GetThreadDesktop(GetCurrentThreadId())); }

    static Dictionary<uint, string> names = new Dictionary<uint, string>();

    static string ProcessName(uint pid)
    {
        string name;
        if (names.TryGetValue(pid, out name)) return name;
        try { name = Process.GetProcessById((int) pid).ProcessName; } catch (ArgumentException) { name = "gone"; } catch (InvalidOperationException) { name = "gone"; }
        names[pid] = name;
        return name;
    }

    // Visible top-level windows by owning process name, on the desktop given or (null) the caller's.
    static SortedDictionary<string, int> Owners(IntPtr desktop, HashSet<uint> only, bool visibleOnly)
    {
        var counts = new SortedDictionary<string, int>();
        EnumWindowsProc cb = (hwnd, l) =>
        {
            if (visibleOnly && !IsWindowVisible(hwnd)) return true;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (only != null && !only.Contains(pid)) return true;
            var key = ProcessName(pid);
            int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
            return true;
        };
        if (desktop == IntPtr.Zero) EnumWindows(cb, IntPtr.Zero); else EnumDesktopWindows(desktop, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return counts;
    }

    static string Show(SortedDictionary<string, int> counts)
    {
        var parts = new List<string>();
        foreach (var pair in counts) parts.Add(pair.Key + ":" + pair.Value);
        return parts.Count == 0 ? "-" : string.Join(" ", parts.ToArray());
    }

    // Every process whose parent chain reaches root, read from a snapshot of (pid, parent) pairs.
    public static HashSet<uint> Tree(uint root, uint[] pids, uint[] parents)
    {
        var tree = new HashSet<uint> { root };
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int i = 0; i < pids.Length; i++)
                if (!tree.Contains(pids[i]) && tree.Contains(parents[i]) && pids[i] != parents[i]) { tree.Add(pids[i]); grew = true; }
        }
        return tree;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public int dwSize; public uint cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);

    public static HashSet<uint> TreeOfPid(uint root)
    {
        var pids = new List<uint>(); var parents = new List<uint>();
        var snap = CreateToolhelp32Snapshot(2, 0);
        try
        {
            var e = new PROCESSENTRY32(); e.dwSize = Marshal.SizeOf(typeof(PROCESSENTRY32));
            if (Process32FirstW(snap, ref e))
                do { pids.Add(e.th32ProcessID); parents.Add(e.th32ParentProcessID); } while (Process32NextW(snap, ref e));
        }
        finally { CloseHandle(snap); }
        return Tree(root, pids.ToArray(), parents.ToArray());
    }

    public static Func<uint, HashSet<uint>> TreeOf = TreeOfPid;

    public static int Run(string name, string commandLine, string directory, Action<string> log, int sampleMs)
    {
        var desktop = CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktop");
        log("created desktop " + NameOf(desktop) + "; launcher is on " + OwnDesktopName());
        try
        {
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            si.lpDesktop = "WinSta0\\" + name;
            // No `STARTF_USESHOWWINDOW`: Windows applies the show command it carries to the first
            // window the child shows, whatever the child asks for, and with `SW_SHOWMINNOACTIVE`
            // there the desktop probe's form came up minimized.
            PROCESS_INFORMATION pi;
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_NEW_CONSOLE | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, directory, ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess");
            log("started pid " + pi.dwProcessId + " on " + si.lpDesktop);
            CloseHandle(pi.hThread);
            var maxPrivate = new SortedDictionary<string, int>();
            var maxDefault = new SortedDictionary<string, int>();
            int samples = 0;
            while (WaitForSingleObject(pi.hProcess, (uint) sampleMs) == WAIT_TIMEOUT)
            {
                samples++;
                var onPrivate = Owners(desktop, null, false);
                HashSet<uint> tree = TreeOf != null ? TreeOf((uint) pi.dwProcessId) : null;
                var onDefault = tree != null ? Owners(IntPtr.Zero, tree, true) : new SortedDictionary<string, int>();
                foreach (var p in onPrivate) { int m; maxPrivate.TryGetValue(p.Key, out m); if (p.Value > m) maxPrivate[p.Key] = p.Value; }
                foreach (var p in onDefault) { int m; maxDefault.TryGetValue(p.Key, out m); if (p.Value > m) maxDefault[p.Key] = p.Value; }
                log("sample " + samples + " private[" + Show(onPrivate) + "] default-visible-of-tree[" + Show(onDefault) + "]");
            }
            uint code;
            GetExitCodeProcess(pi.hProcess, out code);
            CloseHandle(pi.hProcess);
            log("max windows on private desktop by process: " + Show(maxPrivate));
            log("max visible windows of the child's tree on the launcher's desktop: " + Show(maxDefault));
            log("child exit " + code);
            return (int) code;
        }
        finally
        {
            log("CloseDesktop " + CloseDesktop(desktop));
        }
    }
}
