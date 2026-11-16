# Critique of `store-entries`: survival across programs

Written 2026-11-15 against `option-store-entries.md` as it stands, KeePassGate master `a42e38aa`.
The lens is survival: every save and merge in the ground reports, walked through the design one
datum at a time, looking for a sequence that loses, rolls back, duplicates or resurrects something.
The design's own list of what it does not solve is repeated only where it understates.

## How statements are marked

- **[M here]**: run for this critique, from a console, over databases made with invented content.
  Scripts and raw output are under
  `spikes\storage\probes\store-entries\survival\`:
  `scripts\Surv.cs`, `scripts\surv.ps1`, `scripts\pk_delete.py`, `scripts\kw_merge.js`,
  `scripts\Run.ps1`, and the output in `out\bases.txt` and `out\chains.txt`. Section 7 describes
  the runs.
- **[M design]**: measured by the design's own probe, `spikes\storage\probes\store-entries\design\out\summary.txt`,
  which I read and which matches the design's tables.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in that ground report.
- **[R ...]**: read from source, with file and line. "Read here" means I read it myself in the
  clone a ground report left under `spikes\storage\probes\`.
- **[A]**: assumed. Neither run nor read.

## 1. Verdict

**Viable with changes.** The core of the design holds where the design says it is measured. An
entry only one copy holds arrives in every KeePassLib method and every KeePassXC release measured,
and nothing the gate reads depends on a modification time, a history item, custom data or a per-key
time. Section 2 lists what survived the attack.

Three survival claims fail under sequences made of ordinary acts, and each fails toward access:

1. **P1.** A refusal (a revocation, a retirement or a profile's removal) has only one witness in
   many copies, and one deletion by hand takes it away. Measured in two steps: a KeePassXC merge,
   then a pykeepass deletion of `KPG revoked client c1`. The revoked client is then admitted, with
   no further merge needed.
2. **P2.** The profile list's identifiers name a generation and not an act. A removal made in one
   copy is undone by a re-addition made in another copy that never saw it.
3. **P3.** The store group's fixed identifier is reused after a deletion. kdbxweb then drops the
   remade store whole, every time a copy holding the old deletion record takes part. Measured all
   the way to a revoked client admitted.

Each has a change that fixes it. None is fatal. Seven minor problems follow, P4 to P10.

## 2. What survived the attack

| Claim | Result | Basis |
| --- | --- | --- |
| Two copies' entries both arrive in a merge | held in every KeePassLib method and every KeePassXC release run, both directions | [M design, sets I, T, D]; [M here, chains A, B, C] |
| A revocation made in KeePass reaches a stale copy through a KeePassXC merge | held: the record comes back and the revocation entry refuses it | [M design, set D into B]; [M here, chain A step 1, 2.4.2 and 2.8.0] |
| A Create new IDs import brings refusals that still count | held: the copy under a new identifier refused by its content | [M here, chain A step 3, `S2-pk+P-CreateNewUuids`] |
| History, ties and rewritten times do not matter | held, as long as nobody edits a store entry (P7) | [M design, sets I and T] |
| KeePassXC's replacement of the database's custom data, and the Android programs' deletion of node custom data | do not reach the store | [M spike]; the store uses neither |
| KeePass, then Keepass2Android, then KeePass | the union, with both copies' deletion records joined | [R android 1.5, 3.1] |
| KeePass, then KeePassDX, then KeePass | the union; deletion records are dropped and the revocation entry refuses | [R android 3.1]; read here, `kpdx458` `DatabaseKDBXMerger.kt:304-311` |
| A copy damaged below is healed by a KeePass Synchronize with the revoking copy | held in every case run here | [M here, chain A step 3, chain C step 2] |

## 3. Problems

### P1, serious: a refusal has only one witness in many copies, and one deletion by hand takes it

**What the design rests on.** The design gives every refusal two witnesses: the refusing entry,
and, once someone deletes that entry, a deletion record naming it (§3.2). §1 point 4 says a merge
"can only add records". §8 says a deleted revocation "still counts after the bin is emptied, by the
deletion record". §11 lists the loss only as "deleted by hand, the bin emptied, the deletion records
cleared and a stale copy merged in, in that order".

**Why that does not hold.** The deletion records travel only through a KeePass Synchronize
[M keepass X4], a Keepass2Android merge [R android 3.1] and a kdbxweb merge (read here,
`kdbxweb` `lib/format/kdbx.ts:416-421`). Copies that get a revocation any other way hold the
revocation entry and nothing else. Many of them also hold the revoked record beside it:

- **A KeePassXC merge into the other copy** brings the record back and delivers no deletion record
  [M design, set D into B, four releases; M here, chain A step 1].
- **KeePass's Overwrite if newer, Overwrite existing and Keep existing**, into the stale copy, do
  the same [M design, set D].
- **KeePassDX applies the deletion records that arrive and keeps none of them.** The file holds no
  call that records one (read here, `kpdx458` `DatabaseKDBXMerger.kt:304-323`, `:358-395`; `3.2.0`
  `:274-300`).
- **KeePass drops a deletion record whose entry is newer** [M keepass X3, "E back with B's change,
  tombstone gone"], and Keepass2Android does the same [R android 3.1]. Any later edit of the record
  in any copy causes this. So does a record written while KeePass's own clock ran ahead.
- **pykeepass deletes without writing a deletion record** [M here: 0 records before, 0 after].

**Sequence A, measured in two steps** [M here, `chains.txt` "A 2.4.2" and "A 2.8.0"]:

1. KeePass revokes c1. It adds `KPG revoked client c1` and deletes c1's record with a deletion
   record.
2. `keepassxc-cli` 2.4.2 or 2.8.0 merges that file into a stale copy. The result holds c1's record,
   the revocation entry, and no deletion record. The gate refuses c1, by the entry.
3. A program built on pykeepass deletes `KPG revoked client c1`. That GNOME Secrets is such a
   program is [A]; the library's behavior is [M here]. **The gate admits c1.** No stale copy has to
   be merged afterwards, because the KeePassXC merge already put the record there.

**Sequence B, read.** Steps 1 and 2 as in A.

3. The revocation entry is deleted and the bin emptied, in KeePassXC, KeePass or Keepass2Android.
   KeePassXC writes a deletion record for it: [M here, chain A 2.4.2 step 2b, "deletion records:
   revoked:c1"]. The gate still refuses c1, as the design describes.
4. KeePassDX, holding its own copy X, merges that file in. X has the revocation entry and no
   deletion records, because KeePassDX dropped them when it merged the revoking copy. KeePassDX
   applies the arriving deletion record, so X's revocation entry goes. It adds c1's record, which X
   has no deletion record for. It keeps no deletion record (read here, `kpdx458`
   `DatabaseKDBXMerger.kt:304-323`, `:497-504`). **The gate, on that file, admits c1.**

**Sequence C, read: the whole group.**

1. Someone deletes the `KeePassGate` group for good. KeePass, KeePassXC and KeePassDX all write a
   deletion record for every entry in it (KeePass `PwGroup.cs:1367-1386` with
   `MainForm_Functions.cs:5257-5260`; KeePassXC 2.8.0 `Group.cpp:49-73` with `Entry.cpp:69-80`;
   KeePassDX 4.5.8 `Database.kt:1165-1193`; all read here).
2. KeePassDX merges that copy into its own. 3.2.0 removes the subtree (`DatabaseKDBXMerger.kt:285-290`);
   4.5.8 deletes every child a record names (`:358-395`). Neither keeps a record. The result holds
   no store and no witness.
3. Any merge with a copy that predates a retirement or a revocation brings back the authority
   entry and the records. The retired authority is served again, and every record the stale copy
   holds is admitted, revoked ones included.

**The same holds for a profile's removal.** It is a refusing entry like a revocation. Delete
`KPG removed reader 1` in a copy where a KeePassXC merge brought the addition back, and `reader` is
on the list again.

**Why this is worse than today.** Today a refusing key can be removed only through KeePass's
Database Maintenance, on its Plugin Data tab [R inventory A9], and no KeePass merge removes a key.
Here, refusals are entries titled `KPG revoked client …` that every program lists, and one tap
deletes them. They all sit in one group, so deleting the group takes every one of them.

**Change.**

- **Write each refusal in a second place that a deletion of entries does not reach**: today's
  add-only keys in `PwDatabase.CustomData` (`KPG.Revoked.<key>`, `KPG.RetiredAuthority.<fp>`, and
  one per profile removal). A refusal then counts if its entry, its deletion record or its key is
  found.
  - KeePassDX keeps database keys by their own times [M spike]. pykeepass keeps them [M others].
    Deleting an entry leaves them alone.
  - What is left: a KeePassXC merge from 2.4.2 on replaces the whole dictionary from the copy it
    loaded second, and changes nothing when both loads fall in one second [M spike, slow and fast
    KDF]. So sequence A with a fast KDF still leaves no key. That residue has to be stated.
  - Database Maintenance's write-back on a changed key count would cost a key again (the trap
    section of CLAUDE.md). The entry still covers it.
- **Correct §1 point 4, §3.2, §8 and §11.**
- **Give refusing entries notes that say what deleting them does**, for example "Deleting this
  entry admits the client again". That this deters anybody is [A].

### P2, serious: the profile list's identifiers name a generation, not an act

**Design §3.3.** The subject is `<name>/<generation>`, and the generation is worked out from what
the file holds. So two copies acting on their own make the same identifier for different acts.

**Widening sequence** (the union it rests on is [M design]; the rest follows from §3.3's rules):

1. Both copies hold `profile reader/1`, and records name `reader`.
2. Copy B removes `reader`, which writes `removed reader/1`. Later B adds `reader` back: the Profiles
   page, or a pairing approving `reader` (§5, the pairing row). That writes `profile reader/2`.
3. Copy A has not been merged with B since step 1. It removes `reader`, which writes
   `removed reader/1`: the same identifier as B's removal.
4. Any merge: the result holds `removed reader/1` and `profile reader/2`.
5. The fold puts `reader` on the list. **Every client whose record names `reader` gets back what
   `reader` grants.** A's removal may be the latest act by any clock, and it is undone. That is the
   widening requirement E5 forbids ("a profile taken off the list never comes back").

§3.3 calls this outcome "the later decision of an operator who saw the removal". It is neither:
B's operator saw B's removal, not A's, and nothing in the fold knows which act came later.

**Narrowing sequence, in the design's own set I** [M design]:

- Copy A adds `reader/1` for a pairing of its own.
- Copy B adds `reader/1` and removes it.
- After the merge, A's addition is gone: one `profile reader 1` entry stands beside
  `profile-removed reader 1`.

So requirement A3 ("a name added in either copy survives a merge"), which §3.3 claims, fails here.

**Renewal answers (§3.4) have the same shape**: numbers count per file, so two copies' answers
share numbers. Nothing widens, because an answer never admits anybody and a renewal still waits for
the operator. That part is minor.

**Change.**

- Give every act an identity of its own, with a random part in the subject.
- Make an addition list the removals it supersedes, meaning those in the file when it was written.
- Count a name only when every removal of it in the file is superseded by an addition that counts.
  This is an observed-remove set in which a removal made at the same time as an addition wins.

The cost is the narrowing side: a re-addition made at the same time as another copy's removal is
lost. The tab can show that.

### P3, serious: the store group's identifier is reused after a deletion, and kdbxweb drops the remade store whole

**Design §2 and §5.** §2 gives the group a fixed UUID, "the same in every database". §5 adds: "when
the store group has to be made in a file whose deletion records name the group's UUID ... the same
write removes that deletion record". That removal happens in one copy only.

**kdbxweb treats a deletion record as permanent** (read here, `kdbxweb` `7601e45`):

- `mergeCollection` skips every local and every arriving node whose UUID is among the records,
  whatever its time (`lib/format/kdbx-group.ts:205-249`, the tests at `:214` and `:227`). A group
  skipped this way takes everything below it, and nothing is recorded for the children.
- `merge` copies the other copy's deletion records in (`lib/format/kdbx.ts:416-421`).

**Measured** [M here, `chains.txt` "C step 1" to "C step 3c"]:

1. In copy X1, the store group (authority fpA, clients c1 and c2) is deleted for good, the way
   KeePass deletes: a deletion record for every entry and for the group.
2. Copy P1 starts from X1. The operator makes a new authority fpB. The plugin makes the group again
   under the same UUID and removes the group's deletion record. Client c5 pairs, and Q is a copy
   taken at this point. In P2, c5 is revoked.
3. kdbxweb 2.1.1 merges P2 and X1, in either direction. The result has **no store group, no
   authority fpB and no revocation of c5**, and it carries the group's deletion record. Merging P2
   into that result again changes nothing: the drop sticks. Merging Q with X1 drops the store too.
4. A KeePassLib Synchronize of that result with Q brings the store back, and c5 is refused only by
   c5's own deletion record. KeePass itself heals the drop: its Synchronize of P2 with X1 keeps the
   non-empty store and drops the group's record [M here; M keepass X3].
5. The revocation reaches Q only as an entry, through `keepassxc-cli` 2.8.0 (Q5: c5's record, the
   revocation entry, no deletion record for c5). kdbxweb merges Q5 with X1, which leaves no store.
   KeePassLib then merges that result with Q, under Synchronize, Overwrite if newer, Overwrite
   existing and Keep existing, in both directions. **c5 is admitted in all eight results**, and in
   the Create new IDs import into Q as well.

**What the gate does.** Wherever KeeWeb takes part, the remade store vanishes. The gate serves
nothing, and every client pairs again, at every KeeWeb merge with an old copy. Where the revocation
had reached the copy only as an entry, a revoked client is admitted.

**Change.**

- Never reuse an identifier that a deletion record might name. Derive the group's UUID from
  something new each time the store is made, such as the first authority's digest
  (`KPG/1/group/<fp>`).
- Read granting records in any group of that family.
- Drop the step that removes the deletion record.

With P1's second place for refusals as well, kdbxweb's drop costs availability only.

### P4, minor: deleting a client's record or the store group by hand does not keep anything out

§8 makes two claims:

- "after a permanent delete the deletion record keeps it out even if a merge brings the entry
  back";
- "Emptied, the authority and the records are gone for good and stay refused by their deletion
  records".

Neither holds in general:

- The deletion record is dropped by KeePass and Keepass2Android when the entry is newer
  [M keepass X3]. KeePassXC and KeePass's other methods never deliver it to the other copy, and
  KeePassDX drops it (P1).
- **The group, deleted in pykeepass, comes back with its authority serving.** pykeepass deleted
  `KeePassGate` in the revoking copy and wrote no record. Every KeePassLib method that merged a
  stale copy into it brought the store back: authority fpA served and c2 admitted. Only c1 stayed
  refused, by the record the plugin had written at its revocation [M here, `chains.txt` "B step 1",
  "B step 2"].
- KeePassDroid writes no deletion record for a group it deletes. It sends each entry to the bin, or
  deletes it with a record (read here, `kpd27` `DeleteGroup.java:66-91`, `PwDatabaseV4.java:504-508`).

**What the gate does**: a record or a store deleted by hand is admitted or served again. A hand
deletion is not a revocation, but §8 presents it as having the same effect.

**Change**: drop both claims, and have the tab and `docs/` tell the operator that only the KPG tab
revokes.

### P5, minor: every store entry ties in every KeePassDX merge, and a tie moves it

**Why.** Store entries never change, so both copies hold each one at the same time.

- On equal times, KeePassDX 4.5.8 moves the open copy's entry to the arriving copy's group (read
  here, `kpdx458` `DatabaseKDBXMerger.kt:519-524`). It moves groups to the arriving copy's parent
  in the same way [R android 3.2].
- So an entry or the group misplaced in any older copy (moved, or put in the bin) is misplaced in
  the KeePassDX result, whatever the newer copy did since.
- KeePass and Keepass2Android relocate by the later location time instead [M keepass X3;
  R android 3.1].

**What the gate does**: a granting record out of place admits nobody, so the gate serves nothing
or refuses the client. That fails closed.

**Change**: none is needed for security. The tab's "put it back" has to expect to be pressed again
after every KeePassDX merge.

### P6, minor: protecting the authority's string exposes it to a known corruption

**The corruption.** gokeepasslib corrupted protected values in two ways (read here, `gokeepasslib`
`v3.7.1`, `CHANGELOG.md`):

- before 3.7.1, every protected value after the first subgroup, wherever entries and groups
  alternate in a group, and wherever a `<String>` follows a `<History>`;
- before 3.7.0, every protected value after a protected binary in KDBX 3.1 metadata.

79 modules import it, and which of them save files was not read [R others].

**Why it matters here.** Today the authority is an unprotected value in custom data, outside the
protected stream. Which files such a module would meet in that shape is [A].

**What the gate does**: the authority will not read, so the gate serves nothing and every client
pairs again.

**Change**: leave `KPG.Data` unprotected, as today, or accept the risk.

### P7, minor: one stray edit makes two versions after all

§7 ends: "the store never puts two different versions of one entry into the world". That holds only
while nobody edits a store entry. Any of these makes a second version:

- a person's edit in any program;
- an XML Replace;
- a pykeepass edit, which moves no time [R others];
- a KeePassDX or KeePassXC edit.

The newest whole entry then wins every merge and the loser goes into history. On equal times one
side is lost outright [M keepass X2]. One keystroke in an authority's field spreads by Synchronize
to every copy.

§8 calls this "as today", but today no program's interface edits that value: the Plugin Data tab
only deletes [R inventory A9].

**What the gate does**: it fails closed.

**Change**: the tab should name the remedy, which is restoring the history item.

### P8, minor: the prune's "authority gone" is not defined, and this design makes an absent authority common

§5 prunes "revocations whose certificate has expired or whose authority is retired or gone". In
this design an authority that is out of place, in the bin, moved by KeePassDX (P5) or damaged (P7)
does not count. If the prune reads "does not count" as gone, the plugin deletes that authority's
revocations itself. The deletion records it writes then meet P1.

**Change**: prune a revocation only once its certificate has expired, or once its authority has a
retirement.

### P9, minor: a merge can push the list past its ceiling

Take two copies, each well under 32 names. Their union can be over 32, and then the list reads as
one that will not read: every client stays admitted and reaches nothing. This follows from §3.3 and
the measured union. It fails closed.

**Change**: the Profiles page names the overflow and offers the removal.

### P10, minor: the composition with rules as entries is claimed, not shown

§12 says the design "composes best" with rules kept as entries naming their node by UUID. Two
things count against that claim:

- KeePass's Create new IDs, which File then Import preselects [R keepass 2.4], gives every arriving
  node a new UUID [M keepass X1]. A rule keyed by the old UUID does not follow the copy. The copy
  inherits from its new place, and the import puts that at the root.
- A rule edited in two copies needs the act identity P2 asks for.

This concerns the composition claim, not this option.

## 4. Datum by datum

| # | Datum | Survives | Fails | Gate then |
| --- | --- | --- | --- | --- |
| 1 | Authority | every save measured or read; union in every merge [M design] | remade after a deletion, dropped by kdbxweb (P3); retired, then served again (P1 C); corrupted (P6); edited (P7) | serves nothing, or serves a retired one (P1 C) |
| 2 | Retirement | union [M design]; still counts in the bin | one hand deletion plus a KeePassDX merge (P1 C); kdbxweb (P3) | a retired authority served |
| 3 | Admission records | union; brought back beside their revocation [M design; M here] | the refusal taken away (P1); a deletion by hand does not keep one out (P4) | a revoked client admitted |
| 4 | Revocations | every merge as an entry [M design; M here] | one deletion by hand where it is the only witness (P1, measured); kdbxweb (P3, measured); the prune (P8) | a revoked client admitted |
| 5 | Standing answers | union | shared numbers (P2 note) | a renewal dialog where `409 forever` was meant |
| 6 | Profile list | union; a removal refuses wherever it is found | a removal made in another copy undone (P2); a removal entry deleted by hand (P1); the ceiling (P9) | a removed profile grants again |
| 7 | Access rules | not placed by this option | P10 for the composition | as today |
| 8 | Bin markers (optional) | as refusals | as P1 | |

## 5. Survival claims that rest on reading or assumption

Each of these has to be measured before the design can rely on it.

1. **Keepass2Android merges of this shape**: that an entry only the arriving copy holds arrives,
   that a group made in both copies is one group, and that both copies' deletion records are joined
   and later dropped for a non-empty group [R android 1.5, 3.1; A for the dropping]. Only an entry
   the open copy held was ever measured [M spike].
2. **KeePassDX 4.5.8 and 3.2.0 merges**: entries added, deletion records applied and dropped,
   3.2.0's need for the parent group, and the moves on equal times. All read; none run.
3. **Every Android save of the store**: the two custom strings, the protection flag on a value of
   2,903 characters, the group's UUID and its two flags. Entries as wholes were measured [M spike];
   custom strings never were.
4. **KDBX 3.1 output by Keepass2Android** carrying the store's strings, their protection and the
   group's flags [A in the design, §10].
5. **What each program shows of a protected custom string** ("other programs hide it until the
   person asks", [A]).
6. **KeePass's window**: File then Synchronize, File then Import, the entry dialog open while the
   plugin deletes, Delete Duplicate Entries, Empty Recycle Bin, and Database Maintenance's removal
   of deleted objects. All read. "No dialog puts back a copy of a group's entry list" is [A].
7. **KeePass's permanent delete writing a record for every child.** Read; my probe copies the rule
   rather than running KeePass's command.
8. **KeePassXC's window merge, its merge on reload, and KeeShare** [R].
9. **Strongbox, KeePassKit, keepass-rs, KeePassium and programs built on gokeepasslib**: read only.
   Whether any of them delete without a deletion record was not read.
10. **Which end-user programs delete through pykeepass without a record.** The library is
    [M here]; the programs are [A].
11. **Searching off keeping the store out of search, autofill and the keyboard on Android** [R].

## 6. What the design should change in its own text

- §1 point 4: a merge removes records wherever deletion records travel (KeePass Synchronize,
  Keepass2Android, kdbxweb) and wherever they are applied (KeePassDX).
- §3.2, §8 and §11: a deletion record is not a dependable second witness (P1, P4). The loss needs
  one deletion by hand, not three deliberate acts.
- §3.3: the "later decision" sentence (P2).
- §5: the step that removes the group's deletion record (P3).
- §7: "never two versions" holds only while nobody edits a store entry (P7).

## 7. What was run for this critique

**The probe.** `Surv.cs` is compiled in Windows PowerShell 5.1 against KeePass 2.62's `KeePass.exe`
from `.keepass\2.62` and used as a library, with no window. It makes the
databases with invented content, AES-KDF at one round and password `test`, so that kdbxweb can open
them:

- `D0`: the store, with authority fpA and clients c1 and c2.
- `P`: c1 revoked the way the design revokes (§5).
- `S`: the stale copy.
- `X1`: the store deleted the way KeePass deletes.
- `P1` and `Q`: the store made again under the same UUID with authority fpB and client c5, and the
  group's deletion record removed.
- `P2`: c5 revoked.

Its `dump` applies the design's fold from §4 and prints each client's verdict.

**The other programs.**

- `keepassxc-cli` 2.4.2 and 2.8.0: the copies the KeePassXC report downloaded. Their archives match
  `keepassxc.sha256` on the spike branch, and both executables carry a valid Authenticode signature
  from DroidMonkey Apps, LLC, checked again here. They ran `merge -s` and `rm`.
- pykeepass 4.2.0, through `uv run --no-project --with pykeepass python -I -X utf8`, deleted an
  entry and a group.
- kdbxweb 2.1.1, the npm copy the others report installed, called through `node` by
  `scripts\kw_merge.js`, merged.
- KeePassLib merged under all five methods, in both directions.

**What did not work as planned.** In 2.8.0 the second `rm` failed with exit 1, because on this
de-DE machine the bin is named `Papierkorb`. The revocation entry stayed in the bin, where the
design still counts it.

No window was opened, no test tier or `build.ps1` was run, and nothing was written to either
worktree.
