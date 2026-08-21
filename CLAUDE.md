# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that answered whether KeePassGate's
gate can fail while nothing is actually wrong. It is taken from `master`, it is never merged, and
nothing here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, which matters more here than elsewhere: the subject is the
machine and the shell rather than the product, and depending on the product would make the answer
harder to trust.

The TLS and certificate measurements are a different subject and are on `spikes/tls-and-certificates`.

## Why it is its own branch

`build.ps1` is the definition of clean for this project, and a gate that fails while nothing is
wrong teaches whoever runs it to disbelieve the result. Both failures found here do exactly that,
and neither is visible from reading the script: one is a warning becoming an error because
something upstream merged the streams, and the other is a cmdlet that is not missing so much as
never was, because another runtime got to `PSModulePath` first.

Both are now fixed at the source on the mainline. This is kept anyway, because what it measures is
somebody else's behavior rather than this repository's: a later PowerShell could make either of
these false, and the fixes on the mainline would then be guarding against nothing.

## The rule that matters

**Do not improve this code.** Every number recorded on the mainline was produced by running exactly
what is here, so an edit that makes the probe nicer makes those numbers a claim about something
that no longer exists.

If it turns out to be wrong, that is worth having: run it again, and record the correction where
the number is quoted rather than quietly changing the code.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- **Windows PowerShell, not PowerShell 7**, which is .NET Core and answers a different question.
- The probe is a script rather than a type, because what it measures is PowerShell:
  `powershell -NoProfile -File spikes\BuildStepProbe.ps1`.
- It borrows the module path from `pwsh` itself rather than reading it off the current process. A
  Windows PowerShell started from a shortcut has a perfectly good one and would answer that nothing
  is wrong.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
