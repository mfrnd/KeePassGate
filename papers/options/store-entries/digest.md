# Digest: `store-entries`

Sources: the design, critiques S (survival) and F (security and fit), `measured-android.md` (MA);
K2A is Keepass2Android. **M** measured (whose probe), **R** read, **A** assumed. No critic's change
was run, so verdicts are for the design as written. The critics' raw output matches their text.

## 1. What it is

The database-level data become ordinary entries in one group of the plugin's own, found by a fixed
UUID. Each record is one entry, its UUID derived from kind and subject, its JSON in a custom string.
Records are written once and never changed; a later decision is another record (revocation,
retirement, profile removal, numbered renewal answer). The gate folds all records on every request
and writes nothing at open. Granting records count only in place, refusing ones anywhere, and a
deletion record naming a record refuses it.

**Covers the database-level data only. Not the rules.**

## 2. The hard requirement

| Program | Its operations | Verdict |
| --- | --- | --- |
| KeePass 2.62 | Save keeps all [M ground X7, design]; four identifier-matching merges unite both ways; Create new IDs adds a group that grants nothing [M design] | **fails open**: records survive, but P2 (item 6) widens through any merge; also items 2, 8 |
| KeePassXC 2.4.2 to 2.8.0 | Save keeps strings, flags, KDBX 4.0; merge unites but carries no deletion record, so deleted records return inert beside their refusal [M design]; its own delete writes a record [M S] | **fails open** in two steps with a recordless deleter (item 3) [M S] |
| Keepass2Android 1.15-r6 | Every carrier kept through save (3.1 output included), edits, rename, delete, move, merge, and back into KeePass; merge unites, joins deletion records, keeps moves [M MA] | **survives**; overwrite is item 1 [R] |
| KeePassDroid 2.7 | Save, edits, delete kept every carrier; no group edit, move or merge exists [M MA] | **survives**; saves over a changed file overwrite (item 1) [R] |
| KeePassDX 4.5.8 | Save, edits, rename, delete, moves kept every carrier [M MA]. Merge: arriving deletions applied, no record kept; at equal times an entry follows the arriving copy's group [M MA] | **fails open**: items 4, 5, 7 [R chains, KeePassDX steps M MA] |
| KeePassDX 3.2.0 | Not run; whether its merge drops an entry whose group is missing is open | **not measured** |
| The rules | Not covered; left on nodes, KeePassDroid deletes them at every save [M MA] | **fails open** unless combined with a surviving rules option |

**MA confirms** what was read or assumed: the phone programs keep custom strings, flags and the
group; K2A's 3.1 output too (design: A); K2A merges in an entry only the arriving copy holds
(design: unmeasured); KeePassDX keeps no arriving deletion record and moves an entry to the
arriving copy's group at equal times (S P5, F S2; an ordinary move only); K2A and KeePassDroid
recycle without moving the modification time; K2A drops a deletion record whose entry is newer.

**MA contradicts** the design's "KeePassDX writes 4.1" (4.0 stayed 4.0; harmless) and S P4's
KeePassDroid group deletion (it offers only Open on a group).

**Open**: a group made in both copies on Android; an authority-size protected value (29 characters
measured); phones skipping the group in search and autofill [R].

## 3. Sequences ending wider or with a revocation undone

1. **Shared, a file written over a newer one** (K2A "No, overwrite", KeePassDroid, KeePassDX within
   10 s, sync conflicts): one act [R; M spike]. Nothing here helps; a KeePass still holding the
   database offers Synchronize [R].
2. **A hostile `.kdbx` merged with Synchronize**, retiring the open authority and bringing its own:
   the attacker's authority serves [A, design; same today]. Closed by MACed records (F) or a
   per-database store identifier. Not run.
3. **KeePassXC merges the revoking copy into a stale copy, then a pykeepass program deletes the
   revocation entry**: admitted [M S P1 sequence A]. KeePass's non-Synchronize merges leave the
   same single witness [M design]. Closed by S P1's second witness, the `KPG.Revoked` key; residue:
   a fast KDF or 2.4.1 delivers no key [M spike]. Not run.
4. **A client record binned in KeePass, K2A or KeePassDroid, then KeePassDX merges a stale copy**:
   the record moves back and admits [R F; move M MA]. Only the tab revokes, so this undoes an act,
   not a revocation. Closed by the plugin revoking binned records, if it writes between (F). Not
   run.
5. **The store deleted for good, KeePassDX merges that copy, then a merge with an older copy**: the
   retired authority serves, revoked clients admitted [R S P1 sequence C; entry step M MA]. Closed
   by S P1's keys, which KeePassDX keeps [M spike]. Not run.
6. **P2**: copy B removes a profile and re-adds it (page or pairing); copy A, unmerged, removes it;
   both removals are `removed <name>/1`; any merge restores the name [union M design; the fold's
   rule]. Closed by act identities with a random part and additions listing the removals they saw
   (S). Not run.
7. **S P1 sequence B**: KeePassDX copy X merges the revoking copy; KeePassXC merges it into stale
   Y; Y's revocation is deleted with a record; KeePassDX merges Y into X: admitted [R S; steps M
   MA]. Closed by S P1. Not run.
8. **Delete the revocation, empty the bin, clear deletion records, merge a stale copy** [R design].
   Closed by S P1's key [M spike]. Not run.
9. **The prune reads "authority gone" as "does not count"**, deletes revocations, then item 3 [A].
   Closed by pruning only on expiry or retirement (S P8), a text change.
10. **KeeWeb**: kdbxweb drops a store remade under the reused UUID; with the revocation in a copy
    only as an entry, all 8 KeePassLib merges admit the revoked client [M S P3]. Closed by a group
    UUID per store, with P1. Not run.
11. **Shared, a narrowing by a move undone by a merge**: no rules here; item 4 is its instance.

Conceded, as today: whoever holds the master key can edit a record's profiles. F's claim that MACs
stop a resurrected record does not hold for a revocation without Recreate: its MAC is valid (my
reading, A).

## 4. Refusals in ordinary use

All are answered on the KPG tab in KeePass.

- Store group binned: nothing served until put back. Deleted for good: a new authority, and
  everyone pairs again.
- File then Import (Create new IDs preselected): a duplicate group to delete. Delete Duplicate
  Entries may delete the original [M ground X10]: new authority, everyone pairs again.
- After a KeePassDX merge, records an older copy misplaced move back there [M MA]: clients refused
  until "put it back".
- A stray edit of the authority's `KPG.Data`: nothing served until history is restored.
- Merged lists over 32 names: every client reaches nothing until names are removed.
- A planted refusal (a Bitwarden import carried both strings [M F]): nothing served, then the next
  write prunes the authority for good. F's narrow change makes it a listing.
- Several unretired authorities: `503 undecided`, as today.
- kdbxweb dropping a remade store, gokeepasslib corrupting the protected authority [R]: pair again.

## 5. Costs

- **Seen**: a `KeePassGate` group of `KPG …` entries with two extra fields, in every program.
  KeePass keeps them out of search, auto-type, password reports and expiry warnings [M ground X11;
  R], not out of Show All, the large and last-modified reports or `{REF:}` [R F]. Refusals pile
  up visibly, and deleting them is items 3, 5, 7, 8.
- **Authority key**: from custom data no ordinary screen shows to a protected string one copy,
  print, KDB export or KeePassXC HTML export away, in clear [R F; M ground-keepassxc]. Gain: KeePass
  encrypts it in memory [R].
- **Growth**: an entry per record, act and answer; KeePassXC and KeePassDX merges return what the
  plugin deletes. KDBX 4.0 [M design].
- **Per request**: a walk over every entry and deletion record: 1.9 ms at 10,054 entries [M design,
  console]; KeePass's thread unmeasured.
- **Code**: `IKeyValueStore`, `CustomDataStore`, `ProfileNames.Merge` and the Maintenance refusal
  go; a record seam and an API guard arrive (the store absent like the bin, `KPG.` fields
  uncreatable); 42 source files [R F] and 49 test files of about 26,000 lines [M F] change.
- **Editor and tabs**: grant editor untouched; KPG tab rebuilt over records. A 0.1.0 database has
  no authority.

## 6. Reverses

The authority's place in `CustomData`, not the principle; the whole "database's own `CustomData`"
bullet (S P1 keeps two key families as witnesses); "the authority takes the file to KDBX 4.1"; key
by key replay and the profile difference; the trap section's `DatabaseOperationsForm` paragraph;
`CA.md` §204 and §208; with F's strong change, "nothing in a record is authenticated beyond the
file".

## 7. Verdict

For its data this option uses the one carrier every measured program keeps, and MA now proves it on
the Android programs in scope for every operation and back into KeePass. No save or merge
alone loses a record. It fails on deletion: its second witness, the deletion record, travels only
through KeePass Synchronize, K2A and kdbxweb, so a KeePassXC or KeePassDX merge plus one hand
deletion undoes a revocation (measured for KeePassXC). P2, the reused group UUID, the planted
refusal and the key's exposure have named changes, none run. Without the rules it cannot meet the
hard requirement alone. Measure first: the changed design over items 3 to 7 and 10, with a
fast-KDF KeePassXC run; KeePassDX 4.5.8 merging stale copies over a binned record and a deleted
store; KeePassDX 3.2.0's merge; the pass on KeePass's thread at 50,000 entries.
