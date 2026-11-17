# CLAUDE.md

Guidance for anyone, human or AI assistant, working on this branch.

## What this branch is

**Reference material, and nothing else.** It holds every measurement of where KeePassGate can keep
its data inside a `.kdbx` file so that the data comes through the file being saved, edited,
synchronized and merged by every program listed as compatible, and the evaluation built on them.
It is taken from the commit that sets down the rules every branch follows, it is never merged, and
nothing here ships or is built by any gate. [`README.md`](README.md) maps what is where.

**It is the ground truth for any later discussion of the store.** A claim about what a program does
to a file is settled here, by a probe and its record, or it is marked as not measured. The plugin,
its tests and its design documents are on the mainline, and keep what was decided, not the working
out.

## The documents and who answers for what

- **[`RESULTS.md`](RESULTS.md) states what was measured**, once, with the family and the `RUN.md`
  where every figure runs again. It holds no recommendation and no ranking, and it records a
  reading that was measured and not taken as plainly as a reading that was.
- **[`EVALUATION.md`](EVALUATION.md) states what follows for the design.** Its section 2 holds the
  decisions as the project took them, and its section 8 what is deferred. The branch records
  decisions; it does not make them, and nothing here reopens a decision without a measurement
  that changes its premise.
- **The papers under [`papers/`](papers/README.md) are kept as they were written.** A paper is never
  corrected in place: `RESULTS.md` section 6 holds every sentence a record does not bear out, with
  the value that holds. Where a paper and `RESULTS.md` differ, `RESULTS.md` holds.
- **The recorded output is the measurement.** A record under `recorded\` is never edited by hand,
  but for the replacements of machine paths, user names, session identifiers and invented passwords
  that its `RUN.md` names and counts.

## Adding a measurement

In this order, and nothing is skipped:

1. **A probe family** under `spikes\storage\probes\`, following the contract in
   [`spikes/storage/probes/README.md`](spikes/storage/probes/README.md): a `RUN.md` with the
   command that runs it from the branch root, its fresh output in `out\` and its record in
   `recorded\`, and a table of every figure with its recorded value and a second run's. A family
   run again in a configuration its first record did not have keeps a record for each such
   configuration beside the first, `recorded-<configuration>\`, selected by a switch its `RUN.md`
   names in a section of its own; with the switch off, the family still prints its earlier records
   line for line.
2. **The report**, carried into `papers\ground\` by `tools\Copy-Papers.ps1`, which replaces the
   machine's paths and counts each replacement, and listed in `papers/README.md` with its
   reproduction.
3. **A section of `RESULTS.md`**, in the manner of the others: what was run, the counts, every
   sequence that ends wider named as that, what was cut and what was not measured, and the place it
   runs again in section 5. A reading that was measured and not taken into the design is recorded
   all the same, and its section states so where it opens, pointing to `EVALUATION.md` section 2. A
   correction a new measurement makes to a paper, or to an earlier report, goes into section 6.
4. **Only then a sentence in `EVALUATION.md`**, citing the report and its section.

A claim with no measurement behind it is marked so: **read** from a program's source, **model**
from an executable model, **reasoning** from measured facts, or **not measured**.

## How compatibility is worded

**A program is compatible when measurement shows all three**: its save keeps every place in the
file the store uses; it merges, or asks, before it writes over a file that changed underneath it;
and its merge keeps what KeePass's own Synchronize keeps, applied per place the store relies on. A
program is named with what was measured of it, the release, and the conditions under which it was
measured, never as anybody's preference, choice or habit, and never as "ruled out": a program that
fails a criterion is incompatible as measured. "The operator" means the person running the plugin,
approving a pairing or answering a dialog, and nothing else.

## What a run needs

- **Most families**: a console, the .NET SDK, `uv`, PowerShell 7 for the scripts whose `RUN.md`
  names it, and what `tools\` fetches. `tools\KeePass.ps1`
  and `tools\KeePassXC.ps1` provide each pinned release into `.keepass\<version>\` and
  `.keepassxc\<version>\` and refuse a release with no pin; `tools\Product.ps1` exports the files of
  the product a probe compiles into `.product\`.
- **A window**: `desktop-windows` and `desktop-hide-and-lock` drive KeePass's and KeePassXC's own
  windows and need the desktop to themselves while they run.
- **A device**: `spikes\storage\android` runs the phone programs on an emulator under WSL 2, with
  the APKs `android.sha256` pins and, for the synchronization measurements, a server.
- **Further downloads**, each held to a hash, for `kdbx-places` and `source-harness`.
- **Runs go under an ordinary user token**, never an elevated token, as KeePass runs for the
  person using it.

## What is never committed

- **No `.kdbx`**, in any directory, for any reason. A probe makes its databases every time it runs,
  under `work\` or the system's temporary directory, and records a dump of a database, never the
  file.
- **No fetched binary.** Every program arrives through a script, pinned by the hash of the file it
  arrives as, in `keepass.sha256`, `keepassxc.sha256` or `android.sha256`; measuring another release
  adds a line there.
- **Nothing that names a machine, a user or a person's preference for a program.** A machine path
  is written as its place on the branch or as a placeholder such as `<work>`.
- **No source of another project.** KeePass, KeePassXC and the Android programs are cited by file
  and line at the release named, and fetched into directories that are not committed.

## Conventions

**In [`CONVENTIONS.md`](CONVENTIONS.md)**, which every branch inherits: secrecy, branch layout,
prose and commits. This branch adds one rule of its own: a term it coins, such as the name of a
part of the design or the label of a rule, is explained in plain words where a reader first meets
it in each document, before it is used.
