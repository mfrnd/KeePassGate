# source-harness

Keepass2Android 1.15-r6 and KeePassDX 4.5.8, run from their own source on Windows without an
emulator, so that what each program's database code does to a KDBX file on save and on merge can be
measured in bulk. Each harness was first held to the cells the emulator measurements recorded, then
used for three measurements: every place of a KDBX file, the recommended store's records under
thousands of histories, and the format version and key derivation each program writes. The report
is `papers\ground\measured-source-harness.md`, cited below as "the report", with its section
numbers.

## 1. What a harness measures, and what it cannot

**A harness is a filter, not a proof.** It runs the program's own reader, writer and merge, driven
the way the app's surrounding code drives them, as read from that code. When the reader drops a
place or the writer never writes it, no run of the app keeps it, and that exclusion is reliable.
When the code keeps a place, that is necessary and not sufficient: the app may still lose it on a
path the harness does not take, and the emulator confirms it.

**What no harness here shows**: when the app reads, writes or asks. Buttons, dialogs, settings,
caching, background work, a write racing a screen, and how Android's storage reports a changed file
are the app's and the device's. A harness script states the answer a person gave and the order of
the steps, as the emulator flows did, and takes both as given.

## 2. The layout

| Path | What it is |
| --- | --- |
| `fetch.ps1` | clones both programs at their tags into `work\src\`, checks each commit, provides the stand-in Argon2 library, runs `kpdx\build.ps1`, and builds `dumper\`, `k2a\` and `kplib\` |
| `run.ps1` | every step of section 3, into `out\` |
| `dumper\Dumper.csproj` | the emulator measurement's own probe, `spikes\storage\android\probe\overwrite\Store.cs`, compiled from its place and not copied: it makes the bases and writes every dump compared here |
| `k2a\` | the Keepass2Android harness: `K2aHarness.csproj`, `StandIns.cs`, `Driver.cs` (section 5) |
| `kpdx\` | the KeePassDX harness: `build.ps1`, `Driver.kt`, `standin\` (section 6) |
| `kplib\` | `StoreLab`, over KeePassLib 2.62: the store's histories, KeePassLib's own Synchronize of them, their analysis, and the format inputs |
| `dumper2\Dumper2.csproj` | the KeePassDX emulator measurement's own probe, built from a frozen copy of its working directory, for the second KeePassDX record (section 7.2) |
| `common.py`, `calibrate.py`, `cells_android.py`, `cells_kpdx.py`, `cells_kpdx2.py`, `repeat.py` | the calibration (section 7) |
| `batch.py`, `store_table.py` | a harness over a jobs file, the KeePassDX one in several JVMs; measurement 2's table |
| `places.py`, `formats.py` | measurements 1 and 3 (sections 8 and 10); `kplib\StoreLab.cs` is measurement 2 (section 9) |
| `recorded\` | the output the report quotes (section 12) |

`work\`, `out\`, `bin\` and `obj\` are ignored by the branch. Every database a run makes is in
`work\`; `out\` holds text only.

## 3. The commands

Windows PowerShell 5.1, from the branch root. `fetch.ps1` once, then `run.ps1`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\source-harness\fetch.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\source-harness\run.ps1
```

`run.ps1 -Steps` takes a comma separated subset of `calibrate`, `places`, `store`, `long` and
`formats`;
`-Seed`, `-Two` and `-Three` set measurement 2's histories (20261116, 2000 and 1000 here). What
each step runs, each also runnable alone:

| Step | Command, from the branch root | Writes |
| --- | --- | --- |
| calibrate | the dumper's `make`, `makeow`, `makesync` and `newsync` into `work\base\`, then `uv run --no-project python -I -X utf8 spikes\storage\probes\source-harness\calibrate.py k2a kpdx` | `out\calibrate-k2a.txt`, `out\calibrate-kpdx.txt` |
| places | the kdbx-places family's `android\make-bases.py` into `work\places\bases\`, then `places.py k2a kpdx` under `uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8` | `out\places-table.md`, `out\places-<program>.txt` |
| store | `kplib\bin\Release\net48\StoreLab.exe gen work\store 20261116 2000 1000`, both harnesses over `jobs-k2a.txt` and `jobs-kpdx.txt`, `StoreLab.exe ref work\store`, `StoreLab.exe analyze work\store kplib k2a kpdx` | `out\store-<program>-summary.txt`, `out\store-<program>-findings.txt` |
| long | `StoreLab.exe long work\long`, both harnesses, `ref` and `analyze` as for the store | `out\long-<program>-summary.txt`, `out\long-<program>-findings.txt` |
| formats | `formats.py k2a kpdx` | `out\formats.txt` |

The second KeePassDX record of section 7.2 is read from `work\kpdx-record2\`, or from the directory
the environment variable `KPDX_RECORD2` names.

What a run needs: Windows PowerShell 5.1, the .NET SDK (10.0.401 here), `uv`, Git, KeePass 2.62
as `tools\KeePass.ps1` provides it, and what `fetch.ps1` downloads. Nothing shows a window: the
harnesses and probes are console programs, and the JVMs run without a window.

## 4. What is downloaded, and its hashes

Every download is checked against the SHA-256 below before use, and a mismatch stops the script.

| What | Version | From | SHA-256 |
| --- | --- | --- | --- |
| Keepass2Android source | tag `v1.15-r6` | `https://github.com/PhilippC/keepass2android` | commit `bbcf3787c75bd5b8a9769576191129b5dd9e8bc0` |
| KeePassDX source | tag `4.5.8` | `https://github.com/Kunzisoft/KeePassDX` | commit `77ecfa8654a5bd0d79d512f8fa71c0af8a29b554` |
| Argon2.Bindings (its `runtimes\win-x64\native\libargon2.dll`, MIT) | 1.21.0 | `https://www.nuget.org/api/v2/package/Argon2.Bindings/1.21.0` | package `d7691ee528c66018db913ae86aa4c4e35c94726aafc7a43b73f3671d8a25b1da`; the DLL `3644bfd3f0cf7d509bbe7074397c75f1e55d69e5313b50cad2ae8d86c803b647` |
| Temurin JDK | 17.0.21+9 | Adoptium's GitHub release, `OpenJDK17U-jdk_x64_windows_hotspot_17.0.21_9.zip` | `98518ecf554465057ee5509462631deeee374bc368c12a411d225cd29381f84b` |
| kotlin-compiler | 2.0.0 | `https://github.com/JetBrains/kotlin/releases/download/v2.0.0/kotlin-compiler-2.0.0.zip` | `ef578730976154fd2c5968d75af8c2703b3de84a78dffe913f670326e149da3b` |
| joda-time | 2.14.1 | Maven Central | `656721d9456d5ec7e7bfcf02665202ea5a375ff22136b8f23473cf7eae8fa0c6` |
| commons-io | 2.21.0 | Maven Central | `7d643a2afea8b058b762aa6fb90e5b256f6c729739f8b3784c3370ddc609e88d` |
| bcprov-jdk18on | 1.84 | Maven Central | `64d6c5a6121fcd927152dd182cbed39afe0fda641a970d9bcc0c9cb1858b2731` |
| bcpkix-jdk18on | 1.84 | Maven Central | `c87f16ed9e5ec61bc94151e9f3646ac44e50cd448121ce84367fa4b7ec7ec1bb` |
| bcutil-jdk18on | 1.84 | Maven Central | `b374e16963421fb9cfb01cc20d7ad8fd2f8b8188e3eef0ec0a8965e245f7619a` |
| kxml2 | 2.3.0 | Maven Central | `f264dd9f79a1fde10ce5ecc53221eff24be4c9331c830b7d52f2f08a7b633de2` |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | NuGet, through `dotnet build` | the package `7dbd296bdb10c4dfa6af4be75e0d4881bc78f4268ea92f860e44df58d6e852fb`, checked by NuGet's own signature check and not by a script |

The JDK's hash is the value Adoptium publishes for the file and the compiler's the value JetBrains
publishes beside it; the jars' are what Maven Central served on 2026-11-16. Python packages come
through `uv` with `UV_EXCLUDE_NEWER` at `2026-11-16T00:00:00Z`.

## 5. The Keepass2Android harness

`k2a\K2aHarness.csproj` compiles, for .NET 10 on Windows x64, every file of the app's own copy of
KeePassLib, `src\KeePassLib2Android\`, from where the fetch left it, but `Kp2aLog.cs` (the app's
logger) and `Cryptography\SelfTest.cs` (the start-up self test, which no load or save calls); and
the app's `src\Kp2aBusinessLogic\database\KdbxDatabaseFormat.cs`, the format object its loader and
its save use. **No file of the app is edited or copied.** `k2a\StandIns.cs` holds the stand-ins,
each with what it can change:

| Original | Stand-in | What it can change |
| --- | --- | --- |
| `keepass2android.Kp2aLog` | writes nothing | nothing written to a file |
| the native `libargon2` called through `LibraryImport("argon2")`, and `JavaSystem.LoadLibrary` | the reference Argon2 for Windows x64 of section 4, handed to the runtime by a DLL import resolver in `Driver.cs`; `LoadLibrary` does nothing | key derivation only |
| the native AES-KDF, `NativeFinalKey` | throws, so the copy's own managed fallback runs | key derivation only |
| `Android.Graphics.Bitmap`, `BitmapFactory` | empty objects | nothing: a custom icon's bytes are kept and written as read |
| `Java.IO.FileNotFoundException`, and the namespaces `Org.BouncyCastle.*`, `Java.Text`, `Android.Webkit` | an exception type and empty namespaces, named only in `using` lines | nothing |

`k2a\Driver.cs` restates, in the harness's own code, the calls the app's code makes around the
database, each step naming the method it stands for: `Database.LoadData` for `open`,
`SaveDb.PerformSaveWithoutCheck` for `save`, `SaveDb.MergeAndFinish` with `SaveDb.MergeIn` for
`merge`, `EntryEditActivity.SaveEntry` with `UpdateEntryFromUi` for `edit`, `EntryActivity`'s touch
for `view`, `CreateNewWithoutTemplate` with `AddEntry` for `add`, `EditGroup` for `rename-group`,
`DeleteRunnable.DoDeleteEntry` for `recycle` and `purge`, and `MoveElements` for the moves.

## 6. The KeePassDX harness

`kpdx\build.ps1` compiles the `database` module's main sources and the Kotlin and Java sources of
the `crypto` module, 159 files, with kotlinc 2.0.0 (the version the project pins) and its parcelize
plugin, targeting Java 17, against the jars of section 4 and the stand-ins in `kpdx\standin\`, then
`kpdx\Driver.kt`, and writes the launcher `kpdx\bin\harness.cmd`, whose paths are relative to
itself. The module is compiled once, in about 75 s, and again only with `-Clean`. The compiler
runs with `JAVA_OPTS=-Xmx4g`. **No file of KeePassDX is edited or copied**: two are left out of
the compilation and replaced by stand-ins of the same name.

| Original | Stand-in | What it can change |
| --- | --- | --- |
| `crypto\...\argon2\NativeArgon2KeyTransformer.java` (JNI) | Argon2 by Bouncy Castle 1.84's `Argon2BytesGenerator`, the same parameters, a 32-byte output | key derivation only |
| `crypto\...\aes\NativeAESKeyTransformer.java` (JNI) | throws, so the module's own `AESTransformer.transformKeyInJVM` runs | key derivation only |
| `android.os.Parcel`, `Parcelable`, `ParcelUuid`, `Bundle`, `Intent` | enough to compile; `Parcel` refuses every call | nothing: nothing is parceled |
| `android.util.Xml` | `newSerializer()` returns kxml2 2.3.0's `KXmlSerializer`, where Android 11 returns libcore's copy | a character XML 1.0 cannot hold, which libcore refuses and kxml2 writes as a reference (read); no input holds one |
| `android.util.Base64` and its streams | Android's documented flags | none seen in the calibration |
| `Build` (API 30), `Log` (prints only when `KPDX_LOG` is set), `Uri`, `Color`, `Resources`, `Context`, `ContentResolver`, `pm.*`, `SigningInfo`, `AndroidException`, `SuppressLint` | enough to compile code no step here calls | nothing |

`kpdx\Driver.kt` reads the script language of `k2a\Driver.cs` and restates the app module's
runnables, which are not compiled: `LoadDatabaseRunnable` for `open`, `SaveDatabaseRunnable` for
`save`, `MergeDatabaseRunnable` for `merge`, the entry screen's `EntryInfo` with
`UpdateEntryRunnable` for `edit` and with `AddEntryRunnable` for `add`, `TouchEntryRunnable` and
`TouchGroupRunnable` for `view` and `view-group`, the group dialog with `UpdateGroupRunnable` for
`rename-group` and `group-notes`, `DeleteNodesRunnable` for `recycle`, `purge` and `empty-bin`, and
`MoveNodesRunnable` for the moves. A job is given `KPDX_JOB_TIMEOUT` seconds of its own
processor time (120 unless set; the calibration sets 60); one that does not end is named with
where it was, the JVM ends with code 3 and `RESUME <n>`, and `common.run_kpdx` starts a fresh JVM
at the next job, since a thread cannot be stopped without leaving the module's state unsound.

## 7. The calibration

**How a cell is held to the record.** `run.ps1` makes the bases again with the emulator
measurement's own probe, and every base's dump is the record's line for line but the first, which
names the file's hash (the same comparison the android family's `RUN.md` reports). A cell (in
`cells_android.py`, `cells_kpdx.py` and `cells_kpdx2.py`) is the steps of its emulator flow that
change the object model or write the file, read from the flow and its step log. The harness writes
the files into `work\calibrate\<program>\<cell>\`, the probe dumps them, and `common.normalize`
sets aside three things and nothing else before each dump is compared with the recorded dump: the
first line; an identifier the program drew at random, written `~new`; and a time of the run's own
moment, any time from 2026-11-15T12:00:00Z on outside a quoted value, which every base time
precedes. A cell agrees when every file it names agrees.

**A variant** is a cell's steps with a step of the app's surrounding code placed otherwise, and is
counted apart: Keepass2Android shows an entry again when its editor returns, and touches it, on the
screen's thread while the save runs on a worker thread.

**A cell with no file** is a run whose program never ended on the device (its file is a base
unchanged); it agrees when the harness's job does not end within its timeout and writes nothing.

**That the comparison discriminates**: the harness's save of `S-argon` held to the record's save of
`S-aes` differs in its KDF line, and `StoreLab`'s analysis, run on histories whose files were
swapped, reported every swapped file (section 9).

### 7.1 Keepass2Android

60 cells, 94 recorded dumps: every Keepass2Android run of the first measurement, of the overwrite
measurement and of the synchronize measurement that writes a file through the database code.
`recorded\calibrate-k2a.txt` lists every cell.

| Kind in `calibrate-k2a.txt` | Cells | Agree | Agree as the variant |
| --- | --- | --- | --- |
| `save`, `edit`, `delete`, `move` | 9 | 9 | |
| `merge` | 6 | 6 | |
| `ow-overwrite`, `ow-merge`, `ow-reopen` | 29 | 29 | |
| `sy-phone-only`, `sy-both-merge`, `sy-both-overwrite` | 9 | 9 | |
| `sy-save-merge`, `sy-save-overwrite` | 7 | 4 | 3 |
| **all** | **60** | **57** | **3** |

The three that agree only as the variant are cases 5B, 6 and 7 of the synchronize measurement:
P2's usage count, and in case 7 its groups' counts, hold the touch of the entry shown again, where
cases 5 and 9 do not. Not cells: the overwrite record's two "look" runs and its "Cancel" run, which
write nothing, and the synchronize record's cases 1, 2 and 8, which reload or copy the server's
file. One harness defect was found and fixed: the first script of cases 3a to 3c saved again after
the touch.

### 7.2 KeePassDX

34 cells: the 15 KeePassDX runs of the first measurement (`cells_kpdx.py`), and 19 runs of the
second KeePassDX emulator measurement of 2026-11-16 (`cells_kpdx2.py`), whose record was not yet
on the branch: the bases, the dumps and the step logs of that measurement's working directory were
copied into `work\kpdx-record2\` when this calibration ran, and its own probe (its `Store.cs` and
`DX.cs`, SHA-256 `3d5505fc...72f0` and `c6bb1452...034d`) is built from that copy by
`dumper2\Dumper2.csproj` to dump the files of these cells, since its dump names the nodes of its
bases by its own aliases. Its runs that write nothing (Reload data alone) are not cells.
`recorded\calibrate-kpdx.txt` lists every cell.

| Kind in `calibrate-kpdx.txt` | Cells | Agree |
| --- | --- | --- |
| first record: `save`, `edit`, `delete`, `move` | 9 | 9 |
| first record: `merge` ("Merge from ...", both directions, three runs each) | 6 | 6 |
| second record: `r2-merge` ("Merge from ..." and "Merge data", the phone's and the desktop's copies both ways, and the attachment, history, history-limit and nested-group bases) | 13 | 12 |
| second record: `r2-save-data` ("Save data" after the external-change dialog) | 2 | 2 |
| second record: `r2-reload-add` ("Reload data", then an entry added) | 2 | 2 |
| second record: `r2-merge-did-not-end` | 2 | 2 |
| **all** | **34** | **33** |

**The first record's `edit-e1`**: its flow meant to append to E1's password, and its step log
records the text landing in the title (`appended '-phone': 'E1-phone'`); the record's file holds
that, so the cell edits the title. **The six `-src` dumps** of the first record's merges are the
bases P and C exactly: the program reads the arriving file and does not write it, so they are not
cells.

**`r2-merge-did-not-end`** is the two runs whose merge never ended on the device (the report,
section 2.3); in the recorded calibration neither ended in the harness, which stops a job after 60 s
of its own processor time. **The one cell that does not agree**, `mfPD-r2`, is a run of the same
merge that ended on the device and did not end in the harness. Which of the five runs of that
merge end varied between the three calibration runs made here: in an earlier one `extmPD-r3`
ended, its file the record's `extmPD-r2` line for line.

### 7.3 The KeePassDX merge that does not end

`repeat.py 30 30` runs the merge of PHONE with DESK arriving 30 times and the reverse 30 times, a
job stopped after 30 s of its own processor time (a finished merge takes under 2 s), and resumed in
a fresh JVM after every job that did not end, so every PHONE-open run is the first job of its JVM.
`recorded\repeat-kpdx.txt`: PHONE open, 30 of 30 did not end, each found in
`DatabaseKDBXMerger.firstNotDeletedParent`; DESK open, 30 of 30 ended, each file the record's
`mfDP-r1` line for line. **An earlier run limited each job by the clock, 30 s, and is not counted**:
under the endpoint scanner a fresh JVM spent longer than that reading its own class files, so jobs
were stopped that were not looping; the limit on processor time replaced it.

## 8. Measurement 1: the places of the format (`places.py`)

The kdbx-places family's phone bases, made by that family's `android\make-bases.py` into
`work\places\bases\`, each through each harness as a save (`add Root phone-added`), an unrelated
edit (`rename-group =kpg-tpl2 -phone`) and an edit of the entry holding the entry places (its
password), and the two pairs merged both ways (Keepass2Android with an entry added first, as its
merge is a save), with KeePassLib 2.62's Synchronize of the same pairs beside them (`StoreLab
sync`). The family's `android\check.py` reads each file. That family was still being written when
this ran: the scripts used were those whose SHA-256 begins `bd396b1ddc0135f4` (`check.py`),
`179c3e3d5ba46d8b` (`make-bases.py`), `6b6817ba2cd52f3a` (`scripts\places.py`), `0bc331029d084860`
(`scripts\summarize.py`) and `6a588aff278d9431` (`scripts\kdbx_raw.py`), and the place names are the
ones its `check.py` printed then. Writes `out\places-table.md` (a row per place: the base it is
measured on, the three saves of each program, and the four merges of each program and of
KeePassLib), `out\places-<program>.txt` (every line `check.py` printed) and
`out\places-<program>-harness.txt` (the harness's refusals, with the reason the program gave).

## 9. Measurement 2: the store's records (`kplib\StoreLab.cs`)

`StoreLab gen work\store 20261116 2000 1000` makes 3,000 histories, each history from a generator
of its own seeded from the seed and its number, so the run is the same whichever order the
histories are made in; the same command made the same `steps.txt` twice here. Each history is a
directory with its copies, `A.kdbx`, `B.kdbx` and for 1,000 of them `C.kdbx`, and `steps.txt`; the
jobs files hold, for each program, every copy saved (an entry added) and edited (`=O1`'s password),
and every order of merging the copies. `StoreLab ref` runs KeePassLib 2.62's Synchronize in the same
orders, and `StoreLab analyze` reads every file and writes, per program, a summary (the cases, every
kind of finding with its count and six examples) and every finding as a line. The store, the steps
and what is held to what are the report's section 4.

**That the analysis discriminates**: a copy of two histories in which one history's files were put
in the other's place, and a save replaced by another history's copy, gave losses, entries in no
copy and differences from KeePassLib in every swapped file (`work\store-neg1\`, run by hand).

### 9.1 Long strings (`StoreLab long`)

`StoreLab long work\long` makes one history of two copies: A is gen's ancestor with eight more
store entries, each with one string of 4,096, 16,384, 65,536 or 262,144 characters, plain
(`KPG.Data`) or protected (`KPG.Secret`), ASCII with a two-byte and a three-byte UTF-8 character
every 997 and 1,999 characters; B is A after an admission and with a ninth entry of 262,144
characters. The jobs, `ref` and `analyze` are gen's; the analysis compares every string by its
SHA-256 and its protection flag. Writes `out\long-<program>-summary.txt` and `-findings.txt`.

## 10. Measurement 3: format version and key derivation (`formats.py`)

`StoreLab formats` writes the inputs (`kplib\Formats.cs` lists them); each goes through each
harness as a save, an edit of its one entry and a merge with a copy of itself, and through
KeePassLib 2.62's open and save (`StoreLab resave`); `StoreLab dumpver` reads every file written.
`out\formats.txt` holds a line per file: where it came from, its name, its header version, its key
derivation, its cipher, its compression, its database custom data keys, and each 4.1 item kept or
gone.

## 11. How long it ran

On this machine (12 logical processors, Windows 11, an endpoint scanner reading every file written,
which made every run that writes many small files bound by the disk rather than the processors),
on 2026-11-16:

| Step | Here |
| --- | --- |
| `fetch.ps1`: both clones | 17 s |
| `kpdx\build.ps1`: the module, once | about 75 s; the driver alone, about 3 minutes under the load of the other runs |
| calibrate, Keepass2Android: 60 cells, 67 jobs | 24.2 s for the jobs; the dumps and comparisons about a minute |
| calibrate, KeePassDX: 34 cells | 628 s in four JVMs one after the other, three of them ended by a job stopped after 60 s of processor time |
| `repeat.py 30 30` | 30 merges that did not end, at 30 s of processor time each, then 30 that ended |
| places, per program | 64 jobs: Keepass2Android 27.5 s, KeePassDX 31.9 s; then `check.py` for each file |
| store `gen` | 545 s for 3,000 histories |
| store, Keepass2Android | 24,000 jobs in 1,502 s, one process: 62.6 s per 1,000 jobs |
| store, KeePassDX | 24,000 jobs in four JVMs of 6,000 each, 1,030 to 1,095 s each: 45.6 s of the clock per 1,000 jobs, 175 to 182 ms per job in each JVM |
| store `ref` | 2,270 s for 16,000 merges, six workers |
| store `analyze` | 3,150 s for three programs |
| formats | under two minutes |
| long strings: 6 jobs per program | Keepass2Android 6.2 s; KeePassDX 174 s under the load of other runs, and 9.5 s for three loads and a save of the same copies alone |

**Per 1,000 cases**, the harness alone, measured by the KeePassDX harness's builder on a quieter
machine state: KeePassDX 63.4 s per 1,000 opens and saves of `S-argon` (Argon2d, 1 MiB), 164.5 s
with AES-KDF at 60,000 rounds (its JVM fallback). The store's numbers above include writing every
file under the scanner.

## 12. What is recorded, and what was replaced

`recorded\` holds this run's output as `out\` had it: `calibrate-k2a.txt`, `calibrate-kpdx.txt`,
`repeat-kpdx.txt`, `places-table.md`, `places-k2a.txt`, `places-kpdx.txt`, `places-kplib.txt`,
`places-k2a-harness.txt`, `places-kpdx-harness.txt`, `store-table.md`, `long-<program>-summary.txt`
and `long-<program>-findings.txt`,
`store-<program>-summary.txt` and `store-<program>-findings.txt` for `kplib`, `k2a` and `kpdx`, and
`formats.txt`. No `.kdbx`, no path of this machine and no file of either program is in it: the
outputs name histories, cells and files by their names in `work\`, and nothing was replaced.

## 13. The figures the report quotes

"Recorded" is `recorded\`; "this run" is the run that wrote it, and "reproduced" is what a second
run here gave, where one was made.

| Number | Quoted in the report | Recorded | Reproduced |
| --- | --- | --- | --- |
| Keepass2Android: 60 cells, 94 dumps; 57 agree, 3 more as the variant | 2.1 | `calibrate-k2a.txt` | yes, every cell alike in two runs |
| KeePassDX: 34 cells; 33 agree (first record 15 of 15, second record 18 of 19) | 2.2 | `calibrate-kpdx.txt` | differs: in three runs the five merges of PHONE with DESK arriving ended in 4, 3 and 2 of 5 (the record is the third); every other cell agreed in all three |
| the merge of PHONE with DESK arriving as the first job of its JVM: 30 of 30 did not end; DESK with PHONE arriving: 30 of 30 ended, each the record's `mfDP-r1` line for line | 2.3, `RUN.md` 7.3 | `repeat-kpdx.txt` | not run again with the processor-time limit; an earlier run with a limit on the clock stopped jobs for a slow disk and is not counted |
| KeePassDX refused 6 of the 20 place bases, with the reasons given | 3 | `places-kpdx-harness.txt`, `places-table.md` | yes, in three runs |
| every row of the place table | 3 | `places-table.md` | yes, but for KeePassDX's refusals, in two runs |
| 3,000 histories: 2,000 of two copies, 1,000 of three; per program 7,000 saves, 7,000 edits, 10,000 merges of two, 6,000 of three | 4 | `store-table.md` | the histories: yes, `steps.txt` alike twice; the runs: not run again |
| KeePassLib: 30 and 90 merges with the tie, in 15 histories | 4 | `store-kplib-*` | not run again |
| Keepass2Android: no merge differs from KeePassLib's; 7,000 and 7,000 saves and edits 4.0 to 3.1 | 4 | `store-k2a-*` | not run again; the same held in two trial runs of 20 and 80 histories |
| KeePassDX: 6,791 merges of two differ; 10,140 and 10,160 records missing; 1,660 entries back in 1,462 merges of 691 histories, by kind; 1,208 and 1,266 entries placed elsewhere; 15 and 30 at the tie | 4 | `store-kpdx-*` | not run again |
| a sample of 8 entries placed elsewhere: all in the plugin's group in KeePassDX's result, in the bin in KeePassLib's | 4 | not recorded (read from the dumps by hand) | not run again |
| long strings: no limit, truncation or failure; every string and flag kept by every load, save, edit and merge of both | 4.1 | `long-*` | not run again |
| every cell of the version table | 5 | `formats.txt` | yes for Keepass2Android in two runs, for KeePassDX against the harness builder's own probe of the same inputs |

## 14. What was used of other families, and changed

Nothing outside this directory was changed. Read and run as they stood: the emulator
measurement's probe (`spikes\storage\android\probe\overwrite\Store.cs`, compiled by
`dumper\Dumper.csproj`), its record (`spikes\storage\android\recorded\`) and its flows and step
logs (`scripts\flows\`, `recorded\**\logs\`), which the cells cite; the kdbx-places family's
`android\make-bases.py`, `android\check.py` and the scripts they import (section 8, with their
hashes), and its built `probe\bin\Release\net48\Kpl.exe`; and the KeePassDX emulator measurement's
working directory, copied into `work\kpdx-record2\` (section 7.2).
