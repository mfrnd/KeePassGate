# Option `wildcard`: one sealed snapshot of everything, replaced whole, with deletion records as witnesses

Written 2026-11-15 against KeePassGate master `a42e38aa`. The task for this option was "anything
else that survives": the best design that is none of the other options (records as entries in a
dedicated group, rules in fields of their node, marker entries, central rule entries, versions in
timestamp-named fields, a file outside, hardened custom data), built from what other programs
demonstrably keep and merge well. This is one design, in its strongest form, with the reasons the
runners-up were discarded.

## 0. How statements are marked

- **[M probe]**: measured for this report, today, from a console. Probe, scripts and raw output are
  under
  `spikes\storage\probes\wildcard\design\`
  (`probe\Program.cs`, `scripts\Run.ps1`, `scripts\pk.py`, `scripts\kw.js`, `out\summary.txt`,
  `out\merges.txt`, `out\kdbxweb.txt`, `out\keepass247.txt`, `out\bench.txt`, `out\lattice.txt`).
  Section 16 describes the runs.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M measured]**, **[M android]**,
  **[M inventory]**, **[M spike]**: measured in that ground report (`ground-keepass.md`,
  `ground-keepassxc.md`, `ground-others.md`, `ground-measured.md`, `ground-android.md`,
  `ground-inventory.md`), or recorded by `spikes/revocation-merge` as the ground reports digest
  it.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line. **[R here]** is read by me
  from KeePass 2.62's source or the review4 worktree, with the file named.
- **[A]**: assumed. Neither run nor read. Every [A] the design rests on is in section 17.

Requirement numbers (E1 to E14, A1 to A10, O1 to O7, P1 to P27) are the `inventory` ground
report's.

## 1. The design on one page

1. **Everything the plugin keeps about a database is one document, a snapshot.** The authority,
   the retirements, the admission records, the revocations, the standing answers to renewals, the
   profile list and every access rule, rules included, each under a member name of its own.
2. **A snapshot is one ordinary entry**, in a group of the plugin's own: the document in a
   protected custom string, `KPG.Store`, and a seal over it in a second string, `KPG.Seal`. A
   snapshot entry is written once and never changed.
3. **Every write of the plugin adds one new snapshot holding the whole new state and deletes the
   snapshots it replaces.** So in the ordinary state a database holds one KPG entry, and nothing
   of the plugin's is ever in an entry's history.
4. **The document seals itself.** It carries a key made once for the store, and the seal is an
   HMAC under that key over the document, which names its own entry's identifier. A document that
   was cut or edited, a copy of one under another identifier, and one brought in from another file
   are each told apart from a snapshot of this store.
5. **Two copies that both wrote leave two snapshots after a merge**, because every merge
   measured or read adds an entry only one side holds. What is in force is their **join**: a
   union for what is only ever added, the higher number for what is numbered, and for two rules
   of one node that never saw each other, what both allow. No modification time is read and no
   history.
6. **Deletion records are the witnesses**, in three ways. A replaced snapshot leaves the deletion
   record KeePass's own delete leaves, and snapshot identifiers are recognizable, so a snapshot
   that was deleted and that nothing present replaces is noticed. A snapshot that is present
   beside a deletion record naming it is not in force, which is what stops a KeePassXC merge
   bringing a replaced or deleted snapshot back into force. And every revocation and retirement
   is written a second time as a deletion record whose identifier is derived from the certificate
   or the authority, so it holds where no snapshot holds it.
7. **Nothing is written on a governed node.** A rule is a member of the snapshot named by the
   node's identifier. A group or entry that carries a rule looks, in every program, like one that
   carries none.
8. **Loss is all or nothing, and either noticed or refusing.** Another program cannot delete one
   rule, undo one revocation or bring back one profile. It can keep the snapshot, lose it, or put
   an older one beside it. The gate serves a store that is whole and accounted for, answers
   `503 undecided` when it is not, and has no gate at all when no snapshot is left.
9. **Opening, reading and merging write nothing.** The join is computed at read. The plugin's
   next write of its own makes one snapshot of it.

**The honest weaknesses, first.**

- **A rule names a node by identifier and does not cover a copy of that node.** Duplicate an
  entry whose rule narrows what its group grants, and the copy is governed by the group. The one
  case the gate sees is an import that copied the store too (section 9). This is the weakness
  `option-rules-central.md` names for itself, and this design has it whole.
- **All or nothing cuts both ways.** Damage to the one entry stops the gate for that database
  until the operator has pressed a button in KeePass. No other option has a cliff this steep.
- **A deletion that leaves no deletion record**, which is what a script over pykeepass makes
  [M probe], of the newest snapshot in a file that still holds an older one, rolls the store back
  with nothing to show it, except for the revocations and retirements whose witnesses that file
  holds.
- **The store is visible** in every program as a group with one entry carrying a field of 5 to
  40 KB, and whoever opens that entry can delete it.

## 2. The candidates, and why this combination

What the task named, and what became of each. The measurements that decide are the ground
reports'.

| Candidate | Verdict | Why |
| --- | --- | --- |
| Deletion records (`DeletedObjects`) as the tombstones | **used, as the second witness, not as the store** | A record is an identifier and a time and nothing else [R keepass 5]. Every program keeps the list on save [M keepassxc 6; R android 2; R others 3] and none shows it. KeePass's Synchronize, Keepass2Android, Strongbox, KeePassKit and kdbxweb take the union [M keepass X4; R android 3.1; R others; M probe]. But a KeePassXC merge never copies one [M keepassxc; M probe], KeePass's four other merge methods never copy one [M keepass X4; M probe], KeePassDX applies the arriving ones and keeps none [R android 3.1], and Database Maintenance clears them all [R keepass 5]. So a revocation held only there would not reach the other copy through KeePassXC. As a second place for revocations and retirements, which must never be lost, it fails under different programs than an entry does |
| Entry history as an append-only log | **discarded entirely** | Trimmed to 10 items or 6 MiB at every load, save and merge by KeePass [M keepass 1b], at every load by Keepass2Android [R android 1.3], at every save by Strongbox [R others]; items with equal times collapse [M keepass 1b]; a KeePassXC merge never adds a loser behind ten newer items [M keepassxc 5]; KeePassDX's history item does not hold the losing custom data [M measured 4.2]; kdbxweb treats a missing history item as deleted [R others]. This design makes no history at all |
| One attachment holding a signed container | **the container is used; the carrier is a protected string** | KeePassDX takes two attachments for one when 32 bits of their MD5 and their length agree [R android 5.1; such a pair was found in 0.16 s, M android 5.1]; KeePassXC rewrites every attachment's flag [M keepassxc 6]; KeePassium has an open report of entries referring to missing binaries [R others]. A protected custom string is kept, flag included, by every program read or measured [R android 2; M keepassxc 6; M others 3], and 1 MiB of it came through KeePassLib 2.62 and four KeePassXC releases with its seal intact [M probe]. Passkeys already put a private key into such a string in KeePassXC, Strongbox and KeePassium [R others 5] |
| One container entry, changed in place on every write | **discarded for "written once"** | An entry both copies changed is taken whole by its time; at equal times one side is lost with no history item in KeePass, KeePassXC, Keepass2Android and KeePassDX [M keepass 1a; M keepassxc 5; R android 1.1]; a time rewritten forward makes stale content win [M keepass 1a]. A copy restored from a backup that then writes would win over the newer line by its time |
| The binary pool | **discarded** | Not a place: rebuilt at each save from the attachments entries hold [R keepass 6] |
| Tags as flags | **discarded** | Group tags force KDBX 4.1 [M keepass X7], are dropped by KeePassXC 2.4.2 and 2.6.6 saves and never merged by any KeePassXC while it raises the group's time [M keepassxc 4, 9]. An entry tag is taken with its entry by time. The one use weighed was a mark on ruled entries so that a copy betrays itself: it needs a write to the governed entry (a new time and a history item, against E13), is lost when an older copy's edit is the newer, and groups have no carrier for it |
| The recycle bin as the place | **discarded** | The API already cannot see the bin [R inventory P15], but emptying it deletes everything in it in every program [R keepass 9; R keepassxc 8], and KeePassXC does not merge the bin: it arrives as an ordinary group [M keepassxc 4] |
| The templates precedent: a group named in the metadata | **the fixed identifier is used, the pointer is not** | A pointer of our own in `Meta` is unknown XML, which every program drops [M keepassxc 6; M others; R android 2]. Reusing `EntryTemplatesGroup` would offer snapshots as templates [R keepass 9]. What is taken from the precedent is Keepass2Android's: a group with a fixed identifier, so copies made independently hold the same group to a merge [R android 4.1; M probe set X] |
| A store that authenticates itself with a key | **used** | Section 3.3 has what the seal can and cannot do |
| Custom icons, header public custom data, group notes | **discarded** | Unused icons are deleted by a KeePass command [R keepass 8]; the header's custom data is not encrypted [M keepass 7]; a group's notes go with the whole group by time, the group dialog's OK always moves that time, and Delete Empty Groups removes a group holding only notes [M keepass 3] |

**What separates this from `option-store-entries.md` and `option-rules-central.md`**, which also
end in ordinary entries in a group of the plugin's own: their unit is the record, one entry a
record, each merged and damaged on its own. Here the unit is the whole state. That is what makes
the damage all or nothing, lets one seal cover everything, keeps the store at one entry, and puts
rules and records under one merge rule. It is also what makes the cliff in section 1.

## 3. The store

### 3.1 The group and the entry

| Property | Value | Why |
| --- | --- | --- |
| Group | named `KeePassGate`, under the root, icon 29, one line of notes naming what it is, searching off, auto-type off, collapsed | search, reports, expiry warnings and auto-type skip such a group in KeePass [M keepass X11; R keepass 9], KeePassXC [R keepassxc 8] and the Android programs [R android 4.2] |
| Group identifier | fixed, the same in every database | found by identifier, never by name; two copies that each made the group hold one group after a merge [M probe set X, 16 cells] |
| Entry title | `KeePassGate store`, fixed | no client text ever reaches a title or a URL, which KeePass compiles before auto-type matching [R keepass 9] |
| `KPG.Store` | the document, a protected string | section 3.2 |
| `KPG.Seal` | base64 of the HMAC, not protected | section 3.3 |
| Other fields | none. No password, no URL, no expiry, no tags, no attachment, no custom data | KeePassXC adds empty `Notes`, `Password`, `URL` and `UserName` at its first save without moving the entry's time [M probe, all 116 snapshot entries in the files it wrote]; nothing reads them |
| Entry identifier | six bytes `KPG-S1`, four bytes holding the snapshot's sequence number, six random bytes | recognizable, so a deletion record of a snapshot is known for what it is (section 6) |

**A snapshot counts wherever it is found**: in the group, in the recycle bin, in the root (where
Keepass2Android puts an arriving entry whose group is missing [R android 1.5]), in a user's group
somebody dragged it to. Place means nothing, because every way of not counting a snapshot could
drop something it refuses. Measured: moved into `General` by `keepassxc-cli mv` and into the
recycle bin by `keepassxc-cli rm`, it stayed in force [M probe, 2.6.6 and 2.8.0].

### 3.2 The document

One flat JSON object of strings and arrays of strings, which is the grammar `JsonBody` already
reads and `JsonWriter` already writes [R inventory P24], with a ceiling of its own. Stored on one
line; shown here on several, with invented and shortened values.

```json
{"v":"1",
 "id":"4B50472D5331000000070A1B2C3D4E5F",
 "seq":"7",
 "key":"q83vEjRWeJCrze8SNFZ4kKvN7xI0VniQq83vEjRWeJA=",
 "over":["4B50472D533100000006C1D2E3F40516","4B50472D5331000000055A6B7C8D9E0F"],
 "authority.9F2C…":"{\"v\":\"1\",\"ca\":\"…\",\"host\":\"…\",\"sniKey\":\"…\"}",
 "retired.51AB…":"retired",
 "client.77E0…":"v 1\nsubject …\nclient …\nadmitted …\nprofiles reader\ncertificate …",
 "revoked.C4D1…":"2027-03-01T00:00:00Z 9F2C…",
 "refused.77E0…":["2","forever"],
 "profile.reader":"1",
 "profile.writer":"2",
 "rule.0A1B…":["3","6e1f0c22a9b4d011 90aa31c2f0e7b6d5","{\"default\":\"inherit\",\"reader\":[\"read\",\"list\"]}"],
 "rule.5C6D…":["2","-","{\"reader\":[]}","2","-","{\"reader\":[\"list\"]}"]}
```

| Member | Holds |
| --- | --- |
| `v` | the format, `1`. Anything else will not read |
| `id` | the identifier of the entry this document was written into |
| `seq` | one more than the highest sequence number the writer could see |
| `key` | the store's key, 32 random bytes made with the store's first snapshot and copied into every later one |
| `over` | the identifiers of up to 32 snapshots this one's line has replaced, the newest by sequence number |
| the rest | section 4 |

**Size.** The probe's base snapshot (an authority document of 2903 characters, two records of 819,
two profiles, two rules) is 4968 characters, and the database holding it is 7.1 KB. With 22
records and 100 rules it is 37,299 characters [M probe]. The measured sizes of the real values
are the inventory's [M inventory 1].

### 3.3 The seal, and what it is for

`KPG.Seal` is HMAC-SHA256 under `key` over the UTF-8 bytes of `KPG.Store`. A snapshot **reads**
when the seal holds and `id` equals the identifier of the entry it is in.

The key sits inside the document it seals, so the seal proves nothing to anyone who can read the
file, and it is not meant to. CLAUDE.md concedes local code execution and whoever can write the
file can write a whole record [R inventory P23]. What it does:

- **Integrity against accident.** A value cut short, re-encoded or edited by hand in any program
  will not read, and is treated as damage rather than as a smaller state.
- **A copy is seen.** KeePass's File then Import preselects "Create new IDs", which duplicates
  the whole tree under new identifiers [M keepass 2.4]. A snapshot so copied reads under another
  identifier than its own, and is told apart [M probe, 12 cells].
- **What cannot read the store cannot write into it.** A snapshot of this store needs this
  store's key. An import of a crafted file, or an API route that failed to hide what section 10
  hides, can put an entry into the database but cannot make a snapshot of this store. What it can
  make is a second store under another key, and two stores serve nothing until the operator has
  chosen (section 5.2). That is the first decision of CLAUDE.md again: a file vouches only for
  itself.
- **Which store a snapshot belongs to.** Two copies of a database that each made a store hold
  two keys, as they hold two authorities today.

The store key is not a passphrase scheme and protects nothing at rest: the database's encryption
does that, as now (E10).

## 4. Where each datum lives

| # | Datum | Member | Value | Changed how |
| --- | --- | --- | --- | --- |
| 1 | the authority | `authority.<digest>` | today's `AuthorityDocument` text, unchanged | written once. Dropped by the write that retires it. The same digest under two different texts is damage, and that authority does not serve |
| 2 | a retirement | `retired.<digest>`, **and** a deletion record whose identifier is `KPG-A1` followed by the first ten bytes of SHA-256 over the digest | `retired` | only ever added; never dropped |
| 3 | an admission record | `client.<certificate digest>` | today's record text, unchanged | written at pairing; a renewal writes the new certificate's member; dropped once the certificate has expired or its authority is retired |
| 4 | a revocation | `revoked.<certificate digest>`, **and** a deletion record whose identifier is `KPG-R1` followed by the first ten bytes of SHA-256 over the digest | `<notAfter> <issuing authority>`, as today | only ever added; the member dropped once the certificate has expired, the deletion record never |
| 5 | a standing answer to renewals | `refused.<certificate digest>` | a number and `forever`, a moment, or `lifted` | each answer, and each lift, takes the next number, so a lift travels |
| 6 | the profile list | `profile.<name>`, one member a name | a number: odd means on the list, even means taken off | adding or removing takes the next number. The member is never dropped |
| 7 | an access rule | `rule.<node identifier>` | one or more versions; a version is a number, the digests of the versions it replaced (up to eight), and the document: today's `AclDocument` text, `none` for a rule taken away, or `gone` | each change is a version with the next number. The member is never dropped |
| 8 | the recycle bin markers of T049 | would be a member like any other | not designed here | |

The certificate and authority digests in member names are today's SHA-256 digests [R inventory
1.1]. Ten bytes of a second digest in a deletion record's identifier is 80 bits; a false match
refuses a certificate and never admits one.

**What `gone` is for.** A rule whose node was deleted for good (a deletion record names the node
and the node is not in the tree) is replaced, at the plugin's next write, by a version whose
document is `gone`. A node found under that identifier again, which is what a merge with a copy
that never saw the deletion leaves [M keepassxc 4], refuses there and below until the operator
gives it a rule. The rule's text is not kept; the fact that there was one is.

## 5. What is in force

### 5.1 The reading

On every request, under the hold on KeePass's thread every read already takes [R inventory P25]:

1. **Find the snapshots**: the entries of the store group, the entries in the recycle bin, and
   any the last whole pass found elsewhere. An entry is a snapshot when its identifier carries the
   mark or it holds a `KPG.Store` string.
2. **Read the deletion records**: those whose identifier carries one of the three marks.
3. **If nothing changed, nothing is parsed.** The parsed result is kept beside the identities it
   was made from: each entry's identifier and its two string objects, and the marked deletion
   records. KeePassLib's `ProtectedString` objects are immutable [R here,
   `KeePassLib/Security/ProtectedString.cs:37`], so the same object is the same text. This is the
   rule the authority's cache already follows, parsed only when its stored text changes
   [R inventory D1], and it keeps E14: a snapshot that lands takes effect at the next request.
4. Otherwise check the seals, parse, and decide the state (5.2).

**The whole pass**, over every entry of the database, runs at open, at unlock, after every save
(a synchronize ends in one [R inventory P19]), when KeePass's progress window closes, when the
KPG tab opens, and whenever step 1 finds fewer snapshots than the last pass did with no deletion
record to explain it. That an import shows the progress window is [A].

**Cost** [M probe], on a database with 5,000 deletion records and a snapshot of 37,299 characters:

| Entries | Step 1 by identifier, group and bin | Step 2 | Whole pass by identifier | Whole pass looking for the string too | Unprotect and check one seal |
| --- | --- | --- | --- | --- | --- |
| 1,000 | 0.005 ms | 0.08 ms | 0.07 ms | 0.2 ms | 0.04 ms |
| 10,000 | 0.03 ms | 0.06 ms | 0.28 ms | 2.3 ms | 0.04 ms |
| 50,000 | 0.19 ms | 0.07 ms | 2.5 ms | 22 to 30 ms | 0.04 ms |

After that the admission record, the profile list and each rule are a lookup in memory, where
today each is a read of custom data.

### 5.2 The five states

A snapshot is **standing** when it reads and no deletion record names it. It is **replaced** when
a standing snapshot lists it in `over`. The **heads** are the standing snapshots that are not
replaced.

| State | When | What the gate does |
| --- | --- | --- |
| **none** | no snapshot, no marked deletion record | no gate for this database, as today without an authority |
| **in force** | at least one head, one store key among the heads, every deleted or unreadable snapshot accounted for (below), no copy | serves the join of the heads (5.3), with the witnesses of section 6 on top |
| **undecided** | heads under more than one store key; or a copy of a snapshot under another identifier; or, after the join, more than one authority with no retirement, or one authority under two texts | `503 undecided` to every connection, through today's `UndecidedTarget` with a candidate's own host certificate [R here, `Gate/UndecidedTarget.cs`]; the operator is asked over the main window, as for several authorities today |
| **behind** | a deletion record names a snapshot that no standing snapshot accounts for; or a snapshot that will not read is neither named by a deletion record nor accounted for | `503 undecided`, the same way; the KPG tab names what is missing and offers **Use what is here** |
| **gone** | traces of a store (marked deletion records, or snapshots that are all named or unreadable) and no head | no gate, since there is no authority to answer with; the KPG tab names it; the witnesses still refuse what they refuse |

**Accounted for.** A deleted or unreadable snapshot is accounted for when a standing snapshot
lists its identifier in `over`, or when that snapshot's list is full and the identifier's
sequence number is below every number in the list. The sequence number is read from the
identifier itself, so nothing depends on a clock or on a deletion record's time.

**No new word reaches the client.** Undecided and behind both answer `undecided`, which already
means "the operator has to decide in KeePass". The API and the reference client are unchanged.

**Use what is here** is the operator's acknowledgment, on the KPG tab under the dialog's OK like
everything else there: join every snapshot that reads, copies and named ones included, write one
snapshot whose `over` accounts for everything in sight, and delete the rest. **Remove copies**
deletes the copies alone. **Keep**, for two stores, is today's choice between authorities with
the store beside each.

### 5.3 The join

The heads are folded with one operation, in any order. It is commutative, associative and
idempotent, and joining a state with any earlier state of its own line changes nothing: 40,000
checks over random histories, none failed [M probe, `out\lattice.txt`; the checks used a rule's
number alone, not the digests below].

| Datum | Join |
| --- | --- |
| authorities, records | union by digest |
| retirements, revocations | union. Nothing refusing is ever lost by a join |
| standing answers | the higher number; at equal numbers the more refusing: `forever`, then the later moment, then `lifted` |
| profile names | the higher number. Above 32 names on the list it reads as a list that will not read, which grants nothing, as today [R inventory D6, O5] |
| rules, for each node | every version of both sides, less each version another names among those it replaced, less each version whose number is lower than another's by eight or more. **What is left stands.** One version: that document is the rule. Several: the node has what every standing version allows |

**Several standing versions** is what two copies changing the same node's rule between two
synchronizations leave [M probe set C, 16 cells: both documents present, in both directions, in
KeePass's four methods and four KeePassXC releases]. The resolver asks each standing version and
keeps what all of them give: a verb or a switch counts when every version grants it, a version
that does not name the profile answers with what arrives from above, and `default` is `deny`
when any version has it. The grant editor shows the versions and one OK settles it with a version
that names them all. `gone` among the standing versions refuses.

**An old snapshot beside a new one changes nothing.** That is what a KeePassXC merge of a stale
copy into the current one leaves [M probe sets U, R, D]. The old one is either named by the
deletion record its replacement left, or listed in `over`, or, when neither, joined with its own
descendant, which is no change. For that last case the design never drops a member whose loss
could widen: a rule taken away stays as `none`, a profile taken off stays as an even number.
What a prune drops (an expired record, an expired revocation, a retired authority's document) is
refused on its dates or by the retirement if an old snapshot brings it back [M probe set K: the
records a recreated authority dropped came back from the other copy, under a retirement that
held in all 16 cells].

## 6. The witnesses

**Replaced snapshots.** Deleting a replaced snapshot leaves the deletion record KeePass's own
delete leaves. Under KeePass's Synchronize that record travels and deletes the old snapshot in
the other copy when it is older than the record [M probe set U; R here,
`KeePassLib/PwDatabase.cs:1045-1096`]. Under a KeePassXC merge the old snapshot comes back into
the copy that deleted it, beside its record [M keepassxc 4; M probe], and reads as named, so it
is not standing.

**A snapshot deleted by hand.** The record names a snapshot nothing replaced. Measured, with the
newest snapshot deleted for good in copy A and a stale copy B still holding the one before it
[M probe set H, and the same by `keepassxc-cli rm` twice]:

| Merge | Result |
| --- | --- |
| KeePass, every method but Create new IDs, into A | gone: B's older snapshot arrives and A's own record deletes it |
| KeePass Synchronize into B | gone: the records travel |
| KeePassXC, four releases, into A | gone: B's older snapshot comes back and is named |
| kdbxweb 2.1.1, both directions | gone |
| KeePass's three import methods and KeePassXC, into B | B as it was: nothing arrives, because A holds no snapshot any more. B never saw the newer state |

**Revocations and retirements, a second time.** The deletion record derived from the certificate
or the authority is asked on every request beside the join: a certificate is revoked when the
join holds its revocation **or** the file holds its witness. In every "gone" row above, the
revoked certificate was still refused by its witness alone [M probe]. A deletion record that
names nothing survives a KeePass save [M keepass X4], a KeePassXC save in four releases, a
pykeepass save and a kdbxweb save [M probe], and KeePass's merge never drops one whose object is
absent [R here, `PwDatabase.cs:1067-1096`: a record is dropped only when its object is found and
is not older].

**What clears witnesses.** KeePass's Database Maintenance, "Remove deleted objects information"
[R keepass 5]. The plugin's next write puts back the witness of every revocation and retirement
in the join and the record of every snapshot in `over`. Until then the snapshot alone carries
them, which is the state of every other option's single carrier.

## 7. Writing

### 7.1 The one write

`DatabaseWriter` stays the only writer, on KeePass's thread, under its lock. Every change is the
same operation:

1. Read with a whole pass. Refuse unless the store is none or in force; the operator's Keep, Use
   what is here, Remove copies, and Create over a store that is gone are the writes that resolve
   the other states. The first write of a store makes its key.
2. Take the join, apply the change, prune: expired records and revocations, the records of
   retired authorities, a retired authority's document, standing answers no record admits, and
   `gone` for rules whose node a deletion record names.
3. Make the snapshot: `seq` one above the highest in sight, the store's key, `over` from the
   snapshots it replaces and their own lists, cut to the 32 newest. Add the entry to the store
   group, making the group if it is missing.
4. Delete every other snapshot in sight, each with a deletion record unless one names it already.
   Add the witnesses.
5. **A write that must be in the file before it is answered** (a pairing, a renewal, a client
   revoking itself, a standing answer) is saved and believed exactly as today: `Modified` false
   and the file hashed back against `HashOfLastIO` [R inventory P13]. If it did not land it is
   taken back out: the new entry removed, the deleted entries put back where they were, the
   deletion records this write added removed. E7 is unchanged.
6. **A write from a dialog** (the settings tab's OK, the grant editor's OK) changes the database
   in memory and waits for the operator's save, as today.

Two writes in one second are harmless. The record of the first write's snapshot carries the time
of the second, the same second as that snapshot's own time, so in another copy KeePass keeps the
snapshot and drops the record [R here, `PwDatabase.cs:1083-1096`]. It is then standing, listed in
`over`, and replaced. Nothing rests on a time.

Nothing is written at open, at unlock, or at somebody else's save, with the one exception that
exists today: owed standing answers ride every save at `FileSavingPre` [R inventory P18, O4].
They become a store write inside that save. O1 holds.

### 7.2 The settings tab

`PendingDatabase` keeps buffering until the dialog's OK (O2). What it buffers changes from keys
to changes of the state: make an authority, retire, revoke, lift an answer, add a name, remove a
name. At OK they are applied in one write to the join as it is at that moment. A pairing that
landed while the dialog was open is a snapshot written meanwhile, so it is in that join. A name
the operator removed takes the next number if it is on the list then; a name the operator added
takes the next number if it is off. That is "replayed as a difference" for everything, and the
reconcilers by kind in `PendingStore.Reconcile` have nothing left to do.

### 7.3 The grant editor

The editor stops writing into the dialog's private copy, `m_sdCustomData`. It keeps the change on
its own tab and applies it when the group or entry dialog has closed with `DialogResult.OK`, the
way `AuthorityPanelFactory` applies the settings tab, and after KeePass has put a new node into
the tree, which for Add Entry is after the dialog closes [R here, CLAUDE.md, "A dialog that is
adding something"]. Cancel discards it. O3 holds by other means, and the editor still never
writes the same rules in other words: an OK that changed nothing writes nothing.

What goes with the private field: the reflection in `NodeDialog`, the contract entry for the
field's name, and the state "a KeePass that renames it leaves the editor refusing to change
rules". A history revision has no rule of its own any more, and restoring a revision does not
touch the rule.

### 7.4 What changes in the plugin

| Class | Becomes |
| --- | --- |
| `CustomDataStore` | goes. `IKeyValueStore` stays as the seam, answered from the join for reads and collecting changes for one snapshot for writes |
| new: the store reading | finding, seals, states, join, and the cache of 5.1; one class, tested without KeePass over the seam |
| `AuthorityKeys` | the member names, and the three identifier marks |
| `AuthorityStore` | same decisions (written once, retirements decide, several unretired serve nothing) over the join; `Retire` also adds the witness |
| `AdmissionStore` | `Find` is a lookup in the join plus the witness; `Remove` adds the revocation and its witness; the prunes that undo a merge (a record back beside its revocation) go, since the join already refuses it |
| the prune | still rides only on the plugin's own writes (O1), as step 2 of 7.1: it drops members from the snapshot being written, and nothing depends on its having run, because an old snapshot that brings a dropped member back is refused on its dates or by a retirement |
| `ProfileNames` | the list is the names with odd numbers; `Merge` goes, the join does it |
| `RenewalRefusal` | answers are numbered; a lift is an answer |
| `PendingDatabase`, `PendingStore` (`Ui/PendingChanges.cs`) | buffer changes of the state; apply them in one write at OK; the reconcilers go |
| `DatabaseWriter` | still the single writer; the save and its verification unchanged; the take-back is the one in 7.1; the refusal while Database Maintenance is open goes for the store (below) |
| `NodeDialog`, `IGrantStore`, `GrantPanelFactory` | 7.3 |
| `KdbxNodes`, `DatabaseNodes` (`IRuledNodes`) | a node's rule is read from the join by the node's identifier |
| `AclDocument`, `AclResolver` | the walk is unchanged; a node with several standing versions answers with what all allow; `gone` refuses |
| `GroupDialogs` | keeps its watches for the vault's own writes. For the store: Database Maintenance no longer holds a copy of anything the plugin keeps, so the measured undo of a recreate under that dialog [M spike 4] cannot happen; the one dialog over the store is an entry dialog open on a snapshot, and the writer answers `503 busy` while one is |
| `OpenDatabaseTargets` | builds its targets from the reading; `UndecidedTarget` also answers for undecided stores and for behind |
| `AuthorityChooser` | also asks about two stores, copies and a store that is behind |
| `Expected.OfCustomData` | the store's own failures |
| `KdbxVault` | hides the store (section 10) |

## 8. What a save and a merge do to it

### 8.1 Saves

| Program | What a save does to the store | Basis |
| --- | --- | --- |
| KeePass 2.62 | keeps the entry, both strings and the protection flag, and deletion records that name nothing; writes KDBX 4.0 | [M probe; M keepass X4, X7] |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | keeps all of it: after `keepassxc-cli add` the seal held on a 5 KB and on a 1 MiB document, and the snapshot records and witnesses were all there; adds four empty standard fields to the entry, and 2.7.12 and 2.8.0 add two keys of their own to the database's custom data; writes 4.0 | [M probe, 8 files] |
| KeePass 2.47 | keeps all of it, in a file it writes as KDBX 3.1 (AES-KDF) | [M probe, 3 files] |
| Keepass2Android 1.15-r6 | keeps custom strings with their flag and the deletion records | [R android 2]; not run |
| KeePassDX 4.5.8, 3.2.0 | the same; gives the root group the time of the save, which nothing here reads | [R android 2; M spike 6 for the root] |
| KeePassDroid 2.7 | the same. It is the program that deletes every rule today; it kept every entry in every file it saved | [R android 2; M spike 6 for entries] |
| kdbxweb 2.1.1 (KeeWeb) | keeps all of it. The file holds no timestamped custom data of the plugin's, so the plugin no longer makes the file KeePass cannot open after a KeeWeb save | [M probe; M others for the defect] |
| pykeepass 4.2.0 | keeps all of it | [M probe] |
| KeePassium, Strongbox, MacPass | carry strings, flags and deletion records in their models; Strongbox trims history at every save, and there is none | [R others 3]; not run |

No save in any program measured moved an entry's time, dropped an entry or removed a string
[M measured 4.1], and none of that would matter here except the last two.

### 8.2 Merges

Seven sets of two copies, each merged in both directions: KeePassLib 2.62 in five methods (70
cells), `keepassxc-cli merge` in four releases (56 cells), kdbxweb 2.1.1 on five sets (10 cells)
[M probe]. Sets: **U**, A revoked a client and so replaced the snapshot, B is stale. **C**, both
wrote: A paired a client and changed a rule, B revoked a client, changed the same rule and took
a profile off. **H**, as U, then the new snapshot deleted by hand in A. **R**, as U, with the
snapshot in the recycle bin. **D**, as U, with the old snapshot's text edited by hand in B.
**X**, each copy made a store of its own. **K**, A recreated the authority while B paired a
client under the old one.

| Merge | Result | Basis |
| --- | --- | --- |
| KeePass Synchronize, OverwriteIfNewer, OverwriteExisting, KeepExisting; sets U, C, R, D, K | in force in all 40 cells, **the same joined state in both directions**, holding every revocation, the retirement, the profile taken off and both rule documents. Synchronize also carries the deletion records and removes the replaced snapshot from the other copy; the three others leave it there, replaced | [M probe] |
| KeePassXC 2.4.2 to 2.8.0; sets U, C, R, D, K | in force in all 40 cells, the same joined state as KeePass's. A replaced snapshot comes back beside its record and is named. No deletion record is copied | [M probe] |
| either, set X | undecided in all 16 cells: two stores | [M probe] |
| either, set H | section 6 | [M probe] |
| KeePass Create new IDs, all sets | undecided in 12 of 14 cells (a copy); behind in one (the copy was the unreadable snapshot of set D); in the fourteenth the source held no snapshot, so nothing of the store was copied and the target stayed as it was | [M probe] |
| a KeePassXC result, then KeePass Synchronize with the other copy | the same joined state as either merge alone (sets U, C, H; 2.4.2 and 2.8.0; 12 runs) | [M probe] |
| any of the above, then the plugin's next write | one snapshot, the same state, the others deleted and recorded (5 runs) | [M probe] |
| kdbxweb 2.1.1, sets U, C, H, D, K | the same outcomes as KeePass's Synchronize in all 10 cells; it unites the deletion records and removes whatever they name, whatever the times | [M probe; R others] |
| Keepass2Android | a port of KeePassLib's Synchronize: an entry only the arriving copy holds is added to the group of the same identifier, or the root; the deletion lists are united and applied | [R android 1.5, 3.1]; **not run** |
| KeePassDX 4.5.8 | an arriving entry is added and a missing group recreated; arriving deletion records are applied to entries older than the deletion and then not kept. Its equal-time rules, its move back on equal times and its custom data merge touch nothing here, since no snapshot is ever changed and place means nothing | [R android 1.5, 3.1, 5.4]; **not run** |
| KeePassDX 3.2.0 | an arriving entry whose group the open copy lacks is dropped; groups are merged before their entries, so the store group is there | [R android 1.5]; **not run** |
| KeePassDroid | has no merge | [M spike 6] |
| Strongbox, KeePassKit, keepass-rs | add a node the open copy lacks | [R others 4]; not run |

Slow and fast key derivation gave KeePassXC the same result for every group and entry in the
ground report's runs [M keepassxc 4]; the timing that decides today's custom data merge has
nothing to decide here.

## 9. What a person sees, what they can break, and what the gate does

**What is visible.** A group `KeePassGate` at the root holding one entry, `KeePassGate store`,
with a hidden field of 5 to 40 KB and a short one. After a merge of two copies that both wrote,
two entries, until the plugin's next write. After a KeePassXC merge, a replaced entry may sit
beside the current one. No history, no attachment, nothing on any other node. KeePass's "Large
entries" report lists it [R keepass 9]. KeePassXC's HTML export prints protected custom strings
in clear [M keepassxc 8], so the authority's key would be in such an export, as it is in KeePass's
XML export today [R inventory P26]. Both are the operator's own act.

| What a person does, in any program | What the file then holds | What the gate does |
| --- | --- | --- |
| deletes the entry to the recycle bin | the snapshot, in the bin | in force [M probe, `rm` once; set R, 16 cells]; the tab names where it is and offers to put it back |
| deletes it for good, or empties the bin | a deletion record naming it | gone: no gate. Revoked certificates stay refused by their witnesses [M probe]. A stale copy merged in afterwards does not bring the store back into force (section 6). On the tab, Create starts a new store, which accounts for the old records |
| edits `KPG.Store` or `KPG.Seal` | a snapshot that will not read | behind, or gone if it was the only one. If a standing snapshot had replaced it, nothing [M probe set D, 16 cells] |
| edits the title, the notes, the icon; adds a field | a new time and a history item | nothing: the seal covers the document alone [M probe for the title, `keepassxc-cli edit`] |
| moves the entry to another group | the snapshot elsewhere | in force [M probe, `mv`] |
| duplicates the entry, or copies and pastes it | a copy under another identifier | undecided until Remove copies |
| deletes, renames or moves the group | entries in the bin, or gone, as above; the name and place mean nothing | as above |
| imports the database, or a copy of it, into itself with Create new IDs | a copy of the snapshot, and a copy of every node, none of them ruled | undecided [M probe]. Closing without saving is the better remedy |
| then runs Delete Duplicate Entries | **the original deleted, the copy left** | still undecided [M probe: 5 entries removed, the snapshot under its own identifier among them]. Use what is here adopts the copy; the duplicated nodes are the operator's to remove |
| runs Delete Empty Groups | nothing: the group holds an entry while a store exists | nothing [R keepass 3] |
| runs Database Maintenance, "Remove deleted objects information" | no deletion records | in force; the witnesses are back with the plugin's next write (section 6) |
| deletes history, lowers the history limits | nothing | nothing: there is no history |
| restores an older file and synchronizes it with a current copy | the old snapshot beside the current one | in force, the current state |
| restores an older file and carries on alone | the old store, whole | in force, the old state. Section 14 |

## 10. Keeping the API and the reference client out

E8 is structural today, because no route touches custom data [R inventory P15]. Here it has to be
made, in one place, below every handler:

- **The store group is as absent as the recycle bin**: not listed, not readable, not writable,
  not addressable, not counted, and a request naming it answers the 404 any refused node answers.
  So is every entry whose identifier carries the snapshot mark or that holds a string named
  `KPG.Store`, wherever it is, the root included, and so the flat sweep from the root.
- **No route makes, renames, moves into place or changes a string whose name begins `KPG.`**, on
  any entry.
- The exclusion sits in the vault's own enumeration and lookup (`KdbxVault`, `KdbxNodes`), not in
  handlers, for the reason the router asks about rules before any handler runs: a handler could
  forget.
- **The second line is the seal.** If a route did let a client write such a string, what it wrote
  would not read as a snapshot of this store. The worst it could then do is make the store read
  as behind or undecided, which stops the gate and widens nothing.
- **There is no second line for reading.** A route that leaked a snapshot would leak the
  authority's key. That is the price of any store the tree holds, and the reason the exclusion
  is in one place and tested there.

`openapi.yaml` does not change, no status or word is added, and `PyKeePassGate` has nothing new
to cover. The client's reference resolver follows references over what the gate serves, so it
never meets the store. In KeePass itself a field reference that searches by text ignores the
searching switch [M keepass X11]; one written to match text inside the store entry would resolve
to that entry's fields, which is the operator's own placeholder in the operator's own KeePass.

A node's tag key is derived from the host PKCS#12 as the database stores it [R inventory 1.3];
the authority document's text is unchanged, so no tag changes.

## 11. The KDBX version it needs

**4.0.** Every file the probe, the four KeePassXC releases, pykeepass and kdbxweb wrote was KDBX
4.0 [M probe]. Nothing here is a 4.1 feature: no per-key time, no group tag, no previous parent.
The decision "The authority takes the file to KDBX 4.1" is reversed, and with it goes the
dependence on per-key times that KeePassXC, KeePass 2.47 and Keepass2Android drop [M spike 5, 6].

**It survives being written as 3.1.** Keepass2Android and KeePass 2.47 write KDBX 3.1 for a file
with AES-KDF and no node custom data [M measured 3.10], and with this design the plugin puts no
custom data anywhere. Measured: KeePass 2.47's KeePassLib saved three AES-KDF files holding the
store as KDBX 3.1, and KeePassLib 2.62 read them back with the seal holding on a 5 KB and on a
1 MiB document and every snapshot record and witness there [M probe, `out\keepass247.txt`]. That
Keepass2Android's 3.1 writer does the same is [A]; it is a port of KeePassLib [R android 0].

## 12. Against the requirements

| Requirement | With this design |
| --- | --- |
| E1 fail closed | every state but "in force" serves nothing; an unreadable snapshot is never read as a smaller state |
| E2 the record is load bearing | unchanged: no record in the join, no admission |
| E3, E4 a revocation and a retirement survive | two carriers that fail under different programs: the snapshot arrives in every merge measured, and the witness outlives the snapshot. Lost with the whole file, or to a deletion that leaves no record in a file that never held the witness (section 14, points 3 and 4) |
| E5 a narrowing is never undone by another program | no program can change one rule or one profile; the join never prefers an older number; a copy of a node is the exception |
| E6 the loss of a rule is detectable or refuses | a rule cannot be lost apart from its snapshot, and the loss of a snapshot is behind, gone, or the older state of a file that never saw the newer |
| E7 no credential before its record is in the file | unchanged |
| E8 the API reaches none of it | section 10: made, not structural |
| E9, E10 the database is the trust domain; secrets under its encryption only | unchanged; KeePass now holds the store's text as a protected string, where custom data was a plain one [R inventory H6] |
| E11 a dialog's stale write-back | no KeePass dialog holds a copy of the store; the entry dialog over a snapshot is refused while open |
| E12 no silent choice between authorities | unchanged, and extended to stores |
| E13 the plugin moves no node time | it touches its own entries only |
| E14 live reads | section 5.1 |
| A1, A3 pairings and profile names from two copies both survive | yes [M probe sets C, K] |
| A4 the later standing answer wins; the loser refuses | by number, not by time |
| A5 bounded growth | the snapshot is bounded as today; deletion records are not (section 14) |
| A8 damage is shown on the tab | the five states, each named there |

## 13. Settled decisions: reversed and kept

**Reversed, to be recorded as reversals.**

| Decision in CLAUDE.md | What becomes of it |
| --- | --- |
| "stored inside itself under `KPG` keys in the database's own `CustomData`" | the place is reversed: one entry. The principle stays: every database is its own authority, and no host registry |
| "The database's own `CustomData`, never the root group's", with one key an authority, keys only ever added, `KPG.Revoked.<key>`, `KPG.RetiredAuthority.<fp>` | the keys become members of the snapshot. "What has to survive a merge is a key that is only ever added" becomes "an entry that is only ever added, and a deletion record". `503 undecided` stays and covers more |
| "The authority takes the file to KDBX 4.1" | reversed: 4.0 |
| "The grant editor writes into the dialog's own copy", `m_sdCustomData` | reversed: its own buffer, applied when the dialog closes with OK |
| "one document per node … Grants live under a `KPG` key" | the document and its meaning stay; it lives in the snapshot. Added: several standing versions give what all allow, and `gone` refuses |
| "Replayed key by key", "`KPG.Profiles` is replayed as a difference", the merge for `KPG.RenewalRefused.` keys | the tab still waits for OK; what is replayed is changes of the state, and the join replaces the reconcilers |
| The trap section: `GroupForm` and `DatabaseOperationsForm` taking copies of custom data | stays true of KeePass and stops being about the plugin's data. "Testing any `CustomData` work means pressing OK" becomes a rule about the grant editor's buffer |
| "an entry's history holds the rule each revision had" (`DESIGN.md` 972-984) | goes: a rule is not in an entry |
| The measured fact about KeePass's custom data merge | stays true and stops being load bearing |

**Kept.** The admission record is load bearing, read on every request. Claims carry profile names
only. The authority key has no passphrase. Pairing tokens, held approvals and session answers
stay in memory. Two host certificates. The transport, the routing table, the verbs, "nearest
wins, per profile", refused and absent answering the same 404. One JSON grammar and one escaper:
the snapshot goes out through `JsonWriter` and comes in through `JsonBody`. Everything on the
settings tab waits for OK, revocation included. Every write is saved, read back and hashed.
Opening a database changes nothing (O1). Owed answers ride every save (O4). Local code execution
is conceded, and the seal claims nothing against it.

**For the project to decide**: the wording and place of Use what is here, Remove copies and the
choice between two stores; whether a snapshot in the recycle bin should serve, as designed, or
refuse; O7, whether a database made with 0.1.0 is carried over.

## 14. What it does not solve

1. **A copy of a ruled node.** Duplicate, copy and paste, a template, an import with new
   identifiers of a file that holds no store: the copy has a new identifier and no rule, and is
   governed by where it sits. The gate sees only an import that copied the store. Granting
   narrowly and sealing little is the operator's defense; the design has none.
2. **The cliff.** One hand deletion in any program, synchronized, stops the gate for the
   database in every copy until the operator has pressed Create or Use what is here in KeePass,
   and after Create every client pairs again. The other options lose one record at a time.
3. **A deletion with no deletion record** of the newest snapshot, in a file that still holds an
   older one: the older state is in force and nothing shows it [M probe: pykeepass, on a file
   that had taken the newer snapshot through a merge that carries no deletion records; the
   revoked certificate was no longer refused]. Where the file holds the witness, the revocation
   still holds: measured for a file whose only snapshot was deleted that way [M probe]; for a
   file that also holds an older snapshot it follows from the reading and was not run [A].
4. **A whole stale file**: Keepass2Android's "No, overwrite", a KeePassDX save over a file whose
   time moved less than ten seconds [M spike 6], every KeePassDroid save over a changed file, a
   synchronizer's conflict copy chosen, a backup restored [R android 5.5; R others]. The old
   store is whole and reads as in force. No store inside the file survives this; a copy that
   holds the newer snapshot, merged later, puts it right.
5. **A copy that never received a change does not know it.** A KeePassXC merge writes one file;
   KeePass's import methods write one. The other file is as stale as before.
6. **Two copies changing one node's rule**: both documents stand and the node has what both
   allow until the operator settles it, which is a refusal with nobody at fault. If one copy
   changed that rule eight or more times while the other changed it once, between two
   synchronizations, the other's change is dropped unseen.
7. **A fork older than `over` reaches.** A copy that diverged, had its head deleted by hand, and
   is merged after the main line has written more than 32 times: the deletion record is taken as
   accounted for.
8. **Deletion records only grow.** One a write, one a revocation, one a retirement, about 100
   bytes each before compression. KeePass never ages them [R keepass 5]. A client renewed weekly
   is 52 a year.
9. **The field in other programs' entry views.** Tens of kilobytes behind a "show" button;
   nothing measured about what an Android entry view does with it.
10. **Witnesses cleared in KeePass** until the plugin's next write (section 6).
11. **Two stores** after two copies each created one: the operator keeps one and the other's
    clients pair again, as with two authorities today.
12. **The exports**, section 9.

## 15. A migration, in one paragraph

Not designed. It would take: reading today's keys and every node's rule once, writing them as
the first snapshot with every number at 1, and removing the old keys and the rules on the nodes
in the same write, on the operator's press on the KPG tab. A stale copy merged later would bring
old custom data back, which nothing would read any more; a stale copy opened by an older plugin
would serve its old data, so the old plugin has to be gone from every machine first. Whether any
of this is owed at all is O7: 0.1.0 is released, 0.2.0 is not, and the precedent is that an older
stored shape reads as nothing.

## 16. What was run for this report

All from a console, no window, invented content only, modest load (the whole matrix takes under
two minutes). Nothing under the worktrees or the main checkout was touched.

- **The probe**, `Wc.exe`, net48 against KeePassLib 2.62 loaded from
  `.keepass\2.62\KeePass.exe`. It implements the store as designed with three
  differences that change no result: the document and the seal are one string with a line break
  between, the identifier marks are four bytes, not six, and a rule carries its number without
  the digests of what it replaced. `make` writes the seven sets; `kpmerge` runs
  `PwDatabase.MergeIn` and saves; `sum` and `dump` read a file the way the gate would and print
  the state; `write` is the plugin's next write; `dde` is KeePass's `DeleteDuplicateEntries`;
  `big` makes a 1 MiB document; `bench` and `lattice` are sections 5.1 and 5.3.
- **KeePassXC** 2.4.2, 2.6.6, 2.7.12 and 2.8.0: `keepassxc-cli` only (`merge -s`, `add`, `rm`,
  `edit`, `mv`, `ls`), from the archives the `keepassxc` ground report verified against the
  spike's pins; each binary's Authenticode signature was checked again in this run
  (`out\summary.txt`, top).
- **pykeepass 4.2.0** through `uv run --no-project --with pykeepass python -I -X utf8`: a resave,
  and a deletion that leaves no record.
- **kdbxweb 2.1.1**, the module the `others` ground report installed, over copies of the sets
  made with AES-KDF (`work-kw`).
- **KeePass 2.47**, the archive the spike cached, checked again against the spike's pin and its
  Authenticode signature, its KeePassLib loaded by the spike's own `Save-WithKeePass.ps1` under
  Windows PowerShell 5.1: a plain save of three AES-KDF files (`work-kp247`,
  `out\keepass247.txt`).
- Raw results: `out\summary.txt` (one line a file), `out\merges.txt` (every snapshot, record and
  joined state of every file).

## 17. Measurements to make before this design could be chosen

Most important first. The first three decide it, and they need the desktop or the emulator,
which this run could not use.

1. **The Android programs, on the emulator, with the probe's own files.** Sets U, C and H through
   Keepass2Android 1.15-r6 and KeePassDX 4.5.8 and 3.2.0 merges in both directions; a plain save
   of U-A and of the 1 MiB file in those and in KeePassDX 3.3.0 and KeePassDroid 2.7. Read back
   with `Wc.exe sum`: does the seal hold, are the deletion records that name nothing still there,
   does an arriving snapshot arrive, is a replaced one removed. Every Android cell in section 8
   is [R] today. The spike's flows fit as they are.
2. **What each program's entry view does with the field**: KeePassXC's window, Keepass2Android,
   KeePassDX, KeePassDroid, opening the store entry with 5 KB, 40 KB and 1 MiB in it. This
   decides between the protected string and an attachment as the carrier.
3. **KeePass's own window**: a real File then Synchronize; File then Import with Create new IDs,
   then Delete Duplicate Entries; Database Maintenance's removal of deleted objects; the entry
   dialog open on a snapshot while the plugin replaces it, then its OK; that an import shows the
   progress window the whole pass waits on. The merge cells stand for the first two; the dialogs
   were not driven.
4. **A KDBX 3.1 save by Keepass2Android** of a file with AES-KDF and the store in it (KeePass
   2.47's is measured, section 11), and a merge of such a file in each program.
5. **The join with the digests of replaced versions**: the laws again, three and more copies in
   every order, and histories longer than `over` and than a rule's eight.
6. **The reading inside the real plugin**: per request and at the whole pass, on the operator's
   size of database, under the hold on KeePass's thread.
7. **KeePassXC's other merges**: from its window, on reloading a file changed on disk, a KeeShare
   import of a share that holds the store group, 2.8.0's remote sync, and 2.7.0 to 2.7.11.
8. **Strongbox, KeePassium and MacPass**: a save of the probe's files, and Strongbox's and
   MacPass's merges of sets U, C and H.
9. **Identifiers with a mark**: KeePass's and KeePassDX's repair of duplicate identifiers at
   load, which gives one entry a new identifier and so makes a copy; and whether anything
   rewrites an identifier it finds unusual.
10. **The API's exclusion**, once written: every route against the store group, a stray snapshot
    in a user's group, and a field named `KPG.Store` on a client's own entry.
