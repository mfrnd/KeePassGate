# Critique of `hardened-custom-data`: survival across programs

Written 2026-11-15 against `option-hardened-custom-data.md` (the version of 17:33), KeePassGate
master `a42e38aa`, and the six ground reports. Lens: survival. Every save and merge in the ground
reports is walked through the design, datum by datum, looking for a sequence that loses, rolls
back, duplicates or resurrects something. Key `hardened-custom-data-survival`.

## How statements are marked

- **[M here]**: measured for this critique, 2026-11-15. Section 1 lists what ran and where the output is.
- **[M design]**: the design's own probe output, which I read in its scratch directory.
- **[M spike §n]**, **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M measured]**: measured in the
  spike's `RESULTS.md` section named, or in the ground report of that key.
- **[R file:line, tag]**: read from source at that tag. **[R keepass §n]** and the like: read by that
  ground report.
- **[A]**: assumed, reasoned and neither run nor read.

Requirement numbers (E, A, O, P) are the `inventory` report's.

## 0. Verdict

**Not viable.** The design's headline result is that a KeePassXC merge, which replaces the database's
custom data, loses nothing, because the root group's copy (the mirror) still holds what the
dictionary lost: 14 of 14 keys. That holds for one step. After a KeePassXC merge of an older copy into
the fresh one, the fresh state lives only in the root group's custom data. KeePass takes the root group
whole from one copy, and so do Keepass2Android, KeePassDX 4.5.8, Strongbox and keepass-rs. The plugin
never moves the root group's time, and KeePassXC and KeePassDX stamp it. So the next synchronize that
meets any older copy can throw the mirror away. The plugin's own KeePass is no exception, because its
reconcile runs after the merge. I measured this in four of five cells: the result held 7 of 14 keys.
The revocation, the retirement and the profile removal were gone, and the revoked client would be
admitted (F1). No arrangement inside custom data fixes the direction in which the merged file arrives
into another copy. Together with the design's own case 1, that leaves E3 and E4 unmet against
KeePassXC.

Below that, three serious problems: generation numbers are read as causality and are not, so
concurrent rule edits widen access (S1, contradicting the design's case 7); an import with new
identifiers brings narrowed subtrees back under the root's grant, which the design claims cannot happen
(S2); and a narrowing made by moving an entry is undone by merges and recorded nowhere (S3).

## 1. What I ran and what I read

**Probe**, Windows PowerShell 5.1. KeePassLib 2.62 was loaded from
`.keepass\2.62\KeePass.exe` with no window. The only other program was
`keepassxc-cli.exe` 2.8.0, from the archive the `keepassxc` report verified against the spike's pin;
I checked that its Authenticode signature is valid and from DroidMonkey Apps, LLC. All content is
invented and the password is `probe-only`. Three `keepassxc-cli` runs; the slowest took 83 s on a
loaded machine.

- Script: `spikes\storage\probes\hardened-custom-data\survival\scripts\Chain.ps1`
- Output: the `out` directory beside it, `make.txt`, `chain.txt`, `branch.txt`, `xctimes.txt`; the
  files under `work`.
- **Bases** use the design's layout. C, the stale copy, holds 7 `KPG.` keys in both replicas. P, the
  fresh copy, holds 12: C after revoking `c2`, retiring `a1`, adding `a2` and `c3`, removing the
  profile `writer`, and narrowing G and E1 at generation 2. Every rule text carries `#`. All keys are
  timeless, the file is 4.0, Argon2d at 200 iterations and 64 MiB so that the two `keepassxc-cli` loads
  fall in different seconds, and the root group's time is `T0` in both, as the plugin leaves it.

**Read** for this critique, beyond the ground reports:

- KeePass 2.62 `KeePassLib/PwDatabase.cs:763-840` (the group handler) and `:904`
  (`ghSrc(pdSource.RootGroup)`, the source root merged by that handler first).
- KeePassXC 2.8.0: `src/core/Merger.cpp:168-184` and `:264-329` (the root group is never passed to
  `resolveGroupConflict`) and `:700-765` (custom data); `src/core/CustomData.cpp:88-109` and
  `:165-197`; `src/format/KdbxXmlReader.cpp:427-451`; `src/format/KdbxXmlWriter.cpp:169`, `:264-292`;
  `src/format/KeePass2Writer.cpp:52-108`; `src/gui/DatabaseWidget.cpp:1448-1467` and `:2211-2216`.
- KeePassDX 4.5.8: `database/merge/DatabaseKDBXMerger.kt:247-250` (the root), `:268`, `:415-432`,
  `:463-527`, `:557-593`; `database/element/group/GroupKDBX.kt:109-126`.

## 2. Problems, most severe first

### F1 (fatal). The mirror rescues a KeePassXC merge for one step, and the next merge throws the rescue away, in KeePass with the plugin as well

**The sequence.** Every step was run except step 3's program: there a real KeePassXC save stands in
for a phone's save, because both stamp the root group.

1. Desktop, KeePass with the plugin. The operator revokes `c2`, recreates the authority (`a1` retired,
   `a2` made), pairs `c3`, removes the profile `writer`, and narrows G and E1. File P holds 12 keys in
   each replica. The root group's time does not move, since the plugin moves no node time (E13).
2. Laptop, KeePassXC 2.8.0. P is open (the current file), and an older copy C is merged into it (a
   backup, a conflict copy, the phone's file). Result R: the dictionary holds C's 7 keys, the mirror
   P's 12, and the union is 14. The root group's time is unchanged. [M here; M design, four releases]
   The design's §5 stops here.
3. An older copy saved by a program that stamps the root group. Here, `keepassxc-cli add` on C gives
   Q, whose root group takes the time of the save [M here]. KeePassDX does the same at every save
   measured [M spike §6, `RESULTS.md:447-448`]. It also touches every parent group when an entry is
   added, edited, moved or deleted [R android §1.4, `NodeVersioned.kt:134-146`].
4. Desktop, KeePass with the plugin. File then Synchronize of R with Q, or of R with C itself when no
   program has stamped either root. KeePassLib 2.62 `MergeIn(Synchronize)`, then a save [M here]:

| Open copy | Arriving copy | Root groups | Dictionary | Mirror | Union | What the gate reads |
| --- | --- | --- | --- | --- | --- | --- |
| R | Q | Q's newer | 7 (C's) | 7, Q's | **7** | `c2` admitted, `a1` serving, `writer` in force |
| Q | R | Q's newer | 7 | 7, Q's | **7** | the same |
| R | C | equal | 7 | 7, C's (arriving wins a tie) | **7** | the same |
| C | R | equal | 7 | 12, R's (arriving wins a tie) | 14 | intact |
| R2 (C after KeePassXC merged P into it) | Q | Q's newer | **14** | 7 | 14 | intact: the fresh state was in the dictionary |

The plugin's reconcile runs at `FileSavingPre`, after `MergeIn` [R keepass §4, `ImportUtil.cs:204`,
`:234`]. It therefore sees what the table shows and copies it into both replicas, and File then
Synchronize then copies the result over the other file [R keepass §4, `ImportUtil.cs:259-261`]. Both
files now hold the stale state, consistently.

**What the gate then does.** Client `c2` has a record and no revocation, under `a1`, which is present
and no longer retired: **`c2` is admitted, a revocation undone (E3)**. **`a1` serves again**, with every
certificate it ever issued that is still in date and recorded (E4). `a2` and `c3` are gone, so
`c3` is refused (fails closed). **`writer` is in force again**, so every rule that names it grants again:
a widening (E5). Nothing in the file shows any of it, and the Rules page lists nothing about the
database-level keys.

**Rules held in these cells, and only by luck.** G and E1 kept P's narrowing because P's nodes were the
newer. The ledger's generation 2 is gone from both replicas, so each node holds a text above the head
and *disagrees*; the intersection with generation 1 is narrow [M here, for the storage; the state is
the design's table]. Two cases lose the rules too:

- **The root's own rule** is the `KPG` key in the very custom data that the stale root group replaced.
  A narrowing made at the root in P is lost with the mirror, and its ledger with the dictionary. The
  root then *agrees* with the rolled-back head, which is wide. This follows from the measured
  replacement; my bases carry no root rule.
- **A stale group touched later.** A KeePassDX entry edit touches every parent group [R android §1.4],
  so after it the stale copy's G is the newer. G wins whole, its old rule agrees with the rolled-back
  head, and it is wide. [A, from the read rules]

**Why the design does not see this.** Its §5 row for a KeePass Synchronize ("complete; the reconcile
at the ending save makes both replicas the union") holds only when each input's dictionary carries
its own state. Its §3 claim that "one synchronize in a KeePass with the plugin leaves both files
reconciled" is true, but reconciled to whichever state survived the merge. Its case 2 names "two
foreign programs in a row, with no KeePass save between them". Here one foreign merge comes first, and
the step that loses is KeePass's own, with the plugin installed.

**Every second step that discards the rescue.** R is the KeePassXC result of step 2; "stale" is any
copy older than P.

| Second step | R open, stale arriving | Stale open, R arriving | Basis |
| --- | --- | --- | --- |
| KeePass Synchronize, OverwriteIfNewer | lost when the arriving root is newer or equal | lost when the open root is newer | [M here] for Synchronize; [R `PwDatabase.cs:831-835`] for OverwriteIfNewer's identical group handling |
| KeePass OverwriteExisting | lost always | kept | [R `PwDatabase.cs:829-830`] |
| KeePass KeepExisting | kept | lost always | [R `PwDatabase.cs:836`] |
| KeePass CreateNewUuids (File then Import's preselected method) | kept | **lost always**: R's root becomes a new ordinary group, never read as the mirror | [R `PwDatabase.cs:768-772`, `:796-823`]; [M keepass §2.4] for the new group |
| KeePass on the save that finds the file changed | kept, if the outer `FileSavingPre` runs before `PreSaveValidate`'s synchronize | as Synchronize | [R keepass §4], not run |
| Keepass2Android merge | as KeePass Synchronize | as KeePass Synchronize | [A]: a port of KeePassLib's merge [R spike §6] |
| KeePassDX 4.5.8 merge | lost when the arriving root is newer | lost when the open root is newer or equal | [R `DatabaseKDBXMerger.kt:247-250`, `GroupKDBX.kt:117`]: the root is `updateWith` whole and never key by key. **The design's §5 row ("arriving items put key by key, nothing removed", [A]) is wrong** |
| KeePassDX 3.2/3.3, KeePassDroid save, gokeepasslib before 3.7.0 | the mirror is deleted | | the design's case 2 |
| kdbxweb (KeeWeb) merge | kept: the open copy's root custom data always | **lost always** | [R others, `kdbx-group.ts:290-302`]; [M others] for G |
| Strongbox merge | as KeePass Synchronize | as KeePass Synchronize | [R others, `DatabaseMerger.m:294-302`] |
| keepass-rs merge | lost when the arriving root is newer (no metadata merge, so the dictionary stays the open copy's) | lost when the open root is newer | [R others, `merge.rs:78-85`, `:326-350`] |
| KeePassXC merge again | R stays fragile (its dictionary takes the source's, its mirror stays) | R's mirror never reaches the target: group custom data is never merged | [R `Merger.cpp:264-329`]; [M here] |

**How long the window lasts.** Until a plain save of R in KeePass with the plugin, from any plugin
write or any ordinary edit and save. That save's reconcile copies the mirror into the dictionary,
after which every merge carries the state as a union. The common first acts on a file that came back
from the laptop are not plain saves. They are File then Synchronize, a synchronize trigger at
opening, File then Import, or the phone merging it before the desktop opens it.

**Why no change inside custom data fixes it.**

- **Reconcile earlier.** I ran the reconcile in memory on the open copy before the merge [M here]. It
  rescues the case where R is the open file (14 of 14). It does not rescue the case where R arrives
  into a copy whose root group wins (7 of 14). KeePass raises no event before or inside `MergeIn` on
  File then Synchronize [R keepass §4], and the arriving file's root custom data is discarded inside
  it. Done at `FileOpened`, it would also bend O1, since opening would change the database in memory.
- **Move the root group's time at every plugin write.** A KeePassDX entry edit or a KeePassXC save
  stamps a later time afterwards [M spike §6; M here], so this decides nothing reliably.
- **More group replicas.** Every group's custom data merges the same way: whole by the group's time in
  KeePass, Keepass2Android and KeePassDX, never in KeePassXC, deleted by KeePassDroid. Another replica
  adds the same failure.
- The design's own case 1 remains: KeePassXC 2.4.1, KeePassXC within one second, and keepass-rs carry
  no database custom data at all.

So against KeePassXC, a revocation or a retirement survives only if KeePass with the plugin saves the
merged file before anything merges it again. That is ordering, not storage, and it is not under the
operator's control when the file moves through a synchronizer.

### S1 (serious). A higher generation is read as one that has seen the lower ones, and it need not have

**Rules.** I measured the storage; the classification follows the design's §2 table.

1. Every copy holds E1 at generation 1: `{"#":"1","reader":["read","list"]}`, recorded as `1.x`.
2. Copy B, KeePass with the plugin on the operator's second machine (the design's §11 mentions one),
   edits E1 twice: generation 2 adds a profile (`2.w`), generation 3 widens it (`3.v` =
   `{"#":"3","reader":["read","list"],"writer":["read","update"]}`), at `T0+1h` and `T0+2h`.
3. Copy A, from generation 1, narrows `reader` to nothing: `2.y` = `{"#":"2","reader":[]}`, at `T0+3h`,
   later than both of B's edits.
4. KeePass Synchronize, in either direction. E1 holds A's `2.y`, the newer entry whole, and the ledger
   union holds `1.x 2.w 2.y 3.v` [M here, `branch.txt`, both directions].
5. The design's table: the head is every version at the highest generation, here `3.v` alone. The node
   holds the exact text of a recorded version of a lower generation, so it is *behind*, and `3.v` is in
   force.

**What the gate does.** `reader` reads and lists E1, which A's operator had just closed to it, and
`writer` gains `update`. The reconcile at the next save writes `3.v` into E1 without moving its time,
and the synchronize copies that into the other file, so A's narrowing is gone from both copies.
Widening (E5). Had B's entry been the newer, E1 would *agree* with `3.v`, and the narrowing would be
lost just as silently. The design's §10 case 7 ("in force as their intersection") holds only when both
branches stopped at the same generation. Its premise that "a higher generation was written by somebody
who had seen that one" (§2) is false across copies.

**The same route after a rollback.** After a rollback (F1, or the design's cases 1 to 3), the operator
sees a wide rule and narrows it again, at a generation the lost copy already used. When the lost copy
returns by any merge, its higher generation makes the operator's latest decision *behind*.

**Profiles, by the same counter** (reasoned from the design's §1 rules):

1. Every copy holds `writer.in.1`.
2. Copy A removes `writer` (`writer.out.1`) and later adds it back (`writer.in.2`).
3. Copy B removes `writer` (`writer.out.1`, the same key as A's).
4. Union: `in.1 out.1 in.2`. The highest `in` is above the highest `out`, so `writer` is in force.

B's removal has no effect: it is the same key as A's earlier one. A name taken off the list comes back
(E5), against the design's claim that "a removal is an added key, so it survives every union". It
survives as a key and is outranked by an addition that never saw it.

**Fix.** Each ledger key names its parent version, as in `KPG.Rule.<uuid>.<gen>.<d>.<parent d>`.
The heads are the versions no other version names as a parent. *Behind* requires the node's version to
be an ancestor of a single head, and two heads disagree. For profiles, each removal carries an
identifier of its own, each addition names the removals it saw, and a removal that no addition saw
wins. Standing answers need no change: a refusal lost this way brings back the approval dialog and
admits nothing by itself.

### S2 (serious). An import with new identifiers brings a narrowed subtree back under the root's grant, which the design claims cannot happen

The design claims a duplicate "narrows as written and grants nothing" (§2, §5 KeePass import row, §6). Two
things are wrong with that.

**The formula grants an unrecorded rule.** In §2, the witnesses at a disagreeing node are "the node's
own document (or its absence) and each head version". With nothing recorded there is no head version,
so the node's own document is the only witness and its grant counts. The prose claims the opposite. As
written, every duplicate, every rule restored by a program that resurrects deleted nodes (KeePassXC
never applies deletions [M keepassxc §2]) and every rule typed in by a script grants as written.

**An unruled duplicate inherits, and the ledger cannot know it is a duplicate.**

1. The root grants `agent` `["read","list"]`. Later, on the desktop, group Finance gets
   `{"#":"1","agent":[]}`, a seal for that profile. Finance had no rule before.
2. The operator recovers a deleted entry by File then Import of an older backup B, from before the
   seal, with the method KeePass preselects, Create new IDs [R keepass §2.4, `ImportMethodForm.cs:37`,
   `:69`].
3. KeePass adds B's top-level groups again at the open root, under new identifiers, with every group's
   and entry's custom data copied [M keepass §2.4; R `PwDatabase.cs:768-772`, `:811-813`,
   `:860-862`]. Finance' carries no rule, has nothing recorded, and so *agrees*, and inherits the root.
4. The gate: `agent` reads and lists every entry in Finance', copies of the secrets the seal
   withdrew, as of the backup's date. Widening (E5). Had Finance carried an older, wider rule in B, the
   duplicate would carry that rule, unrecorded, and grant it by the formula as written.

**Fix, and what it does not reach.** State the formula: a rule with nothing recorded is a witness that
refuses at its node and below until the operator records it. That closes the ruled duplicate and the
resurrected rule. It does not close the unruled duplicate under a broad ancestor grant, because a new
identifier carries no link to its original. No event lets the plugin tell an imported group from one
the operator made, since both first appear at a save. That residue is shared by every option keyed by
node identifier, today's store included. The design must stop claiming the case is safe, and the
operator documentation should warn against importing with new identifiers into a database the gate serves.

### S3 (serious). A narrowing made by moving an entry is undone by merges, and nothing records where the entry was

Inheritance makes an entry's position part of its rule. The ledger records rules per node identifier
and not positions, and the design's §6 claim that "nothing a person does to one place widens what a
client reaches" is argued over rules only.

1. Desktop: the operator moves Payroll from Shared (`agent` reads) into Sealed (`agent` gets nothing).
   KeePass moves the location time and not the modification time [M, `FINDINGS.md:417` via inventory §4.3].
2. The phone, KeePassDX 4.5.8, has the current file open and merges an older copy, in which Payroll is
   in Shared under the same modification time.
3. KeePassDX puts an entry under the arriving copy's parent whenever the arriving entry is as new or
   newer, and never looks at the location time [R `DatabaseKDBXMerger.kt:509-524`]. Payroll goes back to
   Shared. The phone saves.
4. KeePass synchronizes and relocates by the later location time [R keepass §2.2]. Whether KeePassDX
   sets one on that move is [A].
5. The gate: `agent` reads Payroll. Widening (E5), with no `KPG` datum touched.

KeePassXC loses the same move a different way: when the other copy changed the entry's content, the
moved entry takes the other copy's older location time, and the move never reaches that copy [M
keepassxc §4, `move-A-edit-B`, and the round trip].

**Fix.** Record, in the ledger, the entries a sealing or narrowing group holds when its rule is
recorded, and treat an entry found outside such a group without a recorded move as disagreeing: the
intersection of the answers at both positions. Short of that, the documentation must state that a
narrowing is written as a rule and never made by a move. This is not specific to this option.

### M1 (minor). KeePassXC stamps the plugin's own keys with times, so the file is 4.1 for good and KeeWeb can make it unreadable

1. The file becomes 4.1 for a reason that is not the plugin's: every KeePassDX and KeePassDroid save
   writes 4.1 [M spike §5, §6]; a KeePassXC 2.7.12 or later recycle sets a previous parent
   [M keepassxc §6]; a group tag does it anywhere.
2. KeePassXC 2.7 or later saves it. It keeps the version it read [R `KeePass2Writer.cpp:55-57`],
   gives every timeless item the load time [R `KdbxXmlReader.cpp:451`, `CustomData.cpp:97-98`] and
   writes the database's item times in 4.1 [R `KdbxXmlWriter.cpp:169`, `:287`]. Measured: both
   timeless `KPG.` keys came back carrying the save's time [M here, `xctimes.txt`].
3. KeePass 2.62 keeps those times and writes 4.1 from then on [R inventory P12].
4. KeeWeb saves or merges it: KeePass 2.62 can no longer open the file [M others].

The gate serves nothing (fails closed), and the operator's KeePass cannot open the database. The
design's §8 ("KDBX 4.0") and case 8 ("this design only keeps its own keys from being the cause") hold
only for files no KeePassXC 2.7 save has met in 4.1. Times on write-once keys change no merge's outcome.

### M2 (minor). A revocation pruned by a KeePass whose clock runs ahead comes back as an admission

1. Machine A revokes `X`, whose certificate is good until 2027-03-01.
2. Machine B, KeePass with the plugin and a clock a year ahead, makes any plugin write. The prune
   removes `KPG.Revoked.X` from both replicas, because B believes the certificate expired.
3. Any merge with a copy from before the revocation adds `KPG.Client.X` back, since keys only one side
   holds are added.
4. A's gate, with a correct clock: a record, no revocation, a certificate in date. `X` is admitted.

Today's store shares this. The design's test for a prune ("a key whose return does no harm") and its
"whatever the clocks" both assume the pruning machine's clock is right. Fix: prune a revocation only
well after its date, or keep revocations as long as retirements.

### M3 (minor). Keep all on the Rules page turns a KeePassDroid deletion into a permanent widening

A KeePassDroid save deletes every rule [M spike §6], and a KeePass synchronize at equal node times
takes the phone's nodes [M keepass §1a, §3]. The Rules page then lists every ruled node as missing its
rule, with Restore and Keep, "each for one row or for all". Keep all records the absent state as a
tombstone at the next generation, so every seal and narrowing becomes a recorded removal, and the
grants above flow into what was closed. That is one press on a page the design expects phone users to
visit daily (§11). Fix: no bulk Keep for rows whose node lost its rule, and each such row shows what
keeping it opens.

### M4 (minor). Two copies' profile additions can cross the ceiling together

Two copies each add ten names to a list of fifteen. The union holds 35 names in force, above 32, which
reads as an unreadable list: "every client stays admitted and reaches nothing" (§1). Today's
`ProfileNames.Merge` leaves the excess out at OK; in a union no single writer can. This fails closed,
as an outage until the operator removes names.

## 3. Program by program

Each row is one step on its own, from an intact file. The last column tells whether the program can be
the second step of F1.

| Program and step | Database-level data (1 to 6) | Rules (7) | Can discard the F1 rescue | Basis |
| --- | --- | --- | --- | --- |
| KeePass 2.62 Synchronize, OverwriteIfNewer | union | the newer node whole; ties to the arriving copy; stale under newer is *behind* | yes | [M design; M here] |
| KeePass OverwriteExisting, KeepExisting | union for keys only one side holds | arriving whole, or open kept | yes, one direction each | [M spike §2] for the dictionary on 2.61.1; [R] for the root |
| KeePass CreateNewUuids | union | duplicates: S2 | yes, R arriving | [M keepass §2.4]; [R] |
| KeePassXC 2.4.2 to 2.8.0 merge, loads in different seconds | one replica each side; the union is complete | groups keep the target's rule under the newer time (*behind*); entries newer whole | creates the fragile state | [M design] |
| KeePassXC merge, one second; KeePassXC 2.4.1 | the target's state in both replicas | as above | the design's case 1 | [M design; M spike §5] |
| KeePassXC reload on a changed file | the in-memory copy merged into the one just loaded, which is loaded later and keeps its dictionary: **the disk copy's state in both replicas** | as above | as case 1, in the disk copy's favor | [R `DatabaseWidget.cpp:2211-2216`, `Merger.cpp:724-729`] |
| KeePassXC 2.8.0 remote sync | the remote's dictionary in both files; each keeps its own mirror | as above | the remote file never receives the local state | [R `DatabaseWidget.cpp:1448-1467`] |
| KeePassXC save | kept; in a 4.1 file, every key gets a time (M1) | kept | no | [M design; M here] |
| Keepass2Android save; merge | kept; union | kept; newer whole | merge: yes [A] | [M spike §6] |
| KeePassDX 4.5.8 save; merge | kept, 4.1; union | kept; the arriving rule under the newer time (*behind*); positions: S3 | merge: yes [R] | [M spike §6]; [R] |
| KeePassDX 3.2/3.3 save; KeePassDroid save | dictionary kept; mirror deleted [A for the root] | group rules (DX 3.x) or every rule (KeePassDroid) deleted: grants off | yes, by deletion | [M spike §6] |
| kdbxweb save; merge | kept (no empty values); union | the open copy's rule under the newer time | merge: yes, stale open | [M others; R others] |
| pykeepass, KeePassium, MacPass, Strongbox save | kept | kept | Strongbox merge: yes | [M others] for pykeepass; [R others] |
| keepass-rs merge | the open copy's dictionary; the newer root | the newer node | yes | [R others] |
| gokeepasslib before 3.7.0 save | mirror deleted | group rules deleted | yes | [R others] |
| whole-file synchronizer | one copy whole | one copy whole | the design's case 3 | [R others] |

**Datum by datum**, beyond F1 and S1 to S3:

- **(1) Authority.** Written once and never contested by any measured program. Lost only with its
  replicas (F1, cases 1 to 3), which fails closed for its clients.
- **(2) Retirement.** The same carrier, so the same losses, and a loss here is not closed: the old
  authority serves again (F1).
- **(3) Record.** Removed at revocation, so a stale copy's record always comes back beside the
  revocation, and is refused while the revocation survives. M2 is the clock exception.
- **(4) Revocation.** F1, M2.
- **(5) Standing answer.** A lost refusal shows the approval dialog again, and a lost lift refuses a
  renewal. Neither admits anything. S1's counter flaw is harmless here.
- **(6) Profile list.** F1 (a removal lost), S1 (a removal outranked), M4 (the ceiling).
- **(7) Rules.** F1 (the root's rule and a later-touched stale group), S1, S2, S3, M3. The deletions by
  KeePassDroid and KeePassDX 3.x are handled as the design describes: narrowings hold and grants are off.
  KeePassXC up to 2.7.12 builds an entry's history items without its custom data [M keepassxc §6], so
  restoring a revision there removes the rule. It then *disagrees*, so narrowings hold. Safe.
- **(8) Recycle bin markers.** As write-once keys they would inherit F1 and case 1 unchanged.

## 4. Three-way chains: KeePass, then a phone, then KeePass

| Chain | Outcome | Basis |
| --- | --- | --- |
| KeePass, then a KeePassDroid save, then a KeePass synchronize with the phone's file | dictionary intact; rules deleted in both copies at equal node times; grants off until Restore, every time | [M spike §6; M keepass §3]; the design's §11 |
| KeePass, then a KeePassDX 4.5.8 merge with an older phone copy, then KeePass | dictionary union; stale rules *behind*; positions: S3 | [M spike §6; R] |
| KeePass, then Keepass2Android "Yes, merge", then KeePass | complete | [M spike §6] |
| KeePass, then Keepass2Android "No, overwrite", or a KeePassDroid save over a changed file | one stale file whole: the design's case 3 | [R android §5.5] |
| KeePass, then a KeePassXC merge of an older copy (laptop), then a KeePassDX 4.5.8 "Merge from" on the phone with its own older copy open, then KeePass | **revocation, retirement and profile removal lost** before KeePass sees the file: the dictionary is stale on both sides, and the phone's root group, stamped later, is kept whole | each step [M here, M design] or [R `DatabaseKDBXMerger.kt:248-250`]; not run as a chain |
| KeePass, then a KeePassXC merge of an older copy, then KeePass File then Synchronize with the phone's file | **lost** in 4 of 5 cells (F1) | [M here] |
| KeePass, then a KeePassXC merge of an older copy, then any plain KeePass save of that file | reconciled, safe from then on | [A, from the design's reconcile] |

## 5. The design's survival claims against their basis

| Claim | Where | Basis given | Status |
| --- | --- | --- | --- |
| a KeePassXC merge that replaces the dictionary loses nothing (union 14 of 14) | §0, §5 | [M probe] | measured, for that one step; **refuted as a lasting property** (F1) |
| a KeePass 2.62 Synchronize is complete and its reconcile makes both replicas the union | §5, §3 | [M probe], 6 cells | measured on intact inputs; **refuted after a KeePassXC merge** (F1) |
| one synchronize in a KeePass with the plugin leaves both files reconciled | §3 | [R], not run | reconciled to the merge's survivor (F1); the event order itself is still unrun |
| KeePassDX 4.5.8 merges the mirror key by key | §5 | [A] | **refuted by reading** (`DatabaseKDBXMerger.kt:247-250`) |
| Keepass2Android merges and saves keep the mirror | §4, §5 | [A] | unmeasured |
| KeePassDX 3.2/3.3 and KeePassDroid delete the *root* group's custom data as they delete G's | §4 | [A] | unmeasured; harmless either way except through F1 |
| KeePass 2.47, KeePassDX 4.5.8, kdbxweb, pykeepass keep the mirror on save | §4 | [A]; G measured | unmeasured for the root |
| the file stays 4.0 | §8 | [M probe] on 4.0 inputs | **refuted** for 4.1 inputs saved by KeePassXC 2.7 or later (M1) |
| a stale rule under a newer time is recognized as *behind* | §0, §5 | [M spike], reasoning | holds for one line of edits; **refuted for unequal branches** (S1) |
| two copies that edited one rule are in force as their intersection | §10 case 7 | reasoning | **refuted** unless both stopped at one generation (S1) |
| a removal of a profile survives every union | §1 | reasoning | the key survives, but its effect can be outranked (S1) |
| a duplicate or an imported tree grants nothing | §2, §5, §6 | reasoning | **refuted** by the formula as written and by unruled duplicates (S2) |
| nothing a person does to one place widens what a client reaches | §6 | reasoning | **refuted** by Keep all (M3), import (S2) and moves (S3) |
| prunes remove only keys whose return does no harm | §3 | reasoning | true only with a correct clock on every pruning machine (M2) |
| KeePassXC, Keepass2Android, KeePassDX 4.5.8 and kdbxweb merges never stop grants | §11 | reasoning | not for a KeePassXC merge within one second, where E1 holds an unrecorded generation and disagrees |
| a contested key needs a hand edit or a forgery | §2 | reasoning | consistent with everything measured |
| the window merge, the reload merge, KeeShare and the remote sync keep the root's custom data as the command line does | §14.2 | to be measured | read: the window merge acts like the command line; the reload keeps the disk copy's state in both replicas; the remote sync never sends the local state |

## 6. What would have to be measured

Most important first. The first decides F1's reach beyond what I ran.

1. **F1 with the real programs**: a KeePassDX 4.5.8 save and "Merge from" of the KeePassXC result, a
   Keepass2Android merge of it, and KeePass 2.62's window doing File then Synchronize with a plugin
   handler on `FileSavingPre`. Also the save that finds the file changed, to confirm that the outer
   `FileSavingPre` runs before `PreSaveValidate`'s synchronize (the one path read as safe).
2. **KeePass's other four methods** over the KeePassXC result, both directions (only Synchronize ran).
3. **The KeePassXC window merge and the reload merge** with a stale disk copy, to confirm the read in §5.
4. **KeePassDX's location time** on an equal-time move (S3, step 4).
5. **The root group's custom data** through every Android save and merge (the design's [A]s).
6. **The full M1 chain**: a KeePassDX save, then a KeePassXC save, then a kdbxweb save, then KeePass
   opening the file.

## 7. What this means for the idea of timestamped JSON in an entry field

F1 is a problem of the carrier. A union survives only in something that every merge adds rather than
chooses. Custom data is chosen whole, by the root group's time or by a load's second. By the ground
reports, the one place every merge measured or read adds rather than chooses is an entry that only one
copy holds:

- KeePass 2.62, all five methods [M keepass §1d]
- KeePassXC [M keepassxc §5]
- Keepass2Android and KeePassDX [R android §1.5]
- kdbxweb, Strongbox, KeePassKit and keepass-rs [M, R others]

Fields added to one shared entry are chosen whole by the entry's time, and the losing copy's fields
survive only in history. At equal times they are lost: 12 of 24 survived in KeePass with
preprocessing [M keepass §1e]. One copy's field was in neither result once the other copy's history
was full [M keepassxc, `meta`]. So the idea's preprocessing step meets F1's kind of sequence too,
unless each record is an entry of its own rather than a field.
