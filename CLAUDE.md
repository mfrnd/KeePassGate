# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the question of how to screenshot one window on
Windows 11 so that the file contains the window and nothing behind it, and the working out that
answered it. It is taken from `master`, it is never merged, and nothing here ships or is built by
any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. What
the mainline keeps is the method that was chosen and the reason; the alternatives, the measurements
that ruled them out and the survey of everything else available are here.

## The question

KeePassGate's documentation is written around screenshots a fixture takes by driving a real KeePass.
Those files are committed and published, so what is in them matters twice: a picture holding part of
somebody's desktop publishes it, and a picture that changes on every regeneration makes its own diff
useless for seeing what changed.

Windows 11 rounds the corners of a top-level window when it composes the desktop. A screen copy over
the window's rectangle therefore holds the desktop in those corners, whatever is there.

## The rule that matters

**Do not improve this account.** Every number recorded on the mainline was produced by running exactly
what is described here, so an edit that makes a measurement nicer makes those numbers a claim about
something that no longer exists.

If a measurement turns out to be wrong, that is worth having: run it again, and record the
correction where the number is quoted rather than quietly changing what is written.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- `capture-methods.md` is a survey of every way Windows offers to do this, gathered rather than
  measured. It is kept as it arrived.
- `RESULTS.md` is what was measured on the real dialogs, and what was decided.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
