# Probes for `wildcard`

The probes behind the option that keeps everything the plugin stores as one sealed snapshot in an
entry of its own, replaced whole, with deletion records as witnesses: its design,
`papers\options\wildcard\design.md`, its two critiques, `critique-survival.md` and
`critique-security-and-fit.md` beside it, and the numbers `digest.md` takes from them. The option
has no formal critique. One directory per paper, as `spikes\storage\probes\README.md` lays out:

| Directory | Paper | What runs |
| --- | --- | --- |
| `design\` | `design.md` | `probe\Program.cs` built as `Wc.exe` over KeePassLib 2.62, driven by `scripts\Run.ps1` with four `keepassxc-cli` releases and pykeepass, then kdbxweb 2.1.1, KeePass 2.47's KeePassLib, a timing and a check of the join's laws |
| `survival\` | `critique-survival.md` | the design's `Wc.exe` unchanged, `scripts\kpops.ps1` over KeePassLib 2.62, kdbxweb 2.1.1 and `keepassxc-cli` 2.4.2 and 2.8.0, driven by `scripts\Run.ps1` and `scripts\Run-e4.ps1` |
| `security-and-fit\` | `critique-security-and-fit.md` | `scripts\probe.ps1` over KeePassLib 2.62, and a count over the product's sources |

Every probe was run again from this branch on 2026-11-15, each three times, on the machine the
record was made on and under the same `de-DE` locale.
**Every output came out the same as its record** once what differs between two runs by design is
taken away, the timings apart, which came out within the machine's spread or faster. Every number
the papers quote from these probes reproduced, with three exceptions and one sentence worded wider
than its output (`design.md` §8.2, the chains). **The three do not hold, and each is contradicted
by the record itself**, not only by this run: that every file KeePassXC wrote was KDBX 4.0
(`design.md` §11), the Delete Duplicate Entries row (`design.md` §9) and the survival critique's
reuse of that row (§2.8). What no probe here covers is listed at the end.

## What a run needs

- Windows PowerShell 5.1 as `powershell.exe`, which runs every command below, and PowerShell 7 as
  `pwsh.exe`, which runs the two `scripts\Run.ps1` (see "PowerShell 7" below).
- The .NET SDK for `dotnet build`, uv, Node.js with npm, and git.
- KeePass 2.62 and 2.47, which `. .\tools\KeePass.ps1; Install-KeePass` and
  `Install-KeePass -Version 2.47` provide from the branch root, the second from the pin
  `keepass.sha256` holds for `spikes/revocation-merge`; KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0,
  which `tools\KeePassXC.ps1` provides when a probe asks for a release.
- The product's commit in the repository's history, for the counts in `security-and-fit\`, which
  `tools\Product.ps1` reads.

Each `Reproduce.ps1` writes only into gitignored places: `out\` and `work\` beside it, the design's
`probe\out\`, `probe\obj\` and `kdbxweb\node_modules\`, and through the tools `.keepass\`,
`.keepassxc\` and `.product\` when something is not there yet. **Every database is made by the
run, in `work\`, and none is kept here.**

## Comparing a run with the record

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\wildcard\compare.py spikes\storage\probes\wildcard\design
```

The same with `survival` or `security-and-fit` as the last argument. `compare.py` was written on
this branch. It takes away only what differs between any two runs of the same code: identifiers a
run draws at random, the order in which the probe prints a list of them, which follows their random
bytes, the times in milliseconds and the clock at the moment of a run. An identifier both files
share is kept, so a derived identifier that moved would still show, and the renaming is one to one,
so two places that held one identifier still hold one name after it. It printed "the same after
normalizing" for every recorded file, after each run. What it took away:

| File | Taken away |
| --- | --- |
| `design\out\summary.txt`, `merges.txt`, `kdbxweb.txt`, `keepass247.txt` | the snapshots' identifiers (`S2:4b1e04`), which carry six random bytes after the mark and the sequence number; the identifiers of copies a `CreateNewUuids` merge makes; the state digests, which hash the rules under their nodes' random identifiers; the order of two snapshots of one sequence number in a list and of two rules in `rules=[...]`; the clock the plugin's next write stamps on its snapshot (`mod=11-15T...`) |
| `design\out\bench.txt` | the timings, discussed under the design's table |
| `survival\out\run.txt`, `e4.txt`, `group-records.txt` | the same identifiers, the random groups and snapshots `scripts\kpops.ps1 records` prints in full, and the clock Delete Empty Groups, the critic's deletions and the next write stamp |
| `security-and-fit\out\probe.txt` | the random identifiers of the user entries and of the imported copies |

`design\out\lattice.txt` was the same as its record byte for byte. The witnesses' identifiers, the
store group's and the security probe's fixed identifiers are derived or written down, and stayed.

`design\scripts\claims.py`, also written on this branch, counts in one directory what the design's
table rows below quote, so that the record and a fresh run are held to the paper with the same
code. Run over `design\recorded` and over `design\out` it printed the same 93 lines once the
identifiers are taken away:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\wildcard\design\scripts\claims.py spikes\storage\probes\wildcard\design\recorded
```

## `design\`: the design's matrix, saves, cost and laws

**What it measures.** `Wc.exe make` builds seven pairs of databases with invented content
(Argon2d, 2 iterations, 1 MiB): set U, where copy A revokes a client and so replaces the snapshot;
C, where both copies write; H, as U with the new snapshot then deleted for good in A; R, as U with
it in the recycle bin; D, as U with the old snapshot's text edited by hand in B; X, where each copy
makes a store of its own; K, where A recreates the authority while B pairs a client.
`scripts\Run.ps1` merges each pair with KeePassLib's five methods and four `keepassxc-cli`
releases in both directions, saves with each release, runs twelve chains and five next writes, the
hand operations of `keepassxc-cli` 2.6.6 and 2.8.0, Delete Duplicate Entries after Create new IDs,
and pykeepass. `scripts\Run-kdbxweb.ps1` makes the sets again with AES-KDF and merges five of them
with kdbxweb; `scripts\Run-kp247.ps1` saves three of those files with KeePass 2.47's KeePassLib.
`Wc.exe bench` times the reading at 1,000, 10,000 and 50,000 entries, and `Wc.exe lattice` checks
the join's laws on random histories. Cited by `design.md` sections 1, 2, 3.1, 3.2, 3.3, 5.1, 5.3,
6, 8.1, 8.2, 9, 11, 12, 14 and 16, by both critiques as `[M wildcard]` and `[M design]`, and by
`digest.md` sections 2, 3 and 5.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\wildcard\design\Reproduce.ps1
```

**Output.** Fresh: `design\out\summary.txt`, `merges.txt`, `kdbxweb.txt`, `keepass247.txt`,
`bench.txt` and `lattice.txt`, and two with no recorded twin: `checks.txt`, from
`scripts\checks.ps1`, and `keepass247-save.txt`, what the 2.47 save printed. Recorded: the first six
names under `design\recorded\`. The timing alone is `design\probe\out\Wc.exe bench` once the
command has built it.

**Runtime here**: 127 and 117 seconds for the whole command, the build included; `scripts\Run.ps1`
alone, the matrix, took 106 seconds in the run between them.

**Changes made to run it here:**

1. `probe\Wc.csproj`: the `HintPath` to KeePass became `$(KeePassExe)`, which
   `spikes\storage\Directory.Build.props` defines as the pinned `.keepass\2.62\KeePass.exe`, the
   same file by hash as the file the record was built against. `probe\Program.cs` is unchanged.
2. `scripts\Run.ps1`: `$Root`, a fixed path into the agent's scratch directory, became
   `Split-Path -Parent $PSScriptRoot`; `$XcRoot` and the four paths into the KeePassXC ground
   report's downloads became `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1`, which
   provides the same pinned archives and checks their signatures. `scripts\kw.js` and
   `scripts\pk.py` are unchanged.
3. `scripts\Save-WithKeePass.ps1` was copied unchanged from the branch `spikes/revocation-merge`
   at `11a9aa12`, `spikes\revocation\Save-WithKeePass.ps1`, the script `design.md` §16 names.
4. `kdbxweb\package.json` and `kdbxweb\package-lock.json` were copied unchanged from the directory
   the others ground report installed kdbxweb in, so that `npm ci` installs the same kdbxweb 2.1.1.
   The survival critique uses this install too.
5. `scripts\Run-kdbxweb.ps1` and `scripts\Run-kp247.ps1` were written on this branch, because the
   agent kept `out\kdbxweb.txt` and `out\keepass247.txt` with the databases that made them, in
   `work-kw\` and `work-kp247\`, and not the commands. Each runs what the record's labels and those
   files' names and times imply, and records this in its header; one choice in them is read off
   nothing: "into A" is taken as A loaded and B merged into it, which no output can tell apart,
   since kdbxweb gave the same result both ways in the record and here. Their databases go to
   `work\kw\` and `work\kp247\`, which `.gitignore` covers.
6. `Reproduce.ps1`, `scripts\checks.ps1` and `scripts\claims.py` were added. The first holds the
   steps the record was made with and measures nothing; the other two are checks written here and
   are named as such wherever a row below rests on them. `scripts\Run.ps1` is started with
   `pwsh.exe`; see "PowerShell 7".

**What the papers quote, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| Every `keepassxc-cli` used carries a valid signature from DroidMonkey Apps, LLC | `design.md` §16 | 4 `Valid` at the top of `summary.txt` | 4 `Valid` | yes | `tools\KeePassXC.ps1` checks the same before the run |
| A deletion that leaves no deletion record is what a script over pykeepass makes | `design.md` §1 and §14.3; `digest.md` §3 item 6 | pykeepass deletes the newest snapshot of a file that took it through OverwriteIfNewer: `records S[] R0`, in force at `S1`, nothing refused; deletes the only snapshot of `U-A`: `S2` not among the records | the same | yes | pykeepass is not pinned by the probe; it resolved to 4.2.0 here (`checks.txt`), and the record names no version |
| Where the file holds the witness, the revocation still holds, for a file whose only snapshot was deleted that way | `design.md` §14.3 | `GONE`, `c1(witness)` | the same | yes | |
| A KeePassXC merge never copies a deletion record, and KeePass's four other methods never copy a record | `design.md` §2, table; `digest.md` §2 | set U into B: `S[] R0` under OverwriteIfNewer, OverwriteExisting, KeepExisting and Create new IDs and in all four releases; `S[S1] R1` under Synchronize | the same | yes | into A each copy keeps its own records |
| kdbxweb takes the union of the deletion records | `design.md` §2, table | set H, both directions: `S[S1 S2] R1` | the same | yes | |
| 1 MiB of a protected string came through KeePassLib 2.62 and four KeePassXC releases with its seal intact | `design.md` §2, table, and §8.1; `digest.md` §2 | `big: big.kdbx text 1052608 chars, 1250 extra records`; in force as KeePassLib wrote it and after `keepassxc-cli add` in four releases | the same | yes | 1,052,608 characters is the stored string, document and seal together (§16) |
| Two copies that each made the store group hold one group after a merge, set X, 16 cells | `design.md` §2, table, and §3.1, table | nothing: the dump prints a snapshot's path, which two groups of one name print alike | `checks.txt`: one group under the fixed identifier and one group named `KeePassGate` in all 16 cells | yes, by a check written here | under Create new IDs the result holds a second group named `KeePassGate` under a new identifier, which the paper does not count among the 16 |
| KeePassXC adds empty `Notes`, `Password`, `URL` and `UserName` at its first save without moving the entry's time: all 116 snapshot entries in the files it wrote | `design.md` §3.1, table | 120 snapshot entries in the dumps of KeePassXC's results; the 4 in `H into B` carry no added fields, because that merge had nothing to bring and left the target unwritten; the other 116 carry the four empty fields; 112 of those kept their time | the same | yes | the 4 whose time moved are the hand operations that move it by design: the edits in 2.6.6 and 2.8.0, and 2.8.0's `rm` once and `mv`. The 8 merge entries with one history item are the edited `S1` of set D, which had it before the merge |
| Moved into `General` by `keepassxc-cli mv` and into the recycle bin by `rm`, it stayed in force, 2.6.6 and 2.8.0 | `design.md` §3.1 and §9, table; `digest.md` §2 | in force in all four | the same | yes | 2.8.0 names its bin `Papierkorb`, after the locale, which is `de-DE` in the record and here; 2.8.0's `rm` and `mv` both write KDBX 4.1, and its `mv` adds a history item (see the §11 row below) |
| The base snapshot is 4,968 characters | `design.md` §3.2 | `text=4968P` in `## base` | the same | yes | the stored string: the document, a line break and the 44 characters of the seal, since the probe keeps both in one string (§16). The 2,903 and 819 characters beside it are the probe's own constants |
| The database holding it is 7.1 KB | `design.md` §3.2 | nothing | `work\base\base.kdbx`: 7,157, 7,141 and 7,157 bytes in three runs | yes, by a check written here | the agent's own file was 7,141 bytes; the size moves by a few bytes with the random identifiers and key |
| With 22 records and 100 rules it is 37,299 characters | `design.md` §3.2 and §5.1; `digest.md` §2 ("5 to 37 KB") | `snapshot text 37299 chars` in `bench.txt` | the same | yes | the 5 KB end is `U-A`'s 5,042 characters, `text=5042P` |
| A snapshot copied by Create new IDs is told apart, 12 cells | `design.md` §3.3 | 12 of 14 Create new IDs cells `UNDECIDED: a copy of a snapshot under another identifier` | the same | yes | the other two are in the §8.2 row below |
| The cost table, §5.1 | `design.md` §5.1; `digest.md` §5 | one run | four runs | see below | |
| 40,000 checks over random histories, none failed | `design.md` §5.3 | `lattice: 40000 checks, 0 failed` | the same, three times | yes | the checks hold the join to its laws as the probe codes it, on rule numbers alone; `critique-survival.md` §2.3 and §2.9 describe what they leave out |
| Two copies changing one rule leave both documents standing, set C, 16 cells | `design.md` §5.3 | 16 of 16 `E1 n2 SIBLINGS` under KeePass's four methods and KeePassXC's four releases, both directions | the same | yes | |
| An old snapshot beside a new snapshot changes nothing, sets U, R and D | `design.md` §5.3 | into A by KeePassXC the old snapshot is present and named, into B by the three import methods and KeePassXC it is present and replaced; one joined state per set | the same | yes | |
| Set K: the records the recreated authority dropped came back under a retirement that held in all 16 cells | `design.md` §5.3 | 16 of 16 with `retired=[fpA]` and `clients=[c1,c2,c3]` | the same | yes | |
| Synchronize carries the record and deletes the old snapshot in the other copy, set U | `design.md` §6 | `KeePass U into B Synchronize`: present `[S2*]`, `records S[S1] R1` | the same | yes | |
| Under a KeePassXC merge the old snapshot comes back into the copy that deleted it, beside its record, and reads as named | `design.md` §6 | set U into A, four releases: `[S1!named S2*]` | the same | yes | |
| The table of set H: gone into A by KeePass's four methods, into B by Synchronize, into A by KeePassXC with `B`'s snapshot named, both ways by kdbxweb; `B` as it was under the three import methods and KeePassXC into B; the same by `keepassxc-cli rm` twice | `design.md` §6, table | 4, 1, 4 and 2 `GONE`; 3 and 4 in force at `S1`; `rm twice, then the stale copy merged in`: `GONE`, `[S1!named]`, 2.6.6 and 2.8.0 | the same | yes | |
| In every gone row the revoked certificate was still refused by its witness alone | `design.md` §6 and §9, table | every `GONE` line of the sets, chains, hand deletions, pykeepass and kdbxweb refuses `c1(witness)`: 19 of the 20 in `summary.txt` and both in `kdbxweb.txt` | the same | yes | the twentieth is the input `D-B`, which revoked nothing |
| A deletion record that names nothing survives a KeePassXC save in four releases, a pykeepass save and a kdbxweb save | `design.md` §6 | `R1` after `add on U-A` in four releases, after the pykeepass resave and after the kdbxweb resave | the same | yes | |
| KeePass 2.62 keeps the entry, its strings, the protection flag and deletion records that name nothing, and writes KDBX 4.0 | `design.md` §8.1, table | every KeePassLib result dumped: the store string protected (`P`), records kept, `kdbx 4.0` | the same | yes | |
| KeePassXC, after `keepassxc-cli add`: the seal held on 5 KB and on 1 MiB, the records and witnesses there, four empty fields added, two keys of its own in 2.7.12 and 2.8.0, KDBX 4.0, 8 files | `design.md` §8.1, table | in force in all 8, `S[S1] R1` on `U-A`; `dbCustomData=2` for 2.7.12 and 2.8.0 and 0 for 2.4.2 and 2.6.6; `kdbx 4.0` in all 8 | the same | yes | |
| KeePass 2.47 keeps all of it in a KDBX 3.1 file, 3 files, read back by KeePassLib 2.62 with the seal holding on 5 KB and on 1 MiB and every record and witness there | `design.md` §8.1, table, and §11 | `keepass247.txt`: three files `kdbx 3.1`, in force; `U-A` with `S[S1] R1` and `c1` refused, `C-B` with `R1` and `c2` refused, the 1 MiB file in force | the same | yes | made by the reconstruction `scripts\Run-kp247.ps1`; `keepass247-save.txt` shows KeePassLib 2.47 opening and saving each file |
| kdbxweb keeps all of it, and KeePass can open the file it saves | `design.md` §8.1, table | `kdbxweb resave of U-A`: in force, `S[S1] R1`, `c1` refused, read by KeePassLib 2.62 | the same | yes | made by the reconstruction `scripts\Run-kdbxweb.ps1` |
| pykeepass 4.2.0 keeps all of it | `design.md` §8.1, table, and §16 | `pykeepass resave of U-A`: in force, `S[S1] R1` | the same; 4.2.0 resolved | yes | see the pykeepass row above |
| 70 KeePassLib cells, 56 `keepassxc-cli` cells, 10 kdbxweb cells | `design.md` §8.2; `critique-survival.md` §1 ("136 merge cells") | 70, 56 and 10 lines | the same | yes | |
| KeePass's four matching methods, sets U, C, R, D and K: in force in all 40 cells, one joined state both ways, every revocation, the retirement, the profile taken off and both rule documents kept; Synchronize removes the replaced snapshot from the other copy, the three others leave it, replaced | `design.md` §8.2, table; `digest.md` §2 | 40 of 40 in force, one joined state per set; set U into B: `[S2*]` under Synchronize, `[S1 S2*]` under the other three | the same | yes | |
| KeePassXC 2.4.2 to 2.8.0, the same sets: in force in all 40 cells, the same joined state as KeePass's, the replaced snapshot back beside its record and named, no record copied | `design.md` §8.2, table; `digest.md` §2 | 40 of 40, the joined state equal to KeePass's in each set | the same | yes | |
| Set X, either program: undecided in all 16 cells, two stores | `design.md` §8.2, table | 16 of 16 `snapshots of 2 stores` | the same | yes | |
| Create new IDs: undecided in 12 of 14 cells, behind in one cell (set D), and in the fourteenth the source held no snapshot | `design.md` §8.2, table; `digest.md` §2 | 12 `UNDECIDED`, `BEHIND` for D into A, in force for H into B | the same | yes | |
| A KeePassXC result, then KeePass Synchronize with the other copy: the same joined state as either merge alone, sets U, C and H, 12 runs | `design.md` §8.2, table | 12 runs: U and C in force with the state both merges give alone; H gone in all four | the same | differs | **the claim is wider than its output.** The output is reproduced exactly. For set H, KeePassXC's merge into B alone left B in force at its old snapshot, while the chain through it ends gone, as KeePass's Synchronize alone does, so "either" holds for U, C and the H chains into A, not for the two H chains into B |
| Then the plugin's next write: one snapshot, the same state, the others deleted and recorded, 5 runs | `design.md` §8.2, table | 5 of 5: one `S3`, the joined state unchanged, every earlier snapshot among the records | the same | yes | |
| kdbxweb, sets U, C, H, D and K: the same outcomes as KeePass's Synchronize in all 10 cells | `design.md` §8.2, table; `critique-survival.md` §7 | verdict and joined state equal to Synchronize's in 10 of 10 | the same | yes | the files differ in set D: Synchronize keeps the edited `S1`, newer than the record naming it, and drops the record, while kdbxweb removes `S1`, as the paper states it does "whatever the times" |
| Deleted to the recycle bin: in force, `rm` once and set R, 16 cells | `design.md` §9, table | 2 of 2 and 16 of 16 | the same | yes | |
| `KPG.Store` edited by hand, with a standing snapshot that replaced it: nothing, set D, 16 cells | `design.md` §9, table | 16 of 16 in force | the same | yes | |
| The title edited by `keepassxc-cli edit`: nothing | `design.md` §9, table | in force, 2.6.6 and 2.8.0, with one history item | the same | yes | |
| An import with Create new IDs: undecided | `design.md` §9, table | the 12 cells above | the same | yes | the probe imports the other copy, not the database into itself; a self-import is the security critique's P1 |
| Then Delete Duplicate Entries: the original deleted and the copy left, "5 entries removed, the snapshot under its own identifier among them" | `design.md` §9, table; `digest.md` §2 | `Delete Duplicate Entries removed 4`, twice, and **both snapshots still present** in each, the original under its own identifier and the copy | the same | **no** | the run imported the other copy, whose snapshot differs from the target's, so no two snapshots were equal. What the row states is what the security critique's P1 measured on a self-import, with 3 entries removed (below) |
| Every file the probe, the four KeePassXC releases, pykeepass and kdbxweb wrote was KDBX 4.0; nothing here is a 4.1 feature, "no previous parent" | `design.md` §11 | 180 of the 182 dumps at `kdbx 4.0`; the other 2 at `kdbx 4.1` are KeePassXC 2.8.0's `rm` once and `mv`; kdbxweb's files are not dumped | the same dumps; `checks.txt`: the 11 kdbxweb files at 4.0, and 2.8.0's `mv.kdbx` at 4.1 with the store group as the snapshot entry's previous parent group | **no** | after the `mv`, KeePassXC 2.8.0 has written the entry's previous parent group, a 4.1 field; the `rm` once is 4.1 in the dump, and its file is overwritten by the second `rm` before the check can read it. Every merge and `add` result is 4.0, and the plugin writes no 4.1 field; a person moving the store entry in KeePassXC 2.8.0 makes the file 4.1 |
| Pairings and profile names from two copies both survive, sets C and K | `design.md` §12, table | set C: `c3`, A's pairing, and `writer:2(off)`, B's, in every matching cell; set K: `c3` in every cell | the same | yes | |
| The whole matrix takes under two minutes | `design.md` §16 | nothing | 106 seconds for `scripts\Run.ps1` | yes | |
| KeePass 2.47 checked again against the spike's pin and its Authenticode signature | `design.md` §16 | nothing | `tools\KeePass.ps1` checks both when it provides 2.47 and every time it is asked for it | yes | |

**The cost table**, `design.md` §5.1, against `bench.txt`; `digest.md` §5 quotes it as 0.005 to
0.19 ms a request and up to 30 ms for the whole pass. The paper's value, the record's, and the range
of the fresh runs: five whole runs, two in `Reproduce.ps1` and three of `Wc.exe bench` on their
own, and for the pass looking for the string and the seal three more whose other columns were not
kept:

| Entries | Column | Paper | Recorded | This run | Reproduced |
| --- | --- | --- | --- | --- | --- |
| 1,000 | step 1, store group and bin by identifier | 0.005 ms | 0.0049 | 0.0046 to 0.0051 | yes |
| 1,000 | step 2, deletion records | 0.08 ms | 0.0797 | 0.052 to 0.060 | differs, noise |
| 1,000 | whole pass by identifier | 0.07 ms | 0.070 | 0.064 to 0.076 | yes |
| 1,000 | whole pass looking for the string too | 0.2 ms | 0.265 | 0.17 to 0.22 | yes |
| 1,000 | unprotect and check one seal | 0.04 ms | 0.035 | 0.032 to 0.043, and 0.110 and 0.062 in `Reproduce.ps1` | yes |
| 10,000 | step 1 | 0.03 ms | 0.0344 | 0.026 to 0.034 | yes |
| 10,000 | step 2 | 0.06 ms | 0.0604 | 0.052 to 0.060 | yes |
| 10,000 | whole pass by identifier | 0.28 ms | 0.283 | 0.22 to 0.25 | differs, noise |
| 10,000 | whole pass looking for the string too | 2.3 ms | 2.257 | 1.7 to 2.0 | differs, noise |
| 10,000 | unprotect and check one seal | 0.04 ms | 0.046 | 0.035 to 0.046 | yes |
| 50,000 | step 1 | 0.19 ms | 0.1876 | 0.14 to 0.16 | differs, noise |
| 50,000 | step 2 | 0.07 ms | 0.0700 | 0.051 to 0.059 | differs, noise |
| 50,000 | whole pass by identifier | 2.5 ms | 2.454 | 1.5 to 2.3 | differs, noise |
| 50,000 | whole pass looking for the string too | 22 to 30 ms | 29.759 | 20.2 to 23.3 | yes |
| 50,000 | unprotect and check one seal | 0.04 ms | 0.037 | 0.035 to 0.042 | yes |

Every value marked "differs" is below the record in every fresh run: where this machine disagreed
with the record, it was faster. Step 2 does the same work at every size, a pass over the same 5,000
deletion records, and the record's own three values for it run from 0.060 to 0.080 ms, which is the
spread of the machine the record was made on while other measurements shared it. So each "differs"
is noise of the environment and not a claim that fails: every conclusion the paper draws from the
table, a request in well under a millisecond and a whole pass of tens of milliseconds at 50,000
entries, holds in every run. Two of the paper's values are not its record's: 0.2 ms rounds a
recorded 0.265 down, and the 22 in "22 to 30" comes from a run that left no record (see the end).
The two slower seals at 1,000 entries were the first measurement of the first `bench` in each
`Reproduce.ps1`; the six runs on their own gave 0.032 to 0.043. The probe prints its milliseconds
with a decimal comma, after the locale.

## `survival\`: the survival critique's sequences

**What it measures.** `scripts\Run.ps1` makes the design's seven sets again with AES-KDF at one
round, so that kdbxweb opens them, and runs sequence e1, Delete Empty Groups while the snapshot sits
in the recycle bin, then the plugin's next write, then kdbxweb merges both ways with KeePass's
Synchronize and KeePassXC as controls; e2, the store group deleted for good and a new store made
under the same group identifier; and e3, the newest snapshot arriving through a merge that carries
no deletion records and then deleted for good with a record. `scripts\Run-e4.ps1` runs e4, set C
with A's head in the bin and the store group removed by Delete Empty Groups, then kdbxweb.
`scripts\kpops.ps1` holds the KeePassLib operations. Cited by `critique-survival.md` sections 1, 2,
4, 5 and 9, and by `digest.md` sections 2 and 3.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\wildcard\survival\Reproduce.ps1
```

**Output.** Fresh: `survival\out\run.txt`, `e4.txt` and `group-records.txt`. Recorded: the same
names under `survival\recorded\`.

**Runtime here**: 30, 30 and 29 seconds in three runs, the build of the design's probe and `npm ci`
included.

**Changes made to run it here:**

1. `scripts\Run.ps1` and `scripts\Run-e4.ps1`: `$S` and `$Root`, fixed paths into the agents'
   scratch directories, became `Split-Path -Parent $PSScriptRoot` and the family directory above
   it; `$Wc`, the design agent's `probe\out\Wc.exe`, became `..\design\probe\out\Wc.exe`; `$Mod`,
   the others report's kdbxweb install, became `..\design\kdbxweb\node_modules\kdbxweb`. In
   `scripts\Run.ps1` the two paths into the KeePassXC ground report's downloads became
   `Get-KeePassXCCli`.
2. `scripts\kpops.ps1`: the product worktree's `.keepass\2.62\KeePass.exe` became `Get-KeePassExe`
   from `tools\KeePass.ps1`, the same file by hash. `scripts\kw.js` is unchanged.
3. `scripts\group-records.ps1` was written on this branch, because the critic recorded
   `out\group-records.txt` and not the command that made it. It runs `scripts\kpops.ps1 records`
   over the eleven files the record names, in its order, and keeps the lines naming the store
   group, which is the record's shape.
4. `Reproduce.ps1` was added. It builds the design's probe, since the critic ran the design's
   `Wc.exe` unchanged, installs kdbxweb into the design's directory, and runs the three scripts in
   the order the record's files were written. `scripts\Run.ps1` is started with `pwsh.exe`; see
   "PowerShell 7".

**What the paper quotes, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| The tools: `keepassxc-cli` 2.4.2 and 2.8.0 with valid signatures, kdbxweb 2.1.1 | `critique-survival.md` §9 | the top of `run.txt` | the same | yes | |
| e1 step 2: Delete Empty Groups removes one group, the store group, with a record `4B504747...01`; the gate stays in force | `critique-survival.md` §2.1 | `removed 1 group(s)`, in force, the record in `its records and groups` | the same | yes | the record's time is the clock |
| e1 step 3: the next write makes `S3` in a store group made again under that identifier, newer than the record, and deletes `S2` with its record | `critique-survival.md` §2.1 | in force at `S3`, the group and its record side by side | the same | yes | |
| e1 step 4: kdbxweb in either direction leaves no snapshot: gone, `c1` refused by its witness | `critique-survival.md` §2.1; `digest.md` §2 | 2 of 2 `GONE`, `c1(witness)` | the same | yes | |
| e1 step 5: KeePass's Synchronize both ways keeps `S3` in force and drops the group's record; KeePassXC 2.4.2 and 2.8.0 keep `S3` in force, and 2.8.0 leaves the record in its target | `critique-survival.md` §2.1 and §4, table | in force in all six; `group-records.txt`: no record in `kp-intoA` and `kp-intoB`, the record in `xc-2.8.0-intoA` | the same | yes | |
| e1 step 6: kdbxweb merging the copy KeePass repaired with the first kdbxweb result deletes the store again; a Synchronize of a kdbxweb result with a copy still holding `S3` brings it back | `critique-survival.md` §2.1 | `GONE`, then in force at `S3` | the same | yes | |
| e2: kdbxweb with the stale copy in either direction leaves out the new snapshot; Synchronize keeps it | `critique-survival.md` §2.1 and §5 item 5 | 2 `GONE`; 2 with the new `S1` present | the same | yes | the new store reads `BEHIND`, an artifact of the probe's write, as the critique states |
| e4: kdbxweb either way reads in force with A's state alone, `writer` on and E1 with A's rule, B's snapshot gone with no record of its own, `c2` refused by the witness; Synchronize keeps both heads with `writer` off and both rule documents; the next write keeps A's state alone | `critique-survival.md` §2.1 and §5 item 4; `digest.md` §3 item 8 | `e4.txt` as stated | the same | yes | |
| The copies that receive no deletion record: KeePassXC, `summary.txt` lines 39 to 42, and KeePass's three import methods, lines 35 to 37, all `R0` | `critique-survival.md` §2.2, table | those lines of the design's record | the same lines in the design's fresh run | yes | |
| e3: after a KeePassXC 2.4.2 or 2.8.0 merge or an OverwriteIfNewer, the stale copy holds `S1` and `S2` and no record, `c1` refused by `S2`; `S2` deleted for good gives behind, at `S1`'s state, `c1` not revoked and no witness | `critique-survival.md` §2.2 and §5 item 1; `digest.md` §3 item 7 | the three cases as stated | the same | yes | |
| 136 merge cells of the design's | `critique-survival.md` §1 | 70, 56 and 10 in the design's record | the same | yes | see the design's table |
| The probe implements the profile join as written, `Program.cs:87-91`; its "widened" test, `:946-952`, restates the rule; its histories never prune, `:898-921` | `critique-survival.md` §2.3 and §2.9 | read in the source | `design\probe\Program.cs` is carried unchanged and those lines are the lines named | yes | a reading of the probe, not a measurement |
| After an import with new identifiers, Delete Duplicate Entries deleted the original snapshot and left the copy, `[M wildcard]` | `critique-survival.md` §2.8 | the design's record shows `removed 4` and both snapshots left | the same | **no** | the design's run does not show it; the security critique's P1 does, for a self-import |
| All of it under two minutes | `critique-survival.md` §9 | nothing | 30 seconds | yes | |

## `security-and-fit\`: the security and fit critique's probes

**What it measures.** `scripts\probe.ps1` runs P1, a database with a store group and one snapshot
imported into itself with Create new IDs and then put through Delete Duplicate Entries, and P2, a
deletion record used as a witness against an entry carrying its identifier in the other copy,
newer or older, merged by Synchronize both ways. `scripts\count-code.ps1`, written on this branch,
counts the product's files the critique names. Cited by `critique-security-and-fit.md` sections
3.3, 3.8, 4, 5.1, 5.2 and 7, and by `digest.md` section 3.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\wildcard\security-and-fit\Reproduce.ps1
```

**Output.** Fresh: `security-and-fit\out\probe.txt`, and `out\code-counts.txt`, which has no
recorded twin. Recorded: `security-and-fit\recorded\probe.txt`.

**Runtime here**: 7, 8 and 7 seconds in three runs.

**Changes made to run it here:**

1. `scripts\probe.ps1`: the product worktree's `.keepass\2.62\KeePass.exe` became `Get-KeePassExe`
   from `tools\KeePass.ps1`, the same file by hash.
2. The critic's work directory `work2` is `work\` here, because `.gitignore` covers `work\` and not
   that name. It is an argument, so no source changed.
3. `scripts\count-code.ps1` and `Reproduce.ps1` were added. The first is a check of the critique's
   counts, which the critic made with `wc -l` and a search and recorded without the commands: it
   exports each named file through `tools\Product.ps1` and counts its newline characters, as
   `wc -l` does, and lists the files holding `CustomData` with `git grep -l` at the product's
   commit, through the same tool.

**What the paper quotes, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| P1: Delete Duplicate Entries after a self-import with Create new IDs removed 3 entries, among them the original snapshot under its own identifier, with a deletion record naming it, and left the copy | `critique-security-and-fit.md` §3.8, §4 and §7; `digest.md` §3 item 12 | `removed 3`, the original's identifier among the deletion records, `snapshot left: ... a copy` | the same | yes | the two user entries and the snapshot of the original are what went |
| The design's row cites a run whose record shows the two U-set runs, each `removed 4` with both snapshots left, `summary.txt` lines 207 to 210 | `critique-security-and-fit.md` §3.8; `digest.md` §2 | the design's record | the same | yes | line 207 is the heading; the two counts and results are lines 208 to 211 |
| P2: a record at +10 s and an entry under its identifier at +20 s: after Synchronize both ways the record is gone and the entry present; at +5 s the record stays and the entry goes | `critique-security-and-fit.md` §3.3 and §7; `digest.md` §3 | the four lines as stated | the same | yes | |
| The design's own outputs it quotes: `KeePass U into A CreateNewUuids \| UNDECIDED: a copy ...`; `KeePassXC 2.4.2 C into A` with `R0`, `revoked=[c2]`, `writer:2(off)` and `E1 n2 SIBLINGS`; the line `C-A`; `KeePassXC 2.8.0 rm of the snapshot, twice`; `KeePassXC 2.6.6 rm twice, then the stale copy merged in \| GONE ... [S1:b71818!named]` | `critique-security-and-fit.md` §2.1, §2.2, §2.5 and §2.6 | the design's record | the same in the design's fresh run, identifiers apart | yes | |
| Line counts at `a42e38aa`: `CustomDataStore` 110, `AuthorityKeys` 267, `AuthorityStore` 643, `AdmissionStore` 854, `ProfileNames` 277, `RenewalRefusal` 150, `PendingChanges` 971, `NodeDialog` 457, `NodeDialogs` 84, `GrantPanelFactory` 114, `KdbxNodes` 152, `DatabaseNodes` 93, `AclDocument` 294, `AclResolver` 537, `AuthorityChooser` 537, `UndecidedTarget` 184, `DatabaseWriter` 2179, `OpenDatabaseTargets` 1642, `GroupDialogs` 1228, `KdbxVault` 2349, `Expected` 289 | `critique-security-and-fit.md` §5.1 | nothing | the same 21 numbers in `code-counts.txt` | yes, by a check written here | |
| `AuthorityPanel` 2361, `DatabaseProfiles` 489, `ClientReaches` 96, `ApprovalBook` 494, `AuthorityService` 349, `MainWindowDatabases` 244 | `critique-security-and-fit.md` §5.1 | nothing | the same 6 numbers | yes, by a check written here | |
| Thirty-two plugin files mention `CustomData` | `critique-security-and-fit.md` §5.1 | nothing | 32 under `src/KeePassGate` | yes, by a check written here | |
| Fifty-two test files mention `CustomData`; `CustomDataMergeRuleTests` 338 lines, `MergeInRealDatabaseTests` 329, `AuthorityMergeTests` 295, `SnapshotHazardTests` 256, `PendingChangesTests` 886 | `critique-security-and-fit.md` §5.2 | nothing | 52 under `tests`; the same five line counts | yes, by a check written here | the search matches case and covers every file under each tree |

## PowerShell 7

`spikes\storage\probes\README.md` states that the PowerShell probes were measured with Windows
PowerShell 5.1. **The two `scripts\Run.ps1` here were not**, and cannot have been: their
`Invoke-Cli` and `XC` set `ProcessStartInfo.StandardInputEncoding`, which exists in .NET from Core
2.1 on and not in .NET Framework 4.8. So both `Reproduce.ps1` start them with `pwsh.exe`, as the
store-entries family does for the same code. Nothing measured runs in that shell: the KeePassLib
work runs in `Wc.exe`, a .NET Framework 4.8 program, and in `scripts\kpops.ps1`, which the survival
critique's `scripts\Run.ps1` starts in `powershell.exe`. Everything else here runs in 5.1:
`scripts\Run-e4.ps1`, `scripts\probe.ps1` and the scripts written on this branch, and
`scripts\Save-WithKeePass.ps1`, which refuses any other shell.

## Claims nothing here can vouch for

- **The 22 in "22 to 30 ms"**, `design.md` §5.1 and the "up to 30 ms" of `digest.md` §5: the
  record holds one run of `Wc.exe bench`, at 29.759 ms. Fresh runs gave 20.2 to 23.3 ms, so the
  lower end is what this machine gives today, but it came from a run that left no output. Likewise
  the paper's 0.2 ms at 1,000 entries is not its record's 0.265 ms, and is what fresh runs give.
- **The "5 entries removed"** of `design.md` §9, which the table marks "no": no output anywhere
  shows five; the record shows four, and the critic's P1 three.
- **The code the design's first four records came from.** In the agent's directory,
  `probe\Program.cs` was last written at 18:07, after `out\summary.txt` and `out\merges.txt`
  (18:01) and `out\bench.txt` and `out\lattice.txt` (17:53), and before `out\kdbxweb.txt`,
  `out\keepass247.txt` and the survival critic's runs, which used the build of 18:07. The version
  that made the first four is therefore not on record. Today's source reproduced all six, so
  whatever that last change was, it changed none of them.
- **Everything the papers mark as another report's**: `[M keepass]`, `[M keepassxc]`,
  `[M others]`, `[M measured]`, `[M android]`, `[M inventory]` and `[M spike]` belong to the ground
  families, the Android measurement and `spikes/revocation-merge`, and the `[R ...]` marks are
  reading, not running. The KeePassDX sequences of `critique-survival.md` §2.2 and
  `critique-security-and-fit.md` §2.5 are read, as both state.

## Defects and deviations found

- **`design.md` §11 does not hold for KeePassXC 2.8.0**, as its own record shows: its `rm`, which
  moves the store entry into the bin, and its `mv` into another group each leave a KDBX 4.1 file.
  For the `mv`, `checks.txt` finds the store group recorded as the entry's previous parent group,
  a 4.1 field; the file the `rm` left is overwritten by the second `rm`, so only the dump shows its
  version. Every merge and save in the matrix is 4.0, and the plugin writes no 4.1 field, so the
  claim holds for what the plugin and the merges write and fails for a person moving the entry in
  that release.
- **`design.md` §9 cites a run that shows the opposite** for Delete Duplicate Entries, and
  `critique-survival.md` §2.8 repeats it as `[M wildcard]`. The outcome stated is right for a
  self-import, which only the security critique ran.
- **The chains row of `design.md` §8.2 is wider than its output**, as its table row states.
- **The design's `kdbxweb.txt` and `keepass247.txt` and the survival critique's
  `group-records.txt` were made by commands nobody kept.** The three scripts written here to make
  them again reproduce them, which is the evidence that they are what was run; it is not the
  record of a command.
- **The locale decides two things in the outputs**: KeePassXC 2.8.0's bin is `Papierkorb`, and the
  bench prints a decimal comma. Both runs, the record's and the run here, were under `de-DE`;
  elsewhere those lines read differently with the same results.
- **Stated limits of the probes, not defects**: the probe keeps the document and the seal in one
  string, marks identifiers with four bytes, and carries a rule's number without the digests of the
  versions it replaced, as `design.md` §16 states; its reading has no "lower by eight", no prune and
  no `gone` from a deletion record, as `critique-survival.md` §2.9 states; and its write over a
  store that is gone leaves the old snapshots out of `over`, which the survival critic names as the
  reason e2's new store reads behind.

None of these changes a recorded output. No probe was changed to make a row come out.

## Not carried

- **Every database**: 242 `.kdbx` files under the design's `work\`, 27 under its `work-kw\` and 3
  under its `work-kp247\`, 47 under the survival critique's `work\`, and 9 under the security
  critique's `work2\`. Each run makes them again.
- **Build output**: the design's `probe\obj\` and `probe\out\`, which holds `Wc.exe`, `Wc.pdb`,
  `Wc.exe.config` and copies of KeePass's `KeePass.exe`, `KeePass.exe.config` and
  `KeePass.XmlSerializers.dll`.
- **The download** `dl-kp247\`, `KeePass-2.47.zip` and the 14 files unpacked from it, which
  `tools\KeePass.ps1` provides from the pin and checks.
- **The 56 `merge.txt` and 2 `ls.txt` files** under the design's `work\`: what `keepassxc-cli`
  printed in each cell, in German and naming the scratch paths of each file, and quoted by no paper.
  The one fact the table above takes from them, that a set H merge into B left the target unwritten,
  shows in the dump as well, where that target's snapshot carries none of the fields KeePassXC adds.
- **The three `names.txt`** under the design's `work\base\` and `work-kw\base\` and the survival
  critique's `work\base\`, which every `Wc.exe make` writes.
- **The kdbxweb install** under the others report's working directory: its `package.json` and
  `package-lock.json` are carried into `design\kdbxweb\`, the installed modules are not.
- **The security critique's empty `work\`**.

## Machine paths replaced

The recorded outputs held no machine path, user name or session identifier, and each is a byte copy
of what the agent left. In the sources, the full paths to the agents' scratch directories, to the
KeePassXC ground report's downloads, to the others report's kdbxweb install and to the product
worktree's KeePass, in the two `scripts\Run.ps1`, `scripts\Run-e4.ps1`, `scripts\kpops.ps1`,
`scripts\probe.ps1` and `probe\Wc.csproj`, became the paths relative to the branch and the tools
listed under each probe's changes. A search of this directory for `C:\`, `Users\`, `AppData`,
`scratchpad`, `Temp\`, the session identifiers and the user name finds nothing but the project's
abbreviation `KPG`, in the names `KPG.Store` and `KPG.Seal` and in the store group's note that names
the KPG tab. The runs' databases and build output were removed after the runs; their fresh output
stays in each `out\`, which is not committed.
