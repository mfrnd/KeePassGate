# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probes that ask what KeePassGate would meet in
KeePass on Mono, and the tools that confirm the answers against the plugin. It is taken from `master`,
it is never merged, and nothing here ships or is built by any gate. `README.md` holds what is asked and
how to run it, and `RESULTS.md` what was measured.

The plugin is on the mainline, and this branch carries none of it. The probes repeat what the plugin
does without depending on it, so that their answers are about the runtime. What is about the plugin
itself is measured against a build of a named `master` commit and cited by that commit.

## Rules that matter here

**Do not improve this code.** What `RESULTS.md` records was produced by running exactly what is here,
so an edit that makes a probe nicer makes that record a claim about something that no longer exists.
A new question gets a new measurement beside the old one.

**Nothing Mono draws may reach the Windows desktop**, which is shared. WSLg stays off in
`%USERPROFILE%\.wslconfig`, and every probe, test and KeePass run gets Xvfb.

**The database is a throwaway** with the password `dummy`, made by `product/MakeDatabase` under
`/tmp`. Never point any of this at a real one, and never commit a `.kdbx`.
