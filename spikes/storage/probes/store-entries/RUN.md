# Probes for `store-entries`

<!-- cspell:ignore Papierkorb KPXC -->

The probes behind the option that keeps the database-level data as ordinary entries in a group of
the plugin's own: its design, `papers\options\store-entries\design.md`, its two critiques,
`critique-survival.md` and `critique-security-and-fit.md` beside it, and the numbers `digest.md`
takes from them. The option has no formal critique. One directory per paper, as
`spikes\storage\probes\README.md` lays out:

| Directory | Paper | What runs |
| --- | --- | --- |
| `design\` | `design.md` | `probe\Program.cs` built as `Sep.exe` over KeePassLib 2.62, and `scripts\Run.ps1` driving it and four `keepassxc-cli` releases |
| `survival\` | `critique-survival.md` | `scripts\Surv.cs` compiled by `scripts\surv.ps1` over KeePassLib 2.62, `keepassxc-cli` 2.4.2 and 2.8.0, pykeepass and kdbxweb, driven by `scripts\Run.ps1` |
| `security-and-fit\` | `critique-security-and-fit.md` | `scripts\import-plant.ps1` over KeePass 2.62's own importer, `scripts\pk_delete.py` over pykeepass, and a count over the product's tests |

Every probe was run again from this branch on 2026-11-15, twice, on the machine the record was made
on and under the same `de-DE` locale. **Every output came out the same as its record** once what
differs between two runs by design is taken away. Every number the papers quote from these probes
reproduced, the timings within the spread of the machine, apart from one sentence worded wider than
its own output (P4 below). What no probe here covers is listed at the end.

## What a run needs

- Windows PowerShell 5.1 as `powershell.exe`, which runs every command below, and PowerShell 7 as
  `pwsh.exe`, which runs the two `scripts\Run.ps1` (see "PowerShell 7" below).
- The .NET SDK for `dotnet build`, uv, Node.js with npm, and git.
- KeePass 2.62, which `. .\tools\KeePass.ps1; Install-KeePass` provides from the branch root, and
  KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, which `tools\KeePassXC.ps1` provides when a probe asks
  for a release.
- The product's commit in the repository's history, for the count in `security-and-fit\`, which
  `tools\Product.ps1` exports.

Each `Reproduce.ps1` writes only into gitignored places beside it: `out\`, `work\`, `bin\`,
`probe\out\`, `probe\obj\` and `kdbxweb\node_modules\`, and through the tools into `.product\`,
and into `.keepassxc\` when a release is not there yet. **Every database is made by the run, in
`work\`, and none is kept here.**

## Comparing a run with the record

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\store-entries\compare.py spikes\storage\probes\store-entries\design
```

The same with `survival` or `security-and-fit` as the last argument. `compare.py` was written on
this branch. It takes away only three things, each of which differs between any two runs of the
same code: identifiers a run draws at random, the times in milliseconds and the clock at the moment
of a run, and the lines uv prints while it installs a package. An identifier both files share is
kept, so a derived identifier that moved would still show. It printed "the same after
normalizing" for every recorded file a run writes again. What it took away, file by file:

| File | Taken away |
| --- | --- |
| `design\out\merges.txt` | the random identifiers of the root group, `General` and the user entry, which `NewDatabase` draws, and of the group and entries each `CreateNewUuids` merge makes; the clock `keepassxc-cli add` stamps on `General` and on `xc-added` |
| `design\out\summary.txt` | the same `CreateNewUuids` identifiers, as the 4 digit prefixes after `@` |
| `design\out\walk.txt` | the timings, discussed under the design's table |
| `survival\out\chains.txt` | the line uv printed in the record while it downloaded lxml before the first pykeepass call (`Downloading lxml (3.8MiB) Downloaded lxml Installed 12 packages in 791ms`) |
| `security-and-fit\out\import-plant.txt`, `out\pk-delete.txt` | the random identifier of the one imported or created entry |

`design\out\uuids.txt` and `survival\out\bases.txt` were the same as their records byte for byte,
and so was `security-and-fit\out\import-plant.txt` once its one identifier was replaced in both.

## `design\`: the design's merges, saves and walk

**What it measures.** `Sep.exe make` builds three pairs of databases with invented content
(Argon2d, 2 iterations, 1 MiB): set I, where each copy makes the store group and its entries on its
own; set T, the same with the shared entry stamped in the same second; and set D, where copy A
revokes a client the way the design does, deletes an older revocation by hand and prunes a record
while the stale copy B pairs a client. `scripts\Run.ps1` merges each pair with KeePassLib's five
methods and four `keepassxc-cli` releases in both directions, saves with each release, runs one
chain, and dumps every result. `Sep.exe walk` times the read pass over an invented database of
10,054 entries; `Sep.exe uuids` prints the derived identifiers. Cited by `design.md` sections 2, 3,
3.1, 3.2, 4, 5, 7, 10, 11 and "What was run for this report", by `critique-survival.md` as
`[M design]`, by `critique-security-and-fit.md` M1 as `[M store-entries]`, and by `digest.md`
sections 2, 3 and 5.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-entries\design\Reproduce.ps1
```

**Output.** Fresh: `design\out\merges.txt`, `summary.txt`, `walk.txt`, `uuids.txt`. Recorded: the
same names under `design\recorded\`. The timing alone is
`spikes\storage\probes\store-entries\design\probe\out\Sep.exe walk` once the command has built it.

**Runtime here**: 23 and 24 seconds in two runs, the build included.

**Changes made to run it here:**

1. `probe\Sep.csproj`: the `HintPath` to KeePass became `$(KeePassExe)`, which
   `spikes\storage\Directory.Build.props` defines as the pinned `.keepass\2.62\KeePass.exe`, the
   same file by hash as the file the record was built against.
2. `scripts\Run.ps1`: `$Root`, a fixed path into the agent's scratch directory, became
   `Split-Path -Parent $PSScriptRoot`; `$XcRoot` and the four paths into the KeePassXC ground
   report's downloads became `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1`, which
   provides the same pinned archives and checks their signatures.
3. `Reproduce.ps1` was added. It holds the three steps the record was made with and measures
   nothing: `dotnet build -c Release -o probe\out`, which the record's `probe\obj\Release` and
   `probe\out\` imply and which nothing recorded; `scripts\Run.ps1`; and `scripts\summarize.py`
   over `out\merges.txt` into `out\summary.txt`.
4. `scripts\Run.ps1` is started with `pwsh.exe`. See "PowerShell 7".

**What the papers quote, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| The store group's UUID is `FE4040A9E0FAE884FA8BECAEEE9FBB46` | `design.md` §2, table | `uuids.txt`: that value | the same | yes | |
| Set I ends as one group holding both copies' entries under the four KeePassLib methods that match by identifier and in all four `keepassxc-cli` releases, both directions, 16 cells | `design.md` §2 and §7 | 16 of 16 in `summary.txt` | 16 of 16 | yes | the union is what `critique-survival.md` §3 P2 and `digest.md` §3 item 6 rest on |
| A KeePassXC save keeps both group flags, four releases | `design.md` §2 and §7 | 4 of 4 `search=False autotype=False` | 4 of 4 | yes | |
| The flags came through all 59 results unchanged | `design.md` §2 | all 72 `KeePassGate` group lines false and false | the same | yes | 72 counts the inputs and the second groups `CreateNewUuids` makes |
| A KeePassXC save adds the standard fields an entry lacks: three to the invented user entry, none to a store entry | `design.md` §3, table | `Notes`, `URL`, `UserName` added to `user entry`, nothing to any `KPG` entry, four releases | the same | yes | |
| KeePassXC 2.4.2 to 2.8.0 read and write back custom strings and their protection flag | `design.md` §3 | every store entry keeps its strings and flags | the same | yes | see the row on every store entry below |
| A profile record's `KPG.Data` is about 45 characters | `design.md` §3.1, table | `KPG.Data(45:d32882)` | 45 | yes | the table's other sizes come from `ground-inventory.md`, not from this probe: its stand-ins are 2,883 characters for an authority, 693 for a client record with one profile and 80 for a revocation, against the table's 2,903, 819 and about 200 |
| A deletion record reaches the other copy through Synchronize and through none of the other four methods, set D | `design.md` §3.2 and §5; `digest.md` §3 item 3 | set D into B: three records under Synchronize, none under the others | the same | yes | into A every method keeps A's own three records |
| No KeePassXC merge applies or copies a deletion record: the three entries A deleted come back into A, and B receives no record, four releases | `design.md` §3.2, §7 and §11; `digest.md` §2 | 4 of 4 in each direction | the same | yes | |
| An entry only one copy holds arrives in the other in every merge: five methods, four releases, both directions | `design.md` §3.2 | it arrives in all 54 merge cells | the same | yes | under `CreateNewUuids` it arrives in the second group, under a new identifier |
| After `keepassxc-cli` merges the stale copy into the revoking copy, the client's record is back in the store group beside its revocation entry and its own deletion record, four releases | `design.md` §3.2 | 4 of 4 | the same | yes | |
| The walk's database: 10,054 entries, 501 groups, 5,000 deletion records, a store of 53 entries | `design.md` §4 | the same | the same | yes | |
| One pass over the tree and the deletion records: 1.9 ms | `design.md` §4; `critique-security-and-fit.md` M1; `digest.md` §5 | 1.871 ms | 3.720 ms and 2.066 ms in two runs of `Reproduce.ps1`; 1.824 to 2.199 ms in five runs of `Sep.exe walk` on their own | yes, within the machine's spread | the first run of `Reproduce.ps1` was slower in every traversal, on a machine other measurements were sharing; the second and the five runs on their own bracket the record. Noise, not a claim that fails |
| The tree by identifier: 0.26 ms | `design.md` §4 | 0.262 ms | 0.382 and 0.276 ms; 0.273 to 0.301 ms | yes, within the spread | |
| The deletion records: 0.08 ms | `design.md` §4 | 0.075 ms | 0.097 and 0.080 ms; 0.076 to 0.091 ms | yes, within the spread | |
| Testing every entry for `KPG.Kind`: 1.6 ms more | `design.md` §4 | 1.600 ms | 2.656 and 1.900 ms; 1.648 to 1.761 ms | yes, within the spread | 1.6 ms is the walk that tests the string alone, its own traversal included; the pass costs about 1.5 ms more than the identifier walk and the records together (1.871 less 0.262 and 0.075 in the record) |
| Reading the store group's 53 entries: 0.02 ms | `design.md` §4 | 0.020 ms | 0.020 and 0.030 ms; 0.020 to 0.030 ms | yes | |
| About 0.19 microseconds an entry, so 0.1 ms for 500 entries | `design.md` §4 | | | not run | arithmetic on the 1.9 ms row, not a measurement at 500 entries |
| KeePass returns the same `ProtectedString` object on a second read of an unchanged entry | `design.md` §4 | `True` | `True` | yes | |
| Synchronize carries the prune: both directions of set D end with the pruned record gone and its deletion record present | `design.md` §5 | `client c9` gone, `bf654ed4` present, both directions | the same | yes | |
| 59 results: 30 KeePassLib merges, 24 `keepassxc-cli` merges, 4 KeePassXC saves, 1 chain | `design.md` §7 | 59: 30, 24, 4, 1 | the same | yes | |
| Every result is KDBX 4.0 | `design.md` §7 and §10; `digest.md` §5 | 66 of 66 files, the 7 inputs and the 59 results | the same | yes | |
| Every store entry in every result has the strings it was written with and the protection flag where it was written | `design.md` §7 | each of the 11 `KPG` titles carries one string signature in every file | the same | yes | a signature is each string's name, `[P]` when protected, its length and the first 3 bytes of its SHA-256, as the dump prints them |
| Synchronize, set D: the record, the pruned record and the hand-deleted revocation gone in both copies with their three deletion records, the new revocation and the stale copy's own client present | `design.md` §7, table | both directions | the same | yes | |
| Overwrite if newer, Overwrite existing, Keep existing, set D: into the copy that acted as Synchronize; into the stale copy the deleted entries kept, no deletion record, the revocation entry received | `design.md` §7, table; `digest.md` §3 item 3 | the same in all three | the same | yes | |
| Create new IDs: a second `KeePassGate` group under a new UUID holding the arriving entries under new UUIDs, beside the untouched store | `design.md` §7, table; `digest.md` §2 | 6 of 6 cells | the same | yes | |
| The same decision made in both copies at different times is one entry with one history item; made in the same second, one entry and no history | `design.md` §7, table | set I: `revoked c0` and `profile reader 1` each with one history item, in every identifier-matching KeePassLib cell and every KeePassXC cell; set T: `revoked c0` with none | the same | yes | the table adds that the history item holds the same content; the dump prints a history item's time and not its strings, so that part holds by construction, since both copies write the same strings, and is not shown by the output |
| KeePassXC adds `_LAST_MODIFIED` and `KPXC_RANDOM_SLUG` to the database's custom data in 2.8.0 | `design.md` §7, table | 2.7.12 and 2.8.0 add both; 2.4.2 and 2.6.6 add none | the same | yes | 2.7.12 adds them too, which the paper does not state |
| A KeePassXC merge and then a KeePassLib Synchronize with the stale copy equals a plain Synchronize | `design.md` §7, table | the chain's result equals set D into A under Synchronize | the same | yes | |
| Every file made and every result is 4.0, and KeePassXC 2.4.2 and 2.6.6 kept all of it | `design.md` §10 | yes | the same | yes | |
| In set I copy B's removal stands beside one entry for both copies' addition, so A's addition is gone | `critique-survival.md` §3 P2 | `profile reader 1` with one history item beside `profile-removed reader 1` | the same | yes | the probe names the kind `profile-removed`, where `design.md` §3.1 names it `removed` |
| Every `keepassxc-cli` used carries a valid signature from DroidMonkey Apps, LLC | `design.md`, "What was run for this report" | 4 `Valid` | 4 `Valid` | yes | `tools\KeePassXC.ps1` checks the same before the run |

## `survival\`: the survival critique's chains

**What it measures.** `scripts\surv.ps1 make` builds seven databases with invented content (AES-KDF
at one round, password `test`, so that kdbxweb opens them): `D0`, the store with authority `fpA`
and clients `c1` and `c2`; `P`, `c1` revoked as the design revokes; `S`, the stale copy; `X1`, the
store deleted the way KeePass deletes; `P1` and `Q`, the store made again under the same UUID with
authority `fpB` and client `c5`; `P2`, `c5` revoked. Its `dump` applies the design's fold from §4
and prints each client's verdict. `scripts\Run.ps1` runs chains A, B and C across
`keepassxc-cli` 2.4.2 and 2.8.0, pykeepass, kdbxweb 2.1.1 and KeePassLib's five methods. Cited by
`critique-survival.md` sections 1, 2, 3 (P1, P3, P4) and 7, and by `digest.md` sections 2 and 3.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-entries\survival\Reproduce.ps1
```

**Output.** Fresh: `survival\out\bases.txt` and `survival\out\chains.txt`. Recorded: the same names
under `survival\recorded\`.

**Runtime here**: 38 and 43 seconds in two runs, about two of them for `npm ci`.

**Changes made to run it here:**

1. `scripts\surv.ps1`: `$kpExe`, the product worktree's `.keepass\2.62\KeePass.exe`, became
   `Get-KeePassExe` from `tools\KeePass.ps1`, the same file by hash; `$scripts` and `$bin`, fixed
   paths into the agent's scratch directory, became `$PSScriptRoot` and `bin\` beside `scripts\`.
2. `scripts\Run.ps1`: `$Root` became `Split-Path -Parent $PSScriptRoot`; `$XcRoot` and the two
   paths into the KeePassXC ground report's downloads became `Get-KeePassXCCli`; `$KdbxWeb`, the
   others report's npm install in its own working directory, became `kdbxweb\node_modules\kdbxweb`.
3. `kdbxweb\package.json` and `kdbxweb\package-lock.json` were copied unchanged from the directory
   that install was made in, so that `npm ci` installs the same kdbxweb 2.1.1, `@xmldom/xmldom`
   0.7.13 and `fflate` 0.7.5.
4. `Reproduce.ps1` was added. It runs `npm ci`, then `scripts\surv.ps1 make` into `work\base\`, then
   `scripts\surv.ps1 dump` over the seven files into `out\bases.txt`, which the critic made by hand
   and recorded without the command (the order of the files is the record's), then
   `scripts\Run.ps1`, started with `pwsh.exe`.

**What the paper quotes, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| The bases fold as described: `D0` admits `c1` and `c2`; `P` admits `c2` only; `X1` serves nothing; `P1` and `Q` serve `fpB` and admit `c5`; `P2` serves `fpB` and admits nobody | `critique-survival.md` §7 | `bases.txt` | the same | yes | |
| Sequence A step 2: a `keepassxc-cli` merge of the revoking copy into the stale copy leaves `c1`'s record, the revocation entry and no deletion record, and `c1` is refused by the entry, 2.4.2 and 2.8.0 | `critique-survival.md` §2 and §3 P1 | both releases | the same | yes | |
| Sequence A step 3: pykeepass deletes `KPG revoked client c1` and writes no deletion record, 0 before and 0 after; the gate then admits `c1` | `critique-survival.md` §1 and §3 P1; `digest.md` §2 and §3 item 3 | `0 -> 0`, `c1: ADMITTED`, both releases | the same | yes | pykeepass is not pinned by the probe; it resolved to 4.2.0 in the record and here |
| Sequence B step 3: KeePassXC writes a deletion record for the revocation entry it deletes | `critique-survival.md` §3 P1; `digest.md` §2 | 2.4.2 step 2b: `deletion records: revoked:c1`, `c1` refused by it | the same | yes | |
| In 2.8.0 the second `rm` fails with exit 1, because the bin is named `Papierkorb`, and the revocation entry stays in the bin, where it still counts | `critique-survival.md` §7 | exits 0 and 1, the entry in `Papierkorb`, `c1` refused | the same | yes | the environment decides this cell: the bin's name comes from the locale, and the probe names the bin `Recycle Bin` |
| A Create new IDs import brings refusals that still count, `S2-pk+P-CreateNewUuids` | `critique-survival.md` §2 | `c1` refused by the revocation entry in the second group | the same | yes | |
| A copy damaged below is healed by a KeePass Synchronize with the revoking copy, chain A step 3 and chain C step 2 | `critique-survival.md` §2 | yes in every case run | the same | yes | |
| Two copies' entries both arrive in a merge, chains A, B and C | `critique-survival.md` §2 | yes | the same | yes | |
| P3 step 3: kdbxweb merges `P2` and `X1` in either direction to no store group, no `fpB`, no revocation of `c5`, with the group's deletion record; merging `P2` in again changes nothing; `Q` with `X1` drops the store too | `critique-survival.md` §3 P3 | `K1`, `K2`, `K3`, `K4` | the same | yes | |
| P3 step 4: a KeePassLib Synchronize of that result with `Q` brings the store back and `c5` is refused only by its own deletion record; Synchronize of `P2` with `X1` keeps the store and drops the group's record | `critique-survival.md` §3 P3 | `K1+Q-Sync`, `Q+K1-Sync`, `P2+X1-Sync` | the same | yes | the dump reads `c5: no record`: the Synchronize applied `c5`'s deletion record to the record `Q` held |
| P3 step 5: `keepassxc-cli` 2.8.0 brings the revocation to `Q` only as an entry; kdbxweb then drops the store; `c5` is admitted in all eight KeePassLib results and in the Create new IDs import into `Q` | `critique-survival.md` §3 P3; `digest.md` §3 item 10 | 8 of 8, and `Q+K5-CreateNewUuids` | the same | yes | `K5+Q-CreateNewUuids`, the import the other way, serves nothing |
| P4: pykeepass deletes the group `KeePassGate` in the revoking copy and writes no deletion record | `critique-survival.md` §3 P4 | `1 -> 1`, the one record being `c1`'s from the revocation | the same | yes | |
| P4: every KeePassLib method that merged the stale copy into it brought the store back, served `fpA` and admitted `c2`, with only `c1` refused | `critique-survival.md` §3 P4 | 4 of 5 methods; under `CreateNewUuids` (`P3+S-CreateNewUuids`) the store came back as a group under a new UUID and nothing was served | the same | differs | **the claim is wider than its own output**: one of the five methods fails closed rather than serving. The output is reproduced exactly; the sentence is not. The other direction, `S+P3`, admits `c1` under four methods, which the paper does not claim either way |
| The tools: `keepassxc-cli` 2.4.2 and 2.8.0 with valid signatures from DroidMonkey Apps, LLC, pykeepass 4.2.0, kdbxweb 2.1.1 | `critique-survival.md` §7 | as stated | the same | yes | |

## `security-and-fit\`: the security and fit critique's probes

**What it measures.** `scripts\import-plant.ps1` writes an invented Bitwarden JSON file whose one
item carries `KPG.Kind` and `KPG.Data` as custom fields, runs it through KeePass 2.62's own
`BitwardenJson112` format provider into an in-memory database, and merges that into another with
`CreateNewUuids`, as File then Import does for a format without identifiers. `scripts\pk_delete.py`
makes a database with pykeepass, deletes a store-like entry, saves and counts the
`DeletedObject` elements. `scripts\count-affected-tests.ps1`, written on this branch, counts the
files, lines and `[Test]` attributes of the test files the critic listed, at the product's commit.
Cited by `critique-security-and-fit.md` S1, S4 and M8, and by `digest.md` sections 4 and 5.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-entries\security-and-fit\Reproduce.ps1
```

**Output.** Fresh: `security-and-fit\out\import-plant.txt`, `out\pk-delete.txt` and
`out\affected-tests-count.txt`. Recorded: `security-and-fit\recorded\import-plant.txt`,
`pk-delete.txt` and `affected-tests.txt`, the critic's list, which the critique cites at the root
of its directory and which no run writes.

**Runtime here**: 10 and 9 seconds in two runs.

**Changes made to run it here:**

1. None to `scripts\import-plant.ps1` or `scripts\pk_delete.py`. The first takes the KeePass it
   loads as `-KeePassExe`, and `Reproduce.ps1` passes `Get-KeePassExe`, the pinned KeePass 2.62;
   the record names `KeePass assembly: 2.62.0.0` and not the path it was given.
2. The critic's work directories `work-import` and `work-pykeepass` are `work\import` and
   `work\pykeepass` here, because `.gitignore` covers `work\` and not those names. Both are
   arguments, so no source changed.
3. `affected-tests.txt` moved from the directory's root into `recorded\`.
4. `scripts\count-affected-tests.ps1` and `Reproduce.ps1` were added. The first is a check of the
   critique's two counts, not the critic's probe, since the critic recorded no command for them.

**What the paper quotes, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| A Bitwarden JSON item carrying `KPG.Kind = retired` and `KPG.Data = {"v":"1","authority":"<64 hex>"}`, imported with the format provider and a `CreateNewUuids` merge, gives an entry holding both strings unchanged, in a user group | `critique-security-and-fit.md` S1; `digest.md` §4 | both strings unprotected and unchanged, in `Shopping` | the same | yes | the entry's identifier is drawn at random |
| The format declares no identifiers, which is why File then Import merges it with `CreateNewUuids` | `critique-security-and-fit.md` S1 | `SupportsUuids=False` | the same | yes | that the import uses `CreateNewUuids` for such a format is read from `ImportUtil.cs`, not run |
| pykeepass deletes an entry and saves with no deletion record: 0 `DeletedObject` elements | `critique-security-and-fit.md` S4 | 0 | 0 | yes | pykeepass resolved to 4.2.0 in the record and here; the store-like entry is made by pykeepass in a group of its own, not the design's derived group |
| 49 test files | `critique-security-and-fit.md` M8; `digest.md` §5 | 49 in `affected-tests.txt` | 49 exported at `a42e38aa` | yes | the list is the record's; see below for how it was chosen |
| About 26,000 lines in them | `critique-security-and-fit.md` M8; `digest.md` §5 | no count recorded | 26,434 | yes | counted as newline characters, as `wc -l` does |
| 734 test methods in them | `critique-security-and-fit.md` M8 | no count recorded | 734 lines holding `[Test]` | yes | counting `[TestCase]` and the other test attributes as well gives 836 lines |
| One pass over a database of 10,054 entries: 1.9 ms | `critique-security-and-fit.md` M1 | 1.871 ms | see the design's table | yes, within the spread | the design's walk, `design\out\walk.txt` |

## PowerShell 7

`spikes\storage\probes\README.md` states that the PowerShell probes were measured with Windows
PowerShell 5.1. **The two `scripts\Run.ps1` here were not**, and cannot have been: their
`Invoke-Cli` sets `ProcessStartInfo.StandardInputEncoding`, which exists in .NET from Core 2.1 on
and not in .NET Framework 4.8, so Windows PowerShell stops at that line with "The property
'StandardInputEncoding' cannot be found on this object", which is what the first run here did. The
record agrees: `design\recorded\walk.txt` carries no byte order mark, which
`Set-Content -Encoding utf8` writes in 5.1 and not in 7. So `Reproduce.ps1` starts both with
`pwsh.exe`. Nothing measured runs in that shell: the design's KeePassLib work runs in `Sep.exe`, a
.NET Framework 4.8 program, and the survival critique's `scripts\Run.ps1` starts `scripts\surv.ps1`
in `powershell.exe`, which compiles `Surv.cs` with the .NET Framework's CodeDom.
`scripts\import-plant.ps1` did run in 5.1: its record names the runtime `4.0.30319.42000`.

## Claims nothing here can vouch for

- **Which 49 test files**, `critique-security-and-fit.md` M8: the critique states that they "touch
  the database-level store or its merge behavior by name", and the critic recorded the list and not
  the search that made it. The counts over the list reproduce; the choice of files does not. The
  closest of the searches tried here, run from the branch root, finds 51 files, 46 of them on the
  list, so the list is near such a search and is none of the searches tried:

  ```powershell
  git grep -lE "IKeyValueStore|CustomDataStore|InMemoryKeyValueStore|KPG\.Authority|KPG\.Revoked|KPG\.Profiles|KPG\.RetiredAuthority|KPG\.RenewalRefused|AuthorityKeys|MergeIn" a42e38aa -- tests
  ```

- **42 source files**, `critique-security-and-fit.md` "What it costs in this code base" and
  `digest.md` §5: marked as read from a search of 42 source files, with neither the list nor the
  search recorded anywhere the critic left.
- **The sizes in `design.md` §3.1** other than the profile record's 45 characters: the paper takes
  them from `ground-inventory.md`, so they belong to that family and not to these probes.

## Defects and deviations found

None changes a result.

- **P4's sentence is wider than its output**, as the table shows: four of five KeePassLib methods,
  not every method.
- **The design's probe names a profile's removal `profile-removed`** where `design.md` §3.1 names
  the kind `removed`. The derived identifiers therefore differ from the identifiers the design
  specifies, which changes no merge, since every merge treats them as entries like any other.
- **The design's dump does not show a history item's content**, so "one history item holding the
  same content" in `design.md` §7 is shown as one history item and not as its content.
- **The survival probe copies KeePass's permanent delete of a group** rather than running it, as
  `critique-survival.md` §5 item 7 states, and **the design's walk was measured in a console
  process**, as `design.md` §4 and `critique-security-and-fit.md` M1 state. Both are stated limits
  of the probes, not defects in them.

## Not carried

- **Every database**: 92 `.kdbx` files under the design's `work\`, 67 under the survival
  critique's `work\`, and `A.kdbx` and `A-before-delete.kdbx` under `work-pykeepass`. Each run
  makes them again.
- **Build output**: the design's `probe\obj\` and `probe\out\`, which holds `Sep.exe` and copies of
  KeePass's `KeePass.exe`, `KeePass.exe.config` and `KeePass.XmlSerializers.dll`, and the survival
  critique's `bin\Surv.dll`.
- **The 24 `merge.txt` files** under the design's `work\`: what `keepassxc-cli merge` printed in
  each cell, localized and naming the scratch paths of each file, and quoted by no paper.
- **The design's `scripts\fix.py`**: a script that edited the wording of the paper itself, not a
  probe, and needed neither to run a probe nor to check a claim.
- **`work-import\invented-bitwarden.json`**: `scripts\import-plant.ps1` writes it on every run.
- **The kdbxweb install** the survival critique used, under the others report's working
  directory: its `package.json` and `package-lock.json` are carried, the installed modules are not.

## Machine paths replaced

The recorded outputs held no machine path, user name or session identifier, and nothing in them was
changed. In the sources, the full paths to the agents' scratch directories, to the KeePassXC
downloads and to the product worktree's KeePass, in the two `scripts\Run.ps1`, `scripts\surv.ps1`
and `probe\Sep.csproj`, became the paths relative to the branch and the tools listed under each
probe's changes. A search of this directory for `C:\`, `Users\`, `AppData`, `scratchpad`, `Temp\`,
the session identifiers and the user name finds only this sentence, and the user name only as the
project's abbreviation in `kpg-walk-never-saved.kdbx`, the name the walk gives a database it never
saves. The runs' databases and build output were removed after the runs; their fresh output stays
in each `out\`, which is not committed.
