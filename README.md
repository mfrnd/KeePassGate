# KeePassGate: can the test tiers run on a desktop nobody sees?

**A measurement, kept for reference and never merged.** Every KeePassGate test run that shows a
window shares the one visible desktop of the machine it runs on, so runs queue for it. Windows lets
a process start on a desktop of its own, created with `CreateDesktop` and named in
`STARTUPINFO.lpDesktop`, where its windows are seen by nobody and take no foreground from the
visible desktop. The question was which of the tiers can run there, in parallel, without disturbing
the visible desktop or each other.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. Where a run used the product's tests, `RESULTS.md` names the commit of `master` it ran
against and how to repeat the run.

## The short answer

**The unit tier and the host tier run on a private desktop as they are**: every test passed there,
alone, two runs at a time and beside a run on the visible desktop, and nothing reached the visible
desktop. **The end to end tier does not**: a private desktop has no foreground window, refuses
synthesized input, and has no screen to copy. `RESULTS.md` has the figures.

## What is here

| | |
| --- | --- |
| [`RESULTS.md`](RESULTS.md) | the question, the machine, every step with its figures, and the answer |
| `launcher/` | `private-desktop.ps1` and `PrivateDesktop.cs`, which start a command under a basic user token on a desktop of its own, and `basic-user.ps1`, which lowers the token |
| `probe/` | `desktop-probe.ps1` with `DesktopProbe.cs`, what a process can do on a private desktop |
| `runs/` | the scripts that ran the tiers: a tier on a private desktop, two at once, the watcher of the visible desktop, and the readers of the result files |
| `results/` | the text the desktop probe wrote |

## Running it

Everything is Windows PowerShell 5.1 and .NET Framework, on a Windows machine with a desktop
session, started from an elevated process (the launcher lowers the token itself). The tiers need a
KeePassGate worktree at the named commit, built in Debug, with its pinned KeePass provided.

```powershell
.\launcher\private-desktop.ps1 -Dir C:\work -Log C:\work\run.log -Command "Get-Date"
.\probe\desktop-probe.ps1 -Out C:\work\probe                # on the desktop it is started on
.\runs\run-tier.ps1 -Tier unit -Worktree C:\src\KeePassGate -Out C:\work\unit
```
