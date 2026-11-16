# The desktop-windows family's helpers, dot-sourced by every measurement script: starting KeePass
# and KeePassXC under a basic user token, finding and reading their windows through UI automation,
# pressing keys, taking a picture of a window, replacing a file as a synchronizer does, and
# stopping every program a run started. Windows PowerShell 5.1 only: UI automation is a .NET
# Framework library.

Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

if (-not ('DeskNative' -as [type])) {
    $refs = @('System.Drawing')
    if ($PSVersionTable.PSEdition -eq 'Core') { $refs = @('System.Security.Principal.Windows', 'System.Security.Claims', 'System.Runtime.InteropServices', 'Microsoft.Win32.Primitives') }
    Add-Type -ReferencedAssemblies $refs @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class DeskNative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool MoveFileEx(string from, string to, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    public static string FrameBounds(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) return null; return r.L + "," + r.T + "," + (r.R - r.L) + "," + (r.B - r.T); }
    public static long GetParentWindow(IntPtr h) { return GetParent(h).ToInt64(); }
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, System.Text.StringBuilder l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
    // Every child window below h as "hwnd<TAB>class<TAB>visible<TAB>enabled<TAB>left,top,right,bottom<TAB>text";
    // the text is read with WM_GETTEXT, which a password box answers with its mask characters.
    public static string[] Kids(IntPtr parent) {
        var l = new System.Collections.Generic.List<string>();
        EnumChildWindows(parent, (h, x) => {
            var c = new System.Text.StringBuilder(256); GetClassName(h, c, 256);
            var t = new System.Text.StringBuilder(4096); SendMessage(h, 0x000D, (IntPtr)4096, t);
            RECT r; GetWindowRect(h, out r);
            l.Add(h.ToInt64() + "\t" + c + "\t" + (IsWindowVisible(h) ? 1 : 0) + "\t" + (IsWindowEnabled(h) ? 1 : 0) + "\t" + r.L + "," + r.T + "," + r.R + "," + r.B + "\t" + t.ToString().Replace("\r", "\\r").Replace("\n", "\\n"));
            return true;
        }, IntPtr.Zero);
        return l.ToArray();
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    public static int Checked(IntPtr h) { return SendMessage(h, 0x00F0, IntPtr.Zero, IntPtr.Zero).ToInt32(); }
    public static string Text(IntPtr h) { var t = new System.Text.StringBuilder(65536); SendMessage(h, 0x000D, (IntPtr)65536, t); return t.ToString(); }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr t);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr t, int cls, IntPtr info, int len, out int ret);
    [DllImport("advapi32.dll")] public static extern IntPtr GetSidSubAuthority(IntPtr sid, uint n);
    [DllImport("advapi32.dll")] public static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    // Whether a process's token is elevated, its integrity level, and whether it may act as an
    // Administrator, read from the process itself.
    public static string TokenReport(int pid) {
        IntPtr p = OpenProcess(0x1000, false, pid);
        if (p == IntPtr.Zero) return "OpenProcess failed " + Marshal.GetLastWin32Error();
        IntPtr t;
        if (!OpenProcessToken(p, 0x000A, out t)) { CloseHandle(p); return "OpenProcessToken failed " + Marshal.GetLastWin32Error(); }
        string r = "";
        IntPtr buf = Marshal.AllocHGlobal(4096);
        int len;
        if (GetTokenInformation(t, 20, buf, 4, out len)) r += "TokenIsElevated=" + Marshal.ReadInt32(buf); else r += "TokenIsElevated=error" + Marshal.GetLastWin32Error();
        if (GetTokenInformation(t, 18, buf, 4, out len)) r += " ElevationType=" + Marshal.ReadInt32(buf);
        if (GetTokenInformation(t, 25, buf, 4096, out len)) {
            IntPtr sid = Marshal.ReadIntPtr(buf);
            int n = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            r += " Integrity=0x" + Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(n - 1))).ToString("x");
        }
        using (var id = new WindowsIdentity(t)) {
            r += " AdministratorsEnabled=" + new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        Marshal.FreeHGlobal(buf);
        CloseHandle(t); CloseHandle(p);
        return r;
    }
}
'@
}
[void][DeskNative]::SetProcessDPIAware()

# KeePass's group tree and entry list are WinForms tree and list views, and UI automation's managed
# client shows each as a bare pane with no items here (its client-side providers fail to load:
# ProxyManager.LoadDefaultProxies throws NullReferenceException, in Windows PowerShell 5.1 and in a
# managed host alike). The accessibility interface of the same windows lists every item, so the
# tree and the list are read and driven through it.
if (-not ('DeskMsaa' -as [type])) {
    Add-Type -AssemblyName Accessibility
    Add-Type -ReferencedAssemblies Accessibility @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;
public static class DeskMsaa {
    [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
    public static IAccessible FromWindow(IntPtr h) {
        Guid g = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        object o; int hr = AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref g, out o);
        if (hr != 0) throw new Exception("AccessibleObjectFromWindow hr " + hr);
        return (IAccessible)o;
    }
    // Each child as "id<TAB>role<TAB>state<TAB>name<TAB>value".
    public static string[] Items(IntPtr h) {
        var a = FromWindow(h);
        var l = new List<string>();
        int n = a.accChildCount;
        for (int i = 1; i <= n; i++) {
            try {
                string d = ""; try { d = a.get_accDescription(i); } catch (Exception) { }
                l.Add(i + "\t" + a.get_accRole(i) + "\t" + a.get_accState(i) + "\t" + a.get_accName(i) + "\t" + a.get_accValue(i) + "\t" + (d ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "));
            } catch (Exception) { }
        }
        return l.ToArray();
    }
    public static void Select(IntPtr h, int id) { FromWindow(h).accSelect(0x1 | 0x2, id); }
    // A control's own role and state, read from its window (OBJID_CLIENT, CHILDID_SELF).
    public static string Self(IntPtr h) { var a = FromWindow(h); return a.get_accRole(0) + "\t" + a.get_accState(0); }
    public static void Default(IntPtr h, int id) { FromWindow(h).accDoDefaultAction(id); }
    public static string Location(IntPtr h, int id) {
        int x, y, w, ht; FromWindow(h).accLocation(out x, out y, out w, out ht, id);
        return x + "," + y + "," + w + "," + ht;
    }
}
'@
}

function Get-ChildByClass($Root, [string] $ClassPart) {
    foreach ($e in (Find-All $Root $null $CT::Pane $null)) {
        try { if ($e.Current.ClassName -like "*$ClassPart*") { return $e } } catch { }
    }
    $null
}

# The child windows of a window, read with Win32 calls: KeePass's dialogs are WinForms, which UI
# automation reports here as panes with no patterns, so their buttons and boxes are found by class
# and text and pressed with the messages a click sends.
function Get-Kids([int64] $Hwnd, [switch] $Hidden) {
    @([DeskNative]::Kids([IntPtr]$Hwnd) | ForEach-Object {
        $f = $_.Split("`t", 6)
        $r = $f[4].Split(',')
        [pscustomobject]@{ H = [int64]$f[0]; Class = $f[1]; Visible = $f[2] -eq '1'; Enabled = $f[3] -eq '1'
            L = [int]$r[0]; T = [int]$r[1]; R = [int]$r[2]; B = [int]$r[3]; Text = $f[5] }
    } | Where-Object { $Hidden -or $_.Visible })
}

function Find-Kid([int64] $Hwnd, [string] $Text, [string] $ClassPart, [switch] $Prefix, [double] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        foreach ($k in (Get-Kids $Hwnd)) {
            if ($ClassPart -and $k.Class -notlike "*$ClassPart*") { continue }
            $t = $k.Text.Replace('&', '')
            if (-not $Text -or ($Prefix -and $t.StartsWith($Text)) -or ($t -eq $Text)) { return $k }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $null
}

# The box to the right of a label, on the label's line.
function Find-BoxBy([int64] $Hwnd, [string] $Label) {
    $kids = Get-Kids $Hwnd
    $l = $kids | Where-Object { ($_.Class -like '*STATIC*' -or $_.Class -like '*BUTTON*') -and $_.Text.Replace('&', '') -eq $Label } | Select-Object -First 1
    if (-not $l) { return $null }
    $mid = ($l.T + $l.B) / 2
    $kids | Where-Object { ($_.Class -like '*EDIT*' -or $_.Class -like '*RICHEDIT*') -and $_.L -ge $l.R - 4 -and $_.T -le $mid + 6 -and $_.B -ge $mid - 6 } |
        Sort-Object L | Select-Object -First 1
}

function Click-Hwnd([int64] $H) {
    [void][DeskNative]::PostMessage([IntPtr]$H, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

function Click-Kid([int64] $Hwnd, [string] $Text, [switch] $Prefix) {
    $k = Find-Kid $Hwnd $Text 'BUTTON' -Prefix:$Prefix
    if (-not $k) { throw "no button '$Text' in window $Hwnd" }
    Click-Hwnd $k.H
    $k
}

# Focus by a click in the control, which is what reliably moves the focus between another
# process's controls (UI automation's SetFocus left it on the dialog's first box here).
function Focus-Hwnd([int64] $H) {
    $r = Get-Kids ([DeskNative]::GetParentWindow([IntPtr]$H)) -Hidden | Where-Object { $_.H -eq $H } | Select-Object -First 1
    if (-not $r) { try { $AE::FromHandle([IntPtr]$H).SetFocus() } catch { }; return }
    Click-At ([int](($r.L + $r.R) / 2)) ([int](($r.T + [Math]::Min($r.B, $r.T + 20)) / 2))
    Start-Sleep -Milliseconds 200
}
function Type-Hwnd([int64] $H, [string] $Text) {
    Focus-Hwnd $H
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 100
    $escaped = ($Text.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join ''
    [System.Windows.Forms.SendKeys]::SendWait($escaped)
    Start-Sleep -Milliseconds 150
}

function Get-MsaaItems([int64] $Hwnd) {
    @([DeskMsaa]::Items([IntPtr]$Hwnd) | ForEach-Object {
        $f = $_.Split("`t")
        [pscustomobject]@{ Id = [int]$f[0]; Role = $f[1]; State = $f[2]; Name = $f[3]; Value = $f[4]; Description = $(if ($f.Count -gt 5) { $f[5] } else { '' }) }
    })
}


$script:AE = [System.Windows.Automation.AutomationElement]
$script:CT = [System.Windows.Automation.ControlType]
$script:TS = [System.Windows.Automation.TreeScope]

$script:UnelevatedScript = $null
$script:LogFile = $null
$script:ShotDir = $null
$script:WorkRoot = $null

function Say([string] $line) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host "$stamp $line"
    if ($script:LogFile) { Add-Content -LiteralPath $script:LogFile -Value "$stamp $line" -Encoding UTF8 }
}

# ------------------------------------------------------------------ programs

# Starts a program under a basic user token through the session's `unelevated.ps1`, which waits
# for the command it runs; the command is `Start-Process`, which returns at once. Answers the
# process, found by its path and a start time after the call.
function Start-Unelevated([string] $Exe, [string[]] $Arguments, [string] $StdinFile, [string] $Tag) {
    $before = [DateTime]::Now.AddSeconds(-1)
    $quoted = @($Arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ','
    $cmd = "Start-Process -FilePath '$Exe' -WorkingDirectory '$(Split-Path -Parent $Exe)'"
    if ($Arguments -and $Arguments.Count -gt 0) { $cmd += " -ArgumentList $quoted" }
    if ($StdinFile) { $cmd += " -RedirectStandardInput '$StdinFile'" }
    $log = Join-Path $script:WorkRoot "logs\start-$Tag.log"
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
    if ($admin) {
        & $script:UnelevatedScript -Dir (Split-Path -Parent $Exe) -Log $log -Command $cmd | Out-Null
    } else {
        # The driver itself already runs under the basic user token that script gives, so the
        # program is its child and inherits it.
        Invoke-Expression $cmd
    }
    $name = [IO.Path]::GetFileNameWithoutExtension($Exe)
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $p = Get-Process -Name $name -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and ($_.Path -eq $Exe) -and $_.StartTime -ge $before } |
            Sort-Object StartTime | Select-Object -Last 1
        if ($p) {
            Say "started $name pid=$($p.Id) token: $([DeskNative]::TokenReport($p.Id))"
            return $p
        }
        Start-Sleep -Milliseconds 300
    }
    throw "no $name process appeared"
}

function Stop-Started {
    foreach ($n in 'KeePass', 'KeePassXC', 'DeskTarget') {
        Get-Process -Name $n -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($script:WorkRoot, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { Say "stopping $n pid=$($_.Id)"; Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; $_.WaitForExit(10000) | Out-Null }
    }
    Get-Process -Name powershell -ErrorAction SilentlyContinue | Where-Object {
        try { $_.MainWindowTitle -eq 'DeskTarget' } catch { $false } } | Stop-Process -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------ finding

function New-Cond([string] $Name, $ControlType, [string] $AutomationId, [int] $ProcessId) {
    $c = @()
    if ($Name) { $c += New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Name) }
    if ($ControlType) { $c += New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ControlType) }
    if ($AutomationId) { $c += New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $AutomationId) }
    if ($ProcessId) { $c += New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId) }
    if ($c.Count -eq 0) { return [System.Windows.Automation.Condition]::TrueCondition }
    if ($c.Count -eq 1) { return $c[0] }
    return New-Object System.Windows.Automation.AndCondition($c)
}

function Find-All($Root, [string] $Name, $ControlType, [string] $AutomationId) {
    try { @($Root.FindAll($TS::Descendants, (New-Cond $Name $ControlType $AutomationId 0))) } catch { @() }
}

function Find-El($Root, [string] $Name, $ControlType, [string] $AutomationId, [string] $Prefix, [double] $Seconds = 8) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $all = Find-All $Root $Name $ControlType $AutomationId
        foreach ($e in $all) {
            try {
                if (-not $Prefix -or $e.Current.Name.StartsWith($Prefix)) { return $e }
            } catch { }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Need($Element, [string] $What) {
    if (-not $Element) { throw "not found: $What" }
    $Element
}

# Every window of a process: its top-level windows, and the windows UI automation puts below them
# (a WinForms dialog owned by the main window appears under it).
function Get-ProcWindows([int] $ProcessId) {
    $out = New-Object System.Collections.Generic.List[object]
    try {
        foreach ($w in $AE::RootElement.FindAll($TS::Children, (New-Cond $null $null $null $ProcessId))) {
            $out.Add($w)
            try { foreach ($d in $w.FindAll($TS::Descendants, (New-Cond $null $CT::Window $null 0))) { $out.Add($d) } } catch { }
        }
    } catch { }
    $out
}

function Get-Handle($w) { try { [int64]$w.Current.NativeWindowHandle } catch { 0 } }

function Get-WindowNames([int] $ProcessId) {
    @(Get-ProcWindows $ProcessId | ForEach-Object { try { $_.Current.Name } catch { '?' } })
}

function Wait-Window([int] $ProcessId, [string] $Like, [double] $Seconds = 20, [int64[]] $Except = @()) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        foreach ($w in (Get-ProcWindows $ProcessId)) {
            try {
                $h = Get-Handle $w
                if ($Except -contains $h) { continue }
                if ($w.Current.Name -like $Like) { return $w }
            } catch { }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

# Waits for a window of the process that is none of the handles given, for at most $Seconds.
function Wait-NewWindow([int] $ProcessId, [int64[]] $Known, [double] $Seconds = 6) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        foreach ($w in (Get-ProcWindows $ProcessId)) {
            $h = Get-Handle $w
            if ($h -ne 0 -and -not ($Known -contains $h)) {
                try {
                    if ($w.Current.IsOffscreen -and $w.Current.BoundingRectangle.Width -eq 0) { continue }
                    # A menu's shadow and a tooltip are windows too, and none is a dialog.
                    if ($w.Current.ClassName -in 'SysShadow', 'tooltips_class32') { continue }
                } catch { continue }
                return $w
            }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-Handles([int] $ProcessId) { @(Get-ProcWindows $ProcessId | ForEach-Object { Get-Handle $_ }) }

# ------------------------------------------------------------------ reading

function Get-Text($e) {
    $p = $null
    try {
        if ($e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $p)) { return $p.Current.Value }
        if ($e.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref] $p)) { return $p.DocumentRange.GetText(-1) }
    } catch { }
    ''
}

# Logs a window's title, every text and every button it shows, and answers them.
function Read-Window($w, [string] $Label) {
    $title = try { $w.Current.Name } catch { '?' }
    $class = try { $w.Current.ClassName } catch { '?' }
    Say "[$Label] WINDOW title=<<$title>> class=$class"
    $texts = New-Object System.Collections.Generic.List[string]
    $buttons = New-Object System.Collections.Generic.List[string]
    foreach ($e in (Find-All $w $null $null $null)) {
        try {
            $type = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            $name = $e.Current.Name
            $text = Get-Text $e
            $help = $e.Current.HelpText
            if ($type -eq 'Button' -or $type -eq 'SplitButton') { $buttons.Add($name) }
            if ($name -or $text) {
                $line = "$type name=<<$name>>"
                if ($text -and $text -ne $name) { $line += " text=<<$text>>" }
                if ($help) { $line += " help=<<$help>>" }
                if (-not $e.Current.IsEnabled) { $line += ' disabled' }
                Say "[$Label]   $line"
                if ($type -ne 'Button') { $texts.Add(($name + ' ' + $text).Trim()) }
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    # A WinForms or Win32 dialog's own child windows, whose captions UI automation may not carry.
    foreach ($k in (Get-Kids (Get-Handle $w))) {
        if (-not $k.Text) { continue }
        Say "[$Label]   win32 $($k.Class -replace '^WindowsForms10\.', '' -replace '\.app\..*$', '') <<$($k.Text)>>$(if (-not $k.Enabled) { ' disabled' })"
        if ($k.Class -like '*BUTTON*') { $buttons.Add($k.Text) } else { $texts.Add($k.Text) }
    }
    [pscustomobject]@{ Title = $title; Texts = $texts; Buttons = $buttons }
}

# ------------------------------------------------------------------ acting

function Show-Front($w) {
    $h = [IntPtr](Get-Handle $w)
    if ($h -eq [IntPtr]::Zero) { return }
    if ([DeskNative]::IsIconic($h)) { [void][DeskNative]::ShowWindow($h, 9) }
    for ($i = 0; $i -lt 5; $i++) {
        [DeskNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [DeskNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [void][DeskNative]::SetForegroundWindow($h)
        [void][DeskNative]::BringWindowToTop($h)
        Start-Sleep -Milliseconds 150
        if ([DeskNative]::GetForegroundWindow() -eq $h) { return }
    }
    try { $w.SetFocus() } catch { }
}

function Press($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $p)) { $p.Invoke(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $p)) { $p.Select(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref] $p)) { $p.Toggle(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $p)) { $p.Expand(); return }
    throw "nothing to press on '$($Element.Current.Name)'"
}

# Presses a button without waiting for what it starts: a modal dialog raised by the press would
# otherwise hold InvokePattern.Invoke until it closes.
function Press-Async($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $p)) {
        $ps = [PowerShell]::Create()
        [void]$ps.AddScript({ param($pat) $pat.Invoke() }).AddArgument($p)
        $script:AsyncPress = @{ PS = $ps; Handle = $ps.BeginInvoke() }
        Start-Sleep -Milliseconds 300
        return
    }
    Press $Element
}

function Select-Item($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $p)) { $p.Select() }
    else { $Element.SetFocus() }
}

function Set-Value($Element, [string] $Value) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $p)) { $p.SetValue($Value); return }
    throw "no value pattern on '$($Element.Current.Name)'"
}

function Get-Toggle($Element) {
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref] $p)) { return [string]$p.Current.ToggleState }
    'none'
}

# Keys to whichever window is in front, after bringing $w there.
function Send-To($w, [string] $Keys, [int] $After = 400) {
    if ($w) { Show-Front $w }
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds $After
}

function Type-Into($Element, [string] $Text) {
    $Element.SetFocus()
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 100
    $escaped = ($Text.ToCharArray() | ForEach-Object { if ('+^%~(){}[]'.Contains([string]$_)) { '{' + $_ + '}' } else { [string]$_ } }) -join ''
    [System.Windows.Forms.SendKeys]::SendWait($escaped)
    Start-Sleep -Milliseconds 150
}

function Click-At([int] $X, [int] $Y, [switch] $Double) {
    [void][DeskNative]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 80
    [DeskNative]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); [DeskNative]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
    if ($Double) { Start-Sleep -Milliseconds 60; [DeskNative]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); [DeskNative]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero) }
}

function Center($e) {
    $r = $e.Current.BoundingRectangle
    @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}

# ------------------------------------------------------------------ pictures

# A picture of one window, cut from the screen at its bounds after bringing it to the front, so
# that nothing but that window is in it.
function Save-Shot($w, [string] $Name) {
    if (-not $script:ShotDir) { return }
    try {
        Show-Front $w
        Start-Sleep -Milliseconds 350
        # The frame DWM draws, without the invisible resize borders that would show the desktop.
        $fb = [DeskNative]::FrameBounds([IntPtr](Get-Handle $w))
        if ($fb) { $f = $fb.Split(','); $rx = [int]$f[0]; $ry = [int]$f[1]; $rw = [int]$f[2]; $rh = [int]$f[3] }
        else { $r = $w.Current.BoundingRectangle; $rx = [int]$r.X; $ry = [int]$r.Y; $rw = [int]$r.Width; $rh = [int]$r.Height }
        $x = [Math]::Max(0, $rx); $y = [Math]::Max(0, $ry)
        $wd = [Math]::Min($rw, 1280 - $x); $ht = [Math]::Min($rh, 800 - $y)
        if ($wd -le 0 -or $ht -le 0) { Say "shot $Name skipped: no bounds"; return }
        $bmp = New-Object System.Drawing.Bitmap $wd, $ht
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
        $g.Dispose()
        $file = Join-Path $script:ShotDir "$Name.png"
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Say "shot $Name.png ($wd x $ht)"
    } catch { Say "shot $Name failed: $($_.Exception.Message)" }
}

# ------------------------------------------------------------------ files

function Get-FileState([string] $Path) {
    $i = Get-Item -LiteralPath $Path
    $h = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.Substring(0, 12).ToLowerInvariant()
    "size=$($i.Length) mtime=$($i.LastWriteTimeUtc.ToString('o')) sha=$h"
}

# Replaces $Target by $Source as a file synchronizer does: the new bytes written beside the
# target under another name, renamed over it, and the modification time set to the case named.
function Replace-File([string] $Target, [string] $Source, [string] $Time = 'later') {
    $before = Get-Item -LiteralPath $Target
    $oldTime = $before.LastWriteTimeUtc
    Say "replace: before $(Get-FileState $Target)"
    $tmp = Join-Path (Split-Path -Parent $Target) ('.syncthing.' + (Split-Path -Leaf $Target) + '.tmp')
    Copy-Item -LiteralPath $Source -Destination $tmp -Force
    switch ($Time) {
        'later' { (Get-Item -LiteralPath $tmp).LastWriteTimeUtc = [DateTime]::UtcNow }
        'equal' { (Get-Item -LiteralPath $tmp).LastWriteTimeUtc = $oldTime }
        'earlier' { (Get-Item -LiteralPath $tmp).LastWriteTimeUtc = $oldTime.AddHours(-1) }
    }
    # Retried a few times, as a synchronizer retries: a program reading the file holds it open
    # for a moment without sharing its deletion.
    $moved = $false
    for ($try = 1; $try -le 20 -and -not $moved; $try++) {
        $moved = [DeskNative]::MoveFileEx($tmp, $Target, 0x1 -bor 0x8)
        if (-not $moved) { $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error(); Say "replace: MoveFileEx failed $err, try $try"; Start-Sleep -Milliseconds 250 }
    }
    if (-not $moved) { throw "MoveFileEx failed $err" }
    Say "replace ($Time) with $(Split-Path -Leaf $Source): after $(Get-FileState $Target)"
}
