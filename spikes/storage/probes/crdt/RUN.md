# Probes of `crdt`

The option that keeps every datum the plugin stores as ordinary entries written once, in one group
of its own, and reads them as conflict-free replicated data types: a two-phase set, a remove-wins
set and registers whose concurrent versions meet as the narrower, with a head carrying every
decision and deletion records as witnesses. Four papers quote programs that ran for it, all under
`papers\options\crdt\`: the design (`design.md`, written as `option-crdt.md`), its survival
critique (`critique-survival.md`), its security critique (`critique-security-and-fit.md`) and its
formal critique (`critique-formal.md`). The digest (`digest.md`) repeats many of their numbers. In
the tables below "survival P1" means the problem P1 of the survival critique, "security M6" the
problem M6 of the security critique, "digest 3" section 3 of the digest, and so on.

**The design's probe and the formal critic's are executable models**, and they live in
`spikes\storage\model\`, with a `RUN.md` of their own that traces every number the design and the
formal critique quote from them. This file covers the other two critics' probes:

| Directory | Paper | What runs | Command, from the branch root |
| --- | --- | --- | --- |
| `survival\` | the survival critique | `probe\facts.py`, `probe\bridge.py` and `probe\order.py` over KeePassLib 2.62 through `probe\host.ps1` and `probe\Crdt.cs`, and `keepassxc-cli`; `model\model_ext.py` over the design's model | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\crdt\survival\run.ps1 all` |
| `security-and-fit\` | the security critique | `scripts\counts.ps1`, written here: the agent kept its output and no command | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\crdt\security-and-fit\scripts\counts.ps1` |

`run.ps1` also takes one probe at a time: `facts`, `replay`, `random`, `random1`, `ext` or `order`
(section 1). The design's agent left no probe of its own beside the model (section 9), so there is
no `design\` here. The critics' directories are named `survival\` and `security-and-fit\`, as
`probes\README.md` lays out and as the papers' paths already name them; the formal critic's is
`spikes\storage\model\critic\`.

**Every probe here ran again from the branch on 2026-11-15 and 16, and every line of every recorded
output came out the same but for the running times a run prints and, in `facts.txt`, the moment of
the run and one identifier KeePassLib draws at random** (section 5). Every outcome the papers quote
from them holds, on real files as in the models. Two statements about what was run differ: the
agent's total machine time, and survival P6's reading that none of the 19 steps where a file
disagreed with the model changed what the read rule decides, since in five of them the file reads
whole where the model predicts an incomplete store; nothing widens there (section 1). Three things
quoted as run had no command or output on the branch and were run again by scripts written here:
the design's model on survival P2's history, the KeePassDX searches of `ext-results-dx.txt`, and
the security critique's counts. All three agree with what the papers state, and they are marked as
written here wherever they appear. Section 8 lists the claims no output here shows.

Each command writes into `out\` beside it, which is not committed, and each file it writes that has
a record has its recorded copy under the same name in `recorded\`. The databases go to `work\`
beside the probe, which is not committed either and which each probe empties first; none is kept.
After the commands, one more command compares every fresh output with its record:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\crdt\compare.py
```

It masks what section 5 names, compares `counts.txt` by what it counts, holds the two
reconstructions to what the papers state, and exits 0 when nothing differs. What a run needs:
Windows PowerShell 5.1, which loads KeePassLib and compiles `Crdt.cs` with the .NET Framework's own
compiler through CodeDom; `uv`, which provides Python and, for `order.py`, `pykeepass`; and git, for
`tools\Product.ps1`. KeePass 2.62 and the four KeePassXC releases are provided and checked by
`tools\KeePass.ps1` and `tools\KeePassXC.ps1`, which `run.ps1` calls before any probe starts. No
.NET SDK is needed, and nothing shows a window.

## What the agents ran, and that the branch runs the same programs

- **KeePass.** The survival agent loaded `.keepass\2.62\KeePass.exe` of the product's worktree,
  whose SHA-256 is that of `.keepass\2.62\KeePass.exe` here, which every run loads:
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`.
- **KeePassXC.** The agent ran `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 out of the
  `keepassxc` ground report's downloads. Each of the four executables is byte for byte the
  `keepassxc-cli.exe` that `.keepassxc\<version>\` holds here (SHA-256 compared).
- **`Crdt.cs`** is compiled by `host.ps1` through CodeDom when the compiled library is missing or
  older than the source, as the agent's was. The agent's `Crdt.dll` was not carried; the branch
  compiles the unchanged source the same way, into `survival\bin\`.
- **Python.** The agent's bytecode caches are CPython 3.14's; the branch ran 3.14.8 through `uv`.
  `order.py` needs `pykeepass`, whose version the agent did not record; the branch's run resolved
  `pykeepass` 4.2.0 with `lxml` 6.1.3. `-I` makes Python ignore `PYTHONHASHSEED`, so every run had
  a string hash seed of its own, and every random choice is drawn from a `random.Random` with a seed
  in the source or on the command line.
- **The design's model.** `survival\model\crdt_model.py` is byte for byte
  `spikes\storage\model\crdt_model.py`, with the SHA-256 the survival critique's header quotes,
  `ED5F93CD…4707F`.
- **Which source wrote which record.** Every recorded output was written after its probe's source
  and the sources it imports were last changed: `facts.txt` at 20:22:10 after `facts.py` at
  20:21:58, `ext-results.txt` at 20:29:03 after `model_ext.py` at 20:26:57, `bridge-replays.txt` and
  `bridge-random-1.txt` at 20:31 and `bridge-random-2.txt` at 20:37:47 after `bridge.py` at
  20:30:52, `order.txt` at 20:41:59 after `order.py` at 20:41:53. `ext-results-dx.txt` was written
  at 20:43:19, after every source, by a command that was not kept.

## 1. `survival\`: the survival critique's probes

**What they measure.** `Crdt.cs` makes real `.kdbx` files in the design's shape (a store group
whose identifier begins `KPG/STOR`, a recycle bin, one user entry, AES-KDF one round, password
`test`) and changes them only through KeePassLib 2.62: elements and records added, a merge by any
of KeePassLib's five methods, a move into the bin, an unchanged save that moves the time, a hand
edit, a permanent delete, the bin emptied, every record cleared, Delete Duplicate Entries, and a
dump. `host.ps1` serves it over a pipe to `kp.py`, which also drives `keepassxc-cli`.

- `facts.py`: the five transport facts F1 to F5 on single files. Cited by survival 2, P1, P3, P4,
  P6, 4 and 6, and by the digest (sections 2 and 3).
- `bridge.py replay`: the histories R1 to R9 on real files, each as written and with the bin read
  for every kind, which is P1's change. Every gate act is the design's own write path out of the
  model; every other step is done on the file by KeePassLib or `keepassxc-cli`, and the design's
  read is applied to what the file then holds. Cited by survival 1 to 3 and 6, and by the digest
  (sections 2, 3 and 7).
- `bridge.py random 2000 2`: 2,000 random histories through the same bridge, every step compared
  with the model's prediction. `random 40 1` is a smaller run the agent left beside it. Cited by
  survival 1, P6 and 9.
- `model_ext.py 8000`: the design's model with what it leaves out added (where an entry sits, the
  bin, KeePassDroid's unchanged Save, KeePassLib's strict rule for a record, each merge's
  relocation), 16 searches of 8,000 histories. Cited by survival P1, P3 and 9.
- `order.py`: the order of a head's twelve custom strings as KeePassLib and each KeePassXC release
  write them, read back with `pykeepass`. Cited by survival 2.

**Command and output.** `run.ps1 all`, or one probe by name. The outputs are `out\facts.txt`,
`out\bridge-replays.txt`, `out\bridge-random-2.txt`, `out\bridge-random-1.txt`,
`out\ext-results.txt` and `out\order.txt`; their records are in `recorded\`, with
`recorded\ext-results-dx.txt` beside them, which no command here writes.

**How long.** About eight and a half minutes for the six run in sequence, with the design's
model running beside `random` and `ext`: `facts` 4 to 5 s, `replay` 5 s, `random` 389 s (the
record's first line reads 354 s), `random1` 8 s (7 s by its own line, 6 s in the record), `ext` 95 s
(123 s in the record), `order` 2 s. A run leaves 87 databases in `work\`, as many as the agent's
run left in its own.

**Compared.** `facts` and `replay` twice, the others once: every line equal to the record but the
masked lines of section 5.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| 2,000 random histories over two and three copies, 9,602 real steps through KeePassLib's five methods, four KeePassXC releases, deletions with and without a record, the bin, KeePassDroid's unchanged Save, Empty Recycle Bin, cleared records and imports | survival 1, 9 | `random histories: 2000, steps done: 9602` | the same | yes | the alphabet holds every kind named; the five methods are Synchronize, the three of `kp_other` and Create new IDs as `import` |
| Each file agreed with the model's prediction at every step but 19 | survival 1, 9 | 19 | 19 | yes | |
| No failure of the design's checks in the random histories | survival 9 | 0 | 0 | yes | |
| The 19: 8 under OverwriteExisting, 10 under KeepExisting, an element's time or a hand-edited head taken or not, and one under Create new IDs, which removed elements KeePassXC had brought back beside their records | survival P6 | as claimed | the same | yes | each `DISAGREE` attributed to the step it names; no disagreement is about where an entry sits |
| None of the 19 altered what the read rule decides: "since elements never change" | survival 1, P6 | 13 are an element's modification time; 5 are a hand-edited head the model predicted would arrive and the file did not take; 1 is the import's three entries | the same | differs | a reading of the 19: a time is not read by the read rule, and the import's three were elements KeePassXC had brought back beside their records, already out of force; but a hand-edited head is damage, which makes the store incomplete, so in those 5 the file reads whole where the prediction refuses (history 1671: file 2 `ok`). Both answers are inside the checks, none of which failed, so nothing widened |
| F1: an entry and its record in the same second: all four methods that match identifiers, both directions, kept the entry and dropped the record; at 101 the entry went | survival P3, P6, 4 | as claimed, 8 cells and the cell at 101 | the same | yes | |
| F2: an old head and an old version pruned at 200 and saved unchanged on the phone at 300: the four methods, both directions, kept both and dropped both records, in the gate's copy too; KeePassXC brought them back and kept the records beside them | survival P3, 4; digest 2 | as claimed, 8 cells and four releases | the same | yes | |
| F3: a head binned at location time 400: Synchronize put it into the other copy's bin in both directions; the other three methods left the other copy's placement alone; every KeePassXC release moved it into the other copy's bin, keeping that copy's own location time | survival P1, P6, 4; digest 2 | as claimed | the same | yes | the move into the bin is the probe's own (section 7) |
| F4: a witness that names nothing, in one copy, kept by that copy under all five methods and four releases, carried only by Synchronize | survival 2 (T9), 4 | as claimed | the same | yes | |
| Create new IDs gives the store group a new identifier too, so the copies land in a group without the prefix | survival 2 (T11), 4 | `NEWUUID(...)[... O:KeePassGate]` in F4 and F5 | the same | yes | |
| F5: Create new IDs, then Delete Duplicate Entries deleted four entries, the original authority, profile addition, rule version and user entry, each with a record dated at the moment, kept the imported copies; a third copy synchronized with the result lost the same originals | survival P4, 4, 6; digest 3 item 8 | `ok 4`, the four records, the copies left | the same, the moment and the user entry's random identifier masked | yes | |
| A head of 12 custom strings, written by KeePassLib and rewritten by each KeePassXC release, comes out in name order each time | survival 2 | as claimed | the same | yes | KeePassLib's row shows it sorting; the KeePassXC rows cannot tell sorting from keeping what they read (section 7) |
| R1: revoke, Delete its entry, Empty Recycle Bin, a KeePassXC merge into a stale copy, Delete the new head there: B whole and admitting the revoked `c0`; A refusing `c0` | survival 1, P2, 6; digest 3 item 6 | `FAIL safety` and `FAIL one-loss` at the last step, file 1 `ok, admitted ['c0']`, file 0 `admitted []` | the same | yes | |
| R2: B whole and the root granting `read` again, the last step alone making it wider | survival 1, P1, 6; digest 2, 3 | `FAIL safety`, `FAIL one-loss`, file 1 `ok`, root `r` | the same | yes | |
| R3: KeePassDroid's unchanged Save and KeePass only: the gate's own copy granting `read` at the root | survival 1, P3, 6; digest 2, 3 | file 0 `ok`, root `r`, `FAIL safety` | the same | yes | |
| R4: two gate copies and Synchronize only: B asking again about `c0`'s renewals | survival 1, P3; digest 2, 3 | `FAIL safety ... renewal of c0 asked` | the same | yes | |
| R5 to R8, the design's scenarios 7, 9, 18 and 20: 7 incomplete, 9 widens, 18 and 20 hold | survival 2, 6; digest 3 item 9 | as claimed | the same | yes | |
| R9: a third copy that only ever synchronized granting `read` at the root after two Deletes | survival 1, P1, 6; digest 3 item 6 | file 2 `ok`, root `r`, `FAIL safety` | the same | yes | |
| KeePassXC was 2.6.6 in these replays | survival P1 | `[keepassxc-cli 2.6.6]` on every KeePassXC step | the same | yes | the bridge draws it with seed 7 |
| R1 to R4 and R9 step counts: 5, 4, 6, 5, 5 | digest 3 item 6 | as claimed | the same | yes | |
| With P1's change, R1 to R4 and R9 pass on real files; R6 still widens | survival 1, P1, P3, 6; digest 2, 3 | `no failure of the checks` in the five, `FAIL safety` in R6 | the same | yes | |
| The extended model found R4 unprompted | survival P3 | `ext-results.txt`, P1 with 2 files: the shortest `safety` is R4's history | the same | yes | |
| As written, the extended model fails X with KeePassXC through R2's history | survival P1 | X with 2 files: `one-loss` and `safety`, R2's history | the same | yes | |
| 16 searches of 8,000 histories in `ext-results.txt` | survival 9 | eight configurations, 2 and 3 files | the same | yes | |
| With P1's change no failure in any configuration: Synchronize and Keepass2Android with the bin, KeePassDroid's Save, Empty Recycle Bin and restore, and the same with KeePassXC and the import methods (F and F2, 32,000 histories) | survival 1, P1; digest 2 | no failure in the four cells | the same | yes | |
| The same with KeePassDX (DF, 16,000 histories), completing 48,000 | survival 1, P1; digest 2 | `ext-results-dx.txt`: no failure in four cells | no command | not run | `scripts\ext_dx.py`, below, searched the same four configurations again with seeds of its own: no failure |
| The design's model, unchanged, on `revoke 0 c0; del 0 (the revocation) with a record; merge xc 1 0; del 1 (the newest head) without a record`: file 1 refuses `c0` after step 3 and admits it after step 4, failing `safety` and `one-loss` | survival P2; digest 3 item 4 | no output kept | as claimed | yes | `scripts\p2_history.py`, below |
| The same model on every single deletion after step 3 | survival P2, 9 | no output kept | of 72 single deletions only one widens, the newest head in file 1 without a record | yes | the critique states no result for the others; `scripts\p2_history.py` |
| "Total machine time about 12 minutes, one process at a time" | survival 9 | none | about eight and a half minutes for the same commands | differs | the agent's session is not recorded; its total may hold runs that were not kept |

### Two scripts written on the branch

**`scripts\p2_history.py`.** Survival P2 quotes the design's model, unchanged, on its history "and
on every single deletion after its step 3: one command, quoted in P2", and quotes no command. This
replays the history through `survival\model\crdt_model.py`, prints file 1's value after every step
and the model's own checks, and then runs the first three steps followed by each of the 72 single
deletions the model's alphabet has (twelve kinds of store entry, in either file, with a record,
with none and with a record dated before the entry). It writes `out\p2-history.txt`, in under a
second:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\crdt\survival\scripts\p2_history.py
```

**`scripts\ext_dx.py`.** `recorded\ext-results-dx.txt` holds four searches with KeePassDX's merge,
which none of `model_ext.py`'s configurations has, and the command that wrote it was not kept. This
takes the configurations from the record's own labels (Synchronize, Keepass2Android and KeePassDX;
Delete to the bin, a deletion with a record, Seal again; with and without the bin read for every
kind), draws 8,000 histories of up to nine steps for each through `model_ext.search` with seeds
900 to 903 chosen here, and writes `out\ext-results-dx-again.txt`, in about 25 seconds. Its four
lines are the record's four, word for word; the seeds are not the record's, so this shows the same
result over other histories, not the same run:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\crdt\survival\scripts\ext_dx.py
```

## 2. `security-and-fit\`: the security critique's counts

**What it measures.** Line counts of thirteen files of the product, the files that mention
`CustomData`, the lines that name the recycle bin per file, and four lines of the design's model.
The critique states that nothing else was run. Cited by security S3, M6 and lens (f), as `[M here]`.

**Command and output.** The agent kept `counts.txt`, now `recorded\counts.txt`, which the critique
names as `spikes\storage\probes\crdt\security-and-fit\counts.txt`; its first line records how the
lines were counted, `[System.IO.File]::ReadAllLines(...).Count`, and no command was kept.
`scripts\counts.ps1`, written here, counts each again over the repository's own history at the
commit `tools\Product.ps1` names: it exports the thirteen files into `.product\` and counts their
lines as the agent did, lists the files that mention `CustomData` with `git grep -l`, counts the
lines matching `RecycleBin|InRecycleBin|IsInBin` per file with `git grep -c`, which counts lines as
`rg -c` does, and prints lines 206 to 209 of `spikes\storage\model\crdt_model.py`. It writes
`out\counts.txt`, in about two seconds.

**Compared.** Twice, by `compare.py`, number by number, since the script words its lines its own
way.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| Lines today: `CustomDataStore` 110, `AuthorityKeys` 267, `AuthorityStore` 643, `AdmissionStore` 854, `RenewalRefusal` 150, `ProfileNames` 277, `PendingChanges` 971, `NodeDialog` 457, `AclResolver` 537, `DatabaseWriter` 2,179, `OpenDatabaseTargets` 1,642, `GroupDialogs` 1,228, `KdbxVault` 2,349 | security lens (f) | as claimed | the same | yes | |
| 32 source files mention `CustomData` today | security lens (f) | 32 | 32, each a `.cs` file | yes | |
| The recycle bin alone is excluded in 20 places in `KdbxVault.cs` | security M6 | `Bridge\KdbxVault.cs 20` | 20 lines, and 20 occurrences | yes | the count is of lines naming the bin; that each is an exclusion is the critique's reading, not counted |
| The model counts as damage only an entry under its own element's identifier, `crdt_model.py:206-209` | security S3 | the line quoted | lines 206 to 209 hold it, line 208 the quoted condition | yes | |

## 3. The digest

The digest repeats these probes' numbers; each is in a table above or in
`spikes\storage\model\RUN.md`. The claims it adds or rounds:

| Claim | Quoted in | Reproduced | Note |
| --- | --- | --- | --- |
| "48,000 histories clean [model surv]" | digest 2 | yes for 32,000; not run for 16,000 | the KeePassDX third has no command, section 1 |
| "with P1 clean beside Synchronize [model surv]" for KeePassDX 4.5.8 | digest 2 | not run | the same third |
| "after a new authority, sealed copies go by place [model surv]" | digest 3 item 8 | not run | a reading of `crdt_model.py:276-280` in survival P4, section 8 |
| "the design's model refutes its one-loss claim [model design, surv]" | digest 3 item 4 | yes | `scripts\p2_history.py` |

## 4. Machine paths and what else was replaced in what was copied

The sources and every recorded output were searched for a user's profile directory and the
application data under it, the scratch directory, the session identifiers, the machine's name and
the product's worktree. Three files held any, and in each only the path was replaced:

- `security-and-fit\recorded\counts.txt`, line 1: the product's worktree, named by its full path,
  became "the product", giving "Measured 2026-11-15 in the product at a42e38aa".
- `survival\probe\kp.py` and `survival\probe\host.ps1`: the scratch directory's full path, the
  product worktree's copy of KeePass.exe and the KeePassXC releases in the `keepassxc` ground
  report's downloads, which section 6 replaces with the branch's.

No other file held any; the other recorded outputs are byte for byte the agent's. The string
`kpg-crdt/` in `kp.py` is the prefix the probe hashes into the identifiers it derives, the plugin's
abbreviation rather than anybody's name, and stays as the agent wrote it.

## 5. What a run prints differently, and why that is noise

- **Running times**: the end of the first line of `bridge-random-1.txt` and `bridge-random-2.txt`,
  and the last line of `ext-results.txt`. They depend on the machine and on what runs beside it.
- **`facts.txt`, section F5.** `PwDatabase.DeleteDuplicateEntries` dates each deletion record at
  the moment it runs, printed in seconds after 2026-01-01 (`@27544930` in the record), and one of
  the entries it deletes is the user entry, whose identifier `PwEntry` draws at random when the
  probe makes the database (`aaa85ae6` in the record). The dump lists records sorted by name, so the
  random identifier also decides where that record stands. `compare.py` masks both and sorts the
  records of such a line; everything else in the line is compared as it is. The two lines are the
  lines the critique reads for "each with a record dated now".

## 6. Every change made to run the probes here

- `survival\probe\kp.py`: `ROOT`, the scratch directory, replaced by `SURVIVAL`, the probe's own
  directory found from the file, and `BRANCH`, the branch root five directories above it; `HOST`
  built from `SURVIVAL`; the four KeePassXC paths built as
  `<branch root>\.keepassxc\<version>\keepassxc-cli.exe`, which `tools\KeePassXC.ps1` provides, in
  the same order. A comment there records the change.
- `survival\probe\facts.py`, `bridge.py` and `order.py`: each path built as
  `os.path.join(kp.ROOT, 'crdt-survival', ...)` now built as `os.path.join(kp.SURVIVAL, ...)`, two
  lines in each.
- `survival\probe\host.ps1`: KeePass.exe through `Get-KeePassExe` of `tools\KeePass.ps1` in place
  of the product worktree's copy; the probe's directory taken from `$PSScriptRoot`; the compiled
  `Crdt.dll` written into `survival\bin\`, which the branch ignores, rather than beside the source,
  where it would be an untracked build output in the tree. `tools\KeePass.ps1` prints nothing when
  KeePass is already provided, which matters because `kp.py` reads `ready` as the host's first
  line; `run.ps1` therefore provides it before any probe starts.
- `survival\probe\Crdt.cs`, `survival\model\crdt_model.py` and `survival\model\model_ext.py`:
  unchanged, byte for byte.
- `survival\run.ps1`: added, the commands the agent typed and did not keep, with the arguments the
  records show (`random 2000 2` and `random 40 1` by the files' own names, `model_ext.py 8000` by
  the record's counts). It provides KeePass and the four KeePassXC releases and creates `out\`,
  which the probes write into and do not make.
- `survival\scripts\p2_history.py`, `survival\scripts\ext_dx.py` and
  `security-and-fit\scripts\counts.ps1`: added, sections 1 and 2.
- `compare.py`: added, the comparison above.

## 7. Defects and weaknesses found in the probes

Reported, not fixed; none changes an outcome the papers draw.

- **The KeePassXC rows of `order.txt` cannot tell sorting from keeping.** Each release rewrote a
  head it read from a file KeePassLib wrote, whose strings were already in name order, so the rows
  show that no release reorders them, not that a release would sort strings that arrived in another
  order. KeePassLib's row does show sorting, since `Crdt.cs` sets the strings in another order. The
  change the critique draws from it, a MAC over the names in ordinal order, does not depend on
  either.
- **The bin, the unchanged Save and the emptied bin are the probe's imitations.** `bin`, `touch`
  and `empty` in `Crdt.cs` do through KeePassLib's object model what the critique reads or the
  emulator measured KeePass's Delete, KeePassDroid's editor Save and Empty Recycle Bin to do; they
  are not those commands run. What F3 and the replays measure on real files is what each merge
  does with the result. The critique marks the commands' own behavior as read or as measured
  elsewhere, so no claim states more than this; "a Delete" in R1 to R4 and R9 means the imitation.
- **The probe's `KPG.Mac` is a SHA-256 over kind and data, with no key.** It stands for the
  design's MAC only as far as the probe needs: a hand edit changes it. No claim here rests on a key.
- **`ext-results-dx.txt` has no command.** Section 1.
- **The bridge compares an element's time, intact flag and place with the model's prediction, and
  not its fields.** "Never field by field" in survival 2 (T2) rests on the intact flag, which covers
  `KPG.Kind` and `KPG.Data`, the strings the read rule reads.

## 8. Claims no probe or output on the branch vouches for

Each is quoted as measured or as a result, or rests on a number, but no output here shows it.

- **Survival P4 and digest 3 item 8: a copy whose original's rule cannot be found gets "the
  place's grant" (`crdt_model.py:276-280`), so sealed copies go by place once an authority is made
  again.** A reading of the model's source, marked `[model]`; lines 276 to 280 hold the rule it
  describes, and no history was run.
- **Survival P3: "the model deletes an entry whose time equals the record's (`crdt_model.py:609`,
  `<=`)", and P2: why the design's search missed P2's history.** Readings of the source; line 609
  holds the comparison, and `main` and `alphabet` hold the exhaustive alphabets the explanation
  describes. Not runs.
- **Survival P1: a full walk of the tree's entries "is assumed to cost about the same" as 0.26 ms,
  and P2: a fresh refusal entry "shortens the time a refusal has one carrier".** Marked `[A]` by the
  critique.
- **Security M4: "some 750 a year", "about 140 entries", "some 20 KB", and What holds 6:
  "microseconds to about a millisecond".** Arithmetic or assumed, marked so.
- **Survival 9: "about 12 minutes".** Section 1.

The papers also quote measurements of other families as `[M android]`, `[M keepass]`,
`[M keepassxc]`, `[M spike]`, `[M store-entries]`, `[M crit rules-linked]` and `[R ...]`. Those are
traced where their probes are, not here: among them the read of 5,000 deletion records in 0.08 ms
and the 0.26 ms lookup over 10,054 entries (security M4, survival P1) under `store-entries`, the 49
and 27 test files (security lens (f)) under `store-entries` and `rules-linked`, and every statement
about Keepass2Android, KeePassDroid and KeePassDX under `spikes\storage\android\`.

## 9. What was not carried, and why

The probes' sources and every recorded output were carried, the outputs from the agent's `out\`
into `recorded\`. Nothing else was:

- **The design's agent's directory, `crdt\`**, held no probe: the design's probe is its model, which
  is under `spikes\storage\model\`. Its ten scripts edited the design's text and the model in place
  on their way to the versions carried; `spikes\storage\model\RUN.md`, section 8, names them.
- `crdt-survival\probe\Crdt.dll`: the agent's build of `Crdt.cs`, which `host.ps1` builds again.
- `crdt-survival\probe\__pycache__\` and `crdt-survival\model\__pycache__\`: bytecode.
- `crdt-survival\work\`: 87 databases, which the probes make again every run, and four `spec.txt`
  files, the last list of additions and records each probe handed `Crdt.cs`, which each run writes
  again.
- `crdt-security-and-fit\` held nothing but `counts.txt`.
- The formal critic's directory belongs to the model: `spikes\storage\model\RUN.md`, section 8.
- None of the directories held a clone, a download or a log.
