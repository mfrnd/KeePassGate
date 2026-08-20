# Can a hosted runner drive a Windows GUI?

Yes. Every part of it, on `windows-latest`, with no self-hosted runner and no container.

This branch exists because the answer had been argued rather than measured, and the argument was
going the wrong way: the claim being defended was that hosted runners cannot drive a desktop and
that the end to end tier would therefore need a self-hosted runner. That claim was wrong.

## What this spike is

One script and one workflow, and nothing else. It branches from the commit that writes down the
rules holding on every branch, so it carries the conventions and the prose checks and **not** the
plugin, the build script or the test projects. The subject is the machine, not the product, so
depending on the product would only make the answer harder to trust and the branch harder to keep.

`.github/desktop-probe.ps1` puts a window up **in another process** and then crosses the process
boundary in every place the real driver does: finds the window through the accessibility tree,
takes the foreground through the input queue, types at it with synthesized keystrokes, clicks it
with a synthesized pointer, opens a menu with Alt, and finds the window and the button
through the tree. What arrived is read out of a file the target writes, rather than back through the tree.
Windows PowerShell, WinForms and UI Automation, all in the box.

Run it with `.\.github\desktop-probe.ps1` on any machine, or push the branch, which is how the
workflow runs it. Its exit code
is the verdict, so a red job means a machine that cannot drive a GUI rather than a job that failed
to run one.

## What a hosted runner answered

`windows-latest` was `win25-vs2026` image `20260818.207.1`, Windows NT 10.0.26100. Run
[32422918510](https://github.com/mfrnd/KeePassGate/actions/runs/32422918510). The local column is
the same script on a developer machine, for comparison.

| | local | `windows-latest` |
| --- | --- | --- |
| `session.id`, interactive | 1, True | 2, True |
| `station.name`, `desktop.name` | `WinSta0`, `Default` | `WinSta0`, `Default` |
| `screen.bounds`, `screen.count` | 1920x1200, two | **1024x768, one** |
| `uia.windowFound`, `uia.otherProcess` | True, True | True, True |
| `window.visible`, `window.foreground` | True, True | True, True |
| `keyboard.readBack` | `'kpg'` | `'kpg'` |
| `uia.controlFound` | True | True |
| `mouse.readBack` | `'kpg!'` | `'kpg!'` |
| `capture.notBlank` | True | True |
| `verdict` | a GUI can be driven | a GUI can be driven |

Every row that has to work does, and the only difference is the screen: **1024x768 and a single
monitor**. Nothing in the end to end tier needs more, and the measurement below says so rather than
assuming it.

`menu.items` reads zero on both machines and is reported rather than required, because that is
WinForms describing its own strip to UI Automation and not the machine answering anything. That
menus can be driven is settled below, by a tier that drives KeePass's own.

## What the product itself answered

Measured separately, because this branch deliberately cannot build the plugin. A staged workflow on
a branch cut from the tip of the stack ran the real thing on `windows-latest`:

| stage | result | wall clock |
| --- | --- | --- |
| the desktop probe | passed | 21s |
| provisioning KeePass and building against it | passed | 75s |
| one end to end fixture | 10 passed, 0 skipped | 95s |
| the whole end to end tier | **47 passed, 0 failed, 0 skipped** | 158s |

Forty-seven is the same count the tier passes locally, so nothing was quietly skipped. Sixty-five
seconds of that was test time. The whole run cost about thirteen runner minutes.

Provisioning is included on purpose: it fetches the pinned portable KeePass, checks it against the
pinned hash, and then checks that `KeePass.exe` carries a valid code-signing signature from
KeePass's author. All three pass on a runner, which is the half of the toolchain most likely to be
where a hosted machine differs.

## What this settles

- **No self-hosted runner.** A hosted `windows-latest` runner has a real interactive window
  station, and everything the driver relies on works on it.
- **No container.** Linux containers are the wrong operating system for UI Automation, `keybd_event`
  and WinForms on .NET Framework; Windows containers have no interactive desktop, and GitHub does
  not support container jobs on Windows runners at all. Neither was needed.
- **1024x768 is enough**, including the one place the tier moves a real pointer.
- **The screenshot fixtures stay a local task regardless**, and that is unchanged by any of this.
  They write into the working tree and their output is committed bytes that the documentation is
  written around, so regenerating them on a different desktop would rewrite them for no reason.

## What it does not settle

How stable the result is. This is one run, not a hundred. The tier synthesizes input and waits on
windows, so the failure mode to watch for is a fixture that passes here and times out under a slower
or busier runner. That is a question for the first weeks of a real workflow, not for a spike.

## Two traps worth keeping

**`notepad.exe` is not a usable automation target on Windows 11.** It is a stub that hands off to a
Store process, so the id it returns owns no window. A first version of this probe used it and
reported the accessibility tree missing on a machine whose tree was fine. The target is now a window
the probe puts up itself.

**A probe must not send synthesized input it cannot aim.** Input goes wherever the focus is, so a
probe that failed to take the foreground and typed anyway types into somebody else's window. This
one sends nothing unless the foreground was taken, which is also the better measurement: it tells a
machine that refuses the foreground apart from one that swallows keys.
