# What is in here

`settings.json` wires one hook, and it is checked in because it is part of the gate rather than
somebody's preference. `settings.local.json` is not: it is per machine and gitignored.

## The hook

After every `Write` or `Edit`, `hooks/check-file.ps1` runs the repository's own prose checks against
the one file that changed, and refuses it if they fail.

It is deliberately not a second opinion. The rules live in `.markdownlint-cli2.yaml` and
`cspell.yaml`, the same two files `build.ps1 -Task Lint` reads, and it spells only the files
`cspell.yaml` includes, so a file that passes the hook passes the gate, and a file the gate never
reads is not read here either. What the hook buys is the distance between the two: a misspelling
found at the edit costs a sentence, and the same misspelling found at the gate costs a rebuild and
a rerun.

It stays quiet when `node_modules` is missing, because a fresh clone should be editable before it is
installable. The gate is what refuses to let an unchecked file reach `master`, since a merge waits
for it.

It is started by its path under the project's root, `${CLAUDE_PROJECT_DIR}`, because a path
relative to wherever the session stands names nothing once the session has changed directory, and
every edit after that would go unchecked.

## What is deliberately not hooked

**The C# build.** Compiling on every edit would make every edit take seconds, and the compiler
is not the fast half of anything. `build.ps1` is the gate; the hook is the fast half of it.
