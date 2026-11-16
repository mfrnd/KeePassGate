# Option `hardened-custom-data`: the baseline, stay in custom data and harden it

Written 2026-11-15 against KeePassGate master `a42e38aa`. This is the comparison the other options
are measured against: the data stays where it is, in `PwDatabase.CustomData` and in the `KPG` key of
each ruled group and entry, and everything that can be added there is added. It is one design, the
strongest form of this option that still fails closed, followed by what it can never fix.

## How statements are marked

- **[M probe]** measured for this report, 2026-11-15: a console probe over KeePassLib 2.62 (loaded
  from `.keepass\2.62\KeePass.exe`, no window) and `keepassxc-cli` 2.4.2, 2.6.6,
  2.7.12 and 2.8.0 (the archives the `keepassxc` ground report verified against the spike's pins).
  Section 13 has what was run and where the output is.
- **[M spike]** recorded as run on `spikes/revocation-merge`, `RESULTS.md` section named.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M measured]**: measured in the ground report
  of that key.
- **[R ...]** read from source, in the ground report named, at the tag that report names.
- **[A]** assumed: reasoned, not run and not read. Every [A] that the design rests on is in
  section 14.

Requirement numbers (E1 to E14, A1 to A10, O1 to O7, P1 to P27) are the `inventory` ground report's.

## 0. The design on one page

1. **Two replicas of every database-level key.** Each `KPG.` key is held in `PwDatabase.CustomData`
   (the *dictionary*) and, under the same name with the same value, in the root group's
   `CustomData` (the *mirror*). The plugin reads the **union** of the two and writes both. The two
   places fail differently: a KeePassXC merge replaces the dictionary and leaves the target's root
   group alone [M probe]; KeePassDroid and KeePassDX 3.2/3.3 delete group custom data and keep the
   dictionary [M spike §6]; a KeePass merge never removes a dictionary key [M spike §1] and takes
   the root group whole from one copy [M probe].
2. **Every key is written once and never changed**, with a value that is never empty and no
   modification time of its own. What changes today (the profile list, a standing answer to a
   renewal, a rule) is spelled as a new key each time, so a union of two copies is always a correct
   merge and no program's choice between two values of one key matters any more.
3. **Rules stay on their node**, under `KPG`, edited by the same grant editor, and each rule now
   carries its own generation number. Each version is also recorded in a **rule ledger** in the two
   replicas, and a removal is recorded as a **rule tombstone**. Reading a node compares its rule
   with the ledger. Equal: in force. Provably behind the record: the recorded rule is in force.
   Anything else: **every witness must allow**, so the node's rule and the recorded rule can each
   only narrow, and a grant counts only when both hold it.
4. **Repair rides on saves, never on opening.** The two replicas are made equal, and nodes that are
   provably behind are put right, inside the plugin's own writes and at `FileSavingPre` of every
   save, the way owed standing answers already ride every save. Opening and reading change nothing.
5. **Nothing new refuses the whole database.** A disagreement costs the grants at that node and
   below until the operator has looked, on a fourth page of the KPG tab. Section 11 has what that
   costs, and why a refusal of the whole database after another program wrote the file cannot be
   built here.

What this buys against the three measured failures the task names:

| Measured failure | With this design | Basis |
| --- | --- | --- |
| KeePassXC merge from 2.4.2 replaces the dictionary entire | everything the target held is still in its root mirror, so the union is complete: 14 of 14 keys in 8 of 8 slow-KDF merges, both directions, four releases | [M probe] |
| KeePassDroid deletes every rule, KeePassDX 3.2/3.3 every group's, no time moved | the ledger still records each rule, so a narrowing or a seal still holds; the grants on those nodes are off until the operator restores them | [M spike §6] for the deletion; the ledger surviving is [M spike §6] (all eleven dictionary keys kept in 27 saves) |
| a stale rule under a newer time (KeePassDX merge, KeePassXC group merge, kdbxweb) | the node's rule equals a recorded version below the newest, which proves it stale: the newest recorded rule is in force at once | [M spike §5, §6], [M others] for the failure; [M probe] for the ledger arriving in a KeePassXC slow merge |

What it does not buy is in section 10. In short: a merge that carries no custom data at all, two
different foreign programs in a row with no KeePass save between them, and a whole stale file.

## 1. Where each datum lives

All key names below are in both replicas. `<fp>` is 64 lowercase hex digits (SHA-256 of a
certificate), `<uuid>` is the 32 hex digits of a node's identifier.

| # | Datum | Key | Value | Example |
| --- | --- | --- | --- | --- |
| 1 | authority | `KPG.Authority.<fp>` | the `AuthorityDocument`, unchanged: one flat JSON object with the authority PKCS#12, the host PKCS#12 and the gate key | `KPG.Authority.3b7e…` = `{"v":"1","ca":"MIIG…","host":"MIIG…","sniKey":"q83v…"}` |
| 2 | retirement | `KPG.RetiredAuthority.<fp>` | `retired` | unchanged |
| 3 | admission record | `KPG.Client.<fp>` | the record, unchanged format; **written once, never emptied** | `KPG.Client.9f02…` = the six lines of `CA.md` §223 |
| 4 | revocation | `KPG.Revoked.<fp>` | `<notAfter> <authority fp>`, with `-` for a part that is not known; **never empty** | `2027-03-01T00:00:00Z 3b7e…`, or `- -` for a record that would not read |
| 5 | standing answer to a renewal | `KPG.RenewalAnswer.<fp>.<n>.<word>` | `1` | `KPG.RenewalAnswer.9f02….1.forever`, `….2.lifted`, `….3.until-20270101T000000Z` |
| 6 | profile list | `KPG.Profile.<name>.in.<g>` and `KPG.Profile.<name>.out.<g>` | `1` | `KPG.Profile.reader.in.1`, `KPG.Profile.writer.out.1`, `KPG.Profile.writer.in.2` |
| 7a | rule on a node | the node's own `CustomData`, key `KPG` | the `AclDocument`, with one new reserved member `#` holding the rule's generation | `{"#":"2","default":"deny","reader":["read","list"]}` |
| 7b | rule ledger | `KPG.Rule.<uuid>.<gen>.<d>` | the rule text exactly as the node holds it, generation included | `KPG.Rule.134b…ace.2.5c1e9a07d2b44f10` = `{"#":"2","default":"deny","reader":["read","list"]}` |
| 7c | rule tombstone | `KPG.Rule.<uuid>.<gen>.none` | `none` | `KPG.Rule.134b…ace.3.none` |

How each changing datum reads:

- **Revocation and retirement**: presence in either replica decides. The record is removed in the
  same write as the revocation is added; nothing depends on that removal, because a record beside a
  revocation admits nobody (P6, P20). Today's emptied record goes: kdbxweb drops every custom data
  item whose value is empty on every save [M others], so an empty value is not a mark that
  survives, and for the same reason a revocation is never written empty.
- **Standing answer**: the answer in force for a certificate is the one with the highest `<n>`.
  Where several keys carry that `<n>` (two copies answered at once), the most refusing wins:
  `forever`, then the latest `until`, then `lifted`. A name that will not parse counts as
  `forever`. Allow renewal adds `….<n+1>.lifted`, so **a lift now travels through a merge**, which
  today it does not (`CA.md:225-227`).
- **Profile list**: a name is in force when its highest `in` generation is above its highest `out`
  generation. Adding a name writes `in.<g+1>` where `g` is the highest `out` seen (or `in.1`);
  removing it writes `out.<g>` for the highest `in` seen. At a tie `out` wins. More than 32 names in
  force reads as an unreadable list, which is an empty one (`ProfileNames`, unchanged rule): every
  client stays admitted and reaches nothing until the operator removes names. **A removal is an
  added key, so it survives every union**, which closes the measured Keepass2Android and KeePass
  cases where a removed name came back [M spike §6; `CA.md:147-155`].
- **Rule ledger**: `<gen>` counts from 1 per node. `<d>` is the first 16 hex digits of the SHA-256
  of the rule text, so the same edit made in two copies is one key and two different edits at one
  generation are two. The *head* of a node is every version at its highest generation, across both
  replicas. A ledger value whose `#` is not the generation in its key will not read.
- **The generation inside the rule** is what lets a stale rule be told from a fresh one without a
  clock. `#` cannot be a profile name (names are ASCII letters, digits, `-` and `_`), as `default`
  cannot. It changes only when the rules change, so the editor still never writes the same rules in
  other words. Because the text of generation 3 differs from the text of generation 1 even when the
  rules are the same again, a node holding the exact text of an older recorded version is holding
  that older version and nothing newer. The member takes one of the reader's 32, so a node names 30
  profiles at most where it names 31 today (`AclDocument.MostProfiles`).

Sizes, from the `inventory` report's measured lengths: an authority 2903 characters, a record 819, a
typical rule 101, so a database with one authority, ten clients, five names and twenty ruled nodes
holds about 14 KB in the dictionary and the same again in the mirror, plus about 3 KB of ledger in
each (arithmetic on measured sizes).

**Why the root group, when `CLAUDE.md` rules it out.** The root group was refused as the *store*
because a group's dictionary is taken whole in a merge, so two copies' additions do not both
survive there [M `MergeInRealDatabaseTests`, `inventory` P5]. As a *second copy read by union* that
weakness only makes the mirror lag, and a lagging mirror loses nothing while the dictionary holds.
What the root group has that nothing else in custom data has is that the one program that replaces
the dictionary never touches it [M probe].

**Why no times on the keys.** Nothing in this design reads a per-key time. A dictionary key with a
time forces KDBX 4.1 [M probe: 4.1 with times, 4.0 without], and kdbxweb then writes that time as
text into a file KeePass 2.62 cannot open [M others]. The plugin therefore writes each dictionary
key through `StringDictionaryEx.Set(string, string, DateTime?)` with no time. That member is
`internal` [M probe, by reflection on 2.62], so it joins `KeePassContract` as a pinned member, and a
KeePass without it gets keys with times, a 4.1 file and today's exposure to kdbxweb.

## 2. The read path

**Database-level keys.** A new `ReplicatedStore : IKeyValueStore` wraps `CustomDataStore` (the
dictionary) and a new `RootMirrorStore` (the root group's `CustomData`, keys beginning `KPG.` only,
so never the root's own rule under `KPG`).

- `Keys` is the union. `Get` answers the value either replica holds.
- A key both hold with **different values** is *contested*. Values are written once, so no measured
  program produces this; it means a hand edit or a forgery. A contested key is unreadable and fails
  closed by its kind: a record admits nobody, an authority document is damage, a ledger version
  refuses at its node and below, and for a revocation or a retirement presence still decides.
- `AuthorityStore`, `AdmissionStore`, `ProfileNames` and `OpenDatabaseTargets` keep reading through
  `IKeyValueStore` and do not know there are two places. The cache in `OpenDatabaseTargets` keys on
  the union's text. Every read still holds KeePass's thread still and is refused inside a merge
  (P25), and nothing that grants is cached across requests (E14).

**Rules.** `IAclNode` gains `Recorded`: the ledger versions for that node, from an index of
`KPG.Rule.` keys built once per request from the union (one pass over the keys). `AclResolver`
decides each node on the walk as one of three states:

| State | When | What is in force at that node |
| --- | --- | --- |
| **agrees** | the node holds no rule and nothing is recorded; or the head is one version and the node's rule is that text; or the head is one tombstone and the node holds no rule | the node's rule, as today |
| **behind** | the head is one version or one tombstone, and the node holds a rule whose text is exactly a recorded version of a **lower** generation | the head. The node is stale by the record's own order: its text carries its generation, and a higher generation was written by somebody who had seen that one |
| **disagrees** | anything else: a rule with nothing recorded, a rule whose text matches no recorded version (one of a higher generation than the head included), no rule where the head is a rule, a head of two or more versions, a head that will not read | **every witness must allow** |

An absent rule is never *behind*, because absence carries no generation: a rule that another
program deleted and a rule that was removed on purpose in a copy whose ledger did not arrive look
the same.

"Every witness must allow" is computed per profile on the climb. The witnesses at a disagreeing node
are the node's own document (or its absence) and each head version. For profile `p`:

```text
answer(node, p) = intersection over the witnesses w at node of:
                    w names p, or w's default is deny  ->  w's set for p (empty when not named)
                    otherwise (w is silent, or absent) ->  answer(parent, p)
```

At a node that agrees there is one witness, and this is today's "nearest wins, per profile" exactly.
At a node that disagrees, a witness can only take away: a narrowing or a seal that any witness holds
is in force, and a grant is in force only when every witness holds it. A witness that will not read
refuses there and below, as an unreadable rule does today (`AclDocument`, unchanged).

Three consequences worth stating once:

- **A rule another program deleted is no longer silence.** The record is still there, so the
  narrowing it made still holds (E6, E5). What the deletion costs is that node's grants.
- **A rule nobody recorded cannot grant.** A duplicated entry, a tree imported with new identifiers,
  a rule typed into custom data by hand or by a script, an entry that arrived through a merge that
  did not carry the ledger: each narrows as written and grants nothing until the operator presses
  OK on that node's KPG tab.
- **A restored history revision no longer rolls a rule back.** The live entry then holds a recorded
  older version, which is the *behind* state, so the newest recorded rule stays in force. Today the
  restore silently puts the old rule in force (`DESIGN.md:972-973`).

The routes that tell a client what it may do at a node answer through the same resolver, so they
report the narrowed answer and nothing about why.

## 3. The write path

**Database-level writes** go through `DatabaseWriter` as today: on KeePass's thread, saved, believed
only when `Modified` is false and the file hashes back (E7, P13). Each write now sets the key in
both replicas and ends with the *reconcile* below. One exception: while the root group's own Edit
Group dialog is open, the mirror half is skipped, because that dialog's OK would put its stale copy
of the root's custom data back [R keepass, `GroupForm.cs:209-231`]. The dictionary half lands, the
mirror lags, and the next save makes it good. Database Maintenance still refuses the write outright
(P11).

**The reconcile.** For every `KPG.` key in either replica: put it in the other. A contested key is
left as it is and logged. Then, for every node in the *behind* state whose dialog is not open, write
the head's text into the node's `KPG` key (or remove the key for a tombstone), moving no time (E13).
It runs in three places:

1. inside every `DatabaseWriter` write, before the save;
2. when the KPG settings dialog is accepted, with the buffer's replay;
3. at `FileSavingPre` of every save of that database, whoever asked for it, except a save of a copy
   and except while Database Maintenance is open: the same event and the same two exceptions as
   owed standing answers (P18, O4). `FileSavingPre` is raised after KeePass's merge and before the
   file is written, and File then Synchronize then copies that file over the other one [R keepass
   §4, `ImportUtil.cs:204-261`; not run], so one synchronize in a KeePass with the plugin leaves
   both files reconciled.

It never runs on open (O1 holds). A database that is opened and closed without a save is read by
union and left as it was found.

**Rules.** The grant editor is unchanged: it writes into the dialog's own copy `m_sdCustomData`, OK
keeps the rule and Cancel discards it (O3, P10). What is new is one step at `FormClosed` with
`DialogResult.OK`, where `TabPageInjector` already has a handler: `RuleLedger.Record(node)` compares
the node's rule, as the dialog left it, with the head. Where they differ, or the node disagreed, it
adds `KPG.Rule.<uuid>.<head + 1>.<d>` (or `.none`) to both replicas. The editor has already written
that generation into the rule's `#` when it changed the rules in the dialog's copy, so the node and
the ledger hold the same text. That is an in-memory change beside the dialog's own, not a save.

**Opening a node's KPG tab and pressing OK is how the operator settles a disagreement.** On a node
that disagrees, the tab shows the recorded rule where the head is one version and the node's own
rule otherwise, with one line naming which, and OK writes what is shown as the next generation, to
the node and to the ledger. A dialog whose KPG tab was never opened settles nothing: the editor has
not touched the dialog's copy, so an OK given for another reason (a rename, a new password) leaves
the node and the record as they were, and a deleted grant does not come back unseen.

**The Rules page**, a fourth page on the KPG settings tab beside Certificate Authority, Profiles and
Clients: one row per node that disagrees, with its path and why (rule missing, rule not recorded,
two versions), and two actions, **Restore** (write the recorded head back into the node) and
**Keep** (record the node's present state as the newest version), each for one row or for all. Like
everything on that tab it waits for the dialog's OK (O2): the ledger keys go into the buffer, and
the node writes are applied at OK through `DatabaseWriter` under the `GroupDialogs` guards.

**Pruning** still rides only on the plugin's own writes, and removes from both replicas: expired
records and revocations, retired authorities' documents, records beside a revocation, standing
answers once no record admits their certificate, profile generations below the newest of each kind,
ledger versions more than eight generations below a head, and the ledger of a node that is no longer
in the tree. Every one of these is a key whose return does no harm, which is the test a removal has
to pass in a store where removals never travel.

### What each named class becomes

| Class | Change |
| --- | --- |
| `CustomDataStore` | stays; writes without a time through the pinned internal setter |
| `RootMirrorStore`, `ReplicatedStore` | new, `IKeyValueStore` |
| `AuthorityStore` | logic unchanged, over the replicated store |
| `AdmissionStore` | `Remove` adds the revocation and removes the record (no emptying); standing answers become numbered keys; prunes cover the new kinds |
| `ProfileNames` | reads and writes `in`/`out` generations; `Merge` and the single `KPG.Profiles` value go |
| `PendingDatabase`, `PendingStore` | the buffer stays; the three reconcilers (`MergeProfiles`, `KeepTheLaterAnswer`, `KeepTheRevocationThere`) go, because replaying write-once keys key by key cannot conflict; the ceiling check at OK stays |
| `DatabaseWriter` | every write sets both replicas and reconciles; a new write kind applies Rules page restores |
| `OwedAnswerSaves` | also runs the reconcile at `FileSavingPre` |
| grant editor (`GrantPanel`, `NodeDialog`) | still writes into `m_sdCustomData`; loads the rule in force, shows one line when the node disagrees, and records at `FormClosed` |
| `AclDocument` | reads and writes the reserved member `#`; `MostProfiles` is one lower |
| `AclResolver`, `AclDocuments`, `KdbxNodes` | `IAclNode.Recorded`, the three states, the intersection |
| `GroupDialogs` | watches the same six dialogs; additionally answers whether the root group's dialog is open, for the mirror half |
| the prune | as above, both replicas |
| `KeePassContract` | pins `StringDictionaryEx.Set(string, string, DateTime?)` |

## 4. What a save does to it

"Kept" means both replicas, the node rules and the ledger came back as written.

| Program | Dictionary | Root mirror | Node rules | Net effect | Basis |
| --- | --- | --- | --- | --- | --- |
| KeePass 2.62 with the plugin | kept | kept, reconciled | kept | none | [M probe] for the file staying 4.0 and both places reading back |
| KeePass 2.47 | kept, times dropped (there are none) | kept | kept | none | [M spike §5] for 35 saves keeping every key and rule; the root group's custom data is [A], measured for group G |
| KeePassXC 2.4.2 to 2.8.0 | kept; `_LAST_MODIFIED` and `KPXC_RANDOM_SLUG` added | kept; `_LAST_MODIFIED` added | kept | none; the root group takes the time of the save | [M probe] 2.4.2 and 2.8.0; [M spike §5]; [M keepassxc §6] |
| Keepass2Android | kept | kept | kept | none | [M spike §6] for dictionary and rules; mirror on the root [A] |
| KeePassDX 4.5.8 | kept | kept | kept | none; root group takes the time of the save | [M spike §6]; mirror on the root [A] |
| KeePassDX 3.2.0, 3.3.0 | kept | **deleted** | **every group's rule deleted**, entry rules kept | group grants off until restored; narrowings hold from the ledger; the mirror is rebuilt at the next KeePass save | [M spike §6] for group G; [R android §2] for the writer having no group custom data; the root group [A] |
| KeePassDroid 2.7 | kept | **deleted** | **every rule deleted** | every grant off until restored; narrowings hold | [M spike §6]; [R android §2] |
| kdbxweb 2.1.1 (KeeWeb) | kept on a 4.0 file; an empty value would be dropped, and there is none | kept | kept | none, **as long as no dictionary key in the file carries a time** | [M others] |
| pykeepass 4.2.0 | kept | kept | kept | none | [M others] |
| KeePassium, Strongbox, MacPass | kept | kept | kept | none | [R others] |
| gokeepasslib before 3.7.0 | kept | **deleted** | group rules deleted | as KeePassDX 3.2 | [R others], consequence [A] |

No save in any program was measured to remove a dictionary key [M spike §5, §6; M measured §3.1], so
after any single save the ledger and every tombstone are intact, whatever happened to the nodes.

## 5. What a merge does to it

C is the stale copy, P the copy that revoked a client, recreated the authority, removed a profile
and narrowed two rules since. "Union" means the result's two replicas together hold every `KPG.`
key either copy held.

| Merge | Dictionary | Root mirror | Rules on nodes | Result under this design | Basis |
| --- | --- | --- | --- | --- | --- |
| KeePass 2.62 Synchronize, either direction | union: 14 of 14 keys, nothing removed | one copy's whole: the newer root group's, the arriving copy's at equal times | the newer node whole; the arriving node at equal times | complete; the reconcile at the ending save makes both replicas the union | [M probe] 6 cells; [M spike §1 to §3] for the dictionary under all five methods on 2.61.1; [M keepass §3] for groups |
| KeePass, a copy whose rules another program deleted, arriving at equal node times | union | the wiped copy's (empty) when its root is newer or equal and arriving | **the deletion travels**: the arriving node wins a tie, rule and all | narrowings hold from the ledger; grants off on those nodes until restored | [M probe] for a wiped root group that is newer, where the mirror and the root's own rule were gone from the result in both directions; [M keepass §1a, §3] for the tie rule on other nodes |
| KeePass import, Create new IDs | union | the arriving mirror lands on a new, non-root group and is never read | every arriving node is a duplicate with a rule nobody recorded | duplicates narrow as written and grant nothing | [M keepass §2.4] for the duplication; the consequence follows from section 2 |
| KeePassXC 2.4.2 to 2.8.0, loads in different seconds, stale C into P | **replaced by C's**: revocation, retirement, new authority, new record, profile removal and both rule versions gone | **P's, untouched** | groups keep the target's; an entry is the newer whole | **union is complete, 14 of 14**; nothing P held is lost | [M probe] 4 of 4 releases |
| the same, P into stale C | replaced by P's | C's, untouched | group G keeps C's wide rule under P's time; entry E1 takes P's | union complete; G holds a recorded older version, so it is *behind* and P's narrow rule is in force at once | [M probe] 4 of 4 releases |
| KeePassXC, both loads in one second, stale C into P | unchanged | unchanged | unchanged | nothing lost | [M probe] 2.4.2, 2.8.0 |
| KeePassXC, both loads in one second, P into stale C | **C's, unchanged** | **C's** | G keeps C's rule; E1 takes P's | **the result is C's state: 7 of 14 keys. The revocation, the retirement and G's narrowing are not in it.** E1 holds a rule nobody recorded there, so it narrows | [M probe] 2.4.2, 2.8.0 |
| KeePassXC 2.4.1 | never merged | target's | as above | as the row above, always | [M spike §5]; mirror [A] |
| Keepass2Android | union for keys only one copy holds; a key both hold has one value here, so nothing is decided | the newer root group whole | the newer node whole | complete; the measured return of a stale record and a stale list (9 of 18 runs) has nothing left to act on, since no key changes value | [M spike §6] for the dictionary and nodes; root mirror [A] |
| KeePassDX 4.5.8 | by per-key time, removing nothing; with no times the arriving item is put, and it is the same value | arriving items put key by key, nothing removed | **the arriving rule under the newer time** | complete; the stale rule is a recorded older version, so the newest recorded rule is in force | [M spike §6] for dictionary and rules; [R android §1.1] for `mergeCustomData`; mirror union [A] |
| KeePassDX 3.2.0 | as 4.5.8 | deleted by the save that follows | group rules deleted, entry rules as 4.5.8 | as a 3.2.0 save | [M spike §6] |
| kdbxweb 2.1.1 | a key the open copy lacks is added | the open copy's | **the open copy's rule under the newer time** | complete when the fresher copy is open; when the stale copy is open the ledger arrives and the node is *behind* | [M others] for nodes; [R others] for the dictionary rule |
| Strongbox | KeePass's rule, never removing a key | the newer root whole | the newer node whole | complete | [R others] |
| keepass-rs 0.17.0 | **not merged** | the newer root group whole | the newer node whole | the target's dictionary with whichever mirror came with the newer root; union of those two | [R others] |
| whole-file synchronizers | one file | | | one copy's state, whole | [R others] |

Two things in that table are new measurements and carry the design. First, in every KeePassXC merge
that replaced the dictionary, the union of the dictionary and the root mirror held all 14 keys, in
both directions, in 2.4.2, 2.6.6, 2.7.12 and 2.8.0 [M probe]. Second, a KeePass 2.62 Synchronize
over keys with no times is a plain union in both directions, including when the other copy's root
group was newer or its node custom data had been deleted [M probe].

## 6. What a person sees, what they can break, and what the gate does

**What is visible.**

- KeePass: Database Maintenance lists every dictionary key on its Plugin Data tab, as today, now
  with more names (`KPG.Profile.…`, `KPG.RenewalAnswer.…`, `KPG.Rule.…`). The root group's Edit
  Group dialog lists the whole mirror on its own plugin data page, the authority document
  included [R keepass §3 for the dialog copying and writing back custom data; the page's content
  [A]]. A group's or entry's dialog shows its `KPG` rule there as today.
- KeePassXC: the Properties page of the root group lists every custom data item with a Remove
  button, behind a confirmation [R keepassxc §8]; that page then holds the mirror. A merge prints
  each dictionary key it removes or adds **with its value** [M probe, 2.8.0: `Plugin-Daten
  KPG.Authority.a2 entfernt [document-a2]`], so on the command line, and from 2.7.11 in the merge
  dialog's change list [R keepassxc §3], an authority document with its private key is written to
  the screen. That is true of the present store as well.
- The Android programs show custom data nowhere [A].
- KeePass's XML export writes both replicas in the clear (P26). KeePassXC's CSV and HTML exports
  carry no custom data [M keepassxc §8].

**What a person can do by hand, and the gate's answer.**

| Act | Where | What the gate does |
| --- | --- | --- |
| delete a dictionary key | KeePass Database Maintenance | the mirror still holds it; read by union; put back at the next save. To really remove a key the operator uses the KPG tab |
| delete a mirror key, or all of them | KeePass root group dialog, KeePassXC root group Properties | the dictionary still holds it; put back at the next save |
| delete the same key in both places | both of the above | the key is gone. For a revocation or a retirement this is the hand deletion `CA.md:324-332` already describes, now needing two deliberate steps. Fails closed for a record (the client pairs again) and open for a tombstone, as today |
| delete a node's `KPG` rule on the plugin data page of its dialog | KeePass, KeePassXC | the record has no tombstone: the node disagrees. Narrowings hold, grants are off, the Rules page lists it. Removing a rule is done on the KPG tab |
| type or script a rule into a node's custom data | any program, pykeepass | not recorded: narrows as written, grants nothing, listed |
| change a value in one replica | any program | contested: unreadable, fails closed by kind, named on the page that shows that kind |
| edit the root group (name, notes, icon) | any program | nothing; the mirror is custom data and travels with the root group's version |
| restore a history revision | KeePass | the entry is *behind*; the newest recorded rule stays in force and the node is put right at the next save |
| duplicate an entry, copy and paste, import with new IDs | KeePass | the copy's rule is not recorded: it narrows, grants nothing, and is listed until the operator presses OK on it |
| delete a ruled node | any program | the rule goes with it; its ledger is pruned once the node is out of the tree |
| Database Maintenance left open | KeePass | writes refused as today; the save-time reconcile is skipped |

Everything in the right-hand column is either unchanged access or narrower access. Nothing a person
does to one place widens what a client reaches, and a revocation holds until its key is removed from
both places.

## 7. Keeping the API and the reference client out

Nothing changes, because nothing moves into what the API serves. No route reads or writes custom
data of the database, of a group or of an entry (P15: no use of `CustomData` under `Vault\` or in
`Pipeline\VaultHandlers.cs`), and that covers the root group's mirror as it covers a node's rule
today. A client holding `create` and `update` everywhere can create no key, forge no record and
write no rule (E8). Node tags are made from no custom data (no use of it in `Bridge\NodeTags.cs`),
so a mirror write moves no tag. `PyKeePassGate` has no route to cover and no model to change. The
one thing a client can observe is its own reach narrowing at a node that disagrees, and a refused
node still answers the 404 an absent one answers, so the reason stays in the audit.

## 8. The KDBX version it needs

**KDBX 4.0.** Database, group and entry custom data are all 4.0 features [M keepass §2.1], and with
no per-key time on any `KPG.` key nothing here asks for 4.1: the probe's files were written as 4.0
by KeePassLib 2.62 and stayed 4.0 through every KeePassXC save and merge [M probe]. The same layout
with times was written as 4.1 [M probe]. KeePass 2.62 never writes below 4.0 [M keepass §2.1], and a
node's custom data keeps Keepass2Android and KeePass 2.47 from writing 3.1 [M spike §5, §6].

## 9. Settled decisions: reversed and kept

**Reversed, each to be recorded as a reversal with its reason:**

- "The database's own `CustomData`, never the root group's": the root group now holds a second
  copy. The reason it was ruled out (a group's dictionary is taken whole) is why it is only ever
  read by union.
- "The authority takes the file to KDBX 4.1": gone with the per-key times.
- "A revocation is `KPG.Revoked.<key>` beside the emptied record": the record is removed, not
  emptied, and a revocation's value is never empty.
- "`KPG.Profiles` is replayed as a difference" and "a standing answer to a renewal is the other key
  with a second writer that matters": both go. The representation is the difference, and
  `PendingStore.Reconcile` has nothing left to merge.
- "A removal is the one thing a KeePass merge never carries, so a copy from before the lift brings
  the answer back": a lift is now an added key.
- "The plugin writes no rule of its own" (`CA.md:131`): the reconcile and the Rules page write
  recorded rules back into nodes.
- The access model's "an empty value is an absence", which inherits (`AclDocument.cs:110-120`): an
  absent rule beside a recorded one is no longer silence. And "one document per node" gains a
  reserved member, `#`, beside `default`.
- The measured fact "a KeePass merge of `PwDatabase.CustomData` … decides by the two modification
  times": still true, no longer load bearing.

**Kept:** every database is its own authority, inside the encrypted file, with no host registry
(E9, E10); the admission record is load bearing and read on every request (E2, E14); claims carry
profile names only; no passphrase on the authority key; one key per authority and `503 undecided`;
the two host certificates; the single writer, its save rules and `500 not verified` (E7); the KPG
tab waiting for OK, revocation included (O2); the grant editor writing into `m_sdCustomData` (O3);
opening changes nothing (O1); prunes ride the plugin's own writes; nearest wins per profile, at
every node that agrees; a document that will not read refuses; no per-subject opt-in; spec-first
routing and the 404 rule; one JSON grammar and one escaper (the ledger value is the rule text
itself, and no new JSON shape is stored); `GroupDialogs` and the two traps, with the root group's
dialog added to the first.

## 10. What it does not solve

Each of these follows from a measurement and cannot be fixed while the data lives in custom data.

1. **A merge that carries no custom data leaves the target's state as the result.** KeePassXC 2.4.1
   merges no database custom data at all [M spike §5]; KeePassXC from 2.4.2 merges none when both
   loads fall in the same second [M spike §5; M probe]; no KeePassXC release and no kdbxweb merge
   ever takes the other copy's group custom data [M spike §5; M keepassxc §4; M others]; keepass-rs
   merges no database metadata [R others]. Merging the fresher copy into a stale one in any of
   these gives a file that holds the newer entries and the **older** revocations, retirements,
   profile list and group rules, consistently, in both replicas: 7 of 14 keys in the probe. Nothing
   in that file shows it, because nothing in that file is newer. If that file becomes the database,
   a revoked client is served. The carrier is the problem: the merge does not transport it, so no
   arrangement inside it can.
2. **Two foreign programs in a row, with no KeePass save between them.** A KeePassDroid or
   KeePassDX 3.2/3.3 save deletes the mirror [M spike §6 for group custom data]; a KeePassXC merge
   of a stale copy into that file then replaces the dictionary [M probe]. Both replicas have now
   lost everything written since the stale copy, revocations included, and the result is again
   consistent. The same holds if the wiped root group reaches the desktop copy through a KeePass
   synchronize run without the plugin. Not run as a chain; each half is measured.
3. **A whole stale file.** An overwrite in Keepass2Android, a KeePassDX save over a file whose time
   moved less than ten seconds, every KeePassDroid save over a changed file, a file synchronizer's
   pick between two files, a restored backup [R android §5.5; M spike §6 `keepPC`; R others]: the
   result is one copy's entire state. A witness inside the file is rolled back with it.
4. **Grants do not survive a rule's deletion; only narrowings do.** After a KeePassDroid save every
   grant is off, and after a KeePassDX 3.2/3.3 save every group's, until a person restores them.
   Since everything a client reaches is reached through a grant, that is the gate serving nothing.
   Section 11 has the cost.
5. **For anyone who uses those two programs, the node is no longer where the rule lives.** The
   ledger is what survives, and the node's copy is deleted at every save on the phone. The design
   then behaves like rules kept centrally, with a per-node copy that exists for the dialogs.
6. **A stale rule is recognized only as far back as the ledger is kept** (eight generations). An
   older one reads as not recorded: it narrows and cannot grant, which is safe and is an outage for
   that node.
7. **Two copies that edited one node's rule** are in force as their intersection until the operator
   settles it. Safe, and possibly narrower than either operator meant.
8. **The file can still be made unreadable by KeeWeb**, when any dictionary key written by something
   else carries a time [M others]. This design only keeps its own keys from being the cause.
9. **Whoever can write the file can write both replicas and the ledger** (P23). All of this defends
   against programs that damage data without meaning to, and against nobody who means to.
10. **The mirror is one more place where the authority's private key is displayed and exported**
    (section 6), and KeePassXC prints dictionary values in a merge's change list [M probe].
11. **Three dependencies on KeePass internals or ordering**: the internal setter, `FormClosed`
    running after the dialog has put its copy back, and `FileSavingPre` running after a merge.
12. The recycle bin markers of T049 would fit as write-once keys (`KPG.RecycleBin.<group>.<since>`)
    in both replicas; nothing here needs them and they are not designed further.

## 11. What "refuse to serve until the operator has looked" costs

There are two things that sentence can mean, and they cost differently.

**What this design does: refuse the grants at a node whose rule and record disagree.**

| Program | How often | What stops |
| --- | --- | --- |
| KeePassDroid 2.7 | **every save** | every grant in the database |
| KeePassDX 3.2.0, 3.3.0 | **every save** | every grant on a group |
| a KeePass synchronize that takes such a copy's nodes at equal times | every such synchronize | the same grants, now in the desktop copy too [M keepass §1a, §3] |
| KeePassXC, Keepass2Android, KeePassDX 4.5.8, kdbxweb merges | never | nothing: their stale rules are recognized as *behind* |
| duplicate, import with new IDs, hand edits | each time | that node's grants |

What stopping means: clients get the same 404 a missing node gets, so an agent sees an empty
database and no error; the audit holds the reason. It lasts until a person sits at the Windows
machine, opens the KPG tab, presses Restore all and OK. An unattended gate stays dark for as long as
nobody does. And an operator who edits on a phone daily presses Restore all daily, at which point
the look is a habit, and in case 2 of section 10 the habit restores a stale ledger.

The one relaxation available is to restore without asking when a node's rule is simply gone and the
record is intact. It would end the outage for KeePassDroid and KeePassDX 3.x users. It is not in the
design, because the same picture (rule gone, record present) is what an entry looks like when its
rule was removed on purpose in a copy whose ledger did not travel (section 10, case 1), and there
the restore would put a removed grant back.

**The stricter reading: refuse the whole database once another program has written the file.**
This cannot be built here, for two reasons.

- **No in-file sign reliably shows who wrote the file.** KeePassXC leaves `_LAST_MODIFIED` and, from
  2.7.12, a fresh `KPXC_RANDOM_SLUG` at every save [M spike §5; M probe]. KeePassDX gives the root
  group the save's time [M spike §6]. KeePassDroid writes no `SettingsChanged` [M spike §6].
  Keepass2Android was measured to add no key, move no node time and keep a 4.0 file's
  `SettingsChanged` [M spike §6; M measured §3]; the one trace it left was the per-key times it
  dropped, and this design has none to drop. pykeepass leaves nothing either [M others]. A check
  built on these signs would be a list of today's accidents.
- **A sign that is reliable needs a memory outside the file** (the hash of the last file this
  KeePass wrote), which is the host-side registry E9 rules out, and it would fire every time the
  same database arrives from the operator's other machine.

And if it could be built, it would cost the outage above on every phone save in any program, for a
look that has nothing to show: in the cases that matter (section 10, cases 1 to 3) the file is
consistent, and what is missing is exactly what the operator cannot be shown.

## 12. What a migration would take

One paragraph, as asked. A database holding today's layout would be converted inside one plugin
write: `KPG.Profiles` into `in.1` keys, each `KPG.RenewalRefused.<fp>` into answer 1, emptied
records removed, each key rewritten without its time, every rule rewritten with `"#":"1"` and
recorded as generation 1 of the ledger, and the mirror built. The rule step is the one with a cost.
It blesses whatever rules the nodes hold on that day, so a rule that is already stale or already
deleted is recorded as right or as absent, and the operator would have to be shown the rules before
accepting. And it changes the custom data of every ruled entry and group outside a dialog: without
moving their times, another copy's unconverted node wins the next tie; with them moved, every ruled
entry gains a history revision nobody asked for. Whether a 0.1.0 database is carried over at all is
a question for the project (O7).

## 13. The probe behind the [M probe] marks

Windows PowerShell 5.1, KeePassLib 2.62 loaded as an assembly, `keepassxc-cli` only, no window,
databases with invented content and the password `probe-only`. Script and output:

- `spikes\storage\probes\hardened-custom-data\design\scripts\Probe.ps1`
- `spikes\storage\probes\hardened-custom-data\design\out\` (`make2.txt`, `keepass.txt`, `xc-<release>-slow.txt`, `xc-<release>-fast.txt`, `xcsave-<release>.txt`)

**The databases.** A base with a group G and an entry E1 in it. C, the stale copy: seven `KPG.` keys
in the dictionary and the same seven in the root group's custom data (an authority, two records, two
profile `in` keys, one ledger version each for G and E1), and a wide rule on G, on E1 and on the
root. P, the fresh copy: C after `KPG.Revoked.c2`, `KPG.RetiredAuthority.a1`, `KPG.Authority.a2`,
`KPG.Client.c3`, `KPG.Profile.writer.out.1` and a second ledger version for G and for E1 were added
to both places, `KPG.Client.c2` and `KPG.Authority.a1` were removed from both, and G and E1 were
given a narrow rule and a time 31 days later. Values are placeholders. Dictionary keys were written
through the internal setter with no time. Slow is Argon2d at 200 iterations and 64 MiB, under which
one `keepassxc-cli merge` took 7.9 to 11.0 s; fast is 2 iterations and 1 MiB, 0.1 to 0.2 s.

**What was run and what it showed.**

| Run | Cells | Result |
| --- | --- | --- |
| file version | timeless and timed sets | timeless keys in both places: KDBX 4.0; the same with times: 4.1 |
| `StringDictionaryEx` members, by reflection | 2.62 | `Set(string, string, DateTime?)` and `GetLastModificationTime(string)` are `internal` |
| KeePassLib 2.62 `MergeIn`, Synchronize | C and P as made; C's root group stamped later; C's root later and every node's custom data deleted; each into P and into C: 6 cells | dictionary: 14 keys in all 6, none removed, no key with two values. Root mirror: at equal root times the arriving copy's (C's 7 into P, P's 12 into C); with C's root later, C's in both directions; with C wiped, empty in both directions, and the root's own rule gone with it. G and E1 kept P's narrow rule (P's nodes are newer) |
| `keepassxc-cli merge -s`, slow | 2.4.2, 2.6.6, 2.7.12, 2.8.0, into P and into C: 8 merges | dictionary replaced by the source's in all 8 (into P: C's 7; into C: P's 12); root mirror the target's in all 8; **union 14 of 14 in all 8**; no key with two values. Into C, G kept C's wide rule under P's time and E1 took P's rule, in all 4. Root time unchanged. Files stayed 4.0 |
| `keepassxc-cli merge -s`, fast | 2.4.2, 2.8.0, into P and into C: 4 merges | the `KPG.` keys of both places unchanged in all 4. Into C the result held C's 7 keys in both places, G's wide rule, and E1's narrow rule from P |
| `keepassxc-cli add`, a plain save of P | 2.4.2, 2.8.0 | all 12 keys kept in both places; `_LAST_MODIFIED` added to both, `KPXC_RANDOM_SLUG` to the dictionary in 2.8.0; root time became the save's; 4.0 |
| what a merge prints | 2.8.0 | each dictionary key removed or added, with its value in brackets |

The probe measures the storage, not the plugin: its rule texts carry no `#`, and the three states of
section 2 are applied to its results by reasoning, not by code. Not run: any program with a window,
any Android program, KeePass's own window, the chain of section 10 case 2, kdbxweb over this layout.

## 14. Measurements to make before this design could be chosen

Most important first. The first four decide whether the design stands at all.

1. **The root mirror through the Android programs**, on the spike's emulator flows: a
   Keepass2Android save and merge, a KeePassDX 4.5.8 save and both merge paths, and that KeePassDX
   3.2/3.3 and KeePassDroid delete the *root* group's custom data as they were measured to delete
   G's. Every "mirror [A]" in sections 4 and 5 is this. If KeePassDX 4.5.8's key-by-key merge of
   group custom data drops or rewrites root keys, the mirror is weaker than claimed.
2. **KeePassXC's merge from its window, its merge on reloading a changed file, KeeShare and 2.8.0's
   remote sync**: that the root group's custom data is kept there as on the command line, and which
   dictionary wins when the target has been open for a while (section 10 case 1 is frequent or rare
   depending on this).
3. **A real File then Synchronize in KeePass 2.62 with a plugin handler on `FileSavingPre`**: that
   it runs after `MergeIn` and before the write, that a key added there reaches both files, and
   what the second, inner `FileSavingPre` of a forced synchronize sees [R keepass §4, not run].
4. **The grant editor's close**: that at `FormClosed` with OK the group and entry dialogs have
   already put `m_sdCustomData` back, on the group dialog, the entry dialog and Add Entry, so the
   ledger records what the node really holds; that Cancel leaves nothing recorded; and that a rule
   whose only change is its `#` is a change the entry dialog keeps.
5. **The spike's 70-cell matrix again on 2.62 for this layout**: write-once keys with no times in
   both places, all five merge methods, both directions, with the root group newer on either side.
   Only Synchronize was run here.
6. **The two-program chains** of section 10 case 2, end to end, and a KeePass synchronize of a file
   KeePassXC merged, to confirm the stated losses and that nothing else is lost.
7. **kdbxweb 2.1.1 over this layout**: a save and a merge of the 4.0 file with both replicas, and
   that KeePass opens the result.
8. **Whether Database Maintenance's write-back and KeePass's own save keep a key timeless**, and
   whether any KeePass path stamps a time onto an existing key (which would take the file to 4.1).
9. **The resolver's cost with the ledger index** on a large tree: the index is built once per
   request from the union, on a connection thread holding KeePass's thread still (A6).
10. **A root group custom data of about 30 KB** through each program's save, and through KeePass's
    group dialog, which copies and writes it back.
11. **How often two `keepassxc-cli` loads fall in one second** under KeePassXC's default KDF
    settings on ordinary hardware.
12. **Strongbox, KeePassium and MacPass** saving and, for the two that merge, merging this layout:
    read only so far.
