# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that measured how KeePass merges a
database's custom data, which is where KeePassGate keeps its certificate authority, its admission
records and its revocations. It is taken from `master`, it is never merged, and nothing here ships
or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so the probe can be read without the project around it and the
project can be read without the probe in it. The mainline keeps what was established: a measured
fact in its `CLAUDE.md`, and host tests that pin the rule against the KeePass it builds with.

## Why one probe asks three questions

They are one argument. `rules` reads the merge rule off a real KeePass one key at a time, the matrix
asks what that rule does to each way a revocation could be written, and `layout` asks the same of
the way the authority could be kept. Each later question is only worth asking because of what the
one before it answered, so they share a program and a branch.

## The rule that matters

**Do not improve this account.** Every number recorded here and on the mainline was produced by
running exactly what is here, so an edit that makes the probe nicer makes those numbers a claim
about something that no longer exists.

If the probe turns out to be wrong, that is worth having: run it again, and record the correction in
`RESULTS.md` beside what it corrects rather than quietly changing the code.

**It stays worth re-running.** How KeePass merges custom data is a claim about somebody else's
program, and a later release could make it false. Recording the output as it came back is what makes
a re-run that disagrees a finding rather than a mistake.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- One console program, `spikes/revocation`, built with the .NET SDK against KeePass itself.
- **KeePass is provided, not found.** `keepass.version` says which release, `keepass.sha256` what it
  must arrive as, and `tools/KeePass.ps1` fetches it into `.keepass\<version>\`, which is not
  committed.
- Every database it opens is one it created a moment before, under a work directory named on its
  command line, with an invented password.
- **Nobody else's source is here.** KeePass and KeePassXC are both GPL and this repository is MIT,
  so `RESULTS.md` cites their files by path and line in the named release instead of carrying them.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
