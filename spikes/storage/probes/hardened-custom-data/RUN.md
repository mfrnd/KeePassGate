# hardened-custom-data: the probes and what they reproduce

<!-- KeePassXC's German change line, quoted as it printed it. cspell:ignore Daten entfernt -->

The family behind `papers/options/hardened-custom-data/`: the design (`design.md`), its two
critiques (`critique-survival.md` and `critique-security-and-fit.md`) and the digest (`digest.md`),
which quotes the same numbers. Three probes ran for those papers, one for each of the first three,
and the design's agent also ran a short script that lists a KeePassLib class's members. Each lives
in the directory of the paper that ran it, as `probes/README.md` lays out, keeping the layout its
agent gave it:

| Directory | Probe | What it measures | Cited by |
| --- | --- | --- | --- |
| `design\` | `scripts\Probe.ps1` | the design's store (every `KPG.` key in `PwDatabase.CustomData` and again in the root group's custom data, written once and without a time) under KeePassLib 2.62's Synchronize and under `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, merging and saving; the KDBX version with and without key times | `design.md` §0, §1, §4, §5, §6, §8, §10, §11, §13; `critique-survival.md` §1, §2 F1, §3, §4; `digest.md` §2, §3 |
| `design\` | `scripts\Members.ps1` | the access of each member of `StringDictionaryEx` in KeePass 2.62, by reflection | `design.md` §1, §13 |
| `survival\` | `scripts\Chain.ps1` | a KeePassXC 2.8.0 merge followed by a KeePassLib 2.62 Synchronize (F1), two copies editing one rule (S1), and a 4.1 file saved by KeePassXC 2.8.0 (M1) | `critique-survival.md` §0 to §5; `digest.md` §2 to §4, §7 |
| `security-and-fit\` | `scripts\Attack.ps1` | six scenarios over the design's store, the first two of them read with the design's three states as the probe codes them: a fork of one rule (S1), a pruned ledger under a KeePassXC 2.8.0 merge (S2), Create new IDs and Delete authority (S3), the profile generations (S4), KeePassXC 2.8.0 putting times on keys in a 4.1 file (S5 and S5b) | `critique-security-and-fit.md` §0, P1, P3, P5, P8, P11, §2 (b), §5; `digest.md` §2 to §5 |

All four ran in this run, and each gives its recorded output again: after `compare.py` takes
away the identifiers a run draws at random, the clock and the paths, every recorded file is the
same as its fresh twin. The only numbers that came out differently are how long the
`keepassxc-cli` runs took, which move with the load on the machine. One count the survival critique
states does not come out of its own output in either run: "four of five cells" is three of five
(V5). The claims are in the tables under each probe; the claims these papers present as measured
that no probe here backs are listed apart at the end.

## Running them

From the branch root, in Windows PowerShell 5.1, which is what the probes were measured with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\hardened-custom-data\design\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\hardened-custom-data\survival\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\hardened-custom-data\security-and-fit\run.ps1
```

Each `run.ps1` empties its directory's `out\` and `work\`, because the probes append to their
output files, and starts its probe once per task, in the order the recorded files were written,
with the arguments the probe's own first lines document. The probe provides and checks KeePass 2.62
through `tools\KeePass.ps1` and each KeePassXC release it starts through `tools\KeePassXC.ps1`.
Fresh output goes to `out\` under the names the recorded files have in `recorded\` beside it; the
databases go to `work\`. Both are ignored by the branch. Nothing shows a window: KeePassLib is
loaded as an assembly and `keepassxc-cli` runs as a console child process with no window.

To compare a fresh run with the record, from the branch root:

```powershell
$family = 'spikes\storage\probes\hardened-custom-data'
uv run --no-project python -I -X utf8 $family\compare.py $family\design
uv run --no-project python -I -X utf8 $family\compare.py $family\survival
uv run --no-project python -I -X utf8 $family\compare.py $family\security-and-fit
```

`compare.py` was written on this branch. Its first lines list exactly what it takes away: the root
of the branch in front of `spikes\storage\probes\`, the identifiers of the groups and entries the
probes make, the clock, how long a `keepassxc-cli` run took, and the order in which one
`keepassxc-cli merge` lists its changes, which follows KeePassXC's hash tables. It prints any other
difference. In this run it printed none.

How long each ran here, in one run of all three, on a machine running other measurements at the
same time. Each step includes the tools checking the programs' versions and signatures.

| Run | Here | Recorded |
| --- | --- | --- |
| `design`: `make` | 45.9 s | not stated |
| `design`: `keepass` | 1.9 s | not stated |
| `design`: `xc` slow for 2.4.2, 2.6.6, 2.7.12 and 2.8.0 | 32.8 s, 34.7 s, 39.7 s and 32.1 s | not stated; each `keepassxc-cli merge` 7.9 s to 11.0 s, `design.md` §13 |
| `design`: `xc` fast for 2.4.2 and 2.8.0, `xcsave` for 2.4.2 and 2.8.0, `Members.ps1` | 1.9 s, 1.4 s, 1.4 s, 1.2 s and 0.5 s | not stated |
| `survival`: `make`, `chain`, `branch`, `xctimes` | 25.5 s, 177.4 s, 1.2 s, 1.0 s | not stated; the slowest `keepassxc-cli` run 83 s, `critique-survival.md` §1 |
| `security-and-fit`: `fork`, `import`, `profiles`, `prune`, `xc41`, `xc41save` | 1.2 s, 0.9 s, 0.9 s, 130.2 s, 48.7 s, 14.1 s | not stated; one merge 7.4 s to 18.2 s, `critique-security-and-fit.md` §5 |

The three `run.ps1` took 194 s, 205 s and 196 s in all.

**How a result is judged.** "Yes" is that this run gives what the paper quotes. "Differs" is that
it gives something else, with the note stating whether that is noise, the environment or the claim.
"No" is that neither the record nor this run gives what the paper states. Where the record and this
run agree and the paper's conclusion holds, but its sentence is broader than the output or passes
over a cell, the result is "yes" and the note names the cell or the part that is not in the output.

The claims are numbered D for the design, V for the survival critique and A for the security
critique, because both critiques already use S for something of their own. All paths in the
"Quoted in" columns are under `papers/options/hardened-custom-data/`.

## The design's probe, `design\`

`scripts\Probe.ps1` makes four sets of databases, `timeless` and `timed` each with a slow KDF
(Argon2d, 200 iterations, 64 MiB) and a fast KDF (2 iterations, 1 MiB). Each set holds a base with
a group G and an entry E1 in it, and two copies: C, the stale state, with 7 `KPG.` keys in the
dictionary and the same 7 in the root group's custom data and a wide rule on G, E1 and the root;
and P, which revoked `c2`, retired `a1`, made `a2` and `c3`, removed the profile `writer` and
narrowed G and E1 a month later, holding 12 keys in each place. Its tasks: `make` (into
`make2.txt`), `keepass` (KeePassLib 2.62 Synchronize over the timeless fast set, as made, with C's
root group stamped later, and with C's root and node custom data deleted, into P and into C,
`keepass.txt`), `xc` (`keepassxc-cli merge -s` into P and into C, `xc-<release>-<kdf>.txt`) and
`xcsave` (`keepassxc-cli add` on P, `xcsave-<release>.txt`). `scripts\Members.ps1` prints to the
console, and `run.ps1` writes what it prints to `out\members.txt`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | timeless keys in both places give KDBX 4.0, the same keys with times 4.1 | `design.md` §1 "Why no times", §8, §13 row 1 | `make2.txt` | `timeless-slow` C and P `kdbx 4.0` with 0 timed keys; `timed-slow` C and P `kdbx 4.1` with 7 and 12 | the same | yes | |
| D2 | `StringDictionaryEx.Set(string, string, DateTime?)` and `GetLastModificationTime(string)` are `internal` in 2.62 | `design.md` §1, §13 row 2 | `members.txt` | nothing recorded: `Members.ps1` printed to the console | both `internal`, beside the public `Set(String, String)` | yes | the first record of this output is a run of this branch; `Probe.ps1` also finds both through non-public binding flags or stops |
| D3 | KeePassLib 2.62 Synchronize, 6 cells: the dictionary holds all 14 keys in all 6, none removed, no key with two values | `design.md` §0 item 1, §5 row 1 and the paragraph after the table, §13 row 3; `critique-survival.md` §3 row 1; `digest.md` §2 | `keepass.txt` | `dictionary (14)` and `keys both hold with different values: 0` in all 6 | the same | yes | |
| D4 | the root mirror in those cells: the arriving copy's at equal root times (C's 7 into P, P's 12 into C); C's in both directions when C's root is later; empty in both directions when C was wiped, the root's own rule gone with it | `design.md` §0 item 1, §1, §5 rows 1 and 2, §13 row 3 | `keepass.txt` | `root mirror (8)` into P and `(13)` into C as made; `(8)` both ways root later; `(0)` and `root rule []` both ways wiped | the same | yes | the counts printed include the root's own rule `KPG`, so 8 and 13 are the 7 and 12 `KPG.` keys the paper names |
| D5 | G and E1 kept P's narrow rule in all 6 | `design.md` §13 row 3 | `keepass.txt` | `G rule [{"reader":[]}]`, `E1 rule [{"reader":[]}]` in all 6 | the same | yes | |
| D6 | KeePassLib writes the layout as 4.0 and both places read back | `design.md` §4 row 1, §8 | `make2.txt`, `keepass.txt` | `kdbx 4.0` for every timeless file, both places listed | the same | yes | |
| D7 | `keepassxc-cli merge -s`, slow KDF, 8 merges: the dictionary replaced by the source's (into P C's 7, into C P's 12), the root mirror the target's, the union 14 of 14, no key with two values, the root time unchanged, the files 4.0 | `design.md` §0 item 1 and table row 1, §1, §5 rows 4 and 5 and the paragraph after the table, §8, §13 row 4; `critique-survival.md` §2 F1 step 2, §3; `digest.md` §2 | `xc-*-slow.txt` | as quoted in all 8 | the same | yes | the dictionary counts printed include KeePassXC's own keys: `_LAST_MODIFIED` always, `KPXC_RANDOM_SLUG` from 2.7.12, so 8 or 9 into P and 13 or 14 into C |
| D8 | into C, G kept C's wide rule under P's time and E1 took P's rule, in all 4 releases | `design.md` §5 row 5, §13 row 4 | `xc-*-slow.txt`, into C | `G rule [{"reader":["read","list"]}] time 2026-02-01`, E1 narrow | the same | yes | |
| D9 | one slow `keepassxc-cli merge` took 7.9 s to 11.0 s | `design.md` §13 | `xc-*-slow.txt`, the `cli` lines | 7.9 s to 11.0 s | 9.9 s to 14.0 s | differs | timing, noise: the machine was running other measurements |
| D10 | one fast merge took 0.1 s to 0.2 s | `design.md` §13 | `xc-*-fast.txt` | 0.1 s to 0.2 s | 0.1 s to 0.2 s | yes | timing |
| D11 | fast KDF, both loads in one second, 4 merges: the `KPG.` keys of both places unchanged; into C the result held C's 7 keys in both places, G's wide rule and E1's narrow rule from P, so 7 of 14 | `design.md` §5 rows 6 and 7, §10 case 1, §13 row 5; `critique-survival.md` §3; `digest.md` §2, §3 item 1 | `xc-2.4.2-fast.txt`, `xc-2.8.0-fast.txt` | union 12 into P, 7 into C; G wide and E1 narrow into C | the same | yes | depends on both loads falling in one second, which held in both runs; a merge that straddles a second boundary gives the slow cell instead (caveats). Into C the file did change: KeePassXC added its own keys and E1 took P's rule |
| D12 | a plain `keepassxc-cli add` on P keeps all 12 keys in both places, adds `_LAST_MODIFIED` to both and `KPXC_RANDOM_SLUG` to the dictionary in 2.8.0, gives the root the save's time, stays 4.0 | `design.md` §4 KeePassXC row, §13 row 6 | `xcsave-2.4.2.txt`, `xcsave-2.8.0.txt` | as quoted | the same, with this run's clock as the root time | yes | |
| D13 | a 2.8.0 merge prints each dictionary key it removes or adds with its value, `Plugin-Daten KPG.Authority.a2 entfernt [document-a2]` | `design.md` §6, §10 case 10, §13 row 7 | `xc-2.8.0-slow.txt`, the `cli said` lines | 9 changes each way, each with its value | the same 9, in another order | yes | 2.6.6 and 2.7.12 print the values too, 2.6.6 in English; 2.4.2 prints none. The order follows KeePassXC's hash tables |
| D14 | `KPXC_RANDOM_SLUG` from 2.7.12 at every save | `design.md` §11 | `xc-2.7.12-slow.txt`, `xc-2.8.0-*.txt`, `xcsave-2.8.0.txt` | in every dictionary 2.7.12 and 2.8.0 wrote, in none 2.4.2 or 2.6.6 wrote | the same | yes | the probe prints names, not values, so that the slug is fresh at each save is not in the output; `design.md` also cites the spike for it |

## The survival critic's probe, `survival\`

`scripts\Chain.ps1` makes the design's bases C and P again, slow KDF only, with `#` in every rule
text, and prints for each result what the gate would read from the union: whether `c2` is admitted,
whether `a1` is retired, whether `writer` is in force, and how many generation 2 ledger keys
remain. Its tasks: `make`, `chain` (KeePassXC 2.8.0 merges C into P giving R and P into C giving
R2, saves C giving Q, then seven KeePassLib 2.62 Synchronize cells, two of them with the design's
reconcile run first in memory), `branch` (two copies editing one rule from generation 1) and
`xctimes` (a 4.1 file saved by KeePassXC 2.8.0).

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V1 | the bases: C holds 7 `KPG.` keys in both places, P holds 12; every key timeless; 4.0; the root time `T0` in both | `critique-survival.md` §1 | `make.txt` | as quoted | the same | yes | |
| V2 | R: the dictionary C's 7, the mirror P's 12, the union 14, the root time unchanged | `critique-survival.md` §2 F1 step 2 | `chain.txt`, R | as quoted | the same | yes | |
| V3 | a KeePassXC save of C gives Q, whose root group takes the save's time | `critique-survival.md` §2 F1 step 3 and "Move the root group's time" | `chain.txt`, Q | the root time the clock of the run | the same | yes | |
| V4 | the five cells of the F1 table: R and Q 7, Q and R 7, R and C 7 (C's mirror), C and R 14 (R's mirror), R2 and Q 14 (dictionary 14, mirror 7); where 7, `c2` admitted, `a1` present and not retired, `writer` in force | `critique-survival.md` §2 F1 table, §4 rows 5 and 6; `digest.md` §2, §3 item 5, §7 | `chain.txt`, step 3 | as quoted | the same | yes | |
| V5 | "I measured this in four of five cells: the result held 7 of 14 keys", and "lost in 4 of 5 cells" | `critique-survival.md` §0, §4; `digest.md` §3 item 5 | `chain.txt`, step 3 | 3 of the 5 cells the table lists hold 7 keys; of all 7 cells the probe prints, the two reconciled first included, 4 hold 7 | the same | no | the count does not come out of the output; the loss it describes does, in 3 of the 5 cells (V4) |
| V6 | in the losing cells G and E1 kept P's narrowing and generation 2 is gone from both places | `critique-survival.md` §2 F1, "Rules held in these cells" | `chain.txt` | `G rule {"#":"2","reader":[]}`, E1 the same, `ledger generation-2 keys in union: 0` | the same | yes | the states the critique derives from this are reasoning, not code |
| V7 | the reconcile run first in memory: 14 of 14 with R open, 7 of 14 with R arriving into Q | `critique-survival.md` §2 F1, "Reconcile earlier"; `digest.md` §3 item 5 | `chain.txt`, the last two cells | 14 and 7 | the same | yes | |
| V8 | a KeePassXC merge leaves the target's mirror as it was: R2's mirror stays C's | `critique-survival.md` §2 F1, last row of the second table | `chain.txt`, R2 | `root mirror 7 keys`, C's | the same | yes | |
| V9 | two copies edit one rule from generation 1; KeePassLib Synchronize either way: E1 holds A's `2.y`, the ledger holds `1.x 2.w 2.y 3.v` | `critique-survival.md` §2 S1 step 4; `digest.md` §2, §3 item 3 | `branch.txt` | `E1 rule {"#":"2","reader":[]} @2026-01-01 13:00:00Z` and `1.x 2.w 2.y 3.v`, both ways | the same | yes | the probe lists the ledger keys of the dictionary, which it writes beside the mirror, not the union the paper names. That the node is *behind* is the design's table applied by hand |
| V10 | a 4.1 file whose `KPG.` keys carry no time, saved once by KeePassXC 2.8.0: both keys come back carrying the save's time | `critique-survival.md` §2 M1 step 2, §3, §5; `digest.md` §4 | `xctimes.txt` | before: `kdbx 4.1`, 0 timed; after: `Client.c1@` and `Revoked.c2@` the save's time | the same, with this run's clock | yes | measured for 2.8.0 only; "2.7 or later" in both papers is read from KeePassXC's source |
| V11 | three `keepassxc-cli` runs, the slowest 83 s on a loaded machine | `critique-survival.md` §1 | `chain.txt`, `xctimes.txt` | 13.6 s, 12.1 s and 82.8 s in `chain.txt`, 0.1 s in `xctimes.txt` | 10.1 s, 9.9 s, 6.0 s and 0.1 s | differs | timing, the environment; the probe starts four runs, three of them in `chain` |

## The security critic's probe, `security-and-fit\`

`scripts\Attack.ps1` models the design's store as the design's probe does, writes ledger keys as
`KPG.Rule.<uuid>.<gen>.<16 hex>`, and applies the design's three states of `design.md` §2 in a
function of its own, `Classify`. It is not the plugin. Every task appends to `out\attack.txt`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A1 | S1, KeePassLib Synchronize either way: N holds B's generation 2 (day 3 is the newest), with three items of history; the union holds generations 1, 2 (A's), 2 (B's) and 3 | `critique-security-and-fit.md` P1 step 4 | `attack.txt` S1 | `N rule [{"#":"2","default":"inherit","agent":["list"]}]; history 3`, four versions recorded, both ways | the same | yes | |
| A2 | S1, the design's read: head generation 3, *behind*, generation 3 in force, so `agent` reads N | `critique-security-and-fit.md` §0 item 1, P1 step 5; `digest.md` §2, §3 item 3 | `attack.txt` S1 | `BEHIND (node holds g2) \| in force: head g3 [... "agent":["read","list"]]`, both ways | the same | yes | the state is the probe's `Classify`, written from `design.md` §2 |
| A3 | S2, ledger pruned, `keepassxc-cli` 2.8.0 merge, slow KDF, either way: N comes back with generation 1, which agrees and is in force | `critique-security-and-fit.md` §0 item 3, P3 steps 3 and 4; `digest.md` §3 item 6 | `attack.txt` S2 | `N is back`, `agrees \| in force: [g1]`, `recorded: g1` only, both ways | the same | yes | P3 step 3 also names which place holds what (into A the dictionary C's, the mirror A's); the probe prints only the union, so that split is D7's measurement, not this output |
| A4 | S2, ledger kept: generation 2 in the union both ways, N *behind*, generation 2 in force | `critique-security-and-fit.md` P3 step 5; `digest.md` §3 item 6 | `attack.txt` S2 | `BEHIND (node holds g1) \| in force: head g2 [... "agent":[]]`, both ways | the same | yes | |
| A5 | S2, KeePassLib Synchronize deletes N again in both variants | `critique-security-and-fit.md` P3 step 5 | `attack.txt` S2, `control` | `N not in the result`, pruned and kept | the same | yes | into A only, for each of the two variants |
| A6 | one slow merge took 7.4 s to 18.2 s | `critique-security-and-fit.md` §5 | `attack.txt`, the `keepassxc-cli` lines | 7.4 s to 18.2 s over the six merges | 6.7 s to 12.1 s | differs | timing, noise |
| A7 | S3, Create new IDs of a copy: a group named after the database sits under the root holding `KPG`, `KPG.Authority.a1` and `KPG.Client.c1` | `critique-security-and-fit.md` P5 step 2, §2 (b) | `attack.txt` S3 | `group [Probe] root=False parent=[Probe] custom data: KPG \| KPG.Authority.a1 \| KPG.Client.c1` | the same | yes | |
| A8 | S3, after Delete authority the dictionary no longer holds the authority and the imported group still does | `critique-security-and-fit.md` P5 step 3; `digest.md` §5 | `attack.txt` S3 | `database dictionary holds it: False; groups holding it: [Probe]` | the same | yes | the root and the imported group are both named `Probe`; the probe removed the key from the root itself, so the group named is the imported group |
| A9 | S4, the union `in.1`, `in.2`, `out.1`, and `writer` in force | `critique-security-and-fit.md` P8 step 4; `digest.md` §2, §3 item 4 | `attack.txt` S4 | `writer in force: True` | the same | yes | |
| A10 | S5, a file made 4.1 by a group tag, merged by `keepassxc-cli` 2.8.0 either way: every `KPG.` key of the dictionary carries a time, keys already there with the same value included | `critique-security-and-fit.md` P11; `digest.md` §4 | `attack.txt` S5 | `base kdbx 4.1`; into P 2 of 2 keys timed, into C 4 of 4 | the same | yes | 2.8.0 only, as V10 |
| A11 | S5b, a plain `keepassxc-cli` 2.8.0 save of that P: every `KPG.` key carries a time | `critique-security-and-fit.md` P11 | `attack.txt` S5b | 4 of 4 timed, `kdbx 4.1` | the same | yes | |

## Claims no probe here backs

**Presented as measured, with no probe or output behind them.** These are what the branch cannot
yet vouch for from this family's probes; where a check could be made read only, it was.

| Claim | Quoted in | What was found |
| --- | --- | --- |
| The line counts of fourteen product files, "at `a42e38aa` [M here, `wc -l`]": `ProfileNames.cs` 277, `PendingChanges.cs` 971, `AdmissionStore.cs` 854, `AuthorityStore.cs` 643, `AuthorityKeys.cs` 267, `RenewalRefusal.cs` 150, `DatabaseWriter.cs` 2179, `AclResolver.cs` 537, `AclDocument.cs` 294, `GrantPanel.cs` 2004, `NodeDialog.cs` 457, `GateTab.cs` 486, `ClientList.cs` 1688, `GroupDialogs.cs` 1228 | `critique-security-and-fit.md` §2 (e) | No output was recorded. Checked here, read only: `git cat-file -p a42e38aa019d572c5a10b489e6aadeea91ef91c1:src/KeePassGate/<path> \| wc -l` gives all fourteen as quoted |
| That the `keepassxc-cli` 2.8.0 used carries a valid Authenticode signature from DroidMonkey Apps, LLC | `critique-survival.md` §1; `critique-security-and-fit.md` §5 | No output was recorded. `tools\KeePassXC.ps1` makes that check on every call, and every run here passed it |
| That `KPXC_RANDOM_SLUG` is fresh at every save | `design.md` §11 | The probe prints the key's name, not its value (D14); the design also cites the spike for it |

**A count read from source, checked the same way.** "The 44 operations" of `Spec/openapi.yaml`
(`critique-security-and-fit.md` §2 (a)) is marked as read, not measured. `git grep -cE
"^\s+operationId:" a42e38aa019d572c5a10b489e6aadeea91ef91c1 -- src/KeePassGate/Spec/openapi.yaml`
gives 44.

**Measured by another family, and not run here.** The papers also quote measurements of the ground
reports, which their own families carry, all under `spikes\storage\probes\`: `[M keepass §n]` in
`ground-keepass\`, `[M keepassxc §n]` in `ground-keepassxc\`, `[M others §n]` in `ground-others\`,
`[M spike §n]` and `[M measured §n]` in `ground-measured\`, and `[M inventory §n]` (the sizes of an
authority, a record and a rule that `design.md` §1 multiplies) in `ground-inventory\`. The Android
measurements the digest calls `M Android` (`papers/ground/measured-android.md`) are in
`spikes\storage\android\`. `[M MergeInRealDatabaseTests]` (`design.md` §1) and `FINDINGS.md:417`
(`critique-survival.md` S3) are the product's own tests and findings, at `a42e38aa`. Claims marked
`[R ...]` or `[A]` are reading and assumption, and no probe is owed for them; the arithmetic of
`design.md` §1 and `critique-security-and-fit.md` §2 (a) and P7 is marked as such by the papers.

## Changes made to run them here

- **Only the lines naming programs on the agent's machine changed.** In `design\scripts\Probe.ps1`
  the full path of `KeePass.exe` in another worktree and the four full paths of `keepassxc-cli.exe`
  in another agent's download directory, `$XcRoot` and the `$Cli` table, are replaced by
  `Get-KeePassExe` and `Get-KeePassXCCli -Version <release>` for the same four releases. In
  `design\scripts\Members.ps1` the path of `KeePass.exe` is replaced by `Get-KeePassExe`. In
  `survival\scripts\Chain.ps1` and `security-and-fit\scripts\Attack.ps1` the paths of
  `KeePass.exe` and of `keepassxc-cli.exe` 2.8.0 are replaced by `Get-KeePassExe` and
  `Get-KeePassXCCli -Version '2.8.0'`. Each change carries a comment recording it. KeePass's file is
  the same: `.keepass\2.62\KeePass.exe` hashes to
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`, as the papers quote.
- **The tools are dot-sourced in a child scope**, `& { . <tool>; Get-... }`, rather than into the
  probe's own scope, because both tools set `Set-StrictMode -Version Latest`, which would then
  apply to the probe as well and make it run under rules it was not measured under. The
  `rules-central` family does the same.
- **Nothing else in the probes changed**, including what is inaccurate and harmless: the usage line
  of `Probe.ps1` names a task `wipe` its `switch` does not have (the wiped case is part of
  `keepass`), and that of `Attack.ps1` names four of its six tasks.
- **A `run.ps1` for each paper's directory**, added on the branch, since the agents ran the tasks
  by hand and kept no script. Each passes the arguments the probe's own first lines document, with
  full paths, and the work directory `work\` beside it, which is where each agent made its
  databases.
- **`compare.py`**, added on the branch, as described under "Running them".

**Recorded output.** Copied as the runs left it, UTF-8 with a byte order mark and CRLF. In the
design's eight `keepassxc-cli` outputs, `xc-*.txt` and `xcsave-*.txt`, the `cli` lines held the
full path of the agent's working directory, a session scratch directory under a user's profile, 54
times in all; it is replaced by the branch path `spikes\storage\probes\hardened-custom-data\design`,
and nothing else in them changed. `make2.txt`, `keepass.txt`, the survival critic's four files and
`attack.txt` held no machine path, user name or session identifier (the two critics' probes print
`<work>` for their work directory), and are byte for byte the originals. The `keepassxc-cli` lines
are in German where the release answers in the machine's language, 2.7.12 and 2.8.0, as on the
machine that ran them, and so are this run's.

## Not carried

- **Every database**: 50 `.kdbx` files of the design's runs, 19 of the survival run and 35 of the
  security run, all made by the probes, which make them again (this run made the same numbers).
- **The design's `out\make.txt`**: two runs of an earlier state of `Probe.ps1`, each stopped after
  the first line of its dump. `make2.txt`, the run of the probe as it stands, superseded it, and
  `design.md` §13 names that run.
- Nothing else was in the three directories: no clone, no download and no build output. The
  KeePassXC releases the probes started were in another agent's directory, and the branch provides
  the same releases through `tools\KeePassXC.ps1`.

## Caveats and defects

- **No defect was found that changes a quoted result.** None of the three probes is the plugin:
  the design's probe and the survival probe measure the storage and leave the read rule to
  reasoning, which both papers state, and the security probe's `Classify` is that critic's coding of
  `design.md` §2, so A2 to A4 hold for the design as written there, not for any resolver.
- **The fast cells depend on time.** KeePassXC merges no database custom data when both of its loads
  fall within one second (`design.md` §10 case 1). At 0.1 s to 0.2 s per merge that held in both
  runs, but a merge that happens to straddle a second boundary gives the slow cell's result, so a
  rerun of D11 can come out differently without anything being wrong. The slow cells depend the
  other way round, and their KDF makes each load take seconds.
- **Language and culture.** `keepassxc-cli` 2.7.12 and 2.8.0 answer in the machine's language, and
  `xctimes.txt` prints its times in the current culture's format; both were German in the recorded
  run and in this run. `compare.py` knows the German and the English change lines only.
- **The timings** of D9, V11 and A6 are single runs on a machine doing other work, and they moved
  by up to a factor of fourteen (V11's 82.8 s against 6.0 s). They are not numbers to compare
  designs by.
