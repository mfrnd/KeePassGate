<#
    Asks a machine whether a Windows GUI can be driven on it, one question at a time.

    This exists because the end to end tier of the plugin drives a real KeePass across the desktop,
    and whether a hosted runner can do that has been argued rather than measured. Nothing here knows
    about the plugin: the subject is the machine, so the target is a window this script puts up in
    another process, and the only things used are Windows PowerShell, WinForms and UI Automation,
    all of which are in the box.

    **Another process on purpose.** Driving a window inside this process would answer an easier
    question than the one being asked. Everything below crosses a process boundary exactly where the
    real driver does: the window is found through the accessibility tree, brought to the front
    through the input queue, typed at with synthesized keystrokes, clicked with a synthesized
    pointer, and read back through the tree again.

    **Every answer is printed as `name=value`** so a log can be read without prose, and the exit
    code is the verdict: nonzero when a row that has to work did not.
#>

$ErrorActionPreference = 'Continue'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Compiled by Add-Type, which on Windows PowerShell is the in-box CodeDOM provider and caps the
# language at C# 5. Nothing here needs more than that.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Probe
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll")]
    public static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetUserObjectInformation(
        IntPtr handle, int index, StringBuilder info, int length, out int needed);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte key, byte scanCode, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);

    /// <summary>
    /// The name of a window station or desktop, which is what says whether this is Session 0.
    /// </summary>
    public static string NameOf(IntPtr handle)
    {
        int needed;
        StringBuilder text = new StringBuilder(256);
        if (GetUserObjectInformation(handle, 2, text, text.Capacity, out needed))
        {
            return text.ToString();
        }

        return "(unavailable, error " + Marshal.GetLastWin32Error() + ")";
    }

    /// <summary>
    /// Windows refuses the foreground to a process that does not already hold it, which a script
    /// does not. Attaching to the current foreground thread's input queue for the duration lifts
    /// that refusal. The real driver does exactly this, for exactly this reason.
    /// </summary>
    public static bool Front(IntPtr window)
    {
        ShowWindow(window, 9);

        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint target = GetWindowThreadProcessId(window, IntPtr.Zero);
        bool attached = foreground != target && AttachThreadInput(foreground, target, true);

        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached) { AttachThreadInput(foreground, target, false); }
        }

        return GetForegroundWindow() == window;
    }
}
'@

$script:failures = New-Object System.Collections.Generic.List[string]

function Say([string] $name, $value) {
    Write-Host ("{0}={1}" -f $name, $value)
}

# A row that has to work for a GUI to be drivable here. Anything reported with `Say` instead is
# context: a probe that failed on the pixel format would be answering a question nobody asked.
function Require([string] $name, $value) {
    Say $name $value
    if ($value -ne $true) {
        $script:failures.Add($name)
    }
}

function Wait([int] $milliseconds) {
    Start-Sleep -Milliseconds $milliseconds
}

$Automation = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$Control = [System.Windows.Automation.ControlType]

# What the target says it received. It writes the text box out on every change, so this is the
# other side of the boundary reporting what actually reached it.
$script:statePath = Join-Path $env:TEMP 'kpg-probe-state.txt'

function Received {
    if (-not (Test-Path $script:statePath)) {
        return ''
    }

    return (Get-Content $script:statePath -Raw -ErrorAction SilentlyContinue).Trim()
}

Write-Host '--- the session ---'
Say 'session.id' ([System.Diagnostics.Process]::GetCurrentProcess().SessionId)
Say 'session.interactive' ([Environment]::UserInteractive)

# `WinSta0` is the interactive window station. A service under Session 0 isolation gets one named
# for the service instead, and nothing below this line can work there.
$station = [Probe]::NameOf([Probe]::GetProcessWindowStation())
Say 'station.name' $station
Say 'desktop.name' ([Probe]::NameOf([Probe]::GetThreadDesktop([Probe]::GetCurrentThreadId())))
Require 'station.interactive' ($station -eq 'WinSta0')

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Say 'screen.bounds' ("{0}x{1}" -f $screen.Width, $screen.Height)
Say 'screen.count' ([System.Windows.Forms.Screen]::AllScreens.Length)

# The target. A window this script puts up in another process, rather than a program that happens
# to be installed: `notepad.exe` on Windows 11 is a stub that hands off to a Store process, so the
# id it returns owns no window, and a probe built on it reports the accessibility tree missing on a
# machine where the tree is fine.
$targetScript = Join-Path $env:TEMP 'kpg-probe-target.ps1'
@'
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = 'KPG probe target'
$form.Width = 460
$form.Height = 260
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 60, 60

$strip = New-Object System.Windows.Forms.MenuStrip
$file = New-Object System.Windows.Forms.ToolStripMenuItem
$file.Text = '&File'
$open = New-Object System.Windows.Forms.ToolStripMenuItem
$open.Text = '&Open'
$file.DropDownItems.Add($open) | Out-Null
$strip.Items.Add($file) | Out-Null
$form.Controls.Add($strip)
$form.MainMenuStrip = $strip

$box = New-Object System.Windows.Forms.TextBox
$box.Location = New-Object System.Drawing.Point 20, 50
$box.Width = 400
$form.Controls.Add($box)

$button = New-Object System.Windows.Forms.Button
$button.Text = 'Click me'
$button.Location = New-Object System.Drawing.Point 20, 100
$button.Width = 140
$button.Height = 44
$button.Add_Click({ $box.Text = $box.Text + '!' })
$form.Controls.Add($button)

# What arrives is written out rather than left for the other side to read off a control. WinForms
# reports a text box and a button to UI Automation as panes, with no value pattern between them, so
# an assertion made through the tree would be measuring the provider rather than the input.
$state = Join-Path $env:TEMP 'kpg-probe-state.txt'
$box.Add_TextChanged({ Set-Content -Path $state -Value $box.Text -Encoding UTF8 })
Set-Content -Path $state -Value '' -Encoding UTF8

[System.Windows.Forms.Application]::Run($form)
'@ | Set-Content -Path $targetScript -Encoding UTF8

Remove-Item $script:statePath -ErrorAction SilentlyContinue

$target = $null
try {
    $target = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $targetScript -PassThru

    Write-Host '--- the accessibility tree, across a process boundary ---'

    $byName = New-Object System.Windows.Automation.PropertyCondition(
        $Automation::NameProperty, 'KPG probe target')

    $window = $null
    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline -and $null -eq $window) {
        $window = $Automation::RootElement.FindFirst($Tree::Children, $byName)
        if ($null -eq $window) { Wait 500 }
    }

    Require 'uia.windowFound' ($null -ne $window)
    if ($null -eq $window) {
        throw 'Without the window there is nothing left to ask.'
    }

    $handle = [IntPtr] $window.Current.NativeWindowHandle
    Say 'uia.rect' $window.Current.BoundingRectangle
    Say 'uia.handle' $window.Current.NativeWindowHandle
    Require 'uia.otherProcess' ($window.Current.ProcessId -ne [System.Diagnostics.Process]::GetCurrentProcess().Id)
    Require 'window.visible' ([Probe]::IsWindowVisible($handle))

    Write-Host '--- the foreground ---'
    $front = [Probe]::Front($handle)
    Require 'window.foreground' $front
    Wait 500

    # Synthesized input goes wherever the focus is. Without the foreground it would go to somebody
    # else's window, so a probe that could not take it sends nothing. That is also the better
    # answer: it tells apart a machine that refuses the foreground from one that swallows keys.
    if (-not $front) {
        Write-Host '--- synthesized input not attempted: this window is not in front ---'
        Require 'keyboard.arrives' 'not attempted'
        Require 'mouse.arrives' 'not attempted'
    }
    else {
        Write-Host '--- synthesized keyboard ---'

        # Aimed at the window rather than at a control, because the keystrokes go to whatever holds
        # the focus inside it, which is the text box: it is the first control and nothing has moved
        # the focus away.
        foreach ($key in 0x4B, 0x50, 0x47) {
            [Probe]::keybd_event([byte] $key, 0, 0, [UIntPtr]::Zero)
            [Probe]::keybd_event([byte] $key, 0, 2, [UIntPtr]::Zero)
            Wait 250
        }

        $typed = Received
        Say 'keyboard.readBack' ("'" + $typed + "'")

        # Contains them in order rather than equals them, because a desktop somebody is sitting at
        # types into the box as well. On a runner nothing else does.
        Require 'keyboard.arrives' ($typed -cmatch 'k.*p.*g')

        Write-Host '--- synthesized pointer ---'

        # Found by name, and clicked where the tree says it is. Which control type a provider
        # reports is its own business and WinForms says pane for both of these, but the name and the
        # rectangle are what the real driver clicks with anyway.
        $button = $window.FindFirst($Tree::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($Automation::NameProperty, 'Click me')))

        Require 'uia.controlFound' ($null -ne $button)

        if ($null -ne $button) {
            $box = $button.Current.BoundingRectangle
            $x = [int] ($box.Left + ($box.Width / 2))
            $y = [int] ($box.Top + ($box.Height / 2))
            Say 'mouse.target' ("{0},{1}" -f $x, $y)

            [Probe]::SetCursorPos($x, $y) | Out-Null
            Wait 200
            [Probe]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
            Wait 80
            [Probe]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
            Wait 600

            $after = Received
            Say 'mouse.readBack' ("'" + $after + "'")
            Require 'mouse.arrives' ($after -like '*!*')
        }

        # Reported rather than required. A WinForms strip puts its items in the tree only once it
        # has been realized and this provider does not always describe them at all, so a machine
        # that answered zero here would be telling us about WinForms rather than about itself. That
        # menus can be driven is settled by the end to end tier, which drives KeePass's own.
        Write-Host '--- menus, for information ---'
        [Probe]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
        [Probe]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        Wait 700

        $menus = $window.FindAll($Tree::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                $Automation::ControlTypeProperty, $Control::MenuItem)))
        Say 'menu.items' $menus.Count
    }

    # Reported rather than required. The end to end tier excludes the screenshot fixtures, so what
    # the screen reads back matters to the documentation build and to nothing else.
    Write-Host '--- reading the screen ---'
    try {
        $shot = New-Object System.Drawing.Bitmap 200, 200
        $graphics = [System.Drawing.Graphics]::FromImage($shot)
        $graphics.CopyFromScreen(
            (New-Object System.Drawing.Point 60, 60),
            [System.Drawing.Point]::Empty,
            (New-Object System.Drawing.Size 200, 200))
        $graphics.Dispose()

        $distinct = New-Object 'System.Collections.Generic.HashSet[string]'
        for ($x = 0; $x -lt 200; $x += 10) {
            for ($y = 0; $y -lt 200; $y += 10) {
                $distinct.Add($shot.GetPixel($x, $y).Name) | Out-Null
            }
        }

        Say 'capture.distinctValues' $distinct.Count
        Say 'capture.notBlank' ($distinct.Count -gt 1)
        $shot.Dispose()
    }
    catch {
        Say 'capture.notBlank' ('threw: ' + $_.Exception.Message)
    }
}
catch {
    Write-Host ('probe stopped: ' + $_.Exception.Message)
    $script:failures.Add('probe')
}
finally {
    if ($null -ne $target) {
        Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $targetScript -ErrorAction SilentlyContinue
    Remove-Item $script:statePath -ErrorAction SilentlyContinue
}

Write-Host '--- the verdict ---'
if ($script:failures.Count -eq 0) {
    Say 'verdict' 'a GUI can be driven on this machine'
    exit 0
}

Say 'verdict' ('not here: ' + ($script:failures -join ', '))
exit 1
