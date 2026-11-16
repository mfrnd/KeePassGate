# Critique of `option-outside-file`: security, KeePass's own behavior, the operator, the cost

Subject: `option-outside-file.md` ("the authority stays in the file, everything that changes moves
to a sealed folder beside it"). Written 2026-11-15 against KeePassGate master `a42e38aa`, KeePass
2.62. This is an attack on the design, not a review of its prose. Where the design already lists a
weakness, it appears here only where it is understated.

## How statements are marked

- **[read]**: from source at a named place: the design document, a ground report (`ground-*.md` in
  this folder), the plugin in the product at `a42e38aa` (cited `file:line`), KeePass 2.62
  source, or the prototype `Store.cs` under the design's own scratch directory.
- **[measured elsewhere]**: a run recorded in a ground report or in the design's `out\` files, cited
  by its own label.
- **[assumed]**: reasoned, neither run nor read directly.

I ran no program with a window, started no KeePass, and built no new probe: the design's own 21
scenario checks and timing run (`out\scenarios.txt`, `out\timing.txt`) and the ground reports supply
the measurements; the rest is read and reasoning. I did confirm the KeePass 2.62 binary I read
against matches the worktree's (`KeePass.exe` SHA-256 `AFB89B74...C89C3F3`) [measured here].

## What the design gets right, stated first so the criticism is weighed against it

1. **The store leaves the API's reach entirely, and this is cleaner than any in-file design.** No
   route reads or writes custom data today (`ground-inventory` P15 [read]); the store is not a
   group, an entry, a field or an attachment, so no `createEntry`, `createField`,
   `updateGroup`, `stripAttachment` or flat sweep from the root can reach it, and the folder is not
   something a route can name [read, design §15; openapi operationId list has no move, copy or
   duplicate]. A prompt-injected agent holding `create` and `update` everywhere has nothing it can
   write that the store would read back. **E8 is met structurally**, which the in-node and in-entry
   options have to buy with explicit exclusions on every route.
2. **The worst in-file failure flips from fail-open to fail-closed.** Today a KeePassXC merge from a
   stale copy removes a revocation and silently readmits a revoked client (`ground-measured` §3.1,
   `ground-keepassxc` §9 [measured elsewhere]). Here the same merge can only remove the two
   write-once keys, which closes the gate (`503 undecided`), admitting nobody, rather than readmitting
   anyone [design §13, §18.1; the prototype's add-only join holds a revocation while any writer
   asserts it, `Store.cs:189-193` read].
3. **The signing keys stay where they are.** The authority private key, the host key and the gate key
   remain in the file, kdbx-protected, written nowhere else, exactly as today (`CA.md` §208, §222
   [read]). The new secret, the 32-byte store key, opens less than the database it sits in, and to
   hold it one must have opened the database [design §2]. So the task's question (b), where the
   authority private key shows up in search, auto-type, exports, reports, a phone or the clipboard,
   is answered: **unchanged**. The authority key is as exposed, and no more, than today.
4. **The join is fail-closed.** Two rule versions that do not know of each other refuse the node and
   everything under it; a profile removal wins; a revocation is held while any writer asserts it; an
   emptied record wins (`Store.cs:186-211` [read], `out\scenarios.txt` 21 of 21 [measured elsewhere]).
   A store forger (design §18.6) can widen a rule or re-add a profile name, but **cannot undo a
   revocation**, because a revocation is add-only in the join, so the revoked certificate stays
   refused. That backstop limits the forger to existing clients' reach and never to resurrection.

These are real gains. The problems below are why they are not enough on their own.

## The problems

### 1. Multi-gate over KeePass's own Synchronize is dead, and revocations stop propagating that way

Severity: **serious**.

Steps:

1. The common two-machine topology today: one `.kdbx` on a cloud drive (Dropbox, OneDrive), opened
   on a desktop and a laptop that both run the gate, kept in step by **File**, **Synchronize**.
   `DESIGN.md` §113 names two gate computers as supported [read].
2. Today a revocation, a new pairing and a profile name are custom-data keys that travel through that
   Synchronize, because a KeePass merge never removes a key (`ground-measured` §3.1, 70 of 70; P1,
   P2 [measured elsewhere]).
3. Under this design none of that is in the file. KeePass announces a synchronize to a plugin only by
   the save that ends it and hands the plugin the database alone, never the other file
   (`ground-inventory` P19; design §12 row, citing `ImportUtil.cs:344-371` [read]). So the plugin
   cannot carry the folder, and KeePass does not carry it.
4. On the laptop, the first open finds a synced `.kdbx` with **no `.kpg` folder beside it**: the gate
   answers `503 undecided`, "the store folder is not beside it", and **serves nobody** until the
   operator sets up a second, separate sync channel for the folder and points the machine at it
   (design §11, §12, §18.3 [read]).

Outcome: a topology that works today is a dead gate on the second machine until reconfigured, and
after reconfiguration a revocation made on one machine reaches the other only when the two folders
next meet, through a channel the operator had no reason to build and can forget. That is a
regression in the reliability of revocation (E3's purpose) for a supported case. The fix the design
names is real but costly: **require a shared or separately synced folder, and document that KeePass
Synchronize alone no longer suffices** — which also drags in problem 7 (a per-request stat on a
share).

### 2. A narrowing can fail to be in force, two ways, because the node is in the file and its rule is in the folder

Severity: **serious** (E5 and E6 are essential: a narrowing is never undone, and its loss is
detectable or refuses).

Two sequences, both widenings:

**2a. A new narrowed node arrives before its rule.** With two gate computers, the operator creates an
entry on machine A and puts a rule on it that is narrower than its group (a seal, or an empty mask
for a profile the group grants). The entry is in the `.kdbx`; the rule is in the folder. The two
travel on different channels with no ordering. The file channel can be ahead of the folder channel
(the folder sync lags, or is down). For that window, machine B holds the entry and not its rule, and
**the gate cannot tell a node whose rule has not arrived from a node that legitimately has no rule
and inherits** — inheritance is the normal case (`AclDocument.cs:110-120`, absent is silence
[read]). So B serves the entry under the wider inherited grant. This gap is **inherent to the
split**: fail-closing on every node the store does not mention would deny every ordinary inheriting
node, so it cannot be closed while rules live in a folder keyed by UUID and nodes live in the file
[assumed, from the inheritance rule]. The design concedes it (§18.4) and rates it mild; it is a new
fail-open window that does not exist today, where node and rule are one object in one file and arrive
together. It is not caller-exploitable (an agent cannot write rules), which caps it at the operator's
own multi-gate setup, but it is exactly the E5/E6 class.

**2b. A copy of a narrowed node loses the narrowing.** KeePass's Duplicate Entry and Duplicate Group
clone a node's custom data and give the copy a new UUID (`PwGroup.Duplicate` →
`CreateNewItemUuids`, KeePass 2.62 `PwGroup.cs:1437-1447` [read]). Today the rule is in that custom
data, so the copy carries it (`docs/access-control.md` "A copy of a node carries its rules" [read]).
Under this design the rule is keyed by the old UUID in the store, so the copy carries **nothing** and
is governed by where it lands (design §18.2 [read]). If the original narrowed an inherited grant, the
copy is **wider** than the original. An import with new identifiers duplicates the whole arriving
tree at the root, where it gets what the root grants (`ground-keepass` §2.4 [measured elsewhere]).
The gate cannot see a copy, and nothing on the KPG tab warns. The design's answer is a habit and a
sentence in `docs/` ("narrow at the group, grant nothing at the root"), which is not a fix: it asks
the operator never to rely on a per-node narrowing surviving a copy, which today it does.

Outcome: both are widenings the design accepts rather than closes. 2a cannot be closed within the
architecture; 2b could only be closed by re-homing a rule onto a duplicate, which the design says it
cannot detect.

### 3. Rollback protection is host-local and absent where it is most needed

Severity: **serious**.

The headline "the store checks itself" (design §1.6, §6) rests on two things that refuse: each
writer file records how far it saw the others, and a per-installation "seen" journal in
`%LOCALAPPDATA%\KeePassGate\seen\<id>.kpgs` records the highest head this machine loaded [read,
design §5.1, §6; `Store.cs:170-176`, `212`].

Steps to the hole:

1. The operator restores a whole backup of the folder to an earlier state, or migrates to a new
   Windows machine, or reinstalls the OS: there is **no seen journal** for this store on that machine.
2. The design serves a rolled-back store "as found" when there is no seen file (§18.5), because there
   is nothing to compare with (§12 row "Restore both on a new computer: served as found").
3. A revocation, a profile removal or a rule narrowing made after the backup is **silently absent**,
   and a client revoked since is readmitted.

Outcome: the mechanism that is supposed to catch a rollback is exactly the thing a restore or a
machine migration loses, so it protects the machine that did the writes and nobody else. This is the
same failure class as today (a restored old backup serves old state), now reached with a whole new
sealed-file-on-the-host subsystem that itself adds failure modes (a stale or corrupt seen file closes
the gate, §11 row "older than this computer saw"). The design spends complexity on rollback detection
and still concedes the rollback (§18.5, §18.6). The protection is worth less than its cost.

### 4. The "one writer per file" invariant breaks when KeePass's configuration is copied

Severity: **serious**.

The writer name is 8 random bytes kept as `KPG.Writer` in KeePass's own configuration, beside
`KPG.Port` (design §5.1; `KeePassSettings.cs:42` shows the plugin already keeps `KPG.Port` there
[read]). KeePass's configuration is per installation and lives in `%APPDATA%\KeePass`
(`AppConfigSerializer.cs:143-144` [read]). Operators copy that configuration routinely: a roaming
profile, a VM template, a disk image for several machines, a config backup restored onto a new
machine. This is a Windows-only, often enterprise, plugin, where imaging is normal [assumed].

Steps:

1. Two installations are set up from one image, or one config is copied to another machine. Both hold
   the same `KPG.Writer`, say `W`.
2. Both write `W.kpgw`. Through the shared folder each overwrites the other's file wholesale. The
   design's own invariant "No file ever has two writers, so a file synchronizer never has a conflict
   to settle" (§1.4) is false from the start.
3. The design's detection ("this writer's own file is not the file it wrote", §11) closes the gate on
   both machines until the operator presses "Take a new name" on each — and that detection is design
   text, not in the prototype, whose journal stores only a head and not the promised digest
   (`Store.cs:113` [read]), so whether it fires on a file that merely advanced is unmeasured.

Outcome: the correctness argument rests on an invariant that the design's own storage of the writer
name (in copyable KeePass config) makes easy to break, and the failure is at best a closed gate on
every affected machine and at worst, in a write race under sync, a lost write rather than a clean
refusal. A fix would make the writer name unique per machine by construction (derive it from a
machine identifier the config copy would not carry) and detect collision before any write.

### 5. The gate now closes on a wide range of ordinary file-management events

Severity: **serious**.

Today `503 undecided` has one cause: more than one unretired authority. Under this design section 11
lists nine states that answer `503 undecided`, most triggered by routine events: the database copied
or moved without its folder; the folder on a cloud drive held as a placeholder that cannot be
fetched; a writer file older than another saw it; a conflict copy a synchronizer made; a hand edit;
`store.json` deleted; a disk full on a write; a share that is away (design §11, §12, §18.8 [read]).

Outcome: "availability now depends on a folder" (§18.8), and the gate's job is to serve automation
agents reliably. Turning "copy the database to a USB stick", "open it from a cloud folder whose files
are on-demand", or "restore last night's backup of the file only" into a total outage for every
client is a real availability regression against a design where the data is in the file and travels
with it. The design is honest about this but understates the breadth relative to today's single
cause.

### 6. The folder is a new metadata disclosure the design does not list

Severity: **serious** (disclosure to parties outside the conceded threat model), tending to minor if
the only observers are local.

Today an observer who sees the `.kdbx` on a synced drive, a phone backup or a stolen laptop sees one
opaque encrypted file and cannot tell KeePassGate is in use. Under this design, beside it sits:

- a folder named for the database, `Passwords.kdbx.kpg` (design §5.1 [read]);
- `store.json` **in the clear**, whose `note` field says "KeePassGate data for a KeePass database"
  (§5.1 [read]);
- one `<16 hex>.kpgw` per installation that writes, so the **number of gate computers** is visible;
- file sizes that grow with the number of rules, records and names, so the **scale of the policy** is
  visible (§5.3: 200 rules, 30 records and more is ~82 KB [measured elsewhere]).

The design synchronizers and phones that it discusses carrying the folder (§12 "A phone", "A
synchronized folder") deliver all of this to a cloud provider or a phone's own backup. The content is
sealed, but the **existence, the product, the machine count and the rough policy size are not**, and
today they are inside the encrypted file. This disclosure is not in §18 "what it does not solve". It
should be: at a minimum `store.json` should not name the product, and the folder should not derive
its name from the database.

### 7. The recommended multi-machine configuration puts a filesystem stat on a share in every request

Severity: **minor**, tending to serious on a flaky share.

The gate asks the folder for the names, lengths and times of its `.kpgw` files on every request
(50 microseconds on local NTFS, design §8, `out\timing.txt` [measured elsewhere]). The design's own
answer to multi-gate (problem 1) is to put the store on a folder both machines reach, which is a
network share. The design concedes "50 microseconds will not hold" on a share (§21.7, unmeasured).

Outcome: every API response then carries a stat on a network share in its path, which adds
network-dependent latency and, on a share that is slow or away, can block the connection thread. The
gate shares KeePass's thread for reads; the per-request look is said to need no turn on that thread,
but a blocking stat still stalls the connection that is serving an agent. The design marks it
to-measure; until measured, the recommended configuration's request latency is unknown.

### 8. A new on-disk cipher and a store key reverse a settled decision and create a persistent forge capability

Severity: **minor** (the design concedes it; the signing keys are untouched).

The design seals the store with AES-256-GCM under a key the plugin manages (design §5.2,
`Store.cs:69-104` [read]), reversing "Nothing here is ever written to the disk" for records and rules
(`CA.md` §222) and the spirit of E10 (keys stay off the disk, secrets guarded by the kdbx encryption
alone). The signing keys are not under it, so E10's security purpose holds; what changes is that a
32-byte store key, once extracted by anyone who opens the database once, is a **persistent capability
to forge the store by writing the folder**, without re-opening the database or holding its current
master key (design §18.6). Today forging a record or a rule means writing a valid encrypted `.kdbx`,
which needs the master key. The forger still cannot undo a revocation (point 4 of the credits above),
so the damage is bounded to widening existing clients' reach until Recreate. The design rates this
"worse than today" and is right to.

### 9. Correctness rests on real-synchronizer behavior that was never measured

Severity: **minor** (a viability caveat rather than a defect found).

The prototype's `Sync` is a hand-rolled copy of the newer of each writer file between two folders
(`Store.cs:281-294` [read]), not a real synchronizer. The whole no-conflict argument is "each file
has one writer, so a synchronizer has nothing to merge", cited as `[A]` in the design from
Syncthing's and Dropbox's documented rule (§12, §21.1). But problem 4 shows the one-writer invariant
is breakable, and `ground-others` records that Syncthing, Dropbox and OneDrive make conflict copies,
rename files and propagate deletions on their own schedule [read]. If a synchronizer ever makes a
conflict copy of a writer file, propagates a deletion of one, or delivers a half-written file, the
"correct merge" and "a replace is one step" claims (§9.1) are untested against it. The design lists
this first among its own measurements to make (§21.1); until then the load-bearing assumption is
unvalidated.

### 10. The Create/Recreate path becomes a two-phase commit across the file and the folder, on the leaked-key recovery operation

Severity: **minor**.

Create and Recreate must save the database (new authority, new store key) and then write the store,
and "or a discarded save would leave a store nobody holds the key to" (design §9.4 [read]). This runs
from the settings dialog's `FormClosed` (§21.4, unmeasured). If the save lands and the store write
then fails (disk full, folder locked, share away), the database holds a new store key and no store:
`503 undecided`, "store missing", recoverable only by creating the folder (§11 row). Recreate is the
documented recovery from a leaked unlocked-database copy (`PAIRING.md` §301 [read]) and from a leaked
authority key (`CA.md` §380 [read]); making the most safety-critical operation a cross-artifact
commit that can half-complete is a robustness cost on exactly the path an operator reaches in an
emergency.

## The cost in this code base (task e)

- **Rewritten or new**: a whole store subsystem (`SidecarStore`, `StoreImage`, `WriterFile`,
  `StoreSeal`, `StoreSeen`, `StoreLocation`), `RoutedStore` over the two in-file families and the
  image, and changes to `DatabaseWriter`, `OpenDatabaseTargets` (per-request look, nine new undecided
  states), the grant editor and `NodeDialog`, `ProfileNames`, `AclDocument` (key removed),
  `KdbxNodes`, `MainWindowDatabases` (Save As copies the folder), `KeePassSettings`, and a fourth KPG
  page (design §10 [read]). This replaces a simple, measured model — data in `PwDatabase.CustomData`,
  merged by KeePass key by key — with a bespoke sealed-file store that has its own crypto, a mutex, a
  temp-file replace, a cross-folder sync assumption and a nine-state failure taxonomy.
- **Measured facts and tests that fall away**: `SnapshotHazardTests` and the rule cells of
  `MergeInRealDatabaseTests`; the `GroupForm`/`DatabaseOperationsForm` trap stops applying to the
  plugin's own data (a genuine simplification, since only the two write-once keys remain in custom
  data, and those are written from a modal dialog `DatabaseOperationsForm` cannot be open under —
  `UIUtil.ShowDialogAndDestroy` is modal on the main window [read]). `CustomDataMergeRuleTests` stays,
  because the two keys still rest on it.
- **New hazards needing new tests**: the join's cases, the seal's refusals, the rollback checks, the
  real-synchronizer behavior, `File.Replace` on real storage (§21.6), the FormClosed save at
  Create/Recreate, and the writer-collision detection the prototype does not yet implement.
- **0.1.0 carry-over**: a migration exists (§19), but a non-migrated copy merged with a migrated one
  brings the old in-file keys back, and the design falls back to "an older stored shape is not read or
  migrated; the alternative is Recreate". The project's decision records 0.1.0 as released but unused
  (memory: release-step-plan, 2026-11-08), so in practice this is close to a non-issue, but
  if any real 0.1.0 database exists, carrying it over is Recreate (re-pair every client, re-author
  every rule).

## Verdict

**Viable with changes**, not in its current form. From the pure caller-in-the-threat-model lens the
design is strong: the store is unreachable by every route, the join is fail-closed, and a revocation
cannot be forged away. Its serious problems are not caller-exploitable; they are availability,
propagation and operator-integrity:

- it breaks the common multi-gate-over-KeePass-Synchronize topology and the propagation of
  revocations through it (problem 1);
- it opens two narrowing-loss windows inherent to splitting the node from its rule across the file and
  the folder (problem 2), one of which cannot be closed within the architecture;
- its rollback detection is host-local and absent on the restore and migration cases where rollback
  matters (problem 3);
- its core invariant breaks when KeePass config is copied (problem 4);
- it closes the whole gate on routine file events (problem 5);
- it discloses the existence and scale of the deployment beside the database (problem 6).

None is strictly fatal: the single-gate deployment is sound, and each multi-gate and availability
failure has a named change (require a shared or synced folder and document the loss of
KeePass-Synchronize parity; make the writer name unique by construction; strip the product name and
the database name from the folder; accept and document the on-disk cipher and the metadata it leaves).
But problem 2a is uncloseable while rules live in the folder, so **if parity with today's multi-gate
behavior is treated as an essential requirement, the verdict tips to not viable for that case**. The
design should either narrow its own claim to single-gate (plus a shared-folder multi-gate with the
new-node window documented) or keep rules in the file and move only the add-only, API-unreachable data
out.

## Claims the design rests on that are not yet measured

Carried from the design's §21 and from the reasoning above, so none hides:

- Real synchronizers (Syncthing, Dropbox, OneDrive placeholders, Nextcloud) over the folder: conflict
  copies, deletions, half-written files, placeholder reads, arrival latency (design §21.1).
- `File.Replace` on .NET Framework 4.8 over NTFS, exFAT, FAT32 and SMB, under Defender and a
  synchronizer's reader, and the retry window needed (§21.6).
- The per-request folder look on a network share (§21.7), which the recommended multi-gate
  configuration depends on.
- The writer-collision detection and the "my own file advanced" case, which the prototype does not
  implement (its journal stores a head, not the promised digest, `Store.cs:113`).
- The FormClosed save-and-verify at Create and Recreate in a real KeePass, with triggers and a
  changed-on-disk file (§21.4).
- Writing the two in-file keys with no per-key time through KeePass's internal setter, and kdbxweb
  opening the resulting 4.0 file (§21.10).
- What a real synchronizer and a phone backup expose of the clear `store.json` and the folder name to
  a cloud account (problem 6; not in the design's own list).
