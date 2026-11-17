# KeePassGate: where the plugin's data can live in a `.kdbx` file

**Measurements and the evaluation built on them, kept for reference and never merged.** A
KeePassGate database is a single file that several programs write. The plugin keeps its certificate
authority, its admission records, its revocations, its profile names and its access rules inside
that file, so whatever it keeps has to come through the file being saved, edited, synchronized and
merged by every program listed as compatible. This branch answers where in the file that can be.

**A program is compatible when measurement shows all three**, and never because anybody prefers it:

1. its save keeps every place in the file the store uses;
2. it merges, or asks, before it writes over a file that changed underneath it;
3. its merge keeps what KeePass's own Synchronize keeps, applied per place the store relies on.

The programs were run, not read about: KeePass 2.62 through KeePassLib and in its own window,
KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0 through `keepassxc-cli` and 2.8.0 in its window,
Keepass2Android, KeePassDroid and KeePassDX on an Android emulator, Keepass2Android and KeePassDX
also from their own source compiled for a desktop runtime, and kdbxweb, pykeepass and KeePass 2.47
for what they do to a file.

## What is on the branch

| Where | What |
| --- | --- |
| [`RESULTS.md`](RESULTS.md) | what was measured, once, with the place every figure runs again, readings of the store that were measured and not taken included, and every sentence of a paper that the output does not bear out |
| [`EVALUATION.md`](EVALUATION.md) | what follows for the design: the store as the project decided it, how each program fares against the three criteria, every sequence that still ends wider or refusing, and what is deferred |
| [`papers/`](papers/README.md) | the working papers as they were written: the ground reports, each option's design with its critiques and its digest, the first draft of the evaluation with its check, and the reports of the measurements made after that draft |
| [`spikes/storage/probes/`](spikes/storage/probes/README.md) | a family of probes per paper, each with its `RUN.md` and its recorded output under `recorded\`, and a record of its own, `recorded-<configuration>\`, for each further configuration a family was run in: `store-as-a-whole` holds the store as composed, the store as decided, an edited record written again, and the readings of a copy |
| `spikes\storage\model\` | the executable models of the option `crdt`, whose store the decided design builds on, with their record |
| `spikes\storage\android\` | the emulator measurements of the Android programs: scripts, flows, the probe that makes and reads their databases, and the evidence under `recorded\` |
| `tools\` | the scripts that provide KeePass, KeePassXC and the product's files, and the script that copied the papers onto the branch |
| `keepass.version`, `keepass.sha256`, `keepassxc.sha256`, `android.sha256` | the pins: each program the probes ran, by the hash of the file it arrives as |

## Running something again

Every family's `RUN.md` gives the command that runs it from the branch root, how long it ran, and
the recorded value of every figure beside what a later run gave. Nothing that is somebody else's
is committed: `tools\KeePass.ps1` and `tools\KeePassXC.ps1` fetch the pinned releases into
`.keepass\<version>\` and `.keepassxc\<version>\`, check each archive against its pin and each
program's Authenticode signature, and refuse a release with no pin; `tools\Product.ps1` exports the
few files of the product that a probe compiles, at the commit the evaluation was made against,
into `.product\`. A probe makes its databases every time it runs and keeps none.

**A checkout needs room for long paths.** The longest path on the branch has 161 characters, so a
clone into a deep directory on Windows fails part way through its checkout and leaves most files
missing: clone into a short path, or with `git -c core.longpaths=true clone`.

```powershell
. .\tools\KeePass.ps1
Install-KeePass
. .\tools\KeePassXC.ps1
foreach ($v in '2.4.2', '2.6.6', '2.7.12', '2.8.0') { Install-KeePassXC -Version $v }
```

Most families then need nothing but those downloads and a console. Two need **a window**,
`desktop-windows` and `desktop-hide-and-lock`, which drive KeePass's and KeePassXC's own windows and
need the desktop to themselves. The Android measurements need **a device**: an emulator under
WSL 2, the APKs `android.sha256` pins, and for the synchronization measurements a server. A few
need **further downloads**, each held to a hash: `kdbx-places` takes kdbxweb from npm and Python
packages through `uv`, and `source-harness` the programs' sources at their tags, a JDK and the
Kotlin compiler. `RESULTS.md` section 5 is the full account: what each family needs, what was run
again and how, and what differed.

## What the branch is not

- **Never merged.** It is taken from the commit that sets down the rules every branch follows, and
  nothing on it ships or is built by any gate.
- **No product code.** The plugin, its client, its tests and its design documents are on the
  mainline. A probe that compiles part of the product gets that part from the history at the commit
  `tools\Product.ps1` names, into a directory that is not committed.
- **No `.kdbx`**, in any directory, and no fetched program.
- **Not where the design changes.** `EVALUATION.md` holds the design as decided; the product's
  design documents record it on the mainline when the store is built, and change there, not here.

## Status

Every measurement the evaluation rests on is on the branch with its record, and `RESULTS.md`
states each with the place it runs again, the readings that were measured and not taken among
them. `EVALUATION.md` holds the design as the project decided it, the sequences that still end
wider or refusing, and what is deferred. Building the store in the plugin is work for the
mainline.
