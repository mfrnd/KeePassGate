# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that measured what a version of a group
or an entry could be made of in KeePass, which is what KeePassGate's `ETag` had to be built from. It
is taken from `master`, it is never merged, and nothing here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so the probe can be read without the project around it and the
project can be read without the probe in it. The mainline keeps what was established: a measured
fact in its `CLAUDE.md`, a section of `FINDINGS.md`, and host tests that drive the cases that
mattered against the KeePass it builds with.

## Why one probe asks four questions

They are one argument. `times` asks whether KeePass's modification time moves with every change
that matters, and finds four ways it does not. `survey` asks whether KeePass's own windows at least
always stamp it, which decides whether the misses are KeePass's or only a plugin's and a merge's.
Once the answer is a digest of the content instead, `binary` asks whether an attachment's digest can
be kept per object, and `cost` asks what the digest costs. Each is only worth asking because of the
one before it, so they share a program and a branch.

## The rule that matters

**Do not improve this code.** Every number recorded here and on the mainline was produced by running
exactly what is here, so an edit that makes the probe nicer makes those numbers a claim about
something that no longer exists.

If the probe turns out to be wrong, that is worth having: run it again, and record the correction in
`RESULTS.md` beside what it corrects rather than quietly changing the code.

**It stays worth re-running.** How KeePass stamps and merges its times, and whether an attachment can
change in place, are claims about somebody else's program, and a later release could make them false.
Recording the output as it came back is what makes a re-run that disagrees a finding rather than a
mistake.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- One console program, `spikes/tags`, built with the .NET SDK against KeePass itself.
- **KeePass is provided, not found.** `keepass.version` says which release, `keepass.sha256` what it
  must arrive as, and `tools/KeePass.ps1` fetches it into `.keepass\<version>\`, which is not
  committed.
- Every database it makes is made in memory a moment before, with an invented password, and never
  written to disk.
- **Nobody else's source is here.** KeePass is GPL and this repository is MIT, so `RESULTS.md` names
  KeePass's types and methods instead of carrying them.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
