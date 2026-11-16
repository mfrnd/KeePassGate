# Probes of `ground-others`

The probes behind `papers\ground\ground-others.md`, the survey of the programs a database meets
beyond KeePassXC and the Android programs and of how others keep their own data in a `.kdbx`. In
the tables below "others 1.3" means item 3 of section 1 of `ground-others.md`. The re-read behind
`ground-measured.md` is the family `ground-measured\`, with a `RUN.md` of its own. The agents'
working directories behind this family:

| Agent directory | Here | Paper |
| --- | --- | --- |
| `others` | this directory: `run.ps1`, `scripts\`, `kdbxweb\`, `recorded\` | `ground-others.md` |
| `evaluation-completeness` | not carried, section 3 | none quotes it |

Every probe here ran again from the branch on 2026-11-16, on the machine the paper was measured
on. **Every number and outcome the paper quotes from a probe here came out the same.** What
differs is noise, the moment a database was made, a random identifier and a line `uv` prints while
it builds an environment. Section 2 lists what the paper presents as measured that no probe or
output here shows.

| Directory | Paper | What runs | Command, from the branch root |
| --- | --- | --- | --- |
| this directory | `ground-others.md` | `scripts\kp.ps1` over `scripts\Kp.cs`, `scripts\kw.js` on kdbxweb 2.1.1, `scripts\pk.py` on pykeepass 4.2.0, in the agent's order | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-others\run.ps1` |
| this directory | `ground-others.md`, its counts from registries | the agent's queries to npm, crates.io and pkg.go.dev | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-others\run.ps1 -Only registries` |

Each writes into `out\` beside it, which is not committed, and each file it writes has its recorded
copy under the same name in `recorded\`, apart from the files of the steps added on the branch.
The databases go to `work\`, which is not committed either; none is kept. What a run needs:
Windows PowerShell 5.1; Node.js with npm (run here with 24.19.0 and 11.17.0); `uv` (0.12.23) with
a Python it can find; `curl.exe` for the registries. KeePass 2.62 is provided and checked by
`tools\KeePass.ps1`, which `run.ps1` and `scripts\kp.ps1` call.

## What the agent ran, and that the branch runs the same programs

- **KeePass.** The agent loaded the product worktree's `.keepass\2.62\KeePass.exe`. That file
  and the branch's `.keepass\2.62\KeePass.exe`, which every run here loads, have the same SHA-256,
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`.
- **kdbxweb.** The agent installed `kdbxweb@2.1.1` with `npm install --ignore-scripts` into a
  directory of its own and kept the `package.json` and `package-lock.json` that wrote. Both are
  carried unchanged in `kdbxweb\`, and `run.ps1` installs from them with `npm ci --ignore-scripts`,
  which holds each package to the integrity hash the lock file records: kdbxweb 2.1.1
  (`sha512-z+a2+BEzyK2kUh0x...`), `@xmldom/xmldom` 0.7.13 and `fflate` 0.7.5, the same three the
  agent's install reported. npm marks that `@xmldom/xmldom` as deprecated, then and now.
- **pykeepass.** The agent ran `uv run --no-project --with pykeepass`, which resolved 4.2.0, and
  the probe printed that version. The command here pins `pykeepass==4.2.0`. The agent's
  environment held 12 packages whose versions it did not print; `out\pykeepass-env.txt`, added on
  the branch, lists what this run resolved, lxml 6.1.3 and construct 2.10.70 among them.
- **Which PowerShell.** The agent's console commands ran in PowerShell 7, the session's shell;
  `kp.ps1` ran in Windows PowerShell 5.1, started by those commands. `run.ps1` runs in Windows
  PowerShell 5.1 and gives byte for byte the agent's files, since nothing it does depends on the
  edition.

## Changes made to the probes, and why

| Probe | Change | Why |
| --- | --- | --- |
| `scripts\kp.ps1` | KeePass is `Get-KeePassExe` from `tools\KeePass.ps1`, dot-sourced inside a script block | the product worktree's path was written in; the script block keeps the tool's `Set-StrictMode` out of the probe's scope |
| `scripts\kp.ps1` | `$scripts` is `$PSScriptRoot`, and `KpProbe.dll` is compiled into `bin\`, made first, rather than beside `Kp.cs` | the scratch path was written in; `bin\` is ignored, the scripts directory is committed |
| `scripts\run-kdbxweb.ps1`, `scripts\run-keepass.ps1` | `$S` is the family directory, and kdbxweb is loaded from `kdbxweb\node_modules\kdbxweb` | the scratch path was written in, and the agent's npm directory sat in `work\` |
| `scripts\Kp.cs`, `scripts\kw.js`, `scripts\pk.py` | none | |
| `scripts\build-xml.js` | added: the program the agent ran inline as `node -e`, from the session transcript, its code unchanged after a header comment | the agent kept no file; `run.ps1` passes its text to `node -e` as the agent did, so `process.argv` means what it meant |
| `run.ps1` | added | the agent ran its steps by hand; `run.ps1` runs them in the agent's order with the agent's arguments, and writes each output with the bytes the agent's `Tee-Object` or `Out-File` wrote in PowerShell 7: UTF-8 without a byte order mark, CRLF, and for `group-merge.txt` the empty first and last line that the formatting of `Select-String`'s matches adds |
| `run.ps1`, `pykeepass` | `pykeepass==4.2.0` where the agent wrote `pykeepass` | the version the agent's run resolved, held so that a later release cannot change the run |
| `run.ps1`, `pykeepass` | `out\pykeepass-env.txt` added | the versions of the environment, which the agent did not print |
| `run.ps1`, `inputs` | added | a dump of every input database, which the report's appendix summarizes and no recorded output shows |
| `run.ps1`, `registries` | the agent's `npm view` and `curl` lines; `npm view kdbxweb time` added; the pkg.go.dev page fetched with `curl.exe` | the agent printed no publication date, and read the pkg.go.dev page through a fetch tool rather than a command |

A defect in a probe that changes a result: none found. **The history limits in `ground-others.md`
4 are read off one file**, kdbxweb's XML export of its resave of A, which the report calls
"kdbxweb's output of the base file", and which holds the limits KeePassLib wrote when `Kp.cs` made
the databases; that changes no quoted number.

**What was scrubbed.** The three scripts above named the scratch directory and the product
worktree's KeePass, which the changes replace. Everything in `recorded\` is either the agent's file
byte for byte (the four logs and `A-resaved.xml`) or console output the session transcripts hold,
kept as printed (`bases.txt`, `build-xml.txt`, `pykeepass.txt`, `npm-view.txt` and `crates.txt`),
none of which names a path of the machine. The transcripts trim the trailing space the dump writes
at the end of a history line from the last line of an output, so `bases.txt` lacks that one space.

## 1. This directory: kdbxweb, pykeepass and KeePass 2.62 over invented databases

**What it measures.** `Kp.cs` makes a base database and five copies of it with invented content,
in two sets: `db`, whose database custom data carries per-key times (KDBX 4.1), and `db40`,
without them (KDBX 4.0), and a third, `db40g`, for the group. kdbxweb 2.1.1 opens and saves A and
merges six pairs (A and B, A and Btie, A and Bcd, each way) of each set; KeePass 2.62 merges the six
pairs of the 4.1 set with Synchronize; pykeepass 4.2.0 opens and saves A, adds an element KeePass
does not know to Meta, G and E, and counts them after KeePass and kdbxweb save the result; and
KeePassLib dumps every result. Cited by `ground-others.md` 1, 3, 4, 5, 6 and the appendix, and
through it by the draft evaluation's tables.

**Command.** `run.ps1` above, in about 40 seconds (about 15 more the first time, when it compiles
`Kp.cs`), and `-Only registries` in 4 to 7. It installs kdbxweb when `kdbxweb\node_modules` lacks
it, empties `work\`, and runs the steps its header lists: `bases`, `build-xml`, `kdbxweb40`,
`kdbxweb41`, `xml`, `pykeepass`, `keepass`, `group` and `inputs`. `-Only` takes a list of them;
`registries` runs only when named.

**How the fresh run compares with the record.**

- `kdbxweb-db40.txt`, `kdbxweb-db41.txt`, `keepass-db41.txt`, `group-merge.txt`, `crates.txt`:
  identical bytes.
- `bases.txt`: identical lines; the record lacks the last line's trailing space, as noted above.
- `build-xml.txt`: identical but for the root group's identifier and its creation time in Base64,
  `cbuL4g4AAAA=` then and another value on every run, which are random and the moment `Kp.cs` made
  the database. Noise.
- `A-resaved.xml`: identical once every time, which is the moment the database was made, and every
  identifier, which is random, are set aside. The record is the export of kdbxweb's resave of an A
  that an earlier version of `Kp.cs` made, before it wrote `KPG.Empty`, in a run that left nothing
  else; kdbxweb drops that empty value on every save, so the export holds the same items either way.
- `pykeepass.txt`: identical but for the record's first line, `Installed 12 packages in 312ms`,
  which `uv` prints only while it builds the environment, and for a space at the end of a line
  of the fresh output that the record lacks, as in `bases.txt`. Noise.
- `npm-view.txt`: the 20 lines of `npm view` identical; the record goes on with the agent's install
  output, which `npm ci` replaces here, and this run adds the publication times, among them
  `"2.1.1": "2021-09-06T19:57:25.199Z"`.
- `inputs.txt` and `pkg-go-dev.txt` have no record. The first matches the appendix line for line;
  the second holds `Known importing modules: 79` and `Known importing packages: 46 (displaying 99
  packages, including internal and invalid packages)`.

**The numbers and outcomes `ground-others.md` quotes.** A merge "X+Y" is Y merged into an open X.
Every row is in the file named in its recorded column, under `recorded\` and `out\` alike.

| Claim | Quoted in | Recorded | This run | Reproduced |
| --- | --- | --- | --- | --- |
| KeePass 2.62 and kdbxweb merge no entry field by field: the newer entry is current whole, the older copy's string only in history | others 1.1, 4 table, 4 "a different field", 6 | `keepass-db41.txt`, `kdbxweb-db40.txt`: A+B and B+A current E holds `Note-B` only, `Note-A` in the history item at 10:00:10 | same | yes |
| Two entries added to G, one in each copy, both arrive, both directions and at a tie | others 1.2, 4, 6 | G holds E, EA and EB in all four A, B and Btie results of each program; Bcd adds no entry | same | yes |
| At a tie KeePass 2.62 takes the arriving copy's entry and files the open copy's nowhere, both directions | others 1.3, 4 table, appendix | `keepass-db41.txt`: A+Btie current `Note-B`, Btie+A current `Note-A`, history 09:00 and 10:00 only | same | yes |
| At a tie kdbxweb keeps the open copy's entry and files the arriving copy's nowhere, both directions | others 1.3, 4 table, appendix | `kdbxweb-db40.txt`: A+Btie `Note-A`, Btie+A `Note-B`, history 09:00 and 10:00 only | same | yes |
| KeePass 2.62's history item is the older version with its custom data; kdbxweb's has none | others 4 table, 4 "history", appendix | `keepass-db41.txt` item 10:00:10 `KPG=rule-base`; `kdbxweb-db40.txt` item 10:00:10 `customdata (none)` | same | yes |
| The history limits written: `HistoryMaxItems` 10, `HistoryMaxSize` 6291456, `MaintenanceHistoryDays` 365 | others 1.4, 4 | `A-resaved.xml`: 10, 6291456, 365 | same | yes |
| KeePass 2.62 cannot open any of the seven files kdbxweb saved or merged from the 4.1 set: `FormatException` in `KdbxFile.ReadTime` | others 1.5, 3, 6, appendix | `kdbxweb-db41.txt`: 7 of 7 | 7 of 7 | yes |
| kdbxweb's document for a binary save holds the custom data item's time as `2026-01-01T10:00:00Z` beside node times in Base64 | others 3, appendix | `build-xml.txt` | same, other identifier and time | yes |
| pykeepass opens a file kdbxweb wrote from the 4.1 set | others 3 | `pykeepass.txt` last line: 0 unknown elements, history 2, `(4, 1)` | same | yes |
| kdbxweb's merge never takes the other copy's entry or group custom data: the stale rule under the newer time | others 1.6, 4 table, appendix | `kdbxweb-db40.txt` A+Bcd `KPG=rule-base` at 10:00:20; `group-merge.txt` kdbxweb A+Bg `grule-base` at 10:00:20 | same | yes |
| KeePass 2.62 takes the newer copy's entry and group custom data | others appendix | `keepass-db41.txt` A+Bcd and Bcd+A `rule-B`; `group-merge.txt` both ways `grule-B` | same | yes |
| kdbxweb drops a custom data item whose value is empty, on every save | others 1.6, 3 table, 6 | `KPG.Empty` absent from all 7 results of `kdbxweb-db40.txt` and both kdbxweb results of `group-merge.txt` | same | yes |
| A resave by kdbxweb keeps every string with its protection flag, the attachment, the tag, both history items, the entry's and the group's custom data | others 1.8, 3 | `kdbxweb-db40.txt` A-resaved | same | yes |
| pykeepass 4.2.0 keeps every item, the empty value, the per-key times and KDBX 4.1 on a save | others 1.8, 3, appendix | `pykeepass.txt`: version `(4, 1)`, `KPG.Empty=@2026-01-01T10:00:00Z`, the dump of A-resaved | same | yes |
| Unknown elements in Meta, G and E: pykeepass keeps all 3; KeePass 2.62 drops all 3 on its save; kdbxweb drops them | others 1.7, 3, appendix | `pykeepass.txt`: 3, 0, 0 | 3, 0, 0 | yes |
| KeePass 2.62 keeps strings, attachments, tags, history, every custom data and its times | others 3 table, appendix | `keepass-db41.txt`: every result; `KPG.Empty` and `KPG.Key` with their times in all six | same | yes |
| No program measured dropped an entry's own modification time | others 4 | every dump: E at its set time | same | yes |
| An attachment travels with its entry | others 5, KeeAgent row | `binaries 1` in every dump | same | yes |
| The inputs: A, B, Btie, Bcd and Bg as the appendix describes them | others 2, appendix | no record | `inputs.txt` | yes, no record |
| kdbxweb 2.1.1 is the latest on npm, published 2021-09-06 | others 2 | `npm-view.txt`: `"latest": "2.1.1"`; no date | same, and `2021-09-06T19:57:25.199Z` | yes; the date has no record |
| pykeepass 4.2.0 | others 2 | `pykeepass.txt` | same | yes |
| keepass-rs has 23 reverse dependencies on crates.io | others 3 | `crates.txt`: 23 | 23, the same names | yes, on 2026-11-16 |
| gokeepasslib has 79 known importing modules on pkg.go.dev | others 3 | none: read through a fetch tool | `pkg-go-dev.txt`: 79 | yes, on 2026-11-16; no record |

## 2. Claims for which no probe or output can be found

These are the findings the branch cannot yet vouch for. Claims marked `[R]` (read from source) and
`[A]` (assumed) are not listed: their marks already record that nothing ran.

| Claim | Quoted in | Why nothing here shows it |
| --- | --- | --- |
| The history limits are "carried by every writer measured" | others 4 | only kdbxweb's XML export prints them; no output of pykeepass or of KeePass 2.62 prints a limit |
| The file stores whole seconds | others 1.3, 4 `[M, spike]` | `spikes/revocation-merge`'s record, not a probe here |
| KeePassXC 2.4.1 to 2.8.0, Keepass2Android and KeePassDX rows, among them "216 runs" | others 1.1, 4 table `[M, spike]` | `spikes/revocation-merge`'s record; `ground-measured\` re-reads the files behind their history columns |

## 3. Not carried, and why

- **Clones**, which the `[R]` claims cite by file and line. Each is a public repository, at the
  commit `ground-others.md` 2 names:

  | Project | Repository | Commit |
  | --- | --- | --- |
  | KeePassium | `https://github.com/keepassium/KeePassium.git` | `e651df4f89b0b8550371566ca9aa55a964d2904e` ("version bump to 2.5.173", tagged `2.6.173`) |
  | Strongbox | `https://github.com/strongbox-password-safe/Strongbox.git` | `c70fc7b021d9dfa2b18a6d7e13406e717dd5251a` |
  | KeePassKit | `https://github.com/MacPass/KeePassKit.git` | `b5cf50ee4ebd67cb355792d7813965fe3ac7125e` |
  | kdbxweb | `https://github.com/keeweb/kdbxweb.git` | `7601e45d2a437f35a7fa3d767164eab60bc05d42` |
  | pykeepass | `https://github.com/libkeepass/pykeepass.git` | `aec256659e7b2b497a37891476241b720f60c84e` |
  | keepass-rs | `https://github.com/sseemayer/keepass-rs.git` | `26cb076aa5cbbe36e175326dfdaf5a19799c4085` (`v0.17.0`) |
  | gokeepasslib | `https://github.com/tobischo/gokeepasslib.git` | `7b40897fc44c9b7cbbb87ca681582fb1c4a8a85c` (`v3.7.1`) |
  | KeePassXC | `https://github.com/keepassxreboot/keepassxc.git` | `860e51d8e9a5a67e73de79b173ae9bd5bdb5ebda` (`2.7.12`) |
  | KeePassDX | `https://github.com/Kunzisoft/KeePassDX.git` | `77ecfa8654a5bd0d79d512f8fa71c0af8a29b554` (`4.5.8`) |
  | Keepass2Android | `https://github.com/PhilippC/keepass2android.git` | `a9c511405633d92fd1c92e019fa7558a4acffe6c` (tagged `v1.16-r1`) |
  | KeeAgent | `https://github.com/dlech/KeeAgent.git` | `431b32a63436e4e8813036aeec41cbd7b4168233` |
  | KeePassRPC | `https://github.com/kee-org/keepassrpc.git` | `dc0a59b60b3ac21da9944bc5b98fe96e706af80a` |
  | KPEntryTemplates | `https://github.com/mitchcapper/KPEntryTemplates.git` | `4e435a30ec663d567eaf8285fa727d18e3d397a1` |
  | KeeTheme | `https://github.com/xatupal/KeeTheme.git` | `b42b8f4f3bc21b620ca6791ef5150ad776e3eb27` |

  The KeePassium commit carries the tag `2.6.173` although its message and the report give 2.5.173;
  the report's line numbers are those of that commit whatever its version is called.
- **Downloads**: kdbxweb and its two dependencies, which `npm ci` fetches against the lock file;
  pykeepass and its dependencies, which `uv` fetches.
- **Databases**: every `.kdbx` the agent of `others` made in its `work\` (the `db`, `db40` and
  `db40g` sets and every result), which a run here makes again.
- **Build output**: `KpProbe.dll`, which `kp.ps1` compiles into `bin\` when it is absent.
- **The first kdbxweb run of `others`**, `work\out-kdbxweb\`, over a `db` set an earlier `Kp.cs`
  made and that the later sets replaced: its databases, and `A-raw.xml`, the two bytes `??` of a
  `kw.js` action that failed. Its `A-resaved.xml` is carried, because the history limits are read
  off it.
- **`evaluation-completeness`**: `scripts\generator.py`, which prints the `Generator` element and
  the version of the databases it is given, and `out\generator.txt`, its output over eight files of
  the `android-measure` and `keepassxc` agents. No paper quotes either, and both read databases of
  other families, so nothing here needs them.
