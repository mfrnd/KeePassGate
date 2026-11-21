# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**A measurement, and nothing else.** It holds the question of whether KeePassGate's test tiers can
run on a desktop created for them rather than on the visible desktop, the code that measured it,
and what it settled. It is taken from `master`, it is never merged, and nothing here ships or is
built by any gate.

The plugin, its tests and its build script are on the mainline branches. Nothing here depends on
them except at run time: `RESULTS.md` names the commit the tiers were run at.

## The rule that matters

**Do not improve this account.** Every number in `RESULTS.md` was produced by running exactly what
is here, so an edit that makes a script nicer makes those numbers a claim about something that no
longer exists.

If a measurement turns out to be wrong, that is worth having: run it again, and record the
correction where the number is quoted rather than quietly changing what is written.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`.

## Working here

- `launcher/` starts a command on a private desktop; `probe/` asks what works there; `runs/` holds
  what ran the tiers and read their results.
- `results/` holds the text the probes and the sweep wrote, kept as written.
- Pictures are not kept: KeePass shows the open database's path in some of them.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
