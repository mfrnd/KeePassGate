# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probes that answered what .NET Framework
actually does about TLS and X.509, which is what KeePassGate's transport is built on. It is taken
from `master`, it is never merged, and nothing here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so a probe can be read without the project around it and the
project can be read without the probes in it.

## Why these nine are together

They are one argument rather than nine questions. `TlsProbe` establishes what the platform gives a
plugin at all; `ChainProbe`, `SanProbe` and `OptionalClientCertProbe` find the three traps in it;
`SniProbe`, `HmacSniProbe` and `BindingProbe` build the transport that avoids them;
`EphemeralKeyProbe` finds the one blocker left, and `ManagedTlsProbe` is the answer to it. Reading
the last without the eighth is reading a conclusion with its premise on another branch.

The build environment is a different subject and is on `spikes/build-environment`.

## The rule that matters

**Do not improve this account.** Every number recorded on the mainline was produced by running exactly
what is here, so an edit that makes a probe nicer makes those numbers a claim about something that
no longer exists. Fixing a typo in a comment is the sort of change that costs nothing and buys
nothing.

If a probe turns out to be wrong, that is worth having: run it again, and record the correction
where the number is quoted rather than quietly changing the code. Two of the verdicts here were
reversed that way, and the reversals are the most useful thing on the branch.

**These stay worth re-running.** `X509Chain` not pinning an anchor is a claim about .NET rather
than about this project, and a later .NET could make it false. Recording the output as it came back
is what makes a re-run that disagrees a finding rather than a mistake.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- One file per question, `probes/<Name>Probe.cs`, compiled by `Add-Type` and run by hand.
- **Windows PowerShell, not PowerShell 7**, which is .NET Core and answers a different question.
- Every probe mints its certificates in memory and serves on loopback, so running one arranges
  nothing and leaves nothing behind.
- Eight of them reference nothing outside the framework. `ManagedTlsProbe` needs Bouncy Castle, and
  `tools/BouncyCastle.ps1` fetches the pinned version into a directory that is not committed.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
