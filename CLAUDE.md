# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds the probe that measured how KeePass merges a
database's custom data, which is where KeePassGate keeps its certificate authority, its admission
records and its revocations, and what KeePassXC and KeePass 2.47 do to the same keys when they save
or merge the file. It is taken from `master`, it is never merged, and nothing here ships or is
built by any gate.

The plugin, its tests, its build script and its design documents are on the mainline branches. This
branch deliberately has none of them, so the probe can be read without the project around it and the
project can be read without the probe in it. The mainline keeps what was established: a measured
fact in its `CLAUDE.md`, and host tests that pin the rule against the KeePass it builds with.

## Why one probe asks four questions

They are one argument. `rules` reads the merge rule off a real KeePass one key at a time, the matrix
asks what that rule does to each way a revocation could be written, and `layout` asks the same of
the way the authority could be kept. Each later question is only worth asking because of what the
one before it answered, so they share a program and a branch.

The fourth asks the same of the programs other than KeePass 2.61.1 that open a paired file, which
is where the first three stop: what a KeePassXC save, a KeePassXC merge and a KeePass 2.47 save
leave of the keys the design rests on. The probe makes those databases and reads them back with the
same KeePassLib, and scripts beside it drive the other programs, because KeePassXC is a program
rather than a library and KeePass 2.47 cannot share a process with the 2.61.1 that reads.

It was asked of the Android programs that open KDBX 4 files as well, with scripts of their own
under `spikes/revocation/android`, because those programs run in an emulator under Linux and have
no command line: each is driven on its screen, and a writer and reader of their own, `Kpg.cs`,
makes the databases and reads them back with the same KeePassLib.

## The rule that matters

**Do not improve this account.** Every number recorded here and on the mainline was produced by
running exactly what is here, so an edit that makes the probe nicer makes those numbers a claim
about something that no longer exists.

If the probe turns out to be wrong, that is worth having: run it again, and record the correction in
`RESULTS.md` beside what it corrects rather than quietly changing the code.

**It stays worth re-running.** How KeePass merges custom data is a claim about somebody else's
program, and a later release could make it false. Recording the output as it came back is what makes
a re-run that disagrees a finding rather than a mistake.

If a **new** question needs measuring, it gets its own branch from `master`, named
`spikes/<topic>`, rather than a commit on a mainline branch.

## Working here

- One console program, `spikes/revocation`, built with the .NET SDK against KeePass itself, and
  the Windows PowerShell 5.1 scripts beside it that run KeePassXC and KeePass 2.47 against its
  databases. The Android programs have a directory of their own, `spikes/revocation/android`:
  shell scripts and a Python driver for the emulator, which run under Linux, and the PowerShell
  that makes their databases and reads them back.
- **KeePass is provided, not found.** `keepass.version` says which release, `keepass.sha256` what it
  must arrive as, and `tools/KeePass.ps1` fetches it into `.keepass\<version>\`, which is not
  committed. KeePass 2.47 arrives the same way, by version.
- **So is KeePassXC.** `keepassxc.sha256` pins each release by its archive, and
  `tools/KeePassXC.ps1` fetches a release into `.keepassxc\<version>\` after checking the hash and
  its publisher's signature.
- **So are the Android programs.** `android.sha256` pins each APK by its hash and by the digest of
  the certificate that signed it, and the Android SDK's command-line tools by their hash.
  `spikes/revocation/android/fetch.sh` fetches the APKs into a work directory that is not
  committed, and `verify-apks.sh` beside it refuses an APK signed by any other certificate.
- Every database it opens is one it created a moment before, under a work directory named on its
  command line, with an invented password.
- **Nobody else's source is here.** KeePass and KeePassXC are both GPL and this repository is MIT,
  so `RESULTS.md` cites their files by path and line in the named release instead of carrying them.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. Nothing on this branch needs anything more.
