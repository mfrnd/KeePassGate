# Counts, per file of the end to end tier, the calls that deliver input or read the screen, by kind.
# A call is counted where it is written: a fixture's own file, and the shared helpers on their own
# rows. The driver's methods are counted at their call sites and classified by what they do inside.
param([Parameter(Mandatory)] [string] $Live, [string] $Csv)
$kinds = [ordered] @{
    'menu by keystroke'      = '\.(OpenDatabaseSettings|OpenStatusWindow|OpenGroupDialog|OpenEntryDialog|OpenChosenEntryDialog|OpenTanWizard|OpenAttachmentEditor|Synchronize)\('
    'shortcut with Ctrl'     = '\.(OpenAddEntryDialog|ChangeInAttachmentEditor)\(|[dD]river\.(Save|LockWorkspace)\(|Keyboard\.SendControl\('
    'plain key'              = '\.(CloseDialog|PrepareTans)\(|Keyboard\.(Send|SendEscape|SendEnter)\('
    'Shift+F10'              = 'Keyboard\.SendShiftF10\('
    'pointer press'          = 'Mouse\.(Click|RightClick)\(|\.(OpenRefuseMenu|OpenClientMenu|PressClientMark)\('
    'pointer move or wheel'  = 'Mouse\.Park\(|mouse_event\(|Cursor\.Position'
    'foreground check'       = 'Keyboard\.(Front|BringToFront)\(|GetForegroundWindow\(|SetForegroundWindow\('
    'automation pattern'     = 'SelectionItemPattern|InvokePattern|ValuePattern|TogglePattern|ExpandCollapsePattern|\.(SelectGroup|SelectEntry|SelectFirstEntry|SelectGateTab|SelectPage|SelectAuthority|PressButton|AcceptDialog)\(|\.SetFocus\(\)'
    'posted or sent message' = 'TestNativeMethods\.(PostMessage|SendMessage)\('
    'screen read'            = 'CopyFromScreen|Screenshot\.|Steadied\.|Redacted\.'
}
$rows = foreach ($file in Get-ChildItem $Live -Filter '*.cs' | Sort-Object Name) {
    $text = Get-Content $file.FullName -Raw
    # Comments carry the names too; count code only.
    $code = ($text -split "`n" | Where-Object { $_ -notmatch '^\s*///' -and $_ -notmatch '^\s*//' }) -join "`n"
    $row = [ordered] @{ File = $file.Name }
    $any = $false
    foreach ($k in $kinds.Keys) { $n = ([regex]::Matches($code, $kinds[$k])).Count; $row[$k] = $n; if ($n) { $any = $true } }
    if ($any) { [pscustomobject] $row }
}
if ($Csv) { $rows | Export-Csv $Csv -NoTypeInformation -Encoding UTF8 }
$rows | Format-Table -AutoSize | Out-String -Width 300
