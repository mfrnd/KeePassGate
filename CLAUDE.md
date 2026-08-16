# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It answers one question: whether a `pykeepass` program can
be pointed at a KeePassGate database. It is taken from `master`, it is never merged, and nothing
here ships or is built by any gate.

The plugin, its client, its tests and its design documents are on the mainline branches. This branch
deliberately has none of them, so the question can be read without the project around it and the
project can be read without the working out in it.

## The rules that matter

**Measure the other library, do not describe it.** Every claim here about `pykeepass` is read off
the installed package, because a claim about somebody else's release is exactly the kind that rots.
A statement that cannot be measured does not belong in a results table; it belongs in prose that
says it is reasoning.

**Do not vendor it.** `pykeepass` is fetched at run time, never checked in, so this branch
redistributes nothing and owes no notices. If that ever changes, the notices come with it in the
same commit.

**Do not build a compatibility shim here.** This branch decides whether one is worth building. If
the answer becomes yes, the shim belongs in the client on a mainline branch, where it is tested and
shipped, not beside the analysis that recommended it.

If a **new** question needs a spike, it gets its own branch from `master`, named `spikes/<topic>`,
rather than a commit on a mainline branch. A proof that is added and then deleted again leaves the
deletion in the history of everything downstream, which is what this arrangement exists to avoid.

## Running what is here

`uv` with no project, so nothing is installed into anything:

```powershell
cd spikes/pykeepass
uv run --no-project --with pykeepass python surface.py
uv run --no-project --with pykeepass --with pytest python -m pytest test_pykeepass.py -q
```

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
