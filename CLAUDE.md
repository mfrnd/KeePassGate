# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that measured why an application
domain that has driven KeePass through UI automation cannot be unloaded, which is what made every
end to end run of KeePassGate that read a dialog end with an engine error. It is taken from
`master`, it is never merged, and nothing here ships or is built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so the probe can be read without the project around it and the
project can be read without the probe in it. The mainline keeps what was established: a row of
`FINDINGS.md` under "Driving KeePass through the desktop", the `ClientSideProviderQueue`
class that ends the thread from the `Live` namespace's set up fixture, and a host test that pins
the private names it reaches.

## Why one probe asks seven questions

They are one argument. `menu` repeats what the desktop tests do to a menu and finds the unload
failing. `minimal` narrows that to one call, a `FindAll` over a window's descendants, and shows that
registering no client-side providers changes nothing; `tree` shows the tree is the same either way.
`members`, `items` and `il` read the thread that call starts off the framework and find no way to
stop it, and `stop` ends it through the queue's own way in and finds the unload succeeding. Each is
only worth asking because of the one before it, so they share a program and a branch.

## The rule that matters

**Do not improve this code.** Every number recorded here and on the mainline was produced by running
exactly what is here, so an edit that makes the probe nicer makes those numbers a claim about
something that no longer exists.

If the probe turns out to be wrong, that is worth having: run it again, and record the correction in
`RESULTS.md` beside what it corrects rather than quietly changing the code.

**It stays worth re-running.** What UI automation's client-side providers leave in a domain, and
whether their queue can be ended from outside, are claims about somebody else's code, and an update
to .NET Framework could make them false. Recording the output as it came back is what makes a
re-run that disagrees a finding rather than a mistake.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- One console program, `spikes/unload`, built with the .NET SDK against the framework's UI
  automation client, and two scripts beside it: `run.ps1` asks every question against a throwaway
  KeePass, and `each-fixture.ps1` runs a KeePassGate checkout's `Live` fixtures one at a time.
- **KeePass is provided, not found.** `keepass.version` says which release, `keepass.sha256` what it
  must arrive as, and `tools/KeePass.ps1` fetches it into `.keepass\<version>\`, which is not
  committed. The probe does not compile against it; `run.ps1` starts a copy of it with no database.
- **Nobody else's source is here.** The framework's providers are Microsoft's, so `RESULTS.md` names
  their types and methods, and `il.txt` holds the IL the probe printed, instead of carrying source.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
