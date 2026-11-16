# Option `versioned-fields`: every write a JSON document in a field named from its time, reconciled when read

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. This is the timestamp-named-field
proposal: JSON stored inside an entry field whose name is derived from a timestamp, so that a
merge would preserve both documents, with a path for groups (a meta entry inside the group or a
global meta entry for all KPG data) and a preprocessing step when loading to take care of merge
issues. One design, the strongest form of that idea I could find, covering all eight data.

## How statements are marked

- **[M here]**: measured for this design, today, from a console: KeePass 2.62's KeePassLib and
  `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 over invented databases, with the design's own
  reading rule applied to every result. Section 14 describes the runs and names the files.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in the ground report
  of that key (`ground-keepass.md`, `ground-keepassxc.md`, `ground-others.md`), or recorded by
  `spikes/revocation-merge` as `ground-measured.md` and `ground-inventory.md` digest it.
- **[M inventory]**: measured by `ground-inventory.md`'s own probe (its section 8).
- **[M store-entries]**, **[M rules-central]**: measured by the probe of `option-store-entries.md`
  or `option-rules-central.md`.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[R 2.62]**: read by me in `KeePass-2.62-Source.zip`, the copy the `keepass` ground report
  verified. **[R plugin]**: read in the review4 worktree at `a42e38aa`.
- **[A]**: assumed. Neither run nor read. Every assumed statement about another program comes back
  in section 15 as a measurement to make.

## 1. The point the idea stands or falls on

**No program merges an entry field by field.** When the same entry was changed in two copies, one
version is taken whole and the other is put into the entry's history, if it is kept at all:
KeePass 2.62 in 120 of 120 cells [M keepass 1a], KeePassXC in four releases [M keepassxc 5],
kdbxweb [M others 4], Keepass2Android and KeePassDX [R android 1.1], Strongbox, KeePassKit and
keepass-rs [R others 4]. So two fields added to one entry in two copies do **not** both survive in
the entry. The idea as worded does not hold.

What a step at load could find afterwards is the entry's history, and history is not a store:

| What happens to the losing version | Where | Basis |
| --- | --- | --- |
| Lost outright when both versions carry the same second: no history item at all, and which side is lost depends on the program and the direction | KeePass, KeePassXC, kdbxweb; KeePassKit, Keepass2Android and KeePassDX | [M keepass 1a; M keepassxc 5; M others 4]; [R others 4; R android 1.1] |
| Never put into history when the winner already has ten newer history items | KeePassXC, every release, with exactly the proposed shape | [M keepassxc 4, case `meta`] |
| Trimmed to 10 items or 6 MiB at every load, save and merge | KeePass | [M keepass 1b] |
| Trimmed at every load | Keepass2Android | [R android 1.3] |
| Trimmed at every save | Strongbox | [R others 3] |
| Deleted by age (365 days) by Database Maintenance | KeePass | [R keepass 1b] |
| Lost when the operator set the history limit to 0, or imported with Keep existing | KeePass | [M keepass 1b, 1a] |

KeePass's own report measured the proposal as worded, one shared entry and a step that copies
fields back out of history: all 24 fields survived twelve synchronized rounds only when the two
copies wrote at least a second apart, history was kept and the step ran between KeePass's merge
and its save; with writes in the same second half the fields were lost, with or without the step
[M keepass 1e].

**A revocation cannot rest on that.** There are three ways on, and this design takes the second
and a carefully limited part of the first:

1. *Read the history as well.* Rejected as the store, for the table above. Kept as one more place
   a version may be found, and only for versions that close something (section 5.2): history can
   then refuse, and never admit.
2. *Put the timestamp in the name of an entry.* Every write is an entry of its own, written once
   and never changed. Two entries added in two copies both arrive in every merge measured: KeePass
   in all five methods [M keepass 1d], KeePassXC in four releases [M keepassxc 5], kdbxweb
   [M others 4], and in every one of the 160 merges run for this design [M here].
3. *Both.* That is this design: the entry is the unit a merge keeps, and the timestamp-named field
   inside it is the unit of identity, so that one version can sit in several entries and still be
   one version.

**What is kept of the idea**: every write is a JSON document in a string field named from the
time of the write; nothing is changed in place; two copies that both wrote leave two documents;
what the gate serves is worked out from every version found; entries carry such a field
themselves, and a group gets a meta entry inside it. **What is changed**: a write gets an entry
to itself instead of a field in a shared entry; the step "at load" writes nothing and runs at
every read; and the carrier on a node is a second witness, never the only one.

## 2. The design in one page

1. **A version is one string field**, named `KPG.<time>.<origin>.<number>`, holding one flat JSON
   object. It is written once and never changed. Its name is its identity: the same name is the
   same version wherever it is found.
2. **A write is one new entry** in the plugin's own group, holding the versions that write made.
   A write entry is never changed afterwards. So no write entry ever exists in two versions, and
   no modification time, history item or tie decides anything.
3. **Whatever closes something is written twice.** A revocation, a retirement, a profile's
   removal and a rule change are written as two entries with the same versions, and those two
   entries carry everything in force at that moment, so that the write can delete every older
   write entry. Whatever only opens (a pairing, a profile's addition, a new authority) is one
   small entry beside them.
4. **A rule also sits on its node**: a copy of the newest version in a field of the ruled entry,
   and in a small meta entry inside a ruled group. That copy travels with the node and with its
   copies, and a copy of a ruled node refuses until the operator has settled it.
5. **Later means "written knowing it"**, never "stamped later". A version names the versions of
   the same subject it replaces. Two versions that do not name each other were written without
   knowledge of each other, whatever their clocks said, and then the datum's own rule decides: a
   refusal wins, a removal wins, and of two rules the gate applies what both allow.
6. **What opens counts only where it was written; what closes counts wherever it is found.** A
   record, a profile's addition or an authority counts only from a field of an entry that no
   deletion record names. A revocation, a retirement, a removal or a rule counts from any entry,
   its history, the recycle bin, or a node.
7. **The plugin deletes with a mark.** Its own deletion records carry the time in the entry's
   identifier plus one second. Any other deletion record that names a write entry means somebody
   deleted KeePassGate data by hand, and the gate serves nothing until the operator has looked.
8. **Every version carries the database's own store identifier**, 32 random hex digits that no
   file made elsewhere knows. Versions of another store count for nothing, so an import or a
   merged file cannot plant a refusal or a rule.
9. **Nothing is written when a database is opened, read or merged.** The reconciling rule is a
   computation at read. Tidying rides on the plugin's own writes.
10. **The API cannot see or make any of it**: the plugin's entries are as absent as the recycle
    bin, and no route lists, reads or writes a string whose name begins `KPG.`.

## 3. A version

### 3.1 Its name

`KPG.<time>.<origin>.<number>`, for example `KPG.20261115T101502Z.D110E16556F73DB5.01`
[M here, `out\marks.txt`].

| Part | What it is | What it is for |
| --- | --- | --- |
| `<time>` | the UTC second of the write, `yyyyMMddTHHmmssZ` | the timestamp-named-field idea: a person reading the entry in any program sees the versions in the order they were written. **Nothing decides by it** |
| `<origin>` | 16 hex digits: the 8 random bytes of the identifier of the entry the version was first written in | uniqueness across copies. Two copies writing in the same second differ here |
| `<number>` | `01`, `02`, ... within one write | a pairing that also names a profile writes two versions |

The entry a write makes has the identifier `367B11D1`, then 4 bytes holding the same second, then
those 8 random bytes. `367B11D1` is the first four bytes of SHA-256 over `KPG/1/write`
[M here, `out\marks.txt`]. So a write entry is recognized by four bytes of its identifier, a
deletion record that names one is recognized too, and the time inside the identifier is what the
plugin's own deletions are stamped from (section 6.3).

### 3.2 Its content

One flat JSON object whose members are all strings, which is what `JsonBody` reads and
`JsonWriter` writes today [R inventory P24]. No new grammar and no second escaper.

| Member | In every version | Content |
| --- | --- | --- |
| `v` | yes | `1`. A version of another number makes the gate serve nothing, as a newer plugin's data must |
| `db` | yes | the store identifier: 32 hex digits drawn at random with the first write into a database that holds none |
| `id` | yes | the name without `KPG.`. A field whose name and `id` disagree does not read |
| `kind` | yes | one word from the table below |
| `after` | where a subject can have several versions | the word `none`, or 16-digit hashes separated by one space: every earlier version of the same subject that the writer held, whatever its kind, and everything those named. A hash is the first 8 bytes of SHA-256 over the version's `id` |
| `sum` | yes | the first 8 bytes of SHA-256 over the other members, so a field changed by hand does not read |

| # | Datum | `kind` | Other members | Opens or closes | Size |
| --- | --- | --- | --- | --- | --- |
| 1 | An authority | `authority` | `authority` (the certificate's SHA-256), `doc` (today's `AuthorityDocument`, unchanged). The field is a **protected** string | opens | 3,063 chars with a stand-in document of today's size [M here] |
| 2 | An authority's retirement | `retire` | `authority` | closes | about 200 |
| 3 | An admission record | `admit` | `certificate` (its SHA-256), `doc` (today's record, unchanged line format) | opens | 857 chars with a stand-in record [M here]; about 990 with a real one of 819 [M inventory 8] |
| 4 | A revocation | `revoke` | `certificate`, `notAfter`, `authority` | closes | 205 [M here] |
| 5 | A standing answer to renewals | `answer` | `certificate`, `answer` (`forever`, `until`, `allowed`), `until`, `after` | neither: it never admits | about 230 |
| 6 | A profile's addition or removal | `name` | `name`, `act` (`add` or `remove`), `after` | an addition opens, a removal closes | 175 [M here] |
| 7 | A rule of one node | `rule` | `node` (the group's or entry's UUID), `doc` (today's `AclDocument` text, unchanged; `{"default":"inherit"}` for a rule taken away), `after` | closes | 230 to 242 with the probe's short rule text [M here]; about 300 with a typical rule of 101 characters and about 5,200 with the largest the reader accepts, 4,482 [M inventory 8], since the document's quotation marks are escaped inside the version |
| 8 | A recycle bin marker of the open T049 design, if it is taken | `bin` | `group`, `since` | closes | about 200 |
| | The operator's word that a deletion made elsewhere is known | `accept` | `origins` | opens | about 170 |

```text
Group  KeePassGate                                  (searching off, auto-type off)
Entry  KPG 2026-11-15 10:15:02 state                UUID 367B11D1 6AF98626 D110E16556F73DB5
  User name, password, URL   (empty)
  Notes      KeePassGate data. Do not edit or delete: it keeps revoked clients out.
             Managed on the KPG tab in KeePass.
  KPG.Write  D110E16556F73DB5
  KPG.20261115T101502Z.D110E16556F73DB5.01
             {"v":"1","db":"5D3F0A77C2E94B1886F0A1D2C3B4E5F6","id":"20261115T101502Z.D110E16556F73DB5.01",
              "kind":"revoke","certificate":"9b1c...","notAfter":"2027-03-01T10:00:00Z","authority":"3f2a...",
              "sum":"0A1B2C3D4E5F6071"}
  KPG.20261102T081100Z.02F713008103A986.01
             {"v":"1","db":"5D3F...","id":"20261102T081100Z.02F713008103A986.01","kind":"admit", ...}
  ... every other version in force
Entry  KPG 2026-11-15 10:15:02 state (second copy)  the same version fields
```

**Why custom strings.** Custom strings and their protection flag are read and written back by
Keepass2Android, KeePassDX 4.5.8 and 3.2.0 and KeePassDroid [R android 2], by KeePassXC 2.4.2 to
2.8.0 [M keepassxc 6; M here], by kdbxweb and pykeepass [M others 3], and by KeePassium,
Strongbox, KeePassKit and gokeepasslib [R others 3]. Every other carrier fails somewhere: entry
and group custom data are deleted by KeePassDroid and KeePassDX [M spike], attachments are one
when 32 bits of their MD5 agree in KeePassDX [R android 5.1], tags are normalized
[R keepass 2.3; M keepassxc 6], unknown XML is dropped everywhere and refused by KeePassium
[M others 3; R others 3], and per-key times in the database's custom data are dropped by
KeePassXC and Keepass2Android [M spike] and written unreadably by kdbxweb [M others 3].

## 4. Where each datum lives

### 4.1 Three carriers

**The plugin's group**, named `KeePassGate`, made under the root with the first write. Its
identifier is `A1B1D236` (the first four bytes of SHA-256 over `KPG/1/group`) and 12 random bytes:
random, so that nothing derived is ever reused after a deletion, which is what makes kdbxweb drop
a group made again under an old identifier [`critique-store-entries-survival.md` P3]. Searching
and auto-type are off, which keeps its entries out of search, the password reports and the expiry
warning in KeePass [M keepass 9], out of search in KeePassXC [R keepassxc 8], and out of search,
autofill and the keyboard on Android [R android 4.2]. **No decision reads the group**: not its
place, its name, its flags or its time. A write entry counts wherever it is, so nothing changes
when KeePassDX moves an entry on equal times [R android 3.1] or a person drags it.

**Write entries**, in that group. Two sorts, which differ only in what they hold:

- a **small entry**: the versions of one write that only opens. `KPG 2026-11-15 10:20:44 client
  paired`.
- a **state pair**: two entries, `KPG <time> state` and `KPG <time> state (second copy)`, each
  holding **every version in force**. The two differ in their identifier, their title and their
  `KPG.Write` field, so KeePass's Delete Duplicate Entries, which compares strings
  [M keepass 9], does not take one for the other's duplicate [M here: after an import that copied
  the whole store, it deleted four entries and neither state entry].

**The node.** A ruled **entry** carries the field `KPG.Rule`: a copy of the newest rule version,
its `id` inside. A ruled **group** holds a marker entry titled `KPG rule for this group`, whose
identifier is `2B82E624` (SHA-256 over `KPG/1/marker`) and 12 bytes derived from the group's
identifier, and whose field `KPG.GroupRule` is the same copy. The node's copy is the only field
of the plugin's that is changed in place, and it is never the only carrier.

### 4.2 Datum by datum

| # | Datum | Carrier | Why this one |
| --- | --- | --- | --- |
| 1 | Authority | a small entry of its own, one protected field | written once, never part of a state pair, so the key is in exactly one entry and leaves the file when that entry is deleted at the retirement. Loss fails closed |
| 2 | Retirement | the state pair | closes: two entries |
| 3 | Admission record | a small entry at the pairing or renewal; carried into the next state pair | opens: loss means the client pairs again |
| 4 | Revocation | the state pair, written at the revocation, which no longer carries the record | closes: two entries, and the record is gone from the file in the same write |
| 5 | Standing answer | a small entry; carried into the next state pair | never admits: a lost answer costs a dialog |
| 6 | Profile list | an addition in a small entry (in the pairing's own entry when a pairing names it); a removal in the state pair | the list is the set of additions that stand (section 5.3); a removal closes |
| 7 | Rule | the state pair, **and** the node: `KPG.Rule` on an entry, the marker entry in a group | three witnesses, one of which follows the node into every copy of it |
| 8 | Bin marker | the state pair | added once |

### 4.3 Carriers considered and not used

| Carrier | Verdict | Why |
| --- | --- | --- |
| One global meta entry, a field per write (the idea as worded) | rejected | section 1: the field of the copy that loses the merge is in history at best, and nowhere at a tie [M keepass 1e; M keepassxc 4] |
| A few global meta entries, one per writing copy, each appended to by one copy only | rejected | a merge then never has to choose between two versions, but the entry is changed in place: Overwrite existing puts an older version over a newer [M keepass 1a], Keep existing drops an arriving newer one [M keepass 1a], and a deletion record made when the entry was tidied removes a version another copy appended since, in kdbxweb whatever the times [R others 3] |
| The rule's versions as fields of the ruled entry only | rejected as the store | the user's entry is edited in every copy and every program; a rule set in one copy and a password changed later in another leaves the rule in history [M keepass 1a], and the loss widens. Kept as the node's copy |
| A meta entry inside the group as the only carrier of a group's rule | rejected as the store | a person can move or delete it in any program, and an absent rule inherits. Kept as the node's copy, where its loss changes nothing |
| History | never the store | section 1 |
| Database, group or entry custom data, per-key times, attachments, tags, unknown XML | not used at all | section 3.2 |
| Deletion records as tombstones | evidence only | they travel only through KeePass's Synchronize [M keepass 5], a Keepass2Android merge [R android 3.1] and kdbxweb [R others 3]; KeePassXC never applies or copies one [M keepassxc 2], KeePassDX applies and drops them [R android 3.1], Database Maintenance clears them all [R keepass 5] |

## 5. Reading: the reconciling rule

"A preprocessing step when loading" becomes a computation at every read that writes nothing. It
runs once per request and once more inside a write's turn, on the calling thread while KeePass's
thread stands still, never inside a merge, as every read does today [R inventory P25]. Opening a
database still changes nothing in it [R inventory O1].

### 5.1 Gathering

1. Every entry of the tree: four bytes of its identifier against the write mark and the marker
   mark. Every deletion record: the same four bytes against the write mark.
2. From each write entry and each of its history items: every field named `KPG.<time>.<origin>.
   <number>`.
3. For each node a request walks: its own copy. On an entry that is `KPG.Rule`. For a group it
   is the marker under the group's derived identifier, and any other direct entry of the group
   that carries `KPG.GroupRule`, which is what a copied group holds.
4. One content per identifier. A field that does not read (bad JSON, wrong `id`, wrong `sum`) is
   ignored when the same version is found whole anywhere else [M here, file F2: both entries of
   a revocation edited by hand; the whole version was found in their history and the client
   stayed refused].

### 5.2 Which versions count

- **Home or not.** The store identifier of the unretired authority is home. With no unretired
  authority there is nothing to serve; the tabs then read the one store the file holds, and the
  operator names the store on the Data page when the file holds more than one. Versions of any
  other store **count for nothing**, whatever they are, and are listed on that page. This is
  safe in the closing direction too, because a version of this database cannot come to carry
  another identifier except by an edit that also has to fix its `sum`. Measured: a marked entry
  planted in a user group with a retirement of the authority and a wide rule for an unruled
  entry, under another store identifier, changed nothing [M here, file F1].
- **What opens** (`authority`, `admit`, a `name` addition, `accept`) counts only from a **current
  field** of a write entry that **no deletion record names**. Never from history, never from an
  entry that came back after it was deleted.
- **What closes** (`retire`, `revoke`, a `name` removal, `rule`, `bin`) counts from **anywhere**:
  a current field, a history item, an entry in the recycle bin, an entry a deletion record names,
  a node's copy.
- **Replaced.** A version is replaced when another version that counts names its hash in `after`.
  `after` is cumulative, so a version stays replaced when the versions in between are long gone
  and it alone comes back from a stale copy.

### 5.3 Datum by datum

| Datum | What is in force | When two copies disagree | If it cannot be read |
| --- | --- | --- | --- |
| Authority | the authorities that count and that no `retire` names | exactly one serves; more than one answers `503 undecided` and the operator keeps one, as today | serves nothing |
| Retirement | any `retire` naming the authority | **wins over the authority, always** | cannot be made unreadable alone: two entries |
| Admission | an `admit` that counts for the caller's certificate, whose record reads and matches its digest, with no `revoke` for that certificate | a union: two copies that paired different clients keep both | the client is not admitted |
| Revocation | any `revoke` for the certificate, anywhere | **wins over every record, always**, in either order | two entries; a revocation is kept until its certificate has expired |
| Standing answer | of the answers for a certificate that are not replaced, the strictest: `forever`, then the later `until`, then `allowed` | the strictest; a lift made knowing the refusal replaces it and travels | counts as `forever` |
| Profile list | a name is on the list when some addition of it counts, no removal names that addition, and that addition names every removal of the name that is found | **a removal wins** over an addition made without knowledge of it; names added in two copies are both kept | more than 32 names, or a damaged version: the list reads as empty, so every client stays admitted and reaches nothing, as today |
| Rule | the versions for the node that are not replaced. One: its document. None: silence, the node inherits | **what both allow**: a profile all of them name keeps the words all of them give it, a profile only some name keeps nothing, the default is `deny` when any denies | the node refuses, there and below |
| Bin marker | every marker found | a union | the group is treated as a bin |

Measured, with the reading rule applied to every result [M here]:

- Two copies each changed entry X's rule from the same version. All 16 results (four KeePassLib
  methods, four KeePassXC releases, both directions) held both versions and read `default=deny`,
  `ops` nothing, `reader` read: what both allow.
- One copy removed the profile `reader` while the other paired a client that added `ops`: `reader`
  off, `ops` on, in all 16.
- One copy removed `reader` and added it back; the other, not knowing, removed it later: `reader`
  off in all 16. This is the sequence that put the name back on the list in the design critiqued
  as `store-entries` P2.

**The node's own copy**, for a rule:

- It names the node it sits on: it is one more version of that node's rule, and the newest of
  all the versions found is in force.
- It names **another** node: this node is a copy of a ruled node, and it **refuses** until the
  operator settles it on its KPG tab. Measured: after KeePass's Duplicate Group
  (`PwGroup.Duplicate` [R 2.62 `KeePassLib/PwGroup.cs:1437-1447`,
  `KeePass/Forms/MainForm.cs:2313-2341`]) the copied group and the copied entry inside both
  refused, and after File then Import with new identifiers every copied ruled node refused
  [M here].
- It is another store's, or does not read: the node refuses.
- It is the newest version and no write entry holds it: the node's history items are read for a
  newer copy. Measured: with both state entries deleted and the entry itself replaced by a later
  edit from another copy, the narrowing was read from the entry's history, and X kept it in all
  16 merges of that pair [M here, pair H5].
- A marker with the right identifier for some other group, dragged into this group, is ignored
  for this group.

### 5.4 What "later" means

A is later than B when A names B in `after`. That is recorded by the copy that wrote A, from
what it held, and no clock takes part. A version stamped an hour "earlier" by a machine whose
clock is behind still replaces what it names; a version stamped "later" replaces nothing it does
not name. Programs that rewrite, drop or tie modification times change nothing, because the
design never reads one: a write entry exists in one version only. Two measured facts make times
matter in exactly one place, the plugin's own deletion records, in section 6.3.

### 5.5 When it cannot decide

| What is found | What the gate does |
| --- | --- |
| A deletion record, not the plugin's own and not accepted, naming a write entry | **serves nothing**, `503 damaged`, until the operator accepts it on the tab. If the entry is back, nothing in it opens anything |
| A version field in a write entry that does not read and is found whole nowhere else | serves nothing, `503 damaged`: nobody knows what it closed |
| Two contents under one identifier, both reading | serves nothing, `503 damaged` |
| A home version of a `v` or `kind` this plugin does not know | serves nothing, `503 damaged` |
| More than one unretired authority | `503 undecided`, as today |
| A node whose copy names another node, is another store's, or does not read | that node refuses, there and below |
| Several rule versions none of which replaces the others | what both allow |
| More than 32 names | the list reads as empty |

`503 damaged` is a new word beside `503 locked`, `503 undecided` and `503 busy`. It tells a client
that waiting will not help and the operator has to act in KeePass.

### 5.6 Cost

Measured in a console process, in memory, on an invented database with 5,000 deletion records
and a store of 30 write entries holding 142 versions [M here, `out\walk.txt`]:

| | 10,030 entries | 50,030 entries |
| --- | --- | --- |
| find the plugin's entries and marked deletion records by identifier | 0.13 ms | 0.96 ms |
| the same, and compare every version field's name and stored object with the last request's | 0.15 ms | 0.95 ms |
| read and check all 142 versions from nothing, the protected authority included | 2.9 ms | 2.5 ms |
| one lookup of `KPG.Rule` on every user entry, which a full listing makes | 1.5 ms | 12.0 ms |

Every request pays the first two rows. A listing makes two such lookups an entry, one for
`KPG.Rule` and one for `KPG.GroupRule`. The third row is paid only when a field is not the object
it was at the last request: KeePass hands back the same `ProtectedString` object for an unchanged
field [M store-entries], so the comparison is exact and nothing that grants is trusted from an
earlier request, as the authority's material is handled today [R inventory A6]. The probe parsed
with the framework's own JSON reader; the plugin's is faster [A]. A lookup on the node replaces
today's read of the node's custom data, measured at 0.55 ms for 10,060 entries [M rules-central].

## 6. Writing

### 6.1 Two kinds of write

Every write is made by `DatabaseWriter` in its turn on KeePass's thread, saved through
`MainForm.SaveDatabase(db, null)` and believed when `Modified` is false and the file hashes to
`HashOfLastIO`, exactly as today [R inventory P13]. A write that did not land is taken back out
by removing the entries it added, with no deletion record, and putting back the entries it
deleted, without the deletion records it made for them.

- **A write that only opens** adds one small entry. It deletes nothing, except small entries
  whose every version it replaces.
- **A write that closes something** adds a **state pair**: two entries, each holding every
  version in force, the new ones included. Then it deletes every other write entry, except an
  unretired authority's and any entry that is open in a KeePass dialog, which the next state
  pair takes.
- **A tidy** is a state pair made by a write that only opens when it finds more than 16 small
  entries.

So the group normally holds: one authority entry, one state pair, and the pairings and additions
made since.

### 6.2 Each operation

| Operation | Today | In this design |
| --- | --- | --- |
| Create an authority | `KPG.Authority.<fp>` | the group if there is none; a small entry with the `authority` version |
| Recreate, Delete, Keep this authority | retirement key; records and revocations removed | a state pair with a `retire` for each authority retired and without their records, revocations and answers; the retired authority's entry deleted in the same write, so the key leaves the file; for Recreate a second small entry with the new authority |
| Pairing, renewal | record, and the list when a profile is new | a small entry: the `admit`, and a `name` addition for a profile the list lacks. Saved and verified before the certificate is handed over |
| Revocation (clients page at OK, or `DELETE /gate/certificate` at once) | `KPG.Revoked.<key>`, record emptied | a state pair with the `revoke` and without the `admit` |
| Standing answer; Allow renewal | key set, changed, removed | a small entry with an `answer` that names the earlier ones in `after`; a lift is the answer `allowed`. Owed answers still ride every save at `FileSavingPre` [R inventory P18], as a small entry |
| Profiles page | list replaced as a difference | an addition: a small entry. A removal: a state pair with a `name` removal that names every addition of that name |
| Rule changed on a group or entry dialog | the dialog's own copy of the node's custom data | a state pair with the new `rule`, and the node's copy (section 6.4) |
| Accept a deletion made elsewhere | | a small entry with an `accept` |

### 6.3 When versions and entries go, and how much there is

**A version is left out of a state pair** in three cases, and each is safe against a stale copy
bringing the version back:

| Left out | Why it cannot come back to life |
| --- | --- |
| a version that another version replaces | the newest version names it in `after`, cumulatively, for as long as that subject has a version |
| an `admit` with a `revoke`; a `name` addition a removal names; a retired authority | the closing version stays in every later state pair |
| an `admit` whose certificate has expired, a `revoke` or `answer` about one, a `revoke` under a retired authority | the certificate is refused on its dates or by the retirement |

**Never left out**: a `retire`; a `revoke` before its certificate's end; a `name` removal no
addition has replaced; the newest `rule` version of every node, a taken-away rule included, even
after the node is gone, because the node can come back from a stale copy with an older copy on
it. The tab offers to remove the rules of nodes that are gone, on the operator's press.

**An entry is deleted with the plugin's own deletion record**: the time inside the entry's
identifier plus one second. Two measurements fix that choice:

- KeePass removes an entry a deletion record names only when the entry's modification time is
  strictly earlier than the record's. With the record at the entry's own time or a second before,
  the entry stayed and the record was dropped in all 16 cells; at one second after, the entry was
  removed, by every method in the copy that deleted, and by Synchronize in the other copy
  [M here, `out\sentinel.txt`; R 2.62 `KeePassLib/PwDatabase.cs:1066-1098`].
- A write entry is never changed, so its modification time is the time in its identifier, and a
  record one second later removes every copy of it that a Synchronize meets
  [M here, pair R2: the four write entries the stale copy still held were gone after a
  Synchronize in either direction].

A record with any other time is somebody else's (section 5.5). Where a merge carries no deletion
records, which is every KeePassXC merge [M keepassxc 2] and three of KeePass's methods, a deleted
entry comes back. It then holds nothing that counts: what opens in it is dead because a deletion
record names it, or refused by the closing version in the state pair, and what closes in it is
replaced. Measured: `keepassxc-cli` brought four deleted entries back beside the ten records, in
four releases, and every verdict stayed as it was [M here, pair R2]; a KeePass Synchronize of
that result removed them again [M here, chain].

**How much.** Entries: the group, one per unretired authority, two for the state, at most 16
small ones, one marker per ruled group. Fields outside the group: one on each ruled entry. For
the database `ground-inventory.md` section 1.4 sizes (one authority, ten clients, three
revocations, five names, rules on twenty nodes), a state entry holds about 17,000 characters, so
the store is about 38,000 against some 14,000 today. A busy database, ten clients that each
renew weekly, makes 520 small entries a year and a tidy every eleven days; nothing accumulates
in the tree, and the file gains about 585 deletion records a year, which KeePass never ages out
and Database Maintenance clears [R keepass 5]. Clearing them costs nothing the design relies on.
What does grow without bound is small: 17 characters in a subject's `after` for each version it
ever replaced, and one retirement per authority ever retired.

### 6.4 OK and Cancel

**The entry dialog.** `PwEntryForm.EntryStrings` is the dialog's own working copy of the entry's
strings and is public [R 2.62 `KeePass/Forms/PwEntryForm.cs:141`]; the dialog assigns it to the
entry on OK and not on Cancel [R 2.62 `:1087-1088`]. The grant editor writes the new version into
`KPG.Rule` there, where it writes into `m_sdCustomData` today. So:

- **OK** keeps it, **Cancel** discards it, and KeePass's own question on Escape
  (`PwEntryForm.cs:2146-2192`, as `option-rules-central.md` 5.1 read it) is asked because the
  entry's strings changed [A until item 3 of section 15 is run]. The reflection on a private
  field goes [R plugin `KeePassContract.cs:184`], and with it the read-only state for a KeePass
  that renamed it.
- When the dialog has closed, a `FormClosed` handler, the way `AuthorityPanelFactory` applies the
  settings tab [R plugin `Ui/AuthorityPanelFactory.cs:198-271`], looks at the entry: if it now
  carries the version the tab wrote, the state pair is added. It tests the entry and not the
  dialog's result, so every way KeePass has of keeping or dropping the edit is followed.
- The pair is added in memory and saved with the operator's next save, as a rule is today. If
  the pair were lost and the entry kept, the node's copy alone is still a version of the rule.
- A rule change still makes a revision of the entry and moves its time, as today. Restoring a
  revision no longer brings an old rule back: the older copy is a replaced version.
- The same rules are never written again, as today.
- Several entries edited together and a revision opened for viewing stay read only.

**The group dialog** has no strings. The tab holds the change in a buffer of its own behind
`IGrantStore` [R plugin `Ui/IGrantStore.cs:17-33`], and the `FormClosed` handler, on OK only,
adds the state pair and creates or updates the marker entry in the group. KeePass marks the
database modified after a group dialog's OK in any case [`option-rules-central.md` 5.1, R 2.62].
A group being added is in the tree before its dialog opens and is taken out on Cancel, so a rule
chosen there is written at OK like any other.

**A copy that refuses** is settled on its own KPG tab: the tab shows the rule it was copied with,
and OK writes a first version for the copy's own identifier. Until then it refuses.

**The settings tab still waits for OK.** `PendingDatabase` keeps wrapping `IGateDatabase`, over a
list of intended writes instead of keys. At OK they are made against the live database, so a
pairing that landed under the dialog is simply one more small entry the state pair carries. The
profile list needs no merge of its own at OK: a removal names the additions found at that moment,
which is today's outcome for a name the operator is part way through removing. A buffered lift of
a standing answer is dropped when an answer about that certificate arrived under the dialog,
which is the one reconciler left.

**KeePass's own dialogs.** No KeePass dialog copies a group's list of entries and puts it back
[`critique-store-entries-security-and-fit.md`, checked item 4], so no OK undoes an added entry.
Database Maintenance copies the database's custom data [R keepass 4], which holds nothing of the
plugin's any more, so the writer's refusal while it is open goes. The entry dialog copies one
entry and writes it back [R keepass 4]: the plugin never changes a write entry, and a deletion
skips one that is open, which `GroupDialogs` already knows. The marker entry is the one entry
the plugin changes in place; a marker open in a dialog is left as it is and is brought up to
date with the next rule change, which costs nothing because the state pair holds the rule.

### 6.5 What changes in the plugin

| Class | Change |
| --- | --- |
| `IKeyValueStore`, `CustomDataStore` | go. A seam of versions replaces the seam of keys |
| new `Bridge/VersionStore` | the gathering of section 5.1, the comparison with the last request, the deletion records, the store identifier; the only code that knows the group, the marks and the field names |
| new `Authority/Version` | the envelope read strictly through `JsonBody`, `sum`, `after` |
| `AuthorityKeys` | becomes the kinds and the names |
| `AuthorityDocument`, `AdmissionRecord`, `AclDocument` | formats unchanged. `AclDocument.CustomDataKey` goes; `AclDocument` gains "what both allow" |
| `AuthorityStore` | same decisions over versions; `Write`, `Retire` and the prune become state pairs |
| `AdmissionStore` | `Find` reads the reconciled view; `Remove`, `RemoveAll` and the prunes become state pairs |
| `RenewalRefusal`, `ProfileNames` | read by the rules of section 5.3; `ProfileNames.Merge` goes |
| `DatabaseWriter` | same lock, turn, save and verification; every write is one of the two kinds; the Database Maintenance refusal and its exception for owed answers go; a deletion asks `GroupDialogs` about the entry |
| `PendingDatabase`, `PendingStore`, `PendingChanges` | buffer intended writes; one reconciler left |
| `OpenDatabaseTargets` | reads the view; the authority's material keyed by the stored object; answers `503 damaged` |
| `KdbxNodes`, `IRuledNodes`, `DatabaseNodes` | `DocumentOf` [R plugin `Bridge/KdbxNodes.cs:65-73`] asks the view for the node, with the node's copy; the Profiles page counts from the view |
| `NodeDialog`, `IGrantStore`, `GrantPanelFactory` | section 6.4: `EntryStrings` for an entry, a buffer for a group, a `FormClosed` handler |
| `GrantPanel`, `GrantSheet`, `GrantTab` | two more states: "two versions" (the table shows what both allow; OK keeps what is shown) and "copied" |
| `GroupDialogs` | the watch on Database Maintenance stops mattering to the store; the others stay |
| `AuthorityPanel` and the settings tab | a fourth page, **Data**: what is wrong, by count and by name, with Accept, Put back, Remove another store's data, Remove rules of nodes that are gone |
| `KdbxVault`, `VaultHandlers`, `NodeTags` | the guard of section 9 |
| `Expected.OfCustomData` | becomes the filter list for the store's own failures |
| Tests | `CustomDataMergeRuleTests`, `SnapshotHazardTests` and the rule cells of `MergeInRealDatabaseTests` give way to tests that pin, against the pinned KeePass, what section 7 rests on. 49 test files touch the present store by name [`critique-store-entries-security-and-fit.md` M8]; this is a rebuild of that part of the tiers |

## 7. What a save and a merge do to it

### 7.1 Measured

Ten pairs of copies, each merged in both directions by KeePassLib 2.62 under Synchronize,
Overwrite if newer, Overwrite existing and Keep existing (80 cells) and by `keepassxc-cli` in four
releases (80 cells), every result saved, reopened and read by the rule of section 5
[M here, `out\summary.txt`]. A is the copy that acted; B is a stale copy that changed an entry's
password later, paired a client of its own and changed a group's rule.

| Pair | What A did before the merge | Read from the results |
| --- | --- | --- |
| R1 | revoked client c1 and narrowed entry X's rule | all 16: c1 refused, its revocation in two entries; c2 and B's c4 admitted; X carries A's narrowing although B's later edit took the entry; group U carries B's newer rule |
| R2 | the same, then a state pair | all 16: the same verdicts. Where the plugin's deletion records applied (KeePassLib into A under all four methods, and Synchronize into B) 6 write entries were left; in the other 11 cells 10 were left, the four returned ones counting for nothing |
| C | changed X's rule, removed `reader`; B changed X's rule too and added `ops` | all 16: section 5.3 |
| P | removed `reader`; B had removed it and added it back | all 16: `reader` off |
| H1 | then one of the revocation's two entries was deleted, leaving no deletion record | all 16: c1 refused |
| H3 | then one of the two state entries was deleted, no record | all 16: every verdict as in R2 |
| H4 | then one of the revocation's entries was deleted with a deletion record | all 16: c1 refused. `503 damaged` in 9: every merge into A, and Synchronize into B |
| H2 | then **both** of the revocation's entries were deleted, no record | 9 closed: A's file holds no record of c1 at all, and a Synchronize removes B's. **7 open**: the three other KeePass methods and the four KeePassXC releases, merging A into B, leave B as it was, c1 admitted |
| H5 | then **both** state entries were deleted, no record | into A, all 8: c1 and c2 not admitted, `reader` off. Into B: the same after Synchronize; the other 7 leave B's clients as they were. X keeps A's narrowing in all 16, read from the entry's history |
| H6 | then the whole group was deleted for good, a record for each entry | `503 damaged` in 9. The other 7 leave B as it was |

The 21 open cells are one case: both witnesses of a refusal destroyed in the copy that made it,
and that copy then merged **into** a stale copy by a method that carries no deletion records. The
stale copy has learned nothing and is what it was before. Section 12 has it.

A plain save by `keepassxc-cli add` in four releases kept every write entry, every field, the
protection flag on the authority, the marker, `KPG.Rule`, every time, and all ten deletion
records with their times; the file stayed KDBX 4.0 [M here, `out\dumps.txt`].

### 7.2 By program

| Program | A save | A merge | What the gate then does |
| --- | --- | --- | --- |
| KeePass 2.62 | strings, protected strings and deletion records are KDBX 4.0 features and all come back [M keepass 2.1] | section 7.1. An entry only one copy holds arrives under every method [M keepass 1d; M here]. Synchronize carries deletion records; under every method a database applies its own [M keepass 5; M here]. History limits, ties and rewritten times touch nothing, because no write entry has two versions | the verdicts of 7.1 |
| KeePass, File then Import with new identifiers (preselected [R keepass 2.4]) | | the store arrives again in a second group under new identifiers, and every user node is duplicated [M keepass 2.4; M here] | entries without the mark are not read, so the copies of the store count for nothing; every copied ruled node refuses. Delete Duplicate Entries afterwards deleted four **originals**, the authority's entry and a marker among them [M here]: `503 damaged`, and the Data page puts the authority back from its copy |
| KeePassXC 2.4.2 to 2.8.0 | 7.1. It adds keys of its own to the database's custom data [M here], which holds nothing of ours | 7.1. It never applies or copies a deletion record [M keepassxc 2; M here], and from 2.4.2 replaces the database's custom data whole [M spike], which no longer matters. A group merge raises a group's time without copying its fields [M keepassxc 4]: no group field is read | the verdicts of 7.1 |
| Keepass2Android 1.15-r6, 1.08d-r5 | keeps entries, custom strings and a custom string's own flag [R android 2]; entries as wholes [M spike]. It resets the protection of standard strings only [R android 5.6]. Its KDBX 3.1 output for AES-KDF files [M spike] holds strings [A] | a port of KeePassLib's Synchronize: an arriving entry is added, to the root when its group is missing; the two lists of deletion records are joined and applied to entries older than the record [R android 1.5, 3.1]. It trims history at every load [R android 1.3], which holds nothing the design needs | by the reads: as KeePass's Synchronize. Not measured for this shape |
| KeePassDX 4.5.8 | keeps entries, custom strings and flags; writes 4.1 and stamps the root group [R android 2; M spike] | adds an arriving entry and recreates a missing group; applies arriving deletion records and keeps none; on equal times moves the open copy's entry to the arriving copy's group [R android 1.5, 3.1] | by the reads: the union; place means nothing. A hand deletion it applied leaves no record in its copy, so that copy does not answer `503 damaged`; the second entry of the pair is what is left |
| KeePassDX 3.2.0 | as 4.5.8 for entries and strings; it deletes group custom data [M spike], which is not used | adds an arriving entry only when its group exists in the open copy [R android 1.5] | by the reads: the union, since the group exists in every copy that has ever held the store. A dropped entry leaves that copy as stale as it was |
| KeePassDroid 2.7 | keeps entries [M spike] and custom strings [R android 2]; deletes node custom data [M spike], which is not used; has no merge | | unchanged |
| kdbxweb 2.1.1, KeeWeb | keeps strings, flags and history [M others 3]. Its unreadable KDBX 4.1 output needs timed custom data [M others 3], which this store never writes | adds entries; removes whatever a deletion record names, whatever the times [R others 3] | the plugin's deletions stay deleted; a hand deletion reaches every copy and with it `503 damaged` |
| Strongbox, KeePassKit, keepass-rs | carry strings [R others 3] | add a node the open copy lacks [R others 4] | by the reads: the union |
| pykeepass and programs on it | keeps everything [M others 3]; deletes without a deletion record [R others 3] | no merge | a deleted entry leaves no trace; the pair is what is left |
| Programs on gokeepasslib before 3.7.1 | can corrupt protected values in some layouts [`critique-store-entries-survival.md` P6] | | an authority that does not read serves nothing |
| A whole file written over another | | nothing merges [R android 5.5; R others 3] | not solved, section 12 |

## 8. What a person sees, what they can break, and what the gate does

**What they see**, in every program: a group `KeePassGate` with a few entries titled `KPG <time>
<what>`, with empty user name and password and a line of notes; in each ruled group an entry `KPG
rule for this group`; on each ruled entry one extra field, `KPG.Rule`, holding the rule in
readable JSON. That last point is new and useful: on a phone a person can see that an entry is
ruled, which today they cannot.

| By hand, in any program | What the gate does | Basis |
| --- | --- | --- |
| Edits a title, notes, icon, or moves an entry or the group anywhere, the recycle bin included | nothing; none is read, and place means nothing | section 4.1 |
| Edits a version field | it does not read; the same version is in the pair's other entry or in the edited entry's history. Found nowhere whole: `503 damaged` | [M here, F2] |
| Deletes one write entry for good, with a deletion record | `503 damaged` until accepted on the Data page; the pair's other entry holds everything | [M here, H4] |
| Deletes one write entry, in a program that leaves no record | nothing is lost and nothing shows; the next state pair makes two again | [M here, H1, H3] |
| Deletes both state entries | every client and name is gone from that file, so it admits nobody; rules are read from the nodes. With records: `503 damaged` | [M here, H5] |
| Deletes the whole group | serves nothing. With records, `503 damaged`, and a Synchronize carries that to every copy | [M here, H6] |
| Then merges that copy into a stale copy with a method that carries no deletion records | the stale copy is as stale as before | [M here, 21 cells]; section 12 |
| Clears deleted objects information in Database Maintenance | the evidence of a hand deletion is gone from that file | [R keepass 5] |
| Duplicates a write entry, or imports the database into itself with new identifiers | copies without the mark are not read. The Data page counts them and removes them on a press; the copy of the authority's entry holds the key until then | [M here] |
| Runs Delete Duplicate Entries after such an import | originals can go and copies stay: `503 damaged`, Put back | [M here] |
| Runs Delete Empty Groups | the plugin's group is never empty; a user's group that holds only a marker is not empty to KeePass | [M keepass 3] |
| Deletes or edits `KPG.Rule` on an entry, or deletes a marker | nothing: the state pair holds the rule | section 5.3 |
| Copies, duplicates or makes from a template a ruled entry or group | the copy refuses until settled | [M here] |
| Moves a marker into another group | ignored there; the group it left keeps its rule | section 5.3 |
| Copies a marker into another group | that group refuses until settled | section 5.3 |
| Restores an older revision of a ruled entry | nothing: the older copy is a replaced version | section 5.2 |
| Moves a ruled node | its rule follows | the rule names the node |
| Deletes a ruled node | its rule stays in the state pair until removed on the Data page | section 6.3 |
| Writes a whole version by hand, with the store identifier, a right `sum` and the right `after` | it takes effect. Whoever holds the master key can write anything, as today [R inventory P23] | conceded |
| Exports or prints | section 12 | |

Every closed state is shown on the Data page with its reason and its one remedy, logged, and
named in the audit line of each request it refuses. Nothing is repaired without a press and
nothing at open.

## 9. Keeping the API and the reference client out

Today no route touches custom data, so the store is out of reach by construction
[R inventory P15]. Here the guard is explicit, in the vault and not in any rule:

1. **An entry whose identifier carries the write mark or the marker mark is absent**, as an entry
   in the recycle bin is (`KdbxVault.Discarded`, [R plugin `Bridge/KdbxVault.cs:2123-2154`]): not
   listed, counted, tagged or addressable, and answered with the `404` a refused node gets. So is
   the group whose identifier carries the group mark.
2. **An entry anywhere that carries `KPG.Write` or `KPG.GroupRule` is absent the same way.** That
   covers a copy of a write entry or of a marker under another identifier, the copy of the
   authority's key above all. The test runs on the entries a request is about to serve.
3. **A string whose name begins `KPG.`, compared without regard to ASCII case, does not exist to
   any route**: `listFields` leaves it out, `readField`, `updateField` and `stripField` answer as
   for a field that is not there, `createField` refuses the name with the `400 malformed` it
   gives a name its own routes could not name again, and the entry's tag is computed without it.
   Master reserves no name today [`critique-store-entries-security-and-fit.md` S1], so this
   closes a gap that exists now. A ruled entry stays visible; only its `KPG.Rule` is hidden.
4. **No route lets a caller choose an identifier**; a new entry or group gets a random one
   [R plugin `Bridge/KdbxVault.cs:385`, `:603`], drawn again if it carries one of the three marks.
5. **A group that holds the plugin's group cannot be removed.** A group whose only entry is its
   marker is empty to the API, and removing it takes the marker along.
6. The store identifier, the version names and the hashes never appear in any answer, any tag or
   the audit. They are what a foreign file would need.
7. The routing document gains no route and no schema; the vocabulary of `503` gains `damaged`.

The reference client changes in one place: `503 damaged` joins the words it knows and is not
retried. `pykeepassgate.api` has no method that could reach the store. `pykeepassgate.refs`
follows references under the caller's rules and meets the same `404` for the plugin's entries,
and a reference to a custom string named `KPG.Rule` resolves as a field that is not there, which
needs one case in `ReferenceVectors.json`, regenerated from KeePass as that file always is.

## 10. KDBX version

**4.0.** Strings, protected strings, the group's two flags and deletion records are written at
4.0 by KeePass 2.62 [M keepass 2.1]; every file made and every one of the 164 results here is 4.0
[M here], and KeePassXC 2.4.2 and 2.6.6, which only write 4.0, kept all of it [M here]. The
plugin no longer takes a database to 4.1, and the file is no longer one that KeeWeb makes
unreadable to KeePass [M others 3].

## 11. Settled decisions: reversed and kept

**Reversed, each to be recorded as a reversal.**

1. "Stored inside itself under `KPG` keys in the database's own `CustomData`": the place. The
   principle, that a database is its own authority and vouches only for itself, stays, and the
   store identifier restates it for data that a merge can now bring.
2. "The database's own `CustomData`, never the root group's", with one key per authority, per
   certificate and per revocation: the whole bullet. Its finding stays and widens: what has to
   survive a merge is something only ever added, and here that is every write.
3. "The authority takes the file to KDBX 4.1": gone.
4. "The access control model is one document per node ... Grants live under a `KPG` key": the
   document is kept; it lives in a version, with a copy on the node.
5. "The grant editor writes into the dialog's own copy, `m_sdCustomData`": into the entry
   dialog's public strings, and into a buffer for a group.
6. "The plugin writes no rule of its own" (`CA.md:127-138`): it writes the state pair and the
   marker, at the operator's OK.
7. "Replayed key by key" and "`KPG.Profiles` is replayed as a difference": intended writes made
   at OK; the stored form is the difference, and `ProfileNames.Merge` goes.
8. Only a rule that will not read refuses: a copied node refuses too, and two versions give what
   both allow.
9. "The KPG tab has three pages of its own": four.
10. The trap section's `DatabaseOperationsForm` paragraph stops being about the store.
11. An entry's history holds each revision's rule and a restore brings it back
    (`DESIGN.md:972-973`): no longer.
12. `CA.md` §204 and §208: the merge argument is rewritten, and the stored authority is a
    protected string that KeePass keeps encrypted in its process memory [R inventory H6].
13. "Nothing in a record is authenticated beyond the file" (`CA.md:62-64`): still true for
    whoever holds the file; the store identifier is a new line against files made without it.

**Kept.** No host-level authority or registry; the admission record is load bearing and its
profiles come from the record less what the list lacks; no `kpg://any`; no passphrase on the
authority key; pairing tokens and held approvals in memory only; opening or reading changes
nothing, and prunes ride only on the plugin's own writes; everything on the settings tab waits
for OK, revocation included; OK keeps a rule and Cancel discards it on both dialogs; one writer,
saved and verified, before any certificate is handed over; live reads on every request; nearest
wins, per profile; the rule document, its ceilings and `CRUSDLAP`; the profile list decides what
a client holds; one JSON grammar and one escaper; refused and absent answer the same `404`;
spec-first routing; `503 undecided` and the authority chooser; a rule travels with its node, and
now with a copy of it only on the operator's word.

## 12. What it does not solve

- **A file written over.** Keepass2Android's "No, overwrite", a KeePassDX save within ten seconds
  of the other change, any KeePassDroid save over a changed file and a file synchronizer's
  conflict copy keep one file whole [R android 5.5; M spike; R others 3]. No store inside the
  file survives that.
- **A stale copy on its own**, and a stale copy that a merge told nothing: the 21 cells of
  section 7.1. Both entries of a pair, or the whole group, have to be deleted in the copy that
  revoked, and that copy merged into the stale one by a method that carries no deletion records.
- **Deletions that leave no record.** pykeepass writes none [R others 3] and KeePassDX keeps none
  of those it applies [R android 3.1]. One such deletion is covered by the pair; two are not seen.
- **The store is in plain sight, and the authority's key with it.** Every program lists the
  group, and the key is one reveal or one copy to the clipboard away in each. KeePassXC's HTML
  export prints protected custom strings in clear [M keepassxc 8], KeePass's print does when
  custom strings are ticked [R keepass 9], and an import with new identifiers leaves a second
  copy of the key until the operator removes it [M here]. A leaked key alone admits nobody, since
  a certificate needs a record in the database [R inventory E2], but it can impersonate the gate
  to a client on the same machine. Keeping the authority alone in the database's custom data
  would hide it again, at the price of the one place this design otherwise leaves behind.
- **Clutter in the user's groups**: a marker entry in every ruled group and a field on every
  ruled entry, in every program.
- **Every copy of a ruled node refuses until settled**, a template's children included. That is
  the closed direction and it is work for the operator.
- **A hand deletion closes the whole gate**, not the one thing the deleted entry held, because
  nobody can know what it held.
- **A rule changed in two copies is narrower than either operator meant** until one of them
  settles it on the node's tab.
- **A file made by somebody who once held the database** knows the store identifier. It is
  stopped only by what stops it today: an authority, a record and a rule it brings are concurrent
  with the newer ones and lose to them.
- **Every closing write rewrites the state**, about 17,000 characters twice in the example and
  far more with many large rules.
- **The Android merges are read, not measured**, for this shape.

## 13. A migration, in one paragraph

None is designed. If a database made with 0.1.0 were to be carried over, it would be a press on
the KPG tab and never an act at open: read the six key families and every node's `KPG` item,
write one state pair and the nodes' copies, save and verify, and only then remove the old keys
and items. From then on the old places are not read, so a copy that was not converted, merged in
later, brings back keys that mean nothing and nodes whose old rules mean nothing; its nodes would
then inherit where the old rule narrowed, which the operator has to be told before pressing. A
file that ever held timed custom data stays KDBX 4.1 until those keys are gone. Reading the old
refusing keys for as long as a stale copy could return is exactly the legacy code the project
does not carry, so the alternative within that rule is that an old database reads as having no
authority and no rules, and the operator creates, pairs and rules again.

## 14. What was run

- **The probe**:
  `spikes\storage\probes\versioned-fields\design\probe\Program.cs`,
  built with the .NET SDK against `.keepass\2.62\KeePass.exe` and used as a
  library. No window. It makes the databases with invented content (Argon2d, 2 iterations,
  1 MiB), merges with `PwDatabase.MergeIn`, and implements the reading rule of section 5, which
  it applies to every result after a save and a reopen.
- **The runs**: `scripts\Run.ps1` beside it. KeePassLib 2.62, four methods, ten pairs, both
  directions: 80 cells. `keepassxc-cli merge -s` in 2.4.2, 2.6.6, 2.7.12 and 2.8.0, the same
  pairs and directions: 80 merges, and four plain saves; all 84 exited 0. The copies are the ones
  the KeePassXC ground report downloaded and held to the spike's pinned hashes; each carried a
  valid Authenticode signature from DroidMonkey Apps, LLC, checked again. One chain (KeePassXC,
  then KeePassLib Synchronize), Duplicate Group, an import with new identifiers followed by
  Delete Duplicate Entries, two single files (planted versions of another store, a field edited
  by hand), the deletion-time measurement (24 cells), and the cost on 10,030 and 50,030 entries.
- **Outputs**, under `versioned-fields\out\`: `merges.txt` (every result as the rule reads it),
  `summary.txt` (the cells that read alike, grouped), `dumps.txt`, `sentinel.txt`,
  `newids-detail.txt`, `walk.txt`, `marks.txt`. Databases under `versioned-fields\work\`.
- **Where the probe is not the design.** In pairs R1, C, P, H1, H2 and H4 a closing write is a
  pair of entries that carries along only what the entries it deleted had held; the design
  writes the whole state, which is the shape of pairs R2, H3, H5 and H6. The probe's rule
  documents use a short syntax of its own and not `AclDocument`'s. The store identifier is fixed.
  It reads a node's history always, where the design reads it only when the node's copy is the
  newest version and no write entry holds it. The kinds `retire`, `answer`, `accept` and `bin`
  were not exercised. It is not the plugin's
  code, KeePass's window, KeePassXC's window or any Android program.

## 15. Measurements to make before this design could be chosen

Most important first. Items 1 and 2 need the emulator and wait for the desktop.

1. **The Android merges over this shape**, with the emulator runs `ground-android.md` section 7
   lays out and the probe's pairs: that a Keepass2Android and a KeePassDX merge (4.5.8 and 3.2.0)
   add a write entry only the arriving copy holds; what each does with the plugin's deletion
   records, whose time is one second after the entry's; that KeePassDX leaves no record of a
   deletion it applied; and the read back of pairs R2, H4 and H6.
2. **Every Android save of the store**, KeePassDroid 2.7, KeePassDX 3.2.0, 3.3.0 and 4.5.8 and
   Keepass2Android with its KDBX 3.1 output: an entry of some 60 custom strings and 17,000
   characters, a protected string of 3,063, field names of 40 characters with dots, `KPG.Rule` on
   a user entry, the marker entry, and the deletion records with their times.
3. **The dialogs in a real KeePass 2.62** (the Live tier): a rule written into `EntryStrings`,
   then OK, Cancel and Escape; that the `FormClosed` handler finds the entry carrying the version;
   Add Entry and Add Group, accepted and canceled; several entries edited together; a revision
   viewed and restored; the group dialog's buffer and the marker; a copied node settled.
4. **KeePass's window over the store**: a real File then Synchronize and File then Import with
   the plugin reading in between; Delete Duplicate Entries on a store as the plugin writes it and
   after an import; Empty Recycle Bin with a write entry in it; Database Maintenance clearing
   deletion records; a state pair written while the entry dialog is open on a write entry.
5. **The read inside the gate**, under the hold and its limit, for 1,000, 10,000 and 50,000
   entries: the two rows every request pays, the re-read after a change with the plugin's own
   JSON reader, and the node's history when it has to be read.
6. **A state pair at its largest**: 31 profiles, rules of 4,482 characters on a few hundred
   nodes, through a KeePass save, a `keepassxc-cli` save and merge, and an Android save.
7. **kdbxweb and KeeWeb** over pairs R2, H4 and H6, for its rule that a deletion record removes
   whatever it names; **Strongbox, KeePassKit and KeePassium**, which need another platform.
8. **KeePassXC's window merge, its reload of a file changed on disk, and a KeeShare import**, the
   one path that applies deletion records, over pairs R2 and H4.
9. **How often a ruled node is copied** in real use, since every copy refuses until settled.
10. **The guard**, as host and end to end tests: every route against the plugin's group, a write
    entry in a user's group, a marker, a copy under another identifier, the reserved names on
    every field route, and the tag of a ruled entry.
11. **What each program shows**: the entries and the `KPG.Rule` field on each program's screens
    and in search, what revealing and copying the authority's field looks like, and KeePassXC's
    HTML export of it.
