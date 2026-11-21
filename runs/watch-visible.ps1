# Samples the visible desktop once a second: the foreground window (handle, owning process, title),
# the pointer position, and the clipboard's sequence number. Stops when <Stop> exists or after
# <Minutes>. One CSV line per sample, a line only when something changed unless -Every.
param([Parameter(Mandatory)] [string] $Out, [Parameter(Mandatory)] [string] $Stop, [int] $Minutes = 60, [switch] $Every)

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Visible
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
    public static uint LastInput() { var i = new LASTINPUTINFO(); i.cbSize = 8; GetLastInputInfo(ref i); return i.dwTime; }
    public static string Title(IntPtr h) { var s = new StringBuilder(200); GetWindowText(h, s, 200); return s.ToString(); }
}
'@

$until = (Get-Date).AddMinutes($Minutes)
'time,foreground,pid,process,title,x,y,clipboard,lastinput,ancestry' | Set-Content $Out -Encoding UTF8
$last = ''
while (-not (Test-Path $Stop) -and (Get-Date) -lt $until) {
    $h = [Visible]::GetForegroundWindow()
    $procId = [uint32] 0
    [void] [Visible]::GetWindowThreadProcessId($h, [ref] $procId)
    $name = try { (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { '?' }
    $p = New-Object Visible+POINT
    $okCursor = [Visible]::GetCursorPos([ref] $p)
    $title = ([Visible]::Title($h)).Replace(',', ' ').Replace('"', "'")
    if ($title.Length -gt 60) { $title = $title.Substring(0, 60) }
    $state = "$h,$procId,$name,$title,$($p.X),$($p.Y),$([Visible]::GetClipboardSequenceNumber()),$([Visible]::LastInput())"
    if ($Every -or $state -ne $last) {
        # The foreground process's ancestry, so that a later reading can tell whose run it was.
        $chain = @(); $at = $procId
        $all = @{}; foreach ($p2 in (Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name)) { $all[[uint32]$p2.ProcessId] = $p2 }
        for ($i = 0; $i -lt 8 -and $all.ContainsKey([uint32]$at); $i++) { $chain += "$at"; $at = [uint32]$all[[uint32]$at].ParentProcessId }
        ((Get-Date -Format 'HH:mm:ss') + ',' + $state + ',' + ($chain -join '<') + $(if ($okCursor) { '' } else { ',cursor-failed' })) | Add-Content $Out -Encoding UTF8
        $last = $state
    }
    Start-Sleep -Milliseconds 1000
}
((Get-Date -Format 'HH:mm:ss') + ',end') | Add-Content $Out -Encoding UTF8
