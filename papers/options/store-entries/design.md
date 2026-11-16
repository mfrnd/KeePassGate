# Option `store-entries`: the database-level data as ordinary entries in a dedicated group

Written 2026-11-15 against KeePassGate master `a42e38aa`. One design, in its strongest form, for
everything `PwDatabase.CustomData` holds today. Access rules are another option's subject; section
12 names the rule placement this design composes with.

## How statements are marked

Every statement about what a program does carries its basis:

- **[M here]**: measured for this design, today, from a console. Probe, scripts and raw output are
  under
  `spikes\storage\probes\store-entries\design\`
  (`probe\Program.cs`, `scripts\Run.ps1`, `scripts\summarize.py`, `out\merges.txt`,
  `out\summary.txt`, `out\walk.txt`). Section 14 describes the runs.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in that ground report
  (`ground-keepass.md`, `ground-keepassxc.md`, `ground-others.md`) or recorded by
  `spikes/revocation-merge` as `ground-measured.md` and `ground-inventory.md` digest it.
- **[R android]**, **[R keepass]**, **[R keepassxc]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[A]**: assumed. Neither run nor read.

## 1. The design in one page

**The timestamp-named-field idea, moved from the field to the entry.** The idea was a JSON document in an
entry field whose name comes from a timestamp, so that a merge keeps both copies' documents, plus a
step at load that repairs what a merge left. Every merge anybody measured takes an entry whole and
never field by field [M keepass, 120 cells; M keepassxc; M others; R android], so two fields added
to one entry in two copies do not both survive. Two entries added to one group in two copies do, in
every merge measured [M keepass X1; M keepassxc; M others] and read [R android]. So this design
keeps the idea's core and changes its unit:

1. **One record is one ordinary entry**, in one group of the plugin's own. Its JSON sits in a
   custom string of that entry.
2. **A record is written once and never changed.** What changes a decision is another record
   beside it: a revocation beside a client's record, a retirement beside an authority, a removal
   beside a profile's addition, a later answer beside an earlier one.
3. **A record's identifier comes from what the record is about, not from a clock.** The entry's
   UUID is derived from its kind and its subject (the certificate's digest, the authority's
   digest, the profile's name and generation). Two copies that make the same decision make the
   same entry, and "is this certificate revoked" is a lookup of one identifier.
4. **The step at load becomes a fold at read, and writes nothing.** What the gate serves is
   computed from the set of records on every request. A merge can only add records to that set,
   so there is nothing to repair afterwards and opening a database still changes nothing in it.
5. **Granting records count only in place; refusing records count wherever they are.** An
   authority, a client's record or a profile's addition counts only as a direct child of the
   store group, outside the recycle bin, under the identifier its content derives, and with no
   deletion record naming it. A retirement, a revocation or a profile's removal counts wherever
   its entry is found in the file, the recycle bin included, and still counts when only a
   deletion record naming its identifier is left. Every way a person or a program can damage the
   store therefore lands on the refusing side.

Nothing in the design reads a modification time, a history item, a custom data item, an
attachment, a tag or a per-key time. Those are the features the ground reports show other
programs dropping, rewriting or deciding by.

## 2. The store group

| Property | Value | Why |
| --- | --- | --- |
| UUID | fixed, the same in every database: the first 16 bytes of SHA-256 over the UTF-8 text `KPG/1/group/`, which is `FE4040A9E0FAE884FA8BECAEEE9FBB46` [M here, `out\uuids.txt`] | Two copies that each make the group on their own make the same group to a merge. Measured: set I, where each copy created the group independently, came out as one group holding both copies' entries under the four KeePassLib 2.62 methods that match by identifier and in all four `keepassxc-cli` releases, both directions, 16 cells [M here]. Keepass2Android's templates set the precedent with hard-coded UUIDs [R android 4.1] |
| Found by | that UUID, anywhere in the tree. Never by name, never by a pointer in `Meta` | Names are adopted by other programs (`Backup`, `AutoOpen`, the localized bin and templates names) [R android 4.1, 4.2]. A pointer in `Meta` would be one more value a merge decides by a time |
| Name | `KeePassGate`. The operator may rename it; nothing reads the name | |
| Place | made directly under the root. The operator may move it; it is still found. Inside the recycle bin it is found and its granting records do not count | |
| Icon | KeePass's standard icon 29, `TerminalEncrypted`, the plugin's own | every program keeps a standard icon [R android 2] |
| Searching | off (`EnableSearching` false) | keeps the entries out of search, the password reports and the expiry warning in KeePass [M keepass X11; R keepass 9], out of search in KeePassXC [R keepassxc 8], out of search, autofill and the keyboard in Keepass2Android and KeePassDX [R android 4.2] |
| Auto-type | off (`EnableAutoType` false) | KeePass and KeePassXC then offer none of its entries [R keepass 9; R keepassxc 8] |
| Notes | one fixed line: `KeePassGate data. Managed on the KPG tab in KeePass.` | tells a person in another program what they are looking at |
| Subgroups | none | KeePassDX 4.5.8 can loop forever over nested deleted groups [R android 5.3]; a flat group under the root never reaches that code [A] |
| Never empty | made with the first authority, and a retirement is never removed, so it always holds an entry | KeePass's Delete Empty Groups removes a group that holds no entry [M keepass X10] |

The flags and the icon are conveniences. No decision reads them, so a program that drops or flips
them costs tidiness and nothing else. A KeePassXC save kept both flags [M keepassxc 6; M here, four
releases], and they came through all 59 results here unchanged [M here]. The plugin puts them back
with its next write of its own.

## 3. One record, one entry

Every record has the same shape. Sizes are the ones `ground-inventory.md` measured for today's
values, which stay the values.

| Part of the entry | Content |
| --- | --- |
| UUID | the first 16 bytes of SHA-256 over the UTF-8 text `KPG/1/<kind>/<subject>` |
| `Title` | `KPG <kind words> <shortened subject>`: fixed words and hex digits, or a profile's name, which is ASCII letters, digits, `-` and `_`. Never text a client supplied, never a brace, because KeePass compiles titles as placeholders when it matches auto-type [R keepass 9] |
| `UserName`, `Password`, `URL` | present and empty. Empty `Password`: KeePass's quality and duplicate reports and KeePassXC's health check and HIBP check skip the entry [R keepass 9; R keepassxc 8]. Empty `URL`: nothing for browser matching or autofill to match [R keepassxc 8; R android 4.2]. Present: a KeePassXC save adds the standard fields an entry lacks; it added three to the invented user entry here and none to a store entry [M here] |
| `Notes` | the same fixed line as the group's, one line, so no dialog's line break handling can touch it |
| custom string `KPG.Kind` | one word naming the kind, not protected. It marks the entry as the plugin's for the API's guard (section 9) |
| custom string `KPG.Data` | the record's content. Protected for an authority, not protected otherwise |
| expiry, tags, attachments, entry custom data, history | none |

**Why custom strings.** Custom strings and their protection flag are read and written back by
Keepass2Android, KeePassDX 4.5.8 and 3.2.0 and KeePassDroid [R android 2], by KeePassXC 2.4.2 to
2.8.0 [M keepassxc 6; M here], by kdbxweb and pykeepass [M others] and by KeePassium, Strongbox,
KeePassKit and gokeepasslib [R others]. Every other carrier fails somewhere: entry custom data is
deleted by KeePassDroid [M spike] and replaced per key by KeePassDX merges [M spike, re-read];
attachments are treated as one when 32 bits of their MD5 agree in KeePassDX [R android 5.1, the
collision cost M android] and are lost inside arriving history [R android 5.2]; tags are
normalized [R keepass 2.3; M keepassxc 6]; unknown XML is dropped everywhere and makes KeePassium
refuse the file [M others; R others]. **Why not a standard string for the content**: the standard
strings are what search, reports, exports to CSV, auto-type and browser matching read [R keepass
9; M keepassxc 8], and Keepass2Android resets the protection of standard strings at every load
while keeping a custom string's own flag [R android 5.6].

**Why the authority's content is a protected string.** KeePass keeps a protected string encrypted
in its process memory [R inventory H6], which is better than today's plain string in custom data
that nothing can overwrite [R inventory P14].
Other programs hide it until the person asks to see it [A]. The other kinds hold nothing secret
and stay unprotected, so reading them costs no decoding.

**Why one string for the whole authority.** `AuthorityDocument` keeps its three parts in one JSON
value today so that no merge can combine parts of two authorities. An entry merges whole, so three
strings would hold together too [M keepass X2], but one string keeps that guarantee independent of
the whole-entry measurement and keeps the parser as it is.

### 3.1 The kinds

Digests below are invented and shortened; a real subject is the 64 hex digits the present keys
use.

| # | Datum | Kind | Subject (what the UUID derives from) | `KPG.Data` | Size | Granting or refusing |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | An authority: certificate, private key, host PKCS#12, gate key | `authority` | the SHA-256 of the authority's certificate | today's `AuthorityDocument`, unchanged: `{"v":"1","ca":…,"host":…,"sniKey":…}`. **Protected** | 2903 chars | granting |
| 2 | An authority's retirement | `retired` | the same digest | `{"v":"1","authority":"3f2a…"}` | about 85 | refusing |
| 3 | An admission record, one per certificate admitted | `client` | the SHA-256 of the client's certificate | today's record, unchanged line format (`v`, `subject`, `client`, `admitted`, `profiles`, `certificate`) | 819 chars with two profiles | granting |
| 4 | A revocation | `revoked` | the same digest as the record | `{"v":"1","certificate":"9b1c…","notAfter":"2027-03-01T10:00:00Z","authority":"3f2a…"}` | about 200 | refusing |
| 5 | A standing answer to renewals | `renewal` | `<certificate digest>/<number>/<answer>/<until>` | `{"v":"1","certificate":"9b1c…","number":"2","answer":"until","until":"2026-12-01T00:00:00Z"}`; `answer` is `forever`, `until` or `allowed` | about 150 | neither: it never admits |
| 6a | A profile's addition | `profile` | `<name>/<generation>` | `{"v":"1","profile":"reader","generation":"1"}` | about 45 | granting |
| 6b | A profile's removal | `removed` | the same `<name>/<generation>` | the same object | about 45 | refusing |

Every JSON value is one flat object of strings, which is what `JsonBody` reads and `JsonWriter`
writes today. No new grammar and no second escaper arrives.

An example, as a person would see a revocation in any program:

```text
Group  KeePassGate                      (searching off, auto-type off)
Entry  KPG revoked client 9b1c2d3e      UUID = first 16 bytes of SHA-256("KPG/1/revoked/9b1c…")
  User name, password, URL   (empty)
  Notes      KeePassGate data. Managed on the KPG tab in KeePass.
  KPG.Kind   revoked
  KPG.Data   {"v":"1","certificate":"9b1c…","notAfter":"2027-03-01T10:00:00Z","authority":"3f2a…"}
```

### 3.2 Tombstones: entries, with deletion records as a second witness

The task asks which of the two a tombstone should be. **An entry that is only ever added**, and
the measurement decides it:

- A deletion with its `DeletedObjects` record is carried to the other copy by KeePass's
  Synchronize and by none of its other four methods [M keepass X4; M here, set D]. No KeePassXC
  merge applies or copies one: the entries copy A had deleted with deletion records came back into
  A from the stale copy, and the stale copy received no record, in all four releases [M
  keepassxc; M here, set D, all three deleted entries]. KeePassDX applies the arriving records and
  does not keep them [R android 3.1]. Database Maintenance clears every record at once [R keepass
  5]. pykeepass deletes without writing one [R others].
- An entry only one copy holds arrived in the other copy in every merge: all five KeePassLib
  methods, all four KeePassXC releases, both directions [M keepass X1; M keepassxc; M here]. No
  merge measured removed an entry on its own; only a deletion record does, and only where the
  entry is not newer than the record [M keepass X3, X4].

So a revocation and a retirement are entries. The deletion records are still read, because they
are free evidence that points the safe way: **a deletion record that names a record's identifier
refuses that record**. For a refusing record that means it still counts after somebody deleted it
and emptied the bin. For a granting record it means a deleted record stays dead when a merge
brings its entry back. Measured for exactly that case: after `keepassxc-cli` merged a stale copy
into the copy that had revoked a client, the client's record was back in the store group, with
its revocation entry and its own deletion record beside it [M here, set D, four releases]; the
gate refuses on both.

### 3.3 The profile list as additions and removals

The list is the set of names with a live addition:

- **Adding** a name that is not on the list writes `profile <name>/<g>`, where `g` is one more than
  the highest generation any addition or removal of that name carries in the file, or 1.
- **Removing** a name writes `removed <name>/<g>` for every addition of it that counts.
- **A name is on the list** when some addition of it counts (in place, section 1 point 5) and no
  removal of the same name and generation is found anywhere.
- **Adding it back** writes the next generation. Removals are never pruned; additions that have a
  removal are.
- More than 32 names on the list reads as a list that will not read, which is an empty list, as
  today: every client stays admitted and reaches nothing.

What this buys, with no clock and no time in it: a name added in either copy survives a merge
(requirement A3, which today's single value fails); a name removed in either copy stays removed
when a stale copy brings its addition back, because the removal names that very addition
(requirement E5); the same addition made in two copies is one entry. Two copies that disagree on
purpose, one removing a name while the other removes it and adds it back, end with the name on the
list: that is the later decision of an operator who saw the removal, not a stale value.

### 3.4 Standing answers to renewals as numbered answers

An answer is never changed. A later answer about the same certificate is a new entry with the next
number. The answer in force is, among the answers with the highest number, the strictest
(`forever`, then the later `until`, then `allowed`). **Allow renewal** writes `allowed` with the
next number. So a lift travels through a merge, which it does not today [R inventory D5], a stale
`forever` loses to a later lift, and two copies answering at the same number end on the refusing
side. An answer never admits anybody. Answers go with the prune once no record admits the
certificate, as today.

## 4. Reading

`ClientCredentials.For`, `ClientCredentials.Again` and `OpenDatabaseTargets.TargetFor` read one
view of the store per request, on KeePass's thread as every read is today, and never inside a
merge (`MergeWatch`, unchanged). Nothing that grants is cached across requests.

**The pass.** One walk over every entry of the tree and every deletion record:

1. Find the store group by its UUID. None, more than one, or one inside the recycle bin: no
   granting record counts, so the database serves nothing until the operator acts.
2. Read the store group's direct entries by `KPG.Kind`. A granting entry counts when its content
   reads strictly, its UUID is the one its content derives (for an authority and a client that
   includes the digest of the certificate inside, which `AdmissionRecord` already demands) and no
   deletion record names it. Anything else in the group is damage: ignored and shown (section 8).
3. Collect the refusing identifiers the granting entries call for (for each authority its
   retirement, for each record its revocation, for each addition its removal) and look for them
   among all entries of the tree and all deletion records. An entry anywhere that carries
   `KPG.Kind` of a refusing kind and content naming a subject counts as well, whatever its UUID,
   so a copy that an import gave a new identifier still refuses.
4. Fold: the authorities that count and are not retired (exactly one is served, none serves
   nothing, more than one answers `503 undecided` as today); the record for the caller's
   certificate unless revoked; the list.

**Cost [M here, `out\walk.txt`].** On an invented database of 10,054 entries in 501 groups with
5,000 deletion records and a store of 53 entries, in a console process: 0.26 ms for the tree by
identifier, 0.08 ms for the deletion records, 1.6 ms more for testing every entry for the
`KPG.Kind` string (the string dictionary lookup is the cost), 1.9 ms in all; reading the store
group's 53 entries, 0.02 ms. That is about 0.19 microseconds an entry, so 0.1 ms for a database of
500 entries. If the 1.6 ms matters on a very large database, testing the icon before the string
brings the pass back to the identifier cost, at the price of making the icon mean something;
section 14 lists the measurement that would decide it.

**The authority's document** is parsed only when it changes, as today. The cache is keyed by the
entry's identifier and the `ProtectedString` object read; KeePass returned the same object on a
second read of an unchanged entry [M here], so an unchanged authority costs a reference
comparison and decodes nothing.

## 5. Writing

Every write is one of two operations on the store group, **add an entry** or **delete an entry**,
made by `DatabaseWriter` in its turn on KeePass's thread, saved through
`MainForm.SaveDatabase(db, null)` and believed on `Modified` being false and the file hashing to
`HashOfLastIO`, exactly as today. No entry's content is ever changed, so no write stamps a time on
anything that existed before it.

| Operation | Today | In this design |
| --- | --- | --- |
| Create an authority | `KPG.Authority.<fp>` written once | the store group if absent, then an `authority` entry |
| Recreate, Delete, Keep this authority | retirement key added; records and revocations removed | a `retired` entry added; the retired authority's entry, every record and every revocation deleted. The retirement is never deleted |
| Pairing, renewal | record written with any new profile name, saved and verified before the certificate is handed over | a `client` entry and, for a name the list lacks, a `profile` entry, in the same turn; saved and verified before the certificate is handed over |
| Revocation (clients page at OK, or `DELETE /gate/certificate` at once) | `KPG.Revoked.<key>` added, record emptied | a `revoked` entry added, the record's entry deleted |
| Standing answer, Allow renewal | key set, changed, removed | a `renewal` entry with the next number |
| Profiles page | list replaced as the operator's difference | `profile` and `removed` entries |
| Prune, riding on the plugin's own writes only | keys removed | entries deleted: expired records, revocations whose certificate has expired or whose authority is retired or gone, additions that have a removal, answers no record asks about, records that came back beside a revocation or a deletion record, authority entries beside their retirement |

**A deletion by the plugin is permanent and leaves a deletion record**, as KeePass's own permanent
delete does. Synchronize then carries the prune to the other copy [M keepass X4; M here: both
directions of set D ended with the pruned record gone and its deletion record present], so a pruned
record does not travel back and forth between copies. Where a merge does not carry it (KeePassXC,
KeePassDX, KeePass's other methods) the pruned entry comes back inert and the next prune takes it
again. **A write that did not land is taken back out** by removing the entries it added, with no
deletion record, since they were never in a file. **When the store group has to be made in a file
whose deletion records name the group's UUID**, because the operator deleted it and emptied the
bin, the same write removes that deletion record: kdbxweb removes whatever a deletion record
names, whatever the times [R others], while KeePass itself drops such a record for a group that
is not empty [M keepass X3].

**Which KeePass dialogs matter.** Nothing in KeePass copies a group's list of entries and puts it
back [R keepass 4, 9, for the dialogs read there; A for completeness], so no OK undoes an added
entry. Database Maintenance copies the database's custom data [R keepass 4], which holds nothing
of the plugin's any more, so the writer's refusal while it is open goes, and so does its
exception for owed answers. The entry dialog copies one entry and writes it back on OK [R keepass
4]: the plugin never changes an existing entry, so the only write that dialog could collide with
is a deletion. `GroupDialogs` already knows which entry each open entry dialog holds, so a deletion skips
an entry that is open in a dialog or under Edit Entries and the next prune takes it. A revocation
is never held up by that: its revocation entry is an addition, and the record it leaves behind for
the prune is refused from that moment. The refusals while KeePass has
blocked itself, while a message box stands, or while its progress window is open stay as they
are.

**The settings tab still waits for OK.** `PendingDatabase` keeps wrapping `IGateDatabase`, over a
buffer of additions and deletions instead of keys. Replaying it is replaying records: an addition
whose identifier is already there is nothing to do, and a deletion of an entry already gone is
nothing to do. The profile list needs no merge at OK any more, because what the tab buffered
already is the difference; the ceiling still leaves out an addition that no longer fits and names
it. One reconciler remains: a lift buffered on the tab is dropped when an answer about that
certificate arrived underneath the dialog, since that answer is the later word. Owed standing
answers still ride every save at `FileSavingPre`, as an entry.

## 6. What changes in the plugin

| Class | Change |
| --- | --- |
| `IKeyValueStore`, `CustomDataStore` | go. A seam of records replaces the seam of keys: every record, add one, delete one by identifier. `Bridge` gets the one class that knows the group, the strings, the derivation and the deletion records |
| `AuthorityKeys` | becomes the kinds, the subjects and the derivation; its remarks on why a key is only ever added move with it and widen to every kind |
| `AuthorityDocument`, `AdmissionRecord` | formats unchanged |
| `AuthorityStore` | same decisions (one document per authority, retirements decide, more than one unretired serves nothing) over records; `Write`, `Retire`, the prune rewritten |
| `AdmissionStore` | `Find` is a lookup by identifier in the view instead of a list of every key; `Remove` adds the revocation and deletes the record; `RemoveAll` and the prunes delete entries |
| `RenewalRefusal` | gains the number; the answer in force is the fold of section 3.4 |
| `ProfileNames` | reading is the fold of section 3.3; `Merge` goes |
| `PendingDatabase`, `PendingStore`, `PendingChanges` | buffer records instead of keys; `PendingStore.Reconcile` keeps only the renewal case; `PendingSessionRefusals` unchanged |
| `DatabaseWriter` | same lock, turn, save and verification. `WriteAdmission`, `RefuseRenewal`, the revocation and the owed answers add entries; the Database Maintenance refusal goes; a deletion asks `GroupDialogs` about the entry |
| `GroupDialogs` | unchanged for the vault and the grant editor. The store no longer needs the Database Maintenance watch; whether the watch itself goes depends on where the rules end up |
| `OpenDatabaseTargets` | reads the view; the authority cache keyed as in section 4 |
| `KdbxVault`, `VaultHandlers`, `DatabaseNodes` | the guard of section 9 |
| `Expected.OfCustomData` | becomes the filter list for the store's own failures |
| Grant editor, `NodeDialog`, `m_sdCustomData` | untouched by this option |
| Tests | `CustomDataMergeRuleTests` gives way to tests pinning what section 7 rests on, against the pinned KeePass: entries added in two copies both arrive under every method, one identifier is one entry, a deletion record does what section 3.2 describes |

## 7. What a save and a merge do to it

Sets measured here: **I**, each copy makes the store group and its entries on its own, two of
them the same decision made at different times; **T**, the same with the shared entry stamped in
the same second; **D**, copy A revokes a client the way this design does (adds the revocation,
deletes the record with a deletion record), deletes an older revocation by hand with a deletion
record and prunes a record, while the stale copy B pairs a client of its own. 59 results: 30
KeePassLib 2.62 merges, 24 `keepassxc-cli` merges, 4 KeePassXC saves, 1 chain. Every result is
KDBX 4.0, and every store entry in every result has the strings it was written with and the
protection flag where it was written [M here].

| Program | A save | A merge | What the gate then serves |
| --- | --- | --- | --- |
| KeePass 2.62, Synchronize | keeps everything [M keepass X7] | sets I and T: one group, the union of both copies' entries, both directions [M here]. Set D: the record, the pruned record and the hand-deleted revocation gone in both copies with their three deletion records present, the new revocation present, the stale copy's own client present [M here] | the union; the revoked client refused by its revocation; the client whose revocation was deleted by hand still refused, by the deletion record |
| KeePass 2.62, Overwrite if newer, Overwrite existing, Keep existing | | sets I and T as Synchronize [M here]. Set D into the copy that acted: as Synchronize, since a database applies its own deletion records under every method [M keepass X4; M here]. Set D into the stale copy: it keeps the entries the other copy deleted, receives no deletion record and receives the revocation entry [M here] | the revoked client's record is there and refused by the revocation entry |
| KeePass 2.62, Create new IDs (File then Import preselects it [R keepass 2.4]) | | a second group named `KeePassGate` under a new UUID holding the arriving entries under new UUIDs, beside the untouched store [M here; M keepass X1] | the arriving granting entries count for nothing (wrong group, wrong identifier); arriving refusing entries count by their content; the open database's own records are as they were |
| The same decision made in both copies | | at different times: one entry, with one history item holding the same content [M here, set I, KeePassLib and KeePassXC]. In the same second: one entry, no history [M here, set T] | one record; the history item is never read |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | keeps every string, protection flag, both group flags; adds nothing to a store entry; file stays 4.0; it adds keys of its own to the database's custom data (`_LAST_MODIFIED` and `KPXC_RANDOM_SLUG` in 2.8.0), which holds nothing of ours [M here; M keepassxc 6] | sets I and T: one group, the union, both directions, all four [M here]. Set D into the copy that acted: the three deleted entries come back and that copy's three deletion records stay [M here; M keepassxc]. Set D into the stale copy: it gains the revocation entry and no deletion record [M here]. From 2.4.2 it replaces the database's custom data whole [M spike], which no longer holds anything of ours | the revoked client refused in both results by the revocation entry; in the copy that acted also by the deletion record |
| KeePassXC merge, then KeePass Synchronize with the stale copy | | the entries KeePassXC brought back are deleted again and the result equals a plain Synchronize [M here, chain; M keepassxc 9] | as Synchronize |
| Keepass2Android 1.15-r6, 1.08d-r5 | keeps entries, custom strings and a custom string's flag [R android 2; entries as wholes M spike] | an entry the open copy lacks is added to the group of the same identifier, or the root if that group is missing; the two copies' deletion records are joined; a group in both is the same group [R android 1.5, 3.1, 3.2]. An entry only the open copy held stayed in 18 of 18 runs [M spike]. Not measured: an entry only the arriving copy holds, the group made in both copies | by the reads: the union. An arriving entry that landed at the root because its group was missing does not count if it grants and counts if it refuses |
| KeePassDX 4.5.8 | keeps entries, custom strings and flags; writes 4.1 [R android 2; M spike] | an arriving entry is added and a missing group is recreated; arriving deletion records are applied and not kept [R android 1.5, 3.1]. Not measured | by the reads: the union; deletions it applied can be undone by a later merge with a stale copy, where the revocation entry still refuses |
| KeePassDX 3.2.0 | as 4.5.8 for entries and strings [R android 2]; it deletes group custom data [M spike], which the store does not use | an arriving entry is added only if its group exists in the open copy; groups are merged before their entries [R android 1.5]. Not measured | by the reads: the union. If an arriving refusing entry were dropped, that copy would not learn the revocation; this is the first thing to measure |
| KeePassDroid 2.7 | keeps entries, custom strings and flags [R android 2; entries as wholes M spike]; it deletes all node custom data [M spike], which the store does not use. It has no merge [M spike] | none | unchanged |
| kdbxweb 2.1.1 (KeeWeb) | keeps strings, flags and history [M others]. Its unreadable 4.1 output came from timed custom data [M others], which this store does not write | two entries added in two copies both arrive [M others]; it removes whatever a deletion record names, whatever the times [R others] | the union; its stricter deletions land on the refusing side |
| Strongbox, KeePassKit, keepass-rs | carry strings in their models [R others] | add a node the open copy lacks [R others] | by the reads: the union |
| An overwrite: Keepass2Android "No, overwrite", KeePassDX within 10 seconds, every KeePassDroid save over a changed file, a file synchronizer's conflict copy | | nothing merges; one file wins [R android 5.5; M spike for KeePassDX; R others] | **not solved**, section 11 |

History limits, history trimming, equal times, rewritten times and the root group's time, which
decide so much in the ground reports, do not appear in this table because the store never puts
two different versions of one entry into the world.

## 8. What a person sees, what they can break, and what the gate does

**What they see.** In every program: a group `KeePassGate` at the root with a handful of entries
titled `KPG …`, each with an empty user name and password, one line of notes and two extra
fields. In KeePass the group carries the plugin's icon and the entries stay out of search,
auto-type, the password reports and the expiry warning [M keepass X11; R keepass 9]; the large
entries and last modified reports list them [R keepass 9]. In KeePassXC the two fields are under
the entry's additional attributes, the authority's hidden until revealed [A]; search skips the
group [R keepassxc 8]. In Keepass2Android and KeePassDX the fields show as extra strings and the
group is skipped by search, autofill and the keyboard [R android 4.2]. KeePassDroid lists them
[A].

**What they can break, and the answer.** The KPG tab shows each kind of damage on the page it
concerns and offers the one remedy; nothing is repaired without a press, and nothing at open.

| By hand, in any program | What the gate does |
| --- | --- |
| Edits a title, the notes, a user name, an icon | nothing; none is read |
| Edits or removes `KPG.Data` of a client's record | a record that will not read, or whose certificate no longer matches its identifier, admits nobody and is shown as damaged. A record edited into another valid record takes effect, as a record rewritten in custom data does today [R inventory P23]; it takes the master key |
| Edits `KPG.Data` of an authority | an authority that will not read serves nothing, as today |
| Edits or removes `KPG.Data` or `KPG.Kind` of a revocation, a retirement or a removal | still counts: the identifier alone decides |
| Deletes a client's record (to the bin, or for good) | the client is not admitted. Out of the bin it is admitted again unless revoked; after a permanent delete the deletion record keeps it out even if a merge brings the entry back |
| Deletes a revocation, a retirement or a removal | still counts in the bin; still counts after the bin is emptied, by the deletion record |
| Then runs Database Maintenance's removal of deleted objects information | the last trace in that file is gone. The record was deleted at the revocation, so that file still admits nobody; a stale copy merged in later can bring the record back with nothing left to refuse it. Three deliberate acts and a stale copy; section 11 |
| Moves a granting entry out of the group | it stops counting |
| Moves a refusing entry anywhere | still counts |
| Moves or renames the group | nothing |
| Deletes the group | the database serves nothing; the KPG tab shows where the group is and offers to put it back while it is in the bin. Emptied, the authority and the records are gone for good and stay refused by their deletion records; the operator makes a new authority and pairs again |
| Duplicates an entry or the group | the copy has another identifier: it grants nothing, and refuses if it is a refusing record |
| Imports with Create new IDs | a duplicate group that counts for nothing; the tab offers to delete it. Close without saving is the better remedy, because KeePass's Delete Duplicate Entries deletes one of two entries with equal strings, the older by preference [M keepass X10], which can be the original and then leaves the database serving nothing |
| Restores a history item | the same content |
| Turns the group's searching or auto-type back on | the entries show up in search; nothing else. Put back at the plugin's next write |
| Points the templates group at it, or references a store entry from a field | KeePass offers store entries as templates, and a search reference can resolve to one [M keepass X11]; copies made that way count for nothing |
| Exports | section 11 |

## 9. Keeping the API and the reference client out

Today no route touches custom data, so the store is out of reach by construction [R inventory
P15]. A store made of entries sits where the API works, so the guard is explicit, in the vault
and not in any rule:

1. **The store group and everything in it is absent**, exactly as the recycle bin is
   (`KdbxVault.Discarded`, `DESIGN.md` §138): not listed, not counted, not tagged, not addressable,
   the same `404` a refused node gets. The test joins `Discarded` in `FindGroup`, `FindEntry` and
   every listing, the flat sweep and the Profiles page's counts included.
2. **Any entry anywhere that carries a string whose name begins `KPG.` is absent the same way.**
   That covers a record somebody moved or copied into a user's group, the authority's key above
   all. The test runs on the entries a request is about to serve, not on the whole tree.
3. **No route makes a string whose name begins `KPG.`**, compared without regard to ASCII case.
   `createField` is the one route that makes a name, and it refuses this one with the
   `400 malformed` it already gives a name its own routes could not name again; the other field
   routes name a string an entry already holds, and an entry holding one of these is absent by
   point 2. Without this a client holding `create` somewhere could turn an entry there into a
   `revoked` or `retired` record and shut another client or the whole gate out. It could never
   make a granting one, since those count only inside the store group and under an identifier
   the API does not let a caller choose.
4. **A group that holds the store group cannot be removed**, with the conflict a group holding
   the bin gets today.
5. The routing document gains nothing: no route, no schema. `API.md` gains the reserved prefix
   and the absent group.

The reference client changes nothing. `pykeepassgate.api` has no method that could reach the
store, `pykeepassgate.refs` follows references by identifier under the caller's rules and meets
the same `404`, and the stand-in gate models no rules. A client that sends a reserved field name
gets the gate's refusal like any other.

## 10. KDBX version

**4.0, and nothing in the store needs more.** Strings, protected strings, the two group flags,
notes and deletion records are all written at 4.0 by KeePass 2.62 [M keepass X7], every file made
and every result measured here is 4.0 [M here], and KeePassXC 2.4.2 and 2.6.6, which only write
4.0, kept all of it [M here]. The database is no longer taken to 4.1 by the plugin, and the file
is no longer one that KeeWeb makes unreadable to KeePass [M others]. Keepass2Android writes KDBX
3.1 for a file with AES-KDF and no node custom data [M spike]; strings, their protection and the
group flags exist in 3.1 [A], so the store should pass through, which is on the list to measure.

## 11. What it does not solve

- **A file written over.** Keepass2Android's "No, overwrite", a KeePassDX save within ten seconds
  of the other change, any KeePassDroid save over a changed file and a file synchronizer's
  conflict copy keep one file whole [R android 5.5; M spike; R others]. If the file that lost
  held a revocation and KeePass no longer has that database open, the revocation is gone and the
  stale record admits again. No store inside the file survives that. While KeePass still has the
  database open, its next save meets the changed file and offers Synchronize [R keepass 9], which
  puts the union back.
- **A stale copy on its own** still admits whoever it admitted.
- **A revocation deleted by hand, the bin emptied, the deletion records cleared and a stale copy
  merged in**, in that order.
- **Deletions do not travel through KeePassXC or KeePassDX merges** [M here; R android 3.1]. So a
  deleted authority's key material and pruned records come back from a stale copy. They are inert,
  refused by the retirement or revocation beside them, and the next prune removes them, but
  "Delete authority" does not remove the key from a file another program later merges a stale
  copy into. Today's store has the same limit for keys a merge adds back.
- **A hostile file merged in by the operator** with a method that matches identifiers can bring a
  retirement of the open database's authority and an authority of its own. The identifiers are
  derivable by anybody who knows the authority's digest. Today's keys arrive the same way [A, from
  the merge rule M spike]; under Create new IDs this design is the stricter one, since arriving
  granting entries count for nothing.
- **The store is in plain sight.** Every program shows the group, and the authority's private key
  is one reveal or one copy to the clipboard away in each of them, where today it sits in custom
  data most programs do not show. KeePassXC's HTML export prints every custom string, protected
  ones in clear [M keepassxc 8], and KeePass's print can include custom strings [R keepass 9];
  today only the XML export carries the key [R inventory P26]. Whoever holds the master key holds
  everything either way, which is the concession the project already makes, but the ways to leak
  it by accident are more.
- **The Android merges are read, not measured**, for this shape (section 14).
- **Access rules.** Not this option's subject.

## 12. What it composes with, reverses and keeps

**Rule placement.** This design composes best with the option that keeps each rule as an entry of
the plugin's own, naming its node by UUID, in the same store: one reader, one pass, one API
guard, and the same fold (a rule's versions as records, a rule's removal as a record, and a
deletion record that refuses, which is what makes the loss of a rule visible). It needs nothing
from any other placement and takes nothing away from one: with rules left on their nodes, the
grant editor, `m_sdCustomData` and `GroupDialogs` stay exactly as they are. The recycle bin
markers of the open T049 design fit as one more kind of record, added once and never changed.

**Reverses, each to be recorded as a reversal:**

- "Every database is its own certificate authority, stored inside itself under `KPG` keys in the
  database's own `CustomData`": the place. The principle stays.
- "The database's own `CustomData`, never the root group's": the whole bullet. Its finding, that
  what has to survive a merge is something only ever added, stays and now covers every kind
  including the list and the answers. `KPG.Revoked.<key>` beside an emptied record becomes a
  revocation entry and a deleted record; `KPG.Authority.<fp>` and `KPG.RetiredAuthority.<fp>`
  become entries; `503 undecided` stays.
- "The authority takes the file to KDBX 4.1": gone.
- "Replayed key by key" and "`KPG.Profiles` is replayed as a difference": replayed record by
  record; the stored form is the difference and `ProfileNames.Merge` goes. The standing answers
  keep a reconciler.
- The trap section's `DatabaseOperationsForm` paragraph stops being about the store; the entry
  dialog takes its place, for deletions only.
- `CA.md` §204 and §208: the merge argument and the erasure account are rewritten (the stored
  PKCS#12 is a protected string now).
- The measured fact about a KeePass merge of `PwDatabase.CustomData` stays true and stops being
  load bearing.

**Keeps:** no host-level authority or registry; the admission record is load bearing and its
profiles come from the record less what the list lacks; no `kpg://any`; no passphrase on the
authority key; pairing tokens and held approvals in memory only; opening or reading changes
nothing and prunes ride only on the plugin's own writes; everything on the KPG settings tab waits
for OK, revocation included; one writer, saved and verified on `Modified` and the file's hash,
before any certificate is handed over; live reads on every request; the JSON grammar and the one
escaper; refused and absent answer the same `404`; spec-first routing; the profile list decides
what a client holds; the grant editor.

## 13. A migration, in one paragraph

None is designed. If 0.1.0 databases are to be carried over, it would be a press on the KPG tab,
never an act at open: read the six key families, write the entries, save and verify, and only
then remove the keys. The refusing keys would have to keep counting for as long as a stale copy
could bring an old record back, which means reading `KPG.Revoked.*` and `KPG.RetiredAuthority.*`
from custom data as refusals indefinitely, and that is exactly the legacy code the project's rule
against carrying an older stored shape forbids. The alternative within that rule is that a
database made with 0.1.0 reads as having no authority, and the operator creates one and pairs
again.

## 14. Measurements to make before this design could be chosen

Most important first. Items 1 and 2 need the emulator and wait for the desktop.

1. **The Android merges over sets I, T and D** (the files exist under `work\base`): Keepass2Android
   1.15-r6 and KeePassDX 4.5.8 and 3.2.0, both directions. Does an entry only the arriving copy
   holds arrive; is the group made in both copies one group; does KeePassDX 3.2.0 drop an arriving
   entry; what happens to the deletion records. The design's claim for Android rests on reads
   until this runs.
2. **A save by KeePassDroid 2.7, KeePassDX 3.2.0, 3.3.0 and 4.5.8 and Keepass2Android (its KDBX
   3.1 output included)** of a file holding the store: the two custom strings, the protected flag
   on a 2,903 character value, the group's UUID and flags.
3. **KeePass's own window**: a real File then Synchronize and File then Import over sets I and D
   with the plugin reading in between; the entry dialog open on a store entry while the plugin
   deletes it; Delete Duplicate Entries after an import with new IDs; Empty Recycle Bin with the
   store group in it; Database Maintenance's removal of deleted objects information; that no
   dialog puts back a copy of a group's entry list.
4. **The pass on KeePass's thread** for databases of 1,000, 10,000 and 50,000 entries, and whether
   the icon test before the string test is needed.
5. **What each program shows and offers**: that search, autofill, auto-type and reports leave the
   store alone in KeePassXC, Keepass2Android and KeePassDX (read so far), what revealing and
   copying the authority's field looks like, and KeePassXC's HTML export of it.
6. **kdbxweb and KeeWeb** over sets I and D, for its rule that a deletion record removes whatever
   it names; **Strongbox, KeePassKit, KeePassium** saves and merges, which need another platform.
7. **KeePassXC's window merge, its reload of a changed file and a KeeShare import** over set D;
   they use the same merger [R keepassxc 3], and KeeShare applies deletions.
8. **A hostile file** carrying a retirement of the open database's authority, imported under each
   method, in this design and in today's.
9. **Every route against the store group and against a marked entry in a user's group**, and the
   reserved field name, as host and end to end tests.

### What was run for this report

A console probe over KeePassLib 2.62 loaded from `.keepass\2.62\KeePass.exe`
made the three sets with invented content (Argon2d, 2 iterations, 1 MiB), merged each pair with
`PwDatabase.MergeIn` under all five methods in both directions, and read every result back after
a save. `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, the copies the KeePassXC ground report
downloaded and held to the spike's pinned hashes, each with a valid Authenticode signature
checked again here, ran `merge -s` over the same pairs in both directions and `add` as a plain
save. No window was opened and no test tier was run. The timing ran in the same console probe on
an in-memory database.
