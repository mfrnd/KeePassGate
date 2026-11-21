# Results: the test tiers on a private desktop

## The question

Every KeePassGate test run that shows a window shares the visible desktop of the machine it runs
on, so runs that show windows take turns for it. Windows lets a process start on a desktop of its
own: `CreateDesktop`, then `CreateProcess` with `STARTUPINFO.lpDesktop` naming it. Its windows are
seen by nobody and take no foreground from the visible desktop. Can the unit tier and the host tier
run on such a desktop, so that several runs go in parallel on a machine without disturbing the
visible desktop or each other? Which tests cannot, and why? And for the end to end tier, which
drives a real KeePass with real input: is the visible desktop really needed, or can input reach a
desktop that is not the input desktop with logic added?

## The machine and the product

| | |
| --- | --- |
| Windows | 11 Enterprise Evaluation, build 10.0.26300, a virtual machine under QEMU |
| processors | 12 logical |
| memory | 24 GB |
| logon | an administrator, elevated; every test ran under a basic user token, as the tiers require |
| KeePassGate | `master` at `2b55f511dc375fe28d1a5c3e44e6ba5f353cee75`, "Keep the access rules in the plugin's store (#180)" |
| KeePass | 2.62, the release that commit pins |

Two worktrees of that commit were built in Debug with `dotnet build KeePassGate.sln -c Debug`, so
that two runs never shared a build output. Everything ran on 2026-11-21.

**To repeat a run**: check out that commit, provide its pinned KeePass as its `build.ps1` does,
build it in Debug, and give the worktree to the scripts here. The baselines on the visible desktop
quoted below are the figures that commit's gate reported on the same machine: the unit tier 5,075
passed and 4 skipped in about 2 min 40 s, the host tier 1,436 passed in about 4 min 30 s.

The visible desktop of the machine was shared with other test runs, which took turns through a lock
file. Every run here that showed a window on the visible desktop took that turn. Runs on a private
desktop took it too until step 6 had shown that they disturb nothing, and went without it
afterwards, except the end to end fixtures, for the reason given under step 10.

## The answer

**The unit tier and the host tier run on a private desktop as they are.** The unit tier passed 5,075
of 5,075 there and the host tier 1,436 of 1,436, the same counts as on the visible desktop, with
nothing failing in any arrangement tried: alone, two at once in every pairing, beside a host tier on
the visible desktop, and as part of the whole default gate. Nothing a private run did reached the
visible desktop.

**One change to the tests is worth making, for speed rather than correctness.** The unit tests'
`[Desktop]` attribute takes a mutex named `Local\KeePassGate.Tests.Desktop`, which is shared by the
whole logon session, so the window showing fixtures of two unit tiers take turns although each run
is alone on its own desktop. Two unit tiers at once took 3 min 58 s each with the mutex shared, and
2 min 48 s and 2 min 49 s with it named per run, against 2 min 39 s for a unit tier alone.

**The end to end tier does not run on a private desktop as it stands, and could with logic added.**
A private desktop has no foreground window, refuses synthesized input and has no screen to copy, and
UI automation's own Invoke and Toggle fail there because they synthesize a key to take the
foreground. Of the 29 fixtures, 7 passed on a private desktop unchanged. Messages posted to
KeePass's windows, with the modifier keys set in KeePass's input state, and Microsoft Active
Accessibility's default action reached every kind of input tried: text, Enter and Escape, shortcuts
with Ctrl, menus by key and by press, buttons, selection in the tree and the list, and context
menus. They do not reach the screen or the notification area, which a private desktop does not
have, nor raise a tooltip by hovering, which the tier does not need; the wheel was not tried. The
price is what the tier exists to prove: that real input, delivered by Windows to whatever window
holds the foreground, does what an operator's does. **The recommendation is to keep the end to end
tier on the visible desktop and move everything else off it** (step 12).

## 1. The launcher, and what a process can do on a private desktop

`launcher\private-desktop.ps1`, called from an elevated process, hands itself to
`launcher\basic-user.ps1 -Headless`, so that a process under a basic user token creates the desktop
(`CreateDesktopW` in `WinSta0`, `GENERIC_ALL`, that token's default security) and starts the child
on it with `CreateProcessW` and `lpDesktop = "WinSta0\<name>"`, under a new console that is on that
desktop too. It waits, closes the desktop, and samples meanwhile which processes own windows on the
private desktop and which processes of the child's tree own visible windows on the visible desktop.

Two things learned building it. `STARTF_USESHOWWINDOW` in the child's `STARTUPINFO` is applied by
Windows to the first window the child shows, whatever the child asks for: with `SW_SHOWMINNOACTIVE`
there, the probe's form came up minimized and too small to sample, so the launcher sets no show
command. And a variable named `$inner` in a script with an `-Inner` switch is that switch, since
PowerShell names ignore case.

`probe\desktop-probe.ps1` with `probe\DesktopProbe.cs` ran once on a private desktop through the
launcher and once on the visible desktop under a basic user token, both STA
(`results\desktop-probe-private.txt`, `results\desktop-probe-visible.txt`).

| asked | on a private desktop | on the visible desktop |
| --- | --- | --- |
| `GetThreadDesktop`, by `UOI_NAME` | the private desktop's name | `Default` |
| window station | `WinSta0` | `WinSta0` |
| `OpenInputDesktop`, by name | `Default` | `Default` |
| a form shown: handle, `IsWindowVisible`, size | yes, true, 400 by 300 | yes, true, 400 by 300 |
| `DrawToBitmap`, distinct colors sampled | 187 | 187 |
| `PrintWindow(PW_RENDERFULLCONTENT)` | true, 61 colors | true, 61 colors |
| `Graphics.CopyFromScreen` | throws `Win32Exception`, "The handle is invalid" | 151 colors |
| `GetForegroundWindow` after `Activate` | 0 | the form |
| `GetActiveWindow` after `Activate` | the form | the form |
| `Form.ActiveForm` | null | the form |
| `TextBox.Focus`, then `GetFocus` | the box | the box |
| `SetForegroundWindow(form)` | false | true |
| `GetCursorPos` | false, error 5 | true |
| `SetCursorPos`, 37 by 23 pixels away | false, the pointer unchanged | true, the pointer moved |
| `SendInput`, a zero mouse move | 0, error 5 | 1 |
| `keybd_event` of an unassigned key, then `GetLastInputInfo` | last input time unchanged | advanced |
| `mouse_event`, a zero move | last input time unchanged | advanced |
| UI automation: the root, `FromHandle(form)`, the form among the root's children | the private desktop's own root; works; found | works; found |
| `ToolTip.Show` | no exception | no exception |
| the clipboard's sequence number | the same as the visible desktop's at that moment | |

A private desktop paints, focuses and activates within a thread as the visible desktop does, and
UI automation works there against that desktop's own tree. What it lacks is everything that belongs
to the input desktop: a foreground window, the pointer, synthesized input, and the screen's pixels.
Input synthesized there is refused rather than delivered elsewhere, which is what makes a private
run harmless to the visible desktop. The clipboard belongs to the window station and so is shared
with the visible desktop; no run here changed it.

Through the private probe the visible desktop, sampled once a second by `runs\watch-visible.ps1`,
kept the same foreground window, pointer position, clipboard sequence number and last input time.

## 2. The unit tier alone on a private desktop

`runs\run-tier.ps1 -Tier unit`, with the arguments the commit's `build.ps1` gives the tier.

| | passed | failed | did not run | test run | wall clock |
| --- | --- | --- | --- | --- | --- |
| private desktop | 5,075 | 0 | 8 | 2 min 38.9 s | 2 min 51.7 s |
| visible desktop, the gate's baseline | 5,075 | 0 | 4 skipped | about 2 min 40 s | |

The eight that did not run are four held explicit as measurements and four that need the backup and
restore privileges, which ignore themselves under a basic user token; the console counts only the
second four as skipped, which is the baseline's 4. The launcher saw up to 13 windows of
`testhost.net48` on the private desktop and none of the child's tree on the visible desktop.

The wall clock is taken from the elevated side and carries the launcher's own cost: about 1.5 s to
get through `runas` and `Add-Type`, the desktop and the child then existing within 0.3 s, and up to
11 s at the end, because `basic-user.ps1` looks for the exit file every ten seconds. The launcher's
own log times the child at 2 min 41.1 s for this run.

## 3. The host tier alone on a private desktop

| | passed | failed | did not run | test run | wall clock |
| --- | --- | --- | --- | --- | --- |
| private desktop, first run | 1,436 | 0 | 35 | 4 min 50.2 s | 5 min 1.8 s |
| private desktop, last run of the day | 1,436 | 0 | 35 | 4 min 42.4 s | 4 min 51.9 s |
| visible desktop, alone, the same day | 1,436 | 0 | 35 | 4 min 39.4 s | 4 min 51.3 s, with `build.ps1`'s build |
| visible desktop, the gate's baseline | 1,436 | 0 | | about 4 min 30 s | |

The 35 that did not run are the fixtures held explicit as measurements. Up to 9 windows of
`testhost.net48` on the private desktop, none of the tree on the visible desktop. A private desktop
cost the host tier between 3 and 11 seconds against the visible run alone, which is inside the
spread of the visible runs themselves: 4 min 39.4 s alone, and 4 min 51.7 s and 4 min 55.7 s beside
private runs in step 6.

## 4. Failures by cause

There are none to sort: no test of either tier failed on a private desktop, in any run here.

Read against step 1, that is what the tests' own calls predict. They drive their windows with
`PostMessage` and `SendMessage`, which reach a window's queue whatever desktop it is on; they ask
`GetActiveWindow`, `SetActiveWindow` and `GetFocus`, which are about the calling thread and work on
any desktop; they draw with `DrawToBitmap` and `PrintWindow`, which paint there; they use a fake
clipboard; and nothing in either tier calls `GetForegroundWindow`, `SetForegroundWindow`,
`SetCursorPos`, `keybd_event`, `mouse_event`, `SendInput` or `CopyFromScreen`. Those calls are all
in the end to end tier, under `tests\KeePassGate.Host.Tests\Live`.

## 5. Two runs at once, each on a private desktop of its own

`runs\parallel.ps1`, the first worktree beside the second. Beyond what the question asked, two unit
tiers ran at once twice: as the tests stand, and with the `[Desktop]` mutex named per run. For the
second, `DesktopAttribute.LockName` was changed in both worktrees from the constant
`@"Local\KeePassGate.Tests.Desktop"` to that string followed by the value of the environment
variable `KPG_MEASURE_DESKTOP`, both were rebuilt, `run-tier.ps1 -DesktopMutex` set the variable
to each run's name, and the change was then reverted and both rebuilt again.

| arrangement | run | passed | failed | test run | alone | cost |
| --- | --- | --- | --- | --- | --- | --- |
| unit beside host | unit | 5,075 | 0 | 2 min 41.2 s | 2 min 38.9 s | +1 % |
| | host | 1,436 | 0 | 5 min 19.6 s | 4 min 50.2 s | +10 % |
| host beside host | host, first worktree | 1,436 | 0 | 5 min 22.4 s | 4 min 50.2 s | +11 % |
| | host, second worktree | 1,436 | 0 | 5 min 20.5 s | 4 min 50.2 s | +10 % |
| unit beside unit, mutex shared | unit, first | 5,075 | 0 | 3 min 58.6 s | 2 min 38.9 s | +50 % |
| | unit, second | 5,075 | 0 | 3 min 58.0 s | 2 min 38.9 s | +50 % |
| unit beside unit, mutex per run | unit, first | 5,075 | 0 | 2 min 48.3 s | 2 min 38.9 s | +6 % |
| | unit, second | 5,075 | 0 | 2 min 49.4 s | 2 min 38.9 s | +7 % |

| arrangement | both finished in | the same two in sequence |
| --- | --- | --- |
| unit and host | 5 min 50.8 s | 7 min 53.5 s |
| host and host | 5 min 44.5 s | about 10 min 4 s |
| unit and unit, mutex shared | 4 min 13.7 s | about 5 min 43 s |
| unit and unit, mutex per run | 3 min 3.5 s | about 5 min 43 s |

No failure, and the visible desktop did not change during any of them. On twelve processors the
host tier, which runs its tests in sequence, gives up about a tenth of its speed to a second run;
the unit tier, which runs its fixtures side by side, gives up little to a host tier.

Where the unit tier's extra 80 seconds went beside another unit tier, and the 10 seconds it still
lost with the mutex named per run, summed per class from the result files by
`runs\compare-classes.ps1`:

| unit beside unit | the 22 classes carrying `[Desktop]` | the 146 other classes |
| --- | --- | --- |
| alone | 51.3 s | 597.2 s |
| mutex shared | 104.5 s | 784.6 s |
| mutex per run | 53.9 s | 741.0 s |

The `[Desktop]` fixtures are also `[NonParallelizable]`, so they run with nothing beside them and
their time is on the critical path: sharing the mutex doubled it, and naming it per run gave it
back. The rest of the growth is the two runs sharing the processors.

## 6. A private run beside a run on the visible desktop

The host tier on the visible desktop, through the commit's `build.ps1 -Task Host` in a visible
console under a basic user token, with private runs from the second worktree beside it and the
visible desktop sampled every second.

| round | run | desktop | from | to | passed | failed | test run |
| --- | --- | --- | --- | --- | --- | --- | --- |
| a | host, `build.ps1 -Task Host` | visible | 15:25:32 | 15:30:28 | 1,436 | 0 | 4 min 51.7 s |
| a | unit | private | 15:25:52 | 15:28:44 | 5,075 | 0 | 2 min 43.4 s |
| a | host | private | 15:28:44 | 15:33:36 | 1,436 | 0 | 4 min 42.7 s |
| b | host, `build.ps1 -Task Host` | visible | 15:49:06 | 15:54:06 | 1,436 | 0 | 4 min 55.7 s |
| b | host | private | 15:49:21 | 15:54:23 | 1,436 | 0 | 4 min 57.0 s |

In round a the private host tier overlapped the visible run for its last 1 min 44 s; in round b it
started fifteen seconds after the visible run and overlapped it throughout. Neither side gained a
failure.

| round | samples | foreground | pointer | clipboard | last input |
| --- | --- | --- | --- | --- | --- |
| a | 459 | the visible run's console from its start to its end, then the window that held the foreground before | unchanged | unchanged | unchanged |
| b | 300 | the same | unchanged | unchanged | unchanged |

Every change is the visible run's: its console took the foreground when it started and gave it back
when it ended, and its own test host never took it. Nothing changed that the visible run does not
explain.

## 7. The whole default gate on a private desktop

The commit's `.\build.ps1` with no task, through the launcher.

| | outcome |
| --- | --- |
| exit code | 0, "Test finished." |
| wall clock | 9 min 1.5 s |
| unit tier | 5,075 passed, 0 failed, 2 min 59.4 s |
| host tier | 1,436 passed, 0 failed, 5 min 18.5 s |
| the client, beside the plugin | 1,981 passed, 15 skipped, coverage 97.60 % against a floor of 90 % |
| plugin coverage | 30,961 of 33,724 lines, 91.8 %, against a floor of 80 % |

As it happened, another test run holding the machine's desktop at the time was driving a KeePass on
the visible desktop throughout: its KeePass held the foreground, and the pointer moved and keys were
pressed. By the process ancestry the watcher recorded, each of those changes belonged to that
run's tree and none to the gate's, which owned no visible window on the visible desktop. The gate
passed; whether that other run passed is not something this measurement can see.

## 8. The end to end tier, two fixtures as a first look

Under the machine's lock, since a Live session ends every KeePass running from under
`%ProgramData%\KeePassGateLive` and so could end another run's.

| fixture | outcome | where it stopped |
| --- | --- | --- |
| `LiveStoreTests` | 3 passed, 0 failed, 10.9 s | did not stop: it starts KeePass on its database, puts ten requests to the gate and has KeePass exit, typing nothing; KeePass opened 20 windows on the private desktop |
| `LiveGrantPickerCloseTests` | 2 failed in the one time set up, after 7.6 s | `Keyboard.Front`: "'throwaway.kdbx - KeePass' could not be brought to the front, so nothing was typed. In front instead: ''." |

KeePass exited both times, and the visible desktop did not change. Steps 9 and 10 took this
further.

## 9. What reaches a real KeePass on a private desktop without real input

`probe\new-keepass.ps1` builds a throwaway KeePass as the end to end tier does: a copy of the pinned
KeePass, the configuration the tier writes, the plugin as built, and a database of three invented
entries under a dummy password. `probe\InputProbe.cs`, built by `probe\build-input-probe.ps1`,
starts that KeePass on the desktop it is itself on and drives it with nothing but messages posted or
sent to KeePass's windows, UI automation, and Microsoft Active Accessibility, the way the tier
drives KeePass from its test host, as a client in another process. `probe\run-input-probe.ps1` ran
it once on a private desktop and once on the visible desktop for comparison
(`results\input-probe-private.txt`, `results\input-probe-visible.txt`).

"Set" below means the modifier was set in KeePass's own input state for the moment of the post:
`AttachThreadInput` from the probe's thread to KeePass's, `SetKeyboardState` with the key down,
the message posted, a pause, the key let go, and the threads detached.

| | route | private desktop | visible desktop |
| --- | --- | --- | --- |
| A0 | after start: KeePass's thread, by `GetGUIThreadInfo` | the main window active, focus in the quick search box; `GetForegroundWindow` 0 | the same; `GetForegroundWindow` the main window |
| A1 | `SetForegroundWindow(main)` from the probe | false | true |
| A2 | `AttachThreadInput`, `SetActiveWindow(main)`, `SetFocus(list)` | the focus moved to the entry list | the same |
| A3 | UI automation `SetFocus` on the entry list | the focus moved | the same |
| T1 | `WM_CHAR` posted to the quick search box | the text arrived | the same |
| T2 | Enter posted to the quick search box | the search ran | the same |
| K1 | Ctrl+I posted, Ctrl not set | nothing | nothing |
| K2 | Ctrl+I posted, Ctrl set | Add Entry opened, active, focus in its title box | the same |
| D1 | `WM_CHAR` posted to the title box | the text arrived | the same |
| D2 | Enter posted to the title box | the dialog's OK: closed, the entry added | the same |
| K3 | Escape posted to the dialog's focused control | the dialog's Cancel: closed | the same |
| B1 | `BM_CLICK` sent to Cancel | closed | closed |
| B2 | `WM_LBUTTONDOWN` and `WM_LBUTTONUP` posted to Cancel | closed | closed |
| B3 | UI automation `InvokePattern.Invoke` on Cancel | **throws `InvalidOperationException`**, the dialog stays | closed |
| B4 | `accDoDefaultAction` on Cancel, through `oleacc` | closed | closed |
| V1 | UI automation `ValuePattern.SetValue` on the title box | the text arrived | the same |
| V2 | UI automation `TogglePattern.Toggle` on a check box | **throws `Win32Exception`** | toggled |
| V3 | UI automation `InvokePattern.Invoke` on OK | **throws `Win32Exception`**, the dialog stays | closed, the entry added |
| S1 | Ctrl+S posted, Ctrl not set | nothing | nothing |
| S2 | Ctrl+S posted, Ctrl set | saved | saved |
| M1 | Alt+F posted as `WM_SYSKEYDOWN`, `WM_SYSCHAR`, `WM_SYSKEYUP`, Alt not set | **no menu** | the File menu opened |
| M2 | the same with Alt set | the File menu opened | the same |
| M3 | then the item's access key posted | Database Settings opened | the same |
| M4 | UI automation `Expand` on File, then `Invoke` on the item, on a thread of its own | the dialog opened; the `Invoke` did not return while it stood, and a second automation call against KeePass timed out after 67 s | the same, 67 s |
| M5 | a click posted to the menu bar's window at File | the File menu opened | the same |
| M6 | then a click posted to the open menu at the item | Database Settings opened | the same |
| L1 | UI automation `SelectionItemPattern.Select` on a group | selected | selected |
| L2 | a click posted to the group tree at a group | selected | selected |
| L3 | a click posted to the entry list at a row | selected | selected |
| L4 | UI automation `Select` on a row | selected | selected |
| C1 | `WM_CONTEXTMENU` posted to the entry list, as from the keyboard | the context menu opened | the same |
| C2 | the right button posted down and up at a row | the context menu opened | the same |
| C3 | Shift+F10 posted, Shift set | the context menu opened | the same |
| C4 | Escape posted to the open context menu | closed | closed |
| H1 to H3 | `WM_MOUSEMOVE` and `WM_MOUSEHOVER` posted over a toolbar button and a row | no tooltip | **no tooltip either** |

**Why UI automation's Invoke and Toggle fail on a private desktop**, read off the stack traces of
the fixtures in step 10 that met it: the providers UI automation uses for Win32 controls,
`MS.Internal.AutomationProxies.WindowsButton.Invoke` and, where the default action is reached
through UI automation, `MsaaNativeProvider.CallDoDefaultAction`, both call
`MS.Internal.AutomationProxies.Misc.SetFocus`, which synthesizes a key with `SendInput` to win the
foreground and throws when `SendInput` is refused. "Hot key is already registered" is a stale error
code that exception carries, not a hot key. Accessibility's own `accDoDefaultAction`, called through
`oleacc` directly (B4), synthesizes nothing and works.

**Menus.** KeePass's menus are WinForms `ToolStrip` menus: the main window has no Win32 menu
(`GetMenu` answers 0), so `WM_COMMAND` reaches no item. Posted keys and posted clicks both open
them and choose from them. The difference between the desktops in M1 is that on the visible desktop
KeePass took a posted Alt+F as a menu key with Alt not down, and on a private desktop it did not;
with Alt set, both did. The tree's `Invoke` on an item that opens a modal dialog blocks every
automation call against KeePass for as long as the dialog stands on either desktop, which is what
the tier's `KeePassDriver` already records as its reason for choosing menus by keystroke.

**Activation without the foreground.** KeePass's thread on a private desktop has an active window
and a focus window of its own, which `GetGUIThreadInfo` reads from another process; a dialog
KeePass opens becomes that thread's active window with the focus in it, as on the visible desktop.
What the drivers' `Keyboard.Front` checks, `GetForegroundWindow` equal to the window, cannot hold
there. What can stand for it is the thread's active window from `GetGUIThreadInfo`, which another
process can set with `AttachThreadInput` and `SetActiveWindow` (A2), and the keys then go to the
thread's focus window by `PostMessage` rather than to the foreground by `keybd_event`.

**Tooltips** follow the real pointer: posted moves raised none on either desktop. The end to end
tier needs none; `Mouse.Park` exists to keep them out of the pictures.

**Reading the screen.** Pictures of Add Entry, Database Settings and the main window:

| method | private desktop | visible desktop |
| --- | --- | --- |
| `PrintWindow(PW_RENDERFULLCONTENT)` | painted, the compositor's caption | painted, the compositor's caption |
| `PrintWindow(0)`, and `BitBlt` from `GetWindowDC` | painted, with the frame the window draws itself rather than the compositor's | the same, identical to the private desktop's to the pixel |
| `Graphics.CopyFromScreen` | throws `Win32Exception`, "The handle is invalid" | the screen, what is behind the window included |

`probe\compare-pictures.ps1` and `probe\caption-ink.ps1` held the
`PrintWindow(PW_RENDERFULLCONTENT)` pictures of the two desktops against each other. Database
Settings differs in 444 of 240,588 pixels, all of them in the top 32 rows, the caption; its text is
gray, 145,145,145, on the private desktop and black on the visible desktop, which are the captions
of a window that is not and that is in the foreground. Add Entry differs in 262 caption pixels and
in 105 more on its password quality bar, whose generated password differs from run to run. The main
window's two pictures hold different entries, since V3 added an entry only on the visible desktop,
and are not compared.

The documentation's pictures are taken by `Screenshot.cs` as a screen copy over two backdrops,
black and white, solved for transparency, which `spikes/window-capture` chose after ruling out
`PrintWindow` for its square corners and missing shadow. On a private desktop there is no screen to
copy, so that method cannot run there at all; `PrintWindow` can, with the drawbacks that branch
measured and an inactive caption besides.

## 10. The end to end tier, fixture by fixture, on a private desktop as it stands

`runs\live-sweep.ps1` ran each of the tier's 29 fixtures alone on a private desktop, in sequence,
with `TestCategory=Live` and the fixture's name as the filter, `DocumentationScreenshots` among them
with its pictures sent to a scratch directory. Each fixture took the machine's desktop lock for its
run although it showed nothing on the visible desktop: a Live session ends every KeePass running
from under `%ProgramData%\KeePassGateLive` (`LiveSession.EndOrphans`) and builds its throwaway in a
directory named after the fixture there, so a Live fixture anywhere on the machine ends any other
Live run's KeePass, whatever desktop that run is on. The sweep took 978 s; nothing needed stopping,
and no KeePass was left running (`results\live-sweep.txt`).

| fixture | tests | passed | where it stopped |
| --- | --- | --- | --- |
| `LiveApiTests` | 9 | 9 | did not stop |
| `LiveCertificateTests` | 4 | 4 | did not stop |
| `LiveCloseAfterTheFileTests` | 5 | 5 | did not stop |
| `LiveGateTests` | 12 | 12 | did not stop |
| `LiveLockTimerTests` | 3 | 3 | did not stop |
| `LiveSessionLockTests` | 4 | 4 | did not stop |
| `LiveStoreTests` | 3 | 3 | did not stop |
| `LiveCloseBeforeTheFileTests` | 5 | 3 | KeePass's question on closing a changed database is answered through UI automation's `Invoke`, which threw; the fixture's own wrapper swallows that, so the question stood and KeePass never exited |
| `LiveExitUnderARenewalTests` | 4 | 1 | the approval dialog appeared; Exit is chosen from KeePass's icon in the notification area, which it looks for under `Shell_TrayWnd`, and a private desktop has no taskbar |
| `LiveAbandonedSaveTests` | 5 | 0 | the default action through UI automation: `MsaaNativeProvider.CallDoDefaultAction` reached `Misc.SetFocus`, whose `SendInput` was refused |
| `LiveChooserOverTheMainWindowTests` | 7 | 0 | `WindowsButton.Invoke`, the same `SendInput` |
| `LiveRenewalTests` | 8 | 0 | `WindowsButton.Invoke`, the same `SendInput` |
| `LiveAuthorityChooserTests` | 13 | 0 | the File menu expanded through UI automation to learn its keys, and the Synchronize submenu's item never appeared within 30 s |
| `LiveOwedAnswerAtExitTests` | 3 | 0 | `Keyboard.Front` on the renewal dialog: no foreground |
| `DocumentationScreenshots` | 2 | 0 | `Keyboard.Front` on the main window: no foreground; the pictures would have needed the screen next |
| 14 more, below | 111 | 0 | `Keyboard.Front` on the main window: no foreground |

The fourteen are `LiveAttachmentEditorTests`, `LiveAuthorityTests`, `LiveExitUnderAPairingTests`,
`LiveGrantEditorTests`, `LiveGrantPickerCloseTests`, `LiveKeePassTests`, `LiveLockTests`,
`LivePairingTests`, `LiveReferenceClientTests`, `LiveRuleOkInsideASaveTests`,
`LiveRuleSeededStatesTests`, `LiveRuleWaitsForTheSaveTests`, `LiveStoreSetBackTests` and
`LiveTanWizardTests`. Every message read the same: "'throwaway.kdbx - KeePass' could not be brought
to the front, so nothing was typed. In front instead: ''."

In all, 44 of 198 tests passed, and the seven fixtures that passed whole took 175 s together.
The other 22 failed in their one time set up, at the first step needing the input desktop, but
for the two that passed in part, which failed further on. The seven drive KeePass by starting it,
by the gate's own connections and by closing its main window, and synthesize no input,
though `LiveSessionLockTests` posts a message to KeePass.

**How the tier's input was counted.** `runs\count-calls.ps1` counts, in each file of
`tests\KeePassGate.Host.Tests\Live` at the commit, the calls that deliver input or read the screen,
outside comments, by kind; a call to a method of `KeePassDriver` is counted where it is written and
classified by what that method does inside (read from its source), and the helpers' own files
(`KeePassDriver.cs`, `LiveRules.cs`, `Screenshot.cs` and the rest) are counted on rows of their own
(`results\live-calls.csv`).

| kind | calls in every file | in the fixtures' own files | fixture files using it |
| --- | --- | --- | --- |
| a menu chosen by keystroke (Alt and access keys) | 36 | 34 | 14 |
| a shortcut with Ctrl | 20 | 15 | 13 |
| a plain key: Enter, Escape, a letter | 36 | 25 | 8 |
| Shift+F10 | 1 | 0 | 0 |
| a press with the pointer | 29 | 23 | 7 |
| the pointer moved, or the wheel turned | 9 | 2 | 1 |
| a check or a grab of the foreground | 30 | 7 | 6 |
| a UI automation pattern | 122 | 75 | 18 |
| a message posted or sent | 9 | 4 | 3 |
| the screen read | 9 | 5 | 2 |

Every kind but the screen and the wheel has a route through messages or accessibility measured in
step 9, and the wheel, which `LiveRules.cs` turns with `mouse_event`, was not tried: keys and
chords as messages to the focus window of KeePass's thread with the modifiers set in its input
state, presses as button messages posted at the place, the context menu as `WM_CONTEXTMENU`, a
button by `BM_CLICK` or `accDoDefaultAction`, a value and a selection through UI automation's own
`ValuePattern` and `SelectionItemPattern`, which worked on a private desktop, and the foreground
check through `GetGUIThreadInfo`. The screen has no such route: `PrintWindow` stands in for a copy
of it at the cost step 9 measured, and the notification area does not exist on a private desktop at
all, so the tray's menu would have to be reached by posting its callback message to KeePass's own
icon window, which `TrayExit` already posts once it has found the icon on the taskbar.

## 11. Making a private desktop the input desktop, and another session

**`SwitchDesktop` was not called**: it would have taken the screen from everybody using the
machine. Its documentation: it "makes the specified desktop visible and activates it. This enables
the desktop to receive input from the user." The desktop must be in the caller's window station,
and the call fails from a secured desktop such as Winlogon's. A window station has a single input
desktop at a time, which is what makes it the input desktop, so switching does not add a desktop
that takes input: it moves the screen, the pointer and the keyboard from the visible desktop to the
private desktop for as long as the run lasts. A run there would have real input and real foreground
again, and two runs could not both have it, so it gives no parallel runs; it gives the run that
holds the machine's screen a desktop the operator cannot reach, and leaves the operator facing an
empty screen should the run end without switching back.

**Another session**, read without changing anything:

| | on this machine |
| --- | --- |
| `query session` | `services`, session 0, disconnected; `console`, session 1, active; nothing else |
| edition | Windows 11 Enterprise Evaluation, SKU 72: a client edition, not the multi-session edition |
| Remote Desktop | `fDenyTSConnections` 1, the service `TermService` stopped and manual, `fSingleSessionPerUser` 1 |
| Windows Sandbox (`Containers-DisposableClientVM`) | offered, disabled |
| Hyper-V (`Microsoft-Hyper-V-All`) | offered, disabled; `VirtualMachinePlatform` enabled |
| the processor as this machine sees it | `VMMonitorModeExtensions` false, `VirtualizationFirmwareEnabled` false, `HypervisorPresent` true |

Each session has its own interactive window station and its own input desktop, so in a second
session the tier would run unchanged, real input included, beside a run in the first. A client
edition of Windows keeps one interactive session active at a time, and a remote connection takes
over the console session rather than adding a session, so a second session needs a server edition
with Remote Desktop Session Host or the multi-session edition, and an account for each session. A
sandbox or a nested virtual machine has a desktop of its own too, and needs the processor's
virtualization extensions passed through to this machine, which it does not see now, plus an image
holding the .NET SDK, the pinned KeePass and the worktree. Nothing was enabled or installed.

## 12. What the change would cost the tier, and the recommendation

The end to end tier exists to drive a real KeePass the way an operator does; the unit tier and the
host tier already post messages. What a run by posted messages and accessibility no longer proves:

- **That Windows delivers the keys where an operator's go.** Real keys go to the focus window of
  whichever thread holds the foreground; a posted key goes to the window it is posted to, whoever
  holds the foreground. Every fixture that types relies on the first: `Keyboard.Front` is the
  check that the window about to be typed into is the window that has the foreground, which is the
  operator's situation, and that check is what stopped 16 fixtures here.
- **Activation and focus as Windows decides them.** Whether a dialog the plugin raises takes the
  foreground or the focus from whatever the operator was typing into, and whether a press on a
  window that is not active activates it on the way in, is decided by the window manager for real
  input and not at all for a posted message. `KeePassDriver.OpenRefuseMenu` records that the
  documentation's run once pressed a dialog that was not active and the menu it opened was closed by
  the activation; a posted press would not have shown that. The approval dialogs' rule of never
  taking the focus is held by the unit tier's `ApprovalDialogTests`, in process; in the end to end
  tier the fixtures around the approval dialogs (`LivePairingTests`, `LiveRenewalTests`,
  `LiveExitUnderAPairingTests`, `LiveExitUnderARenewalTests`, `LiveOwedAnswerAtExitTests`) are where
  the dialogs meet KeePass's own windows under real input.
- **A press landing on the window under the pointer.** A real press is delivered to whatever window
  Windows finds at that point, a dropdown or a dialog standing over the target included; a posted
  press is delivered to the window it names. The grant table's cell that opens the picker, the
  arrow of the Refuse button and the clients page's mark are all decisions a control makes about
  where a press landed (`LiveGrantPickerCloseTests`, `LiveRenewalTests`, `LiveRules.cs` with the
  fixtures that use it, and the documentation's lifecycle).
- **A key pressed while another is held.** Real input sets the system's key state, synchronous and
  asynchronous; `SetKeyboardState` sets only the synchronous state of the attached threads for the
  moment of the post. KeePass's shortcuts read the synchronous state and worked; anything reading
  `GetAsyncKeyState` would see no modifier, and that was not measured.
- **The notification area and the screen.** The tray menu that `LiveExitUnderARenewalTests` and
  `LiveExitUnderAPairingTests` exit KeePass from belongs to the visible desktop's taskbar, and the
  documentation's pictures are copies of the screen with a caption drawn as an operator sees it.

**Which fixtures fall on which side, as the tier stands:** the seven that passed whole in step 10
need nothing of the visible desktop; the other 22 need it. A mixed arrangement buys less than the
seven suggest, because the seven may not run beside any other Live run either, a private desktop or
not: `LiveSession` shares `%ProgramData%\KeePassGateLive` by fixture name and ends every KeePass
running from under it. Taking them off the visible desktop would free about three minutes of it per
end to end run, and to run them in parallel with anything Live, `LiveSession` would need a root of
its own per run and an `EndOrphans` limited to it, a small change to the tier.

**Recommendation: keep the end to end tier on the visible desktop, under the desktop lock, as it
is, and move the unit tier, the host tier and the whole default gate to private desktops.** The
price of keeping it is the visible desktop's turn, which the end to end tier then has to itself,
and which the rest of the gate no longer needs. Converting the tier to posted messages and
accessibility is possible, by measurement, for every kind of input it sends but the wheel, which was
not tried. It would cost a rewrite of `Keyboard` and `Mouse`, of the presses, expansions and toggles
through UI automation in `KeePassDriver`, `Uia` and 14 other files of the tier, and of the tray's
path, while the screenshots stayed on the visible desktop regardless; and what it would cost the
tier is the list above, which is the reason the tier exists. If more than one end to end run at a
time is ever wanted, the route that keeps real input is a second session, each with its own input
desktop, which this machine's edition and settings do not offer as they are (step 11).

## What was not measured

- No more than two private desktops existed at once beside the visible desktop. Desktop heap was
  not measured, and nothing ran short.
- The unit tier was not rerun alone on the visible desktop; its comparison is with the gate's
  baseline.
- The plugin's approval dialog calls `FlashWindowEx` on its owner, and the unit tier shows that
  dialog. The foreground and the pointer were watched, the taskbar was not, so whether a flash on a
  private desktop shows on the visible taskbar is not known.
- The wheel, which `LiveRules.cs` turns with `mouse_event`, was not tried as a posted message, and
  nothing in KeePass was checked for reading `GetAsyncKeyState`.
- End to end fixtures never ran two at once or beside another Live run, by design (step 10), and
  the tier was not rewritten to posted messages and run: step 12's conversion is a measured
  possibility for each kind of input, not a measured tier.
