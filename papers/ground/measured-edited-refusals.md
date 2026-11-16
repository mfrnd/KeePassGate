# Measured: refusal records of the store, edited by hand and then merged

Run 2026-11-16 on a Windows machine, for the storage rethink. The store `EVALUATION.md` section 4
recommends keeps every datum as an entry written once, and its first version put the check, a MAC
under a store key, on the records that grant alone, on the argument that an edited refusal can
only refuse. An adversarial reading argued the opposite: changing a single character of a refusal
makes a newer version of that entry, the newer version wins a merge, and if the reading rule then
no longer recognizes it as that refusal, what it refused is back, through KeePass's own Synchronize
and without any deletion. That argument had not been run. This report runs it: four kinds of
refusal and, as a control, a record that grants; ten kinds of hand edit in a copy; every merge
method of KeePassLib 2.62 and `keepassxc-cli merge` of four releases, in both directions; and five
reading rules side by side, since the design is still being decided.

**Headless.** Every database was made, edited, merged and read by KeePassLib 2.62 inside a console
process, or merged by `keepassxc-cli` started without a window. No KeePass or KeePassXC window, no
phone, no emulator and no WSL. Every database, key and certificate is invented.

**The evidence** is the probe family `spikes\storage\probes\edited-refusals\` in the working tree of
the branch `spikes/storage-survival`, not committed: the probe, the reading rules in `rules.py`,
the command `run.ps1`, the dumps and tables of the run reported here in `recorded\`, and `RUN.md`,
which holds every figure quoted here beside a second run's value. The whole run was made twice, and
every output of the second run is the same as the record line for line, apart from the times.

**Everything in the tables is measured**: a file KeePassLib or `keepassxc-cli` wrote, read back with
KeePassLib 2.62, and a verdict computed from its dump by the rules of section 2. What follows from
the counts and was not itself run is marked **reasoning**.

## 1. The store as built

### 1.1 Where the shapes come from

`EVALUATION.md` section 4 as it stood in the working tree on 2026-11-16 at 08:27 UTC, SHA-256
`980ce06c99ede2bde5fccd08d60043d10754f983dff55b70e3059edee5bcb803`: rule 2 of 4.1 (custom strings
whose names begin `KPG.`, the secret parts protected, a check under a store key over those strings
and nothing else, the store key a field of the authority's document), rule 4 (an entry counts
wherever it sits, the recycle bin included), the witness and the table of 4.2. The table of 1.2
names every place where this probe departs from that text. Nothing of the product was compiled.

### 1.2 The shapes, field by field

Every store entry holds the five standard strings, `Title` as in the table below, `UserName`,
`Password` (protected) and `URL` empty, and `Notes` holding
`KeePassGate data. Managed on the KPG tab in KeePass.`, and three custom strings:

- `KPG.Kind`, unprotected: the record's kind.
- `KPG.Data`, the payload: a JSON object, protected for the authority only.
- `KPG.Mac`, unprotected: the check, in lower-case hex. It is HMAC-SHA256 under a store key over the
  SHA-256 of a text made of every string of the entry whose name begins `KPG.`, other than
  `KPG.Mac`, in ordinal order of name, each as its name, a NUL, its value and a line feed, in UTF-8.
  The title is not covered.

Each entry's identifier is drawn at random, from a fixed seed so that two runs draw the same.

| Record | `Title` | `KPG.Kind` | `KPG.Data` | Check under | Departure from 4.2 |
| --- | --- | --- | --- | --- | --- |
| authority a0, a1 | `KPG authority a0` | `authority` | `{"v":"1","fp":F,"certificate":C,"key":K,"host":H,"gateKey":G,"storeKey":S}` with C, K, H and G invented text of 1,400, 1,150, 260 and 44 characters, F the SHA-256 of C, S 32 random bytes in hex | its own store key S | F is written beside the certificate, so that a reader needs no certificate parser |
| retirement of a0 | `KPG retired a0` | `retired` | `{"v":"1","authority":<a0's F>,"act":<16 random bytes>}` | a0's store key | none |
| admission records of c1, c2, c3 | `KPG client c1` | `client` | `{"v":"1","certificate":<SHA-256 of an invented certificate>,"subject":"c1","authority":<a1's F>,"profiles":["reader","legacy"],"notAfter":"2027-01-01T00:00:00Z"}`; c2 holds `writer`, c3 `auditor` | a1's store key | a JSON stand-in for today's record text |
| revocation of c3 | `KPG revoked c3` | `revoked` | `{"v":"1","certificate":<c3's digest>,"authority":<a1's F>,"notAfter":"2027-01-01T00:00:00Z"}` | a1's store key | 4.2 gives it no check |
| additions of `reader`, `writer`, `auditor`, `legacy` | `KPG profile added reader` | `profile-added` | `{"v":"1","name":"reader","act":<16 random bytes>,"seen":[]}` | a1's store key | none |
| removal of `legacy` | `KPG profile removed legacy` | `profile-removed` | `{"v":"1","name":"legacy","act":<16 random bytes>}` | a1's store key | 4.2 gives it no check |
| recycle bin marker | `KPG recycle bin` | `recycle-bin` | `{"v":"1","group":<the recycle bin group's identifier>,"act":<16 random bytes>}` | a1's store key | 4.2 names the record and gives it no shape |

**The witnesses.** The retirement and the revocation each leave a deletion record that names no
entry. Its identifier is the first 16 bytes of HMAC-SHA256 under a store key over
`KPG-witness/1/<kind>/<target>`: a0's key and a0's F for the retirement, a1's key and c3's
certificate digest for the revocation. Its deletion time is the refusal's own time.

**Every record carries a check here**, the revocation and the removal included, so that all five
rules of section 2 read the same files. R1 and R2 never look at the check of a refusal.

### 1.3 The database, and what is decided

Both copies start as the same file, made with times at 2026-01-01T00:00:00Z (T0) and after, in
whole seconds:

- the root group, a group `General` holding a user entry, and the group `KeePassGate`, with
  searching and auto-type off and the icon `TerminalEncrypted`, holding the store;
- in that group: authority a0 (T0+60), authority a1 (T0+120), the retirement of a0 (T0+120), the
  admission records of c3 (T0+180) and c1 (T0+240), the revocation of c3 (T0+360), the additions of
  `reader`, `writer`, `auditor` and `legacy` (T0+420 to T0+600), the removal of `legacy` (T0+660)
  and the recycle bin marker (T0+720);
- the database's recycle bin, a group `Recycle Bin` with searching and auto-type off, holding a
  user entry deleted into it (T0+840) and **a store entry, c2's admission record** (made at T0+300,
  moved there at T0+780), which counts there by rule 4 of 4.1;
- the two witnesses.

**What is decided**: a1 serves and a0 is retired; c1 and c2 are admitted and c3 is revoked;
`reader`, `writer` and `auditor` are on the list and `legacy` is removed; the marker hides the
recycle bin. All five rules read exactly that from the unedited copy (`recorded\checks.txt`).

**How the task's bin record was read.** The task names "a marker that an entry sitting in the
recycle bin still counts" and "a store entry in the recycle bin with its marker". The probe builds
the record 4.2 gives, an entry naming a group that is or has been the recycle bin, and puts a store
entry, c2's admission record, in that bin. That the bin's store entry counts is rule 4 of 4.1 and is
part of what is decided (c2 admitted); the marker's own refusal is the group it hides.

**The marker's verdict is the marker's own.** The group the marker names is the database's recycle
bin in all 4,120 files of the run (`recorded\checks.txt`), and today's rule already hides the
current bin. A cell where the marker is lost is counted wider by what the marker itself refuses,
and not by what a client of that file could reach: the marker carries the case where a merge makes
another group the bin, and no run here did that.

## 2. The reading rules, as implemented

`rules.py` holds them and nothing else. `scripts\verdicts.py` reads every cell under every rule that
`RULES` in `rules.py` names, so a further rule is written there and read over `recorded\` without a
database.

**Common to all five**, which is the read of 4.4 over current versions wherever they sit:

- A version is read as a record from `KPG.Kind` and the JSON in `KPG.Data`. A version whose kind is
  none of the seven, whose `KPG.Data` is missing or is no JSON object, or which lacks a field its
  kind needs, is no record.
- An authority counts when its own check verifies under the store key in its own document; copies
  with the same F count once.
- Exactly a single authority that no retirement in force names serves. Two are `503 undecided`, and
  nothing is served; with none, nothing is served.
- What grants, an admission record or a profile addition, counts in its current version alone and
  only when its check verifies under the serving authority's store key. A client is admitted by such
  a record that names the serving authority, when no revocation in force names its certificate.
- A profile name is on the list when an addition of that name counts and lists in `seen` the `act`
  of every removal of that name in force.
- A group is hidden when a marker in force names its identifier.

**What each rule adds**:

| Rule | Where a refusal is looked for | When a refusal counts | What else |
| --- | --- | --- | --- |
| R1 | current versions | whenever a version of its kind names the target; no check | |
| R2 | current versions and history items | as R1 | |
| R3 | current versions | only when its check verifies: a retirement under the store key of the authority it names, every other refusal under the serving authority's | a refusal whose check fails is ignored |
| R4 | current versions | as R3 | a refusal whose check fails, or a store entry that reads as no record, makes the whole store refuse; a store entry is an entry in a group named `KeePassGate`, or an entry carrying a string whose name begins `KPG.` |
| R5 | current versions | as R3 | a revocation or a retirement also counts when its witness is among the deletion records |

**The verdict**, against what is decided. **Wider**: anything decided as refused is served (a0
serves, c3 is admitted, `legacy` is on the list, the bin is not hidden by a marker). **Refusing**:
nothing is wider and anything decided as served is refused (the whole store refuses, `503
undecided`, c1 refused, a name off the list), with what it refuses. **Unchanged** otherwise. A cell
in which nothing is served is never wider.

## 3. The edits, the conditions and the merges

**The edits**, each made in copy B at T0+7200 by KeePassLib standing for a person: an edit in the
entry dialog puts the version before it into history (`CreateBackup`) and then moves the
modification time; a move sets the location time and the previous parent and leaves the
modification time and the history alone, as `crdt` and `grant-only` stand for KeePass's move and its
Delete into the recycle bin.

| Edit | What the probe does |
| --- | --- |
| one character of the payload | the dialog edit: the last character of the target's value in `KPG.Data` (the authority's F, the certificate digest, the name `legacy`, the bin's identifier) becomes the next hex digit or letter |
| payload string renamed | the dialog edit: `KPG.Data` becomes `KPG.Datum`, value and protection kept |
| payload string deleted | the dialog edit: `KPG.Data` removed |
| every string emptied | the dialog edit: every string, standard and custom, set to an empty value, names and protection kept |
| title only | the dialog edit: a space and `(seen)` appended to the title |
| moved to another group | the move, into `General` |
| moved to the recycle bin | the move, into the recycle bin |
| deleted with a deletion record | removed, and a deletion record of its identifier added at T0+7200 |
| deleted without a record | removed, nothing added |
| duplicated, original edited | a copy under a new identifier with every string and the history, a space and `- Copy` appended to its title and its times at T0+7200, in the same group; then the original takes the edit of a single character |

**The records edited**: the retirement of a0, the revocation of c3, the removal of `legacy`, the
recycle bin marker, and as the control c1's admission record. **The time conditions**: A untouched;
A's own dialog edit of the same entry's title (a space and `(A)` appended) at T0+9000, so that B's
edit is the older; at T0+5400, so that B's edit is the newer; and at T0+7200, the same second. **No
edit** in B is the other control, with A untouched and with A's title edit alone. That makes 206
scenarios.

**The merges**: B merged into A and A merged into B,

- with KeePassLib 2.62's `PwDatabase.MergeIn` under Synchronize (Sync), Overwrite if newer (OIN),
  Overwrite existing (OE), Keep existing (KE) and Create new IDs (CNI), the result saved and opened
  again, over copies under the fast key derivation of `store-entries` (Argon2d, 2 iterations,
  1 MiB);
- with `keepassxc-cli merge -s` of 2.4.2, 2.6.6, 2.7.12 and 2.8.0 (xc), each program's signature
  from DroidMonkey Apps, LLC valid (`recorded\tools.txt`). **These copies use the slow key
  derivation of `ground-keepassxc`** (Argon2d, 200 iterations, 64 MiB, 2 lanes), **so that the two
  loads of a merge fall in different seconds**: a single load of a slow copy took 3.59, 3.59, 3.95
  and 3.34 seconds in the four releases, and a merge 5.50 to 26.06 seconds, on a machine other work
  was sharing (3.73, 3.60, 4.27 and 3.12 seconds, and 9.08 to 98.15, in the second run). All 1,648
  `keepassxc-cli` merges exited 0, and 568 of them left the target as it was
  (`recorded\xc-times.tsv`).

**The cells**: 206 scenarios by 18 merges are 3,708 merge cells; the two copies read as they are
add 412, for 4,120 files, 926 of them distinct (`recorded\checks.txt`). Each is dumped whole: every
entry's current version and history items with every string beyond the standard five, its group,
its times, and every deletion record.

## 4. What each result holds

### 4.1 The holdings

**The holdings are the same for every record.** For each of the five records, `recorded\contents.md`
gives what each result holds of it; the five tables are the same cell for cell, and the four
`keepassxc-cli` releases agree in every cell of them (`recorded\checks.txt`). The merges therefore
treat the five records alike, and only the reading differs, which is section 4.2. The table below is
the revocation's, and it is the table of each of the other four.

How to read a cell: the entry's current version, then its history items in brackets. `O` is the
`KPG.` strings as written, `E` the strings B's edit gave them; a following `a` is A's title, `b`
B's title, `c` the duplicate's title. `@g` is the group `General`, `@r` the recycle bin, and `@2`,
`@2g` and `@2r` the second `KeePassGate`, `General` and `Recycle Bin` that Create new IDs makes. `-`
is no current version, and `!` a deletion record naming the entry. After the original come `c:`, the
duplicate B made, and `n:`, each copy under an identifier Create new IDs drew. The witness is in
every file and is not printed: 4,120 of 4,120 for each of the two (`recorded\checks.txt`). The
column `xc into A` holds the four releases, which agree.

| edit time | B | A | Sync into A | OIN into A | OE into A | KE into A | CNI into A | xc into A | Sync into B | OIN into B | OE into B | KE into B | CNI into B | xc into B |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| none title-only | O | Oa[O] | Oa[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2 | Oa[O] | Oa[O] | Oa[O] | Oa[O] | O[O] | O n:Oa@2[O] | Oa[O] |
| character untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| character older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| character newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| character same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| rename untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| rename older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| rename newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| rename same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| delete-string untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| delete-string older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| delete-string newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| delete-string same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| empty untouched | E[O] | O | E[O] | E[O] | E[O] | O[O] | O n:E@2[O] | E[O] | E[O] | E[O] | O[O,E] | E[O] | E[O] n:O@2 | E[O] |
| empty older | E[O] | Oa[O] | Oa[O,E] | Oa[O,E] | E[O,Oa] | Oa[O,E] | Oa[O] n:E@2[O] | Oa[O,E] | Oa[O,E] | Oa[O,E] | Oa[O,E] | E[O,E] | E[O] n:Oa@2[O] | Oa[O,E] |
| empty newer | E[O] | Oa[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,Oa] | Oa[O] n:E@2[O] | E[O,Oa] | E[O,Oa] | E[O,Oa] | Oa[O,E] | E[O,Oa] | E[O] n:Oa@2[O] | E[O,Oa] |
| empty same | E[O] | Oa[O] | E[O] | E[O] | E[O,Oa] | Oa[O] | Oa[O] n:E@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,E] | E[O] | E[O] n:Oa@2[O] | E[O] |
| title untouched | Ob[O] | O | Ob[O] | Ob[O] | Ob[O] | O[O] | O n:Ob@2[O] | Ob[O] | Ob[O] | Ob[O] | O[O,Ob] | Ob[O] | Ob[O] n:O@2 | Ob[O] |
| title older | Ob[O] | Oa[O] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Oa] | Oa[O,Ob] | Oa[O] n:Ob@2[O] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Oa[O,Ob] | Ob[O,Ob] | Ob[O] n:Oa@2[O] | Oa[O,Ob] |
| title newer | Ob[O] | Oa[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Oa] | Oa[O] n:Ob@2[O] | Ob[O,Oa] | Ob[O,Oa] | Ob[O,Oa] | Oa[O,Ob] | Ob[O,Oa] | Ob[O] n:Oa@2[O] | Ob[O,Oa] |
| title same | Ob[O] | Oa[O] | Ob[O] | Ob[O] | Ob[O,Oa] | Oa[O] | Oa[O] n:Ob@2[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O,Ob] | Ob[O] | Ob[O] n:Oa@2[O] | Ob[O] |
| move untouched | O@g | O | O@g | O | O | O | O n:O@2g | O@g | O@g | O@g | O@g | O@g | O@g n:O@2 | O@g |
| move older | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move newer | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| move same | O@g | Oa[O] | Oa@g[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2g | Oa@g[O] | Oa@g[O] | Oa@g[O] | Oa@g[O] | O@g[O] | O@g n:Oa@2[O] | Oa@g[O] |
| recycle untouched | O@r | O | O@r | O | O | O | O n:O@2r | O@r | O@r | O@r | O@r | O@r | O@r n:O@2 | O@r |
| recycle older | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle newer | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| recycle same | O@r | Oa[O] | Oa@r[O] | Oa[O] | O[O,Oa] | Oa[O] | Oa[O] n:O@2r | Oa@r[O] | Oa@r[O] | Oa@r[O] | Oa@r[O] | O@r[O] | O@r n:Oa@2[O] | Oa@r[O] |
| delete-record untouched | -! | O | -! | O | O | O | O | O | -! | -! | -! | -! | -! n:O@2 | O! |
| delete-record older | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-record newer | -! | Oa[O] | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! | -! | -! | -! | -! n:Oa@2[O] | Oa[O]! |
| delete-record same | -! | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | -! n:Oa@2[O] | Oa[O]! |
| delete-no-record untouched | - | O | O | O | O | O | O | O | O | O | O | O | - n:O@2 | O |
| delete-no-record older | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record newer | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| delete-no-record same | - | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | Oa[O] | - n:Oa@2[O] | Oa[O] |
| duplicate untouched | E[O] c:Oc | O | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O] c:Oc | O n:E@2[O] n:Oc@2 | E[O] c:Oc | E[O] c:Oc | E[O] c:Oc | O[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:O@2 | E[O] c:Oc |
| duplicate older | E[O] c:Oc | Oa[O] | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | Oa[O,E] c:Oc | E[O,E] c:Oc | E[O] c:Oc n:Oa@2[O] | Oa[O,E] c:Oc |
| duplicate newer | E[O] c:Oc | Oa[O] | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,Oa] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | E[O,Oa] c:Oc | E[O,Oa] c:Oc | E[O,Oa] c:Oc | Oa[O,E] c:Oc | E[O,Oa] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O,Oa] c:Oc |
| duplicate same | E[O] c:Oc | Oa[O] | E[O] c:Oc | E[O] c:Oc | E[O,Oa] c:Oc | Oa[O] c:Oc | Oa[O] n:E@2[O] n:Oc@2 | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O] c:Oc | Oa[O,E] c:Oc | E[O] c:Oc | E[O] c:Oc n:Oa@2[O] | E[O] c:Oc |

Where the holdings fall, for each edit of the payload (a character, renamed, deleted, emptied): B's
edited content ends current, with the written content in history, in 14 of the 18 merges with A
untouched (every merge into A but KE and CNI, every merge into B but OE and CNI), 14 with A's title
edit before B's edit, 2 with A's title edit after it (OE into A, KE into B) and 8 in the same second
(Sync, OIN and OE into A, KE into B, the four releases into B): 38 of 72. Create new IDs leaves the
written and the edited content side by side, under two identifiers, in the other 8 of 72 that are
not the written content alone. A deletion with its record leaves the entry absent beside its record
in 10 of 72: Sync into A, and Sync, OIN, OE and KE into B, each with A untouched or with A's title
edit before the deletion. In the other cells of that deletion the written content is present:
KeePass drops a record whose entry is newer, Create new IDs brings the entry into B as a copy under
a new identifier, and `keepassxc-cli` applies no record, bringing the entry back beside it (`O!`).
A deletion without a record leaves the written content present after every merge.

### 4.2 What each holding means, record by record

From `recorded\meanings.md`, which gives every distinct holding of every edit with its number of
cells and its verdict under each rule. Every holding where the written content is current, wherever
it sits and whatever its history, or brought back beside its own deletion record, is unchanged under
all five rules for all five records; the rows below are the holdings that are not. "Payload edit" is
each of the four edits of the payload, which give the same holdings in the same number of cells.

| Holding | Cells of 72 | R1 | R2 | R3 | R4 | R5 |
| --- | --- | --- | --- | --- | --- | --- |
| **retirement of a0** | | | | | | |
| B's edited content current, the written content in history | 38, payload edit | refusing: undecided | unchanged | refusing: undecided | refusing: the store refuses | unchanged |
| written and edited content side by side (CNI) | 8, payload edit | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| the edited original beside the duplicate, which holds the written content | 46, duplicate | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| absent, with its deletion record | 10, deleted with a record | refusing: undecided | refusing: undecided | refusing: undecided | refusing: undecided | unchanged |
| **revocation of c3** | | | | | | |
| B's edited content current, the written content in history | 38, payload edit | wider: c3 admitted | unchanged | wider: c3 admitted | refusing: the store refuses | unchanged |
| written and edited content side by side (CNI) | 8, payload edit | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| the edited original beside the duplicate | 46, duplicate | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| absent, with its deletion record | 10, deleted with a record | wider: c3 admitted | wider: c3 admitted | wider: c3 admitted | wider: c3 admitted | unchanged |
| **removal of `legacy`** | | | | | | |
| B's edited content current, the written content in history | 38, payload edit | wider: `legacy` on the list | unchanged | wider: `legacy` on the list | refusing: the store refuses | wider: `legacy` on the list |
| written and edited content side by side (CNI) | 8, payload edit | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| the edited original beside the duplicate | 46, duplicate | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| absent, with its deletion record | 10, deleted with a record | wider: `legacy` on the list | wider: `legacy` on the list | wider: `legacy` on the list | wider: `legacy` on the list | wider: `legacy` on the list |
| **recycle bin marker** | | | | | | |
| B's edited content current, the written content in history | 38, payload edit | wider: bin not hidden | unchanged | wider: bin not hidden | refusing: the store refuses | wider: bin not hidden |
| written and edited content side by side (CNI) | 8, payload edit | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| the edited original beside the duplicate | 46, duplicate | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| absent, with its deletion record | 10, deleted with a record | wider: bin not hidden | wider: bin not hidden | wider: bin not hidden | wider: bin not hidden | wider: bin not hidden |
| **c1's admission record (control)** | | | | | | |
| B's edited content current, a character changed | 38 | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused |
| B's edited content current, renamed, deleted or emptied | 38 each | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: the store refuses | refusing: c1 refused |
| written and edited content side by side (CNI), renamed, deleted or emptied | 8 each | unchanged | unchanged | unchanged | refusing: the store refuses | unchanged |
| absent, with its deletion record | 10, deleted with a record | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused | refusing: c1 refused |

For the control, a character changed beside a copy from Create new IDs and the duplicate are
unchanged under all five rules: an admission record whose check fails is ignored, and the copy
admits c1.

## 5. Verdicts by reading rule

Each cell of a table holds wider / unchanged / refusing among the 72 merge cells of that edit of
that record (18 merges by four time conditions), and the `no edit` row the 18 cells with A's title
edit alone. From `recorded\counts.md`.

### 5.1 R1

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string renamed | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string deleted | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| every string emptied | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

### 5.2 R2

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| payload string renamed | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| payload string deleted | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| every string emptied | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

### 5.3 R3

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string renamed | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string deleted | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| every string emptied | 0/34/38 | 38/34/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

R1 and R3 give the same verdict in all 4,120 cells (`recorded\verdicts.tsv`). That follows from the
edits run, each of which changes the target or the payload's readability as well as the check; an
edit elsewhere in the payload was not run (section 8).

### 5.4 R4

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/34/38 |
| payload string renamed | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 |
| payload string deleted | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 |
| every string emptied | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/62/10 | 10/62/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/26/46 | 0/26/46 | 0/26/46 | 0/26/46 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

### 5.5 R5

| edit | retirement of a0 | revocation of c3 | removal of legacy | recycle bin marker | admission record of c1 (control) |
| --- | --- | --- | --- | --- | --- |
| one character of the payload | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string renamed | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| payload string deleted | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| every string emptied | 0/72/0 | 0/72/0 | 38/34/0 | 38/34/0 | 0/34/38 |
| title only | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to another group | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| moved to the recycle bin | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| deleted with a deletion record | 0/72/0 | 0/72/0 | 10/62/0 | 10/62/0 | 0/62/10 |
| deleted without a record | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| duplicated, original edited | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 | 0/72/0 |
| no edit | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 | 0/18/0 |

### 5.6 All rules

Merge cells, wider / unchanged / refusing: the four records that refuse (2,880), the control (720),
and the scenarios with no edit in B (108).

| rule | refusal cells (2,880) | control cells (720) | no-edit cells (108) |
| --- | --- | --- | --- |
| R1 | 486/2232/162 | 0/558/162 | 0/108/0 |
| R2 | 30/2840/10 | 0/558/162 | 0/108/0 |
| R3 | 486/2232/162 | 0/558/162 | 0/108/0 |
| R4 | 30/1920/930 | 0/534/186 | 0/108/0 |
| R5 | 324/2556/0 | 0/558/162 | 0/108/0 |

No control cell is wider under any rule, and every no-edit cell is unchanged under every rule
(`recorded\checks.txt`).

### 5.7 The edited copy itself

Copy B read as it is, before any merge, which is the first step of every sequence
(`recorded\counts.md`, the last table under each rule). Title edits and moves are unchanged under
every rule.

| Rule | Retirement of a0 | Revocation of c3 | Removal of `legacy` | Recycle bin marker | c1's admission record |
| --- | --- | --- | --- | --- | --- |
| R1, R3 | refusing (undecided) after a payload edit or a deletion | wider (c3 admitted) after a payload edit or a deletion | wider (`legacy` on the list) after a payload edit or a deletion | wider (bin not hidden) after a payload edit or a deletion | refusing (c1 refused) after a payload edit or a deletion |
| R2 | refusing (undecided) after a deletion | wider after a deletion | wider after a deletion | wider after a deletion | refusing after a payload edit or a deletion |
| R4 | refusing after a payload edit, the duplicate or a deletion | refusing (the store) after a payload edit or the duplicate; wider after a deletion | as the revocation | as the revocation | refusing after a payload edit or a deletion |
| R5 | unchanged after every edit | unchanged after every edit | wider after a payload edit or a deletion | wider after a payload edit or a deletion | refusing after a payload edit or a deletion |

## 6. The shortest sequences that end wider

A step is B's edit, A's own title edit, or a merge; `recorded\shortest.md` lists every sequence
that ends wider by its number of steps. **Under every rule the shortest is a single step**: the
copy where the edit was made, read as it is, is wider at once (section 5.7). The sequences below
are the shortest that end wider in copy A, which nobody edited: two steps under every rule.

**R1 and R3**, for the revocation, the removal and the marker alike:

1. In copy B, a person changes a character of the revocation of c3 in the entry dialog: the last
   character of the certificate digest in `KPG.Data`. The version before goes into history, and the
   modification time moves to T0+7200. Renaming or deleting `KPG.Data`, or emptying every string,
   does the same.
2. Copy A, untouched, merges B in with KeePass's Synchronize, Overwrite if newer or Overwrite
   existing, or with `keepassxc-cli merge` of any of the four releases.
3. In A the entry's current version is B's, naming a certificate nobody holds. No revocation of c3
   is found, and c3's admission record admits c3.

The retirement takes no such sequence under any rule: an edited or deleted retirement leaves a0 and
a1 both unretired, which is `503 undecided`, a refusal.

**R2 and R4**, for the revocation, the removal and the marker alike, and only through a deletion:

1. In copy B, the revocation of c3 is deleted for good, with a deletion record (T0+7200).
2. Copy A, untouched, merges B in with Synchronize.
3. The record removes A's entry and its history with it. No version of the revocation is left; no
   check fails and no store entry is unreadable, so R4 does not refuse either, and c3 is admitted.

With A's own title edit before the deletion the same holds in three steps; with A's title edit after
it, Synchronize drops the record and keeps the entry. Into B, the same deletion stays wider after
Sync, OIN, OE and KE of A into B, since B's own record deletes A's entry on arrival.

**R5**, for the removal and the marker only:

1. In copy B, a character of the removal of `legacy` changes (`legacy` becomes `legacz`), or
   `KPG.Data` is renamed or deleted, or every string emptied.
2. Copy A, untouched, merges B in with Synchronize, Overwrite if newer, Overwrite existing, or
   `keepassxc-cli`.
3. The removal's current version fails its check and is ignored; a removal has no witness, and
   `legacy` is back on the list. A deletion with its record followed by Synchronize does the same.

Under R5 no cell is wider for the revocation or the retirement: their witnesses are in every file.

## 7. Which rules have no cell that ends wider, and what each costs

**None of the five.** Every rule has merge cells that end wider:

| Rule | Wider, of 2,880 refusal cells | Which | Refusing, of 2,880 refusal cells | Refusing, of 720 control cells |
| --- | --- | --- | --- | --- |
| R1 | 486 | revocation, removal, marker: every payload edit (38 of 72 each) and a deletion with its record (10 of 72) | 162: the retirement, `503 undecided` | 162 |
| R2 | 30 | revocation, removal, marker: a deletion with its record (10 of 72 each) | 10: the retirement, `503 undecided` | 162 |
| R3 | 486 | as R1 | 162, as R1 | 162 |
| R4 | 30 | as R2 | 930: the whole store refuses wherever an edited refusal, or an edited copy of a refusal, is current (46 of 72 for each payload edit and for the duplicate, for each of the four records), and the retirement's 10 | 186 |
| R5 | 324 | removal and marker: every payload edit (38 of 72 each) and a deletion with its record (10 of 72) | 0 | 162 |

What the counts hold beyond that, with no recommendation:

- **For the records that leave a witness**, the revocation and the retirement, **R5 has no cell
  that ends wider** and none that refuses more, among the 1,440 merge cells of their edits and in
  the edited copy alone (sections 4.2, 5.5 and 5.7).
- **For the edits that leave the entry in the file**, every edit but the two deletions, **R2 and R4
  have no cell that ends wider.** R2 refuses no more than was decided in any of those refusal cells;
  R4 refuses the whole store in 920 of them, and in 24 more control cells than the other rules.
- **Every wider cell of R2 and R4** is a deletion with its record that a KeePass merge carried:
  Synchronize into the copy that was not edited, or any of Sync, OIN, OE and KE into the copy where
  the deletion was made. None is a `keepassxc-cli` cell, since `keepassxc-cli` applies no deletion
  record.
- **R1 and R3 widen through KeePass's own Synchronize, Overwrite if newer and Overwrite existing,
  and through all four `keepassxc-cli` releases, with no deletion**: the adversarial reading's
  sequence, as run.
- **The control costs the same under every rule but R4**: 162 cells in which c1 is refused,
  wherever an edited admission record is current or a deleted record is absent. R4 adds 24 in
  which an unreadable copy from Create new IDs refuses the whole store.

## 8. What was not run

- **A KeePass or KeePassXC window.** Every edit is KeePassLib standing for the entry dialog; a move
  stands for KeePass's move and Delete as `crdt` and `grant-only` stand for them; every KeePass
  merge is `PwDatabase.MergeIn`.
- **A phone program**, kdbxweb and pykeepass. "Deleted without a record" stands for a program that
  writes none.
- **An edit that leaves the target intact**, such as a character of `act` or `notAfter`, or of
  `KPG.Kind`: the edit run changes the last character of the target, so R1 and R3 come out alike.
- **More than a single edit of the same entry**, and so a history trimmed past the written version,
  which is the version R2 rests on.
- **An edit of an authority, of a profile addition, of a rule version, of a renewal answer or of a
  reference**; the last three are not built.
- **A merge that makes another group the recycle bin**, which is the case the marker exists for.
- **Database Maintenance**, Delete Duplicate Entries and Import; three copies; a chain of merges;
  the fast key derivation for `keepassxc-cli`.

**Reasoning, not run**: under R2 the written version survives an edit only as a history item, so
anything that drops history after the merge, Database Maintenance by age or a history limit reached
by later edits, would leave the edited version alone, which R2 then reads as R1 does. Section 9 runs
that.

## 9. Extension: R6, and the limits of history

Added the same day. Sections 1 to 8 stand as written; this section has its own command,
`run-history.ps1`, and its own record, `recorded\history\`, both in the same family, and `RUN.md`
holds its figures beside a second run's.

### 9.1 R6, as implemented

R2 and R4 joined, in `rules.py` beside the other five: **every record carries the check; a record
counts in its current version when that version passes, or else in its newest history item that
passes; a store entry with no passing version anywhere makes the whole store refuse.** "Passes" is
R3's check: an authority under its own store key, a retirement under the key of the authority it
names, every other record under the serving authority's. It holds for what grants as for what
refuses, and counts no witness. A store entry is R4's, judged by its current version. Every table
below reads all six rules over the same dumps.

### 9.2 The five parts

Each part runs on all five records, with A untouched unless the part names A, and every result is
merged B into A and A into B with KeePassLib's five methods and the four `keepassxc-cli` releases:

1. **History limits.** B's dialog edit of a character of the payload, then N-1 dialog edits of the
   title, N in 1, 2, 3, 10, 11 and 12, under the database's limits at KeePass's defaults (10 items,
   6 MiB), a single item, no item, none at all (-1) and a single byte.
2. **Database Maintenance.** KeePassLib standing for "Delete history entries older than (days)",
   which the desktop measurement saw remove every history item of that age or older with no
   question: in B after the edit at 30 days, which takes the written version (319 days old at the
   run), and at 365, which keeps it; or in the merge's result at 30 days.
3. **An edit that leaves no history.** The four payload edits by KeePassLib with the backup step
   left out, the modification time moved; and a character changed or `KPG.Data` deleted by pykeepass
   4.2.0 through `set_custom_property` and `delete_custom_property`, which left no history item and
   **kept the entry's modification time** (`recorded\history\pykeepass.txt`, the dumps).
4. **An edit in both copies.** B's dialog edit of the target's last character, and A's edit of its
   first character with no history item, 1800 seconds after B's, 1800 before, and in the same
   second.
5. **A history item tampered with.** After B's dialog edit, a script changes the first character of
   the target in the history item, or removes the item, leaving the entry's times as they are.

**The derivation.** Every copy here, `keepassxc-cli`'s included, uses the fast derivation: what the
two loads' seconds decide is the database's custom data, where this store keeps nothing, so no cell
can depend on them and the slow derivation was run for none. 220 scenarios give 3,960 merge cells
and 440 copies read as they are, 711 distinct files; the 1,760 `keepassxc-cli` merges all exited 0.
Every rule reads the decided state from the unedited copy, the five records hold the same in every
cell, and the four releases agree in every cell (`recorded\history\checks.txt`).

### 9.3 R6 beside the other rules

Wider / unchanged / refusing (`recorded\history\counts.md`, and for part 0
`recorded\history\part0\counts.md`). Refusal cells are the merge cells of the four records that
refuse; control cells those of c1's admission record; "B alone" the edited copy read as it is,
over the four records that refuse.

| Part | Rule | Refusal cells | Control cells | B alone |
| --- | --- | --- | --- | --- |
| 0, sections 3 to 7 (2,880, 720) | R1 | 486/2232/162 | 0/558/162 | |
| | R2 | 30/2840/10 | 0/558/162 | |
| | R3 | 486/2232/162 | 0/558/162 | |
| | R4 | 30/1920/930 | 0/534/186 | |
| | R5 | 324/2556/0 | 0/558/162 | |
| | **R6** | **30/2840/10** | **0/710/10** | |
| 1, history limits (2,160, 540, 120) | R1 | 1260/480/420 | 0/120/420 | 90/0/30 |
| | R2 | 798/1096/266 | 0/120/420 | 57/44/19 |
| | R3 | 1260/480/420 | 0/120/420 | 90/0/30 |
| | R4 | 0/240/1920 | 0/120/420 | 0/0/120 |
| | R5 | 840/1320/0 | 0/120/420 | 60/60/0 |
| | **R6** | **0/944/1216** | **0/236/304** | **0/44/76** |
| 2, Database Maintenance (216, 54, 12) | R1 | 126/48/42 | 0/12/42 | 9/0/3 |
| | R2 | 42/160/14 | 0/12/42 | 3/8/1 |
| | R3 | 126/48/42 | 0/12/42 | 9/0/3 |
| | R4 | 0/24/192 | 0/12/42 | 0/0/12 |
| | R5 | 84/132/0 | 0/12/42 | 6/6/0 |
| | **R6** | **0/144/72** | **0/36/18** | **0/8/4** |
| 3, no history (432, 108, 24) | R1 | 216/144/72 | 0/36/72 | 18/0/6 |
| | R2 | 42/376/14 | 0/36/72 | 18/0/6 |
| | R3 | 216/144/72 | 0/36/72 | 18/0/6 |
| | R4 | 0/96/336 | 0/28/80 | 0/0/24 |
| | R5 | 144/288/0 | 0/36/72 | 12/12/0 |
| | **R6** | **0/328/104** | **0/82/26** | **0/0/24** |
| 4, both copies (216, 54, 12) | R1 | 162/0/54 | 0/0/54 | 9/0/3 |
| | R2 | 0/216/0 | 0/0/54 | 0/12/0 |
| | R3 | 162/0/54 | 0/0/54 | 9/0/3 |
| | R4 | 0/0/216 | 0/0/54 | 0/0/12 |
| | R5 | 108/108/0 | 0/0/54 | 6/6/0 |
| | **R6** | **0/192/24** | **0/48/6** | **0/12/0** |
| 5, history tampered (144, 36, 8) | R1 | 84/32/28 | 0/8/28 | 6/0/2 |
| | R2 | 33/100/11 | 0/8/28 | 6/0/2 |
| | R3 | 84/32/28 | 0/8/28 | 6/0/2 |
| | R4 | 0/16/128 | 0/8/28 | 0/0/8 |
| | R5 | 56/88/0 | 0/8/28 | 4/4/0 |
| | **R6** | **0/84/60** | **0/21/15** | **0/0/8** |

**Under R6 no cell of the five parts ends wider.** Every R6 cell that is not unchanged is refusing,
and refusing as the whole store refusing or as `503 undecided` (the retirement), the counts of each
part's breakdown in `counts.md`. R2, the rule R6 extends, widens in every part but part 4: once
the written version is out of the history, R2 reads the edited version as R1 does.

### 9.4 Where the written version leaves the history

Copy B alone, after N edits of the same entry (`recorded\history\limits.md`):

| Limit | The written version is in the history after | It is gone from the history at | R6 on B from then on |
| --- | --- | --- | --- |
| default, 10 items and 6 MiB | edits 1 to 10 | the 11th edit | the store refuses |
| a single item | the first edit | the 2nd edit | the store refuses |
| no item (0) | never | the first edit | the store refuses |
| a single byte | never | the first edit | the store refuses |
| none (-1) | edits 1 to 12, every edit run | never | unchanged |

Database Maintenance at 30 days took the written version out of B's history, and at 365 kept it.

### 9.5 What a merge brings back

Where copy B kept no written version (`recorded\history\holdings.md`), the other, untouched copy
brings it back: as a history item under Synchronize, Overwrite if newer and Overwrite existing into
A, Synchronize, Overwrite if newer and Keep existing into B, and every `keepassxc-cli` release in
both directions; as the current version under Keep existing into A and Overwrite existing into B;
and as a copy under a new identifier under Create new IDs. Three cases lose it in both copies:

- **The history limit applies to the merged entry.** After 11 edits under the default limit, or 2
  under a single item, the other copy's written version arrives as the oldest item and goes again:
  KeePassLib trims the merged history to the open copy's limit in every merge that does not keep
  the version current, and `keepassxc-cli` keeps no losing version behind ten later items or under
  the copy's limit.
- **pykeepass's edit keeps the modification time**, so the copies meet at the same second:
  Synchronize and Overwrite if newer take the arriving copy's version, and every `keepassxc-cli`
  release keeps the target's, each keeping the other nowhere.
- **A tampered history item keeps the written version's time**, and a merge keeps a single item per
  time: KeePassLib keeps the open copy's, so the written version survives into A and the tampered
  item into B; `keepassxc-cli` kept the tampered item in both directions.

Of the 504 merges of the revocation scenarios whose copy B held no written version, the written
version came out current in 96, as a history item in 89, as a copy under a new identifier in 28,
and nowhere in 291 (`recorded\history\checks.txt`).

### 9.6 The shortest sequence that ends wider under R6

- **Parts 1 to 5: none.** No cell ends wider under R6 (`recorded\history\shortest.md`).
- **Part 0**: two steps, as under R2 and R4: in copy B the revocation, the removal or the marker is
  deleted for good with its deletion record, and copy A merges B in with Synchronize; the record
  removes A's entry and its history with it (`recorded\history\part0\shortest.md`). In the edited
  copy itself a single step, the deletion with or without a record, is wider.

### 9.7 What R6 depends on

Stated from the counts, with no recommendation.

- **R6 widens only where the entry is gone** from the copy the gate reads: a deletion, carried into
  the other copy by a deletion record that Synchronize applies. An edit of the entry, however made,
  and the loss of its history make R6 refuse, never widen: in every part, a store entry with no
  passing version stops the whole store.
- **What keeps the written version**: an edit in KeePass's entry dialog, which puts the version
  before it into history (and KeePassXC's `edit`, per section 2.2 of `RESULTS.md`, not run here); a
  history limit not yet reached, or none; a merge that keeps the older version as a history item.
- **What removes it**, each measured here: a history limit reached by later edits (at the 11th edit
  under the default, the 2nd under a single item, at once under no item or a single byte); KeePass's
  Database Maintenance when the written version is older than its number of days (by default 365
  in the window); an edit by a program that writes no history, pykeepass 4.2.0 and KeePassLib
  without its backup step; a merge that keeps the other version nowhere, at the same second or by
  `keepassxc-cli` taking the newer entry whole; Create new IDs when the copy it makes carries no
  written version; and a script that edits or removes the history item.
- **What each removal costs under R6**: the whole store refuses until the record is written again,
  in 1,216 of part 1's 2,160 refusal cells, 72 of part 2's 216, 104 of part 3's 432, 24 of part 4's
  216 and 60 of part 5's 144. The control's edited admission record falls back to its history the
  same way: in part 0, R6 admits c1 in 710 of 720 cells, where R1 to R5 refuse it in 162 or 186.
