# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that answered whether a GitHub-hosted
Windows runner can drive a desktop GUI. It is taken from `master`, it is never merged, and nothing
here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, and that matters more here than elsewhere: the subject is the
machine rather than the product, so depending on the product would only make the answer harder to
trust and the branch harder to keep.

## Why it is its own branch

The end to end tier starts a real KeePass and types into it, and the claim being defended was that a
hosted runner cannot do that, so CI would need a self-hosted machine. That claim had been argued
rather than measured, and it was wrong. Measuring it needed a workflow that actually runs on a hosted
runner, which is not something a mainline branch can carry without running it on every push.

It is kept rather than retired for the reason every spike here is: it is a claim about somebody
else's machine, and a later runner image could make it false.

## The rule that matters

**Do not improve this code.** What the mainline records was produced by running exactly what is
here, so an edit that makes the probe nicer makes that record a claim about something that no longer
exists.

If it turns out to be wrong, that is worth having: run it again, and record the correction where the
answer is quoted rather than quietly changing the script.

If a **new** question needs measuring, it gets its own branch from `master`, named `spikes/<topic>`,
rather than a commit on a mainline branch.

## Working here

- The workflow runs on **push**, because `workflow_dispatch` is not offered for a branch whose
  workflow file has never run on the default branch.
- The probe is a script rather than a type, because what it measures is the machine:
  `.github/desktop-probe.ps1`.
- Read [`RESULTS.md`](RESULTS.md) before changing anything. It records what each part answered.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
