# Results: the test tiers on a private desktop

## The question

Every KeePassGate test run that shows a window shares the visible desktop of the machine it runs
on, so runs that show windows take turns for it. Windows lets a process start on a desktop of its
own: `CreateDesktop`, then `CreateProcess` with `STARTUPINFO.lpDesktop` naming it. Its windows are
seen by nobody and take no foreground from the visible desktop. Can the unit tier and the host tier
run on such a desktop, so that several runs go in parallel on a machine without disturbing the
visible desktop or each other? Which tests cannot, and why?

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
afterwards, except the end to end fixtures of step 8, for the reason given there.

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

**The end to end tier stays on the visible desktop.** A fixture that only starts KeePass and talks
to the gate passed on a private desktop; a fixture that types stopped at its first
`Keyboard.Front`, because a private desktop has no foreground window and refuses synthesized input.
Independently of the desktop, the tier shares a directory per fixture under
`%ProgramData%\KeePassGateLive`, and each session, by its code (`LiveSession.EndOrphans`), ends
every KeePass running from under that root, so a Live run anywhere ends any other Live run's
KeePass.

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

KeePass exited both times, and the visible desktop did not change.

## What was not measured

- No more than two private desktops existed at once beside the visible desktop. Desktop heap was
  not measured, and nothing ran short.
- The unit tier was not rerun alone on the visible desktop; its comparison is with the gate's
  baseline.
- The plugin's approval dialog calls `FlashWindowEx` on its owner, and the unit tier shows that
  dialog. The foreground and the pointer were watched, the taskbar was not, so whether a flash on a
  private desktop shows on the visible taskbar is not known.
- The end to end tier ran on a private desktop for two fixtures only.
