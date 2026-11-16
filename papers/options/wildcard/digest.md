# Digest: option `wildcard`, one sealed snapshot of everything, joined at read

Checked 2026-11-15 against `option-wildcard.md` (design), its two critiques (survival, sec-fit),
`measured-android.md` (the Android run) and `ground-inventory.md`. "Wildcard" means "anything
else", not the refused `kpg://any`. M measured, R read (source or the design's own rule), A
assumed; whose run in brackets.

## 1. What it is

All KPG data is one JSON document, a snapshot, in a protected custom string `KPG.Store` of one
entry in a group `KeePassGate` with a fixed identifier, sealed by an HMAC under a key kept inside
it. A snapshot is never changed: each plugin write adds one holding the whole new state and deletes
those it replaces. After a merge the gate serves the join of the survivors: unions for records,
revocations and retirements, the higher number for profile names and standing answers, and what
every version allows for one node's rule. Revocations and retirements are also written as deletion
records ("witnesses"); a snapshot lost without account makes the store `behind` (`503 undecided`),
or `gone` (no gate) when none is left. Nothing is written on governed nodes, and the file is KDBX
4.0. **Covers all KPG data, rules included.**

## 2. The hard requirement

The Android run settles the design's Android [R] cells: fixed `KPG` identifiers, a dedicated group
and its flags, protected JSON custom strings and deletion records naming nothing came back byte for
byte through every operation each phone program offers, and back into KeePass [M]. Not run on a
phone: a real snapshot or seal, or a value above 2,861 characters (a snapshot is 5 to 37 KB).
Today's rule killers (KeePassDroid, KeePassDX 3.x) find no rule on a node to delete.

| Program | What happens | Verdict |
| --- | --- | --- |
| KeePass 2.62 | Save, bin, Synchronize and three other methods: one joined state both ways, 40 cells [M design]. Create new IDs: undecided [M design]. Duplicate: the copy has no rule, where today it carries one [R sec-fit]. The join and "Use what is here" widen (section 3) | fails open |
| KeePassXC 2.4.2 to 2.8.0 | Save keeps a 1 MiB seal; `rm`, `mv` in force; merges in force, 40 cells [M design]. No merge copies a deletion record, so no witness reaches the target [M design, survival] | fails open |
| Keepass2Android 1.15-r6 | Save at every KDF, including KDBX 3.1 (settling the design's [A] for these carriers), edits, move: kept. Merge: one-sided entries arrive, records united and applied. Back: exact [M]. Delete asks "Delete permanently? Press No to recycle"; Yes outside the bin not run | survives; permanent delete fails closed |
| KeePassDroid 2.7 | Save, edit, delete kept every carrier; no move, group edit or merge [M]; overwrites a changed file unasked [R] | survives; permanent delete fails closed |
| KeePassDX 4.5.8 | Save, edits, emptying the bin, moves: kept. "Merge from" adds arriving entries, applies arriving deletion records and keeps none [M]. 3.x not run | fails open |

**Contradictions.** The run contradicts no Android statement of design or critics; it measures
what survival 2.2 read of KeePassDX. The critics refute two design rows: Delete Empty Groups is not
harmless while a snapshot sits outside its group [M survival e1], and the Delete Duplicate Entries
row cites a run showing otherwise [sec-fit 3.8].

## 3. Sequences ending wider, shortest first

No change below was run.

1. **Copy** of a ruled node (Duplicate Group): new identifiers, no rule [R sec-fit 2.3]. Change:
   copy hint `KPG.Of` in node custom data.
2. **Shared, stale whole file** over a newer one (K2A "No, overwrite", KeePassDX within 10 s,
   KeePassDroid always, a backup): old store in force [M spike, R]. Only a later merge repairs it.
3. **Shared, a move undone by a merge**: KeePassDX puts the entry back at equal times [M Android
   run]. Rules key on identifiers; nothing.
4. **OK that never lands**: the writer refuses the editor's apply after its dialog closed [R
   sec-fit 2.4, A timing]. Change: overlay in force from OK.
5. **Profile join**: A takes `writer` off (2), B off and on (3), any merge: on [R survival 2.3].
   Change: per-name versions, concurrent ones resolving to off.
6. **Unrecorded deletion** (pykeepass) of the newest snapshot beside an older one: revoked client
   admitted [M design 14.3].
7. **e3**: revoke; KeePassXC or OverwriteIfNewer merge into a stale copy; newest deleted for good;
   Use what is here joins the old one: revoked client admitted [M survival to `behind`, R the
   button]. Change: revocation and retirement entries; the button never re-admits.
8. **KeeWeb e4**: snapshot binned; Delete Empty Groups records the store group; a kdbxweb merge
   drops the other copy's snapshot unseen: profile back, rule reverted, in force [M survival].
   Change: random group identifier, anchor entry, drop such records.
9. **Foreign store**: a client's crafted file imported (Create new IDs, the default); Use what is
   here joins copies under any key [M design state, R sec-fit 2.1]. Change: only this store's key.
10. **KeePassDX**: a merge leaves the phone S2 and no witness; a desktop deletes S2 for good; the
    next merge removes it, keeping no record: `none`; an older file brings S1 back; KeePass opens
    the phone's file: revoked client admitted [links M, chain A]. Change: revocation entries.
11. **Two identical heads, one deleted**, then Use what is here: revoked client back, `writer` on
    [M design set C, R sec-fit 2.2]. Change: version witnesses, numbered titles.
12. **Self-import, Delete Duplicate Entries, Use what is here**: copies unruled [M sec-fit P1,
    A for user entries].
13. Eight rule changes against one drop the one; a fork beyond `over`'s 32 [R design 14.6, 14.7].

Aggravating: a node under a witness's identifier removes it [M sec-fit P2]; Database Maintenance
clears witnesses; the store key outlives Recreate; a 0.1.0 plugin serves the old keys [R].

## 4. Refusals in ordinary use

- A permanent delete of the store in any program: `gone` in every synced copy; Create in KeePass,
  every client pairs again, even with a readable snapshot present. An older one left: `behind`.
- An edit of `KPG.Store` or `KPG.Seal`: `behind` or `gone`, winning later merges by its time.
- A copy of the store entry or a Create new IDs import: `undecided` until Remove copies, which after
  Delete Duplicate Entries deletes the only snapshot.
- Two stores: Keep; one side re-pairs.
- One rule changed in two copies: what all allow, until one OK in the editor.
- A deleted ruled node a merge keeps: `gone`, refused below, rule text lost.
- Over 32 names after a join: nothing granted.
- An entry dialog on a snapshot, or Edit Entries anywhere: `503 busy` to pairing and renewal.
- At the unnamed document ceiling, every write.

## 5. Costs

- **Seen**: a root group with `KeePassGate store` (twins after merges), a 5 to 40 KB hidden field.
  Phone search skips the group, not a stray snapshot [R]; phone views of the field not measured.
- **Authority key**: shown nowhere today; here in KeePass's entry dialog, print and KDB 1.x export,
  KeePassXC's HTML export, and the history item any edit makes [R sec-fit 3.1; edit M on every
  phone]. Without a record it admits nobody.
- **Growth**: rules, names and `gone` versions never dropped; about 100 bytes of deletion record
  per write, revocation and retirement, never aged.
- **Per request**: 0.005 to 0.19 ms, parse only on change; whole pass up to 30 ms at 50,000 entries
  [M design, console]. An import shows no progress window, so that trigger never fires [R sec-fit].
- **Classes**: `CustomDataStore`, the reconcilers, `ProfileNames.Merge` and the `m_sdCustomData`
  reflection go; a store reader arrives; the stores, `PendingDatabase`, `DatabaseWriter`,
  `AclResolver`, `AuthorityPanel` and `ApprovalBook` change.
- **Editor and tabs**: the editor buffers and applies after OK; the tab gains five states and three
  buttons. E8 becomes vault code, and refusing `KPG.` field names changes `openapi.yaml`.

## 6. Settled decisions reversed

The authority's place in `CustomData`; "the database's own `CustomData`" with its add-only keys;
KDBX 4.1; "the grant editor writes into the dialog's own copy"; "replayed key by key" and both
reconcilers; grants under a node `KPG` key; the trap section and the merge fact stop bearing load;
E8's structural basis.

## 7. Verdict

Its carrier is the best measured: an entry written once survives every save and arrives in every
merge of every program, phones included, and no other program can change one rule. As written it
fails the hard requirement. KeePassXC, three KeePass methods and, now measured, KeePassDX strip the
witness; one accidental permanent delete, which identical twins invite, costs every client or,
through its remedy button or a KeePassDX merge, re-admits a revoked one; the profile join widens.
Every fix is named, none run. Measure first: the probe's snapshots through the three phone programs
(seal at 5 KB to 1 MiB, entry views); K2A and KeePassDX merges of sets U, C and H with a hand
deletion, and KeePassDX "Merge data"; the critics' changes rerun against e1 to e4 and the KeePassDX
chain; the join's laws with prunes.
