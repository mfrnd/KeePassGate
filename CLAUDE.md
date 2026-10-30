# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that measured which comments CodeQL
takes for a suppression and what GitHub's code scanning does with a finding one suppresses, which is
what KeePassGate's answer to a CodeQL finding is built on. It is taken from `master`, it is never
merged, and nothing here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so the probe can be read without the project around it and the
project can be read without the probe in it. The mainline keeps what was established, from step 32
on: a measured fact in its `CLAUDE.md`, a code scanning workflow that cites this branch, and vectors
that hold its reading of a suppression comment to the CodeQL it pins.

## Why one probe asks eight questions

They are one argument. Which comments suppress decides what a suppression can be written as. That
code scanning makes an alert of a suppressed result anyway is what makes anything more necessary,
and how an alert's fingerprint moves decides that a dismissal is made again on every run.
`advanced-security/dismiss-alerts` was measured as the answer to that and found unfit, which is why
the filter was built, and a filter applied everywhere leaves the default branch with no record of
what the code suppresses, which is what dismissing on `main` was measured to give back. Each of
those six is only worth asking because of the one before it. The review job and the choice of
CodeQL version are what the design they arrive at needed besides, so all eight share a repository
and a branch.

## The rule that matters

**Do not improve this code.** What is recorded here and on the mainline was produced by running these
files, at the points of the probe's history that `RESULTS.md` names, so an edit that makes the probe
nicer makes those results a claim about something that never ran.

If the probe turns out to be wrong, that is worth having: run it again, and record the correction in
`RESULTS.md` beside what it corrects rather than quietly changing the code.

**It stays worth re-running.** What CodeQL takes for a suppression and what code scanning does with
one are claims about somebody else's code and somebody else's service, and a new CodeQL or a change
on GitHub's side could make them false with no commit anywhere. Recording what came back is what
makes a re-run that disagrees a finding rather than a mistake.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- **The probe is a repository, not a program.** `spikes/suppressions` is the tree of a throwaway
  public repository as its last pull request left it, and `spikes/suppressions/ruleset.json` is the
  ruleset its default branch carried. [`README.md`](README.md) describes how to make another.
- **Its workflows do not run here.** They are under `spikes/suppressions/.github`, where GitHub reads
  none of them, and at the root they would run as this repository's own.
- **Its history is not here**, only the tree it ended with, so `RESULTS.md` lists each pull request
  with what it changed. The comment above each case describes it as first written, and a later pull
  request may have changed the case since.
- **Nobody else's source is here.** CodeQL's query library and `github/codeql-action` are GitHub's,
  so `RESULTS.md` cites each by path at the release it names instead of carrying it.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
