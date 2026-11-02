# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probes that ask what a read on a connection
thread meets while KeePass's own thread changes the tree, what holding that thread still costs, and
how to take a turn on it without starving its window. It is taken from `master`, it is never merged,
and nothing here ships or is built by any gate. `README.md` holds what is asked and how to run it,
and `RESULTS.md` what was measured.

The plugin is on the mainline, and this branch carries none of it. The probes repeat what the plugin
does without depending on it, so that their answers are about KeePass, WinForms and Windows. The
one measurement made against the plugin is cited as such.

## Rules that matter here

**Do not improve this code.** What `RESULTS.md` records was produced by running exactly what is here,
so an edit that makes a probe nicer makes that record a claim about something that no longer exists.
A new question gets a new measurement beside the old one.

**Never point a probe at a real database.** None of them opens one: every tree they read is made in
memory with KeePassLib.
