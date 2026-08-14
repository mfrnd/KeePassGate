# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the proofs that answered which HTTP
implementation KeePassGate should carry. It is taken from `master`, it is never merged, and nothing
here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so a proof can be read without the project around it and the
project can be read without the proofs in it.

## The rule that matters

**Do not improve this code.** Every number recorded on the mainline was produced by running exactly
what is here, so an edit that makes a proof nicer makes the numbers a claim about something that no
longer exists. Fixing a typo in a comment is the sort of change that costs nothing and buys nothing.

If a proof turns out to be wrong, that is worth having: run it again, and record the correction
where the number is quoted rather than quietly changing the code. Two of the verdicts here were
reversed that way, and the reversals are the most useful thing on the branch.

If a **new** question needs a spike, it gets its own branch from `master`, named `spikes/<topic>`,
rather than a commit on a mainline branch. A proof that is added and then deleted again leaves the
deletion in the history of everything downstream, which is what this arrangement exists to avoid.

## Working here

- One directory per candidate, `spikes/<library>`, self contained, buildable with `dotnet build`.
- The vendored library goes under `vendor/` **as it arrives**, and every line changed is recorded
  beside it, because divergence is the measurement.
- The harness goes under `src/` and is ours.
- A proof mints its certificate authority in memory and serves on loopback, so running one arranges
  nothing and leaves nothing behind.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
