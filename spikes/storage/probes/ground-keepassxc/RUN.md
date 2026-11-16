# Probes for `ground-keepassxc`

<!-- cspell:ignore Hibp Totp -->

The probes behind the ground report on KeePassXC, `papers\ground\ground-keepassxc.md`: what
KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0 do, through `keepassxc-cli`, to each place the plugin's
data could live when they save or merge a file, and what a later KeePass synchronize makes of the
result. A ground report has no critics, so this family is one directory with no papers below it:

| Path | What it is | Written by |
| --- | --- | --- |
| `scripts\probe\Program.cs`, `Xcp.csproj` | `Xcp.exe`, a console program over KeePassLib 2.62: `make` and `make2` build the two sets of invented databases, `dump` prints any database as text, `kpsync` runs KeePassLib's own Synchronize | the agent |
| `scripts\Common.ps1` | `Invoke-Cli`, which starts `keepassxc-cli` with no window and the password on standard input and logs what it printed, and `Invoke-Dump` | the agent |
| `scripts\Run-Merge.ps1` | merges each set's copies with each release in both directions, dry run first where the release has a dry run, and dumps every result | the agent |
| `scripts\Run-Save.ps1` | one plain save (`add`) and then the edits (`edit`, `rm`, `mv`) on a copy of A, each release, dumped after each stage | the agent |
| `scripts\count_hist.py`, `set_history_max.py`, `tags_raw.py`, `unknown_xml.py` | pykeepass on the raw XML: history counts, the history limit, raw tag text, elements no reader knows | the agent |
| `scripts\Show-Diff.ps1` | the normalized diff the report's cells were read with | the agent |
| `replay\Replay.ps1` | the steps the agent typed by hand, replayed from their logs | this branch |
| `checks\compare.py`, `checks\claims.py` | the comparison with the record, and a check of every measured claim | this branch |
| `Reproduce.ps1` | the one command | this branch |
| `recorded\` | the record's text output, at the paths the run writes them under `work\` | the agent's run |

Every probe was run again from this branch, twice, on 2026-11-16, on the machine the record was
made on and under the same `de-DE` locale. **All 135 recorded files that a run writes again came
out the same as their record** once what differs between any two runs is taken away, and **every
measured claim of the report holds in both runs**, including the claims whose output the record
never kept (one check, I01, was added between the runs and ran in the second). The one number that
came out different is a time: one merge under the slow key derivation took 10.6 to 14.5 seconds in
the first run and 9.2 to 13.4 in the second, against the record's 7.3 to 10.7, on a machine other
measurements were sharing. Each sentence of the report that claims more or less than its own output
is listed under "Where the claims and the record part".

## What a run needs

- Windows PowerShell 5.1 as `powershell.exe`, which runs the command below, and PowerShell 7 as
  `pwsh.exe`, which runs the agent's scripts (see "PowerShell 7").
- The .NET SDK for `dotnet build`, uv for Python, and git, which `scripts\Show-Diff.ps1` calls.
- KeePass 2.62, which `. .\tools\KeePass.ps1; Install-KeePass` provides from the branch root, and
  KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, which `tools\KeePassXC.ps1` provides when a probe asks
  for a release.

Nothing a run starts shows a window: `keepassxc-cli` is started with `CreateNoWindow`, and no
KeePass or KeePassXC window is ever opened. **Every database is made by the run, in `work\`, and
none is kept here.** The run writes only into gitignored places beside it: `work\`, `out\`,
`scripts\probe\out\`, `scripts\probe\obj\`, and through the tools into `.keepassxc\` when a
release is not there yet.

## The command

From the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-keepassxc\Reproduce.ps1
```

`-SkipSlow` leaves out the set with the slow key derivation, which is most of the time.

**What it does, in order.** Checks that KeePass 2.62 is provided; builds `scripts\probe\Xcp.csproj`
with `dotnet build -c Release -o scripts\probe\out`, where `scripts\Common.ps1` looks for `Xcp.exe`
(the record's `obj\Release` shows Release, and nothing recorded the command); empties `work\` and
`out\`; runs `scripts\Run-Merge.ps1 -Set fast`, `-Set slow` and `-Set extra`, `scripts\Run-Save.ps1`
and `replay\Replay.ps1`, each in `pwsh.exe`; copies every text file the run left in `work\` into
`out\` at the same relative path; and writes three reports into `out\`: `compare.txt` from
`checks\compare.py`, and `claims.txt` and `claims-recorded.txt` from `checks\claims.py` over `out\`
and over `recorded\`.

**Runtime here**: 355 and 329 seconds in two runs, of which the slow set took 269 and 253.
`-SkipSlow` leaves about a minute and a half.

**Output.** The probe writes its text beside its databases, in `work\`, which is where the report's
paths point: `work\merge-fast\2.8.0-intoA\result.txt` and so on. The run copies those into `out\`;
the record's are under `recorded\` at the same relative path, without the `work\` in front, so
`work\merge-fast\2.8.0-intoA\result.txt` is `out\merge-fast\2.8.0-intoA\result.txt` and
`recorded\merge-fast\2.8.0-intoA\result.txt`. `recorded\logs\download.log` is the record's check of
the four releases.

## What the record holds, and what it does not

The record is the agent's working directory as it left it, and it holds two kinds of output.

**What its scripts wrote.** `Run-Merge.ps1` and `Run-Save.ps1` wrote every dump, every report the
command printed and every `cli.log` of the merges and the saves, and the agent kept them. These are
carried, and the run compares itself with them.

**What the agent typed by hand.** The unknown XML, the raw tags, the history limit, the exports, the
round trip and the KeePass synchronize were run as commands typed into a session, through
`scripts\Common.ps1` and the Python helpers, and **no script of them was kept**. What was kept of
them is their `cli.log`, the exports, and the dumps of the round trip and the synchronize. Of the
pykeepass reads, the raw history counts and the checks of the unknown XML and the raw tags,
**nothing was kept at all**: their numbers stood only in the report. `replay\Replay.ps1` replays
each of those steps, and every input it uses was read off the record's own databases before they
were left behind: the unknown XML, the raw tags and the history limit were each made from set 1's
`A.kdbx`, which the record's `U.kdbx`, `T.kdbx` and `H.kdbx` show by their titles, limits and
tags; the tag text is the text the record's `T.kdbx` holds, `zeta; Alpha;alpha, b:c,  spaced tag
;zeta`; and the synchronize took 2.8.0's results, which `A-sync-xcB.txt` shows by the English day
name in its marker, which 2.8.0 writes and 2.7.12 does not. Each step's comment names the log it
replays. The files those steps write and the record never had are listed under "Only in this run"
below, and the claims resting on them are marked "no record" in the table.

**`Program.cs` changed while the record was being made.** The file carried here is the last
version, saved at 15:59 local time, after `merge-fast`, `merge-slow` and `save-fast` were recorded
between 15:46 and 15:52. The earlier version's dump printed no `search=` and `autotype=` on a
group line, which is why those three directories lack them and `merge-extra`, `roundtrip` and
`kpsync` have them. The earlier version is gone. A fresh run reproduces those three directories
exactly apart from those two fields, so its `make` and the rest of its dump were the same as these.

## Changes made to run it here

1. `scripts\probe\Xcp.csproj`: the `HintPath` to KeePass became `$(KeePassExe)`, which
   `spikes\storage\Directory.Build.props` defines as the pinned `.keepass\2.62\KeePass.exe`, the
   same file by hash as the file the record was built against.
2. `scripts\Common.ps1`: `$script:Root`, a fixed path into the agent's scratch directory, became
   `Split-Path -Parent $PSScriptRoot`; the four paths into the agent's downloads became
   `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1`, which provides the same pinned
   archives and checks their signatures. The tool is dot-sourced inside a script block, so that its
   `Set-StrictMode -Version Latest` stays there and does not reach the probe's own code.
3. `scripts\Run-Merge.ps1` and `scripts\Run-Save.ps1`: the full path to `Common.ps1` became
   `Join-Path $PSScriptRoot 'Common.ps1'`.
4. `scripts\Show-Diff.ps1`: its scratch directory became `work\tmp` beside `scripts\`. The run does
   not call it: `checks\claims.py` applies the same three replacements where it compares a save
   with its input (check V02).
5. Added on this branch: `Reproduce.ps1`, `replay\Replay.ps1`, `checks\compare.py` and
   `checks\claims.py`. None of them changes what the agent's code measured; `Replay.ps1` also writes
   the pykeepass reads the record never kept, the dumps of the inputs (`inputs\`), and the release
   check (`releases.txt`) that `tools\KeePassXC.ps1` makes in place of the agent's download script.
6. The agent's scripts are started with `pwsh.exe`. See "PowerShell 7".

Nothing else was changed, and no defect that changes a result was found in the probe.

**Machine paths in the record** were replaced in every file under `recorded\`: the agent's working
directory, `<scratch>\storage\keepassxc`, became `spikes\storage\probes\ground-keepassxc`, and a
release inside its downloads, `<scratch>\storage\keepassxc\dl\<release>\x\KeePassXC-<release>-...\`,
became `.keepassxc\<release>\`, the place the scaffold provides it. Nothing else in the record named
the machine, a user or a session. `logs\download.log` keeps the publisher's address, which is part
of the certificate the release is signed with.

## PowerShell 7

`spikes\storage\probes\README.md` states that the PowerShell probes were measured with Windows
PowerShell 5.1. **The agent's scripts here were not**, and cannot have been: `Invoke-Cli` sets
`ProcessStartInfo.StandardInputEncoding`, which exists in .NET from Core 2.1 on and not in .NET
Framework 4.8, and Windows PowerShell answers that line with "The property 'StandardInputEncoding'
cannot be found on this object". The record agrees: no `cli.log` in it starts with a byte order
mark, which `Add-Content -Encoding utf8` writes in 5.1 and not in 7. Nothing measured runs in that
shell: the databases are made and read by `Xcp.exe`, a .NET Framework 4.8 program, and the merges
and saves by `keepassxc-cli`.

## Comparing a run with the record

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\ground-keepassxc\checks\compare.py spikes\storage\probes\ground-keepassxc
```

`compare.py` was written on this branch. It takes away from both sides only what differs between
any two runs of the same code, and prints for each file what it took away:

| Taken away | Why it differs between runs | Files |
| --- | --- | --- |
| paths | the branch root in front of a path, which the record had replaced already | every `cli.log`, report and export that names a file |
| times in `cli.log` | the start and end of each command | every `cli.log` |
| identifiers | `Program.cs` draws every group and entry identifier at random, so each becomes the name of its group or entry, and the `deleted` lines, sorted by identifier, are sorted by name; a full identifier in a command's report, as `{...}` or as 2.6.6's 32 digits in brackets, and an XML export's base64 identifiers are replaced | every dump, report and XML export |
| clock | the date in KeePassXC's `_LAST_MODIFIED` marker, the value of `KPXC_RANDOM_SLUG`, and every time not within two days of the invented T0, as text or as an XML export's base64 seconds: a command's own "now" and KeePassLib's default expiry time | dumps after a save, the XML exports |
| order of custom data items | an XML export writes the items of a custom data dictionary in no fixed order | the XML exports |
| group flags | the record's `merge-fast`, `merge-slow` and `save-fast` dumps were written before `Program.cs` printed `search=` and `autotype=`, so those fields are taken from the fresh file only, and only where the recorded file has none | those three directories |

**Both runs: 135 the same, 0 different.** Every recorded file a run writes again came out the same
once those were taken away. Three of the rules were added only after the first run showed the noise
they take away: 2.6.6's command prints identifiers as 32 digits in brackets rather than in braces,
and the XML exports carry the clock in base64 and write custom data items in a different order
each time. With them, the first run's output compared the same as well.

**The display language is part of the record.** From 2.7.12 on the command line answers in the
machine's language, German here, and `compare.py` does not take that away: a run on a machine in
another language differs in the text of every `cli.log`, `merge.txt` and `dry.txt` from those two
releases, while every dump stays the same. `claims.py` reads
the dumps and accepts the English and the German word for a saved merge, so its checks do not
depend on it.

**Only in the record**: `logs\download.log`, the agent's check of the releases, which
`out\releases.txt` makes again.

**Only in this run**, with no recorded counterpart: `counts\merge-fast.txt`, `merge-slow.txt`,
`merge-extra.txt`, `save-fast.txt` and `base.txt` (raw history counts), `unknown\check.txt`,
`tags\get.txt`, `histmax\set.txt`, `histmax\count.txt` and `histmax\H-dump.txt`, `inputs\*.txt`,
`releases.txt`, and the four `help-<release>.err`, which are empty in both runs as the record's
were (the record's are not carried, being empty).

## The claims, and this run

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\ground-keepassxc\checks\claims.py spikes\storage\probes\ground-keepassxc\out
```

`claims.py` was written on this branch. It holds one check per claim the report makes from running
code, reads only the text output, and prints for each check `holds`, `FAILS`, or `no data` where a
file it needs is absent. Run over `recorded\` it shows what the record alone can vouch for: every
check whose file the record has holds there too, and the rest show `no data`. Section numbers are
the report's. A cell is one release, one direction and one key derivation, so set 1 has 16 cells
per claim: four releases, into A and into B, fast and slow.

| Claim | Quoted in | Check | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- |
| Each release verified against its pinned hash; `KeePassXC.exe` reports its version; both programs validly signed by DroidMonkey Apps, LLC | §1 | Z01 | `download.log`: hash, version of `KeePassXC.exe`, signature of `keepassxc-cli.exe` | 8 of 8 programs | yes | the record names only the command line's signature; this run checks both programs. `releases.txt` also names the pykeepass and lxml it read with, 4.2.0 and 6.1.3; the record names none, `--with pykeepass` being unpinned |
| One merge under the slow key derivation took 7.3 to 10.7 seconds, so the two loads always fell in different seconds | §1 | C03 | 7.3 to 10.7 s from the record's `cli.log` | 10.6 to 14.5 s, and 9.2 to 13.4 s in the second run | differs | a time on a machine other measurements were sharing; the conclusion drawn from it, two loads in different seconds, holds the more |
| KeePassLib 2.62's `Open` trims history to the file's limits: 10 items, limit 3, read back as 3 | §1, §5 | H01, H02 | no record | 10 in the file, 3 through KeePassLib | yes, no record | |
| Every merge exited 0 | §4 | C01 | 24 of 24 | 24 of 24 | yes | `merge --help` exits 1 from 2.6.6 on, which is not a merge |
| The slow key derivation gives exactly the fast derivation's result in every cell | §4 | C02 | 8 of 8 | 8 of 8, raw counts included | yes | the report's aside that "the only differing line was the recycle bin's UUID" is narrower than the record: see "Where the claims and the record part" |
| `added-A`, `added-B`: two entries added to one group, one in each copy, both arrive | §2 item 2, §4, §5 | R01 | 16 of 16 | 16 of 16 | yes | |
| `in-newA`: a group added on one side arrives with its entry | §4, §5 | R02 | 16 of 16 | 16 of 16 | yes | |
| `one-A`: A's change in both; into B, B's version in history | §4 | R03 | 16 of 16 | 16 of 16 | yes | |
| `prot-A`: A's protected string in both, still protected | §4, §7 | R04 | 16 of 16 | 16 of 16 | yes | |
| `att-A`: A's attachment in both; into B, the old attachment in history | §4 | R05 | 16 of 16 | 16 of 16 | yes | |
| `notes-A`, `tag-A`: A's notes and tag in both | §4 | R06, R07 | 16 of 16 each | 16 of 16 each | yes | |
| `both-diff`: B's entry whole, `FieldB` only; `FieldA` only in the history item at +1h | §2 item 3, §4, §5 | R08 | 16 of 16 | 16 of 16 | yes | the claim the whole report turns on |
| `both-samefield`, `att-both`, `tag-both`, `cd-both`: B's whole; A's only in history | §4, §5 | R09 to R12 | 16 of 16 each | 16 of 16 each | yes | |
| `both-tie`: on equal times the target's version stays and the source's is nowhere, history included | §2 item 4, §4, §5 | R13 | 16 of 16 | 16 of 16 | yes | |
| `both-tie`: the command prints nothing about it | §2 item 4, §5 | R30 | 16 of 16 | 16 of 16 | yes | |
| `rewound-B`: a change whose time was set before the base loses in both directions, kept only as a history item | §4, §5 | R14 | 16 of 16 | 16 of 16 | yes | |
| `hist`: history of 4 after the merge, every version of both copies present | §4, §5 | R15 | no data: the dump's four times are in the record, the raw count is not | 16 of 16, raw 4 | yes, no record for the count | |
| `hist-many`: raw history into A 10 in 2.4.2 and 2.6.6, 11 in 2.7.12 and 2.8.0; into B 10, oldest dropped | §2 item 6, §4 | R16 | no data | 16 of 16 | yes, no record | |
| `hist-big`: into A 2 items; into B 2 in 2.4.2 and 2.6.6, 3 items of 400 KiB each in 2.7.12 and 2.8.0 | §2 item 6, §4 | R17 | no data | 16 of 16 | yes, no record | "1.2 MiB" is three items of the probe's 400 KiB, computed rather than measured |
| KeePassLib trims the excess history when it opens the file | §2 item 6 | L01 | no data | 4 of 4: raw 11, KeePassLib 10 | yes, no record | |
| `move-A`: into B moved, B's location time stays T0, previous parent set by 2.7.12 and 2.8.0 only | §4 | R18 | 16 of 16 | 16 of 16 | yes | |
| `move-A-edit-B`: into A, B's content in `Sub` with B's location time and no previous parent; into B moved | §2 item 9, §4 | R19 | 16 of 16 | 16 of 16 | yes | |
| `del-A-unch`, `del-A-chB-after`, `del-A-chB-before`, `GDelA`: everything A deleted comes back into A, beside A's tombstones, and stays in B | §2 item 1, §4, §7 | R20 to R23 | 16 of 16 each | 16 of 16 each | yes | |
| B never receives a tombstone | §2 item 1, §7, §9 | R28 | 16 of 16 | 16 of 16 | yes | |
| No merge removes an entry or a group the target holds | §2 item 2, §5 | R29 | 16 of 16 | 16 of 16 | yes | |
| `bin-A`: A's bin is not merged; into B a group "Recycle Bin" arrives as an ordinary group, B's bin setting empty, searching and auto-type off | §2 item 10, §4, §7 | R24 | 16 of 16, the flags not printed | 16 of 16, flags off | yes | the record's `merge-fast` and `merge-slow` dumps print no group flags; the round trip's do, and agree |
| `GNotesBoth`, `GNotesA`: the newer group's notes | §2 item 8, §4 | R25, R26 | 16 of 16 each | 16 of 16 each | yes | |
| `GTagsA`: into A kept by 2.7.12 and 2.8.0, none in 2.4.2 and 2.6.6; into B none, under A's modification time | §2 item 8, §4 | R27 | 16 of 16 | 16 of 16 | yes | |
| `meta`: B's entry and its 10 history items; A's timestamped field nowhere, eight results read raw | §2 item 5, §4, §5 | X01 | no data: the dumps are recorded, the raw read is not | 8 of 8 | yes, no record for the raw read | |
| `meta2`: A's entry with both A fields; B's version as the history item at +2h | §4 | X02 | 8 of 8 | 8 of 8 | yes | |
| `GFlags`: never merged; into B B's, under A's modification time | §2 item 8, §4, §7 | X03 | 8 of 8 | 8 of 8 | yes | |
| `GCustom`: never merged; into A kept with `_LAST_MODIFIED` added | §2 item 8, §4, §6, §7 | X04 | 8 of 8 | 8 of 8 | yes | the marker is added by all four releases; the save table names it for 2.4.2 only |
| `excl`: the exclusion from reports kept by 2.7.12 and 2.8.0, lost by 2.4.2 and 2.6.6 | §2 item 7, §4, §6 | X05 | 8 of 8 | 8 of 8 | yes | |
| Round trip, 2.4.2 and 2.8.0: `both-tie` stays split, `move-A-edit-B` stays split, only A holds tombstones and a bin, in 2.8.0 only A has the group tag | §2 items 4 and 9, §4 | T01 to T05 | all | all | yes | |
| Round trip, 2.8.0: the third merge reports a history change to `hist-big` and writes the file, although that entry is the same in both copies | §4, §10 | T06 | 1 of 1 | 1 of 1 | yes | |
| A plain save writes 4.0 in 2.4.2 and 2.6.6, 4.1 in 2.7.12 and 2.8.0 | §2 item 7, §6 | V01 | 4 of 4 | 4 of 4 | yes | |
| A plain save keeps strings, protected flags, attachments, notes, history, custom data, group notes, tombstones, the bin; the only time it changes is `General`'s | §2 item 7, §5, §6 | V02 | 4 of 4 | 4 of 4 | yes | what is left of each dump once the report's listed changes are taken away is the input, line for line |
| A plain save adds empty standard fields to every entry, `Password` flagged protected | §2 item 7, §6 | V03 | 4 of 4 | 4 of 4 | yes | |
| A plain save flags every attachment protected | §2 item 7, §6, §7 | V04 | 4 of 4 | 4 of 4 | yes | |
| A plain save adds `_LAST_MODIFIED` to entry custom data, the entry's and each history item's | §2 item 7, §6 | V05 | 4 of 4 | 4 of 4 | yes | |
| The previous parent dropped by 2.4.2 and 2.6.6 | §2 item 7, §6 | V06 | 4 of 4 | 4 of 4 | yes | |
| Group tags dropped by 2.4.2 and 2.6.6, as KeePassLib reads them | §2 item 7, §6 | V07 | 4 of 4 | 4 of 4 | yes | the raw text is G01 |
| A plain save keeps the bin and every tombstone | §6 | V08 | 4 of 4 | 4 of 4 | yes | |
| A plain save keeps the group searching and auto-type flags | §6 | V09, X03 | V09 not printed; X03 8 of 8 | V09 4 of 4 | yes | see "Where the claims and the record part" |
| Raw tag text: entry tags verbatim in 2.4.2 and 2.6.6, `Alpha,alpha,b:c,spaced tag,zeta` in 2.7.12 and 2.8.0; group tags dropped by 2.4.2 and 2.6.6, verbatim after | §6 | G01 | no record | 5 of 5 | yes, no record | |
| Unknown XML at 8 positions dropped by every release's save | §2 item 7, §6, §7 | U01 | no record | 5 of 5: all 8 in the input, none after | yes, no record | |
| No truncation on load or on a plain save: limit 3, all ten items of `hist-many` still there, four releases | §2 item 7, §5 | H01 | no record | 4 of 4 | yes, no record | |
| KeePassLib 2.62's `Save` wrote 2 of the 4 history items the probe created, under the 1 MiB limit | §5 | B01 | no record | 2 | yes, no record | |
| `edit -u`: the pre-edit state into history, the time now | §6 | E01 | 4 of 4 | 4 of 4 | yes | |
| `edit` truncates: the oldest of `hist-many`, and one of `hist-big` by size, raw counts | §5, §6 | E02 | no data: the dumps are recorded, the raw counts are not | 4 of 4: raw 10 to 10 and 2 to 2, oldest dropped | yes, no record for the counts | |
| The history item an edit makes has no entry custom data up to 2.7.12 and has it in 2.8.0 | §6, §7 | E03 | 4 of 4 | 4 of 4 | yes | |
| `rm` outside the bin and in the bin | §6, §8 | E04, E05 | 4 of 4 each | 4 of 4 each | yes | |
| `mv`: not in 2.4.2; location time only in 2.6.6; history item, both times and previous parent from 2.7.12 | §6 | E06 | 4 of 4 | 4 of 4 | yes | |
| CSV export: the ten columns, recycled entries included, no custom strings, attachments, custom data, history, tags or group notes | §2 item 11, §8 | P01 | 3 of 3 | 3 of 3 | yes | |
| HTML export: every custom string, protected strings in clear, entry and group notes; not the bin, attachments or custom data; absent from 2.6.6's command line | §2 item 11, §7, §8 | P02 | 3 of 3 | 3 of 3 | yes | |
| XML export: everything, unencrypted | §2 item 11, §8 | P03 | 3 of 3 | 3 of 3 | yes | |
| KeePass synchronize: KeePassXC's resurrected entries still go, by A's tombstones | §9 | K01 | 1 of 1 | 1 of 1 | yes | |
| KeePass synchronize: a group time KeePassXC raised without copying the field makes the stale field win (the tag) | §2 item 8, §9 | K02 | 1 of 1 | 1 of 1 | yes | measured for the tag only; the flags and custom data are the report's assumption |
| KeePass synchronize: the location time regressed on `move-A-edit-B` comes through | §9 | K03 | 1 of 1 | 1 of 1 | yes | |
| KeePass synchronize, A target and B source: `both-tie` resolved to B's, no trace of A's | §9 | K04 | 1 of 1 | 1 of 1 | yes | |
| The inputs: base and B are KDBX 4.0, A is 4.1; `KPG` holds 21 entries and five subgroups | §1 | I01 | no record | 1 of 1 | yes, no record | added between the two runs, so the second run alone has it |

## Where the claims and the record part

None of these changes a conclusion of the report; each is a sentence worded differently from what
its own output shows, and the table above marks the claims themselves.

- **"The only differing line was the recycle bin's UUID"** (§4), of the fast and slow results.
  `Program.cs` draws every identifier at random in `make`, so the two sets differ in every
  identifier, not only the bin's: the record's `merge-fast` and `merge-slow` dumps differ on every
  `u=` and every `deleted` line. The comparison the sentence describes was not kept. With every
  identifier replaced by its name, C02 finds the two the same in every cell, which is the claim.
- **The plain save table's basis** (§6). The report states that the table compares a save of a
  copy of A, "a KDBX 4.1 file holding every candidate feature", with that copy. A holds no
  exclusion from reports, no group custom data and no group flags but the bin's, and the record's
  dumps of the save print no group flags. Those three rows rest instead on set 2 merged into A
  (X03, X04, X05), where KeePassXC loads A, keeps A's values and writes A, and for the flags on the
  bin's group in a fresh dump of the save (V09). Both agree with the table.
- **"1.2 MiB"** (§2 item 6, §4): the probe counts three history items of one attachment each; the
  size is three times the 400 KiB the probe gives each version, computed rather than read.
- **Both programs' signatures** (§1): `download.log` records the signature of `keepassxc-cli.exe`
  and the version of `KeePassXC.exe`, not the signature of `KeePassXC.exe`. Z01 checks both now.

## Read from source, not run here

Every [R] statement of the report cites KeePassXC's source at a tag. They are not programmatic
findings, the run checks none of them, and the clones are not carried: each is cited here by tag
and commit, from `https://github.com/keepassxreboot/keepassxc`.

| Tag | Commit | Files the report cites |
| --- | --- | --- |
| `2.4.2` | `a775031fe9471310e50232d1861d4991e2803aff` | `src/core/Merger.cpp`, `src/core/Group.cpp` |
| `2.6.6` | `9c108b95100bfa3b172edcfd5754aa2e831a3221` | `src/core/Merger.cpp`, `src/core/Group.cpp` |
| `2.7.0` | `d7a9ef493241421402cbe3aba2518e159c4a2dd8` | `src/core/Merger.cpp` (the clone held `src/core` only) |
| `2.7.12` | `860e51d8e9a5a67e73de79b173ae9bd5bdb5ebda` | `src/cli/Merge.cpp`, `src/keeshare/ShareImport.cpp`, `src/core/Group.h`, `Group.cpp`, `Merger.cpp`, `Entry.cpp`, `Metadata.cpp`, `src/format/KdbxXmlReader.cpp`, `KeePass2Writer.cpp`, `Kdbx4Writer.cpp`, `KdbxXmlWriter.cpp` |
| `2.8.0` | `1c2f23ef5914c0f442a00533def1689857afb5a7` | `src/cli/Merge.cpp`, `Move.cpp`, `Remove.cpp`; `src/gui/MergeDialog.cpp`, `DatabaseWidget.cpp`, `DatabaseTabWidget.cpp`, `EditWidgetProperties.cpp`, `src/gui/dbsettings/DatabaseSettingsWidgetGeneral.cpp`, `DatabaseSettingsWidgetBrowser.cpp`, `src/gui/entry/EditEntryWidget.cpp`, `EntryAttachmentsWidget.cpp`, `src/gui/group/EditGroupWidget.cpp`, `src/gui/reports/ReportsWidgetHealthcheck.cpp`, `ReportsWidgetHibp.cpp`, `ReportsWidgetPasskeys.cpp`, `ReportsWidgetBrowserStatistics.cpp`; `src/keeshare/ShareImport.cpp`; `src/core/Group.cpp`, `Entry.cpp`, `Metadata.cpp`, `Database.cpp`, `Config.cpp`, `EntrySearcher.cpp`, `EntryAttributes.cpp`, `CustomData.cpp`, `Totp.h`; `src/format/Kdbx4Writer.cpp`, `CsvExporter.cpp`, `HtmlExporter.cpp`; `src/browser/BrowserService.cpp`, `BrowserEntryConfig.cpp`; `src/autotype/AutoType.cpp`; `src/networking/HibpDownloader.cpp` |

The line numbers are in the report, beside each statement.

## Not carried, and why

- **The four release archives and what was extracted from them**, `dl\` in the agent's directory,
  about 590 MB: `tools\KeePassXC.ps1` provides the same archives by the hashes `keepassxc.sha256`
  pins, and checks their signatures every time a probe asks. `dl\<release>\ok.txt` repeated
  `logs\download.log`, which is carried.
- **`scripts\Get-Releases.ps1`**, which fetched and checked those archives into `dl\`: the tool does
  the same, and `out\releases.txt` records its check.
- **The source clones**, `src\`, about 160 MB: cited above by tag and commit.
- **Build output**, `scripts\probe\out\` and `scripts\probe\obj\`, which held a copy of
  `KeePass.exe` beside `Xcp.exe`.
- **Every database**, 84 `.kdbx` under `work\`. What the record shows of them is their dumps, which
  are carried.
- **`work\help-<release>.err`**, four empty files, which the run writes again; and `work\tmp\`,
  empty, where `Show-Diff.ps1` keeps its files for the length of one diff.
